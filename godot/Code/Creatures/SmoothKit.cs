using Godot;

/// <summary>
/// Smooth shapes for creatures built in code (the mermaids, Poseidon): lofts through spline-smoothed rings, round-ended
/// tubes, high-segment balls, curved fans for fins and shells (fins painted and rippled by <see cref="FinShade"/>),
/// skinned tubes down a chain of joints (tails, hair, beards) and damped springs for animation. Meshes are cached by
/// their sizes and shared. The parent node's units; fans point down -Y and cup toward -Z.
/// </summary>
public static class SmoothKit
{
	static readonly Dictionary<string, ArrayMesh> Meshes = new();
	public static ArrayMesh Cached( string key, Func<ArrayMesh> make )
	{
		if ( !Meshes.TryGetValue( key, out var m ) ) Meshes[key] = m = make();
		return m;
	}

	public static readonly SphereMesh SmoothBall = new() { Radius = 0.5f, Height = 1f, RadialSegments = 32, Rings = 16 };

	/// <summary>
	/// Fins: see-through, with soft rays fanning from the base and a brighter rim, rippling from base to tip (`beat`
	/// is advanced by the animation, faster when swimming). UV.x across the fin, UV.y base to tip, UV2.x its length.
	/// </summary>
	public static readonly Shader FinShade = new()
	{
		Code = @"shader_type spatial;
render_mode cull_disabled;
uniform vec3 colour : source_color = vec3(0.6, 0.9, 1.0);
uniform vec3 ray : source_color = vec3(0.2, 0.4, 0.6);
uniform float alpha = 0.7;
uniform float rays = 6.0;
uniform float beat = 0.0;
uniform float ripple = 0.12;
void vertex() {
	float ph = dot(NODE_POSITION_WORLD, vec3(1.7, 0.9, 1.3));
	VERTEX.z += sin(beat + ph - UV.y * 3.0 + UV.x * 1.5) * ripple * UV2.x * UV.y * UV.y;
}
void fragment() {
	float d = abs(fract(UV.x * rays) - 0.5);
	float r = (1.0 - smoothstep(0.03, 0.11, d)) * (1.0 - smoothstep(0.75, 1.0, UV.y)) * smoothstep(0.0, 0.15, UV.y);
	float rim = smoothstep(0.82, 1.0, UV.y);
	vec3 c = mix(colour, ray, r * 0.75) + vec3(0.25) * rim;
	ALBEDO = c;
	ALPHA = clamp(alpha * (0.7 + 0.3 * UV.y) + r * 0.25 + rim * 0.15, 0.0, 1.0);
	ROUGHNESS = 0.35;
	EMISSION = colour * 0.08;
}",
	};

	// ---- Smooth mesh builders ----

	public static float CatmullRom( float a, float b, float c, float d, float t ) =>
		0.5f * (2f * b + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t * t + (-a + 3f * b - 3f * c + d) * t * t * t);

	/// <summary> Rings in between the given ones on a smooth spline, so a profile of a few rings lofts with no facets. </summary>
	public static List<(float Y, float Rx, float Rz, float Z)> Smooth( IReadOnlyList<(float Y, float Rx, float Rz, float Z)> p, int steps = 4 )
	{
		var o = new List<(float, float, float, float)>();
		for ( var i = 0; i < p.Count - 1; i++ )
		{
			var a = p[Math.Max( i - 1, 0 )];
			var b = p[i];
			var c = p[i + 1];
			var d = p[Math.Min( i + 2, p.Count - 1 )];
			for ( var s = 0; s < steps; s++ )
			{
				var t = s / (float)steps;
				o.Add( (CatmullRom( a.Y, b.Y, c.Y, d.Y, t ), MathF.Max( 0.002f, CatmullRom( a.Rx, b.Rx, c.Rx, d.Rx, t ) ),
					MathF.Max( 0.002f, CatmullRom( a.Rz, b.Rz, c.Rz, d.Rz, t ) ), CatmullRom( a.Z, b.Z, c.Z, d.Z, t )) );
			}
		}
		o.Add( p[^1] );
		return o;
	}

	/// <summary>
	/// A smooth tube through rings (height, half width, half depth, forward offset), closed at both ends; UV.x round it,
	/// UV.y from v0 at the first ring to v1 at the last.
	/// </summary>
	public static ArrayMesh Loft( IReadOnlyList<(float Y, float Rx, float Rz, float Z)> rings, float v0 = 0f, float v1 = 1f, int sides = 28 )
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
		var down = rings[^1].Y < rings[0].Y;
		for ( var r = 0; r < rings.Count - 1; r++ )
			for ( var k = 0; k < sides; k++ )
			{
				// Rings go downward or upward: wind so the faces point out either way.
				var q = down
					? new[] { (r, k), (r + 1, k + 1), (r + 1, k), (r, k), (r, k + 1), (r + 1, k + 1) }
					: new[] { (r, k), (r + 1, k), (r + 1, k + 1), (r, k), (r + 1, k + 1), (r, k + 1) };
				foreach ( var (rr, kk) in q ) { st.SetUV( Uv( rr, kk ) ); st.AddVertex( At( rr, kk ) ); }
			}
		foreach ( var (r, outward) in new[] { (0, -1f), (rings.Count - 1, 1f) } )
		{
			var centre = new Vector3( 0f, rings[r].Y, rings[r].Z );
			for ( var k = 0; k < sides; k++ )
			{
				var flip = (outward > 0f) == down;
				foreach ( var v in flip ? new[] { centre, At( r, k ), At( r, k + 1 ) } : new[] { centre, At( r, k + 1 ), At( r, k ) } )
				{ st.SetUV( Uv( r, 0 ) ); st.AddVertex( v ); }
			}
		}
		st.GenerateNormals();
		return st.Commit();
	}

	/// <summary> A tube along +Y from 0 to `length` with rounded ends, its radius going from r0 to r1. </summary>
	public static ArrayMesh TubeMesh( float length, float r0, float r1 )
	{
		var rings = new List<(float, float, float, float)>();
		for ( var j = 0; j <= 5; j++ )
		{
			var th = -MathF.PI / 2f + j / 5f * MathF.PI / 2f;
			var r = r0 * MathF.Cos( th ) + 0.0005f;
			rings.Add( (r0 * MathF.Sin( th ) * 0.8f, r, r, 0f) );
		}
		for ( var j = 1; j <= 5; j++ )
		{
			var th = j / 5f * MathF.PI / 2f;
			var r = r1 * MathF.Cos( th ) + 0.0005f;
			rings.Add( (length + r1 * MathF.Sin( th ) * 0.8f, r, r, 0f) );
		}
		return Loft( rings, 0f, 1f, 20 );
	}

	/// <summary>
	/// A curved fan from a base at the origin out along -Y: `width` across X, cupped toward -Z by `cup`, ribbed in the
	/// mesh when `ribs` is over 0 (shells). UV.x across, UV.y base to tip, UV2.x the length (for the fin ripple).
	/// </summary>
	public static ArrayMesh FanMesh( float length, float width, float cup, int ribs = 0, float ribDepth = 0f )
	{
		const int across = 22, out_ = 7;
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		Vector3 P( int i, int j )
		{
			var u = i / (float)across;
			var a = u * 2f - 1f;
			var v = j / (float)out_;
			var edge = 1f - 0.3f * a * a;
			var x = a * width * 0.5f * (0.16f + 0.84f * MathF.Sqrt( v ));
			var y = -length * v * edge;
			var z = cup * length * (a * a - 0.35f) * v;
			if ( ribs > 0 ) z -= ribDepth * length * MathF.Sqrt( v ) * MathF.Pow( MathF.Abs( MathF.Cos( ribs * MathF.PI * u ) ), 0.6f );
			return new Vector3( x, y, z );
		}
		void V( int i, int j )
		{
			st.SetUV( new Vector2( i / (float)across, j / (float)out_ ) );
			st.SetUV2( new Vector2( length, 0f ) );
			st.AddVertex( P( i, j ) );
		}
		for ( var i = 0; i < across; i++ )
			for ( var j = 0; j < out_; j++ )
			{
				V( i, j ); V( i + 1, j + 1 ); V( i + 1, j );
				V( i, j ); V( i, j + 1 ); V( i + 1, j + 1 );
			}
		st.GenerateNormals();
		return st.Commit();
	}

	public static MeshInstance3D Put( Node3D parent, Mesh mesh, Material material, string name, Vector3? at = null )
	{
		var m = new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = material };
		parent.AddChild( m );
		if ( at is { } p ) m.Position = p;
		return m;
	}

	/// <summary> A smooth ellipsoid (`size` its full width, height, depth). </summary>
	public static MeshInstance3D Ball( Node3D parent, Vector3 at, Vector3 size, Material material, Basis? turn = null, string name = "Ball" )
	{
		var m = Put( parent, SmoothBall, material, name );
		m.Transform = new Transform3D( (turn ?? Basis.Identity) * Basis.FromScale( size ), at );
		return m;
	}

	/// <summary> A smooth tube with rounded ends from one point to another. </summary>
	public static MeshInstance3D Tube( Node3D parent, Vector3 from, Vector3 to, float r0, float r1, Material material, string name = "Tube" )
	{
		var axis = to - from;
		var length = axis.Length();
		var mesh = Cached( $"tube {length:F3} {r0:F3} {r1:F3}", () => TubeMesh( length, r0, r1 ) );
		var m = Put( parent, mesh, material, name );
		m.Transform = new Transform3D( length > 1e-5f ? new Basis( new Quaternion( Vector3.Up, axis / length ) ) : Basis.Identity, from );
		return m;
	}

	public static MeshInstance3D Fin( Node3D parent, Vector3 baseAt, Basis turn, float length, float width, Material material, float cup = 0.12f )
	{
		var m = Put( parent, Cached( $"fan {length:F3} {width:F3} {cup:F3}", () => FanMesh( length, width, cup ) ), material, "Fin" );
		m.Transform = new Transform3D( turn, baseAt );
		m.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		return m;
	}

	public static MeshInstance3D Scallop( Node3D parent, Vector3 hinge, Basis turn, float length, float width, Material material )
	{
		var m = Put( parent, Cached( $"shell {length:F3} {width:F3}", () => FanMesh( length, width, 0.22f, 6, 0.06f ) ), material, "Shell" );
		m.Transform = new Transform3D( turn, hinge );
		return m;
	}

	/// <summary> A turn that points a fan's length (-Y) along `along`, its width along `side`. </summary>
	public static Basis Aim( Vector3 along, Vector3 side )
	{
		var y = -along.Normalized();
		var z = side.Cross( y ).Normalized();
		var x = y.Cross( z ).Normalized();
		return new Basis( x, y, z );
	}

	/// <summary>
	/// A smooth skinned tube down a chain of joints (each the last's child, `step` below it, the first under `parent`):
	/// a bone per joint, every ring weighted between the two bones it lies between, so it bends without seams.
	/// `radius(t)` gives the half width and half depth from the top (0) to the end (1). Shared by key.
	/// </summary>
	public static Skeleton3D SkinChain( Node3D parent, List<Node3D> joints, float step, float above, float below, Func<float, (float Rx, float Rz)> radius, Material paint, string key, int rings, int sides )
	{
		var bones = new Skeleton3D { Name = $"{key} Bones" };
		parent.AddChild( bones );
		for ( var i = 0; i < joints.Count; i++ )
		{
			bones.AddBone( $"{key} {i}" );
			if ( i > 0 ) bones.SetBoneParent( i, i - 1 );
			bones.SetBoneRest( i, joints[i].Transform );
			bones.SetBonePosePosition( i, joints[i].Position );
		}
		var root = joints[0].Position;
		var n = joints.Count;
		var mesh = Cached( $"chain {key} {n} {step:F3} {root}", () =>
		{
			var top = root.Y + above;
			var bottom = root.Y - step * (n - 1) - below;
			var st = new SurfaceTool();
			st.Begin( Mesh.PrimitiveType.Triangles );
			void Vertex( float y, int k )
			{
				var t = (top - y) / (top - bottom);
				var (rx, rz) = radius( t );
				var a = k / (float)sides * MathF.Tau;
				var f = MathX.Clamp( (root.Y - y) / step, 0f, n - 1f );
				var b0 = Math.Min( (int)f, n - 2 );
				var w = MathX.Clamp( f - b0, 0f, 1f );
				st.SetBones( new[] { b0, b0 + 1, 0, 0 } );
				st.SetWeights( new[] { 1f - w, w, 0f, 0f } );
				st.SetUV( new Vector2( k / (float)sides, t ) );
				st.AddVertex( new Vector3( root.X + MathF.Sin( a ) * rx, y, root.Z - MathF.Cos( a ) * rz ) );
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
			return st.Commit();
		} );
		var mi = new MeshInstance3D { Name = key, Mesh = mesh, MaterialOverride = paint };
		bones.AddChild( mi );
		mi.Skeleton = new NodePath( ".." );
		return bones;
	}

	/// <summary> A chain's bones follow its joints (call after the joints are animated). </summary>
	public static void PoseChain( Skeleton3D bones, List<Node3D> joints )
	{
		if ( !GodotObject.IsInstanceValid( bones ) ) return;
		for ( var i = 0; i < joints.Count; i++ )
		{
			if ( !GodotObject.IsInstanceValid( joints[i] ) || joints[i].GetParent() is null ) break;
			bones.SetBonePosePosition( i, joints[i].Position );
			bones.SetBonePoseRotation( i, joints[i].Quaternion );
		}
	}

	public static ShaderMaterial FinMaterial( Color colour, Color ray, float alpha, float ripple )
	{
		var m = new ShaderMaterial { Shader = FinShade };
		m.SetShaderParameter( "colour", colour );
		m.SetShaderParameter( "ray", ray );
		m.SetShaderParameter( "alpha", alpha );
		m.SetShaderParameter( "ripple", ripple );
		return m;
	}

	/// <summary> A damped spring toward `target` (`freq` in Hz; `damping` 1 settles with no overshoot). </summary>
	public static void Spring( ref float x, ref float v, float target, float freq, float damping, float dt )
	{
		var w = MathF.Tau * freq;
		dt = MathF.Min( dt, 0.1f );
		while ( dt > 0f )
		{
			var h = MathF.Min( dt, 1f / 120f );
			v += (w * w * (target - x) - 2f * damping * w * v) * h;
			x += v * h;
			dt -= h;
		}
	}

	/// <summary> Smoothstep of x clamped to 0..1. </summary>
	public static float Ease( float x ) { x = MathX.Clamp( x, 0f, 1f ); return x * x * (3f - 2f * x); }
}
