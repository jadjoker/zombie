using Godot;

/// <summary>
/// Garden eels (--eels): none on the first stage; on the wreck site 4 to 6 beds of 2 to 4 eels packed close, the beds
/// well apart, on open sand, hidden in the sand while nobody is near and not counted in the round. A player walking up
/// wakes a bed: the eels dig out of their holes and bite; a shot sends one ducking back in; killed, it bursts, adds
/// nothing to the wave's kills, and its bed grows a new one. Atlantis gets its own beds down in the dry canals. With
/// --shots (a window): a bed close up, an eel's angry face, a bite.
/// </summary>
public partial class AutoTest
{
	async Task GardenEelTest()
	{
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		var d = WaveDirector.Current;
		d.SpawningPaused = true;
		AllyCommander.KeepAlly = false;
		foreach ( var ally in SeaSpider.All.Where( s => !s.IsEnemy ).ToList() ) ally.QueueFree();
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.Invulnerable = true;
		Check( GardenEel.All.Count == 0 && EelBed.All.Count == 0, "garden eels on the first stage" );

		var level = GetTree().CurrentScene as Node3D;
		AreaTwo.Go( level, player );
		await Wait( 1f );
		Check( SeaTerrain.Area == 2, "did not get to the wreck site" );
		await WaitForNav();
		var beds = EelBed.All.ToList();
		var counts = beds.Select( b => b.Eels.Count( e => e is not null ) ).ToList();
		GD.Print( $"[autotest] eels: {beds.Count} beds of {string.Join( ", ", counts )}, {GardenEel.All.Count} eels" );
		Check( beds.Count >= EelBed.BedsMin && beds.Count <= EelBed.BedsMax, $"{beds.Count} eel beds on the wreck site, not 4 to 6" );
		Check( counts.All( n => n >= EelBed.EelsMin && n <= EelBed.EelsMax ), "a bed without 2 to 4 eels" );
		var packed = beds.All( b => b.Holes.All( h => b.Holes.Where( o => o != h ).Select( o => o.Flat().DistanceTo( h.Flat() ) ).DefaultIfEmpty( 0f ).Min() < 0.8f ) );
		var spaced = beds.All( b => b.Holes.All( h => h.Flat().DistanceTo( b.Holes[0].Flat() ) < 2.4f ) );
		var apart = beds.All( b => beds.Where( o => o != b ).All( o => o.GlobalPosition.Flat().DistanceTo( b.GlobalPosition.Flat() ) > 9f ) );
		var onSand = GardenEel.All.All( e => MathF.Abs( e.GlobalPosition.Y - SeaTerrain.HeightAt( e.GlobalPosition ) ) < 0.3f );
		GD.Print( $"[autotest] eels: packed {packed}, beds tight {spaced}, beds apart {apart}, on the sand {onSand}" );
		Check( packed && spaced, "a bed's eels are not packed close together" );
		Check( apart, "two eel beds are on top of each other" );
		Check( onSand, "an eel's hole is not on the sand" );
		var tints = GardenEel.All.Select( e => e.Tint ).Distinct().Count();
		GD.Print( $"[autotest] eels: {tints} skin colours of {GardenEel.Skins.Length}" );
		Check( GardenEel.All.Count < 6 || tints > 1, "every garden eel has the same skin colour" );

		// Asleep while nobody is near; not part of the round.
		var bed = beds.OrderBy( b => b.GlobalPosition.Flat().Length() ).First();
		var hole = bed.Holes[0];
		var away = hole + (hole.Flat().IsNearlyZero() ? Vector3.Forward : -hole.Flat().Normalized()) * 30f;
		player.GlobalPosition = Nav.Closest( player, away ) + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		await Wait( 3.5f );
		var near = bed.Eels.Where( e => e is not null && IsInstanceValid( e ) ).ToList();
		GD.Print( $"[autotest] eels: with nobody near, hidden {near.Count( e => e.Hidden )} of {near.Count}; wave enemies left {d.ZombiesRemaining}" );
		Check( near.All( e => e.Hidden && e.Rise < 0.05f ), "garden eels were up with nobody near" );
		Check( near.All( e => !e.IsEnemy ), "hidden garden eels still count as enemies (health bars, aim assist)" );

		// A player walks up: they dig out and bite.
		var toBed = (hole - player.GlobalPosition).Flat().Normalized();
		var stand = hole - toBed * 1.2f;
		stand = stand with { Y = Sculpt.FloorBelow( player, stand + Vector3.Up * 2f, 5f ) };
		player.GlobalPosition = stand + Vector3.Up * 0.05f;
		player.Velocity = Vector3.Zero;
		player.EyeAngles = new Angles( 0f, MathX.Deg( MathF.Atan2( -toBed.X, -toBed.Z ) ), 0f );
		var digs = GardenEel.Digs;
		GardenEel.Bites = 0;
		var shotBite = false;
		var closest = float.MaxValue;
		GardenEel.Squeezes = 0;
		for ( var t = 0f; t < 6f && GardenEel.Bites == 0; t += 0.02f )
		{
			player.GlobalPosition = player.GlobalPosition with { X = stand.X, Z = stand.Z };
			if ( near.Count( e => IsInstanceValid( e ) && e.Rise > 0.5f ) > 1 ) closest = MathF.Min( closest, GardenEel.ClosestPair() );
			if ( shots is not null && !shotBite && near.Any( e => IsInstanceValid( e ) && e.ActName == "Lunge" ) ) { shotBite = true; await EelShot( "eels_bite", stand - toBed * 1.8f + Vector3.Up * 1.6f + new Vector3( -toBed.Z, 0f, toBed.X ) * 1.6f, hole + Vector3.Up * 0.8f ); }
			await Wait( 0.02f );
		}
		// Keep them up and biting a while longer, walking round the bed so they lean every way over each other.
		for ( var t = 0f; t < 6f; t += 0.02f )
		{
			var a = t / 6f * MathF.Tau;
			var round = hole + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * 1.3f;
			player.GlobalPosition = round with { Y = Sculpt.FloorBelow( player, round + Vector3.Up * 2f, 5f ) + 0.05f };
			player.Velocity = Vector3.Zero;
			if ( near.Count( e => IsInstanceValid( e ) && e.Rise > 0.5f ) > 1 ) closest = MathF.Min( closest, GardenEel.ClosestPair() );
			await Wait( 0.02f );
		}
		player.GlobalPosition = stand + Vector3.Up * 0.05f;
		GD.Print( $"[autotest] eels: the closest two eels' skins came {closest:0.000} m apart, bent round each other {GardenEel.Squeezes} times" );
		Check( closest > 0f, $"two garden eels went into each other ({closest:0.000} m)" );
		var up = near.Count( e => IsInstanceValid( e ) && e.Rise > 0.8f );
		GD.Print( $"[autotest] eels: walked up: {GardenEel.Digs - digs} dug out, {up} up, bites {GardenEel.Bites}" );
		Check( GardenEel.Digs > digs && up > 0, "the garden eels did not dig out when a player came near" );
		Check( GardenEel.Bites > 0, "a garden eel never bit the player next to its hole" );
		Check( near.All( e => !IsInstanceValid( e ) || e.HeadPosition.Y > hole.Y + 0.4f || e.Rise < 0.5f ), "a garden eel's head is down in the sand while it is up" );

		if ( shots is not null )
		{
			DevFlags.FreezeEnemies = true;
			await Wait( 0.3f );
			var eel = near.First( e => IsInstanceValid( e ) );
			var face = eel.HeadPosition;
			var front = (player.GlobalPosition - eel.GlobalPosition).Flat().Normalized();
			await EelShot( "eels_bed", hole - toBed * 2.4f + Vector3.Up * 1.3f + new Vector3( -toBed.Z, 0f, toBed.X ) * 1.2f, hole + Vector3.Up * 0.6f );
			await EelShot( "eels_face", face + eel.GlobalBasis * new Vector3( 0.15f, 0.08f, -0.45f ), face );
			await EelShot( "eels_side", face + new Vector3( -front.Z, 0f, front.X ) * 0.9f - Vector3.Up * 0.2f, face - Vector3.Up * 0.35f );
			DevFlags.FreezeEnemies = false;
		}

		// Rooted in its hole: shoves and drags (the Gore Vac, the grab, explosions) don't move it, and none wander.
		var pinned = near.First( e => IsInstanceValid( e ) );
		var pinnedAt = pinned.GlobalPosition;
		SkillFx.Shove( pinned, Vector3.Right * 3f + Vector3.Up );
		pinned.Pull( Vector3.Forward * 2f );
		await Wait( 0.3f );
		var moved = pinned.GlobalPosition.DistanceTo( pinnedAt );
		var wandered = EelBed.All.SelectMany( b => b.Eels.Select( ( e, i ) => (e, hole: b.Holes[i]) ) ).Where( x => x.e is not null && IsInstanceValid( x.e ) ).Max( x => x.e.GlobalPosition.Flat().DistanceTo( x.hole.Flat() ) );
		GD.Print( $"[autotest] eels: shoved and dragged one {moved:0.000} m; furthest any eel is from its hole {wandered:0.000} m" );
		Check( moved < 0.01f && wandered < 0.01f, "a garden eel moved out of its hole" );

		// The worst bed: three in a row pointing at the player, each leaning straight over the one in front.
		var (laneAt, laneYaw) = OpenLane( 6f );
		var laneDir = new Vector3( -MathF.Sin( MathX.Rad( laneYaw ) ), 0f, -MathF.Cos( MathX.Rad( laneYaw ) ) );
		var rowDir = -laneDir;   // from the holes toward where the player stands
		var rowHoles = new List<Vector3>();
		for ( var k = 0; k < 3; k++ ) { var h = laneAt + laneDir * (3f + 0.45f * k); rowHoles.Add( h with { Y = Sculpt.FloorBelow( player, h + Vector3.Up * 2f, 5f ) } ); }
		var row = EelBed.MakeAt( level, rowHoles );
		var rowStand = rowHoles[0] + rowDir * 1.1f;
		var rowClosest = float.MaxValue;
		GardenEel.Squeezes = 0;
		for ( var t = 0f; t < 7f; t += 0.02f )
		{
			var a = MathF.Sin( t * 0.9f ) * 0.5f;   // stepping side to side in front of the row
			var at = rowStand + new Vector3( -rowDir.Z, 0f, rowDir.X ) * a;
			player.GlobalPosition = at with { Y = Sculpt.FloorBelow( player, at + Vector3.Up * 2f, 5f ) + 0.05f };
			player.Velocity = Vector3.Zero;
			if ( row.Eels.Count( e => e is not null && IsInstanceValid( e ) && e.Rise > 0.5f ) > 1 ) rowClosest = MathF.Min( rowClosest, GardenEel.ClosestPair() );
			await Wait( 0.02f );
		}
		GD.Print( $"[autotest] eels: a row of three pointing at the player: closest skins {rowClosest:0.000} m, bent round each other {GardenEel.Squeezes} times, up {row.Eels.Count( e => e is not null && e.Rise > 0.5f )}" );
		Check( rowClosest < float.MaxValue && rowClosest > 0f, $"garden eels in a row went into each other ({rowClosest:0.000} m)" );
		if ( shots is not null ) await EelShot( "eels_row", rowStand + new Vector3( -rowDir.Z, 0f, rowDir.X ) * 2.2f + Vector3.Up * 1.1f, rowHoles[1] + Vector3.Up * 0.8f );
		player.GlobalPosition = stand + Vector3.Up * 0.05f;

		// A shot sends it ducking into its hole; killed, it bursts and is not one of the round's kills.
		for ( var t = 0f; t < 5f && !near.Any( e => IsInstanceValid( e ) && e.IsOut ); t += 0.1f ) await Wait( 0.1f );
		var target = near.FirstOrDefault( e => IsInstanceValid( e ) && e.IsOut );
		Check( target is not null, "no garden eel came back up when the player came back" );
		if ( target is null ) { GetTree().Quit(); return; }
		var ducks = GardenEel.Ducks;
		target.ReceiveShot( 1, 5f, toBed, target.HeadPosition, player.GlobalPosition, survivor, true );
		await Wait( 0.4f );
		GD.Print( $"[autotest] eels: shot: ducks {GardenEel.Ducks - ducks}, out of the sand {target.Rise:0.00}" );
		Check( GardenEel.Ducks > ducks && target.Rise < 0.5f, "a shot garden eel did not duck into its hole" );
		var kills = d.Kills;
		var left = d.ZombiesRemaining;
		var holeOf = target.GlobalPosition;
		target.Health.OnDamage( new DamageInfo { Damage = 1000f, Attacker = player } );
		await Wait( 0.5f );
		GD.Print( $"[autotest] eels: killed one: wave kills {kills} -> {d.Kills}, enemies left {left} -> {d.ZombiesRemaining}" );
		Check( !IsInstanceValid( target ), "the killed garden eel is still there" );
		Check( d.Kills == kills && d.ZombiesRemaining == left, "a garden eel counted in the round" );
		EelBed.RegrowScale = 200f;
		for ( var t = 0f; t < 4f && !GardenEel.All.Any( e => e.GlobalPosition.DistanceTo( holeOf ) < 0.05f ); t += 0.1f ) await Wait( 0.1f );
		EelBed.RegrowScale = 1f;
		Check( GardenEel.All.Any( e => e.GlobalPosition.DistanceTo( holeOf ) < 0.05f ), "the bed did not grow a new eel in the dead one's hole" );

		// Atlantis: new beds down in the canals, the wreck site's gone.
		AreaThree.Go( level, player );
		await Wait( 1f );
		var canal = GardenEel.All.All( e => MathF.Abs( e.GlobalPosition.Y - Atlantis.CanalFloorY ) < 0.3f );
		GD.Print( $"[autotest] eels: Atlantis: {EelBed.All.Count} beds, {GardenEel.All.Count} eels, all in the canals {canal}" );
		Check( EelBed.All.Count >= EelBed.BedsMin && EelBed.All.Count <= EelBed.BedsMax, $"{EelBed.All.Count} eel beds in Atlantis, not 4 to 6" );
		Check( canal, "an eel in Atlantis is not down in a canal" );

		GD.Print( "[autotest] done" );
		GetTree().Quit();

		async Task EelShot( string name, Vector3 eye, Vector3 at )
		{
			foreach ( var n in GetTree().Root.FindChildren( "*", "", true, false ) )
				if ( n is ViewModel or ViewModelAxe && n is Node3D vm ) vm.Visible = false;
			Transform3D? view = new Transform3D( Shapes.LookAt( at - eye ), eye );
			player.CameraOverride = _ => view;
			player.ThirdPerson = true;
			await Wait( 0.1f );
			await Shot( name );
			player.ThirdPerson = false;
			player.CameraOverride = null;
		}
	}
}
