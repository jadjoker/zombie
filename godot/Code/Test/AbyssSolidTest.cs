using Godot;

/// <summary>
/// The Abyss's solidity (part of --abyss, user's ask: nothing to get inside): no holes in the cave walls, the robot
/// walked hard into walls, stalagmites and props never gets its eye inside the rock nor its body inside a prop, no
/// navmesh inside rock, and enemies chasing through the maze never end up inside a wall.
/// </summary>
public partial class AutoTest
{
	const uint VisualLayer = 1u << 20;

	static bool IsCaveSolid( GodotObject collider ) =>
		collider is Node n && (n.HasMeta( VegetationColliders.RockMeta ) || n.Name.ToString() is "Cave Props Solid" or "Chest Solid" or "Statue Solid");

	/// <summary> A point inside a cave rock, a stalagmite, a prop or the statue (not the ground or the roof). </summary>
	bool InCaveRock( Vector3 p )
	{
		var space = player.GetWorld3D().DirectSpaceState;
		var hits = space.IntersectPoint( new PhysicsPointQueryParameters3D { Position = p, CollisionMask = Layers.World, CollideWithAreas = false }, 8 );
		foreach ( var h in hits )
			if ( IsCaveSolid( h["collider"].AsGodotObject() ) ) return true;
		return false;
	}

	async Task AbyssSolid()
	{
		var space = player.GetWorld3D().DirectSpaceState;
		var random = new Random( 11 );

		// 1. Holes: free room for a 0.3 m ball anywhere within a metre of a wall's middle line, at knee and head height.
		var segs = Abyss.WallSegments.Where( s => s.A.DistanceTo( s.B ) > 0.5f ).ToList();
		var ball = new SphereShape3D { Radius = 0.3f };
		int probes = 0, holes = 0;
		foreach ( var (a, b) in segs )
		{
			for ( var t = 0f; t <= 1.001f; t += 0.25f )
				foreach ( var lift in new[] { 0.9f, 2.4f } )
				{
					var q = a.Lerp( b, t );
					var p = new Vector3( q.X, SeaTerrain.HeightAt( q.X, q.Y ) + lift, q.Y );
					probes++;
					var hit = space.IntersectShape( new PhysicsShapeQueryParameters3D { Shape = ball, Transform = new Transform3D( Basis.Identity, p ), CollisionMask = Layers.World }, 4 );
					if ( !hit.Any( h => IsCaveSolid( h["collider"].AsGodotObject() ) ) )
					{
						holes++;
						if ( holes <= 8 ) GD.Print( $"[autotest] abyss solid: a hole in a wall at {p}" );
					}
				}
		}
		GD.Print( $"[autotest] abyss solid: {holes} holes in {probes} probes along the walls" );
		Check( holes == 0, "a cave wall has a hole a robot could get into" );

		// The look of the rock as a body of its own (this test only), to tell when the eye is inside what is drawn.
		var visual = new StaticBody3D { Name = "Visual Rock Probe", CollisionLayer = VisualLayer, CollisionMask = 0 };
		Abyss.Root.AddChild( visual );
		foreach ( var m in Abyss.Root.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			var name = m.Name.ToString();
			if ( !name.StartsWith( "Cave Rock" ) && name != "Stalagmites" ) continue;
			visual.AddChild( new CollisionShape3D { Shape = m.Mesh.CreateTrimeshShape(), Transform = m.GlobalTransform } );
		}
		// The statue's figure too (not its glow and beam).
		foreach ( var m in AbyssStatue.Root.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
			if ( m.Mesh is not null && m.Skin is null && m.IsVisibleInTree() && m.GetParent()?.Name.ToString() != "Abyss Statue" )
				visual.AddChild( new CollisionShape3D { Shape = m.Mesh.CreateTrimeshShape(), Transform = m.GlobalTransform } );
		await Wait( 0.1f );
		var dirs = new List<Vector3>();
		for ( var x = -1; x <= 1; x++ ) for ( var y = -1; y <= 1; y++ ) for ( var z = -1; z <= 1; z++ )
					if ( x != 0 || y != 0 || z != 0 ) dirs.Add( new Vector3( x, y, z ).Normalized() );
		// How near the eye is to the drawn rock (negative: inside it).
		float EyeClear( Vector3 eye )
		{
			var near = 9f;
			var inside = 0;
			foreach ( var d in dirs )
			{
				var r = space.IntersectRay( new PhysicsRayQueryParameters3D { From = eye, To = eye + d * 1.5f, CollisionMask = VisualLayer, HitBackFaces = true } );
				if ( r.Count == 0 ) continue;
				var at = r["position"].AsVector3();
				var n = r["normal"].AsVector3();
				near = MathF.Min( near, at.DistanceTo( eye ) );
				if ( n.Dot( d ) > 0.05f ) inside++;
			}
			return inside >= dirs.Count / 2 ? -1f : near;
		}

		// 2. Walk into walls: from open floor, straight at a wall and at a slant along it.
		var nearest = 0f;
		var nearestAt = "";
		var nearestFeet = 0f;
		async Task<float> Push( Vector3 start, Vector3 toward, float seconds = 1.3f, bool jump = false )
		{
			nearest = 99f;
			player.GlobalPosition = start + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			var dir = (toward - start).Flat().Normalized();
			player.EyeAngles = new Angles( 0f, MathX.Deg( MathF.Atan2( -dir.X, -dir.Z ) ), 0f );
			await Wait( 0.05f );
			Input.ActionPress( "forward" );
			Input.ActionPress( "run" );
			var least = 9f;
			for ( var t = 0f; t < seconds; t += 0.1f )
			{
				if ( jump && t < 0.05f ) Input.ActionPress( "jump" );
				await Wait( 0.1f );
				if ( jump ) Input.ActionRelease( "jump" );
				least = MathF.Min( least, EyeClear( player.Camera.GlobalPosition ) );
				var g = player.GlobalPosition.Flat().DistanceTo( toward.Flat() );
				if ( g < nearest ) { nearest = g; nearestFeet = player.GlobalPosition.Y; nearestAt = $"{player.GlobalPosition} at {t:0.0} s, floor {player.IsOnFloor()}"; }
			}
			Input.ActionRelease( "forward" );
			Input.ActionRelease( "run" );
			await Wait( 0.05f );
			return least;
		}
		int pushes = 0, clipped = 0;
		var worst = 9f;
		foreach ( var (a, b) in segs.OrderBy( _ => random.Next() ).Take( 40 ) )
		{
			var mid = (a + b) * 0.5f;
			var across = new Vector2( -(b - a).Y, (b - a).X ).Normalized();
			var along = (b - a).Normalized();
			foreach ( var side in new[] { -1f, 1f } )
			{
				var s2 = mid + across * side * (Abyss.WallThickness * 0.5f + 2.6f);
				var s = new Vector3( s2.X, 0f, s2.Y );
				if ( Abyss.InWall( s, 0f ) || s.Flat().Length() > Abyss.PocketBack - 2f ) continue;
				var start = Nav.Closest( player, s );
				if ( start.Flat().DistanceTo( s.Flat() ) > 1f ) continue;
				var slant = random.NextDouble() < 0.5 ? 0f : (random.NextDouble() < 0.5 ? -1.6f : 1.6f);
				var aim = mid + along * slant;
				var clear = await Push( start, new Vector3( aim.X, start.Y, aim.Y ) );
				pushes++;
				worst = MathF.Min( worst, clear );
				if ( clear < 0.05f )
				{
					clipped++;
					if ( clipped <= 8 ) GD.Print( $"[autotest] abyss solid: walking into the wall at {mid} put the eye {(clear < 0f ? "inside the rock" : $"{clear:0.00} m from it")} (from {start})" );
				}
				break;
			}
		}
		GD.Print( $"[autotest] abyss solid: {clipped}/{pushes} walks into walls put the eye in the rock (nearest {worst:0.00} m)" );
		Check( pushes >= 20 && clipped == 0, "walking into a cave wall puts the view inside the rock" );

		// 3. Walk into stalagmites and props: the body never ends up inside one.
		int propPushes = 0, propInside = 0;
		var props = AbyssProps.Solids.OrderBy( _ => random.Next() ).Take( 30 ).ToList();
		foreach ( var (name, centre, radius, height) in props )
		{
			var angle = (float)random.NextDouble() * MathF.Tau;
			var s = centre + new Vector3( MathF.Cos( angle ), 0f, MathF.Sin( angle ) ) * (radius + 2.2f);
			var start = Nav.Closest( player, s );
			if ( start.Flat().DistanceTo( s.Flat() ) > 1f ) continue;
			var clear = await Push( start, centre, 1.1f );
			var gap = nearest;
			propPushes++;
			// Inside: the body's middle nearer the prop's middle than its drawn reach, or the eye in the rock.
			if ( (gap < radius * 0.6f && nearestFeet < centre.Y + height * 0.8f) || clear < 0.05f )
			{
				propInside++;
				var here = space.IntersectPoint( new PhysicsPointQueryParameters3D { Position = centre + Vector3.Up * 0.3f, CollisionMask = Layers.World }, 8 ).Select( h => (h["collider"].AsGodotObject() as Node)?.Name.ToString() );
				GD.Print( $"[autotest] abyss solid: walked into the {name} at {centre} (body {gap:0.00} m from its middle, reach {radius:0.00}, eye clear {clear:0.00}); nearest {nearestAt}, started {start}, player now {player.GlobalPosition}, solid at its middle: {string.Join( ",", here )}" );
			}
		}
		GD.Print( $"[autotest] abyss solid: {propInside}/{propPushes} walks into stalagmites and props got inside" );
		// The statue: jumped up onto the base and walked into the figure from all round.
		int statueIn = 0;
		for ( var i = 0; i < 8; i++ )
		{
			var a = i / 8f * MathF.Tau;
			var start = new Vector3( MathF.Cos( a ) * 3.6f, 1.05f, MathF.Sin( a ) * 3.6f );
			var clear = await Push( start, Vector3.Zero, 1.6f, true );
			GD.Print( $"[autotest] abyss solid: into the statue from {start}: on the base at height {player.GlobalPosition.Y:0.00}, {nearest:0.00} m from its middle, eye clear {clear:0.00}" );
			if ( clear < 0.05f ) statueIn++;
		}
		Check( statueIn == 0, "walking into the Abyss statue puts the view inside it" );
		Check( propPushes >= 10 && propInside == 0, "the robot can walk into a stalagmite or a prop in the caves" );
		visual.GetParent().RemoveChild( visual );
		visual.Free();

		// Rock shapes the convex hull maker can't build (the navmesh baker skips those).
		{
			int shapes = 0, bad = 0;
			foreach ( var body in Abyss.Root.FindChildren( "*", "StaticBody3D", true, false ).OfType<StaticBody3D>() )
				foreach ( var cs in body.GetChildren().OfType<CollisionShape3D>() )
					if ( cs.Shape is ConvexPolygonShape3D c )
					{
						shapes++;
						var dm = c.GetDebugMesh();
						if ( dm is null || dm.GetSurfaceCount() == 0 || dm.GetFaces().Length == 0 ) bad++;
					}
			GD.Print( $"[autotest] abyss solid: {bad} of {shapes} convex rock shapes have no hull" );
		}
		// 4. No navmesh in the rock.
		var map = player.GetWorld3D().NavigationMap;
		int navIn = 0, navShallow = 0;
		var probeDeep = new List<Vector3>();
		var navDeepest = 0f;
		for ( var i = 0; i < 1500; i++ )
		{
			var p = NavigationServer3D.MapGetRandomPoint( map, 1, true );
			if ( p.Flat().Length() > Abyss.PocketBack + 4f ) continue;
			if ( InCaveRock( p + Vector3.Up * 0.35f ) )
			{
				// How deep: the nearest spot beside it that is clear.
				var depth = 9f;
				for ( var r = 0.05f; r < 3f && depth > 8f; r += 0.05f )
					for ( var k = 0; k < 16; k++ )
					{
						var a = k / 16f * MathF.Tau;
						if ( !InCaveRock( p + new Vector3( MathF.Cos( a ) * r, 0.35f, MathF.Sin( a ) * r ) ) ) { depth = r; break; }
					}
				navDeepest = MathF.Max( navDeepest, depth );
				if ( depth < 0.3f ) { navShallow++; continue; }
				navIn++;
				probeDeep.Add( p );
				if ( navIn <= 6 )
				{
					var what = space.IntersectPoint( new PhysicsPointQueryParameters3D { Position = p + Vector3.Up * 0.35f, CollisionMask = Layers.World }, 8 ).Select( h => (h["collider"].AsGodotObject() as Node)?.Name.ToString() );
					GD.Print( $"[autotest] abyss solid: navmesh {depth:0.00} m inside rock at {p} (ground {SeaTerrain.HeightAt( p ):0.00}, in at +0.05 {InCaveRock( p + Vector3.Up * 0.05f )}, +1 {InCaveRock( p + Vector3.Up * 1f )}, +1.6 {InCaveRock( p + Vector3.Up * 1.6f )}; {string.Join( ",", what )})" );
				}
			}
		}
		// PROBE (temporary): pictures of the navmesh round the deep points.
		if ( !string.IsNullOrEmpty( shots ) )
		{
			var nm = Nav.Region.NavigationMesh;
			var verts = nm.GetVertices();
			var k2 = 0;
			foreach ( var p in probeDeep.Take( 3 ) )
			{
				var im = new ImmediateMesh();
				var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color( 1f, 0.1f, 0.1f ), NoDepthTest = true };
				im.SurfaceBegin( Mesh.PrimitiveType.Lines, mat );
				for ( var i = 0; i < nm.GetPolygonCount(); i++ )
				{
					var poly = nm.GetPolygon( i );
					if ( verts[poly[0]].DistanceTo( p ) > 10f ) continue;
					for ( var j = 0; j < poly.Length; j++ ) { im.SurfaceAddVertex( verts[poly[j]] + Vector3.Up * 0.05f ); im.SurfaceAddVertex( verts[poly[(j + 1) % poly.Length]] + Vector3.Up * 0.05f ); }
				}
				im.SurfaceAddVertex( p ); im.SurfaceAddVertex( p + Vector3.Up * 3f );
				im.SurfaceEnd();
				var mi = new MeshInstance3D { Mesh = im };
				Abyss.Root.AddChild( mi );
				var lamp = new OmniLight3D { OmniRange = 30f, LightEnergy = 4f };
				Abyss.Root.AddChild( lamp );
				lamp.GlobalPosition = p + Vector3.Up * 12f;
				var view = new Transform3D( Shapes.LookAt( Vector3.Down, Vector3.Forward ), p + Vector3.Up * 16f );
				player.CameraOverride = _ => view;
				await Wait( 0.3f );
				await Shot( $"navprobe_{k2++}" );
				player.CameraOverride = null;
				mi.QueueFree();
				lamp.QueueFree();
			}
		}
		GD.Print( $"[autotest] abyss solid: {navIn} of 1500 navmesh points more than 0.3 m inside rock, {navShallow} less (deepest {navDeepest:0.00} m)" );
		Check( navIn == 0, "the Abyss's navmesh goes inside rock" );

		// 5. Enemies chase through the maze for a while and never stand or swim inside rock.
		var director = WaveDirector.Current;
		var hub = Nav.Closest( player, Abyss.Polar( 18f, 60f ) );
		player.GlobalPosition = hub + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		var kinds = new[] { WaveDirector.EnemyKind.Crab, WaveDirector.EnemyKind.Spider, WaveDirector.EnemyKind.Bloober, WaveDirector.EnemyKind.RockyShark, WaveDirector.EnemyKind.Squilien, WaveDirector.EnemyKind.Stingray, WaveDirector.EnemyKind.Angler, WaveDirector.EnemyKind.Puffer, WaveDirector.EnemyKind.HermitCrab };
		var spawned = new List<(string Kind, Node3D Node)>();
		for ( var i = 0; i < kinds.Length * 2; i++ )
		{
			var at = Abyss.SpawnSpots[i % Abyss.SpawnSpots.Count];
			if ( director.SpawnAt( kinds[i % kinds.Length], at, hub ) is { } n ) spawned.Add( (kinds[i % kinds.Length].ToString(), n) );
		}
		DevFlags.FreezeEnemies = false;
		var insideBy = new Dictionary<string, int>();
		int samples = 0;
		for ( var t = 0f; t < 24f; t += 0.25f )
		{
			await Wait( 0.25f );
			foreach ( var (kind, n) in spawned )
			{
				if ( !IsInstanceValid( n ) ) continue;
				samples++;
				// Its middle: a crawler's a little over its feet, a swimmer's where it is.
				var p = n.GlobalPosition + Vector3.Up * 0.4f;
				if ( !InCaveRock( p ) ) continue;
				insideBy[kind] = insideBy.GetValueOrDefault( kind ) + 1;
				if ( insideBy[kind] <= 2 )
				{
					var what = space.IntersectPoint( new PhysicsPointQueryParameters3D { Position = p, CollisionMask = Layers.World }, 8 ).Select( h => (h["collider"].AsGodotObject() as Node)?.Name.ToString() );
					GD.Print( $"[autotest] abyss solid: a {kind} inside rock at {n.GlobalPosition} after {t:0.0} s ({string.Join( ",", what )}; lifted out to {Sculpt.OutOfRock( n.GlobalPosition )})" );
				}
			}
		}
		DevFlags.FreezeEnemies = true;
		var total = insideBy.Values.Sum();
		GD.Print( $"[autotest] abyss solid: {spawned.Count} enemies chased for 24 s, {total} of {samples} samples inside rock ({string.Join( ", ", insideBy.Select( kv => $"{kv.Key} {kv.Value}" ) )})" );
		Check( total == 0, "an enemy went inside the cave rock" );
		director.ClearEnemies();
		await Wait( 0.5f );
	}
}
