using Godot;

/// <summary>
/// The sea's surface height while a dock is in the level (s&box DockHouse.SurfaceZ); null without one, where the
/// whole level is underwater.
/// </summary>
public static class Sea
{
	public static float? SurfaceY;
}

/// <summary>
/// Port of s&box WaterFeel: on the water level, moving on the seabed feels underwater. Slower, floatier jumps
/// (less gravity, a capped sink), water drag on sideways speed in the air, longer and smoother slides, a slow camera
/// sway, muffled sound, and bubbles off the feet when moving fast or landing. Blends in and out (dry on the dock).
/// Child of the PlayerController.
/// </summary>
public partial class WaterFeel : Node, ICameraModifier
{
	/// <summary> Dev override: force on or off (null = by level). </summary>
	public static bool? Force;

	public float SpeedScale = 0.88f;
	public float GravityScale = 0.4f;
	public float JumpScale = 0.66f;
	public float MaxSink = 300f * U.In;
	public float AirDrag = 0.55f;
	public float SlideFrictionScale = 0.35f;
	public float SlideTimeScale = 1.7f;
	public float SlideSteerScale = 1.5f;
	public float MuffleFrequency = 1500f;

	public float Blend { get; private set; }
	public float Swimming { get; private set; }
	TimeSince timeSinceSwish;

	PlayerController controller;
	PlayerMovement movement;
	float dryJump, dryWalk, dryRun, drySlideFriction, drySlideTime, drySlideSteer, dryAirJump;
	bool captured;
	TimeUntil nextTrail;
	AudioEffectLowPassFilter muffle;

	public static bool IsOn()
	{
		if ( Force is bool forced ) return forced;
		return (Level.Current?.LevelName ?? "").Contains( "water", StringComparison.OrdinalIgnoreCase );
	}

	public override void _Ready()
	{
		Name = "WaterFeel";
		controller = GetParent<PlayerController>();
		movement = controller.GetNodeOrNull<PlayerMovement>( "PlayerMovement" );
		controller.Landed += OnLanded;
		var bus = AudioServer.GetBusIndex( "Master" );
		muffle = new AudioEffectLowPassFilter { CutoffHz = MuffleFrequency };
		AudioServer.AddBusEffect( bus, muffle );
		AudioServer.SetBusEffectEnabled( bus, AudioServer.GetBusEffectCount( bus ) - 1, false );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( !captured ) ApplyTuning();
		var alive = controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.IsAlive ?? true;
		var underwater = Sea.SurfaceY is not float surface || controller.GlobalPosition.Y < surface;
		var floating = movement?.IsFloating ?? false;
		var want = alive && underwater && (IsOn() || floating) ? 1f : 0f;
		Blend = MathX.Approach( Blend, want, dt * 2f );
		Swimming = MathX.Approach( Swimming, Blend > 0.5f && (!controller.IsOnGround || floating) ? 1f : 0f, dt * 5f );
		// A swish now and then while swimming along.
		if ( Swimming > 0.5f && controller.Velocity.Length() > 120f * U.In && timeSinceSwish > 0.75f )
		{
			timeSinceSwish = 0f;
			Sfx.Play( "swim", controller.GlobalPosition, 0.35f, Rng.Float( 0.85f, 1.15f ) );
		}
		ApplyTuning();
		UpdateSound();
		if ( Blend > 0.3f ) SpawnBubbles();
	}

	void ApplyTuning()
	{
		if ( Blend <= 0.001f || !captured )
		{
			dryJump = controller.JumpSpeed;
			if ( movement is not null )
			{
				dryWalk = movement.MoveSpeed;
				dryRun = movement.SprintSpeed;
				drySlideFriction = movement.SlideFriction;
				drySlideTime = movement.SlideMaxTime;
				drySlideSteer = movement.SlideSteer;
				dryAirJump = movement.AirJumpSpeed;
			}
			captured = true;
			controller.GravityScale = 1f;
			return;
		}
		var b = Blend * Blend * (3f - 2f * Blend);
		controller.JumpSpeed = MathX.Lerp( dryJump, dryJump * JumpScale, b );
		controller.GravityScale = MathX.Lerp( 1f, GravityScale, b );
		if ( movement is not null )
		{
			// The movement kit owns walk/run speed (and the weapon scales it for aiming), so scale the kit's numbers.
			movement.MoveSpeed = MathX.Lerp( dryWalk, dryWalk * SpeedScale, b );
			movement.SprintSpeed = MathX.Lerp( dryRun, dryRun * SpeedScale, b );
			movement.SlideFriction = MathX.Lerp( drySlideFriction, drySlideFriction * SlideFrictionScale, b );
			movement.SlideMaxTime = MathX.Lerp( drySlideTime, drySlideTime * SlideTimeScale, b );
			movement.SlideSteer = MathX.Lerp( drySlideSteer, drySlideSteer * SlideSteerScale, b );
			movement.AirJumpSpeed = MathX.Lerp( dryAirJump, dryAirJump * JumpScale, b );
		}
	}

	public override void _PhysicsProcess( double delta )
	{
		if ( Blend <= 0.001f || controller.IsOnGround ) return;
		var dt = (float)delta;
		var b = Blend * Blend * (3f - 2f * Blend);
		var v = controller.Velocity;
		// Water pushes back on a fast fall and on sideways speed.
		if ( v.Y < -MaxSink ) v.Y = MathX.Lerp( v.Y, -MaxSink, dt * 8f );
		var drag = MathF.Exp( -AirDrag * b * dt );
		controller.Velocity = new Vector3( v.X * drag, v.Y, v.Z * drag );
	}

	void UpdateSound()
	{
		var bus = AudioServer.GetBusIndex( "Master" );
		var index = AudioServer.GetBusEffectCount( bus ) - 1;
		muffle.CutoffHz = MathX.Lerp( 20000f, MuffleFrequency, Blend * 0.85f );
		AudioServer.SetBusEffectEnabled( bus, index, Blend > 0.01f );
	}

	void SpawnBubbles()
	{
		var speed = controller.Velocity.Length() / U.In;
		var shedding = (movement?.IsSliding ?? false) || speed > 420f;
		if ( shedding && nextTrail <= 0f )
		{
			nextTrail = 0.06f;
			var p = controller.GlobalPosition + Vector3.Up * Rng.Float( 4f, 36f ) * U.In - controller.Velocity.Flat().SafeNormal() * 14f * U.In;
			WaterBubble.Create( p + Rng.InSphere( 6f * U.In ), Rng.Float( 0.7f, 1.8f ) );
		}
	}

	void OnLanded( float distance, Vector3 impactVelocity )
	{
		if ( Blend < 0.3f ) return;
		var count = (int)MathX.Clamp( -impactVelocity.Y / U.In / 60f, 2f, 10f );
		for ( var i = 0; i < count; i++ )
			WaterBubble.Create( controller.GlobalPosition + Vector3.Up * 3f * U.In + Rng.InSphere( 14f * U.In ), Rng.Float( 0.7f, 1.7f ), i * 0.02f );
	}

	public void PostCameraSetup( Camera3D cam )
	{
		if ( Blend <= 0.01f ) return;
		var t = (float)Clock.Now;
		var calm = 1f - MathX.Clamp( controller.Velocity.Length() / U.In / 500f, 0f, 1f ) * 0.6f;
		var s = Blend * calm;
		var sway = new Angles(
			(MathF.Sin( t * 0.9f ) * 0.32f + MathF.Sin( t * 2.1f ) * 0.08f) * s,
			MathF.Sin( t * 0.63f ) * 0.45f * s,
			MathF.Sin( t * 0.77f ) * 0.65f * s );
		cam.GlobalBasis = cam.GlobalBasis * sway.ToBasis();
		cam.GlobalPosition += Vector3.Up * MathF.Sin( t * 1.1f ) * 1.2f * s * U.In;
	}
}

/// <summary>
/// Port of s&box WaterBubble: bubbles that wobble up, grow in and pop, drawn as GPU particles (bubbles.gdshader).
/// Sizes are s&box radii in inches. Trail() leaves a line of them along a bullet's path.
/// </summary>
public static class WaterBubble
{
	public static void Trail( Vector3 from, Vector3 to )
	{
		var length = (to - from).Length() / U.In;
		var count = (int)MathX.Clamp( length / 38f, 3f, 22f );
		for ( var i = 0; i < count; i++ )
		{
			var t = (i + Rng.Float( 0f, 0.8f )) / count;
			Create( from.Lerp( to, t ) + Rng.InSphere( 2.2f * U.In ), Rng.Float( 0.35f, 0.9f ), t * 0.06f, Rng.Float( 0.7f, 1.5f ) );
		}
	}

	public static void Create( Vector3 position, float size, float delay = 0f, float lifetime = 0f ) =>
		GpuFx.Bubble( position, size, delay, lifetime > 0f ? lifetime : Rng.Float( 1.4f, 2.6f ) );
}

/// <summary> Marine snow: specks of drifting silt around the camera (the s&box particle box emitter). </summary>
public partial class MarineSnow : GpuParticles3D
{
	public override void _Ready()
	{
		Amount = 1500;
		Lifetime = 12f;
		Preprocess = 12f;
		VisibilityAabb = new Aabb( -Vector3.One * 15f, Vector3.One * 30f );
		var process = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3( 15f, 8f, 15f ),
			Gravity = new Vector3( 0f, -0.08f, 0f ),
			Direction = new Vector3( 0.3f, 0f, 0.2f ),
			Spread = 180f,
			InitialVelocityMin = 0.02f,
			InitialVelocityMax = 0.12f,
			TurbulenceEnabled = true,
			TurbulenceNoiseStrength = 0.4f,
			ScaleMin = 0.5f,
			ScaleMax = 1.4f,
		};
		ProcessMaterial = process;
		DrawPass1 = new QuadMesh
		{
			Size = Vector2.One * 0.6f * U.In * 2f,
			Material = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
				AlbedoColor = new Color( 0.85f, 0.95f, 1f, 0.55f ),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			},
		};
		LocalCoords = false;
	}

	public override void _Process( double delta )
	{
		// Follow the camera so there's always snow around you.
		var cam = GetViewport().GetCamera3D();
		if ( cam is not null ) GlobalPosition = cam.GlobalPosition;
	}
}
