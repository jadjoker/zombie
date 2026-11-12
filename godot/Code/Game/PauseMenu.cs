using Godot;

/// <summary>
/// The pause menu (Esc): stops the game and shows Resume, the settings (mouse sensitivity, volume, field of view,
/// fullscreen), Restart, the dev menu (where builds allow it) and Quit. Settings take effect as they are changed and are saved when the menu closes.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
	public static bool IsOpen { get; private set; }

	Label hint;
	Control window;
	Button resume;
	static readonly Color Accent = new( "5fffe0" );

	public override void _Ready()
	{
		Name = "PauseMenu";
		Layer = 60;
		ProcessMode = ProcessModeEnum.Always;   // it has to work while everything else is stopped

		Controls.Changed += OnSchemeChanged;
		window = new Control { Visible = false };
		window.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		AddChild( window );
		var backdrop = new ColorRect { Color = new Color( 0f, 0.02f, 0.05f, 0.6f ) };
		backdrop.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		window.AddChild( backdrop );

		var center = new CenterContainer();
		center.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		window.AddChild( center );
		var panel = new PanelContainer { CustomMinimumSize = new Vector2( 560, 0 ) };
		panel.AddThemeStyleboxOverride( "panel", new StyleBoxFlat
		{
			BgColor = new Color( "10141c" ) with { A = 0.97f }, BorderColor = Accent with { A = 0.5f },
			BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
			CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12, CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
			ContentMarginLeft = 32, ContentMarginRight = 32, ContentMarginTop = 26, ContentMarginBottom = 26,
		} );
		center.AddChild( panel );
		var column = new VBoxContainer();
		column.AddThemeConstantOverride( "separation", 14 );
		panel.AddChild( column );

		var title = new Label { Text = "PAUSED", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride( "font_size", 34 );
		title.AddThemeColorOverride( "font_color", Accent );
		column.AddChild( title );

		resume = MenuButton( "Resume", () => SetOpen( false ) );
		column.AddChild( resume );
		column.AddChild( new HSeparator() );

		column.AddChild( Slider( "Mouse sensitivity", GameSettings.MinSensitivity, GameSettings.MaxSensitivity, 0.05f, () => GameSettings.Sensitivity, v => GameSettings.Sensitivity = v, v => $"{v:0.00}x" ) );
		column.AddChild( Slider( "Stick sensitivity", GameSettings.MinStick, GameSettings.MaxStick, 0.05f, () => GameSettings.StickSensitivity, v => GameSettings.StickSensitivity = v, v => $"{v:0.00}x" ) );
		var vibration = new Button { ToggleMode = true, ButtonPressed = GameSettings.Vibration, Name = "Vibration", CustomMinimumSize = new Vector2( 0, 40 ) };
		vibration.AddThemeFontSizeOverride( "font_size", 20 );
		vibration.Text = GameSettings.Vibration ? "Controller vibration: On" : "Controller vibration: Off";
		vibration.Toggled += on => { GameSettings.Vibration = on; vibration.Text = on ? "Controller vibration: On" : "Controller vibration: Off"; if ( on ) Controls.Rumble( 0.5f ); };
		column.AddChild( vibration );
		column.AddChild( Slider( "Volume", 0f, 1f, 0.05f, () => GameSettings.Volume, v => GameSettings.Volume = v, v => $"{v * 100f:0}%" ) );
		column.AddChild( Slider( "Field of view", GameSettings.MinFov, GameSettings.MaxFov, 1f, () => GameSettings.Fov, v => GameSettings.Fov = v, v => $"{v:0}" ) );
		column.AddChild( Slider( "Gun up and down", GameSettings.MinGunHeight, GameSettings.MaxGunHeight, 0.5f, () => GameSettings.GunHeight, v => GameSettings.GunHeight = v, v => v == 0f ? "Default" : $"{v:+0.0;-0.0}" ) );
		column.AddChild( Slider( "Gun toward and away", GameSettings.MinGunDistance, GameSettings.MaxGunDistance, 0.5f, () => GameSettings.GunDistance, v => GameSettings.GunDistance = v, v => v == 0f ? "Default" : $"{v:+0.0;-0.0}" ) );
		var full = new Button { ToggleMode = true, ButtonPressed = GameSettings.Fullscreen, Name = "Fullscreen", CustomMinimumSize = new Vector2( 0, 40 ) };
		full.AddThemeFontSizeOverride( "font_size", 20 );
		full.Text = GameSettings.Fullscreen ? "Fullscreen: On" : "Fullscreen: Off";
		full.Toggled += on => { GameSettings.Fullscreen = on; full.Text = on ? "Fullscreen: On" : "Fullscreen: Off"; GameSettings.Apply(); };
		column.AddChild( full );

		column.AddChild( new HSeparator() );
		column.AddChild( MenuButton( "Restart", Restart ) );
		if ( DevMenu.Allowed ) column.AddChild( MenuButton( "Dev menu", OpenDevMenu ) );
		column.AddChild( MenuButton( "Quit to desktop", () => { GameSettings.Save(); GetTree().Quit(); } ) );

		hint = new Label { Text = "Esc to resume", HorizontalAlignment = HorizontalAlignment.Center };
		hint.AddThemeColorOverride( "font_color", new Color( 1, 1, 1, 0.45f ) );
		column.AddChild( hint );
	}

	/// <summary> With a pad the cursor is hidden (the buttons are picked with the stick and A); with a mouse it shows. </summary>
	static Input.MouseModeEnum MenuMouse() => Controls.IsController ? Input.MouseModeEnum.Hidden : Input.MouseModeEnum.Visible;

	void UpdateHint() { if ( hint is not null ) hint.Text = $"{Controls.Label( "pause" )} to resume"; }

	void OnSchemeChanged( Scheme scheme )
	{
		UpdateHint();
		if ( IsOpen ) Input.MouseMode = MenuMouse();
	}

	public override void _ExitTree()
	{
		Controls.Changed -= OnSchemeChanged;
		// Never leave the game stopped behind us (a level change while paused).
		if ( IsOpen ) { IsOpen = false; GetTree().Paused = false; }
	}

	static Button MenuButton( string text, Action pressed )
	{
		var b = new Button { Text = text, CustomMinimumSize = new Vector2( 0, 46 ), Name = text };
		b.AddThemeFontSizeOverride( "font_size", 22 );
		b.Pressed += pressed;
		return b;
	}

	/// <summary> A labelled slider row: name, slider, the value in words. Changes apply at once. </summary>
	static Control Slider( string name, float min, float max, float step, Func<float> get, Action<float> set, Func<float, string> show )
	{
		var row = new HBoxContainer { Name = name };
		row.AddThemeConstantOverride( "separation", 14 );
		var label = new Label { Text = name, CustomMinimumSize = new Vector2( 190, 0 ) };
		label.AddThemeFontSizeOverride( "font_size", 20 );
		row.AddChild( label );
		var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = get(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Name = "Slider" };
		row.AddChild( slider );
		var value = new Label { Text = show( get() ), CustomMinimumSize = new Vector2( 70, 0 ), HorizontalAlignment = HorizontalAlignment.Right };
		value.AddThemeFontSizeOverride( "font_size", 20 );
		row.AddChild( value );
		slider.ValueChanged += v =>
		{
			set( (float)v );
			value.Text = show( (float)v );
			GameSettings.Apply();
		};
		return row;
	}

	public override void _Input( InputEvent e )
	{
		// Esc or the pad's Start pauses and resumes; the pad's B (cancel) also closes it.
		if ( !(e.IsActionPressed( "pause" ) || (IsOpen && e.IsActionPressed( "ui_cancel" ))) ) return;
		GetViewport().SetInputAsHandled();
		// Esc backs out of the dev menu first.
		if ( DevMenu.IsOpen && !IsOpen ) { (GetTree().Root.FindChild( "DevMenu", true, false ) as DevMenu)?.SetOpen( false ); return; }
		SetOpen( !IsOpen );
	}

	public void SetOpen( bool open )
	{
		if ( IsOpen == open ) return;
		IsOpen = open;
		window.Visible = open;
		GetTree().Paused = open;
		Input.MouseMode = open ? MenuMouse() : Input.MouseModeEnum.Captured;
		UpdateHint();
		if ( open ) resume.GrabFocus();
		else GameSettings.Save();
	}

	/// <summary> Hands over to the dev menu: the game stays paused behind it, and Esc closes it back to the game. </summary>
	void OpenDevMenu()
	{
		SetOpen( false );
		(GetTree().Root.FindChild( "DevMenu", true, false ) as DevMenu)?.SetOpen( true );
	}

	void Restart()
	{
		GameSettings.Save();
		SetOpen( false );
		GetTree().ReloadCurrentScene();
	}
}
