using Godot;

/// <summary>
/// Port of the s&box PlayerWeapon: a hitscan gun. Attack1 fires, R reloads, Attack2 aims (eased in over AimTime,
/// slowing you and your look, the rifle's scope taking over at the end). Pellets above 1 makes it a shotgun, and
/// ShellReload loads one shell at a time (firing interrupts it). One node per weapon under the player;
/// WeaponInventory activates one at a time. Sounds are hooks only: the s&box ones were Facepunch cloud assets.
/// </summary>
public partial class PlayerWeapon : Node
{
	// Stats (rifle defaults; the shotgun's come from Shotgun())
	public float Damage = 34f;
	public float FireRate = 8f;
	public float Range = 5000f * U.In;
	public float SpreadDegrees = 1.2f;
	public float Recoil = 0.6f;
	public int MagazineSize = 30;
	public int StartingReserve = 180;
	public float ReloadTime = 1.6f;
	public float BulletImpulse = 150f * U.In;
	public int Pellets = 1;
	public bool Automatic = true;
	/// <summary>
	/// Seconds the trigger can be held to charge a shot (0: none). The shot goes on release; held the full time it is
	/// a charged shot (the trident flies faster and goes through enemies).
	/// </summary>
	public float ChargeTime;
	/// <summary> How charged the held shot is, 0 to 1 (for the HUD). </summary>
	public float ChargeAmount => charging && ChargeTime > 0f ? MathX.Clamp( (float)sinceChargeStart / ChargeTime, 0f, 1f ) : 0f;
	bool charging, chargeFull, chargedShot;
	/// <summary> This shot came out of the gun in the robot's hands (third person). </summary>
	bool thirdPersonMuzzle;
	/// <summary> The trigger has stayed down since the last shot (holding on charges the next trident). </summary>
	bool heldSinceShot;
	TimeSince sinceChargeStart;
	ulong chargeFrame;
	public bool ShellReload;
	public float ShellTime = 0.5f;
	/// <summary> Fires a flying trident (TridentBolt) instead of hitscan rays. </summary>
	public bool Projectile;
	/// <summary>
	/// The Trident-Bot's melee slot: the trident held as a spear. Left click thrusts it (twice the axe's reach), right
	/// click raises the energy shield, which takes BlockShare of every hit while it is up.
	/// </summary>
	public bool Spear;
	public bool Blocking { get; private set; }
	public const float BlockShare = 0.65f;
	public float SpearReach = 220f * U.In;   // the axe's reach is 110
	public float SpearRadius = 18f * U.In;
	public float SpearCooldown = 0.55f, SpearWindUp = 0.14f;   // the hit lands at SpearWindUp, at the end of the lunge
	TimeSince spearSwing = 99f;
	bool spearStruck, spearLeft;
	float shieldUp;
	/// <summary>
	/// How far into its thrust the spear is, for the model: 0 at rest, a quick cock back (to -0.35), a violent lunge
	/// that overshoots (1.2), settles at full reach and then comes back.
	/// </summary>
	public float SpearThrust => ThrustCurve( (float)spearSwing, SpearCooldown );

	public static float ThrustCurve( float t, float length )
	{
		const float cock = 0.09f, lunge = 0.15f, settle = 0.22f;
		if ( t < 0f || t >= length ) return 0f;
		if ( t < cock ) { var k = t / cock; return -0.35f * k * k * (3f - 2f * k); }
		if ( t < lunge ) { var k = 1f - (t - cock) / (lunge - cock); return MathX.Lerp( -0.35f, 1.2f, 1f - k * k * k ); }
		if ( t < settle ) return MathX.Lerp( 1.2f, 1f, (t - lunge) / (settle - lunge) );
		var r = (t - settle) / (length - settle);
		return 1f - r * r * (3f - 2f * r);
	}
	public float ShieldRaise => shieldUp;
	/// <summary> Trident-Bot's surge thrust (TridentSurge): the held weapon cocked back (below 0) or driven forward (to 1.2). </summary>
	public float SurgePose;
	/// <summary> The firing sound (null: "{ModelName}_shoot"). </summary>
	public string ShootSound;

	public string DisplayName = "Rifle";
	public int Slot = 1;
	/// <summary> Which custom first-person model (rifle, shotgun). </summary>
	public string ModelName = "rifle";

	public float MuzzleFlashScale = 0.3f;
	public float ShakePerShot = 0.12f;
	public float FovKickPerShot = 1.5f;

	// Aiming
	public float AimFovScale = 0.7f;
	public float AimSpreadScale = 0.15f;
	public float AimRecoilScale = 0.55f;
	public float AimMoveSpeedScale = 0.6f;
	public float AimTime = 0.2f;
	public float ScopeFovScale = 0.3f;

	public int DamageLevel;
	public float DamagePerLevel = 0.25f;
	public float CurrentDamage => Damage * (1f + DamagePerLevel * DamageLevel);
	/// <summary>
	/// A charge weapon's damage with no charge (0: the charge doesn't change damage). The shot does this let go at once,
	/// rising evenly to <see cref="Damage"/> at a full charge; damage upgrades scale both.
	/// </summary>
	public float ChargeMinDamage;
	/// <summary> How charged the shot going out is, 0 to 1. </summary>
	float shotCharge = 1f;
	/// <summary> This shot's damage, after the charge and the upgrades. </summary>
	public float ShotDamage => ChargeMinDamage > 0f
		? MathX.Lerp( ChargeMinDamage, Damage, MathX.Clamp( shotCharge, 0f, 1f ) ) * (1f + DamagePerLevel * DamageLevel)
		: CurrentDamage;

	public int Ammo { get; private set; }
	public int Reserve { get; private set; }
	public bool InfiniteAmmo;
	public bool IsReloading { get; private set; }
	PlayerPerks Perks => PlayerPerks.Of( controller );
	/// <summary> Reload time and rate of fire with the player's pickups counted in. </summary>
	/// <summary> The rifle's tree (Fire Rate, Faster Reload, Overclock) is the only one that speeds these up. </summary>
	public float CurrentReloadTime => ReloadTime * (ModelName == "rifle" ? Perks?.ReloadScale ?? 1f : 1f) * (Seeds ? CoralFx.SeedReloadScale( Skill( SkillId.QuickSeed ) ) : 1f);
	public float CurrentFireRate => FireRate * (ModelName == "rifle" ? (Perks?.FireRateScale ?? 1f) * (overclockLeft > 0 ? 1f + SkillFx.OverclockRate : 1f) : 1f);
	public float ReloadProgress => !IsReloading ? 0f : ShellReload ? (float)Ammo / MagazineSize : 1f - MathX.Clamp( reloadFinished / MathF.Max( 0.01f, CurrentReloadTime ), 0f, 1f );

	public bool HasScope => DisplayName == "Rifle" && RifleModels.Get().Scope && !(controller?.SeenFromOutside ?? false);

	Color? FlashTint => ModelName switch { "rifle" => RifleModels.Get().Flash, "trident" => TridentModel.Green, _ => null };

	/// <summary> The model folder to show: the rifle's is whichever design is picked (RifleModels). </summary>
	public string ViewModelName => ModelName == "rifle" ? RifleModels.Get().Model : ModelName;
	public float ScopeAmount { get; private set; }
	public bool IsAiming { get; private set; }
	public float AimAmount { get; private set; }
	public bool ForceAim;

	public TimeSince TimeSinceHit { get; private set; } = 999f;
	public TimeSince TimeSinceKill { get; private set; } = 999f;
	public TimeSince TimeSinceShot { get; private set; } = 999f;

	public ViewModel ViewModel { get; private set; }

	/// <summary> s&box Component.Enabled: only the weapon in hand is active. </summary>
	public bool Active
	{
		get => active;
		set
		{
			if ( active == value ) return;
			active = value;
			if ( value ) OnEnabled(); else OnDisabled();
		}
	}
	bool active;

	/// <summary> The melee swing hides the gun and stops it firing (set by PlayerMelee). </summary>
	public Func<bool> MeleeActive;

	PlayerController controller;
	PlayerMovement movement;
	CameraJuice juice;
	float aimLinear;
	TimeUntil reloadFinished;
	TimeUntil nextShell;
	bool firstShell;
	double lastFrameAt;
	float frameStep;
	readonly HashSet<Node> hitThisShot = new();

	public static PlayerWeapon Rifle() => new() { Name = "Rifle" };

	/// <summary>
	/// The trident shooter (bought on the wreck site): slot 3, a few heavy tridents per load that fly to the crosshair
	/// with a slight drop, one at a time.
	/// </summary>
	public static PlayerWeapon Trident( int slot = 3 ) => new()
	{
		Name = "Trident", DisplayName = "Trident Shooter", Slot = slot, ModelName = "trident",
		Damage = 250f /* a full charge; 380 flat before, 140 at first (playtest) */, ChargeMinDamage = 75f, FireRate = 1.4f, Range = 6000f * U.In, SpreadDegrees = 0.25f, Recoil = 2.4f,
		MagazineSize = 5, StartingReserve = 30, ReloadTime = 2.1f, BulletImpulse = 200f * U.In, Automatic = false,
		MuzzleFlashScale = 0.25f, ShakePerShot = 0.35f, FovKickPerShot = 2.5f,
		AimFovScale = 0.72f, AimSpreadScale = 0.3f, AimRecoilScale = 0.6f,
		Projectile = true, ShootSound = "harpoon", ChargeTime = 1f,
	};

	/// <summary> The Trident-Bot's second slot: the trident as a spear, and an energy shield (right click) that takes 65% of hits. </summary>
	public static PlayerWeapon TridentSpear( int slot = 2 ) => new()
	{
		Name = "Trident Spear", DisplayName = "Trident & Shield", Slot = slot, ModelName = "spear",
		Damage = 90f, FireRate = 1.6f, Range = 220f * U.In, MagazineSize = 1, StartingReserve = 0, Automatic = false,
		MuzzleFlashScale = 0f, Spear = true,
	};

	public static PlayerWeapon Shotgun() => new()
	{
		Name = "Shotgun", DisplayName = "Shotgun", Slot = 2, ModelName = "shotgun",
		Damage = 20f, FireRate = 1.4f, Range = 2600f * U.In, SpreadDegrees = 5.5f, Recoil = 3.5f,
		MagazineSize = 6, StartingReserve = 42, BulletImpulse = 120f * U.In, Pellets = 12, Automatic = false,
		ShellReload = true, ShellTime = 0.5f, MuzzleFlashScale = 0.45f, ShakePerShot = 0.45f, FovKickPerShot = 4f,
		AimFovScale = 0.8f, AimSpreadScale = 0.6f, AimRecoilScale = 0.7f,
	};

	public override void _Ready()
	{
		controller = GetParent<PlayerController>();
		movement = controller.GetNodeOrNull<PlayerMovement>( "PlayerMovement" );
		juice = controller.GetNodeOrNull<CameraJuice>( "CameraJuice" );
		Ammo = Vacuum ? StartGunk : MagazineSize;
		Reserve = StartingReserve;
	}

	public void RefillAmmo()
	{
		if ( Vacuum ) return;   // the Gore Vac fills only with what it sucks up
		Ammo = MagazineSize;
		Reserve = Math.Max( Reserve, StartingReserve );
		IsReloading = false;
	}

	public void AddAmmo( int amount ) => Reserve += amount;

	/// <summary> Dropped (a different robot was picked): its first person model goes with it. </summary>
	public override void _ExitTree()
	{
		if ( ViewModel is not null && IsInstanceValid( ViewModel ) ) ViewModel.QueueFree();
		ViewModel = null;
		if ( Bubbles ) BubbleTrap.PopAll( controller );
	}

	void OnEnabled()
	{
		if ( ViewModel is not null ) { ViewModel.Visible = true; ViewModel.Shown(); }
	}

	void OnDisabled()
	{
		if ( ViewModel is not null ) ViewModel.Visible = false;
		Blocking = false;
		shieldUp = 0f;
		CancelReload();
		Sucking = false;
		ReleaseCaught();
		Blowing = false;
		charging = false;
		suckSound?.Stop();
		blowSound?.Stop();
		if ( movement is not null )
		{
			controller.WalkSpeed = movement.MoveSpeed;
			controller.RunSpeed = movement.SprintSpeed;
		}
		IsAiming = false;
		AimAmount = 0f;
		aimLinear = 0f;
		ScopeAmount = 0f;
		controller.LookSensitivity = GameSettings.LookSensitivity;
	}

	void EnsureViewModel()
	{
		if ( ViewModel is not null || controller.Camera is null || controller.IsRemote ) return;   // a remote robot's gun is the one in its hands
		ViewModel = ViewModel.Create( controller.Camera, ViewModelName, controller, this );
	}

	public override void _Process( double delta )
	{
		using var remoteSounds = Sfx.Remote( controller );   // online: another player's robot is heard where it is
		// Game time since the last frame, measured on the same clock as TimeSinceShot (for Shoot's carry-over).
		frameStep = (float)(Clock.Now - lastFrameAt);
		lastFrameAt = Clock.Now;
		SyncSkills();
		if ( !Active ) return;
		// Dead (issue #34): no gun on screen, no aiming, no firing or reloading.
		if ( controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) is { Health: not null, IsAlive: false } )
		{
			if ( ViewModel is not null ) ViewModel.Visible = false;
			AimAmount = ScopeAmount = 0f;
			return;
		}
		var dt = (float)delta;
		// A different rifle design was picked: put this one away and bring that one up.
		if ( ViewModel is not null && ViewModel.ModelName != ViewModelName ) { ViewModel.QueueFree(); ViewModel = null; }
		EnsureViewModel();

		var melee = MeleeActive?.Invoke() ?? false;
		if ( ViewModel is not null ) ViewModel.Visible = !controller.SeenFromOutside && !DevFlags.HideWeapon && !DevFlags.FreeCam && !melee;
		ShowLoaded();

		if ( InfiniteAmmo ) { Ammo = MagazineSize; Reserve = Math.Max( Reserve, StartingReserve ); }

		if ( IsReloading && ShellReload ) UpdateShellReload();
		else if ( IsReloading && reloadFinished <= 0f ) FinishReload();

		if ( Spear )
		{
			UpdateSpear( dt, !(DevFlags.Placing || melee || LocalBlocked) );
			return;
		}

		if ( Vacuum )
		{
			// The Gore Vac: no aiming or reloading; fire sucks and aim spits (GoreVac.cs).
			var free = !(DevFlags.Placing || melee || LocalBlocked);
			UpdateVacuum( dt, free );
			return;
		}

		if ( !Bubbles && !Lure && !Seeds ) UpdateAiming( dt );   // the Bubble Cannon's right click pops its bubbles, the lure's bursts it, the seeds' grows a wall

		if ( controller.In.JustPressed( "reload" ) ) StartReload();
		if ( ShellReload && IsReloading && controller.In.JustPressed( "attack1" ) && Ammo > 0 ) CancelReload();
		if ( DevFlags.Placing || melee ) return;   // left click is placing an enemy; the axe is out
		if ( LocalBlocked ) return;   // not during the dive, nor while clicking the dev menu, nor while Esc has the mouse
		if ( Bubbles && controller.In.JustPressed( "attack2" ) ) BubbleTrap.PopAll( controller, true );
		if ( Lure && controller.In.JustPressed( "attack2" ) ) ActiveLure?.Burst();
		if ( Seeds && controller.In.JustPressed( "attack2" ) && !IsReloading && TimeSinceShot >= 0.3f )
		{
			if ( Ammo > 0 ) { TimeSinceShot = 0f; Ammo--; PlantSeed( controller.Camera.GlobalPosition, -controller.Camera.GlobalBasis.Z, true ); }
			else if ( Reserve > 0 ) StartReload();
		}



		if ( ChargeTime > 0f )
		{
			// A pull fires at once. Keep holding after the shot and the next trident charges; let go with it full to
			// fire it charged (let go early and it stays loaded). A charge the gun was put away during is dropped.
			var frame = Engine.GetProcessFrames();
			if ( charging && frame - chargeFrame > 3 ) charging = false;
			chargeFrame = frame;
			if ( IsReloading ) charging = false;
			var held = controller.In.Pressed( "attack1" );
			if ( !charging && !Bubbles )
			{
				// The trident: a pull only starts the charge, nothing leaves yet. Let go to fire: charged if it filled,
				// a plain shot if it didn't. (One trident a pull, never a plain one and then a charged one.)
				if ( controller.In.JustPressed( "attack1" ) && !IsReloading && TimeSinceShot >= 1f / CurrentFireRate )
				{
					if ( Ammo > 0 ) { charging = true; chargeFull = false; sinceChargeStart = 0f; Sfx.Play2D( "trident_charge", 0.5f, bus: Sfx.GunBus ); }
					else if ( Reserve > 0 ) StartReload();
					else Sfx.Play2D( "dry_fire" );
				}
			}
			else if ( charging && !Bubbles )
			{
				if ( !chargeFull && ChargeAmount >= 1f ) { chargeFull = true; Controls.Rumble( 0.3f ); }
				if ( !held )
				{
					shotCharge = chargeFull ? 1f : ChargeAmount;
					charging = false;
					chargedShot = chargeFull;
					Shoot();
					chargedShot = false;
					shotCharge = 1f;
				}
			}
			else if ( !charging )
			{
				if ( controller.In.JustPressed( "attack1" ) && !IsReloading && TimeSinceShot >= 1f / CurrentFireRate )
				{
					if ( Ammo > 0 ) { Shoot(); heldSinceShot = true; }
					else if ( Reserve > 0 ) StartReload();
					else Sfx.Play2D( "dry_fire" );
				}
				else if ( held && heldSinceShot && Ammo > 0 && !IsReloading && TimeSinceShot >= 1f / CurrentFireRate )
				{
					charging = true; chargeFull = false; sinceChargeStart = 0f;
					Sfx.Play2D( Bubbles ? "bubble_charge" : "trident_charge", 0.5f, bus: Sfx.GunBus );
				}
				if ( !held ) heldSinceShot = false;
			}
			else
			{
				if ( !chargeFull && ChargeAmount >= 1f ) { chargeFull = true; Controls.Rumble( 0.3f ); }
				if ( !held )
				{
					charging = false;
					heldSinceShot = false;
					if ( chargeFull ) { chargedShot = true; Shoot(); chargedShot = false; }
				}
			}
			return;
		}

		var wantsFire = Automatic ? controller.In.Pressed( "attack1" ) : controller.In.JustPressed( "attack1" );
		if ( wantsFire && !IsReloading && TimeSinceShot >= 1f / CurrentFireRate && !(Harpoon && LineOut) )
		{
			if ( Ammo > 0 ) Shoot();
			else if ( Reserve > 0 ) StartReload();
			else if ( controller.In.JustPressed( "attack1" ) ) Sfx.Play2D( "dry_fire" );   // one click per pull, not every frame it is held
		}
	}

	Node3D loaded;

	/// <summary>
	/// The spear and shield: left click thrusts (the hit lands a moment in, a fan of sweeps out to twice the axe's
	/// reach, PlayerMelee.StrikeWith); right click holds the shield up, which takes BlockShare of each hit
	/// (PlayerPerks.Absorb). No thrusting from behind the shield.
	/// </summary>
	void UpdateSpear( float dt, bool free )
	{
		Ammo = MagazineSize;
		var wasBlocking = Blocking;
		Blocking = free && (controller.In.Pressed( "attack2" ) || forceBlock);
		shieldUp = MathX.Lerp( shieldUp, Blocking ? 1f : 0f, 1f - MathF.Exp( -14f * dt ) );
		ShieldSkills( wasBlocking, dt );
		if ( free && (!Blocking || Skill( SkillId.Spartan ) > 0) && controller.In.JustPressed( "attack1" ) && spearSwing >= SpearCooldown ) TestThrust();
		if ( !spearStruck && spearSwing >= SpearWindUp && spearSwing < SpearCooldown )
		{
			spearStruck = true;
			controller.GetNodeOrNull<PlayerMelee>( "PlayerMelee" )?.StrikeWith( SpearReach, SpearRadius, CurrentDamage * TakeRiposte(), this );
			// The lunge lands: a jolt through the camera and the pad.
			juice?.Shake( 0.45f );
			juice?.KickFov( 5f );
			Controls.Rumble( 0.3f );
		}
		if ( ViewModel is not null ) SpearModel.Pose( ViewModel, MathF.Abs( SurgePose ) > MathF.Abs( SpearThrust ) ? SurgePose : SpearThrust, spearLeft, shieldUp, dt );
	}

	/// <summary> Thrusts the spear as if clicked (also the testing hook). </summary>
	public void TestThrust()
	{
		spearSwing = 0f;
		spearStruck = false;
		spearLeft = !spearLeft;
		Sfx.Play2D( "axe_swing", 1f, Rng.Float( 0.95f, 1.08f ) );
	}

	/// <summary> Testing aid: raise or lower the shield as if right click were held. </summary>
	public void TestBlock( bool up ) { Blocking = forceBlock = up; shieldUp = up ? 1f : 0f; }
	bool forceBlock;

	/// <summary> The trident shooter shows its next trident in the mouth: gone just after a shot, back once one is loaded. </summary>
	void ShowLoaded()
	{
		if ( Bubbles && ViewModel is not null ) { ShowBubbleLoaded(); return; }
		if ( !(Projectile || Harpoon || Lure) || ViewModel is null ) return;
		if ( loaded is null || !IsInstanceValid( loaded ) ) loaded = ViewModel.FindChild( "Loaded", true, false ) as Node3D;
		if ( loaded is not null ) loaded.Visible = Ammo > 0 && TimeSinceShot > 0.35f && !LineOut;
	}

	void UpdateAiming( float dt )
	{
		IsAiming = (controller.In.Pressed( "attack2" ) || ForceAim) && !IsReloading;
		aimLinear = MathX.Approach( aimLinear, IsAiming ? 1f : 0f, dt / MathF.Max( 0.05f, AimTime ) );
		AimAmount = aimLinear * aimLinear * (3f - 2f * aimLinear);
		var scope = HasScope ? MathX.Clamp( (AimAmount - 0.78f) / 0.22f, 0f, 1f ) : 0f;
		ScopeAmount = scope * scope * (3f - 2f * scope);

		// Look slower when zoomed, in step with the zoom.
		var zoom = MathX.Lerp( 1f, AimFovScale, AimAmount ) * MathX.Lerp( 1f, ScopeFovScale / MathF.Max( 0.05f, AimFovScale ), ScopeAmount );
		controller.LookSensitivity = GameSettings.LookSensitivity * MathX.Lerp( 1f, zoom, 0.85f );

		// Slow the player while aiming (the movement kit owns the base speeds).
		var baseWalk = movement?.MoveSpeed ?? controller.WalkSpeed;
		var baseRun = movement?.SprintSpeed ?? controller.RunSpeed;
		var scale = MathX.Lerp( 1f, AimMoveSpeedScale, AimAmount );
		controller.WalkSpeed = baseWalk * scale;
		controller.RunSpeed = MathX.Lerp( baseRun, baseWalk * AimMoveSpeedScale, AimAmount );
	}

	/// <summary> The mouse is out of the game (Esc). Scripted tests run with it free, so they don't count. </summary>
	public static bool MouseFree => Input.MouseMode != Input.MouseModeEnum.Captured && !AutoTest.Requested;
	/// <summary> This machine's menus or the dive hold the local player's gun (a remote player's owner already held its presses back). </summary>
	bool LocalBlocked => !controller.IsRemote && (DockHouse.WeaponsHeld || DevMenu.IsOpen || MouseFree);

	/// <summary> Testing aid: fires one shot as if the trigger was pulled. </summary>
	public void TestFire() => Shoot();
	/// <summary> Testing aid: fires one charged shot (as if the trigger had been held the full time). </summary>
	public void TestFireCharged() { chargedShot = true; Shoot(); chargedShot = false; }

	void Shoot()
	{
		// Firing on the first frame the gun is ready: keep the part of that frame that ran past the interval, so held
		// fire keeps the real rate at any frame rate. Otherwise (a fresh pull, a test shot) start from zero.
		var interval = 1f / CurrentFireRate;
		var over = TimeSinceShot - interval;
		TimeSinceShot = over > 0f && over < frameStep && over < interval ? over : 0f;
		Sfx.Play2D( ShootSound ?? $"{ModelName}_shoot", 1.1f, bus: Sfx.GunBus );   // 10% louder (playtest 11/12)
		Controls.Rumble( ModelName == "shotgun" ? 0.55f : 0.22f );
		Ammo--;
		var camera = controller.Camera;
		var basis = camera.GlobalBasis;
		var forward = -basis.Z;
		var start = camera.GlobalPosition;
		var spread = MathF.Tan( MathX.Rad( SpreadDegrees * MathX.Lerp( 1f, AimSpreadScale, AimAmount ) ) );

		var trailStart = start + forward * 24f * U.In - basis.Y * 7f * U.In + basis.X * 7f * U.In;
		if ( ViewModel is not null && ViewModel.Visible )
		{
			ViewModel.Fire();
			var barrel = ViewModel.Muzzle();
			trailStart = barrel.Origin;
			if ( MuzzleFlashScale > 0f ) Fx.MuzzleFlash( barrel.Origin, barrel.Basis.Y, MuzzleFlashScale, ViewModel, FlashTint );
		}
		else if ( controller.SeenFromOutside && controller.GetNodeOrNull<PlayerRig>( "PlayerRig" )?.WorldMuzzle() is { } held )
		{
			// Third person: the flash and the tracers come out of the gun in the robot's hands, not where the
			// first person gun would be on screen.
			trailStart = held.Origin;
			if ( MuzzleFlashScale > 0f ) Fx.MuzzleFlash( held.Origin, held.Basis.Y, MuzzleFlashScale, null, FlashTint );
			thirdPersonMuzzle = true;
		}
		else if ( MuzzleFlashScale > 0f ) Fx.MuzzleFlash( trailStart, forward, MuzzleFlashScale, null, FlashTint );

		hitThisShot.Clear();
		var usedHeldMuzzle = thirdPersonMuzzle;
		thirdPersonMuzzle = false;
		var exclude = new Godot.Collections.Array<Rid> { controller.GetRid() };
		if ( Bubbles ) BlowBubble( controller.SeenFromOutside ? controller.EyeTransform.Origin + forward * 0.6f : trailStart, start, forward, spread, chargedShot );
		else if ( Harpoon ) FireHarpoon( start, forward, spread, trailStart );
		else if ( Lure ) ThrowLure( start, forward );
		else if ( Seeds ) PlantSeed( start, forward, false );
		else if ( Projectile )
		{
			// The trident flies from the muzzle to whatever is under the crosshair (or as far as it reaches).
			var aimDirection = (forward + Rng.InSphere( 1f ) * spread).Normalized();
			var aim = Trace.Ray( controller, start, start + aimDirection * Range, Layers.World | Layers.Creature | Layers.Gib, exclude );
			var target = aim.Hit ? aim.HitPosition : start + aimDirection * Range;
			// In third person the camera hangs behind the robot: the trident leaves from in front of the robot's eyes.
			// The check for something between the shooter and the muzzle starts at the robot's eyes then, not the camera.
			var eyes = controller.SeenFromOutside ? controller.EyeTransform.Origin : start;
			var from = controller.SeenFromOutside && !usedHeldMuzzle ? eyes + aimDirection * 0.6f : trailStart;
			TridentBolt.Launch( GetTree().CurrentScene, from, target, aimDirection, eyes, ShotDamage, controller, this, chargedShot );
			if ( chargedShot ) Fork( from, aimDirection, eyes, basis );
			if ( chargedShot ) { Sfx.Play2D( "trident_pierce", 0.5f, 1.3f, bus: Sfx.GunBus ); juice?.Shake( ShakePerShot ); }
		}
		else for ( var i = 0; i < Math.Max( 1, Pellets ); i++ )
		{
			if ( i == 0 ) BeginShotSkills();
			var direction = (forward + Rng.InSphere( 1f ) * spread).Normalized();
			// World and creatures in one ray: creature parts are areas/bodies on their own layer.
			var tr = Trace.Ray( controller, start, start + direction * Range, Layers.World | Layers.Creature | Layers.Gib, exclude );
			if ( tr.Hit ) HandleHit( tr, start, direction );
			if ( WaterFeel.IsOn() ) WaterBubble.Trail( trailStart, tr.Hit ? tr.HitPosition : start + direction * MathF.Min( Range, 900f * U.In ) );
		}

		if ( !Bubbles && !Projectile && !Harpoon && !Lure && !Seeds ) EndShotSkills( start, forward, trailStart );
		juice?.Shake( ShakePerShot );
		juice?.KickFov( FovKickPerShot );

		var recoil = Recoil * MathX.Lerp( 1f, AimRecoilScale, AimAmount );
		controller.EyeAngles += new Angles( -recoil, Rng.Float( -recoil, recoil ) * 0.4f, 0f );
	}

	void HandleHit( TraceResult tr, Vector3 start, Vector3 direction )
	{
		var shootable = Shootables.Find( tr.Node );
		// A glow bulb pops (its own burst and sound; no blood).
		if ( shootable is GlowBulb bulb )
		{
			if ( !bulb.IsDead ) { TimeSinceHit = 0f; bulb.ReceiveShot( CurrentDamage, tr.Node, direction, tr.HitPosition, start, controller, true ); }
			return;
		}
		if ( shootable is not null && shootable.ShotOwner is not null )
		{
			var firstPellet = hitThisShot.Add( shootable.ShotOwner );
			var wasAlive = !shootable.IsDead;
			if ( wasAlive ) TimeSinceHit = 0f;
			Fx.ImpactFlesh( tr.HitPosition, -direction, new Color( 0.9f, 0.3f, 0.2f ) );
			if ( firstPellet ) Sfx.Play( "hit_flesh", tr.HitPosition, 0.8f );
			shootable.ReceiveShot( CurrentDamage * HitScale( tr, start ) * (Spines ? 1f + CoralFx.SharpPer * Skill( SkillId.SharpSpines ) : 1f), tr.Node, direction, tr.HitPosition, start, controller, firstPellet );
			if ( ModelName == "rifle" ) Perks?.RifleHit( tr.Node, tr.HitPosition, overclockShot );   // Electric Bullets (always, overclocked)
			if ( firstPellet ) PelletSkills( tr, direction );
			if ( Spines && wasAlive ) SpineHit( tr, direction );
			if ( wasAlive && shootable.IsDead ) TimeSinceKill = 0f;
			return;
		}

		// Gibs: shoved, and enough damage pulverises them.
		if ( tr.Node is Gib gib )
		{
			gib.TakeHit( tr.HitPosition, direction, CurrentDamage, BulletImpulse / U.In );
			return;
		}

		// A sea mine: the shot rings off it.
		if ( tr.Node is SeaMineShell ) { if ( hitThisShot.Add( tr.Node ) ) SeaMine.Tink( tr.HitPosition, tr.Normal ); return; }

		// Loose physics props just get shoved.
		if ( tr.Node is RigidBody3D body && !body.Freeze )
		{
			body.ApplyImpulse( direction * BulletImpulse * body.Mass, tr.HitPosition - body.GlobalPosition );
			return;
		}

		Fx.ImpactWorld( tr.HitPosition, tr.Normal );
		if ( hitThisShot.Add( this ) ) Sfx.Play( "hit_world", tr.HitPosition, 0.6f );   // once per blast
	}

	public void MarkKill() => TimeSinceKill = 0f;
	public void MarkHit() => TimeSinceHit = 0f;

	void StartReload()
	{
		if ( IsReloading || Ammo >= MagazineSize || Reserve <= 0 ) return;
		IsReloading = true;
		Sfx.Play2D( "mag_out" );
		if ( ShellReload )
		{
			firstShell = Ammo == 0;
			nextShell = ShellTime * 1.2f * ShellScale;
			ViewModel?.ShellReloadBegin();
			return;
		}
		// Explosive Round: an emptied magazine is thrown, and goes off on what it hits.
		if ( Ammo == 0 && !Projectile ) Perks?.ThrowClip();
		reloadFinished = CurrentReloadTime;
		ViewModel?.Reload( CurrentReloadTime );
	}

	void UpdateShellReload()
	{
		if ( Ammo >= MagazineSize || Reserve <= 0 ) { EndShellReload(); return; }
		if ( nextShell > 0f ) return;
		Ammo++;
		Reserve--;
		ViewModel?.InsertShell();
		firstShell = false;
		nextShell = ShellTime * ShellScale;
		if ( Ammo >= MagazineSize || Reserve <= 0 ) EndShellReload();
	}

	void EndShellReload()
	{
		IsReloading = false;
		ViewModel?.ShellReloadEnd();
	}

	void CancelReload()
	{
		if ( !IsReloading ) return;
		if ( ShellReload ) EndShellReload();
		else IsReloading = false;
	}

	void FinishReload()
	{
		IsReloading = false;
		Sfx.Play2D( "mag_in" );
		var taken = Math.Min( MagazineSize - Ammo, Reserve );
		Ammo += taken;
		Reserve -= taken;
		StartOverclock();
	}
}
