using Godot;

/// <summary>
/// The puffer bomber's looks: a round pufferfish body built in code, sandy yellow on the back over a white belly with
/// dark spots and glowing green alien freckles, the classic alien eyes (big glossy black almonds slanted out at the top
/// on a glowing green rim), a small puckered beak, fluttering fins and a fan tail, and spines all over that lie flat
/// against it until it swells, then stand straight out. While the fuse burns its skin goes hot red, flashing on each
/// tick, and a red ring on the sand shows the burst's reach.
/// </summary>
public partial class PufferBomber
{
	static readonly Color GooColour = new( 0.95f, 0.72f, 0.2f );
	static readonly Color BackColour = new( 0.86f, 0.72f, 0.3f );
	static readonly Color BellyColour = new( 0.95f, 0.93f, 0.85f );
	static readonly Color SpotColour = new( 0.24f, 0.18f, 0.1f );
	static readonly Color AlienGreen = new( 0.3f, 1f, 0.42f );
	static readonly StandardMaterial3D EyeBlack = new()
	{
		AlbedoColor = new Color( 0.01f, 0.01f, 0.015f ), Roughness = 0.06f, ClearcoatEnabled = true, Clearcoat = 1f, ClearcoatRoughness = 0.04f, Metallic = 0.2f,
	};
	static readonly StandardMaterial3D EyeRimGlow = Sculpt.Glow( new Color( 0.3f, 1f, 0.35f ), 1.3f );

	public override GibRecipe Gibs => GibRecipes.Puffer( GooColour, BackColour, BellyColour );

	static readonly Shader SkinShader = new()
	{
		Code = @"shader_type spatial;
uniform vec3 back : source_color;
uniform vec3 belly : source_color;
uniform vec3 spot : source_color;
uniform vec3 glow : source_color;
uniform float fuse = 0.0;    // 0 to 1 as the fuse burns
uniform float flash = 0.0;   // each tick of the fuse
uniform float hit = 0.0;     // a shot just landed
varying vec3 lp;
float hash3(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
void vertex() { lp = VERTEX; }
void fragment() {
	vec3 n = normalize(lp);
	float b = smoothstep(-0.2, 0.25, n.y);   // 1 on the back, 0 on the belly
	vec3 col = mix(belly, back, b);
	// Dark spots over the back.
	vec3 g = n * 6.5;
	vec3 cell = floor(g);
	vec3 c = vec3(hash3(cell), hash3(cell + 3.1), hash3(cell + 5.7)) * 0.5 + 0.25;
	float dots = (1.0 - smoothstep(0.2, 0.27, length(fract(g) - c))) * b;
	col = mix(col, spot, dots * 0.85);
	// Small glowing alien freckles, some of the cells only.
	vec3 g2 = n * 12.0 + 1.7;
	vec3 cell2 = floor(g2);
	vec3 c2 = vec3(hash3(cell2), hash3(cell2 + 2.3), hash3(cell2 + 4.9)) * 0.6 + 0.2;
	float fr = (1.0 - smoothstep(0.08, 0.13, length(fract(g2) - c2))) * step(0.62, hash3(cell2 + 9.1));
	vec3 hot = vec3(1.0, 0.16, 0.06);
	col = mix(col, hot, fuse * 0.5);
	ALBEDO = mix(col, vec3(1.0, 0.92, 0.88), hit * 0.7);
	EMISSION = glow * fr * 1.6 * (1.0 - fuse * 0.5) + hot * (fuse * 0.5 + flash * 2.6);
	ROUGHNESS = 0.55;
	SPECULAR = 0.4;
}",
	};

	/// <summary> The body (scaled evenly as it swells: its hit spheres are on it) and the look under it (squashes as it wobbles). </summary>
	Node3D body, look, tailJoint;
	ShaderMaterial skin;
	readonly List<(Node3D Pivot, Vector3 Normal, Vector3 Lie)> spines = new();
	readonly List<(Node3D Pivot, float Side)> fins = new();
	MeshInstance3D ring;
	ImmediateMesh ringMesh;
	float swell = 1f, swellVel, flinch;

	/// <summary> A point on the body's surface (it is a little longer than it is tall), in body space. </summary>
	static Vector3 Surface( Vector3 dir ) => new Vector3( dir.X * Radius, dir.Y * Radius * 0.92f, dir.Z * Radius * 1.06f );

	void Build()
	{
		skin = new ShaderMaterial { Shader = SkinShader };
		skin.SetShaderParameter( "back", BackColour );
		skin.SetShaderParameter( "belly", BellyColour );
		skin.SetShaderParameter( "spot", SpotColour );
		skin.SetShaderParameter( "glow", AlienGreen );

		body = new Node3D { Name = "Body" };
		AddChild( body );
		look = new Node3D { Name = "Look" };
		body.AddChild( look );
		SmoothKit.Ball( look, Vector3.Zero, new Vector3( 2f * Radius, 2f * Radius * 0.92f, 2f * Radius * 1.06f ), skin, null, "Puffer Body" );

		// The eyes: the classic alien eyes, high on each side of the head.
		var blinker = Blinker.Add( this );
		foreach ( var side in new[] { -1f, 1f } )
		{
			var gaze = new Vector3( side * 0.55f, 0.3f, -0.78f ).Normalized();
			var eye = new Node3D { Name = "Alien Eye", Position = Surface( gaze ) * 0.97f };
			look.AddChild( eye );
			var x = Vector3.Up.Cross( gaze ).Normalized();
			var y = gaze.Cross( x ).Normalized();
			eye.Basis = new Basis( x, y, gaze ) * new Basis( Vector3.Back, side * MathX.Rad( 38f ) );
			SmoothKit.Ball( eye, new Vector3( 0f, 0f, -0.025f ), new Vector3( 0.13f, 0.2f, 0.08f ), skin, null, "Eye Bulb" );
			var rim = Sculpt.Blob( eye, new Vector3( 0f, 0f, 0.012f ), new Vector3( 0.1f, 0.19f, 0.032f ), EyeRimGlow, name: "Eye Rim" );
			var black = Sculpt.Blob( eye, new Vector3( 0f, 0f, 0.016f ), new Vector3( 0.092f, 0.178f, 0.034f ), EyeBlack, name: "Eye" );
			blinker.Eye( rim, black );
			var part = CreaturePart.Create( this, side < 0f ? 1 : 2, side < 0f ? "Puffer Eye L" : "Puffer Eye R", 0.075f );
			body.AddChild( part );
			part.Position = eye.Position;
		}

		// A small puckered beak at the front.
		var lip = Sculpt.Paint( BackColour.Lerp( new Color( 0.95f, 0.55f, 0.35f ), 0.5f ), 0.5f );
		var front = Surface( new Vector3( 0f, -0.18f, -1f ).Normalized() );
		Sculpt.Blob( look, front + new Vector3( 0f, 0.022f, -0.01f ), new Vector3( 0.1f, 0.05f, 0.06f ), lip, name: "Upper Lip" );
		Sculpt.Blob( look, front + new Vector3( 0f, -0.022f, -0.01f ), new Vector3( 0.09f, 0.045f, 0.055f ), lip, name: "Lower Lip" );
		Sculpt.Blob( look, front + new Vector3( 0f, 0f, -0.03f ), new Vector3( 0.05f, 0.03f, 0.02f ), Sculpt.Paint( new Color( 0.15f, 0.04f, 0.05f ), 0.6f ), name: "Mouth" );
		var tooth = Sculpt.Paint( new Color( 0.97f, 0.96f, 0.9f ), 0.3f );
		Sculpt.Blob( look, front + new Vector3( 0f, 0.008f, -0.035f ), new Vector3( 0.04f, 0.014f, 0.012f ), tooth, name: "Beak" );
		Sculpt.Blob( look, front + new Vector3( 0f, -0.009f, -0.034f ), new Vector3( 0.036f, 0.012f, 0.012f ), tooth, name: "Beak" );

		// Fins: two fluttering pectorals behind the eyes, a little dorsal and anal fin, and a fan tail on a short stalk.
		var finPaint = SmoothKit.FinMaterial( BackColour.Lerp( Colors.White, 0.25f ), BackColour * 0.7f, 0.85f, 0.25f );
		foreach ( var side in new[] { -1f, 1f } )
		{
			var pivot = Sculpt.Joint( look, Surface( new Vector3( side, -0.05f, 0.05f ).Normalized() ) * 0.96f, "Pectoral" );
			SmoothKit.Fin( pivot, Vector3.Zero, SmoothKit.Aim( new Vector3( side, -0.1f, 0.6f ), Vector3.Up ), 0.14f, 0.11f, finPaint, 0.1f );
			fins.Add( (pivot, side) );
		}
		SmoothKit.Fin( look, Surface( new Vector3( 0f, 0.82f, 0.57f ).Normalized() ) * 0.97f, SmoothKit.Aim( new Vector3( 0f, 0.7f, 1f ), Vector3.Right ), 0.12f, 0.08f, finPaint, 0.05f );
		SmoothKit.Fin( look, Surface( new Vector3( 0f, -0.8f, 0.6f ).Normalized() ) * 0.97f, SmoothKit.Aim( new Vector3( 0f, -0.7f, 1f ), Vector3.Right ), 0.1f, 0.07f, finPaint, 0.05f );
		tailJoint = Sculpt.Joint( look, new Vector3( 0f, 0f, Radius * 0.95f ), "Tail" );
		SmoothKit.Tube( tailJoint, Vector3.Zero, new Vector3( 0f, 0f, 0.12f ), 0.07f, 0.04f, skin, "Tail Stalk" );
		SmoothKit.Fin( tailJoint, new Vector3( 0f, 0f, 0.1f ), SmoothKit.Aim( Vector3.Back, Vector3.Up ), 0.17f, 0.2f, finPaint, 0.06f );

		// The spines: spread evenly over the body (golden angle), clear of the eyes, the mouth and the tail.
		var spineMesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.016f, Height = 0.11f, RadialSegments = 5, Rings = 1 };
		var spinePaint = Sculpt.Paint( BellyColour.Lerp( BackColour, 0.35f ), 0.45f );
		const int count = 70;
		for ( var k = 0; k < count; k++ )
		{
			var yy = 1f - (k + 0.5f) / count * 2f;
			var r = MathF.Sqrt( 1f - yy * yy );
			var a = k * 2.399963f;
			var dir = new Vector3( MathF.Cos( a ) * r, yy, MathF.Sin( a ) * r );
			if ( dir.Z < -0.75f && MathF.Abs( dir.Y ) < 0.5f ) continue;   // the mouth
			if ( dir.Z > 0.85f ) continue;                                  // the tail
			if ( MathF.Abs( dir.X ) > 0.3f && dir.Z < -0.45f && dir.Y > 0f ) continue;   // the eyes
			var normal = Surface( dir ).Normalized();
			// Lying flat it points back along the body.
			var lie = (Vector3.Back - normal * normal.Dot( Vector3.Back )).SafeNormal();
			if ( lie == Vector3.Zero ) lie = Vector3.Down;
			var pivot = Sculpt.Joint( look, Surface( dir ) * 0.98f, "Spine" );
			var mesh = new MeshInstance3D { Name = "Spine", Mesh = spineMesh, MaterialOverride = spinePaint, Position = Vector3.Up * 0.055f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			pivot.AddChild( mesh );
			spines.Add( (pivot, normal, lie) );
		}

		// What can be shot: the body (0) and the eyes (1, 2).
		var core = CreaturePart.Create( this, 0, "Puffer Body", Radius * 1.05f );
		body.AddChild( core );

		ringMesh = new ImmediateMesh();
		var ringPaint = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled, VertexColorUseAsAlbedo = true,
		};
		ringPaint.SetMeta( "no_caustics", true );
		ring = new MeshInstance3D { Name = "Puffer Warning", Mesh = ringMesh, MaterialOverride = ringPaint, TopLevel = true, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( ring );
		ring.GlobalTransform = Transform3D.Identity;
	}

	protected override void Cleanup()
	{
		if ( ring is not null && IsInstanceValid( ring ) ) ring.QueueFree();
	}

	void Pose( float dt )
	{
		var now = (float)Clock.Now;
		var fuse = Fuse;
		// The swell: up with the fuse (fast at the end), and a little puff when it is shot.
		flinch = MathF.Max( 0f, flinch - dt * 2.5f );
		var want = act == Act.Fuse ? 1f + (Swell - 1f) * SmoothKit.Ease( fuse * 1.15f ) : 1f + 0.25f * flinch;
		SmoothKit.Spring( ref swell, ref swellVel, want, act == Act.Fuse ? 3.5f : 2f, 0.55f, dt );
		swell = MathF.Max( 0.8f, swell );
		var wobble = act == Act.Fuse ? MathF.Sin( now * 34f ) * 0.035f * fuse : MathF.Sin( now * 2.1f + phase ) * 0.015f;
		body.Scale = Vector3.One * swell;
		look.Scale = new Vector3( 1f + wobble, 1f - wobble, 1f + wobble * 0.5f );

		// Spines stand up as it swells.
		var raise = MathX.Clamp( (swell - 1f) / (Swell - 1f) * 1.4f, 0f, 1f );
		foreach ( var (pivot, normal, lie) in spines )
		{
			var dir = lie.Lerp( normal, 0.15f + 0.85f * raise ).Normalized();
			pivot.Basis = new Basis( new Quaternion( Vector3.Up, dir ) );
		}

		// Fins flutter (faster while it swims hard), the tail wags.
		var rate = act == Act.Fuse ? 22f : 12f;
		foreach ( var (pivot, side) in fins )
			pivot.Basis = new Basis( Vector3.Up, side * MathF.Sin( now * rate + phase ) * 0.5f );
		tailJoint.Basis = new Basis( Vector3.Up, MathF.Sin( now * 5f + phase ) * 0.4f );

		// The skin: hot with the fuse, a flash on each tick (the ticks come faster as it burns down).
		if ( act == Act.Fuse )
		{
			var gap = MathX.Lerp( 0.34f, 0.07f, fuse );
			if ( sinceTick > gap )
			{
				sinceTick = 0f;
				Sfx.Play( "puffer_tick", GlobalPosition, 0.7f, 1f + fuse * 0.7f );
			}
		}
		var flash = act == Act.Fuse ? MathF.Max( 0f, 1f - (float)sinceTick / 0.06f ) : 0f;
		skin.SetShaderParameter( "fuse", fuse );
		skin.SetShaderParameter( "flash", flash );
		skin.SetShaderParameter( "hit", MathF.Max( 0f, 1f - (float)sinceHit / 0.1f ) );
		DrawWarning( fuse, flash );
	}

	/// <summary> The red ring on the ground round it while the fuse burns: how far the burst reaches. </summary>
	void DrawWarning( float fuse, float flash )
	{
		if ( act != Act.Fuse || ring is null ) { if ( ring is not null ) ring.Visible = false; return; }
		ring.Visible = true;
		ringMesh.ClearSurfaces();
		ringMesh.SurfaceBegin( Mesh.PrimitiveType.Triangles );
		var p = GlobalPosition;
		var ground = Sculpt.GroundAt( p );
		var at = p with { Y = ground == float.MinValue ? rayFloor : MathF.Max( ground, rayFloor ) };
		var alpha = 0.35f + 0.35f * fuse + 0.3f * flash;
		var colour = new Color( 1f, 0.22f, 0.1f, alpha );
		GroundRing.Flat( ringMesh, this, at, WarnRadius - 0.14f, WarnRadius, colour, 40 );
		GroundRing.Flat( ringMesh, this, at, 0f, WarnRadius * fuse, colour with { A = 0.12f + 0.1f * flash }, 32 );
		ringMesh.SurfaceEnd();
	}
}
