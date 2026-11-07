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

	[Property, Group( "Burn" )] public float BurnDuration { get; set; } = 4f;
	[Property, Group( "Burn" )] public float BurnDamagePerSecond { get; set; } = 8f;
	[Property, Group( "Slow" )] public float SlowDuration { get; set; } = 3.5f;
	/// <summary> Move speed while slowed, as a fraction. </summary>
	[Property, Group( "Slow" )] public float SlowSpeed { get; set; } = 0.45f;

	public float BurnLeft { get; private set; }
	public float SlowLeft { get; private set; }
	/// <summary> What the weapon multiplies move speed by (slowed or not). </summary>
	public float SpeedScale => SlowLeft > 0f ? SlowSpeed : 1f;
	public TimeSince TimeSinceCursed { get; private set; } = 999f;
	public CardKind LastCurse { get; private set; }

	/// <summary> A splash of goo on the camera: where (percent of screen), how big (px), what colour, when. </summary>
	public readonly record struct ScreenDrip( float X, float Y, float Size, Color Color, float Born, float Slide );
	/// <summary> How long goo stays on the screen. </summary>
	public const float DripLife = 1.8f;
	readonly List<ScreenDrip> drips = new();
	/// <summary> Goo currently on the screen (this machine only). Old splats are cleared in OnUpdate. </summary>
	public IReadOnlyList<ScreenDrip> Drips => drips;

	/// <summary>
	/// Something burst right in front of you: splashes of its goo on the camera that slide down and
	/// fade. Local and cosmetic, called on the machine whose player was close.
	/// </summary>
	public void Splattered( Color color, int count )
	{
		if ( IsProxy ) return;
		var now = Time.Now;
		for ( var i = 0; i < count; i++ )
		{
			// Mostly round the middle and upper half, where it's dramatic but you can still see past it.
			var x = Game.Random.Float( 12f, 88f );
			var y = Game.Random.Float( 8f, 70f );
			drips.Add( new ScreenDrip( x, y, Game.Random.Float( 40f, 140f ), color, now - Game.Random.Float( 0f, 0.1f ), Game.Random.Float( 6f, 18f ) ) );
		}
		while ( drips.Count > 24 ) drips.RemoveAt( 0 );
	}

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
		SplatInk();
	}

	void SplatInk()
	{
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

	/// <summary> Enemy (host) -> this player: one of MC61's cards hit you. </summary>
	[Rpc.Owner]
	public void Cursed( CardKind kind )
	{
		if ( Health is null || Health.IsDead ) return;
		LastCurse = kind;
		TimeSinceCursed = 0f;

		switch ( kind )
		{
			case CardKind.Burn:
				BurnLeft = BurnDuration;
				break;
			case CardKind.Poison:
				VenomLeft = VenomDuration;
				TimeSinceVenomed = 0f;
				break;
			case CardKind.Slow:
				SlowLeft = SlowDuration;
				break;
			case CardKind.Ink:
				SplatInk();
				break;
		}
		GetComponent<CameraJuice>()?.Shake( 0.25f );
	}

	/// <summary> Wipes everything (respawning). </summary>
	public void Clear()
	{
		BurnLeft = 0f;
		SlowLeft = 0f;
		VenomLeft = 0f;
		InkLeft = 0f;
		blots.Clear();
		recentInk.Clear();
		recentBites.Clear();
		if ( !IsProxy ) Venomed = false;
	}

	protected override void OnUpdate()
	{
		// Expire old camera splats up front, so the HUD just reads the list.
		var now = Time.Now;
		drips.RemoveAll( d => now - d.Born > DripLife );

		if ( IsProxy ) return;

		var health = Health;
		if ( health is null || health.IsDead )
		{
			if ( VenomLeft > 0f || BurnLeft > 0f || SlowLeft > 0f || InkLeft > 0f || Venomed ) Clear();
			return;
		}

		if ( InkLeft > 0f ) InkLeft = MathF.Max( 0f, InkLeft - Time.Delta );
		if ( SlowLeft > 0f ) SlowLeft = MathF.Max( 0f, SlowLeft - Time.Delta );
		if ( BurnLeft > 0f ) BurnLeft = MathF.Max( 0f, BurnLeft - Time.Delta );

		// Venom and burn tick together.
		var dps = (VenomLeft > 0f ? VenomDamagePerSecond : 0f) + (BurnLeft > 0f ? BurnDamagePerSecond : 0f);
		if ( dps > 0f )
		{
			if ( VenomLeft > 0f ) VenomLeft = MathF.Max( 0f, VenomLeft - Time.Delta );

			// Tick twice a second. Self-inflicted, so the player's hurt reaction stays small.
			tickTimer += Time.Delta;
			if ( tickTimer >= 0.5f )
			{
				tickTimer -= 0.5f;
				health.OnDamage( new DamageInfo
				{
					Damage = dps * 0.5f,
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
