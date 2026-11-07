/// <summary>
/// Player side of the survival game: regenerating health, and locking controls on death.
/// Sits next to the PlayerController.
/// </summary>
public sealed class PlayerSurvivor : Component
{
	[Property] public float RegenDelay { get; set; } = 4f;
	[Property] public float RegenPerSecond { get; set; } = 20f;
	[Property, Group( "Juice" )] public SoundEvent HurtSound { get; set; }
	[Property, Group( "Juice" )] public SoundEvent DeathSound { get; set; }
	[Property, Group( "Juice" )] public float HurtShake { get; set; } = 0.5f;

	[RequireComponent] public Health Health { get; set; }
	public PlayerController Controller => GetComponent<PlayerController>();
	/// <summary> The gun in hand (the inventory's current one, falling back to the first PlayerWeapon). </summary>
	public PlayerWeapon Weapon
	{
		get
		{
			var current = GetComponent<WeaponInventory>()?.Current;
			return current.IsValid() ? current : GetComponent<PlayerWeapon>();
		}
	}
	public bool IsAlive => Health.IsValid() && !Health.IsDead;

	[Property, Group( "Points" )] public int StartingPoints { get; set; } = 500;
	[Property, Group( "Points" )] public int PointsPerHit { get; set; } = 10;
	[Property, Group( "Points" )] public int PointsPerKill { get; set; } = 60;

	/// <summary> Currency for buy stations. </summary>
	public int Points { get; private set; }
	/// <summary> The most recent gain, for the HUD's "+60" popup. </summary>
	public int LastGain { get; private set; }
	public TimeSince TimeSinceGain { get; private set; } = 999f;
	public TimeSince TimeSinceDenied { get; private set; } = 999f;

	public void AddPoints( int amount )
	{
		if ( amount <= 0 ) return;
		Points += amount;

		// Gains within a moment of each other stack into one popup.
		LastGain = TimeSinceGain < 0.6f ? LastGain + amount : amount;
		TimeSinceGain = 0f;
	}

	/// <summary> Spends points if there are enough. Returns false (and flashes the counter) if not. </summary>
	public bool TrySpend( int cost )
	{
		if ( Points < cost )
		{
			TimeSinceDenied = 0f;
			return false;
		}

		Points -= cost;
		return true;
	}

	protected override void OnAwake()
	{
		Points = StartingPoints;
	}

	float startingMaxHealth = 100f;

	protected override void OnStart()
	{
		startingMaxHealth = Health.MaxHealth;
		Health.Damaged += OnHurt;
		Health.Died += OnDied;
	}

	// ---- Networked rewards and respawning ----------------------------------------------------
	// The host decides hits, kills and wave bonuses; these run on the player's own machine,
	// where their points and HUD live.

	/// <summary> Host -> this player: your bullet hit (or killed) a zombie. </summary>
	[Rpc.Owner]
	public void RewardHit( bool killed, bool headshot, bool scoreHit )
	{
		if ( killed )
		{
			Weapon?.MarkKill();
			AddPoints( headshot ? PointsPerKill * 3 / 2 : PointsPerKill );
		}
		else if ( scoreHit )
		{
			AddPoints( PointsPerHit );
		}
	}

	/// <summary> Host -> this player: bonus points (wave cleared). </summary>
	[Rpc.Owner]
	public void RewardPoints( int amount ) => AddPoints( amount );

	/// <summary>
	/// Host -> this player: come back. A full reset (new game) also resets points, upgrades and max health;
	/// otherwise (respawning between waves) you keep what you've earned.
	/// </summary>
	[Rpc.Owner]
	public void Respawn( Vector3 position, bool fullReset )
	{
		if ( fullReset )
		{
			Points = StartingPoints;
			Health.ResetTo( startingMaxHealth );
		}
		else
		{
			Health.ResetTo( Health.MaxHealth );
		}

		GetComponent<PlayerAfflictions>()?.Clear();
		GetComponent<PlayerMovement>()?.ResetStamina();

		foreach ( var weapon in Components.GetAll<PlayerWeapon>( FindMode.EverythingInSelf ) )
		{
			if ( fullReset ) weapon.DamageLevel = 0;
			weapon.RefillAmmo();
		}

		var controller = Controller;
		if ( controller.IsValid() )
		{
			controller.UseInputControls = true;
			controller.UseLookControls = true;
			if ( controller.Body.IsValid() ) controller.Body.Velocity = Vector3.Zero;
		}
		WorldPosition = position;
	}

	void OnHurt( DamageInfo damage )
	{
		if ( IsProxy ) return;

		// Our own afflictions ticking (venom): just a nudge, no sound or view kick every tick.
		if ( damage.Attacker == GameObject )
		{
			GetComponent<CameraJuice>()?.Shake( 0.08f );
			return;
		}

		GetComponent<CameraJuice>()?.Shake( HurtShake );
		if ( HurtSound is not null ) Sound.Play( HurtSound, damage.Position );

		// Knock the view away from whoever hit us.
		var controller = Controller;
		if ( controller.IsValid() )
			controller.EyeAngles += new Angles( -3f, Game.Random.Float( -4f, 4f ), 0f );
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || !IsAlive ) return;

		if ( Health.TimeSinceDamaged > RegenDelay )
			Health.Heal( RegenPerSecond * Time.Delta );
	}

	void OnDied()
	{
		var controller = Controller;
		if ( controller.IsValid() )
		{
			controller.UseInputControls = false;
			controller.UseLookControls = false;
			controller.WishVelocity = Vector3.Zero;
		}

		if ( DeathSound is not null ) Sound.Play( DeathSound, WorldPosition + Vector3.Up * 60f );
		GetComponent<CameraJuice>()?.Shake( 1f );
	}

	public static PlayerSurvivor FindNearestAlive( Scene scene, Vector3 position )
	{
		PlayerSurvivor best = null;
		var bestDist = float.MaxValue;

		foreach ( var player in scene.GetAllComponents<PlayerSurvivor>() )
		{
			if ( !player.IsAlive ) continue;
			var dist = player.WorldPosition.DistanceSquared( position );
			if ( dist < bestDist )
			{
				bestDist = dist;
				best = player;
			}
		}

		return best;
	}
}
