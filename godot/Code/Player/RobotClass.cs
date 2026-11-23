using Godot;

/// <summary>
/// The playable robots. Each has its own weapons and its own mouse 4 move; both swing the axe. Ram-Bot is the
/// original kit (rifle, shotgun, the charged surge that rams through enemies). Vac-Bot carries the Gore Vac and the
/// Bubble Cannon and blinks with a reverse-thrust burst instead. Hook-Bot reels enemies in on a harpoon line, lures them to a lantern and grapples about; Coral-Bot
/// encrusts them in coral, plants turrets and walls, and burrows. Magnet-Bot balls up scrap and smashes tagged enemies
/// together; Plunger-Bot sticks plungers on them and flushes them; Pinball-Bot bounces steel balls and flips them. Picked at the dock before the dive (ClassPicker),
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
	/// <summary> Mouse 4 casts a grapple line and zips you along it (Hook-Bot, `JetpackMoves.cs`). </summary>
	public bool Grapple;
	/// <summary> Mouse 4 digs you into the sand and erupts (Coral-Bot, `JetpackMoves.cs`). </summary>
	public bool Burrow;
	/// <summary> Mouse 4 throws enemies back with a magnetic pulse (Magnet-Bot, `JetpackToys.cs`). </summary>
	public bool Pulse;
	/// <summary> Mouse 4 flings you off a plunger on a bungee (Plunger-Bot). </summary>
	public bool Sling;
	/// <summary> Mouse 4 curls you into a ball that bowls enemies over (Pinball-Bot). </summary>
	public bool Roll;
	/// <summary> Mouse 4 is a tap move of its own, not the charged surge. </summary>
	public bool OwnMove => Blink || Grapple || Burrow || Pulse || Sling || Roll;
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
		Melee = "Trident thrust (2x axe reach), shield blocks 65% (right click)",
		Tint = new Color( 0.12f, 0.75f, 0.72f ), BaseHealth = 250f, Paint = "Trident",
		Kit = () => new[] { PlayerWeapon.Trident( 1 ), PlayerWeapon.TridentSpear( 2 ) },
	};

	public static readonly RobotClass HookBot = new()
	{
		Id = "hookbot", Name = "Hook-Bot", Tagline = "Reel 'em in.",
		Weapons = "Harpoon Reel, Lantern Lure", Movement = "Grapple zip: tap mouse 4 to cast a line and zip to where it bites (an enemy gets a boot)",
		Tint = new Color( 1f, 0.58f, 0.2f ), BaseHealth = 150f, Paint = "Hook", Grapple = true,
		Kit = () => new[] { PlayerWeapon.HarpoonReel( 1 ), PlayerWeapon.LanternLure( 2 ) },
	};

	public static readonly RobotClass CoralBot = new()
	{
		Id = "coralbot", Name = "Coral-Bot", Tagline = "Grow the reef. Hold the reef.",
		Weapons = "Spine Gun, Reef Seeds", Movement = "Burrow: tap mouse 4 on the ground to dig in (untouchable, fast), tap again or wait to erupt",
		Tint = new Color( 1f, 0.45f, 0.52f ), BaseHealth = 200f, Paint = "Coral", Burrow = true,
		Kit = () => new[] { PlayerWeapon.SpineGun( 1 ), PlayerWeapon.ReefSeeds( 2 ) },
	};

	public static readonly RobotClass MagnetBot = new()
	{
		Id = "magnetbot", Name = "Magnet-Bot", Tagline = "Opposites attract. Violently.",
		Weapons = "Scrap Cannon, Polarity Gun", Movement = "Repulsor pulse: tap mouse 4 to throw every enemy near you back",
		Tint = new Color( 1f, 0.3f, 0.28f ), BaseHealth = 175f, Paint = "Magnet", Pulse = true,
		Kit = () => new[] { PlayerWeapon.ScrapCannon( 1 ), PlayerWeapon.PolarityGun( 2 ) },
	};

	public static readonly RobotClass PlungerBot = new()
	{
		Id = "plungerbot", Name = "Plunger-Bot", Tagline = "Unclog the deep.",
		Weapons = "Plunger Launcher, Flush Bomb", Movement = "Slingshot: tap mouse 4 to fling yourself off a plunger on a bungee (land on them for a belly flop)",
		Tint = new Color( 0.4f, 0.8f, 1f ), BaseHealth = 175f, Paint = "Plunger", Sling = true,
		Kit = () => new[] { PlayerWeapon.PlungerLauncher( 1 ), PlayerWeapon.FlushBombs( 2 ) },
	};

	public static readonly RobotClass PinballBot = new()
	{
		Id = "pinballbot", Name = "Pinball-Bot", Tagline = "Tilt the odds.",
		Weapons = "Pinball Launcher, Flipper Arms", Movement = "Roll: tap mouse 4 to curl into a ball and bowl enemies over",
		Tint = new Color( 1f, 0.35f, 0.88f ), BaseHealth = 150f, Paint = "Pinball", Roll = true,
		Kit = () => new[] { PlayerWeapon.PinballLauncher( 1 ), PlayerWeapon.FlipperArms( 2 ) },
	};

	public static readonly RobotClass[] All = { RamBot, VacBot, TridentBot, HookBot, CoralBot, MagnetBot, PlungerBot, PinballBot };

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
