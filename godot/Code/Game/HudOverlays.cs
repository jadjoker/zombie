using Godot;

/// <summary>
/// The rest of the s&box ZombieHud: screen-edge glows for hurt, venom, burn and slow, ink blots, the stamina pips
/// and jetpack power bar under the crosshair, the slam flash, surge speed lines, the boss bar with MC61's shouting,
/// and the buy station prompt. Pixel sizes are the s&box ones at 1080p.
/// </summary>
public partial class HudOverlays : Control
{
	/// <summary> Tests: the mimic octopus's "CUT THE TENTACLE OFF!" was drawn last frame. </summary>
	public static bool CutPromptShown;

	PlayerSurvivor player;
	ColorRect hurt, venom, radiation, malfunction, burn, slow, slam;
	Control boss, prompt;
	Label speech, promptKey, promptTitle, promptDesc;
	float jetShown, staminaShown;

	static readonly Shader Vignette = GD.Load<Shader>( "res://assets/shaders/vignette.gdshader" );

	public static HudOverlays Create( PlayerSurvivor player )
	{
		var hud = new HudOverlays { player = player, MouseFilter = MouseFilterEnum.Ignore };
		hud.SetAnchorsPreset( LayoutPreset.FullRect );
		return hud;
	}

	ColorRect Glow( Color edge, float inner, Color? centre = null, float outer = 1f )
	{
		var m = new ShaderMaterial { Shader = Vignette };
		m.SetShaderParameter( "tint", edge );
		m.SetShaderParameter( "centre_tint", centre ?? edge with { A = 0f } );
		m.SetShaderParameter( "inner", inner );
		m.SetShaderParameter( "outer", outer );
		var rect = new ColorRect { Material = m, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
		rect.SetAnchorsPreset( LayoutPreset.FullRect );
		AddChild( rect );
		return rect;
	}

	static Label Text( Control parent, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Center ) => UiStyle.Text( parent, size, color, align );

	public override void _Ready()
	{
		Build();
		UiStyle.Changed += Rebuild;
	}

	public override void _ExitTree() => UiStyle.Changed -= Rebuild;

	/// <summary> The theme changed: build the glows, the speech and the prompt again in the new look. </summary>
	void Rebuild()
	{
		foreach ( var child in GetChildren() ) { RemoveChild( child ); child.QueueFree(); }
		Build();
	}

	static HudTheme Theme => UiStyle.Theme;
	static float Sc => Theme.Scale;
	static int S( int size ) => UiStyle.S( size );
	/// <summary> The prompt's title colour when it is not tinted (white, or dark on a pale panel). </summary>
	static Color TitleColour => Theme.LightPanel ? Theme.PanelReadout : Colors.White;

	void Build()
	{
		hurt = Glow( new Color( "b00000" ) with { A = 0.9f }, 0.4f );
		venom = Glow( new Color( "3cff20" ) with { A = 0.75f }, 0.45f );
		radiation = Glow( new Color( "9dff1a" ) with { A = 0.7f }, 0.52f );
		malfunction = Glow( new Color( "ffb020" ) with { A = 0.7f }, 0.5f );
		burn = Glow( new Color( "ff3c00" ) with { A = 0.8f }, 0.45f );
		slow = Glow( new Color( "a8e6ff" ) with { A = 0.8f }, 0.5f );
		slam = Glow( new Color( "0a3040" ) with { A = 0f }, 0f, new Color( 1f, 1f, 1f, 0.9f ), 0.8f );

		boss = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
		boss.SetAnchorsPreset( LayoutPreset.FullRect );
		AddChild( boss );
		speech = UiStyle.Big( this, S( 66 ), new Color( "ff7ae3" ) );
		speech.AnchorLeft = 0f; speech.AnchorRight = 1f; speech.AnchorTop = speech.AnchorBottom = 0.26f;
		speech.OffsetLeft = speech.OffsetRight = 0f; speech.OffsetBottom = 80f;

		// The use prompt: a suit frame with the key as a chunky teal button, the title and a line under it.
		prompt = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
		prompt.AddThemeStyleboxOverride( "panel", UiStyle.Frame( Sc, null, 16 ) );
		prompt.Draw += () => UiStyle.DrawFrameExtras( prompt, new Rect2( Vector2.Zero, prompt.Size ), 1f, null, Sc );
		prompt.SetAnchorsPreset( LayoutPreset.CenterTop );
		prompt.AnchorTop = prompt.AnchorBottom = 0.58f; prompt.GrowHorizontal = GrowDirection.Both;
		AddChild( prompt );
		var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		row.AddThemeConstantOverride( "separation", S( 14 ) );
		prompt.AddChild( row );
		var keyCap = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
		keyCap.AddThemeStyleboxOverride( "panel", UiStyle.KeyCap( Sc ) );
		row.AddChild( keyCap );
		promptKey = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
		promptKey.AddThemeFontOverride( "font", UiStyle.Number );
		promptKey.AddThemeFontSizeOverride( "font_size", S( 26 ) );
		promptKey.FontColour( UiStyle.KeyText );
		keyCap.AddChild( promptKey );
		promptKey.Show( "E" );
		var textBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		textBox.AddThemeConstantOverride( "separation", -2 );
		row.AddChild( textBox );
		promptTitle = UiStyle.Text( textBox, S( 22 ), TitleColour, HorizontalAlignment.Left, onPanel: true );
		promptTitle.AddThemeFontOverride( "font", UiStyle.LabelBold );
		promptTitle.Uppercase = true;
		promptDesc = UiStyle.Text( textBox, S( 15 ), Theme.PanelReadout, HorizontalAlignment.Left, Theme.MonoSub, onPanel: true );
	}

	PlayerAfflictions Afflictions => player?.Afflictions;
	PlayerMovement Movement => player?.Controller?.GetNodeOrNull<PlayerMovement>( "PlayerMovement" );
	Jetpack Jet => player?.Controller?.GetNodeOrNull<Jetpack>( "Jetpack" );
	static MC61 Boss => MC61.All.FirstOrDefault( b => !b.IsDead );

	static void Show( ColorRect rect, float opacity )
	{
		rect.Visible = opacity > 0.001f;
		if ( rect.Visible ) ((ShaderMaterial)rect.Material).SetShaderParameter( "strength", opacity );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var now = (float)Clock.Now;
		var a = Afflictions;
		Modulate = PlayerSurvivor.LocalDead ? new Color( 1, 1, 1, 0 ) : Colors.White;   // no HUD on the death screen
		Position = new Vector2( VisorOverlay.Jitter, 0f );

		var health = player?.Health;
		var hurtOpacity = 0f;
		if ( health is not null )
		{
			if ( health.IsDead ) hurtOpacity = 0.6f;
			else hurtOpacity = MathF.Max( MathX.Clamp( 0.5f - health.TimeSinceDamaged, 0f, 0.5f ), health.Fraction < 0.3f ? 0.15f + 0.1f * MathF.Sin( now * 6f ) : 0f );
		}
		Show( hurt, hurtOpacity );

		var venomOpacity = 0f;
		if ( a is not null && a.VenomLeft > 0f )
		{
			var kick = MathX.Clamp( 1f - a.TimeSinceVenomed / 0.6f, 0f, 1f ) * 0.5f;
			venomOpacity = MathF.Max( kick, 0.28f + 0.12f * MathF.Sin( now * 7f ) ) * MathX.Clamp( a.VenomLeft / 0.8f, 0f, 1f );
		}
		Show( venom, venomOpacity );
		Show( burn, a is not null && a.BurnLeft > 0f ? (0.3f + 0.15f * MathF.Sin( now * 11f )) * MathX.Clamp( a.BurnLeft / 0.6f, 0f, 1f ) : 0f );
		// A suit malfunction round: amber warning light round the edge of the view, pulsing, with the odd glitch flicker.
		var failing = WaveDirector.Current is { FloatRound: true, State: WaveState.Fighting };
		var glitch = MathF.Sin( now * 23f ) * MathF.Sin( now * 7.3f ) > 0.93f ? 0.3f : 0f;
		Show( malfunction, failing ? 0.22f + 0.1f * MathF.Sin( now * 3.2f ) + glitch : 0f );
		Show( radiation, a is not null && a.RadiationLeft > 0f ? (0.34f + 0.14f * MathF.Sin( now * 5f )) * MathX.Clamp( a.RadiationLeft / 0.6f, 0f, 1f ) : 0f );
		Show( slow, a is not null && a.SlowLeft > 0f ? 0.45f * MathX.Clamp( a.SlowLeft / 0.6f, 0f, 1f ) : 0f );

		var jet = Jet;
		if ( jet is not null && jet.TimeSinceSlam < 0.35f )
		{
			var k = 1f - jet.TimeSinceSlam / 0.35f;
			Show( slam, k * k * 0.55f * jet.LastSlamStrength );
		}
		else Show( slam, 0f );

		var wantJet = jet is not null && (jet.Power < jet.MaxPower - 0.5f || jet.Thrusting || jet.Surging || jet.Charging || jet.TimeSinceRefused < 0.6f) ? 1f : 0f;
		jetShown = MathX.Approach( jetShown, wantJet, dt * (wantJet > 0f ? 8f : 1.5f) );
		var m = Movement;
		var wantStamina = m is not null && (m.Stamina < m.StaminaCap - 0.01f || m.TimeSinceOutOfStamina < 0.6f) ? 1f : 0f;
		staminaShown = MathX.Approach( staminaShown, wantStamina, dt * (wantStamina > 0f ? 8f : 1.5f) );

		var b = Boss;
		boss.Visible = b is not null;
		// The boss's line or move name: MC-51's in pink, Davey's in green, King Raymond's in gold.
		string line = null;
		var lineAge = 99f;
		var lineColour = new Color( "ff7ae3" );
		if ( b is not null && !string.IsNullOrEmpty( b.Line ) ) { line = b.Line; lineAge = b.TimeSinceLine; }
		foreach ( var dj in DaveyJones.All )
			if ( IsInstanceValid( dj ) && !string.IsNullOrEmpty( dj.Line ) && dj.TimeSinceLine < lineAge )
			{
				line = dj.Line;
				lineAge = dj.TimeSinceLine;
				lineColour = new Color( "5cff6e" );
			}
		foreach ( var king in KingRaymond.All )
			if ( IsInstanceValid( king ) && !string.IsNullOrEmpty( king.Line ) && king.TimeSinceLine < lineAge )
			{
				line = king.Line;
				lineAge = king.TimeSinceLine;
				lineColour = KingGold;
			}
		if ( line is not null && lineAge < 2.4f )
		{
			speech.Visible = true;
			speech.Show( line );
			speech.FontColour( lineColour );
			speech.Modulate = new Color( 1, 1, 1, MathX.Clamp( (2.4f - lineAge) / 0.5f, 0f, 1f ) );
			speech.PivotOffset = speech.Size * 0.5f;
			speech.Scale = Vector2.One * (1f + MathX.Clamp( 1f - lineAge / 0.2f, 0f, 1f ) * 0.5f);
			speech.Rotation = MathX.Rad( MathF.Sin( now * 30f ) * 2f );
		}
		else speech.Visible = false;

		var use = player?.Controller?.GetNodeOrNull<PlayerUse>( "PlayerUse" );
		var station = use?.Hovered;
		prompt.Visible = station is not null || (use?.AtDivingBoard ?? false) || (use?.AtTeleporter ?? false);
		promptKey.Show( Controls.Label( "use" ) );   // E on a keyboard, Y or Triangle on a pad
		if ( station is null && (use?.AtDivingBoard ?? false) )
		{
			promptTitle.Show( "Dive in" );
			promptDesc.Show( "Head down to the sea floor" );
			promptTitle.FontColour( TitleColour );
		}
		if ( station is null && (use?.AtTeleporter ?? false) )
		{
			promptTitle.Show( "Teleport" );
			promptDesc.Show( SeaTerrain.Area >= 2
				? $"To Atlantis, the third stage. Wave {WaveDirector.Current?.Wave + 1 ?? 21} starts there"
				: "To the wreck site, the second stage. Wave 11 starts there" );
			promptTitle.FontColour( new Color( 0.5f, 1f, 0.55f ) );
		}
		if ( station is not null )
		{
			var afford = !station.SoldOut && (player?.Points ?? 0) >= station.CurrentCost;
			promptTitle.Show( $"{station.Title}  {(station.SoldOut ? "Sold out" : station.CurrentCost.ToString())}" );
			promptDesc.Show( station.Description );
			promptTitle.FontColour( afford ? TitleColour : new Color( "ff6a6a" ) );
			if ( !station.SoldOut ) promptTitle.Show( $"{station.Title}   {station.CurrentCost}" );
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		var now = (float)Clock.Now;
		var c = Size * 0.5f;

		// Ink blots over the view, fading out over the last second.
		var a = Afflictions;
		if ( a is not null && a.InkLeft > 0f )
		{
			var ink = MathX.Clamp( a.InkLeft, 0f, 1f ) * 0.92f;
			foreach ( var blot in a.Blots )
			{
				var at = new Vector2( blot.X / 100f * Size.X, blot.Y / 100f * Size.Y );
				Blot( at, blot.Size, 0.85f, ink );
				var r = new Random( blot.Seed );
				for ( var d = 0; d < 4; d++ )
				{
					var size = blot.Size * (0.15f + (float)r.NextDouble() * 0.2f);
					var angle = (float)r.NextDouble() * MathF.PI * 2f;
					var dist = blot.Size * (0.45f + (float)r.NextDouble() * 0.25f);
					Blot( at + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * dist, size, 1f, ink );
				}
			}
		}

		// Stamina pips under the crosshair.
		var m = Movement;
		if ( m is not null && staminaShown > 0.01f )
		{
			var empty = m.TimeSinceOutOfStamina < 0.35f;
			// One pip for each whole point; the movement buff's extra is a shorter pip on the end.
			var cap = m.StaminaCap;
			var count = (int)MathF.Ceiling( cap - 0.001f );
			var start = new Vector2( c.X - (cap * 30f - 4f) * 0.5f, c.Y + 34f );
			for ( var i = 0; i < count; i++ )
			{
				var size = MathX.Clamp( cap - i, 0f, 1f );
				var fill = MathX.Clamp( (m.Stamina - i) / size, 0f, 1f );
				var at = start + new Vector2( i * 30f, 0f );
				DrawRect( new Rect2( at, new Vector2( 26f * size, 5f ) ), (empty ? new Color( "ff3b3b" ) : new Color( 0, 0, 0 )) with { A = 0.45f * staminaShown } );
				DrawRect( new Rect2( at, new Vector2( 26f * size * fill, 5f ) ), (fill >= 1f ? Colors.White : new Color( 1, 1, 1, 0.55f )) with { A = (fill >= 1f ? 0.9f : 0.55f) * staminaShown } );
			}
		}

		// Jetpack power bar (white charge over it while winding up a surge; a tick where it restarts after a stall).
		var jet = Jet;
		if ( jet is not null && jetShown > 0.01f )
		{
			var at = new Vector2( c.X - 45f, c.Y + 46f );
			var refused = jet.TimeSinceRefused < 0.35f;
			DrawRect( new Rect2( at, new Vector2( 90f, 5f ) ), (refused ? new Color( "ff3b3b" ) : Colors.Black) with { A = 0.45f * jetShown } );
			var fillColor = jet.DockedTo is not null ? new Color( "5fffe0" ) : jet.Stalled ? new Color( "ff8a3a" ) : new Color( "8fe8ff" );
			DrawRect( new Rect2( at, new Vector2( 90f * jet.PowerFraction, 5f ) ), fillColor with { A = 0.9f * jetShown } );
			if ( jet.Charging ) DrawRect( new Rect2( at, new Vector2( 90f * jet.Charge * jet.PowerFraction, 5f ) ), new Color( 1, 1, 1, 0.85f * jetShown ) );
			DrawRect( new Rect2( at + new Vector2( 90f * jet.RestartAt - 1f, -2f ), new Vector2( 2f, 9f ) ), new Color( 1, 1, 1, 0.5f * jetShown ) );
		}

		// Surge: streaks rushing out from the middle (a faint pull inward while winding up).
		if ( jet is not null && (jet.Surging || jet.Charging) )
		{
			var strength = jet.Surging ? jet.SurgeStrength * jet.SurgeLeftFraction : jet.Charge * 0.35f;
			var alpha = MathX.Clamp( strength * 1.4f, 0f, 0.9f );
			for ( var i = 0; i < 22; i++ )
			{
				var angle = MathX.Rad( i * (360f / 22f) + (i % 3) * 6f );
				var travel = (now * (jet.Surging ? 1400f : -300f) + i * 157f) % 300f;
				if ( travel < 0f ) travel += 300f;
				var distance = 160f + travel;
				var length = 60f + (i % 4) * 25f;
				var dir = new Vector2( MathF.Sin( angle ), -MathF.Cos( angle ) );
				DrawLine( c + dir * distance, c + dir * (distance + length), new Color( 1, 1, 1, 0.6f * alpha ), 2f );
			}
		}

		// Boss health bars: a suit frame across the top with TARGET and the name, the percentage, and a row of pills.
		var b = Boss;
		if ( b is not null ) BossBar( 0f, "MC-51", b.HealthFraction, UiStyle.Pink, UiStyle.PinkTop );
		else if ( KingRaymond.All.FirstOrDefault( k => !k.IsDead ) is { Health: not null } king )
			BossBar( 0f, "KING RAYMOND", king.Health.Fraction, KingGold, new Color( "fff1b8" ), numeral: "IV" );
		else if ( DaveyJones.All.FirstOrDefault( g => !g.IsDead ) is { Health: not null } davey )
			BossBar( 0f, "DAVEY PHONE HOME", davey.HealthFraction, new Color( "5cff6e" ), new Color( "d4ffd0" ) );
		else if ( Poseidon.All.FirstOrDefault( p => !p.IsDead ) is { Health: not null } poseidon )
			BossBar( 0f, "POSEIDON", poseidon.HealthFraction, new Color( "2fe0c8" ), new Color( "b8fff4" ) );
		else if ( Kraken.All.FirstOrDefault( k => !k.IsDead ) is { Health: not null } kraken )
		{
			BossBar( 28f, "KRAKEN", kraken.Health.Fraction, new Color( "3fd84f" ), new Color( "c4ffb0" ), kraken.HelmetOn ? kraken.HelmetFraction : -1f );
		}

		// Buried by Davey: mash jump to climb out.
		if ( DaveyJones.Sinking is { } sinker && !PlayerSurvivor.LocalDead )
		{
			var mashPulse = 0.75f + 0.25f * MathF.Sin( now * 14f );
			var mashY = Size.Y * 0.6f;
			UiStyle.DrawBig( this, new Vector2( 0f, mashY ), $"MASH {Controls.Label( "jump" )}!", (int)UiStyle.S( 44f ), UiStyle.Gold with { A = mashPulse }, HorizontalAlignment.Center, Size.X );
			var mashWidth = UiStyle.S( 340f );
			var mashBar = new Rect2( c.X - mashWidth * 0.5f, mashY + UiStyle.S( 22f ), mashWidth, UiStyle.S( 16f ) );
			DrawRect( mashBar.Grow( 3f ), new Color( 0f, 0f, 0f, 0.6f ) );
			DrawRect( mashBar with { Size = mashBar.Size with { X = mashWidth * MathX.Clamp( sinker.Mashes / (float)Math.Max( 1, sinker.MashesNeeded ), 0f, 1f ) } }, new Color( "5cff6e" ) );
			UiStyle.DrawTag( this, new Vector2( 0f, mashY + UiStyle.S( 46f ) ), $"{sinker.Mashes} / {sinker.MashesNeeded}", 18, UiStyle.Teal, HorizontalAlignment.Center, Size.X );
		}

		// Held by a mimic octopus: any melee strike cuts the arm off.
		CutPromptShown = MimicOctopus.HoldingLocal is not null && !PlayerSurvivor.LocalDead;
		if ( CutPromptShown )
		{
			var cutPulse = 0.7f + 0.3f * MathF.Sin( now * 12f );
			var cutY = Size.Y * 0.58f;
			UiStyle.DrawBig( this, new Vector2( 0f, cutY ), "CUT THE TENTACLE OFF!", (int)UiStyle.S( 44f ), new Color( "ff5a8c" ) with { A = cutPulse }, HorizontalAlignment.Center, Size.X );
			UiStyle.DrawTag( this, new Vector2( 0f, cutY + UiStyle.S( 34f ) ), $"MELEE  {Controls.Label( "melee" )}", 20, UiStyle.Gold, HorizontalAlignment.Center, Size.X );
		}

		// The radio: the song's name for a few seconds when it changes.
		if ( Radio.NowPlaying is { } song && Radio.SinceChange < 5f && GameSettings.MusicVolume > 0f && !PlayerSurvivor.LocalDead )
		{
			var fade = MathX.Clamp( MathF.Min( (float)Radio.SinceChange / 0.4f, (5f - (float)Radio.SinceChange) / 0.8f ), 0f, 1f );
			var at = new Vector2( Size.X * 0.2f, Size.Y - 56f );
			UiStyle.DrawTag( this, at, $"Now playing  {song}", 15, UiStyle.Gold with { A = fade }, HorizontalAlignment.Center, Size.X * 0.6f, true, false );
		}

		// The robot's own readout while the suit is failing.
		var suitFailing = WaveDirector.Current is { FloatRound: true, State: WaveState.Fighting };
		if ( suitFailing && !PlayerSurvivor.LocalDead )
		{
			var blink = MathF.Sin( now * 5f ) > -0.2f ? 1f : 0.35f;
			var amber = new Color( "ffb020" );
			var at = new Vector2( 28f, Size.Y * 0.2f );
			UiStyle.DrawFrame( this, new Rect2( at - new Vector2( 14f, 34f ) * Sc, new Vector2( 340f, 96f ) * Sc ), 1f, amber with { A = blink }, Sc );
			UiStyle.DrawBig( this, at, "SUIT MALFUNCTION", S( 26 ), amber, HorizontalAlignment.Left, -1f, blink, true );
			UiStyle.DrawTag( this, at + new Vector2( 0f, 24f * Sc ), "BALLAST OFFLINE", S( 15 ), Theme.LightPanel ? new Color( "c0301a" ) : new Color( 1f, 0.5f, 0.35f ), onPanel: true );
			UiStyle.DrawTag( this, at + new Vector2( 0f, 44f * Sc ), "boots cannot grip: you float", S( 14 ), Theme.PanelReadout, HorizontalAlignment.Left, -1f, true, false, true );
		}

		// Irradiated by the Kraken: how long is left, and what it is doing to you.
		if ( player?.Afflictions is { RadiationLeft: > 0f } dose )
		{
			var words = dose.RadiationPoisonLeft > 0f ? "poisoned, and stamina recovers slowly" : "stamina recovers slowly";
			UiStyle.DrawBig( this, new Vector2( 0f, Size.Y * 0.74f ), $"IRRADIATED {MathF.Ceiling( dose.RadiationLeft ):0}s", S( 30 ), new Color( "9dff1a" ), HorizontalAlignment.Center, Size.X );
			UiStyle.DrawTag( this, new Vector2( 0f, Size.Y * 0.74f + 26f ), words, 15, UiStyle.Readout, HorizontalAlignment.Center, Size.X );
		}

		// The Kraken's whirlpool: a countdown while the player is outside its ring, then the ride back.
		if ( player is not null && Kraken.WhirlpoolWarning( player ) is { } left )
		{
			var warn = new Color( "7dfff0" );
			var pulse = 0.75f + 0.25f * MathF.Sin( (float)Clock.Now * 10f );
			var y = Size.Y * 0.3f;
			var title = left > 0f ? $"WHIRLPOOL IN {MathF.Ceiling( left ):0}" : "WHIRLPOOL";
			UiStyle.DrawBig( this, new Vector2( 0f, y ), title, S( 58 ), warn, HorizontalAlignment.Center, Size.X, pulse );
			if ( left > 0f )
			{
				UiStyle.DrawTag( this, new Vector2( 0f, y + 36f ), "Get back inside the ring of lights", 17, Colors.White, HorizontalAlignment.Center, Size.X );
				var width = Size.X * 0.24f;
				UiStyle.DrawPills( this, new Rect2( (Size.X - width) * 0.5f, y + 50f, width, 10f ), 10, MathX.Clamp( left / 5f, 0f, 1f ), warn, Colors.White );
			}
		}
	}

	/// <summary> A boss bar across the top: the theme's word and the name, the percentage, a bar (and a thinner second one, the Kraken's dome). </summary>
	void BossBar( float down, string name, float fraction, Color fill, Color top, float second = -1f, string numeral = null )
	{
		var width = MathF.Min( Size.X * 0.4f * Sc, 760f * Sc );
		var rect = new Rect2( (Size.X - width) * 0.5f, (136f + down) * Sc, width, (second >= 0f ? 70f : 58f) * Sc );
		UiStyle.DrawFrame( this, rect, 1f, fill, Sc );
		var text = Theme.LightPanel ? Theme.PanelReadout : top;
		var title = $"{Theme.BossWord}{name}";
		var at = rect.Position + new Vector2( 18f, 24f ) * Sc;
		UiStyle.DrawTag( this, at, title, S( 15 ), text, onPanel: true );
		if ( numeral is not null ) RomanNumeral( at + new Vector2( UiStyle.TagWidth( title + " ", S( 15 ) ), 0f ), numeral, S( 15 ), fill );
		UiStyle.DrawTag( this, rect.Position + new Vector2( 0f, 24f * Sc ), $"{MathF.Ceiling( fraction * 100f ):0}%", S( 15 ), text, HorizontalAlignment.Right, width - 18f * Sc, true, onPanel: true );
		UiStyle.DrawPills( this, new Rect2( rect.Position + new Vector2( 18f, 34f ) * Sc, new Vector2( width - 36f * Sc, 12f * Sc ) ), 24, fraction, fill, top, 1f, -1f, 3f );
		if ( second >= 0f )
			UiStyle.DrawPills( this, new Rect2( rect.Position + new Vector2( 18f, 52f ) * Sc, new Vector2( width - 36f * Sc, 6f * Sc ) ), 24, second, new Color( "bfeaff" ), Colors.White, 1f, -1f, 3f );
	}

	static readonly Color KingGold = new( "ffc935" );

	/// <summary>
	/// A Roman numeral as carved on a monument (King Raymond's "IV"): gold capitals in the display font with a bar
	/// along the top and the bottom.
	/// </summary>
	void RomanNumeral( Vector2 baseline, string numeral, int size, Color gold )
	{
		var font = UiStyle.Display;
		var big = (int)(size * 1.25f);
		var w = font.GetStringSize( numeral, HorizontalAlignment.Left, -1f, big ).X;
		var ascent = font.GetAscent( big ) * 0.72f;
		var pad = size * 0.18f;
		DrawString( font, baseline + new Vector2( 1.5f, 1.5f ), numeral, HorizontalAlignment.Left, -1f, big, new Color( 0f, 0f, 0f, 0.6f ) );
		DrawString( font, baseline, numeral, HorizontalAlignment.Left, -1f, big, gold );
		var thick = MathF.Max( 1.5f, size * 0.12f );
		DrawRect( new Rect2( baseline + new Vector2( -pad, -ascent - pad - thick ), new Vector2( w + pad * 2f, thick ) ), gold );
		DrawRect( new Rect2( baseline + new Vector2( -pad, pad * 0.6f ), new Vector2( w + pad * 2f, thick ) ), gold );
	}

	/// <summary> A soft ink blot: solid to 45% of its corner distance, gone by 72% (the s&box radial gradient). </summary>
	void Blot( Vector2 at, float size, float squash, float alpha )
	{
		var ink = new Color( "1c0a33" );
		DrawSetTransform( at, 0f, new Vector2( 1f, squash ) );
		var corner = size * 0.7071f;
		DrawCircle( Vector2.Zero, corner * 0.45f, ink with { A = 0.97f * alpha } );
		for ( var i = 1; i <= 5; i++ )
		{
			var k = i / 5f;
			DrawCircle( Vector2.Zero, corner * MathX.Lerp( 0.45f, 0.72f, k ), new Color( "2a1048" ) with { A = 0.97f * alpha * (1f - k) * 0.35f } );
		}
		DrawSetTransform( Vector2.Zero, 0f, Vector2.One );
	}
}
