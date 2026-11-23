using Godot;

/// <summary> Every skill in the robots' skill trees. </summary>
public enum SkillId
{
	// Ram-Bot: rifle
	FireRate, FasterReload, ElectricBullets, DeepMag, ExplosiveRound, Headhunter, Overclock,
	// Ram-Bot: shotgun
	WideLoad, PointBlank, ShellShuffle, Kickback, DragonsBreath, LimbRipper, FullChoke,
	// Ram-Bot: core
	MovementBuff, ThickPlating, HeavyHitter, QuickCharge, BidenBlast, Lumberjack, Unstoppable,
	// Trident-Bot: trident shooter
	QuickDraw, TridentPierce, TridentReach, Riptide, TridentSwarm, TidalBurst, PoseidonsFork,
	// Trident-Bot: spear and shield
	ShieldBulwark, LongSpear, BarbedTip, ShieldBash, Riposte, MirrorShield, Spartan,
	// Trident-Bot: core
	TridentShield, SeaLegs, LongLunge, VioletEdge, Undertow, PlecoPals, KrakensBlessing,
	// Vac-Bot: Gore Vac
	BigBelly, IndustrialSuction, Gourmet, Shredder, BiggerBooms, Clusterfunk, BlackHole,
	// Vac-Bot: Bubble Cannon
	LongLasting, Bouncy, BigGulp, HighLift, ChainReaction, Fizz, BubblePrison,
	// Vac-Bot: core
	VacShield, LightFrame, EfficientJets, Afterburner, ToxicTrail, Slipstream, PhaseShift,
	// Hook-Bot: Harpoon Reel
	BarbedHooks, QuickReel, DeepQuiver, Snag, TowLine, GuttingHook, Trawler,
	// Hook-Bot: Lantern Lure
	BrighterBulb, LongGlow, ShockLantern, DeepPockets, ChumBucket, BaitAndSwitch, AbyssalBloom,
	// Hook-Bot: core
	ThickLine, LongCast, Momentum, BootStomp, ReelRecharge, SeaDog, LeviathanPull,
	// Coral-Bot: Spine Gun
	SharpSpines, FastGrowth, BrittleBurst, HeavyClusters, ToxicPolyps, Ricochet, Bleach,
	// Coral-Bot: Reef Seeds
	ToughPolyps, QuickSeed, Barrage, HealingReef, SpikedWalls, Anemone, LivingReef,
	// Coral-Bot: core
	ShellPlating, DeepDig, Upheaval, Sandstorm, Calcify, Regrowth, GreatBarrier,
	// Magnet-Bot: Scrap Cannon
	HeavyMetal, QuickGather, Bouncer, Shrapnel, WreckingBall, JunkMagnet, Katamari,
	// Magnet-Bot: Polarity Gun
	StrongField, LongCharge, HardSmash, StaticCling, ArcWeld, SpreadCharge, Magnetar,
	// Magnet-Bot: core
	IronPlating, Maglev, Overcharge, Capacitor, ScrapArmour, Discharge, PoleReversal,
	// Plunger-Bot: Plunger Launcher
	ExtraSuction, RapidPlunge, PlungerPouch, Clingy, Shlorp, TugOfWar, PlumbersNightmare,
	// Plunger-Bot: Flush Bomb
	StrongFlush, LongFlush, WiderBowl, DoubleFlush, Geyser, Swirly, Clogged,
	// Plunger-Bot: core
	RubberPlating, SpringyLegs, LongBungee, Elastic, BellyFlop, BounceBack, Cannonball,
	// Pinball-Bot: Pinball Launcher
	HeavySteel, SuperSpring, ExtraBall, Bumper, Jackpot, MagneticSave, Multiball,
	// Pinball-Bot: Flipper Arms
	PowerFlip, WideFlipper, QuickFlip, Combo, LaunchPad, BallReturn, Ramp,
	// Pinball-Bot: core
	ChromePlating, BumperGuard, SpinUp, LongRoll, Strike, TiltBonus, HighScore,
}

/// <summary> One skill: where it sits, how far it goes, and what each point does. </summary>
public sealed class SkillDef
{
	public SkillId Id;
	public string Robot;
	/// <summary> 0 the primary weapon, 1 the secondary, 2 the core (the robot's move and survival). </summary>
	public int Tree;
	/// <summary> 0 to 2 are the skill rows; 3 is the capstone. </summary>
	public int Row;
	public string Name, Icon;
	public int Max = 5;
	public Color Colour;
	/// <summary> The old pickup this skill now gives (its effect code reads `PlayerPerks.Count`). </summary>
	public PerkKind? Perk;
	/// <summary> What it does at a rank (1 up to Max). </summary>
	public Func<int, string> Says;
	public bool Capstone => Row == Skills.Rows;
}

/// <summary>
/// The skill trees (Borderlands style): each robot has three, its primary weapon, its secondary and its core. Each
/// tree has three rows of two skills and a capstone. A skill takes up to five points (three for the strongest), and
/// `UnlockPoints` points in a row open the next. Clearing a wave earns a token, up to `MaxTokens` a run; the pause
/// menu spends them (`SkillTreePage`). A new run clears them (no refunds, user's choice).
/// The skills that were pickups (fire rate, the shield, the trident perks and so on) keep their code in PlayerPerks:
/// `PlayerPerks.Count` adds their points.
/// </summary>
public static class Skills
{
	public const int Rows = 3, UnlockPoints = 3, MaxTokens = 50;

	public static readonly string[][] TreeNames =
	{
		new[] { "Trigger Happy", "Buckshot Brawler", "Battering Ram" },
		new[] { "Wrath of the Deep", "Phalanx", "Tidal Surge" },
		new[] { "Gross Domestic Product", "Bath Time", "Reverse Thrust" },
		new[] { "Catch of the Day", "Bright Ideas", "Old Salt" },
		new[] { "Prickly", "Gardener", "Bedrock" },
		new[] { "Scrapyard", "Opposites Attract", "Field Theory" },
		new[] { "Suction Cup", "Royal Flush", "Bungee Jumper" },
		new[] { "Silver Ball", "Flippin' Out", "Tilt" },
	};
	public static readonly string[][] TreeKit =
	{
		new[] { "Rifle", "Shotgun", "Surge, axe, survival" },
		new[] { "Trident Shooter", "Spear and Shield", "Surge, survival" },
		new[] { "Gore Vac", "Bubble Cannon", "Blink, survival" },
		new[] { "Harpoon Reel", "Lantern Lure", "Grapple, survival" },
		new[] { "Spine Gun", "Reef Seeds", "Burrow, survival" },
		new[] { "Scrap Cannon", "Polarity Gun", "Pulse, survival" },
		new[] { "Plunger Launcher", "Flush Bomb", "Slingshot, survival" },
		new[] { "Pinball Launcher", "Flipper Arms", "Roll, survival" },
	};

	static int RobotIndex( string robot ) => robot == RobotClass.TridentBot.Id ? 1 : robot == RobotClass.VacBot.Id ? 2
		: robot == RobotClass.HookBot.Id ? 3 : robot == RobotClass.CoralBot.Id ? 4 : robot == RobotClass.MagnetBot.Id ? 5
		: robot == RobotClass.PlungerBot.Id ? 6 : robot == RobotClass.PinballBot.Id ? 7 : 0;

	/// <summary> The skills that add max health (bought: more health at once). </summary>
	public static bool AddsHealth( SkillId id ) => id is SkillId.ThickPlating or SkillId.ThickLine or SkillId.ShellPlating
		or SkillId.IronPlating or SkillId.RubberPlating or SkillId.ChromePlating;
	public static string TreeName( string robot, int tree ) => TreeNames[RobotIndex( robot )][tree];
	public static string TreeKitName( string robot, int tree ) => TreeKit[RobotIndex( robot )][tree];

	static string Pct( float v ) => $"{v * 100f:0}%";

	static readonly Color Orange = new( 1f, 0.55f, 0.16f ), Blue = new( 0.27f, 0.59f, 1f ), Violet = new( 0.59f, 0.31f, 1f ),
		Red = new( 0.9f, 0.24f, 0.2f ), Green = new( 0.24f, 0.78f, 0.43f ), Gold = new( 1f, 0.8f, 0.24f ), Teal = new( 0.12f, 0.75f, 0.72f ),
		Sky = new( 0.16f, 0.75f, 1f ), Pink = new( 1f, 0.42f, 0.55f ), Lime = new( 0.55f, 1f, 0.25f ), Steel = new( 0.6f, 0.68f, 0.78f ), Ink = new( 0.35f, 0.2f, 0.6f );

	static SkillDef S( SkillId id, string robot, int tree, int row, string name, string icon, Color colour, Func<int, string> says, int max = 5, PerkKind? perk = null )
		=> new() { Id = id, Robot = robot, Tree = tree, Row = row, Name = name, Icon = icon, Colour = colour, Says = says, Max = max, Perk = perk };

	public static readonly SkillDef[] All = Build();

	static SkillDef[] Build()
	{
		var ram = RobotClass.RamBot.Id;
		var tri = RobotClass.TridentBot.Id;
		var vac = RobotClass.VacBot.Id;
		var hook = RobotClass.HookBot.Id;
		var coral = RobotClass.CoralBot.Id;
		var mag = RobotClass.MagnetBot.Id;
		var plu = RobotClass.PlungerBot.Id;
		var pin = RobotClass.PinballBot.Id;
		var Magenta = new Color( 1f, 0.3f, 0.85f );
		return new[]
		{
			// ---- Ram-Bot: rifle ----
			S( SkillId.FireRate, ram, 0, 0, "Fire Rate", "firerate", Orange, n => $"+{Pct( PlayerPerks.FireRatePerStack * n )} rifle fire rate", perk: PerkKind.FireRate ),
			S( SkillId.FasterReload, ram, 0, 0, "Faster Reload", "reload", Blue, n => $"-{Pct( PlayerPerks.ReloadPerStack * n )} rifle reload time", perk: PerkKind.Reload ),
			S( SkillId.ElectricBullets, ram, 0, 1, "Electric Bullets", "electric", Violet, n => $"{Pct( PlayerPerks.ShockChance + PlayerPerks.ShockChancePerStack * (n - 1) )} chance a rifle hit shocks for {PlayerPerks.ShockDamage + PlayerPerks.ShockDamagePerStack * (n - 1):0}, jumping to enemies near by", perk: PerkKind.Electric ),
			S( SkillId.DeepMag, ram, 0, 1, "Deep Mag", "deepmag", Steel, n => $"+{Pct( SkillFx.DeepMagPer * n )} rifle magazine" ),
			S( SkillId.ExplosiveRound, ram, 0, 2, "Explosive Round", "explosive", Red, n => $"An emptied magazine is thrown, seeks an enemy and blows for {PlayerPerks.ClipDamage + PlayerPerks.ClipDamagePerStack * (n - 1):0}", perk: PerkKind.Explosive ),
			S( SkillId.Headhunter, ram, 0, 2, "Headhunter", "headhunter", Gold, n => $"+{Pct( SkillFx.HeadhunterPer * n )} rifle damage to weak spots" ),
			S( SkillId.Overclock, ram, 0, 3, "Overclock", "overclock", Orange, n => $"After a reload the next {SkillFx.OverclockShots( n )} rifle shots fire {Pct( SkillFx.OverclockRate )} faster and always shock", max: 3 ),
			// ---- Ram-Bot: shotgun ----
			S( SkillId.WideLoad, ram, 1, 0, "Wide Load", "wideload", Orange, n => $"+{n} shotgun pellet{(n == 1 ? "" : "s")}" ),
			S( SkillId.PointBlank, ram, 1, 0, "Point Blank", "pointblank", Red, n => $"+{Pct( SkillFx.PointBlankPer * n )} shotgun damage within {SkillFx.PointBlankRange:0} m" ),
			S( SkillId.ShellShuffle, ram, 1, 1, "Shell Shuffle", "shellshuffle", Blue, n => $"-{Pct( SkillFx.ShellShufflePer * n )} shotgun reload time" ),
			S( SkillId.Kickback, ram, 1, 1, "Kickback", "kickback", Sky, n => $"A shotgun blast shoves enemies {SkillFx.KickbackPer * n:0.0} m back" ),
			S( SkillId.DragonsBreath, ram, 1, 2, "Dragon's Breath", "dragon", Orange, n => $"Shotgun hits set enemies alight: {SkillFx.BurnPer * n:0} damage a second for {SkillFx.BurnTime:0} s" ),
			S( SkillId.LimbRipper, ram, 1, 2, "Limb Ripper", "limbripper", Pink, n => $"+{Pct( SkillFx.LimbPer * n )} shotgun damage to limbs" ),
			S( SkillId.FullChoke, ram, 1, 3, "Full Choke", "fullchoke", Gold, n => $"Every {SkillFx.ChokeEvery}th shot is a slug through every enemy in a line, {SkillFx.ChokeScale( n ):0}x a pellet's damage", max: 3 ),
			// ---- Ram-Bot: core ----
			S( SkillId.MovementBuff, ram, 2, 0, "Movement Buff", "move", Green, n => $"+{PlayerPerks.MoveSpeedPerStack * n:0.0} m/s and +{Pct( PlayerPerks.StaminaPerStack * n )} stamina", perk: PerkKind.Move ),
			S( SkillId.ThickPlating, ram, 2, 0, "Thick Plating", "plating", Steel, n => $"+{SkillFx.PlatingPer * n:0} max health" ),
			S( SkillId.HeavyHitter, ram, 2, 1, "Heavy Hitter", "heavyhitter", Red, n => $"+{Pct( SkillFx.RamDamagePer * n )} surge damage" ),
			S( SkillId.QuickCharge, ram, 2, 1, "Quick Charge", "quickcharge", Sky, n => $"+{Pct( SkillFx.RechargePer * n )} surge power recharge" ),
			S( SkillId.BidenBlast, ram, 2, 2, "Biden Blast", "blast", new Color( 0.16f, 0.35f, 0.9f ), n => $"{n} blue blast{(n == 1 ? "" : "s")} a wave on {Controls.Label( "blast" )}: {PlayerPerks.BlastDamage:0} to every enemy within {PlayerPerks.BlastRadius:0} m", max: 3, perk: PerkKind.Blast ),
			S( SkillId.Lumberjack, ram, 2, 2, "Lumberjack", "lumberjack", Green, n => $"+{Pct( SkillFx.AxePer * n )} axe damage, and each axe hit heals {SkillFx.AxeHealPer * n:0}" ),
			S( SkillId.Unstoppable, ram, 2, 3, "Unstoppable", "unstoppable", Red, n => $"The surge doesn't slow ramming through enemies, and each kill in it gives back {SkillFx.UnstoppableRefund( n ):0} power", max: 3 ),
			// ---- Trident-Bot: trident shooter ----
			S( SkillId.QuickDraw, tri, 0, 0, "Quick Draw", "quickdraw", Teal, n => $"-{Pct( SkillFx.QuickDrawPer * n )} charge time" ),
			S( SkillId.TridentPierce, tri, 0, 0, "Trident Pierce", "pierce", Teal, n => $"Tridents seek {n} more enem{(n == 1 ? "y" : "ies")}", perk: PerkKind.Pierce ),
			S( SkillId.TridentReach, tri, 0, 1, "Trident Reach", "reach", Violet, n => $"+{PlayerPerks.ReachPerStack * n:0} m seeking range", perk: PerkKind.Reach ),
			S( SkillId.Riptide, tri, 0, 1, "Riptide", "riptide", Sky, n => $"+{Pct( SkillFx.RiptidePer * n )} seeking speed" ),
			S( SkillId.TridentSwarm, tri, 0, 2, "Trident Swarm", "swarm", Gold, n => n switch { 1 => "30% chance of a small trident off the first hit", 2 => "50% chance of one small trident, 30% of two", _ => "70% chance of one small trident, 50% of two, 30% of three" }, max: 3, perk: PerkKind.Swarm ),
			S( SkillId.TidalBurst, tri, 0, 2, "Tidal Burst", "tidalburst", Sky, n => $"A full charge bursts where it lands: {SkillFx.TidalPer * n:0} to enemies within {SkillFx.TidalRadius:0} m" ),
			S( SkillId.PoseidonsFork, tri, 0, 3, "Poseidon's Fork", "fork", Gold, n => $"A full charge throws two more tridents in a fan at {Pct( SkillFx.ForkShare( n ) )} damage", max: 3 ),
			// ---- Trident-Bot: spear and shield ----
			S( SkillId.ShieldBulwark, tri, 1, 0, "Shield Bulwark", "bulwark", new Color( 0.35f, 0.85f, 1f ), n => $"The raised shield takes {Pct( PlayerWeapon.BlockShare + PlayerPerks.BulwarkPerStack * n )} of each hit", perk: PerkKind.Bulwark ),
			S( SkillId.LongSpear, tri, 1, 0, "Long Spear", "longspear", Teal, n => $"+{Pct( SkillFx.LongSpearPer * n )} thrust reach" ),
			S( SkillId.BarbedTip, tri, 1, 1, "Barbed Tip", "barbed", Red, n => $"Thrusts make enemies bleed {SkillFx.BleedPer * n:0} a second for {SkillFx.BleedTime:0} s" ),
			S( SkillId.ShieldBash, tri, 1, 1, "Shield Bash", "bash", Sky, n => $"Raising the shield shoves enemies in front back and hits for {SkillFx.BashPer * n:0}" ),
			S( SkillId.Riposte, tri, 1, 2, "Riposte", "riposte", Gold, n => $"A blocked hit makes the next thrust deal +{Pct( SkillFx.RipostePer * n )}" ),
			S( SkillId.MirrorShield, tri, 1, 2, "Mirror Shield", "mirror", Violet, n => $"{Pct( SkillFx.MirrorPer * n )} chance a blocked hit flashes back at the attacker for double" ),
			S( SkillId.Spartan, tri, 1, 3, "Spartan", "spartan", Red, n => $"Thrust with the shield up, and enemies touching it take {SkillFx.SpartanDps( n ):0} a second", max: 3 ),
			// ---- Trident-Bot: core ----
			S( SkillId.TridentShield, tri, 2, 0, "Shield", "shield", Sky, n => $"A {PlayerPerks.ShieldFirst + PlayerPerks.ShieldPerStack * (n - 1):0} point energy shield soaks hits before health", perk: PerkKind.Shield ),
			S( SkillId.SeaLegs, tri, 2, 0, "Sea Legs", "sealegs", Green, n => $"+{SkillFx.SeaLegsPer * n:0.0} m/s move speed" ),
			S( SkillId.LongLunge, tri, 2, 1, "Long Lunge", "longlunge", Teal, n => $"+{Pct( SkillFx.LungePer * n )} surge distance" ),
			S( SkillId.VioletEdge, tri, 2, 1, "Violet Edge", "violetedge", Violet, n => $"+{Pct( SkillFx.RamDamagePer * n )} surge stab damage" ),
			S( SkillId.Undertow, tri, 2, 2, "Undertow", "undertow", Sky, n => $"The surge drags enemies within {SkillFx.UndertowReach( n ):0.0} m along behind you" ),
			S( SkillId.PlecoPals, tri, 2, 2, "Pleco Pals", "plecos", Green, n => $"+{Pct( SkillFx.PlecoPer * n )} healing" ),
			S( SkillId.KrakensBlessing, tri, 2, 3, "Kraken's Blessing", "kraken", Violet, n => $"The surge leaves a whirlpool that holds enemies for {SkillFx.WhirlTime( n ):0} s, {SkillFx.WhirlDps:0} damage a second", max: 3 ),
			// ---- Vac-Bot: Gore Vac ----
			S( SkillId.BigBelly, vac, 0, 0, "Big Belly", "belly", Lime, n => $"+{SkillFx.BellyPer * n} tank size" ),
			S( SkillId.IndustrialSuction, vac, 0, 0, "Industrial Suction", "suction", Sky, n => $"+{Pct( SkillFx.SuctionPer * n )} pull strength" ),
			S( SkillId.Gourmet, vac, 0, 1, "Gourmet", "gourmet", Pink, n => $"+{Pct( SkillFx.GourmetPer * n )} gunk from each piece" ),
			S( SkillId.Shredder, vac, 0, 1, "Shredder", "shredder", Red, n => $"Rips limbs off at {Pct( PlayerWeapon.RipBelow + SkillFx.ShredderPer * n )} health" ),
			S( SkillId.BiggerBooms, vac, 0, 2, "Bigger Booms", "explosive", Orange, n => $"+{Pct( SkillFx.BoomsPer * n )} shot blast radius" ),
			S( SkillId.Clusterfunk, vac, 0, 2, "Clusterfunk", "cluster", Gold, n => $"The huge shot throws {4 + n} bomblets", max: 3 ),
			S( SkillId.BlackHole, vac, 0, 3, "Black Hole", "blackhole", Ink, n => $"The huge shot opens a black hole for {BlackHole.Life( n ):0.0} s, dragging enemies within {BlackHole.Reach( n ):0} m into one spot, then blows {Pct( BlackHole.DamageScale( n ) - 1f )} harder", max: 3 ),
			// ---- Vac-Bot: Bubble Cannon ----
			S( SkillId.LongLasting, vac, 1, 0, "Long Lasting", "lasting", Sky, n => $"+{SkillFx.LastingPer * n:0} s before a trapped enemy's bubble pops" ),
			S( SkillId.Bouncy, vac, 1, 0, "Bouncy", "bouncy", Pink, n => $"+{n} bounce{(n == 1 ? "" : "s")} off the world" ),
			S( SkillId.BigGulp, vac, 1, 1, "Big Gulp", "biggulp", Teal, n => $"A Mega Bubble wraps up {BubbleShot.MegaCatches + n} enemies" ),
			S( SkillId.HighLift, vac, 1, 1, "High Lift", "highlift", Sky, n => $"Bubbles rise {Pct( SkillFx.LiftPer * n )} higher (a harder landing)" ),
			S( SkillId.ChainReaction, vac, 1, 2, "Chain Reaction", "chain", Orange, n => $"Each link of a pop chain is {Pct( BubbleTrap.ChainBonus + SkillFx.ChainPer * n )} harder" ),
			S( SkillId.Fizz, vac, 1, 2, "Fizz", "electric", Violet, n => $"A pop shocks {n} enem{(n == 1 ? "y" : "ies")} near it for {SkillFx.FizzDamage( n ):0}" ),
			S( SkillId.BubblePrison, vac, 1, 3, "Bubble Prison", "prison", Pink, n => $"Bubbled enemies take {Pct( SkillFx.PrisonScale( n ) - 1f )} more damage from everything", max: 3 ),
			// ---- Vac-Bot: core ----
			S( SkillId.VacShield, vac, 2, 0, "Shield", "shield", Sky, n => $"A {PlayerPerks.ShieldFirst + PlayerPerks.ShieldPerStack * (n - 1):0} point energy shield soaks hits before health", perk: PerkKind.Shield ),
			S( SkillId.LightFrame, vac, 2, 0, "Light Frame", "move", Green, n => $"+{PlayerPerks.MoveSpeedPerStack * n:0.0} m/s and +{Pct( PlayerPerks.StaminaPerStack * n )} stamina", perk: PerkKind.Move ),
			S( SkillId.EfficientJets, vac, 2, 1, "Efficient Jets", "jets", Sky, n => $"-{Pct( SkillFx.JetsPer * n )} blink cost" ),
			S( SkillId.Afterburner, vac, 2, 1, "Afterburner", "afterburner", Orange, n => $"Burning gunk carries a blink {Pct( SkillFx.AfterburnerPer * n )} farther" ),
			S( SkillId.ToxicTrail, vac, 2, 2, "Toxic Trail", "toxic", Lime, n => $"A blink leaves goo that hurts enemies {SkillFx.ToxicDps( n ):0} a second (double with gunk)" ),
			S( SkillId.Slipstream, vac, 2, 2, "Slipstream", "slipstream", Teal, n => $"+{Pct( SkillFx.SlipPer * n )} speed for {SkillFx.SlipTime:0} s after a blink" ),
			// ---- Hook-Bot: Harpoon Reel ----
			S( SkillId.BarbedHooks, hook, 0, 0, "Barbed Hooks", "barbed", Red, n => $"+{Pct( HookFx.BarbedPer * n )} harpoon damage" ),
			S( SkillId.QuickReel, hook, 0, 0, "Quick Reel", "reel", Sky, n => $"+{Pct( HookFx.QuickReelPer * n )} reel speed" ),
			S( SkillId.DeepQuiver, hook, 0, 1, "Deep Quiver", "deepmag", Steel, n => $"+{n} harpoon{(n == 1 ? "" : "s")} a load" ),
			S( SkillId.Snag, hook, 0, 1, "Snag", "limbripper", Pink, n => $"Reeling tears off a limb at or under {Pct( HookFx.SnagBelow( n ) )} health" ),
			S( SkillId.TowLine, hook, 0, 2, "Tow Line", "towline", Orange, n => $"A reeled enemy bowls through others on its way in: {HookFx.TowPer * n:0} damage and a shove" ),
			S( SkillId.GuttingHook, hook, 0, 2, "Gutting Hook", "gutting", Red, n => $"Hooked enemies bleed {HookFx.GutPer * n:0} a second for {HookFx.GutTime:0} s" ),
			S( SkillId.Trawler, hook, 0, 3, "Trawler", "trawler", Gold, n => $"The harpoon bursts into a net: up to {HookFx.TrawlerCatches( n )} more enemies within {HookFx.NetRadius:0.0} m are reeled in with it", max: 3 ),
			// ---- Hook-Bot: Lantern Lure ----
			S( SkillId.BrighterBulb, hook, 1, 0, "Brighter Bulb", "bulb", Gold, n => $"+{Pct( HookFx.BrighterPer * n )} lure zap and burst radius" ),
			S( SkillId.LongGlow, hook, 1, 0, "Long Glow", "lasting", Sky, n => $"The lure glows {HookFx.LongGlowPer * n:0.0} s longer" ),
			S( SkillId.ShockLantern, hook, 1, 1, "Shock Lantern", "electric", Violet, n => $"+{HookFx.ShockPer * n:0} lure zap damage" ),
			S( SkillId.DeepPockets, hook, 1, 1, "Deep Pockets", "pockets", Steel, n => $"Carry {n} more lure{(n == 1 ? "" : "s")}" ),
			S( SkillId.ChumBucket, hook, 1, 2, "Chum Bucket", "gourmet", Pink, n => $"+{Pct( HookFx.ChumPer * n )} lure burst damage, and it sprays chum" ),
			S( SkillId.BaitAndSwitch, hook, 1, 2, "Bait and Switch", "plecos", Green, n => $"Heal {HookFx.BaitHealPer * n:0} for each enemy a lure burst hits" ),
			S( SkillId.AbyssalBloom, hook, 1, 3, "Abyssal Bloom", "bloom", Lime, n => $"The lure's burst is a giant angler jaw from the sand: {HookFx.BloomDamage( n ):0} to everything within {HookFx.BloomRadius:0.0} m, dragged in first", max: 3 ),
			// ---- Hook-Bot: core ----
			S( SkillId.ThickLine, hook, 2, 0, "Thick Line", "plating", Steel, n => $"+{SkillFx.PlatingPer * n:0} max health" ),
			S( SkillId.LongCast, hook, 2, 0, "Long Cast", "reach", Teal, n => $"+{HookFx.LongCastPer * n:0} m grapple range" ),
			S( SkillId.Momentum, hook, 2, 1, "Momentum", "slipstream", Green, n => $"+{HookFx.MomentumPer * n:0.0} m/s for {HookFx.MomentumTime:0} s after a zip" ),
			S( SkillId.BootStomp, hook, 2, 1, "Boot Stomp", "kickback", Orange, n => $"+{HookFx.StompPer * n:0} damage when a zip ends in an enemy" ),
			S( SkillId.ReelRecharge, hook, 2, 2, "Reel Recharge", "quickcharge", Sky, n => $"-{Pct( HookFx.RechargePer * n )} grapple cost" ),
			S( SkillId.SeaDog, hook, 2, 2, "Sea Dog", "seadog", Gold, n => $"A kill within {HookFx.SeaDogTime:0} s of a zip heals {HookFx.SeaDogPer * n:0}" ),
			S( SkillId.LeviathanPull, hook, 2, 3, "Leviathan Pull", "leviathan", Ink, n => $"A zip into an enemy ends in a slam: {HookFx.LeviathanDamage( n ):0} to it, and enemies within {HookFx.LeviathanRadius:0} m are dragged in for half", max: 3 ),
			// ---- Coral-Bot: Spine Gun ----
			S( SkillId.SharpSpines, coral, 0, 0, "Sharp Spines", "spines", Red, n => $"+{Pct( CoralFx.SharpPer * n )} spine damage" ),
			S( SkillId.FastGrowth, coral, 0, 0, "Fast Growth", "growth", Green, n => $"Coral shatters at {CoralFx.ShatterAt( n )} spines (from {CoralFx.ShatterAt( 0 )})" ),
			S( SkillId.BrittleBurst, coral, 0, 1, "Brittle Burst", "explosive", Orange, n => $"+{Pct( CoralFx.BrittlePer * n )} shatter damage and reach" ),
			S( SkillId.HeavyClusters, coral, 0, 1, "Heavy Clusters", "clusters", Steel, n => $"Coral slows enemies up to {Pct( CoralFx.MaxSlow( n ) )}" ),
			S( SkillId.ToxicPolyps, coral, 0, 2, "Toxic Polyps", "toxic", Lime, n => $"A shatter poisons: {CoralFx.ToxicPer * n:0} a second for {CoralFx.ToxicTime:0} s" ),
			S( SkillId.Ricochet, coral, 0, 2, "Ricochet", "ricochet", Sky, n => $"{Pct( CoralFx.RicochetPer * n )} chance a spine bounces on to another enemy" ),
			S( SkillId.Bleach, coral, 0, 3, "Bleach", "bleach", new Color( 0.92f, 0.9f, 0.84f ), n => $"A shatter turns an enemy at or under {Pct( CoralFx.BleachBelow( n ) )} health to stone and breaks it", max: 3 ),
			// ---- Coral-Bot: Reef Seeds ----
			S( SkillId.ToughPolyps, coral, 1, 0, "Tough Polyps", "lasting", Sky, n => $"Turrets last {CoralFx.ToughPer * n:0} s longer" ),
			S( SkillId.QuickSeed, coral, 1, 0, "Quick Seed", "reload", Blue, n => $"-{Pct( CoralFx.QuickSeedPer * n )} seed reload time" ),
			S( SkillId.Barrage, coral, 1, 1, "Barrage", "firerate", Orange, n => $"+{Pct( CoralFx.BarragePer * n )} turret fire rate" ),
			S( SkillId.HealingReef, coral, 1, 1, "Healing Reef", "heal", Green, n => $"Heal {CoralFx.HealingPer * n:0.0} a second within {CoralFx.HealingReach:0} m of your coral" ),
			S( SkillId.SpikedWalls, coral, 1, 2, "Spiked Walls", "spikedwall", Red, n => $"Walls hurt enemies touching them {CoralFx.SpikedPer * n:0} a second and grow coral on them" ),
			S( SkillId.Anemone, coral, 1, 2, "Anemone", "anemone", Pink, n => $"+{Pct( CoralFx.AnemonePer * n )} turret damage" ),
			S( SkillId.LivingReef, coral, 1, 3, "Living Reef", "livingreef", Violet, n => $"Coral within {CoralFx.LinkReach:0} m links up: glowing veins hurt enemies crossing them {CoralFx.VeinDps( n ):0} a second, linked turrets fire {Pct( CoralFx.LinkRate( n ) - 1f )} faster", max: 3 ),
			// ---- Coral-Bot: core ----
			S( SkillId.ShellPlating, coral, 2, 0, "Shell Plating", "plating", Steel, n => $"+{SkillFx.PlatingPer * n:0} max health" ),
			S( SkillId.DeepDig, coral, 2, 0, "Deep Dig", "dig", new Color( 0.85f, 0.7f, 0.45f ), n => $"Burrow {CoralFx.DeepDigPer * n:0.00} s longer" ),
			S( SkillId.Upheaval, coral, 2, 1, "Upheaval", "upheaval", Orange, n => $"+{Pct( CoralFx.UpheavalPer * n )} eruption damage and reach" ),
			S( SkillId.Sandstorm, coral, 2, 1, "Sandstorm", "jets", Gold, n => $"-{Pct( CoralFx.SandstormPer * n )} burrow cost" ),
			S( SkillId.Calcify, coral, 2, 2, "Calcify", "calcify", new Color( 0.92f, 0.9f, 0.84f ), n => $"Standing still for {CoralFx.CalcifyAfter:0} s: take {Pct( CoralFx.CalcifyPer * n )} less damage" ),
			S( SkillId.Regrowth, coral, 2, 2, "Regrowth", "heal", Green, n => $"Erupting heals {CoralFx.RegrowthPer * n:0}" ),
			S( SkillId.GreatBarrier, coral, 2, 3, "Great Barrier", "barrier", Pink, n => $"An eruption throws up a ring of coral walls for {CoralFx.BarrierTime( n ):0} s and spears enemies inside it for {CoralFx.BarrierDamage( n ):0}", max: 3 ),
			// ---- Magnet-Bot: Scrap Cannon ----
			S( SkillId.HeavyMetal, mag, 0, 0, "Heavy Metal", "heavymetal", Steel, n => $"+{Pct( MagnetFx.HeavyMetalPer * n )} scrap ball damage" ),
			S( SkillId.QuickGather, mag, 0, 0, "Quick Gather", "quickcharge", Sky, n => $"-{Pct( MagnetFx.QuickGatherPer * n )} time to gather a full ball" ),
			S( SkillId.Bouncer, mag, 0, 1, "Bouncer", "bouncy", Orange, n => $"+{n} bounce{(n == 1 ? "" : "s")} off the world" ),
			S( SkillId.Shrapnel, mag, 0, 1, "Shrapnel", "shrapnel", Red, n => $"A spent ball bursts into {2 + n} shards at enemies within {MagnetFx.ShrapnelReach:0} m, {Pct( MagnetFx.ShrapnelPer * n )} of its damage each" ),
			S( SkillId.WreckingBall, mag, 0, 2, "Wrecking Ball", "wreckingball", Steel, n => $"+{Pct( MagnetFx.WreckingPer * n )} ball size (bigger knock back)" ),
			S( SkillId.JunkMagnet, mag, 0, 2, "Junk Magnet", "junkmagnet", Red, n => $"Each piece of gore gathered adds {Pct( MagnetFx.JunkPer + MagnetFx.JunkMagnetPer * n )} to the ball (from {Pct( MagnetFx.JunkPer )})" ),
			S( SkillId.Katamari, mag, 0, 3, "Katamari", "katamari", Gold, n => $"Light enemies the ball hits stick to it, up to {MagnetFx.KatamariHolds( n )}: each adds 25% damage, and they take half of it when it stops", max: 3 ),
			// ---- Magnet-Bot: Polarity Gun ----
			S( SkillId.StrongField, mag, 1, 0, "Strong Field", "suction", Blue, n => $"+{Pct( MagnetFx.StrongFieldPer * n )} pull between tags" ),
			S( SkillId.LongCharge, mag, 1, 0, "Long Charge", "lasting", Sky, n => $"Tags last {MagnetFx.LongChargePer * n:0} s longer" ),
			S( SkillId.HardSmash, mag, 1, 1, "Hard Smash", "smash", Red, n => $"+{Pct( MagnetFx.HardSmashPer * n )} smash damage" ),
			S( SkillId.StaticCling, mag, 1, 1, "Static Cling", "electric", Violet, n => $"Tagged enemies take {MagnetFx.ClingPer * n:0} a second" ),
			S( SkillId.ArcWeld, mag, 1, 2, "Arc Weld", "arcweld", Gold, n => $"A smash shocks {n} enem{(n == 1 ? "y" : "ies")} near it for {MagnetFx.ArcWeldDamage( n ):0}" ),
			S( SkillId.SpreadCharge, mag, 1, 2, "Spread Charge", "chain", Teal, n => $"{Pct( MagnetFx.SpreadPer * n )} chance a tag jumps to the nearest untagged enemy too" ),
			S( SkillId.Magnetar, mag, 1, 3, "Magnetar", "magnetar", Ink, n => $"A smash drags every enemy within {MagnetFx.MagnetarReach( n ):0} m into it for {MagnetFx.MagnetarDamage( n ):0}", max: 3 ),
			// ---- Magnet-Bot: core ----
			S( SkillId.IronPlating, mag, 2, 0, "Iron Plating", "plating", Steel, n => $"+{SkillFx.PlatingPer * n:0} max health" ),
			S( SkillId.Maglev, mag, 2, 0, "Maglev", "move", Green, n => $"+{PlayerPerks.MoveSpeedPerStack * n:0.0} m/s and +{Pct( PlayerPerks.StaminaPerStack * n )} stamina", perk: PerkKind.Move ),
			S( SkillId.Overcharge, mag, 2, 1, "Overcharge", "upheaval", Orange, n => $"+{Pct( MagnetFx.OverchargePer * n )} pulse damage and reach" ),
			S( SkillId.Capacitor, mag, 2, 1, "Capacitor", "jets", Sky, n => $"-{Pct( MagnetFx.CapacitorPer * n )} pulse cost" ),
			S( SkillId.ScrapArmour, mag, 2, 2, "Scrap Armour", "scraparmour", Steel, n => $"Take {Pct( MagnetFx.ArmourPer * n )} less damage for {MagnetFx.ArmourTime:0} s after a pulse" ),
			S( SkillId.Discharge, mag, 2, 2, "Discharge", "electric", Violet, n => $"A pulse shocks the {n} nearest enem{(n == 1 ? "y" : "ies")} within {MagnetFx.DischargeReach:0} m for {MagnetFx.DischargeDamage( n ):0}" ),
			S( SkillId.PoleReversal, mag, 2, 3, "Pole Reversal", "polereversal", Red, n => $"The pulse drags enemies in for a beat first, then blasts them for {MagnetFx.ReversalDamage( n ):0} more", max: 3 ),
			// ---- Plunger-Bot: Plunger Launcher ----
			S( SkillId.ExtraSuction, plu, 0, 0, "Extra Suction", "plunger", Red, n => $"+{Pct( PlungerFx.SuctionPer * n )} plunger damage" ),
			S( SkillId.RapidPlunge, plu, 0, 0, "Rapid Plunge", "firerate", Orange, n => $"+{Pct( PlungerFx.RapidPer * n )} fire rate" ),
			S( SkillId.PlungerPouch, plu, 0, 1, "Plunger Pouch", "deepmag", Steel, n => $"+{2 * n} plungers a load" ),
			S( SkillId.Clingy, plu, 0, 1, "Clingy", "clusters", Pink, n => $"Each stuck plunger slows its enemy {Pct( PlungerFx.ClingyPer * n )}" ),
			S( SkillId.Shlorp, plu, 0, 2, "Shlorp", "shlorp", Red, n => $"+{Pct( PlungerFx.ShlorpPer * n )} damage when {PlungerFx.PopAt:0} plungers pop off at once" ),
			S( SkillId.TugOfWar, plu, 0, 2, "Tug of War", "towline", Gold, n => $"Right click's yank pulls {PlungerFx.TugPer * n:0.0} m further and does {PlungerFx.TugDamagePer * n:0} more a plunger" ),
			S( SkillId.PlumbersNightmare, plu, 0, 3, "Plumber's Nightmare", "nightmare", Lime, n => $"Popped plungers fly on and stick to the {PlungerFx.NightmareThrows( n )} nearest enemies", max: 3 ),
			// ---- Plunger-Bot: Flush Bomb ----
			S( SkillId.StrongFlush, plu, 1, 0, "Strong Flush", "flush", Blue, n => $"+{Pct( PlungerFx.StrongFlushPer * n )} whirlpool pull" ),
			S( SkillId.LongFlush, plu, 1, 0, "Long Flush", "lasting", Sky, n => $"The flush lasts {PlungerFx.LongFlushPer * n:0.0} s longer" ),
			S( SkillId.WiderBowl, plu, 1, 1, "Wider Bowl", "reach", Teal, n => $"+{Pct( PlungerFx.WiderBowlPer * n )} whirlpool size" ),
			S( SkillId.DoubleFlush, plu, 1, 1, "Double Flush", "pockets", Steel, n => $"Carry {n} more toilet{(n == 1 ? "" : "s")}" ),
			S( SkillId.Geyser, plu, 1, 2, "Geyser", "geyser", Sky, n => $"+{Pct( PlungerFx.GeyserPer * n )} geyser damage" ),
			S( SkillId.Swirly, plu, 1, 2, "Swirly", "riptide", Violet, n => $"+{PlungerFx.SwirlyPer * n:0} whirlpool damage a second" ),
			S( SkillId.Clogged, plu, 1, 3, "Clogged", "clogged", new Color( 0.6f, 0.5f, 0.2f ), n => $"It clogs and bursts instead: {PlungerFx.CloggedDamage( n ):0} to everything in the bowl, and a toxic puddle ({PlungerFx.CloggedDps( n ):0} a second)", max: 3 ),
			// ---- Plunger-Bot: core ----
			S( SkillId.RubberPlating, plu, 2, 0, "Rubber Plating", "plating", Steel, n => $"+{SkillFx.PlatingPer * n:0} max health" ),
			S( SkillId.SpringyLegs, plu, 2, 0, "Springy Legs", "move", Green, n => $"+{PlayerPerks.MoveSpeedPerStack * n:0.0} m/s and +{Pct( PlayerPerks.StaminaPerStack * n )} stamina", perk: PerkKind.Move ),
			S( SkillId.LongBungee, plu, 2, 1, "Long Bungee", "longlunge", Gold, n => $"+{Pct( PlungerFx.LongBungeePer * n )} sling distance" ),
			S( SkillId.Elastic, plu, 2, 1, "Elastic", "jets", Sky, n => $"-{Pct( PlungerFx.ElasticPer * n )} sling cost" ),
			S( SkillId.BellyFlop, plu, 2, 2, "Belly Flop", "kickback", Orange, n => $"+{PlungerFx.BellyFlopPer * n:0} landing damage" ),
			S( SkillId.BounceBack, plu, 2, 2, "Bounce Back", "heal", Green, n => $"Heal {PlungerFx.BounceBackPer * n:0} for each enemy a landing hits" ),
			S( SkillId.Cannonball, plu, 2, 3, "Cannonball", "cannonball", Blue, n => $"Landing is a cannonball: {PlungerFx.CannonballDamage( n ):0} more to enemies within {PlungerFx.CannonballRadius( n ):0.0} m, thrown up", max: 3 ),
			// ---- Pinball-Bot: Pinball Launcher ----
			S( SkillId.HeavySteel, pin, 0, 0, "Heavy Steel", "heavymetal", Steel, n => $"+{Pct( PinballFx.HeavySteelPer * n )} ball damage" ),
			S( SkillId.SuperSpring, pin, 0, 0, "Super Spring", "spring", Gold, n => $"+{Pct( PinballFx.SpringPer * n )} launch speed" ),
			S( SkillId.ExtraBall, pin, 0, 1, "Extra Ball", "deepmag", Steel, n => $"+{n} ball{(n == 1 ? "" : "s")} a load" ),
			S( SkillId.Bumper, pin, 0, 1, "Bumper", "bumper", Magenta, n => $"Balls speed up {Pct( PinballFx.BumperGain - 1f + PinballFx.BumperPer * n )} off each enemy (from {Pct( PinballFx.BumperGain - 1f )})" ),
			S( SkillId.Jackpot, pin, 0, 2, "Jackpot", "jackpot", Gold, n => $"+{Pct( PinballFx.JackpotPer * n )} damage for each enemy a ball already hit" ),
			S( SkillId.MagneticSave, pin, 0, 2, "Magnetic Save", "junkmagnet", Red, n => "Balls bend toward enemies ahead of them" + (n > 1 ? $" ({n}x harder)" : "") ),
			S( SkillId.Multiball, pin, 0, 3, "Multiball", "multiball", Magenta, n => $"A ball splits into {PinballFx.MultiballSplit( n ) + 1} off the first enemy it hits (the new ones at 60%)", max: 3 ),
			// ---- Pinball-Bot: Flipper Arms ----
			S( SkillId.PowerFlip, pin, 1, 0, "Power Flip", "flipper", Orange, n => $"+{Pct( PinballFx.PowerFlipPer * n )} flipper damage" ),
			S( SkillId.WideFlipper, pin, 1, 0, "Wide Flipper", "reach", Teal, n => $"+{Pct( PinballFx.WidePer * n )} flipper reach" ),
			S( SkillId.QuickFlip, pin, 1, 1, "Quick Flip", "reload", Blue, n => $"-{Pct( PinballFx.QuickFlipPer * n )} flipper cooldown" ),
			S( SkillId.Combo, pin, 1, 1, "Combo", "combo", Gold, n => $"Each flip that hits within {PinballFx.ComboTime:0.0} s of the last adds {Pct( PinballFx.ComboPer * n )} (up to {PinballFx.ComboMax})" ),
			S( SkillId.LaunchPad, pin, 1, 2, "Launch Pad", "kickback", Sky, n => $"+{Pct( PinballFx.LaunchPadPer * n )} flipper knock back" ),
			S( SkillId.BallReturn, pin, 1, 2, "Ball Return", "riposte", Magenta, n => $"A flipped pinball hits {Pct( PinballFx.BallReturnPer * n )} harder (stacking each flip)" ),
			S( SkillId.Ramp, pin, 1, 3, "Ramp", "ramp", Violet, n => $"Flipped enemies fly up and crash down for {PinballFx.RampDamage( n ):0}, half to enemies they land near", max: 3 ),
			// ---- Pinball-Bot: core ----
			S( SkillId.ChromePlating, pin, 2, 0, "Chrome Plating", "plating", Steel, n => $"+{SkillFx.PlatingPer * n:0} max health" ),
			S( SkillId.BumperGuard, pin, 2, 0, "Bumper Guard", "shield", Sky, n => $"A {PlayerPerks.ShieldFirst + PlayerPerks.ShieldPerStack * (n - 1):0} point energy shield soaks hits before health", perk: PerkKind.Shield ),
			S( SkillId.SpinUp, pin, 2, 1, "Spin Up", "slipstream", Green, n => $"+{Pct( PinballFx.SpinUpPer * n )} roll speed" ),
			S( SkillId.LongRoll, pin, 2, 1, "Long Roll", "lasting", Sky, n => $"Roll {PinballFx.LongRollPer * n:0.0} s longer" ),
			S( SkillId.Strike, pin, 2, 2, "Strike", "heavyhitter", Red, n => $"+{Pct( PinballFx.StrikePer * n )} roll damage" ),
			S( SkillId.TiltBonus, pin, 2, 2, "Tilt Bonus", "quickcharge", Gold, n => $"Each enemy bowled over gives back {PinballFx.TiltPer * n:0} power" ),
			S( SkillId.HighScore, pin, 2, 3, "High Score", "highscore", Gold, n => $"Bowling {PinballFx.HighScoreNeeds}+ enemies in one roll ends it in a {PinballFx.HighScoreDamage( n ):0} blast and three pinballs", max: 3 ),
			S( SkillId.PhaseShift, vac, 2, 3, "Phase Shift", "phase", Violet, n => $"A blink makes you untouchable for {SkillFx.PhaseTime( n ):0.0} s and leaves a decoy enemies chase for {SkillFx.DecoyTime( n ):0.0} s", max: 3 ),
		};
	}

	static readonly Dictionary<SkillId, SkillDef> byId = All.ToDictionary( d => d.Id );
	public static SkillDef Def( SkillId id ) => byId[id];
	public static IEnumerable<SkillDef> Tree( string robot, int tree ) => All.Where( d => d.Robot == robot && d.Tree == tree );
	public static IEnumerable<SkillDef> Of( string robot ) => All.Where( d => d.Robot == robot );

	/// <summary> The local player's points in a skill (0 when it is another robot's). Cheap: read in hot paths. </summary>
	public static int Rank( SkillId id ) => PlayerSkills.Local?.Rank( id ) ?? 0;
	public static int Rank( Node controller, SkillId id ) => PlayerSkills.Of( controller )?.Rank( id ) ?? 0;
}

/// <summary>
/// A player's skill points and tokens this run (a child of the PlayerController named PlayerSkills). Clearing a wave
/// earns a token (up to `Skills.MaxTokens` a run); a token buys a point in a skill that is open (its row unlocked by
/// `Skills.UnlockPoints` points in the row above) and not full. A new run clears it all.
/// </summary>
public partial class PlayerSkills : Node
{
	readonly int[] ranks = new int[Enum.GetValues<SkillId>().Length];
	public int Tokens { get; private set; }
	/// <summary> Tokens earned this run (they stop at `Skills.MaxTokens`). </summary>
	public int Earned { get; private set; }
	/// <summary> Bumped on every change, so weapons can reapply their stats. </summary>
	public static int Version { get; private set; }
	public event Action Changed;
	PlayerController controller;

	public static PlayerSkills Local { get; private set; }
	public static PlayerSkills Of( Node controller ) => controller?.GetNodeOrNull<PlayerSkills>( "PlayerSkills" );

	public override void _Ready()
	{
		Name = "PlayerSkills";
		controller = GetParent<PlayerController>();
		Local ??= this;
		RobotClass.Changed += OnRobotChanged;
	}

	public override void _ExitTree()
	{
		RobotClass.Changed -= OnRobotChanged;
		if ( Local == this ) Local = null;
	}

	void OnRobotChanged() { Version++; Changed?.Invoke(); }

	/// <summary> Points in a skill, counted only while playing the robot it belongs to. </summary>
	public int Rank( SkillId id )
	{
		var r = ranks[(int)id];
		return r == 0 || Skills.Def( id ).Robot != RobotClass.Current.Id ? 0 : r;
	}
	/// <summary> Points held, whichever robot is being played (the tree page shows them all). </summary>
	public int Held( SkillId id ) => ranks[(int)id];

	public int RowPoints( string robot, int tree, int row )
	{
		var n = 0;
		foreach ( var d in Skills.All ) if ( d.Robot == robot && d.Tree == tree && d.Row == row ) n += ranks[(int)d.Id];
		return n;
	}
	public int TreePoints( string robot, int tree )
	{
		var n = 0;
		foreach ( var d in Skills.All ) if ( d.Robot == robot && d.Tree == tree ) n += ranks[(int)d.Id];
		return n;
	}
	public bool Unlocked( SkillDef d ) => d.Row == 0 || RowPoints( d.Robot, d.Tree, d.Row - 1 ) >= Skills.UnlockPoints;
	public bool CanBuy( SkillDef d ) => Tokens > 0 && Unlocked( d ) && ranks[(int)d.Id] < d.Max && d.Robot == RobotClass.Current.Id;

	/// <summary> Spends a token on a point in this skill. </summary>
	public bool Buy( SkillId id )
	{
		var d = Skills.Def( id );
		if ( !CanBuy( d ) ) return false;
		Tokens--;
		ranks[(int)id]++;
		Bought( d );
		Version++;
		Changed?.Invoke();
		return true;
	}

	/// <summary> Tests and the dev menu: sets a skill's points outright (no token, no row rule). </summary>
	public void Set( SkillId id, int rank )
	{
		var d = Skills.Def( id );
		rank = Math.Clamp( rank, 0, d.Max );
		var was = ranks[(int)id];
		ranks[(int)id] = rank;
		if ( Skills.AddsHealth( id ) && Survivor?.Health is { } h ) { h.MaxHealth += (rank - was) * SkillFx.PlatingPer; h.Current = MathF.Min( h.MaxHealth, h.Current + MathF.Max( 0f, rank - was ) * SkillFx.PlatingPer ); }
		if ( d.Perk == PerkKind.Shield ) PlayerPerks.Of( controller )?.RefillShield();
		Version++;
		Changed?.Invoke();
	}

	PlayerSurvivor Survivor => controller?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );

	/// <summary> What a point does the moment it is bought (more health, a full shield). </summary>
	void Bought( SkillDef d )
	{
		if ( Skills.AddsHealth( d.Id ) && Survivor?.Health is { } h ) { h.MaxHealth += SkillFx.PlatingPer; h.Heal( SkillFx.PlatingPer ); }
		if ( d.Perk == PerkKind.Shield ) PlayerPerks.Of( controller )?.RefillShield();
	}

	/// <summary> A wave was cleared: a token, while the run has some left to give. </summary>
	public void Award( int count = 1 )
	{
		var give = Math.Min( count, Skills.MaxTokens - Earned );
		if ( give <= 0 ) return;
		Earned += give;
		Tokens += give;
		Version++;
		Changed?.Invoke();
	}

	/// <summary> Dev menu: tokens on top of the run's (they still count toward the 50). </summary>
	public void Grant( int count ) => Award( count );

	/// <summary> A new run: no points, no tokens. </summary>
	public void Clear()
	{
		Array.Clear( ranks );
		Tokens = Earned = 0;
		Version++;
		Changed?.Invoke();
	}
}
