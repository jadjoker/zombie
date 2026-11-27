using Godot;

/// <summary>
/// The Blind Warden (user's pick, the Abyss's wave 25 boss): a huge pale cave lobster with no eyes that hunts by sound.
/// It stands still listening, clicking, its gill vents open and glowing down its sides (the weak spot, triple damage);
/// shut, its shell turns most shots. Gunfire, blasts, hard landings (<see cref="SeaStir"/>), sprinting, the jetpack and
/// dashes are noises it hears (<see cref="Hear"/>); a player walking is only felt up close. On a noise a ripple runs out
/// on the floor where it heard it, it turns and charges there, hurting whoever is in its way; a charge into rock (a wall,
/// the statue) crashes, shakes rubble down and leaves it dazed with its gills wide open. Up close in front it claps its
/// claws (a shock ring that stuns; jump it), behind it flicks its tail and throws rubble, and now and then it screams:
/// every player near is marked for <see cref="MarkTime"/> s and it charges them wherever they are. Built in code
/// (SmoothKit lofts, Sculpt shapes), metres, facing -Z, standing on the floor.
/// </summary>
public partial class BlindWarden : Creature
{
	public static readonly List<BlindWarden> All = new();
	public override bool CanScare => false;
	public override Color Blood => new( 0.35f, 0.62f, 1f );
	public override float AreaDamageScale => 0.6f;
	public override GibRecipe Gibs => GibRecipes.Crab( ShellColour, BellyColour, Blood );
	/// <summary> The gill vents are the weak spots. </summary>
	public override bool WeakSpot( int index ) => index is 2 or 3;

	public float WalkSpeed = 2.6f;
	public float ChargeSpeed = 15f;
	public float ChargeDamage = 40f;
	public float HearRange = 34f;        // a gunshot (a stir of radius 9) this far off is heard; bigger stirs further
	public float RunHearRange = 22f;     // sprinting, the jetpack, dashes
	public float FeelRange = 5.5f;       // a player walking this close is felt
	public float ClapRange = 6.5f;
	public float ClapDamage = 25f;
	public float ClapStun = 0.9f;
	public float ClapCooldown = 4f;
	public float FlickRange = 7f;
	public float FlickCooldown = 5f;
	public float RubbleDamage = 14f;
	public float ScreamEvery = 22f;
	public const float MarkTime = 5f;
	public float DazeTime = 3.6f;
	public float GillOpenMultiplier = 3f;
	public float GillShutMultiplier = 0.5f;
	public float ShellMultiplier = 0.4f;
	public float ClawMultiplier = 0.3f;
	public float TailMultiplier = 0.8f;
	public const float BodyHeight = 1.9f;

	/// <summary> Tests: what it has done. </summary>
	public static int Charges, ChargeHits, Crashes, Claps, Screams, Flicks, RubbleHits, Heard;
	/// <summary> The local player was marked by a scream until then (the HUD's warning). </summary>
	public static double LocalMarkedUntil;

	static readonly Color ShellColour = new( 0.86f, 0.83f, 0.78f );
	static readonly Color BellyColour = new( 0.93f, 0.78f, 0.76f );
	static readonly Color GillColour = new( 0.4f, 0.9f, 1f );

	public enum Act { Listen, Turn, Charge, Prowl, Daze, Clap, Scream, Flick }
	Act act = Act.Listen;
	TimeSince sinceAct;
	TimeSince sinceClap = 99f, sinceFlick = 99f, sinceScream, sinceHeard = 99f, sinceHit = 99f, sinceClick;
	Vector3 target, chargeDir;
	bool struck;
	int lastSeq;
	readonly HashSet<PlayerSurvivor> chargeHit = new();
	readonly Dictionary<PlayerSurvivor, double> marked = new();
	float gill, gillVel, clawOpen, clawVel, spread, spreadVel, rear, rearVel, curl, curlVel, gait, slump, slumpVel;
	Vector3 velocity;

	Node3D body, head;
	readonly List<Node3D> legs = new();
	readonly List<Node3D> tail = new();
	readonly List<Node3D> antennae = new();
	readonly List<Node3D> gillFlaps = new();
	Node3D[] shoulder = new Node3D[2], elbow = new Node3D[2], finger = new Node3D[2];
	ShaderMaterial shell;
	StandardMaterial3D gillGlow, feelerGlow, mawGlow, seamGlow, crackGlow;
	ShaderMaterial brain;
	Node3D brainNode;
	OmniLight3D brainLight;
	readonly List<Node3D> facePlates = new();
	float split, splitVel, sense, senseVel;
	const int FeelerPieces = 8;
	static readonly Color SenseGreen = new( 0.35f, 1f, 0.45f );
	OmniLight3D gillLight;

	/// <summary> The move name the HUD shows. </summary>
	public string Line { get; private set; } = "";
	public TimeSince TimeSinceLine { get; private set; } = 999f;
	void Say( string line ) { Line = line; TimeSinceLine = 0f; }

	public static BlindWarden Spawn( Node parent, Vector3 position, float maxHealth ) =>
		Place( new BlindWarden { Name = "Blind Warden" }, parent, position, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	protected override void Setup()
	{
		BeginSetup( 15000f );
		lastSeq = SeaStir.Seq;
		sinceScream = ScreamEvery * 0.5f;
		Build();
		GlobalPosition = GlobalPosition with { Y = Floor( GlobalPosition ) };
	}

	// ---- Looks ----

	static readonly Shader ShellShader = new()
	{
		Code = @"shader_type spatial;
uniform vec3 shell : source_color = vec3(0.86, 0.83, 0.78);
uniform vec3 deep : source_color = vec3(0.58, 0.5, 0.54);
uniform vec3 blush : source_color = vec3(0.95, 0.7, 0.68);
uniform vec3 rim_col : source_color = vec3(0.65, 0.85, 1.0);
uniform float flash = 0.0;
uniform float hurt = 0.0;
varying vec3 p;
float hash(vec3 q) { return fract(sin(dot(q, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
float noise(vec3 q) {
	vec3 i = floor(q); vec3 f = fract(q); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1,0,0)), f.x), mix(hash(i + vec3(0,1,0)), hash(i + vec3(1,1,0)), f.x), f.y),
		mix(mix(hash(i + vec3(0,0,1)), hash(i + vec3(1,0,1)), f.x), mix(hash(i + vec3(0,1,1)), hash(i + vec3(1,1,1)), f.x), f.y), f.z);
}
void vertex() { p = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz - NODE_POSITION_WORLD; }
void fragment() {
	float n = noise(p * 2.2) * 0.6 + noise(p * 6.0) * 0.3 + noise(p * 15.0) * 0.1;
	vec3 c = mix(shell, blush, smoothstep(0.55, 0.8, noise(p * 0.9 + 3.0)) * 0.5);
	// Pits and grooves of an old shell, darker in the dents.
	c = mix(c, deep, smoothstep(0.55, 0.75, n) * 0.55);
	c *= 0.85 + 0.15 * smoothstep(0.2, 0.6, n);
	c = mix(c, c * 0.45 + vec3(0.12, 0.18, 0.32), hurt * smoothstep(0.45, 0.7, noise(p * 3.0 + 9.0)));
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = mix(c, vec3(2.0), flash);
	ROUGHNESS = 0.55 - 0.25 * n;
	EMISSION = rim_col * rim * 0.45 + c * 0.07;
}",
	};

	static readonly Shader BrainShader = new()
	{
		Code = @"shader_type spatial;
uniform float beat = 0.0;
uniform float sense = 0.0;
varying vec3 lp;
void vertex() { lp = VERTEX; VERTEX += NORMAL * beat * 0.04; }
void fragment() {
	// Winding folds as dark grooves, the glow brightest in the ridges.
	vec2 q = vec2(lp.x * 3.0 + lp.y * 2.0, lp.z) * 26.0;
	float w = sin(q.x + sin(q.y * 0.9) * 2.2) + sin(q.y * 1.1 + sin(q.x * 1.3) * 2.0);
	float ridge = smoothstep(0.0, 0.6, abs(w));
	vec3 c = mix(vec3(0.05, 0.25, 0.08), vec3(0.45, 1.0, 0.5), ridge);
	ALBEDO = c * 0.5;
	ROUGHNESS = 0.2;
	SPECULAR = 0.7;
	EMISSION = c * (0.5 + beat * 0.5 + sense * 0.9) * ridge;
}",
	};

	void Build()
	{
		shell = new ShaderMaterial { Shader = ShellShader };
		var belly = Sculpt.Paint( BellyColour, 0.6f );
		var milk = new StandardMaterial3D { AlbedoColor = new Color( 0.92f, 0.94f, 0.96f ), Roughness = 0.08f, ClearcoatEnabled = true, Clearcoat = 1f, EmissionEnabled = true, Emission = new Color( 0.6f, 0.7f, 0.8f ), EmissionEnergyMultiplier = 0.3f };
		var dark = Sculpt.Paint( new Color( 0.12f, 0.05f, 0.08f ), 0.7f );
		gillGlow = new StandardMaterial3D { AlbedoColor = GillColour, EmissionEnabled = true, Emission = GillColour, EmissionEnergyMultiplier = 0.4f, Roughness = 0.3f };

		body = new Node3D { Name = "Body", Position = new Vector3( 0f, BodyHeight, 0f ) };
		AddChild( body );

		// The carapace: one long loft, the rostrum spike out the front, a ridge down the top.
		var rings = SmoothKit.Smooth( new List<(float, float, float, float)>
		{
			(-1.5f, 0.85f, 0.75f, 0.05f), (-0.9f, 1.15f, 0.98f, 0.1f), (0f, 1.28f, 1.06f, 0.14f), (0.9f, 1.12f, 0.95f, 0.1f),
			(1.6f, 0.78f, 0.68f, 0f), (2.05f, 0.42f, 0.36f, -0.06f), (2.25f, 0.06f, 0.06f, -0.08f),
		}, 4 );
		var hull = SmoothKit.Put( body, SmoothKit.Cached( "warden carapace", () => SmoothKit.Loft( rings, 0f, 1f, 32 ) ), shell, "Carapace" );
		hull.Basis = new Basis( Vector3.Right, -MathF.PI / 2f );
		Sculpt.Cone( body, new Vector3( 0f, 0.05f, -2.1f ), new Vector3( 0f, 0.3f, -3.1f ), 0.22f, shell, 8, "Rostrum" );
		for ( var i = 0; i < 7; i++ )
		{
			if ( i is >= 3 and <= 5 ) continue;   // the brain window
			var z = -1.6f + i * 0.45f;
			Sculpt.Cone( body, new Vector3( 0f, 1.0f - MathF.Abs( z + 0.2f ) * 0.12f, z ), new Vector3( 0f, 1.32f - MathF.Abs( z + 0.2f ) * 0.12f, z + 0.15f ), 0.1f, shell, 6, "Ridge Spine" );
		}
		// The groove across the back where the head meets the body, and the belly plates underneath.
		SmoothKit.Ball( body, new Vector3( 0f, -0.55f, -0.1f ), new Vector3( 1.9f, 0.8f, 3.2f ), belly, null, "Belly" );

		// The head (the alien pass, user's picks): long glowing feelers that swing to a sound, four plates over the face
		// that split open into jaws when it screams, and down each side a row of small alien eyes sealed shut and scarred over.
		head = Sculpt.Joint( body, new Vector3( 0f, 0f, -1.7f ), "Head" );
		feelerGlow = new StandardMaterial3D { AlbedoColor = ShellColour, EmissionEnabled = true, Emission = SenseGreen, EmissionEnergyMultiplier = 0.4f, Roughness = 0.4f };
		foreach ( var x in new[] { -1f, 1f } )
		{
			var a = Sculpt.Joint( head, new Vector3( x * 0.22f, 0.2f, -0.6f ), x < 0f ? "Feeler L" : "Feeler R" );
			antennae.Add( a );
			var parent = a;
			var dir = new Vector3( x * 0.6f, 0.3f, -0.8f ).Normalized();
			for ( var k = 0; k < FeelerPieces; k++ )
			{
				var step = dir * (1.3f - k * 0.08f);
				SmoothKit.Tube( parent, Vector3.Zero, step, 0.055f - k * 0.0045f, 0.05f - k * 0.0045f, feelerGlow, "Feeler Piece" );
				var next = Sculpt.Joint( parent, step, "Feeler Joint" );
				antennae.Add( next );
				parent = next;
				dir = (dir + new Vector3( x * 0.1f, -0.06f, 0.06f )).Normalized();
			}
			SmoothKit.Ball( parent, Vector3.Zero, Vector3.One * 0.09f, feelerGlow, null, "Feeler Tip" );
			for ( var k = 0; k < 3; k++ )
				SmoothKit.Tube( head, new Vector3( x * (0.12f + k * 0.08f), -0.3f, -0.55f ), new Vector3( x * (0.14f + k * 0.1f), -0.75f, -0.75f - k * 0.05f ), 0.05f, 0.03f, belly, "Mouth Part" );
		}
		Sculpt.Blob( head, new Vector3( 0f, -0.25f, -0.55f ), new Vector3( 0.4f, 0.25f, 0.2f ), dark, name: "Mouth" );
		// The face: a glowing green maw ringed with teeth behind four plates set in an X (the rostrum rides between the
		// top two). Shut they close into a blunt point; screaming they split wide.
		mawGlow = new StandardMaterial3D { AlbedoColor = new Color( 0.1f, 0.3f, 0.12f ), EmissionEnabled = true, Emission = SenseGreen, EmissionEnergyMultiplier = 0.6f };
		var fang = Sculpt.Paint( new Color( 0.95f, 0.93f, 0.85f ), 0.3f );
		Sculpt.Blob( head, new Vector3( 0f, 0.05f, -0.5f ), new Vector3( 0.6f, 0.55f, 0.2f ), mawGlow, name: "Maw" );
		for ( var i = 0; i < 10; i++ )
		{
			var a = i / 10f * MathF.Tau;
			var at = new Vector3( MathF.Cos( a ) * 0.3f, 0.05f + MathF.Sin( a ) * 0.28f, -0.55f );
			Sculpt.Cone( head, at, at * new Vector3( 0.4f, 1f, 1f ) + new Vector3( 0f, 0.05f - MathF.Sin( a ) * 0.18f, -0.12f ), 0.04f, fang, 5, "Maw Tooth" );
		}
		for ( var k = 0; k < 4; k++ )
		{
			var turn = MathF.PI / 4f + k * MathF.PI / 2f;
			var hinge = new Node3D { Name = "Face Plate Hinge", Basis = new Basis( Vector3.Back, turn ) };
			head.AddChild( hinge );
			hinge.Position = new Vector3( -MathF.Sin( turn ) * 0.36f, 0.05f + MathF.Cos( turn ) * 0.32f, -0.4f );
			var plate = new Node3D { Name = "Face Plate" };
			hinge.AddChild( plate );
			SmoothKit.Ball( plate, new Vector3( 0f, 0f, -0.42f ), new Vector3( 0.5f, 0.16f, 0.95f ), shell, null, "Plate" );
			for ( var i = 0; i < 3; i++ )
				Sculpt.Cone( plate, new Vector3( (i - 1) * 0.13f, -0.06f, -0.3f - i % 2 * 0.2f ), new Vector3( (i - 1) * 0.1f, -0.3f, -0.42f - i % 2 * 0.2f ), 0.035f, fang, 5, "Plate Fang" );
			facePlates.Add( plate );
		}
		// The sealed eyes: four a side on the front of the carapace, each a slanted almond under a lid of shell grown
		// shut, a dark seam, pale scars across it and a faint green light leaking out that flares when it hears.
		var scar = Sculpt.Paint( new Color( 0.85f, 0.5f, 0.52f ), 0.7f );
		var black = new StandardMaterial3D { AlbedoColor = new Color( 0.01f, 0.01f, 0.015f ), Roughness = 0.06f, ClearcoatEnabled = true, Clearcoat = 1f };
		seamGlow = new StandardMaterial3D { AlbedoColor = SenseGreen, EmissionEnabled = true, Emission = SenseGreen, EmissionEnergyMultiplier = 0.5f };
		var rows = new[] { (-2.0f, 0.5f, 0.43f, -0.05f, 0.15f), (-1.78f, 0.66f, 0.57f, -0.02f, 0.18f), (-1.54f, 0.8f, 0.69f, 0f, 0.2f), (-1.3f, 0.92f, 0.79f, 0.03f, 0.22f) };
		foreach ( var x in new[] { -1f, 1f } )
			foreach ( var (z, rx, rz, lift, size) in rows )
			{
				var normal = new Vector3( x * 0.8f / rx, 0.6f / rz, 0f ).Normalized();
				var eye = new Node3D { Name = "Sealed Eye", Position = new Vector3( x * rx * 0.8f, lift + rz * 0.6f, z ) + normal * size * 0.12f };
				body.AddChild( eye );
				eye.Basis = new Basis( new Quaternion( Vector3.Back, normal ) ) * new Basis( Vector3.Back, -x * MathX.Rad( 38f ) );
				// The almond, then two lids of shell grown together over it, leaving a dark slit that leaks green.
				Sculpt.Blob( eye, Vector3.Zero, new Vector3( 0.55f, 1.1f, 0.5f ) * size, black, name: "Almond" );
				SmoothKit.Ball( eye, new Vector3( 0.03f, 0f, 0.04f ) * size, new Vector3( 0.42f, 1.15f, 0.48f ) * size, shell, null, "Lid" );
				SmoothKit.Ball( eye, new Vector3( -0.03f, 0f, 0.04f ) * size, new Vector3( 0.42f, 1.15f, 0.48f ) * size, shell, null, "Lid" );
				Sculpt.Blob( eye, new Vector3( 0f, 0f, 0.27f * size ), new Vector3( 0.07f, 0.95f, 0.06f ) * size, seamGlow, name: "Seam" );
				Sculpt.Blob( eye, new Vector3( 0f, 0.1f * size, 0.27f * size ), new Vector3( 0.6f, 0.07f, 0.06f ) * size, scar, Basis.FromEuler( new Vector3( 0f, 0f, 0.5f ) ), "Scar" );
				Sculpt.Blob( eye, new Vector3( 0f, -0.2f * size, 0.26f * size ), new Vector3( 0.45f, 0.06f, 0.05f ) * size, scar, Basis.FromEuler( new Vector3( 0f, 0f, -0.3f ) ), "Scar" );
			}

		// The brain: a glowing green lump of folds under see-through plates in a window in the back of the shell,
		// showing through. It throbs, and quickens when it hears.
		brain = new ShaderMaterial { Shader = BrainShader };
		var glass = new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.75f, 0.9f, 0.95f, 0.22f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f,
			Metallic = 0.3f, RimEnabled = true, Rim = 0.6f, CullMode = BaseMaterial3D.CullModeEnum.Back,
		};
		brainNode = new Node3D { Name = "Brain", Position = new Vector3( 0f, 1.17f, 0.2f ) };
		body.AddChild( brainNode );
		SmoothKit.Ball( brainNode, new Vector3( -0.17f, 0f, 0f ), new Vector3( 0.45f, 0.4f, 1.25f ), brain, null, "Brain Lobe" );
		SmoothKit.Ball( brainNode, new Vector3( 0.17f, 0f, 0f ), new Vector3( 0.45f, 0.4f, 1.25f ), brain, null, "Brain Lobe" );
		SmoothKit.Ball( brainNode, new Vector3( 0f, -0.05f, 0.55f ), new Vector3( 0.4f, 0.3f, 0.4f ), brain, null, "Brain Stem" );
		for ( var k = 0; k < 3; k++ )
		{
			var z = -0.4f + k * 0.42f;
			var pane = SmoothKit.Ball( body, new Vector3( 0f, 1.25f - MathF.Abs( z - 0.2f ) * 0.08f, z + 0.2f ), new Vector3( 1.0f, 0.42f, 0.5f ), glass, null, "Brain Pane" );
			pane.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		}
		// Cracks in the shell running out from the window, glowing with the same light.
		crackGlow = new StandardMaterial3D { AlbedoColor = new Color( 0.1f, 0.35f, 0.15f ), EmissionEnabled = true, Emission = SenseGreen, EmissionEnergyMultiplier = 0.3f };
		var crackRandom = new Random( 25 );
		for ( var k = 0; k < 9; k++ )
		{
			var a = k / 9f * MathF.Tau + (float)crackRandom.NextDouble() * 0.4f;
			var from = new Vector2( MathF.Cos( a ) * 0.5f, MathF.Sin( a ) * 0.75f );
			for ( var j = 0; j < 3; j++ )
			{
				var bend = a + ((float)crackRandom.NextDouble() - 0.5f) * 0.9f;
				var len = 0.22f + (float)crackRandom.NextDouble() * 0.2f;
				var to = from + new Vector2( MathF.Cos( bend ), MathF.Sin( bend ) * 1.4f ) * len;
				var mid = (from + to) / 2f;
				var r = new Vector2( mid.X / 1.28f, mid.Y / 1.6f ).Length();
				if ( r > 0.85f ) break;
				var y = 0.1f + 1.06f * MathF.Sqrt( MathF.Max( 0.05f, 1f - r * r ) );
				var dir = to - from;
				Sculpt.Blob( body, new Vector3( mid.X, y, mid.Y + 0.2f ), new Vector3( 0.025f, 0.03f, dir.Length() + 0.03f ), crackGlow,
					new Basis( Vector3.Up, MathF.Atan2( dir.X, dir.Y ) ) * new Basis( Vector3.Right, -mid.Y * 0.5f ) * Basis.FromEuler( new Vector3( 0f, 0f, -mid.X * 0.7f ) ), "Crack" );
				from = to;
			}
		}
		brainLight = new OmniLight3D { Name = "Brain Light", LightColor = SenseGreen, LightEnergy = 0.2f, OmniRange = 2.2f, ShadowEnabled = false, Position = new Vector3( 0f, 1.35f, 0.2f ) };
		body.AddChild( brainLight );

		// The gill vents: three glowing slits down each side, each under a flap of shell that lifts while it listens.
		foreach ( var x in new[] { -1f, 1f } )
		{
			for ( var k = 0; k < 3; k++ )
			{
				var at = new Vector3( x * 1.18f, -0.05f - k * 0.05f, -0.6f + k * 0.5f );
				var slit = Sculpt.Blob( body, at, new Vector3( 0.12f, 0.28f, 0.42f ), gillGlow, name: "Gill" );
				var flap = Sculpt.Joint( body, at + new Vector3( 0f, 0.2f, 0f ), "Gill Flap" );
				Sculpt.Blob( flap, new Vector3( x * 0.06f, -0.15f, 0f ), new Vector3( 0.12f, 0.34f, 0.5f ), shell, name: "Flap" );
				flap.SetMeta( "side", x );
				gillFlaps.Add( flap );
			}
			var part = CreaturePart.Create( this, x < 0f ? 2 : 3, x < 0f ? "Warden Gills L" : "Warden Gills R", 0.65f );
			body.AddChild( part );
			part.Position = new Vector3( x * 1.15f, -0.1f, -0.1f );
		}
		gillLight = new OmniLight3D { Name = "Gill Light", LightColor = GillColour, LightEnergy = 0.4f, OmniRange = 6f, ShadowEnabled = false, Position = new Vector3( 0f, -0.2f, 0f ) };
		body.AddChild( gillLight );

		// The claws: a shoulder, an arm, a huge pincer with a finger that opens.
		for ( var side = 0; side < 2; side++ )
		{
			var x = side == 0 ? -1f : 1f;
			shoulder[side] = Sculpt.Joint( body, new Vector3( x * 0.85f, -0.3f, -1.5f ), "Shoulder" );
			SmoothKit.Tube( shoulder[side], Vector3.Zero, new Vector3( x * 0.9f, 0.15f, -0.9f ), 0.32f, 0.28f, shell, "Upper Arm" );
			elbow[side] = Sculpt.Joint( shoulder[side], new Vector3( x * 0.9f, 0.15f, -0.9f ), "Elbow" );
			SmoothKit.Tube( elbow[side], Vector3.Zero, new Vector3( -x * 0.2f, 0f, -1.2f ), 0.28f, 0.34f, shell, "Forearm" );
			var wrist = Sculpt.Joint( elbow[side], new Vector3( -x * 0.2f, 0f, -1.2f ), "Wrist" );
			SmoothKit.Ball( wrist, new Vector3( 0f, 0f, -0.6f ), new Vector3( 0.95f, 0.75f, 1.5f ), shell, null, "Palm" );
			// The fixed finger and the moving one, toothed along the inside.
			SmoothKit.Tube( wrist, new Vector3( -x * 0.15f, -0.12f, -1.1f ), new Vector3( -x * 0.1f, -0.2f, -2.2f ), 0.24f, 0.08f, shell, "Fixed Finger" );
			finger[side] = Sculpt.Joint( wrist, new Vector3( x * 0.2f, 0.15f, -1.1f ), "Finger" );
			SmoothKit.Tube( finger[side], Vector3.Zero, new Vector3( -x * 0.05f, 0.05f, -1.1f ), 0.22f, 0.07f, shell, "Moving Finger" );
			for ( var k = 0; k < 4; k++ )
			{
				Sculpt.Cone( finger[side], new Vector3( -x * 0.12f, -0.05f, -0.25f - k * 0.22f ), new Vector3( -x * 0.32f, -0.08f, -0.3f - k * 0.22f ), 0.05f, belly, 5, "Claw Tooth" );
				Sculpt.Cone( wrist, new Vector3( -x * 0.05f, -0.05f, -1.35f - k * 0.2f ), new Vector3( x * 0.12f, 0f, -1.4f - k * 0.2f ), 0.05f, belly, 5, "Claw Tooth" );
			}
			var clawPart = CreaturePart.Create( this, side == 0 ? 5 : 6, side == 0 ? "Warden Claw L" : "Warden Claw R", 0.85f );
			wrist.AddChild( clawPart );
			clawPart.Position = new Vector3( 0f, 0f, -0.8f );
		}

		// Eight walking legs: a hip that swings, a thigh up and out, a shin down to the floor.
		for ( var k = 0; k < 8; k++ )
		{
			var x = k < 4 ? -1f : 1f;
			var z = -0.9f + (k % 4) * 0.6f;
			var hip = Sculpt.Joint( body, new Vector3( x * 0.95f, -0.45f, z ), "Hip" );
			var knee = new Vector3( x * 1.05f, 0.35f, 0f );
			SmoothKit.Tube( hip, Vector3.Zero, knee, 0.13f, 0.11f, shell, "Thigh" );
			SmoothKit.Tube( hip, knee, new Vector3( x * 1.75f, -BodyHeight + 0.45f, (k % 4 - 1.5f) * 0.2f ), 0.11f, 0.04f, shell, "Shin" );
			hip.SetMeta( "side", x );
			legs.Add( hip );
		}

		// The tail: five plated segments that curl, and a fan of five blades.
		Node3D link = Sculpt.Joint( body, new Vector3( 0f, 0.05f, 1.45f ), "Tail" );
		for ( var k = 0; k < 5; k++ )
		{
			var r = MathX.Lerp( 0.95f, 0.55f, k / 4f );
			SmoothKit.Ball( link, new Vector3( 0f, 0f, 0.4f ), new Vector3( r * 2.1f, r * 1.4f, 0.95f ), shell, null, "Tail Plate" );
			SmoothKit.Ball( link, new Vector3( 0f, -r * 0.35f, 0.4f ), new Vector3( r * 1.7f, r * 0.8f, 0.85f ), belly, null, "Tail Belly" );
			tail.Add( link );
			if ( k == 2 )
			{
				var tailPart = CreaturePart.Create( this, 4, "Warden Tail", 0.95f );
				link.AddChild( tailPart );
				tailPart.Position = new Vector3( 0f, 0f, 0.4f );
			}
			link = Sculpt.Joint( link, new Vector3( 0f, 0f, 0.75f ), "Tail Link" );
		}
		for ( var k = 0; k < 5; k++ )
		{
			var a = (k - 2f) * 0.42f;
			SmoothKit.Fin( link, new Vector3( 0f, 0f, 0.1f ), SmoothKit.Aim( new Vector3( MathF.Sin( a ), 0f, MathF.Cos( a ) ), Vector3.Up ), 1.0f, 0.55f, shell, 0.05f );
		}
		tail.Add( link );

		// What else can be shot: the head (0) and the carapace (1).
		var headPart = CreaturePart.Create( this, 0, "Warden Head", 0.95f );
		head.AddChild( headPart );
		headPart.Position = new Vector3( 0f, 0.15f, -0.2f );
		var bodyPart = CreaturePart.Create( this, 1, "Warden Carapace", 1.3f );
		body.AddChild( bodyPart );
		bodyPart.Position = new Vector3( 0f, 0.1f, 0.1f );
	}

	// ---- Every frame ----

	/// <summary> How open the gills are (0 shut, 1 listening, more while dazed). </summary>
	public float GillsOpen => gill;
	public bool GillsVulnerable => gill > 0.5f;
	public Act Doing => act;
	public Vector3 Target => target;
	/// <summary> The middle of its pincers, in the world. </summary>
	public Vector3 FrontAt => GlobalTransform * new Vector3( 0f, 1.2f, -4.2f );

	public override void _Process( double delta )
	{
		if ( health is null || body is null ) return;
		var dt = MathF.Min( (float)delta, 0.1f );
		var t = (float)Clock.Now;
		var a = (float)sinceAct;

		// What the move wants of the body.
		var gillWant = act switch { Act.Listen => 1f, Act.Daze => 1.4f, Act.Prowl => 0.2f, _ => 0f };
		var clawWant = act switch { Act.Clap => a < 0.7f ? 1f : 0f, Act.Charge => 0.6f, Act.Prowl => 0.3f + 0.3f * MathF.Sin( t * 3f ), _ => 0.15f };
		var spreadWant = act switch { Act.Clap => a < 0.7f ? 1f : -0.35f, Act.Charge => -0.2f, Act.Daze => 0.5f, _ => 0f };
		var rearWant = act switch { Act.Scream => a < 1.5f ? 1f : 0f, Act.Turn => 0.15f, Act.Charge => -0.25f, _ => 0f };
		var curlWant = act switch { Act.Flick => a < 0.45f ? 1f : -0.6f, Act.Charge => 0.25f, _ => 0.1f + 0.05f * MathF.Sin( t * 0.8f ) };
		var slumpWant = act == Act.Daze ? 1f : 0f;
		SmoothKit.Spring( ref gill, ref gillVel, gillWant, 1.6f, 0.7f, dt );
		SmoothKit.Spring( ref clawOpen, ref clawVel, clawWant, act == Act.Clap && a >= 0.7f ? 7f : 2.5f, 0.55f, dt );
		SmoothKit.Spring( ref spread, ref spreadVel, spreadWant, act == Act.Clap && a >= 0.7f ? 5f : 2f, 0.5f, dt );
		SmoothKit.Spring( ref rear, ref rearVel, rearWant, 1.5f, 0.6f, dt );
		SmoothKit.Spring( ref curl, ref curlVel, curlWant, act == Act.Flick && a >= 0.45f ? 5f : 1.8f, 0.45f, dt );
		SmoothKit.Spring( ref slump, ref slumpVel, slumpWant, 1.2f, 0.7f, dt );
		var splitWant = act switch { Act.Scream => a < 1.6f ? 1f : 0f, Act.Clap => a < 0.7f ? 0.3f : 0f, Act.Charge => 0.15f, Act.Daze => 0.45f, _ => 0f };
		SmoothKit.Spring( ref split, ref splitVel, splitWant, act == Act.Scream ? 3.5f : 2f, 0.45f, dt );
		var heard = (float)sinceHeard;
		SmoothKit.Spring( ref sense, ref senseVel, heard < 1.2f ? 1f : act == Act.Listen ? 0.25f : 0f, 3f, 0.7f, dt );

		Pose( t, dt );
		var flash = MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f );
		shell.SetShaderParameter( "flash", flash );
		shell.SetShaderParameter( "hurt", 1f - health.Fraction );
		var glow = MathX.Clamp( gill, 0f, 1.5f );
		gillGlow.EmissionEnergyMultiplier = 0.3f + glow * (3.2f + 0.8f * MathF.Sin( t * 6f ));
		gillLight.LightEnergy = 0.2f + glow * 1.8f;
		var heardGlow = MathX.Clamp( sense, 0f, 1.2f );
		feelerGlow.EmissionEnergyMultiplier = 0.2f + heardGlow * (1f + MathF.Sin( t * 18f ) * 0.3f);
		seamGlow.EmissionEnergyMultiplier = 0.3f + heardGlow * 2.5f;
		mawGlow.EmissionEnergyMultiplier = 0.3f + split * 3.5f;
		var beat = MathF.Pow( MathF.Max( 0f, MathF.Sin( t * (2.2f + heardGlow * 5f) ) ), 6f );
		brain.SetShaderParameter( "beat", beat );
		crackGlow.EmissionEnergyMultiplier = 0.25f + beat * 0.3f + heardGlow * 0.6f;
		brain.SetShaderParameter( "sense", heardGlow );
		brainLight.LightEnergy = 0.15f + beat * 0.2f + heardGlow * 0.4f;

		if ( IsDead ) return;
		if ( Puppet ) { PuppetFeet( dt ); return; }
		if ( Frozen ) return;
		Hear();
		Think( dt );
	}

	void Pose( float t, float dt )
	{
		var speed = velocity.Flat().Length();
		gait += dt * (1.2f + speed * 1.1f);
		body.Position = new Vector3( 0f, BodyHeight + rear * 0.6f - slump * 0.6f + MathF.Sin( gait * 2f ) * 0.04f * MathF.Min( 1f, speed ), 0f );
		body.Rotation = new Vector3( rear * 0.35f - slump * 0.12f, 0f, slump * 0.15f + MathF.Sin( gait ) * 0.02f * MathF.Min( 1f, speed ) );
		head.Rotation = new Vector3( act == Act.Scream ? rear * 0.4f : 0f, MathF.Sin( t * 0.7f ) * 0.08f, 0f );
		for ( var i = 0; i < legs.Count; i++ )
		{
			var side = (float)legs[i].GetMeta( "side" );
			var phase = gait * 2f + (i % 4) * 1.6f + (side < 0f ? 0f : MathF.PI);
			var step = MathF.Min( 1f, speed / 3f + (act == Act.Listen ? 0f : 0.1f) );
			legs[i].Rotation = new Vector3( 0f, MathF.Sin( phase ) * 0.35f * step, side * (MathF.Max( 0f, MathF.Cos( phase ) ) * 0.25f * step - slump * 0.2f) );
		}
		for ( var i = 0; i < tail.Count; i++ )
			tail[i].Rotation = new Vector3( -curl * (i == 0 ? 0.25f : 0.38f) + MathF.Sin( t * 1.1f - i * 0.6f ) * 0.03f, MathF.Sin( t * 0.6f - i * 0.4f ) * 0.04f, 0f );
		for ( var s = 0; s < 2; s++ )
		{
			var x = s == 0 ? -1f : 1f;
			shoulder[s].Rotation = new Vector3( -rear * 0.5f - slump * -0.3f, -x * spread * 0.7f, 0f );
			elbow[s].Rotation = new Vector3( 0f, x * spread * 0.5f, 0f );
			finger[s].Rotation = new Vector3( 0f, -x * clawOpen * 0.55f, 0f );
		}
		// The feelers sweep for sound while it listens and swing to point at a sound it hears; they trail back in a charge.
		var per = antennae.Count / 2;
		for ( var i = 0; i < antennae.Count; i++ )
		{
			var side = i < per ? -1f : 1f;
			var k = i % per;
			var sweep = act == Act.Listen ? MathF.Sin( t * 1.3f + side ) * 0.3f : 0.05f * MathF.Sin( t * 2f + k );
			var back = act == Act.Charge ? 0.25f : 0f;
			var pitch = 0f;
			if ( k == 0 && sense > 0.05f )
			{
				var local = head.GlobalTransform.AffineInverse() * target;
				var yaw = MathF.Atan2( -local.X, -local.Z ) - MathF.Atan2( -side * 0.6f, 0.8f );
				sweep = MathX.Lerp( sweep, MathX.Clamp( yaw, -0.7f, 0.7f ), MathX.Clamp( sense, 0f, 1f ) );
				pitch = -0.25f * sense;
			}
			var quiver = sense * 0.05f * MathF.Sin( t * 23f + i );
			antennae[i].Rotation = new Vector3( MathF.Sin( t * 1.7f - k * 0.7f ) * 0.05f + back * (k == 0 ? 3f : 0.3f) + pitch + quiver, (k == 0 ? sweep : sweep * 0.12f) + side * back + quiver, 0f );
		}
		// The face plates split open into four jaws.
		for ( var k = 0; k < facePlates.Count; k++ )
			facePlates[k].Rotation = new Vector3( -0.42f + split * 1.25f + MathF.Sin( t * 30f + k ) * 0.04f * split, 0f, 0f );
		brainNode.Scale = Vector3.One * (1f + MathF.Pow( MathF.Max( 0f, MathF.Sin( t * (2.2f + sense * 5f) ) ), 6f ) * 0.06f);
		foreach ( var flap in gillFlaps )
		{
			var side = (float)flap.GetMeta( "side" );
			flap.Rotation = new Vector3( 0f, 0f, side * MathX.Clamp( gill, 0f, 1.4f ) * 0.9f * (0.85f + 0.15f * MathF.Sin( t * 5f + flap.Position.Z * 4f )) );
		}
	}

	float Floor( Vector3 at )
	{
		var g = Sculpt.FloorBelow( this, at with { Y = at.Y + 3f }, 12f );
		return g == float.MinValue ? at.Y : g;
	}

	// ---- Hearing ----

	/// <summary>
	/// What it heard since last frame: gunshots, blasts and landings stir the sea (SeaStir); players sprinting, on the
	/// jetpack or dashing are loud; a scream's mark gives a player away wherever they are; anyone this close is felt.
	/// </summary>
	void Hear()
	{
		var me = GlobalPosition;
		Vector3? best = null;
		var bestScore = float.MaxValue;
		void Offer( Vector3 at, float distance )
		{
			if ( distance < bestScore ) { bestScore = distance; best = at; }
		}
		foreach ( var s in SeaStir.Recent )
		{
			if ( s.Seq <= lastSeq ) continue;
			var d = s.At.DistanceTo( me );
			if ( d < 5f ) continue;   // its own crashes and steps
			var reach = HearRange * MathX.Clamp( s.Radius / 9f, 0.6f, 2f );
			if ( d < reach ) Offer( s.At, d * 0.8f );   // a sound counts a little more than a footfall the same distance off
		}
		lastSeq = SeaStir.Seq;
		var now = Clock.Now;
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c ) continue;
			var at = p.RealPosition;
			var d = at.DistanceTo( me );
			if ( marked.TryGetValue( p, out var until ) && now < until ) { Offer( at, d * 0.5f ); continue; }
			if ( d < FeelRange ) { Offer( at, d * 0.5f ); continue; }
			if ( d > RunHearRange ) continue;
			var move = c.GetNodeOrNull<PlayerMovement>( "PlayerMovement" );
			var jet = c.GetNodeOrNull<Jetpack>( "Jetpack" );
			var loud = (move is not null && (move.IsDashing || (move.IsSprinting && c.Velocity.Flat().Length() > 3f))) || (jet is not null && (jet.Thrusting || jet.Surging));
			if ( loud ) Offer( at, d );
		}
		if ( best is { } heard )
		{
			target = heard;
			sinceHeard = 0f;
			Heard++;
		}
	}

	// ---- The brain ----

	void Think( float dt )
	{
		var a = (float)sinceAct;
		var wish = Vector3.Zero;
		var prey = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		switch ( act )
		{
			case Act.Listen:
			{
				if ( sinceClick > 1.4f ) { sinceClick = 0f; Sfx.Play( "warden_listen", GlobalPosition + Vector3.Up * 2f, 0.7f, Rng.Float( 0.9f, 1.1f ) ); }
				if ( a < 1.1f ) break;   // a moment of listening first: the window to hit the gills
				if ( prey is not null && sinceScream > ScreamEvery && prey.RealPosition.DistanceTo( GlobalPosition ) < 45f ) { Begin( Act.Scream ); break; }
				if ( prey is not null && CloseMove( prey ) ) break;
				if ( sinceHeard < 1.5f ) { Begin( Act.Turn ); break; }
				if ( a > 7f ) { target = Wander(); Begin( Act.Prowl ); }
				break;
			}
			case Act.Turn:
			{
				var to = (target - GlobalPosition).Flat();
				Face( to, 5f, dt );
				if ( a > 0.65f && to.Length() > 2f )
				{
					chargeDir = to.Normalized();
					Begin( Act.Charge );
				}
				else if ( a > 0.65f ) Begin( Act.Listen );
				break;
			}
			case Act.Charge:
			{
				// Mostly straight at where it heard the noise, steering a little if it hears more on the way.
				var to = (target - GlobalPosition).Flat();
				if ( to.Length() > 3f && sinceHeard < 0.3f ) chargeDir = chargeDir.Slerp( to.Normalized(), MathX.Clamp( dt * 0.8f, 0f, 1f ) ).Normalized();
				Face( chargeDir, 6f, dt );
				wish = chargeDir * ChargeSpeed * MathX.Clamp( a / 0.35f, 0.2f, 1f );
				ChargeContact();
				// Into rock: a crash.
				var from = GlobalPosition + Vector3.Up * 1.4f;
				var wall = Trace.Ray( this, from, from + chargeDir * 4.6f, Layers.World );
				if ( wall.Hit && a > 0.15f ) { Crash( wall.HitPosition ); break; }
				if ( to.Dot( chargeDir ) < -3f || a > 3.2f ) { target = GlobalPosition + chargeDir * 4f; Begin( Act.Prowl ); }
				break;
			}
			case Act.Prowl:
			{
				// A slow walk, claws sweeping, then it stops to listen again.
				var to = (target - GlobalPosition).Flat();
				if ( to.Length() > 1.5f ) { Face( to, 1.5f, dt ); wish = -GlobalBasis.Z.Flat().Normalized() * WalkSpeed; }
				var from = GlobalPosition + Vector3.Up * 1.4f;
				if ( Trace.Ray( this, from, from - GlobalBasis.Z.Flat().Normalized() * 5f, Layers.World ).Hit ) target = Wander();
				if ( prey is not null && CloseMove( prey ) ) break;
				if ( sinceHeard < 0.5f && a > 0.6f ) { Begin( Act.Turn ); break; }
				if ( a > 2.5f || to.Length() < 1.5f ) Begin( Act.Listen );
				break;
			}
			case Act.Daze:
				if ( a > DazeTime ) Begin( Act.Listen );
				break;
			case Act.Clap:
				if ( !struck && a >= 0.7f )
				{
					struck = true;
					Claps++;
					Sfx.Play( "warden_clap", FrontAt, 1f, Rng.Float( 0.9f, 1.05f ) );
					var at = FrontAt with { Y = Floor( FrontAt ) };
					ShockRing.Send( GetTree().CurrentScene, at, 14f, ClapRange + 3f, ClapDamage, ClapStun, this );
					SeaStir.Push( at, 8f, 0.8f );
					SeaStir.SiltRing( at, 1.4f );
					ShakeNear( at, 14f, 0.5f );
				}
				if ( a > 1.4f ) Begin( Act.Listen );
				break;
			case Act.Scream:
				if ( !struck && a >= 0.5f )
				{
					struck = true;
					Screams++;
					Sfx.Play( "warden_scream", GlobalPosition + Vector3.Up * 3f, 1.2f, Rng.Float( 0.9f, 1.05f ) );
					Mark();
				}
				if ( a > 2f ) Begin( Act.Listen );
				break;
			case Act.Flick:
				if ( !struck && a >= 0.45f )
				{
					struck = true;
					Flicks++;
					Sfx.Play( "creature_attack", GlobalPosition, 1f, 0.4f );
					ThrowRubble( prey );
				}
				if ( a > 1.2f ) Begin( Act.Listen );
				break;
		}
		Move( wish, dt );
	}

	/// <summary> A player in front gets the claws, one behind the tail. True if it started one. </summary>
	bool CloseMove( PlayerSurvivor prey )
	{
		var to = (prey.RealPosition - GlobalPosition).Flat();
		var d = to.Length();
		var ahead = -GlobalBasis.Z.Flat().Normalized();
		var front = to.Normalized().Dot( ahead );
		if ( d < ClapRange && front > 0.35f && sinceClap > ClapCooldown ) { Begin( Act.Clap ); return true; }
		if ( d < FlickRange && front < -0.35f && sinceFlick > FlickCooldown ) { Begin( Act.Flick ); return true; }
		// Felt beside it: it turns to face them and claps when it can.
		if ( d < FeelRange ) { target = prey.RealPosition; sinceHeard = 0f; }
		return false;
	}

	Vector3 Wander()
	{
		for ( var i = 0; i < 8; i++ )
		{
			var p = GlobalPosition + Rng.OnSphere().Flat().SafeNormal() * Rng.Float( 6f, 14f );
			if ( SeaTerrain.Area == SeaTerrain.Caves && Abyss.InWall( p, 3f ) ) continue;
			return Sculpt.InBounds( p );
		}
		return GlobalPosition;
	}

	void Move( Vector3 wish, float dt )
	{
		velocity = velocity.Lerp( wish, 1f - MathF.Exp( -(act == Act.Charge ? 5f : 3f) * dt ) );
		if ( act is Act.Daze or Act.Clap or Act.Scream or Act.Flick or Act.Listen ) velocity = velocity.Lerp( Vector3.Zero, 1f - MathF.Exp( -6f * dt ) );
		var next = GlobalPosition + velocity.Flat() * dt;
		var from = GlobalPosition + Vector3.Up * 1.4f;
		if ( act != Act.Charge && velocity.LengthSquared() > 0.01f && Trace.Ray( this, from, from + velocity.Flat().SafeNormal() * 3.5f, Layers.World ).Hit ) { velocity = Vector3.Zero; next = GlobalPosition; }
		next = Sculpt.InBounds( next );
		GlobalPosition = next with { Y = MathX.Lerp( GlobalPosition.Y, Floor( next ), 1f - MathF.Exp( -10f * dt ) ) };
		// Players are shoved out of its body (they walk through creatures otherwise).
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c ) continue;
			var away = (p.RealPosition - GlobalPosition).Flat();
			var d = away.Length();
			if ( d < 2.2f && d > 0.01f && p.RealPosition.Y < GlobalPosition.Y + 3.2f ) c.Velocity += away / d * (2.2f - d) * 12f * dt;
		}
	}

	/// <summary> Whoever is in the way of a charge is run down, once a charge. </summary>
	void ChargeContact()
	{
		var front = GlobalTransform * new Vector3( 0f, 1.4f, -3.2f );
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c || chargeHit.Contains( p ) ) continue;
			var chest = p.RealPosition + Vector3.Up * 1f;
			if ( chest.DistanceTo( front ) > 2.8f && chest.DistanceTo( GlobalPosition + Vector3.Up * 1.6f ) > 2.4f ) continue;
			chargeHit.Add( p );
			ChargeHits++;
			p.Health?.OnDamage( new DamageInfo { Damage = ChargeDamage, Attacker = this, Position = chest, Origin = GlobalPosition } );
			c.Velocity += chargeDir * 9f + Vector3.Up * 4f;
			p.Juice?.Shake( 0.8f );
			Sfx.Play( "crab_snap", chest, 1f, 0.5f );
		}
	}

	/// <summary> A charge into rock: it slams to a stop, rubble and dust come down, and it is dazed, gills wide open. </summary>
	void Crash( Vector3 at )
	{
		Crashes++;
		velocity = Vector3.Zero;
		Begin( Act.Daze );
		Sfx.Play( "rock_crumble", at, 1.2f, 0.8f );
		Sfx.Play( "leviathan_crash", at, 0.6f, 1.5f );
		var recipe = GibRecipes.CaveRock();
		for ( var i = 0; i < 6; i++ )
			GibKit.Chips( recipe, at + Vector3.Up * Rng.Float( 2f, 7f ) + Rng.InSphere( 1.5f ), Vector3.Down, Vector3.Down, Rng.Float( 0.15f, 0.4f ), 1, 0.5f );
		var dust = SeaStir.SiltColour( at );
		for ( var i = 0; i < 12; i++ )
			GpuFx.SandPuff( at + Rng.InSphere( 2.5f ) + Vector3.Up * Rng.Float( 0.5f, 5f ), Rng.InSphere( 1.2f ), dust, Rng.Float( 10f, 20f ), Rng.Float( 1.6f, 2.8f ) );
		SeaStir.SiltRing( GlobalPosition, 1.6f );
		ShakeNear( at, 25f, 0.6f );
	}

	static void ShakeNear( Vector3 at, float range, float amount )
	{
		foreach ( var p in PlayerSurvivor.All )
			if ( p.IsAlive && p.RealPosition.DistanceTo( at ) < range ) p.Juice?.Shake( amount * (1f - p.RealPosition.DistanceTo( at ) / range) + 0.1f );
	}

	/// <summary> The echo scream: everyone near is marked and heard wherever they go for a while. </summary>
	void Mark()
	{
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.RealPosition.DistanceTo( GlobalPosition ) > 50f ) continue;
			marked[p] = Clock.Now + MarkTime;
			if ( p == PlayerSurvivor.Local ) LocalMarkedUntil = Clock.Now + MarkTime;
			p.Juice?.Shake( 0.35f );
		}
		ShockRing.Send( GetTree().CurrentScene, GlobalPosition, 18f, 30f, 0f, 0f, this, sand: true );
	}

	/// <summary> Tail flick: rubble thrown back at whoever is behind it. </summary>
	void ThrowRubble( PlayerSurvivor prey )
	{
		var start = GlobalTransform * new Vector3( 0f, 1.2f, 4.5f );
		var aim = prey is not null ? prey.RealPosition + Vector3.Up : start + GlobalBasis.Z * 6f;
		for ( var i = 0; i < 5; i++ )
		{
			var to = aim + Rng.InSphere( 1.6f ).Flat();
			var flat = (to - start).Flat();
			var time = MathX.Clamp( flat.Length() / 11f, 0.35f, 1.2f );
			var v = flat / time + Vector3.Up * (0.5f * WardenRubble.Gravity * time + (to.Y - start.Y) / time);
			WardenRubble.Throw( GetTree().CurrentScene, start + Rng.InSphere( 0.5f ), v, Puppet ? 0f : RubbleDamage, this );
		}
		SeaStir.SiltRing( start with { Y = Floor( start ) }, 1f );
	}

	void Begin( Act next )
	{
		act = next;
		sinceAct = 0f;
		struck = false;
		switch ( next )
		{
			case Act.Turn:
				Say( "IT HEARD YOU" );
				ShockRing.Send( GetTree().CurrentScene, target with { Y = Floor( target ) }, 7f, 4.5f, 0f, 0f, this, sand: true );
				break;
			case Act.Charge:
				Charges++;
				chargeHit.Clear();
				Say( "CHARGE" );
				Sfx.Play( "warden_charge", GlobalPosition + Vector3.Up * 2f, 1.1f, Rng.Float( 0.9f, 1.05f ) );
				break;
			case Act.Daze: Say( "DAZED" ); break;
			case Act.Clap: sinceClap = 0f; Say( "CLAW CLAP" ); break;
			case Act.Scream: sinceScream = 0f; Say( "ECHO SCREAM" ); break;
			case Act.Flick: sinceFlick = 0f; Say( "TAIL FLICK" ); break;
		}
	}

	/// <summary> Tests and the dev menu: start a move now (a charge goes at `at`). </summary>
	public void Force( Act next, Vector3? at = null )
	{
		if ( at is { } p ) { target = p; sinceHeard = 0f; }
		if ( next == Act.Charge ) chargeDir = (target - GlobalPosition).Flat().SafeNormal();
		Begin( next );
	}

	// ---- Online ----

	Vector3 lastPuppetAt;
	void PuppetFeet( float dt )
	{
		velocity = (GlobalPosition - lastPuppetAt) / MathF.Max( dt, 0.001f );
		lastPuppetAt = GlobalPosition;
		// Its claps, screams and flicks show here; only the host's do harm.
		var a = (float)sinceAct;
		if ( !struck && act == Act.Clap && a >= 0.7f ) { struck = true; Sfx.Play( "warden_clap", FrontAt ); ShockRing.Send( GetTree().CurrentScene, FrontAt with { Y = Floor( FrontAt ) }, 14f, ClapRange + 3f, 0f, 0f, this ); }
		if ( !struck && act == Act.Scream && a >= 0.5f )
		{
			struck = true;
			Sfx.Play( "warden_scream", GlobalPosition + Vector3.Up * 3f, 1.2f );
			if ( PlayerSurvivor.Local is { IsAlive: true } me && me.RealPosition.DistanceTo( GlobalPosition ) < 50f ) LocalMarkedUntil = Clock.Now + MarkTime;
		}
		if ( !struck && act == Act.Flick && a >= 0.45f ) { struck = true; ThrowRubble( PlayerSurvivor.FindNearestAlive( GlobalPosition ) ); }
	}

	public override void NetWrite( List<float> s )
	{
		s.Add( (int)act ); s.Add( (float)sinceAct ); s.Add( target.X ); s.Add( target.Y ); s.Add( target.Z );
	}

	public override void NetRead( ReadOnlySpan<float> s )
	{
		if ( s.Length < 5 ) return;
		var next = (Act)(int)s[0];
		target = new Vector3( s[2], s[3], s[4] );
		var changed = next != act;
		if ( changed ) { if ( next == Act.Daze ) Crash( GlobalPosition + -GlobalBasis.Z * 4f ); else Begin( next ); }
		act = next;
		NetTimer( ref sinceAct, s[1], changed );
	}

	// ---- Hits and death ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( NetCreatures.Intercept( this, index, ref damage, direction, position, origin, shooter, scoreHit ) ) return;
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		var gills = index is 2 or 3;
		var open = gills && GillsVulnerable;
		var scale = index switch
		{
			2 or 3 => open ? GillOpenMultiplier : GillShutMultiplier,
			4 => TailMultiplier,
			5 or 6 => ClawMultiplier,
			_ => ShellMultiplier,
		};
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * scale, Position = position, Origin = origin, Attacker = shooter } );
		if ( gibs++ % 3 == 0 ) GibKit.Chips( open ? GibRecipes.Crab( ShellColour, BellyColour, Blood ) : GibRecipes.CaveRock(), position, -direction, direction, open ? 0.2f : 0.1f, 1, 0.6f );
		if ( !open && scale < 0.6f && Rng.Float() < 0.3f ) Sfx.Play( "shell_crack", position, 0.6f, Rng.Float( 0.7f, 0.9f ) );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, open, scoreHit );
	}
	int gibs;

	protected override void OnDied()
	{
		RaiseKilled();
		var centre = GlobalPosition + Vector3.Up * BodyHeight;
		var recipe = Gibs;
		GibKit.Burst( recipe, centre, lastShotDirection, 0.6f, 18, 1.2f );
		GibKit.Burst( recipe, GlobalTransform * new Vector3( 0f, 1.6f, -3.5f ), lastShotDirection, 0.5f, 10, 1f );
		GibKit.Burst( recipe, GlobalTransform * new Vector3( 0f, 1.6f, 3f ), lastShotDirection, 0.5f, 10, 1f );
		AlienBlast.Spawn( GetTree().CurrentScene, centre, 1.4f );
		Sfx.Play( "warden_scream", centre, 1f, 0.6f );
		QueueFree();
	}
}

/// <summary> A lump of rock the Warden's tail throws: it arcs, hurts a player it hits, and breaks on the floor. </summary>
public partial class WardenRubble : Node3D
{
	public const float Gravity = 9f;
	Vector3 velocity;
	float damage, age;
	Node3D owner;

	public static void Throw( Node parent, Vector3 at, Vector3 velocity, float damage, Node3D owner )
	{
		var r = new WardenRubble { Name = "Warden Rubble", velocity = velocity, damage = damage, owner = owner };
		parent.AddChild( r );
		r.GlobalPosition = at;
		Sculpt.Blob( r, Vector3.Zero, Vector3.One * Rng.Float( 0.45f, 0.7f ), Abyss.RockPaint, new Basis( Rng.OnSphere(), Rng.Float( 0f, 3f ) ), "Rock" );
	}

	public override void _Process( double delta )
	{
		var dt = MathF.Min( (float)delta, 0.1f );
		age += dt;
		velocity += Vector3.Down * Gravity * dt;
		var next = GlobalPosition + velocity * dt;
		var hit = Trace.Ray( this, GlobalPosition, next, Layers.World );
		if ( damage > 0f )
			foreach ( var p in PlayerSurvivor.All )
			{
				if ( !p.IsAlive || (p.RealPosition + Vector3.Up).DistanceTo( next ) > 1.1f ) continue;
				BlindWarden.RubbleHits++;
				p.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = IsInstanceValid( owner ) ? owner : null, Position = next, Origin = GlobalPosition } );
				p.Juice?.Shake( 0.3f );
				Break( next );
				return;
			}
		if ( hit.Hit || age > 4f ) { Break( hit.Hit ? hit.HitPosition : next ); return; }
		GlobalPosition = next;
		Rotation += Vector3.One * dt * 3f;
	}

	void Break( Vector3 at )
	{
		GibKit.Chips( GibRecipes.CaveRock(), at, Vector3.Up, velocity.SafeNormal(), 0.12f, 2, 0.5f );
		Sfx.Play( "rock_crumble", at, 0.4f, Rng.Float( 1.1f, 1.3f ) );
		QueueFree();
	}
}
