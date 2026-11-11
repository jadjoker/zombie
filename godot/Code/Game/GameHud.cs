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
	int lastKills;
	TimeSince timeSinceKillCount = 999f;

	public static GameHud Create( PlayerSurvivor player )
	{
		var hud = new GameHud { player = player, MouseFilter = MouseFilterEnum.Ignore };
		hud.SetAnchorsPreset( LayoutPreset.FullRect );
		return hud;
	}

	static Label Text( Control parent, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Center )
	{
		var label = new Label { HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore };
		label.AddThemeFontSizeOverride( "font_size", size );
		label.AddThemeColorOverride( "font_color", color );
		label.AddThemeColorOverride( "font_outline_color", new Color( 0, 0, 0, 0.7f ) );
		label.AddThemeConstantOverride( "outline_size", Math.Max( 4, size / 8 ) );
		parent.AddChild( label );
		return label;
	}

	public override void _Ready()
	{
		var top = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		top.SetAnchorsPreset( LayoutPreset.CenterTop );
		top.OffsetTop = 32; top.GrowHorizontal = GrowDirection.Both;
		AddChild( top );
		wave = Text( top, 44, new Color( "c8ff7a" ) );
		status = Text( top, 20, new Color( 1, 1, 1, 0.85f ) );

		banner = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		banner.SetAnchorsPreset( LayoutPreset.CenterTop );
		banner.AnchorTop = 0.28f; banner.GrowHorizontal = GrowDirection.Both;
		AddChild( banner );
		bannerBig = Text( banner, 110, Colors.White );
		bannerSmall = Text( banner, 26, new Color( 1, 1, 1, 0.85f ) );

		var killBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		killBox.SetAnchorsPreset( LayoutPreset.TopRight );
		killBox.OffsetTop = 32; killBox.OffsetRight = -40; killBox.GrowHorizontal = GrowDirection.Begin;
		AddChild( killBox );
		kills = Text( killBox, 40, Colors.White, HorizontalAlignment.Right );
		killsCaption = Text( killBox, 14, new Color( 1, 1, 1, 0.7f ), HorizontalAlignment.Right );
		killsCaption.Text = "KILLS";

		points = Text( this, 36, new Color( "ffd24a" ), HorizontalAlignment.Left );
		points.SetAnchorsPreset( LayoutPreset.BottomLeft );
		points.OffsetLeft = 40; points.OffsetTop = points.OffsetBottom = -84; points.GrowVertical = GrowDirection.Begin;
		gain = Text( this, 22, new Color( "ffe58a" ), HorizontalAlignment.Left );
		gain.SetAnchorsPreset( LayoutPreset.BottomLeft );
		gain.OffsetLeft = 40; gain.OffsetTop = gain.OffsetBottom = -128; gain.GrowVertical = GrowDirection.Begin;

		healthCaption = Text( this, 16, new Color( 1, 1, 1, 0.8f ), HorizontalAlignment.Left );
		healthCaption.SetAnchorsPreset( LayoutPreset.BottomLeft );
		healthCaption.OffsetLeft = 40; healthCaption.OffsetTop = healthCaption.OffsetBottom = -58; healthCaption.GrowVertical = GrowDirection.Begin;

		// The dive: crushed counter and controls up top, depth meter on the right.
		dive = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
		dive.SetAnchorsPreset( LayoutPreset.FullRect );
		AddChild( dive );
		var diveTop = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		diveTop.SetAnchorsPreset( LayoutPreset.CenterTop );
		diveTop.OffsetTop = 110; diveTop.GrowHorizontal = GrowDirection.Both;
		dive.AddChild( diveTop );
		diveCount = Text( diveTop, 26, new Color( "d6fff7" ) );
		diveHelp = Text( diveTop, 14, new Color( 1, 1, 1, 0.7f ) );
		diveHelp.Text = "WASD tilt to glide   Ctrl tuck   Space spread out";
		var depthBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		depthBox.SetAnchorsPreset( LayoutPreset.CenterRight );
		depthBox.OffsetRight = -60; depthBox.OffsetTop = -170; depthBox.GrowHorizontal = GrowDirection.Begin;
		dive.AddChild( depthBox );
		depthNum = Text( depthBox, 44, new Color( "d6fff7" ) );
		depthSpeed = Text( depthBox, 16, new Color( "ffd24a" ) );

		gameOver = new ColorRect { Color = new Color( 0, 0, 0, 0.5f ), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
		gameOver.SetAnchorsPreset( LayoutPreset.FullRect );
		AddChild( gameOver );
		var goBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
		goBox.SetAnchorsPreset( LayoutPreset.FullRect );
		gameOver.AddChild( goBox );
		gameOverTitle = Text( goBox, 96, new Color( "ff3b3b" ) );
		gameOverTitle.Text = "YOU DIED";
		gameOverDetail = Text( goBox, 26, Colors.White );
	}

	public override void _Process( double delta )
	{
		var d = Director;
		wave.Text = d is null ? "" : $"{(d.OpenWorld ? "DANGER" : "WAVE")} {d.Wave}";
		status.Text = StatusText;

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
			bannerBig.Text = d.OpenWorld ? (isStart ? $"DANGER {d.Wave}" : "") : floatStart ? "ANCHOR SNAPPED" : isStart ? $"WAVE {d.Wave}" : "WAVE CLEARED";
			bannerBig.AddThemeColorOverride( "font_color", isStart ? new Color( "ff4a3a" ) : new Color( "c8ff7a" ) );
			bannerSmall.Text = d.OpenWorld ? (d.DangerBy == WaveDirector.DangerSource.Time ? "The longer you stay, the worse it gets" : "The ground ahead is getting worse")
				: floatStart ? $"Wave {d.Wave}: you're floating. Jump to rise, crouch to sink"
				: isStart ? (d.KrakenWave ? "The sea bed is shaking. THE KRAKEN is coming up" : d.BossWave ? "MC61 has entered the arena" : "They're coming") : "Catch your breath";
		}

		var k = d?.Kills ?? 0;
		if ( k != lastKills ) { lastKills = k; timeSinceKillCount = 0f; }
		kills.Text = k.ToString();
		var pop = 1f + MathX.Clamp( 1f - timeSinceKillCount / 0.25f, 0f, 1f ) * 0.5f;
		kills.PivotOffset = kills.Size * 0.5f;
		kills.Scale = Vector2.One * pop;

		points.Text = (player?.Points ?? 0).ToString();
		points.AddThemeColorOverride( "font_color", (player?.TimeSinceDenied ?? 999f) < 0.4f ? new Color( "ff3b3b" ) : new Color( "ffd24a" ) );
		var gainAge = player is null ? 999f : (float)player.TimeSinceGain;
		gain.Modulate = new Color( 1, 1, 1, MathX.Clamp( 1f - (gainAge - 0.5f) / 0.4f, 0f, 1f ) );
		gain.Text = $"+{player?.LastGain ?? 0}";
		gain.OffsetTop = gain.OffsetBottom = -84 - 44 - MathF.Min( gainAge, 0.9f ) * 30f;

		var afflictions = player?.Afflictions;
		var venom = afflictions?.Venomed ?? false;
		var burning = (afflictions?.BurnLeft ?? 0f) > 0f;
		var slowed = (afflictions?.SlowLeft ?? 0f) > 0f;
		healthCaption.Text = "HEALTH" + (venom ? "   VENOM" : "") + (burning ? "   BURNING" : "") + (slowed ? "   SLOWED" : "");
		var percent = MathF.Round( (player?.Health?.Fraction ?? 0f) * 100f );
		healthCaption.AddThemeColorOverride( "font_color", venom ? new Color( "6dff4a" ) : burning ? new Color( "ff8a3a" ) : percent <= 30 ? new Color( "ff3b3b" ) : Colors.White );
		lagHealth = percent > lagHealth ? percent : MathX.Lerp( lagHealth, percent, (float)delta * 3f );

		dive.Visible = DockHouse.Diving;
		if ( DockHouse.Diving )
		{
			diveCount.Text = $"BLOOBERS CRUSHED  {DockHouse.DiveCrushed}";
			depthNum.Text = $"{DockHouse.DiveDepth:0} m";
			depthSpeed.Text = $"{DockHouse.DiveVelocity.Length() / U.PerMetre * 3.6f:0} km/h";
		}
		gameOver.Visible = d?.State == WaveState.GameOver;
		if ( d is not null ) gameOverDetail.Text = $"{(d.OpenWorld ? "Reached danger" : "Survived to wave")} {d.Wave} with {d.Kills} kills\nPress JUMP to try again";
		UpdateGooPhysics( (float)delta );
		QueueRedraw();
	}

	// ---- Goo on the lens: it lags behind how you move and look, then sticks ----

	readonly Dictionary<float, (Vector2 Offset, Vector2 Velocity)> gooMotion = new();
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

		var alive = new HashSet<float>();
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
		foreach ( var key in gooMotion.Keys.Where( k => !alive.Contains( k ) ).ToList() ) gooMotion.Remove( key );
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
				WaveState.Intermission => $"Next wave in {MathF.Ceiling( d.SecondsUntilNextWave )}",
				WaveState.Fighting => $"{d.ZombiesRemaining} enemies left",
				_ => "",
			};
		}
	}

	public override void _Draw()
	{
		// Health bar: dark track, pale lag, red (green when venomed) fill.
		var at = new Vector2( 40, Size.Y - 40 - 16 );
		var percent = (player?.Health?.Fraction ?? 0f) * 100f;
		var venom = player?.Afflictions?.Venomed ?? false;
		DrawRect( new Rect2( at, new Vector2( 320, 16 ) ), new Color( 0, 0, 0, 0.5f ) );
		DrawRect( new Rect2( at, new Vector2( 320 * lagHealth / 100f, 16 ) ), new Color( 1, 1, 1, 0.6f ) );
		DrawRect( new Rect2( at, new Vector2( 320 * percent / 100f, 16 ) ), venom ? new Color( "5fe03a" ) : new Color( "e8443a" ) );

		// Danger bar (open world).
		var d = Director;
		if ( d is { OpenWorld: true } )
		{
			var bar = new Vector2( Size.X * 0.5f - 160, 32 + 44 + 30 + 8 );
			DrawRect( new Rect2( bar, new Vector2( 320, 10 ) ), new Color( 0, 0, 0, 0.5f ) );
			DrawRect( new Rect2( bar, new Vector2( 320 * d.DangerProgress, 10 ) ), new Color( "c8ff7a" ).Lerp( new Color( "ff5a3c" ), d.DangerProgress ) );
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
}
