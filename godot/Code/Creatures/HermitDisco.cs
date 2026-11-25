using Godot;

/// <summary>
/// The disco ball shell's show (user's ask): it spins at a party pace, its glints twinkle on and off, sparkles fly off
/// it in disco colours, and a ring of coloured spots sweeps round the sand under the crab as the ball turns. All of it
/// stops when the shell falls apart or the crab dies.
/// </summary>
public partial class HermitCrab
{
	/// <summary> The disco ball's turn, radians a second. </summary>
	const float DiscoSpin = 2.2f;
	const int DiscoSpots = 9;

	static readonly Color[] DiscoColours =
	{
		new( 1f, 0.35f, 0.75f ), new( 0.35f, 0.75f, 1f ), new( 1f, 0.9f, 0.35f ), new( 0.6f, 0.4f, 1f ), new( 0.4f, 1f, 0.8f ), new( 1f, 1f, 1f ),
	};

	readonly List<MeshInstance3D> glints = new();
	readonly List<float> glintPhase = new();
	GpuParticles3D sparkles;
	Node3D spots;
	float discoAngle;

	static ShaderMaterial sparkleMat, spotMat;

	void BuildDisco( Part shell )
	{
		foreach ( var _ in glints ) glintPhase.Add( Rng.Float( 0f, MathF.Tau ) );

		// Sparkles off the ball: little four-pointed stars in disco colours drifting out and fading.
		sparkleMat ??= new ShaderMaterial { Shader = new Shader { Code = SparkleShader } };
		var ramp = new Gradient();
		ramp.SetColor( 0, new Color( 1f, 1f, 1f, 1f ) );
		ramp.SetColor( 1, new Color( 1f, 1f, 1f, 0f ) );
		var hues = new Gradient { Offsets = new float[DiscoColours.Length], Colors = DiscoColours };
		for ( var i = 0; i < DiscoColours.Length; i++ ) hues.SetOffset( i, i / (float)(DiscoColours.Length - 1) );
		var process = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
			EmissionSphereRadius = 0.25f * ShellScale,
			Direction = Vector3.Up,
			Spread = 180f,
			InitialVelocityMin = 0.3f,
			InitialVelocityMax = 0.9f,
			Gravity = new Vector3( 0f, 0.15f, 0f ),
			DampingMin = 0.5f,
			DampingMax = 1f,
			ScaleMin = 0.6f,
			ScaleMax = 1.4f,
			ColorRamp = new GradientTexture1D { Gradient = ramp },
			ColorInitialRamp = new GradientTexture1D { Gradient = hues },
			AngleMin = 0f,
			AngleMax = 45f,
		};
		sparkles = new GpuParticles3D
		{
			Name = "Disco Sparkles",
			Amount = 28,
			Lifetime = 0.9,
			ProcessMaterial = process,
			DrawPass1 = new QuadMesh { Size = Vector2.One * 0.15f, Material = sparkleMat },
			LocalCoords = false,
			VisibilityAabb = new Aabb( -Vector3.One * 2f, Vector3.One * 4f ),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		shell.Go.AddChild( sparkles );
		sparkles.Position = shellRoot.Transform * shellMiddle;

		// The spots on the sand, swept round by the ball.
		spotMat ??= new ShaderMaterial { Shader = new Shader { Code = SpotShader } };
		spots = new Node3D { Name = "Disco Spots", TopLevel = true };
		AddChild( spots );
		for ( var k = 0; k < DiscoSpots; k++ )
		{
			var a = k / (float)DiscoSpots * MathF.Tau + Rng.Float( -0.2f, 0.2f );
			var r = Rng.Float( 1.1f, 2.6f ) * Size;
			var spot = new MeshInstance3D
			{
				Name = "Spot",
				Mesh = new PlaneMesh { Size = Vector2.One * Rng.Float( 0.3f, 0.45f ) * Size },
				MaterialOverride = spotMat,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Position = new Vector3( MathF.Cos( a ) * r, 0.04f, MathF.Sin( a ) * r ),
			};
			spot.SetInstanceShaderParameter( "colour", DiscoColours[k % DiscoColours.Length] );
			spots.AddChild( spot );
		}
	}

	void Disco( float delta )
	{
		if ( shellRoot is null || !IsInstanceValid( shellRoot ) ) return;
		discoAngle += delta * DiscoSpin;
		shellRoot.RotateY( delta * DiscoSpin );
		var t = (float)Clock.Now;
		for ( var i = 0; i < glints.Count; i++ )
		{
			var g = glints[i];
			if ( !IsInstanceValid( g ) ) continue;
			// A sharp twinkle now and then, a dim glow between.
			var w = MathF.Pow( MathF.Max( 0f, MathF.Sin( t * 2.6f + glintPhase[i] ) ), 6f );
			g.Scale = Vector3.One * (0.35f + 1.4f * w);
		}
		if ( spots is not null && IsInstanceValid( spots ) )
		{
			spots.Visible = !IsDead;
			spots.GlobalTransform = new Transform3D( new Basis( Vector3.Up, -discoAngle ), GlobalPosition );
		}
		if ( sparkles is not null && IsInstanceValid( sparkles ) ) sparkles.Emitting = !IsDead;
	}

	/// <summary> The ball is gone: the spots go dark and the sparkles stop. </summary>
	void EndDisco()
	{
		if ( spots is not null && IsInstanceValid( spots ) ) spots.QueueFree();
		spots = null;
		if ( sparkles is not null && IsInstanceValid( sparkles ) ) sparkles.Emitting = false;
		spins = false;
	}

	public override void _ExitTree()
	{
		if ( spots is not null && IsInstanceValid( spots ) ) spots.QueueFree();
		base._ExitTree();
	}

	/// <summary> Tests: the disco ball's sparkles are on and its spots are out. </summary>
	public bool DiscoGoing => spins && sparkles is not null && IsInstanceValid( sparkles ) && sparkles.Emitting && spots is not null && IsInstanceValid( spots ) && spots.GetChildCount() == DiscoSpots;

	const string SparkleShader = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
void vertex() {
	// Face the camera, keeping the particle's own size and spin.
	float size = length(MODEL_MATRIX[0].xyz);
	mat4 m = mat4(INV_VIEW_MATRIX[0] * size, INV_VIEW_MATRIX[1] * size, INV_VIEW_MATRIX[2] * size, MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = VIEW_MATRIX * m;
}
void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float star = max(1.0 - abs(p.x) * 7.0 - abs(p.y) * 0.9, 0.0) + max(1.0 - abs(p.y) * 7.0 - abs(p.x) * 0.9, 0.0);
	float core = max(1.0 - length(p) * 2.2, 0.0);
	float a = clamp(star + core * 1.5, 0.0, 1.0) * COLOR.a;
	ALBEDO = mix(COLOR.rgb, vec3(1.0), core) * a * 2.5;
}";

	const string SpotShader = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
instance uniform vec4 colour : source_color = vec4(1.0);
void fragment() {
	float d = length(UV * 2.0 - 1.0);
	float a = smoothstep(1.0, 0.35, d);
	ALBEDO = colour.rgb * a * 1.4;
}";
}
