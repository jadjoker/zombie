using Godot;

/// <summary>
/// Port of the s&box PlayerMovement: the movement kit on top of the controller, inspired by Deadlock's. You always
/// run, hold Shift to sprint, and a small pool of stamina pips pays for air jumps. Crouch at speed to slide (faster
/// downhill); jump at a ledge to mantle up onto it; kick off walls. Momentum never snaps: fast landings ease back
/// down to your run (or sprint) speed. Camera feel sells it: speed FOV, slide lean, wall-kick roll, landing dip.
/// (The old dash is still here, reachable only through TestPressDash, as in s&box.)
/// Child of the PlayerController.
/// </summary>
public partial class PlayerMovement : Node, ICameraModifier
{
	// Stamina
	public int MaxStamina = 3;
	public float StaminaRegenTime = 2.4f;
	public float StaminaRegenDelay = 0.6f;

	// Dash (test hook only)
	public float DashSpeed = 720f * U.In;
	public float DashTime = 0.16f;
	public float AirDashSpeed = 600f * U.In;
	public float DashJumpWindow = 0.25f;
	public float DashJumpSpeed = 820f * U.In;
	public float DashJumpUp = 270f * U.In;

	// Double jump
	public int AirJumps = 1;
	public float AirJumpSpeed = 340f * U.In;
	public float AirJumpRedirect = 0.65f;

	// Wall bounce
	public float WallReach = 24f * U.In;
	public float WallJumpUp = 330f * U.In;
	public float WallJumpPush = 360f * U.In;
	public float WallKeepMomentum = 0.9f;
	public float WallJumpCooldown = 0.2f;
	public float WallSlideFallSpeed = 140f * U.In;

	// Mantle
	public float MantleHeight = 95f * U.In;
	public float MantleTime = 0.24f;

	// Slide
	public float SlideMinSpeed = 200f * U.In;
	public float SlideBoost = 150f * U.In;
	public float SlideBoostCooldown = 1.0f;
	public float SlideFriction = 220f * U.In;
	public float SlideSlopeAccel = 900f * U.In;
	public float SlideEndSpeed = 160f * U.In;
	public float SlideMaxTime = 1.4f;
	public float SlideSteer = 2.4f;
	public float SlideJumpBoost = 1.1f;

	// Speed
	public float MoveSpeed = 320f * U.In;
	public float SprintSpeed = 470f * U.In;
	public float MomentumCarryTime = 0.45f;
	public float MaxHorizontalSpeed = 900f * U.In;

	// Feel
	public float SpeedFov = 12f;
	public float SlideTilt = 5f;

	public enum GroundMode { None, Slide, Dash, Carry }

	public GroundMode Ground { get; private set; }
	public bool IsSliding => Ground == GroundMode.Slide;
	public bool IsDashing => Ground == GroundMode.Dash || timeSinceDash < DashTime + 0.1f;
	public bool IsMantling => mantle is not null;
	public bool IsFloating => MoveModeFloat.IsActive && (Controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.IsAlive ?? true);

	/// <summary> A burst wherever you're swimming (or looking), which the water then eases back out. </summary>
	void FloatDash()
	{
		if ( timeSinceDash < DashTime + 0.05f ) return;
		if ( !TrySpend() ) return;
		var swim = Controller.GetNodeOrNull<MoveModeFloat>( "MoveModeFloat" )?.WishSwim() ?? Vector3.Zero;
		dashDirection = swim.IsNearlyZero() ? -Controller.EyeAngles.ToBasis().Z : swim.Normalized();
		timeSinceDash = 0f;
		dashFromGround = false;
		Controller.Velocity = dashDirection * AirDashSpeed * 1.1f;
		Kick( 7f );
		Juice?.Shake( 0.06f );
	}
	public bool OverridesGround => Ground != GroundMode.None;
	public Vector3 GroundVelocity { get; private set; }

	public int AirJumpsLeft { get; private set; }
	public float Stamina { get; private set; }
	public TimeSince TimeSinceStaminaSpent { get; private set; } = 999f;
	public TimeSince TimeSinceOutOfStamina { get; private set; } = 999f;

	/// <summary> Testing aid: holds crouch without the key. </summary>
	public bool ForceCrouch;
	bool CrouchHeld => Input.IsActionPressed( "duck" ) || ForceCrouch;
	public bool IsSprinting => Input.IsActionPressed( "run" );
	float TopSpeed => IsSprinting ? SprintSpeed : MoveSpeed;

	/// <summary> Set by the jetpack later: a surge lifts the speed cap. </summary>
	public Func<bool> SpeedCapLifted;

	public float HorizontalSpeed => Controller.Velocity.Flat().Length();

	PlayerController Controller;
	CameraJuice Juice => Controller.GetNodeOrNull<CameraJuice>( "CameraJuice" );

	TimeSince timeSinceGroundMode;
	TimeSince timeSinceSlideBoost = 999f;
	TimeSince timeSinceWallJump = 999f;
	TimeSince timeSinceDash = 999f;
	TimeSince timeSinceDust;
	Vector3 dashDirection;
	bool dashFromGround;
	float carryFrom;
	bool wasOnGround = true;

	sealed class Mantle
	{
		public Vector3 From;
		public Vector3 To;
		public Vector3 Forward;
		public TimeSince Time;
	}
	Mantle mantle;

	float fovBoost;
	float fovKick;
	float tilt;
	float wallRoll;
	float landDip;

	public override void _Ready()
	{
		Name = "PlayerMovement";
		Controller = GetParent<PlayerController>();
		Controller.CoyoteTime = 0.12f;
		Controller.RunSpeed = SprintSpeed;
		Controller.WalkSpeed = MoveSpeed;
		Controller.GroundOverride = () => OverridesGround ? GroundVelocity : null;
		Controller.Landed += OnLanded;
		Controller.Jumped += () => Sfx.Play( "jump", Controller.GlobalPosition, 0.6f );
		AirJumpsLeft = AirJumps;
		Stamina = MaxStamina;
		ProcessPhysicsPriority = -10;   // runs before the controller moves, like s&box OnFixedUpdate before the physics step
	}

	// ---- Stamina ----

	bool TrySpend( int pips = 1 )
	{
		if ( Stamina < pips - 0.001f ) { TimeSinceOutOfStamina = 0f; return false; }
		Stamina -= pips;
		TimeSinceStaminaSpent = 0f;
		return true;
	}

	void RegenStamina( float dt )
	{
		if ( TimeSinceStaminaSpent < StaminaRegenDelay ) return;
		Stamina = MathF.Min( MaxStamina, Stamina + dt / MathF.Max( 0.05f, StaminaRegenTime ) );
	}

	public void ResetStamina() => Stamina = MaxStamina;

	// ---- Input (s&box OnUpdate) ----

	public override void _Process( double delta )
	{
		var controller = Controller;
		var onGround = controller.IsOnGround;
		RegenStamina( (float)delta );
		if ( onGround ) AirJumpsLeft = AirJumps;
		if ( mantle is not null ) { wasOnGround = onGround; return; }

		// Anchor snapped: you're swimming. Only the dash still works (in 3D).
		if ( IsFloating )
		{
			if ( testDash ) FloatDash();
			testDash = false;
			testJump = false;
			wasOnGround = onGround;
			return;
		}

		var jumpPressed = Input.IsActionJustPressed( "jump" ) || testJump;
		var dashPressed = testDash;
		testJump = false;
		testDash = false;

		var airborne = !onGround && controller.TimeSinceGrounded > 0.1f;

		if ( dashPressed ) TryDash( onGround );

		if ( jumpPressed )
		{
			if ( dashFromGround && timeSinceDash < DashJumpWindow && (onGround || controller.TimeSinceGrounded < 0.15f) ) DashJump();
			else if ( IsSliding && onGround ) SlideJump();
			else
			{
				var wall = timeSinceWallJump > WallJumpCooldown ? FindWall() : null;
				if ( airborne && FindLedge() is { } ledge ) StartMantle( ledge );
				else if ( wall is { } w && (airborne || (onGround && IsPushingInto( w.Normal ))) ) pendingWallKick = w;
				else if ( airborne ) TryAirJump();
			}
		}

		if ( airborne && mantle is null && controller.Velocity.Y < 120f * U.In && IsPushingForward() && FindLedge() is { } reach )
			StartMantle( reach );

		var justLanded = onGround && !wasOnGround;
		if ( Ground != GroundMode.Slide && onGround && (Input.IsActionJustPressed( "duck" ) || ForceCrouchPressed() || (justLanded && CrouchHeld)) )
			TryStartSlide();

		if ( IsSliding && (!CrouchHeld || !onGround || timeSinceGroundMode > SlideMaxTime || GroundVelocity.Length() < SlideEndSpeed) )
			EndGroundMode();

		if ( OverridesGround && !onGround ) EndGroundMode();

		if ( justLanded && Ground == GroundMode.None && HorizontalSpeed > TopSpeed + 40f * U.In )
			StartCarry( controller.Velocity.Flat() );

		wasOnGround = onGround;
	}

	bool forceCrouchWas;
	bool ForceCrouchPressed()
	{
		var pressed = ForceCrouch && !forceCrouchWas;
		forceCrouchWas = ForceCrouch;
		return pressed;
	}

	// ---- Physics (s&box OnFixedUpdate) ----

	public override void _PhysicsProcess( double delta )
	{
		var dt = (float)delta;
		var controller = Controller;
		if ( mantle is not null ) { StepMantle(); return; }
		if ( IsFloating ) return;

		if ( pendingWallKick is { } kick ) { pendingWallKick = null; WallJump( kick ); }

		switch ( Ground )
		{
			case GroundMode.Slide: StepSlide( dt ); break;
			case GroundMode.Dash: StepDash( dt ); break;
			case GroundMode.Carry: StepCarry( dt ); break;
		}

		var v = controller.Velocity;
		if ( !controller.IsOnGround && v.Y < -WallSlideFallSpeed && FindWall() is { } wall
			&& controller.WishVelocity.Flat().SafeNormal().Dot( -wall.Normal ) > 0.4f )
			controller.Velocity = v.WithY( MathX.Lerp( v.Y, -WallSlideFallSpeed, dt * 10f ) );

		var horizontal = controller.Velocity.Flat();
		var lifted = SpeedCapLifted?.Invoke() ?? false;
		if ( !lifted && horizontal.Length() > MaxHorizontalSpeed )
			controller.Velocity = horizontal.Normalized() * MaxHorizontalSpeed + Vector3.Up * controller.Velocity.Y;
	}

	// ---- Dash ----

	Vector3 WishDirection()
	{
		var wish = Controller.WishVelocity.Flat();
		return wish.IsNearlyZero() ? Controller.EyeAngles.WithPitch( 0f ).Forward : wish.Normalized();
	}

	void TryDash( bool onGround )
	{
		if ( timeSinceDash < DashTime + 0.05f ) return;
		if ( !TrySpend() ) return;
		dashDirection = WishDirection();
		timeSinceDash = 0f;
		dashFromGround = onGround;
		var c = Controller;
		if ( onGround )
		{
			var speed = MathF.Max( DashSpeed, IsSliding ? GroundVelocity.Length() : 0f );
			SetGroundMode( GroundMode.Dash, dashDirection * speed );
			c.Velocity = dashDirection * speed + Vector3.Up * c.Velocity.Y;
		}
		else
		{
			c.Velocity = dashDirection * MathF.Max( AirDashSpeed, c.Velocity.Flat().Length() * 0.8f ) + Vector3.Up * MathF.Max( c.Velocity.Y, 40f * U.In );
			c.PreventGrounding( 0.05f );
		}
		Kick( 7f );
		Juice?.Shake( 0.06f );
	}

	void StepDash( float dt )
	{
		var speed = GroundVelocity.Length();
		var direction = GroundVelocity.SafeNormal().Lerp( WishDirection(), dt * 3f ).SafeNormal();
		GroundVelocity = direction * speed;
		if ( timeSinceGroundMode > DashTime )
		{
			if ( CrouchHeld ) TryStartSlide();
			else StartCarry( GroundVelocity );
		}
	}

	void DashJump()
	{
		var bonus = TrySpend() ? DashJumpSpeed : DashSpeed * 0.9f;
		EndGroundMode( handBack: false );
		Controller.Velocity = dashDirection * MathF.Min( bonus, MaxHorizontalSpeed ) + Vector3.Up * DashJumpUp;
		Controller.PreventGrounding( 0.15f );
		dashFromGround = false;
		Kick( 6f );
		Juice?.Shake( 0.12f );
	}

	// ---- Momentum carry ----

	void StartCarry( Vector3 velocity )
	{
		carryFrom = velocity.Length();
		if ( carryFrom <= TopSpeed + 10f * U.In ) { EndGroundMode(); return; }
		SetGroundMode( GroundMode.Carry, velocity );
	}

	void StepCarry( float dt )
	{
		var t = MathX.Clamp( timeSinceGroundMode / MathF.Max( 0.05f, MomentumCarryTime ), 0f, 1f );
		var speed = MathX.LerpUnclamped( carryFrom, TopSpeed, 1f - (1f - t) * (1f - t) );
		var wish = Controller.WishVelocity.Flat();
		var direction = GroundVelocity.SafeNormal();
		if ( wish.IsNearlyZero() ) speed *= 1f - t * 0.6f;
		else direction = direction.Lerp( wish.Normalized(), dt * 5f ).SafeNormal();
		GroundVelocity = direction * speed;
		if ( t >= 1f ) EndGroundMode();
	}

	// ---- Air moves ----

	bool TryAirJump()
	{
		if ( AirJumpsLeft <= 0 ) return false;
		if ( !TrySpend() ) return false;
		AirJumpsLeft--;
		var c = Controller;
		var horizontal = c.Velocity.Flat();
		var wish = c.WishVelocity.Flat();
		if ( !wish.IsNearlyZero() )
		{
			var speed = MathF.Max( horizontal.Length(), MoveSpeed );
			horizontal = horizontal.Lerp( wish.Normalized() * speed, AirJumpRedirect );
		}
		c.Velocity = horizontal + Vector3.Up * AirJumpSpeed;
		c.PreventGrounding( 0.1f );
		Kick( 1.5f );
		return true;
	}

	public void TestPressJump() => testJump = true;
	public void TestPressDash() => testDash = true;
	bool testJump;
	bool testDash;
	(Vector3 Position, Vector3 Normal)? pendingWallKick;

	bool IsPushingInto( Vector3 normal )
	{
		var wish = Controller.WishVelocity.Flat();
		var look = Controller.EyeAngles.WithPitch( 0f ).Forward;
		return (!wish.IsNearlyZero() && wish.Normalized().Dot( -normal ) > 0.3f) || look.Dot( -normal ) > 0.5f;
	}

	bool IsPushingForward()
	{
		var wish = Controller.WishVelocity.Flat();
		var look = Controller.EyeAngles.WithPitch( 0f ).Forward;
		return !wish.IsNearlyZero() && wish.Normalized().Dot( look ) > 0.5f;
	}

	void WallJump( (Vector3 Position, Vector3 Normal) wall )
	{
		var c = Controller;
		var velocity = c.Velocity;
		var along = (velocity - wall.Normal * velocity.Dot( wall.Normal )).Flat() * WallKeepMomentum;
		var wish = c.WishVelocity.Flat();
		if ( !wish.IsNearlyZero() )
		{
			var steer = wish.Normalized() - wall.Normal * wish.Normalized().Dot( wall.Normal );
			along += steer * 80f * U.In;
		}
		EndGroundMode( handBack: false );
		c.Velocity = along + wall.Normal * WallJumpPush + Vector3.Up * WallJumpUp;
		c.PreventGrounding( 0.15f );
		timeSinceWallJump = 0f;
		AirJumpsLeft = AirJumps;

		var right = c.EyeAngles.WithPitch( 0f ).Right;
		wallRoll = wall.Normal.Dot( right ) * -9f;
		Kick( 3f );
		Juice?.Shake( 0.15f );
	}

	Godot.Collections.Array<Rid> Self => new() { Controller.GetRid() };

	(Vector3 Position, Vector3 Normal)? FindWall()
	{
		var c = Controller;
		var center = c.GlobalPosition + Vector3.Up * (c.CurrentHeight * 0.5f);
		var reach = c.BodyRadius + WallReach;
		(Vector3, Vector3)? best = null;
		var bestDistance = float.MaxValue;
		for ( var i = 0; i < 12; i++ )
		{
			var dir = new Angles( 0f, i * 30f, 0f ).Forward;
			var tr = Trace.Ray( c, center, center + dir * reach, Layers.World, Self );
			if ( !tr.Hit || MathF.Abs( tr.Normal.Y ) > 0.35f ) continue;
			if ( tr.Distance < bestDistance )
			{
				bestDistance = tr.Distance;
				best = (tr.HitPosition, tr.Normal.Flat().SafeNormal());
			}
		}
		return best;
	}

	// ---- Mantle ----

	(Vector3 Top, Vector3 Forward)? FindLedge()
	{
		var c = Controller;
		var forward = c.EyeAngles.WithPitch( 0f ).Forward;
		var feet = c.GlobalPosition;
		var radius = c.BodyRadius;

		TraceResult wall = default;
		foreach ( var probeHeight in new[] { 15f * U.In, 40f * U.In } )
		{
			var from = feet + Vector3.Up * probeHeight;
			wall = Trace.Ray( c, from, from + forward * (radius + 40f * U.In), Layers.World, Self );
			if ( wall.Hit && MathF.Abs( wall.Normal.Y ) <= 0.4f ) break;
		}
		if ( !wall.Hit || MathF.Abs( wall.Normal.Y ) > 0.4f ) return null;

		var probe = wall.HitPosition + forward * (radius + 4f * U.In);
		var down = Trace.Ray( c, probe.WithY( feet.Y + MantleHeight + 4f * U.In ), probe.WithY( feet.Y + 8f * U.In ), Layers.World, Self );
		if ( !down.Hit || down.Normal.Y < 0.7f || down.StartedSolid ) return null;
		var top = down.HitPosition;
		if ( top.Y - feet.Y < 18f * U.In ) return null;

		var height = c.BodyHeight;
		var box = new BoxShape3D { Size = new Vector3( radius * 2f, height, radius * 2f ) };
		var start = top + Vector3.Up * (2f * U.In + height * 0.5f);
		var room = Trace.Shape( c, box, start, start + Vector3.Up * 1f * U.In, Layers.World, Self );
		if ( room.Hit || room.StartedSolid ) return null;

		return (top + Vector3.Up * 1f * U.In, forward);
	}

	void StartMantle( (Vector3 Top, Vector3 Forward) ledge )
	{
		EndGroundMode( handBack: false );
		mantle = new Mantle { From = Controller.GlobalPosition, To = ledge.Top, Forward = ledge.Forward, Time = 0f };
		AirJumpsLeft = AirJumps;
		landDip = MathF.Max( landDip, 3f );
		Kick( 2f );
	}

	void StepMantle()
	{
		var c = Controller;
		var t = MathX.Clamp( mantle.Time / MathF.Max( 0.05f, MantleTime ), 0f, 1f );
		var up = 1f - (1f - t) * (1f - t);
		var over = t * t;
		c.GlobalPosition = new Vector3(
			MathX.LerpUnclamped( mantle.From.X, mantle.To.X, over ),
			MathX.LerpUnclamped( mantle.From.Y, mantle.To.Y + 4f * U.In, up ),
			MathX.LerpUnclamped( mantle.From.Z, mantle.To.Z, over ) );
		c.Velocity = Vector3.Zero;
		c.PreventGrounding( 0.05f );
		if ( t >= 1f )
		{
			c.Velocity = mantle.Forward * MoveSpeed * 0.8f + Vector3.Up * 60f * U.In;
			mantle = null;
		}
	}

	// ---- Slide ----

	void TryStartSlide()
	{
		var horizontal = OverridesGround ? GroundVelocity : Controller.Velocity.Flat();
		if ( horizontal.Length() < SlideMinSpeed ) return;
		var speed = horizontal.Length();
		if ( timeSinceSlideBoost > SlideBoostCooldown && Ground != GroundMode.Dash )
		{
			speed += SlideBoost;
			timeSinceSlideBoost = 0f;
			Kick( 2.5f );
		}
		SetGroundMode( GroundMode.Slide, horizontal.Normalized() * MathF.Min( speed, MaxHorizontalSpeed ) );
	}

	void StepSlide( float dt )
	{
		var direction = GroundVelocity.SafeNormal();
		var speed = GroundVelocity.Length();

		var normal = Controller.GroundNormal;
		var downhill = (Vector3.Down - normal * Vector3.Down.Dot( normal )).Flat();
		var slope = downhill.Length();
		var alongSlope = slope > 0.02f ? direction.Dot( downhill.Normalized() ) : 0f;
		speed += SlideSlopeAccel * slope * alongSlope * dt;
		speed -= SlideFriction * (alongSlope > 0.2f ? 0.3f : 1f) * dt;
		speed = MathX.Clamp( speed, 0f, MaxHorizontalSpeed );

		var wish = Controller.WishVelocity.Flat();
		if ( !wish.IsNearlyZero() )
			direction = direction.Lerp( wish.Normalized(), dt * SlideSteer ).SafeNormal();

		GroundVelocity = direction * speed;
		if ( alongSlope > 0.3f && slope > 0.1f ) timeSinceGroundMode = MathF.Min( timeSinceGroundMode, SlideMaxTime * 0.5f );
	}

	void SlideJump()
	{
		var carried = GroundVelocity * SlideJumpBoost;
		EndGroundMode( handBack: false );
		Controller.Velocity = carried.ClampLength( MaxHorizontalSpeed ) + Vector3.Up * Controller.JumpSpeed;
		Controller.PreventGrounding( 0.1f );
		Kick( 2f );
	}

	// ---- Bookkeeping ----

	void SetGroundMode( GroundMode mode, Vector3 velocity )
	{
		Ground = mode;
		GroundVelocity = velocity;
		timeSinceGroundMode = 0f;
	}

	void EndGroundMode( bool handBack = true )
	{
		if ( Ground == GroundMode.None ) return;
		Ground = GroundMode.None;
		if ( handBack ) Controller.Velocity = GroundVelocity + Vector3.Up * Controller.Velocity.Y;
	}

	// ---- Feel ----

	void OnLanded( float distance, Vector3 impactVelocity )
	{
		var hardness = MathX.Clamp( -impactVelocity.Y / (700f * U.In), 0f, 1f );
		if ( hardness > 0.1f ) Sfx.Play( "land", Controller.GlobalPosition, hardness );
		landDip = MathF.Max( landDip, hardness * 6f );
		if ( hardness > 0.25f ) Juice?.Shake( hardness * 0.3f );
	}

	public void PostCameraSetup( Camera3D cam )
	{
		var dt = (float)GetProcessDeltaTime();
		var speed = OverridesGround ? GroundVelocity.Length() : HorizontalSpeed;
		var overRun = MathX.Clamp( (speed - MoveSpeed) / (350f * U.In), 0f, 1f );
		var targetFov = overRun * SpeedFov + (IsSliding ? 4f : 0f);
		fovKick = MathX.Lerp( fovKick, 0f, dt * 4f );
		fovBoost = MathX.Lerp( fovBoost, targetFov + fovKick, dt * 7f );

		var targetTilt = 0f;
		var right = Controller.EyeAngles.WithPitch( 0f ).Right;
		if ( IsSliding ) targetTilt = -GroundVelocity.SafeNormal().Dot( right ) * SlideTilt - SlideTilt * 0.4f;
		else if ( timeSinceDash < DashTime + 0.15f ) targetTilt = -dashDirection.Dot( right ) * 3f;
		tilt = MathX.Lerp( tilt, targetTilt, dt * 9f );
		wallRoll = MathX.Lerp( wallRoll, 0f, dt * 5f );
		landDip = MathX.Lerp( landDip, 0f, dt * 7f );

		cam.Fov += fovBoost;
		cam.GlobalBasis = cam.GlobalBasis * new Angles( landDip, 0f, tilt + wallRoll ).ToBasis();
		cam.GlobalPosition += Vector3.Down * landDip * 0.6f * U.In;

		// Dev menu camera tools.
		if ( DevFlags.Fov > 0f ) cam.Fov = DevFlags.Fov;
		if ( DevFlags.FreeCam ) cam.GlobalTransform = new Transform3D( DevFlags.FreeCamAngles.ToBasis(), DevFlags.FreeCamPosition );
	}

	void Kick( float fov ) => fovKick = MathF.Max( fovKick, fov );
}
