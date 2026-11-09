using Godot;

/// <summary>
/// GPU particle pools the CPU effects emit into (step 8): bubbles and sand puffs. One pool per kind per scene;
/// code emits each particle with its own size, life and delay (bubbles.gdshader, sand_puffs.gdshader), so the
/// behaviour matches the s&box per-object versions at a fraction of the cost.
/// </summary>
public static class GpuFx
{
	static GpuParticles3D bubbles, sand;

	static GpuParticles3D Pool( ref GpuParticles3D pool, string name, string process, string draw, int amount, float lifetime )
	{
		if ( pool is not null && GodotObject.IsInstanceValid( pool ) && pool.IsInsideTree() ) return pool;
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( root is null ) return null;
		pool = new GpuParticles3D
		{
			Name = name,
			Amount = amount,
			Lifetime = lifetime,
			Emitting = true,
			LocalCoords = false,
			ProcessMaterial = new ShaderMaterial { Shader = GD.Load<Shader>( $"res://assets/shaders/{process}.gdshader" ) },
			DrawPass1 = new QuadMesh { Size = Vector2.One, Material = SpriteMaterial( draw ) },
			VisibilityAabb = new Aabb( -Vector3.One * 5000f, Vector3.One * 10000f ),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		root.AddChild( pool );
		return pool;
	}

	const GpuParticles3D.EmitFlags Flags = GpuParticles3D.EmitFlags.Position | GpuParticles3D.EmitFlags.Velocity | GpuParticles3D.EmitFlags.Color | GpuParticles3D.EmitFlags.Custom;

	/// <summary> One bubble (size in s&box units, life and delay in seconds). </summary>
	/// <summary> The draw material, with its Blender-rendered sprite (Tools/blender/sprites.py) when there is one. </summary>
	static ShaderMaterial SpriteMaterial( string draw )
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>( $"res://assets/shaders/{draw}.gdshader" ) };
		var file = draw == "sprite_bubble" ? "bubble" : "puff";
		var path = $"res://assets/textures/fx/{file}.png";
		if ( ResourceLoader.Exists( path ) ) { m.SetShaderParameter( "sprite", GD.Load<Texture2D>( path ) ); m.SetShaderParameter( "use_sprite", true ); }
		return m;
	}

	public static void Bubble( Vector3 position, float size, float delay, float life )
	{
		var p = Pool( ref bubbles, "Bubbles", "bubbles", "sprite_bubble", 4000, 4f );
		p?.EmitParticle( new Transform3D( Basis.Identity, position ), Vector3.Zero, Colors.White, new Color( size, life, delay, 0f ), (uint)Flags );
	}

	/// <summary> One sand puff (velocity in m/s, size in s&box units, life in seconds). </summary>
	public static void SandPuff( Vector3 position, Vector3 velocity, Color color, float size, float life )
	{
		var p = Pool( ref sand, "Sand Puffs", "sand_puffs", "sprite_puff", 1500, 4f );
		p?.EmitParticle( new Transform3D( Basis.Identity, position ), velocity, color, new Color( size, life, 0f, 0f ), (uint)Flags );
	}
}
