/// <summary>
/// A hitscan gun: Attack1 fires, R reloads, Attack2 aims. One component per weapon on the player;
/// WeaponInventory enables one at a time. Pellets above 1 makes it a shotgun, and ShellReload loads
/// one shell at a time. Sounds and effects are set in the inspector so the scene tracks which packages it uses.
/// </summary>
public sealed class PlayerWeapon : Component
{
	[Property, Group( "Stats" )] public float Damage { get; set; } = 34f;
	[Property, Group( "Stats" )] public float FireRate { get; set; } = 8f;
	[Property, Group( "Stats" )] public float Range { get; set; } = 5000f;
	[Property, Group( "Stats" )] public float SpreadDegrees { get; set; } = 1.2f;
	[Property, Group( "Stats" )] public float Recoil { get; set; } = 0.6f;
	[Property, Group( "Stats" )] public int MagazineSize { get; set; } = 30;
	[Property, Group( "Stats" )] public int StartingReserve { get; set; } = 180;
	[Property, Group( "Stats" )] public float ReloadTime { get; set; } = 1.6f;
	/// <summary> Speed change given to loose physics objects a bullet hits. </summary>
	[Property, Group( "Stats" )] public float BulletImpulse { get; set; } = 150f;
	/// <summary> Traces per shot. Above 1 it's a shotgun; Damage is per pellet. </summary>
	[Property, Group( "Stats" )] public int Pellets { get; set; } = 1;
	/// <summary> Hold to keep firing (rifle) or click per shot (shotgun). </summary>
	[Property, Group( "Stats" )] public bool Automatic { get; set; } = true;
	/// <summary> Load one round at a time (shotgun) instead of swapping a magazine. Firing interrupts it. </summary>
	[Property, Group( "Stats" )] public bool ShellReload { get; set; } = false;
	[Property, Group( "Stats" )] public float ShellTime { get; set; } = 0.5f;

	[Property, Group( "Weapon" )] public string DisplayName { get; set; } = "Rifle";
	/// <summary> Number key that selects it, and its order when scrolling. </summary>
	[Property, Group( "Weapon" )] public int Slot { get; set; } = 1;

	[Property, Group( "Sounds" )] public SoundEvent ShootSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent MagOutSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent MagInSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent DryFireSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent HitWorldSound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent HitFleshSound { get; set; }

	[Property, Group( "Effects" )] public GameObject MuzzleFlash { get; set; }
	[Property, Group( "Effects" )] public GameObject ImpactWorld { get; set; }
	[Property, Group( "Effects" )] public GameObject ImpactFlesh { get; set; }
	[Property, Group( "Effects" )] public float MuzzleFlashScale { get; set; } = 0.3f;
	[Property, Group( "Effects" )] public float ShakePerShot { get; set; } = 0.12f;
	[Property, Group( "Effects" )] public float FovKickPerShot { get; set; } = 1.5f;

	public int Ammo { get; private set; }
	public int Reserve { get; private set; }

	/// <summary> Raised by the damage upgrade station. </summary>
	public int DamageLevel { get; set; }
	[Property, Group( "Stats" )] public float DamagePerLevel { get; set; } = 0.25f;
	public float CurrentDamage => Damage * (1f + DamagePerLevel * DamageLevel);

	/// <summary> Fills the magazine and the reserve back to their starting amounts. </summary>
	public void RefillAmmo()
	{
		ammoInitialised = true;
		Ammo = MagazineSize;
		Reserve = Math.Max( Reserve, StartingReserve );
		IsReloading = false;
	}
	public bool IsReloading { get; private set; }
	public float ReloadProgress => !IsReloading ? 0f : ShellReload ? (float)Ammo / MagazineSize : reloadFinished.Fraction;

	[Property, Group( "Aiming" )] public float AimFovScale { get; set; } = 0.7f;
	[Property, Group( "Aiming" )] public float AimSpreadScale { get; set; } = 0.15f;
	[Property, Group( "Aiming" )] public float AimRecoilScale { get; set; } = 0.55f;
	[Property, Group( "Aiming" )] public float AimMoveSpeedScale { get; set; } = 0.6f;
	[Property, Group( "Aiming" )] public float AimSpeed { get; set; } = 9f;

	/// <summary> Holding Attack2 (right mouse) and allowed to aim. </summary>
	public bool IsAiming { get; private set; }
	/// <summary> 0 = hip, 1 = fully down the sights. Smoothed, for FOV and the HUD. </summary>
	public float AimAmount { get; private set; }
	/// <summary> Testing aid: aims without holding the button (scripts can't fake mouse input). </summary>
	public bool ForceAim { get; set; }

	float baseWalkSpeed = -1f;
	float baseRunSpeed = -1f;

	void UpdateAiming()
	{
		var controller = GetComponent<PlayerController>();
		IsAiming = (Input.Down( "Attack2" ) || ForceAim) && !IsReloading;
		AimAmount = MathX.Lerp( AimAmount, IsAiming ? 1f : 0f, Time.Delta * AimSpeed );

		// Slow the player while aiming; remember the original speeds the first time through.
		if ( !controller.IsValid() ) return;
		if ( baseWalkSpeed < 0f ) { baseWalkSpeed = controller.WalkSpeed; baseRunSpeed = controller.RunSpeed; }
		// The movement kit owns the base speeds: normal run, and the Shift sprint.
		if ( GetComponent<PlayerMovement>() is { } movement )
		{
			baseRunSpeed = movement.SprintSpeed;
			baseWalkSpeed = movement.MoveSpeed;
		}
		var scale = MathX.Lerp( 1f, AimMoveSpeedScale, AimAmount );
		// Curses (MC61's slow card) slow everything down.
		var slow = GetComponent<PlayerAfflictions>()?.SpeedScale ?? 1f;
		controller.WalkSpeed = baseWalkSpeed * scale * slow;
		controller.RunSpeed = MathX.Lerp( baseRunSpeed, baseWalkSpeed * AimMoveSpeedScale, AimAmount ) * slow;
	}

	/// <summary> Time since a bullet hit something damageable. The HUD shows a hitmarker. </summary>
	public TimeSince TimeSinceHit { get; private set; } = 999f;
	/// <summary> Time since a bullet killed something. The HUD shows a red hitmarker. </summary>
	public TimeSince TimeSinceKill { get; private set; } = 999f;
	public TimeSince TimeSinceShot { get; private set; } = 999f;

	TimeUntil reloadFinished;
	bool magInPlayed;

	/// <summary> The gun as seen in hand in third person, and by other players. </summary>
	[Property, Group( "Third Person" )] public Model WorldModel { get; set; }
	/// <summary> Where the pistol grip sits in the gun model, so it lands in the hand (the model's origin isn't at the grip). </summary>
	[Property, Group( "Third Person" )] public Vector3 WorldModelGrip { get; set; } = new( -4.2f, 0f, 3f );
	/// <summary>
	/// Where the left hand (wrist bone) goes on the gun, in the gun model's space, so it holds the
	/// handguard or pump. Zero leaves the left hand to the animation.
	/// </summary>
	[Property, Group( "Third Person" )] public Vector3 WorldModelLeftHand { get; set; } = new( 8.5f, 2.2f, 3f );
	[Property, Group( "Third Person" )] public Angles WorldModelLeftHandAngles { get; set; } = new( -5.4f, -70.6f, 11.2f );
	/// <summary> Extra angle tweak for the gun in hand. </summary>
	[Property, Group( "Third Person" )] public Angles WorldModelAngles { get; set; } = Angles.Zero;
	[Property, Group( "Third Person" )] public Sandbox.Citizen.CitizenAnimationHelper.HoldTypes HoldType { get; set; } = Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.Rifle;

	PlayerAppearance Appearance => GetComponent<PlayerAppearance>();
	bool InThirdPerson => GetComponent<PlayerController>()?.ThirdPerson ?? false;

	[Property, Group( "View Model" )] public Model ViewModelGun { get; set; }
	[Property, Group( "View Model" )] public Model ViewModelArms { get; set; }

	public ViewModel ViewModel { get; private set; }

	bool ammoInitialised;

	protected override void OnStart()
	{
		if ( ammoInitialised ) return;
		ammoInitialised = true;
		Ammo = MagazineSize;
		Reserve = StartingReserve;
	}

	protected override void OnEnabled()
	{
		if ( ViewModel.IsValid() ) ViewModel.GameObject.Enabled = true;
	}

	/// <summary> Holstered: hide the gun, drop any reload, and give back the walking speed aiming took. </summary>
	protected override void OnDisabled()
	{
		if ( ViewModel.IsValid() ) ViewModel.GameObject.Enabled = false;
		CancelReload();

		var controller = GetComponent<PlayerController>();
		if ( controller.IsValid() && baseWalkSpeed >= 0f )
		{
			controller.WalkSpeed = baseWalkSpeed;
			controller.RunSpeed = baseRunSpeed;
		}
		IsAiming = false;
		AimAmount = 0f;
	}

	/// <summary> The camera isn't always ready when we start, so build the view model on demand. </summary>
	void EnsureViewModel()
	{
		if ( ViewModel.IsValid() || IsProxy || ViewModelGun is null ) return;
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;
		ViewModel = ViewModel.Create( camera.GameObject, ViewModelGun, ViewModelArms, GetComponent<PlayerController>(), this );
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		EnsureViewModel();

		var survivor = GetComponent<PlayerSurvivor>();
		if ( survivor.IsValid() && !survivor.IsAlive )
		{
			if ( ViewModel.IsValid() ) ViewModel.GameObject.Enabled = false;
			return;
		}

		// The first-person gun only shows in first person (and comes back after a respawn).
		if ( ViewModel.IsValid() ) ViewModel.GameObject.Enabled = !InThirdPerson;

		if ( IsReloading && ShellReload )
		{
			UpdateShellReload();
		}
		else if ( IsReloading )
		{
			// Mag in plays a bit before the reload ends, so the sound lines up with ammo appearing.
			if ( !magInPlayed && reloadFinished.Fraction > 0.55f )
			{
				magInPlayed = true;
				PlayLocal( MagInSound );
			}

			if ( reloadFinished ) FinishReload();
		}

		UpdateAiming();

		if ( Input.Pressed( "Reload" ) )
			StartReload();

		// Firing interrupts a shell-by-shell reload, as long as there's something loaded.
		if ( ShellReload && IsReloading && Input.Pressed( "Attack1" ) && Ammo > 0 )
			CancelReload();

		var wantsFire = Automatic ? Input.Down( "Attack1" ) : Input.Pressed( "Attack1" );
		if ( wantsFire && !IsReloading && TimeSinceShot >= 1f / FireRate )
		{
			if ( Ammo > 0 )
			{
				Shoot();
			}
			else if ( Reserve > 0 )
			{
				StartReload();
			}
			else if ( Input.Pressed( "Attack1" ) )
			{
				PlayLocal( DryFireSound );
				ViewModel?.DryFire();
			}
		}
	}

	public void AddAmmo( int amount ) => Reserve += amount;

	void Shoot()
	{
		TimeSinceShot = 0f;
		Ammo--;

		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;

		var camRot = camera.WorldRotation;
		var start = camera.WorldPosition;
		var spread = MathF.Tan( (SpreadDegrees * MathX.Lerp( 1f, AimSpreadScale, AimAmount )).DegreeToRadian() );
		var muzzle = start + camRot.Forward * 24f + camRot.Down * 7f + camRot.Right * 7f;

		var shot = PlayLocal( ShootSound );
		if ( shot.IsValid() ) shot.Pitch = Game.Random.Float( 0.95f, 1.05f );
		ShotEffectsForOthers();

		// With a view model, the flash comes out of the real barrel and rides along with the gun.
		GameObject flash = null;
		if ( InThirdPerson && Appearance.IsValid() )
		{
			Appearance.PlayFire( this, MuzzleFlash, MuzzleFlashScale * 2f );
		}
		else if ( ViewModel.IsValid() )
		{
			ViewModel.Fire();
			var barrel = ViewModel.Muzzle();
			flash = SpawnEffect( MuzzleFlash, barrel.Position, barrel.Rotation, ViewModel.GameObject );
		}
		else
		{
			flash = SpawnEffect( MuzzleFlash, muzzle, camRot, camera.GameObject );
		}
		if ( flash.IsValid() ) flash.WorldScale = MuzzleFlashScale;

		// Each pellet traces on its own. Hitboxes give the exact body part; the zombie's movement capsule
		// is skipped so it can't eat shots.
		zombiesHitThisShot.Clear();
		for ( var i = 0; i < Math.Max( 1, Pellets ); i++ )
		{
			var direction = (camRot.Forward + Vector3.Random * spread).Normal;
			var tr = Scene.Trace.Ray( start, start + direction * Range )
				.IgnoreGameObjectHierarchy( GameObject )
				.UseHitboxes()
				.WithoutTags( "zombieblocker" )
				.Run();

			// Floating creatures are triggers (so nothing snags on them): look for them separately
			// and take whichever is nearer.
			var creature = Scene.Trace.Ray( start, start + direction * Range )
				.HitTriggersOnly()
				.WithTag( "creature" )
				.Run();
			if ( creature.Hit && (!tr.Hit || creature.Distance < tr.Distance) ) tr = creature;

			if ( tr.Hit ) HandleHit( tr, start, direction );
		}

		var juice = GetComponent<CameraJuice>();
		juice?.Shake( ShakePerShot );
		juice?.KickFov( FovKickPerShot );

		var controller = GetComponent<PlayerController>();
		if ( controller.IsValid() )
		{
			var recoil = Recoil * MathX.Lerp( 1f, AimRecoilScale, AimAmount );
			controller.EyeAngles += new Angles( -recoil, Game.Random.Float( -recoil, recoil ) * 0.4f, 0f );
		}
	}

	void HandleHit( SceneTraceResult tr, Vector3 start, Vector3 direction )
	{
		var facing = Rotation.LookAt( tr.Normal );
		var zombie = tr.GameObject.IsValid() ? tr.GameObject.Components.Get<Zombie>( FindMode.EverythingInSelfAndAncestors ) : null;

		if ( zombie.IsValid() )
		{
			HitZombie( zombie, tr, start, direction );
			return;
		}

		// Creatures that handle bullets themselves (Bloober segments...).
		var shootable = tr.GameObject.IsValid() ? tr.GameObject.Components.Get<IShootable>( FindMode.EverythingInSelfAndAncestors ) : null;
		if ( shootable is not null && shootable.ShotOwner.IsValid() )
		{
			var firstPellet = zombiesHitThisShot.Add( shootable.ShotOwner );
			if ( firstPellet ) PlayAt( HitFleshSound, tr.HitPosition );
			if ( !shootable.IsDead ) TimeSinceHit = 0f;
			shootable.ReceiveShot( CurrentDamage, tr.GameObject, direction, tr.HitPosition, start, GetComponent<PlayerSurvivor>(), scoreHit: firstPellet );
			return;
		}

		// Gibs: shove them, and enough damage splits or pulverises them.
		var gib = tr.GameObject.IsValid() ? tr.GameObject.Components.Get<Gib>( FindMode.EverythingInSelfAndAncestors ) : null;
		if ( gib.IsValid() )
		{
			PlayAt( HitFleshSound, tr.HitPosition );
			gib.TakeHit( tr.HitPosition, direction, CurrentDamage, BulletImpulse );
			return;
		}

		// Other loose physics props just get shoved.
		if ( tr.Body is { } hitBody && hitBody.MotionEnabled )
			hitBody.ApplyImpulseAt( tr.HitPosition, direction * BulletImpulse * hitBody.Mass );

		var damageables = tr.GameObject.IsValid()
			? tr.GameObject.Components.GetAll<Component.IDamageable>( FindMode.EverythingInSelfAndAncestors ).ToList()
			: new List<Component.IDamageable>();

		if ( damageables.Count == 0 )
		{
			SpawnEffect( ImpactWorld, tr.HitPosition, facing );
			PlayAt( HitWorldSound, tr.HitPosition );
			return;
		}

		SpawnEffect( ImpactFlesh, tr.HitPosition, Rotation.LookAt( -direction ) );
		PlayAt( HitFleshSound, tr.HitPosition );

		var damage = new DamageInfo
		{
			Damage = CurrentDamage,
			Attacker = GameObject,
			Weapon = GameObject,
			Position = tr.HitPosition,
			Origin = start,
		};

		foreach ( var target in damageables )
		{
			var health = target as Health;
			var wasAlive = health is null || !health.IsDead;
			target.OnDamage( damage );
			if ( !wasAlive ) continue;

			var survivor = GetComponent<PlayerSurvivor>();
			var killed = health is not null && health.IsDead;
			if ( killed ) TimeSinceKill = 0f;
			survivor?.AddPoints( killed ? survivor.PointsPerKill : survivor.PointsPerHit );
		}

		TimeSinceHit = 0f;
	}

	/// <summary> Enemies already hit by this shot: a shotgun blast plays one hit sound and scores once per enemy. </summary>
	readonly HashSet<Component> zombiesHitThisShot = new();

	void HitZombie( Zombie zombie, SceneTraceResult tr, Vector3 start, Vector3 direction )
	{
		var firstPellet = zombiesHitThisShot.Add( zombie );
		SpawnEffect( ImpactFlesh, tr.HitPosition, Rotation.LookAt( -direction ) );
		if ( firstPellet ) PlayAt( HitFleshSound, tr.HitPosition );

		// Hitbox bone while alive; on a ragdoll each body is a GameObject named after its bone.
		var boneName = tr.Hitbox?.Bone?.Name;
		if ( string.IsNullOrEmpty( boneName ) && tr.GameObject != zombie.GameObject ) boneName = tr.GameObject.Name;
		if ( string.IsNullOrEmpty( boneName ) )
			boneName = zombie.GetComponent<SkinnedModelRenderer>()?.GetClosestBone( tr.HitPosition )?.Name;

		// The hitmarker shows straight away; the host applies the damage and sends back points and kills.
		if ( !zombie.IsDead ) TimeSinceHit = 0f;
		zombie.ReceiveBullet( CurrentDamage, boneName, direction, tr.HitPosition, start, GetComponent<PlayerSurvivor>(), scoreHit: firstPellet );
	}

	/// <summary> Testing aid: fires one shot as if the trigger was pulled (scripts can't fake input). </summary>
	public void TestFire() => Shoot();

	/// <summary> Called back from the host when one of our bullets killed something. </summary>
	public void MarkKill() => TimeSinceKill = 0f;

	/// <summary> Other players hear our shots and see the flash from roughly where we stand. </summary>
	[Rpc.Broadcast( NetFlags.Unreliable )]
	void ShotEffectsForOthers()
	{
		if ( !IsProxy ) return;
		var at = WorldPosition + Vector3.Up * 60f;
		if ( ShootSound is not null ) Sound.Play( ShootSound, at );
		Appearance?.PlayFire( this, MuzzleFlash, MuzzleFlashScale * 2f );
	}

	/// <summary> Other players see us reload; we see it ourselves in third person. </summary>
	[Rpc.Broadcast]
	void ReloadEffectsEverywhere()
	{
		if ( IsProxy || InThirdPerson ) Appearance?.PlayReload();
	}

	void StartReload()
	{
		if ( IsReloading || Ammo >= MagazineSize || Reserve <= 0 ) return;
		IsReloading = true;
		ReloadEffectsEverywhere();

		if ( ShellReload )
		{
			firstShell = Ammo == 0;
			nextShell = ShellTime * 1.2f;
			ViewModel?.ShellReloadBegin();
			return;
		}

		magInPlayed = false;
		reloadFinished = ReloadTime;
		PlayLocal( MagOutSound );
		ViewModel?.Reload( ReloadTime );
	}

	TimeUntil nextShell;
	bool firstShell;

	/// <summary> Loads one shell per ShellTime until full, out of reserve, or interrupted by firing. </summary>
	void UpdateShellReload()
	{
		if ( Ammo >= MagazineSize || Reserve <= 0 ) { EndShellReload(); return; }
		if ( !nextShell ) return;

		Ammo++;
		Reserve--;
		PlayLocal( MagInSound );
		ViewModel?.InsertShell( firstShell );
		firstShell = false;
		nextShell = ShellTime;

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
		var taken = Math.Min( MagazineSize - Ammo, Reserve );
		Ammo += taken;
		Reserve -= taken;
	}

	SoundHandle PlayLocal( SoundEvent sound )
	{
		if ( sound is null ) return default;
		var camera = Scene.Camera;
		return Sound.Play( sound, camera.IsValid() ? camera.WorldPosition : WorldPosition );
	}

	static void PlayAt( SoundEvent sound, Vector3 position )
	{
		if ( sound is not null ) Sound.Play( sound, position );
	}

	static GameObject SpawnEffect( GameObject prefab, Vector3 position, Rotation rotation, GameObject parent = null )
	{
		if ( !prefab.IsValid() ) return null;
		var fx = prefab.Clone( position, rotation );
		if ( parent.IsValid() ) fx.SetParent( parent, true );
		fx.AddComponent<SelfDestruct>().Lifetime = 2f;
		return fx;
	}
}
