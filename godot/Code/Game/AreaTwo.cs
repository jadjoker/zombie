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

	/// <summary> On the wreck site's plain (with a margin). </summary>
	public static bool OnPlain( Vector3 p, float margin ) =>
		MathF.Abs( p.X - SeaTerrain.PlainCentre.X ) < SeaTerrain.PlainHalf.X + margin && MathF.Abs( p.Z - SeaTerrain.PlainCentre.Y ) < SeaTerrain.PlainHalf.Y + margin;
}
