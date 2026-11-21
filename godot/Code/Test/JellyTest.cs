using Godot;

/// <summary>
/// The Squilien swims like a jellyfish (user's reference video): the bell squeezes narrow and tall on each stroke and
/// relaxes wide and flat with a wobble, and the tentacles trail slowly instead of whipping. With --shots (a window) it
/// films one side on, a frame every 0.1 s, as jelly_00.png and on.
/// </summary>
public partial class AutoTest
{
	async Task JellyTest()
	{
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		WaveDirector.Current.SpawningPaused = true;
		AllyCommander.KeepAlly = false;
		foreach ( var ally in SeaSpider.All.Where( s => !s.IsEnemy ).ToList() ) ally.QueueFree();
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.Invulnerable = true;
		await WaitForNav();
		var (open, yaw) = OpenLane( 14f );
		player.GlobalPosition = open + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		var ahead = new Angles( 0f, yaw, 0f ).Forward;
		var jelly = Squilien.Spawn( GetTree().CurrentScene, open + ahead * 9f + Vector3.Up * 3.5f, 5000f );
		jelly.Hue = 0.6f;
		jelly.ThrowCooldown = 999f;
		await Wait( 1.5f );
		jelly.WidestBell = 0f;
		jelly.NarrowestBell = 9f;
		jelly.FastestTentacle = 0f;
		var strokes = jelly.PulseCount;

		Transform3D? view = null;
		if ( shots is not null )
		{
			foreach ( var n in GetTree().Root.FindChildren( "*", "", true, false ) )
				if ( n is CanvasLayer cl ) cl.Visible = false;
				else if ( n is ViewModel or ViewModelAxe && n is Node3D vm ) vm.Visible = false;
			player.CameraOverride = _ => view;
		}
		for ( var i = 0; i < 30; i++ )
		{
			var at = jelly.GlobalPosition + Vector3.Down * 0.5f;
			var side = ahead.Cross( Vector3.Up ).Normalized();
			view = new Transform3D( Shapes.LookAt( -side ), at + side * 3.2f );
			if ( shots is not null ) await Shot( $"jelly_{i:00}" );
			await Wait( 0.1f );
		}
		player.CameraOverride = null;
		GD.Print( $"[autotest] jelly: {jelly.PulseCount - strokes} strokes in 3 s, bell width {jelly.NarrowestBell:0.00} to {jelly.WidestBell:0.00} of its rest, fastest tentacle {jelly.FastestTentacle:0.0} m/s against the bell" );
		Check( jelly.PulseCount - strokes >= 2, "the squilien did not swim strokes" );
		Check( jelly.NarrowestBell < 0.88f && jelly.WidestBell > 1.02f, "the squilien's bell does not squeeze narrow and relax wide" );
		Check( jelly.FastestTentacle < 4f, $"the squilien's tentacles whip at {jelly.FastestTentacle:0.0} m/s" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
