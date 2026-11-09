using Sandbox.Citizen;

/// <summary>
/// What a player looks like from the outside: the gun in their hands, the matching hold pose,
/// and the over-the-shoulder camera. Runs for every player on every machine, so you see your own
/// body in third person and everyone else's all the time.
/// Sits on the player next to the PlayerController and WeaponInventory.
/// </summary>
public sealed class PlayerAppearance : Component
{
	/// <summary> Third-person camera: distance back, sideways (shoulder), up. </summary>
	[Property] public Vector3 ShoulderOffset { get; set; } = new( 95f, 26f, 4f );
	/// <summary> Camera offset while aiming down the sights in third person. </summary>
	[Property] public Vector3 AimShoulderOffset { get; set; } = new( 55f, 22f, 2f );
	/// <summary> Pulled back on top of the shoulder offset, always (not while aiming). </summary>
	[Property] public Vector3 ExtraPullBack { get; set; } = new( 25f, 0f, 4f );
	/// <summary> Pulled back further while swimming, jetting or floating, so you can see around you in open water. </summary>
	[Property] public Vector3 FlyingPullBack { get; set; } = new( 55f, 4f, 14f );
	float flyingCam;
	/// <summary> The hand attachment on the body model that guns hang from. </summary>
	[Property] public string HandAttachment { get; set; } = "hold_R";

	/// <summary>
	/// The gun sits at the hand but points where the player is aiming (the hand's own angle changes
	/// with every animation, so following it leaves the gun crooked). This is an extra tweak on top.
	/// </summary>
	[Property] public Angles GunAngleOffset { get; set; } = Angles.Zero;

	/// <summary>
	/// True: the gun takes the hand's own angle, so it sits in both hands the way the hold animation
	/// was made. False: it points straight along the body's facing instead.
	/// </summary>
	[Property] public bool FollowHandRotation { get; set; } = true;

	/// <summary> Skin set on the body, matched to the light-skinned first-person arms. Empty leaves it alone. </summary>
	[Property, Group( "Look" )] public string SkinMaterialGroup { get; set; } = "skin_light";
	/// <summary> Which male head to use (0-4). Head 3 (index 2) is the light-skinned one. -1 leaves it alone. </summary>
	[Property, Group( "Look" )] public int HeadBodyGroup { get; set; } = 2;

	PlayerController Controller => GetComponent<PlayerController>();
	WeaponInventory Inventory => GetComponent<WeaponInventory>();
	PlayerSurvivor Survivor => GetComponent<PlayerSurvivor>();
	SkinnedModelRenderer Body => Controller?.Renderer;

	readonly Dictionary<PlayerWeapon, GameObject> worldGuns = new();
	Transform? leftHandTarget;

	/// <summary> True when this machine should see the body and the gun in hand. </summary>
	public bool ShowsBody => IsProxy || (Controller?.ThirdPerson ?? false);

	/// <summary> The weapon in hand: the local choice for us, the synced slot for everyone else. </summary>
	public PlayerWeapon HeldWeapon
	{
		get
		{
			var inventory = Inventory;
			if ( inventory is null ) return GetComponent<PlayerWeapon>();
			if ( !IsProxy && inventory.Current.IsValid() ) return inventory.Current;
			return Components.GetAll<PlayerWeapon>( FindMode.EverythingInSelf ).FirstOrDefault( w => w.Slot == inventory.CurrentSlot )
				?? Components.GetAll<PlayerWeapon>( FindMode.EverythingInSelf ).FirstOrDefault();
		}
	}

	/// <summary> The visible gun model in hand for a weapon, if it has one. </summary>
	public GameObject WorldGunFor( PlayerWeapon weapon ) =>
		weapon is not null && worldGuns.TryGetValue( weapon, out var go ) && go.IsValid() ? go : null;

	protected override void OnUpdate()
	{
		var body = Body;
		if ( !body.IsValid() ) return;

		// Keep the look matched to the first-person arms (the dresser can reset these when it applies clothes).
		if ( !string.IsNullOrEmpty( SkinMaterialGroup ) && body.MaterialGroup != SkinMaterialGroup )
			body.MaterialGroup = SkinMaterialGroup;
		if ( HeadBodyGroup >= 0 && body.GetBodyGroup( "Head" ) != HeadBodyGroup )
			body.SetBodyGroup( "Head", HeadBodyGroup );

		var held = HeldWeapon;
		var alive = Survivor?.IsAlive ?? true;
		var show = ShowsBody && alive;

		EnsureWorldGuns( body );
		foreach ( var (weapon, gun) in worldGuns )
		{
			if ( gun.IsValid() ) gun.Enabled = show && weapon == held && !(GetComponent<PlayerRig>()?.AxeOut ?? false);
		}

		// Hold pose for the gun in hand (rifle, shotgun...). Everything else the controller animates.
		body.Set( "holdtype", (int)(held?.HoldType ?? CitizenAnimationHelper.HoldTypes.None) );
		body.Set( "holdtype_handedness", 0 );

		// Left hand onto the handguard (worked out last frame, after the gun was placed).
		if ( show && leftHandTarget is Transform target ) body.SetIk( "hand_left", target );
		else body.ClearIk( "hand_left" );

		// Our own over-the-shoulder camera, tightening in while aiming.
		var controller = Controller;
		if ( !IsProxy && controller.IsValid() && controller.ThirdPerson )
		{
			var aim = held?.AimAmount ?? 0f;
			// Ducking moves the eye by up to 36 units (in the air it drops, on the ground it doesn't), so ducking in mid-air and
			// then landing in a slide made the camera dip and pop back up. Third person holds the camera at standing height instead.
			var standingEye = controller.BodyHeight - controller.EyeDistanceFromTop;
			var eyeNow = controller.EyePosition.z - controller.WorldPosition.z;
			var heightFix = Vector3.Up * (standingEye - eyeNow);
			var movement = GetComponent<PlayerMovement>();
			var jet = GetComponent<Jetpack>();
			var flying = (movement?.IsFloating ?? false) || (jet is not null && (jet.Thrusting || jet.Surging || jet.Charging))
				|| (!controller.IsOnGround && (GetComponent<WaterFeel>()?.Swimming ?? 0f) > 0.5f);
			flyingCam = MathX.Lerp( flyingCam, flying ? 1f : 0f, 1f - MathF.Exp( -2.5f * Time.Delta ) );
			var pull = (ExtraPullBack + FlyingPullBack * flyingCam) * (1f - aim);
			controller.CameraOffset = Vector3.Lerp( ShoulderOffset, AimShoulderOffset, aim ) + pull + heightFix;
		}
	}

	/// <summary> One gun model per weapon, hung off the body's hand. </summary>
	void EnsureWorldGuns( SkinnedModelRenderer body )
	{
		foreach ( var weapon in Components.GetAll<PlayerWeapon>( FindMode.EverythingInSelf ) )
		{
			if ( weapon.WorldModel is null ) continue;
			if ( worldGuns.TryGetValue( weapon, out var existing ) && existing.IsValid() ) continue;

			var go = new GameObject( body.GameObject, false, $"World {weapon.DisplayName}" );
			go.NetworkMode = NetworkMode.Never;
			go.LocalPosition = Vector3.Zero;
			go.LocalRotation = Rotation.Identity;
			var renderer = go.AddComponent<SkinnedModelRenderer>();
			renderer.Model = weapon.WorldModel;
			worldGuns[weapon] = go;
		}
	}

	/// <summary>
	/// After animation: put the gun in hand at the hand, pointing along the body's facing (which follows
	/// the aim and is synced for everyone). Pitch comes from our own view; other players' view angles
	/// aren't synced, so theirs stay level. Done here, not in OnUpdate, because the hand moves when the
	/// animation runs and a gun parented to it ends up crooked.
	/// </summary>
	protected override void OnPreRender()
	{
		var body = Body;
		if ( !body.IsValid() ) return;
		if ( body.GetAttachment( HandAttachment ) is not Transform hand ) return;

		Rotation rotation;
		if ( FollowHandRotation )
		{
			rotation = hand.Rotation * GunAngleOffset.ToRotation();
		}
		else
		{
			var facing = body.WorldRotation.Forward.WithZ( 0 ).Normal;
			var pitch = IsProxy ? 0f : (Controller?.EyeAngles.pitch ?? 0f) * 0.85f;
			rotation = new Angles( pitch, Rotation.LookAt( facing ).Yaw(), 0f ).ToRotation() * GunAngleOffset.ToRotation();
		}
		leftHandTarget = null;
		foreach ( var (weapon, gun) in worldGuns )
		{
			if ( !gun.IsValid() || !gun.Enabled ) continue;
			// Turn the gun, then slide it so its grip (not the model's origin) lands in the palm.
			var gunRotation = rotation * weapon.WorldModelAngles.ToRotation();
			gun.WorldRotation = gunRotation;
			gun.WorldPosition = hand.Position - gunRotation * weapon.WorldModelGrip;

			if ( !weapon.WorldModelLeftHand.IsNearZeroLength )
				leftHandTarget = gun.WorldTransform.ToWorld( new Transform( weapon.WorldModelLeftHand, weapon.WorldModelLeftHandAngles.ToRotation() ) );
		}
	}

	/// <summary> Plays the body's fire animation (and a flash at the gun in hand) for the given weapon. </summary>
	public void PlayFire( PlayerWeapon weapon, GameObject flashPrefab, float flashScale )
	{
		Body?.Set( "b_attack", true );
		var gun = WorldGunFor( weapon );
		if ( !gun.IsValid() || !gun.Enabled || !flashPrefab.IsValid() ) return;

		var renderer = gun.GetComponent<SkinnedModelRenderer>();
		var muzzle = renderer?.GetAttachment( "muzzle" ) ?? gun.WorldTransform;
		var flash = flashPrefab.Clone( muzzle.Position, muzzle.Rotation );
		flash.SetParent( gun, true );
		flash.WorldScale = flashScale;
		flash.AddComponent<SelfDestruct>().Lifetime = 2f;
	}

	public void PlayReload() => Body?.Set( "b_reload", true );
}
