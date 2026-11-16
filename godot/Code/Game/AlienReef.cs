using Godot;

/// <summary>
/// Life for the water level's sea floor (issue #35). The starting basin keeps its layout but gets a coral reef: a dense
/// ring of coral round its edge (the reef round the Great Blue Hole of Belize) and patches inside it. The game's theme
/// runs through it: some of the kelp and coral is recoloured alien (neon green, black, deep purple, violet), and
/// glowing alien plants float everywhere, bobbing and drifting: lantern bulbs trailing black fronds, clusters of spore
/// orbs, and spiky star ferns. None of it is solid. Metres.
/// </summary>
public static class AlienReef
{
	public const float ReefInner = 23f;
	public const float ReefOuter = 33f;

	// The clutter pass: no neon green on the scenery (that is the enemies' colour, their eyes and weak points). Dark,
	// muted alien tones; only the violet and teal glow, and softly.
	static readonly Color[] AlienColours =
	{
		new( 0.04f, 0.04f, 0.06f ),  // black
		new( 0.26f, 0.07f, 0.4f ),   // deep purple
		new( 0.42f, 0.16f, 0.66f ),  // dim violet
		new( 0.07f, 0.3f, 0.33f ),   // dark teal
		new( 0.08f, 0.14f, 0.1f ),   // black-green
	};

	public static void Add( Node3D level, List<(Vector3 Position, float Radius)> taken )
	{
		var random = new Random( 4242 );
		float Range( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var all = level.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>().ToList();
		var dressing = all.Where( n => n.GetParent() is { } par && Level.Kind( par ) == "Dressing" ).ToList();
		var corals = dressing.Where( n => Level.Kind( n ).StartsWith( "Coral" ) ).ToList();
		var kelps = dressing.Where( n => Level.Kind( n ).StartsWith( "Kelp" ) ).ToList();
		var pebbles = dressing.Where( n => Level.Kind( n ).StartsWith( "Pebbles" ) ).ToList();

		Node3D Copy( Node3D source, Vector3 at, float scale )
		{
			var copy = (Node3D)source.Duplicate();
			source.GetParent().AddChild( copy );
			var lift = source.GlobalPosition.Y - SeaTerrain.HeightAt( source.GlobalPosition );
			copy.GlobalTransform = new Transform3D( new Basis( Vector3.Up, Range( 0f, MathF.Tau ) ) * source.GlobalBasis.Orthonormalized() * Basis.FromScale( source.GlobalBasis.Scale * scale ), at with { Y = SeaTerrain.HeightAt( at ) + lift * scale } );
			return copy;
		}
		bool Clear( Vector3 at, float pad ) => taken.All( t => t.Position.Flat().DistanceTo( at.Flat() ) > t.Radius + pad );

		// The reef: coral packed round the basin's rim, in clumps, with kelp and pebbles among it and a few gaps.
		var reef = new List<Node3D>();
		if ( corals.Count > 0 )
		{
			for ( var clump = 0; clump < 20; clump++ )
			{
				var angle = Range( 0f, MathF.Tau );
				var radius = Range( ReefInner + 1f, ReefOuter );
				var centre = new Vector3( MathF.Cos( angle ) * radius, 0f, MathF.Sin( angle ) * radius );
				var n = random.Next( 2, 5 );
				for ( var k = 0; k < n; k++ )
				{
					var at = centre + new Vector3( Range( -4f, 4f ), 0f, Range( -4f, 4f ) );
					if ( !Clear( at, 1.2f ) ) continue;
					taken.Add( (at, 1.6f) );   // room to walk between them
					reef.Add( Copy( corals[random.Next( corals.Count )], at, Range( 0.9f, 1.7f ) ) );
				}
				if ( kelps.Count > 0 && random.NextDouble() < 0.3 ) reef.Add( Copy( kelps[random.Next( kelps.Count )], centre + new Vector3( Range( -1.5f, 1.5f ), 0f, Range( -1.5f, 1.5f ) ), Range( 0.9f, 1.5f ) ) );
				if ( pebbles.Count > 0 && random.NextDouble() < 0.5 ) Copy( pebbles[random.Next( pebbles.Count )], centre, Range( 0.8f, 1.4f ) );
			}
			// A few patches inside the basin, kept off the stations, the dive spot and the barricades (fewer than before:
			// the middle was crowded).
			for ( var patch = 0; patch < 3; patch++ )
			{
				var at0 = new Vector3( Range( -ReefInner, ReefInner ), 0f, Range( -ReefInner, ReefInner ) );
				if ( at0.Length() < 6f || at0.Length() > ReefInner || !Clear( at0, 2f ) ) continue;
				for ( var k = random.Next( 2, 5 ); k > 0; k-- )
				{
					var at = at0 + new Vector3( Range( -1.2f, 1.2f ), 0f, Range( -1.2f, 1.2f ) );
					if ( Clear( at, 0.8f ) ) reef.Add( Copy( corals[random.Next( corals.Count )], at, Range( 0.7f, 1.4f ) ) );
				}
				taken.Add( (at0, 2.2f) );
			}
		}

		// The alien theme: recolour a share of all the kelp and coral (the old and the new).
		var plants = level.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>()
			.Where( n => n.GetParent() is { } par && Level.Kind( par ) == "Dressing" && (Level.Kind( n ).StartsWith( "Kelp" ) || Level.Kind( n ).StartsWith( "Coral" )) ).ToList();
		var recoloured = 0;
		foreach ( var plant in plants )
		{
			if ( random.NextDouble() > 0.45 ) continue;
			var colour = AlienColours[random.Next( AlienColours.Length )];
			foreach ( var mesh in plant.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
				mesh.MaterialOverride = Alien( mesh.MaterialOverride as StandardMaterial3D, colour );
			recoloured++;
		}

		// Glow bulbs (the clutter pass): small schools drifting round the reef and over the hills, open water between
		// them and none crowding the basin's middle where the fighting is; spiky star ferns low on the sand.
		var floaters = new AlienFloaters { Name = "Alien Floaters" };
		level.AddChild( floaters );
		for ( var k = 0; k < 16; k++ )
		{
			var a = Range( 0f, MathF.Tau );
			var r = k < 10 ? Range( ReefInner, ReefOuter + 6f ) : Range( ReefOuter + 12f, SeaTerrain.HalfSize - 20f );
			var centre = new Vector3( MathF.Cos( a ) * r, 0f, MathF.Sin( a ) * r );
			centre.Y = SeaTerrain.HeightAt( centre ) + Range( 2.6f, 5.5f );
			floaters.SpawnSchool( random, centre, random.Next( 3, 7 ) );
		}
		for ( var k = 0; k < 28; k++ )
		{
			var a = Range( 0f, MathF.Tau );
			var r = k < 14 ? Range( ReefInner - 2f, ReefOuter + 2f ) : Range( ReefOuter + 6f, SeaTerrain.HalfSize - 16f );
			var at = new Vector3( MathF.Cos( a ) * r, 0f, MathF.Sin( a ) * r );
			floaters.SpawnFern( random, at, SeaTerrain.HeightAt( at ) );
		}
		// Plants right in front of the camera dither away, so a kelp stalk never fills the view.
		foreach ( var plant in level.FindChildren( "*", "Node3D", true, false ).OfType<Node3D>()
			.Where( n => n.GetParent() is { } par && Level.Kind( par ) == "Dressing" && (Level.Kind( n ).StartsWith( "Kelp" ) || Level.Kind( n ).StartsWith( "Coral" )) ) )
			foreach ( var mesh in plant.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
				FadeNear( mesh );
		GD.Print( $"[reef] {reef.Count} reef pieces, {recoloured} plants recoloured alien, {floaters.Count} glow bulbs and ferns" );
	}

	/// <summary> Dithers a material away within about 2 m of the camera. </summary>
	public static void NearFade( BaseMaterial3D m )
	{
		m.DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelDither;
		m.DistanceFadeMinDistance = 0.7f;
		m.DistanceFadeMaxDistance = 2.2f;
	}

	static readonly Dictionary<Material, Material> faded = new();

	/// <summary> Gives each of a mesh's surfaces a near-fading copy of its material (shared between meshes). </summary>
	public static void FadeNear( MeshInstance3D mesh )
	{
		if ( mesh.MaterialOverride is BaseMaterial3D over )
		{
			if ( over.DistanceFadeMode == BaseMaterial3D.DistanceFadeModeEnum.Disabled ) mesh.MaterialOverride = Faded( over );
			return;
		}
		if ( mesh.Mesh is null ) return;
		for ( var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++ )
			if ( mesh.GetActiveMaterial( i ) is BaseMaterial3D m && m.DistanceFadeMode == BaseMaterial3D.DistanceFadeModeEnum.Disabled )
				mesh.SetSurfaceOverrideMaterial( i, Faded( m ) );
	}

	static Material Faded( BaseMaterial3D m )
	{
		if ( faded.TryGetValue( m, out var f ) ) return f;
		var copy = (BaseMaterial3D)m.Duplicate();
		NearFade( copy );
		faded[m] = copy;
		return copy;
	}

	static readonly Dictionary<(StandardMaterial3D, Color), StandardMaterial3D> recolours = new();

	/// <summary> A plant material in an alien colour: the same surface, the new colour, and the bright ones glow. </summary>
	static StandardMaterial3D Alien( StandardMaterial3D source, Color colour )
	{
		source ??= new StandardMaterial3D { Roughness = 0.6f };
		if ( recolours.TryGetValue( (source, colour), out var m ) ) return m;
		m = (StandardMaterial3D)source.Duplicate();
		m.AlbedoColor = colour;
		m.NextPass = null;
		if ( colour.Luminance > 0.2f )
		{
			m.EmissionEnabled = true;
			m.Emission = colour;
			m.EmissionEnergyMultiplier = 0.35f;
		}
		NearFade( m );
		recolours[(source, colour)] = m;
		return m;
	}
}

