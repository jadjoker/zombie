using Godot;

/// <summary> --perfcreatures (a window): ten of each common enemy at a time, chasing the (unhurt) player; frame time and their own code's cost. </summary>
public partial class AutoTest
{
	async Task PerfCreaturesTest()
	{
		DisplayServer.WindowSetVsyncMode( DisplayServer.VSyncMode.Disabled );
		Engine.MaxFps = 0;
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		if ( WaveDirector.Current is { } director ) director.SpawningPaused = true;
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		var (spot, _) = OpenLane( 10f );
		player.GlobalPosition = spot + Vector3.Up * 0.1f;
		await Wait( 1.5f );
		foreach ( var kind in new[] { WaveDirector.EnemyKind.Crab, WaveDirector.EnemyKind.Spider, WaveDirector.EnemyKind.Squilien, WaveDirector.EnemyKind.Bloober, WaveDirector.EnemyKind.RockyShark } )
		{
			var made = new List<Node3D>();
			for ( var i = 0; i < 10; i++ )
			{
				var a = i / 10f * MathF.Tau;
				if ( WaveDirector.Current.SpawnAt( kind, Nav.Closest( player, spot + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * 12f ) + Vector3.Up * 0.3f, spot ) is { } n ) made.Add( n );
			}
			await Wait( 2f );
			Perf.Clear();
			Perf.Enabled = true;
			var frames = new List<double>();
			var last = Time.GetTicksUsec();
			var start = last;
			while ( (Time.GetTicksUsec() - start) / 1e6 < 3.0 )
			{
				await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
				var now = Time.GetTicksUsec();
				frames.Add( (now - last) / 1000.0 );
				last = now;
			}
			Perf.Enabled = false;
			frames.Sort();
			GD.Print( $"[autotest] creatures {kind} x{made.Count}: median frame {frames[frames.Count / 2]:0.00} ms" );
			foreach ( var (label, (ms, calls)) in Perf.Totals.OrderByDescending( t => t.Value.Ms ) )
				GD.Print( $"[autotest] creatures {kind}:   {label,-22} {ms / frames.Count:0.000} ms/frame ({calls / (double)frames.Count:0.0} calls)" );
			foreach ( var n in made ) if ( IsInstanceValid( n ) ) n.QueueFree();
			await Wait( 1f );
		}
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
