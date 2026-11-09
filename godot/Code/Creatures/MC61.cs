using Godot;

/// <summary>
/// Port of the s&box MC61: a floating ghost magician in a tux and top hat, with one big living eye and two detached
/// white-gloved hands. The boss. Eye beam (charge, then a slow sweeping beam), "Pick any card" rings of cursed cards,
/// and the worm dance (takes extra damage). Hat, hands and tail pieces can be shot off; the eye is a crit spot and
/// can be popped; the head is lethal. The eye is a live painted picture; the hands are skinned and curled by code.
/// Numbers are the s&box ones (inches). Surface shaders and the hologram spawn-in come with step 7c.
/// </summary>
public partial class MC61 : Node3D, ICreature, MC61Marker
{
	public float Size = 1.35f;
	public float PreferredDistance = 600f;
	public float HoverHeight = 110f;
	public float MoveSpeed = 230f;
	public float ChargeTime = 1.6f;
	public float BeamTime = 2.4f;
	public float BeamDamagePerSecond = 45f;
	public float BeamWidth = 16f;
	public float BeamTrackSpeed = 40f;
	public float BeamRange = 3000f;
	public int CardsPerRing = 22;
	public int CardRings = 2;
	public float CardSpeed = 520f;
	public float CardDamage = 6f;
	public Vector2 AttackGap = new( 2f, 3.5f );
	public float DanceChance = 0.15f;
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
	public float HandScale = 1.2f;
	public float EyeHealth = 350f;
	public enum Surface { Plain, RimGlow, Sheen }
	/// <summary> How his ghostly body (and tail) is shaded: plain, a blue rim glow with drifting mist, or a rainbow sheen. </summary>
	public Surface Look = Surface.RimGlow;
	/// <summary> He scans in from the floor up as a hologram for this long when he appears (0 = off). </summary>
	public float SpawnInTime = 1.8f;
	/// <summary> Debug: false leaves the hand rigs in their rest pose. </summary>
	public static bool PoseHands = true;

	public enum Mode { Float, Charge, Beam, Cards, Dance }
	public Mode State { get; private set; }
	public Vector3 BeamEnd { get; private set; }
	public int GoneMask;

	public static readonly List<MC61> All = new();
	public bool IsDead => health?.IsDead ?? false;
	public Health Health => health;
	public float HealthFraction => health?.Fraction ?? 0f;
	public string Line { get; private set; } = "";
	public TimeSince TimeSinceLine { get; private set; } = 999f;
	public event Action<MC61> Killed;

	const int Torso = 0, Head = 1, Eye = 2, Hat = 3, HandL = 4, HandR = 5, Tail0 = 6, TailCount = 4;
	const int PartCount = 10;

	static readonly Color Tux = new( 0.06f, 0.06f, 0.09f );
	static readonly Color GhostBody = new( 0.7f, 0.78f, 0.93f );
	static readonly Color Skin = new( 0.42f, 0.47f, 0.4f );
	static readonly Color Glove = new( 0.97f, 0.97f, 1f );
	static readonly Color Iris = new( 1f, 0.12f, 0.08f );
	static readonly Color BeamColor = new( 1f, 0.22f, 0.12f );

	sealed class Part
	{
		public int Index;
		public int Parent = -1;
		public CreaturePart Go;
		public float Health;
		public float MaxHealth;
		public bool Gone;
		public float Hurt;
		public TimeSince Flash = 99f;
		public readonly List<(MeshInstance3D Renderer, Color Tint)> Renderers = new();
		public Vector3 LastPosition;
		public Vector3 Velocity;
	}

	readonly Part[] parts = new Part[PartCount];
	Node3D rig;
	MeshInstance3D beam, beamCore, aimLine;
	Health health;
	GoreSystem gore;

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
	bool diedLocally;
	TimeSince timeSinceDeath;
	Vector3 lastShotDirection = Vector3.Forward;
	TimeSince bleedTimer;
	bool lastShotPointBlank;

	// Layout numbers from Tools/blender/mc61_ghost.py (s&box local, inches).
	static readonly Vector3 HeadPivot = new( 0, 0, 38f );
	static readonly Vector3 EyeFromHead = new( 20.68f, 0, 0 );
	static readonly Vector3 HatFromHead = new( -2.13f, 11.63f, 17.92f );
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
		parent.AddChild( boss );
		boss.GlobalPosition = position;
		var h = new Health();
		h.ResetTo( maxHealth );
		boss.AddChild( h );
		return boss;
	}

	public override void _Ready()
	{
		All.Add( this );
		CallDeferred( MethodName.Setup );
	}

	public override void _ExitTree()
	{
		All.Remove( this );
		if ( rig is not null && IsInstanceValid( rig ) ) rig.QueueFree();
		foreach ( var b in new[] { beam, beamCore, aimLine } ) if ( b is not null && IsInstanceValid( b ) ) b.QueueFree();
	}

	void Setup()
	{
		health = GetNodeOrNull<Health>( "Health" );
		if ( health is null ) { health = new Health(); health.ResetTo( 2000f ); AddChild( health ); }
		gore = GoreSystem.Get();
		nextAttack = 2.5f;
		orbitSign = Rng.Float() < 0.5f ? -1f : 1f;
		seenState = State;
		health.Died += OnDied;
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
		Sculpt( torso, "body", GhostBody, ghost: true );
		Sculpt( torso, "jacket", Tux );
		Sculpt( torso, "bow", Tux );
		Sculpt( torso, "front", Tux );
		Trigger( torso, new Vector3( 0, 0, -16 ), new Vector3( 0, 0, 24 ), 19f );

		var head = AddPart( Head, Torso, torso.Go, "Head", HeadPivot, Basis.Identity, 0f );
		Trigger( head, new Vector3( 0, 0, -2 ), new Vector3( 0, 0, 5 ), 19f );

		var eye = AddPart( Eye, Head, head.Go, "Eye", EyeFromHead, Basis.Identity, EyeHealth );
		MakeEyePicture();
		var eyeMaterial = new StandardMaterial3D { AlbedoTexture = eyeTexture, AlbedoColor = Colors.White, Roughness = 0.25f, EmissionEnabled = true, Emission = Colors.Black };
		Sculpt( eye, "eyeball", Colors.White, material: eyeMaterial );
		Sculpt( eye, "lids", Colors.Black );
		Trigger( eye, new Vector3( 0, -5, 0 ), new Vector3( 0, 5, 0 ), 9f );

		var hat = AddPart( Hat, Head, head.Go, "Hat", HatFromHead, Ax( SForward, HatTilt ) * new Basis( HatRotation ), HatHealth );
		Sculpt( hat, "hat", Tux );
		Trigger( hat, new Vector3( 0, 0, 2 ), new Vector3( 0, 0, 10 ), 8.5f );

		BuildHand( HandL, 1f );
		BuildHand( HandR, -1f );

		var parentIndex = Torso;
		Node3D parentGo = torso.Go;
		for ( var i = 0; i < TailCount; i++ )
		{
			var tail = AddPart( Tail0 + i, parentIndex, parentGo, $"Tail {i}", i == 0 ? TailRoot : new Vector3( 0, 0, -TailSegment ), Basis.Identity, TailHealth );
			Sculpt( tail, $"tail{i}", GhostBody, ghost: true );
			Trigger( tail, Vector3.Zero, new Vector3( 0, 0, -TailSegment ), TailRadius[i] * 0.9f );
			parentIndex = Tail0 + i;
			parentGo = tail.Go;
		}

		beam = BeamPiece( "Beam", BeamColor * 2.5f );
		beamCore = BeamPiece( "Beam Core", Colors.White * 3f );
		aimLine = BeamPiece( "Aim Line", new Color( 1f, 0.1f, 0.3f ) * 2f );
	}

	void BuildHand( int index, float side )
	{
		var part = AddPart( index, Torso, parts[Torso].Go, side > 0 ? "Hand L" : "Hand R", new Vector3( 6f, side * 36f, 18f ), Basis.Identity, HandHealth );
		Trigger( part, Vector3.Zero, new Vector3( 0, 0, 13 * HandScale ), 8f * HandScale );
		var holder = new Node3D { Name = "Hand Skin", Scale = Vector3.One * HandScale };
		part.Go.AddChild( holder );
		var scene = GD.Load<PackedScene>( $"res://assets/models/creatures/mc61g/{(side > 0 ? "hand_l" : "hand_r")}.fbx" );
		var model = scene.Instantiate<Node3D>();
		// The hand rig imports already facing the s&box way round (its armature carries the axis turn).
		model.Basis = Basis.FromScale( Vector3.One * 2.54f );
		holder.AddChild( model );
		foreach ( var mesh in model.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			mesh.MaterialOverride = Shapes.Material( Glove, "smooth" );
			part.Renderers.Add( (mesh, Glove) );
		}
		hands[index == HandL ? 0 : 1] = new Hand { Side = side, Root = part.Go, Skeleton = model.FindChildren( "*", "Skeleton3D", true, false ).OfType<Skeleton3D>().FirstOrDefault() };
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

	void Sculpt( Part part, string model, Color color, bool ghost = false, StandardMaterial3D material = null )
	{
		if ( !sculpts.TryGetValue( model, out var scene ) )
			sculpts[model] = scene = GD.Load<PackedScene>( $"res://assets/models/creatures/mc61g/{model}.fbx" );
		var node = scene.Instantiate<Node3D>();
		node.Basis = new Basis( Vector3.Up, MathF.PI * 0.5f ) * Basis.FromScale( Vector3.One * 2.54f );
		part.Go.AddChild( node );
		foreach ( var mesh in node.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			// His ghostly body and tail wear the chosen surface look (RimGlow: fx_ghost).
			mesh.MaterialOverride = ghost ? LookMaterial( color ) : material ?? Shapes.Material( color, "smooth" );
			if ( ghost ) styleRenderers.Add( mesh );
			part.Renderers.Add( (mesh, color) );
		}
	}

	Part AddPart( int index, int parent, Node3D parentGo, string name, Vector3 sboxPosition, Basis rotation, float hp )
	{
		var go = new CreaturePart { Name = $"MC61 {name}", Creature = this, Index = index, CollisionLayer = Layers.Creature, CollisionMask = 0 };
		parentGo.AddChild( go );
		go.Transform = new Transform3D( rotation, U.Sbox( sboxPosition ) );
		var part = new Part { Index = index, Parent = parent, Go = go, Health = hp, MaxHealth = hp };
		parts[index] = part;
		return part;
	}

	void Trigger( Part part, Vector3 from, Vector3 to, float radius )
	{
		var a = U.Sbox( from );
		var b = U.Sbox( to );
		var axis = b - a;
		var length = axis.Length();
		var shape = new CapsuleShape3D { Radius = radius * U.In, Height = length + radius * 2f * U.In };
		var basis = length > 1e-5f ? new Basis( new Quaternion( Vector3.Up, axis / length ) ) : Basis.Identity;
		part.Go.AddChild( new CollisionShape3D { Shape = shape, Transform = new Transform3D( basis, (a + b) * 0.5f ) } );
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
					if ( ny > lidEdge ) { r = GhostBody.R; g = GhostBody.G; b = GhostBody.B; }
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

	bool Has( int index ) => parts[index] is { Gone: false };

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
		Tint( dt );
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
		var q = part.Go.Basis.Orthonormalized().GetRotationQuaternion().Slerp( rotation.GetRotationQuaternion(), MathX.Clamp( dt * 9f, 0f, 1f ) );
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
		MoveHand( HandL, posL, rotL, lag, frame );
		MoveHand( HandR, posR, rotR, lag, frame );
		CurlHand( hands[0], poseL, 10f, tremble, fan, thumbSplay, ripple, frame );
		CurlHand( hands[1], poseR, 10f, tremble, fan, thumbSplay, ripple, frame );

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
			var targetPitch = (i == 0 ? 42f : 12f) + MathX.Clamp( local.X * 0.05f, -22f, 22f ) * drag + MathF.Sin( t * 2.2f - i * 0.9f ) * (5f + 4f * i) * wiggle;
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
		var q = part.Go.Basis.Orthonormalized().GetRotationQuaternion().Slerp( angles.ToBasis().GetRotationQuaternion(), MathX.Clamp( dt * 12f, 0f, 1f ) );
		part.Go.Basis = new Basis( q );
	}

	void Tint( float dt )
	{
		foreach ( var part in parts )
		{
			if ( part is null || part.Gone ) continue;
			var flash = MathX.Clamp( 1f - part.Flash / 0.1f, 0f, 1f );
			foreach ( var (renderer, tint) in part.Renderers )
			{
				if ( !IsInstanceValid( renderer ) || !spawnDone ) continue;
				var color = tint.Lerp( tint * 0.5f, part.Hurt ) with { A = 1f };
				Shapes.Tint( renderer, color.Lerp( Colors.White * 3f, flash ) );
				// The eye glows as it charges (after the tint, which would set its glow to its colour).
				if ( part.Index == Eye && tint == Colors.White && renderer.MaterialOverride is StandardMaterial3D em )
				{
					em.AlbedoColor = Colors.White;
					em.Emission = new Color( 1f, 0.2f, 0.1f ) * eyeGlowNow * 1.2f;
				}
			}
			var position = part.Go.GlobalPosition;
			part.Velocity = (position - part.LastPosition) / MathF.Max( dt, 0.0001f );
			part.LastPosition = position;
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
				var due = (int)MathF.Floor( ((float)stateTime - 0.6f) / 0.35f ) + 1;
				while ( hasCase && cardRingsThrown < Math.Min( due, CardRings ) )
				{
					var origin = parts[Has( HandR ) ? HandR : HandL].Go.GlobalPosition;
					var slope = 0f;
					if ( target is not null )
					{
						var toTarget = target.WorldPosition + Vector3.Up * (50f - cardRingsThrown * 25f) * U.In - origin;
						slope = toTarget.Y / MathF.Max( 100f * U.In, toTarget.Flat().Length() );
					}
					ThrowCards( origin, cardRingsThrown * 360f / CardsPerRing * 0.5f, slope );
					cardRingsThrown++;
				}
				if ( stateTime > 1.9f || !hasCase ) EndAttack();
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
			var q = GlobalBasis.Orthonormalized().GetRotationQuaternion().Slerp( Shapes.LookAt( face ).GetRotationQuaternion(), MathX.Clamp( dt * 4f, 0f, 1f ) );
			GlobalBasis = new Basis( q );
		}
	}

	void ChooseAttack( PlayerSurvivor target, bool hasEye, bool hasCase )
	{
		var options = new List<Mode>();
		if ( hasEye && CanSee( target ) ) options.Add( Mode.Beam );
		if ( hasCase ) options.Add( Mode.Cards );
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
			Say( "Pick any card!" );
			cardRingsThrown = 0;
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

	/// <summary> Anyone the beam passes through (head to toe) gets burned. </summary>
	void BeamDamage( float amount )
	{
		var from = EyePosition;
		var to = BeamEnd;
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive ) continue;
			var feet = player.WorldPosition;
			var closest = float.MaxValue;
			for ( var h = 10f; h <= 70f; h += 15f )
				closest = MathF.Min( closest, DistanceToSegment( feet + Vector3.Up * h * U.In, from, to ) );
			if ( closest < (BeamWidth + 14f) * U.In ) Hurt( player, amount );
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

	void ThrowCards( Vector3 origin, float angleOffset, float slope )
	{
		var count = Math.Max( 1, CardsPerRing );
		for ( var i = 0; i < count; i++ )
		{
			var yaw = angleOffset + i * 360f / count;
			var dir = (new Angles( 0f, yaw, 0f ).Forward + Vector3.Up * (slope + Rng.Float( -0.03f, 0.03f ))).Normalized();
			Card.Spawn( origin + dir * 12f * U.In, dir * CardSpeed, (CardKind)Rng.Int( 0, 3 ), CardDamage, this );
		}
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

	public void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead ) return;
		if ( index < 0 || index >= parts.Length || !Has( index ) ) index = Torso;
		lastShotDirection = direction;
		lastShotPointBlank = (position - origin).Length() < CreatureGore.OverkillRange * U.In;
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

	int SubtreeMask( int index )
	{
		var mask = 1 << index;
		for ( var i = 0; i < parts.Length; i++ )
		{
			if ( i == index || parts[i] is null ) continue;
			for ( var p = parts[i].Parent; p >= 0; p = parts[p].Parent )
				if ( p == index ) { mask |= 1 << i; break; }
		}
		return mask;
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

	void MarkGone( int index )
	{
		var mask = SubtreeMask( index );
		for ( var i = 0; i < parts.Length; i++ )
			if ( (mask & (1 << i)) != 0 && parts[i] is not null ) parts[i].Gone = true;
	}

	/// <summary> Lets a piece (and anything hanging off it) go as one loose physics chunk. Push in s&box units/s. </summary>
	Node3D Detach( Part part, Vector3 push, float spin = 10f )
	{
		var source = part.Go;
		var piece = new Node3D { Name = "MC61 Piece" };
		GetTree().CurrentScene.AddChild( piece );
		piece.GlobalTransform = source.GlobalTransform.Orthonormalized();
		var shapes = new List<(Shape3D Shape, Transform3D Global)>();
		foreach ( var shape in source.FindChildren( "*", "CollisionShape3D", true, false ).OfType<CollisionShape3D>() )
			shapes.Add( (shape.Shape, shape.GlobalTransform) );
		foreach ( var child in source.GetChildren().OfType<Node3D>().ToList() )
			if ( child is not CollisionShape3D ) child.Reparent( piece, true );
		foreach ( var sub in piece.FindChildren( "*", "StaticBody3D", true, false ).OfType<CreaturePart>().ToList() )
		{
			var holder = new Node3D { Name = sub.Name };
			sub.GetParent().AddChild( holder );
			holder.GlobalTransform = sub.GlobalTransform;
			foreach ( var c in sub.GetChildren().OfType<Node3D>().ToList() ) if ( c is not CollisionShape3D ) c.Reparent( holder, true );
			sub.QueueFree();
		}
		source.QueueFree();
		var gib = Gib.Attach( piece, gore, 8f, 40f, 6f, part.Velocity * 0.4f + push * U.In, Goo, addCollider: shapes.Count == 0 );
		foreach ( var (shape, global) in shapes )
		{
			var cs = new CollisionShape3D { Shape = shape };
			gib.AddChild( cs );
			cs.GlobalTransform = global.Orthonormalized();
		}
		gib.AngularVelocity = Rng.InSphere( spin );
		return piece;
	}

	// ---- Death ----

	void OnDied()
	{
		timeSinceDeath = 0f;
		Killed?.Invoke( this );
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
