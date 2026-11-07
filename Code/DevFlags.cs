/// <summary>
/// Editor-only dev switches the dev menu (P) flips. Game code checks these; they never turn on in a published build.
/// </summary>
public static class DevFlags
{
	/// <summary> Enemies stop thinking: no moving, no attacking. Their idle animation keeps playing; projectiles hang in the air. </summary>
	public static bool FreezeEnemies;
	/// <summary> Fly through walls (the dev menu's noclip drives the player). </summary>
	public static bool Noclip;
	/// <summary> Placing enemies: left click spawns at the crosshair, so the gun doesn't fire. </summary>
	public static bool Placing;
	public static bool HideHud;
	public static bool HideWeapon;

	public static void Reset()
	{
		FreezeEnemies = Noclip = Placing = HideHud = HideWeapon = false;
	}
}
