using Godot;

/// <summary> Every skill in the three robots' skill trees. </summary>
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
	};
	public static readonly string[][] TreeKit =
	{
		new[] { "Rifle", "Shotgun", "Surge, axe, survival" },
		new[] { "Trident Shooter", "Spear and Shield", "Surge, survival" },
		new[] { "Gore Vac", "Bubble Cannon", "Blink, survival" },
	};

	static int RobotIndex( string robot ) => robot == RobotClass.TridentBot.Id ? 1 : robot == RobotClass.VacBot.Id ? 2 : 0;
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
		if ( id == SkillId.ThickPlating && Survivor?.Health is { } h ) { h.MaxHealth += (rank - was) * SkillFx.PlatingPer; h.Current = MathF.Min( h.MaxHealth, h.Current + MathF.Max( 0f, rank - was ) * SkillFx.PlatingPer ); }
		if ( d.Perk == PerkKind.Shield ) PlayerPerks.Of( controller )?.RefillShield();
		Version++;
		Changed?.Invoke();
	}

	PlayerSurvivor Survivor => controller?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );

	/// <summary> What a point does the moment it is bought (more health, a full shield). </summary>
	void Bought( SkillDef d )
	{
		if ( d.Id == SkillId.ThickPlating && Survivor?.Health is { } h ) { h.MaxHealth += SkillFx.PlatingPer; h.Heal( SkillFx.PlatingPer ); }
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
