using Godot;
using static SmoothKit;

/// <summary>
/// The mermaids' looks: alien but lightly cartoony, kept smooth and simple. Every round part is a smooth mesh (lofted
/// through spline-smoothed rings, or a high-segment ball); limbs are tubes with rounded ends; fins and shells are one
/// curved fan each (fins painted with rays by <see cref="FinShade"/>, which also ripples them; shells ribbed in the
/// mesh). The tail and the hair are skinned meshes bent by bones that follow their joints (<see cref="PoseChains"/>).
/// Meshes are built once and shared. Model units (the body node is scaled by <see cref="Mermaid.Scale"/>); it faces -Z.
/// </summary>
public partial class Mermaid
{
	Node3D maw;
	float mawOpen;
	readonly List<(Skeleton3D Bones, List<Node3D> Joints)> chains = new();
	readonly List<Node3D> hairMid = new(), hairTip = new(), gills = new();
	ShaderMaterial finPaint, crestPaint;

	static readonly Shader BodySkin = new()
	{
		Code = @"shader_type spatial;
// Mermaid skin: one colour, paler down the front, a soft shimmer at the edges; flashes white when hit.
uniform vec3 colour : source_color = vec3(0.6, 0.66, 0.95);
uniform vec3 belly : source_color = vec3(0.86, 0.9, 1.0);
uniform vec3 shimmer : source_color = vec3(0.6, 1.0, 0.95);
uniform float flash = 0.0;
varying vec3 local_n;
void vertex() { local_n = NORMAL; }
void fragment() {
	float front = smoothstep(0.1, 0.85, -local_n.z);
	vec3 c = mix(colour, belly, front * 0.55);
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = mix(c, vec3(2.2), flash);
	ROUGHNESS = 0.45;
	EMISSION = shimmer * rim * 0.18 + vec3(1.6) * flash * 0.3;
}",
	};

	static readonly Shader TailScales = new()
	{
		Code = @"shader_type spatial;
// A scaled tail: rows of rounded scales (each darker at its lower edge, with a light top), shading from the hip colour
// to the tip colour down the tail; UV.y runs 0 at the hips to 1 at the fluke, UV.x round the tail.
uniform vec3 top : source_color = vec3(0.62, 0.25, 0.9);
uniform vec3 tip : source_color = vec3(0.2, 0.7, 0.85);
uniform float rows = 46.0;
uniform float around = 14.0;
uniform float flash = 0.0;
void fragment() {
	vec2 g = vec2(UV.x * around, UV.y * rows);
	float row = floor(g.y);
	g.x += mod(row, 2.0) * 0.5;
	vec2 f = vec2(fract(g.x) - 0.5, fract(g.y));
	float d = length(f * vec2(1.0, 0.85));
	float edge = smoothstep(0.36, 0.5, d);
	float shine = (1.0 - smoothstep(0.0, 0.35, d)) * 0.2;
	vec3 c = mix(top, tip, smoothstep(0.0, 1.0, UV.y));
	c = c * (1.0 - edge * 0.3) + vec3(shine);
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = mix(c, vec3(2.2), flash);
	ROUGHNESS = 0.3;
	METALLIC = 0.15;
	EMISSION = c * rim * 0.35;
}",
	};

	void Chain( Node3D parent, List<Node3D> joints, float step, float above, float below, Func<float, (float Rx, float Rz)> radius, Material paint, string key, int rings, int sides ) =>
		chains.Add( (SkinChain( parent, joints, step, above, below, radius, paint, key, rings, sides ), joints) );

	void PoseChains() { foreach ( var (bones, joints) in chains ) PoseChain( bones, joints ); }

	/// <summary> The tail's thickness down its length (0 at the hips, 1 at the fluke), in model units. </summary>
	static float TailRadius( float t ) => MathX.Lerp( 0.43f, 0.1f, MathF.Pow( MathX.Clamp( t, 0f, 1f ), 0.85f ) ) + MathF.Sin( MathX.Clamp( t / 0.35f, 0f, 1f ) * MathF.PI ) * 0.05f;

	/// <summary> When it dies: the lower tail from joint `from` gets a plain mesh of its own, to fly off as a piece. </summary>
	void TailPieceFrom( int from )
	{
		if ( from >= tail.Count || !IsInstanceValid( tail[from] ) || tailMaterial is null ) return;
		var rings = new List<(float, float, float, float)>();
		var length = 0.36f * (tail.Count - 1 - from) + 0.3f;
		for ( var k = 0; k <= 16; k++ )
		{
			var y = -length * k / 16f;
			var t = (0.1f + 0.36f * from - y) / (0.36f * (tail.Count - 1) + 0.42f);
			var r = TailRadius( t );
			rings.Add( (y, r, r * 0.82f, 0f) );
		}
		Put( tail[from], Loft( rings, 0.45f, 1f, 24 ), tailMaterial, "Tail Piece" );
	}

	/// <summary> The body, head, hair or helmet, arms and fins. Leaves the shield, weapon and hit shapes to Build. </summary>
	(float Width, Material Gold, Material Fins) BuildLook()
	{
		// Colours: women lavender-blue or seafoam with a magenta-to-teal or violet-to-gold tail; men sea-green with a teal
		// to gold tail. Kept clear of the enemies' neon green and of the player's colours.
		var variant = (int)(GetInstanceId() % 2);
		var skinColour = Male ? new Color( 0.36f, 0.66f, 0.62f ) : variant == 0 ? new Color( 0.62f, 0.66f, 0.96f ) : new Color( 0.52f, 0.86f, 0.82f );
		var tailTop = Male ? new Color( 0.08f, 0.45f, 0.5f ) : variant == 0 ? new Color( 0.82f, 0.25f, 0.62f ) : new Color( 0.5f, 0.22f, 0.85f );
		var tailTip = Male ? new Color( 0.95f, 0.72f, 0.25f ) : variant == 0 ? new Color( 0.18f, 0.65f, 0.85f ) : new Color( 0.98f, 0.75f, 0.3f );
		var finColour = tailTip.Lerp( Colors.White, 0.25f );

		skin = new ShaderMaterial { Shader = BodySkin };
		skin.SetShaderParameter( "colour", skinColour );
		skin.SetShaderParameter( "belly", skinColour.Lerp( Colors.White, 0.55f ) );
		skin.SetShaderParameter( "shimmer", tailTip.Lerp( Colors.White, 0.4f ) );
		// The head and hands: the same skin without the paler front (it read as a pale muzzle on the face).
		handSkin = new ShaderMaterial { Shader = BodySkin };
		handSkin.SetShaderParameter( "colour", skinColour );
		handSkin.SetShaderParameter( "belly", skinColour );
		handSkin.SetShaderParameter( "shimmer", tailTip.Lerp( Colors.White, 0.4f ) );
		var tailPaint = new ShaderMaterial { Shader = TailScales };
		tailPaint.SetShaderParameter( "top", tailTop );
		tailPaint.SetShaderParameter( "tip", tailTip );
		tailMaterial = tailPaint;
		var gold = Sculpt.Paint( Gold, 0.18f, 0.9f );
		gold.EmissionEnabled = true;
		gold.Emission = Gold;
		gold.EmissionEnergyMultiplier = 0.1f;
		var shell = Sculpt.Paint( Shell, 0.4f );
		shell.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		var fin = finPaint = FinMaterial( finColour, tailTip.Darkened( 0.3f ), 0.72f, 0.12f );
		crestPaint = FinMaterial( Gold, Gold.Darkened( 0.4f ), 1f, 0.05f );
		var hairPaint = Sculpt.Paint( HairColours[HairColour], 0.55f );

		body = new Node3D { Name = "Body", Scale = Vector3.One * Scale };
		AddChild( body );

		// ---- The tail: nine joints (they swim and take the hits), one smooth skinned mesh bent by a bone per joint ----
		Node3D parent = body;
		var at = new Vector3( 0f, -0.15f, 0f );
		for ( var i = 0; i < 9; i++ )
		{
			var joint = Sculpt.Joint( parent, at, $"Tail {i}" );
			tail.Add( joint );
			parent = joint;
			at = new Vector3( 0f, -0.36f, 0f );
		}
		Chain( body, tail, 0.36f, 0.1f, 0.32f, t => (TailRadius( t ), TailRadius( t ) * 0.82f), tailPaint, "Tail", 64, 24 );
		// The fluke: two broad lobes swept back and out, and a smaller pair behind them.
		foreach ( var s in new[] { -1f, 1f } )
		{
			Fin( parent, new Vector3( s * 0.04f, -0.3f, 0f ), new Basis( Vector3.Back, s * 0.95f ) * new Basis( Vector3.Right, 0.12f ), 0.9f, 0.55f, fin, 0.08f );
			Fin( parent, new Vector3( s * 0.02f, -0.28f, 0.02f ), new Basis( Vector3.Back, s * 0.4f ), 0.5f, 0.3f, fin, 0.08f );
		}
		// Hip fins, and a low ridge fin down the back of the tail.
		foreach ( var s in new[] { -1f, 1f } )
			Fin( tail[1], new Vector3( s * 0.36f, -0.1f, 0.05f ), new Basis( Vector3.Back, s * 1.0f ) * new Basis( Vector3.Right, -0.25f ), 0.5f, 0.34f, fin );
		for ( var i = 2; i <= 5; i++ )
			Fin( tail[i], new Vector3( 0f, -0.05f, TailRadius( i / 9f ) * 0.72f ), new Basis( Vector3.Right, -1.25f ), 0.3f, 0.26f, fin, 0.05f );

		// ---- The torso, lofted from the hips to the neck on a smooth spline ----
		chest = Sculpt.Joint( body, new Vector3( 0f, 0.15f, 0f ), "Chest" );
		var width = Male ? 1.2f : 0.9f;
		var broad = Male ? 1.32f : 1f;
		var torso = new List<(float, float, float, float)>
		{
			(-0.32f, 0.42f, 0.35f, 0f),
			(-0.05f, 0.37f, 0.3f, 0f),
			(0.22f, Male ? 0.33f : 0.27f, 0.24f, -0.01f),
			(0.48f, (Male ? 0.36f : 0.29f), 0.25f, -0.02f),
			(0.72f, 0.36f * broad, 0.27f, -0.03f),
			(0.9f, 0.4f * broad, 0.26f, -0.01f),
			(1.03f, 0.33f * broad, 0.21f, 0.01f),
			(1.12f, 0.17f, 0.15f, 0.02f),
			(1.34f, 0.14f, 0.12f, 0f),
		};
		Put( chest, Cached( $"torso {Male}", () => Loft( Smooth( torso ) ) ), skin, "Torso" );
		// Where skin meets scales: a ring of soft fins round the hips, rippling.
		for ( var k = 0; k < 10; k++ )
		{
			var a = (k + 0.5f) / 10f * MathF.Tau;
			var foot = new Vector3( MathF.Sin( a ) * 0.38f, -0.18f, -MathF.Cos( a ) * 0.31f );
			var down = new Vector3( MathF.Sin( a ) * 0.45f, -1f, -MathF.Cos( a ) * 0.4f );
			Fin( chest, foot, Aim( down, new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) ), 0.32f, 0.3f, fin, 0.1f );
		}
		if ( Male )
		{
			// Gold shoulder plates, and a gold belt.
			foreach ( var s in new[] { -1f, 1f } )
				Ball( chest, new Vector3( s * 0.5f, 1.0f, 0f ), new Vector3( 0.42f, 0.24f, 0.42f ), gold, new Basis( Vector3.Back, -s * 0.35f ), "Pauldron" );
			var belt = new MeshInstance3D { Name = "Belt", Mesh = new TorusMesh { InnerRadius = 0.36f, OuterRadius = 0.43f, Rings = 40, RingSegments = 12 }, MaterialOverride = gold };
			chest.AddChild( belt );
			belt.Position = new Vector3( 0f, -0.2f, 0f );
			belt.Scale = new Vector3( 1f, 1f, 0.85f );
		}
		else
		{
			// A shell top: a ribbed scallop on each side, hinge down, on a thin cord.
			foreach ( var s in new[] { -1f, 1f } )
				Scallop( chest, new Vector3( s * 0.16f, 0.64f, -0.27f ), new Basis( Vector3.Back, MathF.PI - s * 0.12f ) * new Basis( Vector3.Right, -0.25f ), 0.26f, 0.32f, shell );
			Tube( chest, new Vector3( -0.06f, 0.8f, -0.28f ), new Vector3( 0.06f, 0.8f, -0.28f ), 0.012f, 0.012f, shell, "Cord" );
		}
		// Gill slits on the neck (they flare as it breathes).
		var gillPaint = Sculpt.Paint( skinColour.Darkened( 0.45f ), 0.5f );
		foreach ( var s in new[] { -1f, 1f } )
			for ( var k = 0; k < 3; k++ )
			{
				var g = Sculpt.Joint( chest, new Vector3( s * 0.13f, 1.16f + k * 0.045f, -0.02f ), "Gill" );
				Ball( g, Vector3.Zero, new Vector3( 0.014f, 0.025f, 0.09f ), gillPaint, new Basis( Vector3.Up, s * 0.5f ), "Slit" );
				gills.Add( g );
			}

		// ---- The head: big, with large glossy alien eyes ----
		head = Sculpt.Joint( chest, new Vector3( 0f, 1.32f, -0.02f ), "Head" );
		var skull = new List<(float, float, float, float)>
		{
			(-0.02f, 0.05f, 0.05f, -0.06f),
			(0.04f, 0.13f, 0.13f, -0.06f),
			(0.14f, 0.22f, 0.24f, -0.03f),
			(0.28f, 0.3f, 0.3f, 0f),
			(0.44f, 0.32f, 0.31f, 0.02f),
			(0.58f, 0.27f, 0.27f, 0.03f),
			(0.66f, 0.15f, 0.15f, 0.03f),
			(0.69f, 0.02f, 0.02f, 0.03f),
		};
		Put( head, Cached( "skull", () => Loft( Smooth( skull ), 0f, 1f, 32 ) ), handSkin, "Head" );
		var black = Sculpt.Paint( new Color( 0.02f, 0.02f, 0.035f ), 0.05f );
		black.ClearcoatEnabled = true;
		black.Clearcoat = 1f;
		black.ClearcoatRoughness = 0.02f;
		var irisColour = Male ? new Color( 1f, 0.72f, 0.2f ) : tailTip.Lerp( Colors.White, 0.2f );
		var iris = Sculpt.Glow( irisColour, 1.6f );
		var white = Sculpt.Glow( Colors.White, 1.2f );
		foreach ( var s in new[] { -1f, 1f } )
		{
			// A big almond eye, slanted up at the outer corner, a glowing ring of iris low in it and a highlight.
			var eyeAt = new Vector3( s * 0.13f, 0.31f, -0.28f );
			var slant = new Basis( Vector3.Back, -s * 0.32f ) * new Basis( Vector3.Up, s * 0.35f );
			var ball = Ball( head, eyeAt, new Vector3( 0.22f, 0.13f, 0.07f ), black, slant, "Eye" );
			var ring = Ball( head, eyeAt + new Vector3( s * 0.012f, -0.012f, -0.03f ), new Vector3( 0.08f, 0.07f, 0.02f ), iris, slant, "Iris" );
			var glint = Ball( head, eyeAt + new Vector3( -s * 0.04f, 0.025f, -0.036f ), Vector3.One * 0.028f, white, null, "Glint" );
			(blinks ??= Blinker.Add( this )).Eye( ball, ring, glint );
			// An ear fin: narrow and pointed, swept back.
			Fin( head, new Vector3( s * 0.28f, 0.34f, 0.06f ), new Basis( Vector3.Up, -s * 1.1f ) * new Basis( Vector3.Back, s * 2.1f ), 0.42f, 0.1f, fin, 0.05f );
		}
		// The mouth: a dark line until it attacks, when it gapes open on needle teeth.
		maw = Sculpt.Joint( head, new Vector3( 0f, 0.12f, -0.24f ), "Maw" );
		Ball( maw, Vector3.Zero, new Vector3( 0.17f, 0.15f, 0.05f ), Sculpt.Paint( new Color( 0.12f, 0.02f, 0.05f ), 0.5f ), null, "Inside" );
		var tooth = Sculpt.Paint( new Color( 0.98f, 0.96f, 0.9f ), 0.25f );
		for ( var k = 0; k < 7; k++ )
		{
			var x = (k - 3f) * 0.022f;
			var len = 0.045f + (k % 2) * 0.02f;
			Sculpt.Cone( maw, new Vector3( x, 0.065f, -0.02f ), new Vector3( x * 0.9f, 0.065f - len, -0.022f ), 0.009f, tooth, 8, "Tooth" );
			Sculpt.Cone( maw, new Vector3( x + 0.011f, -0.065f, -0.02f ), new Vector3( x * 0.9f + 0.011f, -0.065f + len * 0.85f, -0.022f ), 0.008f, tooth, 8, "Tooth" );
		}
		maw.Scale = new Vector3( 1f, 0.08f, 1f );

		if ( Male )
		{
			// A gold helmet: a dome over the crown, a brow band, a gem, and a fin crest from brow to nape.
			Ball( head, new Vector3( 0f, 0.52f, 0.04f ), new Vector3( 0.68f, 0.52f, 0.66f ), gold, null, "Helmet" );
			var band = new MeshInstance3D { Name = "Brow Band", Mesh = new TorusMesh { InnerRadius = 0.29f, OuterRadius = 0.345f, Rings = 40, RingSegments = 12 }, MaterialOverride = gold };
			head.AddChild( band );
			band.Position = new Vector3( 0f, 0.44f, 0.03f );
			Ball( head, new Vector3( 0f, 0.47f, -0.3f ), new Vector3( 0.08f, 0.1f, 0.04f ), Sculpt.Glow( new Color( 0.66f, 0.22f, 1f ), 2f ), null, "Gem" );
			Fin( head, new Vector3( 0f, 0.62f, -0.12f ), new Basis( Vector3.Right, -1.9f ), 0.6f, 0.36f, crestPaint, 0.05f );
		}
		else
		{
			// Long hair: a full crown, seven locks down her back (skinned, three joints each so they flow), two locks
			// forward over the shoulders, a shell tiara.
			Ball( head, new Vector3( 0f, 0.46f, 0.12f ), new Vector3( 0.68f, 0.56f, 0.6f ), hairPaint, null, "Crown" );
			for ( var k = 0; k < 7; k++ )
			{
				var x = (k - 3f) * 0.075f;
				var length = 1.0f + (k % 3) * 0.16f;
				var step = length / 2.6f;
				var root = Sculpt.Joint( head, new Vector3( x, 0.38f, 0.24f - MathF.Abs( x ) * 0.3f ), $"Hair {k}" );
				var mid = Sculpt.Joint( root, new Vector3( 0f, -step, 0f ), "Mid" );
				var tip = Sculpt.Joint( mid, new Vector3( 0f, -step, 0f ), "Tip" );
				Chain( head, new List<Node3D> { root, mid, tip }, step, 0.06f, step * 0.6f,
					t => (MathX.Lerp( 0.1f, 0.025f, MathF.Pow( t, 1.3f ) ), MathX.Lerp( 0.06f, 0.015f, t )), hairPaint, $"Hair {k} {length:F2}", 18, 12 );
				hair.Add( root );
				hairMid.Add( mid );
				hairTip.Add( tip );
			}
			foreach ( var s in new[] { -1f, 1f } )
			{
				Tube( head, new Vector3( s * 0.26f, 0.32f, -0.06f ), new Vector3( s * 0.3f, -0.05f, -0.13f ), 0.075f, 0.06f, hairPaint, "Lock" );
				Tube( head, new Vector3( s * 0.3f, -0.05f, -0.13f ), new Vector3( s * 0.24f, -0.42f, -0.17f ), 0.06f, 0.015f, hairPaint, "Lock" );
			}
			// The tiara: one small scallop at the front of the hairline, a pearl either side.
			Scallop( head, new Vector3( 0f, 0.5f, -0.3f ), new Basis( Vector3.Back, MathF.PI ) * new Basis( Vector3.Right, -0.35f ), 0.14f, 0.17f, shell );
			foreach ( var s in new[] { -1f, 1f } )
				Ball( head, new Vector3( s * 0.1f, 0.5f, -0.3f ), Vector3.One * 0.035f, Sculpt.Paint( new Color( 0.98f, 0.95f, 0.98f ), 0.15f ), null, "Pearl" );
		}

		// ---- Arms: smooth round-ended tubes, a fin down the back of the forearm, a bracelet, a webbed fin hand ----
		(Node3D Arm, Node3D Fore, Node3D Hand) Arm( float s )
		{
			var arm = Sculpt.Joint( chest, new Vector3( s * width * 0.48f, 0.98f, 0f ), s > 0 ? "Right Arm" : "Left Arm" );
			Ball( arm, Vector3.Zero, Vector3.One * (Male ? 0.28f : 0.21f), skin, null, "Shoulder" );
			Tube( arm, Vector3.Zero, new Vector3( 0f, -0.55f, 0f ), Male ? 0.13f : 0.095f, Male ? 0.105f : 0.078f, skin, "Upper Arm" );
			var fore = Sculpt.Joint( arm, new Vector3( 0f, -0.55f, 0f ), "Elbow" );
			Tube( fore, Vector3.Zero, new Vector3( 0f, -0.52f, 0f ), Male ? 0.105f : 0.078f, 0.06f, skin, "Forearm" );
			Fin( fore, new Vector3( 0f, -0.06f, 0.06f ), new Basis( Vector3.Right, -0.3f ) * new Basis( Vector3.Up, MathF.PI * 0.5f ), 0.42f, 0.18f, fin, 0.06f );
			var cuff = new MeshInstance3D { Name = "Bracelet", Mesh = new TorusMesh { InnerRadius = 0.065f, OuterRadius = 0.095f, Rings = 24, RingSegments = 10 }, MaterialOverride = Male ? gold : shell };
			fore.AddChild( cuff );
			cuff.Position = new Vector3( 0f, -0.42f, 0f );
			var hand = Sculpt.Joint( fore, new Vector3( 0f, -0.55f, 0f ), "Hand" );
			Ball( hand, new Vector3( 0f, -0.02f, 0f ), new Vector3( 0.13f, 0.16f, 0.09f ), skin, null, "Palm" );
			Fin( hand, new Vector3( 0f, -0.04f, 0f ), Basis.Identity, 0.28f, 0.26f, fin, 0.15f );
			return (arm, fore, hand);
		}
		(armR, foreR, handR) = Arm( 1f );
		(armL, foreL, handL) = Arm( -1f );
		return (width, gold, fin);
	}
}
