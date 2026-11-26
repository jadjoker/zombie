using Godot;

/// <summary>
/// Playtest 11/12 notes (issue #44, --playtest44): MC-51 has 20% more health each time he comes back; the ally spider
/// leaves an enemy held 2.6 m over it alone (stab and harpoon) but still stabs one beside it; dying plays the womp womp
/// and cuts the music, and a respawn brings the music back.
/// </summary>
public partial class AutoTest
{
	async Task Playtest44Test()
	{
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		var d = WaveDirector.Current;
		d.SpawningPaused = true;
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );

		// MC-51: each visit at least 20% over the last.
		var healths = Enumerable.Range( 1, 7 ).Select( i => d.BossHealthAt( i * d.BossEvery ) ).ToList();
		GD.Print( $"[autotest] playtest44: MC-51 health by visit {string.Join( ", ", healths.Select( h => h.ToString( "0" ) ) )}" );
		Check( healths[0] == d.BossBaseHealth, "MC-51's first visit is not at his base health" );
		for ( var i = 1; i < healths.Count; i++ )
			Check( healths[i] >= healths[i - 1] * 1.2f, $"MC-51's visit {i + 1} has under 20% more health than visit {i}" );

		// The ally spider and an enemy held over it.
		survivor.Health.Invulnerable = true;
		await WaitForNav();
		var (open, yaw) = OpenLane( 12f );
		player.GlobalPosition = open + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		AllyCommander.KeepAlly = false;
		foreach ( var s in SeaSpider.All.Where( s => s.IsAlly ).ToList() ) s.QueueFree();
		await Wait( 0.2f );
		var ally = SeaSpider.SpawnAlly( player );
		await Wait( 1.5f );
		Check( ally is not null && IsInstanceValid( ally ), "no ally spider" );
		var ahead = new Angles( 0f, yaw, 0f ).Forward;
		var swimmer = Bloober.Spawn( GetTree().CurrentScene, ally.GlobalPosition + Vector3.Up * 2.6f, 5000f );
		await Wait( 0.2f );
		var full = swimmer.Health.Current;
		for ( var t = 0f; t < 5f; t += 1f / 60f )
		{
			swimmer.StunnedUntil = Clock.Now + 1.0;
			swimmer.GlobalPosition = ally.GlobalPosition + ahead * 0.4f + Vector3.Up * 2.6f;
			await ToSignal( GetTree(), SceneTree.SignalName.PhysicsFrame );
		}
		var lowest = CreaturePart.All.Where( p => IsInstanceValid( p ) && p.Creature == swimmer ).Select( p => p.GlobalPosition.Y ).DefaultIfEmpty( swimmer.GlobalPosition.Y ).Min() - ally.GlobalPosition.Y;
		var above = full - swimmer.Health.Current;
		GD.Print( $"[autotest] playtest44: ally spider hurt the Bloober over it (lowest part {lowest:0.0} m up) by {above:0}" );
		Check( above <= 0f, $"the ally spider hit a Bloober {lowest:0.0} m above it ({above:0} damage)" );
		swimmer.QueueFree();
		var crab = JunkCrab.Spawn( GetTree().CurrentScene, ally.GlobalPosition + ahead * 1.2f, 5000f );
		await Wait( 0.2f );

		var low = crab.Health.Current;
		for ( var t = 0f; t < 5f && crab.Health.Current >= low; t += 1f / 60f )
		{
			crab.StunnedUntil = Clock.Now + 1.0;
			crab.GlobalPosition = ally.GlobalPosition with { Y = ally.GlobalPosition.Y } + ahead * 1.2f;
			await ToSignal( GetTree(), SceneTree.SignalName.PhysicsFrame );
		}
		GD.Print( $"[autotest] playtest44: ally spider hurt the crab beside it by {low - crab.Health.Current:0}" );
		Check( crab.Health.Current < low, "the ally spider did not stab a crab right beside it" );
		crab.QueueFree();
		ally.QueueFree();

		// Dying: womp womp, the music stops, and it comes back with the robot.
		var radio = Radio.Instance;
		radio.Start();
		await Wait( 0.5f );
		Check( radio.Playing, "the radio did not start" );
		var womps = Radio.DeathSounds;
		survivor.Health.Invulnerable = false;
		survivor.Health.Kill();
		await Wait( 0.5f );
		Check( Radio.DeathSounds == womps + 1, "dying did not play the womp womp" );
		Check( !radio.Playing, "the music kept playing after the player died" );
		survivor.Respawn( open + Vector3.Up * 0.1f, true );
		await Wait( 0.5f );
		Check( radio.Playing, "the music did not come back with the respawn" );
		Check( Radio.DeathSounds == womps + 1, "the womp womp played more than once" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
