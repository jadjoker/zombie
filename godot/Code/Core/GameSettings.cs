using Godot;

/// <summary>
/// The settings the pause menu changes: mouse sensitivity, volume, field of view, fullscreen. Saved to
/// user://settings.cfg and loaded at start. Scripted tests neither load nor save, so they always run on the
/// defaults and never touch the saved file.
/// </summary>
public static class GameSettings
{
	const string Path = "user://settings.cfg";
	/// <summary> Degrees per mouse pixel at sensitivity 1 (the s&box feel). </summary>
	const float BaseLook = 0.12f;

	/// <summary> Mouse sensitivity multiplier (1 = default). </summary>
	public static float Sensitivity = 1f;
	/// <summary> Master volume, 0 to 1. </summary>
	public static float Volume = 1f;
	/// <summary> Horizontal field of view in degrees. </summary>
	public static float Fov = 90f;
	public static bool Fullscreen;
	/// <summary> Controller: right stick look speed (1 is the default), and whether the pad rumbles. </summary>
	public static float StickSensitivity = 1f;
	public static bool Vibration = true;
	/// <summary> How strong the controller's aim assist is, 0 (off) to 1. </summary>
	public static float AimAssist = 0.7f;
	public const float MinStick = 0.3f, MaxStick = 3f;
	/// <summary> How far out from the body the gun is held in first person, in inches from the usual spot (issue #31). </summary>
	public static float GunDistance;
	public const float MinGunDistance = -4f, MaxGunDistance = 10f;
	/// <summary> How high the gun sits in first person, in inches from the usual spot: down takes it out of view. </summary>
	public static float GunHeight;
	public const float MinGunHeight = -16f, MaxGunHeight = 4f;
	/// <summary> Which rifle design is in hand (RifleModels). </summary>
	public static string Rifle = RifleArgument() ?? "Blaster";

	static string RifleArgument()
	{
		var args = OS.GetCmdlineUserArgs();
		var i = Array.IndexOf( args, "--rifle" );
		return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
	}

	public const float MinSensitivity = 0.2f, MaxSensitivity = 3f, MinFov = 70f, MaxFov = 110f;

	/// <summary> Degrees per mouse pixel right now (before any zoom slows it). </summary>
	public static float LookSensitivity => BaseLook * Sensitivity;

	public static void Load()
	{
		if ( AutoTest.Requested ) return;
		var file = new ConfigFile();
		if ( file.Load( Path ) != Error.Ok ) return;
		Sensitivity = MathX.Clamp( (float)file.GetValue( "controls", "sensitivity", 1f ), MinSensitivity, MaxSensitivity );
		Volume = MathX.Clamp( (float)file.GetValue( "audio", "volume", 1f ), 0f, 1f );
		Fov = MathX.Clamp( (float)file.GetValue( "video", "fov", 90f ), MinFov, MaxFov );
		Fullscreen = (bool)file.GetValue( "video", "fullscreen", false );
		StickSensitivity = MathX.Clamp( (float)file.GetValue( "controller", "stick_sensitivity", 1f ), MinStick, MaxStick );
		Vibration = (bool)file.GetValue( "controller", "vibration", true );
		AimAssist = MathX.Clamp( (float)file.GetValue( "controller", "aim_assist", 0.7f ), 0f, 1f );
		GunDistance = MathX.Clamp( (float)file.GetValue( "video", "gun_distance", 0f ), MinGunDistance, MaxGunDistance );
		GunHeight = MathX.Clamp( (float)file.GetValue( "video", "gun_height", 0f ), MinGunHeight, MaxGunHeight );
		Rifle = RifleArgument() ?? (string)file.GetValue( "game", "rifle", Rifle );
	}

	public static void Save()
	{
		if ( AutoTest.Requested ) return;
		var file = new ConfigFile();
		file.SetValue( "controls", "sensitivity", Sensitivity );
		file.SetValue( "audio", "volume", Volume );
		file.SetValue( "video", "fov", Fov );
		file.SetValue( "video", "fullscreen", Fullscreen );
		file.SetValue( "controller", "stick_sensitivity", StickSensitivity );
		file.SetValue( "controller", "vibration", Vibration );
		file.SetValue( "controller", "aim_assist", AimAssist );
		file.SetValue( "video", "gun_distance", GunDistance );
		file.SetValue( "video", "gun_height", GunHeight );
		file.SetValue( "game", "rifle", Rifle );
		file.Save( Path );
	}

	/// <summary> Puts the settings into effect (sound, window, every camera and look speed). </summary>
	public static void Apply()
	{
		var master = AudioServer.GetBusIndex( "Master" );
		AudioServer.SetBusVolumeDb( master, Mathf.LinearToDb( MathF.Max( Volume, 0.0001f ) ) );
		AudioServer.SetBusMute( master, Volume <= 0.001f );
		if ( !AutoTest.Requested )
		{
			var now = DisplayServer.WindowGetMode();
			var isFull = now is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
			if ( isFull != Fullscreen ) DisplayServer.WindowSetMode( Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed );
		}
		foreach ( var player in PlayerSurvivor.All )
		{
			player.Controller.FieldOfView = Fov;
			player.Controller.LookSensitivity = LookSensitivity;
		}
	}
}
