using Godot;

/// <summary>
/// Builds a big structure (the sunken liner, Atlantis) out of simple shapes cheaply: every shape is appended into
/// one mesh per material, and solid shapes add a box collider to one static body. Positions are in the space of the
/// node it builds under. Call <see cref="Commit"/> once at the end to make the meshes.
/// </summary>
public class MeshKit
{
	readonly Dictionary<string, (SurfaceTool Tool, Material Material)> batches = new();
	readonly IReadOnlyDictionary<string, Material> materials;
	readonly HashSet<string> noShadow;
	public readonly Node3D Node;
	readonly StaticBody3D body;
	public int Pieces, Solids;

	/// <summary> Materials by key; `noShadow` lists keys that cast no shadow (glass, glows, leaves). </summary>
	public MeshKit( Node3D node, IReadOnlyDictionary<string, Material> materials, string bodyName, IEnumerable<string> noShadow = null )
	{
		Node = node;
		this.materials = materials;
		this.noShadow = new HashSet<string>( noShadow ?? Array.Empty<string>() );
		body = new StaticBody3D { Name = bodyName, CollisionLayer = Layers.World, CollisionMask = 0 };
		node.AddChild( body );
	}

	SurfaceTool Batch( string key )
	{
		if ( batches.TryGetValue( key, out var b ) ) return b.Tool;
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		batches[key] = (st, materials[key]);
		return st;
	}

	public void Add( string key, Mesh mesh, Transform3D at )
	{
		Batch( key ).AppendFrom( mesh, 0, at );
		Pieces++;
	}

	public void Box( string key, Vector3 centre, Vector3 size, Basis? turn = null, bool solid = true )
	{
		var at = new Transform3D( turn ?? Basis.Identity, centre );
		Add( key, new BoxMesh { Size = size }, at );
		if ( solid ) Solid( at, size );
	}

	public void Solid( Transform3D at, Vector3 size )
	{
		body.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Transform = at } );
		Solids++;
	}

	/// <summary> A solid of any shape (a cylinder, a hull) at a transform. </summary>
	public void Solid( Shape3D shape, Transform3D at )
	{
		body.AddChild( new CollisionShape3D { Shape = shape, Transform = at } );
		Solids++;
	}

	public void Cylinder( string key, Vector3 centre, float radius, float height, Basis? turn = null, int sides = 14, float topRadius = -1f ) =>
		Add( key, new CylinderMesh { TopRadius = topRadius < 0f ? radius : topRadius, BottomRadius = radius, Height = height, RadialSegments = sides, Rings = 1 }, new Transform3D( turn ?? Basis.Identity, centre ) );

	public void Sphere( string key, Vector3 centre, float radius, int sides = 10 ) =>
		Add( key, new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = sides, Rings = sides / 2 }, new Transform3D( Basis.Identity, centre ) );

	public void Commit()
	{
		foreach ( var (key, (tool, material)) in batches )
		{
			tool.GenerateNormals();
			var mi = new MeshInstance3D { Name = key, Mesh = tool.Commit(), MaterialOverride = material };
			if ( noShadow.Contains( key ) ) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			Node.AddChild( mi );
		}
	}
}
