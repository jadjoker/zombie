using Godot;

/// <summary>
/// What each creature comes apart into (the chunking pass): its own skin on the outside, its inside, and the pieces
/// only it has. Built on first use and shared by every creature of the kind. Sizes in metres.
/// </summary>
public static class GibRecipes
{
	static readonly Dictionary<string, GibRecipe> made = new();
	static GibRecipe Get( string key, Func<GibRecipe> build ) => made.TryGetValue( key, out var r ) ? r : made[key] = build();

	static float R( Random random, float a, float b ) => a + (float)random.NextDouble() * (b - a);

	/// <summary>
	/// Colours snapped to a few shades, so creatures with slightly different random colours share one recipe (and its
	/// piece templates) instead of each building their own.
	/// </summary>
	static Color Q( Color c ) => new( MathF.Round( c.R * 6f ) / 6f, MathF.Round( c.G * 6f ) / 6f, MathF.Round( c.B * 6f ) / 6f, c.A );
	static readonly Color Bone = new( 0.93f, 0.9f, 0.8f ), Cartilage = new( 0.82f, 0.88f, 0.86f );

	/// <summary> A goo-wet meat colour from a creature's goo: the goo darkened toward red-brown. </summary>
	static Color Meat( Color goo ) => goo.Lerp( new Color( 0.62f, 0.18f, 0.22f ), 0.78f );

	// ---- Junk Crab and sea spider: chitin shell, pale meat, gills ----

	public static GibRecipe Crab( Color shell, Color belly, Color goo ) => CrabQ( Q( shell ), Q( belly ), Q( goo ) );

	static GibRecipe CrabQ( Color shell, Color belly, Color goo ) => Get( $"crab{shell}{belly}{goo}", () =>
	{
		var r = new GibRecipe
		{
			Kind = GibKind.Shell, Goo = goo, Shape = "crab",
			Outer = Shapes.Material( shell, "chitin" ),
			Layer = GibKit.Paint( belly.Lerp( Colors.White, 0.35f ), 0.6f ),
			Inner = GibKit.Paint( new Color( 0.93f, 0.66f, 0.6f ), 0.45f, wet: true ),   // crab meat, pink and wet
			Bone = null,
			Sinew = GibKit.Paint( goo.Lerp( Colors.White, 0.4f ), 0.3f, wet: true ),
		};
		var spike = GibKit.Paint( shell.Darkened( 0.3f ), 0.4f );
		// A claw tip: two curved hooks off a knuckle.
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Blob( p, Vector3.Zero, new Vector3( s * 0.5f, s * 0.4f, s * 0.5f ), r.Outer, null, "Knuckle" );
			Sculpt.Cone( p, new Vector3( 0f, 0.05f * s, 0f ), new Vector3( s * 0.15f, s * 0.1f, -s * 1.1f ), s * 0.18f, r.Outer, 6, "Hook" );
			Sculpt.Cone( p, new Vector3( 0f, -0.05f * s, 0f ), new Vector3( -s * 0.2f, -s * 0.15f, -s * 0.85f ), s * 0.15f, spike, 6, "Hook" );
			GibKit.Instance( p, GibKit.Lump( random, new Vector3( s * 0.35f, s * 0.3f, s * 0.12f ), 0.2f, 0, false ), r.Inner, r.Inner, "Meat" ).Position = new Vector3( 0f, 0f, s * 0.28f );
		} );
		// A leg section: a jointed tube, jagged at the break, with a spike.
		r.Specials.Add( ( random, p, s ) =>
		{
			var len = s * R( random, 1.2f, 1.8f );
			Sculpt.Limb( p, Vector3.Zero, Vector3.Forward * len, s * 0.16f, s * 0.11f, r.Outer, "Leg" );
			Sculpt.Blob( p, Vector3.Zero, Vector3.One * s * 0.34f, r.Outer, null, "Joint" );
			Sculpt.Cone( p, Vector3.Forward * len, Vector3.Forward * (len + s * 0.3f) + Vector3.Up * s * 0.08f, s * 0.1f, spike, 5, "Spike" );
			Sculpt.Blob( p, Vector3.Back * s * 0.05f, new Vector3( s * 0.26f, s * 0.26f, s * 0.08f ), r.Inner, null, "Torn" );
		} );
		// A gill fan: thin plates in a row.
		r.Specials.Add( ( random, p, s ) =>
		{
			var gill = GibKit.Paint( new Color( 0.85f, 0.45f, 0.5f ), 0.4f, wet: true );
			for ( var i = 0; i < 6; i++ )
				Sculpt.Block( p, new Vector3( (i - 2.5f) * s * 0.11f, 0f, 0f ), new Vector3( s * 0.03f, s * R( random, 0.4f, 0.6f ), s * 0.5f ), gill, new Basis( Vector3.Forward, (i - 2.5f) * 0.08f ), "Gill" );
		} );
		return r;
	} );

	/// <summary> The Junk Crab's eye on its antenna (the Kraken's eye on a stalk), thrown off when it bursts. </summary>
	public static void EyeStalk( Node3D p, float s, Material stalk, Color rim )
	{
		Sculpt.Limb( p, Vector3.Zero, new Vector3( 0f, s * 0.9f, s * 0.15f ), s * 0.06f, s * 0.04f, stalk, "Stalk" );
		Sculpt.Blob( p, new Vector3( 0f, s * 0.95f, s * 0.15f ), Vector3.One * s * 0.32f, GibKit.Paint( new Color( 0.24f, 0.46f, 0.42f ), 0.4f, wet: true ), null, "Bulb" );
		Sculpt.Blob( p, new Vector3( 0f, s * 0.97f, s * 0.0f ), new Vector3( s * 0.26f, s * 0.14f, s * 0.08f ), GibKit.Paint( new Color( 0.01f, 0.01f, 0.015f ), 0.1f, wet: true ), new Basis( Vector3.Forward, 0.66f ), "Eye" );
		Sculpt.Blob( p, new Vector3( 0f, s * 0.97f, s * 0.01f ), new Vector3( s * 0.3f, s * 0.17f, s * 0.06f ), GibKit.Paint( rim, 0.3f, glow: 2f ), new Basis( Vector3.Forward, 0.66f ), "Rim" );
	}

	// ---- Bloober and Squilien: spotted jelly, a glowing core, tentacle tips ----

	public static GibRecipe Jelly( Color body, Color spot, Color goo, bool suckers ) => JellyQ( Q( body ), Q( spot ), Q( goo ), suckers );

	static GibRecipe JellyQ( Color body, Color spot, Color goo, bool suckers ) => Get( $"jelly{body}{spot}{goo}{suckers}", () =>
	{
		var r = new GibRecipe
		{
			Kind = GibKind.Jelly, Goo = goo, SpecialShare = 0.4f, Shape = suckers ? "jelly-suckers" : "jelly",
			Outer = GibKit.JellyPaint( body, 0.75f ),
			Layer = GibKit.Paint( spot, 0.3f, wet: true, glow: 0.6f ),
			Inner = GibKit.JellyPaint( body.Lerp( goo, 0.5f ), 0.85f ),
			Sinew = GibKit.JellyPaint( goo.Lerp( Colors.White, 0.3f ), 0.8f ),
		};
		// A tentacle tip: tapering, curling, with suckers on the Squilien's.
		r.Specials.Add( ( random, p, s ) =>
		{
			var last = Vector3.Zero;
			var dir = Vector3.Forward;
			var radius = s * 0.16f;
			for ( var i = 0; i < 5; i++ )
			{
				dir = (new Basis( Vector3.Up, R( random, -0.5f, 0.5f ) ) * new Basis( Vector3.Right, 0.35f ) * dir).Normalized();
				var next = last + dir * s * 0.35f;
				Sculpt.Limb( p, last, next, radius, radius * 0.75f, r.Outer, "Tentacle" );
				if ( suckers ) Sculpt.Blob( p, (last + next) * 0.5f + Vector3.Down * radius * 0.7f, new Vector3( radius * 0.9f, radius * 0.3f, radius * 0.9f ), r.Layer, null, "Sucker" );
				last = next;
				radius *= 0.75f;
			}
			Sculpt.Blob( p, Vector3.Zero, Vector3.One * s * 0.34f, r.Inner, null, "Torn" );
		} );
		// The glowing core: a bright nodule in a sac of jelly.
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Blob( p, Vector3.Zero, Vector3.One * s * 0.45f, GibKit.Paint( goo, 0.2f, wet: true, glow: 2.2f ), null, "Core" );
			GibKit.Instance( p, GibKit.Lump( random, Vector3.One * s * 0.8f, 0.12f, 1, false ), r.Outer, r.Inner, "Sac" );
		} );
		// A spotted skin flap.
		r.Specials.Add( ( random, p, s ) =>
		{
			GibKit.Instance( p, GibKit.Shard( random, s, s * 0.8f, s * 0.25f, s * 0.06f ), r.Outer, r.Inner, "Flap" );
			for ( var i = 0; i < 3; i++ )
				Sculpt.Blob( p, new Vector3( R( random, -0.25f, 0.25f ) * s, s * 0.2f, R( random, -0.25f, 0.25f ) * s ), new Vector3( s * 0.2f, s * 0.05f, s * 0.2f ), r.Layer, null, "Spot" );
		} );
		return r;
	} );

	// ---- Rocky Shark: stone plates in its pebble skin, crater pieces, teeth, white belly ----

	public static GibRecipe RockyShark( Color goo ) => Get( "rockyshark", () =>
	{
		var pebble = Shapes.Material( Colors.White, "pebbles" );
		pebble.Uv1Scale = Vector3.One * 6f;
		var r = new GibRecipe
		{
			Kind = GibKind.Rock, Goo = goo,
			Outer = pebble,
			Inner = GibKit.Paint( new Color( 0.66f, 0.62f, 0.57f ), 0.95f ),   // fresh broken stone, paler than the weathered outside
			Layer = GibKit.Paint( new Color( 0.93f, 0.91f, 0.86f ), 0.7f ),
			Bone = GibKit.Paint( Bone, 0.5f ),
			Sinew = GibKit.Paint( Meat( goo ), 0.4f, wet: true ),
		};
		var glow = GibKit.Paint( goo, 0.3f, glow: 2.5f );
		var tooth = GibKit.Paint( new Color( 0.97f, 0.95f, 0.88f ), 0.3f, wet: true );
		// A crater piece: a stone with a glowing pit in it.
		r.Specials.Add( ( random, p, s ) =>
		{
			GibKit.Instance( p, GibKit.Lump( random, Vector3.One * s * new Vector3( 1.1f, 0.6f, 1f ), 0.2f, 1, true ), r.Outer, r.Inner, "Stone" );
			var rim = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = s * 0.12f, OuterRadius = s * 0.24f, Rings = 12, RingSegments = 6 }, MaterialOverride = r.Inner };
			p.AddChild( rim );
			rim.Position = Vector3.Up * s * 0.28f;
			Sculpt.Blob( p, Vector3.Up * s * 0.27f, new Vector3( s * 0.22f, s * 0.08f, s * 0.22f ), glow, null, "Pit" );
		} );
		// A tooth, root and all.
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Cone( p, Vector3.Zero, new Vector3( 0f, s * 0.9f, s * 0.12f ), s * 0.22f, tooth, 6, "Tooth" );
			Sculpt.Blob( p, Vector3.Down * s * 0.08f, new Vector3( s * 0.3f, s * 0.2f, s * 0.3f ), r.Sinew, null, "Root" );
		} );
		// A fin shard: a flat stone wedge, white underneath.
		r.Specials.Add( ( random, p, s ) =>
		{
			GibKit.Instance( p, GibKit.Shard( random, s * 1.4f, s * 0.8f, s * 0.05f, s * 0.14f ), r.Outer, r.Layer, "Fin" );
		} );
		// A glowing radiation nodule.
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Blob( p, Vector3.Zero, Vector3.One * s * 0.35f, glow, null, "Nodule" );
			GibKit.Instance( p, GibKit.Lump( random, Vector3.One * s * 0.6f, 0.2f, 2, true ), r.Outer, r.Inner, "Shell" ).Position = Vector3.Down * s * 0.15f;
		} );
		return r;
	} );

	/// <summary> Rubble off the Abyss's roof: dark cave rock, paler where it broke. </summary>
	public static GibRecipe CaveRock() => Get( "caverock", () => new GibRecipe
	{
		Kind = GibKind.Rock, Goo = new Color( 0.2f, 0.2f, 0.22f ),
		Outer = GibKit.Paint( new Color( 0.2f, 0.21f, 0.24f ), 0.95f ),
		Inner = GibKit.Paint( new Color( 0.42f, 0.42f, 0.45f ), 0.95f ),
		Layer = GibKit.Paint( new Color( 0.3f, 0.3f, 0.33f ), 0.9f ),
	} );

	/// <summary> The shark's belly: the same stone, white. </summary>
	public static GibRecipe RockyBelly( Color goo ) => Get( "rockybelly", () =>
	{
		var r = RockyShark( goo );
		return new GibRecipe { Kind = GibKind.Rock, Goo = goo, Outer = r.Layer, Inner = r.Inner, Bone = r.Bone, Sinew = r.Sinew };
	} );

	// ---- MC-51: stained glass mosaic in yellow leading, navy spikes, fins ----

	public static GibRecipe Mosaic( Color goo, Material mosaic ) => Get( "mosaic", () =>
	{
		var r = new GibRecipe
		{
			Kind = GibKind.Glass, Goo = goo, SpecialShare = 0.35f,
			Outer = mosaic ?? GibKit.Paint( new Color( 0.2f, 0.75f, 0.78f ), 0.2f ),
			Layer = GibKit.Paint( new Color( 1f, 0.82f, 0.2f ), 0.35f, metallic: 0.6f ),   // the leading
			Inner = GibKit.Paint( Meat( goo ), 0.4f, wet: true ),
			Bone = GibKit.Paint( Cartilage, 0.4f ),
			Sinew = GibKit.Paint( goo.Lerp( Colors.White, 0.3f ), 0.3f, wet: true ),
		};
		var navy = GibKit.Paint( new Color( 0.07f, 0.1f, 0.3f ), 0.35f );
		var fin = GibKit.JellyPaint( new Color( 0.3f, 0.85f, 0.8f ), 0.7f );
		// A back spike, broken off at the base.
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Cone( p, Vector3.Zero, new Vector3( 0f, s * 1.1f, s * 0.2f ), s * 0.25f, navy, 6, "Spike" );
			Sculpt.Blob( p, Vector3.Zero, new Vector3( s * 0.5f, s * 0.12f, s * 0.5f ), r.Outer, null, "Base" );
		} );
		// A torn fin: a translucent membrane over ribs.
		r.Specials.Add( ( random, p, s ) =>
		{
			GibKit.Instance( p, GibKit.Shard( random, s * 1.3f, s * 0.9f, s * 0.12f, s * 0.02f ), fin, fin, "Membrane" );
			for ( var i = 0; i < 3; i++ )
				Sculpt.Limb( p, Vector3.Zero, new Vector3( (i - 1) * s * 0.35f, s * 0.02f, -s * 0.55f ), s * 0.04f, s * 0.015f, r.Layer, "Rib" );
		} );
		// A cell of glass still in its leading.
		r.Specials.Add( ( random, p, s ) =>
		{
			GibKit.Instance( p, GibKit.Shard( random, s, s * 0.9f, s * 0.08f, s * 0.08f ), r.Outer, r.Layer, "Cell" );
			var frame = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = s * 0.36f, OuterRadius = s * 0.44f, Rings = 7, RingSegments = 4 }, MaterialOverride = r.Layer };
			p.AddChild( frame );
			frame.Position = Vector3.Up * s * 0.06f;
		} );
		return r;
	} );

	// ---- The Kraken: mud-crack hide over green-grey flesh, suckers, teeth ----

	public static GibRecipe Kraken( Color skin, Color goo ) => Get( "kraken", () =>
	{
		var hide = Shapes.Material( skin, "mudcrack" );
		var r = new GibRecipe
		{
			Kind = GibKind.Flesh, Goo = goo, SpecialShare = 0.25f,
			Outer = hide,
			Inner = GibKit.Paint( new Color( 0.62f, 0.3f, 0.36f ), 0.4f, wet: true ),
			Layer = GibKit.Paint( new Color( 0.9f, 0.85f, 0.62f ), 0.5f, wet: true ),   // blubber
			Bone = GibKit.Paint( Cartilage, 0.45f ),
			Sinew = GibKit.Paint( goo.Lerp( new Color( 0.9f, 0.8f, 0.8f ), 0.5f ), 0.3f, wet: true ),
		};
		var sucker = GibKit.Paint( new Color( 0.5f, 0.56f, 0.46f ), 0.4f, wet: true );
		// A strip of tentacle with a row of suckers.
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Limb( p, Vector3.Zero, Vector3.Forward * s * 1.3f, s * 0.3f, s * 0.2f, r.Outer, "Tentacle" );
			for ( var i = 0; i < 4; i++ )
			{
				var at = Vector3.Forward * s * (0.15f + i * 0.3f) + Vector3.Down * s * 0.22f;
				var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = s * 0.05f, OuterRadius = s * 0.11f, Rings = 10, RingSegments = 5 }, MaterialOverride = sucker };
				p.AddChild( ring );
				ring.Position = at;
			}
			Sculpt.Blob( p, Vector3.Zero, new Vector3( s * 0.5f, s * 0.5f, s * 0.12f ), r.Inner, null, "Torn" );
		} );
		// A tooth in a scrap of gum.
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Cone( p, Vector3.Zero, new Vector3( s * 0.1f, s * 0.8f, 0f ), s * 0.16f, GibKit.Paint( new Color( 0.95f, 0.92f, 0.8f ), 0.25f, wet: true ), 6, "Tooth" );
			GibKit.Instance( p, GibKit.Lump( random, new Vector3( s * 0.5f, s * 0.25f, s * 0.4f ), 0.2f, 1, false ), r.Inner, r.Layer, "Gum" );
		} );
		return r;
	} );

	// ---- Mermaids, seahorses and Poseidon: scales, fins, shell, gold ----

	public static GibRecipe Mermaid( Color goo, Color flesh ) => Get( $"mermaid{flesh}", () =>
	{
		var scales = GibKit.Paint( flesh, 0.35f, wet: true );
		var r = new GibRecipe
		{
			Kind = GibKind.Flesh, Goo = goo, SpecialShare = 0.4f,
			Outer = scales,
			Inner = GibKit.Paint( Meat( goo ), 0.4f, wet: true ),
			Layer = GibKit.Paint( flesh.Lerp( Colors.White, 0.5f ), 0.5f, wet: true ),
			Bone = GibKit.Paint( Bone, 0.45f ),
			Sinew = GibKit.Paint( goo.Lerp( Colors.White, 0.35f ), 0.3f, wet: true ),
		};
		var shell = GibKit.Paint( new Color( 0.97f, 0.83f, 0.74f ), 0.4f );
		var shellIn = GibKit.Paint( new Color( 0.86f, 0.55f, 0.5f ), 0.3f, wet: true );
		var fin = GibKit.JellyPaint( flesh.Lerp( Colors.White, 0.3f ), 0.65f );
		// A patch of scales: overlapping little plates on a scrap of skin.
		r.Specials.Add( ( random, p, s ) =>
		{
			GibKit.Instance( p, GibKit.Shard( random, s, s * 0.8f, s * 0.15f, s * 0.05f ), r.Outer, r.Inner, "Skin" );
			for ( var i = 0; i < 6; i++ )
				Sculpt.Blob( p, new Vector3( R( random, -0.3f, 0.3f ) * s, s * 0.1f, R( random, -0.3f, 0.3f ) * s ), new Vector3( s * 0.18f, s * 0.03f, s * 0.14f ), r.Layer, new Basis( Vector3.Right, 0.3f ), "Scale" );
		} );
		// A torn fin.
		r.Specials.Add( ( random, p, s ) => GibKit.Instance( p, GibKit.Shard( random, s * 1.4f, s, s * 0.1f, s * 0.02f ), fin, fin, "Fin" ) );
		// A piece of the scallop shell mask.
		r.Specials.Add( ( random, p, s ) =>
		{
			GibKit.Instance( p, GibKit.Shard( random, s, s * 0.9f, s * 0.3f, s * 0.06f ), shell, shellIn, "Shell" );
			for ( var i = -1; i <= 1; i++ )
				Sculpt.Block( p, new Vector3( i * s * 0.18f, s * 0.24f, 0f ), new Vector3( s * 0.05f, s * 0.04f, s * 0.7f ), shellIn, new Basis( Vector3.Up, i * 0.3f ), "Ridge" );
		} );
		return r;
	} );

	public static GibRecipe Seahorse( Color goo, Color yellow, Color green ) => Get( "seahorse", () =>
	{
		var r = new GibRecipe
		{
			Kind = GibKind.Armour, Goo = goo, SpecialShare = 0.3f,
			Outer = GibKit.Paint( yellow, 0.4f ),
			Layer = GibKit.Paint( green, 0.45f ),
			Inner = GibKit.Paint( Meat( goo ), 0.4f, wet: true ),
			Bone = GibKit.Paint( Cartilage, 0.4f ),
			Sinew = GibKit.Paint( goo.Lerp( Colors.White, 0.3f ), 0.3f, wet: true ),
		};
		// A ringed body segment with a little spike.
		r.Specials.Add( ( random, p, s ) =>
		{
			var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = s * 0.2f, OuterRadius = s * 0.42f, Rings = 10, RingSegments = 6 }, MaterialOverride = r.Outer };
			p.AddChild( ring );
			Sculpt.Blob( p, Vector3.Zero, Vector3.One * s * 0.42f, r.Inner, null, "Meat" );
			Sculpt.Cone( p, Vector3.Up * s * 0.3f, Vector3.Up * s * 0.7f, s * 0.08f, r.Layer, 5, "Spike" );
		} );
		return r;
	} );

	/// <summary> The garden eel: its skin colour (green, purple or orange) with black spots, red meat, and a spotted scrap of its body. </summary>
	public static GibRecipe GardenEel( Color goo, Color cream, Color spots ) => Get( $"garden eel {cream.ToHtml( false )}", () =>
	{
		var r = new GibRecipe
		{
			Kind = GibKind.Flesh, Goo = goo, SpecialShare = 0.35f,
			Outer = GibKit.Paint( cream, 0.5f ),
			Inner = GibKit.Paint( Meat( goo ), 0.4f, wet: true ),
			Layer = GibKit.Paint( cream.Lerp( Colors.White, 0.3f ), 0.45f ),
			Bone = GibKit.Paint( Bone, 0.4f ),
			Sinew = GibKit.Paint( goo.Lerp( Colors.White, 0.35f ), 0.3f, wet: true ),
		};
		var black = GibKit.Paint( spots, 0.5f );
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Limb( p, Vector3.Zero, new Vector3( 0f, 0f, s * 1.4f ), s * 0.28f, s * 0.26f, r.Outer, "Eel Scrap" );
			for ( var k = 0; k < 4; k++ )
				Sculpt.Blob( p, new Vector3( R( random, -0.2f, 0.2f ) * s, s * 0.25f, R( random, 0.1f, 1.3f ) * s ), Vector3.One * s * 0.1f, black, null, "Spot" );
		} );
		return r;
	} );

	/// <summary> The mimic octopus: mottled skin over pale flesh, and pieces of arm with suckers on. </summary>
	public static GibRecipe Octopus( Color goo, Color skin, Color sucker ) => Get( "octopus", () =>
	{
		var r = new GibRecipe
		{
			Kind = GibKind.Flesh, Goo = goo, SpecialShare = 0.45f,
			Outer = GibKit.Paint( skin, 0.4f ),
			Inner = GibKit.Paint( new Color( 0.95f, 0.85f, 0.85f ), 0.4f, wet: true ),
			Layer = GibKit.Paint( skin.Lerp( Colors.White, 0.35f ), 0.45f ),
			Sinew = GibKit.Paint( goo.Lerp( Colors.White, 0.4f ), 0.3f, wet: true ),
		};
		var cup = GibKit.Paint( sucker, 0.35f );
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Limb( p, Vector3.Zero, new Vector3( 0f, 0f, s * 1.5f ), s * 0.32f, s * 0.2f, r.Outer, "Arm Piece" );
			for ( var k = 0; k < 4; k++ )
				Sculpt.Blob( p, new Vector3( 0f, -s * 0.24f, (0.2f + k * 0.32f) * s ), new Vector3( s * 0.22f, s * 0.08f, s * 0.22f ), cup, null, "Sucker" );
		} );
		return r;
	} );

	/// <summary> The puffer bomber: spotted sandy hide over a white belly, pale meat, and scraps of skin still bristling with spines. </summary>
	public static GibRecipe Puffer( Color goo, Color back, Color belly ) => Get( "puffer", () =>
	{
		var r = new GibRecipe
		{
			Kind = GibKind.Flesh, Goo = goo, SpecialShare = 0.45f,
			Outer = GibKit.Paint( back, 0.55f ),
			Inner = GibKit.Paint( Meat( goo ), 0.4f, wet: true ),
			Layer = GibKit.Paint( belly, 0.45f ),
			Bone = GibKit.Paint( Bone, 0.4f ),
			Sinew = GibKit.Paint( goo.Lerp( Colors.White, 0.35f ), 0.3f, wet: true ),
		};
		var spine = GibKit.Paint( belly.Lerp( back, 0.35f ), 0.45f );
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Blob( p, Vector3.Zero, new Vector3( s * 1.2f, s * 0.25f, s * 1f ), r.Outer, null, "Spiny Hide" );
			for ( var k = 0; k < 4; k++ )
			{
				var at = new Vector3( R( random, -0.4f, 0.4f ) * s, s * 0.1f, R( random, -0.35f, 0.35f ) * s );
				Sculpt.Cone( p, at, at + new Vector3( R( random, -0.2f, 0.2f ), 1f, R( random, -0.2f, 0.2f ) ).Normalized() * s * 0.8f, s * 0.07f, spine, 5, "Spine" );
			}
		} );
		return r;
	} );

	/// <summary> The cleaner wrasse: blue and pale striped hide, pale meat, and scraps of its black-striped fins. </summary>
	public static GibRecipe Cleaner( Color goo, Color blue, Color pale ) => Get( "cleaner", () =>
	{
		var r = new GibRecipe
		{
			Kind = GibKind.Flesh, Goo = goo, SpecialShare = 0.4f,
			Outer = GibKit.Paint( blue, 0.45f ),
			Inner = GibKit.Paint( Meat( goo ), 0.4f, wet: true ),
			Layer = GibKit.Paint( pale, 0.45f ),
			Bone = GibKit.Paint( Bone, 0.4f ),
			Sinew = GibKit.Paint( goo.Lerp( Colors.White, 0.35f ), 0.3f, wet: true ),
		};
		var stripe = GibKit.Paint( new Color( 0.03f, 0.03f, 0.05f ), 0.5f );
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Blob( p, Vector3.Zero, new Vector3( s * 1.3f, s * 0.22f, s * 0.8f ), r.Outer, null, "Striped Hide" );
			Sculpt.Blob( p, new Vector3( 0f, s * 0.06f, 0f ), new Vector3( s * 1.2f, s * 0.12f, s * 0.22f ), stripe, null, "Stripe" );
		} );
		return r;
	} );

	/// <summary> The sting ray: a sand camouflaged hide over a pale belly, cartilage, torn wing scraps and its glowing barb. </summary>
	public static GibRecipe Stingray( Color goo, Color sand, Color belly ) => Get( "stingray", () =>
	{
		var r = new GibRecipe
		{
			Kind = GibKind.Flesh, Goo = goo, SpecialShare = 0.4f,
			Outer = GibKit.Paint( sand, 0.8f ),
			Inner = GibKit.Paint( Meat( goo ), 0.4f, wet: true ),
			Layer = GibKit.Paint( belly, 0.45f ),
			Bone = GibKit.Paint( Cartilage, 0.4f ),
			Sinew = GibKit.Paint( goo.Lerp( Colors.White, 0.35f ), 0.3f, wet: true ),
		};
		var barb = GibKit.Paint( new Color( 0.3f, 1f, 0.45f ), 0.3f, glow: 2f );
		// A scrap of wing: sandy on top, pale underneath.
		r.Specials.Add( ( random, p, s ) =>
		{
			GibKit.Instance( p, GibKit.Shard( random, s * 1.5f, s * 1.1f, s * 0.12f, s * 0.05f ), r.Outer, r.Layer, "Wing" );
		} );
		// The barb: a serrated glowing spine on a scrap of tail.
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Limb( p, Vector3.Zero, new Vector3( 0f, 0f, s * 1.2f ), s * 0.1f, s * 0.06f, r.Outer, "Tail" );
			Sculpt.Cone( p, new Vector3( 0f, s * 0.06f, s * 0.2f ), new Vector3( 0f, s * 0.2f, s * 0.9f ), s * 0.08f, barb, 5, "Barb" );
		} );
		// Cartilage: a pale ray of gristle with meat on it.
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Limb( p, Vector3.Zero, new Vector3( R( random, -0.2f, 0.2f ) * s, 0f, s * 1.3f ), s * 0.08f, s * 0.05f, r.Bone, "Gristle" );
			GibKit.Instance( p, GibKit.Lump( random, new Vector3( s * 0.4f, s * 0.2f, s * 0.5f ), 0.25f, 0, false ), r.Inner, r.Inner, "Meat" );
		} );
		return r;
	} );

	/// <summary> The deep sea angler: blue and green scaled hide, long needle teeth, torn fins, bits of its compound eyes. </summary>
	public static GibRecipe Angler( Color goo, Color blue, Color green, Color tooth ) => Get( "angler", () =>
	{
		var r = new GibRecipe
		{
			Kind = GibKind.Flesh, Goo = goo, SpecialShare = 0.45f,
			Outer = GibKit.Paint( blue, 0.35f, wet: true ),
			Inner = GibKit.Paint( Meat( goo ), 0.4f, wet: true ),
			Layer = GibKit.Paint( green, 0.4f, wet: true ),
			Bone = GibKit.Paint( Bone, 0.45f ),
			Sinew = GibKit.Paint( goo.Lerp( Colors.White, 0.35f ), 0.3f, wet: true ),
		};
		var fang = GibKit.Paint( tooth, 0.25f );
		var fin = GibKit.JellyPaint( new Color( 0.3f, 0.75f, 0.8f ), 0.65f );
		var eye = GibKit.Paint( new Color( 0.55f, 0.06f, 0.08f ), 0.1f );
		// A patch of scales, blue and green plates on a scrap of hide.
		r.Specials.Add( ( random, p, s ) =>
		{
			GibKit.Instance( p, GibKit.Shard( random, s, s * 0.8f, s * 0.15f, s * 0.05f ), r.Outer, r.Inner, "Hide" );
			for ( var i = 0; i < 6; i++ )
				Sculpt.Blob( p, new Vector3( R( random, -0.3f, 0.3f ) * s, s * 0.1f, R( random, -0.3f, 0.3f ) * s ), new Vector3( s * 0.18f, s * 0.03f, s * 0.14f ), i % 2 == 0 ? r.Layer : r.Outer, new Basis( Vector3.Right, 0.3f ), "Scale" );
		} );
		// A long needle tooth with a scrap of gum.
		r.Specials.Add( ( random, p, s ) =>
		{
			Sculpt.Cone( p, Vector3.Zero, new Vector3( R( random, -0.1f, 0.1f ) * s, s * 1.6f, 0f ), s * 0.14f, fang, 6, "Fang" );
			Sculpt.Blob( p, Vector3.Zero, new Vector3( s * 0.4f, s * 0.2f, s * 0.35f ), r.Inner, null, "Gum" );
		} );
		// A torn fin.
		r.Specials.Add( ( random, p, s ) => GibKit.Instance( p, GibKit.Shard( random, s * 1.4f, s, s * 0.1f, s * 0.02f ), fin, fin, "Fin" ) );
		// A piece of a compound eye.
		r.Specials.Add( ( random, p, s ) =>
		{
			GibKit.Instance( p, GibKit.Shard( random, s * 0.8f, s * 0.8f, s * 0.35f, s * 0.05f ), eye, r.Inner, "Eye" );
		} );
		return r;
	} );

	public static GibRecipe Poseidon( Color goo, Color skin, Color scales, Color armour ) => Get( "poseidon", () =>
	{
		var r = new GibRecipe
		{
			Kind = GibKind.Flesh, Goo = goo, SpecialShare = 0.35f,
			Outer = GibKit.Paint( skin, 0.45f, wet: true ),
			Inner = GibKit.Paint( Meat( goo ), 0.4f, wet: true ),
			Layer = GibKit.Paint( new Color( 0.95f, 0.88f, 0.66f ), 0.5f, wet: true ),
			Bone = GibKit.Paint( Bone, 0.45f ),
			Sinew = GibKit.Paint( goo.Lerp( Colors.White, 0.35f ), 0.3f, wet: true ),
		};
		var gold = GibKit.Paint( armour, 0.25f, metallic: 0.85f );
		var scale = GibKit.Paint( scales, 0.35f, wet: true );
		// A dented gold armour plate.
		r.Specials.Add( ( random, p, s ) => GibKit.Instance( p, GibKit.Shard( random, s * 1.3f, s, s * 0.25f, s * 0.06f ), gold, gold, "Plate" ) );
		// A patch of tail scales.
		r.Specials.Add( ( random, p, s ) =>
		{
			GibKit.Instance( p, GibKit.Shard( random, s, s * 0.8f, s * 0.2f, s * 0.08f ), scale, r.Inner, "Scales" );
			for ( var i = 0; i < 5; i++ )
				Sculpt.Blob( p, new Vector3( R( random, -0.3f, 0.3f ) * s, s * 0.14f, R( random, -0.3f, 0.3f ) * s ), new Vector3( s * 0.2f, s * 0.03f, s * 0.16f ), scale, new Basis( Vector3.Right, 0.3f ), "Scale" );
		} );
		return r;
	} );
}
