using Godot;

/// <summary>
/// The playable robots. Each has its own weapons and its own mouse 4 move; both swing the axe. Ram-Bot is the
/// original kit (rifle, shotgun, the charged surge that rams through enemies). Vac-Bot carries the Gore Vac and the
/// Bubble Cannon and blinks with a reverse-thrust burst instead. Picked at the dock before the dive (ClassPicker),
/// or any time in the dev menu; saved with the settings (`game/robot_class`); `--class <name>` forces one.
/// Buy stations only sell what fits the robot (`Sells`): the trident and Gore Vac stations are for nobody now.
/// </summary>
public sealed class RobotClass
{
	public string Id, Name, Tagline, Weapons, Movement, Melee = "Axe";
	/// <summary> Health at the start of a run (upgrades add to it). </summary>
	public float BaseHealth = 100f;
	/// <summary> The paint job its body wears (PlayerRig), whatever skin is picked in the dev menu; null keeps that skin. </summary>
	public string Paint;
	public Color Tint;
	/// <summary> Mouse 4 is the quick reverse-thrust blink (Jetpack) instead of the charged surge. </summary>
	public bool Blink;
	public Func<PlayerWeapon[]> Kit;

	public static readonly RobotClass RamBot = new()
	{
		Id = "rambot", Name = "Ram-Bot", Tagline = "Guns up, head down.",
		Weapons = "Rifle, Shotgun", Movement = "Charged surge: hold mouse 4, let go to ram through enemies",
		Tint = new Color( 0.3f, 0.85f, 1f ),
		Kit = () => new[] { PlayerWeapon.Rifle(), PlayerWeapon.Shotgun() },
	};

	public static readonly RobotClass VacBot = new()
	{
		Id = "vacbot", Name = "Vac-Bot", Tagline = "Suck it up. Spit it back.",
		Weapons = "Gore Vac, Bubble Cannon", Movement = "Reverse-thrust blink: tap mouse 4 to burst the way you move (gunk goes further)",
		Tint = new Color( 0.55f, 1f, 0.25f ), Blink = true, Paint = "Vac",
		Kit = () => new[] { PlayerWeapon.GoreVac( 1 ), PlayerWeapon.BubbleCannon( 2 ) },
	};

	public static readonly RobotClass TridentBot = new()
	{
		Id = "tridentbot", Name = "Trident-Bot", Tagline = "Hold the line. Skewer what crosses it.",
		Weapons = "Trident Shooter, Trident & Shield", Movement = "Charged surge: hold mouse 4, let go to ram through enemies",
		Melee = "One-handed trident thrust (twice the axe's reach); the shield in the other hand blocks 65% on right click; axe on mouse 5",
		Tint = new Color( 0.12f, 0.75f, 0.72f ), BaseHealth = 250f, Paint = "Trident",
		Kit = () => new[] { PlayerWeapon.Trident( 1 ), PlayerWeapon.TridentSpear( 2 ) },
	};

	public static readonly RobotClass[] All = { RamBot, VacBot, TridentBot };

	public static RobotClass Find( string id ) => All.FirstOrDefault( c => string.Equals( c.Id, id, StringComparison.OrdinalIgnoreCase ) || string.Equals( c.Name, id, StringComparison.OrdinalIgnoreCase ) ) ?? RamBot;

	/// <summary> The robot being played. </summary>
	public static RobotClass Current => Find( GameSettings.RobotClass );

	/// <summary> Fires after a robot is picked and its kit handed out. </summary>
	public static event Action Changed;

	/// <summary> Whether a buy station is for this robot. Weapons come with the robot, so no robot buys a weapon. </summary>
	public static bool Sells( BuyStationKind kind ) => kind is not (BuyStationKind.Trident or BuyStationKind.GoreVac);

	/// <summary> Picks a robot and gives the player its weapons in place of the old ones (damage upgrades are kept). </summary>
	public static void Pick( RobotClass robot, PlayerController player = null )
	{
		GameSettings.RobotClass = robot.Id;
		GameSettings.Save();
		if ( player is not null && IsInstanceValid( player ) ) player.GetNodeOrNull<WeaponInventory>( "WeaponInventory" )?.Replace( robot.Kit() );
		Changed?.Invoke();
	}

	static bool IsInstanceValid( GodotObject o ) => GodotObject.IsInstanceValid( o );
}
