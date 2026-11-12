using Godot;

/// <summary>
/// The rest of the s&box ZombieHud: screen-edge glows for hurt, venom, burn and slow, ink blots, the stamina pips
/// and jetpack power bar under the crosshair, the slam flash, surge speed lines, the boss bar with MC61's shouting,
/// and the buy station prompt. Pixel sizes are the s&box ones at 1080p.
/// </summary>
public partial class HudOverlays : Control
{
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
		speech = Text( this, 64, new Color( "ff4fd8" ) );
		speech.AnchorLeft = 0f; speech.AnchorRight = 1f; speech.AnchorTop = speech.AnchorBottom = 0.26f;
		speech.OffsetLeft = speech.OffsetRight = 0f; speech.OffsetBottom = 80f;
		speech.AddThemeColorOverride( "font_outline_color", new Color( 1f, 0f, 0.67f, 0.6f ) );
		speech.AddThemeConstantOverride( "outline_size", 14 );

		prompt = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
		prompt.AddThemeStyleboxOverride( "panel", new StyleBoxFlat { BgColor = new Color( 0, 0, 0, 0.55f ), CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8, ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 10, ContentMarginBottom = 10 } );
		prompt.SetAnchorsPreset( LayoutPreset.CenterTop );
		prompt.AnchorTop = prompt.AnchorBottom = 0.58f; prompt.GrowHorizontal = GrowDirection.Both;
		AddChild( prompt );
		var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		row.AddThemeConstantOverride( "separation", 14 );
		prompt.AddChild( row );
		promptKey = Text( row, 26, Colors.White );
		promptKey.Text = " E ";
		var textBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		row.AddChild( textBox );
		promptTitle = Text( textBox, 22, Colors.White, HorizontalAlignment.Left );
		promptDesc = Text( textBox, 15, new Color( 1, 1, 1, 0.7f ), HorizontalAlignment.Left );
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
		var wantStamina = m is not null && (m.Stamina < m.MaxStamina - 0.01f || m.TimeSinceOutOfStamina < 0.6f) ? 1f : 0f;
		staminaShown = MathX.Approach( staminaShown, wantStamina, dt * (wantStamina > 0f ? 8f : 1.5f) );

		var b = Boss;
		boss.Visible = b is not null;
		if ( b is not null && b.TimeSinceLine < 2.4f && !string.IsNullOrEmpty( b.Line ) )
		{
			speech.Visible = true;
			speech.Text = b.Line;
			speech.Modulate = new Color( 1, 1, 1, MathX.Clamp( (2.4f - b.TimeSinceLine) / 0.5f, 0f, 1f ) );
			speech.PivotOffset = speech.Size * 0.5f;
			speech.Scale = Vector2.One * (1f + MathX.Clamp( 1f - b.TimeSinceLine / 0.2f, 0f, 1f ) * 0.5f);
			speech.Rotation = MathX.Rad( MathF.Sin( now * 30f ) * 2f );
		}
		else speech.Visible = false;

		var use = player?.Controller?.GetNodeOrNull<PlayerUse>( "PlayerUse" );
		var station = use?.Hovered;
		prompt.Visible = station is not null || (use?.AtDivingBoard ?? false);
		if ( station is null && (use?.AtDivingBoard ?? false) )
		{
			promptTitle.Text = "Dive in";
			promptDesc.Text = "Head down to the sea floor";
			promptTitle.AddThemeColorOverride( "font_color", Colors.White );
		}
		if ( station is not null )
		{
			var afford = !station.SoldOut && (player?.Points ?? 0) >= station.CurrentCost;
			promptTitle.Text = $"{station.Title}  {(station.SoldOut ? "Sold out" : station.CurrentCost.ToString())}";
			promptDesc.Text = station.Description;
			promptTitle.AddThemeColorOverride( "font_color", afford ? Colors.White : new Color( "ff6a6a" ) );
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
			var count = m.MaxStamina;
			var start = new Vector2( c.X - (count * 30f - 4f) * 0.5f, c.Y + 34f );
			for ( var i = 0; i < count; i++ )
			{
				var fill = MathX.Clamp( m.Stamina - i, 0f, 1f );
				var at = start + new Vector2( i * 30f, 0f );
				DrawRect( new Rect2( at, new Vector2( 26f, 5f ) ), (empty ? new Color( "ff3b3b" ) : new Color( 0, 0, 0 )) with { A = 0.45f * staminaShown } );
				DrawRect( new Rect2( at, new Vector2( 26f * fill, 5f ) ), (fill >= 1f ? Colors.White : new Color( 1, 1, 1, 0.55f )) with { A = (fill >= 1f ? 0.9f : 0.55f) * staminaShown } );
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

		// Boss health bar.
		var b = Boss;
		if ( b is not null )
		{
			var at = new Vector2( Size.X * 0.3f, 96f + 34f );
			var width = Size.X * 0.4f;
			DrawRect( new Rect2( at, new Vector2( width, 14f ) ), new Color( 0, 0, 0, 0.55f ) );
			DrawRect( new Rect2( at, new Vector2( width * b.HealthFraction, 14f ) ), new Color( "ff4fd8" ) );
			DrawString( ThemeDB.FallbackFont, new Vector2( at.X, 96f + 22f ), "M C 6 1", HorizontalAlignment.Center, width, 22, new Color( "ff4fd8" ) );
		}
		else if ( Kraken.All.FirstOrDefault( k => !k.IsDead ) is { Health: not null } kraken )
		{
			var green = new Color( "58ff6a" );
			var at = new Vector2( Size.X * 0.3f, 96f + 62f );
			var width = Size.X * 0.4f;
			DrawRect( new Rect2( at, new Vector2( width, 14f ) ), new Color( 0, 0, 0, 0.55f ) );
			DrawRect( new Rect2( at, new Vector2( width * kraken.Health.Fraction, 14f ) ), green );
			if ( kraken.HelmetOn )
			{
				// The glass dome over its brain.
				DrawRect( new Rect2( at + new Vector2( 0f, 17f ), new Vector2( width, 5f ) ), new Color( 0, 0, 0, 0.55f ) );
				DrawRect( new Rect2( at + new Vector2( 0f, 17f ), new Vector2( width * kraken.HelmetFraction, 5f ) ), new Color( "bfeaff" ) );
			}
			DrawString( ThemeDB.FallbackFont, new Vector2( at.X, 96f + 52f ), "K R A K E N", HorizontalAlignment.Center, width, 22, green );
		}

		// The robot's own readout while the suit is failing.
		var suitFailing = WaveDirector.Current is { FloatRound: true, State: WaveState.Fighting };
		if ( suitFailing && !PlayerSurvivor.LocalDead )
		{
			var blink = MathF.Sin( now * 5f ) > -0.2f ? 1f : 0.35f;
			var amber = new Color( "ffb020" );
			var at = new Vector2( 28f, Size.Y * 0.2f );
			DrawRect( new Rect2( at - new Vector2( 10f, 26f ), new Vector2( 330f, 86f ) ), new Color( 0, 0, 0, 0.45f ) );
			DrawRect( new Rect2( at - new Vector2( 10f, 26f ), new Vector2( 4f, 86f ) ), amber with { A = blink } );
			DrawString( ThemeDB.FallbackFont, at, "SUIT MALFUNCTION", HorizontalAlignment.Left, -1f, 22, amber with { A = blink } );
			DrawString( ThemeDB.FallbackFont, at + new Vector2( 0f, 24f ), "BALLAST OFFLINE", HorizontalAlignment.Left, -1f, 16, new Color( 1f, 0.45f, 0.3f ) );
			DrawString( ThemeDB.FallbackFont, at + new Vector2( 0f, 44f ), "boots cannot grip: you float", HorizontalAlignment.Left, -1f, 14, new Color( 1, 1, 1, 0.7f ) );
		}

		// Irradiated by the Kraken: how long is left, and what it is doing to you.
		if ( player?.Afflictions is { RadiationLeft: > 0f } dose )
		{
			var words = dose.RadiationPoisonLeft > 0f ? "poisoned, and stamina recovers slowly" : "stamina recovers slowly";
			DrawString( ThemeDB.FallbackFont, new Vector2( 0f, Size.Y * 0.74f ), $"IRRADIATED {MathF.Ceiling( dose.RadiationLeft ):0}s", HorizontalAlignment.Center, Size.X, 26, new Color( "9dff1a" ) );
			DrawString( ThemeDB.FallbackFont, new Vector2( 0f, Size.Y * 0.74f + 22f ), words, HorizontalAlignment.Center, Size.X, 16, new Color( 1, 1, 1, 0.8f ) );
		}

		// The Kraken's whirlpool: a countdown while the player is outside its ring, then the ride back.
		if ( player is not null && Kraken.WhirlpoolWarning( player ) is { } left )
		{
			var warn = new Color( "7dfff0" );
			var pulse = 0.75f + 0.25f * MathF.Sin( (float)Clock.Now * 10f );
			var y = Size.Y * 0.3f;
			var title = left > 0f ? $"WHIRLPOOL IN {MathF.Ceiling( left ):0}" : "WHIRLPOOL";
			DrawString( ThemeDB.FallbackFont, new Vector2( 3f, y + 3f ), title, HorizontalAlignment.Center, Size.X, 54, new Color( 0, 0, 0, 0.6f ) );
			DrawString( ThemeDB.FallbackFont, new Vector2( 0f, y ), title, HorizontalAlignment.Center, Size.X, 54, warn with { A = pulse } );
			if ( left > 0f )
			{
				DrawString( ThemeDB.FallbackFont, new Vector2( 0f, y + 34f ), "Get back inside the ring of lights", HorizontalAlignment.Center, Size.X, 22, new Color( 1, 1, 1, 0.9f ) );
				var width = Size.X * 0.24f;
				var at = new Vector2( (Size.X - width) * 0.5f, y + 46f );
				DrawRect( new Rect2( at, new Vector2( width, 6f ) ), new Color( 0, 0, 0, 0.55f ) );
				DrawRect( new Rect2( at, new Vector2( width * MathX.Clamp( left / 5f, 0f, 1f ), 6f ) ), warn );
			}
		}
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
