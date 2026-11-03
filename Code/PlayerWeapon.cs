/// <summary>
/// A hitscan rifle: hold Attack1 to fire, R to reload.
/// Sounds and effects are set in the inspector so the scene tracks which packages it uses.
/// </summary>
public sealed class PlayerWeapon : Component
{
	[Property, Group( "Stats" )] public float Damage { get; set; } = 20f;
	[Property, Group( "Stats" )] public float FireRate { get; set; } = 8f;
	[Property, Group( "Stats" )] public float Range { get; set; } = 5000f;
	[Property, Group( "Stats" )] public float SpreadDegrees { get; set; } = 1.2f;
	[Property, Group( "Stats" )] public float Recoil { get; set; } = 0.6f;
	[Property, Group( "Stats" )] public int MagazineSize { get; set; } = 30;
	[Property, Group( "Stats" )] public int StartingReserve { get; set; } = 180;
	[Property, Group( "Stats" )] public float ReloadTime { get; set; } = 1.6f;
	/// <summary> Speed change given to loose physics objects a bullet hits. </summary>
	[Property, Group( "Stats" )] public float BulletImpulse { get; set; } = 150f;

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
		Ammo = MagazineSize;
		Reserve = Math.Max( Reserve, StartingReserve );
		IsReloading = false;
	}
	public bool IsReloading { get; private set; }
	public float ReloadProgress => IsReloading ? reloadFinished.Fraction : 0f;

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
		var scale = MathX.Lerp( 1f, AimMoveSpeedScale, AimAmount );
		controller.WalkSpeed = baseWalkSpeed * scale;
		controller.RunSpeed = MathX.Lerp( baseRunSpeed, baseWalkSpeed * AimMoveSpeedScale, AimAmount );
	}

	/// <summary> Time since a bullet hit something damageable. The HUD shows a hitmarker. </summary>
	public TimeSince TimeSinceHit { get; private set; } = 999f;
	/// <summary> Time since a bullet killed something. The HUD shows a red hitmarker. </summary>
	public TimeSince TimeSinceKill { get; private set; } = 999f;
	public TimeSince TimeSinceShot { get; private set; } = 999f;

	TimeUntil reloadFinished;
	bool magInPlayed;

	[Property, Group( "View Model" )] public Model ViewModelGun { get; set; }
	[Property, Group( "View Model" )] public Model ViewModelArms { get; set; }

	public ViewModel ViewModel { get; private set; }

	protected override void OnStart()
	{
		Ammo = MagazineSize;
		Reserve = StartingReserve;
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

		if ( IsReloading )
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

		if ( Input.Down( "Attack1" ) && !IsReloading && TimeSinceShot >= 1f / FireRate )
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
		var direction = (camRot.Forward + Vector3.Random * spread).Normal;
		var end = start + direction * Range;
		var muzzle = start + camRot.Forward * 24f + camRot.Down * 7f + camRot.Right * 7f;

		var shot = PlayLocal( ShootSound );
		if ( shot.IsValid() ) shot.Pitch = Game.Random.Float( 0.95f, 1.05f );

		// With a view model, the flash comes out of the real barrel and rides along with the gun.
		GameObject flash;
		if ( ViewModel.IsValid() )
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

		// Hitboxes give us the exact body part; the zombie's movement capsule is skipped so it can't eat shots.
		var tr = Scene.Trace.Ray( start, end )
			.IgnoreGameObjectHierarchy( GameObject )
			.UseHitboxes()
			.WithoutTags( "zombieblocker" )
			.Run();

		if ( tr.Hit ) HandleHit( tr, start, direction );

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

	void HitZombie( Zombie zombie, SceneTraceResult tr, Vector3 start, Vector3 direction )
	{
		SpawnEffect( ImpactFlesh, tr.HitPosition, Rotation.LookAt( -direction ) );
		PlayAt( HitFleshSound, tr.HitPosition );

		// Hitbox bone while alive; on a ragdoll each body is a GameObject named after its bone.
		var boneName = tr.Hitbox?.Bone?.Name;
		if ( string.IsNullOrEmpty( boneName ) && tr.GameObject != zombie.GameObject ) boneName = tr.GameObject.Name;
		if ( string.IsNullOrEmpty( boneName ) )
			boneName = zombie.GetComponent<SkinnedModelRenderer>()?.GetClosestBone( tr.HitPosition )?.Name;

		var wasAlive = !zombie.IsDead;
		var damage = new DamageInfo
		{
			Damage = CurrentDamage,
			Attacker = GameObject,
			Weapon = GameObject,
			Position = tr.HitPosition,
			Origin = start,
		};

		var killed = zombie.TakeBullet( damage, boneName, direction );
		if ( !wasAlive ) return; // Shooting corpses is for fun, not points.

		TimeSinceHit = 0f;
		var survivor = GetComponent<PlayerSurvivor>();
		if ( killed )
		{
			TimeSinceKill = 0f;
			var headshot = zombie.LastHitSegment == Segment.Head;
			survivor?.AddPoints( headshot ? survivor.PointsPerKill * 3 / 2 : survivor.PointsPerKill );
		}
		else
		{
			survivor?.AddPoints( survivor.PointsPerHit );
		}
	}

	void StartReload()
	{
		if ( IsReloading || Ammo >= MagazineSize || Reserve <= 0 ) return;
		IsReloading = true;
		magInPlayed = false;
		reloadFinished = ReloadTime;
		PlayLocal( MagOutSound );
		ViewModel?.Reload( ReloadTime );
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
