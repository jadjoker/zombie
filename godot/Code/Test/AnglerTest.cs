using Godot;

/// <summary>
/// The deep sea angler: pictures (with --shots, a window) from the front, three-quarter, side and of the lantern, the
/// jaw open and the lantern wound back; then it swims at a player standing still and must both swing its lantern into
/// them and chomp them; its eyes take more than its body; and it dies cleanly.
/// </summary>
public partial class AutoTest
{
	async Task AnglerTest()
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
		player.EyeAngles = new Angles( 0f, yaw, 0f );
		var ahead = new Angles( 0f, yaw, 0f ).Forward;

		Check( WaveDirector.Spawners.ContainsKey( WaveDirector.EnemyKind.Angler ), "the wave director cannot spawn anglers" );
		var fish = Angler.Spawn( GetTree().CurrentScene, open + ahead * 8f + Vector3.Up * 1.7f, 2000f );
		fish.GlobalBasis = Shapes.LookAt( -ahead );
		DevFlags.FreezeEnemies = true;
		await Wait( 1f );

		if ( shots is not null )
		{
			foreach ( var n in GetTree().Root.FindChildren( "*", "", true, false ) )
				if ( n is CanvasLayer cl ) cl.Visible = false;
				else if ( n is ViewModel or ViewModelAxe && n is Node3D vm ) vm.Visible = false;
			Transform3D? view = null;
			player.CameraOverride = _ => view;
			player.ThirdPerson = true;   // no first person gun in the frames
			async Task From( string name, Vector3 offset, Vector3? lookAt = null )
			{
				var centre = lookAt ?? fish.GlobalPosition + Vector3.Up * 0.3f;
				var eye = centre + fish.GlobalBasis * offset;
				view = new Transform3D( Shapes.LookAt( centre - eye ), eye );
				foreach ( var child in player.Camera.GetChildren() ) if ( child is Node3D held ) held.Visible = false;
				await Wait( 0.15f );
				await Shot( name );
			}
			await From( "angler_front", new Vector3( 0f, 0.3f, -4.6f ) );
			await From( "angler_three_quarter", new Vector3( 3.2f, 0.6f, -3.6f ) );
			await From( "angler_side", new Vector3( 5f, 0.4f, 0f ) );
			await From( "angler_face", new Vector3( 0.8f, 0.4f, -2.4f ), fish.GlobalPosition + fish.GlobalBasis * new Vector3( 0f, 0.1f, -0.7f ) );
			await From( "angler_lantern", new Vector3( 1.2f, 0.2f, -1.6f ), fish.LanternAt );
			fish.ForceChomp();
			await Wait( 0.3f );
			await From( "angler_chomp", new Vector3( 1.6f, 0.2f, -3.6f ) );
			fish.ForceSwing();
			await Wait( 0.4f );
			await From( "angler_swing", new Vector3( 4.5f, 0.6f, -1.5f ) );
			player.CameraOverride = null;
			player.ThirdPerson = false;
		}
		else
		{
			fish.ForceChomp();
			await Wait( 0.3f );
		}
		GD.Print( $"[autotest] angler: jaw open {fish.JawOpen:0.00} while chomping" );
		Check( fish.JawOpen > 0.55f, $"the angler's jaw only opened {fish.JawOpen:0.00} to chomp" );
		fish.ForceSwing();
		await Wait( 0.4f );
		GD.Print( $"[autotest] angler: lantern swing pose {fish.SwingPose:0.00} on the wind-up" );
		Check( fish.SwingPose < -0.4f, "the angler did not wind its lantern back to swing" );

		// Loose on a player standing still: it must swing the lantern into them and chomp them.
		fish.GlobalPosition = open + ahead * 8f + Vector3.Up * 1.7f;
		DevFlags.FreezeEnemies = false;
		Angler.SwingHits = Angler.ChompHits = 0;
		for ( var waited = 0f; waited < 20f && (Angler.SwingHits == 0 || Angler.ChompHits == 0); waited += 0.25f )
		{
			player.GlobalPosition = open + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			await Wait( 0.25f );
		}
		GD.Print( $"[autotest] angler: {Angler.SwingHits} lantern hits (closest {Angler.LastSwingMiss:0.00} m), {Angler.ChompHits} chomps, {fish.GlobalPosition.DistanceTo( player.GlobalPosition ):0.0} m from the player" );
		Check( Angler.SwingHits > 0, "the angler never hit the player with its lantern" );
		Check( Angler.ChompHits > 0, "the angler never chomped the player" );

		// The eyes are weak spots.
		var parts = CreaturePart.All.Where( p => p.Creature == fish ).ToList();
		var eyePart = parts.FirstOrDefault( p => p.Index == 2 );
		var bodyPart = parts.FirstOrDefault( p => p.Index == 1 );
		Check( eyePart is not null && bodyPart is not null && parts.Any( p => p.Index == 4 ), "the angler is missing its eye, body or lantern hit shapes" );
		if ( eyePart is not null && bodyPart is not null )
		{
			var before = fish.Health.Current;
			bodyPart.ReceiveShot( 10f, bodyPart, Vector3.Forward, bodyPart.GlobalPosition, player.GlobalPosition, player, true );
			var bodyLoss = before - fish.Health.Current;
			before = fish.Health.Current;
			eyePart.ReceiveShot( 10f, eyePart, Vector3.Forward, eyePart.GlobalPosition, player.GlobalPosition, player, true );
			var eyeLoss = before - fish.Health.Current;
			GD.Print( $"[autotest] angler: a 10 shot took {bodyLoss:0} off the body, {eyeLoss:0} off an eye" );
			Check( eyeLoss > bodyLoss * 1.5f, "the angler's eyes are not weak spots" );
		}

		// It dies cleanly.
		fish.Health.OnDamage( new DamageInfo { Damage = 1_000_000f, Attacker = player } );
		await Wait( 0.6f );
		Check( !IsInstanceValid( fish ) || fish.IsDead, "the angler did not die" );
		Check( Angler.All.Count == 0, $"{Angler.All.Count} anglers left after it died" );

		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
