using Godot;

public enum BuyStationKind { Ammo, Health, Damage }

/// <summary>
/// Port of s&box BuyStation: something in the world you press Use on to spend points. Lives under a level object
/// whose collider the player looks at (see <see cref="PlayerUse"/>).
/// </summary>
public partial class BuyStation : Node
{
	public BuyStationKind Kind = BuyStationKind.Ammo;
	public string Title = "Ammo";
	public int Cost = 250;
	public int CostIncrease;
	public int MaxPurchases;
	public float HealthPerPurchase = 25f;

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

	/// <summary> The station on this collider (or one of its parents), if any. </summary>
	public static BuyStation On( Node node )
	{
		for ( var n = node; n is not null; n = n.GetParent() )
			if ( n.GetNodeOrNull<BuyStation>( "BuyStation" ) is { } station ) return station;
		return null;
	}

	public bool CanPress( PlayerSurvivor buyer ) => buyer is not null && buyer.IsAlive && !SoldOut;

	public bool Press( PlayerSurvivor buyer )
	{
		if ( buyer is null || SoldOut ) return false;
		if ( !CanUse( buyer ) || !buyer.TrySpend( CurrentCost ) ) return false;
		Apply( buyer );
		Purchases++;
		return true;
	}

	/// <summary> Stops you paying for something that would do nothing, like ammo when you're full. </summary>
	bool CanUse( PlayerSurvivor buyer ) => Kind switch
	{
		BuyStationKind.Ammo => Weapons( buyer ).Any( w => w.Ammo < w.MagazineSize || w.Reserve < w.StartingReserve ),
		_ => true,
	};

	static IEnumerable<PlayerWeapon> Weapons( PlayerSurvivor buyer ) => buyer.Controller.GetChildren().OfType<PlayerWeapon>();

	void Apply( PlayerSurvivor buyer )
	{
		switch ( Kind )
		{
			case BuyStationKind.Ammo:
				foreach ( var w in Weapons( buyer ) ) w.RefillAmmo();
				break;
			case BuyStationKind.Health:
				buyer.Health.IncreaseMax( HealthPerPurchase );
				break;
			case BuyStationKind.Damage:
				foreach ( var w in Weapons( buyer ) ) w.DamageLevel++;
				break;
		}
	}
}

/// <summary> The s&box PlayerController's pressing: look at something within reach and press Use. </summary>
public partial class PlayerUse : Node
{
	public float ReachLength = 130f;
	public BuyStation Hovered { get; private set; }
	PlayerController controller;
	PlayerSurvivor survivor;

	public override void _Ready()
	{
		Name = "PlayerUse";
		controller = GetParent<PlayerController>();
	}

	public override void _Process( double delta )
	{
		survivor ??= controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
		var cam = controller.Camera;
		var from = cam.GlobalPosition;
		var tr = Trace.Ray( controller, from, from + -cam.GlobalBasis.Z * ReachLength * U.In, Layers.World );
		Hovered = tr.Hit ? BuyStation.On( tr.Node ) : null;
		if ( Hovered is not null && Input.IsActionJustPressed( "use" ) ) TryPress();
	}

	/// <summary> Presses what you're looking at (also used by tests). False if refused. </summary>
	public bool TryPress()
	{
		if ( Hovered is null || !Hovered.CanPress( survivor ) ) return false;
		return Hovered.Press( survivor );
	}
}
