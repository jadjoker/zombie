using Godot;

/// <summary>
/// --perfsplit: where the idle frame goes. Stands in the first area, measures, then hides each big piece of the scene
/// in turn (a top-level subtree, the post effects, the HUD) for a moment and measures again; prints the node count of
/// each piece and how much faster the frame was without it. Checks nothing; it guides the optimisation pass.
/// </summary>
public partial class AutoTest
{
	async Task PerfSplitTest()
	{
		DisplayServer.WindowSetVsyncMode( DisplayServer.VSyncMode.Disabled );
		Engine.MaxFps = 0;
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		if ( WaveDirector.Current is { } director ) director.SpawningPaused = true;
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 2f );
		async Task<double> Frame( float seconds )
		{
			var frames = new List<double>();
			var last = Time.GetTicksUsec();
			var start = last;
			while ( (Time.GetTicksUsec() - start) / 1e6 < seconds )
			{
				await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
				var now = Time.GetTicksUsec();
				frames.Add( (now - last) / 1000.0 );
				last = now;
			}
			frames.Sort();
			return frames[frames.Count / 2];   // the median frame: steadier than the mean
		}
		static int Count( Node n ) { var c = 1; foreach ( var k in n.GetChildren() ) c += Count( k ); return c; }
		var baseline = await Frame( 2f );
		GD.Print( $"[autotest] split: baseline median {baseline:0.00} ms, {Count( GetTree().Root )} nodes" );
		var scene = GetTree().CurrentScene;
		var pieces = new List<Node>();
		foreach ( var child in scene.GetChildren() ) if ( Count( child ) >= 40 || child is GpuParticles3D or WorldEnvironment ) pieces.Add( child );
		foreach ( var level in scene.FindChildren( "*", "", false, false ) ) { }
		// Big subtrees one level down inside the level too.
		if ( Level.Current is { } lv ) foreach ( var child in lv.GetChildren() ) if ( Count( child ) >= 60 ) pieces.Add( child );
		foreach ( var piece in pieces.Distinct() )
		{
			if ( piece is CanvasLayer cl ) { cl.Visible = false; }
			else if ( piece is Node3D n3 ) n3.Visible = false;
			else if ( piece is WorldEnvironment ) { }
			else continue;
			var without = await Frame( 1.2f );
			if ( piece is CanvasLayer cl2 ) cl2.Visible = true; else if ( piece is Node3D n4 ) n4.Visible = true;
			await Wait( 0.3f );
			GD.Print( $"[autotest] split: {piece.Name,-28} {Count( piece ),6} nodes   hidden: {without:0.00} ms ({baseline - without:+0.00;-0.00} ms)" );
		}
		// The full-screen post effects.
		foreach ( var post in scene.FindChildren( "*", "", true, false ).OfType<CanvasItem>().Where( c => c.Material is ShaderMaterial && c is ColorRect && c.IsVisibleInTree() ).ToList() )
		{
			post.Visible = false;
			var without = await Frame( 1.2f );
			post.Visible = true;
			await Wait( 0.3f );
			GD.Print( $"[autotest] split: post {post.Name,-23}        hidden: {without:0.00} ms ({baseline - without:+0.00} ms)" );
		}
		// GPU against CPU, and each environment effect and the sun's shadows switched off in turn.
		var vp = GetViewport().GetViewportRid();
		RenderingServer.ViewportSetMeasureRenderTime( vp, true );
		await Frame( 0.5f );
		GD.Print( $"[autotest] split: gpu {RenderingServer.ViewportGetMeasuredRenderTimeGpu( vp ):0.00} ms, render cpu {RenderingServer.ViewportGetMeasuredRenderTimeCpu( vp ):0.00} ms" );
		var env = GetViewport().World3D.Environment ?? scene.FindChildren( "*", "WorldEnvironment", true, false ).OfType<WorldEnvironment>().FirstOrDefault()?.Environment;
		var sunLight = scene.FindChildren( "*", "DirectionalLight3D", true, false ).OfType<DirectionalLight3D>().FirstOrDefault( l => l.ShadowEnabled );
		if ( env is not null )
		{
			foreach ( var prop in new[] { "ssao_enabled", "ssil_enabled", "volumetric_fog_enabled", "glow_enabled", "fog_enabled", "sdfgi_enabled", "ssr_enabled" } )
			{
				if ( !(bool)env.Get( prop ) ) continue;
				env.Set( prop, false );
				var without = await Frame( 1.2f );
				var gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu( vp );
				env.Set( prop, true );
				await Wait( 0.3f );
				GD.Print( $"[autotest] split: env {prop,-24}        off: {without:0.00} ms ({baseline - without:+0.00;-0.00} ms), gpu {gpu:0.00}" );
			}
		}
		if ( sunLight is not null )
		{
			sunLight.ShadowEnabled = false;
			var without = await Frame( 1.2f );
			sunLight.ShadowEnabled = true;
			GD.Print( $"[autotest] split: sun shadows                      off: {without:0.00} ms ({baseline - without:+0.00;-0.00} ms)" );
		}
		var shadowed = scene.FindChildren( "*", "Light3D", true, false ).OfType<Light3D>().Count( l => l is not DirectionalLight3D && l.ShadowEnabled && l.IsVisibleInTree() );
		var lights = scene.FindChildren( "*", "Light3D", true, false ).OfType<Light3D>().Count( l => l is not DirectionalLight3D && l.IsVisibleInTree() );
		GD.Print( $"[autotest] split: {lights} omni/spot lights visible, {shadowed} with shadows" );
		// Who runs code every frame: nodes processing, by type.
		var all = new List<Node>();
		void Walk( Node n ) { all.Add( n ); foreach ( var k in n.GetChildren() ) Walk( k ); }
		Walk( GetTree().Root );
		foreach ( var g in all.Where( n => n.IsProcessing() || n.IsPhysicsProcessing() ).GroupBy( n => n.GetType().Name + (n.IsPhysicsProcessing() ? " (physics)" : "") ).OrderByDescending( g => g.Count() ).Take( 25 ) )
			GD.Print( $"[autotest] split: processing {g.Key,-30} {g.Count()}" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
