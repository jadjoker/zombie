/// <summary>
/// Lingering effects enemies put on a player: venom from Bloober bites (a few bites close together
/// poison you, ticking health down and stopping regeneration) and ink from Squilien (a couple of ink
/// hits close together splat ink round the edges of your screen for a few seconds).
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

	/// <summary> Ink hits within the window it takes to get ink on your screen. </summary>
	[Property, Group( "Ink" )] public int InkHits { get; set; } = 2;
	[Property, Group( "Ink" )] public float InkWindow { get; set; } = 4f;
	[Property, Group( "Ink" )] public float InkDuration { get; set; } = 3.5f;
	/// <summary> How many blots land on the screen. Kept to the edges so you can still play. </summary>
	[Property, Group( "Ink" )] public int InkBlots { get; set; } = 4;

	/// <summary> A blot of ink on the screen: centre (percent of screen), size (px), and splatter seed. </summary>
	public readonly record struct Blot( float X, float Y, float Size, int Seed );

	/// <summary> Seconds of ink left on screen (on the owner). </summary>
	public float InkLeft { get; private set; }
	public IReadOnlyList<Blot> Blots => blots;
	readonly List<Blot> blots = new();
	readonly List<float> recentInk = new();

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

	/// <summary> Enemy (host) -> this player: an ink ball hit you. A couple in quick succession ink your screen. </summary>
	[Rpc.Owner]
	public void Inked()
	{
		if ( Health is null || Health.IsDead ) return;

		var now = Time.Now;
		recentInk.RemoveAll( t => now - t > InkWindow );
		recentInk.Add( now );
		if ( recentInk.Count < InkHits ) return;
		recentInk.Clear();

		// Fresh splatter, round the edges of the view (never dead centre).
		InkLeft = InkDuration;
		blots.Clear();
		var start = Game.Random.Float( 0f, 360f );
		for ( var i = 0; i < InkBlots; i++ )
		{
			var angle = (start + i * 360f / InkBlots + Game.Random.Float( -25f, 25f )).DegreeToRadian();
			var reach = Game.Random.Float( 26f, 44f );
			blots.Add( new Blot(
				50f + MathF.Cos( angle ) * reach * 0.95f,
				50f + MathF.Sin( angle ) * reach,
				Game.Random.Float( 170f, 300f ),
				Game.Random.Int( 0, 100000 ) ) );
		}
		GetComponent<CameraJuice>()?.Shake( 0.3f );
	}

	/// <summary> Wipes everything (respawning). </summary>
	public void Clear()
	{
		VenomLeft = 0f;
		InkLeft = 0f;
		blots.Clear();
		recentInk.Clear();
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

		if ( InkLeft > 0f ) InkLeft = MathF.Max( 0f, InkLeft - Time.Delta );

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
