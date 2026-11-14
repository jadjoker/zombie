using Godot;

/// <summary>
/// The far side of the teleporter (issue #35): the same sea floor, now with a solid flat plain holding the sunken
/// liner (<see cref="ShipWreck"/>). Going there rebuilds the ground in place: everything set on the sea floor keeps its
/// height above the ground, the plants and props that stood where the plain is are cleared away, the ship is built,
/// the navmesh is baked again, and the player arrives on a pad at the edge of the basin, looking at the wreck.
/// Wave 11 then begins. Metres.
/// </summary>
public static class AreaTwo
{
	/// <summary> Where the player arrives: the basin's edge on the wreck's side, facing it. </summary>
	public static readonly Vector3 ArrivalFlat = new( 0f, 0f, -36f );
	/// <summary> Near where the next teleporter (to Atlantis, after wave 20) zaps in: the old basin, past the stations. </summary>
	public static readonly Vector3 TeleporterNear = new( 0f, 0f, -14f );

	public static bool Active => SeaTerrain.Area == 2;

	public static void Go( Node3D level, PlayerController controller )
	{
		if ( Active || level is null ) return;
		var watch = System.Diagnostics.Stopwatch.StartNew();
		// What stands on the sea floor, and how high above the ground (measured on the old ground).
		var settled = new List<(Node3D Node, float Lift)>();
		foreach ( var n in level.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>() )
		{
			var parent = n.GetParent();
			if ( parent is null ) continue;
			var kind = Level.Kind( parent );
			if ( kind is "Dressing" or "Zombie Spawns" || parent.Name.ToString() == "Sea Dressing" )
				settled.Add( (n, n.GlobalPosition.Y - SeaTerrain.HeightAt( n.GlobalPosition )) );
		}

		SeaTerrain.Area = 2;
		ShipWreck.Place();
		var cleared = 0;
		foreach ( var (n, lift) in settled )
		{
			if ( !GodotObject.IsInstanceValid( n ) ) continue;
			var p = n.GlobalPosition;
			if ( OnPlain( p, 3f ) )
			{
				if ( n.IsInGroup( "spawn_point" ) )
				{
					// Spawn points step off the plain, toward the basin.
					var off = new Vector3( p.X, 0f, SeaTerrain.PlainCentre.Y + SeaTerrain.PlainHalf.Y + 6f );
					n.GlobalPosition = off with { Y = SeaTerrain.HeightAt( off ) };
					continue;
				}
				n.GetParent().RemoveChild( n );
				n.Free();
				cleared++;
				continue;
			}
			n.GlobalPosition = p with { Y = SeaTerrain.HeightAt( p ) + lift };
		}
		if ( level.GetNodeOrNull<AlienFloaters>( "Alien Floaters" ) is { } floaters )
			floaters.Resettle( p => OnPlain( p, 2f ) );

		SeaTerrain.Build( level );
		ShipWreck.Build( level );
		// A few glowing plants drift through the ship too.
		if ( level.GetNodeOrNull<AlienFloaters>( "Alien Floaters" ) is { } inside )
		{
			var random = new Random( 11 );
			foreach ( var (_, local) in ShipWreck.Rooms )
				for ( var k = 0; k < 2; k++ )
					inside.Spawn( random, ShipWreck.World( local + new Vector3( (float)random.NextDouble() * 4f - 2f, 1.8f, (float)random.NextDouble() * 3f - 1.5f ) ) );
		}
		Caustics.AddTo( level );
		PlaceStations( level, new Dictionary<BuyStationKind, Vector3>
		{
			[BuyStationKind.Ammo] = new( -6f, 0f, -27f ),
			[BuyStationKind.Health] = new( -2.2f, 0f, -25f ),
			[BuyStationKind.Damage] = new( 2.2f, 0f, -25f ),
			[BuyStationKind.Trident] = new( 6f, 0f, -27f ),
		}, ArrivalFlat, p => SeaTerrain.HeightAt( p ) );
		Nav.Bake( level );

		// The arrival pad, and the player on it looking at the wreck.
		var arrive = ArrivalFlat with { Y = SeaTerrain.HeightAt( ArrivalFlat ) };
		var pad = new Teleporter { Name = "Arrival Pad", Arrival = true };
		level.AddChild( pad );
		pad.GlobalPosition = arrive;
		if ( controller is not null )
		{
			var land = Nav.Closest( controller, arrive + new Vector3( 0f, 0f, 2.5f ) );
			controller.GlobalPosition = (land == Vector3.Zero ? arrive : land) + Vector3.Up * 0.1f;
			controller.Velocity = Vector3.Zero;
			var toShip = (ShipWreck.Frame.Origin - controller.GlobalPosition).Flat().Normalized();
			controller.EyeAngles = new Angles( 0f, MathX.Deg( MathF.Atan2( -toShip.X, -toShip.Z ) ), 0f );
		}
		WaveDirector.Current?.TeleportDone();
		GD.Print( $"[area2] the wreck site in {watch.ElapsedMilliseconds} ms, {cleared} props cleared off the plain" );
	}

	/// <summary> The trident shooter's price. </summary>
	public const int TridentCost = 1500;

	/// <summary>
	/// Moves the buy stations to a new area (playtest): the level's own three (with everything bought at them, so the
	/// upgrades and their prices carry on) and the trident shooter's station (made here the first time) stand at the
	/// given spots, set on the ground and facing `lookAt` (the arrival pad). Plants and props in the way are cleared.
	/// </summary>
	public static void PlaceStations( Node3D level, Dictionary<BuyStationKind, Vector3> spots, Vector3 lookAt, Func<Vector3, float> ground )
	{
		Vector3 Ground( Vector3 p ) => p with { Y = ground( p ) };
		Vector3 Facing( Vector3 p ) => (lookAt - p).Flat();

		var used = new List<Vector3>();
		foreach ( var body in level.GetTree().GetNodesInGroup( "buy_station" ).OfType<Node3D>().ToList() )
		{
			if ( body.GetNodeOrNull<BuyStation>( "BuyStation" ) is not { } station || !spots.TryGetValue( station.Kind, out var spot ) ) continue;
			if ( body.GetParent() is not Node3D node ) continue;
			var at = Ground( spot );
			node.GlobalTransform = new Transform3D( Shapes.LookAt( Facing( at ) ), at );
			used.Add( at );
		}
		if ( !level.GetTree().GetNodesInGroup( "buy_station" ).OfType<Node3D>().Any( b => b.GetNodeOrNull<BuyStation>( "BuyStation" )?.Kind == BuyStationKind.Trident ) )
		{
			var at = Ground( spots[BuyStationKind.Trident] );
			BuyStation.CreateTridentStation( level, at, Facing( at ), TridentCost );
			used.Add( at );
		}

		// Clear what grows or lies where they now stand (plants, their solid shapes, rocks and the sea props).
		var cleared = 0;
		foreach ( var n in level.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>().ToList() )
		{
			if ( !GodotObject.IsInstanceValid( n ) || n.GetParent() is not { } parent ) continue;
			if ( !(Level.Kind( parent ) == "Dressing" || parent.Name.ToString() == "Sea Dressing") ) continue;
			if ( !used.Any( u => u.Flat().DistanceTo( n.GlobalPosition.Flat() ) < 2.8f ) ) continue;
			parent.RemoveChild( n );
			n.Free();
			cleared++;
		}
		GD.Print( $"[area2] {used.Count} buy stations by the arrival pad, {cleared} props cleared round them" );
	}

	/// <summary> On the wreck site's plain (with a margin). </summary>
	public static bool OnPlain( Vector3 p, float margin ) =>
		MathF.Abs( p.X - SeaTerrain.PlainCentre.X ) < SeaTerrain.PlainHalf.X + margin && MathF.Abs( p.Z - SeaTerrain.PlainCentre.Y ) < SeaTerrain.PlainHalf.Y + margin;
}
