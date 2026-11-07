/// <summary>
/// Shapes built in code for the creatures, beyond plain spheres and boxes. The main one is a
/// "spindle": round at the base, tapering along +X to a point (fangs, spikes, claws) or a blunt end
/// (legs, tentacles), and optionally curving as it goes (fangs and claws hook, horns sweep).
/// Models are made once per shape and shared.
/// </summary>
public static class ShapeMeshes
{
	static readonly Dictionary<(int, int), Model> spindles = new();
	static Material plain;
	static Material Plain => plain ??= Material.Load( "materials/default/white.vmat" );

	/// <summary>
	/// A unit spindle: length 1 along +X, radius 1 at its fattest. tip = end radius as a fraction (0 = sharp
	/// point). bend = how far the tip curves toward +Z (in lengths).
	/// </summary>
	public static Model Spindle( float tip, float bend )
	{
		var key = ((int)MathF.Round( tip * 10f ), (int)MathF.Round( bend * 10f ));
		if ( spindles.TryGetValue( key, out var cached ) && cached is not null ) return cached;
		var model = BuildSpindle( key.Item1 / 10f, key.Item2 / 10f );
		spindles[key] = model;
		return model;
	}

	static Model BuildSpindle( float tip, float bend )
	{
		const int rings = 18, sides = 14;
		var grid = new Vector3[rings + 1, sides + 1];
		for ( var j = 0; j <= rings; j++ )
		{
			var t = j / (float)rings;
			// Round at the base, then tapering to the tip (rounded off if it's blunt).
			var baseRound = t < 0.15f ? MathF.Sqrt( MathF.Max( 0f, 1f - MathF.Pow( 1f - t / 0.15f, 2f ) ) ) : 1f;
			var taper = MathX.Lerp( 1f, MathF.Max( tip, 0.001f ), MathF.Pow( t, 0.9f ) );
			var endRound = tip > 0.05f && t > 0.88f ? MathF.Sqrt( MathF.Max( 0f, 1f - MathF.Pow( (t - 0.88f) / 0.12f, 2f ) ) ) : 1f;
			var r = baseRound * taper * endRound;
			if ( j == 0 || j == rings ) r = 0f;
			var curve = bend * t * t; // hooks more toward the tip
			for ( var i = 0; i <= sides; i++ )
			{
				var a = i / (float)sides * MathF.PI * 2f;
				grid[j, i] = new Vector3( t, MathF.Cos( a ) * r, MathF.Sin( a ) * r + curve );
			}
		}

		var verts = new List<Vertex>();
		var indices = new List<int>();
		for ( var j = 0; j <= rings; j++ )
		{
			for ( var i = 0; i <= sides; i++ )
			{
				// Normal from the neighbouring points on the surface.
				var p = grid[j, i];
				var along = grid[Math.Min( rings, j + 1 ), i] - grid[Math.Max( 0, j - 1 ), i];
				var around = grid[j, (i + 1) % sides] - grid[j, (i + sides - 1) % sides];
				var normal = Vector3.Cross( around, along ).Normal;
				if ( normal.IsNearlyZero() ) normal = j == 0 ? Vector3.Backward : Vector3.Forward;
				verts.Add( new Vertex( p, normal, along.Normal, new Vector4( i / (float)sides, j / (float)rings, 0, 0 ) ) );
			}
		}
		for ( var j = 0; j < rings; j++ )
		{
			for ( var i = 0; i < sides; i++ )
			{
				var a = j * (sides + 1) + i;
				var b = a + sides + 1;
				indices.AddRange( new[] { a, a + 1, b, b, a + 1, b + 1 } );
			}
		}

		var mesh = new Mesh( Plain );
		mesh.CreateVertexBuffer( verts.Count, verts );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.Bounds = new BBox( new Vector3( 0, -1, -1 ), new Vector3( 1, 1, 1 + MathF.Max( 0f, bend ) ) );
		return Model.Builder.AddMesh( mesh ).Create();
	}

	static Model dome;

	/// <summary>
	/// A unit half-sphere: the dome over +Z (radius 1) closed by a flat base at z = 0. Eyelids are made of
	/// these: a dome a bit bigger than the eyeball, swung forward over it.
	/// </summary>
	public static Model Dome => dome ??= BuildDome();

	static Model BuildDome()
	{
		const int rings = 14, sides = 28;
		var verts = new List<Vertex>();
		var indices = new List<int>();
		for ( var j = 0; j <= rings; j++ )
		{
			var polar = j / (float)rings * MathF.PI * 0.5f;      // 0 at the pole, 90 degrees at the rim
			for ( var i = 0; i <= sides; i++ )
			{
				var a = i / (float)sides * MathF.PI * 2f;
				var n = new Vector3( MathF.Sin( polar ) * MathF.Cos( a ), MathF.Sin( polar ) * MathF.Sin( a ), MathF.Cos( polar ) );
				var tangent = new Vector3( -MathF.Sin( a ), MathF.Cos( a ), 0f );
				verts.Add( new Vertex( n, n, tangent, new Vector4( i / (float)sides, j / (float)rings, 0, 0 ) ) );
			}
		}
		for ( var j = 0; j < rings; j++ )
			for ( var i = 0; i < sides; i++ )
			{
				var a = j * (sides + 1) + i;
				var b = a + sides + 1;
				indices.AddRange( new[] { a, b, a + 1, a + 1, b, b + 1 } );
			}
		// Flat base, facing down.
		var center = verts.Count;
		verts.Add( new Vertex( Vector3.Zero, Vector3.Down, Vector3.Forward, new Vector4( 0.5f, 0.5f, 0, 0 ) ) );
		for ( var i = 0; i <= sides; i++ )
		{
			var a = i / (float)sides * MathF.PI * 2f;
			var p = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f );
			verts.Add( new Vertex( p, Vector3.Down, Vector3.Forward, new Vector4( 0.5f + p.x * 0.5f, 0.5f + p.y * 0.5f, 0, 0 ) ) );
		}
		for ( var i = 0; i < sides; i++ )
			indices.AddRange( new[] { center, center + 2 + i, center + 1 + i } );

		var mesh = new Mesh( Plain );
		mesh.CreateVertexBuffer( verts.Count, verts );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.Bounds = new BBox( new Vector3( -1, -1, 0 ), new Vector3( 1, 1, 1 ) );
		return Model.Builder.AddMesh( mesh ).Create();
	}

	/// <summary>
	/// Adds a spindle under a parent: starts at 'at', points along 'direction', 'length' long and
	/// 'radius' thick. 'up' says which way it curves.
	/// </summary>
	public static ModelRenderer AddSpindle( GameObject parent, string name, Vector3 at, Vector3 direction, Vector3 up, float radius, float length, float tip, float bend, Color color )
	{
		var go = new GameObject( parent, true, name );
		go.LocalPosition = at;
		go.LocalRotation = Rotation.LookAt( direction.Normal, up );
		go.LocalScale = new Vector3( length, radius, radius );
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Spindle( tip, bend );
		r.Tint = color;
		return r;
	}
}
