using Godot;

/// <summary>
/// Hooks for anything that adjusts the camera after the controller places it (s&box PlayerController.IEvents.PostCameraSetup).
/// </summary>
public interface ICameraModifier
{
	void PostCameraSetup( Camera3D cam );
}

/// <summary>
/// The player body: a port of the s&box PlayerController with its walk mode (Source-style ground friction and
/// acceleration, air strafing, ducking, coyote jumps, stepping, ground sticking), its eye angles and its camera
/// (first person, or third person pulled back and clipped against the world). Numbers are the s&box ones used by
/// the Player Template and PlayerMovement, in inches, scaled to metres.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
	// ---- Body (inches) ----
	public float BodyRadius = 16f * U.In;
	public float BodyHeight = 72f * U.In;
	public float DuckedHeight = 36f * U.In;
	public float EyeDistanceFromTop = 8f * U.In;

	// ---- Input speeds (PlayerMovement.OnStart: walk = MoveSpeed, run (Shift) = SprintSpeed) ----
	public float WalkSpeed = 320f * U.In;
	public float RunSpeed = 470f * U.In;
	public float DuckedSpeed = 70f * U.In;
	public float JumpSpeed = 300f * U.In;
	public float CoyoteTime = 0.33f;   // the scene value (the s&box default was 0.12)

	// ---- Walk mode ----
	public float GroundAcceleration = 10f;
	public float Friction = 4f;
	public float StopSpeed = 100f * U.In;
	public float AirAcceleration = 10f;
	public float AirSpeedCap = 30f * U.In;
	public float AirFriction = 0.1f;
	public float BrakePower = 1f;
	public float MaxVelocity = 3500f * U.In;
	public float StepHeight = 18f * U.In;
	public float GroundAngle = 45f;
	public float Gravity = U.Gravity;
	/// <summary> Scales gravity (water, jetpack). </summary>
	public float GravityScale = 1f;
	/// <summary> A move that drives the body itself this tick (the Pinball-Bot's roll): returns true when it did. </summary>
	public Func<float, bool> MoveOverride;
	/// <summary> For a move that drives the body itself: whether it is on the ground, and the ground's slope. </summary>
	public void SetGround( bool grounded, Vector3 normal )
	{
		if ( grounded ) { GroundNormal = normal; TimeSinceGrounded = 0f; } else { GroundNormal = Vector3.Up; TimeSinceUngrounded = 0f; }
		IsOnGround = grounded;
	}

	// ---- Look and camera ----
	public bool UseInputControls = true;
	/// <summary> Something else is moving the body (the dock dive): no physics movement while set. </summary>
	public bool Scripted;
	public bool UseLookControls = true;
	public float LookSensitivity = 0.12f;   // degrees per mouse pixel
	public float PitchClamp = 89f;
	public bool ThirdPerson;
	public Vector3 CameraOffset = new( 256f * U.In, 0f, 12f * U.In );   // back, right, up
	public float FieldOfView = 90f;   // horizontal, like s&box

	public Angles EyeAngles;
	/// <summary> Where the movement keys want to go (horizontal, m/s). </summary>
	public Vector3 WishVelocity { get; private set; }
	public bool IsOnGround { get; private set; }
	public Vector3 GroundNormal { get; private set; } = Vector3.Up;
	public bool IsDucking { get; private set; }
	public float CurrentHeight => IsDucking ? DuckedHeight : BodyHeight;
	/// <summary> How far (m) the view sits below the usual eye height (Coral-Bot's burrow). </summary>
	public float EyeDrop;
	/// <summary> Seconds since last standing on the ground (counts up while airborne). </summary>
	public TimeSince TimeSinceGrounded { get; private set; }
	/// <summary> Seconds since last airborne (counts up while grounded). </summary>
	public TimeSince TimeSinceUngrounded { get; private set; }
	public bool CanCoyoteJump => canCoyote && TimeSinceGrounded < CoyoteTime;
	public bool IsDuckTransition { get; private set; }

	public Camera3D Camera { get; private set; }
	public Transform3D EyeTransform { get; private set; }

	/// <summary> Landed: fall distance (m) and the velocity just before touching down. </summary>
	public event Action<float, Vector3> Landed;
	public event Action Jumped;

	/// <summary> Supplies the ground velocity while a slide, dash or carry drives the ground (MoveModeSlide). </summary>
	public Func<Vector3?> GroundOverride;
	/// <summary> When this gives a transform the camera goes there instead of to the eye (the death camera). </summary>
	public Func<float, Transform3D?> CameraOverride;

	// ---- Online (Net) ----
	/// <summary> Another player's robot in an online game: moved by what its owner sends, not by this machine's keys. </summary>
	public bool IsRemote;
	/// <summary> The network peer that owns this player (1 is the host; 0 offline). </summary>
	public long PeerId;
	/// <summary> This player's peer id right now (the local player's is this machine's, even if it went online after the level began). </summary>
	public long NetPeer => IsRemote ? PeerId : Net.Me;
	/// <summary> Where this player's buttons come from: the keyboard and pad, or the network. </summary>
	public PlayerInput In = LocalInput.Instance;
	/// <summary> The robot this player is (the local pick, or what a remote player picked). </summary>
	public RobotClass Robot => IsRemote ? (remoteRobot ?? RobotClass.RamBot) : RobotClass.Current;
	RobotClass remoteRobot;
	public void SetRemoteRobot( RobotClass robot ) => remoteRobot = robot;
	/// <summary> The body, gun and effects are seen from outside: third person, or someone else's robot. </summary>
	public bool SeenFromOutside => ThirdPerson || IsRemote || (Emote != global::Emote.None && !IsRemote);

	// ---- Emotes (PlayerRigEmotes) ----
	/// <summary> The emote playing (the view goes to third person for the local player while it does). </summary>
	public Emote Emote { get; private set; }
	public double EmoteAt { get; private set; }
	/// <summary> Tests: emotes other players played that showed here. </summary>
	public static int RemoteEmotes;
	Vector3 emoteFrom, emoteCam;
	bool emoteCamSet;

	/// <summary> Plays an emote (online the others see it too). Moving or shooting ends it. </summary>
	public void PlayEmote( Emote emote )
	{
		Emote = emote;
		EmoteAt = Clock.Now;
		if ( IsRemote && emote != global::Emote.None ) RemoteEmotes++;
		emoteFrom = GlobalPosition;
		if ( !IsRemote && emote != global::Emote.None ) Net.Instance?.SendEmote( (int)emote );
	}

	void UpdateEmote()
	{
		if ( Emote == global::Emote.None ) return;
		var over = Clock.Now - EmoteAt > Emotes.Length( Emote );
		var moved = !IsRemote && (In.GetVector( "left", "right", "backward", "forward" ) != Vector2.Zero || In.JustPressed( "jump" ) || In.JustPressed( "attack1" ) || In.JustPressed( "attack2" ) || GlobalPosition.DistanceTo( emoteFrom ) > 1.5f);
		var dead = GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) is { IsAlive: false };
		if ( over || moved || dead ) Emote = global::Emote.None;
	}
	/// <summary> A remote player's ground contact, as its owner sent it. </summary>
	public void SetRemoteGround( bool grounded, bool ducking ) { IsOnGround = grounded; IsDucking = ducking; }

	CollisionShape3D shape;
	CapsuleShape3D capsule;
	TimeUntil preventGrounding;
	bool canCoyote;
	bool jumpRequested;
	Vector2 lookDelta;
	float fallStartY;
	float cameraDistance = 100f * U.In;
	float eyeY;

	const float DuckDuration = 0.4f;
	const float UnduckDuration = 0.2f;
	bool duckInput;
	float duckElapsed;

	/// <summary> Where the player is drawn this frame (between physics ticks). Use for anything placed per frame. </summary>
	public Vector3 DrawnPosition
	{
		get
		{
			// Right after a teleport the interpolated spot is still the old one: use where we are now.
			var drawn = GetGlobalTransformInterpolated().Origin;
			return drawn.DistanceTo( GlobalPosition ) > 3f ? GlobalPosition : drawn;
		}
	}

	Vector3 lastTickPosition;

	public override void _Ready()
	{
		PhysicsInterpolationMode = PhysicsInterpolationModeEnum.On;
		capsule = new CapsuleShape3D { Radius = BodyRadius, Height = BodyHeight };
		shape = new CollisionShape3D { Shape = capsule, Position = Vector3.Up * BodyHeight * 0.5f };
		AddChild( shape );

		CollisionLayer = Layers.Player;
		CollisionMask = Layers.World | Layers.Plant;
		FloorMaxAngle = MathX.Rad( GroundAngle );
		FloorSnapLength = StepHeight;
		FloorStopOnSlope = true;
		FloorBlockOnWall = false;
		MaxSlides = 6;
		SafeMargin = 0.004f;

		Camera = new Camera3D { KeepAspect = Camera3D.KeepAspectEnum.Width, Fov = FieldOfView, Near = 0.04f, Far = 3000f };
		Camera.TopLevel = true;
		Camera.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;   // placed every frame from DrawnPosition
		AddChild( Camera );
		if ( !IsRemote ) Camera.MakeCurrent();

		FieldOfView = GameSettings.Fov;
		LookSensitivity = GameSettings.LookSensitivity;
		EyeAngles = new Angles( 0f, MathX.Deg( GlobalRotation.Y ), 0f );
		GlobalRotation = Vector3.Zero;
		if ( IsRemote )
		{
			// Someone else's robot: no physics of its own here (its owner moves it). It keeps the player layer so
			// enemy shots and checks still find it; the local player doesn't collide with that layer.
			CollisionMask = 0;
			return;
		}
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _UnhandledInput( InputEvent e )
	{
		if ( IsRemote ) return;
		if ( e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured )
			lookDelta += motion.Relative;
		// (Esc belongs to the pause menu.) A click takes the mouse back if something else let go of it (alt-tab).
		if ( e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured && !DevMenu.IsOpen && !PauseMenu.IsOpen )
			Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	/// <summary> Ignore the ground for a moment (jumps and launches), like s&box PreventGrounding. </summary>
	public void PreventGrounding( float seconds )
	{
		preventGrounding = MathF.Max( seconds, preventGrounding );
		IsOnGround = false;
		canCoyote = false;
	}

	/// <summary> s&box PlayerController.Jump: adds velocity, cancelling any part that opposes it. </summary>
	public void Jump( Vector3 velocity )
	{
		canCoyote = false;
		PreventGrounding( 0.2f );
		var current = Velocity;
		var dir = velocity.SafeNormal();
		var against = current.Dot( dir );
		if ( against < 0f ) current -= dir * against;
		Velocity = current.AddClamped( velocity, velocity.Length() );
		Jumped?.Invoke();
	}

	/// <summary> The right stick's round dead zone, and how much faster a held full tilt turns. </summary>
	public const float StickDeadzone = 0.12f, StickBoost = 1.8f;
	float fullTilt;

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		// Teleported (spawn, travel, dive): snap the drawn position instead of sliding there.
		if ( GlobalPosition.DistanceTo( lastTickPosition ) > 3f ) ResetPhysicsInterpolation();
		lastTickPosition = GlobalPosition;
		if ( UseLookControls && !IsRemote )
		{
			var e = EyeAngles;
			e.Yaw -= lookDelta.X * LookSensitivity;
			e.Pitch += lookDelta.Y * LookSensitivity;
			// The right stick: a round dead zone (rescaled, so just past it is the slowest turn), a curve so small tilts
			// are fine, and a turn boost: held at full tilt a moment, it turns up to `StickBoost` faster (for turning round).
			var stick = Input.GetVector( "look_left", "look_right", "look_up", "look_down", StickDeadzone );
			if ( stick.Length() > 0.95f ) fullTilt += dt; else fullTilt = 0f;
			var boost = 1f + (StickBoost - 1f) * MathX.Clamp( (fullTilt - 0.18f) / 0.3f, 0f, 1f );
			// Aim assist (controller only): the stick slows near an enemy and the aim is drawn toward it.
			var assist = AimAssist.None;
			if ( Controls.IsController && GetNodeOrNull<WeaponInventory>( "WeaponInventory" )?.Current is { } held )
				assist = AimAssist.Evaluate( this, e, dt, stick != Vector2.Zero, Input.IsActionPressed( "attack1" ), held.AimAmount );
			if ( stick != Vector2.Zero )
			{
				var shaped = stick.Normalized() * MathF.Pow( stick.Length(), 1.6f );
				var zoomScale = GameSettings.LookSensitivity > 0f ? LookSensitivity / GameSettings.LookSensitivity : 1f;
				var speed = 220f * GameSettings.StickSensitivity * zoomScale * assist.Slow;   // degrees a second at full tilt
				e.Yaw -= shaped.X * speed * boost * dt;
				e.Pitch += shaped.Y * speed * 0.8f * dt;   // up and down a little slower than across, as on consoles
			}
			e.Yaw += assist.Pull.X;
			e.Pitch += assist.Pull.Y;
			e.Pitch = MathX.Clamp( e.Pitch, -PitchClamp, PitchClamp );
			e.Roll = 0f;
			EyeAngles = e;
		}
		lookDelta = Vector2.Zero;

		if ( !IsRemote && Input.IsActionJustPressed( "view" ) )
		{
			ThirdPerson = !ThirdPerson;
			cameraDistance = 20f * U.In;
		}

		UpdateEmote();
		UpdateCamera( dt );
	}

	// ---- Movement ---------------------------------------------------------------------------------------

	public override void _PhysicsProcess( double delta )
	{
		var dt = (float)delta;
		if ( IsRemote ) return;   // its owner simulates it (NetPlayer places it)
		if ( Scripted ) { Velocity = Vector3.Zero; IsOnGround = false; return; }
		if ( MoveOverride is not null && MoveOverride( dt ) ) return;
		// Anchor snapped: the float mode moves us (s&box MoveModeFloat outscores the walk mode).
		if ( GetNodeOrNull<MoveModeFloat>( "MoveModeFloat" ) is { Active: true } floating )
		{
			Velocity = floating.Step( dt );
			MoveAndSlide();
			IsOnGround = false;
			return;
		}
		var wasGrounded = IsOnGround;

		// Wish velocity from the movement keys (yaw only), or the active ground move's velocity.
		var input = UseInputControls ? In.GetVector( "left", "right", "backward", "forward" ) : Vector2.Zero;
		var yaw = new Angles( 0f, EyeAngles.Yaw, 0f );
		var direction = (yaw.Forward * input.Y + yaw.Right * input.X).ClampLength( 1f );
		var run = UseInputControls && In.Pressed( "run" );
		var speed = run ? RunSpeed : WalkSpeed;
		if ( IsDucking && IsOnGround ) speed = DuckedSpeed;
		WishVelocity = direction.Length() < 0.1f ? Vector3.Zero : direction * speed;
		var ground = GroundOverride?.Invoke();
		if ( ground is { } g ) WishVelocity = g.Flat();

		UpdateDuck( UseInputControls && In.Pressed( "duck" ), dt );
		if ( UseInputControls && In.JustPressed( "jump" ) ) jumpRequested = true;

		var v = Velocity;
		var wish = WishVelocity;
		var wishSpeed = wish.Length();
		var wishDir = wishSpeed > 0f ? wish / wishSpeed : Vector3.Zero;

		if ( IsOnGround )
		{
			var horizontal = v.Flat();
			horizontal = ApplyFriction( horizontal, Friction * BrakePower, StopSpeed, dt );
			horizontal = Accelerate( horizontal, wishDir, wishSpeed, GroundAcceleration, dt );
			if ( horizontal.Length() < 1f * U.In ) horizontal = Vector3.Zero;
			// Follow the slope we stand on.
			var normalUp = GroundNormal.Y;
			if ( normalUp > 0.01f ) horizontal.Y = -horizontal.Dot( GroundNormal with { Y = 0f } ) / normalUp;
			v = horizontal;
		}
		else
		{
			v = AirAccelerate( v, wishDir, wishSpeed, AirAcceleration, AirSpeedCap, dt );
			v.Y -= Gravity * GravityScale * dt;
			v /= 1f + MathF.Max( AirFriction, 0f ) * dt;
		}

		var jumped = false;
		if ( jumpRequested )
		{
			jumpRequested = false;
			if ( (IsOnGround || CanCoyoteJump) && !(IsDucking && IsDuckTransition) )
			{
				// s&box MovementMath.Jump: ducked jumps set the speed, others add to what you had (0 on the ground).
				v.Y = IsDucking ? JumpSpeed : (IsOnGround ? 0f : v.Y) + JumpSpeed;
				v.Y -= Gravity * GravityScale * dt * 0.5f;   // s&box splits the jump tick's gravity in half (FinishGravity)
				jumped = true;
				canCoyote = false;
				preventGrounding = 0.05f;
				IsOnGround = false;
			}
		}

		v = new Vector3( MathX.Clamp( v.X, -MaxVelocity, MaxVelocity ), MathX.Clamp( v.Y, -MaxVelocity, MaxVelocity ), MathX.Clamp( v.Z, -MaxVelocity, MaxVelocity ) );
		Velocity = v;
		if ( jumped ) Jumped?.Invoke();

		var stickToGround = IsOnGround && preventGrounding <= 0f;
		FloorSnapLength = stickToGround ? StepHeight : 0f;
		if ( !stickToGround ) ApplyFloorSnapOff();

		var before = Velocity;
		var stepped = IsOnGround && TryStepUp( dt );
		if ( stepped )
		{
			var keep = Velocity;
			Velocity = new Vector3( 0f, 0f, 0f );
			MoveAndSlide();
			Velocity = keep;
		}
		else MoveAndSlide();

		var grounded = preventGrounding <= 0f && IsOnFloor();
		if ( grounded )
		{
			GroundNormal = GetFloorNormal();
			if ( !wasGrounded )
			{
				Landed?.Invoke( MathF.Max( 0f, fallStartY - GlobalPosition.Y ), before );
				Velocity = Velocity.Flat();
			}
			TimeSinceGrounded = 0f;
			canCoyote = true;
		}
		else
		{
			if ( wasGrounded || IsOnGround ) fallStartY = GlobalPosition.Y;
			fallStartY = MathF.Max( fallStartY, GlobalPosition.Y );
			TimeSinceUngrounded = 0f;
			GroundNormal = Vector3.Up;
		}
		IsOnGround = grounded;
	}

	void ApplyFloorSnapOff()
	{
		// Leaving the ground on purpose: nothing pulls us back down this tick.
		FloorSnapLength = 0f;
	}

	static Vector3 ApplyFriction( Vector3 velocity, float friction, float stopSpeed, float dt )
	{
		var speed = velocity.Length();
		if ( speed < 0.1f * U.In ) return velocity;
		var control = speed < stopSpeed ? stopSpeed : speed;
		var drop = control * friction * dt;
		var newSpeed = MathF.Max( speed - drop, 0f );
		return newSpeed == speed ? velocity : velocity * (newSpeed / speed);
	}

	static Vector3 Accelerate( Vector3 velocity, Vector3 direction, float wishSpeed, float acceleration, float dt )
	{
		var add = wishSpeed - velocity.Dot( direction );
		if ( add <= 0f ) return velocity;
		var speed = acceleration * dt * wishSpeed;
		return velocity + direction * MathF.Min( speed, add );
	}

	static Vector3 AirAccelerate( Vector3 velocity, Vector3 direction, float wishSpeed, float acceleration, float cap, float dt )
	{
		var add = MathF.Min( wishSpeed, cap ) - velocity.Dot( direction );
		if ( add <= 0f ) return velocity;
		var speed = acceleration * wishSpeed * dt;
		return velocity + direction * MathF.Min( speed, add );
	}

	/// <summary> Blocked by something low while walking: lift up by the step height, move over, settle down onto it. </summary>
	bool TryStepUp( float dt )
	{
		var motion = Velocity.Flat() * dt;
		if ( motion.LengthSquared() < 1e-8f ) return false;
		var from = GlobalTransform;
		var hit = new KinematicCollision3D();
		if ( !TestMove( from, motion, hit ) ) return false;
		if ( MathX.Deg( hit.GetNormal().AngleTo( Vector3.Up ) ) <= GroundAngle ) return false;

		var up = Vector3.Up * StepHeight;
		if ( TestMove( from, up ) ) return false;
		var raised = from.Translated( up );
		if ( TestMove( raised, motion ) ) return false;
		var over = raised.Translated( motion );
		var down = new KinematicCollision3D();
		if ( !TestMove( over, -up * 1.05f, down ) ) return false;
		if ( MathX.Deg( down.GetNormal().AngleTo( Vector3.Up ) ) > GroundAngle ) return false;
		GlobalPosition = over.Origin + down.GetTravel();
		return true;
	}

	// ---- Ducking ----------------------------------------------------------------------------------------

	float DuckInputFraction
	{
		get
		{
			if ( !IsDuckTransition || !IsOnGround ) return IsDucking ? 1f : 0f;
			var t = MathX.Clamp( duckElapsed / (duckInput ? DuckDuration : UnduckDuration), 0f, 1f );
			var f = t * t * (3f - 2f * t);
			return duckInput ? f : 1f - f;
		}
	}

	void UpdateDuck( bool wants, float dt )
	{
		if ( wants != duckInput ) { duckInput = wants; duckElapsed = 0f; }
		else duckElapsed += dt;

		IsDuckTransition = wants != IsDucking;
		if ( !IsDuckTransition ) return;
		if ( !wants && !CanUnduck() ) { duckElapsed = 0f; IsDuckTransition = false; return; }
		if ( IsOnGround && duckElapsed <= (wants ? DuckDuration : UnduckDuration) ) return;
		SetDucking( wants );
		IsDuckTransition = wants != IsDucking;
	}

	bool CanUnduck()
	{
		var target = GlobalPosition;
		if ( !IsOnGround ) target -= Vector3.Up * (BodyHeight - DuckedHeight);
		var full = new CapsuleShape3D { Radius = BodyRadius * 0.98f, Height = BodyHeight - 0.02f };
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = full,
			Transform = new Transform3D( Basis.Identity, target + Vector3.Up * (BodyHeight * 0.5f + 0.01f) ),
			CollisionMask = Layers.World | Layers.Plant,
			Exclude = new Godot.Collections.Array<Rid> { GetRid() },
		};
		return GetWorld3D().DirectSpaceState.IntersectShape( query, 1 ).Count == 0;
	}

	public void SetDucking( bool wants )
	{
		if ( wants == IsDucking ) return;
		var delta = BodyHeight - DuckedHeight;
		if ( !wants && !CanUnduck() ) return;
		IsDucking = wants;
		IsDuckTransition = false;
		if ( !IsOnGround ) GlobalPosition += Vector3.Up * (wants ? delta : -delta);
		capsule.Height = CurrentHeight;
		shape.Position = Vector3.Up * CurrentHeight * 0.5f;
	}

	// ---- Camera -----------------------------------------------------------------------------------------

	void UpdateCamera( float dt )
	{
		var origin = DrawnPosition;
		var height = CurrentHeight;
		if ( IsDuckTransition && IsOnGround ) height = BodyHeight - (BodyHeight - DuckedHeight) * DuckInputFraction;
		var eye = origin + Vector3.Up * (height - EyeDistanceFromTop - EyeDrop);
		if ( IsOnGround && eyeY != 0f ) eye.Y = MathX.Lerp( eyeY, eye.Y, dt * 50f );
		eyeY = eye.Y;

		var basis = EyeAngles.ToBasis();
		EyeTransform = new Transform3D( basis, eye );

		// Emoting: the camera swings round in front of the robot and looks back at it (its aim, and so its facing, stay put).
		var emoting = Emote != global::Emote.None && !IsRemote;
		if ( emoting && CameraOverride is null )
		{
			var face = new Angles( 0f, EyeAngles.Yaw, 0f ).ToBasis();
			var front = eye + (-face.Z * 2.9f + face.X * 0.5f) + Vector3.Up * 0.15f;
			var hit = Trace.Sphere( this, 8f * U.In, eye, front, Layers.World, new Godot.Collections.Array<Rid> { GetRid() } );
			if ( hit.Hit && !hit.StartedSolid ) front = eye + (front - eye).SafeNormal() * MathF.Max( 0.6f, hit.Distance - 0.1f );
			emoteCam = emoteCamSet ? emoteCam.Lerp( front, 1f - MathF.Exp( -8f * dt ) ) : front;
			emoteCamSet = true;
			Camera.GlobalTransform = new Transform3D( Shapes.LookAt( (eye + Vector3.Down * 0.45f - emoteCam).SafeNormal() ), emoteCam );
			Camera.Fov = FieldOfView;
			return;
		}
		emoteCamSet = false;
		if ( ThirdPerson )
		{
			var forward = -basis.Z;
			var cameraDelta = forward * -CameraOffset.X + basis.Y * CameraOffset.Z + basis.X * CameraOffset.Y;
			var tr = Trace.Sphere( this, 8f * U.In, eye, eye + cameraDelta, Layers.World, new Godot.Collections.Array<Rid> { GetRid() } );
			var free = tr.Hit ? tr.Distance : cameraDelta.Length();
			if ( tr.StartedSolid ) cameraDistance = MathX.Lerp( cameraDistance, cameraDelta.Length(), dt * 100f );
			else if ( free < cameraDistance ) cameraDistance = MathX.Lerp( cameraDistance, free, dt * 200f );
			else cameraDistance = MathX.Lerp( cameraDistance, free, dt * 2f );
			eye += cameraDelta.SafeNormal() * cameraDistance;
		}

		Camera.GlobalTransform = CameraOverride?.Invoke( dt ) ?? new Transform3D( basis, eye );
		Camera.Fov = FieldOfView;

		if ( IsRemote ) return;   // a remote player's camera only carries its aim: no shake, sway or underwater wobble
		foreach ( var child in GetChildren() )
			if ( child is ICameraModifier modifier ) modifier.PostCameraSetup( Camera );
	}
}
