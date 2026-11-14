using Godot;

/// <summary>
/// Going through the teleporter from the wreck site (after wave 20): the ground is rebuilt in place as area 3, the
/// sea floor round Atlantis under its glass globe (see <see cref="Atlantis"/>). The wreck goes; everything that stood
/// inside the globe's footprint is cleared (the city takes its place); what is outside is set on the new ground. The
/// buy stations (with what was bought at them) and the trident station come along to the arrival plaza, the enemy
/// spawn points move to the city's outer ring street, the floating plants drift high over the rooftops, the marine
/// snow stops (a snow globe without the snow), the navmesh is baked again and wave 21 starts.
/// </summary>
public static class AreaThree
{
	public static bool Active => SeaTerrain.Area == 3;

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

		SeaTerrain.Area = 3;
		ShipWreck.Clear();
		// The wreck site's arrival pad goes (not the teleporter being used: it frees itself when the trip is done).
		foreach ( var pad in level.FindChildren( "*", "Node3D", true, false ).OfType<Teleporter>().Where( t => t.Arrival ).ToList() )
		{
			pad.GetParent().RemoveChild( pad );
			pad.Free();
		}

		var cleared = 0;
		var spawns = new List<Node3D>();
		foreach ( var (n, lift) in settled )
		{
			if ( !GodotObject.IsInstanceValid( n ) ) continue;
			if ( n.IsInGroup( "spawn_point" ) ) { spawns.Add( n ); continue; }
			var p = n.GlobalPosition;
			if ( Atlantis.Inside( p, 2f ) )
			{
				n.GetParent().RemoveChild( n );
				n.Free();
				cleared++;
				continue;
			}
			n.GlobalPosition = p with { Y = SeaTerrain.HeightAt( p ) + lift };
		}
		// The dive's crater (still in the old basin's middle) would poke its rocks up through the city.
		foreach ( var crater in level.GetTree().CurrentScene.GetChildren().OfType<Node3D>().Where( c => c.Name.ToString().StartsWith( "Dive Crater" ) ).ToList() )
		{
			crater.GetParent().RemoveChild( crater );
			crater.Free();
		}
		var floaters = level.GetNodeOrNull<AlienFloaters>( "Alien Floaters" );
		floaters?.Resettle( p => Atlantis.Inside( p, 2f ) );

		SeaTerrain.Build( level );
		Atlantis.Build( level );

		// Glowing plants drifting high over the rooftops, under the dome.
		if ( floaters is not null )
		{
			var random = new Random( 2121 );
			float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
			for ( var i = 0; i < 70; i++ )
			{
				var a = R( 0f, MathF.Tau );
				var r = MathF.Sqrt( R( 0.02f, 1f ) ) * 80f;
				floaters.Spawn( random, new Vector3( MathF.Cos( a ) * r, R( 26f, 58f ), MathF.Sin( a ) * r ) );
			}
		}

		// Enemies come in along the outer ring street, between the avenues.
		for ( var k = 0; k < spawns.Count; k++ )
		{
			var angle = 22.5f + 360f * k / Math.Max( 1, spawns.Count );
			spawns[k].GlobalPosition = Atlantis.Polar( Atlantis.SpawnRing, angle, Atlantis.Land );
		}

		// The buy stations on the arrival plaza, behind the pad, facing it.
		var arrival = Atlantis.Arrival;
		AreaTwo.PlaceStations( level, new Dictionary<BuyStationKind, Vector3>
		{
			[BuyStationKind.Ammo] = arrival + new Vector3( -6f, 0f, -9f ),
			[BuyStationKind.Health] = arrival + new Vector3( -2.2f, 0f, -11f ),
			[BuyStationKind.Damage] = arrival + new Vector3( 2.2f, 0f, -11f ),
			[BuyStationKind.Trident] = arrival + new Vector3( 6f, 0f, -9f ),
		}, arrival, Atlantis.SurfaceAt );

		// No snow in this snow globe.
		foreach ( var snow in level.FindChildren( "*", "GpuParticles3D", true, false ).OfType<MarineSnow>() )
		{
			snow.Emitting = false;
			snow.Visible = false;
		}

		Caustics.AddTo( level );
		Nav.Bake( level );
		Atlantis.AddGlobeCollider();

		// The arrival pad, and the player on it looking up the avenue at the temple.
		var pad2 = new Teleporter { Name = "Arrival Pad", Arrival = true };
		level.AddChild( pad2 );
		pad2.GlobalPosition = arrival;
		if ( controller is not null )
		{
			var land = Nav.Closest( controller, arrival + new Vector3( 0f, 0f, 2.5f ) );
			controller.GlobalPosition = (land == Vector3.Zero ? arrival : land) + Vector3.Up * 0.1f;
			controller.Velocity = Vector3.Zero;
			var toTemple = (Vector3.Zero - controller.GlobalPosition).Flat().Normalized();
			controller.EyeAngles = new Angles( 0f, MathX.Deg( MathF.Atan2( -toTemple.X, -toTemple.Z ) ), 0f );
		}
		WaveDirector.Current?.TeleportDone();
		GD.Print( $"[area3] Atlantis in {watch.ElapsedMilliseconds} ms, {cleared} props cleared from under the globe" );
	}
}
