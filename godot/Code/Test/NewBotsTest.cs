using Godot;

/// <summary>
/// --hookbot and --coralbot: the two newer robots. Hook-Bot: the grab reel lifts, slams and throws a light crab (a heavy
/// one only takes the beam), Deep Quiver, Depth Charge, the lure as the decoy (zaps, bursts, Abyssal Bloom's jaw), the
/// grapple zip and its boot, Leviathan Pull. Coral-Bot: spines grow coral that shatters, Bleach, Fast Growth, a turret
/// that shoots, a wall that blocks shots, Living Reef's veins, Healing Reef, the burrow (untouchable, eye at the sand,
/// the eruption) and Great Barrier. Both print what single hits do to early and late crabs, to keep an eye on balance.
/// With --shots (a window) a picture of each.
/// </summary>
public partial class AutoTest
{
	(Vector3 Spot, Vector3 Ahead, Vector3 Side) BotArena()
	{
		var (spot, yaw) = OpenLane( 18f );
		var ahead = new Vector3( -MathF.Sin( MathX.Rad( yaw ) ), 0f, -MathF.Cos( MathX.Rad( yaw ) ) );
		return (spot, ahead, ahead.Cross( Vector3.Up ).Normalized());
	}

	/// <summary> Turns the view onto a point (after a teleport, wait a frame first so the camera has caught up). </summary>
	void AimAt( Vector3 at )
	{
		var to = (at - player.Camera.GlobalPosition).Normalized();
		player.EyeAngles = new Angles( MathX.Deg( -MathF.Asin( to.Y ) ), MathX.Deg( MathF.Atan2( -to.X, -to.Z ) ), 0f );
	}

	/// <summary> A look at the robot from the front in third person, gun in hand (shots only). </summary>
	async Task BotPortrait( string name )
	{
		if ( shots is null ) return;
		player.ThirdPerson = true;
		await Wait( 0.4f );
		var hidden = GetTree().Root.FindChildren( "*", "", true, false ).OfType<CanvasLayer>().Where( c => c.Visible ).ToList();
		foreach ( var cl in hidden ) cl.Visible = false;
		var body = player.GlobalPosition + Vector3.Up * 1f;
		var face = new Angles( 0f, player.EyeAngles.Yaw, 0f ).Forward;
		var from = body + face * 2.2f + face.Cross( Vector3.Up ) * 0.9f + Vector3.Up * 0.3f;
		var at = new Transform3D( Basis.LookingAt( (body - from).Normalized(), Vector3.Up ), from );
		player.CameraOverride = _ => at;
		await Wait( 0.3f );
		await Shot( name );
		player.CameraOverride = null;
		foreach ( var cl in hidden ) cl.Visible = true;
		player.ThirdPerson = false;
		await Wait( 0.2f );
	}

	async Task BotSetup( RobotClass robot )
	{
		DevFlags.FreezeEnemies = true;
		if ( WaveDirector.Current is { } director ) director.SpawningPaused = true;
		foreach ( var laid in SeaMine.All.ToList() ) laid.QueueFree();
		RobotClass.Pick( robot, player );
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 0.6f );
	}

	async Task HookBotTest()
	{
		await BotSetup( RobotClass.HookBot );
		var me = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		var skills = PlayerSkills.Of( player );
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var jet = player.GetNode<Jetpack>( "Jetpack" );
		var harpoon = inventory.Weapons.FirstOrDefault( w => w.Grabber );
		var lure = inventory.Weapons.FirstOrDefault( w => w.Lure );
		Check( harpoon is not null && lure is not null, "Hook-Bot has no grab reel or lantern lure" );
		Check( RobotClass.All.Length == 5 && me.Health.MaxHealth == RobotClass.HookBot.BaseHealth, $"Hook-Bot's health is {me.Health.MaxHealth}, not {RobotClass.HookBot.BaseHealth}" );
		if ( harpoon is null || lure is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		// The picker fits all five robots on screen.
		var picker = ClassPicker.Find( GetTree() );
		picker?.SetOpen( true );
		await Wait( 0.3f );
		var cards = picker?.FindChildren( "Pick", "Button", true, false ).OfType<Control>().ToList() ?? new();
		var screen = GetViewport().GetVisibleRect();
		GD.Print( $"[autotest] hookbot: the picker shows {cards.Count} robots, all on screen {cards.All( c => screen.Encloses( c.GetGlobalRect() ) )}" );
		Check( cards.Count == 5 && cards.All( c => screen.Encloses( c.GetGlobalRect() ) ), "the robot picker does not fit five robots on screen" );
		if ( shots is not null ) await Shot( "class_picker5" );
		picker?.SetOpen( false );
		await Wait( 0.2f );
		var (spot, ahead, side) = BotArena();
		void Stand() { player.GlobalPosition = spot + Vector3.Up * 0.1f; player.Velocity = Vector3.Zero; }
		var crabs = new List<JunkCrab>();
		JunkCrab Crab( Vector3 at, float hp ) { var c = JunkCrab.Spawn( GetTree().CurrentScene, Nav.Closest( player, at ), hp ); crabs.Add( c ); return c; }
		void ClearCrabs() { foreach ( var c in crabs ) if ( IsInstanceValid( c ) ) c.QueueFree(); crabs.Clear(); }

		// ---- The Grab Reel: lift a light crab, slam it into the floor, throw it into another, let go of one ----
		var grab = harpoon;
		inventory.Select( grab, force: true );
		await Wait( 0.5f );
		player.GlobalPosition = spot + Vector3.Up * 0.1f;
		await BotPortrait( "hookbot_portrait" );
		Stand();
		var crab = Crab( spot + ahead * 7f, 400f );
		await Wait( 0.8f );
		Stand();
		await Wait( 0.1f );
		AimAt( crab.GlobalPosition + Vector3.Up * 0.35f );
		await Wait( 0.2f );
		AimAt( crab.GlobalPosition + Vector3.Up * 0.35f );
		await Wait( 0.1f );
		var floorY = crab.GlobalPosition.Y;
		var before = crab.Health.Current;
		grab.ForceGrab = true;
		await Wait( 0.15f );
		AimAt( player.Camera.GlobalPosition + ahead * 6f + Vector3.Up * 1.5f );   // lift it up in front
		await Wait( 0.6f );
		var lifted = crab.GlobalPosition.Y - floorY;
		GD.Print( $"[autotest] hookbot: grabs {HookFx.Grabs}, held {grab.Held is not null}, lifted {lifted:0.0} m, {(crab.GlobalPosition - player.GlobalPosition).Length():0.0} m from the player" );
		Check( HookFx.Grabs == 1 && grab.Held?.Victim == crab && crab.Grabbed, "the grab reel did not take hold of a light crab" );
		Check( lifted > 0.8f, $"the held crab only rose {lifted:0.00} m" );
		if ( shots is not null ) await Shot( "grab_held" );
		// Swing it down into the sand.
		AimAt( player.Camera.GlobalPosition + ahead * 3f - Vector3.Up * 3f );
		await Wait( 0.5f );
		var slamLoss = before - crab.Health.Current;
		GD.Print( $"[autotest] hookbot: slams {HookFx.GrabSlams}, the crab lost {slamLoss:0} of {before:0}" );
		Check( HookFx.GrabSlams >= 1 && slamLoss > 20f, "swinging the held crab into the floor did not slam it" );
		Check( !crab.IsDead, "a 400 health crab died to one slam" );
		// Throw it at another crab.
		var pin = Crab( spot + ahead * 11f, 400f );
		await Wait( 0.8f );
		Stand();
		AimAt( pin.GlobalPosition + Vector3.Up * 0.4f );
		await Wait( 0.35f );
		var pinBefore = pin.Health.Current;
		var charges = grab.Ammo;
		grab.TestThrow();
		await Wait( 0.1f );
		grab.ForceGrab = false;
		for ( var i = 0; i < 60 && HookFx.Landings == 0; i++ ) await Wait( 0.05f );
		await Wait( 0.2f );
		GD.Print( $"[autotest] hookbot: throws {HookFx.Throws}, bowled {HookFx.Bowled}, landings {HookFx.Landings}; charges {charges} -> {grab.Ammo}; the crab hit lost {pinBefore - pin.Health.Current:0}; the thrown one {(IsInstanceValid( crab ) && !crab.IsDead ? $"{crab.Health.Current:0} hp, {crab.GlobalPosition.Y - floorY:0.00} m off the floor" : "dead")}" );
		Check( HookFx.Throws == 1 && grab.Ammo == charges - 1, "right click did not throw the held crab for a charge" );
		Check( HookFx.Bowled >= 1 && pin.Health.Current < pinBefore, "the thrown crab did not bowl into the other" );
		Check( HookFx.Landings >= 1 && (!IsInstanceValid( crab ) || crab.IsDead || (!crab.Grabbed && MathF.Abs( crab.GlobalPosition.Y - floorY ) < 1f)), "the thrown crab did not land back on the floor" );
		ClearCrabs();
		// Let go of one: it is flung, not thrown.
		var loose = Crab( spot + ahead * 6f, 400f );
		await Wait( 0.8f );
		Stand();
		AimAt( loose.GlobalPosition + Vector3.Up * 0.35f );
		await Wait( 0.1f );
		grab.ForceGrab = true;
		await Wait( 0.4f );
		grab.ForceGrab = false;
		await Wait( 0.1f );
		for ( var i = 0; i < 60 && loose.Grabbed; i++ ) await Wait( 0.05f );
		Check( HookFx.Flings == 1 && !loose.Grabbed, "letting go did not fling the crab and set it down" );
		ClearCrabs();

		// A heavy one can't be lifted: the beam locks on and hurts it.
		var heavy = Crab( spot + ahead * 8f, 3000f );
		await Wait( 0.8f );
		Stand();
		await Wait( 0.1f );
		AimAt( heavy.GlobalPosition + Vector3.Up * 0.35f );
		await Wait( 0.2f );
		AimAt( heavy.GlobalPosition + Vector3.Up * 0.35f );
		await Wait( 0.1f );
		var heavyAt = heavy.GlobalPosition;
		grab.ForceGrab = true;
		await Wait( 1.5f );
		grab.ForceGrab = false;
		GD.Print( $"[autotest] hookbot: beams {HookFx.Beams}, beam damage {HookFx.BeamDamage:0}; the heavy crab moved {(heavy.GlobalPosition - heavyAt).Flat().Length():0.00} m, health {heavy.Health.Current:0}" );
		Check( HookFx.Beams == 1 && !heavy.Grabbed && (heavy.GlobalPosition - heavyAt).Flat().Length() < 0.5f, "a heavy crab was lifted" );
		Check( heavy.Health.Current < 3000f - 40f, "the beam did not hurt the heavy crab" );
		ClearCrabs();

		// ---- Skills: Deep Quiver, Depth Charge ----
		skills.Set( SkillId.DeepQuiver, 3 );
		skills.Set( SkillId.DepthCharge, 3 );
		await Wait( 0.3f );
		Check( grab.MagazineSize == 6, $"Deep Quiver 3 gave {grab.MagazineSize} throw charges, not 6" );
		var bomb = Crab( spot + ahead * 6f, 400f );
		var target = Crab( spot + ahead * 12f, 2000f );
		var nextTo = Crab( spot + ahead * 12f + side * 1.6f, 2000f );
		await Wait( 0.8f );
		Stand();
		AimAt( bomb.GlobalPosition + Vector3.Up * 0.35f );
		await Wait( 0.1f );
		grab.InfiniteAmmo = true;
		grab.ForceGrab = true;
		await Wait( 0.5f );
		AimAt( target.GlobalPosition + Vector3.Up * 0.4f );
		await Wait( 0.3f );
		var nextBefore = nextTo.Health.Current;
		grab.TestThrow();
		await Wait( 0.1f );
		grab.ForceGrab = false;
		for ( var i = 0; i < 60 && HookFx.DepthCharges == 0; i++ ) await Wait( 0.05f );
		await Wait( 0.2f );
		GD.Print( $"[autotest] hookbot: depth charges {HookFx.DepthCharges}; the crab beside the target lost {nextBefore - nextTo.Health.Current:0}" );
		Check( HookFx.DepthCharges == 1 && nextTo.Health.Current < nextBefore, "Depth Charge did not burst where the thrown crab hit" );
		grab.InfiniteAmmo = false;
		ClearCrabs();

		// ---- The lure: the decoy, its zaps, the burst, Abyssal Bloom ----
		inventory.Select( lure, force: true );
		await Wait( 0.5f );
		Stand();
		var bait = Crab( spot + ahead * 7f, 2000f );
		await Wait( 0.8f );
		Stand();
		await Wait( 0.1f );
		AimAt( spot + ahead * 7f + Vector3.Up * 1.5f );
		lure.TestFire();
		for ( var i = 0; i < 60 && lure.ActiveLure is not { Planted: true }; i++ ) await Wait( 0.05f );
		var planted = lure.ActiveLure;
		await Wait( 1.2f );
		var decoyed = planted is not null && me.WorldPosition.DistanceTo( planted.GlobalPosition ) < 0.01f;
		GD.Print( $"[autotest] hookbot: lure planted {planted is not null} {(planted is null ? 0f : planted.GlobalPosition.DistanceTo( spot )):0.0} m out, enemies aim at it {decoyed}, zaps {HookFx.Zaps}, bait crab {bait.Health.Current:0}" );
		Check( decoyed, "enemies do not go for the planted lure" );
		Check( HookFx.Zaps >= 1, "the lure zapped nothing near it" );
		if ( shots is not null ) await Shot( "hook_lure" );
		var baitBefore = bait.Health.Current;
		lure.TestBurstLure();
		await Wait( 0.2f );
		Check( HookFx.Bursts == 1 && me.WorldPosition.DistanceTo( player.GlobalPosition ) < 0.01f, "the lure did not burst, or enemies still go for it" );
		Check( bait.Health.Current < baitBefore, "the lure's burst did not hurt the crab beside it" );
		skills.Set( SkillId.AbyssalBloom, 3 );
		lure.InfiniteAmmo = true;
		await Wait( 0.5f );
		AimAt( bait.GlobalPosition + Vector3.Up * 1.2f );
		await Wait( 0.1f );
		lure.TestFire();
		for ( var i = 0; i < 60 && lure.ActiveLure is not { Planted: true }; i++ ) await Wait( 0.05f );
		baitBefore = bait.Health.Current;
		lure.TestBurstLure();
		await Wait( 0.5f );
		if ( shots is not null ) await Shot( "hook_bloom" );
		await Wait( 0.6f );
		GD.Print( $"[autotest] hookbot: Abyssal Bloom jaws {HookFx.Blooms}, the crab {baitBefore:0} -> {bait.Health.Current:0}" );
		Check( HookFx.Blooms == 1 && baitBefore - bait.Health.Current > HookFx.BloomDamage( 3 ) * 0.9f, "Abyssal Bloom's jaw did not bite the crab" );
		ClearCrabs();

		// ---- The grapple: zip to a crab and boot it; Leviathan Pull ----
		skills.Set( SkillId.LeviathanPull, 2 );
		var mark = Crab( spot + ahead * 12f, 950f );
		var friend = Crab( spot + ahead * 12f + side * 2.5f, 950f );
		await Wait( 0.8f );
		Stand();
		await Wait( 0.1f );
		AimAt( mark.GlobalPosition + Vector3.Up * 0.4f );
		var from = player.GlobalPosition;
		var markHp = mark.Health.Current;
		var friendAt = friend.GlobalPosition;
		await Wait( 0.1f );
		jet.TestGrapple();
		await Wait( 1.2f );
		var moved = (player.GlobalPosition - from).Flat().Length();
		GD.Print( $"[autotest] hookbot: zips {HookFx.Zips}, kicks {HookFx.Kicks}, slams {HookFx.Slams}; the player moved {moved:0.0} m; the crab {markHp:0} -> {mark.Health.Current:0}; the one beside it was pulled {(friend.GlobalPosition - friendAt).Flat().Length():0.0} m" );
		Check( HookFx.Zips == 1 && moved > 7f, "the grapple did not zip the player to the crab" );
		Check( HookFx.Kicks == 1 && HookFx.Slams == 1 && (mark.IsDead || mark.Health.Current < markHp - HookFx.LeviathanDamage( 2 )), "the zip did not end in a boot and Leviathan Pull's slam" );
		Check( (friend.GlobalPosition - friendAt).Flat().Length() > 0.5f, "Leviathan Pull did not drag the crab beside it" );
		ClearCrabs();
		BalanceLine( harpoon );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	async Task CoralBotTest()
	{
		await BotSetup( RobotClass.CoralBot );
		var me = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		var skills = PlayerSkills.Of( player );
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var jet = player.GetNode<Jetpack>( "Jetpack" );
		var gun = inventory.Weapons.FirstOrDefault( w => w.Spines );
		var seeds = inventory.Weapons.FirstOrDefault( w => w.Seeds );
		Check( gun is not null && seeds is not null, "Coral-Bot has no spine gun or reef seeds" );
		if ( gun is null || seeds is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		var (spot, ahead, side) = BotArena();
		void Stand() { player.GlobalPosition = spot + Vector3.Up * 0.1f; player.Velocity = Vector3.Zero; }
		var crabs = new List<JunkCrab>();
		JunkCrab Crab( Vector3 at, float hp ) { var c = JunkCrab.Spawn( GetTree().CurrentScene, Nav.Closest( player, at ), hp ); crabs.Add( c ); return c; }
		void ClearCrabs() { foreach ( var c in crabs ) if ( IsInstanceValid( c ) ) c.QueueFree(); crabs.Clear(); }

		// ---- Spines grow coral; at 12 it shatters ----
		inventory.Select( gun, force: true );
		gun.InfiniteAmmo = true;
		await Wait( 0.5f );
		player.GlobalPosition = spot + Vector3.Up * 0.1f;
		await BotPortrait( "coralbot_portrait" );
		var crab = Crab( spot + ahead * 6f, 3000f );
		await Wait( 0.8f );
		Stand();
		await Wait( 0.1f );
		var hp = crab.Health.Current;
		gun.ForceAim = true;
		var fired = 0;
		var most = 0;
		for ( ; fired < 30 && CoralFx.Shatters == 0; fired++ )
		{
			AimAt( crab.GlobalPosition + Vector3.Up * 0.35f );
			gun.TestFire();
			await Wait( 0.05f );
			most = Math.Max( most, CoralGrowth.Of( crab )?.Stacks ?? 0 );
			if ( most == 9 && shots is not null ) await Shot( "coral_encrusted" );
		}
		await Wait( 0.1f );
		GD.Print( $"[autotest] coralbot: coral grew to {most}, shattered {CoralFx.Shatters} after {fired} spines ({CoralFx.Stacks} landed); the crab lost {hp - crab.Health.Current:0}" );
		Check( most >= CoralFx.ShatterAt( 0 ) - 1 && CoralFx.Shatters == 1 && CoralFx.Stacks == CoralFx.ShatterAt( 0 ), "spines did not grow coral that shatters at 12" );
		Check( hp - crab.Health.Current > 12 * 18f, "the shatter did not add its damage" );
		ClearCrabs();

		// Fast Growth, Bleach: a crab at a quarter health turns to stone.
		skills.Set( SkillId.FastGrowth, 5 );
		skills.Set( SkillId.Bleach, 3 );
		crab = Crab( spot + ahead * 6f, 1000f );
		await Wait( 0.8f );
		Stand();
		crab.Health.ResetTo( 1000f );
		crab.Health.Current = 400f;
		for ( var i = 0; i < 20 && IsInstanceValid( crab ) && !crab.IsDead; i++ ) { AimAt( crab.GlobalPosition + Vector3.Up * 0.35f ); gun.TestFire(); await Wait( 0.05f ); }
		gun.ForceAim = false;
		GD.Print( $"[autotest] coralbot: Fast Growth 5 shatters at {CoralFx.ShatterAt( 5 )}; bleached {CoralFx.Bleached}, the crab dead {!IsInstanceValid( crab ) || crab.IsDead}" );
		Check( CoralFx.Bleached == 1 && (!IsInstanceValid( crab ) || crab.IsDead), "Bleach did not turn a weak crab to stone" );
		ClearCrabs();

		// ---- Reef Seeds: a turret shoots, a wall blocks shots ----
		inventory.Select( seeds, force: true );
		seeds.InfiniteAmmo = true;
		await Wait( 0.5f );
		Stand();
		var target = Crab( spot + ahead * 9f, 2000f );
		await Wait( 0.8f );
		Stand();
		await Wait( 0.1f );
		AimAt( spot + ahead * 4f + side * 2f );
		seeds.TestFire();
		await Wait( 2.2f );
		GD.Print( $"[autotest] coralbot: turrets {CoralFx.Turrets}, shots {CoralFx.TurretShots}; the crab {target.Health.Current:0}" );
		Check( CoralFx.Turrets == 1 && CoralFx.TurretShots >= 3 && target.Health.Current < 2000f, "the polyp turret did not shoot the crab" );
		AimAt( spot + ahead * 4f - side * 2f );
		seeds.TestPlantWall();
		await Wait( 0.6f );
		var wall = ReefPiece.All.OfType<CoralWall>().FirstOrDefault();
		var through = wall is not null && Trace.Ray( player, wall.GlobalPosition + Vector3.Up * 1f - wall.GlobalBasis.Z * 2f, wall.GlobalPosition + Vector3.Up * 1f + wall.GlobalBasis.Z * 2f, Layers.World ).Hit;
		GD.Print( $"[autotest] coralbot: walls {CoralFx.Walls}, a shot through it is stopped {through}" );
		Check( CoralFx.Walls == 1 && through, "the coral wall did not grow, or shots go through it" );
		if ( shots is not null ) { AimAt( spot + ahead * 6f ); await Wait( 0.1f ); await Shot( "coral_reef" ); }

		// Living Reef: a crab between the turret and the wall is hurt by the vein; Healing Reef heals near it.
		skills.Set( SkillId.LivingReef, 3 );
		skills.Set( SkillId.HealingReef, 5 );
		var pieces = ReefPiece.All.Take( 2 ).ToList();
		var middle = pieces.Count == 2 ? (pieces[0].GlobalPosition + pieces[1].GlobalPosition) * 0.5f : spot + ahead * 4f;
		var between = Crab( middle, 2000f );
		await Wait( 1.2f );
		player.GlobalPosition = Nav.Closest( player, spot + ahead * 4f + side * 2.6f ) + Vector3.Up * 0.1f;
		await Wait( 0.1f );
		me.Health.OnDamage( new DamageInfo { Damage = me.Health.Current * 0.5f, Attacker = this, Position = player.GlobalPosition + Vector3.Up } );   // real damage: the usual regen waits
		var low = me.Health.Current;
		await Wait( 1.1f );
		GD.Print( $"[autotest] coralbot: veins {ReefLinks.Pairs}, pieces {string.Join( ", ", ReefPiece.All.Select( p => $"{p.Name} {(p.Heart - between.GlobalPosition).Flat().Length():0.0} m" ) )}; vein hits {CoralFx.VeinHits}; health {low:0} -> {me.Health.Current:0} by the reef" );
		Check( CoralFx.VeinHits >= 1, "Living Reef's vein did not hurt the crab crossing it" );
		Check( me.Health.Current > low + 4f && me.Health.Current < low + 15f, $"Healing Reef healed {me.Health.Current - low:0} in 1.1 s, not about {CoralFx.HealingPer * 5f * 1.1f:0}" );
		ReefPiece.ClearAll();
		ClearCrabs();

		// ---- The burrow: untouchable, eye at the sand, the eruption; Great Barrier ----
		skills.Set( SkillId.GreatBarrier, 1 );
		Stand();
		var foe = Crab( spot + ahead * 2f, 2000f );
		await Wait( 0.8f );
		Stand();
		await Wait( 0.3f );
		me.Health.ResetTo( me.Health.MaxHealth );
		jet.TestBurrow();
		await Wait( 0.35f );
		me.Health.OnDamage( new DamageInfo { Damage = 30f, Attacker = this, Position = player.GlobalPosition + Vector3.Up } );
		var untouched = me.Health.Current == me.Health.MaxHealth;
		GD.Print( $"[autotest] coralbot: burrowed {jet.Burrowed}, eye drop {player.EyeDrop:0.00} m, untouched {untouched}" );
		Check( jet.Burrowed && player.EyeDrop > 0.6f && untouched, "the burrow did not dig in (eye at the sand, untouchable)" );
		var foeHp = foe.Health.Current;
		var walls = CoralFx.Walls;
		await Wait( CoralFx.BurrowTime + 0.3f );
		if ( shots is not null ) await Shot( "coral_barrier" );
		GD.Print( $"[autotest] coralbot: eruptions {CoralFx.Eruptions}, barriers {CoralFx.Barriers} ({CoralFx.Walls - walls} walls); the crab {foeHp:0} -> {foe.Health.Current:0}" );
		Check( CoralFx.Eruptions == 1 && !jet.Burrowed, "the burrow did not end in an eruption" );
		Check( foe.Health.Current < foeHp - CoralFx.EruptDamage * 0.5f, "the eruption did not hurt the crab beside it" );
		Check( CoralFx.Barriers == 1 && CoralFx.Walls - walls == 8, "Great Barrier did not raise its ring of walls" );
		ReefPiece.ClearAll();
		ClearCrabs();
		BalanceLine( gun );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> What a weapon's hits come to against a crab on wave 1, 10, 20 and 30 (base damage, no upgrades). </summary>
	void BalanceLine( PlayerWeapon w )
	{
		var director = WaveDirector.Current;
		if ( director is null ) return;
		var waves = new[] { 1, 10, 20, 30 };
		var line = string.Join( ", ", waves.Select( wave => $"wave {wave} crab {director.CrabBaseHealth + director.CrabHealthPerWave * (wave - 1):0} hp = {(director.CrabBaseHealth + director.CrabHealthPerWave * (wave - 1)) / w.Damage:0.0} hits" ) );
		GD.Print( $"[autotest] balance: {w.DisplayName} {w.Damage:0} a hit at {w.FireRate:0.0}/s: {line}" );
	}
}
