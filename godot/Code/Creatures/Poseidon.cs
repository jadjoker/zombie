using Godot;

/// <summary>
/// Poseidon, the wave 30 boss (playtest): a giant merman of the alien sea, after the classic painting of him. Bare
/// muscled torso, white hair and beard, a long scaled mermaid's tail, black armour and crown set with purple, green
/// and yellow jewels, and a trident like the player's (only far bigger). He floats, swimming with his tail, circling the
/// player at a distance; every so often he sinks into a whirlpool and comes up out of another one close to the player
/// (never outside the map or the globe). His attacks:
/// - throws tridents that stream the same bubbles as the player's trident shooter;
/// - throws a whirlpool along the floor toward the player: too close and it drags them in and carries them to him,
///   and he stabs them with his trident;
/// - summons seahorses (they join the wave).
/// Lots of health. The head is the weak spot. Built in code (Sculpt), metres, facing -Z; drawn at <see cref="Size"/>.
/// </summary>
public partial class Poseidon : Creature
{
	public static readonly List<Poseidon> All = new();

	public float Size = 1.5f;              // times the built model (about 5.5 m tall, so over 8 m)
	public float HoverHeight = 4.5f;       // his waist over the floor, in metres at Size
	public float KeepAway = 15f;           // circles the player at about this distance
	public float SwimSpeed = 5f;
	public float TridentDamage = 18f;
	public float TridentSpeed = 24f;
	public float StabDamage = 30f;
	public int MaxSeahorses = 6;
	public float SeahorseHealth = 30f;
	public float HeadMultiplier = 2f;
	public float TailMultiplier = 0.6f;
	/// <summary> Tests: he only attacks or vanishes when told (ForceAttack). </summary>
	public bool OnlyWhenTold;
	/// <summary> How many times his whirlpool has brought someone to him and he stabbed them. </summary>
	public int Stabs { get; private set; }
	/// <summary> How many seahorses he has called up. </summary>
	public int Summoned { get; private set; }

	static readonly Color Skin = new( 0.86f, 0.68f, 0.5f );
	static readonly Color Hair = new( 0.94f, 0.94f, 0.92f );
	static readonly Color Scales = new( 0.1f, 0.56f, 0.5f );
	static readonly Color Armour = new( 1f, 0.76f, 0.26f );   // polished gold
	static readonly Color JewelPurple = new( 0.7f, 0.22f, 1f );
	static readonly Color JewelGreen = new( 0.35f, 1f, 0.3f );
	static readonly Color JewelYellow = new( 1f, 0.82f, 0.15f );
	static readonly Color GooColor = new( 0.3f, 0.95f, 0.85f );

	enum Mode { Swim, WindUp, Throw, Summon, Vanish, Appear, Stab, Dead }

	Node3D body, chest, head, armR, foreR, armL, foreL, hand, heldTrident;
	readonly List<Node3D> tail = new();
	ShaderMaterial skin, handSkin;
	StandardMaterial3D scales;
	/// <summary> Where the held trident points (eased toward what he is doing). </summary>
	Vector3 tridentPoint = Vector3.Up;

	/// <summary>
	/// His skin: the alien sea's purple, blue and green blended in slow drifting patches with faint glowing veins, in
	/// world space round his middle so every part of him (face, chest, arms) shares one pattern. Hits flash it.
	/// </summary>
	public static readonly Shader SkinShader = new()
	{
		Code = @"shader_type spatial;
// His skin drifts like the alien sea: broad slow patches of purple, blue and green blending into each other, with
// faint glowing veins. Measured from his own middle (centre), so the pattern stays on him as he swims.
uniform vec3 purple : source_color = vec3(0.56, 0.26, 0.8);
uniform vec3 blue : source_color = vec3(0.22, 0.42, 0.88);
uniform vec3 green : source_color = vec3(0.32, 0.78, 0.36);
uniform vec3 centre = vec3(0.0);
// Hands: w > 0.5 pins the pattern to one spot (xyz, from his middle), so they are the colour of his chest.
uniform vec4 pin = vec4(0.0);
uniform float flash = 0.0;
varying vec3 world_pos;
float hash(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise(vec3 x) {
	vec3 i = floor(x); vec3 f = fract(x); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}
void vertex() { world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
	vec3 p = (pin.w > 0.5 ? pin.xyz : world_pos - centre) * 0.7;
	float t = TIME * 0.18;
	float u = noise(p + vec3(t, -t * 0.7, t * 0.4));
	float v = noise(p * 0.8 - vec3(t * 0.6, t * 0.5, -t * 0.3) + vec3(7.3, 2.1, 4.4));
	vec3 c = mix(purple, blue, smoothstep(0.25, 0.75, u));
	c = mix(c, green, smoothstep(0.4, 0.8, v));
	float vein = pow(1.0 - abs(noise(p * 2.4 + vec3(0.0, t * 2.0, 0.0)) * 2.0 - 1.0), 12.0);
	vec3 shown = mix(c, vec3(2.2), flash);
	ALBEDO = shown;
	ROUGHNESS = 0.5;
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	EMISSION = shown * rim * 0.2 + mix(green, purple, u) * vein * 0.45;
}",
	};
	/// <summary>
	/// A tail scale: a small six-sided plate in one colour with darker lines cut into it: its rim, a hexagon inside
	/// (its size from the scale's own number) and two or three grooves through the middle at the scale's own angles.
	/// </summary>
	static readonly Shader ScaleShader = new()
	{
		Code = @"shader_type spatial;
render_mode cull_disabled;
uniform vec3 colour : source_color = vec3(0.1, 0.56, 0.5);
uniform float metal = 0.3;
uniform float glow = 0.0;
varying vec2 q;
varying float seed;
float hex(vec2 v) { return max(abs(v.x), max(abs(0.5 * v.x + 0.866 * v.y), abs(0.5 * v.x - 0.866 * v.y))); }
void vertex() { q = UV; seed = COLOR.r; }
void fragment() {
	float d = hex(q);
	float inner = 0.16 + fract(seed * 7.31) * 0.14;
	float line = smoothstep(0.37, 0.41, d);
	line = max(line, 1.0 - smoothstep(0.0, 0.022, abs(d - inner)));
	for (int i = 0; i < 3; i++) {
		float a = fract(seed * (3.17 + float(i) * 2.3)) * 3.14159;
		vec2 n = vec2(cos(a), sin(a));
		float groove = 1.0 - smoothstep(0.0, 0.018, abs(dot(q, n)));
		if (i < 2 || fract(seed * 11.7) > 0.5) line = max(line, groove * step(inner, d));
	}
	vec3 c = mix(colour, colour * 0.28, line);
	ALBEDO = c;
	METALLIC = metal;
	ROUGHNESS = 0.3;
	EMISSION = c * glow;
}",
	};

	/// <summary> A tail scale material in a colour (see ScaleShader). </summary>
	public static ShaderMaterial ScaleMaterial( Color c, float metal = 0.3f, float glow = 0f )
	{
		var m = new ShaderMaterial { Shader = ScaleShader };
		m.SetShaderParameter( "colour", c );
		m.SetShaderParameter( "metal", metal );
		m.SetShaderParameter( "glow", glow );
		return m;
	}

	/// <summary> The arm's resting pose: upper arm out from his side, forearm forward, the trident standing in his fist like a staff. </summary>
	static readonly Vector3 RestArmR = new( 0.1f, 0f, 0.7f ), RestForeR = new( 1.35f, 0f, 0f );

	Mode mode = Mode.Swim;
	TimeSince inMode;
	TimeUntil nextAttack = 5f;
	TimeUntil nextVanish = 14f;
	Action afterWindUp;
	Vector3 velocity;
	float orbit;
	float orbitSign = 1f;
	TimeSince sinceHit = 99f;
	TimeSince sinceFloor = 99f;
	float floorY;
	Vector3 appearAt;
	/// <summary> How much of him is there (shrinks into a whirlpool and grows out of one). On the body node: facing
	/// resets the creature node's own scale. </summary>
	float shown = 1f;
	PlayerSurvivor grabbed;
	Vector3 grabFrom;
	bool stabbed;
	PoseidonVortex grabbedIn;
	float carryTime = 1f;
	bool burst;
	/// <summary> How far his tail is curled up behind him (0 to 1), so it never goes into the floor. </summary>
	float curl;

	public static Poseidon Spawn( Node parent, Vector3 position, float maxHealth ) =>
		Place( new Poseidon { Name = "Poseidon" }, parent, position, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	public float HealthFraction => health?.Fraction ?? 0f;

	protected override void Setup()
	{
		BeginSetup( 18000f );
		orbit = Rng.Float( 0f, MathF.Tau );
		orbitSign = Rng.Float() < 0.5f ? -1f : 1f;
		floorY = Sculpt.FloorBelow( this, GlobalPosition, 80f );
		Build();
		// He arrives out of a whirlpool.
		shown = 0.05f;
		appearAt = GlobalPosition;
		SetMode( Mode.Appear );
		Sfx.Play2D( "boss_spawn", 0.9f );
	}

	protected override void Cleanup() => Release();

	// ---- The body ----

	/// <summary> His materials: the boss's own colours, or all gold for his statue in Atlantis. </summary>
	public sealed class Look
	{
		public Material Skin, Hands, Vein, Scales, Hair, Armour, Purple, Green, Yellow, Fins, EyeBlack, Pupil, Dark;
		public Material[] ScaleColours;
	}

	/// <summary> The joints of his body, to pose and animate. </summary>
	public sealed class Rig
	{
		public Node3D Body, Chest, Head, ArmR, ForeR, ArmL, ForeL, Hand, Trident;
		public readonly List<Node3D> Tail = new();
	}

	void Build()
	{
		skin = new ShaderMaterial { Shader = SkinShader };
		handSkin = new ShaderMaterial { Shader = SkinShader };
		handSkin.SetShaderParameter( "pin", new Vector4( 0f, 1.5f, -0.5f, 1f ) );   // the middle of his chest
		scales = Sculpt.Paint( Scales, 0.35f, 0.2f );
		var armourGold = Sculpt.Paint( Armour, 0.14f, 0.95f );
		armourGold.EmissionEnabled = true;
		armourGold.Emission = Armour;
		armourGold.EmissionEnergyMultiplier = 0.12f;
		var look = new Look
		{
			Skin = skin, Hands = handSkin, Vein = Sculpt.Glow( new Color( 0.2f, 0.95f, 0.7f ), 0.9f ), Scales = scales, Hair = Sculpt.Paint( Hair, 0.7f ), Armour = armourGold,
			Purple = Sculpt.Glow( JewelPurple, 2.4f ), Green = Sculpt.Glow( JewelGreen, 2.4f ), Yellow = Sculpt.Glow( JewelYellow, 2.2f ),
			Fins = Sculpt.Membrane( new Color( 0.2f, 0.75f, 0.65f ), 0.55f ),
			EyeBlack = Sculpt.Paint( new Color( 0.02f, 0.02f, 0.025f ), 0.15f ), Pupil = Sculpt.Glow( new Color( 0.35f, 1f, 0.3f ), 3.2f ),
			Dark = Sculpt.Paint( new Color( 0.12f, 0.03f, 0.08f ), 0.6f ),
			ScaleColours = new Material[] { ScaleMaterial( new Color( 1f, 0.8f, 0.15f ) ), ScaleMaterial( JewelPurple ), ScaleMaterial( new Color( 0.3f, 0.85f, 0.3f ) ), ScaleMaterial( Scales ) },
		};
		var rig = BuildRig( this, look );
		body = rig.Body;
		chest = rig.Chest;
		head = rig.Head;
		armR = rig.ArmR;
		foreR = rig.ForeR;
		armL = rig.ArmL;
		foreL = rig.ForeL;
		hand = rig.Hand;
		heldTrident = rig.Trident;
		tail.AddRange( rig.Tail );

		// What can be shot: the head (index 0), chest, belly, and the top of the tail.
		void Hit( Node3D on, int index, Vector3 at, float radius )
		{
			var part = CreaturePart.Create( this, index, $"Poseidon {index}", radius );
			on.AddChild( part );
			part.Position = at;
		}
		Hit( head, 0, new Vector3( 0f, 0.25f, 0f ), 0.36f );
		Hit( chest, 1, new Vector3( 0f, 0.75f, 0f ), 0.68f );
		Hit( chest, 2, new Vector3( 0f, 0.15f, 0f ), 0.5f );
		for ( var i = 0; i < 4; i++ ) Hit( tail[i], 3 + i, new Vector3( 0f, -0.2f, 0f ), MathX.Lerp( 0.42f, 0.3f, i / 3f ) );
	}


	/// <summary>
	/// His body, under a node: the scaled tail, the belt, the bare-chested torso with armour down the sides and back,
	/// the angry crowned head, the armoured arms and the trident in his right hand. Metres, facing -Z, the waist at the
	/// node's origin. Placing the trident is left to the caller.
	/// </summary>
	public static Rig BuildRig( Node3D into, Look look )
	{
		var skin = look.Skin;
		var hands = look.Hands ?? look.Skin;
		var vein = look.Vein ?? look.Skin;
		var scales = look.Scales;
		var hair = look.Hair;
		var armour = look.Armour;
		var purple = look.Purple;
		var green = look.Green;
		var yellow = look.Yellow;
		var fins = look.Fins;
		var eyeBlack = look.EyeBlack;
		var pupil = look.Pupil;
		var dark = look.Dark;
		var scaleColours = look.ScaleColours;
		var rig = new Rig();
		var body = new Node3D { Name = "Body" };
		into.AddChild( body );

		BuildTail( body, look, rig.Tail );

		// Belt: black with a big yellow jewel and purple and green ones round it.
		Sculpt.Blob( body, new Vector3( 0f, 0f, 0f ), new Vector3( 1.05f, 0.32f, 0.82f ), armour );
		Sculpt.Blob( body, new Vector3( 0f, 0.0f, -0.42f ), new Vector3( 0.26f, 0.3f, 0.12f ), yellow );
		foreach ( var s in new[] { -1f, 1f } )
		{
			Sculpt.Blob( body, new Vector3( s * 0.32f, 0.02f, -0.34f ), Vector3.One * 0.12f, purple );
			Sculpt.Blob( body, new Vector3( s * 0.5f, 0.02f, -0.12f ), Vector3.One * 0.1f, green );
		}

		// Veins: thin raised cords wandering over a muscle (an ellipsoid at `centre` with half-sizes `half`), from one
		// direction on it to another.
		void Veins( Node3D on, Vector3 centre, Vector3 half, Vector3 from, Vector3 to, float wander, int seed )
		{
			Vector3 P( float t )
			{
				var d = from.Lerp( to, t ) + new Vector3( MathF.Sin( t * 9f + seed ), MathF.Sin( t * 7f + seed * 1.7f ), 0f ) * wander * MathF.Sin( t * MathF.PI );
				d = d.Normalized();
				return centre + new Vector3( d.X * half.X, d.Y * half.Y, d.Z * half.Z ) * 1.015f;
			}
			const int steps = 7;
			for ( var n = 0; n < steps; n++ )
				Sculpt.Limb( on, P( n / (float)steps ), P( (n + 1) / (float)steps ), 0.02f, 0.017f, vein, "Vein" );
		}

		// The torso: a narrow waist under a huge chest (he is built like a bodybuilder).
		var chest = Sculpt.Joint( body, new Vector3( 0f, 0.2f, 0f ), "Chest" );
		Sculpt.Blob( chest, new Vector3( 0f, 0.2f, 0f ), new Vector3( 0.88f, 0.75f, 0.62f ), skin );
		Sculpt.Blob( chest, new Vector3( 0f, 0.78f, 0f ), new Vector3( 1.55f, 0.95f, 0.84f ), skin );
		Sculpt.Limb( chest, new Vector3( 0f, 1.1f, 0f ), new Vector3( 0f, 1.45f, -0.01f ), 0.25f, 0.2f, skin, "Neck" );
		foreach ( var s in new[] { -1f, 1f } )
		{
			var pec = new Vector3( s * 0.31f, 0.86f, -0.3f );
			var pecHalf = new Vector3( 0.33f, 0.25f, 0.19f );
			Sculpt.Blob( chest, pec, pecHalf * 2f, skin );   // pecs
			Sculpt.Blob( chest, pec + new Vector3( s * 0.02f, -0.08f, -0.186f ), new Vector3( 0.07f, 0.07f, 0.03f ), dark );   // nipples
			Veins( chest, pec, pecHalf, new Vector3( s * 0.8f, 0.7f, -0.5f ), new Vector3( -s * 0.2f, -0.1f, -1f ), 0.25f, 1 );
			Veins( chest, pec, pecHalf, new Vector3( s * 0.5f, 0.85f, -0.4f ), new Vector3( s * 0.55f, -0.3f, -0.8f ), 0.2f, 4 );
			Sculpt.Blob( chest, new Vector3( s * 0.5f, 1.16f, 0.02f ), new Vector3( 0.6f, 0.36f, 0.5f ), skin );   // traps
			Sculpt.Blob( chest, new Vector3( s * 0.66f, 0.62f, 0.02f ), new Vector3( 0.4f, 0.8f, 0.6f ), skin );   // lats
			for ( var k = 0; k < 3; k++ )   // abs
				Sculpt.Blob( chest, new Vector3( s * 0.13f, 0.44f - k * 0.17f, -0.3f + k * 0.02f ), new Vector3( 0.23f, 0.15f, 0.12f ), skin );
			Sculpt.Blob( chest, new Vector3( s * 0.36f, 0.3f, -0.2f ), new Vector3( 0.2f, 0.42f, 0.2f ), skin, new Basis( Vector3.Back, s * 0.35f ) );   // obliques
			// Veins up the neck and down the belly.
			Sculpt.Limb( chest, new Vector3( s * 0.14f, 1.13f, -0.2f ), new Vector3( s * 0.17f, 1.3f, -0.16f ), 0.02f, 0.018f, vein, "Vein" );
			Sculpt.Limb( chest, new Vector3( s * 0.17f, 1.3f, -0.16f ), new Vector3( s * 0.13f, 1.43f, -0.15f ), 0.018f, 0.016f, vein, "Vein" );
			Sculpt.Limb( chest, new Vector3( s * 0.27f, 0.42f, -0.27f ), new Vector3( s * 0.3f, 0.2f, -0.27f ), 0.018f, 0.018f, vein, "Vein" );
			Sculpt.Limb( chest, new Vector3( s * 0.3f, 0.2f, -0.27f ), new Vector3( s * 0.24f, 0.0f, -0.3f ), 0.018f, 0.016f, vein, "Vein" );
			// Gold plates down his sides.
			Sculpt.Blob( chest, new Vector3( s * 0.72f, 0.58f, 0.08f ), new Vector3( 0.32f, 0.95f, 0.62f ), armour );
			Sculpt.Blob( chest, new Vector3( s * 0.43f, 0.12f, 0.08f ), new Vector3( 0.3f, 0.55f, 0.5f ), armour );
		}
		// The back plate, with spikes like the shoulders' and a purple jewel.
		Sculpt.Blob( chest, new Vector3( 0f, 0.64f, 0.27f ), new Vector3( 1.35f, 1.15f, 0.46f ), armour );
		Sculpt.Blob( chest, new Vector3( 0f, 0.16f, 0.2f ), new Vector3( 0.82f, 0.6f, 0.36f ), armour );
		Sculpt.Blob( chest, new Vector3( 0f, 0.62f, 0.51f ), Vector3.One * 0.13f, purple );
		foreach ( var (x, y) in new[] { (-0.36f, 0.94f), (0f, 1f), (0.36f, 0.94f), (-0.22f, 0.45f), (0.22f, 0.45f) } )
			Sculpt.Cone( chest, new Vector3( x, y, 0.44f ), new Vector3( x * 1.4f, y + 0.25f, 0.9f ), 0.075f, armour );

		// Shoulder armour: gold spiked pauldrons with jewels, joined to a high collar round the back of his neck
		// by a plate over each trapezius (the throat and the chest stay bare).
		foreach ( var s in new[] { -1f, 1f } )
		{
			var p = new Vector3( s * 0.86f, 1.08f, 0f );
			Sculpt.Blob( chest, p, new Vector3( 0.7f, 0.5f, 0.7f ), armour );
			Sculpt.Blob( chest, p + new Vector3( s * 0.12f, 0.1f, -0.3f ), Vector3.One * 0.13f, s < 0 ? green : purple );
			for ( var k = 0; k < 3; k++ )
				Sculpt.Cone( chest, p + new Vector3( s * 0.2f, 0.14f, (k - 1) * 0.18f ), p + new Vector3( s * 0.6f, 0.6f, (k - 1) * 0.24f ), 0.07f, armour );
			Sculpt.Blob( chest, new Vector3( s * 0.5f, 1.24f, 0.08f ), new Vector3( 0.62f, 0.3f, 0.5f ), armour, new Basis( Vector3.Back, -s * 0.3f ) );   // over the trapezius
			Sculpt.Block( chest, new Vector3( s * 0.26f, 1.47f, 0.13f ), new Vector3( 0.2f, 0.42f, 0.3f ), armour, new Basis( Vector3.Up, -s * 0.5f ) * new Basis( Vector3.Back, -s * 0.2f ) );   // collar wings
			Sculpt.Blob( chest, new Vector3( s * 0.33f, 1.38f, -0.03f ), Vector3.One * 0.09f, s < 0 ? purple : green );
		}
		Sculpt.Block( chest, new Vector3( 0f, 1.45f, 0.24f ), new Vector3( 0.46f, 0.5f, 0.14f ), armour );   // the collar's back
		Sculpt.Limb( chest, new Vector3( 0f, 1.2f, 0.03f ), new Vector3( 0f, 1.3f, 0.03f ), 0.33f, 0.3f, armour, "Collar" );
		Sculpt.Blob( chest, new Vector3( 0f, 1.5f, 0.32f ), Vector3.One * 0.11f, yellow );

		// Head: white hair and beard, glowing eyes, the black crown with its jewels and spikes.
		var head = Sculpt.Joint( chest, new Vector3( 0f, 1.4f, -0.02f ), "Head" );
		Sculpt.Blob( head, new Vector3( 0f, 0.22f, 0f ), new Vector3( 0.5f, 0.58f, 0.52f ), skin );
		Sculpt.Blob( head, new Vector3( 0f, 0.3f, 0.12f ), new Vector3( 0.6f, 0.66f, 0.5f ), hair );                              // hair
		Sculpt.Cone( head, new Vector3( 0f, 0.1f, -0.12f ), new Vector3( 0f, -0.36f, -0.08f ), 0.2f, hair, 10 );                  // beard, tucked under the chin
		Sculpt.Blob( head, new Vector3( 0f, 0.16f, -0.24f ), new Vector3( 0.32f, 0.1f, 0.12f ), hair );                           // moustache
		foreach ( var s in new[] { -1f, 1f } )
		{
			// Alien eyes: big black almonds slanted up at the outer corners, a green pupil in each, under a heavy brow
			// that drops toward the nose (angry).
			var slant = new Basis( Vector3.Back, -s * 0.5f );
			Sculpt.Blob( head, new Vector3( s * 0.125f, 0.285f, -0.225f ), new Vector3( 0.19f, 0.105f, 0.07f ), eyeBlack, slant );
			Sculpt.Blob( head, new Vector3( s * 0.115f, 0.28f, -0.258f ), new Vector3( 0.05f, 0.05f, 0.02f ), pupil );
			Sculpt.Block( head, new Vector3( s * 0.12f, 0.375f, -0.25f ), new Vector3( 0.19f, 0.045f, 0.06f ), hair, new Basis( Vector3.Back, s * 0.5f ) );
			// A frown: the corners of the mouth pulled down under the moustache.
			Sculpt.Block( head, new Vector3( s * 0.055f, 0.1f, -0.245f ), new Vector3( 0.08f, 0.018f, 0.02f ), dark, new Basis( Vector3.Back, -s * 0.45f ) );
		}
		Sculpt.Block( head, new Vector3( 0f, 0.35f, -0.255f ), new Vector3( 0.025f, 0.06f, 0.02f ), dark );                     // the scowl crease
		Sculpt.Limb( head, new Vector3( 0f, 0.44f, 0f ), new Vector3( 0f, 0.62f, 0f ), 0.3f, 0.32f, armour );                    // crown band
		Sculpt.Blob( head, new Vector3( 0f, 0.53f, -0.3f ), new Vector3( 0.15f, 0.18f, 0.08f ), yellow );
		for ( var k = 0; k < 7; k++ )
		{
			var a = MathX.Rad( -90f + (k - 3) * 30f );
			var foot = new Vector3( MathF.Cos( a ) * 0.3f, 0.6f, MathF.Sin( a ) * 0.3f );
			var tall = k == 3 ? 0.55f : 0.32f + (k % 2) * 0.08f;
			Sculpt.Cone( head, foot, foot + new Vector3( MathF.Cos( a ) * 0.08f, tall, MathF.Sin( a ) * 0.08f ), 0.07f, armour );
			if ( k != 3 ) Sculpt.Blob( head, foot + new Vector3( MathF.Cos( a ) * 0.02f, -0.06f, MathF.Sin( a ) * 0.02f ), Vector3.One * 0.07f, k % 3 == 0 ? green : k % 3 == 1 ? purple : yellow );
		}

		// Arms: shoulder and elbow joints to pose, black bracers with jewels, the trident in the right hand.
		(Node3D Arm, Node3D Fore, Node3D Hand) Arm( float s, Material bracerJewel )
		{
			var arm = Sculpt.Joint( chest, new Vector3( s * 0.92f, 1.02f, 0f ), s > 0 ? "Right Arm" : "Left Arm" );
			Sculpt.Limb( arm, Vector3.Zero, new Vector3( 0f, -0.68f, 0f ), 0.24f, 0.2f, skin );
			var biceps = new Vector3( 0f, -0.3f, -0.06f );
			var bicepsHalf = new Vector3( 0.27f, 0.29f, 0.27f );
			Sculpt.Blob( arm, biceps, bicepsHalf * 2f, skin );
			Sculpt.Blob( arm, new Vector3( 0f, -0.36f, 0.12f ), new Vector3( 0.4f, 0.5f, 0.34f ), skin );   // triceps
			Veins( arm, biceps, bicepsHalf, new Vector3( s * 0.3f, 0.9f, -0.6f ), new Vector3( -s * 0.2f, -0.8f, -0.8f ), 0.3f, 2 );
			Veins( arm, biceps, bicepsHalf, new Vector3( s * 0.9f, 0.6f, -0.2f ), new Vector3( s * 0.6f, -0.8f, -0.5f ), 0.25f, 5 );
			// Gold bands round the arm (the muscle bulges out between them), an elbow cop with a spike.
			Sculpt.Limb( arm, new Vector3( 0f, 0.0f, 0f ), new Vector3( 0f, -0.1f, 0f ), 0.29f, 0.28f, armour );
			Sculpt.Limb( arm, new Vector3( 0f, -0.54f, 0f ), new Vector3( 0f, -0.62f, 0f ), 0.26f, 0.25f, armour );
			Sculpt.Blob( arm, new Vector3( 0f, -0.66f, 0f ), Vector3.One * 0.4f, armour );                        // elbow cop
			Sculpt.Cone( arm, new Vector3( 0f, -0.68f, 0.16f ), new Vector3( 0f, -0.72f, 0.46f ), 0.06f, armour );   // elbow spike
			var fore = Sculpt.Joint( arm, new Vector3( 0f, -0.68f, 0f ), "Elbow" );
			Sculpt.Limb( fore, Vector3.Zero, new Vector3( 0f, -0.62f, 0f ), 0.2f, 0.14f, skin );
			Sculpt.Blob( fore, new Vector3( 0f, -0.2f, 0f ), new Vector3( 0.4f, 0.42f, 0.38f ), skin );   // the forearm's bulge
			Veins( fore, new Vector3( 0f, -0.2f, 0f ), new Vector3( 0.2f, 0.21f, 0.19f ), new Vector3( s * 0.5f, 0.8f, -0.5f ), new Vector3( s * 0.3f, -0.9f, -0.6f ), 0.3f, 3 );
			Sculpt.Limb( fore, new Vector3( 0f, -0.36f, 0f ), new Vector3( 0f, -0.62f, 0f ), 0.2f, 0.17f, armour );   // bracer
			Sculpt.Blob( fore, new Vector3( 0f, -0.48f, -0.17f ), Vector3.One * 0.1f, bracerJewel );
			var h = Sculpt.Joint( fore, new Vector3( 0f, -0.72f, 0f ), "Hand" );
			Sculpt.Blob( h, Vector3.Zero, new Vector3( 0.26f, 0.28f, 0.24f ), hands );
			return (arm, fore, h);
		}
		var (armR, foreR, hand) = Arm( 1f, green );
		var (armL, foreL, _) = Arm( -1f, purple );
		var heldTrident = new Node3D { Name = "Trident" };
		hand.AddChild( heldTrident );
		// Placed each frame in world space (Animate): upright in his fist like a staff, whatever the arm does.
		TridentModel.BuildTrident( heldTrident );
		rig.Body = body;
		rig.Chest = chest;
		rig.Head = head;
		rig.ArmR = armR;
		rig.ForeR = foreR;
		rig.ArmL = armL;
		rig.ForeL = foreL;
		rig.Hand = hand;
		rig.Trident = heldTrident;
		return rig;
	}

	/// <summary>
	/// A merfolk tail under `body`: nine joints from the hips down, each a scaled blob with a band and four rings of
	/// hexagon scales, a wide fluke and hip fins, in Poseidon's model units (the waist at the origin, about 3.9 long).
	/// The joints go into `tail`, hips first. Shared with the mermaids.
	/// </summary>
	public static void BuildTail( Node3D body, Look look, List<Node3D> tail )
	{
		var scales = look.Scales;
		var armour = look.Armour;
		var fins = look.Fins;
		var scaleColours = look.ScaleColours;
		// The tail: a chain of joints from the hips down, each a scaled blob with a darker band, ending in a wide fluke.
		Node3D parent = body;
		var at = new Vector3( 0f, -0.15f, 0f );
		for ( var i = 0; i < 9; i++ )
		{
			var joint = Sculpt.Joint( parent, at, $"Tail {i}" );
			var r = MathX.Lerp( 0.44f, 0.12f, i / 8f );
			Sculpt.Blob( joint, new Vector3( 0f, -0.2f, 0f ), new Vector3( r * 2f, 0.5f, r * 1.8f ), scales );
			Sculpt.Blob( joint, new Vector3( 0f, -0.05f, 0f ), new Vector3( r * 2.05f, 0.08f, r * 1.85f ), armour );
			// Scales: four rings of sixteen small six-sided plates, laid like tiles.
			var plates = new List<(Transform3D At, float Seed)>[scaleColours.Length];
			for ( var c = 0; c < plates.Length; c++ ) plates[c] = new();
			for ( var row = 0; row < 4; row++ )
				for ( var k = 0; k < 16; k++ )
				{
					var a = (k + row * 0.5f) / 16f * MathF.Tau;
					var y = -0.03f - row * 0.115f;
					var off = (y + 0.2f) / 0.25f;
					var squeeze = MathF.Sqrt( MathF.Max( 0.2f, 1f - off * off ) );   // the blob narrows toward its ends
					var out3 = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
					var at2 = new Vector3( out3.X * r * squeeze, y, out3.Z * r * 0.9f * squeeze );
					var normal = (out3 + Vector3.Up * 0.35f).Normalized();   // lower edge lifted, like roof tiles
					var across = Vector3.Up.Cross( out3 ).Normalized();
					var down = across.Cross( normal );
					var size = r * 0.46f;
					var colour = (i * 3 + k + row * 5 + (k * 7 + row * 3 + i) % 3) % scaleColours.Length;
					plates[colour].Add( (new Transform3D( new Basis( across * size, normal * 0.03f, down * size * 1.15f ), at2 + normal * 0.012f ), (i * 64 + row * 16 + k) * 0.6180339f % 1f) );
				}
			// Baked into one mesh per colour per section (multimeshes corrupted memory in the test runs): each plate a
			// flat hexagon, its pattern coordinates in the UVs and its own number in the vertex colour.
			for ( var c = 0; c < plates.Length; c++ )
			{
				if ( plates[c].Count == 0 ) continue;
				var st = new SurfaceTool();
				st.Begin( Mesh.PrimitiveType.Triangles );
				foreach ( var (plateAt, seed) in plates[c] )
				{
					var normal = plateAt.Basis.Y.Normalized();
					var colourSeed = new Color( seed, 0f, 0f, 1f );
					void Corner( float angle )
					{
						var local = angle < 0f ? Vector2.Zero : new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * 0.5f;
						st.SetNormal( normal );
						st.SetColor( colourSeed );
						st.SetUV( local );
						st.AddVertex( plateAt * new Vector3( local.X, 0.5f, local.Y ) );
					}
					for ( var k = 0; k < 6; k++ )
					{
						Corner( -1f );
						Corner( k / 6f * MathF.Tau );
						Corner( (k + 1) / 6f * MathF.Tau );
					}
				}
				joint.AddChild( new MeshInstance3D { Name = "Scales", Mesh = st.Commit(), MaterialOverride = scaleColours[c] } );
			}
			tail.Add( joint );
			parent = joint;
			at = new Vector3( 0f, -0.36f, 0f );
		}
		foreach ( var s in new[] { -1f, 1f } )   // the fluke
			Sculpt.Block( parent, new Vector3( s * 0.32f, -0.42f, 0.08f ), new Vector3( 0.7f, 0.03f, 0.45f ), fins, new Basis( Vector3.Back, s * 0.5f ) * new Basis( Vector3.Right, 0.35f ) );
		foreach ( var s in new[] { -1f, 1f } )   // hip fins
			Sculpt.Block( tail[1], new Vector3( s * 0.45f, -0.15f, 0.05f ), new Vector3( 0.55f, 0.03f, 0.3f ), fins, new Basis( Vector3.Back, s * 0.7f ) );
	}

	/// <summary>
	/// The statue of him that stands on Atlantis's island: his own body all in gold (the jewels still glowing), standing
	/// on the tip of his tail with the trident upright like a staff and his fist out, facing `facing`. `feet` is where
	/// the tail's tip rests; `size` scales the whole statue (1 is the boss's model before his own Size). `marble` carves him in
	/// white stone instead.
	/// </summary>
	public static Node3D BuildStatue( Node3D parent, Vector3 feet, float size, Vector3 facing, bool marble = false )
	{
		var gold = marble ? Sculpt.Paint( new Color( 0.93f, 0.88f, 0.78f ), 0.5f ) : Sculpt.Paint( new Color( 1f, 0.78f, 0.32f ), 0.22f, 0.85f );
		var darkGold = marble ? Sculpt.Paint( new Color( 0.7f, 0.62f, 0.55f ), 0.6f ) : Sculpt.Paint( new Color( 0.78f, 0.52f, 0.16f ), 0.3f, 0.85f );
		if ( !marble )   // a little lit, so the gold shines in the murk
			foreach ( var m in new[] { gold, darkGold } ) { m.EmissionEnabled = true; m.Emission = m.AlbedoColor; m.EmissionEnergyMultiplier = 0.25f; }
		var look = new Look
		{
			Skin = gold, Hands = gold, Vein = darkGold, Scales = gold, Hair = gold, Armour = darkGold, Fins = darkGold, EyeBlack = darkGold, Dark = darkGold,
			Purple = Sculpt.Glow( JewelPurple, 2.4f ), Green = Sculpt.Glow( JewelGreen, 2.4f ), Yellow = Sculpt.Glow( JewelYellow, 2.2f ),
			Pupil = Sculpt.Glow( new Color( 0.35f, 1f, 0.3f ), 3.2f ),
			ScaleColours = new Material[] { ScaleMaterial( gold.AlbedoColor, marble ? 0f : 0.85f, marble ? 0f : 0.25f ), ScaleMaterial( darkGold.AlbedoColor, marble ? 0f : 0.85f, marble ? 0f : 0.25f ) },
		};
		var root = new Node3D { Name = "Statue of Poseidon" };
		parent.AddChild( root );
		var turn = Shapes.LookAt( facing.Flat().SafeNormal() is var f && f != Vector3.Zero ? f : Vector3.Forward );
		// The tail hangs about 3.5 model metres below his waist.
		root.GlobalTransform = new Transform3D( turn.Scaled( Vector3.One * size ), feet + Vector3.Up * 3.5f * size );
		var rig = BuildRig( root, look );
		rig.ArmR.Rotation = RestArmR;
		rig.ForeR.Rotation = RestForeR;
		rig.ArmL.Rotation = new Vector3( 0.2f, 0f, -0.5f );
		rig.ForeL.Rotation = new Vector3( 0.9f, 0f, 0f );
		for ( var i = 0; i < rig.Tail.Count; i++ )
			rig.Tail[i].Rotation = new Vector3( MathF.Sin( 1.2f - i * 0.55f ) * (0.1f + i * 0.02f), 0f, 0f );
		var up = Vector3.Up + turn * Vector3.Forward * 0.08f;
		rig.Trident.GlobalTransform = new Transform3D( Shapes.LookAt( up, turn * Vector3.Back ).Scaled( Vector3.One * 3.2f * size ), rig.Hand.GlobalPosition + turn * Vector3.Forward * 0.14f * size );
		return root;
	}

	// ---- Thinking ----

	void SetMode( Mode m )
	{
		mode = m;
		inMode = 0f;
	}

	PlayerSurvivor Target => PlayerSurvivor.FindNearestAlive( GlobalPosition );

	public override void _Process( double delta )
	{
		if ( health is null || body is null ) return;
		var dt = (float)delta;
		var t = (float)Clock.Now;
		Animate( t, dt );
		if ( mode == Mode.Dead ) { Sink( dt ); return; }
		if ( DevFlags.FreezeEnemies && mode is not Mode.Appear and not Mode.Vanish ) return;
		if ( sinceFloor > 0.3f ) { sinceFloor = 0f; floorY = Sculpt.FloorBelow( this, GlobalPosition, 80f ); }

		var prey = Target;
		switch ( mode )
		{
			case Mode.Swim:
				Swim( prey, dt );
				if ( prey is null ) break;
				if ( OnlyWhenTold ) break;
				if ( nextVanish <= 0f || prey.WorldPosition.DistanceTo( GlobalPosition ) > 40f ) { Vanish(); break; }
				if ( nextAttack <= 0f ) ChooseAttack();
				break;
			case Mode.WindUp:
			case Mode.Summon:
				Hold( prey, dt );
				if ( inMode >= (mode == Mode.Summon ? 1.1f : 0.65f) )
				{
					var then = afterWindUp;
					afterWindUp = null;
					SetMode( mode == Mode.WindUp ? Mode.Throw : Mode.Swim );
					then?.Invoke();
				}
				break;
			case Mode.Throw:
				Hold( prey, dt );
				if ( inMode >= 0.45f ) SetMode( Mode.Swim );
				break;
			case Mode.Vanish:
				shown = MathX.Lerp( 1f, 0.05f, MathX.Clamp( (float)inMode / 0.8f, 0f, 1f ) );
				RotateY( dt * 9f );
				if ( inMode >= 0.85f )
				{
					GlobalPosition = appearAt;
					SetMode( Mode.Appear );
					PoseidonVortex.Burst( this, appearAt + Vector3.Down * HoverHeight * 0.8f );
				}
				break;
			case Mode.Appear:
				shown = MathX.Lerp( 0.05f, 1f, MathX.Clamp( (float)inMode / 0.8f, 0f, 1f ) );
				RotateY( dt * 9f * (1f - MathX.Clamp( (float)inMode / 0.8f, 0f, 1f )) );
				if ( inMode >= 0.85f ) { shown = 1f; SetMode( Mode.Swim ); nextAttack = Rng.Float( 1.5f, 3f ); }
				break;
			case Mode.Stab:
				Stab( dt );
				break;
		}
	}

	/// <summary> Tests and the dev menu: "trident", "whirlpool", "seahorses" or "vanish", now. </summary>
	public void ForceAttack( string what )
	{
		if ( mode is Mode.Dead or Mode.Stab ) return;
		SetMode( Mode.Swim );
		switch ( what )
		{
			case "trident": WindUp( Mode.WindUp, ThrowTrident ); break;
			case "whirlpool": WindUp( Mode.Summon, ThrowWhirlpool ); break;
			case "seahorses": WindUp( Mode.Summon, SummonSeahorses ); break;
			case "vanish": Vanish(); break;
		}
	}

	void ChooseAttack()
	{
		var roll = Rng.Float();
		var horses = Seahorse.All.Count( s => !s.IsDead );
		if ( roll < 0.18f && horses < MaxSeahorses ) { WindUp( Mode.Summon, SummonSeahorses ); return; }
		if ( roll < 0.42f ) { WindUp( Mode.Summon, ThrowWhirlpool ); return; }
		WindUp( Mode.WindUp, ThrowTrident );
	}

	void WindUp( Mode m, Action then )
	{
		afterWindUp = then;
		SetMode( m );
		nextAttack = Rng.Float( 3.5f, 6f ) * MathX.Lerp( 0.7f, 1f, HealthFraction );   // quicker as he weakens
	}

	/// <summary> Circles the player at a distance, at his hover height over the floor, facing them. </summary>
	void Swim( PlayerSurvivor prey, float dt )
	{
		var wish = Vector3.Zero;
		if ( prey is not null )
		{
			orbit += orbitSign * dt * 0.25f;
			var spot = prey.WorldPosition + new Vector3( MathF.Cos( orbit ), 0f, MathF.Sin( orbit ) ) * KeepAway;
			spot = Sculpt.InBounds( spot );
			spot.Y = floorY + HoverHeight;
			var to = spot - GlobalPosition;
			wish = to.Length() > 0.5f ? to.Normalized() * MathF.Min( SwimSpeed, to.Length() * 1.5f ) : Vector3.Zero;
		}
		velocity = velocity.Lerp( wish, 1f - MathF.Exp( -1.5f * dt ) );
		var next = GlobalPosition + velocity * dt;
		next.Y = MathF.Max( next.Y, floorY + HoverHeight * 0.75f );
		GlobalPosition = Sculpt.InBounds( next );
		if ( prey is not null ) Face( prey.WorldPosition - GlobalPosition, 2.5f, dt );
	}

	void Hold( PlayerSurvivor prey, float dt )
	{
		velocity = velocity.Lerp( Vector3.Zero, 1f - MathF.Exp( -3f * dt ) );
		GlobalPosition += velocity * dt;
		if ( prey is not null ) Face( prey.WorldPosition - GlobalPosition, 4f, dt );
	}

	/// <summary> Sinks into a whirlpool here and comes up out of another close to the player, inside the map. </summary>
	void Vanish()
	{
		var prey = Target;
		if ( prey is null ) return;
		nextVanish = Rng.Float( 12f, 18f );
		var around = prey.WorldPosition;
		var best = GlobalPosition;
		for ( var i = 0; i < 8; i++ )
		{
			var a = Rng.Float( 0f, MathF.Tau );
			var spot = Sculpt.InBounds( around + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * Rng.Float( 9f, 16f ) );
			var ground = Nav.Closest( this, spot );
			if ( ground == Vector3.Zero || ground.Flat().DistanceTo( spot.Flat() ) > 3f ) continue;
			best = Sculpt.InBounds( ground ) with { Y = ground.Y + HoverHeight };
			break;
		}
		appearAt = best;
		SetMode( Mode.Vanish );
		PoseidonVortex.Burst( this, GlobalPosition + Vector3.Down * HoverHeight * 0.8f );
		Sfx.Play( "whirlpool", GlobalPosition, 1f, 0.8f );
	}

	// ---- Attacks ----

	Vector3 HandAt => hand.GlobalPosition;

	void ThrowTrident()
	{
		var prey = Target;
		if ( prey is null ) return;
		var from = HandAt + Vector3.Up * 1.2f;
		var aim = prey.WorldPosition + Vector3.Up * 1.1f;
		// Leads the player a little.
		var flight = from.DistanceTo( aim ) / TridentSpeed;
		aim += prey.Controller.Velocity * flight * 0.6f;
		PoseidonTrident.Throw( this, from, (aim - from).SafeNormal() * TridentSpeed, TridentDamage );
		if ( AutoTest.Requested ) GD.Print( $"[autotest] poseidon throws a trident from {from} at {aim}" );
		heldTrident.Visible = false;   // in flight now; another is in his hand when the swing is over
		GetTree().CreateTimer( 0.9 ).Timeout += () => { if ( IsInstanceValid( heldTrident ) ) heldTrident.Visible = true; };
		Sfx.Play( "harpoon", from, 1f, 0.7f );
	}

	void ThrowWhirlpool()
	{
		var prey = Target;
		if ( prey is null ) return;
		var start = GlobalPosition with { Y = Sculpt.FloorBelow( this, GlobalPosition, 80f ) };
		var toward = (prey.WorldPosition - start).Flat().SafeNormal();
		if ( toward == Vector3.Zero ) toward = -GlobalBasis.Z.Flat().SafeNormal();
		PoseidonVortex.Throw( this, start + toward * 3f, toward );
		Sfx.Play( "whirlpool", GlobalPosition, 0.9f, 1.1f );
	}

	void SummonSeahorses()
	{
		var director = WaveDirector.Current;
		var count = Math.Min( Rng.Int( 3, 4 ), MaxSeahorses - Seahorse.All.Count( s => !s.IsDead ) );
		for ( var i = 0; i < count; i++ )
		{
			var a = i / (float)count * MathF.Tau + Rng.Float( -0.3f, 0.3f );
			var at = Sculpt.InBounds( GlobalPosition + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * 3.5f );
			at.Y = Sculpt.FloorBelow( this, at, 80f ) + 1.6f;
			PoseidonVortex.Burst( this, at + Vector3.Down * 1.2f, 0.4f );
			var horse = Seahorse.Spawn( GetTree().CurrentScene, at, SeahorseHealth );
			Summoned++;
			if ( director is not null ) director.Adopt( horse );
		}
	}

	/// <summary>
	/// A whirlpool caught the player: it sweeps them up and runs back to him with them inside, and he stabs them
	/// when it gets there.
	/// </summary>
	public bool Grab( PlayerSurvivor player, PoseidonVortex carrier = null )
	{
		if ( mode is Mode.Dead or Mode.Vanish or Mode.Appear or Mode.Stab || player?.Controller is null || !player.IsAlive ) return false;
		grabbed = player;
		grabFrom = carrier is not null ? carrier.GlobalPosition : player.Controller.GlobalPosition;
		grabbedIn = carrier;
		stabbed = false;
		// The whirlpool runs at about 13 m/s: further away, a longer ride.
		carryTime = MathX.Clamp( grabFrom.Flat().DistanceTo( GlobalPosition.Flat() ) / 13f, 0.9f, 2.4f );
		player.Controller.Scripted = true;
		player.Controller.Velocity = Vector3.Zero;
		SetMode( Mode.Stab );
		return true;
	}

	void Stab( float dt )
	{
		var c = grabbed?.Controller;
		if ( c is null || !IsInstanceValid( c ) || !grabbed.IsAlive ) { Release(); SetMode( Mode.Swim ); return; }
		// The whirlpool runs along the floor to just in front of him, the player spinning up inside it; there it
		// lifts them to his height for the thrust.
		var front = GlobalPosition + -GlobalBasis.Z.Flat().SafeNormal() * 2.6f * Size / 1.5f + Vector3.Down * 1.2f;
		var floor = Sculpt.FloorBelow( this, front, 80f );
		front = front with { Y = MathF.Max( front.Y, floor + 0.1f ) };
		var carry = MathX.Clamp( (float)inMode / carryTime, 0f, 1f );
		var ease = carry * carry * (3f - 2f * carry);
		var ground = grabFrom.Lerp( front with { Y = floor }, ease );
		ground.Y = MathF.Max( ground.Y, Sculpt.FloorBelow( this, ground + Vector3.Up * 3f, 30f ) );
		if ( grabbedIn is not null && IsInstanceValid( grabbedIn ) ) grabbedIn.GlobalPosition = ground;
		var lift = MathX.Lerp( 1.2f, MathF.Max( 1.2f, front.Y - ground.Y ), ease * ease );
		var spin = (float)inMode * 7f;
		var swirl = new Vector3( MathF.Cos( spin ), 0f, MathF.Sin( spin ) ) * 0.5f * (1f - ease);
		c.GlobalPosition = ground + Vector3.Up * lift + swirl;
		c.Velocity = Vector3.Zero;
		c.ResetPhysicsInterpolation();
		Face( c.GlobalPosition - GlobalPosition, 6f, dt );
		if ( !stabbed && inMode >= carryTime + 0.25f )
		{
			stabbed = true;
			Stabs++;
			grabbed.Health?.OnDamage( new DamageInfo { Damage = StabDamage, Attacker = this, Position = c.GlobalPosition + Vector3.Up * 1.1f, Origin = HandAt } );
			Sfx.Play( "hit_flesh", c.GlobalPosition, 1f, 0.7f );
			c.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 1f );
			var push = (c.GlobalPosition - GlobalPosition).Flat().SafeNormal();
			Release();
			c.Velocity = push * 9f + Vector3.Up * 3f;
			SetMode( Mode.Swim );
			nextAttack = Rng.Float( 2.5f, 4f );
		}
	}

	void Release()
	{
		if ( grabbed?.Controller is { } c && IsInstanceValid( c ) ) c.Scripted = false;
		if ( grabbedIn is not null && IsInstanceValid( grabbedIn ) ) grabbedIn.Finish();
		grabbedIn = null;
		grabbed = null;
	}

	// ---- Looks ----

	void Animate( float t, float dt )
	{
		body.Scale = Vector3.One * Size * MathF.Max( 0.02f, shown );
		var flash = MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f );
		skin.SetShaderParameter( "flash", flash );
		handSkin.SetShaderParameter( "flash", flash );
		skin.SetShaderParameter( "centre", body.GlobalPosition );
		scales.AlbedoColor = Scales.Lerp( Colors.White * 2.2f, flash ) with { A = 1f };
		if ( mode == Mode.Dead ) return;
		// Swimming: the tail sweeps in a wave down its length, the body bobbing and rocking with it.
		var speed = mode == Mode.Swim ? 1f + velocity.Length() * 0.15f : 0.7f;
		for ( var i = 0; i < tail.Count; i++ )
			tail[i].Rotation = new Vector3( MathF.Sin( t * 2.2f * speed - i * 0.55f ) * (0.12f + i * 0.025f) * (1f - curl * 0.6f) + 0.06f - curl * 0.21f, 0f, MathF.Sin( t * 1.1f - i * 0.4f ) * 0.04f );
		// Low over the floor his tail curls up behind him instead of going through it (playtest: it clipped and glitched).
		if ( tail.Count > 0 )
		{
			// From the room the tail would have hanging straight (measuring the curled tip fed back on itself).
			var ground = MathF.Max( floorY, Sculpt.GroundAt( GlobalPosition ) );
			var room = GlobalPosition.Y - 4.2f * Size * MathF.Max( 0.02f, shown ) - ground;
			var want = MathX.Clamp( (0.5f - room) / 1.5f, 0f, 1f );
			curl = MathX.Lerp( curl, want, 1f - MathF.Exp( -4f * dt ) );
		}
		body.Position = new Vector3( 0f, MathF.Sin( t * 1.4f ) * 0.12f, 0f );
		body.Rotation = new Vector3( MathF.Sin( t * 2.2f * speed + 1.2f ) * 0.05f, 0f, MathF.Sin( t * 0.9f ) * 0.04f );

		// Arms (Euler YXZ: Z swings an arm out and up from his side, X forward; elbows bend forward with +X): the trident
		// stood upright in his fist like a staff (upper arm out, forearm forward, so the shaft crosses the forearm and
		// runs down clear of him); drawn back over the shoulder to throw and whipped forward to let go; both arms
		// raised to call up the sea; thrust out in front to stab.
		var raiseR = RestArmR;
		var foreR0 = RestForeR;
		var raiseL = new Vector3( 0.2f, 0f, -0.5f );     // left fist out
		var foreL0 = new Vector3( 0.9f, 0f, 0f );
		var u = MathX.Clamp( (float)inMode / 0.6f, 0f, 1f );
		var point = Vector3.Up + -GlobalBasis.Z * 0.08f;   // the trident: upright, a touch forward
		var prey = Target;
		var aim = prey is not null ? (prey.WorldPosition + Vector3.Up - HandAt).SafeNormal() : -GlobalBasis.Z;
		switch ( mode )
		{
			case Mode.WindUp:
				raiseR = raiseR.Lerp( new Vector3( 0.85f, 0f, 2.75f ), u );   // up and back over the shoulder
				foreR0 = foreR0.Lerp( new Vector3( 1.1f, 0f, 0f ), u );
				point = point.Lerp( (aim + Vector3.Up * 0.35f).SafeNormal(), u );   // levelled at the target like a javelin
				break;
			case Mode.Throw:
				var swing = MathX.Clamp( (float)inMode / 0.15f, 0f, 1f );
				raiseR = new Vector3( 0.85f, 0f, 2.75f ).Lerp( new Vector3( -0.9f, 0f, 2.1f ), swing );   // whipped forward
				foreR0 = new Vector3( 1.1f, 0f, 0f ).Lerp( Vector3.Zero, swing );
				point = aim;
				break;
			case Mode.Summon:
				raiseR = raiseR.Lerp( new Vector3( 0f, 0f, 2.9f ), u );
				raiseL = raiseL.Lerp( new Vector3( 0f, 0f, -2.9f ), u );
				foreL0 = foreL0.Lerp( Vector3.Zero, u );
				break;
			case Mode.Stab:
				var thrust = MathX.Clamp( ((float)inMode - carryTime) / 0.25f, 0f, 1f );
				raiseR = raiseR.Lerp( new Vector3( 1.45f, 0f, 0.15f ), thrust );   // straight out in front (+X about the shoulder swings the arm to -Z)
				foreR0 = foreR0.Lerp( Vector3.Zero, thrust );
				point = point.Lerp( (aim + Vector3.Down * 0.2f).SafeNormal(), MathX.Clamp( (float)inMode / carryTime, 0f, 1f ) );   // points at the victim
				break;
		}
		// The trident in his fist, gripped at the middle of the shaft, swinging smoothly to where it should point.
		tridentPoint = tridentPoint.Slerp( point.SafeNormal(), 1f - MathF.Exp( -12f * dt ) ).SafeNormal();
		if ( tridentPoint == Vector3.Zero ) tridentPoint = Vector3.Up;
		// Never through his own arm: the shaft is long, so when it would lie along the forearm it is leant away from
		// it (the lower end outward), and it sits in his palm, just off the wrist's line.
		var forearm = (HandAt - foreR.GlobalPosition).SafeNormal();
		var held = tridentPoint;
		var along = held.Dot( forearm );
		var side = held - forearm * along;
		var outward = GlobalBasis.X.Normalized() * (along >= 0f ? -1f : 1f) - GlobalBasis.Z.Normalized() * 0.4f;
		outward = (outward - forearm * outward.Dot( forearm )).SafeNormal();
		if ( side.Length() < 0.55f && outward != Vector3.Zero )
			held = (forearm * along + side + outward * (0.55f - side.Length())).SafeNormal();
		var palm = -GlobalBasis.Z.Normalized();
		palm = (palm - forearm * palm.Dot( forearm )).SafeNormal();
		if ( palm == Vector3.Zero ) palm = outward;
		var grip = HandAt + palm * 0.14f * Size * MathF.Max( 0.02f, shown );
		heldTrident.GlobalTransform = new Transform3D( Shapes.LookAt( held, GlobalBasis.Z ).Scaled( Vector3.One * 3.2f * Size * MathF.Max( 0.02f, shown ) ), grip );
		var k = 1f - MathF.Exp( -10f * dt );
		armR.Rotation = armR.Rotation.Lerp( raiseR + new Vector3( MathF.Sin( t * 1.3f ) * 0.05f, 0f, 0f ), k );
		foreR.Rotation = foreR.Rotation.Lerp( foreR0, k );
		armL.Rotation = armL.Rotation.Lerp( raiseL + new Vector3( MathF.Sin( t * 1.1f + 1f ) * 0.08f, 0f, 0f ), k );
		foreL.Rotation = foreL.Rotation.Lerp( foreL0, k );
		head.Rotation = new Vector3( MathF.Sin( t * 0.7f ) * 0.05f, MathF.Sin( t * 0.5f ) * 0.08f, 0f );
	}

	// ---- Hits and death ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		var multiplier = index == 0 ? HeadMultiplier : index >= 3 ? TailMultiplier : 1f;
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * multiplier, Position = position, Origin = origin, Attacker = shooter } );
		gore?.GooBurst( position, -direction, GooColor, 2, 160f, 0.6f );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, index == 0, scoreHit );
	}

	protected override void OnDied()
	{
		Release();
		SetMode( Mode.Dead );
		RaiseKilled();
		Sfx.Play2D( "boss_spawn", 0.7f, 0.6f );
		gore?.GooBurst( GlobalPosition, Vector3.Up, GooColor, 20, 380f, 1.6f );
		foreach ( var part in FindChildren( "*", "StaticBody3D", true, false ).OfType<CreaturePart>().ToList() ) part.QueueFree();
	}

	/// <summary> How long he swells, shaking and spurting, before he bursts. </summary>
	public const float SwellSeconds = 1.1f;
	static readonly Color[] Flesh = { new( 0.56f, 0.26f, 0.8f ), new( 0.22f, 0.42f, 0.88f ), new( 0.32f, 0.78f, 0.36f ) };

	/// <summary> Dead: he clutches, swells and shakes, goo spurting out of him, then bursts (see Burst). </summary>
	void Sink( float dt )
	{
		if ( burst ) return;
		var t = MathX.Clamp( (float)timeSinceDeath / SwellSeconds, 0f, 1f );
		shown = 1f + t * t * 0.35f + MathF.Sin( (float)timeSinceDeath * 40f ) * 0.03f * t;
		body.Position = Rng.InSphere( 0.12f * t );
		RotateY( dt * (0.5f + t * 3f) );
		armR.Rotation = armR.Rotation.Lerp( new Vector3( 0.4f, 0f, 2.4f ), 1f - MathF.Exp( -6f * dt ) );
		armL.Rotation = armL.Rotation.Lerp( new Vector3( 0.4f, 0f, -2.4f ), 1f - MathF.Exp( -6f * dt ) );
		head.Rotation = new Vector3( -0.5f * t, MathF.Sin( (float)timeSinceDeath * 25f ) * 0.3f * t, 0f );
		if ( Rng.Float() < 0.25f + t * 0.5f )
		{
			var from = chest.GlobalPosition + Rng.InSphere( 1.2f * Size );
			gore?.GooSpray( from, (from - chest.GlobalPosition).SafeNormal() + Vector3.Up * 0.3f, 4, Rng.Float() < 0.5f ? GooColor : Flesh[Rng.Int( 0, 2 )], 260f );
		}
		if ( t >= 1f ) Burst();
	}

	/// <summary>
	/// He bursts: head, arms, chest, trident and the tail in sections fly apart as loose pieces (they bleed, can be
	/// shot and float up like the other creatures' pieces), with lumps of flesh, a wide spray of goo, a shock ring
	/// of bubbles and a beat of slow motion.
	/// </summary>
	void Burst()
	{
		burst = true;
		var centre = chest.GlobalPosition;
		var away = lastShotDirection.Flat().SafeNormal();
		void Loose( Node3D piece, float radiusMetres, float mass, float speed )
		{
			if ( piece is null || !IsInstanceValid( piece ) ) return;
			var dir = (piece.GlobalPosition - centre).SafeNormal();
			if ( dir == Vector3.Zero ) dir = Vector3.Up;
			var velocity = (dir + away * 0.3f + Vector3.Up * 0.5f + Rng.InSphere( 0.35f )).SafeNormal() * speed * Rng.Float( 0.7f, 1.2f );
			var gib = Gib.Attach( piece, gore, radiusMetres / U.In, 80f, mass, velocity, GooColor );
			gib.AngularVelocity = Rng.InSphere( 7f );
			CreatureGore.AddStump( piece, piece.GlobalPosition, -dir, radiusMetres * 0.5f / U.In, Flesh[Rng.Int( 0, 2 )], GooColor, 8f );
		}
		// From the tips inward, so each piece takes only what is still joined to it.
		Loose( heldTrident, 0.5f, 6f, 9f );
		for ( var i = tail.Count - 1; i >= 0; i -= 2 ) Loose( tail[i], 0.5f * Size * MathX.Lerp( 1f, 0.5f, i / 8f ), 10f, 8f );
		Loose( head, 0.42f * Size, 9f, 11f );
		Loose( foreR, 0.3f * Size, 5f, 12f );
		Loose( armR, 0.34f * Size, 6f, 11f );
		Loose( foreL, 0.3f * Size, 5f, 12f );
		Loose( armL, 0.34f * Size, 6f, 11f );
		Loose( chest, 0.6f * Size, 16f, 6f );
		Loose( body, 0.5f * Size, 12f, 5f );

		// Lumps of him, big and small, in his three colours, and his goo everywhere.
		for ( var i = 0; i < 34; i++ )
			gore?.ThrowBlob( centre + Rng.InSphere( 1.4f * Size ), Rng.InSphere( 1f ) + Vector3.Up * 0.6f, Flesh[i % 3], GooColor, i % 5 == 0 ? 16f : Rng.Float( 5f, 11f ) );
		for ( var i = 0; i < 5; i++ )
			CreatureGore.Overkill( centre + Rng.InSphere( 1.2f * Size ), Rng.InSphere( 1f ) + Vector3.Up * 0.4f, Flesh[i % 3], GooColor, 2.4f );
		gore?.GooBurst( centre, Vector3.Up, GooColor, 70, 620f, 2.6f );
		gore?.GooBurst( centre, away + Vector3.Up * 0.3f, Flesh[0], 30, 480f, 2f );
		gore?.GooBurst( centre, -away + Vector3.Up * 0.3f, Flesh[2], 30, 480f, 2f );
		for ( var i = 0; i < 16; i++ )
		{
			var a = i / 16f * MathF.Tau;
			gore?.GooSpray( centre, new Vector3( MathF.Cos( a ), 0.35f, MathF.Sin( a ) ), 6, i % 2 == 0 ? GooColor : Flesh[i % 3], 520f, 0.8f, 2f );
		}
		for ( var i = 0; i < 60; i++ )
			WaterBubble.Create( centre + Rng.InSphere( 2.5f * Size ), Rng.Float( 0.4f, 1.4f ), 0f, Rng.Float( 0.6f, 1.6f ) );
		PoseidonVortex.Burst( this, GlobalPosition + Vector3.Down * HoverHeight * 0.8f, 1.8f );
		gore?.Squelch( centre, 1.6f, 0.6f );
		Sfx.Play( "gib", centre, 1f, 0.6f );
		Sfx.Play2D( "boss_spawn", 0.9f, 0.45f );
		foreach ( var player in PlayerSurvivor.All )
			if ( player.Controller is { } pc && pc.GlobalPosition.DistanceTo( centre ) < 45f ) pc.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 1.4f );
		if ( Engine.TimeScale == 1.0 && !AutoTest.Requested )
		{
			Engine.TimeScale = 0.3;
			GetTree().CreateTimer( 0.45, true, false, true ).Timeout += () => { if ( Engine.TimeScale == 0.3 ) Engine.TimeScale = 1.0; };
		}
		if ( AutoTest.Requested ) GD.Print( "[autotest] poseidon burst into pieces" );
		QueueFree();
	}
}

/// <summary>
/// A trident thrown by Poseidon: flies at the player with a slight drop, turning about its shaft, streaming the same
/// bubbles as the player's trident shooter. Hurts a player it passes close to; sticks in the world and shrinks away.
/// </summary>
public partial class PoseidonTrident : Node3D
{
	Vector3 velocity;
	float damage;
	float roll;
	bool stuck;
	TimeSince age;
	TimeSince sinceStuck;
	Node3D thrower;

	public static void Throw( Node3D thrower, Vector3 from, Vector3 velocity, float damage )
	{
		var t = new PoseidonTrident { Name = "Poseidon Trident", velocity = velocity, damage = damage, thrower = thrower };
		thrower.GetTree().CurrentScene.AddChild( t );
		t.GlobalPosition = from;
		var look = new Node3D { Name = "Model" };
		t.AddChild( look );
		look.Scale = Vector3.One * 1.8f;
		TridentModel.BuildTrident( look );
		t.Face();
		t.age = 0f;
	}

	void Face()
	{
		var f = velocity.SafeNormal();
		if ( f != Vector3.Zero ) GlobalBasis = Shapes.LookAt( f ) * new Basis( Vector3.Back, roll );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( stuck )
		{
			if ( sinceStuck > 2.5f ) { Scale = Vector3.One * MathX.Clamp( 1f - ((float)sinceStuck - 2.5f) * 2f, 0.01f, 1f ); if ( sinceStuck > 3f ) QueueFree(); }
			return;
		}
		if ( DevFlags.FreezeEnemies ) return;
		var from = GlobalPosition;
		velocity += Vector3.Down * 1f * dt;
		var to = from + velocity * dt;
		// A player in the way: the trident is long (1.7 m), so anywhere near the line counts.
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive ) continue;
			var chest = player.WorldPosition + Vector3.Up * 1f;
			var closest = Geometry3D.GetClosestPointToSegment( chest, from, to );
			if ( closest.DistanceTo( chest ) > 0.8f ) continue;
			player.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = IsInstanceValid( thrower ) ? thrower : this, Position = closest, Origin = from } );
			player.Controller.Velocity += velocity.SafeNormal() * 4f;
			Sfx.Play( "hit_flesh", closest, 0.9f, 0.8f );
			if ( AutoTest.Requested ) GD.Print( $"[autotest] poseidon trident hit a player at {closest}" );
			QueueFree();
			return;
		}
		var hit = Trace.Ray( this, from, to, Layers.World );
		if ( hit.Hit )
		{
			stuck = true;
			sinceStuck = 0f;
			GlobalPosition = hit.HitPosition - velocity.SafeNormal() * 0.5f;
			Fx.ImpactWorld( hit.HitPosition, hit.Normal );
			if ( AutoTest.Requested ) GD.Print( $"[autotest] poseidon trident stuck in {hit.Node?.Name} at {hit.HitPosition} (flying from {from})" );
			return;
		}
		GlobalPosition = to;
		roll += 5f * dt;
		Face();
		if ( TridentBolt.UnderWater( to ) ) TridentBolt.Bubbles( from, to );
		if ( age > 4f )
		{
			if ( AutoTest.Requested ) GD.Print( $"[autotest] poseidon trident flew out at {GlobalPosition}" );
			QueueFree();
		}
	}
}

/// <summary>
/// Poseidon's whirlpool: a spinning funnel of water rings and bubbles. Thrown, it travels along the floor toward the
/// player for a few seconds, dragging them in when they are close; one that reaches them carries them to Poseidon for
/// his stab. A burst is the short one he vanishes and appears through (and calls seahorses out of).
/// </summary>
public partial class PoseidonVortex : Node3D
{
	public const float PullRadius = 6f;
	public const float CatchRadius = 2.4f;
	public const float Speed = 5.5f;
	public const float Life = 7f;

	Poseidon owner;
	Vector3 heading;
	bool travelling;
	float size = 1f;
	float life;
	TimeSince age;
	readonly List<Node3D> rings = new();
	static StandardMaterial3D ringMaterial, coreMaterial;

	public static void Throw( Poseidon owner, Vector3 at, Vector3 heading ) => Make( owner, at, heading, true, 1f, Life );

	/// <summary> Its work is done (the player it carried has been stabbed, or let go): it dies away. </summary>
	public void Finish() => life = MathF.Min( life, (float)age + 0.5f );

	/// <summary> A short burst at a spot (Poseidon vanishing or appearing, a seahorse arriving). </summary>
	public static void Burst( Node3D source, Vector3 at, float size = 1f ) => Make( source as Poseidon, at, Vector3.Zero, false, size, 1.6f );

	static void Make( Poseidon owner, Vector3 at, Vector3 heading, bool travelling, float size, float life )
	{
		var scene = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( scene is null ) return;
		var v = new PoseidonVortex { Name = "Poseidon Whirlpool", owner = owner, heading = heading, travelling = travelling, size = size, life = life };
		scene.AddChild( v );
		v.GlobalPosition = at;
		v.age = 0f;
	}

	public override void _Ready()
	{
		ringMaterial ??= new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.45f, 0.95f, 1f, 0.45f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		coreMaterial ??= new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.55f, 0.25f, 1f, 0.35f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		// Rings widening upward round a purple funnel, each turning at its own speed.
		for ( var i = 0; i < 5; i++ )
		{
			var ring = new Node3D { Name = $"Ring {i}" };
			AddChild( ring );
			var r = 0.6f + i * 0.45f;
			ring.Position = Vector3.Up * (0.3f + i * 0.55f);
			ring.AddChild( new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = r - 0.08f, OuterRadius = r, Rings = 24, RingSegments = 6 }, MaterialOverride = ringMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Rotation = new Vector3( 0.15f, 0f, 0f ) } );
			rings.Add( ring );
		}
		AddChild( new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 2.4f, BottomRadius = 0.3f, Height = 2.8f, RadialSegments = 16, Rings = 1, CapTop = false, CapBottom = false }, MaterialOverride = coreMaterial, Position = Vector3.Up * 1.4f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
		Scale = Vector3.One * 0.05f;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var t = (float)age;
		// Grows in, holds, and dies away.
		var grow = MathX.Clamp( t / 0.3f, 0f, 1f ) * MathX.Clamp( (life - t) / 0.4f, 0f, 1f );
		Scale = Vector3.One * MathF.Max( 0.02f, size * grow );
		for ( var i = 0; i < rings.Count; i++ ) rings[i].RotateY( dt * (6f + i * 2f) * (i % 2 == 0 ? 1f : -1f) );
		if ( Rng.Float() < 0.6f )
		{
			var a = Rng.Float( 0f, MathF.Tau );
			var h = Rng.Float( 0f, 2.6f ) * size;
			WaterBubble.Create( GlobalPosition + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * (0.4f + h * 0.7f) + Vector3.Up * h, Rng.Float( 0.3f, 0.8f ), 0f, Rng.Float( 0.5f, 1f ) );
		}
		if ( t >= life ) { QueueFree(); return; }
		if ( !travelling || DevFlags.FreezeEnemies ) return;

		// Along the floor toward where it was thrown, bending after the nearest player, kept inside the map.
		if ( PlayerSurvivor.FindNearestAlive( GlobalPosition ) is { } nearest )
		{
			var want = (nearest.WorldPosition - GlobalPosition).Flat().SafeNormal();
			if ( want != Vector3.Zero ) heading = heading.Lerp( want, 1f - MathF.Exp( -1.6f * dt ) ).Flat().SafeNormal();
		}
		var next = GlobalPosition + heading * Speed * dt;
		next.Y = Sculpt.FloorBelow( this, next + Vector3.Up * 2f, 10f );
		GlobalPosition = Sculpt.InBounds( next );
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive || player.Controller is not { } c || c.Scripted ) continue;
			var offset = (GlobalPosition - c.GlobalPosition).Flat();
			var d = offset.Length();
			if ( d > PullRadius ) continue;
			// Dragged in, harder the closer they are; caught at the middle.
			c.Velocity += offset.SafeNormal() * (1f - d / PullRadius) * 22f * dt;
			// Swept up: the whirlpool turns back to Poseidon with the player inside (he moves it from here).
			if ( d < CatchRadius && MathF.Abs( c.GlobalPosition.Y - GlobalPosition.Y ) < 4f && owner is not null && IsInstanceValid( owner ) && owner.Grab( player, this ) )
			{
				Sfx.Play2D( "whirlpool", 1f );
				c.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.6f );
				life = t + 8f;
				travelling = false;
				return;
			}
		}
	}
}
