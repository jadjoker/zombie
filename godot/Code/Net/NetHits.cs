using Godot;

/// <summary>
/// Who applies a hit when playing online (<see cref="Net"/>). Enemies live on the host, so a hit an enemy lands on
/// another player's robot is sent to that player's machine and taken there (where its shield, skills and health are).
/// Anything else that touches a remote robot here (a sea mine, a glow bulb) is left to its owner, who has the same
/// hazards in their own world.
/// </summary>
public static class NetHits
{
	public static void PlayerHurt( PlayerController remote, DamageInfo damage )
	{
		if ( !Net.IsHost || damage.Attacker is not Creature ) return;
		Net.Instance?.SendHurt( remote.PeerId, damage );
	}
}
