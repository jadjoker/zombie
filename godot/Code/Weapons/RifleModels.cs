using Godot;

/// <summary>
/// The rifle's designs (Tools/blender/fps_rifle.py, RIFLE_VARIANT): the same gun and animations with a different
/// model. The dev menu switches between them (Player, Appearance, Rifle) and the choice is saved with the settings.
/// </summary>
public static class RifleModels
{
	/// <summary>
	/// One design: its menu name, model folder, whether aiming goes into the scope view, where the muzzle is on the
	/// gun bone (inches), how high its sight sits above the grip (inches, for the aim pose) and the muzzle flash tint.
	/// </summary>
	public sealed record Design( string Name, string Model, bool Scope, Vector3 Muzzle, float SightHeight, Color? Flash );

	public static readonly Design[] All =
	{
		new( "Blaster", "rifle_blaster", false, new Vector3( 0f, 36.2f, 3f ), 8.85f, new Color( 0.25f, 1f, 0.9f ) ),
		new( "Carbine", "rifle_carbine", false, new Vector3( 0f, 36.2f, 3f ), 7.5f, null ),
		new( "Marksman", "rifle_marksman", true, new Vector3( 0f, 37f, 3.6f ), 6.7f, null ),
		new( "Classic", "rifle", true, new Vector3( 0f, 36.2f, 3f ), 9.4f, null ),
	};

	public static string[] Names => All.Select( d => d.Name ).ToArray();

	/// <summary> The design in use. Models pick a change up on the next frame. </summary>
	public static string Current
	{
		get => GameSettings.Rifle;
		set
		{
			if ( GameSettings.Rifle == value || All.All( d => d.Name != value ) ) return;
			GameSettings.Rifle = value;
			GameSettings.Save();
		}
	}

	public static Design Get() => All.FirstOrDefault( d => d.Name == Current ) ?? All[0];

	/// <summary> The design a model folder belongs to (null for other weapons). </summary>
	public static Design ByModel( string model ) => All.FirstOrDefault( d => d.Model == model );
}
