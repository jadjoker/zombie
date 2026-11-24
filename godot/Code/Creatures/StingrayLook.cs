using Godot;

/// <summary>
/// The sting ray's looks (user's design, after photos of a ray from above, below and the side, and the classic alien
/// head): one flat diamond disc built in code, sand camouflaged on top (it draws the sea bed's own sand texture at its
/// world position, so it blends into the floor it glides over), pale underneath with the ray's "face" of gill slits and a
/// mouth, glowing faintly alien green; two big black almond alien eyes on bulges on its head; a long whip tail with a
/// glowing barb. The wings move in the vertex shader: a wave running nose to tail along the edges (the bottom ray's
/// glide), big strokes (the climb), a droop (the dive) and a bank. Metres, facing -Z.
/// </summary>
public partial class Stingray
{
	public const float HalfLength = 0.62f;   // nose to the back of the disc, half of it
	public const float HalfSpan = 0.95f;     // wing tip to the middle
	public const float Sweep = 0.2f;         // how far back the wing tips are swept
	public const float ModelScale = 1.15f;   // the whole ray is drawn this much bigger

	static readonly Color GooColor = new( 0.3f, 0.95f, 0.72f );
	static readonly Color AlienGreen = new( 0.3f, 1f, 0.45f );
	static readonly Color SandColour = new( 0.6f, 0.53f, 0.41f );
	static readonly Color BellyColour = new( 0.86f, 0.88f, 0.85f );
	static readonly StandardMaterial3D EyeBlack = new()
	{
		AlbedoColor = new Color( 0.01f, 0.01f, 0.015f ), Roughness = 0.06f, ClearcoatEnabled = true, Clearcoat = 1f, ClearcoatRoughness = 0.04f, Metallic = 0.2f,
	};
	static readonly StandardMaterial3D EyeRimGlow = Sculpt.Glow( new Color( 0.3f, 1f, 0.35f ), 1.3f );

	public override GibRecipe Gibs => GibRecipes.Stingray( GooColor, SandColour, BellyColour );

	// ---- The disc's shape (shared by the mesh, the eyes and the tail) ----

	static readonly float[] WidthTable = { 0.06f, 0.22f, 0.5f, 0.78f, 0.95f, 1f, 0.9f, 0.66f, 0.4f, 0.17f, 0.05f };

	/// <summary> Half the disc's width at `t` (0 the nose, 1 the back), as a share of <see cref="HalfSpan"/>. </summary>
	static float WidthAt( float t )
	{
		var f = MathX.Clamp( t, 0f, 1f ) * (WidthTable.Length - 1);
		var i = Math.Min( (int)f, WidthTable.Length - 2 );
		var u = f - i;
		float W( int k ) => WidthTable[Math.Clamp( k, 0, WidthTable.Length - 1 )];
		return MathF.Max( 0.02f, SmoothKit.CatmullRom( W( i - 1 ), W( i ), W( i + 1 ), W( i + 2 ), u ) );
	}

	/// <summary> How thick the body is down the middle at `t`. </summary>
	static float ThickAt( float t ) => 0.17f * MathF.Pow( MathF.Max( 0f, MathF.Sin( MathF.PI * MathX.Clamp( t / 0.97f, 0f, 1f ) ) ), 0.75f );

	/// <summary> A point on the disc: `side` -1 to 1 across, `t` 0 (nose) to 1 (back), on the top or the belly. </summary>
	static Vector3 DiscPoint( float side, float t, bool top )
	{
		var w = WidthAt( t ) * HalfSpan;
		var x = side * w;
		var z = -HalfLength + t * 2f * HalfLength + Sweep * side * side * (w / HalfSpan);
		var c = MathF.Max( 0f, 1f - side * side );
		var thick = ThickAt( t );
		float y;
		if ( top )
		{
			// A low dome, and the head's lobe swelling up at the front (after the side photo).
			var head = 0.055f * MathF.Exp( -MathF.Pow( (t - 0.2f) / 0.11f, 2f ) ) * MathF.Exp( -MathF.Pow( x / 0.22f, 2f ) );
			y = thick * MathF.Pow( c, 1.3f ) * 0.72f + head * c;
		}
		else
		{
			var head = 0.03f * MathF.Exp( -MathF.Pow( (t - 0.2f) / 0.1f, 2f ) ) * MathF.Exp( -MathF.Pow( x / 0.2f, 2f ) );
			y = -thick * MathF.Pow( c, 1.8f ) * 0.32f - head * c;
		}
		return new Vector3( x, y, z );
	}

	/// <summary> The disc: a top (camouflage) surface and a belly surface meeting at the edge. Built once. </summary>
	static ArrayMesh DiscMesh() => SmoothKit.Cached( "stingray disc", () =>
	{
		const int across = 36, along = 44;
		var mesh = new ArrayMesh();
		foreach ( var top in new[] { true, false } )
		{
			var st = new SurfaceTool();
			st.Begin( Mesh.PrimitiveType.Triangles );
			var points = new Vector3[(across + 1) * (along + 1)];
			for ( var j = 0; j <= along; j++ )
				for ( var i = 0; i <= across; i++ )
				{
					// Rows bunch up at the nose and the back (where the outline turns fast), columns at the edges.
					var t = 0.5f - 0.5f * MathF.Cos( MathF.PI * j / along );
					var u = i / (float)across * 2f - 1f;
					var side = MathF.Sign( u ) * MathF.Pow( MathF.Abs( u ), 0.8f );
					points[j * (across + 1) + i] = DiscPoint( side, t, top );
				}
			for ( var j = 0; j <= along; j++ )
				for ( var i = 0; i <= across; i++ )
				{
					var p = points[j * (across + 1) + i];
					var dx = points[j * (across + 1) + Math.Min( across, i + 1 )] - points[j * (across + 1) + Math.Max( 0, i - 1 )];
					var dz = points[Math.Min( along, j + 1 ) * (across + 1) + i] - points[Math.Max( 0, j - 1 ) * (across + 1) + i];
					var n = dz.Cross( dx ).Normalized();
					if ( n == Vector3.Zero || (top ? n.Y : -n.Y) < 0f ) n = -n;
					if ( n == Vector3.Zero ) n = top ? Vector3.Up : Vector3.Down;
					st.SetNormal( n );
					st.SetUV( new Vector2( i / (float)across, 0.5f - 0.5f * MathF.Cos( MathF.PI * j / along ) ) );
					st.AddVertex( p );
				}
			for ( var j = 0; j < along; j++ )
				for ( var i = 0; i < across; i++ )
				{
					int a = j * (across + 1) + i, b = a + 1, c = a + across + 1, d = c + 1;
					Tri( st, points, a, b, c, top );
					Tri( st, points, b, d, c, top );
				}
			mesh.AddSurfaceFromArrays( Mesh.PrimitiveType.Triangles, st.CommitToArrays() );
		}
		return mesh;
	} );

	/// <summary> One triangle, wound so its front faces out (Godot's front faces are clockwise). </summary>
	static void Tri( SurfaceTool st, Vector3[] p, int a, int b, int c, bool top )
	{
		var n = (p[b] - p[a]).Cross( p[c] - p[a] );
		var outward = top ? Vector3.Up : Vector3.Down;
		if ( n.Dot( outward ) > 0f ) (b, c) = (c, b);
		st.AddIndex( a ); st.AddIndex( b ); st.AddIndex( c );
	}

	/// <summary>
	/// The disc's shader. Vertex: the wings move (wave, stroke, droop, bank), and the normals follow. Fragment, on top:
	/// the sea bed's sand at the ray's world position (its camouflage), faint darker mottling, an edge a touch darker so
	/// it is sneaky but not invisible, a few alien spots that glow brighter as it attacks, and crackling green lines as it
	/// charges a zap. Underneath (`underside`): pale, with the ray's gill slits and mouth glowing faintly green.
	/// </summary>
	static readonly Shader DiscShader = new()
	{
		Code = @"shader_type spatial;
uniform sampler2D sand_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 sand_tint : source_color = vec3(1.0, 0.99, 0.96);
uniform vec3 belly : source_color = vec3(0.86, 0.88, 0.85);
uniform vec3 glow : source_color = vec3(0.3, 1.0, 0.45);
uniform vec3 spark : source_color = vec3(0.45, 1.0, 0.95);
uniform float underside = 0.0;
uniform float rigid = 0.0;
uniform float half_span = 0.95;
uniform float half_len = 0.62;
uniform float wave_phase = 0.0;
uniform float undulate = 1.0;
uniform float stroke = 0.0;
uniform float droop = 0.0;
uniform float bank = 0.0;
uniform float alert = 0.0;
uniform float charge = 0.0;
uniform float flash = 0.0;
uniform float bruise = 0.0;
varying vec3 lp;
varying vec3 wp;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
	vec2 i = floor(p); vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}
float fbm(vec2 p) { float v = 0.0; float a = 0.5; for (int i = 0; i < 4; i++) { v += a * noise(p); p *= 2.03; a *= 0.5; } return v; }
float disp(float x, float z) {
	float s = clamp(abs(x) / half_span, 0.0, 1.0);
	float m = pow(clamp((s - 0.12) / 0.88, 0.0, 1.0), 1.5);
	float t = (z + half_len) / (2.0 * half_len);
	// The glide: a wave running from the nose to the back along each wing (both in step, like a real ray's).
	float wave = sin(t * 7.5 - wave_phase) * 0.11 * undulate;
	return m * (wave + stroke * 0.42 - droop * 0.34) + sign(x) * m * bank * 0.16;
}
void vertex() {
	lp = VERTEX;
	if (rigid < 0.5) {
		float e = 0.02;
		float d = disp(VERTEX.x, VERTEX.z);
		float dx = (disp(VERTEX.x + e, VERTEX.z) - disp(VERTEX.x - e, VERTEX.z)) / (2.0 * e);
		float dz = (disp(VERTEX.x, VERTEX.z + e) - disp(VERTEX.x, VERTEX.z - e)) / (2.0 * e);
		VERTEX.y += d;
		float up = NORMAL.y >= 0.0 ? 1.0 : -1.0;
		NORMAL = normalize(NORMAL + up * vec3(-dx, 0.0, -dz));
	}
	wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
	float edge = clamp(abs(lp.x) / half_span, 0.0, 1.0);
	vec3 c;
	float em = 0.0;
	if (underside < 0.5) {
		// The camouflage: the floor's own sand where it is, so from above it blends in.
		c = texture(sand_tex, wp.xz * 0.12).rgb * sand_tint;
		float m = fbm(lp.xz * 7.0 + 3.0);
		c *= mix(0.86, 1.05, smoothstep(0.3, 0.7, m));
		// Sneaky, not invisible: the rim of the disc and the head's ridge a little darker.
		float rim = smoothstep(0.62, 1.0, edge);
		c *= mix(1.0, 0.8, rim);
		c *= 1.0 - 0.12 * exp(-pow(lp.x / 0.05, 2.0)) * step(-0.45, lp.z);
		// Alien spots: a few round glowing dots down the back and over the wings, dim while it hunts.
		vec2 g = lp.xz * vec2(9.0, 9.0);
		vec2 cell = floor(g);
		float h = hash(cell + 3.7);
		float dd = length(fract(g) - vec2(0.5));
		float spot = (h > 0.82 ? 1.0 : 0.0) * (1.0 - smoothstep(0.12, 0.2, dd)) * (1.0 - rim);
		c = mix(c, c * 0.55, spot * 0.6);
		em += spot * (0.18 + alert * 2.2) * (0.75 + 0.25 * sin(TIME * 2.0 + h * 20.0));
	} else {
		// The belly: pale, a little grey at the edge.
		c = belly * mix(1.0, 0.78, smoothstep(0.55, 1.0, edge));
		// Gill slits: five short curved slits each side behind the mouth, glowing faintly alien green inside.
		float gill = 0.0;
		for (int i = 0; i < 5; i++) {
			float z0 = -0.26 + float(i) * 0.042;
			float ax = abs(lp.x);
			float zz = lp.z - z0 - 0.35 * pow(ax - 0.13, 2.0);
			float on = step(0.085, ax) * step(ax, 0.17 + float(i) * 0.004);
			gill = max(gill, on * (1.0 - smoothstep(0.003, 0.007, abs(zz))));
		}
		// The mouth: a wide gentle smile across the front, and two nostrils ahead of it.
		float mz = lp.z + 0.37 + 0.9 * lp.x * lp.x;
		float mouth = step(abs(lp.x), 0.085) * (1.0 - smoothstep(0.004, 0.008, abs(mz)));
		float nose = 1.0 - smoothstep(0.012, 0.018, length(vec2(abs(lp.x) - 0.045, lp.z + 0.43)));
		c = mix(c, vec3(0.08, 0.1, 0.09), max(gill, max(mouth, nose)));
		em += gill * (0.7 + 0.5 * alert) + mouth * 0.25;
	}
	// Charging a zap: crackling lines of light over the whole disc.
	float lines = 1.0 - smoothstep(0.0, 0.025, abs(fbm(lp.xz * 5.0 + vec2(TIME * 3.1, -TIME * 2.3)) - 0.5));
	float crackle = lines * charge * (0.6 + 0.4 * sin(TIME * 40.0 + lp.x * 30.0));
	// Hurt: bloodied in patches.
	float blood = bruise * smoothstep(0.45, 0.7, fbm(lp.xz * 6.0 + 11.0));
	c = mix(c, c * 0.45 + vec3(0.05, 0.22, 0.16), blood * 0.8);
	float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = mix(c, vec3(2.2), flash);
	ROUGHNESS = underside > 0.5 ? 0.42 : 1.0;
	SPECULAR = underside > 0.5 ? 0.5 : 0.25;
	EMISSION = glow * em + spark * crackle * 3.0 + spark * fres * charge * 0.8 + (underside > 0.5 ? c * 0.4 : vec3(0.0));   // the belly a little self lit (the sand throws light back up), so it reads pale
}",
	};

	ShaderMaterial topSkin, bellySkin, bulbSkin;
	Node3D body, tailRoot;
	readonly List<Node3D> tail = new();
	readonly List<Node3D> eyes = new();
	MeshInstance3D disc, barb, vortex;
	ShaderMaterial vortexPaint;
	OmniLight3D zapLight;
	CreaturePart tailPart;

	static Texture2D sand;
	static Texture2D Sand()
	{
		const string path = "res://assets/materials/ground/seabed_color.png";
		if ( sand is null && ResourceLoader.Exists( path ) ) sand = GD.Load<Texture2D>( path );
		return sand;
	}

	ShaderMaterial Skin( bool underside, bool rigid )
	{
		var m = new ShaderMaterial { Shader = DiscShader };
		if ( Sand() is { } tex ) m.SetShaderParameter( "sand_tex", tex );
		m.SetShaderParameter( "underside", underside ? 1f : 0f );
		m.SetShaderParameter( "rigid", rigid ? 1f : 0f );
		m.SetShaderParameter( "half_span", HalfSpan );
		m.SetShaderParameter( "half_len", HalfLength );
		m.SetShaderParameter( "belly", BellyColour );
		m.SetShaderParameter( "glow", AlienGreen );
		return m;
	}

	void Build()
	{
		topSkin = Skin( false, false );
		bellySkin = Skin( true, false );
		bulbSkin = Skin( false, true );
		body = new Node3D { Name = "Body", Scale = Vector3.One * ModelScale };
		AddChild( body );

		disc = new MeshInstance3D { Name = "Disc", Mesh = DiscMesh() };
		disc.SetSurfaceOverrideMaterial( 0, topSkin );
		disc.SetSurfaceOverrideMaterial( 1, bellySkin );
		body.AddChild( disc );

		// The eyes: the classic alien eyes (big glossy black almonds slanted out at the top, on a thin glowing green
		// rim) set in bulges on the head, looking up, out and forward. Spiracles (the ray's breathing holes) behind.
		foreach ( var x in new[] { -1f, 1f } )
		{
			var at = DiscPoint( x * 0.34f, 0.19f, true ) + Vector3.Up * 0.012f;
			var eye = new Node3D { Name = "Alien Eye", Position = at };
			body.AddChild( eye );
			var look = new Vector3( x * 0.7f, 0.72f, -0.3f ).Normalized();
			eye.Basis = new Basis( new Quaternion( Vector3.Back, look ) ) * new Basis( Vector3.Back, -x * MathX.Rad( 38f ) );
			SmoothKit.Ball( eye, new Vector3( 0f, 0f, -0.03f ), new Vector3( 0.115f, 0.19f, 0.075f ), bulbSkin, null, "Eye Bulb" );
			Sculpt.Blob( eye, new Vector3( 0f, 0f, 0.0f ), new Vector3( 0.088f, 0.186f, 0.03f ), EyeRimGlow, name: "Eye Rim" );
			Sculpt.Blob( eye, new Vector3( 0f, 0f, 0.004f ), new Vector3( 0.08f, 0.176f, 0.032f ), EyeBlack, name: "Eye" );
			var part = CreaturePart.Create( this, x < 0f ? 2 : 3, x < 0f ? "Stingray Eye L" : "Stingray Eye R", 0.1f );
			body.AddChild( part );
			part.Position = at;
			eyes.Add( eye );
			Sculpt.Blob( body, DiscPoint( x * 0.3f, 0.3f, true ) + Vector3.Up * 0.004f, new Vector3( 0.06f, 0.02f, 0.045f ), Sculpt.Paint( new Color( 0.08f, 0.07f, 0.06f ), 0.8f ), name: "Spiracle" );
		}

		// The tail: a long whip from the back of the disc in a chain of joints, a glowing barb a third of the way down.
		var bone = GibKit.Paint( SandColour * 0.75f, 0.8f );
		var parent = Sculpt.Joint( body, DiscPoint( 0f, 1f, true ) + new Vector3( 0f, -0.01f, -0.04f ), "Tail" );
		tailRoot = parent;
		tail.Add( parent );
		const int pieces = 6;
		for ( var i = 0; i < pieces; i++ )
		{
			var length = i == 0 ? 0.3f : 0.24f;
			var r0 = MathX.Lerp( 0.04f, 0.006f, i / (float)pieces );
			var r1 = MathX.Lerp( 0.04f, 0.006f, (i + 1f) / pieces );
			SmoothKit.Tube( parent, Vector3.Zero, new Vector3( 0f, 0f, length ), r0, r1, bone, "Tail Piece" );
			if ( i == 1 )
			{
				barb = Sculpt.Cone( parent, new Vector3( 0f, 0.02f, 0.02f ), new Vector3( 0f, 0.07f, 0.2f ), 0.018f, Sculpt.Glow( AlienGreen, 2.2f ), 5, "Barb" );
				for ( var k = 0; k < 3; k++ )
					Sculpt.Cone( barb, new Vector3( 0f, 0f, 0f ), new Vector3( 0f, 0.012f, -0.03f ), 0.006f, Sculpt.Glow( AlienGreen, 2.2f ), 4, "Serration" ).Position = new Vector3( 0f, 0.02f + k * 0.015f, 0.05f + k * 0.045f );
			}
			var next = Sculpt.Joint( parent, new Vector3( 0f, 0f, length ), $"Tail {i + 1}" );
			tail.Add( next );
			parent = next;
		}
		tailPart = CreaturePart.Create( this, 4, "Stingray Tail", 0.1f );
		tail[1].AddChild( tailPart );

		// Small pelvic fins either side of the tail's root.
		var fins = SmoothKit.FinMaterial( SandColour, SandColour * 0.7f, 0.9f, 0.15f );
		foreach ( var x in new[] { -1f, 1f } )
			SmoothKit.Fin( body, DiscPoint( x * 0.12f, 0.93f, false ) + Vector3.Up * 0.01f, SmoothKit.Aim( new Vector3( x * 0.5f, -0.05f, 1f ), Vector3.Up ), 0.16f, 0.1f, fins, 0.04f );

		// What else can be shot: the head (0), the body (1), the wings (5, 6).
		void Part( int index, string name, Vector3 at, float radius )
		{
			var p = CreaturePart.Create( this, index, name, radius );
			body.AddChild( p );
			p.Position = at;
		}
		Part( 0, "Stingray Head", new Vector3( 0f, 0.02f, -0.36f ), 0.24f );
		Part( 1, "Stingray Body", new Vector3( 0f, 0.02f, 0.08f ), 0.32f );
		Part( 5, "Stingray Wing L", new Vector3( -0.55f, 0f, 0.12f ), 0.3f );
		Part( 6, "Stingray Wing R", new Vector3( 0.55f, 0f, 0.12f ), 0.3f );

		zapLight = new OmniLight3D { Name = "Zap Light", LightColor = new Color( 0.45f, 1f, 0.9f ), LightEnergy = 0f, OmniRange = 4.5f, ShadowEnabled = false, Visible = false };
		body.AddChild( zapLight );
		BuildVortex();
	}

	/// <summary> The whirlpool round it as it dives: a cone of spiralling light trailing behind it (top level, aimed each frame). </summary>
	void BuildVortex()
	{
		vortexPaint = new ShaderMaterial { Shader = VortexShader };
		vortex = new MeshInstance3D
		{
			Name = "Dive Whirlpool",
			TopLevel = true,
			Mesh = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 1.35f, Height = 3f, RadialSegments = 24, Rings = 6, CapTop = false, CapBottom = false },
			MaterialOverride = vortexPaint,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
		};
		vortexPaint.SetMeta( "no_caustics", true );
		AddChild( vortex );
	}

	static readonly Shader VortexShader = new()
	{
		Code = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;
uniform vec3 inner : source_color = vec3(0.45, 1.0, 0.9);
uniform vec3 outer : source_color = vec3(0.3, 1.0, 0.45);
uniform float fade = 0.0;
uniform float spin = 0.0;
void fragment() {
	// UV.x round the cone, UV.y along it (0 the narrow end behind, 1 the wide end round the ray).
	float arms = 4.0;
	float s = fract(UV.x * arms + UV.y * 1.6 - spin);
	float band = smoothstep(0.0, 0.18, s) * (1.0 - smoothstep(0.35, 0.6, s));
	float along = smoothstep(0.0, 0.35, UV.y) * (1.0 - smoothstep(0.85, 1.0, UV.y));
	vec3 c = mix(outer, inner, UV.y);
	ALBEDO = c * band * along * fade * 1.6;
	ALPHA = 1.0;
}",
	};

	// ---- Every frame: the body's pose and the wings ----

	float wavePhase, strokePhase, spin;
	float pitch, pitchVel, roll, rollVel, stroke, droop, droopVel, bankV, diveBlend, diveBlendVel, charge;
	float lastYaw;
	Vector3 lastPos;

	/// <summary>
	/// Poses the body: its tilt to the ground under it, banks into turns, rears up to zap, noses up to climb, and in the
	/// dive points along its path and spins. Sets the wings' wave, strokes and droop, and the tail's sway.
	/// </summary>
	void Pose( float dt )
	{
		var t = (float)Clock.Now + phase;
		var a = (float)sinceAct;
		var moved = dt > 0f ? (GlobalPosition - lastPos) / dt : Vector3.Zero;
		lastPos = GlobalPosition;
		var speed = moved.Flat().Length();
		var yaw = GlobalBasis.GetEuler().Y;
		var yawRate = dt > 0f ? Mathf.AngleDifference( lastYaw, yaw ) / dt : 0f;
		lastYaw = yaw;

		// What the act wants.
		var pitchWant = groundPitch;
		var rollWant = groundRoll + MathX.Clamp( yawRate * 0.35f, -0.5f, 0.5f );
		var undulate = 0.9f + speed * 0.25f;
		var strokeAmp = 0f;
		var strokeRate = 2.4f;
		var droopWant = 0f;
		var chargeWant = 0f;
		var alert = 0f;
		switch ( act )
		{
			case Act.Prowl:
				if ( watched ) undulate = 0.35f;   // frozen flat under your gaze
				break;
			case Act.Zap:
				// It rears up, showing its belly, the disc crackling as it charges; the zap snaps it forward.
				var rear = a < ZapWindup ? SmoothKit.Ease( a / ZapWindup ) : 1f - SmoothKit.Ease( (a - ZapWindup) / 0.4f );
				pitchWant = groundPitch * (1f - rear) + 0.85f * rear;
				chargeWant = a < ZapWindup ? 0.35f + 0.65f * a / ZapWindup : MathX.Clamp( 1f - (a - ZapWindup) / 0.3f, 0f, 1f );
				strokeAmp = 0.35f * rear;
				strokeRate = 3.2f;
				undulate = 1.4f;
				alert = 1f;
				break;
			case Act.Climb:
				pitchWant = 0.55f;
				strokeAmp = 1f;
				strokeRate = 2.2f;
				undulate = 0.4f;
				alert = 0.6f;
				break;
			case Act.Aim or Act.Dive:
				droopWant = 1f;
				undulate = 0.25f;
				alert = 1f;
				break;
			case Act.Recover:
				undulate = 1.6f;
				break;
		}
		SmoothKit.Spring( ref pitch, ref pitchVel, MathX.Clamp( pitchWant, -0.6f, 1f ), act == Act.Zap ? 2.2f : 1.2f, 0.8f, dt );
		SmoothKit.Spring( ref roll, ref rollVel, MathX.Clamp( rollWant, -0.6f, 0.6f ), 1.1f, 0.8f, dt );
		SmoothKit.Spring( ref droop, ref droopVel, droopWant, 2f, 0.7f, dt );
		SmoothKit.Spring( ref diveBlend, ref diveBlendVel, act is Act.Aim or Act.Dive ? 1f : 0f, act is Act.Aim or Act.Dive ? 2.2f : 1.2f, 0.9f, dt );
		charge = MathX.Lerp( charge, chargeWant, 1f - MathF.Exp( -12f * dt ) );
		stroke = MathX.Lerp( stroke, strokeAmp, 1f - MathF.Exp( -4f * dt ) );
		bankV = MathX.Lerp( bankV, MathX.Clamp( -yawRate * 0.2f, -0.6f, 0.6f ), 1f - MathF.Exp( -3f * dt ) );
		wavePhase += dt * (2.6f + speed * 1.4f) * MathF.Max( 0.4f, undulate );
		strokePhase += dt * MathF.Tau * strokeRate * 0.5f;

		// The body's turn: the ground-following pose, or (diving) along its path, spinning.
		var rest = GlobalBasis.Orthonormalized() * new Basis( Vector3.Right, pitch ) * new Basis( Vector3.Back, -roll );
		var spinning = act is Act.Aim or Act.Dive;
		spin += dt * (spinning ? (act == Act.Dive ? 22f : MathX.Lerp( 2f, 16f, MathX.Clamp( a / AimTime, 0f, 1f ) )) : 0f);
		if ( !spinning ) spin = Mathf.Wrap( spin, 0f, MathF.Tau ) * MathF.Max( 0f, 1f - dt * 4f );
		Quaternion q = rest.GetRotationQuaternion();
		if ( diveBlend > 0.01f && !diveDir.IsNearlyZero() )
		{
			var path = Shapes.LookAt( diveDir ) * new Basis( Vector3.Back, spin );
			q = q.Slerp( path.GetRotationQuaternion(), MathX.Clamp( diveBlend, 0f, 1f ) );
		}
		body.GlobalTransform = new Transform3D( new Basis( q ) * Basis.FromScale( Vector3.One * ModelScale ), GlobalPosition + Vector3.Up * MathF.Sin( t * 1.3f ) * 0.02f * (act == Act.Prowl ? 1f : 0f) );

		// The wings.
		var strokeNow = stroke * MathF.Sin( strokePhase );
		var watchDim = act == Act.Prowl && watched ? 0f : 1f;
		foreach ( var m in new[] { topSkin, bellySkin } )
		{
			m.SetShaderParameter( "wave_phase", wavePhase );
			m.SetShaderParameter( "undulate", undulate );
			m.SetShaderParameter( "stroke", strokeNow );
			m.SetShaderParameter( "droop", droop );
			m.SetShaderParameter( "bank", bankV );
			m.SetShaderParameter( "alert", alert * watchDim );
			m.SetShaderParameter( "charge", charge );
			m.SetShaderParameter( "flash", MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f ) );
			m.SetShaderParameter( "bruise", 1f - MathX.Clamp( health.Current / MathF.Max( 1f, health.MaxHealth ), 0f, 1f ) );
		}
		bulbSkin.SetShaderParameter( "flash", MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f ) );

		// The tail: a lazy whip that lags behind the body, flicking up when it rears, streaming straight in the dive.
		var stream = MathX.Clamp( diveBlend, 0f, 1f );
		for ( var i = 1; i < tail.Count; i++ )
		{
			var k = i / (float)tail.Count;
			var sway = MathF.Sin( t * 1.7f - i * 0.75f ) * 0.16f * (1f - stream) + (float)-yawRate * 0.05f * k;
			var lift = MathF.Sin( t * 1.1f - i * 0.6f ) * 0.05f + (act == Act.Zap ? 0.25f * charge : 0f) - pitch * 0.18f;
			tail[i].Rotation = new Vector3( lift * (1f - stream), sway, 0f );
		}

		// The zap's light flickers with the charge.
		zapLight.Visible = charge > 0.05f;
		if ( zapLight.Visible ) zapLight.LightEnergy = charge * (2.2f + 1.2f * MathF.Sin( t * 47f ));

		// The whirlpool: grows round it as it aims, trails behind the dive, fades as it pulls out.
		var swirl = act == Act.Aim ? MathX.Clamp( a / AimTime, 0f, 1f ) * 0.7f : act == Act.Dive ? 1f : 0f;
		vortexFade = MathX.Lerp( vortexFade, swirl, 1f - MathF.Exp( -8f * dt ) );
		vortex.Visible = vortexFade > 0.02f && !diveDir.IsNearlyZero();
		if ( vortex.Visible )
		{
			vortexPaint.SetShaderParameter( "fade", vortexFade );
			vortexPaint.SetShaderParameter( "spin", (float)Clock.Now * 3.2f );
			var back = -diveDir.Normalized();
			// The cylinder's +Y (its narrow end) points back along the path; the wide end rings the ray.
			vortex.GlobalTransform = new Transform3D( new Basis( new Quaternion( Vector3.Up, back ) ), GlobalPosition + back * 1.35f );
		}
	}

	float vortexFade;
}

/// <summary> Bolts of alien green electricity (the sting ray's zap). </summary>
public static class RayBolt
{
	static StandardMaterial3D bolt, core;

	static void Materials()
	{
		if ( bolt is not null ) return;
		bolt = new StandardMaterial3D { AlbedoColor = new Color( 0.35f, 1f, 0.8f ), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = new Color( 0.35f, 1f, 0.8f ), EmissionEnergyMultiplier = 5f };
		core = new StandardMaterial3D { AlbedoColor = new Color( 0.92f, 1f, 0.95f ), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = new Color( 0.9f, 1f, 0.95f ), EmissionEnergyMultiplier = 6f };
		bolt.SetMeta( "no_caustics", true );
		core.SetMeta( "no_caustics", true );
	}

	/// <summary> A jagged bolt from one point to another that lasts `life` seconds. </summary>
	public static void Arc( Node parent, Vector3 from, Vector3 to, float life = 0.12f, float width = 1f, bool light = true )
	{
		if ( parent is null ) return;
		Materials();
		var root = new Node3D { Name = "Ray Bolt" };
		parent.AddChild( root );
		var length = from.DistanceTo( to );
		var pieces = Math.Clamp( (int)(length / 0.25f), 3, 9 );
		var last = from;
		for ( var i = 1; i <= pieces; i++ )
		{
			var next = i == pieces ? to : from.Lerp( to, i / (float)pieces ) + Rng.InSphere( MathF.Min( 0.35f, length * 0.14f ) );
			Sculpt.Limb( root, last, next, 0.03f * width, 0.03f * width, bolt, "Bolt" );
			Sculpt.Limb( root, last, next, 0.012f * width, 0.012f * width, core, "Core" );
			last = next;
		}
		if ( light )
		{
			var l = new OmniLight3D { LightColor = new Color( 0.45f, 1f, 0.85f ), LightEnergy = 3f, OmniRange = 4.5f, ShadowEnabled = false };
			root.AddChild( l );
			l.GlobalPosition = (from + to) * 0.5f;
		}
		Fx.Expire( root, life );
	}
}

/// <summary>
/// A player being electrocuted by a sting ray: held in place (<see cref="PlayerController.Scripted"/>) for the stun, bolts
/// crackling over the robot and across the view, a flickering light and a shaking camera. Online, the host holds a remote
/// robot this way and its owner is held with it; a joiner's own copy only shows the bolts (`hold` false).
/// </summary>
public partial class Electrocuted : Node3D
{
	/// <summary> Tests: stuns that held a player. </summary>
	public static int Stuns;
	public static readonly List<Electrocuted> All = new();

	PlayerController who;
	float left, tick;
	bool holding;
	OmniLight3D light;

	public PlayerController Who => who;

	public static void Apply( PlayerController c, float seconds, bool hold )
	{
		if ( c is null || !IsInstanceValid( c ) || c.GetTree()?.CurrentScene is not Node scene ) return;
		var e = new Electrocuted { Name = "Electrocuted", who = c, left = seconds, TopLevel = true };
		scene.AddChild( e );
		e.GlobalPosition = c.GlobalPosition;
		if ( hold && !c.Scripted )
		{
			c.Scripted = true;
			c.Velocity = Vector3.Zero;
			e.holding = true;
			Stuns++;
		}
		e.light = new OmniLight3D { LightColor = new Color( 0.45f, 1f, 0.85f ), LightEnergy = 2.5f, OmniRange = 3.5f, ShadowEnabled = false, Position = Vector3.Up * 1.1f };
		e.AddChild( e.light );
		c.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.55f );
		Sfx.Play( "ray_zap", c.GlobalPosition + Vector3.Up, 1f, Rng.Float( 0.95f, 1.1f ) );
	}

	public override void _EnterTree() => All.Add( this );

	public override void _ExitTree()
	{
		All.Remove( this );
		Release();
	}

	void Release()
	{
		if ( !holding ) return;
		holding = false;
		if ( IsInstanceValid( who ) ) who.Scripted = false;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		left -= dt;
		if ( !IsInstanceValid( who ) || left <= 0f ) { Release(); QueueFree(); return; }
		GlobalPosition = who.GlobalPosition;
		if ( holding ) who.Velocity = Vector3.Zero;
		light.LightEnergy = Rng.Float( 0.8f, 3.2f );
		tick -= dt;
		if ( tick > 0f ) return;
		tick = 0.045f;
		var scene = GetTree().CurrentScene;
		// Over the robot: short bolts between points round its body.
		for ( var i = 0; i < 2; i++ )
		{
			var a = who.GlobalPosition + new Vector3( Rng.Float( -0.35f, 0.35f ), Rng.Float( 0.3f, 1.7f ), Rng.Float( -0.35f, 0.35f ) );
			RayBolt.Arc( scene, a, a + Rng.InSphere( 0.6f ), 0.06f, 0.6f, light: false );
		}
		// Across the view (the local player's own robot in first person sees them).
		if ( !who.IsRemote && who.Camera is { } cam && IsInstanceValid( cam ) )
		{
			var f = -cam.GlobalBasis.Z;
			var r = cam.GlobalBasis.X;
			var u = cam.GlobalBasis.Y;
			var c0 = cam.GlobalPosition + f * 0.45f;
			var from = c0 + r * Rng.Float( -0.35f, 0.35f ) + u * Rng.Float( -0.22f, 0.22f );
			var to = c0 + r * Rng.Float( -0.35f, 0.35f ) + u * Rng.Float( -0.22f, 0.22f );
			RayBolt.Arc( scene, from, to, 0.05f, 0.25f, light: false );
		}
	}
}
