using Godot;

/// <summary>
/// One look for every screen: the palette, the fonts, how panels, bars, big numbers and the crosshair are drawn, the
/// health widget, the visor overlay and the HUD's size. UiStyle draws with the current one; the dev menu (Look tab,
/// HUD theme) switches between them and the choice is saved with the settings. Arcade Dive is the default; the
/// others are the mockup directions (A to K on the HUD looks page).
/// </summary>
public sealed class HudTheme
{
	public enum BigLook { Cartoon, Split, Glow, Flat, Comic }
	public enum FrameLook { Suit, Brackets, Brass, Glass, Comic, Sticker, Sonar, None, Lcd, Halftone }
	public enum BarLook { Pills, Ticks, Bubbles, Blocks, Segments, Orbs, Tube }
	public enum CrossLook { Ring, Brackets, Brass, Bubble, Ink, Tm, Ping, Glow, Diamond }
	public enum HealthLook { Panel, Porthole, Sonar, Tank, Tentacle }
	public enum FontRole { Display, Bold, Mono }

	public string Name { get; init; }
	public string Letter { get; init; } = "";

	public Color Ink { get; init; } = new( "06202c" );
	public Color Panel { get; init; } = new( 0.03f, 0.16f, 0.21f, 0.86f );
	public Color PanelDeep { get; init; } = new( 0.016f, 0.086f, 0.125f, 0.92f );
	/// <summary> The theme's glow: frames, captions, keys. </summary>
	public Color Accent { get; init; } = new( "5fffe0" );
	public Color AccentSoft { get; init; } = new( "7ff5e0" );
	public Color Readout { get; init; } = new( "9fe9de" );
	/// <summary> Numbers that matter: points, damage. </summary>
	public Color Gold { get; init; } = new( "ffe46b" );
	public Color Red { get; init; } = new( "ff3b3b" );
	public Color RedTop { get; init; } = new( "ff9a8a" );
	/// <summary> Big numbers floating on the view (kills), and the wave number. </summary>
	public Color Number { get; init; } = Colors.White;
	public Color Wave { get; init; } = new( "ffe46b" );
	/// <summary> Text inside panels: the number, the caption, the small readout. </summary>
	public Color? PanelNumberColour { get; init; }
	public Color? PanelCaptionColour { get; init; }
	public Color? PanelReadoutColour { get; init; }
	public Color PanelNumber => PanelNumberColour ?? Number;
	public Color PanelCaption => PanelCaptionColour ?? AccentSoft;
	public Color PanelReadout => PanelReadoutColour ?? Readout;
	/// <summary> Panels are pale (paper, LCD): text on them gets no dark outline. </summary>
	public bool LightPanel { get; init; }
	/// <summary> The health bar's fill (low health always turns it red). </summary>
	public Color HealthFill { get; init; } = new( "ff3b3b" );
	public Color HealthTop { get; init; } = new( "ff9a8a" );

	public FontRole NumberFont { get; init; } = FontRole.Display;
	public BigLook Big { get; init; } = BigLook.Cartoon;
	public FrameLook Frame { get; init; } = FrameLook.Suit;
	public BarLook Bars { get; init; } = BarLook.Pills;
	public CrossLook Crosshair { get; init; } = CrossLook.Ring;
	public HealthLook Health { get; init; } = HealthLook.Panel;

	/// <summary> HUD size against Arcade Dive's (1). </summary>
	public float Scale { get; init; } = 1f;
	/// <summary> Panels tilted a little (degrees): the health panel, and the ammo panel the other way. </summary>
	public float Tilt { get; init; }
	/// <summary> The health panel floats gently up and down. </summary>
	public bool Bob { get; init; }
	/// <summary> Small sub-lines (banner, game over) in the mono readout font. </summary>
	public bool MonoSub { get; init; }

	public string HealthWord { get; init; } = "HEALTH";
	public string BossWord { get; init; } = "TARGET  ";
	public string ReserveSep { get; init; } = "/";
	public string WeaponSuffix { get; init; } = "";
	/// <summary> A blinking warning under the health caption when low (null for none). </summary>
	public string Breach { get; init; }
	/// <summary> Joke fine print under the health number (one picked per run). </summary>
	public string[] FinePrint { get; init; }
	/// <summary> A little ™ sticker on the health widget. </summary>
	public bool TmBadge { get; init; }

	/// <summary> The visor overlay: scanline strength, dark rim, and whether big hits glitch the HUD. </summary>
	public float Scan { get; init; }
	public float Rim { get; init; }
	public bool Glitch { get; init; }

	public string Label => string.IsNullOrEmpty( Letter ) ? Name : $"{Letter}. {Name}";

	// ---- The looks ----

	public static readonly HudTheme ArcadeDive = new() { Name = "Arcade Dive" };

	public static readonly HudTheme RobotVisor = new()
	{
		Name = "Robot Visor", Letter = "B",
		Ink = new( "020c14" ), Panel = new( 0.01f, 0.06f, 0.09f, 0.42f ), PanelDeep = new( 0.01f, 0.05f, 0.08f, 0.9f ),
		Accent = new( "7ff6ff" ), AccentSoft = new( "a6f8ff" ), Readout = new( "8fdcea" ),
		Number = new( "7ff6ff" ), Wave = new( "7ff6ff" ), HealthFill = new( "7ff6ff" ), HealthTop = Colors.White,
		NumberFont = FontRole.Bold, Big = BigLook.Split, Frame = FrameLook.Brackets, Bars = BarLook.Ticks, Crosshair = CrossLook.Brackets,
		Scale = 0.72f, MonoSub = true, HealthWord = "HULL", BossWord = "TARGET LOCK: ", ReserveSep = "//", Breach = "! BREACH",
		Scan = 0.025f, Rim = 0.38f, Glitch = true,
	};

	public static readonly HudTheme Brass = new()
	{
		Name = "Brass Diving Gear", Letter = "A",
		Ink = new( "2b1906" ), Panel = new( 0.2f, 0.12f, 0.04f, 0.9f ), PanelDeep = new( 0.14f, 0.08f, 0.02f, 0.95f ),
		Accent = new( "e8c070" ), AccentSoft = new( "f6dc9a" ), Readout = new( "f1d9a6" ), Gold = new( "ffe2a0" ),
		Number = new( "ffe2a0" ), Wave = new( "ffe2a0" ), HealthFill = new( "ff4a55" ), HealthTop = new( "ffb4b4" ),
		Frame = FrameLook.Brass, Bars = BarLook.Tube, Crosshair = CrossLook.Brass, Health = HealthLook.Porthole,
		Scale = 0.9f, HealthWord = "HULL",
	};

	public static readonly HudTheme BubblePop = new()
	{
		Name = "Bubble Pop", Letter = "C",
		Ink = new( "0a3a5a" ), Panel = new( 0.04f, 0.24f, 0.35f, 0.35f ), PanelDeep = new( 0.03f, 0.16f, 0.25f, 0.92f ),
		Accent = new( "dff9ff" ), AccentSoft = new( "e6ffff" ), Readout = new( "bfefff" ), Gold = new( "ffe07a" ),
		Wave = Colors.White, HealthFill = new( "e3303d" ), HealthTop = new( "ff9a9a" ),
		Frame = FrameLook.Glass, Bars = BarLook.Bubbles, Crosshair = CrossLook.Bubble, Scale = 0.9f, HealthWord = "HP",
	};

	public static readonly HudTheme InkComic = new()
	{
		Name = "Ink Comic", Letter = "D",
		Ink = new( "111111" ), Panel = new( "fff6e0" ), PanelDeep = new( "fff6e0" ),
		Accent = new( "111111" ), AccentSoft = new( "fff6e0" ), Readout = new( "fff6e0" ), Gold = new( "ffd21f" ),
		Red = new( "e8202a" ), Number = Colors.White, Wave = new( "ffd21f" ), HealthFill = new( "e8202a" ), HealthTop = new( "ff8a8a" ),
		PanelNumberColour = new( "e8202a" ), PanelCaptionColour = new( "111111" ), PanelReadoutColour = new( "111111" ), LightPanel = true,
		Big = BigLook.Comic, Frame = FrameLook.Comic, Bars = BarLook.Blocks, Crosshair = CrossLook.Ink,
		Tilt = -2.5f, HealthWord = "HULL", BossWord = "",
	};

	public static readonly HudTheme Stickers = new()
	{
		Name = "Copyright Stickers", Letter = "E",
		Ink = new( "1b1b1b" ), Panel = new( "e53b3b" ), PanelDeep = new( "7a1650" ),
		Accent = Colors.White, AccentSoft = Colors.White, Readout = new( "ffe9e9" ), Gold = new( "ffd21f" ),
		Number = Colors.White, Wave = new( "ff4b4b" ), HealthFill = new( "ffd21f" ), HealthTop = Colors.White,
		Frame = FrameLook.Sticker, Bars = BarLook.Tube, Crosshair = CrossLook.Tm, Tilt = -4f, Scale = 0.95f,
		HealthWord = "HEALTH™", BossWord = "BOSS®  ", WeaponSuffix = "™",
		FinePrint = new[]
		{
			"Not actual health. Results may vary.", "Health sold separately.", "Do not eat ammo.", "Void where prohibited.",
			"Objects in visor are closer than they appear.", "Contains no real fish.", "Batteries not included.",
			"Any resemblance to other games is pure coincidence.", "Ask your doctor if health is right for you.",
			"No sea creatures were licensed in the making of this HUD.",
		},
	};

	public static readonly HudTheme Sonar = new()
	{
		Name = "Sonar", Letter = "F",
		Ink = new( "021a16" ), Panel = new( 0.01f, 0.1f, 0.09f, 0.62f ), PanelDeep = new( 0.01f, 0.08f, 0.07f, 0.94f ),
		Accent = new( "4af0b0" ), AccentSoft = new( "7fffc9" ), Readout = new( "7fffc9" ), Gold = new( "ffd56b" ),
		Number = new( "d8ffef" ), Wave = new( "7fffc9" ), HealthFill = new( "ff4a55" ), HealthTop = new( "ffb0b0" ),
		NumberFont = FontRole.Bold, Big = BigLook.Glow, Frame = FrameLook.Sonar, Bars = BarLook.Segments, Crosshair = CrossLook.Ping, Health = HealthLook.Sonar,
		Scale = 0.85f, MonoSub = true, HealthWord = "HULL", BossWord = "CONTACT  ",
	};

	public static readonly HudTheme FishTank = new()
	{
		Name = "Fish Tank", Letter = "G",
		Ink = new( "0a2a40" ), Panel = new( 0.04f, 0.16f, 0.24f, 0.35f ), PanelDeep = new( 0.03f, 0.12f, 0.2f, 0.92f ),
		Accent = new( "dcfaff" ), AccentSoft = new( "e6fbff" ), Readout = new( "bfefff" ), Gold = new( "ffd56b" ),
		Wave = Colors.White, HealthFill = new( "e0303c" ), HealthTop = new( "ff7a5c" ),
		Frame = FrameLook.Glass, Bars = BarLook.Tube, Crosshair = CrossLook.Bubble, Health = HealthLook.Tank, Scale = 0.9f, HealthWord = "HULL",
	};

	public static readonly HudTheme DeepGlow = new()
	{
		Name = "Deep Glow", Letter = "H",
		Ink = new( "03141c" ), Panel = new( 0f, 0f, 0f, 0f ), PanelDeep = new( 0.01f, 0.06f, 0.09f, 0.92f ),
		Accent = new( "3fffe0" ), AccentSoft = new( "c9fff6" ), Readout = new( "9fe9de" ), Gold = new( "ffd56b" ),
		Number = new( "c9fff6" ), Wave = new( "c9fff6" ), HealthFill = new( "ff7aa8" ), HealthTop = new( "ffd0e0" ),
		Big = BigLook.Glow, Frame = FrameLook.None, Bars = BarLook.Orbs, Crosshair = CrossLook.Glow, Health = HealthLook.Tentacle,
		Scale = 0.9f, MonoSub = true, HealthWord = "HULL", BossWord = "",
	};

	public static readonly HudTheme DiveComputer = new()
	{
		Name = "Dive Computer", Letter = "I",
		Ink = new( "15191c" ), Panel = new( "b4c69a" ), PanelDeep = new( "1e2327" ),
		Accent = new( "ffb02e" ), AccentSoft = new( "ffd27a" ), Readout = new( "ffd27a" ), Gold = new( "ffb02e" ),
		Number = new( "fff0d0" ), Wave = new( "ffb02e" ), HealthFill = new( "1b2a1a" ), HealthTop = new( "1b2a1a" ),
		PanelNumberColour = new( "1b2a1a" ), PanelCaptionColour = new( 0.106f, 0.165f, 0.102f, 0.75f ), PanelReadoutColour = new( "1b2a1a" ), LightPanel = true,
		NumberFont = FontRole.Mono, Big = BigLook.Flat, Frame = FrameLook.Lcd, Bars = BarLook.Segments, Crosshair = CrossLook.Diamond,
		Scale = 0.85f, MonoSub = true, HealthWord = "HULL", BossWord = "",
	};

	public static readonly HudTheme ArcadeJuiced = new()
	{
		Name = "Arcade Dive, Juiced", Letter = "J",
		Accent = new( "3fe0d0" ), AccentSoft = new( "3fe0d0" ), Panel = new( 0.03f, 0.07f, 0.11f, 0.86f ),
		Frame = FrameLook.Halftone, Tilt = -2f, Bob = true,
	};

	public static readonly HudTheme DivingBell = new()
	{
		Name = "Diving-Bell Robot", Letter = "K",
		Ink = new( "2b1906" ), Panel = new( 0.2f, 0.12f, 0.04f, 0.9f ), PanelDeep = new( 0.14f, 0.08f, 0.02f, 0.95f ),
		Accent = new( "e8c070" ), AccentSoft = new( "f6dc9a" ), Readout = new( "9fe6f0" ), Gold = new( "ffe2a0" ),
		Number = new( "ffe2a0" ), Wave = new( "ffe2a0" ), HealthFill = new( "ff4a55" ), HealthTop = new( "ffb4b4" ),
		Frame = FrameLook.Brass, Bars = BarLook.Tube, Crosshair = CrossLook.Brass, Health = HealthLook.Porthole,
		Scale = 0.9f, Bob = true, HealthWord = "HULL", Breach = "! HULL BREACH", TmBadge = true,
		Scan = 0.02f, Rim = 0.28f, Glitch = true,
	};

	/// <summary> Every look, in dev menu order. </summary>
	public static readonly HudTheme[] All = { ArcadeDive, Brass, RobotVisor, BubblePop, InkComic, Stickers, Sonar, FishTank, DeepGlow, DiveComputer, ArcadeJuiced, DivingBell };

	/// <summary> A look by its name or its mockup letter (any case, spaces and punctuation ignored); Arcade Dive if none. </summary>
	public static HudTheme Find( string name )
	{
		static string Key( string s ) => new string( (s ?? "").Where( char.IsLetterOrDigit ).ToArray() ).ToLowerInvariant();
		var key = Key( name );
		return All.FirstOrDefault( t => Key( t.Name ) == key || Key( t.Letter ) == key ) ?? ArcadeDive;
	}
}
