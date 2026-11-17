using Godot;

/// <summary>
/// The mermaids' looks (the mermaid pass): alien but lightly cartoony, like the rest of the creatures. One smooth body
/// (a torso lofted from hips to neck, flowing into a tapering tail of joined sections that ends in a two-lobed fluke),
/// painted skin in one colour with a paler front, a scaled tail shading from hip colour to tip colour, a big head with
/// large glossy alien eyes, ear fins and gill slits, and a mouth that is a thin line until it attacks, when it gapes on
/// needle teeth. Women wear their hair long, a shell tiara and a shell top; men a sculpted gold helmet with a fin crest
/// and gold shoulder plates. Fin hands, finned forearms, a frill of fins where skin meets tail. Model units (the body
/// node is scaled by <see cref="Mermaid.Scale"/>); the mermaid faces -Z.
/// </summary>
public partial class Mermaid
{
	Node3D maw;
	float mawOpen;

	static readonly Shader BodySkin = new()
	{
		Code = @"shader_type spatial;
// Mermaid skin: one colour, paler down the front, a soft shimmer at the edges; flashes white when hit.
uniform vec3 colour : source_color = vec3(0.6, 0.66, 0.95);
uniform vec3 belly : source_color = vec3(0.86, 0.9, 1.0);
uniform vec3 shimmer : source_color = vec3(0.6, 1.0, 0.95);
uniform float flash = 0.0;
uniform vec3 centre = vec3(0.0);
uniform vec4 pin = vec4(0.0);
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
	float edge = smoothstep(0.38, 0.5, d);
	float shine = (1.0 - smoothstep(0.0, 0.35, d)) * 0.25;
	vec3 c = mix(top, tip, smoothstep(0.0, 1.0, UV.y));
	c = c * (1.0 - edge * 0.38) + vec3(shine);
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = mix(c, vec3(2.2), flash);
	ROUGHNESS = 0.3;
	METALLIC = 0.15;
	EMISSION = c * rim * 0.35;
}",
	};

	/// <summary>
	/// A smooth tube through rings (height, half width, half depth, forward offset), closed at both ends; UV.x round it,
	/// UV.y from v0 at the first ring to v1 at the last.
	/// </summary>
	static ArrayMesh Loft( IReadOnlyList<(float Y, float Rx, float Rz, float Z)> rings, float v0 = 0f, float v1 = 1f, int sides = 18 )
	{
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		Vector3 At( int r, int k )
		{
			var a = k / (float)sides * MathF.Tau;
			var ring = rings[r];
			return new Vector3( MathF.Sin( a ) * ring.Rx, ring.Y, ring.Z - MathF.Cos( a ) * ring.Rz );
		}
		Vector2 Uv( int r, int k ) => new( k / (float)sides, MathX.Lerp( v0, v1, r / (float)(rings.Count - 1) ) );
		for ( var r = 0; r < rings.Count - 1; r++ )
			for ( var k = 0; k < sides; k++ )
			{
				// Rings go downward or upward: wind so the faces point out either way.
				var down = rings[r + 1].Y < rings[r].Y;
				var q = new[] { (r, k), (r + 1, k), (r + 1, k + 1), (r, k), (r + 1, k + 1), (r, k + 1) };
				if ( down ) q = new[] { (r, k), (r + 1, k + 1), (r + 1, k), (r, k), (r, k + 1), (r + 1, k + 1) };
				foreach ( var (rr, kk) in q ) { st.SetUV( Uv( rr, kk ) ); st.AddVertex( At( rr, kk ) ); }
			}
		// Caps: a fan to the middle of each end ring.
		foreach ( var (r, outward) in new[] { (0, -1f), (rings.Count - 1, 1f) } )
		{
			var down = rings[^1].Y < rings[0].Y;
			var centre = new Vector3( 0f, rings[r].Y, rings[r].Z );
			for ( var k = 0; k < sides; k++ )
			{
				var a = At( r, k );
				var b = At( r, k + 1 );
				var flip = (outward > 0f) == down;
				foreach ( var v in flip ? new[] { centre, a, b } : new[] { centre, b, a } ) { st.SetUV( Uv( r, 0 ) ); st.AddVertex( v ); }
			}
		}
		st.GenerateNormals();
		return st.Commit();
	}

	static MeshInstance3D Put( Node3D parent, Mesh mesh, Material material, string name, Vector3? at = null )
	{
		var m = new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = material };
		parent.AddChild( m );
		if ( at is { } p ) m.Position = p;
		return m;
	}

	/// <summary> A flat membrane with a few darker rays, fanned from a base (fins, ears, flukes). </summary>
	static void Fin( Node3D parent, Vector3 baseAt, Basis turn, float length, float width, Material membrane, Material ray, int rays = 4 )
	{
		var holder = new Node3D { Name = "Fin" };
		parent.AddChild( holder );
		holder.Transform = new Transform3D( turn, baseAt );
		// The membrane: a soft leaf shape (a squashed sphere), the rays over it.
		Sculpt.Blob( holder, new Vector3( 0f, -length * 0.5f, 0f ), new Vector3( width, length, length * 0.03f + 0.012f ), membrane, null, "Membrane" );
		for ( var i = 0; i < rays; i++ )
		{
			var a = (i - (rays - 1) * 0.5f) / Math.Max( 1, rays - 1 ) * 0.8f;
			Sculpt.Limb( holder, Vector3.Zero, new Vector3( MathF.Sin( a ) * width * 0.45f, -length * 0.95f * MathF.Cos( a * 0.5f ), 0f ), 0.016f, 0.006f, ray, "Ray" );
		}
	}

	Skeleton3D tailBones;

	/// <summary> The tail's thickness down its length (0 at the hips, 1 at the fluke), in model units. </summary>
	static float TailRadius( float t ) => MathX.Lerp( 0.43f, 0.11f, MathF.Pow( MathX.Clamp( t, 0f, 1f ), 0.85f ) ) + MathF.Sin( MathX.Clamp( t / 0.35f, 0f, 1f ) * MathF.PI ) * 0.05f;

	/// <summary>
	/// The tail as one smooth skinned mesh: a skeleton with a bone at each tail joint, every ring of the tube weighted
	/// between the two bones it lies between, so it bends without seams. The joints still move; <see cref="PoseTail"/>
	/// copies them onto the bones each frame.
	/// </summary>
	void BuildTailSkin( Material paint )
	{
		tailBones = new Skeleton3D { Name = "Tail Bones" };
		body.AddChild( tailBones );
		for ( var i = 0; i < tail.Count; i++ )
		{
			tailBones.AddBone( $"Tail {i}" );
			if ( i > 0 ) tailBones.SetBoneParent( i, i - 1 );
			tailBones.SetBoneRest( i, tail[i].Transform );
			tailBones.SetBonePosePosition( i, tail[i].Position );
		}
		const float top = -0.05f, step = 0.36f;
		var bottom = -0.15f - step * (tail.Count - 1) - 0.32f;
		const int rings = 44, sides = 18;
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		Vector3 Ring( float y, int k )
		{
			var t = (top - y) / (top - bottom);
			var r = TailRadius( t );
			var a = k / (float)sides * MathF.Tau;
			return new Vector3( MathF.Sin( a ) * r, y, -MathF.Cos( a ) * r * 0.82f );
		}
		void Vertex( float y, int k )
		{
			var f = MathX.Clamp( (-0.15f - y) / step, 0f, tail.Count - 1f );
			var b0 = Math.Min( (int)f, tail.Count - 2 );
			var w = MathX.Clamp( f - b0, 0f, 1f );
			st.SetBones( new[] { b0, b0 + 1, 0, 0 } );
			st.SetWeights( new[] { 1f - w, w, 0f, 0f } );
			st.SetUV( new Vector2( k / (float)sides, (top - y) / (top - bottom) ) );
			st.AddVertex( Ring( y, k ) );
		}
		for ( var r = 0; r < rings; r++ )
		{
			var y0 = MathX.Lerp( top, bottom, r / (float)rings );
			var y1 = MathX.Lerp( top, bottom, (r + 1f) / rings );
			for ( var k = 0; k < sides; k++ )
			{
				Vertex( y0, k ); Vertex( y1, k + 1 ); Vertex( y1, k );
				Vertex( y0, k ); Vertex( y0, k + 1 ); Vertex( y1, k + 1 );
			}
		}
		st.GenerateNormals();
		var mesh = new MeshInstance3D { Name = "Tail", Mesh = st.Commit(), MaterialOverride = paint };
		tailBones.AddChild( mesh );
		mesh.Skeleton = new NodePath( ".." );
	}

	/// <summary> The bones follow the tail joints (called after the joints are animated). </summary>
	void PoseTail()
	{
		if ( tailBones is null || !IsInstanceValid( tailBones ) ) return;
		for ( var i = 0; i < tail.Count; i++ )
		{
			if ( !IsInstanceValid( tail[i] ) || tail[i].GetParent() is null ) return;
			tailBones.SetBonePosePosition( i, tail[i].Position );
			tailBones.SetBonePoseRotation( i, tail[i].Quaternion );
		}
	}

	/// <summary> When it dies: the lower tail from joint `from` gets a plain mesh of its own, to fly off as a piece. </summary>
	void TailPieceFrom( int from )
	{
		if ( from >= tail.Count || !IsInstanceValid( tail[from] ) || tailMaterial is null ) return;
		var rings = new List<(float, float, float, float)>();
		var length = 0.36f * (tail.Count - 1 - from) + 0.3f;
		for ( var k = 0; k <= 8; k++ )
		{
			var y = -length * k / 8f;
			var t = (0.1f + 0.36f * from - y) / (0.36f * (tail.Count - 1) + 0.42f);
			var r = TailRadius( t );
			rings.Add( (y, r, r * 0.82f, 0f) );
		}
		Put( tail[from], Loft( rings, 0.45f, 1f ), tailMaterial, "Tail Piece" );
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
		var shellRib = Sculpt.Paint( ShellDark, 0.45f );
		var fin = Sculpt.Membrane( finColour, 0.7f );
		var ray = Sculpt.Paint( tailTip.Darkened( 0.35f ), 0.4f );
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
		BuildTailSkin( tailPaint );
		// The fluke: two broad lobes swept back and out, and a smaller pair behind them.
		foreach ( var s in new[] { -1f, 1f } )
		{
			Fin( parent, new Vector3( s * 0.05f, -0.35f, 0f ), new Basis( Vector3.Back, s * 0.95f ) * new Basis( Vector3.Right, 0.15f ), 0.85f, 0.5f, fin, ray, 5 );
			Fin( parent, new Vector3( s * 0.03f, -0.3f, 0.02f ), new Basis( Vector3.Back, s * 0.45f ), 0.5f, 0.3f, fin, ray, 3 );
		}
		// Hip fins, and a low ridge fin down the back of the tail.
		foreach ( var s in new[] { -1f, 1f } )
			Fin( tail[1], new Vector3( s * 0.36f, -0.1f, 0.05f ), new Basis( Vector3.Back, s * 1.0f ) * new Basis( Vector3.Right, -0.25f ), 0.5f, 0.32f, fin, ray, 3 );
		for ( var i = 2; i <= 5; i++ )
			Fin( tail[i], new Vector3( 0f, -0.05f, TailRadius( i / 9f ) * 0.7f ), new Basis( Vector3.Right, -1.25f ), 0.28f, 0.2f, fin, ray, 2 );

		// ---- The torso, lofted from the hips to the neck ----
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
			(1.03f, 0.35f * broad, 0.22f, 0.01f),
			(1.1f, 0.18f, 0.15f, 0.02f),
			(1.34f, 0.14f, 0.12f, 0f),
		};
		Put( chest, Loft( torso ), skin, "Torso" );
		// Where skin meets scales: a frill of small fins round the hips, in the tail's colours.
		for ( var k = 0; k < 12; k++ )
		{
			var a = k / 12f * MathF.Tau;
			var foot = new Vector3( MathF.Sin( a ) * 0.4f, -0.22f, -MathF.Cos( a ) * 0.33f );
			var down = new Vector3( MathF.Sin( a ) * 0.35f, -1f, -MathF.Cos( a ) * 0.3f ).Normalized();
			Sculpt.Cone( chest, foot, foot + down * 0.26f, 0.075f, k % 2 == 0 ? fin : tailPaint, 5, "Frill" );
		}
		if ( Male )
		{
			// Gold shoulder plates, and a gold belt.
			foreach ( var s in new[] { -1f, 1f } )
				Sculpt.Blob( chest, new Vector3( s * 0.5f, 1.02f, 0f ), new Vector3( 0.42f, 0.24f, 0.42f ), gold, new Basis( Vector3.Back, -s * 0.35f ), "Pauldron" );
			var belt = new MeshInstance3D { Name = "Belt", Mesh = new TorusMesh { InnerRadius = 0.36f, OuterRadius = 0.44f, Rings = 24, RingSegments = 6 }, MaterialOverride = gold };
			chest.AddChild( belt );
			belt.Position = new Vector3( 0f, -0.2f, 0f );
			belt.Scale = new Vector3( 1f, 1f, 0.85f );
		}
		else
		{
			// A shell top: a ribbed scallop on each side, on a thin cord.
			foreach ( var s in new[] { -1f, 1f } )
			{
				var cup = new Vector3( s * 0.17f, 0.76f, -0.25f );
				Sculpt.Blob( chest, cup, new Vector3( 0.28f, 0.25f, 0.12f ), shell, new Basis( Vector3.Back, -s * 0.15f ), "Shell" );
				for ( var k = -3; k <= 3; k++ )
					Sculpt.Block( chest, cup + new Vector3( k * 0.032f, 0.0f, -0.055f ), new Vector3( 0.016f, 0.22f, 0.02f ), shellRib, new Basis( Vector3.Back, -k * 0.2f - s * 0.15f ), "Rib" );
				Sculpt.Blob( chest, cup + new Vector3( 0f, -0.11f, -0.03f ), new Vector3( 0.06f, 0.04f, 0.05f ), shellRib, null, "Hinge" );
			}
			Sculpt.Limb( chest, new Vector3( -0.05f, 0.82f, -0.28f ), new Vector3( 0.05f, 0.82f, -0.28f ), 0.012f, 0.012f, shellRib, "Cord" );
		}
		// Gill slits on the neck.
		foreach ( var s in new[] { -1f, 1f } )
			for ( var k = 0; k < 3; k++ )
				Sculpt.Blob( chest, new Vector3( s * 0.13f, 1.16f + k * 0.045f, -0.02f ), new Vector3( 0.012f, 0.025f, 0.09f ), Sculpt.Paint( skinColour.Darkened( 0.45f ), 0.5f ), new Basis( Vector3.Up, s * 0.5f ), "Gill" );

		// ---- The head: big, with large glossy alien eyes ----
		head = Sculpt.Joint( chest, new Vector3( 0f, 1.32f, -0.02f ), "Head" );
		// One smooth head: a big rounded cranium narrowing to a small soft chin (no muzzle).
		var skull = new List<(float, float, float, float)>
		{
			(-0.02f, 0.05f, 0.05f, -0.06f),
			(0.04f, 0.13f, 0.13f, -0.06f),
			(0.14f, 0.22f, 0.24f, -0.03f),
			(0.28f, 0.3f, 0.3f, 0f),
			(0.44f, 0.32f, 0.31f, 0.02f),
			(0.58f, 0.27f, 0.27f, 0.03f),
			(0.66f, 0.15f, 0.15f, 0.03f),
			(0.69f, 0.03f, 0.03f, 0.03f),
		};
		Put( head, Loft( skull, 0f, 1f, 20 ), handSkin, "Head" );
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
			var ball = Sculpt.Blob( head, eyeAt, new Vector3( 0.22f, 0.13f, 0.07f ), black, slant, "Eye" );
			var ring = Sculpt.Blob( head, eyeAt + new Vector3( s * 0.012f, -0.012f, -0.03f ), new Vector3( 0.08f, 0.07f, 0.02f ), iris, slant, "Iris" );
			var glint = Sculpt.Blob( head, eyeAt + new Vector3( -s * 0.04f, 0.025f, -0.036f ), Vector3.One * 0.028f, white, null, "Glint" );
			(blinks ??= Blinker.Add( this )).Eye( ball, ring, glint );
			// An ear fin: narrow and pointed, swept back.
			Fin( head, new Vector3( s * 0.29f, 0.32f, 0.05f ), new Basis( Vector3.Up, -s * 0.9f ) * new Basis( Vector3.Back, s * 1.75f ), 0.36f, 0.12f, fin, ray, 3 );
		}
		// Two small nostril slits, no nose to speak of.
		foreach ( var s in new[] { -1f, 1f } )
			Sculpt.Blob( head, new Vector3( s * 0.022f, 0.2f, -0.285f ), new Vector3( 0.012f, 0.024f, 0.012f ), Sculpt.Paint( skinColour.Darkened( 0.5f ), 0.5f ), null, "Nostril" );
		// The mouth: a dark line until it attacks, when it gapes open on needle teeth.
		maw = Sculpt.Joint( head, new Vector3( 0f, 0.12f, -0.24f ), "Maw" );
		Sculpt.Blob( maw, Vector3.Zero, new Vector3( 0.17f, 0.15f, 0.05f ), Sculpt.Paint( new Color( 0.12f, 0.02f, 0.05f ), 0.5f ), null, "Inside" );
		var tooth = Sculpt.Paint( new Color( 0.98f, 0.96f, 0.9f ), 0.25f );
		for ( var k = 0; k < 7; k++ )
		{
			var x = (k - 3f) * 0.022f;
			var len = 0.045f + (k % 2) * 0.02f;
			Sculpt.Cone( maw, new Vector3( x, 0.065f, -0.02f ), new Vector3( x * 0.9f, 0.065f - len, -0.022f ), 0.009f, tooth, 4, "Tooth" );
			Sculpt.Cone( maw, new Vector3( x + 0.011f, -0.065f, -0.02f ), new Vector3( x * 0.9f + 0.011f, -0.065f + len * 0.85f, -0.022f ), 0.008f, tooth, 4, "Tooth" );
		}
		maw.Scale = new Vector3( 1f, 0.08f, 1f );

		if ( Male )
		{
			// A gold helmet: a dome over the crown, a brow band, cheek guards, a gem, and a fin crest from brow to nape.
			Sculpt.Blob( head, new Vector3( 0f, 0.56f, 0.05f ), new Vector3( 0.7f, 0.38f, 0.68f ), gold, null, "Helmet" );
			var band = new MeshInstance3D { Name = "Brow Band", Mesh = new TorusMesh { InnerRadius = 0.29f, OuterRadius = 0.345f, Rings = 24, RingSegments = 6 }, MaterialOverride = gold };
			head.AddChild( band );
			band.Position = new Vector3( 0f, 0.44f, 0.03f );
			foreach ( var s in new[] { -1f, 1f } )
				Sculpt.Blob( head, new Vector3( s * 0.29f, 0.3f, 0.04f ), new Vector3( 0.07f, 0.24f, 0.2f ), gold, new Basis( Vector3.Up, s * 0.35f ), "Cheek Guard" );
			Sculpt.Blob( head, new Vector3( 0f, 0.47f, -0.3f ), new Vector3( 0.08f, 0.1f, 0.04f ), Sculpt.Glow( new Color( 0.66f, 0.22f, 1f ), 2f ), null, "Gem" );
			Fin( head, new Vector3( 0f, 0.62f, -0.12f ), new Basis( Vector3.Right, -1.9f ), 0.55f, 0.32f, fin, gold, 5 );
		}
		else
		{
			// Long hair: a full crown and locks down her back (swaying), two locks forward over the shoulders, a shell tiara.
			Sculpt.Blob( head, new Vector3( 0f, 0.46f, 0.12f ), new Vector3( 0.68f, 0.56f, 0.6f ), hairPaint, null, "Crown" );
			for ( var k = 0; k < 7; k++ )
			{
				var x = (k - 3f) * 0.08f;
				var strand = Sculpt.Joint( head, new Vector3( x, 0.36f, 0.22f ), $"Hair {k}" );
				var length = 1.0f + (k % 3) * 0.18f;
				var mid = new Vector3( x * 0.5f, -length * 0.5f, 0.14f );
				Sculpt.Limb( strand, Vector3.Zero, mid, 0.11f, 0.08f, hairPaint, "Strand" );
				Sculpt.Limb( strand, mid, new Vector3( x * 0.8f, -length, 0.05f ), 0.08f, 0.02f, hairPaint, "Strand" );
				hair.Add( strand );
			}
			foreach ( var s in new[] { -1f, 1f } )
			{
				Sculpt.Limb( head, new Vector3( s * 0.26f, 0.32f, -0.08f ), new Vector3( s * 0.3f, -0.05f, -0.14f ), 0.08f, 0.06f, hairPaint, "Lock" );
				Sculpt.Limb( head, new Vector3( s * 0.3f, -0.05f, -0.14f ), new Vector3( s * 0.24f, -0.4f, -0.18f ), 0.06f, 0.02f, hairPaint, "Lock" );
			}
			// The tiara: three small scallops at the hairline, pearls between them.
			for ( var k = -1; k <= 1; k++ )
			{
				var at2 = new Vector3( k * 0.13f, 0.56f - MathF.Abs( k ) * 0.04f, -0.25f + MathF.Abs( k ) * 0.06f );
				var tilt = new Basis( Vector3.Back, -k * 0.35f ) * new Basis( Vector3.Right, 0.25f );
				Sculpt.Blob( head, at2, new Vector3( k == 0 ? 0.13f : 0.1f, k == 0 ? 0.12f : 0.09f, 0.03f ), shell, tilt, "Tiara" );
				for ( var r = -2; r <= 2; r++ )
					Sculpt.Block( head, at2 + tilt * new Vector3( r * 0.016f, 0f, -0.014f ), new Vector3( 0.008f, k == 0 ? 0.1f : 0.08f, 0.01f ), shellRib, tilt * new Basis( Vector3.Back, -r * 0.25f ), "Rib" );
			}
			foreach ( var s in new[] { -1f, 1f } )
				Sculpt.Blob( head, new Vector3( s * 0.07f, 0.5f, -0.28f ), Vector3.One * 0.035f, Sculpt.Paint( new Color( 0.98f, 0.95f, 0.98f ), 0.15f ), null, "Pearl" );
		}

		// ---- Arms: a round shoulder, a fin down the back of the forearm, a bracelet, a webbed fin hand ----
		(Node3D Arm, Node3D Fore, Node3D Hand) Arm( float s )
		{
			var arm = Sculpt.Joint( chest, new Vector3( s * width * 0.48f, 0.98f, 0f ), s > 0 ? "Right Arm" : "Left Arm" );
			Sculpt.Blob( arm, Vector3.Zero, Vector3.One * (Male ? 0.3f : 0.22f), skin, null, "Shoulder" );
			Sculpt.Limb( arm, Vector3.Zero, new Vector3( 0f, -0.55f, 0f ), Male ? 0.14f : 0.1f, Male ? 0.11f : 0.08f, skin, "Upper Arm" );
			var fore = Sculpt.Joint( arm, new Vector3( 0f, -0.55f, 0f ), "Elbow" );
			Sculpt.Blob( fore, Vector3.Zero, Vector3.One * (Male ? 0.22f : 0.16f), skin, null, "Elbow" );
			Sculpt.Limb( fore, Vector3.Zero, new Vector3( 0f, -0.5f, 0f ), Male ? 0.11f : 0.08f, 0.065f, skin, "Forearm" );
			Fin( fore, new Vector3( 0f, -0.08f, 0.07f ), new Basis( Vector3.Right, -0.25f ), 0.42f, 0.16f, fin, ray, 3 );
			var cuff = new MeshInstance3D { Name = "Bracelet", Mesh = new TorusMesh { InnerRadius = 0.07f, OuterRadius = 0.1f, Rings = 16, RingSegments = 6 }, MaterialOverride = Male ? gold : shell };
			fore.AddChild( cuff );
			cuff.Position = new Vector3( 0f, -0.42f, 0f );
			var hand = Sculpt.Joint( fore, new Vector3( 0f, -0.55f, 0f ), "Hand" );
			Sculpt.Blob( hand, Vector3.Zero, new Vector3( 0.14f, 0.16f, 0.1f ), skin, null, "Palm" );
			for ( var k = 0; k < 4; k++ )
			{
				var a = (k - 1.5f) * 0.3f;
				Sculpt.Limb( hand, new Vector3( MathF.Sin( a ) * 0.05f, -0.05f, 0f ), new Vector3( MathF.Sin( a ) * 0.15f, -0.24f, 0f ), 0.022f, 0.01f, skin, "Finger" );
			}
			Sculpt.Blob( hand, new Vector3( 0f, -0.15f, 0.005f ), new Vector3( 0.26f, 0.2f, 0.012f ), fin, null, "Web" );
			return (arm, fore, hand);
		}
		(armR, foreR, handR) = Arm( 1f );
		(armL, foreL, handL) = Arm( -1f );
		return (width, gold, fin);
	}
}
