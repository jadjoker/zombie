using Sandbox.Movement;

/// <summary>
/// Floating, for anchor-snapped rounds: no gravity, you swim where you look. WASD moves along your
/// view (look up and press forward to rise), Jump swims straight up and Crouch straight down. Movement
/// eases in and drifts to a stop, like being underwater. Shift still dashes, in 3D.
/// Takes over while the wave director says the anchor is off.
/// </summary>
public sealed class MoveModeFloat : MoveMode
{
	[Property] public float SwimSpeed { get; set; } = 300f;
	/// <summary> How quickly you reach swim speed (and drift to a stop). Higher is snappier. </summary>
	[Property] public float Response { get; set; } = 3.5f;
	/// <summary> How high above the floor you can float before the water pushes you back down. </summary>
	[Property] public float Ceiling { get; set; } = 650f;

	/// <summary> Testing aid: swim in this direction (world space) as if keys were held. </summary>
	public Vector3? TestInput { get; set; }

	public static bool Active( Scene scene )
	{
		if ( scene is null ) return false;
		// Look it up even if it's disabled, and remember it (this runs every physics step).
		if ( !director.IsValid() || director.Scene != scene )
			director = scene.Components.GetAll<WaveDirector>( FindMode.EverythingInDescendants ).FirstOrDefault();
		return director?.FloatRound ?? false;
	}
	static WaveDirector director;

	public override int Score( PlayerController controller )
	{
		var alive = GetComponent<PlayerSurvivor>()?.IsAlive ?? true;
		return Active( Scene ) && alive ? 2000 : -1000;
	}

	/// <summary> Where the player wants to swim, in world space, at full speed. </summary>
	public Vector3 WishSwim()
	{
		if ( TestInput is { } test ) return test.Normal * SwimSpeed;
		var eyes = Controller.EyeAngles.ToRotation();
		var move = Input.AnalogMove;
		var wish = eyes.Forward * move.x + eyes.Left * move.y;
		if ( Input.Down( "Jump" ) ) wish += Vector3.Up;
		if ( Input.Down( "Duck" ) ) wish += Vector3.Down;
		// Holding sprint swims faster too.
		var speed = Input.Down( "Run" ) ? SwimSpeed * 1.45f : SwimSpeed;
		return wish.IsNearlyZero() ? Vector3.Zero : wish.Normal * speed;
	}

	public override Vector3 UpdateMove( Rotation eyes, Vector3 input ) => WishSwim();

	public override void PrePhysicsStep()
	{
		base.PrePhysicsStep();
		var body = Controller.Body;
		if ( !body.IsValid() ) return;
		// The controller turns gravity on whenever you're airborne; floating means none.
		// (Once the anchor's back on, the controller manages it again by itself.)
		body.Gravity = false;

		// Ease toward where you want to go; dashes and knockbacks fade out the same way.
		var velocity = body.Velocity;
		var target = WishSwim();
		var rate = target.IsNearlyZero() ? Response * 0.6f : Response;
		velocity = Vector3.Lerp( velocity, target, MathX.Clamp( rate * Time.Delta, 0f, 1f ) );

		// Don't drift out of the arena: the water pushes back near the top and the floor.
		var feet = WorldPosition;
		var floor = Scene.Trace.Ray( feet + Vector3.Up * 5f, feet + Vector3.Down * 2000f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "zombie", "zombieblocker", "gib", "creature", "player", "trigger" )
			.IgnoreDynamic()
			.Run();
		var height = floor.Hit ? feet.z - floor.HitPosition.z : 0f;
		if ( height > Ceiling ) velocity.z = MathF.Min( velocity.z, -60f * (height - Ceiling) / 100f );

		body.Velocity = velocity;
	}
}
