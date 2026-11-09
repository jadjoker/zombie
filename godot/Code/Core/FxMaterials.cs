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
/// Port of s&box GlobalStylize: the whole-screen stylized look (stylize.gdshader) on a quad in front of the camera.
/// The water level uses Flat (4 bands, ink 0.9, width 1.6).
/// </summary>
public partial class GlobalStylize : MeshInstance3D
{
	public enum Look { Off, InkOutlines, Toon, Painterly, Retro, Storybook, Comic, Anime, Sketch, Watercolor, Riso, Flat }

	public Look Style = Look.Flat;
	public float Strength = 1f;
	public float Outline = 0.9f;
	public float OutlineWidth = 1.6f;
	public Color InkColor = new( 0.05f, 0.05f, 0.08f );
	public float Bands = 4f;
	public float PixelSize = 3f;

	public static GlobalStylize Current { get; private set; }
	ShaderMaterial material;

	public override void _Ready()
	{
		Name = "Global Stylize";
		Current = this;
		Mesh = new QuadMesh { Size = Vector2.One };
		material = new ShaderMaterial { Shader = GD.Load<Shader>( "res://assets/shaders/stylize.gdshader" ) };
		MaterialOverride = material;
		CastShadow = ShadowCastingSetting.Off;
		ExtraCullMargin = 16384f;
		SortingOffset = 1000f;
		Position = new Vector3( 0f, 0f, -0.5f );
	}

	public override void _ExitTree()
	{
		if ( Current == this ) Current = null;
	}

	public override void _Process( double delta )
	{
		Visible = Style != Look.Off;
		material.SetShaderParameter( "style", (int)Style );
		material.SetShaderParameter( "strength", Strength );
		material.SetShaderParameter( "outline", Outline );
		material.SetShaderParameter( "outline_width", OutlineWidth );
		material.SetShaderParameter( "ink", new Vector3( InkColor.R, InkColor.G, InkColor.B ) );
		material.SetShaderParameter( "bands", Bands );
		material.SetShaderParameter( "pixel_size", PixelSize );
	}
}
