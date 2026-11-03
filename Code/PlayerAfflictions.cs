/// <summary>
/// Lingering effects enemies put on a player. For now: venom from Bloober bites (a few bites close
/// together poison you, and the poison ticks your health down and stops regeneration).
/// Burn, slow and the rest of MC61's cards can live here later.
///
/// Networking: runs on the player's own machine, where their health lives. Enemies on the host call
/// the [Rpc.Owner] methods; Venomed is synced so everyone could show it.
/// </summary>
public sealed class PlayerAfflictions : Component
{
	/// <summary> Bites within the window it takes to get poisoned. </summary>
	[Property, Group( "Venom" )] public int VenomBites { get; set; } = 2;
	[Property, Group( "Venom" )] public float VenomWindow { get; set; } = 5f;
	[Property, Group( "Venom" )] public float VenomDuration { get; set; } = 6f;
	[Property, Group( "Venom" )] public float VenomDamagePerSecond { get; set; } = 5f;

	[Sync] public bool Venomed { get; set; }
	/// <summary> Seconds of venom left (on the owner). </summary>
	public float VenomLeft { get; private set; }
	/// <summary> How recently venom kicked in or was topped up, for the HUD's pulse. </summary>
	public TimeSince TimeSinceVenomed { get; private set; } = 999f;

	readonly List<float> recentBites = new();
	float tickTimer;

	Health Health => GetComponent<Health>();

	/// <summary> Enemy (host) -> this player: you got bitten by something venomous. </summary>
	[Rpc.Owner]
	public void Bitten()
	{
		if ( Health is null || Health.IsDead ) return;

		var now = Time.Now;
		recentBites.RemoveAll( t => now - t > VenomWindow );
		recentBites.Add( now );

		// Already poisoned: every bite tops it back up.
		if ( VenomLeft > 0f || recentBites.Count >= VenomBites )
		{
			recentBites.Clear();
			VenomLeft = VenomDuration;
			TimeSinceVenomed = 0f;
			GetComponent<CameraJuice>()?.Shake( 0.35f );
		}
	}

	/// <summary> Wipes everything (respawning). </summary>
	public void Clear()
	{
		VenomLeft = 0f;
		recentBites.Clear();
		if ( !IsProxy ) Venomed = false;
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		var health = Health;
		if ( health is null || health.IsDead )
		{
			if ( VenomLeft > 0f || Venomed ) Clear();
			return;
		}

		if ( VenomLeft > 0f )
		{
			VenomLeft = MathF.Max( 0f, VenomLeft - Time.Delta );

			// Tick twice a second. Self-inflicted, so the player's hurt reaction stays small.
			tickTimer += Time.Delta;
			if ( tickTimer >= 0.5f )
			{
				tickTimer -= 0.5f;
				health.OnDamage( new DamageInfo
				{
					Damage = VenomDamagePerSecond * 0.5f,
					Attacker = GameObject,
					Position = WorldPosition + Vector3.Up * 50f,
					Origin = WorldPosition,
				} );
			}
		}
		else
		{
			tickTimer = 0f;
		}

		var venomed = VenomLeft > 0f;
		if ( Venomed != venomed ) Venomed = venomed;
	}
}
