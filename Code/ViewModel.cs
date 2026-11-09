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

	// ---- Custom (Blender-made) viewmodels: one skinned model with gun and arms, driven by named animations ----
	bool custom;
	string playing;
	TimeUntil busyUntil;
	float bobTime;
	/// <summary> Where the whole viewmodel slides to when aiming (camera space): puts the sights on the middle of the screen. </summary>
	[Property] public Vector3 AimOffset { get; set; } = new( -4f, 10f, -1.2f );

	public static ViewModel CreateCustom( GameObject cameraObject, Model model, PlayerController controller, PlayerWeapon weapon )
	{
		var go = new GameObject( cameraObject, true, "ViewModel" );
		go.LocalPosition = Vector3.Zero;
		go.LocalRotation = Rotation.Identity;
		go.Tags.Add( "viewmodel" );

		var vm = go.AddComponent<ViewModel>();
		vm.controller = controller;
		vm.weapon = weapon;
		vm.custom = true;
		vm.Gun = go.AddComponent<SkinnedModelRenderer>();
		vm.Gun.Model = model;
		vm.Gun.RenderType = ModelRenderer.ShadowRenderType.Off;
		vm.PlayOnce( "draw", 0.8f, 1f );
		return vm;
	}

	void Restart( string name )
	{
		if ( !Gun.IsValid() ) return;
		// While the view model is hidden (third person, or another weapon out) its model isn't live, and touching the
		// sequence throws. Forget what was playing so the right animation starts when it's shown again.
		if ( !Gun.Active || Gun.SceneModel is null ) { playing = null; return; }
		Gun.Sequence.Name = name;
		Gun.Sequence.Time = 0f;
		playing = name;
	}

	void PlayLoop( string name )
	{
		if ( !Gun.IsValid() || playing == name ) return;
		Restart( name );
		Gun.Sequence.Looping = true;
		Gun.PlaybackRate = 1f;
	}

	void PlayOnce( string name, float seconds, float rate )
	{
		if ( !Gun.IsValid() ) return;
		Restart( name );
		Gun.Sequence.Looping = false;
		Gun.PlaybackRate = rate;
		busyUntil = seconds;
	}

	protected override void OnEnabled()
	{
		// Switching back to this weapon: raise it again.
		if ( custom && Gun.IsValid() ) PlayOnce( "draw", 0.8f, 1f );
	}

	string appliedSkin;

	/// <summary>
	/// Where the view model goes when aiming, worked out from each gun's sight line: it's held turned 8 degrees in at the
	/// hip, so it turns straight and slides that line onto the middle of the view, the rear sight a set distance ahead.
	/// </summary>
	(Vector3 Offset, float Yaw) AimPose()
	{
		var name = Gun.IsValid() ? Gun.Model?.Name ?? "" : "";
		if ( name.Contains( "rifle" ) ) return (new Vector3( -11.5f, 13.8f, -0.4f ), -8f);      // the scope's eyepiece 10 units out
		if ( name.Contains( "shotgun" ) ) return (new Vector3( -6f, 12.4f, 3.3f ), -8f);        // rear of the rib 17 out, bead on it
		return (AimOffset, 0f);
	}

	void UpdateCustom()
	{
		var dt = Time.Delta;
		if ( appliedSkin != WeaponSkins.Current )
		{
			appliedSkin = WeaponSkins.Current;
			WeaponSkins.Apply( Gun, appliedSkin );
		}
		var eye = controller.EyeAngles;
		var delta = new Vector2( Angles.NormalizeAngle( eye.yaw - lastEyeAngles.yaw ), Angles.NormalizeAngle( eye.pitch - lastEyeAngles.pitch ) );
		lastEyeAngles = eye;
		inertia = Vector2.Lerp( inertia, delta, dt * SwaySmoothing );

		var velocity = controller.Velocity.WithZ( 0 );
		var speed = velocity.Length;
		var aim = weapon?.AimAmount ?? 0f;
		var steady = MathX.Lerp( 1f, 0.25f, aim );
		var movement = controller.GetComponent<PlayerMovement>();
		var rushing = movement is not null && (movement.IsDashing || movement.IsSliding) && speed > 150f;

		// Back to idle (or the sprint pose) when a one-shot animation has finished.
		if ( busyUntil )
		{
			var wantSprint = rushing && !(weapon?.IsReloading ?? false) && !(weapon?.IsAiming ?? false);
			if ( shellReloading ) PlayLoop( "reload_hold" );
			else PlayLoop( wantSprint ? "sprint" : "idle" );
		}

		// Sway from the mouse, a walking bob, and sliding toward the middle when aiming.
		bobTime += dt * MathX.Clamp( speed / 55f, 0f, 6f );
		var moving = MathX.Clamp( speed / 300f, 0f, 1f ) * steady;
		var bob = new Vector3( 0f, MathF.Sin( bobTime * 0.5f ) * 0.45f * moving, -MathF.Abs( MathF.Sin( bobTime ) ) * 0.55f * moving );
		var lag = new Vector3( 0f, inertia.x * 0.05f * SwayAmount * steady, -inertia.y * 0.05f * SwayAmount * steady );
		// Aiming: the gun turns straight and slides so its sight line runs down the middle of the view. A scoped gun
		// carries on until the eyepiece is at the eye, then drops away under the scope view.
		var (aimOffset, aimYaw) = AimPose();
		var scope = weapon?.ScopeAmount ?? 0f;
		var tuck = Vector3.Down * 40f * MathX.Clamp( (scope - 0.6f) / 0.4f, 0f, 1f );
		var settle = Vector3.Forward * MathF.Sin( aim * MathF.PI ) * -1.2f;        // a small pull back as it comes up
		var offset = Vector3.Lerp( Vector3.Zero, aimOffset, aim ) + settle + tuck + bob + lag;
		GameObject.LocalPosition = offset;
		GameObject.LocalRotation = Rotation.FromYaw( aimYaw * aim ) * Rotation.From( inertia.y * 0.35f * steady, inertia.x * 0.35f * steady, 0f );
	}
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
		if ( custom ) { UpdateCustom(); return; }

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

	public void Fire()
	{
		if ( custom ) { shellReloading = false; PlayOnce( "fire", FireSeconds, 1f ); return; }
		Gun?.Set( "b_attack", true );
	}

	public void DryFire()
	{
		if ( custom ) return;
		Gun?.Set( "b_attack_dry", true );
	}

	public void Reload( float duration )
	{
		if ( !Gun.IsValid() ) return;
		if ( custom ) { PlayOnce( "reload", duration, duration > 0.01f ? (100f / 30f) / duration : 1f ); return; }
		// Speed the graph up or down so its reload finishes with the weapon's real reload time.
		Gun.Set( "speed_reload", duration > 0.01f ? GraphReloadTime / duration : 1f );
		Gun.Set( "b_reload", true );
	}

	/// <summary> Shell-by-shell reload (shotgun): open, push shells, close. </summary>
	// Shell-by-shell reloads on custom models: roll the gun over, feed shells one at a time (holding the loading pose
	// between them), then roll back and rack the pump.
	bool shellReloading;

	public void ShellReloadBegin()
	{
		if ( !custom ) { Gun?.Set( "b_reloading", true ); return; }
		shellReloading = true;
		PlayOnce( "reload_start", 0.4f, 1f );
	}

	public void InsertShell( bool intoEmptyGun )
	{
		if ( !custom ) { Gun?.Set( intoEmptyGun ? "b_reloading_first_shell" : "b_reloading_shell", true ); return; }
		var seconds = MathF.Max( 0.15f, weapon?.ShellTime ?? 0.5f );
		PlayOnce( "reload_shell", seconds, (15f / 30f) / seconds );
	}

	public void ShellReloadEnd()
	{
		if ( !custom ) { Gun?.Set( "b_reloading", false ); return; }
		if ( !shellReloading ) return;
		shellReloading = false;
		PlayOnce( "reload_end", 0.6f, 1f );
	}

	/// <summary> Length of the custom model's fire animation (the shotgun's includes racking the pump). </summary>
	float FireSeconds => Gun.IsValid() && Gun.Model is { } m && m.Name.Contains( "shotgun" ) ? 0.7f : 0.27f;

	/// <summary> World transform of the gun's muzzle, for the flash. Falls back to the camera. </summary>
	public Transform Muzzle()
	{
		if ( Gun.IsValid() && Gun.GetAttachment( "muzzle" ) is Transform t ) return t;
		return WorldTransform;
	}
}
