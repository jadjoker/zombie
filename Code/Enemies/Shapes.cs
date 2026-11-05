/// <summary>
/// The simple shapes the alien creatures are built from. They're made in code from blobs, so every
/// piece is its own object that can be shot off.
/// </summary>
public static class Shapes
{
	static Model sphere;
	static Model box;

	public static Model Sphere => sphere ??= Model.Load( "models/dev/sphere.vmdl" );
	public static Model Box => box ??= Model.Load( "models/dev/box.vmdl" );

	/// <summary> Scale to give the box model so it's this size (x, y, z). </summary>
	public static Vector3 BoxScale( Vector3 size )
	{
		var b = Box.Bounds.Size;
		return new Vector3( size.x / MathF.Max( b.x, 0.01f ), size.y / MathF.Max( b.y, 0.01f ), size.z / MathF.Max( b.z, 0.01f ) );
	}

	/// <summary> Scale to give the sphere model so it has this radius. </summary>
	public static float SphereScale( float radius )
	{
		var size = Sphere.Bounds.Size.x;
		return size > 0.01f ? radius * 2f / size : radius / 32f;
	}

	static Material wet;
	/// <summary> A glossy material for creature skin, so they look wet instead of plastic. Null if missing. </summary>
	public static Material Wet => wet ??= Material.Load( "materials/shine.vmat" );
	public static bool UseWetSkin = true;

	/// <summary> Gives a creature renderer the wet look (if the material's available). </summary>
	public static void Skin( ModelRenderer renderer )
	{
		// The only glossy core material reads as chrome, not wet flesh, so this is off for now.
		// Kept as the one place to switch creature skin material later.
		return;
	}

	/// <summary> A glowing colour: bright enough to bloom. </summary>
	public static Color Glow( float hue, float strength = 1f ) => Hsv( hue, 0.9f, 1f ); // tints above white just clip, so pure and saturated reads best

	/// <summary> A bright, saturated colour from a hue (0-1, wraps), saturation and brightness. </summary>
	public static Color Hsv( float hue, float saturation, float value )
	{
		hue -= MathF.Floor( hue );
		return new ColorHsv( hue * 360f, saturation, value, 1f ).ToColor();
	}
}
