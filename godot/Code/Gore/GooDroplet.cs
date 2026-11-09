using Godot;

/// <summary>
/// Port of s&box GooDroplet: one drop of goo flying under gravity, stretched along its flight, leaving a splat
/// where it lands (now and then, to keep decal counts down). Capped at MaxLive.
/// </summary>
public partial class GooDroplet : MeshInstance3D
{
	Vector3 velocity;
	Color color;
	float size;
	float lifetime;
	TimeSince age;

	public const int MaxLive = 500;
	static int live;

	/// <summary> Velocity in m/s; size is the s&box drop radius in inches. </summary>
	public static void Spawn( Vector3 position, Vector3 velocity, Color color, float size, float lifeScale = 1f )
	{
		if ( live >= MaxLive ) return;
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( root is null ) return;
		var drop = new GooDroplet
		{
			Mesh = Shapes.Sphere,
			MaterialOverride = Materials( color ),
			CastShadow = ShadowCastingSetting.Off,
			velocity = velocity,
			color = color,
			size = size,
			lifetime = Rng.Float( 0.45f, 0.8f ) * lifeScale,
		};
		root.AddChild( drop );
		// Sized now: the unit sphere drew as a 1 m ball for its first frame (playtest 1's "giant green spheres").
		var r = size * 2f * U.In;
		drop.GlobalTransform = new Transform3D( Basis.FromScale( Vector3.One * r ), position );
		drop.age = 0f;
		live++;
	}

	static readonly Dictionary<Color, StandardMaterial3D> materials = new();
	static StandardMaterial3D Materials( Color c )
	{
		if ( !materials.TryGetValue( c, out var m ) )
			materials[c] = m = new StandardMaterial3D { AlbedoColor = c, Roughness = 0.2f, Metallic = 0.1f };
		return m;
	}

	public override void _ExitTree() => live = Math.Max( 0, live - 1 );

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		velocity += Vector3.Down * 700f * U.In * dt;
		var next = GlobalPosition + velocity * dt;
		var gore = GoreSystem.Get();
		if ( gore.TraceWorld( GlobalPosition, next ) is { } hit )
		{
			if ( Rng.Float() < 0.3f ) gore.GooSplat( hit.Position, hit.Normal, Rng.Float( 0.2f, 0.45f ), color );
			QueueFree();
			return;
		}
		GlobalPosition = next;
		var t = age / lifetime;
		if ( t >= 1f ) { QueueFree(); return; }
		var r = size * (1f - t * t) * 2f * U.In;
		var speed = velocity.Length() / U.In;
		var stretch = 1f + MathF.Min( speed / 160f, 3f );
		var basis = speed > 1f ? Shapes.LookAt( velocity ) : Basis.Identity;
		GlobalBasis = basis.Scaled( new Vector3( r, r, r * stretch ) );
	}
}
