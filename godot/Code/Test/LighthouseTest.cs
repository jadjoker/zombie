using Godot;

/// <summary>
/// --lighthouse: the Lighthouse Hermit, the shore's wave 10 boss. Wave 10 brings him (his banner and his health) up out of the
/// sand, and on command: the claw slam hurts a player in front of him and its sand rings hit one on the ground further off
/// and miss one in the air; the burrow takes him under the sand to the player and he bursts up under them, throwing them up;
/// the foghorn jams when it is shot as it winds up (he takes more while jammed), or blows and rolls thick fog over the
/// player (their view closes in) with small hermit crabs coming in under it, and the fog lifts after; the lantern beam bares
/// his belly (a weak spot then), burns a player in the open and leaves a trail that burns, and shooting his belly puts it
/// out; at half health he is enraged; left on his own he stays on the sand inside the map and attacks; dead, he bursts and
/// the fog is gone. With --shots (a window), pictures of him and of each attack.
/// </summary>
public partial class AutoTest
{
	async Task LighthouseTest()
	{
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		await WaitForNav();
		var d = WaveDirector.Current;
		d.SpawningPaused = true;
		d.SuitMalfunctionEvery = 0;
		AllyCommander.KeepAlly = false;
		foreach ( var ally in SeaSpider.All.Where( s => !s.IsEnemy ).ToList() ) ally.QueueFree();
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.ResetTo( 5000f );

		var was = d.Wave;
		d.Wave = 10; var on10 = d.BossKind;
		d.Wave = 5; var on5 = d.BossKind;
		d.Wave = 15; var on15 = d.BossKind;
		d.Wave = was;
		Check( on10 == WaveDirector.EnemyKind.Lighthouse && on5 == WaveDirector.EnemyKind.Boss && on15 == WaveDirector.EnemyKind.KingRaymond,
			$"the shore's boss waves are wrong (5 {on5}, 10 {on10}, 15 {on15})" );
		Check( d.BossHealthAt( 5 ) > 2499f && d.BossHealthAt( 5 ) < 2501f, $"MC-51 has {d.BossHealthAt( 5 ):0} on wave 5, not 2500" );

		var (open, openYaw) = OpenLane( 22f );
		var ahead = new Vector3( -MathF.Sin( MathX.Rad( openYaw ) ), 0f, -MathF.Cos( MathX.Rad( openYaw ) ) );
		var side = Vector3.Up.Cross( ahead );
		void Stand( Vector3 at, Vector3 look )
		{
			player.GlobalPosition = at + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			var dir = (look - at).Flat().SafeNormal();
			player.EyeAngles = new Angles( -4f, MathX.Deg( MathF.Atan2( -dir.X, -dir.Z ) ), 0f );
		}
		Stand( open, open + ahead );
		var h = await BossWave( 10, () => LighthouseHermit.All.FirstOrDefault(), "lighthouse" );
		if ( h is null ) { Done(); return; }
		Check( GameHud.BossArrives( WaveDirector.EnemyKind.Lighthouse ).Contains( "LIGHTHOUSE HERMIT" ), "wave 10's banner doesn't name the Lighthouse Hermit" );
		Check( MathF.Abs( h.Health.MaxHealth - d.LighthouseHealth ) < 1f && d.LighthouseHealth > d.BossHealthAt( 5 ) && d.LighthouseHealth < d.KingRaymondHealth, $"the hermit's health is {h.Health.MaxHealth:0}" );
		h.OnlyWhenTold = true;
		var hermitAt = open + ahead * 13f;
		void PlaceHermit()
		{
			h.GlobalPosition = hermitAt with { Y = Sculpt.GroundAt( hermitAt ) };
			h.GlobalBasis = Shapes.LookAt( -ahead );
		}
		for ( var t = 0f; t < 4f && h.Doing == LighthouseHermit.Act.Arrive; t += 0.1f ) await Wait( 0.1f );
		Check( h.Doing == LighthouseHermit.Act.Walk, $"the hermit did not finish coming up out of the sand ({h.Doing})" );
		PlaceHermit();
		await Wait( 0.5f );
		GD.Print( $"[autotest] lighthouse: at {h.GlobalPosition}, player {open}, lamp {h.LampAt.Y - h.GlobalPosition.Y:0.0} m up" );
		Check( h.LampAt.Y - h.GlobalPosition.Y > 7f, "the lighthouse does not stand tall over him" );

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
			HideHud();
			Transform3D view = new( Basis.LookingAt( (at - eye).Normalized(), Vector3.Up ), eye );
			player.CameraOverride = _ => view;
			player.ThirdPerson = true;
			await Wait( 0.15f );
			await Shot( name );
			player.ThirdPerson = false;
			player.CameraOverride = null;
			ShowHud();
		}
		Vector3 Front() => (-h.GlobalBasis.Z).Flat().Normalized();
		Vector3 Side() => Vector3.Up.Cross( Front() );

		if ( shots is not null )
		{
			DevFlags.FreezeEnemies = true;
			await Wait( 0.5f );
			var middle = h.GlobalPosition + Vector3.Up * 3.5f;
			await View( "lighthouse", middle + Front() * 17f + Vector3.Up * 0.5f, middle + Vector3.Up * 1f );
			await View( "lighthouse_three_quarter", middle + Front() * 12f + Side() * 11f + Vector3.Up * 1.5f, middle );
			await View( "lighthouse_side", middle + Side() * 17f, middle + Vector3.Up * 0.5f );
			await View( "lighthouse_back", middle - Front() * 15f + Side() * 5f + Vector3.Up * 2f, middle + Vector3.Up * 1f );
			var face = h.GlobalPosition + Vector3.Up * 3f + Front() * 2.2f;
			await View( "lighthouse_face", face + Front() * 6.5f + Vector3.Up * 1.2f + Side() * 1.5f, face );
			await View( "lighthouse_lamp", h.LampAt + Front() * 6f + Vector3.Down * 1f + Side() * 2f, h.LampAt );
			await View( "lighthouse_low", h.GlobalPosition + Front() * 6f + Side() * 4f + Vector3.Up * 0.6f, h.GlobalPosition + Vector3.Up * 1.4f );
			DevFlags.FreezeEnemies = false;
			if ( OS.GetCmdlineUserArgs().Contains( "--portrait" ) ) { Done(); return; }
		}

		// Hits: the lamp and eyes take more than the thorax, the claws less, the tower far less, the belly (shut) no more.
		{
			var hp = h.Health;
			float Dealt( int index ) { var before = hp.Current; h.ReceiveShot( index, 10f, Vector3.Forward, h.GlobalPosition, open, survivor, false ); return before - hp.Current; }
			var lamp = Dealt( LighthouseHermit.LampIndex ); var eye = Dealt( LighthouseHermit.EyeIndex ); var thorax = Dealt( LighthouseHermit.ThoraxIndex );
			var claw = Dealt( LighthouseHermit.BigClawIndex ); var tower = Dealt( LighthouseHermit.TowerIndex ); var belly = Dealt( LighthouseHermit.BellyIndex );
			GD.Print( $"[autotest] lighthouse: 10 damage on the lamp {lamp:0.0}, eye {eye:0.0}, thorax {thorax:0.0}, claw {claw:0.0}, tower {tower:0.0}, shut belly {belly:0.0}" );
			Check( lamp > thorax && eye > thorax && claw < thorax && tower < claw && belly <= thorax + 0.01f, "the hermit's parts don't scale damage" );
			Check( h.WeakSpot( LighthouseHermit.LampIndex ) && !h.WeakSpot( LighthouseHermit.BellyIndex ), "the hermit's weak spots are wrong while his belly is in" );
			hp.Current = hp.MaxHealth;
		}

		// ---- Claw Slam: in front of him it hurts; further off its sand rings hit on the ground, miss in the air ----
		survivor.Health.Heal( 9999f );
		PlaceHermit();
		var close = h.GlobalPosition + Front() * 5f;
		Stand( close, h.GlobalPosition );
		await Wait( 0.3f );
		var slamHits = LighthouseHermit.SlamHits;
		var before = survivor.Health.Current;
		h.Force( "slam" );
		Check( h.Line == "CLAW SLAM", "the slam did not put its name up" );
		await Wait( 0.5f );
		if ( shots is not null ) await View( "lighthouse_slam_up", h.GlobalPosition + Front() * 11f + Side() * 7f + Vector3.Up * 1.5f, h.GlobalPosition + Vector3.Up * 3f );
		for ( var t = 0f; t < 2f && LighthouseHermit.SlamHits == slamHits; t += 0.05f ) await Wait( 0.05f );
		if ( shots is not null ) await View( "lighthouse_slam", h.GlobalPosition + Front() * 12f + Side() * 8f + Vector3.Up * 2f, h.GlobalPosition + Front() * 3f );
		GD.Print( $"[autotest] lighthouse slam: hits {LighthouseHermit.SlamHits - slamHits}, health {before:0} to {survivor.Health.Current:0}" );
		Check( LighthouseHermit.SlamHits > slamHits && survivor.Health.Current <= before - h.SlamDamage + 1f, "the claw slam did not hit a player in front of him" );
		await Wait( 2.2f );
		PlaceHermit();
		var mid = h.GlobalPosition + Front() * 13f;
		Stand( mid, h.GlobalPosition );
		await Wait( 0.3f );
		var ringHits = Stingray.RingHits;
		h.Force( "slam" );
		await Wait( 2.6f );
		GD.Print( $"[autotest] lighthouse rings: hits {Stingray.RingHits - ringHits}" );
		Check( Stingray.RingHits > ringHits, "his sand rings did not hit a player on the ground" );
		await Wait( 1f );
		PlaceHermit();
		Stand( mid, h.GlobalPosition );
		await Wait( 0.3f );
		ringHits = Stingray.RingHits;
		var ringJumps = Stingray.RingJumps;
		h.Force( "slam" );
		for ( var t = 0f; t < 3.4f; t += 0.02f )
		{
			player.GlobalPosition = mid with { Y = Sculpt.GroundAt( mid ) + 1.2f };
			player.Velocity = Vector3.Zero;
			await Wait( 0.02f );
		}
		GD.Print( $"[autotest] lighthouse rings in the air: jumped {Stingray.RingJumps - ringJumps}, hits {Stingray.RingHits - ringHits}" );
		Check( Stingray.RingJumps > ringJumps && Stingray.RingHits == ringHits, "a player in the air did not clear his sand rings" );
		Stand( open, h.GlobalPosition );
		await Wait( 1f );

		// ---- Burrow Ambush: under the sand to the player, then up under them ----
		survivor.Health.Heal( 9999f );
		PlaceHermit();
		Stand( open, h.GlobalPosition );
		await Wait( 0.3f );
		var eruptHits = LighthouseHermit.EruptHits;
		before = survivor.Health.Current;
		h.Force( "burrow" );
		Check( h.Doing == LighthouseHermit.Act.Dig, $"the hermit did not dig in on open sand ({h.Doing})" );
		for ( var t = 0f; t < 3f && h.Doing == LighthouseHermit.Act.Dig; t += 0.1f ) await Wait( 0.1f );
		await Wait( 0.4f );
		var depth = Sculpt.GroundAt( h.GlobalPosition ) - h.GlobalPosition.Y;
		var lampOver = h.LampAt.Y - Sculpt.GroundAt( h.LampAt );
		GD.Print( $"[autotest] lighthouse buried: {depth:0.0} m down, the lamp {lampOver:0.0} m over the sand, doing {h.Doing}" );
		Check( h.Underground && depth > 5f && lampOver > 0.3f, "under the sand he is not buried with his lamp showing" );
		if ( shots is not null ) await View( "lighthouse_buried", open - ahead * 4f + side * 3f + Vector3.Up * 2.5f, h.LampAt );
		var top = 0f;
		var launched = 0f;
		for ( var t = 0f; t < 9f && LighthouseHermit.EruptHits == eruptHits; t += 0.05f )
		{
			if ( h.Doing == LighthouseHermit.Act.Erupt && shots is not null && top == 0f ) { top = 1f; await View( "lighthouse_erupt_cue", open - ahead * 6f + side * 4f + Vector3.Up * 3f, h.EruptSpot ); }
			await Wait( 0.05f );
		}
		for ( var t = 0f; t < 0.6f; t += 0.05f ) { launched = MathF.Max( launched, player.GlobalPosition.Y - Sculpt.GroundAt( player.GlobalPosition ) ); await Wait( 0.05f ); }
		if ( shots is not null ) await View( "lighthouse_erupt", open - ahead * 9f + side * 7f + Vector3.Up * 3f, h.GlobalPosition + Vector3.Up * 3f );
		var surfaced = h.GlobalPosition.Flat().DistanceTo( open.Flat() );
		GD.Print( $"[autotest] lighthouse erupt: hits {LighthouseHermit.EruptHits - eruptHits}, thrown {launched:0.0} m up, came up {surfaced:0.0} m from the player, health {before:0} to {survivor.Health.Current:0}" );
		Check( LighthouseHermit.EruptHits > eruptHits && launched > 1.5f && survivor.Health.Current < before, "he did not burst up under the player and throw them" );
		Check( surfaced < 4f, "he came up far from the player" );
		for ( var t = 0f; t < 3f && h.Doing != LighthouseHermit.Act.Walk; t += 0.1f ) await Wait( 0.1f );
		Check( !h.Underground && MathF.Abs( h.GlobalPosition.Y - Sculpt.GroundAt( h.GlobalPosition ) ) < 0.5f, "he did not come all the way back up onto the sand" );

		// ---- Foghorn: shot as it winds up it jams; he takes more while jammed ----
		survivor.Health.Heal( 9999f );
		PlaceHermit();
		Stand( open, h.GlobalPosition );
		await Wait( 0.3f );
		var jams = LighthouseHermit.Jams;
		h.Force( "horn" );
		Check( h.HornWinding, "the foghorn did not start winding up" );
		await Wait( 0.6f );
		if ( shots is not null ) await View( "lighthouse_horn", h.HornAt + Front() * 5f + Side() * 2.5f + Vector3.Down * 1f, h.HornAt );
		for ( var i = 0; i < 40 && h.Doing == LighthouseHermit.Act.Horn; i++ ) h.ReceiveShot( LighthouseHermit.HornIndex, 30f, Vector3.Forward, h.HornAt, open, survivor, true );
		Check( LighthouseHermit.Jams > jams && h.Doing == LighthouseHermit.Act.Jammed, $"shooting the horn did not jam it ({h.Doing})" );
		var jamBefore = h.Health.Current;
		h.ReceiveShot( LighthouseHermit.ThoraxIndex, 10f, Vector3.Forward, h.GlobalPosition, open, survivor, false );
		Check( jamBefore - h.Health.Current >= 14.9f, "he does not take more while his horn is jammed" );
		Check( FogBank.All.Count == 0, "a jammed horn still blew fog" );
		h.Health.Current = h.Health.MaxHealth;
		for ( var t = 0f; t < 5f && h.Doing != LighthouseHermit.Act.Walk; t += 0.1f ) await Wait( 0.1f );

		// ---- Foghorn: it blows; thick fog over the player, hermit crabs come in, then it lifts ----
		PlaceHermit();
		Stand( open, h.GlobalPosition );
		await Wait( 0.3f );
		h.FogTime = 9f;
		var env = GetTree().CurrentScene.FindChildren( "*", "WorldEnvironment", true, false ).OfType<WorldEnvironment>().FirstOrDefault()?.Environment;
		var clearEnd = env?.FogDepthEnd ?? 0f;
		var crabs = LighthouseHermit.FogCrabsCalled;
		var fogs = LighthouseHermit.Fogs;
		h.Force( "horn" );
		await Wait( h.HornWindUp + 2.3f );
		var thick = FogBank.Amount( h );
		var local = FogBank.Local;
		var foggedEnd = env?.FogDepthEnd ?? 0f;
		var newCrabs = HermitCrab.All.Count( c => !c.IsDead );
		GD.Print( $"[autotest] lighthouse fog: fogs {LighthouseHermit.Fogs - fogs}, thickness {thick:0.00}, on the player {local:0.00}, view {clearEnd:0} to {foggedEnd:0} m, crabs called {LighthouseHermit.FogCrabsCalled - crabs} ({newCrabs} alive)" );
		Check( LighthouseHermit.Fogs > fogs && thick > 0.6f && local > 0.6f, "the foghorn did not roll thick fog over the player" );
		Check( foggedEnd < clearEnd * 0.5f && foggedEnd < 15f, "in the fog the player can still see far" );
		Check( LighthouseHermit.FogCrabsCalled > crabs && newCrabs > 0, "no hermit crabs came in under the fog" );
		if ( shots is not null ) { Stand( open, h.GlobalPosition ); await Wait( 0.2f ); await Shot( "lighthouse_fog" ); }
		if ( shots is not null ) await View( "lighthouse_fog_outside", open - ahead * 26f + Vector3.Up * 6f, h.GlobalPosition + Vector3.Up * 3f );
		foreach ( var c in HermitCrab.All.ToList() ) c.Health.OnDamage( new DamageInfo { Damage = 1e6f } );
		for ( var t = 0f; t < 8f && FogBank.All.Count > 0; t += 0.2f ) await Wait( 0.2f );
		await Wait( 0.3f );
		var afterEnd = env?.FogDepthEnd ?? 0f;
		GD.Print( $"[autotest] lighthouse fog lifted: banks {FogBank.All.Count}, view back to {afterEnd:0} m" );
		Check( FogBank.All.Count == 0 && MathF.Abs( afterEnd - clearEnd ) < 0.5f, "the fog did not lift, or the view did not come back" );
		for ( var t = 0f; t < 4f && h.Doing != LighthouseHermit.Act.Walk; t += 0.1f ) await Wait( 0.1f );

		// ---- Lantern Beam: his belly bared, the beam burns a player in the open and leaves a burning trail ----
		survivor.Health.Heal( 9999f );
		PlaceHermit();
		Stand( open, h.GlobalPosition );
		await Wait( 0.3f );
		var beamHits = LighthouseHermit.BeamHits;
		var trailHits = LighthouseHermit.TrailHits;
		before = survivor.Health.Current;
		h.Force( "beam" );
		await Wait( h.BeamRear + 0.2f );
		Check( h.BellyOpen && h.WeakSpot( LighthouseHermit.BellyIndex ), "his belly is not bared for the beam" );
		if ( shots is not null ) await View( "lighthouse_rear", h.GlobalPosition + Front() * 10f + Side() * 8f + Vector3.Up * 1f, h.GlobalPosition + Vector3.Up * 3.5f );
		await Wait( h.BeamCharge + 0.9f );
		if ( shots is not null ) await View( "lighthouse_beam", h.GlobalPosition + Side() * 15f + Front() * 7f + Vector3.Up * 2f, (h.LampAt + h.BeamAt) * 0.5f );
		for ( var t = 0f; t < 5f && h.Doing == LighthouseHermit.Act.Beam; t += 0.1f ) await Wait( 0.1f );
		var marks = LampTrail.All.Sum( l => l.Marks );
		GD.Print( $"[autotest] lighthouse beam: hits {LighthouseHermit.BeamHits - beamHits}, trail burns {LighthouseHermit.TrailHits - trailHits}, marks {marks}, health {before:0} to {survivor.Health.Current:0}" );
		Check( LighthouseHermit.BeamHits > beamHits && survivor.Health.Current < before - 20f, "the lantern beam did not burn a player in the open" );
		Check( LighthouseHermit.TrailHits > trailHits, "the beam's trail did not burn" );
		Check( !h.BellyOpen, "his belly stayed out after the beam" );
		// Shot in the belly, the light goes out.
		survivor.Health.Heal( 9999f );
		PlaceHermit();
		Stand( open, h.GlobalPosition );
		await Wait( 0.3f );
		var outs = LighthouseHermit.LightsOut;
		h.Force( "beam" );
		await Wait( h.BeamRear + 0.3f );
		for ( var i = 0; i < 40 && h.Doing == LighthouseHermit.Act.Beam; i++ ) h.ReceiveShot( LighthouseHermit.BellyIndex, 30f, Vector3.Forward, h.GlobalPosition, open, survivor, true );
		GD.Print( $"[autotest] lighthouse lights out: {LighthouseHermit.LightsOut - outs}, doing {h.Doing}" );
		Check( LighthouseHermit.LightsOut > outs && h.Doing == LighthouseHermit.Act.Recover && !h.Burning, "shooting his belly did not put the light out" );
		h.Health.Current = h.Health.MaxHealth;
		for ( var t = 0f; t < 3f && h.Doing != LighthouseHermit.Act.Walk; t += 0.1f ) await Wait( 0.1f );

		// ---- Enraged at half health ----
		h.Health.Current = h.Health.MaxHealth * 0.49f;
		await Wait( 0.3f );
		Check( h.Enraged && LighthouseHermit.Enrages == 1, "he was not enraged at half health" );

		// ---- On his own for a while: on the sand, inside the map, using his moves ----
		survivor.Health.Heal( 9999f );
		survivor.Health.Invulnerable = true;
		Stand( open, h.GlobalPosition );
		h.OnlyWhenTold = false;
		var moves = LighthouseHermit.Slams + LighthouseHermit.Burrows + LighthouseHermit.Horns + LighthouseHermit.Beams;
		var worst = 0f;
		var outside = false;
		for ( var t = 0f; t < 20f; t += 0.25f )
		{
			await Wait( 0.25f );
			if ( !IsInstanceValid( h ) ) break;
			if ( h.Doing == LighthouseHermit.Act.Walk ) worst = MathF.Max( worst, MathF.Abs( h.GlobalPosition.Y - Sculpt.GroundAt( h.GlobalPosition ) ) );
			if ( Sculpt.InBounds( h.GlobalPosition ).Flat().DistanceTo( h.GlobalPosition.Flat() ) > 0.01f ) outside = true;
		}
		var used = LighthouseHermit.Slams + LighthouseHermit.Burrows + LighthouseHermit.Horns + LighthouseHermit.Beams - moves;
		GD.Print( $"[autotest] lighthouse free: {used} moves, worst {worst:0.00} m off the sand while walking, left the map {outside}" );
		Check( used > 1, "left to himself he hardly attacked" );
		Check( !outside && worst < 0.6f, "left to himself he left the map or walked off the sand" );
		survivor.Health.Invulnerable = false;
		foreach ( var c in HermitCrab.All.ToList() ) c.Health.OnDamage( new DamageInfo { Damage = 1e6f } );

		// ---- Dead: the tower falls apart, the fog goes ----
		var killed = false;
		h.Killed += _ => killed = true;
		h.Health.OnDamage( new DamageInfo { Damage = 1e7f } );
		await Wait( LighthouseHermit.ToppleSeconds + 2f );
		Check( killed && !IsInstanceValid( h ), "the hermit did not die and burst" );
		Check( FogBank.All.Count == 0 && FogBank.Local == 0f && MathF.Abs( (env?.FogDepthEnd ?? 0f) - clearEnd ) < 0.5f, "his fog outlived him" );
		await Wait( 4f );
		Check( LampTrail.All.Count == 0, "his beam's trail outlived him" );
		Done();
	}
}
