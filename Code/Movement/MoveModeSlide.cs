using Sandbox.Movement;

/// <summary>
/// Ground movement during a slide, dash or momentum carry. Reuses the walk mode's ground handling
/// (sticking, steps), but instead of walking toward your input it feeds in PlayerMovement's velocity.
/// Takes over whenever PlayerMovement says one of those moves is active.
/// </summary>
public sealed class MoveModeSlide : MoveModeWalk
{
	PlayerMovement Movement => GetComponent<PlayerMovement>();

	public override int Score( PlayerController controller )
	{
		var movement = Movement;
		return movement.IsValid() && movement.OverridesGround ? 1000 : -1000;
	}

	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		var movement = Movement;
		return movement.IsValid() ? movement.GroundVelocity : base.UpdateMove( eyes, input );
	}
}
