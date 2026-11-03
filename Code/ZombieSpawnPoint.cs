/// <summary>
/// Drop this on an empty object to mark where zombies can come from.
/// With none in the scene, zombies spawn at random navmesh points around the player.
/// </summary>
public sealed class ZombieSpawnPoint : Component
{
	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = Color.Green.WithAlpha( 0.6f );
		Gizmo.Draw.LineCylinder( Vector3.Zero, Vector3.Up * 72f, 16f, 16f, 12 );
	}
}
