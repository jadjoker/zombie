using Godot;

/// <summary>
/// --queen: the Mermaid Queen, Atlantis's wave 35 boss. Wave 35 brings her (her banner and her health), she grows out of a
/// whirlpool, and on command: her siren song draws a player standing in the open toward her, turns their view to her and
/// she bites the one who reaches her; shot in the throat while she sings she chokes and takes more from every hit; her
/// pearls fly at the player and burst on them, and a shot one bursts harmlessly; her riptide crescents hit a player on
/// the ground and miss one in the air; her royal guard is mermen in the wave; her gaze beam burns a player in the open;
/// her dash brings her back near a player who ran; at half health she ascends (crown grown, mouth split); left to fight
/// on her own for a while she stays inside the globe and over the floor; dead, she bursts. With --shots (a window),
/// pictures of her and of each attack.
/// </summary>
public partial class AutoTest
{
	async Task QueenTest()
	{
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		var d = WaveDirector.Current;
		d.SpawningPaused = true;
		d.SuitMalfunctionEvery = 0;
		AllyCommander.KeepAlly = false;
		foreach ( var ally in SeaSpider.All.Where( s => !s.IsEnemy ).ToList() ) ally.QueueFree();
		var level = GetTree().CurrentScene as Node3D;
		AtlantisArea.Go( level, player );
		await Wait( 1f );
		await WaitForNav();
		Check( SeaTerrain.Area == SeaTerrain.Globe, "did not get to Atlantis" );
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.ResetTo( 5000f );

		var was = d.Wave;
		d.Wave = 35; var on35 = d.BossKind;
		d.Wave = 40; var on40 = d.BossKind;
		d.Wave = 30; var on30 = d.BossKind;
		d.Wave = 25; var on25 = d.BossKind;
		d.Wave = was;
		Check( on35 == WaveDirector.EnemyKind.Queen && on40 == WaveDirector.EnemyKind.Poseidon && on25 == WaveDirector.EnemyKind.Warden && on30 != WaveDirector.EnemyKind.Queen,
			$"the boss waves are wrong (25 {on25}, 30 {on30}, 35 {on35}, 40 {on40})" );

		// The open plaza on the west avenue, the queen in toward the middle along it.
		var home = Atlantis.KrakenCentre;
		home = Nav.Closest( player, home );
		void Stand( Vector3 at, Vector3 look )
		{
			player.GlobalPosition = at + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			var dir = (look - at).Flat().SafeNormal();
			player.EyeAngles = new Angles( -4f, MathX.Deg( MathF.Atan2( -dir.X, -dir.Z ) ), 0f );
		}
		Stand( home, Vector3.Zero );
		var q = await BossWave( 35, () => MermaidQueen.All.FirstOrDefault(), "queen" );
		if ( q is null ) { Done(); return; }
		Check( GameHud.BossArrives( WaveDirector.EnemyKind.Queen ).Contains( "MERMAID QUEEN" ), "wave 35's banner doesn't name the Mermaid Queen" );
		Check( MathF.Abs( q.Health.MaxHealth - d.QueenHealth ) < 1f && d.QueenHealth > d.LeviathanHealth && d.QueenHealth < d.PoseidonHealth, $"the queen's health is {q.Health.MaxHealth:0}" );
		q.OnlyWhenTold = true;
		var inward = (Vector3.Zero - home).Flat().Normalized();
		var queenAt = home + inward * 13f;
		void PlaceQueen()
		{
			q.GlobalPosition = queenAt with { Y = Sculpt.FloorBelow( q, queenAt + Vector3.Up * 20f, 60f ) + q.HoverHeight };
			q.GlobalBasis = Shapes.LookAt( -inward );
		}
		PlaceQueen();
		for ( var t = 0f; t < 3f && q.Doing == MermaidQueen.Act.Arrive; t += 0.1f ) await Wait( 0.1f );
		Check( q.Doing == MermaidQueen.Act.Swim, $"the queen did not finish coming up out of her whirlpool ({q.Doing})" );
		GD.Print( $"[autotest] queen: at {q.GlobalPosition}, {q.GlobalPosition.Y - Sculpt.FloorBelow( q, q.GlobalPosition, 60f ):0.0} m over the floor, player {home}" );

		var hidden = new List<CanvasLayer>();
		var guns = new List<Node3D>();
		void HideHud()
		{
			foreach ( var n in GetTree().Root.FindChildren( "*", "", true, false ) )
			{
				if ( n is CanvasLayer cl && cl.Visible ) { cl.Visible = false; hidden.Add( cl ); }
				if ( n is ViewModel or ViewModelAxe && n is Node3D vm && vm.Visible ) { vm.Visible = false; guns.Add( vm ); }
			}
		}
		void ShowHud()
		{
			foreach ( var cl in hidden ) if ( IsInstanceValid( cl ) ) cl.Visible = true;
			foreach ( var g in guns ) if ( IsInstanceValid( g ) ) g.Visible = true;
			hidden.Clear();
			guns.Clear();
		}
		async Task View( string name, Vector3 eye, Vector3 at )
		{
			if ( shots is null ) return;
			Transform3D view = new( Basis.LookingAt( (at - eye).Normalized(), Vector3.Up ), eye );
			player.CameraOverride = _ => view;
			player.ThirdPerson = true;   // no gun in the way
			await Wait( 0.15f );
			await Shot( name );
			player.ThirdPerson = false;
			player.CameraOverride = null;
		}
		Vector3 Front() => (-q.GlobalBasis.Z).Flat().Normalized();
		Vector3 Side() => Vector3.Up.Cross( Front() );

		// Pictures of her: front, three-quarter, side, back (hair and collar), the face, the tail.
		if ( shots is not null )
		{
			HideHud();
			DevFlags.FreezeEnemies = true;
			await Wait( 0.5f );
			var middle = q.GlobalPosition + Vector3.Up * 0.8f;
			var face = q.HeadNode.GlobalPosition + Vector3.Up * 0.45f;
			await View( "queen", middle + Front() * 16f + Vector3.Down * 1f, middle + Vector3.Down * 1.5f );
			await View( "queen_three_quarter", middle + Front() * 11f + Side() * 9f + Vector3.Up * 1f, middle );
			await View( "queen_side", middle + Side() * 15f + Vector3.Down * 1f, middle + Vector3.Down * 1.6f );
			await View( "queen_back", middle + -Front() * 9f + Side() * 3f + Vector3.Up * 3f, face );
			// The face from where she looks (at the player), close.
			var toPlayer = (player.GlobalPosition + Vector3.Up * 1.6f - face).Normalized();
			await View( "queen_face", face + toPlayer * 3.4f, face );
			await View( "queen_face_side", face + (toPlayer + Side() * 0.8f).Normalized() * 3f + Vector3.Up * 0.3f, face );
			await View( "queen_tail", middle + Front() * 8f + Side() * 7f + Vector3.Down * 4f, middle + Vector3.Down * 5f );
			DevFlags.FreezeEnemies = false;
			ShowHud();
			if ( OS.GetCmdlineUserArgs().Contains( "--portrait" ) ) { Done(); return; }
		}

		// Hits: the head and heart take more than the chest, the tail less, the throat (shut) no more than the chest.
		{
			var h = q.Health;
			float Dealt( int index ) { var before = h.Current; q.ReceiveShot( index, 10f, Vector3.Forward, q.GlobalPosition, home, survivor, false ); return before - h.Current; }
			var head = Dealt( 0 ); var heart = Dealt( 1 ); var chest = Dealt( 2 ); var tail = Dealt( 5 );
			GD.Print( $"[autotest] queen: 10 damage on the head {head:0.0}, heart {heart:0.0}, chest {chest:0.0}, tail {tail:0.0}" );
			Check( head > chest && heart > head && tail < chest, "the queen's weak spots and tail don't scale damage" );
			Check( q.WeakSpot( 0 ) && q.WeakSpot( 1 ) && !q.WeakSpot( MermaidQueen.ThroatIndex ), "the queen's weak spots are wrong while her mouth is shut" );
			h.Current = h.MaxHealth;
		}

		// ---- Siren Song: drawn in, the view turned to her, bitten ----
		survivor.Health.Heal( 9999f );
		Stand( home, home - inward * 10f );   // facing away from her
		var startYaw = player.EyeAngles.Yaw;
		var startGap = player.GlobalPosition.Flat().DistanceTo( q.GlobalPosition.Flat() );
		var bites = MermaidQueen.Bites;
		var before = survivor.Health.Current;
		q.Force( "song" );
		Check( q.Line == "SIREN SONG", "the song did not put its name up" );
		await Wait( q.SongWindUp + 1.2f );
		var charmed = MermaidQueen.LocalCharmed;
		var turned = MathF.Abs( Mathf.Wrap( player.EyeAngles.Yaw - startYaw, -180f, 180f ) );
		var gap = player.GlobalPosition.Flat().DistanceTo( q.GlobalPosition.Flat() );
		GD.Print( $"[autotest] queen song: gap {startGap:0.0} to {gap:0.0} m, view turned {turned:0} degrees, charmed {charmed}, throat open {q.ThroatOpen}" );
		Check( charmed && turned > 15f, "the song did not charm the player and turn their view to her" );
		Check( gap < startGap - 2f, "the song did not draw the player in" );
		Check( q.WeakSpot( MermaidQueen.ThroatIndex ), "her throat is not a weak spot while she sings" );
		if ( shots is not null ) { HideHud(); await View( "queen_song", q.GlobalPosition + Front() * 12f + Side() * 7f + Vector3.Up * 0.5f, q.HeadNode.GlobalPosition ); ShowHud(); }
		for ( var t = 0f; t < 8f && MermaidQueen.Bites == bites; t += 0.1f ) await Wait( 0.1f );
		await Wait( 0.6f );
		GD.Print( $"[autotest] queen bite: bites {MermaidQueen.Bites - bites}, health {before:0} to {survivor.Health.Current:0}" );
		Check( MermaidQueen.Bites > bites && survivor.Health.Current <= before - q.BiteDamage + 1f, "the queen did not bite the player her song drew in" );
		await Wait( 1.2f );
		Check( !player.Scripted, "the player is held after the bite" );

		// ---- Choked: shot in the throat as she sings ----
		survivor.Health.Heal( 9999f );
		PlaceQueen();
		Stand( home, q.GlobalPosition );
		await Wait( 0.3f );
		var chokes = MermaidQueen.Chokes;
		q.Force( "song" );
		await Wait( q.SongWindUp + 0.2f );
		var throatBefore = q.Health.Current;
		q.ReceiveShot( MermaidQueen.ThroatIndex, 10f, Vector3.Forward, q.HeadAt, home, survivor, true );
		var throatDealt = throatBefore - q.Health.Current;
		for ( var i = 0; i < 40 && q.Doing == MermaidQueen.Act.Song; i++ ) q.ReceiveShot( MermaidQueen.ThroatIndex, 40f, Vector3.Forward, q.HeadAt, home, survivor, true );
		GD.Print( $"[autotest] queen choke: 10 on the open throat did {throatDealt:0.0}, chokes {MermaidQueen.Chokes - chokes}, doing {q.Doing}" );
		Check( throatDealt >= 29f, "her open throat does not take triple" );
		Check( MermaidQueen.Chokes > chokes && q.Doing == MermaidQueen.Act.Choke, "shooting her throat did not choke the song off" );
		var chokeBefore = q.Health.Current;
		q.ReceiveShot( 2, 10f, Vector3.Forward, q.GlobalPosition, home, survivor, false );
		Check( chokeBefore - q.Health.Current >= 14.9f, "she does not take more while choking" );
		await Wait( 0.3f );
		Check( !MermaidQueen.LocalCharmed, "the player is still charmed after she choked" );
		q.Health.Current = q.Health.MaxHealth;
		for ( var t = 0f; t < 6f && q.Doing != MermaidQueen.Act.Swim; t += 0.1f ) await Wait( 0.1f );

		// ---- Pearl Storm: they fly and burst on the player; one shot down bursts harmlessly ----
		survivor.Health.Heal( 9999f );
		PlaceQueen();
		Stand( home, q.GlobalPosition );
		await Wait( 0.3f );
		var thrown = MermaidQueen.PearlsThrown;
		var pearlHits = MermaidQueen.PearlHits;
		var shotDown = MermaidQueen.PearlsShot;
		before = survivor.Health.Current;
		q.Force( "pearls" );
		await Wait( 0.8f );
		var held = QueenPearl.All.FirstOrDefault( p => p.Held );
		Check( held is not null && QueenPearl.All.Count( p => p.Held ) >= q.PearlCount, "the pearls did not gather round her sceptre" );
		if ( held?.GetNodeOrNull<PearlBody>( "Pearl Body" ) is { } body ) body.ReceiveShot( 30f, body, Vector3.Forward, held.GlobalPosition, home, player, true );
		if ( shots is not null ) { HideHud(); await View( "queen_pearls", q.GlobalPosition + Front() * 10f + Side() * 7f + Vector3.Up * 1f, q.OrbAt ); ShowHud(); }
		for ( var t = 0f; t < 7f && (QueenPearl.All.Count > 0 || q.Doing == MermaidQueen.Act.Pearls); t += 0.1f ) await Wait( 0.1f );
		GD.Print( $"[autotest] queen pearls: thrown {MermaidQueen.PearlsThrown - thrown}, hits {MermaidQueen.PearlHits - pearlHits}, shot down {MermaidQueen.PearlsShot - shotDown}, health {before:0} to {survivor.Health.Current:0}, left {QueenPearl.All.Count}" );
		Check( MermaidQueen.PearlsShot > shotDown, "a shot did not burst a pearl" );
		Check( MermaidQueen.PearlsThrown - thrown >= q.PearlCount - 1, "she did not throw her pearls" );
		Check( MermaidQueen.PearlHits > pearlHits && survivor.Health.Current < before, "her pearls did not hit a player standing still" );
		Check( QueenPearl.All.Count == 0, "pearls were left about" );

		// ---- Riptide: hits a player on the ground, misses one in the air ----
		survivor.Health.Heal( 9999f );
		PlaceQueen();
		Stand( home, q.GlobalPosition );
		await Wait( 0.3f );
		var waveHits = MermaidQueen.WaveHits;
		before = survivor.Health.Current;
		q.Force( "riptide" );
		await Wait( 1.4f );
		if ( shots is not null ) { HideHud(); await View( "queen_riptide", home - inward * 2f + Side() * 2f + Vector3.Up * 1.6f, q.GlobalPosition with { Y = home.Y + 2f } ); ShowHud(); }
		for ( var t = 0f; t < 4f && QueenWave.All.Count > 0; t += 0.1f ) await Wait( 0.1f );
		GD.Print( $"[autotest] queen riptide: hits {MermaidQueen.WaveHits - waveHits}, health {before:0} to {survivor.Health.Current:0}" );
		Check( MermaidQueen.WaveHits > waveHits && survivor.Health.Current < before, "her riptide did not hit a player on the ground" );
		await Wait( 1.5f );
		PlaceQueen();
		Stand( home, q.GlobalPosition );
		await Wait( 0.5f );
		waveHits = MermaidQueen.WaveHits;
		var jumps = MermaidQueen.WaveJumps;
		q.Force( "riptide" );
		for ( var t = 0f; t < 5f && (q.Doing == MermaidQueen.Act.Riptide || QueenWave.All.Count > 0); t += 0.02f )
		{
			player.GlobalPosition = home with { Y = Sculpt.GroundAt( home ) + 1.25f };
			player.Velocity = Vector3.Zero;
			await Wait( 0.02f );
		}
		GD.Print( $"[autotest] queen riptide in the air: jumped {MermaidQueen.WaveJumps - jumps}, hits {MermaidQueen.WaveHits - waveHits}" );
		Check( MermaidQueen.WaveJumps > jumps && MermaidQueen.WaveHits == waveHits, "a player in the air did not clear her riptide" );
		Stand( home, q.GlobalPosition );

		// ---- Royal Guard: mermen in the wave ----
		var alive = d.AliveCount;
		q.Force( "guard" );
		await Wait( 2.6f );
		var guard = Mermaid.All.Where( m => m.Guard == q && !m.IsDead ).ToList();
		GD.Print( $"[autotest] queen guard: {guard.Count} guards ({guard.Count( m => m.Male )} men), wave {alive} to {d.AliveCount}" );
		Check( guard.Count == q.GuardCount && guard.All( m => m.Male ) && d.AliveCount >= alive + guard.Count, "her royal guard did not come into the wave" );
		if ( shots is not null && guard.Count > 0 ) { HideHud(); DevFlags.FreezeEnemies = true; await View( "queen_guard", q.GlobalPosition + Front() * 15f + Vector3.Down * 2f, q.GlobalPosition + Vector3.Down * 3f ); DevFlags.FreezeEnemies = false; ShowHud(); }
		foreach ( var m in guard ) m.Health.OnDamage( new DamageInfo { Damage = 1e6f } );
		await Wait( 0.5f );

		// ---- Abyssal Gaze: the beam burns a player in the open ----
		survivor.Health.Heal( 9999f );
		PlaceQueen();
		Stand( home, q.GlobalPosition );
		await Wait( 0.3f );
		var beamHits = MermaidQueen.BeamHits;
		before = survivor.Health.Current;
		q.Force( "gaze" );
		await Wait( q.GazeCharge + 0.9f );
		if ( shots is not null ) { HideHud(); await View( "queen_gaze", q.GlobalPosition + Side() * 14f + Front() * 7f + Vector3.Down * 2f, (q.GlobalPosition + q.GazeAt) * 0.5f ); ShowHud(); }
		for ( var t = 0f; t < 4f && q.Doing == MermaidQueen.Act.Gaze; t += 0.1f ) await Wait( 0.1f );
		GD.Print( $"[autotest] queen gaze: hits {MermaidQueen.BeamHits - beamHits}, health {before:0} to {survivor.Health.Current:0}, beam damage {MermaidQueen.BeamDamage:0}" );
		Check( MermaidQueen.BeamHits > beamHits && survivor.Health.Current < before - 20f, "her gaze did not burn a player in the open" );

		// ---- Tide Dash: back near a player who ran ----
		var far = Nav.Closest( player, Atlantis.Polar( 70f, 130f, Atlantis.Land ) );
		Stand( far, q.GlobalPosition );
		await Wait( 0.2f );
		q.Force( "dash" );
		for ( var t = 0f; t < 4f && q.Doing == MermaidQueen.Act.Dash; t += 0.1f ) await Wait( 0.1f );
		var dashGap = q.GlobalPosition.Flat().DistanceTo( far.Flat() );
		var inside = Sculpt.InBounds( q.GlobalPosition ).Flat().DistanceTo( q.GlobalPosition.Flat() ) < 0.01f;
		GD.Print( $"[autotest] queen dash: {dashGap:0.0} m from the player, inside {inside}" );
		Check( dashGap < 17f && inside, "her dash did not bring her back near the player" );
		Stand( home, q.GlobalPosition );

		// ---- Ascend at half health ----
		PlaceQueen();
		Stand( home, q.GlobalPosition );
		await Wait( 0.3f );
		q.Health.Current = q.Health.MaxHealth * 0.49f;
		await Wait( 0.3f );
		Check( q.Doing == MermaidQueen.Act.Ascend, $"the queen did not ascend at half health ({q.Doing})" );
		await Wait( 1.4f );
		if ( shots is not null ) { HideHud(); await View( "queen_ascend", home + Vector3.Up * 2f, q.HeadNode.GlobalPosition ); ShowHud(); }
		for ( var t = 0f; t < 3f && q.Doing == MermaidQueen.Act.Ascend; t += 0.1f ) await Wait( 0.1f );
		GD.Print( $"[autotest] queen ascend: ascended {q.Ascended}, ascents {MermaidQueen.Ascents}, doing {q.Doing}" );
		Check( q.Ascended && MermaidQueen.Ascents == 1, "the queen did not ascend" );
		if ( shots is not null )
		{
			HideHud();
			DevFlags.FreezeEnemies = true;
			var face = q.HeadNode.GlobalPosition + Vector3.Up * 0.45f;
			await View( "queen_ascended_face", face + Front() * 4f + Side() * 1f + Vector3.Up * 0.4f, face );
			DevFlags.FreezeEnemies = false;
			ShowHud();
		}

		// ---- On her own for a while: inside the globe, over the floor, using her moves ----
		survivor.Health.Heal( 9999f );
		survivor.Health.Invulnerable = true;
		q.OnlyWhenTold = false;
		var moves = MermaidQueen.Songs + MermaidQueen.PearlsThrown + MermaidQueen.Riptides + MermaidQueen.Guards + MermaidQueen.Gazes;
		var lowest = 99f;
		var outside = false;
		for ( var t = 0f; t < 16f; t += 0.25f )
		{
			await Wait( 0.25f );
			if ( !IsInstanceValid( q ) ) break;
			var floor = Sculpt.FloorBelow( q, q.GlobalPosition + Vector3.Up * 0.5f, 60f );
			lowest = MathF.Min( lowest, q.GlobalPosition.Y - floor );
			if ( q.GlobalPosition.Flat().Length() > Atlantis.BaseInner - 7f ) outside = true;
		}
		var used = MermaidQueen.Songs + MermaidQueen.PearlsThrown + MermaidQueen.Riptides + MermaidQueen.Guards + MermaidQueen.Gazes - moves;
		GD.Print( $"[autotest] queen free: {used} moves, lowest {lowest:0.0} m over the floor, left the globe {outside}" );
		Check( used > 0, "left to herself she never attacked" );
		Check( !outside && lowest > 1.5f, "left to herself she left the globe or sank to the floor" );
		survivor.Health.Invulnerable = false;
		foreach ( var m in Mermaid.All.ToList() ) m.Health.OnDamage( new DamageInfo { Damage = 1e6f } );

		// ---- Dead: she bursts ----
		var killed = false;
		q.Killed += _ => killed = true;
		q.Health.OnDamage( new DamageInfo { Damage = 1e7f } );
		await Wait( MermaidQueen.SwellSeconds + 1f );
		Check( killed && !IsInstanceValid( q ), "the queen did not die and burst" );
		Check( QueenPearl.All.Count == 0 && !MermaidQueen.LocalCharmed, "her pearls or her song outlived her" );
		Done();
	}
}
