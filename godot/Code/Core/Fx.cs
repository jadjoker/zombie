using Godot;

/// <summary>
/// Short-lived effects: the muzzle flash, bullet impacts and sparks. (The s&box game used Facepunch's stock particle
/// prefabs, which couldn't come along.)
/// </summary>
public static class Fx
{
	static Node Root => (Engine.GetMainLoop() as SceneTree)?.CurrentScene;

	/// <summary> Frees a node after a while. </summary>
	public static void Expire( Node node, float seconds )
	{
		var timer = node.GetTree().CreateTimer( seconds );
		timer.Timeout += () => { if ( GodotObject.IsInstanceValid( node ) ) node.QueueFree(); };
	}

	/// <summary> Testing aid: how long a muzzle flash lasts (screenshots need it to hold still). </summary>
	public static float FlashLife = 0.08f;

	static Shader flashShader, onTopShader;
	static readonly QuadMesh Quad = new() { Size = Vector2.One };
	static StandardMaterial3D smokeMaterial;

	/// <summary>
	/// A muzzle flash (assets/shaders/muzzle_flash.gdshader): a camera-facing star burst, three crossed flame tongues
	/// licking forward, two side jets out of the brake, a few sparks, a quick light and a puff of smoke left hanging
	/// in the air. Every shot is randomised. Rides along with the gun if given a parent. Forward is the barrel.
	/// </summary>
	public static void MuzzleFlash( Vector3 at, Vector3 forward, float scale, Node3D parent = null, Color? tint = null )
	{
		var scene = Root;
		if ( scene is null ) return;
		flashShader ??= GD.Load<Shader>( "res://assets/shaders/muzzle_flash.gdshader" );
		// On the first person gun it draws over walls like the gun itself does (it would vanish when the barrel pokes through one).
		onTopShader ??= new Shader { Code = flashShader.Code.Replace( "shadows_disabled;", "shadows_disabled, depth_test_disabled;" ) };
		var shader = parent is not null ? onTopShader : flashShader;
		forward = forward.Normalized();
		var k = scale / 0.3f * U.In;          // rifle = 1
		var root = new Node3D { Name = "Muzzle Flash" };
		(parent ?? scene).AddChild( root );
		root.GlobalPosition = at;
		var up = MathF.Abs( forward.Dot( Vector3.Up ) ) > 0.95f ? Vector3.Right : Vector3.Up;
		var side = forward.Cross( up ).Normalized();
		var materials = new List<ShaderMaterial>();

		ShaderMaterial Piece( int kind, float energy )
		{
			var m = new ShaderMaterial { Shader = shader };
			m.SetShaderParameter( "kind", kind );
			m.SetShaderParameter( "seed", Rng.Float( 0f, 1f ) );
			m.SetShaderParameter( "energy", energy );
			if ( tint is { } c ) { m.SetShaderParameter( "hot", c.Lightened( 0.75f ) ); m.SetShaderParameter( "warm", c ); }
			materials.Add( m );
			return m;
		}
		void Tongue( Vector3 dir, Vector3 across, float length, float width, float energy )
		{
			var mesh = new MeshInstance3D { Mesh = Quad, MaterialOverride = Piece( 1, energy ), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			root.AddChild( mesh );
			// Quad +Y is the base (UV y 0), so it points back at the muzzle.
			var x = across.Normalized() * width;
			var y = -dir.Normalized() * length;
			mesh.GlobalTransform = new Transform3D( new Basis( x, y, x.Cross( y ).Normalized() * 0.01f ), at + dir.Normalized() * length * 0.5f );
		}

		// The star, facing the camera.
		var star = new MeshInstance3D { Mesh = Quad, MaterialOverride = Piece( 0, 6f ), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		root.AddChild( star );
		star.GlobalPosition = at + forward * 1.2f * k;
		star.Scale = Vector3.One * Rng.Float( 13f, 17f ) * k;
		// Three crossed tongues forward, each its own length.
		var turn = Rng.Float( 0f, MathF.PI );
		for ( var i = 0; i < 3; i++ )
		{
			var across = side.Rotated( forward, turn + i * MathF.PI / 3f );
			Tongue( forward, across, Rng.Float( 14f, 22f ) * k, Rng.Float( 4.5f, 6.5f ) * k, 4f );
		}
		// Side jets out of the brake ports.
		foreach ( var s in new[] { -1f, 1f } )
			Tongue( (side * s + forward * 0.35f).Normalized(), forward.Cross( side ).Normalized(), Rng.Float( 5f, 8f ) * k, 2.4f * k, 3f );

		// The flash, light and all, gone in a few frames; it shrinks a little as it fades.
		var light = new OmniLight3D { LightColor = tint ?? new Color( 1f, 0.68f, 0.35f ), LightEnergy = 2.5f, OmniRange = 240f * U.In, OmniAttenuation = 1.6f, ShadowEnabled = false };
		root.AddChild( light );
		light.GlobalPosition = at + forward * 4f * k;
		var tween = root.CreateTween();
		tween.TweenMethod( Callable.From<float>( f =>
		{
			foreach ( var m in materials ) m.SetShaderParameter( "fade", f );
			light.LightEnergy = 2.5f * f;
			root.Scale = Vector3.One * (0.75f + 0.25f * f);
		} ), 1f, 0f, FlashLife ).SetEase( Tween.EaseType.In ).SetTrans( Tween.TransitionType.Quad );
		tween.TweenCallback( Callable.From( root.QueueFree ) );

		// Sparks: a few hot streaks thrown forward (left in the world, not on the gun).
		for ( var i = 0; i < 4; i++ )
		{
			var dir = (forward + side * Rng.Float( -0.35f, 0.35f ) + up * Rng.Float( -0.2f, 0.35f )).Normalized();
			var spark = new MeshInstance3D { Mesh = Quad, MaterialOverride = Piece( 1, 4f ), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			scene.AddChild( spark );
			var across = dir.Cross( up ).Normalized();
			var len = Rng.Float( 1.5f, 2.6f ) * k;
			spark.GlobalTransform = new Transform3D( new Basis( across * 0.25f * k, -dir * len, dir.Cross( across ) * 0.01f ), at + dir * 3f * k );
			var m = (ShaderMaterial)spark.MaterialOverride;
			var t = spark.CreateTween().SetParallel();
			var life = Rng.Float( 0.08f, 0.16f );
			t.TweenProperty( spark, "global_position", spark.GlobalPosition + dir * Rng.Float( 30f, 60f ) * k, life );
			t.TweenMethod( Callable.From<float>( f => m.SetShaderParameter( "fade", f ) ), 1f, 0f, life );
			t.Chain().TweenCallback( Callable.From( spark.QueueFree ) );
		}

		// A soft puff of smoke that hangs, grows and drifts up.
		smokeMaterial ??= new StandardMaterial3D
		{
			AlbedoTexture = new GradientTexture2D
			{
				Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2( 0.5f, 0.5f ), FillTo = new Vector2( 1f, 0.5f ), Width = 64, Height = 64,
				Gradient = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color( 1f, 1f, 1f, 1f ), new Color( 1f, 1f, 1f, 0f ) } },
			},
			AlbedoColor = new Color( 0.75f, 0.75f, 0.72f, 0.22f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			VertexColorUseAsAlbedo = false, DisableReceiveShadows = true,
		};
		var puff = new MeshInstance3D { Mesh = Quad, MaterialOverride = smokeMaterial.Duplicate() as StandardMaterial3D, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		scene.AddChild( puff );
		puff.GlobalPosition = at + forward * 3f * k;
		puff.Scale = Vector3.One * 3f * k;
		var pm = (StandardMaterial3D)puff.MaterialOverride;
		var pt = puff.CreateTween().SetParallel();
		pt.TweenProperty( puff, "scale", Vector3.One * 14f * k, 0.7f ).SetEase( Tween.EaseType.Out ).SetTrans( Tween.TransitionType.Cubic );
		pt.TweenProperty( puff, "global_position", puff.GlobalPosition + forward * 10f * k + Vector3.Up * 8f * k, 0.7f ).SetEase( Tween.EaseType.Out );
		pt.TweenProperty( pm, "albedo_color:a", 0f, 0.7f ).SetEase( Tween.EaseType.In );
		pt.Chain().TweenCallback( Callable.From( puff.QueueFree ) );
	}

	/// <summary> A bullet hitting the world: a quick spark and a small dark mark that fades. </summary>
	public static void ImpactWorld( Vector3 at, Vector3 normal )
	{
		var root = Root;
		if ( root is null ) return;
		var mark = new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 1.2f * U.In, BottomRadius = 1.2f * U.In, Height = 0.05f * U.In },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color( 0.05f, 0.05f, 0.05f ), Roughness = 1f },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		root.AddChild( mark );
		var up = normal.SafeNormal() == Vector3.Zero ? Vector3.Up : normal.Normalized();
		var side = MathF.Abs( up.Dot( Vector3.Up ) ) > 0.99f ? Vector3.Right : Vector3.Up.Cross( up ).Normalized();
		mark.GlobalTransform = new Transform3D( new Basis( side, up, side.Cross( up ) ), at + up * 0.02f * U.In );
		Expire( mark, 8f );
		Spark( at, up, new Color( 1f, 0.85f, 0.5f ) );
	}

	/// <summary> A hit on something alive: a splash of its colour. </summary>
	public static void ImpactFlesh( Vector3 at, Vector3 facing, Color color ) => Spark( at, facing, color, 4f );

	static void Spark( Vector3 at, Vector3 direction, Color color, float size = 2.5f )
	{
		var root = Root;
		if ( root is null ) return;
		var spark = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = size * U.In, Height = size * 2f * U.In },
			MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = color },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		root.AddChild( spark );
		spark.GlobalPosition = at + direction * size * U.In;
		var tween = spark.CreateTween();
		tween.TweenProperty( spark, "scale", Vector3.One * 0.1f, 0.12f );
		tween.TweenCallback( Callable.From( spark.QueueFree ) );
	}
}
