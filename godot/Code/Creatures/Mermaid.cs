using Godot;

/// <summary>
/// Mermaids (Atlantis only, from wave 21): about player height, with a small copy of Poseidon's scaled tail and his
/// drifting purple, blue and green skin. The women's faces, after the user's reference photo: a pale mask of scallop
/// shells with spiked cheek flares; all have black eyes and a mouth stretched open on long needle teeth. Fin hands like the Rocky Shark's fins. Women have long
/// red, blonde or black hair and shell tops; men are broader and wear gold helmets. Most carry a trident and swim in to
/// stab behind futuristic honeycomb energy shields held in the left fist (cells stop bullets until they shatter; the
/// head and tail are open); about one in three carries a bow and keeps its distance, shooting arrows. Slower than the other enemies, with
/// about 30% more health. Built in code (Sculpt) in Poseidon's model units, drawn at `Scale` of them. Head is the weak spot.
/// </summary>
public partial class Mermaid : Creature
{
	public static readonly List<Mermaid> All = new();

	/// <summary> Metres per model unit (Poseidon's units): about 1.9 m from the crown to the tip of the tail. </summary>
	public const float Scale = 0.3f;
	public float Speed = 3.6f;              // m/s: a little slower than the others (about 4 to 5)
	public float HoverHeight = 1.25f;       // the waist above the floor
	public float TridentDamage = 14f;
	public float TridentReach = 2.4f;
	public float StabCooldown = 1.5f;
	public float ArrowDamage = 10f;
	public float ArrowSpeed = 24f;
	public float ShootEvery = 2.4f;
	public float KeepAway = 13f;            // archers circle at about this distance
	public float HeadMultiplier = 2f;
	public float TailMultiplier = 0.7f;
	/// <summary> The trident carriers' shield: its part index. Shots that hit a whole cell (or the frame) do nothing. </summary>
	public const int ShieldIndex = 9;
	const float ShieldRadius = 0.95f;   // model units
	/// <summary> The shield is a honeycomb of hexagon cells (this is each one's radius, model units). </summary>
	const float CellSize = 0.19f;
	/// <summary> What it takes to break a cell; a broken cell lets shots through to the mermaid behind it. </summary>
	public float CellHealth = 60f;
	/// <summary> Tests: shots the shield has stopped, and how many of its cells are broken. </summary>
	public int Blocked { get; private set; }
	public int CellsBroken => cells.Count( c => c.Health <= 0f );

	/// <summary>
	/// One cell of the shield's energy honeycomb: teal glass, bright at its rim, flashing when hit, cracking and going
	/// red as it weakens (hurt 0 to 1), its cracks placed by its own seed.
	/// </summary>
	static readonly Shader CellShader = new()
	{
		Code = @"shader_type spatial;
render_mode cull_disabled, depth_draw_never, unshaded;
uniform vec3 tint : source_color = vec3(0.25, 0.95, 1.0);
uniform float cell_r = 0.17;
uniform float hurt = 0.0;
uniform float flash = 0.0;
uniform float seed = 0.0;
varying vec3 p;
void vertex() { p = VERTEX; }
void fragment() {
	vec2 v = p.xz / cell_r;
	float d = max(abs(v.y), abs(v.x) * 0.866 + abs(v.y) * 0.5) / 0.866;
	float rim = smoothstep(0.72, 0.97, d);
	float crack = 0.0;
	for (int i = 0; i < 3; i++) {
		float a = fract(seed * (3.1 + float(i) * 1.7)) * 3.14159;
		vec2 n = vec2(cos(a), sin(a));
		vec2 o = vec2(fract(seed * 5.3) - 0.5, fract(seed * 7.9) - 0.5) * 0.5;
		crack = max(crack, (1.0 - smoothstep(0.0, 0.05, abs(dot(v - o, n)))) * step(0.25 + float(i) * 0.25, hurt));
	}
	float pulse = 0.5 + 0.5 * sin(TIME * 3.0 + seed * 20.0);
	vec3 c = mix(tint, vec3(1.0, 0.4, 0.35), hurt * 0.7);
	ALBEDO = c * (0.35 + rim * 1.2 + flash * 1.6 + pulse * 0.1) + vec3(1.0) * crack;
	ALPHA = clamp(0.22 + rim * 0.55 + flash * 0.5 + crack * 0.7, 0.0, 0.95);
}",
	};
	sealed class Cell
	{
		public Vector2 At;
		public float Health;
		public MeshInstance3D Mesh;
		public ShaderMaterial Material;
		public TimeSince SinceHit = 99f;
		public bool Lit;
	}
	readonly List<Cell> cells = new();
	Node3D shield;
	CreaturePart guard;

	/// <summary> A man (gold helmet, broad chest) rather than a woman (long hair, shell top). </summary>
	public bool Male;
	/// <summary> Carries a bow and shoots from a distance, instead of a trident. </summary>
	public bool Archer;
	/// <summary> Women's hair: 0 red, 1 blonde, 2 black. </summary>
	public int HairColour;
	/// <summary> Tests: how many times its trident or arrows have hurt a player. </summary>
	public int Hits { get; private set; }
	public int ArrowsShot { get; private set; }

	static readonly Color[] HairColours = { new( 0.78f, 0.17f, 0.08f ), new( 0.98f, 0.8f, 0.38f ), new( 0.08f, 0.04f, 0.1f ) };
	static readonly Color Shell = new( 0.97f, 0.83f, 0.74f );
	static readonly Color ShellDark = new( 0.86f, 0.55f, 0.5f );
	static readonly Color Gold = new( 1f, 0.76f, 0.26f );
	static readonly Color GooColor = new( 0.3f, 0.95f, 0.85f );
	static readonly Color[] Flesh = { new( 0.56f, 0.26f, 0.8f ), new( 0.22f, 0.42f, 0.88f ), new( 0.32f, 0.78f, 0.36f ) };

	enum Mode { Swim, WindUp, Stab, Draw }

	Node3D body, chest, head, armR, foreR, handR, armL, foreL, handL, weapon;
	readonly List<Node3D> tail = new();
	readonly List<Node3D> hair = new();
	ShaderMaterial skin, handSkin;
	Mode mode = Mode.Swim;
	TimeSince inMode;
	TimeSince sinceAttack = 99f;
	TimeSince sinceHit = 99f;
	TimeSince sinceFloor = 99f;
	Vector3 velocity;
	float floorY;
	float phase;
	float orbit, orbitSign = 1f;
	float curl;
	float lean;
	bool struck;

	public static Mermaid Spawn( Node parent, Vector3 position, float maxHealth, bool? male = null, bool? archer = null )
	{
		var m = new Mermaid { Name = "Mermaid" };
		m.Male = male ?? Rng.Float() < 0.4f;
		m.Archer = archer ?? Rng.Float() < 0.3f;
		m.HairColour = Rng.Int( 0, 2 );
		return Place( m, parent, position, maxHealth );
	}

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	protected override void Setup()
	{
		BeginSetup( 200f );
		phase = Rng.Float( 0f, MathF.Tau );
		orbit = Rng.Float( 0f, MathF.Tau );
		orbitSign = Rng.Float() < 0.5f ? -1f : 1f;
		floorY = Sculpt.FloorBelow( this, GlobalPosition );
		sinceAttack = Rng.Float( 0f, 1f );
		Build();
	}

	// ---- Looks ----

	void Build()
	{
		skin = new ShaderMaterial { Shader = Poseidon.SkinShader };
		handSkin = new ShaderMaterial { Shader = Poseidon.SkinShader };
		handSkin.SetShaderParameter( "pin", new Vector4( 0f, 1.4f, -0.4f, 1f ) );
		var gold = Sculpt.Paint( Gold, 0.14f, 0.95f );
		gold.EmissionEnabled = true;
		gold.Emission = Gold;
		gold.EmissionEnergyMultiplier = 0.12f;
		var shell = Sculpt.Paint( Shell, 0.45f );
		var shellDark = Sculpt.Paint( ShellDark, 0.5f );
		var fin = Sculpt.Membrane( Male ? new Color( 0.25f, 0.75f, 0.7f ) : new Color( 0.75f, 0.35f, 0.9f ), 0.65f );
		var dark = Sculpt.Paint( new Color( 0.08f, 0.02f, 0.06f ), 0.5f );
		var hairPaint = Sculpt.Paint( HairColours[HairColour], 0.6f );
		var look = new Poseidon.Look
		{
			Scales = Sculpt.Paint( Male ? new Color( 0.1f, 0.5f, 0.55f ) : new Color( 0.45f, 0.18f, 0.6f ), 0.35f, 0.2f ),
			Armour = Male ? gold : shellDark,
			Fins = fin,
			ScaleColours = Male
				? new Material[] { Poseidon.ScaleMaterial( new Color( 0.1f, 0.62f, 0.6f ) ), Poseidon.ScaleMaterial( new Color( 0.3f, 0.85f, 0.3f ) ), Poseidon.ScaleMaterial( Gold, 0.8f, 0.1f ) }
				: new Material[] { Poseidon.ScaleMaterial( new Color( 0.62f, 0.25f, 0.9f ) ), Poseidon.ScaleMaterial( new Color( 0.95f, 0.45f, 0.7f ) ), Poseidon.ScaleMaterial( new Color( 0.2f, 0.7f, 0.85f ) ) },
		};

		body = new Node3D { Name = "Body", Scale = Vector3.One * Scale };
		AddChild( body );
		Poseidon.BuildTail( body, look, tail );
		Sculpt.Blob( body, Vector3.Zero, new Vector3( 0.8f, 0.24f, 0.62f ), Male ? gold : shellDark );   // the belt where skin meets scales

		// The torso.
		chest = Sculpt.Joint( body, new Vector3( 0f, 0.15f, 0f ), "Chest" );
		var width = Male ? 1.2f : 0.9f;
		Sculpt.Blob( chest, new Vector3( 0f, 0.22f, 0f ), new Vector3( width * 0.72f, 0.7f, 0.5f ), skin );
		Sculpt.Blob( chest, new Vector3( 0f, 0.74f, 0f ), new Vector3( width, 0.75f, 0.64f ), skin );
		Sculpt.Limb( chest, new Vector3( 0f, 1.0f, 0f ), new Vector3( 0f, 1.3f, -0.01f ), Male ? 0.2f : 0.15f, Male ? 0.17f : 0.13f, skin, "Neck" );
		foreach ( var s in new[] { -1f, 1f } )
		{
			if ( Male )
			{
				Sculpt.Blob( chest, new Vector3( s * 0.25f, 0.82f, -0.26f ), new Vector3( 0.46f, 0.34f, 0.22f ), skin );   // pecs
				for ( var k = 0; k < 3; k++ )
					Sculpt.Blob( chest, new Vector3( s * 0.11f, 0.42f - k * 0.15f, -0.23f ), new Vector3( 0.18f, 0.12f, 0.1f ), skin );   // abs
				Sculpt.Blob( chest, new Vector3( s * 0.58f, 1.0f, 0f ), new Vector3( 0.48f, 0.34f, 0.5f ), gold );   // gold shoulder plates
			}
			else
			{
				// A shell top: a scallop over each breast, ridged, on a thin cord.
				var cup = new Vector3( s * 0.2f, 0.78f, -0.27f );
				Sculpt.Blob( chest, cup, new Vector3( 0.32f, 0.3f, 0.16f ), shell );
				for ( var k = -2; k <= 2; k++ )
					Sculpt.Block( chest, cup + new Vector3( k * 0.05f, 0.02f, -0.075f ), new Vector3( 0.022f, 0.26f, 0.03f ), shellDark, new Basis( Vector3.Back, -k * 0.28f ) );
				Sculpt.Limb( chest, cup + new Vector3( s * 0.13f, 0.06f, 0.02f ), new Vector3( s * 0.32f, 0.95f, 0.08f ), 0.015f, 0.015f, shellDark, "Cord" );
			}
		}

		// The head, after the reference (a shell-mask mermaid): a pale mask of scallop shells over the forehead with a
		// spiked ridge up the middle, flared ribbed shells on the cheeks bristling with needle spikes, black eyes, pastel
		// dots round them, and a mouth stretched open on rows of long needle teeth.
		head = Sculpt.Joint( chest, new Vector3( 0f, 1.32f, -0.02f ), "Head" );
		Sculpt.Blob( head, new Vector3( 0f, 0.22f, 0f ), new Vector3( 0.44f, 0.52f, 0.46f ), skin );
		var ivory = Sculpt.Paint( new Color( 0.96f, 0.93f, 0.88f ), 0.4f );
		var blush = Sculpt.Paint( new Color( 0.95f, 0.78f, 0.78f ), 0.45f );
		var spike = Sculpt.Paint( new Color( 1f, 0.98f, 0.92f ), 0.3f );
		var black = Sculpt.Paint( new Color( 0.01f, 0.01f, 0.012f ), 0.08f );
		var iris = Sculpt.Glow( new Color( 0.55f, 0.28f, 0.08f ), 1.2f );
		var gums = Sculpt.Paint( new Color( 0.45f, 0.06f, 0.1f ), 0.5f );
		var throat = Sculpt.Paint( new Color( 0.06f, 0.0f, 0.02f ), 0.6f );

		// A scallop: a flattened fan of ribs from a hinge, its face tilted by `turn`.
		void Scallop( Vector3 hinge, float size, Basis turn, Material face, Material rib )
		{
			Sculpt.Blob( head, hinge + turn * new Vector3( 0f, size * 0.55f, 0f ), new Vector3( size * 0.95f, size * 1.05f, size * 0.22f ), face, turn );
			for ( var k = -3; k <= 3; k++ )
			{
				var a = k * 0.2f;
				Sculpt.Block( head, hinge + turn * new Vector3( MathF.Sin( a ) * size * 0.3f, size * 0.5f, -size * 0.1f ), new Vector3( size * 0.06f, size * 0.95f, size * 0.06f ), rib, turn * new Basis( Vector3.Back, -a ) );
			}
			Sculpt.Blob( head, hinge, new Vector3( size * 0.3f, size * 0.18f, size * 0.15f ), rib, turn );
		}
		if ( !Male )   // the women wear the shell mask; the men show their faces under their helmets
		{
			// The forehead: five scallops fanned up over the brow.
			for ( var k = -2; k <= 2; k++ )
			{
				var size = k == 0 ? 0.17f : MathF.Abs( k ) == 1 ? 0.15f : 0.16f;
				var tilt = new Basis( Vector3.Back, -k * 0.32f ) * new Basis( Vector3.Right, 0.3f );
				Scallop( new Vector3( k * 0.09f, 0.34f, -0.24f + MathF.Abs( k ) * 0.04f ), size, tilt, k % 2 == 0 ? ivory : blush, ivory );
			}
			// The ridge up the middle of the brow, with two tall spikes and pearls at its foot.
			Sculpt.Limb( head, new Vector3( 0f, 0.29f, -0.27f ), new Vector3( 0f, 0.58f, -0.2f ), 0.025f, 0.015f, spike, "Ridge" );
			foreach ( var s in new[] { -1f, 1f } )
				Sculpt.Cone( head, new Vector3( s * 0.06f, 0.5f, -0.22f ), new Vector3( s * 0.07f, 0.7f, -0.2f ), 0.014f, spike, 5 );
			for ( var k = -1; k <= 1; k++ ) Sculpt.Blob( head, new Vector3( k * 0.025f, 0.285f, -0.275f ), Vector3.One * 0.022f, ivory );
		}
		else
		{
			// A heavy brow and hard cheekbones.
			Sculpt.Blob( head, new Vector3( 0f, 0.33f, -0.21f ), new Vector3( 0.4f, 0.09f, 0.12f ), skin );
			foreach ( var s in new[] { -1f, 1f } )
				Sculpt.Blob( head, new Vector3( s * 0.15f, 0.18f, -0.19f ), new Vector3( 0.12f, 0.08f, 0.1f ), skin );
		}
		foreach ( var s in new[] { -1f, 1f } )
		{
			// The cheeks: a ribbed shell flaring back past the eye, spikes standing out of its edge.
			var cheek = new Vector3( s * 0.16f, 0.2f, -0.2f );
			var flare = new Basis( Vector3.Up, -s * 0.9f ) * new Basis( Vector3.Back, -s * 0.5f );
			if ( !Male ) Scallop( cheek, 0.2f, flare, blush, ivory );
			for ( var k = 0; k < (Male ? 0 : 8); k++ )
			{
				var a = -0.9f + k * 0.28f;   // round the shell's outer edge, from below the eye to above it
				var foot = cheek + new Vector3( s * (0.12f + MathF.Cos( a ) * 0.08f), MathF.Sin( a ) * 0.13f, 0.06f + MathF.Cos( a ) * 0.05f );
				var outward = new Vector3( s * MathF.Cos( a ), MathF.Sin( a ) * 0.8f, 0.25f ).Normalized();
				Sculpt.Cone( head, foot, foot + outward * (0.1f + (k % 3) * 0.035f), 0.01f, spike, 4 );
			}
			if ( !Male ) Sculpt.Cone( head, cheek + new Vector3( s * 0.02f, -0.06f, -0.05f ), cheek + new Vector3( s * 0.1f, -0.1f, -0.1f ), 0.01f, spike, 4 );
			// Black eyes, a dark amber iris in each.
			Sculpt.Blob( head, new Vector3( s * 0.1f, 0.26f, -0.225f ), new Vector3( 0.12f, 0.065f, 0.05f ), black, new Basis( Vector3.Back, -s * 0.15f ) );
			Sculpt.Blob( head, new Vector3( s * 0.1f, 0.26f, -0.248f ), new Vector3( 0.045f, 0.045f, 0.012f ), iris );
			// Pastel dots under and beside the eyes.
			for ( var k = 0; k < (Male ? 0 : 6); k++ )
			{
				var dot = new Vector3( s * (0.09f + (k % 3) * 0.04f), 0.2f - (k / 3) * 0.035f, -0.235f + (k % 3) * 0.015f );
				Sculpt.Blob( head, dot, Vector3.One * (0.018f + (k % 2) * 0.008f), k % 3 == 0 ? Sculpt.Glow( new Color( 1f, 0.5f, 0.65f ), 0.8f ) : k % 3 == 1 ? Sculpt.Glow( new Color( 1f, 0.82f, 0.3f ), 0.8f ) : Sculpt.Glow( new Color( 0.5f, 1f, 0.6f ), 0.8f ) );
			}
		}
		Sculpt.Blob( head, new Vector3( 0f, 0.19f, -0.25f ), new Vector3( 0.07f, 0.09f, 0.07f ), skin );   // the nose
		// The mouth, stretched open: dark gums and throat, rows of long uneven needle teeth top and bottom.
		var mouth = new Vector3( 0f, 0.04f, -0.21f );
		Sculpt.Blob( head, mouth, new Vector3( 0.2f, 0.14f, 0.08f ), gums );
		Sculpt.Blob( head, mouth + new Vector3( 0f, 0f, 0.015f ), new Vector3( 0.15f, 0.09f, 0.06f ), throat );
		for ( var k = 0; k < 9; k++ )
		{
			var x = (k - 4f) * 0.019f;
			var curve = MathF.Abs( k - 4f ) * 0.004f;
			var top = 0.06f + (k % 3) * 0.018f;
			var bottom = 0.045f + ((k + 1) % 3) * 0.016f;
			Sculpt.Cone( head, mouth + new Vector3( x, 0.06f - curve, -0.035f + curve ), mouth + new Vector3( x * 0.9f, 0.06f - curve - top, -0.04f + curve ), 0.009f, spike, 4 );
			Sculpt.Cone( head, mouth + new Vector3( x + 0.009f, -0.06f + curve, -0.035f + curve ), mouth + new Vector3( x * 0.9f + 0.009f, -0.06f + curve + bottom, -0.04f + curve ), 0.008f, spike, 4 );
		}
		// Green glitter on the neck.
		for ( var k = 0; k < 5; k++ )
			Sculpt.Blob( chest, new Vector3( 0.08f + k * 0.03f, 1.05f + (k % 2) * 0.05f, -0.15f - k * 0.01f ), Vector3.One * 0.025f, Sculpt.Glow( new Color( 0.45f, 1f, 0.55f ), 1.6f ) );
		if ( Male )
		{
			// A gold helmet: a dome, a crest like a fin from brow to nape, cheek guards.
			Sculpt.Blob( head, new Vector3( 0f, 0.36f, 0.03f ), new Vector3( 0.52f, 0.42f, 0.54f ), gold );
			Sculpt.Block( head, new Vector3( 0f, 0.58f, 0.05f ), new Vector3( 0.05f, 0.22f, 0.5f ), gold );
			Sculpt.Block( head, new Vector3( 0f, 0.66f, 0.1f ), new Vector3( 0.03f, 0.16f, 0.36f ), fin );
			foreach ( var s in new[] { -1f, 1f } )
				Sculpt.Block( head, new Vector3( s * 0.22f, 0.14f, -0.06f ), new Vector3( 0.05f, 0.26f, 0.2f ), gold, new Basis( Vector3.Up, s * 0.3f ) );
			Sculpt.Blob( head, new Vector3( 0f, 0.48f, -0.23f ), new Vector3( 0.09f, 0.09f, 0.04f ), Sculpt.Glow( new Color( 0.66f, 0.22f, 1f ), 2f ) );
		}
		else
		{
			// Long hair: a full crown and strands down her back, swaying.
			Sculpt.Blob( head, new Vector3( 0f, 0.33f, 0.06f ), new Vector3( 0.54f, 0.5f, 0.52f ), hairPaint );
			for ( var k = 0; k < 7; k++ )
			{
				var x = (k - 3f) * 0.07f;
				var strand = Sculpt.Joint( head, new Vector3( x, 0.3f, 0.18f ), $"Hair {k}" );
				var length = 0.9f + (k % 3) * 0.15f;
				Sculpt.Limb( strand, Vector3.Zero, new Vector3( x * 0.6f, -length, 0.12f ), 0.09f, 0.03f, hairPaint, "Strand" );
				hair.Add( strand );
			}
			foreach ( var s in new[] { -1f, 1f } )
				Sculpt.Limb( head, new Vector3( s * 0.2f, 0.3f, -0.04f ), new Vector3( s * 0.26f, -0.3f, -0.08f ), 0.07f, 0.03f, hairPaint, "Lock" );
		}

		// Arms with fin hands: a palm and four webbed fin rays fanned out, like the Rocky Shark's fins.
		(Node3D Arm, Node3D Fore, Node3D Hand) Arm( float s )
		{
			var arm = Sculpt.Joint( chest, new Vector3( s * width * 0.5f, 0.98f, 0f ), s > 0 ? "Right Arm" : "Left Arm" );
			Sculpt.Limb( arm, Vector3.Zero, new Vector3( 0f, -0.55f, 0f ), Male ? 0.15f : 0.11f, Male ? 0.12f : 0.09f, skin );
			var fore = Sculpt.Joint( arm, new Vector3( 0f, -0.55f, 0f ), "Elbow" );
			Sculpt.Limb( fore, Vector3.Zero, new Vector3( 0f, -0.5f, 0f ), Male ? 0.12f : 0.09f, 0.07f, skin );
			Sculpt.Limb( fore, new Vector3( 0f, -0.32f, 0f ), new Vector3( 0f, -0.46f, 0f ), 0.1f, 0.09f, Male ? gold : shellDark );   // a cuff
			var hand = Sculpt.Joint( fore, new Vector3( 0f, -0.55f, 0f ), "Hand" );
			Sculpt.Blob( hand, Vector3.Zero, new Vector3( 0.15f, 0.17f, 0.12f ), handSkin );
			for ( var k = 0; k < 4; k++ )
			{
				var a = (k - 1.5f) * 0.32f;
				Sculpt.Block( hand, new Vector3( MathF.Sin( a ) * 0.11f, -0.14f, 0f ), new Vector3( 0.07f, 0.24f, 0.012f ), fin, new Basis( Vector3.Back, a ) );
				Sculpt.Limb( hand, new Vector3( MathF.Sin( a ) * 0.05f, -0.05f, 0f ), new Vector3( MathF.Sin( a ) * 0.17f, -0.25f, 0f ), 0.012f, 0.006f, handSkin, "Ray" );
			}
			return (arm, fore, hand);
		}
		(armR, foreR, handR) = Arm( 1f );
		(armL, foreL, handL) = Arm( -1f );

		if ( !Archer )
		{
			// A futuristic shield gripped in the left hand: a honeycomb of glowing energy cells in a dark metal frame
			// with gold corners and light strips. Whole cells stop bullets; shoot one enough and it shatters, and
			// shots go through the hole. Placed each frame at the hand (Animate), facing the way the mermaid faces.
			shield = new Node3D { Name = "Shield" };
			AddChild( shield );
			const float r = ShieldRadius;
			var face = new Basis( Vector3.Right, MathF.PI * 0.5f );   // a cylinder's flat face turned to -Z
			var cellMesh = new CylinderMesh { TopRadius = CellSize * 0.9f, BottomRadius = CellSize * 0.9f, Height = 0.02f, RadialSegments = 6, Rings = 1 };
			var apothem = r * 0.866f - 0.05f;
			float HexDistance( Vector2 v ) => MathF.Max( MathF.Abs( v.Y ), MathF.Abs( v.X ) * 0.866f + MathF.Abs( v.Y ) * 0.5f );
			var seedBase = Rng.Float( 0f, 10f );
			for ( var q = -3; q <= 3; q++ )
				for ( var rr = -3; rr <= 3; rr++ )
				{
					var at = new Vector2( CellSize * 1.5f * q, CellSize * 1.7320508f * (rr + q * 0.5f) );
					// Only cells wholly inside the frame (each corner of the cell inside the frame's hexagon).
					var inside = true;
					for ( var k = 0; k < 6 && inside; k++ )
						inside = HexDistance( at + new Vector2( MathF.Cos( k * MathF.Tau / 6f ), MathF.Sin( k * MathF.Tau / 6f ) ) * CellSize ) <= apothem;
					if ( !inside ) continue;
					var material = new ShaderMaterial { Shader = CellShader };
					material.SetShaderParameter( "cell_r", CellSize * 0.9f );
					material.SetShaderParameter( "seed", (seedBase + cells.Count * 0.6180339f) % 1f );
					material.SetMeta( "no_caustics", true );
					var mesh = new MeshInstance3D { Name = "Cell", Mesh = cellMesh, MaterialOverride = material, Transform = new Transform3D( face, new Vector3( at.X, at.Y, 0f ) ), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
					shield.AddChild( mesh );
					cells.Add( new Cell { At = at, Health = CellHealth, Mesh = mesh, Material = material } );
				}
			var frame = Sculpt.Paint( new Color( 0.12f, 0.14f, 0.18f ), 0.25f, 0.85f );
			var light = Sculpt.Glow( new Color( 0.3f, 1f, 0.95f ), 2.6f );
			for ( var k = 0; k < 6; k++ )
			{
				var a0 = k / 6f * MathF.Tau;
				var a1 = (k + 1) / 6f * MathF.Tau;
				var c0 = new Vector3( MathF.Cos( a0 ) * r, MathF.Sin( a0 ) * r, 0f );
				var c1 = new Vector3( MathF.Cos( a1 ) * r, MathF.Sin( a1 ) * r, 0f );
				Sculpt.Limb( shield, c0, c1, 0.06f, 0.06f, frame, "Frame" );
				Sculpt.Limb( shield, c0 * 0.93f + new Vector3( 0f, 0f, -0.04f ), c1 * 0.93f + new Vector3( 0f, 0f, -0.04f ), 0.018f, 0.018f, light, "Strip" );
				Sculpt.Blob( shield, c0, Vector3.One * 0.16f, gold );   // the corners
			}
			// The grip behind the middle, where the hand holds it, and the emitter bar it hangs from.
			Sculpt.Limb( shield, new Vector3( 0f, -0.25f, 0.12f ), new Vector3( 0f, 0.25f, 0.12f ), 0.05f, 0.05f, frame, "Grip" );
			Sculpt.Limb( shield, new Vector3( 0f, -r * 0.8f, 0.05f ), new Vector3( 0f, r * 0.8f, 0.05f ), 0.025f, 0.025f, frame, "Spine" );
		}

		weapon = new Node3D { Name = Archer ? "Bow" : "Trident" };
		if ( Archer )
		{
			// A bow of driftwood and gold, its string glowing faintly, held upright in the left hand.
			handL.AddChild( weapon );
			var wood = Sculpt.Paint( new Color( 0.45f, 0.28f, 0.16f ), 0.7f );
			const int pieces = 8;
			Vector3 Arc( int i )
			{
				var a = (i / (float)pieces - 0.5f) * 2.2f;
				return new Vector3( 0f, MathF.Sin( a ) * 1.5f, -MathF.Cos( a ) * 0.55f + 0.3f );
			}
			for ( var i = 0; i < pieces; i++ ) Sculpt.Limb( weapon, Arc( i ), Arc( i + 1 ), 0.045f, 0.045f, i == pieces / 2 || i == pieces / 2 - 1 ? gold : wood, "Limb" );
			Sculpt.Limb( weapon, Arc( 0 ), Arc( pieces ), 0.008f, 0.008f, Sculpt.Glow( new Color( 0.7f, 1f, 0.9f ), 1.2f ), "String" );
		}
		else
		{
			AddChild( weapon );   // placed each frame in world space, in the right fist
			TridentModel.BuildTrident( weapon );
		}

		// What can be shot: the head (index 0, the weak spot), the chest, the belly, the tail.
		void Hit( Node3D on, int index, Vector3 at, float radius )
		{
			var part = CreaturePart.Create( this, index, $"Mermaid {index}", radius );
			on.AddChild( part );
			part.Position = at;
		}
		Hit( head, 0, new Vector3( 0f, 0.25f, 0f ), 0.34f );
		Hit( chest, 1, new Vector3( 0f, 0.72f, 0f ), 0.5f );
		Hit( chest, 2, new Vector3( 0f, 0.18f, 0f ), 0.4f );
		if ( shield is not null )
		{
			// The shield's own part: a hexagon-wide slab, so shots stop on it before the body.
			guard = CreaturePart.Create( this, ShieldIndex, "Mermaid Shield", 1f );
			guard.Collider.Shape = new CylinderShape3D { Radius = ShieldRadius * 0.95f, Height = 0.4f };   // thick enough for the physics engine at this scale
			guard.Collider.Basis = new Basis( Vector3.Right, MathF.PI * 0.5f );
			shield.AddChild( guard );
		}
		for ( var i = 0; i < 3; i++ ) Hit( tail[i * 2], 3 + i, new Vector3( 0f, -0.2f, 0f ), MathX.Lerp( 0.42f, 0.3f, i / 2f ) );
	}

	// ---- Thinking ----

	void SetMode( Mode m )
	{
		mode = m;
		inMode = 0f;
	}

	public override void _Process( double delta )
	{
		if ( health is null || body is null ) return;
		var dt = (float)delta;
		var t = (float)Clock.Now + phase;
		Animate( t, dt );
		if ( IsDead || DevFlags.FreezeEnemies ) return;
		if ( sinceFloor > 0.25f ) { sinceFloor = 0f; floorY = Sculpt.FloorBelow( this, GlobalPosition + Vector3.Up * 0.5f ); }

		var prey = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var wish = Vector3.Zero;
		if ( prey is not null )
		{
			var chestAt = prey.WorldPosition + Vector3.Up * 1.1f;
			var to = (chestAt - GlobalPosition).Flat();
			var distance = to.Length();
			var dir = to / MathF.Max( 0.01f, distance );
			if ( Archer )
			{
				// Circle at a distance, closing or backing off to keep it.
				orbit += orbitSign * dt * 0.35f;
				var spot = prey.WorldPosition + new Vector3( MathF.Cos( orbit ), 0f, MathF.Sin( orbit ) ) * KeepAway;
				var toSpot = (spot - GlobalPosition).Flat();
				if ( mode == Mode.Swim ) wish = toSpot.Length() > 1f ? toSpot.Normalized() * Speed : Vector3.Zero;
				if ( mode == Mode.Swim && sinceAttack > ShootEvery && distance < KeepAway * 2.2f ) SetMode( Mode.Draw );
				if ( mode == Mode.Draw && inMode > 0.7f )
				{
					Loose( prey );
					sinceAttack = 0f;
					SetMode( Mode.Swim );
				}
			}
			else
			{
				// In close, a wind-up, the thrust, then a moment before the next.
				if ( mode == Mode.Swim )
				{
					if ( distance > 1.6f ) wish = dir * Speed;
					else wish = -dir * 0.6f;
					if ( distance < TridentReach && sinceAttack > StabCooldown ) SetMode( Mode.WindUp );
				}
				else if ( mode == Mode.WindUp && inMode > 0.4f ) { SetMode( Mode.Stab ); struck = false; }
				else if ( mode == Mode.Stab )
				{
					wish = dir * Speed * 0.8f;   // lunges into it
					if ( !struck && inMode > 0.1f )
					{
						struck = true;
						sinceAttack = 0f;
						if ( (prey.WorldPosition - GlobalPosition).Flat().Length() < TridentReach + 0.4f && prey.Health is { } h )
						{
							h.OnDamage( new DamageInfo { Damage = TridentDamage, Attacker = this, Position = chestAt, Origin = GlobalPosition } );
							Hits++;
							Sfx.Play( "hit_flesh", chestAt, 0.7f, 1.2f );
						}
						else Sfx.Play( "creature_attack", GlobalPosition, 0.5f, 1.3f );
					}
					if ( inMode > 0.45f ) SetMode( Mode.Swim );
				}
			}
			Face( dir, mode == Mode.Swim ? 4f : 9f, dt );
		}

		// Keep away from the other mermaids a little.
		foreach ( var other in All )
		{
			if ( other == this || other.IsDead ) continue;
			var away = (GlobalPosition - other.GlobalPosition).Flat();
			var d = away.Length();
			if ( d < 1.8f && d > 0.01f ) wish += away / d * (1.8f - d) * 2f;
		}
		velocity = velocity.Lerp( wish, 1f - MathF.Exp( -3f * dt ) );
		// At hover height over the floor, round walls (rising over them), inside the map.
		var next = GlobalPosition + velocity * dt;
		var ahead = Trace.Ray( this, GlobalPosition, next + velocity.SafeNormal() * 0.8f, Layers.World );
		if ( ahead.Hit ) { velocity = velocity.Slide( ahead.Normal ) + Vector3.Up * 2.5f; next = GlobalPosition + velocity * dt; }
		var want = floorY + HoverHeight + MathF.Sin( t * 1.6f ) * 0.08f;
		next.Y = MathX.Lerp( next.Y, want, 1f - MathF.Exp( -2.5f * dt ) );
		next.Y = MathF.Max( next.Y, floorY + HoverHeight * 0.6f );
		GlobalPosition = Sculpt.InBounds( next );
	}

	/// <summary> An arrow at the player, leading them a little. </summary>
	void Loose( PlayerSurvivor prey )
	{
		var from = handL.GlobalPosition;
		var aim = prey.WorldPosition + Vector3.Up * 1.1f;
		var flight = from.DistanceTo( aim ) / ArrowSpeed;
		aim += prey.Controller.Velocity * flight * 0.5f;
		MermaidArrow.Shoot( this, from, (aim - from).SafeNormal() * ArrowSpeed, ArrowDamage );
		ArrowsShot++;
		Sfx.Play( "harpoon", from, 0.6f, 1.4f );
	}

	/// <summary> Called by its arrows when one hits. </summary>
	public void ArrowHit() => Hits++;

	void Animate( float t, float dt )
	{
		var flash = MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f );
		skin.SetShaderParameter( "flash", flash );
		handSkin.SetShaderParameter( "flash", flash );
		skin.SetShaderParameter( "centre", body.GlobalPosition );
		// Leaning into the swim; upright to fight.
		var moving = MathX.Clamp( velocity.Length() / Speed, 0f, 1f );
		var wantLean = mode == Mode.Swim ? -0.55f * moving : -0.12f;
		lean = MathX.Lerp( lean, wantLean, 1f - MathF.Exp( -4f * dt ) );
		body.Rotation = new Vector3( lean, 0f, MathF.Sin( t * 1.3f ) * 0.05f );
		body.Position = new Vector3( 0f, MathF.Sin( t * 1.9f ) * 0.04f, 0f );
		// The tail: a wave down it, curling up behind when it would touch the floor.
		var speed = 1f + moving;
		for ( var i = 0; i < tail.Count; i++ )
			tail[i].Rotation = new Vector3( MathF.Sin( t * 3f * speed - i * 0.55f ) * (0.12f + i * 0.025f) * (1f - curl * 0.6f) + 0.06f - curl * 0.21f, 0f, MathF.Sin( t * 1.4f - i * 0.4f ) * 0.05f );
		if ( tail.Count > 0 )
		{
			var tip = tail[^1].GlobalPosition + Vector3.Down * 0.5f * Scale;
			var ground = MathF.Max( floorY, Sculpt.GroundAt( tip ) );
			if ( tip.Y < ground + 0.12f ) curl = MathF.Min( 1f, curl + dt * 5f );
			else if ( tip.Y > ground + 0.6f ) curl = MathF.Max( 0f, curl - dt * 1.2f );
		}
		for ( var i = 0; i < hair.Count; i++ )
			hair[i].Rotation = new Vector3( 0.35f - lean * 0.6f + MathF.Sin( t * 2.1f + i ) * 0.12f, 0f, MathF.Sin( t * 1.7f + i * 0.8f ) * 0.08f );
		head.Rotation = new Vector3( -lean * 0.6f + MathF.Sin( t * 0.9f ) * 0.05f, MathF.Sin( t * 0.6f ) * 0.1f, 0f );

		// Arms (Euler YXZ like Poseidon's: Z swings an arm out from the side, X forward; elbows bend forward with +X).
		var k = 1f - MathF.Exp( -12f * dt );
		Vector3 rArm, rFore, lArm, lFore;
		var u = MathX.Clamp( (float)inMode / 0.4f, 0f, 1f );
		if ( Archer )
		{
			// The bow held out in the left hand, the right hand drawing the string back to the cheek.
			lArm = new Vector3( 1.45f, 0f, -0.15f );
			lFore = new Vector3( 0.05f, 0f, 0f );
			var draw = mode == Mode.Draw ? MathX.Clamp( (float)inMode / 0.6f, 0f, 1f ) : 0f;
			rArm = new Vector3( 0.4f, 0f, 0.35f ).Lerp( new Vector3( 1.4f, 0f, 0.2f ), MathF.Max( draw, 0.3f ) );
			rFore = new Vector3( 0.3f, 0f, 0f ).Lerp( new Vector3( 2.2f, 0f, 0f ), draw );
			weapon.Rotation = new Vector3( -1.45f, 0f, 0f );   // the bow upright, its back to the target
		}
		else
		{
			// The shield arm out in front, forearm forward, the shield gripped in the fist.
			lArm = new Vector3( 0.6f, 0f, -0.12f + MathF.Sin( t * 2f ) * 0.04f );
			lFore = new Vector3( 1.0f, 0f, 0f );
			rArm = new Vector3( 0.1f, 0f, 0.5f );
			rFore = new Vector3( 1.3f, 0f, 0f );
			if ( mode == Mode.WindUp ) { rArm = rArm.Lerp( new Vector3( -0.6f, 0f, 0.45f ), u ); rFore = rFore.Lerp( new Vector3( 1.8f, 0f, 0f ), u ); }
			if ( mode == Mode.Stab ) { var thrust = MathX.Clamp( (float)inMode / 0.12f, 0f, 1f ); rArm = new Vector3( -0.6f, 0f, 0.45f ).Lerp( new Vector3( 1.5f, 0f, 0.1f ), thrust ); rFore = new Vector3( 1.8f, 0f, 0f ).Lerp( Vector3.Zero, thrust ); }
		}
		armR.Rotation = armR.Rotation.Lerp( rArm, k );
		foreR.Rotation = foreR.Rotation.Lerp( rFore, k );
		armL.Rotation = armL.Rotation.Lerp( lArm, k );
		foreL.Rotation = foreL.Rotation.Lerp( lFore, k );

		if ( shield is not null && IsInstanceValid( shield ) && shield.GetParent() == this )
		{
			// In the left fist, upright, facing the way the body faces.
			var bodyBasis = body.GlobalBasis;
			shield.GlobalTransform = new Transform3D( bodyBasis, handL.GlobalPosition - bodyBasis.Z.Normalized() * 0.04f );
			foreach ( var cell in cells )
			{
				if ( cell.SinceHit > 0.35f ) { if ( cell.Lit ) { cell.Lit = false; cell.Material.SetShaderParameter( "flash", 0f ); } continue; }
				cell.Lit = true;
				cell.Material.SetShaderParameter( "flash", MathX.Clamp( 1f - (float)cell.SinceHit / 0.3f, 0f, 1f ) );
			}
		}

		if ( !Archer && IsInstanceValid( weapon ) && weapon.GetParent() == this )
		{
			// The trident in the right fist: upright at rest, levelled at the target to stab. Kept off the forearm.
			var forward = -GlobalBasis.Z.Normalized();
			var point = mode is Mode.Stab or Mode.WindUp ? (forward + Vector3.Down * 0.15f).Normalized() : (Vector3.Up + forward * 0.15f).Normalized();
			var forearm = (handR.GlobalPosition - foreR.GlobalPosition).SafeNormal();
			var side = point - forearm * point.Dot( forearm );
			if ( side.Length() < 0.5f ) point = (point + GlobalBasis.X.Normalized() * 0.5f).Normalized();
			weapon.GlobalTransform = new Transform3D( Shapes.LookAt( point, GlobalBasis.Z ).Scaled( Vector3.One * 0.95f ), handR.GlobalPosition + forward * 0.05f );
		}
	}

	// ---- Hits and death ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead ) return;
		if ( index == ShieldIndex && shield is not null )
		{
			// Which cell it hit (in the shield's own units): a whole one takes the hit and may break; a broken one
			// lets the shot through to whatever is behind it; the frame just stops it.
			var local = shield.GlobalTransform.AffineInverse() * position;
			var spot = new Vector2( local.X, local.Y );
			Cell hit = null;
			foreach ( var c in cells )
				if ( c.At.DistanceTo( spot ) < CellSize * 1.05f && (hit is null || c.At.DistanceSquaredTo( spot ) < hit.At.DistanceSquaredTo( spot )) ) hit = c;
			if ( hit is { Health: <= 0f } )
			{
				ShotThrough( damage, direction, position, origin, shooter, scoreHit );
				return;
			}
			Blocked++;
			velocity += direction.SafeNormal() * 0.4f;
			Fx.ImpactWorld( position, -direction.SafeNormal() );
			if ( hit is null ) { Sfx.Play( "hit_world", position, 0.5f, 1.4f ); return; }
			hit.Health -= damage;
			hit.SinceHit = 0f;
			hit.Material.SetShaderParameter( "hurt", MathX.Clamp( 1f - hit.Health / CellHealth, 0f, 1f ) );
			if ( hit.Health > 0f ) { Sfx.Play( "glass_hit", position, 0.6f, Rng.Float( 1.5f, 1.8f ) ); return; }
			// Shattered: the cell goes, with a burst of glowing shards.
			hit.Mesh.Visible = false;
			Sfx.Play( "glass_break", position, 0.7f, Rng.Float( 1.3f, 1.6f ) );
			for ( var n = 0; n < 4; n++ )
				gore?.ThrowBlob( position + Rng.InSphere( 0.05f ), -direction.SafeNormal() * 0.5f + Rng.InSphere( 1f ), new Color( 0.3f, 1f, 0.95f ), new Color( 0.3f, 1f, 0.95f ), Rng.Float( 0.6f, 1.2f ) );
			if ( cells.All( c => c.Health <= 0f ) ) guard.Collider.Disabled = true;   // nothing left to stop anything
			return;
		}
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		velocity += direction.SafeNormal() * 1.5f;
		var multiplier = index == 0 ? HeadMultiplier : index >= 3 ? TailMultiplier : 1f;
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * multiplier, Position = position, Origin = origin, Attacker = shooter } );
		gore?.GooBurst( position, -direction, GooColor, 2, 140f, 0.5f );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, index == 0, scoreHit );
	}

	/// <summary> A shot through a broken shield cell: on to the first thing behind the shield (the mermaid, most likely). </summary>
	void ShotThrough( float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		var dir = direction.SafeNormal();
		var behind = Trace.Ray( this, position + dir * 0.01f, position + dir * 8f, Layers.Creature, new Godot.Collections.Array<Rid> { guard.GetRid() } );
		if ( behind.Node is CreaturePart part && part != guard )
			part.ReceiveShot( damage, part, direction, behind.HitPosition, origin, shooter, scoreHit );
	}

	protected override void OnDied()
	{
		RaiseKilled();
		foreach ( var part in FindChildren( "*", "StaticBody3D", true, false ).OfType<CreaturePart>().ToList() ) part.QueueFree();
		var centre = chest.GlobalPosition;
		var away = lastShotDirection.SafeNormal();
		// Burst: the head, an arm, the weapon and the end of the tail fly off and float up; goo and lumps everywhere.
		void Fling( Node3D piece, float radius, float speed )
		{
			if ( piece is null || !IsInstanceValid( piece ) ) return;
			var dir = (piece.GlobalPosition - centre).SafeNormal();
			if ( dir == Vector3.Zero ) dir = Vector3.Up;
			var gib = Gib.Attach( piece, gore, radius / U.In, 30f, 2f, (dir + away * 0.4f + Vector3.Up * 0.4f).SafeNormal() * speed * Rng.Float( 0.7f, 1.2f ), GooColor );
			gib.AngularVelocity = Rng.InSphere( 8f );
			CreatureGore.AddStump( piece, piece.GlobalPosition, -dir, radius * 0.5f / U.In, Flesh[Rng.Int( 0, 2 )], GooColor, 5f );
		}
		if ( IsInstanceValid( weapon ) && weapon.GetParent() == this ) Fling( weapon, 0.25f, 6f );
		if ( shield is not null && IsInstanceValid( shield ) ) Fling( shield, 0.3f, 6f );
		if ( tail.Count > 4 ) Fling( tail[4], 0.2f, 6f );
		Fling( head, 0.2f, 8f );
		Fling( Rng.Float() < 0.5f ? armL : armR, 0.15f, 7f );
		Fling( chest, 0.3f, 4f );
		for ( var i = 0; i < 6; i++ )
			gore?.ThrowBlob( centre + Rng.InSphere( 0.4f ), Rng.InSphere( 1f ) + Vector3.Up * 0.5f + away * 0.3f, Flesh[i % 3], GooColor, Rng.Float( 3f, 6f ) );
		gore?.GooBurst( centre, away + Vector3.Up * 0.3f, GooColor, 24, 340f, 1.2f );
		gore?.Squelch( centre, 1f, 1.1f );
		QueueFree();
	}
}

/// <summary> A mermaid's arrow: flies straight with a little drop, hurts a player it passes close to, sticks in the world. </summary>
public partial class MermaidArrow : Node3D
{
	Vector3 velocity;
	float damage;
	bool stuck;
	TimeSince age, sinceStuck;
	Mermaid archer;
	static StandardMaterial3D wood, tip, feather;

	public static void Shoot( Mermaid archer, Vector3 from, Vector3 velocity, float damage )
	{
		var arrow = new MermaidArrow { Name = "Mermaid Arrow", velocity = velocity, damage = damage, archer = archer };
		archer.GetTree().CurrentScene.AddChild( arrow );
		arrow.GlobalPosition = from;
		arrow.age = 0f;
		arrow.Face();
	}

	public override void _Ready()
	{
		wood ??= Sculpt.Paint( new Color( 0.5f, 0.32f, 0.18f ), 0.7f );
		tip ??= Sculpt.Glow( new Color( 0.4f, 1f, 0.5f ), 1.8f );
		feather ??= Sculpt.Paint( new Color( 0.9f, 0.4f, 0.6f ), 0.6f );
		// Along -Z: the shaft, a glowing sea-glass head, pink fin feathers.
		Sculpt.Limb( this, new Vector3( 0f, 0f, 0.4f ), new Vector3( 0f, 0f, -0.4f ), 0.012f, 0.012f, wood, "Shaft" );
		Sculpt.Cone( this, new Vector3( 0f, 0f, -0.4f ), new Vector3( 0f, 0f, -0.52f ), 0.03f, tip, 4 );
		foreach ( var a in new[] { 0f, 2.1f, 4.2f } )
			Sculpt.Block( this, new Vector3( MathF.Cos( a ) * 0.025f, MathF.Sin( a ) * 0.025f, 0.33f ), new Vector3( 0.004f, 0.05f, 0.12f ), feather, new Basis( Vector3.Back, a + MathF.PI * 0.5f ) );
	}

	void Face()
	{
		var f = velocity.SafeNormal();
		if ( f != Vector3.Zero ) GlobalBasis = Shapes.LookAt( f );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( stuck )
		{
			if ( sinceStuck > 3f ) QueueFree();
			return;
		}
		if ( DevFlags.FreezeEnemies ) return;
		var from = GlobalPosition;
		velocity += Vector3.Down * 1.2f * dt;
		var to = from + velocity * dt;
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive ) continue;
			var chest = player.WorldPosition + Vector3.Up * 1f;
			var closest = Geometry3D.GetClosestPointToSegment( chest, from, to );
			if ( closest.DistanceTo( chest ) > 0.6f ) continue;
			player.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = IsInstanceValid( archer ) ? archer : this, Position = closest, Origin = from } );
			if ( IsInstanceValid( archer ) ) archer.ArrowHit();
			Sfx.Play( "hit_flesh", closest, 0.6f, 1.3f );
			QueueFree();
			return;
		}
		var hit = Trace.Ray( this, from, to, Layers.World );
		if ( hit.Hit )
		{
			stuck = true;
			sinceStuck = 0f;
			GlobalPosition = hit.HitPosition - velocity.SafeNormal() * 0.3f;
			return;
		}
		GlobalPosition = to;
		Face();
		if ( TridentBolt.UnderWater( to ) && Rng.Float() < 0.4f ) TridentBolt.Bubbles( from, to );
		if ( age > 4f ) QueueFree();
	}
}
