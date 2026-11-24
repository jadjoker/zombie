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
			fish.ForceHook();
			await Wait( 0.5f );
			await From( "angler_gape", new Vector3( 1.2f, 0.2f, -3.2f ) );
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
		fish.HookCooldown = 999f;   // the hook has its own check below
		for ( var waited = 0f; waited < 20f && (Angler.SwingHits == 0 || Angler.ChompHits == 0); waited += 0.25f )
		{
			player.GlobalPosition = open + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			await Wait( 0.25f );
		}
		GD.Print( $"[autotest] angler: {Angler.SwingHits} lantern hits (closest {Angler.LastSwingMiss:0.00} m), {Angler.ChompHits} chomps, {fish.GlobalPosition.DistanceTo( player.GlobalPosition ):0.0} m from the player" );
		Check( Angler.SwingHits > 0, "the angler never hit the player with its lantern" );
		Check( Angler.ChompHits > 0, "the angler never chomped the player" );

		// The hook: from 5 m off it gapes, shoots the claw into the player and slowly reels them in for a bite.
		await Wait( 1.5f );
		var mouthAhead = (fish.MouthAt - fish.GlobalPosition).Flat().SafeNormal();
		player.GlobalPosition = (fish.MouthAt + mouthAhead * 5f) with { Y = open.Y + 0.1f };
		player.Velocity = Vector3.Zero;
		Angler.HookCatches = Angler.HookBites = 0;
		fish.HookCooldown = 8f;
		fish.ForceHook();
		var hookedFrom = 0f;
		var pictured = false;
		for ( var waited = 0f; waited < 9f && Angler.HookBites == 0; waited += 0.1f )
		{
			if ( !fish.Reeling && Angler.HookCatches == 0 ) { player.Velocity = Vector3.Zero; }
			if ( fish.Reeling && hookedFrom == 0f ) hookedFrom = player.GlobalPosition.DistanceTo( fish.GlobalPosition );
			if ( fish.Reeling && shots is not null && !pictured && waited > 1.2f )
			{
				pictured = true;
				var side = fish.GlobalBasis.X * 4.5f + Vector3.Up * 0.8f;
				var middle = (fish.MouthAt + player.GlobalPosition + Vector3.Up * 1.1f) * 0.5f;
				player.ThirdPerson = true;
				player.CameraOverride = _ => new Transform3D( Shapes.LookAt( -side ), middle + side );
				await Wait( 0.1f );
				await Shot( "angler_hook" );
				player.CameraOverride = null;
				player.ThirdPerson = false;
			}
			await Wait( 0.1f );
		}
		GD.Print( $"[autotest] angler: hook caught {Angler.HookCatches}, bit {Angler.HookBites}, reeled from {hookedFrom:0.0} m to {player.GlobalPosition.DistanceTo( fish.GlobalPosition ):0.0} m" );
		Check( Angler.HookCatches > 0, "the angler's hook never caught the player" );
		Check( Angler.HookBites > 0, "the angler never reeled the player in and bit them" );
		Check( !player.Scripted, "the player was left stuck on the angler's hook" );

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

		// The gore: wounds that bleed, teeth knocked out, an eye burst, the lantern shot off, the jaw torn away.
		var max = fish.Health.MaxHealth;
		Transform3D? goreView = null;
		async Task GoreShot( string name, Vector3 offset, Vector3? lookAt = null )
		{
			if ( shots is null ) return;
			var centre = lookAt ?? fish.GlobalPosition + Vector3.Up * 0.2f;
			var eye = centre + fish.GlobalBasis * offset;
			goreView = new Transform3D( Shapes.LookAt( centre - eye ), eye );
			player.CameraOverride = _ => goreView;
			player.ThirdPerson = true;
			foreach ( var n in GetTree().Root.FindChildren( "*", "", true, false ) ) if ( n is CanvasLayer cl ) cl.Visible = false;
			await Wait( 0.12f );
			await Shot( name );
		}
		void HitPart( int index, float share, Vector3 at )
		{
			fish.ReceiveShot( index, max * share, (at - player.Camera.GlobalPosition).Normalized(), at, player.Camera.GlobalPosition, player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ), true );
			fish.Health.Heal( max );   // keep it alive for the next part
		}
		Vector3 Local( Vector3 p ) => fish.GlobalTransform * p;
		var wounds = Angler.Wounds;
		for ( var i = 0; i < 4; i++ ) HitPart( 1, 0.04f, Local( new Vector3( 0.55f - i * 0.3f, 0.1f + 0.1f * i, -0.1f + i * 0.2f ) ) );
		for ( var i = 0; i < 6; i++ ) HitPart( 0, 0.03f, Local( new Vector3( 0.2f * (i % 3 - 1), -0.15f, -0.95f ) ) );
		await Wait( 0.4f );
		await GoreShot( "angler_wounds", new Vector3( 2.6f, 0.5f, -2.8f ) );
		HitPart( 2, 0.1f, Local( new Vector3( -0.4f, 0.46f, -0.52f ) ) );
		await Wait( 0.3f );
		await GoreShot( "angler_eye_out", new Vector3( -1.6f, 0.5f, -2.2f ), Local( new Vector3( -0.4f, 0.4f, -0.5f ) ) );
		for ( var i = 0; i < 3 && !fish.LanternGone; i++ ) HitPart( 4, 0.1f, fish.LanternAt );
		await Wait( 0.5f );
		await GoreShot( "angler_lantern_off", new Vector3( 3.2f, 1.2f, -2.6f ) );
		for ( var i = 0; i < 6 && !fish.JawGone; i++ ) HitPart( 5, 0.08f, Local( new Vector3( 0f, -0.55f, -0.8f ) ) );
		await Wait( 0.5f );
		await GoreShot( "angler_jaw_off", new Vector3( 2.4f, 0.1f, -3f ) );
		GD.Print( $"[autotest] angler: gore: wounds {Angler.Wounds - wounds}, teeth out {Angler.TeethOut}, eyes out {fish.EyesGone}, lantern off {fish.LanternGone}, jaw off {fish.JawGone}, fins off {Angler.FinsOff}" );
		Check( Angler.Wounds - wounds >= 4, "hits on the angler left no wounds" );
		Check( Angler.TeethOut >= 1, "hits on the angler's mouth knocked no teeth out" );
		Check( fish.EyesGone == 1, "a hard hit on an eye did not burst it" );
		Check( fish.LanternGone && fish.JawGone, "the angler's lantern or jaw did not come off" );
		Check( !CreaturePart.All.Any( p => p.Creature == fish && p.Index is 4 or 5 ), "the angler's lantern or jaw can still be shot after coming off" );

		// Badly hurt: the hide darkens and bloodies, and it bleeds as it goes.
		fish.Health.OnDamage( new DamageInfo { Damage = max * 0.8f, Attacker = player } );
		await Wait( 1.2f );
		await GoreShot( "angler_bleeding", new Vector3( 3f, 0.4f, -1.8f ) );
		// It dies cleanly, coming apart.
		var piecesBefore = Angler.DeathPieces;
		var deathAt = fish.GlobalPosition;
		var deathBasis = fish.GlobalBasis;
		fish.Health.OnDamage( new DamageInfo { Damage = 1_000_000f, Attacker = player } );
		if ( shots is not null )
		{
			await Wait( 0.3f );
			var eye = deathAt + Vector3.Up * 0.4f + deathBasis * new Vector3( 3.4f, 0.8f, -3.4f );
			goreView = new Transform3D( Shapes.LookAt( deathAt - eye ), eye );
			await Wait( 0.05f );
			await Shot( "angler_death" );
			await Wait( 1.2f );
			await Shot( "angler_death_after" );
			player.CameraOverride = null;
		}
		await Wait( 0.6f );
		GD.Print( $"[autotest] angler: it came apart into {Angler.DeathPieces - piecesBefore} pieces" );
		Check( Angler.DeathPieces - piecesBefore >= 4, "the angler did not come apart when it died" );
		Check( !IsInstanceValid( fish ) || fish.IsDead, "the angler did not die" );
		Check( Angler.All.Count == 0, $"{Angler.All.Count} anglers left after it died" );

		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
