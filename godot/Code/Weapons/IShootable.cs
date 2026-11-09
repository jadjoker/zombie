using Godot;

/// <summary>
/// Port of s&box IShootable: anything that handles bullet and melee hits itself (creature parts, test targets).
/// Found on the collider that was hit or any of its parents.
/// </summary>
public interface IShootable
{
	/// <summary> The creature (or object) the hit belongs to; one shot scores once per owner. </summary>
	Node ShotOwner { get; }
	bool IsDead { get; }
	void ReceiveShot( float damage, Node3D hitPart, Vector3 direction, Vector3 position, Vector3 origin, Node attacker, bool scoreHit );
}

public static class Shootables
{
	/// <summary> The IShootable on this node or the nearest parent that has one. </summary>
	public static IShootable Find( Node node )
	{
		for ( var n = node; n is not null; n = n.GetParent() )
			if ( n is IShootable s ) return s;
		return null;
	}
}
