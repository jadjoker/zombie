using Godot;

/// <summary>
/// Scripted checks for the ported movement, run with: godot -- --autotest [--shots dir]
/// Presses the actions like a player would and prints speeds and heights in s&box units, so they can be compared
/// with the s&box tunings. Takes screenshots when asked, then quits.
/// </summary>
public partial class AutoTest : Node
{
	PlayerController player;
	PlayerMovement movement;
	string shots;

	public static bool Requested => OS.GetCmdlineUserArgs().Contains( "--autotest" );

	/// <summary> A check that must hold: prints FAIL (which the release script looks for) and counts it. </summary>
	void Check( bool ok, string what )
	{
		if ( ok ) return;
		failures++;
		GD.Print( $"[autotest] FAIL: {what}" );
	}
	int failures;

	public override async void _Ready()
	{
		var args = OS.GetCmdlineUserArgs();
		var i = Array.IndexOf( args, "--shots" );
		if ( i >= 0 && i + 1 < args.Length ) shots = args[i + 1];
		if ( args.Contains( "--pops" ) ) AddChild( new PopProbe() );
		if ( args.Contains( "--nostyle" ) )
			foreach ( var n in GetTree().Root.FindChildren( "*", "", true, false ) )
				if ( n is GlobalStylize gs ) gs.Style = GlobalStylize.Look.Off;

		await Wait( 0.5f );
		player = GetTree().Root.FindChild( "Player", true, false ) as PlayerController;
		movement = player.GetNode<PlayerMovement>( "PlayerMovement" );
		Input.MouseMode = Input.MouseModeEnum.Visible;
		if ( args.Contains( "--jitter" ) ) { await JitterTest(); return; }
		if ( args.Contains( "--flicker" ) ) { await FlickerTest(); return; }
		if ( args.Contains( "--playtest1" ) ) { await Playtest1Test(); return; }
		if ( args.Contains( "--seabed" ) ) { SeabedMap(); return; }
		if ( args.Contains( "--dive-edge" ) ) { await DiveEdgeTest(); return; }
		if ( args.Contains( "--hitframes" ) ) { await HitFramesTest(); return; }
		if ( args.Contains( "--swimframes" ) ) { await SwimFramesTest(); return; }
		if ( args.Contains( "--devclick" ) ) { await DevClickTest(); return; }
		if ( args.Contains( "--flash" ) ) { await FlashTest(); return; }
		if ( args.Contains( "--waveflow" ) ) { await WaveFlowTest(); return; }
		if ( args.Contains( "--pause" ) ) { await PauseTest(); return; }
		if ( args.Contains( "--sea" ) ) { await SeaTest(); return; }
		if ( args.Contains( "--rifles" ) ) { await RiflesTest(); return; }
		if ( args.Contains( "--kraken" ) ) { await KrakenTest(); return; }
		if ( args.Contains( "--whirlpool" ) ) { await WhirlpoolTest(); return; }
		if ( Level.Current is not null ) { await LevelTest(); return; }
		if ( args.Contains( "--creatures" ) ) { await CreatureTest(); return; }

		Report( "settled" );
		await Shot( "spawn" );

		// Run (default) and sprint across the open half of the range (+Z, yaw 180).
		player.GlobalPosition = new Vector3( 0f, 0.05f, 1600f * U.In );
		player.EyeAngles = new Angles( 0f, 180f, 0f );
		await Wait( 0.3f );
		Input.ActionPress( "forward" );
		await Wait( 1.0f );
		Report( "run 1s (expect 320)" );
		Input.ActionPress( "run" );
		await Wait( 1.0f );
		Report( "sprint 1s (expect 470)" );
		Input.ActionRelease( "run" );
		Input.ActionRelease( "forward" );
		await Wait( 0.6f );
		Report( "stopped 0.6s (expect 0)" );

		// Jump height: s&box JumpSpeed 300, gravity 850 -> 52.9 units.
		var startY = player.GlobalPosition.Y;
		var peak = startY;
		Input.ActionPress( "jump" );
		await Wait( 0.05f );
		Input.ActionRelease( "jump" );
		for ( var t = 0f; t < 0.9f; t += 0.02f )
		{
			await Wait( 0.02f );
			peak = MathF.Max( peak, player.GlobalPosition.Y );
		}
		GD.Print( $"[autotest] jump height {(peak - startY) / U.In:0.0} u (expect ~52.9)" );

		// Slide: sprint, then crouch.
		Input.ActionPress( "forward" );
		Input.ActionPress( "run" );
		await Wait( 0.8f );
		Input.ActionPress( "duck" );
		await Wait( 0.08f );
		Report( "slide start (expect ~620 = 470 + 150 boost)" );
		await Shot( "slide" );
		await Wait( 0.6f );
		Report( "slide 0.6s" );
		Input.ActionRelease( "duck" );
		Input.ActionRelease( "run" );
		Input.ActionRelease( "forward" );
		await Wait( 1.0f );

		// Air jump: jump, then jump again near the top.
		Input.ActionPress( "jump" ); await Wait( 0.05f ); Input.ActionRelease( "jump" );
		await Wait( 0.3f );
		var before = movement.Stamina;
		Input.ActionPress( "jump" ); await Wait( 0.05f ); Input.ActionRelease( "jump" );
		await Wait( 0.05f );
		GD.Print( $"[autotest] air jump: vertical {player.Velocity.Y / U.In:0} u/s (expect ~340), stamina {before:0.0} -> {movement.Stamina:0.0}" );
		await Wait( 1.2f );

		// Weapons: face the targets from 6 m back.
		Input.MouseMode = Input.MouseModeEnum.Captured;
		player.ThirdPerson = false;
		player.GlobalPosition = new Vector3( 0f, 0.05f, 900f * U.In );
		player.Velocity = Vector3.Zero;
		player.EyeAngles = new Angles( 4f, 180f, 0f );
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		await Wait( 1.0f );
		await Shot( "rifle_hip" );
		var rifle = inventory.Current;
		Input.ActionPress( "attack1" );
		await Wait( 0.5f );
		Input.ActionRelease( "attack1" );
		await Shot( "rifle_after_burst" );
		GD.Print( $"[autotest] rifle burst: ammo {rifle.Ammo}/{rifle.MagazineSize} reserve {rifle.Reserve} (8/s for 0.5 s -> about 26 left)" );
		Input.ActionPress( "attack2" );
		await Wait( 0.12f );
		await Shot( "rifle_aim_mid" );
		await Wait( 0.4f );
		await Shot( "rifle_scoped" );
		GD.Print( $"[autotest] scope: aim {rifle.AimAmount:0.00} scope {rifle.ScopeAmount:0.00} fov {player.Camera.Fov:0.0} (expect 90*0.7*0.43 = 27)" );
		Input.ActionRelease( "attack2" );
		await Wait( 0.4f );
		Input.ActionPress( "reload" ); await Wait( 0.05f ); Input.ActionRelease( "reload" );
		await Wait( 0.8f );
		await Shot( "rifle_reload" );
		await Wait( 1.0f );
		GD.Print( $"[autotest] after reload: ammo {rifle.Ammo} reserve {rifle.Reserve}" );

		inventory.Select( inventory.Weapons[1] );
		await Wait( 1.0f );
		await Shot( "shotgun_hip" );
		var shotgun = inventory.Current;
		await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
		Input.ActionPress( "attack1" );
		await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
		await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
		Input.ActionRelease( "attack1" );
		GD.Print( $"[autotest] shotgun fired: ammo {shotgun.Ammo} (expect 5), active {shotgun.Active}" );
		await Wait( 0.1f );
		await Shot( "shotgun_fire" );
		await Wait( 0.6f );
		Input.ActionPress( "reload" ); await Wait( 0.05f ); Input.ActionRelease( "reload" );
		await Wait( 1.0f );
		await Shot( "shotgun_reload" );
		await Wait( 1.0f );
		GD.Print( $"[autotest] shotgun: ammo {shotgun.Ammo}/{shotgun.MagazineSize} reserve {shotgun.Reserve}" );
		Input.ActionPress( "attack2" );
		await Wait( 0.5f );
		await Shot( "shotgun_aim" );
		Input.ActionRelease( "attack2" );

		player.GetNode<PlayerMelee>( "PlayerMelee" ).TestSwing();
		await Wait( 0.2f );
		await Shot( "axe_swing" );
		await Wait( 0.8f );

		// Bloobers: three in front, then shoot the nearest one apart.
		var range = GetTree().CurrentScene;
		inventory.Select( inventory.Weapons[0] );
		player.EyeAngles = new Angles( -4f, 180f, 0f );
		for ( var b = 0; b < 3; b++ )
			Bloober.Spawn( range, player.GlobalPosition + new Vector3( (b - 1) * 120f, 70f, 500f + b * 80f ) * U.In, 160f, Vector3.Back * -1f );
		await Wait( 0.6f );
		await Shot( "bloobers" );
		var first = Bloober.All.OrderBy( x => x.GlobalPosition.DistanceTo( player.GlobalPosition ) ).First();
		// Close look with everything frozen: is the sculpted head facing the same way as the eyes?
		DevFlags.FreezeEnemies = true;
		var faceDir = -first.GlobalBasis.Z;
		player.GlobalPosition = first.GlobalPosition + faceDir * 90f * U.In - Vector3.Up * 60f * U.In;
		player.Velocity = Vector3.Zero;
		LookAt( first.GlobalPosition );
		await Wait( 0.3f );
		LookAt( first.GlobalPosition );
		await Shot( "bloober_face" );
		player.GlobalPosition = first.GlobalPosition + first.GlobalBasis.X * 140f * U.In - Vector3.Up * 60f * U.In;
		LookAt( first.GlobalPosition + first.GlobalBasis.Z * 40f * U.In );
		await Wait( 0.3f );
		LookAt( first.GlobalPosition + first.GlobalBasis.Z * 40f * U.In );
		await Shot( "bloober_side" );
		var weapon = inventory.Current;
		// Shoot the body (segment 3 area) a few times to cut it, then the head.
		for ( var k = 0; k < 4; k++ ) { LookAt( first.GlobalPosition + first.GlobalBasis.Z * 50f * U.In ); weapon.TestFire(); await Wait( 0.13f ); }
		await Wait( 0.2f );
		await Shot( "bloober_cut" );
		for ( var k = 0; k < 10 && !first.IsDead; k++ ) { LookAt( first.GlobalPosition ); weapon.TestFire(); await Wait( 0.13f ); }
		DevFlags.FreezeEnemies = false;
		GD.Print( $"[autotest] bloober after 14 rifle hits: dead {first.IsDead}, health {first.Health?.Current:0}, bloobers alive {Bloober.All.Count( x => !x.IsDead )}, points {player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Points}" );
		await Shot( "bloober_shot" );
		await Wait( 1.2f );
		await Shot( "bloober_after" );
		await Wait( 3f );
		GD.Print( $"[autotest] after 3 s: player health {player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Current:0}" );
		await Shot( "bloober_bite" );

		// Waves: unpause the director and let wave 1 arrive.
		var director = WaveDirector.Current;
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		director.SpawningPaused = false;
		await Wait( 9f );
		GD.Print( $"[autotest] waves: state {director.State} wave {director.Wave} alive {director.AliveCount} remaining {director.ZombiesRemaining}" );
		player.EyeAngles = new Angles( 0f, 180f, 0f );
		await Shot( "wave1" );

		// Third person view.
		player.ThirdPerson = true;
		player.EyeAngles = new Angles( 15f, 200f, 0f );
		await Wait( 0.6f );
		await Shot( "third_person" );

		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> A converted level: look around from the spawn, then let some Bloobers in. </summary>
	/// <summary> Buy stations: walk up to each, look at it, buy with and without the points. Also shows the HUD overlays. </summary>
	async Task StationTest()
	{
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		var use = player.GetNode<PlayerUse>( "PlayerUse" );
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		var spawn = player.GlobalPosition;
		foreach ( var node in GetTree().GetNodesInGroup( "buy_station" ).OfType<Node3D>() )
		{
			var station = node.GetNode<BuyStation>( "BuyStation" );
			// Stand on the floor between the station and the spawn, a step away from it.
			var toward = (node.GlobalPosition - spawn).Flat().SafeNormal();
			if ( toward == Vector3.Zero ) toward = Vector3.Forward;
			var stand = node.GlobalPosition - toward * 70f * U.In;
			var floor = Trace.Ray( player, stand + Vector3.Up * 60f * U.In, stand + Vector3.Down * 400f * U.In, Layers.World );
			GD.Print( $"[autotest] station {station.Title} at {node.GlobalPosition / U.In}, floor hit {floor.Hit} {floor.HitPosition / U.In}" );
			player.GlobalPosition = (floor.Hit ? floor.HitPosition : stand) + Vector3.Up * 2f * U.In;
			player.Velocity = Vector3.Zero;
			for ( var k = 0; k < 3; k++ ) { LookAt( node.GlobalPosition ); await Wait( 0.1f ); }
			var hovered = use.Hovered == station;
			var poor = use.TryPress();
			survivor.AddPoints( 5000 );
			if ( station.Kind == BuyStationKind.Health ) survivor.Health.OnDamage( new DamageInfo { Damage = 30f } );
			var before = (survivor.Points, survivor.Health.MaxHealth);
			var bought = use.TryPress();
			GD.Print( $"[autotest] station {station.Title}: hovered {hovered}, poor buy {poor}, buy {bought}, points {before.Points} to {survivor.Points}, max health {before.MaxHealth} to {survivor.Health.MaxHealth}, next cost {station.CurrentCost}" );
			await Shot( $"station_{station.Title.ToLower()}" );
		}

		// Overlays: burn, slow, ink, venom, low health, stamina, jet bar and the boss bar with a line.
		var a = survivor.Afflictions;
		a.Cursed( PlayerAfflictions.Curse.Burn );
		a.Cursed( PlayerAfflictions.Curse.Slow );
		a.Cursed( PlayerAfflictions.Curse.Ink );
		a.Inked(); a.Inked();
		await Wait( 0.3f );
		await Shot( "hud_curses" );
		var boss = MC61.Spawn( GetTree().CurrentScene, player.GlobalPosition + new Vector3( 0f, 200f, 600f ) * U.In, 2000f );
		await Wait( 2.5f );
		boss.ForceState( MC61.Mode.Cards );
		await Wait( 0.2f );
		await Shot( "hud_boss" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> The step 7 creatures on the test range: spawn each, look at it, shoot it apart. </summary>
	async Task CreatureTest()
	{
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.Invulnerable = true;
		inventory.Select( inventory.Weapons[0] );
		var range = GetTree().CurrentScene;

		var userArgs = OS.GetCmdlineUserArgs();
		MC61.PoseHands = !userArgs.Contains( "--rest-hands" );
		if ( userArgs.Contains( "--boss" ) ) goto Boss;
		if ( userArgs.Contains( "--float" ) )
		{
			// Anchor snapped: no gravity, swim where you look.
			var fm = player.GetNode<MoveModeFloat>( "MoveModeFloat" );
			WaveDirector.Current.FloatRound = true;
			var start = player.GlobalPosition;
			await Wait( 1f );
			GD.Print( $"[autotest] float idle: moved {(player.GlobalPosition - start).Length() / U.In:0} u (should stay put)" );
			fm.TestInput = Vector3.Up;
			start = player.GlobalPosition;
			await Wait( 1.5f );
			GD.Print( $"[autotest] float up: rose {(player.GlobalPosition.Y - start.Y) / U.In:0} u, speed {player.Velocity.Length() / U.In:0}" );
			fm.TestInput = null;
			start = player.GlobalPosition;
			await Wait( 1.5f );
			GD.Print( $"[autotest] float drift after letting go: {(player.GlobalPosition - start).Length() / U.In:0} u, speed {player.Velocity.Length() / U.In:0}" );
			player.GetNode<PlayerMovement>( "PlayerMovement" ).TestPressDash();
			await Wait( 0.1f );
			GD.Print( $"[autotest] float dash: speed {player.Velocity.Length() / U.In:0}" );
			await Shot( "float_round" );
			WaveDirector.Current.FloatRound = false;
			await Wait( 1.5f );
			GD.Print( $"[autotest] anchor back: on ground {player.IsOnGround}" );
			GD.Print( "[autotest] done" );
			GetTree().Quit();
			return;
		}
		if ( userArgs.Contains( "--robot" ) )
		{
			// Third person and a free camera looking at the robot from the front-left.
			player.ThirdPerson = true;
			player.EyeAngles = new Angles( 0f, 180f, 0f );
			await Wait( 0.5f );
			async Task Look( string name, float yawAround = 35f, float dist = 120f, float height = 50f )
			{
				var feet = player.GlobalPosition;
				var facing = new Angles( 0f, player.EyeAngles.Yaw + yawAround, 0f ).Forward;
				DevFlags.FreeCamPosition = feet + facing * dist * U.In + Vector3.Up * height * U.In;
				var dir = (feet + Vector3.Up * 36f * U.In) - DevFlags.FreeCamPosition;
				DevFlags.FreeCamAngles = new Angles( -MathX.Deg( MathF.Atan2( dir.Y, dir.Flat().Length() ) ), MathX.Deg( MathF.Atan2( -dir.X, -dir.Z ) ), 0f );
				DevFlags.FreeCam = true;
				await Wait( 0.05f );
				await Shot( name );
			}
			await Look( "robot_idle", 0f );
			await Look( "robot_side", 90f );
			player.EyeAngles = new Angles( 40f, 180f, 0f );
			await Wait( 0.3f );
			await Look( "robot_aim_down" );
			player.EyeAngles = new Angles( -40f, 180f, 0f );
			await Wait( 0.3f );
			await Look( "robot_aim_up" );
			player.EyeAngles = new Angles( 0f, 180f, 0f );
			Input.ActionPress( "forward" );
			Input.ActionPress( "run" );
			for ( var k = 0; k < 4; k++ ) { await Wait( 0.17f ); await Look( $"robot_run_{k}", 60f, 160f ); }
			Input.ActionRelease( "run" );
			Input.ActionRelease( "forward" );
			await Wait( 0.6f );
			Input.ActionPress( "duck" );
			await Wait( 0.5f );
			await Look( "robot_crouch" );
			Input.ActionRelease( "duck" );
			await Wait( 0.4f );
			Input.ActionPress( "jump" );
			await Wait( 0.05f );
			Input.ActionRelease( "jump" );
			await Wait( 0.25f );
			await Look( "robot_jump" );
			await Wait( 1f );
			Input.ActionPress( "forward" );
			Input.ActionPress( "run" );
			await Wait( 0.6f );
			Input.ActionPress( "duck" );
			await Wait( 0.25f );
			await Look( "robot_slide", 70f, 150f );
			Input.ActionRelease( "duck" );
			Input.ActionRelease( "run" );
			Input.ActionRelease( "forward" );
			await Wait( 0.8f );
			player.GetNode<PlayerMelee>( "PlayerMelee" ).TestSwing();
			await Wait( 0.15f );
			await Look( "robot_axe_up" );
			await Wait( 0.1f );
			await Look( "robot_axe_hit" );
			await Wait( 0.6f );
			inventory.Select( inventory.Weapons[1] );
			await Wait( 0.6f );
			CharacterSkins.Current = "Hazard";
			await Wait( 0.1f );
			await Look( "robot_shotgun_hazard" );
			DevFlags.FreeCam = false;
			await Wait( 0.1f );
			await Shot( "robot_third_person_cam" );
			// Death: the robot drops as a ragdoll.
			var surv = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
			surv.Health.Invulnerable = false;
			surv.Health.OnDamage( new DamageInfo { Damage = 9999f, Position = player.GlobalPosition, Origin = player.GlobalPosition + Vector3.Forward } );
			for ( var k = 0; k < 30; k++ )
			{
				await Wait( 0.05f );
				var rp = GetTree().Root.FindChild( "Rag pelvis", true, false ) as Node3D;
				GD.Print( $"[autotest] ragdoll pelvis {(rp?.GlobalPosition - player.GlobalPosition) / U.In}" );
			}
			await Look( "robot_ragdoll_0", 40f, 140f );
			await Wait( 1.5f );
			await Look( "robot_ragdoll_1", 40f, 140f );
			var rag = GetTree().Root.FindChild( "Robot Ragdoll", true, false ) as Node3D;
			GD.Print( $"[autotest] ragdoll: {(rag is null ? "none" : "spawned")}, pelvis height {(rag?.FindChild( "Rag pelvis", true, false ) as Node3D)?.GlobalPosition.Y / U.In:0} vs feet {player.GlobalPosition.Y / U.In:0}" );
			GD.Print( "[autotest] done" );
			GetTree().Quit();
			return;
		}
		if ( userArgs.Contains( "--devmenu" ) )
		{
			var menu = GetTree().Root.FindChild( "DevMenu", true, false ) as DevMenu;
			foreach ( var t in new[] { "Enemies", "Player", "Look", "World" } ) { menu.TestShow( t ); await Wait( 0.2f ); await Shot( $"devmenu_{t.ToLower()}" ); }
			menu.TestShow( "Enemies", "boss" );
			await Wait( 0.2f );
			await Shot( "devmenu_search_boss" );
			menu.TestShow( "Player" );
			GD.Print( $"[autotest] devmenu: god pressed {menu.TestPress( "God mode" )}, god {survivor.Health.Invulnerable}" );
			survivor.Health.Invulnerable = false;
			menu.TestPress( "God mode" );
			menu.TestShow( "Enemies" );
			var squBefore = Squilien.All.Count;
			menu.TestPress( "Spawn" );   // first row: Zombie (not ported, nothing happens)
			var spawnButtons = 0;
			foreach ( var b in menu.FindChildren( "*", "Button", true, false ).OfType<Button>() ) if ( b.Text == "Spawn" ) spawnButtons++;
			menu.TestShow( "Enemies", "squilien" );
			menu.TestPress( "Spawn" );
			await Wait( 0.3f );
			GD.Print( $"[autotest] devmenu: spawn buttons {spawnButtons}, squiliens {squBefore} to {Squilien.All.Count}, god now {survivor.Health.Invulnerable}" );
			menu.TestShow( "World" );
			menu.TestPress( "Stats overlay" );
			menu.TestPress( "0.25x" );
			GD.Print( $"[autotest] devmenu: time scale {Engine.TimeScale} stats {DevFlags.ShowStats}" );
			menu.TestPress( "1x" );
			menu.SetOpen( false );
			await Wait( 0.5f );
			await Shot( "devmenu_closed_stats" );
			GD.Print( "[autotest] done" );
			GetTree().Quit();
			return;
		}

		// Squilien: two hovering in front.
		player.EyeAngles = new Angles( -10f, 180f, 0f );
		for ( var k = 0; k < 2; k++ )
			Squilien.Spawn( range, player.GlobalPosition + new Vector3( (k - 0.5f) * 200f, 150f, 450f ) * U.In, 130f ).Hue = k * 0.4f + 0.1f;
		await Wait( 2.5f );
		await Shot( "squilien_swim" );
		var q = Squilien.All.OrderBy( x => x.GlobalPosition.DistanceTo( player.GlobalPosition ) ).First();
		DevFlags.FreezeEnemies = true;
		var face = -q.GlobalBasis.Z;
		player.GlobalPosition = q.GlobalPosition + face * 110f * U.In - Vector3.Up * 60f * U.In;
		player.Velocity = Vector3.Zero;
		LookAt( q.GlobalPosition + Vector3.Down * 15f * U.In );
		await Wait( 0.4f );
		LookAt( q.GlobalPosition + Vector3.Down * 15f * U.In );
		await Shot( "squilien_face" );
		var weapon = inventory.Current;
		for ( var k = 0; k < 5; k++ ) { LookAt( q.GlobalPosition + Vector3.Down * 40f * U.In ); weapon.TestFire(); await Wait( 0.13f ); }
		await Wait( 0.2f );
		await Shot( "squilien_tentacles" );
		for ( var k = 0; k < 14 && !q.IsDead; k++ ) { LookAt( q.GlobalPosition ); weapon.TestFire(); await Wait( 0.13f ); }
		DevFlags.FreezeEnemies = false;
		GD.Print( $"[autotest] dents so far {DentedMesh.Total}, shapes refused {DentedMesh.Refused}" );
		GD.Print( $"[autotest] squilien: dead {q.IsDead}, health {q.Health?.Current:0}, alive {Squilien.All.Count( x => !x.IsDead )}" );
		await Wait( 0.4f );
		await Shot( "squilien_dead" );

		// Let the other one throw ink at us.
		survivor.Health.Invulnerable = false;
		var other = Squilien.All.FirstOrDefault( x => !x.IsDead );
		if ( other is not null )
		{
			player.GlobalPosition = other.GlobalPosition + Vector3.Back * 400f * U.In;
			await Wait( 0.1f );
			player.Velocity = Vector3.Zero;
			for ( var k = 0; k < 40; k++ ) { LookAt( other.GlobalPosition ); await Wait( 0.1f ); }
			GD.Print( $"[autotest] after ink: player health {survivor.Health.Current:0}" );
			await Shot( "squilien_ink" );
		}

		// Junk Crab: one walking at us, look close, shoot legs, claw and eye off, then kill it.
		survivor.Health.Invulnerable = true;
		foreach ( var s in Squilien.All.ToList() ) s.QueueFree();
		player.GlobalPosition = new Vector3( 0f, player.GlobalPosition.Y, 0f );
		player.Velocity = Vector3.Zero;
		player.EyeAngles = new Angles( 8f, 180f, 0f );
		var crab = JunkCrab.Spawn( range, player.GlobalPosition + new Vector3( 0f, 0f, 400f ) * U.In, 90f );
		crab.Hue = 0.3f;
		await Wait( 1.5f );
		await Shot( "crab_walk" );
		DevFlags.FreezeEnemies = true;
		var cf = -crab.GlobalBasis.Z;
		player.GlobalPosition = crab.GlobalPosition + cf * 75f * U.In;
		player.Velocity = Vector3.Zero;
		LookAt( crab.GlobalPosition + Vector3.Up * 10f * U.In );
		await Wait( 0.4f );
		LookAt( crab.GlobalPosition + Vector3.Up * 10f * U.In );
		await Shot( "crab_face" );
		player.GlobalPosition = crab.GlobalPosition + crab.GlobalBasis.X * 90f * U.In + Vector3.Up * 20f * U.In;
		LookAt( crab.GlobalPosition + Vector3.Up * 10f * U.In );
		await Wait( 0.4f );
		LookAt( crab.GlobalPosition + Vector3.Up * 10f * U.In );
		await Shot( "crab_side" );
		var cweapon = inventory.Current;
		var targets = CreaturePart.All.Where( p => p.Creature == crab && p.Name.ToString().Contains( "Leg" ) ).Take( 2 ).ToList();
		targets.AddRange( CreaturePart.All.Where( p => p.Creature == crab && p.Name.ToString().Contains( "Pincer" ) ).Take( 1 ) );
		foreach ( var t in targets )
			for ( var k = 0; k < 3 && IsInstanceValid( t ); k++ ) { LookAt( t.GlobalPosition ); cweapon.TestFire(); await Wait( 0.13f ); }
		await Wait( 0.3f );
		await Shot( "crab_cut" );
		for ( var k = 0; k < 14 && !crab.IsDead; k++ ) { LookAt( crab.GlobalPosition + Vector3.Up * 14f * U.In ); cweapon.TestFire(); await Wait( 0.13f ); }
		DevFlags.FreezeEnemies = false;
		GD.Print( $"[autotest] crab: dead {crab.IsDead}, health {crab.Health?.Current:0}" );
		await Wait( 0.5f );
		await Shot( "crab_dead" );

		// One more crab to see it pinch.
		survivor.Health.Invulnerable = false;
		var before = survivor.Health.Current;
		JunkCrab.Spawn( range, player.GlobalPosition + new Vector3( 0f, 0f, 250f ) * U.In, 90f );
		for ( var k = 0; k < 40; k++ ) { var c2 = JunkCrab.All.LastOrDefault(); if ( c2 is not null ) LookAt( c2.GlobalPosition ); await Wait( 0.1f ); }
		GD.Print( $"[autotest] crab pinch: health {before:0} to {survivor.Health.Current:0}" );
		await Shot( "crab_pinch" );

	Boss:
		// MC61: spawn, look at him, run each move, then shoot the hat and eye off and kill him.
		foreach ( var c in JunkCrab.All.ToList() ) c.QueueFree();
		survivor.Health.Invulnerable = true;
		player.GlobalPosition = new Vector3( 0f, player.GlobalPosition.Y, 0f );
		player.Velocity = Vector3.Zero;
		var boss = MC61.Spawn( range, player.GlobalPosition + new Vector3( 0f, 160f, 500f ) * U.In, 2000f );
		await Wait( 0.9f );
		LookAt( boss.GlobalPosition );
		await Wait( 0.1f );
		await Shot( "mc61_spawn_in" );
		await Wait( 1.5f );
		LookAt( boss.GlobalPosition );
		await Wait( 0.2f );
		LookAt( boss.GlobalPosition );
		await Shot( "mc61_float" );
		DevFlags.FreezeEnemies = true;
		var bf = -boss.GlobalBasis.Z;
		player.GlobalPosition = (boss.GlobalPosition + bf * 200f * U.In).WithY( player.GlobalPosition.Y );
		player.Velocity = Vector3.Zero;
		LookAt( boss.GlobalPosition + Vector3.Up * 20f * U.In );
		await Wait( 0.4f );
		LookAt( boss.GlobalPosition + Vector3.Up * 20f * U.In );
		await Shot( "mc61_face" );
		var handL = CreaturePart.All.First( p => p.Creature == boss && p.Name.ToString().Contains( "Hand L" ) );
		// Straight on at hand height (the camera floats; physics is frozen for the creature only, so pin the player).
		player.Scripted = true;
		player.GlobalPosition = handL.GlobalPosition + bf * 45f * U.In - Vector3.Up * 64f * U.In;
		LookAt( handL.GlobalPosition );
		await Wait( 0.3f );
		LookAt( handL.GlobalPosition );
		await Shot( "mc61_hand" );
		var bs = boss.GlobalBasis.X;
		player.GlobalPosition = handL.GlobalPosition - bs * 45f * U.In - Vector3.Up * 64f * U.In;
		LookAt( handL.GlobalPosition );
		await Wait( 0.3f );
		LookAt( handL.GlobalPosition );
		await Shot( "mc61_hand_side" );
		player.Scripted = false;
		player.GlobalPosition = (boss.GlobalPosition + bf * 200f * U.In).WithY( player.GlobalPosition.Y );
		DevFlags.FreezeEnemies = false;
		boss.ForceState( MC61.Mode.Charge );
		await Wait( 1.2f );
		await Shot( "mc61_charge" );
		await Wait( 1.2f );
		await Shot( "mc61_beam" );
		await Wait( 2.6f );
		boss.ForceState( MC61.Mode.Cards );
		await Wait( 0.4f );
		await Shot( "mc61_cards_hold" );
		await Wait( 0.7f );
		await Shot( "mc61_cards" );
		await Wait( 1.2f );
		boss.ForceState( MC61.Mode.Dance );
		await Wait( 1.2f );
		LookAt( boss.GlobalPosition );
		await Wait( 0.1f );
		await Shot( "mc61_dance" );
		await Wait( 3.5f );
		DevFlags.FreezeEnemies = true;
		var bweapon = inventory.Current;
		var hatPart = CreaturePart.All.FirstOrDefault( p => p.Creature == boss && p.Name.ToString().Contains( "Hat" ) );
		for ( var k = 0; k < 4 && hatPart is not null && IsInstanceValid( hatPart ); k++ ) { LookAt( hatPart.GlobalPosition ); bweapon.TestFire(); await Wait( 0.13f ); }
		var eyePart = CreaturePart.All.FirstOrDefault( p => p.Creature == boss && p.Name.ToString().Contains( "Eye" ) );
		for ( var k = 0; k < 14 && eyePart is not null && IsInstanceValid( eyePart ) && !boss.IsDead; k++ ) { LookAt( eyePart.GlobalPosition ); bweapon.TestFire(); await Wait( 0.13f ); }
		await Wait( 0.3f );
		GD.Print( $"[autotest] mc61 after hat and eye: health {boss.Health?.Current:0} line {boss.Line}" );
		await Shot( "mc61_cut" );
		boss.Health.OnDamage( new DamageInfo { Damage = 99999f, Position = boss.GlobalPosition, Origin = player.GlobalPosition, Attacker = player } );
		DevFlags.FreezeEnemies = false;
		await Wait( 0.6f );
		GD.Print( $"[autotest] mc61 dead {boss.IsDead}" );
		await Shot( "mc61_dead" );

		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	async Task LevelTest()
	{
		if ( OS.GetCmdlineUserArgs().Contains( "--stations" ) ) { await StationTest(); return; }
		var hp = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health;
		// The dive Bloobers bite (as in s&box); this test is about the dive itself, so the player can't die here.
		hp.Invulnerable = true;
		if ( DockHouse.Current is { } dock )
		{
			await Wait( 0.5f );
			player.EyeAngles = new Angles( 8f, 0f, 0f );
			await Shot( "dock_start" );
			player.EyeAngles = new Angles( 15f, 60f, 0f );
			await Wait( 0.3f );
			await Shot( "dock_water" );
			// Walk out onto the pier and look back at the house and out over the water.
			var spot = player.GlobalPosition + U.Sbox( 330f, 0f, 0f );
			player.GlobalPosition = spot;
			player.ThirdPerson = true;
			player.EyeAngles = new Angles( 15f, 180f, 0f );
			await Wait( 0.5f );
			await Shot( "dock_house" );
			player.ThirdPerson = false;
			player.EyeAngles = new Angles( 12f, -40f, 0f );
			await Wait( 0.3f );
			await Shot( "dock_sea" );
			if ( !dock.DiveSequence )
			{
				// Walk to the end of the diving board: the prompt shows, E takes you down to the sea floor.
				player.GlobalPosition = U.Sbox( dock.Origin.X + dock.PierLength + 30f, dock.Origin.Y, dock.SurfaceHeight + 24f );
				player.EyeAngles = new Angles( 10f, 0f, 0f );
				await Wait( 0.4f );
				var use = player.GetNode<PlayerUse>( "PlayerUse" );
				GD.Print( $"[autotest] at board: prompt {use.AtDivingBoard}, waves held {DockHouse.HoldWaves}, wave {WaveDirector.Current?.Wave}" );
				await Shot( "board_prompt" );
				use.PressUse();
				await Wait( 1.6f );
				GD.Print( $"[autotest] sea floor: at {player.GlobalPosition}, on ground {player.IsOnGround}, waves held {DockHouse.HoldWaves}, weapons held {DockHouse.WeaponsHeld}" );
				await Shot( "sea_floor" );
				await Wait( 4f );
				GD.Print( $"[autotest] after landing: wave {WaveDirector.Current?.Wave}, state {WaveDirector.Current?.State}" );
			}
			else
			{
			dock.TestDive();
			var t0 = Clock.Real;
			while ( !DockHouse.Diving && Clock.Real - t0 < 5 ) await Wait( 0.05f );
			player.EyeAngles = new Angles( 85f, 0f, 0f );
			await Wait( 1.5f );
			await Shot( "dive_fall" );
			await Wait( 4f );
			await Shot( "dive_bloobers" );
			GD.Print( $"[autotest] dive: depth {DockHouse.DiveDepth:0} m, speed {DockHouse.DiveVelocity.Length():0} u/s, crushed {DockHouse.DiveCrushed}" );
			while ( DockHouse.Diving && DockHouse.DiveVelocity.Length() > 1f && Clock.Real - t0 < 60 ) await Wait( 0.05f );
			GD.Print( $"[autotest] landed after {Clock.Real - t0:0.0} s, crushed {DockHouse.DiveCrushed}" );
			await Wait( 0.2f );
			await Shot( "dive_land" );
			while ( DockHouse.Diving && Clock.Real - t0 < 70 ) await Wait( 0.1f );
			}
			player.EyeAngles = new Angles( 30f, 0f, 0f );
			await Wait( 1.5f );
			await Shot( "crater" );
			player.ThirdPerson = true;
			player.EyeAngles = new Angles( 40f, 180f, 0f );
			await Wait( 0.5f );
			await Shot( "crater_3p" );
			player.ThirdPerson = false;
		}

		for ( var i = 0; i < 4; i++ )
		{
			player.EyeAngles = new Angles( 5f, i * 90f, 0f );
			await Wait( 0.4f );
			await Shot( $"level_{Level.Current.LevelName}_{i * 90}" );
		}
		player.EyeAngles = new Angles( 25f, 45f, 0f );
		player.ThirdPerson = true;
		await Wait( 0.5f );
		await Shot( $"level_{Level.Current.LevelName}_third" );
		player.ThirdPerson = false;

		// The ally spider: it should have spawned beside us and be standing on its legs.
		await Wait( 2.5f );
		var ally = SeaSpider.All.FirstOrDefault( x => x.IsAlly );
		GD.Print( $"[autotest] ally: {(ally is null ? "none" : $"at {ally.GlobalPosition / U.In}, steps {ally.StepCount}")}" );
		if ( ally is not null )
		{
			DevFlags.FreezeEnemies = true;
			player.GlobalPosition = ally.GlobalPosition + new Vector3( 160f, 0f, 160f ) * U.In;
			player.ThirdPerson = true;
			LookAt( ally.GlobalPosition + Vector3.Up * 40f * U.In );
			await Wait( 0.3f );
			LookAt( ally.GlobalPosition + Vector3.Up * 40f * U.In );
			await Shot( "ally_spider" );
			player.ThirdPerson = false;
			DevFlags.FreezeEnemies = false;
			// Walk away so it follows.
			player.GlobalPosition += new Vector3( 600f, 0f, 0f ) * U.In;
			await Wait( 3f );
			LookAt( ally.GlobalPosition + Vector3.Up * 40f * U.In );
			await Wait( 0.1f );
			await Shot( "ally_follow" );
			GD.Print( $"[autotest] ally after follow: dist {(ally.GlobalPosition - player.GlobalPosition).Flat().Length() / U.In:0} steps {ally.StepCount}" );
		}

		// Jetpack: hold thrust for a second, then a surge.
		var jet = player.GetNode<Jetpack>( "Jetpack" );
		var y0 = player.GlobalPosition.Y;
		Input.ActionPress( "jump" ); await Wait( 0.05f ); Input.ActionRelease( "jump" );
		jet.TestHold = true;
		await Wait( 1.2f );
		jet.TestHold = false;
		GD.Print( $"[autotest] jetpack: rose {(player.GlobalPosition.Y - y0) / U.In:0} u, power {jet.Power:0}, stalled {jet.Stalled}" );
		await Shot( "jetpack" );
		await Wait( 3f );
		player.EyeAngles = new Angles( 0f, 90f, 0f );
		var p0 = player.GlobalPosition;
		jet.TestSurge( 0.5f );
		await Wait( 0.75f );
		await Shot( "surge" );
		await Wait( 0.6f );
		GD.Print( $"[autotest] surge: moved {(player.GlobalPosition - p0).Length() / U.In:0} u" );

		// An enemy spider: shoot its legs off.
		var director = WaveDirector.Current;
		var enemySpider = director.SpawnAt( WaveDirector.EnemyKind.Spider, player.GlobalPosition + new Vector3( 0f, 0f, -500f ) * U.In, player.GlobalPosition ) as SeaSpider;
		DevFlags.FreezeEnemies = true;
		await Wait( 0.5f );
		if ( enemySpider is not null )
		{
			player.GlobalPosition = enemySpider.GlobalPosition + new Vector3( 160f, 0f, 160f ) * U.In;
			LookAt( enemySpider.GlobalPosition + Vector3.Up * 30f * U.In );
			await Wait( 0.3f );
			LookAt( enemySpider.GlobalPosition + Vector3.Up * 30f * U.In );
			await Shot( "enemy_spider" );
			player.ThirdPerson = true;
			await Wait( 0.3f );
			await Shot( "enemy_spider_3p" );
			player.ThirdPerson = false;
			var weapon = player.GetNode<WeaponInventory>( "WeaponInventory" ).Current;
			for ( var k = 0; k < 12 && !enemySpider.IsDead; k++ ) { LookAt( enemySpider.GlobalPosition + Vector3.Up * 10f * U.In + Vector3.Right * 30f * U.In ); weapon.TestFire(); await Wait( 0.13f ); }
			await Shot( "enemy_spider_shot" );
			GD.Print( $"[autotest] enemy spider: dead {enemySpider.IsDead} health {enemySpider.Health?.Current:0}" );
		}
		DevFlags.FreezeEnemies = false;

		director.SpawningPaused = false;
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		await Wait( 9f );
		GD.Print( $"[autotest] level waves: state {director.State} wave {director.Wave} alive {director.AliveCount}" );
		var b = Bloober.All.FirstOrDefault();
		if ( b is not null ) LookAt( b.GlobalPosition );
		await Wait( 0.2f );
		await Shot( $"level_{Level.Current.LevelName}_bloober" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	void LookAt( Vector3 at )
	{
		var dir = (at - player.Camera.GlobalPosition).Normalized();
		player.EyeAngles = new Angles( MathX.Deg( -MathF.Asin( dir.Y ) ), MathX.Deg( MathF.Atan2( -dir.X, -dir.Z ) ), 0f );
	}

	void Report( string label )
	{
		var v = player.Velocity;
		GD.Print( $"[autotest] {label}: horizontal {v.Flat().Length() / U.In:0} u/s, vertical {v.Y / U.In:0}, ground={player.IsOnGround}, mode={movement.Ground}, pos {player.GlobalPosition / U.In}" );
	}

	/// <summary> Frame-to-frame smoothness of what is drawn while walking past creatures at 144 fps. </summary>
	async Task JitterTest()
	{
		Engine.MaxFps = 144;
		player.ThirdPerson = OS.GetCmdlineUserArgs().Contains( "--third" );
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		var at = player.GlobalPosition - player.GlobalBasis.Z * 300f * U.In;
		var tracked = new System.Collections.Generic.List<(string, Node3D)> { ("camera", player.Camera) };
		var bloober = Bloober.Spawn( GetTree().CurrentScene, at + Vector3.Up * 60f * U.In, 1000f );
		var squilien = Squilien.Spawn( GetTree().CurrentScene, at + Vector3.Right * 150f * U.In, 1000f );
		var crab = JunkCrab.Spawn( GetTree().CurrentScene, at + Vector3.Left * 150f * U.In, 1000f );
		await Wait( 1f );
		tracked.Add( ("bloober", bloober) );
		tracked.Add( ("squilien", squilien) );
		tracked.Add( ("crab", crab) );
		foreach ( var (name, n) in tracked.ToArray() )
			if ( n.FindChildren( "*", "MeshInstance3D", true, false ) is { Count: > 2 } meshes ) tracked.Add( (name + " part", (Node3D)meshes[2]) );
		var rig = player.FindChild( "Survivor Rig", true, false ) as Node3D;
		if ( rig is not null ) tracked.Add( ("rig", rig) );
		// Everything the player carries (only the rough ones are printed).
		var all = OS.GetCmdlineUserArgs().Contains( "--all" );
		if ( all )
			foreach ( var n in player.FindChildren( "*", "Node3D", true, false ) )
				if ( n is Node3D n3 && n3.IsVisibleInTree() ) tracked.Add( (player.GetPathTo( n3 ).ToString(), n3) );

		var samples = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Vector3>>();
		var errors = new System.Collections.Generic.Dictionary<string, float>();
		foreach ( var (name, _) in tracked ) { samples[name] = new(); errors[name] = 0f; }
		Input.ActionPress( "forward" );
		var frames = 0;
		var start = Time.GetTicksMsec();
		while ( Time.GetTicksMsec() - start < 3000 )
		{
			await ToSignal( RenderingServer.Singleton, RenderingServer.SignalName.FramePreDraw );
			frames++;
			player.EyeAngles = new Angles( 0f, player.EyeAngles.Yaw + 0.4f, 0f );
			foreach ( var (name, n) in tracked )
			{
				if ( !IsInstanceValid( n ) ) continue;
				var drawn = n.IsPhysicsInterpolatedAndEnabled() ? n.GetGlobalTransformInterpolated().Origin : n.GlobalPosition;
				samples[name].Add( drawn );
				errors[name] = MathF.Max( errors[name], drawn.DistanceTo( n.GlobalPosition ) );
			}
		}
		Input.ActionRelease( "forward" );
		GD.Print( $"[jitter] {frames} frames in 3 s, interpolation {(GetTree().PhysicsInterpolation ? "on" : "off")}" );
		foreach ( var (name, list) in samples )
		{
			// Roughness: how much the per-frame step changes, relative to the average step (0 = perfectly even motion).
			float sum = 0, change = 0; var steps = 0; var still = 0;
			for ( var i = 2; i < list.Count; i++ )
			{
				var a = list[i - 1] - list[i - 2]; var b = list[i] - list[i - 1];
				sum += b.Length(); change += (b - a).Length(); steps++;
				if ( b.Length() < 1e-5f && a.Length() > 1e-4f ) still++;
			}
			var mean = sum / MathF.Max( steps, 1 );
			if ( name.Contains( '/' ) && (mean < 0.05f * U.In || change / steps / mean < 0.2f) ) continue;
			GD.Print( $"[jitter] {name,-14} mean step {mean / U.In:0.00} u, roughness {(mean > 0 ? change / steps / mean : 0):0.00}, frozen frames {still}, max drawn-vs-real {errors[name] / U.In:0.0} u" );
		}
		GetTree().Quit();
	}

	/// <summary> Camera held still: counts pixels that blink (change and change back) between frames. </summary>
	async Task FlickerTest()
	{
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		await Wait( 2f );
		var views = new[] { 0f, 90f, 180f, 270f };
		foreach ( var yaw in views )
		{
			player.EyeAngles = new Angles( 8f, yaw, 0f );
			await Wait( 0.5f );
			Image prev = null, prev2 = null;
			int blinks = 0, worstFrame = 0; var frames = 30;
			for ( var f = 0; f < frames; f++ )
			{
				await ToSignal( RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw );
				var img = GetViewport().GetTexture().GetImage();
				img.Resize( 320, 180, Image.Interpolation.Nearest );
				if ( prev2 is not null )
				{
					var count = 0;
					for ( var y = 0; y < 180; y++ )
						for ( var x = 0; x < 320; x++ )
						{
							float a = prev2.GetPixel( x, y ).Luminance, b = prev.GetPixel( x, y ).Luminance, c = img.GetPixel( x, y ).Luminance;
							if ( MathF.Abs( b - a ) > 0.15f && MathF.Abs( b - c ) > 0.15f && MathF.Abs( c - a ) < 0.05f ) count++;
						}
					blinks += count;
					if ( count > worstFrame ) { worstFrame = count; if ( !string.IsNullOrEmpty( shots ) ) { prev.SavePng( System.IO.Path.Combine( shots, $"blink_{yaw}_b.png" ) ); prev2.SavePng( System.IO.Path.Combine( shots, $"blink_{yaw}_a.png" ) ); } }
				}
				prev2 = prev; prev = img;
			}
			GD.Print( $"[flicker] yaw {yaw}: blinking pixels per frame {blinks / (float)(frames - 2):0.0} of 57600, worst frame {worstFrame}" );
		}
		GetTree().Quit();
	}

	/// <summary> Playtest 1 fixes: ammo pickups refill, kills before a wave shorten it. </summary>
	async Task Playtest1Test()
	{
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var rifle = inventory.Weapons[0];
		while ( rifle.Reserve > 0 ) rifle.AddAmmo( -1 );
		var before = rifle.Reserve;
		AmmoPickup.DropChance = 1f;
		var dummy = new Node3D();
		GetTree().CurrentScene.AddChild( dummy );
		dummy.GlobalPosition = player.GlobalPosition + player.GlobalBasis.Z * -10f * U.In;
		dummy.GlobalPosition = player.GlobalPosition + -new Angles( 0f, player.EyeAngles.Yaw, 0f ).ToBasis().Z * 200f * U.In;
		AmmoPickup.MaybeDrop( dummy );
		await Wait( 0.4f );
		await Shot( "ammo_box" );
		dummy.GlobalPosition = player.GlobalPosition + player.GlobalBasis.Z * -10f * U.In;
		AmmoPickup.MaybeDrop( dummy );
		await Wait( 0.5f );
		GD.Print( $"[autotest] ammo pickup: reserve {before} -> {rifle.Reserve} (expect +{rifle.MagazineSize})" );

		var director = WaveDirector.Current;
		if ( director is not null )
		{
			director.SpawningPaused = true;
			var wave = director.Wave;
			for ( var i = 0; i < 3; i++ ) director.CreditKill();
			GD.Print( $"[autotest] credited 3 kills before/at wave {wave}: kills {director.Kills}, state {director.State}, remaining {director.ZombiesRemaining}" );
		}
		GD.Print( $"[autotest] weapons held (no dock here): {DockHouse.WeaponsHeld} (expect False)" );
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Afflictions?.Splattered( new Color( 0.3f, 0.95f, 0.25f ), 7 );
		await Wait( 0.15f );
		await Shot( "screen_goo" );
		// Turn hard right for half a second (the goo should drag left), then strafe left (it should drift right).
		for ( var i = 0; i < 30; i++ ) { player.EyeAngles = new Angles( 0f, player.EyeAngles.Yaw - 3f, 0f ); await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame ); }
		await Shot( "screen_goo_turned" );
		Input.ActionPress( "left" );
		await Wait( 0.6f );
		Input.ActionRelease( "left" );
		await Shot( "screen_goo_strafed" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> Prints a map of where a ray straight down hits ground (#) or not (.) around the dive line. </summary>
	void SeabedMap()
	{
		var center = new Vector3( 8.9f, 0f, -18.8f );
		var walk = Nav.Closest( player, center with { Y = 400f } );
		GD.Print( $"[seabed] nav near 400 m: {walk}" );
		foreach ( var n in GetTree().Root.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>() )
			if ( n.GlobalPosition.Y > 300f && n.GlobalPosition.Y < 500f && n is not MeshInstance3D && n.GetParent() is not Node3D { Name: var pn } || false )
				GD.Print( $"[seabed] node {n.GetPath()} at {n.GlobalPosition}" );
		var counts = new System.Collections.Generic.Dictionary<string, int>();
		foreach ( var n in GetTree().Root.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>() )
			if ( n.GlobalPosition.Y > 300f && n.GlobalPosition.Y < 500f )
			{
				var top = n; while ( top.GetParent() is Node3D up && up.GlobalPosition.Y > 300f && up.GlobalPosition.Y < 500f ) top = up;
				var key = top.GetPath().ToString();
				counts[key] = counts.GetValueOrDefault( key ) + 1;
			}
		foreach ( var (k, v) in counts.OrderByDescending( x => x.Value ).Take( 15 ) ) GD.Print( $"[seabed] group {k} x{v}" );
		GD.Print( $"[seabed] level {Level.Current?.LevelName} at {Level.Current?.GlobalPosition}, navmesh {Nav.Region?.GetPath()}" );
		foreach ( var n in GetTree().Root.FindChildren( "*", "CollisionShape3D", true, false ).OfType<CollisionShape3D>() )
			if ( n.GlobalPosition.Y > 100f && n.GlobalPosition.Y < 700f ) GD.Print( $"[seabed] shape at {n.GlobalPosition} under {n.GetParent()?.GetParent()?.Name}/{n.GetParent()?.Name} {n.Shape?.GetType().Name}" );
		for ( var z = -100; z <= 100; z += 10 )
		{
			var row = "";
			for ( var x = -100; x <= 100; x += 10 )
			{
				var at = center + new Vector3( x, 0f, z );
				var hit = Trace.Ray( player, at + Vector3.Up * 800f, at + Vector3.Down * 200f, Layers.World, new Godot.Collections.Array<Rid> { player.GetRid() } );
				row += !hit.Hit ? "." : hit.HitPosition.Y > 600f ? "^" : hit.HitPosition.Y > 100f ? "S" : "0";
			}
			GD.Print( $"[seabed] z {center.Z + z,6:0} {row}" );
		}
		var probe = Trace.Ray( player, center + Vector3.Up * 800f, center + Vector3.Down * 200f, Layers.World, new Godot.Collections.Array<Rid> { player.GetRid() } );
		GD.Print( $"[seabed] at dive line: hit {probe.Hit} {probe.HitPosition} node {probe.Node?.Name} parent {probe.Node?.GetParent()?.Name}" );
		GetTree().Quit();
	}

	/// <summary> Dives while steering hard sideways the whole way (the playtest endless fall). Should still land. </summary>
	async Task DiveEdgeTest()
	{
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		await Wait( 1f );
		DockHouse.Current.TestDive();
		Input.ActionPress( "right" );
		Input.ActionPress( "duck" );   // full speed
		var start = Time.GetTicksMsec();
		await Wait( 1f );
		while ( DockHouse.Diving && Time.GetTicksMsec() - start < 90000 ) await Wait( 0.5f );
		Input.ActionRelease( "right" );
		Input.ActionRelease( "duck" );
		await Wait( 1f );
		GD.Print( $"[autotest] dive edge: still diving {DockHouse.Diving}, at {player.GlobalPosition}, on ground {player.IsOnGround}, took {(Time.GetTicksMsec() - start) / 1000f:0} s" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> Shoots each creature kind at a few distances and saves the frames right after each hit. </summary>
	async Task HitFramesTest()
	{
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		DevFlags.FreezeEnemies = true;
		var range = GetTree().CurrentScene;
		foreach ( var weaponIndex in new[] { 0, 1 } )
		foreach ( var dist in new[] { 120f, 300f } )
		{
			inventory.Select( inventory.Weapons[weaponIndex], force: true );
			await Wait( 0.6f );
			var target = Bloober.Spawn( range, player.GlobalPosition + new Vector3( 0f, 60f, -dist ) * U.In, 400f );
			await Wait( 0.5f );
			LookAt( target.GlobalPosition );
			await Wait( 0.2f );
			inventory.Weapons[weaponIndex].TestFire();
			for ( var f = 0; f < 10; f++ )
			{
				await ToSignal( RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw );
				if ( f % 2 == 0 ) await Shot( $"hit_w{weaponIndex}_{dist:0}_f{f}" );
			}
			await Wait( 1f );
			target.QueueFree();
			await Wait( 0.3f );
		}
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> A Bloober swimming across in front of the camera, then biting: frames every 0.12 s. </summary>
	async Task SwimFramesTest()
	{
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		var range = GetTree().CurrentScene;
		var forward = -new Angles( 0f, player.EyeAngles.Yaw, 0f ).ToBasis().Z;
		var right = new Angles( 0f, player.EyeAngles.Yaw, 0f ).ToBasis().X;
		var b = Bloober.Spawn( range, player.GlobalPosition + forward * 260f * U.In + right * -200f * U.In + Vector3.Up * 50f * U.In, 5000f, right );
		await Wait( 0.3f );
		for ( var f = 0; f < 8; f++ ) { await Wait( 0.12f ); await Shot( $"swim_{f}" ); }
		await Wait( 2.5f );
		LookAt( b.GlobalPosition );
		for ( var f = 0; f < 6; f++ ) { await Wait( 0.08f ); LookAt( b.GlobalPosition ); await Shot( $"bite_{f}" ); }
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> Real mouse events on a dev menu chip: hovering must not rebuild it, a click must toggle it. </summary>
	async Task DevClickTest()
	{
		var menu = GetTree().Root.FindChild( "DevMenu", true, false ) as DevMenu;
		menu.TestShow( "Enemies", "freeze" );
		await Wait( 0.5f );
		var chip = menu.FindChip( "Freeze enemies" );
		if ( chip is null ) { GD.Print( "[autotest] devclick: chip not found" ); GetTree().Quit(); return; }
		var at = chip.GetGlobalRect().GetCenter();
		GetViewport().PushInput( new InputEventMouseMotion { Position = at, GlobalPosition = at }, true );
		var before = menu.Rebuilds;
		await Wait( 2f );
		GD.Print( $"[autotest] devclick: rebuilds while hovering 2 s: {menu.Rebuilds - before} (expect 0)" );
		Check( menu.Rebuilds == before, "the dev menu rebuilt while the mouse hovered over it" );
		chip = menu.FindChip( "Freeze enemies" );
		at = chip.GetGlobalRect().GetCenter();
		var was = DevFlags.FreezeEnemies;
		GetViewport().PushInput( new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = true }, true );
		await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
		GetViewport().PushInput( new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = false }, true );
		await Wait( 0.3f );
		GD.Print( $"[autotest] devclick: freeze enemies {was} -> {DevFlags.FreezeEnemies} (expect a change), menu open {DevMenu.IsOpen}, mouse {Input.MouseMode}" );
		Check( was != DevFlags.FreezeEnemies, "clicking a dev menu chip did nothing" );
		if ( !was ) DevFlags.FreezeEnemies = false;

		// Spawning the Kraken the way a player does: on the sea bed, dev menu (game paused), click Spawn, close.
		menu.SetOpen( false );
		DockHouse.Current?.SkipDive();
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		player.GlobalPosition = Nav.Closest( player, new Vector3( -8f, 0f, 12f ) ) + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		await Wait( 0.6f );
		menu.TestShow( "Enemies", "kraken" );
		await Wait( 0.4f );
		var spawn = menu.FindChip( "Spawn" );
		Check( spawn is not null, "the dev menu has no Spawn button for the Kraken" );
		if ( spawn is not null )
		{
			at = spawn.GetGlobalRect().GetCenter();
			GetViewport().PushInput( new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = true }, true );
			await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
			GetViewport().PushInput( new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = false }, true );
			await Wait( 0.5f );
			var kraken = Kraken.All.FirstOrDefault();
			GD.Print( $"[autotest] devclick: Kraken spawned {kraken is not null}, tree paused {GetTree().Paused}, state {kraken?.State}" );
			Check( kraken is not null, "clicking Spawn did not make a Kraken" );
			Check( GetTree().Paused, "the game was running behind the dev menu" );
			menu.SetOpen( false );
			await Wait( 6f );
			if ( kraken is not null )
			{
				var away = kraken.GlobalPosition.DistanceTo( player.GlobalPosition );
				var onFloor = MathF.Abs( kraken.GlobalPosition.Y - player.GlobalPosition.Y ) < 3f;
				GD.Print( $"[autotest] devclick: after closing the menu the Kraken is {kraken.State}, {away:0.0} m away, on the sea bed {onFloor}, brain {kraken.BrainPosition.Y - kraken.GlobalPosition.Y:0.0} m up" );
				Check( kraken.State == Kraken.Mode.Idle, "the Kraken did not finish coming up after the menu closed" );
				Check( onFloor, "the Kraken did not come up on the sea bed" );
				LookAt( kraken.GlobalPosition + Vector3.Up * 2f );
				await Wait( 0.3f );
				await Shot( "kraken_dev_spawn" );
			}
		}
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	async Task Wait( float seconds )
	{
		await ToSignal( GetTree().CreateTimer( seconds, processAlways: true, processInPhysics: true ), SceneTreeTimer.SignalName.Timeout );
	}

	/// <summary> The rifle up close and its muzzle flash, frame by frame, in first and third person. </summary>
	async Task FlashTest()
	{
		if ( OS.GetCmdlineUserArgs().Contains( "--hold" ) ) Fx.FlashLife = 3f;
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		DevFlags.FreezeEnemies = true;
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		foreach ( var weaponIndex in new[] { 0, 1 } )
		{
			inventory.Select( inventory.Weapons[weaponIndex], force: true );
			await Wait( 1.2f );
			player.EyeAngles = new Angles( 4f, 30f, 0f );
			await Wait( 0.3f );
			await Shot( $"gun_w{weaponIndex}_idle" );
			player.EyeAngles = new Angles( 2f, 0f, 0f );   // out of the door, over the sea
			await Wait( 0.3f );
			var weapon = inventory.Weapons[weaponIndex];
			if ( weapon.ViewModel is { } vm )
			{
				var m = vm.Muzzle();
				var cam = GetViewport().GetCamera3D();
				GD.Print( $"[autotest] flash: barrel forward vs view {m.Basis.Y.Normalized().Dot( -cam.GlobalBasis.Z ):0.00} (expect near 1)" );
			}
			weapon.TestFire();
			var flashNode = GetTree().Root.FindChild( "Muzzle Flash", true, false ) as Node3D;
			GD.Print( $"[autotest] flash: node {(flashNode is null ? "missing" : $"at {flashNode.GlobalPosition}, children {flashNode.GetChildCount()}, parent scale {(flashNode.GetParent() as Node3D)?.GlobalBasis.Scale}, visible {flashNode.IsVisibleInTree()}")}, camera at {GetViewport().GetCamera3D().GlobalPosition}" );
			for ( var f = 0; f < 4; f++ )
			{
				await ToSignal( RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw );
				await Shot( $"flash_w{weaponIndex}_f{f}" );
			}
			await Wait( 1f );
		}
		// The axe, mid swing.
		var melee = player.GetChildren().OfType<PlayerMelee>().FirstOrDefault();
		if ( melee is not null )
		{
			player.EyeAngles = new Angles( 4f, 0f, 0f );
			melee.TestSwing();
			await Wait( 0.12f );
			await Shot( "axe_raised" );
			await Wait( 0.14f );
			await Shot( "axe_strike" );
			await Wait( 1f );
		}
		player.ThirdPerson = true;
		inventory.Select( inventory.Weapons[0], force: true );
		player.EyeAngles = new Angles( 4f, 120f, 0f );
		await Wait( 1f );
		inventory.Weapons[0].TestFire();
		await ToSignal( RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw );
		await Shot( "flash_3p" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> The real start of a run, with nothing helped along: dive in off the board, then enemies must come. </summary>
	async Task WaveFlowTest()
	{
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		await Wait( 1f );
		var dock = DockHouse.Current;
		player.GlobalPosition = U.Sbox( dock.Origin.X + dock.PierLength + 30f, dock.Origin.Y, dock.SurfaceHeight + 24f );
		await Wait( 0.4f );
		player.GetNode<PlayerUse>( "PlayerUse" ).PressUse();
		await Wait( 16f );
		var d = WaveDirector.Current;
		GD.Print( $"[autotest] waveflow: wave {d.Wave}, state {d.State}, spawning paused {d.SpawningPaused}, alive {d.AliveCount} (expect some), remaining {d.ZombiesRemaining}" );
		Check( d.Wave >= 1 && d.State == WaveState.Fighting, "the first wave did not start after diving in" );
		Check( d.AliveCount > 0, "no enemies spawned after diving in" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> The pause menu with real input: Esc stops the game, a slider changes a setting, Resume carries on. </summary>
	/// <summary> The sea floor's dressing: the landmarks are there and reachable, and pictures of each. </summary>
	async Task SeaTest()
	{
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		var dressing = GetTree().Root.FindChild( "Sea Dressing", true, false ) as Node3D;
		Check( dressing is not null, "the water level has no sea dressing" );
		if ( dressing is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		GD.Print( $"[autotest] sea: {dressing.GetChildCount()} things placed, schools {dressing.GetChildren().OfType<FishSchool>().Count()}" );
		foreach ( var name in new[] { "Anchor", "Rowboat" } )
		{
			var prop = dressing.GetNodeOrNull<Node3D>( name );
			Check( prop is not null, $"no {name} on the sea floor" );
			if ( prop is null ) continue;
			var target = prop.GlobalPosition + Vector3.Up * 0.6f;
			var stand = Nav.Closest( player, prop.GlobalPosition + new Vector3( 5.5f, 0f, 3.5f ) );
			player.GlobalPosition = stand + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			await Wait( 0.6f );
			LookAt( target );
			await Wait( 0.3f );
			GD.Print( $"[autotest] sea: {name} at {prop.GlobalPosition}, standing {player.GlobalPosition.DistanceTo( prop.GlobalPosition ):0.0} m away on the navmesh" );
			Check( player.GlobalPosition.DistanceTo( prop.GlobalPosition ) < 9f, $"could not stand near the {name}" );
			await Shot( "sea_" + name.ToLower() );
		}
		// A wide look across the floor, and up at the fish.
		player.GlobalPosition = Nav.Closest( player, new Vector3( -6f, 0f, 10f ) ) + Vector3.Up * 0.1f;
		player.ThirdPerson = true;
		player.EyeAngles = new Angles( 14f, 135f, 0f );
		await Wait( 0.8f );
		await Shot( "sea_wide_a" );
		player.EyeAngles = new Angles( 10f, -40f, 0f );
		await Wait( 0.5f );
		await Shot( "sea_wide_b" );
		player.ThirdPerson = false;
		var school = dressing.GetChildren().OfType<FishSchool>().FirstOrDefault();
		if ( school is not null )
		{
			var fish = school.GlobalTransform * school.Multimesh.GetInstanceTransform( 0 ).Origin;
			player.GlobalPosition = Nav.Closest( player, fish.WithY( 0f ) + new Vector3( 3f, 0f, 3f ) ) + Vector3.Up * 0.1f;
			await Wait( 0.4f );
			fish = school.GlobalTransform * school.Multimesh.GetInstanceTransform( 0 ).Origin;
			LookAt( fish );
			await Wait( 0.2f );
			await Shot( "sea_fish" );
		}
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> Every rifle design: switched to live (as the dev menu does), held, aimed and fired, with pictures. </summary>
	async Task RiflesTest()
	{
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		DevFlags.FreezeEnemies = true;
		Fx.FlashLife = 3f;   // hold the flash still for the picture
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var rifle = inventory.Weapons[0];
		inventory.Select( rifle, force: true );
		player.EyeAngles = new Angles( 2f, 0f, 0f );
		foreach ( var design in RifleModels.All )
		{
			RifleModels.Current = design.Name;
			await Wait( 1.3f );
			var shown = rifle.ViewModel?.ModelName;
			GD.Print( $"[autotest] rifles: {design.Name} shows {shown}, scope {rifle.HasScope}, ammo {rifle.Ammo}" );
			Check( shown == design.Model, $"picking {design.Name} did not bring up its model" );
			await Shot( $"rifle_{design.Name.ToLower()}_held" );
			rifle.ForceAim = true;
			await Wait( 0.7f );
			Check( rifle.HasScope == design.Scope, $"{design.Name}: wrong scope setting" );
			await Shot( $"rifle_{design.Name.ToLower()}_aimed" );
			rifle.ForceAim = false;
			await Wait( 0.5f );
			var before = rifle.Ammo;
			rifle.TestFire();
			await ToSignal( RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw );
			await Shot( $"rifle_{design.Name.ToLower()}_fired" );
			Check( rifle.Ammo == before - 1, $"{design.Name} did not fire" );
			foreach ( var flash in GetTree().Root.FindChildren( "Muzzle Flash", "", true, false ) ) flash.QueueFree();
			await Wait( 0.4f );
		}
		player.ThirdPerson = true;
		player.EyeAngles = new Angles( 8f, 150f, 0f );
		foreach ( var design in RifleModels.All )
		{
			RifleModels.Current = design.Name;
			await Wait( 0.8f );
			await Shot( $"rifle_{design.Name.ToLower()}_third_person" );
		}
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> The Kraken, stage 1: it burrows up, its dome soaks shots and shatters, the brain is the weak point, a tentacle comes off, it dies. </summary>
	async Task KrakenTest()
	{
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		var director = WaveDirector.Current;
		var ground = Nav.Closest( player, new Vector3( -8f, 0f, 12f ) );
		var stand = Nav.Closest( player, ground + new Vector3( 8f, 0f, 9f ) );
		player.GlobalPosition = stand + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		await Wait( 0.5f );
		var kraken = director.SpawnAt( WaveDirector.EnemyKind.Kraken, ground, player.GlobalPosition ) as Kraken;
		Check( kraken is not null, "the Kraken did not spawn" );
		if ( kraken is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		await Wait( 0.3f );
		Check( kraken.State == Kraken.Mode.Burrowing, "the Kraken did not start underground" );
		var buried = kraken.BrainPosition.Y - ground.Y;
		LookAt( ground + Vector3.Up * 1.5f );
		foreach ( var (at, name) in new[] { (0.5f, "kraken_rise_a"), (1.2f, "kraken_rise_b"), (1.2f, "kraken_rise_c"), (1.4f, "kraken_unfurl") } )
		{
			await Wait( at );
			await Shot( name );
		}
		await Wait( 1.6f );
		var up = kraken.BrainPosition.Y - ground.Y;
		GD.Print( $"[autotest] kraken: brain {buried:0.0} m at spawn, {up:0.0} m once up, state {kraken.State}, health {kraken.Health.Current:0}, tentacles {kraken.TentaclesLeft}" );
		Check( buried < 0f && up > 2.5f, "the Kraken did not come up out of the ground" );
		Check( kraken.State == Kraken.Mode.Idle, "the Kraken never finished arriving" );
		LookAt( ground + Vector3.Up * 2f );
		await Wait( 0.3f );
		await Shot( "kraken_idle" );

		// Real shots at the dome: it takes them, the Kraken doesn't.
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var weapon = inventory.Weapons[0];
		inventory.Select( weapon, force: true );
		await Wait( 1.2f );
		var full = kraken.Health.Current;
		LookAt( kraken.BrainPosition ); await Wait( 0.2f );
		for ( var k = 0; k < 4; k++ ) { LookAt( kraken.BrainPosition ); weapon.TestFire(); await Wait( 0.15f ); }
		GD.Print( $"[autotest] kraken: dome at {kraken.HelmetFraction:0.00} after 4 shots, health {kraken.Health.Current:0}/{full:0}" );
		Check( kraken.HelmetFraction < 1f, "shots at the brain did not hit the dome" );
		Check( kraken.Health.Current == full, "the Kraken was hurt through its dome" );
		await Shot( "kraken_dome_hit" );
		kraken.ReceiveShot( 1, 5000f, -kraken.GlobalBasis.Z, kraken.BrainPosition, player.GlobalPosition, null, false );
		Check( !kraken.HelmetOn, "the dome did not shatter" );
		await ToSignal( RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw );
		await Wait( 0.12f );
		await Shot( "kraken_dome_shatter" );
		await Wait( 0.8f );

		// Real shots at the bare brain: triple damage.
		var before = kraken.Health.Current;
		LookAt( kraken.BrainPosition ); await Wait( 0.2f );
		for ( var k = 0; k < 3; k++ ) { LookAt( kraken.BrainPosition ); weapon.TestFire(); await Wait( 0.15f ); }
		var brainDamage = (before - kraken.Health.Current) / 3f;
		before = kraken.Health.Current;
		LookAt( kraken.GlobalPosition + Vector3.Up * 1.6f ); await Wait( 0.2f );
		for ( var k = 0; k < 3; k++ ) { LookAt( kraken.GlobalPosition + Vector3.Up * 1.6f ); weapon.TestFire(); await Wait( 0.15f ); }
		var headDamage = (before - kraken.Health.Current) / 3f;
		GD.Print( $"[autotest] kraken: per shot, brain {brainDamage:0}, head {headDamage:0}" );
		Check( headDamage > 0f && brainDamage > headDamage * 2.5f, "the bare brain is not the weak point" );
		await Shot( "kraken_brain_bare" );

		// A tentacle: real shots land on it, then it comes off.
		before = kraken.Health.Current;
		LookAt( kraken.TentaclePosition( 3 ) ); await Wait( 0.2f );
		for ( var k = 0; k < 3; k++ ) { LookAt( kraken.TentaclePosition( 3 ) ); weapon.TestFire(); await Wait( 0.15f ); }
		GD.Print( $"[autotest] kraken: 3 shots at a tentacle took {before - kraken.Health.Current:0}" );
		Check( kraken.Health.Current < before, "shots at a tentacle missed" );
		// Shot through half way along: the far half drops, the near half stays. Then another one off at the root.
		var cutSpot = kraken.TentaclePosition( 3, 4 );
		var backTo = player.GlobalPosition;
		player.GlobalPosition = Nav.Closest( player, cutSpot + (cutSpot - kraken.GlobalPosition).Flat().Normalized() * 4.5f ) + Vector3.Up * 0.1f;
		await Wait( 0.4f );
		LookAt( cutSpot );
		await Wait( 0.2f );
		kraken.ReceiveShot( Kraken.SegIndex( 3, 4 ), 1000f, Vector3.Right, kraken.TentaclePosition( 3, 4 ), player.GlobalPosition, null, false );
		GD.Print( $"[autotest] kraken: cut at segment 4, {kraken.SegmentsLeft( 3 )} segments left on it, {kraken.TentaclesLeft} tentacles" );
		Check( kraken.SegmentsLeft( 3 ) == 4 && kraken.TentaclesLeft == Kraken.Tentacles, "the tentacle was not cut at the segment that was shot" );
		await Wait( 0.35f );
		await Shot( "kraken_tentacle_cut" );
		kraken.ReceiveShot( Kraken.SegIndex( 4, 0 ), 1000f, Vector3.Right, kraken.TentaclePosition( 4, 0 ), player.GlobalPosition, null, false );
		Check( kraken.TentaclesLeft == Kraken.Tentacles - 1, "the tentacle did not come off at the root" );
		await Wait( 2.2f );
		LookAt( cutSpot + Vector3.Down * 0.3f );
		await Wait( 0.1f );
		await Shot( "kraken_tentacle_flop" );
		player.GlobalPosition = backTo;
		await Wait( 0.4f );
		var loose = GetTree().Root.FindChildren( "Kraken Piece", "", true, false ).Count;
		GD.Print( $"[autotest] kraken: {loose} loose tentacle pieces" );
		Check( loose == 12, "the cut tentacles did not come loose as one piece per segment" );
		before = kraken.Health.Current;
		for ( var k = 0; k < 2; k++ ) { LookAt( kraken.TentaclePosition( 3, 1 ) ); await Wait( 0.2f ); weapon.TestFire(); await Wait( 0.15f ); }
		Check( kraken.Health.Current < before, "the stump of a cut tentacle cannot be shot" );

		// From the side and close up, then the death.
		player.GlobalPosition = Nav.Closest( player, ground + kraken.GlobalBasis * new Vector3( 1.5f, 0f, -5.5f ) ) + Vector3.Up * 0.1f;
		await Wait( 0.4f );
		LookAt( kraken.GlobalPosition + Vector3.Up * 1.7f );
		await Wait( 0.2f );
		await Shot( "kraken_face" );
		kraken.ReceiveShot( 0, 100000f, (kraken.GlobalPosition - player.GlobalPosition).Normalized(), kraken.GlobalPosition + Vector3.Up * 2f, player.GlobalPosition, null, false );
		Check( kraken.IsDead, "the Kraken did not die" );
		await Wait( 0.9f );
		await Shot( "kraken_death" );
		await Wait( 1.2f );
		await Shot( "kraken_sinking" );
		await Wait( 2f );
		Check( Kraken.All.Count == 0, "the dead Kraken was not cleared away" );
		await Shot( "kraken_remains" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> The Kraken's whirlpool: a warning outside its ring, cancelled by going back in, and the ride back when it runs out. </summary>
	async Task WhirlpoolTest()
	{
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		var ground = Nav.Closest( player, new Vector3( -8f, 0f, 12f ) );
		player.GlobalPosition = Nav.Closest( player, ground + new Vector3( 7f, 0f, 7f ) ) + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		await Wait( 0.5f );
		var kraken = WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Kraken, ground, player.GlobalPosition ) as Kraken;
		Check( kraken is not null, "the Kraken did not spawn" );
		if ( kraken is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		await Wait( 5.6f );
		Check( kraken.State == Kraken.Mode.Idle, "the Kraken never finished arriving" );
		Check( Kraken.WhirlpoolWarning( survivor ) is null, "a whirlpool warning showed inside the ring" );
		var ring = GetTree().Root.FindChild( "Kraken Ring", true, false );
		GD.Print( $"[autotest] whirlpool: ring markers {ring?.GetChildCount()}, radius {Kraken.FightRadius} m" );
		Check( ring is not null && ring.GetChildCount() > 12, "the fight ring was not laid out" );

		// Outside the ring: the countdown starts. Find a spot on the navmesh that is really outside.
		var inside = player.GlobalPosition;
		var outside = inside;
		foreach ( var angle in new[] { 0f, 90f, 180f, 270f, 45f, 135f, 225f, 315f } )
		{
			var dir = new Vector3( MathF.Sin( MathX.Rad( angle ) ), 0f, MathF.Cos( MathX.Rad( angle ) ) );
			var spot = Nav.Closest( player, ground + dir * (Kraken.FightRadius + 7f) );
			if ( (spot - ground).Flat().Length() > Kraken.FightRadius + 3f ) { outside = spot + Vector3.Up * 0.1f; break; }
		}
		Check( outside != inside, "found nowhere outside the ring to stand" );
		player.GlobalPosition = outside;
		await Wait( 1.5f );
		var warning = Kraken.WhirlpoolWarning( survivor );
		GD.Print( $"[autotest] whirlpool: {(outside - ground).Flat().Length():0.0} m out, warning {warning:0.0} s left (expect about 3.5)" );
		Check( warning is > 2.5f and < 4.5f, "no 5 second warning outside the ring" );
		LookAt( ground + Vector3.Up * 2f );
		await Wait( 0.2f );
		await Shot( "whirlpool_warning" );
		player.ThirdPerson = true;
		await Wait( 0.3f );
		await Shot( "whirlpool_warning_third_person" );
		player.ThirdPerson = false;
		var funnel = GetTree().Root.FindChild( "Kraken Whirlpool", true, false ) as Node3D;
		GD.Print( $"[autotest] whirlpool: funnel {funnel is not null}, visible {funnel?.Visible}, {funnel?.GlobalPosition.DistanceTo( player.GlobalPosition ):0.0} m from the player, scale {funnel?.Scale}" );
		Check( funnel is { Visible: true }, "no whirlpool gathers round the player during the warning" );

		// Back inside: it dies away.
		player.GlobalPosition = inside;
		await Wait( 2.5f );
		Check( Kraken.WhirlpoolWarning( survivor ) is null, "the warning did not clear after going back inside" );
		Check( !player.Scripted, "the player was taken although they went back in time" );

		// Outside and staying there: taken after 5 seconds and put down inside the ring.
		player.GlobalPosition = outside;
		await Wait( 5.4f );
		Check( Kraken.Carrying( survivor ) && player.Scripted, "the whirlpool did not take the player after the warning" );
		await Wait( 0.9f );
		await Shot( "whirlpool_ride" );
		player.ThirdPerson = true;
		await Wait( 0.3f );
		await Shot( "whirlpool_ride_third_person" );
		player.ThirdPerson = false;
		await Wait( 2.2f );
		var away = (player.GlobalPosition - ground).Flat().Length();
		GD.Print( $"[autotest] whirlpool: put down {away:0.0} m from the Kraken, carried {Kraken.Carrying( survivor )}, scripted {player.Scripted}, on ground {player.IsOnGround}" );
		Check( !Kraken.Carrying( survivor ) && !player.Scripted, "the whirlpool never let go" );
		Check( away < Kraken.FightRadius, "the whirlpool did not bring the player back inside the ring" );
		Check( Kraken.WhirlpoolWarning( survivor ) is null, "a warning was still showing after the ride" );
		await Wait( 0.6f );
		Check( player.IsOnGround, "the player was not put down on the sea bed" );

		// Killed mid-ride: it must let go.
		player.GlobalPosition = outside;
		await Wait( 5.6f );
		Check( Kraken.Carrying( survivor ), "the second whirlpool did not start" );
		kraken.ReceiveShot( 0, 100000f, Vector3.Forward, kraken.GlobalPosition + Vector3.Up * 2f, player.GlobalPosition, null, false );
		await Wait( 0.3f );
		Check( !player.Scripted && Kraken.WhirlpoolWarning( survivor ) is null, "the dead Kraken kept hold of the player" );
		await Wait( 4f );
		Check( GetTree().Root.FindChild( "Kraken Ring", true, false ) is null, "the ring stayed after the Kraken was gone" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	static bool restartedOnce;

	async Task PauseTest()
	{
		if ( restartedOnce )
		{
			// Second pass, after Restart reloaded the level: everything must be back and running.
			await Wait( 1.5f );
			var d = WaveDirector.Current;
			GD.Print( $"[autotest] pause: after Restart player {player is not null}, paused {GetTree().Paused}, menu open {PauseMenu.IsOpen}, wave {d?.Wave}, on dock {DockHouse.HoldWaves}, players {PlayerSurvivor.All.Count}, creatures {Creature.Everyone.Count( c => c.IsEnemy )}" );
			Check( !GetTree().Paused && !PauseMenu.IsOpen, "the game was still paused after Restart" );
			Check( PlayerSurvivor.All.Count == 1, "Restart left the wrong number of players" );
			Check( d is not null && d.Wave == 0 && DockHouse.HoldWaves, "Restart did not put the run back at the start" );
			GD.Print( "[autotest] done" );
			GetTree().Quit();
			return;
		}
		void Key( Godot.Key key, bool down ) => Input.ParseInputEvent( new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = down } );
		void Click( Vector2 at )
		{
			GetViewport().PushInput( new InputEventMouseMotion { Position = at, GlobalPosition = at }, true );
			GetViewport().PushInput( new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = true }, true );
			GetViewport().PushInput( new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = false }, true );
		}
		await Wait( 1f );
		var menu = GetTree().Root.FindChild( "PauseMenu", true, false ) as PauseMenu;
		Check( menu is not null, "there is no pause menu" );
		if ( menu is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		var before = Clock.Now;
		Key( Godot.Key.Escape, true ); Key( Godot.Key.Escape, false );
		await Wait( 0.5f );
		var stopped = Clock.Now;
		await Wait( 1f );
		GD.Print( $"[autotest] pause: open {PauseMenu.IsOpen}, tree paused {GetTree().Paused}, mouse {Input.MouseMode}, game clock moved {Clock.Now - stopped:0.000} s in 1 s (expect 0)" );
		Check( PauseMenu.IsOpen && GetTree().Paused, "Esc did not pause the game" );
		Check( Clock.Now - stopped < 0.01, "game time kept running while paused" );
		await Shot( "pause_menu" );

		// Drag-free check of a setting: click near the right end of the field of view slider.
		var slider = menu.FindChild( "Field of view", true, false )?.FindChild( "Slider", true, false ) as HSlider;
		var fovBefore = GameSettings.Fov;
		if ( slider is not null ) Click( slider.GetGlobalRect().Position + new Vector2( slider.GetGlobalRect().Size.X * 0.9f, slider.GetGlobalRect().Size.Y * 0.5f ) );
		await Wait( 0.3f );
		GD.Print( $"[autotest] pause: field of view {fovBefore:0} -> {GameSettings.Fov:0}, player {player.FieldOfView:0}" );
		Check( GameSettings.Fov > fovBefore + 5f && MathF.Abs( player.FieldOfView - GameSettings.Fov ) < 0.5f, "the field of view slider did not change the setting" );
		GameSettings.Fov = fovBefore;
		GameSettings.Apply();

		var resume = menu.FindChild( "Resume", true, false ) as Button;
		Click( resume.GetGlobalRect().GetCenter() );
		await Wait( 0.6f );
		GD.Print( $"[autotest] pause: after Resume open {PauseMenu.IsOpen}, tree paused {GetTree().Paused}, clock running {Clock.Now > stopped + 0.3}" );
		Check( !PauseMenu.IsOpen && !GetTree().Paused, "Resume did not unpause" );
		Check( Clock.Now > stopped + 0.3, "game time did not carry on after Resume" );

		// Esc closes it too.
		Key( Godot.Key.Escape, true ); Key( Godot.Key.Escape, false );
		await Wait( 0.3f );
		Key( Godot.Key.Escape, true ); Key( Godot.Key.Escape, false );
		await Wait( 0.3f );
		Check( !PauseMenu.IsOpen && !GetTree().Paused, "Esc twice did not end up unpaused" );

		// The dev menu is reached from here now (no key of its own): its button opens it with the game still paused.
		menu.SetOpen( true );
		await Wait( 0.3f );
		var devButton = menu.FindChild( "Dev menu", true, false ) as Button;
		Check( devButton is not null, "the pause menu has no Dev menu button" );
		if ( devButton is not null ) Click( devButton.GetGlobalRect().GetCenter() );
		await Wait( 0.4f );
		GD.Print( $"[autotest] pause: dev menu open {DevMenu.IsOpen}, pause open {PauseMenu.IsOpen}, tree paused {GetTree().Paused}, mouse {Input.MouseMode}" );
		Check( DevMenu.IsOpen && !PauseMenu.IsOpen, "the Dev menu button did not open the dev menu" );
		var clockAtDev = Clock.Now;
		await Wait( 0.6f );
		GD.Print( $"[autotest] pause: with the dev menu up the game clock moved {Clock.Now - clockAtDev:0.000} s in 0.6 s (expect 0)" );
		Check( GetTree().Paused && Clock.Now - clockAtDev < 0.01f, "the game ran on behind the dev menu" );
		await Shot( "dev_menu_from_pause" );
		Key( Godot.Key.P, true ); Key( Godot.Key.P, false );
		await Wait( 0.3f );
		Check( DevMenu.IsOpen, "P still toggles the dev menu" );
		Key( Godot.Key.Escape, true ); Key( Godot.Key.Escape, false );
		await Wait( 0.3f );
		Check( !DevMenu.IsOpen && !PauseMenu.IsOpen && !GetTree().Paused, "Esc did not close the dev menu back to the game" );
		Key( Godot.Key.P, true ); Key( Godot.Key.P, false );
		await Wait( 0.3f );
		Check( !DevMenu.IsOpen, "P opened the dev menu" );

		// Restart reloads the level: this test node goes with it and a new one picks up above.
		restartedOnce = true;
		menu.SetOpen( true );
		await Wait( 0.3f );
		Click( (menu.FindChild( "Restart", true, false ) as Button).GetGlobalRect().GetCenter() );
	}

	async Task Shot( string name )
	{
		if ( string.IsNullOrEmpty( shots ) ) return;
		await ToSignal( RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw );
		var image = GetViewport().GetTexture().GetImage();
		var path = System.IO.Path.Combine( shots, name + ".png" );
		image.SavePng( path );
		GD.Print( $"[autotest] shot {path}" );
	}
}
