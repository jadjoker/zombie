public enum BuyStationKind
{
	/// <summary> Refills the magazine and reserve. </summary>
	Ammo,
	/// <summary> Raises max health and heals fully. </summary>
	Health,
	/// <summary> Raises gun damage. </summary>
	Damage,
}

/// <summary>
/// Something in the world you can press Use on to spend points.
/// Needs a collider so the player controller can look at it.
/// </summary>
public sealed class BuyStation : Component, Component.IPressable
{
	[Property] public BuyStationKind Kind { get; set; } = BuyStationKind.Ammo;
	[Property] public string Title { get; set; } = "Ammo";
	[Property] public int Cost { get; set; } = 250;
	/// <summary> Added to the cost after each purchase, so upgrades get pricier. </summary>
	[Property] public int CostIncrease { get; set; } = 0;
	/// <summary> 0 means unlimited. </summary>
	[Property] public int MaxPurchases { get; set; } = 0;
	[Property] public float HealthPerPurchase { get; set; } = 25f;

	[Property, Group( "Sounds" )] public SoundEvent BuySound { get; set; }
	[Property, Group( "Sounds" )] public SoundEvent DenySound { get; set; }

	public int Purchases { get; private set; }
	public int CurrentCost => Cost + CostIncrease * Purchases;
	public bool SoldOut => MaxPurchases > 0 && Purchases >= MaxPurchases;

	public string Description => Kind switch
	{
		BuyStationKind.Ammo => "Refill magazine and reserve",
		BuyStationKind.Health => $"+{HealthPerPurchase:0} max health, full heal",
		BuyStationKind.Damage => $"Damage level {Purchases + 1}",
		_ => "",
	};

	static PlayerSurvivor Buyer( IPressable.Event e ) => e.Source?.GetComponent<PlayerSurvivor>();

	bool IPressable.CanPress( IPressable.Event e )
	{
		var buyer = Buyer( e );
		return buyer.IsValid() && buyer.IsAlive && !SoldOut;
	}

	bool IPressable.Press( IPressable.Event e )
	{
		var buyer = Buyer( e );
		if ( !buyer.IsValid() || SoldOut ) return false;

		if ( !CanUse( buyer ) || !buyer.TrySpend( CurrentCost ) )
		{
			if ( DenySound is not null ) Sound.Play( DenySound, WorldPosition );
			return false;
		}

		Apply( buyer );
		Purchases++;
		if ( BuySound is not null ) Sound.Play( BuySound, WorldPosition );
		return true;
	}

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		var buyer = Buyer( e );
		var label = SoldOut ? "Sold out" : $"{CurrentCost} points";
		var affordable = buyer.IsValid() && buyer.Points >= CurrentCost && !SoldOut;
		return new IPressable.Tooltip( $"{Title} - {label}", "shopping_cart", Description, affordable, this );
	}

	/// <summary> Stops you paying for something that would do nothing, like ammo when you're full. </summary>
	bool CanUse( PlayerSurvivor buyer )
	{
		return Kind switch
		{
			BuyStationKind.Ammo => AllWeapons( buyer ).Any( w => w.Ammo < w.MagazineSize || w.Reserve < w.StartingReserve ),
			_ => true,
		};
	}

	/// <summary> Every gun the player carries, including the holstered ones. </summary>
	static IEnumerable<PlayerWeapon> AllWeapons( PlayerSurvivor buyer ) =>
		buyer.Components.GetAll<PlayerWeapon>( FindMode.EverythingInSelf );

	void Apply( PlayerSurvivor buyer )
	{
		switch ( Kind )
		{
			case BuyStationKind.Ammo:
				foreach ( var w in AllWeapons( buyer ) ) w.RefillAmmo();
				break;
			case BuyStationKind.Health:
				buyer.Health.IncreaseMax( HealthPerPurchase );
				break;
			case BuyStationKind.Damage:
				foreach ( var w in AllWeapons( buyer ) ) w.DamageLevel++;
				break;
		}
	}
}
