using Godot;

/// <summary>
/// Gibs, creature by creature (the chunking pass): each kind is put on open sand and killed at point blank, then shot
/// from a camera off to the side as the pieces fly (time slowed), and again once they have landed. With --shots the
/// pictures are the before and after. Checks that every kill leaves pieces.
/// </summary>
public partial class AutoTest
{
	/// <summary> The free camera at a spot, looking at a point (no gun on screen). </summary>
	static void FreeView( Vector3 from, Vector3 at )
	{
		var dir = at - from;
		DevFlags.FreeCamPosition = from;
		DevFlags.FreeCamAngles = new Angles( -MathX.Deg( MathF.Atan2( dir.Y, dir.Flat().Length() ) ), MathX.Deg( MathF.Atan2( -dir.X, -dir.Z ) ), 0f );
		DevFlags.FreeCam = true;
	}

	/// <summary>
	/// The pieces themselves, up close: for each creature's recipe, a row of its outside pieces, an inside piece, each of
	/// its special pieces and a torn end, held still in open water and lit, one picture each.
	/// </summary>
	async Task GibGalleryTest()
	{
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 0.5f );
		var (spot, yaw) = OpenLane( 10f );
		var ahead = new Vector3( -MathF.Sin( MathX.Rad( yaw ) ), 0f, -MathF.Cos( MathX.Rad( yaw ) ) );
		var right = ahead.Cross( Vector3.Up );
		var stage = spot + ahead * 4f + Vector3.Up * 2.2f;
		var recipes = new (string Name, GibRecipe Recipe, float Size)[]
		{
			("crab", GibRecipes.Crab( new Color( 0.45f, 0.12f, 0.1f ), new Color( 0.85f, 0.6f, 0.5f ), new Color( 0.35f, 1f, 0.28f ) ), 0.25f),
			("bloober", GibRecipes.Jelly( new Color( 0.75f, 0.85f, 0.3f ), new Color( 0.5f, 0.2f, 0.8f ), new Color( 0.35f, 1f, 0.28f ), false ), 0.25f),
			("squilien", GibRecipes.Jelly( new Color( 0.45f, 0.3f, 0.75f ), new Color( 0.9f, 0.4f, 0.7f ), new Color( 0.35f, 1f, 0.28f ), true ), 0.25f),
			("rockyshark", GibRecipes.RockyShark( new Color( 0.35f, 1f, 0.28f ) ), 0.28f),
			("mc51", GibRecipes.Mosaic( new Color( 0.35f, 1f, 0.28f ), FxMaterials.Mosaic( 3.6f * U.In ) ), 0.28f),
			("kraken", GibRecipes.Kraken( new Color( 0.24f, 0.46f, 0.42f ), new Color( 0.35f, 1f, 0.28f ) ), 0.3f),
			("mermaid", GibRecipes.Mermaid( new Color( 0.3f, 0.95f, 0.85f ), new Color( 0.22f, 0.42f, 0.88f ) ), 0.25f),
			("seahorse", GibRecipes.Seahorse( new Color( 0.4f, 1f, 0.35f ), new Color( 1f, 0.82f, 0.12f ), new Color( 0.24f, 0.78f, 0.3f ) ), 0.22f),
			("poseidon", GibRecipes.Poseidon( new Color( 0.3f, 0.95f, 0.85f ), new Color( 0.86f, 0.68f, 0.5f ), new Color( 0.1f, 0.56f, 0.5f ), new Color( 1f, 0.76f, 0.26f ) ), 0.28f),
		};
		var random = new Random( 5 );
		foreach ( var (name, r, size) in recipes )
		{
			var shelf = new Node3D { Name = "Gallery" };
			GetTree().CurrentScene.AddChild( shelf );
			shelf.GlobalPosition = stage;
			var items = new List<Node3D>();
			for ( var i = 0; i < 2; i++ ) items.Add( GibKit.OuterPiece( r, random, size ) );
			items.Add( GibKit.InnerPiece( r, random, size * 0.8f ) );
			foreach ( var special in r.Specials ) { var n = new Node3D(); special( random, n, size ); items.Add( n ); }
			var cap = GibKit.TornEnd( r, random, size * 0.45f );
			items.Add( cap );
			for ( var i = 0; i < items.Count; i++ )
			{
				shelf.AddChild( items[i] );
				var col = i % 4;
				var row = i / 4;
				items[i].GlobalPosition = stage + right * (col - 1.5f) * size * 2.4f + Vector3.Down * row * size * 2.2f;
				// Turned to show the inside and the outside both.
				items[i].GlobalBasis = new Basis( Vector3.Up, 0.6f + i ) * new Basis( Vector3.Right, i == items.Count - 1 ? -1.1f : 0.5f );
			}
			shelf.AddChild( new OmniLight3D { LightEnergy = 1.6f, OmniRange = 6f, Position = -ahead * 1.2f + Vector3.Up * 1f } );
			var rows = (items.Count + 3) / 4;
			var middle = stage + Vector3.Down * (rows - 1) * size * 1.1f;
			FreeView( middle - ahead * size * 15f + Vector3.Up * size * 2f, middle );
			var fov = player.FieldOfView;
			player.FieldOfView = 34f;
			await Wait( 0.3f );
			await Shot( $"gallery_{name}" );
			player.FieldOfView = fov;
			shelf.QueueFree();
			await Wait( 0.1f );
		}
		DevFlags.FreeCam = false;
		// What a big burst costs: the Kraken's 26 pieces, then a crab's 6, built and thrown in one frame.
		var watch = System.Diagnostics.Stopwatch.StartNew();
		GibKit.Burst( recipes[5].Recipe, stage, Vector3.Up, 0.5f, 26 );
		var cold = watch.Elapsed.TotalMilliseconds;
		await Wait( 0.2f );
		watch.Restart();
		GibKit.Burst( recipes[5].Recipe, stage, Vector3.Up, 0.5f, 26 );
		var big = watch.Elapsed.TotalMilliseconds;
		GD.Print( $"[autotest] gallery: the first 26-piece burst (building its templates) took {cold:0.0} ms" );
		watch.Restart();
		GibKit.Burst( recipes[0].Recipe, stage, Vector3.Up, 0.17f, 6 );
		var small = watch.Elapsed.TotalMilliseconds;
		GD.Print( $"[autotest] gallery: a 26-piece burst took {big:0.0} ms, a 6-piece burst {small:0.0} ms" );
		Check( big < 25.0, $"a big burst is too slow to build ({big:0} ms)" );
		GD.Print( $"[autotest] gallery: {recipes.Length} recipes, torn ends made {GibKit.Caps}" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> The four kinds of mermaid side by side, held still and lit: a front view, a closer face view, and a side view. </summary>
	/// <summary> --oldgallery: three crabs, Bloobers and Squiliens in a row (plus a shark and a mermaid to compare), close and wide. </summary>
	async Task OldGalleryTest()
	{
		DevFlags.FreezeEnemies = true;
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 0.5f );
		var (spot, yaw) = OpenLane( 16f );
		var ahead = new Vector3( -MathF.Sin( MathX.Rad( yaw ) ), 0f, -MathF.Cos( MathX.Rad( yaw ) ) );
		var right = ahead.Cross( Vector3.Up );
		var director = WaveDirector.Current;
		var rows = new[] { WaveDirector.EnemyKind.Crab, WaveDirector.EnemyKind.Bloober, WaveDirector.EnemyKind.Squilien };
		var spawned = new List<(WaveDirector.EnemyKind Kind, Node3D Node)>();
		for ( var r = 0; r < rows.Length; r++ )
			for ( var i = 0; i < 3; i++ )
			{
				var at = Nav.Closest( player, spot + ahead * 7f + right * ((r - 1) * 4.5f + (i - 1) * 1.4f) );
				if ( director.SpawnAt( rows[r], at + Vector3.Up * 0.3f, spot ) is Node3D n ) spawned.Add( (rows[r], n) );
			}
		foreach ( var extra in new[] { WaveDirector.EnemyKind.RockyShark, WaveDirector.EnemyKind.Mermaid } )
		{
			var at = Nav.Closest( player, spot + ahead * 11f + right * (extra == WaveDirector.EnemyKind.Mermaid ? 3f : -3f) );
			if ( director.SpawnAt( extra, at + Vector3.Up * 0.5f, spot ) is Node3D n ) spawned.Add( (extra, n) );
		}
		await Wait( 1.2f );
		var middle = spot + ahead * 8f + Vector3.Up * 0.8f;
		FreeView( middle - ahead * 9f + Vector3.Up * 2.2f, middle );
		await Wait( 0.4f );
		await Shot( "old_lineup" );
		foreach ( var kind in rows )
		{
			var group = spawned.Where( s => s.Kind == kind && IsInstanceValid( s.Node ) ).Select( s => s.Node.GlobalPosition ).ToList();
			if ( group.Count == 0 ) continue;
			var c = group.Aggregate( Vector3.Zero, ( a, b ) => a + b ) / group.Count + Vector3.Up * 0.4f;
			FreeView( c - ahead * 3.6f + Vector3.Up * 1.2f, c );
			await Wait( 0.4f );
			await Shot( $"old_{kind.ToString().ToLowerInvariant()}" );
			if ( kind == WaveDirector.EnemyKind.Crab )
			{
				FreeView( c + Vector3.Up * 2.6f - ahead * 0.4f, c );
				await Wait( 0.3f );
				await Shot( "old_crab_top" );
			}
		}
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	async Task MermaidGalleryTest()
	{
		DevFlags.FreezeEnemies = true;
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 0.5f );
		var (spot, yaw) = OpenLane( 14f );
		var ahead = new Vector3( -MathF.Sin( MathX.Rad( yaw ) ), 0f, -MathF.Cos( MathX.Rad( yaw ) ) );
		var right = ahead.Cross( Vector3.Up );
		var line = spot + ahead * 7f;
		var kinds = new (bool Male, bool Archer, int Hair)[] { (false, false, 0), (false, true, 1), (true, false, 0), (false, false, 2) };
		var all = new List<Mermaid>();
		for ( var i = 0; i < kinds.Length; i++ )
		{
			var at = Nav.Closest( player, line + right * (i - 1.5f) * 1.6f ) + Vector3.Up * 1.3f;
			var m = Mermaid.Spawn( GetTree().CurrentScene, at, 500f, kinds[i].Male, kinds[i].Archer );
			m.HairColour = kinds[i].Hair;
			all.Add( m );
		}
		await Wait( 0.6f );
		foreach ( var m in all ) if ( IsInstanceValid( m ) ) m.GlobalBasis = Shapes.LookAt( -ahead );
		var light = new OmniLight3D { LightEnergy = 2.5f, OmniRange = 12f };
		GetTree().CurrentScene.AddChild( light );
		light.GlobalPosition = line - ahead * 3f + Vector3.Up * 3f;
		await Wait( 0.4f );
		var middle = line + Vector3.Up * 1.4f;
		FreeView( middle - ahead * 6.5f + Vector3.Up * 0.4f, middle + Vector3.Down * 0.4f );
		await Wait( 0.3f );
		await Shot( "mermaids_lineup" );
		var face = middle + Vector3.Up * 0.75f;
		FreeView( face - ahead * 2.6f, face );
		var fov = player.FieldOfView;
		player.FieldOfView = 40f;
		await Wait( 0.3f );
		await Shot( "mermaids_faces" );
		player.FieldOfView = fov;
		FreeView( middle + right * 6.5f + Vector3.Up * 0.3f - ahead * 1.5f, middle );
		await Wait( 0.3f );
		await Shot( "mermaids_side" );
		// Each one close up: the whole body, then the head and shoulders, then three-quarter on.
		for ( var i = 0; i < all.Count; i++ )
		{
			if ( !IsInstanceValid( all[i] ) ) continue;
			var at = all[i].GlobalPosition;
			FreeView( at - ahead * 3.4f + Vector3.Up * 0.1f, at + Vector3.Down * 0.35f );
			await Wait( 0.2f );
			await Shot( $"mermaid_{i}_body" );
			var top = at + Vector3.Up * 0.55f;
			FreeView( top - ahead * 1.3f + right * 0.15f, top );
			await Wait( 0.2f );
			await Shot( $"mermaid_{i}_head" );
			FreeView( at + (right * 0.8f - ahead).Normalized() * 2.6f + Vector3.Up * 0.3f, at + Vector3.Down * 0.2f );
			await Wait( 0.2f );
			await Shot( $"mermaid_{i}_three" );
		}
		// In motion: let them go and watch the first one from the side as it swims in and attacks.
		if ( OS.GetCmdlineUserArgs().Contains( "--motion" ) )
		{
			DevFlags.FreezeEnemies = false;
			for ( var f = 0; f < 12; f++ )
			{
				if ( !IsInstanceValid( all[0] ) ) break;
				var at = all[0].GlobalPosition;
				var side = all[0].GlobalBasis.X.Normalized();
				FreeView( at + side * 3.2f + Vector3.Up * 0.2f, at + Vector3.Down * 0.3f );
				await Wait( 0.25f );
				await Shot( $"mermaid_motion_{f:00}" );
			}
		}
		DevFlags.FreeCam = false;
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> Poseidon up close: whole, face, three-quarter, side; `--motion` adds frames of each attack. </summary>
	async Task PoseidonGalleryTest()
	{
		DevFlags.FreezeEnemies = true;
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 0.5f );
		var (spot, yaw) = OpenLane( 20f );
		var ahead = new Vector3( -MathF.Sin( MathX.Rad( yaw ) ), 0f, -MathF.Cos( MathX.Rad( yaw ) ) );
		var right = ahead.Cross( Vector3.Up );
		var floor = Nav.Closest( player, spot + ahead * 14f );
		var god = Poseidon.Spawn( GetTree().CurrentScene, floor + Vector3.Up * 4.5f, 18000f );
		god.OnlyWhenTold = true;
		await Wait( 1.4f );
		god.GlobalBasis = Shapes.LookAt( -ahead );
		var light = new OmniLight3D { LightEnergy = 3f, OmniRange = 20f };
		GetTree().CurrentScene.AddChild( light );
		light.GlobalPosition = god.GlobalPosition - ahead * 6f + Vector3.Up * 4f;
		await Wait( 0.3f );
		var waist = god.GlobalPosition;
		var face = waist + Vector3.Up * 2.95f;
		FreeView( waist - ahead * 13f + Vector3.Up * 0.5f, waist + Vector3.Down * 1f );
		await Wait( 0.3f );
		await Shot( "poseidon_body" );
		FreeView( face - ahead * 2.6f, face + Vector3.Down * 0.1f );
		await Wait( 0.3f );
		await Shot( "poseidon_face" );
		FreeView( face + (right * 0.9f - ahead).Normalized() * 2.6f, face + Vector3.Down * 0.1f );
		await Wait( 0.3f );
		await Shot( "poseidon_face_three" );
		if ( OS.GetCmdlineUserArgs().Contains( "--debugface" ) )
		{
			var headNode = god.FindChild( "Head", true, false ) as Node3D;
			foreach ( var n in headNode.GetChildren() )
				if ( n is MeshInstance3D mi ) GD.Print( $"[face] {mi.Name} {mi.MaterialOverride?.GetType().Name} {mi.Mesh?.GetType().Name} aabb {mi.GetAabb()}" );
			if ( god.FindChild( "Hair", true, false ) is Node3D h ) { h.Visible = false; GD.Print( $"[face] hid {h.GetPath()} at {h.Position} scale {h.Scale}" ); }
			if ( god.FindChild( "Beard", true, false ) is Node3D b ) GD.Print( $"[face] beard {b.GetPath()} global {b.GlobalPosition} head {headNode.GlobalPosition} jaw rot {b.GetParent<Node3D>().Rotation}" );
			FreeView( face - ahead * 2.6f, face + Vector3.Down * 0.1f );
			await Wait( 0.3f );
			await Shot( "poseidon_face_bald" );
		}
		FreeView( waist + (right * 0.8f - ahead).Normalized() * 9f + Vector3.Up * 1.5f, waist + Vector3.Up * 0.5f );
		await Wait( 0.3f );
		await Shot( "poseidon_three" );
		FreeView( waist + right * 10f + Vector3.Up * 0.5f, waist + Vector3.Down * 0.5f );
		await Wait( 0.3f );
		await Shot( "poseidon_side" );
		if ( OS.GetCmdlineUserArgs().Contains( "--motion" ) )
		{
			// Each attack from three-quarter on: frames through the wind-up and the release.
			DevFlags.FreezeEnemies = false;
			var cam = waist + (right * 0.8f - ahead).Normalized() * 11f + Vector3.Up * 1.5f;
			foreach ( var what in new[] { "trident", "seahorses" } )
			{
				god.ForceAttack( what );
				for ( var f = 0; f < 8; f++ )
				{
					if ( !IsInstanceValid( god ) ) break;
					FreeView( cam, god.GlobalPosition + Vector3.Up * 0.8f );
					await Wait( what == "trident" ? 0.15f : 0.2f );
					await Shot( $"poseidon_{what}_{f}" );
				}
				await Wait( 1.5f );
			}
			for ( var f = 0; f < 8; f++ )
			{
				FreeView( god.GlobalPosition + god.GlobalBasis.X.Normalized() * 11f + Vector3.Up * 0.5f, god.GlobalPosition );
				await Wait( 0.2f );
				await Shot( $"poseidon_swim_{f}" );
			}
		}
		DevFlags.FreeCam = false;
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	/// <summary> The HUD's look: firing at a crab (damage numbers, enemy bar, hitmarker, ammo), MC-51's bar, the pause menu. </summary>
	async Task HudShotsTest()
	{
		var me = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 0.5f );
		DevFlags.FreezeEnemies = true;
		var (spot, yaw) = OpenLane( 12f );
		player.GlobalPosition = spot + Vector3.Up * 0.1f;
		player.EyeAngles = new Angles( 4f, yaw, 0f );
		var ahead = new Vector3( -MathF.Sin( MathX.Rad( yaw ) ), 0f, -MathF.Cos( MathX.Rad( yaw ) ) );
		var crab = WaveDirector.Current.SpawnAt( WaveDirector.EnemyKind.Crab, Nav.Closest( player, spot + ahead * 6f ) + Vector3.Up * 0.3f, spot );
		await Wait( 1f );
		if ( crab is Node3D c3 )
		{
			var look = (c3.GlobalPosition + Vector3.Up * 0.3f - player.Camera.GlobalPosition).Normalized();
			player.EyeAngles = new Angles( MathX.Deg( -MathF.Asin( look.Y ) ), MathX.Deg( MathF.Atan2( -look.X, -look.Z ) ), 0f );
		}
		me.Health.OnDamage( new DamageInfo { Damage = me.Health.MaxHealth * 0.62f } );
		me.Health.Invulnerable = true;
		Input.ActionPress( "attack1" );
		await Wait( 0.35f );
		await Shot( "hud_firing" );
		Input.ActionRelease( "attack1" );
		await Wait( 0.1f );
		MC61.Spawn( GetTree().CurrentScene, spot + ahead * 14f + Vector3.Up * 2.5f, 2000f );
		await Wait( 2.5f );
		await Shot( "hud_boss" );
		// --allthemes: the same moment in every HUD theme, switched live (the dev menu's HUD theme picker).
		if ( OS.GetCmdlineUserArgs().Contains( "--allthemes" ) )
		{
			for ( var i = 0; i < HudTheme.All.Length; i++ )
			{
				var t = HudTheme.All[i];
				UiStyle.Theme = t;
				await Wait( 0.4f );
				var hud = GetTree().Root.FindChildren( "*", "", true, false ).OfType<GameHud>().FirstOrDefault();
				if ( hud is null || hud.GetChildCount() == 0 ) GD.Print( $"[autotest] FAIL theme {t.Name}: the HUD did not rebuild" );
				await Shot( $"theme_{i:00}_{new string( t.Name.Where( char.IsLetter ).ToArray() )}" );
			}
			UiStyle.Theme = HudTheme.ArcadeDive;
			await Wait( 0.2f );
			GD.Print( "[autotest] done" );
			GetTree().Quit();
			return;
		}
		// The wave banner as the first wave starts.
		for ( var i = 0; i < 80 && WaveDirector.Current.State != WaveState.Fighting; i++ ) await Wait( 0.1f );
		await Wait( 0.35f );
		await Shot( "hud_banner" );
		// A buy station's prompt: walk up to the nearest one and look at it.
		if ( GetTree().Root.FindChildren( "*", "", true, false ).OfType<BuyStation>().Select( b => b.GetParent() as Node3D ).Where( n => n is not null ).OrderBy( n => n.GlobalPosition.DistanceTo( player.GlobalPosition ) ).FirstOrDefault() is { } station )
		{
			var to = (station.GlobalPosition - player.GlobalPosition).Flat().Normalized();
			player.GlobalPosition = Nav.Closest( player, station.GlobalPosition - to * 1.6f ) + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			await Wait( 0.3f );
			var look = (station.GlobalPosition + Vector3.Up * 0.8f - player.Camera.GlobalPosition).Normalized();
			player.EyeAngles = new Angles( MathX.Deg( -MathF.Asin( look.Y ) ), MathX.Deg( MathF.Atan2( -look.X, -look.Z ) ), 0f );
			await Wait( 0.5f );
			await Shot( "hud_prompt" );
		}
		var pause = GetTree().Root.FindChild( "PauseMenu", true, false ) as PauseMenu;
		pause?.SetOpen( true );
		await ToSignal( GetTree().CreateTimer( 0.4, true ), SceneTreeTimer.SignalName.Timeout );
		await Shot( "hud_pause" );
		pause?.SetOpen( false );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	async Task GibTest()
	{
		DevFlags.FreezeEnemies = true;
		player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Invulnerable = true;
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		await Wait( 0.5f );
		var director = WaveDirector.Current;
		var (spot, yaw) = OpenLane( 16f );
		var ahead = new Vector3( -MathF.Sin( MathX.Rad( yaw ) ), 0f, -MathF.Cos( MathX.Rad( yaw ) ) );
		var only = OS.GetCmdlineUserArgs().SkipWhile( a => a != "--only" ).Skip( 1 ).FirstOrDefault();
		var kinds = new[] { WaveDirector.EnemyKind.Crab, WaveDirector.EnemyKind.Bloober, WaveDirector.EnemyKind.Squilien, WaveDirector.EnemyKind.RockyShark, WaveDirector.EnemyKind.Spider, WaveDirector.EnemyKind.Mermaid, WaveDirector.EnemyKind.Seahorse, WaveDirector.EnemyKind.Boss };
		foreach ( var kind in kinds )
		{
			if ( only is not null && !kind.ToString().Equals( only, StringComparison.OrdinalIgnoreCase ) ) continue;
			player.GlobalPosition = spot + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			var at = Nav.Closest( player, spot + ahead * 6f );
			var enemy = director.SpawnAt( kind, at, spot ) as Creature;
			if ( enemy is null ) { GD.Print( $"[autotest] gibs: no {kind}" ); continue; }
			await Wait( kind == WaveDirector.EnemyKind.Boss ? 3f : 1.2f );
			var gibsBefore = GetTree().CurrentScene.FindChildren( "*", "RigidBody3D", true, false ).OfType<Gib>().Count();
			var centre = enemy.GlobalPosition + Vector3.Up * (kind == WaveDirector.EnemyKind.Boss ? 2.5f : 0.6f);
			var size = kind == WaveDirector.EnemyKind.Boss ? 9f : 4f;
			var side = (ahead.Cross( Vector3.Up ) * size + Vector3.Up * size * 0.45f - ahead * size * 0.2f);
			FreeView( centre + side, centre );
			// Point blank: shots from right in front of it until it dies (the overkill burst), parts first.
			var parts = CreaturePart.All.Where( p => IsInstanceValid( p ) && p.Creature == enemy ).ToList();
			foreach ( var p in parts.Take( 3 ) )
				if ( IsInstanceValid( p ) && !enemy.IsDead ) p.ReceiveShot( 60f, p, ahead, p.GlobalPosition, p.GlobalPosition - ahead * 0.5f, player, true );
			for ( var i = 0; i < 80 && IsInstanceValid( enemy ) && !enemy.IsDead; i++ )
			{
				var p = CreaturePart.All.FirstOrDefault( x => IsInstanceValid( x ) && x.Creature == enemy );
				if ( p is null ) break;
				p.ReceiveShot( 400f, p, ahead, p.GlobalPosition, p.GlobalPosition - ahead * 0.4f, player, true );
				await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
			}
			Engine.TimeScale = 0.2f;
			await Wait( 0.05f );
			await Shot( $"gibs_{kind.ToString().ToLower()}_flying" );
			Engine.TimeScale = 1f;
			await Wait( 2.2f );
			var gibs = GetTree().CurrentScene.FindChildren( "*", "RigidBody3D", true, false ).OfType<Gib>().Count();
			GD.Print( $"[autotest] gibs: {kind} left {gibs - gibsBefore} pieces" );
			Check( gibs > gibsBefore, $"killing a {kind} left no pieces" );
			FreeView( at + Vector3.Up * 3.2f - ahead * 2.2f + ahead.Cross( Vector3.Up ) * 1.5f, at + Vector3.Up * 0.8f );
			await Wait( 0.05f );
			await Shot( $"gibs_{kind.ToString().ToLower()}_landed" );
			DevFlags.FreeCam = false;
			foreach ( var g in GetTree().CurrentScene.FindChildren( "*", "RigidBody3D", true, false ).OfType<Gib>() ) g.QueueFree();
			await Wait( 0.3f );
		}
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
