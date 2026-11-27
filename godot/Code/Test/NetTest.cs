using Godot;

/// <summary>
/// --nettest: online co-op between two copies of the game on one machine. Run one with `--host` and one with
/// `--join 127.0.0.1 --class vacbot`. Stage 1: each sees the other's robot (the right one), the joiner's walk shows
/// on the host, and the joiner's Bubble Cannon tap fires on the host's copy of its robot too.
/// </summary>
public partial class AutoTest
{
	async Task NetTest()
	{
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		var host = Net.IsHost;
		GD.Print( $"[autotest] net: {(host ? "hosting" : "joining")}" );
		PlayerController other = null;
		for ( var t = 0f; t < 40f && other is null; t += 0.25f )
		{
			await Wait( 0.25f );
			foreach ( var id in Net.Peers ) other = Net.RemoteOf( id ) ?? other;
		}
		Check( other is not null, "the other player's robot never showed up" );
		if ( other is null ) { Finish(); return; }
		GD.Print( $"[autotest] net: the other robot is {other.Robot.Name} ({other.Name})" );

		if ( host )
		{
			Check( other.Robot == RobotClass.VacBot, $"the joiner picked Vac-Bot but shows as {other.Robot.Name}" );
			await Wait( 1f );   // its first state has placed it
			var start = other.GlobalPosition;
			var firedBefore = BubbleShot.Fired;
			var moved = 0f;
			for ( var t = 0f; t < 20f && (moved < 3f || BubbleShot.Fired == firedBefore); t += 0.25f )
			{
				await Wait( 0.25f );
				moved = MathF.Max( moved, other.GlobalPosition.DistanceTo( start ) );
			}
			GD.Print( $"[autotest] net: the joiner's robot moved {moved:0.0} m here; bubbles it blew here: {BubbleShot.Fired - firedBefore}" );
			Check( moved >= 3f, "the joiner walked but its robot didn't move on the host" );
			Check( BubbleShot.Fired > firedBefore, "the joiner's Bubble Cannon tap didn't fire on the host" );
		}
		else
		{
			Check( other.Robot == RobotClass.RamBot, $"the host is Ram-Bot but shows as {other.Robot.Name}" );
			// Long enough that the host has noted where this robot starts (it waits 1 s after seeing it) before it moves.
			await Wait( 3f );
			var (spot, _) = OpenLane( 8f );
			player.GlobalPosition = spot + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			await Wait( 1.5f );
			var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
			inventory.Select( inventory.Weapons.First( w => w.Bubbles ), force: true );
			await Wait( 1f );
			var mine = BubbleShot.Fired;
			Input.ActionPress( "attack1" );
			await Wait( 0.06f );
			Input.ActionRelease( "attack1" );
			await Wait( 1f );
			Check( BubbleShot.Fired > mine, "the tap didn't blow a bubble here" );
			await Wait( 1f );
		}
		await NetEnemies( host, other );
		await NetStage3( host, other );
		await NetPlaytest1( host, other );
		await NetAttacks( host, other );
		Finish();
	}

	/// <summary>
	/// Stage 2: the host's crabs show on the joiner as puppets in the same spots; the joiner's rifle, fired at one,
	/// hurts it on the host (through its robot there), and the joiner's copy loses the same health.
	/// </summary>
	async Task NetEnemies( bool host, PlayerController other )
	{
		DevFlags.FreezeEnemies = true;
		if ( host )
		{
			// Crabs in front of the host's robot.
			var (spot, yaw) = OpenLane( 10f );
			player.GlobalPosition = spot + Vector3.Up * 0.1f;
			var ahead = new Vector3( -MathF.Sin( MathX.Rad( yaw ) ), 0f, -MathF.Cos( MathX.Rad( yaw ) ) );
			var crabs = new List<Creature>();
			for ( var i = 0; i < 3; i++ )
				if ( WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Crab, Nav.Closest( player, spot + ahead * (6f + i * 2f) ) + Vector3.Up * 0.3f, spot ) is Creature c ) crabs.Add( c );
			GD.Print( $"[autotest] net: host spawned {crabs.Count} crabs, numbers {string.Join( ", ", crabs.Select( c => c.NetId ) )}" );
			for ( var t = 0f; t < 20f && other.Robot != RobotClass.RamBot; t += 0.25f ) await Wait( 0.25f );
			Check( other.Robot == RobotClass.RamBot, $"the joiner switched to Ram-Bot but shows as {other.Robot.Name}" );
			var before = NetCreatures.RemoteShots;
			for ( var t = 0f; t < 20f && NetCreatures.RemoteShots == before; t += 0.25f ) await Wait( 0.25f );
			GD.Print( $"[autotest] net: the joiner's robot landed {NetCreatures.RemoteShots - before} hits on the host's crabs" );
			Check( NetCreatures.RemoteShots > before, "the joiner's rifle never hit the host's crabs" );
			await Wait( 4f );   // the joiner checks its copies

			// An enemy at the joiner's robot: its bites land on the joiner's machine.
			DevFlags.FreezeEnemies = false;
			foreach ( var c in crabs ) if ( IsInstanceValid( c ) && !c.IsDead ) c.QueueFree();
			// This robot steps well away first: both robots stood on one spot and the crab went for the nearer, this one.
			player.GlobalPosition = Nav.Closest( player, other.GlobalPosition + Vector3.Back * 14f ) + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			var biter = WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Crab, Nav.Closest( player, other.GlobalPosition + Vector3.Right * 1.2f ) + Vector3.Up * 0.3f, other.GlobalPosition ) as Creature;
			GD.Print( $"[autotest] net: a crab ({biter?.NetId}) put by the joiner's robot to bite it" );
			await Wait( 10f );
			// The run: the joiner sees the host's wave.
			WaveDirector.Current.JumpToWave( 3 );
			await Wait( 8f );
			GD.Print( $"[autotest] net: host is on wave {WaveDirector.Current.Wave}" );
		}
		else
		{
			Creature target = null;
			for ( var t = 0f; t < 15f && target is null; t += 0.25f )
			{
				await Wait( 0.25f );
				target = Creature.Everyone.FirstOrDefault( c => c.Puppet && c is JunkCrab && !c.IsDead );
			}
			Check( target is not null, "no puppet crab showed up from the host" );
			if ( target is null ) return;
			var puppets = Creature.Everyone.Count( c => c.Puppet );
			GD.Print( $"[autotest] net: {puppets} puppets here; crab {target.NetId} at {target.GlobalPosition}" );
			// Stand by the host's robot, look at the crab, hold the trigger.
			player.GlobalPosition = other.GlobalPosition + Vector3.Up * 0.1f;
			await Wait( 1f );
			var before = target.Health.Current;
			RobotClass.Pick( RobotClass.RamBot, player );   // a rifle (and the host swaps this robot's kit too)
			await Wait( 1f );
			var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
			inventory.Select( inventory.Weapons.First( w => w.ModelName == "rifle" ), force: true );
			await Wait( 0.8f );
			for ( var t = 0f; t < 2f; t += 0.05f )
			{
				if ( !IsInstanceValid( target ) || target.IsDead ) { GD.Print( $"[autotest] net: the crab is gone ({(IsInstanceValid( target ) ? "dead" : "freed")}) at {t:0.00} s" ); break; }
				var to = (target.GlobalPosition + Vector3.Up * 0.35f - player.Camera.GlobalPosition).Normalized();
				player.EyeAngles = new Angles( MathX.Deg( -MathF.Asin( to.Y ) ), MathX.Deg( MathF.Atan2( -to.X, -to.Z ) ), 0f );
				if ( t < 0.06f ) Input.ActionPress( "attack1" );
				await Wait( 0.05f );
			}
			Input.ActionRelease( "attack1" );
			await Wait( 1.5f );
			var after = IsInstanceValid( target ) ? target.Health.Current : 0f;
			GD.Print( $"[autotest] net: the puppet crab went {before:0} -> {after:0}" );
			Check( after < before, "the host's hits never reached the joiner's copy of the crab" );

			// Bitten by the host's crab.
			var me = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
			me.Health.Invulnerable = false;
			var hp = me.Health.Current;
			for ( var t = 0f; t < 14f && me.Health.Current >= hp; t += 0.25f ) await Wait( 0.25f );
			GD.Print( $"[autotest] net: the joiner's health {hp:0} -> {me.Health.Current:0}" );
			Check( me.Health.Current < hp, "the host's crab never hurt the joiner" );
			for ( var t = 0f; t < 14f && WaveDirector.Current.Wave < 3; t += 0.25f ) await Wait( 0.25f );
			GD.Print( $"[autotest] net: joiner shows wave {WaveDirector.Current.Wave} ({WaveDirector.Current.State})" );
			Check( WaveDirector.Current.Wave == 3, "the joiner never saw the host's wave 3" );
			await Wait( 2f );
		}
	}

	/// <summary>
	/// Stage 3: the joiner's skill points reach its robot on the host; a host creature's grab carries the joiner and
	/// its shove pushes it; the joiner's Bubble Cannon traps a host crab and the joiner sees that bubble.
	/// </summary>
	async Task NetStage3( bool host, PlayerController other )
	{
		DevFlags.FreezeEnemies = true;
		if ( host )
		{
			for ( var t = 0f; t < 10f && Skills.Rank( other, SkillId.HighLift ) == 0; t += 0.25f ) await Wait( 0.25f );
			GD.Print( $"[autotest] net: the joiner's High Lift here: {PlayerSkills.Of( other ).Held( SkillId.HighLift )}" );
			Check( PlayerSkills.Of( other ).Held( SkillId.HighLift ) == 2, "the joiner's skill points never reached its robot here" );
			// A grab: held here and carried 5 m, then let go; then a shove.
			await Wait( 1f );
			var from = other.GlobalPosition;
			other.Scripted = true;
			for ( var t = 0f; t < 1.2f; t += 0.05f ) { other.GlobalPosition = from + Vector3.Up * 0.2f + Vector3.Right * (5f * t / 1.2f); await Wait( 0.05f ); }
			other.Scripted = false;
			await Wait( 1.5f );
			other.Velocity += Vector3.Up * 6f;
			await Wait( 1.5f );
			// A crab for the joiner's bubble.
			var (spot, yaw) = OpenLane( 10f );
			var crab = WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Crab, Nav.Closest( player, other.GlobalPosition + new Vector3( 0f, 0f, -5f ) ) + Vector3.Up * 0.3f, other.GlobalPosition ) as Creature;
			var trapped = BubbleTrap.Trapped;
			for ( var t = 0f; t < 20f && BubbleTrap.Trapped == trapped; t += 0.25f ) await Wait( 0.25f );
			GD.Print( $"[autotest] net: bubbles the joiner caught crabs in here: {BubbleTrap.Trapped - trapped}" );
			Check( BubbleTrap.Trapped > trapped, "the joiner's bubble never caught the host's crab" );
			await Wait( 6f );
		}
		else
		{
			PlayerSkills.Of( player ).Set( SkillId.HighLift, 2 );
			RobotClass.Pick( RobotClass.VacBot, player );
			await Wait( 1f );
			var start = player.GlobalPosition;
			var maxMove = 0f;
			var wasHeld = false;
			var maxRise = 0f;
			for ( var t = 0f; t < 8f; t += 0.05f )
			{
				await Wait( 0.05f );
				wasHeld |= player.Scripted;
				maxMove = MathF.Max( maxMove, (player.GlobalPosition - start).Flat().Length() );
				maxRise = MathF.Max( maxRise, player.Velocity.Y );
			}
			GD.Print( $"[autotest] net: held by the host {wasHeld}, carried {maxMove:0.0} m, pushed up to {maxRise:0.0} m/s; held now {player.Scripted}" );
			Check( wasHeld && maxMove > 3f, "the host's grab never carried the joiner" );
			Check( !player.Scripted, "the joiner was never let go" );
			Check( maxRise > 3f, "the host's shove never reached the joiner" );
			// Bubble the host's crab.
			Creature crab = null;
			for ( var t = 0f; t < 10f && crab is null; t += 0.25f ) { await Wait( 0.25f ); crab = Creature.Everyone.FirstOrDefault( c => c.Puppet && c is JunkCrab && !c.IsDead ); }
			Check( crab is not null, "no crab for the bubble" );
			if ( crab is null ) return;
			var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
			inventory.Select( inventory.Weapons.First( w => w.Bubbles ), force: true );
			await Wait( 0.8f );
			for ( var t = 0f; t < 4f && BubbleTrap.Of( crab ) is null && IsInstanceValid( crab ); t += 0.5f )
			{
				var to = (crab.GlobalPosition + Vector3.Up * 0.35f - player.Camera.GlobalPosition).Normalized();
				player.EyeAngles = new Angles( MathX.Deg( -MathF.Asin( to.Y ) ), MathX.Deg( MathF.Atan2( -to.X, -to.Z ) ), 0f );
				await Wait( 0.1f );
				Input.ActionPress( "attack1" );
				await Wait( 0.06f );
				Input.ActionRelease( "attack1" );
				await Wait( 0.35f );
			}
			for ( var t = 0f; t < 3f && IsInstanceValid( crab ) && BubbleTrap.Of( crab ) is null; t += 0.1f ) await Wait( 0.1f );
			GD.Print( $"[autotest] net: the host's bubble shows round the crab here: {IsInstanceValid( crab ) && BubbleTrap.Of( crab ) is not null}" );
			Check( IsInstanceValid( crab ) && BubbleTrap.Of( crab ) is not null, "the bubble the host made never showed on the joiner" );
			await Wait( 2f );
		}
	}

	/// <summary>
	/// The first online playtest's asks: a ping reaches the other player; the scoreboard knows the other's points;
	/// Davey's locker takes both players down and brings both back up; a dead joiner watches the host.
	/// </summary>
	async Task NetPlaytest1( bool host, PlayerController other )
	{
		await Wait( 1f );
		var me = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		if ( host )
		{
			for ( var t = 0f; t < 10f && Pings.Seen == 0; t += 0.25f ) await Wait( 0.25f );
			Check( Pings.Seen > 0, "the joiner's ping never showed here" );
			var points = other.GetNode<NetPlayer>( "NetPlayer" ).Points;
			GD.Print( $"[autotest] net: the joiner ({Net.PlayerName( other.PeerId )}) shows {points} points here" );
			Check( points > 0, "the scoreboard never got the joiner's points" );
			for ( var t = 0f; t < 6f && PlayerController.RemoteEmotes == 0; t += 0.1f ) await Wait( 0.1f );
			Check( PlayerController.RemoteEmotes > 0, "the joiner's wave never showed here" );
			// Davey's locker: everyone down, then everyone up.
			Net.Instance.SendLocker( true );
			DaveyLocker.Enter( player, NetWorld.Ground( player ), 0 );
			await Wait( 5f );
			Net.Instance.SendLocker( false );
			DaveyLocker.Leave();
			await Wait( 5f );
			// The joiner is about to die and watch this robot.
			await Wait( 8f );
		}
		else
		{
			Pings.Place( player );
			await Wait( 1f );
			player.PlayEmote( Emote.Wave );
			await Wait( 1.5f );
			var top = player.GlobalPosition;
			for ( var t = 0f; t < 30f && !DaveyLocker.Active; t += 0.25f ) await Wait( 0.25f );
			await Wait( 1f );
			var down = top.Y - player.GlobalPosition.Y;
			GD.Print( $"[autotest] net: in Davey's locker here {DaveyLocker.Active}, {down:0} m down" );
			Check( DaveyLocker.Active && down > 100f, "Davey's locker never took the joiner down" );
			for ( var t = 0f; t < 10f && DaveyLocker.Active; t += 0.25f ) await Wait( 0.25f );
			await Wait( 1f );
			GD.Print( $"[autotest] net: back up, {MathF.Abs( player.GlobalPosition.Y - top.Y ):0.0} m from where it went down" );
			Check( !DaveyLocker.Active && MathF.Abs( player.GlobalPosition.Y - top.Y ) < 6f, "the joiner never came back up from the locker" );
			// Dead: after a moment the camera watches the host.
			me.Health.Invulnerable = false;
			me.Health.Kill();
			for ( var t = 0f; t < 6f && Spectate.Watching is null; t += 0.25f ) await Wait( 0.25f );
			GD.Print( $"[autotest] net: dead, watching {Spectate.Watching?.Name}" );
			Check( Spectate.Watching == other, "a dead joiner never watched the host" );
			me.Respawn( player.GlobalPosition, fullReset: false );
			await Wait( 1f );
		}
	}

	/// <summary>
	/// The host's enemies' attacks show on the joiner: the angler's hook flies out on its line, MC-51's beam fires.
	/// </summary>
	async Task NetAttacks( bool host, PlayerController other )
	{
		if ( host )
		{
			DevFlags.FreezeEnemies = false;
			var scene = GetTree().CurrentScene;
			var at = other.GlobalPosition;
			player.GlobalPosition = at + new Vector3( 30f, 0f, 30f );
			var angler = Angler.Spawn( scene, at + new Vector3( 5f, 1.5f, 0f ), 370f );
			await Wait( 1f );
			angler.ForceHook();
			await Wait( 5f );
			if ( IsInstanceValid( angler ) ) angler.QueueFree();
			var boss = MC61.Spawn( scene, player.GlobalPosition + new Vector3( 0f, 3f, -12f ), 20000f );
			await Wait( 2f );
			boss.ForceState( MC61.Mode.Charge );
			await Wait( 6f );
			if ( IsInstanceValid( boss ) ) boss.QueueFree();
			// The bosses: the Kraken's slam, Poseidon's trident, Davey's cannons.
			var kraken = Kraken.Spawn( scene, at + new Vector3( -14f, 0f, 0f ), 20000f );
			// It may pick an attack of its own first (the ink): keep asking until the slam has really begun.
			for ( var t = 0f; t < 20f && kraken.Attacking is not (Kraken.Attack.Suck or Kraken.Attack.Raise or Kraken.Attack.Slam); t += 0.25f )
			{
				kraken.ForceSlam();
				await Wait( 0.25f );
			}
			GD.Print( $"[autotest] net: host Kraken {kraken.State}, attacking {kraken.Attacking}" );
			await Wait( 4f );
			if ( IsInstanceValid( kraken ) ) kraken.QueueFree();
			var god = Poseidon.Spawn( scene, at + new Vector3( 0f, 2f, 10f ), 18000f );
			await Wait( 2.5f );
			god.ForceAttack( "trident" );
			await Wait( 3f );
			if ( IsInstanceValid( god ) ) god.QueueFree();
			var davey = DaveyJones.Spawn( scene, at + new Vector3( 0f, 1.5f, -8f ), 11000f );
			davey.OnlyWhenTold = true;
			await Wait( 2.5f );
			davey.ForceAttack( "cannons" );
			await Wait( 2f );
			GD.Print( $"[autotest] net: host Davey cannons {DaveyCannon.All.Count}" );
			await Wait( 2f );
			if ( IsInstanceValid( davey ) ) davey.QueueFree();
			// King Raymond IV's shock rings.
			var king = KingRaymond.Spawn( scene, at + new Vector3( 10f, 1f, 0f ), 8000f );
			await Wait( 2f );
			king.ForceAttack( "Shock rings" );
			await Wait( 4f );
			GD.Print( $"[autotest] net: host King Raymond rings sent {Stingray.RingsSent}" );
			if ( IsInstanceValid( king ) ) king.QueueFree();
			// The Mermaid Queen's pearls and riptide.
			var queen = MermaidQueen.Spawn( scene, at + new Vector3( -10f, 6f, 6f ), 28000f );
			queen.OnlyWhenTold = true;
			await Wait( 2.5f );
			queen.Force( "pearls" );
			await Wait( 4f );
			queen.Force( "riptide" );
			await Wait( 3f );
			GD.Print( $"[autotest] net: host queen pearls {MermaidQueen.PearlsThrown}, riptides {MermaidQueen.Riptides}" );
			if ( IsInstanceValid( queen ) ) queen.QueueFree();
			DevFlags.FreezeEnemies = true;
			await Wait( 1f );
		}
		else
		{
			player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
			bool hook = false, beam = false, slam = false, cannons = false, rings = false;
			var tridents = Poseidon.PuppetTridents;
			int pearls = MermaidQueen.PuppetPearls, waves = MermaidQueen.PuppetWaves;
			bool QueenShown() => MermaidQueen.PuppetPearls > pearls && MermaidQueen.PuppetWaves > waves;
			for ( var t = 0f; t < 100f && !(hook && beam && slam && cannons && rings && Poseidon.PuppetTridents > tridents && QueenShown()); t += 0.05f )
			{
				await Wait( 0.05f );
				hook |= Angler.All.Any( a => a.Puppet && a.HookShown );
				beam |= MC61.All.Any( m => m.Puppet && m.BeamShown );
				slam |= Kraken.All.Any( k => k.Puppet && k.Attacking is Kraken.Attack.Raise or Kraken.Attack.Slam );
				cannons |= DaveyCannon.All.Count > 0;
				rings |= ShockRing.All.Count > 0 && KingRaymond.All.Any( k => k.Puppet );
			}
			GD.Print( $"[autotest] net: shown here: angler hook {hook}, MC-51 beam {beam}, Kraken slam {slam}, Poseidon tridents {Poseidon.PuppetTridents - tridents}, Davey cannons {cannons}, King Raymond rings {rings}" );
			Check( hook, "the host's angler hook never showed on the joiner" );
			Check( beam, "the host's MC-51 beam never showed on the joiner" );
			Check( slam, "the host's Kraken slam never showed on the joiner" );
			Check( Poseidon.PuppetTridents > tridents, "the host's Poseidon trident never showed on the joiner" );
			Check( cannons, "the host's Davey cannons never showed on the joiner" );
			Check( rings, "the host's King Raymond shock rings never showed on the joiner" );
			GD.Print( $"[autotest] net: queen pearls {MermaidQueen.PuppetPearls - pearls}, riptides {MermaidQueen.PuppetWaves - waves}" );
			Check( QueenShown(), "the host's Mermaid Queen pearls and riptide never showed on the joiner" );
		}
	}

	void Finish()
	{
		GD.Print( failures == 0 ? "[autotest] net: PASS" : $"[autotest] net: {failures} failed" );
		GD.Print( "[autotest] done" );
		Net.Instance?.Leave();
		GetTree().Quit();
	}
}
