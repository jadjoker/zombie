using Godot;

/// <summary> What a piece is made of: how it is shaped, how it moves in the water and what it sounds like landing. </summary>
public enum GibKind { Flesh, Shell, Rock, Jelly, Glass, Armour }

/// <summary>
/// How one creature comes apart (the chunking pass): the stuff of its outside (its own skin material and colour), its
/// inside (meat, fat, bone or cartilage, goo), what its chips and burst pieces are made of, and the few pieces only it
/// has (a crab's claw tip, a shark's tooth, a mermaid's scale patch). Built once per creature kind and shared.
/// </summary>
public sealed class GibRecipe
{
	public GibKind Kind = GibKind.Flesh;
	/// <summary> The outside: the creature's own skin (textured where it has one). </summary>
	public Material Outer;
	/// <summary> The inside: what shows where it is torn or broken. </summary>
	public Material Inner;
	/// <summary> A layer between the two (fat, the shell's pale underside). Null: none. </summary>
	public Material Layer;
	/// <summary> Bone, cartilage or a shell's spine; null for boneless creatures (jelly). </summary>
	public Material Bone;
	public Material Sinew;
	public Color Goo = new( 0.35f, 1f, 0.28f );
	/// <summary> Pieces only this creature has: given a random, a parent and a size (metres), they build themselves. </summary>
	public readonly List<Action<Random, Node3D, float>> Specials = new();
	/// <summary> How many of a burst's pieces are its specials (0 to 1). </summary>
	public float SpecialShare = 0.3f;
}

/// <summary>
/// Builds the detailed pieces creatures come apart into (the chunking pass), in code: curved shell shards with a pale
/// underside, faceted rock chunks with fresh broken faces, flesh chunks with skin on one side and meat on the torn side,
/// wobbling jelly lumps, torn ends (a ragged rim of skin round meat, a bone stub, dangling sinew), and throws them as
/// loose physics pieces that sink or float by what they are made of. Metres throughout.
/// </summary>
public static class GibKit
{
	/// <summary> Tests: pieces thrown, torn ends made. </summary>
	public static int Thrown, Caps;

	// ---- Materials ----

	/// <summary> A plain material in the game's look: flat colour, a soft rim; wet ones get a glossy coat. </summary>
	public static StandardMaterial3D Paint( Color c, float roughness = 0.55f, bool wet = false, float metallic = 0f, float glow = 0f )
	{
		var m = new StandardMaterial3D { AlbedoColor = c, Roughness = roughness, Metallic = metallic, RimEnabled = true, Rim = 0.3f, RimTint = 0.5f };
		if ( wet ) { m.ClearcoatEnabled = true; m.Clearcoat = 0.6f; m.ClearcoatRoughness = 0.12f; }
		if ( glow > 0f ) { m.EmissionEnabled = true; m.Emission = c; m.EmissionEnergyMultiplier = glow; }
		return m;
	}

	/// <summary> See-through jelly. </summary>
	public static StandardMaterial3D JellyPaint( Color c, float alpha = 0.7f ) => new()
	{
		AlbedoColor = c with { A = alpha }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.15f,
		RimEnabled = true, Rim = 0.6f, RimTint = 0.4f, ClearcoatEnabled = true, Clearcoat = 0.8f, ClearcoatRoughness = 0.05f,
		EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 0.25f, CullMode = BaseMaterial3D.CullModeEnum.Back,
	};

	// ---- Meshes ----

	static (List<Vector3> Verts, List<int[]> Tris) Icosphere( int levels = 1 )
	{
		var t = (1f + MathF.Sqrt( 5f )) / 2f;
		var verts = new List<Vector3>
		{
			new( -1, t, 0 ), new( 1, t, 0 ), new( -1, -t, 0 ), new( 1, -t, 0 ), new( 0, -1, t ), new( 0, 1, t ),
			new( 0, -1, -t ), new( 0, 1, -t ), new( t, 0, -1 ), new( t, 0, 1 ), new( -t, 0, -1 ), new( -t, 0, 1 ),
		};
		for ( var i = 0; i < verts.Count; i++ ) verts[i] = verts[i].Normalized();
		var tris = new List<int[]>
		{
			new[] { 0, 11, 5 }, new[] { 0, 5, 1 }, new[] { 0, 1, 7 }, new[] { 0, 7, 10 }, new[] { 0, 10, 11 }, new[] { 1, 5, 9 }, new[] { 5, 11, 4 },
			new[] { 11, 10, 2 }, new[] { 10, 7, 6 }, new[] { 7, 1, 8 }, new[] { 3, 9, 4 }, new[] { 3, 4, 2 }, new[] { 3, 2, 6 }, new[] { 3, 6, 8 },
			new[] { 3, 8, 9 }, new[] { 4, 9, 5 }, new[] { 2, 4, 11 }, new[] { 6, 2, 10 }, new[] { 8, 6, 7 }, new[] { 9, 8, 1 },
		};
		var cache = new Dictionary<long, int>();
		int Mid( int a, int b )
		{
			var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
			if ( cache.TryGetValue( key, out var i ) ) return i;
			verts.Add( ((verts[a] + verts[b]) * 0.5f).Normalized() );
			return cache[key] = verts.Count - 1;
		}
		for ( var level = 0; level < levels; level++ )
		{
			var finer = new List<int[]>();
			foreach ( var f in tris )
			{
				int a = Mid( f[0], f[1] ), b = Mid( f[1], f[2] ), c = Mid( f[2], f[0] );
				finer.Add( new[] { f[0], a, c } ); finer.Add( new[] { f[1], b, a } ); finer.Add( new[] { f[2], c, b } ); finer.Add( new[] { a, b, c } );
			}
			tris = finer;
		}
		return (verts, tris);
	}

	/// <summary>
	/// A lump (rock, flesh or jelly): a jittered ball, sliced flat by `cuts` planes. Faces on a slice are the fresh
	/// inside (surface 1), the rest the outside (surface 0). Faceted (flat shaded) or smooth.
	/// </summary>
	public static ArrayMesh Lump( Random random, Vector3 size, float jitter, int cuts, bool faceted, float cutDepth = 0.45f )
	{
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var (verts, tris) = Icosphere( faceted ? 1 : 2 );
		// Smooth lumps get a lower, rounder wobble (a noise of a few bumps) instead of per-point jitter.
		var bumps = new List<(Vector3 Dir, float Amount)>();
		for ( var b = 0; b < 5; b++ ) bumps.Add( (new Vector3( R( -1f, 1f ), R( -1f, 1f ), R( -1f, 1f ) ).Normalized(), R( -jitter, jitter )) );
		for ( var i = 0; i < verts.Count; i++ )
			verts[i] *= faceted ? 1f + R( -jitter, jitter ) : 1f + bumps.Sum( b => b.Amount * MathF.Max( 0f, verts[i].Dot( b.Dir ) ) ) + R( -0.03f, 0.03f );
		var onCut = new bool[verts.Count];
		for ( var c = 0; c < cuts; c++ )
		{
			var n = new Vector3( R( -1f, 1f ), R( -1f, 1f ), R( -1f, 1f ) ).Normalized();
			var d = R( cutDepth * 0.6f, cutDepth * 1.2f );
			for ( var i = 0; i < verts.Count; i++ )
			{
				var over = verts[i].Dot( n ) - d;
				if ( over <= 0f ) continue;
				verts[i] -= n * over;
				onCut[i] = true;
			}
		}
		var outside = new SurfaceTool();
		var inside = new SurfaceTool();
		outside.Begin( Mesh.PrimitiveType.Triangles );
		inside.Begin( Mesh.PrimitiveType.Triangles );
		if ( faceted ) { outside.SetSmoothGroup( uint.MaxValue ); inside.SetSmoothGroup( uint.MaxValue ); }
		else inside.SetSmoothGroup( uint.MaxValue );   // a cut is flat either way
		var any = false;
		foreach ( var f in tris )
		{
			var cut = onCut[f[0]] && onCut[f[1]] && onCut[f[2]];
			var st = cut ? inside : outside;
			any |= cut;
			// Godot fronts are clockwise seen from outside: the icosphere winds the other way.
			foreach ( var i in new[] { f[0], f[2], f[1] } )
			{
				var v = verts[i] * size * 0.5f;
				st.SetUV( new Vector2( v.X + v.Z, v.Y ) * 2f );
				st.AddVertex( v );
			}
		}
		outside.GenerateNormals();
		var mesh = outside.Commit();
		if ( any ) { inside.GenerateNormals(); inside.Commit( mesh ); }
		return mesh;
	}

	/// <summary>
	/// A shard of shell, plate or glass: a jagged outline curved over like a piece of a dome, with a thickness. The top
	/// (surface 0) is the outside, the underside and broken edges (surface 1) the inside.
	/// </summary>
	public static ArrayMesh Shard( Random random, float width, float length, float curve, float thickness )
	{
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var n = random.Next( 6, 10 );
		var outline = new List<Vector2>();
		for ( var i = 0; i < n; i++ )
		{
			var a = i / (float)n * MathF.Tau + R( -0.2f, 0.2f );
			var r = R( 0.55f, 1f ) * (i % 2 == 0 ? 1f : R( 0.7f, 1f ));
			outline.Add( new Vector2( MathF.Cos( a ) * width * 0.5f * r, MathF.Sin( a ) * length * 0.5f * r ) );
		}
		var span = MathF.Max( width, length ) * 0.5f;
		Vector3 Top( Vector2 p ) => new( p.X, curve * (1f - p.LengthSquared() / (span * span)), p.Y );
		var top = new SurfaceTool();
		var rest = new SurfaceTool();
		top.Begin( Mesh.PrimitiveType.Triangles );
		rest.Begin( Mesh.PrimitiveType.Triangles );
		rest.SetSmoothGroup( uint.MaxValue );
		var centre = Top( Vector2.Zero );
		var under = Vector3.Down * thickness;
		for ( var i = 0; i < n; i++ )
		{
			var p0 = outline[i];
			var p1 = outline[(i + 1) % n];
			var mid0 = Top( p0 * 0.5f );
			var mid1 = Top( p1 * 0.5f );
			var o0 = Top( p0 );
			var o1 = Top( p1 );
			// Top: two rings (so the curve shows), wound clockwise from above.
			foreach ( var v in new[] { centre, mid0, mid1, mid0, o1, mid1, mid0, o0, o1 } ) { top.SetUV( new Vector2( v.X, v.Z ) * 3f ); top.AddVertex( v ); }
			// Underside (a little smaller, so the edge reads as broken), and the edge.
			var u0 = Top( p0 * 0.92f ) + under;
			var u1 = Top( p1 * 0.92f ) + under;
			var uc = centre + under;
			foreach ( var v in new[] { uc, u1, u0 } ) { rest.SetUV( new Vector2( v.X, v.Z ) * 3f ); rest.AddVertex( v ); }
			foreach ( var v in new[] { o0, u1, o1, o0, u0, u1 } ) { rest.SetUV( new Vector2( v.X + v.Z, v.Y ) * 3f ); rest.AddVertex( v ); }
		}
		top.GenerateNormals();
		var mesh = top.Commit();
		rest.GenerateNormals();
		rest.Commit( mesh );
		return mesh;
	}

	/// <summary> A mesh instance of a two-surface mesh, outside and inside materials set. </summary>
	public static MeshInstance3D Instance( Node3D parent, Mesh mesh, Material outer, Material inner, string name = "Piece" )
	{
		var m = new MeshInstance3D { Name = name, Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		parent.AddChild( m );
		if ( mesh.GetSurfaceCount() > 0 ) m.SetSurfaceOverrideMaterial( 0, outer );
		if ( mesh.GetSurfaceCount() > 1 ) m.SetSurfaceOverrideMaterial( 1, inner ?? outer );
		return m;
	}

	/// <summary> A tapered strand (sinew, a tentacle tip, a hair), sagging in the middle. </summary>
	public static void Strand( Node3D parent, Vector3 from, Vector3 to, float r0, float r1, Material material, float sag = 0.25f, int bends = 3 )
	{
		var last = from;
		for ( var i = 1; i <= bends; i++ )
		{
			var t = i / (float)bends;
			var next = from.Lerp( to, t ) + Vector3.Down * MathF.Sin( t * MathF.PI ) * sag * from.DistanceTo( to );
			Sculpt.Limb( parent, last, next, MathX.Lerp( r0, r1, (i - 1f) / bends ), MathX.Lerp( r0, r1, t ), material, "Strand" );
			last = next;
		}
	}

	// ---- The pieces of a recipe ----

	/// <summary> One piece of a creature's outside (a shell shard, a rock plate, a skinned flesh chunk, a jelly lump). </summary>
	public static Node3D OuterPiece( GibRecipe r, Random random, float size )
	{
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var node = new Node3D { Name = "Gib Piece" };
		switch ( r.Kind )
		{
			case GibKind.Shell:
			case GibKind.Glass:
			case GibKind.Armour:
			{
				var w = size * R( 0.8f, 1.3f );
				Instance( node, Shard( random, w, w * R( 0.6f, 1.1f ), w * R( 0.12f, 0.3f ), w * (r.Kind == GibKind.Armour ? 0.07f : 0.1f) ), r.Outer, r.Layer ?? r.Inner );
				// Shell: a little meat still stuck under some of them.
				if ( r.Kind == GibKind.Shell && random.NextDouble() < 0.5 )
					Instance( node, Lump( random, new Vector3( w * 0.45f, w * 0.18f, w * 0.35f ), 0.25f, 0, false ), r.Inner, r.Inner ).Position = Vector3.Down * w * 0.12f;
				break;
			}
			case GibKind.Rock:
				Instance( node, Lump( random, Vector3.One * size * R( 0.8f, 1.2f ) * new Vector3( 1f, R( 0.55f, 0.8f ), R( 0.8f, 1.1f ) ), 0.22f, random.Next( 1, 3 ), true ), r.Outer, r.Inner );
				break;
			case GibKind.Jelly:
			{
				var lump = Instance( node, Lump( random, Vector3.One * size * new Vector3( R( 0.8f, 1.2f ), R( 0.6f, 0.9f ), R( 0.8f, 1.1f ) ), 0.15f, 0, false ), r.Outer, r.Outer );
				// Spots on the skin.
				for ( var i = random.Next( 1, 4 ); i > 0; i-- )
				{
					var dir = new Vector3( R( -1f, 1f ), R( 0f, 1f ), R( -1f, 1f ) ).Normalized();
					Sculpt.Blob( node, dir * size * 0.42f, new Vector3( size * 0.22f, size * 0.06f, size * 0.22f ), r.Layer ?? r.Inner, Shapes.LookAt( dir ) * new Basis( Vector3.Right, MathF.PI * 0.5f ), "Spot" );
				}
				break;
			}
			default:
			{
				// Flesh: skin on the outside, meat on the torn side, a band of fat between, and a strand of sinew.
				Instance( node, Lump( random, Vector3.One * size * new Vector3( R( 0.8f, 1.2f ), R( 0.6f, 0.9f ), R( 0.8f, 1.1f ) ), 0.18f, 1, false, 0.25f ), r.Outer, r.Inner );
				if ( r.Layer is not null ) Instance( node, Lump( random, Vector3.One * size * 0.75f, 0.12f, 1, false, 0.15f ), r.Layer, r.Inner ).Position = Vector3.Down * size * 0.08f;
				if ( r.Sinew is not null && random.NextDouble() < 0.6 )
				{
					var dir = new Vector3( R( -1f, 1f ), R( -0.5f, 0.5f ), R( -1f, 1f ) ).Normalized();
					Strand( node, dir * size * 0.3f, dir * size * R( 0.8f, 1.4f ), size * 0.06f, size * 0.015f, r.Sinew );
				}
				break;
			}
		}
		return node;
	}

	/// <summary> A piece of a creature's inside: meat (with fat), cartilage or a bit of bone; a lump of jelly core. </summary>
	public static Node3D InnerPiece( GibRecipe r, Random random, float size )
	{
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var node = new Node3D { Name = "Gib Inside" };
		Instance( node, Lump( random, Vector3.One * size * new Vector3( R( 0.7f, 1.1f ), R( 0.5f, 0.8f ), R( 0.7f, 1f ) ), 0.2f, random.Next( 0, 2 ), false ), r.Inner, r.Layer ?? r.Inner );
		if ( r.Bone is not null && random.NextDouble() < 0.4 )
		{
			// A splinter of bone (or cartilage) through it.
			var dir = new Vector3( R( -1f, 1f ), R( -0.4f, 0.4f ), R( -1f, 1f ) ).Normalized();
			Sculpt.Limb( node, -dir * size * 0.5f, dir * size * R( 0.6f, 0.9f ), size * 0.09f, size * 0.03f, r.Bone, "Bone" );
		}
		if ( r.Sinew is not null && random.NextDouble() < 0.5 )
		{
			var dir = new Vector3( R( -1f, 1f ), R( -0.5f, 0.2f ), R( -1f, 1f ) ).Normalized();
			Strand( node, Vector3.Zero, dir * size * R( 0.8f, 1.5f ), size * 0.05f, size * 0.012f, r.Sinew );
		}
		return node;
	}

	/// <summary>
	/// A torn end: a ragged rim of skin round a face of meat (with a ring of fat), a stub of bone in the middle and a few
	/// strands of sinew hanging out. Its local -Z faces out of the wound. Radius in metres.
	/// </summary>
	public static Node3D TornEnd( GibRecipe r, Random random, float radius )
	{
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		Caps++;
		var cap = new Node3D { Name = "Torn End" };
		var flat = new Basis( Vector3.Right, MathF.PI * 0.5f );   // a ring lying across Z
		// The ring of skin (or shell, or stone) round the wound.
		var ring = new MeshInstance3D { Name = "Rim", Mesh = new TorusMesh { InnerRadius = radius * 0.82f, OuterRadius = radius * 1.05f, Rings = 20, RingSegments = 6 }, MaterialOverride = r.Outer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		cap.AddChild( ring );
		ring.Basis = flat;
		// A layer under it (fat, the shell's pale underside).
		if ( r.Layer is not null )
		{
			var layer = new MeshInstance3D { Name = "Layer", Mesh = new TorusMesh { InnerRadius = radius * 0.66f, OuterRadius = radius * 0.86f, Rings = 18, RingSegments = 5 }, MaterialOverride = r.Layer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			cap.AddChild( layer );
			layer.Basis = flat;
			layer.Position = Vector3.Forward * radius * 0.06f;
		}
		// The face of the wound: a wet bulge of meat, lumpy, with a dark hollow and a few torn lumps on it.
		var meat = Instance( cap, Lump( random, new Vector3( radius * 1.6f, radius * 1.6f, radius * 0.7f ), 0.25f, 0, false ), r.Inner, r.Inner, "Meat" );
		meat.Position = Vector3.Forward * radius * 0.08f;
		for ( var i = random.Next( 3, 6 ); i > 0; i-- )
		{
			var a = R( 0f, MathF.Tau );
			var at = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f ) * radius * R( 0.2f, 0.6f ) + Vector3.Forward * radius * 0.3f;
			Instance( cap, Lump( random, Vector3.One * radius * R( 0.25f, 0.45f ), 0.3f, 0, false ), r.Inner, r.Inner, "Lump" ).Position = at;
		}
		Sculpt.Blob( cap, Vector3.Forward * radius * 0.36f + new Vector3( R( -0.2f, 0.2f ), R( -0.2f, 0.2f ), 0f ) * radius, new Vector3( radius * 0.45f, radius * 0.45f, radius * 0.12f ), Paint( r.Goo.Lerp( Colors.Black, 0.55f ), 0.2f, wet: true ), null, "Hollow" );
		// Ragged flaps of skin round the edge, bent out of the wound.
		var teeth = random.Next( 7, 11 );
		for ( var i = 0; i < teeth; i++ )
		{
			var a = i / (float)teeth * MathF.Tau + R( -0.2f, 0.2f );
			var at = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f ) * radius * 0.95f;
			var tip = at * R( 1.0f, 1.12f ) + Vector3.Forward * radius * R( 0.18f, 0.42f );
			Sculpt.Cone( cap, at, tip, radius * R( 0.12f, 0.2f ), r.Outer, 4, "Flap" );
		}
		// Bone (or cartilage) sticking out, with a jagged tip.
		if ( r.Bone is not null )
		{
			var off = new Vector3( R( -0.2f, 0.2f ), R( -0.2f, 0.2f ), 0f ) * radius;
			var len = radius * R( 0.5f, 0.9f );
			Sculpt.Limb( cap, off, off + Vector3.Forward * len, radius * 0.22f, radius * 0.18f, r.Bone, "Bone" );
			Sculpt.Cone( cap, off + Vector3.Forward * len, off + Vector3.Forward * (len + radius * 0.25f) + new Vector3( radius * 0.1f, 0f, 0f ), radius * 0.18f, r.Bone, 5, "Splinter" );
		}
		// Sinew hanging out and down.
		if ( r.Sinew is not null )
			for ( var i = random.Next( 2, 4 ); i > 0; i-- )
			{
				var from = new Vector3( R( -0.5f, 0.5f ), R( -0.5f, 0.5f ), 0f ) * radius + Vector3.Forward * radius * 0.2f;
				Strand( cap, from, from + Vector3.Forward * radius * R( 0.6f, 1.3f ) + Vector3.Down * radius * R( 0.3f, 0.8f ), radius * 0.08f, radius * 0.02f, r.Sinew, 0.3f );
			}
		return cap;
	}

	// ---- Throwing them ----

	/// <summary>
	/// Makes a built piece a loose physics gib at a spot, flying at a velocity (m/s): a collider from its own shape, a
	/// mass and behaviour for what it is made of (stone and shell sink, jelly and flesh float up, and each lands with its
	/// own sound).
	/// </summary>
	public static Gib Throw( Node3D piece, GibRecipe r, Vector3 at, Vector3 velocity, float size )
	{
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( root is null ) return null;
		root.AddChild( piece );
		piece.GlobalPosition = at;
		var scale = piece.Scale;   // a template copy carries its size as a scale
		piece.GlobalBasis = new Basis( new Quaternion( Rng.OnSphere(), Rng.Float( 0f, MathF.Tau ) ).Normalized() ) * Basis.FromScale( scale );
		var mass = r.Kind switch { GibKind.Rock => 4f, GibKind.Armour => 3f, GibKind.Shell or GibKind.Glass => 1.2f, GibKind.Jelly => 0.8f, _ => 1.5f } * MathX.Clamp( size / 0.15f, 0.4f, 4f );
		var gib = Gib.Attach( piece, GoreSystem.Get(), size * 0.45f / U.In, 12f + size * 60f, mass, velocity, r.Goo, addCollider: false );
		// Collides as the shape it is, so a shard lands flat and a rock rolls.
		if ( piece.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>().FirstOrDefault( m => m.Mesh is not null ) is { } shape )
		{
			var cs = new CollisionShape3D { Shape = Hull( shape.Mesh ) };
			gib.AddChild( cs );
			cs.GlobalTransform = shape.GlobalTransform;
		}
		else gib.AddChild( new CollisionShape3D { Shape = new SphereShape3D { Radius = MathF.Max( 0.02f, size * 0.45f ) } } );
		gib.Stuff = r.Kind;
		Thrown++;
		return gib;
	}

	static Random random = new();

	// ---- Templates: each recipe builds a few variants of each piece once; every piece after is a copy ----

	const float TemplateSize = 0.2f;
	const int Variants = 6;
	static readonly Dictionary<(GibRecipe, int), List<Node3D>> templates = new();
	static readonly Dictionary<Mesh, Shape3D> hulls = new();
	/// <summary> Tests: templates built (each built once). </summary>
	public static int TemplatesBuilt;

	/// <summary>
	/// A piece of a recipe at a size: kind 0 is the outside, 1 the inside, 2 a torn end, 3 and up its specials. Built at
	/// a standard size the first few times (kept as templates), copied and scaled after that.
	/// </summary>
	static Node3D holder;

	/// <summary>
	/// Where templates are kept: a hidden node in the scene (so they are freed with it, not leaked). A new scene gets a new
	/// holder, and the old templates are forgotten.
	/// </summary>
	static Node3D Holder()
	{
		if ( holder is not null && GodotObject.IsInstanceValid( holder ) && holder.IsInsideTree() ) return holder;
		templates.Clear();
		var gore = GoreSystem.Get();
		holder = new Node3D { Name = "Gib Templates", Visible = false, ProcessMode = Node.ProcessModeEnum.Disabled };
		gore.AddChild( holder );
		return holder;
	}

	static Node3D Make( GibRecipe r, int kind, float size )
	{
		var keep = Holder();
		if ( !templates.TryGetValue( (r, kind), out var list ) ) templates[(r, kind)] = list = new List<Node3D>();
		Node3D template;
		if ( list.Count < Variants )
		{
			template = kind switch
			{
				0 => OuterPiece( r, random, TemplateSize ),
				1 => InnerPiece( r, random, TemplateSize ),
				2 => TornEnd( r, random, TemplateSize ),
				_ => Special( r, kind - 3 ),
			};
			keep.AddChild( template );
			list.Add( template );
			TemplatesBuilt++;
		}
		else template = list[random.Next( list.Count )];
		var copy = (Node3D)template.Duplicate();
		copy.Scale = Vector3.One * (size / TemplateSize);
		copy.Visible = true;
		return copy;
	}

	static Node3D Special( GibRecipe r, int index )
	{
		var node = new Node3D { Name = "Gib Special" };
		r.Specials[index % r.Specials.Count]( random, node, TemplateSize );
		return node;
	}

	static readonly Queue<(GibRecipe, int)> warming = new();

	/// <summary> Builds a recipe's templates ahead of time, one a frame (so a first kill does not hitch). </summary>
	public static void Prewarm( GibRecipe r )
	{
		if ( r is null ) return;
		for ( var v = 0; v < Variants; v++ )
		{
			warming.Enqueue( (r, 0) );
			warming.Enqueue( (r, 1) );
			warming.Enqueue( (r, 2) );
		}
		for ( var i = 0; i < r.Specials.Count; i++ )
			for ( var v = 0; v < Variants; v++ ) warming.Enqueue( (r, 3 + i) );
	}

	/// <summary> One template from the warming queue (called each frame by the gore system). </summary>
	public static void PrewarmStep()
	{
		if ( warming.Count == 0 ) return;
		var (r, kind) = warming.Dequeue();
		if ( templates.TryGetValue( (r, kind), out var list ) && list.Count >= Variants ) return;
		Make( r, kind, TemplateSize ).Free();
	}

	/// <summary> A collision hull for a mesh, made once per mesh. </summary>
	static Shape3D Hull( Mesh mesh )
	{
		if ( hulls.TryGetValue( mesh, out var hull ) ) return hull;
		return hulls[mesh] = mesh.CreateConvexShape( true, false );
	}

	/// <summary>
	/// Chips knocked off where a shot hits (in place of the old round blobs): bits of the outside flung back off the
	/// surface, and now and then a scrap of the inside. Size in metres.
	/// </summary>
	public static void Chips( GibRecipe r, Vector3 at, Vector3 normal, Vector3 direction, float size, int count = 2, float speed = 1f )
	{
		if ( r is null ) return;
		var reflect = direction - 2f * direction.Dot( normal ) * normal;
		for ( var i = 0; i < count; i++ )
		{
			var dir = (normal + reflect * 0.4f + Rng.InSphere( 1f ) * 0.5f).SafeNormal();
			var s = size * Rng.Float( 0.5f, 0.9f );
			var piece = i == count - 1 && Rng.Float() < 0.35f ? Make( r, 1, s * 0.8f ) : Make( r, 0, s );
			Throw( piece, r, at + normal * s * 0.6f, (dir * Rng.Float( 2.5f, 5f ) + Vector3.Up * Rng.Float( 1f, 2.5f )) * speed, s );
		}
	}

	/// <summary>
	/// A creature bursting (death, or a point-blank kill): a spread of outside pieces, inside pieces and its own special
	/// pieces, flung out round a centre. Size is the biggest piece in metres.
	/// </summary>
	public static void Burst( GibRecipe r, Vector3 centre, Vector3 direction, float size, int count, float speed = 1f )
	{
		if ( r is null ) return;
		direction = direction == Vector3.Zero ? Vector3.Up : direction.Normalized();
		var specials = r.Specials.Count > 0 ? Math.Max( 1, (int)MathF.Round( count * r.SpecialShare ) ) : 0;
		for ( var i = 0; i < count; i++ )
		{
			var s = size * Rng.Float( 0.45f, 1f );
			Node3D piece;
			if ( i < specials ) piece = Make( r, 3 + random.Next( r.Specials.Count ), s );
			else piece = (i % 3 == 2) ? Make( r, 1, s * 0.8f ) : Make( r, 0, s );
			var dir = (direction * 0.6f + Rng.OnSphere() + Vector3.Up * 0.5f).SafeNormal();
			Throw( piece, r, centre + Rng.InSphere( size * 0.6f ), dir * Rng.Float( 2.5f, 6.5f ) * speed, s );
		}
	}

	/// <summary>
	/// A torn end left on what is still standing (the stump), jetting goo and then oozing. Replaces the old round stump.
	/// </summary>
	public static Node3D Stump( GibRecipe r, Node3D attachTo, Vector3 worldPosition, Vector3 outward, float radius, float dripSeconds = 6f )
	{
		if ( r is null || attachTo is null || !GodotObject.IsInstanceValid( attachTo ) ) return null;
		var cap = Make( r, 2, radius );
		cap.Name = "Stump";
		attachTo.AddChild( cap );
		cap.GlobalTransform = new Transform3D( Shapes.LookAt( outward == Vector3.Zero ? Vector3.Up : outward ), worldPosition );
		cap.Scale = Vector3.One * (radius / TemplateSize) / MathF.Max( attachTo.GlobalBasis.Scale.X, 0.001f );
		if ( dripSeconds > 0f )
		{
			cap.AddChild( new GooJet { Color = r.Goo, Lifetime = MathX.Clamp( dripSeconds * 0.45f, 1f, 2.8f ), Strength = MathX.Clamp( radius / 0.13f, 0.5f, 1.6f ) * (dripSeconds < 3f ? 0.55f : 1f) } );
			cap.AddChild( new GooOoze { Color = r.Goo, Lifetime = dripSeconds, Interval = 0.14f } );
		}
		return cap;
	}

	/// <summary> The torn end on a piece that came off (where it was joined on), oozing a little. </summary>
	public static void CapPiece( GibRecipe r, Node3D piece, Vector3 worldPosition, Vector3 outward, float radius )
	{
		if ( r is null || piece is null || !GodotObject.IsInstanceValid( piece ) ) return;
		var cap = Make( r, 2, radius );
		piece.AddChild( cap );
		cap.GlobalTransform = new Transform3D( Shapes.LookAt( outward == Vector3.Zero ? Vector3.Up : outward ), worldPosition );
		cap.Scale = Vector3.One * (radius / TemplateSize) / MathF.Max( piece.GlobalBasis.Scale.X, 0.001f );
		cap.AddChild( new GooOoze { Color = r.Goo, Lifetime = 2.5f, Interval = 0.2f } );
	}

	/// <summary>
	/// The joint end of a piece that came off and which way it faces: the piece's own origin (parts pivot at their joint),
	/// facing away from the middle of what is drawn.
	/// </summary>
	public static (Vector3 At, Vector3 Out, float Radius) JointEnd( Node3D piece )
	{
		var meshes = piece.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>().Where( m => m.Mesh is not null && m.Name.ToString() != "Torn End" ).ToList();
		if ( meshes.Count == 0 ) return (piece.GlobalPosition, Vector3.Up, 0.05f);
		var box = new Aabb();
		var first = true;
		foreach ( var m in meshes )
		{
			var b = m.GlobalTransform * m.GetAabb();
			box = first ? b : box.Merge( b );
			first = false;
		}
		var centre = box.GetCenter();
		var outward = (piece.GlobalPosition - centre).SafeNormal();
		if ( outward == Vector3.Zero ) outward = Vector3.Up;
		var radius = MathX.Clamp( MathF.Min( box.Size.X, MathF.Min( box.Size.Y, box.Size.Z ) ) * 0.45f, 0.02f, 0.6f );
		return (piece.GlobalPosition, outward, radius);
	}
}
