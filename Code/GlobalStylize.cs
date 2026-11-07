/// <summary>
/// Whole-screen stylized look (shaders/pp_stylize.shader). Add to the main camera and pick a style.
/// </summary>
[Title( "Global Stylize" ), Category( "Post Processing" ), Icon( "brush" )]
public sealed class GlobalStylize : BasePostProcess<GlobalStylize>
{
	public enum Look { Off, InkOutlines, Toon, Painterly, Retro, Storybook }

	[Property] public Look Style { get; set; } = Look.Off;
	[Property, Range( 0, 1 )] public float Strength { get; set; } = 1f;
	[Property, Range( 0, 1 )] public float Outline { get; set; } = 0.9f;
	[Property, Range( 1, 4 )] public float OutlineWidth { get; set; } = 1.6f;
	[Property] public Color InkColor { get; set; } = new( 0.05f, 0.05f, 0.08f );
	[Property, Range( 2, 8 )] public float Bands { get; set; } = 4f;
	[Property, Range( 1, 8 )] public float PixelSize { get; set; } = 3f;

	static Material shader;

	public override void Render()
	{
		if ( Style == Look.Off ) return;
		shader ??= Material.FromShader( "shaders/pp_stylize.shader" );

		Attributes.Set( "Style", (int)Style );
		Attributes.Set( "Strength", GetWeighted( x => x.Strength, 0f ) );
		Attributes.Set( "Outline", Outline );
		Attributes.Set( "OutlineWidth", OutlineWidth );
		Attributes.Set( "InkColor", new Vector3( InkColor.r, InkColor.g, InkColor.b ) );
		Attributes.Set( "Bands", Bands );
		Attributes.Set( "PixelSize", PixelSize );

		Blit( BlitMode.WithBackbuffer( shader, Sandbox.Rendering.Stage.AfterPostProcess, 50, false ), "Global Stylize" );
	}
}
