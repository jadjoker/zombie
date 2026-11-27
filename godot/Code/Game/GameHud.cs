using Godot;

/// <summary>
/// The survival parts of the s&box ZombieHud: wave number and status, danger bar (open world), wave banners
/// (start / cleared / anchor snapped / boss), kill counter with a pop, points with the gain popup and denied flash,
/// health bar with the pale lag bar and venom/burn/slow states, camera goo drips, venom tint and the game over screen.
/// Pixel sizes are the s&box ones at 1080p. (The scope and weapon parts are in WeaponHud.)
/// </summary>
public partial class GameHud : Control
{
	// A soft round blob (solid to 48% of the radius, gone by 64%) and a streak fading in toward the bottom.
	static readonly Texture2D Blob = new GradientTexture2D
	{
		Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2( 0.5f, 0.5f ), FillTo = new Vector2( 0.5f, 0f ),
		Gradient = new Gradient { Offsets = new[] { 0f, 0.679f, 0.905f, 1f }, Colors = new[] { Colors.White, Colors.White, new Color( 1, 1, 1, 0 ), new Color( 1, 1, 1, 0 ) } },   // CSS 48% / 64% of the corner distance
	};
	static Texture2D[] splats;
	static Texture2D[] Splats => splats ??= Enumerable.Range( 0, 8 ).Select( i => GD.Load<Texture2D>( $"res://assets/textures/goo/goo_splat_{i}.png" ) ).Where( t => t is not null ).DefaultIfEmpty( Blob ).ToArray();

	static readonly Texture2D Streak = new GradientTexture2D
	{
		Width = 8, Height = 64, FillFrom = new Vector2( 0.5f, 0f ), FillTo = new Vector2( 0.5f, 1f ),
		Gradient = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color( 1, 1, 1, 0 ), Colors.White } },
	};

	PlayerSurvivor player;
	WaveDirector Director => WaveDirector.Current;

	Label wave, status, bannerBig, bannerSmall, kills, killsCaption, points, gain, healthCaption, gameOverTitle, gameOverDetail;
	Control banner, gameOver, dive;
	Label diveCount, diveHelp, depthNum, depthSpeed;
	float lagHealth = 100f;
	static string announceText = "";
	static Color announceColour = Colors.White;
	static TimeSince announceAge = 99f;

	/// <summary> Shows a pickup's name in the middle of the screen for a moment. </summary>
	public static void Announce( string text, Color colour )
	{
		announceText = text.ToUpperInvariant();
		announceColour = colour.Lightened( 0.35f );
		announceAge = 0f;
	}
	int lastKills;
	TimeSince timeSinceKillCount = 999f;

	public static GameHud Create( PlayerSurvivor player )
	{
		var hud = new GameHud { player = player, MouseFilter = MouseFilterEnum.Ignore };
		hud.SetAnchorsPreset( LayoutPreset.FullRect );
		return hud;
	}

	static Label Text( Control parent, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Center ) => UiStyle.Text( parent, size, color, align );
	static Label Big( Control parent, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Center ) => UiStyle.Big( parent, size, color, align );

	/// <summary> The health widget's size (bottom left) in the current theme, and where the points sit over it. </summary>
	static HudTheme Theme => UiStyle.Theme;
	static float Sc => Theme.Scale;
	static Vector2 HullSize => Theme.Health switch
	{
		HudTheme.HealthLook.Porthole or HudTheme.HealthLook.Sonar => new Vector2( 150f, 150f ) * Sc,
		HudTheme.HealthLook.Tank => new Vector2( 96f, 140f ) * Sc,
		HudTheme.HealthLook.Tentacle => new Vector2( 230f, 110f ) * Sc,
		_ => new Vector2( 360f, 78f ) * Sc,
	};
	static float HullW => HullSize.X;
	static float HullH => HullSize.Y;
	const float Edge = 32f;
	static float PointsBottom => -(Edge + HullH + 16f * Sc);
	static int S( int size ) => UiStyle.S( size );
	string finePrint;

	public override void _Ready()
	{
		_ = Splats;   // the visor goo textures: loading them on the first close kill cost 35 ms mid-fight
		AmmoPickup.Preload();
		Build();
		UiStyle.Changed += Rebuild;
	}

	public override void _ExitTree() => UiStyle.Changed -= Rebuild;

	/// <summary> The theme changed: build every label again in the new look. </summary>
	void Rebuild()
	{
		foreach ( var child in GetChildren() ) { RemoveChild( child ); child.QueueFree(); }
		Build();
	}

	void Build()
	{
		finePrint = Theme.FinePrint is { Length: > 0 } lines ? lines[new Random().Next( lines.Length )] : null;
		var top = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		top.SetAnchorsPreset( LayoutPreset.CenterTop );
		top.OffsetTop = 32; top.GrowHorizontal = GrowDirection.Both;
		AddChild( top );
		wave = Big( top, S( 54 ), Theme.Wave );
		status = UiStyle.Text( top, S( 18 ), UiStyle.Readout, mono: true );
		status.Uppercase = true;

		banner = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		banner.SetAnchorsPreset( LayoutPreset.CenterTop );
		banner.AnchorTop = 0.28f; banner.GrowHorizontal = GrowDirection.Both;
		AddChild( banner );
		bannerBig = Big( banner, S( 112 ), Colors.White );
		bannerSmall = UiStyle.Sub( banner, S( 26 ), new Color( 1, 1, 1, 0.92f ) );

		var killBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		killBox.SetAnchorsPreset( LayoutPreset.TopRight );
		killBox.OffsetTop = 32; killBox.OffsetRight = -40; killBox.GrowHorizontal = GrowDirection.Begin;
		AddChild( killBox );
		kills = Big( killBox, S( 46 ), Theme.Number, HorizontalAlignment.Right );
		killsCaption = UiStyle.Tag( killBox, S( 14 ), Theme.LightPanel ? Theme.Number : null, HorizontalAlignment.Right );
		killsCaption.Show( "KILLS" );

		points = Big( this, S( 40 ), UiStyle.Gold, HorizontalAlignment.Left );
		points.SetAnchorsPreset( LayoutPreset.BottomLeft );
		points.OffsetLeft = Edge + 4; points.OffsetTop = points.OffsetBottom = PointsBottom; points.GrowVertical = GrowDirection.Begin;
		gain = Big( this, S( 24 ), UiStyle.Gold, HorizontalAlignment.Left );
		gain.SetAnchorsPreset( LayoutPreset.BottomLeft );
		gain.OffsetLeft = Edge + 4; gain.OffsetTop = gain.OffsetBottom = PointsBottom - S( 44 ); gain.GrowVertical = GrowDirection.Begin;

		// The HEALTH caption and its states are drawn inside the health panel now (_Draw); this label stays for tests.
		healthCaption = Text( this, 16, Colors.White, HorizontalAlignment.Left );
		healthCaption.Visible = false;

		// The dive: crushed counter and controls up top, depth meter on the right.
		dive = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
		dive.SetAnchorsPreset( LayoutPreset.FullRect );
		AddChild( dive );
		var diveTop = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		diveTop.SetAnchorsPreset( LayoutPreset.CenterTop );
		diveTop.OffsetTop = 110; diveTop.GrowHorizontal = GrowDirection.Both;
		dive.AddChild( diveTop );
		diveCount = Big( diveTop, S( 30 ), new Color( "d6fff7" ) );
		diveHelp = UiStyle.Text( diveTop, 15, UiStyle.Readout, mono: true );
		diveHelp.Show( DiveHelp );
		var depthBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		depthBox.SetAnchorsPreset( LayoutPreset.CenterRight );
		depthBox.OffsetRight = -60; depthBox.OffsetTop = -170; depthBox.GrowHorizontal = GrowDirection.Begin;
		dive.AddChild( depthBox );
		depthNum = Big( depthBox, S( 48 ), new Color( "d6fff7" ) );
		depthSpeed = UiStyle.Text( depthBox, 17, UiStyle.Gold, mono: true );

		gameOver = new ColorRect { Color = new Color( 0, 0, 0, 0.5f ), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
		gameOver.SetAnchorsPreset( LayoutPreset.FullRect );
		AddChild( gameOver );
		var goBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
		goBox.SetAnchorsPreset( LayoutPreset.FullRect );
		gameOver.AddChild( goBox );
		gameOverTitle = Big( goBox, S( 120 ), UiStyle.Red );
		gameOverTitle.Show( "YOU DIED" );
		gameOverDetail = UiStyle.Sub( goBox, S( 26 ), Colors.White );
	}

	static string DiveHelp => $"{Controls.Label( "move" )} tilt to glide   {Controls.Label( "duck" )} tuck   {Controls.Label( "jump" )} spread out";

	public override void _Process( double delta )
	{
		if ( diveHelp is not null && DockHouse.Diving ) diveHelp.Show( DiveHelp );
		var d = Director;
		wave.Show( d is null ? "" : $"{(d.OpenWorld ? "DANGER" : "WAVE")} {d.Wave}" );
		status.Show( StatusText );

		var age = d is null ? 999f : MathF.Min( d.TimeSinceWaveStarted, d.TimeSinceWaveCleared );
		var isStart = d is not null && d.TimeSinceWaveStarted < d.TimeSinceWaveCleared;
		var floatStart = isStart && (d?.FloatRound ?? false);
		var opacity = age < 0.2f ? age / 0.2f : MathX.Clamp( 1f - (age - 2f) / 0.6f, 0f, 1f );
		banner.Visible = opacity > 0f;
		banner.Modulate = new Color( 1, 1, 1, opacity );
		var scale = 1f + MathX.Clamp( 1f - age / 0.25f, 0f, 1f ) * 0.6f;
		banner.PivotOffset = banner.Size * 0.5f;
		banner.Scale = Vector2.One * scale;
		if ( d is not null )
		{
			bannerBig.Show( d.OpenWorld ? (isStart ? $"DANGER {d.Wave}" : "") : floatStart ? "SUIT MALFUNCTION" : isStart ? $"WAVE {d.Wave}" : d.MalfunctionJustEnded ? "SUIT BACK ONLINE" : "WAVE CLEARED" );
			bannerBig.FontColour( floatStart ? new Color( "ffb020" ) : isStart ? new Color( "ff4a3a" ) : new Color( "c8ff7a" ) );
			bannerSmall.Show( d.OpenWorld ? (d.DangerBy == WaveDirector.DangerSource.Time ? "The longer you stay, the worse it gets" : "The ground ahead is getting worse")
				: floatStart ? $"BALLAST ERROR: the suit cannot grip the ground. Jump to rise, crouch to sink"
				: isStart ? (d.KrakenWave ? "The sea bed is shaking. THE KRAKEN is coming up" : d.BossWave ? BossArrives( d.BossKind ) : "They're coming") : d.MalfunctionJustEnded ? "Ballast restored. Boots gripping again" : "Catch your breath" );
		}

		var k = d?.Kills ?? 0;
		if ( k != lastKills ) { lastKills = k; timeSinceKillCount = 0f; }
		kills.Show( k.ToString() );
		var pop = 1f + MathX.Clamp( 1f - timeSinceKillCount / 0.25f, 0f, 1f ) * 0.5f;
		kills.PivotOffset = kills.Size * 0.5f;
		kills.Scale = Vector2.One * pop;

		points.Show( (player?.Points ?? 0).ToString() );
		points.FontColour( (player?.TimeSinceDenied ?? 999f) < 0.4f ? UiStyle.Red : UiStyle.Gold );
		var gainAge = player is null ? 999f : (float)player.TimeSinceGain;
		gain.Modulate = new Color( 1, 1, 1, MathX.Clamp( 1f - (gainAge - 0.5f) / 0.4f, 0f, 1f ) );
		gain.Show( $"+{player?.LastGain ?? 0}" );
		gain.OffsetTop = gain.OffsetBottom = PointsBottom - S( 44 ) - MathF.Min( gainAge, 0.9f ) * 30f * Sc;

		var afflictions = player?.Afflictions;
		var venom = afflictions?.Venomed ?? false;
		var burning = (afflictions?.BurnLeft ?? 0f) > 0f;
		var slowed = (afflictions?.SlowLeft ?? 0f) > 0f;
		healthCaption.Show( "HEALTH" + (venom ? "   VENOM" : "") + (burning ? "   BURNING" : "") + (slowed ? "   SLOWED" : "") );
		var percent = MathF.Round( (player?.Health?.Fraction ?? 0f) * 100f );
		healthCaption.FontColour( venom ? new Color( "6dff4a" ) : burning ? new Color( "ff8a3a" ) : percent <= 30 ? new Color( "ff3b3b" ) : Colors.White );
		lagHealth = percent > lagHealth ? percent : MathX.Lerp( lagHealth, percent, (float)delta * 3f );

		Position = new Vector2( VisorOverlay.Jitter, 0f );   // a big hit knocks the visor readouts sideways
		dive.Visible = DockHouse.Diving;
		if ( DockHouse.Diving )
		{
			diveCount.Show( $"BLOOBERS CRUSHED  {DockHouse.DiveCrushed}" );
			depthNum.Show( $"{DockHouse.DiveDepth:0} m" );
			depthSpeed.Show( $"{DockHouse.DiveVelocity.Length() / U.PerMetre * 3.6f:0} km/h" );
		}
		gameOver.Visible = d?.State == WaveState.GameOver;
		// Dead: nothing on screen but the body and the You Died screen (issues #34 and #40).
		var gone = PlayerSurvivor.LocalDead ? new Color( 1, 1, 1, 0 ) : Colors.White;
		if ( gone != lastGone )
		{
			lastGone = gone;
			SelfModulate = gone;
			foreach ( var child in GetChildren().OfType<CanvasItem>() ) if ( child != gameOver ) child.Modulate = gone;
		}
		if ( d is not null ) gameOverDetail.Show( $"{(d.OpenWorld ? "Reached danger" : "Survived to wave")} {d.Wave} with {d.Kills} kills\nPress {Controls.Label( "jump" ).ToUpperInvariant()} to try again" );
		UpdateGooPhysics( (float)delta );
		QueueRedraw();
	}

	// ---- Goo on the lens: it lags behind how you move and look, then sticks ----

	readonly Dictionary<float, (Vector2 Offset, Vector2 Velocity)> gooMotion = new();
	readonly HashSet<float> gooAlive = new();
	readonly List<float> gooGone = new();
	Color lastGone = new( -1, -1, -1, -1 );
	Angles lastEye;
	bool hasLastEye;

	static float Key( PlayerAfflictions.ScreenDrip d ) => d.Born * 1000f + d.X;

	void UpdateGooPhysics( float dt )
	{
		var controller = player?.Controller;
		var afflictions = player?.Afflictions;
		if ( controller is null || afflictions is null || dt <= 0f ) return;
		var eye = controller.EyeAngles;
		var yawRate = hasLastEye ? Mathf.Wrap( eye.Yaw - lastEye.Yaw, -180f, 180f ) / dt : 0f;      // + turning left
		var pitchRate = hasLastEye ? (eye.Pitch - lastEye.Pitch) / dt : 0f;                           // + looking down
		lastEye = eye; hasLastEye = true;
		// How we move, in the camera's frame (m/s): the dive moves us by script, not by velocity.
		var velocity = DockHouse.Diving ? DockHouse.DiveVelocity * U.In : controller.Velocity;
		var basis = controller.Camera.GlobalBasis;
		var right = velocity.Dot( basis.X );
		var up = velocity.Dot( basis.Y );
		var forward = velocity.Dot( -basis.Z );
		var scale = Size.Y / 1080f;
		var center = Size * 0.5f;

		var alive = gooAlive;
		alive.Clear();
		foreach ( var drip in afflictions.Drips )
		{
			var key = Key( drip );
			alive.Add( key );
			var (offset, vel) = gooMotion.TryGetValue( key, out var m ) ? m : (Vector2.Zero, Vector2.Zero);
			var at = new Vector2( drip.X / 100f * Size.X, drip.Y / 100f * Size.Y ) + offset;
			// Turning drags it the other way, moving sideways or up pushes it opposite, rushing forward spreads it out.
			var accel = new Vector2( yawRate * 2.2f, -pitchRate * 2.2f ) * scale
				+ new Vector2( -right, up ) * 26f * scale
				+ (at - center).Normalized() * MathF.Max( 0f, forward ) * 9f * scale;
			vel += accel * dt;
			vel *= MathF.Exp( -5f * dt );   // thick: it creeps, then sticks
			offset += vel * dt;
			offset = offset.LimitLength( 220f * scale );
			gooMotion[key] = (offset, vel);
		}
		gooGone.Clear();
		foreach ( var key in gooMotion.Keys ) if ( !alive.Contains( key ) ) gooGone.Add( key );
		foreach ( var key in gooGone ) gooMotion.Remove( key );
	}


	string StatusText
	{
		get
		{
			var d = Director;
			if ( d is null ) return "";
			if ( d.OpenWorld && d.State == WaveState.Fighting )
				return d.DangerBy == WaveDirector.DangerSource.Time ? $"{(int)(d.SurvivalSeconds / 60)}:{(int)(d.SurvivalSeconds % 60):00} survived, {d.ZombiesRemaining} enemies near" : $"{d.CellsExplored} areas explored, {d.ZombiesRemaining} enemies near";
			return d.State switch
			{
				WaveState.Intermission when d.Wave == 0 => $"Enemies incoming in {MathF.Ceiling( d.SecondsUntilNextWave )}",
				WaveState.Intermission when d.WaitingForTeleport => "Find the teleporter",
				WaveState.Intermission => $"Next wave in {MathF.Ceiling( d.SecondsUntilNextWave )}",
				WaveState.Fighting => $"{d.ZombiesRemaining} enemies left",
				_ => "",
			};
		}
	}

	public override void _Draw()
	{
		// The health widget (bottom left), in the theme's shape: a panel, a porthole, a sonar screen, a tank or a
		// tentacle. Then the shield's bar over it.
		var frame = new Rect2( Edge, Size.Y - Edge - HullH, HullW, HullH );
		var percent = (player?.Health?.Fraction ?? 0f) * 100f;
		var perks = PlayerPerks.Of( player?.Controller );
		var centre = frame.GetCenter();
		var bob = Theme.Bob ? new Vector2( 0f, MathF.Sin( (float)Clock.Now * 2.1f ) * 3f * Sc ) : Vector2.Zero;
		DrawSetTransformMatrix( Transform2D.Identity.Translated( -centre ).Rotated( MathX.Rad( Theme.Tilt ) ).Translated( centre + bob ) );
		switch ( Theme.Health )
		{
			case HudTheme.HealthLook.Porthole: DrawPorthole( frame, percent ); break;
			case HudTheme.HealthLook.Sonar: DrawSonar( frame, percent ); break;
			case HudTheme.HealthLook.Tank: DrawTank( frame, percent ); break;
			case HudTheme.HealthLook.Tentacle: DrawTentacle( frame, percent ); break;
			default: DrawHealthPanel( frame, percent ); break;
		}
		if ( Theme.TmBadge ) DrawTm( new Vector2( frame.End.X - 8f * Sc, frame.Position.Y + 6f * Sc ) );
		if ( perks is { ShieldMax: > 0f } )
		{
			// As long as the health bar when the shield matches the starting health; longer shields are capped at it.
			var inner = Theme.Health == HudTheme.HealthLook.Panel ? HullW - 40f * Sc : MathF.Max( HullW, 150f * Sc );
			var width = inner * MathF.Min( 1f, perks.ShieldMax / 100f );
			var x = Theme.Health == HudTheme.HealthLook.Panel ? 20f * Sc : 0f;
			UiStyle.DrawPills( this, new Rect2( frame.Position + new Vector2( x, -16f * Sc ), new Vector2( width, 9f * Sc ) ), 10, perks.Shield / perks.ShieldMax, UiStyle.Blue, UiStyle.BlueTop, 1f, -1f, 3f );
		}
		DrawSetTransform( Vector2.Zero, 0f, Vector2.One );

		// The pickups held: their icons in a row over the bars, a count on each that stacks; the blast's key when ready.
		if ( perks is not null )
		{
			var x = Edge + 4f;
			// Above everything else in the corner: the health panel, the points and the "+points" popup over them.
			var y = Size.Y + PointsBottom - S( 44 ) - 30f * Sc - 12f - 40f * Sc;
			foreach ( var entry in PlayerPerks.Table )
			{
				var count = perks.Count( entry.Kind );
				if ( count == 0 ) continue;
				var spent = entry.Kind == PerkKind.Blast && !perks.BlastReady;
				var tint = spent ? new Color( 1f, 1f, 1f, 0.3f ) : Colors.White;
				var rect = new Rect2( x, y, 40f * Sc, 40f * Sc );
				if ( PerkPickup.Icon( entry.Icon ) is { } icon ) DrawTextureRect( icon, rect, false, tint );
				else DrawRect( rect, entry.Colour with { A = tint.A } );
				if ( entry.Kind == PerkKind.Blast )
					UiStyle.DrawTag( this, new Vector2( x - 10f, y - 5f ), spent ? "next wave" : (Controls.IsController ? "RB" : "G") + (perks.BlastsLeft > 1 ? $" x{perks.BlastsLeft}" : ""), 12, UiStyle.TealSoft with { A = tint.A }, HorizontalAlignment.Center, 60f );
				else if ( count > 1 )
				{
					UiStyle.DrawBig( this, new Vector2( x + 18f * Sc, y + 44f * Sc ), $"x{count}", S( 16 ), UiStyle.Gold, HorizontalAlignment.Center, 28f * Sc );
				}
				x += 46f * Sc;
			}
		}

		// Unspent skill tree tokens: a reminder where the pickups sit, with the key that opens the trees.
		if ( PlayerSkills.Of( player?.Controller ) is { Tokens: > 0 } skillTokens )
		{
			var y = Size.Y + PointsBottom - S( 44 ) - 30f * Sc - 12f - 40f * Sc - 26f;
			var pulse = 0.75f + 0.25f * MathF.Sin( (float)Clock.Real * 4f );
			UiStyle.DrawTag( this, new Vector2( Edge + 4f, y ), $"{skillTokens.Tokens} skill token{(skillTokens.Tokens == 1 ? "" : "s")}  ·  {Controls.Label( "pause" )}", 14, UiStyle.Gold with { A = pulse } );
		}

		// A pickup just taken: its name over the middle of the screen for a moment.
		if ( announceAge < 2.2f )
		{
			var alpha = MathX.Clamp( MathF.Min( (float)announceAge / 0.15f, (2.2f - (float)announceAge) / 0.5f ), 0f, 1f );
			UiStyle.DrawBig( this, new Vector2( 0f, Size.Y * 0.68f ), announceText, S( 34 ), announceColour, HorizontalAlignment.Center, Size.X, alpha );
		}

		// Danger bar (open world).
		var d = Director;
		if ( d is { OpenWorld: true } )
		{
			var bar = new Vector2( Size.X * 0.5f - 160f * Sc, 32 + (60 + 30) * Sc + 8 );
			var hot = UiStyle.Lime.Lerp( new Color( "ff5a3c" ), d.DangerProgress );
			UiStyle.DrawPills( this, new Rect2( bar, new Vector2( 320, 12 ) * Sc ), 12, d.DangerProgress, hot, hot.Lightened( 0.5f ) );
		}

		// The dive: darkening edges the deeper you go, speed lines rushing out, and the depth bar.
		if ( DockHouse.Diving )
		{
			var c = Size * 0.5f;
			var now = (float)Clock.Now;
			var dark = DockHouse.DiveProgress * 0.3f;
			for ( var r = 0; r < 12; r++ )
			{
				var k = r / 12f;
				DrawArc( c, Size.Length() * (0.3f + 0.25f * k), 0f, MathF.Tau, 64, new Color( 0f, 0.03f, 0.08f, dark * k * 0.35f ), Size.Length() * 0.03f );
			}
			for ( var i = 0; i < 26; i++ )
			{
				var angle = MathX.Rad( i * (360f / 26f) + (i % 3) * 5f );
				var distance = 150f + ((now * 1600f + i * 157f) % 320f);
				var length = 70f + (i % 4) * 30f;
				var dir = new Vector2( MathF.Sin( angle ), -MathF.Cos( angle ) );
				DrawLine( c + dir * distance, c + dir * (distance + length), new Color( 1, 1, 1, 0.35f * 0.6f ), 2f );
			}
			var bar = new Vector2( Size.X - 60 - 60, Size.Y * 0.5f - 60 );
			DrawRect( new Rect2( bar, new Vector2( 10, 240 ) ), new Color( "d6fff7" ) with { A = 0.15f } );
			DrawRect( new Rect2( bar, new Vector2( 10, 240 * DockHouse.DiveProgress ) ), new Color( "5fe3ff" ) );
		}

		// Goo splatted on the camera by close kills (port of the s&box HUD): soft blobs that pop in, leave a streak
		// running down the lens, throw a few flecks, and fade after a moment. (They were hard flat circles.)
		var afflictions = player?.Afflictions;
		if ( afflictions is not null && afflictions.Drips.Count > 0 )
		{
			var now = (float)Clock.Now;
			var scale = Size.Y / 1080f;   // s&box UI px are 1080p px
			foreach ( var drip in afflictions.Drips )
			{
				var age = now - drip.Born;
				var fade = MathX.Clamp( 1f - (age - 0.6f) / (PlayerAfflictions.DripLife - 0.6f), 0f, 1f );
				var pop = MathX.Clamp( age / 0.06f, 0.3f, 1f );
				var size = drip.Size * pop * scale;
				var slide = MathF.Max( 0f, age - 0.25f ) * drip.Slide;   // % of the screen height
				var c = drip.Color;
				var moved = gooMotion.TryGetValue( Key( drip ), out var motion ) ? motion.Offset : Vector2.Zero;
				var x = drip.X / 100f * Size.X + moved.X;
				var y = drip.Y / 100f * Size.Y + moved.Y;
				if ( slide > 0.3f )
				{
					var w = size * 0.28f;
					DrawTextureRect( Streak, new Rect2( x - w * 0.5f, y, w, slide / 100f * Size.Y ), false, c with { A = fade * 0.55f * 0.8f } );
				}
				// The splat itself: one of the Blender renders (Tools/blender/goo_splats.py), tinted to the goo,
				// tilted a little, sliding down the lens. The renders carry their own specks and drips.
				var r = new Random( (int)(drip.Born * 1000f) ^ (int)(drip.X * 37f) );
				var tex = Splats[r.Next( Splats.Length )];
				var big = size * 2.4f;
				var tilt = (float)(r.NextDouble() - 0.5) * 0.5f;
				DrawSetTransform( new Vector2( x, y + slide / 100f * Size.Y ), tilt, Vector2.One );
				DrawTextureRect( tex, new Rect2( -big * 0.5f, -big * 0.42f, big, big ), false, c with { A = fade * 0.95f } );
				DrawSetTransform( Vector2.Zero, 0f, Vector2.One );
			}
		}
	}

	// ---- The health widgets ----

	bool Venomed => player?.Afflictions?.Venomed ?? false;
	string States => (Venomed ? "  VENOM" : "") + ((player?.Afflictions?.BurnLeft ?? 0f) > 0f ? "  BURNING" : "") + ((player?.Afflictions?.SlowLeft ?? 0f) > 0f ? "  SLOWED" : "");
	string ShownHealth => player?.Health is { } hp ? MathF.Ceiling( hp.Current ).ToString( "0" ) : "0";
	Color CaptionColour( bool low, Color normal ) => Venomed ? new Color( "6dff4a" ) : (player?.Afflictions?.BurnLeft ?? 0f) > 0f ? new Color( "ff8a3a" ) : low ? UiStyle.Red : normal;
	Color Fill( bool low ) => Venomed ? new Color( "3fc22a" ) : low && Theme.HealthFill != Theme.Red ? UiStyle.Red : Theme.HealthFill;
	Color FillTop => Venomed ? new Color( "b8ff9a" ) : Theme.HealthTop;
	static bool Blink => ((int)(Clock.Now * 2.5f) & 1) == 0;

	/// <summary> A warning line under the widget when low (themes with a breach warning). </summary>
	void DrawBreach( Vector2 at, bool low, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f )
	{
		if ( Theme.Breach is { } words && low && Blink ) UiStyle.DrawTag( this, at, words, S( 12 ), UiStyle.Red, align, width, true );
	}

	/// <summary> The panel: the caption and any states, the number big on the right, a bar of ten segments. </summary>
	void DrawHealthPanel( Rect2 frame, float percent )
	{
		var low = percent <= 30;
		UiStyle.DrawFrame( this, frame, 1f, low ? UiStyle.Red : null, Sc );
		UiStyle.DrawTag( this, frame.Position + new Vector2( 20f, 30f ) * Sc, Theme.HealthWord + States, S( 15 ), CaptionColour( low, Theme.PanelCaption ), onPanel: true );
		if ( Theme.Breach is not null ) DrawBreach( frame.Position + new Vector2( 20f, 46f ) * Sc, low );
		UiStyle.DrawBig( this, frame.Position + new Vector2( 0f, 38f * Sc ), ShownHealth, S( 36 ), low ? UiStyle.Red : Theme.PanelNumber, HorizontalAlignment.Right, HullW - 20f * Sc, onPanel: true );
		UiStyle.DrawPills( this, new Rect2( frame.Position + new Vector2( 20f, 50f ) * Sc, new Vector2( HullW - 40f * Sc, 14f * Sc ) ), 10, percent / 100f, Fill( low ), FillTop, 1f, lagHealth / 100f );
		if ( finePrint is not null )
			UiStyle.DrawTag( this, new Vector2( frame.Position.X + 4f, frame.End.Y + 17f * Sc ), finePrint, S( 14 ), Colors.White, HorizontalAlignment.Left, -1f, true, false );
	}

	/// <summary> Ten arc segments round a centre, from `from` to `to` degrees clockwise from the top. </summary>
	void DrawRing( Vector2 c, float radius, float width, float fraction, Color fill, Color empty, float from = -140f, float to = 140f, float gapDegrees = 4f )
	{
		const int n = 10;
		var step = (to - from) / n;
		var lag = lagHealth / 100f;
		for ( var i = 0; i < n; i++ )
		{
			var a0 = MathX.Rad( from + i * step + gapDegrees * 0.5f - 90f );
			var a1 = MathX.Rad( from + (i + 1) * step - gapDegrees * 0.5f - 90f );
			var full = MathX.Clamp( fraction * n - i, 0f, 1f );
			var lagged = MathX.Clamp( lag * n - i, 0f, 1f );
			DrawArc( c, radius, a0, a1, 12, empty, width, true );
			if ( lagged > full ) DrawArc( c, radius, a0, a0 + (a1 - a0) * lagged, 12, new Color( 1f, 0.8f, 0.8f, 0.6f ), width, true );
			if ( full > 0f ) DrawArc( c, radius, a0, a0 + (a1 - a0) * full, 12, fill, width, true );
		}
	}

	/// <summary> Brass: a riveted porthole with the health lights round the glass and the number in it. </summary>
	void DrawPorthole( Rect2 frame, float percent )
	{
		var low = percent <= 30;
		var c = frame.GetCenter();
		var r = frame.Size.X * 0.5f;
		DrawCircle( c + new Vector2( 0f, 4f * Sc ), r, UiStyle.Ink with { A = 0.6f } );
		DrawCircle( c, r, new Color( "5e3e10" ) );
		DrawCircle( c, r - 2f * Sc, new Color( "c4913a" ) );
		DrawArc( c, r - 5f * Sc, MathX.Rad( 200f ), MathX.Rad( 290f ), 16, new Color( "fff0c0" ) with { A = 0.7f }, 3f * Sc, true );
		for ( var i = 0; i < 10; i++ )
		{
			var a = MathX.Rad( i * 36f + 18f - 90f );
			var at = c + new Vector2( MathF.Cos( a ), MathF.Sin( a ) ) * (r * 0.88f);
			DrawCircle( at, 3.2f * Sc, new Color( "5a3a0c" ) );
			DrawCircle( at, 2.2f * Sc, new Color( "fff1c4" ) );
		}
		var glass = r * 0.78f;
		DrawCircle( c, glass + 2f * Sc, new Color( "3a2606" ) );
		DrawCircle( c, glass, new Color( "0b3446" ) );
		DrawCircle( c - new Vector2( glass * 0.15f, glass * 0.15f ), glass * 0.75f, new Color( "174f63" ) with { A = 0.6f } );
		if ( low ) DrawArc( c, glass, 0f, MathF.Tau, 48, UiStyle.Red with { A = Blink ? 0.9f : 0.4f }, 2.5f * Sc, true );
		DrawRing( c, glass * 0.84f, 7f * Sc, percent / 100f, Fill( low ), new Color( 1f, 1f, 1f, 0.12f ) );
		UiStyle.DrawBig( this, c + new Vector2( -r, 12f * Sc ), ShownHealth, S( 40 ), low ? UiStyle.Red : UiStyle.Gold, HorizontalAlignment.Center, r * 2f );
		UiStyle.DrawTag( this, c + new Vector2( -r, glass * 0.55f ), Theme.HealthWord + States, S( 12 ), CaptionColour( low, Theme.Readout ), HorizontalAlignment.Center, r * 2f );
		DrawBreach( new Vector2( frame.Position.X, frame.End.Y + 18f * Sc ), low, HorizontalAlignment.Center, frame.Size.X );
	}

	/// <summary> Sonar: a round screen with a sweep and the enemies near you as blips, health round the rim. </summary>
	void DrawSonar( Rect2 frame, float percent )
	{
		var low = percent <= 30;
		var c = frame.GetCenter();
		var r = frame.Size.X * 0.5f - 8f * Sc;
		var green = Theme.Accent;
		DrawCircle( c, r + 5f * Sc, new Color( "0b2a24" ) );
		DrawCircle( c, r, new Color( "021a16" ) with { A = 0.92f } );
		for ( var k = 1; k <= 3; k++ ) DrawArc( c, r * k / 4f, 0f, MathF.Tau, 40, green with { A = 0.2f }, 1f, true );
		DrawLine( c - new Vector2( r, 0f ), c + new Vector2( r, 0f ), green with { A = 0.15f }, 1f );
		DrawLine( c - new Vector2( 0f, r ), c + new Vector2( 0f, r ), green with { A = 0.15f }, 1f );
		// The sweep: a fading wedge behind the turning line.
		var now = (float)Clock.Now;
		var sweep = now * MathF.Tau / 3f;
		for ( var i = 0; i < 12; i++ )
		{
			var a0 = sweep - i * 0.06f;
			var a1 = a0 - 0.06f;
			DrawColoredPolygon( new[] { c, c + Vector2.FromAngle( a0 ) * r, c + Vector2.FromAngle( a1 ) * r }, green with { A = 0.35f * (1f - i / 12f) } );
		}
		// Blips: enemies within 30 m, turned to where you look (up is ahead).
		var controller = player?.Controller;
		if ( controller is not null )
		{
			var me = controller.GlobalPosition;
			var yaw = MathX.Rad( controller.EyeAngles.Yaw );
			const float range = 30f;
			foreach ( var enemy in CreatureRegistry.Alive() )
			{
				var d = enemy.GlobalPosition - me;
				var flat = new Vector2( d.X, d.Z ) / U.PerMetre;
				if ( flat.Length() > range ) continue;
				var local = flat.Rotated( yaw );
				var at = c + local / range * r;
				var age = Mathf.Wrap( sweep - MathF.Atan2( at.Y - c.Y, at.X - c.X ), 0f, MathF.Tau );
				UiStyle.Glow( this, at, 2.6f * Sc, new Color( "ff5a6e" ), MathX.Clamp( 1f - age / MathF.Tau, 0.25f, 1f ) );
			}
		}
		DrawRing( c, r + 12f * Sc, 5f * Sc, percent / 100f, Fill( low ), green with { A = 0.15f }, 0f, 360f );
		UiStyle.DrawBig( this, c + new Vector2( -r, 8f * Sc ), ShownHealth, S( 40 ), low ? UiStyle.Red : Theme.Number, HorizontalAlignment.Center, r * 2f );
		UiStyle.DrawTag( this, c + new Vector2( -r, 26f * Sc ), Theme.HealthWord + States, S( 12 ), CaptionColour( low, Theme.Readout ), HorizontalAlignment.Center, r * 2f, true );
	}

	/// <summary> Fish tank: a glass tank with the health as water that sloshes as you move, pebbles on the bottom. </summary>
	void DrawTank( Rect2 frame, float percent )
	{
		var low = percent <= 30;
		if ( tankGlass is null || tankGlassScale != Sc )
		{
			tankGlassScale = Sc;
			tankGlass = new StyleBoxFlat { BgColor = new Color( 0.04f, 0.16f, 0.24f, 0.4f ), BorderColor = new Color( 0.86f, 0.98f, 1f, 0.85f ), AntiAliasing = true };
			tankGlass.SetBorderWidthAll( Math.Max( 2, (int)(4f * Sc) ) );
			tankGlass.CornerRadiusTopLeft = tankGlass.CornerRadiusTopRight = (int)(6f * Sc);
			tankGlass.CornerRadiusBottomLeft = tankGlass.CornerRadiusBottomRight = (int)(20f * Sc);
		}
		DrawStyleBox( tankGlass, frame );
		var inner = frame.Grow( -4f * Sc );
		var now = (float)Clock.Now;
		var speed = player?.Controller?.Velocity.Length() ?? 0f;
		var slosh = 2.5f * Sc + MathF.Min( speed * 0.4f, 6f ) * Sc;
		void Water( float level, Color colour )
		{
			if ( level <= 0f ) return;
			var top = inner.End.Y - inner.Size.Y * level;
			var points = waterPoints;
			for ( var i = 0; i <= 12; i++ )
			{
				var x = inner.Position.X + inner.Size.X * i / 12f;
				points[i] = new Vector2( x, MathF.Min( inner.End.Y, top + MathF.Sin( now * 4f + i * 0.9f ) * slosh ) );
			}
			points[13] = new Vector2( inner.End.X, inner.End.Y );
			points[14] = new Vector2( inner.Position.X, inner.End.Y );
			DrawColoredPolygon( points, colour );
		}
		Water( lagHealth / 100f, new Color( 1f, 0.8f, 0.75f, 0.45f ) );
		Water( percent / 100f, Fill( low ) with { A = 0.9f } );
		for ( var i = 0; i < 5; i++ )
			DrawCircle( new Vector2( inner.Position.X + inner.Size.X * (0.15f + i * 0.18f), inner.End.Y - 2f * Sc ), (3f + (i % 3)) * Sc, new Color( "d9c89a" ).Darkened( (i % 2) * 0.2f ) );
		DrawRect( new Rect2( inner.Position + new Vector2( 5f * Sc, 4f * Sc ), new Vector2( 4f * Sc, inner.Size.Y * 0.7f ) ), new Color( 1f, 1f, 1f, 0.18f ) );
		var mid = frame.GetCenter();
		UiStyle.DrawBig( this, new Vector2( frame.Position.X - 20f, mid.Y + 8f * Sc ), ShownHealth, S( 40 ), low ? UiStyle.Red : Colors.White, HorizontalAlignment.Center, frame.Size.X + 40f );
		UiStyle.DrawTag( this, new Vector2( frame.Position.X - 30f, mid.Y + 26f * Sc ), Theme.HealthWord + States, S( 12 ), CaptionColour( low, Theme.AccentSoft ), HorizontalAlignment.Center, frame.Size.X + 60f );
	}

	/// <summary> Deep Glow: health lights running along a tentacle, the number glowing at its root. </summary>
	void DrawTentacle( Rect2 frame, float percent )
	{
		var low = percent <= 30;
		var p0 = frame.Position + new Vector2( 0.04f, 0.86f ) * frame.Size;
		var p1 = frame.Position + new Vector2( 0.3f, 0.9f ) * frame.Size;
		var p2 = frame.Position + new Vector2( 0.45f, 0.3f ) * frame.Size;
		var p3 = frame.Position + new Vector2( 0.96f, 0.08f ) * frame.Size;
		var now = (float)Clock.Now;
		Vector2 At( float t )
		{
			var u = 1f - t;
			var sway = new Vector2( 0f, MathF.Sin( now * 1.6f + t * 4f ) * 4f * Sc * t );
			return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3 + sway;
		}
		var line = new Vector2[24];
		for ( var i = 0; i < line.Length; i++ ) line[i] = At( i / (line.Length - 1f) );
		DrawPolyline( line, Theme.Accent with { A = 0.35f }, 3f * Sc, true );
		var beat = low ? 9f : 4.5f;   // the glow pulses faster at low health, like a heartbeat
		for ( var i = 0; i < 10; i++ )
		{
			var at = At( 0.14f + 0.84f * i / 9f );
			var full = MathX.Clamp( percent / 10f - i, 0f, 1f );
			if ( full <= 0f ) { DrawCircle( at, 4f * Sc, new Color( "20333d" ) ); continue; }
			var pulse = 1f + 0.18f * MathF.Sin( now * beat - i * 0.4f );
			UiStyle.Glow( this, at, 6f * Sc * pulse, Fill( low ), full );
		}
		var root = frame.Position + new Vector2( 6f * Sc, frame.Size.Y - 4f * Sc );
		UiStyle.DrawBig( this, root, ShownHealth, S( 40 ), low ? UiStyle.Red : Theme.Number );
		UiStyle.DrawTag( this, root + new Vector2( UiStyle.BigWidth( ShownHealth, S( 40 ) ) + 8f * Sc, 0f ), Theme.HealthWord + States, S( 12 ), CaptionColour( low, Theme.AccentSoft ), HorizontalAlignment.Left, -1f, true );
	}

	// Reused by the per-frame draws below (they built new ones every frame).
	StyleBoxFlat tankGlass, tmBox;
	float tankGlassScale;
	readonly Vector2[] waterPoints = new Vector2[15];

	/// <summary> A little ™ sticker, tilted. </summary>
	void DrawTm( Vector2 at )
	{
		if ( tmBox is null ) { tmBox = new StyleBoxFlat { BgColor = Colors.White, AntiAliasing = true, ShadowColor = new Color( 0f, 0f, 0f, 0.4f ), ShadowSize = 2, ShadowOffset = new Vector2( 1f, 2f ) }; tmBox.SetCornerRadiusAll( 5 ); }
		var box = tmBox;
		var size = new Vector2( 26f, 20f ) * Sc;
		var r = new Rect2( at - size * 0.5f, size );
		DrawStyleBox( box, r );
		DrawString( UiStyle.Display, new Vector2( r.Position.X, r.End.Y - 4f * Sc ), "™", HorizontalAlignment.Center, size.X, S( 16 ), new Color( "e53b3b" ) );
	}

	/// <summary> The boss wave's line under its banner: who is coming. </summary>
	public static string BossArrives( WaveDirector.EnemyKind kind ) => kind switch
	{
		WaveDirector.EnemyKind.Lighthouse => "THE LIGHTHOUSE HERMIT comes ashore. Mind the light",
		WaveDirector.EnemyKind.KingRaymond => "KING RAYMOND IV glides in. Bow down",
		WaveDirector.EnemyKind.Davey => "DAVEY JONES has come for your soul",
		WaveDirector.EnemyKind.Poseidon => "POSEIDON rises to defend his city",
		WaveDirector.EnemyKind.Queen => "THE MERMAID QUEEN is singing. Don't listen",
		WaveDirector.EnemyKind.Warden => "THE BLIND WARDEN hunts by sound. Stay quiet",
		WaveDirector.EnemyKind.Leviathan => "THE LEVIATHAN is coming down through the skylight",
		_ => "MC-51 has entered the arena",
	};
}
