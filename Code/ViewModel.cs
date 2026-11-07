/// <summary>
/// The first-person gun and arms. The gun model's own animation graph does the firing, reloading,
/// drawing, bob and sway; this just feeds it parameters and keeps it on the camera.
/// Created by PlayerWeapon for the local player.
/// </summary>
public sealed class ViewModel : Component
{
	public SkinnedModelRenderer Gun { get; private set; }
	public SkinnedModelRenderer Arms { get; private set; }

	/// <summary> Extra lag on top of the graph's own sway, in degrees per unit of mouse movement. </summary>
	[Property] public float SwayAmount { get; set; } = 0.6f;
	[Property] public float SwaySmoothing { get; set; } = 10f;
	/// <summary> How long the graph's reload animation runs at speed 1, so it can be scaled to the real reload time. </summary>
	[Property] public float GraphReloadTime { get; set; } = 1.6f;

	PlayerController controller;
	PlayerWeapon weapon;
	Angles lastEyeAngles;
	Vector2 inertia;

	public static ViewModel Create( GameObject cameraObject, Model gunModel, Model armsModel, PlayerController controller, PlayerWeapon weapon )
	{
		var go = new GameObject( cameraObject, true, "ViewModel" );
		go.LocalPosition = Vector3.Zero;
		go.LocalRotation = Rotation.Identity;
		go.Tags.Add( "viewmodel" );

		var vm = go.AddComponent<ViewModel>();
		vm.controller = controller;
		vm.weapon = weapon;

		vm.Gun = go.AddComponent<SkinnedModelRenderer>();
		vm.Gun.Model = gunModel;
		vm.Gun.RenderType = ModelRenderer.ShadowRenderType.Off;

		if ( armsModel is not null )
		{
			var arms = new GameObject( go, true, "Arms" );
			vm.Arms = arms.AddComponent<SkinnedModelRenderer>();
			vm.Arms.Model = armsModel;
			vm.Arms.BoneMergeTarget = vm.Gun;
			vm.Arms.RenderType = ModelRenderer.ShadowRenderType.Off;
		}

		return vm;
	}

	protected override void OnStart()
	{
		if ( controller.IsValid() ) lastEyeAngles = controller.EyeAngles;
	}

	protected override void OnUpdate()
	{
		if ( !Gun.IsValid() || !controller.IsValid() ) return;

		// Mouse movement since last frame drives the graph's inertia sway.
		var eye = controller.EyeAngles;
		var delta = new Vector2( Angles.NormalizeAngle( eye.yaw - lastEyeAngles.yaw ), Angles.NormalizeAngle( eye.pitch - lastEyeAngles.pitch ) );
		lastEyeAngles = eye;
		inertia = Vector2.Lerp( inertia, delta, Time.Delta * SwaySmoothing );

		// Aiming steadies the gun: much less sway and bob.
		var aim = weapon?.AimAmount ?? 0f;
		var steady = MathX.Lerp( 1f, 0.25f, aim );
		Gun.Set( "aim_yaw_inertia", inertia.x * SwayAmount * 10f * steady );
		Gun.Set( "aim_pitch_inertia", inertia.y * SwayAmount * 10f * steady );
		Gun.Set( "ironsights", (weapon?.IsAiming ?? false) ? 1 : 0 );
		Gun.Set( "ironsights_fire_scale", 0.5f );
		Gun.Set( "speed_ironsights", 1.2f );

		var velocity = controller.Velocity.WithZ( 0 );
		var speed = velocity.Length;
		Gun.Set( "move_bob", MathX.Clamp( speed / 300f, 0f, 1f ) * steady );
		Gun.Set( "move_x", Vector3.Dot( velocity, controller.EyeAngles.ToRotation().Forward ) );
		Gun.Set( "move_y", Vector3.Dot( velocity, controller.EyeAngles.ToRotation().Right ) );
		Gun.Set( "move_z", controller.Velocity.z );
		Gun.Set( "b_grounded", !controller.IsAirborne );
		// Gun tucks in during the fast moves (dash, slide, momentum).
		var movement = controller.GetComponent<PlayerMovement>();
		var rushing = movement is not null && (movement.IsDashing || movement.IsSliding) && speed > 150f;
		Gun.Set( "b_sprint", rushing && !(weapon?.IsReloading ?? false) && !(weapon?.IsAiming ?? false) );

		if ( weapon.IsValid() )
			Gun.Set( "b_empty", weapon.Ammo <= 0 );
	}

	public void Fire() => Gun?.Set( "b_attack", true );
	public void DryFire() => Gun?.Set( "b_attack_dry", true );

	public void Reload( float duration )
	{
		if ( !Gun.IsValid() ) return;
		// Speed the graph up or down so its reload finishes with the weapon's real reload time.
		Gun.Set( "speed_reload", duration > 0.01f ? GraphReloadTime / duration : 1f );
		Gun.Set( "b_reload", true );
	}

	/// <summary> Shell-by-shell reload (shotgun): open, push shells, close. </summary>
	public void ShellReloadBegin() => Gun?.Set( "b_reloading", true );
	public void InsertShell( bool intoEmptyGun ) => Gun?.Set( intoEmptyGun ? "b_reloading_first_shell" : "b_reloading_shell", true );
	public void ShellReloadEnd() => Gun?.Set( "b_reloading", false );

	/// <summary> World transform of the gun's muzzle, for the flash. Falls back to the camera. </summary>
	public Transform Muzzle()
	{
		if ( Gun.IsValid() && Gun.GetAttachment( "muzzle" ) is Transform t ) return t;
		return WorldTransform;
	}
}
