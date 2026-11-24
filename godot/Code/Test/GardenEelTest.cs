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
		for ( var t = 0f; t < 6f && GardenEel.Bites == 0; t += 0.02f )
		{
			player.GlobalPosition = player.GlobalPosition with { X = stand.X, Z = stand.Z };
			if ( shots is not null && !shotBite && near.Any( e => IsInstanceValid( e ) && e.ActName == "Lunge" ) ) { shotBite = true; await EelShot( "eels_bite", stand - toBed * 1.8f + Vector3.Up * 1.6f + new Vector3( -toBed.Z, 0f, toBed.X ) * 1.6f, hole + Vector3.Up * 0.8f ); }
			await Wait( 0.02f );
		}
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

		// A shot sends it ducking into its hole; killed, it bursts and is not one of the round's kills.
		var target = near.First( e => IsInstanceValid( e ) && e.IsOut );
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
