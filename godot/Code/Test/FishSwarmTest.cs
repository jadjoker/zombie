using Godot;

/// <summary>
/// --fish: the fish swarms. The level's schools are swarms; one set close to the player streams over and nips; shot,
/// fish die in step with its health (one health bar for the whole swarm); killed, all its fish are gone. With --shots
/// (a window): the swarm on the player and one thinned out.
/// </summary>
public partial class AutoTest
{
	async Task FishSwarmTest()
	{
		DockHouse.Current?.SkipDive();
		AllyCommander.KeepAlly = false;   // the ally spider would go for the swarm too
		foreach ( var c in Creature.Everyone.ToList() ) if ( c is SeaSpider ) c.QueueFree();
		await WaitForNav();
		await Wait( 0.5f );
		Check( FishSwarm.All.Count >= 3, $"the sea has {FishSwarm.All.Count} fish swarms, not its schools" );
		var me = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		me.Health.ResetTo( 5000f );
		var (spot, yaw) = OpenLane( 10f );
		player.GlobalPosition = spot + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		var ahead = new Angles( 0f, yaw, 0f ).Forward;
		foreach ( var s in FishSwarm.All.ToList() ) s.QueueFree();
		await Wait( 0.2f );
		var swarm = FishSwarm.Spawn( GetTree().CurrentScene, spot + ahead * 9f + Vector3.Up * 2.5f, new Color( 1f, 0.75f, 0.2f ), 16, 1.5f, 0.15f, 0f, 7 );
		await Wait( 0.3f );
		var count = swarm.Alive;
		var health = me.Health.Current;
		for ( var t = 0f; t < 6f && FishSwarm.Bites < 3; t += 0.1f ) { player.GlobalPosition = spot + Vector3.Up * 0.1f; await Wait( 0.1f ); }
		if ( shots is not null ) { player.ThirdPerson = true; await Wait( 0.3f ); await Shot( "fish_swarm_attack" ); player.ThirdPerson = false; LookAt( player.GlobalPosition + ahead * 1.3f + Vector3.Up * 0.8f ); await Wait( 0.2f ); await Shot( "fish_swarm_attack_fp" ); }
		GD.Print( $"[autotest] fish: a swarm of {count} on the player: aggro {swarm.Aggro}, bites {FishSwarm.Bites}, health {health:0} -> {me.Health.Current:0}" );
		Check( swarm.Aggro && FishSwarm.Bites > 0 && me.Health.Current < health, "a swarm near the player did not go for them" );

		// Shot: fish die in step with its health.
		var max = swarm.Health.MaxHealth;
		swarm.Health.Heal( max );   // whatever nibbled at it while it was on the player
		for ( var i = 0; i < 5; i++ ) swarm.ReceiveShot( 0, max * 0.1f, ahead, swarm.Centre, player.Camera.GlobalPosition, me, true );
		await Wait( 0.3f );
		var share = swarm.Health.Current / max;
		GD.Print( $"[autotest] fish: after half its health: {swarm.Alive} of {count} fish left (health {share:P0}), fish killed {FishSwarm.FishKilled}" );
		Check( swarm.Alive == (int)MathF.Ceiling( count * share ) && swarm.Alive < count, "the swarm's fish did not die along with its health" );
		if ( shots is not null ) { LookAt( swarm.Centre ); await Wait( 0.2f ); await Shot( "fish_swarm_thinned" ); }
		for ( var i = 0; i < 10 && IsInstanceValid( swarm ) && !swarm.IsDead; i++ ) swarm.ReceiveShot( 0, max * 0.15f, ahead, swarm.Centre, player.Camera.GlobalPosition, me, true );
		await Wait( 0.5f );
		GD.Print( $"[autotest] fish: killed: {(IsInstanceValid( swarm ) ? "still here" : "gone")}, fish killed {FishSwarm.FishKilled}" );
		Check( !IsInstanceValid( swarm ) && FishSwarm.FishKilled >= count, "the swarm did not die with all its fish" );
		var bodies = GetTree().CurrentScene.FindChildren( "Fish *", "", true, false ).Count;
		var joints = GetTree().CurrentScene.GetChildren().OfType<ConeTwistJoint3D>().Count();
		GD.Print( $"[autotest] fish: the dead: {bodies} body pieces, {joints} joints holding them, {FishSwarm.FishTorn} torn apart" );
		Check( bodies >= count * 3 && joints > 0 && FishSwarm.FishTorn > 0, "the dead fish are not ragdolls that come apart" );
		if ( shots is not null )
		{
			var dead = GetTree().CurrentScene.FindChildren( "Fish Body", "", true, false ).OfType<Node3D>().FirstOrDefault();
			if ( dead is not null )
			{
				var look = dead.GlobalPosition + new Vector3( 1.2f, 0.6f, 1.2f );
				player.CameraOverride = _ => new Transform3D( Basis.LookingAt( (dead.GlobalPosition - look).Normalized(), Vector3.Up ), look );
				await Wait( 0.2f );
				await Shot( "fish_dead" );
				await Wait( 1.5f );
				await Shot( "fish_dead_later" );
				player.CameraOverride = null;
			}
		}
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
