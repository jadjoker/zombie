using Godot;

/// <summary>
/// Port of s&box PlayerRig + PlayerAppearance: the third-person robot (models/characters/survivor), its paint job,
/// the gun in its hands, and the over-the-shoulder camera. In s&box the robot copied an unseen citizen skeleton (walk,
/// jump, crouch, hold poses); that skeleton is Facepunch's, so here a small procedural driver makes the same targets:
/// a walk cycle for the ankles, a crouch, a lean, the aim pitch through spine and head, and the hands on the gun.
/// The rest is the s&box code: two-bone IK for arms and legs, the slide, swimming, the surge curl and superman,
/// the jet-foot propellers and the two-handed axe swing. All of it works in s&box character space (inches, x forward,
/// y left, z up, feet at the origin) and converts to the skeleton at the end.
/// </summary>
public partial class PlayerRig : Node
{
	public float SlideLean = 48f;
	public Vector2 SlideHips = new( -9f, 17f );
	// Over-the-shoulder camera (back, sideways, up), from PlayerAppearance.
	public Vector3 ShoulderOffset = new( 95f, 26f, 4f );
	public Vector3 AimShoulderOffset = new( 55f, 22f, 2f );
	public Vector3 ExtraPullBack = new( 25f, 0f, 4f );
	public Vector3 FlyingPullBack = new( 55f, 4f, 14f );

	PlayerController controller;
	PlayerSurvivor survivor;
	PlayerMovement movement;
	WaterFeel water;
	Jetpack jet;
	PlayerMelee melee;
	WeaponInventory inventory;

	Node3D rigRoot;           // the character frame: feet, facing (and tilts for slide, swim, surge)
	Node3D model;
	Skeleton3D skeleton;
	readonly List<MeshInstance3D> meshes = new();
	Transform3D K;            // rig root space (Godot metres) to skeleton space
	Basis kRot;
	readonly Dictionary<string, int> bones = new();
	readonly Dictionary<string, Vector3> restPos = new();   // s&box character space, inches
	readonly Dictionary<string, Basis> restRot = new();     // s&box character space
	bool ready;
	RobotRagdoll ragdoll;
	string appliedSkin;

	float slidePropTime, slideHeading;
	bool wasSliding;
	/// <summary> The slide is a head-first dive now (playtest 11/11); the old folded-leg sit is switched off. </summary>
	const bool FoldedSlide = false;
	float slideBlend, slideHold, swimBlend, swimPhase, chargeBlend, surgeBlend, jetBlend, propSpin, flyingCam;
	float swimPitch, swimRoll;
	Vector3 swimMove, swimAccel, lastSwimLocal;
	Basis slideYaw = Basis.Identity;
	TimeSince timeSincePropBubble;
	readonly Dictionary<string, Node3D> props = new(), propSpinners = new();
	Node3D worldAxe;
	readonly Dictionary<PlayerWeapon, (Node3D Root, AnimationPlayer Player, Skeleton3D Skeleton)> worldGuns = new();

	// Procedural driver state.
	float gait, duck, air, bodyYaw;
	bool hasYaw;

	public override void _Ready()
	{
		Name = "PlayerRig";
		controller = GetParent<PlayerController>();
	}

	public override void _ExitTree()
	{
		if ( rigRoot is not null && IsInstanceValid( rigRoot ) ) rigRoot.QueueFree();
	}

	// ---- s&box space helpers (the same axes as the s&box code; converted at the end) ----

	/// <summary> s&box directions to Godot ones, as a basis (x forward, y left, z up). </summary>
	static readonly Basis M = new( new Vector3( 0, 0, -1 ), new Vector3( -1, 0, 0 ), new Vector3( 0, 1, 0 ) );
	static Basis ToG( Basis s ) => M * s * M.Transposed();
	static Basis ToS( Basis g ) => M.Transposed() * g * M;
	static Vector3 G2S( Vector3 g ) => M.Transposed() * g / U.In;   // Godot metres to s&box inches
	/// <summary> s&box Angles( pitch, yaw, roll ).ToRotation() in s&box space. </summary>
	static Basis SAng( float pitch, float yaw, float roll ) => ToS( new Angles( pitch, yaw, roll ).ToBasis() );
	static Basis SAxis( Vector3 axis, float degrees ) => new( axis.Normalized(), MathX.Rad( degrees ) );
	/// <summary> s&box Rotation.LookAt( forward, up ) in s&box space. </summary>
	static Basis SLook( Vector3 forward, Vector3 up )
	{
		var x = forward.Normalized();
		var y = up.Cross( x );
		if ( y.LengthSquared() < 1e-6f ) y = new Vector3( 0, 0, 1 ).Cross( x );
		if ( y.LengthSquared() < 1e-6f ) y = new Vector3( 0, 1, 0 );
		y = y.Normalized();
		return new Basis( x, y, x.Cross( y ) );
	}
	static Basis FromTo( Vector3 a, Vector3 b )
	{
		a = a.Normalized(); b = b.Normalized();
		var d = a.Dot( b );
		if ( d > 0.99999f ) return Basis.Identity;
		if ( d < -0.99999f )
		{
			var axis = a.Cross( new Vector3( 1, 0, 0 ) );
			if ( axis.LengthSquared() < 1e-6f ) axis = a.Cross( new Vector3( 0, 1, 0 ) );
			return new Basis( axis.Normalized(), MathF.PI );
		}
		return new Basis( new Quaternion( a, b ) );
	}
	static Vector3 SFwd => new( 1, 0, 0 );
	static Vector3 SLeft => new( 0, 1, 0 );
	static Vector3 SUp => new( 0, 0, 1 );

	// ---- setup ----

	bool Setup()
	{
		survivor = controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
		movement = controller.GetNodeOrNull<PlayerMovement>( "PlayerMovement" );
		water = controller.GetNodeOrNull<WaterFeel>( "WaterFeel" );
		jet = controller.GetNodeOrNull<Jetpack>( "Jetpack" );
		melee = controller.GetNodeOrNull<PlayerMelee>( "PlayerMelee" );
		inventory = controller.GetNodeOrNull<WeaponInventory>( "WeaponInventory" );

		rigRoot = new Node3D { Name = "Survivor Rig", TopLevel = true, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
		controller.AddChild( rigRoot );
		rigRoot.GlobalTransform = new Transform3D( Basis.Identity, controller.GlobalPosition );
		model = GD.Load<PackedScene>( "res://assets/models/characters/survivor/survivor.fbx" ).Instantiate<Node3D>();
		// Like the creature sculpts: the FBX is 0.01 per inch with s&box forward along -Y; turn it to face Godot -Z.
		model.Basis = new Basis( Vector3.Up, MathF.PI ) * Basis.FromScale( Vector3.One * 2.54f );
		rigRoot.AddChild( model );
		skeleton = model.FindChildren( "*", "Skeleton3D", true, false ).OfType<Skeleton3D>().FirstOrDefault();
		if ( skeleton is null ) return false;
		foreach ( var mesh in model.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() ) meshes.Add( mesh );

		K = rigRoot.GlobalTransform.AffineInverse() * skeleton.GlobalTransform;
		kRot = K.Basis.Orthonormalized();
		for ( var b = 0; b < skeleton.GetBoneCount(); b++ )
		{
			var name = skeleton.GetBoneName( b );
			bones[name] = b;
			var rest = skeleton.GetBoneGlobalRest( b );
			restPos[name] = G2S( K * rest.Origin );
			restRot[name] = ToS( kRot * rest.Basis.Orthonormalized() );
		}
		return bones.Count > 10;
	}

	/// <summary> Sets a bone from s&box character space: where it is, and its turn from the rest pose. </summary>
	void Put( string name, Vector3 position, Basis delta, float scale = 1f )
	{
		if ( !bones.TryGetValue( name, out var bone ) ) return;
		var rot = ToG( delta * restRot[name] );
		var skelBasis = kRot.Inverse() * rot;
		if ( scale != 1f ) skelBasis = skelBasis.Scaled( Vector3.One * scale );
		skeleton.SetBoneGlobalPose( bone, new Transform3D( skelBasis, K.AffineInverse() * U.Sbox( position ) ) );
	}

	Vector3 RestDir( string from, string to ) => (restPos[to] - restPos[from]).Normalized();

	/// <summary> Where a two-bone chain bends: the elbow or knee, on the side the pole points to. </summary>
	static Vector3 Bend( Vector3 root, Vector3 target, float upper, float lower, Vector3 pole, out Vector3 reached )
	{
		var d = target - root;
		var dist = MathX.Clamp( d.Length(), MathF.Abs( upper - lower ) + 0.05f, upper + lower - 0.05f );
		var dir = d.Length() > 0.001f ? d.Normalized() : new Vector3( 0, 0, -1 );
		reached = root + dir * dist;
		var along = (dist * dist + upper * upper - lower * lower) / (2f * dist);
		var h = MathF.Sqrt( MathF.Max( upper * upper - along * along, 0f ) );
		var p = pole - dir * pole.Dot( dir );
		p = p.Length() > 0.001f ? p.Normalized() : new Vector3( -1, 0, 0 );
		return root + dir * along + p * h;
	}

	// ---- every frame ----

	public bool ShowsBody => controller.ThirdPerson;
	PlayerWeapon Held => inventory?.Current;

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( !ready ) { ready = Setup(); if ( !ready ) return; }
		var alive = survivor?.IsAlive ?? true;
		// Step 8: on death the body goes limp as a real ragdoll (gone again on respawn).
		if ( !alive && ragdoll is null )
		{
			ApplySkin();
			PlaceFrame( dt );
			Pose( dt );
			ragdoll = RobotRagdoll.Create( model, controller.Velocity, meshes );
			// The camera leaves the eyes and watches the body (issues #34 and #40).
			deathCamAt = controller.Camera.GlobalPosition;
			// From behind where they were looking, or the nearest side with room if something is in the way there.
			var behind = MathX.Rad( controller.EyeAngles.Yaw );   // looking along -(sin, cos) of the yaw, so +(sin, cos) is behind
			deathCamAngle = behind;
			var most = -1f;
			for ( var i = 0; i < 8; i++ )
			{
				var angle = behind + (i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * MathF.PI * 0.25f;
				var body = ragdoll.Centre + Vector3.Up * 0.3f;
				var to = body + new Vector3( MathF.Sin( angle ), 0f, MathF.Cos( angle ) ) * 3.8f + Vector3.Up * 2.1f;
				// Swept from a little above the body: it is lying on the sea bed, and a sweep that starts touching it sees no room at all.
				var tr = Trace.Sphere( controller, 0.25f, body + Vector3.Up * 0.5f, to, Layers.World, new Godot.Collections.Array<Rid> { controller.GetRid() } );
				var room = tr.Hit && !tr.StartedSolid ? tr.Distance : 4.4f;
				if ( room > most + 0.3f ) { most = room; deathCamAngle = angle; }
			}
			controller.CameraOverride = DeathCamera;
		}
		else if ( alive && ragdoll is not null )
		{
			if ( IsInstanceValid( ragdoll ) ) ragdoll.QueueFree();
			ragdoll = null;
			controller.CameraOverride = null;
		}
		var show = alive && (ShowsBody || DevFlags.FreeCam && ShowsBody);
		rigRoot.Visible = show;
		UpdateCamera( dt );
		if ( !show )
		{
			foreach ( var (_, gun) in worldGuns ) gun.Root.Visible = false;
			return;
		}
		ApplySkin();
		PlaceFrame( dt );
		Pose( dt );
	}

	/// <summary> Our own over-the-shoulder camera, tightening in while aiming (PlayerAppearance). </summary>
	Vector3 deathCamAt;
	float deathCamAngle;

	/// <summary>
	/// Dead: the camera pulls back and up from where the eyes were, keeps the ragdoll in the middle of the view and
	/// drifts slowly round it, staying clear of walls.
	/// </summary>
	Transform3D? DeathCamera( float dt )
	{
		if ( ragdoll is null || !IsInstanceValid( ragdoll ) ) return null;
		var body = ragdoll.Centre + Vector3.Up * 0.3f;
		deathCamAngle += dt * 0.25f;
		var wanted = body + new Vector3( MathF.Sin( deathCamAngle ), 0f, MathF.Cos( deathCamAngle ) ) * 3.8f + Vector3.Up * 2.1f;
		var above = body + Vector3.Up * 0.5f;
		var tr = Trace.Sphere( controller, 0.25f, above, wanted, Layers.World, new Godot.Collections.Array<Rid> { controller.GetRid() } );
		if ( tr.Hit && !tr.StartedSolid )
		{
			wanted = above + (wanted - above).SafeNormal() * MathF.Max( 0.6f, tr.Distance );
			// Something is in the way on this side: swing round it rather than sit pressed up close.
			if ( tr.Distance < 2.5f ) deathCamAngle += dt * 1.6f;
		}
		deathCamAt = deathCamAt.Lerp( wanted, 1f - MathF.Exp( -3f * dt ) );
		// Aimed a little over the body, so it sits below the You Died text and not under it.
		var look = body + Vector3.Up * 1f - deathCamAt;
		if ( look.LengthSquared() < 0.01f ) look = Vector3.Forward;
		return new Transform3D( Shapes.LookAt( look ), deathCamAt );
	}

	void UpdateCamera( float dt )
	{
		if ( !controller.ThirdPerson ) return;
		var aim = Held?.AimAmount ?? 0f;
		var flying = (movement?.IsFloating ?? false) || (jet is not null && (jet.Thrusting || jet.Surging || jet.Charging)) || (!controller.IsOnGround && (water?.Swimming ?? 0f) > 0.5f);
		flyingCam = MathX.Lerp( flyingCam, flying ? 1f : 0f, 1f - MathF.Exp( -2.5f * dt ) );
		var pull = (ExtraPullBack + FlyingPullBack * flyingCam) * (1f - aim);
		var offset = ShoulderOffset.Lerp( AimShoulderOffset, aim ) + pull;
		controller.CameraOffset = offset * U.In;
	}

	static readonly Dictionary<string, Color[]> Paint = new()
	{
		// frame, body, accent, metal (from the s&box survivor materials and outfits)
		["Default"] = new[] { new Color( 0.17f, 0.18f, 0.21f ), new Color( 0.74f, 0.78f, 0.82f ), new Color( 0.95f, 0.52f, 0.14f ), new Color( 0.88f, 0.90f, 0.93f ) },
		["Rust"] = new[] { new Color( 0.20f, 0.16f, 0.14f ), new Color( 0.58f, 0.36f, 0.22f ), new Color( 0.78f, 0.66f, 0.30f ), new Color( 0.62f, 0.50f, 0.40f ) },
		["Stealth"] = new[] { new Color( 0.08f, 0.08f, 0.10f ), new Color( 0.20f, 0.21f, 0.25f ), new Color( 0.75f, 0.12f, 0.14f ), new Color( 0.40f, 0.41f, 0.46f ) },
		["Hazard"] = new[] { new Color( 0.14f, 0.14f, 0.16f ), new Color( 0.96f, 0.80f, 0.14f ), new Color( 0.12f, 0.12f, 0.14f ), new Color( 0.80f, 0.80f, 0.84f ) },
		["Frost"] = new[] { new Color( 0.30f, 0.38f, 0.50f ), new Color( 0.90f, 0.94f, 0.98f ), new Color( 0.30f, 0.58f, 0.90f ), new Color( 0.80f, 0.88f, 0.96f ) },
		["Gold"] = new[] { new Color( 0.20f, 0.17f, 0.14f ), new Color( 0.92f, 0.70f, 0.22f ), new Color( 0.70f, 0.12f, 0.14f ), new Color( 0.98f, 0.86f, 0.50f ) },
	};

	void ApplySkin()
	{
		if ( appliedSkin == CharacterSkins.Current ) return;
		appliedSkin = CharacterSkins.Current;
		var paint = Paint.TryGetValue( appliedSkin, out var p ) ? p : Paint["Default"];
		foreach ( var mesh in meshes )
		{
			for ( var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++ )
			{
				var slot = mesh.Mesh.SurfaceGetMaterial( i )?.ResourceName ?? "";
				if ( slot == "glow" ) { mesh.SetSurfaceOverrideMaterial( i, FxMaterials.GlowBulb( CharacterSkins.GlowOf( appliedSkin ), 2.5f ) ); continue; }
				StandardMaterial3D m = slot switch
				{
					"frame" => Shapes.Material( paint[0], "smooth" ),
					"body" => Shapes.Material( paint[1], "smooth" ),
					"accent" => Shapes.Material( paint[2], "smooth" ),
					"metal" => Shapes.Material( paint[3], "smooth" ),
					_ => null,
				};
				if ( m is null ) continue;
				m.Metallic = slot is "body" or "metal" ? 0.5f : 0.2f;
				mesh.SetSurfaceOverrideMaterial( i, m );
			}
		}
	}

	/// <summary> The character frame: feet and facing, tilted for the slide, swimming and the surge (the s&box driver moves). </summary>
	void PlaceFrame( float dt )
	{
		var feet = controller.DrawnPosition;
		var wantYaw = controller.EyeAngles.Yaw;
		if ( !hasYaw ) { bodyYaw = wantYaw; hasYaw = true; }
		bodyYaw += Mathf.Wrap( wantYaw - bodyYaw, -180f, 180f ) * MathX.Clamp( dt * 14f, 0f, 1f );
		var yawRot = new Angles( 0f, bodyYaw, 0f ).ToBasis();
		var rot = yawRot;
		var pos = feet;

		var sliding = (movement?.IsSliding ?? false) && !(movement?.IsFloating ?? false);
		if ( sliding ) slideHold = 0.18f;
		// The foot propellers kick in for a moment as the dive starts.
		if ( sliding && !wasSliding ) slidePropTime = 0.5f;
		wasSliding = sliding;
		slidePropTime = MathF.Max( 0f, slidePropTime - dt );
		slideHold = MathF.Max( 0f, slideHold - dt );
		var wantSlide = slideHold > 0f && (survivor?.IsAlive ?? true) && !(movement?.IsFloating ?? false) ? 1f : 0f;
		slideBlend = MathX.Approach( slideBlend, wantSlide, dt * (wantSlide > slideBlend ? 8f : 3.2f) );
		swimBlend = MathX.Approach( swimBlend, slideBlend > 0.001f ? 0f : (water?.Swimming ?? 0f), dt * 4f );

		var hips = 34.8f * U.In;
		if ( swimBlend > 0.001f )
		{
			var velocity = G2S( controller.Velocity );
			var local = SAng( 0f, bodyYaw, 0f ).Inverse() * velocity;
			var forward = MathX.Clamp( local.X / 260f, -1f, 1f );
			var sideways = MathX.Clamp( local.Y / 260f, -1f, 1f );
			var rising = MathX.Clamp( velocity.Z / 250f, -1f, 1f );
			swimMove = new Vector3( forward, sideways, rising );
			var accel = (local - lastSwimLocal) / MathF.Max( dt, 0.001f );
			lastSwimLocal = local;
			swimAccel = swimAccel.Lerp( accel, 1f - MathF.Exp( -6f * dt ) );
			var speed = new Vector2( local.X, local.Y ).Length();
			swimPhase += dt * (3.2f + speed / 70f);
			var idle = 1f - MathX.Clamp( speed / 120f, 0f, 1f );
			var drift = (float)Clock.Now;
			var pitch = (forward > 0f ? 44f * forward : 20f * forward) - 16f * rising + MathX.Clamp( swimAccel.X * 0.02f, -10f, 10f ) + (8f + 3f * MathF.Sin( drift * 1.3f )) * idle;
			var roll = -26f * sideways + MathX.Clamp( -swimAccel.Y * 0.015f, -8f, 8f ) + 2.5f * MathF.Sin( drift * 0.9f ) * idle;
			swimPitch = MathX.Lerp( swimPitch, pitch, 1f - MathF.Exp( -5f * dt ) );
			swimRoll = MathX.Lerp( swimRoll, roll, 1f - MathF.Exp( -5f * dt ) );
			rot = yawRot * new Angles( swimPitch * swimBlend, 0f, swimRoll * swimBlend ).ToBasis();
			var bob = Vector3.Up * MathF.Sin( drift * 1.6f ) * 1.8f * idle * swimBlend * U.In;
			pos = feet + Vector3.Up * hips + bob - rot * (Vector3.Up * hips);
		}

		jetBlend = MathX.Approach( jetBlend, (jet?.Thrusting ?? false) || (jet?.Surging ?? false) || slidePropTime > 0f ? 1f : 0f, dt * ((jet?.Thrusting ?? false) || slidePropTime > 0f ? 6f : 3f) );
		chargeBlend = MathX.Lerp( chargeBlend, jet?.Charging ?? false ? jet.Charge : 0f, 1f - MathF.Exp( -12f * dt ) );
		surgeBlend = MathX.Approach( surgeBlend, jet?.Surging ?? false ? 1f : 0f, dt * ((jet?.Surging ?? false) ? 10f : 3f) );
		propSpin = (propSpin + dt * 1800f * jetBlend * (1f + 1.5f * chargeBlend + ((jet?.Surging ?? false) ? 1f : 0f))) % 360f;
		if ( (chargeBlend > 0.01f || surgeBlend > 0.001f) && slideBlend <= 0.001f )
		{
			var q = rot.GetRotationQuaternion();
			if ( chargeBlend > 0.01f ) q = q.SafeSlerp( (yawRot * new Angles( 28f, 0f, 0f ).ToBasis()).GetRotationQuaternion(), chargeBlend );
			if ( surgeBlend > 0.001f )
			{
				var dir = jet.SurgeDirection.IsNearlyZero() ? -yawRot.Z : jet.SurgeDirection.Normalized();
				// Head toward the burst, belly toward the ground (or forward when going straight up).
				var belly = Vector3.Down - dir * Vector3.Down.Dot( dir );
				if ( belly.Length() < 0.2f ) belly = -yawRot.Z;
				var superman = Basis.LookingAt( belly.Normalized(), dir );   // forward = belly, up = head
				var e = surgeBlend * surgeBlend * (3f - 2f * surgeBlend);
				q = q.SafeSlerp( superman.GetRotationQuaternion(), e );
			}
			rot = new Basis( q );
			pos = feet + Vector3.Up * hips - rot * (Vector3.Up * hips);
		}

		if ( slideBlend > 0.001f )
		{
			slideYaw = yawRot;
			var ease = slideBlend * slideBlend * (3f - 2f * slideBlend);
			// Head first along the way it is travelling, belly to the ground, a hand's width off it.
			var heading = controller.Velocity.Flat();
			if ( heading.Length() > 0.5f ) slideHeading = MathF.Atan2( heading.X, -heading.Z );
			var along = new Vector3( MathF.Sin( slideHeading ), 0f, -MathF.Cos( slideHeading ) );
			var dive = Basis.LookingAt( Vector3.Down, (along + Vector3.Up * 0.14f).Normalized() );
			rot = new Basis( rot.GetRotationQuaternion().SafeSlerp( dive.GetRotationQuaternion(), ease ) );
			var pelvisRest = feet + Vector3.Up * hips;
			var pelvisDive = feet + Vector3.Up * (13f * U.In);
			var pelvis = pelvisRest.Lerp( pelvisDive, ease );
			pos = pelvis - rot * (Vector3.Up * hips);
		}
		rigRoot.GlobalTransform = new Transform3D( rot, pos );
	}

	// ---- the procedural driver (stands in for the citizen animation) ----

	/// <summary> A world point in s&box character space. </summary>
	Vector3 Char( Vector3 world ) => G2S( rigRoot.GlobalTransform.AffineInverse() * world );
	Vector3 CharDir( Vector3 worldDir ) => M.Transposed() * (rigRoot.GlobalBasis.Orthonormalized().Inverse() * worldDir);

	void Pose( float dt )
	{
		var aimPitch = MathX.Clamp( controller.EyeAngles.Pitch, -80f, 80f );
		var velS = CharDir( controller.Velocity ) / U.In;
		var flat = new Vector2( velS.X, velS.Y );
		var speed = flat.Length();
		var grounded = controller.IsOnGround;
		air = MathX.Approach( air, grounded ? 0f : 1f, dt * 6f );
		duck = MathX.Approach( duck, controller.IsDucking && grounded ? 1f : 0f, dt * 6f );
		var moving = MathX.Clamp( speed / 60f, 0f, 1f ) * (1f - air);
		var run = MathX.Clamp( (speed - 200f) / 250f, 0f, 1f );
		gait += dt * MathX.Clamp( speed / 13f, 0f, 30f );
		var moveDir = speed > 1f ? new Vector3( flat.X / speed, flat.Y / speed, 0f ) : SFwd;
		var stride = MathX.Clamp( speed * 0.045f, 0f, 15f ) * (1f - 0.4f * duck);

		// Hips: lowered by the crouch, bobbing with the steps, leaning into a run.
		var pelvisPos = restPos["pelvis"] + new Vector3( 0f, 0f, -14f * duck + (-MathF.Cos( gait * 2f ) * 1.2f - 0.6f) * moving );
		var pelvisRot = SAxis( SUp, MathF.Sin( gait ) * 7f * moving ) * SAng( 4f * run + 10f * duck, 0f, 0f );
		Put( "pelvis", pelvisPos, pelvisRot );

		var spinePos = pelvisPos + pelvisRot * (restPos["spine"] - restPos["pelvis"]);
		var spineRot = SAng( aimPitch * 0.45f + 6f * run, -MathF.Sin( gait ) * 5f * moving, 0f );
		var swinging = melee is not null && melee.IsSwinging;
		(Vector3 Grip, Basis Rot, float Twist, float Weight) axePose = swinging ? SwingPose() : default;
		if ( swinging ) spineRot = SAxis( SUp, axePose.Twist * axePose.Weight ) * spineRot;
		Put( "spine", spinePos, spineRot );

		if ( swinging )
		{
			worldAxe ??= BuildWorldAxe();
			var axeOut = melee.SwingTime > 0.035f && melee.SwingTime < PlayerMelee_Cooldown - 0.065f;
			worldAxe.Visible = axeOut;
			worldAxe.GlobalTransform = rigRoot.GlobalTransform * new Transform3D( ToG( axePose.Rot ), U.Sbox( axePose.Grip ) );
		}
		else if ( worldAxe is not null ) worldAxe.Visible = false;

		var headPos = spinePos + spineRot * (restPos["head"] - restPos["spine"]);
		Put( "head", headPos, SAng( aimPitch * 0.9f, 0f, 0f ) );

		// The gun in hand: held at the chest and pointing along the aim; the hands go where the gun's hands are.
		var held = Held;
		var gunShown = held is not null && !(worldAxe?.Visible ?? false);
		Vector3? gripR = null, gripL = null;
		foreach ( var weapon in controller.GetChildren().OfType<PlayerWeapon>() )
		{
			var gun = WorldGun( weapon );
			var on = gunShown && weapon == held;
			gun.Root.Visible = on;
			if ( !on ) continue;
			var aimS = SAng( aimPitch * 0.85f, 0f, 0f );
			var pivot = spinePos + spineRot * new Vector3( 4f, -2f, 10f );    // upper chest, a touch right
			var handAt = pivot + aimS * new Vector3( 10f, -4.5f, -7f );
			// Turn the gun with the aim, then slide it so its right-hand bone lands at the hand spot.
			gun.Root.GlobalBasis = rigRoot.GlobalBasis * ToG( aimS );
			gun.Root.GlobalPosition = rigRoot.GlobalTransform * U.Sbox( handAt );
			var r = BoneWorld( gun.Skeleton, "hand_R" );
			gun.Root.GlobalPosition += rigRoot.GlobalTransform * U.Sbox( handAt ) - r;
			gripR = Char( BoneWorld( gun.Skeleton, "hand_R" ) );
			gripL = Char( BoneWorld( gun.Skeleton, "hand_L" ) );
			var anim = weapon.IsReloading ? "reload" : "idle";
			if ( gun.Player.HasAnimation( anim ) && gun.Player.CurrentAnimation != anim ) gun.Player.Play( anim );
		}

		var now = (float)Clock.Now;
		foreach ( var s in new[] { "L", "R" } )
		{
			var side = s == "L" ? 1f : -1f;
			var shoulder = spinePos + spineRot * (restPos[$"upper_{s}"] - restPos["spine"]);
			var upperLen = (restPos[$"lower_{s}"] - restPos[$"upper_{s}"]).Length();
			var lowerLen = (restPos[$"hand_{s}"] - restPos[$"lower_{s}"]).Length();
			// No gun: arms hang and swing with the steps.
			var swing = MathF.Sin( gait + (s == "L" ? MathF.PI : 0f) ) * 7f * moving;
			var wristTarget = shoulder + new Vector3( 2f + swing, side * 2.5f, -(upperLen + lowerLen) * 0.92f );
			var armPole = new Vector3( -0.6f, side * 0.6f, -1f );
			if ( (s == "R" ? gripR : gripL) is { } grip ) { wristTarget = grip; armPole = new Vector3( -0.4f, side * 0.9f, -1f ); }
			if ( swinging )
			{
				var along = s == "R" ? 1.5f : 8f;
				var onAxe = axePose.Grip + axePose.Rot * new Vector3( along, 0f, 2.2f );
				var w = axePose.Weight;
				wristTarget = wristTarget.Lerp( onAxe, w );
				armPole = armPole.Normalized().Lerp( new Vector3( -0.2f, side, -0.9f ).Normalized(), w );
			}
			var elbow = Bend( shoulder, wristTarget, upperLen, lowerLen, armPole, out var wrist );
			var upperRot = FromTo( RestDir( $"upper_{s}", $"lower_{s}" ), elbow - shoulder );
			var lowerRot = FromTo( RestDir( $"lower_{s}", $"hand_{s}" ), wrist - elbow );
			Put( $"upper_{s}", shoulder, upperRot );
			Put( $"lower_{s}", elbow, lowerRot );
			Put( $"hand_{s}", wrist, lowerRot );

			// Leg: a walk cycle for the ankle (the citizen did this), feet tucked a little in the air.
			var hip = pelvisPos + pelvisRot * (restPos[$"thigh_{s}"] - restPos["pelvis"]);
			var thighLen = (restPos[$"shin_{s}"] - restPos[$"thigh_{s}"]).Length();
			var shinLen = (restPos[$"foot_{s}"] - restPos[$"shin_{s}"]).Length();
			var phase = gait + (s == "L" ? 0f : MathF.PI);
			var ankleRest = new Vector3( 0f, restPos[$"foot_{s}"].Y, restPos[$"foot_{s}"].Z );
			var step = moveDir * MathF.Cos( phase ) * stride;
			var lift = MathF.Max( 0f, MathF.Sin( phase ) ) * (4f + 4f * run) * moving;
			var ankleTarget = ankleRest + step + new Vector3( 0f, 0f, lift );
			if ( duck > 0.001f ) ankleTarget += new Vector3( s == "L" ? 2f : -3f, side * 1.5f, 0f ) * duck;
			ankleTarget = ankleTarget.Lerp( hip + new Vector3( s == "L" ? 4f : -3f, side * 1.5f, -(thighLen + shinLen) * 0.78f ), air );
			var kneePole = new Vector3( 20f, side * 2f, 0f );
			var footLift = 0f;
			if ( FoldedSlide && slideBlend > 0.001f )
			{
				var ease = slideBlend * slideBlend * (3f - 2f * slideBlend);
				var lead = s == "R";
				var spot = lead ? new Vector3( 34f, -5.5f, 6.5f ) : new Vector3( -2f, 7.5f, 5.2f );
				var slideTarget = Char( controller.DrawnPosition + slideYaw * U.Sbox( spot ) );
				ankleTarget = ankleTarget.Lerp( slideTarget, ease );
				var slidePole = CharDir( slideYaw * ToG( Basis.Identity ) * U.Sbox( lead ? new Vector3( 0.4f, 0f, 1f ) : new Vector3( 0.6f, 0f, 1f ) ) ) / U.In;
				kneePole = kneePole.Lerp( slidePole * 30f, ease );
				footLift = lead ? 38f * ease : 0f;
			}
			if ( swimBlend > 0.001f )
			{
				var hanging = hip + new Vector3( 0f, 0f, -(thighLen + shinLen) * 0.9f ) + new Vector3( 0f, side * 1.5f, 0f );
				ankleTarget = ankleTarget.Lerp( hanging, swimBlend );
				kneePole = kneePole.Lerp( new Vector3( 30f, 0f, 0f ), swimBlend );
				var kick = MathF.Sin( swimPhase + (s == "L" ? 0f : MathF.PI) );
				var effort = 0.5f + 0.5f * MathX.Clamp( new Vector2( swimMove.X, swimMove.Y ).Length() + MathF.Abs( swimMove.Z ), 0f, 1f );
				var back = MathF.Max( 0f, -swimMove.X );
				var trail = new Vector3( -1f, 0f, 0f ) * (3f + kick * 2f) * (1f - back) + SFwd * back * (1.5f + kick * 5f);
				var sideMove = SLeft * (-swimMove.Y * 7f) + SLeft * side * 1.5f * kick * MathF.Abs( swimMove.Y );
				ankleTarget += (SUp * (kick * 7f * effort * (1f - 0.7f * back) + 2.5f * back) + trail + sideMove) * swimBlend;
			}
			if ( chargeBlend > 0.01f )
			{
				var tuck = hip + new Vector3( 11f, side * 2f, -13f );
				ankleTarget = ankleTarget.Lerp( tuck, chargeBlend );
				kneePole = kneePole.Lerp( new Vector3( 30f, 0f, 20f ), chargeBlend );
			}
			if ( jetBlend > 0.001f && chargeBlend < 0.5f )
			{
				var straight = hip + new Vector3( 0f, 0f, -(thighLen + shinLen) * 0.97f ) + new Vector3( 0f, -side * 1.2f, 0f );
				straight += SFwd * MathF.Sin( now * 40f + (s == "L" ? 0f : 1.7f) ) * 0.4f;
				ankleTarget = ankleTarget.Lerp( straight, jetBlend );
				kneePole = kneePole.Lerp( new Vector3( 30f, 0f, 0f ), jetBlend );
			}
			var knee = Bend( hip, ankleTarget, thighLen, shinLen, kneePole, out var ankle );
			Put( $"thigh_{s}", hip, FromTo( RestDir( $"thigh_{s}", $"shin_{s}" ), knee - hip ) );
			Put( $"shin_{s}", knee, FromTo( RestDir( $"shin_{s}", $"foot_{s}" ), ankle - knee ) );

			// Foot: toes along the facing (tipping up with the step), flat for the slide's folded foot.
			var toeTip = MathF.Max( 0f, -MathF.Cos( phase ) ) * 15f * moving;
			var footRot = SAng( -toeTip * 0.5f, 0f, 0f );
			if ( FoldedSlide && slideBlend > 0.001f )
			{
				var slideForward = CharDir( slideYaw * ToG( SAng( -footLift, 0f, 0f ) ) * U.Sbox( SFwd ) ) / U.In;
				var up = CharDir( Vector3.Up );
				footRot = new Basis( footRot.GetRotationQuaternion().SafeSlerp( SLook( slideForward, up ).GetRotationQuaternion(), slideBlend ) );
			}
			if ( jetBlend > 0.001f )
			{
				var downRot = SLook( new Vector3( 0.05f, 0f, -1f ), SFwd );
				footRot = new Basis( footRot.GetRotationQuaternion().SafeSlerp( downRot.GetRotationQuaternion(), jetBlend ) );
			}
			var bootScale = 1f - 0.65f * jetBlend;
			Put( $"foot_{s}", ankle, footRot, bootScale );
			PlaceProp( s, ankle + footRot * SFwd * 4f * bootScale, footRot );
		}
	}

	const float PlayerMelee_Cooldown = 0.55f;

	(Node3D Root, AnimationPlayer Player, Skeleton3D Skeleton) WorldGun( PlayerWeapon weapon )
	{
		var modelName = weapon.ViewModelName;
		if ( worldGuns.TryGetValue( weapon, out var gun ) && IsInstanceValid( gun.Root ) )
		{
			if ( (string)gun.Root.GetMeta( "model", "" ) == modelName ) return gun;
			gun.Root.QueueFree();   // a different rifle design was picked
		}
		gun = WeaponModels.Create( modelName );
		gun.Root.SetMeta( "model", modelName );
		gun.Root.Name = $"World {weapon.DisplayName}";
		gun.Root.TopLevel = true;
		rigRoot.AddChild( gun.Root );
		// Only the gun: the first-person arms (the robot's plates, joints and trim) are hidden, the rig's own hands hold it.
		var hide = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color( 0, 0, 0, 0 ), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		foreach ( var mesh in gun.Root.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
			for ( var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++ )
			{
				var n = mesh.Mesh.SurfaceGetMaterial( i )?.ResourceName ?? "";
				if ( n is "sleeve" or "glove" or "plate" or "joint" or "trim" ) mesh.SetSurfaceOverrideMaterial( i, hide );
			}
		}
		if ( gun.Player.HasAnimation( "idle" ) ) gun.Player.Play( "idle" );
		worldGuns[weapon] = gun;
		return gun;
	}

	static Vector3 BoneWorld( Skeleton3D skel, string name )
	{
		var b = skel.FindBone( name );
		return b < 0 ? skel.GlobalPosition : skel.GlobalTransform * skel.GetBoneGlobalPose( b ).Origin;
	}

	// ---- the two-handed axe swing (s&box keys, body space, inches) ----

	readonly record struct AxeKey( float Time, Vector3 Grip, Vector3 Haft, Vector3 Edge, float Twist );
	static readonly AxeKey[] SwingKeys =
	{
		new( 0.00f, new Vector3( 15f, -11f, 44f ), new Vector3( 0.35f, 0.1f, 0.93f ), new Vector3( 1f, 0f, 0f ), 0f ),
		new( 0.07f, new Vector3( 12f, -12f, 50f ), new Vector3( 0.2f, 0.1f, 0.97f ), new Vector3( 1f, 0.2f, 0f ), 8f ),
		new( 0.15f, new Vector3( 3f, -11f, 64f ), new Vector3( -0.55f, -0.15f, 0.82f ), new Vector3( 0.5f, 0.3f, 0.8f ), 26f ),
		new( 0.24f, new Vector3( 22f, -2f, 47f ), new Vector3( 0.9f, 0.25f, -0.1f ), new Vector3( 0f, 0.45f, -0.9f ), -12f ),
		new( 0.31f, new Vector3( 17f, 9f, 34f ), new Vector3( 0.5f, 0.6f, -0.6f ), new Vector3( 0f, 0.7f, -0.7f ), -28f ),
		new( 0.55f, new Vector3( 15f, -11f, 44f ), new Vector3( 0.35f, 0.1f, 0.93f ), new Vector3( 1f, 0f, 0f ), 0f ),
	};

	static Basis AxeRotation( Vector3 haft, Vector3 edge )
	{
		var x = haft.Normalized();
		var e = (edge - x * edge.Dot( x )).Normalized();
		return SLook( x, -e );      // haft along X, the edge toward -Z
	}

	static Vector3 CatmullRom( Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t )
	{
		var t2 = t * t; var t3 = t2 * t;
		return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
	}

	static float CatmullRom( float p0, float p1, float p2, float p3, float t )
	{
		var t2 = t * t; var t3 = t2 * t;
		return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
	}

	(Vector3 Grip, Basis Rot, float Twist, float Weight) SwingPose()
	{
		var t = MathX.Clamp( melee.SwingTime, 0f, PlayerMelee_Cooldown );
		var n = SwingKeys.Length;
		var i = 0;
		while ( i < n - 2 && t > SwingKeys[i + 1].Time ) i++;
		var k0 = SwingKeys[Math.Max( i - 1, 0 )];
		var k1 = SwingKeys[i];
		var k2 = SwingKeys[i + 1];
		var k3 = SwingKeys[Math.Min( i + 2, n - 1 )];
		var u = k2.Time > k1.Time ? MathX.Clamp( (t - k1.Time) / (k2.Time - k1.Time), 0f, 1f ) : 1f;
		var side = melee.LeftSwing ? -1f : 1f;
		Vector3 Mir( Vector3 v ) => new( v.X, v.Y * side, v.Z );
		var grip = CatmullRom( Mir( k0.Grip ), Mir( k1.Grip ), Mir( k2.Grip ), Mir( k3.Grip ), u );
		var haft = CatmullRom( Mir( k0.Haft ), Mir( k1.Haft ), Mir( k2.Haft ), Mir( k3.Haft ), u );
		var edge = CatmullRom( Mir( k0.Edge ), Mir( k1.Edge ), Mir( k2.Edge ), Mir( k3.Edge ), u );
		var twist = CatmullRom( k0.Twist, k1.Twist, k2.Twist, k3.Twist, u ) * side;
		static float Smooth( float x ) { x = MathX.Clamp( x, 0f, 1f ); return x * x * (3f - 2f * x); }
		var weight = MathF.Min( Smooth( t / 0.08f ), Smooth( (PlayerMelee_Cooldown - t) / 0.14f ) );
		return (grip, AxeRotation( haft, edge ), twist, weight);
	}

	/// <summary> A fire axe from boxes (origin at the grip, haft along +X, edge toward -Z; s&box sizes). </summary>
	Node3D BuildWorldAxe()
	{
		var root = new Node3D { Name = "World Axe" };
		rigRoot.AddChild( root );
		root.TopLevel = true;
		void Part( Vector3 at, Vector3 size, Color color ) => Shapes.Part( root, "Axe Part", at, Shapes.BoxSize( size.X, size.Y, size.Z ), color, box: true );
		Part( new Vector3( 12f, 0, 0 ), new Vector3( 28f, 1.9f, 1.9f ), new Color( 0.55f, 0.34f, 0.18f ) );
		Part( new Vector3( 2.5f, 0, 0 ), new Vector3( 6f, 2.3f, 2.3f ), new Color( 0.27f, 0.3f, 0.18f ) );
		Part( new Vector3( 26f, 0, 0 ), new Vector3( 4.4f, 2.6f, 4.2f ), new Color( 0.82f, 0.12f, 0.1f ) );
		Part( new Vector3( 26.4f, 0, -4.8f ), new Vector3( 5.6f, 1.4f, 5.6f ), new Color( 0.82f, 0.12f, 0.1f ) );
		Part( new Vector3( 26.6f, 0, -7.6f ), new Vector3( 6.6f, 0.7f, 1.4f ), new Color( 0.85f, 0.87f, 0.9f ) );
		Part( new Vector3( 26f, 0, 3.8f ), new Vector3( 2.8f, 1.6f, 4f ), new Color( 0.3f, 0.32f, 0.35f ) );
		return root;
	}

	// ---- jet feet: the boots fold into little propellers ----

	Node3D BuildProp( string side )
	{
		var root = new Node3D { Name = $"Prop {side}" };
		rigRoot.AddChild( root );
		root.TopLevel = true;
		var spinner = new Node3D { Name = "Spinner" };
		root.AddChild( spinner );
		var metal = new Color( 0.85f, 0.87f, 0.9f );
		var accent = new Color( 0.95f, 0.52f, 0.14f );
		Shapes.Part( root, "Hub", Vector3.Zero, Shapes.Ellipsoid( 3f ), accent, shadow: false );
		for ( var i = 0; i < 3; i++ )
		{
			var yaw = i * 120f;
			var blade = SAng( 0f, yaw, 0f ) * new Vector3( 5.2f, 0f, 0f );
			var piece = Shapes.Part( spinner, "Blade", blade, Vector3.One, metal, box: true, shadow: false );
			piece.Basis = ToG( SAng( 0f, yaw, 0f ) * SAng( 0f, 0f, 18f ) ) * Basis.FromScale( Shapes.BoxSize( 8.5f, 3f, 0.5f ) );
		}
		propSpinners[side] = spinner;
		return root;
	}

	void PlaceProp( string side, Vector3 tipChar, Basis footRot )
	{
		if ( jetBlend <= 0.01f )
		{
			if ( props.TryGetValue( side, out var hidden ) ) hidden.Visible = false;
			return;
		}
		if ( !props.TryGetValue( side, out var prop ) || !IsInstanceValid( prop ) ) props[side] = prop = BuildProp( side );
		prop.Visible = true;
		var open = jetBlend + 0.3f * MathF.Sin( jetBlend * MathF.PI );
		var rot = rigRoot.GlobalBasis.Orthonormalized() * ToG( footRot * SAng( 90f, 0f, 0f ) );
		prop.GlobalTransform = new Transform3D( rot.Scaled( Vector3.One * MathF.Max( 0.001f, open ) ), rigRoot.GlobalTransform * U.Sbox( tipChar ) );
		if ( propSpinners.TryGetValue( side, out var spinner ) ) spinner.Basis = ToG( SAng( 0f, propSpin * (side == "L" ? 1f : -1f), 0f ) );
		if ( jetBlend > 0.5f && timeSincePropBubble > 0.025f )
		{
			if ( side == "R" ) timeSincePropBubble = 0f;
			WaterBubble.Create( prop.GlobalPosition + (Vector3.Down * 3f + Rng.InSphere( 2.5f )) * U.In, Rng.Float( 0.35f, 0.9f ), 0f, Rng.Float( 0.6f, 1.2f ) );
		}
	}
}
