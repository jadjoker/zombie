using Godot;
using static SmoothKit;

/// <summary>
/// The Mermaid Queen's looks, on the mermaids' smooth kit (lofts through smoothed rings, round balls and tubes, curved fans,
/// skinned chains) and kept alien: an iridescent pearl hide with glowing green veins (<see cref="QueenSkin"/>), a skull swept
/// long and back, four black almond eyes on glowing green rims, a mouth of four petals over a glowing throat, a crown of
/// glowing crystal spires on a gold band, nine tentacle locks with lit tips, a jellyfish ruff round her neck, a gold shell
/// collar rising behind her head, a gold shell bodice with a glowing pearl heart, long webbed hands with claws, a scaled
/// tail with a glowing line of lights down each side (<see cref="QueenTail"/>) ending in a huge veiled fluke, and a gold
/// sceptre with an alien pearl in a coral claw. Model units; the body node is scaled by <see cref="Scale"/>.
/// </summary>
public partial class MermaidQueen
{
	Node3D body, chest, head, maw, armR, foreR, handR, armL, foreL, handL, crown, sceptre, orb, orbRings;
	readonly List<Node3D> tail = new();
	readonly List<(Node3D Hinge, Vector3 Axis)> petals = new();
	readonly List<(Skeleton3D Bones, List<Node3D> Joints)> chains = new();
	readonly List<Node3D> hair = new(), hairMid = new(), hairLow = new(), hairTip = new(), ruff = new(), spires = new();
	readonly List<MeshInstance3D> tips = new();
	ShaderMaterial skin, faceSkin, tailPaint, finPaint, veilPaint, ruffPaint, throatPaint;
	StandardMaterial3D eyeRim, crystal, tipGlow, heartGlow;
	MeshInstance3D tailMesh;
	CreaturePart throat;
	OmniLight3D glowLight, orbLight;
	Blinker blinks;
	float heartFlash;

	static StandardMaterial3D pearlPaint;
	/// <summary> A glowing pearl (her heart, her sceptre's orb, the pearls she throws). </summary>
	public static StandardMaterial3D PearlPaint => pearlPaint ??= new StandardMaterial3D
	{
		AlbedoColor = new Color( 0.95f, 0.9f, 1f ), Roughness = 0.15f, Metallic = 0.2f, RimEnabled = true, Rim = 0.8f, RimTint = 0.2f,
		EmissionEnabled = true, Emission = new Color( 0.75f, 0.6f, 1f ), EmissionEnergyMultiplier = 1.4f,
		ClearcoatEnabled = true, Clearcoat = 1f, ClearcoatRoughness = 0.05f,
	};

	static readonly Color Gold = new( 1f, 0.78f, 0.3f );
	static readonly Color EyeGreen = new( 0.35f, 1f, 0.45f );
	static readonly Color CrystalViolet = new( 0.62f, 0.35f, 1f );
	static readonly Color TipTeal = new( 0.3f, 1f, 0.9f );

	/// <summary>
	/// Her hide: a pale pearl colour, paler down the front, iridescent at the edges (teal to pink as the angle changes),
	/// laced with thin green veins that glow and pulse (brighter as she charges an attack), flashing white when hit.
	/// </summary>
	static readonly Shader QueenSkin = new()
	{
		Code = @"shader_type spatial;
uniform vec3 colour : source_color = vec3(0.74, 0.66, 0.96);
uniform vec3 belly : source_color = vec3(0.92, 0.9, 1.0);
uniform vec3 sheen_a : source_color = vec3(0.35, 1.0, 0.9);
uniform vec3 sheen_b : source_color = vec3(1.0, 0.45, 0.85);
uniform vec3 vein : source_color = vec3(0.35, 1.0, 0.45);
uniform float veins = 3.0;
uniform float front = 1.0;
uniform float glow = 0.5;
uniform float charge = 0.0;
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
	float f = smoothstep(0.1, 0.85, -ln.z) * front;
	vec3 c = mix(colour, belly, f * 0.5);
	float fr = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	vec3 irid = mix(sheen_a, sheen_b, 0.5 + 0.5 * sin(fr * 6.0 + lp.y * 3.0 + TIME * 0.3));
	c = mix(c, irid, pow(fr, 2.0) * 0.5);
	float n = noise(lp * veins + vec3(0.0, TIME * 0.04, 0.0));
	float v = pow(1.0 - abs(n * 2.0 - 1.0), 16.0);
	float pulse = 0.5 + 0.5 * sin(TIME * 2.2 - lp.y * 5.0);
	ALBEDO = mix(c, vec3(2.2), flash);
	ROUGHNESS = 0.35;
	EMISSION = irid * pow(fr, 3.0) * 0.3 + vein * v * (glow * (0.5 + 0.5 * pulse) + charge * 2.0) + vec3(1.5) * flash * 0.3;
}",
	};

	/// <summary>
	/// Her tail: rows of rounded scales edged in gold, deep violet at the hips through teal to gold at the fluke, with a line
	/// of glowing lights down each side that pulse in a wave toward the tip (faster as she charges). UV.y 0 at the hips to
	/// 1 at the fluke, UV.x round the tail (its sides at 0.25 and 0.75).
	/// </summary>
	static readonly Shader QueenTail = new()
	{
		Code = @"shader_type spatial;
uniform vec3 top : source_color = vec3(0.36, 0.12, 0.62);
uniform vec3 mid : source_color = vec3(0.1, 0.55, 0.65);
uniform vec3 tip : source_color = vec3(1.0, 0.75, 0.3);
uniform vec3 edge : source_color = vec3(1.0, 0.8, 0.35);
uniform vec3 light : source_color = vec3(0.3, 1.0, 0.9);
uniform float rows = 58.0;
uniform float around = 16.0;
uniform float charge = 0.0;
uniform float flash = 0.0;
void fragment() {
	vec2 g = vec2(UV.x * around, UV.y * rows);
	float row = floor(g.y);
	g.x += mod(row, 2.0) * 0.5;
	vec2 f = vec2(fract(g.x) - 0.5, fract(g.y));
	float d = length(f * vec2(1.0, 0.85));
	float rim = smoothstep(0.38, 0.5, d);
	float shine = (1.0 - smoothstep(0.0, 0.35, d)) * 0.18;
	vec3 c = mix(top, mid, smoothstep(0.0, 0.55, UV.y));
	c = mix(c, tip, smoothstep(0.55, 1.0, UV.y));
	c = mix(c, edge, rim * 0.45) + vec3(shine);
	// The lights: a dot every few rows down each side.
	float side = min(abs(UV.x - 0.25), abs(UV.x - 0.75)) * around;
	float dot_y = fract(UV.y * rows / 3.0) - 0.5;
	float spot = 1.0 - smoothstep(0.18, 0.34, length(vec2(side, dot_y * 1.2)));
	float wave = 0.5 + 0.5 * sin(UV.y * 22.0 - TIME * (3.0 + charge * 6.0));
	float fr = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = mix(c, vec3(2.2), flash);
	ROUGHNESS = 0.3;
	METALLIC = 0.2;
	EMISSION = c * fr * 0.3 + light * spot * (0.6 + 1.6 * wave + charge * 1.5);
}",
	};

	/// <summary> Her throat: dark red flesh glowing pink and green from deep inside, brighter while she sings. </summary>
	static readonly Shader ThroatShader = new()
	{
		Code = @"shader_type spatial;
uniform float open = 0.0;
varying vec3 lp;
void vertex() { lp = VERTEX; }
void fragment() {
	float r = length(lp.xy) * 2.0;
	float rings = 0.5 + 0.5 * sin(r * 18.0 - TIME * 9.0);
	vec3 deep = mix(vec3(0.35, 1.0, 0.5), vec3(1.0, 0.35, 0.8), r);
	ALBEDO = vec3(0.25, 0.02, 0.06);
	ROUGHNESS = 0.4;
	EMISSION = deep * (1.0 - r) * (0.4 + open * (1.6 + rings * 1.4));
}",
	};

	void Chain( Node3D parent, List<Node3D> joints, float step, float above, float below, Func<float, (float Rx, float Rz)> radius, Material paint, string key, int rings, int sides ) =>
		chains.Add( (SkinChain( parent, joints, step, above, below, radius, paint, key, rings, sides ), joints) );

	void PoseChains() { foreach ( var (bones, joints) in chains ) PoseChain( bones, joints ); }

	const int TailJoints = 11;
	const float TailStep = 0.4f;

	static float TailRadius( float t ) => MathX.Lerp( 0.46f, 0.1f, MathF.Pow( MathX.Clamp( t, 0f, 1f ), 0.85f ) ) + MathF.Sin( MathX.Clamp( t / 0.35f, 0f, 1f ) * MathF.PI ) * 0.06f;

	/// <summary> When she bursts: `count` joints of the tail from `from` get a plain mesh of their own, to fly off as a piece. </summary>
	void TailPieceFrom( int from, int count )
	{
		if ( from >= tail.Count || !IsInstanceValid( tail[from] ) || tailPaint is null ) return;
		var rings = new List<(float, float, float, float)>();
		var length = TailStep * count;
		var whole = TailStep * (TailJoints - 1) + 0.42f;
		for ( var k = 0; k <= 12; k++ )
		{
			var y = -length * k / 12f;
			var r = TailRadius( (0.1f + TailStep * from - y) / whole );
			rings.Add( (y, r, r * 0.82f, 0f) );
		}
		Put( tail[from], Loft( rings, 0.45f, 1f, 24 ), tailPaint, "Tail Piece" );
	}

	ShaderMaterial Skin( Color colour, bool front, float veins )
	{
		var m = new ShaderMaterial { Shader = QueenSkin };
		m.SetShaderParameter( "colour", colour );
		m.SetShaderParameter( "belly", colour.Lerp( Colors.White, 0.55f ) );
		m.SetShaderParameter( "front", front ? 1f : 0f );
		m.SetShaderParameter( "veins", veins );
		return m;
	}

	void Build()
	{
		skin = Skin( SkinColour, true, 3.2f );
		faceSkin = Skin( SkinColour, false, 5f );
		tailPaint = new ShaderMaterial { Shader = QueenTail };
		finPaint = FinMaterial( new Color( 0.75f, 0.55f, 1f ), new Color( 0.35f, 1f, 0.85f ), 0.6f, 0.14f );
		veilPaint = FinMaterial( new Color( 1f, 0.55f, 0.85f ), new Color( 0.55f, 0.3f, 1f ), 0.45f, 0.22f );
		ruffPaint = FinMaterial( new Color( 0.55f, 0.95f, 1f ), new Color( 0.9f, 0.5f, 1f ), 0.42f, 0.18f );
		var gold = Sculpt.Paint( Gold, 0.2f, 0.9f );
		gold.EmissionEnabled = true;
		gold.Emission = Gold;
		gold.EmissionEnergyMultiplier = 0.12f;
		var goldShell = (StandardMaterial3D)gold.Duplicate();
		goldShell.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		crystal = new StandardMaterial3D
		{
			AlbedoColor = CrystalViolet with { A = 0.9f }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f, Metallic = 0.3f,
			EmissionEnabled = true, Emission = CrystalViolet, EmissionEnergyMultiplier = 1f, RimEnabled = true, Rim = 0.6f, RimTint = 0.8f,
		};
		tipGlow = Sculpt.Glow( TipTeal, 1.8f );
		eyeRim = Sculpt.Glow( EyeGreen, 1.6f );
		heartGlow = (StandardMaterial3D)PearlPaint.Duplicate();
		var black = Sculpt.Paint( new Color( 0.015f, 0.015f, 0.03f ), 0.04f );
		black.ClearcoatEnabled = true;
		black.Clearcoat = 1f;
		black.ClearcoatRoughness = 0.02f;
		var claw = Sculpt.Paint( new Color( 0.2f, 0.12f, 0.3f ), 0.2f, 0.3f );
		var hairPaint = new ShaderMaterial { Shader = QueenSkin };
		hairPaint.SetShaderParameter( "colour", new Color( 0.3f, 0.12f, 0.45f ) );
		hairPaint.SetShaderParameter( "belly", new Color( 0.3f, 0.12f, 0.45f ) );
		hairPaint.SetShaderParameter( "front", 0f );
		hairPaint.SetShaderParameter( "veins", 9f );
		hairPaint.SetShaderParameter( "vein", TipTeal );

		body = new Node3D { Name = "Body", Scale = Vector3.One * Scale * shown };
		AddChild( body );
		glowLight = new OmniLight3D { Name = "Queen Glow", LightColor = new Color( 0.7f, 0.5f, 1f ), LightEnergy = 1.3f, OmniRange = 13f, ShadowEnabled = false, Position = Vector3.Up * 1.2f };
		AddChild( glowLight );

		// ---- The tail: eleven joints, one skinned mesh, a veiled fluke, hip and ridge fins ----
		Node3D parent = body;
		var at = new Vector3( 0f, -0.15f, 0f );
		for ( var i = 0; i < TailJoints; i++ )
		{
			var joint = Sculpt.Joint( parent, at, $"Tail {i}" );
			tail.Add( joint );
			parent = joint;
			at = new Vector3( 0f, -TailStep, 0f );
		}
		var bones = SkinChain( body, tail, TailStep, 0.1f, 0.34f, t => (TailRadius( t ), TailRadius( t ) * 0.82f), tailPaint, "Queen Tail", 80, 28 );
		chains.Add( (bones, tail) );
		tailMesh = bones.GetChildOrNull<MeshInstance3D>( 0 );
		foreach ( var s in new[] { -1f, 1f } )
		{
			Fin( parent, new Vector3( s * 0.05f, -0.32f, 0f ), new Basis( Vector3.Back, s * 0.9f ) * new Basis( Vector3.Right, 0.12f ), 1.35f, 0.85f, finPaint, 0.08f );
			Fin( parent, new Vector3( s * 0.03f, -0.3f, 0.02f ), new Basis( Vector3.Back, s * 0.35f ), 1.9f, 0.7f, veilPaint, 0.05f );
		}
		Fin( parent, new Vector3( 0f, -0.3f, 0.03f ), Basis.Identity, 2.2f, 0.75f, veilPaint, 0.04f );
		foreach ( var s in new[] { -1f, 1f } )
		{
			Fin( tail[1], new Vector3( s * 0.4f, -0.1f, 0.05f ), new Basis( Vector3.Back, s * 1.0f ) * new Basis( Vector3.Right, -0.25f ), 0.75f, 0.5f, finPaint );
			Fin( tail[6], new Vector3( s * 0.26f, -0.1f, 0.03f ), new Basis( Vector3.Back, s * 1.15f ) * new Basis( Vector3.Right, -0.2f ), 0.6f, 0.36f, veilPaint );
		}
		for ( var i = 2; i <= 8; i++ )
			Fin( tail[i], new Vector3( 0f, -0.05f, TailRadius( i / (float)TailJoints ) * 0.75f ), new Basis( Vector3.Right, -1.25f ), 0.42f, 0.34f, finPaint, 0.05f );

		// ---- The torso, slender and tall ----
		chest = Sculpt.Joint( body, new Vector3( 0f, 0.15f, 0f ), "Chest" );
		var torso = new List<(float, float, float, float)>
		{
			(-0.34f, 0.43f, 0.36f, 0f),
			(-0.05f, 0.37f, 0.3f, 0f),
			(0.24f, 0.27f, 0.23f, -0.01f),
			(0.52f, 0.3f, 0.25f, -0.02f),
			(0.78f, 0.36f, 0.27f, -0.03f),
			(0.98f, 0.41f, 0.26f, -0.01f),
			(1.12f, 0.33f, 0.21f, 0.01f),
			(1.22f, 0.16f, 0.14f, 0.02f),
			(1.46f, 0.13f, 0.12f, 0f),
		};
		Put( chest, Cached( "queen torso", () => Loft( Smooth( torso ) ) ), skin, "Torso" );
		// A gold belt with pearl drops, and the soft fins round the hips where skin meets scales.
		var belt = new MeshInstance3D { Name = "Belt", Mesh = new TorusMesh { InnerRadius = 0.37f, OuterRadius = 0.44f, Rings = 40, RingSegments = 12 }, MaterialOverride = gold };
		chest.AddChild( belt );
		belt.Position = new Vector3( 0f, -0.18f, 0f );
		belt.Scale = new Vector3( 1f, 1f, 0.85f );
		for ( var k = 0; k < 7; k++ )
		{
			var a = (k - 3f) * 0.33f;
			var top = new Vector3( MathF.Sin( a ) * 0.42f, -0.2f, -MathF.Cos( a ) * 0.36f );
			var bottom = top + new Vector3( 0f, -0.18f - (k % 2) * 0.08f, -0.03f );
			Tube( chest, top, bottom, 0.012f, 0.012f, gold, "Pearl String" );
			Ball( chest, bottom, Vector3.One * 0.07f, PearlPaint, null, "Pearl Drop" );
		}
		for ( var k = 0; k < 12; k++ )
		{
			var a = (k + 0.5f) / 12f * MathF.Tau;
			var foot = new Vector3( MathF.Sin( a ) * 0.4f, -0.22f, -MathF.Cos( a ) * 0.33f );
			var down = new Vector3( MathF.Sin( a ) * 0.5f, -1f, -MathF.Cos( a ) * 0.45f );
			Fin( chest, foot, Aim( down, new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) ), 0.42f, 0.34f, finPaint, 0.1f );
		}
		// The bodice: two gold scallops, and her heart, a glowing pearl in a gold setting between them.
		foreach ( var s in new[] { -1f, 1f } )
			Scallop( chest, new Vector3( s * 0.17f, 0.7f, -0.27f ), new Basis( Vector3.Back, MathF.PI - s * 0.14f ) * new Basis( Vector3.Right, -0.25f ), 0.3f, 0.36f, goldShell );
		var heart = Ball( chest, new Vector3( 0f, 0.8f, -0.31f ), Vector3.One * 0.19f, heartGlow, null, "Heart Pearl" );
		var setting = new MeshInstance3D { Name = "Heart Setting", Mesh = new TorusMesh { InnerRadius = 0.09f, OuterRadius = 0.13f, Rings = 24, RingSegments = 8 }, MaterialOverride = gold };
		chest.AddChild( setting );
		setting.Transform = new Transform3D( new Basis( Vector3.Right, MathF.PI * 0.5f ), new Vector3( 0f, 0.8f, -0.29f ) );
		// Rows of small lights down her flanks (photophores).
		foreach ( var s in new[] { -1f, 1f } )
			for ( var k = 0; k < 6; k++ )
			{
				var y = 0.05f + k * 0.17f;
				var r = k < 3 ? 0.33f : 0.35f;
				Ball( chest, new Vector3( s * r, y, 0.04f ), Vector3.One * 0.04f, tipGlow, null, "Photophore" );
			}
		// The jellyfish ruff round her neck: a ring of see-through fins that ripple.
		for ( var k = 0; k < 14; k++ )
		{
			var a = (k + 0.5f) / 14f * MathF.Tau;
			var foot = new Vector3( MathF.Sin( a ) * 0.18f, 1.26f, -MathF.Cos( a ) * 0.16f );
			var outward = new Vector3( MathF.Sin( a ), -0.55f, -MathF.Cos( a ) );
			var fin = Sculpt.Joint( chest, foot, "Ruff" );
			Fin( fin, Vector3.Zero, Aim( outward, new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) ), 0.48f, 0.3f, ruffPaint, 0.18f );
			ruff.Add( fin );
		}
		// The gold shell collar rising behind her head, ribbed like a scallop.
		foreach ( var s in new[] { -1f, 1f } )
			Scallop( chest, new Vector3( s * 0.16f, 1.15f, 0.2f ), Aim( new Vector3( s * 0.55f, 1f, 0.35f ), new Vector3( 1f, 0f, -s * 0.4f ) ), 0.95f, 0.75f, goldShell );

		// ---- The head: a long skull swept back, four eyes, a mouth of petals, a crown, tentacle hair ----
		head = Sculpt.Joint( chest, new Vector3( 0f, 1.44f, -0.02f ), "Head" );
		var skull = new List<(float, float, float, float)>
		{
			(-0.02f, 0.04f, 0.04f, -0.08f),
			(0.04f, 0.11f, 0.12f, -0.07f),
			(0.14f, 0.2f, 0.23f, -0.04f),
			(0.28f, 0.28f, 0.29f, 0f),
			(0.42f, 0.3f, 0.31f, 0.03f),
			(0.56f, 0.28f, 0.32f, 0.07f),
			(0.68f, 0.24f, 0.31f, 0.16f),
			(0.78f, 0.18f, 0.27f, 0.3f),
			(0.84f, 0.11f, 0.19f, 0.44f),
			(0.86f, 0.03f, 0.05f, 0.52f),
		};
		Put( head, Cached( "queen skull", () => Loft( Smooth( skull ), 0f, 1f, 32 ) ), faceSkin, "Head" );
		// Cheek ridges and a brow ridge, low and smooth.
		foreach ( var s in new[] { -1f, 1f } )
			Ball( head, new Vector3( s * 0.19f, 0.2f, -0.18f ), new Vector3( 0.12f, 0.06f, 0.16f ), faceSkin, new Basis( Vector3.Up, s * 0.5f ), "Cheek" );
		// The eyes: a big pair low, a small pair above at the temples, each a black almond on a glowing green rim.
		void Eye( Vector3 eyeAt, Vector3 size, float slant, float turn )
		{
			var socket = Sculpt.Joint( head, eyeAt, "Eye" );
			socket.Basis = new Basis( Vector3.Up, turn );
			var almond = Sculpt.Joint( socket, Vector3.Zero, "Almond" );
			almond.Basis = new Basis( Vector3.Back, slant );
			var rim = Ball( almond, new Vector3( 0f, 0f, 0.008f ), size * new Vector3( 1.16f, 1.3f, 0.9f ), eyeRim, null, "Eye Rim" );
			var ball = Ball( almond, Vector3.Zero, size, black, null, "Eye" );
			(blinks ??= Blinker.Add( this )).Eye( ball, rim );
		}
		foreach ( var s in new[] { -1f, 1f } )
		{
			Eye( new Vector3( s * 0.14f, 0.32f, -0.265f ), new Vector3( 0.3f, 0.145f, 0.08f ), -s * 0.5f, s * 0.3f );
			Eye( new Vector3( s * 0.2f, 0.48f, -0.2f ), new Vector3( 0.14f, 0.06f, 0.05f ), -s * 0.65f, s * 0.6f );
			// Fin ears, long and pointed, swept back.
			Fin( head, new Vector3( s * 0.28f, 0.38f, 0.08f ), new Basis( Vector3.Up, -s * 1.1f ) * new Basis( Vector3.Back, s * 2.1f ), 0.55f, 0.13f, finPaint, 0.05f );
		}
		// The mouth: four petals of skin meeting over the throat; they split open to sing and bite.
		maw = Sculpt.Joint( head, new Vector3( 0f, 0.12f, -0.27f ), "Maw" );
		throatPaint = new ShaderMaterial { Shader = ThroatShader };
		Ball( maw, new Vector3( 0f, 0f, 0.045f ), new Vector3( 0.13f, 0.13f, 0.05f ), throatPaint, null, "Throat" );
		var tooth = Sculpt.Paint( new Color( 0.96f, 0.95f, 0.9f ), 0.2f );
		for ( var k = 0; k < 12; k++ )
		{
			var a = k / 12f * MathF.Tau;
			var dir = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f );
			Sculpt.Cone( maw, dir * 0.068f + Vector3.Back * 0.03f, dir * 0.03f + Vector3.Back * 0.022f, 0.01f, tooth, 6, "Tooth" );
		}
		foreach ( var dir in new[] { Vector3.Up, Vector3.Down, Vector3.Left, Vector3.Right } )
		{
			var across = MathF.Abs( dir.X ) > 0.5f;
			var hinge = Sculpt.Joint( maw, dir * 0.08f + Vector3.Forward * 0.005f, "Petal" );
			var size = across ? new Vector3( 0.1f, 0.15f, 0.028f ) : new Vector3( 0.16f, 0.1f, 0.028f );
			Ball( hinge, -dir * 0.045f, size, faceSkin, null, "Petal" );
			// The petal's inner face glows faintly green when it opens.
			Ball( hinge, -dir * 0.045f + Vector3.Back * 0.014f, size * new Vector3( 0.8f, 0.8f, 0.6f ), eyeRim, null, "Petal Inside" );
			petals.Add( (hinge, dir.Cross( Vector3.Back ).Normalized()) );
		}

		// The crown: a gold band tilted back, crystal spires round it (tallest in the middle), a green jewel at the front.
		crown = Sculpt.Joint( head, new Vector3( 0f, 0.56f, 0.06f ), "Crown" );
		crown.Rotation = new Vector3( 0.32f, 0f, 0f );
		var band = new MeshInstance3D { Name = "Crown Band", Mesh = new TorusMesh { InnerRadius = 0.25f, OuterRadius = 0.3f, Rings = 40, RingSegments = 10 }, MaterialOverride = gold };
		crown.AddChild( band );
		band.Scale = new Vector3( 1f, 1f, 1.1f );
		for ( var k = 0; k < 9; k++ )
		{
			var a = (k - 4f) * 0.32f;
			var foot = new Vector3( MathF.Sin( a ) * 0.27f, 0.02f, -MathF.Cos( a ) * 0.3f );
			var tall = 0.62f - MathF.Abs( k - 4f ) * 0.1f;
			var spire = Sculpt.Joint( crown, foot, "Spire" );
			spire.Basis = new Basis( Vector3.Back, -MathF.Sin( a ) * 0.35f ) * new Basis( Vector3.Right, -0.15f );
			Sculpt.Cone( spire, Vector3.Zero, Vector3.Up * tall, 0.055f, crystal, 6, "Crystal" );
			Sculpt.Cone( spire, Vector3.Up * 0.02f + Vector3.Right * 0.04f, Vector3.Up * tall * 0.55f + Vector3.Right * 0.07f, 0.03f, crystal, 6, "Crystal" );
			spires.Add( spire );
		}
		Ball( crown, new Vector3( 0f, 0.04f, -0.31f ), new Vector3( 0.11f, 0.13f, 0.06f ), Sculpt.Glow( EyeGreen, 2.4f ), null, "Crown Jewel" );
		// Two gold fins sweeping back from the band.
		foreach ( var s in new[] { -1f, 1f } )
			Fin( crown, new Vector3( s * 0.26f, 0.02f, 0.05f ), new Basis( Vector3.Up, -s * 1.3f ) * new Basis( Vector3.Back, s * 2.2f ), 0.45f, 0.2f, goldShell, 0.05f );

		// Tentacle hair: nine locks off the back of the skull, skinned on four joints each, lit at the tips.
		for ( var k = 0; k < 9; k++ )
		{
			var x = (k - 4f) * 0.07f;
			var length = 1.5f + (k % 3) * 0.2f - MathF.Abs( k - 4f ) * 0.06f;
			var step = length / 3.4f;
			var root = Sculpt.Joint( head, new Vector3( x, 0.5f - MathF.Abs( x ) * 0.3f, 0.26f + (k % 2) * 0.03f ), $"Tentacle {k}" );
			var j1 = Sculpt.Joint( root, new Vector3( 0f, -step, 0f ), "Mid" );
			var j2 = Sculpt.Joint( j1, new Vector3( 0f, -step, 0f ), "Low" );
			var j3 = Sculpt.Joint( j2, new Vector3( 0f, -step, 0f ), "Tip" );
			Chain( head, new List<Node3D> { root, j1, j2, j3 }, step, 0.05f, step * 0.5f,
				t => (MathX.Lerp( 0.075f, 0.022f, MathF.Pow( t, 1.2f ) ), MathX.Lerp( 0.06f, 0.018f, t )), hairPaint, $"Queen Hair {k} {length:F2}", 24, 12 );
			tips.Add( Ball( j3, new Vector3( 0f, -step * 0.5f, 0f ), Vector3.One * 0.075f, tipGlow, null, "Tentacle Light" ) );
			hair.Add( root );
			hairMid.Add( j1 );
			hairLow.Add( j2 );
			hairTip.Add( j3 );
		}

		// ---- Arms: long, with fins, gold cuffs, webbed hands with dark claws ----
		(Node3D Arm, Node3D Fore, Node3D Hand) Arm( float s )
		{
			var arm = Sculpt.Joint( chest, new Vector3( s * 0.44f, 1.04f, 0f ), s > 0 ? "Right Arm" : "Left Arm" );
			Ball( arm, Vector3.Zero, Vector3.One * 0.22f, skin, null, "Shoulder" );
			Tube( arm, Vector3.Zero, new Vector3( 0f, -0.62f, 0f ), 0.1f, 0.08f, skin, "Upper Arm" );
			var fore = Sculpt.Joint( arm, new Vector3( 0f, -0.62f, 0f ), "Elbow" );
			Tube( fore, Vector3.Zero, new Vector3( 0f, -0.58f, 0f ), 0.08f, 0.06f, skin, "Forearm" );
			Fin( fore, new Vector3( 0f, -0.06f, 0.06f ), new Basis( Vector3.Right, -0.3f ) * new Basis( Vector3.Up, MathF.PI * 0.5f ), 0.55f, 0.22f, finPaint, 0.06f );
			var cuff = new MeshInstance3D { Name = "Cuff", Mesh = new TorusMesh { InnerRadius = 0.065f, OuterRadius = 0.1f, Rings = 24, RingSegments = 10 }, MaterialOverride = gold };
			fore.AddChild( cuff );
			cuff.Position = new Vector3( 0f, -0.45f, 0f );
			var hand = Sculpt.Joint( fore, new Vector3( 0f, -0.6f, 0f ), "Hand" );
			Ball( hand, new Vector3( 0f, -0.03f, 0f ), new Vector3( 0.14f, 0.18f, 0.09f ), skin, null, "Palm" );
			Fin( hand, new Vector3( 0f, -0.05f, 0f ), Basis.Identity, 0.34f, 0.3f, finPaint, 0.15f );
			for ( var k = 0; k < 4; k++ )
			{
				var x = (k - 1.5f) * 0.045f;
				Sculpt.Cone( hand, new Vector3( x, -0.1f, -0.02f ), new Vector3( x * 1.3f, -0.36f, -0.06f ), 0.014f, claw, 6, "Claw" );
			}
			return (arm, fore, hand);
		}
		(armR, foreR, handR) = Arm( 1f );
		(armL, foreL, handL) = Arm( -1f );

		// ---- The sceptre: a gold staff, a coral claw round a glowing alien pearl, rings of light turning round it ----
		sceptre = Sculpt.Joint( handR, new Vector3( 0f, -0.05f, 0f ), "Sceptre" );
		Tube( sceptre, new Vector3( 0f, -1.4f, 0f ), new Vector3( 0f, 1.5f, 0f ), 0.045f, 0.04f, gold, "Staff" );
		Sculpt.Cone( sceptre, new Vector3( 0f, -1.4f, 0f ), new Vector3( 0f, -1.75f, 0f ), 0.05f, gold, 8, "Foot" );
		var coral = Sculpt.Paint( new Color( 0.95f, 0.4f, 0.55f ), 0.5f );
		for ( var k = 0; k < 4; k++ )
		{
			var a = k / 4f * MathF.Tau + 0.4f;
			var side = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			var bend = new Vector3( 0f, 1.62f, 0f ) + side * 0.26f;
			Tube( sceptre, new Vector3( 0f, 1.48f, 0f ) + side * 0.04f, bend, 0.035f, 0.025f, coral, "Coral" );
			Tube( sceptre, bend, new Vector3( 0f, 2.0f, 0f ) + side * 0.1f, 0.025f, 0.008f, coral, "Coral" );
		}
		orb = Sculpt.Joint( sceptre, new Vector3( 0f, 1.78f, 0f ), "Orb" );
		Ball( orb, Vector3.Zero, Vector3.One * 0.32f, PearlPaint, null, "Orb Pearl" );
		orbRings = Sculpt.Joint( orb, Vector3.Zero, "Orb Rings" );
		var ringGlow = Sculpt.Glow( TipTeal, 2.2f );
		foreach ( var tilt in new[] { 0.5f, -0.9f } )
		{
			var ring = new MeshInstance3D { Name = "Orb Ring", Mesh = new TorusMesh { InnerRadius = 0.27f, OuterRadius = 0.29f, Rings = 36, RingSegments = 5 }, MaterialOverride = ringGlow, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			orbRings.AddChild( ring );
			ring.Rotation = new Vector3( tilt, 0f, tilt * 0.6f );
		}
		orbLight = new OmniLight3D { Name = "Orb Light", LightColor = new Color( 0.7f, 0.6f, 1f ), LightEnergy = 1f, OmniRange = 5f, ShadowEnabled = false };
		orb.AddChild( orbLight );

		// What can be shot: head (0), her heart (1), chest (2), belly (3), the tail (4 to 6), the throat (7, open only to sing).
		CreaturePart Hit( Node3D on, int index, Vector3 where, float r )
		{
			var part = CreaturePart.Create( this, index, $"Queen {index}", r );
			on.AddChild( part );
			part.Position = where;
			return part;
		}
		Hit( head, 0, new Vector3( 0f, 0.32f, 0.02f ), 0.34f );
		Hit( chest, 1, new Vector3( 0f, 0.8f, -0.33f ), 0.2f );
		Hit( chest, 2, new Vector3( 0f, 0.9f, 0.02f ), 0.44f );
		Hit( chest, 3, new Vector3( 0f, 0.22f, 0f ), 0.42f );
		for ( var i = 0; i < 3; i++ ) Hit( tail[2 + i * 3], 4 + i, new Vector3( 0f, -0.2f, 0f ), MathX.Lerp( 0.44f, 0.28f, i / 2f ) );
		throat = Hit( maw, ThroatIndex, new Vector3( 0f, 0f, -0.05f ), 0.13f );
		throat.Collider.Disabled = true;
	}

	public Vector3 OrbAt => orb is not null && IsInstanceValid( orb ) ? orb.GlobalPosition : GlobalPosition + Vector3.Up * 2f;
	/// <summary> The head, for close-up test shots. </summary>
	public Node3D HeadNode => head;
}
