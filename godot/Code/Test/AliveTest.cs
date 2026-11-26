using Godot;

/// <summary>
/// The world feeling alive (--alive): a blast bursts a fish school apart and it gathers again; plants are shoved over
/// by a blast and spring back, and lean toward the Gore Vac's pull; a shot startles; a big kill leaves a goo haze.
/// Enemies cry out when they spot the player, badly hurt ones may run, a scared one gets further away, crabs far off
/// stop to pick at the sand. The areas change with the waves: the wreck's hull groans, the Abyss's lamps flicker and
/// go out, a blast shakes rubble off its roof, a leviathan crosses the skylight, and Atlantis's heart beats faster.
/// </summary>
public partial class AutoTest
{
	async Task AliveTest()
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

		// ---- The sea reacts ----
		var floaters = GetTree().Root.FindChildren( "*", "", true, false ).OfType<AlienFloaters>().FirstOrDefault();
		Check( floaters is not null, "no glow bulbs on the shore" );
		var bulb = floaters?.FindChildren( "*", "", true, false ).OfType<GlowBulb>().FirstOrDefault();
		if ( bulb is not null )
		{
			SeaStir.Blast( bulb.GlobalPosition + Vector3.Right * 1.5f, 1f );
			await Wait( 0.6f );
			var shoved = floaters.MaxPush;
			await Wait( 8f );
			GD.Print( $"[autotest] alive: a blast by the glow bulbs shoved them {shoved:0.00} m; 8 s later {floaters.MaxPush:0.00} m" );
			Check( shoved > 0.6f, "a blast did not shove the glow bulbs" );
			Check( floaters.MaxPush < 0.3f, "shoved glow bulbs did not drift back" );
		}

		var eye = player.GlobalPosition;
		var plant = Sway.Active.Where( s => IsInstanceValid( s ) && IsInstanceValid( s.Plant ) ).OrderBy( s => s.Plant.GlobalPosition.DistanceTo( eye ) ).FirstOrDefault();
		Check( plant is not null, "no swaying plants" );
		if ( plant is not null )
		{
			var at = plant.Plant.GlobalPosition;
			var tilt0 = plant.Plant.GlobalBasis.Y.Normalized();
			SeaStir.Blast( at + new Vector3( 2f, 0.5f, 0f ), 1f );
			await Wait( 0.25f );
			var lean = plant.Lean;
			var away = plant.LeanDir.Dot( Vector3.Right );
			var tilted = MathX.Deg( MathF.Acos( MathX.Clamp( plant.Plant.GlobalBasis.Y.Normalized().Dot( tilt0 ), -1f, 1f ) ) );
			await Wait( 6f );
			GD.Print( $"[autotest] alive: a blast 2 m from a plant leant it {MathX.Deg( lean ):0} deg ({tilted:0} deg drawn), away {away:0.00}; 6 s later {MathX.Deg( plant.Lean ):0.0} deg" );
			Check( lean > 0.15f && tilted > 6f, "a blast next to a plant did not shove it over" );
			Check( away < -0.5f, "a blast shoved a plant toward itself" );
			Check( plant.Lean < 0.03f, "a shoved plant did not spring back upright" );

			// The Gore Vac's pull from 6 m off: it leans toward the nozzle.
			var nozzle = at + new Vector3( 0f, 1f, 6f );
			for ( var t = 0f; t < 1.2f; t += 0.02f ) { SeaStir.Pull( nozzle, at - nozzle, 16f ); await Wait( 0.02f ); }
			var toward = plant.LeanDir.Dot( Vector3.Back );
			GD.Print( $"[autotest] alive: the Gore Vac's pull leant a plant {MathX.Deg( plant.Lean ):0} deg, toward the nozzle {toward:0.00}" );
			Check( plant.Lean > 0.1f && toward > 0.5f, "a plant did not lean toward the Gore Vac's pull" );
		}

		var seq = SeaStir.Seq;
		player.GetNode<WeaponInventory>( "WeaponInventory" ).Current?.TestFire();
		await Wait( 0.1f );
		Check( SeaStir.Seq > seq, "firing a gun did not startle the sea" );

		// A big kill leaves a goo haze; a small one does not.
		var (lane, yaw) = OpenLane( 8f );
		var ahead = new Vector3( -MathF.Sin( MathX.Rad( yaw ) ), 0f, -MathF.Cos( MathX.Rad( yaw ) ) );
		player.GlobalPosition = lane + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		player.EyeAngles = new Angles( 0f, yaw, 0f );
		await Wait( 0.3f );
		var hazes = GooHaze.Made;
		var small = d.SpawnAt( WaveDirector.EnemyKind.Crab, lane + ahead * 6f, lane ) as Creature;
		await Wait( 0.3f );
		small?.Health.ResetTo( 100f );
		small?.Health.OnDamage( new DamageInfo { Damage = 1_000_000f } );
		await Wait( 0.3f );
		var afterSmall = GooHaze.Made - hazes;
		var big = d.SpawnAt( WaveDirector.EnemyKind.Crab, lane + ahead * 6f, lane ) as Creature;
		await Wait( 0.3f );
		big?.Health.ResetTo( 900f );
		big?.Health.OnDamage( new DamageInfo { Damage = 1_000_000f } );
		await Wait( 0.3f );
		if ( shots is not null ) { await Wait( 0.7f ); await Shot( "alive_haze" ); }
		GD.Print( $"[autotest] alive: goo hazes from a small kill {afterSmall}, from a big one {GooHaze.Made - hazes - afterSmall}" );
		Check( afterSmall == 0 && GooHaze.Made - hazes == 1, "a big kill did not leave a goo haze (or a small one did)" );

		// ---- Enemies ----
		var spots = EnemyVoices.Spots;
		var crabs = new List<Creature>();
		for ( var i = 0; i < 8; i++ )
			if ( d.SpawnAt( WaveDirector.EnemyKind.Crab, lane + ahead * 7f + new Vector3( -ahead.Z, 0f, ahead.X ) * (i - 3.5f) * 1.2f, lane ) is Creature c ) crabs.Add( c );
		DevFlags.FreezeEnemies = true;
		await Wait( 1.5f );
		GD.Print( $"[autotest] alive: {crabs.Count} crabs in sight, {EnemyVoices.Spots - spots} cried out" );
		Check( EnemyVoices.Spots - spots >= 3, "enemies in sight of the player did not cry out" );
		var scares = Creature.Scares;
		foreach ( var c in crabs ) c.Health.OnDamage( new DamageInfo { Damage = c.Health.MaxHealth * 0.8f } );
		var scared = crabs.Where( c => c.Scared ).ToList();
		GD.Print( $"[autotest] alive: hurt to 20%, {Creature.Scares - scares} of {crabs.Count} lost their nerve" );
		Check( Creature.Scares - scares >= 1, "no badly hurt enemy lost its nerve" );
		DevFlags.FreezeEnemies = false;
		if ( scared.Count > 0 )
		{
			var c = scared[0];
			var before = c.GlobalPosition.Flat().DistanceTo( player.GlobalPosition.Flat() );
			await Wait( 1.3f );
			var later = IsInstanceValid( c ) ? c.GlobalPosition.Flat().DistanceTo( player.GlobalPosition.Flat() ) : before;
			GD.Print( $"[autotest] alive: a scared crab went from {before:0.0} m to {later:0.0} m away" );
			Check( later > before + 1f, "a scared crab did not back away from the player" );
		}
		d.ClearEnemies();
		await Wait( 0.5f );

		// A crab far from the player stops now and then to pick at the sand.
		var forages = JunkCrab.Forages;
		var far = Nav.Closest( player, lane + ahead * 22f );
		var lonely = d.SpawnAt( WaveDirector.EnemyKind.Crab, far, lane ) as Creature;
		for ( var t = 0f; t < 14f && JunkCrab.Forages == forages; t += 0.1f )
		{
			player.GlobalPosition = lane + Vector3.Up * 0.1f;
			if ( IsInstanceValid( lonely ) ) lonely.GlobalPosition = lonely.GlobalPosition.Flat().DistanceTo( lane.Flat() ) < 16f ? far : lonely.GlobalPosition;
			await Wait( 0.1f );
		}
		GD.Print( $"[autotest] alive: a far-off crab foraged {JunkCrab.Forages - forages} times" );
		Check( JunkCrab.Forages > forages, "a crab far from the player never stopped to forage" );
		d.ClearEnemies();

		// ---- The areas change ----
		var level = GetTree().CurrentScene as Node3D;
		AreaLife.ForceTension = 1f;
		AreaTwo.Go( level, player );
		await Wait( 1f );
		await WaitForNav();
		var groans = AreaLife.Groans;
		for ( var t = 0f; t < 16f && AreaLife.Groans == groans; t += 0.2f ) await Wait( 0.2f );
		GD.Print( $"[autotest] alive: the wreck's hull groaned {AreaLife.Groans - groans} times late in the stage" );
		Check( AreaLife.Groans > groans, "the wreck's hull never groaned" );

		AbyssArea.Go( level, player );
		await Wait( 1f );
		await WaitForNav();
		var lamps = AbyssProps.MossLamps;
		var outages = AreaLife.Outages;
		var low = 0;
		for ( var t = 0f; t < 6f; t += 0.1f )
		{
			low = Math.Max( low, lamps.Count( l => IsInstanceValid( l.Light ) && l.Light.LightEnergy < l.Energy * 0.75f ) );
			await Wait( 0.1f );
		}
		GD.Print( $"[autotest] alive: {lamps.Count} moss lamps, up to {low} dimmed at once, {AreaLife.Outages - outages} went out" );
		Check( lamps.Count > 0 && low > 0, "the Abyss's lamps did not flicker late in the stage" );
		Check( AreaLife.Outages > outages, "no Abyss lamp went out late in the stage" );

		var rubble = AreaLife.Rubble;
		SeaStir.Blast( Abyss.Arrival + Vector3.Up * 2f, 1.3f );
		await Wait( 0.2f );
		Check( AreaLife.Rubble > rubble, "a big blast in the caves shook no rubble off the roof" );

		var pass = AreaLife.PassSkylight();
		Check( pass is not null, "no leviathan came over the skylight" );
		if ( pass is not null )
		{
			var start = pass.At;
			var beam0 = AbyssStatue.Light?.LightEnergy ?? 0f;
			var dimmest = beam0;
			var shotTaken = false;
			if ( shots is not null )
			{
				player.GlobalPosition = Nav.Closest( player, new Vector3( 8f, 0f, 0f ) ) + Vector3.Up * 0.1f;
				player.EyeAngles = new Angles( -62f, 90f, 0f );
			}
			for ( var t = 0f; t < SkylightPass.Time * 0.6f; t += 0.1f )
			{
				if ( AbyssStatue.Light is { } l ) dimmest = MathF.Min( dimmest, l.LightEnergy );
				if ( shots is not null && !shotTaken && IsInstanceValid( pass ) && pass.At.Flat().Length() < 6f ) { shotTaken = true; await Shot( "alive_leviathan" ); }
				await Wait( 0.1f );
			}
			var moved = IsInstanceValid( pass ) ? pass.At.DistanceTo( start ) : 0f;
			GD.Print( $"[autotest] alive: the leviathan over the skylight crossed {moved:0} m, the statue's light dimmed {beam0:0.00} -> {dimmest:0.00}" );
			Check( moved > 30f, "the leviathan over the skylight did not swim across" );
			Check( dimmest < beam0 * 0.7f, "the statue's light did not dim as the leviathan crossed it" );
		}

		AtlantisArea.Go( level, player );
		await Wait( 3f );
		var school = Atlantis.Root?.GetChildren().OfType<AtlantisFish>().FirstOrDefault();
		Check( school is not null, "no fish schools in Atlantis" );
		if ( school is not null )
		{
			SeaStir.Blast( school.Centre + Vector3.Right * 1.5f, 1f );
			await Wait( 0.6f );
			var burst = school.Scatter.Spread;
			await Wait( 7f );
			var after = school.Scatter.Spread;
			GD.Print( $"[autotest] alive: a blast by a fish school threw its fish {burst:0.00} m out; 7 s later {after:0.00} m" );
			Check( burst > 1f, "a blast next to a fish school did not scatter it" );
			Check( after < 0.4f, "a scattered fish school did not gather again" );
		}
		var fast = AtlantisHeart.BeatRate;
		AreaLife.ForceTension = 0f;
		await Wait( 2f );
		var slow = AtlantisHeart.BeatRate;
		GD.Print( $"[autotest] alive: Atlantis's heart beats {slow:0.00}/s early in the stage, {fast:0.00}/s late" );
		Check( fast > slow * 2f, "Atlantis's heart did not beat faster late in the stage" );
		AreaLife.ForceTension = null;

		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
