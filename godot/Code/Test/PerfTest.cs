using Godot;

/// <summary>
/// --perf: frame times in a window, vsync off, through four scenes: standing idle, a gore-heavy fight (a dozen enemies
/// blown apart at once), the Gore Vac sucking up that whole field, and a barrage of Gore Vac shots into a crowd. Prints
/// the average, 95th percentile and worst frame, the engine's process and physics time, draw calls, node and gore
/// counts, and the per-label code timings from Perf. Checks nothing; it is for comparing before and after a change.
/// </summary>
public partial class AutoTest
{
	async Task PerfTest()
	{
		DisplayServer.WindowSetVsyncMode( DisplayServer.VSyncMode.Disabled );
		Engine.MaxFps = 0;
		var me = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		me.Health.Invulnerable = true;
		DevFlags.FreezeEnemies = true;
		if ( WaveDirector.Current is { } director ) director.SpawningPaused = true;
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 1f );
		var (spot, yaw) = OpenLane( 14f );
		var ahead = new Vector3( -MathF.Sin( MathX.Rad( yaw ) ), 0f, -MathF.Cos( MathX.Rad( yaw ) ) );
		var right = ahead.Cross( Vector3.Up );
		var gore = GoreSystem.Get( this );
		void Stand()
		{
			player.GlobalPosition = spot + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
		}
		void LookAt( Vector3 at )
		{
			var to = (at - player.Camera.GlobalPosition).Normalized();
			player.EyeAngles = new Angles( MathX.Deg( -MathF.Asin( to.Y ) ), MathX.Deg( MathF.Atan2( -to.X, -to.Z ) ), 0f );
		}
		var field = spot + ahead * 7f;
		Stand();
		LookAt( field );

		async Task Measure( string name, float seconds, Action everyFrame = null )
		{
			Perf.Clear();
			Perf.Enabled = true;
			var frames = new List<double>();
			double process = 0, physics = 0, draws = 0;
			var last = Time.GetTicksUsec();
			var start = last;
			var before = new Dictionary<string, double>();
			// Where a hitch frame went: our process pass, the end of the frame (redraws, frees), the draw, the physics tick.
			var probe = new FrameProbe { ProcessPriority = int.MaxValue };
			AddChild( probe );
			ulong drawAt = 0, drawnAt = 0, physicsAt = 0;
			void OnPre() => drawAt = Time.GetTicksUsec();
			void OnPost() => drawnAt = Time.GetTicksUsec();
			void OnPhysics() { if ( physicsAt == 0 ) physicsAt = Time.GetTicksUsec(); }
			RenderingServer.FramePreDraw += OnPre;
			RenderingServer.FramePostDraw += OnPost;
			GetTree().PhysicsFrame += OnPhysics;
			while ( (Time.GetTicksUsec() - start) / 1e6 < seconds )
			{
				before.Clear();
				foreach ( var (k, v) in Perf.Totals ) before[k] = v.Ms;
				var gc0 = GC.CollectionCount( 0 ); var gc1 = GC.CollectionCount( 1 ); var gc2 = GC.CollectionCount( 2 );
				double pm = Performance.GetMonitor( Performance.Monitor.PipelineCompilationsMesh ), ps = Performance.GetMonitor( Performance.Monitor.PipelineCompilationsSurface ), psp = Performance.GetMonitor( Performance.Monitor.PipelineCompilationsSpecialization ), pc = Performance.GetMonitor( Performance.Monitor.PipelineCompilationsCanvas ), pd = Performance.GetMonitor( Performance.Monitor.PipelineCompilationsDraw );
				var pipes = Performance.GetMonitor( Performance.Monitor.PipelineCompilationsMesh ) + Performance.GetMonitor( Performance.Monitor.PipelineCompilationsSurface ) + Performance.GetMonitor( Performance.Monitor.PipelineCompilationsSpecialization ) + Performance.GetMonitor( Performance.Monitor.PipelineCompilationsCanvas );
				everyFrame?.Invoke();
				physicsAt = 0;
				await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
				var split = physicsAt == 0 ? "" : $", process {(probe.DoneAt - last) / 1000.0:0}, deferred calls {(probe.QueueAt - probe.DoneAt) / 1000.0:0}, frame end {(drawAt - probe.QueueAt) / 1000.0:0}, draw {(drawnAt - drawAt) / 1000.0:0}, physics {(Time.GetTicksUsec() - physicsAt) / 1000.0:0}";
				var now = Time.GetTicksUsec();
				var ms = (now - last) / 1000.0;
				frames.Add( ms );
				last = now;
				// A hitch: what its own code did that frame, and how many render pipelines were built.
				if ( ms > 40.0 && frames.Count > 1 )
				{
					var spent = Perf.Totals.Select( kv => (kv.Key, kv.Value.Ms - before.GetValueOrDefault( kv.Key )) ).Where( x => x.Item2 > 0.3 ).OrderByDescending( x => x.Item2 ).Take( 10 );
					var newPipes = Performance.GetMonitor( Performance.Monitor.PipelineCompilationsMesh ) + Performance.GetMonitor( Performance.Monitor.PipelineCompilationsSurface ) + Performance.GetMonitor( Performance.Monitor.PipelineCompilationsSpecialization ) + Performance.GetMonitor( Performance.Monitor.PipelineCompilationsCanvas ) - pipes;
					GD.Print( $"[autotest] hitch {name}: {ms:0} ms, gc {GC.CollectionCount( 0 ) - gc0}/{GC.CollectionCount( 1 ) - gc1}/{GC.CollectionCount( 2 ) - gc2}, pipelines built {newPipes:0} (mesh {Performance.GetMonitor( Performance.Monitor.PipelineCompilationsMesh ) - pm:0} surface {Performance.GetMonitor( Performance.Monitor.PipelineCompilationsSurface ) - ps:0} spec {Performance.GetMonitor( Performance.Monitor.PipelineCompilationsSpecialization ) - psp:0} canvas {Performance.GetMonitor( Performance.Monitor.PipelineCompilationsCanvas ) - pc:0} draw {Performance.GetMonitor( Performance.Monitor.PipelineCompilationsDraw ) - pd:0}){split}; code: {string.Join( ", ", spent.Select( x => $"{x.Key} {x.Item2:0}" ) )}" );
				}
				process += Performance.GetMonitor( Performance.Monitor.TimeProcess ) * 1000.0;
				physics += Performance.GetMonitor( Performance.Monitor.TimePhysicsProcess ) * 1000.0;
				draws += Performance.GetMonitor( Performance.Monitor.RenderTotalDrawCallsInFrame );
			}
			RenderingServer.FramePreDraw -= OnPre;
			RenderingServer.FramePostDraw -= OnPost;
			GetTree().PhysicsFrame -= OnPhysics;
			probe.QueueFree();
			Perf.Enabled = false;
			var n = Math.Max( 1, frames.Count );
			var sorted = frames.OrderBy( f => f ).ToList();
			var p95 = sorted[(int)Math.Min( sorted.Count - 1, sorted.Count * 0.95 )];
			var droplets = GooDroplet.Live;
			GD.Print( $"[autotest] perf {name}: {n} frames, avg {frames.Average():0.00} ms ({1000.0 / frames.Average():0} fps), p95 {p95:0.00} ms, worst {sorted[^1]:0.00} ms | process {process / n:0.00} ms, physics {physics / n:0.00} ms, draws {draws / n:0} | nodes {Performance.GetMonitor( Performance.Monitor.ObjectNodeCount ):0}, gibs {gore.Gibs.Count}, droplets {droplets}" );
			foreach ( var (label, t) in Perf.Totals.OrderByDescending( kv => kv.Value.Ms ) )
				GD.Print( $"[autotest] perf {name}:   {label,-16} {t.Ms / n:0.000} ms/frame ({t.Calls / (double)n:0.0} calls/frame)" );
		}

		// ---- Idle ----
		await Measure( "idle", 3f );

		// ---- A gore-heavy fight: a dozen enemies in front blown apart at once ----
		var kinds = new[] { WaveDirector.EnemyKind.Crab, WaveDirector.EnemyKind.Bloober, WaveDirector.EnemyKind.Squilien };
		var enemies = new List<Creature>();
		for ( var i = 0; i < 12; i++ )
		{
			var at = Nav.Closest( player, field + right * ((i % 4) - 1.5f) * 2f + ahead * (i / 4 - 1) * 2f ) + Vector3.Up * 0.4f;
			if ( WaveDirector.Current.SpawnAt( kinds[i % 3], at, spot ) is Creature c ) enemies.Add( c );
		}
		await Wait( 1.2f );
		// The kill lands inside the measurement (its third frame), so the frame that blows them all apart is counted
		// (it used to happen just before, and the worst frame was missed).
		var killFrame = 0;
		await Measure( "gore fight", 3f, () =>
		{
			if ( ++killFrame != 3 ) return;
			using ( Perf.Time( "kill all" ) )
				foreach ( var c in enemies.Where( c => IsInstanceValid( c ) && !c.IsDead ) )
					c.ReceiveShot( 0, 99999f, ahead, c.GlobalPosition, player.Camera.GlobalPosition, me, false );
		} );

		// ---- The Gore Vac sucking up the field ----
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		inventory.Add( PlayerWeapon.GoreVac() );
		await Wait( 0.6f );
		var vac = inventory.Current;
		vac.ForceSuck = true;
		Vector3 Centre()
		{
			var near = gore.Gibs.Where( g => g.GlobalPosition.DistanceTo( spot ) < 20f ).ToList();
			return near.Count > 0 ? near.Aggregate( Vector3.Zero, ( a, g ) => a + g.GlobalPosition ) / near.Count : field;
		}
		await Measure( "gore vac suck", 4f, () => { Stand(); LookAt( Centre() ); } );
		vac.ForceSuck = false;

		// ---- A barrage of shots into a crowd ----
		enemies.Clear();
		for ( var i = 0; i < 8; i++ )
		{
			var at = Nav.Closest( player, field + right * ((i % 4) - 1.5f) * 2.2f + ahead * (i / 4) * 2.5f ) + Vector3.Up * 0.4f;
			if ( WaveDirector.Current.SpawnAt( kinds[i % 3], at, spot ) is Creature c ) enemies.Add( c );
		}
		await Wait( 1f );
		vac.InfiniteAmmo = true;
		TimeSince sinceShot = 0f;
		var target = 0;
		await Measure( "gore vac barrage", 4f, () =>
		{
			Stand();
			var alive = enemies.Where( c => IsInstanceValid( c ) && !c.IsDead ).ToList();
			LookAt( alive.Count > 0 ? alive[target % alive.Count].GlobalPosition + Vector3.Up * 0.4f : field );
			if ( sinceShot > 0.15f ) { sinceShot = 0f; target++; vac.TestSpit( target % 6 == 0 ); }
		} );
		vac.InfiniteAmmo = false;
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}

/// <summary> Runs after every other node's _Process (perf tests): when the process pass ended. </summary>
public partial class FrameProbe : Node
{
	public ulong DoneAt, QueueAt;
	public override void _Process( double delta )
	{
		Callable.From( () => QueueAt = Time.GetTicksUsec() ).CallDeferred();
		DoneAt = Time.GetTicksUsec();
	}
}
