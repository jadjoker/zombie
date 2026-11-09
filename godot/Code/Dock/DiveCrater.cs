using Godot;

/// <summary>
/// Port of s&box DiveCrater: the crater the dive punches into the seabed. A mesh that hugs the ground (a charred core,
/// a scorched bowl, a raised lumpy rim of thrown sand), boulders sitting in the rim, pale ejecta rays fanning out, and
/// a glowing hot core with cracks that cool while vents bubble. Kelp, coral and pebbles inside the rim are blasted
/// away. Sizes in inches.
/// </summary>
public static class DiveCrater
{
	struct Shape
	{
		public float Radius, Rim, P1, P2, P3;

		public float Height( float x, float a )
		{
			var lump = 1f + 0.28f * MathF.Sin( 3f * a + P1 ) + 0.16f * MathF.Sin( 7f * a + P2 ) + 0.08f * MathF.Sin( 13f * a + P3 );
			var rim = Rim * lump;
			if ( x < 0.75f ) return 9f + rim * 0.22f * MathF.Pow( x / 0.75f, 3f );
			if ( x < 1f ) return 9f + rim * (0.22f + 0.78f * Smooth( (x - 0.75f) / 0.25f ));
			return rim * (1f - Smooth( (x - 1f) / 0.7f )) + 9f * (1f - Smooth( x - 1f )) - 12f * Smooth( (x - 1.55f) / 0.4f );
		}

		static float Smooth( float t )
		{
			t = MathX.Clamp( t, 0f, 1f );
			return t * t * (3f - 2f * t);
		}
	}

	static Material sand;
	static Material Sand => sand ??= MakeSand();

	static Material MakeSand()
	{
		var m = new StandardMaterial3D { Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		if ( ResourceLoader.Exists( "res://assets/materials/ground/seabed_color.png" ) ) m.AlbedoTexture = GD.Load<Texture2D>( "res://assets/materials/ground/seabed_color.png" );
		if ( ResourceLoader.Exists( "res://assets/materials/ground/seabed_normal.png" ) ) { m.NormalEnabled = true; m.NormalTexture = GD.Load<Texture2D>( "res://assets/materials/ground/seabed_normal.png" ); }
		m.VertexColorUseAsAlbedo = true;
		return m;
	}

	static readonly PackedScene[] boulders = new PackedScene[3];

	/// <summary> One of the seabed boulder models at a scale (of the s&box model), tinted. </summary>
	public static Node3D Boulder( float scale, Color tint )
	{
		var i = Rng.Int( 0, 2 );
		boulders[i] ??= GD.Load<PackedScene>( $"res://assets/models/arena/boulder{i}.fbx" );
		var node = boulders[i].Instantiate<Node3D>();
		node.Scale = Vector3.One * 2.54f * scale;
		var material = new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.9f };
		if ( ResourceLoader.Exists( "res://assets/materials/arena/rock_color.png" ) ) material.AlbedoTexture = GD.Load<Texture2D>( "res://assets/materials/arena/rock_color.png" );
		foreach ( var mesh in node.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() ) mesh.MaterialOverride = material;
		return node;
	}

	static float Ground( Node3D context, Vector3 p, float fallback )
	{
		var tr = Trace.Ray( context, p.WithY( fallback + 300f * U.In ), p.WithY( fallback - 300f * U.In ), Layers.World );
		return tr.Hit ? MathX.Clamp( tr.HitPosition.Y, fallback - 30f * U.In, fallback + 30f * U.In ) : fallback;
	}

	public static Node3D Build( Node3D context, Vector3 at, float power )
	{
		var shape = new Shape { Radius = 170f + 100f * power, Rim = 42f + 46f * power, P1 = Rng.Float( 0f, 6.28f ), P2 = Rng.Float( 0f, 6.28f ), P3 = Rng.Float( 0f, 6.28f ) };
		var scene = context.GetTree().CurrentScene;
		var root = new Node3D { Name = "Dive Crater" };
		scene.AddChild( root );
		root.GlobalPosition = at;

		// The blast clears the small stuff inside the rim.
		foreach ( var node in scene.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>().ToList() )
		{
			var name = node.Name.ToString();
			if ( !(name.StartsWith( "Kelp" ) || name.StartsWith( "Coral" ) || name.StartsWith( "Pebbles" )) ) continue;
			if ( node.GlobalPosition.Flat().DistanceTo( at.Flat() ) < shape.Radius * 1.5f * U.In ) node.Visible = false;
		}

		const int sides = 64;
		const float outer = 1.95f;
		const int rings = 30;
		var grid = new Vector3[rings + 1, sides + 1];
		for ( var j = 0; j <= rings; j++ )
		{
			var x = outer * j / rings;
			for ( var i = 0; i <= sides; i++ )
			{
				var a = i * MathF.Tau / sides;
				var local = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * x * shape.Radius * U.In;
				var ground = Ground( context, at + local, at.Y ) - at.Y;
				grid[j, i] = local.WithY( ground + shape.Height( x, a ) * U.In );
			}
		}
		int Ring( float x ) => (int)MathF.Round( x / outer * rings );
		Band( root, grid, 0, Ring( 0.62f ), sides, new Color( 0.14f, 0.1f, 0.08f ) );
		Band( root, grid, Ring( 0.62f ), Ring( 1.02f ), sides, new Color( 0.42f, 0.34f, 0.27f ) );
		Band( root, grid, Ring( 1.02f ), rings, sides, new Color( 1f, 0.97f, 0.92f ) );

		var rocks = (int)(12 + 10 * power);
		for ( var k = 0; k < rocks; k++ )
		{
			var a = k * MathF.Tau / rocks + Rng.Float( -0.2f, 0.2f );
			var x = Rng.Float( 0.9f, 1.4f );
			var local = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * x * shape.Radius * U.In;
			var y = Ground( context, at + local, at.Y ) - at.Y + shape.Height( x, a ) * U.In;
			var size = Rng.Float( 20f, 44f ) * (0.8f + 0.4f * power);
			var rock = Boulder( size / 100f, new Color( 0.9f, 0.85f, 0.75f ).Lerp( new Color( 0.55f, 0.5f, 0.45f ), Rng.Float() ) );
			root.AddChild( rock );
			rock.Position = local.WithY( y );
			rock.Rotation = new Vector3( MathX.Rad( Rng.Float( -45f, -15f ) ), -a, MathX.Rad( Rng.Float( -25f, 25f ) ) );
		}

		var rayMaterial = new StandardMaterial3D { AlbedoColor = new Color( 0.42f, 0.37f, 0.29f ) };
		for ( var k = 0; k < 16; k++ )
		{
			var a = k * MathF.Tau / 16f + Rng.Float( -0.15f, 0.15f );
			var dir = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			var len = shape.Radius * Rng.Float( 1.2f, 2.6f ) * (0.7f + 0.5f * power);
			for ( var s = 0; s < 4; s++ )
			{
				var t0 = 1.75f * shape.Radius + len * s / 4f;
				var mid = dir * (t0 + len / 8f) * U.In;
				var y = Ground( context, at + mid, at.Y ) - at.Y + 0.6f * U.In;
				var w = Rng.Float( 8f, 18f ) * (1f - s / 4f * 0.7f);
				var ray = new MeshInstance3D { Mesh = Shapes.Sphere, MaterialOverride = rayMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
				root.AddChild( ray );
				ray.Transform = new Transform3D( Shapes.LookAt( dir ) * Basis.FromScale( new Vector3( w, 0.5f, len / 4f * 0.6f ) * 2f * U.In ), mid.WithY( y ) );
			}
		}

		var heat = new CraterHeat { Name = "Heat" };
		root.AddChild( heat );
		for ( var k = 0; k < 11; k++ )
		{
			var a = k * MathF.Tau / 11f + Rng.Float( -0.25f, 0.25f );
			var len = shape.Radius * Rng.Float( 0.45f, 0.85f );
			var dir = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			var mid = dir * len * 0.5f * U.In;
			var crackMat = new StandardMaterial3D { AlbedoColor = new Color( 1f, 0.6f, 0.2f ), EmissionEnabled = true, Emission = new Color( 1f, 0.6f, 0.2f ), EmissionEnergyMultiplier = 4f };
			var crack = new MeshInstance3D { Mesh = Shapes.Box, MaterialOverride = crackMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			root.AddChild( crack );
			var y = Ground( context, at + mid, at.Y ) - at.Y + shape.Height( len * 0.5f / shape.Radius, a ) * U.In + 0.4f * U.In;
			crack.Transform = new Transform3D( Shapes.LookAt( dir.Rotated( Vector3.Up, MathX.Rad( Rng.Float( -8f, 8f ) ) ) ) * Basis.FromScale( new Vector3( Rng.Float( 2.5f, 6f ), 1.2f, len ) * U.In ), mid.WithY( y ) );
			heat.Cracks.Add( crackMat );
		}
		heat.Light = new OmniLight3D { OmniRange = shape.Radius * 3f * U.In, ShadowEnabled = false };
		root.AddChild( heat.Light );
		heat.Light.Position = Vector3.Up * 40f * U.In;
		for ( var v = 0; v < 5; v++ )
		{
			var a = Rng.Float( 0f, MathF.Tau );
			var p = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * Rng.Float( 0f, shape.Radius * 0.6f ) * U.In;
			heat.Vents.Add( at + p.WithY( Ground( context, at + p, at.Y ) - at.Y + 2f * U.In ) );
		}
		return root;
	}

	static void Band( Node3D root, Vector3[,] grid, int j0, int j1, int sides, Color tint )
	{
		var rings = grid.GetLength( 0 ) - 1;
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		for ( var j = j0; j < j1; j++ )
		{
			for ( var i = 0; i < sides; i++ )
			{
				void V( int jj, int ii )
				{
					var p = grid[jj, ii];
					st.SetColor( tint );
					st.SetUV( new Vector2( p.X, p.Z ) / (300f * U.In) );
					st.AddVertex( p );
				}
				V( j, i ); V( j + 1, i ); V( j, i + 1 );
				V( j, i + 1 ); V( j + 1, i ); V( j + 1, i + 1 );
			}
		}
		st.GenerateNormals();
		st.GenerateTangents();
		root.AddChild( new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Sand } );
	}
}

/// <summary> The crater core cooling: crack glow and light fade from white-hot to a dull ember, vents bubble. </summary>
public partial class CraterHeat : Node
{
	public List<StandardMaterial3D> Cracks { get; } = new();
	public OmniLight3D Light;
	public List<Vector3> Vents = new();
	TimeSince age;
	TimeSince lastBubble;

	public override void _Ready() => age = 0f;

	public override void _Process( double delta )
	{
		var t = (float)age;
		var heat = 0.12f + 0.88f * MathF.Exp( -t / 3.5f );
		var flicker = 0.9f + 0.1f * MathF.Sin( t * 23f ) * MathF.Sin( t * 7.3f );
		var col = new Color( 0.9f, 0.18f, 0.04f ).Lerp( new Color( 1f, 0.75f, 0.35f ), MathX.Clamp( heat * 1.3f - 0.2f, 0f, 1f ) );
		foreach ( var m in Cracks )
		{
			m.AlbedoColor = col;
			m.Emission = col;
			m.EmissionEnergyMultiplier = 6f * heat * flicker;
		}
		if ( Light is not null ) { Light.LightColor = col; Light.LightEnergy = 6f * heat * flicker; }
		if ( t < 14f && lastBubble > 0.05f + t * 0.01f )
		{
			lastBubble = 0f;
			foreach ( var v in Vents )
				if ( Rng.Float() < 0.6f ) WaterBubble.Create( v + Rng.InSphere( 6f * U.In ).Flat(), Rng.Float( 0.8f, 2.4f ), 0f, Rng.Float( 1.5f, 3f ) );
		}
	}
}

/// <summary> One piece of crater debris: flies its own arc through the water (s&box units/s), lands, bounces once, settles. </summary>
public partial class DebrisChunk : Node3D
{
	public Vector3 Velocity;
	public Vector3 Spin;
	public float Size = 8f;
	public bool Trail;
	bool landed;
	TimeSince lastBubble;

	public override void _Process( double delta )
	{
		if ( landed ) return;
		var dt = (float)delta;
		Velocity += Vector3.Down * 520f * dt;
		Velocity *= MathF.Exp( -0.7f * dt );
		var from = GlobalPosition;
		var to = from + Velocity * U.In * dt;
		if ( Velocity.Y < 0f )
		{
			var tr = Trace.Ray( this, from, to + Vector3.Down * Size * 0.4f * U.In, Layers.World );
			if ( tr.Hit )
			{
				if ( Velocity.Length() > 160f && Size > 7f )
				{
					GlobalPosition = tr.HitPosition + Vector3.Up * Size * 0.4f * U.In;
					Velocity = Velocity.WithY( -Velocity.Y * 0.3f ) * 0.45f;
					Spin *= 0.5f;
					return;
				}
				GlobalPosition = tr.HitPosition + Vector3.Up * Size * 0.3f * U.In;
				landed = true;
				return;
			}
		}
		GlobalPosition = to;
		Rotation += Spin * dt;
		if ( Trail && lastBubble > 0.06f )
		{
			lastBubble = 0f;
			WaterBubble.Create( GlobalPosition, Rng.Float( 1f, 2.4f ), 0f, 1.2f );
		}
	}
}

/// <summary> The sand thrown up by the landing: an expanding ground ring, a rising boil, and a slow haze that settles. </summary>
public partial class SandCloud : Node3D
{
	public float Radius = 150f;
	public float Power = 1f;
	MeshInstance3D ring;
	TimeSince age;

	public override void _Ready()
	{
		age = 0f;
		ring = new MeshInstance3D { Mesh = Shapes.Sphere, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color( 0.8f, 0.72f, 0.55f ) }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( ring );
		CallDeferred( MethodName.EmitPuffs );   // once it has been placed
	}

	void EmitPuffs()
	{
		var sand = new Color( 0.62f, 0.54f, 0.4f );
		var count = (int)(300 * Power) + 80;
		for ( var i = 0; i < count; i++ )
		{
			var a = Rng.Float( 0f, MathF.Tau );
			var outward = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			var rising = i % 2 == 0;
			var at = GlobalPosition + (outward * Rng.Float( 0f, Radius * (rising ? 0.5f : 1f) ) + Vector3.Up * Rng.Float( 4f, 30f )) * U.In;
			var vel = rising
				? outward * Rng.Float( 20f, 140f ) + Vector3.Up * Rng.Float( 180f, 520f ) * Power
				: outward * Rng.Float( 260f, 640f ) * Power + Vector3.Up * Rng.Float( 20f, 90f );
			// Step 8: GPU puffs (sand_puffs.gdshader does the drag, buoyancy, growth and fade).
			GpuFx.SandPuff( at, vel * U.In, sand.Lerp( new Color( 0.42f, 0.36f, 0.28f ), Rng.Float() ), Rng.Float( 4f, 12f ) * (0.7f + 0.5f * Power), Rng.Float( 1.4f, 3.2f ) );
		}
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var t = (float)age;
		var spread = 1f - MathF.Exp( -4.5f * t );
		var d = Radius * 0.6f + Radius * 5.5f * spread * Power;
		var ringFade = MathX.Clamp( 1f - t / 0.9f, 0f, 1f );
		ring.Position = Vector3.Up * 6f * U.In;
		ring.Scale = new Vector3( d, 14f * ringFade + 0.01f, d ) * 2f * U.In;
	}
}
