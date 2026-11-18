using Godot;

/// <summary>
/// The Gore Vac (slot 4): an industrial wet-vac for the robot, hazard orange and black with a glass tank on top that
/// fills with whatever it has sucked up (in that goo's colour) and a wide flared nozzle with a fan spinning in it.
/// Like the trident shooter, the first person model is the Blaster's rig (robot arms and animations) with the
/// Blaster's gun taken off and this one put on the gun bone. Gun space is the Blender rifle's: inches, +X forward
/// along the barrel, +Y to the side, +Z up, the grip at the origin. PlayerWeapon finds "GunkFill" (its scale is the
/// tank level, its material the goo colour) and "Fan" (spun while sucking) by name.
/// </summary>
public static class GoreVacModel
{
	/// <summary> The mouth of the nozzle (gun space, inches): where goo goes in and gore balls come out. </summary>
	public static readonly Vector3 MuzzleAt = new( 31f, 0f, 3.0f );
	/// <summary> The tank on top: its middle, radius and length (inches, along X). </summary>
	static readonly Vector3 TankAt = new( 4.5f, 0f, 7.4f );
	const float TankRadius = 1.75f, TankLength = 10f;

	public static readonly Color Orange = new( 1f, 0.5f, 0.08f );
	public static readonly Color Gunk = new( 0.55f, 1f, 0.25f );

	static StandardMaterial3D orange, black, metal, glass, yellow, glow;
	static StandardMaterial3D Paint( Color c, float roughness, float metallic = 0f ) => new() { AlbedoColor = c, Roughness = roughness, Metallic = metallic, RimEnabled = true, Rim = 0.3f, RimTint = 0.6f };
	static StandardMaterial3D OrangePaint => orange ??= Paint( Orange, 0.4f );
	static StandardMaterial3D BlackPaint => black ??= Paint( new Color( 0.04f, 0.04f, 0.05f ), 0.6f );
	static StandardMaterial3D Metal => metal ??= Paint( new Color( 0.62f, 0.64f, 0.68f ), 0.3f, 0.7f );
	static StandardMaterial3D YellowPaint => yellow ??= Paint( new Color( 1f, 0.85f, 0.12f ), 0.45f );
	static StandardMaterial3D Glass => glass ??= new StandardMaterial3D
	{
		AlbedoColor = new Color( 0.75f, 0.95f, 1f, 0.12f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.2f,
		MetallicSpecular = 0.15f, RimEnabled = true, Rim = 0.35f, RimTint = 0.2f, CullMode = BaseMaterial3D.CullModeEnum.Back,
	};
	static StandardMaterial3D Glow => glow ??= new StandardMaterial3D { AlbedoColor = Gunk, EmissionEnabled = true, Emission = Gunk, EmissionEnergyMultiplier = 2.2f };

	/// <summary> A fresh goo material for one tank (its colour follows what was sucked up). </summary>
	public static StandardMaterial3D GunkMaterial( Color colour ) => new()
	{
		AlbedoColor = colour, Roughness = 0.15f, EmissionEnabled = true, Emission = colour, EmissionEnergyMultiplier = 0.8f,
		ClearcoatEnabled = true, Clearcoat = 0.8f, ClearcoatRoughness = 0.1f,
	};

	static readonly Basis GunToBone = new( new Vector3( 0f, 1f, 0f ), new Vector3( -1f, 0f, 0f ), new Vector3( 0f, 0f, 1f ) );

	/// <summary> Puts the Gore Vac on the rig's gun bone, and a Muzzle node at the nozzle's mouth. </summary>
	public static void Attach( Skeleton3D skeleton )
	{
		if ( skeleton.FindBone( "gun" ) < 0 ) return;
		var onGun = new BoneAttachment3D { Name = "Gore Vac Gun Bone", BoneName = "gun" };
		skeleton.AddChild( onGun );
		var body = new Node3D { Name = "Gore Vac", Transform = new Transform3D( GunToBone.Scaled( Vector3.One * 0.01f ), Vector3.Zero ) };
		onGun.AddChild( body );
		BuildGun( body );
		onGun.AddChild( new Node3D { Name = "Muzzle", Position = GunToBone * MuzzleAt * 0.01f } );
	}

	// ---- Shapes (gun space, inches) ----

	static MeshInstance3D Part( Node3D parent, Mesh mesh, Material material, Vector3 at, Basis? turn = null )
	{
		var mi = new MeshInstance3D { Name = "Part", Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		parent.AddChild( mi );
		mi.Transform = new Transform3D( turn ?? Basis.Identity, at );
		return mi;
	}

	static MeshInstance3D Box( Node3D parent, Vector3 centre, Vector3 size, Material material, float tipDegrees = 0f ) =>
		Part( parent, new BoxMesh { Size = size }, material, centre, tipDegrees == 0f ? null : new Basis( new Vector3( 0f, 1f, 0f ), MathX.Rad( tipDegrees ) ) );

	/// <summary> A cylinder along X (the barrel); `frontRadius` is the radius at +X. </summary>
	static MeshInstance3D AlongX( Node3D parent, Vector3 centre, float radius, float length, Material material, float frontRadius = -1f, int sides = 20 ) =>
		Part( parent, new CylinderMesh { BottomRadius = radius, TopRadius = frontRadius < 0f ? radius : frontRadius, Height = length, RadialSegments = sides, Rings = 1 }, material, centre, new Basis( Vector3.Back, -MathF.PI * 0.5f ) );

	static void BuildGun( Node3D gun )
	{
		// Grip, trigger and guard where the Blaster's were, so the right hand closes round them.
		Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), BlackPaint, 18f );
		Box( gun, new Vector3( 1.6f, 0f, -1.1f ), new Vector3( 0.5f, 0.5f, 1.6f ), YellowPaint );
		Box( gun, new Vector3( 2.4f, 0f, -2.1f ), new Vector3( 4.2f, 0.6f, 0.5f ), BlackPaint );

		// The motor housing: chunky hazard orange over a black frame, with black stripes down the sides and vents.
		Box( gun, new Vector3( 5.5f, 0f, 3.6f ), new Vector3( 16f, 4.6f, 5.2f ), OrangePaint );
		Box( gun, new Vector3( 5.5f, 0f, 0.8f ), new Vector3( 13f, 3.4f, 1.6f ), BlackPaint );
		foreach ( var side in new[] { -1f, 1f } )
		{
			for ( var i = 0; i < 4; i++ )
				Part( gun, new BoxMesh { Size = new Vector3( 0.9f, 0.14f, 5.4f ) }, BlackPaint, new Vector3( -0.5f + i * 2.4f, side * 2.33f, 3.6f ), new Basis( new Vector3( 0f, 1f, 0f ), MathX.Rad( 35f ) ) );
			for ( var i = 0; i < 3; i++ ) Box( gun, new Vector3( 9.5f + i * 1.2f, side * 2.34f, 3.4f ), new Vector3( 0.4f, 0.14f, 2.6f ), BlackPaint );
			Box( gun, new Vector3( 12.4f, side * 2.35f, 4.9f ), new Vector3( 1.6f, 0.14f, 0.7f ), Glow );   // a status light
		}

		// The tank on top: a glass drum with a metal cap at the front, a metal ring round the open back (so you see the
		// goo through the glass end from behind), a clamp, and the goo inside (level set from the gun).
		AlongX( gun, TankAt, TankRadius, TankLength, Glass, sides: 24 );
		AlongX( gun, TankAt + new Vector3( TankLength * 0.5f, 0f, 0f ), TankRadius + 0.3f, 0.9f, Metal, sides: 24 );
		var ring = new TorusMesh { InnerRadius = TankRadius - 0.05f, OuterRadius = TankRadius + 0.35f, Rings = 24, RingSegments = 8 };
		Part( gun, ring, Metal, TankAt - new Vector3( TankLength * 0.5f, 0f, 0f ), new Basis( Vector3.Back, -MathF.PI * 0.5f ) );
		AlongX( gun, TankAt + new Vector3( 1.5f, 0f, 0f ), TankRadius + 0.12f, 0.5f, BlackPaint, sides: 24 );
		var fill = new Node3D { Name = "GunkFill", Position = TankAt - new Vector3( 0f, 0f, TankRadius * 0.92f ) };
		gun.AddChild( fill );
		var goo = AlongX( fill, new Vector3( 0f, 0f, TankRadius * 0.92f ), TankRadius * 0.92f, TankLength - 0.6f, GunkMaterial( Gunk ), sides: 24 );
		goo.Name = "Gunk";
		fill.Scale = new Vector3( 1f, 1f, 0.3f );
		// The intake pipe from the tank's front cap down into the nozzle.
		AlongX( gun, TankAt + new Vector3( TankLength * 0.5f + 1.2f, 0f, -1.2f ), 0.8f, 2.4f, Metal );
		Box( gun, new Vector3( TankAt.X + TankLength * 0.5f + 2.2f, 0f, 5.6f ), new Vector3( 1.4f, 1.4f, 4.4f ), Metal );

		// The hose the left hand holds: black ribs on a grey tube.
		AlongX( gun, new Vector3( 18.5f, 0f, 3.0f ), 2.0f, 9f, BlackPaint );
		for ( var i = 0; i < 6; i++ ) AlongX( gun, new Vector3( 14.6f + i * 1.55f, 0f, 3.0f ), 2.25f, 0.55f, Metal );

		// The nozzle: a flared black funnel with a yellow lip, a green glow ring and a fan turning in its throat.
		AlongX( gun, new Vector3( 26.5f, 0f, 3.0f ), 2.1f, 9f, BlackPaint, 4.4f, 28 );
		AlongX( gun, new Vector3( 31.1f, 0f, 3.0f ), 4.55f, 0.6f, YellowPaint, 4.65f, 28 );
		AlongX( gun, new Vector3( 24.2f, 0f, 3.0f ), 2.85f, 0.35f, Glow, sides: 28 );
		var fan = new Node3D { Name = "Fan", Position = new Vector3( 25.5f, 0f, 3.0f ) };
		gun.AddChild( fan );
		AlongX( fan, Vector3.Zero, 0.6f, 0.8f, Metal );
		for ( var i = 0; i < 4; i++ )
		{
			var a = i * MathF.PI * 0.5f;
			var radial = new Vector3( 0f, MathF.Cos( a ), MathF.Sin( a ) );
			Part( fan, new BoxMesh { Size = new Vector3( 0.25f, 0.9f, 2.2f ) }, Metal, radial * 1.3f, new Basis( Vector3.Right, a - MathF.PI * 0.5f ) * new Basis( Vector3.Up, 0.5f ) );
		}

		// Stock: black with an orange band and a yellow butt plate.
		Box( gun, new Vector3( -7.0f, 0f, 2.9f ), new Vector3( 8f, 3.0f, 4.6f ), BlackPaint );
		Box( gun, new Vector3( -4.0f, 0f, 2.9f ), new Vector3( 0.7f, 3.15f, 4.75f ), OrangePaint );
		Box( gun, new Vector3( -11.2f, 0f, 2.7f ), new Vector3( 0.9f, 3.1f, 5.4f ), YellowPaint );
	}

	/// <summary> A little Gore Vac for the station's hologram (metres, nozzle toward -Z). </summary>
	public static void BuildIcon( Node3D parent )
	{
		var gun = new Node3D { Name = "Gore Vac Icon", Basis = new Basis( new Vector3( 0f, 0f, -1f ), new Vector3( -1f, 0f, 0f ), new Vector3( 0f, 1f, 0f ) ).Scaled( Vector3.One * 0.03f ) };
		parent.AddChild( gun );
		BuildGun( gun );
		if ( gun.FindChild( "GunkFill", true, false ) is Node3D fill ) fill.Scale = new Vector3( 1f, 1f, 0.75f );
	}
}
