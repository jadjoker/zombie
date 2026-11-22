using Godot;

/// <summary>
/// --skills: the skill trees. Tokens (one a wave, 50 a run at most), the rules (3 points in a row open the next, five
/// points a skill, three for the strongest, only the robot being played), the pause menu's tree page buying a point,
/// and a sample of the effects on each robot: Ram-Bot's Deep Mag, Wide Load, Thick Plating, Overclock, Full Choke,
/// Dragon's Breath and Kickback; Trident-Bot's Poseidon's Fork, Kraken's Blessing, Mirror Shield and Riposte;
/// Vac-Bot's Big Belly, Bubble Prison, Phase Shift (decoy and untouchable) and the Black Hole pulling a ring of
/// crabs together before it blows. With --shots (a window): the tree page and the black hole as it pulls.
/// </summary>
public partial class AutoTest
{
	async Task SkillTest()
	{
		var me = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		DevFlags.FreezeEnemies = true;
		if ( WaveDirector.Current is { } director ) director.SpawningPaused = true;
		foreach ( var laid in SeaMine.All.ToList() ) laid.QueueFree();
		RobotClass.Pick( RobotClass.RamBot, player );
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 0.5f );
		var skills = PlayerSkills.Of( player );
		Check( skills is not null, "the player has no PlayerSkills" );
		if ( skills is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );

		// ---- The trees: three per robot, three rows of two and a capstone ----
		foreach ( var robot in RobotClass.All )
			for ( var t = 0; t < 3; t++ )
			{
				var tree = Skills.Tree( robot.Id, t ).ToList();
				Check( tree.Count == 7 && Enumerable.Range( 0, 3 ).All( r => tree.Count( d => d.Row == r ) == 2 ) && tree.Count( d => d.Capstone ) == 1,
					$"{robot.Name}'s tree {t} is not three rows of two and a capstone" );
			}
		var capped = Skills.All.Where( d => d.Max == 3 ).Select( d => d.Name ).ToList();
		GD.Print( $"[autotest] skills: {Skills.All.Length} skills, capped at 3: {string.Join( ", ", capped )}" );
		Check( Skills.All.All( d => d.Max is 3 or 5 ) && Skills.All.Where( d => d.Capstone ).All( d => d.Max == 3 ), "a skill doesn't stop at 5 (or 3), or a capstone isn't 3" );
		Check( Skills.All.All( d => PerkPickup.Icon( d.Icon ) is not null ), $"skills with no icon: {string.Join( ", ", Skills.All.Where( d => PerkPickup.Icon( d.Icon ) is null ).Select( d => d.Name ) )}" );

		// ---- Tokens: one a wave, 50 a run ----
		Check( skills.Tokens == 0 && skills.Earned == 0, "a run starts with tokens" );
		for ( var i = 0; i < 60; i++ ) skills.Award();
		Check( skills.Earned == Skills.MaxTokens && skills.Tokens == Skills.MaxTokens, $"60 waves gave {skills.Earned} tokens, not {Skills.MaxTokens}" );

		// ---- The rules ----
		Check( !skills.Buy( SkillId.DeepMag ), "a row 2 skill was bought with nothing in row 1" );
		skills.Buy( SkillId.FireRate ); skills.Buy( SkillId.FireRate ); skills.Buy( SkillId.FasterReload );
		Check( skills.Buy( SkillId.DeepMag ), "3 points in row 1 did not open row 2" );
		for ( var i = 0; i < 4; i++ ) skills.Buy( SkillId.FireRate );
		Check( skills.Held( SkillId.FireRate ) == 5, $"Fire Rate went to {skills.Held( SkillId.FireRate )}, not stopping at 5" );
		Check( !skills.Buy( SkillId.BlackHole ) && !skills.Buy( SkillId.QuickDraw ), "another robot's skill was bought" );
		Check( !skills.Buy( SkillId.Overclock ), "the capstone opened with nothing in row 3" );
		Check( skills.Tokens == Skills.MaxTokens - 7, $"{skills.Tokens} tokens left after 7 points, not {Skills.MaxTokens - 7}" );
		var rifle = inventory.Weapons.First( w => w.ModelName == "rifle" );
		await Wait( 0.1f );
		GD.Print( $"[autotest] skills: rifle fire rate {rifle.FireRate:0.0} -> {rifle.CurrentFireRate:0.00} with 5 points" );
		Check( MathF.Abs( rifle.CurrentFireRate - rifle.FireRate * 1.4f ) < 0.01f, "5 points of Fire Rate are not +40%" );

		// ---- The pause menu opens on the trees; a click buys a point ----
		var pause = GetTree().Root.FindChild( "PauseMenu", true, false ) as PauseMenu;
		pause?.SetOpen( true );
		await Wait( 0.3f );
		var nodes = pause?.FindChildren( "*", "", true, false ).OfType<SkillNode>().ToList() ?? new();
		GD.Print( $"[autotest] skills: the pause menu is on the trees {pause?.OnSkills}, {nodes.Count} skills shown" );
		Check( pause is not null && pause.OnSkills && nodes.Count == 21, $"the pause menu did not open on the trees with 21 skills ({nodes.Count})" );
		var page = pause?.FindChild( "SkillTreePage", true, false ) as Control;
		var arrow = pause?.FindChild( "Menu", true, false ) as Control;
		GD.Print( $"[autotest] skills: page {page?.GetGlobalRect()}, arrow {arrow?.GetGlobalRect()} visible {arrow?.IsVisibleInTree()}, viewport {GetViewport().GetVisibleRect()}" );
		Check( arrow is not null && arrow.IsVisibleInTree() && GetViewport().GetVisibleRect().Encloses( arrow.GetGlobalRect() ), "the arrow to the menu is not on screen" );
		var reload = nodes.FirstOrDefault( n => n.Def.Id == SkillId.FasterReload );
		var tokens = skills.Tokens;
		reload?.EmitSignal( Button.SignalName.Pressed );
		await Wait( 0.2f );
		Check( skills.Held( SkillId.FasterReload ) == 2 && skills.Tokens == tokens - 1, "pressing a skill on the page did not buy a point" );
		if ( shots is not null ) { reload?.GrabFocus(); await Wait( 0.2f ); await Shot( "skills_page" ); }
		pause?.SetOpen( false );
		await Wait( 0.2f );

		// ---- Ram-Bot ----
		skills.Clear();
		Check( skills.Tokens == 0 && skills.Held( SkillId.FireRate ) == 0, "clearing the skills left points or tokens" );
		var (spot, yaw) = OpenLane( 16f );
		var ahead = new Vector3( -MathF.Sin( MathX.Rad( yaw ) ), 0f, -MathF.Cos( MathX.Rad( yaw ) ) );
		void Stand() { player.GlobalPosition = spot + Vector3.Up * 0.1f; player.Velocity = Vector3.Zero; }
		void LookAt( Vector3 at )
		{
			var to = (at - player.Camera.GlobalPosition).Normalized();
			player.EyeAngles = new Angles( MathX.Deg( -MathF.Asin( to.Y ) ), MathX.Deg( MathF.Atan2( -to.X, -to.Z ) ), 0f );
		}
		Stand();
		var health = me.Health.MaxHealth;
		skills.Set( SkillId.DeepMag, 5 );
		skills.Set( SkillId.WideLoad, 3 );
		skills.Set( SkillId.ThickPlating, 5 );
		skills.Set( SkillId.Overclock, 3 );
		await Wait( 0.1f );
		var shotgun = inventory.Weapons.First( w => w.ModelName == "shotgun" );
		GD.Print( $"[autotest] skills: rifle magazine {rifle.MagazineSize}, shotgun pellets {shotgun.Pellets}, max health {health:0} -> {me.Health.MaxHealth:0}" );
		Check( rifle.MagazineSize == (int)MathF.Round( 30 * 1.75f ) && shotgun.Pellets == 15, "Deep Mag or Wide Load did not change the gun" );
		Check( MathF.Abs( me.Health.MaxHealth - health - 100f ) < 0.1f, "5 points of Thick Plating did not add 100 health" );
		inventory.Select( rifle, force: true );
		await Wait( 0.5f );
		rifle.TestFinishReload();
		Check( rifle.OverclockLeft == SkillFx.OverclockShots( 3 ) && MathF.Abs( rifle.CurrentFireRate - rifle.FireRate * 1.5f ) < 0.01f, $"Overclock did not wind up {SkillFx.OverclockShots( 3 )} shots at +50% ({rifle.OverclockLeft})" );

		var crabs = new List<JunkCrab>();
		JunkCrab Crab( Vector3 at, float hp ) { var c = JunkCrab.Spawn( GetTree().CurrentScene, Nav.Closest( player, at ), hp ); crabs.Add( c ); return c; }
		void ClearCrabs() { foreach ( var c in crabs ) if ( IsInstanceValid( c ) ) c.QueueFree(); crabs.Clear(); }

		// Shotgun: the slug, the fire, the shove.
		inventory.Select( shotgun, force: true );
		skills.Set( SkillId.FullChoke, 1 );
		skills.Set( SkillId.DragonsBreath, 5 );
		skills.Set( SkillId.Kickback, 5 );
		var target = Crab( spot + ahead * 5f, 400f );
		Crab( spot + ahead * 7.5f, 3000f );
		await Wait( 0.8f );
		Stand();
		LookAt( target.GlobalPosition + Vector3.Up * 0.35f );
		await Wait( 0.1f );
		var before = target.GlobalPosition;
		shotgun.TestPrimeChoke();
		shotgun.TestFire();
		await Wait( 0.4f );
		var shoved = IsInstanceValid( target ) ? (target.GlobalPosition - before).Flat().Length() : -1f;
		GD.Print( $"[autotest] skills: Full Choke slugs {SkillFx.Slugs} (hit {SkillFx.SlugHits}), burns {SkillFx.Burns}, Kickback moved the crab {shoved:0.00} m" );
		Check( SkillFx.Slugs == 1 && SkillFx.SlugHits >= 1, "the fourth shotgun blast had no slug, or it hit nothing" );
		Check( SkillFx.Burns >= 1, "Dragon's Breath set nothing alight" );
		Check( shoved > 0.3f || !IsInstanceValid( target ) || target.IsDead, "Kickback did not shove the crab" );
		ClearCrabs();

		// ---- Trident-Bot ----
		RobotClass.Pick( RobotClass.TridentBot, player );
		await Wait( 0.6f );
		Check( Skills.Rank( SkillId.DeepMag ) == 0 && skills.Held( SkillId.DeepMag ) == 5, "Ram-Bot's points counted while playing Trident-Bot (or were lost)" );
		var shooter = inventory.Weapons.First( w => w.Projectile );
		var spear = inventory.Weapons.First( w => w.Spear );
		skills.Set( SkillId.PoseidonsFork, 1 );
		skills.Set( SkillId.KrakensBlessing, 1 );
		skills.Set( SkillId.MirrorShield, 5 );
		skills.Set( SkillId.Riposte, 5 );
		inventory.Select( shooter, force: true );
		await Wait( 0.6f );
		Stand();
		player.EyeAngles = new Angles( 5f, yaw, 0f );
		var boltsBefore = GetTree().CurrentScene.GetChildren().OfType<TridentBolt>().Count();
		shooter.TestFireCharged();
		var bolts = GetTree().CurrentScene.GetChildren().OfType<TridentBolt>().Count() - boltsBefore;
		GD.Print( $"[autotest] skills: a full charge with Poseidon's Fork threw {bolts} tridents" );
		Check( SkillFx.Forks == 1 && bolts == 3, $"Poseidon's Fork threw {bolts} tridents, not 3" );
		await Wait( 0.5f );
		player.GetNode<Jetpack>( "Jetpack" ).TestSurge( 0.3f );
		await Wait( 2f );
		GD.Print( $"[autotest] skills: Kraken's Blessing whirlpools {SkillFx.Whirlpools}" );
		Check( SkillFx.Whirlpools >= 1, "the surge left no whirlpool with Kraken's Blessing" );
		Stand();
		inventory.Select( spear, force: true );
		await Wait( 0.5f );
		var biter = Crab( spot + ahead * 3f, 3000f );
		await Wait( 0.6f );
		spear.TestBlock( true );
		PlayerWeapon.ForceMirror = true;
		var crabHp = biter.Health.Current;
		me.Health.ResetTo( me.Health.MaxHealth );
		me.Health.OnDamage( new DamageInfo { Damage = 20f, Attacker = biter, Position = player.GlobalPosition + Vector3.Up, Origin = biter.GlobalPosition } );
		PlayerWeapon.ForceMirror = null;
		GD.Print( $"[autotest] skills: Mirror Shield reflects {SkillFx.Reflects}, the crab lost {crabHp - biter.Health.Current:0}; Riposte ready {spear.RiposteReady}" );
		Check( SkillFx.Reflects == 1 && crabHp - biter.Health.Current >= 39f, "Mirror Shield did not flash a blocked hit back for double" );
		Check( spear.RiposteReady, "a blocked hit did not ready Riposte" );
		spear.TestBlock( false );
		ClearCrabs();

		// ---- Vac-Bot ----
		RobotClass.Pick( RobotClass.VacBot, player );
		await Wait( 0.6f );
		var vac = inventory.Weapons.First( w => w.Vacuum );
		skills.Set( SkillId.BigBelly, 5 );
		skills.Set( SkillId.BubblePrison, 3 );
		skills.Set( SkillId.PhaseShift, 3 );
		await Wait( 0.1f );
		Check( vac.MagazineSize == 200, $"5 points of Big Belly made the tank {vac.MagazineSize}, not 200" );

		// Bubble Prison: a bubbled crab takes 50% more.
		var bubbled = Crab( spot + ahead * 6f, 3000f );
		await Wait( 0.6f );
		bubbled.Bubbled = true;
		var hpBefore = bubbled.Health.Current;
		bubbled.Health.OnDamage( new DamageInfo { Damage = 100f, Attacker = player } );
		var took = hpBefore - bubbled.Health.Current;
		bubbled.Bubbled = false;
		GD.Print( $"[autotest] skills: a 100 hit on a bubbled crab with 3 points of Bubble Prison took {took:0}" );
		Check( MathF.Abs( took - 150f ) < 0.5f, "Bubble Prison did not make a bubbled crab take 50% more" );
		ClearCrabs();

		// Phase Shift: a blink leaves a decoy enemies go for, and a hit aimed at it doesn't land; untouchable a moment.
		Stand();
		player.EyeAngles = new Angles( 0f, yaw, 0f );
		var startAt = player.GlobalPosition;
		player.GetNode<Jetpack>( "Jetpack" ).TestSurge();
		for ( var i = 0; i < 30 && me.Decoy is null; i++ ) await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
		var decoy = me.Decoy;
		Check( decoy is not null && IsInstanceValid( decoy ) && Clock.Now < me.UntouchableUntil, "a blink with Phase Shift left no decoy or no untouchable moment" );
		me.Health.ResetTo( me.Health.MaxHealth );
		me.Health.OnDamage( new DamageInfo { Damage = 30f, Attacker = this, Position = player.GlobalPosition + Vector3.Up } );
		var untouched = me.Health.Current == me.Health.MaxHealth;
		await Wait( 0.9f );
		var aimedAt = me.WorldPosition;
		me.Health.ResetTo( me.Health.MaxHealth );
		me.Health.OnDamage( new DamageInfo { Damage = 30f, Attacker = this, Position = (decoy?.GlobalPosition ?? startAt) + Vector3.Up } );
		GD.Print( $"[autotest] skills: decoys {SkillFx.Decoys}; untouched right after the blink {untouched}; enemies aim {aimedAt.DistanceTo( startAt ):0.0} m from where it began; a hit at the decoy left health {me.Health.Current:0}/{me.Health.MaxHealth:0}" );
		Check( untouched, "a hit right after a Phase Shift blink landed" );
		Check( aimedAt.DistanceTo( startAt ) < 0.5f, "enemies don't go for the decoy" );
		Check( me.Health.Current == me.Health.MaxHealth, "a hit aimed at the decoy reached the player" );
		await Wait( SkillFx.DecoyTime( 3 ) );
		Check( me.WorldPosition.DistanceTo( player.GlobalPosition ) < 0.01f, "enemies still go for the decoy after it is gone" );

		// ---- The Black Hole: a ring of crabs dragged together, then the blast ----
		skills.Set( SkillId.BlackHole, 3 );
		Stand();
		var centre = Nav.Closest( player, spot + ahead * 10f );
		var side = ahead.Cross( Vector3.Up ).Normalized();
		// Under 1000 health, so light enough to drag (heavier ones only get crushed).
		Crab( centre, 950f );
		foreach ( var a in new[] { 0f, 1.3f, 2.6f, 3.9f, 5.2f } )
			Crab( centre + (ahead * MathF.Cos( a ) + side * MathF.Sin( a )) * 4.5f, 950f );
		await Wait( 0.8f );
		inventory.Select( vac, force: true );
		vac.InfiniteAmmo = true;
		await Wait( 0.6f );
		vac.InfiniteAmmo = false;
		Stand();
		LookAt( crabs[0].GlobalPosition + Vector3.Up * 0.3f );
		await Wait( 0.1f );
		var opened = BlackHole.Opened;
		vac.TestSpit( true );
		for ( var i = 0; i < 40 && BlackHole.Opened == opened; i++ ) await Wait( 0.05f );
		Check( BlackHole.Opened == opened + 1, "the huge shot did not open a black hole" );
		var spread = crabs.Where( c => IsInstanceValid( c ) && !c.IsDead ).Average( c => (c.GlobalPosition + Vector3.Up * 0.4f).DistanceTo( BlackHole.LastAt ) );
		GD.Print( $"[autotest] skills: the black hole opened {BlackHole.LastAt.DistanceTo( centre ):0.0} m from the ring's middle, {BlackHole.LastAt.DistanceTo( player.GlobalPosition ):0.0} m from the player; crabs at {string.Join( ", ", crabs.Select( c => (c.GlobalPosition + Vector3.Up * 0.4f).DistanceTo( BlackHole.LastAt ).ToString( "0.0" ) ) )}" );
		if ( shots is not null )
		{
			// A clear look: stood a few metres off it, the gun away.
			DevFlags.HideWeapon = true;
			player.GlobalPosition = Nav.Closest( player, BlackHole.LastAt - ahead * 6f + side * 1.5f ) + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			LookAt( BlackHole.LastAt );
			// Timed by the hole's own clock (a slow window would throw fixed waits off).
			async Task At( float age, string name )
			{
				for ( var i = 0; i < 400 && BlackHole.Current is { } h && IsInstanceValid( h ) && h.Age < age; i++ ) await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
				await Shot( name );
			}
			await At( 0.45f, "blackhole_forming" );
			await At( 1.4f, "blackhole_pulling" );
			await At( BlackHole.Life( 3 ) - 0.12f, "blackhole_collapse" );
			for ( var i = 0; i < 400 && BlackHole.Detonated == 0; i++ ) await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
			await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
			await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
			await Shot( "blackhole_blast" );
			DevFlags.HideWeapon = false;
		}
		for ( var i = 0; i < 100 && BlackHole.Detonated == 0; i++ ) await Wait( 0.05f );
		GD.Print( $"[autotest] skills: crabs at the end {string.Join( ", ", crabs.Where( c => IsInstanceValid( c ) ).Select( c => (c.GlobalPosition + Vector3.Up * 0.4f).DistanceTo( BlackHole.LastAt ).ToString( "0.0" ) ) )}" );
		GD.Print( $"[autotest] skills: black hole opened {BlackHole.Opened}, went off {BlackHole.Detonated}; crabs {spread:0.0} m from it on average when it opened, {BlackHole.LastPulledIn:0.0} m when it blew; gibs swallowed {BlackHole.Swallowed}" );
		Check( BlackHole.Detonated == 1, "the black hole never went off" );
		Check( BlackHole.LastPulledIn >= 0f && BlackHole.LastPulledIn < spread * 0.5f, $"the black hole did not drag the crabs together ({spread:0.0} m to {BlackHole.LastPulledIn:0.0} m)" );
		ClearCrabs();

		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
