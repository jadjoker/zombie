using Godot;

/// <summary>
/// Port of s&box ProceduralMap: builds a big randomized forest world instead of the hand-made clearing. A huge
/// ground with an invisible boundary, woods that thicken and thin in patches, boulder fields, fallen logs and a clear
/// start area. Only solid props. Trees and boulders get colliders so the navmesh routes around them; the hand-made
/// arena and its fixed spawns are switched off, so enemies spawn on the navmesh instead. Numbers in inches.
/// </summary>
public partial class ProceduralMap : Node3D
{
	public float HalfSize = 6000f;
	public float StartClearing = 450f;
	public float Density = 1f;
	public string ArenaName = "Arena";
	public string FixedSpawnsName = "Zombie Spawns";
	public int Seed;

	Random rnd;
	int noiseSeed;
	readonly Dictionary<(int, int), List<(Vector2 At, float Radius)>> blockers = new();
	const float BlockCell = 200f;

	static readonly Color[] TrunkTints = { new( 0.32f, 0.22f, 0.13f ), new( 0.28f, 0.2f, 0.15f ), new( 0.4f, 0.28f, 0.17f ) };
	static readonly Color[] LeafTints = { new( 0.2f, 0.5f, 0.18f ), new( 0.28f, 0.6f, 0.22f ), new( 0.16f, 0.42f, 0.16f ), new( 0.4f, 0.62f, 0.24f ), new( 0.24f, 0.48f, 0.3f ) };
	static readonly Color[] RockTints = { new( 0.46f, 0.47f, 0.45f ), new( 0.54f, 0.52f, 0.48f ), new( 0.4f, 0.42f, 0.4f ) };
	static readonly Color[] PineTints = { new( 0.14f, 0.38f, 0.2f ), new( 0.18f, 0.44f, 0.24f ), new( 0.12f, 0.34f, 0.19f ) };

	static readonly Dictionary<string, PackedScene> models = new();
	static PackedScene Model( string name )
	{
		if ( !models.TryGetValue( name, out var scene ) ) models[name] = scene = GD.Load<PackedScene>( $"res://assets/models/forest/{name}.fbx" );
		return scene;
	}

	float R( float a, float b ) => a + (float)rnd.NextDouble() * (b - a);

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

	// p is s&box (x, y) in inches.
	float Woodland( Vector2 p ) => Noise( p.X / 2600f + 11.3f, p.Y / 2600f - 4.7f ) * 0.7f + Noise( p.X / 900f, p.Y / 900f ) * 0.3f;
	float Rocky( Vector2 p ) => Noise( p.X / 1700f - 31.1f, p.Y / 1700f + 9.9f );

	bool Free( Vector2 p, float pad )
	{
		if ( MathF.Abs( p.X ) > HalfSize - 150f || MathF.Abs( p.Y ) > HalfSize - 150f ) return false;
		if ( p.Length() < StartClearing + pad ) return false;
		var cx = (int)MathF.Floor( p.X / BlockCell ); var cy = (int)MathF.Floor( p.Y / BlockCell );
		for ( var x = cx - 1; x <= cx + 1; x++ )
			for ( var y = cy - 1; y <= cy + 1; y++ )
			{
				if ( !blockers.TryGetValue( (x, y), out var list ) ) continue;
				foreach ( var (at, radius) in list )
					if ( p.DistanceTo( at ) < radius + pad ) return false;
			}
		return true;
	}

	void Block( Vector2 p, float radius )
	{
		var key = ((int)MathF.Floor( p.X / BlockCell ), (int)MathF.Floor( p.Y / BlockCell ));
		if ( !blockers.TryGetValue( key, out var list ) ) blockers[key] = list = new();
		list.Add( (p, radius) );
	}

	Vector2 RandomPoint() => new( R( -HalfSize, HalfSize ), R( -HalfSize, HalfSize ) );

	/// <summary> A forest prop at an s&box point (x, y, z inches), turned by yaw, scaled (s&box x, y, z), tinted. </summary>
	Node3D Put( string model, Vector3 sbox, float yaw, Vector3 scale, Color tint, string name )
	{
		var node = new Node3D { Name = name };
		AddChild( node );
		node.Transform = new Transform3D( new Angles( 0f, yaw, 0f ).ToBasis() * Basis.FromScale( new Vector3( scale.Y, scale.Z, scale.X ) ), U.Sbox( sbox ) );
		var instance = Model( model ).Instantiate<Node3D>();
		instance.Scale = Vector3.One * 2.54f;
		node.AddChild( instance );
		ForestLook.Apply( instance, model, scale.X, tint, rnd );
		return node;
	}

	/// <summary> Builds the world (and switches off the hand-made arena). Call before the navmesh bake. </summary>
	public void Build( Node level, Func<string, Node3D> find )
	{
		if ( Seed == 0 ) Seed = Rng.Int( 1, 1_000_000_000 );
		rnd = new Random( Seed );
		noiseSeed = Seed;
		foreach ( var name in new[] { ArenaName, FixedSpawnsName } )
			if ( find( "/" + name ) is { } go ) { go.GetParent().RemoveChild( go ); go.QueueFree(); }

		BuildGround();
		BuildBoundary();
		var d = MathF.Max( 0.1f, Density );
		for ( var i = 0; i < (int)(2600 * d); i++ ) TryTree();
		for ( var i = 0; i < (int)(900 * d); i++ ) TryRock();
		for ( var i = 0; i < (int)(40 * d); i++ ) TryLog();
		BuildEdgeWoods();
		GD.Print( $"ProceduralMap: seed {Seed}, {GetChildCount()} props" );
	}

	/// <summary> A collider for a prop, sized by its (uniform) scale: Jolt can't take the stretched prop scales. </summary>
	void Solid( Node3D node, Shape3D shape, Vector3 sboxOffset )
	{
		var scale = node.Basis.Scale.X;
		switch ( shape )
		{
			case CapsuleShape3D c: c.Radius *= scale; c.Height *= scale; break;
			case SphereShape3D sp: sp.Radius *= scale; break;
			case BoxShape3D b: if ( node.Name.ToString() != "Ground" && node.Name.ToString() != "Boundary" ) b.Size *= scale; break;
		}
		var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
		AddChild( body );
		body.Transform = new Transform3D( node.Basis.Orthonormalized(), node.Position );
		body.AddChild( new CollisionShape3D { Shape = shape, Position = U.Sbox( sboxOffset ) * scale } );
	}

	void BuildGround()
	{
		var size = HalfSize * 2f + 800f;
		var floor = new Node3D { Name = "Ground" };
		AddChild( floor );
		floor.Position = U.Sbox( 0, 0, -10f );
		Solid( floor, new BoxShape3D { Size = new Vector3( size, 20f, size ) * U.In }, Vector3.Zero );
		// materials/ground/forest_ground_world.vmat: the forest ground texture, one repeat about 250 units.
		var material = new StandardMaterial3D
		{
			AlbedoTexture = GD.Load<Texture2D>( "res://assets/materials/ground/forest_ground_color.png" ),
			NormalEnabled = true,
			NormalTexture = GD.Load<Texture2D>( "res://assets/materials/ground/forest_ground_normal.png" ),
			RoughnessTexture = GD.Load<Texture2D>( "res://assets/materials/ground/forest_ground_rough.png" ),
			Roughness = 1f,
			Uv1Scale = new Vector3( 51.2f, 51.2f, 1f ),
		};
		floor.AddChild( new MeshInstance3D { Name = "Ground Visual", Mesh = new BoxMesh { Size = new Vector3( size, 20f, size ) * U.In }, MaterialOverride = material } );
	}

	/// <summary> Invisible walls round the edge so nobody walks off the world. </summary>
	void BuildBoundary()
	{
		var reach = HalfSize + 60f;
		var thick = 300f;
		var tall = 4000f;
		void Wall( Vector3 at, Vector3 sboxSize )
		{
			var node = new Node3D { Name = "Boundary" };
			AddChild( node );
			node.Position = U.Sbox( at );
			Solid( node, new BoxShape3D { Size = new Vector3( sboxSize.Y, sboxSize.Z, sboxSize.X ) * U.In }, Vector3.Zero );
		}
		Wall( new Vector3( reach + thick / 2f, 0, tall / 2f ), new Vector3( thick, reach * 2f + thick * 2f, tall ) );
		Wall( new Vector3( -reach - thick / 2f, 0, tall / 2f ), new Vector3( thick, reach * 2f + thick * 2f, tall ) );
		Wall( new Vector3( 0, reach + thick / 2f, tall / 2f ), new Vector3( reach * 2f + thick * 2f, thick, tall ) );
		Wall( new Vector3( 0, -reach - thick / 2f, tall / 2f ), new Vector3( reach * 2f + thick * 2f, thick, tall ) );
	}

	void PlantTree( Vector2 p, float scale )
	{
		var kind = rnd.Next( 3 );
		Node3D trunk;
		var at = new Vector3( p.X, p.Y, 0f );
		if ( kind == 2 )
		{
			trunk = Put( "tree_trunk_c", at, R( 0, 360 ), new Vector3( scale, scale, scale * R( 0.9f, 1.15f ) ), TrunkTints[rnd.Next( TrunkTints.Length )], "Pine Trunk" );
			Put( "tree_canopy_c", at + new Vector3( 0, 0, 560f * scale ), R( 0, 360 ), Vector3.One * scale * R( 0.9f, 1.15f ), PineTints[rnd.Next( PineTints.Length )], "Pine Canopy" );
		}
		else
		{
			var useB = kind == 1;
			var trunkTop = useB ? 500f : 385f;
			trunk = Put( useB ? "tree_trunk_b" : "tree_trunk_a", at, R( 0, 360 ), new Vector3( scale, scale, scale * R( 0.92f, 1.12f ) ), TrunkTints[rnd.Next( TrunkTints.Length )], "Tree Trunk" );
			Put( useB ? "tree_canopy_b" : "tree_canopy_a", at + new Vector3( 0, 0, trunkTop * scale ), R( 0, 360 ), Vector3.One * scale * R( 0.85f, 1.1f ), LeafTints[rnd.Next( LeafTints.Length )], "Canopy" );
		}
		// Capsule from the ground to 260 up, radius 20 (in the trunk's own scaled space, like the s&box collider).
		Solid( trunk, new CapsuleShape3D { Radius = 20f * U.In, Height = 300f * U.In }, new Vector3( 0, 0, 130f ) );
		Block( p, 66f * scale );
	}

	void TryTree()
	{
		var p = RandomPoint();
		var chance = MathX.Clamp( (Woodland( p ) - 0.38f) / 0.3f, 0f, 1f ) * 0.95f + 0.04f;
		if ( rnd.NextDouble() > chance ) return;
		var scale = R( 0.85f, 1.4f );
		if ( !Free( p, 70f * scale ) ) return;
		PlantTree( p, scale );
	}

	void TryRock()
	{
		var p = RandomPoint();
		var chance = Rocky( p ) > 0.6f ? 0.7f : 0.04f;
		if ( rnd.NextDouble() > chance ) return;
		var sc = R( 0.6f, 1.8f );
		if ( !Free( p, 60f * sc ) ) return;
		var go = Put( rnd.NextDouble() < 0.5 ? "rock_a" : "rock_b", new Vector3( p.X, p.Y, 50f * sc ), R( 0, 360 ), new Vector3( sc, sc, sc * R( 0.8f, 1.1f ) ), RockTints[rnd.Next( RockTints.Length )], "Boulder" );
		Solid( go, new SphereShape3D { Radius = 45f * U.In }, Vector3.Zero );
		Block( p, 55f * sc );
	}

	void TryLog()
	{
		var p = RandomPoint();
		if ( !Free( p, 110f ) ) return;
		var sc = R( 0.8f, 1.4f );
		var go = Put( "log_a", new Vector3( p.X, p.Y, 0f ), R( 0, 360 ), Vector3.One * sc, TrunkTints[rnd.Next( TrunkTints.Length )], "Log" );
		Solid( go, new BoxShape3D { Size = new Vector3( 36f, 36f, 120f ) * U.In }, Vector3.Zero );
		Block( p, 95f * sc );
	}

	void BuildEdgeWoods()
	{
		var step = 190f;
		for ( var t = -HalfSize + 100f; t <= HalfSize - 100f; t += step )
		{
			foreach ( var p in new[]
			{
				new Vector2( t, HalfSize - R( 120f, 360f ) ), new Vector2( t, -HalfSize + R( 120f, 360f ) ),
				new Vector2( HalfSize - R( 120f, 360f ), t ), new Vector2( -HalfSize + R( 120f, 360f ), t ),
			} )
				PlantTree( p, R( 1.0f, 1.5f ) );
		}
	}
}

/// <summary>
/// Port of s&box ForestLook: gives forest props their nature material (nature.gdshader) and a colour from a curated
/// palette. Palette entries are the sRGB colour wanted on screen, converted to a linear tint on the mid-grey textures.
/// </summary>
public static class ForestLook
{
	static Color[] Broadleaf => new[] { C( 0.22f, 0.40f, 0.12f ), C( 0.30f, 0.50f, 0.14f ), C( 0.42f, 0.58f, 0.16f ), C( 0.36f, 0.42f, 0.14f ), C( 0.26f, 0.44f, 0.18f ) };
	static Color[] Autumn => new[] { C( 0.72f, 0.42f, 0.12f ), C( 0.62f, 0.24f, 0.10f ), C( 0.70f, 0.58f, 0.16f ) };
	static Color[] Pine => new[] { C( 0.12f, 0.28f, 0.16f ), C( 0.15f, 0.33f, 0.18f ), C( 0.10f, 0.24f, 0.17f ) };
	static Color[] Bark => new[] { C( 0.36f, 0.30f, 0.25f ), C( 0.30f, 0.26f, 0.22f ), C( 0.42f, 0.36f, 0.29f ) };
	static Color PineBark => C( 0.40f, 0.30f, 0.24f );
	static Color[] Bush => new[] { C( 0.20f, 0.38f, 0.12f ), C( 0.40f, 0.42f, 0.18f ), C( 0.18f, 0.36f, 0.28f ), C( 0.16f, 0.30f, 0.12f ), C( 0.30f, 0.44f, 0.20f ) };
	static Color[] Grass => new[] { C( 0.32f, 0.50f, 0.14f ), C( 0.40f, 0.56f, 0.16f ), C( 0.28f, 0.44f, 0.12f ) };
	static Color[] Fern => new[] { C( 0.18f, 0.40f, 0.14f ), C( 0.24f, 0.46f, 0.16f ) };
	static Color[] Blossom => new[] { C( 0.80f, 0.42f, 0.56f ), C( 0.60f, 0.48f, 0.80f ), C( 0.82f, 0.66f, 0.24f ) };
	static Color[] Rock => new[] { C( 0.46f, 0.45f, 0.42f ), C( 0.40f, 0.40f, 0.38f ), C( 0.52f, 0.50f, 0.46f ) };
	static Color Hill => C( 0.42f, 0.42f, 0.40f );

	static Color C( float r, float g, float b )
	{
		const float lift = 2.6f;
		static float L( float c ) => MathF.Pow( c, 2.2f ) * lift;
		return new Color( L( r ), L( g ), L( b ) );
	}

	static T Pick<T>( Random rnd, T[] list ) => list[rnd.Next( list.Length )];

	/// <summary> Restyles a forest prop instance by its model name. False if it isn't a forest prop. </summary>
	public static bool Apply( Node3D instance, string name, float scale, Color fallbackTint, Random rnd )
	{
		string mat;
		Color tint;
		if ( name.StartsWith( "tree_trunk" ) ) { mat = "bark"; tint = name == "tree_trunk_c" ? PineBark : Pick( rnd, Bark ); }
		else if ( name == "tree_canopy_c" ) { mat = "pine"; tint = Pick( rnd, Pine ); }
		else if ( name.StartsWith( "tree_canopy" ) ) { mat = "leaves"; tint = rnd.NextDouble() < 0.06 ? Pick( rnd, Autumn ) : Pick( rnd, Broadleaf ); }
		else if ( name.StartsWith( "bush" ) ) { mat = "bush"; tint = Pick( rnd, Bush ); }
		else if ( name.StartsWith( "flowerbush" ) ) { mat = "blossom"; tint = Pick( rnd, Blossom ); }
		else if ( name.StartsWith( "grass" ) ) { mat = "grass"; tint = Pick( rnd, Grass ); }
		else if ( name.StartsWith( "fern" ) ) { mat = "grass"; tint = Pick( rnd, Fern ); }
		else if ( name.StartsWith( "log" ) || name.StartsWith( "stump" ) ) { mat = "log"; tint = Pick( rnd, Bark ); }
		else if ( name.StartsWith( "rock" ) ) { var hill = scale > 4f; mat = hill ? "hill" : "rock"; tint = hill ? Hill : Pick( rnd, Rock ); }
		else return false;

		// Small per-instance brightness jitter, and tints above 1 scaled down keeping the hue (as in s&box).
		var j = 0.9f + (float)rnd.NextDouble() * 0.2f;
		var c = new Color( tint.R * j, tint.G * j, tint.B * j );
		var max = MathF.Max( c.R, MathF.Max( c.G, c.B ) );
		if ( max > 1f ) c = new Color( c.R / max, c.G / max, c.B / max );
		var material = Level.NatureMaterial( mat );
		foreach ( var mesh in instance.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			if ( material is not null ) { mesh.MaterialOverride = material; mesh.SetInstanceShaderParameter( "tint", new Vector3( c.R, c.G, c.B ) ); }
			else mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = fallbackTint };
		}
		return true;
	}
}
