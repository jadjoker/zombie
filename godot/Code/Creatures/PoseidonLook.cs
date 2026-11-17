using Godot;
using static SmoothKit;

/// <summary>
/// Poseidon's body, smooth and simple like the mermaids' (<see cref="SmoothKit"/>): a lofted bodybuilder torso with
/// soft pecs and abs, gold pauldrons, back plate, collar, belt and bracers; a stern sea god's face (heavy brow over
/// glowing green eyes with lids, a strong nose, fin ears, bushy white brows that knit when he is angry, a moustache, a
/// beard on a jaw that drops when he roars, flowing locks of hair and beard); a spiked gold crown; one smooth skinned
/// tail with jewel-coloured scales and gold bands (<see cref="GodTail"/>), a two-lobed fluke and rippling fins.
/// Metres, facing -Z, the waist at the body's origin. The statue in Atlantis is the same rig in gold or marble.
/// </summary>
public partial class Poseidon
{
	/// <summary>
	/// His tail: rounded scales in rows, each a jewel colour picked by its own hash (gold, purple, green, sea-teal), with
	/// a darker rim and inset line, and a gold band at each joint. UV.y 0 at the hips to 1 at the fluke.
	/// </summary>
	static readonly Shader GodTail = new()
	{
		Code = @"shader_type spatial;
uniform vec3 c0 : source_color = vec3(1.0, 0.8, 0.15);
uniform vec3 c1 : source_color = vec3(0.7, 0.22, 1.0);
uniform vec3 c2 : source_color = vec3(0.3, 0.85, 0.3);
uniform vec3 c3 : source_color = vec3(0.1, 0.56, 0.5);
uniform vec3 band : source_color = vec3(1.0, 0.76, 0.26);
uniform float rows = 38.0;
uniform float around = 26.0;
uniform float joints = 9.0;
uniform float metal = 0.3;
uniform float glow = 0.0;
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
	vec3 c = h < 0.4 ? c3 : h < 0.6 ? c2 : h < 0.8 ? c1 : c0;
	float edge = smoothstep(0.36, 0.5, d);
	float inset = 1.0 - smoothstep(0.0, 0.03, abs(d - 0.24));
	float shine = (1.0 - smoothstep(0.0, 0.3, d)) * 0.18;
	c = c * (1.0 - edge * 0.45 - inset * 0.3) + vec3(shine);
	float j = fract(UV.y * joints * 0.98 + 0.02);
	float b = 1.0 - smoothstep(0.0, 0.035, abs(j - 0.04));
	c = mix(c, band, b);
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = mix(c, vec3(2.2), flash);
	METALLIC = mix(metal, 0.9, b);
	ROUGHNESS = mix(0.32, 0.15, b);
	EMISSION = c * (glow + rim * 0.3);
}",
	};

	/// <summary> His materials: the boss's own colours, or all gold (or marble) for his statue in Atlantis. </summary>
	public sealed class Look
	{
		public Material Skin, Hands, Vein, Scales, Hair, Armour, Purple, Green, Yellow, Fins, EyeBlack, EyeWhite, Pupil, Dark;
		/// <summary> The tail's four scale colours, its bands, and how metal and lit it is. </summary>
		public Color[] TailColours;
		public Color TailBand;
		public float TailMetal = 0.3f, TailGlow;
	}

	/// <summary> The joints of his body, to pose and animate. </summary>
	public sealed class Rig
	{
		public Node3D Body, Chest, Head, Jaw, ArmR, ForeR, ArmL, ForeL, Hand, Trident;
		public readonly List<Node3D> Tail = new();
		public readonly Node3D[] Brows = new Node3D[2];
		/// <summary> Locks of hair down his back and of beard under his chin: each a chain of joints, root first. </summary>
		public readonly List<List<Node3D>> Hair = new(), Beard = new();
		/// <summary> Every skinned chain (tail, locks), for <see cref="SmoothKit.PoseChain"/>. </summary>
		public readonly List<(Skeleton3D Bones, List<Node3D> Joints)> Chains = new();
		public MeshInstance3D TailMesh;
		public ShaderMaterial TailPaint;
		/// <summary> Each eye's pieces, for the blinker (the statue does not blink). </summary>
		public readonly List<Node3D[]> Eyes = new();

		public void Pose() { foreach ( var (bones, joints) in Chains ) PoseChain( bones, joints ); }
	}

	ShaderMaterial skin, handSkin, finPaint;
	Rig rig;

	void Build()
	{
		skin = new ShaderMaterial { Shader = SkinShader };
		handSkin = new ShaderMaterial { Shader = SkinShader };
		handSkin.SetShaderParameter( "pin", new Vector4( 0f, 1.5f, -0.5f, 1f ) );   // the middle of his chest
		var armourGold = Sculpt.Paint( Armour, 0.14f, 0.95f );
		armourGold.EmissionEnabled = true;
		armourGold.Emission = Armour;
		armourGold.EmissionEnergyMultiplier = 0.12f;
		finPaint = FinMaterial( new Color( 0.35f, 0.9f, 0.8f ), new Color( 0.1f, 0.4f, 0.4f ), 0.62f, 0.1f );
		var look = new Look
		{
			Skin = skin, Hands = handSkin, Hair = Sculpt.Paint( Hair, 0.7f ), Armour = armourGold,
			Purple = Sculpt.Glow( JewelPurple, 2.4f ), Green = Sculpt.Glow( JewelGreen, 2.4f ), Yellow = Sculpt.Glow( JewelYellow, 2.2f ),
			Fins = finPaint,
			EyeBlack = Sculpt.Paint( new Color( 0.02f, 0.02f, 0.025f ), 0.1f ), EyeWhite = Sculpt.Paint( new Color( 0.92f, 0.95f, 1f ), 0.2f ),
			Pupil = Sculpt.Glow( new Color( 0.35f, 1f, 0.3f ), 2.6f ),
			Dark = Sculpt.Paint( new Color( 0.12f, 0.03f, 0.08f ), 0.6f ),
			TailColours = new[] { new Color( 1f, 0.8f, 0.15f ), JewelPurple, new Color( 0.3f, 0.85f, 0.3f ), Scales },
			TailBand = Armour,
		};
		rig = BuildRig( this, look );
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
		var blinker = Blinker.Add( this );
		foreach ( var eye in rig.Eyes ) blinker.Eye( eye );

		// What can be shot: the head (index 0), chest, belly, and the top of the tail.
		void Hit( Node3D on, int index, Vector3 at, float radius )
		{
			var part = CreaturePart.Create( this, index, $"Poseidon {index}", radius );
			on.AddChild( part );
			part.Position = at;
		}
		Hit( head, 0, new Vector3( 0f, 0.28f, 0f ), 0.38f );
		Hit( chest, 1, new Vector3( 0f, 0.75f, 0f ), 0.68f );
		Hit( chest, 2, new Vector3( 0f, 0.15f, 0f ), 0.5f );
		for ( var i = 0; i < 4; i++ ) Hit( tail[i], 3 + i, new Vector3( 0f, -0.2f, 0f ), MathX.Lerp( 0.42f, 0.3f, i / 3f ) );
	}

	/// <summary> The tail's thickness down its length (0 at the hips, 1 at the fluke). </summary>
	static float TailRadius( float t ) => MathX.Lerp( 0.47f, 0.12f, MathF.Pow( MathX.Clamp( t, 0f, 1f ), 0.85f ) ) + MathF.Sin( MathX.Clamp( t / 0.3f, 0f, 1f ) * MathF.PI ) * 0.05f;
	const float TailTop = -0.03f, TailStep = 0.36f, TailFirst = -0.15f, TailBelow = 0.32f;
	static float TailBottom => TailFirst - TailStep * 8 - TailBelow;

	/// <summary>
	/// His body, under a node: the tail, the belt, the torso with its gold armour, the head and crown, the arms and the
	/// trident in his right hand. Metres, facing -Z, the waist at the node's origin. Placing the trident is left to the caller.
	/// </summary>
	public static Rig BuildRig( Node3D into, Look look )
	{
		var skin = look.Skin;
		var hands = look.Hands ?? look.Skin;
		var hair = look.Hair;
		var armour = look.Armour;
		var purple = look.Purple;
		var green = look.Green;
		var yellow = look.Yellow;
		var fins = look.Fins;
		if ( fins is BaseMaterial3D plain )
		{
			// A solid fin material (the statue's) must show both sides of the fans.
			plain = (BaseMaterial3D)plain.Duplicate();
			plain.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			fins = plain;
		}
		var rig = new Rig();
		var body = new Node3D { Name = "Body" };
		into.AddChild( body );

		BuildTail( body, look, rig, fins );

		// Belt: a gold ring with a big yellow jewel at the front, purple and green ones beside it.
		var belt = new MeshInstance3D { Name = "Belt", Mesh = new TorusMesh { InnerRadius = 0.4f, OuterRadius = 0.53f, Rings = 48, RingSegments = 14 }, MaterialOverride = armour };
		body.AddChild( belt );
		belt.Position = new Vector3( 0f, 0.02f, 0f );
		belt.Scale = new Vector3( 1f, 1.2f, 0.84f );
		Ball( body, new Vector3( 0f, 0.02f, -0.46f ), new Vector3( 0.2f, 0.24f, 0.1f ), yellow, null, "Buckle" );
		foreach ( var s in new[] { -1f, 1f } )
		{
			Ball( body, new Vector3( s * 0.26f, 0.03f, -0.4f ), Vector3.One * 0.1f, purple, null, "Jewel" );
			Ball( body, new Vector3( s * 0.44f, 0.03f, -0.2f ), Vector3.One * 0.09f, green, null, "Jewel" );
		}

		// ---- The torso: one smooth loft, a narrow waist under a huge chest (a bodybuilder's V) ----
		var chest = Sculpt.Joint( body, new Vector3( 0f, 0.2f, 0f ), "Chest" );
		var torso = new List<(float, float, float, float)>
		{
			(-0.26f, 0.47f, 0.4f, 0f),
			(0.0f, 0.43f, 0.34f, 0f),
			(0.25f, 0.47f, 0.34f, -0.02f),
			(0.5f, 0.6f, 0.38f, -0.03f),
			(0.75f, 0.74f, 0.42f, -0.02f),
			(0.95f, 0.8f, 0.42f, 0f),
			(1.1f, 0.66f, 0.36f, 0.02f),
			(1.24f, 0.34f, 0.28f, 0.03f),
			(1.4f, 0.23f, 0.22f, 0f),
			(1.58f, 0.18f, 0.18f, -0.01f),
		};
		Put( chest, Cached( "god torso", () => Loft( Smooth( torso ), 0f, 1f, 36 ) ), skin, "Torso" );
		foreach ( var s in new[] { -1f, 1f } )
		{
			Ball( chest, new Vector3( s * 0.29f, 0.86f, -0.27f ), new Vector3( 0.62f, 0.44f, 0.3f ), skin, new Basis( Vector3.Back, s * 0.12f ), "Pec" );
			for ( var k = 0; k < 3; k++ )
				Ball( chest, new Vector3( s * 0.12f, 0.5f - k * 0.16f, -0.33f + k * 0.012f ), new Vector3( 0.2f, 0.14f, 0.08f ), skin, null, "Ab" );
		}

		// Gold armour: a back plate with three spikes and a purple jewel, a collar behind the neck, spiked pauldrons.
		Ball( chest, new Vector3( 0f, 0.7f, 0.28f ), new Vector3( 1.3f, 1.05f, 0.36f ), armour, null, "Back Plate" );
		Ball( chest, new Vector3( 0f, 0.68f, 0.47f ), Vector3.One * 0.13f, purple, null, "Jewel" );
		foreach ( var (x, y) in new[] { (-0.34f, 0.92f), (0f, 1.0f), (0.34f, 0.92f) } )
			Sculpt.Cone( chest, new Vector3( x, y, 0.4f ), new Vector3( x * 1.4f, y + 0.22f, 0.82f ), 0.075f, armour, 14, "Spike" );
		var gorget = new MeshInstance3D { Name = "Collar", Mesh = new TorusMesh { InnerRadius = 0.22f, OuterRadius = 0.33f, Rings = 40, RingSegments = 12 }, MaterialOverride = armour };
		chest.AddChild( gorget );
		gorget.Position = new Vector3( 0f, 1.24f, 0.02f );
		Ball( chest, new Vector3( 0f, 1.42f, 0.2f ), new Vector3( 0.66f, 0.46f, 0.26f ), armour, new Basis( Vector3.Right, -0.25f ), "Collar Back" );
		Ball( chest, new Vector3( 0f, 1.5f, 0.32f ), Vector3.One * 0.11f, yellow, null, "Jewel" );
		foreach ( var s in new[] { -1f, 1f } )
		{
			var p = new Vector3( s * 0.86f, 1.08f, 0f );
			Ball( chest, p, new Vector3( 0.74f, 0.48f, 0.72f ), armour, new Basis( Vector3.Back, -s * 0.22f ), "Pauldron" );
			Ball( chest, p + new Vector3( s * 0.14f, 0.08f, -0.32f ), Vector3.One * 0.13f, s < 0 ? green : purple, null, "Jewel" );
			for ( var k = 0; k < 3; k++ )
				Sculpt.Cone( chest, p + new Vector3( s * 0.2f, 0.14f, (k - 1) * 0.18f ), p + new Vector3( s * 0.5f, 0.5f, (k - 1) * 0.22f ), 0.065f, armour, 14, "Spike" );
		}

		BuildHead( chest, look, rig );

		// ---- Arms: smooth upper arm and forearm with soft muscle, gold band, elbow cop, bracer, a fist ----
		(Node3D Arm, Node3D Fore, Node3D Hand) Arm( float s, Material bracerJewel )
		{
			var arm = Sculpt.Joint( chest, new Vector3( s * 0.92f, 1.02f, 0f ), s > 0 ? "Right Arm" : "Left Arm" );
			Ball( arm, Vector3.Zero, Vector3.One * 0.46f, skin, null, "Shoulder" );
			Tube( arm, Vector3.Zero, new Vector3( 0f, -0.68f, 0f ), 0.22f, 0.17f, skin, "Upper Arm" );
			Ball( arm, new Vector3( 0f, -0.3f, -0.07f ), new Vector3( 0.5f, 0.54f, 0.48f ), skin, null, "Biceps" );
			Ball( arm, new Vector3( 0f, -0.36f, 0.11f ), new Vector3( 0.4f, 0.5f, 0.34f ), skin, null, "Triceps" );
			var band = new MeshInstance3D { Name = "Arm Band", Mesh = new TorusMesh { InnerRadius = 0.18f, OuterRadius = 0.26f, Rings = 32, RingSegments = 10 }, MaterialOverride = armour };
			arm.AddChild( band );
			band.Position = new Vector3( 0f, -0.58f, 0f );
			Ball( arm, new Vector3( 0f, -0.68f, 0.02f ), Vector3.One * 0.34f, armour, null, "Elbow Cop" );
			Sculpt.Cone( arm, new Vector3( 0f, -0.7f, 0.14f ), new Vector3( 0f, -0.74f, 0.4f ), 0.055f, armour, 14, "Spike" );
			var fore = Sculpt.Joint( arm, new Vector3( 0f, -0.68f, 0f ), "Elbow" );
			Tube( fore, Vector3.Zero, new Vector3( 0f, -0.64f, 0f ), 0.17f, 0.13f, skin, "Forearm" );
			Ball( fore, new Vector3( 0f, -0.18f, -0.01f ), new Vector3( 0.38f, 0.4f, 0.36f ), skin, null, "Forearm Muscle" );
			Tube( fore, new Vector3( 0f, -0.36f, 0f ), new Vector3( 0f, -0.62f, 0f ), 0.18f, 0.155f, armour, "Bracer" );
			Ball( fore, new Vector3( 0f, -0.49f, -0.17f ), Vector3.One * 0.1f, bracerJewel, null, "Jewel" );
			var h = Sculpt.Joint( fore, new Vector3( 0f, -0.72f, 0f ), "Hand" );
			// A fist: the palm and a row of curled fingers, the thumb over them.
			Ball( h, Vector3.Zero, new Vector3( 0.26f, 0.28f, 0.24f ), hands, null, "Palm" );
			for ( var k = 0; k < 4; k++ )
				Ball( h, new Vector3( (k - 1.5f) * 0.058f, -0.09f, -0.08f ), new Vector3( 0.07f, 0.1f, 0.1f ), hands, null, "Finger" );
			Tube( h, new Vector3( -s * 0.1f, 0.02f, -0.06f ), new Vector3( -s * 0.03f, -0.06f, -0.14f ), 0.035f, 0.03f, hands, "Thumb" );
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
		rig.ArmR = armR;
		rig.ForeR = foreR;
		rig.ArmL = armL;
		rig.ForeL = foreL;
		rig.Hand = hand;
		rig.Trident = heldTrident;
		// The locks' resting hang (built straight down, so their skins bind true), then the bones follow.
		foreach ( var lockJoints in rig.Hair ) lockJoints[0].Rotation = new Vector3( -0.35f, 0f, 0f );
		foreach ( var lockJoints in rig.Beard ) lockJoints[0].Rotation = new Vector3( 0.08f, 0f, 0f );
		rig.Pose();
		return rig;
	}

	/// <summary>
	/// The head: a smooth face, heavy brow, eyes with lids, strong nose, fin ears, bushy brows on pivots, moustache,
	/// a beard on a dropping jaw, white hair with locks down his back, the crown.
	/// </summary>
	static void BuildHead( Node3D chest, Look look, Rig rig )
	{
		var skin = look.Skin;
		var hair = look.Hair;
		var armour = look.Armour;
		var head = Sculpt.Joint( chest, new Vector3( 0f, 1.42f, -0.02f ), "Head" );
		rig.Head = head;
		var skull = new List<(float, float, float, float)>
		{
			(-0.04f, 0.1f, 0.1f, -0.05f),
			(0.04f, 0.2f, 0.2f, -0.04f),
			(0.14f, 0.24f, 0.25f, -0.02f),
			(0.28f, 0.25f, 0.27f, 0f),
			(0.42f, 0.25f, 0.27f, 0.01f),
			(0.56f, 0.22f, 0.24f, 0.02f),
			(0.66f, 0.13f, 0.15f, 0.03f),
			(0.7f, 0.02f, 0.02f, 0.03f),
		};
		Put( head, Cached( "god skull", () => Loft( Smooth( skull ), 0f, 1f, 32 ) ), skin, "Head" );
		// The brow ridge, the nose, and the cheekbones.
		Ball( head, new Vector3( 0f, 0.39f, -0.22f ), new Vector3( 0.4f, 0.09f, 0.11f ), skin, null, "Brow Ridge" );
		Ball( head, new Vector3( 0f, 0.29f, -0.27f ), new Vector3( 0.075f, 0.15f, 0.09f ), skin, new Basis( Vector3.Right, -0.3f ), "Nose" );
		Ball( head, new Vector3( 0f, 0.225f, -0.295f ), new Vector3( 0.1f, 0.075f, 0.08f ), skin, null, "Nose Tip" );
		foreach ( var s in new[] { -1f, 1f } )
		{
			Ball( head, new Vector3( s * 0.045f, 0.22f, -0.28f ), new Vector3( 0.05f, 0.045f, 0.05f ), skin, null, "Nostril Wing" );
			Ball( head, new Vector3( s * 0.15f, 0.25f, -0.2f ), new Vector3( 0.14f, 0.09f, 0.1f ), skin, null, "Cheekbone" );
		}
		foreach ( var s in new[] { -1f, 1f } )
		{
			// An eye: white, a glowing green iris, a black pupil and a glint, its upper lid low and slanted (stern).
			var at = new Vector3( s * 0.1f, 0.33f, -0.225f );
			var white = Ball( head, at, new Vector3( 0.11f, 0.085f, 0.06f ), look.EyeWhite ?? skin, null, "Eye" );
			var iris = Ball( head, at + new Vector3( 0f, -0.005f, -0.026f ), new Vector3( 0.055f, 0.055f, 0.02f ), look.Pupil, null, "Iris" );
			var pupil = Ball( head, at + new Vector3( 0f, -0.005f, -0.034f ), new Vector3( 0.024f, 0.024f, 0.01f ), look.EyeBlack ?? skin, null, "Pupil" );
			var glint = Ball( head, at + new Vector3( -s * 0.012f, 0.012f, -0.037f ), Vector3.One * 0.012f, Sculpt.Glow( Colors.White, 1f ), null, "Glint" );
			rig.Eyes.Add( new Node3D[] { white, iris, pupil, glint } );
			Ball( head, at + new Vector3( 0f, 0.033f, 0.002f ), new Vector3( 0.13f, 0.06f, 0.075f ), skin, new Basis( Vector3.Back, s * 0.28f ), "Lid" );
			// A fin for an ear, swept back.
			Fin( head, new Vector3( s * 0.24f, 0.32f, 0.04f ), new Basis( Vector3.Up, -s * 1.1f ) * new Basis( Vector3.Back, s * 2.1f ), 0.4f, 0.14f, look.Fins, 0.06f );
			// A bushy white brow on a pivot at its inner end (it knits down when he is angry).
			var brow = Sculpt.Joint( head, new Vector3( s * 0.035f, 0.4f, -0.29f ), s > 0 ? "Right Brow" : "Left Brow" );
			Tube( brow, Vector3.Zero, new Vector3( s * 0.15f, 0.04f, 0.045f ), 0.024f, 0.014f, hair, "Brow" );
			brow.Rotation = new Vector3( 0f, 0f, s * 0.3f );
			rig.Brows[s > 0 ? 1 : 0] = brow;
			// The moustache: from under the nose out and down past the corners of the mouth.
			Tube( head, new Vector3( s * 0.02f, 0.19f, -0.3f ), new Vector3( s * 0.13f, 0.14f, -0.27f ), 0.04f, 0.035f, hair, "Moustache" );
			Tube( head, new Vector3( s * 0.13f, 0.14f, -0.27f ), new Vector3( s * 0.19f, 0.0f, -0.22f ), 0.035f, 0.01f, hair, "Moustache" );
		}
		// The mouth (dark, teeth along the top) shows when the jaw drops.
		Ball( head, new Vector3( 0f, 0.11f, -0.22f ), new Vector3( 0.18f, 0.16f, 0.08f ), look.Dark ?? skin, null, "Mouth" );
		Ball( head, new Vector3( 0f, 0.155f, -0.255f ), new Vector3( 0.13f, 0.03f, 0.03f ), look.EyeWhite ?? skin, null, "Teeth" );
		// The jaw: the beard's mass and its locks hang from it.
		var jaw = Sculpt.Joint( head, new Vector3( 0f, 0.2f, -0.04f ), "Jaw" );
		rig.Jaw = jaw;
		Ball( jaw, new Vector3( 0f, -0.21f, -0.09f ), new Vector3( 0.46f, 0.38f, 0.34f ), hair, null, "Beard" );
		for ( var k = 0; k < 5; k++ )
		{
			var x = (k - 2f) * 0.08f;
			var length = 0.62f - MathF.Abs( k - 2f ) * 0.09f;
			LockOfHair( jaw, rig, rig.Beard, new Vector3( x, -0.3f, -0.17f + MathF.Abs( x ) * 0.5f ), length, 0.085f, hair, $"Beard {k} {length:F2}" );
		}
		// Hair: a white mass over the back of the head and seven locks down his back.
		Ball( head, new Vector3( 0f, 0.4f, 0.08f ), new Vector3( 0.62f, 0.62f, 0.56f ), hair, null, "Hair" );
		for ( var k = 0; k < 7; k++ )
		{
			var x = (k - 3f) * 0.085f;
			var length = 1.05f + (k % 3) * 0.15f;
			LockOfHair( head, rig, rig.Hair, new Vector3( x, 0.42f, 0.26f - MathF.Abs( x ) * 0.3f ), length, 0.11f, hair, $"God Hair {k} {length:F2}" );
		}
		// The crown: a gold band rimmed top and bottom, seven spikes (the middle one tallest), jewels below them.
		var band = new MeshInstance3D { Name = "Crown", Mesh = new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.29f, Height = 0.18f, RadialSegments = 48, Rings = 1, CapTop = false, CapBottom = false }, MaterialOverride = armour };
		head.AddChild( band );
		band.Position = new Vector3( 0f, 0.57f, 0.02f );
		foreach ( var (y, r) in new[] { (0.48f, 0.29f), (0.66f, 0.32f) } )
		{
			var rim = new MeshInstance3D { Name = "Crown Rim", Mesh = new TorusMesh { InnerRadius = r - 0.02f, OuterRadius = r + 0.025f, Rings = 48, RingSegments = 8 }, MaterialOverride = armour };
			head.AddChild( rim );
			rim.Position = new Vector3( 0f, y, 0.02f );
		}
		for ( var k = 0; k < 7; k++ )
		{
			var a = MathX.Rad( -90f + (k - 3) * 30f );
			var foot = new Vector3( MathF.Cos( a ) * 0.31f, 0.64f, MathF.Sin( a ) * 0.31f + 0.02f );
			var tall = k == 3 ? 0.5f : 0.3f + (k % 2) * 0.08f;
			Sculpt.Cone( head, foot, foot + new Vector3( MathF.Cos( a ) * 0.08f, tall, MathF.Sin( a ) * 0.08f ), 0.065f, armour, 14, "Crown Spike" );
			Ball( head, foot + new Vector3( MathF.Cos( a ) * 0.02f, -0.07f, MathF.Sin( a ) * 0.02f ), Vector3.One * (k == 3 ? 0.11f : 0.07f), k == 3 ? look.Yellow : k % 2 == 0 ? look.Green : look.Purple, null, "Jewel" );
		}
	}

	/// <summary> A skinned lock (hair or beard) of three joints hanging down from `root` under `parent`. </summary>
	static void LockOfHair( Node3D parent, Rig rig, List<List<Node3D>> into, Vector3 root, float length, float width, Material paint, string key )
	{
		var step = length / 2.6f;
		var a = Sculpt.Joint( parent, root, "Lock" );
		var b = Sculpt.Joint( a, new Vector3( 0f, -step, 0f ), "Lock Mid" );
		var c = Sculpt.Joint( b, new Vector3( 0f, -step, 0f ), "Lock Tip" );
		var joints = new List<Node3D> { a, b, c };
		var bones = SkinChain( parent, joints, step, 0.06f, step * 0.6f,
			t => (MathX.Lerp( width, width * 0.2f, MathF.Pow( t, 1.2f ) ), MathX.Lerp( width * 0.8f, width * 0.18f, t )), paint, key, 18, 12 );
		rig.Chains.Add( (bones, joints) );
		into.Add( joints );
	}

	/// <summary>
	/// His tail under `body`: nine joints from the hips down, one smooth skinned mesh painted with jewel scales and gold
	/// bands, a broad two-lobed fluke, hip fins and a low ridge fin. The joints go into the rig, hips first.
	/// </summary>
	static void BuildTail( Node3D body, Look look, Rig rig, Material fins )
	{
		Node3D parent = body;
		var at = new Vector3( 0f, TailFirst, 0f );
		for ( var i = 0; i < 9; i++ )
		{
			var joint = Sculpt.Joint( parent, at, $"Tail {i}" );
			rig.Tail.Add( joint );
			parent = joint;
			at = new Vector3( 0f, -TailStep, 0f );
		}
		var paint = new ShaderMaterial { Shader = GodTail };
		var colours = look.TailColours ?? new[] { new Color( 1f, 0.8f, 0.15f ), JewelPurple, new Color( 0.3f, 0.85f, 0.3f ), Scales };
		for ( var c = 0; c < 4; c++ ) paint.SetShaderParameter( $"c{c}", colours[c % colours.Length] );
		paint.SetShaderParameter( "band", look.TailBand );
		paint.SetShaderParameter( "metal", look.TailMetal );
		paint.SetShaderParameter( "glow", look.TailGlow );
		rig.TailPaint = paint;
		var bones = SkinChain( body, rig.Tail, TailStep, TailTop - TailFirst, TailBelow, t => (TailRadius( t ), TailRadius( t ) * 0.85f), paint, "God Tail", 72, 30 );
		rig.TailMesh = bones.GetChild<MeshInstance3D>( 0 );
		rig.Chains.Add( (bones, rig.Tail) );
		// The fluke: two broad lobes swept back and out, a smaller pair behind them; hip fins; a ridge fin down his back.
		foreach ( var s in new[] { -1f, 1f } )
		{
			Fin( parent, new Vector3( s * 0.05f, -0.3f, 0f ), new Basis( Vector3.Back, s * 0.95f ) * new Basis( Vector3.Right, 0.12f ), 1.0f, 0.62f, fins, 0.08f );
			Fin( parent, new Vector3( s * 0.03f, -0.28f, 0.02f ), new Basis( Vector3.Back, s * 0.4f ), 0.56f, 0.34f, fins, 0.08f );
			Fin( rig.Tail[1], new Vector3( s * 0.42f, -0.1f, 0.05f ), new Basis( Vector3.Back, s * 1.0f ) * new Basis( Vector3.Right, -0.25f ), 0.6f, 0.4f, fins );
		}
		for ( var i = 2; i <= 6; i++ )
			Fin( rig.Tail[i], new Vector3( 0f, -0.05f, TailRadius( i / 9f ) * 0.75f ), new Basis( Vector3.Right, -1.25f ), 0.32f, 0.28f, fins, 0.05f );
	}

	/// <summary>
	/// When he bursts: a plain smooth mesh on tail joint `from` covering `count` sections (to the fluke when it is the
	/// last), so the skinned tail can be hidden and its sections fly as pieces.
	/// </summary>
	void TailPiece( int from, int count )
	{
		if ( from >= tail.Count || !IsInstanceValid( tail[from] ) || rig?.TailPaint is null ) return;
		var top = from == 0 ? TailTop - TailFirst : 0.02f;
		var length = from + count >= tail.Count ? TailStep * (tail.Count - 1 - from) + TailBelow : TailStep * count;
		var rings = new List<(float, float, float, float)>();
		for ( var k = 0; k <= 16; k++ )
		{
			var y = MathX.Lerp( top, -length, k / 16f );
			var worldY = TailFirst - TailStep * from + y;
			var t = (TailTop - worldY) / (TailTop - TailBottom);
			var r = TailRadius( t );
			rings.Add( (y, r, r * 0.85f, 0f) );
		}
		var v0 = (TailTop - (TailFirst - TailStep * from + top)) / (TailTop - TailBottom);
		var v1 = (TailTop - (TailFirst - TailStep * from - length)) / (TailTop - TailBottom);
		Put( tail[from], Loft( rings, v0, v1, 28 ), rig.TailPaint, "Tail Piece" );
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
			Skin = gold, Hands = gold, Vein = darkGold, Scales = gold, Hair = gold, Armour = darkGold, Fins = darkGold, EyeBlack = darkGold, EyeWhite = gold, Dark = darkGold,
			Purple = Sculpt.Glow( JewelPurple, 2.4f ), Green = Sculpt.Glow( JewelGreen, 2.4f ), Yellow = Sculpt.Glow( JewelYellow, 2.2f ),
			Pupil = Sculpt.Glow( new Color( 0.35f, 1f, 0.3f ), 3.2f ),
			TailColours = new[] { gold.AlbedoColor, darkGold.AlbedoColor, gold.AlbedoColor, gold.AlbedoColor.Darkened( 0.1f ) },
			TailBand = darkGold.AlbedoColor, TailMetal = marble ? 0f : 0.85f, TailGlow = marble ? 0f : 0.25f,
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
		rig.Pose();
		var up = Vector3.Up + turn * Vector3.Forward * 0.08f;
		rig.Trident.GlobalTransform = new Transform3D( Shapes.LookAt( up, turn * Vector3.Back ).Scaled( Vector3.One * 3.2f * size ), rig.Hand.GlobalPosition + turn * Vector3.Forward * 0.14f * size );
		return root;
	}
}
