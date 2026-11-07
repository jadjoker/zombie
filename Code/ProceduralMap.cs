/// <summary>
/// Builds a big randomized forest world at runtime instead of the hand-made clearing. Put it on the Game
/// object of a scene that already has the forest lighting, the player template and the director.
///
/// The host picks a seed (synced); every machine builds the same world from it: a huge grass floor with an
/// invisible boundary, woods that thicken and thin in patches, boulder fields, meadows and a clear start area.
/// Trees and boulders get colliders so the navmesh routes around them. The hand-made arena (floor, walls,
/// crates, dressing) and its fixed zombie spawns are switched off, so enemies spawn on the navmesh instead.
/// </summary>
public sealed class ProceduralMap : Component
{
	[Property, Group( "World" )] public float HalfSize { get; set; } = 6000f;
	/// <summary> Radius of bare ground kept around the start. </summary>
	[Property, Group( "World" )] public float StartClearing { get; set; } = 450f;
	/// <summary> Multiplies how many props are scattered. Lower it if the framerate suffers. </summary>
	[Property, Group( "World" )] public float Density { get; set; } = 1f;
	[Property, Group( "World" )] public string ArenaName { get; set; } = "Arena";
	[Property, Group( "World" )] public string FixedSpawnsName { get; set; } = "Zombie Spawns";

	/// <summary> Picked by the host; 0 until then. </summary>
	[Sync] public int Seed { get; set; }

	bool built;
	GameObject root;
	System.Random rnd;
	int noiseSeed;

	readonly Dictionary<(int, int), List<(Vector2 At, float Radius)>> blockers = new();
	const float BlockCell = 200f;

	static readonly Color[] TrunkTints = { new( 0.32f, 0.22f, 0.13f ), new( 0.28f, 0.2f, 0.15f ), new( 0.4f, 0.28f, 0.17f ) };
	static readonly Color[] LeafTints = { new( 0.2f, 0.5f, 0.18f ), new( 0.28f, 0.6f, 0.22f ), new( 0.16f, 0.42f, 0.16f ), new( 0.4f, 0.62f, 0.24f ), new( 0.24f, 0.48f, 0.3f ) };
	static readonly Color[] RockTints = { new( 0.46f, 0.47f, 0.45f ), new( 0.54f, 0.52f, 0.48f ), new( 0.4f, 0.42f, 0.4f ) };
	static readonly Color[] GrassTints = { new( 0.3f, 0.6f, 0.22f ), new( 0.44f, 0.7f, 0.32f ), new( 0.26f, 0.5f, 0.2f ) };
	static readonly Color[] PineTints = { new( 0.14f, 0.38f, 0.2f ), new( 0.18f, 0.44f, 0.24f ), new( 0.12f, 0.34f, 0.19f ) };

	Model trunkA, trunkB, trunkC, canopyA, canopyB, canopyC, rockA, rockB, bush, grass, log, stump, fern, box;

	protected override void OnStart()
	{
		if ( !IsProxy && Seed == 0 ) Seed = Game.Random.Int( 1, 1_000_000_000 );
	}

	protected override void OnUpdate()
	{
		if ( built || Seed == 0 ) return;
		built = true;
		Build();
	}

	float R( float a, float b ) => a + (float)rnd.NextDouble() * (b - a);

	// ---- Smooth value noise, seeded --------------------------------------------

	float Hash( int x, int y )
	{
		unchecked
		{
			var h = (uint)(x * 374761393 + y * 668265263 + noiseSeed * 1442695041);
			h = (h ^ (h >> 13)) * 1274126177u;
			return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0x1000000;
		}
	}

	float Noise( float x, float y )
	{
		var xi = (int)MathF.Floor( x ); var yi = (int)MathF.Floor( y );
		var fx = x - xi; var fy = y - yi;
		fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
		var a = MathX.Lerp( Hash( xi, yi ), Hash( xi + 1, yi ), fx );
		var b = MathX.Lerp( Hash( xi, yi + 1 ), Hash( xi + 1, yi + 1 ), fx );
		return MathX.Lerp( a, b, fy );
	}

	float Woodland( Vector3 p ) => Noise( p.x / 2600f + 11.3f, p.y / 2600f - 4.7f ) * 0.7f + Noise( p.x / 900f, p.y / 900f ) * 0.3f;
	float Rocky( Vector3 p ) => Noise( p.x / 1700f - 31.1f, p.y / 1700f + 9.9f );

	// ---- Placement bookkeeping ---------------------------------------------------

	bool Free( Vector3 p, float pad )
	{
		if ( MathF.Abs( p.x ) > HalfSize - 150f || MathF.Abs( p.y ) > HalfSize - 150f ) return false;
		if ( p.WithZ( 0 ).Length < StartClearing + pad ) return false;
		var cx = (int)MathF.Floor( p.x / BlockCell ); var cy = (int)MathF.Floor( p.y / BlockCell );
		for ( var x = cx - 1; x <= cx + 1; x++ )
			for ( var y = cy - 1; y <= cy + 1; y++ )
			{
				if ( !blockers.TryGetValue( (x, y), out var list ) ) continue;
				foreach ( var (at, radius) in list )
					if ( new Vector2( p.x, p.y ).Distance( at ) < radius + pad ) return false;
			}
		return true;
	}

	void Block( Vector3 p, float radius )
	{
		var key = ((int)MathF.Floor( p.x / BlockCell ), (int)MathF.Floor( p.y / BlockCell ));
		if ( !blockers.TryGetValue( key, out var list ) ) blockers[key] = list = new();
		list.Add( (new Vector2( p.x, p.y ), radius) );
	}

	Vector3 RandomPoint() => new( R( -HalfSize, HalfSize ), R( -HalfSize, HalfSize ), 0f );

	GameObject Put( Model model, Vector3 pos, float yaw, Vector3 scale, Color tint, string name )
	{
		var go = new GameObject( root, true, name );
		go.WorldPosition = pos;
		go.WorldRotation = Rotation.FromYaw( yaw );
		go.WorldScale = scale;
		var r = go.AddComponent<ModelRenderer>();
		r.Model = model;
		r.Tint = tint;
		return go;
	}

	// ---- Build ---------------------------------------------------------------------

	void Build()
	{
		rnd = new System.Random( Seed );
		noiseSeed = Seed;

		// Switch off the hand-made arena and its fixed spawns.
		foreach ( var name in new[] { ArenaName, FixedSpawnsName } )
		{
			var go = Scene.Children.FirstOrDefault( g => g.Name == name );
			if ( go.IsValid() ) go.Enabled = false;
		}

		root = new GameObject( true, "Procedural World" );
		LoadModels();
		BuildGround();
		BuildBoundary();

		var d = MathF.Max( 0.1f, Density );
		for ( var i = 0; i < (int)(2600 * d); i++ ) TryTree();
		for ( var i = 0; i < (int)(1400 * d); i++ ) TryBush();
		for ( var i = 0; i < (int)(900 * d); i++ ) TryRock();
		for ( var i = 0; i < (int)(70 * d); i++ ) TryLogOrStump();
		for ( var i = 0; i < (int)(500 * d); i++ ) TryGrassPatch();
		for ( var i = 0; i < (int)(500 * d); i++ ) TryFern();
		BuildEdgeWoods();

		// Trees and boulders are new solid things: rebuild the navmesh around them.
		_ = Scene.NavMesh.Generate( Scene.PhysicsWorld );
		Log.Info( $"ProceduralMap: seed {Seed}, {root.Children.Count} props" );
	}

	void LoadModels()
	{
		box = Model.Load( "models/dev/box.vmdl" );
		trunkA = Model.Load( "models/forest/tree_trunk_a.vmdl" );
		trunkB = Model.Load( "models/forest/tree_trunk_b.vmdl" );
		trunkC = Model.Load( "models/forest/tree_trunk_c.vmdl" );
		canopyA = Model.Load( "models/forest/tree_canopy_a.vmdl" );
		canopyB = Model.Load( "models/forest/tree_canopy_b.vmdl" );
		canopyC = Model.Load( "models/forest/tree_canopy_c.vmdl" );
		rockA = Model.Load( "models/forest/rock_a.vmdl" );
		rockB = Model.Load( "models/forest/rock_b.vmdl" );
		bush = Model.Load( "models/forest/bush_a.vmdl" );
		grass = Model.Load( "models/forest/grass_a.vmdl" );
		log = Model.Load( "models/forest/log_a.vmdl" );
		stump = Model.Load( "models/forest/stump_a.vmdl" );
		fern = Model.Load( "models/forest/fern_a.vmdl" );
	}

	Vector3 FitBox( Vector3 size )
	{
		var b = box.Bounds.Size;
		return new Vector3( size.x / MathF.Max( b.x, 0.01f ), size.y / MathF.Max( b.y, 0.01f ), size.z / MathF.Max( b.z, 0.01f ) );
	}

	void BuildGround()
	{
		var size = HalfSize * 2f + 800f;
		var floor = new GameObject( root, true, "Ground" );
		floor.WorldPosition = new Vector3( 0, 0, -10f );
		var col = floor.AddComponent<BoxCollider>();
		col.Scale = new Vector3( size, size, 20f );

		var visual = new GameObject( floor, true, "Ground Visual" );
		visual.WorldScale = FitBox( new Vector3( size, size, 20f ) );
		var r = visual.AddComponent<ModelRenderer>();
		r.Model = box;
		r.MaterialOverride = Material.Load( "materials/arena/grass.vmat" );
		r.Tint = new Color( 0.5f, 0.68f, 0.36f );
	}

	/// <summary> Invisible walls round the edge so nobody walks off the world. </summary>
	void BuildBoundary()
	{
		var reach = HalfSize + 60f;
		var thick = 300f;
		var tall = 4000f;
		void Wall( Vector3 at, Vector3 size )
		{
			var go = new GameObject( root, true, "Boundary" );
			go.WorldPosition = at;
			go.AddComponent<BoxCollider>().Scale = size;
		}
		Wall( new Vector3( reach + thick / 2f, 0, tall / 2f ), new Vector3( thick, reach * 2f + thick * 2f, tall ) );
		Wall( new Vector3( -reach - thick / 2f, 0, tall / 2f ), new Vector3( thick, reach * 2f + thick * 2f, tall ) );
		Wall( new Vector3( 0, reach + thick / 2f, tall / 2f ), new Vector3( reach * 2f + thick * 2f, thick, tall ) );
		Wall( new Vector3( 0, -reach - thick / 2f, tall / 2f ), new Vector3( reach * 2f + thick * 2f, thick, tall ) );
	}

	// ---- Props -----------------------------------------------------------------------

	void PlantTree( Vector3 p, float scale )
	{
		var kind = rnd.Next( 3 );
		GameObject trunkGo;
		if ( kind == 2 )
		{
			trunkGo = Put( trunkC, p, R( 0, 360 ), new Vector3( scale, scale, scale * R( 0.9f, 1.15f ) ), TrunkTints[rnd.Next( TrunkTints.Length )], "Pine Trunk" );
			Put( canopyC, p + Vector3.Up * 560f * scale, R( 0, 360 ), Vector3.One * scale * R( 0.9f, 1.15f ), PineTints[rnd.Next( PineTints.Length )], "Pine Canopy" );
		}
		else
		{
			var useB = kind == 1;
			var trunkTop = useB ? 500f : 385f;
			trunkGo = Put( useB ? trunkB : trunkA, p, R( 0, 360 ), new Vector3( scale, scale, scale * R( 0.92f, 1.12f ) ), TrunkTints[rnd.Next( TrunkTints.Length )], "Tree Trunk" );
			Put( useB ? canopyB : canopyA, p + Vector3.Up * trunkTop * scale, R( 0, 360 ), Vector3.One * scale * R( 0.85f, 1.1f ), LeafTints[rnd.Next( LeafTints.Length )], "Canopy" );
		}
		var capsule = trunkGo.AddComponent<CapsuleCollider>();
		capsule.Start = Vector3.Zero;
		capsule.End = Vector3.Up * 260f;
		capsule.Radius = 20f;
		Block( p, 66f * scale );
	}

	void TryTree()
	{
		var p = RandomPoint();
		var w = Woodland( p );
		var chance = MathX.Clamp( (w - 0.38f) / 0.3f, 0f, 1f ) * 0.95f + 0.04f;
		if ( rnd.NextDouble() > chance ) return;
		var scale = R( 0.85f, 1.4f );
		if ( !Free( p, 70f * scale ) ) return;
		PlantTree( p, scale );
	}

	void TryBush()
	{
		var p = RandomPoint();
		var chance = 0.2f + Woodland( p ) * 0.6f;
		if ( rnd.NextDouble() > chance || !Free( p, 60f ) ) return;
		Put( bush, p, R( 0, 360 ), Vector3.One * R( 0.7f, 1.35f ), LeafTints[rnd.Next( LeafTints.Length )], "Bush" );
		Block( p, 40f );
	}

	void TryRock()
	{
		var p = RandomPoint();
		var rocky = Rocky( p );
		var chance = rocky > 0.6f ? 0.7f : 0.04f;
		if ( rnd.NextDouble() > chance ) return;
		var sc = R( 0.6f, 1.8f );
		if ( !Free( p, 60f * sc ) ) return;
		var go = Put( rnd.NextDouble() < 0.5 ? rockA : rockB, p + Vector3.Up * (50f * sc), R( 0, 360 ), new Vector3( sc, sc, sc * R( 0.8f, 1.1f ) ), RockTints[rnd.Next( RockTints.Length )], "Boulder" );
		go.AddComponent<SphereCollider>().Radius = 45f;
		Block( p, 55f * sc );
	}

	void TryLogOrStump()
	{
		var p = RandomPoint();
		if ( !Free( p, 110f ) ) return;
		if ( rnd.NextDouble() < 0.5 )
		{
			var sc = R( 0.8f, 1.4f );
			var go = Put( log, p, R( 0, 360 ), Vector3.One * sc, TrunkTints[rnd.Next( TrunkTints.Length )], "Log" );
			go.AddComponent<BoxCollider>().Scale = new Vector3( 120f, 36f, 36f );
			Block( p, 95f * sc );
		}
		else
		{
			Put( stump, p, R( 0, 360 ), Vector3.One * R( 0.7f, 1.3f ), TrunkTints[rnd.Next( TrunkTints.Length )], "Stump" );
			Block( p, 30f );
		}
	}

	void TryGrassPatch()
	{
		var p = RandomPoint();
		if ( !Free( p, 10f ) ) return;
		for ( var g = rnd.Next( 3, 6 ); g > 0; g-- )
		{
			var at = p + new Vector3( R( -80f, 80f ), R( -80f, 80f ), 0 );
			Put( grass, at, R( 0, 360 ), Vector3.One * R( 0.8f, 1.5f ), GrassTints[rnd.Next( GrassTints.Length )], "Grass" );
		}
	}

	void TryFern()
	{
		var p = RandomPoint();
		if ( !Free( p, 30f ) ) return;
		Put( fern, p, R( 0, 360 ), Vector3.One * R( 0.7f, 1.35f ), LeafTints[rnd.Next( LeafTints.Length )], "Fern" );
	}

	/// <summary> A dense belt of trees along the border so the edge reads as the end of the woods. </summary>
	void BuildEdgeWoods()
	{
		var step = 190f;
		for ( var t = -HalfSize + 100f; t <= HalfSize - 100f; t += step )
		{
			foreach ( var p in new[]
			{
				new Vector3( t, HalfSize - R( 120f, 360f ), 0 ), new Vector3( t, -HalfSize + R( 120f, 360f ), 0 ),
				new Vector3( HalfSize - R( 120f, 360f ), t, 0 ), new Vector3( -HalfSize + R( 120f, 360f ), t, 0 ),
			} )
			{
				PlantTree( p, R( 1.0f, 1.5f ) );
			}
		}
	}
}
