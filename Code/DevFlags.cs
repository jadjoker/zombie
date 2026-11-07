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
	/// <summary> Camera detached from the player and flown separately (the player stays put). </summary>
	public static bool FreeCam;
	public static Vector3 FreeCamPosition;
	public static Angles FreeCamAngles;
	/// <summary> Field of view override; 0 = the game's own. </summary>
	public static float Fov;
	/// <summary> Any hit on an enemy kills it. </summary>
	public static bool OneShot;
	public static bool ShowHitboxes;
	public static bool ShowStats;

	public static void Reset()
	{
		FreezeEnemies = Noclip = Placing = HideHud = HideWeapon = FreeCam = OneShot = ShowHitboxes = ShowStats = false;
		Fov = 0f;
	}
}
