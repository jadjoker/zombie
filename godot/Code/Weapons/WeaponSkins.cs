using Godot;

/// <summary>
/// Port of s&box WeaponSkins: per-slot material skins for the custom weapon models (gun metal, second metal, accent,
/// glove, sleeve). Colours and metalness come from the s&box .vmat files. Iridescent and Hologram used custom shaders
/// in s&box; until those are ported they fall back to flat colours.
/// </summary>
public static class WeaponSkins
{
	public static string[] Names => new[] { "Military", "Desert", "Arctic", "Crimson", "Gold", "Toxic", "Iridescent", "Hologram" };
	public static string[] Slots => new[] { "metal", "metal2", "accent", "glove", "sleeve" };

	/// <summary> The skin worn by every custom weapon model. Models pick changes up on the next frame. </summary>
	public static string Current { get; set; } = "Military";

	static readonly Dictionary<string, Dictionary<string, (Color Color, float Metal)>> table = new()
	{
		["Military"] = new()
		{
			["metal"] = (new Color( 0.22f, 0.24f, 0.26f ), 0.45f),
			["metal2"] = (new Color( 0.5f, 0.52f, 0.54f ), 0.5f),
			["accent"] = (new Color( 0.27f, 0.3f, 0.18f ), 0.0f),
			["glove"] = (new Color( 0.2f, 0.17f, 0.14f ), 0.0f),
			["sleeve"] = (new Color( 0.15f, 0.18f, 0.12f ), 0.0f),
			["lens"] = (new Color( 0.08f, 0.2f, 0.3f ), 0.6f),
			["wood"] = (new Color( 0.55f, 0.34f, 0.18f ), 0.0f),
			["axered"] = (new Color( 0.82f, 0.12f, 0.1f ), 0.0f),
		},
		["Arctic"] = new()
		{
			["metal"] = (new Color( 0.52f, 0.56f, 0.6f ), 0.8f),
			["metal2"] = (new Color( 0.8f, 0.83f, 0.86f ), 0.9f),
			["accent"] = (new Color( 0.88f, 0.9f, 0.92f ), 0.0f),
			["glove"] = (new Color( 0.62f, 0.65f, 0.7f ), 0.0f),
			["sleeve"] = (new Color( 0.74f, 0.78f, 0.82f ), 0.0f),
		},
		["Crimson"] = new()
		{
			["metal"] = (new Color( 0.06f, 0.06f, 0.07f ), 0.9f),
			["metal2"] = (new Color( 0.28f, 0.28f, 0.3f ), 0.9f),
			["accent"] = (new Color( 0.62f, 0.07f, 0.07f ), 0.2f),
			["glove"] = (new Color( 0.08f, 0.08f, 0.09f ), 0.0f),
			["sleeve"] = (new Color( 0.3f, 0.05f, 0.05f ), 0.0f),
		},
		["Desert"] = new()
		{
			["metal"] = (new Color( 0.22f, 0.19f, 0.15f ), 0.8f),
			["metal2"] = (new Color( 0.55f, 0.5f, 0.4f ), 0.9f),
			["accent"] = (new Color( 0.62f, 0.52f, 0.34f ), 0.0f),
			["glove"] = (new Color( 0.3f, 0.25f, 0.2f ), 0.0f),
			["sleeve"] = (new Color( 0.5f, 0.42f, 0.3f ), 0.0f),
		},
		["Gold"] = new()
		{
			["metal"] = (new Color( 0.85f, 0.62f, 0.18f ), 0.6f),
			["metal2"] = (new Color( 0.96f, 0.82f, 0.42f ), 0.6f),
			["accent"] = (new Color( 0.07f, 0.07f, 0.08f ), 0.3f),
			["glove"] = (new Color( 0.07f, 0.07f, 0.08f ), 0.0f),
			["sleeve"] = (new Color( 0.1f, 0.1f, 0.11f ), 0.0f),
		},
		["Toxic"] = new()
		{
			["metal"] = (new Color( 0.08f, 0.1f, 0.08f ), 0.85f),
			["metal2"] = (new Color( 0.35f, 0.4f, 0.33f ), 0.9f),
			["accent"] = (new Color( 0.45f, 0.85f, 0.1f ), 0.1f),
			["glove"] = (new Color( 0.1f, 0.12f, 0.1f ), 0.0f),
			["sleeve"] = (new Color( 0.12f, 0.2f, 0.1f ), 0.0f),
		},
		["Iridescent"] = new() { ["metal"] = (new Color( 0.35f, 0.25f, 0.6f ), 0.9f), ["metal2"] = (new Color( 0.3f, 0.7f, 0.7f ), 0.9f), ["accent"] = (new Color( 0.8f, 0.4f, 0.7f ), 0.6f) },
		["Hologram"] = new() { ["metal"] = (new Color( 0.2f, 0.7f, 0.9f ), 0f), ["metal2"] = (new Color( 0.5f, 0.9f, 1f ), 0f), ["accent"] = (new Color( 0.3f, 0.8f, 1f ), 0f) },
	};

	static readonly Dictionary<(string, string), StandardMaterial3D> cache = new();

	/// <summary> Fine mottled wear for the roughness (so painted metal isn't one flat sheen). </summary>
	static readonly NoiseTexture2D Wear = new()
	{
		Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true,
		Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.06f, FractalOctaves = 3 },
		ColorRamp = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color( 0.75f, 0.75f, 0.75f ), new Color( 1.15f, 1.15f, 1.15f ) } },
	};

	/// <summary> A skin's material for a slot (falls back to Military, then to null for slots no skin covers). </summary>
	public static StandardMaterial3D Resolve( string skin, string slot )
	{
		if ( cache.TryGetValue( (skin, slot), out var m ) ) return m;
		(Color Color, float Metal) entry;
		if ( !(table.TryGetValue( skin, out var s ) && s.TryGetValue( slot, out entry )) && !table["Military"].TryGetValue( slot, out entry ) ) return null;
		var metal = entry.Metal > 0.3f;
		m = new StandardMaterial3D
		{
			AlbedoColor = entry.Color, Metallic = entry.Metal, Roughness = metal ? 0.42f : 0.78f, ResourceName = slot,
			// A soft rim, fine wear in the roughness, and a clear coat on metal so it catches highlights.
			RimEnabled = true, Rim = metal ? 0.25f : 0.35f, RimTint = 0.6f,
			RoughnessTexture = Wear, RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red,
			ClearcoatEnabled = metal, Clearcoat = 0.35f, ClearcoatRoughness = 0.3f,
		};
		if ( skin == "Hologram" ) { m.EmissionEnabled = true; m.Emission = entry.Color; m.EmissionEnergyMultiplier = 1.2f; }
		cache[(skin, slot)] = m;
		return m;
	}

	/// <summary> Puts a skin on every mesh under a model by matching surface material names to slots. </summary>
	public static void Apply( Node model, string skin )
	{
		if ( skin is "Iridescent" or "Hologram" )
		{
			var fx = skin == "Iridescent" ? (Material)FxMaterials.Iridescent( Colors.White ) : FxMaterials.Hologram();
			foreach ( var mesh in model.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
				for ( var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++ )
					if ( Array.IndexOf( new[] { "metal", "metal2", "accent", "glove", "sleeve" }, mesh.Mesh.SurfaceGetMaterial( i )?.ResourceName ?? "" ) >= 0 )
						mesh.SetSurfaceOverrideMaterial( i, fx );
			return;
		}
		foreach ( var mesh in model.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			for ( var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++ )
			{
				var name = mesh.Mesh.SurfaceGetMaterial( i )?.ResourceName ?? "";
				var replacement = Resolve( skin, name );
				if ( replacement is not null ) mesh.SetSurfaceOverrideMaterial( i, replacement );
			}
		}
	}
}
