using Godot;

/// <summary>
/// King Raymond IV (--kingray), on the wreck site: he must be wave 15's boss (not MC-51) with his health, wear his crown,
/// and land all five attacks on a player standing still: the zap (a stun of about a second), the nose dive (half damage
/// while diving, a shockwave on landing), the carpet bomb (marks that follow and then lock before nine bombs fall), the
/// sand ambush (under the sand with only his crown and eyes showing, a glowing spot, then he bursts up and launches the
/// player) and the shock rings (warning rings first; a player on the ground is hit, one in the air jumps them). No part of
/// him may go into the ground except while buried, and he comes apart, crown and all, when he dies, leaving nothing behind.
/// With --shots (a window), pictures of him and of each attack.
/// </summary>
public partial class AutoTest
{
	async Task KingRaymondTest()
	{
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		var d = WaveDirector.Current;
		d.SpawningPaused = true;
		AllyCommander.KeepAlly = false;
		foreach ( var ally in SeaSpider.All.Where( s => !s.IsEnemy ).ToList() ) ally.QueueFree();
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.Invulnerable = true;
		var level = GetTree().CurrentScene as Node3D;
		AreaTwo.Go( level, player );
		await Wait( 1f );
		Check( SeaTerrain.Area == 2, "did not get to the wreck site" );
		await WaitForNav();
		var (open, yaw) = OpenLane( 22f );
		player.GlobalPosition = open + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		player.EyeAngles = new Angles( 0f, yaw, 0f );
		var ahead = new Angles( 0f, yaw, 0f ).Forward;
		var side = new Vector3( -ahead.Z, 0f, ahead.X );

		// Wave 15 is his (the others keep theirs).
		var waveWas = d.Wave;
		d.Wave = 15; var on15 = d.BossKind;
		d.Wave = 10; var on10 = d.BossKind;
		d.Wave = 20; var on20 = d.BossKind;
		d.Wave = waveWas;
		Check( on15 == WaveDirector.EnemyKind.KingRaymond && on10 == WaveDirector.EnemyKind.Boss && on20 == WaveDirector.EnemyKind.Davey, $"the boss waves are wrong (10 {on10}, 15 {on15}, 20 {on20})" );
		// The real thing: wave 15 starts and he comes.
		d.JumpToWave( 15 );
		d.SpawningPaused = false;
		for ( var t = 0f; t < 8f && KingRaymond.All.Count == 0; t += 0.1f ) await Wait( 0.1f );
		d.SpawningPaused = true;
		foreach ( var c in Creature.Everyone.Where( c => c is not KingRaymond ).ToList() ) c.QueueFree();
		var king = KingRaymond.All.FirstOrDefault();
		GD.Print( $"[autotest] kingray: wave {d.Wave}, kings {KingRaymond.All.Count}, MC-51s {MC61.All.Count}, health {king?.Health?.MaxHealth ?? 0:0}" );
		Check( king is not null, "King Raymond IV did not come on wave 15" );
		Check( MC61.All.Count == 0, "MC-51 came on wave 15 too" );
		if ( king is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		Check( MathF.Abs( king.Health.MaxHealth - d.KingRaymondHealth ) < 1f && d.KingRaymondHealth >= 6000f, $"King Raymond's health is {king.Health.MaxHealth:0}" );
		Check( king.HasCrown, "King Raymond has no crown" );
		Check( king.Size >= 2.5f, "King Raymond is not much bigger than a sting ray" );

		Stingray.TrackClearance = true;
		Stingray.LowestClearance = 99f;
		void Place( Vector3 at, Vector3 face )
		{
			king.GlobalPosition = at with { Y = Sculpt.GroundAt( at ) + king.CruiseHeight };
			if ( !face.Flat().IsNearlyZero() ) king.GlobalBasis = Shapes.LookAt( face.Flat() );
		}
		void Hold() => player.GlobalPosition = player.GlobalPosition with { X = open.X, Z = open.Z };
		Place( open + ahead * 9f, -ahead );
		king.Calm();
		DevFlags.FreezeEnemies = true;
		await Wait( 1f );

		Transform3D? view = null;
		if ( shots is not null )
		{
			foreach ( var n in GetTree().Root.FindChildren( "*", "", true, false ) )
				if ( n is ViewModel or ViewModelAxe && n is Node3D vm ) vm.Visible = false;
			player.CameraOverride = _ => view;
			player.ThirdPerson = true;
			async Task From( string name, Vector3 offset, Vector3? lookAt = null )
			{
				var centre = lookAt ?? king.GlobalPosition;
				var eye = centre + king.GlobalBasis * offset;
				view = new Transform3D( Shapes.LookAt( centre - eye ), eye );
				await Wait( 0.15f );
				await Shot( name );
			}
			await From( "kingray_front", new Vector3( 1.8f, 1.6f, -7f ) );
			await From( "kingray_above", new Vector3( 3f, 7f, 4f ) );
			await From( "kingray_crown", new Vector3( 0.9f, 1.2f, -2.6f ), king.GlobalPosition + king.GlobalBasis * new Vector3( 0f, 0.5f, -0.9f ) );
			await From( "kingray_side", new Vector3( 8f, 1.2f, 0.5f ) );
			view = null;
			player.ThirdPerson = false;
		}
		DevFlags.FreezeEnemies = false;

		// 1. The zap: a stun of about a second.
		Place( open + ahead * 4.5f, -ahead );
		king.Calm();
		Stingray.ZapHits = 0;
		Electrocuted.Stuns = 0;
		king.ForceAttack( "Zap" );
		for ( var t = 0f; t < 6f && Stingray.ZapHits == 0; t += 0.02f ) { Hold(); await Wait( 0.02f ); }
		var stunned = 0f;
		if ( shots is not null ) await ShotFrom( "kingray_zap", king.GlobalPosition + side * 7f + Vector3.Up * 2f, king.GlobalPosition );
		while ( player.Scripted && stunned < 3f ) { stunned += 0.02f; await Wait( 0.02f ); }
		stunned += 0.03f;
		GD.Print( $"[autotest] kingray: zap hits {Stingray.ZapHits}, held {stunned:0.00} s, line \"{king.Line}\"" );
		Check( Stingray.ZapHits > 0, "King Raymond's zap never hit" );
		Check( Electrocuted.Stuns > 0 && stunned > 0.85f && stunned < 1.3f, $"King Raymond's zap did not stun about 1 s ({stunned:0.00} s)" );
		Check( !player.Scripted, "the player was left stuck after the zap" );

		// 2. The nose dive: half damage while diving, a shockwave where he lands.
		await Wait( 1f );
		Place( open + ahead * 13f, -ahead );
		king.Calm();
		Stingray.DiveHits = 0;
		var dives = Stingray.Dives;
		var shocks = Stingray.DiveShocks;
		var body = CreaturePart.All.FirstOrDefault( p => p.Creature == king && p.Index == 1 );
		var normalLoss = 0f;
		var diveLoss = 0f;
		if ( body is not null )
		{
			var before = king.Health.Current;
			body.ReceiveShot( 10f, body, Vector3.Forward, body.GlobalPosition, player.GlobalPosition, player, false );
			normalLoss = before - king.Health.Current;
		}
		king.ForceAttack( "Dive" );
		var peak = 0f;
		var halfChecked = false;
		var shotDive = false;
		for ( var t = 0f; t < 8f && (Stingray.DiveHits == 0 || king.Diving); t += 0.02f )
		{
			Hold();
			peak = MathF.Max( peak, king.GlobalPosition.Y - king.GroundUnder );
			if ( king.Diving && !halfChecked && body is not null && IsInstanceValid( body ) )
			{
				halfChecked = true;
				var before = king.Health.Current;
				body.ReceiveShot( 10f, body, Vector3.Forward, body.GlobalPosition, player.GlobalPosition, player, false );
				diveLoss = before - king.Health.Current;
			}
			if ( shots is not null && !shotDive && king.ActName == "Dive" ) { shotDive = true; await ShotFrom( "kingray_dive", player.GlobalPosition + side * 9f + Vector3.Up * 3f - ahead * 4f, king.GlobalPosition ); }
			await Wait( 0.02f );
		}
		await Wait( 1.2f );
		GD.Print( $"[autotest] kingray: dives {Stingray.Dives - dives}, hits {Stingray.DiveHits}, climbed {peak:0.0} m, landing shockwaves {Stingray.DiveShocks - shocks}, a 10 shot took {normalLoss:0.0} normally and {diveLoss:0.0} diving" );
		Check( Stingray.Dives > dives && Stingray.DiveHits > 0, "King Raymond's nose dive never hit" );
		Check( peak > 8f, $"King Raymond only climbed {peak:0.0} m to dive" );
		Check( halfChecked && MathF.Abs( diveLoss - normalLoss * Stingray.DiveDamageScale ) < 0.6f, $"King Raymond does not take half damage while diving ({diveLoss:0.0} vs {normalLoss:0.0})" );
		Check( Stingray.DiveShocks > shocks, "King Raymond's dive landing threw no shockwave" );

		// 3. The carpet bomb: the marks follow, lock, and nine bombs fall along them onto the player.
		await Wait( 1f );
		Place( open + ahead * 12f, -ahead );
		king.Calm();
		var dropped = Stingray.BombsDropped;
		Stingray.BombHits = 0;
		king.ForceAttack( "Carpet bomb" );
		bool sawFollow = false, sawLock = false, lockedBeforeRun = false, shotMarks = false, shotBombs = false;
		for ( var t = 0f; t < 14f && king.ActName != "Prowl"; t += 0.02f )
		{
			Hold();
			if ( king.ActName == "BombMark" && king.CueShowing ) { if ( king.LineLocked ) sawLock = true; else sawFollow = true; }
			if ( king.ActName == "BombRun" && !lockedBeforeRun ) lockedBeforeRun = sawLock;
			if ( shots is not null && !shotMarks && king.LineLocked && king.ActName == "BombMark" ) { shotMarks = true; await ShotFrom( "kingray_bomb_marks", open - ahead * 9f + side * 5f + Vector3.Up * 6f, open + ahead * 2f ); }
			if ( shots is not null && !shotBombs && king.BombsOut >= 5 ) { shotBombs = true; await ShotFrom( "kingray_bombs", open - ahead * 10f + side * 8f + Vector3.Up * 4f, open + Vector3.Up * 3f ); }
			await Wait( 0.02f );
		}
		await Wait( 1.5f );
		GD.Print( $"[autotest] kingray: carpet bomb: marks followed {sawFollow}, locked {sawLock} (before the run {lockedBeforeRun}), bombs {Stingray.BombsDropped - dropped}, hits {Stingray.BombHits}, bombs left {RayBomb.All.Count}" );
		Check( sawFollow && sawLock && lockedBeforeRun, "the carpet bomb's marks did not show, follow and lock before the run" );
		Check( Stingray.BombsDropped - dropped == king.BombCount, $"King Raymond dropped {Stingray.BombsDropped - dropped} bombs, not {king.BombCount}" );
		Check( Stingray.BombHits > 0, "the carpet bomb never hit the player standing on the line" );
		Check( RayBomb.All.Count == 0 && !king.CueShowing, "bombs or marks were left after the carpet bomb" );

		// 4. The sand ambush: under the sand (only the crown and eyes showing), a glowing spot, then he bursts up and launches the player.
		await Wait( 1f );
		Place( open + ahead * 14f, -ahead );
		king.Calm();
		var eruptions = Stingray.Eruptions;
		Stingray.EruptHits = 0;
		king.ForceAttack( "Sand ambush" );
		bool sawBuried = false, hidden = true, crownUp = true, sawCue = false, shotBuried = false, shotCue = false, shotBurst = false;
		var deepest = 0f;
		var launched = 0f;
		for ( var t = 0f; t < 14f && king.ActName != "Prowl"; t += 0.02f )
		{
			Hold();
			if ( king.ActName == "Buried" )
			{
				sawBuried = true;
				hidden &= !king.DiscShowing;
				var sand = Sculpt.GroundAt( king.GlobalPosition );
				deepest = MathF.Max( deepest, sand - king.GlobalPosition.Y );
				var top = CreaturePart.All.FirstOrDefault( p => p.Creature == king && p.Index == 7 );
				crownUp &= top is not null && top.GlobalPosition.Y > sand;
				if ( shots is not null && !shotBuried && t > 1.5f ) { shotBuried = true; await ShotFrom( "kingray_buried", king.GlobalPosition + side * 4f + Vector3.Up * 2.2f - ahead * 3f, king.GlobalPosition ); }
			}
			if ( king.ActName == "Erupt" && king.Underground && king.CueShowing )
			{
				sawCue = true;
				if ( shots is not null && !shotCue ) { shotCue = true; await ShotFrom( "kingray_erupt_cue", open + side * 6f - ahead * 5f + Vector3.Up * 4f, open ); }
			}
			if ( Stingray.Eruptions > eruptions ) launched = MathF.Max( launched, player.GlobalPosition.Y - Sculpt.GroundAt( player.GlobalPosition ) );
			if ( shots is not null && !shotBurst && king.ActName == "Erupt" && !king.Underground && launched > 1f ) { shotBurst = true; await ShotFrom( "kingray_erupt", open + side * 8f - ahead * 6f + Vector3.Up * 3f, open + Vector3.Up * 2f ); }
			await Wait( 0.02f );
		}
		await Wait( 1.5f );
		GD.Print( $"[autotest] kingray: ambush: buried {sawBuried} ({deepest:0.00} m under), wings hidden {hidden}, crown showing {crownUp}, cue {sawCue}, bursts {Stingray.Eruptions - eruptions}, hits {Stingray.EruptHits}, player thrown {launched:0.0} m up" );
		Check( sawBuried && hidden && deepest > 0.1f, "King Raymond did not go under the sand" );
		Check( crownUp, "King Raymond's crown did not show over the sand while buried" );
		Check( sawCue, "no glowing spot before King Raymond burst up" );
		Check( Stingray.Eruptions > eruptions && Stingray.EruptHits > 0 && launched > 1.5f, $"King Raymond's ambush did not launch the player ({launched:0.0} m)" );
		Check( king.DiscShowing && king.GlobalPosition.Y - king.GroundUnder > 0.2f, "King Raymond stayed in the sand after the ambush" );

		// 5. The shock rings: warning rings, then rings that hit a player on the ground...
		await Wait( 1f );
		Place( open + ahead * 6f, -ahead );
		king.Calm();
		var sent = Stingray.RingsSent;
		Stingray.RingHits = 0;
		Stingray.RingJumps = 0;
		Electrocuted.Stuns = 0;
		king.ForceAttack( "Shock rings" );
		var warned = false;
		bool shotWarn = false, shotRings = false;
		for ( var t = 0f; t < 6f && king.ActName != "Prowl"; t += 0.02f )
		{
			Hold();
			if ( king.ActName == "ShockRise" && king.CueShowing ) warned = true;
			if ( shots is not null && !shotWarn && king.ActName == "ShockRise" && king.CueShowing && t > 0.7f ) { shotWarn = true; await ShotFrom( "kingray_ring_warning", open - ahead * 6f + side * 6f + Vector3.Up * 6f, king.GlobalPosition ); }
			if ( shots is not null && !shotRings && ShockRing.All.Count >= 2 ) { shotRings = true; await ShotFrom( "kingray_rings", open - ahead * 5f + side * 7f + Vector3.Up * 2.5f, king.GlobalPosition ); }
			await Wait( 0.02f );
		}
		await Wait( 2f );
		var groundHits = Stingray.RingHits;
		GD.Print( $"[autotest] kingray: rings sent {Stingray.RingsSent - sent}, warned {warned}, hits standing {groundHits}, stuns {Electrocuted.Stuns}" );
		Check( warned, "no warning rings before the shock rings" );
		Check( Stingray.RingsSent - sent == Stingray.RingCount, $"King Raymond sent {Stingray.RingsSent - sent} rings, not {Stingray.RingCount}" );
		Check( groundHits > 0 && Electrocuted.Stuns > 0, "the shock rings never hit (and stunned) a player on the ground" );
		// ...and that a player in the air (a jump) clears.
		await Wait( 1f );
		Place( open + ahead * 6f, -ahead );
		king.Calm();
		Stingray.RingHits = 0;
		Stingray.RingJumps = 0;
		king.ForceAttack( "Shock rings" );
		for ( var t = 0f; t < 6f && king.ActName != "Prowl"; t += 0.02f )
		{
			player.GlobalPosition = new Vector3( open.X, Sculpt.GroundAt( open ) + 1f, open.Z );
			player.Velocity = Vector3.Zero;
			await Wait( 0.02f );
		}
		await Wait( 2f );
		GD.Print( $"[autotest] kingray: in the air: rings jumped {Stingray.RingJumps}, hits {Stingray.RingHits}, rings left {ShockRing.All.Count}" );
		Check( Stingray.RingJumps >= Stingray.RingCount && Stingray.RingHits == 0, "a player in the air did not clear the shock rings" );
		Check( ShockRing.All.Count == 0, "shock rings were left running" );
		Check( !player.Scripted, "the player is still held after the rings" );

		// The ground: nothing of him went into it (except while buried).
		GD.Print( $"[autotest] kingray: lowest clearance over the ground {Stingray.LowestClearance:0.00} m ({Stingray.LowestWhen})" );
		Check( Stingray.LowestClearance > 0f, $"part of King Raymond went into the ground ({Stingray.LowestClearance:0.00} m)" );

		// He dies: crown and all come apart, and nothing of his attacks stays.
		var pieces = Stingray.DeathPieces;
		if ( shots is not null ) view = new Transform3D( Shapes.LookAt( king.GlobalPosition - (king.GlobalPosition + side * 8f + Vector3.Up * 3f) ), king.GlobalPosition + side * 8f + Vector3.Up * 3f );
		king.ForceAttack( "Shock rings" );
		await Wait( 1.5f );
		king.Health.OnDamage( new DamageInfo { Damage = 1_000_000f, Attacker = player } );
		if ( shots is not null )
		{
			player.CameraOverride = _ => view;
			player.ThirdPerson = true;
			await Wait( 0.4f );
			await Shot( "kingray_death" );
			player.CameraOverride = null;
			player.ThirdPerson = false;
		}
		await Wait( 0.8f );
		GD.Print( $"[autotest] kingray: came apart into {Stingray.DeathPieces - pieces} pieces, kings left {KingRaymond.All.Count}, rings {ShockRing.All.Count}, bombs {RayBomb.All.Count}" );
		Check( Stingray.DeathPieces - pieces >= 3, "King Raymond did not come apart (crown and all) when he died" );
		Check( KingRaymond.All.Count == 0 && ShockRing.All.Count == 0 && RayBomb.All.Count == 0, "King Raymond or his attacks were left after he died" );
		Check( !player.Scripted, "the player is still held" );

		GD.Print( "[autotest] done" );
		GetTree().Quit();

		async Task ShotFrom( string name, Vector3 eye, Vector3 at )
		{
			view = new Transform3D( Shapes.LookAt( at - eye ), eye );
			player.CameraOverride = _ => view;
			var third = player.ThirdPerson;
			player.ThirdPerson = true;
			await Wait( 0.05f );
			await Shot( name );
			player.ThirdPerson = third;
			player.CameraOverride = null;
		}
	}
}
