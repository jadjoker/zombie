/// <summary>
/// A creature that takes bullets per body part (Bloober segments, Squilien tentacles...).
/// </summary>
public interface ICreature
{
	bool IsDead { get; }
	void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit );
}

/// <summary>
/// Sits on each shootable piece of a creature so bullets know which creature, and which part of it,
/// they hit. The piece has a trigger collider tagged "creature".
/// </summary>
public sealed class CreaturePart : Component, IShootable
{
	public Component Creature { get; set; }
	public int Index { get; set; }

	ICreature Owner => Creature as ICreature;

	public Component ShotOwner => Creature;
	public bool IsDead => !Creature.IsValid() || (Owner?.IsDead ?? true);

	public void ReceiveShot( float damage, GameObject hitObject, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( Creature.IsValid() ) Owner?.ReceiveShot( Index, damage, direction, position, origin, shooter, scoreHit );
	}
}
