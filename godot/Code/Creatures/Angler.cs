using Godot;

/// <summary>
/// The deep sea angler (user's request; the wreck site and Atlantis, from the second stage): a tall, round, blue and
/// green scaled fish (about 30% of the scales yellow) with purple spots, a jutting underbite full of huge needle teeth, two bulging compound eyes made
/// of hex facets like a fly's, and on a long stalk off its brow a whole lantern glowing green. It swims slowly, the
/// lantern bobbing ahead of it. Attacks: it swings the lantern at you on its stalk (a wind-up, then a whip), and up
/// close it opens its jaw wide and lunges in to chomp; from further off its jaw gapes and it shoots a barbed claw on a line
/// (up to 8 m) that pierces the player and slowly reels them in for a bite. Built in code (SmoothKit lofts, Sculpt shapes), metres, facing
/// -Z. Its eyes are the weak spots; the lantern takes more than the body.
/// </summary>
public partial class Angler : Creature
{
	public static readonly List<Angler> All = new();

	public float Speed = 3.14f;            // m/s: slow and steady (8% up from 2.91)
	public float HoverHeight = 1.5f;       // body centre over the floor: the mouth at the player's chest
	public float SwingRange = 4.4f;        // from the mouth: the lantern reaches this far
	public float SwingDamage = 16f;
	public float SwingCooldown = 3.2f;
	public float ChompRange = 2.4f;
	public float ChompDamage = 24f;
	public float ChompCooldown = 2.2f;
	public const float ModelScale = 1.05f;  // the whole model (body, jaw, eyes, stalk, lantern) is drawn this much bigger
	public float HookRange = 8f;          // the claw on its line shoots this far out of the mouth
	public float HookSpeed = 18f;         // m/s out
	public float HookDamage = 8f;         // when it pierces
	public float ReelSpeed = 1.89f;       // m/s: slowly in
	public float HookBiteDamage = 30f;    // the bite at the end of the reel
	public float HookCooldown = 8f;
	public float HookBreak = 0.1f;        // a share of its health shot off while reeling snaps the line
	public float EyeMultiplier = 2.2f;
	public float LanternMultiplier = 1.5f;

	/// <summary> Tests: swings and chomps that landed on a player. </summary>
	public static int SwingHits, ChompHits, HookCatches, HookBites;
	public static float LastSwingMiss;   // how far the lantern was from the chest on the last swing (tests)
	bool lastChomped;

	static readonly Color DeepBlue = new( 0.06f, 0.16f, 0.42f );
	static readonly Color Blue = new( 0.12f, 0.36f, 0.78f );
	static readonly Color Teal = new( 0.08f, 0.55f, 0.6f );
	static readonly Color Green = new( 0.2f, 0.7f, 0.38f );
	static readonly Color Purple = new( 0.62f, 0.2f, 0.95f );
	static readonly Color LanternGreen = new( 0.35f, 1f, 0.35f );
	static readonly Color ToothColor = new( 0.96f, 0.93f, 0.8f );
	static readonly Color GooColor = new( 0.35f, 1f, 0.5f );
	public override GibRecipe Gibs => GibRecipes.Angler( GooColor, Blue, Green, ToothColor );

	enum Act { Swim, Swing, Chomp, Hook }

	// The hook: aimed out of the gaping mouth, flying, caught in a player and reeling them in, or coming back empty.
	enum Line { Stowed, Out, Reel, Back }
	Line line = Line.Stowed;
	Node3D hook;
	MeshInstance3D hookLine;
	Vector3 hookAt, hookDir;
	float hookOut, reelStartHealth;
	PlayerSurvivor hooked;
	TimeSince sinceHook = 99f;

	Node3D body, jaw, tailFin;
	readonly List<Node3D> stalk = new();
	readonly List<Node3D> pectorals = new();
	Node3D lantern;
	OmniLight3D lanternLight;
	MeshInstance3D lanternGlass;
	ShaderMaterial skin;
	Vector3 velocity;
	Act act = Act.Swim;
	TimeSince sinceAct = 99f;
	TimeSince sinceSwing = 99f, sinceChomp = 99f;
	TimeSince sinceHit = 99f, sinceFloor = 99f;
	bool struck;
	float floorY, phase;
	float jawOpen, jawVel;
	float swingPose, swingVel;
	float lanternTilt, lanternTiltVel;

	// Where things sit on the body (metres, facing -Z).
	static readonly Vector3 Hinge = new( 0f, -0.32f, -0.12f );
	static readonly Vector3 StalkBase = new( 0f, 0.78f, -0.38f );
	static readonly Vector3[] StalkSegments = { new( 0f, 0.36f, -0.12f ), new( 0f, 0.28f, -0.3f ), new( 0f, 0.06f, -0.38f ), new( 0f, -0.16f, -0.28f ) };

	public static Angler Spawn( Node parent, Vector3 position, float maxHealth ) =>
		Place( new Angler { Name = "Angler" }, parent, position, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	protected override void Setup()
	{
		BeginSetup( 300f );
		phase = Rng.Float( 0f, MathF.Tau );
		floorY = Sculpt.FloorBelow( this, GlobalPosition );
		sinceSwing = Rng.Float( 0f, SwingCooldown );
		Build();
	}

	// ---- Looks ----

	/// <summary>
	/// Its skin: rows of rounded scales in blues and greens (like Poseidon's tail, in sea colours), a paler belly and big
	/// purple spots scattered over it. UV.x round the body, UV.y nose to tail.
	/// </summary>
	static readonly Shader SkinShader = new()
	{
		Code = @"shader_type spatial;
uniform vec3 c0 : source_color = vec3(0.06, 0.16, 0.42);
uniform vec3 c1 : source_color = vec3(0.12, 0.36, 0.78);
uniform vec3 c2 : source_color = vec3(0.08, 0.55, 0.6);
uniform vec3 c3 : source_color = vec3(0.2, 0.7, 0.38);
uniform vec3 c4 : source_color = vec3(0.95, 0.8, 0.12);   // yellow: about 30% of the scales
uniform vec3 spot : source_color = vec3(0.62, 0.2, 0.95);
uniform float rows = 44.0;
uniform float around = 34.0;
uniform float flash = 0.0;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
void fragment() {
	vec2 g = vec2(UV.x * around, UV.y * rows);
	float row = floor(g.y);
	g.x += mod(row, 2.0) * 0.5;
	vec2 cell = vec2(floor(g.x), row);
	vec2 f = vec2(fract(g.x) - 0.5, fract(g.y));
	float d = length(f * vec2(1.0, 0.85));
	float h = hash(vec2(mod(cell.x, around), cell.y));
	vec3 c = h < 0.3 ? c4 : h < 0.545 ? c1 : h < 0.72 ? c2 : h < 0.874 ? c3 : c0;
	float edge = smoothstep(0.36, 0.5, d);
	float inset = 1.0 - smoothstep(0.0, 0.03, abs(d - 0.24));
	c = c * (1.0 - edge * 0.45 - inset * 0.25) + vec3((1.0 - smoothstep(0.0, 0.3, d)) * 0.12);
	// The belly (round the bottom of the loft) is paler.
	float belly = smoothstep(0.55, 1.0, cos(UV.x * 6.2832));
	c = mix(c, c * 0.6 + vec3(0.3, 0.42, 0.5), belly * 0.55);
	// Purple spots: a coarse grid, about half its cells holding a round spot, off centre.
	vec2 sg = vec2(UV.x * 11.0, UV.y * 9.0);
	vec2 sc = floor(sg);
	float sh = hash(sc + 7.0);
	vec2 so = vec2(hash(sc + 1.3), hash(sc + 4.1)) * 0.5 + 0.25;
	float sr = 0.14 + 0.16 * hash(sc + 9.7);
	float s = (sh > 0.45) ? 1.0 - smoothstep(sr * 0.8, sr, length(fract(sg) - so)) : 0.0;
	c = mix(c, spot, s * 0.9);
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = mix(c, vec3(2.2), flash);
	ROUGHNESS = 0.32;
	METALLIC = 0.15;
	EMISSION = spot * s * 0.35 + c * rim * 0.35;
}",
	};

	/// <summary>
	/// A compound eye: a dome of hexagonal facets like a fly's, each its own glossy dark red with a green and purple
	/// sheen that shifts with the view, dark seams between them.
	/// </summary>
	static readonly Shader EyeShader = new()
	{
		Code = @"shader_type spatial;
uniform float facets = 30.0;
uniform float flash = 0.0;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
void fragment() {
	vec2 p = vec2(UV.x * facets * 2.0, UV.y * facets);
	vec2 r = vec2(1.0, 1.732);
	vec2 hr = r * 0.5;
	vec2 a = mod(p, r) - hr;
	vec2 b = mod(p - hr, r) - hr;
	vec2 g = dot(a, a) < dot(b, b) ? a : b;
	vec2 id = p - g;
	float hd = max(dot(abs(g), normalize(vec2(1.0, 1.732))), abs(g.x));
	float seam = smoothstep(0.4, 0.49, hd);
	float h = hash(id);
	float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.0);
	vec3 facet = mix(vec3(0.45, 0.04, 0.06), vec3(0.75, 0.12, 0.1), h);
	vec3 sheen = mix(vec3(0.2, 1.0, 0.45), vec3(0.7, 0.3, 1.0), hash(id + 3.1));
	vec3 c = mix(facet, sheen, fres * 0.7 + 0.15 * (1.0 - length(g) * 2.0));
	c = mix(c, vec3(0.02), seam);
	ALBEDO = mix(c, vec3(2.2), flash);
	ROUGHNESS = mix(0.08, 0.6, seam);
	METALLIC = 0.3;
	SPECULAR = 0.9;
	EMISSION = sheen * fres * 0.25 * (1.0 - seam) + facet * 0.15;
}",
	};

	void Build()
	{
		skin = new ShaderMaterial { Shader = SkinShader };
		var mouth = Sculpt.Paint( new Color( 0.16f, 0.02f, 0.06f ), 0.6f );
		var gum = Sculpt.Paint( new Color( 0.45f, 0.08f, 0.2f ), 0.4f );
		var tooth = Sculpt.Paint( ToothColor, 0.25f );
		var metal = Sculpt.Paint( new Color( 0.1f, 0.11f, 0.13f ), 0.35f, 0.7f );
		var fins = SmoothKit.FinMaterial( Teal, Purple, 0.78f, 0.35f );
		body = new Node3D { Name = "Body", Scale = Vector3.One * ModelScale };
		AddChild( body );

		// The body: one smooth loft from the tail stalk to the nose, tall and round (rings: along, half width, half
		// height, up offset), turned so its length runs along -Z.
		var rings = SmoothKit.Smooth( new List<(float, float, float, float)>
		{
			(-1.18f, 0.05f, 0.08f, 0.12f), (-0.98f, 0.15f, 0.25f, 0.08f), (-0.62f, 0.36f, 0.56f, 0.03f), (-0.2f, 0.55f, 0.84f, 0f),
			(0.2f, 0.62f, 0.92f, -0.02f), (0.5f, 0.6f, 0.84f, -0.08f), (0.72f, 0.5f, 0.64f, -0.15f), (0.86f, 0.33f, 0.38f, -0.24f), (0.92f, 0.12f, 0.14f, -0.3f),
		}, 4 );
		var hull = SmoothKit.Put( body, SmoothKit.Cached( "angler body", () => SmoothKit.Loft( rings, 0f, 1f, 32 ) ), skin, "Hull" );
		hull.Basis = new Basis( Vector3.Right, -MathF.PI / 2f );

		// The mouth: a dark maw across the front, gums along the upper lip, and the upper teeth hanging from it.
		Sculpt.Blob( body, new Vector3( 0f, -0.3f, -0.66f ), new Vector3( 1.02f, 0.5f, 0.62f ), mouth, name: "Maw" );
		Sculpt.Blob( body, new Vector3( 0f, -0.08f, -0.62f ), new Vector3( 1.06f, 0.14f, 0.6f ), gum, name: "Gum" );
		var random = new Random( 17 );
		for ( var i = 0; i <= 14; i++ )
		{
			var s = i / 7f - 1f;
			var root = new Vector3( 0.5f * s, -0.1f, -0.3f - 0.6f * (1f - s * s) );
			var length = (i % 2 == 0 ? 0.4f : 0.22f) * (1.15f - 0.35f * MathF.Abs( s )) * (0.85f + 0.3f * (float)random.NextDouble());
			var tip = root + new Vector3( s * 0.04f, -length, -0.05f );
			Sculpt.Cone( body, root, tip, 0.03f + length * 0.08f, tooth, 6, "Tooth" );
		}

		// The lower jaw: a jutting underbite on a hinge, its teeth standing up long and crooked.
		jaw = Sculpt.Joint( body, Hinge, "Jaw" );
		Sculpt.Blob( jaw, new Vector3( 0f, -0.2f, -0.6f ), new Vector3( 1.08f, 0.34f, 1.12f ), skin, name: "Chin" );
		Sculpt.Blob( jaw, new Vector3( 0f, -0.12f, -0.6f ), new Vector3( 0.9f, 0.16f, 0.94f ), mouth, name: "Tongue" );
		for ( var i = 0; i <= 12; i++ )
		{
			var s = i / 6f - 1f;
			var root = new Vector3( 0.48f * s, -0.06f, -0.2f - 0.82f * (1f - s * s) );
			var length = (i % 2 == 0 ? 0.62f : 0.32f) * (1.2f - 0.4f * MathF.Abs( s )) * (0.8f + 0.4f * (float)random.NextDouble());
			var tip = root + new Vector3( s * 0.06f + ((float)random.NextDouble() - 0.5f) * 0.06f, length, -0.09f - length * 0.25f );
			Sculpt.Cone( jaw, root, tip, 0.034f + length * 0.08f, tooth, 6, "Fang" );
		}

		// The eyes: two bulging compound domes high on the head.
		var eyeMaterial = new ShaderMaterial { Shader = EyeShader };
		foreach ( var x in new[] { -1f, 1f } )
		{
			var at = new Vector3( x * 0.36f, 0.44f, -0.5f );
			Sculpt.Blob( body, at - new Vector3( x * 0.03f, 0.02f, -0.04f ), new Vector3( 0.42f, 0.4f, 0.42f ), skin, name: "Eye Socket" );
			var eye = SmoothKit.Ball( body, at + new Vector3( x * 0.05f, 0.02f, -0.02f ), new Vector3( 0.4f, 0.38f, 0.4f ), eyeMaterial, new Basis( Vector3.Up, x * 0.6f ), "Compound Eye" );
			var part = CreaturePart.Create( this, x < 0f ? 2 : 3, x < 0f ? "Angler Eye L" : "Angler Eye R", 0.2f );
			body.AddChild( part );
			part.Position = eye.Position;
		}

		// Fins: pectorals at the sides, two spiny fins on the back, a ventral pair and the tail fan.
		foreach ( var x in new[] { -1f, 1f } )
		{
			var pivot = Sculpt.Joint( body, new Vector3( x * 0.56f, -0.22f, 0.05f ), x < 0f ? "Pectoral L" : "Pectoral R" );
			SmoothKit.Fin( pivot, Vector3.Zero, SmoothKit.Aim( new Vector3( x, -0.25f, 0.7f ), Vector3.Up ), 0.55f, 0.5f, fins );
			pectorals.Add( pivot );
			SmoothKit.Fin( body, new Vector3( x * 0.25f, -0.82f, 0.3f ), SmoothKit.Aim( new Vector3( x * 0.4f, -1f, 0.5f ), Vector3.Forward ), 0.3f, 0.26f, fins );
		}
		SmoothKit.Fin( body, new Vector3( 0f, 0.86f, 0.15f ), SmoothKit.Aim( new Vector3( 0f, 0.8f, 1f ), Vector3.Right ), 0.5f, 0.45f, fins );
		SmoothKit.Fin( body, new Vector3( 0f, 0.58f, 0.62f ), SmoothKit.Aim( new Vector3( 0f, 0.6f, 1f ), Vector3.Right ), 0.42f, 0.4f, fins );
		tailFin = Sculpt.Joint( body, new Vector3( 0f, 0.12f, 1.16f ), "Tail Fin" );
		SmoothKit.Fin( tailFin, Vector3.Zero, SmoothKit.Aim( Vector3.Back, Vector3.Up ), 0.7f, 0.85f, fins, 0.05f );

		// The lantern stalk: a chain of joints off the brow, each with its piece of stalk, bending as one.
		var parent = Sculpt.Joint( body, StalkBase, "Stalk" );
		stalk.Add( parent );
		for ( var i = 0; i < StalkSegments.Length; i++ )
		{
			var seg = StalkSegments[i];
			var r0 = MathX.Lerp( 0.06f, 0.03f, i / (float)StalkSegments.Length );
			var r1 = MathX.Lerp( 0.06f, 0.03f, (i + 1f) / StalkSegments.Length );
			SmoothKit.Tube( parent, Vector3.Zero, seg, r0, r1, skin, "Stalk Piece" );
			var next = Sculpt.Joint( parent, seg, $"Stalk {i + 1}" );
			stalk.Add( next );
			parent = next;
		}
		BuildHook( tooth );
		lantern = BuildLantern( metal );
		parent.AddChild( lantern );
		var lanternPart = CreaturePart.Create( this, 4, "Angler Lantern", 0.24f );
		lantern.AddChild( lanternPart );
		lanternPart.Position = new Vector3( 0f, -0.32f, 0f );

		// What else can be shot: the head (index 0), the body (1) and the jaw (5).
		var headPart = CreaturePart.Create( this, 0, "Angler Head", 0.62f );
		body.AddChild( headPart );
		headPart.Position = new Vector3( 0f, 0.1f, -0.3f );
		var bodyPart = CreaturePart.Create( this, 1, "Angler Body", 0.66f );
		body.AddChild( bodyPart );
		bodyPart.Position = new Vector3( 0f, 0f, 0.4f );
		var jawPart = CreaturePart.Create( this, 5, "Angler Jaw", 0.42f );
		jaw.AddChild( jawPart );
		jawPart.Position = new Vector3( 0f, -0.15f, -0.6f );
	}

	/// <summary>
	/// The hook it shoots out of its mouth: a bony shank with three curved, barbed claws, on a pale gut line. Both are
	/// top level so they fly free of the body; stowed, the hook sits in the mouth and the line is hidden.
	/// </summary>
	void BuildHook( Material tooth )
	{
		hook = new Node3D { Name = "Hook", TopLevel = true };
		AddChild( hook );
		var bone = Sculpt.Paint( new Color( 0.82f, 0.8f, 0.7f ), 0.35f );
		var tip = Sculpt.Glow( LanternGreen, 1.6f );
		Sculpt.Limb( hook, new Vector3( 0f, 0f, 0.28f ), new Vector3( 0f, 0f, -0.08f ), 0.045f, 0.035f, bone, "Shank" );
		Sculpt.Blob( hook, new Vector3( 0f, 0f, 0.3f ), new Vector3( 0.1f, 0.1f, 0.1f ), bone, name: "Eye" );
		for ( var k = 0; k < 3; k++ )
		{
			// Each claw sweeps forward and out, then curls back to a barbed point.
			var around = new Basis( Vector3.Forward, k * MathF.Tau / 3f );
			var a = around * new Vector3( 0f, 0f, -0.06f );
			var b = around * new Vector3( 0f, 0.13f, -0.2f );
			var c = around * new Vector3( 0f, 0.2f, -0.12f );
			var d = around * new Vector3( 0f, 0.15f, 0.02f );
			Sculpt.Limb( hook, a, b, 0.035f, 0.03f, bone, "Claw" );
			Sculpt.Limb( hook, b, c, 0.03f, 0.022f, bone, "Claw" );
			Sculpt.Cone( hook, c, d, 0.022f, tooth, 6, "Point" );
			Sculpt.Cone( hook, c + around * new Vector3( 0f, -0.02f, 0.03f ), around * new Vector3( 0f, 0.08f, -0.06f ), 0.014f, tip, 5, "Barb" );
		}
		hookLine = new MeshInstance3D
		{
			Name = "HookLine",
			TopLevel = true,
			Mesh = new CylinderMesh { TopRadius = 0.018f, BottomRadius = 0.018f, Height = 1f, RadialSegments = 6, Rings = 1 },
			MaterialOverride = Sculpt.Paint( new Color( 0.9f, 0.85f, 0.75f ), 0.4f ),
			Visible = false,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild( hookLine );
	}

	/// <summary>
	/// The middle of the open mouth, between the upper lip and the dropped jaw, a little back from the teeth: where the
	/// hook line comes out. It follows the jaw and the body's own pitch, so it stays in the gape.
	/// </summary>
	Vector3 GapeAt
	{
		get
		{
			if ( body is null || jaw is null ) return MouthAt;
			var top = body.GlobalTransform * new Vector3( 0f, -0.2f, -0.78f );
			var bottom = jaw.GlobalTransform * new Vector3( 0f, -0.06f, -0.66f );
			return (top + bottom) * 0.5f;
		}
	}

	/// <summary> Places the hook and stretches the line from the mouth to it. </summary>
	void PoseHook()
	{
		if ( hook is null ) return;
		var mouth = GapeAt;
		var facing = (-GlobalBasis.Z).SafeNormal();
		if ( line == Line.Stowed )
		{
			// Back in the mouth, just behind the teeth; it slides forward as the jaw opens to fire.
			hook.Visible = jawOpen > 0.3f;
			hook.GlobalTransform = new Transform3D( Shapes.LookAt( facing ), mouth - facing * (0.35f - 0.25f * MathF.Min( jawOpen, 1f )) );
			hookLine.Visible = false;
			return;
		}
		hook.Visible = true;
		hook.GlobalTransform = new Transform3D( Shapes.LookAt( hookDir ), hookAt );
		var span = hookAt + hookDir * 0.3f - mouth;
		var length = span.Length();
		hookLine.Visible = length > 0.05f;
		if ( !hookLine.Visible ) return;
		hookLine.GlobalTransform = new Transform3D( Shapes.LookAt( span / length ) * new Basis( Vector3.Right, -MathF.PI / 2f ) * Basis.FromScale( new Vector3( 1f, length, 1f ) ), mouth + span * 0.5f );
	}

	/// <summary> Lets go of a hooked player (the line snapped, it bit, it died). </summary>
	void ReleaseHook()
	{
		if ( hooked?.Controller is { } c && IsInstanceValid( c ) ) c.Scripted = false;
		hooked = null;
		if ( line == Line.Reel ) line = Line.Back;
	}

	protected override void Cleanup() => ReleaseHook();

	/// <summary>
	/// A whole lantern on the end of the stalk: a ring handle, a domed cap with a little chimney, a green glowing glass
	/// between four bars, a base, and a green light. It hangs from the handle (origin) downward.
	/// </summary>
	Node3D BuildLantern( Material metal )
	{
		var root = new Node3D { Name = "Lantern" };
		var glow = Sculpt.Glow( LanternGreen, 4.5f );
		var glass = new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.55f, 1f, 0.55f, 0.55f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			EmissionEnabled = true, Emission = LanternGreen, EmissionEnergyMultiplier = 2.2f, Roughness = 0.1f,
		};
		glass.SetMeta( "no_caustics", true );
		var ring = new MeshInstance3D { Name = "Handle", Mesh = new TorusMesh { InnerRadius = 0.05f, OuterRadius = 0.075f, Rings = 16, RingSegments = 8 }, MaterialOverride = metal };
		root.AddChild( ring );
		ring.Rotation = new Vector3( MathF.PI / 2f, 0f, 0f );
		ring.Position = new Vector3( 0f, -0.05f, 0f );
		Sculpt.Cone( root, new Vector3( 0f, -0.2f, 0f ), new Vector3( 0f, -0.11f, 0f ), 0.14f, metal, 12, "Cap" );
		Sculpt.Limb( root, new Vector3( 0f, -0.15f, 0f ), new Vector3( 0f, -0.1f, 0f ), 0.035f, 0.03f, metal, "Chimney" );
		Sculpt.Limb( root, new Vector3( 0f, -0.23f, 0f ), new Vector3( 0f, -0.2f, 0f ), 0.15f, 0.15f, metal, "Cap Rim" );
		lanternGlass = Sculpt.Limb( root, new Vector3( 0f, -0.45f, 0f ), new Vector3( 0f, -0.23f, 0f ), 0.11f, 0.11f, glass, "Glass" );
		Sculpt.Blob( root, new Vector3( 0f, -0.34f, 0f ), new Vector3( 0.12f, 0.16f, 0.12f ), glow, name: "Flame" );
		for ( var k = 0; k < 4; k++ )
		{
			var a = k * MathF.PI / 2f + MathF.PI / 4f;
			var x = MathF.Cos( a ) * 0.115f;
			var z = MathF.Sin( a ) * 0.115f;
			Sculpt.Limb( root, new Vector3( x, -0.46f, z ), new Vector3( x, -0.22f, z ), 0.014f, 0.014f, metal, "Bar" );
		}
		Sculpt.Limb( root, new Vector3( 0f, -0.5f, 0f ), new Vector3( 0f, -0.45f, 0f ), 0.12f, 0.15f, metal, "Base" );
		Sculpt.Cone( root, new Vector3( 0f, -0.5f, 0f ), new Vector3( 0f, -0.58f, 0f ), 0.06f, metal, 8, "Finial" );
		lanternLight = new OmniLight3D { Name = "Lantern Light", LightColor = LanternGreen, LightEnergy = 2.6f, OmniRange = 6f, ShadowEnabled = false, Position = new Vector3( 0f, -0.34f, 0f ) };
		root.AddChild( lanternLight );
		root.Scale = Vector3.One * 1.35f;
		return root;
	}

	// ---- Every frame ----

	/// <summary> The lantern's glowing middle, in the world. </summary>
	public Vector3 LanternAt => lantern is not null && IsInstanceValid( lantern ) ? lantern.GlobalTransform * new Vector3( 0f, -0.34f, 0f ) : GlobalPosition;
	/// <summary> The front of the mouth, in the world. </summary>
	public Vector3 MouthAt => GlobalTransform * new Vector3( 0f, -0.3f, -1.05f );
	/// <summary> Tests: how far the jaw is open (0 shut, 1 wide) and the lantern's swing (back below 0, struck above 0). </summary>
	public float JawOpen => jawOpen;
	public float SwingPose => swingPose;

	public override void _Process( double delta )
	{
		if ( health is null || body is null ) return;
		var dt = MathF.Min( (float)delta, 0.1f );
		var t = (float)Clock.Now + phase;

		// What the attack (if any) wants of the jaw and the lantern.
		var a = (float)sinceAct;
		var jawWant = 0.12f + 0.06f * MathF.Sin( t * 1.3f );   // a slow, gaping breath
		var swingWant = 0f;
		if ( act == Act.Chomp ) jawWant = a < 0.38f ? 1f : a < 0.5f ? 0f : 0.05f;
		if ( act == Act.Hook ) jawWant = line == Line.Reel || a < 0.6f || line == Line.Out ? 1.15f : 0.2f;
		if ( act == Act.Hook && struck ) jawWant = 0f;   // the bite
		if ( act == Act.Swing ) swingWant = a < 0.5f ? -1f : a < 0.8f ? 1.4f : 0f;
		SmoothKit.Spring( ref jawOpen, ref jawVel, jawWant, (act == Act.Chomp && a >= 0.38f) || (act == Act.Hook && struck) ? 6f : 2.5f, 0.6f, dt );
		SmoothKit.Spring( ref swingPose, ref swingVel, swingWant, act == Act.Swing && a >= 0.5f ? 3.2f : 1.4f, 0.45f, dt );

		// Swimming: a slow bob and roll, the tail fan and pectorals beating, the jaw on its hinge.
		body.Position = new Vector3( 0f, MathF.Sin( t * 1.4f ) * 0.06f, 0f );
		body.Rotation = new Vector3( MathF.Sin( t * 1.4f + 0.8f ) * 0.04f - jawOpen * 0.12f, MathF.Sin( t * 1.1f ) * 0.05f, MathF.Sin( t * 0.9f ) * 0.03f );
		tailFin.Rotation = new Vector3( 0f, MathF.Sin( t * 3.2f ) * 0.45f, 0f );
		for ( var i = 0; i < pectorals.Count; i++ )
			pectorals[i].Rotation = new Vector3( 0f, 0f, (i == 0 ? 1f : -1f) * MathF.Sin( t * 4.4f + i ) * 0.35f );
		jaw.Rotation = new Vector3( -jawOpen * 0.75f, 0f, 0f );

		// The stalk bends as one: a lazy sway, thrown back on the wind-up and whipped forward and down on the swing.
		for ( var i = 1; i < stalk.Count; i++ )
		{
			var k = i / (float)(stalk.Count - 1);
			var sway = MathF.Sin( t * 1.7f - i * 0.6f ) * 0.08f;
			stalk[i].Rotation = new Vector3( sway + swingPose * 0.32f * k, MathF.Sin( t * 1.2f - i * 0.5f ) * 0.06f, 0f );
		}
		stalk[0].Rotation = new Vector3( swingPose * 0.22f, 0f, 0f );
		// The lantern hangs level from its handle, swinging on it with the stalk's motion.
		SmoothKit.Spring( ref lanternTilt, ref lanternTiltVel, -swingVel * 0.25f, 1.4f, 0.25f, dt );
		lantern.GlobalBasis = GlobalBasis.Orthonormalized() * new Basis( Vector3.Right, lanternTilt ) * Basis.FromScale( Vector3.One * 1.35f * ModelScale );
		var flicker = 1f + 0.08f * MathF.Sin( t * 13f ) + 0.05f * MathF.Sin( t * 29f );
		lanternLight.LightEnergy = 2.6f * flicker * (act == Act.Swing ? 1.4f : 1f);

		var flash = MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f );
		skin.SetShaderParameter( "flash", flash );

		if ( IsDead || Frozen ) { PoseHook(); return; }
		Think( dt );
		PoseHook();
	}

	void Think( float dt )
	{
		if ( sinceFloor > 0.25f ) { sinceFloor = 0f; floorY = Sculpt.FloorBelow( this, GlobalPosition + Vector3.Up * 0.5f ); }
		var prey = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var wish = Vector3.Zero;
		if ( prey is not null )
		{
			var chest = prey.WorldPosition + Vector3.Up * 1.1f;
			var fromMouth = chest - MouthAt;
			var distance = fromMouth.Length();
			var dir = (chest - GlobalPosition).SafeNormal();
			Face( dir, act == Act.Swim ? 2.2f : 4f, dt );

			if ( act == Act.Swim )
			{
				// In to just past the lantern's reach, holding there while it swings; closer still when it can bite.
				var swingNext = sinceSwing > SwingCooldown && (lastChomped || sinceChomp <= ChompCooldown);
				var holdAt = !swingNext && sinceChomp > ChompCooldown ? ChompRange * 0.6f : SwingRange * 0.6f;
				wish = distance > holdAt ? dir * Speed : -dir * Speed * 0.3f;
				// The two attacks take turns when both are ready, so it never just bites.
				var canChomp = distance < ChompRange && sinceChomp > ChompCooldown;
				// From further off it shoots its hook, if the way to the player is clear.
				if ( distance > ChompRange + 0.8f && distance < HookRange - 0.3f && sinceHook > HookCooldown && prey.Controller is { Scripted: false }
					&& !Trace.Ray( this, MouthAt, chest, Layers.World ).Hit ) { Begin( Act.Hook ); canChomp = false; }
				var canSwing = distance < SwingRange && sinceSwing > SwingCooldown;
				if ( act != Act.Swim ) { }
				else if ( canSwing && (!canChomp || lastChomped) ) Begin( Act.Swing );
				else if ( canChomp ) Begin( Act.Chomp );
			}
			else if ( act == Act.Chomp )
			{
				var a = (float)sinceAct;
				// Drawn back while the jaw opens, then a lunge as it snaps shut.
				wish = a < 0.38f ? -dir * 0.8f : a < 0.55f ? dir * 7.5f : Vector3.Zero;
				if ( !struck && a >= 0.5f )
				{
					struck = true;
					Sfx.Play( "crab_snap", MouthAt, 1f, 0.55f );
					if ( MouthAt.DistanceTo( chest ) < 1.6f ) Hurt( prey, ChompDamage, dir, 2.5f, ref ChompHits );
				}
				if ( a > 0.95f ) End();
			}
			else if ( act == Act.Hook )
			{
				wish = HookThink( prey, chest, dt );
				if ( struck && line == Line.Stowed && sinceAct > 0.5f ) End();
			}
			else if ( act == Act.Swing )
			{
				var a = (float)sinceAct;
				wish = a < 0.5f ? -dir * 0.4f : dir * 1.2f;
				// The whip lands anywhere along its arc, not on one frame.
				if ( a >= 0.5f && a < 0.9f ) LastSwingMiss = MathF.Min( LastSwingMiss, LanternAt.DistanceTo( chest ) );
				if ( a >= 0.62f && a - dt < 0.62f ) Sfx.Play( "creature_attack", LanternAt, 0.8f, 0.7f );
				if ( !struck && a >= 0.5f && a < 0.9f && LanternAt.DistanceTo( chest ) < 1.7f )
				{
					struck = true;
					Hurt( prey, SwingDamage, (chest - LanternAt).SafeNormal(), 4.5f, ref SwingHits );
					AlienBlast.Spawn( GetTree().CurrentScene, LanternAt, 0.35f );
				}
				if ( a >= 0.9f ) struck = true;
				if ( a > 1.15f ) End();
			}
		}
		else if ( act != Act.Swim ) { ReleaseHook(); line = Line.Stowed; End(); }

		velocity = velocity.Lerp( wish, 1f - MathF.Exp( -(act == Act.Chomp ? 6f : 2.2f) * dt ) );
		var next = GlobalPosition + velocity * dt;
		var ahead = Trace.Ray( this, GlobalPosition, next + velocity.SafeNormal() * 1.2f, Layers.World );
		if ( ahead.Hit ) { velocity = velocity.Slide( ahead.Normal ) + Vector3.Up * 1.5f; next = GlobalPosition + velocity * dt; }
		// Keep its belly off the floor and its body at the player's height.
		next.Y = MathF.Max( next.Y, floorY + 1.05f );
		if ( next.Y < floorY + HoverHeight ) velocity.Y += 2.5f * dt;
		else if ( next.Y > floorY + HoverHeight + 1.2f ) velocity.Y -= 1.5f * dt;
		GlobalPosition = Sculpt.InBounds( next );
		// Keep clear of other anglers.
		foreach ( var other in All )
		{
			if ( other == this || other.IsDead ) continue;
			var away = GlobalPosition - other.GlobalPosition;
			var d = away.Length();
			if ( d < 2.6f && d > 0.01f ) velocity += away / d * (2.6f - d) * 2f * dt;
		}
	}

	/// <summary>
	/// The hook attack: the jaw gapes for a moment, the hook shoots out at the player along a line up to
	/// <see cref="HookRange"/>; if it pierces them it reels them in slowly to the mouth and bites. A miss comes back.
	/// Shooting it hard enough while it reels (<see cref="HookBreak"/> of its health) snaps the line.
	/// </summary>
	Vector3 HookThink( PlayerSurvivor prey, Vector3 chest, float dt )
	{
		var a = (float)sinceAct;
		var mouth = GapeAt;
		switch ( line )
		{
			case Line.Stowed:
				if ( struck || a < 0.6f ) return Vector3.Zero;   // the jaw opening, or done
				line = Line.Out;
				hookAt = mouth;
				hookDir = (chest - mouth).SafeNormal();
				hookOut = 0f;
				Sfx.Play( "trident_charge", mouth, 0.7f, 1.4f );
				return Vector3.Zero;
			case Line.Out:
			{
				var step = HookSpeed * dt;
				var next = hookAt + hookDir * step;
				var wall = Trace.Ray( this, hookAt, next, Layers.World );
				hookAt = wall.Hit ? wall.HitPosition : next;
				hookOut += step;
				// It pierces whoever it passes close to.
				foreach ( var p in PlayerSurvivor.All )
				{
					if ( !p.IsAlive || p.Controller is not { } c || c.Scripted ) continue;
					var body = c.GlobalPosition + Vector3.Up * 1.1f;
					if ( hookAt.DistanceTo( body ) > 0.75f ) continue;
					hooked = p;
					c.Scripted = true;
					c.Velocity = Vector3.Zero;
					line = Line.Reel;
					reelStartHealth = health.Current;
					HookCatches++;
					p.Health?.OnDamage( new DamageInfo { Damage = HookDamage, Attacker = this, Position = body, Origin = mouth } );
					Sfx.Play( "trident_flesh", body, 1f, 0.8f );
					c.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.6f );
					break;
				}
				if ( line == Line.Out && (wall.Hit || hookOut >= HookRange) ) line = Line.Back;
				return Vector3.Zero;
			}
			case Line.Reel:
			{
				var c = hooked?.Controller;
				if ( c is null || !IsInstanceValid( c ) || !hooked.IsAlive || reelStartHealth - health.Current > health.MaxHealth * HookBreak || a > 9f )
				{
					if ( c is not null && IsInstanceValid( c ) ) Sfx.Play( "bubble_pop", hookAt, 0.9f, 0.6f );
					ReleaseHook();
					return Vector3.Zero;
				}
				// Reeled in slowly toward the gaping mouth, the hook in their chest.
				var body = c.GlobalPosition + Vector3.Up * 1.1f;
				var to = mouth + (body - mouth).SafeNormal() * 0.9f;
				var next = body.MoveToward( to, ReelSpeed * dt );
				var feet = next - Vector3.Up * 1.1f;
				feet.Y = MathF.Max( feet.Y, Sculpt.FloorBelow( this, feet + Vector3.Up * 1f, 6f ) );
				c.GlobalPosition = feet;
				c.Velocity = Vector3.Zero;
				c.ResetPhysicsInterpolation();
				hookDir = (mouth - body).SafeNormal() * -1f;
				hookAt = feet + Vector3.Up * 1.1f - hookDir * 0.1f;
				Face( (body - GlobalPosition).SafeNormal(), 4f, dt );
				if ( !struck && next.DistanceTo( to ) < 0.05f )
				{
					// The bite.
					struck = true;
					HookBites++;
					hooked.Health?.OnDamage( new DamageInfo { Damage = HookBiteDamage, Attacker = this, Position = body, Origin = mouth } );
					Sfx.Play( "crab_snap", mouth, 1f, 0.5f );
					c.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 1f );
					var victim = hooked;
					ReleaseHook();
					line = Line.Stowed;
					if ( victim.Controller is { } vc ) vc.Velocity += (body - GlobalPosition).Flat().SafeNormal() * 3f + Vector3.Up * 1.5f;
					sinceAct = 0f;   // a beat to close the jaw before it swims on
				}
				return Vector3.Zero;
			}
			case Line.Back:
			{
				hookAt = hookAt.MoveToward( mouth, HookSpeed * 0.8f * dt );
				hookDir = (hookAt - mouth).SafeNormal();
				if ( hookAt.DistanceTo( mouth ) < 0.15f ) { line = Line.Stowed; struck = true; sinceAct = 0f; }
				return Vector3.Zero;
			}
		}
		return Vector3.Zero;
	}

	void Begin( Act next )
	{
		act = next;
		sinceAct = 0f;
		struck = false;
		if ( next == Act.Swing || next == Act.Chomp ) lastChomped = next == Act.Chomp;
		if ( next == Act.Swing ) { sinceSwing = 0f; LastSwingMiss = 99f; Sfx.Play( "creature_attack", GlobalPosition, 0.5f, 0.55f ); }
		if ( next == Act.Chomp ) sinceChomp = 0f;
		if ( next == Act.Hook ) { sinceHook = 0f; line = Line.Stowed; Sfx.Play( "creature_attack", GlobalPosition, 0.6f, 0.45f ); }
	}

	void End() { act = Act.Swim; sinceAct = 0f; }

	void Hurt( PlayerSurvivor prey, float damage, Vector3 push, float shove, ref int count )
	{
		if ( prey.Health is not { } h ) return;
		count++;
		h.OnDamage( new DamageInfo { Damage = damage, Attacker = this, Position = prey.WorldPosition + Vector3.Up * 1.1f, Origin = GlobalPosition } );
		if ( prey.Controller is { } c ) c.Velocity += push.Flat().SafeNormal() * shove + Vector3.Up * 1.2f;
	}

	/// <summary> Tests: start an attack now. </summary>
	public void ForceSwing() { sinceSwing = 99f; Begin( Act.Swing ); }
	public void ForceChomp() { sinceChomp = 99f; Begin( Act.Chomp ); }
	public void ForceHook() { Begin( Act.Hook ); }
	/// <summary> Tests: the hook is in a player and reeling. </summary>
	public bool Reeling => line == Line.Reel && hooked is not null;
	public Vector3 HookAt => hook?.GlobalPosition ?? GlobalPosition;

	/// <summary> The compound eyes are the weak spots. </summary>
	public override bool WeakSpot( int index ) => index is 2 or 3;

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( NetCreatures.Intercept( this, index, damage, direction, position, origin, shooter, scoreHit ) ) return;   // online: the host takes the hit and sends it
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		var weak = index is 2 or 3;
		var scale = weak ? EyeMultiplier : index == 4 ? LanternMultiplier : 1f;
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * scale, Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, weak, scoreHit );
		if ( !killed ) GibKit.Chips( Gibs, position, -direction, direction, 0.06f, 1 );
	}

	protected override void OnDied()
	{
		RaiseKilled();
		if ( lanternLight is not null ) lanternLight.Visible = false;
		ReleaseHook();
		gore?.GooBurst( GlobalPosition, lastShotDirection, GooColor, 26, 320f, 1.2f );
		GibKit.Burst( Gibs, GlobalPosition, lastShotDirection, 0.26f, 12 );
		QueueFree();
	}
}
