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

	static readonly Color[] AlienColours =
	{
		new( 0.35f, 1f, 0.28f ),     // neon green
		new( 0.04f, 0.04f, 0.06f ),  // black
		new( 0.36f, 0.08f, 0.55f ),  // deep purple
		new( 0.62f, 0.2f, 0.95f ),   // violet
		new( 0.1f, 0.22f, 0.08f ),   // black-green
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
			for ( var clump = 0; clump < 46; clump++ )
			{
				var angle = Range( 0f, MathF.Tau );
				var radius = Range( ReefInner + 1f, ReefOuter );
				var centre = new Vector3( MathF.Cos( angle ) * radius, 0f, MathF.Sin( angle ) * radius );
				var n = random.Next( 3, 7 );
				for ( var k = 0; k < n; k++ )
				{
					var at = centre + new Vector3( Range( -2.2f, 2.2f ), 0f, Range( -2.2f, 2.2f ) );
					if ( !Clear( at, 0.6f ) ) continue;
					reef.Add( Copy( corals[random.Next( corals.Count )], at, Range( 0.9f, 2.1f ) ) );
				}
				if ( kelps.Count > 0 && random.NextDouble() < 0.55 ) reef.Add( Copy( kelps[random.Next( kelps.Count )], centre + new Vector3( Range( -1.5f, 1.5f ), 0f, Range( -1.5f, 1.5f ) ), Range( 0.9f, 1.5f ) ) );
				if ( pebbles.Count > 0 && random.NextDouble() < 0.5 ) Copy( pebbles[random.Next( pebbles.Count )], centre, Range( 0.8f, 1.4f ) );
			}
			// Patches inside the basin, kept off the stations, the dive spot and the barricades.
			for ( var patch = 0; patch < 16; patch++ )
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

		// Floating alien plants: thick round the reef and in the basin, scattered over the hills.
		var floaters = new AlienFloaters { Name = "Alien Floaters" };
		level.AddChild( floaters );
		for ( var k = 0; k < 230; k++ )
		{
			Vector3 at;
			var roll = random.NextDouble();
			if ( roll < 0.3 )
			{
				var a = Range( 0f, MathF.Tau );
				var r = Range( ReefInner - 2f, ReefOuter + 3f );
				at = new Vector3( MathF.Cos( a ) * r, 0f, MathF.Sin( a ) * r );
			}
			else if ( roll < 0.45 )
			{
				var a = Range( 0f, MathF.Tau );
				at = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * Range( 7f, ReefInner );
			}
			else at = new Vector3( Range( -SeaTerrain.HalfSize + 14f, SeaTerrain.HalfSize - 14f ), 0f, Range( -SeaTerrain.HalfSize + 14f, SeaTerrain.HalfSize - 14f ) );
			at.Y = SeaTerrain.HeightAt( at ) + Range( 1.6f, 6.5f );
			floaters.Spawn( random, at );
		}
		GD.Print( $"[reef] {reef.Count} reef pieces, {recoloured} plants recoloured alien, {floaters.Count} floating plants" );
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
		var bright = colour.Luminance > 0.3f;
		if ( bright )
		{
			m.EmissionEnabled = true;
			m.Emission = colour;
			m.EmissionEnergyMultiplier = colour.G > 0.8f ? 0.9f : 0.6f;
		}
		recolours[(source, colour)] = m;
		return m;
	}
}

/// <summary> Glowing alien plants that float in the water, bobbing, drifting in slow loops and turning. </summary>
public partial class AlienFloaters : Node3D
{
	sealed class Floater
	{
		public Node3D Node;
		public Vector3 Home;
		public float Phase, Bob, Drift, Rate, Spin, Lift;
	}

	readonly List<Floater> all = new();
	public int Count => all.Count;

	static StandardMaterial3D Glow( Color c, float energy ) => new() { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy, Roughness = 0.3f, RimEnabled = true, Rim = 0.5f };
	static StandardMaterial3D Frond( Color c ) => new() { AlbedoColor = c, Roughness = 0.45f, RimEnabled = true, Rim = 0.6f, RimTint = 0.8f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };

	static readonly StandardMaterial3D[] Bulbs = { Glow( new Color( 0.35f, 1f, 0.25f ), 2.4f ), Glow( new Color( 0.7f, 0.25f, 1f ), 2.2f ), Glow( new Color( 0.2f, 1f, 0.7f ), 2f ) };
	static readonly StandardMaterial3D[] Fronds = { Frond( new Color( 0.03f, 0.03f, 0.05f ) ), Frond( new Color( 0.22f, 0.05f, 0.34f ) ), Frond( new Color( 0.05f, 0.14f, 0.05f ) ) };

	public void Spawn( Random random, Vector3 at )
	{
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var node = new Node3D { Name = "Floater" };
		AddChild( node );
		node.GlobalPosition = at;
		var bulb = Bulbs[random.Next( Bulbs.Length )];
		var frond = Fronds[random.Next( Fronds.Length )];
		MeshInstance3D Part( Mesh mesh, Material material, Vector3 position, Basis basis )
		{
			var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			node.AddChild( m );
			m.Transform = new Transform3D( basis, position );
			return m;
		}

		switch ( random.Next( 3 ) )
		{
			case 0:
			{
				// Lantern: a glowing bulb with a dark cap, trailing black or purple fronds.
				var r = R( 0.18f, 0.42f );
				Part( new SphereMesh { Radius = r, Height = r * 2.2f, RadialSegments = 14, Rings = 8 }, bulb, Vector3.Zero, Basis.Identity );
				Part( new SphereMesh { Radius = r * 1.15f, Height = r * 1.1f, RadialSegments = 14, Rings = 6, IsHemisphere = true }, frond, Vector3.Up * r * 0.35f, Basis.Identity );
				for ( var i = random.Next( 3, 6 ); i > 0; i-- )
				{
					var len = R( 0.8f, 2.2f ) * (r / 0.3f);
					var a = R( 0f, MathF.Tau );
					var tilt = new Basis( new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ), R( 0.05f, 0.35f ) );
					Part( new CylinderMesh { TopRadius = r * 0.12f, BottomRadius = 0.01f, Height = len, RadialSegments = 5, Rings = 1 }, frond, tilt * (Vector3.Down * (r * 0.6f + len * 0.5f)), tilt );
				}
				break;
			}
			case 1:
			{
				// Spore cluster: a dark core studded with glowing orbs.
				var r = R( 0.15f, 0.3f );
				Part( new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 10, Rings = 6 }, frond, Vector3.Zero, Basis.Identity );
				for ( var i = random.Next( 5, 10 ); i > 0; i-- )
				{
					var d = new Vector3( R( -1f, 1f ), R( -1f, 1f ), R( -1f, 1f ) ).Normalized();
					var orb = R( 0.06f, 0.14f );
					Part( new SphereMesh { Radius = orb, Height = orb * 2f, RadialSegments = 8, Rings = 4 }, bulb, d * (r + orb * 0.6f), Basis.Identity );
				}
				break;
			}
			default:
			{
				// Star fern: spiky black and purple blades with glowing tips, splayed from a centre.
				for ( var i = random.Next( 5, 9 ); i > 0; i-- )
				{
					var dir = new Vector3( R( -1f, 1f ), R( -0.4f, 1f ), R( -1f, 1f ) ).Normalized();
					var len = R( 0.5f, 1.3f );
					var basis = new Basis( new Quaternion( Vector3.Up, dir ) );
					Part( new BoxMesh { Size = new Vector3( 0.16f, len, 0.02f ) }, frond, dir * len * 0.5f, basis );
					Part( new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 6, Rings = 3 }, bulb, dir * len, Basis.Identity );
				}
				break;
			}
		}
		all.Add( new Floater { Node = node, Home = at, Lift = at.Y - (SeaTerrain.Active ? SeaTerrain.HeightAt( at ) : 0f), Phase = R( 0f, MathF.Tau ), Bob = R( 0.25f, 0.7f ), Drift = R( 0.4f, 1.6f ), Rate = R( 0.12f, 0.3f ), Spin = R( -0.25f, 0.25f ) } );
	}

	/// <summary> After the ground changes (the wreck site): drops the ones where `remove` says, sets the rest at the
	/// same height above the new ground. </summary>
	public void Resettle( Func<Vector3, bool> remove )
	{
		foreach ( var f in all.ToList() )
		{
			if ( remove( f.Home ) ) { f.Node.QueueFree(); all.Remove( f ); continue; }
			f.Home = f.Home with { Y = SeaTerrain.HeightAt( f.Home ) + f.Lift };
		}
	}

	public override void _Process( double delta )
	{
		var t = (float)Clock.Now;
		foreach ( var f in all )
		{
			var a = t * f.Rate + f.Phase;
			var offset = new Vector3( MathF.Cos( a ) * f.Drift, MathF.Sin( a * 2.3f ) * f.Bob, MathF.Sin( a * 0.8f ) * f.Drift );
			var sway = new Basis( Vector3.Right, MathF.Sin( a * 1.7f ) * 0.18f ) * new Basis( Vector3.Forward, MathF.Cos( a * 1.3f ) * 0.15f );
			f.Node.GlobalTransform = new Transform3D( new Basis( Vector3.Up, t * f.Spin + f.Phase ) * sway, f.Home + offset );
		}
	}
}
