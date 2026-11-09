using Sandbox.Audio;

/// <summary>
/// Makes moving feel like being underwater: everything eases in and drifts out, gravity is weak so jumps are slow
/// swim kicks, fast falls are capped (you sink), slides become long glides, the camera sways, bubbles rise from your
/// breath and your moves, and sound goes muffled. Automatic in the water level, or forced on and off from the dev
/// menu. Also on while the anchor is off and you're floating. Local player only; sits next to the PlayerController.
/// </summary>
public sealed class WaterFeel : Component, PlayerController.IEvents
{
	/// <summary> Dev override: true forces it on, false off, null leaves it to the level. </summary>
	public static bool? Force { get; set; }

	[Property, Group( "Ground" )] public float SpeedScale { get; set; } = 0.88f;
	/// <summary> Seconds to reach full speed (the dry value is far snappier). </summary>
	[Property, Group( "Ground" )] public float AccelerationTime { get; set; } = 0.5f;
	/// <summary> Seconds to coast to a stop. </summary>
	[Property, Group( "Ground" )] public float DeaccelerationTime { get; set; } = 0.9f;

	[Property, Group( "Air" )] public float GravityScale { get; set; } = 0.4f;
	/// <summary> Fraction of the normal jump speed; with weak gravity it still goes about as high, but hangs. </summary>
	[Property, Group( "Air" )] public float JumpScale { get; set; } = 0.66f;
	/// <summary> The fastest you sink. </summary>
	[Property, Group( "Air" )] public float MaxSink { get; set; } = 300f;
	/// <summary> Sideways speed lost per second in the air (water resists). </summary>
	[Property, Group( "Air" )] public float AirDrag { get; set; } = 0.55f;

	[Property, Group( "Slide" )] public float SlideFrictionScale { get; set; } = 0.35f;
	[Property, Group( "Slide" )] public float SlideTimeScale { get; set; } = 1.7f;
	[Property, Group( "Slide" )] public float SlideSteerScale { get; set; } = 1.5f;

	[Property, Group( "Sound" )] public float MuffleFrequency { get; set; } = 1500f;

	/// <summary> 0 = dry, 1 = fully in the water feel (eases over about half a second). </summary>
	public float Blend { get; private set; }
	/// <summary> True while in the water feel and off the ground (or floating): the body should look like it's swimming. </summary>
	public float Swimming { get; private set; }

	PlayerController Controller => GetComponent<PlayerController>();
	PlayerMovement Movement => GetComponent<PlayerMovement>();

	public static bool IsOn( Scene scene )
	{
		if ( Force is bool forced ) return forced;
		var name = scene?.Source?.ResourcePath ?? scene?.Name ?? "";
		return name.Contains( "water", StringComparison.OrdinalIgnoreCase );
	}

	// Dry values, remembered while we're not touching anything.
	float dryAccel = 0.1f, dryDecel = 0.1f, dryJump = 300f, dryWalk = 320f, dryRun = 470f;
	float drySlideFriction = 220f, drySlideTime = 1.4f, drySlideSteer = 2.4f, dryAirJump = 340f;
	bool captured;

	TimeUntil nextTrail;
	static LowPassProcessor muffle;

	protected override void OnDestroy()
	{
		if ( muffle is not null ) { Mixer.Master.RemoveProcessor( muffle ); muffle = null; }
	}

	bool Floating => Movement?.IsFloating ?? false;

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		var controller = Controller;
		var movement = Movement;
		if ( !controller.IsValid() ) return;

		if ( !captured ) ApplyTuning( controller, movement );      // remember the dry numbers before touching anything
		var alive = GetComponent<PlayerSurvivor>()?.IsAlive ?? true;
		// Above the sea's surface (on the dock) it's dry land.
		var underwater = DockHouse.SurfaceZ is not float surface || WorldPosition.z < surface;
		var want = alive && underwater && (IsOn( Scene ) || Floating) ? 1f : 0f;
		Blend = MathX.Approach( Blend, want, Time.Delta * 2f );
		var airborne = !controller.IsOnGround || Floating;
		Swimming = MathX.Approach( Swimming, Blend > 0.5f && airborne ? 1f : 0f, Time.Delta * 5f );

		ApplyTuning( controller, movement );
		UpdateSound();
		if ( Blend > 0.3f ) SpawnBubbles( controller, movement );
	}

	/// <summary> Blend the controller's feel between its dry numbers and the water ones. </summary>
	void ApplyTuning( PlayerController controller, PlayerMovement movement )
	{
		// While dry (and idle), keep a fresh copy of the normal values so we can return to them.
		if ( Blend <= 0.001f || !captured )
		{
			dryAccel = controller.AccelerationTime;
			dryDecel = controller.DeaccelerationTime;
			dryJump = controller.JumpSpeed;
			if ( movement.IsValid() )
			{
				dryWalk = movement.MoveSpeed;
				dryRun = movement.SprintSpeed;
				drySlideFriction = movement.SlideFriction;
				drySlideTime = movement.SlideMaxTime;
				drySlideSteer = movement.SlideSteer;
				dryAirJump = movement.AirJumpSpeed;
			}
			captured = true;
			return;
		}
		if ( !captured ) return;

		var b = Blend * Blend * (3f - 2f * Blend);
		controller.AccelerationTime = MathX.Lerp( dryAccel, AccelerationTime, b );
		controller.DeaccelerationTime = MathX.Lerp( dryDecel, DeaccelerationTime, b );
		controller.JumpSpeed = MathX.Lerp( dryJump, dryJump * JumpScale, b );
		controller.WalkSpeed = MathX.Lerp( dryWalk, dryWalk * SpeedScale, b );
		controller.RunSpeed = MathX.Lerp( dryRun, dryRun * SpeedScale, b );
		if ( movement.IsValid() )
		{
			movement.SlideFriction = MathX.Lerp( drySlideFriction, drySlideFriction * SlideFrictionScale, b );
			movement.SlideMaxTime = MathX.Lerp( drySlideTime, drySlideTime * SlideTimeScale, b );
			movement.SlideSteer = MathX.Lerp( drySlideSteer, drySlideSteer * SlideSteerScale, b );
			movement.AirJumpSpeed = MathX.Lerp( dryAirJump, dryAirJump * JumpScale, b );
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;
		var controller = Controller;
		var body = controller.IsValid() ? controller.Body : null;
		if ( !body.IsValid() ) return;

		// Floating rounds handle their own physics; this is for walking around on the seabed.
		var physics = Blend > 0.001f && !Floating;
		if ( !physics )
		{
			if ( body.GravityScale != 1f && !Floating ) body.GravityScale = 1f;
			return;
		}

		var b = Blend * Blend * (3f - 2f * Blend);
		body.GravityScale = MathX.Lerp( 1f, GravityScale, b );
		if ( controller.IsOnGround ) return;

		var velocity = body.Velocity;
		// Water pushes back on a fast fall and on sideways speed.
		if ( velocity.z < -MaxSink ) velocity.z = MathX.Lerp( velocity.z, -MaxSink, Time.Delta * 8f );
		var drag = MathF.Exp( -AirDrag * b * Time.Delta );
		velocity = new Vector3( velocity.x * drag, velocity.y * drag, velocity.z );
		body.Velocity = velocity;
	}

	// ---- Sound ---------------------------------------------------------------

	void UpdateSound()
	{
		if ( muffle is null )
		{
			muffle = new LowPassProcessor { Frequency = MuffleFrequency, Mix = 0f, Enabled = false };
			Mixer.Master.AddProcessor( muffle );
		}
		muffle.Frequency = MuffleFrequency;
		muffle.Mix = Blend * 0.85f;
		muffle.Enabled = Blend > 0.01f;
	}

	// ---- Bubbles -------------------------------------------------------------

	void SpawnBubbles( PlayerController controller, PlayerMovement movement )
	{
		var eye = controller.EyePosition;
		var facing = Rotation.FromYaw( controller.EyeAngles.yaw );
		var speed = controller.Velocity.Length;

		// A wake while sliding or moving fast: bubbles shed from the feet and back.
		var shedding = (movement?.IsSliding ?? false) || speed > 420f;
		if ( shedding && nextTrail )
		{
			nextTrail = 0.06f;
			var p = WorldPosition + Vector3.Up * Game.Random.Float( 4f, 36f ) - Controller.Velocity.WithZ( 0 ).Normal * 14f;
			WaterBubble.Create( p + Game.Random.VectorInSphere( 6f ), Game.Random.Float( 0.7f, 1.8f ) );
		}
	}

	void PlayerController.IEvents.OnLanded( float distance, Vector3 impactVelocity )
	{
		if ( IsProxy || Blend < 0.3f ) return;
		// A puff of bubbles off the seabed where you land (the harder the landing, the more).
		var count = (int)MathX.Clamp( -impactVelocity.z / 60f, 2f, 10f );
		for ( var i = 0; i < count; i++ )
			WaterBubble.Create( WorldPosition + Vector3.Up * 3f + Game.Random.VectorInSphere( 14f ), Game.Random.Float( 0.7f, 1.7f ), i * 0.02f );
	}

	// ---- Camera --------------------------------------------------------------

	void PlayerController.IEvents.PostCameraSetup( CameraComponent cam )
	{
		if ( IsProxy || Blend <= 0.01f ) return;
		var t = Time.Now;
		var calm = 1f - MathX.Clamp( (Controller?.Velocity.Length ?? 0f) / 500f, 0f, 1f ) * 0.6f;   // it settles a little when you're racing along
		var s = Blend * calm;
		var sway = new Angles(
			MathF.Sin( t * 0.9f ) * 0.32f + MathF.Sin( t * 2.1f ) * 0.08f,
			MathF.Sin( t * 0.63f ) * 0.45f,
			MathF.Sin( t * 0.77f ) * 0.65f ) * s;
		cam.WorldRotation *= sway.ToRotation();
		cam.WorldPosition += Vector3.Up * MathF.Sin( t * 1.1f ) * 1.2f * s;
	}
}

/// <summary> A bubble: rises with a lazy wobble, swells, and pops away. </summary>
public sealed class WaterBubble : Component
{
	float life = 1.8f;
	float size = 1f;
	float phase;
	float delay;
	TimeSince born;
	Vector3 drift;
	static Material ghost;

	/// <summary> A line of small bubbles along a bullet's path; they appear one after another from the muzzle outward, like the shot is leaving them behind. </summary>
	public static void Trail( Vector3 from, Vector3 to )
	{
		var length = (to - from).Length;
		var count = (int)MathX.Clamp( length / 38f, 3f, 22f );
		for ( var i = 0; i < count; i++ )
		{
			var t = (i + Game.Random.Float( 0f, 0.8f )) / count;
			var p = Vector3.Lerp( from, to, t ) + Game.Random.VectorInSphere( 2.2f );
			Create( p, Game.Random.Float( 0.35f, 0.9f ), t * 0.06f, Game.Random.Float( 0.7f, 1.5f ) );
		}
	}

	public static void Create( Vector3 position, float size, float delay = 0f, float lifetime = 0f )
	{
		var go = new GameObject( true, "Bubble" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		var bubble = go.AddComponent<WaterBubble>();
		bubble.size = size;
		bubble.delay = delay;
		bubble.life = lifetime > 0f ? lifetime : Game.Random.Float( 1.4f, 2.6f );
		bubble.phase = Game.Random.Float( 0f, 6.28f );
		bubble.drift = Game.Random.VectorInSphere( 6f ).WithZ( 0 );

		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = Shapes.Sphere;
		renderer.Tint = new Color( 0.8f, 0.96f, 1f, 0.55f );
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		ghost ??= Material.Load( "materials/fx/fx_ghost.vmat" );
		if ( ghost is not null && !ghost.IsError ) renderer.MaterialOverride = ghost;
		go.WorldScale = Vector3.Zero;
	}

	protected override void OnStart() => born = 0f;

	protected override void OnUpdate()
	{
		var age = born - delay;
		if ( age < 0f ) return;
		var t = age / life;
		if ( t >= 1f ) { GameObject.Destroy(); return; }

		var rise = 38f + 40f * t;
		var wobble = new Vector3( MathF.Sin( age * 4f + phase ), MathF.Cos( age * 3.3f + phase ), 0f ) * 7f;
		GameObject.WorldPosition += (Vector3.Up * rise + wobble + drift) * Time.Delta;

		var grow = MathX.Clamp( t * 8f, 0f, 1f);
		var pop = t > 0.88f ? 1f - (t - 0.88f) / 0.12f : 1f;
		GameObject.WorldScale = Vector3.One * Shapes.SphereScale( size * (0.6f + 0.4f * grow) * pop );
	}
}
