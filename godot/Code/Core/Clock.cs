using Godot;

/// <summary>
/// Game time (scaled by Engine.TimeScale, like s&box Time.Now). Autoloaded so it ticks before gameplay nodes.
/// </summary>
public partial class Clock : Node
{
	public static double Now { get; private set; }

	/// <summary> Unscaled wall-clock seconds (s&box RealTime.Now). </summary>
	public static double Real => Time.GetTicksUsec() / 1_000_000.0;

	public override void _Ready()
	{
		ProcessPriority = -1000;
		ProcessPhysicsPriority = -1000;
	}

	double lastPhysics;

	public override void _Process( double delta ) => Now += delta;

	public override void _PhysicsProcess( double delta )
	{
		// Physics ticks can run several times per frame; keep Now moving with them so timers inside ticks advance.
		lastPhysics += delta;
		if ( lastPhysics > Now ) Now = lastPhysics;
	}
}
