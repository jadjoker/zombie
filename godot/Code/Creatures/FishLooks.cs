using Godot;

/// <summary>
/// The fish swarm's looks (user's ask: less plain). Every fish wears `BodyShader` (dark back, pale belly, stripes on
/// some colours) on the school's fish model, and there are three kinds, built once per colour as one mesh each so a
/// MultiMesh draws them: plain; undead (rotted grey-green hide, a raw wound with bare ribs, one milky glowing eye, and
/// a brain bulging out of the top of its head that throbs and every so often pops up out of the skull, `BrainShader`);
/// alien (a dark hide with glowing spots, a glowing lure on a stalk, spikes down its back, extra glowing eyes and
/// neon fins). A swarm has a `Theme` (most of its fish that kind) and a colour from `Colours`.
/// </summary>
public static class FishLooks
{
	public enum Kind { Plain, Undead, Alien }

	/// <summary> Swarm colours: rust, yellow and green (user's picks) and a few more. </summary>
	public static readonly Color[] Colours =
	{
		new( 0.62f, 0.28f, 0.12f ),   // rust
		new( 1f, 0.8f, 0.12f ),       // yellow
		new( 0.32f, 0.68f, 0.2f ),    // green
		new( 0.55f, 0.6f, 0.15f ),    // olive
		new( 1f, 0.45f, 0.15f ),      // orange
		new( 0.2f, 0.55f, 0.9f ),     // blue
		new( 0.85f, 0.25f, 0.3f ),    // red
	};

	static readonly Color[] Neon = { new( 0.4f, 1f, 0.3f ), new( 0.75f, 0.35f, 1f ), new( 0.2f, 1f, 0.9f ) };
	static readonly Color Rot = new( 0.42f, 0.48f, 0.38f );
	public static readonly Color BrainPink = new( 0.93f, 0.56f, 0.6f );

	/// <summary> Blood for each kind's dead fish: red, dark undead ooze, glowing alien green. </summary>
	public static Color Blood( Kind k ) => k switch
	{
		Kind.Undead => new Color( 0.45f, 0.12f, 0.1f ),
		Kind.Alien => new Color( 0.4f, 1f, 0.3f ),
		_ => new Color( 0.85f, 0.2f, 0.25f ),
	};

	/// <summary> The colour of a kind's hide (the ragdoll pieces use it too). </summary>
	public static Color Skin( Kind k, Color colour ) => k switch
	{
		Kind.Undead => colour.Lerp( Rot, 0.6f ),
		Kind.Alien => colour.Darkened( 0.45f ),
		_ => colour,
	};

	public static Color Accent( Color colour ) => Neon[(int)(MathF.Abs( colour.R * 7f + colour.G * 13f + colour.B * 3f ) * 10f) % Neon.Length];

	/// <summary> Picks each fish's kind for a swarm: most of them its theme, the rest plain. </summary>
	public static Kind[] Mix( Kind theme, int count, Random random )
	{
		var kinds = new Kind[count];
		for ( var i = 0; i < count; i++ ) kinds[i] = theme == Kind.Plain || random.NextDouble() < 0.7 ? theme : Kind.Plain;
		return kinds;
	}

	public static Kind RandomTheme() => (Kind)Rng.Int( 0, 2 );

	static Mesh model;
	static Shader body, brain;
	static readonly Dictionary<(Kind, Color), Mesh> cache = new();

	/// <summary> The fish for this kind and colour (shared). Nose +Z, about 0.33 m long. </summary>
	public static Mesh Get( Kind kind, Color colour )
	{
		if ( cache.TryGetValue( (kind, colour), out var m ) ) return m;
		model ??= FishSchool.Painted( Colours[0] );
		if ( model is null ) return null;
		var mesh = new ArrayMesh();
		var skin = Skin( kind, colour );
		var accent = Accent( colour );
		for ( var i = 0; i < model.GetSurfaceCount(); i++ )
		{
			var name = model.SurfaceGetMaterial( i )?.ResourceName ?? "";
			mesh.AddSurfaceFromArrays( Mesh.PrimitiveType.Triangles, model.SurfaceGetArrays( i ) );
			Material mat = name switch
			{
				"Fish" => BodyMaterial( kind, skin, colour, accent ),
				"FishFin" => kind == Kind.Alien
					? new StandardMaterial3D { AlbedoColor = accent with { A = 0.75f }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = accent, EmissionEnergyMultiplier = 0.8f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 0.3f }
					: new StandardMaterial3D { AlbedoColor = kind == Kind.Undead ? skin.Darkened( 0.2f ) : colour.Lightened( 0.35f ), Roughness = 0.5f, RimEnabled = true, Rim = 0.4f, RimTint = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
				_ => new StandardMaterial3D { AlbedoColor = new Color( 0.03f, 0.03f, 0.04f ), Roughness = 0.1f },
			};
			mesh.SurfaceSetMaterial( i, mat );
		}
		if ( kind == Kind.Undead ) Undead( mesh );
		else if ( kind == Kind.Alien ) Alien( mesh, accent );
		cache[(kind, colour)] = mesh;
		return mesh;
	}

	// Fish space (from the model): body x ±0.035, y ±0.07, z ±0.13 (nose +Z), eyes at (±0.035, 0.018, 0.085).

	static readonly Vector3 BrainAt = new( 0f, 0.058f, 0.055f );

	static void Undead( ArrayMesh mesh )
	{
		var ball = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 12, Rings = 8 };
		// The brain: a main lump and lobes bulging out of a torn hole in the skull.
		// One smooth lump (lobes as separate balls read wrong); the shader draws the folds and the middle groove.
		var b = Part();
		Put( b, new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 20, Rings = 12 }, BrainAt + new Vector3( 0f, 0.006f, 0f ), new Vector3( 0.036f, 0.026f, 0.046f ) );
		Commit( b, mesh, new ShaderMaterial { Shader = Brain } );
		// The wound: the torn skull rim round the brain, and a raw gash in the flank with bare ribs.
		var w = Part();
		Put( w, ball, BrainAt + new Vector3( 0f, -0.01f, 0f ), new Vector3( 0.042f, 0.014f, 0.05f ) );
		Put( w, ball, new Vector3( 0.026f, -0.006f, -0.018f ), new Vector3( 0.022f, 0.07f, 0.085f ) );
		Commit( w, mesh, new StandardMaterial3D { AlbedoColor = new Color( 0.4f, 0.06f, 0.07f ), Roughness = 0.25f } );
		var r = Part();
		var box = new BoxMesh { Size = Vector3.One };
		for ( var i = 0; i < 4; i++ )
		{
			var z = -0.05f + i * 0.021f;
			Put( r, box, new Vector3( 0.037f, -0.004f, z ), new Vector3( 0.006f, 0.054f - 0.006f * MathF.Abs( i - 1.5f ), 0.006f ) );
		}
		Put( r, box, new Vector3( 0.035f, 0.022f, -0.018f ), new Vector3( 0.006f, 0.006f, 0.07f ) );   // the spine bar over them
		Commit( r, mesh, new StandardMaterial3D { AlbedoColor = new Color( 0.92f, 0.88f, 0.76f ), Roughness = 0.6f } );
		// One milky dead eye, bulging and glowing.
		var e = Part();
		Put( e, ball, new Vector3( -0.037f, 0.018f, 0.085f ), Vector3.One * 0.03f );
		Commit( e, mesh, Glow( new Color( 0.8f, 0.95f, 0.7f ), 1.2f ) );
	}

	static void Alien( ArrayMesh mesh, Color accent )
	{
		var ball = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 6 };
		var cone = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 8 };
		var rod = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 6 };
		// Spikes down the back and a stalk curling up over the nose.
		var s = Part();
		for ( var i = 0; i < 4; i++ )
		{
			var z = 0.05f - i * 0.04f;
			var y = 0.07f * MathF.Sqrt( 1f - z * z / (0.13f * 0.13f) );
			var tilt = new Basis( Vector3.Right, -0.5f );
			s.AppendFrom( cone, 0, new Transform3D( tilt * Basis.FromScale( new Vector3( 0.016f, 0.045f - i * 0.006f, 0.016f ) ), new Vector3( 0f, y + 0.012f, z - 0.008f ) ) );
		}
		var root = new Vector3( 0f, 0.06f, 0.075f );
		var bend = new Vector3( 0f, 0.13f, 0.11f );
		var tip = new Vector3( 0f, 0.14f, 0.17f );
		Rod( s, rod, root, bend, 0.007f );
		Rod( s, rod, bend, tip, 0.006f );
		Commit( s, mesh, new StandardMaterial3D { AlbedoColor = new Color( 0.1f, 0.08f, 0.14f ), Roughness = 0.35f, RimEnabled = true, Rim = 0.5f } );
		// The glowing lure and a second pair of eyes.
		var g = Part();
		Put( g, ball, tip, Vector3.One * 0.026f );
		foreach ( var x in new[] { -1f, 1f } )
		{
			Put( g, ball, new Vector3( x * 0.032f, 0.04f, 0.06f ), Vector3.One * 0.014f );
			Put( g, ball, new Vector3( x * 0.03f, -0.006f, 0.07f ), Vector3.One * 0.01f );
		}
		Commit( g, mesh, Glow( accent, 2.2f ) );
	}

	static SurfaceTool Part() { var st = new SurfaceTool(); st.Begin( Mesh.PrimitiveType.Triangles ); return st; }

	static void Put( SurfaceTool st, Mesh m, Vector3 at, Vector3 scale ) => st.AppendFrom( m, 0, new Transform3D( Basis.FromScale( scale ), at ) );

	static void Rod( SurfaceTool st, Mesh m, Vector3 a, Vector3 b, float radius )
	{
		var d = b - a;
		var basis = new Basis( new Quaternion( Vector3.Up, d.Normalized() ) ) * Basis.FromScale( new Vector3( radius * 2f, d.Length(), radius * 2f ) );
		st.AppendFrom( m, 0, new Transform3D( basis, (a + b) * 0.5f ) );
	}

	static void Commit( SurfaceTool st, ArrayMesh mesh, Material mat )
	{
		st.Commit( mesh );
		mesh.SurfaceSetMaterial( mesh.GetSurfaceCount() - 1, mat );
	}

	static StandardMaterial3D Glow( Color c, float energy ) => new() { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy, Roughness = 0.2f };

	static ShaderMaterial BodyMaterial( Kind kind, Color skin, Color colour, Color accent )
	{
		var m = new ShaderMaterial { Shader = body ??= new Shader { Code = BodyShader } };
		m.SetShaderParameter( "back_col", kind == Kind.Alien ? skin.Darkened( 0.4f ) : skin.Darkened( 0.35f ) );
		m.SetShaderParameter( "belly_col", kind == Kind.Alien ? skin.Lightened( 0.15f ) : skin.Lightened( 0.4f ) );
		m.SetShaderParameter( "side_col", skin );
		// Yellow, orange and blue plain fish get dark bars.
		var striped = kind == Kind.Plain && (colour.G > 0.7f || colour.B > 0.8f || (colour.R > 0.9f && colour.G < 0.5f));
		m.SetShaderParameter( "stripe_col", skin.Darkened( 0.6f ) );
		m.SetShaderParameter( "stripes", striped ? 1f : 0f );
		m.SetShaderParameter( "rot", kind == Kind.Undead ? 1f : 0f );
		m.SetShaderParameter( "spots", kind == Kind.Alien ? 1f : 0f );
		m.SetShaderParameter( "glow_col", accent );
		return m;
	}

	const string BodyShader = @"
shader_type spatial;
render_mode cull_disabled;
uniform vec3 back_col : source_color;
uniform vec3 side_col : source_color;
uniform vec3 belly_col : source_color;
uniform vec3 stripe_col : source_color;
uniform vec3 glow_col : source_color;
uniform float stripes;
uniform float rot;
uniform float spots;
varying vec3 lp;
float hash( vec3 p ) { return fract( sin( dot( p, vec3( 12.9898, 78.233, 37.719 ) ) ) * 43758.5453 ); }
float noise( vec3 p )
{
	vec3 i = floor( p ); vec3 f = fract( p ); f = f * f * ( 3.0 - 2.0 * f );
	return mix( mix( mix( hash( i ), hash( i + vec3( 1, 0, 0 ) ), f.x ), mix( hash( i + vec3( 0, 1, 0 ) ), hash( i + vec3( 1, 1, 0 ) ), f.x ), f.y ),
		mix( mix( hash( i + vec3( 0, 0, 1 ) ), hash( i + vec3( 1, 0, 1 ) ), f.x ), mix( hash( i + vec3( 0, 1, 1 ) ), hash( i + vec3( 1, 1, 1 ) ), f.x ), f.y ), f.z );
}
void vertex() { lp = VERTEX; }
void fragment()
{
	float h = clamp( lp.y / 0.07 * 0.5 + 0.5, 0.0, 1.0 );
	vec3 c = mix( belly_col, side_col, smoothstep( 0.2, 0.45, h ) );
	c = mix( c, back_col, smoothstep( 0.62, 0.8, h ) );
	// Bars down the flanks.
	float bar = step( 0.62, fract( lp.z * 22.0 + 0.3 ) ) * step( 0.3, h ) * step( lp.z, 0.07 );
	c = mix( c, stripe_col, bar * stripes );
	// Rot: dark mottled patches and pale flaking scales.
	float n = noise( lp * 70.0 );
	float n2 = noise( lp * 180.0 + 3.0 );
	c = mix( c, c * 0.45, rot * smoothstep( 0.55, 0.7, n ) );
	c = mix( c, vec3( 0.78, 0.8, 0.7 ), rot * step( 0.82, n2 ) * 0.6 );
	ALBEDO = c;
	ROUGHNESS = mix( 0.45, 0.8, rot );
	RIM = 0.4; RIM_TINT = 0.5;
	// Alien: rows of glowing spots along each flank and fine scales.
	vec2 g = vec2( lp.z * 60.0, lp.y * 60.0 );
	vec2 cell = fract( g ) - 0.5;
	float row = step( abs( lp.y + 0.012 ), 0.02 ) + step( abs( lp.y - 0.035 ), 0.012 );
	float spot = smoothstep( 0.3, 0.18, length( cell ) ) * clamp( row, 0.0, 1.0 ) * step( 0.4, hash( floor( vec3( g, 1.0 ) ) ) );
	ALBEDO = mix( ALBEDO, ALBEDO * 0.75, spots * step( 0.42, abs( cell.x + cell.y ) ) * 0.5 );
	EMISSION = glow_col * spot * spots * 2.5;
}
";

	/// <summary> The undead fish's brain: wet pink folds, a throb, and every few seconds it pops up out of the skull and squelches back. </summary>
	public static Shader Brain => brain ??= new Shader { Code = BrainShader };

	/// <summary> The brain that flies out of a dead undead fish: the same folds, no throb (`size` is its width in metres). </summary>
	public static ShaderMaterial LooseBrain( float size )
	{
		var m = new ShaderMaterial { Shader = Brain };
		m.SetShaderParameter( "alive", 0f );
		m.SetShaderParameter( "fold_scale", size );
		return m;
	}

	const string BrainShader = @"
shader_type spatial;
const vec3 centre = vec3( 0.0, 0.058, 0.055 );
uniform float alive = 1.0;
uniform float fold_scale = 1.0;   // metres per mesh unit (the loose brain is a unit ball)
varying vec3 lp;
void vertex()
{
	float id = float( INSTANCE_ID );
	float pop = alive * pow( max( sin( TIME * 1.7 + id * 1.9 ), 0.0 ), 10.0 );
	float throb = alive * 0.07 * sin( TIME * 8.0 + id * 2.3 );
	lp = ( VERTEX - centre * alive ) * fold_scale;
	if ( alive > 0.5 ) VERTEX = centre + ( VERTEX - centre ) * ( 1.0 + throb + 0.3 * pop ) + vec3( 0.0, 0.035 * pop, 0.0 );
}
void fragment()
{
	// Wiggly folds (gyri) as winding dark lines, and the groove down the middle.
	vec2 q = vec2( lp.x, lp.z ) * 520.0;
	float w = sin( q.x + sin( q.y * 0.9 ) * 2.2 ) + sin( q.y * 1.1 + sin( q.x * 1.3 ) * 2.0 );
	float line = 1.0 - smoothstep( 0.0, 0.35, abs( w ) );
	float groove = 1.0 - smoothstep( 0.0015, 0.0035, abs( lp.x ) );
	vec3 c = mix( vec3( 0.95, 0.62, 0.66 ), vec3( 0.62, 0.24, 0.32 ), max( line * 0.85, groove ) );
	ALBEDO = c;
	ROUGHNESS = 0.18;
	SPECULAR = 0.7;
}
";
}
