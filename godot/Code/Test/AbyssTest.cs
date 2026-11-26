using Godot;

/// <summary>
/// The Abyss (area 3, --abyss): through the wreck site's teleporter after wave 20 into the caves; wave 21 starts; the
/// stations stand by the arrival; the spawn points are in the cave mouths and every mouth and every corridor cell is
/// reachable on foot; the walls stop shots and the roof holds; it is dark (the mood is on, each player has a hip
/// lantern, the ally spider a headlamp); a chest opens and pays; a mimic chest gives itself away. With --shots (a
/// window), a tour: the arrival, the statue from all sides, the tunnels, corridors by lantern light, the scenery and a
/// map view.
/// </summary>
public partial class AutoTest
{
	async Task AbyssTest()
	{
		DevFlags.FreezeEnemies = true;
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		var director = WaveDirector.Current;
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.Invulnerable = true;
		var use = player.GetNode<PlayerUse>( "PlayerUse" );
		var level = GetTree().CurrentScene as Node3D;
		AreaTwo.Go( level, player );
		await Wait( 1f );
		director.JumpToWave( 20 );
		await Wait( 0.5f );
		director.SkipWave();
		await Wait( 4f );
		var tp = Teleporter.Current;
		Check( tp is not null && director.WaitingForTeleport, "no teleporter after wave 20, or wave 21 did not wait for it" );
		if ( tp is null ) { GD.Print( "[autotest] done" ); GetTree().Quit(); return; }
		player.GlobalPosition = Nav.Closest( player, tp.GlobalPosition + new Vector3( 0.6f, 0f, 0.3f ) ) + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		await Wait( 0.4f );
		Check( use.AtTeleporter, "standing on the wreck site's teleporter does not offer it" );
		use.PressUse();
		await Wait( 0.75f );
		await Shot( "abyss_loading" );
		await Wait( 2.25f );
		var allyNow = SeaSpider.All.FirstOrDefault( s => s.IsAlly );
		Check( allyNow is null || allyNow.GlobalPosition.DistanceTo( player.GlobalPosition ) < 6f, "the ally spider did not come through the teleporter with the player" );
		Check( SeaTerrain.Area == SeaTerrain.Caves && Abyss.Root is not null && IsInstanceValid( Abyss.Root ) && !ShipWreck.Placed, "the teleporter did not take the player into the Abyss" );
		var arrival = Abyss.Arrival;
		GD.Print( $"[autotest] abyss: player at {player.GlobalPosition}, {player.GlobalPosition.Flat().DistanceTo( arrival.Flat() ):0.0} m from the arrival" );
		Check( player.GlobalPosition.Flat().DistanceTo( arrival.Flat() ) < 5f, "the player did not arrive on the Abyss's arrival pad" );
		await Shot( "abyss_arrival" );
		await Wait( 5.5f );
		Check( director.Wave == 21 && director.State == WaveState.Fighting, $"wave 21 did not begin in the Abyss (wave {director.Wave}, {director.State})" );
		director.SkipWave();
		director.SpawningPaused = true;

		// The stations, the spawn points in the cave mouths.
		foreach ( var station in GetTree().GetNodesInGroup( "buy_station" ).OfType<Node3D>() )
		{
			var d = station.GlobalPosition.Flat().DistanceTo( arrival.Flat() );
			Check( d < 10f, $"a buy station is not by the Abyss's arrival ({d:0.0} m)" );
			Check( !Abyss.InWall( station.GlobalPosition, 0f ), "a buy station is in a cave wall" );
		}
		var spawns = GetTree().GetNodesInGroup( "spawn_point" ).OfType<Node3D>().ToList();
		foreach ( var s in spawns )
		{
			var r = s.GlobalPosition.Flat().Length();
			Check( r > Abyss.Rings[3] && r < Abyss.PocketBack, $"a spawn point is not in a cave mouth ({s.GlobalPosition}, {r:0.0} m out)" );
		}

		// Everything reachable on foot from the arrival: each cave mouth and each corridor cell.
		var from = Nav.Closest( player, arrival );
		int reached = 0, cells = 0;
		var far = 0f;
		foreach ( var (name, want) in Abyss.SpawnSpots.Select( (s, i) => ($"mouth {i}", s) ).Concat( Abyss.CellCentres.Select( (c, i) => ($"cell {i}", c) ) ) )
		{
			// Somewhere open near it (a cell's middle may have a stalagmite or a chest).
			var got = Nav.Closest( player, want );
			var path = Nav.Path( player, from, got );
			var ok = got.Flat().DistanceTo( want.Flat() ) < 6f && path.Length > 1 && path[^1].DistanceTo( got ) < 1f;
			var length = 0f;
			for ( var i = 1; i < path.Length; i++ ) length += path[i].DistanceTo( path[i - 1] );
			far = MathF.Max( far, length );
			cells++;
			if ( ok ) reached++;
			else
			{
				GD.Print( $"[autotest] abyss: {name} at {want} not reached (navmesh {got}, path {path.Length} points, ends at {path[^1]})" );
				if ( name.StartsWith( "cell" ) && int.TryParse( name[5..], out var ci ) )
					foreach ( var w in Abyss.MazeReport.Where( w => w.A == ci || w.B == ci ) ) GD.Print( $"[autotest] abyss:   wall {w.A}|{w.B} {w.Where} {(w.Open ? "open" : "shut")}" );
			}
			Check( ok, $"the Abyss's {name} is not reachable on foot from the arrival" );
		}
		GD.Print( $"[autotest] abyss: {reached}/{cells} places reachable on foot, the longest walk {far:0} m; {Abyss.WallChunks} rock chunks, {Abyss.OpenWalls} openings" );

		// Walls stop shots: rays across thirty wall pieces at chest height all hit rock.
		var random = new Random( 4 );
		var stopped = 0;
		var tried = 0;
		foreach ( var (a, b) in Abyss.WallSegments.OrderBy( _ => random.Next() ).Take( 30 ) )
		{
			if ( a.DistanceTo( b ) < 0.5f ) continue;
			var mid = (a + b) * 0.5f;
			var across = new Vector2( -(b - a).Y, (b - a).X ).Normalized();
			var y = SeaTerrain.HeightAt( mid.X, mid.Y ) + 1.4f;
			var p0 = new Vector3( mid.X - across.X * 4f, y, mid.Y - across.Y * 4f );
			var p1 = new Vector3( mid.X + across.X * 4f, y, mid.Y + across.Y * 4f );
			tried++;
			if ( Trace.Ray( player, p0, p1, Layers.World ).Hit ) stopped++;
		}
		GD.Print( $"[autotest] abyss: {stopped}/{tried} rays across the walls stopped" );
		Check( stopped == tried, "a shot went through a cave wall" );
		// The roof holds (nobody floats out), with room over the walls for swimmers.
		var up = Trace.Ray( player, new Vector3( 40f, 14f, 3f ), new Vector3( 40f, 80f, 3f ), Layers.World );
		GD.Print( $"[autotest] abyss: the roof over (40, 3) at {(up.Hit ? up.HitPosition.Y.ToString( "0.0" ) : "none")}" );
		Check( up.Hit && up.HitPosition.Y < Abyss.RoofHeight + 4f, "there is no solid roof over the caves" );
		var kept = Abyss.KeepIn( new Vector3( 120f, 60f, 0f ) );
		Check( kept.Flat().Length() <= Abyss.SwimRadius + 0.01f && kept.Y <= Abyss.SwimCeiling, "swimmers are not kept inside the cavern" );
		Check( Sculpt.InBounds( new Vector3( 120f, 60f, 0f ) ).Y <= Abyss.SwimCeiling, "Sculpt.InBounds does not keep swimmers under the Abyss's roof" );

		// Dark, with lights to see by.
		var env = GetTree().Root.FindChildren( "*", "WorldEnvironment", true, false ).OfType<WorldEnvironment>().First().Environment;
		GD.Print( $"[autotest] abyss: ambient {env.AmbientLightEnergy:0.00}, murk to {env.FogDepthEnd:0} m, lanterns {HipLantern.All.Count}" );
		Check( AbyssMood.Current is not null && env.AmbientLightEnergy <= AbyssMood.AmbientEnergy + 0.01f, "the Abyss is not dark" );
		Check( player.GetNodeOrNull<HipLantern>( HipLantern.NodeName ) is { Light.Visible: true }, "the player has no hip lantern in the Abyss" );
		var ally = SeaSpider.All.FirstOrDefault( s => s.IsAlly );
		GD.Print( $"[autotest] abyss: ally spider {(ally is null ? "none" : $"at {ally.GlobalPosition}, headlamp {ally.Headlamp is not null}")}" );
		Check( ally is null || ally.Headlamp is not null, "the ally spider has no headlamp" );
		Check( AbyssStatue.Root is not null && AbyssStatue.Light is not null, "no statue or no light on it" );
		GD.Print( $"[autotest] abyss: statue light at {AbyssStatue.Light.GlobalPosition}, pointing {-AbyssStatue.Light.GlobalBasis.Z}, visible {AbyssStatue.Light.IsVisibleInTree()}" );
		var statueTop = Trace.Ray( player, new Vector3( 0f, 30f, 0f ), new Vector3( 0f, -5f, 0f ), Layers.World );
		Check( statueTop.Hit && statueTop.HitPosition.Y > AbyssStatue.BaseHeight + 1f, "the statue is not solid" );

		// The scenery.
		GD.Print( $"[autotest] abyss: {AbyssProps.Chests} chests, {AbyssProps.Mimics} mimic chests, {AbyssProps.Skeletons} skeletons, {AbyssProps.BonePiles} bone piles, {AbyssProps.Anchors} anchors, {AbyssProps.Boats} boats, {AbyssProps.MossPatches} moss" );
		Check( TreasureChest.All.Count >= 4 && AbyssProps.Skeletons >= 4 && AbyssProps.MossPatches > 200, "too little scenery in the caves" );
		var bulbs = GetTree().CurrentScene.GetNodeOrNull<AlienFloaters>( "Alien Floaters" );
		GD.Print( $"[autotest] abyss: {bulbs?.Count ?? 0} glow bulbs, in walls: {string.Join( ", ", bulbs?.Bulbs.Where( b => Abyss.InWall( b.GlobalPosition, -1.4f ) ).Select( b => b.GlobalPosition.ToString() ) ?? Array.Empty<string>() )}" );
		Check( bulbs is not null && bulbs.Bulbs.All( b => !Abyss.InWall( b.GlobalPosition, -1.4f ) ) && bulbs.Count > 20, "no glow bulbs in the caves, or one is in a wall" );

		// Views (before a chest is opened, so one is still shut for its picture).
		if ( !string.IsNullOrEmpty( shots ) ) await AbyssTour( level );

		// A chest opens when walked up to, and pays.
		var chest = TreasureChest.All.Where( c => !c.IsOpen ).MinBy( c => c.GlobalPosition.DistanceTo( arrival ) );
		var points = survivor.Points;
		var front = chest.GlobalPosition + chest.GlobalBasis.Z * 1.6f;
		player.GlobalPosition = Nav.Closest( player, front ) + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		LookAt( chest.GlobalPosition + Vector3.Up * 0.4f );
		for ( var t = 0f; t < 2f && !chest.IsOpen; t += 0.1f ) await Wait( 0.1f );
		await Wait( 0.5f );
		await Shot( "abyss_chest_open" );
		GD.Print( $"[autotest] abyss: chest open {chest.IsOpen}, points {points} -> {survivor.Points}" );
		Check( chest.IsOpen && survivor.Points >= points + TreasureChest.Points, "a treasure chest did not open and pay when walked up to" );

		// A mimic chest gives itself away when a player comes close.
		var open = Nav.Closest( player, player.GlobalPosition + player.GlobalBasis.Z * -5f );
		var reveals = MimicOctopus.Reveals;
		DevFlags.FreezeEnemies = false;
		var mimic = MimicOctopus.Disguised( level, open, MimicOctopus.Disguise.Chest, 0f );
		await Wait( 0.3f );
		player.GlobalPosition = Nav.Closest( player, open + (player.GlobalPosition - open).Flat().Normalized() * 2.5f ) + Vector3.Up * 0.1f;
		LookAt( open + Vector3.Up * 0.3f );
		for ( var t = 0f; t < 2f && MimicOctopus.Reveals == reveals; t += 0.1f ) await Wait( 0.1f );
		await Wait( 0.4f );
		await Shot( "abyss_mimic_chest" );
		Check( MimicOctopus.Reveals > reveals, "a mimic chest did not give itself away" );
		mimic.Health.OnDamage( new DamageInfo { Damage = 100000f } );
		DevFlags.FreezeEnemies = true;

		// Nothing to get inside: the walls, stalagmites and props are solid where they are drawn.
		await AbyssSolid();

		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> The photo tour of the Abyss (needs a window). </summary>
	async Task AbyssTour( Node3D level )
	{
		var view = Transform3D.Identity;
		async Task From( string name, Vector3 eye, Vector3 at, bool third = false )
		{
			view = new Transform3D( Shapes.LookAt( at - eye ), eye );
			player.CameraOverride = _ => view;
			var was = player.ThirdPerson;
			// Third person draws no gun in view; the robot stands just behind the camera (its lantern lights the scene).
			player.ThirdPerson = true;
			if ( !third )
			{
				var back = (eye - at).Flat().Normalized();
				player.GlobalPosition = Nav.Closest( player, eye + back * 1.5f ) + Vector3.Up * 0.1f;
			}
			await Wait( 0.25f );
			await Shot( name );
			player.ThirdPerson = was;
			player.CameraOverride = null;
		}
		async Task Stand( string name, Vector3 at, Vector3 look, bool third = false, float pitch = float.NaN )
		{
			player.GlobalPosition = Nav.Closest( player, at ) + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			await Wait( 0.3f );
			LookAt( look );
			if ( !float.IsNaN( pitch ) ) player.EyeAngles = player.EyeAngles with { Pitch = pitch };
			player.ThirdPerson = third;
			await Wait( 0.35f );
			await Shot( name );
			player.ThirdPerson = false;
		}
		var hud = GetTree().Root.FindChildren( "*", "", true, false ).OfType<CanvasLayer>().ToList();
		foreach ( var l in hud ) l.Visible = false;
		var statue = new Vector3( 0f, 5f, 0f );
		var faceDir = Abyss.Arrival.Flat().Normalized();

		// The statue: from the arrival, in front and below, from the side, the face close, from above down the shaft.
		await Stand( "abyss_tour_01_arrival", Abyss.Arrival, statue );
		await From( "abyss_tour_02_statue_front", faceDir * 13f + Vector3.Up * 2f, new Vector3( 0f, 5.5f, 0f ) );
		await From( "abyss_tour_03_statue_below", faceDir * 6f + Vector3.Up * 1.2f, new Vector3( 0f, 7.5f, 0f ) );
		var side = faceDir.Cross( Vector3.Up );
		await From( "abyss_tour_04_statue_side", side * 11f + faceDir * 4f + Vector3.Up * 4f, new Vector3( 0f, 5.5f, 0f ) );
		await From( "abyss_tour_05_statue_face", faceDir * 3.2f + Vector3.Up * 8.6f, new Vector3( 0f, 7.6f, 0f ) );
		await From( "abyss_tour_06_statue_above", faceDir * 4f + Vector3.Up * 21f, new Vector3( 0f, 3f, 0f ) );
		await From( "abyss_tour_07_hub_wide", -faceDir * 22f + side * 6f + Vector3.Up * 9f, new Vector3( 0f, 3f, 0f ) );

		// The main tunnels: from the hub out along one, and from deep in one back toward the light.
		var t0 = Abyss.MainTunnels[0];
		var tdir = new Vector3( MathF.Cos( MathX.Rad( t0 ) ), 0f, MathF.Sin( MathX.Rad( t0 ) ) );
		await Stand( "abyss_tour_08_tunnel_out", tdir * 24f, tdir * 60f, pitch: -4f );
		await Stand( "abyss_tour_09_tunnel_mid", tdir * 48f, tdir * 80f, pitch: -4f );
		await Stand( "abyss_tour_10_tunnel_home", tdir * 62f, Vector3.Zero, pitch: 0f );
		await From( "abyss_tour_11_arch", tdir * 40f + Vector3.Up * 3f + tdir.Cross( Vector3.Up ) * 2f, tdir * 66f + Vector3.Up * 6f );

		// A side corridor by lantern light, first and third person, and the cave mouth.
		var cell = Abyss.CellCentres.Where( c => AbyssProps.FromTunnel( c ) > 9f ).OrderBy( c => c.Flat().Length() ).Skip( 3 ).First();
		await Stand( "abyss_tour_12_corridor_lantern", cell, cell * 1.4f, pitch: -10f );
		await Stand( "abyss_tour_13_lantern_third", cell, cell * 1.4f, third: true, pitch: 10f );
		var hip = player.GlobalPosition + Vector3.Up * 0.9f;
		var right = new Vector3( MathF.Cos( MathX.Rad( player.EyeAngles.Yaw ) ), 0f, -MathF.Sin( MathX.Rad( player.EyeAngles.Yaw ) ) );
		await From( "abyss_tour_14_lantern_close", hip + right * 1.4f + Vector3.Up * 0.3f + player.GlobalBasis.Z * -0.8f, hip + right * 0.3f, third: true );
		var mouth = Abyss.SpawnSpots[1];
		await Stand( "abyss_tour_15_cave_mouth", mouth.Flat().Normalized() * (Abyss.Rings[3] - 10f), mouth, pitch: 2f );

		// The ally spider's headlamp down a corridor.
		if ( SeaSpider.All.FirstOrDefault( s => s.IsAlly ) is { } ally )
		{
			var spot = Nav.Closest( player, cell );
			ally.GlobalPosition = spot;
			player.GlobalPosition = spot + (Vector3.Zero - spot).Flat().Normalized() * 6f + Vector3.Up * 0.1f;
			await Wait( 1.5f );
			var a = ally.GlobalPosition;
			GD.Print( $"[autotest] abyss: headlamp at {ally.Headlamp.GlobalPosition}, pointing {-ally.Headlamp.GlobalBasis.Z}, energy {ally.Headlamp.LightEnergy}, visible {ally.Headlamp.IsVisibleInTree()}" );
			var ahead = -ally.Headlamp.GlobalBasis.Z.Flat().Normalized();
			await From( "abyss_tour_16_spider_headlamp", a - ahead * 4f + ahead.Cross( Vector3.Up ) * 2.5f + Vector3.Up * 2.5f, a + ahead * 5f );
		}

		// Scenery up close: a chest, a skeleton, bones, an anchor, the rowboat.
		async Task Prop( string name, Node3D n, float back, float height )
		{
			if ( n is null ) return;
			var p = n.GlobalPosition;
			var away = (n.GlobalBasis.Z.Flat().LengthSquared() > 0.1f ? n.GlobalBasis.Z.Flat().Normalized() : Vector3.Forward);
			await From( name, p + away * back + Vector3.Up * height, p + Vector3.Up * 0.35f );
		}
		var props = Abyss.Root.GetChildren().OfType<Node3D>().ToList();
		await Prop( "abyss_tour_17_chest", TreasureChest.All.FirstOrDefault( c => !c.IsOpen ), 2.6f, 1.4f );
		await Prop( "abyss_tour_18_skeleton", props.FirstOrDefault( n => n.Name.ToString().StartsWith( "Diver Skeleton" ) ), 2.4f, 1.2f );
		await Prop( "abyss_tour_19_bones", props.FirstOrDefault( n => n.Name.ToString().StartsWith( "Bone Pile" ) ), 2.2f, 1.4f );
		await Prop( "abyss_tour_20_anchor", props.FirstOrDefault( n => n.Name.ToString().StartsWith( "Anchor" ) ), 3.5f, 1.8f );
		await Prop( "abyss_tour_21_rowboat", props.FirstOrDefault( n => n.Name.ToString().StartsWith( "Rowboat" ) ), 5f, 2.5f );

		// Enemies in the dark: a few in a corridor ahead, their eyes the first thing seen.
		var kinds = new[] { WaveDirector.EnemyKind.Crab, WaveDirector.EnemyKind.Angler, WaveDirector.EnemyKind.Bloober, WaveDirector.EnemyKind.Crab };
		var stand = Nav.Closest( player, tdir * 34f );
		player.GlobalPosition = stand + Vector3.Up * 0.1f;
		var spawned = new List<Node3D>();
		for ( var i = 0; i < kinds.Length; i++ )
			if ( WaveDirector.Current.SpawnAt( kinds[i], Nav.Closest( player, tdir * (44f + i * 4f) + tdir.Cross( Vector3.Up ) * ((i - 1.5f) * 3f) ), stand ) is { } e ) spawned.Add( e );
		await Wait( 1.2f );
		LookAt( tdir * 50f + Vector3.Up * 1.5f );
		await Wait( 0.3f );
		await Shot( "abyss_tour_22_enemies_dark" );
		foreach ( var e in spawned.Where( IsInstanceValid ) ) e.QueueFree();

		// A map view from high over the caves, lit up for the picture only.
		var env = GetTree().Root.FindChildren( "*", "WorldEnvironment", true, false ).OfType<WorldEnvironment>().First().Environment;
		var (ambient, fogEnd) = (env.AmbientLightEnergy, env.FogDepthEnd);
		var roof = Abyss.Root.FindChildren( "Roof", "MeshInstance3D", false, false ).OfType<Node3D>().Concat( Abyss.Root.FindChildren( "Stalactites", "", false, false ).OfType<Node3D>() ).ToList();
		foreach ( var r in roof ) r.Visible = false;
		var sun = GetTree().Root.FindChildren( "*", "DirectionalLight3D", true, false ).OfType<DirectionalLight3D>().FirstOrDefault();
		var sunWas = sun?.LightEnergy ?? 0f;
		if ( sun is not null ) sun.LightEnergy = 1.2f;
		env.AmbientLightEnergy = 2.5f;
		env.FogDepthEnd = 900f;
		await From( "abyss_tour_23_map", new Vector3( 0f, 190f, 1f ), Vector3.Zero );
		await From( "abyss_tour_24_map_angle", new Vector3( 0f, 110f, 120f ), new Vector3( 0f, 0f, 10f ) );
		foreach ( var r in roof ) r.Visible = true;
		if ( sun is not null ) sun.LightEnergy = sunWas;
		env.AmbientLightEnergy = ambient;
		env.FogDepthEnd = fogEnd;
		foreach ( var l in hud ) l.Visible = true;
	}
}
