using Godot;

/// <summary>
/// Shared bits for the guns built in code on the Blaster's rig (the Hook-Bot's and the Coral-Bot's): simple shapes in
/// gun space (inches, +X along the barrel, +Y to the side, +Z up, the grip at the origin), flat paints, and putting a
/// gun on the rig's gun bone with a Muzzle node at its mouth.
/// </summary>
public static class GunKit
{
	/// <summary> Gun space (X forward, Z up) to the gun bone's space (Y forward, Z up). </summary>
	public static readonly Basis GunToBone = new( new Vector3( 0f, 1f, 0f ), new Vector3( -1f, 0f, 0f ), new Vector3( 0f, 0f, 1f ) );

	static readonly Dictionary<(Color, float, float, float), StandardMaterial3D> paints = new();

	public static StandardMaterial3D Paint( Color c, float roughness = 0.45f, float metallic = 0f, float glow = 0f )
	{
		if ( paints.TryGetValue( (c, roughness, metallic, glow), out var m ) ) return m;
		m = new StandardMaterial3D { AlbedoColor = c, Roughness = roughness, Metallic = metallic, RimEnabled = true, Rim = 0.3f, RimTint = 0.6f };
		if ( glow > 0f ) { m.EmissionEnabled = true; m.Emission = c; m.EmissionEnergyMultiplier = glow; }
		paints[(c, roughness, metallic, glow)] = m;
		return m;
	}

	public static MeshInstance3D Part( Node3D parent, Mesh mesh, Material material, Vector3 at, Basis? turn = null, string name = "Part" )
	{
		var mi = new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		parent.AddChild( mi );
		mi.Transform = new Transform3D( turn ?? Basis.Identity, at );
		return mi;
	}

	public static MeshInstance3D Box( Node3D parent, Vector3 centre, Vector3 size, Material material, float tipDegrees = 0f ) =>
		Part( parent, new BoxMesh { Size = size }, material, centre, tipDegrees == 0f ? null : new Basis( new Vector3( 0f, 1f, 0f ), MathX.Rad( tipDegrees ) ) );

	/// <summary> A cylinder along the barrel (+X), radius at the back and (optionally) another at the front. </summary>
	public static MeshInstance3D AlongX( Node3D parent, Vector3 centre, float radius, float length, Material material, float frontRadius = -1f, int sides = 20 ) =>
		Part( parent, new CylinderMesh { BottomRadius = radius, TopRadius = frontRadius < 0f ? radius : frontRadius, Height = length, RadialSegments = sides, Rings = 1 }, material, centre, new Basis( Vector3.Back, -MathF.PI * 0.5f ) );

	public static MeshInstance3D Ball( Node3D parent, Vector3 centre, float radius, Material material ) =>
		Part( parent, new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = 20, Rings = 10 }, material, centre );

	/// <summary> A cone from its base to its tip (any direction). </summary>
	public static MeshInstance3D Spike( Node3D parent, Vector3 baseAt, Vector3 tip, float radius, Material material, int sides = 8 )
	{
		var axis = tip - baseAt;
		var length = axis.Length();
		var turn = length > 1e-5f ? Along( axis / length ) : Basis.Identity;
		return Part( parent, new CylinderMesh { BottomRadius = radius, TopRadius = 0f, Height = length, RadialSegments = sides, Rings = 1 }, material, (baseAt + tip) * 0.5f, turn, "Spike" );
	}

	/// <summary> A rod between two points. </summary>
	public static MeshInstance3D Rod( Node3D parent, Vector3 from, Vector3 to, float radius, Material material, int sides = 10 )
	{
		var axis = to - from;
		var length = axis.Length();
		var turn = length > 1e-5f ? Along( axis / length ) : Basis.Identity;
		return Part( parent, new CylinderMesh { BottomRadius = radius, TopRadius = radius, Height = length, RadialSegments = sides, Rings = 1 }, material, (from + to) * 0.5f, turn, "Rod" );
	}

	/// <summary> Puts a gun built by `build` on the rig's gun bone, with a Muzzle node at `muzzleAt` (gun space). </summary>
	public static void Attach( Skeleton3D skeleton, string name, Action<Node3D> build, Vector3 muzzleAt )
	{
		if ( skeleton.FindBone( "gun" ) < 0 ) return;
		var onGun = new BoneAttachment3D { Name = $"{name} Gun Bone", BoneName = "gun" };
		skeleton.AddChild( onGun );
		var body = new Node3D { Name = name, Transform = new Transform3D( GunToBone.Scaled( Vector3.One * 0.01f ), Vector3.Zero ) };
		onGun.AddChild( body );
		build( body );
		onGun.AddChild( new Node3D { Name = "Muzzle", Position = GunToBone * muzzleAt * 0.01f } );
	}

	/// <summary> A turn taking +Y onto a direction (safe for straight down too). </summary>
	public static Basis Along( Vector3 dir ) => dir.Dot( Vector3.Up ) < -0.999f ? new Basis( Vector3.Right, MathF.PI ) : new Basis( new Quaternion( Vector3.Up, dir ) );

	/// <summary> A rope (or vein, or line) drawn as a thin cylinder, laid between two points each frame. </summary>
	public static MeshInstance3D Line( Node parent, Material material, float radius )
	{
		var mi = new MeshInstance3D
		{
			Name = "Line", Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 1f, RadialSegments = 6, Rings = 1 },
			MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true,
		};
		parent.AddChild( mi );
		return mi;
	}

	public static void Lay( MeshInstance3D line, Vector3 from, Vector3 to )
	{
		var axis = to - from;
		var length = axis.Length();
		if ( length < 1e-4f ) { line.Visible = false; return; }
		line.Visible = true;
		var turn = Along( axis / length ) * Basis.FromScale( new Vector3( 1f, length, 1f ) );
		line.GlobalTransform = new Transform3D( turn, (from + to) * 0.5f );
	}
}
