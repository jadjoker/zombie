using Godot;

/// <summary>
/// The s&box fx materials (materials/fx/*.vmat) as Godot shader materials: ghost, dissolve, hologram, iridescent,
/// glow bulb, and invisible. Each call gives a new material so its parameters can be set per object.
/// </summary>
public static class FxMaterials
{
	static readonly Dictionary<string, Shader> shaders = new();

	static Shader Load( string name )
	{
		if ( !shaders.TryGetValue( name, out var shader ) ) shaders[name] = shader = GD.Load<Shader>( $"res://assets/shaders/{name}.gdshader" );
		return shader;
	}

	static ShaderMaterial Make( string name, Color? tint = null )
	{
		var m = new ShaderMaterial { Shader = Load( name ) };
		if ( tint is { } t ) m.SetShaderParameter( "tint", t );
		return m;
	}

	/// <summary> fx_ghost.vmat: rim strength 3.5, power 1.6, mist 0.7. </summary>
	public static ShaderMaterial Ghost( Color? tint = null )
	{
		var m = Make( "fx_ghost", tint );
		m.SetShaderParameter( "rim_strength", 3.5f );
		m.SetShaderParameter( "rim_power", 1.6f );
		m.SetShaderParameter( "mist", 0.7f );
		return m;
	}

	public static ShaderMaterial Dissolve( Color? tint = null ) => Make( "fx_dissolve", tint );
	public static ShaderMaterial Hologram() => Make( "fx_hologram" );
	public static ShaderMaterial Iridescent( Color? tint = null ) => Make( "fx_iridescent", tint );

	public static ShaderMaterial GlowBulb( Color color, float strength = 4f )
	{
		var m = Make( "fx_glowbulb" );
		m.SetShaderParameter( "glow_color", color );
		m.SetShaderParameter( "glow_strength", strength );
		return m;
	}

	/// <summary> materials/fx/invisible.vmat: draws nothing. </summary>
	public static readonly StandardMaterial3D Invisible = new() { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color( 0, 0, 0, 0 ), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };

	/// <summary> The dev menu's creature skin effects by name (null for "Off"). </summary>
	public static Material ByName( string fx, Color tint ) => fx switch
	{
		"Dissolve" => Dissolve( tint ),
		"Ghost" => Ghost( tint ),
		"Holo" => Hologram(),
		"Iridescent" => Iridescent( tint ),
		_ => null,
	};
}

/// <summary>
/// Port of s&box GlobalStylize: the whole-screen stylized look. Sits under the camera and puts a compositor effect
/// on it (<see cref="StylizeEffect"/>, stylize_compute.glsl) that runs after the transparent pass. The water level
/// uses Flat (4 bands, ink 0.9, width 1.6), like its s&box camera, with that camera's vignette.
/// </summary>
public partial class GlobalStylize : Node
{
	public enum Look { Off, InkOutlines, Toon, Painterly, Retro, Storybook, Comic, Anime, Sketch, Watercolor, Riso, Flat }

	public Look Style = Look.Flat;
	public float Strength = 1f;
	public float Outline = 0.9f;
	public float OutlineWidth = 1.6f;
	public Color InkColor = new( 0.05f, 0.05f, 0.08f );
	public float Bands = 4f;
	public float PixelSize = 3f;
	public float Vignette = 0.28f;

	public static GlobalStylize Current { get; private set; }
	StylizeEffect effect;
	Godot.Environment env;
	Godot.Environment.ToneMapper original;

	public override void _Ready()
	{
		Name = "Global Stylize";
		Current = this;
		effect = new StylizeEffect();
		if ( GetParent() is Camera3D cam ) cam.Compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { effect } };
	}

	public override void _ExitTree()
	{
		if ( Current == this ) Current = null;
	}

	public override void _Process( double delta )
	{
		// s&box tonemaps before this pass: the scene's tonemapper goes linear while a look is on and ACES runs in it.
		env ??= GetViewport().World3D?.Environment ?? GetTree().Root.FindChildren( "*", "WorldEnvironment", true, false ).OfType<WorldEnvironment>().FirstOrDefault()?.Environment;
		if ( env is not null )
		{
			if ( env.TonemapMode != Godot.Environment.ToneMapper.Linear ) original = env.TonemapMode;
			env.TonemapMode = Style == Look.Off ? original : Godot.Environment.ToneMapper.Linear;
		}
		effect.Enabled = Style != Look.Off;
		var cam = GetParent() as Camera3D;
		effect.Params = new[] { (float)(int)Style, Strength, Outline, OutlineWidth, Bands, PixelSize, InkColor.R, InkColor.G, InkColor.B, Vignette, cam?.Near ?? 0.05f, cam?.Far ?? 4000f };
	}
}

/// <summary> The compute pass behind <see cref="GlobalStylize"/>: copies the colour, then writes the stylized image back. </summary>
[Tool]
public partial class StylizeEffect : CompositorEffect
{
	public float[] Params = { 11f, 1f, 0.9f, 1.6f, 4f, 3f, 0.05f, 0.05f, 0.08f, 0.28f, 0.05f, 4000f };
	RenderingDevice rd;
	Rid shader, pipeline, copyShader, copyPipeline, sampler, copyTex;
	Vector2I copySize;

	public StylizeEffect()
	{
		EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
		RenderingServer.CallOnRenderThread( Callable.From( Init ) );
	}

	void Init()
	{
		rd = RenderingServer.GetRenderingDevice();
		if ( rd is null ) return;
		var file = GD.Load<RDShaderFile>( "res://assets/shaders/stylize_compute.glsl" );
		shader = rd.ShaderCreateFromSpirV( file.GetSpirV() );
		pipeline = rd.ComputePipelineCreate( shader );
		copyShader = rd.ShaderCreateFromSpirV( GD.Load<RDShaderFile>( "res://assets/shaders/copy_compute.glsl" ).GetSpirV() );
		copyPipeline = rd.ComputePipelineCreate( copyShader );
		sampler = rd.SamplerCreate( new RDSamplerState { MinFilter = RenderingDevice.SamplerFilter.Linear, MagFilter = RenderingDevice.SamplerFilter.Linear, RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge } );
	}

	public override void _Notification( int what )
	{
		if ( what != NotificationPredelete || rd is null ) return;
		if ( copyTex.IsValid ) rd.FreeRid( copyTex );
		if ( copyShader.IsValid ) rd.FreeRid( copyShader );
		if ( sampler.IsValid ) rd.FreeRid( sampler );
		if ( shader.IsValid ) rd.FreeRid( shader );
	}

	public override void _RenderCallback( int effectCallbackType, RenderData renderData )
	{
		if ( rd is null || !pipeline.IsValid || effectCallbackType != (int)EffectCallbackTypeEnum.PostTransparent ) return;
		if ( renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers ) return;
		var size = buffers.GetInternalSize();
		if ( size.X == 0 || size.Y == 0 ) return;
		var pc = new float[16];
		pc[0] = size.X; pc[1] = size.Y;
		Array.Copy( Params, 0, pc, 2, Params.Length );
		pc[14] = 1f; pc[15] = 0f;   // tonemap on (near and far came in with the params)
		var bytes = new byte[pc.Length * 4];
		Buffer.BlockCopy( pc, 0, bytes, 0, bytes.Length );

		for ( uint view = 0; view < buffers.GetViewCount(); view++ )
		{
			var color = buffers.GetColorLayer( view );
			var depth = buffers.GetDepthLayer( view );
			EnsureCopy( color, size );
			var srcU = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
			srcU.AddId( color );
			var dstU = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
			dstU.AddId( copyTex );
			var copySet = UniformSetCacheRD.GetCache( copyShader, 0, new Godot.Collections.Array<RDUniform> { srcU, dstU } );
			var cl = rd.ComputeListBegin();
			rd.ComputeListBindComputePipeline( cl, copyPipeline );
			rd.ComputeListBindUniformSet( cl, copySet, 0 );
			rd.ComputeListDispatch( cl, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1 );
			rd.ComputeListEnd();

			var outU = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
			outU.AddId( color );
			var inU = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 };
			inU.AddId( sampler ); inU.AddId( copyTex );
			var depthU = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 2 };
			depthU.AddId( sampler ); depthU.AddId( depth );
			var set = UniformSetCacheRD.GetCache( shader, 0, new Godot.Collections.Array<RDUniform> { outU, inU, depthU } );

			var list = rd.ComputeListBegin();
			rd.ComputeListBindComputePipeline( list, pipeline );
			rd.ComputeListBindUniformSet( list, set, 0 );
			rd.ComputeListSetPushConstant( list, bytes, (uint)bytes.Length );
			rd.ComputeListDispatch( list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1 );
			rd.ComputeListEnd();
		}
	}

	void EnsureCopy( Rid color, Vector2I size )
	{
		if ( copyTex.IsValid && copySize == size ) return;
		if ( copyTex.IsValid ) rd.FreeRid( copyTex );
		var fmt = rd.TextureGetFormat( color );
		var format = new RDTextureFormat
		{
			Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
			Width = (uint)size.X,
			Height = (uint)size.Y,
			UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit,
		};
		copyTex = rd.TextureCreate( format, new RDTextureView() );
		copySize = size;
	}
}
