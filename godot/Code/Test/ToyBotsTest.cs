using Godot;

/// <summary>
/// --magnetbot, --plungerbot and --pinballbot: the three physics robots. Magnet-Bot: a scrap ball hits and gathers
/// gore, the recall gives a ball back, opposite tags smash two crabs together (Magnetar), the pulse throws a crab off
/// (Pole Reversal). Plunger-Bot: plungers stick and pop at three, the yank drags a crab in, the toilet's whirlpool pulls
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

	// ---------------------------------------------------------------- Magnet-Bot

	async Task MagnetBotTest()
	{
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		var a = await ToyArena( RobotClass.MagnetBot, "magnetbot_portrait" );
		var scrap = inventory.Weapons.FirstOrDefault( w => w.Scrap );
		var polarity = inventory.Weapons.FirstOrDefault( w => w.Polarity );
		var me = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		Check( scrap is not null && polarity is not null, "Magnet-Bot has no scrap cannon or polarity gun" );
		Check( me.Health.MaxHealth == RobotClass.MagnetBot.BaseHealth, $"Magnet-Bot's health is {me.Health.MaxHealth}" );
		if ( scrap is null || polarity is null ) { Done(); return; }
		inventory.Select( scrap, force: true );
		await Wait( 0.4f );
		var skills = PlayerSkills.Of( player );
		var jet = player.GetNode<Jetpack>( "Jetpack" );

		// ---- A full scrap ball, with gore gathered into it ----
		var crab = Crab( a, a.Spot + a.Ahead * 8f, 2000f );
		await Wait( 0.8f );
		await Face( a, crab.GlobalPosition + Vector3.Up * 0.3f );
		GibKit.Burst( MagnetFx.Gibs, player.Camera.GlobalPosition + a.Ahead * 1.6f, Vector3.Up, 0.06f, 8, 0.1f );
		await Wait( 0.3f );
		var hp = Hp( crab );
		var ammo = scrap.Ammo;
		scrap.TestScrap( 1.2f );
		if ( shots is not null ) { await Wait( 0.15f ); await Shot( "magnet_scrapball" ); }
		await Wait( 1.2f );
		GD.Print( $"[autotest] magnetbot: balls {MagnetFx.Balls}, hits {MagnetFx.BallHits}, junk gathered {MagnetFx.Junk}; the crab lost {hp - Hp( crab ):0}" );
		Check( MagnetFx.Balls == 1 && MagnetFx.BallHits >= 1, "the scrap ball did not hit the crab" );
		Check( MagnetFx.Junk >= 1, "the scrap cannon gathered no gore" );
		Check( hp - Hp( crab ) > 60f && hp - Hp( crab ) < 300f, $"a full scrap ball did {hp - Hp( crab ):0}, outside 40 to 260" );
		// The recall brings a ball back.
		AimAt( a.Spot + a.Ahead * 20f + Vector3.Up * 3f );
		scrap.TestScrap( 0.1f );
		await Wait( 0.35f );
		var afterShot = scrap.Ammo;
		scrap.Recall();
		for ( var i = 0; i < 40 && BotBall.All.Count > 0; i++ ) await Wait( 0.05f );
		GD.Print( $"[autotest] magnetbot: ammo {ammo} -> {afterShot} after two balls, {scrap.Ammo} after the recall" );
		Check( MagnetFx.Recalls == 1 && scrap.Ammo > afterShot, "the recalled balls did not come back to the magazine" );
		Clear( a );

		// ---- Polarity: opposite tags smash two crabs together; Magnetar ----
		inventory.Select( polarity, force: true );
		polarity.InfiniteAmmo = true;
		skills.Set( SkillId.Magnetar, 2 );
		await Wait( 0.5f );
		var red = Crab( a, a.Spot + a.Ahead * 8f + a.Side * 1.6f, 600f );
		var blue = Crab( a, a.Spot + a.Ahead * 8f + a.Side * 4.6f, 600f );
		var third = Crab( a, a.Spot + a.Ahead * 10f, 600f );
		await Wait( 0.8f );
		var gap = red.GlobalPosition.DistanceTo( blue.GlobalPosition );
		await Face( a, red.GlobalPosition + Vector3.Up * 0.3f );
		polarity.TestTag( +1 );
		// The crab's antenna eyes can stand in the way (they don't take shots): try a few heights.
		for ( var k = 0; k < 4 && MagnetFx.Tags < 2; k++ )
		{
			AimAt( blue.GlobalPosition + Vector3.Up * (0.15f + 0.1f * k) );
			await Wait( 0.1f );
			AimAt( blue.GlobalPosition + Vector3.Up * (0.15f + 0.1f * k) );
			await Wait( 0.05f );
			polarity.TestTag( -1 );
		}
		var thirdHp = Hp( third );
		for ( var i = 0; i < 60 && MagnetFx.Smashes == 0; i++ )
		{
			await Wait( 0.05f );
			if ( i == 8 && shots is not null ) await Shot( "magnet_tags" );
		}
		GD.Print( $"[autotest] magnetbot: tags {MagnetFx.Tags}, smashes {MagnetFx.Smashes}, magnetars {MagnetFx.Magnetars}; the crabs started {gap:0.0} m apart, health {Hp( red ):0} and {Hp( blue ):0}; the third {thirdHp:0} -> {Hp( third ):0}" );
		Check( MagnetFx.Tags >= 2 && MagnetFx.Smashes == 1, "opposite tags did not smash the two crabs together" );
		Check( Hp( red ) < 600f - MagnetFx.SmashDamage * 0.9f && Hp( blue ) < 600f - MagnetFx.SmashDamage * 0.9f, "the smash did not hurt both crabs" );
		Check( MagnetFx.Magnetars == 1 && Hp( third ) < thirdHp, "Magnetar did not drag in and hurt the crab near the smash" );
		Clear( a );

		// ---- The pulse; Pole Reversal ----
		var near = Crab( a, a.Spot + a.Ahead * 2.5f, 300f );
		await Wait( 0.8f );
		Stand( a );
		await Wait( 0.1f );
		var nearAt = (near.GlobalPosition - player.GlobalPosition).Flat().Length();
		hp = Hp( near );
		jet.TestPulse();
		await Wait( 0.3f );
		var pushed = (near.GlobalPosition - player.GlobalPosition).Flat().Length();
		GD.Print( $"[autotest] magnetbot: pulses {MagnetFx.Pulses}; the crab went from {nearAt:0.0} m to {pushed:0.0} m and lost {hp - Hp( near ):0}" );
		Check( MagnetFx.Pulses == 1 && pushed > nearAt + 1f && Hp( near ) < hp, "the pulse did not throw back and hurt the crab" );
		skills.Set( SkillId.PoleReversal, 2 );
		await Wait( 1.5f );
		hp = Hp( near );
		jet.TestPulse();
		await Wait( 0.8f );
		GD.Print( $"[autotest] magnetbot: Pole Reversal {MagnetFx.Reversals}; the crab lost {hp - Hp( near ):0}" );
		Check( MagnetFx.Reversals == 1 && (hp - Hp( near ) > MagnetFx.ReversalDamage( 2 ) * 0.8f || !IsInstanceValid( near ) || near.IsDead), "Pole Reversal did not pull in and blast the crab" );
		Clear( a );
		BalanceLine( scrap );
		BalanceLine( polarity );
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

		// ---- A ball off a crab; Multiball ----
		var crab = Crab( a, a.Spot + a.Ahead * 8f, 2000f );
		await Wait( 0.8f );
		await Face( a, crab.GlobalPosition + Vector3.Up * 0.3f );
		var hp = Hp( crab );
		launcher.TestLaunch( 1f );
		if ( shots is not null ) { await Wait( 0.12f ); await Shot( "pinball_ball" ); }
		await Wait( 1f );
		GD.Print( $"[autotest] pinballbot: balls {PinballFx.Balls}, hits {PinballFx.BallHits}, fastest {PinballFx.FastestBall:0.0} m/s; the crab lost {hp - Hp( crab ):0}" );
		Check( PinballFx.BallHits >= 1 && hp - Hp( crab ) > 25f && hp - Hp( crab ) < 260f, "the pinball did not hit the crab for a fair amount" );
		skills.Set( SkillId.Multiball, 2 );
		await Wait( 2f );
		AimAt( crab.GlobalPosition + Vector3.Up * 0.3f );
		launcher.TestLaunch( 1f );
		await Wait( 0.8f );
		GD.Print( $"[autotest] pinballbot: Multiball split {PinballFx.Splits}" );
		Check( PinballFx.Splits == 2, "Multiball did not split the ball in three" );
		Clear( a );
		foreach ( var b in BotBall.All.ToList() ) b.End();

		// ---- The flippers: a whack, a ball slapped back out, Ramp ----
		inventory.Select( flippers, force: true );
		await Wait( 0.5f );
		var close = Crab( a, a.Spot + a.Ahead * 2.2f, 600f );
		await Wait( 0.8f );
		await Face( a, close.GlobalPosition + Vector3.Up * 0.3f );
		var closeAt = (close.GlobalPosition - player.GlobalPosition).Flat().Length();
		hp = Hp( close );
		flippers.Flip( true );
		if ( shots is not null ) { await Wait( 0.05f ); await Shot( "pinball_flip" ); }
		await Wait( 0.3f );
		var thrown = (close.GlobalPosition - player.GlobalPosition).Flat().Length();
		GD.Print( $"[autotest] pinballbot: flips {PinballFx.Flips}, hits {PinballFx.FlipHits}; the crab went from {closeAt:0.0} m to {thrown:0.0} m and lost {hp - Hp( close ):0}" );
		Check( PinballFx.FlipHits == 1 && thrown > closeAt + 1f && hp - Hp( close ) > 25f, "the flipper did not whack the crab away" );
		var eye = player.Camera.GlobalPosition;
		var slow = Pinball.Fire( launcher, player, eye + (-player.Camera.GlobalBasis.Z) * 1.2f, Vector3.Up * 0.5f, 40f );
		flippers.Flip( false );
		await Wait( 0.05f );
		GD.Print( $"[autotest] pinballbot: balls flipped {PinballFx.BallsFlipped}, its speed {(IsInstanceValid( slow ) ? slow.Velocity.Length() : 0f):0.0} m/s" );
		Check( PinballFx.BallsFlipped == 1, "the flipper did not slap the ball" );
		skills.Set( SkillId.Ramp, 2 );
		await Wait( 0.5f );
		var ramped = Crab( a, a.Spot + a.Ahead * 2.2f, 600f );
		await Wait( 0.8f );
		await Face( a, ramped.GlobalPosition + Vector3.Up * 0.3f );
		hp = Hp( ramped );
		flippers.Flip( true );
		await Wait( 1.2f );
		GD.Print( $"[autotest] pinballbot: ramps {PinballFx.Ramps}; the crab lost {hp - Hp( ramped ):0}" );
		Check( PinballFx.Ramps == 1 && hp - Hp( ramped ) > PinballFx.RampDamage( 2 ), "Ramp did not throw the crab up and drop it" );
		Clear( a );

		// ---- The roll bowls a line of crabs over; High Score ----
		skills.Set( SkillId.HighScore, 2 );
		var line = new[] { 2.5f, 4.5f, 6.5f }.Select( d => Crab( a, a.Spot + a.Ahead * d, 600f ) ).ToList();
		await Wait( 0.8f );
		Stand( a );
		await Wait( 0.1f );
		var hps = line.Select( Hp ).ToList();
		jet.TestRoll();
		var low = 0f;
		for ( var i = 0; i < 30 && jet.Rolling; i++ )
		{
			// Roll on down the line (the test moves the player, as the keys would).
			player.GlobalPosition = Nav.Closest( player, a.Spot + a.Ahead * (0.3f * i) ) + Vector3.Up * 0.1f;
			low = MathF.Max( low, player.EyeDrop );
			if ( i == 6 && shots is not null ) await Shot( "pinball_roll" );
			await Wait( 0.05f );
		}
		if ( jet.Rolling ) jet.TestRoll();
		await Wait( 0.2f );
		GD.Print( $"[autotest] pinballbot: rolls {PinballFx.Rolls}, bowled {PinballFx.Bowled}, High Scores {PinballFx.HighScores}; the view dropped {low:0.00} m; crabs {string.Join( ", ", hps.Select( h => h.ToString( "0" ) ) )} -> {string.Join( ", ", line.Select( c => Hp( c ).ToString( "0" ) ) )}" );
		Check( PinballFx.Bowled >= 3 && low > 0.5f, "the roll did not bowl the line of crabs over" );
		Check( PinballFx.HighScores == 1, "High Score did not go off after bowling three" );
		Clear( a );
		foreach ( var b in BotBall.All.ToList() ) b.End();
		await Wait( 0.3f );
		BalanceLine( launcher );
		BalanceLine( flippers );
		Done();
	}
}
