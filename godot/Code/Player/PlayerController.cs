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
		Camera.MakeCurrent();

		FieldOfView = GameSettings.Fov;
		LookSensitivity = GameSettings.LookSensitivity;
		EyeAngles = new Angles( 0f, MathX.Deg( GlobalRotation.Y ), 0f );
		GlobalRotation = Vector3.Zero;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _UnhandledInput( InputEvent e )
	{
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

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		// Teleported (spawn, travel, dive): snap the drawn position instead of sliding there.
		if ( GlobalPosition.DistanceTo( lastTickPosition ) > 3f ) ResetPhysicsInterpolation();
		lastTickPosition = GlobalPosition;
		if ( UseLookControls )
		{
			var e = EyeAngles;
			e.Yaw -= lookDelta.X * LookSensitivity;
			e.Pitch += lookDelta.Y * LookSensitivity;
			// The right stick: a curve so small tilts are fine and a full tilt turns fast, slowed along with the mouse when aiming.
			var stick = Input.GetVector( "look_left", "look_right", "look_up", "look_down" );
			// Aim assist (controller only): the stick slows near an enemy and the aim is drawn toward it.
			var assist = AimAssist.None;
			if ( Controls.IsController && GetNodeOrNull<WeaponInventory>( "WeaponInventory" )?.Current is { } held )
				assist = AimAssist.Evaluate( this, e, dt, stick != Vector2.Zero, Input.IsActionPressed( "attack1" ), held.AimAmount );
			if ( stick != Vector2.Zero )
			{
				var shaped = stick.Normalized() * MathF.Pow( stick.Length(), 1.8f );
				var zoomScale = GameSettings.LookSensitivity > 0f ? LookSensitivity / GameSettings.LookSensitivity : 1f;
				var speed = 210f * GameSettings.StickSensitivity * zoomScale * assist.Slow;   // degrees a second at full tilt
				e.Yaw -= shaped.X * speed * dt;
				e.Pitch += shaped.Y * speed * dt;
			}
			e.Yaw += assist.Pull.X;
			e.Pitch += assist.Pull.Y;
			e.Pitch = MathX.Clamp( e.Pitch, -PitchClamp, PitchClamp );
			e.Roll = 0f;
			EyeAngles = e;
		}
		lookDelta = Vector2.Zero;

		if ( Input.IsActionJustPressed( "view" ) )
		{
			ThirdPerson = !ThirdPerson;
			cameraDistance = 20f * U.In;
		}

		UpdateCamera( dt );
	}

	// ---- Movement ---------------------------------------------------------------------------------------

	public override void _PhysicsProcess( double delta )
	{
		var dt = (float)delta;
		if ( Scripted ) { Velocity = Vector3.Zero; IsOnGround = false; return; }
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
		var input = UseInputControls ? Input.GetVector( "left", "right", "backward", "forward" ) : Vector2.Zero;
		var yaw = new Angles( 0f, EyeAngles.Yaw, 0f );
		var direction = (yaw.Forward * input.Y + yaw.Right * input.X).ClampLength( 1f );
		var run = UseInputControls && Input.IsActionPressed( "run" );
		var speed = run ? RunSpeed : WalkSpeed;
		if ( IsDucking && IsOnGround ) speed = DuckedSpeed;
		WishVelocity = direction.Length() < 0.1f ? Vector3.Zero : direction * speed;
		var ground = GroundOverride?.Invoke();
		if ( ground is { } g ) WishVelocity = g.Flat();

		UpdateDuck( UseInputControls && Input.IsActionPressed( "duck" ), dt );
		if ( UseInputControls && Input.IsActionJustPressed( "jump" ) ) jumpRequested = true;

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
		var eye = origin + Vector3.Up * (height - EyeDistanceFromTop);
		if ( IsOnGround && eyeY != 0f ) eye.Y = MathX.Lerp( eyeY, eye.Y, dt * 50f );
		eyeY = eye.Y;

		var basis = EyeAngles.ToBasis();
		EyeTransform = new Transform3D( basis, eye );

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

		foreach ( var child in GetChildren() )
			if ( child is ICameraModifier modifier ) modifier.PostCameraSetup( Camera );
	}
}
