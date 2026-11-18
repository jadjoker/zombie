using Godot;

/// <summary>
/// Port of s&box GooDroplet: drops of goo flying under gravity, stretched along their flight, shrinking as they go,
/// leaving a splat where they land (now and then, to keep decal counts down). Capped at MaxLive.
/// All drops live in one system node and draw as one MultiMesh (one draw call, no node per drop); each still traces
/// its own path against the world every frame, so they land on props and slopes exactly as before.
/// </summary>
public partial class GooDroplet : Node3D
{
	public const int MaxLive = 500;
	/// <summary> How many drops are flying right now. </summary>
	public static int Live => instance is not null && IsInstanceValid( instance ) ? instance.count : 0;

	static GooDroplet instance;
	MultiMeshInstance3D view;
	readonly Vector3[] position = new Vector3[MaxLive], velocity = new Vector3[MaxLive];
	readonly Color[] colour = new Color[MaxLive];
	readonly float[] size = new float[MaxLive], lifetime = new float[MaxLive], age = new float[MaxLive];
	int count;
	PhysicsRayQueryParameters3D query;

	/// <summary> Velocity in m/s; size is the s&box drop radius in inches. </summary>
	public static void Spawn( Vector3 position, Vector3 velocity, Color color, float size, float lifeScale = 1f )
	{
		var system = Get();
		if ( system is null || system.count >= MaxLive ) return;
		var i = system.count++;
		system.position[i] = position;
		system.velocity[i] = velocity;
		system.colour[i] = color with { A = 1f };
		system.size[i] = size;
		system.lifetime[i] = Rng.Float( 0.45f, 0.8f ) * lifeScale;
		system.age[i] = 0f;
		// Placed now, sized: never drawn as a unit sphere for a frame (playtest 1's "giant green spheres").
		system.Draw( i );
		system.view.Multimesh.VisibleInstanceCount = system.count;
	}

	static GooDroplet Get()
	{
		if ( instance is not null && IsInstanceValid( instance ) && instance.IsInsideTree() ) return instance;
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( root is null ) return null;
		instance = new GooDroplet { Name = "Goo Droplets" };
		var material = new StandardMaterial3D { Roughness = 0.2f, Metallic = 0.1f, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true };
		var mesh = (Mesh)Shapes.Sphere.Duplicate();
		mesh.SurfaceSetMaterial( 0, material );
		instance.view = new MultiMeshInstance3D
		{
			Name = "Drops", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = MaxLive, VisibleInstanceCount = 0, Mesh = mesh },
			// The drops fly anywhere in the level: never culled as one box.
			CustomAabb = new Aabb( -Vector3.One * 5000f, Vector3.One * 10000f ),
		};
		instance.AddChild( instance.view );
		root.AddChild( instance );
		return instance;
	}

	public override void _ExitTree()
	{
		if ( instance == this ) instance = null;
	}

	/// <summary> A drop's place in the MultiMesh: stretched along its flight, shrinking toward the end of its life. </summary>
	void Draw( int i )
	{
		var t = age[i] / lifetime[i];
		var r = size[i] * (1f - t * t) * 2f * U.In;
		var speed = velocity[i].Length() / U.In;
		var stretch = 1f + MathF.Min( speed / 160f, 3f );
		var basis = speed > 1f ? Shapes.LookAt( velocity[i] ) : Basis.Identity;
		view.Multimesh.SetInstanceTransform( i, new Transform3D( basis.Scaled( new Vector3( r, r, r * stretch ) ), position[i] ) );
		view.Multimesh.SetInstanceColor( i, colour[i] );
	}

	/// <summary> Drop i is done: the last drop takes its slot. </summary>
	void Remove( int i )
	{
		var last = --count;
		if ( i == last ) return;
		position[i] = position[last];
		velocity[i] = velocity[last];
		colour[i] = colour[last];
		size[i] = size[last];
		lifetime[i] = lifetime[last];
		age[i] = age[last];
	}

	public override void _Process( double delta )
	{
		if ( count == 0 ) return;
		using var perf = Perf.Time( "GooDroplet" );
		var dt = (float)delta;
		var gore = GoreSystem.Get();
		var space = GetWorld3D().DirectSpaceState;
		query ??= new PhysicsRayQueryParameters3D { CollisionMask = Layers.World, HitFromInside = true };
		for ( var i = 0; i < count; i++ )
		{
			velocity[i] += Vector3.Down * 700f * U.In * dt;
			var next = position[i] + velocity[i] * dt;
			query.From = position[i];
			query.To = next;
			var hit = space.IntersectRay( query );
			if ( hit.Count > 0 )
			{
				if ( Rng.Float() < 0.3f ) gore.GooSplat( (Vector3)hit["position"], (Vector3)hit["normal"], Rng.Float( 0.2f, 0.45f ), colour[i] );
				Remove( i-- );
				continue;
			}
			position[i] = next;
			age[i] += dt;
			if ( age[i] >= lifetime[i] ) { Remove( i-- ); continue; }
			Draw( i );
		}
		view.Multimesh.VisibleInstanceCount = count;
	}
}
