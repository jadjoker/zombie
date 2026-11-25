using Godot;

/// <summary>
/// The puffer bomber (--puffer): it swims at the player, lights its fuse in reach (swelling, spines up, the ticking
/// and the warning ring), creeps on, then bursts: the blast and the ring of spines hurt a player who stayed put; one who
/// backs off past the ring is clear of the blast. Shot early next to other enemies, it pops where it is and hurts them.
/// With --shots (a window): close up, swelling, the warning ring, the burst.
/// </summary>
public partial class AutoTest
{
	async Task PufferTest()
	{
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		var d = WaveDirector.Current;
		d.SpawningPaused = true;
		AllyCommander.KeepAlly = false;
		foreach ( var ally in SeaSpider.All.Where( s => !s.IsEnemy ).ToList() ) ally.QueueFree();
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.Invulnerable = true;
		await WaitForNav();
		var (open, yaw) = OpenLane( 16f );
		void Stand() { player.GlobalPosition = open + Vector3.Up * 0.1f; player.Velocity = Vector3.Zero; player.EyeAngles = new Angles( 0f, yaw, 0f ); }
		Stand();
		var ahead = new Angles( 0f, yaw, 0f ).Forward;
		var side = ahead.Cross( Vector3.Up ).Normalized();
		Check( WaveDirector.Spawners.ContainsKey( WaveDirector.EnemyKind.Puffer ), "the wave director cannot spawn puffer bombers" );
		Check( d.PufferFromWave >= 2 && d.MaxPuffers > 0, "puffer bombers never come in a wave" );

		// Close up (frozen), then swelling with its fuse lit.
		var puffer = PufferBomber.Spawn( GetTree().CurrentScene, open + ahead * 3.5f + Vector3.Up * 1.2f, 500f );
		puffer.GlobalBasis = Shapes.LookAt( -ahead );
		await Wait( 0.2f );
		Check( puffer.Health.MaxHealth == 500f, "the puffer did not keep its health" );
		if ( shots is not null )
		{
			DevFlags.FreezeEnemies = true;
			HideHud();
			Transform3D? view = null;
			player.CameraOverride = _ => view;
			player.ThirdPerson = true;
			async Task From( string name, Vector3 offset )
			{
				var centre = puffer.Centre;
				var eye = centre + puffer.GlobalBasis * offset;
				view = new Transform3D( Shapes.LookAt( centre - eye ), eye );
				await Wait( 0.15f );
				await Shot( name );
			}
			await From( "puffer_front", new Vector3( 0.35f, 0.25f, -1.3f ) );
			await From( "puffer_side", new Vector3( 1.4f, 0.3f, 0.1f ) );
			await From( "puffer_eyes", new Vector3( 0.2f, 0.25f, -0.75f ) );
			puffer.ForceFuse();
			DevFlags.FreezeEnemies = false;
			await Wait( PufferBomber.FuseTime * 0.85f );
			DevFlags.FreezeEnemies = true;
			await From( "puffer_swollen", new Vector3( 0.6f, 0.4f, -1.9f ) );
			view = new Transform3D( Shapes.LookAt( puffer.Centre - (puffer.Centre + Vector3.Up * 7f + ahead * 4f) ), puffer.Centre + Vector3.Up * 7f + ahead * 4f );
			await Wait( 0.15f );
			await Shot( "puffer_ring" );
			DevFlags.FreezeEnemies = false;
			await Wait( 0.05f );
			view = new Transform3D( Shapes.LookAt( puffer.Centre - (puffer.Centre + side * 4f + Vector3.Up * 1.5f) ), puffer.Centre + side * 4f + Vector3.Up * 1.5f );
			for ( var t = 0f; t < 1.5f && IsInstanceValid( puffer ) && !puffer.IsDead; t += 0.02f ) await Wait( 0.02f );
			await Wait( 0.08f );
			await Shot( "puffer_burst" );
			player.CameraOverride = null;
			player.ThirdPerson = false;
			ShowHud();
			await Wait( 1f );
		}
		if ( IsInstanceValid( puffer ) && !puffer.IsDead ) puffer.Health.Kill();
		await Wait( 0.5f );
		foreach ( var s in GetTree().CurrentScene.FindChildren( "*", "", true, false ).OfType<PufferSpine>() ) s.QueueFree();

		// It swims at a player standing still, lights up in reach, and the burst hurts them.
		Stand();
		survivor.Health.Invulnerable = false;
		survivor.Health.Heal( 9999f );
		var before = survivor.Health.Current;
		var lit = PufferBomber.FusesLit;
		var bursts = PufferBomber.Bursts;
		PufferBomber.SpineHitsPlayer = 0;
		puffer = PufferBomber.Spawn( GetTree().CurrentScene, open + ahead * 8f + Vector3.Up * 1.4f, 500f );
		var start = puffer.GlobalPosition.DistanceTo( open );
		var lowest = 99f;
		var maxSwell = 0f;
		var litAt = -1f;
		for ( var t = 0f; t < 10f && IsInstanceValid( puffer ) && !puffer.IsDead; t += 0.02f )
		{
			Stand();
			var ground = Sculpt.FloorBelow( player, puffer.Centre, 10f );
			lowest = MathF.Min( lowest, puffer.Centre.Y - PufferBomber.Radius * puffer.SwellNow - ground );
			maxSwell = MathF.Max( maxSwell, puffer.SwellNow );
			if ( litAt < 0f && puffer.Fusing ) litAt = puffer.Centre.DistanceTo( open + Vector3.Up * 1.1f );
			await Wait( 0.02f );
		}
		await Wait( 0.6f );
		var lost = before - survivor.Health.Current;
		GD.Print( $"[autotest] puffer: came from {start:0.0} m, lit {PufferBomber.FusesLit - lit} at {litAt:0.0} m, swelled to {maxSwell:0.00}x, burst {PufferBomber.Bursts - bursts}, lowest {lowest:0.00} m over the floor, spines that hit {PufferBomber.SpineHitsPlayer}, health lost {lost:0}" );
		Check( PufferBomber.FusesLit > lit && litAt > 0f && litAt <= puffer.ArmRange + 0.3f, "the puffer never lit its fuse in reach" );
		Check( maxSwell > 1.7f, $"the puffer did not swell up with its fuse ({maxSwell:0.00}x)" );
		Check( PufferBomber.Bursts > bursts, "the puffer never burst" );
		Check( lost > PufferBomber.BlastDamage * 0.4f, $"standing still by a bursting puffer cost only {lost:0}" );
		Check( PufferBomber.SpineHitsPlayer > 0, "no spine hit the player standing next to it" );
		Check( lowest > -0.05f, $"the puffer went into the floor ({lowest:0.00} m)" );

		// Backing off once its fuse is lit gets you clear of the blast.
		foreach ( var s in GetTree().CurrentScene.FindChildren( "*", "", true, false ).OfType<PufferSpine>() ) s.QueueFree();
		Stand();
		survivor.Health.Heal( 9999f );
		puffer = PufferBomber.Spawn( GetTree().CurrentScene, open + ahead * 2.4f + Vector3.Up * 1.2f, 500f );
		await Wait( 0.1f );
		puffer.ForceFuse();
		var fled = open - ahead * (PufferBomber.SpineRange + 1.5f);
		fled = Nav.Closest( player, fled );
		before = survivor.Health.Current;
		for ( var t = 0f; t < 3f && IsInstanceValid( puffer ) && !puffer.IsDead; t += 0.02f )
		{
			player.GlobalPosition = fled + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			await Wait( 0.02f );
		}
		await Wait( 0.6f );
		var fledLoss = before - survivor.Health.Current;
		GD.Print( $"[autotest] puffer: backed off {fled.DistanceTo( open ):0.0} m, lost {fledLoss:0}" );
		Check( fledLoss < 1f, $"backing off a lit puffer still cost {fledLoss:0}" );
		survivor.Health.Invulnerable = true;

		// Shot early next to other enemies: it pops where it is and they get hurt.
		Stand();
		var far = open + ahead * 13f;
		var crabs = new List<Creature>();
		for ( var i = -1; i <= 1; i += 2 )
			if ( d.SpawnAt( WaveDirector.EnemyKind.Crab, Nav.Closest( player, far + side * i * 1.1f ) + Vector3.Up * 0.3f, open ) is Creature c ) crabs.Add( c );
		DevFlags.FreezeEnemies = true;
		puffer = PufferBomber.Spawn( GetTree().CurrentScene, Nav.Closest( player, far ) + Vector3.Up * 1f, 60f );
		await Wait( 0.3f );
		var crabHealth = crabs.Select( c => c.Health.Current ).ToList();
		var blastHits = PufferBomber.BlastCreatureHits;
		PufferBomber.SpineHitsCreature = 0;
		bursts = PufferBomber.Bursts;
		puffer.ReceiveShot( 0, 1000f, ahead, puffer.Centre, open + Vector3.Up * 1.5f, survivor, true );
		await Wait( 0.6f );
		var hurt = crabs.Count( c => !IsInstanceValid( c ) || c.IsDead || c.Health.Current < crabHealth[crabs.IndexOf( c )] );
		GD.Print( $"[autotest] puffer: shot early by {crabs.Count} crabs, burst {PufferBomber.Bursts - bursts}, blast hurt {PufferBomber.BlastCreatureHits - blastHits}, spines into enemies {PufferBomber.SpineHitsCreature}, crabs hurt {hurt}" );
		Check( PufferBomber.Bursts > bursts, "a puffer shot dead did not burst" );
		Check( crabs.Count == 2 && hurt == 2, $"a puffer bursting by two crabs hurt {hurt} of them" );
		DevFlags.FreezeEnemies = false;
		foreach ( var c in crabs ) if ( IsInstanceValid( c ) && !c.IsDead ) c.Health.Kill();
		await Wait( 0.5f );
		Check( PufferBomber.All.Count == 0, $"{PufferBomber.All.Count} puffers left" );

		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	void HideHud()
	{
		foreach ( var n in GetTree().Root.FindChildren( "*", "", true, false ) )
			if ( n is CanvasLayer cl ) cl.Visible = false;
			else if ( n is ViewModel or ViewModelAxe && n is Node3D vm ) vm.Visible = false;
	}

	void ShowHud()
	{
		foreach ( var n in GetTree().Root.FindChildren( "*", "", true, false ) )
			if ( n is CanvasLayer cl ) cl.Visible = true;
	}
}
