using Godot;

/// <summary>
/// Port of the s&box ViewModel for the custom (Blender-made) first-person models: one rig with gun and arms, driven
/// by named animations. Plays draw, fire, reload (and the shotgun's shell reload), idles or tucks into the sprint
/// pose, and adds mouse sway, a walking bob and the aim pose (sights slid onto the middle of the view, the rifle's
/// scope carried up to the eye then dropped away under the scope view). Child of the camera.
/// </summary>
public partial class ViewModel : Node3D
{
	public float SwayAmount = 0.6f;
	public float SwaySmoothing = 10f;
	/// <summary> Where the whole view model slides to when aiming (camera space, inches) for guns without their own pose. </summary>
	public Vector3 AimOffset = new( -4f, 10f, -1.2f );

	public string ModelName { get; private set; }
	public AnimationPlayer Player { get; private set; }
	Node3D model;
	PlayerController controller;
	PlayerWeapon weapon;
	PlayerMovement movement;

	string playing;
	TimeUntil busyUntil;
	float bobTime;
	Angles lastEyeAngles;
	Vector2 inertia;
	string appliedSkin;
	bool shellReloading;

	/// <summary> Render layer 2: the first person gun and arms. </summary>
	public const uint ViewLayer = 1u << 1;

	/// <summary>
	/// A soft key from the upper right and a cool rim from behind, lighting only the first person gun and arms, so
	/// they read in dark scenes (underwater the metal otherwise reflects the murk and goes black).
	/// </summary>
	static void AddStudioLights( Camera3D camera )
	{
		if ( camera.GetNodeOrNull( "ViewModel Key" ) is not null ) return;
		var key = new OmniLight3D { Name = "ViewModel Key", LightColor = new Color( 1f, 0.95f, 0.88f ), LightEnergy = 0.9f, OmniRange = 2.2f, OmniAttenuation = 0.8f, LightCullMask = ViewLayer, ShadowEnabled = false };
		camera.AddChild( key );
		key.Position = new Vector3( 0.6f, 0.45f, 0.1f );
		var rim = new OmniLight3D { Name = "ViewModel Rim", LightColor = new Color( 0.6f, 0.85f, 1f ), LightEnergy = 1.4f, OmniRange = 2f, OmniAttenuation = 1.2f, LightCullMask = ViewLayer, ShadowEnabled = false, LightSpecular = 1.5f };
		camera.AddChild( rim );
		rim.Position = new Vector3( -0.2f, 0.35f, -1.1f );
	}

	public static ViewModel Create( Camera3D camera, string modelName, PlayerController controller, PlayerWeapon weapon )
	{
		var (root, player, _) = WeaponModels.Create( modelName );
		var vm = new ViewModel { Name = "ViewModel_" + modelName, ModelName = modelName, controller = controller, weapon = weapon, Player = player, model = root };
		vm.AddChild( root );
		camera.AddChild( vm );
		// First person meshes live on their own render layer, so the studio lights below light only them.
		foreach ( var mesh in root.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			mesh.Layers = ViewLayer;
			mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		}
		AddStudioLights( camera );
		vm.movement = controller.GetNodeOrNull<PlayerMovement>( "PlayerMovement" );
		vm.lastEyeAngles = controller.EyeAngles;
		vm.PlayOnce( "draw", 0.8f, 1f );
		return vm;
	}

	void Restart( string name, float blend, float rate )
	{
		if ( Player is null || !Player.HasAnimation( name ) ) { playing = null; return; }
		if ( !IsVisibleInTree() ) { playing = null; return; }
		if ( Player.CurrentAnimation == name ) Player.Stop();
		Player.Play( name, blend, rate );
		playing = name;
	}

	void PlayLoop( string name )
	{
		if ( playing == name ) return;
		Restart( name, 0.1f, 1f );
	}

	void PlayOnce( string name, float seconds, float rate )
	{
		Restart( name, 0.1f, rate );
		busyUntil = seconds;
	}

	/// <summary> Switching back to this weapon: raise it again (s&box OnEnabled). </summary>
	public void Shown()
	{
		playing = null;
		PlayOnce( "draw", 0.8f, 1f );
	}

	(Vector3 Offset, float Yaw) AimPose()
	{
		// Rifles: the sight (scope eyepiece, reflex window or carry handle) 10 units out, at eye height.
		if ( RifleModels.ByModel( ModelName ) is { } rifle ) return (new Vector3( -11.5f, 12.25f, 9.55f - rifle.SightHeight ), -8f);
		if ( ModelName == "shotgun" ) return (new Vector3( -6f, 12.4f, 3.3f ), -8f);      // rear of the rib 17 out, bead on it
		return (AimOffset, 0f);
	}

	public override void _Process( double delta )
	{
		if ( controller is null || !IsVisibleInTree() ) return;
		var dt = (float)delta;
		if ( appliedSkin != WeaponSkins.Current )
		{
			appliedSkin = WeaponSkins.Current;
			WeaponSkins.Apply( model, appliedSkin );
		}

		var eye = controller.EyeAngles;
		var d = new Vector2( Mathf.Wrap( eye.Yaw - lastEyeAngles.Yaw, -180f, 180f ), Mathf.Wrap( eye.Pitch - lastEyeAngles.Pitch, -180f, 180f ) );
		lastEyeAngles = eye;
		inertia = inertia.Lerp( d, MathX.Clamp( dt * SwaySmoothing, 0f, 1f ) );

		// Speeds in s&box units, so the s&box numbers below read the same.
		var speed = controller.Velocity.Flat().Length() / U.In;
		var aim = weapon?.AimAmount ?? 0f;
		var steady = MathX.Lerp( 1f, 0.25f, aim );
		var rushing = movement is not null && (movement.IsDashing || movement.IsSliding) && speed > 150f;

		if ( busyUntil <= 0f )
		{
			var wantSprint = rushing && !(weapon?.IsReloading ?? false) && !(weapon?.IsAiming ?? false);
			if ( shellReloading ) PlayLoop( "reload_hold" );
			else PlayLoop( wantSprint ? "sprint" : "idle" );
		}

		bobTime += dt * MathX.Clamp( speed / 55f, 0f, 6f );
		var moving = MathX.Clamp( speed / 300f, 0f, 1f ) * steady;
		var bob = new Vector3( 0f, MathF.Sin( bobTime * 0.5f ) * 0.45f * moving, -MathF.Abs( MathF.Sin( bobTime ) ) * 0.55f * moving );
		var lag = new Vector3( 0f, inertia.X * 0.05f * SwayAmount * steady, -inertia.Y * 0.05f * SwayAmount * steady );
		var (aimOffset, aimYaw) = AimPose();
		var scope = weapon?.ScopeAmount ?? 0f;
		var tuck = new Vector3( 0f, 0f, -40f ) * MathX.Clamp( (scope - 0.6f) / 0.4f, 0f, 1f );
		var settle = new Vector3( 1f, 0f, 0f ) * MathF.Sin( aim * MathF.PI ) * -1.2f;
		// The player's own choice of how far out the gun sits (pause menu); the aimed pose is left alone so the sights line up.
		var reach = new Vector3( GameSettings.GunDistance, 0f, 0f ) * (1f - aim);
		var offset = Vector3.Zero.Lerp( aimOffset, aim ) + settle + tuck + bob + lag + reach;

		Position = U.Sbox( offset );
		Basis = new Angles( 0f, aimYaw * aim, 0f ).ToBasis() * new Angles( inertia.Y * 0.35f * steady, inertia.X * 0.35f * steady, 0f ).ToBasis();
	}

	public void Fire()
	{
		shellReloading = false;
		PlayOnce( "fire", FireSeconds, 1f );
	}

	public void Reload( float duration ) => PlayOnce( "reload", duration, duration > 0.01f ? (100f / 30f) / duration : 1f );

	public void ShellReloadBegin()
	{
		shellReloading = true;
		PlayOnce( "reload_start", 0.4f, 1f );
	}

	public void InsertShell()
	{
		var seconds = MathF.Max( 0.15f, weapon?.ShellTime ?? 0.5f );
		PlayOnce( "reload_shell", seconds, (15f / 30f) / seconds );
	}

	public void ShellReloadEnd()
	{
		if ( !shellReloading ) return;
		shellReloading = false;
		PlayOnce( "reload_end", 0.6f, 1f );
	}

	float FireSeconds => ModelName == "shotgun" ? 0.7f : 0.27f;

	/// <summary> World transform of the muzzle, for the flash. Falls back to the view model itself. </summary>
	public Transform3D Muzzle() => WeaponModels.Muzzle( model )?.GlobalTransform ?? GlobalTransform;
}
