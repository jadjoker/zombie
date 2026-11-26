using Godot;

/// <summary>
/// The Abyss's centrepiece (user's design, after the photo they shared of a sunken statue of Christ): a robed,
/// bearded figure on a stepped round base with its arms raised up and out and its face turned up toward a shaft of
/// light from a hole in the roof, crusted all over with pink coralline, dark sponges, pale barnacles and a film of algae on
/// what faces up (`Crust`, a world-space shader). Our own build in smooth code shapes, not a copy of the real
/// statue. A spotlight down the shaft lights it, with a visible beam in the murk and fish circling. Solid: the base
/// tiers and the figure have colliders. In "human" units (about 1.8 tall) scaled up by `Scale`. Metres.
/// </summary>
public static class AbyssStatue
{
	/// <summary> The figure's size against a person (it stands about 6 m tall on its base). </summary>
	public const float Scale = 3.4f;
	public const float BaseHeight = 1.5f;
	/// <summary> The base's tiers (radius, height). </summary>
	static readonly (float R, float H)[] Tiers = { (5.6f, 0.5f), (4.2f, 0.5f), (2.8f, 0.5f) };

	public static Node3D Root { get; private set; }
	public static SpotLight3D Light { get; private set; }

	static readonly Shader CrustShader = new()
	{
		Code = @"shader_type spatial;
uniform vec3 stone_a : source_color = vec3(0.50, 0.55, 0.52);
uniform vec3 stone_b : source_color = vec3(0.36, 0.42, 0.42);
uniform vec3 coral : source_color = vec3(0.82, 0.44, 0.56);
uniform vec3 algae : source_color = vec3(0.32, 0.40, 0.24);
uniform vec3 sponge : source_color = vec3(0.10, 0.10, 0.09);
uniform float scale = 1.6;
varying vec3 wp;
varying vec3 wn;
void vertex() {
	wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wn = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}
float hash(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise(vec3 x) {
	vec3 i = floor(x); vec3 f = fract(x); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1,0,0)), f.x), mix(hash(i + vec3(0,1,0)), hash(i + vec3(1,1,0)), f.x), f.y),
		mix(mix(hash(i + vec3(0,0,1)), hash(i + vec3(1,0,1)), f.x), mix(hash(i + vec3(0,1,1)), hash(i + vec3(1,1,1)), f.x), f.y), f.z);
}
float fbm(vec3 p) { return noise(p) * 0.55 + noise(p * 2.1) * 0.3 + noise(p * 4.3) * 0.15; }
void fragment() {
	vec3 p = wp * scale;
	vec3 c = mix(stone_a, stone_b, fbm(p * 0.6 + 5.0));
	float pink = smoothstep(0.5, 0.6, fbm(p * 1.2 + 11.0));
	c = mix(c, coral * (0.85 + 0.3 * noise(p * 8.0)), pink * 0.9);
	float up = clamp(wn.y, 0.0, 1.0);
	float alg = smoothstep(0.35, 0.8, up) * smoothstep(0.4, 0.62, fbm(p * 1.8 + 3.0));
	c = mix(c, algae, alg * 0.75);
	float spots = smoothstep(0.7, 0.76, noise(p * 5.0 + 20.0));
	c = mix(c, sponge, spots * 0.92);
	float dots = smoothstep(0.82, 0.86, noise(p * 13.0 + 7.0));
	c = mix(c, vec3(0.86, 0.85, 0.78), dots * 0.7);
	ALBEDO = c * (0.85 + 0.3 * fbm(p * 3.0));
	ROUGHNESS = 0.92;
}",
	};

	/// <summary>
	/// The figure is solid where it is drawn: a hull for each of its bigger pieces (robe, body, arms, head), so a robot
	/// that climbs the base can't walk into it.
	/// </summary>
	static void FigureSolid( Node3D figure, StaticBody3D body )
	{
		foreach ( var m in figure.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			if ( m.Mesh is null || m.Skin is not null ) continue;
			// Its place in the body's space (the body sits at the statue's root), baked into the points: shapes can't be
			// scaled unevenly.
			var to = Transform3D.Identity;
			for ( Node n = m; n is not null && n != body.GetParent(); n = n.GetParent() )
				if ( n is Node3D n3 ) to = n3.Transform * to;
			var size = m.GetAabb().Size * to.Basis.Scale;
			if ( MathF.Max( size.X, MathF.Max( size.Y, size.Z ) ) < 0.35f ) continue;
			if ( m.Mesh.CreateConvexShape( true, true ) is not ConvexPolygonShape3D hull ) continue;
			var points = hull.Points.Select( p => to * p ).ToArray();
			body.AddChild( new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = points } } );
		}
	}

	public static void Build( Node3D parent )
	{
		Root = new Node3D { Name = "Abyss Statue" };
		parent.AddChild( Root );
		var crust = new ShaderMaterial { Shader = CrustShader };
		var baseCrust = new ShaderMaterial { Shader = CrustShader };
		baseCrust.SetShaderParameter( "stone_a", new Color( 0.48f, 0.54f, 0.58f ) );
		baseCrust.SetShaderParameter( "stone_b", new Color( 0.34f, 0.40f, 0.46f ) );
		var body = new StaticBody3D { Name = "Statue Solid", CollisionLayer = Layers.World | Layers.Plant, CollisionMask = 0 };
		body.SetMeta( VegetationColliders.RockMeta, true );
		Root.AddChild( body );

		// The base: three round steps, the lowest one cut away on a side (worn down to a low ledge there), like a spiral.
		var y = 0f;
		for ( var i = 0; i < Tiers.Length; i++ )
		{
			var (r, h) = Tiers[i];
			if ( i == 0 )
			{
				SmoothKit.Put( Root, SectorMesh( r, h, 40f, 330f ), baseCrust, "Base Step" ).Position = new Vector3( 0f, y - 0.3f, 0f );
				SmoothKit.Put( Root, SectorMesh( r, h * 0.45f, 330f, 400f ), baseCrust, "Base Ledge" ).Position = new Vector3( 0f, y - 0.3f, 0f );
				body.AddChild( new CollisionShape3D { Shape = new CylinderShape3D { Radius = r, Height = h + 0.3f }, Position = new Vector3( 0f, y + (h - 0.3f) * 0.5f, 0f ) } );
			}
			else
			{
				SmoothKit.Put( Root, SectorMesh( r, h, 0f, 360f ), baseCrust, "Base Step" ).Position = new Vector3( 0f, y, 0f );
				body.AddChild( new CollisionShape3D { Shape = new CylinderShape3D { Radius = r, Height = h }, Position = new Vector3( 0f, y + h * 0.5f, 0f ) } );
			}
			y += h;
		}

		// The figure, in human units, facing +Z (turned to face the arrival).
		var figure = new Node3D { Name = "Figure", Scale = Vector3.One * Scale };
		Root.AddChild( figure );
		figure.Position = new Vector3( 0f, BaseHeight, 0f );
		BuildFigure( figure, crust );
		FigureSolid( figure, body );
		var face = Abyss.Arrival.Flat().Normalized();
		Root.Rotation = new Vector3( 0f, MathF.Atan2( face.X, face.Z ), 0f );
		body.AddChild( new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.3f * Scale, Height = 1.5f * Scale }, Position = new Vector3( 0f, BaseHeight + 0.75f * Scale, 0f ) } );

		// The light down the shaft, a beam you can see in the murk, and a dim glow round the hub.
		Light = new SpotLight3D
		{
			Name = "Statue Light", LightColor = new Color( 0.78f, 0.9f, 1f ), LightEnergy = 7f, SpotRange = 40f, SpotAngle = 17f,
			SpotAngleAttenuation = 0.8f, SpotAttenuation = 0.35f, ShadowEnabled = true, LightVolumetricFogEnergy = 3f,
		};
		parent.AddChild( Light );
		Light.GlobalTransform = new Transform3D( Shapes.LookAt( Vector3.Down, Vector3.Forward ), new Vector3( 0f, Abyss.RoofHeight - 3f, 0f ) );
		// A soft cold light round the figure's head and arms from the shaft (so it reads from across the hub).
		var halo = new OmniLight3D { Name = "Statue Halo", LightColor = new Color( 0.7f, 0.85f, 1f ), LightEnergy = 1.6f, OmniRange = 11f, OmniAttenuation = 1.2f, ShadowEnabled = false, LightVolumetricFogEnergy = 0.8f };
		parent.AddChild( halo );
		halo.Position = new Vector3( 0f, BaseHeight + 1.9f * Scale + 1.5f, 0f ) + Abyss.Arrival.Flat().Normalized() * 2.5f;
		var spill = new OmniLight3D { Name = "Hub Glow", LightColor = new Color( 0.45f, 0.62f, 0.9f ), LightEnergy = 0.5f, OmniRange = 30f, OmniAttenuation = 1.4f, ShadowEnabled = false, LightVolumetricFogEnergy = 0.2f };
		parent.AddChild( spill );
		spill.Position = new Vector3( 0f, 9f, 0f );
		var beam = new ShaderMaterial { Shader = GD.Load<Shader>( "res://assets/shaders/fx_statue_beam.gdshader" ) };
		beam.SetShaderParameter( "strength", 0.2f );
		beam.SetMeta( "no_caustics", true );
		// From the floor up through the skylight.
		var shaftHeight = Abyss.RoofHeight + 6f;
		var shaft = new MeshInstance3D
		{
			Name = "Light Beam",
			Mesh = new CylinderMesh { TopRadius = Abyss.SkylightRadius * 0.7f, BottomRadius = 6.5f, Height = shaftHeight, RadialSegments = 24, Rings = 1, CapTop = false, CapBottom = false },
			MaterialOverride = beam,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		parent.AddChild( shaft );
		shaft.Position = new Vector3( 0f, shaftHeight * 0.5f, 0f );

		// Small fish circling the figure in the light.
		var fish = new AtlantisFish( 78 ) { Name = "Statue Fish" };
		fish.Around( 9f, 6f );
		parent.AddChild( fish );
	}

	/// <summary> Poseidon's head is drawn this much of his size on the statue. </summary>
	public const float HeadScale = 0.42f;

	static void BuildFigure( Node3D f, Material m )
	{
		// The robe: a long flaring loft with folds, from the feet to the waist.
		f.AddChild( new MeshInstance3D { Name = "Robe", Mesh = Robe(), MaterialOverride = m } );
		// The torso, chest and shoulders.
		var torso = SmoothKit.Smooth( new List<(float, float, float, float)>
		{
			(0.92f, 0.165f, 0.125f, 0f), (1.08f, 0.17f, 0.12f, 0f), (1.25f, 0.19f, 0.125f, 0.01f), (1.38f, 0.2f, 0.12f, 0f), (1.45f, 0.17f, 0.1f, -0.01f), (1.5f, 0.07f, 0.065f, 0f),
		} );
		SmoothKit.Put( f, SmoothKit.Loft( torso ), m, "Torso" );
		SmoothKit.Tube( f, new Vector3( 0f, 1.46f, 0f ), new Vector3( 0f, 1.58f, 0.01f ), 0.058f, 0.055f, m, "Neck" );
		// A sash at the waist where the robe gathers.
		SmoothKit.Ball( f, new Vector3( 0f, 0.95f, 0f ), new Vector3( 0.36f, 0.06f, 0.27f ), m, null, "Sash" );

		// The head, turned up to the light: Poseidon's own (user's choice), in the same crusted stone, his eyes faintly lit.
		var head = new Node3D { Name = "Head", Position = new Vector3( 0f, 1.6f, 0.01f ), Rotation = new Vector3( -0.42f, 0f, 0f ) };
		f.AddChild( head );
		var face = new Node3D { Name = "Face", Rotation = new Vector3( 0f, MathF.PI, 0f ), Scale = Vector3.One * HeadScale };
		head.AddChild( face );
		Poseidon.StatueHead( face, m, Sculpt.Glow( new Color( 0.35f, 1f, 0.3f ), 1.2f ) );

		// The arms, raised up and out to the light, in wide sleeves, the hands open.
		foreach ( var side in new[] { -1f, 1f } )
		{
			var shoulder = new Vector3( side * 0.19f, 1.4f, 0f );
			SmoothKit.Ball( f, shoulder, new Vector3( 0.15f, 0.13f, 0.14f ), m, null, "Shoulder" );
			var up1 = new Vector3( side * 1f, 0.75f, 0.28f ).Normalized();
			var elbow = shoulder + up1 * 0.3f;
			var up2 = new Vector3( side * 0.75f, 1.05f, 0.3f ).Normalized();
			var wrist = elbow + up2 * 0.27f;
			SmoothKit.Tube( f, shoulder, elbow, 0.068f, 0.062f, m, "Upper Arm" );
			SmoothKit.Ball( f, elbow, Vector3.One * 0.12f, m, null, "Elbow" );
			// The sleeve hangs open below the forearm (the robe's cloth falling back toward the shoulder).
			SmoothKit.Tube( f, elbow + up2 * 0.02f, wrist - up2 * 0.06f, 0.06f, 0.075f, m, "Sleeve" );
			SmoothKit.Ball( f, elbow + up2 * 0.1f + Vector3.Down * 0.05f, new Vector3( 0.1f, 0.16f, 0.1f ), m, null, "Sleeve Fold" );
			SmoothKit.Tube( f, wrist - up2 * 0.08f, wrist, 0.04f, 0.035f, m, "Wrist" );
			// The open hand: a palm facing forward and up, four fingers spread a little, a thumb out to the side.
			var palmAt = wrist + up2 * 0.06f;
			var across = up2.Cross( Vector3.Back ).Normalized() * side;
			var palm = SmoothKit.Ball( f, palmAt, new Vector3( 0.085f, 0.1f, 0.035f ), m, SmoothKit.Aim( -up2, across ), "Palm" );
			for ( var k = 0; k < 4; k++ )
			{
				var spread = (k - 1.5f) * 0.12f;
				var dir = (up2 + across * spread).Normalized();
				var root = palmAt + up2 * 0.045f + across * ((k - 1.5f) * 0.022f);
				SmoothKit.Tube( f, root, root + dir * (k is 1 or 2 ? 0.085f : 0.07f), 0.014f, 0.011f, m, "Finger" );
			}
			var thumbDir = (across * -side * 0f + across + up2 * 0.6f).Normalized();
			SmoothKit.Tube( f, palmAt - up2 * 0.02f + across * 0.04f, palmAt + thumbDir * 0.08f, 0.016f, 0.012f, m, "Thumb" );
			_ = palm;
		}
	}

	/// <summary> The robe: a flaring tube with soft vertical folds, closed at the hem. </summary>
	static ArrayMesh Robe()
	{
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		const int sides = 48, rows = 16;
		Vector3 At( int r, int k )
		{
			var t = r / (float)rows;
			var y = MathX.Lerp( -0.05f, 0.96f, t );
			// Wide at the hem, narrowing up the legs to the waist.
			var radius = MathX.Lerp( 0.3f, 0.165f, MathF.Pow( t, 0.8f ) );
			var a = k / (float)sides * MathF.Tau;
			var folds = 1f + (0.07f * (1f - t) + 0.015f) * MathF.Sin( a * 9f + t * 2.5f ) + 0.03f * (1f - t) * MathF.Sin( a * 4f + 1f );
			return new Vector3( MathF.Sin( a ) * radius * folds, y, MathF.Cos( a ) * radius * folds * 0.85f );
		}
		for ( var r = 0; r < rows; r++ )
			for ( var k = 0; k < sides; k++ )
				foreach ( var (rr, kk) in new[] { (r, k), (r + 1, k + 1), (r + 1, k), (r, k), (r, k + 1), (r + 1, k + 1) } )
					st.AddVertex( At( rr, kk ) );
		var hem = new Vector3( 0f, -0.05f, 0f );
		for ( var k = 0; k < sides; k++ ) { st.AddVertex( hem ); st.AddVertex( At( 0, k ) ); st.AddVertex( At( 0, k + 1 ) ); }
		st.GenerateNormals();
		return st.Commit();
	}

	/// <summary> A ring of the base from angle a0 to a1 (degrees): a slab with a rounded top edge. </summary>
	static ArrayMesh SectorMesh( float radius, float height, float a0, float a1 )
	{
		var st = new SurfaceTool();
		st.Begin( Mesh.PrimitiveType.Triangles );
		var steps = Math.Max( 6, (int)((a1 - a0) / 6f) );
		var full = a1 - a0 >= 359f;
		Vector3 P( float r, float y, float deg ) { var a = MathX.Rad( deg ); return new Vector3( MathF.Cos( a ) * r, y, MathF.Sin( a ) * r ); }
		var profile = new[] { (radius * 0.0f, height), (radius - 0.12f, height), (radius - 0.03f, height - 0.05f), (radius, height - 0.14f), (radius, 0f) };
		for ( var i = 0; i < steps; i++ )
		{
			var d0 = MathX.Lerp( a0, a1, i / (float)steps );
			var d1 = MathX.Lerp( a0, a1, (i + 1) / (float)steps );
			for ( var j = 0; j < profile.Length - 1; j++ )
			{
				var (ra, ya) = profile[j];
				var (rb, yb) = profile[j + 1];
				Quad( st, P( ra, ya, d0 ), P( ra, ya, d1 ), P( rb, yb, d1 ), P( rb, yb, d0 ) );
			}
		}
		if ( !full )
			foreach ( var (d, flip) in new[] { (a0, true), (a1, false) } )
			{
				var c0 = P( 0f, 0f, d ); var c1 = P( 0f, height, d ); var e0 = P( radius, 0f, d ); var e1 = P( radius, height, d );
				if ( flip ) Quad( st, c0, c1, e1, e0 ); else Quad( st, c0, e0, e1, c1 );
			}
		st.GenerateNormals();
		return st.Commit();
	}

	static void Quad( SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d )
	{
		st.AddVertex( a ); st.AddVertex( b ); st.AddVertex( c );
		st.AddVertex( a ); st.AddVertex( c ); st.AddVertex( d );
	}
}
