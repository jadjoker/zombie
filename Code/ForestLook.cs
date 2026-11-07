/// <summary>
/// Gives forest props their material (triplanar nature shader) and a colour from a curated palette.
/// Used by ProceduralMap at runtime and by Tools/visual_pass.cs for the hand-dressed forest scene.
/// Palette entries are the sRGB colour we want on screen; renderer tints are linear multipliers on a
/// mid-grey texture, so they get converted and lifted to land on that colour.
/// </summary>
public static class ForestLook
{
	static Color[] Broadleaf => new[] { C( 0.22f, 0.40f, 0.12f ), C( 0.30f, 0.50f, 0.14f ), C( 0.42f, 0.58f, 0.16f ), C( 0.36f, 0.42f, 0.14f ), C( 0.26f, 0.44f, 0.18f ) };
	static Color[] Autumn => new[] { C( 0.72f, 0.42f, 0.12f ), C( 0.62f, 0.24f, 0.10f ), C( 0.70f, 0.58f, 0.16f ) };
	static Color[] Pine => new[] { C( 0.12f, 0.28f, 0.16f ), C( 0.15f, 0.33f, 0.18f ), C( 0.10f, 0.24f, 0.17f ) };
	static Color[] Bark => new[] { C( 0.36f, 0.30f, 0.25f ), C( 0.30f, 0.26f, 0.22f ), C( 0.42f, 0.36f, 0.29f ) };
	static Color PineBark => C( 0.40f, 0.30f, 0.24f );
	static Color[] Bush => new[] { C( 0.20f, 0.38f, 0.12f ), C( 0.40f, 0.42f, 0.18f ), C( 0.18f, 0.36f, 0.28f ), C( 0.16f, 0.30f, 0.12f ), C( 0.30f, 0.44f, 0.20f ) };
	static Color[] Grass => new[] { C( 0.32f, 0.50f, 0.14f ), C( 0.40f, 0.56f, 0.16f ), C( 0.28f, 0.44f, 0.12f ) };
	static Color[] Fern => new[] { C( 0.18f, 0.40f, 0.14f ), C( 0.24f, 0.46f, 0.16f ) };
	static Color[] Blossom => new[] { C( 0.80f, 0.42f, 0.56f ), C( 0.60f, 0.48f, 0.80f ), C( 0.82f, 0.66f, 0.24f ) };
	static Color[] Rock => new[] { C( 0.46f, 0.45f, 0.42f ), C( 0.40f, 0.40f, 0.38f ), C( 0.52f, 0.50f, 0.46f ) };
	static Color Hill => C( 0.42f, 0.42f, 0.40f );
	
	/// <summary> sRGB colour -> linear tint for a texture averaging about 0.8 sRGB. </summary>
	static Color C( float r, float g, float b )
	{
		const float lift = 2.6f;
		static float L( float c ) => MathF.Pow( c, 2.2f ) * lift;
		return new Color( L( r ), L( g ), L( b ) );
	}

	static T Pick<T>( System.Random rnd, T[] list ) => list[rnd.Next( list.Length )];

	/// <summary> Restyle a forest prop. Returns false if the model isn't a forest prop. </summary>
	public static bool Apply( ModelRenderer r, System.Random rnd )
	{
		var path = r.Model?.ResourcePath;
		if ( path is null || !path.Contains( "models/forest/" ) ) return false;
		var name = System.IO.Path.GetFileNameWithoutExtension( path );

		string mat;
		Color tint;
		if ( name.StartsWith( "tree_trunk" ) )
		{
			mat = "bark";
			tint = name == "tree_trunk_c" ? PineBark : Pick( rnd, Bark );
		}
		else if ( name == "tree_canopy_c" ) { mat = "pine"; tint = Pick( rnd, Pine ); }
		else if ( name.StartsWith( "tree_canopy" ) ) { mat = "leaves"; tint = rnd.NextDouble() < 0.06 ? Pick( rnd, Autumn ) : Pick( rnd, Broadleaf ); }
		else if ( name.StartsWith( "bush" ) ) { mat = "bush"; tint = Pick( rnd, Bush ); }
		else if ( name.StartsWith( "flowerbush" ) ) { mat = "blossom"; tint = Pick( rnd, Blossom ); }
		else if ( name.StartsWith( "grass" ) ) { mat = "grass"; tint = Pick( rnd, Grass ); }
		else if ( name.StartsWith( "fern" ) ) { mat = "grass"; tint = Pick( rnd, Fern ); }
		else if ( name.StartsWith( "log" ) || name.StartsWith( "stump" ) ) { mat = "log"; tint = Pick( rnd, Bark ); }
		else if ( name.StartsWith( "rock" ) )
		{
			var hill = r.WorldScale.x > 4f;
			mat = hill ? "hill" : "rock";
			tint = hill ? Hill : Pick( rnd, Rock );
		}
		else return false;

		// Small per-instance brightness jitter so neighbours never match exactly.
		var j = 0.9f + (float)rnd.NextDouble() * 0.2f;
		r.MaterialOverride = Material.Load( $"materials/nature/{mat}.vmat" );
		// Tints above 1 overflow in the renderer (they wrap to dark colours), so scale down to fit, keeping the hue.
		var c = new Color( tint.r * j, tint.g * j, tint.b * j );
		var max = MathF.Max( c.r, MathF.Max( c.g, c.b ) );
		if ( max > 1f ) c = new Color( c.r / max, c.g / max, c.b / max );
		r.Tint = c;
		return true;
	}
}
