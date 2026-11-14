using Godot;

/// <summary>
/// Area 3 (after wave 20): Atlantis under a giant glass globe, like a snow globe with no snow. Plato's city of rings:
/// a raised central island with the temple of Poseidon, then rings of land and glowing canals one inside the other,
/// crossed by bridges and grand stairways, with eight avenues running out from the middle. The city is white marble
/// and orichalcum gold with sea-glass domes, and the game's alien side runs through it: obsidian ziggurats with purple
/// seams, neon green crystal growths, glowing canals, and a giant green crystal (the city's heart) turning over the
/// temple. The globe stands on a black and gold base; the sea floor shows through the glass outside.
/// Built from simple shapes merged into a few meshes (MeshKit) with box and cylinder colliders. Metres; the map's
/// middle is the city's middle. Angles go round from +X toward +Z: avenue 6 (270 degrees) runs south.
/// </summary>
public static class Atlantis
{
	// ---- Layout ----
	public const float GlobeRadius = 100f;
	public const float GlobeCentreY = 30f;
	/// <summary> The globe's base: a black and gold ring wall the glass stands in. </summary>
	public const float BaseInner = 91f, BaseOuter = 101f, BaseHeight = 7f;
	public const float IslandRadius = 18f, IslandHeight = 3f;
	/// <summary> The top of the land rings (the canals are the sea floor, at 0). </summary>
	public const float Land = 1f;
	/// <summary> The floor of the dry canals, a storey below the streets (the old sea floor round the city is at 0). </summary>
	public const float CanalFloorY = -3f;
	public static readonly (float Inner, float Outer)[] Canals = { (18f, 26f), (46f, 54f) };
	public static readonly (float Inner, float Outer)[] Rings = { (26f, 46f), (54f, BaseInner) };
	public const int Avenues = 8;
	public const float AvenueWidth = 8f;
	/// <summary> Where the teleporter brings you: the south avenue, facing the temple. </summary>
	public static readonly Vector3 Arrival = new( 0f, Land, -68f );
	/// <summary> The open plaza on the west avenue where the Kraken comes up. </summary>
	public static readonly Vector3 KrakenCentre = new( -68f, Land, 0f );
	const float ArrivalPlaza = 18f, KrakenPlaza = 17f;
	/// <summary> The street round the outer ring where enemies come in (between the building rows). </summary>
	public const float SpawnRing = 65f;

	/// <summary> Places worth standing on (the --atlantis test walks to each). </summary>
	public static readonly (string Name, Vector3 At)[] Places =
	{
		("arrival", Arrival),
		("island", new Vector3( 5f, IslandHeight, -10f )),
		("ring1 street", Polar( 36f, 22.5f, Land )),
		("canal", Polar( 22f, 60f, CanalFloorY )),
		("outer canal", Polar( 50f, 112.5f, CanalFloorY )),
		("bridge east", Polar( 50f, 0f, Land )),
		("bridge north", Polar( 50f, 90f, Land )),
		("kraken plaza", KrakenCentre),
		("ring2 street", Polar( SpawnRing, 112.5f, Land )),
		("outer street", Polar( 77.7f, 202.5f, Land )),
	};

	public static Node3D Root { get; private set; }

	/// <summary> A point at a radius and an angle (degrees) at a height. </summary>
	public static Vector3 Polar( float radius, float degrees, float y ) =>
		new( MathF.Cos( MathX.Rad( degrees ) ) * radius, y, MathF.Sin( MathX.Rad( degrees ) ) * radius );

	/// <summary> The walking surface's height: the island top, a land ring, or the dry canals' floor. </summary>
	public static float SurfaceAt( Vector3 p )
	{
		var r = p.Flat().Length();
		if ( r < IslandRadius ) return IslandHeight;
		foreach ( var (inner, outer) in Rings ) if ( r >= inner && r < outer ) return Land;
		return r < BaseInner ? CanalFloorY : 0f;
	}

	/// <summary> Inside the globe's base (with a margin), where the city replaces the sea floor's own things. </summary>
	public static bool Inside( Vector3 p, float margin ) => p.Flat().Length() < BaseOuter + margin;

	// ---- Materials ----

	static StandardMaterial3D Mat( Color c, float rough = 0.6f, float metal = 0f ) =>
		new() { AlbedoColor = c, Roughness = rough, Metallic = metal, RimEnabled = true, Rim = 0.2f, RimTint = 0.5f };
	static StandardMaterial3D Shine( Color c, float glow )
	{
		var m = Mat( c, 0.25f, 0.8f );
		m.EmissionEnabled = true;
		m.Emission = c;
		m.EmissionEnergyMultiplier = glow;
		return m;
	}
	static StandardMaterial3D Glow( Color c, float energy )
	{
		var m = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy, Roughness = 0.3f };
		m.SetMeta( "no_caustics", true );
		return m;
	}
	static StandardMaterial3D Crystal( Color c )
	{
		var m = new StandardMaterial3D
		{
			AlbedoColor = c with { A = 0.78f }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f, Metallic = 0.2f,
			EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 1.4f, RimEnabled = true, Rim = 0.6f,
		};
		m.SetMeta( "no_caustics", true );
		return m;
	}

	static readonly Color GreenGlow = new( 0.35f, 1f, 0.3f );
	static readonly Color PurpleGlow = new( 0.66f, 0.22f, 1f );
	static readonly Color TealGlow = new( 0.2f, 1f, 0.85f );

	static readonly Dictionary<string, Material> Materials = new()
	{
		["marble"] = Mat( new Color( 0.94f, 0.86f, 0.72f ), 0.5f ),       // warm, honey-veined
		["marble_dark"] = Mat( new Color( 0.56f, 0.45f, 0.38f ), 0.6f ),
		["gold"] = Shine( new Color( 0.98f, 0.74f, 0.3f ), 0.12f ),   // orichalcum, a little lit so it reads in the murk
		["gold_bright"] = Shine( new Color( 1f, 0.8f, 0.36f ), 0.4f ),   // the temple's
		["roof_coral"] = Mat( new Color( 0.86f, 0.4f, 0.34f ), 0.5f ),
		["roof_jade"] = Mat( new Color( 0.22f, 0.62f, 0.42f ), 0.4f, 0.2f ),
		["terracotta"] = Mat( new Color( 0.72f, 0.36f, 0.2f ), 0.7f ),
		["vine"] = Mat( new Color( 0.1f, 0.3f, 0.1f ), 0.8f ),
		["vine_purple"] = Mat( new Color( 0.28f, 0.08f, 0.38f ), 0.8f ),
		["leaf"] = Mat( new Color( 0.24f, 0.62f, 0.16f ), 0.7f ),
		["coral_pink"] = Mat( new Color( 0.95f, 0.42f, 0.6f ), 0.6f ),
		["coral_orange"] = Mat( new Color( 1f, 0.55f, 0.22f ), 0.6f ),
		["banner_purple"] = Glow( new Color( 0.5f, 0.14f, 0.78f ), 0.45f ),
		["banner_green"] = Glow( new Color( 0.2f, 0.7f, 0.22f ), 0.45f ),
		["water"] = Glow( new Color( 0.3f, 0.95f, 0.9f ), 1.2f ),
		["rock"] = Mat( new Color( 0.45f, 0.42f, 0.38f ), 0.9f ),
		["rock_dark"] = Mat( new Color( 0.24f, 0.22f, 0.24f ), 0.9f ),
		["teal"] = Mat( new Color( 0.16f, 0.55f, 0.56f ), 0.3f, 0.3f ),    // sea-glass domes
		["obsidian"] = Mat( new Color( 0.05f, 0.05f, 0.07f ), 0.25f, 0.2f ),
		["purple"] = Mat( new Color( 0.34f, 0.1f, 0.52f ), 0.4f ),
		["glow_green"] = Glow( GreenGlow, 2.6f ),
		["glow_purple"] = Glow( PurpleGlow, 2.4f ),
		["glow_teal"] = Glow( TealGlow, 2f ),
		["crystal"] = Crystal( GreenGlow ),
		["crystal_purple"] = Crystal( PurpleGlow ),
	};
	static readonly string[] NoShadow = { "glow_green", "glow_purple", "glow_teal", "crystal", "crystal_purple", "water" };

	/// <summary> Each district's roof colour (domes and tower cones), going round the eight sectors. </summary>
	static readonly string[] DistrictRoofs = { "teal", "roof_coral", "purple", "roof_jade", "teal", "gold", "roof_coral", "roof_jade" };
	static string roof = "teal";

	// Materials for the rings (their own meshes, not the kit's).
	static readonly StandardMaterial3D Paving = Mat( new Color( 0.36f, 0.3f, 0.27f ), 0.75f );   // dark warm stone
	static readonly StandardMaterial3D CanalFloor = CanalMaterial();
	static readonly StandardMaterial3D BaseStone = Mat( new Color( 0.05f, 0.05f, 0.07f ), 0.3f, 0.2f );

	/// <summary> The dry canals' floor: pale sea sand. </summary>
	static StandardMaterial3D CanalMaterial() => Mat( new Color( 0.62f, 0.54f, 0.42f ), 0.95f );

	/// <summary>
	/// The glass: clear where you look straight through it, bright and sea-green at the edges where you see it side-on
	/// (a fresnel rim), with faint streaks of light running down it so the dome reads as a surface.
	/// </summary>
	static readonly Shader GlassShader = new()
	{
		Code = @"shader_type spatial;
render_mode cull_disabled, depth_draw_never, specular_schlick_ggx;
uniform vec3 tint = vec3(0.45, 0.95, 1.0);
varying vec3 local_pos;
void vertex() { local_pos = VERTEX; }
void fragment() {
	float f = pow(1.0 - abs(dot(NORMAL, VIEW)), 3.0);
	float streak = smoothstep(0.92, 1.0, sin(atan(local_pos.z, local_pos.x) * 48.0 + local_pos.y * 0.04)) * 0.35;
	ALBEDO = tint;
	ROUGHNESS = 0.03;
	METALLIC = 0.2;
	SPECULAR = 1.0;
	EMISSION = tint * (0.03 + f * 0.85 + streak * 0.12);
	ALPHA = clamp(0.04 + f * 0.6 + streak * 0.05, 0.0, 0.85);
}",
	};

	static Material GlassMaterial()
	{
		var m = new ShaderMaterial { Shader = GlassShader };
		m.SetMeta( "no_caustics", true );
		return m;
	}

	// ---- Building ----

	public static Node3D Build( Node3D level )
	{
		if ( Root is not null && GodotObject.IsInstanceValid( Root ) ) { Root.GetParent()?.RemoveChild( Root ); Root.Free(); }
		Root = new Node3D { Name = "Atlantis" };
		level.AddChild( Root );
		var kit = new MeshKit( Root, Materials, "Atlantis Body", NoShadow );
		var random = new Random( 1620 );
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);

		Ground( kit );
		DryCanals( kit, random, R );
		Island( kit );
		Crossings( kit );
		AvenueLamps( kit );
		Districts( kit, random, R );
		Arches( kit );
		StairStatues( kit );
		GlobeBase( kit );
		kit.Commit();

		var glass = new MeshInstance3D
		{
			Name = "Globe Glass",
			Mesh = new SphereMesh { Radius = GlobeRadius, Height = GlobeRadius * 2f, RadialSegments = 96, Rings = 48 },
			MaterialOverride = GlassMaterial(),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		Root.AddChild( glass );
		glass.Position = new Vector3( 0f, GlobeCentreY, 0f );

		var heart = new AtlantisHeart { Name = "Heart of Atlantis" };
		Root.AddChild( heart );
		heart.Position = new Vector3( 0f, IslandHeight + 21f, 2f );
		// Schools of glowing fish circling through the streets and over the roofs.
		for ( var i = 0; i < 10; i++ )
		{
			var school = new AtlantisFish( i ) { Name = $"Fish School {i}" };
			Root.AddChild( school );
		}
		Lights();
		GD.Print( $"[atlantis] {kit.Pieces} pieces, {kit.Solids} colliders" );
		return Root;
	}

	/// <summary>
	/// The glass as a solid shell, so nothing gets out (the jetpack, floating pieces). Added after the navmesh is baked:
	/// the curved glass is no ground to walk on.
	/// </summary>
	public static void AddGlobeCollider()
	{
		if ( Root is null || !GodotObject.IsInstanceValid( Root ) ) return;
		var body = new StaticBody3D { Name = "Globe Glass Solid", CollisionLayer = Layers.World, CollisionMask = 0 };
		Root.AddChild( body );
		var sphere = new SphereMesh { Radius = GlobeRadius, Height = GlobeRadius * 2f, RadialSegments = 48, Rings = 24 };
		// Seen from inside the faces are back faces: they must still collide.
		var shape = new ConcavePolygonShape3D { Data = sphere.GetFaces(), BackfaceCollision = true };
		body.AddChild( new CollisionShape3D { Shape = shape, Position = new Vector3( 0f, GlobeCentreY, 0f ) } );
	}

	// ---- The ground: land rings over the dry canals, a storey down ----

	static void Ground( MeshKit kit )
	{
		foreach ( var (inner, outer) in Canals )
			Ring( "Canal", CanalFloor, inner, outer, CanalFloorY + 0.02f, CanalFloorY + 0.03f, 96, walls: false );
		foreach ( var (inner, outer) in Rings )
		{
			Ring( "Land Ring", Paving, inner, outer, CanalFloorY, Land, 128 );
			RingSolid( kit, inner, outer, CanalFloorY, Land, 128 );
			// Glowing runes along the canal walls, below the street.
			if ( inner > IslandRadius + 1f ) Ring( "Canal Wall Light", Materials["glow_teal"], inner - 0.04f, inner, -0.6f, -0.45f, 128 );
			if ( outer < BaseInner ) Ring( "Canal Wall Light", Materials["glow_purple"], outer, outer + 0.04f, -0.6f, -0.45f, 128 );
			// Gold coping along both edges.
			Ring( "Coping", Materials["gold"], inner, inner + 0.35f, Land, Land + 0.06f, 128 );
			if ( outer < BaseInner ) Ring( "Coping", Materials["gold"], outer - 0.35f, outer, Land, Land + 0.06f, 128 );
			// Gold inlay bands set into the paving, a pair every few metres.
			for ( var r = inner + 4f; r < outer - 3f; r += 7f )
			{
				Ring( "Inlay", Materials["gold"], r, r + 0.25f, Land, Land + 0.012f, 128, walls: false );
				Ring( "Inlay", Materials["gold"], r + 0.55f, r + 0.65f, Land, Land + 0.012f, 128, walls: false );
			}
		}
		// Steps down into the canals: one in every stretch of canal between crossings.
		for ( var k = 0; k < 4; k++ )
		{
			var a = 45f + 90f * k;   // canal 1: between the island's four stairways
			Ramp( kit, Polar( Canals[0].Outer, a, Land ), Polar( Canals[0].Outer - 7f, a, CanalFloorY ), 3f, "marble" );
		}
		for ( var k = 0; k < Avenues; k++ )
		{
			var a = 22.5f + 45f * k;   // canal 2: between the bridges
			Ramp( kit, Polar( Canals[1].Inner, a, Land ), Polar( Canals[1].Inner + 7f, a, CanalFloorY ), 3f, "marble" );
		}
	}

	// ---- The island and the temple of Poseidon ----

	static void Island( MeshKit kit )
	{
		var islandMid = (IslandHeight + CanalFloorY) * 0.5f;
		var islandTall = IslandHeight - CanalFloorY;
		kit.Cylinder( "marble", new Vector3( 0f, islandMid, 0f ), IslandRadius, islandTall, sides: 64 );
		kit.Solid( new CylinderShape3D { Radius = IslandRadius, Height = islandTall }, new Transform3D( Basis.Identity, new Vector3( 0f, islandMid, 0f ) ) );
		kit.Cylinder( "gold", new Vector3( 0f, IslandHeight - 0.2f, 0f ), IslandRadius + 0.12f, 0.3f, sides: 64 );
		kit.Cylinder( "obsidian", new Vector3( 0f, CanalFloorY + 0.35f, 0f ), IslandRadius + 0.1f, 0.7f, sides: 64 );
		for ( var i = 0; i < 48; i++ )   // glowing runes round the island's foot
		{
			var a = i * 7.5f;
			var at = Polar( IslandRadius + 0.13f, a, 1.5f );
			kit.Box( "glow_green", at, new Vector3( 0.35f, 0.7f, 0.05f ), new Basis( Vector3.Up, MathF.PI * 0.5f - MathX.Rad( a ) ), solid: false );
		}

		Temple( kit, new Vector3( 0f, IslandHeight, 2f ) );
		Statue( kit, new Vector3( 0f, IslandHeight, -11.5f ) );
		foreach ( var (x, z) in new[] { (-13f, -9f), (13f, -9f), (-13f, 11f), (13f, 11f) } )
			Obelisk( kit, new Vector3( x, IslandHeight, z ), 7f );
	}

	/// <summary> A peristyle temple, long side east-west, its front to the south: columns, a dark cella, a gold roof. </summary>
	static void Temple( MeshKit kit, Vector3 at )
	{
		const float w = 22f, d = 13f, columns = 7f;
		kit.Box( "marble_dark", at + new Vector3( 0f, 0.2f, 0f ), new Vector3( w, 0.4f, d ) );
		var floor = at.Y + 0.4f;
		// The cella: dark stone with a glowing purple portal on its front.
		kit.Box( "marble_dark", new Vector3( at.X, floor + columns * 0.5f, at.Z + 0.8f ), new Vector3( 13f, columns, 6.5f ) );
		kit.Box( "glow_purple", new Vector3( at.X, floor + 2.3f, at.Z + 0.8f - 3.27f ), new Vector3( 2.6f, 4.6f, 0.1f ), solid: false );
		kit.Box( "gold_bright", new Vector3( at.X, floor + 4.75f, at.Z + 0.8f - 3.28f ), new Vector3( 3.2f, 0.35f, 0.1f ), solid: false );
		// The peristyle.
		void Column( float x, float z )
		{
			var c = new Vector3( at.X + x, floor, at.Z + z );
			kit.Box( "marble_dark", c + new Vector3( 0f, 0.2f, 0f ), new Vector3( 1.1f, 0.4f, 1.1f ), solid: false );
			kit.Cylinder( "marble", c + new Vector3( 0f, columns * 0.5f, 0f ), 0.45f, columns - 0.6f, sides: 12, topRadius: 0.38f );
			kit.Box( "gold_bright", c + new Vector3( 0f, columns - 0.15f, 0f ), new Vector3( 1.2f, 0.3f, 1.2f ), solid: false );
			kit.Cylinder( "glow_purple", c + new Vector3( 0f, 1.1f, 0f ), 0.47f, 0.12f, sides: 12 );
			kit.Solid( new Transform3D( Basis.Identity, c + new Vector3( 0f, columns * 0.5f, 0f ) ), new Vector3( 0.8f, columns, 0.8f ) );
		}
		for ( var i = 0; i < 8; i++ )
		{
			var x = -w * 0.5f + 1.2f + i * (w - 2.4f) / 7f;
			Column( x, -d * 0.5f + 1.1f );
			Column( x, d * 0.5f - 1.1f );
		}
		for ( var i = 1; i < 4; i++ )
		{
			var z = -d * 0.5f + 1.1f + i * (d - 2.2f) / 4f;
			Column( -w * 0.5f + 1.2f, z );
			Column( w * 0.5f - 1.2f, z );
		}
		// Entablature and a gold gable roof, a glowing green frieze between.
		var top = floor + columns;
		kit.Box( "gold_bright", new Vector3( at.X, top + 0.5f, at.Z ), new Vector3( w + 0.4f, 1f, d + 0.4f ) );
		kit.Box( "glow_green", new Vector3( at.X, top + 0.5f, at.Z - d * 0.5f - 0.22f ), new Vector3( w - 1f, 0.25f, 0.05f ), solid: false );
		kit.Box( "glow_green", new Vector3( at.X, top + 0.5f, at.Z + d * 0.5f + 0.22f ), new Vector3( w - 1f, 0.25f, 0.05f ), solid: false );
		var pitch = MathF.Atan2( 2.4f, d * 0.5f );
		var slope = (d * 0.5f + 0.4f) / MathF.Cos( pitch );
		foreach ( var s in new[] { -1f, 1f } )
			kit.Box( "gold_bright", new Vector3( at.X, top + 1f + 1.2f, at.Z + s * d * 0.25f ), new Vector3( w + 0.8f, 0.4f, slope ), new Basis( Vector3.Right, s * pitch ) );
		foreach ( var s in new[] { -1f, 1f } )   // the gable ends, filled
			kit.Box( "marble", new Vector3( at.X + s * (w * 0.5f + 0.05f), top + 1.9f, at.Z ), new Vector3( 0.3f, 1.6f, d * 0.6f ), solid: false );
		// Glowing trim: purple along the eaves and the ridge, green round the platform's edge, purple column bands.
		foreach ( var s in new[] { -1f, 1f } )
		{
			kit.Box( "glow_purple", new Vector3( at.X, top + 1.02f, at.Z + s * (d * 0.5f + 0.42f) ), new Vector3( w + 0.8f, 0.14f, 0.12f ), solid: false );
			kit.Box( "glow_green", new Vector3( at.X, at.Y + 0.42f, at.Z + s * (d * 0.5f + 0.02f) ), new Vector3( w, 0.08f, 0.06f ), solid: false );
			kit.Box( "glow_green", new Vector3( at.X + s * (w * 0.5f + 0.02f), at.Y + 0.42f, at.Z ), new Vector3( 0.06f, 0.08f, d ), solid: false );
		}
		kit.Box( "glow_purple", new Vector3( at.X, top + 3.45f, at.Z ), new Vector3( w + 0.9f, 0.16f, 0.16f ), solid: false );
		foreach ( var x in new[] { -6f, 0f, 6f } )   // three gold tridents on the ridge
		{
			kit.Cylinder( "gold_bright", new Vector3( at.X + x, top + 4.4f, at.Z ), 0.08f, 2f, sides: 6 );
			foreach ( var o in new[] { -0.35f, 0f, 0.35f } )
				kit.Cylinder( "gold_bright", new Vector3( at.X + x + o, top + 5.6f - MathF.Abs( o ), at.Z ), 0.06f, 0.8f, sides: 6, topRadius: 0f );
		}
	}

	/// <summary> A colossal gold Poseidon on a black plinth, facing the arrival. </summary>
	static void Statue( MeshKit kit, Vector3 at )
	{
		kit.Box( "obsidian", at + new Vector3( 0f, 1.2f, 0f ), new Vector3( 3.2f, 2.4f, 3.2f ) );
		foreach ( var s in new[] { -1f, 1f } )
			kit.Box( "glow_green", at + new Vector3( s * 1.62f, 1.2f, 0f ), new Vector3( 0.05f, 1.6f, 2f ), solid: false );
		kit.Box( "glow_green", at + new Vector3( 0f, 1.2f, -1.62f ), new Vector3( 2f, 1.6f, 0.05f ), solid: false );
		// Poseidon himself, the wave 30 boss, cast in gold: standing on his tail, trident upright, facing the arrival.
		var foot = at + Vector3.Up * 2.4f;
		Poseidon.BuildStatue( kit.Node, foot, 1.7f, (Arrival - at).Flat() );
		kit.Solid( new Transform3D( Basis.Identity, foot + new Vector3( 0f, 5f, 0f ) ), new Vector3( 2.6f, 10f, 2f ) );
	}

	/// <summary> A black obelisk with green runes and a glowing tip. </summary>
	static void Obelisk( MeshKit kit, Vector3 at, float height )
	{
		kit.Cylinder( "obsidian", at + Vector3.Up * height * 0.5f, 0.7f, height, new Basis( Vector3.Up, MathF.PI * 0.25f ), 4, 0.42f );
		kit.Cylinder( "glow_green", at + Vector3.Up * (height + 0.45f), 0.42f, 0.9f, new Basis( Vector3.Up, MathF.PI * 0.25f ), 4, 0f );
		for ( var i = 0; i < 4; i++ )
		{
			var a = MathF.PI * 0.5f * i;
			var dir = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			kit.Box( "glow_green", at + dir * 0.52f + Vector3.Up * height * 0.55f, new Vector3( 0.06f, height * 0.6f, 0.12f ), new Basis( Vector3.Up, -a ), solid: false );
		}
		kit.Solid( new Transform3D( Basis.Identity, at + Vector3.Up * height * 0.5f ), new Vector3( 1f, height, 1f ) );
	}

	// ---- Crossings: grand stairways up to the island, bridges over the outer canal ----

	static void Crossings( MeshKit kit )
	{
		for ( var k = 0; k < 4; k++ )
		{
			var a = 90f * k;
			Ramp( kit, Polar( IslandRadius - 0.3f, a, IslandHeight ), Polar( Canals[0].Outer + 0.3f, a, Land ), 6f, "marble" );
		}
		for ( var k = 0; k < Avenues; k++ )
		{
			var a = 45f * k;
			var from = Polar( Canals[1].Inner - 0.4f, a, Land );
			var to = Polar( Canals[1].Outer + 0.4f, a, Land );
			var along = (to - from).Normalized();
			var across = Vector3.Up.Cross( along ).Normalized();
			var turn = new Basis( along, Vector3.Up, -across );   // X along the bridge
			var mid = (from + to) * 0.5f;
			var length = from.DistanceTo( to );
			kit.Box( "marble", mid + Vector3.Down * 0.2f, new Vector3( length, 0.4f, 6f ), turn );
			kit.Box( "gold", mid + Vector3.Down * 0.5f, new Vector3( length, 0.2f, 6.2f ), turn, solid: false );
			foreach ( var s in new[] { -1f, 1f } )
			{
				kit.Box( "marble", mid + across * s * 2.85f + Vector3.Up * 0.45f, new Vector3( length, 0.9f, 0.3f ), turn );
				foreach ( var end in new[] { from, to } )
				{
					var post = end + across * s * 2.85f;
					kit.Cylinder( "obsidian", post + Vector3.Up * 1.3f, 0.14f, 2.6f, sides: 8 );
					kit.Sphere( "glow_teal", post + Vector3.Up * 2.75f, 0.32f, 10 );
				}
			}
		}
	}

	/// <summary>
	/// A walkway from one point to another (the top surface on the line), with a solid slab, gold step lines and
	/// marble side walls. Gentle enough to walk on (the steepest is under 20 degrees).
	/// </summary>
	static void Ramp( MeshKit kit, Vector3 from, Vector3 to, float width, string key )
	{
		var run = to - from;
		var length = run.Length();
		var x = run / length;
		// Square axes with Y the slab's up (checked: for a run going down, Up x run would point it down).
		var z = x.Cross( Vector3.Up ).Normalized();
		var y = z.Cross( x );
		var turn = new Basis( x, y, z );
		const float thick = 0.5f;
		var centre = (from + to) * 0.5f - y * thick * 0.5f;
		kit.Box( key, centre, new Vector3( length + 0.3f, thick, width ), turn );
		var steps = (int)(length / 0.5f);
		for ( var i = 1; i < steps; i++ )
			kit.Box( "gold", from + run * (i / (float)steps) + y * 0.02f, new Vector3( 0.06f, 0.04f, width * 0.94f ), turn, solid: false );
		foreach ( var s in new[] { -1f, 1f } )
			kit.Box( "marble", centre + z * s * (width * 0.5f - 0.15f) + y * (thick * 0.5f + 0.4f), new Vector3( length + 0.3f, 0.8f, 0.3f ), turn );
	}

	// ---- Avenues: lamps along the eight streets out from the middle ----

	static void AvenueLamps( MeshKit kit )
	{
		for ( var k = 0; k < Avenues; k++ )
		{
			var a = 45f * k;
			var dir = Polar( 1f, a, 0f );
			var side = Vector3.Up.Cross( dir );
			foreach ( var r in new[] { 30f, 40f, 60f, 70f, 80f } )
				foreach ( var s in new[] { -1f, 1f } )
				{
					var at = dir * r + side * s * 3.6f + Vector3.Up * Land;
					// None among the buy stations behind the arrival pad, or where the Kraken comes up.
					if ( at.Flat().DistanceTo( Arrival.Flat() + new Vector3( 0f, 0f, -10f ) ) < 6f ) continue;
					if ( at.Flat().DistanceTo( KrakenCentre.Flat() ) < KrakenPlaza ) continue;
					kit.Cylinder( "obsidian", at + Vector3.Up * 1.6f, 0.12f, 3.2f, sides: 8 );
					kit.Cylinder( "gold", at + Vector3.Up * 3.25f, 0.2f, 0.12f, sides: 8 );
					kit.Sphere( "glow_teal", at + Vector3.Up * 3.55f, 0.3f, 10 );
					kit.Solid( new Transform3D( Basis.Identity, at + Vector3.Up * 1.6f ), new Vector3( 0.3f, 3.2f, 0.3f ) );
				}
		}
	}

	// ---- Districts: rows of buildings between the avenues ----

	/// <summary> A row of buildings: its middle's radius, how deep the plots are, and which way the fronts face (+1 out). </summary>
	static readonly (float Radius, float Depth, int Facing, int Ring)[] Rows =
	{
		(30.5f, 6f, +1, 1), (41.5f, 6f, -1, 1),
		(59f, 7f, +1, 2), (71.5f, 7f, -1, 2), (84.5f, 8f, -1, 2),
	};

	static void Districts( MeshKit kit, Random random, Func<float, float, float> R )
	{
		var count = 0;
		foreach ( var (radius, depth, facing, ring) in Rows )
		{
			// Each sector between two avenues: from one avenue's edge to the next, plots of 6 to 9 m with 2 m lanes.
			var margin = (AvenueWidth * 0.5f + 1f) / radius;
			for ( var k = 0; k < Avenues; k++ )
			{
				roof = DistrictRoofs[k];
				var start = MathX.Rad( 45f * k ) + margin;
				var end = MathX.Rad( 45f * (k + 1) ) - margin;
				var angle = start;
				while ( true )
				{
					var w = R( 6f, 9f );
					var span = w / radius;
					if ( angle + span > end ) break;
					var mid = angle + span * 0.5f;
					angle += span + 2f / radius;
					var centre = new Vector3( MathF.Cos( mid ) * radius, Land, MathF.Sin( mid ) * radius );
					var reach = MathF.Max( w, depth ) * 0.6f + 1f;
					if ( centre.Flat().DistanceTo( Arrival.Flat() ) < ArrivalPlaza + reach ) continue;
					if ( centre.Flat().DistanceTo( KrakenCentre.Flat() ) < KrakenPlaza + reach ) continue;
					var radial = new Vector3( MathF.Cos( mid ), 0f, MathF.Sin( mid ) );
					var tangent = new Vector3( -MathF.Sin( mid ), 0f, MathF.Cos( mid ) );
					// Local X across the plot, Y up, Z out of the front (toward the street it faces).
					var basis = new Basis( tangent * -facing, Vector3.Up, radial * facing );
					var roll = random.NextDouble();
					var built = true;
					if ( ring == 1 )
					{
						if ( roll < 0.4 ) Villa( kit, random, R, centre, basis, w, depth );
						else if ( roll < 0.6 ) Shrine( kit, random, R, centre, basis, w, depth );
						else if ( roll < 0.73 ) Tower( kit, random, R, centre, basis, w, depth );
						else if ( roll < 0.82 ) Ziggurat( kit, random, R, centre, basis, w, depth );
						else if ( roll < 0.87 ) Crystals( kit, random, R, centre, w, depth );
						else if ( roll < 0.94 ) { CoralGarden( kit, random, R, centre, basis, w, depth ); built = false; }
						else { Plaza( kit, random, R, centre, basis, w, depth ); built = false; }
					}
					else
					{
						if ( roll < 0.33 ) Villa( kit, random, R, centre, basis, w, depth );
						else if ( roll < 0.55 ) Tower( kit, random, R, centre, basis, w, depth );
						else if ( roll < 0.68 ) Ziggurat( kit, random, R, centre, basis, w, depth );
						else if ( roll < 0.77 ) Shrine( kit, random, R, centre, basis, w, depth );
						else if ( roll < 0.83 ) Crystals( kit, random, R, centre, w, depth );
						else if ( roll < 0.92 ) { CoralGarden( kit, random, R, centre, basis, w, depth ); built = false; }
						else { Plaza( kit, random, R, centre, basis, w, depth ); built = false; }
					}
					if ( built && random.NextDouble() < 0.4 ) Amphorae( kit, random, R, centre + basis * new Vector3( R( -1f, 1f ) * w * 0.42f, 0f, depth * 0.5f - 0.45f ) );
					count++;
				}
			}
		}
		GD.Print( $"[atlantis] {count} buildings" );
	}

	/// <summary> A marble house: a gold band, a flat roof with a sea-glass or gold dome, a columned porch at the front. </summary>
	static void Villa( MeshKit kit, Random random, Func<float, float, float> R, Vector3 at, Basis b, float w, float depth )
	{
		var d = depth - 2.6f;   // the porch takes the rest of the plot
		var h = R( 4.5f, 8f );
		Vector3 L( float x, float y, float z ) => at + b * new Vector3( x, y, z );
		var back = -1.3f;   // the house sits to the back of the plot, the porch in front
		kit.Box( "marble", L( 0f, h * 0.5f, back ), new Vector3( w, h, d ), b );
		kit.Box( "gold", L( 0f, h - 0.4f, back ), new Vector3( w + 0.12f, 0.3f, d + 0.12f ), b, solid: false );
		kit.Box( "marble_dark", L( 0f, h + 0.15f, back ), new Vector3( w + 0.4f, 0.3f, d + 0.4f ), b, solid: false );
		kit.Sphere( random.NextDouble() < 0.7 ? roof : "gold", L( 0f, h + 0.3f, back ), MathF.Min( w, d ) * 0.32f, 14 );
		if ( random.NextDouble() < 0.4 )
			kit.Cylinder( "crystal", L( 0f, h + 0.3f + MathF.Min( w, d ) * 0.32f + 0.6f, back ), 0.18f, 1.4f, sides: 6, topRadius: 0f );
		// The door and two lit windows on the front.
		var front = back + d * 0.5f + 0.03f;
		kit.Box( "obsidian", L( 0f, 1.2f, front ), new Vector3( 1.3f, 2.4f, 0.08f ), b, solid: false );
		foreach ( var s in new[] { -1f, 1f } )
			kit.Box( "glow_green", L( s * w * 0.3f, h * 0.6f, front ), new Vector3( 0.8f, 1f, 0.08f ), b, solid: false );
		// The porch: four columns under a gold-trimmed roof.
		var porch = back + d * 0.5f + 1.25f;
		var ph = MathF.Min( h * 0.75f, 4.2f );
		for ( var i = 0; i < 4; i++ )
		{
			var x = (-0.5f + i / 3f) * w * 0.8f;
			kit.Cylinder( "marble", L( x, ph * 0.5f, porch + 0.6f ), 0.22f, ph, sides: 10 );
			kit.Solid( new Transform3D( b, L( x, ph * 0.5f, porch + 0.6f ) ), new Vector3( 0.4f, ph, 0.4f ) );
		}
		kit.Box( "marble", L( 0f, ph + 0.18f, porch ), new Vector3( w * 0.92f, 0.35f, 2.6f ), b, solid: false );
		kit.Box( "gold", L( 0f, ph + 0.42f, porch ), new Vector3( w * 0.92f, 0.14f, 2.6f ), b, solid: false );
		// Alien vines creeping down the walls, and banners either side of the door.
		var vines = random.Next( 0, 4 );
		for ( var i = 0; i < vines; i++ )
		{
			var side = random.Next( 3 );   // front, left, right
			var top = side == 0 ? L( R( -0.45f, 0.45f ) * w, h, front ) : L( (side == 1 ? -1f : 1f) * (w * 0.5f + 0.03f), h, back + R( -0.4f, 0.4f ) * d );
			var normal = side == 0 ? b.Z : b.X * (side == 1 ? -1f : 1f);
			Vine( kit, random, R, top, normal, R( h * 0.4f, h * 0.95f ) );
		}
		if ( random.NextDouble() < 0.55 )
		{
			var banner = random.NextDouble() < 0.5 ? "banner_purple" : "banner_green";
			foreach ( var sx in new[] { -1f, 1f } )
				Banner( kit, L( sx * w * 0.3f, h - 0.7f, front + 0.06f ), b, MathF.Min( 3f, h * 0.5f ), banner );
		}
	}

	/// <summary> A round tower: marble with glowing green window bands, a gold cone and a crystal spire. </summary>
	static void Tower( MeshKit kit, Random random, Func<float, float, float> R, Vector3 at, Basis b, float w, float depth )
	{
		var r = MathF.Min( w, depth ) * 0.42f;
		var h = R( 11f, 22f );
		kit.Box( "obsidian", at + Vector3.Up * 0.4f, new Vector3( r * 2.3f, 0.8f, r * 2.3f ), b );
		kit.Cylinder( "marble", at + Vector3.Up * h * 0.5f, r, h, sides: 16, topRadius: r * 0.88f );
		kit.Solid( new CylinderShape3D { Radius = r, Height = h }, new Transform3D( Basis.Identity, at + Vector3.Up * h * 0.5f ) );
		for ( var y = 3f; y < h - 1.5f; y += R( 2.6f, 3.4f ) )
			kit.Cylinder( "glow_green", at + Vector3.Up * y, r * (1f - 0.12f * y / h) + 0.04f, 0.28f, sides: 16 );
		kit.Cylinder( "gold", at + Vector3.Up * (h + 0.15f), r * 0.98f, 0.3f, sides: 16 );
		kit.Cylinder( random.NextDouble() < 0.35 ? "gold" : roof, at + Vector3.Up * (h + 0.3f + r * 0.9f), r * 0.95f, r * 1.8f, sides: 16, topRadius: 0f );
		kit.Cylinder( "crystal", at + Vector3.Up * (h + 0.3f + r * 1.8f + 0.9f), 0.25f, 1.8f, sides: 6, topRadius: 0f );
		var vines = random.Next( 0, 4 );
		for ( var i = 0; i < vines; i++ )
		{
			var a = R( 0f, MathF.Tau );
			var dir = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			var top = R( h * 0.45f, h * 0.95f );
			Vine( kit, random, R, at + dir * (r * (1f - 0.12f * top / h) + 0.05f) + Vector3.Up * top, dir, R( top * 0.5f, top - 0.3f ) );
		}
		if ( random.NextDouble() < 0.5 )
		{
			var front = b.Z;
			Banner( kit, at + front * (r + 0.08f) + Vector3.Up * (h - 1.2f), b, MathF.Min( 5f, h * 0.35f ), random.NextDouble() < 0.5 ? "banner_purple" : "banner_green" );
		}
	}

	/// <summary> An open shrine: a low platform, a ring of columns, a gold roof, and a purple crystal on the altar. </summary>
	static void Shrine( MeshKit kit, Random random, Func<float, float, float> R, Vector3 at, Basis b, float w, float depth )
	{
		var d = depth - 0.6f;
		var h = R( 3.6f, 4.8f );
		Vector3 L( float x, float y, float z ) => at + b * new Vector3( x, y, z );
		kit.Box( "marble_dark", L( 0f, 0.15f, 0f ), new Vector3( w, 0.3f, d ), b );
		var nx = w > 7.5f ? 4 : 3;
		for ( var i = 0; i < nx; i++ )
			foreach ( var z in new[] { -d * 0.5f + 0.6f, d * 0.5f - 0.6f } )
			{
				var x = (-0.5f + i / (float)(nx - 1)) * (w - 1.2f);
				kit.Cylinder( "marble", L( x, 0.3f + h * 0.5f, z ), 0.26f, h, sides: 10 );
				kit.Solid( new Transform3D( b, L( x, 0.3f + h * 0.5f, z ) ), new Vector3( 0.45f, h, 0.45f ) );
			}
		kit.Box( "gold", L( 0f, 0.3f + h + 0.2f, 0f ), new Vector3( w + 0.3f, 0.4f, d + 0.3f ), b, solid: false );
		kit.Box( "marble", L( 0f, 0.3f + h + 0.55f, 0f ), new Vector3( w * 0.7f, 0.3f, d * 0.7f ), b, solid: false );
		kit.Box( "obsidian", L( 0f, 0.8f, 0f ), new Vector3( 1.2f, 1f, 1.2f ), b );
		kit.Cylinder( "crystal_purple", L( 0f, 1.3f + 0.9f, 0f ), 0.35f, 1.8f, sides: 6, topRadius: 0f );
	}

	/// <summary> A stepped obsidian ziggurat, alien: purple glowing seams on every step, a green crystal cluster on top. </summary>
	static void Ziggurat( MeshKit kit, Random random, Func<float, float, float> R, Vector3 at, Basis b, float w, float depth )
	{
		var d = depth - 0.4f;
		var y = 0f;
		var scale = 1f;
		for ( var tier = 0; tier < 3; tier++ )
		{
			var h = R( 2.2f, 3f );
			kit.Box( "obsidian", at + b * new Vector3( 0f, y + h * 0.5f, 0f ), new Vector3( w * scale, h, d * scale ), b );
			kit.Box( "glow_purple", at + b * new Vector3( 0f, y + h - 0.1f, 0f ), new Vector3( w * scale + 0.06f, 0.12f, d * scale + 0.06f ), b, solid: false );
			y += h;
			scale *= 0.7f;
		}
		for ( var i = 0; i < 4; i++ )
		{
			var tilt = new Basis( Vector3.Up, R( 0f, MathF.Tau ) ) * new Basis( Vector3.Right, R( -0.35f, 0.35f ) );
			var tall = R( 1.2f, 2.6f );
			kit.Add( "crystal", new CylinderMesh { BottomRadius = 0.32f, TopRadius = 0f, Height = tall, RadialSegments = 6, Rings = 1 },
				new Transform3D( b * tilt, at + b * new Vector3( R( -0.6f, 0.6f ), y + tall * 0.45f, R( -0.6f, 0.6f ) ) ) );
		}
		if ( random.NextDouble() < 0.6 )   // purple vines down the front of the bottom tier
			for ( var i = 0; i < 3; i++ )
				Vine( kit, random, R, at + b * new Vector3( R( -0.4f, 0.4f ) * w, 2.1f, (depth - 0.4f) * 0.5f + 0.03f ), b.Z, R( 1.2f, 2f ), purple: true );
	}

	/// <summary> A grove of alien crystals growing out of the paving (instead of a building), each one solid. </summary>
	static void Crystals( MeshKit kit, Random random, Func<float, float, float> R, Vector3 at, float w, float depth )
	{
		var n = random.Next( 3, 7 );
		for ( var i = 0; i < n; i++ )
		{
			var tall = R( 3f, 9f );
			var r = R( 0.5f, 1.1f );
			var tilt = new Basis( Vector3.Up, R( 0f, MathF.Tau ) ) * new Basis( Vector3.Right, R( -0.3f, 0.3f ) );
			var foot = at + new Vector3( R( -w * 0.3f, w * 0.3f ), 0f, R( -depth * 0.3f, depth * 0.3f ) );
			var centre = foot + tilt * (Vector3.Up * tall * 0.5f);
			kit.Add( random.NextDouble() < 0.65 ? "crystal" : "crystal_purple", new CylinderMesh { BottomRadius = r, TopRadius = 0f, Height = tall, RadialSegments = 6, Rings = 1 }, new Transform3D( tilt, centre ) );
			kit.Solid( new Transform3D( tilt, centre - tilt * (Vector3.Up * tall * 0.15f) ), new Vector3( r * 1.2f, tall * 0.7f, r * 1.2f ) );
		}
	}


	/// <summary> A vine hanging down a wall from `top`: a wandering stem of short pieces with leaves, the wall's normal out. </summary>
	static void Vine( MeshKit kit, Random random, Func<float, float, float> R, Vector3 top, Vector3 normal, float length, bool purple = false )
	{
		var across = Vector3.Up.Cross( normal ).SafeNormal();
		if ( across == Vector3.Zero ) return;
		var p = top + normal * 0.05f;
		var drift = R( -0.4f, 0.4f );
		var hang = 0f;
		while ( hang < length )
		{
			var step = R( 0.45f, 0.7f );
			drift = MathX.Clamp( drift + R( -0.35f, 0.35f ), -0.7f, 0.7f );
			var next = p + Vector3.Down * step + across * drift * step;
			var mid = (p + next) * 0.5f;
			var dir = (next - p).Normalized();
			kit.Box( purple ? "vine_purple" : "vine", mid, new Vector3( 0.09f, step + 0.04f, 0.09f ), new Basis( new Quaternion( Vector3.Up, dir ) ), solid: false );
			if ( random.NextDouble() < 0.7 )
			{
				var leaf = random.NextDouble() < 0.18 ? (purple ? "glow_green" : "glow_purple") : purple ? "vine_purple" : "leaf";   // a few glowing buds
				var side = random.NextDouble() < 0.5 ? -1f : 1f;
				kit.Box( leaf, mid + across * side * 0.14f + normal * 0.05f, new Vector3( 0.22f, 0.14f, 0.04f ),
					new Basis( normal.Cross( Vector3.Up ).SafeNormal() * -1f, Vector3.Up, normal ) * new Basis( Vector3.Back, side * R( 0.3f, 0.9f ) ), solid: false );
			}
			p = next;
			hang += step;
		}
	}

	/// <summary> A hanging banner against a wall: a gold rod, the cloth (a little glow), a gold tassel at the point. </summary>
	static void Banner( MeshKit kit, Vector3 top, Basis b, float length, string cloth )
	{
		kit.Cylinder( "gold", top, 0.05f, 1.2f, b * new Basis( Vector3.Back, MathF.PI * 0.5f ), 6 );
		kit.Box( cloth, top + Vector3.Down * length * 0.5f + b.Z * 0.02f, new Vector3( 1f, length, 0.03f ), b, solid: false );
		kit.Box( "gold", top + Vector3.Down * length * 0.08f + b.Z * 0.04f, new Vector3( 0.5f, 0.08f, 0.02f ), b, solid: false );
		kit.Cylinder( "gold", top + Vector3.Down * (length + 0.15f) + b.Z * 0.02f, 0.12f, 0.3f, sides: 6, topRadius: 0f );
	}

	/// <summary> A few clay and gold amphorae: round bellies, necks and lips, some fallen over. </summary>
	static void Amphorae( MeshKit kit, Random random, Func<float, float, float> R, Vector3 at )
	{
		var n = random.Next( 1, 4 );
		for ( var i = 0; i < n; i++ )
		{
			var key = random.NextDouble() < 0.75 ? "terracotta" : "gold";
			var p = at + new Vector3( R( -0.6f, 0.6f ), 0f, R( -0.4f, 0.4f ) );
			var size = R( 0.75f, 1.15f );
			var fallen = random.NextDouble() < 0.3;
			var turn = fallen ? new Basis( Vector3.Up, R( 0f, MathF.Tau ) ) * new Basis( Vector3.Right, MathF.PI * 0.5f ) : Basis.Identity;
			var centre = p + Vector3.Up * (fallen ? 0.3f : 0.45f) * size;
			kit.Add( key, new SphereMesh { Radius = 0.3f * size, Height = 0.8f * size, RadialSegments = 10, Rings = 6 }, new Transform3D( turn, centre ) );
			kit.Cylinder( key, centre + turn * (Vector3.Up * 0.48f * size), 0.09f * size, 0.3f * size, turn, 8 );
			kit.Cylinder( key, centre + turn * (Vector3.Up * 0.64f * size), 0.14f * size, 0.06f * size, turn, 8 );
			if ( !fallen ) kit.Box( "purple", centre, new Vector3( 0.62f * size, 0.08f * size, 0.62f * size ), solid: false );   // a painted band
			kit.Solid( new Transform3D( turn, centre ), new Vector3( 0.5f, 0.8f, 0.5f ) * size );
		}
	}

	/// <summary> A coral garden in a plot: a low marble kerb round sand, branching coral in pink, orange and alien glow. </summary>
	static void CoralGarden( MeshKit kit, Random random, Func<float, float, float> R, Vector3 at, Basis b, float w, float depth )
	{
		Vector3 L( float x, float y, float z ) => at + b * new Vector3( x, y, z );
		var gw = w - 1f;
		var gd = depth - 1f;
		kit.Box( "marble_dark", L( 0f, 0.05f, 0f ), new Vector3( gw, 0.1f, gd ), b, solid: false );
		foreach ( var s in new[] { -1f, 1f } )
		{
			kit.Box( "marble", L( 0f, 0.25f, s * gd * 0.5f ), new Vector3( gw, 0.5f, 0.3f ), b );
			kit.Box( "marble", L( s * gw * 0.5f, 0.25f, 0f ), new Vector3( 0.3f, 0.5f, gd ), b );
		}
		var n = random.Next( 5, 9 );
		for ( var i = 0; i < n; i++ )
			Coral( kit, random, R, L( R( -0.4f, 0.4f ) * gw, 0.1f, R( -0.38f, 0.38f ) * gd ), 1f );
	}

	/// <summary> One branching coral: pink, orange, purple or jade, forking twice, glowing at the tips. Solid at the trunk. </summary>
	static void Coral( MeshKit kit, Random random, Func<float, float, float> R, Vector3 foot, float size )
	{
		var key = random.NextDouble() switch { < 0.35 => "coral_pink", < 0.6 => "coral_orange", < 0.8 => "purple", _ => "roof_jade" };
		var tip = random.NextDouble() < 0.5 ? "glow_purple" : "glow_green";
		Branch( foot, Vector3.Up, R( 0.9f, 1.6f ) * size, 0.13f * size, 0 );
		kit.Solid( new Transform3D( Basis.Identity, foot + Vector3.Up * 0.6f * size ), new Vector3( 0.35f, 1.2f, 0.35f ) * size );

		void Branch( Vector3 from, Vector3 dir, float length, float r, int level )
		{
			var to = from + dir * length;
			kit.Cylinder( key, (from + to) * 0.5f, r, length, new Basis( new Quaternion( Vector3.Up, dir ) ), 6, r * 0.7f );
			if ( level == 2 ) { kit.Sphere( tip, to, r * 1.4f, 6 ); return; }
			var forks = random.Next( 2, 4 );
			for ( var f = 0; f < forks; f++ )
			{
				var a = R( 0f, MathF.Tau );
				var bent = (dir + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * R( 0.4f, 0.9f )).Normalized();
				Branch( to, bent, length * R( 0.5f, 0.75f ), r * 0.7f, level + 1 );
			}
		}
	}

	/// <summary>
	/// The dry canals: a lower floor of sand a storey under the streets, reached by the ramps, with the sea floor's own
	/// things growing in it: rocks, coral, tall kelp reaching up past the street, glowing alien bulbs, crystals, and the
	/// city's fallen pieces (column drums, amphorae). Kept clear of the ramps, stairways and bridges.
	/// </summary>
	static void DryCanals( MeshKit kit, Random random, Func<float, float, float> R )
	{
		var floor = CanalFloorY;
		for ( var c = 0; c < Canals.Length; c++ )
		{
			var (inner, outer) = Canals[c];
			var mid = (inner + outer) * 0.5f;
			// What crosses or comes down into this canal, by angle: keep 5 m either side clear.
			var crossings = new List<float>();
			for ( var k = 0; k < 8; k++ )
			{
				if ( c == 0 && k % 2 == 0 ) crossings.Add( 45f * k );   // the island's stairways
				if ( c == 0 && k % 2 == 1 ) crossings.Add( 45f * k );   // the ramps down
				if ( c == 1 ) { crossings.Add( 45f * k ); crossings.Add( 22.5f + 45f * k ); }   // bridges, ramps
			}
			bool Clear( float degrees ) => crossings.All( x => MathF.Abs( Mathf.Wrap( degrees - x, -180f, 180f ) ) * MathF.PI / 180f * mid > 5.5f );
			var count = (int)(MathF.Tau * mid / 2.6f);
			for ( var i = 0; i < count; i++ )
			{
				var deg = R( 0f, 360f );
				if ( !Clear( deg ) ) continue;
				var at = Polar( R( inner + 1f, outer - 1f ), deg, floor );
				if ( Places.Any( pl => pl.At.Flat().DistanceTo( at.Flat() ) < 3.5f ) ) continue;   // where the test stands
				var roll = random.NextDouble();
				if ( roll < 0.25 )   // rocks
				{
					var size = R( 0.5f, 1.4f );
					kit.Add( random.NextDouble() < 0.5 ? "rock" : "rock_dark", new SphereMesh { Radius = size, Height = size * R( 1f, 1.5f ), RadialSegments = 8, Rings = 5 },
						new Transform3D( new Basis( Vector3.Up, R( 0f, MathF.Tau ) ).Scaled( new Vector3( R( 1f, 1.6f ), 1f, 1f ) ), at + Vector3.Up * size * 0.25f ) );
					kit.Solid( new Transform3D( Basis.Identity, at + Vector3.Up * size * 0.3f ), new Vector3( size * 1.4f, size * 0.9f, size * 1.4f ) );
				}
				else if ( roll < 0.45 ) Coral( kit, random, R, at, R( 0.8f, 1.5f ) );
				else if ( roll < 0.62 ) Kelp( kit, random, R, at );
				else if ( roll < 0.74 )   // glowing alien bulbs on stalks
				{
					var n = random.Next( 2, 5 );
					for ( var b = 0; b < n; b++ )
					{
						var foot = at + new Vector3( R( -0.6f, 0.6f ), 0f, R( -0.6f, 0.6f ) );
						var tall = R( 0.4f, 1.3f );
						kit.Cylinder( "vine_purple", foot + Vector3.Up * tall * 0.5f, 0.04f, tall, sides: 5 );
						kit.Sphere( random.NextDouble() < 0.5 ? "glow_green" : "glow_purple", foot + Vector3.Up * tall, R( 0.12f, 0.22f ), 8 );
					}
				}
				else if ( roll < 0.82 )   // a cluster of crystals
				{
					for ( var b = 0; b < 3; b++ )
					{
						var tall = R( 0.8f, 2.2f );
						var tilt = new Basis( Vector3.Up, R( 0f, MathF.Tau ) ) * new Basis( Vector3.Right, R( -0.4f, 0.4f ) );
						kit.Add( random.NextDouble() < 0.6 ? "crystal" : "crystal_purple", new CylinderMesh { BottomRadius = 0.25f, TopRadius = 0f, Height = tall, RadialSegments = 6, Rings = 1 },
							new Transform3D( tilt, at + tilt * (Vector3.Up * tall * 0.45f) ) );
					}
					kit.Solid( new Transform3D( Basis.Identity, at + Vector3.Up * 0.5f ), new Vector3( 0.6f, 1f, 0.6f ) );
				}
				else if ( roll < 0.92 )   // a fallen column drum, half in the sand
				{
					var turn = new Basis( Vector3.Up, R( 0f, MathF.Tau ) ) * new Basis( Vector3.Back, MathF.PI * 0.5f );
					var length = R( 1.2f, 2.6f );
					kit.Cylinder( "marble", at + Vector3.Up * 0.3f, 0.42f, length, turn, 12 );
					kit.Solid( new Transform3D( turn, at + Vector3.Up * 0.3f ), new Vector3( 0.8f, length, 0.8f ) );
				}
				else Amphorae( kit, random, R, at );
			}
		}
	}

	/// <summary> A strand of kelp from the canal floor up past the street: a swaying chain of stem pieces with blades. </summary>
	static void Kelp( MeshKit kit, Random random, Func<float, float, float> R, Vector3 foot )
	{
		var p = foot;
		var tall = R( 3.5f, 7f );
		var lean = new Vector3( R( -0.25f, 0.25f ), 0f, R( -0.25f, 0.25f ) );
		var key = random.NextDouble() < 0.35 ? "vine_purple" : "leaf";
		for ( var h = 0f; h < tall; )
		{
			var step = R( 0.5f, 0.8f );
			lean = new Vector3( MathX.Clamp( lean.X + R( -0.15f, 0.15f ), -0.4f, 0.4f ), 0f, MathX.Clamp( lean.Z + R( -0.15f, 0.15f ), -0.4f, 0.4f ) );
			var dir = (Vector3.Up + lean).Normalized();
			var next = p + dir * step;
			kit.Cylinder( "vine", (p + next) * 0.5f, 0.05f, step, new Basis( new Quaternion( Vector3.Up, dir ) ), 5 );
			var a = R( 0f, MathF.Tau );
			kit.Box( key, (p + next) * 0.5f + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * 0.18f, new Vector3( 0.12f, step * 0.9f, 0.03f ),
				new Basis( Vector3.Up, -a ) * new Basis( Vector3.Back, R( -0.4f, 0.4f ) ), solid: false );
			p = next;
			h += step;
		}
		if ( random.NextDouble() < 0.4 ) kit.Sphere( "glow_green", p, 0.14f, 6 );
		kit.Solid( new Transform3D( Basis.Identity, foot + Vector3.Up * 1f ), new Vector3( 0.15f, 2f, 0.15f ) );
	}

	/// <summary> A little square in place of a building: a fountain, or the broken columns of a fallen shrine. </summary>
	static void Plaza( MeshKit kit, Random random, Func<float, float, float> R, Vector3 at, Basis b, float w, float depth )
	{
		Vector3 L( float x, float y, float z ) => at + b * new Vector3( x, y, z );
		kit.Box( "marble", L( 0f, 0.04f, 0f ), new Vector3( w - 0.4f, 0.08f, depth - 0.4f ), b, solid: false );
		kit.Box( "gold", L( 0f, 0.085f, 0f ), new Vector3( w - 1.2f, 0.02f, depth - 1.2f ), b, solid: false );
		kit.Box( "marble", L( 0f, 0.09f, 0f ), new Vector3( w - 1.5f, 0.02f, depth - 1.5f ), b, solid: false );
		var r = MathF.Min( w, depth ) * 0.32f;
		if ( random.NextDouble() < 0.6 )
		{
			// The fountain: a round marble basin of glowing water, a gold bowl on a column, water falling from it.
			kit.Cylinder( "marble", at + Vector3.Up * 0.35f, r, 0.7f, sides: 24 );
			kit.Solid( new CylinderShape3D { Radius = r, Height = 0.7f }, new Transform3D( Basis.Identity, at + Vector3.Up * 0.35f ) );
			kit.Cylinder( "gold", at + Vector3.Up * 0.72f, r + 0.05f, 0.08f, sides: 24 );
			kit.Cylinder( "water", at + Vector3.Up * 0.62f, r - 0.2f, 0.06f, sides: 24 );
			kit.Cylinder( "marble", at + Vector3.Up * 1.4f, 0.25f, 1.6f, sides: 12 );
			kit.Cylinder( "gold", at + Vector3.Up * 2.25f, 0.3f, 0.35f, sides: 16, topRadius: 1f );
			kit.Cylinder( "water", at + Vector3.Up * 1.45f, 1.02f, 1.3f, sides: 16, topRadius: 0.98f );   // the falling sheet
			kit.Cylinder( "water", at + Vector3.Up * 2.75f, 0.08f, 0.7f, sides: 8, topRadius: 0.02f );   // the jet
			kit.Sphere( "glow_teal", at + Vector3.Up * 3.15f, 0.16f, 8 );
		}
		else
		{
			// Broken columns: stumps of different heights, drums lying where they fell.
			for ( var i = 0; i < 4; i++ )
			{
				var p = L( (i % 2 == 0 ? -1f : 1f) * r, 0f, (i < 2 ? -1f : 1f) * r * 0.7f );
				var h = R( 0.6f, 3.4f );
				kit.Cylinder( "marble", p + Vector3.Up * h * 0.5f, 0.32f, h, sides: 12 );
				kit.Solid( new Transform3D( Basis.Identity, p + Vector3.Up * h * 0.5f ), new Vector3( 0.6f, h, 0.6f ) );
				if ( random.NextDouble() < 0.6 )
				{
					var a = R( 0f, MathF.Tau );
					var turn = new Basis( Vector3.Up, a ) * new Basis( Vector3.Back, MathF.PI * 0.5f );
					var drum = p + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * R( 0.9f, 1.5f ) + Vector3.Up * 0.32f;
					kit.Cylinder( "marble", drum, 0.32f, 0.9f, turn, 12 );
					kit.Solid( new Transform3D( turn, drum ), new Vector3( 0.6f, 0.9f, 0.6f ) );
				}
			}
			kit.Box( "marble_dark", L( 0f, 0.3f, 0f ), new Vector3( 2.4f, 0.6f, 0.9f ), b * new Basis( Vector3.Up, 0.3f ) * new Basis( Vector3.Back, 0.12f ) );   // the fallen lintel
			if ( random.NextDouble() < 0.5 ) Vine( kit, random, R, L( r, 2.5f, -r * 0.7f + 0.33f ), b.Z, 1.8f );
		}
	}

	/// <summary> Arches across the avenues where they enter the rings: obsidian piers, a marble lintel with gold, a glowing keystone and banners. </summary>
	static void Arches( MeshKit kit )
	{
		for ( var k = 0; k < Avenues; k++ )
		{
			var a = 45f * k;
			foreach ( var r in new[] { 31.5f, 58.5f } )
			{
				var at = Polar( r, a, Land );
				if ( at.Flat().DistanceTo( KrakenCentre.Flat() ) < KrakenPlaza + 2f ) continue;
				var dir = Polar( 1f, a, 0f );
				var across = Vector3.Up.Cross( dir ).Normalized();
				var b = new Basis( across, Vector3.Up, dir );   // X across the avenue
				const float half = 4.7f, h = 7f;
				foreach ( var s in new[] { -1f, 1f } )
				{
					var pier = at + across * s * half;
					kit.Box( "obsidian", pier + Vector3.Up * h * 0.5f, new Vector3( 1f, h, 1.2f ), b );
					kit.Box( "gold", pier + Vector3.Up * 0.3f, new Vector3( 1.2f, 0.6f, 1.4f ), b, solid: false );
					kit.Box( "gold", pier + Vector3.Up * (h - 0.2f), new Vector3( 1.2f, 0.4f, 1.4f ), b, solid: false );
					kit.Box( "glow_green", pier + Vector3.Up * h * 0.5f + across * -s * 0.51f, new Vector3( 0.04f, h * 0.6f, 0.2f ), b, solid: false );
					foreach ( var face in new[] { -1f, 1f } )
						Banner( kit, pier + Vector3.Up * (h - 0.8f) + dir * face * 0.62f, face > 0 ? b : b * new Basis( Vector3.Up, MathF.PI ), 3.2f, k % 2 == 0 ? "banner_purple" : "banner_green" );
				}
				kit.Box( "marble", at + Vector3.Up * (h + 0.5f), new Vector3( half * 2f + 2f, 1f, 1.4f ), b );
				kit.Box( "gold", at + Vector3.Up * (h + 1.1f), new Vector3( half * 2f + 2.3f, 0.25f, 1.6f ), b, solid: false );
				kit.Box( "glow_purple", at + Vector3.Up * (h + 0.05f), new Vector3( half * 2f - 1f, 0.1f, 1.2f ), b, solid: false );
				kit.Box( "glow_green", at + Vector3.Up * (h + 0.5f), new Vector3( 0.9f, 1.3f, 1.5f ), b * new Basis( Vector3.Back, MathF.PI * 0.25f ), solid: false );
				kit.Cylinder( "crystal_purple", at + Vector3.Up * (h + 2.1f), 0.3f, 1.8f, sides: 6, topRadius: 0f );
			}
		}
	}

	/// <summary> Two marble Poseidons flanking the south stairway up to the island, facing the arrival. </summary>
	static void StairStatues( MeshKit kit )
	{
		foreach ( var s in new[] { -1f, 1f } )
		{
			var at = new Vector3( s * 4.3f, Land, -27.4f );
			kit.Box( "marble_dark", at + Vector3.Up * 0.6f, new Vector3( 1.6f, 1.2f, 1.6f ) );
			kit.Box( "gold", at + Vector3.Up * 1.25f, new Vector3( 1.7f, 0.1f, 1.7f ), solid: false );
			Poseidon.BuildStatue( kit.Node, at + Vector3.Up * 1.3f, 0.55f, (Arrival - at).Flat(), marble: true );
			kit.Solid( new Transform3D( Basis.Identity, at + Vector3.Up * 2.5f ), new Vector3( 1.2f, 2.5f, 1f ) );
		}
	}

	// ---- The globe's base ----

	static void GlobeBase( MeshKit kit )
	{
		Ring( "Globe Base", BaseStone, BaseInner, BaseOuter, 0f, BaseHeight, 128 );
		RingSolid( kit, BaseInner, BaseOuter, 0f, BaseHeight, 128 );
		Ring( "Base Rim", Materials["gold"], BaseInner - 0.2f, BaseOuter + 0.2f, BaseHeight, BaseHeight + 0.3f, 128 );
		Ring( "Base Skirting", Materials["gold"], BaseInner - 0.15f, BaseInner, 0.6f, 0.9f, 128 );
		// Glowing runes round the inside of the base, and a band of light under the gold rim.
		for ( var i = 0; i < 64; i++ )
		{
			var a = i * 360f / 64f;
			var at = Polar( BaseInner - 0.04f, a, BaseHeight * 0.5f );
			kit.Box( i % 2 == 0 ? "glow_green" : "glow_purple", at, new Vector3( 0.9f, 2.4f, 0.06f ), new Basis( Vector3.Up, MathF.PI * 0.5f - MathX.Rad( a ) ), solid: false );
		}
		Ring( "Base Light", Materials["glow_teal"], BaseInner - 0.06f, BaseInner - 0.02f, BaseHeight - 0.5f, BaseHeight - 0.3f, 128 );
		// Outside: gold pilasters between purple and green glowing panels, and a glowing collar where the glass sits.
		for ( var i = 0; i < 48; i++ )
		{
			var a = i * 360f / 48f;
			var face = new Basis( Vector3.Up, MathF.PI * 0.5f - MathX.Rad( a ) );
			kit.Box( "gold", Polar( BaseOuter + 0.2f, a, BaseHeight * 0.5f ), new Vector3( 0.9f, BaseHeight, 0.4f ), face, solid: false );
			kit.Box( i % 2 == 0 ? "glow_purple" : "glow_green", Polar( BaseOuter + 0.03f, a + 3.75f, BaseHeight * 0.55f ), new Vector3( 2.2f, 3.6f, 0.06f ), face, solid: false );
		}
		Ring( "Glass Collar", Materials["glow_teal"], BaseInner + 0.6f, BaseOuter - 0.6f, BaseHeight + 0.3f, BaseHeight + 0.42f, 128, walls: false );
	}

	// ---- Rings (annulus meshes and their colliders) ----

	/// <summary> A flat ring between two radii, its top at y1, with walls down to y0 on both edges (unless walls is false). </summary>
	static ArrayMesh RingMesh( float r0, float r1, float y0, float y1, int segments, bool walls )
	{
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		st.SetSmoothGroup( uint.MaxValue );   // flat: the top and the walls keep their own normals
		void Tri( Vector3 a, Vector3 b, Vector3 c, Vector3 facing )
		{
			// Godot's front face: (c - a) x (b - a) points out of it.
			if ( (c - a).Cross( b - a ).Dot( facing ) < 0f ) (b, c) = (c, b);
			st.SetUV( new Vector2( a.X, a.Z ) * 0.25f ); st.AddVertex( a );
			st.SetUV( new Vector2( b.X, b.Z ) * 0.25f ); st.AddVertex( b );
			st.SetUV( new Vector2( c.X, c.Z ) * 0.25f ); st.AddVertex( c );
		}
		void Quad( Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing ) { Tri( a, b, c, facing ); Tri( a, c, d, facing ); }
		for ( var i = 0; i < segments; i++ )
		{
			var a0 = i * MathF.Tau / segments;
			var a1 = (i + 1) * MathF.Tau / segments;
			var d0 = new Vector3( MathF.Cos( a0 ), 0f, MathF.Sin( a0 ) );
			var d1 = new Vector3( MathF.Cos( a1 ), 0f, MathF.Sin( a1 ) );
			var top = Vector3.Up * y1;
			var bottom = Vector3.Up * y0;
			Quad( d0 * r0 + top, d0 * r1 + top, d1 * r1 + top, d1 * r0 + top, Vector3.Up );
			if ( !walls ) continue;
			var outward = (d0 + d1).Normalized();
			Quad( d0 * r1 + bottom, d1 * r1 + bottom, d1 * r1 + top, d0 * r1 + top, outward );
			if ( r0 > 0.01f ) Quad( d0 * r0 + bottom, d1 * r0 + bottom, d1 * r0 + top, d0 * r0 + top, -outward );
		}
		st.GenerateNormals();
		return st.Commit();
	}

	/// <summary> A ring as its own mesh (not merged into the kit: its vertices carry fewer attributes than the kit's shapes). </summary>
	static void Ring( string name, Material material, float r0, float r1, float y0, float y1, int segments, bool walls = true ) =>
		Root.AddChild( new MeshInstance3D { Name = name, Mesh = RingMesh( r0, r1, y0, y1, segments, walls ), MaterialOverride = material } );

	/// <summary> A ring's collider: a box for each of its segments, chord-wide, radius to radius. </summary>
	static void RingSolid( MeshKit kit, float r0, float r1, float y0, float y1, int segments )
	{
		var chord = 2f * r1 * MathF.Sin( MathF.PI / segments ) + 0.05f;
		for ( var i = 0; i < segments; i++ )
		{
			var a = (i + 0.5f) * MathF.Tau / segments;
			var dir = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			var turn = new Basis( Vector3.Up, MathF.PI * 0.5f - a );   // local +Z out along dir, X along the ring
			kit.Solid( new Transform3D( turn, dir * (r0 + r1) * 0.5f + Vector3.Up * (y0 + y1) * 0.5f ), new Vector3( chord, y1 - y0, r1 - r0 ) );
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
		for ( var k = 0; k < Avenues; k++ ) Light( Polar( 50f, 45f * k, Land + 3f ), TealGlow, 1.1f, 13f );
		Light( new Vector3( 0f, IslandHeight + 6f, -9f ), new Color( 1f, 0.85f, 0.55f ), 1.4f, 18f );   // the statue and the temple front
		Light( new Vector3( 0f, IslandHeight + 9f, -20f ), new Color( 1f, 0.8f, 0.45f ), 2.2f, 22f );   // the statue, lit from the arrival side
		// The dry canals: low teal and purple light along the lower floor.
		for ( var k = 0; k < 4; k++ ) Light( Polar( 22f, 22.5f + 90f * k, CanalFloorY + 2f ), k % 2 == 0 ? TealGlow : PurpleGlow, 1.4f, 14f );
		for ( var k = 0; k < 6; k++ ) Light( Polar( 50f, 11f + 60f * k, CanalFloorY + 2f ), k % 2 == 0 ? PurpleGlow : TealGlow, 1.5f, 16f );
		// The temple: lit from inside its colonnade, purple and green, and washed in warm light from the front.
		Light( new Vector3( -6f, IslandHeight + 4f, 2f ), PurpleGlow, 2.2f, 14f );
		Light( new Vector3( 6f, IslandHeight + 4f, 2f ), GreenGlow, 2.2f, 14f );
		Light( new Vector3( 0f, IslandHeight + 13f, -6f ), new Color( 1f, 0.85f, 0.55f ), 2.5f, 20f );
		// Each district its own colour: pools of purple, green and warm gold light over the streets.
		Color[] pools = { PurpleGlow, GreenGlow, new Color( 1f, 0.7f, 0.35f ), TealGlow };
		for ( var k = 0; k < Avenues; k++ )
		{
			Light( Polar( 36f, 22.5f + 45f * k, Land + 5f ), pools[k % pools.Length], 1.6f, 15f );
			Light( Polar( 72f, 22.5f + 45f * k, Land + 6f ), pools[(k + 2) % pools.Length], 1.8f, 20f );
		}
	}
}

/// <summary> The heart of Atlantis: a giant green crystal turning slowly over the temple, glowing over the whole city. </summary>
public partial class AtlantisHeart : Node3D
{
	float time;

	public override void _Ready()
	{
		var crystal = new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.4f, 1f, 0.45f, 0.82f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f,
			EmissionEnabled = true, Emission = new Color( 0.35f, 1f, 0.3f ), EmissionEnergyMultiplier = 2f, RimEnabled = true, Rim = 0.8f,
		};
		crystal.SetMeta( "no_caustics", true );
		var core = new StandardMaterial3D { AlbedoColor = Colors.White, EmissionEnabled = true, Emission = new Color( 0.7f, 1f, 0.6f ), EmissionEnergyMultiplier = 5f };
		core.SetMeta( "no_caustics", true );
		// Two long six-sided points, one up and one down, round a white-hot core.
		AddChild( new MeshInstance3D { Mesh = new CylinderMesh { BottomRadius = 2.4f, TopRadius = 0f, Height = 6f, RadialSegments = 6, Rings = 1 }, MaterialOverride = crystal, Position = Vector3.Up * 3f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
		AddChild( new MeshInstance3D { Mesh = new CylinderMesh { BottomRadius = 0f, TopRadius = 2.4f, Height = 3.5f, RadialSegments = 6, Rings = 1 }, MaterialOverride = crystal, Position = Vector3.Down * 1.75f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
		AddChild( new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8 }, MaterialOverride = core, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
		AddChild( new OmniLight3D { LightColor = new Color( 0.45f, 1f, 0.4f ), LightEnergy = 3f, OmniRange = 60f, OmniAttenuation = 1.4f, ShadowEnabled = false } );
	}

	public override void _Process( double delta )
	{
		time += (float)delta;
		Rotation = new Vector3( 0f, time * 0.35f, MathF.Sin( time * 0.4f ) * 0.06f );
		Position = Position with { Y = Atlantis.IslandHeight + 21f + MathF.Sin( time * 0.7f ) * 0.6f };
	}
}

/// <summary> A school of small glowing fish swimming a slow loop through Atlantis, weaving as they go. </summary>
public partial class AtlantisFish : MultiMeshInstance3D
{
	const int Count = 14;
	readonly float radius, height, speed, phase, wobble;
	readonly Vector3[] offsets = new Vector3[Count];
	float time;

	public AtlantisFish() : this( 0 ) { }

	public AtlantisFish( int seed )
	{
		var random = new Random( 900 + seed );
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		radius = R( 24f, 82f );
		height = R( 3f, 16f );
		speed = R( 1.8f, 3.2f ) / radius * (seed % 2 == 0 ? 1f : -1f);
		phase = R( 0f, MathF.Tau );
		wobble = R( 2f, 6f );
		for ( var i = 0; i < Count; i++ ) offsets[i] = new Vector3( R( -1.6f, 1.6f ), R( -0.8f, 0.8f ), R( -2.2f, 2.2f ) );
		var colours = new[] { new Color( 0.35f, 1f, 0.3f ), new Color( 0.66f, 0.22f, 1f ), new Color( 0.2f, 1f, 0.85f ), new Color( 1f, 0.75f, 0.25f ) };
		var c = colours[seed % colours.Length];
		var material = new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 2.2f, Roughness = 0.4f };
		material.SetMeta( "no_caustics", true );
		var body = new PrismMesh { Size = new Vector3( 0.12f, 0.18f, 0.5f ), LeftToRight = 0.5f, Material = material };
		Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = body, InstanceCount = Count };
		CastShadow = ShadowCastingSetting.Off;
	}

	public override void _Process( double delta )
	{
		time += (float)delta;
		var a = phase + time * speed;
		var centre = new Vector3( MathF.Cos( a ) * radius, height + MathF.Sin( time * 0.3f + phase ) * wobble * 0.4f, MathF.Sin( a ) * radius );
		var heading = new Vector3( -MathF.Sin( a ), 0f, MathF.Cos( a ) ) * MathF.Sign( speed );
		var turn = Basis.LookingAt( heading, Vector3.Up );
		for ( var i = 0; i < Count; i++ )
		{
			var o = offsets[i];
			var weave = new Vector3( MathF.Sin( time * 2.3f + i ) * 0.3f, MathF.Sin( time * 1.7f + i * 1.3f ) * 0.2f, 0f );
			var wag = new Basis( Vector3.Up, MathF.Sin( time * 9f + i * 0.7f ) * 0.25f );
			Multimesh.SetInstanceTransform( i, new Transform3D( turn * wag, centre + turn * (o + weave) ) );
		}
	}
}
