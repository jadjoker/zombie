using Sandbox;

/// <summary>
/// Paint jobs for the third-person robot. Same idea as WeaponSkins: a material per slot
/// (body armour, frame, metal, accent, glow). Add a folder of those under
/// materials/characters/survivor/outfits/ and list it here; any shader can be used in the materials.
/// </summary>
public static class CharacterSkins
{
	public static string[] Names => new[] { "Default", "Rust", "Stealth", "Hazard", "Frost", "Gold" };
	public static string[] Slots => new[] { "body", "frame", "metal", "accent", "glow" };

	public static string Current { get; set; } = "Default";

	public static Material Resolve( string skin, string slot )
	{
		var path = skin == "Default"
			? $"materials/characters/survivor/{slot}.vmat"
			: $"materials/characters/survivor/outfits/{skin.ToLowerInvariant()}/{slot}.vmat";
		var material = Material.Load( path );
		return material is null || material.IsError ? null : material;
	}

	/// <summary> Colour of the lights (eye, chest, wrists, antenna) for each paint job. </summary>
	static Vector3 GlowOf( string skin ) => skin switch
	{
		"Rust" => new Vector3( 1f, 0.6f, 0.15f ),
		"Stealth" => new Vector3( 1f, 0.1f, 0.12f ),
		"Hazard" => new Vector3( 1f, 0.45f, 0.1f ),
		"Frost" => new Vector3( 0.4f, 0.7f, 1f ),
		"Gold" => new Vector3( 1f, 0.9f, 0.4f ),
		_ => new Vector3( 0.3f, 1f, 0.9f ),
	};

	/// <summary> True once every slot got its material (new material files compile on first use, so this can take a few frames). </summary>
	public static bool Apply( SkinnedModelRenderer renderer, string skin )
	{
		if ( !renderer.IsValid() || renderer.Model is null ) return false;
		var complete = true;
		renderer.Attributes.Set( "GlowColor", GlowOf( skin ) );
		renderer.Attributes.Set( "GlowStrength", 2.5f );
		var index = 0;
		foreach ( var original in renderer.Model.Materials )
		{
			var name = System.IO.Path.GetFileNameWithoutExtension( original?.Name ?? "" );
			if ( System.Array.IndexOf( Slots, name ) >= 0 )
			{
				var replacement = Resolve( skin, name );
				if ( replacement is not null ) renderer.Materials.SetOverride( index, replacement );
				else complete = false;
			}
			index++;
		}
		return complete;
	}
}
