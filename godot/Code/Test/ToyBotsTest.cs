using Godot;

/// <summary>
/// --goobot, --plungerbot and --pinballbot: the three physics robots. Goo-Bot: goo blobs stack on a crab (each
/// harder), a blob restarts the clock, the ball pops and splashes, a 6th blob pops it at once (Big Ball Energy), the
/// jelly springs from crab to crab (Mother Jelly, Volatile), the splat jump glues a crab (Gooquake). Plunger-Bot: plungers stick and pop at three, the yank drags a crab in, the toilet's whirlpool pulls
/// and its geyser throws (Clogged), the sling flings you and Cannonball lands on a crab. Pinball-Bot: a ball bounces
/// off a crab (Multiball splits it), the flippers whack a crab and slap a ball back out (Ramp), the roll bowls a line
/// of crabs over (High Score). Each prints its guns against crabs by wave. With --shots (a window) pictures.
/// </summary>
public partial class AutoTest
{
	sealed class Arena
	{
		public Vector3 Spot, Ahead, Side;
		public readonly List<JunkCrab> Crabs = new();
	}

	async Task<Arena> ToyArena( RobotClass robot, string portrait )
	{
		await BotSetup( robot );
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		if ( inventory.Weapons.FirstOrDefault( w => w.Slot == 1 ) is { } first ) inventory.Select( first, force: true );
		await Wait( 0.5f );
		var (spot, ahead, side) = BotArena();
		var a = new Arena { Spot = spot, Ahead = ahead, Side = side };
		player.GlobalPosition = spot + Vector3.Up * 0.1f;
		await Wait( 0.3f );
		await BotPortrait( portrait );
		return a;
	}

	void Stand( Arena a ) { player.GlobalPosition = a.Spot + Vector3.Up * 0.1f; player.Velocity = Vector3.Zero; }
	JunkCrab Crab( Arena a, Vector3 at, float hp ) { var c = JunkCrab.Spawn( GetTree().CurrentScene, Nav.Closest( player, at ), hp ); a.Crabs.Add( c ); return c; }
	void Clear( Arena a ) { foreach ( var c in a.Crabs ) if ( IsInstanceValid( c ) ) c.QueueFree(); a.Crabs.Clear(); }
	static float Hp( Creature c ) => IsInstanceValid( c ) && !c.IsDead ? c.Health.Current : 0f;

	async Task Face( Arena a, Vector3 at )
	{
		Stand( a );
		await Wait( 0.1f );
		AimAt( at );
		await Wait( 0.1f );
		AimAt( at );
	}

	/// <summary> The end of a run: the balls still out are put away a few frames before the quit. </summary>
	async void Done()
	{
		GD.Print( "[autotest] done" );
		foreach ( var b in BotBall.All.ToList() ) b.End();
		for ( var i = 0; i < 3; i++ ) await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
		GetTree().Quit();
	}

	// ---------------------------------------------------------------- Goo-Bot

	static Vector3 CrabBody( Creature c ) => SkillFx.NearestPart( c, c.GlobalPosition + Vector3.Up * 0.9f );

	async Task GooBotTest()
	{
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var a = await ToyArena( RobotClass.GooBot, "goobot_portrait" );
		var goo = inventory.Weapons.FirstOrDefault( w => w.Goo );
		var jelly = inventory.Weapons.FirstOrDefault( w => w.JellyGun );
		var me = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		Check( goo is not null && jelly is not null, "Goo-Bot has no goo gun or jelly cannon" );
		Check( me.Health.MaxHealth == RobotClass.GooBot.BaseHealth, $"Goo-Bot's health is {me.Health.MaxHealth}" );
		if ( goo is null || jelly is null ) { Done(); return; }
		Check( goo.Ammo == 60 && goo.MagazineSize == 60 && goo.Reserve == 240, $"the goo gun holds {goo.Ammo}/{goo.Reserve}, not 60/240" );
		inventory.Select( goo, force: true );
		await Wait( 0.4f );
		var skills = PlayerSkills.Of( player );
		var jet = player.GetNode<Jetpack>( "Jetpack" );
		if ( shots is not null )
		{
			// The back: the goo tank.
			player.ThirdPerson = true;
			await Wait( 0.3f );
			var body = player.GlobalPosition + Vector3.Up * 1.1f;
			var face = new Angles( 0f, player.EyeAngles.Yaw, 0f ).Forward;
			var eye = body - face * 2f + face.Cross( Vector3.Up ) * 0.8f + Vector3.Up * 0.4f;
			var view = new Transform3D( Basis.LookingAt( (body - eye).Normalized(), Vector3.Up ), eye );
			player.CameraOverride = _ => view;
			await Wait( 0.3f );
			await Shot( "goobot_back" );
			player.CameraOverride = null;
			player.ThirdPerson = false;
			await Wait( 0.3f );
		}

		// ---- Blobs stack on a crab, each harder than the last; another blob restarts the clock; the pop splashes ----
		var crab = Crab( a, a.Spot + a.Ahead * 8f, 3000f );
		var side = Crab( a, a.Spot + a.Ahead * 8f + a.Side * 1.8f, 3000f );
		await Wait( 0.8f );
		await Face( a, CrabBody( crab ) );
		var hits = new List<float>();
		for ( var i = 0; i < 3; i++ )
		{
			var before = Hp( crab );
			AimAt( CrabBody( crab ) );
			await Wait( 0.05f );
			AimAt( CrabBody( crab ) );
			goo.TestGoo();
			for ( var k = 0; k < 20 && GooFx.BlobHits <= i; k++ ) await Wait( 0.03f );
			await Wait( 0.05f );
			hits.Add( before - Hp( crab ) );
		}
		var ball = GooBall.Of( crab );
		GD.Print( $"[autotest] goobot: blobs {GooFx.Blobs}, hits {GooFx.BlobHits}, ball {ball?.Blobs ?? 0}; damage {string.Join( ", ", hits.Select( h => h.ToString( "0" ) ) )}; ammo {goo.Ammo}" );
		Check( GooFx.BlobHits == 3 && ball is { Blobs: 3 }, "three blobs did not build a 3 blob goo ball" );
		Check( hits.Count == 3 && hits[1] > hits[0] && hits[2] > hits[1], "each blob on the ball did not hit harder than the last" );
		Check( goo.Ammo == 30, $"three shots left {goo.Ammo} of 60 (10 a shot)" );
		if ( shots is not null ) await Shot( "goo_ball3" );
		await Wait( ball?.PopTime - 1f ?? 3f );
		Check( GooFx.Pops == 0, "the ball popped before its time" );
		AimAt( CrabBody( crab ) );
		await Wait( 0.05f );
		AimAt( CrabBody( crab ) );
		goo.TestGoo();
		for ( var k = 0; k < 20 && GooFx.BlobHits < 4; k++ ) await Wait( 0.03f );
		var left = GooBall.Of( crab )?.Left ?? 0f;
		Check( left > 3.5f, $"a 4th blob did not restart the clock ({left:0.0} s left)" );
		var crabHp = Hp( crab );
		var sideHp = Hp( side );
		for ( var k = 0; k < 120 && GooFx.Pops == 0; k++ ) await Wait( 0.05f );
		await Wait( 0.1f );
		GD.Print( $"[autotest] goobot: pops {GooFx.Pops}; the crab lost {crabHp - Hp( crab ):0} to the pop, the one beside it {sideHp - Hp( side ):0}" );
		Check( GooFx.Pops == 1, "the goo ball never popped" );
		Check( crabHp - Hp( crab ) >= GooFx.PopPerBlob * 4f * 0.6f, "the pop did not hurt the crab by its 4 blobs" );
		Check( sideHp - Hp( side ) > 0f, "the pop's splash missed the crab beside it" );

		// ---- The 6th blob pops it at once; Big Ball Energy rolls a boulder on ----
		goo.InfiniteAmmo = true;
		skills.Set( SkillId.BigBallEnergy, 1 );
		var hitsBefore = GooFx.BlobHits;
		for ( var i = 0; i < 6; i++ )
		{
			AimAt( CrabBody( crab ) );
			await Wait( 0.05f );
			AimAt( CrabBody( crab ) );
			goo.TestGoo();
			for ( var k = 0; k < 20 && GooFx.BlobHits <= hitsBefore + i; k++ ) await Wait( 0.03f );
			GD.Print( $"[autotest] goobot: shot {i + 1} of 6, the ball has {GooBall.Of( crab )?.Blobs ?? 0}" );
			if ( i == 4 && shots is not null ) await Shot( "goo_ball5" );
		}
		await Wait( 0.1f );
		if ( shots is not null ) await Shot( "goo_bigpop" );
		GD.Print( $"[autotest] goobot: big pops {GooFx.BigPops}, boulders {GooFx.Boulders}, most blobs {GooFx.MostBlobs}" );
		Check( GooFx.BigPops == 1, "6 blobs did not pop the ball at once" );
		Check( GooFx.Boulders == 1, "Big Ball Energy did not roll a boulder" );
		await Wait( 1.5f );
		skills.Set( SkillId.BigBallEnergy, 0 );
		Clear( a );
		foreach ( var b in BotBall.All.ToList() ) b.End();

		// ---- The jelly springs from crab to crab, gooing each ----
		inventory.Select( jelly, force: true );
		jelly.InfiniteAmmo = true;
		await Wait( 0.5f );
		var line = new List<JunkCrab>();
		for ( var i = 0; i < 4; i++ ) line.Add( Crab( a, a.Spot + a.Ahead * (7f + 2.8f * i) + a.Side * (i % 2 == 0 ? 0f : 1.5f), 2000f ) );
		await Wait( 0.8f );
		await Face( a, line[0].GlobalPosition + Vector3.Up * 0.3f );
		var lineHp = line.Select( Hp ).ToList();
		jelly.TestJelly();
		if ( shots is not null ) { await Wait( 0.35f ); await Shot( "jelly_hop" ); }
		for ( var k = 0; k < 60 && GooFx.JellyHits < 4; k++ ) await Wait( 0.05f );
		await Wait( 0.2f );
		var gooed = line.Count( c => GooBall.Of( c ) is not null );
		GD.Print( $"[autotest] goobot: jelly hits {GooFx.JellyHits}, hops {GooFx.JellyHops}, crabs gooed {gooed}; lost {string.Join( ", ", line.Select( ( c, i ) => (lineHp[i] - Hp( c )).ToString( "0" ) ) )}" );
		Check( GooFx.JellyHits >= 3 && GooFx.JellyHops >= 2, "the jelly did not spring on from crab to crab" );
		Check( gooed >= 3, "the jelly's hits did not put goo on the crabs" );
		// Volatile and Mother Jelly.
		skills.Set( SkillId.VolatileJelly, 1 );
		skills.Set( SkillId.MotherJelly, 1 );
		await Wait( 4.5f );
		AimAt( line[0].GlobalPosition + Vector3.Up * 0.3f );
		await Wait( 0.1f );
		jelly.TestJelly();
		for ( var k = 0; k < 80 && GooFx.Volatiles == 0; k++ ) await Wait( 0.05f );
		GD.Print( $"[autotest] goobot: mothers {GooFx.Mothers}, volatiles {GooFx.Volatiles}" );
		Check( GooFx.Mothers == 1, "Mother Jelly did not bring the jelly to life" );
		Check( GooFx.Volatiles == 1, "Volatile did not blow up the jelly's last hit" );
		Clear( a );
		foreach ( var b in BotBall.All.ToList() ) b.End();

		// ---- The splat jump glues and goos a crab; Gooquake ----
		var near = Crab( a, a.Spot + a.Ahead * 2.5f, 600f );
		await Wait( 0.8f );
		Stand( a );
		await Wait( 0.1f );
		var hp = Hp( near );
		var from = player.GlobalPosition;
		jet.TestSplat();
		await Wait( 0.35f );
		if ( shots is not null ) await Shot( "splat_air" );
		var took = 0f;
		for ( var k = 0; k < 120 && GooFx.Splats == 0; k++ ) { await Wait( 0.03f ); took += 0.03f; }
		GD.Print( $"[autotest] goobot: the splat jump took {took + 0.35f:0.0} s and landed {(player.GlobalPosition - from).Flat().Length():0.0} m from the start" );
		await Wait( 0.05f );
		if ( shots is not null ) await Shot( "splat_land" );
		GD.Print( $"[autotest] goobot: splats {GooFx.Splats}; the crab lost {hp - Hp( near ):0}, glued {near.StunnedUntil > Clock.Now}, goo {GooBall.Of( near )?.Blobs ?? 0}" );
		Check( GooFx.Splats == 1 && Hp( near ) < hp, "the splat did not land on and hurt the crab" );
		Check( near.StunnedUntil > Clock.Now && GooBall.Of( near ) is not null, "the splat did not glue and goo the crab" );
		skills.Set( SkillId.Gooquake, 2 );
		await Wait( 1.5f );
		Stand( a );
		hp = Hp( near );
		jet.TestSplat();
		for ( var k = 0; k < 120 && GooFx.Splats == 1; k++ ) await Wait( 0.03f );
		await Wait( 0.5f );
		GD.Print( $"[autotest] goobot: quakes {GooFx.Quakes}; the crab lost {hp - Hp( near ):0}" );
		Check( GooFx.Quakes == 1 && (hp - Hp( near ) > GooFx.QuakeDamage( 2 ) * 0.8f || !IsInstanceValid( near ) || near.IsDead), "Gooquake did not fling and hurt the crab" );
		Clear( a );
		BalanceLine( goo );
		BalanceLine( jelly );
		GD.Print( $"[autotest] balance: a full goo ball is {string.Join( " + ", Enumerable.Range( 1, 6 ).Select( b => goo.BlobDamage( b ).ToString( "0" ) ) )} = {Enumerable.Range( 1, 6 ).Sum( b => goo.BlobDamage( b ) ):0} from its blobs and {GooFx.PopPerBlob * 6:0} from the pop" );
		Done();
	}

	// ---------------------------------------------------------------- Plunger-Bot

	async Task PlungerBotTest()
	{
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var a = await ToyArena( RobotClass.PlungerBot, "plungerbot_portrait" );
		var launcher = inventory.Weapons.FirstOrDefault( w => w.Plunger );
		var flush = inventory.Weapons.FirstOrDefault( w => w.Toilet );
		Check( launcher is not null && flush is not null, "Plunger-Bot has no plunger launcher or flush bomb" );
		if ( launcher is null || flush is null ) { Done(); return; }
		inventory.Select( launcher, force: true );
		launcher.InfiniteAmmo = true;
		await Wait( 0.4f );
		var skills = PlayerSkills.Of( player );
		var jet = player.GetNode<Jetpack>( "Jetpack" );

		// ---- Plungers stick, and pop off at three ----
		var crab = Crab( a, a.Spot + a.Ahead * 8f, 2000f );
		await Wait( 0.8f );
		await Face( a, crab.GlobalPosition + Vector3.Up * 0.4f );
		var hp = Hp( crab );
		launcher.TestFire();
		await Wait( 0.4f );
		var one = StuckPlunger.On( crab );
		launcher.TestFire();
		await Wait( 0.4f );
		if ( shots is not null ) await Shot( "plunger_stuck" );
		launcher.TestFire();
		await Wait( 0.4f );
		GD.Print( $"[autotest] plungerbot: fired {PlungerFx.Plungers}, stuck {PlungerFx.Stuck} (one after the first: {one}), pops {PlungerFx.Pops}, on it now {StuckPlunger.On( crab )}; the crab lost {hp - Hp( crab ):0}" );
		Check( one == 1 && PlungerFx.Stuck == 3, "the plungers did not stick to the crab" );
		Check( PlungerFx.Pops == 1 && StuckPlunger.On( crab ) == 0, "three plungers did not pop off together" );
		Check( hp - Hp( crab ) > PlungerFx.PopDamage + 60f && hp - Hp( crab ) < 400f, $"three plungers and a pop did {hp - Hp( crab ):0}" );
		Clear( a );
		// The yank drags a crab in.
		var far = Crab( a, a.Spot + a.Ahead * 9f, 600f );
		await Wait( 0.8f );
		await Face( a, far.GlobalPosition + Vector3.Up * 0.4f );
		launcher.TestFire();
		await Wait( 0.35f );
		launcher.TestFire();
		await Wait( 0.45f );
		var before = (far.GlobalPosition - player.GlobalPosition).Flat().Length();
		launcher.Unclog();
		await Wait( 0.2f );
		var after = (far.GlobalPosition - player.GlobalPosition).Flat().Length();
		GD.Print( $"[autotest] plungerbot: yanks {PlungerFx.Yanks}; the crab came from {before:0.0} m to {after:0.0} m" );
		Check( PlungerFx.Yanks == 2 && after < before - 1f, "the yank did not drag the crab in" );
		Clear( a );

		// ---- The Flush Bomb: the whirlpool pulls, the geyser throws; Clogged ----
		inventory.Select( flush, force: true );
		flush.InfiniteAmmo = true;
		await Wait( 0.5f );
		await Face( a, a.Spot + a.Ahead * 7f + Vector3.Up * 1.5f );
		flush.TestFire();
		for ( var i = 0; i < 60 && flush.ActiveBomb is not { Landed: true }; i++ ) await Wait( 0.05f );
		var bomb = flush.ActiveBomb;
		Check( bomb is not null, "the toilet did not land" );
		if ( bomb is null ) { Done(); return; }
		var swirled = new[] { Crab( a, bomb.GlobalPosition + a.Side * 3.5f, 600f ), Crab( a, bomb.GlobalPosition - a.Side * 3.5f, 600f ) };
		await Wait( 0.1f );
		var starts = swirled.Select( c => (c.GlobalPosition - bomb.GlobalPosition).Flat().Length() ).ToList();
		await Wait( 1f );
		if ( shots is not null ) await Shot( "plunger_flush" );
		var mid = swirled.Select( c => IsInstanceValid( c ) ? (c.GlobalPosition - bomb.GlobalPosition).Flat().Length() : 0f ).ToList();
		for ( var i = 0; i < 60 && PlungerFx.Flushes == 0; i++ ) await Wait( 0.05f );
		await Wait( 0.1f );
		GD.Print( $"[autotest] plungerbot: toilets {PlungerFx.Toilets}, flushes {PlungerFx.Flushes}, geysers {PlungerFx.Geysers}; crabs {string.Join( ", ", starts.Select( d => d.ToString( "0.0" ) ) )} m -> {string.Join( ", ", mid.Select( d => d.ToString( "0.0" ) ) )} m from the bowl, health {string.Join( ", ", swirled.Select( c => Hp( c ).ToString( "0" ) ) )}" );
		Check( mid.Zip( starts ).All( p => p.First < p.Second - 0.8f ), "the whirlpool did not pull the crabs in" );
		Check( PlungerFx.Geysers == 1 && swirled.All( c => Hp( c ) < 600f - PlungerFx.GeyserDamage ), "the geyser did not hit the crabs in the bowl" );
		Clear( a );
		skills.Set( SkillId.Clogged, 2 );
		await Wait( 0.3f );
		await Face( a, a.Spot + a.Ahead * 7f + Vector3.Up * 1.5f );
		flush.TestFire();
		for ( var i = 0; i < 60 && flush.ActiveBomb is not { Landed: true }; i++ ) await Wait( 0.05f );
		var clogged = Crab( a, flush.ActiveBomb.GlobalPosition + a.Side * 1.5f, 600f );
		await Wait( 0.3f );
		hp = Hp( clogged );
		flush.ActiveBomb?.Flush();
		await Wait( 0.2f );
		GD.Print( $"[autotest] plungerbot: clogs {PlungerFx.Clogs}; the crab lost {hp - Hp( clogged ):0}" );
		Check( PlungerFx.Clogs == 1 && hp - Hp( clogged ) > PlungerFx.CloggedDamage( 2 ) * 0.5f, "Clogged did not burst on the crab" );
		Clear( a );

		// ---- The sling: once to see where it lands, then Cannonball onto a crab there ----
		await Face( a, a.Spot + a.Ahead * 10f + Vector3.Up * 2f );
		var from = player.GlobalPosition;
		jet.TestSling();
		for ( var i = 0; i < 100 && PlungerFx.Flops == 0; i++ ) await Wait( 0.05f );
		var landed = player.GlobalPosition;
		GD.Print( $"[autotest] plungerbot: slings {PlungerFx.Slings}, flops {PlungerFx.Flops}; flung {(landed - from).Flat().Length():0.0} m" );
		Check( PlungerFx.Flops == 1 && (landed - from).Flat().Length() > 4f, "the sling did not fling the player" );
		skills.Set( SkillId.Cannonball, 3 );
		var target = Crab( a, landed, 800f );
		await Wait( 1.5f );
		await Face( a, a.Spot + a.Ahead * 10f + Vector3.Up * 2f );
		hp = Hp( target );
		jet.TestSling();
		for ( var i = 0; i < 100 && PlungerFx.Flops < 2; i++ ) await Wait( 0.05f );
		GD.Print( $"[autotest] plungerbot: cannonballs {PlungerFx.Cannonballs}; the crab at the landing lost {hp - Hp( target ):0}" );
		Check( PlungerFx.Cannonballs == 1 && hp - Hp( target ) > PlungerFx.CannonballDamage( 3 ) * 0.5f, "Cannonball did not land on the crab" );
		Clear( a );
		BalanceLine( launcher );
		BalanceLine( flush );
		Done();
	}

	// ---------------------------------------------------------------- Pinball-Bot

	async Task PinballBotTest()
	{
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var a = await ToyArena( RobotClass.PinballBot, "pinballbot_portrait" );
		var launcher = inventory.Weapons.FirstOrDefault( w => w.Pinball );
		var flippers = inventory.Weapons.FirstOrDefault( w => w.Flippers );
		Check( launcher is not null && flippers is not null, "Pinball-Bot has no launcher or flippers" );
		if ( launcher is null || flippers is null ) { Done(); return; }
		inventory.Select( launcher, force: true );
		launcher.InfiniteAmmo = true;
		await Wait( 0.4f );
		var skills = PlayerSkills.Of( player );
		var jet = player.GetNode<Jetpack>( "Jetpack" );

		// ---- The plunger pulls back on its own as the trigger is held ----
		launcher.TestHoldPull = true;
		await Wait( 0.7f );
		var pulled = launcher.SpringPull;
		if ( shots is not null ) await Shot( "pinball_pull" );
		launcher.TestHoldPull = false;
		await Wait( 0.3f );
		GD.Print( $"[autotest] pinballbot: the plunger pulled to {pulled:0.00}" );
		Check( pulled > 0.9f, "holding the trigger did not pull the plunger back" );
		foreach ( var b in BotBall.All.ToList() ) b.End();
		await Wait( 0.5f );

		// ---- A ball off a crab, kicked on to the next like off a pop bumper; Multiball ----
		var crab = Crab( a, a.Spot + a.Ahead * 8f, 2000f );
		var second = Crab( a, a.Spot + a.Ahead * 8f + a.Side * 4f, 2000f );
		var third = Crab( a, a.Spot + a.Ahead * 12f + a.Side * 0.5f, 2000f );   // clear of the station that stands off to one side
		await Wait( 0.8f );
		await Face( a, crab.GlobalPosition + Vector3.Up * 0.3f );
		var hp = Hp( crab );
		var hp2 = Hp( second ) + Hp( third );
		launcher.TestLaunch( 1f );
		if ( shots is not null ) { await Wait( 0.12f ); await Shot( "pinball_ball" ); await Wait( 0.18f ); await Shot( "pinball_bumper" ); }
		await Wait( 1.2f );
		GD.Print( $"[autotest] pinballbot: balls {PinballFx.Balls}, hits {PinballFx.BallHits}, longest chain {PinballFx.LongestChain}, fastest {PinballFx.FastestBall:0.0} m/s; the crab lost {hp - Hp( crab ):0}, the other two {hp2 - Hp( second ) - Hp( third ):0}" );
		Check( PinballFx.BallHits >= 1 && hp - Hp( crab ) > 12f && hp - Hp( crab ) < 260f, "the pinball did not hit the crab for a fair amount" );
		Check( PinballFx.LongestChain >= 4 && hp2 - Hp( second ) - Hp( third ) > 5f, "the pinball was not kicked on to another crab" );
		foreach ( var b in BotBall.All.ToList() ) b.End();
		Clear( a );
		// A crowd walking in (the user's case: one shot into a pack should bounce round it many times).
		DevFlags.FreezeEnemies = false;
		var pack = new[] { (9f, 0f), (10.2f, 1.1f), (10.2f, -1.1f), (11.4f, 0.4f), (8.6f, -1.6f) }.Select( p => Crab( a, a.Spot + a.Ahead * p.Item1 + a.Side * p.Item2, 2000f ) ).ToList();
		await Wait( 0.9f );
		await Face( a, pack[0].GlobalPosition + Vector3.Up * 0.3f );
		var longestBefore = PinballFx.LongestChain;
		PinballFx.LongestChain = 0;
		launcher.TestLaunch( 1f );
		if ( shots is not null ) { await Wait( 0.5f ); await Shot( "pinball_pack" ); }
		await Wait( 2.5f );
		var packHurt = pack.Count( c => IsInstanceValid( c ) && Hp( c ) < 2000f );
		GD.Print( $"[autotest] pinballbot: into a walking pack of 5: chain {PinballFx.LongestChain}, crabs hit {packHurt}" );
		Check( PinballFx.LongestChain >= 8 && packHurt >= 4, "a ball into a pack didn't bounce round it" );
		PinballFx.LongestChain = Math.Max( PinballFx.LongestChain, longestBefore );
		DevFlags.FreezeEnemies = true;
		foreach ( var b in BotBall.All.ToList() ) b.End();
		Clear( a );
		crab = Crab( a, a.Spot + a.Ahead * 8f, 2000f );
		await Wait( 0.8f );
		skills.Set( SkillId.Multiball, 2 );
		await Wait( 2f );
		Stand( a );
		await Face( a, crab.GlobalPosition + Vector3.Up * 0.3f );   // (the pack may have shoved the player about)
		var splitsBefore = PinballFx.Splits;
		launcher.TestLaunch( 1f );
		await Wait( 0.8f );
		PinballFx.Splits -= splitsBefore;
		GD.Print( $"[autotest] pinballbot: Multiball split {PinballFx.Splits}" );
		Check( PinballFx.Splits == 2, "Multiball did not split the ball in three" );
		Clear( a );
		foreach ( var b in BotBall.All.ToList() ) b.End();

		// ---- The flippers: a tip hit is PERFECT and launches a human pinball into the crab behind ----
		inventory.Select( flippers, force: true );
		await Wait( 0.5f );
		var close = Crab( a, a.Spot + a.Ahead * 2.6f, 600f );
		var behind = Crab( a, a.Spot + a.Ahead * 6.5f, 600f );
		await Wait( 0.8f );
		await Face( a, close.GlobalPosition + Vector3.Up * 0.3f );
		var closeAt = (close.GlobalPosition - player.GlobalPosition).Flat().Length();
		hp = Hp( close );
		var hpBehind = Hp( behind );
		flippers.Flip( true );
		if ( shots is not null ) { await Wait( 0.03f ); await Shot( "pinball_flip" ); }
		var thrown = 0f;
		for ( var i = 0; i < 12; i++ ) { await Wait( 0.05f ); if ( IsInstanceValid( close ) ) thrown = MathF.Max( thrown, (close.GlobalPosition - player.GlobalPosition).Flat().Length() ); }
		GD.Print( $"[autotest] pinballbot: flips {PinballFx.Flips}, hits {PinballFx.FlipHits}, perfects {PinballFx.Perfects}, launched {PinballFx.Launched}, human pinball hits {PinballFx.HumanHitsDone}; the crab flew from {closeAt:0.0} m out to {thrown:0.0} m and lost {hp - Hp( close ):0}, the one behind lost {hpBehind - Hp( behind ):0}" );
		Check( PinballFx.FlipHits == 1 && PinballFx.Perfects == 1 && PinballFx.Launched == 1 && thrown > closeAt + 1.5f && hp - Hp( close ) > 50f, "the flipper did not send the crab flying with a PERFECT tip hit" );
		Check( PinballFx.HumanHitsDone >= 1 && hpBehind - Hp( behind ) > 20f, "the human pinball did not hit the crab behind" );
		Clear( a );
		await Wait( 0.3f );

		// ---- A loose ball slapped out; a ball cradled on a held flipper, then a skill shot as it rolls off ----
		var eye = player.Camera.GlobalPosition;
		var slow = Pinball.Fire( launcher, player, eye + (-player.Camera.GlobalBasis.Z) * 1.2f, Vector3.Up * 0.5f, 40f );
		flippers.Flip( false );
		await Wait( 0.05f );
		GD.Print( $"[autotest] pinballbot: balls flipped {PinballFx.BallsFlipped}, its speed {(IsInstanceValid( slow ) ? slow.Velocity.Length() : 0f):0.0} m/s" );
		Check( PinballFx.BallsFlipped == 1, "the flipper did not slap the ball" );
		await Wait( 0.4f );
		flippers.TestHoldLeft = true;
		flippers.Raise( true );
		await Wait( 0.35f );
		eye = player.Camera.GlobalPosition;
		var caught = Pinball.Fire( launcher, player, eye + (-player.Camera.GlobalBasis.Z) * 2.5f + Vector3.Up * 0.3f, player.Camera.GlobalBasis.Z * 3f, 40f );
		await Wait( 0.4f );
		if ( shots is not null ) await Shot( "pinball_cradle" );
		var cradled = IsInstanceValid( caught ) && caught.OnFlipper == 1;
		flippers.TestHoldLeft = false;
		flippers.Lower( true );
		await Wait( 0.12f );
		flippers.Raise( false );
		flippers.Lower( false );
		await Wait( 0.05f );
		GD.Print( $"[autotest] pinballbot: cradles {PinballFx.Cradles} (held: {cradled}), skill shots {PinballFx.SkillShots}, its speed {(IsInstanceValid( caught ) ? caught.Velocity.Length() : 0f):0.0} m/s" );
		Check( cradled && PinballFx.SkillShots == 1, "the held flipper did not cradle the ball for a skill shot" );
		foreach ( var b in BotBall.All.ToList() ) b.End();

		// ---- Ramp: the human pinball comes down in a crash ----
		skills.Set( SkillId.Ramp, 2 );
		await Wait( 0.5f );
		var ramped = Crab( a, a.Spot + a.Ahead * 2.6f, 600f );
		await Wait( 0.8f );
		await Face( a, ramped.GlobalPosition + Vector3.Up * 0.3f );
		hp = Hp( ramped );
		await Wait( 0.35f );
		flippers.Flip( true );
		await Wait( 2f );
		GD.Print( $"[autotest] pinballbot: ramps {PinballFx.Ramps}; the crab lost {hp - Hp( ramped ):0}" );
		Check( PinballFx.Ramps == 1 && hp - Hp( ramped ) > PinballFx.RampDamage( 2 ), "Ramp did not crash the human pinball down" );
		Clear( a );

		// ---- The roll: third person, a ball with its own momentum that bowls a line of crabs over (and kills a weak
		// one at the end); High Score ----
		skills.Set( SkillId.HighScore, 2 );
		var line = new[] { 3f, 5.5f, 8f }.Select( d => Crab( a, a.Spot + a.Ahead * d, 600f ) ).ToList();
		var weak = Crab( a, a.Spot + a.Ahead * 10.5f, 100f );
		await Wait( 0.8f );
		Stand( a );
		await Face( a, a.Spot + a.Ahead * 10f + Vector3.Up * 0.5f );
		await Wait( 0.1f );
		var hps = line.Select( Hp ).ToList();
		var bowledBefore = PinballFx.Bowled;
		var start = player.GlobalPosition;
		jet.TestRoll();
		var wentThird = player.ThirdPerson;
		var low = 0f;
		var fastest = 0f;
		for ( var i = 0; i < 40 && jet.Rolling; i++ )
		{
			low = MathF.Max( low, player.EyeDrop );
			fastest = MathF.Max( fastest, player.Velocity.Flat().Length() );
			if ( i == 8 && shots is not null ) await Shot( "pinball_roll" );
			await Wait( 0.05f );
		}
		var travelled = (player.GlobalPosition - start).Flat().Length();
		if ( jet.Rolling ) jet.TestRoll();
		await Wait( 0.3f );
		GD.Print( $"[autotest] pinballbot: rolls {PinballFx.Rolls}, third person {wentThird} (after {player.ThirdPerson}), bowled {PinballFx.Bowled - bowledBefore}, High Scores {PinballFx.HighScores}; rolled {travelled:0.0} m at up to {fastest:0.0} m/s; the view dropped {low:0.00} m; crabs {string.Join( ", ", hps.Select( h => h.ToString( "0" ) ) )} -> {string.Join( ", ", line.Select( c => Hp( c ).ToString( "0" ) ) )}, the weak one {(IsInstanceValid( weak ) && !weak.IsDead ? "lived" : "died")}" );
		Check( wentThird && !player.ThirdPerson, "the roll did not go to third person and back" );
		Check( PinballFx.Bowled - bowledBefore >= 3 && travelled > 5f && low > 0.5f, "the roll did not bowl the line of crabs over" );
		Check( !IsInstanceValid( weak ) || weak.IsDead, "the roll did not kill a weak crab" );
		Check( PinballFx.HighScores == 1, "High Score did not go off after bowling three" );
		Clear( a );
		foreach ( var b in BotBall.All.ToList() ) b.End();
		await Wait( 0.3f );
		BalanceLine( launcher );
		BalanceLine( flippers );
		Done();
	}
}
