using Godot;

/// <summary>
/// The inside of the robot's faceplate (themes with a visor: Robot Visor, Diving-Bell Robot), laid over the world
/// under the HUD: a barely-there scanline, a soft dark rim
/// round the edges, and a short glitch on big hits (a few tinted bands flicker, the HUD jumps sideways and its colour
/// split widens). `Glitch` (0..1) and `Jitter` (px) are read by the HUD pieces.
/// </summary>
public partial class VisorOverlay : ColorRect
{
	/// <summary> How hard the visor is glitching right now (0 calm, 1 a big hit just landed). </summary>
	public static float Glitch { get; private set; }
	/// <summary> How far the HUD is knocked sideways this frame (px). </summary>
	public static float Jitter { get; private set; }

	/// <summary> A hit this big or bigger glitches the visor. </summary>
	public const float GlitchDamage = 12f;
	const float GlitchTime = 0.35f;

	PlayerSurvivor player;
	Health hooked;
	ShaderMaterial material;
	readonly Random random = new();

	public static VisorOverlay Create( PlayerSurvivor player )
	{
		var overlay = new VisorOverlay { player = player, Name = "Visor", MouseFilter = MouseFilterEnum.Ignore, Color = Colors.White };
		overlay.SetAnchorsPreset( LayoutPreset.FullRect );
		return overlay;
	}

	/// <summary> Glitches the visor (strength 0..1). </summary>
	public static void Kick( float strength ) => Glitch = MathF.Max( Glitch, MathX.Clamp( strength, 0f, 1f ) );

	public override void _Ready()
	{
		material = new ShaderMaterial { Shader = new Shader { Code = Code } };
		Material = material;
		Glitch = 0f;
		Jitter = 0f;
	}

	public override void _ExitTree()
	{
		if ( IsInstanceValid( hooked ) ) hooked.Damaged -= OnDamaged;
		hooked = null;
		Glitch = 0f;
		Jitter = 0f;
	}

	void OnDamaged( DamageInfo damage )
	{
		if ( UiStyle.Theme.Glitch && damage.Damage >= GlitchDamage ) Kick( 0.45f + damage.Damage / 60f );
	}

	public override void _Process( double delta )
	{
		var health = player?.Health;
		if ( health != hooked )
		{
			if ( IsInstanceValid( hooked ) ) hooked.Damaged -= OnDamaged;
			hooked = health;
			if ( hooked is not null ) hooked.Damaged += OnDamaged;
		}
		Glitch = MathF.Max( 0f, Glitch - (float)delta / GlitchTime );
		Jitter = Glitch > 0.05f ? (float)(random.NextDouble() * 2.0 - 1.0) * 7f * Glitch : 0f;
		var theme = UiStyle.Theme;
		if ( !theme.Glitch ) { Glitch = 0f; Jitter = 0f; }
		Visible = !DevFlags.HideHud && !PlayerSurvivor.LocalDead && (theme.Scan > 0f || theme.Rim > 0f);
		material.SetShaderParameter( "glitch", Glitch );
		material.SetShaderParameter( "scan", theme.Scan );
		material.SetShaderParameter( "rim", theme.Rim );
	}

	const string Code = @"
shader_type canvas_item;
uniform float glitch = 0.0;
uniform float scan = 0.025;
uniform float rim = 0.38;

float hash(float n) { return fract(sin(n) * 43758.5453); }

void fragment() {
	vec2 size = 1.0 / SCREEN_PIXEL_SIZE;
	// Scanlines (one faint dark line every 3 px) and the faceplate rim (a soft dark edge round a rounded rectangle).
	vec2 p = abs(SCREEN_UV - 0.5) * 2.0;
	float edge = smoothstep(0.0, 0.45, length(max(p - vec2(0.8, 0.7), 0.0)));
	vec3 col = vec3(0.0, 0.03, 0.05);
	float a = step(2.0, mod(FRAGCOORD.y, 3.0)) * scan + edge * rim;
	// Glitch: a few horizontal bands tinted cyan or red, changing 30 times a second.
	float row = floor(FRAGCOORD.y / 14.0);
	float frame = floor(TIME * 30.0);
	float band = step(0.86, hash(row * 7.13 + frame));
	vec3 tint = hash(row + frame * 3.1) > 0.5 ? vec3(0.5, 0.96, 1.0) : vec3(1.0, 0.25, 0.42);
	float g = band * glitch * 0.22;
	col = mix(col, tint, g / max(a + g, 0.001));
	COLOR = vec4(col, a + g);
}";
}
