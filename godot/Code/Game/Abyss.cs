using Godot;

/// <summary>
/// The Abyss, area 3 (waves 21 to 30, user's design): a dark cavern under a rock roof. The middle is a flat hub lit
/// by one shaft of light from a hole in the roof onto the statue (<see cref="AbyssStatue"/>); round it a cave maze in
/// three rings of wide rock corridors (big openings, so enemies and players get in and out), four straight main
/// tunnels lined with glowing moss from the hub to the cave mouths at the edge where enemies come in, rock arches
/// over the tunnels, stalactites, and scenery in the corridors (treasure chests, some of them mimics, diver skeletons,
/// bone piles, anchors, a rowboat, glow bulbs). Walls are rock chunks with matching convex colliders on the world and
/// plant layers (players, shots and the navmesh stop at them; swimmers are lifted over them by `Sculpt.OutOfRock`).
/// The walls stop well short of the roof, so swimmers have room over them. Metres; angle 0 is +X, turning toward +Z.
/// </summary>
public static class Abyss
{
	/// <summary> The flat hub's radius (the statue, the stations, the arrival pad). </summary>
	public const float HubRadius = 26f;
	/// <summary> The maze's ring boundaries: the hub's wall, two between the rings, the outer wall. </summary>
	public static readonly float[] Rings = { 26f, 42f, 58f, 74f };
	/// <summary> Cells round each ring. </summary>
	public static readonly int[] Sectors = { 8, 12, 16 };
	/// <summary> The cave mouths (where enemies come in) go back to here. </summary>
	public const float PocketBack = 84f;
	/// <summary> The roof's height over the middle; it comes down a little toward the edge. </summary>
	public const float RoofHeight = 27f;
	/// <summary> The hole in the roof over the statue. </summary>
	public const float SkylightRadius = 7f;
	public const float WallThickness = 3.2f;
	/// <summary> Swimmers stay inside this radius and under this height. </summary>
	public const float SwimRadius = 82f;
	public const float SwimCeiling = 20f;

	/// <summary> Half the width kept clear down each main tunnel. </summary>
	public const float TunnelHalfWidth = 6f;

	/// <summary> How far a floor plan point is sideways from the nearest main tunnel's middle line. </summary>
	public static float FromMainTunnel( Vector2 p )
	{
		var best = float.MaxValue;
		foreach ( var a in MainTunnels )
		{
			var dir = new Vector2( MathF.Cos( MathX.Rad( a ) ), MathF.Sin( MathX.Rad( a ) ) );
			var along = p.Dot( dir );
			if ( along < 0f ) continue;
			best = MathF.Min( best, (p - dir * along).Length() );
		}
		return best;
	}

	/// <summary> The four main tunnels' angles (degrees): straight from the hub out to a cave mouth. </summary>
	public static readonly float[] MainTunnels = { 11.25f, 101.25f, 191.25f, 281.25f };
	/// <summary> The cave mouths' angles: one at each main tunnel's end and one between each pair. </summary>
	public static readonly float[] Mouths = { 11.25f, 56.25f, 101.25f, 146.25f, 191.25f, 236.25f, 281.25f, 326.25f };
	/// <summary> Where the player arrives and the stations stand (between two main tunnels), and the teleporter out. </summary>
	public const float ArrivalAngle = 146.25f;
	public static Vector3 Arrival => Polar( 16f, ArrivalAngle );
	public static Vector3 TeleporterNear => Polar( 14f, 326.25f );

	public static Node3D Root { get; private set; }
	/// <summary> The walls as segments on the floor plan (for keeping things out of them). </summary>
	static readonly List<(Vector2 A, Vector2 B)> walls = new();
	public static IReadOnlyList<(Vector2 A, Vector2 B)> WallSegments => walls;
	/// <summary> The middle of each corridor cell (ring, sector) on the floor plan. </summary>
	public static readonly List<Vector3> CellCentres = new();
	public static int WallChunks { get; private set; }
	/// <summary> Every wall between cells: the cells either side (-1 the hub, -2 outside), where it is, open or not. </summary>
	public static List<(int A, int B, string Where, bool Open)> MazeReport = new();
	public static int OpenWalls { get; private set; }
	public static readonly List<Vector3> SpawnSpots = new();

	public static Vector3 Polar( float radius, float degrees )
	{
		var a = MathX.Rad( degrees );
		var p = new Vector3( MathF.Cos( a ) * radius, 0f, MathF.Sin( a ) * radius );
		return p with { Y = SeaTerrain.HeightAt( p ) };
	}

	static Vector2 Plan( float radius, float degrees )
	{
		var a = MathX.Rad( degrees );
		return new Vector2( MathF.Cos( a ) * radius, MathF.Sin( a ) * radius );
	}

	/// <summary> Inside a wall (or within `margin` of one) on the floor plan. </summary>
	public static bool InWall( Vector3 p, float margin )
	{
		var q = new Vector2( p.X, p.Z );
		var reach = WallThickness * 0.5f + 0.8f + margin;
		foreach ( var (a, b) in walls )
		{
			var ab = b - a;
			var t = Math.Clamp( (q - a).Dot( ab ) / MathF.Max( ab.LengthSquared(), 1e-4f ), 0f, 1f );
			if ( (a + ab * t).DistanceTo( q ) < reach ) return true;
		}
		return false;
	}

	/// <summary> The roof's height at a point (its underside, before stalactites). </summary>
	public static float RoofAt( float x, float z )
	{
		var r = MathF.Sqrt( x * x + z * z );
		return RoofHeight - 3f * Smooth( 30f, 110f, r ) + roofNoise.GetNoise2D( x, z ) * 2.2f;
	}

	static readonly FastNoiseLite roofNoise = new() { Seed = 303, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / 18f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 2 };
	static readonly FastNoiseLite lumps = new() { Seed = 404, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / 2.2f };

	static float Smooth( float a, float b, float x )
	{
		var t = MathX.Clamp( (x - a) / (b - a), 0f, 1f );
		return t * t * (3f - 2f * t);
	}

	/// <summary> Keeps a swimmer in the cavern: inside the edge, under the roof (and out of the rock). </summary>
	public static Vector3 KeepIn( Vector3 p )
	{
		var flat = p.Flat();
		if ( flat.Length() > SwimRadius ) flat = flat.Normalized() * SwimRadius;
		var y = MathF.Min( p.Y, MathF.Min( SwimCeiling, RoofAt( flat.X, flat.Z ) - 4f ) );
		return new Vector3( flat.X, y, flat.Z );
	}

	// ---- The maze ----

	sealed class Wall
	{
		public bool Radial;
		public float R0, R1, A0, A1;   // radial: R0..R1 at angle A0; arc: radius R0 from A0 to A1
		public int CellA, CellB;       // -1 is the hub, -2 outside
		public bool Open;
	}

	static int CellIndex( int ring, int sector )
	{
		var i = 0;
		for ( var k = 0; k < ring; k++ ) i += Sectors[k];
		return i + ((sector % Sectors[ring]) + Sectors[ring]) % Sectors[ring];
	}

	static int CellAt( int ring, float degrees ) => CellIndex( ring, (int)MathF.Floor( Wrap( degrees ) / (360f / Sectors[ring]) ) );
	static float Wrap( float d ) => ((d % 360f) + 360f) % 360f;

	static bool AngleIn( float a, float a0, float a1 )
	{
		a = Wrap( a - a0 );
		return a >= 0f && a <= a1 - a0;
	}

	/// <summary> Lays out the maze: every wall between cells, then a random spanning tree opened through them, some loops, and the main tunnels. </summary>
	static List<Wall> LayOut( Random random )
	{
		var list = new List<Wall>();
		// Radial walls between neighbouring cells of a ring.
		for ( var k = 0; k < 3; k++ )
			for ( var s = 0; s < Sectors[k]; s++ )
				list.Add( new Wall { Radial = true, R0 = Rings[k], R1 = Rings[k + 1], A0 = s * 360f / Sectors[k], CellA = CellIndex( k, s - 1 ), CellB = CellIndex( k, s ) } );
		// The hub's wall: one arc per ring A cell.
		for ( var s = 0; s < Sectors[0]; s++ )
			list.Add( new Wall { R0 = Rings[0], A0 = s * 45f, A1 = (s + 1) * 45f, CellA = -1, CellB = CellIndex( 0, s ) } );
		// Between the rings: the arc cut where either ring's cells end.
		for ( var k = 1; k < 3; k++ )
		{
			var cuts = new SortedSet<float>();
			for ( var s = 0; s < Sectors[k - 1]; s++ ) cuts.Add( MathF.Round( s * 360f / Sectors[k - 1], 3 ) );
			for ( var s = 0; s < Sectors[k]; s++ ) cuts.Add( MathF.Round( s * 360f / Sectors[k], 3 ) );
			var c = cuts.ToList();
			for ( var i = 0; i < c.Count; i++ )
			{
				var a0 = c[i];
				var a1 = i + 1 < c.Count ? c[i + 1] : 360f;
				var mid = (a0 + a1) * 0.5f;
				list.Add( new Wall { R0 = Rings[k], A0 = a0, A1 = a1, CellA = CellAt( k - 1, mid ), CellB = CellAt( k, mid ) } );
			}
		}
		// The outer wall: one arc per ring C cell.
		for ( var s = 0; s < Sectors[2]; s++ )
			list.Add( new Wall { R0 = Rings[3], A0 = s * 22.5f, A1 = (s + 1) * 22.5f, CellA = CellIndex( 2, s ), CellB = -2 } );

		// A random spanning tree through the ring cells (the hub only opens on the main tunnels).
		var cells = Sectors.Sum();
		var seen = new bool[cells];
		var stack = new Stack<int>();
		var start = random.Next( cells );
		seen[start] = true;
		stack.Push( start );
		while ( stack.Count > 0 )
		{
			var c = stack.Peek();
			var options = list.Where( w => w.CellA >= 0 && w.CellB >= 0 && ((w.CellA == c && !seen[w.CellB]) || (w.CellB == c && !seen[w.CellA])) ).ToList();
			if ( options.Count == 0 ) { stack.Pop(); continue; }
			var pick = options[random.Next( options.Count )];
			pick.Open = true;
			var next = pick.CellA == c ? pick.CellB : pick.CellA;
			seen[next] = true;
			stack.Push( next );
		}
		// Loops, so it isn't one long dead-end tree.
		foreach ( var w in list.Where( w => !w.Open && w.CellA >= 0 && w.CellB >= 0 ) )
			if ( random.NextDouble() < 0.3 ) w.Open = true;
		// The main tunnels: every arc they cross opens (the hub, the rings, the outer wall into the mouth).
		foreach ( var a in MainTunnels )
			foreach ( var w in list.Where( w => !w.Radial && AngleIn( a, w.A0, w.A1 ) ) )
				w.Open = true;
		// The cave mouths between the tunnels open from their ring C cell too.
		foreach ( var a in Mouths )
			foreach ( var w in list.Where( w => !w.Radial && w.CellB == -2 && AngleIn( a, w.A0, w.A1 ) ) )
				w.Open = true;
		return list;
	}

	// ---- Building ----

	public static void Build( Node3D level )
	{
		Clear();
		var watch = System.Diagnostics.Stopwatch.StartNew();
		Root = new Node3D { Name = "Abyss" };
		level.AddChild( Root );
		var random = new Random( 3030 );
		var maze = LayOut( random );
		OpenWalls = maze.Count( w => w.Open );
		MazeReport = maze.Select( w => (w.CellA, w.CellB, w.Radial ? $"radial {w.A0:0.#} deg r {w.R0:0}-{w.R1:0}" : $"arc r {w.R0:0} {w.A0:0.#}-{w.A1:0.#} deg", w.Open) ).ToList();

		var rock = new RockBuilder( Root );
		foreach ( var w in maze )
		{
			if ( w.Radial )
			{
				var r0 = w.R0;
				var r1 = w.R1;
				if ( w.Open )
				{
					// Stubs at both ends: an opening most of the wall's length.
					rock.Wall( Line( r0, r0 + 3f, w.A0 ), random );
					rock.Wall( Line( r1 - 3f, r1, w.A0 ), random );
					continue;
				}
				rock.Wall( Line( r0, r1, w.A0 ), random );
				continue;
			}
			var length = MathX.Rad( w.A1 - w.A0 ) * w.R0;
			if ( w.Open )
			{
				if ( length < 16f ) continue;
				var stub = MathX.Deg( 3.5f / w.R0 );
				rock.Wall( Arc( w.R0, w.A0, w.A0 + stub ), random );
				rock.Wall( Arc( w.R0, w.A1 - stub, w.A1 ), random );
				continue;
			}
			rock.Wall( Arc( w.R0, w.A0, w.A1 ), random );
		}

		// The cave mouths: a recess out past the outer wall, roofed over, where the enemies come in.
		SpawnSpots.Clear();
		foreach ( var a in Mouths )
		{
			var half = 5.5f;
			rock.Wall( Line( Rings[3] - 1f, PocketBack, a - half ), random );
			rock.Wall( Line( Rings[3] - 1f, PocketBack, a + half ), random );
			rock.Wall( Arc( PocketBack, a - half - 1f, a + half + 1f ), random );
			// A low roof over the mouth (a lintel of rock across it, then slabs back to the far wall).
			for ( var r = Rings[3] + 1f; r < PocketBack; r += 3.5f )
				rock.Slab( Plan( r, a ), Plan( r, a + 90f ) - Plan( 0f, 0f ), MathX.Rad( half * 2f + 4f ) * r, 8.5f + (float)random.NextDouble(), random );
			SpawnSpots.Add( Polar( (Rings[3] + PocketBack) * 0.5f + 1f, a ) );
		}

		// Rock arches over each main tunnel, in the middle and outer rings.
		foreach ( var a in MainTunnels )
			foreach ( var r in new[] { 50f, 66f } )
			{
				var across = MathX.Deg( 7.5f / r );
				var left = Plan( r, a - across );
				var right = Plan( r, a + across );
				rock.Pillar( left, 2.6f, 10f, random );
				rock.Pillar( right, 2.6f, 10f, random );
				rock.Span( left, right, 9.5f, random );
			}

		WallChunks = rock.Chunks;
		rock.Commit();
		BuildRoof( Root, random );

		// Each corridor cell's middle (scenery, glow bulbs, tests).
		CellCentres.Clear();
		for ( var k = 0; k < 3; k++ )
			for ( var s = 0; s < Sectors[k]; s++ )
				CellCentres.Add( Polar( (Rings[k] + Rings[k + 1]) * 0.5f, (s + 0.5f) * 360f / Sectors[k] ) );

		GD.Print( $"[abyss] the cave maze: {maze.Count} walls ({OpenWalls} open), {WallChunks} rock chunks, built in {watch.ElapsedMilliseconds} ms" );
	}

	static List<Vector2> Line( float r0, float r1, float degrees ) => new() { Plan( r0, degrees ), Plan( r1, degrees ) };

	static List<Vector2> Arc( float radius, float a0, float a1 )
	{
		var length = MathX.Rad( a1 - a0 ) * radius;
		var steps = Math.Max( 1, (int)MathF.Ceiling( length / 4f ) );
		var points = new List<Vector2>();
		for ( var i = 0; i <= steps; i++ ) points.Add( Plan( radius, MathX.Lerp( a0, a1, i / (float)steps ) ) );
		return points;
	}

	public static void Clear()
	{
		if ( Root is not null && GodotObject.IsInstanceValid( Root ) ) { Root.GetParent()?.RemoveChild( Root ); Root.Free(); }
		Root = null;
		walls.Clear();
		CellCentres.Clear();
		SpawnSpots.Clear();
	}

	/// <summary> The roof is solid (so nobody floats out through it). Added after the navmesh, so its top isn't walkable. </summary>
	public static void AddRoofCollider()
	{
		if ( roofMesh is null || Root is null ) return;
		var body = new StaticBody3D { Name = "Roof Solid", CollisionLayer = Layers.World, CollisionMask = 0 };
		body.AddChild( new CollisionShape3D { Shape = roofMesh.CreateTrimeshShape() } );
		Root.AddChild( body );
	}

	static ArrayMesh roofMesh;

	public static readonly StandardMaterial3D RockPaint = new()
	{
		VertexColorUseAsAlbedo = true,
		Roughness = 0.95f,
		AlbedoColor = Colors.White,
	};

	/// <summary> The roof: a rough rock ceiling over the whole map with a hole over the statue, and stalactites. </summary>
	static void BuildRoof( Node3D root, Random random )
	{
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		const float step = 4f;
		const float half = SeaTerrain.HalfSize + 8f;
		var n = (int)(2f * half / step);
		Vector3 At( int i, int j )
		{
			var x = -half + i * step;
			var z = -half + j * step;
			var r = MathF.Sqrt( x * x + z * z );
			// Round the skylight the roof bends up into the shaft.
			var lift = (1f - Smooth( SkylightRadius, SkylightRadius + 6f, r )) * 6f;
			return new Vector3( x, RoofAt( x, z ) + lift, z );
		}
		for ( var j = 0; j < n; j++ )
			for ( var i = 0; i < n; i++ )
			{
				var c = new Vector3( -half + (i + 0.5f) * step, 0f, -half + (j + 0.5f) * step );
				if ( c.Flat().Length() < SkylightRadius + 1f ) continue;
				var a = At( i, j ); var b = At( i + 1, j ); var d = At( i, j + 1 ); var e = At( i + 1, j + 1 );
				// Faces down (seen from below).
				Tri( st, a, e, b, RoofShade( a ) );
				Tri( st, a, d, e, RoofShade( a ) );
			}
		roofMesh = st.Commit();
		root.AddChild( new MeshInstance3D { Name = "Roof", Mesh = roofMesh, MaterialOverride = RockPaint, CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided } );

		// The shaft up through the roof to the far-off surface glow.
		var shaft = new MeshInstance3D
		{
			Name = "Skylight Shaft",
			Mesh = new CylinderMesh { TopRadius = SkylightRadius * 0.8f, BottomRadius = SkylightRadius + 1.5f, Height = 40f, RadialSegments = 24, Rings = 1, CapTop = false, CapBottom = false, FlipFaces = true },
			MaterialOverride = RockPaint,
		};
		root.AddChild( shaft );
		shaft.Position = new Vector3( 0f, RoofHeight + 20f, 0f );
		var sky = new StandardMaterial3D { AlbedoColor = new Color( 0.5f, 0.8f, 1f ), EmissionEnabled = true, Emission = new Color( 0.45f, 0.75f, 1f ), EmissionEnergyMultiplier = 2.2f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		var glow = new MeshInstance3D { Name = "Surface Glow", Mesh = new CylinderMesh { TopRadius = SkylightRadius, BottomRadius = SkylightRadius, Height = 0.2f, RadialSegments = 24 }, MaterialOverride = sky, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		root.AddChild( glow );
		glow.Position = new Vector3( 0f, RoofHeight + 39f, 0f );

		// Stalactites (and a few from the arches), hanging in clumps; none over the hub's light.
		var drip = new SurfaceTool();
		drip.Begin( Mesh.PrimitiveType.Triangles );
		var made = 0;
		for ( var i = 0; i < 260; i++ )
		{
			var r = MathF.Sqrt( R( 0.02f, 1f ) ) * 100f;
			var a = R( 0f, MathF.Tau );
			var x = MathF.Cos( a ) * r;
			var z = MathF.Sin( a ) * r;
			if ( r < SkylightRadius + 5f ) continue;
			var clump = random.Next( 1, 4 );
			for ( var c = 0; c < clump; c++ )
			{
				var px = x + R( -1.6f, 1.6f );
				var pz = z + R( -1.6f, 1.6f );
				var top = RoofAt( px, pz ) + 0.5f;
				var length = R( 1.5f, 7.5f ) * (c == 0 ? 1f : 0.55f);
				var width = length * R( 0.14f, 0.22f );
				Spike( drip, new Vector3( px, top, pz ), Vector3.Down, length, width, random );
				made++;
			}
		}
		root.AddChild( new MeshInstance3D { Name = "Stalactites", Mesh = drip.Commit(), MaterialOverride = RockPaint } );
		GD.Print( $"[abyss] the roof, {made} stalactites" );
	}

	static Color RoofShade( Vector3 p )
	{
		var v = 0.36f + lumps.GetNoise3D( p.X * 0.3f, p.Y, p.Z * 0.3f ) * 0.06f;
		return new Color( v * 0.9f, v * 0.92f, v );
	}

	static void Tri( SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Color colour )
	{
		var n = (c - a).Cross( b - a ).Normalized();
		st.SetColor( colour );
		st.SetNormal( n ); st.AddVertex( a );
		st.SetNormal( n ); st.AddVertex( b );
		st.SetNormal( n ); st.AddVertex( c );
	}

	/// <summary> A rough six-sided spike (a stalactite or stalagmite) from `root` along `dir`, faces outward. </summary>
	public static void Spike( SurfaceTool st, Vector3 root, Vector3 dir, float length, float width, Random random, float shade = -1f )
	{
		const int sides = 6;
		var y = dir.Normalized();
		var x = MathF.Abs( y.Dot( Vector3.Right ) ) > 0.9f ? y.Cross( Vector3.Back ).Normalized() : y.Cross( Vector3.Right ).Normalized();
		var z = x.Cross( y ).Normalized();
		var rings = new[] { (0f, 1f), (0.35f, 0.62f), (0.7f, 0.3f) };
		var points = new Vector3[rings.Length, sides];
		for ( var k = 0; k < rings.Length; k++ )
			for ( var s = 0; s < sides; s++ )
			{
				var a = s / (float)sides * MathF.Tau + k * 0.3f;
				var w = width * rings[k].Item2 * (0.8f + 0.4f * (float)random.NextDouble());
				points[k, s] = root + y * (rings[k].Item1 * length) + (x * MathF.Cos( a ) + z * MathF.Sin( a )) * w;
			}
		var tip = root + y * length;
		var v = shade >= 0f ? shade : 0.3f + (float)random.NextDouble() * 0.12f;
		var colour = new Color( v * 0.95f, v * 0.95f, v * 1.05f );
		void Out( Vector3 a, Vector3 b, Vector3 c )
		{
			var mid = (a + b + c) / 3f;
			var axis = root + y * (mid - root).Dot( y );
			if ( (c - a).Cross( b - a ).Dot( mid - axis ) < 0f ) (b, c) = (c, b);
			Tri( st, a, b, c, colour );
		}
		for ( var k = 0; k < rings.Length; k++ )
			for ( var s = 0; s < sides; s++ )
			{
				var s1 = (s + 1) % sides;
				if ( k + 1 < rings.Length )
				{
					Out( points[k, s], points[k + 1, s1], points[k + 1, s] );
					Out( points[k, s], points[k, s1], points[k + 1, s1] );
				}
				else Out( points[k, s], points[k, s1], tip );
			}
	}

	/// <summary>
	/// The cave rock: walls are rows of overlapping rough boxy boulders, each with a convex collider of its own
	/// points (pulled in a little), merged into a few meshes by direction so the far ones are culled.
	/// </summary>
	sealed class RockBuilder
	{
		readonly Node3D root;
		readonly Dictionary<int, SurfaceTool> batches = new();
		readonly StaticBody3D body;
		public int Chunks;
		static Vector3[] unit;
		static int[] faces, coarse;

		public RockBuilder( Node3D root )
		{
			this.root = root;
			body = new StaticBody3D { Name = "Cave Rock", CollisionLayer = Layers.World | Layers.Plant, CollisionMask = 0 };
			body.SetMeta( VegetationColliders.RockMeta, true );
			root.AddChild( body );
			MakeUnit();
		}

		/// <summary> A cube subdivided and pushed toward a sphere, then squared off again: a boxy boulder of radius 1. </summary>
		static void MakeUnit()
		{
			if ( unit is not null ) return;
			const int n = 4;
			var verts = new List<Vector3>();
			var tris = new List<int>();
			var axes = new[] { (Vector3.Right, Vector3.Up, Vector3.Back), (Vector3.Left, Vector3.Up, Vector3.Forward), (Vector3.Up, Vector3.Back, Vector3.Right), (Vector3.Down, Vector3.Forward, Vector3.Right), (Vector3.Back, Vector3.Up, Vector3.Left), (Vector3.Forward, Vector3.Up, Vector3.Right) };
			foreach ( var (normal, u, v) in axes )
			{
				var first = verts.Count;
				for ( var j = 0; j <= n; j++ )
					for ( var i = 0; i <= n; i++ )
					{
						var p = normal + u * (i / (float)n * 2f - 1f) + v * (j / (float)n * 2f - 1f);
						var s = p.Normalized();
						// Superellipsoid-ish: between the sphere and the cube.
						verts.Add( new Vector3( MathF.Sign( s.X ) * MathF.Pow( MathF.Abs( s.X ), 0.55f ), MathF.Sign( s.Y ) * MathF.Pow( MathF.Abs( s.Y ), 0.55f ), MathF.Sign( s.Z ) * MathF.Pow( MathF.Abs( s.Z ), 0.55f ) ) );
					}
				for ( var j = 0; j < n; j++ )
					for ( var i = 0; i < n; i++ )
					{
						var a = first + j * (n + 1) + i;
						var b = a + 1;
						var c = a + n + 1;
						var d = c + 1;
						tris.AddRange( new[] { a, d, b, a, c, d } );
					}
			}
			unit = verts.ToArray();
			faces = tris.ToArray();
			// The collider's points: each face's corners, edge middles and centre (a 3 by 3 grid of the 5 by 5).
			var keep = new List<int>();
			for ( var f = 0; f < 6; f++ )
				for ( var j = 0; j <= n; j += 2 )
					for ( var i = 0; i <= n; i += 2 )
						keep.Add( f * (n + 1) * (n + 1) + j * (n + 1) + i );
			coarse = keep.ToArray();
		}

		/// <summary> A wall of boulders along a line of floor plan points. </summary>
		public void Wall( List<Vector2> points, Random random )
		{
			for ( var i = 0; i < points.Count - 1; i++ )
			{
				var a = points[i];
				var b = points[i + 1];
				var length = a.DistanceTo( b );
				var count = Math.Max( 1, (int)MathF.Ceiling( length / 3.2f ) );
				for ( var c = 0; c <= count; c++ )
				{
					if ( c == count && i < points.Count - 2 ) continue;   // the next piece starts here
					var at = a.Lerp( b, c / (float)count );
					var along = (b - a).Normalized();
					// The main tunnels run straight and wide: no rock across them between the hub and the cave mouths.
					var r = at.Length();
					if ( r > HubRadius - 3f && r < Rings[3] + 2f && FromMainTunnel( at ) < TunnelHalfWidth ) continue;
					walls.Add( (at - along * 1.6f, at + along * 1.6f) );
					var height = 9.5f + 3.5f * (lumps.GetNoise2D( at.X * 0.4f, at.Y * 0.4f ) * 0.5f + 0.5f) + (float)random.NextDouble() * 1.2f;
					var size = new Vector3( 2.6f + (float)random.NextDouble() * 0.8f, height * 0.5f, WallThickness * 0.5f * (0.9f + (float)random.NextDouble() * 0.25f) );
					Boulder( at, along, size, random );
				}
			}
		}

		/// <summary> A free-standing column (an arch's leg). </summary>
		public void Pillar( Vector2 at, float radius, float height, Random random )
		{
			walls.Add( (at, at + new Vector2( 0.01f, 0f )) );
			Boulder( at, new Vector2( 1f, 0f ), new Vector3( radius * 0.6f, height * 0.5f, radius * 0.6f ), random );
		}

		/// <summary> A span of rock from one point to another at a height (an arch's top), in a few boulders. </summary>
		public void Span( Vector2 from, Vector2 to, float height, Random random )
		{
			var along = (to - from).Normalized();
			var count = Math.Max( 2, (int)MathF.Ceiling( from.DistanceTo( to ) / 3f ) );
			for ( var i = 0; i <= count; i++ )
			{
				var t = i / (float)count;
				var p = from.Lerp( to, t );
				// Sagging a little in the middle, thicker at the legs.
				var y = height - MathF.Sin( t * MathF.PI ) * 1.2f;
				Boulder( p, along, new Vector3( 2.2f, 1.3f + (1f - MathF.Sin( t * MathF.PI )) * 0.8f, 1.6f ), random, y );
			}
		}

		/// <summary> A roof slab over a cave mouth: boulders laid across at a height. </summary>
		public void Slab( Vector2 centre, Vector2 across, float width, float height, Random random )
		{
			var dir = across.Normalized();
			var count = Math.Max( 2, (int)MathF.Ceiling( width / 3f ) );
			for ( var i = 0; i <= count; i++ )
			{
				var p = centre + dir * MathX.Lerp( -width * 0.5f, width * 0.5f, i / (float)count );
				Boulder( p, dir, new Vector3( 2.2f, 1.2f, 2.2f ), random, height + 1.2f );
			}
		}

		void Boulder( Vector2 at, Vector2 along, Vector3 size, Random random, float? centreY = null )
		{
			var ground = SeaTerrain.HeightAt( at.X, at.Y );
			// Sat into the ground: its bottom well below the lowest ground round it.
			var low = MathF.Min( ground, MathF.Min( SeaTerrain.HeightAt( at.X + size.X, at.Y ), SeaTerrain.HeightAt( at.X - size.X, at.Y ) ) );
			var cy = centreY ?? low - 1.2f + size.Y;
			var centre = new Vector3( at.X, cy, at.Y );
			var x = new Vector3( along.X, 0f, along.Y );
			var basis = new Basis( x, Vector3.Up, x.Cross( Vector3.Up ) ) * new Basis( Vector3.Up, (float)(random.NextDouble() - 0.5) * 0.3f );
			var points = new Vector3[unit.Length];
			for ( var i = 0; i < unit.Length; i++ )
			{
				var u = unit[i];
				var world = centre + basis * new Vector3( u.X * size.X, u.Y * size.Y, u.Z * size.Z );
				// Rough, from a noise on the world position (neighbours share the look where they meet).
				var push = lumps.GetNoise3Dv( world ) * 0.65f + lumps.GetNoise3Dv( world * 2.7f ) * 0.28f + lumps.GetNoise3Dv( world * 6f ) * 0.08f;
				points[i] = world + (basis * u).Normalized() * push;
			}
			var key = (int)MathF.Floor( Wrap( MathX.Deg( MathF.Atan2( at.Y, at.X ) ) ) / 45f );
			if ( !batches.TryGetValue( key, out var st ) )
			{
				st = new SurfaceTool();
				st.Begin( Mesh.PrimitiveType.Triangles );
				batches[key] = st;
			}
			for ( var f = 0; f < faces.Length; f += 3 )
			{
				var a = points[faces[f]]; var b = points[faces[f + 1]]; var c = points[faces[f + 2]];
				var mid = (a + b + c) / 3f;
				// Basalt dark, a little lighter on top, cooler low down; streaks from the noise.
				var up = (c - a).Cross( b - a ).Normalized().Y;
				// Strata: bands along the height, lighter ledges on top, darker cracks low down.
				var band = MathF.Sin( mid.Y * 2.1f + lumps.GetNoise2D( mid.X, mid.Z ) * 3f ) * 0.035f;
				var v = 0.32f + 0.1f * up + band + lumps.GetNoise3D( mid.X * 1.3f, mid.Y * 3f, mid.Z * 1.3f ) * 0.09f - (up < -0.3f ? 0.06f : 0f);
				var colour = new Color( v * 0.9f, v * 0.94f, v * 1.06f );
				Tri( st, a, b, c, colour );
			}
			// The collider: every other point of its own (a simpler shape, so the navmesh along the walls has fewer,
			// bigger polygons), pulled in toward its middle a touch.
			var hull = new Vector3[coarse.Length];
			for ( var i = 0; i < coarse.Length; i++ ) hull[i] = centre + (points[coarse[i]] - centre) * 0.96f;
			body.AddChild( new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = hull } } );
			Chunks++;
		}

		public void Commit()
		{
			foreach ( var (key, st) in batches )
				root.AddChild( new MeshInstance3D { Name = $"Cave Rock {key}", Mesh = st.Commit(), MaterialOverride = RockPaint } );
		}
	}
}
