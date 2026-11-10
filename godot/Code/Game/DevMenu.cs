using Godot;

/// <summary>
/// Port of the s&box DevMenu (opened from the pause menu): tabs (Enemies, Player, Look, World) and a search box that pulls matching controls
/// from every tab. Chips toggle dev flags and run actions: wave spawning, spawning or placing enemies, allies,
/// enemy debug, the MC61 moves, cheats, skins, water feel, noclip, sun, camera tools, waves, time scale, travel and
/// a stats overlay. Debug builds only. The stylize looks and creature skin shaders come with step 7c.
/// </summary>
public partial class DevMenu : CanvasLayer
{
	bool open;
	public bool Open => open;
	string search = "";
	string tab = "Enemies";
	int sectionsShown;
	static readonly string[] Tabs = { "Enemies", "Player", "Look", "World" };

	Control root, window;
	LineEdit searchEntry;
	HBoxContainer tabRow;
	VBoxContainer body;
	Label stats, hintText;
	int lastHash;
	TimeSince rebuilt;
	string shownState = "";

	/// <summary> Everything the chips show that can change by itself (flags, time scale, skin, waves, look). </summary>
	static string StateKey()
	{
		var sb = new System.Text.StringBuilder();
		foreach ( var f in typeof( DevFlags ).GetFields( System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static ) )
			if ( f.FieldType == typeof( bool ) || f.FieldType == typeof( float ) || f.FieldType == typeof( int ) ) sb.Append( f.GetValue( null ) ).Append( '|' );
		sb.Append( Engine.TimeScale ).Append( '|' ).Append( CharacterSkins.Current ).Append( '|' ).Append( RifleModels.Current ).Append( '|' );
		if ( WaveDirector.Current is { } d ) sb.Append( d.Wave ).Append( d.State ).Append( d.SpawningPaused ).Append( '|' );
		sb.Append( GlobalStylize.Current?.Style ).Append( '|' ).Append( Level.Current?.LevelName );
		return sb.ToString();
	}
	Vector2 freeLook;
	(Vector3 Position, Angles Angles)? savedSpot;
	WaveDirector.EnemyKind placeKind;
	bool noclipApplied, hudHidden;
	MeshInstance3D placeMarker;
	string sunPreset = "Default";
	Basis? sunBasis;
	Color sunColor;

	const float NoclipSpeed = 450f;
	const float FreeCamSpeed = 350f;

	static WaveDirector Director => WaveDirector.Current;
	static PlayerSurvivor Player => PlayerSurvivor.All.FirstOrDefault();
	static PlayerController Controller => Player?.Controller;

	public override void _Ready()
	{
		Name = "DevMenu";
		Layer = 50;
		ProcessMode = ProcessModeEnum.Always;
		DevFlags.Reset();
		Engine.TimeScale = 1f;

		root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		root.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		AddChild( root );

		stats = new Label();
		stats.AddThemeFontSizeOverride( "font_size", 16 );
		stats.AddThemeColorOverride( "font_outline_color", Colors.Black );
		stats.AddThemeConstantOverride( "outline_size", 4 );
		stats.Position = new Vector2( 12, 8 );
		root.AddChild( stats );

		hintText = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		hintText.AddThemeFontSizeOverride( "font_size", 18 );
		hintText.AddThemeColorOverride( "font_outline_color", Colors.Black );
		hintText.AddThemeConstantOverride( "outline_size", 5 );
		hintText.SetAnchorsPreset( Control.LayoutPreset.CenterBottom );
		hintText.OffsetTop = hintText.OffsetBottom = -150; hintText.GrowHorizontal = Control.GrowDirection.Both;
		root.AddChild( hintText );

		window = new Control { Visible = false };
		window.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		root.AddChild( window );
		var backdrop = new ColorRect { Color = new Color( 0, 0, 0, 0.45f ) };
		backdrop.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		window.AddChild( backdrop );

		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride( "panel", new StyleBoxFlat { BgColor = new Color( "10141c" ) with { A = 0.96f }, BorderColor = new Color( "5fffe0" ) with { A = 0.5f }, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10, CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 16, ContentMarginBottom = 16 } );
		panel.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		panel.AnchorLeft = 0.2f; panel.AnchorRight = 0.8f; panel.AnchorTop = 0.08f; panel.AnchorBottom = 0.92f;
		window.AddChild( panel );
		var column = new VBoxContainer();
		column.AddThemeConstantOverride( "separation", 12 );
		panel.AddChild( column );

		var header = new HBoxContainer();
		header.AddThemeConstantOverride( "separation", 16 );
		column.AddChild( header );
		var title = new Label { Text = "DEV MENU" };
		title.AddThemeFontSizeOverride( "font_size", 26 );
		title.AddThemeColorOverride( "font_color", new Color( "5fffe0" ) );
		header.AddChild( title );
		searchEntry = new LineEdit { PlaceholderText = "Search... (god, boss, skin, time)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true };
		searchEntry.AddThemeFontSizeOverride( "font_size", 18 );
		searchEntry.TextChanged += t => { search = t; Rebuild(); };
		header.AddChild( searchEntry );
		var hintLabel = new Label { Text = "Esc to close" };
		hintLabel.AddThemeColorOverride( "font_color", new Color( 1, 1, 1, 0.5f ) );
		header.AddChild( hintLabel );
		header.AddChild( Button( "X", false, () => SetOpen( false ) ) );

		tabRow = new HBoxContainer();
		tabRow.AddThemeConstantOverride( "separation", 6 );
		column.AddChild( tabRow );

		var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		column.AddChild( scroll );
		body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		body.AddThemeConstantOverride( "separation", 14 );
		scroll.AddChild( body );
	}

	/// <summary>
	/// Debug builds always have it; release builds too while playtesting (project setting game/dev_menu_in_builds,
	/// turn off to ship). It is opened from the pause menu (Esc), which only shows its button when this is true.
	/// </summary>
	public static bool Allowed => OS.IsDebugBuild() || (bool)ProjectSettings.GetSetting( "game/dev_menu_in_builds", false );

	public override void _ExitTree()
	{
		IsOpen = false;
		DevFlags.Reset();
		Engine.TimeScale = 1f;
		if ( placeMarker is not null && IsInstanceValid( placeMarker ) ) placeMarker.QueueFree();
	}

	public override void _UnhandledInput( InputEvent e )
	{
		if ( e is InputEventMouseMotion motion && DevFlags.FreeCam && !open && Input.MouseMode == Input.MouseModeEnum.Captured ) freeLook += motion.Relative;
		if ( DevFlags.Placing && !open && e is InputEventMouseButton { Pressed: true } mb )
		{
			if ( mb.ButtonIndex == MouseButton.Right ) DevFlags.Placing = false;
			else if ( mb.ButtonIndex == MouseButton.Left ) PlaceAtCrosshair();
		}
	}

	public override void _Process( double delta )
	{
		if ( !Allowed ) return;
		var dt = (float)(delta / MathF.Max( 0.0001f, (float)Engine.TimeScale ));
		UpdateNoclip( dt );
		UpdateFreeCam( dt );
		UpdatePlacing();
		UpdateHideHud();
		GetTree().DebugCollisionsHint = DevFlags.ShowHitboxes;

		stats.Visible = DevFlags.ShowStats;
		if ( DevFlags.ShowStats )
		{
			var p = Controller?.GlobalPosition ?? Vector3.Zero;
			var s = new Vector3( -p.Z, -p.X, p.Y ) / U.In;   // s&box units, x forward, z up
			stats.Text = $"{Engine.GetFramesPerSecond()} fps ({1000.0 / Math.Max( 1, Engine.GetFramesPerSecond() ):0.0} ms)   enemies {CreatureRegistry.Alive().Count()}   time {Engine.TimeScale}x   {s.X:0} {s.Y:0} {s.Z:0}";
		}
		hintText.Text = !open && DevFlags.Placing ? $"Placing {placeKind}: left click to place, right click to stop"
			: !open && DevFlags.FreeCam && !DevFlags.HideHud ? "Free camera: WASD, Space up, Ctrl down, Shift fast (turn it off in the menu)" : "";

		// While open, redraw only when something it shows changed (checked four times a second). Rebuilding
		// every 0.1 s recreated the buttons under the mouse: hover flickered and clicks were lost.
		if ( open && rebuilt > 0.25f && !Input.IsMouseButtonPressed( MouseButton.Left ) )
		{
			rebuilt = 0f;
			var now = StateKey();
			if ( now != shownState ) Rebuild();
		}
	}

	/// <summary> Testing aid: open on a tab, or with a search typed in. </summary>
	public void TestShow( string tabName, string searchText = "" )
	{
		SetOpen( true );
		tab = tabName;
		search = searchText;
		searchEntry.Text = searchText;
		Rebuild();
	}

	/// <summary> Testing aid: press the first chip whose text starts with this. </summary>
	public bool TestPress( string text )
	{
		var button = body.FindChildren( "*", "Button", true, false ).OfType<Button>().FirstOrDefault( b => b.Text.StartsWith( text ) );
		button?.EmitSignal( BaseButton.SignalName.Pressed );
		return button is not null;
	}

	/// <summary> True while the menu is up: clicks belong to it, not the gun. </summary>
	public static bool IsOpen { get; private set; }

	public void SetOpen( bool value )
	{
		if ( open == value ) return;
		open = value;
		IsOpen = value;
		window.Visible = open;
		Input.MouseMode = open ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
		var controller = Controller;
		if ( controller is not null )
		{
			if ( open ) { controller.UseInputControls = false; controller.UseLookControls = false; }
			else if ( Player.IsAlive )
			{
				controller.UseInputControls = !DevFlags.Noclip && !DevFlags.FreeCam;
				controller.UseLookControls = !DevFlags.FreeCam;
			}
		}
		if ( open ) Rebuild();
		else searchEntry.ReleaseFocus();
	}

	// ---- building the chips ----

	bool Searching => !string.IsNullOrWhiteSpace( search );
	bool Has( string text ) => text.Contains( search.Trim(), StringComparison.OrdinalIgnoreCase );
	bool Sec( string tabName, string section, string keywords )
	{
		var show = Searching ? Has( section ) || Has( keywords ) : tab == tabName;
		if ( show ) sectionsShown++;
		return show;
	}
	bool Show( string section, string keywords ) => !Searching || Has( section ) || Has( keywords );

	static Button Button( string text, bool? on, Action click )
	{
		var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, ActionMode = BaseButton.ActionModeEnum.Press };
		b.Pressed += () => Sfx.Play2D( "ui_click", 0.5f );
		b.AddThemeFontSizeOverride( "font_size", 16 );
		var bg = on switch { true => new Color( "1f8f7a" ), false => new Color( "2a2f3a" ), null => new Color( "343b4a" ) };
		foreach ( var state in new[] { "normal", "hover", "pressed" } )
			b.AddThemeStyleboxOverride( state, new StyleBoxFlat { BgColor = state == "hover" ? bg.Lightened( 0.15f ) : bg, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6 } );
		b.AddThemeColorOverride( "font_color", on == false ? new Color( 1, 1, 1, 0.6f ) : Colors.White );
		b.Pressed += () => click();
		return b;
	}

	VBoxContainer section;
	HFlowContainer row;

	void Section( string heading )
	{
		section = new VBoxContainer();
		section.AddThemeConstantOverride( "separation", 6 );
		body.AddChild( section );
		var label = new Label { Text = heading.ToUpper() };
		label.AddThemeFontSizeOverride( "font_size", 15 );
		label.AddThemeColorOverride( "font_color", new Color( "5fffe0" ) );
		section.AddChild( label );
		row = null;
	}

	void Row( string label = null )
	{
		row = new HFlowContainer();
		row.AddThemeConstantOverride( "h_separation", 6 );
		row.AddThemeConstantOverride( "v_separation", 6 );
		section.AddChild( row );
		if ( label is not null )
		{
			var l = new Label { Text = label, CustomMinimumSize = new Vector2( 130, 0 ), VerticalAlignment = VerticalAlignment.Center };
			l.AddThemeColorOverride( "font_color", new Color( 1, 1, 1, 0.7f ) );
			row.AddChild( l );
		}
	}

	void Chip( string text, bool on, Action click ) { if ( row is null ) Row(); row.AddChild( Button( text, on, () => { click(); Rebuild(); } ) ); }
	void Action( string text, Action click ) { if ( row is null ) Row(); row.AddChild( Button( text, null, () => { click(); Rebuild(); } ) ); }
	void Pick( string text, bool on, Action click ) { if ( row is null ) Row(); row.AddChild( Button( text, on ? true : null, () => { click(); Rebuild(); } ) ); }
	void Info( string text )
	{
		var l = new Label { Text = text };
		l.AddThemeColorOverride( "font_color", new Color( 1, 1, 1, 0.75f ) );
		section.AddChild( l );
	}

	/// <summary> How many times the chips were rebuilt (tests check it stays still while hovering). </summary>
	public int Rebuilds { get; private set; }

	/// <summary> Testing aid: the first chip whose text starts with this. </summary>
	public Button FindChip( string text ) => body.FindChildren( "*", "Button", true, false ).OfType<Button>().FirstOrDefault( b => b.Text.StartsWith( text ) );

	void Rebuild()
	{
		Rebuilds++;
		shownState = StateKey();
		rebuilt = 0f;
		foreach ( var c in tabRow.GetChildren() ) c.QueueFree();
		foreach ( var t in Tabs )
		{
			var name = t;
			tabRow.AddChild( Button( t, tab == t && !Searching ? true : false, () => { tab = name; search = ""; searchEntry.Text = ""; Rebuild(); } ) );
		}
		if ( Searching ) tabRow.AddChild( new Label { Text = "Showing matches from every tab", Modulate = new Color( 1, 1, 1, 0.6f ) } );

		foreach ( var c in body.GetChildren() ) { body.RemoveChild( c ); c.QueueFree(); }
		sectionsShown = 0;
		var d = Director;

		// ======== ENEMIES
		if ( Sec( "Enemies", "Wave spawning", "spawning pause zombies bloobers squiliens junk crabs sea spiders boss waves kill all enemies skip wave" ) )
		{
			Section( "Wave spawning" );
			Row();
			if ( Show( "Wave spawning", "spawning pause" ) ) Chip( $"Spawning: {(Paused ? "PAUSED" : "ON")}", !Paused, () => { if ( d is not null ) d.SpawningPaused = !d.SpawningPaused; } );
			if ( Show( "Wave spawning", "zombies" ) ) Chip( "Zombies", d is null || d.SpawnZombies, () => { if ( d is not null ) d.SpawnZombies = !d.SpawnZombies; } );
			if ( Show( "Wave spawning", "bloobers" ) ) Chip( "Bloobers", d is null || d.SpawnBloobers, () => { if ( d is not null ) d.SpawnBloobers = !d.SpawnBloobers; } );
			if ( Show( "Wave spawning", "squiliens" ) ) Chip( "Squiliens", d is null || d.SpawnSquiliens, () => { if ( d is not null ) d.SpawnSquiliens = !d.SpawnSquiliens; } );
			if ( Show( "Wave spawning", "junk crabs" ) ) Chip( "Junk Crabs", d is null || d.SpawnCrabs, () => { if ( d is not null ) d.SpawnCrabs = !d.SpawnCrabs; } );
			if ( Show( "Wave spawning", "sea spiders" ) ) Chip( "Sea Spiders", d is null || d.SpawnSpiders, () => { if ( d is not null ) d.SpawnSpiders = !d.SpawnSpiders; } );
			if ( Show( "Wave spawning", "boss waves mc61" ) ) Chip( "Boss waves", d is null || d.SpawnBossWaves, () => { if ( d is not null ) d.SpawnBossWaves = !d.SpawnBossWaves; } );
			Row();
			if ( Show( "Wave spawning", "kill all enemies clear" ) ) Action( "Kill all enemies", () => d?.ClearEnemies() );
			if ( Show( "Wave spawning", "skip wave" ) ) Action( "Skip wave", () => d?.SkipWave() );
		}

		if ( Sec( "Enemies", "Spawn an enemy", string.Join( " ", EnemyKinds.Select( k => k.Name ) ) + " spawn place crosshair" ) )
		{
			Section( "Spawn an enemy" );
			foreach ( var k in EnemyKinds )
			{
				if ( !Show( "Spawn an enemy", k.Name + " spawn place crosshair" ) ) continue;
				Row( k.Name );
				var kind = k.Kind;
				Action( "Spawn", () => d?.Spawn( kind ) );
				Action( "Place at crosshair", () => StartPlacing( kind ) );
			}
		}

		if ( Sec( "Enemies", "Allies", "ally spider spawn despawn allies friendly" ) )
		{
			Section( "Allies" );
			Action( "Spawn ally spider", SpawnAlly );
			Action( "Despawn allies", DespawnAllies );
		}

		if ( Sec( "Enemies", "Enemy debug", "freeze enemies one-shot kills show hitboxes" ) )
		{
			Section( "Enemy debug" );
			if ( Show( "Enemy debug", "freeze enemies" ) ) Chip( "Freeze enemies", DevFlags.FreezeEnemies, () => DevFlags.FreezeEnemies = !DevFlags.FreezeEnemies );
			if ( Show( "Enemy debug", "one-shot kills" ) ) Chip( "One-shot kills", DevFlags.OneShot, () => DevFlags.OneShot = !DevFlags.OneShot );
			if ( Show( "Enemy debug", "show hitboxes" ) ) Chip( "Show hitboxes", DevFlags.ShowHitboxes, () => DevFlags.ShowHitboxes = !DevFlags.ShowHitboxes );
		}

		if ( Sec( "Enemies", "MC61 boss", "boss mc61 float charge beam cards dance 10% hp teleport" ) )
		{
			Section( "MC61 boss" );
			Row( "Force move" );
			foreach ( var mode in Enum.GetValues<MC61.Mode>() )
			{
				var m = mode;
				Action( m.ToString(), () => { foreach ( var b in MC61.All ) if ( !b.IsDead ) b.ForceState( m ); } );
			}
			Row();
			Action( "Boss to 10% HP", () => { foreach ( var b in MC61.All ) if ( !b.IsDead && b.Health is { } h ) h.Current = h.MaxHealth * 0.1f; } );
			Action( "Teleport to MC61", TeleportToBoss );
		}

		// ======== PLAYER
		var player = Player;
		var weapons = Controller?.GetChildren().OfType<PlayerWeapon>().ToList() ?? new();
		if ( Sec( "Player", "Cheats", "god mode infinite ammo heal refill ammo points money +1000" ) )
		{
			Section( "Cheats" );
			var god = player?.Health?.Invulnerable ?? false;
			var infinite = weapons.Any( w => w.InfiniteAmmo );
			if ( Show( "Cheats", "god mode invulnerable" ) ) Chip( "God mode", god, () => { if ( player?.Health is { } h ) h.Invulnerable = !h.Invulnerable; } );
			if ( Show( "Cheats", "infinite ammo" ) ) Chip( "Infinite ammo", infinite, () => { foreach ( var w in weapons ) w.InfiniteAmmo = !infinite; } );
			if ( Show( "Cheats", "heal health" ) ) Action( "Heal", () => { if ( player?.Health is { } h ) h.Heal( h.MaxHealth ); } );
			if ( Show( "Cheats", "refill ammo" ) ) Action( "Refill ammo", () => { foreach ( var w in weapons ) w.RefillAmmo(); } );
			if ( Show( "Cheats", "+1000 points money" ) ) Action( "+1000 points", () => player?.AddPoints( 1000 ) );
		}

		if ( Sec( "Player", "Appearance", "robot paint outfit skin gun skin weapon rifle model design " + string.Join( " ", CharacterSkins.Names ) + " " + string.Join( " ", WeaponSkins.Names ) + " " + string.Join( " ", RifleModels.Names ) ) )
		{
			Section( "Appearance" );
			if ( Show( "Appearance", "rifle model design gun weapon " + string.Join( " ", RifleModels.Names ) ) )
			{
				Row( "Rifle" );
				foreach ( var rifle in RifleModels.Names ) { var r = rifle; Pick( r, RifleModels.Current == r, () => RifleModels.Current = r ); }
			}
			if ( Show( "Appearance", "robot paint outfit " + string.Join( " ", CharacterSkins.Names ) ) )
			{
				Row( "Robot paint" );
				foreach ( var outfit in CharacterSkins.Names ) { var o = outfit; Pick( o, CharacterSkins.Current == o, () => CharacterSkins.Current = o ); }
			}
			if ( Show( "Appearance", "gun skin weapon " + string.Join( " ", WeaponSkins.Names ) ) )
			{
				Row( "Gun skin" );
				foreach ( var skin in WeaponSkins.Names ) { var s = skin; Pick( s, WeaponSkins.Current == s, () => WeaponSkins.Current = s ); }
			}
		}

		if ( Sec( "Player", "Movement", "water feel swim underwater noclip fly" ) )
		{
			Section( "Movement" );
			if ( Show( "Movement", "water feel swim underwater" ) )
			{
				Row( "Water feel" );
				Pick( "Auto", WaterFeel.Force == null, () => WaterFeel.Force = null );
				Pick( "On", WaterFeel.Force == true, () => WaterFeel.Force = true );
				Pick( "Off", WaterFeel.Force == false, () => WaterFeel.Force = false );
			}
			if ( Show( "Movement", "noclip fly" ) ) { Row(); Chip( "Noclip", DevFlags.Noclip, () => DevFlags.Noclip = !DevFlags.Noclip ); }
		}

		// ======== LOOK
		var stylize = GlobalStylize.Current;
		var lookNames = string.Join( " ", Enum.GetValues<GlobalStylize.Look>().Select( LookName ) );
		if ( Sec( "Look", "Style", "look style shader post toon flat bands ink outline " + lookNames ) )
		{
			Section( "Style" );
			if ( Show( "Style", "look style shader post toon flat " + lookNames ) )
			{
				Row( "Look" );
				foreach ( var look in Enum.GetValues<GlobalStylize.Look>() ) { var l = look; Pick( LookName( l ), stylize?.Style == l, () => { if ( stylize is not null ) stylize.Style = l; } ); }
			}
			if ( Show( "Style", "bands ink outline" ) )
			{
				Row( "Bands" );
				foreach ( var n in BandCounts ) { var v = n; Pick( $"{v}", stylize is not null && stylize.Bands == v, () => { if ( stylize is not null ) stylize.Bands = v; } ); }
				Row( "Ink" );
				foreach ( var o in InkLevels ) { var v = o; Pick( v == 0f ? "None" : $"{v}", stylize is not null && MathF.Abs( stylize.Outline - v ) < 0.01f, () => { if ( stylize is not null ) stylize.Outline = v; } ); }
			}
		}

		if ( Sec( "Look", "Lighting and effects", "sun lighting time of day morning noon dusk night skin effect dissolve ghost holo iridescent" ) )
		{
			Section( "Lighting and effects" );
			if ( Show( "Lighting and effects", "sun lighting time of day " + string.Join( " ", SunPresets ) ) )
			{
				Row( "Sun" );
				foreach ( var preset in SunPresets ) { var p = preset; Pick( p, sunPreset == p, () => SetSun( p ) ); }
			}
			if ( Show( "Lighting and effects", "skin effect creature " + string.Join( " ", SkinFx ) ) )
			{
				Row( "Creature skin" );
				foreach ( var fx in SkinFx ) { var v = fx; Pick( v, skinFx == v, () => SetSkinFx( v ) ); }
			}
		}

		if ( Sec( "Look", "Camera and screenshots", "camera fov free camera hide hud hide weapon screenshot" ) )
		{
			Section( "Camera and screenshots" );
			if ( Show( "Camera and screenshots", "fov field of view" ) )
			{
				Row( "FOV" );
				foreach ( var f in Fovs ) { var v = f; Pick( v == 0f ? "Game" : $"{v}", DevFlags.Fov == v, () => DevFlags.Fov = v ); }
			}
			Row();
			if ( Show( "Camera and screenshots", "free camera" ) ) Chip( "Free camera", DevFlags.FreeCam, () => SetFreeCam( !DevFlags.FreeCam ) );
			if ( Show( "Camera and screenshots", "hide hud" ) ) Chip( "Hide HUD", DevFlags.HideHud, () => DevFlags.HideHud = !DevFlags.HideHud );
			if ( Show( "Camera and screenshots", "hide weapon gun" ) ) Chip( "Hide weapon", DevFlags.HideWeapon, () => DevFlags.HideWeapon = !DevFlags.HideWeapon );
		}

		// ======== WORLD
		if ( Sec( "World", "Waves", "wave next +5 boss wave state kills enemies left phase" ) )
		{
			Section( "Waves" );
			var wave = d?.Wave ?? 0;
			Action( "Next wave", () => d?.JumpToWave( wave + 1 ) );
			Action( "+5 waves", () => d?.JumpToWave( wave + 5 ) );
			if ( d is { NextBossWave: > 0 } ) Action( $"Boss wave ({d.NextBossWave})", () => d.JumpToWave( d.NextBossWave ) );
			Info( $"Wave {wave}    Kills {d?.Kills ?? 0}    Enemies left {d?.ZombiesRemaining ?? 0}    Phase {d?.State.ToString() ?? "-"}" );
		}

		if ( Sec( "World", "Time", "time scale slow motion stop pause speed" ) )
		{
			Section( "Time" );
			foreach ( var t in TimeScales ) { var v = t; Pick( v == 0f ? "Stop" : $"{v}x", MathF.Abs( (float)Engine.TimeScale - v ) < 0.01f, () => Engine.TimeScale = MathF.Max( v, 0.0001f ) ); }
		}

		if ( Sec( "World", "Travel", "travel teleport spawn point save spot go to saved scene level indoor forest water test range" ) )
		{
			Section( "Travel" );
			if ( Show( "Travel", "teleport spawn point save spot go to saved" ) )
			{
				Row();
				Action( "To a spawn point", TeleportToSpawnPoint );
				Action( "Save spot", () => { if ( Controller is { } c ) savedSpot = (c.GlobalPosition, c.EyeAngles); } );
				if ( savedSpot is not null ) Action( "Go to saved spot", () => MovePlayer( savedSpot.Value.Position, savedSpot.Value.Angles ) );
			}
			if ( Show( "Travel", "scene level load indoor forest water test range" ) )
			{
				Row( "Scene" );
				Action( "Indoor", () => LoadScene( "res://scenes/indoor.tscn" ) );
				Action( "Forest", () => LoadScene( "res://scenes/forest.tscn" ) );
				Action( "Water level", () => LoadScene( "res://scenes/water_level.tscn" ) );
				Action( "Test range", () => LoadScene( "res://scenes/test_range.tscn" ) );
			}
		}

		if ( Sec( "World", "Debug", "stats overlay fps performance" ) )
		{
			Section( "Debug" );
			Chip( "Stats overlay (fps)", DevFlags.ShowStats, () => DevFlags.ShowStats = !DevFlags.ShowStats );
		}

		if ( Searching && sectionsShown == 0 )
		{
			var none = new Label { Text = $"Nothing matches \"{search}\"" };
			none.AddThemeColorOverride( "font_color", new Color( 1, 1, 1, 0.6f ) );
			body.AddChild( none );
		}
	}

	bool Paused => Director is { SpawningPaused: true };

	readonly record struct EnemyEntry( string Name, WaveDirector.EnemyKind Kind );
	static readonly EnemyEntry[] EnemyKinds =
	{
		new( "Zombie", WaveDirector.EnemyKind.Zombie ),
		new( "Bloober", WaveDirector.EnemyKind.Bloober ),
		new( "Squilien", WaveDirector.EnemyKind.Squilien ),
		new( "Junk Crab", WaveDirector.EnemyKind.Crab ),
		new( "Sea Spider", WaveDirector.EnemyKind.Spider ),
		new( "MC61 boss", WaveDirector.EnemyKind.Boss ),
	};
	static readonly float[] TimeScales = { 0f, 0.1f, 0.25f, 0.5f, 1f };
	static readonly float[] Fovs = { 0f, 50f, 75f, 90f, 110f };
	static readonly string[] SunPresets = { "Default", "Morning", "Noon", "Dusk", "Night" };
	static readonly float[] BandCounts = { 2f, 3f, 4f, 6f };
	static readonly float[] InkLevels = { 0f, 0.5f, 0.9f };
	static readonly string[] SkinFx = { "Off", "Dissolve", "Ghost", "Holo", "Iridescent" };
	string skinFx = "Off";
	readonly Dictionary<MeshInstance3D, Material> skinOriginals = new();

	static string LookName( GlobalStylize.Look look ) => look switch
	{
		GlobalStylize.Look.InkOutlines => "Ink",
		GlobalStylize.Look.Painterly => "Paint",
		GlobalStylize.Look.Storybook => "Book",
		GlobalStylize.Look.Watercolor => "Water",
		_ => look.ToString(),
	};

	/// <summary> Creature skin shaders (experiment): swaps the creature body skins, puts them back on Off. </summary>
	void SetSkinFx( string fx )
	{
		skinFx = fx;
		foreach ( var (r, original) in skinOriginals ) if ( IsInstanceValid( r ) ) r.MaterialOverride = original;
		skinOriginals.Clear();
		if ( fx == "Off" ) return;
		foreach ( var part in CreaturePart.All )
			foreach ( var r in part.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
			{
				// Only the body skins (textured creature materials), so eyes and props keep their look.
				if ( skinOriginals.ContainsKey( r ) || r.MaterialOverride is not StandardMaterial3D { AlbedoTexture: not null } current ) continue;
				skinOriginals[r] = current;
				r.MaterialOverride = FxMaterials.ByName( fx, current.AlbedoColor );
			}
	}

	// ---- actions ----

	void SpawnAlly()
	{
		AllyCommander.KeepAlly = true;
		foreach ( var a in SeaSpider.All.Where( s => s.IsAlly ).ToList() ) a.QueueFree();
		if ( Controller is { } c ) SeaSpider.SpawnAlly( c );
	}

	void DespawnAllies()
	{
		AllyCommander.KeepAlly = false;
		foreach ( var a in SeaSpider.All.Where( s => s.IsAlly ).ToList() ) a.QueueFree();
	}

	void StartPlacing( WaveDirector.EnemyKind kind )
	{
		placeKind = kind;
		DevFlags.Placing = true;
		SetOpen( false );
	}

	(Vector3 Position, Vector3 Normal)? CrosshairHit()
	{
		var cam = GetViewport().GetCamera3D();
		if ( cam is null || Controller is null ) return null;
		var from = cam.GlobalPosition;
		var tr = Trace.Ray( Controller, from, from + -cam.GlobalBasis.Z * 8000f * U.In, Layers.World, new Godot.Collections.Array<Rid> { Controller.GetRid() } );
		return tr.Hit ? (tr.HitPosition, tr.Normal) : null;
	}

	void UpdatePlacing()
	{
		var showMarker = DevFlags.Placing && !open && CrosshairHit() is not null;
		if ( showMarker && placeMarker is null )
		{
			placeMarker = new MeshInstance3D { Mesh = Shapes.Sphere, MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.Cyan, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }, Scale = Vector3.One * 20f * U.In };
			GetTree().CurrentScene.AddChild( placeMarker );
		}
		if ( placeMarker is not null )
		{
			placeMarker.Visible = showMarker;
			if ( showMarker ) placeMarker.GlobalPosition = CrosshairHit()!.Value.Position;
		}
	}

	void PlaceAtCrosshair()
	{
		if ( CrosshairHit() is not { } hit ) return;
		Director?.SpawnAt( placeKind, hit.Position + hit.Normal * 2f * U.In, GetViewport().GetCamera3D().GlobalPosition );
	}

	void UpdateNoclip( float dt )
	{
		var controller = Controller;
		if ( controller is null ) return;
		if ( DevFlags.Noclip != noclipApplied )
		{
			noclipApplied = DevFlags.Noclip;
			controller.Scripted = noclipApplied;   // no gravity, no collision: we move it by hand
			controller.Velocity = Vector3.Zero;
			if ( !open ) controller.UseInputControls = !noclipApplied && !DevFlags.FreeCam;
		}
		if ( !noclipApplied || open || DevFlags.FreeCam ) return;
		var view = controller.EyeAngles.ToBasis();
		var move = Input.GetVector( "left", "right", "backward", "forward" );
		var wish = -view.Z * move.Y + view.X * move.X;
		if ( Input.IsActionPressed( "jump" ) ) wish += Vector3.Up;
		if ( Input.IsActionPressed( "duck" ) ) wish += Vector3.Down;
		if ( wish.IsNearlyZero() ) return;
		var speed = NoclipSpeed * (Input.IsActionPressed( "run" ) ? 3f : 1f);
		controller.GlobalPosition += wish.Normalized() * speed * U.In * dt;
	}

	void SetFreeCam( bool on )
	{
		var controller = Controller;
		if ( on )
		{
			var cam = GetViewport().GetCamera3D();
			DevFlags.FreeCamPosition = cam?.GlobalPosition ?? (controller?.GlobalPosition ?? Vector3.Zero) + Vector3.Up * 64f * U.In;
			DevFlags.FreeCamAngles = controller?.EyeAngles ?? new Angles( 0, 0, 0 );
			freeLook = Vector2.Zero;
		}
		DevFlags.FreeCam = on;
		if ( controller is not null && !open )
		{
			controller.UseInputControls = !on && !DevFlags.Noclip;
			controller.UseLookControls = !on;
		}
	}

	void UpdateFreeCam( float dt )
	{
		if ( !DevFlags.FreeCam || open ) { freeLook = Vector2.Zero; return; }
		var sensitivity = Controller?.LookSensitivity ?? 0.12f;
		var angles = DevFlags.FreeCamAngles;
		angles.Yaw -= freeLook.X * sensitivity;
		angles.Pitch = MathX.Clamp( angles.Pitch + freeLook.Y * sensitivity, -89f, 89f );
		angles.Roll = 0f;
		freeLook = Vector2.Zero;
		DevFlags.FreeCamAngles = angles;
		var view = angles.ToBasis();
		var move = Input.GetVector( "left", "right", "backward", "forward" );
		var wish = -view.Z * move.Y + view.X * move.X;
		if ( Input.IsActionPressed( "jump" ) ) wish += Vector3.Up;
		if ( Input.IsActionPressed( "duck" ) ) wish += Vector3.Down;
		if ( wish.IsNearlyZero() ) return;
		var speed = FreeCamSpeed * (Input.IsActionPressed( "run" ) ? 3f : 1f);
		DevFlags.FreeCamPosition += wish.Normalized() * speed * U.In * dt;
	}

	void UpdateHideHud()
	{
		if ( DevFlags.HideHud == hudHidden ) return;
		hudHidden = DevFlags.HideHud;
		foreach ( var layer in GetTree().Root.FindChildren( "*", "CanvasLayer", true, false ).OfType<CanvasLayer>() )
			if ( layer != this ) layer.Visible = !hudHidden;
	}

	void SetSun( string preset )
	{
		var sun = GetTree().Root.FindChildren( "*", "DirectionalLight3D", true, false ).OfType<DirectionalLight3D>().FirstOrDefault();
		if ( sun is null ) return;
		if ( sunBasis is null ) { sunBasis = sun.GlobalBasis; sunColor = sun.LightColor; }
		sunPreset = preset;
		var forward = -sunBasis.Value.Z;
		var yaw = MathX.Deg( MathF.Atan2( -forward.X, -forward.Z ) );
		(Basis basis, Color color) = preset switch
		{
			"Morning" => (new Angles( 18f, yaw, 0f ).ToBasis(), new Color( 1f, 0.82f, 0.62f )),
			"Noon" => (new Angles( 78f, yaw, 0f ).ToBasis(), new Color( 1f, 0.97f, 0.92f )),
			"Dusk" => (new Angles( 6f, yaw + 40f, 0f ).ToBasis(), new Color( 1f, 0.5f, 0.28f )),
			"Night" => (new Angles( 45f, yaw, 0f ).ToBasis(), new Color( 0.35f, 0.45f, 0.8f )),
			_ => (sunBasis.Value, sunColor),
		};
		sun.GlobalBasis = basis;
		sun.LightColor = color;
		sun.LightEnergy = preset switch { "Morning" => 2.2f, "Noon" => 3f, "Dusk" => 1.8f, "Night" => 0.5f, _ => sun.LightEnergy };
	}

	void MovePlayer( Vector3 position, Angles? angles = null )
	{
		var controller = Controller;
		if ( controller is null ) return;
		controller.GlobalPosition = position;
		if ( angles is { } a ) controller.EyeAngles = a;
		controller.Velocity = Vector3.Zero;
		if ( DevFlags.FreeCam ) SetFreeCam( true );
	}

	Vector3 Ground( Vector3 near )
	{
		var tr = Trace.Ray( Controller, near + Vector3.Up * 50f * U.In, near + Vector3.Down * 3000f * U.In, Layers.World );
		return tr.Hit ? tr.HitPosition : near;
	}

	void TeleportToBoss()
	{
		var boss = MC61.All.FirstOrDefault( b => !b.IsDead );
		if ( boss is null ) return;
		var forward = (-boss.GlobalBasis.Z).Flat().SafeNormal();
		var spot = Ground( boss.GlobalPosition + forward * 260f * U.In );
		var dir = boss.GlobalPosition - (spot + Vector3.Up * 64f * U.In);
		var yaw = MathX.Deg( MathF.Atan2( -dir.X, -dir.Z ) );
		var pitch = -MathX.Deg( MathF.Atan2( dir.Y, dir.Flat().Length() ) );
		MovePlayer( spot, new Angles( pitch, yaw, 0f ) );
	}

	void TeleportToSpawnPoint()
	{
		var points = GetTree().GetNodesInGroup( "spawn_point" ).OfType<Node3D>().ToList();
		if ( points.Count == 0 ) return;
		MovePlayer( points[Rng.Int( 0, points.Count - 1 )].GlobalPosition + Vector3.Up * 4f * U.In );
	}

	void LoadScene( string path )
	{
		SetOpen( false );
		Engine.TimeScale = 1f;
		GetTree().ChangeSceneToFile( path );
	}
}

/// <summary>
/// Port of s&box CharacterSkins: paint jobs for the third-person robot. The robot model itself comes with step 7b,
/// which applies these.
/// </summary>
public static class CharacterSkins
{
	public static string[] Names => new[] { "Default", "Rust", "Stealth", "Hazard", "Frost", "Gold" };
	public static string[] Slots => new[] { "body", "frame", "metal", "accent", "glow" };
	public static string Current { get; set; } = "Default";

	/// <summary> Colour of the lights (eye, chest, wrists, antenna) for each paint job. </summary>
	public static Color GlowOf( string skin ) => skin switch
	{
		"Rust" => new Color( 1f, 0.6f, 0.15f ),
		"Stealth" => new Color( 1f, 0.1f, 0.12f ),
		"Hazard" => new Color( 1f, 0.45f, 0.1f ),
		"Frost" => new Color( 0.4f, 0.7f, 1f ),
		"Gold" => new Color( 1f, 0.9f, 0.4f ),
		_ => new Color( 0.3f, 1f, 0.9f ),
	};
}
