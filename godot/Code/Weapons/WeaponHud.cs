using Godot;

/// <summary>
/// The weapon parts of the s&box ZombieHud: centre dot (kicks out on each shot, fades when aiming), hitmarker
/// (red and bigger on kills), ammo counter with reload bar, and the rifle's scope overlay (dark mask with a lagging
/// shadow, glass tint, duplex reticle with mil dots and a red centre dot). Pixel sizes are the s&box ones at 1080p.
/// </summary>
public partial class WeaponHud : Control
{
	/// <summary> For tests: how solid the centre dot was drawn last frame. </summary>
	public static float DotAlpha { get; private set; }
	PlayerController player;
	WeaponInventory inventory;
	PlayerWeapon Weapon => inventory?.Current;

	ColorRect scopeMask;
	ShaderMaterial maskMaterial;
	Label weaponName, mag, reserve;
	Vector2 scopeDrift;
	Angles lastScopeLook;

	static readonly Color Ink = new( 0.043f, 0.059f, 0.063f, 0.9f );

	public static WeaponHud Create( PlayerController player )
	{
		var hud = new WeaponHud { player = player, MouseFilter = MouseFilterEnum.Ignore };
		hud.SetAnchorsPreset( LayoutPreset.FullRect );
		return hud;
	}

	public override void _Ready()
	{
		inventory = player.GetNodeOrNull<WeaponInventory>( "WeaponInventory" );
		Build();
		UiStyle.Changed += Rebuild;
	}

	public override void _ExitTree() => UiStyle.Changed -= Rebuild;

	/// <summary> The theme changed: build the scope mask and the ammo panel again in the new look. </summary>
	void Rebuild()
	{
		foreach ( var child in GetChildren() ) { RemoveChild( child ); child.QueueFree(); }
		Build();
	}

	static HudTheme Theme => UiStyle.Theme;
	static float Sc => Theme.Scale;

	void Build()
	{

		maskMaterial = new ShaderMaterial { Shader = new Shader { Code = MaskShader } };
		scopeMask = new ColorRect { Material = maskMaterial, MouseFilter = MouseFilterEnum.Ignore };
		scopeMask.SetAnchorsPreset( LayoutPreset.FullRect );
		AddChild( scopeMask );

		// The ammo panel (bottom right): a suit frame with the weapon's name as a caption, the magazine big and the
		// reserve as a small readout.
		ammoPanel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
		ammoPanel.AddThemeStyleboxOverride( "panel", UiStyle.Frame( Sc, null, 16 ) );
		ammoPanel.Draw += () => UiStyle.DrawFrameExtras( ammoPanel, new Rect2( Vector2.Zero, ammoPanel.Size ), 1f, null, Sc );
		ammoPanel.Resized += () => ammoPanel.PivotOffset = ammoPanel.Size * 0.5f;
		ammoPanel.Rotation = MathX.Rad( -Theme.Tilt * 0.75f );
		ammoPanel.SetAnchorsPreset( LayoutPreset.BottomRight );
		ammoPanel.GrowHorizontal = GrowDirection.Begin;
		ammoPanel.GrowVertical = GrowDirection.Begin;
		ammoPanel.OffsetRight = -32; ammoPanel.OffsetBottom = -32;
		AddChild( ammoPanel );
		var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = MouseFilterEnum.Ignore };
		box.AddThemeConstantOverride( "separation", (int)(-6 * Sc) );
		ammoPanel.AddChild( box );
		weaponName = UiStyle.Tag( box, UiStyle.S( 14 ), Theme.PanelCaption, HorizontalAlignment.Right, onPanel: true );
		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = MouseFilterEnum.Ignore };
		row.AddThemeConstantOverride( "separation", 8 );
		box.AddChild( row );
		mag = UiStyle.Big( row, UiStyle.S( 52 ), Theme.PanelNumber, HorizontalAlignment.Right );
		UiStyle.StyleBig( mag, UiStyle.S( 52 ), Theme.PanelNumber, true );
		reserve = UiStyle.Text( row, UiStyle.S( 22 ), Theme.PanelReadout, HorizontalAlignment.Left, mono: true, onPanel: true );
		reserve.SizeFlagsVertical = SizeFlags.ShrinkEnd;
	}

	PanelContainer ammoPanel;

	bool chargeWasFull;
	float chargeFullAt;

	public override void _Process( double delta )
	{
		Modulate = PlayerSurvivor.LocalDead ? new Color( 1, 1, 1, 0 ) : Colors.White;   // no ammo counter on the death screen
		var weapon = Weapon;
		weaponName.Text = (weapon?.DisplayName ?? "").ToUpperInvariant() + Theme.WeaponSuffix;
		mag.Text = (weapon?.Ammo ?? 0).ToString();
		reserve.Text = Theme.ReserveSep + (weapon is { Vacuum: true } ? weapon.MagazineSize : weapon?.Reserve ?? 0) + (weapon is { HasMine: true } ? "  +MINE" : "");   // the Gore Vac: the tank out of 100
		var low = weapon is not null && !weapon.IsReloading && (weapon.Vacuum ? weapon.Ammo < PlayerWeapon.BallCost : weapon.Ammo <= weapon.MagazineSize / 4);
		mag.AddThemeColorOverride( "font_color", low ? new Color( "ff6a3a" ) : Theme.PanelNumber );
		// The Gore Vac: the tank's number pops each time a piece goes down.
		var pop = weapon is { Vacuum: true } ? MathX.Clamp( 1f - (float)weapon.SinceGulp / 0.15f, 0f, 1f ) : 0f;
		mag.PivotOffset = mag.Size * 0.5f;
		mag.Scale = Vector2.One * (1f + pop * 0.22f);
		ammoPanel.OffsetRight = -32f + VisorOverlay.Jitter;   // a big hit knocks it sideways
		ammoPanel.Visible = weapon is not null;

		// The scope's shadow lags a little behind fast turns.
		var look = player.EyeAngles;
		var d = new Vector2( Mathf.Wrap( look.Yaw - lastScopeLook.Yaw, -180f, 180f ), Mathf.Wrap( look.Pitch - lastScopeLook.Pitch, -180f, 180f ) );
		lastScopeLook = look;
		var target = new Vector2( d.X * 60f, -d.Y * 60f ).LimitLength( 36f );
		scopeDrift = scopeDrift.Lerp( target, MathX.Clamp( (float)delta * 8f, 0f, 1f ) );

		var scope = weapon?.ScopeAmount ?? 0f;
		scopeMask.Visible = scope > 0.01f;
		maskMaterial.SetShaderParameter( "amount", scope );
		maskMaterial.SetShaderParameter( "drift", scopeDrift );
		QueueRedraw();
	}

	public override void _Draw()
	{
		var weapon = Weapon;
		var c = Size * 0.5f;
		var scope = weapon?.ScopeAmount ?? 0f;

		if ( scope > 0.01f )
		{
			var ink = Ink with { A = 0.95f * scope };
			DrawRect( new Rect2( c + new Vector2( -420, -4 ), new Vector2( 200, 8 ) ), ink );
			DrawRect( new Rect2( c + new Vector2( 220, -4 ), new Vector2( 200, 8 ) ), ink );
			DrawRect( new Rect2( c + new Vector2( -4, -410 ), new Vector2( 8, 260 ) ), ink );
			DrawRect( new Rect2( c + new Vector2( -4, 150 ), new Vector2( 8, 260 ) ), ink );
			var wire = Ink with { A = 0.9f * scope };
			DrawRect( new Rect2( c + new Vector2( -220, -1 ), new Vector2( 440, 2 ) ), wire );
			DrawRect( new Rect2( c + new Vector2( -1, -150 ), new Vector2( 2, 300 ) ), wire );
			for ( var i = 1; i <= 4; i++ )
			{
				DrawCircle( c + new Vector2( i * 24, 0 ), 2f, wire );
				DrawCircle( c + new Vector2( -i * 24, 0 ), 2f, wire );
				DrawCircle( c + new Vector2( 0, i * 24 ), 2f, wire );
				DrawCircle( c + new Vector2( 0, -i * 24 ), 2f, wire );
			}
			DrawCircle( c, 2.5f, new Color( "ff3b2e" ) with { A = scope } );
		}

		// The crosshair in the theme's style, ink-edged so it reads on anything. It stays while you aim down the sights
		// (issue #37) and only gives way to the scope's own reticle.
		var dotAlpha = PlayerSurvivor.LocalDead ? 0f : MathF.Max( 0f, 1f - (weapon?.ScopeAmount ?? 0f) * 3f );
		DotAlpha = dotAlpha;
		if ( dotAlpha > 0f ) DrawCrosshair( c, dotAlpha, weapon );

		// A charging shot (the trident's 1 s hold): a ring round the crosshair fills clockwise from the top, white to
		// sea green, while three trident prongs close in on the middle. Full, it flashes a ring outward and the
		// prongs and ring pulse green until it is let go.
		var charge = weapon?.ChargeAmount ?? 0f;
		if ( charge > 0.02f && dotAlpha > 0f )
		{
			var now = (float)Clock.Now;
			var full = charge >= 1f;
			if ( full && !chargeWasFull ) chargeFullAt = now;
			chargeWasFull = full;
			var green = new Color( 0.45f, 1f, 0.55f );
			var pulse = full ? 0.5f + 0.5f * MathF.Sin( (now - chargeFullAt) * 14f ) : 0f;
			var colour = full ? green.Lerp( Colors.White, pulse * 0.45f ) : Colors.White.Lerp( new Color( 0.55f, 1f, 0.85f ), charge );
			const float radius = 17f;
			const float top = -MathF.PI * 0.5f;
			DrawArc( c, radius, 0f, MathF.Tau, 48, new Color( 0f, 0f, 0f, 0.4f ), 3.5f, true );
			DrawArc( c, radius, top, top + MathF.Tau * charge, 48, colour, full ? 3.5f + pulse : 2.5f, true );
			// The prongs: from up and the two lower sides, sliding in as it charges (the trident only).
			var reach = MathX.Lerp( 36f, radius + 4f, charge * charge * (3f - 2f * charge) );
			foreach ( var degrees in weapon.Vacuum ? Array.Empty<float>() : new[] { -90f, 30f, 150f } )
			{
				var dir = Vector2.FromAngle( MathX.Rad( degrees ) );
				var side = new Vector2( -dir.Y, dir.X );
				var tip = c + dir * reach;
				var back = tip + dir * 9f;
				DrawLine( tip, back, colour, 2f, true );
				DrawColoredPolygon( new[] { tip - dir * 3.5f, tip + side * 3f, tip - side * 3f }, colour );
			}
			// The moment it fills: a ring bursting outward and fading.
			var since = now - chargeFullAt;
			if ( full && since < 0.3f )
				DrawArc( c, radius + since * 90f, 0f, MathF.Tau, 48, green with { A = 1f - since / 0.3f }, 2f, true );
		}
		else chargeWasFull = false;

		// The Gore Vac sucking: three arcs swirling in round the crosshair.
		if ( weapon is { Sucking: true } or { Blowing: true } && dotAlpha > 0f )
		{
			var spin = (float)Clock.Now * (weapon.Blowing ? -9f : 9f);
			for ( var i = 0; i < 3; i++ )
			{
				var travel = ((float)Clock.Now * 30f + i * 7f) % 18f;
				var r = weapon.Blowing ? 8f + travel : 26f - travel;   // in while sucking, out while blowing
				var a0 = spin + i * MathF.Tau / 3f;
				DrawArc( c, r, a0, a0 + 1.1f, 12, weapon.TankColour with { A = 0.8f * dotAlpha * (r / 26f) }, 2f, true );
			}
		}

		// Hitmarker.
		if ( weapon is not null )
		{
			var kill = weapon.TimeSinceKill < 0.35f;
			var age = kill ? (float)weapon.TimeSinceKill : (float)weapon.TimeSinceHit;
			var opacity = MathX.Clamp( 1f - age / (kill ? 0.35f : 0.18f), 0f, 1f );
			if ( opacity > 0f )
			{
				var scale = (kill ? 1.5f : 1f) * (1.3f - 0.3f * opacity);
				var color = (kill ? new Color( "ff3b3b" ) : Colors.White) with { A = opacity };
				DrawSetTransform( c, MathF.PI / 4f, Vector2.One * scale );
				DrawRect( new Rect2( -1, -20, 2, 12 ), color );
				DrawRect( new Rect2( -1, 8, 2, 12 ), color );
				DrawRect( new Rect2( -20, -1, 12, 2 ), color );
				DrawRect( new Rect2( 8, -1, 12, 2 ), color );
				DrawSetTransform( Vector2.Zero, 0f, Vector2.One );
			}

			// Reload: a row of pills filling just over the ammo panel.
			if ( weapon.IsReloading )
			{
				var panel = ammoPanel.GetRect();
				UiStyle.DrawPills( this, new Rect2( panel.Position.X + 10f * Sc, panel.Position.Y - 16f * Sc, panel.Size.X - 20f * Sc, 9f * Sc ), 8, weapon.ReloadProgress, UiStyle.Teal, Colors.White, 1f, -1f, 3f );
			}
		}
	}

	/// <summary> The theme's crosshair round the middle of the screen. </summary>
	void DrawCrosshair( Vector2 c, float a, PlayerWeapon weapon )
	{
		var ink = UiStyle.Ink with { A = 0.75f * a };
		var accent = UiStyle.Teal;
		var since = weapon is null ? 9f : (float)weapon.TimeSinceHit;
		switch ( Theme.Crosshair )
		{
			case HudTheme.CrossLook.Brackets:
				UiStyle.DrawBrackets( this, new Rect2( c - new Vector2( 9f, 9f ), new Vector2( 18f, 18f ) ), accent with { A = 0.9f * a }, 5f, 1.6f );
				DrawCircle( c, 2.4f, UiStyle.Ink with { A = 0.8f * a } );
				DrawCircle( c, 1.4f, Colors.White with { A = a } );
				break;
			case HudTheme.CrossLook.Brass:
				DrawArc( c, 11f, 0f, MathF.Tau, 40, new Color( "3a2606" ) with { A = 0.85f * a }, 5f, true );
				DrawArc( c, 11f, 0f, MathF.Tau, 40, new Color( "e8c070" ) with { A = a }, 2.6f, true );
				DrawCircle( c, 2.2f, new Color( "ffe2a0" ) with { A = a } );
				break;
			case HudTheme.CrossLook.Bubble:
				DrawArc( c, 10f, 0f, MathF.Tau, 40, new Color( 0.9f, 1f, 1f, 0.85f * a ), 1.6f, true );
				DrawCircle( c - new Vector2( 3.5f, 3.5f ), 2.4f, new Color( 1f, 1f, 1f, 0.7f * a ) );
				break;
			case HudTheme.CrossLook.Ink:
				DrawArc( c, 11f, 0f, MathF.Tau, 40, new Color( "fff6e0" ) with { A = 0.9f * a }, 6f, true );
				DrawArc( c, 11f, 0f, MathF.Tau, 40, new Color( "111111" ) with { A = a }, 3f, true );
				DrawCircle( c, 2f, new Color( "111111" ) with { A = a } );
				break;
			case HudTheme.CrossLook.Tm:
				DrawArc( c, 8f, 0f, MathF.Tau, 32, UiStyle.Ink with { A = 0.75f * a }, 4.5f, true );
				DrawArc( c, 8f, 0f, MathF.Tau, 32, Colors.White with { A = a }, 2f, true );
				DrawLine( c - new Vector2( 8f, 0f ), c + new Vector2( 8f, 0f ), Colors.White with { A = a }, 1.5f );
				DrawLine( c - new Vector2( 0f, 8f ), c + new Vector2( 0f, 8f ), Colors.White with { A = a }, 1.5f );
				UiStyle.DrawBig( this, c + new Vector2( 9f, -5f ), "™", 15, Colors.White with { A = a } );
				break;
			case HudTheme.CrossLook.Ping:
				// Rings ping outward from the dot, and once more on every hit.
				var now = (float)Clock.Now;
				for ( var k = 0; k < 2; k++ )
				{
					var t = (now / 1.8f + k * 0.5f) % 1f;
					DrawArc( c, 4f + t * 16f, 0f, MathF.Tau, 32, accent with { A = (1f - t) * 0.8f * a }, 1.5f, true );
				}
				if ( since < 0.4f ) DrawArc( c, 6f + since * 60f, 0f, MathF.Tau, 32, accent with { A = (1f - since / 0.4f) * a }, 2f, true );
				DrawCircle( c, 2.2f, new Color( "d8ffef" ) with { A = a } );
				break;
			case HudTheme.CrossLook.Glow:
				UiStyle.Glow( this, c, 3f, UiStyle.TealSoft, a );
				break;
			case HudTheme.CrossLook.Diamond:
				var d = 9f;
				var pts = new[] { c + new Vector2( 0f, -d ), c + new Vector2( d, 0f ), c + new Vector2( 0f, d ), c + new Vector2( -d, 0f ), c + new Vector2( 0f, -d ) };
				DrawPolyline( pts, ink, 4.5f, true );
				DrawPolyline( pts, new Color( "ffb02e" ) with { A = a }, 2.2f, true );
				DrawCircle( c, 1.6f, Colors.White with { A = a } );
				break;
			default:   // Ring (Arcade Dive): a small glowing suit ring with a dot in the middle.
				DrawArc( c, 9f, 0f, MathF.Tau, 40, UiStyle.Ink with { A = 0.75f * a }, 4f, true );
				DrawArc( c, 9f, 0f, MathF.Tau, 40, accent with { A = 0.85f * a }, 1.6f, true );
				DrawCircle( c, 2.6f, UiStyle.Ink with { A = 0.8f * a } );
				DrawCircle( c, 1.6f, Colors.White with { A = a } );
				break;
		}
	}

	/// <summary>
	/// The s&box scope mask and tint: radial gradients over the screen grown by 40px and shifted by the drift.
	/// Stops are fractions of the half-diagonal (CSS "farthest-corner").
	/// </summary>
	const string MaskShader = @"
shader_type canvas_item;
uniform float amount = 1.0;
uniform vec2 drift = vec2(0.0);

void fragment() {
	vec2 size = 1.0 / SCREEN_PIXEL_SIZE;
	float scale = size.y / 1080.0;
	vec2 center = size * 0.5 + drift * scale;
	vec2 half_box = size * 0.5 + vec2(40.0) * scale;
	float t = distance(FRAGCOORD.xy, center) / length(half_box);
	float a = 0.0;
	if (t < 0.30) a = 0.0;
	else if (t < 0.34) a = mix(0.0, 0.35, (t - 0.30) / 0.04);
	else if (t < 0.365) a = mix(0.35, 0.97, (t - 0.34) / 0.025);
	else if (t < 0.45) a = mix(0.97, 1.0, (t - 0.365) / 0.085);
	else a = 1.0;
	vec3 mask_col = mix(vec3(0.0), vec3(0.02, 0.031, 0.039), step(0.34, t) * (1.0 - step(0.45, t)));
	// glass tint
	float tt = clamp(distance(FRAGCOORD.xy, size * 0.5) / length(size * 0.5) / 0.30, 0.0, 1.0);
	vec4 tint = mix(vec4(0.498, 0.847, 1.0, 0.05), vec4(0.039, 0.165, 0.2, 0.12), tt);
	vec3 col = mix(tint.rgb, mask_col, a);
	float alpha = max(a, tint.a);
	COLOR = vec4(col, alpha * amount);
}";
}
