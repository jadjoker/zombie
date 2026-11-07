/// <summary>
/// The movement kit on top of the PlayerController, inspired by Deadlock's: you always run, hold Shift
/// to sprint for as long as you like, and a small pool of stamina pips pays for air jumps. Crouch at
/// speed to slide (faster downhill); jump at a ledge to mantle up onto it; kick off walls. Momentum
/// never snaps: fast landings ease back down to your run (or sprint) speed.
/// (The old Shift dash code is still here, reachable only through the TestPressDash hook.)
///
/// Camera feel sells it: speed FOV, dash kick, slide lean, wall-kick roll, landing dip.
/// Sits on the player next to the controller and MoveModeSlide (which drives the ground while a dash,
/// slide or momentum carry is active).
/// </summary>
public sealed class PlayerMovement : Component, PlayerController.IEvents
{
	[Property, Group( "Stamina" )] public int MaxStamina { get; set; } = 3;
	/// <summary> Seconds to refill one pip. </summary>
	[Property, Group( "Stamina" )] public float StaminaRegenTime { get; set; } = 2.4f;
	/// <summary> Pause after spending stamina before it starts coming back. </summary>
	[Property, Group( "Stamina" )] public float StaminaRegenDelay { get; set; } = 0.6f;

	[Property, Group( "Dash" )] public float DashSpeed { get; set; } = 720f;
	/// <summary> How long the dash holds full speed before easing back down. </summary>
	[Property, Group( "Dash" )] public float DashTime { get; set; } = 0.16f;
	[Property, Group( "Dash" )] public float AirDashSpeed { get; set; } = 600f;
	/// <summary> Jump within this long of a ground dash for a dash-jump. </summary>
	[Property, Group( "Dash" )] public float DashJumpWindow { get; set; } = 0.25f;
	[Property, Group( "Dash" )] public float DashJumpSpeed { get; set; } = 820f;
	[Property, Group( "Dash" )] public float DashJumpUp { get; set; } = 270f;

	[Property, Group( "Double Jump" )] public int AirJumps { get; set; } = 1;
	[Property, Group( "Double Jump" )] public float AirJumpSpeed { get; set; } = 340f;
	/// <summary> How much an air jump swings your momentum toward where you're steering (0 to 1). </summary>
	[Property, Group( "Double Jump" )] public float AirJumpRedirect { get; set; } = 0.65f;

	[Property, Group( "Wall Bounce" )] public float WallReach { get; set; } = 24f;
	[Property, Group( "Wall Bounce" )] public float WallJumpUp { get; set; } = 330f;
	[Property, Group( "Wall Bounce" )] public float WallJumpPush { get; set; } = 360f;
	/// <summary> Share of your speed along the wall that survives the kick. </summary>
	[Property, Group( "Wall Bounce" )] public float WallKeepMomentum { get; set; } = 0.9f;
	[Property, Group( "Wall Bounce" )] public float WallJumpCooldown { get; set; } = 0.2f;
	/// <summary> Fastest you fall while pushing into a wall. </summary>
	[Property, Group( "Wall Bounce" )] public float WallSlideFallSpeed { get; set; } = 140f;

	/// <summary> Highest ledge (above your feet) you can mantle onto. </summary>
	[Property, Group( "Mantle" )] public float MantleHeight { get; set; } = 95f;
	[Property, Group( "Mantle" )] public float MantleTime { get; set; } = 0.24f;

	[Property, Group( "Slide" )] public float SlideMinSpeed { get; set; } = 200f;
	[Property, Group( "Slide" )] public float SlideBoost { get; set; } = 150f;
	[Property, Group( "Slide" )] public float SlideBoostCooldown { get; set; } = 1.0f;
	/// <summary> Speed lost per second while sliding on flat ground. </summary>
	[Property, Group( "Slide" )] public float SlideFriction { get; set; } = 220f;
	/// <summary> Speed gained per second sliding down a steep slope. </summary>
	[Property, Group( "Slide" )] public float SlideSlopeAccel { get; set; } = 900f;
	[Property, Group( "Slide" )] public float SlideEndSpeed { get; set; } = 160f;
	[Property, Group( "Slide" )] public float SlideMaxTime { get; set; } = 1.4f;
	/// <summary> How quickly you can carve the slide toward your input, in turns per second-ish. </summary>
	[Property, Group( "Slide" )] public float SlideSteer { get; set; } = 2.4f;
	[Property, Group( "Slide" )] public float SlideJumpBoost { get; set; } = 1.1f;

	/// <summary> Base move speed (always running, like Deadlock). Alt walks. </summary>
	[Property, Group( "Speed" )] public float MoveSpeed { get; set; } = 320f;
	/// <summary> Holding Shift: an endless sprint, no stamina. </summary>
	[Property, Group( "Speed" )] public float SprintSpeed { get; set; } = 470f;
	public bool IsSprinting => Input.Down( "Run" ) && !IsProxy;
	/// <summary> The speed you settle back to after a fast move: sprint speed while Shift is held. </summary>
	float TopSpeed => IsSprinting ? SprintSpeed : MoveSpeed;
	/// <summary> Seconds to ease from a fast landing or dash back down to run speed. </summary>
	[Property, Group( "Speed" )] public float MomentumCarryTime { get; set; } = 0.45f;
	[Property, Group( "Speed" )] public float MaxHorizontalSpeed { get; set; } = 900f;

	[Property, Group( "Feel" )] public float SpeedFov { get; set; } = 12f;
	[Property, Group( "Feel" )] public float SlideTilt { get; set; } = 5f;
	[Property, Group( "Feel" )] public GameObject DustEffect { get; set; }
	[Property, Group( "Feel" )] public SoundEvent JumpSound { get; set; }
	[Property, Group( "Feel" )] public SoundEvent LandSound { get; set; }

	public enum GroundMode { None, Slide, Dash, Carry }

	/// <summary> Which move is steering you on the ground right now (None = normal walking). </summary>
	public GroundMode Ground { get; private set; }
	public bool IsSliding => Ground == GroundMode.Slide;
	public bool IsDashing => Ground == GroundMode.Dash || timeSinceDash < DashTime + 0.1f;
	public bool IsMantling => mantle is not null;
	/// <summary> True while MoveModeSlide should drive the ground with GroundVelocity. </summary>
	public bool OverridesGround => Ground != GroundMode.None;
	/// <summary> What MoveModeSlide feeds the controller while a ground move is active. </summary>
	public Vector3 GroundVelocity { get; private set; }

	public int AirJumpsLeft { get; private set; }
	/// <summary> Stamina pips, fractional while refilling (0 to MaxStamina). </summary>
	public float Stamina { get; private set; }
	public TimeSince TimeSinceStaminaSpent { get; private set; } = 999f;
	/// <summary> Set when a move was refused for lack of stamina, so the HUD can flash the pips. </summary>
	public TimeSince TimeSinceOutOfStamina { get; private set; } = 999f;

	/// <summary> Testing aid: holds crouch without the key (scripts can't fake input). </summary>
	public bool ForceCrouch { get; set; }
	bool CrouchHeld => Input.Down( "Duck" ) || ForceCrouch;

	public float HorizontalSpeed => Controller.IsValid() ? Controller.Velocity.WithZ( 0 ).Length : 0f;

	PlayerController Controller => GetComponent<PlayerController>();
	Rigidbody Body => Controller?.Body;

	TimeSince timeSinceGroundMode;
	TimeSince timeSinceSlideBoost = 999f;
	TimeSince timeSinceWallJump = 999f;
#pragma warning disable SB3003 // Owner-local dash timer; a reset on host migration is harmless.
	TimeSince timeSinceDash = 999f;
#pragma warning restore SB3003
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

	// Camera feel state.
	float fovBoost;
	/// <summary> Short-lived FOV impulse from moves; the view eases toward it instead of snapping. </summary>
	float fovKick;
	float tilt;
	float wallRoll;
	float landDip;

	protected override void OnStart()
	{
		var controller = Controller;
		if ( !controller.IsValid() ) return;

		// Run by default; hold Shift to sprint (as long as you like).
		controller.CoyoteTime = 0.12f;
		controller.RunByDefault = false;
		controller.AltMoveButton = "Run";
		controller.RunSpeed = SprintSpeed;
		controller.WalkSpeed = MoveSpeed;

		AirJumpsLeft = AirJumps;
		Stamina = MaxStamina;
	}

	bool CanAct => !IsProxy && !DevFlags.Noclip && Controller.IsValid() && Body.IsValid()
		&& (GetComponent<PlayerSurvivor>()?.IsAlive ?? true);

	// ---- Stamina ----------------------------------------------------------------

	bool TrySpend( int pips = 1 )
	{
		if ( Stamina < pips - 0.001f )
		{
			TimeSinceOutOfStamina = 0f;
			return false;
		}
		Stamina -= pips;
		TimeSinceStaminaSpent = 0f;
		return true;
	}

	void RegenStamina()
	{
		if ( TimeSinceStaminaSpent < StaminaRegenDelay ) return;
		Stamina = MathF.Min( MaxStamina, Stamina + Time.Delta / MathF.Max( 0.05f, StaminaRegenTime ) );
	}

	/// <summary> Refills stamina (respawning). </summary>
	public void ResetStamina() => Stamina = MaxStamina;

	// ---- Input ----------------------------------------------------------------

	protected override void OnUpdate()
	{
		if ( !CanAct ) return;
		var controller = Controller;
		var onGround = controller.IsOnGround;

		RegenStamina();
		if ( onGround ) AirJumpsLeft = AirJumps;
		if ( mantle is not null ) { wasOnGround = onGround; return; }

		// Anchor snapped: you're swimming. Only the dash still works (in 3D).
		if ( IsFloating )
		{
			EndGroundMode( handBack: false );
			if ( testDash ) FloatDash();
			testDash = false;
			testJump = false;
			wasOnGround = onGround;
			return;
		}

		var jumpPressed = Input.Pressed( "Jump" ) || testJump;
		var dashPressed = testDash; // Shift is a held sprint now; dashes only via test hooks.
		testJump = false;
		testDash = false;

		// Note: TimeSinceGrounded counts up while airborne (TimeSinceUngrounded resets every airborne frame).
		var airborne = !onGround && controller.TimeSinceGrounded > 0.1f;

		if ( dashPressed ) TryDash( onGround );

		if ( jumpPressed )
		{
			if ( dashFromGround && timeSinceDash < DashJumpWindow && (onGround || controller.TimeSinceGrounded < 0.15f) )
			{
				DashJump();
			}
			else if ( IsSliding && onGround )
			{
				SlideJump();
			}
			else
			{
				var wall = timeSinceWallJump > WallJumpCooldown ? FindWall() : null;
				if ( airborne && FindLedge() is { } ledge ) StartMantle( ledge );
				else if ( wall is { } w && (airborne || (onGround && IsPushingInto( w.Normal ))) ) pendingWallKick = w;
				else if ( airborne ) TryAirJump();
			}
		}

		// Holding forward into a ledge while falling past it climbs it too.
		if ( airborne && mantle is null && Body.Velocity.z < 120f && IsPushingForward() && FindLedge() is { } reach )
			StartMantle( reach );

		// Slide: crouch while moving fast, or land while holding crouch.
		var justLanded = onGround && !wasOnGround;
		if ( Ground != GroundMode.Slide && onGround && (Input.Pressed( "Duck" ) || ForceCrouchPressed() || (justLanded && CrouchHeld)) )
			TryStartSlide();

		if ( IsSliding && (!CrouchHeld || !onGround || timeSinceGroundMode > SlideMaxTime || GroundVelocity.Length < SlideEndSpeed) )
			EndGroundMode();

		// Ground moves only live on the ground; in the air the body carries the momentum.
		if ( OverridesGround && !onGround ) EndGroundMode();

		// Landing faster than a run eases down instead of snapping to run speed.
		if ( justLanded && Ground == GroundMode.None && HorizontalSpeed > TopSpeed + 40f )
			StartCarry( Controller.Velocity.WithZ( 0 ) );

		wasOnGround = onGround;
	}

	bool forceCrouchWas;
	bool ForceCrouchPressed()
	{
		var pressed = ForceCrouch && !forceCrouchWas;
		forceCrouchWas = ForceCrouch;
		return pressed;
	}

	protected override void OnFixedUpdate()
	{
		if ( !CanAct ) return;
		if ( IsFloating ) return;
		var controller = Controller;
		var body = Body;

		if ( mantle is not null ) { StepMantle(); return; }

		if ( pendingWallKick is { } kick )
		{
			pendingWallKick = null;
			WallJump( kick );
		}

		switch ( Ground )
		{
			case GroundMode.Slide: StepSlide(); break;
			case GroundMode.Dash: StepDash(); break;
			case GroundMode.Carry: StepCarry(); break;
		}

		// Wall slide: pushing into a wall while falling slows the fall.
		if ( !controller.IsOnGround && body.Velocity.z < -WallSlideFallSpeed && FindWall() is { } wall
			&& Vector3.Dot( controller.WishVelocity.WithZ( 0 ).Normal, -wall.Normal ) > 0.4f )
		{
			body.Velocity = body.Velocity.WithZ( MathX.Lerp( body.Velocity.z, -WallSlideFallSpeed, Time.Delta * 10f ) );
		}

		// Keep chained moves from getting silly.
		var horizontal = body.Velocity.WithZ( 0 );
		if ( horizontal.Length > MaxHorizontalSpeed )
			body.Velocity = horizontal.Normal * MaxHorizontalSpeed + Vector3.Up * body.Velocity.z;
	}

	// ---- Dash ---------------------------------------------------------------------

	/// <summary> The way you want to go: your movement keys, or straight ahead if you're not pressing any. </summary>
	Vector3 WishDirection()
	{
		var wish = Controller.WishVelocity.WithZ( 0 );
		return wish.IsNearlyZero() ? Controller.EyeAngles.WithPitch( 0 ).ToRotation().Forward : wish.Normal;
	}

	void TryDash( bool onGround )
	{
		if ( timeSinceDash < DashTime + 0.05f ) return;
		if ( !TrySpend() ) return;

		dashDirection = WishDirection();
		timeSinceDash = 0f;
		dashFromGround = onGround;

		if ( onGround )
		{
			// A dash out of a slide keeps whichever is faster.
			var speed = MathF.Max( DashSpeed, IsSliding ? GroundVelocity.Length : 0f );
			SetGroundMode( GroundMode.Dash, dashDirection * speed );
			// Straight to full speed: no ramp-up.
			Body.Velocity = dashDirection * speed + Vector3.Up * Body.Velocity.z;
		}
		else
		{
			// Air dash: a flat burst that briefly cancels the fall.
			var body = Body;
			body.Velocity = dashDirection * MathF.Max( AirDashSpeed, body.Velocity.WithZ( 0 ).Length * 0.8f ) + Vector3.Up * MathF.Max( body.Velocity.z, 40f );
			Controller.PreventGrounding( 0.05f );
		}

		Kick( fov: 7f );
		GetComponent<CameraJuice>()?.Shake( 0.06f );
		Puff( WorldPosition, onGround ? 0.8f : 0.5f );
	}

	void StepDash()
	{
		// Full speed for DashTime, then hand over to a carry that eases back to run speed.
		var speed = GroundVelocity.Length;
		var direction = Vector3.Lerp( GroundVelocity.Normal, WishDirection(), Time.Delta * 3f ).Normal;
		GroundVelocity = direction * speed;

		if ( timeSinceGroundMode > DashTime )
		{
			if ( CrouchHeld ) TryStartSlide();
			else StartCarry( GroundVelocity );
		}
		else if ( timeSinceDust > 0.05f )
		{
			timeSinceDust = 0f;
			Puff( WorldPosition, 0.35f );
		}
	}

	void DashJump()
	{
		// The dash's speed (or more) carried into a long, low leap. Costs a pip if you have one;
		// without one it's still a jump that keeps the dash's speed.
		var bonus = TrySpend() ? DashJumpSpeed : DashSpeed * 0.9f;
		var body = Body;
		EndGroundMode( handBack: false );
		body.Velocity = dashDirection * MathF.Min( bonus, MaxHorizontalSpeed ) + Vector3.Up * DashJumpUp;
		Controller.PreventGrounding( 0.15f );
		dashFromGround = false;

		Kick( fov: 6f );
		GetComponent<CameraJuice>()?.Shake( 0.12f );
		Puff( WorldPosition, 0.9f );
		Play( JumpSound, 1f );
	}

	/// <summary> True while the anchor is off and the player is swimming. </summary>
	public bool IsFloating => MoveModeFloat.IsActive( Scene ) && (GetComponent<PlayerSurvivor>()?.IsAlive ?? true);

	/// <summary> A burst wherever you're swimming (or looking), which the water then eases back out. </summary>
	void FloatDash()
	{
		if ( timeSinceDash < DashTime + 0.05f ) return;
		if ( !TrySpend() ) return;
		var swim = GetComponent<MoveModeFloat>()?.WishSwim() ?? Vector3.Zero;
		dashDirection = swim.IsNearlyZero() ? Controller.EyeAngles.ToRotation().Forward : swim.Normal;
		timeSinceDash = 0f;
		dashFromGround = false;
		Body.Velocity = dashDirection * AirDashSpeed * 1.1f;
		Kick( fov: 7f );
		GetComponent<CameraJuice>()?.Shake( 0.06f );
	}

	// ---- Momentum carry -------------------------------------------------------------

	void StartCarry( Vector3 velocity )
	{
		carryFrom = velocity.Length;
		if ( carryFrom <= TopSpeed + 10f ) { EndGroundMode(); return; }
		SetGroundMode( GroundMode.Carry, velocity );
	}

	void StepCarry()
	{
		// Ease from the speed you had down to run speed, steering toward your input.
		var t = MathX.Clamp( timeSinceGroundMode / MathF.Max( 0.05f, MomentumCarryTime ), 0f, 1f );
		var speed = MathX.Lerp( carryFrom, TopSpeed, 1f - (1f - t) * (1f - t) );
		var wish = Controller.WishVelocity.WithZ( 0 );
		var direction = GroundVelocity.Normal;
		if ( wish.IsNearlyZero() ) speed *= 1f - t * 0.6f; // letting go of the keys bleeds speed faster
		else direction = Vector3.Lerp( direction, wish.Normal, Time.Delta * 5f ).Normal;

		GroundVelocity = direction * speed;
		if ( t >= 1f ) EndGroundMode();
	}

	// ---- Air moves ------------------------------------------------------------

	bool TryAirJump()
	{
		if ( AirJumpsLeft <= 0 ) return false;
		if ( !TrySpend() ) return false;
		AirJumpsLeft--;

		var body = Body;
		var horizontal = body.Velocity.WithZ( 0 );
		var wish = Controller.WishVelocity.WithZ( 0 );

		// Swing momentum toward where you're steering, never losing speed.
		if ( !wish.IsNearlyZero() )
		{
			var speed = MathF.Max( horizontal.Length, MoveSpeed );
			horizontal = Vector3.Lerp( horizontal, wish.Normal * speed, AirJumpRedirect );
		}

		body.Velocity = horizontal + Vector3.Up * AirJumpSpeed;
		Controller.PreventGrounding( 0.1f );

		Kick( fov: 1.5f );
		Puff( WorldPosition, 0.6f );
		Play( JumpSound, 0.7f );
		return true;
	}

	/// <summary> Testing aids: press jump or dash once (scripts can't fake input). </summary>
	public void TestPressJump() => testJump = true;
	public void TestPressDash() => testDash = true;
	bool testJump;
	bool testDash;
	(Vector3 Position, Vector3 Normal)? pendingWallKick;

	/// <summary> Moving or looking into a surface with this normal. </summary>
	bool IsPushingInto( Vector3 normal )
	{
		var wish = Controller.WishVelocity.WithZ( 0 );
		var look = Controller.EyeAngles.WithPitch( 0 ).ToRotation().Forward;
		return (!wish.IsNearlyZero() && Vector3.Dot( wish.Normal, -normal ) > 0.3f) || Vector3.Dot( look, -normal ) > 0.5f;
	}

	bool IsPushingForward()
	{
		var wish = Controller.WishVelocity.WithZ( 0 );
		var look = Controller.EyeAngles.WithPitch( 0 ).ToRotation().Forward;
		return !wish.IsNearlyZero() && Vector3.Dot( wish.Normal, look ) > 0.5f;
	}

	void WallJump( (Vector3 Position, Vector3 Normal) wall )
	{
		var body = Body;
		var velocity = body.Velocity;

		// Keep the speed you had along the wall, bounce off it, and get lifted.
		var along = (velocity - wall.Normal * Vector3.Dot( velocity, wall.Normal )).WithZ( 0 ) * WallKeepMomentum;
		var wish = Controller.WishVelocity.WithZ( 0 );
		if ( !wish.IsNearlyZero() )
		{
			var steer = wish.Normal - wall.Normal * Vector3.Dot( wish.Normal, wall.Normal );
			along += steer * 80f;
		}

		EndGroundMode( handBack: false );
		body.Velocity = along + wall.Normal * WallJumpPush + Vector3.Up * WallJumpUp;
		Controller.PreventGrounding( 0.15f );
		timeSinceWallJump = 0f;
		AirJumpsLeft = AirJumps; // bouncing gives your double jump back, so you can chain

		// Roll the view away from the wall.
		var right = Controller.EyeAngles.WithPitch( 0 ).ToRotation().Right;
		wallRoll = Vector3.Dot( wall.Normal, right ) * -9f;
		Kick( fov: 3f );
		GetComponent<CameraJuice>()?.Shake( 0.15f );
		Puff( wall.Position, 0.9f );
		Play( JumpSound, 1f );
	}

	SceneTrace WorldTrace( SceneTrace trace ) => trace
		.IgnoreGameObjectHierarchy( GameObject )
		.WithoutTags( "zombie", "zombieblocker", "gib", "viewmodel", "trigger", "creature", "player" );

	/// <summary> The closest wall around the player at chest height, if any is within reach. </summary>
	(Vector3 Position, Vector3 Normal)? FindWall()
	{
		var controller = Controller;
		var center = WorldPosition + Vector3.Up * (controller.CurrentHeight * 0.5f);
		var reach = controller.BodyRadius + WallReach;

		(Vector3, Vector3)? best = null;
		var bestDistance = float.MaxValue;
		for ( var i = 0; i < 12; i++ )
		{
			var dir = Rotation.FromYaw( i * 30f ).Forward;
			var tr = WorldTrace( Scene.Trace.Ray( center, center + dir * reach ) ).Run();

			if ( !tr.Hit || MathF.Abs( tr.Normal.z ) > 0.35f ) continue;
			if ( tr.Distance < bestDistance )
			{
				bestDistance = tr.Distance;
				best = (tr.HitPosition, tr.Normal.WithZ( 0 ).Normal);
			}
		}
		return best;
	}

	// ---- Mantle -----------------------------------------------------------------

	/// <summary>
	/// A ledge in front of you that you could climb: a wall ahead, a flat top no higher than
	/// MantleHeight above your feet, and room to stand on it.
	/// </summary>
	(Vector3 Top, Vector3 Forward)? FindLedge()
	{
		var controller = Controller;
		var forward = Controller.EyeAngles.WithPitch( 0 ).ToRotation().Forward;
		var feet = WorldPosition;
		var radius = controller.BodyRadius;

		// Something solid just ahead, at knee or waist height...
		SceneTraceResult wall = default;
		foreach ( var probeHeight in new[] { 15f, 40f } )
		{
			var from = feet + Vector3.Up * probeHeight;
			wall = WorldTrace( Scene.Trace.Ray( from, from + forward * (radius + 40f) ) ).Run();
			if ( wall.Hit && MathF.Abs( wall.Normal.z ) <= 0.4f ) break;
		}
		if ( !wall.Hit || MathF.Abs( wall.Normal.z ) > 0.4f ) return null;

		// ...with a top we can reach, found by looking down from above, a little way onto it.
		var probe = wall.HitPosition + forward * (radius + 4f);
		var down = WorldTrace( Scene.Trace.Ray( probe.WithZ( feet.z + MantleHeight + 4f ), probe.WithZ( feet.z + 8f ) ) ).Run();
		if ( !down.Hit || down.Normal.z < 0.7f || down.StartedSolid ) return null;
		var top = down.HitPosition;
		if ( top.z - feet.z < 18f ) return null; // a step, the controller handles those

		// Room to stand there?
		var height = controller.BodyHeight;
		var room = WorldTrace( Scene.Trace.Box( new BBox( new Vector3( -radius, -radius, 0 ), new Vector3( radius, radius, height ) ), top + Vector3.Up * 2f, top + Vector3.Up * 3f ) ).Run();
		if ( room.Hit || room.StartedSolid ) return null;

		return (top + Vector3.Up * 1f, forward);
	}

	void StartMantle( (Vector3 Top, Vector3 Forward) ledge )
	{
		EndGroundMode( handBack: false );
		mantle = new Mantle { From = WorldPosition, To = ledge.Top, Forward = ledge.Forward, Time = 0f };
		AirJumpsLeft = AirJumps;
		landDip = MathF.Max( landDip, 3f );
		Kick( fov: 2f );
		Puff( ledge.Top, 0.6f );
		Play( JumpSound, 0.6f );
	}

	void StepMantle()
	{
		var body = Body;
		var t = MathX.Clamp( mantle.Time / MathF.Max( 0.05f, MantleTime ), 0f, 1f );

		// Up first, then over: rise most of the way before moving onto the ledge.
		var up = 1f - (1f - t) * (1f - t);
		var over = t * t;
		var position = new Vector3(
			MathX.Lerp( mantle.From.x, mantle.To.x, over ),
			MathX.Lerp( mantle.From.y, mantle.To.y, over ),
			MathX.Lerp( mantle.From.z, mantle.To.z + 4f, up ) );

		WorldPosition = position;
		body.Velocity = Vector3.Zero;
		Controller.PreventGrounding( 0.05f );

		if ( t >= 1f )
		{
			// Pop out the far side moving, so it flows into whatever you do next.
			body.Velocity = mantle.Forward * MoveSpeed * 0.8f + Vector3.Up * 60f;
			mantle = null;
		}
	}

	// ---- Slide ------------------------------------------------------------------

	void TryStartSlide()
	{
		var horizontal = OverridesGround ? GroundVelocity : Controller.Velocity.WithZ( 0 );
		if ( horizontal.Length < SlideMinSpeed ) return;

		var speed = horizontal.Length;
		if ( timeSinceSlideBoost > SlideBoostCooldown && Ground != GroundMode.Dash )
		{
			speed += SlideBoost;
			timeSinceSlideBoost = 0f;
			Kick( fov: 2.5f );
		}

		SetGroundMode( GroundMode.Slide, horizontal.Normal * MathF.Min( speed, MaxHorizontalSpeed ) );
		Puff( WorldPosition, 0.8f );
	}

	void StepSlide()
	{
		var direction = GroundVelocity.Normal;
		var speed = GroundVelocity.Length;

		// Downhill speeds you up; uphill and flat slow you down.
		var normal = Controller.GroundNormal;
		var downhill = (Vector3.Down - normal * Vector3.Dot( Vector3.Down, normal )).WithZ( 0 );
		var slope = downhill.Length; // 0 on flat ground
		var alongSlope = slope > 0.02f ? Vector3.Dot( direction, downhill.Normal ) : 0f;
		speed += SlideSlopeAccel * slope * alongSlope * Time.Delta;
		speed -= SlideFriction * (alongSlope > 0.2f ? 0.3f : 1f) * Time.Delta;
		speed = MathX.Clamp( speed, 0f, MaxHorizontalSpeed );

		// Carve gently toward your input.
		var wish = Controller.WishVelocity.WithZ( 0 );
		if ( !wish.IsNearlyZero() )
			direction = Vector3.Lerp( direction, wish.Normal, Time.Delta * SlideSteer ).Normal;

		GroundVelocity = direction * speed;

		// Long slides downhill are allowed to keep going.
		if ( alongSlope > 0.3f && slope > 0.1f ) timeSinceGroundMode = MathF.Min( timeSinceGroundMode, SlideMaxTime * 0.5f );

		if ( timeSinceDust > 0.07f )
		{
			timeSinceDust = 0f;
			Puff( WorldPosition + direction * 12f, 0.4f );
		}
	}

	void SlideJump()
	{
		var carried = GroundVelocity * SlideJumpBoost;
		EndGroundMode( handBack: false );
		Body.Velocity = carried.ClampLength( MaxHorizontalSpeed ) + Vector3.Up * Controller.JumpSpeed;
		Controller.PreventGrounding( 0.1f );
		Kick( fov: 2f );
		Play( JumpSound, 0.9f );
	}

	// ---- Ground mode bookkeeping ----------------------------------------------------

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

		// Hand the momentum back to the body so nothing snaps.
		var body = Body;
		if ( handBack && body.IsValid() ) body.Velocity = GroundVelocity + Vector3.Up * body.Velocity.z;
	}

	// ---- Feel -------------------------------------------------------------------

	void PlayerController.IEvents.OnLanded( float distance, Vector3 impactVelocity )
	{
		if ( IsProxy ) return;
		var hardness = MathX.Clamp( -impactVelocity.z / 700f, 0f, 1f );
		landDip = MathF.Max( landDip, hardness * 6f );
		if ( hardness > 0.25f )
		{
			GetComponent<CameraJuice>()?.Shake( hardness * 0.3f );
			Play( LandSound, hardness );
			Puff( WorldPosition, 0.5f + hardness );
		}
	}

	void PlayerController.IEvents.PostCameraSetup( CameraComponent cam )
	{
		if ( IsProxy || !Controller.IsValid() ) return;

		// Widen the view as you go faster than a run; it makes speed feel like speed.
		var speed = OverridesGround ? GroundVelocity.Length : HorizontalSpeed;
		var overRun = MathX.Clamp( (speed - MoveSpeed) / 350f, 0f, 1f );
		var targetFov = overRun * SpeedFov + (IsSliding ? 4f : 0f);
		fovKick = MathX.Lerp( fovKick, 0f, Time.Delta * 4f );
		fovBoost = MathX.Lerp( fovBoost, targetFov + fovKick, Time.Delta * 7f );

		// Lean into slides in the direction you're carving, and a touch into dashes.
		var targetTilt = 0f;
		var right = Controller.EyeAngles.WithPitch( 0 ).ToRotation().Right;
		if ( IsSliding )
			targetTilt = -Vector3.Dot( GroundVelocity.Normal, right ) * SlideTilt - SlideTilt * 0.4f;
		else if ( timeSinceDash < DashTime + 0.15f )
			targetTilt = -Vector3.Dot( dashDirection, right ) * 3f;
		tilt = MathX.Lerp( tilt, targetTilt, Time.Delta * 9f );
		wallRoll = MathX.Lerp( wallRoll, 0f, Time.Delta * 5f );
		landDip = MathX.Lerp( landDip, 0f, Time.Delta * 7f );

		cam.FieldOfView += fovBoost;
		cam.WorldRotation *= new Angles( landDip, 0f, tilt + wallRoll ).ToRotation();
		cam.WorldPosition += Vector3.Down * landDip * 0.6f;

		// Dev menu camera tools.
		if ( DevFlags.Fov > 0f ) cam.FieldOfView = DevFlags.Fov;
		if ( DevFlags.FreeCam )
		{
			cam.WorldPosition = DevFlags.FreeCamPosition;
			cam.WorldRotation = DevFlags.FreeCamAngles.ToRotation();
		}
	}

	void Kick( float fov )
	{
		fovKick = MathF.Max( fovKick, fov );
	}

	void Puff( Vector3 position, float scale )
	{
		if ( !DustEffect.IsValid() ) return;
		var fx = DustEffect.Clone( position, Rotation.LookAt( Vector3.Up ) );
		fx.WorldScale = scale;
		fx.AddComponent<SelfDestruct>().Lifetime = 2f;
	}

	void Play( SoundEvent sound, float volume )
	{
		if ( sound is null ) return;
		var handle = Sound.Play( sound, WorldPosition );
		if ( handle.IsValid() ) handle.Volume = volume;
	}
}
