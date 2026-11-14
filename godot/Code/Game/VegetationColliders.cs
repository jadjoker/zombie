using Godot;

/// <summary>
/// Solid sea floor plants and rocks (playtest): the converted level's kelp, coral, boulders, reef rocks and cliffs had
/// no colliders, so the player walked through them. Run once everything is placed (before the navmesh): each prop gets
/// a static body beside it (not under it, so kelp sway and odd scales don't bend the shape) holding a shape taken from
/// its own mesh at its final size:
/// - kelp: an upright cylinder round the stem (the leaves stay soft, so they don't snag you),
/// - branching coral, tube sponges, reef rocks and cliffs: the mesh split into a few convex pieces that follow its
///   shape (worked out once per model; its own triangles if that fails),
/// - brain coral and boulders: a convex hull of the mesh.
/// The bodies are on their own layer (Layers.Plant): the player's body and the navmesh see them, nothing else does.
/// Pebbles stay soft. Props close to an enemy spawn point stay soft too, so nothing spawns stuck inside one.
/// </summary>
public static class VegetationColliders
{
	/// <summary> The stem's radius in metres at scale 1 (the stalk wanders about 0.15 m round its axis). </summary>
	const float KelpRadius = 0.2f;
	const float SpawnClearance = 3f;
	/// <summary> Meta on a prop: its collider's body. </summary>
	public const string SolidMeta = "solid";

	/// <summary> Takes away a prop's collider (the prop was blasted away or hidden). </summary>
	public static void RemoveSolid( Node prop )
	{
		if ( !prop.HasMeta( SolidMeta ) ) return;
		if ( prop.GetMeta( SolidMeta ).AsGodotObject() is Node body && GodotObject.IsInstanceValid( body ) ) body.QueueFree();
		prop.RemoveMeta( SolidMeta );
	}

	static readonly Dictionary<Mesh, Vector3[]> hullPoints = new();
	static readonly Dictionary<Mesh, Vector3[][]> pieces = new();

	enum Kind { None, Stem, Pieces, Hull }

	static Kind KindOf( Node3D prop )
	{
		var kind = Level.Kind( prop );
		var model = prop.HasMeta( "model" ) ? (string)prop.GetMeta( "model" ) : "";
		if ( kind.StartsWith( "Kelp" ) ) return Kind.Stem;
		if ( model.Contains( "coral_branch" ) || model.Contains( "sponge" ) ) return Kind.Pieces;
		// Reef rocks and cliffs are lumpy and overhang: one hull would fill their hollows with an invisible wall.
		if ( kind.StartsWith( "Reef" ) || kind.StartsWith( "Cliff" ) ) return Kind.Pieces;
		if ( kind.StartsWith( "Coral" ) || kind.StartsWith( "Boulder" ) ) return Kind.Hull;
		return Kind.None;
	}

	public static void Add( Node3D level )
	{
		var props = level.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>()
			.Where( n => n.GetParent() is { } parent && Level.Kind( parent ) == "Dressing" && n is not StaticBody3D ).ToList();
		var spawns = level.GetTree().GetNodesInGroup( "spawn_point" ).OfType<Node3D>().Select( s => s.GlobalPosition ).ToList();
		int made = 0, skipped = 0;
		foreach ( var prop in props )
		{
			var kind = KindOf( prop );
			if ( kind == Kind.None ) continue;
			if ( spawns.Any( s => s.Flat().DistanceTo( prop.GlobalPosition.Flat() ) < SpawnClearance ) ) { skipped++; continue; }
			var shapes = kind == Kind.Stem ? Stem( prop ) : FromMesh( prop, kind );
			if ( shapes.Count == 0 ) continue;
			// Beside the prop, at its origin, unrotated: the shape is already in world orientation and size.
			var body = new StaticBody3D { Name = $"{prop.Name} Solid", CollisionLayer = Layers.Plant, CollisionMask = 0 };
			body.SetMeta( "kind", "Solid" );
			prop.GetParent().AddChild( body );
			body.GlobalTransform = new Transform3D( Basis.Identity, prop.GlobalPosition );
			foreach ( var (shape, offset) in shapes ) body.AddChild( new CollisionShape3D { Shape = shape, Position = offset } );
			prop.SetMeta( SolidMeta, body );   // so whatever removes the plant (the dive crater) can take its collider too
			made++;
		}
		GD.Print( $"[vegetation] {made} solid props, {skipped} left soft by spawn points" );
	}

	/// <summary> Every mesh under the prop with its transform relative to the prop's origin (world orientation and scale). </summary>
	static IEnumerable<(Mesh Mesh, Transform3D ToBody)> Meshes( Node3D prop )
	{
		var origin = new Transform3D( Basis.Identity, prop.GlobalPosition ).AffineInverse();
		foreach ( var mi in prop.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
			if ( mi.Mesh is not null ) yield return (mi.Mesh, origin * mi.GlobalTransform);
	}

	/// <summary> An upright cylinder from the kelp's base to its top, round the stem. </summary>
	static List<(Shape3D Shape, Vector3 Offset)> Stem( Node3D prop )
	{
		var low = float.MaxValue;
		var high = float.MinValue;
		foreach ( var (mesh, toBody) in Meshes( prop ) )
		{
			var box = toBody * mesh.GetAabb();
			low = MathF.Min( low, box.Position.Y );
			high = MathF.Max( high, box.End.Y );
		}
		if ( high <= low ) return new();
		var basis = prop.GlobalBasis;
		var spread = MathF.Max( basis.X.Length(), basis.Z.Length() );
		var height = high - low;
		return new() { (new CylinderShape3D { Radius = KelpRadius * spread, Height = height }, new Vector3( 0f, low + height * 0.5f, 0f )) };
	}

	/// <summary> Convex shapes for the prop, in the body's space: one hull, or one per piece of each mesh. </summary>
	static List<(Shape3D Shape, Vector3 Offset)> FromMesh( Node3D prop, Kind kind )
	{
		var shapes = new List<(Shape3D, Vector3)>();
		foreach ( var (mesh, toBody) in Meshes( prop ) )
		{
			var parts = kind == Kind.Pieces ? Pieces( mesh ) : new[] { Hull( mesh ) };
			foreach ( var part in parts )
			{
				if ( part.Length == 0 ) continue;
				var points = new Vector3[part.Length];
				for ( var i = 0; i < part.Length; i++ ) points[i] = toBody * part[i];
				// The fallback (the mesh's own triangles) is a triangle shape; hull points make a hull.
				Shape3D shape = kind == Kind.Pieces && triangleMeshes.Contains( mesh )
					? new ConcavePolygonShape3D { Data = points }
					: new ConvexPolygonShape3D { Points = points };
				shapes.Add( (shape, Vector3.Zero) );
			}
		}
		return shapes;
	}

	/// <summary> Meshes the split gave nothing for: their one piece is their triangles. </summary>
	static readonly HashSet<Mesh> triangleMeshes = new();

	/// <summary>
	/// The mesh split into convex pieces (each a list of hull points, in its own space); worked out once per mesh. If
	/// the split gives nothing, the mesh's own triangles are used as one piece.
	/// </summary>
	static Vector3[][] Pieces( Mesh mesh )
	{
		if ( pieces.TryGetValue( mesh, out var p ) ) return p;
		var settings = new MeshConvexDecompositionSettings { MaxConvexHulls = 10 };
		p = mesh.ConvexDecompose( settings ).OfType<ConvexPolygonShape3D>().Select( hull => hull.Points ).Where( points => points.Length >= 4 ).ToArray();
		if ( p.Length == 0 )
		{
			triangleMeshes.Add( mesh );
			p = new[] { mesh.GetFaces() };
		}
		pieces[mesh] = p;
		return p;
	}

	/// <summary> The points of a simplified convex hull round the mesh, in its own space; worked out once per mesh. </summary>
	static Vector3[] Hull( Mesh mesh )
	{
		if ( hullPoints.TryGetValue( mesh, out var h ) ) return h;
		h = mesh.CreateConvexShape( true, true )?.Points ?? Array.Empty<Vector3>();
		hullPoints[mesh] = h;
		return h;
	}
}
