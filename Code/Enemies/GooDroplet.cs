/// <summary>
/// One flying drop of creature goo: arcs under gravity, shrinks away, and sometimes leaves a splat where
/// it lands. No physics body, so a burst of them is cheap.
/// </summary>
public sealed class GooDroplet : Component
{
	Vector3 velocity;
	Color color;
	float size;
	float lifetime;
	TimeSince age;

	/// <summary> Most droplets in flight at once; past this, new ones are skipped (the splats still happen). </summary>
	public const int MaxLive = 500;
	static int live;
	static int spawnsSinceRecount;

	public static void Spawn( Scene scene, Vector3 position, Vector3 velocity, Color color, float size, float lifeScale = 1f )
	{
		// Every so often, recount the real number so the cap can never get stuck.
		if ( ++spawnsSinceRecount > 60 )
		{
			spawnsSinceRecount = 0;
			live = scene.GetAllComponents<GooDroplet>().Count();
		}
		if ( live >= MaxLive ) return;
		var go = new GameObject( true, "Goo" );
		go.NetworkMode = NetworkMode.Never;
		go.Tags.Add( "gore" );
		go.WorldPosition = position;
		go.WorldScale = Shapes.SphereScale( size );
		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = Shapes.Sphere;
		renderer.Tint = color;
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;

		var drop = go.AddComponent<GooDroplet>();
		drop.velocity = velocity;
		drop.color = color;
		drop.size = size;
		drop.lifetime = Game.Random.Float( 0.45f, 0.8f ) * lifeScale;
		drop.age = 0f;
		live++;
	}

	protected override void OnDestroy() => live = Math.Max( 0, live - 1 );

	protected override void OnUpdate()
	{
		var dt = Time.Delta;
		velocity += Vector3.Down * 700f * dt;
		var next = WorldPosition + velocity * dt;

		// Landed on something: leave a little splat (now and then, to keep decal counts down) and go.
		var gore = GoreSystem.Get( Scene );
		if ( gore is not null && gore.TraceWorld( WorldPosition, next ) is { } hit )
		{
			if ( Game.Random.Float() < 0.3f ) gore.GooSplat( hit.Position, hit.Normal, Game.Random.Float( 0.2f, 0.45f ), color );
			GameObject.Destroy();
			return;
		}

		WorldPosition = next;
		var t = age / lifetime;
		if ( t >= 1f ) { GameObject.Destroy(); return; }
		// Stretched along its flight: fast drops read as streaks, slow ones as beads.
		var r = Shapes.SphereScale( size * (1f - t * t) );
		var speed = velocity.Length;
		if ( speed > 1f ) WorldRotation = Rotation.LookAt( velocity / speed );
		WorldScale = new Vector3( r * (1f + MathF.Min( speed / 160f, 3f )), r, r );
	}
}
