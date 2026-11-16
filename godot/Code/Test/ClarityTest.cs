using Godot;

/// <summary>
/// Visual clarity (the clutter pass): from fixed spots in each area, at the player's eye, a fan of enemies stands 12 to
/// 28 m off. For each it measures how much of the enemy a ray from the eye reaches past the world and the solid plants
/// (`seen`), and counts the floating plants and the lights in view (`clutter`). Shots of each view (with --shots, in a
/// window) give the before and after pictures. Checks that a fair share of every enemy stays in sight.
/// </summary>
public partial class AutoTest
{
	/// <summary>
	/// The glow bulbs: about 60 to 100 out (not 230), in schools; one flees from a player who walks up to it; a shot pops
	/// it for points; a gold one drops a pickup; popped ones grow back.
	/// </summary>
	async Task BulbTest()
	{
		DevFlags.FreezeEnemies = true;
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 0.5f );
		var level = GetTree().CurrentScene as Node3D;
		var floaters = level.GetNodeOrNull<AlienFloaters>( "Alien Floaters" );
		Check( floaters is not null, "no glow bulbs" );
		if ( floaters is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		var bulbs = floaters.Bulbs.Count();
		GD.Print( $"[autotest] bulbs: {bulbs} bulbs, {floaters.Count} with the ferns" );
		Check( bulbs >= 40 && bulbs <= 110, $"expected about 60 to 100 bulbs, not {bulbs}" );

		// Walk up to one: it shies away.
		var target = floaters.NearestBulb( new Vector3( 28f, 0f, 0f ) );
		var start = target.GlobalPosition;
		var near = Nav.Closest( player, start );
		player.GlobalPosition = near + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		await Wait( 2f );
		var fled = target.GlobalPosition.DistanceTo( start );
		var gap = target.GlobalPosition.DistanceTo( player.GlobalPosition + Vector3.Up );
		GD.Print( $"[autotest] bulbs: one {start.DistanceTo( near ):0.0} m from the player moved {fled:0.0} m away, now {gap:0.0} m off" );
		Check( fled > 1.5f, "a bulb did not flee from a player standing by it" );

		// Shoot one: it pops for points.
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var weapon = inventory.Weapons[0];
		inventory.Select( weapon, force: true );
		weapon.SpreadDegrees = 0f;
		async Task<bool> ShootAt( Node3D bulb )
		{
			// Stand off a little (it would flee) and fire straight at it.
			var spot = Nav.Closest( player, bulb.GlobalPosition + new Vector3( 9f, 0f, 0f ) );
			player.GlobalPosition = spot + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			await Wait( 0.3f );
			var before = AlienFloaters.Popped;
			for ( var i = 0; i < 6 && AlienFloaters.Popped == before; i++ )
			{
				LookAt( bulb.GlobalPosition );
				await Wait( 0.05f );
				weapon.TestFire();
				await Wait( 0.15f );
			}
			return AlienFloaters.Popped > before;
		}
		var points = survivor.Points;
		var plain = floaters.NearestBulb( new Vector3( -28f, 0f, 0f ), gold: false );
		var popped = await ShootAt( plain );
		GD.Print( $"[autotest] bulbs: shot one, popped {popped}, points {points} -> {survivor.Points}" );
		Check( popped && survivor.Points - points >= AlienFloaters.BulbPoints, "shooting a bulb did not pop it for points" );

		// A gold one drops a pickup.
		var pickups = level.FindChildren( "*", "Node3D", true, false ).OfType<PerkPickup>().Count();
		var gold = floaters.NearestBulb( new Vector3( 0f, 0f, 28f ), gold: true );
		points = survivor.Points;
		var goldPopped = await ShootAt( gold );
		await Wait( 0.2f );
		var after = GetTree().CurrentScene.FindChildren( "*", "Node3D", true, false ).OfType<PerkPickup>().Count();
		GD.Print( $"[autotest] bulbs: gold popped {goldPopped}, points +{survivor.Points - points}, pickups {pickups} -> {after}" );
		Check( goldPopped && after > pickups && survivor.Points - points >= AlienFloaters.GoldPoints, "a gold bulb did not pay out and drop a pickup" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	async Task ClarityTest()
	{
		DevFlags.FreezeEnemies = true;
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 0.5f );
		var level = GetTree().CurrentScene as Node3D;
		var director = WaveDirector.Current;
		var seenAll = new List<float>();

		async Task View( string name, Vector3 stand, Vector3 toward )
		{
			var spot = Nav.Closest( player, stand );
			player.GlobalPosition = spot + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			await Wait( 0.4f );
			var eye = player.Camera.GlobalPosition;
			var ahead = (toward - spot).Flat().Normalized();
			var right = ahead.Cross( Vector3.Up );
			var kinds = new[] { WaveDirector.EnemyKind.Crab, WaveDirector.EnemyKind.Bloober, WaveDirector.EnemyKind.RockyShark, WaveDirector.EnemyKind.Squilien, WaveDirector.EnemyKind.Crab, WaveDirector.EnemyKind.Bloober };
			var enemies = new List<Node3D>();
			for ( var i = 0; i < kinds.Length; i++ )
			{
				var along = 12f + 16f * (i % 3) / 2f;
				var side = (i - 2.5f) * 3.2f;
				var at = Nav.Closest( player, spot + ahead * along + right * side );
				if ( director.SpawnAt( kinds[i], at, spot ) is { } e ) enemies.Add( e );
			}
			await Wait( 1.2f );
			LookAt( spot + ahead * 20f + Vector3.Up * 1.2f );
			await Wait( 0.2f );
			eye = player.Camera.GlobalPosition;

			// How much of each enemy the eye can see: rays to each of its parts, past the world and the solid plants.
			var space = player.GetWorld3D().DirectSpaceState;
			var seen = new List<float>();
			foreach ( var e in enemies.Where( IsInstanceValid ) )
			{
				var parts = CreaturePart.All.Where( p => IsInstanceValid( p ) && p.Creature == e ).ToList();
				if ( parts.Count == 0 ) continue;
				var clear = 0;
				foreach ( var p in parts )
				{
					var q = PhysicsRayQueryParameters3D.Create( eye, p.GlobalPosition, Layers.World | Layers.Plant, new Godot.Collections.Array<Rid> { player.GetRid() } );
					if ( space.IntersectRay( q ).Count == 0 ) clear++;
				}
				seen.Add( clear / (float)parts.Count );
			}
			// Clutter in view: floating plants and lights within 30 m in front of the eye.
			var forward = -player.Camera.GlobalBasis.Z;
			bool InView( Vector3 p ) => p.DistanceTo( eye ) < 30f && forward.Dot( (p - eye).Normalized() ) > 0.6f;
			var floaters = level.GetNodeOrNull<AlienFloaters>( "Alien Floaters" )?.GetChildren().OfType<Node3D>().Count( n => InView( n.GlobalPosition ) ) ?? 0;
			var lights = level.FindChildren( "*", "OmniLight3D", true, false ).OfType<OmniLight3D>().Count( l => l.Visible && InView( l.GlobalPosition ) );
			var plants = level.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>()
				.Count( n => n.GetParent() is { } par && (Level.Kind( par ) == "Dressing" || par.Name.ToString() == "Sea Dressing") && InView( n.GlobalPosition ) );
			var mean = seen.Count > 0 ? seen.Average() : 0f;
			seenAll.AddRange( seen );
			GD.Print( $"[autotest] clarity {name}: enemies seen {mean:P0} (worst {(seen.Count > 0 ? seen.Min() : 0f):P0}, {seen.Count} enemies), in view: {floaters} floaters, {plants} props, {lights} lights" );
			await Shot( "clarity_" + name );
			foreach ( var e in enemies.Where( IsInstanceValid ) ) e.QueueFree();
			await Wait( 0.3f );
		}

		// Area 1: where the dive lands, the basin's middle, and out on the hills.
		var tip = DockHouse.Current?.BoardTip ?? Vector3.Zero;
		await View( "a1_landing", tip with { Y = 0f }, Vector3.Zero );
		await View( "a1_basin", new Vector3( 0f, 0f, 6f ), new Vector3( 0f, 0f, -30f ) );
		await View( "a1_reef", new Vector3( 18f, 0f, 0f ), new Vector3( 60f, 0f, 10f ) );
		await View( "a1_hills", new Vector3( -45f, 0f, 30f ), new Vector3( -80f, 0f, 60f ) );

		// Area 2: the arrival, looking at the wreck, and along the plain.
		AreaTwo.Go( level, player );
		await Wait( 1f );
		await View( "a2_arrival", AreaTwo.ArrivalFlat, SeaTerrain.PlainCentre is var c ? new Vector3( c.X, 0f, c.Y ) : Vector3.Zero );
		await View( "a2_basin", new Vector3( 10f, 0f, 0f ), new Vector3( -20f, 0f, 20f ) );

		// Area 3: Atlantis, from the arrival up the avenue.
		AreaThree.Go( level, player );
		await Wait( 1f );
		await View( "a3_arrival", Atlantis.Arrival, Vector3.Zero );

		var overall = seenAll.Count > 0 ? seenAll.Average() : 0f;
		GD.Print( $"[autotest] clarity: overall enemies seen {overall:P0} over {seenAll.Count}" );
		Check( seenAll.Count >= 20, "too few enemies placed to judge clarity" );
		Check( overall > 0.5f, "enemies are mostly hidden behind props" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
