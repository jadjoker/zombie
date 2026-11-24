using Godot;

/// <summary>
/// The sting ray (--stingray): pictures (with --shots, a window) from above (the sand camouflage), the front (the alien
/// eyes), underneath, the zap and the dive; then it must sneak (freezing while watched), zap a player standing still
/// (stunning them about 0.6 s), climb and nose dive into them, take half damage while diving, glide in over the hills
/// without any part going into the ground, have weak eyes, bleed, lose an eye and its tail, and come apart when it dies.
/// </summary>
public partial class AutoTest
{
	async Task StingrayTest()
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

		Check( WaveDirector.Spawners.ContainsKey( WaveDirector.EnemyKind.Stingray ), "the wave director cannot spawn sting rays" );
		Check( WaveDirector.Current.MaxStingrays == 4, "more than 4 sting rays may be alive at once" );
		Stingray.TrackClearance = true;
		var ray = Stingray.Spawn( GetTree().CurrentScene, open + ahead * 5f + Vector3.Up * 2f, 2000f );
		ray.GlobalBasis = Shapes.LookAt( -ahead );
		await Wait( 2.2f );
		DevFlags.FreezeEnemies = true;
		await Wait( 0.3f );
		var settled = ray.GlobalPosition.Y - ray.GroundUnder;
		GD.Print( $"[autotest] stingray: settled {settled:0.00} m over the ground" );
		Check( settled > 0.2f && settled < 0.7f, $"the sting ray did not settle low over the ground ({settled:0.00} m)" );

		if ( shots is not null )
		{
			foreach ( var n in GetTree().Root.FindChildren( "*", "", true, false ) )
				if ( n is CanvasLayer cl ) cl.Visible = false;
				else if ( n is ViewModel or ViewModelAxe && n is Node3D vm ) vm.Visible = false;
			Transform3D? view = null;
			player.CameraOverride = _ => view;
			player.ThirdPerson = true;
			async Task From( string name, Vector3 offset, Vector3? lookAt = null )
			{
				var centre = lookAt ?? ray.GlobalPosition;
				var eye = centre + ray.GlobalBasis * offset;
				view = new Transform3D( Shapes.LookAt( centre - eye ), eye );
				await Wait( 0.15f );
				await Shot( name );
			}
			await From( "stingray_above", new Vector3( 1.2f, 3.4f, 1.8f ) );
			await From( "stingray_front", new Vector3( 0.6f, 0.5f, -2.4f ) );
			await From( "stingray_eyes", new Vector3( 0.35f, 0.45f, -1.1f ), ray.GlobalPosition + ray.GlobalBasis * new Vector3( 0f, 0.15f, -0.25f ) );
			await From( "stingray_side", new Vector3( 3f, 0.4f, 0.4f ) );
			await From( "stingray_far", new Vector3( 2.5f, 2.2f, -6f ) );
			// Lifted up to see its belly.
			var low = ray.GlobalPosition;
			ray.GlobalPosition = low + Vector3.Up * 1.8f;
			await Wait( 0.2f );
			await From( "stingray_belly", new Vector3( 0.4f, -1.5f, -0.9f ) );
			ray.GlobalPosition = low;
			player.CameraOverride = null;
			player.ThirdPerson = false;
		}

		// Sneaky: from afar, watched, it freezes flat; looked away from, it comes on.
		DevFlags.FreezeEnemies = false;
		ray.GlobalPosition = open + ahead * 9f + Vector3.Up * 0.5f;
		ray.Calm();
		player.EyeAngles = new Angles( 0f, yaw, 0f );
		await Wait( 0.5f );
		var watchedNow = ray.Watched;
		player.EyeAngles = new Angles( 0f, yaw + 180f, 0f );
		await Wait( 0.3f );
		GD.Print( $"[autotest] stingray: watched {watchedNow}, then looked away {ray.Watched}" );
		Check( watchedNow && !ray.Watched, "the sting ray does not freeze while watched" );

		// The zap: it rears up, zaps a player standing still and stuns them about 0.6 s.
		player.EyeAngles = new Angles( 0f, yaw, 0f );
		ray.GlobalPosition = open + ahead * 2.2f + Vector3.Up * 0.5f;
		Stingray.ZapHits = 0;
		Electrocuted.Stuns = 0;
		var stunnedFor = 0f;
		var sawCharge = 0f;
		for ( var waited = 0f; waited < 8f && Stingray.ZapHits == 0; waited += 0.02f )
		{
			player.GlobalPosition = player.GlobalPosition with { X = open.X, Z = open.Z };
			sawCharge = MathF.Max( sawCharge, ray.Charge );
			await Wait( 0.02f );
		}
		if ( shots is not null )
		{
			Transform3D? view = new Transform3D( Shapes.LookAt( ray.GlobalPosition - (player.GlobalPosition + Vector3.Up * 1.4f + player.GlobalBasis.X * 2.2f) ), player.GlobalPosition + Vector3.Up * 1.4f + player.GlobalBasis.X * 2.2f );
			player.CameraOverride = _ => view;
			await Shot( "stingray_zap" );
			player.CameraOverride = null;
		}
		while ( player.Scripted && stunnedFor < 3f ) { stunnedFor += 0.02f; await Wait( 0.02f ); }
		stunnedFor += 0.03f;
		GD.Print( $"[autotest] stingray: zaps {Stingray.ZapHits}, stuns {Electrocuted.Stuns}, held {stunnedFor:0.00} s, charge seen {sawCharge:0.00}" );
		Check( Stingray.ZapHits > 0, "the sting ray never zapped the player" );
		Check( sawCharge > 0.5f, "the sting ray did not charge up before the zap" );
		Check( Electrocuted.Stuns > 0 && stunnedFor > 0.45f && stunnedFor < 0.85f, $"the zap did not stun the player about 0.6 s ({stunnedFor:0.00} s)" );
		Check( !player.Scripted, "the player was left stuck after the zap" );

		// The dive: it climbs above the player, nose dives spinning into them, and pulls out above the sand.
		await Wait( 1.5f );
		ray.GlobalPosition = open + ahead * 8f + Vector3.Up * 0.5f;
		Stingray.DiveHits = 0;
		var dives = Stingray.Dives;
		ray.ForceDive();
		var peak = 0f;
		var shotDive = false;
		var halfChecked = false;
		var bodyPart = CreaturePart.All.FirstOrDefault( p => p.Creature == ray && p.Index == 1 );
		var diveLoss = 0f;
		var normalLoss = 0f;
		if ( bodyPart is not null )
		{
			var before = ray.Health.Current;
			bodyPart.ReceiveShot( 10f, bodyPart, Vector3.Forward, bodyPart.GlobalPosition, player.GlobalPosition, player, false );
			normalLoss = before - ray.Health.Current;
		}
		for ( var waited = 0f; waited < 7f && (Stingray.DiveHits == 0 || ray.Diving); waited += 0.02f )
		{
			player.GlobalPosition = player.GlobalPosition with { X = open.X, Z = open.Z };
			peak = MathF.Max( peak, ray.GlobalPosition.Y - ray.GroundUnder );
			if ( ray.Diving && !halfChecked && bodyPart is not null && IsInstanceValid( bodyPart ) )
			{
				halfChecked = true;
				var before = ray.Health.Current;
				bodyPart.ReceiveShot( 10f, bodyPart, Vector3.Forward, bodyPart.GlobalPosition, player.GlobalPosition, player, false );
				diveLoss = before - ray.Health.Current;
			}
			if ( shots is not null && !shotDive && ray.ActName == "Dive" )
			{
				shotDive = true;
				var eye = player.GlobalPosition + Vector3.Up * 1.2f - ahead * 3f + player.GlobalBasis.X * 3f;
				Transform3D? view = new Transform3D( Shapes.LookAt( ray.GlobalPosition - eye ), eye );
				player.CameraOverride = _ => view;
				await Shot( "stingray_dive" );
				player.CameraOverride = null;
			}
			await Wait( 0.02f );
		}
		await Wait( 1.2f );
		GD.Print( $"[autotest] stingray: dives {Stingray.Dives - dives}, dive hits {Stingray.DiveHits}, climbed {peak:0.0} m, a 10 shot took {normalLoss:0.0} normally and {diveLoss:0.0} diving" );
		Check( Stingray.Dives > dives, "the sting ray never nose dived" );
		Check( Stingray.DiveHits > 0, "the sting ray's dive never hit the player" );
		Check( peak > 4f, $"the sting ray only climbed {peak:0.0} m before diving" );
		Check( halfChecked && MathF.Abs( diveLoss - normalLoss * Stingray.DiveDamageScale ) < 0.6f, $"the sting ray does not take half damage while diving ({diveLoss:0.0} vs {normalLoss:0.0})" );
		Check( ray.GlobalPosition.Y - ray.GroundUnder > Stingray.MinClear - 0.02f, "the sting ray ended its dive in the ground" );

		// Over the hills: from out past the basin it glides in, and no part of it goes into the ground.
		DevFlags.FreezeEnemies = true;
		var outward = open.Flat().IsNearlyZero() ? Vector3.Forward : open.Flat().Normalized();
		var far = Nav.Closest( player, outward * 52f );
		ray.GlobalPosition = far with { Y = Sculpt.GroundAt( far ) + 0.5f };
		ray.GlobalBasis = Shapes.LookAt( (open - far).Flat() );
		await Wait( 0.3f );
		Stingray.LowestClearance = 99f;
		ray.Calm();
		DevFlags.FreezeEnemies = false;
		var startDist = ray.GlobalPosition.DistanceTo( open );
		var hills = 0f;
		for ( var waited = 0f; waited < 9f; waited += 0.1f )
		{
			player.EyeAngles = new Angles( 0f, yaw + 180f, 0f );
			player.GlobalPosition = player.GlobalPosition with { X = open.X, Z = open.Z };
			hills = MathF.Max( hills, Sculpt.GroundAt( ray.GlobalPosition ) - Sculpt.GroundAt( open ) );
			await Wait( 0.1f );
		}
		var endDist = ray.GlobalPosition.DistanceTo( open );
		GD.Print( $"[autotest] stingray: over the hills from {startDist:0} m to {endDist:0} m (ground up to {hills:0.0} m above the basin), lowest clearance {Stingray.LowestClearance:0.00} m" );
		Check( endDist < startDist - 10f, "the sting ray did not glide in over the hills" );
		Check( Stingray.LowestClearance > 0f, $"part of the sting ray went into the ground ({Stingray.LowestClearance:0.00} m)" );

		// The eyes are weak spots.
		DevFlags.FreezeEnemies = true;
		ray.GlobalPosition = open + ahead * 4f + Vector3.Up * 0.5f;
		ray.GlobalBasis = Shapes.LookAt( -ahead );
		await Wait( 0.5f );
		var parts = CreaturePart.All.Where( p => p.Creature == ray ).ToList();
		var eyePart = parts.FirstOrDefault( p => p.Index == 2 );
		bodyPart = parts.FirstOrDefault( p => p.Index == 1 );
		Check( eyePart is not null && bodyPart is not null && parts.Any( p => p.Index == 4 ), "the sting ray is missing its eye, body or tail hit shapes" );
		if ( eyePart is not null && bodyPart is not null )
		{
			var before = ray.Health.Current;
			bodyPart.ReceiveShot( 10f, bodyPart, Vector3.Forward, bodyPart.GlobalPosition, player.GlobalPosition, player, true );
			var bodyLoss = before - ray.Health.Current;
			before = ray.Health.Current;
			eyePart.ReceiveShot( 10f, eyePart, Vector3.Forward, eyePart.GlobalPosition, player.GlobalPosition, player, true );
			var eyeLoss = before - ray.Health.Current;
			GD.Print( $"[autotest] stingray: a 10 shot took {bodyLoss:0} off the body, {eyeLoss:0} off an eye" );
			Check( eyeLoss > bodyLoss * 1.5f, "the sting ray's eyes are not weak spots" );
		}

		// Gore: wounds, an eye burst, the tail shot off.
		var max = ray.Health.MaxHealth;
		void HitPart( int index, float share, Vector3 at )
		{
			ray.ReceiveShot( index, max * share, (at - player.Camera.GlobalPosition).Normalized(), at, player.Camera.GlobalPosition, survivor, true );
			ray.Health.Heal( max );
		}
		var wounds = Stingray.Wounds;
		for ( var i = 0; i < 4; i++ ) HitPart( 1, 0.04f, ray.GlobalTransform * new Vector3( 0.3f - i * 0.2f, 0.06f, 0.1f ) );
		var eyeNode = CreaturePart.All.FirstOrDefault( p => p.Creature == ray && p.Index == 2 );
		if ( eyeNode is not null ) HitPart( 2, 0.1f, eyeNode.GlobalPosition );
		var tailNode = CreaturePart.All.FirstOrDefault( p => p.Creature == ray && p.Index == 4 );
		for ( var i = 0; i < 3 && !ray.TailGone && tailNode is not null; i++ ) HitPart( 4, 0.1f, tailNode.GlobalPosition );
		await Wait( 0.5f );
		if ( shots is not null )
		{
			var eye = ray.GlobalPosition + ray.GlobalBasis * new Vector3( 1.8f, 1.6f, -1.6f );
			Transform3D? view = new Transform3D( Shapes.LookAt( ray.GlobalPosition - eye ), eye );
			player.CameraOverride = _ => view;
			player.ThirdPerson = true;
			await Wait( 0.1f );
			await Shot( "stingray_wounds" );
		}
		GD.Print( $"[autotest] stingray: gore: wounds {Stingray.Wounds - wounds}, eyes out {ray.EyesGone}, tail off {ray.TailGone}" );
		Check( Stingray.Wounds - wounds >= 4, "hits on the sting ray left no wounds" );
		Check( ray.EyesGone == 1, "a hard hit on an eye did not burst it" );
		Check( ray.TailGone, "the sting ray's tail did not come off" );

		// It dies cleanly, coming apart.
		var piecesBefore = Stingray.DeathPieces;
		ray.Health.OnDamage( new DamageInfo { Damage = 1_000_000f, Attacker = player } );
		if ( shots is not null )
		{
			await Wait( 0.4f );
			await Shot( "stingray_death" );
			player.CameraOverride = null;
		}
		await Wait( 0.6f );
		GD.Print( $"[autotest] stingray: it came apart into {Stingray.DeathPieces - piecesBefore} pieces" );
		Check( Stingray.DeathPieces - piecesBefore >= 2, "the sting ray did not come apart when it died" );
		Check( Stingray.All.Count == 0, $"{Stingray.All.Count} sting rays left after it died" );
		Check( !player.Scripted, "the player is still held" );

		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
