using Godot;

/// <summary>
/// Port of s&box PlayerSurvivor: the player's health (regenerating after a pause), points for hits and kills,
/// hurt reactions, death and respawning. Child of the PlayerController; finds its siblings by name.
/// </summary>
public partial class PlayerSurvivor : Node
{
	public float RegenDelay = 4f;
	public float RegenPerSecond = 20f;
	public float HurtShake = 0.5f;
	public int StartingPoints = 500;
	public int PointsPerHit = 10;
	public int PointsPerKill = 60;

	public static readonly List<PlayerSurvivor> All = new();

	public PlayerController Controller { get; private set; }
	public Health Health { get; private set; }
	public CameraJuice Juice => Controller.GetNodeOrNull<CameraJuice>( "CameraJuice" );
	public PlayerAfflictions Afflictions => Controller.GetNodeOrNull<PlayerAfflictions>( "PlayerAfflictions" );
	public PlayerWeapon Weapon => Controller.GetNodeOrNull<WeaponInventory>( "WeaponInventory" )?.Current;
	public bool IsAlive => Health is not null && !Health.IsDead;
	public Vector3 WorldPosition => Controller.GlobalPosition;

	public int Points { get; private set; }
	public int LastGain { get; private set; }
	public TimeSince TimeSinceGain { get; private set; } = 999f;
	public TimeSince TimeSinceDenied { get; private set; } = 999f;
	float startingMaxHealth = 100f;

	public override void _Ready()
	{
		Name = "PlayerSurvivor";
		Controller = GetParent<PlayerController>();
		Points = StartingPoints;
		Health = Controller.GetNodeOrNull<Health>( "Health" );
		if ( Health is null ) { Health = new Health(); Controller.AddChild( Health ); }
		startingMaxHealth = Health.MaxHealth;
		Health.Damaged += OnHurt;
		Health.Died += OnDied;
		if ( Afflictions is null ) Controller.AddChild( new PlayerAfflictions() );
		All.Add( this );
	}

	public override void _ExitTree() => All.Remove( this );

	public void AddPoints( int amount )
	{
		if ( amount <= 0 ) return;
		Points += amount;
		LastGain = TimeSinceGain < 0.6f ? LastGain + amount : amount;
		TimeSinceGain = 0f;
	}

	public bool TrySpend( int cost )
	{
		if ( Points < cost ) { TimeSinceDenied = 0f; return false; }
		Points -= cost;
		return true;
	}

	public void RewardHit( bool killed, bool headshot, bool scoreHit )
	{
		if ( killed )
		{
			Weapon?.MarkKill();
			AddPoints( headshot ? PointsPerKill * 3 / 2 : PointsPerKill );
		}
		else if ( scoreHit ) AddPoints( PointsPerHit );
	}

	public void RewardPoints( int amount ) => AddPoints( amount );

	public void Respawn( Vector3 position, bool fullReset )
	{
		if ( fullReset ) { Points = StartingPoints; Health.ResetTo( startingMaxHealth ); }
		else Health.ResetTo( Health.MaxHealth );
		Afflictions?.Clear();
		Controller.GetNodeOrNull<PlayerMovement>( "PlayerMovement" )?.ResetStamina();
		foreach ( var weapon in Controller.GetChildren().OfType<PlayerWeapon>() )
		{
			if ( fullReset ) weapon.DamageLevel = 0;
			weapon.RefillAmmo();
		}
		Controller.UseInputControls = true;
		Controller.UseLookControls = true;
		Controller.Velocity = Vector3.Zero;
		Controller.GlobalPosition = position;
	}

	void OnHurt( DamageInfo damage )
	{
		if ( damage.Attacker is not PlayerAfflictions ) Sfx.Play( "hurt", damage.Position );
		// Our own afflictions ticking (venom): just a nudge.
		if ( damage.Attacker == this || damage.Attacker == Controller || damage.Attacker is PlayerAfflictions ) { Juice?.Shake( 0.08f ); return; }
		Juice?.Shake( HurtShake );
		Controller.EyeAngles += new Angles( -3f, Rng.Float( -4f, 4f ), 0f );
	}

	TimeSince timeSinceHeartbeat;

	public override void _Process( double delta )
	{
		if ( !IsAlive ) return;
		if ( Health.TimeSinceDamaged > RegenDelay ) Health.Heal( RegenPerSecond * (float)delta );
		// A heartbeat when nearly dead.
		if ( Health.Fraction < 0.25f && timeSinceHeartbeat > 1.1f )
		{
			timeSinceHeartbeat = 0f;
			Sfx.Play2D( "low_health", 0.55f );
		}
	}

	void OnDied()
	{
		Sfx.Play( "death", Controller.GlobalPosition + Vector3.Up * 60f * U.In );
		Controller.UseInputControls = false;
		Controller.UseLookControls = false;
		Juice?.Shake( 1f );
	}

	public static PlayerSurvivor FindNearestAlive( Vector3 position )
	{
		PlayerSurvivor best = null;
		var bestDist = float.MaxValue;
		foreach ( var player in All )
		{
			if ( !player.IsAlive ) continue;
			var dist = player.WorldPosition.DistanceSquaredTo( position );
			if ( dist < bestDist ) { bestDist = dist; best = player; }
		}
		return best;
	}
}

/// <summary>
/// Port of s&box PlayerAfflictions: venom from repeated bites (damage over time), ink blots over the view, MC61's
/// curses (burn, poison, slow, ink), and goo splatted on the camera by close kills.
/// </summary>
public partial class PlayerAfflictions : Node
{
	public int VenomBites = 2;
	public float VenomWindow = 5f;
	public float VenomDuration = 6f;
	public float VenomDamagePerSecond = 5f;
	public int InkHits = 2;
	public float InkWindow = 4f;
	public float InkDuration = 3.5f;
	public int InkBlots = 4;
	public float BurnDuration = 4f;
	public float BurnDamagePerSecond = 8f;
	public float SlowDuration = 3.5f;
	public float SlowSpeed = 0.45f;

	public readonly record struct Blot( float X, float Y, float Size, int Seed );
	public readonly record struct ScreenDrip( float X, float Y, float Size, Color Color, float Born, float Slide );
	public const float DripLife = 1.8f;

	public float InkLeft { get; private set; }
	public float BurnLeft { get; private set; }
	public float SlowLeft { get; private set; }
	public float VenomLeft { get; private set; }
	public bool Venomed { get; private set; }
	public float SpeedScale => SlowLeft > 0f ? SlowSpeed : 1f;
	public TimeSince TimeSinceVenomed { get; private set; } = 999f;
	public TimeSince TimeSinceCursed { get; private set; } = 999f;
	public IReadOnlyList<Blot> Blots => blots;
	public IReadOnlyList<ScreenDrip> Drips => drips;

	readonly List<Blot> blots = new();
	readonly List<ScreenDrip> drips = new();
	readonly List<float> recentInk = new();
	readonly List<float> recentBites = new();
	float tickTimer;

	Health Health => GetParent().GetNodeOrNull<Health>( "Health" );
	CameraJuice Juice => GetParent().GetNodeOrNull<CameraJuice>( "CameraJuice" );

	public override void _Ready() => Name = "PlayerAfflictions";

	public void Splattered( Color color, int count )
	{
		var now = (float)Clock.Now;
		for ( var i = 0; i < count; i++ )
			drips.Add( new ScreenDrip( Rng.Float( 12f, 88f ), Rng.Float( 8f, 70f ), Rng.Float( 40f, 140f ), color, now - Rng.Float( 0f, 0.1f ), Rng.Float( 6f, 18f ) ) );
		while ( drips.Count > 24 ) drips.RemoveAt( 0 );
	}

	public void Bitten()
	{
		if ( Health is null || Health.IsDead ) return;
		var now = (float)Clock.Now;
		recentBites.RemoveAll( t => now - t > VenomWindow );
		recentBites.Add( now );
		if ( VenomLeft > 0f || recentBites.Count >= VenomBites )
		{
			recentBites.Clear();
			VenomLeft = VenomDuration;
			TimeSinceVenomed = 0f;
			Juice?.Shake( 0.35f );
		}
	}

	public void Inked()
	{
		if ( Health is null || Health.IsDead ) return;
		var now = (float)Clock.Now;
		recentInk.RemoveAll( t => now - t > InkWindow );
		recentInk.Add( now );
		if ( recentInk.Count < InkHits ) return;
		recentInk.Clear();
		SplatInk();
	}

	void SplatInk()
	{
		InkLeft = InkDuration;
		blots.Clear();
		var start = Rng.Float( 0f, 360f );
		for ( var i = 0; i < InkBlots; i++ )
		{
			var angle = MathX.Rad( start + i * 360f / InkBlots + Rng.Float( -25f, 25f ) );
			var reach = Rng.Float( 26f, 44f );
			blots.Add( new Blot( 50f + MathF.Cos( angle ) * reach * 0.95f, 50f + MathF.Sin( angle ) * reach, Rng.Float( 170f, 300f ), Rng.Int( 0, 100000 ) ) );
		}
		Juice?.Shake( 0.3f );
	}

	public enum Curse { Burn, Poison, Slow, Ink }

	public void Cursed( Curse kind )
	{
		if ( Health is null || Health.IsDead ) return;
		TimeSinceCursed = 0f;
		switch ( kind )
		{
			case Curse.Burn: BurnLeft = BurnDuration; break;
			case Curse.Poison: VenomLeft = VenomDuration; TimeSinceVenomed = 0f; break;
			case Curse.Slow: SlowLeft = SlowDuration; break;
			case Curse.Ink: SplatInk(); break;
		}
		Juice?.Shake( 0.25f );
	}

	public void Clear()
	{
		BurnLeft = SlowLeft = VenomLeft = InkLeft = 0f;
		blots.Clear();
		recentInk.Clear();
		recentBites.Clear();
		Venomed = false;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var now = (float)Clock.Now;
		drips.RemoveAll( d => now - d.Born > DripLife );
		var health = Health;
		if ( health is null || health.IsDead )
		{
			if ( VenomLeft > 0f || BurnLeft > 0f || SlowLeft > 0f || InkLeft > 0f || Venomed ) Clear();
			return;
		}
		if ( InkLeft > 0f ) InkLeft = MathF.Max( 0f, InkLeft - dt );
		if ( SlowLeft > 0f ) SlowLeft = MathF.Max( 0f, SlowLeft - dt );
		if ( BurnLeft > 0f ) BurnLeft = MathF.Max( 0f, BurnLeft - dt );
		var dps = (VenomLeft > 0f ? VenomDamagePerSecond : 0f) + (BurnLeft > 0f ? BurnDamagePerSecond : 0f);
		if ( dps > 0f )
		{
			if ( VenomLeft > 0f ) VenomLeft = MathF.Max( 0f, VenomLeft - dt );
			tickTimer += dt;
			if ( tickTimer >= 0.5f )
			{
				tickTimer -= 0.5f;
				health.OnDamage( new DamageInfo { Damage = dps * 0.5f, Attacker = this } );
			}
		}
		else tickTimer = 0f;
		Venomed = VenomLeft > 0f;
	}
}
