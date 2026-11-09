/// <summary>
/// The crater the dive punches into the seabed: a real mesh that hugs the ground (a churned bowl inside a raised,
/// lumpy rim of thrown sand), rim boulders, pale ejecta rays out across the seabed, and a glowing hot core with
/// cracks that cool off while vents bubble.
/// </summary>
public static class DiveCrater
{
	static Material plain;
	static Material Plain => plain ??= Material.Load( "materials/default/white.vmat" );
	static Material sand;
	static Material Sand => sand ??= Material.Load( "materials/ground/seabed.vmat" ) ?? Plain;

	/// <summary> Profile seeds, so one crater has one shape. </summary>
	struct Shape
	{
		public float Radius, Rim, P1, P2, P3;

		/// <summary> Height above the ground at radius fraction x (of Radius) and angle a. </summary>
		public float Height( float x, float a )
		{
			var lump = 1f + 0.28f * MathF.Sin( 3f * a + P1 ) + 0.16f * MathF.Sin( 7f * a + P2 ) + 0.08f * MathF.Sin( 13f * a + P3 );
			var rim = Rim * lump;
			if ( x < 0.75f ) return 5f + rim * 0.22f * MathF.Pow( x / 0.75f, 3f );
			if ( x < 1f ) return 5f + rim * (0.22f + 0.78f * Smooth( (x - 0.75f) / 0.25f ));
			return rim * (1f - Smooth( (x - 1f) / 0.7f )) + 5f * (1f - Smooth( x - 1f )) - 12f * Smooth( (x - 1.55f) / 0.4f );
		}

		static float Smooth( float t )
		{
			t = MathX.Clamp( t, 0f, 1f );
			return t * t * (3f - 2f * t);
		}
	}

	static float Ground( Scene scene, Vector3 p, float fallback )
	{
		var tr = scene.Trace.Ray( p.WithZ( fallback + 300f ), p.WithZ( fallback - 300f ) ).WithoutTags( "player", "trigger", "creature", "gib" ).Run();
		// Clamped, so a boulder or kelp under a vertex does not throw up a spike.
		return tr.Hit ? MathX.Clamp( tr.HitPosition.z, fallback - 30f, fallback + 30f ) : fallback;
	}

	public static GameObject Build( Scene scene, Vector3 at, float power )
	{
		var shape = new Shape
		{
			Radius = 170f + 100f * power,
			Rim = 42f + 46f * power,
			P1 = Game.Random.Float( 0f, 6.28f ),
			P2 = Game.Random.Float( 0f, 6.28f ),
			P3 = Game.Random.Float( 0f, 6.28f ),
		};
		var root = new GameObject( true, "Dive Crater" );
		root.NetworkMode = NetworkMode.Never;
		root.WorldPosition = at;

		// The blast clears the small stuff: kelp, coral and pebbles inside the rim are gone.
		foreach ( var r in scene.GetAllComponents<ModelRenderer>().ToList() )
		{
			var go = r.GameObject;
			if ( go.Name is not ("Kelp" or "Coral" or "Pebbles") ) continue;
			if ( go.WorldPosition.WithZ( 0 ).Distance( at.WithZ( 0 ) ) < shape.Radius * 1.5f ) go.Enabled = false;
		}

		// Ground height under every vertex, so the crater lies on slopes instead of floating or burying itself.
		const int sides = 64;
		const float outer = 1.95f;
		const int rings = 30;
		var grid = new Vector3[rings + 1, sides + 1];
		for ( var j = 0; j <= rings; j++ )
		{
			var x = outer * j / rings;
			for ( var i = 0; i <= sides; i++ )
			{
				var a = i * MathF.PI * 2f / sides;
				var local = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f ) * x * shape.Radius;
				var ground = Ground( scene, at + local, at.z ) - at.z;
				grid[j, i] = local.WithZ( ground + shape.Height( x, a ) );
			}
		}

		// Three bands: charred core, scorched bowl, sand rim. They share their edge rings so there are no seams.
		Band( root, "Crater Core", grid, 0, Ring( 0.62f, outer, rings ), sides, new Color( 0.14f, 0.1f, 0.08f ) );
		Band( root, "Crater Bowl", grid, Ring( 0.62f, outer, rings ), Ring( 1.02f, outer, rings ), sides, new Color( 0.42f, 0.34f, 0.27f ) );
		Band( root, "Crater Rim", grid, Ring( 1.02f, outer, rings ), rings, sides, new Color( 1f, 0.97f, 0.92f ) );

		// Boulders sitting in the rim, tipped outward.
		var rocks = (int)(12 + 10 * power);
		for ( var k = 0; k < rocks; k++ )
		{
			var a = k * MathF.PI * 2f / rocks + Game.Random.Float( -0.2f, 0.2f );
			var x = Game.Random.Float( 0.9f, 1.4f );
			var local = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f ) * x * shape.Radius;
			var z = Ground( scene, at + local, at.z ) - at.z + shape.Height( x, a );
			var size = new Vector3( Game.Random.Float( 20f, 44f ), Game.Random.Float( 16f, 32f ), Game.Random.Float( 14f, 30f ) ) * (0.8f + 0.4f * power);
			var rock = new GameObject( root, true, "Rim Boulder" );
			rock.LocalPosition = local.WithZ( z + size.z * 0.1f );
			rock.LocalRotation = Rotation.FromYaw( a.RadianToDegree() ) * Rotation.FromPitch( Game.Random.Float( -45f, -15f ) ) * Rotation.FromRoll( Game.Random.Float( -25f, 25f ) );
			rock.LocalScale = Vector3.One * (size.x / 100f);
			var rr = rock.AddComponent<ModelRenderer>();
			rr.Model = Boulder();
			rr.Tint = Color.Lerp( new Color( 0.9f, 0.85f, 0.75f ), new Color( 0.55f, 0.5f, 0.45f ), Game.Random.Float() );
		}

		// Ejecta rays: pale streaks of thrown sand fanning out across the seabed.
		var rays = 16;
		for ( var k = 0; k < rays; k++ )
		{
			var a = k * MathF.PI * 2f / rays + Game.Random.Float( -0.15f, 0.15f );
			var dir = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f );
			var len = shape.Radius * Game.Random.Float( 1.2f, 2.6f ) * (0.7f + 0.5f * power);
			var segs = 4;
			for ( var s = 0; s < segs; s++ )
			{
				var t0 = 1.75f * shape.Radius + len * s / segs;
				var mid = dir * (t0 + len / segs * 0.5f);
				var z = Ground( scene, at + mid, at.z ) - at.z + 0.6f;
				var ray = new GameObject( root, true, "Ejecta Ray" );
				ray.LocalPosition = mid.WithZ( z );
				ray.LocalRotation = Rotation.FromYaw( a.RadianToDegree() );
				var w = Game.Random.Float( 8f, 18f ) * (1f - s / (float)segs * 0.7f);
				ray.LocalScale = new Vector3( len / segs * 0.6f, w, 0.5f ) * Shapes.SphereScale( 1f );
				var r = ray.AddComponent<ModelRenderer>();
				r.Model = Shapes.Sphere;
				r.Tint = new Color( 0.42f, 0.37f, 0.29f );
				r.RenderType = ModelRenderer.ShadowRenderType.Off;
			}
		}

		// The hot core: glowing cracks across the floor and a red light, cooling over a few seconds.
		var heat = root.AddComponent<CraterHeat>();
		var glow = Material.Load( "materials/creature/glowbulb.vmat" );
		for ( var k = 0; k < 11; k++ )
		{
			var a = k * MathF.PI * 2f / 11f + Game.Random.Float( -0.25f, 0.25f );
			var len = shape.Radius * Game.Random.Float( 0.45f, 0.85f );
			var mid = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f ) * len * 0.5f;
			var crack = new GameObject( root, true, "Hot Crack" );
			crack.LocalPosition = mid.WithZ( Ground( scene, at + mid, at.z ) - at.z + shape.Height( len * 0.5f / shape.Radius, a ) + 0.4f );
			crack.LocalRotation = Rotation.FromYaw( a.RadianToDegree() + Game.Random.Float( -8f, 8f ) );
			crack.LocalScale = Shapes.BoxScale( new Vector3( len, Game.Random.Float( 2.5f, 6f ), 1.2f ) );
			var r = crack.AddComponent<ModelRenderer>();
			r.Model = Shapes.Box;
			r.RenderType = ModelRenderer.ShadowRenderType.Off;
			if ( glow is not null && !glow.IsError ) r.MaterialOverride = glow;
			heat.Cracks.Add( r );
		}
		var lightGo = new GameObject( root, true, "Core Glow" );
		lightGo.LocalPosition = Vector3.Up * 40f;
		heat.Light = lightGo.AddComponent<PointLight>();
		heat.Light.Radius = shape.Radius * 3f;
		heat.Vents = Enumerable.Range( 0, 5 ).Select( _ =>
		{
			var v = Game.Random.VectorInCircle( shape.Radius * 0.6f );
			var p = new Vector3( v.x, v.y, 0f );
			return at + p.WithZ( Ground( scene, at + p, at.z ) - at.z + 2f );
		} ).ToList();
		return root;
	}

	/// <summary> One of the seabed boulder models, so rim rocks match the rest of the level. </summary>
	public static Model Boulder() => Model.Load( $"models/arena/boulder{Game.Random.Int( 0, 2 )}.vmdl" );

	static int Ring( float x, float outer, int rings ) => (int)MathF.Round( x / outer * rings );

	static void Band( GameObject root, string name, Vector3[,] grid, int j0, int j1, int sides, Color tint )
	{
		var verts = new List<Vertex>();
		var indices = new List<int>();
		var rings = grid.GetLength( 0 ) - 1;
		for ( var j = j0; j <= j1; j++ )
			for ( var i = 0; i <= sides; i++ )
			{
				var p = grid[j, i];
				var inR = grid[Math.Min( rings, j + 1 ), i] - grid[Math.Max( 0, j - 1 ), i];
				var around = grid[j, (i + 1) % sides] - grid[j, (i + sides - 1) % sides];
				var n = Vector3.Cross( inR, around ).Normal;
				if ( n.z < 0f ) n = -n;
				if ( n.IsNearlyZero() ) n = Vector3.Up;
				verts.Add( new Vertex( p, n, around.Normal, new Vector4( p.x / 300f, p.y / 300f, 0, 0 ) ) );
			}
		for ( var j = 0; j < j1 - j0; j++ )
			for ( var i = 0; i < sides; i++ )
			{
				var a = j * (sides + 1) + i;
				var b = a + sides + 1;
				// Wound so the top faces up.
				indices.AddRange( new[] { a, b, a + 1, a + 1, b, b + 1 } );
			}
		var mesh = new Mesh( Sand );
		mesh.CreateVertexBuffer( verts.Count, verts );
		mesh.CreateIndexBuffer( indices.Count, indices );
		var min = verts.Aggregate( new Vector3( float.MaxValue ), ( m, v ) => Vector3.Min( m, v.Position ) );
		var max = verts.Aggregate( new Vector3( float.MinValue ), ( m, v ) => Vector3.Max( m, v.Position ) );
		mesh.Bounds = new BBox( min, max );
		var go = new GameObject( root, true, name );
		go.NetworkMode = NetworkMode.Never;
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Model.Builder.AddMesh( mesh ).Create();
		r.Tint = tint;
	}
}

/// <summary> The crater core cooling: crack glow and light fade from white-hot to a dull ember, vents bubble. </summary>
public sealed class CraterHeat : Component
{
	public List<ModelRenderer> Cracks { get; } = new();
	public PointLight Light { get; set; }
	public List<Vector3> Vents { get; set; } = new();
	TimeSince age;
	TimeSince lastBubble;

	protected override void OnStart() => age = 0f;

	protected override void OnUpdate()
	{
		var t = (float)age;
		// Fast flash, then a long cool down to a faint ember that stays.
		var heat = 0.12f + 0.88f * MathF.Exp( -t / 3.5f );
		var flicker = 0.9f + 0.1f * MathF.Sin( t * 23f ) * MathF.Sin( t * 7.3f );
		var col = Color.Lerp( new Color( 0.9f, 0.18f, 0.04f ), new Color( 1f, 0.75f, 0.35f ), MathX.Clamp( heat * 1.3f - 0.2f, 0f, 1f ) );
		foreach ( var r in Cracks )
		{
			if ( !r.IsValid() ) continue;
			r.Tint = col * (1f + 3f * heat);
			r.Attributes.Set( "GlowColor", new Vector3( col.r, col.g, col.b ) );
			r.Attributes.Set( "GlowStrength", 6f * heat * flicker );
		}
		if ( Light.IsValid() ) Light.LightColor = col * (6f * heat * flicker);

		// Vents: streams of bubbles while it is still hot.
		if ( t < 14f && lastBubble > 0.05f + t * 0.01f )
		{
			lastBubble = 0f;
			foreach ( var v in Vents )
				if ( Game.Random.Float() < 0.6f )
					WaterBubble.Create( v + Game.Random.VectorInSphere( 6f ).WithZ( 0 ), Game.Random.Float( 0.8f, 2.4f ), 0f, Game.Random.Float( 1.5f, 3f ) );
		}
	}
}
