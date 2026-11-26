using Godot;

/// <summary>
/// The Abyss's bosses. --warden: wave 25 brings the Blind Warden; it listens with its gills open (triple damage, the
/// shell much less), ignores a player standing quiet, charges at a gunshot and runs them down, crashes into the statue
/// and is dazed, claps a stunning ring at a player in front, flicks rubble at one behind, and screams to mark a quiet
/// player far off and charge them anyway. --leviathan: wave 30 brings the Leviathan down through the skylight; its mark
/// follows the player then locks, the ram lands there (a shock ring, stalactites, a crack in the roof), it rests with
/// its eyes and blowholes open, its coil sweep stays low enough to jump and hits a player in its way, its song throws
/// back and slows, its swallow takes the player to its stomach, where shooting the ulcers hurts it and it spits them
/// out, its brood brings puffers and an angler, and dead it lies across the hub. With --shots (a window), pictures.
/// </summary>
public partial class AutoTest
{
	async Task<bool> ToAbyss()
	{
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		var d = WaveDirector.Current;
		d.SpawningPaused = true;
		AllyCommander.KeepAlly = false;
		foreach ( var ally in SeaSpider.All.Where( s => !s.IsEnemy ).ToList() ) ally.QueueFree();
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		AbyssArea.Go( GetTree().CurrentScene as Node3D, player );
		await Wait( 1f );
		await WaitForNav();
		Check( SeaTerrain.Area == SeaTerrain.Caves, "did not get to the Abyss" );
		return SeaTerrain.Area == SeaTerrain.Caves;
	}

	/// <summary> Stands the player on the floor at a point, looking at another. </summary>
	void StandAt( Vector3 at, Vector3 look )
	{
		player.GlobalPosition = at with { Y = SeaTerrain.HeightAt( at.X, at.Z ) + 0.1f };
		player.Velocity = Vector3.Zero;
		var dir = (look - at).Flat().SafeNormal();
		player.EyeAngles = new Angles( -6f, MathX.Deg( MathF.Atan2( -dir.X, -dir.Z ) ), 0f );
	}

	/// <summary> Turns the view to look at a point (for the pictures). </summary>
	void Face( Vector3 at )
	{
		var eye = player.GlobalPosition + Vector3.Up * 1.6f;
		var d = at - eye;
		player.EyeAngles = new Angles( -MathX.Deg( MathF.Atan2( d.Y, d.Flat().Length() ) ), MathX.Deg( MathF.Atan2( -d.X, -d.Z ) ), 0f );
	}

	static Vector3 Hub( float r, float degrees ) => Abyss.Polar( r, degrees ) with { Y = 0f };

	async Task<T> BossWave<T>( int wave, Func<T> find, string name ) where T : Creature
	{
		var d = WaveDirector.Current;
		d.JumpToWave( wave );
		d.SpawningPaused = false;
		T boss = null;
		for ( var t = 0f; t < 10f && (boss = find()) is null; t += 0.1f ) await Wait( 0.1f );
		var banner = GameHud.BossArrives( d.BossKind );
		GD.Print( $"[autotest] {name}: wave {d.Wave} banner \"{banner}\", MC-51s {MC61.All.Count}, health {boss?.Health?.MaxHealth ?? 0:0}" );
		Check( boss is not null, $"no {name} came on wave {wave}" );
		Check( d.BossWave && MC61.All.Count == 0, $"wave {wave} is not a boss wave, or MC-51 came too" );
		d.SpawningPaused = true;
		foreach ( var c in Creature.Everyone.Where( c => c != boss && c.IsEnemy ).ToList() ) c.QueueFree();
		await Wait( 0.3f );
		return boss;
	}

	async Task WardenTest()
	{
		if ( !await ToAbyss() ) { Done(); return; }
		var d = WaveDirector.Current;
		var was = d.Wave;
		d.Wave = 25; var on25 = d.BossKind;
		d.Wave = 30; var on30 = d.BossKind;
		d.Wave = 20; var on20 = d.BossKind;
		d.Wave = 40; var on40 = d.BossKind;
		d.Wave = was;
		Check( on25 == WaveDirector.EnemyKind.Warden && on30 == WaveDirector.EnemyKind.Leviathan && on20 == WaveDirector.EnemyKind.Davey && on40 == WaveDirector.EnemyKind.Poseidon,
			$"the boss waves are wrong (20 {on20}, 25 {on25}, 30 {on30}, 40 {on40})" );
		StandAt( Hub( 15f, 230f ), Vector3.Zero );
		var w = await BossWave( 25, () => BlindWarden.All.FirstOrDefault(), "warden" );
		if ( w is null ) { Done(); return; }
		Check( GameHud.BossArrives( WaveDirector.EnemyKind.Warden ).Contains( "BLIND WARDEN" ), "wave 25's banner doesn't name the Blind Warden" );
		Check( MathF.Abs( w.Health.MaxHealth - d.WardenHealth ) < 1f, $"the Warden's health is {w.Health.MaxHealth:0}" );

		// Quiet: it listens, gills open, and doesn't come.
		var home = Hub( 15f, 290f );
		w.GlobalPosition = home with { Y = SeaTerrain.HeightAt( home.X, home.Z ) };
		w.GlobalBasis = Shapes.LookAt( (Vector3.Zero - home).Flat().Normalized() );
		var spot = Hub( 15f, 200f );
		StandAt( spot, w.GlobalPosition );
		await Wait( 0.5f );
		var charges = BlindWarden.Charges;
		await Wait( 3f );
		GD.Print( $"[autotest] warden: a quiet player {spot.DistanceTo( w.GlobalPosition ):0} m off, charges {BlindWarden.Charges - charges}, doing {w.Doing}, gills {w.GillsOpen:0.00}" );
		Check( BlindWarden.Charges == charges, "the Warden charged a player standing quiet" );
		Check( w.GillsVulnerable, "the Warden's gills are not open while it listens" );
		if ( !string.IsNullOrEmpty( shots ) ) { var keep = player.GlobalPosition; StandAt( w.GlobalPosition + (spot - w.GlobalPosition).Normalized() * 11f, w.GlobalPosition ); Face( w.GlobalPosition + Vector3.Up * 1.5f ); await Wait( 0.2f ); await Shot( "warden_listening" ); StandAt( keep, w.GlobalPosition ); await Wait( 0.2f ); }

		// The gills take triple, the shell much less.
		var h0 = w.Health.Current;
		w.ReceiveShot( 2, 100f, Vector3.Forward, w.GlobalPosition, w.GlobalPosition, null, false );
		var gills = h0 - w.Health.Current;
		h0 = w.Health.Current;
		w.ReceiveShot( 1, 100f, Vector3.Forward, w.GlobalPosition, w.GlobalPosition, null, false );
		var shell = h0 - w.Health.Current;
		GD.Print( $"[autotest] warden: 100 on the open gills took {gills:0}, on the shell {shell:0}" );
		Check( gills > 250f && shell < 60f, "the Warden's gills and shell take the wrong damage" );

		// A gunshot: it turns and charges there, running the player down.
		var hits = BlindWarden.ChargeHits;
		charges = BlindWarden.Charges;
		player.GetNode<WeaponInventory>( "WeaponInventory" ).Current?.TestFire();
		var start = w.GlobalPosition;
		for ( var t = 0f; t < 4f && BlindWarden.ChargeHits == hits; t += 0.05f ) await Wait( 0.05f );
		GD.Print( $"[autotest] warden: a shot, charges {BlindWarden.Charges - charges}, ran {start.DistanceTo( w.GlobalPosition ):0.0} m, now {w.GlobalPosition.DistanceTo( player.GlobalPosition ):0.0} m from the shooter, hits {BlindWarden.ChargeHits - hits}" );
		Check( BlindWarden.Charges > charges, "the Warden did not charge at a gunshot" );
		Check( BlindWarden.ChargeHits > hits, "the Warden's charge did not run the shooter down" );
		await Wait( 3f );

		// A charge into the statue: it crashes and is dazed, gills wide.
		var crashes = BlindWarden.Crashes;
		var side = Hub( 16f, 250f );
		w.GlobalPosition = side with { Y = SeaTerrain.HeightAt( side.X, side.Z ) };
		w.GlobalBasis = Shapes.LookAt( (Vector3.Zero - side).Flat().Normalized() );
		StandAt( Hub( 18f, 200f ), w.GlobalPosition );
		w.Force( BlindWarden.Act.Charge, Vector3.Zero );
		for ( var t = 0f; t < 3f && BlindWarden.Crashes == crashes; t += 0.05f ) await Wait( 0.05f );
		await Wait( 0.6f );
		GD.Print( $"[autotest] warden: charged at the statue, crashes {BlindWarden.Crashes - crashes}, doing {w.Doing}, gills {w.GillsOpen:0.00}, {w.GlobalPosition.Flat().Length():0.0} m from the middle" );
		Check( BlindWarden.Crashes > crashes && w.Doing == BlindWarden.Act.Daze, "the Warden did not crash into the statue and get dazed" );
		Check( w.GillsOpen > 0.9f, "the dazed Warden's gills are not wide open" );
		Check( w.GlobalPosition.Flat().Length() > 5f, "the Warden ran into the statue" );
		Face( w.GlobalPosition + Vector3.Up * 1.5f );
		await Shot( "warden_dazed" );
		await Wait( 4f );

		// The claw clap: a shock ring that stuns a player in front.
		w.GlobalPosition = home with { Y = SeaTerrain.HeightAt( home.X, home.Z ) };
		w.GlobalBasis = Shapes.LookAt( (Vector3.Zero - home).Flat().Normalized() );
		var ahead = -w.GlobalBasis.Z.Flat().Normalized();
		StandAt( home + ahead * 5f, home );
		var rings = Stingray.RingHits;
		var claps = BlindWarden.Claps;
		w.Force( BlindWarden.Act.Clap );
		await Wait( 0.75f );
		await Shot( "warden_clap" );
		await Wait( 0.8f );
		GD.Print( $"[autotest] warden: a clap, rings that hit {Stingray.RingHits - rings}" );
		Check( BlindWarden.Claps > claps && Stingray.RingHits > rings, "the Warden's claw clap did not hit a player in front" );
		await Wait( 1.2f );

		// The tail flick: rubble at a player behind.
		StandAt( home - ahead * 6f, home );
		var rubble = BlindWarden.RubbleHits;
		w.Force( BlindWarden.Act.Flick );
		for ( var t = 0f; t < 3f && BlindWarden.RubbleHits == rubble; t += 0.05f ) await Wait( 0.05f );
		GD.Print( $"[autotest] warden: a tail flick, rubble that hit {BlindWarden.RubbleHits - rubble}" );
		Check( BlindWarden.RubbleHits > rubble, "the Warden's tail flick did not hit a player behind it" );
		await Wait( 1.5f );

		// The scream: a quiet player far off is marked and charged anyway.
		var far = Hub( 15f, 200f );
		StandAt( far, w.GlobalPosition );
		await Wait( 0.3f );
		charges = BlindWarden.Charges;
		w.Force( BlindWarden.Act.Scream );
		for ( var t = 0f; t < 5f && BlindWarden.Charges == charges; t += 0.05f ) await Wait( 0.05f );
		GD.Print( $"[autotest] warden: a scream, marked {BlindWarden.LocalMarkedUntil - Clock.Now:0.0} s more, charges after {BlindWarden.Charges - charges}" );
		Check( BlindWarden.Screams > 0 && BlindWarden.LocalMarkedUntil > Clock.Now - 3.0, "the Warden's scream did not mark the player" );
		Check( BlindWarden.Charges > charges, "the Warden did not charge a marked player" );
		Face( w.GlobalPosition + Vector3.Up * 1.5f );
		await Shot( "warden_marked" );

		// It dies.
		w.ReceiveShot( 2, w.Health.MaxHealth * 10f, Vector3.Forward, w.GlobalPosition, w.GlobalPosition, null, false );
		await Wait( 0.5f );
		Check( BlindWarden.All.Count == 0, "the Warden is still there after it died" );
		Done();
	}

	async Task LeviathanTest()
	{
		if ( !await ToAbyss() ) { Done(); return; }
		var d = WaveDirector.Current;
		var floor = SeaTerrain.HeightAt( 0f, 0f );
		StandAt( Hub( 14f, 200f ), Vector3.Zero );
		var l = await BossWave( 30, () => Leviathan.All.FirstOrDefault(), "leviathan" );
		if ( l is null ) { Done(); return; }
		Check( GameHud.BossArrives( WaveDirector.EnemyKind.Leviathan ).Contains( "LEVIATHAN" ), "wave 30's banner doesn't name the Leviathan" );
		Check( MathF.Abs( l.Health.MaxHealth - d.LeviathanHealth ) < 1f, $"the Leviathan's health is {l.Health.MaxHealth:0}" );
		Check( l.GlobalPosition.Y > Abyss.RoofAt( 0f, 0f ), "the Leviathan did not come in above the roof" );

		// The mark follows the player, then locks.
		await Wait( 0.6f );
		var moved = Hub( 14f, 250f );
		StandAt( moved, Vector3.Zero );
		await Wait( 0.8f );
		var follow = l.Spot.Flat().DistanceTo( moved.Flat() );
		for ( var t = 0f; t < 4f && !l.Locked; t += 0.05f ) await Wait( 0.05f );
		var lockedAt = l.Spot;
		StandAt( Hub( 14f, 200f ), Vector3.Zero );
		var rings = Stingray.RingHits;
		var rams = Leviathan.Rams;
		var cracks = Leviathan.Cracks;
		for ( var t = 0f; t < 4f && Leviathan.Rams == rams; t += 0.05f ) { if ( l.Doing == Leviathan.Act.Dive && t > 0.5f ) { } await Wait( 0.05f ); }
		await Wait( 0.1f );
		var headToSpot = l.GlobalPosition.DistanceTo( lockedAt );
		GD.Print( $"[autotest] leviathan: the mark followed to {follow:0.0} m from the player, locked, stayed {l.Spot.DistanceTo( lockedAt ):0.0} m; rammed {Leviathan.Rams - rams}, head {headToSpot:0.0} m from the mark, cracks {Leviathan.Cracks - cracks}" );
		Check( follow < 3f, "the Leviathan's mark did not follow the player" );
		Check( l.Spot.DistanceTo( lockedAt ) < 0.1f, "the Leviathan's mark moved after it locked" );
		Check( Leviathan.Rams > rams && headToSpot < 3f, "the Leviathan's ram did not land on its mark" );
		Check( Leviathan.Cracks > cracks, "the ram did not crack the roof" );
		await Wait( 1.2f );
		GD.Print( $"[autotest] leviathan: the shock ring hit {Stingray.RingHits - rings}; doing {l.Doing}, open {l.Open}" );
		Check( Stingray.RingHits > rings, "the ram's shock ring did not hit a player on the floor" );
		Check( l.Doing == Leviathan.Act.Rest && l.Open, "the Leviathan does not rest open after a ram" );
		var skylit = Enumerable.Range( 0, Leviathan.Segments ).Count( i => l.SegmentAt( i ).Y > Abyss.RoofAt( 0f, 0f ) - 2f );
		Check( skylit > 2, "the Leviathan's body does not hang down through the skylight" );
		if ( !string.IsNullOrEmpty( shots ) ) { StandAt( l.GlobalPosition + (l.GlobalPosition.Flat().Length() > 1f ? -l.GlobalPosition.Flat().Normalized() : Vector3.Forward) * 13f + Vector3.Right * 3f, l.GlobalPosition ); Face( l.GlobalPosition + Vector3.Up * 4f ); await Wait( 0.2f ); await Shot( "leviathan_resting" ); }

		// Open, the eyes take triple; the hide half.
		var h0 = l.Health.Current;
		l.ReceiveShot( 2, 100f, Vector3.Forward, l.GlobalPosition, l.GlobalPosition, null, false );
		var eye = h0 - l.Health.Current;
		h0 = l.Health.Current;
		l.ReceiveShot( 1, 100f, Vector3.Forward, l.GlobalPosition, l.GlobalPosition, null, false );
		var hide = h0 - l.Health.Current;
		GD.Print( $"[autotest] leviathan: 100 on an open eye took {eye:0}, on the hide {hide:0}" );
		Check( eye > 250f && hide < 60f, "the Leviathan's weak spots take the wrong damage" );

		// The coil sweep: low enough to jump, it hits a player standing on the ring.
		var ringAt = Hub( l.SweepRadius, MathX.Deg( MathF.Atan2( l.GlobalPosition.Z, l.GlobalPosition.X ) ) + 180f );
		StandAt( ringAt, Vector3.Zero );
		var sweeps = Leviathan.SweepHits;
		var entry = l.GlobalPosition.Flat();
		l.Force( Leviathan.Act.Sweep );
		var tallest = 0f;
		for ( var t = 0f; t < 10f && l.Doing == Leviathan.Act.Sweep; t += 0.05f )
		{
			for ( var i = 0; i < Leviathan.Segments; i++ )
			{
				var c = l.SegmentAt( i );
				// Round the ring, away from where the body comes down into the sand.
				if ( MathF.Abs( c.Flat().Length() - l.SweepRadius ) < 1.5f && c.Flat().DistanceTo( entry ) > 9f ) tallest = MathF.Max( tallest, c.Y + l.SegmentRadius( i ) * 0.95f - floor );
			}
			if ( t > 2.5f && t < 2.56f ) { Face( l.GlobalPosition ); await Shot( "leviathan_sweep" ); }
			await Wait( 0.05f );
		}
		GD.Print( $"[autotest] leviathan: the sweep hit {Leviathan.SweepHits - sweeps}, its back at most {tallest:0.00} m over the floor" );
		Check( Leviathan.SweepHits > sweeps, "the coil sweep did not hit a player on the ring" );
		Check( tallest > 0.2f && tallest < 1.3f, "the coil sweep's back is not low enough to jump" );

		// The song: thrown back and slowed.
		var at = l.GlobalPosition.Flat();
		var toward = (at.Length() > 1f ? -at.Normalized() : Vector3.Forward);
		StandAt( at + toward * 10f, at );
		await Wait( 0.2f );
		var songs = Leviathan.SongHits;
		l.Force( Leviathan.Act.Song );
		await Wait( 1.45f );
		Face( l.GlobalPosition );
		await Shot( "leviathan_song" );
		await Wait( 0.6f );
		var slowed = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Afflictions?.SlowLeft ?? 0f;
		GD.Print( $"[autotest] leviathan: the song hit {Leviathan.SongHits - songs}, slowed {slowed:0.0} s" );
		Check( Leviathan.SongHits > songs && slowed > 0f, "the whale song did not hit and slow a player in front" );
		await Wait( 1.5f );

		// The swallow: pulled in, held in its stomach; the ulcers hurt it; it spits the player out.
		at = l.GlobalPosition.Flat();
		toward = (at.Length() > 1f ? -at.Normalized() : Vector3.Forward);
		StandAt( at + toward * 8f, at );
		await Wait( 0.2f );
		l.Force( Leviathan.Act.Swallow );
		for ( var t = 0f; t < 5f && l.SwallowedCount == 0; t += 0.05f ) await Wait( 0.05f );
		await Wait( 0.3f );
		GD.Print( $"[autotest] leviathan: swallowed {Leviathan.Swallows}, the player at y {player.GlobalPosition.Y:0}, in the stomach {Leviathan.SwallowedLocal}" );
		Check( Leviathan.SwallowedLocal, "the swallow did not take the player into its stomach" );
		player.EyeAngles = new Angles( 8f, 30f, 0f );
		await Wait( 0.1f );
		await Shot( "leviathan_stomach" );
		h0 = l.Health.Current;
		l.ReceiveShot( 8, 100f, Vector3.Forward, player.GlobalPosition, player.GlobalPosition, null, false );
		var ulcer = h0 - l.Health.Current;
		var stomachRay = Trace.Ray( player, player.GlobalPosition + Vector3.Up, player.GlobalPosition + Vector3.Up + Vector3.Right * 20f, Layers.Creature );
		GD.Print( $"[autotest] leviathan: 100 on an ulcer took {ulcer:0}; a shot across the stomach hits {stomachRay.Node?.Name}" );
		Check( ulcer > 350f, "the stomach's ulcers don't take extra damage" );
		Check( stomachRay.Hit && stomachRay.Node is CreaturePart { Index: 7 or 8 }, "a shot inside the stomach does not hit its wall" );
		l.ReceiveShot( 7, l.Health.MaxHealth * 0.05f, Vector3.Forward, player.GlobalPosition, player.GlobalPosition, null, false );
		for ( var t = 0f; t < 9f && Leviathan.SwallowedLocal; t += 0.1f ) await Wait( 0.1f );
		GD.Print( $"[autotest] leviathan: spat out {Leviathan.Spits}, the player at y {player.GlobalPosition.Y:0.0}, held {player.Scripted}" );
		Check( !Leviathan.SwallowedLocal && Leviathan.Spits > 0 && !player.Scripted, "the Leviathan did not spit the player back out" );
		await Wait( 0.5f );

		// The brood: puffers and an angler.
		var enemies = Creature.Everyone.Count( c => c is PufferBomber or Angler );
		l.Force( Leviathan.Act.Brood );
		await Wait( 2.6f );
		var brood = Creature.Everyone.Count( c => c is PufferBomber or Angler ) - enemies;
		GD.Print( $"[autotest] leviathan: the brood brought {brood} enemies" );
		Check( brood >= 3, "the Leviathan's brood brought too few enemies" );
		foreach ( var c in Creature.Everyone.Where( c => c is PufferBomber or Angler ).ToList() ) c.QueueFree();

		// Dead, it lies across the hub.
		l.ReceiveShot( 1, l.Health.MaxHealth * 10f, Vector3.Forward, l.GlobalPosition, l.GlobalPosition, null, false );
		await Wait( 4f );
		var carcass = GetTree().Root.FindChildren( "Leviathan Body", "", true, false ).OfType<Node3D>().FirstOrDefault();
		var lying = carcass?.GetChildren().OfType<Node3D>().Where( n => n.Name.ToString().StartsWith( "Segment" ) ).ToList() ?? new();
		var high = lying.Count > 0 ? lying.Max( n => n.GlobalPosition.Y - floor ) : 99f;
		var inStatue = lying.Count( n => n.GlobalPosition.Flat().Length() < 5f );
		GD.Print( $"[autotest] leviathan: dead, {Leviathan.All.Count} left, carcass {LeviathanCarcass.Laid}, {lying.Count} segments lying, highest {high:0.0} m, in the statue {inStatue}" );
		Check( Leviathan.All.Count == 0 && LeviathanCarcass.Laid > 0, "the Leviathan left no carcass" );
		Check( lying.Count == Leviathan.Segments && high < 3f, "the Leviathan's carcass is not lying on the floor" );
		Check( inStatue == 0, "the Leviathan's carcass lies through the statue" );
		if ( lying.Count > 0 ) { var mid = lying[lying.Count / 2].GlobalPosition; StandAt( (mid.Flat().Length() > 1f ? mid.Flat().Normalized() : Vector3.Forward) * 6.8f + mid.Flat().Normalized().Cross( Vector3.Up ) * 4f, mid ); Face( mid ); }
		await Wait( 0.3f );
		await Shot( "leviathan_carcass" );
		Done();
	}

}
