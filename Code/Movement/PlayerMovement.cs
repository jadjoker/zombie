/// <summary>
/// Extra moves on top of the PlayerController: double jump, wall bounce, wall slide and a momentum slide,
/// plus the camera feel that sells them (speed FOV, slide tilt, wall-kick roll, landing dip).
/// Sits on the player next to the controller and MoveModeSlide.
/// </summary>
public sealed class PlayerMovement : Component, PlayerController.IEvents
{
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

	[Property, Group( "Slide" )] public float SlideMinSpeed { get; set; } = 180f;
	[Property, Group( "Slide" )] public float SlideBoost { get; set; } = 150f;
	[Property, Group( "Slide" )] public float SlideBoostCooldown { get; set; } = 1.0f;
	/// <summary> Speed lost per second while sliding. </summary>
	[Property, Group( "Slide" )] public float SlideFriction { get; set; } = 240f;
	[Property, Group( "Slide" )] public float SlideEndSpeed { get; set; } = 140f;
	[Property, Group( "Slide" )] public float SlideMaxTime { get; set; } = 1.4f;
	/// <summary> How quickly you can carve the slide toward your input, in turns per second-ish. </summary>
	[Property, Group( "Slide" )] public float SlideSteer { get; set; } = 2.2f;
	[Property, Group( "Slide" )] public float SlideJumpBoost { get; set; } = 1.12f;

	[Property, Group( "Limits" )] public float MaxHorizontalSpeed { get; set; } = 760f;

	[Property, Group( "Feel" )] public float SpeedFov { get; set; } = 12f;
	[Property, Group( "Feel" )] public float SlideTilt { get; set; } = 5f;
	[Property, Group( "Feel" )] public GameObject DustEffect { get; set; }
	[Property, Group( "Feel" )] public SoundEvent JumpSound { get; set; }
	[Property, Group( "Feel" )] public SoundEvent LandSound { get; set; }

	public bool IsSliding { get; private set; }
	/// <summary> What MoveModeSlide feeds the controller while sliding. </summary>
	public Vector3 SlideVelocity { get; private set; }
	public int AirJumpsLeft { get; private set; }
	/// <summary> Testing aid: holds crouch without the key (scripts can't fake input). </summary>
	public bool ForceCrouch { get; set; }
	bool CrouchHeld => Input.Down( "Duck" ) || ForceCrouch;

	public float HorizontalSpeed => Controller.IsValid() ? Controller.Velocity.WithZ( 0 ).Length : 0f;

	PlayerController Controller => GetComponent<PlayerController>();
	Rigidbody Body => Controller?.Body;

	TimeSince timeSinceSlideStart;
	TimeSince timeSinceSlideBoost = 999f;
	TimeSince timeSinceWallJump = 999f;
	TimeSince timeSinceDust;
	bool wasOnGround = true;

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

		// Short coyote time so the first air jump comes quickly after leaving a ledge.
		controller.CoyoteTime = 0.12f;
		AirJumpsLeft = AirJumps;
	}

	bool CanAct => !IsProxy && Controller.IsValid() && Body.IsValid()
		&& (GetComponent<PlayerSurvivor>()?.IsAlive ?? true);

	protected override void OnUpdate()
	{
		if ( !CanAct ) return;
		var controller = Controller;
		var onGround = controller.IsOnGround;

		if ( onGround ) AirJumpsLeft = AirJumps;

		var jumpPressed = Input.Pressed( "Jump" ) || testJump;
		testJump = false;

		// Note: TimeSinceGrounded counts up while airborne (TimeSinceUngrounded resets every airborne frame).
		var airborne = !onGround && controller.TimeSinceGrounded > 0.1f;

		if ( jumpPressed && !IsSliding )
		{
			var wall = timeSinceWallJump > WallJumpCooldown ? FindWall() : null;
			if ( wall is { } w && (airborne || (onGround && IsPushingInto( w.Normal ))) )
			{
				// Against a wall: kick off it. Applied on the next physics step so it beats the normal ground jump.
				pendingWallKick = w;
			}
			else if ( airborne )
			{
				TryAirJump();
			}
		}

		// Slide: crouch while moving fast, or land while holding crouch.
		var justLanded = onGround && !wasOnGround;
		if ( !IsSliding && onGround && (Input.Pressed( "Duck" ) || (justLanded && CrouchHeld)) )
			TryStartSlide();

		if ( IsSliding )
		{
			if ( !CrouchHeld || !onGround || timeSinceSlideStart > SlideMaxTime || SlideVelocity.Length < SlideEndSpeed )
				EndSlide();

			if ( Input.Pressed( "Jump" ) && onGround )
				SlideJump();
		}

		wasOnGround = onGround;
	}

	protected override void OnFixedUpdate()
	{
		if ( !CanAct ) return;
		var controller = Controller;
		var body = Body;

		if ( pendingWallKick is { } kick )
		{
			pendingWallKick = null;
			WallJump( kick );
		}

		if ( IsSliding ) StepSlide();

		// Wall slide: pushing into a wall while falling slows the fall.
		if ( !controller.IsOnGround && body.Velocity.z < -WallSlideFallSpeed && FindWall() is { } wall
			&& Vector3.Dot( controller.WishVelocity.WithZ( 0 ).Normal, -wall.Normal ) > 0.4f )
		{
			body.Velocity = body.Velocity.WithZ( MathX.Lerp( body.Velocity.z, -WallSlideFallSpeed, Time.Delta * 10f ) );
		}

		// Keep chained bounces from getting silly.
		var horizontal = body.Velocity.WithZ( 0 );
		if ( horizontal.Length > MaxHorizontalSpeed )
			body.Velocity = horizontal.Normal * MaxHorizontalSpeed + Vector3.Up * body.Velocity.z;
	}

	// ---- Air moves ------------------------------------------------------------

	bool TryAirJump()
	{
		if ( AirJumpsLeft <= 0 ) return false;
		AirJumpsLeft--;

		var body = Body;
		var horizontal = body.Velocity.WithZ( 0 );
		var wish = Controller.WishVelocity.WithZ( 0 );

		// Swing momentum toward where you're steering, never losing speed.
		if ( !wish.IsNearlyZero() )
		{
			var speed = MathF.Max( horizontal.Length, Controller.WalkSpeed );
			horizontal = Vector3.Lerp( horizontal, wish.Normal * speed, AirJumpRedirect );
		}

		body.Velocity = horizontal + Vector3.Up * AirJumpSpeed;
		Controller.PreventGrounding( 0.1f );

		Kick( fov: 1.5f );
		Puff( WorldPosition, 0.6f );
		Play( JumpSound, 0.7f );
		return true;
	}

	/// <summary> Testing aid: presses jump once (scripts can't fake input). </summary>
	public void TestPressJump() => testJump = true;
	bool testJump;
	(Vector3 Position, Vector3 Normal)? pendingWallKick;

	/// <summary> Moving or looking into a surface with this normal. </summary>
	bool IsPushingInto( Vector3 normal )
	{
		var wish = Controller.WishVelocity.WithZ( 0 );
		var look = Controller.EyeAngles.WithPitch( 0 ).ToRotation().Forward;
		return (!wish.IsNearlyZero() && Vector3.Dot( wish.Normal, -normal ) > 0.3f) || Vector3.Dot( look, -normal ) > 0.5f;
	}

	bool TryWallJump()
	{
		if ( timeSinceWallJump < WallJumpCooldown ) return false;
		if ( FindWall() is not { } wall ) return false;
		WallJump( wall );
		return true;
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
			var tr = Scene.Trace.Ray( center, center + dir * reach )
				.IgnoreGameObjectHierarchy( GameObject )
				.WithoutTags( "zombie", "zombieblocker", "gib", "viewmodel", "trigger" )
				.Run();

			if ( !tr.Hit || MathF.Abs( tr.Normal.z ) > 0.35f ) continue;
			if ( tr.Distance < bestDistance )
			{
				bestDistance = tr.Distance;
				best = (tr.HitPosition, tr.Normal.WithZ( 0 ).Normal);
			}
		}
		return best;
	}

	// ---- Slide ------------------------------------------------------------------

	void TryStartSlide()
	{
		var horizontal = Controller.Velocity.WithZ( 0 );
		if ( horizontal.Length < SlideMinSpeed ) return;

		var speed = horizontal.Length;
		if ( timeSinceSlideBoost > SlideBoostCooldown )
		{
			speed += SlideBoost;
			timeSinceSlideBoost = 0f;
			Kick( fov: 2.5f );
		}

		SlideVelocity = horizontal.Normal * MathF.Min( speed, MaxHorizontalSpeed );
		IsSliding = true;
		timeSinceSlideStart = 0f;
		Puff( WorldPosition, 0.8f );
	}

	void StepSlide()
	{
		var speed = MathF.Max( 0f, SlideVelocity.Length - SlideFriction * Time.Delta );
		var direction = SlideVelocity.Normal;

		// Carve gently toward your input.
		var wish = Controller.WishVelocity.WithZ( 0 );
		if ( !wish.IsNearlyZero() )
			direction = Vector3.Lerp( direction, wish.Normal, Time.Delta * SlideSteer ).Normal;

		SlideVelocity = direction * speed;

		if ( timeSinceDust > 0.07f )
		{
			timeSinceDust = 0f;
			Puff( WorldPosition + direction * 12f, 0.4f );
		}
	}

	void SlideJump()
	{
		var carried = SlideVelocity * SlideJumpBoost;
		EndSlide();
		var body = Body;
		body.Velocity = carried + Vector3.Up * Controller.JumpSpeed;
		Controller.PreventGrounding( 0.1f );
		Kick( fov: 2f );
		Play( JumpSound, 0.9f );
	}

	void EndSlide()
	{
		if ( !IsSliding ) return;
		IsSliding = false;

		// Hand the momentum back to the body so nothing snaps.
		var body = Body;
		if ( body.IsValid() ) body.Velocity = SlideVelocity + Vector3.Up * body.Velocity.z;
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
		var overRun = MathX.Clamp( (HorizontalSpeed - Controller.RunSpeed) / 300f, 0f, 1f );
		var targetFov = overRun * SpeedFov + (IsSliding ? 4f : 0f);
		fovKick = MathX.Lerp( fovKick, 0f, Time.Delta * 3f );
		fovBoost = MathX.Lerp( fovBoost, targetFov + fovKick, Time.Delta * 5f );

		// Lean into slides in the direction you're carving.
		var targetTilt = 0f;
		if ( IsSliding )
		{
			var right = Controller.EyeAngles.WithPitch( 0 ).ToRotation().Right;
			targetTilt = -Vector3.Dot( SlideVelocity.Normal, right ) * SlideTilt - SlideTilt * 0.4f;
		}
		tilt = MathX.Lerp( tilt, targetTilt, Time.Delta * 8f );
		wallRoll = MathX.Lerp( wallRoll, 0f, Time.Delta * 5f );
		landDip = MathX.Lerp( landDip, 0f, Time.Delta * 7f );

		cam.FieldOfView += fovBoost;
		cam.WorldRotation *= new Angles( landDip, 0f, tilt + wallRoll ).ToRotation();
		cam.WorldPosition += Vector3.Down * landDip * 0.6f;
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
