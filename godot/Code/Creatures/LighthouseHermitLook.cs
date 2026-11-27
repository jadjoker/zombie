using Godot;
using static SmoothKit;

/// <summary>
/// The Lighthouse Hermit's looks, built in code: a red and purple crab (a lofted carapace with spines, the classic black
/// almond alien eyes on stalks, whip antennae with glowing tips, one huge warty claw and one smaller, four long walking legs
/// that step on their own), a soft pink abdomen glowing with green veins curled up into the tower, and the lighthouse leant
/// back over him: four banded sections of brick (<see cref="TowerShader"/>: red and white, barnacles, weed, glowing green
/// cracks), windows lit green, an iron gallery with a railing, a glass lamp room with a huge alien eye for its lamp
/// (<see cref="LampShader"/>), a patina dome, and a brass foghorn. Metres; the root is on the ground, facing -Z.
/// </summary>
public partial class LighthouseHermit
{
	Node3D body, crab, thorax, abdomen, tower, lampRoom, lampEye, cap, horn, bigClaw, smallClaw, bigArm, smallArm, bigFinger, smallFinger;
	readonly List<Node3D> sections = new(), stalks = new(), antennae = new();
	readonly List<Leg> legs = new();
	ShaderMaterial shell, softSkin, lampPaint, hornPaint;
	readonly List<ShaderMaterial> towerPaints = new();
	MeshInstance3D bellyBulge;
	CreaturePart bellyPart;
	OmniLight3D lampLight;
	float flash, dying;

	/// <summary> How far the tower leans back over his shell (radians). </summary>
	const float TowerTilt = 0.2f;
	const float SectionHeight = 1.8f;
	const int SectionCount = 4;
	const float TowerTop = SectionHeight * SectionCount;
	/// <summary> The crab's own size (its parts are built at 1 and scaled up). </summary>
	const float CrabScale = 1.45f;
	/// <summary> Where the tower's foot sits on him (body space). </summary>
	static readonly Vector3 TowerBase = new( 0f, 1.5f, 2.25f );
	/// <summary> The belly inside the tower's foot (from TowerBase), and its size: narrower than the tower so the walls hide it. </summary>
	static readonly Vector3 BellyRest = new( 0f, 0.7f, 0f ), BellySize = new( 3f, 1.5f, 3f );
	static float TowerRadius( float y ) => MathX.Lerp( 1.8f, 1.2f, MathX.Clamp( y / TowerTop, 0f, 1f ) );

	static readonly Color Purple = new( 0.36f, 0.16f, 0.5f );
	static readonly Color Cream = new( 0.98f, 0.86f, 0.72f );
	static readonly Color EyeGreen = new( 0.35f, 1f, 0.45f );
	static readonly Color LampGold = new( 1f, 0.86f, 0.42f );

	/// <summary> One walking leg: its hip and rest spot (body space), where its foot is planted, and its step. </summary>
	sealed class Leg
	{
		public Vector3 Hip, Rest, Plant, From, To;
		public float Step = -1f;
		public int Group;
		public MeshInstance3D Upper, Lower, Tip;
		public Node3D Knee, Ankle;
		public readonly List<Node3D> Segments = new();
		public bool Planted;
	}

	/// <summary>
	/// His shell: red on top darkening to purple at the edges and spines, cream underneath (local -Z is down for the
	/// carapace, `down` picks the axis), a fine pitted texture, a few glowing teal spots; flashes white when hit.
	/// </summary>
	static readonly Shader ShellShader = new()
	{
		Code = @"shader_type spatial;
uniform vec3 top : source_color = vec3(0.86, 0.26, 0.18);
uniform vec3 edge : source_color = vec3(0.36, 0.16, 0.5);
uniform vec3 under : source_color = vec3(0.98, 0.86, 0.72);
uniform vec3 spot : source_color = vec3(0.3, 1.0, 0.85);
uniform vec3 down_axis = vec3(0.0, 0.0, -1.0);
uniform float spots = 1.0;
uniform float flash = 0.0;
varying vec3 lp;
varying vec3 ln;
float hash(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise(vec3 x) {
	vec3 i = floor(x); vec3 f = fract(x); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}
void vertex() { lp = VERTEX; ln = NORMAL; }
void fragment() {
	float d = dot(normalize(ln), down_axis);
	float m = noise(lp * 2.2) * 0.6 + noise(lp * 6.0) * 0.4;
	vec3 c = mix(top, edge, smoothstep(0.45, 0.8, m));
	c = mix(c, under, smoothstep(0.1, 0.6, d));
	float pits = smoothstep(0.62, 0.7, noise(lp * 14.0));
	c *= 1.0 - pits * 0.25;
	float s = smoothstep(0.82, 0.86, noise(lp * 3.1 + vec3(7.0))) * spots * (1.0 - smoothstep(0.0, 0.5, d));
	float fr = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = mix(c, vec3(2.0), flash);
	ROUGHNESS = 0.45 + pits * 0.3;
	EMISSION = spot * s * (0.9 + 0.5 * sin(TIME * 2.0 + lp.x * 3.0)) + edge * fr * 0.25 + vec3(1.2) * flash * 0.3;
}",
	};

	/// <summary> His soft parts: pink, paler and wet looking, with green veins that pulse (brighter as `glow` rises). </summary>
	static readonly Shader SoftShader = new()
	{
		Code = @"shader_type spatial;
uniform vec3 colour : source_color = vec3(0.92, 0.55, 0.72);
uniform vec3 deep : source_color = vec3(0.62, 0.3, 0.7);
uniform vec3 vein : source_color = vec3(0.35, 1.0, 0.45);
uniform float glow = 0.5;
uniform float flash = 0.0;
varying vec3 lp;
float hash(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise(vec3 x) {
	vec3 i = floor(x); vec3 f = fract(x); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}
void vertex() { lp = VERTEX; }
void fragment() {
	float n = noise(lp * 2.4 + vec3(0.0, TIME * 0.05, 0.0));
	float v = pow(1.0 - abs(n * 2.0 - 1.0), 14.0);
	float folds = 0.5 + 0.5 * sin(lp.y * 9.0 + noise(lp * 3.0) * 3.0);
	vec3 c = mix(deep, colour, 0.55 + folds * 0.45);
	float pulse = 0.5 + 0.5 * sin(TIME * 3.0 - lp.y * 4.0);
	ALBEDO = mix(c, vec3(2.2), flash);
	ROUGHNESS = 0.2;
	SPECULAR = 0.8;
	EMISSION = vein * v * glow * (0.6 + 0.8 * pulse) + vec3(1.5) * flash * 0.3;
}",
	};

	/// <summary>
	/// The lighthouse's brick: bands of red and white by height (`y0` is the section's height up the tower), rows of
	/// bricks, weathering, barnacle crust and green weed toward the bottom, and cracks that glow alien green (`charge`
	/// brightens them before an attack).
	/// </summary>
	static readonly Shader TowerShader = new()
	{
		Code = @"shader_type spatial;
uniform vec3 red : source_color = vec3(0.72, 0.13, 0.12);
uniform vec3 white : source_color = vec3(0.9, 0.88, 0.82);
uniform vec3 weed : source_color = vec3(0.2, 0.36, 0.22);
uniform vec3 crack : source_color = vec3(0.35, 1.0, 0.45);
uniform float y0 = 0.0;
uniform float band = 1.2;
uniform float charge = 0.0;
uniform float flash = 0.0;
varying vec3 lp;
float hash(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise(vec3 x) {
	vec3 i = floor(x); vec3 f = fract(x); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}
void vertex() { lp = VERTEX; }
void fragment() {
	vec3 p = lp + vec3(0.0, y0, 0.0);
	float wobble = noise(p * vec3(1.5, 0.4, 1.5)) * 0.25;
	float b = mod(floor((p.y + wobble) / band), 2.0);
	vec3 c = mix(red, white, b);
	// Bricks: rows, each row's joints offset.
	float angle = atan(p.x, p.z);
	float row = floor(p.y / 0.24);
	float u = angle * 2.6 + mod(row, 2.0) * 0.5;
	float mortar = max(1.0 - smoothstep(0.0, 0.035, abs(fract(p.y / 0.24) - 0.5) - 0.44), 1.0 - smoothstep(0.0, 0.05, abs(fract(u) - 0.5) - 0.44));
	c = mix(c, c * 0.62, mortar * 0.7);
	c *= 0.82 + 0.18 * hash(vec3(floor(u), row, 1.0));
	// Weathering streaks running down, then barnacles and weed low down.
	float streak = noise(vec3(angle * 6.0, p.y * 0.4, 0.0));
	c *= 1.0 - smoothstep(0.55, 0.85, streak) * 0.3;
	float low = 1.0 - smoothstep(0.0, 3.2, p.y);
	float crust = smoothstep(0.5, 0.75, noise(p * 3.5)) * (0.25 + low * 0.75);
	c = mix(c, vec3(0.72, 0.7, 0.62), crust * 0.8);
	c = mix(c, weed, smoothstep(0.45, 0.7, noise(p * 1.4 + vec3(4.0))) * low * 0.85);
	// Glowing cracks.
	float n = noise(p * 1.1 + vec3(9.0));
	float cr = pow(1.0 - abs(n * 2.0 - 1.0), 48.0) * smoothstep(0.45, 0.7, noise(p * 0.5));
	float pulse = 0.6 + 0.4 * sin(TIME * 2.0 - p.y * 1.5);
	ALBEDO = mix(mix(c, vec3(0.05), cr), vec3(2.0), flash);
	ROUGHNESS = 0.85;
	EMISSION = crack * cr * (pulse * 1.2 + charge * 3.0) + vec3(1.0) * flash * 0.25;
}",
	};

	/// <summary>
	/// The lamp: a huge glowing alien eye, gold and white, a slit pupil that narrows (`focus`) and a ring of green round
	/// it, veins in the white; `power` dims it (dead, it goes out).
	/// </summary>
	static readonly Shader LampShader = new()
	{
		Code = @"shader_type spatial;
render_mode unshaded, fog_disabled;
uniform vec3 white : source_color = vec3(1.0, 0.96, 0.82);
uniform vec3 iris : source_color = vec3(1.0, 0.8, 0.3);
uniform vec3 ring : source_color = vec3(0.35, 1.0, 0.45);
uniform float focus = 0.0;
uniform float power = 1.0;
uniform float flash = 0.0;
varying vec3 lp;
void vertex() { lp = VERTEX; }
void fragment() {
	vec3 d = normalize(lp);
	// The eye looks down -Z: the iris is the front cap.
	float r = d.z < 0.0 ? length(d.xy) : 2.0;
	float irisMask = 1.0 - smoothstep(0.55, 0.6, r);
	float ringMask = smoothstep(0.5, 0.56, r) * (1.0 - smoothstep(0.6, 0.66, r));
	float slitW = mix(0.12, 0.035, focus);
	float pupil = (1.0 - smoothstep(slitW, slitW + 0.03, abs(d.x))) * (1.0 - smoothstep(0.42, 0.46, abs(d.y))) * irisMask;
	float veins = pow(abs(sin(atan(d.y, d.x) * 9.0 + d.z * 6.0)), 40.0) * (1.0 - irisMask);
	vec3 c = mix(white, iris * (1.2 - r * 0.6), irisMask);
	c = mix(c, ring * 1.6, ringMask);
	c = mix(c, vec3(0.9, 0.2, 0.15), veins * 0.6);
	c = mix(c, vec3(0.0), pupil);
	ALBEDO = (c * (1.4 + focus * 1.2) + vec3(2.0) * flash) * power;
}",
	};

	/// <summary> Brass, weathered green in the hollows; glows hot as the horn winds up (`heat`). </summary>
	static readonly Shader HornShader = new()
	{
		Code = @"shader_type spatial;
uniform float heat = 0.0;
uniform float flash = 0.0;
varying vec3 lp;
float hash(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
void vertex() { lp = VERTEX; }
void fragment() {
	float patina = smoothstep(0.55, 0.9, hash(floor(lp * 9.0)));
	vec3 brass = mix(vec3(0.85, 0.62, 0.25), vec3(0.3, 0.6, 0.5), patina * 0.7);
	ALBEDO = mix(brass, vec3(2.0), flash);
	METALLIC = 0.85 - patina * 0.5;
	ROUGHNESS = 0.3 + patina * 0.5;
	EMISSION = vec3(1.0, 0.5, 0.15) * heat * (1.5 + 0.5 * sin(TIME * 30.0));
}",
	};

	ShaderMaterial Shell( bool spots, Vector3 down )
	{
		var m = new ShaderMaterial { Shader = ShellShader };
		m.SetShaderParameter( "top", ShellRed );
		m.SetShaderParameter( "edge", Purple );
		m.SetShaderParameter( "under", Cream );
		m.SetShaderParameter( "down_axis", down );
		m.SetShaderParameter( "spots", spots ? 1f : 0f );
		return m;
	}

	void Build()
	{
		shell = Shell( true, Vector3.Forward );   // the carapace and hands are lofted turned so their local -Z is down
		var limbShell = Shell( false, Vector3.Zero );   // limbs point every way: no belly side
		softSkin = new ShaderMaterial { Shader = SoftShader };
		lampPaint = new ShaderMaterial { Shader = LampShader };
		hornPaint = new ShaderMaterial { Shader = HornShader };
		var black = Sculpt.Paint( new Color( 0.015f, 0.015f, 0.03f ), 0.04f );
		black.ClearcoatEnabled = true;
		black.Clearcoat = 1f;
		black.ClearcoatRoughness = 0.02f;
		var eyeRim = Sculpt.Glow( EyeGreen, 1.6f );
		var tipGlow = Sculpt.Glow( new Color( 0.3f, 1f, 0.9f ), 1.8f );
		var iron = Sculpt.Paint( new Color( 0.09f, 0.1f, 0.11f ), 0.55f, 0.6f );
		var patina = Sculpt.Paint( new Color( 0.25f, 0.5f, 0.44f ), 0.6f, 0.4f );
		var stone = Sculpt.Paint( new Color( 0.42f, 0.4f, 0.38f ), 0.9f );
		var glass = new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.55f, 0.85f, 0.8f, 0.22f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f, Metallic = 0.2f,
			RimEnabled = true, Rim = 0.7f, RimTint = 0.6f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		var windowGlow = Sculpt.Glow( EyeGreen, 1.2f );
		var barnacle = Sculpt.Paint( new Color( 0.82f, 0.8f, 0.72f ), 0.8f );
		var weedPaint = FinMaterial( new Color( 0.12f, 0.3f, 0.14f ), new Color( 0.3f, 0.55f, 0.22f ), 0.95f, 0.35f );
		var rng = new RandomNumberGenerator { Seed = 1010 };

		body = new Node3D { Name = "Body" };
		AddChild( body );
		// The crab itself is built at its own size, scaled up as a whole (its hit shapes too).
		crab = Sculpt.Joint( body, Vector3.Zero, "Crab" );
		crab.Scale = Vector3.One * CrabScale;

		// ---- The carapace: lofted along its own Y, turned so that runs forward (-Z) and its Z is down ----
		thorax = Sculpt.Joint( crab, new Vector3( 0f, 1.55f, -0.55f ), "Thorax" );
		var carapace = Sculpt.Joint( thorax, Vector3.Zero, "Carapace" );
		carapace.Basis = new Basis( Vector3.Right, -MathF.PI * 0.5f );   // local Y forward, local Z up
		var rings = new List<(float, float, float, float)>
		{
			(-1.0f, 0.45f, 0.32f, -0.05f),
			(-0.7f, 0.95f, 0.62f, -0.02f),
			(-0.2f, 1.18f, 0.74f, 0f),
			(0.35f, 1.12f, 0.7f, 0.02f),
			(0.8f, 0.92f, 0.56f, 0.06f),
			(1.1f, 0.62f, 0.4f, 0.1f),
			(1.28f, 0.22f, 0.16f, 0.12f),
		};
		Put( carapace, Cached( "hermit carapace", () => Loft( Smooth( rings ), 0f, 1f, 32 ) ), shell, "Carapace" );
		// Grooves and spines along the shell's edge and a rostrum at the front.
		foreach ( var s in new[] { -1f, 1f } )
		{
			for ( var k = 0; k < 5; k++ )
			{
				var y = -0.6f + k * 0.38f;
				var r = MathX.Lerp( 1.1f, 0.75f, k / 4f );
				var baseAt = new Vector3( s * r * 0.98f, y, -0.05f );
				Sculpt.Cone( carapace, baseAt, baseAt + new Vector3( s * 0.32f, 0.12f, -0.12f ), 0.09f, shell, 8, "Spine" );
			}
			Ball( carapace, new Vector3( s * 0.5f, 0.1f, -0.62f ), new Vector3( 0.5f, 0.9f, 0.16f ), shell, null, "Ridge" );
		}
		Sculpt.Cone( carapace, new Vector3( 0f, 1.2f, -0.05f ), new Vector3( 0f, 1.65f, 0.08f ), 0.12f, shell, 8, "Rostrum" );

		// ---- Eyes on stalks, antennae ----
		foreach ( var s in new[] { -1f, 1f } )
		{
			var stalk = Sculpt.Joint( thorax, new Vector3( s * 0.32f, 0.35f, -1.05f ), "Eye Stalk" );
			Tube( stalk, Vector3.Zero, new Vector3( 0f, 0.85f, 0f ), 0.1f, 0.075f, limbShell, "Stalk" );
			var eye = Sculpt.Joint( stalk, new Vector3( 0f, 0.92f, 0f ), "Eye" );
			var almond = Sculpt.Joint( eye, new Vector3( 0f, 0f, -0.06f ), "Almond" );
			almond.Basis = new Basis( Vector3.Back, -s * 0.5f );
			Ball( almond, new Vector3( 0f, 0f, 0.02f ), new Vector3( 0.33f, 0.21f, 0.08f ), eyeRim, null, "Eye Rim" );
			Ball( almond, Vector3.Zero, new Vector3( 0.28f, 0.16f, 0.12f ), black, null, "Eye" );
			Ball( eye, new Vector3( 0f, 0f, 0.05f ), Vector3.One * 0.2f, limbShell, null, "Eye Socket" );
			stalks.Add( stalk );
			var part = CreaturePart.Create( this, EyeIndex, "Hermit Eye", 0.24f );
			eye.AddChild( part );
			// Long whip antennae with glowing tips, and short feathery ones.
			var whip = Sculpt.Joint( thorax, new Vector3( s * 0.18f, 0.15f, -1.2f ), "Antenna" );
			whip.Basis = new Basis( Vector3.Up, -s * 0.5f ) * new Basis( Vector3.Right, -1.1f );
			Tube( whip, Vector3.Zero, new Vector3( 0f, 2.6f, 0f ), 0.035f, 0.01f, limbShell, "Whip" );
			Ball( whip, new Vector3( 0f, 2.62f, 0f ), Vector3.One * 0.08f, tipGlow, null, "Antenna Light" );
			antennae.Add( whip );
			var small = Sculpt.Joint( thorax, new Vector3( s * 0.08f, 0.05f, -1.25f ), "Antennule" );
			small.Basis = new Basis( Vector3.Up, -s * 0.25f ) * new Basis( Vector3.Right, -0.6f );
			Tube( small, Vector3.Zero, new Vector3( 0f, 0.7f, 0f ), 0.03f, 0.02f, limbShell, "Antennule" );
			Fin( small, new Vector3( 0f, 0.75f, 0f ), new Basis( Vector3.Right, MathF.PI ), 0.3f, 0.12f, FinMaterial( Purple, EyeGreen, 0.8f, 0.3f ), 0.1f );
		}

		// ---- Claws: the right one huge and warty, the left smaller ----
		(Node3D Arm, Node3D Hand, Node3D Finger) Claw( float s, float size )
		{
			var arm = Sculpt.Joint( crab, new Vector3( s * 0.95f, 1.35f, -1.2f ), s > 0 ? "Big Claw Arm" : "Small Claw Arm" );
			Ball( arm, Vector3.Zero, Vector3.One * 0.45f * size, limbShell, null, "Shoulder" );
			Tube( arm, Vector3.Zero, new Vector3( 0f, -0.1f, -1.1f * size ), 0.22f * size, 0.18f * size, limbShell, "Merus" );
			var wrist = Sculpt.Joint( arm, new Vector3( 0f, -0.1f, -1.1f * size ), "Wrist" );
			Ball( wrist, Vector3.Zero, Vector3.One * 0.36f * size, limbShell, null, "Carpus" );
			var hand = Sculpt.Joint( wrist, new Vector3( 0f, 0f, -0.15f * size ), "Hand" );
			var palm = Sculpt.Joint( hand, Vector3.Zero, "Palm" );
			palm.Basis = new Basis( Vector3.Right, -MathF.PI * 0.5f );
			var hr = new List<(float, float, float, float)>
			{
				(0f, 0.22f, 0.2f, 0f),
				(0.25f, 0.42f, 0.34f, 0f),
				(0.6f, 0.5f, 0.38f, 0f),
				(0.95f, 0.42f, 0.32f, 0f),
				(1.15f, 0.26f, 0.2f, 0f),
			};
			Put( palm, Cached( $"hermit hand {size:F2}", () => Loft( Smooth( hr ), 0f, 1f, 24 ) ), shell, "Propodus" ).Scale = Vector3.One * size;
			// Warts on the outside.
			for ( var k = 0; k < 9; k++ )
			{
				var a = rng.RandfRange( -1.2f, 1.2f );
				var y = rng.RandfRange( 0.15f, 0.95f ) * size;
				var r = MathX.Lerp( 0.42f, 0.5f, 0.5f ) * size;
				Ball( palm, new Vector3( MathF.Sin( a ) * r * s, y, -MathF.Cos( a ) * r * 0.8f ), Vector3.One * rng.RandfRange( 0.08f, 0.14f ) * size, limbShell, null, "Wart" );
			}
			// The fixed finger and the moving one, dark tipped.
			var tipPaint = Sculpt.Paint( Purple * 0.6f, 0.3f, 0.2f );
			Sculpt.Cone( palm, new Vector3( -s * 0.12f * size, 1.1f * size, 0f ), new Vector3( -s * 0.05f * size, 1.95f * size, -0.08f * size ), 0.2f * size, limbShell, 10, "Fixed Finger" );
			Sculpt.Cone( palm, new Vector3( -s * 0.05f * size, 1.7f * size, -0.06f * size ), new Vector3( -s * 0.04f * size, 2.02f * size, -0.1f * size ), 0.09f * size, tipPaint, 8, "Finger Tip" );
			var finger = Sculpt.Joint( palm, new Vector3( s * 0.16f * size, 1.05f * size, 0f ), "Dactyl" );
			Sculpt.Cone( finger, Vector3.Zero, new Vector3( -s * 0.1f * size, 0.9f * size, -0.05f * size ), 0.19f * size, limbShell, 10, "Moving Finger" );
			Sculpt.Cone( finger, new Vector3( -s * 0.08f * size, 0.62f * size, -0.04f * size ), new Vector3( -s * 0.1f * size, 0.95f * size, -0.06f * size ), 0.08f * size, tipPaint, 8, "Finger Tip" );
			var part = CreaturePart.Create( this, s > 0 ? BigClawIndex : SmallClawIndex, s > 0 ? "Hermit Big Claw" : "Hermit Small Claw", 0.55f * size );
			hand.AddChild( part );
			part.Position = new Vector3( 0f, 0f, -0.6f * size );
			return (arm, hand, finger);
		}
		(bigArm, bigClaw, bigFinger) = Claw( 1f, 1.25f );
		(smallArm, smallClaw, smallFinger) = Claw( -1f, 0.8f );

		// ---- Legs: two walking pairs each side; the feet step on their own (see StepLegs) ----
		var segment = Cached( "hermit leg", () => TubeMesh( 1f, 0.24f, 0.19f ) );
		var shin = Cached( "hermit shin", () => TubeMesh( 1f, 0.18f, 0.12f ) );
		var dactyl = Cached( "hermit dactyl", () => TubeMesh( 1f, 0.12f, 0.02f ) );
		var tipPaint = Shell( false, Vector3.Zero );
		tipPaint.SetShaderParameter( "top", Purple );
		tipPaint.SetShaderParameter( "edge", Purple * 0.5f );
		var index = 0;
		foreach ( var s in new[] { -1f, 1f } )
			foreach ( var z in new[] { -0.75f, 0.05f } )
			{
				var leg = new Leg
				{
					Hip = new Vector3( s * 1.0f, 1.3f, z ) * CrabScale,
					Rest = new Vector3( s * 2.7f, 0f, z * 1.5f - 0.2f ) * CrabScale,
					Group = (index + (s > 0 ? 1 : 0)) % 2,
				};
				leg.Upper = Put( body, segment, limbShell, "Leg Upper" );
				leg.Lower = Put( body, shin, limbShell, "Leg Lower" );
				leg.Tip = Put( body, dactyl, tipPaint, "Leg Tip" );
				leg.Knee = Sculpt.Joint( body, Vector3.Zero, "Knee" );
				Ball( leg.Knee, Vector3.Zero, Vector3.One * 0.44f, limbShell, null, "Knee Joint" );
				leg.Ankle = Sculpt.Joint( body, Vector3.Zero, "Ankle" );
				Ball( leg.Ankle, Vector3.Zero, Vector3.One * 0.32f, limbShell, null, "Ankle Joint" );
				Sculpt.Cone( leg.Ankle, Vector3.Zero, new Vector3( 0f, 0.35f, 0f ), 0.06f, tipPaint, 6, "Leg Spine" );
				var part = CreaturePart.Create( this, LegIndex, "Hermit Leg", 0.4f );
				leg.Knee.AddChild( part );
				leg.Segments.Add( leg.Upper );
				leg.Segments.Add( leg.Lower );
				leg.Segments.Add( leg.Tip );
				legs.Add( leg );
				index++;
			}

		// ---- The abdomen: a soft belly tucked up inside the tower's foot (the tower covers it; it shows when he rears up) ----
		abdomen = Sculpt.Joint( body, TowerBase + BellyRest, "Abdomen" );
		bellyBulge = Ball( abdomen, Vector3.Zero, BellySize, softSkin, null, "Belly" );
		bellyPart = CreaturePart.Create( this, BellyIndex, "Hermit Belly", 1.2f );
		abdomen.AddChild( bellyPart );
		bellyPart.Collider.Disabled = true;

		// ---- The lighthouse: four banded sections leant back over him ----
		tower = Sculpt.Joint( body, TowerBase, "Tower" );
		tower.Basis = new Basis( Vector3.Right, TowerTilt );
		for ( var k = 0; k < SectionCount; k++ )
		{
			var y0 = k * SectionHeight;
			var section = Sculpt.Joint( tower, new Vector3( 0f, y0, 0f ), $"Tower Section {k}" );
			var paint = new ShaderMaterial { Shader = TowerShader };
			paint.SetShaderParameter( "y0", y0 );
			towerPaints.Add( paint );
			var tr = new List<(float, float, float, float)>();
			for ( var j = 0; j <= 6; j++ )
			{
				var y = SectionHeight * j / 6f;
				var r = TowerRadius( y0 + y );
				tr.Add( (y, r, r, 0f) );
			}
			var mesh = Put( section, Cached( $"hermit tower {k}", () => Loft( tr, 0f, 1f, 36 ) ), paint, "Brick" );
			if ( k == 0 ) mesh.MaterialOverride = paint;
			sections.Add( section );
			// Barnacle clumps and the odd window.
			for ( var b = 0; b < 7 - k; b++ )
			{
				var a = rng.RandfRange( 0f, MathF.Tau );
				var y = rng.RandfRange( 0.2f, SectionHeight - 0.2f );
				var r = TowerRadius( y0 + y );
				var at = new Vector3( MathF.Sin( a ) * r, y, MathF.Cos( a ) * r );
				var outward = new Vector3( MathF.Sin( a ), 0f, MathF.Cos( a ) );
				for ( var c = 0; c < 5; c++ )
				{
					var off = new Vector3( rng.RandfRange( -0.25f, 0.25f ), rng.RandfRange( -0.25f, 0.25f ), rng.RandfRange( -0.25f, 0.25f ) );
					var br = rng.RandfRange( 0.07f, 0.13f );
					var foot = at + off - outward * 0.02f;
					Sculpt.Limb( section, foot, foot + outward * br * 0.9f, br, br * 0.55f, barnacle, "Barnacle" );
				}
			}
			if ( k is 1 or 2 or 3 )
				foreach ( var a in new[] { MathF.PI + 0.3f * k, 0.6f + k, -1.4f + k * 0.7f } )
				{
					var y = SectionHeight * 0.55f;
					var r = TowerRadius( y0 + y );
					var turn = new Basis( Vector3.Up, a );
					Sculpt.Block( section, new Vector3( MathF.Sin( a ) * (r - 0.02f), y, MathF.Cos( a ) * (r - 0.02f) ), new Vector3( 0.34f, 0.55f, 0.08f ), windowGlow, turn, "Window" );
					Sculpt.Block( section, new Vector3( MathF.Sin( a ) * (r + 0.02f), y - 0.32f, MathF.Cos( a ) * (r + 0.02f) ), new Vector3( 0.46f, 0.08f, 0.16f ), stone, turn, "Sill" );
				}
		}
		var inside = new MeshInstance3D { Name = "Tower Inside", Mesh = new CylinderMesh { TopRadius = 1.72f, BottomRadius = 1.72f, Height = 0.06f, RadialSegments = 32 }, MaterialOverride = Sculpt.Paint( new Color( 0.06f, 0.04f, 0.05f ), 0.9f ) };
		tower.AddChild( inside );
		inside.Position = Vector3.Up * 1.6f;
		// A stone lip round the bottom where he went in, and an arched door on the back.
		var lip = new MeshInstance3D { Name = "Tower Lip", Mesh = new TorusMesh { InnerRadius = 1.6f, OuterRadius = 2.0f, Rings = 40, RingSegments = 10 }, MaterialOverride = stone };
		tower.AddChild( lip );
		lip.Position = new Vector3( 0f, 0.1f, 0f );
		Sculpt.Block( sections[0], new Vector3( 0f, 1.0f, TowerRadius( 1f ) - 0.02f ), new Vector3( 0.8f, 1.3f, 0.12f ), Sculpt.Paint( new Color( 0.08f, 0.06f, 0.05f ), 0.9f ), null, "Door" );
		Ball( sections[0], new Vector3( 0f, 1.65f, TowerRadius( 1.65f ) - 0.02f ), new Vector3( 0.8f, 0.5f, 0.12f ), Sculpt.Paint( new Color( 0.08f, 0.06f, 0.05f ), 0.9f ), null, "Door Arch" );

		// The gallery: an iron deck with a railing and brackets under it.
		var gallery = Sculpt.Joint( tower, new Vector3( 0f, TowerTop, 0f ), "Gallery" );
		var deck = new MeshInstance3D { Name = "Gallery Deck", Mesh = new CylinderMesh { TopRadius = 1.75f, BottomRadius = 1.62f, Height = 0.2f, RadialSegments = 36 }, MaterialOverride = iron };
		gallery.AddChild( deck );
		var rail = new MeshInstance3D { Name = "Railing", Mesh = new TorusMesh { InnerRadius = 1.63f, OuterRadius = 1.7f, Rings = 48, RingSegments = 6 }, MaterialOverride = iron };
		gallery.AddChild( rail );
		rail.Position = Vector3.Up * 0.75f;
		for ( var k = 0; k < 18; k++ )
		{
			var a = k / 18f * MathF.Tau;
			var at = new Vector3( MathF.Sin( a ) * 1.66f, 0.1f, MathF.Cos( a ) * 1.66f );
			Sculpt.Limb( gallery, at, at + Vector3.Up * 0.68f, 0.035f, 0.035f, iron, "Post" );
			Sculpt.Cone( gallery, at * 0.92f + Vector3.Down * 0.1f, at * 0.72f + Vector3.Down * 0.55f, 0.08f, iron, 6, "Bracket" );
		}
		// Weed hanging off the deck, rippling.
		for ( var k = 0; k < 14; k++ )
		{
			var a = rng.RandfRange( 0f, MathF.Tau );
			var at = new Vector3( MathF.Sin( a ) * 1.6f, -0.05f, MathF.Cos( a ) * 1.6f );
			Fin( gallery, at, Aim( Vector3.Down, new Vector3( MathF.Cos( a ), 0f, -MathF.Sin( a ) ) ), rng.RandfRange( 0.8f, 2.2f ), rng.RandfRange( 0.09f, 0.16f ), weedPaint, 0.3f );
		}

		// The lamp room: glass and iron, and the eye inside it.
		lampRoom = Sculpt.Joint( gallery, new Vector3( 0f, 0.1f, 0f ), "Lamp Room" );
		var plinth = new MeshInstance3D { Name = "Lamp Plinth", Mesh = new CylinderMesh { TopRadius = 1.12f, BottomRadius = 1.18f, Height = 0.35f, RadialSegments = 24 }, MaterialOverride = iron };
		lampRoom.AddChild( plinth );
		plinth.Position = Vector3.Up * 0.17f;
		var pane = new MeshInstance3D { Name = "Lamp Glass", Mesh = new CylinderMesh { TopRadius = 1.05f, BottomRadius = 1.05f, Height = 1.5f, RadialSegments = 24, CapTop = false, CapBottom = false }, MaterialOverride = glass, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		lampRoom.AddChild( pane );
		pane.Position = Vector3.Up * 1.1f;
		for ( var k = 0; k < 8; k++ )
		{
			var a = k / 8f * MathF.Tau;
			var at = new Vector3( MathF.Sin( a ) * 1.06f, 0.35f, MathF.Cos( a ) * 1.06f );
			Sculpt.Limb( lampRoom, at, at + Vector3.Up * 1.5f, 0.04f, 0.04f, iron, "Mullion" );
		}
		lampEye = Sculpt.Joint( lampRoom, new Vector3( 0f, 1.1f, 0f ), "Lamp Eye" );
		Put( lampEye, SmoothBall, lampPaint, "Lamp" ).Scale = Vector3.One * 1.2f;
		lampLight = new OmniLight3D { Name = "Lamp Light", LightColor = LampGold, LightEnergy = 2.5f, OmniRange = 14f, ShadowEnabled = false };
		lampEye.AddChild( lampLight );
		var lampPart = CreaturePart.Create( this, LampIndex, "Hermit Lamp", 0.75f );
		lampRoom.AddChild( lampPart );
		lampPart.Position = Vector3.Up * 1.1f;
		// The cap: a patina dome, a vent ball and a spike.
		cap = Sculpt.Joint( lampRoom, new Vector3( 0f, 1.85f, 0f ), "Cap" );
		var dome = new MeshInstance3D { Name = "Dome", Mesh = new SphereMesh { Radius = 1.18f, Height = 1.18f, IsHemisphere = true, RadialSegments = 28, Rings = 8 }, MaterialOverride = patina };
		cap.AddChild( dome );
		dome.Scale = new Vector3( 1f, 0.8f, 1f );
		Ball( cap, Vector3.Up * 0.95f, Vector3.One * 0.36f, patina, null, "Vent" );
		Sculpt.Cone( cap, Vector3.Up * 1.1f, Vector3.Up * 2f, 0.05f, iron, 6, "Spike" );

		// The foghorn off the front of the gallery, its bell pointing ahead and a little down (in the world).
		horn = Sculpt.Joint( gallery, new Vector3( 1.05f, 0.55f, -1.15f ), "Foghorn" );
		var ahead = new Basis( Vector3.Right, TowerTilt ).Inverse() * new Vector3( 0.25f, -0.15f, -1f ).Normalized();
		horn.Basis = Aim( ahead, Vector3.Right ).Scaled( Vector3.One * 1.7f );
		var hornRings = new List<(float, float, float, float)>
		{
			(-0.3f, 0.1f, 0.1f, 0f), (0.3f, 0.12f, 0.12f, 0f), (0.7f, 0.18f, 0.18f, 0f), (1.0f, 0.3f, 0.3f, 0f), (1.15f, 0.5f, 0.5f, 0f), (1.2f, 0.56f, 0.56f, 0f),
		};
		Put( horn, Cached( "hermit horn", () => Loft( Smooth( hornRings ), 0f, 1f, 24 ) ), hornPaint, "Horn" );
		var bellMouth = new MeshInstance3D { Name = "Horn Mouth", Mesh = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 0.02f, RadialSegments = 24 }, MaterialOverride = Sculpt.Paint( new Color( 0.05f, 0.04f, 0.03f ), 0.9f ) };
		horn.AddChild( bellMouth );
		bellMouth.Position = Vector3.Up * 1.18f;
		Sculpt.Limb( gallery, new Vector3( 1.05f, 0.1f, -1.0f ), new Vector3( 1.05f, 0.55f, -1.15f ), 0.09f, 0.09f, iron, "Horn Mount" );
		var hornPart = CreaturePart.Create( this, HornIndex, "Hermit Horn", 0.45f );
		horn.AddChild( hornPart );
		hornPart.Position = Vector3.Up * 0.8f;

		// The tower's own hit shapes (stone: it soaks shots) and the thorax's.
		for ( var k = 0; k < 3; k++ )
		{
			var part = CreaturePart.Create( this, TowerIndex, "Hermit Tower", TowerRadius( k * 2.4f + 1.2f ) );
			tower.AddChild( part );
			part.Position = new Vector3( 0f, k * 2.4f + 1.2f, 0f );
		}
		var thoraxPart = CreaturePart.Create( this, ThoraxIndex, "Hermit Thorax", 1.1f );
		thorax.AddChild( thoraxPart );
		thoraxPart.Position = new Vector3( 0f, 0f, -0.2f );

		// The beams and the eruption's warning ring live in the world, not under him.
		EnsureBeams();
	}

	/// <summary> The lamp in the world (where the beam starts). </summary>
	public Vector3 LampAt => lampEye is not null && IsInstanceValid( lampEye ) ? lampEye.GlobalPosition : GlobalPosition + Vector3.Up * 9f;
	public Vector3 HornAt => horn is not null && IsInstanceValid( horn ) ? horn.GlobalTransform * (Vector3.Up * 1.1f) : GlobalPosition + Vector3.Up * 9f;
	public Node3D LampNode => lampEye;
}
