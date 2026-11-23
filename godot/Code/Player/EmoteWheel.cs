using Godot;

/// <summary>
/// Picking an emote: T (or hold the pad's D-pad down) opens a small row of the emotes; 1 to 3, a click, or the pad's
/// D-pad left, up and right plays one; T, Esc or B closes it. The game doesn't stop behind it.
/// </summary>
public partial class EmoteWheel : Control
{
	public static bool IsOpen { get; private set; }
	PlayerController me;
	double padHeldSince = -1;
	readonly List<Button> buttons = new();

	public static EmoteWheel Create( PlayerController player )
	{
		var w = new EmoteWheel { Name = "EmoteWheel", me = player, Visible = false, Theme = UiStyle.Menu };
		w.SetAnchorsPreset( LayoutPreset.CenterBottom );
		w.OffsetTop = -260; w.OffsetBottom = -150; w.OffsetLeft = -330; w.OffsetRight = 330;
		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		row.SetAnchorsPreset( LayoutPreset.FullRect );
		row.AddThemeConstantOverride( "separation", 14 );
		w.AddChild( row );
		for ( var i = 0; i < Emotes.All.Length; i++ )
		{
			var (id, name, _) = Emotes.All[i];
			var b = new Button { Text = $"{i + 1}  {name}", CustomMinimumSize = new Vector2( 200, 64 ), FocusMode = FocusModeEnum.None, Name = id.ToString() };
			b.AddThemeFontSizeOverride( "font_size", 22 );
			b.Pressed += () => w.Play( id );
			row.AddChild( b );
			w.buttons.Add( b );
		}
		return w;
	}

	bool Blocked => PauseMenu.IsOpen || DevMenu.IsOpen || ClassPicker.IsOpen || PlayerSurvivor.LocalDead;

	public override void _Process( double delta )
	{
		if ( me is null || !IsInstanceValid( me ) ) return;
		// The pad: hold the D-pad down a moment (a tap still commands the ally).
		if ( Input.IsActionPressed( "command" ) && Controls.IsController )
		{
			if ( padHeldSince < 0 ) padHeldSince = Clock.Real;
			else if ( !IsOpen && Clock.Real - padHeldSince > 0.35 && !Blocked ) SetOpen( true );
		}
		else padHeldSince = -1;
		if ( IsOpen && Blocked ) SetOpen( false );
	}

	public override void _Input( InputEvent e )
	{
		if ( me is null || !IsInstanceValid( me ) ) return;
		if ( e.IsActionPressed( "emote" ) && !e.IsEcho() ) { if ( !Blocked || IsOpen ) SetOpen( !IsOpen ); GetViewport().SetInputAsHandled(); return; }
		if ( !IsOpen ) return;
		if ( e.IsActionPressed( "pause" ) || e.IsActionPressed( "ui_cancel" ) ) { SetOpen( false ); GetViewport().SetInputAsHandled(); return; }
		if ( e is InputEventKey { Pressed: true, Echo: false } key && key.PhysicalKeycode >= Key.Key1 && key.PhysicalKeycode < Key.Key1 + Emotes.All.Length )
		{
			Play( Emotes.All[key.PhysicalKeycode - Key.Key1].Id );
			GetViewport().SetInputAsHandled();
			return;
		}
		if ( e is InputEventJoypadButton { Pressed: true } pad )
		{
			var pick = pad.ButtonIndex switch { JoyButton.DpadLeft => 0, JoyButton.DpadUp => 1, JoyButton.DpadRight => 2, _ => -1 };
			if ( pick >= 0 && pick < Emotes.All.Length ) { Play( Emotes.All[pick].Id ); GetViewport().SetInputAsHandled(); }
		}
	}

	void SetOpen( bool open )
	{
		IsOpen = open;
		Visible = open;
		Input.MouseMode = open ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
	}

	void Play( Emote id )
	{
		SetOpen( false );
		me.PlayEmote( id );
		Sfx.Play2D( "ui_click", 0.5f );
	}

	public override void _ExitTree() { if ( IsOpen ) IsOpen = false; }
}
