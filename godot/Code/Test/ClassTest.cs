using Godot;

/// <summary>
/// --classes: the robots. Ram-Bot starts with the rifle and shotgun and the weapon stations are hidden; picking
/// Vac-Bot swaps in the Gore Vac and Bubble Cannon (the old guns' models go); a bubble catches a crab, which floats
/// up, and the right click pops it (hurting it), and it drops back and lands; a bubble at a heavy enemy splashes;
/// mouse 4 blinks (burning tank gunk for more distance) instead of surging; picking Ram-Bot brings the guns back.
/// With --shots: pictures of the picker, a trapped crab and the gun.
/// </summary>
public partial class AutoTest
{
	async Task ClassTest()
	{
		var me = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		me.Health.Invulnerable = true;
		DevFlags.FreezeEnemies = false;
		if ( WaveDirector.Current is { } director ) director.SpawningPaused = true;
		foreach ( var laid in SeaMine.All.ToList() ) laid.QueueFree();
		RobotClass.Pick( RobotClass.RamBot, player );
		await Wait( 0.4f );

		var picker = ClassPicker.Find( GetTree() );
		Check( picker is not null, "there is no robot picker" );
		if ( shots is not null && picker is not null )
		{
			picker.SetOpen( true );
			await Wait( 0.3f );
			await Shot( "classes_picker" );
			picker.Choose( RobotClass.RamBot );
			Check( !ClassPicker.IsOpen && !GetTree().Paused, "picking a robot left the picker open or the game paused" );
		}

		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 0.5f );

		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		string Kit() => string.Join( ",", inventory.Weapons.Select( w => w.Name.ToString() ) );
		Check( Kit() == "Rifle,Shotgun", $"Ram-Bot holds {Kit()}, not the rifle and shotgun" );
		var weaponStations = GetTree().GetNodesInGroup( "buy_station" ).OfType<Node3D>()
			.Where( b => b.GetNodeOrNull<BuyStation>( "BuyStation" )?.Kind is BuyStationKind.GoreVac or BuyStationKind.Trident ).ToList();
		Check( weaponStations.Count == 0, $"{weaponStations.Count} weapon stations in the level (weapons come with the robot)" );

		// ---- Vac-Bot ----
		RobotClass.Pick( RobotClass.VacBot, player );
		await Wait( 0.6f );
		Check( Kit() == "GoreVac,BubbleCannon", $"Vac-Bot holds {Kit()}, not the Gore Vac and Bubble Cannon" );
		Check( inventory.Current?.Vacuum ?? false, "Vac-Bot doesn't start with the Gore Vac in hand" );
		var models = player.Camera.FindChildren( "*", "", true, false ).OfType<ViewModel>().Count( v => IsInstanceValid( v ) && !v.IsQueuedForDeletion() );
		Check( models <= 2, $"{models} gun models under the camera after the swap (the old ones stayed)" );

		var (spot, yaw) = OpenLane( 16f );
		var ahead = new Vector3( -MathF.Sin( MathX.Rad( yaw ) ), 0f, -MathF.Cos( MathX.Rad( yaw ) ) );
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
		Stand();

		// ---- A bubble catches a crab ----
		var cannon = inventory.Weapons.First( w => w.Bubbles );
		inventory.Select( cannon, force: true );
		await Wait( 0.5f );
		var crabAt = Nav.Closest( player, spot + ahead * 7f ) + Vector3.Up * 0.3f;
		var crab = WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Crab, crabAt, spot ) as Creature;
		Check( crab is not null, "no crab to bubble" );
		if ( crab is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		DevFlags.FreezeEnemies = true;
		await Wait( 0.8f );
		var groundY = crab.GlobalPosition.Y;
		var before = BubbleTrap.Trapped;
		Stand();
		LookAt( crab.GlobalPosition + Vector3.Up * 0.4f );
		cannon.TestFire();
		for ( var t = 0f; t < 2f && BubbleTrap.Trapped == before; t += 0.05f ) await Wait( 0.05f );
		Check( BubbleTrap.Trapped > before && crab.Bubbled, "the bubble didn't catch the crab" );
		DevFlags.FreezeEnemies = false;   // the bubble alone must hold it now
		await Wait( 1.4f );
		var lifted = crab.GlobalPosition.Y - groundY;
		Check( lifted > 1f, $"the bubbled crab only rose {lifted:0.00} m" );
		if ( shots is not null ) { LookAt( crab.GlobalPosition ); await Wait( 0.1f ); await Shot( "classes_bubbled_crab" ); }
		var health = crab.Health.Current;
		var popped = BubbleTrap.Popped;
		BubbleTrap.PopAll( player );
		Check( BubbleTrap.Popped == popped + 1, "the right click didn't pop the bubble" );
		var landed = BubbleTrap.Landed;
		for ( var t = 0f; t < 2f && BubbleTrap.Landed == landed && IsInstanceValid( crab ) && !crab.IsDead; t += 0.05f ) await Wait( 0.05f );
		Check( !IsInstanceValid( crab ) || crab.IsDead || crab.Health.Current < health, "the pop and the landing didn't hurt the crab" );
		Check( !IsInstanceValid( crab ) || crab.IsDead || (BubbleTrap.Landed > landed && !crab.Bubbled && MathF.Abs( crab.GlobalPosition.Y - groundY ) < 0.3f), "the crab didn't drop back to the floor" );
		if ( IsInstanceValid( crab ) ) crab.QueueFree();

		// ---- A bubble at something heavy just splashes ----
		DevFlags.FreezeEnemies = true;
		var shark = WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.RockyShark, Nav.Closest( player, spot + ahead * 8f ) + Vector3.Up * 1f, spot ) as Creature;
		if ( shark is not null && !shark.Pullable )
		{
			await Wait( 0.8f );
			Stand();
			var splashes = BubbleShot.Splashed;
			var trapped = BubbleTrap.Trapped;
			var sharkHealth = shark.Health.Current;
			var target = CreaturePart.All.Where( p => IsInstanceValid( p ) && p.Creature == shark ).Select( p => p.GlobalPosition ).DefaultIfEmpty( shark.GlobalPosition ).First();
			LookAt( target );
			cannon.TestFire();
			for ( var t = 0f; t < 2f && BubbleShot.Splashed == splashes; t += 0.05f ) await Wait( 0.05f );
			Check( BubbleTrap.Trapped == trapped, "a heavy enemy was caught in a bubble" );
			Check( shark.Health.Current < sharkHealth, "the bubble's splash didn't hurt the heavy enemy" );
		}
		if ( shark is not null && IsInstanceValid( shark ) ) shark.QueueFree();
		DevFlags.FreezeEnemies = false;
		if ( shots is not null ) { LookAt( spot + ahead * 5f ); await Wait( 0.3f ); await Shot( "classes_bubble_cannon" ); }

		// ---- A bubble bounces off the floor once ----
		cannon.InfiniteAmmo = true;
		Stand();
		var bounced = BubbleShot.Bounced;
		LookAt( spot + ahead * 3f + Vector3.Down * 0.5f );
		await Wait( 0.45f );
		cannon.TestFire();
		for ( var t = 0f; t < 1.5f && BubbleShot.Bounced == bounced; t += 0.05f ) await Wait( 0.05f );
		Check( BubbleShot.Bounced > bounced, "a bubble fired at the floor didn't bounce" );
		await Wait( 2f );   // let it finish before the next crabs come
		foreach ( var flying in GetTree().CurrentScene.GetChildren().OfType<BubbleShot>() ) flying.QueueFree();

		// ---- Crabs in a row: a Mega Bubble wraps several up, a second bubble inflates one, a pop chains ----
		var row = new List<Creature>();
		var side = ahead.Cross( Vector3.Up );
		for ( var i = 0; i < 3; i++ )
			if ( WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Crab, Nav.Closest( player, spot + ahead * (5f + i * 1.6f) + side * (i - 1) * 0.3f ) + Vector3.Up * 0.3f, spot ) is Creature c ) row.Add( c );
		DevFlags.FreezeEnemies = true;
		await Wait( 0.8f );
		Stand();
		var trappedBefore = BubbleTrap.Trapped;
		var mega = BubbleShot.MegaFired;
		LookAt( row[1].GlobalPosition + Vector3.Up * 0.35f );
		await Wait( 0.45f );
		cannon.TestFireCharged();
		if ( shots is not null ) { await Wait( 0.2f ); await Shot( "classes_mega_flight" ); }
		for ( var t = 0f; t < 2.5f && BubbleTrap.Trapped - trappedBefore < 2; t += 0.05f ) await Wait( 0.05f );
		Check( BubbleShot.MegaFired == mega + 1, "the charged shot wasn't a Mega Bubble" );
		Check( BubbleTrap.Trapped - trappedBefore >= 2, $"the Mega Bubble only caught {BubbleTrap.Trapped - trappedBefore} of a row of 3 crabs" );
		DevFlags.FreezeEnemies = false;
		await Wait( 0.6f );   // up out of the way of the others
		if ( shots is not null ) { LookAt( row[1].GlobalPosition + Vector3.Up * 0.8f ); await Wait( 0.05f ); await Shot( "classes_mega_trapped" ); }
		var held = row.Where( c => IsInstanceValid( c ) && !c.IsDead && c.Bubbled ).OrderByDescending( c => c.GlobalPosition.Y ).ToList();
		foreach ( var c in row ) if ( IsInstanceValid( c ) && !c.Bubbled ) c.QueueFree();   // nothing in the way of the next shots
		if ( held.Count > 0 )
		{
			var trap = BubbleTrap.Of( held[0] );
			var riseBefore = trap?.TargetRise ?? 0f;
			var inflated = BubbleTrap.Inflated;
			await Wait( 0.45f );
			Stand();
			LookAt( held[0].GlobalPosition + Vector3.Up * 0.3f );
			cannon.TestFire();
			for ( var t = 0f; t < 1.5f && BubbleTrap.Inflated == inflated; t += 0.05f ) await Wait( 0.05f );
			// (It may land on the crab beside it, also in a bubble: either one swelling counts.)
			Check( BubbleTrap.Inflated > inflated && BubbleTrap.Live.Any( l => l.TargetRise > BubbleTrap.MegaRise ), "a bubble fired into a trapped crab didn't inflate its bubble" );
		}
		if ( held.Count >= 2 )
		{
			// Shoot one: its pop sets the bubble next to it off.
			var chained = BubbleTrap.Chained;
			var part = CreaturePart.All.First( p => IsInstanceValid( p ) && p.Creature == held[0] );
			part.ReceiveShot( 30f, part, ahead, part.GlobalPosition, spot, me, true );
			if ( shots is not null ) { LookAt( held[0].GlobalPosition ); await Wait( 0.06f ); await Shot( "classes_pop" ); }
			for ( var t = 0f; t < 1f && BubbleTrap.Chained == chained; t += 0.05f ) await Wait( 0.05f );
			Check( BubbleTrap.Chained > chained, "popping one bubble didn't set off the one beside it" );
		}
		await Wait( 2.5f );
		Check( row.All( c => !IsInstanceValid( c ) || c.IsDead || !c.Bubbled ), "a crab was left in a bubble" );
		if ( shots is not null ) { LookAt( spot + ahead * 5f ); await Wait( 0.1f ); }
		foreach ( var c in row ) if ( IsInstanceValid( c ) ) c.QueueFree();
		cannon.InfiniteAmmo = false;

		// ---- Mouse 4 blinks ----
		var jetpack = player.GetNode<Jetpack>( "Jetpack" );
		var vac = inventory.Weapons.First( w => w.Vacuum );
		await Wait( 2.5f );   // the power cell refills
		Stand();
		LookAt( spot + ahead * 10f + Vector3.Up * 1.4f );
		var gunk = vac.Ammo;
		var blinks = Jetpack.Blinks;
		var start = player.GlobalPosition;
		jetpack.TestSurge();
		await Wait( 0.6f );
		var moved = player.GlobalPosition.DistanceTo( start );
		Check( Jetpack.Blinks == blinks + 1, "mouse 4 didn't blink for Vac-Bot" );
		Check( !jetpack.Charging, "Vac-Bot's mouse 4 wound up a surge" );
		Check( moved > 5f, $"the gunk blink only went {moved:0.0} m" );
		Check( vac.Ammo == gunk - jetpack.BlinkGunk, $"the blink took {gunk - vac.Ammo} gunk, not {jetpack.BlinkGunk}" );
		Check( jetpack.Power > 0f && !jetpack.Stalled, "one blink emptied the power cell" );

		// ---- Back to Ram-Bot ----
		RobotClass.Pick( RobotClass.RamBot, player );
		await Wait( 0.5f );
		Check( Kit() == "Rifle,Shotgun", $"Ram-Bot again holds {Kit()}" );
		Check( BubbleTrap.Live.Count == 0, "a bubble outlived the Bubble Cannon" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
