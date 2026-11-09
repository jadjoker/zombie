using Sandbox;

/// <summary>
/// Skins for the custom first-person weapon models. A skin is a material per slot (metal, metal2, accent, glove, sleeve),
/// so any shader can be used: add a folder of five .vmat files under materials/weapons/skins/ and list it here,
/// or point every slot at one special-shader material (like the iridescent and hologram ones).
/// </summary>
public static class WeaponSkins
{
	public static string[] Names => new[] { "Military", "Desert", "Arctic", "Crimson", "Gold", "Toxic", "Iridescent", "Hologram" };
	public static string[] Slots => new[] { "metal", "metal2", "accent", "glove", "sleeve" };

	/// <summary> The skin worn by every custom weapon model. Change it any time; models pick it up on the next frame. </summary>
	public static string Current { get; set; } = "Military";

	/// <summary> A skin's material for a slot, or null if it doesn't exist. </summary>
	public static Material Resolve( string skin, string slot )
	{
		var path = skin switch
		{
			"Military" => $"materials/weapons/{slot}.vmat",
			"Iridescent" => "materials/fx/fx_iridescent.vmat",
			"Hologram" => "materials/fx/fx_hologram.vmat",
			_ => $"materials/weapons/skins/{skin.ToLowerInvariant()}/{slot}.vmat",
		};
		var material = Material.Load( path );
		return material is null || material.IsError ? null : material;
	}

	/// <summary> Put a skin on a model by overriding each of its material slots. </summary>
	public static void Apply( SkinnedModelRenderer renderer, string skin )
	{
		if ( !renderer.IsValid() || renderer.Model is null ) return;
		var materials = renderer.Model.Materials;
		var index = 0;
		foreach ( var original in materials )
		{
			var name = System.IO.Path.GetFileNameWithoutExtension( original?.Name ?? "" );
			if ( System.Array.IndexOf( Slots, name ) >= 0 )
			{
				var replacement = Resolve( skin, name );
				if ( replacement is not null ) renderer.Materials.SetOverride( index, replacement );
			}
			index++;
		}
	}
}
