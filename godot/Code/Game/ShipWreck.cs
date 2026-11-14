using Godot;

/// <summary>
/// The sunken liner on the wreck site (area 2, from wave 11; issue #35), after the Titanic: a black hull with a red
/// bottom, a white band and a white deckhouse with four buff funnels, lying on the flat sand plain heeled over 27
/// degrees and buried along its low side. Every floor is still walkable at that tilt. Inside, three decks joined by
/// ramps (a grand staircase): a hallway of cabins, a dining saloon with chandeliers, tables, plates and silverware
/// (much of it slid down to the low side), and a kitchen. Broken windows along the low side sit at sand level and
/// there is a hull breach, so you can walk in from outside. Built from boxes, cylinders and spheres merged into a
/// few meshes, with box colliders, so the navmesh runs through it too. Ship space: X along the ship (bow +X), Y up,
/// Z across; metres.
/// </summary>
public static class ShipWreck
{
	public const float Roll = 27f;
	public const float Pitch = -2.5f;
	public const float HalfLength = 48f;
	const float FloorB = 4f, FloorA = 8.5f, DeckMain = 13f, Wall = 0.35f, Slab = 0.35f;

	/// <summary> True on the wreck site once the ship has been placed (the ground then shapes itself round it). </summary>
	public static bool Placed { get; private set; }
	/// <summary> Ship space to world. </summary>
	public static Transform3D Frame { get; private set; }
	public static Node3D Root { get; private set; }

	/// <summary> Half the beam at a point along the ship: a long straight middle, a fine bow, a rounded stern. </summary>
	public static float HalfBeam( float x )
	{
		if ( x > 30f ) return MathF.Max( 0.5f, 8f * (1f - MathF.Pow( MathF.Min( 1f, (x - 30f) / 18.5f ), 1.6f )) );
		if ( x < -40f ) return MathF.Max( 0.5f, 8f * MathF.Sqrt( MathF.Max( 0f, 1f - MathF.Pow( (x + 40f) / 8.6f, 2f ) ) ) );
		return 8f;
	}

	/// <summary> Places the ship's frame on the plain (before the ground is built, which shapes itself to the hull). </summary>
	public static void Place()
	{
		var basis = new Basis( Vector3.Right, MathX.Rad( Roll ) ) * new Basis( Vector3.Back, MathX.Rad( Pitch ) );
		// The low side's lower window row sits just at the sand, so its broken windows are doorways in.
		var low = basis * new Vector3( 0f, 5.2f, 8f );
		var origin = new Vector3( SeaTerrain.PlainCentre.X, 0.3f - low.Y, SeaTerrain.PlainCentre.Y );
		Frame = new Transform3D( basis, origin );
		Placed = true;
	}

	public static void Clear()
	{
		Placed = false;
		if ( Root is not null && GodotObject.IsInstanceValid( Root ) ) { Root.GetParent()?.RemoveChild( Root ); Root.Free(); }
		Root = null;
	}

	/// <summary>
	/// The ground round the ship (area 2): scooped out under the hull so nothing pokes into it, and heaped up against
	/// the high side and the raised stern so it reads as buried.
	/// </summary>
	public static float ShapeGround( float x, float z, float h )
	{
		var b = Frame.Basis;
		var o = Frame.Origin;
		var up = b * Vector3.Up;
		var dx0 = x - o.X; var dz0 = z - o.Z;
		// Where a vertical line here meets the plane of the hull's bottom.
		var t = o.Y - (up.X * dx0 + up.Z * dz0) / up.Y;
		var p = new Vector3( x, t, z ) - o;
		var lx = p.Dot( b * Vector3.Right );
		var lz = p.Dot( b * Vector3.Back );
		var ox = MathF.Max( 0f, MathF.Abs( lx ) - (HalfLength - 0.5f) );
		var oz = MathF.Max( 0f, MathF.Abs( lz ) - HalfBeam( MathX.Clamp( lx, -HalfLength, HalfLength ) ) );
		var outside = MathF.Sqrt( ox * ox + oz * oz );
		if ( outside < 0.6f )
		{
			// In the breach the sand runs in to the deck B floor, so you can walk in off the sea bed.
			if ( MathF.Abs( lx ) < 5.5f && lz > 2f )
			{
				var floorB = o.Y - (up.X * dx0 + up.Z * dz0 - (FloorB + Slab * 0.5f)) / up.Y;
				return MathF.Min( h, floorB - 0.05f );
			}
			return MathF.Min( h, t - 0.8f );
		}
		if ( outside < 9f && t > h ) h = MathF.Max( h, MathF.Min( t, 5f ) * (1f - outside / 9f) - 0.3f );
		return h;
	}

	// ---- Building ----

	sealed class Kit
	{
		readonly Dictionary<string, (SurfaceTool Tool, Material Material)> batches = new();
		public readonly Node3D Node;
		readonly StaticBody3D body;
		public int Pieces, Solids;

		public Kit( Node3D node )
		{
			Node = node;
			body = new StaticBody3D { Name = "Ship Body", CollisionLayer = Layers.World, CollisionMask = 0 };
			node.AddChild( body );
		}

		SurfaceTool Batch( string key )
		{
			if ( batches.TryGetValue( key, out var b ) ) return b.Tool;
			var st = new SurfaceTool();
			st.Begin( Mesh.PrimitiveType.Triangles );
			batches[key] = (st, Materials[key]);
			return st;
		}

		public void Add( string key, Godot.Mesh mesh, Transform3D at )
		{
			Batch( key ).AppendFrom( mesh, 0, at );
			Pieces++;
		}

		public void Box( string key, Vector3 centre, Vector3 size, Basis? turn = null, bool solid = true )
		{
			var at = new Transform3D( turn ?? Basis.Identity, centre );
			Add( key, new BoxMesh { Size = size }, at );
			if ( solid ) Solid( at, size );
		}

		public void Solid( Transform3D at, Vector3 size )
		{
			body.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Transform = at } );
			Solids++;
		}

		public void Cylinder( string key, Vector3 centre, float radius, float height, Basis? turn = null, int sides = 14, float topRadius = -1f ) =>
			Add( key, new CylinderMesh { TopRadius = topRadius < 0f ? radius : topRadius, BottomRadius = radius, Height = height, RadialSegments = sides, Rings = 1 }, new Transform3D( turn ?? Basis.Identity, centre ) );

		public void Sphere( string key, Vector3 centre, float radius, int sides = 10 ) =>
			Add( key, new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = sides, Rings = sides / 2 }, new Transform3D( Basis.Identity, centre ) );

		public void Commit()
		{
			foreach ( var (key, (tool, material)) in batches )
			{
				tool.GenerateNormals();
				var mi = new MeshInstance3D { Name = key, Mesh = tool.Commit(), MaterialOverride = material };
				if ( key is "glass" or "glow_warm" or "glow_green" or "glow_purple" or "vine" or "kudzu_green" or "kudzu_dark" or "kudzu_purple" ) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
				Node.AddChild( mi );
			}
		}
	}

	static StandardMaterial3D Mat( Color c, float rough = 0.75f, float metal = 0f ) => new() { AlbedoColor = c, Roughness = rough, Metallic = metal };
	static StandardMaterial3D Glow( Color c, float energy ) => new() { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy, Roughness = 0.3f };

	static readonly Dictionary<string, Material> Materials = new()
	{
		["hull_black"] = Mat( new Color( 0.07f, 0.07f, 0.08f ), 0.7f ),
		["hull_red"] = Mat( new Color( 0.42f, 0.09f, 0.06f ), 0.8f ),
		["rust"] = Mat( new Color( 0.45f, 0.22f, 0.1f ), 0.95f ),
		["white"] = Mat( new Color( 0.86f, 0.85f, 0.8f ), 0.6f ),
		["deck_wood"] = Mat( new Color( 0.45f, 0.35f, 0.24f ), 0.85f ),
		["panel"] = Mat( new Color( 0.72f, 0.66f, 0.52f ), 0.7f ),
		["dark_wood"] = Mat( new Color( 0.3f, 0.17f, 0.09f ), 0.6f ),
		["carpet"] = Mat( new Color( 0.45f, 0.07f, 0.09f ), 0.95f ),
		["cloth"] = Mat( new Color( 0.92f, 0.9f, 0.85f ), 0.9f ),
		["china"] = Mat( new Color( 0.96f, 0.95f, 0.92f ), 0.25f ),
		["silver"] = Mat( new Color( 0.78f, 0.79f, 0.82f ), 0.2f, 0.9f ),
		["brass"] = Mat( new Color( 0.85f, 0.64f, 0.26f ), 0.3f, 0.85f ),
		["steel"] = Mat( new Color( 0.55f, 0.58f, 0.6f ), 0.35f, 0.7f ),
		["iron"] = Mat( new Color( 0.12f, 0.12f, 0.13f ), 0.5f, 0.5f ),
		["buff"] = Mat( new Color( 0.84f, 0.6f, 0.3f ), 0.7f ),
		["bedding"] = Mat( new Color( 0.75f, 0.72f, 0.66f ), 0.95f ),
		["glass"] = new StandardMaterial3D { AlbedoColor = new Color( 0.2f, 0.45f, 0.55f, 0.35f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f, Metallic = 0.3f, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
		["glow_warm"] = Glow( new Color( 1f, 0.82f, 0.5f ), 2.4f ),
		["glow_green"] = Glow( new Color( 0.3f, 1f, 0.28f ), 2.6f ),
		["glow_purple"] = Glow( new Color( 0.66f, 0.22f, 1f ), 2.2f ),
		// Alien kudzu: near-black vines, leaves in neon green, deep green and purple (the reef's alien colours).
		["vine"] = Mat( new Color( 0.05f, 0.09f, 0.04f ), 0.7f ),
		["kudzu_green"] = new StandardMaterial3D { AlbedoColor = new Color( 0.3f, 0.85f, 0.22f ), Roughness = 0.55f, EmissionEnabled = true, Emission = new Color( 0.3f, 0.9f, 0.2f ), EmissionEnergyMultiplier = 0.25f, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
		["kudzu_dark"] = new StandardMaterial3D { AlbedoColor = new Color( 0.07f, 0.2f, 0.06f ), Roughness = 0.6f, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
		["kudzu_purple"] = new StandardMaterial3D { AlbedoColor = new Color( 0.36f, 0.09f, 0.52f ), Roughness = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
	};

	public static Node3D Build( Node3D level )
	{
		Clear();
		Place();
		foreach ( var m in Materials.Values ) m.SetMeta( "no_caustics", true );
		var random = new Random( 1912 );
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		Root = new Node3D { Name = "Sunken Liner", Transform = Frame };
		level.AddChild( Root );
		var kit = new Kit( Root );
		// "Down" in ship space: the way a hanging thing or a sliding plate goes (the ship is heeled, gravity is not).
		var down = Frame.Basis.Inverse() * Vector3.Down;
		var hang = new Basis( new Quaternion( Vector3.Down, down ) );

		Hull( kit, random, R );
		Decks( kit );
		Cabins( kit, random, R, down );
		Dining( kit, random, R, down, hang );
		Kitchen( kit, random, R, down, hang );
		Topside( kit, random, R );
		// Alien growth: glowing pores on the hull outside.
		for ( var i = 0; i < 90; i++ )
		{
			var x = R( -44f, 44f );
			var s = random.NextDouble() < 0.5 ? -1f : 1f;
			var y = R( 0.5f, 12.5f );
			kit.Sphere( random.NextDouble() < 0.7 ? "glow_green" : "glow_purple", new Vector3( x, y, s * (HalfBeam( x ) + 0.12f) ), R( 0.06f, 0.18f ), 8 );
		}
		Kudzu( kit, down );
		kit.Commit();
		Lights();
		GD.Print( $"[ship] {kit.Pieces} pieces in {Root.GetChildCount() - 1} meshes, {kit.Solids} colliders" );
		return Root;
	}

	// ---- The hull: sides in bands with rows of windows, a bottom, a breach on the low side ----

	static void Hull( Kit kit, Random random, Func<float, float, float> R )
	{
		const float seg = 2.5f;
		for ( var x = -HalfLength + seg * 0.5f; x < HalfLength; x += seg )
		{
			var w = HalfBeam( x );
			var w0 = HalfBeam( x - seg * 0.5f );
			var w1 = HalfBeam( x + seg * 0.5f );
			// Bottom plate.
			kit.Box( "hull_red", new Vector3( x, 0.2f, 0f ), new Vector3( seg + 0.05f, 0.4f, 2f * w ) );
			foreach ( var s in new[] { -1f, 1f } )
			{
				var dw = (w1 - w0) * s;
				var len = MathF.Sqrt( seg * seg + dw * dw ) + 0.06f;
				var turn = new Basis( Vector3.Up, -MathF.Asin( dw / len ) );
				var along = turn * Vector3.Right;
				var at = new Vector3( x, 0f, s * w );
				var straight = MathF.Abs( x ) < 36f;
				var breach = s > 0f && x > -6f && x < 6f;
				void Band( string key, float y0, float y1, float offset = 0f, float length = -1f )
				{
					var l = length < 0f ? len : length;
					kit.Box( key, at + along * offset + new Vector3( 0f, (y0 + y1) * 0.5f, 0f ), new Vector3( l, y1 - y0, Wall ), turn );
				}
				if ( breach )
				{
					// Torn open: the plating is gone up to the upper window row, a few bent plates hang off the edges.
					Band( "hull_black", 9.7f, 13f );
					if ( x < -3.5f || x > 3.5f ) Band( "rust", 0f, R( 2f, 5f ) );
					continue;
				}
				if ( !straight )
				{
					Band( "hull_red", 0f, 4f ); Band( "hull_black", 4f, 12.4f ); Band( "white", 12.4f, 13f );
					continue;
				}
				Band( "hull_red", 0f, 4f );
				Band( "hull_black", 4f, 5.2f );
				Band( "hull_black", 7.2f, 9.7f );
				Band( "hull_black", 11.7f, 12.4f );
				Band( "white", 12.4f, 13f );
				// Each segment: a pillar of plating and a window. Broken ones are open (with a few shards), the rest glazed.
				foreach ( var (y0, y1) in new[] { (5.2f, 7.2f), (9.7f, 11.7f) } )
				{
					Band( "hull_black", y0, y1, -len * 0.5f + 0.6f, 1.2f );
					var windowAt = at + along * (len * 0.5f - 0.65f) + new Vector3( 0f, (y0 + y1) * 0.5f, 0f );
					var broken = random.NextDouble() < (s > 0f ? 0.65 : 0.35);
					if ( !broken )
					{
						kit.Box( "glass", windowAt, new Vector3( 1.3f, y1 - y0, 0.06f ), turn );
						continue;
					}
					for ( var k = random.Next( 2, 5 ); k > 0; k-- )
					{
						var edge = random.Next( 4 );
						var o = edge switch
						{
							0 => new Vector3( R( -0.5f, 0.5f ), (y1 - y0) * 0.5f - 0.1f, 0f ),
							1 => new Vector3( R( -0.5f, 0.5f ), -(y1 - y0) * 0.5f + 0.1f, 0f ),
							2 => new Vector3( -0.6f, R( -0.8f, 0.8f ), 0f ),
							_ => new Vector3( 0.6f, R( -0.8f, 0.8f ), 0f ),
						};
						kit.Box( "glass", windowAt + turn * o, new Vector3( R( 0.12f, 0.3f ), R( 0.15f, 0.4f ), 0.04f ), turn * new Basis( Vector3.Back, R( -0.8f, 0.8f ) ), solid: false );
					}
				}
			}
		}
		// Bent plates round the breach and a scatter of rivet-rust.
		for ( var i = 0; i < 6; i++ )
		{
			var x = random.NextDouble() < 0.5 ? R( -6.5f, -4.5f ) : R( 4.5f, 6.5f );
			kit.Box( "rust", new Vector3( x, R( 1f, 8f ), 8.4f ), new Vector3( R( 1f, 2.2f ), R( 0.8f, 1.8f ), 0.08f ), new Basis( Vector3.Up, R( -0.9f, 0.9f ) ) * new Basis( Vector3.Right, R( -0.5f, 0.5f ) ), solid: false );
		}
	}

	// ---- Alien kudzu over the outside of the ship ----

	/// <summary>
	/// A flat face of the ship the kudzu can creep over: its normal, the two directions across it, and whether a
	/// point (on it) is still on the face. Snap moves a point onto the face (the hull's sides follow its curve).
	/// </summary>
	sealed record Face( Vector3 Normal, Vector3 U, Vector3 V, Func<Vector3, bool> Inside, Func<Vector3, Vector3> Snap );

	/// <summary>
	/// Alien kudzu (playtest): creeping vines with three-lobed leaves smothering the outside of the wreck. It blankets
	/// the exposed high side of the hull (draping down from the deck rail), creeps over the main deck, the deckhouse
	/// walls and roof, climbs the low side up from the sand (the breach left open), and hangs in curtains off the
	/// low side's deck edge, straight down with the world's gravity. Leaves in neon green, deep green and purple,
	/// with glowing seed pods here and there. Only for show: no colliders. Its own seed, so the rest of the wreck
	/// is unchanged.
	/// </summary>
	static void Kudzu( Kit kit, Vector3 down )
	{
		var random = new Random( 2077 );
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var deck = DeckMain + Slab * 0.5f;
		const float hx0 = -24f, hx1 = 22f, hz = 5.5f, hh = 3.4f;
		var roofTop = deck + hh + 0.3f;
		float Side( float x ) => HalfBeam( x ) + Wall * 0.5f + 0.03f;
		bool OffFunnels( Vector3 p ) => new[] { 16f, 5f, -6f, -17f }.All( fx => new Vector2( p.X - fx, p.Z ).Length() > 2.1f );

		var faces = new List<(Face Face, int Patches, Func<Vector3> Start, float Drift)>
		{
			// The high side's hull (-Z), the most exposed: thick, draping down from the rail.
			(new Face( Vector3.Forward, Vector3.Right, Vector3.Up, p => p.X > -34f && p.X < 29.5f && p.Y > 0.8f && p.Y < 12.95f, p => p with { Z = -Side( p.X ) } ),
				26, () => new Vector3( R( -33f, 29f ), random.NextDouble() < 0.6 ? 12.8f : R( 3f, 12.5f ), 0f ), -0.35f),
			// The low side (+Z): climbing up from the sand (which reaches about the lower window row), not over the breach.
			(new Face( Vector3.Back, Vector3.Right, Vector3.Up, p => p.X > -34f && p.X < 29.5f && MathF.Abs( p.X ) > 7f && p.Y > 5.4f && p.Y < 12.95f, p => p with { Z = Side( p.X ) } ),
				12, () => new Vector3( random.NextDouble() < 0.5 ? R( -33f, -7.5f ) : R( 7.5f, 29f ), 5.6f, 0f ), 0.35f),
			// The main deck, round the deckhouse, clear of the rail and the aft stair's opening.
			(new Face( Vector3.Up, Vector3.Right, Vector3.Back, p => MathF.Abs( p.X ) < HalfLength - 3f && MathF.Abs( p.Z ) < HalfBeam( p.X ) - 0.5f
					&& !(p.X > hx0 - 0.2f && p.X < hx1 + 0.2f && MathF.Abs( p.Z ) < hz + 0.2f) && !(p.X > -38.7f && p.X < -24.8f && MathF.Abs( p.Z ) < 3.2f), p => p with { Y = deck + 0.02f } ),
				10, () => new Vector3( R( -42f, 40f ), 0f, R( -7f, 7f ) ), 0f),
			// The deckhouse roof, round the funnels.
			(new Face( Vector3.Up, Vector3.Right, Vector3.Back, p => p.X > hx0 && p.X < hx1 && MathF.Abs( p.Z ) < hz + 0.2f && OffFunnels( p ), p => p with { Y = roofTop + 0.02f } ),
				6, () => new Vector3( R( hx0 + 1f, hx1 - 1f ), 0f, R( -hz, hz ) ), 0f),
			// The deckhouse's two long walls, outside.
			(new Face( Vector3.Forward, Vector3.Right, Vector3.Up, p => p.X > hx0 && p.X < hx1 && p.Y > deck + 0.05f && p.Y < deck + hh, p => p with { Z = -(hz + 0.15f) } ),
				6, () => new Vector3( R( hx0 + 1f, hx1 - 1f ), deck + 0.1f, 0f ), 0.3f),
			(new Face( Vector3.Back, Vector3.Right, Vector3.Up, p => p.X > hx0 && p.X < hx1 && p.Y > deck + 0.05f && p.Y < deck + hh, p => p with { Z = hz + 0.15f } ),
				6, () => new Vector3( R( hx0 + 1f, hx1 - 1f ), deck + 0.1f, 0f ), 0.3f),
		};

		var leaves = 0;
		foreach ( var (face, patches, start, drift) in faces )
			for ( var patch = 0; patch < patches; patch++ )
			{
				var origin = face.Snap( start() );
				if ( !face.Inside( origin ) ) continue;
				// A patch: several vines spreading from one root, each a wandering walk over the face.
				for ( var vine = random.Next( 3, 7 ); vine > 0; vine-- )
				{
					var angle = R( 0f, MathF.Tau );
					var p = origin;
					var steps = random.Next( 8, 21 );
					for ( var step = 0; step < steps; step++ )
					{
						angle += R( -0.55f, 0.55f );
						var heading = face.U * MathF.Cos( angle ) + face.V * (MathF.Sin( angle ) + drift);
						var next = face.Snap( p + heading.Normalized() * 0.32f );
						if ( !face.Inside( next ) ) break;
						Stem( kit, p + face.Normal * 0.04f, next + face.Normal * 0.04f, 0.035f );
						if ( step % 2 == 1 ) leaves += Leaf( kit, random, next + face.Normal * 0.05f, face.Normal, (next - p).Normalized() );
						if ( random.NextDouble() < 0.035 ) kit.Sphere( random.NextDouble() < 0.6 ? "glow_green" : "glow_purple", next + face.Normal * 0.1f, R( 0.05f, 0.1f ), 6 );
						p = next;
					}
				}
			}

		// Curtains off the low side's deck edge: they hang straight down with the world's gravity, out from the
		// heeled hull, until they reach the sand.
		var hang = down.Normalized();
		for ( var i = 0; i < 40; i++ )
		{
			var x = R( -33f, 29f );
			if ( MathF.Abs( x ) < 6.5f ) continue;   // the breach stays open to walk in
			var p = new Vector3( x, deck - 0.1f, Side( x ) + 0.05f );
			var length = R( 1.5f, 6f );
			var sway = R( 0f, MathF.Tau );
			for ( var travelled = 0f; travelled < length; travelled += 0.3f )
			{
				var next = p + hang * 0.3f + Vector3.Right * MathF.Sin( sway + travelled * 1.3f ) * 0.05f;
				var world = Frame * next;
				if ( world.Y < SeaTerrain.HeightAt( world ) + 0.1f ) break;
				Stem( kit, p, next, 0.03f );
				var outward = Vector3.Back.Slide( hang ).Normalized();
				if ( random.NextDouble() < 0.6 ) leaves += Leaf( kit, random, next + outward * 0.04f, outward, hang );
				p = next;
			}
			if ( random.NextDouble() < 0.4 ) kit.Sphere( random.NextDouble() < 0.6 ? "glow_green" : "glow_purple", p, R( 0.06f, 0.11f ), 6 );
		}
		GD.Print( $"[ship] kudzu: {leaves} leaves" );
	}

	/// <summary> A length of vine between two points. </summary>
	static void Stem( Kit kit, Vector3 from, Vector3 to, float radius )
	{
		var axis = to - from;
		var length = axis.Length();
		if ( length < 1e-4f ) return;
		kit.Cylinder( "vine", (from + to) * 0.5f, radius, length + radius, new Basis( new Quaternion( Vector3.Up, axis / length ) ), 5 );
	}

	/// <summary> A kudzu leaf of three lobes on a face (lying on it, along the vine, fanned a little). Returns the lobes added. </summary>
	static int Leaf( Kit kit, Random random, Vector3 at, Vector3 normal, Vector3 along )
	{
		var roll = random.NextDouble();
		var key = roll < 0.5 ? "kudzu_green" : roll < 0.82 ? "kudzu_dark" : "kudzu_purple";
		var size = 0.7f + (float)random.NextDouble() * 0.6f;
		var side = (float)random.NextDouble() < 0.5f ? -1f : 1f;
		// The leaf stands off the vine to one side, its lobes spread from there.
		var across = normal.Cross( along ).Normalized() * side;
		var stalk = at + across * 0.12f * size;
		var lift = (float)random.NextDouble() * 0.35f;   // some leaves lift off the surface a little
		foreach ( var fan in new[] { -0.75f, 0f, 0.75f } )
		{
			var dir = (across * MathF.Cos( fan ) + along * MathF.Sin( fan )).Normalized();
			// The lobe's width stays flat on the face; its tip tilts up off it by `lift`. Square axes (width, up, tip).
			var width = normal.Cross( dir ).Normalized();
			var tip = (dir * MathF.Cos( lift ) + normal * MathF.Sin( lift )).Normalized();
			var up = tip.Cross( width );
			var length = (fan == 0f ? 0.2f : 0.16f) * size;
			var basis = new Basis( width * length * 0.65f, up * 0.02f, tip * length );
			kit.Add( key, LeafMesh, new Transform3D( basis, stalk + tip * length * 0.9f + up * 0.01f ) );
		}
		return 3;
	}

	/// <summary> One lobe: a low-poly unit sphere, flattened by its transform. </summary>
	static readonly SphereMesh LeafMesh = new() { Radius = 1f, Height = 2f, RadialSegments = 6, Rings = 2 };   // few triangles: there are thousands

	// ---- Floors and ramps ----

	static void Decks( Kit kit )
	{
		const float seg = 2.5f;
		for ( var x = -HalfLength + seg * 0.5f; x < HalfLength; x += seg )
		{
			var w = HalfBeam( x ) - Wall * 0.5f;
			if ( w < 1.2f ) continue;
			// Deck B: whole. Deck A: an opening over the grand staircase at the bow end. Main deck: one over the aft stair.
			kit.Box( "deck_wood", new Vector3( x, FloorB, 0f ), new Vector3( seg + 0.04f, Slab, 2f * w ) );
			Floor( kit, "carpet", x, FloorA, w, x > 26f && x < 38f );
			Floor( kit, "deck_wood", x, DeckMain, w, x > -38.5f && x < -25f );
		}
		static void Floor( Kit kit, string key, float x, float y, float w, bool opening )
		{
			if ( !opening ) { kit.Box( key, new Vector3( x, y, 0f ), new Vector3( 2.54f, Slab, 2f * w ) ); return; }
			foreach ( var s in new[] { -1f, 1f } )
				if ( w > 3.2f ) kit.Box( key, new Vector3( x, y, s * (3f + w) * 0.5f ), new Vector3( 2.54f, Slab, w - 3f ) );
		}
		// The grand staircase: from the end of the deck B hallway up toward the bow to the dining deck, and from the
		// back of the kitchen up toward the stern to the main deck.
		Ramp( kit, new Vector3( 26f, FloorB, 0f ), new Vector3( 37.5f, FloorA, 0f ) );
		Ramp( kit, new Vector3( -26f, FloorA, 0f ), new Vector3( -37.98f, DeckMain, 0f ) );
	}

	/// <summary> A carpeted ramp whose surface runs from one floor's top to the edge of the next floor's top, flush. </summary>
	static void Ramp( Kit kit, Vector3 from, Vector3 to )
	{
		from.Y += Slab * 0.5f;
		to.Y += Slab * 0.5f;
		var run = to - from;
		var dir = run.Normalized();
		var angle = MathF.Atan2( run.Y, MathF.Abs( run.X ) ) * (run.X < 0f ? -1f : 1f);
		var turn = new Basis( Vector3.Back, angle );
		var normal = turn * Vector3.Up;
		// Overhang only at the bottom (into the floor below); the top ends exactly at the upper floor's edge.
		var length = run.Length() + 0.4f;
		var mid = (from + to) * 0.5f - dir * 0.2f - normal * 0.15f;
		kit.Box( "carpet", mid, new Vector3( length, 0.3f, 4.6f ), turn );
		foreach ( var s in new[] { -1f, 1f } )
		{
			kit.Box( "dark_wood", mid + normal * 0.45f + new Vector3( 0f, 0f, s * 2.45f ), new Vector3( length, 0.6f, 0.25f ), turn );
			kit.Box( "brass", mid + normal * 1.05f + new Vector3( 0f, 0f, s * 2.45f ), new Vector3( length, 0.08f, 0.08f ), turn, solid: false );
		}
		// Steps drawn on the carpet.
		var steps = (int)(length / 0.45f);
		for ( var i = 1; i < steps; i++ )
			kit.Box( "brass", from + run * (i / (float)steps) + normal * 0.02f, new Vector3( 0.05f, 0.04f, 4.4f ), turn, solid: false );
	}

	// ---- Deck B: a hallway with cabins either side ----

	static void Cabins( Kit kit, Random random, Func<float, float, float> R, Vector3 down )
	{
		const float x0 = -34f, x1 = 26f, room = 5f, half = 1.3f;
		var floor = FloorB + Slab * 0.5f;
		var cabinChairs = new Random( 1914 );
		var ceiling = FloorA - Slab * 0.5f;
		var height = ceiling - floor;
		for ( var x = x0; x < x1 - 0.1f; x += room )
		{
			foreach ( var s in new[] { -1f, 1f } )
			{
				// The hallway wall with a doorway into the cabin.
				var z = s * half;
				var doorHalf = 0.65f;
				var piece = (room - 2f * doorHalf) * 0.5f;
				kit.Box( "panel", new Vector3( x + piece * 0.5f, floor + height * 0.5f, z ), new Vector3( piece, height, 0.15f ) );
				kit.Box( "panel", new Vector3( x + room - piece * 0.5f, floor + height * 0.5f, z ), new Vector3( piece, height, 0.15f ) );
				kit.Box( "panel", new Vector3( x + room * 0.5f, ceiling - (height - 2.4f) * 0.5f, z ), new Vector3( 2f * doorHalf, height - 2.4f, 0.15f ) );
				// The wall between this cabin and the next.
				var depth = 8f - Wall - half;
				kit.Box( "panel", new Vector3( x, floor + height * 0.5f, s * (half + depth * 0.5f) ), new Vector3( 0.15f, height, depth ) );
				// Furniture: a bed against the hull, a wardrobe, a little table, and things fallen about.
				var cx = x + room * 0.5f;
				var bedZ = s * (8f - Wall - 0.75f);
				kit.Box( "dark_wood", new Vector3( cx + 0.6f, floor + 0.25f, bedZ ), new Vector3( 2.1f, 0.5f, 1.2f ) );
				kit.Box( "bedding", new Vector3( cx + 0.6f, floor + 0.58f, bedZ ), new Vector3( 2f, 0.16f, 1.1f ), solid: false );
				kit.Box( "china", new Vector3( cx + 1.4f, floor + 0.72f, bedZ ), new Vector3( 0.4f, 0.12f, 0.8f ), solid: false );
				kit.Box( "dark_wood", new Vector3( cx - 1.8f, floor + 1f, s * (half + 0.45f) ), new Vector3( 1f, 2f, 0.6f ) );
				var tableAt = new Vector3( cx - 1.2f, floor, s * (half + 3.2f) );
				kit.Cylinder( "dark_wood", tableAt + new Vector3( 0f, 0.38f, 0f ), 0.06f, 0.76f );
				kit.Cylinder( "dark_wood", tableAt + new Vector3( 0f, 0.77f, 0f ), 0.4f, 0.04f );
				// A chair: inside this cabin, clear of the wardrobe, the bed (it starts at 6.3 m across) and the table leg
				// (it can tuck under the top), and of the suitcase.
				var cabin = new ChairScatter( kit, cabinChairs, floor, x + 0.1f, x + room - 0.1f,
					s > 0f ? half + 0.1f : -6.25f, s > 0f ? 6.25f : -(half + 0.1f) );
				cabin.Block( new Vector3( cx - 1.8f, floor, s * (half + 0.45f) ), 0.6f );
				cabin.Block( tableAt, 0.25f );
				if ( random.NextDouble() < 0.6 )
				{
					var suitcase = tableAt + new Vector3( R( 0.5f, 1.2f ), 0.25f, s * R( 0.5f, 1.5f ) );
					kit.Box( "rust", suitcase, new Vector3( 0.7f, 0.45f, 0.4f ), new Basis( Vector3.Up, R( 0f, 3f ) ), solid: false );
					cabin.Block( suitcase, 0.42f );
				}
				var chairRoll = cabin.R( 0f, 1f );
				if ( chairRoll < 0.35f )
				{
					// Still at the table, facing it.
					for ( var k = 0; k < 6; k++ )
					{
						var b = cabin.R( 0f, MathF.Tau );
						var away = new Vector3( MathF.Cos( b ), 0f, MathF.Sin( b ) );
						if ( cabin.Place( () => tableAt + away * 0.62f, 1, BackToward( away.X, away.Z ), ChairPose.Upright ) ) break;
					}
				}
				else if ( chairRoll < 0.85f )
					cabin.Place( () => tableAt + new Vector3( cabin.R( -1.2f, 1.2f ), 0f, s * cabin.R( 0.5f, 1.8f ) ), 8, cabin.R( 0f, MathF.Tau ), cabin.Fallen() );
			}
		}
		// The hallway: a runner of carpet and dim wall lamps.
		kit.Box( "carpet", new Vector3( (x0 + x1) * 0.5f, floor + 0.02f, 0f ), new Vector3( x1 - x0, 0.04f, 2.2f ), solid: false );
		for ( var x = x0 + 2.5f; x < x1; x += 5f )
			foreach ( var s in new[] { -1f, 1f } )
				kit.Box( "glow_warm", new Vector3( x + 2.5f, floor + 2.6f, s * (half - 0.1f) ), new Vector3( 0.25f, 0.25f, 0.06f ), solid: false );
		// The stern end of the hallway is a wall with a doorway into the open hold.
		kit.Box( "panel", new Vector3( x0, floor + height * 0.5f, -1.6f - 0.2f ), new Vector3( 0.15f, height, 0.8f ) );
		kit.Box( "panel", new Vector3( x0, floor + height * 0.5f, 1.8f ), new Vector3( 0.15f, height, 0.8f ) );
	}

	// ---- Deck A: the dining saloon ----

	static void Dining( Kit kit, Random random, Func<float, float, float> R, Vector3 down, Basis hang )
	{
		const float x0 = -12f, x1 = 26f;
		var floor = FloorA + Slab * 0.5f;
		var ceiling = DeckMain - Slab * 0.5f;
		// White columns with gilt tops.
		foreach ( var x in new[] { -6f, 4f, 14f, 22f } )
			foreach ( var z in new[] { -4f, 4f } )
			{
				kit.Cylinder( "white", new Vector3( x, (floor + ceiling) * 0.5f, z ), 0.3f, ceiling - floor );
				kit.Cylinder( "brass", new Vector3( x, ceiling - 0.3f, z ), 0.45f, 0.4f, topRadius: 0.32f );
				kit.Solid( new Transform3D( Basis.Identity, new Vector3( x, (floor + ceiling) * 0.5f, z ) ), new Vector3( 0.6f, ceiling - floor, 0.6f ) );
			}
		// Chandeliers hang straight down (the world's down) from the ceiling: a chain, a brass crown, glowing drops.
		foreach ( var x in new[] { -2f, 9f, 18.5f } )
		{
			var top = new Vector3( x, ceiling, 0f );
			var chain = 1.6f;
			kit.Cylinder( "brass", top + down * chain * 0.5f, 0.04f, chain, hang, 6 );
			var hub = top + down * chain;
			kit.Sphere( "glow_warm", hub, 0.32f, 12 );
			kit.Cylinder( "brass", hub, 0.9f, 0.08f, hang, 16, 0.9f );
			for ( var i = 0; i < 10; i++ )
			{
				var a = i / 10f * MathF.Tau;
				var arm = hang * new Vector3( MathF.Cos( a ) * 0.85f, 0f, MathF.Sin( a ) * 0.85f );
				kit.Sphere( "glow_warm", hub + arm + down * 0.12f, 0.08f, 6 );
				kit.Sphere( "glass", hub + arm + down * 0.35f, 0.06f, 6 );
			}
		}
		// Round tables laid for dinner, chairs round them; some knocked over and slid to the low side (+Z).
		var tables = new List<Vector3>();
		foreach ( var x in new[] { -9f, -1.5f, 6.5f, 11f, 17f, 24f } )
			foreach ( var z in new[] { -5.6f, -2f, 2f, 5.6f } )
				if ( !(MathF.Abs( z ) < 3f && x > 23f) ) tables.Add( new Vector3( x + R( -0.4f, 0.4f ), floor, z ) );
		// Chairs stay inside the saloon's walls and clear of the tables (the cloth's radius is 0.82) and the columns.
		var inner = 8f - Wall - 0.05f;
		var chairs = new ChairScatter( kit, new Random( 1913 ), floor, x0 + 0.15f, x1 - 0.15f, -inner, inner );
		foreach ( var t in tables ) chairs.Block( t, 0.84f );
		foreach ( var x in new[] { -6f, 4f, 14f, 22f } )
			foreach ( var z in new[] { -4f, 4f } )
				chairs.Block( new Vector3( x, floor, z ), 0.45f );
		var seats = new List<(Vector3 Table, float Angle)>();
		foreach ( var t in tables )
		{
			kit.Cylinder( "dark_wood", t + new Vector3( 0f, 0.38f, 0f ), 0.1f, 0.76f );
			kit.Cylinder( "cloth", t + new Vector3( 0f, 0.6f, 0f ), 0.82f, 0.36f, topRadius: 0.78f );
			kit.Solid( new Transform3D( Basis.Identity, t + new Vector3( 0f, 0.4f, 0f ) ), new Vector3( 1.3f, 0.8f, 1.3f ) );
			for ( var i = 0; i < 4; i++ )
			{
				var a = i / 4f * MathF.Tau + 0.4f;
				var at = t + new Vector3( MathF.Cos( a ) * 0.55f, 0.79f, MathF.Sin( a ) * 0.55f );
				if ( random.NextDouble() < 0.75 ) Setting( kit, random, R, at, a );
				seats.Add( (t, a) );
			}
		}
		// About a third of the chairs still stand at their places (first, so they get their spots), a few more stand
		// shoved away and turned, and the rest have fallen over, mostly slid toward the low side.
		var standing = new List<(Vector3 Table, float Angle)>();
		var shoved = new List<(Vector3 Table, float Angle)>();
		var fallen = new List<(Vector3 Table, float Angle)>();
		foreach ( var seat in seats )
		{
			var roll = chairs.R( 0f, 1f );
			(roll < 0.3f ? standing : roll < 0.45f ? shoved : fallen).Add( seat );
		}
		foreach ( var (t, a) in standing )
		{
			var outward = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			chairs.Place( () => t + outward * chairs.R( 1.18f, 1.3f ), 3, BackToward( outward.X, outward.Z ) + chairs.R( -0.25f, 0.25f ), ChairPose.Upright );
		}
		foreach ( var (t, a) in shoved )
		{
			chairs.Place( () =>
			{
				var b = a + chairs.R( -0.5f, 0.5f );
				return t + new Vector3( MathF.Cos( b ), 0f, MathF.Sin( b ) ) * chairs.R( 1.5f, 2.4f );
			}, 8, chairs.R( 0f, MathF.Tau ), ChairPose.Upright );
		}
		foreach ( var (t, a) in fallen )
		{
			var from = t + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * 1.3f;
			chairs.Place( () => from + new Vector3( chairs.R( -1.4f, 1.4f ), 0f, chairs.R( -0.4f, 2.6f ) ), 10, chairs.R( 0f, MathF.Tau ), chairs.Fallen() );
		}
		// More chairs slid down against the low side, then plates and cutlery spilled round them (not under them).
		for ( var i = 0; i < 12; i++ )
			chairs.Place( () => new Vector3( chairs.R( x0 + 1f, x1 - 1f ), floor, chairs.R( 5.2f, inner ) ), 10, chairs.R( 0f, MathF.Tau ), chairs.Fallen() );
		for ( var i = 0; i < 60; i++ )
		{
			var at = new Vector3( R( x0 + 1f, x1 - 1f ), floor + 0.02f, R( 5.4f, 7.4f ) );
			var facing = R( 0f, MathF.Tau );
			if ( chairs.Clear( at, 0.3f ) ) Setting( kit, random, R, at, facing, spilled: true );
		}
		// Panelled walls at the saloon's ends: the kitchen side has two doorways.
		var height = ceiling - floor;
		kit.Box( "panel", new Vector3( x0, floor + height * 0.5f, 0f ), new Vector3( 0.2f, height, 2.4f ) );
		kit.Box( "panel", new Vector3( x0, floor + height * 0.5f, 6.2f ), new Vector3( 0.2f, height, 3f ) );
		kit.Box( "panel", new Vector3( x0, floor + height * 0.5f, -6.2f ), new Vector3( 0.2f, height, 3f ) );
		foreach ( var s in new[] { -1f, 1f } )
			kit.Box( "panel", new Vector3( x0, ceiling - 0.9f, s * 3.2f ), new Vector3( 0.2f, 1.8f, 1.8f ) );
	}

	/// <summary> A plate with a knife and fork beside it (and on the floor, scattered and tipped). </summary>
	static void Setting( Kit kit, Random random, Func<float, float, float> R, Vector3 at, float facing, bool spilled = false )
	{
		var turn = new Basis( Vector3.Up, facing + (spilled ? R( 0f, MathF.Tau ) : 0f) );
		if ( !spilled || random.NextDouble() < 0.6 )
			kit.Cylinder( "china", at, 0.13f, 0.015f, spilled ? turn * new Basis( Vector3.Right, R( -0.4f, 0.4f ) ) : turn, 12 );
		var side = turn * Vector3.Right;
		kit.Box( "silver", at + side * 0.19f + new Vector3( 0f, 0.004f, 0f ), new Vector3( 0.02f, 0.008f, 0.19f ), spilled ? new Basis( Vector3.Up, R( 0f, MathF.Tau ) ) : turn, solid: false );
		kit.Box( "silver", at - side * 0.19f + new Vector3( 0f, 0.004f, 0f ), new Vector3( 0.025f, 0.008f, 0.18f ), spilled ? new Basis( Vector3.Up, R( 0f, MathF.Tau ) ) : turn, solid: false );
		if ( !spilled && random.NextDouble() < 0.5 ) kit.Cylinder( "glass", at + turn * new Vector3( 0.12f, 0.08f, -0.14f ), 0.035f, 0.14f, sides: 8 );
	}

	// ---- Chairs: standing, shoved about or fallen over, always resting on the floor ----

	enum ChairPose { Upright, OnBack, OnFront, OnSide }

	/// <summary> A chair's boxes in its own space: the origin on the floor under the seat's middle, the back toward +Z. </summary>
	static readonly (string Key, Vector3 Centre, Vector3 Size)[] ChairParts =
	{
		("dark_wood", new Vector3( 0f, 0.45f, 0f ), new Vector3( 0.46f, 0.06f, 0.46f )),     // seat
		("carpet", new Vector3( 0f, 0.49f, 0f ), new Vector3( 0.42f, 0.04f, 0.42f )),        // cushion
		("dark_wood", new Vector3( 0f, 0.75f, 0.22f ), new Vector3( 0.46f, 0.6f, 0.05f )),    // back
		("dark_wood", new Vector3( -0.19f, 0.22f, -0.19f ), new Vector3( 0.04f, 0.44f, 0.04f )),
		("dark_wood", new Vector3( 0.19f, 0.22f, -0.19f ), new Vector3( 0.04f, 0.44f, 0.04f )),
		("dark_wood", new Vector3( -0.19f, 0.22f, 0.19f ), new Vector3( 0.04f, 0.44f, 0.04f )),
		("dark_wood", new Vector3( 0.19f, 0.22f, 0.19f ), new Vector3( 0.04f, 0.44f, 0.04f )),
	};

	/// <summary> The yaw that turns a chair's back toward a direction on the floor. </summary>
	static float BackToward( float x, float z ) => MathF.Atan2( x, z );

	/// <summary>
	/// Puts chairs in a room without overlaps: each one is posed, dropped so its lowest corner sits exactly on the
	/// floor, and only placed where its footprint (a circle round it) stays inside the room and clear of everything
	/// blocked so far (tables, columns, other chairs).
	/// </summary>
	sealed class ChairScatter
	{
		readonly Kit kit;
		readonly Random random;
		readonly float floor, minX, maxX, minZ, maxZ;
		readonly List<(Vector2 At, float Radius)> taken = new();

		public ChairScatter( Kit kit, Random random, float floor, float minX, float maxX, float minZ, float maxZ )
		{
			this.kit = kit;
			this.random = random;
			this.floor = floor;
			this.minX = minX;
			this.maxX = maxX;
			this.minZ = minZ;
			this.maxZ = maxZ;
		}

		public float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		public bool Chance( double p ) => random.NextDouble() < p;

		/// <summary> Something chairs must keep clear of, as a circle on the floor. </summary>
		public void Block( Vector3 at, float radius ) => taken.Add( (new Vector2( at.X, at.Z ), radius) );

		/// <summary> Is a circle on the floor inside the room and clear of everything placed? </summary>
		public bool Free( Vector3 at, float radius ) =>
			at.X - radius >= minX && at.X + radius <= maxX && at.Z - radius >= minZ && at.Z + radius <= maxZ && Clear( at, radius );

		/// <summary> Is a circle on the floor clear of everything placed (the room's edges aside)? </summary>
		public bool Clear( Vector3 at, float radius )
		{
			var p = new Vector2( at.X, at.Z );
			foreach ( var (other, r) in taken )
				if ( p.DistanceTo( other ) < radius + r ) return false;
			return true;
		}

		/// <summary> A fallen pose, picked at random: mostly on the back or a side, sometimes face down. </summary>
		public ChairPose Fallen()
		{
			var roll = random.NextDouble();
			return roll < 0.45 ? ChairPose.OnBack : roll < 0.85 ? ChairPose.OnSide : ChairPose.OnFront;
		}

		/// <summary>
		/// Tries the spots `spot` gives (up to `tries`) and places the chair at the first clear one, its footprint
		/// centred there. False if none was clear (then nothing is drawn).
		/// </summary>
		public bool Place( Func<Vector3> spot, int tries, float facing, ChairPose pose )
		{
			var basis = pose switch
			{
				ChairPose.OnBack => new Basis( Vector3.Right, MathF.PI * 0.5f ),
				ChairPose.OnFront => new Basis( Vector3.Right, -MathF.PI * 0.5f ),
				ChairPose.OnSide => new Basis( Vector3.Back, Chance( 0.5 ) ? MathF.PI * 0.5f : -MathF.PI * 0.5f ),
				_ => Basis.Identity,
			};
			// The posed chair's lowest point and its footprint on the floor.
			float lowY = float.MaxValue, lowX = float.MaxValue, highX = float.MinValue, lowZ = float.MaxValue, highZ = float.MinValue;
			foreach ( var (_, centre, size) in ChairParts )
				for ( var c = 0; c < 8; c++ )
				{
					var corner = centre + new Vector3( (c & 1) == 0 ? -0.5f : 0.5f, (c & 2) == 0 ? -0.5f : 0.5f, (c & 4) == 0 ? -0.5f : 0.5f ) * size;
					var p = basis * corner;
					lowY = MathF.Min( lowY, p.Y );
					lowX = MathF.Min( lowX, p.X ); highX = MathF.Max( highX, p.X );
					lowZ = MathF.Min( lowZ, p.Z ); highZ = MathF.Max( highZ, p.Z );
				}
			var middle = new Vector3( (lowX + highX) * 0.5f, 0f, (lowZ + highZ) * 0.5f );
			var radius = 0.5f * new Vector2( highX - lowX, highZ - lowZ ).Length();
			for ( var i = 0; i < tries; i++ )
			{
				var at = spot() with { Y = floor };
				if ( !Free( at, radius ) ) continue;
				Block( at, radius );
				var yaw = new Basis( Vector3.Up, facing );
				var turn = yaw * basis;
				foreach ( var (key, centre, size) in ChairParts )
					kit.Box( key, at + new Vector3( 0f, -lowY, 0f ) + yaw * (basis * centre - middle), size, turn, solid: false );
				return true;
			}
			return false;
		}
	}

	// ---- Deck A: the kitchen (behind the saloon, toward the stern) ----

	static void Kitchen( Kit kit, Random random, Func<float, float, float> R, Vector3 down, Basis hang )
	{
		const float x0 = -27f, x1 = -12f;
		var floor = FloorA + Slab * 0.5f;
		var ceiling = DeckMain - Slab * 0.5f;
		// Steel counters along both sides, stoves with burners, an island in the middle.
		foreach ( var s in new[] { -1f, 1f } )
		{
			var z = s * (8f - Wall - 0.45f);
			kit.Box( "steel", new Vector3( (x0 + x1) * 0.5f, floor + 0.45f, z ), new Vector3( x1 - x0 - 1f, 0.9f, 0.8f ) );
			for ( var x = x0 + 2f; x < x1 - 1f; x += 3.2f )
			{
				kit.Box( "iron", new Vector3( x, floor + 0.47f, z ), new Vector3( 1.4f, 0.96f, 0.82f ), solid: false );
				for ( var b = 0; b < 4; b++ )
					kit.Cylinder( "steel", new Vector3( x + (b % 2 - 0.5f) * 0.6f, floor + 0.96f, z + (b / 2 - 0.5f) * 0.38f ), 0.13f, 0.03f, sides: 10 );
				if ( random.NextDouble() < 0.6 ) kit.Cylinder( "steel", new Vector3( x + 0.3f, floor + 1.12f, z ), 0.2f, 0.3f, sides: 12 );   // a stock pot left on
			}
			// Shelves of stacked plates above the counters.
			kit.Box( "steel", new Vector3( (x0 + x1) * 0.5f, floor + 2.1f, s * (8f - Wall - 0.25f) ), new Vector3( x1 - x0 - 1f, 0.05f, 0.4f ), solid: false );
			for ( var x = x0 + 1f; x < x1 - 1f; x += 0.9f )
				if ( random.NextDouble() < 0.7 ) kit.Cylinder( "china", new Vector3( x, floor + 2.2f, s * (8f - Wall - 0.25f) ), 0.13f, R( 0.08f, 0.2f ), sides: 12 );
		}
		kit.Box( "steel", new Vector3( (x0 + x1) * 0.5f + 1f, floor + 0.45f, 0f ), new Vector3( 7f, 0.9f, 1.4f ) );
		// A rack over the island with pots and pans hanging straight down.
		var rackY = ceiling - 0.9f;
		kit.Box( "iron", new Vector3( (x0 + x1) * 0.5f + 1f, rackY, 0f ), new Vector3( 6.5f, 0.06f, 0.06f ), solid: false );
		for ( var x = x0 + 5f; x < x0 + 11f; x += 0.8f )
		{
			var top = new Vector3( x, rackY, 0f );
			kit.Cylinder( "iron", top + down * 0.2f, 0.015f, 0.4f, hang, 4 );
			kit.Cylinder( random.NextDouble() < 0.5 ? "steel" : "brass", top + down * 0.55f, R( 0.12f, 0.2f ), R( 0.1f, 0.25f ), hang, 12 );
		}
		// Pans, pots and plates spilled down to the low side, and a big cauldron on its side.
		for ( var i = 0; i < 30; i++ )
		{
			var at = new Vector3( R( x0 + 1f, x1 - 1f ), floor + 0.05f, R( 3f, 6.6f ) );
			if ( random.NextDouble() < 0.5 ) kit.Cylinder( "china", at, 0.13f, 0.015f, new Basis( Vector3.Right, R( -0.3f, 0.3f ) ), 12 );
			else kit.Cylinder( "steel", at + new Vector3( 0f, 0.08f, 0f ), R( 0.12f, 0.22f ), R( 0.08f, 0.18f ), new Basis( Vector3.Right, R( 0f, 1.6f ) ) * new Basis( Vector3.Up, R( 0f, 3f ) ), 12 );
		}
		kit.Cylinder( "iron", new Vector3( x0 + 3f, floor + 0.5f, 5f ), 0.55f, 0.9f, new Basis( Vector3.Right, MathF.PI * 0.5f ) * new Basis( Vector3.Back, 0.3f ), 16 );
		// A cold room in the corner.
		kit.Box( "white", new Vector3( x0 + 1.6f, floor + 1.4f, -5.2f ), new Vector3( 2.6f, 2.8f, 2.2f ) );
	}

	// ---- Topside: deckhouse, funnels, masts, railings ----

	static void Topside( Kit kit, Random random, Func<float, float, float> R )
	{
		var deck = DeckMain + Slab * 0.5f;
		const float hx0 = -24f, hx1 = 22f, hz = 5.5f, hh = 3.4f;
		// The white deckhouse: walls with windows and doorways (some windows broken), a flat roof.
		foreach ( var s in new[] { -1f, 1f } )
			for ( var x = hx0; x < hx1 - 0.1f; x += 3f )
			{
				var door = ((int)((x - hx0) / 3f)) % 4 == 1;
				if ( door )
				{
					kit.Box( "white", new Vector3( x + 0.4f, deck + hh * 0.5f, s * hz ), new Vector3( 0.8f, hh, 0.25f ) );
					kit.Box( "white", new Vector3( x + 2.6f, deck + hh * 0.5f, s * hz ), new Vector3( 0.8f, hh, 0.25f ) );
					kit.Box( "white", new Vector3( x + 1.5f, deck + 2.5f + (hh - 2.5f) * 0.5f, s * hz ), new Vector3( 1.4f, hh - 2.5f, 0.25f ) );
					continue;
				}
				kit.Box( "white", new Vector3( x + 1.5f, deck + 0.5f, s * hz ), new Vector3( 3f, 1f, 0.25f ) );
				kit.Box( "white", new Vector3( x + 1.5f, deck + 2.9f, s * hz ), new Vector3( 3f, 1f, 0.25f ) );
				kit.Box( "white", new Vector3( x + 0.3f, deck + 1.7f, s * hz ), new Vector3( 0.6f, 1.4f, 0.25f ) );
				if ( random.NextDouble() < 0.6 ) kit.Box( "glass", new Vector3( x + 1.8f, deck + 1.7f, s * hz ), new Vector3( 2.4f, 1.4f, 0.05f ) );
			}
		foreach ( var x in new[] { hx0, hx1 } )
		{
			kit.Box( "white", new Vector3( x, deck + hh * 0.5f, -3.5f ), new Vector3( 0.25f, hh, 4f ) );
			kit.Box( "white", new Vector3( x, deck + hh * 0.5f, 3.5f ), new Vector3( 0.25f, hh, 4f ) );
			kit.Box( "white", new Vector3( x, deck + 2.6f + (hh - 2.6f) * 0.5f, 0f ), new Vector3( 0.25f, hh - 2.6f, 3f ) );
		}
		kit.Box( "white", new Vector3( (hx0 + hx1) * 0.5f, deck + hh + 0.15f, 0f ), new Vector3( hx1 - hx0 + 0.5f, 0.3f, 2f * hz + 0.5f ) );
		// Four buff funnels with black tops, raked back.
		var roof = deck + hh + 0.3f;
		var rake = new Basis( Vector3.Back, MathX.Rad( 7f ) );
		foreach ( var x in new[] { 16f, 5f, -6f, -17f } )
		{
			var baseAt = new Vector3( x, roof, 0f );
			kit.Add( "buff", new CylinderMesh { TopRadius = 1.6f, BottomRadius = 1.7f, Height = 8f, RadialSegments = 18 }, new Transform3D( rake, baseAt + rake * new Vector3( 0f, 4f, 0f ) ) );
			kit.Add( "hull_black", new CylinderMesh { TopRadius = 1.62f, BottomRadius = 1.62f, Height = 1.8f, RadialSegments = 18 }, new Transform3D( rake, baseAt + rake * new Vector3( 0f, 8.9f, 0f ) ) );
			kit.Solid( new Transform3D( rake, baseAt + rake * new Vector3( 0f, 4.9f, 0f ) ), new Vector3( 3f, 9.8f, 3f ) );
		}
		// Masts fore and aft.
		kit.Cylinder( "iron", new Vector3( 40f, deck + 7f, 0f ), 0.22f, 14f, rake, 8 );
		kit.Cylinder( "iron", new Vector3( -44f, deck + 6f, 0f ), 0.2f, 12f, rake, 8 );
		// Railings along the deck edges (the rail is solid: it catches you on the low side).
		const float seg = 2.5f;
		for ( var x = -HalfLength + 2f; x < HalfLength - 2f; x += seg )
			foreach ( var s in new[] { -1f, 1f } )
			{
				var z = s * (HalfBeam( x ) - 0.25f);
				kit.Cylinder( "white", new Vector3( x, deck + 0.5f, z ), 0.04f, 1f, sides: 6 );
				kit.Box( "white", new Vector3( x + seg * 0.5f, deck + 1f, (z + s * (HalfBeam( x + seg ) - 0.25f)) * 0.5f ), new Vector3( seg, 0.06f, 0.06f ), solid: false );
				kit.Solid( new Transform3D( Basis.Identity, new Vector3( x + seg * 0.5f, deck + 0.55f, (z + s * (HalfBeam( x + seg ) - 0.25f)) * 0.5f ) ), new Vector3( seg, 1.1f, 0.12f ) );
			}
	}

	static void Lights()
	{
		void Light( Vector3 at, Color c, float energy, float range )
		{
			var l = new OmniLight3D { LightColor = c, LightEnergy = energy, OmniRange = range, ShadowEnabled = false };
			Root.AddChild( l );
			l.Position = at;
		}
		var warm = new Color( 1f, 0.8f, 0.5f );
		foreach ( var x in new[] { -2f, 9f, 18.5f } ) Light( new Vector3( x, DeckMain - 2.2f, 0f ), warm, 1.6f, 11f );
		Light( new Vector3( -19f, DeckMain - 1.5f, 0f ), new Color( 0.75f, 0.9f, 1f ), 1.2f, 10f );
		foreach ( var x in new[] { -24f, -6f, 12f } ) Light( new Vector3( x, FloorA - 1.2f, 0f ), new Color( 1f, 0.78f, 0.5f ), 1.1f, 10f );
		Light( new Vector3( 4f, FloorA - 1.4f, 0f ), new Color( 0.4f, 1f, 0.45f ), 0.8f, 8f );
		Light( new Vector3( 32f, FloorA, 0f ), warm, 1f, 9f );
	}

	/// <summary> A point inside the ship (ship space) in the world. </summary>
	public static Vector3 World( Vector3 local ) => Frame * local;

	/// <summary> Places worth visiting inside (ship space): the hallway, the dining saloon, the kitchen, the breach. </summary>
	public static readonly (string Name, Vector3 Local)[] Rooms =
	{
		("breach", new Vector3( 0f, FloorB + 0.6f, 5f )),
		("hallway", new Vector3( -10f, FloorB + 0.6f, 0f )),
		("cabin", new Vector3( -6.5f, FloorB + 0.6f, -4f )),
		("dining", new Vector3( 4f, FloorA + 0.6f, 0f )),
		("kitchen", new Vector3( -19f, FloorA + 0.6f, 2.4f )),
		("deck", new Vector3( 30f, DeckMain + 0.6f, 0f )),
	};
}
