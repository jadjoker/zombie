using Godot;

/// <summary>
/// --gorevac: the Gore Vac and its physics. Its station stands in the first area's row; held fire sucks loose gibs and
/// goo puddles into the tank, drags a crab and a Bloober in, tears off a limb shot half away, sucks a weak crab apart,
/// tears a starfish off the floor and a mine off its chain (loaded, then spat and blown up); a full tank slows you; a
/// shot at the floor goes off as a mini explosion, one aimed beside a crab still gets it; the huge shot throws bomblets and hurts
/// several crabs, and kicks you back; blowing shoves you back. With --shots: pictures along the way.
/// (The test drives the controls in code: ForceSuck is right click, TestSpit is left click.)
/// </summary>
public partial class AutoTest
{
	async Task GoreVacTest()
	{
		var me = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		me.Health.Invulnerable = true;
		DevFlags.FreezeEnemies = true;
		if ( WaveDirector.Current is { } director ) director.SpawningPaused = true;   // only the creatures placed here
		foreach ( var laid in SeaMine.All.ToList() ) laid.QueueFree();   // the suction reaches any distance: only the test's own mine
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 0.5f );

		var station = GetTree().GetNodesInGroup( "buy_station" ).OfType<Node3D>().FirstOrDefault( b => b.GetNodeOrNull<BuyStation>( "BuyStation" )?.Kind == BuyStationKind.GoreVac );
		Check( station is not null, "there is no Gore Vac station in the first area" );

		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		inventory.Add( PlayerWeapon.GoreVac() );
		await Wait( 0.6f );
		var vac = inventory.Current;
		Check( vac is { Vacuum: true, Slot: 4 }, "the Gore Vac is not in hand after it was given" );
		if ( vac is null || !vac.Vacuum ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		Check( vac.Ammo == PlayerWeapon.StartGunk, $"the tank starts at {vac.Ammo}, not {PlayerWeapon.StartGunk}" );
		vac.RefillAmmo();
		Check( vac.Ammo == PlayerWeapon.StartGunk, "an ammo refill filled the Gore Vac's tank" );

		var (spot, yaw) = OpenLane( 14f );
		var ahead = new Vector3( -MathF.Sin( MathX.Rad( yaw ) ), 0f, -MathF.Cos( MathX.Rad( yaw ) ) );
		var right = ahead.Cross( Vector3.Up );
		var gore = GoreSystem.Get( this );
		var scene = GetTree().CurrentScene;
		void Stand()
		{
			player.GlobalPosition = spot + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
		}
		void LookAt( Vector3 at )
		{
			var to = (at - player.Camera.GlobalPosition).Normalized();
			player.EyeAngles = new Angles( MathX.Deg( -MathF.Asin( to.Y ) ), MathX.Deg( MathF.Atan2( -to.X, -to.Z ) ), 0f );
		}
		Vector3 Floor( Vector3 at ) => Nav.Closest( player, at );
		// Where a creature is (it may have been sucked apart and freed meanwhile).
		Func<Vector3> At( Node3D n, Vector3 fallback ) => () => IsInstanceValid( n ) ? n.GlobalPosition : fallback;
		// Hold the trigger (or reload, to blow) for a while, keeping the nozzle on something; sucking, the player is
		// kept in place so the pull on it doesn't carry it off.
		async Task Hold( Func<Vector3> target, float seconds, bool blow = false )
		{
			if ( blow ) vac.ForceBlow = true; else vac.ForceSuck = true;
			for ( var t = 0f; t < seconds; t += 0.05f )
			{
				if ( !blow ) Stand();
				LookAt( target() );
				await Wait( 0.05f );
			}
			vac.ForceSuck = vac.ForceBlow = false;
		}
		async Task FillTank()
		{
			vac.InfiniteAmmo = true;
			await Wait( 0.05f );
			vac.InfiniteAmmo = false;
		}

		// ---- Gibs and puddles ----
		Stand();
		var colours = new[] { new Color( 0.4f, 1f, 0.2f ), new Color( 1f, 0.3f, 0.6f ), new Color( 0.3f, 0.7f, 1f ) };
		for ( var i = 0; i < 9; i++ )
		{
			var at = Floor( spot + ahead * Rng.Float( 2.5f, 5f ) + right * Rng.Float( -1.2f, 1.2f ) ) + Vector3.Up * 0.4f;
			gore.ThrowBlob( at, Vector3.Up, colours[i % 3].Darkened( 0.2f ), colours[i % 3], Rng.Float( 1.8f, 3f ) );
			if ( gore.TraceWorld( at + Vector3.Up, at + Vector3.Down * 3f ) is { } floor ) gore.GooSplat( floor.Position, floor.Normal, Rng.Float( 0.8f, 1.4f ), colours[i % 3] );
		}
		await Wait( 1.5f );
		var gunkBefore = vac.Ammo;
		var swallowedBefore = PlayerWeapon.Swallowed;
		var slurpedBefore = PlayerWeapon.Slurped;
		Vector3 GibCentre()
		{
			var loose = gore.Gibs.Where( g => g.GlobalPosition.DistanceTo( player.GlobalPosition ) < 9f ).ToList();
			return loose.Count > 0 ? loose.Aggregate( Vector3.Zero, ( a, g ) => a + g.GlobalPosition ) / loose.Count : spot + ahead * 4f;
		}
		await Hold( GibCentre, 0.35f );
		vac.ForceSuck = true;
		await Shot( "gorevac_caught1" );
		await Hold( GibCentre, 0.3f );
		vac.ForceSuck = true;
		await Shot( "gorevac_caught2" );
		await Hold( GibCentre, 0.3f );
		vac.ForceSuck = true;
		await Shot( "gorevac_vortex" );
		await Hold( GibCentre, 2.2f );
		await Hold( () => Floor( spot + ahead * 3.5f ), 0.7f );
		vac.ForceSuck = true;
		await Wait( 0.05f );
		await Shot( "gorevac_suck" );
		await Hold( () => Floor( spot + ahead * 3.5f ), 1.3f );
		var swallowed = PlayerWeapon.Swallowed - swallowedBefore;
		var slurped = PlayerWeapon.Slurped - slurpedBefore;
		GD.Print( $"[autotest] gorevac: swallowed {swallowed} gibs and {slurped} puddles, tank {gunkBefore} -> {vac.Ammo}" );
		Check( swallowed >= 3, $"the Gore Vac swallowed only {swallowed} of the gibs in front of it" );
		Check( slurped >= 1, "the Gore Vac slurped up no puddles" );
		Check( vac.Ammo > gunkBefore, "the tank did not fill while sucking" );
		await Wait( 0.1f );
		Check( !vac.Sucking, "the Gore Vac kept sucking after the trigger was let go" );

		// ---- A full tank is heavy ----
		await FillTank();
		await Wait( 0.1f );
		var baseWalk = player.GetNodeOrNull<PlayerMovement>( "PlayerMovement" )?.MoveSpeed ?? player.WalkSpeed;
		Check( player.WalkSpeed < baseWalk * 0.85f, $"a full tank did not slow the player ({player.WalkSpeed:0.00} of {baseWalk:0.00} m/s)" );
		await Shot( "gorevac_full" );

		// ---- Dragging enemies in by weight ----
		var crab = WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Crab, Floor( spot + ahead * 6.5f ) + Vector3.Up * 0.3f, spot ) as Creature;
		var bloober = WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Bloober, Floor( spot + ahead * 6.5f + right * 1.5f ) + Vector3.Up * 1f, spot ) as Creature;
		await Wait( 1f );
		if ( crab is not null && bloober is not null )
		{
			Stand();
			var crabFrom = crab.GlobalPosition.DistanceTo( player.GlobalPosition );
			var blooberFrom = bloober.GlobalPosition.DistanceTo( player.GlobalPosition );
			await Hold( () => (At( crab, spot + ahead * 3f )() + At( bloober, spot + ahead * 3f )()) * 0.5f, 1.2f );
			await Shot( "gorevac_drag" );
			var crabMoved = crabFrom - crab.GlobalPosition.DistanceTo( player.GlobalPosition );
			var blooberMoved = blooberFrom - bloober.GlobalPosition.DistanceTo( player.GlobalPosition );
			GD.Print( $"[autotest] gorevac: dragged the crab {crabMoved:0.00} m and the Bloober {blooberMoved:0.00} m closer in 1.2 s" );
			Check( crabMoved > 0.8f, $"the suction dragged the crab only {crabMoved:0.00} m" );
			Check( blooberMoved > 0.8f, $"the suction dragged the Bloober only {blooberMoved:0.00} m" );

			// ---- A limb shot half off is torn away ----
			if ( crab is PartCreature body && !crab.IsDead )
			{
				var limb = body.Limbs().OrderByDescending( l => l.MaxHealth ).First();
				var limbsBefore = body.Limbs().Count;
				body.ReceiveShot( limb.Index, limb.Health * 0.7f, ahead, limb.Go.GlobalPosition, player.Camera.GlobalPosition, null, false );
				var ripped = PlayerWeapon.LimbsRipped;
				await Hold( At( crab, spot + ahead * 3f ), 1f );
				Check( PlayerWeapon.LimbsRipped > ripped, "the suction tore no half-shot limb off the crab" );
				Check( !IsInstanceValid( crab ) || crab.IsDead || body.Limbs().Count < limbsBefore, "the crab still has every limb after one was torn off" );
			}

			// ---- A weak crab at the nozzle is sucked apart ----
			if ( IsInstanceValid( crab ) && !crab.IsDead )
			{
				crab.Health.OnDamage( new DamageInfo { Damage = crab.Health.Current - 20f, Position = crab.GlobalPosition, Origin = crab.GlobalPosition } );
				var eaten = PlayerWeapon.EnemiesSwallowed;
				await Hold( At( crab, spot + ahead * 3f ), 3f );
				Check( PlayerWeapon.EnemiesSwallowed > eaten, "a weak crab dragged to the nozzle was not sucked apart" );
			}
		}
		else Check( false, "no crab or Bloober to drag" );
		// Out of the way of the later shots (it was dragged right up to the nozzle).
		foreach ( var c in new[] { crab, bloober } )
			if ( c is not null && IsInstanceValid( c ) && !c.IsDead ) PlayerPerks.Hurt( c, 9999f, c.GlobalPosition, c.GlobalPosition, null );
		await Wait( 0.5f );
		foreach ( var g in gore.Gibs.ToList() ) g.QueueFree();

		// ---- A starfish torn off the floor ----
		var prop = new Node3D { Name = "Test Starfish" };
		scene.AddChild( prop );
		prop.GlobalPosition = Floor( spot + ahead * 3f );
		prop.AddChild( new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.12f, Height = 0.08f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color( 1f, 0.4f, 0.3f ) } } );
		prop.AddToGroup( SeaDressing.LooseGroup );
		var torn = PlayerWeapon.PropsTorn;
		await Hold( () => prop.GlobalPosition, 1.2f );
		Check( PlayerWeapon.PropsTorn > torn, "the suction did not tear the starfish off the floor" );

		// ---- A mine far off is out of reach (only close ones can be torn off) ----
		var farMine = SeaMine.PlaceAt( scene, Floor( spot + ahead * 13f ), 1.2f );
		await Wait( 0.2f );
		await Hold( () => farMine.BallPosition, 1.5f );
		Check( !vac.HasMine && !farMine.Gone, $"a mine 13 m away was torn off (reach is {PlayerWeapon.MineReach} m)" );
		farMine.QueueFree();
		await Wait( 0.1f );

		// ---- A mine torn off its chain, loaded and spat ----
		var mine = SeaMine.PlaceAt( scene, Floor( spot + ahead * 5f ), 1.2f );
		await Wait( 0.2f );
		await Hold( () => mine is not null && IsInstanceValid( mine ) ? mine.BallPosition : spot + ahead * 2f, 2.5f );
		Check( vac.HasMine, "the Gore Vac did not tear the mine off and load it" );
		var target = WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Crab, Floor( spot + ahead * 10f ) + Vector3.Up * 0.3f, spot ) as Creature;
		await Wait( 0.8f );
		if ( target is not null && vac.HasMine )
		{
			Stand();
			LookAt( target.GlobalPosition + Vector3.Up * 0.3f );
			await Wait( 0.1f );
			var blasts = GoreBall.MineBlasts;
			var gunk = vac.Ammo;
			vac.TestSpit( false );
			Check( vac.Ammo == gunk && !vac.HasMine, "spitting the mine used gunk, or left it loaded" );
			for ( var i = 0; i < 50 && GoreBall.MineBlasts == blasts; i++ ) await Wait( 0.05f );
			await Shot( "gorevac_mine" );
			Check( GoreBall.MineBlasts > blasts, "the spat mine never went off" );
			Check( !IsInstanceValid( target ) || target.IsDead || target.Health.Current < target.Health.MaxHealth, "the spat mine did not hurt the crab" );
		}

		// ---- A shot at the floor goes off as a mini explosion ----
		Stand();
		await FillTank();
		LookAt( Floor( spot + ahead * 4f ) );
		await Wait( 0.1f );
		var floorBursts = GoreBall.Bursts;
		vac.TestSpit( false );
		await Wait( 0.6f );
		Check( GoreBall.Bursts > floorBursts, "a shot at the floor did not go off" );

		// ---- Easy to land: a shot aimed beside a crab still gets it (aim assist, homing, proximity fuse) ----
		var crab2 = WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Crab, Floor( spot + ahead * 12f ) + Vector3.Up * 0.3f, spot ) as Creature;
		await Wait( 1f );
		if ( crab2 is not null )
		{
			Stand();
			LookAt( crab2.GlobalPosition + Vector3.Up * 0.5f + right * 1.3f );   // 1.3 m off to the side
			await Wait( 0.2f );
			var healthBefore = crab2.Health.Current;
			var gunk = vac.Ammo;
			var bursts = GoreBall.Bursts;
			vac.TestSpit( false );
			Check( vac.Ammo == gunk - PlayerWeapon.BallCost, $"a shot took {gunk - vac.Ammo} gunk, not {PlayerWeapon.BallCost}" );
			for ( var i = 0; i < 40 && GoreBall.Bursts == bursts; i++ ) await Wait( 0.02f );
			await Shot( "gorevac_splat" );
			GD.Print( $"[autotest] gorevac: a shot aimed 1.3 m beside a crab went off {GoreBall.LastBurst.DistanceTo( crab2.GlobalPosition ):0.0} m from it ({GoreBall.LastHitName})" );
			await Wait( 0.1f );
			Check( !IsInstanceValid( crab2 ) || crab2.IsDead || crab2.Health.Current < healthBefore, "a shot aimed just beside a crab missed it" );
		}

		// ---- The huge shot: a big blast with bomblets, hurts a group, kicks the player back ----
		var group = new List<Creature>();
		for ( var i = 0; i < 3; i++ )
			if ( WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Crab, Floor( spot + ahead * 11f + right * (i - 1) * 1.6f ) + Vector3.Up * 0.3f, spot ) is Creature c ) group.Add( c );
		await Wait( 1f );
		Stand();
		LookAt( Floor( spot + ahead * 6.5f ) );
		await Wait( 0.2f );
		var before = group.Select( c => c.Health.Current ).ToList();
		var splits = GoreBall.Splits;
		await FillTank();
		var start = player.GlobalPosition;
		vac.TestSpit( true );
		Check( vac.Ammo == 0, $"the huge spit left {vac.Ammo} gunk in the tank" );
		Check( vac.LastKick > 8f, $"the huge spit kicked the player back only {vac.LastKick:0.0} m/s" );
		for ( var i = 0; i < 60 && GoreBall.Splits == splits; i++ ) await Wait( 0.05f );
		await Wait( 0.05f );
		await Shot( "gorevac_bigsplat" );
		var pushedBack = (start - player.GlobalPosition).Dot( ahead );
		GD.Print( $"[autotest] gorevac: the huge spit pushed the player {pushedBack:0.00} m back" );
		Check( pushedBack > 0.3f, $"the huge spit moved the player only {pushedBack:0.00} m back" );
		Check( GoreBall.Splits > splits, "the huge ball threw no bomblets" );
		await Wait( 1.2f );
		var hurt = group.Where( ( c, i ) => !IsInstanceValid( c ) || c.IsDead || c.Health.Current < before[i] ).Count();
		GD.Print( $"[autotest] gorevac: the huge spit hurt {hurt} of {group.Count} crabs" );
		Check( hurt >= 2, $"the huge spit hurt only {hurt} of the {group.Count} crabs round where it burst" );

		// ---- Blowing shoves the player back ----
		Stand();
		await Wait( 0.3f );
		var blowFrom = player.GlobalPosition;
		await Hold( () => player.Camera.GlobalPosition + ahead * 5f, 1f, blow: true );
		var blown = (blowFrom - player.GlobalPosition).Dot( ahead );
		GD.Print( $"[autotest] gorevac: blowing for 1 s pushed the player {blown:0.00} m back" );
		Check( blown > 0.5f, $"blowing moved the player only {blown:0.00} m back" );

		// ---- The station, from the front ----
		if ( station is not null && shots is not null )
		{
			var front = station.GetParent<Node3D>();
			var face = (spot - front.GlobalPosition).Flat().Normalized();
			FreeView( front.GlobalPosition + face * 4f + Vector3.Up * 2f, front.GlobalPosition + Vector3.Up * 1.4f );
			await Wait( 0.5f );
			await Shot( "gorevac_station" );
			DevFlags.FreeCam = false;
		}
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
