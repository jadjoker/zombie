using Godot;

/// <summary>
/// Port of the s&box MC61: a floating ghost magician in a tux and top hat, with one big living eye and two detached
/// white-gloved hands. The boss. Eye beam (charge, then a slow sweeping beam), "Pick any card" rings of cursed cards,
/// and the worm dance (takes extra damage). Hat, hands and tail pieces can be shot off; the eye is a crit spot and
/// can be popped; the head is lethal. The eye is a live painted picture; the hands are skinned and curled by code.
/// Numbers are the s&box ones (inches). Surface shaders and the hologram spawn-in come with step 7c.
/// </summary>
public partial class MC61 : PartCreature, MC61Marker
{
	public float Size = 1.35f;
	public float PreferredDistance = 600f;
	public float HoverHeight = 110f;
	public float MoveSpeed = 230f;
	public float ChargeTime = 1.6f;
	public float BeamTime = 2f;
	public float BeamDamagePerSecond = 40f;
	public float BeamWidth = 16f;
	public float BeamTrackSpeed = 40f;
	public float BeamRange = 3000f;
	public int CardsPerRing = 22;
	public int CardRings = 2;
	public float CardSpeed = 520f;
	public float CardDamage = 6f;
	/// <summary> His spin attack (it used to be rings of cards): this many homing missiles, 15 damage each. </summary>
	public int MissilesMin = 5, MissilesMax = 8;
	int missilesThisAttack = 6;
	public Vector2 AttackGap = new( 2f, 4.5f );
	public float DanceChance = 0.1f;
	public float DanceTime = 4.5f;
	public float DanceDamageScale = 1.6f;
	public float RamDamage = 25f;
	public float EyeMultiplier = 2.5f;
	public float TorsoMultiplier = 0.6f;
	public float LimbMultiplier = 0.15f;
	public float TorsoHealth = 110f;
	public float HatHealth = 40f;
	public float HandHealth = 130f;
	public float TailHealth = 60f;
	public float HandScale = 0.85f;   // seahorse-sized pectoral fins, tucked in at the neck
	public float EyeHealth = 350f;
	public enum Surface { Plain, RimGlow, Sheen, Mosaic }
	/// <summary>
	/// How his body (and tail and fins) is shaded: plain, a blue rim glow with drifting mist, a rainbow sheen, or (his
	/// look now) a teal and aqua stained-glass mosaic in yellow leading, after a jewelled seahorse.
	/// </summary>
	public Surface Look = Surface.Mosaic;
	/// <summary> The mosaic's cell size in the body models' own units (about 4.5 inches), and in metres for the fins. </summary>
	const float MosaicCell = 0.045f, FinCell = 3.2f * U.In;
	/// <summary> He scans in from the floor up as a hologram for this long when he appears (0 = off). </summary>
	public float SpawnInTime = 1.8f;
	/// <summary> Debug: false leaves the hand rigs in their rest pose. </summary>
	public static bool PoseHands = true;

	public enum Mode { Float, Charge, Beam, Cards, Dance }
	public Mode State { get; private set; }
	public Vector3 BeamEnd { get; private set; }

	public static readonly List<MC61> All = new();
	public float HealthFraction => health?.Fraction ?? 0f;
	public string Line { get; private set; } = "";
	public TimeSince TimeSinceLine { get; private set; } = 999f;

	const int Torso = 0, Head = 1, Eye = 2, Hat = 3, HandL = 4, HandR = 5, Tail0 = 6, TailCount = 4;
	const int PartCount = 10;

	static readonly Color Tux = new( 0.06f, 0.06f, 0.09f );
	static readonly Color GhostBody = new( 0.7f, 0.78f, 0.93f );
	/// <summary> The eye picture's lid, in his mosaic's teal. </summary>
	static readonly Color LidTeal = new( 0.12f, 0.6f, 0.62f );
	static readonly Color Skin = new( 0.42f, 0.47f, 0.4f );
	static readonly Color Glove = new( 0.97f, 0.97f, 1f );
	static readonly Color Iris = new( 1f, 0.12f, 0.08f );
	static readonly Color BeamColor = new( 1f, 0.22f, 0.12f );

	public MC61() => parts = new Part[PartCount];
	MeshInstance3D beam, beamCore, aimLine;

	TimeSince stateTime;
	TimeUntil nextAttack;
	TimeSince beamTick;
	TimeSince ramTime = 999f;
	Vector3 velocity;        // s&box units/s
	Vector3 beamDirection;   // Godot unit vector
	int cardRingsThrown;
	float orbitSign = 1f;
	TimeSince localStateTime;
	Mode seenState;
	TimeSince scorchTime;
	TimeSince bleedTimer;

	// Layout numbers from Tools/blender/mc61_ghost.py (s&box local, inches).
	static readonly Vector3 HeadPivot = new( 0, 0, 38f );
	// His seahorse head (smaller than the old ghost's): the one eye on the brow above the snout, the hat on the coronet.
	static readonly Vector3 EyeFromHead = new( 7.6f, 0, 6.8f );
	const float EyeScale = 0.38f;
	static readonly Vector3 HatFromHead = new( -1.5f, 0, 14.5f );
	// s&box quaternion (-0.16307, -0.19920, -0.61210, 0.74771) with its axis turned into Godot space.
	static readonly Quaternion HatRotation = new( 0.19920f, -0.61210f, 0.16307f, 0.74771f );
	const float HatTilt = 14f;
	static readonly Vector3 TailRoot = new( -16.6f, 0, -25f );
	const float TailSegment = 10f;
	static readonly float[] TailRadius = { 10.4f, 8.6f, 6.2f, 3.6f };

	/// <summary> An s&box direction (x forward, y left, z up) as a Godot direction (no unit change). </summary>
	static Vector3 SDir( Vector3 v ) => new( -v.Y, v.Z, -v.X );
	/// <summary> A Godot vector as an s&box one, metres to inches. </summary>
	static Vector3 ToS( Vector3 v ) => new Vector3( -v.Z, -v.X, v.Y ) / U.In;
	/// <summary> s&box Rotation.FromAxis (degrees) as a Godot basis. </summary>
	static Basis Ax( Vector3 sboxAxis, float degrees ) => new( SDir( sboxAxis ).Normalized(), MathX.Rad( degrees ) );
	static readonly Vector3 SForward = new( 1, 0, 0 ), SLeft = new( 0, 1, 0 ), SUp = new( 0, 0, 1 );

	sealed class Hand
	{
		public float Side;
		public Node3D Root;
		public Skeleton3D Skeleton;
		public readonly int[] Bone = new int[14];
		public readonly Transform3D[] Rest = new Transform3D[14];
		public Basis ToSkeleton = Basis.Identity;   // s&box part space to skeleton space (rotation)
		public bool Ready;
		public readonly float[] Curl = new float[14];
		public float Splay = 38f;
	}
	readonly Hand[] hands = new Hand[2];

	const int EyeW = 128, EyeH = 96;
	Image eyeImage;
	ImageTexture eyeTexture;
	byte[] eyePixels;

	readonly float[] tailPitch = new float[TailCount], tailPitchVel = new float[TailCount], tailRoll = new float[TailCount], tailRollVel = new float[TailCount];
	Vector3 lastRigPosition;
	readonly Vector3[] handVel = new Vector3[2];
	Vector3 lastHandRigPosition;

	public static MC61 Spawn( Node parent, Vector3 position, float maxHealth )
	{
		var boss = new MC61 { Name = "MC61" };
		Sfx.Play( "boss_spawn", position, 1f );
		return Place( boss, parent, position, maxHealth );
	}

	protected override void Cleanup()
	{
		base.Cleanup();
		foreach ( var b in new[] { beam, beamCore, aimLine } ) if ( b is not null && IsInstanceValid( b ) ) b.QueueFree();
	}

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }
	protected override string Kind => "MC61";
	protected override Color GibGoo => Goo;

	protected override void Setup()
	{
		BeginSetup( 2000f );
		nextAttack = 2.5f;
		orbitSign = Rng.Float() < 0.5f ? -1f : 1f;
		seenState = State;
		lastRigPosition = lastHandRigPosition = GlobalPosition;
		sinceSpawned = 0f;
		Build();
	}

	// ---- Building ----

	void Build()
	{
		rig = new Node3D { Name = "MC61 Rig" };
		GetTree().CurrentScene.AddChild( rig );
		rig.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized().Scaled( Vector3.One * Size ), GlobalPosition );

		var torso = AddPart( Torso, -1, rig, "Torso", Vector3.Zero, Basis.Identity, TorsoHealth );
		// No suit any more: a seahorse's body, built in code (the old ghost body model and tux are not loaded).
		var bodyModel = SeahorseBody( torso );
		Trigger( torso, new Vector3( -3, 0, -18 ), new Vector3( -3, 0, 20 ), 15f );
		BackSpikes( torso, bodyModel );
		DorsalFin( torso, bodyModel );

		var head = AddPart( Head, Torso, torso.Go, "Head", HeadPivot, Basis.Identity, 0f );
		SeahorseHead( head );
		Trigger( head, new Vector3( -3, 0, 4 ), new Vector3( 7, 0, 4 ), 9f );
		Trigger( head, new Vector3( 9, 0, -2 ), new Vector3( 29, 0, -7.5f ), 3.8f );

		var eye = AddPart( Eye, Head, head.Go, "Eye", EyeFromHead, Basis.Identity, EyeHealth );
		MakeEyePicture();
		var eyeMaterial = new StandardMaterial3D { AlbedoTexture = eyeTexture, AlbedoColor = Colors.White, Roughness = 0.25f, EmissionEnabled = true, Emission = Colors.Black };
		Sculpt( eye, "eyeball", Colors.White, material: eyeMaterial, scale: EyeScale );
		Sculpt( eye, "lids", Colors.Black, scale: EyeScale );
		Trigger( eye, new Vector3( 0, -5, 0 ) * EyeScale, new Vector3( 0, 5, 0 ) * EyeScale, 9f * EyeScale );

		var hat = AddPart( Hat, Head, head.Go, "Hat", HatFromHead, Ax( SForward, HatTilt ) * Ax( SLeft, -8f ), HatHealth );
		Sculpt( hat, "hat", Tux );
		Trigger( hat, new Vector3( 0, 0, 2 ), new Vector3( 0, 0, 10 ), 8.5f );

		BuildHand( HandL, 1f );
		BuildHand( HandR, -1f );

		var parentIndex = Torso;
		Node3D parentGo = torso.Go;
		for ( var i = 0; i < TailCount; i++ )
		{
			var tail = AddPart( Tail0 + i, parentIndex, parentGo, $"Tail {i}", i == 0 ? TailRoot : new Vector3( 0, 0, -TailSegment ), Basis.Identity, TailHealth );
			var tailModel = Sculpt( tail, $"tail{i}", GhostBody, ghost: true );
			Trigger( tail, Vector3.Zero, new Vector3( 0, 0, -TailSegment ), TailRadius[i] * 0.9f );
			if ( i == TailCount - 1 ) BuildCurl( tail, tailModel );
			parentIndex = Tail0 + i;
			parentGo = tail.Go;
		}

		beam = BeamPiece( "Beam", BeamColor * 2.5f );
		beamCore = BeamPiece( "Beam Core", Colors.White * 3f );
		aimLine = BeamPiece( "Aim Line", new Color( 1f, 0.1f, 0.3f ) * 2f );
	}

	void BuildHand( int index, float side )
	{
		var part = AddPart( index, Torso, parts[Torso].Go, side > 0 ? "Hand L" : "Hand R", FinAnchor( side ), Basis.Identity, HandHealth );
		Trigger( part, Vector3.Zero, new Vector3( 0, 0, 13 * HandScale ), 8f * HandScale );
		Trigger( part, Vector3.Zero, new Vector3( 0, side * 11f, 5f ) * HandScale, 7f * HandScale );
		fins[index == HandL ? 0 : 1] = BuildFin( part, side );
		// No glove rig any more: the hand is a fin (the finger curling below finds no skeleton and stands aside).
		hands[index == HandL ? 0 : 1] = new Hand { Side = side, Root = part.Go };
	}

	/// <summary>
	/// The end of his tail rolled up like a seahorse's: the last segment's model is swapped for one smooth tube that
	/// starts exactly where and as wide as that segment did, runs on straight a little, then curls tighter and tighter
	/// into a spiral as it tapers to a point. One mesh, so there is no seam between the tail and its curl.
	/// </summary>
	void BuildCurl( Part tail, Node3D model )
	{
		// Where the last segment's model starts, and how wide it is there, measured from its own vertices.
		var toPart = tail.Go.GlobalTransform.AffineInverse();
		var baseRing = new List<Vector3>();
		foreach ( var mesh in model.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			if ( mesh.Mesh is null ) continue;
			var into = toPart * mesh.GlobalTransform;
			for ( var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++ )
				foreach ( var v in mesh.Mesh.SurfaceGetArrays( surface )[(int)Mesh.ArrayType.Vertex].AsVector3Array() )
				{
					var p = into * v;
					if ( p.Y > -1.2f * U.In && p.Y < 0.6f * U.In ) baseRing.Add( p );
				}
			mesh.Visible = false;   // the tube below takes its place
		}
		var start = Vector3.Zero;
		var r0 = TailRadius[TailCount - 1];
		if ( baseRing.Count > 6 )
		{
			var centre = baseRing.Aggregate( Vector3.Zero, ( a, b ) => a + b ) / baseRing.Count;
			start = centre with { Y = 0f };
			r0 = baseRing.Max( p => new Vector2( p.X - centre.X, p.Z - centre.Z ).Length() ) / U.In;
		}
		r0 = MathX.Clamp( r0, 2f, 9f );

		// The centre line: down from the joint, then bending about his left axis (the way the tail curls), the turn
		// per step growing so it winds into a spiral. Steps shorten as it thins.
		const int steps = 64;
		const int sides = 14;
		var axis = SDir( SLeft ).Normalized();
		var points = new Vector3[steps + 1];
		var tangents = new Vector3[steps + 1];
		var radii = new float[steps + 1];
		var at = start;
		var dir = SDir( new Vector3( 0f, 0f, -1f ) );
		var step = 1.15f;
		for ( var i = 0; i <= steps; i++ )
		{
			var u = i / (float)steps;
			points[i] = at;
			tangents[i] = dir;
			radii[i] = r0 * MathF.Max( 0.07f, 1f - MathF.Pow( u, 0.85f ) * 0.93f );
			var bend = MathX.Clamp( (u - 0.12f) / 0.88f, 0f, 1f );
			dir = (Ax( SLeft, bend * bend * 26f + bend * 6f ) * dir).Normalized();
			at += dir * step * U.In;
			step *= 0.985f;
		}
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		for ( var i = 0; i <= steps; i++ )
		{
			// The ring's frame: the bend axis across, and the normal in the plane of the curl.
			var across = axis;
			var normal = tangents[i].Cross( across ).Normalized();
			for ( var k = 0; k <= sides; k++ )
			{
				var a = k / (float)sides * MathF.Tau;
				var outward = across * MathF.Cos( a ) + normal * MathF.Sin( a );
				st.SetNormal( outward );
				st.AddVertex( points[i] + outward * radii[i] * U.In );
			}
		}
		for ( var i = 0; i < steps; i++ )
			for ( var k = 0; k < sides; k++ )
			{
				var a = i * (sides + 1) + k;
				var b = a + sides + 1;
				st.AddIndex( a ); st.AddIndex( b ); st.AddIndex( a + 1 );
				st.AddIndex( a + 1 ); st.AddIndex( b ); st.AddIndex( b + 1 );
			}
		var tube = new MeshInstance3D { Name = "Tail Curl", Mesh = st.Commit(), MaterialOverride = FxMaterials.Mosaic( 3.6f * U.In ) };
		tail.Go.AddChild( tube );
		tail.Renderers.Add( (tube, Colors.White) );
		// A rounded tip.
		var tip = global::Sculpt.Blob( tail.Go, points[steps], Vector3.One * radii[steps] * 2f * U.In, FxMaterials.Mosaic( 0.6f ), name: "Curl Tip" );
		tail.Renderers.Add( (tip, Colors.White) );
	}

	// ---- The seahorse: body and head built in code ----

	/// <summary> The mosaic for the parts built in code (cells about 3.6 inches, in metres). </summary>
	static ShaderMaterial CodeMosaic() => FxMaterials.Mosaic( 3.6f * U.In );

	/// <summary> A point in a part's space from s&box inches. </summary>
	static Vector3 S( float x, float y, float z ) => U.Sbox( x, y, z );

	/// <summary>
	/// A tube swept along a smooth line through points (Catmull-Rom), its cross-section an ellipse: `deep` front to
	/// back (in the plane of the line) and `wide` side to side. The line lies in the plane across `across`. Godot space.
	/// </summary>
	static ArrayMesh Sweep( Vector3[] points, float[] deep, float[] wide, Vector3 across, int perSpan = 8, int sides = 18 )
	{
		var line = new List<(Vector3 P, float D, float W)>();
		for ( var i = 0; i < points.Length - 1; i++ )
		{
			var p0 = points[Math.Max( 0, i - 1 )];
			var p1 = points[i];
			var p2 = points[i + 1];
			var p3 = points[Math.Min( points.Length - 1, i + 2 )];
			for ( var k = 0; k < perSpan; k++ )
			{
				var t = k / (float)perSpan;
				var t2 = t * t;
				var t3 = t2 * t;
				var p = 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
				var e = t * t * (3f - 2f * t);
				line.Add( (p, MathX.Lerp( deep[i], deep[i + 1], e ), MathX.Lerp( wide[i], wide[i + 1], e )) );
			}
		}
		line.Add( (points[^1], deep[^1], wide[^1]) );
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		for ( var i = 0; i < line.Count; i++ )
		{
			var tangent = (line[Math.Min( line.Count - 1, i + 1 )].P - line[Math.Max( 0, i - 1 )].P).Normalized();
			var normal = tangent.Cross( across ).Normalized();
			for ( var k = 0; k <= sides; k++ )
			{
				var a = k / (float)sides * MathF.Tau;
				var (c, sn) = (MathF.Cos( a ), MathF.Sin( a ));
				st.SetNormal( (across * c / line[i].W + normal * sn / line[i].D).Normalized() );
				st.AddVertex( line[i].P + across * c * line[i].W + normal * sn * line[i].D );
			}
		}
		for ( var i = 0; i < line.Count - 1; i++ )
			for ( var k = 0; k < sides; k++ )
			{
				var a = i * (sides + 1) + k;
				var b = a + sides + 1;
				st.AddIndex( a ); st.AddIndex( b ); st.AddIndex( a + 1 );
				st.AddIndex( a + 1 ); st.AddIndex( b ); st.AddIndex( b + 1 );
			}
		return st.Commit();
	}

	/// <summary> An ellipsoid built at its real size (so the mosaic cells keep their size): radii in s&box inches, forward, left, up. </summary>
	static ArrayMesh Lump( Vector3 centre, float forward, float left, float up, Basis? turn = null )
	{
		var radii = new Vector3( left, up, forward ) * U.In;
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		const int rings = 12, segs = 18;
		var b = turn ?? Basis.Identity;
		for ( var r = 0; r <= rings; r++ )
		{
			var v = r / (float)rings * MathF.PI;
			for ( var k = 0; k <= segs; k++ )
			{
				var u = k / (float)segs * MathF.Tau;
				var unit = new Vector3( MathF.Sin( v ) * MathF.Cos( u ), MathF.Cos( v ), MathF.Sin( v ) * MathF.Sin( u ) );
				st.SetNormal( (b * (unit / radii)).Normalized() );
				st.AddVertex( centre + b * (unit * radii) );
			}
		}
		for ( var r = 0; r < rings; r++ )
			for ( var k = 0; k < segs; k++ )
			{
				var a = r * (segs + 1) + k;
				var c = a + segs + 1;
				st.AddIndex( a ); st.AddIndex( c ); st.AddIndex( a + 1 );   // wound to face outward (checked against the sweep)
				st.AddIndex( a + 1 ); st.AddIndex( c ); st.AddIndex( c + 1 );
			}
		return st.Commit();
	}

	MeshInstance3D Put( Part part, Node3D under, string name, Mesh mesh, Material material = null, Color? tint = null )
	{
		var m = new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = material ?? CodeMosaic() };
		under.AddChild( m );
		part.Renderers.Add( (m, tint ?? Colors.White) );
		if ( material is null ) styleRenderers.Add( m );
		return m;
	}

	/// <summary>
	/// His body, side on like a seahorse's: a fat belly bulging forward, the back curving in, a slim neck bending back
	/// and up into the head. One swept mosaic tube; the tail hangs from its bottom at the back. Returns its holder, for
	/// the back spikes to find.
	/// </summary>
	Node3D SeahorseBody( Part torso )
	{
		var holder = new Node3D { Name = "Seahorse Body" };
		torso.Go.AddChild( holder );
		// The centre line (forward, up, in inches) with its depth and width at each point.
		var line = new (float X, float Z, float Deep, float Wide)[]
		{
			(-14f, -28f, 8f, 6.5f), (-8f, -19f, 13f, 8.6f), (-1f, -7f, 16f, 9.8f), (0f, 5f, 15f, 9.6f),
			(-3f, 16f, 11.5f, 8.2f), (-7f, 25f, 9f, 7f), (-7.5f, 32f, 8f, 6.5f), (-3.5f, 39f, 7.5f, 6.3f),
		};
		var points = line.Select( p => S( p.X, 0f, p.Z ) ).ToArray();
		var mesh = Sweep( points, line.Select( p => p.Deep * U.In ).ToArray(), line.Select( p => p.Wide * U.In ).ToArray(), SDir( SLeft ).Normalized(), 8 );
		Put( torso, holder, "Body", mesh );
		// Rounded off at the bottom, where the tail comes out.
		Put( torso, holder, "Hips", Lump( S( -13f, 0f, -27f ), 8f, 6.2f, 7f ) );
		// The bony rings across the belly, as on a real seahorse: low ridges of the same mosaic.
		for ( var k = 0; k < 6; k++ )
		{
			var z = -16f + k * 5.5f;
			var x = MathX.Lerp( 13f, 10f, k / 5f ) + (k < 3 ? k * 1.2f : (5 - k) * 1.2f);
			Put( torso, holder, "Belly Ring", Lump( S( x, 0f, z ), 2.2f, 7.6f - MathF.Abs( k - 2.5f ) * 0.65f, 1.6f ) );
		}
		return holder;
	}

	/// <summary>
	/// His head side on like a seahorse's: a short deep skull, the long tube of a snout reaching forward and a little
	/// down with a flared end and a small dark mouth, a cheek plate, and a coronet of navy spikes on top (the hat sits
	/// on it). His one big eye sits on the brow above the snout.
	/// </summary>
	void SeahorseHead( Part head )
	{
		var holder = new Node3D { Name = "Seahorse Head" };
		head.Go.AddChild( holder );
		Put( head, holder, "Skull", Lump( S( 0.5f, 0f, 4f ), 9.5f, 7f, 8.5f ) );
		Put( head, holder, "Cheek", Lump( S( 3f, 0f, -2.5f ), 7.5f, 7.6f, 5.5f ) );
		Put( head, holder, "Brow", Lump( S( 5f, 0f, 8f ), 5.5f, 6f, 3.5f ) );
		// The snout: a tapering tube, then the flared tip with the mouth.
		var snout = new[] { S( 6f, 0f, 0f ), S( 14f, 0f, -2.5f ), S( 22f, 0f, -5.2f ), S( 28f, 0f, -7f ) };
		Put( head, holder, "Snout", Sweep( snout, new[] { 4.6f * U.In, 3.2f * U.In, 2.7f * U.In, 2.6f * U.In }, new[] { 4.2f * U.In, 2.9f * U.In, 2.4f * U.In, 2.3f * U.In }, SDir( SLeft ).Normalized(), 6, 14 ) );
		Put( head, holder, "Snout Tip", Lump( S( 28.5f, 0f, -7.2f ), 2.2f, 3.3f, 3.7f ) );
		var mouth = new Color( 0.05f, 0.03f, 0.08f );
		Put( head, holder, "Mouth", Lump( S( 30.4f, 0f, -7.4f ), 0.7f, 1.9f, 1.5f ), Shapes.Material( mouth, "smooth" ), mouth );
		// The coronet: a ring of small navy spikes on the crown, leaning out.
		var navy = new Color( 0.08f, 0.16f, 0.42f );
		var spikeMaterial = Shapes.Material( navy, "smooth" );
		for ( var k = 0; k < 6; k++ )
		{
			var a = k / 6f * MathF.Tau;
			var foot = S( -1.5f + MathF.Cos( a ) * 3.2f, MathF.Sin( a ) * 3.2f, 11.2f );
			var lean = (S( MathF.Cos( a ), MathF.Sin( a ), 0f ).Normalized() * 0.45f + Vector3.Up).Normalized();
			var spike = global::Sculpt.Cone( holder, foot, foot + lean * 3.8f * U.In, 1.1f * U.In, spikeMaterial, 6, "Coronet" );
			head.Renderers.Add( (spike, navy) );
		}
		// A few small spines down the back of the head, carrying on the row on his back.
		for ( var k = 0; k < 3; k++ )
		{
			var foot = S( -8.5f + k * 0.6f, 0f, 8f - k * 4f );
			var lean = (S( -1f, 0f, 0.5f ).Normalized());
			var spike = global::Sculpt.Cone( holder, foot, foot + lean * (3.2f - k * 0.5f) * U.In, 1.1f * U.In, spikeMaterial, 6, "Head Spine" );
			head.Renderers.Add( (spike, navy) );
		}
	}

	/// <summary> Where a pectoral fin sits at rest: just behind the cheek on the neck, as a seahorse's does (inches). </summary>
	static Vector3 FinAnchor( float side ) => new( -2f, side * 8.5f, 30f );

	/// <summary> A hand pose from the old ghost (out at his sides) brought in close: its offset from the old rest, shrunk, about the anchor. </summary>
	static Vector3 Tuck( Vector3 pose, float side ) => FinAnchor( side ) + (pose - new Vector3( 8f, side * 36f, 13f )) * 0.3f;

	/// <summary> His fins (instead of gloved hands), to flap: the left, then the right. </summary>
	readonly Node3D[] fins = new Node3D[2];

	/// <summary>
	/// A seahorse fin in place of a hand: a fan of mosaic membrane in yellow leading, cupped a little, its edge scalloped
	/// between seven pale ribs whose tips stand out past it as spines, on a round mosaic wrist. It spreads from up to
	/// out and a little down, on his outer side. Inches, the part's s&box space.
	/// </summary>
	Node3D BuildFin( Part part, float side )
	{
		var holder = new Node3D { Name = "Fin", Scale = Vector3.One * HandScale };
		part.Go.AddChild( holder );
		const int ribs = 7;
		const float reach = 16f;
		var a0 = MathX.Rad( -18f );
		var a1 = MathX.Rad( 100f );
		// A point of the fan: angle from straight up toward his outer side, distance out, cupped forward in the middle.
		Vector3 At( float a, float r ) => U.Sbox( MathF.Sin( (a - a0) / (a1 - a0) * MathF.PI ) * r * 0.16f, side * MathF.Sin( a ) * r, MathF.Cos( a ) * r );
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		const int steps = 60;
		var root = U.Sbox( 0f, 0f, 0f );
		for ( var k = 0; k < steps; k++ )
		{
			float Edge( int i )
			{
				var u = i / (float)steps * (ribs - 1);
				var nearRib = MathF.Abs( u - MathF.Round( u ) );   // 0 at a rib, 0.5 halfway between
				return reach * (0.8f + 0.2f * MathF.Pow( 1f - nearRib * 2f, 2f ));
			}
			var a = MathX.Lerp( a0, a1, k / (float)steps );
			var b = MathX.Lerp( a0, a1, (k + 1) / (float)steps );
			// Two rings, so the membrane bends with the cup rather than lying as one flat triangle.
			var ma = At( a, Edge( k ) * 0.5f );
			var mb = At( b, Edge( k + 1 ) * 0.5f );
			var ea = At( a, Edge( k ) );
			var eb = At( b, Edge( k + 1 ) );
			foreach ( var v in new[] { root, ma, mb, ma, ea, eb, ma, eb, mb } ) st.AddVertex( v );
		}
		st.GenerateNormals();
		var membrane = new MeshInstance3D { Name = "Membrane", Mesh = st.Commit(), MaterialOverride = FxMaterials.Mosaic( FinCell / HandScale ) };
		holder.AddChild( membrane );
		part.Renderers.Add( (membrane, Colors.White) );
		var ribColor = new Color( 0.62f, 0.97f, 0.9f );
		var ribMaterial = Shapes.Material( ribColor, "smooth" );
		for ( var j = 0; j < ribs; j++ )
		{
			var a = MathX.Lerp( a0, a1, j / (float)(ribs - 1) );
			var rib = global::Sculpt.Limb( holder, root, At( a, reach * 1.1f ), 0.75f * U.In, 0.12f * U.In, ribMaterial, "Rib" );
			part.Renderers.Add( (rib, ribColor) );
		}
		var wrist = Shapes.Part( holder, "Wrist", Vector3.Zero, Shapes.Ellipsoid( 4.2f, 3.6f, 3.6f ), Colors.White );
		wrist.MaterialOverride = FxMaterials.Mosaic( FinCell / HandScale / (4.2f * 2f * U.In) );
		part.Renderers.Add( (wrist, Colors.White) );
		return holder;
	}

	/// <summary> The fins ripple and beat: a slow wave, spread wider with `fan`, shivering with `tremble`. </summary>
	void FlapFins( float t, float fan, float ripple, float tremble )
	{
		for ( var s = 0; s < 2; s++ )
		{
			var fin = fins[s];
			if ( fin is null || !IsInstanceValid( fin ) ) continue;
			var side = s == 0 ? 1f : -1f;
			var beat = MathF.Sin( t * 3.1f + s * 1.7f ) * (10f + ripple * 0.6f) + fan * 0.5f + tremble * MathF.Sin( t * 37f + s ) * 0.8f;
			var sway = MathF.Sin( t * 1.7f + s ) * 8f;
			fin.Basis = Ax( SForward, side * -beat ) * Ax( SLeft, sway ) * Basis.FromScale( Vector3.One * HandScale );
		}
	}

	bool PrepareHand( Hand hand )
	{
		if ( hand.Ready ) return true;
		var skel = hand.Skeleton;
		if ( skel is null || !IsInstanceValid( skel ) || !skel.IsInsideTree() ) return false;
		for ( var i = 0; i < 14; i++ )
		{
			var name = i < 12 ? $"f{i / 3}_{i % 3}" : $"th_{i - 12}";
			var bone = skel.FindBone( name );
			if ( bone < 0 ) return false;
			hand.Bone[i] = bone;
			hand.Rest[i] = skel.GetBoneGlobalRest( bone );
		}
		// s&box part-space axes, expressed in the skeleton's space (rotation only).
		var sboxToGodot = new Basis( SDir( SForward ), SDir( SLeft ), SDir( SUp ) );
		hand.ToSkeleton = skel.GlobalBasis.Orthonormalized().Inverse() * hand.Root.GlobalBasis.Orthonormalized() * sboxToGodot;
		hand.Ready = true;
		return true;
	}

	static readonly Dictionary<string, PackedScene> sculpts = new();
	readonly List<MeshInstance3D> styleRenderers = new();
	readonly List<(MeshInstance3D Renderer, Material Original)> spawnSaved = new();
	ShaderMaterial hologramMaterial;
	TimeSince sinceSpawned = 0f;
	bool spawnDone;

	Material LookMaterial( Color color ) => Look switch
	{
		Surface.RimGlow => FxMaterials.Ghost( color ),
		Surface.Sheen => FxMaterials.Iridescent( color ),
		Surface.Mosaic => FxMaterials.Mosaic( MosaicCell ),
		_ => Shapes.Material( color, "smooth" ),
	};

	/// <summary> Puts the chosen surface shader on the ghost body and tail. </summary>
	public void ApplyLook()
	{
		foreach ( var r in styleRenderers ) if ( IsInstanceValid( r ) ) r.MaterialOverride = LookMaterial( GhostBody );
	}

	/// <summary> The scan-in: every piece wears the hologram shader while a glowing line sweeps up him, then the real materials return. </summary>
	void UpdateSpawnIn()
	{
		if ( spawnDone ) return;
		if ( SpawnInTime <= 0f ) { spawnDone = true; return; }
		if ( hologramMaterial is null )
		{
			hologramMaterial = FxMaterials.Hologram();
			foreach ( var part in parts )
			{
				if ( part is null ) continue;
				foreach ( var (renderer, _) in part.Renderers )
					if ( IsInstanceValid( renderer ) ) spawnSaved.Add( (renderer, renderer.MaterialOverride) );
			}
			foreach ( var (renderer, _) in spawnSaved ) renderer.MaterialOverride = hologramMaterial;
		}
		var progress = MathX.Clamp( (float)sinceSpawned / SpawnInTime, 0f, 1f );
		var centre = parts[Torso].Go.GlobalPosition.Y / U.In;
		hologramMaterial.SetShaderParameter( "reveal_z", MathX.Lerp( centre - 60f * Size, centre + 95f * Size, progress ) );
		if ( progress < 1f ) return;
		foreach ( var (renderer, original) in spawnSaved ) if ( IsInstanceValid( renderer ) ) renderer.MaterialOverride = original;
		spawnSaved.Clear();
		spawnDone = true;
	}

	Node3D Sculpt( Part part, string model, Color color, bool ghost = false, StandardMaterial3D material = null, float scale = 1f )
	{
		if ( !sculpts.TryGetValue( model, out var scene ) )
			sculpts[model] = scene = GD.Load<PackedScene>( $"res://assets/models/creatures/mc61g/{model}.fbx" );
		var node = scene.Instantiate<Node3D>();
		node.Basis = new Basis( Vector3.Up, MathF.PI * 0.5f ) * Basis.FromScale( Vector3.One * 2.54f * scale );
		part.Go.AddChild( node );
		foreach ( var mesh in node.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			// His ghostly body and tail wear the chosen surface look (RimGlow: fx_ghost).
			mesh.MaterialOverride = ghost ? LookMaterial( color ) : material ?? Shapes.Material( color, "smooth" );
			if ( ghost ) styleRenderers.Add( mesh );
			part.Renderers.Add( (mesh, ghost && Look == Surface.Mosaic ? Colors.White : color) );
		}
		return node;
	}

	/// <summary>
	/// A seahorse's crest: a row of small navy spikes up the middle of his back, from below the jacket to the crown of
	/// his head (through the suit). Found on the body model itself: at each height, the back-most point on its middle line.
	/// </summary>
	/// <summary> The body model's vertices on its middle line (side to side), in the torso's space. </summary>
	static List<Vector3> MiddleLine( Part torso, Node3D model )
	{
		var toPart = torso.Go.GlobalTransform.AffineInverse();
		var middle = new List<Vector3>();
		foreach ( var mesh in model.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			if ( mesh.Mesh is null ) continue;
			var into = toPart * mesh.GlobalTransform;
			for ( var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++ )
				foreach ( var v in mesh.Mesh.SurfaceGetArrays( surface )[(int)Mesh.ArrayType.Vertex].AsVector3Array() )
				{
					var p = into * v;
					if ( MathF.Abs( p.X ) < 1.2f * U.In ) middle.Add( p );
				}
		}
		return middle;
	}

	/// <summary> The dorsal fin, to ripple. </summary>
	Node3D dorsal;

	/// <summary>
	/// A seahorse's dorsal fin on his back just above the tail: a fan of fine pale rays standing out behind him with a
	/// mosaic membrane between, its edge scalloped, rooted along the back line found on the body mesh.
	/// </summary>
	void DorsalFin( Part torso, Node3D model )
	{
		var middle = MiddleLine( torso, model );
		if ( middle.Count < 10 ) return;
		const int rays = 9;
		var feet = new List<Vector3>();
		for ( var k = 0; k < rays; k++ )
		{
			var y = MathX.Lerp( -24f, -9f, k / (float)(rays - 1) ) * U.In;
			var band = middle.Where( p => MathF.Abs( p.Y - y ) < 1.5f * U.In ).ToList();
			if ( band.Count == 0 ) return;
			feet.Add( band.MaxBy( p => p.Z ) + Vector3.Back * -0.5f * U.In );
		}
		var root = feet.Aggregate( Vector3.Zero, ( a, b ) => a + b ) / feet.Count;
		dorsal = new Node3D { Name = "Dorsal Fin", Position = root };
		torso.Go.AddChild( dorsal );
		var tips = new List<Vector3>();
		for ( var k = 0; k < rays; k++ )
		{
			var u = k / (float)(rays - 1);
			// Out behind him and fanning up toward the top; longest in the middle.
			var dir = (Vector3.Back + Vector3.Up * MathX.Lerp( -0.15f, 0.7f, u )).Normalized();
			var length = (5.5f + 3f * MathF.Sin( MathF.PI * u )) * U.In;
			tips.Add( feet[k] - root + dir * length );
		}
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		for ( var k = 0; k < rays - 1; k++ )
		{
			// Between two rays, the edge dips a little: a scallop.
			var a0 = feet[k] - root;
			var a1 = feet[k + 1] - root;
			var dip = (tips[k] + tips[k + 1]) * 0.5f;
			var mid0 = (a0 + a1) * 0.5f;
			dip = mid0 + (dip - mid0) * 0.85f;
			foreach ( var v in new[] { a0, tips[k], dip, a0, dip, a1, a1, dip, tips[k + 1] } ) st.AddVertex( v );
		}
		st.GenerateNormals();
		var membrane = new MeshInstance3D { Name = "Membrane", Mesh = st.Commit(), MaterialOverride = FxMaterials.Mosaic( 2.4f * U.In ) };
		dorsal.AddChild( membrane );
		torso.Renderers.Add( (membrane, Colors.White) );
		var rayColor = new Color( 0.62f, 0.97f, 0.9f );
		var rayMaterial = Shapes.Material( rayColor, "smooth" );
		for ( var k = 0; k < rays; k++ )
		{
			var ray = global::Sculpt.Limb( dorsal, feet[k] - root, tips[k] + (tips[k] - (feet[k] - root)).Normalized() * 0.6f * U.In, 0.4f * U.In, 0.08f * U.In, rayMaterial, "Ray" );
			torso.Renderers.Add( (ray, rayColor) );
		}
	}

	void BackSpikes( Part torso, Node3D model )
	{
		var middle = MiddleLine( torso, model );
		if ( middle.Count < 10 ) return;
		var bottom = middle.Min( p => p.Y );
		var top = middle.Max( p => p.Y );
		var navy = new Color( 0.08f, 0.16f, 0.42f );
		var edge = new Color( 0.3f, 0.75f, 0.8f );
		var material = Shapes.Material( navy, "smooth" );
		var tipMaterial = Shapes.Material( edge, "smooth" );
		const int count = 13;
		for ( var k = 0; k < count; k++ )
		{
			// From a third of the way up (above the tail) to just short of the top of the head (the hat sits there).
			var y = MathX.Lerp( bottom + (top - bottom) * 0.3f, top - (top - bottom) * 0.04f, k / (float)(count - 1) );
			var band = middle.Where( p => MathF.Abs( p.Y - y ) < 1.5f * U.In ).ToList();
			if ( band.Count == 0 ) continue;
			var back = band.MaxBy( p => p.Z );   // his back is +Z (he faces -Z)
			var lean = (Vector3.Back * 0.75f + Vector3.Up * (0.35f + 0.4f * k / count)).Normalized();
			var size = 4.2f + 3f * MathF.Sin( MathF.PI * (k + 0.5f) / count );   // biggest in the middle of the row
			var foot = back - lean * 0.6f * U.In;
			var tip = back + lean * size * U.In;
			var spike = global::Sculpt.Cone( torso.Go, foot, tip, 1.7f * U.In * (0.7f + 0.3f * size / 7.2f), material, 6, "Back Spike" );
			torso.Renderers.Add( (spike, navy) );
			var glint = global::Sculpt.Cone( torso.Go, back + lean * size * 0.62f * U.In, tip + lean * 0.05f * U.In, 0.6f * U.In, tipMaterial, 6, "Spike Tip" );
			torso.Renderers.Add( (glint, edge) );
		}
	}

	MeshInstance3D BeamPiece( string name, Color color )
	{
		var mesh = new MeshInstance3D
		{
			Name = name,
			Mesh = Shapes.Sphere,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
		};
		GetTree().CurrentScene.AddChild( mesh );
		return mesh;
	}

	// ---- The live eye picture ----

	void MakeEyePicture()
	{
		eyePixels = new byte[EyeW * EyeH * 4];
		eyeImage = Image.CreateEmpty( EyeW, EyeH, false, Image.Format.Rgba8 );
		eyeTexture = ImageTexture.CreateFromImage( eyeImage );
		DrawEye( Vector2.Zero, 0f, 0f );
	}

	/// <summary> Paints the eye: white with veins that redden with glow, a pupil looking at look, an upper lid closing with lid. </summary>
	void DrawEye( Vector2 look, float lid, float glow )
	{
		if ( eyeTexture is null ) return;
		var px = look.X * 0.5f;
		var py = look.Y * 0.22f;
		var lidEdge = 1f - 2.1f * lid;
		for ( var y = 0; y < EyeH; y++ )
		{
			var ny = 1f - (y + 0.5f) / EyeH * 2f;
			for ( var x = 0; x < EyeW; x++ )
			{
				var nx = (x + 0.5f) / EyeW * 2f - 1f;
				var rr = MathF.Sqrt( nx * nx + ny * ny );
				var veins = MathX.Clamp( (rr - 0.55f) * 1.6f, 0f, 1f ) * glow * 0.7f;
				var r = 1f;
				var g = 1f - 0.9f * glow - veins * 0.5f;
				var b = 1f - 0.92f * glow - veins * 0.5f;
				var dx = (nx - px) / 0.3f;
				var dy = (ny - py) / 0.5f;
				if ( dx * dx + dy * dy < 1f ) { r = glow < 0.5f ? 0.02f : 0.25f; g = b = glow < 0.5f ? 0.02f : 0f; }
				var hx = (nx - px + 0.1f) / 0.07f;
				var hy = (ny - py - 0.2f) / 0.09f;
				if ( hx * hx + hy * hy < 1f ) { r = g = b = 1f; }
				if ( lid > 0.02f )
				{
					if ( ny > lidEdge ) { r = LidTeal.R; g = LidTeal.G; b = LidTeal.B; }
					else if ( ny > lidEdge - 0.06f ) { r = g = b = 0.02f; }
				}
				var o = (y * EyeW + x) * 4;
				eyePixels[o] = (byte)(MathX.Clamp( r, 0f, 1f ) * 255f);
				eyePixels[o + 1] = (byte)(MathX.Clamp( g, 0f, 1f ) * 255f);
				eyePixels[o + 2] = (byte)(MathX.Clamp( b, 0f, 1f ) * 255f);
				eyePixels[o + 3] = 255;
			}
		}
		eyeImage.SetData( EyeW, EyeH, false, Image.Format.Rgba8, eyePixels );
		eyeTexture.Update( eyeImage );
	}

	// ---- Every frame ----

	public override void _Process( double delta )
	{
		if ( health is null || rig is null ) return;
		var dt = (float)delta;
		if ( IsDead )
		{
			if ( timeSinceDeath > 1f ) QueueFree();
			return;
		}
		if ( !DevFlags.FreezeEnemies ) Think( dt );
		if ( diedLocally || !IsInstanceValid( rig ) ) return;
		if ( State != seenState ) { seenState = State; localStateTime = 0f; }
		rig.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized().Scaled( Vector3.One * Size ), GlobalPosition );
		if ( Has( Torso ) ) CreatureGore.BleedTrail( parts[Torso].Go.GlobalPosition, Goo, health.Fraction, ref bleedTimer );
		UpdateSpawnIn();
		Animate( dt );
		DrawBeam();
		TintParts( dt );
	}

	// Hand poses: curl in degrees for [index, middle, ring, pinky] x [base, mid, tip], then thumb [base, tip].
	static readonly float[] PoseWave = { 20, 14, 10, 16, 12, 8, 22, 14, 10, 28, 16, 12, -18, 10 };
	static readonly float[] PosePinch = { 52, 50, 35, 30, 28, 20, 20, 24, 18, 22, 24, 18, 40, 24 };
	static readonly float[] PoseReach = { 18, 40, 26, 20, 44, 28, 18, 40, 26, 14, 36, 24, 8, 20 };
	static readonly float[] PoseRelaxed = { 18, 24, 14, 22, 28, 16, 26, 30, 18, 30, 32, 20, 10, 12 };
	static readonly float[] PoseSpread = { -6, -4, -2, -2, -2, 0, -4, -2, 0, -10, -6, -2, -25, 0 };

	void CurlHand( Hand hand, float[] pose, float rate, float tremble, float fan, float thumbSplay, float ripple, float dt )
	{
		if ( hand is null || !IsInstanceValid( hand.Root ) ) return;
		var t = (float)Clock.Now;
		for ( var i = 0; i < 14; i++ )
		{
			var finger = i / 3;
			var joint = i % 3;
			var wobble = tremble * MathF.Sin( t * 31f + i * 1.7f + hand.Side * 2f );
			var flow = i < 12 ? ripple * MathF.Sin( t * 2.4f - finger * 0.8f - joint * 0.6f + hand.Side * 1.3f ) : ripple * 0.6f * MathF.Sin( t * 1.9f + hand.Side );
			var k = MathX.Clamp( dt * rate * (1f - 0.22f * (i < 12 ? joint : 1)), 0f, 1f );
			hand.Curl[i] = MathX.Lerp( hand.Curl[i], pose[i] + wobble + flow, k );
		}
		hand.Splay = MathX.Lerp( hand.Splay, thumbSplay, MathX.Clamp( dt * rate, 0f, 1f ) );
		if ( !PoseHands || !PrepareHand( hand ) ) return;

		for ( var f = 0; f < 4; f++ )
		{
			var rot = new Basis[3];
			rot[0] = Ax( SForward, hand.Side * (f - 1.5f) * fan ) * Ax( SLeft, hand.Curl[f * 3] );
			rot[1] = Ax( SLeft, hand.Curl[f * 3 + 1] );
			rot[2] = Ax( SLeft, hand.Curl[f * 3 + 2] );
			ApplyChain( hand, f * 3, 3, rot );
		}
		var flexS = new Basis( SForward, MathX.Rad( -hand.Side * 38f ) ) * SLeft;   // in s&box space
		var thumb = new Basis[2];
		thumb[0] = Ax( SForward, -hand.Side * (hand.Splay - 38f) ) * Ax( flexS, hand.Curl[12] );
		thumb[1] = Ax( flexS, hand.Curl[13] );
		ApplyChain( hand, 12, 2, thumb );
	}

	/// <summary> Bone m's pose is its rest pose carried through the rotations of every joint above it (about each joint's pivot). </summary>
	static void ApplyChain( Hand hand, int first, int count, Basis[] rotS )
	{
		var c = hand.ToSkeleton;
		var ci = c.Inverse();
		var rot = new Basis[count];
		for ( var k = 0; k < count; k++ ) rot[k] = c * rotS[k] * ci;
		for ( var m = 0; m < count; m++ )
		{
			var position = hand.Rest[first + m].Origin;
			var rotation = hand.Rest[first + m].Basis;
			for ( var k = m; k >= 0; k-- )
			{
				var pivot = hand.Rest[first + k].Origin;
				position = pivot + rot[k] * (position - pivot);
				rotation = rot[k] * rotation;
			}
			hand.Skeleton.SetBoneGlobalPose( hand.Bone[first + m], new Transform3D( rotation, position ) );
		}
	}

	/// <summary> Hands are springs: they overshoot a little, trail behind his movement, and settle. Positions in s&box local inches. </summary>
	void MoveHand( int index, Vector3 position, Basis rotation, Vector3 drift, float frame )
	{
		var part = parts[index];
		if ( part is null || part.Gone || !IsInstanceValid( part.Go ) ) return;
		var dt = MathF.Min( frame, 1f / 30f );
		var slot = index == HandL ? 0 : 1;
		var target = U.Sbox( position + drift );
		handVel[slot] += (target - part.Go.Position) * 55f * dt;
		handVel[slot] *= MathF.Exp( -6.5f * dt );
		part.Go.Position += handVel[slot] * dt;
		var q = part.Go.Basis.Orthonormalized().GetRotationQuaternion().SafeSlerp( rotation.GetRotationQuaternion(), MathX.Clamp( dt * 9f, 0f, 1f ) );
		part.Go.Basis = new Basis( q );
	}

	void Animate( float frame )
	{
		var t = (float)Clock.Now;
		var dt = MathF.Max( frame, 0.0001f );
		var st = (float)localStateTime;
		var torso = parts[Torso].Go;

		var torsoAngles = new Angles( MathF.Sin( t * 1.3f ) * 3f, 0, MathF.Sin( t * 0.9f ) * 4f );
		var headAngles = new Angles( 0, MathF.Sin( t * 0.7f ) * 8f, 0 );
		var bob = MathF.Sin( t * 1.8f ) * 3f;
		var spin = 0f;
		var tailWiggle = 1f;
		var eyeGlow = 0f;

		float[] poseL = PoseRelaxed, poseR = PoseRelaxed;
		var tremble = 0f;
		var fan = 4f;
		var ripple = 9f;
		var thumbSplay = 34f;
		var posL = new Vector3( 8f, 36f, 12f + MathF.Sin( t * 1.4f ) * 4f + MathF.Sin( t * 0.7f ) * 2f );
		var posR = new Vector3( 8f, -36f, 14f + MathF.Sin( t * 1.4f + 1.9f ) * 4f + MathF.Sin( t * 0.8f + 1f ) * 2f );
		Basis Sway( float f ) => Ax( SLeft, MathF.Sin( t * 1.3f + f ) * 9f ) * Ax( SUp, MathF.Cos( t * 1.1f + f * 1.4f ) * 4f );
		var rotL = Ax( SForward, -24f + MathF.Sin( t * 1.1f ) * 7f ) * Sway( 0f );
		var rotR = Ax( SForward, 26f + MathF.Sin( t * 1.2f ) * 7f ) * Sway( 2f );

		switch ( State )
		{
			case Mode.Charge:
			{
				torsoAngles = new Angles( -8f - st * 3f, 0, 0 );
				headAngles = new Angles( 10f, 0, 0 );
				bob = MathF.Sin( t * 30f ) * 0.6f * st;
				var charge = MathX.Clamp( st / ChargeTime, 0f, 1f );
				eyeGlow = charge * (0.85f + 0.15f * MathF.Sin( t * 20f ));
				poseL = poseR = PoseReach;
				fan = 22f - 4f * charge;
				ripple = 2f;
				tremble = 1.2f + 3.5f * charge;
				var lift = 34f + MathF.Sin( t * 22f ) * charge * 0.8f;
				var reach = 30f + 14f * charge;
				posL = new Vector3( reach, 24f - 6f * charge, lift );
				posR = new Vector3( reach, -24f + 6f * charge, lift );
				rotL = Ax( SForward, -12f ) * Ax( SLeft, 62f );
				rotR = Ax( SForward, 12f ) * Ax( SLeft, 62f );
				tailWiggle = 0.5f;
				break;
			}
			case Mode.Beam:
			{
				torsoAngles = new Angles( 6f, 0, 0 );
				headAngles = new Angles( 0, 0, 0 );
				bob = MathF.Sin( t * 45f ) * 0.8f;
				eyeGlow = 1f;
				poseL = poseR = PoseSpread;
				tremble = 3.5f;
				fan = 10f;
				ripple = 0f;
				posL = new Vector3( 12f, 34f, 30f );
				posR = new Vector3( 12f, -34f, 30f );
				rotL = Ax( SForward, -26f );
				rotR = Ax( SForward, 26f );
				tailWiggle = 0.3f;
				break;
			}
			case Mode.Cards:
			{
				var hold = st < 0.55f;
				poseL = poseR = hold ? PosePinch : PoseSpread;
				fan = hold ? 0f : 12f;
				ripple = 0f;
				tremble = hold ? 1.5f : 0f;
				posL = hold ? new Vector3( 22f, 9f, 14f ) : new Vector3( 8f, 38f, 22f );
				posR = hold ? new Vector3( 22f, -9f, 14f ) : new Vector3( 8f, -38f, 22f );
				rotL = hold ? Basis.Identity : Ax( SForward, -40f );
				rotR = hold ? Basis.Identity : Ax( SForward, 40f );
				spin = MathX.Clamp( (st - 0.6f) / 1.1f, 0f, 1f ) * 360f;
				tailWiggle = 1.6f;
				break;
			}
			case Mode.Dance:
			{
				var w = t * 7f;
				torsoAngles = new Angles( 90f + MathF.Sin( w ) * 14f, 0, 0 );
				headAngles = new Angles( -70f + MathF.Sin( w - 1.2f ) * 20f, 0, 0 );
				bob = MathF.Max( 0f, MathF.Sin( w ) ) * 8f;
				poseL = poseR = PoseWave;
				ripple = 16f;
				posL = new Vector3( 6f, 36f, 22f + MathF.Sin( w ) * 10f );
				posR = new Vector3( 6f, -36f, 22f + MathF.Sin( w + 2f ) * 10f );
				rotL = Ax( SForward, MathF.Sin( w ) * 30f );
				rotR = Ax( SForward, MathF.Sin( w + 2f ) * 30f );
				tailWiggle = 3f;
				break;
			}
		}

		torso.Position = U.Sbox( 0, 0, bob );
		torso.Basis = Ax( SUp, spin ) * torsoAngles.ToBasis();
		Pose( Head, headAngles, dt );

		var rigMove = (GlobalPosition - lastHandRigPosition) / dt;
		lastHandRigPosition = GlobalPosition;
		var lag = -ToS( GlobalBasis.Orthonormalized().Inverse() * rigMove ) * 0.06f;
		lag = lag.Clamp( new Vector3( -14f, -14f, -14f ), new Vector3( 14f, 14f, 14f ) );
		// The fins stay tucked in behind his cheeks: every pose is shrunk toward the anchor at the neck.
		posL = Tuck( posL, 1f );
		posR = Tuck( posR, -1f );
		lag *= 0.35f;
		MoveHand( HandL, posL, rotL, lag, frame );
		MoveHand( HandR, posR, rotR, lag, frame );
		CurlHand( hands[0], poseL, 10f, tremble, fan, thumbSplay, ripple, frame );
		CurlHand( hands[1], poseR, 10f, tremble, fan, thumbSplay, ripple, frame );
		FlapFins( t, fan, ripple, tremble );
		// The dorsal fin ripples fast, as a seahorse's does when it swims.
		if ( dorsal is not null && IsInstanceValid( dorsal ) )
			dorsal.Basis = new Basis( Vector3.Up, MathF.Sin( t * 9f ) * 0.18f ) * new Basis( Vector3.Right, MathF.Sin( t * 4.5f + 1f ) * 0.06f );

		AnimateTail( t, dt, tailWiggle );
		AnimateEye( t, dt, eyeGlow );
	}

	void AnimateTail( float t, float dt, float wiggle )
	{
		var velocityNow = (GlobalPosition - lastRigPosition) / dt;
		lastRigPosition = GlobalPosition;
		var local = ToS( GlobalBasis.Orthonormalized().Inverse() * velocityNow );
		for ( var i = 0; i < TailCount; i++ )
		{
			var tail = parts[Tail0 + i];
			if ( tail is null || tail.Gone ) break;
			var drag = 1f + i * 0.35f;
			// Curled like a seahorse's: each segment bends further than the one before it.
			var targetPitch = (i == 0 ? 38f : 16f + 8f * i) + MathX.Clamp( local.X * 0.05f, -22f, 22f ) * drag + MathF.Sin( t * 2.2f - i * 0.9f ) * (5f + 4f * i) * wiggle;
			var targetRoll = -14f + MathX.Clamp( -local.Y * 0.05f, -22f, 22f ) * drag + MathF.Cos( t * 1.7f - i * 0.8f ) * (4f + 3f * i) * wiggle;
			tailPitchVel[i] += (targetPitch - tailPitch[i]) * 38f * dt;
			tailPitchVel[i] *= MathF.Exp( -4f * dt );
			tailPitch[i] += tailPitchVel[i] * dt;
			tailRollVel[i] += (targetRoll - tailRoll[i]) * 38f * dt;
			tailRollVel[i] *= MathF.Exp( -4f * dt );
			tailRoll[i] += tailRollVel[i] * dt;
			tail.Go.Basis = Ax( SForward, tailRoll[i] ) * Ax( SLeft, tailPitch[i] );
		}
	}

	Vector2 eyeLook;
	float eyeLid;
	float eyeGlowNow;
	float nextBlinkAt = 2f;
	float blinkStart = -1f;
	float blinkLength = 0.2f;
	float nextEyePaint;

	void AnimateEye( float t, float dt, float glowTarget )
	{
		if ( eyeTexture is null || !Has( Eye ) ) return;
		Vector2 want;
		var target = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		if ( target is not null )
		{
			var dir = (target.WorldPosition + Vector3.Up * 50f * U.In - parts[Eye].Go.GlobalPosition).SafeNormal();
			var localDir = ToS( parts[Head].Go.GlobalBasis.Orthonormalized().Inverse() * dir ) * U.In;
			var yaw = MathX.Deg( MathF.Atan2( localDir.Y, MathF.Max( 0.05f, localDir.X ) ) );
			var pitch = MathX.Deg( MathF.Atan2( localDir.Z, MathF.Sqrt( localDir.X * localDir.X + localDir.Y * localDir.Y ) ) );
			want = new Vector2( MathX.Clamp( yaw / 26f, -1f, 1f ), MathX.Clamp( pitch / 60f, -0.8f, 0.8f ) );
		}
		else want = new Vector2( MathF.Sin( t * 0.8f ), MathF.Cos( t * 0.6f ) * 0.4f );
		eyeLook = eyeLook.Lerp( want, MathX.Clamp( dt * 14f, 0f, 1f ) );

		if ( t >= nextBlinkAt )
		{
			blinkStart = t;
			blinkLength = Rng.Float() < 0.2f ? Rng.Float( 0.35f, 0.55f ) : 0.18f;
			nextBlinkAt = t + (Rng.Float() < 0.2f ? 0.4f : Rng.Float( 1.8f, 5f ));
		}
		var k = blinkStart < 0f ? 2f : (t - blinkStart) / blinkLength;
		var lid = k is < 0f or > 1f ? 0f : MathF.Sin( k * MathF.PI );
		if ( State is Mode.Beam ) lid = MathF.Max( lid, 0.18f );
		eyeLid = lid;
		eyeGlowNow = MathX.Lerp( eyeGlowNow, glowTarget, MathX.Clamp( dt * 10f, 0f, 1f ) );

		if ( t < nextEyePaint ) return;
		nextEyePaint = t + 0.025f;
		DrawEye( eyeLook, eyeLid, eyeGlowNow );
	}

	void Pose( int index, Angles angles, float dt )
	{
		var part = parts[index];
		if ( part is null || part.Gone ) return;
		var q = part.Go.Basis.Orthonormalized().GetRotationQuaternion().SafeSlerp( angles.ToBasis().GetRotationQuaternion(), MathX.Clamp( dt * 12f, 0f, 1f ) );
		part.Go.Basis = new Basis( q );
	}

	// Nothing is tinted until he has finished appearing.
	protected override bool TintReady => spawnDone;

	protected override void Tinted( Part part, MeshInstance3D renderer, Color tint )
	{
		// The eye glows as it charges (after the tint, which would set its glow to its colour).
		if ( part.Index == Eye && tint == Colors.White && renderer.MaterialOverride is StandardMaterial3D em )
		{
			em.AlbedoColor = Colors.White;
			em.Emission = new Color( 1f, 0.2f, 0.1f ) * eyeGlowNow * 1.2f;
		}
	}

	Vector3 EyePosition => Has( Eye ) ? parts[Eye].Go.GlobalPosition : GlobalPosition + Vector3.Up * 40f * U.In;

	void DrawBeam()
	{
		var firing = State == Mode.Beam && Has( Eye );
		var aiming = State == Mode.Charge && Has( Eye );
		beam.Visible = firing;
		beamCore.Visible = firing;
		aimLine.Visible = aiming;
		if ( !firing && !aiming ) return;
		var from = EyePosition;
		var to = BeamEnd;
		var flicker = 1f + 0.12f * MathF.Sin( (float)Clock.Now * 60f );
		if ( firing )
		{
			Stretch( beam, from, to, BeamWidth * flicker );
			Stretch( beamCore, from, to, BeamWidth * 0.4f );
			if ( scorchTime > 0.07f )
			{
				scorchTime = 0f;
				var dir = (to - from).SafeNormal();
				if ( gore.TraceWorld( to - dir * 20f * U.In, to + dir * 40f * U.In ) is { } hit )
					gore.GooSplat( hit.Position, hit.Normal, Rng.Float( 0.5f, 0.9f ), new Color( 0.08f, 0.02f, 0.06f ) );
				gore.GooBurst( to - dir * 6f * U.In, -dir, BeamColor * 2f, 2, 260f, 0.6f );
			}
		}
		else Stretch( aimLine, from, to, 1.2f + (float)localStateTime * 1.2f );
	}

	static void Stretch( MeshInstance3D mesh, Vector3 from, Vector3 to, float width )
	{
		var length = MathF.Max( 1f, from.DistanceTo( to ) / U.In );
		mesh.GlobalTransform = new Transform3D( Shapes.LookAt( to - from ) * Basis.FromScale( Shapes.Ellipsoid( length * 0.5f, width * 0.5f, width * 0.5f ) ), (from + to) * 0.5f );
	}

	// ---- Brain ----

	/// <summary> Dev menu: jump straight into a move. </summary>
	public void ForceState( Mode mode )
	{
		if ( mode == Mode.Charge || mode == Mode.Beam )
		{
			var target = PlayerSurvivor.FindNearestAlive( GlobalPosition );
			beamDirection = target is not null ? (target.WorldPosition + Vector3.Up * 40f * U.In - EyePosition).SafeNormal() : -GlobalBasis.Z;
			UpdateBeamEnd();
		}
		if ( mode == Mode.Cards ) cardRingsThrown = 0;
		SetState( mode );
	}

	void SetState( Mode mode )
	{
		State = mode;
		stateTime = 0f;
		if ( mode == Mode.Charge ) Sfx.Play( "beam_charge", GlobalPosition, 0.9f );
		else if ( mode == Mode.Beam ) Sfx.Play( "beam_fire", GlobalPosition, 1f );
	}

	void Think( float dt )
	{
		var target = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var hasEye = Has( Eye );
		var hasCase = Has( HandL ) || Has( HandR );

		switch ( State )
		{
			case Mode.Float:
			{
				Hover( target, dt, hasEye || hasCase ? PreferredDistance : 60f );
				if ( !hasEye && !hasCase && target is not null && ramTime > 1f && (target.WorldPosition + Vector3.Up * 40f * U.In).DistanceTo( GlobalPosition ) < 90f * Size * U.In )
				{
					ramTime = 0f;
					Hurt( target, RamDamage );
				}
				if ( target is not null && nextAttack <= 0f ) ChooseAttack( target, hasEye, hasCase );
				break;
			}
			case Mode.Charge:
				velocity *= MathF.Exp( -6f * dt );
				Move( dt );
				AimBeam( target, dt, BeamTrackSpeed * 0.5f );
				if ( stateTime > ChargeTime ) { SetState( Mode.Beam ); beamTick = 0f; }
				break;
			case Mode.Beam:
				velocity *= MathF.Exp( -6f * dt );
				Move( dt );
				AimBeam( target, dt, BeamTrackSpeed );
				if ( beamTick >= 0.1f )
				{
					beamTick = 0f;
					BeamDamage( BeamDamagePerSecond * 0.1f );
				}
				if ( stateTime > BeamTime || !hasEye ) EndAttack();
				break;
			case Mode.Cards:
			{
				velocity *= MathF.Exp( -6f * dt );
				Move( dt );
				// Missiles out of his hand, one after another, fanned round him: they climb, then dive on the player.
				var due = (int)MathF.Floor( ((float)stateTime - 0.5f) / 0.18f ) + 1;
				while ( hasCase && cardRingsThrown < Math.Min( due, missilesThisAttack ) )
				{
					var origin = parts[Has( HandR ) ? HandR : HandL].Go.GlobalPosition;
					var yaw = cardRingsThrown * MathF.Tau / missilesThisAttack + Rng.Float( -0.3f, 0.3f );
					var outward = new Vector3( MathF.Cos( yaw ), 0f, MathF.Sin( yaw ) );
					HomingMissile.Launch( GetTree().CurrentScene, origin + outward * 0.4f + Vector3.Up * 0.3f, outward, this );
					cardRingsThrown++;
				}
				if ( stateTime > 0.5f + missilesThisAttack * 0.18f + 0.4f || !hasCase ) EndAttack();
				break;
			}
			case Mode.Dance:
			{
				var floor = Ground( GlobalPosition ) + 18f * Size * U.In;
				GlobalPosition = GlobalPosition.Lerp( GlobalPosition.WithY( floor ), MathX.Clamp( dt * 4f, 0f, 1f ) );
				velocity = Vector3.Zero;
				if ( stateTime > DanceTime ) EndAttack();
				break;
			}
		}

		var face = State == Mode.Beam ? beamDirection.Flat() : target is not null ? (target.WorldPosition - GlobalPosition).Flat() : Vector3.Zero;
		if ( State != Mode.Dance && !face.IsNearlyZero() )
		{
			var q = GlobalBasis.Orthonormalized().GetRotationQuaternion().SafeSlerp( Shapes.LookAt( face ).GetRotationQuaternion(), MathX.Clamp( dt * 4f, 0f, 1f ) );
			GlobalBasis = new Basis( q );
		}
	}

	void ChooseAttack( PlayerSurvivor target, bool hasEye, bool hasCase )
	{
		var options = new List<Mode>();
		if ( hasEye && CanSee( target ) ) options.Add( Mode.Beam );
		if ( hasCase && HomingMissile.All.Count < 9 ) options.Add( Mode.Cards );   // not while the last lot is still up
		if ( Rng.Float() < DanceChance || (options.Count == 0 && Rng.Float() < 0.3f) )
		{
			Say( "*breaks out the worm*" );
			SetState( Mode.Dance );
			return;
		}
		if ( options.Count == 0 ) { nextAttack = 1f; return; }
		var pick = options[Rng.Int( 0, options.Count - 1 )];
		if ( pick == Mode.Beam )
		{
			Say( "I SEEE YOUUU" );
			var toTarget = (target.WorldPosition + Vector3.Up * 40f * U.In - EyePosition).SafeNormal();
			beamDirection = (new Basis( Vector3.Up, MathX.Rad( Rng.Float( -25f, 25f ) ) ) * toTarget).Normalized();
			UpdateBeamEnd();
			SetState( Mode.Charge );
		}
		else
		{
			Say( "FETCH!" );
			cardRingsThrown = 0;
			missilesThisAttack = Rng.Int( MissilesMin, MissilesMax );
			SetState( Mode.Cards );
		}
	}

	void EndAttack()
	{
		SetState( Mode.Float );
		nextAttack = Rng.Float( AttackGap.X, AttackGap.Y );
	}

	bool CanSee( PlayerSurvivor target ) => !Trace.Ray( this, EyePosition, target.WorldPosition + Vector3.Up * 50f * U.In, Layers.World ).Hit;

	void AimBeam( PlayerSurvivor target, float dt, float degreesPerSecond )
	{
		if ( target is not null )
		{
			var wanted = (target.WorldPosition + Vector3.Up * 40f * U.In - EyePosition).SafeNormal();
			var angle = MathX.Deg( MathF.Acos( MathX.Clamp( beamDirection.Dot( wanted ), -1f, 1f ) ) );
			if ( angle > 0.01f )
			{
				var step = MathF.Min( 1f, degreesPerSecond * dt / angle );
				beamDirection = beamDirection.Slerp( wanted, step ).Normalized();
			}
		}
		UpdateBeamEnd();
	}

	void UpdateBeamEnd()
	{
		var from = EyePosition;
		var to = from + beamDirection * BeamRange * U.In;
		var tr = Trace.Ray( this, from, to, Layers.World );
		BeamEnd = tr.Hit ? tr.HitPosition : to;
	}

	/// <summary>
	/// Anyone the beam passes through (head to toe) gets burned, unless they are behind something: the part of them
	/// the beam is close to must be in plain sight of his eye (no wall, rock, cover or ground in between).
	/// </summary>
	void BeamDamage( float amount )
	{
		var from = EyePosition;
		var to = BeamEnd;
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive ) continue;
			var feet = player.WorldPosition;
			var exclude = player.Controller is { } c ? new Godot.Collections.Array<Rid> { c.GetRid() } : null;
			for ( var h = 10f; h <= 70f; h += 15f )
			{
				var point = feet + Vector3.Up * h * U.In;
				if ( DistanceToSegment( point, from, to ) >= (BeamWidth + 14f) * U.In ) continue;
				if ( Trace.Ray( this, from, point, Layers.World, exclude ).Hit ) continue;   // in cover
				Hurt( player, amount );
				break;
			}
		}
	}

	static float DistanceToSegment( Vector3 point, Vector3 a, Vector3 b )
	{
		var ab = b - a;
		var t = ab.LengthSquared() > 1e-6f ? MathX.Clamp( (point - a).Dot( ab ) / ab.LengthSquared(), 0f, 1f ) : 0f;
		return point.DistanceTo( a + ab * t );
	}

	void Hurt( PlayerSurvivor player, float amount )
	{
		player.Health?.OnDamage( new DamageInfo { Damage = amount, Attacker = this, Position = player.WorldPosition + Vector3.Up * 50f * U.In, Origin = GlobalPosition } );
	}

	void Hover( PlayerSurvivor target, float dt, float distance )
	{
		var goal = GlobalPosition;
		if ( target is not null )
		{
			var flat = (GlobalPosition - target.WorldPosition).Flat();
			if ( flat.IsNearlyZero() ) flat = Vector3.Forward;
			var around = flat.Normalized().Rotated( Vector3.Up, MathX.Rad( orbitSign * 12f * dt * 10f ) );
			goal = target.WorldPosition + (around * distance + Vector3.Up * HoverHeight) * U.In;
		}
		var wanted = ((goal - GlobalPosition) / U.In).ClampLength( MoveSpeed );
		velocity = velocity.Lerp( wanted, MathX.Clamp( dt * 2f, 0f, 1f ) );
		Move( dt );
	}

	void Move( float dt )
	{
		var position = GlobalPosition;
		var step = velocity * U.In * dt;
		if ( !step.IsNearlyZero() )
		{
			var tr = Trace.Sphere( this, 30f * Size * U.In, position, position + step + step.Normalized() * 20f * U.In, Layers.World );
			if ( tr.Hit && !tr.StartedSolid )
			{
				velocity -= tr.Normal * velocity.Dot( tr.Normal ) * 1.2f;
				step = velocity * U.In * dt;
			}
		}
		var next = position + step;
		var floor = Ground( next ) + 70f * Size * U.In;
		if ( next.Y < floor ) next = next.WithY( MathX.Lerp( next.Y, floor, MathX.Clamp( dt * 5f, 0f, 1f ) ) );
		GlobalPosition = next;
	}

	float Ground( Vector3 at )
	{
		var tr = Trace.Ray( this, at + Vector3.Up * 10f * U.In, at + Vector3.Down * 1000f * U.In, Layers.World );
		return tr.Hit ? tr.HitPosition.Y : at.Y - 150f * U.In;
	}

	// ---- Speech and cards ----

	void Say( string line )
	{
		Line = line;
		TimeSinceLine = 0f;
	}

	void CardConfetti( Vector3 at )
	{
		for ( var i = 0; i < 14; i++ )
		{
			var dir = (Rng.OnSphere() + Vector3.Up * 0.8f).Normalized();
			Card.Spawn( at, dir * Rng.Float( 150f, 350f ), (CardKind)Rng.Int( 0, 3 ), 0f, this, confetti: true );
		}
	}

	// ---- Getting shot ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead ) return;
		if ( index < 0 || index >= parts.Length || !Has( index ) ) index = Torso;
		NoteShot( direction, position, origin );
		if ( State == Mode.Dance ) damage *= DanceDamageScale;
		var part = parts[index];
		var crit = index is Head or Eye;
		var multiplier = index switch
		{
			Head => 1f,
			Eye => EyeMultiplier,
			Torso => TorsoMultiplier,
			Hat => 0.2f,
			_ => LimbMultiplier,
		};
		var cut = false;
		if ( part.MaxHealth > 0f && index != Torso )
		{
			part.Health -= damage;
			cut = part.Health <= 0f;
		}
		var hurt = index is Head or Torso
			? 1f - MathX.Clamp( health.Current / MathF.Max( 1f, health.MaxHealth ), 0f, 1f )
			: 1f - MathX.Clamp( part.Health / MathF.Max( 1f, part.MaxHealth ), 0f, 1f );
		if ( cut )
		{
			GoneMask |= SubtreeMask( index );
			CutLocal( index, direction );
		}
		else Hit( index, position, direction, Rng.Float() < 0.35f, hurt );

		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * multiplier, Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, crit, scoreHit );
	}

	/// <summary> Rainbow goo: he's magical. </summary>
	static Color Goo => Shapes.Hsv( Rng.Float(), 0.85f, 1f );

	void Hit( int index, Vector3 position, Vector3 direction, bool chip, float hurt )
	{
		var part = parts[index];
		if ( part is null ) return;
		part.Flash = 0f;
		part.Hurt = MathF.Max( part.Hurt, hurt * 0.8f );
		var goo = Goo;
		if ( !part.Gone && part.Renderers.Count > 0 )
		{
			CreatureGore.AddWound( part.Go, position, direction, part.Renderers[0].Tint, goo, index is Torso or Head ? 4f : 2.5f );
			if ( index != Eye ) CreatureGore.Jiggle( part.Go, index is Head ? 0.8f : 0.6f );
		}
		gore.GooBurst( position, -direction, goo, 7, 240f, 1f );
		gore.GooSpray( position, direction, 2, goo, 200f, 0.5f, 1f );
		if ( chip && index is Torso or Head ) gore.ThrowBlob( position, -direction, index == Head ? Skin : Tux, goo, 3.5f * Size );
	}

	/// <summary> A piece comes off with everything hanging from it. </summary>
	void CutLocal( int index, Vector3 direction )
	{
		var part = parts[index];
		if ( part is null || part.Gone ) return;
		MarkGone( index );
		var at = part.Go.GlobalPosition;
		AddStumps( part, index == Eye ? 0.5f : 1f );
		for ( var k = 0; k < 3; k++ ) gore.GooBurst( at, direction, Goo, 6, 300f, 1.2f );
		gore.GooSpray( at, direction, 4, Goo, 240f, 0.6f, 1.3f );
		switch ( index )
		{
			case Eye:
				gore.GooBurst( at, direction, Glove, 12, 260f, 1.2f );
				gore.GooBurst( at, direction, Iris, 8, 260f, 1f );
				part.Go.QueueFree();
				Say( "MY EYEEE" );
				return;
			case Hat:
				Detach( part, direction * 80f + Vector3.Up * 260f, spin: 25f );
				return;
		}
		if ( index is HandL or HandR ) CardConfetti( at );
		var piece = Detach( part, direction * Rng.Float( 80f, 180f ) + Vector3.Up * 80f );
		CreatureGore.Twitch( piece, Rng.Float( 2f, 3.5f ), 1.2f );
	}

	void AddStumps( Part part, float scale )
	{
		if ( part.Index == Hat ) return;
		var parent = part.Parent >= 0 ? parts[part.Parent] : null;
		var joint = part.Go.GlobalPosition;
		var outward = parent is not null ? (joint - parent.Go.GlobalPosition).SafeNormal() : Vector3.Up;
		var radius = (part.Index is HandL or HandR ? 5f : 7f) * Size * scale;
		var skin = part.Renderers.Count > 0 ? part.Renderers[0].Tint : Tux;
		if ( parent is not null && !parent.Gone ) CreatureGore.AddStump( parent.Go, joint, outward, radius, skin, Goo );
		if ( part.Index != Eye ) CreatureGore.AddStump( part.Go, joint, -outward, radius, skin, Goo, 2f );
	}

	Node3D Detach( Part part, Vector3 push, float spin = 10f ) => Detach( part, push, spin, 8f, 40f, 6f );

	// ---- Death ----

	protected override void OnDied()
	{
		timeSinceDeath = 0f;
		RaiseKilled();
		DieLocal( lastShotDirection, lastShotPointBlank );
	}

	/// <summary> The head bursts in rainbow goo, cards everywhere, and the rest of him falls apart. </summary>
	void DieLocal( Vector3 direction, bool overkill )
	{
		if ( diedLocally ) return;
		diedLocally = true;
		timeSinceDeath = 0f;
		if ( rig is null || !IsInstanceValid( rig ) ) return;
		foreach ( var b in new[] { beam, beamCore, aimLine } ) if ( IsInstanceValid( b ) ) b.QueueFree();
		var headAt0 = parts[Head].Go.GlobalPosition;
		if ( overkill ) CreatureGore.Overkill( headAt0, direction, Skin, Goo, 1.6f * Size );
		var headAt = headAt0 + Vector3.Up * 18f * Size * U.In;
		for ( var k = 0; k < 6; k++ ) gore.GooBurst( headAt, direction + Vector3.Up, Goo, 10, 380f, 1.6f );
		gore.GooSpray( headAt, Vector3.Down, 8, Goo, 300f, 0.8f, 1.8f );
		for ( var b = 0; b < 4; b++ ) gore.ThrowBlob( headAt, direction, Skin, Goo, 5f * Size );
		CardConfetti( headAt );
		foreach ( var index in new[] { Hat, HandL, HandR, Tail0 } )
		{
			if ( !Has( index ) ) continue;
			var part = parts[index];
			MarkGone( index );
			Detach( part, (direction + Rng.OnSphere() + Vector3.Up).SafeNormal() * Rng.Float( 150f, 300f ), spin: 20f );
		}
		if ( Has( Head ) ) { MarkGone( Head ); parts[Head].Go.QueueFree(); }
		if ( Has( Torso ) ) { MarkGone( Torso ); Detach( parts[Torso], direction * 100f ); }
		rig.QueueFree();
	}
}
