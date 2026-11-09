using Godot;

/// <summary>
/// Port of s&box MoveModeFloat: floating, for anchor-snapped rounds. No gravity, you swim where you look: WASD
/// along your view (look up and press forward to rise), Jump straight up, Crouch straight down. Movement eases in
/// and drifts to a stop like being underwater; sprint swims faster. Takes over while the wave director says the
/// anchor is off (the controller hands its physics step to <see cref="Step"/>).
/// </summary>
public partial class MoveModeFloat : Node
{
	public float SwimSpeed = 300f;
	/// <summary> How quickly you reach swim speed (and drift to a stop). Higher is snappier. </summary>
	public float Response = 3.5f;
	/// <summary> How high above the floor you can float before the water pushes you back down. </summary>
	public float Ceiling = 650f;
	/// <summary> Testing aid: swim in this direction (world space) as if keys were held. </summary>
	public Vector3? TestInput;

	PlayerController controller;

	public static bool IsActive => WaveDirector.Current?.FloatRound ?? false;

	public bool Active => IsActive && (controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.IsAlive ?? true);

	public override void _Ready()
	{
		Name = "MoveModeFloat";
		controller = GetParent<PlayerController>();
	}

	/// <summary> Where the player wants to swim, in world space, at full speed (m/s). </summary>
	public Vector3 WishSwim()
	{
		if ( TestInput is { } test ) return test.Normalized() * SwimSpeed * U.In;
		if ( !controller.UseInputControls ) return Vector3.Zero;
		var eyes = controller.EyeAngles.ToBasis();
		var move = Input.GetVector( "left", "right", "backward", "forward" );
		var wish = -eyes.Z * move.Y + eyes.X * move.X;
		if ( Input.IsActionPressed( "jump" ) ) wish += Vector3.Up;
		if ( Input.IsActionPressed( "duck" ) ) wish += Vector3.Down;
		var speed = Input.IsActionPressed( "run" ) ? SwimSpeed * 1.45f : SwimSpeed;
		return wish.IsNearlyZero() ? Vector3.Zero : wish.Normalized() * speed * U.In;
	}

	/// <summary> One physics step of floating: the controller's new velocity. </summary>
	public Vector3 Step( float dt )
	{
		var velocity = controller.Velocity;
		var target = WishSwim();
		var rate = target.IsNearlyZero() ? Response * 0.6f : Response;
		velocity = velocity.Lerp( target, MathX.Clamp( rate * dt, 0f, 1f ) );
		// Don't drift out of the arena: the water pushes back near the top.
		var feet = controller.GlobalPosition;
		var floor = Trace.Ray( controller, feet + Vector3.Up * 5f * U.In, feet + Vector3.Down * 2000f * U.In, Layers.World, new Godot.Collections.Array<Rid> { controller.GetRid() } );
		var height = floor.Hit ? (feet.Y - floor.HitPosition.Y) / U.In : 0f;
		if ( height > Ceiling ) velocity.Y = MathF.Min( velocity.Y, -60f * (height - Ceiling) / 100f * U.In );
		return velocity;
	}
}
