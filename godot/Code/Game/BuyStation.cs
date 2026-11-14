using Godot;

public enum BuyStationKind { Ammo, Health, Damage, Trident }

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
	/// <summary> Health upgrades past the limit stop adding to your maximum (and keep the last price): it just heals. </summary>
	public bool AtLimit => MaxPurchases > 0 && Purchases >= MaxPurchases;
	public int CurrentCost => Cost + CostIncrease * (Kind == BuyStationKind.Health && MaxPurchases > 0 ? Math.Min( Purchases, MaxPurchases - 1 ) : Purchases);
	public bool SoldOut => AtLimit && Kind != BuyStationKind.Health;

	public string Description => Kind switch
	{
		BuyStationKind.Ammo => "Refill magazine and reserve",
		BuyStationKind.Health => AtLimit ? "Full heal (max health is at its limit)" : $"+{HealthPerPurchase:0} max health, full heal",
		BuyStationKind.Damage => $"Damage level {Purchases + 1}",
		BuyStationKind.Trident => "Trident shooter: weapon slot 3",
		_ => "",
	};

	/// <summary> The station on this collider (or one of its parents), if any. </summary>
	static PackedScene model;

	/// <summary>
	/// Swaps the plain box for the Blender terminal (Tools/blender/buy_station.py): painted and lit in the station
	/// colour, with the hologram icon for its kind floating above (the box's collider stays).
	/// </summary>
	public static void Dress( Node3D node, BuyStationKind kind, Color tint )
	{
		model ??= ResourceLoader.Exists( "res://assets/models/props/buy_station.glb" ) ? GD.Load<PackedScene>( "res://assets/models/props/buy_station.glb" ) : null;
		if ( model is null ) return;
		var box = node.GetChildren().OfType<MeshInstance3D>().FirstOrDefault( m => m.Mesh is BoxMesh );
		if ( box is null ) return;
		box.Visible = false;
		var terminal = model.Instantiate<Node3D>();
		node.AddChild( terminal );
		// The box is a 50 inch cube scaled by the node; the model is 1.5 x 1.0 x 1.5 m.
		var cube = 50f * U.In;
		terminal.Scale = new Vector3( cube / 1.5f, cube / 1.0f, cube / 1.5f );
		var paint = new StandardMaterial3D { AlbedoColor = tint.Darkened( 0.25f ), Metallic = 0.4f, Roughness = 0.45f, ClearcoatEnabled = true, Clearcoat = 0.3f };
		var glow = new StandardMaterial3D { AlbedoColor = tint, EmissionEnabled = true, Emission = tint, EmissionEnergyMultiplier = 2.2f };
		var holo = new StandardMaterial3D { AlbedoColor = tint with { A = 0.75f }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = tint, EmissionEnergyMultiplier = 3f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		foreach ( var mesh in terminal.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			var name = mesh.Name.ToString();
			if ( name.StartsWith( "Icon" ) )
			{
				mesh.Visible = name == "Icon" + kind;
				mesh.MaterialOverride = holo;
				mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
				if ( mesh.Visible ) mesh.AddChild( new Spin { Name = "Spin" } );
				continue;
			}
			for ( var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++ )
				switch ( mesh.Mesh.SurfaceGetMaterial( i )?.ResourceName )
				{
					case "StationPaint": mesh.SetSurfaceOverrideMaterial( i, paint ); break;
					case "StationGlow": mesh.SetSurfaceOverrideMaterial( i, glow ); break;
				}
		}
		var light = new OmniLight3D { LightColor = tint, LightEnergy = 0.8f, OmniRange = 2.5f, ShadowEnabled = false };
		terminal.AddChild( light );
		light.Position = new Vector3( 0f, 0.9f, 0f );
	}

	/// <summary> The trident shooter station's colour (the weapon's neon green). </summary>
	public static readonly Color TridentTint = new( 0.35f, 1f, 0.3f );

	/// <summary>
	/// The trident shooter's station on the wreck site (built like the level's stations: a station node with a Body
	/// that holds the box, its collider and the BuyStation, and a label over it), with a trident turning in the
	/// hologram's place. `facing` is the way its front looks.
	/// </summary>
	public static Node3D CreateTridentStation( Node parent, Vector3 at, Vector3 facing, int cost )
	{
		var station = new Node3D { Name = "Trident Station" };
		parent.AddChild( station );
		station.GlobalTransform = new Transform3D( Shapes.LookAt( facing.Flat().SafeNormal() is var f && f != Vector3.Zero ? f : Vector3.Forward ), at );
		var body = new Node3D { Name = "Body" };
		station.AddChild( body );
		body.Transform = new Transform3D( Basis.FromScale( new Vector3( 1.2f, 1.2f, 0.8f ) ), new Vector3( 0f, 0.762f, 0f ) );
		var cube = Vector3.One * 50f * U.In;
		body.AddChild( new MeshInstance3D { Mesh = new BoxMesh { Size = cube }, MaterialOverride = new StandardMaterial3D { AlbedoColor = TridentTint.Darkened( 0.3f ) } } );
		var solid = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
		body.AddChild( solid );
		solid.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = cube } } );
		body.AddChild( new BuyStation { Name = "BuyStation", Kind = BuyStationKind.Trident, Title = "Trident Shooter", Cost = cost, MaxPurchases = 1 } );
		body.AddToGroup( "buy_station" );
		Dress( body, BuyStationKind.Trident, TridentTint );
		// The hologram: a trident standing on its butt, turning and bobbing over the terminal.
		var holo = new Node3D { Name = "Trident Hologram" };
		station.AddChild( holo );
		holo.Position = new Vector3( 0f, 1.95f, 0f );   // the terminal stands 1.52 m; the trident's butt clears it
		var trident = new Node3D { Name = "Trident" };
		holo.AddChild( trident );
		trident.Transform = new Transform3D( new Basis( Vector3.Right, MathF.PI * 0.5f ).Scaled( Vector3.One * 0.75f ), Vector3.Zero );   // -Z (its points) up
		TridentModel.BuildTrident( trident );
		holo.AddChild( new Spin { Name = "Spin" } );
		station.AddChild( new Label3D
		{
			Name = "Label", Text = "TRIDENT", Modulate = TridentTint, FontSize = 32, PixelSize = 0.12f * U.In,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineSize = 6, OutlineModulate = new Color( 0, 0, 0, 0.6f ),
			Position = new Vector3( 0f, 2.6f, 0f ),
		} );
		return station;
	}

	/// <summary> Turns and bobs the hologram icon it sits under. </summary>
	partial class Spin : Node
	{
		float time;
		Vector3 home;
		public override void _Ready() => home = GetParent<Node3D>().Position;
		public override void _Process( double delta )
		{
			time += (float)delta;
			var icon = GetParent<Node3D>();
			icon.Rotation = new Vector3( 0f, time * 1.2f, 0f );
			icon.Position = home + Vector3.Up * MathF.Sin( time * 2f ) * 0.04f;
		}
	}

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
		if ( !CanUse( buyer ) || !buyer.TrySpend( CurrentCost ) ) { Sfx.Play( "deny", buyer.Controller.GlobalPosition ); return false; }
		Apply( buyer );
		Sfx.Play( "buy", buyer.Controller.GlobalPosition );
		if ( Kind == BuyStationKind.Health ) Sfx.Play2D( "heal", 0.6f );
		else if ( Kind == BuyStationKind.Ammo || Kind == BuyStationKind.Trident ) Sfx.Play2D( "mag_in", 0.7f );
		Purchases++;
		return true;
	}

	/// <summary> Stops you paying for something that would do nothing, like ammo when you're full. </summary>
	bool CanUse( PlayerSurvivor buyer ) => Kind switch
	{
		BuyStationKind.Ammo => Weapons( buyer ).Any( w => w.Ammo < w.MagazineSize || w.Reserve < w.StartingReserve ),
		BuyStationKind.Trident => Inventory( buyer ) is { } inventory && !inventory.HasSlot( 3 ),
		_ => true,
	};

	static IEnumerable<PlayerWeapon> Weapons( PlayerSurvivor buyer ) => buyer.Controller.GetChildren().OfType<PlayerWeapon>();
	static WeaponInventory Inventory( PlayerSurvivor buyer ) => buyer.Controller.GetNodeOrNull<WeaponInventory>( "WeaponInventory" );

	void Apply( PlayerSurvivor buyer )
	{
		switch ( Kind )
		{
			case BuyStationKind.Ammo:
				foreach ( var w in Weapons( buyer ) ) w.RefillAmmo();
				break;
			case BuyStationKind.Health:
				if ( AtLimit ) buyer.Health.Heal( buyer.Health.MaxHealth );   // the cap is reached: it still heals
				else buyer.Health.IncreaseMax( HealthPerPurchase );
				break;
			case BuyStationKind.Damage:
				foreach ( var w in Weapons( buyer ) ) w.DamageLevel++;
				break;
			case BuyStationKind.Trident:
				Inventory( buyer )?.Add( PlayerWeapon.Trident() );
				break;
		}
	}
}

/// <summary> The s&box PlayerController's pressing: look at something within reach and press Use. </summary>
public partial class PlayerUse : Node
{
	public float ReachLength = 130f;
	public BuyStation Hovered { get; private set; }
	/// <summary> Standing at the diving board (the prompt shows "Dive in"). </summary>
	public bool AtDivingBoard { get; private set; }
	/// <summary> Standing on the teleporter (the prompt shows "Teleport"). </summary>
	public bool AtTeleporter { get; private set; }
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
		// In third person the camera hangs well behind the robot (issue #38): the look reaches that much farther,
		// and what it lands on still has to be within arm's reach of the robot itself.
		var eye = controller.EyeTransform.Origin;
		var behind = controller.ThirdPerson ? from.DistanceTo( eye ) : 0f;
		var tr = Trace.Ray( controller, from, from + -cam.GlobalBasis.Z * (ReachLength * U.In + behind), Layers.World, new Godot.Collections.Array<Rid> { controller.GetRid() } );
		Hovered = tr.Hit && tr.HitPosition.DistanceTo( eye ) <= ReachLength * U.In * 1.25f ? BuyStation.On( tr.Node ) : null;
		if ( PlayerSurvivor.LocalDead ) Hovered = null;
		AtDivingBoard = Hovered is null && DockHouse.Current is { } dock && dock.BoardInReach( controller );
		AtTeleporter = Hovered is null && !PlayerSurvivor.LocalDead && Teleporter.Current is { } tp && tp.InReach( controller );
		if ( Input.IsActionJustPressed( "use" ) ) PressUse();
	}

	/// <summary> What pressing E does now (tests call it directly; a simulated key press can miss the frame). </summary>
	public void PressUse()
	{
		if ( Hovered is not null ) TryPress();
		else if ( AtDivingBoard ) DockHouse.Current.EnterSea();
		else if ( AtTeleporter ) Teleporter.Current?.Take( controller );
	}

	/// <summary> Presses what you're looking at (also used by tests). False if refused. </summary>
	public bool TryPress()
	{
		if ( Hovered is null || !Hovered.CanPress( survivor ) ) return false;
		return Hovered.Press( survivor );
	}
}
