using Sandbox.Movement;

/// <summary>
/// Ground movement while sliding. Reuses the walk mode's ground handling (sticking, steps, friction),
/// but instead of walking toward your input it feeds in the slide's decaying momentum.
/// Takes over whenever PlayerMovement says a slide is active.
/// </summary>
public sealed class MoveModeSlide : MoveModeWalk
{
	PlayerMovement Movement => GetComponent<PlayerMovement>();

	public override int Score( PlayerController controller )
	{
		var movement = Movement;
		return movement.IsValid() && movement.IsSliding ? 1000 : -1000;
	}

	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		var movement = Movement;
		return movement.IsValid() ? movement.SlideVelocity : base.UpdateMove( eyes, input );
	}
}
