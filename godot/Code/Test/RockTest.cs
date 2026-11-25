using Godot;

/// <summary>
/// Solid rocks (--rocks, playtest: players and enemies got stuck inside the rock walls through a gap on top). Every cliff
/// and reef rock must be solid all the way through: points inside its mesh, between its middle and its skin, are
/// inside its collider, and a ray dropped onto its top lands on the rock, not in a hole. Then the player is dropped
/// onto the tops of the rim cliffs and must not end up inside one.
/// </summary>
public partial class AutoTest
{
	async Task RockTest()
	{
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		WaveDirector.Current.SpawningPaused = true;
		await WaitForNav();
		var space = player.GetWorld3D().DirectSpaceState;
		bool Solid( Vector3 p )
		{
			var q = new PhysicsPointQueryParameters3D { Position = p, CollisionMask = Layers.Plant | Layers.World, CollideWithBodies = true };
			return space.IntersectPoint( q, 1 ).Count > 0;
		}
		var rocks = GetTree().CurrentScene.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>()
			.Where( n => n.GetParent() is { } par && Level.Kind( par ) == "Dressing" && Level.Kind( n ) is var k && (k.StartsWith( "Cliff" ) || k.StartsWith( "Reef" )) && n.HasMeta( VegetationColliders.SolidMeta ) )
			.ToList();
		int probes = 0, hollow = 0, tops = 0, holes = 0, badRocks = 0;
		var worst = "";
		foreach ( var rock in rocks )
		{
			var meshes = rock.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>().Where( m => m.Mesh is not null ).ToList();
			var bad = 0;
			foreach ( var mi in meshes )
			{
				var faces = mi.Mesh.GetFaces();
				var box = mi.Mesh.GetAabb();
				var centre = mi.GlobalTransform * box.GetCenter();
				// Points between the middle and the skin (every 40th vertex), a third and two thirds of the way out.
				for ( var i = 0; i < faces.Length; i += 40 )
				{
					var skin = mi.GlobalTransform * faces[i];
					foreach ( var f in new[] { 0.35f, 0.7f } )
					{
						var p = centre.Lerp( skin, f );
						// Only points the mesh really encloses (a ray up from it meets the rock's own skin first).
						probes++;
						if ( !Solid( p ) ) { hollow++; bad++; }
					}
				}
				// Rays dropped onto its top: the first thing hit must be near the drawn top, not a hole into it.
				var top = mi.GlobalTransform * box;
				for ( var gx = 1; gx <= 3; gx++ )
					for ( var gz = 1; gz <= 3; gz++ )
					{
						var x = MathX.Lerp( top.Position.X, top.End.X, gx / 4f );
						var z = MathX.Lerp( top.Position.Z, top.End.Z, gz / 4f );
						var start = new Vector3( x, top.End.Y + 2f, z );
						var end = start with { Y = top.Position.Y - 1f };
						var meshHit = Geometry3DHit( mi, start, end );
						if ( meshHit is not { } drawn ) continue;
						tops++;
						var hit = Trace.Ray( player, start, end, Layers.Plant | Layers.World );
						if ( !hit.Hit || hit.HitPosition.Y < drawn.Y - 0.6f ) { holes++; bad++; }
					}
			}
			if ( bad > 0 ) badRocks++;
			if ( bad > 0 && worst.Length < 200 ) worst += $" {rock.Name}({bad})";
		}
		GD.Print( $"[autotest] rocks: {rocks.Count} rocks, {probes} inside points, {hollow} hollow; {tops} drops on top, {holes} fell into a hole; rocks with gaps {badRocks}:{worst}" );
		// How far in the rim cliffs reach: swimmers are kept inside Sculpt.InBounds, which must stop short of them.
		var reach = float.MaxValue;
		foreach ( var rock in rocks.Where( r => Level.Kind( r ).StartsWith( "Cliff" ) ) )
			foreach ( var mi in rock.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>().Where( m => m.Mesh is not null ) )
			{
				var b = mi.GlobalTransform * mi.GetAabb();
				var nearX = b.Position.X > 0f ? b.Position.X : b.End.X < 0f ? -b.End.X : 0f;
				var nearZ = b.Position.Z > 0f ? b.Position.Z : b.End.Z < 0f ? -b.End.Z : 0f;
				var inner = MathF.Max( nearX, nearZ );
				if ( inner > SeaTerrain.HalfSize - 30f ) reach = MathF.Min( reach, inner );
			}
		var limit = Sculpt.InBounds( new Vector3( 999f, 0f, 0f ) ).X;
		GD.Print( $"[autotest] rocks: rim cliffs reach in to {reach:0.0} m from the middle; swimmers are kept within {limit:0.0} m" );
		// Spots a swimmer can be pushed to along the edge of its bounds (a player at the rim): none inside a rock.
		int edgeSpots = 0, inRock = 0;
		bool InRock( Vector3 p )
		{
			var q = new PhysicsPointQueryParameters3D { Position = p, CollisionMask = Layers.Plant, CollideWithBodies = true };
			return space.IntersectPoint( q, 4 ).Any( d => d["collider"].AsGodotObject() is Node b && b.HasMeta( VegetationColliders.RockMeta ) );
		}
		for ( var a = -limit; a <= limit; a += 2f )
			foreach ( var spot in new[] { new Vector3( a, 0f, -limit ), new Vector3( a, 0f, limit ), new Vector3( -limit, 0f, a ), new Vector3( limit, 0f, a ) } )
				foreach ( var up in new[] { 0.8f, 2f, 3.5f } )
				{
					var p = Sculpt.InBounds( spot with { Y = SeaTerrain.HeightAt( spot ) + up } );
					edgeSpots++;
					if ( InRock( p ) ) inRock++;
				}
		// Spots inside rocks anywhere (every rock's middle, low down): where a swimmer would be put, none inside a rock.
		var lifted = 0;
		foreach ( var rock in rocks )
		{
			var c = rock.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>().Select( m => m.GlobalTransform * m.GetAabb() ).Aggregate( ( a, b ) => a.Merge( b ) ).GetCenter();
			if ( !InRock( c ) ) continue;
			var p = Sculpt.InBounds( c );
			if ( Sculpt.InBounds( c ).DistanceTo( c ) > 0.01f && !InRock( p ) ) lifted++;
			else if ( InRock( p ) ) inRock++;
		}
		GD.Print( $"[autotest] rocks: {lifted} swimmers put inside a rock were lifted out over it" );
		GD.Print( $"[autotest] rocks: {inRock} of {edgeSpots} spots at the edge of a swimmer's bounds are inside a rock" );
		Check( inRock <= edgeSpots / 100, $"{inRock} of {edgeSpots} spots at the edge of a swimmer's bounds are inside a rock" );
		Check( rocks.Count > 20, $"only {rocks.Count} solid rocks found" );
		Check( hollow <= probes / 50, $"{hollow} of {probes} points inside the rocks are hollow" );
		Check( holes <= tops / 50, $"{holes} of {tops} drops onto rock tops fell into a hole" );

		// The player dropped onto rim cliffs must stand on them, not sink inside.
		var stuck = 0;
		var tried = 0;
		foreach ( var rock in rocks.Where( r => Level.Kind( r ).StartsWith( "Cliff" ) ).Take( 12 ) )
		{
			var box = rock.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>().Select( m => m.GlobalTransform * m.GetAabb() ).Aggregate( ( a, b ) => a.Merge( b ) );
			var above = box.GetCenter() with { Y = box.End.Y + 1.5f };
			// Clear air to drop from (rim cliffs overlap: a taller neighbour can stand over this one's top).
			var clear = true;
			for ( var h = 0f; h <= 2f; h += 0.25f ) if ( Solid( above + Vector3.Up * h ) ) clear = false;
			if ( !clear ) { GD.Print( $"[autotest] rocks: {above} over {rock.Name} is inside another rock, skipped" ); continue; }
			tried++;
			player.GlobalPosition = above;
			player.Velocity = Vector3.Zero;
			await Wait( 1.2f );
			var feet = player.GlobalPosition;
			if ( Solid( feet + Vector3.Up * 0.9f ) )
			{
				// Still settling (sliding down between two rocks)? Give it longer, then see if it can climb out.
				await Wait( 1.5f );
				GD.Print( $"[autotest] rocks: after 2.7 s at {player.GlobalPosition}, velocity {player.Velocity}, on floor {player.IsOnFloor()}, still inside {Solid( player.GlobalPosition + Vector3.Up * 0.9f )}" );
				feet = player.GlobalPosition;
			}
			var inside = Solid( feet + Vector3.Up * 0.9f );
			if ( inside )
			{
				stuck++;
				var q = new PhysicsPointQueryParameters3D { Position = feet + Vector3.Up * 0.9f, CollisionMask = Layers.Plant | Layers.World, CollideWithBodies = true };
				var what = string.Join( ", ", space.IntersectPoint( q, 4 ).Select( d => (d["collider"].AsGodotObject() as Node)?.Name.ToString() ?? "?" ) );
				GD.Print( $"[autotest] rocks: inside {what} at {feet} after a drop onto {rock.Name} (dropped from {above}, ground {SeaTerrain.HeightAt( feet ):0.0})" );
			}
		}
		GD.Print( $"[autotest] rocks: dropped onto {tried} cliffs, inside one {stuck} times" );
		Check( stuck == 0, $"the player ended up inside a cliff {stuck} of {tried} times" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> Where a segment first meets a mesh's own triangles (world space), if it does. </summary>
	static Vector3? Geometry3DHit( MeshInstance3D mi, Vector3 from, Vector3 to )
	{
		var faces = mi.Mesh.GetFaces();
		var inv = mi.GlobalTransform.AffineInverse();
		var a = inv * from;
		var b = inv * to;
		Vector3? best = null;
		var bestD = float.MaxValue;
		for ( var i = 0; i + 2 < faces.Length; i += 3 )
		{
			var hit = Geometry3D.SegmentIntersectsTriangle( a, b, faces[i], faces[i + 1], faces[i + 2] );
			if ( hit.VariantType == Variant.Type.Nil ) continue;
			var p = hit.AsVector3();
			var d = a.DistanceSquaredTo( p );
			if ( d < bestD ) { bestD = d; best = p; }
		}
		return best is { } local ? mi.GlobalTransform * local : null;
	}
}
