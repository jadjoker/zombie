using Godot;

/// <summary>
/// The Leviathan (user's pick, the Abyss's wave 30 boss): the whale-serpent seen circling over the sea all run comes down
/// through the skylight for you. Its body (<see cref="Segments"/> of them) follows the path its head swam (a trail), so it
/// hangs down through the skylight while it rests and ploughs round the hub in the sand when it sweeps.
/// The fight is a loop: from above the roof it marks a spot that follows the nearest player and locks
/// (<see cref="LockBefore"/>), then rams down through the skylight onto it (a shock ring to jump, stalactites coming
/// down, and the roof cracks letting more light in each time); it rests there with its head on the floor, eyes and
/// blowholes open (the weak spots), then does two of: the coil sweep (half buried, its back sweeps round the hub; jump
/// it), the whale song (a cone of sound that throws you back, slows you and blurs the view), the swallow (it sucks like
/// the Gore Vac; anyone pulled into its mouth is held in its stomach, burned by acid, and shoots the stomach wall for
/// big damage until it spits them out) and the brood (barnacles on its back burst and puffers and an angler come out).
/// Then it goes back up and rams again. Dead, it collapses across the hub and stays there (<see cref="LeviathanCarcass"/>).
/// Built in code, metres, facing -Z; its position is its head's.
/// </summary>
public partial class Leviathan : Creature
{
	public static readonly List<Leviathan> All = new();
	public override bool CanScare => false;
	public override Color Blood => new( 0.2f, 0.55f, 0.85f );
	public override float AreaDamageScale => 0.5f;
	public override GibRecipe Gibs => GibRecipes.Kraken( SkinColour, Blood );
	public override bool WeakSpot( int index ) => index is 2 or 3 or 4 or 8;

	public const int Segments = 18;
	public const float Spacing = 2.5f;
	public float AimTime = 3.2f;
	public float LockBefore = 0.8f;
	public float DiveTime = 1.5f;
	public float RestTime = 5.5f;
	public float RamDamage = 50f;
	public float RingDamage = 30f;
	public float SweepRadius = 15f;
	public float SweepSpeed = 12f;
	public float SweepDamage = 30f;
	public float SongRange = 30f;
	public float SongDamage = 15f;
	public float SuckRange = 22f;
	public float SuckStrength = 9f;
	public float StomachTime = 7f;
	public float AcidPerSecond = 6f;
	public float StomachEscapeShare = 0.06f;   // this much of its health shot off from inside spits you out
	public float HideMultiplier = 0.5f;
	public float EyeMultiplier = 3f;
	public float BlowholeMultiplier = 2.5f;
	public float StomachMultiplier = 2f;
	public float UlcerMultiplier = 4f;
	public static readonly Vector3 StomachAt = new( 0f, -220f, 0f );

	/// <summary> Tests: what it has done. </summary>
	public static int Rams, RamHits, SweepHits, Songs, SongHits, Swallows, Spits, Broods, Cracks, FallingHits;
	/// <summary> The local player is in a leviathan's stomach (the HUD's prompt). </summary>
	public static bool SwallowedLocal => PlayerSurvivor.Local is { IsAlive: true } p && p.RealPosition.Y < StomachAt.Y + 30f;
	/// <summary> Where it comes in: high over the skylight. </summary>
	public static Vector3 EntryPoint => new( 0f, Abyss.RoofAt( 0f, 0f ) + 18f, 0f );

	static readonly Color SkinColour = new( 0.2f, 0.27f, 0.36f );
	static readonly Color GlowBlue = new( 0.35f, 0.8f, 1f );
	static readonly Color VeinGreen = new( 0.3f, 1f, 0.4f );
	static readonly Color BlowGlow = new( 1f, 0.55f, 0.2f );

	public enum Act { Above, Dive, Rest, Sweep, Song, Swallow, Gulp, Brood, Withdraw }
	Act act = Act.Above;
	TimeSince sinceAct;
	Vector3 spot, p0, p1, p2, restForward = Vector3.Forward, lastHead;
	bool struck, locked;
	int cycle, ram;
	float ringAngle, ringTravel, ringDir = 1f;
	readonly List<Act> queue = new();
	readonly Dictionary<PlayerSurvivor, double> sweepHit = new();
	readonly List<PlayerSurvivor> swallowed = new();
	double gulpedAt;
	float gulpStartHealth;

	// The body: a trail of where the head has been, newest last.
	readonly List<Vector3> trail = new();
	Node3D rig, head, jaw;
	readonly List<(Node3D Eye, Node3D Almond, Basis Home)> eyes = new();
	readonly List<List<Node3D>> whiskers = new();
	StandardMaterial3D eyeRim, lureGlow;
	int blinking = -1;
	float blinkT, nextBlink = 1f, charge, chargeVel;
	static readonly Color LureGreen = new( 0.4f, 1f, 0.45f );
	readonly Node3D[] segs = new Node3D[Segments];
	readonly float[] radius = new float[Segments];
	readonly List<MeshInstance3D> blowCores = new();
	readonly List<Node3D> barnacles = new();
	readonly List<Node3D> fins = new();
	Node3D fluke;
	ShaderMaterial skin;
	StandardMaterial3D blowGlow, barnacleGlow;
	OmniLight3D headLight;
	MeshInstance3D marker;
	ImmediateMesh markerMesh;
	Node3D stomach;
	Vector3 lastWhisker;
	float jawOpen, jawVel, blow, blowVel, swell, swellVel;
	TimeSince sinceHit = 99f, sinceAcid;
	readonly List<Node> parts = new();
	bool carcassed;

	public string Line { get; private set; } = "";
	public TimeSince TimeSinceLine { get; private set; } = 999f;
	void Say( string line ) { Line = line; TimeSinceLine = 0f; }

	public static Leviathan Spawn( Node parent, Vector3 position, float maxHealth ) =>
		Place( new Leviathan { Name = "Leviathan" }, parent, position, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	protected override void Setup()
	{
		BeginSetup( 24000f );
		// The body hangs straight up out of sight above where it comes in.
		for ( var i = 160; i >= 0; i-- ) trail.Add( GlobalPosition + Vector3.Up * i * 0.35f );
		lastHead = GlobalPosition;
		Build();
		BuildStomach();
		Begin( Act.Above );
		Sfx.Play2D( "leviathan_roar" );
	}

	protected override void Cleanup()
	{
		ReleaseAll();
		if ( !carcassed && rig is not null && IsInstanceValid( rig ) ) rig.QueueFree();
		if ( marker is not null && IsInstanceValid( marker ) ) marker.QueueFree();
		if ( stomach is not null && IsInstanceValid( stomach ) ) stomach.QueueFree();
	}

	// ---- Looks ----

	static readonly Shader SkinShader = new()
	{
		Code = @"shader_type spatial;
uniform vec3 back : source_color = vec3(0.16, 0.22, 0.31);
uniform vec3 belly : source_color = vec3(0.62, 0.68, 0.7);
uniform vec3 glow : source_color = vec3(0.35, 0.8, 1.0);
uniform vec3 vein : source_color = vec3(0.3, 1.0, 0.4);
uniform float flash = 0.0;
uniform float hurt = 0.0;
uniform float charge = 0.0;
instance uniform float seg = 0.0;
varying vec3 p;
varying vec3 pm;
varying float up;
float hash(vec3 q) { return fract(sin(dot(q, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
float noise(vec3 q) {
	vec3 i = floor(q); vec3 f = fract(q); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1,0,0)), f.x), mix(hash(i + vec3(0,1,0)), hash(i + vec3(1,1,0)), f.x), f.y),
		mix(mix(hash(i + vec3(0,0,1)), hash(i + vec3(1,0,1)), f.x), mix(hash(i + vec3(0,1,1)), hash(i + vec3(1,1,1)), f.x), f.y), f.z);
}
void vertex() {
	p = VERTEX;
	// The same point in metres (the hide balls are unit spheres scaled up), so the veins are one size everywhere.
	pm = VERTEX * vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
	up = (MODEL_MATRIX * vec4(NORMAL, 0.0)).y;
}
void fragment() {
	vec3 q = p + vec3(seg * 3.7, seg * 1.3, 0.0);
	vec3 m = pm + vec3(seg * 3.7, seg * 1.3, 0.0);
	float n = noise(q * 3.0) * 0.6 + noise(q * 9.0) * 0.4;
	vec3 c = mix(belly, back, smoothstep(-0.45, 0.1, up));
	// Grooves along the throat and belly, scars and barnacle scabs over the back.
	c *= 1.0 - (1.0 - smoothstep(0.0, 0.12, abs(fract(p.x * 4.0) - 0.5))) * 0.25 * smoothstep(0.0, -0.5, up);
	c = mix(c, vec3(0.55, 0.55, 0.5), smoothstep(0.72, 0.8, n) * smoothstep(-0.1, 0.4, up) * 0.6);
	c *= 0.85 + 0.25 * n;
	c = mix(c, c * 0.4 + vec3(0.25, 0.03, 0.05), hurt * smoothstep(0.5, 0.75, noise(q * 2.0 + 7.0)));
	// Green alien veins branching over the hide: they pulse slowly, faster as it is hurt, and before an attack a bright
	// wave runs down them from the head to the tail.
	float v1 = 1.0 - abs(noise(m * 0.8 + 5.0) * 2.0 - 1.0);
	float v2 = 1.0 - abs(noise(m * 2.0 + 9.0) * 2.0 - 1.0);
	float veins = smoothstep(0.972, 0.99, v1) + smoothstep(0.98, 0.995, v2) * 0.3;
	float rate = 1.4 + hurt * 3.0;
	float pulse = 0.55 + 0.45 * sin(TIME * rate - seg * 0.6);
	float wave = pow(0.5 + 0.5 * sin(seg * 0.8 - TIME * 9.0), 8.0);
	float lit = veins * (pulse * 0.35 + charge * (0.4 + wave * 3.5));
	c = mix(c, c * 0.7, veins * 0.3);
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = mix(c, vec3(2.0), flash);
	ROUGHNESS = 0.45;
	EMISSION = vein * lit + glow * rim * 0.4 + c * 0.12;
}",
	};

	void Build()
	{
		skin = new ShaderMaterial { Shader = SkinShader };
		var mouth = Sculpt.Paint( new Color( 0.35f, 0.05f, 0.1f ), 0.5f );
		var throat = Sculpt.Paint( new Color( 0.05f, 0.0f, 0.02f ), 0.8f );
		var tooth = Sculpt.Glow( new Color( 0.92f, 0.9f, 0.82f ), 0.3f );
		var bone = Sculpt.Paint( new Color( 0.78f, 0.76f, 0.7f ), 0.6f );
		eyeRim = new StandardMaterial3D { AlbedoColor = VeinGreen, EmissionEnabled = true, Emission = VeinGreen, EmissionEnergyMultiplier = 2.5f };
		blowGlow = new StandardMaterial3D { AlbedoColor = BlowGlow, EmissionEnabled = true, Emission = BlowGlow, EmissionEnergyMultiplier = 0.3f };
		barnacleGlow = new StandardMaterial3D { AlbedoColor = new Color( 0.75f, 0.85f, 0.6f ), EmissionEnabled = true, Emission = new Color( 0.5f, 1f, 0.4f ), EmissionEnergyMultiplier = 0f, Roughness = 0.7f };
		var finPaint = SmoothKit.FinMaterial( SkinColour, GlowBlue, 0.9f, 0.3f );

		rig = new Node3D { Name = "Leviathan Body" };
		GetTree().CurrentScene.AddChild( rig );

		// The head: a long whale skull and a hinged lower jaw full of teeth (the user kept these over a flower mouth), eight
		// black almond alien eyes down the sides and lure whiskers hanging off the jaw.
		head = new Node3D { Name = "Head" };
		rig.AddChild( head );
		var skull = SmoothKit.Smooth( new List<(float, float, float, float)>
		{
			(-2.2f, 1.9f, 1.7f, 0f), (-1f, 2.15f, 1.75f, 0.1f), (1f, 2f, 1.5f, 0.25f), (2.8f, 1.6f, 1.05f, 0.35f), (4.2f, 1.0f, 0.65f, 0.3f), (4.8f, 0.35f, 0.3f, 0.2f),
		}, 4 );
		SmoothKit.Put( head, SmoothKit.Cached( "leviathan skull", () => SmoothKit.Loft( skull, 0f, 1f, 32 ) ), skin, "Skull" ).Basis = new Basis( Vector3.Right, -MathF.PI / 2f );
		Sculpt.Blob( head, new Vector3( 0f, -0.55f, -2.2f ), new Vector3( 3.4f, 1.2f, 4.6f ), mouth, name: "Palate" );
		Sculpt.Blob( head, new Vector3( 0f, -0.6f, -1f ), new Vector3( 2.4f, 1.0f, 2.2f ), throat, name: "Throat" );
		for ( var i = 0; i <= 16; i++ )
		{
			var s = i / 8f - 1f;
			var at = new Vector3( 1.55f * s, -0.75f, -1.2f - 3.0f * (1f - s * s) );
			var length = (i % 2 == 0 ? 0.75f : 0.45f) * (1.1f - 0.3f * MathF.Abs( s ));
			Sculpt.Cone( head, at, at + new Vector3( 0f, -length, -0.1f ), 0.12f, tooth, 6, "Tooth" );
		}
		// The eyes: four a side, a big pair on top, the classic alien eye (a glossy black
		// almond slanted out at the top on a glowing green rim) set in a bulb of hide. They track the prey and blink in turn.
		var eyeBlack = new StandardMaterial3D { AlbedoColor = new Color( 0.01f, 0.01f, 0.015f ), Roughness = 0.06f, ClearcoatEnabled = true, Clearcoat = 1f, ClearcoatRoughness = 0.04f, Metallic = 0.2f };
		foreach ( var x in new[] { -1f, 1f } )
		{
			// The big pair on top (where the old pale brows were) looks up and ahead; three more run down each side.
			var spots = new[] { (new Vector3( 1.05f, 1.45f, -1.7f ), 1.35f, new Vector3( 0.4f, 0.8f, -0.45f )), (new Vector3( 1.55f, 0.55f, -2.6f ), 1.15f, new Vector3( 0.85f, 0.2f, -0.45f )), (new Vector3( 1.85f, 0.7f, -1.2f ), 0.95f, new Vector3( 0.85f, 0.2f, -0.45f )), (new Vector3( 1.95f, 0.55f, 0.2f ), 0.75f, new Vector3( 0.85f, 0.2f, -0.45f )) };
			foreach ( var (spot0, size, look0) in spots )
			{
				var at = new Vector3( spot0.X * x, spot0.Y, spot0.Z );
				var eye = new Node3D { Name = "Alien Eye", Position = at };
				head.AddChild( eye );
				var look = new Vector3( look0.X * x, look0.Y, look0.Z ).Normalized();
				eye.Basis = new Basis( new Quaternion( Vector3.Back, look ) ) * new Basis( Vector3.Back, -x * MathX.Rad( 38f ) );
				SmoothKit.Ball( eye, new Vector3( 0f, 0f, -0.08f ), new Vector3( 0.7f, 0.95f, 0.6f ) * size, skin, null, "Eye Bulb" );
				var almond = new Node3D { Name = "Almond", Position = new Vector3( 0f, 0f, 0.2f * size ) };
				eye.AddChild( almond );
				Sculpt.Blob( almond, Vector3.Zero, new Vector3( 0.5f, 1.0f, 0.2f ) * size, eyeRim, name: "Eye Rim" );
				Sculpt.Blob( almond, new Vector3( 0f, 0f, 0.02f * size ), new Vector3( 0.42f, 0.92f, 0.22f ) * size, eyeBlack, name: "Eye" );
				eyes.Add( (eye, almond, eye.Basis) );
				var part = CreaturePart.Create( this, x < 0f ? 2 : 3, x < 0f ? "Leviathan Eye L" : "Leviathan Eye R", 0.45f * size );
				head.AddChild( part );
				part.Position = at;
				parts.Add( part );
			}
		}
		for ( var i = 0; i < 7; i++ )
			Sculpt.Blob( head, new Vector3( ((i * 37) % 7 - 3) * 0.22f, 1.35f - i * 0.04f, -0.6f - i * 0.38f ), Vector3.One * (0.25f + (i % 3) * 0.08f), bone, name: "Knob" );
		jaw = Sculpt.Joint( head, new Vector3( 0f, -0.6f, 1.4f ), "Jaw" );
		var jawRings = SmoothKit.Smooth( new List<(float, float, float, float)>
		{
			(0f, 1.8f, 0.55f, -0.2f), (2.5f, 1.75f, 0.6f, -0.45f), (4.6f, 1.2f, 0.5f, -0.4f), (5.9f, 0.4f, 0.3f, -0.25f),
		}, 4 );
		SmoothKit.Put( jaw, SmoothKit.Cached( "leviathan jaw", () => SmoothKit.Loft( jawRings, 0f, 1f, 28 ) ), skin, "Lower Jaw" ).Basis = new Basis( Vector3.Right, -MathF.PI / 2f );
		Sculpt.Blob( jaw, new Vector3( 0f, 0.05f, -3f ), new Vector3( 2.8f, 0.4f, 4.4f ), mouth, name: "Tongue" );
		for ( var i = 0; i <= 14; i++ )
		{
			var s = i / 7f - 1f;
			var at = new Vector3( 1.5f * s, 0.15f, -2.4f - 2.6f * (1f - s * s) );
			Sculpt.Cone( jaw, at, at + new Vector3( 0f, 0.55f + (i % 2) * 0.3f, -0.05f ), 0.11f, tooth, 6, "Jaw Tooth" );
		}
		// Whiskers: jointed feelers hanging off the lower jaw, each ending in a glowing lure.
		var feeler = Sculpt.Paint( SkinColour.Lightened( 0.15f ), 0.6f );
		lureGlow = new StandardMaterial3D { AlbedoColor = LureGreen, EmissionEnabled = true, Emission = LureGreen, EmissionEnergyMultiplier = 3f };
		var whiskerAt = new[] { new Vector3( -0.9f, -0.75f, -4.2f ), new Vector3( 0.9f, -0.75f, -4.2f ), new Vector3( -1.45f, -0.75f, -2.8f ), new Vector3( 1.45f, -0.75f, -2.8f ), new Vector3( -0.7f, -0.9f, -1.5f ), new Vector3( 0.7f, -0.9f, -1.5f ) };
		for ( var w = 0; w < whiskerAt.Length; w++ )
		{
			var chain = new List<Node3D>();
			var parent = jaw;
			var at = whiskerAt[w];
			var length = w < 2 ? 1.2f : 0.95f;
			for ( var j = 0; j < 4; j++ )
			{
				var joint = Sculpt.Joint( parent, at, "Whisker" );
				var r0 = 0.13f - j * 0.025f;
				SmoothKit.Tube( joint, Vector3.Zero, Vector3.Down * length, r0, r0 - 0.02f, feeler, "Whisker Piece" );
				chain.Add( joint );
				parent = joint;
				at = Vector3.Down * length;
			}
			SmoothKit.Ball( parent, Vector3.Down * (length + 0.15f), new Vector3( 0.38f, 0.5f, 0.38f ), lureGlow, null, "Lure" );
			whiskers.Add( chain );
		}
		headLight = new OmniLight3D { Name = "Head Light", LightColor = VeinGreen, LightEnergy = 1.6f, OmniRange = 14f, ShadowEnabled = false, Position = new Vector3( 0f, 2f, -1f ) };
		head.AddChild( headLight );
		var headPart = CreaturePart.Create( this, 0, "Leviathan Head", 2.1f );
		head.AddChild( headPart );
		headPart.Position = new Vector3( 0f, 0.2f, -0.6f );
		parts.Add( headPart );
		var snoutPart = CreaturePart.Create( this, 0, "Leviathan Snout", 1.4f );
		head.AddChild( snoutPart );
		snoutPart.Position = new Vector3( 0f, 0f, -3.4f );
		parts.Add( snoutPart );

		// The body: overlapping smooth segments thinning to the tail, a ridge of spines, blowholes on the first few,
		// barnacle clusters, big fins behind the head and a fluke at the end.
		for ( var i = 0; i < Segments; i++ )
		{
			var k = i / (float)(Segments - 1);
			var r = MathX.Lerp( 2.05f, 0.55f, MathF.Pow( k, 1.25f ) );
			radius[i] = r;
			var seg = new Node3D { Name = $"Segment {i}" };
			rig.AddChild( seg );
			segs[i] = seg;
			SmoothKit.Ball( seg, Vector3.Zero, new Vector3( r * 2.1f, r * 1.9f, Spacing * 2.4f ), skin, null, "Hide" ).SetInstanceShaderParameter( "seg", i + 1f );
			Sculpt.Cone( seg, new Vector3( 0f, r * 0.85f, 0f ), new Vector3( 0f, r * 0.85f + 0.4f + r * 0.35f, 0.7f ), 0.18f + r * 0.08f, bone, 6, "Spine" );
			var part = CreaturePart.Create( this, 1, "Leviathan Hide", r );
			seg.AddChild( part );
			parts.Add( part );
			if ( i is >= 1 and <= 4 )
			{
				var ring = new MeshInstance3D { Name = "Blowhole", Mesh = new TorusMesh { InnerRadius = 0.32f, OuterRadius = 0.55f }, MaterialOverride = bone };
				seg.AddChild( ring );
				ring.Position = new Vector3( 0f, r * 0.9f, -0.6f );
				blowCores.Add( Sculpt.Blob( seg, new Vector3( 0f, r * 0.88f, -0.6f ), new Vector3( 0.55f, 0.2f, 0.55f ), blowGlow, name: "Blowhole Core" ) );
				var hole = CreaturePart.Create( this, 4, "Leviathan Blowhole", 0.7f );
				seg.AddChild( hole );
				hole.Position = new Vector3( 0f, r * 0.9f, -0.6f );
				parts.Add( hole );
			}
			if ( i is 5 or 9 or 13 )
			{
				var cluster = new Node3D { Name = "Barnacles" };
				seg.AddChild( cluster );
				var random = new Random( i * 7 );
				for ( var b = 0; b < 6; b++ )
				{
					var a = (float)random.NextDouble() * MathF.Tau;
					var at = new Vector3( MathF.Cos( a ) * r * 0.5f, r * 0.75f, MathF.Sin( a ) * r * 0.5f );
					Sculpt.Cone( cluster, at, at + new Vector3( 0f, 0.35f + (float)random.NextDouble() * 0.3f, 0f ), 0.22f, barnacleGlow, 7, "Barnacle" );
				}
				barnacles.Add( cluster );
			}
			if ( i == 1 )
				foreach ( var x in new[] { -1f, 1f } )
				{
					var pivot = Sculpt.Joint( seg, new Vector3( x * r * 0.9f, -r * 0.4f, 0f ), "Fin" );
					SmoothKit.Fin( pivot, Vector3.Zero, SmoothKit.Aim( new Vector3( x, -0.3f, 0.6f ), Vector3.Back ), 4.2f, 2.2f, finPaint );
					fins.Add( pivot );
				}
		}
		fluke = Sculpt.Joint( segs[Segments - 1], new Vector3( 0f, 0f, 0.8f ), "Fluke" );
		foreach ( var x in new[] { -1f, 1f } )
			SmoothKit.Fin( fluke, Vector3.Zero, SmoothKit.Aim( new Vector3( x, 0f, 0.7f ), Vector3.Up ), 3.6f, 2f, finPaint, 0.04f );

		// The ram's mark on the floor.
		markerMesh = new ImmediateMesh();
		var markPaint = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		markPaint.SetMeta( "no_caustics", true );
		marker = new MeshInstance3D { Name = "Leviathan Mark", Mesh = markerMesh, MaterialOverride = markPaint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true };
		GetTree().CurrentScene.AddChild( marker );
		Place();
	}

	/// <summary> Its stomach, far under the map: a red fleshy room, its whole wall a weak spot, four ulcers weaker still. </summary>
	void BuildStomach()
	{
		stomach = new Node3D { Name = "Leviathan Stomach" };
		GetTree().CurrentScene.AddChild( stomach );
		stomach.GlobalPosition = StomachAt;
		var flesh = new ShaderMaterial { Shader = GutShader };
		var sphere = new SphereMesh { Radius = 7f, Height = 12f, RadialSegments = 32, Rings = 16 };
		stomach.AddChild( new MeshInstance3D { Name = "Wall", Mesh = sphere, MaterialOverride = flesh } );
		var acid = Sculpt.Glow( new Color( 0.6f, 1f, 0.25f ), 1.2f );
		Sculpt.Blob( stomach, new Vector3( 0f, -5.4f, 0f ), new Vector3( 9f, 0.6f, 9f ), acid, name: "Acid" );
		var wall = new CreaturePart { Name = "Leviathan Stomach Wall", Creature = this, Index = 7, CollisionLayer = Layers.Creature, CollisionMask = 0 };
		var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
		shape.SetFaces( sphere.GetFaces() );
		wall.Collider = new CollisionShape3D { Shape = shape };
		wall.AddChild( wall.Collider );
		stomach.AddChild( wall );
		parts.Add( wall );
		var ulcer = Sculpt.Glow( new Color( 1f, 0.6f, 0.3f ), 2.5f );
		for ( var i = 0; i < 6; i++ )
		{
			var a = i * MathF.Tau / 6f + 0.4f;
			var at = new Vector3( MathF.Cos( a ) * 6.3f, -3.6f + (i % 2) * 1.2f, MathF.Sin( a ) * 6.3f );
			Sculpt.Blob( stomach, at, Vector3.One * 1.5f, ulcer, name: "Ulcer" );
			var part = CreaturePart.Create( this, 8, "Leviathan Ulcer", 0.8f );
			stomach.AddChild( part );
			part.Position = at;
			parts.Add( part );
		}
		stomach.AddChild( new OmniLight3D { Name = "Gut Light", LightColor = new Color( 1f, 0.45f, 0.35f ), LightEnergy = 2.2f, OmniRange = 16f, ShadowEnabled = false, Position = Vector3.Up * 2f } );
	}

	/// <summary> The stomach's wall: wet folds of flesh, dark veins, slow throbbing. Seen from inside. </summary>
	static readonly Shader GutShader = new()
	{
		Code = @"shader_type spatial;
render_mode cull_front;
varying vec3 p;
float hash(vec3 q) { return fract(sin(dot(q, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
float noise(vec3 q) {
	vec3 i = floor(q); vec3 f = fract(q); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1,0,0)), f.x), mix(hash(i + vec3(0,1,0)), hash(i + vec3(1,1,0)), f.x), f.y),
		mix(mix(hash(i + vec3(0,0,1)), hash(i + vec3(1,0,1)), f.x), mix(hash(i + vec3(0,1,1)), hash(i + vec3(1,1,1)), f.x), f.y), f.z);
}
void vertex() { p = VERTEX; VERTEX += NORMAL * sin(TIME * 1.7 + VERTEX.y) * 0.08; }
void fragment() {
	float folds = abs(sin(p.y * 3.0 + noise(p * 0.9) * 6.0));
	float vein = 1.0 - smoothstep(0.0, 0.05, abs(noise(p * 0.7 + 4.0) - 0.5));
	vec3 c = mix(vec3(0.32, 0.04, 0.07), vec3(0.75, 0.25, 0.28), folds);
	c = mix(c, vec3(0.15, 0.0, 0.08), vein * 0.8);
	ALBEDO = c;
	ROUGHNESS = 0.2;
	SPECULAR = 0.8;
	EMISSION = c * 0.12;
}",
	};

	// ---- Every frame ----

	public Act Doing => act;
	public Vector3 Spot => spot;
	public bool Locked => locked;
	public bool Open => act is Act.Rest or Act.Gulp;
	public Vector3 MouthAt => head.GlobalTransform * new Vector3( 0f, -0.4f, -4.6f );
	public int SwallowedCount => swallowed.Count;
	public Vector3 SegmentAt( int i ) => segs[i].GlobalPosition;
	public float SegmentRadius( int i ) => radius[i];
	float Floor => SeaTerrain.HeightAt( 0f, 0f );
	float RoofC => Abyss.Root is not null ? Abyss.RoofAt( 0f, 0f ) : Floor + 27f;

	public override void _Process( double delta )
	{
		if ( health is null || rig is null ) return;
		var dt = MathF.Min( (float)delta, 0.1f );
		var t = (float)Clock.Now;
		var a = (float)sinceAct;

		if ( !IsDead && !Puppet && !Frozen ) Think( dt );
		else if ( Puppet && !IsDead ) PuppetActs( a );

		// The mouth, the blowholes and the throat.
		var jawWant = act switch
		{
			Act.Swallow => a > 0.4f ? 1.1f : 0.4f,
			Act.Song => a < 1.3f ? 0.25f : 0.8f,
			Act.Dive => 0.6f,
			Act.Rest => 0.15f + 0.05f * MathF.Sin( t * 1.3f ),
			_ => 0.08f,
		};
		SmoothKit.Spring( ref jawOpen, ref jawVel, jawWant, 2f, 0.6f, dt );
		SmoothKit.Spring( ref blow, ref blowVel, Open ? 1f : 0f, 1.5f, 0.7f, dt );
		SmoothKit.Spring( ref swell, ref swellVel, act == Act.Song && a < 1.3f ? 1f : 0f, 2f, 0.5f, dt );
		jaw.Rotation = new Vector3( -jawOpen * 0.55f, 0f, 0f );
		blowGlow.EmissionEnergyMultiplier = 0.2f + blow * (3.5f + MathF.Sin( t * 7f ));
		foreach ( var core in blowCores ) core.Scale = new Vector3( 1f, 0.4f + blow * 0.8f, 1f );
		barnacleGlow.EmissionEnergyMultiplier = act == Act.Brood ? 2f + 2f * MathF.Sin( t * 14f ) : 0f;
		var windUp = act switch
		{
			Act.Above => a > AimTime - 1.6f,
			Act.Song or Act.Swallow or Act.Sweep or Act.Brood => a < 1.3f,
			Act.Dive => true,
			_ => false,
		};
		SmoothKit.Spring( ref charge, ref chargeVel, windUp ? 1f : 0f, 2.5f, 0.8f, dt );
		skin.SetShaderParameter( "charge", MathX.Clamp( charge, 0f, 1.2f ) );
		Eyes( t, dt );
		Whiskers( t );
		headLight.LightEnergy = 1.4f + blow * 1.2f + swell * 1.5f;

		Place();
		DrawMark( t );
		skin.SetShaderParameter( "flash", MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f ) );
		skin.SetShaderParameter( "hurt", 1f - health.Fraction );
		var t2 = t * 1.4f;
		foreach ( var f in fins ) f.Rotation = new Vector3( MathF.Sin( t2 ) * 0.25f, 0f, 0f );
		if ( fluke is not null ) fluke.Rotation = new Vector3( MathF.Sin( t * 1.1f ) * 0.4f, 0f, 0f );
		HoldSwallowed( dt );
	}

	/// <summary> The eight eyes: narrowed while it moves, wide and bright while it rests open, each turning a little toward
	/// the nearest player, and one at a time blinking. </summary>
	void Eyes( float t, float dt )
	{
		eyeRim.EmissionEnergyMultiplier = 1.2f + blow * 1.6f + charge * 1.2f;
		nextBlink -= dt;
		if ( blinking < 0 && nextBlink <= 0f ) { blinking = Rng.Int( 0, eyes.Count - 1 ); blinkT = 0f; }
		if ( blinking >= 0 ) { blinkT += dt; if ( blinkT > 0.22f ) { blinking = -1; nextBlink = Rng.Float( 0.25f, 1.1f ); } }
		var prey = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var open = 0.5f + 0.5f * MathX.Clamp( blow, 0f, 1f );
		for ( var i = 0; i < eyes.Count; i++ )
		{
			var (eye, almond, rest) = eyes[i];
			var lid = open;
			if ( i == blinking ) lid *= 1f - MathF.Sin( MathX.Clamp( blinkT / 0.22f, 0f, 1f ) * MathF.PI ) * 0.95f;
			var turn = Basis.Identity;
			if ( prey is not null )
			{
				var local = eye.GlobalTransform.AffineInverse() * (prey.RealPosition + Vector3.Up * 1.2f);
				var yaw = MathX.Clamp( MathF.Atan2( local.X, local.Z ), -0.4f, 0.4f );
				var pitch = MathX.Clamp( MathF.Atan2( -local.Y, local.Z ), -0.3f, 0.3f );
				turn = new Basis( Vector3.Up, yaw ) * new Basis( Vector3.Right, pitch );
			}
			almond.Basis = turn * Basis.FromScale( new Vector3( 1f, MathF.Max( lid, 0.05f ), 1f ) );
		}
	}

	/// <summary> The whiskers hang down whatever the head does, trail back as it swims and sway; the lures throb. </summary>
	void Whiskers( float t )
	{
		var moved = (GlobalPosition - lastWhisker) / MathF.Max( (float)GetProcessDeltaTime(), 1e-3f );
		lastWhisker = GlobalPosition;
		var drag = -moved.LimitLength( 20f ) * 0.04f;
		lureGlow.EmissionEnergyMultiplier = 2.2f + 1.2f * MathF.Sin( t * 2.3f ) + charge * 2f;
		for ( var w = 0; w < whiskers.Count; w++ )
		{
			var chain = whiskers[w];
			var sway = new Vector3( MathF.Sin( t * 0.9f + w * 1.3f ), 0f, MathF.Cos( t * 0.7f + w * 2.1f ) ) * 0.35f;
			var down = (Vector3.Down + drag + sway).Normalized();
			chain[0].GlobalBasis = new Basis( new Quaternion( Vector3.Down, down ) );
			for ( var j = 1; j < chain.Count; j++ )
				chain[j].Rotation = new Vector3( MathF.Sin( t * 1.6f + w + j * 0.9f ) * 0.22f, 0f, MathF.Cos( t * 1.3f + w * 0.7f + j ) * 0.18f );
		}
	}

	/// <summary> Lays the head and every segment along the trail. </summary>
	void Place()
	{
		var at = GlobalPosition;
		if ( trail.Count == 0 || trail[^1].DistanceTo( at ) > 0.3f )
		{
			// A jump (a puppet's first snapshot) is filled in so the body doesn't stretch.
			var last = trail.Count > 0 ? trail[^1] : at;
			var gap = last.DistanceTo( at );
			var steps = Math.Min( 400, (int)(gap / 0.3f) );
			for ( var s = 1; s <= steps; s++ ) trail.Add( last.Lerp( at, s / (float)steps ) );
			if ( steps == 0 ) trail.Add( at );
			var keep = (int)((Segments + 4) * Spacing / 0.3f) + 40;
			if ( trail.Count > keep ) trail.RemoveRange( 0, trail.Count - keep );
		}
		head.GlobalTransform = GlobalTransform.Orthonormalized();
		// Each segment sits on the trail at its distance behind the head (the first a little way off its back).
		var want = 3.6f;
		var walked = 0f;
		var i = trail.Count - 1;
		var prev = at;
		for ( var s = 0; s < Segments; s++, want += Spacing )
		{
			var p = prev;
			while ( i > 0 )
			{
				var step = trail[i].DistanceTo( trail[i - 1] );
				if ( walked + step >= want ) { p = trail[i].Lerp( trail[i - 1], (want - walked) / MathF.Max( step, 1e-4f ) ); break; }
				walked += step;
				i--;
				p = trail[i];
			}
			var ahead = s == 0 ? at : segs[s - 1].GlobalPosition;
			var dir = ahead - p;
			if ( dir.LengthSquared() < 1e-4f ) dir = -GlobalBasis.Z;
			var wobble = new Basis( Vector3.Forward, MathF.Sin( (float)Clock.Now * 0.9f - s * 0.4f ) * 0.05f );
			segs[s].GlobalTransform = new Transform3D( Shapes.LookAt( dir.Normalized() ) * wobble, p );
			prev = p;
		}
	}

	// ---- The brain ----

	void Think( float dt )
	{
		var a = (float)sinceAct;
		var prey = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var head0 = GlobalPosition;
		switch ( act )
		{
			case Act.Above:
			{
				var roof = RoofC;
				var circle = new Vector3( MathF.Cos( a * 0.8f ) * 4f, 0f, MathF.Sin( a * 0.8f ) * 4f );
				GlobalPosition = GlobalPosition.Lerp( new Vector3( 0f, roof + 16f, 0f ) + circle, 1f - MathF.Exp( -2f * dt ) );
				if ( a < AimTime - LockBefore ) { spot = AimSpot( prey ); locked = false; }
				else locked = true;
				if ( a >= AimTime )
				{
					p0 = GlobalPosition;
					p1 = new Vector3( 0f, roof - 8f, 0f );
					p2 = spot + Vector3.Up * 1.6f;
					Begin( Act.Dive );
				}
				break;
			}
			case Act.Dive:
			{
				var k = MathX.Clamp( a / DiveTime, 0f, 1f );
				GlobalPosition = Bezier( p0, p1, p2, k * k * (1.6f - 0.6f * k) );
				if ( k >= 1f ) Impact();
				break;
			}
			case Act.Rest:
				GlobalPosition = GlobalPosition.Lerp( spot + Vector3.Up * 1.6f, 1f - MathF.Exp( -3f * dt ) );
				if ( a > RestTime ) Next();
				break;
			case Act.Sweep:
			{
				var floor = Floor;
				var depth = floor - 1.1f;
				var ringPoint = Ring( ringAngle ) with { Y = depth };
				if ( ringTravel <= 0f && GlobalPosition.Flat().DistanceTo( ringPoint.Flat() ) > 1f )
					GlobalPosition = GlobalPosition.MoveToward( ringPoint, SweepSpeed * dt );   // into the sand at the ring
				else
				{
					var step = SweepSpeed * dt / SweepRadius;
					ringAngle += step * ringDir;
					ringTravel += step;
					GlobalPosition = Ring( ringAngle ) with { Y = depth };
					if ( Engine.GetProcessFrames() % 3 == 0 ) GpuFx.SandPuff( GlobalPosition with { Y = floor + 0.3f } + Rng.InSphere( 2f ).Flat(), Vector3.Up * Rng.Float( 1f, 3f ) + Rng.InSphere( 1f ), SeaStir.SiltColour( GlobalPosition ), Rng.Float( 10f, 18f ), Rng.Float( 1.5f, 2.6f ) );
				}
				SweepContact();
				if ( ringTravel >= MathF.Tau ) Next();
				break;
			}
			case Act.Song:
			{
				Hover( prey, 4.5f, dt );
				if ( !struck && a >= 1.3f ) { struck = true; Sing( true ); }
				if ( a > 2.6f ) Next();
				break;
			}
			case Act.Swallow:
			{
				Hover( prey, 2.4f, dt );
				if ( a > 0.6f && a < 4.2f ) Suck( dt );
				if ( swallowed.Count > 0 ) { gulpedAt = Clock.Now; gulpStartHealth = health.Current; Begin( Act.Gulp ); break; }
				if ( a > 4.4f ) Next();
				break;
			}
			case Act.Gulp:
			{
				GlobalPosition = GlobalPosition.Lerp( GlobalPosition with { Y = Floor + 1.6f }, 1f - MathF.Exp( -2f * dt ) );
				var out_ = Clock.Now - gulpedAt > StomachTime || gulpStartHealth - health.Current > health.MaxHealth * StomachEscapeShare || swallowed.Count == 0;
				if ( out_ ) { Spit(); Next(); }
				break;
			}
			case Act.Brood:
			{
				Hover( prey, 2f, dt );
				if ( !struck && a >= 1.6f ) { struck = true; Brood(); }
				if ( a > 2.4f ) Next();
				break;
			}
			case Act.Withdraw:
			{
				var k = MathX.Clamp( a / 3.5f, 0f, 1f );
				GlobalPosition = Bezier( p0, p1, p2, SmoothKit.Ease( k ) );
				if ( k >= 1f ) Begin( Act.Above );
				break;
			}
		}
		Steer( head0, prey, dt );
	}

	/// <summary> The head faces where it is going (or, holding still, the player or the way it lay down). </summary>
	void Steer( Vector3 was, PlayerSurvivor prey, float dt )
	{
		var moved = GlobalPosition - was;
		Vector3 look;
		if ( act is Act.Song or Act.Swallow or Act.Brood && prey is not null ) look = ((prey.RealPosition + Vector3.Up) - GlobalPosition).Normalized();
		else if ( act is Act.Rest or Act.Gulp ) look = restForward;
		else if ( moved.Length() > 0.02f ) look = moved.Normalized();
		else return;
		if ( act is Act.Song or Act.Swallow or Act.Brood ) look = (look.Flat().SafeNormal() + Vector3.Up * MathX.Clamp( look.Y, -0.4f, 0.4f )).Normalized();
		var wanted = Shapes.LookAt( look ).GetRotationQuaternion();
		GlobalBasis = new Basis( GlobalBasis.Orthonormalized().GetRotationQuaternion().SafeSlerp( wanted, MathX.Clamp( dt * (act == Act.Dive ? 8f : 3f), 0f, 1f ) ) );
	}

	void Hover( PlayerSurvivor prey, float height, float dt )
	{
		var want = GlobalPosition with { Y = Floor + height };
		GlobalPosition = GlobalPosition.Lerp( want, 1f - MathF.Exp( -2f * dt ) );
	}

	static Vector3 Bezier( Vector3 a, Vector3 b, Vector3 c, float k ) => a * (1f - k) * (1f - k) + b * 2f * k * (1f - k) + c * k * k;

	Vector3 Ring( float angle ) => new( MathF.Cos( angle ) * SweepRadius, 0f, MathF.Sin( angle ) * SweepRadius );

	/// <summary> The ram's spot: under the nearest player, kept in the hub, clear of the statue and the hub's edge. </summary>
	Vector3 AimSpot( PlayerSurvivor prey )
	{
		var flat = (prey?.RealPosition ?? Abyss.Arrival).Flat();
		var r = flat.Length();
		if ( r < 0.1f ) flat = Vector3.Forward * 9f;
		flat = flat.Normalized() * MathX.Clamp( r, 9f, 20f );
		return flat with { Y = SeaTerrain.HeightAt( flat.X, flat.Z ) };
	}

	void Next()
	{
		if ( queue.Count == 0 ) { Begin( Act.Withdraw ); return; }
		var next = queue[0];
		queue.RemoveAt( 0 );
		Begin( next );
	}

	void Begin( Act next )
	{
		act = next;
		sinceAct = 0f;
		struck = false;
		switch ( next )
		{
			case Act.Above: locked = false; Say( "" ); break;
			case Act.Dive: Say( "SKYLIGHT RAM" ); Sfx.Play( "leviathan_roar", spot + Vector3.Up * 8f, 1.3f, 1.1f ); break;
			case Act.Rest: Say( "IT'S DOWN. HIT THE BLOWHOLES" ); break;
			case Act.Sweep:
				Say( "COIL SWEEP" );
				ringAngle = MathF.Atan2( GlobalPosition.Z, GlobalPosition.X );
				ringTravel = 0f;
				ringDir = Rng.Float() < 0.5f ? 1f : -1f;
				sweepHit.Clear();
				Sfx.Play( "warden_charge", GlobalPosition, 1.3f, 0.6f );
				break;
			case Act.Song: Say( "WHALE SONG" ); break;
			case Act.Swallow: Say( "SWALLOW" ); Sfx.Play( "leviathan_gulp", GlobalPosition, 1f, 0.7f ); break;
			case Act.Gulp: Say( "" ); break;   // the HUD says SHOOT YOUR WAY OUT to the one inside
			case Act.Brood: Say( "BARNACLE BROOD" ); break;
			case Act.Withdraw:
				p0 = GlobalPosition;
				p1 = new Vector3( GlobalPosition.X * 0.3f, RoofC - 6f, GlobalPosition.Z * 0.3f );
				p2 = new Vector3( 0f, RoofC + 16f, 0f );
				Sfx.Play( "leviathan_moan", GlobalPosition, 1f, 0.9f );
				break;
		}
	}

	/// <summary> The ram lands: a crash, a shock ring, whoever is under it hurt, stalactites coming down, the roof cracking. </summary>
	void Impact()
	{
		Rams++;
		ram++;
		cycle++;
		var at = spot;
		restForward = GlobalBasis.Z.Flat().SafeNormal() is var back && back != Vector3.Zero ? -back : Vector3.Forward;
		Sfx.Play( "leviathan_crash", at, 1.6f, 0.9f );
		ShockRing.Send( GetTree().CurrentScene, at, 16f, 30f, Puppet ? 0f : RingDamage, 0f, this );
		SeaStir.Blast( at, 2f );
		SeaStir.SiltRing( at, 2.5f );
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive ) continue;
			var d = p.RealPosition.DistanceTo( at );
			if ( d < 40f ) p.Juice?.Shake( 1.2f * (1f - d / 40f) + 0.2f );
			if ( Puppet || d > 5.5f || p.Controller is not { } c ) continue;
			RamHits++;
			p.Health?.OnDamage( new DamageInfo { Damage = RamDamage, Attacker = this, Position = p.RealPosition + Vector3.Up, Origin = at } );
			c.Velocity += (p.RealPosition - at).Flat().SafeNormal() * 12f + Vector3.Up * 6f;
		}
		Stalactites( ram );
		CrackRoof( ram );
		// The two moves after it rests: never the same twice, the brood every other time round.
		queue.Clear();
		var pool = new List<Act> { Act.Sweep, Act.Song, Act.Swallow };
		if ( cycle % 2 == 0 ) pool.Add( Act.Brood );
		for ( var i = 0; i < 2 && pool.Count > 0; i++ )
		{
			var pick = pool[Rng.Int( 0, pool.Count - 1 )];
			pool.Remove( pick );
			queue.Add( pick );
		}
		Begin( Act.Rest );
	}

	/// <summary> Stalactites shaken off the roof round the hub: a mark on the floor, then the rock comes down. Same spots on every machine. </summary>
	void Stalactites( int seed )
	{
		var random = new Random( seed * 131 + 7 );
		for ( var i = 0; i < 9; i++ )
		{
			var a = (float)random.NextDouble() * MathF.Tau;
			var r = 7f + (float)random.NextDouble() * 17f;
			var at = new Vector3( MathF.Cos( a ) * r, 0f, MathF.Sin( a ) * r );
			at.Y = SeaTerrain.HeightAt( at.X, at.Z );
			FallingStalactite.Drop( GetTree().CurrentScene, at, 0.5f + (float)random.NextDouble() * 1.2f, Puppet ? 0f : 25f, this );
		}
	}

	/// <summary> Each ram cracks the roof: a new shaft of light from above and chips of rock. </summary>
	void CrackRoof( int seed )
	{
		if ( Abyss.Root is null || !IsInstanceValid( Abyss.Root ) || Cracks >= 64 ) return;
		Cracks++;
		var random = new Random( seed * 977 + 3 );
		var a = (float)random.NextDouble() * MathF.Tau;
		var r = 8f + (float)random.NextDouble() * 10f;
		var x = MathF.Cos( a ) * r;
		var z = MathF.Sin( a ) * r;
		var roof = Abyss.RoofAt( x, z );
		var light = new SpotLight3D { Name = "Roof Crack Light", LightColor = new Color( 0.7f, 0.85f, 1f ), LightEnergy = 4f, SpotRange = 40f, SpotAngle = 16f, ShadowEnabled = false };
		Abyss.Root.AddChild( light );
		light.GlobalTransform = new Transform3D( Shapes.LookAt( Vector3.Down, Vector3.Forward ), new Vector3( x, roof - 0.5f, z ) );
		// Jagged glowing cracks on the roof's underside, and a soft light round them so the broken rock shows.
		var glow = Sculpt.Glow( new Color( 0.75f, 0.9f, 1f ), 4f );
		var crack = new Node3D { Name = "Roof Crack" };
		Abyss.Root.AddChild( crack );
		crack.GlobalPosition = new Vector3( x, roof - 0.15f, z );
		for ( var i = 0; i < 5; i++ )
		{
			var ang = i * 1.26f + (float)random.NextDouble() * 0.5f;
			var p = Vector3.Zero;
			for ( var k = 0; k < 4; k++ )
			{
				ang += ((float)random.NextDouble() - 0.5f) * 0.9f;
				var step = new Vector3( MathF.Cos( ang ), 0f, MathF.Sin( ang ) ) * (0.9f - k * 0.15f);
				Sculpt.Limb( crack, p, p + step, 0.07f - k * 0.012f, 0.05f - k * 0.01f, glow, "Crack" );
				p += step;
			}
		}
		crack.AddChild( new OmniLight3D { Name = "Crack Glow", LightColor = new Color( 0.7f, 0.85f, 1f ), LightEnergy = 1.6f, OmniRange = 7f, ShadowEnabled = false, Position = Vector3.Down * 1.2f } );
		var recipe = GibRecipes.CaveRock();
		for ( var i = 0; i < 5; i++ )
			GibKit.Chips( recipe, new Vector3( x, roof - 0.8f, z ) + Rng.InSphere( 2f ).Flat(), Vector3.Down, Vector3.Down, Rng.Float( 0.15f, 0.35f ), 1, 0.4f );
		Sfx.Play( "rock_crumble", new Vector3( x, roof - 1f, z ), 1f, 0.85f );
	}

	/// <summary> The coil sweep: its back, half buried, hurts anyone standing in its way; one in the air goes over. </summary>
	void SweepContact()
	{
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c ) continue;
			if ( sweepHit.TryGetValue( p, out var last ) && Clock.Now - last < 1.2 ) continue;
			var feet = p.RealPosition;
			for ( var s = -1; s < Segments; s++ )
			{
				var centre = s < 0 ? GlobalPosition : segs[s].GlobalPosition;
				var r = s < 0 ? 2.1f : radius[s];
				var top = centre.Y + r * 0.95f;
				if ( top < feet.Y + 0.05f || (feet - centre).Flat().Length() > r + c.BodyRadius ) continue;
				sweepHit[p] = Clock.Now;
				SweepHits++;
				p.Health?.OnDamage( new DamageInfo { Damage = SweepDamage, Attacker = this, Position = feet + Vector3.Up * 0.5f, Origin = centre } );
				c.Velocity += (feet - centre).Flat().SafeNormal() * 8f + Vector3.Up * 5f;
				p.Juice?.Shake( 0.6f );
				Sfx.Play( "crab_snap", feet, 1f, 0.4f );
				break;
			}
		}
	}

	/// <summary> The whale song: a cone of sound from its mouth that throws players back, slows them and blurs their view. </summary>
	void Sing( bool harm )
	{
		Songs++;
		var mouth = MouthAt;
		var forward = -head.GlobalBasis.Z.Normalized();
		Sfx.Play( "leviathan_song", mouth, 1.5f, Rng.Float( 0.9f, 1.05f ) );
		for ( var i = 0; i < 4; i++ ) SongWave.Send( GetTree().CurrentScene, mouth, forward, i * 0.12f );
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c ) continue;
			var to = p.RealPosition + Vector3.Up - mouth;
			var d = to.Length();
			if ( d > SongRange || to.Normalized().Dot( forward ) < 0.8f ) continue;
			if ( harm )
			{
				SongHits++;
				p.Health?.OnDamage( new DamageInfo { Damage = SongDamage, Attacker = this, Position = p.RealPosition + Vector3.Up, Origin = mouth } );
				c.Velocity += to.Flat().SafeNormal() * 14f + Vector3.Up * 3f;
			}
			// The slow and the blur are the owner's (the local robot here).
			if ( !c.IsRemote && p.Afflictions is { } af ) { af.Cursed( PlayerAfflictions.Curse.Slow ); af.Cursed( PlayerAfflictions.Curse.Ink ); }
			p.Juice?.Shake( 0.7f );
		}
	}

	/// <summary> The swallow: players in front are pulled to its mouth; one that reaches it goes down. </summary>
	void Suck( float dt )
	{
		var mouth = MouthAt;
		var forward = -head.GlobalBasis.Z.Normalized();
		if ( Engine.GetProcessFrames() % 2 == 0 )
		{
			var from_ = mouth + (forward + Rng.InSphere( 0.6f )).Normalized() * Rng.Float( 6f, 14f );
			GpuFx.SandPuff( from_, (mouth - from_).Normalized() * 8f, SeaStir.SiltColour( from_ ), Rng.Float( 6f, 10f ), 0.9f );
		}
		SeaStir.Pull( mouth, forward, SuckRange );
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c || c.Scripted || swallowed.Contains( p ) ) continue;
			var chest = p.RealPosition + Vector3.Up;
			var to = chest - mouth;
			var d = to.Length();
			if ( d > SuckRange || to.Normalized().Dot( forward ) < 0.5f ) continue;
			if ( d < 3.2f ) { Swallow( p ); continue; }
			c.Velocity += -to.Normalized() * SuckStrength * (1.4f - d / SuckRange) * dt * 6f;
		}
	}

	void Swallow( PlayerSurvivor p )
	{
		swallowed.Add( p );
		Swallows++;
		Sfx.Play( "leviathan_gulp", MouthAt, 1.4f, 0.9f );
		if ( p.Controller is { } c ) { c.Scripted = true; c.Velocity = Vector3.Zero; }
		p.Juice?.Shake( 1f );
	}

	/// <summary> Everyone in its stomach is held there, burned by acid, until it spits them out. </summary>
	void HoldSwallowed( float dt )
	{
		if ( swallowed.Count == 0 ) return;
		var acid = sinceAcid > 0.5f;
		if ( acid ) sinceAcid = 0f;
		for ( var i = swallowed.Count - 1; i >= 0; i-- )
		{
			var p = swallowed[i];
			if ( !IsInstanceValid( p ) || !p.IsAlive || p.Controller is not { } c || !IsInstanceValid( c ) ) { if ( IsInstanceValid( p ) && p.Controller is { } dead && IsInstanceValid( dead ) ) dead.Scripted = false; swallowed.RemoveAt( i ); continue; }
			c.Scripted = true;
			c.GlobalPosition = StomachAt + new Vector3( i * 1.5f - 1f, -4.9f, 0f );
			c.Velocity = Vector3.Zero;
			if ( acid )
			{
				p.Health?.OnDamage( new DamageInfo { Damage = AcidPerSecond * 0.5f, Attacker = this, Position = c.GlobalPosition, Origin = c.GlobalPosition + Vector3.Down } );
				if ( Rng.Float() < 0.4f ) Sfx.Play( "stomach_gurgle", c.GlobalPosition, 0.8f, Rng.Float( 0.8f, 1.1f ) );
			}
		}
	}

	/// <summary> Spat out in front of its mouth, flung clear. </summary>
	void Spit()
	{
		if ( swallowed.Count > 0 ) Spits++;
		var mouth = MouthAt;
		var forward = (-head.GlobalBasis.Z).Flat().SafeNormal();
		Sfx.Play( "leviathan_gulp", mouth, 1.4f, 0.6f );
		foreach ( var p in swallowed )
		{
			if ( !IsInstanceValid( p ) || p.Controller is not { } c || !IsInstanceValid( c ) ) continue;
			var land = mouth + forward * 4f;
			land.Y = SeaTerrain.HeightAt( land.X, land.Z ) + 0.3f;
			c.GlobalPosition = land;
			c.ResetPhysicsInterpolation();
			c.Scripted = false;
			c.Velocity = forward * 9f + Vector3.Up * 4f;
			p.Juice?.Shake( 0.8f );
		}
		swallowed.Clear();
		AlienBlast.Spawn( GetTree().CurrentScene, mouth + forward * 2f, 0.5f );
	}

	void ReleaseAll()
	{
		if ( swallowed.Count > 0 && head is not null && IsInstanceValid( head ) ) { Spit(); return; }
		foreach ( var p in swallowed ) if ( IsInstanceValid( p ) && p.Controller is { } c && IsInstanceValid( c ) ) c.Scripted = false;
		swallowed.Clear();
	}

	/// <summary> The barnacles on its back burst: puffers and an angler join the wave. </summary>
	void Brood()
	{
		Broods++;
		Sfx.Play( "goo_pop", segs[9].GlobalPosition, 1.2f, 0.6f );
		var d = WaveDirector.Current;
		var near = PlayerSurvivor.FindNearestAlive( GlobalPosition )?.RealPosition ?? Vector3.Zero;
		for ( var i = 0; i < barnacles.Count; i++ )
		{
			var at = barnacles[i].GlobalPosition;
			AlienBlast.Spawn( GetTree().CurrentScene, at, 0.4f );
			if ( Puppet || d is null ) continue;
			var ground = at with { Y = SeaTerrain.HeightAt( at.X, at.Z ) };
			if ( Abyss.Root is not null && Abyss.InWall( ground, 1f ) ) ground = Vector3.Zero.Lerp( ground, 0.6f ) with { Y = SeaTerrain.HeightAt( ground.X * 0.6f, ground.Z * 0.6f ) };
			d.SpawnAt( i == 1 ? WaveDirector.EnemyKind.Angler : WaveDirector.EnemyKind.Puffer, ground, near );
			if ( i != 1 ) d.SpawnAt( WaveDirector.EnemyKind.Puffer, ground + Rng.InSphere( 1.5f ).Flat(), near );
		}
	}

	/// <summary> The ram's mark: a ring that follows the player, red and pulsing once it locks. </summary>
	void DrawMark( float t )
	{
		markerMesh.ClearSurfaces();
		var show = act == Act.Above || (act == Act.Dive && (float)sinceAct < DiveTime);
		marker.Visible = show;
		if ( !show ) return;
		var colour = locked ? new Color( 1f, 0.25f, 0.2f, 0.9f ) * (0.75f + 0.25f * MathF.Sin( t * 20f )) : new Color( 1f, 0.7f, 0.3f, 0.6f );
		markerMesh.SurfaceBegin( Mesh.PrimitiveType.Triangles );
		GroundRing.Flat( markerMesh, this, spot, 4.6f, 5.5f, colour, 48 );
		GroundRing.Flat( markerMesh, this, spot, 0f, 0.8f, colour, 16 );
		GroundRing.Strip( markerMesh, this, spot + Vector3.Left * 4f, spot + Vector3.Right * 4f, 0.15f, colour );
		GroundRing.Strip( markerMesh, this, spot + Vector3.Forward * 4f, spot + Vector3.Back * 4f, 0.15f, colour );
		markerMesh.SurfaceEnd();
	}

	/// <summary> Tests and the dev menu: start a move now. </summary>
	public void Force( Act next )
	{
		if ( next == Act.Dive ) { Begin( Act.Above ); sinceAct = AimTime - 0.01f; return; }
		if ( next == Act.Above ) { Begin( Act.Withdraw ); return; }
		Begin( next );
	}
	/// <summary> Tests: aim the ram at a spot. </summary>
	public void AimAt( Vector3 at ) => spot = at with { Y = SeaTerrain.HeightAt( at.X, at.Z ) };

	// ---- Online ----

	Act netAct = Act.Above;
	/// <summary> A joiner's copy: the host's moves play their sights and sounds here; only the host's do harm. </summary>
	void PuppetActs( float a )
	{
		if ( act != netAct )
		{
			var was = netAct;
			netAct = act;
			if ( act == Act.Rest && was == Act.Dive ) { ram++; Stalactites( ram ); CrackRoof( ram ); Sfx.Play( "leviathan_crash", spot, 1.6f, 0.9f ); ShockRing.Send( GetTree().CurrentScene, spot, 16f, 30f, 0f, 0f, this ); }
		}
		if ( !struck && act == Act.Song && a >= 1.3f ) { struck = true; Sing( false ); }
		if ( !struck && act == Act.Brood && a >= 1.6f ) { struck = true; Brood(); }
	}

	public override void NetWrite( List<float> s )
	{
		s.Add( (int)act ); s.Add( (float)sinceAct ); s.Add( spot.X ); s.Add( spot.Y ); s.Add( spot.Z ); s.Add( locked ? 1f : 0f ); s.Add( ram );
	}

	public override void NetRead( ReadOnlySpan<float> s )
	{
		if ( s.Length < 7 ) return;
		var next = (Act)(int)s[0];
		var changed = next != act;
		spot = new Vector3( s[2], s[3], s[4] );
		locked = s[5] > 0.5f;
		if ( changed ) { act = next; struck = false; Say( next switch { Act.Dive => "SKYLIGHT RAM", Act.Rest => "IT'S DOWN. HIT THE BLOWHOLES", Act.Sweep => "COIL SWEEP", Act.Song => "WHALE SONG", Act.Swallow => "SWALLOW", Act.Brood => "BARNACLE BROOD", _ => "" } ); }
		ram = Math.Max( ram, (int)s[6] - (changed && next == Act.Rest ? 1 : 0) );
		NetTimer( ref sinceAct, s[1], changed );
	}

	// ---- Hits and death ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( NetCreatures.Intercept( this, index, ref damage, direction, position, origin, shooter, scoreHit ) ) return;
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		var weak = index switch { 2 or 3 => Open, 4 => Open, 7 or 8 => true, _ => false };
		var scale = index switch
		{
			2 or 3 => Open ? EyeMultiplier : 1.2f,
			4 => Open ? BlowholeMultiplier : HideMultiplier,
			7 => StomachMultiplier,
			8 => UlcerMultiplier,
			_ => HideMultiplier,
		};
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * scale, Position = position, Origin = origin, Attacker = shooter } );
		if ( gibs++ % 3 == 0 ) GibKit.Chips( Gibs, position, -direction, direction, weak ? 0.25f : 0.15f, 1, 0.6f );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, weak, scoreHit );
	}
	int gibs;

	protected override void OnDied()
	{
		RaiseKilled();
		ReleaseAll();
		Sfx.Play( "leviathan_roar", GlobalPosition, 1.6f, 0.7f );
		GibKit.Burst( Gibs, GlobalPosition, lastShotDirection, 0.8f, 24, 1.3f );
		AlienBlast.Spawn( GetTree().CurrentScene, GlobalPosition, 2f );
		foreach ( var part in parts ) if ( IsInstanceValid( part ) ) part.QueueFree();
		if ( marker is not null && IsInstanceValid( marker ) ) marker.Visible = false;
		// The body slumps across the hub and stays (in the Abyss's node, so it goes with the area).
		if ( rig is not null && IsInstanceValid( rig ) )
		{
			carcassed = true;
			var home = Abyss.Root is not null && IsInstanceValid( Abyss.Root ) ? (Node)Abyss.Root : GetTree().CurrentScene;
			rig.Reparent( home, true );
			var nodes = new List<Node3D> { head };
			nodes.AddRange( segs );
			LeviathanCarcass.Lay( rig, nodes, GlobalPosition, restForward );
		}
		QueueFree();
	}
}

/// <summary>
/// The dead Leviathan collapsing across the hub: the head drops to the floor and each segment after it slumps into a
/// curve on the floor behind it, bending clear of the statue. It stays for the rest of the Abyss.
/// </summary>
public partial class LeviathanCarcass : Node
{
	public static int Laid;
	readonly List<(Node3D Node, Transform3D From, Transform3D To, float Delay)> moves = new();
	float age;

	public static void Lay( Node3D rig, List<Node3D> nodes, Vector3 headAt, Vector3 forward )
	{
		Laid++;
		var c = new LeviathanCarcass { Name = "Leviathan Carcass" };
		rig.AddChild( c );
		var flatHead = headAt.Flat();
		if ( flatHead.Length() < 7f ) flatHead = (flatHead.Length() < 0.1f ? Vector3.Forward : flatHead.Normalized()) * 9f;
		var dir = -forward.Flat().SafeNormal();
		if ( dir == Vector3.Zero ) dir = flatHead.Normalized();
		var at = flatHead;
		var bend = Rng.Float() < 0.5f ? 1f : -1f;
		for ( var i = 0; i < nodes.Count; i++ )
		{
			var n = nodes[i];
			if ( !GodotObject.IsInstanceValid( n ) ) continue;
			var lie = i == 0 ? 1.4f : 0.6f;
			var to = new Transform3D( Shapes.LookAt( i == 0 ? -dir : -dir ) * new Basis( Vector3.Forward, bend * 0.35f ), at with { Y = SeaTerrain.HeightAt( at.X, at.Z ) + lie } );
			c.moves.Add( (n, n.GlobalTransform, to, i * 0.07f) );
			// Curl a little, and keep out of the statue's base.
			dir = new Basis( Vector3.Up, bend * 0.12f ) * dir;
			var nextAt = at + dir * (i == 0 ? 4f : Leviathan.Spacing);
			if ( nextAt.Flat().Length() < 7.5f ) { dir = (dir + nextAt.Flat().Normalized() * 0.8f).Normalized(); nextAt = at + dir * Leviathan.Spacing; }
			at = nextAt;
		}
	}

	public override void _Process( double delta )
	{
		age += (float)delta;
		var done = true;
		foreach ( var (n, from_, to, delay) in moves )
		{
			if ( !GodotObject.IsInstanceValid( n ) ) continue;
			var k = MathX.Clamp( (age - delay) / 2.2f, 0f, 1f );
			if ( k < 1f ) done = false;
			var e = k * k;
			n.GlobalTransform = new Transform3D( new Basis( from_.Basis.Orthonormalized().GetRotationQuaternion().SafeSlerp( to.Basis.GetRotationQuaternion(), SmoothKit.Ease( k ) ) ), from_.Origin.Lerp( to.Origin, e ) );
		}
		if ( done ) SetProcess( false );
	}
}

/// <summary> A stalactite shaken off the roof: a warning mark on the floor, then it falls, hurting whoever is under it. </summary>
public partial class FallingStalactite : Node3D
{
	Vector3 ground;
	float delay, age, damage, y0;
	Node3D owner, rock;
	ImmediateMesh mark;
	bool landed;

	public static void Drop( Node parent, Vector3 ground, float delay, float damage, Node3D owner )
	{
		var s = new FallingStalactite { Name = "Falling Stalactite", ground = ground, delay = delay, damage = damage, owner = owner };
		parent.AddChild( s );
	}

	public override void _Ready()
	{
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		mark = new ImmediateMesh();
		var paint = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		AddChild( new MeshInstance3D { Mesh = mark, MaterialOverride = paint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
		y0 = Abyss.Root is not null ? Abyss.RoofAt( ground.X, ground.Z ) - 1f : ground.Y + 25f;
		rock = new Node3D { Name = "Rock" };
		AddChild( rock );
		Sculpt.Cone( rock, Vector3.Zero, Vector3.Down * 3f, 0.9f, Abyss.RockPaint, 7, "Stalactite" );
		rock.GlobalPosition = ground with { Y = y0 };
	}

	public override void _Process( double delta )
	{
		var dt = MathF.Min( (float)delta, 0.1f );
		age += dt;
		mark.ClearSurfaces();
		if ( !landed )
		{
			var k = MathX.Clamp( age / (delay + 1.2f), 0f, 1f );
			mark.SurfaceBegin( Mesh.PrimitiveType.Triangles );
			var pulse = 0.6f + 0.4f * MathF.Sin( age * (8f + 14f * k) );
			var col = new Color( 1f, 0.35f, 0.2f, (0.3f + 0.5f * k) * pulse );
			GroundRing.Flat( mark, this, ground, 1.8f, 2.2f, col, 28 );
			GroundRing.Flat( mark, this, ground, 0f, 0.3f + 1.4f * k, col * 0.6f, 20 );
			mark.SurfaceEnd();
		}
		if ( age < delay + 1.2f ) return;
		var fall = age - delay - 1.2f;
		var y = y0 - 0.5f * 30f * fall * fall;
		if ( !landed && y - 3f <= ground.Y )
		{
			landed = true;
			y = ground.Y + 3f;
			Sfx.Play( "rock_crumble", ground, 0.9f, Rng.Float( 0.9f, 1.15f ) );
			GibKit.Chips( GibRecipes.CaveRock(), ground + Vector3.Up * 0.5f, Vector3.Up, Vector3.Up, 0.25f, 3, 0.8f );
			SeaStir.SiltRing( ground, 0.8f );
			if ( damage > 0f )
				foreach ( var p in PlayerSurvivor.All )
				{
					if ( !p.IsAlive || p.RealPosition.Flat().DistanceTo( ground.Flat() ) > 2.2f || MathF.Abs( p.RealPosition.Y - ground.Y ) > 3f ) continue;
					Leviathan.FallingHits++;
					p.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = IsInstanceValid( owner ) ? owner : null, Position = p.RealPosition + Vector3.Up, Origin = ground + Vector3.Up * 5f } );
					p.Juice?.Shake( 0.5f );
				}
		}
		if ( landed ) y = ground.Y + 3f - MathX.Clamp( (age - delay - 1.2f - 1f) * 1.5f, 0f, 3.5f );
		rock.GlobalPosition = ground with { Y = y };
		if ( landed && age > delay + 5f ) QueueFree();
	}
}

/// <summary> One ring of the whale song: a pale ring of sound rushing out from its mouth, growing and fading. </summary>
public partial class SongWave : Node3D
{
	Vector3 dir;
	float age, delay;
	MeshInstance3D ring;
	StandardMaterial3D paint;

	public static void Send( Node parent, Vector3 at, Vector3 dir, float delay )
	{
		var w = new SongWave { Name = "Song Wave", dir = dir, delay = delay };
		parent.AddChild( w );
		w.GlobalTransform = new Transform3D( Shapes.LookAt( dir ) * new Basis( Vector3.Right, MathF.PI / 2f ), at );
	}

	public override void _Ready()
	{
		paint = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color( 0.5f, 0.85f, 1f, 0.5f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		paint.SetMeta( "no_caustics", true );
		ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.85f, OuterRadius = 1f, Rings = 48, RingSegments = 6 }, MaterialOverride = paint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
		AddChild( ring );
	}

	public override void _Process( double delta )
	{
		age += (float)delta;
		var k = (age - delay) / 0.9f;
		if ( k < 0f ) return;
		if ( k >= 1f ) { QueueFree(); return; }
		ring.Visible = true;
		ring.Scale = Vector3.One * (1.5f + k * 14f);
		ring.Position = Vector3.Up * k * 26f;   // the ring's own up is along the song
		paint.AlbedoColor = new Color( 0.5f, 0.85f, 1f, 0.6f * (1f - k) );
	}
}
