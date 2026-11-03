/// <summary>
/// Something the player's guns can hit that handles the bullet itself (creature parts). Put it on the
/// object that has the collider; the weapon finds it on whatever the shot hit.
/// </summary>
public interface IShootable
{
	/// <summary> The whole creature this part belongs to, so a shotgun blast scores once per creature. </summary>
	Component ShotOwner { get; }
	bool IsDead { get; }

	void ReceiveShot( float damage, GameObject hitObject, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit );
}
