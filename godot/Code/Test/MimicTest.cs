using Godot;

/// <summary>
/// The mimic octopus (--mimic): 0.2% of sea mines and glow bulbs; with the chance forced up every mine laid is one, in
/// disguise, not an enemy and not in the round. A player walking up gives it away: it drops out of its disguise and
/// grabs them, drawing them in to bite while the HUD says CUT THE TENTACLE OFF!; an axe swing cuts the arm off and frees
/// them, so does any other melee strike (the spear's) and a teammate shooting the arm; it has eight arms, and killing it
/// is no wave kill. A bulb disguise works the same. With --shots (a window): in disguise, giving itself away, the
/// octopus, the grab with the HUD's cue, the coil, the cut.
/// </summary>
public partial class AutoTest
{
	async Task MimicTest()
	{
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		var d = WaveDirector.Current;
		d.SpawningPaused = true;
		AllyCommander.KeepAlly = false;
		foreach ( var ally in SeaSpider.All.Where( s => !s.IsEnemy ).ToList() ) ally.QueueFree();
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.Invulnerable = true;
		var level = GetTree().CurrentScene as Node3D;
		Check( MathF.Abs( MimicOctopus.Chance - 0.002f ) < 1e-6f, $"a mine or bulb is a mimic {MimicOctopus.Chance:0.0%} of the time, not 0.2%" );

		// With the chance forced up, every mine laid is a mimic in disguise, and none of them is in the round.
		var chance = MimicOctopus.Chance;
		MimicOctopus.Chance = 1f;
		SeaMine.Lay( level );
		MimicOctopus.Chance = chance;
		await Wait( 0.3f );
		var laid = MimicOctopus.All.Count;
		GD.Print( $"[autotest] mimic: forced, {laid} mimics and {SeaMine.All.Count} mines laid; hidden {MimicOctopus.All.Count( m => m.Hidden )}, enemies {MimicOctopus.All.Count( m => m.IsEnemy )}, in the round {d.AliveCount}" );
		Check( laid >= SeaMine.PerArea / 2 && SeaMine.All.Count == 0, "forced mimics did not take the mines' places" );
		Check( MimicOctopus.All.All( m => m.Hidden && !m.IsEnemy ), "a mimic in disguise counts as an enemy" );
		Check( d.AliveCount == 0, "mimics count toward the round" );
		foreach ( var m in MimicOctopus.All.ToList() ) m.QueueFree();
		SeaMine.Lay( level );
		await Wait( 0.2f );

		await WaitForNav();
		var (open, yaw) = OpenLane( 16f );
		void Stand( Vector3 at ) { player.GlobalPosition = at + Vector3.Up * 0.1f; player.Velocity = Vector3.Zero; }
		Stand( open );
		player.EyeAngles = new Angles( 0f, yaw, 0f );
		var ahead = new Angles( 0f, yaw, 0f ).Forward;
		var side = ahead.Cross( Vector3.Up ).Normalized();
		var spot = Nav.Closest( player, open + ahead * 8f );
		var mimic = MimicOctopus.Disguised( level, spot with { Y = Sculpt.FloorBelow( player, spot + Vector3.Up * 2f, 10f ) }, MimicOctopus.Disguise.Mine, 2f );
		await Wait( 0.5f );
		Check( mimic.Hidden && !mimic.IsEnemy, "the mimic did not start in disguise" );

		Transform3D? view = null;
		async Task From( string name, Vector3 eye, Vector3 at, bool hud = false )
		{
			if ( shots is null ) return;
			if ( !hud ) HideHud();
			view = new Transform3D( Shapes.LookAt( at - eye ), eye );
			player.CameraOverride = _ => view;
			player.ThirdPerson = true;
			await Wait( 0.12f );
			await Shot( name );
			player.CameraOverride = null;
			player.ThirdPerson = false;
			ShowHud();
		}
		await From( "mimic_disguised", mimic.DisguiseAt + side * 2.5f - ahead * 2.5f + Vector3.Up * 0.6f, mimic.DisguiseAt );

		// Walking up gives it away.
		var near = Nav.Closest( player, mimic.GlobalPosition - ahead * 2.6f );
		Stand( near );
		var revealed = false;
		for ( var t = 0f; t < 3f && !revealed; t += 0.02f ) { revealed = !mimic.Hidden; await Wait( 0.02f ); }
		Check( revealed && mimic.IsEnemy && MimicOctopus.Reveals > 0, "walking up to the mimic did not give it away" );
		DevFlags.FreezeEnemies = true;
		await Wait( 0.25f );
		await From( "mimic_reveal", near + side * 2.2f + Vector3.Up * 1.4f, mimic.DisguiseAt with { Y = mimic.GlobalPosition.Y + 1f } );
		DevFlags.FreezeEnemies = false;
		await Wait( MimicOctopus.RevealTime );
		DevFlags.FreezeEnemies = true;
		var floorGap = mimic.GlobalPosition.Y - Sculpt.FloorBelow( player, mimic.GlobalPosition + Vector3.Up, 6f );
		GD.Print( $"[autotest] mimic: revealed, {mimic.ActName}, {floorGap:0.00} m off the floor, {mimic.ArmsLeft} arms" );
		Check( MathF.Abs( floorGap ) < 0.15f, $"the octopus did not land on the sand ({floorGap:0.00} m)" );
		Check( mimic.ArmsLeft == MimicOctopus.Arms, "the octopus has not got eight arms" );
		var face = mimic.GlobalPosition + mimic.GlobalBasis * new Vector3( 0.5f, 0.9f, -2f );
		await From( "mimic_octopus", face, mimic.Centre );
		await From( "mimic_octopus_side", mimic.GlobalPosition + mimic.GlobalBasis * new Vector3( 2.2f, 1.3f, 0.3f ), mimic.Centre );
		DevFlags.FreezeEnemies = false;

		// It grabs: the player is held, drawn in, hurt, and told to cut the arm off.
		survivor.Health.Invulnerable = false;
		survivor.Health.Heal( 9999f );
		async Task<bool> WaitGrab( float seconds )
		{
			mimic.ReadyGrab();
			for ( var t = 0f; t < seconds; t += 0.02f )
			{
				if ( mimic.Holding ) return true;
				if ( !player.Scripted ) Stand( player.GlobalPosition with { Y = player.GlobalPosition.Y - 0.1f } );
				await Wait( 0.02f );
			}
			return mimic.Holding;
		}
		var grabbed = await WaitGrab( 6f );
		var startGap = player.GlobalPosition.Flat().DistanceTo( mimic.GlobalPosition.Flat() );
		var before = survivor.Health.Current;
		await Wait( 1.8f );
		var endGap = player.GlobalPosition.Flat().DistanceTo( mimic.GlobalPosition.Flat() );
		var lost = before - survivor.Health.Current;
		GD.Print( $"[autotest] mimic: grabbed {grabbed}, held {player.Scripted}, drawn from {startGap:0.0} to {endGap:0.0} m, lost {lost:0}, cue shown {HudOverlays.CutPromptShown}, holds this player {mimic.HoldsLocal}" );
		Check( grabbed && player.Scripted, "the mimic never grabbed the player" );
		Check( endGap < startGap - 0.3f || endGap < mimic.HoldAt + 0.4f, "the held player was not drawn in" );
		Check( lost > 3f, "being held did no harm" );
		Check( HudOverlays.CutPromptShown && mimic.HoldsLocal, "the HUD did not say CUT THE TENTACLE OFF!" );
		if ( shots is not null )
		{
			var look = (mimic.GlobalPosition - player.GlobalPosition).Flat();
			player.EyeAngles = new Angles( 15f, MathX.Deg( MathF.Atan2( -look.X, -look.Z ) ), 0f );
			await Wait( 0.1f );
			await Shot( "mimic_grab_hud" );
			DevFlags.FreezeEnemies = true;
			await From( "mimic_coil", player.GlobalPosition + side * 2.4f + Vector3.Up * 1.5f - ahead * 0.5f, player.GlobalPosition + Vector3.Up * 0.9f + (mimic.GlobalPosition - player.GlobalPosition) * 0.4f );
			DevFlags.FreezeEnemies = false;
		}

		// An axe swing cuts the arm off and frees them.
		var arms = mimic.ArmsLeft;
		var hp = mimic.Health.Current;
		var melee = player.GetNode<PlayerMelee>( "PlayerMelee" );
		melee.TestSwing();
		for ( var t = 0f; t < 0.8f && mimic.Holding; t += 0.02f ) await Wait( 0.02f );
		var cutTook = hp - mimic.Health.Current;
		GD.Print( $"[autotest] mimic: axe swing, arms {arms} to {mimic.ArmsLeft}, free {!player.Scripted}, it lost {cutTook:0} of {mimic.Health.MaxHealth:0}" );
		Check( mimic.ArmsLeft == arms - 1, "an axe swing did not cut the arm off" );
		Check( !player.Scripted && !mimic.Holding, "cutting the arm off did not free the player" );
		Check( cutTook >= mimic.Health.MaxHealth * MimicOctopus.CutShare - 1f, "cutting an arm off did not hurt it" );
		Check( !HudOverlays.CutPromptShown || await Settled( () => !HudOverlays.CutPromptShown ), "the cue stayed up after the cut" );
		if ( shots is not null ) { await Wait( 0.15f ); await From( "mimic_cut", player.GlobalPosition + side * 2.6f + Vector3.Up * 1.6f, (player.GlobalPosition + mimic.GlobalPosition) * 0.5f + Vector3.Up * 0.5f ); }

		// Any melee strike works (the spear's thrust goes through the same strike).
		grabbed = await WaitGrab( 8f );
		arms = mimic.ArmsLeft;
		if ( grabbed ) melee.StrikeWith( melee.Reach * 2f, melee.Radius, 40f, null );
		await Wait( 0.1f );
		GD.Print( $"[autotest] mimic: grabbed again {grabbed}, a spear-style strike left {mimic.ArmsLeft} arms, free {!player.Scripted}" );
		Check( grabbed && mimic.ArmsLeft == arms - 1 && !player.Scripted, "another melee strike did not cut the arm off" );

		// A teammate can shoot the arm off.
		grabbed = await WaitGrab( 8f );
		arms = mimic.ArmsLeft;
		var lash = CreaturePart.All.FirstOrDefault( p => p.Creature == mimic && p.Index == 2 && p.CollisionLayer != 0 );
		if ( grabbed && lash is not null ) for ( var i = 0; i < 3 && mimic.Holding; i++ ) mimic.ReceiveShot( 2, 20f, ahead, lash.GlobalPosition, lash.GlobalPosition - ahead * 4f, null, true );
		await Wait( 0.1f );
		GD.Print( $"[autotest] mimic: grabbed a third time {grabbed}, shooting the arm left {mimic.ArmsLeft} arms, free {!player.Scripted}" );
		Check( grabbed && lash is not null && mimic.ArmsLeft == arms - 1 && !player.Scripted, "shooting the grabbing arm did not cut it off" );

		// Killing it is no wave kill, and lets go.
		survivor.Health.Invulnerable = true;
		var kills = d.Kills;
		if ( IsInstanceValid( mimic ) && !mimic.IsDead ) mimic.Health.Kill();
		await Wait( 0.6f );
		Check( d.Kills == kills, "killing the mimic counted as a wave kill" );
		Check( !player.Scripted, "the player is still held after the mimic died" );

		// A glow bulb disguise gives itself away and drops to the sand too.
		Stand( open );
		spot = Nav.Closest( player, open + ahead * 9f + side * 2f );
		var bulb = MimicOctopus.Disguised( level, spot with { Y = Sculpt.FloorBelow( player, spot + Vector3.Up * 2f, 10f ) }, MimicOctopus.Disguise.Bulb, 1.4f );
		await Wait( 0.4f );
		await From( "mimic_bulb", bulb.DisguiseAt - ahead * 2.6f + side * 1.2f + Vector3.Up * 0.4f, bulb.DisguiseAt );
		bulb.ForceReveal();
		await Wait( MimicOctopus.RevealTime + 0.3f );
		GD.Print( $"[autotest] mimic: bulb disguise revealed {!bulb.Hidden}, {bulb.ActName}" );
		Check( !bulb.Hidden && bulb.IsEnemy, "the glow bulb mimic did not give itself away" );
		bulb.Health.Kill();
		await Wait( 0.5f );
		Check( MimicOctopus.All.Count == 0, $"{MimicOctopus.All.Count} mimics left" );

		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	async Task<bool> Settled( Func<bool> ok )
	{
		for ( var t = 0f; t < 0.5f; t += 0.05f ) { if ( ok() ) return true; await Wait( 0.05f ); }
		return ok();
	}
}
