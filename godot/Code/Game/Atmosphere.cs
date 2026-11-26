using Godot;

/// <summary>
/// The water level's backdrop (the clutter pass): the mood lives far away or high up, never between the player and the
/// enemies. Shafts of light slant down from the surface out past the basin, and a few huge dim leviathans swim slow
/// circles beyond the cliff rim, seen only as shapes in the murk. Built once with the level; nothing here is solid.
/// </summary>
public partial class Atmosphere : Node3D
{
	public const int ShaftCount = 12, LeviathanCount = 3;
	public const float ShaftInner = 34f, ShaftOuter = 95f;

	sealed class Leviathan
	{
		public Node3D Node;
		public Node3D Tail;
		public float Radius, Height, Speed, Phase, Bob;
	}

	readonly List<Leviathan> leviathans = new();
	readonly List<(Node3D Node, float Phase)> shafts = new();
	Basis[] shaftRest = System.Array.Empty<Basis>();

	public static Atmosphere Add( Node3D level )
	{
		var a = new Atmosphere { Name = "Backdrop" };
		level.AddChild( a );
		a.Build();
		return a;
	}

	void Build()
	{
		var random = new Random( 777 );
		float R( float a, float b ) => a + (float)random.NextDouble() * (b - a);
		var shader = GD.Load<Shader>( "res://assets/shaders/fx_light_shaft.gdshader" );

		// Light shafts: out past the basin, slanted the same way (one sun), in a few widths.
		var tilt = new Basis( new Vector3( 1f, 0f, 0.4f ).Normalized(), MathX.Rad( 14f ) );
		var rests = new List<Basis>();
		for ( var i = 0; i < ShaftCount; i++ )
		{
			var angle = i / (float)ShaftCount * MathF.Tau + R( -0.2f, 0.2f );
			var radius = R( ShaftInner, ShaftOuter );
			var foot = new Vector3( MathF.Cos( angle ) * radius, 0f, MathF.Sin( angle ) * radius );
			var ground = SeaTerrain.Active ? SeaTerrain.HeightAt( foot ) : 0f;
			var height = R( 48f, 62f );   // the surface is far above: each fades out high in the water
			var width = R( 2.5f, 5.5f );
			var material = new ShaderMaterial { Shader = shader };
			material.SetShaderParameter( "seed", R( 0f, 1f ) );
			material.SetShaderParameter( "strength", R( 0.2f, 0.32f ) );
			material.SetMeta( "no_caustics", true );
			var mesh = new MeshInstance3D
			{
				Name = "Light Shaft",
				Mesh = new CylinderMesh { TopRadius = width * 0.7f, BottomRadius = width * 1.6f, Height = height, RadialSegments = 16, Rings = 1, CapTop = false, CapBottom = false },
				MaterialOverride = material,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			AddChild( mesh );
			var basis = tilt * new Basis( Vector3.Up, R( 0f, MathF.Tau ) );
			mesh.GlobalTransform = new Transform3D( basis, foot with { Y = ground + height * 0.5f } + tilt * Vector3.Up * 0f );
			rests.Add( basis );
			shafts.Add( (mesh, R( 0f, MathF.Tau )) );
		}
		shaftRest = rests.ToArray();

		// Leviathans: long dark whale shapes circling high over the cliff rim, far out in the murk.
		for ( var i = 0; i < LeviathanCount; i++ )
		{
			var body = BuildLeviathan( R( 26f, 40f ), out var tail );
			AddChild( body );
			leviathans.Add( new Leviathan
			{
				Node = body, Tail = tail, Radius = R( 100f, 125f ), Height = R( 70f, 95f ), Speed = R( 0.012f, 0.02f ) * (i % 2 == 0 ? 1f : -1f),
				Phase = i / (float)LeviathanCount * MathF.Tau + R( -0.4f, 0.4f ), Bob = R( 0f, MathF.Tau ),
			} );
		}
		GD.Print( $"[atmosphere] {shafts.Count} light shafts, {leviathans.Count} leviathans past the rim" );
	}

	static ShaderMaterial silhouette;

	/// <summary> A leviathan's dark shape (body along -Z, a blunt head, a long taper, fins), its tail flukes on their own pivot. </summary>
	public static Node3D BuildLeviathan( float length, out Node3D tail )
	{
		if ( silhouette is null )
		{
			silhouette = new ShaderMaterial { Shader = GD.Load<Shader>( "res://assets/shaders/fx_silhouette.gdshader" ) };
			silhouette.SetMeta( "no_caustics", true );
		}
		var look = silhouette;
		var body = new Node3D { Name = "Leviathan" };
		MeshInstance3D Part( Mesh mesh, Node3D parent, Vector3 at, Basis basis )
		{
			var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = look, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			parent.AddChild( m );
			m.Transform = new Transform3D( basis, at );
			return m;
		}
		var lying = new Basis( Vector3.Right, MathF.PI * 0.5f );
		Part( new CapsuleMesh { Radius = length * 0.13f, Height = length * 0.62f, RadialSegments = 16, Rings = 6 }, body, Vector3.Zero, lying * Basis.FromScale( new Vector3( 1f, 1f, 0.8f ) ) );
		Part( new CylinderMesh { TopRadius = length * 0.11f, BottomRadius = length * 0.025f, Height = length * 0.42f, RadialSegments = 12, Rings = 1 }, body, new Vector3( 0f, 0f, length * 0.47f ), lying );
		// Fins and flukes are squashed spheres: soft, organic edges (boxes read as a submarine).
		var fin = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 12, Rings = 6 };
		foreach ( var side in new[] { -1f, 1f } )
			Part( fin, body, new Vector3( side * length * 0.17f, -length * 0.05f, -length * 0.06f ),
				new Basis( Vector3.Forward, side * 0.35f ) * new Basis( Vector3.Up, side * -0.5f ) * Basis.FromScale( new Vector3( length * 0.26f, length * 0.02f, length * 0.08f ) ) );
		tail = new Node3D { Name = "Tail", Position = new Vector3( 0f, 0f, length * 0.68f ) };
		body.AddChild( tail );
		foreach ( var side in new[] { -1f, 1f } )
			Part( fin, tail, new Vector3( side * length * 0.09f, 0f, length * 0.03f ),
				new Basis( Vector3.Up, side * 0.55f ) * Basis.FromScale( new Vector3( length * 0.2f, length * 0.015f, length * 0.07f ) ) );
		return body;
	}

	public override void _Process( double delta )
	{
		var t = (float)Clock.Now;
		for ( var i = 0; i < shafts.Count; i++ )
		{
			// A slow sway, as if the surface above were moving.
			var (node, phase) = shafts[i];
			var sway = new Basis( Vector3.Forward, MathF.Sin( t * 0.11f + phase ) * 0.03f ) * new Basis( Vector3.Right, MathF.Cos( t * 0.09f + phase ) * 0.03f );
			node.Basis = sway * shaftRest[i];
		}
		foreach ( var l in leviathans )
		{
			var a = t * l.Speed + l.Phase;
			// Later in a stage they circle closer and lower (AreaLife.Tension).
			var close = AreaLife.Tension;
			var radius = l.Radius * (1f - 0.25f * close);
			var at = new Vector3( MathF.Cos( a ) * radius, l.Height * (1f - 0.3f * close) + MathF.Sin( t * 0.05f + l.Bob ) * 4f, MathF.Sin( a ) * radius );
			// Heading along the circle.
			var ahead = new Vector3( -MathF.Sin( a ), 0f, MathF.Cos( a ) ) * MathF.Sign( l.Speed );
			l.Node.GlobalTransform = new Transform3D( Shapes.LookAt( ahead ) * new Basis( Vector3.Forward, MathF.Sin( t * 0.2f + l.Bob ) * 0.08f ), at );
			l.Tail.Rotation = new Vector3( MathF.Sin( t * 0.6f + l.Bob ) * 0.35f, 0f, 0f );
		}
	}
}
