using Godot;

/// <summary>
/// Small helpers for creatures built in code from simple shapes (the seahorse, Poseidon): blobs, limbs between two
/// points, cones and flat fins, all in metres in the parent's space, with flat cel-style materials.
/// </summary>
public static class Sculpt
{
	static readonly SphereMesh Ball = new() { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 8 };
	static readonly BoxMesh Cube = new() { Size = Vector3.One };

	/// <summary> A flat colour with a soft rim, the game's creature look. </summary>
	public static StandardMaterial3D Paint( Color c, float roughness = 0.5f, float metallic = 0f ) =>
		new() { AlbedoColor = c, Roughness = roughness, Metallic = metallic, RimEnabled = true, Rim = 0.3f, RimTint = 0.5f };

	/// <summary> A glowing colour (jewels, eyes). </summary>
	public static StandardMaterial3D Glow( Color c, float energy = 2.5f ) =>
		new() { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy, Roughness = 0.25f };

	/// <summary> See-through, for fins. </summary>
	public static StandardMaterial3D Membrane( Color c, float alpha = 0.6f ) => new()
	{
		AlbedoColor = c with { A = alpha }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.4f,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled, RimEnabled = true, Rim = 0.5f,
	};

	static MeshInstance3D Put( Node3D parent, Mesh mesh, Material material, Transform3D at, string name )
	{
		var mi = new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = material };
		parent.AddChild( mi );
		mi.Transform = at;
		return mi;
	}

	/// <summary> An ellipsoid at a point: `size` is its full width, height and depth. </summary>
	public static MeshInstance3D Blob( Node3D parent, Vector3 at, Vector3 size, Material material, Basis? turn = null, string name = "Blob" ) =>
		Put( parent, Ball, material, new Transform3D( (turn ?? Basis.Identity) * Basis.FromScale( size ), at ), name );

	public static MeshInstance3D Block( Node3D parent, Vector3 at, Vector3 size, Material material, Basis? turn = null, string name = "Block" ) =>
		Put( parent, Cube, material, new Transform3D( (turn ?? Basis.Identity) * Basis.FromScale( size ), at ), name );

	/// <summary> A rounded limb from one point to another, its radius going from r0 to r1. </summary>
	public static MeshInstance3D Limb( Node3D parent, Vector3 from, Vector3 to, float r0, float r1, Material material, string name = "Limb" )
	{
		var axis = to - from;
		var length = axis.Length();
		var turn = length > 1e-5f ? new Basis( new Quaternion( Vector3.Up, axis / length ) ) : Basis.Identity;
		// CylinderMesh: TopRadius is at +Y (the `to` end).
		return Put( parent, new CylinderMesh { BottomRadius = r0, TopRadius = r1, Height = length, RadialSegments = 12, Rings = 1 }, material, new Transform3D( turn, (from + to) * 0.5f ), name );
	}

	/// <summary> A cone from its base to its tip. </summary>
	public static MeshInstance3D Cone( Node3D parent, Vector3 baseAt, Vector3 tip, float radius, Material material, int sides = 8, string name = "Cone" )
	{
		var axis = tip - baseAt;
		var length = axis.Length();
		var turn = length > 1e-5f ? new Basis( new Quaternion( Vector3.Up, axis / length ) ) : Basis.Identity;
		return Put( parent, new CylinderMesh { BottomRadius = radius, TopRadius = 0f, Height = length, RadialSegments = sides, Rings = 1 }, material, new Transform3D( turn, (baseAt + tip) * 0.5f ), name );
	}

	/// <summary> A pivot node to animate a group of parts round (a joint). </summary>
	public static Node3D Joint( Node3D parent, Vector3 at, string name )
	{
		var j = new Node3D { Name = name };
		parent.AddChild( j );
		j.Position = at;
		return j;
	}

	/// <summary> Height of the walkable floor under a point (the first solid thing below it), or the point's own height minus a lot. </summary>
	public static float FloorBelow( Node3D context, Vector3 at, float reach = 40f )
	{
		var tr = Trace.Ray( context, at + Vector3.Up * 0.5f, at + Vector3.Down * reach, Layers.World );
		return tr.Hit ? tr.HitPosition.Y : at.Y - reach;
	}

	/// <summary>
	/// Keeps a point where enemies may go: inside Atlantis's globe in area 3, inside the map's rim elsewhere.
	/// </summary>
	public static Vector3 InBounds( Vector3 p )
	{
		if ( SeaTerrain.Area == 3 )
		{
			var flat = p.Flat();
			var limit = Atlantis.BaseInner - 8f;
			if ( flat.Length() > limit ) flat = flat.Normalized() * limit;
			return new Vector3( flat.X, p.Y, flat.Z );
		}
		if ( SeaTerrain.Active )
		{
			var limit = SeaTerrain.HalfSize - 16f;
			return new Vector3( Math.Clamp( p.X, -limit, limit ), p.Y, Math.Clamp( p.Z, -limit, limit ) );
		}
		return p;
	}
}
