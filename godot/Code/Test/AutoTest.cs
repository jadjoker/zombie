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
		if ( args.Contains( "--slam" ) ) { await SlamTest(); return; }
		if ( args.Contains( "--bite" ) ) { await BiteTest(); return; }
		if ( args.Contains( "--shark" ) ) { await SharkTest(); return; }
		if ( args.Contains( "--board1" ) ) { await Board1Test(); return; }
		if ( args.Contains( "--round11" ) ) { await Round11Test(); return; }
		if ( args.Contains( "--pad" ) ) { await PadTest(); return; }
		if ( args.Contains( "--radio" ) ) { await RadioTest(); return; }
		if ( args.Contains( "--terrain" ) ) { await TerrainTest(); return; }
		if ( args.Contains( "--padmenus" ) ) { await PadMenusTest(); return; }
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
			// Spawn does not spawn: it closes the menu and waits for a click on the spot.
			GD.Print( $"[autotest] devclick: after Spawn menu open {DevMenu.IsOpen}, placing {DevFlags.Placing}, paused {GetTree().Paused}, Krakens {Kraken.All.Count}" );
			Check( !DevMenu.IsOpen && DevFlags.Placing && !GetTree().Paused, "Spawn did not close the menu ready to place" );
			Check( Kraken.All.Count == 0, "Spawn made a Kraken before any click on the sea bed" );
			var spot = Nav.Closest( player, player.GlobalPosition + new Vector3( 10f, 0f, 10f ) );
			LookAt( spot );
			await Wait( 0.4f );
			var middle = GetViewport().GetVisibleRect().Size * 0.5f;
			Input.ParseInputEvent( new InputEventMouseButton { Position = middle, GlobalPosition = middle, ButtonIndex = MouseButton.Left, Pressed = true } );
			await Wait( 0.1f );
			Input.ParseInputEvent( new InputEventMouseButton { Position = middle, GlobalPosition = middle, ButtonIndex = MouseButton.Left, Pressed = false } );
			await Wait( 0.4f );
			var kraken = Kraken.All.FirstOrDefault();
			GD.Print( $"[autotest] devclick: after the click Krakens {Kraken.All.Count}, {(kraken is null ? 0f : (kraken.GlobalPosition - spot).Flat().Length()):0.0} m from where the crosshair pointed, still placing {DevFlags.Placing}" );
			Check( kraken is not null, "clicking on the sea bed did not place the Kraken" );
			// Something may stand between the player and that spot: what matters is that it is on the line of sight.
			var offLine = kraken is null ? 0f : MathX.Deg( (kraken.GlobalPosition - player.Camera.GlobalPosition).AngleTo( -player.Camera.GlobalBasis.Z ) );
			GD.Print( $"[autotest] devclick: the Kraken is {offLine:0.0} degrees off the crosshair" );
			Check( offLine < 6f, "the Kraken was not placed where the crosshair pointed" );
			Check( !DevFlags.Placing, "placing carried on after the one click" );
			menu.SetOpen( false );
			// It takes about 5 seconds to arrive; allow a slow machine twice that.
			for ( var waited = 0f; waited < 10f && kraken is not null && kraken.State != Kraken.Mode.Idle; waited += 0.5f ) await Wait( 0.5f );
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
		DevFlags.FreezeEnemies = true;   // its body and damage only: no attacks to move the player about
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		var director = WaveDirector.Current;
		var ground = Nav.Closest( player, new Vector3( -8f, 0f, 12f ) );
		var stand = Nav.Closest( player, ground + new Vector3( 13f, 0f, 14f ) );
		player.GlobalPosition = stand + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		await Wait( 0.5f );
		var kraken = director.SpawnAt( WaveDirector.EnemyKind.Kraken, ground, player.GlobalPosition ) as Kraken;
		Check( kraken is not null, "the Kraken did not spawn" );
		if ( kraken is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		await Wait( 0.3f );
		Check( kraken.State == Kraken.Mode.Burrowing, "the Kraken did not start underground" );
		var buried = kraken.BrainPosition.Y - ground.Y;
		LookAt( ground + Vector3.Up * 3f );
		foreach ( var (at, name) in new[] { (0.5f, "kraken_rise_a"), (1.2f, "kraken_rise_b"), (1.2f, "kraken_rise_c"), (1.4f, "kraken_unfurl") } )
		{
			await Wait( at );
			await Shot( name );
		}
		for ( var waited = 0f; waited < 12f && kraken.State != Kraken.Mode.Idle; waited += 0.5f ) await Wait( 0.5f );
		var up = kraken.BrainPosition.Y - ground.Y;
		GD.Print( $"[autotest] kraken: brain {buried:0.0} m at spawn, {up:0.0} m once up, state {kraken.State}, health {kraken.Health.Current:0}, tentacles {kraken.TentaclesLeft}" );
		Check( buried < 0f && up > 5f, "the Kraken did not come up out of the ground" );
		Check( kraken.State == Kraken.Mode.Idle, "the Kraken never finished arriving" );
		LookAt( ground + Vector3.Up * 4f );
		await Wait( 0.3f );
		await Shot( "kraken_idle" );

		// Playtest round 1: the tentacles stay out of the sand, and the sand mound does not turn with the Kraken.
		var lowest = float.MaxValue;
		var moundTurn = kraken.Mound.GlobalBasis.GetEuler().Y;
		kraken.GlobalBasis = kraken.GlobalBasis.Rotated( Vector3.Up, 0.9f );
		for ( var i = 0; i < 30; i++ ) { await Wait( 0.1f ); lowest = MathF.Min( lowest, kraken.LowestTentacle() ); }
		GD.Print( $"[autotest] kraken: lowest tentacle surface over 3 s {lowest:0.00} m above the sea bed, mound turned {MathX.Deg( kraken.Mound.GlobalBasis.GetEuler().Y - moundTurn ):0.0} deg while the Kraken turned 52" );
		Check( lowest > -0.06f, "a tentacle dipped under the sea bed" );
		Check( MathF.Abs( kraken.Mound.GlobalBasis.GetEuler().Y - moundTurn ) < 0.01f, "the sand mound turned with the Kraken" );
		kraken.GlobalBasis = kraken.GlobalBasis.Rotated( Vector3.Up, -0.9f );
		await Wait( 2f );

		// Real shots at the dome: it takes them, the Kraken doesn't.
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var weapon = inventory.Weapons[0];
		inventory.Select( weapon, force: true );
		await Wait( 1.2f );
		var full = kraken.Health.Current;
		LookAt( kraken.BrainPosition ); await Wait( 0.2f );
		// The dome is flush with the head: a shot at the edge of the head's top half must land on glass.
		for ( var k = 0; k < 4; k++ ) { LookAt( kraken.BrainPosition + (k % 2 == 0 ? Vector3.Zero : player.Camera.GlobalBasis.X * 1.7f) ); await Wait( 0.06f ); weapon.TestFire(); await Wait( 0.15f ); }
		Check( kraken.PanesLeft < 32 && kraken.PanesLeft > 20, "shots on the dome did not knock a few panes out" );
		GD.Print( $"[autotest] kraken: {kraken.PanesLeft} of 32 panes still on the dome; dome at {kraken.HelmetFraction:0.00} after 4 shots, health {kraken.Health.Current:0}/{full:0}" );
		Check( kraken.HelmetFraction < 1f, "shots at the brain did not hit the dome" );
		Check( kraken.Health.Current == full, "the Kraken was hurt through its dome" );
		await Shot( "kraken_dome_hit" );
		kraken.ReceiveShot( 1, 5000f, -kraken.GlobalBasis.Z, kraken.BrainPosition, player.GlobalPosition, null, false );
		Check( !kraken.HelmetOn, "the dome did not shatter" );
		var shards = GetTree().GetNodesInGroup( "kraken_glass" );
		GD.Print( $"[autotest] kraken: the dome broke into {shards.Count} loose shards, {kraken.PanesLeft} panes left on it" );
		Check( shards.Count >= 24 && kraken.PanesLeft == 0, "the dome did not come apart as loose glass shards" );
		await ToSignal( RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw );
		await Wait( 0.12f );
		await Shot( "kraken_dome_shatter" );
		await Wait( 0.8f );

		// Real shots at the bare brain: triple damage.
		var before = kraken.Health.Current;
		LookAt( kraken.BrainPosition ); await Wait( 0.2f );
		for ( var k = 0; k < 3; k++ ) { LookAt( kraken.BrainPosition ); await Wait( 0.06f ); weapon.TestFire(); await Wait( 0.15f ); }
		var brainDamage = (before - kraken.Health.Current) / 3f;
		var lurch = 0f;
		before = kraken.Health.Current;
		LookAt( kraken.GlobalPosition + Vector3.Up * 3.6f ); await Wait( 0.2f );
		for ( var k = 0; k < 3; k++ ) { LookAt( kraken.GlobalPosition + Vector3.Up * 3.6f ); await Wait( 0.06f ); weapon.TestFire(); for ( var i = 0; i < 3; i++ ) { await Wait( 0.05f ); lurch = MathF.Max( lurch, kraken.HeadLean ); } }
		var headDamage = (before - kraken.Health.Current) / 3f;
		GD.Print( $"[autotest] kraken: {kraken.BleedingHoles} bullet holes spurting, {kraken.Craters} pieces of hide torn off so far" );
		Check( kraken.BleedingHoles >= 2, "shots did not leave bleeding bullet holes" );
		GD.Print( $"[autotest] kraken: per shot, brain {brainDamage:0}, head {headDamage:0}; head knocked up to {lurch:0.0} deg off by the shots" );
		Check( lurch > 1f, "the head did not lurch when shot" );
		Check( headDamage > 0f && brainDamage > headDamage * 2.5f, "the bare brain is not the weak point" );
		await Shot( "kraken_brain_bare" );

		// A tentacle: real shots land on it, then it comes off.
		before = kraken.Health.Current;
		LookAt( kraken.TentaclePosition( 3 ) ); await Wait( 0.2f );
		for ( var k = 0; k < 3; k++ ) { LookAt( kraken.TentaclePosition( 3 ) ); await Wait( 0.06f ); weapon.TestFire(); await Wait( 0.15f ); }
		GD.Print( $"[autotest] kraken: 3 shots at a tentacle took {before - kraken.Health.Current:0} and set it wiggling {kraken.Wiggle( 3 ):0.0} deg" );
		Check( kraken.Wiggle( 3 ) > 1f, "a shot tentacle did not whip about" );
		Check( kraken.Health.Current < before, "shots at a tentacle missed" );
		// Shot through half way along: the far half drops, the near half stays. Then another one off at the root.
		var cutSpot = kraken.TentaclePosition( 3, 4 );
		var backTo = player.GlobalPosition;
		player.GlobalPosition = Nav.Closest( player, cutSpot + (cutSpot - kraken.GlobalPosition).Flat().Normalized() * 7f ) + Vector3.Up * 0.1f;
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
		for ( var k = 0; k < 2; k++ ) { LookAt( kraken.TentaclePosition( 3, 1 ) ); await Wait( 0.2f ); await Wait( 0.06f ); weapon.TestFire(); await Wait( 0.15f ); }
		Check( kraken.Health.Current < before, "the stump of a cut tentacle cannot be shot" );

		// Low and close in front, looking up under the body.
		player.GlobalPosition = Nav.Closest( player, ground + kraken.GlobalBasis * new Vector3( 0.5f, 0f, -6.5f ) ) + Vector3.Up * 0.1f;
		await Wait( 0.4f );
		LookAt( kraken.GlobalPosition + Vector3.Up * 1.6f );
		await Wait( 0.2f );
		await Shot( "kraken_under" );

		// Hurt badly: pieces of hide tear off and leave craters.
		var tornBefore = kraken.Craters;
		kraken.ReceiveShot( 0, kraken.Health.Current * 0.55f, (kraken.GlobalPosition - player.GlobalPosition).Normalized(), kraken.GlobalPosition + Vector3.Up * 3.6f - kraken.GlobalBasis.Z * 1.6f, player.GlobalPosition, null, false );
		GD.Print( $"[autotest] kraken: down to {kraken.Health.Fraction:0.00} of its health, {kraken.Craters - tornBefore} more pieces torn off ({kraken.Craters} in all)" );
		Check( kraken.Craters - tornBefore >= 2 && !kraken.IsDead, "losing half its health did not tear hide off its head" );

		// From the side and close up, then the death.
		player.GlobalPosition = Nav.Closest( player, ground + kraken.GlobalBasis * new Vector3( 3f, 0f, -13f ) ) + Vector3.Up * 0.1f;
		await Wait( 0.4f );
		LookAt( kraken.GlobalPosition + Vector3.Up * 3.8f );
		await Wait( 0.2f );
		await Shot( "kraken_face" );
		kraken.ReceiveShot( 0, 100000f, (kraken.GlobalPosition - player.GlobalPosition).Normalized(), kraken.GlobalPosition + Vector3.Up * 2f, player.GlobalPosition, null, false );
		Check( kraken.IsDead, "the Kraken did not die" );
		await Wait( 0.4f );
		await Shot( "kraken_swelling" );
		await Wait( 0.75f );
		await Shot( "kraken_burst" );
		GD.Print( $"[autotest] kraken: burst into {Kraken.LastBurstPieces} loose pieces (chunks, teeth, guts, lumps) and {GetTree().Root.FindChildren( "Kraken Piece*", "", true, false ).Count} tentacle pieces" );
		Check( Kraken.LastBurstPieces >= 100, "the Kraken did not burst into a lot of pieces" );
		await Wait( 1.2f );
		await Shot( "kraken_chunks" );
		Check( Engine.TimeScale > 0.99, "the slow motion after the burst never ended" );
		await Wait( 4.5f );
		Check( Kraken.All.Count == 0, "the dead Kraken was not cleared away" );
		await Shot( "kraken_remains" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> The Kraken's whirlpool: a warning outside its ring, cancelled by going back in, and the ride back when it runs out. </summary>
	async Task WhirlpoolTest()
	{
		DevFlags.FreezeEnemies = true;   // the whirlpool only: no slams
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
		for ( var waited = 0f; waited < 12f && kraken.State != Kraken.Mode.Idle; waited += 0.5f ) await Wait( 0.5f );
		await Wait( 0.3f );
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
		await Wait( 7f );
		Check( GetTree().Root.FindChild( "Kraken Ring", true, false ) is null, "the ring stayed after the Kraken was gone" );
		Check( GetTree().Root.FindChild( "Kraken Mound", true, false ) is null, "the sand mound stayed after the Kraken was gone" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> The Kraken's tentacle slam: the pull, a hit standing still, a miss stepping aside, the swat in the air, and none with no tentacles. </summary>
	async Task SlamTest()
	{
		DevFlags.FreezeEnemies = true;   // slams only when the test asks for one
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		DockHouse.Current?.SkipDive();
		var ground = Nav.Closest( player, new Vector3( -8f, 0f, 12f ) );
		var stand = Nav.Closest( player, ground + new Vector3( 11f, 0f, 11f ) ) + Vector3.Up * 0.1f;
		player.GlobalPosition = stand;
		player.Velocity = Vector3.Zero;
		await Wait( 0.5f );
		var kraken = WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Kraken, ground, player.GlobalPosition ) as Kraken;
		Check( kraken is not null, "the Kraken did not spawn" );
		if ( kraken is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		for ( var waited = 0f; waited < 10f && kraken.State != Kraken.Mode.Idle; waited += 0.5f ) await Wait( 0.5f );
		LookAt( ground + Vector3.Up * 2f );
		survivor.Health.ResetTo( 1000f );

		// 1. Standing still: pulled in during the suck, then hit.
		var startDistance = (player.GlobalPosition - ground).Flat().Length();
		var health = survivor.Health.Current;
		Check( kraken.MouthOpen < 0.02f, "the mouth was showing before the suck" );
		kraken.ForceSlam();
		Check( kraken.Attacking == Kraken.Attack.Suck, "the slam did not start with the suck" );
		await Wait( 0.6f );
		GD.Print( $"[autotest] slam: mouth open {kraken.MouthOpen:0.00} during the suck" );
		Check( kraken.MouthOpen > 0.9f, "the mouth did not open for the suck" );
		await Shot( "slam_suck" );
		await Wait( 0.5f );
		var pulled = startDistance - (player.GlobalPosition - ground).Flat().Length();
		GD.Print( $"[autotest] slam: pulled {pulled:0.0} m toward the mouth in the suck (from {startDistance:0.0} m)" );
		Check( pulled > 1.5f, "the suck did not pull the player in" );
		Check( kraken.Attacking == Kraken.Attack.Raise, "the tentacles did not rear up after the suck" );
		await Wait( 0.45f );
		LookAt( ground + Vector3.Up * 7f );
		await Wait( 0.1f );
		await Shot( "slam_reared" );
		await Wait( 0.27f );
		await Shot( "slam_locked" );
		await Wait( 0.7f );
		await Shot( "slam_impact" );
		GD.Print( $"[autotest] slam: standing still lost {health - survivor.Health.Current:0} health, hits {kraken.LastSlamHits}, state {kraken.Attacking}" );
		Check( MathF.Abs( health - survivor.Health.Current - kraken.SlamDamage ) < 6f, "standing still under the slam did not take its damage once" );
		for ( var waited = 0f; waited < 4f && kraken.Attacking != Kraken.Attack.None; waited += 0.25f ) await Wait( 0.25f );
		Check( kraken.Attacking == Kraken.Attack.None, "the slam never finished" );
		Check( kraken.MouthOpen < 0.02f, "the mouth stayed out after the suck" );

		// 2. Stepping aside once the lines lock: missed.
		player.GlobalPosition = stand;
		player.Velocity = Vector3.Zero;
		await Wait( 0.4f );
		health = survivor.Health.Current;
		kraken.ForceSlam();
		await Wait( kraken.SuckTime + kraken.RearTime - kraken.LockBefore + 0.06f );
		var side = (player.GlobalPosition - ground).Flat().Normalized().Cross( Vector3.Up );
		player.GlobalPosition = Nav.Closest( player, player.GlobalPosition + side * 6f ) + Vector3.Up * 0.1f;
		await Wait( 1.1f );
		GD.Print( $"[autotest] slam: dodged sideways lost {health - survivor.Health.Current:0} health, hits {kraken.LastSlamHits}" );
		Check( health <= survivor.Health.Current && kraken.LastSlamHits == 0, "stepping aside after the lines locked did not dodge the slam" );
		for ( var waited = 0f; waited < 4f && kraken.Attacking != Kraken.Attack.None; waited += 0.25f ) await Wait( 0.25f );

		// 3. High above the sea bed: swatted down for less.
		player.GlobalPosition = stand;
		player.Velocity = Vector3.Zero;
		await Wait( 0.4f );
		health = survivor.Health.Current;
		kraken.ForceSlam();
		await Wait( kraken.SuckTime + 0.2f );
		var gravity = player.GravityScale;
		for ( var held = 0f; kraken.Attacking is Kraken.Attack.Raise or Kraken.Attack.Slam; held += 0.02f )
		{
			// Held up in the water until the tentacles come down.
			player.GlobalPosition = player.GlobalPosition.WithY( ground.Y + 9f );
			player.Velocity = Vector3.Zero;
			await ToSignal( GetTree(), SceneTree.SignalName.PhysicsFrame );
		}
		var falling = player.Velocity.Y;
		await Wait( 0.1f );
		GD.Print( $"[autotest] slam: in the air lost {health - survivor.Health.Current:0} health, swats {kraken.LastSlamSwats}, falling at {falling:0.0} m/s" );
		Check( kraken.LastSlamSwats == 1 && MathF.Abs( health - survivor.Health.Current - kraken.SwatDamage ) < 6f, "a player high up was not swatted for the smaller damage" );
		Check( falling < -8f, "the swat did not knock the player back down" );
		await Shot( "slam_swat" );
		for ( var waited = 0f; waited < 4f && kraken.Attacking != Kraken.Attack.None; waited += 0.25f ) await Wait( 0.25f );

		// 3b. Playtest round 2: every tentacle of a slam shot off while it is reared. It must carry on, not seize up.
		player.GlobalPosition = stand;
		player.Velocity = Vector3.Zero;
		await Wait( 0.4f );
		kraken.Health.Invulnerable = true;   // the cuts below would otherwise kill it, which ends the slam another way
		kraken.ForceSlam();
		await Wait( kraken.SuckTime + 0.5f );
		Check( kraken.Attacking == Kraken.Attack.Raise, "the slam was not rearing when the test cut its tentacles" );
		foreach ( var k in new[] { 0, 7, 1 } )
			kraken.ReceiveShot( Kraken.SegIndex( k, 0 ), 5000f, Vector3.Right, kraken.GlobalPosition, player.GlobalPosition, null, false );
		var clock = kraken.Age;
		await Wait( 2.5f );
		GD.Print( $"[autotest] slam: after losing its striking tentacles the Kraken ran on {kraken.Age - clock:0.0} s of 2.5, attack {kraken.Attacking}, tentacles {kraken.TentaclesLeft}" );
		Check( kraken.Age - clock > 2f, "the Kraken seized up after a striking tentacle was shot off" );
		Check( kraken.Attacking == Kraken.Attack.None, "the slam did not end after its tentacles were shot off" );
		Check( !kraken.IsDead && kraken.TentaclesLeft == Kraken.Tentacles - 3, "the test did not leave the Kraken alive with five tentacles" );

		// 4. A short tentacle reaches less far; with none left it does not slam at all.
		for ( var k = 0; k < Kraken.Tentacles; k++ )
			kraken.ReceiveShot( Kraken.SegIndex( k, 0 ), 5000f, Vector3.Right, kraken.GlobalPosition, player.GlobalPosition, null, false );
		Check( kraken.TentaclesLeft == 0, "could not shoot every tentacle off" );
		player.GlobalPosition = stand;
		await Wait( 0.4f );
		health = survivor.Health.Current;
		kraken.ForceSlam();
		await Wait( kraken.SuckTime + kraken.RearTime + 0.6f );
		Check( health <= survivor.Health.Current, "a Kraken with no tentacles still slammed" );
		Check( kraken.Attacking == Kraken.Attack.None, "a Kraken with no tentacles got stuck in its attack" );

		// 5. Left alone (not frozen) it attacks on its own.
		kraken.Health.Current = kraken.Health.MaxHealth;
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> The Kraken's mouth attacks: the bite at close range with its radiation, and the ink spit, hit and dodged. </summary>
	async Task BiteTest()
	{
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		var afflictions = survivor.Afflictions;
		DockHouse.Current?.SkipDive();
		var ground = Nav.Closest( player, new Vector3( -8f, 0f, 12f ) );
		var stand = Nav.Closest( player, ground + new Vector3( 11f, 0f, 11f ) ) + Vector3.Up * 0.1f;
		player.GlobalPosition = stand;
		player.Velocity = Vector3.Zero;
		await Wait( 0.5f );
		DevFlags.FreezeEnemies = true;
		var kraken = WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Kraken, ground, player.GlobalPosition ) as Kraken;
		Check( kraken is not null, "the Kraken did not spawn" );
		if ( kraken is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		for ( var waited = 0f; waited < 12f && kraken.State != Kraken.Mode.Idle; waited += 0.5f ) await Wait( 0.5f );
		// Awake, but with no slam or ink of its own, so only what the test provokes happens.
		kraken.HoldAttacks( 9999f );
		DevFlags.FreezeEnemies = false;
		survivor.Health.ResetTo( 1000f );
		kraken.BiteRadiationChance = 1f;

		// 1. Out of reach of the mouth: no bite.
		await Wait( 1.5f );
		Check( kraken.Bites == 0, "the Kraken bit a player who was nowhere near its mouth" );

		// 2. Right at its mouth: bitten, thrown back, irradiated and poisoned.
		var health = survivor.Health.Current;
		var forward = -kraken.GlobalBasis.Z;
		player.GlobalPosition = Nav.Closest( player, kraken.GlobalPosition + forward * 4.4f ) + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		var atMouth = (player.GlobalPosition + Vector3.Up * 0.9f).DistanceTo( kraken.MouthWorld );
		await Wait( 0.25f );
		LookAt( kraken.MouthWorld );
		await Shot( "bite" );
		await Wait( 0.5f );
		GD.Print( $"[autotest] bite: {atMouth:0.0} m from the mouth, bites {kraken.Bites}, lost {health - survivor.Health.Current:0}, radiation {afflictions.RadiationLeft:0.0} s (poison {afflictions.RadiationPoisonLeft:0.0} s), mouth open {kraken.MouthOpen:0.0}" );
		Check( kraken.Bites >= 1 && health - survivor.Health.Current >= kraken.BiteDamage - 1f, "a player at the mouth was not bitten" );
		Check( afflictions.RadiationLeft > 0f && afflictions.RadiationPoisonLeft > 0f, "the bite did not irradiate" );
		Check( afflictions.StaminaRegenScale < 0.5f, "radiation does not slow stamina recovery" );
		await Wait( 1.2f );
		var thrown = (player.GlobalPosition + Vector3.Up * 0.9f).DistanceTo( kraken.MouthWorld );
		GD.Print( $"[autotest] bite: thrown back to {thrown:0.0} m from the mouth after {kraken.Bites} bites" );
		Check( thrown > atMouth + 1.5f, "the bite did not throw the player back" );

		// The dose: it hurts for a while with no more bites, and the body glows.
		player.GlobalPosition = stand;
		player.Velocity = Vector3.Zero;
		await Wait( 0.3f );
		var bitesBefore = kraken.Bites;
		health = survivor.Health.Current;
		player.ThirdPerson = true;
		await Wait( 0.4f );
		await Shot( "irradiated" );
		player.ThirdPerson = false;
		await Wait( 2f );
		var glow = player.GetNodeOrNull<Node3D>( "Radiation Glow" );
		GD.Print( $"[autotest] bite: the dose took {health - survivor.Health.Current:0} in 2.4 s with {kraken.Bites - bitesBefore} more bites; glow on the body {glow is { Visible: true }}" );
		Check( kraken.Bites == bitesBefore && health - survivor.Health.Current >= 5f, "the poisoned dose did no damage over time" );
		Check( glow is { Visible: true }, "an irradiated player does not glow" );
		afflictions.Clear();
		await Wait( 0.2f );
		Check( player.GetNodeOrNull<Node3D>( "Radiation Glow" ) is not { Visible: true }, "the glow stayed after the radiation was gone" );

		// 3. Ink, standing still: three globs, and the one aimed at the player lands on them.
		health = survivor.Health.Current;
		kraken.ForceInk();
		Check( kraken.Attacking == Kraken.Attack.Ink, "the ink attack did not start" );
		await Wait( 0.6f );
		Check( kraken.MouthOpen > 0.8f, "the mouth did not open to spit" );
		LookAt( kraken.MouthWorld );
		await Wait( 0.45f );
		await Shot( "ink_flying" );
		var flying = GetTree().GetNodesInGroup( "kraken_ink" ).Count;
		GD.Print( $"[autotest] ink: spat {kraken.InkSpat}, {flying} in flight" );
		Check( kraken.InkSpat == 3 && flying >= 1, "it did not spit three globs of ink" );
		await Wait( 2.6f );
		GD.Print( $"[autotest] ink: standing still lost {health - survivor.Health.Current:0}, ink on the view {afflictions.InkLeft:0.0} s, radiation {afflictions.RadiationLeft:0.0} s (poison {afflictions.RadiationPoisonLeft:0.0} s), stamina recovery x{afflictions.StaminaRegenScale:0.00}" );
		Check( health - survivor.Health.Current >= kraken.InkDamage - 1f, "the ink did not hurt a player who stood still" );
		Check( afflictions.RadiationLeft > 0f && afflictions.StaminaRegenScale < 0.5f, "the ink did not leave the player glowing with slow stamina" );
		Check( afflictions.RadiationPoisonLeft <= 0f, "the ink poisoned as well (only the bite should)" );
		await Shot( "inked" );
		Check( GetTree().GetNodesInGroup( "kraken_ink" ).Count == 0, "ink globs were left in flight" );

		// 4. Ink, stepping well aside after it is spat: missed.
		afflictions.Clear();
		for ( var waited = 0f; waited < 4f && kraken.Attacking != Kraken.Attack.None; waited += 0.25f ) await Wait( 0.25f );
		player.GlobalPosition = stand;
		player.Velocity = Vector3.Zero;
		await Wait( 0.3f );
		kraken.ForceInk();
		await Wait( kraken.InkWindTime + 0.15f );
		var aside = (player.GlobalPosition - ground).Flat().Normalized().Cross( Vector3.Up );
		player.GlobalPosition = Nav.Closest( player, player.GlobalPosition + aside * 11f ) + Vector3.Up * 0.1f;
		await Wait( 2.8f );
		GD.Print( $"[autotest] ink: after stepping aside radiation {afflictions.RadiationLeft:0.0} s, ink {afflictions.InkLeft:0.0} s" );
		Check( afflictions.RadiationLeft <= 0f && afflictions.InkLeft <= 0f, "stepping well aside did not dodge the ink" );

		// 5. With every tentacle gone it still has the ink.
		kraken.Health.Invulnerable = true;
		for ( var k = 0; k < Kraken.Tentacles; k++ )
			kraken.ReceiveShot( Kraken.SegIndex( k, 0 ), 5000f, Vector3.Right, kraken.GlobalPosition, player.GlobalPosition, null, false );
		var spat = kraken.InkSpat;
		kraken.HoldAttacks( 0.2f );
		await Wait( 2.2f );
		Check( kraken.InkSpat > spat, "a Kraken with no tentacles did not fall back on its ink" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> Board items #31, #32, #34, #37, #38, #40: gun distance, damage machine price, the death screen, the aim dot, third-person buying. </summary>
	/// <summary>
	/// The Rocky Shark (issue #29): the model's radiation holes and eyes are weak points, the rock plates turn
	/// shots, a section comes off with the tail behind it, and a bite irradiates.
	/// </summary>
	async Task SharkTest()
	{
		DevFlags.FreezeEnemies = true;   // it holds still for the aimed shots
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		var director = WaveDirector.Current;
		var ground = Nav.Closest( player, new Vector3( -8f, 0f, 12f ) );
		var stand = Nav.Closest( player, ground + new Vector3( 13f, 0f, 14f ) );
		player.GlobalPosition = stand + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		await Wait( 0.5f );

		var shark = director.SpawnAt( WaveDirector.EnemyKind.RockyShark, ground, player.GlobalPosition ) as RockyShark;
		Check( shark is not null, "the Rocky Shark did not spawn" );
		if ( shark is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		// Put it in open water in front of the player, broadside, for the screenshots.
		shark.GlobalPosition = player.GlobalPosition + (-player.GlobalBasis.Z) * 9f + Vector3.Up * 0.5f;
		shark.GlobalBasis = Shapes.LookAt( -player.GlobalBasis.X );
		await Wait( 0.5f );
		Check( shark.HoleCount >= 8, "the model has too few radiation holes" );
		GD.Print( $"[autotest] shark: {shark.HoleCount} radiation holes, {shark.SegmentCount} body sections, health {shark.Health.Current:0}" );
		LookAt( shark.GlobalPosition + shark.GlobalBasis.Z * 3f + Vector3.Up * 0.2f );
		await Shot( "shark_swim" );
		// Clean review shots of the model in the game: a free camera walked round it, kelp and coral, the gun and the HUD
		// hidden. The sea, its light and the shark's own paint are as they are in play.
		var headPart = CreaturePart.All.FirstOrDefault( p => p.Creature == shark && p.Name.ToString().EndsWith( "Head" ) );
		if ( headPart is not null )
		{
			var standAt = player.GlobalPosition;
			var hidden = new List<Node>();
			foreach ( var n in GetTree().Root.FindChildren( "*", "", true, false ) )
			{
				var nm = n.Name.ToString();
				if ( n is Node3D { Visible: true } n3 && (nm.StartsWith( "Kelp" ) || nm.StartsWith( "Coral" ) || nm == "Sea Dressing" || n is ViewModel || n is ViewModelAxe) ) { n3.Visible = false; hidden.Add( n ); }
				else if ( n is CanvasLayer { Visible: true } cl ) { cl.Visible = false; hidden.Add( n ); }
			}
			// The player's own camera (with the game's look) driven through the override; props near the shark hidden.
			var shotAt = Transform3D.Identity;
			player.CameraOverride = _ => shotAt;
			var rigNode = headPart.GetParent();
			while ( rigNode.GetParent() is Node p && p != GetTree().CurrentScene ) rigNode = p;
			foreach ( var n in GetTree().Root.FindChildren( "*", "GeometryInstance3D", true, false ).OfType<GeometryInstance3D>() )
			{
				if ( !n.Visible || rigNode.IsAncestorOf( n ) || shark.IsAncestorOf( n ) || player.IsAncestorOf( n ) ) continue;
				var box = n.GetAabb();
				var size = (n.GlobalTransform.Basis * box.Size).Abs();
				var centre = n.GlobalTransform * box.GetCenter();
				if ( size.X > 14f || size.Z > 14f ) continue;   // the sea bed and the big rocks stay
				if ( (centre - headPart.GlobalPosition).Flat().Length() < 9f ) { n.Visible = false; hidden.Add( n ); }
			}
			var face = headPart.GlobalPosition;
			var nose = -headPart.GlobalBasis.Z.Normalized();
			var side = headPart.GlobalBasis.X.Normalized();
			var up = headPart.GlobalBasis.Y.Normalized();
			var mid = face - nose * 2.0f;
			foreach ( var (name, at, look) in new[] {
				("shark_front", face + nose * 2.6f + side * 1.6f + up * 0.6f, face + nose * 0.2f),
				("shark_head", face + nose * 0.6f + side * 2.2f + up * 0.3f, face + nose * 0.3f),
				("shark_side", mid + side * 5.2f + up * 0.8f, mid),
				("shark_back", mid - nose * 4.2f + side * 2.8f + up * 2.2f, mid + nose * 0.6f) } )
			{
				shotAt = new Transform3D( Basis.LookingAt( (look - at).Normalized(), Vector3.Up ), at );
				await Wait( 0.25f );
				await Shot( name );
			}
			player.CameraOverride = null;
			foreach ( var n in hidden.Where( IsInstanceValid ) )
				if ( n is Node3D n3 ) n3.Visible = true; else if ( n is CanvasLayer cl ) cl.Visible = true;
			player.GlobalPosition = standAt;
			player.Velocity = Vector3.Zero;
			await Wait( 0.3f );
		}

		// Eyes: each is a weak point, and with both gone the shark is blind.
		var eyeL = CreaturePart.All.FirstOrDefault( p => p.Creature == shark && p.Name.ToString().EndsWith( "Eye L" ) );
		var eyeR = CreaturePart.All.FirstOrDefault( p => p.Creature == shark && p.Name.ToString().EndsWith( "Eye R" ) );
		Check( eyeL is not null && eyeR is not null, "the shark has no eye hitboxes" );
		if ( eyeL is not null && eyeR is not null )
		{
			var before = shark.Health.Current;
			var dir = (eyeL.GlobalPosition - player.GlobalPosition).SafeNormal();
			shark.ReceiveShot( eyeL.Index, 30f, dir, eyeL.GlobalPosition, player.GlobalPosition, survivor, true );
			var eyeDamage = before - shark.Health.Current;
			shark.ReceiveShot( eyeR.Index, 30f, dir, eyeR.GlobalPosition, player.GlobalPosition, survivor, true );
			GD.Print( $"[autotest] shark: an eye shot did {eyeDamage:0} damage, blind now {shark.Blind}" );
			Check( eyeDamage > 30f * shark.ArmorMultiplier * 2f, "an eye is not a weak point" );
			Check( shark.Blind, "shooting both eyes did not blind it" );
			shark.Health.ResetTo( shark.Health.MaxHealth );   // the bite test needs it alive
		}

		// The radiation holes: much more damage than the armoured body.
		var hole = shark.FirstHole;
		Check( hole is not null, "the shark has no live radiation holes" );
		if ( hole is { } h )
		{
			var before = shark.Health.Current;
			shark.ReceiveShot( h.Segment, 10f, Vector3.Forward, h.Position, player.GlobalPosition, survivor, true );
			var holeDamage = before - shark.Health.Current;
			before = shark.Health.Current;
			var bodyPoint = shark.SectionPosition( 1 ) + Vector3.Up * 1f;
			shark.ReceiveShot( shark.SectionIndex( 1 ), 10f, Vector3.Forward, bodyPoint, player.GlobalPosition, survivor, true );
			var bodyDamage = before - shark.Health.Current;
			GD.Print( $"[autotest] shark: a hole did {holeDamage:0} damage, the armoured body {bodyDamage:0}" );
			Check( holeDamage > bodyDamage * 1.6f, "the radiation holes are not weak points" );
			shark.Health.ResetTo( shark.Health.MaxHealth );
		}

		// Cutting a section drops everything behind it, tail and all.
		var lostBefore = shark.LostSegments;
		for ( var i = 0; i < shark.SegmentCount && shark.LostSegments < 2; i++ )
		{
			if ( !shark.SectionAlive( i ) ) continue;
			shark.ReceiveShot( shark.SectionIndex( i ), 100f, Vector3.Forward, shark.SectionPosition( i ) + Vector3.Up, player.GlobalPosition, survivor, true );
		}
		GD.Print( $"[autotest] shark: {shark.LostSegments} sections shot off (was {lostBefore})" );
		Check( shark.LostSegments >= 2, "shooting a section did not cut the shark short" );
		shark.Health.ResetTo( shark.Health.MaxHealth );   // keep it alive for the bite and death checks
		await Shot( "shark_cut" );

		// Bite: radiation sickness, and it shoves the player back.
		DevFlags.FreezeEnemies = false;
		shark.MoveSpeed = 10f;
		shark.BiteCooldown = 0.2f;
		shark.BiteRadiationChance = 1f;
		shark.BiteRange = 400f;
		shark.GlobalPosition = player.GlobalPosition + (-player.GlobalBasis.Z) * 0.7f;
		shark.GlobalBasis = Shapes.LookAt( (player.GlobalPosition - shark.GlobalPosition).SafeNormal() );
		await Wait( 0.3f );
		var afflictions = survivor.Afflictions;
		afflictions.Clear();
		var bitesBefore = shark.Bites;
		var healthBefore = survivor.Health.Current;
		LookAt( shark.MouthWorld );
		await Wait( 0.6f );
		GD.Print( $"[autotest] shark: bites {bitesBefore} to {shark.Bites}, damage {healthBefore - survivor.Health.Current:0}, radiation {afflictions.RadiationLeft:0.0} s (poison {afflictions.RadiationPoisonLeft:0.0} s)" );
		Check( shark.Bites > bitesBefore, "the shark did not bite a player at its mouth" );
		Check( afflictions.RadiationLeft > 0f && afflictions.RadiationPoisonLeft > 0f, "the bite did not irradiate" );
		await Shot( "shark_bite" );

		shark.Health.OnDamage( new DamageInfo { Damage = 100000f, Attacker = survivor, Position = shark.GlobalPosition, Origin = player.GlobalPosition } );
		await Wait( 0.5f );
		GD.Print( $"[autotest] shark: dead {shark.IsDead}, sections left {shark.SegmentCount - shark.LostSegments}" );
		Check( shark.IsDead, "the shark did not die" );
		await Shot( "shark_dead" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	async Task Board1Test()
	{
		DevFlags.FreezeEnemies = true;
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		DockHouse.Current?.SkipDive();
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var weapon = inventory.Weapons[0];
		inventory.Select( weapon, force: true );
		var stations = GetTree().Root.FindChildren( "*", "", true, false ).OfType<BuyStation>().ToList();
		var damage = stations.FirstOrDefault( s => s.Kind == BuyStationKind.Damage );
		Check( damage is not null, "no damage upgrade machine in the level" );

		// #36: the real recordings are in and found.
		foreach ( var name in new[] { "footstep_dock", "footstep_floor" } )
		{
			var picked = Sfx.Pick( name );
			Check( picked != name && ResourceLoader.Exists( $"res://assets/sounds/{picked}.ogg" ), $"no recordings found for {name}" );
		}
		foreach ( var name in new[] { "rifle_shoot", "shotgun_shoot" } )
			Check( ResourceLoader.Exists( $"res://assets/sounds/{name}.wav" ) && GD.Load<AudioStreamWav>( $"res://assets/sounds/{name}.wav" ) is { } wav && wav.GetLength() is > 0.8 and < 2.5, $"{name} is not the trimmed recording" );

		// #32: the damage machine starts at 500.
		GD.Print( $"[autotest] board: damage machine costs {damage?.Cost}, then +{damage?.CostIncrease} each ({string.Join( ", ", Enumerable.Range( 0, damage?.MaxPurchases ?? 0 ).Select( i => damage.Cost + damage.CostIncrease * i ) )})" );
		Check( damage is { Cost: 500 }, "the damage machine does not start at 500" );

		// #38: buying in third person. Stand at the machine, look at it, switch to third person.
		var use = player.GetNode<PlayerUse>( "PlayerUse" );
		var at = (damage.GetParent() as Node3D).GlobalPosition;
		player.GlobalPosition = Nav.Closest( player, at + new Vector3( 1.9f, 0f, 0f ) ) + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		await Wait( 0.6f );
		LookAt( at + Vector3.Up * 0.3f );
		await Wait( 0.3f );
		Check( use.Hovered == damage, "could not reach the machine in first person (the test is standing wrong)" );
		player.ThirdPerson = true;
		await Wait( 0.8f );
		GD.Print( $"[autotest] board: third person, {player.Camera.GlobalPosition.DistanceTo( player.EyeTransform.Origin ):0.0} m behind the robot, looking at {use.Hovered?.Title ?? "nothing"}" );
		Check( use.Hovered == damage, "the machine is out of reach in third person" );
		survivor.AddPoints( 5000 );
		var bought = use.TryPress();
		Check( bought && damage.Purchases == 1, "could not buy in third person" );
		await Shot( "third_person_buy" );
		player.ThirdPerson = false;
		await Wait( 0.4f );

		// #37: the dot stays when aimed down the sights (the Blaster has no scope).
		weapon.ForceAim = true;
		await Wait( 0.8f );
		GD.Print( $"[autotest] board: aimed {weapon.AimAmount:0.00}, scope {weapon.ScopeAmount:0.00}, dot drawn at {WeaponHud.DotAlpha:0.00}" );
		Check( weapon.AimAmount > 0.9f && WeaponHud.DotAlpha > 0.9f, "the dot is missing when aimed down the sights" );
		await Shot( "aimed_dot" );
		weapon.ForceAim = false;
		await Wait( 0.5f );

		// #31: the gun distance setting moves the gun.
		var vm = weapon.ViewModel;
		var usual = vm.Position;
		GameSettings.GunDistance = 8f;
		await Wait( 0.3f );
		var moved = vm.Position.DistanceTo( usual );
		GD.Print( $"[autotest] board: gun distance +8 moved the gun {moved / U.In:0.0} in" );
		Check( moved > 6f * U.In, "the gun distance setting did not move the gun" );
		await Shot( "gun_far" );
		GameSettings.GunDistance = 0f;

		// #34 and #40: dead. No gun, no shooting, no HUD, and the camera watches the body.
		var eye = player.Camera.GlobalPosition;
		survivor.Health.OnDamage( new DamageInfo { Damage = 100000f } );
		await Wait( 2.5f );
		var ragdoll = GetTree().Root.FindChild( "Robot Ragdoll", true, false ) as RobotRagdoll;
		var ammo = weapon.Ammo;
		Input.ParseInputEvent( new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true } );
		await Wait( 0.3f );
		Input.ParseInputEvent( new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false } );
		var hud = GetTree().Root.FindChildren( "*", "", true, false ).OfType<HudOverlays>().FirstOrDefault();
		var cam = player.Camera;
		var toBody = ragdoll is null ? 99f : MathX.Deg( (ragdoll.Centre - cam.GlobalPosition).AngleTo( -cam.GlobalBasis.Z ) );
		GD.Print( $"[autotest] board: dead: ragdoll {ragdoll is not null}, gun shown {weapon.ViewModel?.Visible}, ammo {ammo} -> {weapon.Ammo}, HUD alpha {hud?.Modulate.A:0.0}, camera moved {cam.GlobalPosition.DistanceTo( eye ):0.0} m and looks {toBody:0} deg off the body" );
		Check( ragdoll is not null, "no ragdoll on death" );
		Check( weapon.ViewModel is not { Visible: true }, "the gun is still on screen when dead" );
		Check( weapon.Ammo == ammo, "a dead player could still shoot" );
		Check( hud is { Modulate.A: < 0.01f }, "the HUD is still showing when dead" );
		var back = cam.GlobalPosition.DistanceTo( ragdoll?.Centre ?? eye );
		GD.Print( $"[autotest] board: death camera {back:0.0} m from the body" );
		Check( back > 2.5f && toBody < 22f, "the camera is not standing back watching the body" );
		await Shot( "death_camera" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> Playtest 11/11 (issue #41): upgrade caps, Kraken health, crab reach, slide in a malfunction round, the gun's water filter. </summary>
	async Task Round11Test()
	{
		DevFlags.FreezeEnemies = true;
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		DockHouse.Current?.SkipDive();
		var director = WaveDirector.Current;
		var stations = GetTree().Root.FindChildren( "*", "", true, false ).OfType<BuyStation>().ToList();
		var health = stations.First( s => s.Kind == BuyStationKind.Health );
		var damage = stations.First( s => s.Kind == BuyStationKind.Damage );
		survivor.AddPoints( 1_000_000 );

		// Upgrades: ten of each. Health keeps healing after ten without raising the maximum.
		var max0 = survivor.Health.MaxHealth;
		for ( var i = 0; i < 10; i++ ) { Check( health.Press( survivor ), $"health purchase {i + 1} was refused" ); }
		var max10 = survivor.Health.MaxHealth;
		survivor.Health.OnDamage( new DamageInfo { Damage = 50f } );
		var hurt = survivor.Health.Current;
		var priceAtLimit = health.CurrentCost;
		var again = health.Press( survivor );
		GD.Print( $"[autotest] round11: health max {max0} -> {max10} after 10 buys; an 11th buy {again}, max {survivor.Health.MaxHealth}, healed {hurt} -> {survivor.Health.Current}, price {priceAtLimit} -> {health.CurrentCost}, sold out {health.SoldOut}" );
		Check( max10 == max0 + 250f && again && survivor.Health.MaxHealth == max10 && survivor.Health.Current == max10 && !health.SoldOut, "health after ten upgrades should still heal without raising the maximum" );
		Check( health.CurrentCost == priceAtLimit, "the health price kept climbing past the limit" );
		for ( var i = 0; i < 10; i++ ) Check( damage.Press( survivor ), $"damage purchase {i + 1} was refused" );
		Check( damage.SoldOut && !damage.Press( survivor ), "damage should sell out after ten" );

		// The Kraken is twice as tough and 20% tougher each time it comes back, dome and all.
		director.SpawnAt( WaveDirector.EnemyKind.Kraken, Nav.Closest( player, new Vector3( -8f, 0f, 12f ) ), player.GlobalPosition );
		var kraken = Kraken.All.First();
		await Wait( 0.5f );
		GD.Print( $"[autotest] round11: Kraken health {kraken.Health.MaxHealth:0}, dome {kraken.HelmetHealth:0}, visits {director.KrakenVisits}" );
		Check( kraken.Health.MaxHealth == 7000f && kraken.HelmetHealth == 1200f, "the first Kraken is not 7000 health and a 1200 dome" );
		kraken.QueueFree();
		for ( var visit = 0; visit < 2; visit++ )
		{
			director.ForceKrakenNextWave();
			director.JumpToWave( director.Wave + 1 );
			await Wait( 0.6f );
		}
		director.SpawnAt( WaveDirector.EnemyKind.Kraken, Nav.Closest( player, new Vector3( -8f, 0f, 12f ) ), player.GlobalPosition );
		var second = Kraken.All.Last();
		await Wait( 0.5f );
		GD.Print( $"[autotest] round11: second visit health {second.Health.MaxHealth:0}, dome {second.HelmetHealth:0} (visits {director.KrakenVisits})" );
		Check( second.Health.MaxHealth > 6999f * 1.2f - 1f && second.HelmetHealth > 1199f * 1.2f - 1f, "the Kraken is not 20% tougher on its second visit" );
		foreach ( var k in Kraken.All.ToList() ) k.QueueFree();
		director.ClearEnemies();
		await Wait( 0.4f );

		// A crab cannot claw a player standing high above it, but does hit one on the floor.
		var ground = Nav.Closest( player, new Vector3( 4f, 0f, 6f ) );
		player.GlobalPosition = ground + Vector3.Up * 0.1f;
		survivor.Health.ResetTo( 1000f );
		DevFlags.FreezeEnemies = false;
		var crabNode = director.SpawnAt( WaveDirector.EnemyKind.Crab, ground + new Vector3( 1.2f, 0f, 0f ), ground );
		var crab = crabNode as JunkCrab;
		Check( crab is not null, "no crab to test with" );
		await Wait( 0.5f );
		var health0 = survivor.Health.Current;
		// Held in the water well above it, right over the crab.
		for ( var t = 0f; t < 2.2f; t += 0.02f )
		{
			player.GlobalPosition = crab.GlobalPosition + Vector3.Up * 2.2f;
			player.Velocity = Vector3.Zero;
			await ToSignal( GetTree(), SceneTree.SignalName.PhysicsFrame );
		}
		GD.Print( $"[autotest] round11: player 2.2 m over a crab for 2 s lost {health0 - survivor.Health.Current:0}" );
		Check( survivor.Health.Current >= health0, "a crab clawed a player standing well above it" );
		player.GlobalPosition = Nav.Closest( player, crab.GlobalPosition + new Vector3( 0.8f, 0f, 0f ) ) + Vector3.Up * 0.1f;
		await Wait( 2.2f );
		GD.Print( $"[autotest] round11: player on the floor beside the crab lost {health0 - survivor.Health.Current:0}" );
		Check( survivor.Health.Current < health0, "the crab no longer hits a player on the floor" );
		crab.QueueFree();
		DevFlags.FreezeEnemies = true;

		// A malfunction round that starts mid slide: the slide must end.
		var movement2 = player.GetNode<PlayerMovement>( "PlayerMovement" );
		player.GlobalPosition = Nav.Closest( player, new Vector3( -4f, 0f, 4f ) ) + Vector3.Up * 0.1f;
		await Wait( 0.5f );
		player.Velocity = -player.GlobalBasis.Z * 7f;
		movement2.ForceCrouch = true;
		await Wait( 0.3f );
		var slid = movement2.IsSliding;
		director.FloatRound = true;
		movement2.ForceCrouch = false;
		await Wait( 0.6f );
		GD.Print( $"[autotest] round11: was sliding {slid}, in a malfunction round sliding {movement2.IsSliding}" );
		Check( slid, "the test could not start a slide" );
		Check( !movement2.IsSliding, "the slide is stuck on in a malfunction round" );
		director.FloatRound = false;

		// The malfunction round has its own sounds, banner words and warning light.
		foreach ( var name in new[] { "suit_alarm", "suit_restore" } ) Check( ResourceLoader.Exists( $"res://assets/sounds/{name}.wav" ), $"no {name} sound" );
		director.JumpToWave( 4 );
		await Wait( 1.2f );
		Check( director.FloatRound && director.State == WaveState.Fighting, "wave 4 was not a suit malfunction round" );
		await Shot( "malfunction_hud" );
		director.JumpToWave( 5 );
		await Wait( 1.2f );
		Check( !director.FloatRound, "wave 5 was still a malfunction round" );

		// The slide: a comic head-first dive. It falls into it, ends up on its belly heading the way it moves, the gun
		// still points where the player aims, and it springs back up afterwards.
		var rig = player.GetChildren().OfType<PlayerRig>().First();
		player.ThirdPerson = true;
		player.GlobalPosition = Nav.Closest( player, new Vector3( -4f, 0f, 4f ) ) + Vector3.Up * 0.1f;
		player.EyeAngles = new Angles( 0f, 0f, 0f );
		await Wait( 0.7f );
		var standingAim = -player.EyeAngles.ToBasis().Z;
		Check( rig.GunForward() is { } standing && standing.Dot( standingAim ) > 0.8f, "the gun does not point where the player aims, even standing" );
		player.Velocity = -player.GlobalBasis.Z * 7f;
		movement2.ForceCrouch = true;
		await Wait( 0.12f );
		var falling = rig.DiveAmount;
		await Shot( "slide_fall" );
		await Wait( 0.5f );
		var heading = player.Velocity.Flat().Normalized();
		var up = rig.BodyUp;
		var forwardGun = rig.GunForward();
		var aim = -player.EyeAngles.ToBasis().Z;
		GD.Print( $"[autotest] slide: dive {falling:0.00} a moment in, {rig.DiveAmount:0.00} later; head level {MathF.Abs( up.Y ):0.00}, head along travel {up.Dot( heading ):0.00}, gun along aim {forwardGun?.Dot( aim ):0.00}" );
		Check( falling > 0.02f && falling < 0.8f, "the slide does not lean and fall into the dive" );
		Check( rig.DiveAmount > 0.85f && MathF.Abs( up.Y ) < 0.4f && up.Dot( heading ) > 0.75f, "the slide is not head first along the way it moves" );
		Check( forwardGun is { } g && g.Dot( aim ) > 0.75f, "the gun does not point where the player is aiming while sliding" );
		await Shot( "slide_headfirst" );
		// The camera swings round to the side while the body keeps going: the gun follows the aim.
		player.EyeAngles = new Angles( 4f, 65f, 0f );
		await Wait( 0.35f );
		aim = -player.EyeAngles.ToBasis().Z;
		forwardGun = rig.GunForward();
		GD.Print( $"[autotest] slide: aiming 65 degrees off the way it slides, gun along aim {forwardGun?.Dot( aim ):0.00}" );
		Check( forwardGun is { } g2 && g2.Dot( aim ) > 0.6f, "the gun does not follow the aim sideways while sliding" );
		await Shot( "slide_headfirst_side" );
		movement2.ForceCrouch = false;
		await Wait( 0.9f );
		GD.Print( $"[autotest] slide: back up, dive {rig.DiveAmount:0.00}" );
		Check( rig.DiveAmount < 0.1f && !movement2.IsSliding, "the robot did not spring back up after the slide" );
		player.ThirdPerson = false;

		// The gun's own filter: open air, then underwater.
		Sfx.SetUnderwater( 0f );
		var bus = AudioServer.GetBusIndex( Sfx.GunBus );
		var air = ((AudioEffectLowPassFilter)AudioServer.GetBusEffect( bus, 0 )).CutoffHz;
		Sfx.SetUnderwater( 1f );
		var under = ((AudioEffectLowPassFilter)AudioServer.GetBusEffect( bus, 0 )).CutoffHz;
		GD.Print( $"[autotest] round11: gun filter {air:0} Hz in the air, {under:0} Hz underwater, wash {((AudioEffectReverb)AudioServer.GetBusEffect( bus, 1 )).Wet:0.00}" );
		Check( air > 15000f && under < 800f, "the gun's underwater filter does not close down" );
		Sfx.SetUnderwater( 0f );
		foreach ( var name in new[] { "dock_wave", "gull", "stab_squish" } )
			Check( Sfx.Pick( name ) != name, $"no sounds found for {name}" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> Controller support (issue #33), driven with injected pad events: bindings, device switching, sticks, triggers, buttons, menus, rumble. </summary>
	async Task PadTest()
	{
		DevFlags.FreezeEnemies = true;
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var weapon = inventory.Weapons[0];
		inventory.Select( weapon, force: true );
		player.GlobalPosition = Nav.Closest( player, new Vector3( 3f, 0f, 3f ) ) + Vector3.Up * 0.1f;
		player.EyeAngles = new Angles( 0f, 0f, 0f );
		await Wait( 0.6f );
		void Axis( JoyAxis axis, float value ) => Input.ParseInputEvent( new InputEventJoypadMotion { Axis = axis, AxisValue = value } );
		void Pad( JoyButton button, bool down ) => Input.ParseInputEvent( new InputEventJoypadButton { ButtonIndex = button, Pressed = down } );
		float YawChange( float before ) => MathF.Abs( Mathf.Wrap( player.EyeAngles.Yaw - before, -180f, 180f ) );

		// Every action has a pad binding, and the keyboard and mouse ones are still there.
		var actions = new[] { "left", "right", "forward", "backward", "jump", "duck", "reload", "use", "run", "melee", "surge", "view", "command", "attack1", "attack2", "weapon_prev", "weapon_next", "pause", "look_left", "look_right", "look_up", "look_down" };
		foreach ( var action in actions )
			Check( InputMap.ActionGetEvents( action ).Any( e => e is InputEventJoypadButton or InputEventJoypadMotion ), $"the {action} action has no pad binding" );
		Check( InputMap.ActionGetEvents( "jump" ).Any( e => e is InputEventKey ) && InputMap.ActionGetEvents( "attack1" ).Any( e => e is InputEventMouseButton ), "the keyboard and mouse bindings were lost" );

		// Device switching and the names on screen.
		Controls.Force( Scheme.KeyboardMouse );
		var keys = Controls.Label( "use" );
		Axis( JoyAxis.LeftX, 0.8f );
		await Wait( 0.1f );
		Axis( JoyAxis.LeftX, 0f );
		GD.Print( $"[autotest] pad: after the stick moved the game is in {Controls.Current}; use is {Controls.Label( "use" )} (was {keys})" );
		Check( Controls.IsController && Controls.Label( "use" ) == "Y" && keys == "E", "touching a stick did not switch to the controller" );
		Controls.SimulatePlayStation = true;
		Check( Controls.Label( "use" ) == "Triangle" && Controls.Label( "jump" ) == "Cross", "PlayStation names are wrong" );
		Controls.SimulatePlayStation = false;
		Input.ParseInputEvent( new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = true } );
		await Wait( 0.1f );
		Input.ParseInputEvent( new InputEventKey { Keycode = Key.W, PhysicalKeycode = Key.W, Pressed = false } );
		Check( !Controls.IsController && Controls.Label( "use" ) == "E", "pressing a key did not switch back to the keyboard" );
		Pad( JoyButton.A, true ); await Wait( 0.05f ); Pad( JoyButton.A, false );
		Check( Controls.IsController, "pressing a pad button did not switch to the controller" );
		Input.ParseInputEvent( new InputEventMouseMotion { Relative = new Vector2( 9f, 3f ), Position = new Vector2( 50f, 50f ) } );
		await Wait( 0.1f );
		Check( !Controls.IsController, "moving the mouse did not switch back to the keyboard and mouse" );
		Controls.Force( Scheme.Controller );

		// Moving: the left stick, analogue, with a dead zone.
		player.EyeAngles = new Angles( 0f, 0f, 0f );
		player.GlobalPosition = Nav.Closest( player, player.GlobalPosition ) + Vector3.Up * 0.1f;   // the A press above made it jump
		player.Velocity = Vector3.Zero;
		await Wait( 1.5f );
		Axis( JoyAxis.LeftY, 0.1f );
		await Wait( 0.7f );
		var drift = player.Velocity.Flat().Length();
		Axis( JoyAxis.LeftY, -1f );
		await Wait( 1.0f );
		var full = player.Velocity.Flat().Length();
		Axis( JoyAxis.LeftY, 0f );
		await Wait( 1.0f );
		Axis( JoyAxis.LeftY, -0.5f );
		await Wait( 1.0f );
		var half = player.Velocity.Flat().Length();
		Axis( JoyAxis.LeftY, 0f );
		await Wait( 0.6f );
		GD.Print( $"[autotest] pad: left stick drift {drift:0.00} m/s, full {full:0.0}, half {half:0.0}" );
		Check( drift < 0.2f, "a stick resting inside the dead zone moved the player" );
		Check( full > 3f && half > 1f && half < full * 0.8f, "the left stick is not analogue (half tilt should be slower than full)" );

		// Looking: the right stick, with the curve, and slower when aiming.
		var yaw0 = player.EyeAngles.Yaw;
		Axis( JoyAxis.RightX, 1f );
		await Wait( 0.5f );
		Axis( JoyAxis.RightX, 0f );
		var fullTurn = YawChange( yaw0 );
		await Wait( 0.2f );
		yaw0 = player.EyeAngles.Yaw;
		Axis( JoyAxis.RightX, 0.5f );
		await Wait( 0.5f );
		Axis( JoyAxis.RightX, 0f );
		var halfTurn = YawChange( yaw0 );
		await Wait( 0.2f );
		var pitch0 = player.EyeAngles.Pitch;
		Axis( JoyAxis.RightY, 1f );
		await Wait( 0.3f );
		Axis( JoyAxis.RightY, 0f );
		var pitched = player.EyeAngles.Pitch - pitch0;
		weapon.ForceAim = true;
		await Wait( 0.8f );
		yaw0 = player.EyeAngles.Yaw;
		Axis( JoyAxis.RightX, 1f );
		await Wait( 0.5f );
		Axis( JoyAxis.RightX, 0f );
		var aimedTurn = YawChange( yaw0 );
		weapon.ForceAim = false;
		await Wait( 0.5f );
		GD.Print( $"[autotest] pad: right stick turned {fullTurn:0} deg in 0.5 s at full tilt, {halfTurn:0} at half, {aimedTurn:0} while aiming; stick down looked {pitched:0} deg down" );
		Check( fullTurn > 70f && fullTurn < 150f, "a full right stick turn is the wrong speed" );
		Check( halfTurn > 3f && halfTurn < fullTurn * 0.5f, "half tilt of the right stick does not turn gently" );
		Check( pitched > 20f, "the right stick does not look up and down" );
		Check( aimedTurn < fullTurn - 5f, "aiming does not slow the right stick" );

		// Triggers: a light pull does nothing, a full one fires; the left one aims.
		player.EyeAngles = new Angles( 0f, 0f, 0f );
		var ammo = weapon.Ammo;
		Axis( JoyAxis.TriggerRight, 0.2f );
		await Wait( 0.5f );
		Check( weapon.Ammo == ammo, "a light trigger pull fired" );
		var rumbles = Controls.RumbleCount;
		Axis( JoyAxis.TriggerRight, 1f );
		await Wait( 0.4f );
		Axis( JoyAxis.TriggerRight, 0f );
		GD.Print( $"[autotest] pad: right trigger fired {ammo - weapon.Ammo} rounds and asked for {Controls.RumbleCount - rumbles} rumbles" );
		Check( weapon.Ammo < ammo, "the right trigger does not fire" );
		Check( Controls.RumbleCount > rumbles, "firing does not rumble" );
		GameSettings.Vibration = false;
		var off = Controls.RumbleCount;
		Controls.Rumble( 0.8f );
		Check( Controls.RumbleCount == off, "rumble ignored the vibration setting" );
		GameSettings.Vibration = true;
		Axis( JoyAxis.TriggerLeft, 1f );
		await Wait( 0.8f );
		var aiming = weapon.IsAiming;
		Axis( JoyAxis.TriggerLeft, 0f );
		await Wait( 0.5f );
		Check( aiming, "the left trigger does not aim" );

		// Every button reaches its action, and the real ones do their job.
		foreach ( var (action, button) in new[] { ("reload", JoyButton.X), ("use", JoyButton.Y), ("duck", JoyButton.B), ("run", JoyButton.LeftShoulder), ("run", JoyButton.LeftStick), ("melee", JoyButton.RightShoulder), ("surge", JoyButton.RightStick), ("view", JoyButton.DpadUp), ("command", JoyButton.DpadDown), ("jump", JoyButton.A) } )
		{
			Pad( button, true );
			await Wait( 0.15f );
			var pressed = Input.IsActionPressed( action );
			Pad( button, false );
			Check( pressed, $"{button} does not press {action}" );
			await Wait( 0.35f );
		}
		player.ThirdPerson = false;
		await Wait( 0.2f );
		Pad( JoyButton.DpadUp, true ); await Wait( 0.1f ); Pad( JoyButton.DpadUp, false ); await Wait( 0.2f );
		Check( player.ThirdPerson, "D-pad up does not toggle third person" );
		Pad( JoyButton.DpadUp, true ); await Wait( 0.1f ); Pad( JoyButton.DpadUp, false ); await Wait( 0.2f );
		Check( !player.ThirdPerson, "D-pad up does not toggle third person back" );
		player.GlobalPosition = Nav.Closest( player, player.GlobalPosition ) + Vector3.Up * 0.1f;
		await Wait( 0.8f );
		var floorY = player.GlobalPosition.Y;
		var rise = 0f;
		Pad( JoyButton.A, true );
		for ( var t = 0f; t < 0.5f; t += 0.02f )
		{
			if ( t > 0.12f ) Pad( JoyButton.A, false );
			rise = MathF.Max( rise, player.GlobalPosition.Y - floorY );
			await ToSignal( GetTree(), SceneTree.SignalName.PhysicsFrame );
		}
		Pad( JoyButton.A, false );
		GD.Print( $"[autotest] pad: A lifted the player {rise:0.00} m" );
		Check( rise > 0.2f, "A does not jump" );
		await Wait( 1.2f );

		// Switching weapons on the D-pad.
		var held = inventory.Current;
		Pad( JoyButton.DpadRight, true ); await Wait( 0.1f ); Pad( JoyButton.DpadRight, false ); await Wait( 0.3f );
		var next = inventory.Current;
		Pad( JoyButton.DpadLeft, true ); await Wait( 0.1f ); Pad( JoyButton.DpadLeft, false ); await Wait( 0.3f );
		GD.Print( $"[autotest] pad: D-pad right went from {held?.DisplayName} to {next?.DisplayName}, left came back to {inventory.Current?.DisplayName}" );
		Check( next != held && inventory.Current == held, "the D-pad does not switch weapons" );

		// Menus: Start pauses, B resumes, the cursor hides, the first button has focus, and the hint names the button.
		Pad( JoyButton.Start, true ); await Wait( 0.1f ); Pad( JoyButton.Start, false ); await Wait( 0.4f );
		var focus = GetViewport().GuiGetFocusOwner();
		var menu = GetTree().Root.FindChild( "PauseMenu", true, false ) as PauseMenu;
		var hint = menu.FindChildren( "*", "Label", true, false ).OfType<Label>().FirstOrDefault( l => l.Text.EndsWith( "to resume" ) );
		GD.Print( $"[autotest] pad: Start opened the pause menu {PauseMenu.IsOpen}, mouse {Input.MouseMode}, focus on {focus?.Name}, hint {hint?.Text}" );
		Check( PauseMenu.IsOpen && GetTree().Paused, "Start did not pause" );
		Check( Input.MouseMode == Input.MouseModeEnum.Hidden || DisplayServer.GetName() == "headless", "the cursor shows while pausing with a pad" );
		Check( focus is Button resumeButton && resumeButton.Name == "Resume", "the pause menu has no button focused for the pad" );
		Check( hint?.Text == "Start to resume", "the pause hint does not name the Start button" );
		GD.Print( $"[autotest] pad: ui_cancel is bound to {string.Join( ", ", InputMap.ActionGetEvents( "ui_cancel" ).Select( e => e.AsText() ) )}" );
		Pad( JoyButton.B, true ); await Wait( 0.1f ); Pad( JoyButton.B, false ); await Wait( 0.4f );
		GD.Print( $"[autotest] pad: after B the pause menu is open {PauseMenu.IsOpen}, tree paused {GetTree().Paused}" );
		Check( !PauseMenu.IsOpen && !GetTree().Paused, "B did not close the pause menu" );
		Pad( JoyButton.Start, true ); await Wait( 0.1f ); Pad( JoyButton.Start, false ); await Wait( 0.3f );
		Pad( JoyButton.Start, true ); await Wait( 0.1f ); Pad( JoyButton.Start, false ); await Wait( 0.3f );
		Check( !PauseMenu.IsOpen, "Start did not resume" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> Aim assist, and the dev menu driven entirely by a pad (focus, bumpers, scrolling, A, B, placing an enemy with the trigger). </summary>
	async Task PadMenusTest()
	{
		DevFlags.FreezeEnemies = true;
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		var director = WaveDirector.Current;
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var weapon = inventory.Weapons[0];
		inventory.Select( weapon, force: true );
		void Axis( JoyAxis axis, float value ) => Input.ParseInputEvent( new InputEventJoypadMotion { Axis = axis, AxisValue = value } );
		void Pad( JoyButton button, bool down ) => Input.ParseInputEvent( new InputEventJoypadButton { ButtonIndex = button, Pressed = down } );
		async Task Tap( JoyButton button ) { Pad( button, true ); await Wait( 0.12f ); Pad( button, false ); await Wait( 0.25f ); }

		// ---- aim assist ----
		var ground = Nav.Closest( player, new Vector3( 2f, 0f, 2f ) );
		player.GlobalPosition = ground + Vector3.Up * 0.1f;
		player.EyeAngles = new Angles( 0f, 0f, 0f );
		await Wait( 0.6f );
		var forward = -player.Camera.GlobalBasis.Z.Flat().Normalized();
		var crab = director.SpawnAt( WaveDirector.EnemyKind.Crab, Nav.Closest( player, ground + forward * 14f ), ground ) as JunkCrab;
		Check( crab is not null, "no crab to aim at" );
		await Wait( 0.6f );
		Controls.Force( Scheme.Controller );
		GameSettings.AimAssist = 0.7f;
		float AngleTo( Vector3 point ) => MathX.Deg( (-player.Camera.GlobalBasis.Z).AngleTo( (point - player.Camera.GlobalPosition).Normalized() ) );
		await Wait( 0.8f );   // let it settle on the sea bed
		var mark = crab.GlobalPosition + Vector3.Up * 0.35f;
		void AimAtCrab( float yawOff )
		{
			// The middle of its first hit shape: the very point the aim assist works from.
			var bodyPart = CreaturePart.All.FirstOrDefault( p => IsInstanceValid( p ) && p.Creature == crab );
			if ( bodyPart?.GetChildren().OfType<CollisionShape3D>().FirstOrDefault() is { } shape ) mark = shape.GlobalPosition;
			var look = (mark - player.Camera.GlobalPosition).Normalized();
			player.EyeAngles = new Angles( MathX.Deg( -MathF.Asin( look.Y ) ), MathX.Deg( MathF.Atan2( -look.X, -look.Z ) ) + yawOff, 0f );
		}

		// What it decides: near the crab it slows and pulls; off it, nothing; on the keyboard or with the setting off, nothing.
		AimAtCrab( 0.8f );
		await Wait( 0.2f );
		var near = AimAssist.Evaluate( player, player.EyeAngles, 0.016f, true, false, 0f );
		GD.Print( $"[autotest] aim: near the crab slow {near.Slow:0.00}, pull {near.Pull.Length() * 1000f:0} milli-degrees, closeness {near.Closeness:0.00}" );
		Check( near.Slow < 0.9f && near.Pull.Length() > 0f && near.Closeness > 0.2f, "aim assist does nothing near an enemy" );
		var still = AimAssist.Evaluate( player, player.EyeAngles, 0.016f, false, false, 0f );
		Check( still.Slow < 0.9f && still.Pull == Vector2.Zero, "aim assist pulled with no stick or trigger" );
		var firing = AimAssist.Evaluate( player, player.EyeAngles, 0.016f, false, true, 0f );
		Check( firing.Pull.Length() > 0f, "aim assist does not help while firing" );
		AimAtCrab( 30f );
		await Wait( 0.2f );
		var far = AimAssist.Evaluate( player, player.EyeAngles, 0.016f, true, false, 0f );
		Check( far.Slow == 1f && far.Pull == Vector2.Zero, "aim assist reached a long way off the target" );
		AimAtCrab( 0.8f );
		await Wait( 0.2f );
		Controls.Force( Scheme.KeyboardMouse );
		var keyboard = AimAssist.Evaluate( player, player.EyeAngles, 0.016f, true, false, 0f );
		Check( keyboard.Slow == 1f && keyboard.Pull == Vector2.Zero, "aim assist works on a keyboard and mouse" );
		Controls.Force( Scheme.Controller );
		GameSettings.AimAssist = 0f;
		var off = AimAssist.Evaluate( player, player.EyeAngles, 0.016f, true, false, 0f );
		Check( off.Slow == 1f && off.Pull == Vector2.Zero, "aim assist ignored its off setting" );
		GameSettings.AimAssist = 0.7f;

		// What it does to the aim: a gentle stick push away from the crab still ends up nearer it with assist, and further without.
		async Task<float> PushAway( float strength )
		{
			GameSettings.AimAssist = strength;
			AimAtCrab( 2f );
			await Wait( 0.15f );   // the camera follows the eyes a frame later
			return AngleTo( mark );
		}
		var before1 = await PushAway( 0.7f );
		Axis( JoyAxis.RightX, -0.25f );   // left, which is away from where the crab is (it is to the right of the aim)
		await Wait( 0.7f );
		Axis( JoyAxis.RightX, 0f );
		await Wait( 0.15f );
		var withAssist = AngleTo( mark );
		var before2 = await PushAway( 0f );
		Axis( JoyAxis.RightX, -0.25f );
		await Wait( 0.7f );
		Axis( JoyAxis.RightX, 0f );
		await Wait( 0.15f );
		var without = AngleTo( mark );
		GD.Print( $"[autotest] aim: a stick push away left the crab {withAssist:0.0} deg off with assist (from {before1:0.0}) and {without:0.0} without (from {before2:0.0})" );
		Check( withAssist - before1 < without - before2 - 1.5f, "aim assist did not hold the aim on the crab against a stick push" );
		// Across the crab at full speed it slows: less turn in the same time.
		float Sweep( float strength )
		{
			GameSettings.AimAssist = strength;
			AimAtCrab( 0f );
			return player.EyeAngles.Yaw;
		}
		async Task<float> SweepTurn( float strength )
		{
			var total = 0f;
			for ( var run = 0; run < 2; run++ )
			{
				var from = Sweep( strength );
				await Wait( 0.15f );
				Axis( JoyAxis.RightX, 1f );
				await Wait( 0.25f );
				Axis( JoyAxis.RightX, 0f );
				total += MathF.Abs( Mathf.Wrap( player.EyeAngles.Yaw - from, -180f, 180f ) );
				await Wait( 0.1f );
			}
			return total / 2f;
		}
		var slowed = await SweepTurn( 0.7f );
		var free = await SweepTurn( 0f );
		GD.Print( $"[autotest] aim: a full sweep over the crab turned {slowed:0.0} deg with assist and {free:0.0} without" );
		Check( slowed < free - 1f, "aim assist does not slow the stick across a target" );
		GameSettings.AimAssist = 0.7f;
		crab.QueueFree();
		await Wait( 0.3f );

		// ---- the dev menu by pad ----
		player.EyeAngles = new Angles( 0f, 0f, 0f );
		await Wait( 0.3f );
		Pad( JoyButton.Start, true ); await Wait( 0.1f ); Pad( JoyButton.Start, false ); await Wait( 0.4f );
		Check( PauseMenu.IsOpen, "Start did not pause" );
		for ( var i = 0; i < 16 && GetViewport().GuiGetFocusOwner()?.Name != "Dev menu"; i++ ) await Tap( JoyButton.DpadDown );
		Check( GetViewport().GuiGetFocusOwner()?.Name == "Dev menu", "the D-pad could not reach the Dev menu button" );
		await Tap( JoyButton.A );
		var menu = GetTree().Root.FindChild( "DevMenu", true, false ) as DevMenu;
		await Wait( 0.4f );
		var first = menu.FocusedChip;
		GD.Print( $"[autotest] menu: A on Dev menu opened it {DevMenu.IsOpen}; the pad is on {first}; pause menu still open {PauseMenu.IsOpen}" );
		Check( DevMenu.IsOpen && !PauseMenu.IsOpen, "A on the Dev menu button did not open the dev menu" );
		Check( first is not null, "the dev menu did not put the pad on a button" );
		Check( GetTree().Paused, "the game ran behind the dev menu" );

		// Move, press, and the pad stays on the button after the chips are rebuilt.
		await Tap( JoyButton.DpadDown );
		var second = menu.FocusedChip;
		await Tap( JoyButton.DpadUp );
		Check( second is not null && second != first, "the D-pad did not move through the dev menu" );
		var paused = director.SpawningPaused;
		await Tap( JoyButton.A );
		await Wait( 0.3f );
		GD.Print( $"[autotest] menu: A on {first} flipped wave spawning paused {paused} -> {director.SpawningPaused}; the pad is on {menu.FocusedChip}" );
		Check( director.SpawningPaused != paused, "A on a chip did not press it" );
		Check( menu.FocusedChip is { } kept && kept.StartsWith( "Spawning" ), "the pad lost its place when the chips rebuilt" );
		await Tap( JoyButton.A );

		// Bumpers change tab; the right stick scrolls.
		var tab0 = menu.Tab;
		await Tap( JoyButton.RightShoulder );
		var tab1 = menu.Tab;
		await Tap( JoyButton.LeftShoulder );
		GD.Print( $"[autotest] menu: right bumper went from {tab0} to {tab1}, left bumper back to {menu.Tab}" );
		Check( tab1 != tab0 && menu.Tab == tab0, "the bumpers do not change tabs" );
		Check( menu.FocusedChip is not null, "no button focused after changing tab" );
		var scrolled0 = menu.ScrollPosition;
		Axis( JoyAxis.RightY, 1f );
		await Wait( 0.5f );
		Axis( JoyAxis.RightY, 0f );
		GD.Print( $"[autotest] menu: the right stick scrolled the menu from {scrolled0} to {menu.ScrollPosition}" );
		Check( menu.ScrollPosition > scrolled0, "the right stick does not scroll the dev menu" );

		// B closes it and the game carries on.
		await Tap( JoyButton.B );
		Check( !DevMenu.IsOpen && !PauseMenu.IsOpen && !GetTree().Paused, "B did not close the dev menu" );

		// Spawn an enemy: open it, press Spawn on the crab's row, aim, pull the right trigger.
		menu.SetOpen( true );
		await Wait( 0.5f );
		var crabRow = menu.FindChildren( "*", "HFlowContainer", true, false ).OfType<HFlowContainer>().FirstOrDefault( r => r.GetChildren().OfType<Label>().Any( l => l.Text == "Junk Crab" ) );
		var spawn = crabRow?.GetChildren().OfType<Godot.Button>().FirstOrDefault( b => b.Text == "Spawn" );
		Check( spawn is not null, "no Spawn button on the Junk Crab row" );
		spawn?.GrabFocus();
		await Wait( 0.2f );
		await Tap( JoyButton.A );
		GD.Print( $"[autotest] menu: Spawn on the crab row closed the menu {!DevMenu.IsOpen}, placing {DevFlags.Placing}" );
		Check( !DevMenu.IsOpen && DevFlags.Placing, "Spawn did not leave the game ready to place" );
		await Wait( 0.4f );
		player.EyeAngles = new Angles( 25f, 0f, 0f );
		await Wait( 0.3f );
		var crabs = JunkCrab.All.Count;
		Axis( JoyAxis.TriggerRight, 1f );
		await Wait( 0.15f );
		Axis( JoyAxis.TriggerRight, 0f );
		await Wait( 0.5f );
		GD.Print( $"[autotest] menu: the right trigger placed a crab: {crabs} -> {JunkCrab.All.Count}, still placing {DevFlags.Placing}" );
		Check( JunkCrab.All.Count == crabs + 1 && !DevFlags.Placing, "the right trigger did not place the enemy" );
		// And B backs out of placing.
		menu.SetOpen( true );
		await Wait( 0.4f );
		spawn = menu.FindChildren( "*", "HFlowContainer", true, false ).OfType<HFlowContainer>().First( r => r.GetChildren().OfType<Label>().Any( l => l.Text == "Junk Crab" ) ).GetChildren().OfType<Godot.Button>().First( b => b.Text == "Spawn" );
		spawn.GrabFocus();
		await Wait( 0.2f );
		await Tap( JoyButton.A );
		Check( DevFlags.Placing, "could not start placing a second time" );
		await Wait( 0.3f );
		await Tap( JoyButton.B );
		Check( !DevFlags.Placing && JunkCrab.All.Count == crabs + 1, "B did not cancel placing" );
		foreach ( var c in JunkCrab.All.ToList() ) c.QueueFree();
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> The dock radio (issue #42): three songs found, shuffled with no repeats, volume slider, plays on after the dive, quieter far from it on the dock. </summary>
	async Task RadioTest()
	{
		DevFlags.FreezeEnemies = true;
		var radio = Radio.Instance;
		Check( radio is not null, "no radio" );
		GD.Print( $"[autotest] radio: songs found {radio.TrackCount}: {string.Join( ", ", radio.Titles )}" );
		Check( radio.TrackCount >= 3, "the radio did not find the three songs" );
		var prop = GetTree().Root.FindChild( "Radio Prop", true, false ) as Node3D;
		Check( prop is not null && Radio.Spot is not null, "the dock has no radio on its table" );
		Check( !radio.Playing, "the radio started on its own in a scripted test" );
		// A look at it: on its table in the hut, then up close.
		player.GlobalPosition = prop.GlobalPosition + new Vector3( 1.3f, -0.66f + 0.1f, -0.2f );
		player.Velocity = Vector3.Zero;
		await Wait( 0.6f );
		LookAt( prop.GlobalPosition + Vector3.Up * 0.12f );
		await Wait( 0.3f );
		await Shot( "radio_in_hut" );
		player.GlobalPosition = prop.GlobalPosition + new Vector3( 0.55f, -0.66f + 0.1f, 0f );
		await Wait( 0.5f );
		LookAt( prop.GlobalPosition + Vector3.Up * 0.12f );
		await Wait( 0.3f );
		await Shot( "radio_close" );
		radio.Start();
		await Wait( 0.5f );
		Check( radio.Playing && Radio.NowPlaying is not null, "the radio did not start playing" );
		GD.Print( $"[autotest] radio: playing {Radio.NowPlaying}" );

		// Shuffle: through 3 full rounds every song comes up each round, and never the same song twice in a row.
		var order = new List<string> { Radio.NowPlaying };
		for ( var i = 0; i < 8; i++ ) { radio.Next(); order.Add( Radio.NowPlaying ); }
		GD.Print( $"[autotest] radio: order {string.Join( " > ", order )}" );
		var runs = Enumerable.Range( 1, order.Count - 1 ).Count( i => order[i] == order[i - 1] );
		Check( runs == 0, "the radio played the same song twice running" );
		for ( var round = 0; round < 3; round++ )
			Check( order.Skip( round * 3 ).Take( 3 ).Distinct().Count() == 3, $"round {round + 1} of the shuffle repeated a song before playing them all" );
		Check( order.Distinct().Count() == radio.TrackCount, "the shuffle does not use every song" );

		// Volume: the music has its own setting and bus, separate from the master volume.
		var bus = AudioServer.GetBusIndex( Radio.MusicBus );
		GameSettings.MusicVolume = 0.5f;
		GameSettings.Apply();
		var half = AudioServer.GetBusVolumeDb( bus );
		GameSettings.MusicVolume = 0f;
		GameSettings.Apply();
		var muted = AudioServer.IsBusMute( bus );
		GameSettings.MusicVolume = 1f;
		GameSettings.Apply();
		var full = AudioServer.GetBusVolumeDb( bus );
		GD.Print( $"[autotest] radio: music bus {half:0.0} dB at 50%, muted at 0% {muted}, {full:0.0} dB at 100%" );
		Check( half < -5f && half > -7f && muted && MathF.Abs( full ) < 0.1f, "the music volume does not drive the music bus" );
		GameSettings.MusicVolume = 0.6f;
		GameSettings.Apply();

		// On the dock it is quieter far from the radio; after the dive it is steady.
		DockHouse.Current?.SkipDive();
		await Wait( 0.1f );
		var dockHouse = DockHouse.Current;
		Check( !DockHouse.WeaponsHeld, "the dive was not skipped" );
		await Wait( 1.2f );
		var after = Radio.Gain;
		GD.Print( $"[autotest] radio: after the dive the gain is {after:0.00}" );
		Check( after > 0.95f && radio.Playing, "the radio did not carry on after the dive" );
		var title = Radio.NowPlaying;
		GetTree().Paused = true;
		await Wait( 0.4f );
		Check( radio.Playing, "the radio stopped while paused" );
		GetTree().Paused = false;
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> The sea floor terrain (issue #35): bigger, hills and valleys, the basin flat, the navmesh across it; views for review. </summary>
	async Task TerrainTest()
	{
		DevFlags.FreezeEnemies = true;
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		Check( SeaTerrain.Active, "the water level did not build its terrain" );
		var hi = float.MinValue; var lo = float.MaxValue;
		for ( var x = -SeaTerrain.HalfSize + 25f; x <= SeaTerrain.HalfSize - 25f; x += 4f )
			for ( var z = -SeaTerrain.HalfSize + 25f; z <= SeaTerrain.HalfSize - 25f; z += 4f )
			{
				var h = SeaTerrain.HeightAt( x, z );
				hi = MathF.Max( hi, h ); lo = MathF.Min( lo, h );
			}
		var basin = 0f;
		for ( var a = 0f; a < MathF.Tau; a += 0.3f ) basin = MathF.Max( basin, MathF.Abs( SeaTerrain.HeightAt( MathF.Cos( a ) * 26f, MathF.Sin( a ) * 26f ) ) );
		GD.Print( $"[autotest] terrain: {2 * SeaTerrain.HalfSize:0} m across, hills up to {hi:0.0} m, valleys down to {lo:0.0} m, basin within {basin:0.00} m" );
		Check( hi > 6f && lo < -4f, "the terrain has no real hills and valleys" );
		Check( basin < 0.6f, "the middle is not flat enough for the stations and the Kraken ring" );
		// The navmesh reaches far out over the hills.
		var far = 0;
		foreach ( var a in new[] { 0f, 1.6f, 3.1f, 4.7f } )
		{
			var want = new Vector3( MathF.Cos( a ) * 75f, 0f, MathF.Sin( a ) * 75f );
			want.Y = SeaTerrain.HeightAt( want );
			var got = Nav.Closest( player, want );
			var path = NavigationServer3D.MapGetPath( Nav.Map( player ), Vector3.Zero, got, true );
			if ( got.DistanceTo( want ) < 2f && path.Length > 1 && path[^1].DistanceTo( got ) < 1f ) far++;
			else GD.Print( $"[autotest] terrain: spot {want} -> navmesh {got} ({got.DistanceTo( want ):0.0} m off), path ends {(path.Length > 0 ? path[^1] : Vector3.Zero)}" );
		}
		var steep = 0; var total = 0;
		for ( var x = -80f; x <= 80f; x += 1f )
			for ( var z = -80f; z <= 80f; z += 1f )
			{
				var dx = SeaTerrain.HeightAt( x + 0.5f, z ) - SeaTerrain.HeightAt( x - 0.5f, z );
				var dz = SeaTerrain.HeightAt( x, z + 0.5f ) - SeaTerrain.HeightAt( x, z - 0.5f );
				total++; if ( MathF.Sqrt( dx * dx + dz * dz ) > MathF.Tan( MathX.Rad( 38f ) ) ) steep++;
			}
		GD.Print( $"[autotest] terrain: {100f * steep / total:0.0}% of the ground is steeper than 38 degrees" );
		GD.Print( $"[autotest] terrain: {far} of 4 far spots on the hills are reachable on the navmesh" );
		Check( far >= 3, "the navmesh does not reach out over the hills" );
		Check( steep * 100 < total * 15, "too much of the ground is too steep to walk" );

		foreach ( var n in GetTree().Root.FindChildren( "*", "", true, false ) )
			if ( n is CanvasLayer cl ) cl.Visible = false;
		var shotAt = Transform3D.Identity;
		player.CameraOverride = _ => shotAt;
		foreach ( var (name, at, look) in new[] {
			("terrain_overview", new Vector3( 0f, 70f, 95f ), new Vector3( 0f, 0f, -10f )),
			("terrain_basin", new Vector3( 4f, 2.2f, 6f ), new Vector3( 40f, 2f, -40f )),
			("terrain_hills", new Vector3( 55f, SeaTerrain.HeightAt( 55f, 30f ) + 3f, 30f ), new Vector3( 80f, SeaTerrain.HeightAt( 80f, -20f ), -20f )),
			("terrain_rim", new Vector3( -60f, SeaTerrain.HeightAt( -60f, -60f ) + 4f, -60f ), new Vector3( -105f, 8f, -100f )) } )
		{
			shotAt = new Transform3D( Basis.LookingAt( (look - at).Normalized(), Vector3.Up ), at );
			await Wait( 0.4f );
			await Shot( name );
		}
		player.CameraOverride = null;
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
