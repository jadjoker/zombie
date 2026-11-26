using Godot;

/// <summary>
/// Heat seeking tridents on long enemies (--tridentseek, playtest: tridents circled Rocky Sharks). A trident through a
/// crab must seek on into a Rocky Shark (in several poses) and hit it, not orbit an empty spot beside it.
/// </summary>
public partial class AutoTest
{
	async Task TridentSeekTest()
	{
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		WaveDirector.Current.SpawningPaused = true;
		await WaitForNav();
		var (home, yaw) = OpenLane( 16f );
		player.GlobalPosition = home + Vector3.Up * 0.1f;
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		var ahead = new Angles( 0f, yaw, 0f ).Forward;
		var side = ahead.Cross( Vector3.Up ).Normalized();
		DevFlags.FreezeEnemies = true;
		int trials = 0, hitShark = 0;
		foreach ( var (turn, offset) in new[] { (0f, 5f), (90f, -5f), (200f, 6f), (300f, -6f), (45f, 4f) } )
		{
			var crab = JunkCrab.Spawn( GetTree().CurrentScene, Nav.Closest( player, home + ahead * 5f ), 5000f );
			var sharkAt = home + ahead * 9f + side * offset;
			var shark = RockyShark.Spawn( GetTree().CurrentScene, sharkAt with { Y = SeaTerrain.HeightAt( sharkAt ) + 1.6f }, 5000f );
			shark.RotationDegrees = new Vector3( 0f, turn, 0f );
			await Wait( 0.8f );
			var before = shark.Health.Current;
			var low = crab.GlobalPosition + Vector3.Up * 0.3f - ahead * 2f;
			var along = (crab.GlobalPosition + Vector3.Up * 0.3f - low).Normalized();
			TridentBolt.Launch( GetTree().CurrentScene, low, low + along * 20f, along, low, 250f, player, null, true );
			await Wait( 2f );
			trials++;
			var hurt = shark.Health.Current < before;
			if ( hurt ) hitShark++;
			GD.Print( $"[autotest] trident seek: shark turned {turn:0}, {offset:0} m aside: hits {TridentBolt.LastHits}, shark hurt {hurt}" );
			crab.QueueFree();
			shark.QueueFree();
			foreach ( var b in GetTree().CurrentScene.FindChildren( "*", "", true, false ).OfType<TridentBolt>() ) b.QueueFree();
			await Wait( 0.3f );
		}
		Check( hitShark == trials, $"the seeking trident hit the Rocky Shark in {hitShark} of {trials} tries" );
		DevFlags.FreezeEnemies = false;
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
