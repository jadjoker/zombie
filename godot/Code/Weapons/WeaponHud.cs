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

		maskMaterial = new ShaderMaterial { Shader = new Shader { Code = MaskShader } };
		scopeMask = new ColorRect { Material = maskMaterial, MouseFilter = MouseFilterEnum.Ignore };
		scopeMask.SetAnchorsPreset( LayoutPreset.FullRect );
		AddChild( scopeMask );

		var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		box.SetAnchorsPreset( LayoutPreset.BottomRight );
		box.GrowHorizontal = GrowDirection.Begin;
		box.GrowVertical = GrowDirection.Begin;
		box.OffsetRight = -40; box.OffsetBottom = -36;
		AddChild( box );
		weaponName = Text( 16, 0.75f, HorizontalAlignment.Right );
		box.AddChild( weaponName );
		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		box.AddChild( row );
		mag = Text( 48, 1f, HorizontalAlignment.Right );
		reserve = Text( 24, 0.7f, HorizontalAlignment.Left );
		row.AddChild( mag );
		row.AddChild( reserve );
	}

	static Label Text( int size, float alpha, HorizontalAlignment align )
	{
		var label = new Label { HorizontalAlignment = align, Modulate = new Color( 1, 1, 1, alpha ) };
		label.AddThemeFontSizeOverride( "font_size", size );
		label.AddThemeColorOverride( "font_outline_color", new Color( 0, 0, 0, 0.6f ) );
		label.AddThemeConstantOverride( "outline_size", 4 );
		return label;
	}

	public override void _Process( double delta )
	{
		Modulate = PlayerSurvivor.LocalDead ? new Color( 1, 1, 1, 0 ) : Colors.White;   // no ammo counter on the death screen
		var weapon = Weapon;
		weaponName.Text = (weapon?.DisplayName ?? "").ToUpperInvariant();
		mag.Text = (weapon?.Ammo ?? 0).ToString();
		reserve.Text = "/ " + (weapon?.Reserve ?? 0);
		var low = weapon is not null && !weapon.IsReloading && weapon.Ammo <= weapon.MagazineSize / 4;
		mag.AddThemeColorOverride( "font_color", low ? new Color( "ff6a3a" ) : Colors.White );

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

		// Centre dot: kicks out with each shot. It stays while you aim down the sights (issue #37) and only
		// gives way to the scope's own reticle.
		var since = weapon is null ? 999f : (float)weapon.TimeSinceShot;
		var dotScale = 1f + MathX.Clamp( 1f - since / 0.12f, 0f, 1f ) * 1.2f;
		var dotAlpha = PlayerSurvivor.LocalDead ? 0f : MathF.Max( 0f, 1f - (weapon?.ScopeAmount ?? 0f) * 3f );
		DotAlpha = dotAlpha;
		if ( dotAlpha > 0f ) DrawCircle( c, 3f * dotScale, new Color( 1, 1, 1, 0.9f * dotAlpha ) );

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

			// Reload bar under the ammo.
			if ( weapon.IsReloading )
			{
				var at = new Vector2( Size.X - 40 - 160, Size.Y - 30 );
				DrawRect( new Rect2( at, new Vector2( 160, 6 ) ), new Color( 0, 0, 0, 0.5f ) );
				DrawRect( new Rect2( at, new Vector2( 160 * weapon.ReloadProgress, 6 ) ), Colors.White );
			}
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
