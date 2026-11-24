using Godot;

/// <summary>
/// --perftypes (a window): where the CPU goes, by code type. Stands on the sea floor (idle) and then in a fight (a dozen
/// enemies coming, unfrozen); in each, measures the median frame, the GPU time and the render CPU time, then switches off
/// the per-frame code of every node of one type at a time (its _Process and _PhysicsProcess) and measures again. The
/// types that cost the most are printed first. Checks nothing; it points the optimisation pass at the right code.
/// </summary>
public partial class AutoTest
{
	async Task PerfTypesTest()
	{
		DisplayServer.WindowSetVsyncMode( DisplayServer.VSyncMode.Disabled );
		Engine.MaxFps = 0;
		var me = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		me.Health.Invulnerable = true;
		if ( WaveDirector.Current is { } director ) director.SpawningPaused = true;
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 2f );
		var vp = GetViewport().GetViewportRid();
		RenderingServer.ViewportSetMeasureRenderTime( vp, true );

		async Task<(double Frame, double Gpu, double Cpu)> Measure( float seconds )
		{
			var frames = new List<double>();
			double gpu = 0, cpu = 0;
			var last = Time.GetTicksUsec();
			var start = last;
			while ( (Time.GetTicksUsec() - start) / 1e6 < seconds )
			{
				await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
				var now = Time.GetTicksUsec();
				frames.Add( (now - last) / 1000.0 );
				last = now;
				gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu( vp );
				cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu( vp );
			}
			frames.Sort();
			return (frames[frames.Count / 2], gpu / Math.Max( 1, frames.Count ), cpu / Math.Max( 1, frames.Count ));
		}

		async Task Split( string scene )
		{
			var (frame, gpu, cpu) = await Measure( 2f );
			GD.Print( $"[autotest] types {scene}: median frame {frame:0.00} ms, gpu {gpu:0.00} ms, render cpu {cpu:0.00} ms, process {Performance.GetMonitor( Performance.Monitor.TimeProcess ) * 1000:0.00} ms, physics {Performance.GetMonitor( Performance.Monitor.TimePhysicsProcess ) * 1000:0.00} ms, nodes {Performance.GetMonitor( Performance.Monitor.ObjectNodeCount ):0}, draws {Performance.GetMonitor( Performance.Monitor.RenderTotalDrawCallsInFrame ):0}" );
			var byType = new Dictionary<string, List<Node>>();
			void Walk( Node n )
			{
				if ( n.IsProcessing() || n.IsPhysicsProcessing() )
				{
					var key = n.GetType().Name;
					if ( !byType.TryGetValue( key, out var list ) ) byType[key] = list = new List<Node>();
					list.Add( n );
				}
				foreach ( var k in n.GetChildren() ) Walk( k );
			}
			Walk( GetTree().Root );
			var results = new List<(string Type, int Count, double Saved)>();
			foreach ( var (type, nodes) in byType )
			{
				if ( type is nameof( AutoTest ) or nameof( Clock ) or nameof( PlayerController ) ) continue;
				nodes.RemoveAll( n => !IsInstanceValid( n ) );
				var states = nodes.Select( n => (n, n.IsProcessing(), n.IsPhysicsProcessing()) ).ToList();
				// Paired: on, off, on again (the frame drifts as things move about, so each type gets its own baseline).
				var (before, _, _) = await Measure( 0.5f );
				foreach ( var n in nodes ) { if ( IsInstanceValid( n ) ) { n.SetProcess( false ); n.SetPhysicsProcess( false ); } }
				var (without, _, _) = await Measure( 0.6f );
				foreach ( var (n, p, ph) in states ) if ( IsInstanceValid( n ) ) { n.SetProcess( p ); n.SetPhysicsProcess( ph ); }
				var (after, _, _) = await Measure( 0.5f );
				results.Add( (type, nodes.Count, (before + after) * 0.5 - without) );
			}
			foreach ( var r in results.OrderByDescending( r => r.Saved ).Take( 18 ) )
				GD.Print( $"[autotest] types {scene}:   {r.Type,-28} x{r.Count,-5} {r.Saved,6:+0.00;-0.00} ms" );
		}

		await Split( "idle" );

		// A fight: enemies all round, moving and attacking (the player can't be hurt).
		var (spot, _) = OpenLane( 10f );
		player.GlobalPosition = spot + Vector3.Up * 0.1f;
		DevFlags.FreezeEnemies = false;
		var kinds = new[] { WaveDirector.EnemyKind.Crab, WaveDirector.EnemyKind.Bloober, WaveDirector.EnemyKind.Squilien, WaveDirector.EnemyKind.Spider, WaveDirector.EnemyKind.RockyShark };
		for ( var i = 0; i < 14; i++ )
		{
			var a = i / 14f * MathF.Tau;
			WaveDirector.Current.SpawnAt( kinds[i % kinds.Length], Nav.Closest( player, spot + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * 9f ) + Vector3.Up * 0.3f, spot );
		}
		await Wait( 3f );
		await Split( "fight" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
