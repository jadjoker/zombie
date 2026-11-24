using Godot;

public enum Scheme { KeyboardMouse, Controller }

/// <summary>
/// Controller support (issue #33). Adds a gamepad binding to every action the game reads (so the keyboard and mouse
/// ones keep working beside them), watches which kind of device you used last and switches the game between the two,
/// names the buttons for the on-screen prompts (Xbox or PlayStation style), and rumbles.
///
/// Layout: left stick move, right stick look, A jump, B crouch and slide, X reload, Y use, right trigger fire, left
/// trigger aim, left bumper sprint, right bumper axe, right stick click surge, D-pad up toggles third person, down
/// commands the ally spider, left and right switch weapon, Start pauses (B closes menus).
/// Autoloaded through <see cref="Clock"/>.
/// </summary>
public partial class Controls : Node
{
	/// <summary> The device last used. A pad that is plugged in makes it Controller until the mouse or keys are touched. </summary>
	public static Scheme Current { get; private set; } = Scheme.KeyboardMouse;
	public static bool IsController => Current == Scheme.Controller;
	public static event Action<Scheme> Changed;

	/// <summary> How many times the game asked for rumble this run (for tests: no pad is plugged in there). </summary>
	public static int RumbleCount { get; private set; }
	/// <summary> Testing aid: name buttons as a PlayStation pad does. </summary>
	public static bool SimulatePlayStation;

	static bool installed;
	static int padDevice;
	/// <summary> When a pad was last touched (its buttons or sticks). </summary>
	static double padUsedAt = -99;

	/// <summary> The pad that was last used. </summary>
	public static int PadDevice => padDevice;

	// ---- the layout ----

	static readonly (string Action, JoyButton Button)[] Buttons =
	{
		("jump", JoyButton.A), ("duck", JoyButton.B), ("reload", JoyButton.X), ("use", JoyButton.Y),
		("run", JoyButton.LeftShoulder), ("ping", JoyButton.LeftStick), ("scoreboard", JoyButton.Back), ("melee", JoyButton.RightShoulder), ("surge", JoyButton.RightStick),
		("view", JoyButton.DpadUp), ("command", JoyButton.DpadDown), ("weapon_prev", JoyButton.DpadLeft), ("weapon_next", JoyButton.DpadRight),
		("pause", JoyButton.Start),
	};

	/// <summary> What each action is called on screen: keyboard and mouse, Xbox, PlayStation. </summary>
	static readonly Dictionary<string, (string Keys, string Xbox, string PlayStation)> Names = new()
	{
		["use"] = ("E", "Y", "Triangle"),
		["reload"] = ("R", "X", "Square"),
		["jump"] = ("Space", "A", "Cross"),
		["duck"] = ("Ctrl", "B", "Circle"),
		["attack1"] = ("Click", "RT", "R2"),
		["attack2"] = ("Right click", "LT", "L2"),
		["run"] = ("Shift", "LB", "L1"),
		["melee"] = ("V", "RB", "R1"),
		["surge"] = ("Mouse 4", "R3", "R3"),   // each robot's move (bound to Q too)
		["ping"] = ("Middle mouse", "L3", "L3"),
		["scoreboard"] = ("Tab", "View", "Share"),
		["emote"] = ("T", "Hold D-pad down", "Hold D-pad down"),
		["blast"] = ("G", "RB", "R1"),
		["move"] = ("WASD", "Left stick", "Left stick"),
		["view"] = ("C", "D-pad up", "D-pad up"),
		["command"] = ("Z", "D-pad down", "D-pad down"),
		["weapon_prev"] = ("Wheel up", "D-pad left", "D-pad left"),
		["weapon_next"] = ("Wheel down", "D-pad right", "D-pad right"),
		["pause"] = ("Esc", "Start", "Options"),
	};

	/// <summary> The name of the button or key for an action, in the style of the device in use. </summary>
	public static string Label( string action )
	{
		if ( !Names.TryGetValue( action, out var n ) ) return action.ToUpperInvariant();
		return !IsController ? n.Keys : IsPlayStation ? n.PlayStation : n.Xbox;
	}

	/// <summary>
	/// Text written for the keyboard and mouse ("mouse 4", "right click", "left click", "middle mouse") with the pad's
	/// buttons in their place when a pad is in use. Keep descriptions in keyboard words and pass them through this.
	/// </summary>
	public static string Prompts( string text )
	{
		if ( !IsController || string.IsNullOrEmpty( text ) ) return text;
		foreach ( var (words, action) in new[] { ("mouse 4", "surge"), ("Mouse 4", "surge"), ("MOUSE 4", "surge"), ("right click", "attack2"), ("Right click", "attack2"), ("left click", "attack1"), ("Left click", "attack1"), ("middle mouse", "ping") } )
			text = text.Replace( words, words == words.ToUpperInvariant() ? Label( action ).ToUpperInvariant() : Label( action ) );
		return text;
	}

	public static bool IsPlayStation
	{
		get
		{
			if ( SimulatePlayStation ) return true;
			var name = Input.GetJoyName( padDevice ).ToLowerInvariant();
			return name.Contains( "ps" ) && !name.Contains( "xbox" ) || name.Contains( "dual" ) || name.Contains( "sony" ) || name.Contains( "playstation" );
		}
	}

	// ---- bindings ----

	/// <summary> Adds the pad to every action. Safe to call twice. </summary>
	public static void Install()
	{
		if ( installed ) return;
		installed = true;
		void Action( string name, float deadzone ) { if ( !InputMap.HasAction( name ) ) InputMap.AddAction( name, deadzone ); }
		foreach ( var name in new[] { "look_left", "look_right", "look_up", "look_down" } ) Action( name, 0.15f );
		Action( "weapon_prev", 0.5f );
		Action( "weapon_next", 0.5f );
		Action( "pause", 0.5f );
		InputMap.ActionAddEvent( "pause", new InputEventKey { PhysicalKeycode = Key.Escape } );
		// Co-op: hold Tab for the scoreboard, middle mouse to ping (the pad's are in Buttons).
		Action( "scoreboard", 0.5f );
		InputMap.ActionAddEvent( "scoreboard", new InputEventKey { PhysicalKeycode = Key.Tab } );
		Action( "emote", 0.5f );
		InputMap.ActionAddEvent( "emote", new InputEventKey { PhysicalKeycode = Key.T } );
		Action( "ping", 0.5f );
		InputMap.ActionAddEvent( "ping", new InputEventMouseButton { ButtonIndex = MouseButton.Middle } );

		void Axis( string action, JoyAxis axis, float value ) => InputMap.ActionAddEvent( action, new InputEventJoypadMotion { Axis = axis, AxisValue = value } );
		Axis( "left", JoyAxis.LeftX, -1f ); Axis( "right", JoyAxis.LeftX, 1f );
		Axis( "forward", JoyAxis.LeftY, -1f ); Axis( "backward", JoyAxis.LeftY, 1f );
		Axis( "look_left", JoyAxis.RightX, -1f ); Axis( "look_right", JoyAxis.RightX, 1f );
		Axis( "look_up", JoyAxis.RightY, -1f ); Axis( "look_down", JoyAxis.RightY, 1f );
		Axis( "attack1", JoyAxis.TriggerRight, 1f );
		Axis( "attack2", JoyAxis.TriggerLeft, 1f );
		// Triggers are analogue: pulled about a third of the way counts.
		InputMap.ActionSetDeadzone( "attack1", 0.35f );
		InputMap.ActionSetDeadzone( "attack2", 0.35f );
		foreach ( var (action, button) in Buttons )
			InputMap.ActionAddEvent( action, new InputEventJoypadButton { ButtonIndex = button } );

		// The menus: the pad's A, B and D-pad drive Godot's own UI actions. (Some projects lack the pad half of these.)
		void Ui( string action, JoyButton button )
		{
			if ( !InputMap.HasAction( action ) ) InputMap.AddAction( action );
			if ( !InputMap.ActionGetEvents( action ).Any( e => e is InputEventJoypadButton b && b.ButtonIndex == button ) )
				InputMap.ActionAddEvent( action, new InputEventJoypadButton { ButtonIndex = button } );
		}
		Ui( "ui_accept", JoyButton.A );
		Ui( "ui_cancel", JoyButton.B );
		Ui( "ui_up", JoyButton.DpadUp ); Ui( "ui_down", JoyButton.DpadDown ); Ui( "ui_left", JoyButton.DpadLeft ); Ui( "ui_right", JoyButton.DpadRight );
		void UiStick( string action, JoyAxis axis, float value )
		{
			if ( !InputMap.ActionGetEvents( action ).Any( e => e is InputEventJoypadMotion m && m.Axis == axis && MathF.Sign( m.AxisValue ) == MathF.Sign( value ) ) )
				InputMap.ActionAddEvent( action, new InputEventJoypadMotion { Axis = axis, AxisValue = value } );
		}
		UiStick( "ui_left", JoyAxis.LeftX, -1f ); UiStick( "ui_right", JoyAxis.LeftX, 1f );
		UiStick( "ui_up", JoyAxis.LeftY, -1f ); UiStick( "ui_down", JoyAxis.LeftY, 1f );
	}

	// ---- watching the device ----

	public override void _Ready()
	{
		Name = "Controls";
		ProcessMode = ProcessModeEnum.Always;
		Install();
		Input.JoyConnectionChanged += OnJoyChanged;
		var pads = Input.GetConnectedJoypads();
		if ( pads.Count > 0 ) padDevice = pads[0];
		// A pad plugged in at the start means controller mode (a scripted test does not depend on the hardware here).
		if ( pads.Count > 0 && !AutoTest.Requested ) { Switch( Scheme.Controller ); EnableGyro(); }
	}

	public override void _ExitTree() => Input.JoyConnectionChanged -= OnJoyChanged;

	void OnJoyChanged( long device, bool connected )
	{
		if ( connected ) { padDevice = (int)device; Switch( Scheme.Controller ); EnableGyro(); }
		else if ( Input.GetConnectedJoypads().Count == 0 ) Switch( Scheme.KeyboardMouse );
	}

	// ---- gyro ----

	/// <summary> Whether the pad in use has motion sensors (a Steam Controller, DualSense, Switch Pro...). </summary>
	public static bool HasGyro => Input.GetConnectedJoypads().Contains( padDevice ) && Input.HasJoyMotionSensors( padDevice );
	static Vector3 gyroBias;
	static float stillFor;
	/// <summary> Tests and the menu: the gyro's resting drift that is taken off. </summary>
	public static Vector3 GyroBias => gyroBias;

	static void EnableGyro()
	{
		if ( HasGyro && !Input.IsJoyMotionSensorsEnabled( padDevice ) ) Input.SetJoyMotionSensorsEnabled( padDevice, true );
	}

	/// <summary>
	/// The pad's turn this frame as (yaw, pitch) degrees a second, its resting drift taken off (learned on its own
	/// whenever the pad is held still, as Steam does). Zero without a gyro.
	/// </summary>
	public static Vector2 GyroRate( float dt )
	{
		if ( !HasGyro ) return Vector2.Zero;
		EnableGyro();
		var raw = Input.GetJoyGyroscope( padDevice );   // rad/s round the pad's X (pitch), Y (yaw), Z (roll)
		var g = raw - gyroBias;
		// Still (only a little sensor noise): learn the drift.
		if ( g.Length() < 0.05f ) { stillFor += dt; if ( stillFor > 0.6f ) gyroBias = gyroBias.Lerp( raw, MathF.Min( 1f, dt * 2f ) ); }
		else stillFor = 0f;
		// A small dead zone for hand tremor, then degrees a second. Yaw takes the roll too, so tilting works as well
		// as turning (players hold pads both ways).
		var yaw = g.Y + g.Z * 0.6f;
		var pitch = g.X;
		Vector2 Dead( Vector2 v ) => v.Length() < 0.012f ? Vector2.Zero : v;
		return Dead( new Vector2( yaw, pitch ) ) * (180f / MathF.PI);
	}

	/// <summary> Menu: put the pad down, then this learns its drift over a second. </summary>
	public static void CalibrateGyro()
	{
		if ( !HasGyro ) return;
		EnableGyro();
		Input.StartJoyMotionSensorsCalibration( padDevice );
		var tree = Engine.GetMainLoop() as SceneTree;
		tree?.CreateTimer( 1.2, true, false, true ).Connect( SceneTreeTimer.SignalName.Timeout, Callable.From( () => { Input.StopJoyMotionSensorsCalibration( padDevice ); gyroBias = Vector3.Zero; } ) );
	}

	public override void _Input( InputEvent e )
	{
		switch ( e )
		{
			case InputEventJoypadButton { Pressed: true } b: padDevice = b.Device; padUsedAt = Clock.Real; Switch( Scheme.Controller ); EnableGyro(); break;
			case InputEventJoypadMotion m when MathF.Abs( m.AxisValue ) > 0.5f: padDevice = m.Device; padUsedAt = Clock.Real; Switch( Scheme.Controller ); break;
			case InputEventKey { Pressed: true, Echo: false }: Switch( Scheme.KeyboardMouse ); break;
			// A Steam Controller (and Steam Input on any pad) sends mouse moves and clicks from its trackpads and gyro: with
			// a pad in hand those don't count as the mouse, so the prompts and aim assist stay on the pad. A real mouse is
			// taken up again by a click a little after the pad was last touched, or by moving it with no pad plugged in.
			case InputEventMouseButton { Pressed: true } when Clock.Real - padUsedAt > 3.0: Switch( Scheme.KeyboardMouse ); break;
			case InputEventMouseMotion mm when mm.Relative.LengthSquared() > 4f && Input.GetConnectedJoypads().Count == 0: Switch( Scheme.KeyboardMouse ); break;
		}
	}

	static void Switch( Scheme scheme )
	{
		if ( Current == scheme ) return;
		Current = scheme;
		Changed?.Invoke( scheme );
	}

	/// <summary> Dev and test hook: act as if this device was just used. </summary>
	public static void Force( Scheme scheme ) => Switch( scheme );

	// ---- rumble ----

	/// <summary> Rumbles the pad in use: 0.1 a tap, 0.5 a thump, 1 everything. Does nothing on the keyboard, or with vibration off. </summary>
	public static void Rumble( float amount, float? seconds = null )
	{
		if ( Sfx.RemoteSource is not null ) return;   // another player's robot did it
		if ( !GameSettings.Vibration || amount < 0.03f ) return;
		RumbleCount++;
		if ( !IsController || Input.GetConnectedJoypads().Count == 0 ) return;
		amount = MathX.Clamp( amount, 0f, 1f );
		Input.StartJoyVibration( padDevice, amount * 0.6f, amount, seconds ?? 0.12f + 0.25f * amount );
	}
}
