using Godot;

/// <summary>
/// Going through the teleporter from the wreck site (after wave 20): the ground is rebuilt in place as area 3, the
/// Abyss (<see cref="Abyss"/>). The wreck goes; the sea floor's plants and props that would stand in a cave wall, in
/// the hub or out past the outer wall are cleared and the rest set on the new floor; the statue, the scenery
/// (<see cref="AbyssProps"/>) and the chests go in; the buy stations come along to the hub; the enemy spawn points
/// move into the cave mouths; the glow bulbs drift through the corridors; the light goes (<see cref="AbyssMood"/>);
/// the navmesh is baked again and wave 21 starts.
/// </summary>
public static class AbyssArea
{
	public static bool Active => SeaTerrain.Area == SeaTerrain.Caves;

	public static void Go( Node3D level, PlayerController controller )
	{
		if ( Active || level is null ) return;
		var watch = System.Diagnostics.Stopwatch.StartNew();
		var settled = new List<(Node3D Node, float Lift)>();
		foreach ( var n in level.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>() )
		{
			var parent = n.GetParent();
			if ( parent is null ) continue;
			var kind = Level.Kind( parent );
			if ( kind is "Dressing" or "Zombie Spawns" || parent.Name.ToString() == "Sea Dressing" )
				settled.Add( (n, n.GlobalPosition.Y - SeaTerrain.HeightAt( n.GlobalPosition )) );
		}

		SeaTerrain.Area = SeaTerrain.Caves;
		ShipWreck.Clear();
		foreach ( var pad in level.FindChildren( "*", "Node3D", true, false ).OfType<Teleporter>().Where( t => t.Arrival ).ToList() )
		{
			pad.GetParent().RemoveChild( pad );
			pad.Free();
		}
		foreach ( var crater in level.GetChildren().OfType<Node3D>().Where( c => c.Name.ToString().StartsWith( "Dive Crater" ) ).ToList() )
		{
			crater.GetParent().RemoveChild( crater );
			crater.Free();
		}

		// The maze first (the clearing below needs its walls).
		Abyss.Build( level );
		var cleared = 0;
		var spawns = new List<Node3D>();
		// A prop's collider is its own node beside it: it goes with the prop (left alone it was an invisible wall).
		foreach ( var (n, _) in settled )
		{
			if ( !GodotObject.IsInstanceValid( n ) || n.IsInGroup( "spawn_point" ) || n is StaticBody3D ) continue;
			var p = n.GlobalPosition;
			var r = p.Flat().Length();
			// Out of the walls, the hub and the rock past the outer wall; a cave has room for a little life, not a forest.
			if ( r < Abyss.HubRadius + 1f || r > Abyss.Rings[3] - 2f || Abyss.InWall( p, 1.2f ) || Mathf.PosMod( n.GetIndex(), 3 ) != 0 )
			{
				// Freed now, not queued: the navmesh is baked this frame.
				if ( n.HasMeta( VegetationColliders.SolidMeta ) && n.GetMeta( VegetationColliders.SolidMeta ).AsGodotObject() is Node3D solid && GodotObject.IsInstanceValid( solid ) )
				{
					solid.GetParent()?.RemoveChild( solid );
					solid.Free();
				}
				n.GetParent().RemoveChild( n );
				n.Free();
				cleared++;
			}
		}
		foreach ( var (n, lift) in settled )
		{
			if ( !GodotObject.IsInstanceValid( n ) ) continue;
			if ( n.IsInGroup( "spawn_point" ) ) { spawns.Add( n ); continue; }
			var p = n.GlobalPosition;
			n.GlobalPosition = p with { Y = SeaTerrain.HeightAt( p ) + lift };
		}

		SeaTerrain.Build( level );
		AbyssStatue.Build( Abyss.Root );
		AbyssProps.Dress( Abyss.Root, Net.IsClient );

		// Glow bulbs drift through the corridors, a school here and there.
		if ( level.GetNodeOrNull<AlienFloaters>( "Alien Floaters" ) is { } floaters )
		{
			floaters.Resettle( _ => true );
			var random = new Random( 3131 );
			var cells = Abyss.CellCentres.OrderBy( _ => random.Next() ).Take( 16 ).ToList();
			foreach ( var c in cells )
				floaters.SpawnSchool( random, c + Vector3.Up * (2.5f + (float)random.NextDouble() * 2.5f), random.Next( 2, 5 ) );
		}

		// Enemies come in through the cave mouths.
		for ( var k = 0; k < spawns.Count; k++ )
			spawns[k].GlobalPosition = Abyss.SpawnSpots[k % Abyss.SpawnSpots.Count];

		// The buy stations stand behind the arrival pad, facing it (the statue past it).
		var arrival = Abyss.Arrival;
		var outward = arrival.Flat().Normalized();
		var side = outward.Cross( Vector3.Up );
		AreaTwo.PlaceStations( level, new Dictionary<BuyStationKind, Vector3>
		{
			[BuyStationKind.Ammo] = arrival + outward * 5f + side * 4.5f,
			[BuyStationKind.Health] = arrival + outward * 6.5f + side * 1.5f,
			[BuyStationKind.Damage] = arrival + outward * 6.5f - side * 1.5f,
		}, arrival, p => SeaTerrain.HeightAt( p ) );

		AbyssMood.Enter( level );
		Nav.Bake( level );
		// Only the cave floor everyone can walk to (not patches on the wall tops or in pockets in the rock).
		var dropped = Nav.KeepConnected( arrival );
		GD.Print( $"[area3] navmesh: {dropped} polygons not joined to the cave floor dropped" );
		Abyss.AddRoofCollider();
		SeaMine.Lay( level );
		EelBed.Lay( level );

		var pad2 = new Teleporter { Name = "Arrival Pad", Arrival = true };
		level.AddChild( pad2 );
		pad2.GlobalPosition = arrival;
		if ( controller is not null )
		{
			var land = Nav.Closest( controller, arrival - outward * 2.5f );
			controller.GlobalPosition = (land == Vector3.Zero ? arrival : land) + Vector3.Up * 0.1f;
			controller.Velocity = Vector3.Zero;
			var toStatue = (Vector3.Zero - controller.GlobalPosition).Flat().Normalized();
			controller.EyeAngles = new Angles( -8f, MathX.Deg( MathF.Atan2( -toStatue.X, -toStatue.Z ) ), 0f );
		}
		WaveDirector.Current?.TeleportDone();
		GD.Print( $"[area3] the Abyss in {watch.ElapsedMilliseconds} ms, {cleared} props cleared, {spawns.Count} spawn points in the cave mouths" );
	}
}
