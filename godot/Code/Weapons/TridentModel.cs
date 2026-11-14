using Godot;

/// <summary>
/// The trident shooter (bought on the wreck site): a spear gun for the robot, in the game's alien colours (yellow,
/// neon green, purple and black), built from simple flat-coloured shapes. The first person model is the Blaster's rig
/// (its robot arms and animations) with the Blaster's own gun taken off and this one put on the gun bone in its place,
/// so the hands hold it and it moves with every draw, fire and reload. The flying trident is built here too.
/// Gun space is the Blender rifle's: inches, +X forward along the barrel, +Y to the side, +Z up, the grip at the origin.
/// </summary>
public static class TridentModel
{
	/// <summary> The rig the trident shooter borrows (arms, hands and animations). </summary>
	public const string Rig = "rifle_blaster";
	/// <summary> Where the trident leaves the barrel (gun space, inches). </summary>
	public static readonly Vector3 MuzzleAt = new( 34f, 0f, 3f );
	/// <summary> The sight's height over the grip (inches), for the aim pose: the same as the Blaster's. </summary>
	public const float SightHeight = 8.85f;

	public static readonly Color Yellow = new( 1f, 0.82f, 0.1f );
	public static readonly Color Green = new( 0.35f, 1f, 0.3f );
	public static readonly Color Purple = new( 0.42f, 0.12f, 0.68f );
	public static readonly Color Black = new( 0.035f, 0.035f, 0.045f );

	static StandardMaterial3D yellow, green, purple, black;
	static StandardMaterial3D Paint( Color c, float roughness ) => new() { AlbedoColor = c, Roughness = roughness, RimEnabled = true, Rim = 0.3f, RimTint = 0.6f };
	public static StandardMaterial3D YellowPaint => yellow ??= Paint( Yellow, 0.45f );
	public static StandardMaterial3D PurplePaint => purple ??= Paint( Purple, 0.4f );
	public static StandardMaterial3D BlackPaint => black ??= Paint( Black, 0.6f );
	public static StandardMaterial3D GreenGlow => green ??= new StandardMaterial3D
	{
		AlbedoColor = Green, EmissionEnabled = true, Emission = Green, EmissionEnergyMultiplier = 2.5f, Roughness = 0.3f,
	};

	/// <summary> Gun space (X forward, Z up) to the gun bone's space (Y forward, Z up), as the rifle's muzzle offset uses it. </summary>
	static readonly Basis GunToBone = new( new Vector3( 0f, 1f, 0f ), new Vector3( -1f, 0f, 0f ), new Vector3( 0f, 0f, 1f ) );

	/// <summary> The Blaster rig's surfaces that are the robot's arms (everything else on it is the Blaster's gun). </summary>
	static readonly HashSet<string> ArmSurfaces = new() { "plate", "joint", "trim" };

	/// <summary> Takes the Blaster's gun off the rig: each mesh keeps only its arm surfaces (a copy; the original is shared). </summary>
	public static void StripGun( Node3D model )
	{
		foreach ( var mi in model.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			if ( mi.Mesh is not ArrayMesh source ) continue;
			var mesh = (ArrayMesh)source.Duplicate();
			for ( var i = mesh.GetSurfaceCount() - 1; i >= 0; i-- )
				if ( !ArmSurfaces.Contains( mesh.SurfaceGetMaterial( i )?.ResourceName ?? "" ) ) mesh.SurfaceRemove( i );
			mesh.ShadowMesh = null;   // the imported shadow copy still has the Blaster on it
			mi.Mesh = mesh;
		}
	}

	/// <summary> Puts the trident shooter on the rig's gun bone (its canister on the mag bone) and a Muzzle node at the barrel's end. </summary>
	public static void Attach( Skeleton3D skeleton )
	{
		var gunBone = skeleton.FindBone( "gun" );
		if ( gunBone < 0 ) return;
		var onGun = new BoneAttachment3D { Name = "Trident Gun Bone", BoneName = "gun" };
		skeleton.AddChild( onGun );
		var gunSpace = new Transform3D( GunToBone.Scaled( Vector3.One * 0.01f ), Vector3.Zero );   // inches, under the 0.01 per inch rig
		var body = new Node3D { Name = "Trident Shooter", Transform = gunSpace };
		onGun.AddChild( body );
		BuildGun( body );
		onGun.AddChild( new Node3D { Name = "Muzzle", Position = GunToBone * MuzzleAt * 0.01f } );

		// The canister rides the mag bone, so the reload swaps it like the Blaster's power cell.
		var magBone = skeleton.FindBone( "mag" );
		Node3D canisterParent = onGun;
		var canisterSpace = gunSpace;
		if ( magBone >= 0 )
		{
			var onMag = new BoneAttachment3D { Name = "Trident Mag Bone", BoneName = "mag" };
			skeleton.AddChild( onMag );
			canisterParent = onMag;
			canisterSpace = skeleton.GetBoneGlobalRest( magBone ).AffineInverse() * skeleton.GetBoneGlobalRest( gunBone ) * gunSpace;
		}
		var canister = new Node3D { Name = "Trident Canister", Transform = canisterSpace };
		canisterParent.AddChild( canister );
		BuildCanister( canister );
	}

	// ---- Shapes (gun space, inches) ----

	static MeshInstance3D Part( Node3D parent, Mesh mesh, Material material, Vector3 at, Basis? turn = null )
	{
		var mi = new MeshInstance3D { Name = "Part", Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		parent.AddChild( mi );
		mi.Transform = new Transform3D( turn ?? Basis.Identity, at );
		return mi;
	}

	/// <summary> A box, optionally tipped about the side axis (degrees, like the Blender grip's rot=(0, -18, 0)). </summary>
	static void Box( Node3D parent, Vector3 centre, Vector3 size, Material material, float tipDegrees = 0f ) =>
		Part( parent, new BoxMesh { Size = size }, material, centre, tipDegrees == 0f ? null : new Basis( new Vector3( 0f, 1f, 0f ), MathX.Rad( tipDegrees ) ) );

	/// <summary> A cylinder along the barrel (X); the end radius is the front one. </summary>
	static void AlongX( Node3D parent, Vector3 centre, float radius, float length, Material material, float frontRadius = -1f, int sides = 18 ) =>
		Part( parent, new CylinderMesh { BottomRadius = radius, TopRadius = frontRadius < 0f ? radius : frontRadius, Height = length, RadialSegments = sides, Rings = 1 }, material, centre, new Basis( Vector3.Back, -MathF.PI * 0.5f ) );

	/// <summary> A cylinder standing up (Z). </summary>
	static void AlongZ( Node3D parent, Vector3 centre, float radius, float length, Material material, int sides = 16 ) =>
		Part( parent, new CylinderMesh { BottomRadius = radius, TopRadius = radius, Height = length, RadialSegments = sides, Rings = 1 }, material, centre, new Basis( Vector3.Right, MathF.PI * 0.5f ) );

	static void BuildGun( Node3D gun )
	{
		// Grip, trigger and guard where the Blaster's were, so the right hand closes round them.
		Box( gun, new Vector3( -0.6f, 0f, -4.0f ), new Vector3( 3.0f, 2.7f, 7.8f ), BlackPaint, 18f );   // the foot leans back, as the fingers are posed
		Box( gun, new Vector3( 1.6f, 0f, -1.1f ), new Vector3( 0.5f, 0.5f, 1.6f ), YellowPaint );
		Box( gun, new Vector3( 2.4f, 0f, -2.1f ), new Vector3( 4.2f, 0.6f, 0.5f ), BlackPaint );
		// Receiver: purple, a black underframe, yellow stripes and glowing green windows down both sides.
		Box( gun, new Vector3( 5.0f, 0f, 3.4f ), new Vector3( 18f, 3.8f, 4.8f ), PurplePaint );
		Box( gun, new Vector3( 5.0f, 0f, 1.0f ), new Vector3( 14f, 3.0f, 1.6f ), BlackPaint );
		foreach ( var side in new[] { -1f, 1f } )
		{
			Box( gun, new Vector3( 5.0f, side * 1.93f, 5.0f ), new Vector3( 14f, 0.14f, 0.5f ), YellowPaint );
			Box( gun, new Vector3( 7.0f, side * 1.95f, 3.1f ), new Vector3( 6f, 0.14f, 1.4f ), GreenGlow );
			for ( var i = 0; i < 4; i++ ) Box( gun, new Vector3( 4.8f + i * 1.5f, side * 1.98f, 3.1f ), new Vector3( 0.3f, 0.14f, 1.6f ), BlackPaint );
			// Swept yellow fins at the back of the receiver.
			Box( gun, new Vector3( -1.5f, side * 2.2f, 4.6f ), new Vector3( 4f, 0.25f, 1.6f ), YellowPaint, 20f );
		}
		// Sight: a black frame on two posts with a glowing green dot at the Blaster's sight height.
		foreach ( var x in new[] { 3f, 11f } ) Box( gun, new Vector3( x, 0f, 7.0f ), new Vector3( 1.2f, 1.4f, 2.2f ), BlackPaint );
		Box( gun, new Vector3( 7f, 0f, 8.15f ), new Vector3( 9.4f, 1.4f, 0.8f ), PurplePaint );
		Part( gun, new SphereMesh { Radius = 0.24f, Height = 0.48f, RadialSegments = 10, Rings = 6 }, GreenGlow, new Vector3( 10.9f, 0f, SightHeight ) );
		// Shroud the left hand cups (the Blaster's size), banded yellow, with green coils glowing through.
		AlongX( gun, new Vector3( 19.5f, 0f, 3.0f ), 2.2f, 11f, BlackPaint, sides: 20 );
		foreach ( var x in new[] { 14.6f, 24.4f } ) AlongX( gun, new Vector3( x, 0f, 3.0f ), 2.38f, 0.9f, YellowPaint, sides: 20 );
		foreach ( var x in new[] { 17.6f, 19.5f, 21.4f } ) AlongX( gun, new Vector3( x, 0f, 3.0f ), 2.28f, 0.35f, GreenGlow, sides: 20 );
		// The launch tube, a green collar and three purple guide fins flaring round the mouth.
		AlongX( gun, new Vector3( 29.2f, 0f, 3.0f ), 1.35f, 8.4f, PurplePaint, 1.5f );
		AlongX( gun, new Vector3( 33.6f, 0f, 3.0f ), 1.75f, 0.8f, GreenGlow );
		for ( var i = 0; i < 3; i++ )
		{
			var a = i / 3f * MathF.Tau + MathF.PI * 0.5f;
			var radial = new Vector3( 0f, MathF.Cos( a ), MathF.Sin( a ) );
			// The fin's long side runs along the tube, its depth (local Z) turned out along `radial`.
			Part( gun, new BoxMesh { Size = new Vector3( 3.2f, 0.3f, 1.2f ) }, PurplePaint, new Vector3( 31.5f, 0f, 3.0f ) + radial * 1.9f, new Basis( Vector3.Right, a - MathF.PI * 0.5f ) );
		}
		// Stock: black with a purple band and a yellow butt plate.
		Box( gun, new Vector3( -7.0f, 0f, 2.9f ), new Vector3( 8f, 3.0f, 4.6f ), BlackPaint );
		Box( gun, new Vector3( -4.0f, 0f, 2.9f ), new Vector3( 0.7f, 3.15f, 4.75f ), PurplePaint );
		Box( gun, new Vector3( -11.2f, 0f, 2.7f ), new Vector3( 0.9f, 3.1f, 5.4f ), YellowPaint );

		// The next trident, loaded: its head out of the mouth, the shaft hidden down the tube.
		var loaded = new Node3D { Name = "Loaded" };
		gun.AddChild( loaded );
		const float k = 30f;   // inches per trident metre: smaller than the one in flight, so it fits the tube
		var crossbar = MuzzleAt.X + 0.5f;
		// The crossbar (at HeadAt along the trident's -Z, which runs down the barrel) just out of the mouth.
		loaded.Transform = new Transform3D( TridentToGun.Scaled( Vector3.One * k ), new Vector3( crossbar + HeadAt * k, 0f, 3.0f ) );
		BuildTrident( loaded, tail: false );
	}

	/// <summary> The canister of tridents under the receiver (in the Blaster's power cell spot, so the reload moves it). </summary>
	static void BuildCanister( Node3D canister )
	{
		AlongZ( canister, new Vector3( 8.0f, 0f, -3.4f ), 1.5f, 5.4f, PurplePaint );
		AlongZ( canister, new Vector3( 8.0f, 0f, -0.8f ), 1.62f, 0.5f, YellowPaint );
		AlongZ( canister, new Vector3( 8.0f, 0f, -6.2f ), 1.62f, 0.7f, BlackPaint );
		Box( canister, new Vector3( 9.45f, 0f, -3.4f ), new Vector3( 0.16f, 0.7f, 3.6f ), GreenGlow );
	}

	// ---- The trident (metres, pointing down -Z, prongs spread along X) ----

	/// <summary> Where the crossbar sits along the trident (metres from its middle, toward the point at -Z). </summary>
	const float HeadAt = -0.25f;

	/// <summary> Trident space (-Z forward, X side, Y up) to gun space (X forward, Z up). </summary>
	static readonly Basis TridentToGun = new( new Vector3( 0f, -1f, 0f ), new Vector3( 0f, 0f, 1f ), new Vector3( -1f, 0f, 0f ) );

	static void AlongMinusZ( Node3D parent, Vector3 centre, float radius, float length, Material material, float frontRadius = -1f, int sides = 10 ) =>
		Part( parent, new CylinderMesh { BottomRadius = radius, TopRadius = frontRadius < 0f ? radius : frontRadius, Height = length, RadialSegments = sides, Rings = 1 }, material, centre, new Basis( Vector3.Right, -MathF.PI * 0.5f ) );

	/// <summary>
	/// A trident about 0.95 m long: a yellow shaft with purple bindings and a black butt, a yellow crossbar, three
	/// yellow prongs (the middle one longest) with glowing green barbed points, and two black tail fins (not on the
	/// loaded one, whose tail is down the tube).
	/// </summary>
	public static void BuildTrident( Node3D parent, bool tail = true )
	{
		AlongMinusZ( parent, new Vector3( 0f, 0f, 0.1f ), 0.018f, 0.7f, YellowPaint );
		foreach ( var z in new[] { 0.32f, 0.12f, -0.12f } ) AlongMinusZ( parent, new Vector3( 0f, 0f, z ), 0.026f, 0.05f, PurplePaint );
		Part( parent, new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 8, Rings = 4 }, BlackPaint, new Vector3( 0f, 0f, 0.45f ) );
		if ( tail )
			foreach ( var side in new[] { -1f, 1f } )
				Part( parent, new BoxMesh { Size = new Vector3( 0.07f, 0.005f, 0.12f ) }, BlackPaint, new Vector3( side * 0.045f, 0f, 0.38f ), new Basis( Vector3.Up, side * 0.35f ) );
		Part( parent, new BoxMesh { Size = new Vector3( 0.23f, 0.03f, 0.035f ) }, YellowPaint, new Vector3( 0f, 0f, HeadAt ) );
		Part( parent, new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 8, Rings = 4 }, PurplePaint, new Vector3( 0f, 0f, HeadAt + 0.02f ) );
		foreach ( var x in new[] { -0.1f, 0f, 0.1f } )
		{
			var length = x == 0f ? 0.26f : 0.2f;
			AlongMinusZ( parent, new Vector3( x, 0f, HeadAt - length * 0.5f ), 0.012f, length, YellowPaint, 0.01f, 8 );
			var tip = HeadAt - length;
			AlongMinusZ( parent, new Vector3( x, 0f, tip - 0.035f ), 0.022f, 0.07f, GreenGlow, 0f, 8 );
			// A barb on the outer prongs, angled back.
			if ( x != 0f ) Part( parent, new BoxMesh { Size = new Vector3( 0.012f, 0.012f, 0.06f ) }, GreenGlow, new Vector3( x + MathF.Sign( x ) * 0.018f, 0f, tip + 0.03f ), new Basis( Vector3.Up, -MathF.Sign( x ) * 0.5f ) );
		}
	}
}
