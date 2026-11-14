using Godot;

/// <summary>
/// The sea floor of the water level as rolling terrain (issue #35): far larger than the old 76 m arena, with hills,
/// ridges and valleys, rising into a cliff wall at the edge. The middle (the stations, the dive crater and the Kraken's
/// fight ring) stays a nearly flat sandy basin, so all of that plays as before. Built before the navmesh: its collider
/// is a height map on the world layer, so the navmesh, traces and spawns all follow the ground. Sizes in metres.
/// </summary>
public static class SeaTerrain
{
	/// <summary> Half the width of the sea floor; the old arena's walls were at 38 m. </summary>
	public const float HalfSize = 112f;
	/// <summary> Out to here from the middle the floor is a nearly flat basin (stations, dive crater, Kraken ring). </summary>
	public const float FlatRadius = 30f;
	/// <summary> Over this much further the basin rises into the hills. </summary>
	public const float Blend = 26f;
	/// <summary> Grid spacing of the mesh and the collider. </summary>
	public const float Step = 1f;

	/// <summary> True once the water level has built its terrain (the dressing and spawns then follow its heights). </summary>
	public static bool Active { get; private set; }

	static FastNoiseLite hills, ridges, detail, warp;

	static void Init()
	{
		if ( hills is not null ) return;
		hills = new FastNoiseLite { Seed = 35, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / 70f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 3, FractalGain = 0.45f };
		ridges = new FastNoiseLite { Seed = 136, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / 60f, FractalType = FastNoiseLite.FractalTypeEnum.None };
		detail = new FastNoiseLite { Seed = 9, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / 14f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 2 };
		warp = new FastNoiseLite { Seed = 77, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / 90f };
	}

	static float Smooth( float a, float b, float x )
	{
		var t = MathX.Clamp( (x - a) / (b - a), 0f, 1f );
		return t * t * (3f - 2f * t);
	}

	/// <summary> Which side of the teleporter (issue #35): 1 is the first area, 2 the wreck site from wave 11. </summary>
	public static int Area { get; set; } = 1;
	/// <summary> The wreck site's flat sand plain (area 2): its middle and half sizes (metres). </summary>
	public static readonly Vector2 PlainCentre = new( 0f, -70f );
	public static readonly Vector2 PlainHalf = new( 64f, 18f );
	const float PlainFeather = 12f;

	/// <summary> The height of the sea floor (metres) at a point. 0 in the middle, as the old arena floor was. </summary>
	public static float HeightAt( float x, float z ) => Area == 2 ? WreckSiteHeight( x, z ) : BaseHeight( x, z );

	/// <summary> Area 2: the same sea floor with a solid flat plain for the wreck, the sand heaped against its high side
	/// and scooped out under its hull. </summary>
	static float WreckSiteHeight( float x, float z )
	{
		var h = BaseHeight( x, z );
		var dx = MathF.Max( 0f, MathF.Abs( x - PlainCentre.X ) - PlainHalf.X );
		var dz = MathF.Max( 0f, MathF.Abs( z - PlainCentre.Y ) - PlainHalf.Y );
		var plain = 1f - Smooth( 0f, PlainFeather, MathF.Sqrt( dx * dx + dz * dz ) );
		h = MathX.Lerp( h, detail.GetNoise2D( x, z ) * 0.08f, plain );
		if ( ShipWreck.Placed ) h = ShipWreck.ShapeGround( x, z, h );
		return h;
	}

	static float BaseHeight( float x, float z )
	{
		Init();
		// A little domain warp so the hills meander instead of sitting on a grid.
		var wx = x + warp.GetNoise2D( x, z ) * 10f;
		var wz = z + warp.GetNoise2D( x + 400f, z - 300f ) * 10f;
		var r = MathF.Sqrt( x * x + z * z );
		var outer = Smooth( FlatRadius, FlatRadius + Blend, r );

		// Rolling hills and valleys, with raised shelves (soft-edged plateaus) here and there, more pronounced further
		// out. Kept walkable: the slopes stay under the navmesh's 40 degrees nearly everywhere.
		var rolling = hills.GetNoise2D( wx, wz ) * 8.5f;
		var shelf = Smooth( 0.05f, 0.55f, ridges.GetNoise2D( wx, wz ) ) * 3.5f;
		var far = 0.75f + 0.4f * Smooth( 50f, HalfSize - 20f, r );
		var land = (rolling + shelf) * far + detail.GetNoise2D( x, z ) * 0.35f;
		// The basin: barely any rise, just the sand's soft undulation.
		var basin = detail.GetNoise2D( x, z ) * 0.12f;
		var height = MathX.Lerp( basin, land, outer );

		// The rim: the floor climbs steeply into a wall of rock at the edge of the map.
		var edge = MathF.Max( MathF.Abs( x ), MathF.Abs( z ) ) + warp.GetNoise2D( x * 2f, z * 2f ) * 4f;
		height += Smooth( HalfSize - 24f, HalfSize - 2f, edge ) * 26f;
		return height;
	}

	/// <summary> The height at a world point (metres), for anything placed on the sea floor. </summary>
	public static float HeightAt( Vector3 at ) => HeightAt( at.X, at.Z );

	public static void Build( Node3D level )
	{
		Init();
		Active = true;
		// Built again on the far side of the teleporter: the old ground goes first (now, so the navmesh skips it).
		if ( level.GetNodeOrNull( "Sea Terrain" ) is Node old ) { level.RemoveChild( old ); old.Free(); }
		var root = new Node3D { Name = "Sea Terrain" };
		level.AddChild( root );

		var cells = (int)MathF.Round( 2f * HalfSize / Step );
		var verts = cells + 1;
		var heights = new float[verts * verts];
		for ( var j = 0; j < verts; j++ )
			for ( var i = 0; i < verts; i++ )
				heights[j * verts + i] = HeightAt( -HalfSize + i * Step, -HalfSize + j * Step );

		// The mesh, in chunks so the parts out of view are culled.
		var material = Material();
		const int chunk = 28;
		for ( var cj = 0; cj < cells; cj += chunk )
			for ( var ci = 0; ci < cells; ci += chunk )
			{
				var st = new SurfaceTool();
				st.Begin( Mesh.PrimitiveType.Triangles );
				var ni = Math.Min( chunk, cells - ci );
				var nj = Math.Min( chunk, cells - cj );
				for ( var j = 0; j <= nj; j++ )
					for ( var i = 0; i <= ni; i++ )
					{
						var gi = ci + i; var gj = cj + j;
						var h = heights[gj * verts + gi];
						var p = new Vector3( -HalfSize + gi * Step, h, -HalfSize + gj * Step );
						var n = Normal( heights, verts, gi, gj );
						// Valleys a little darker and cooler, crests and slopes warmer and lighter, so the shape reads.
						var shade = MathX.Clamp( 0.86f + h * 0.018f, 0.62f, 1.08f ) * (0.82f + 0.18f * n.Y);
						st.SetColor( new Color( shade, shade * 0.99f, shade * 0.96f ) );
						st.SetNormal( n );
						st.SetUV( new Vector2( p.X, p.Z ) * 0.12f );
						st.AddVertex( p );
					}
				for ( var j = 0; j < nj; j++ )
					for ( var i = 0; i < ni; i++ )
					{
						int a = j * (ni + 1) + i, b = a + 1, c = a + ni + 1, d = c + 1;
						st.AddIndex( a ); st.AddIndex( b ); st.AddIndex( c );
						st.AddIndex( b ); st.AddIndex( d ); st.AddIndex( c );
					}
				st.GenerateTangents();
				root.AddChild( new MeshInstance3D { Name = $"Floor {ci / chunk}-{cj / chunk}", Mesh = st.Commit(), MaterialOverride = material } );
			}

		// The collider: one height map over the whole floor.
		var body = new StaticBody3D { Name = "Sea Floor", CollisionLayer = Layers.World, CollisionMask = 0 };
		root.AddChild( body );
		body.AddChild( new CollisionShape3D { Shape = new HeightMapShape3D { MapWidth = verts, MapDepth = verts, MapData = heights }, Scale = new Vector3( Step, 1f, Step ) } );

		// Invisible walls just inside the rim, so nothing walks or swims off the edge.
		foreach ( var (centre, size) in new[]
		{
			(new Vector3( 0f, 30f, -HalfSize + 3f ), new Vector3( 2f * HalfSize, 120f, 1f )), (new Vector3( 0f, 30f, HalfSize - 3f ), new Vector3( 2f * HalfSize, 120f, 1f )),
			(new Vector3( -HalfSize + 3f, 30f, 0f ), new Vector3( 1f, 120f, 2f * HalfSize )), (new Vector3( HalfSize - 3f, 30f, 0f ), new Vector3( 1f, 120f, 2f * HalfSize )),
		} )
		{
			var wall = new StaticBody3D { Name = "Edge", CollisionLayer = Layers.World, CollisionMask = 0, Position = centre };
			wall.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = size } } );
			root.AddChild( wall );
		}
	}

	static Vector3 Normal( float[] h, int verts, int i, int j )
	{
		var l = h[j * verts + Math.Max( 0, i - 1 )];
		var r = h[j * verts + Math.Min( verts - 1, i + 1 )];
		var d = h[Math.Max( 0, j - 1 ) * verts + i];
		var u = h[Math.Min( verts - 1, j + 1 ) * verts + i];
		return new Vector3( l - r, 2f * Step, d - u ).Normalized();
	}

	/// <summary> The sea bed sand (the old floor's texture), mapped across the ground and shaded by the vertex colours. </summary>
	static StandardMaterial3D Material()
	{
		var m = new StandardMaterial3D { Roughness = 1f, VertexColorUseAsAlbedo = true };
		const string dir = "res://assets/materials/ground/seabed_";
		if ( ResourceLoader.Exists( dir + "color.png" ) ) m.AlbedoTexture = GD.Load<Texture2D>( dir + "color.png" );
		if ( ResourceLoader.Exists( dir + "normal.png" ) ) { m.NormalEnabled = true; m.NormalTexture = GD.Load<Texture2D>( dir + "normal.png" ); }
		if ( ResourceLoader.Exists( dir + "rough.png" ) ) m.RoughnessTexture = GD.Load<Texture2D>( dir + "rough.png" );
		return m;
	}

	/// <summary>
	/// Fits the converted level to the terrain: removes the old flat floor, sand and walls, sets every prop down on
	/// the ground, moves the cliffs out to the new rim (adding more so it is closed), spreads more of the sea life
	/// over the new ground, and moves the enemy spawn points out into the hills.
	/// </summary>
	public static void Fit( Node3D level )
	{
		var all = level.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>().ToList();
		// Only the converted arena's own floor, sand and walls (the dock and others have parts with the same names).
		bool OldGround( Node3D n ) => (n.GetParent()?.Name.ToString() == "Arena" && (n.Name.ToString() == "Floor" || Level.Kind( n ).StartsWith( "Wall " )))
			|| (Level.Kind( n.GetParent() ) == "Dressing" && Level.Kind( n ) == "Sand");
		foreach ( var n in all.Where( OldGround ).ToList() )
			if ( IsInstanceValid( n ) ) { n.GetParent().RemoveChild( n ); n.Free(); }

		var random = new Random( 3535 );
		float Range( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		bool IsInstanceValid( GodotObject o ) => GodotObject.IsInstanceValid( o );

		// Props and spawn points straight under the level's dressing: set down on the ground where they stand.
		var dressing = all.Where( n => IsInstanceValid( n ) && n.GetParent() is { } par && Level.Kind( par ) is "Dressing" or "Zombie Spawns" ).ToList();
		var cliffs = dressing.Where( n => Level.Kind( n ).StartsWith( "Cliff" ) ).ToList();
		foreach ( var n in dressing.Except( cliffs ) )
			n.GlobalPosition = n.GlobalPosition with { Y = n.GlobalPosition.Y + HeightAt( n.GlobalPosition ) };

		// The rim: cliffs all the way round, every 10 m or so, on the rising ground, facing in.
		if ( cliffs.Count > 0 )
		{
			var parent = cliffs[0].GetParent();
			var spots = new List<Vector3>();
			var inset = HalfSize - 9f;
			for ( var s = -inset; s <= inset; s += 10f )
			{
				spots.Add( new Vector3( s, 0f, -inset ) ); spots.Add( new Vector3( s, 0f, inset ) );
				spots.Add( new Vector3( -inset, 0f, s ) ); spots.Add( new Vector3( inset, 0f, s ) );
			}
			for ( var k = 0; k < spots.Count; k++ )
			{
				var cliff = k < cliffs.Count ? cliffs[k] : (Node3D)cliffs[k % cliffs.Count].Duplicate();
				if ( k >= cliffs.Count ) parent.AddChild( cliff );
				var at = spots[k] + new Vector3( Range( -2f, 2f ), 0f, Range( -2f, 2f ) );
				var size = Range( 1.3f, 2.1f );
				cliff.GlobalTransform = new Transform3D( new Basis( Vector3.Up, Range( 0f, MathF.Tau ) ) * Basis.FromScale( new Vector3( size, size * Range( 1.1f, 1.7f ), size ) ), at with { Y = HeightAt( at ) - 1.5f } );
			}
			// A few outcrops out in the hills too.
			for ( var k = 0; k < 18; k++ )
			{
				var at = OuterSpot( Range, 14f );
				var cliff = (Node3D)cliffs[k % cliffs.Count].Duplicate();
				parent.AddChild( cliff );
				var size = Range( 0.7f, 1.3f );
				cliff.GlobalTransform = new Transform3D( new Basis( Vector3.Up, Range( 0f, MathF.Tau ) ) * Basis.FromScale( Vector3.One * size ), at with { Y = HeightAt( at ) - 0.8f } );
			}
		}

		// Thin out the basin (playtest: the middle was crowded and the hills bare): most of the plants and stones the
		// old arena had in the middle move out over the land, keeping their height off the ground.
		bool Life( Node3D n ) => Level.Kind( n ) is var k && (k.StartsWith( "Kelp" ) || k.StartsWith( "Coral" ) || k.StartsWith( "Pebbles" ) || k.StartsWith( "Boulder" ));
		var moved = 0;
		foreach ( var n in dressing.Where( n => Life( n ) && n.GlobalPosition.Flat().Length() < FlatRadius + 6f ).ToList() )
		{
			if ( random.NextDouble() > 0.6 ) continue;
			var lift = n.GlobalPosition.Y - HeightAt( n.GlobalPosition );
			var at = OuterSpot( Range, 10f );
			n.GlobalPosition = at with { Y = HeightAt( at ) + lift };
			moved++;
		}
		GD.Print( $"[terrain] moved {moved} plants and stones out of the basin" );

		// More life over the new ground: copies of the kelp, coral, pebbles and boulders, out past the basin.
		foreach ( var (prefix, count) in new[] { ("Kelp", 190), ("Coral", 110), ("Pebbles", 80), ("Boulder", 50) } )
		{
			var sources = dressing.Where( n => Level.Kind( n ).StartsWith( prefix ) ).ToList();
			if ( sources.Count == 0 ) continue;
			for ( var k = 0; k < count; k++ )
			{
				var source = sources[k % sources.Count];
				var copy = (Node3D)source.Duplicate();
				source.GetParent().AddChild( copy );
				var at = OuterSpot( Range, 10f );
				copy.GlobalTransform = new Transform3D( new Basis( Vector3.Up, Range( 0f, MathF.Tau ) ) * source.GlobalBasis, at with { Y = HeightAt( at ) + (source.GlobalPosition.Y - HeightAt( source.GlobalPosition )) } );
			}
		}

		// Enemy spawn points: out in the hills round the basin, so they come over the rises.
		var spawns = dressing.Where( n => n.IsInGroup( "spawn_point" ) ).ToList();
		for ( var k = 0; k < spawns.Count; k++ )
		{
			var angle = k / (float)spawns.Count * MathF.Tau + Range( -0.2f, 0.2f );
			var radius = Range( 44f, 58f );
			var at = new Vector3( MathF.Cos( angle ) * radius, 0f, MathF.Sin( angle ) * radius );
			spawns[k].GlobalPosition = at with { Y = HeightAt( at ) };
		}
	}

	/// <summary> A random spot out past the basin and inside the rim. </summary>
	static Vector3 OuterSpot( Func<float, float, float> range, float rimClear )
	{
		for ( var i = 0; i < 40; i++ )
		{
			var at = new Vector3( range( -HalfSize + rimClear, HalfSize - rimClear ), 0f, range( -HalfSize + rimClear, HalfSize - rimClear ) );
			if ( at.Length() > FlatRadius + 4f ) return at;
		}
		return new Vector3( FlatRadius + 10f, 0f, 0f );
	}

	/// <summary> Sets the sea dressing (built after) down on the ground. </summary>
	public static void Settle( Node3D dressingRoot )
	{
		if ( !Active || dressingRoot is null ) return;
		foreach ( var n in dressingRoot.GetChildren().OfType<Node3D>() )
			n.GlobalPosition = n.GlobalPosition with { Y = n.GlobalPosition.Y + HeightAt( n.GlobalPosition ) };
	}
}
