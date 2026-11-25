using Godot;

/// <summary>
/// The cleaner wrasse (--cleaner): it links to a hurt crab, heals it at 7% a second and builds its shield to full in about
/// two seconds; the shield takes health hits and shots before the crab does, and shatters when used up. A second wrasse
/// leaves that crab to the first. Aimed at, it darts aside and stays in reach; dead, its shield fades away. Wave rules:
/// from wave 6, at most 2, the shield grows with the wave. With --shots (a window): the shield, the beam, the fish.
/// </summary>
public partial class AutoTest
{
	async Task CleanerTest()
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
		var ahead = new Angles( 0f, yaw, 0f ).Forward;
		var side = ahead.Cross( Vector3.Up ).Normalized();
		void Stand( Angles look ) { player.GlobalPosition = open + Vector3.Up * 0.1f; player.Velocity = Vector3.Zero; player.EyeAngles = look; }
		var away = new Angles( 0f, yaw + 180f, 0f );
		Stand( away );

		Check( WaveDirector.Spawners.ContainsKey( WaveDirector.EnemyKind.Cleaner ), "the wave director cannot spawn cleaner wrasses" );
		Check( d.CleanerFromWave == 6 && d.MaxCleaners == 2, $"cleaner wrasses come from wave {d.CleanerFromWave}, {d.MaxCleaners} at most (want 6 and 2)" );
		Check( MathF.Abs( CleanerWrasse.HealShare - 0.07f ) < 1e-4f && CleanerWard.FillTime == 2f, "the wrasse's heal or shield fill time is off" );
		var wave = d.Wave;
		d.Wave = d.CleanerFromWave;
		var first = d.CleanerShield;
		d.Wave = 20;
		var later = d.CleanerShield;
		d.Wave = wave;
		GD.Print( $"[autotest] cleaner: shield {first:0} on wave 6, {later:0} on wave 20" );
		Check( first == 100f && later > first, $"the shield should be 100 on wave 6 and grow ({first:0}, {later:0} on wave 20)" );

		var crab = d.SpawnAt( WaveDirector.EnemyKind.Crab, Nav.Closest( player, open + ahead * 7f ), open ) as Creature;
		Check( crab is not null, "no crab to clean" );
		if ( crab is null ) return;
		crab.Stun( 9999f );
		await Wait( 0.3f );
		crab.Health.Current = crab.Health.MaxHealth * 0.3f;
		var w = CleanerWrasse.Spawn( GetTree().CurrentScene, CleanerWard.CentreOf( crab ) + side * 3f + Vector3.Up * 0.6f, 80f );
		w.ShieldMax = 100f;
		var links = CleanerWrasse.Links;
		for ( var t = 0f; t < 6f && !w.Linked; t += 0.05f ) { Stand( away ); await Wait( 0.05f ); }
		Check( w.Linked && w.Patient == crab && CleanerWrasse.Links > links, "the wrasse never linked to the hurt crab" );
		var h0 = crab.Health.Current;
		var t0 = Clock.Now;
		await Wait( 1f );
		var dt = (float)(Clock.Now - t0);
		var rate = (crab.Health.Current - h0) / crab.Health.MaxHealth / dt;
		var ward = crab.Ward;
		var mid = ward?.Fraction ?? 0f;
		await Wait( 1.4f );
		var full = ward?.Amount ?? 0f;
		GD.Print( $"[autotest] cleaner: heals {rate * 100f:0.0}% a second, shield {mid * 100f:0}% after {dt:0.0} s, {full:0} after {dt + 1.4f:0.0} s" );
		Check( rate > 0.055f && rate < 0.085f, $"the wrasse heals {rate * 100f:0.0}% a second (want 7%)" );
		Check( ward is not null && mid > 0.3f && mid < 0.8f, $"the shield was {mid * 100f:0}% a second in (it should take two seconds to fill)" );
		Check( full >= 99f, $"the shield reached {full:0} of 100" );

		if ( shots is not null )
		{
			DevFlags.FreezeEnemies = true;
			HideHud();
			Transform3D? view = null;
			player.CameraOverride = _ => view;
			player.ThirdPerson = true;
			async Task From( string name, Vector3 focus, Vector3 eye )
			{
				view = new Transform3D( Shapes.LookAt( focus - eye ), eye );
				await Wait( 0.15f );
				await Shot( name );
			}
			var c = CleanerWard.CentreOf( crab );
			await From( "cleaner_shield", c, c + (w.Centre - c).Flat().Cross( Vector3.Up ).Normalized() * 4f + Vector3.Up * 1.2f + (c - w.Centre).Flat().Normalized() * 1.5f );
			await From( "cleaner_side", w.Centre, w.Centre + w.GlobalBasis.X * 1.1f + Vector3.Up * 0.15f );
			await From( "cleaner_front", w.Centre, w.Centre - w.GlobalBasis.Z * 1f + w.GlobalBasis.X * 0.35f + Vector3.Up * 0.2f );
			crab.Health.OnDamage( new DamageInfo { Damage = 10f, Position = c + (open - c).Normalized() * 1f, Origin = open } );
			await From( "cleaner_ripple", c, open + Vector3.Up * 1.6f + side * 0.5f );
			player.CameraOverride = null;
			player.ThirdPerson = false;
			ShowHud();
			player.EyeAngles = WrasseAim( c );
			await Wait( 0.2f );
			await Shot( "cleaner_bar" );
			DevFlags.FreezeEnemies = false;
			Stand( away );
			await Wait( 1.2f );   // the shield fills again
		}

		// The shield takes hits first: a health hit, then a shot, then a hit too big for it.
		var before = crab.Health.Current;
		var amount = ward?.Amount ?? 0f;
		crab.Health.OnDamage( new DamageInfo { Damage = 40f, Position = CleanerWard.CentreOf( crab ), Origin = open } );
		var afterHit = ward?.Amount ?? 0f;
		Check( crab.Health.Current >= before - 0.01f, $"a 40 hit on a shielded crab took {before - crab.Health.Current:0} health" );
		Check( MathF.Abs( amount - afterHit - 40f ) < 1f, $"the shield took {amount - afterHit:0} of a 40 hit" );
		before = crab.Health.Current;
		crab.ReceiveShot( 0, 30f, ahead, CleanerWard.CentreOf( crab ), open + Vector3.Up * 1.5f, survivor, true );
		var afterShot = ward?.Amount ?? 0f;
		Check( crab.Health.Current >= before - 0.01f && MathF.Abs( afterHit - afterShot - 30f ) < 1f, $"a 30 shot on the shield took {afterHit - afterShot:0} shield and {before - crab.Health.Current:0} health" );
		var breaks = CleanerWard.Breaks;
		before = crab.Health.Current;
		crab.Health.OnDamage( new DamageInfo { Damage = afterShot + 25f, Position = CleanerWard.CentreOf( crab ), Origin = open } );
		var through = before - crab.Health.Current;
		Check( CleanerWard.Breaks > breaks && (ward?.Amount ?? 1f) <= 0f, "the shield did not break when used up" );
		Check( MathF.Abs( through - 25f ) < 1f, $"{through:0} went through a broken shield (want 25)" );
		await Wait( 1.5f );
		Check( (ward?.Amount ?? 1f) <= 0f, "a broken shield grew straight back" );

		// A second wrasse leaves that crab to the first.
		var w2 = CleanerWrasse.Spawn( GetTree().CurrentScene, CleanerWard.CentreOf( crab ) - side * 3f + Vector3.Up * 0.6f, 80f );
		for ( var t = 0f; t < 2f; t += 0.05f ) { Stand( away ); await Wait( 0.05f ); }
		Check( w2.Patient != crab, "two wrasses cleaned the same crab" );
		w2.Health.Kill();

		// Aimed at, it darts aside and stays in reach of its patient.
		var darts = CleanerWrasse.Darts;
		var start = w.Centre;
		for ( var t = 0f; t < 1.5f && CleanerWrasse.Darts == darts; t += 0.02f ) { Stand( WrasseAim( w.Centre ) ); await Wait( 0.02f ); }
		Check( CleanerWrasse.Darts > darts, $"the wrasse never darted from a steady aim (aimed for {w.AimedFor:0.00} s)" );
		await Wait( 0.6f );
		var moved = w.Centre.DistanceTo( start );
		var reach = w.Centre.DistanceTo( CleanerWard.CentreOf( crab ) );
		GD.Print( $"[autotest] cleaner: darted {moved:0.0} m, {reach:0.0} m from its patient" );
		Check( moved > 1.5f && reach < CleanerWrasse.LinkRange + 1f, $"the dart went {moved:0.0} m, ending {reach:0.0} m from its patient" );
		Stand( away );

		// Shot dead (head shot), its shield fades away.
		w.ReceiveShot( 0, 1000f, ahead, w.Centre, open + Vector3.Up * 1.5f, survivor, true );
		await Wait( 0.2f );
		Check( !IsInstanceValid( w ) || w.IsDead, "the wrasse did not die" );
		await Wait( CleanerWard.Linger + 1.5f );
		Check( crab.Ward is null || !IsInstanceValid( crab.Ward ), "the shield stayed after its wrasse died" );
		if ( IsInstanceValid( crab ) && !crab.IsDead ) crab.Health.Kill();
		await Wait( 0.5f );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	Angles WrasseAim( Vector3 target )
	{
		var dir = (target - player.EyeTransform.Origin).Normalized();
		return new Angles( -MathX.Deg( MathF.Asin( dir.Y ) ), MathX.Deg( MathF.Atan2( -dir.X, -dir.Z ) ), 0f );
	}
}
