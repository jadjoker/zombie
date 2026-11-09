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

	async Task Wait( float seconds )
	{
		await ToSignal( GetTree().CreateTimer( seconds, processAlways: true, processInPhysics: true ), SceneTreeTimer.SignalName.Timeout );
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
