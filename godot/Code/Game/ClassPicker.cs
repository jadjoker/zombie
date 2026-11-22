using Godot;

/// <summary>
/// "Pick your robot": shown on the dock when a run starts (never under autotest), and from the pause menu while still
/// on the dock. One card per RobotClass with its weapons, mouse 4 move and melee; click a card's button, press 1 or 2,
/// or pick with the pad. The game waits behind it. The pick is saved and the player's weapons swapped (RobotClass.Pick).
/// </summary>
public partial class ClassPicker : CanvasLayer
{
	public static bool IsOpen { get; private set; }
	/// <summary> Cards narrow down to fit the row on screen. </summary>
	static int CardWidth => RobotClass.All.Length > 3 ? 286 : 380;

	Control window;
	readonly List<Button> buttons = new();

	public static ClassPicker Find( SceneTree tree ) => tree.Root.FindChild( "ClassPicker", true, false ) as ClassPicker;

	public override void _Ready()
	{
		Name = "ClassPicker";
		Layer = 61;
		ProcessMode = ProcessModeEnum.Always;
		window = new Control { Visible = false, Theme = UiStyle.Menu };
		window.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		AddChild( window );
		var backdrop = new ColorRect { Color = new Color( 0f, 0.02f, 0.05f, 0.72f ) };
		backdrop.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		window.AddChild( backdrop );

		var center = new CenterContainer();
		center.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		window.AddChild( center );
		var column = new VBoxContainer();
		column.AddThemeConstantOverride( "separation", 22 );
		center.AddChild( column );
		var title = UiStyle.Big( column, 56, UiStyle.Gold );
		title.Text = "PICK YOUR ROBOT";
		title.HorizontalAlignment = HorizontalAlignment.Center;

		var row = new HBoxContainer();
		row.AddThemeConstantOverride( "separation", RobotClass.All.Length > 3 ? 14 : 28 );
		column.AddChild( row );
		for ( var i = 0; i < RobotClass.All.Length; i++ ) row.AddChild( Card( RobotClass.All[i], i + 1 ) );

		var hint = new Label { Text = $"Press 1 to {RobotClass.All.Length}, or click a robot", HorizontalAlignment = HorizontalAlignment.Center };
		hint.AddThemeColorOverride( "font_color", UiStyle.Readout with { A = 0.7f } );
		hint.AddThemeFontOverride( "font", UiStyle.MonoWide );
		column.AddChild( hint );

		// A fresh run on the dock asks first (tests pick with --class instead).
		if ( !AutoTest.Requested ) CallDeferred( MethodName.OpenOnDock );
	}

	void OpenOnDock()
	{
		if ( DockHouse.Current is not null && DockHouse.WeaponsHeld ) SetOpen( true );
	}

	Control Card( RobotClass robot, int number )
	{
		var panel = new PanelContainer { CustomMinimumSize = new Vector2( CardWidth, 0 ), Name = robot.Name };
		var frame = UiStyle.Frame( 1.4f, UiStyle.PanelDeep, 20 );
		frame.BorderColor = robot.Tint;
		frame.SetBorderWidthAll( 3 );
		panel.AddThemeStyleboxOverride( "panel", frame );
		var column = new VBoxContainer();
		column.AddThemeConstantOverride( "separation", 10 );
		panel.AddChild( column );

		var name = UiStyle.Big( column, RobotClass.All.Length > 3 ? 32 : 44, robot.Tint );
		name.Text = $"{number}  {robot.Name.ToUpperInvariant()}";
		Line( column, robot.Tagline, 20, UiStyle.Readout );
		column.AddChild( new HSeparator() );
		Line( column, "WEAPONS", 16, UiStyle.Gold );
		Line( column, robot.Weapons, 20, Colors.White );
		Line( column, "MOUSE 4", 16, UiStyle.Gold );
		Line( column, robot.Movement, 18, Colors.White );
		Line( column, "MELEE", 16, UiStyle.Gold );
		Line( column, robot.Melee, 20, Colors.White );
		Line( column, "HEALTH", 16, UiStyle.Gold );
		Line( column, $"{robot.BaseHealth:0}", 20, Colors.White );

		var pick = new Button { Text = $"Play as {robot.Name}", CustomMinimumSize = new Vector2( 0, 50 ), Name = "Pick" };
		pick.AddThemeFontSizeOverride( "font_size", 22 );
		pick.Pressed += () => Choose( robot );
		column.AddChild( pick );
		buttons.Add( pick );
		return panel;
	}

	static void Line( Control parent, string text, int size, Color colour )
	{
		var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2( CardWidth - 20, 0 ) };
		label.AddThemeFontSizeOverride( "font_size", RobotClass.All.Length > 3 ? (int)(size * 0.82f) : size );
		label.AddThemeColorOverride( "font_color", colour );
		parent.AddChild( label );
	}

	public void Choose( RobotClass robot )
	{
		var player = GetTree().Root.FindChild( "Player", true, false ) as PlayerController;
		RobotClass.Pick( robot, player );
		Sfx.Play2D( "buy", 0.6f );
		SetOpen( false );
	}

	public override void _Input( InputEvent e )
	{
		if ( !IsOpen ) return;
		if ( e is InputEventKey { Pressed: true, Echo: false } key && key.PhysicalKeycode >= Key.Key1 && key.PhysicalKeycode < Key.Key1 + RobotClass.All.Length )
		{
			GetViewport().SetInputAsHandled();
			Choose( RobotClass.All[key.PhysicalKeycode - Key.Key1] );
			return;
		}
		// Esc does nothing here: a robot has to be picked (the pause menu waits).
		if ( e.IsActionPressed( "pause" ) ) GetViewport().SetInputAsHandled();
	}

	public void SetOpen( bool open )
	{
		if ( IsOpen == open ) return;
		IsOpen = open;
		window.Visible = open;
		GetTree().Paused = open;
		Input.MouseMode = open ? (Controls.IsController ? Input.MouseModeEnum.Hidden : Input.MouseModeEnum.Visible) : Input.MouseModeEnum.Captured;
		if ( open ) buttons[Math.Max( 0, Array.IndexOf( RobotClass.All, RobotClass.Current ) )].GrabFocus();
	}

	public override void _ExitTree()
	{
		if ( IsOpen ) { IsOpen = false; GetTree().Paused = false; }
	}
}
