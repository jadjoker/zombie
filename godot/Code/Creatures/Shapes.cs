using Godot;

/// <summary>
/// Port of s&box Shapes and ShapeMeshes: the blobs the alien creatures are built from (unit sphere and box),
/// the textured creature skins (chitin, eelskin, slime, scales, jelly; light grey so the tint sets the colour),
/// glow and HSV colour helpers, and the code-built spindle (fangs, claws, legs).
///
/// s&box helpers took sizes in inches along s&box axes (x forward, y left, z up). The ones here do the same and
/// return Godot transforms, so creature code can keep the original numbers.
/// </summary>
public static class Shapes
{
	public static readonly SphereMesh Sphere = new() { Radius = 0.5f, Height = 1f, RadialSegments = 24, Rings = 12 };
	public static readonly BoxMesh Box = new() { Size = Vector3.One };

	/// <summary> Godot scale for the unit sphere so it has these radii (inches, s&box axes: forward, left, up). </summary>
	public static Vector3 Ellipsoid( float forward, float left, float up ) => new( left * 2f * U.In, up * 2f * U.In, forward * 2f * U.In );

	public static Vector3 Ellipsoid( float radius ) => Vector3.One * radius * 2f * U.In;

	/// <summary> Godot scale for the unit box so it has this size (inches, s&box axes). </summary>
	public static Vector3 BoxSize( float forward, float left, float up ) => new( left * U.In, up * U.In, forward * U.In );

	/// <summary> A bright, saturated colour from a hue (0-1, wraps), saturation and brightness. </summary>
	public static Color Hsv( float hue, float saturation, float value )
	{
		hue -= MathF.Floor( hue );
		return Color.FromHsv( hue, saturation, value );
	}

	/// <summary> A glowing colour (s&box clipped tints above white, so pure and saturated reads best). </summary>
	public static Color Glow( float hue, float strength = 1f ) => Hsv( hue, 0.9f, 1f );

	static readonly Dictionary<string, (Texture2D Color, Texture2D Normal, Texture2D Rough)> skins = new();

	/// <summary> A new material for one piece: plain, or one of the creature skins, tinted. </summary>
	public static StandardMaterial3D Material( Color tint, string skin = null, bool glow = false )
	{
		var m = new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.55f };
		if ( skin is not null )
		{
			if ( !skins.TryGetValue( skin, out var tex ) )
			{
				var basePath = $"res://assets/materials/creature/{skin}";
				tex = (GD.Load<Texture2D>( basePath + "_color.png" ), GD.Load<Texture2D>( basePath + "_normal.png" ), GD.Load<Texture2D>( basePath + "_rough.png" ));
				skins[skin] = tex;
			}
			m.AlbedoTexture = tex.Color;
			if ( tex.Normal is not null ) { m.NormalEnabled = true; m.NormalTexture = tex.Normal; }
			if ( tex.Rough is not null ) m.RoughnessTexture = tex.Rough;
			m.Roughness = 1f;
			m.Uv1Triplanar = true;
			m.Uv1Scale = Vector3.One * 2f;
		}
		if ( glow )
		{
			m.EmissionEnabled = true;
			m.Emission = tint;
			m.EmissionEnergyMultiplier = 1.5f;
		}
		return m;
	}

	/// <summary> Sets a piece's colour (s&box ModelRenderer.Tint). </summary>
	public static void Tint( MeshInstance3D mesh, Color color )
	{
		if ( mesh?.MaterialOverride is ShaderMaterial sm ) { sm.SetShaderParameter( "tint", color ); return; }
		if ( mesh?.MaterialOverride is StandardMaterial3D m )
		{
			m.AlbedoColor = color;
			if ( m.EmissionEnabled ) m.Emission = color;
		}
	}

	/// <summary>
	/// A blob under a parent: unit sphere (or box) mesh, placed at an s&box local offset (inches), scaled to
	/// Godot units, with its own tintable material.
	/// </summary>
	public static MeshInstance3D Part( Node3D parent, string name, Vector3 sboxOffset, Vector3 godotScale, Color color, bool box = false, string skin = null, bool glow = false, bool shadow = true )
	{
		var mesh = new MeshInstance3D
		{
			Name = name,
			Mesh = box ? Box : Sphere,
			MaterialOverride = Material( color, skin, glow ),
			Position = U.Sbox( sboxOffset ),
			Scale = godotScale,
			CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
		};
		parent.AddChild( mesh );
		return mesh;
	}

	/// <summary> s&box Rotation.LookAt for a Godot direction: a basis whose forward (-Z) points along it. </summary>
	public static Basis LookAt( Vector3 direction, Vector3? up = null )
	{
		var dir = direction.SafeNormal();
		if ( dir == Vector3.Zero ) return Basis.Identity;
		var u = up ?? Vector3.Up;
		if ( MathF.Abs( dir.Dot( u.Normalized() ) ) > 0.999f ) u = Vector3.Back;
		return Basis.LookingAt( dir, u );
	}

	// ---- Spindle (s&box ShapeMeshes.Spindle) ----

	static readonly Dictionary<(int, int), ArrayMesh> spindles = new();

	/// <summary>
	/// A unit spindle: length 1 along -Z (forward), radius 1 at its fattest. tip = end radius as a fraction (0 = sharp),
	/// bend = how far the tip curves toward +Y (in lengths).
	/// </summary>
	public static ArrayMesh Spindle( float tip, float bend )
	{
		var key = ((int)MathF.Round( tip * 10f ), (int)MathF.Round( bend * 10f ));
		if ( spindles.TryGetValue( key, out var cached ) ) return cached;
		tip = key.Item1 / 10f;
		bend = key.Item2 / 10f;
		const int rings = 18, sides = 14;
		var grid = new Vector3[rings + 1, sides + 1];
		for ( var j = 0; j <= rings; j++ )
		{
			var t = j / (float)rings;
			var baseRound = t < 0.15f ? MathF.Sqrt( MathF.Max( 0f, 1f - MathF.Pow( 1f - t / 0.15f, 2f ) ) ) : 1f;
			var taper = MathX.LerpUnclamped( 1f, MathF.Max( tip, 0.001f ), MathF.Pow( t, 0.9f ) );
			var endRound = tip > 0.05f && t > 0.88f ? MathF.Sqrt( MathF.Max( 0f, 1f - MathF.Pow( (t - 0.88f) / 0.12f, 2f ) ) ) : 1f;
			var r = baseRound * taper * endRound;
			if ( j == 0 || j == rings ) r = 0f;
			var curve = bend * t * t;
			for ( var i = 0; i <= sides; i++ )
			{
				var a = i / (float)sides * MathF.PI * 2f;
				grid[j, i] = new Vector3( MathF.Cos( a ) * r, MathF.Sin( a ) * r + curve, -t );
			}
		}
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		for ( var j = 0; j < rings; j++ )
		{
			for ( var i = 0; i < sides; i++ )
			{
				var a = grid[j, i]; var b = grid[j, i + 1]; var c = grid[j + 1, i]; var d = grid[j + 1, i + 1];
				st.AddVertex( a ); st.AddVertex( c ); st.AddVertex( b );
				st.AddVertex( b ); st.AddVertex( c ); st.AddVertex( d );
			}
		}
		st.GenerateNormals();
		var mesh = st.Commit();
		spindles[key] = mesh;
		return mesh;
	}

	/// <summary>
	/// A spindle under a parent (s&box ShapeMeshes.AddSpindle): starts at an s&box local offset (inches), points along
	/// a Godot direction, length and radius in inches, curving toward 'up'.
	/// </summary>
	public static MeshInstance3D AddSpindle( Node3D parent, string name, Vector3 sboxAt, Vector3 direction, Vector3 up, float radius, float length, float tip, float bend, Color color )
	{
		var basis = LookAt( direction, up ) * Basis.FromScale( new Vector3( radius * U.In, radius * U.In, length * U.In ) );
		var mesh = new MeshInstance3D
		{
			Name = name,
			Mesh = Spindle( tip, bend ),
			MaterialOverride = Material( color ),
			Transform = new Transform3D( basis, U.Sbox( sboxAt ) ),
		};
		parent.AddChild( mesh );
		return mesh;
	}
}
