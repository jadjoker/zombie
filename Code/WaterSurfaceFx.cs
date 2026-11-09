/// <summary>
/// Life on the sea's surface around the dock: drifting swell crests that rise and sink, foam rings breathing round
/// the stilts, and the splash when something goes in (a crown of droplets, a column of spray and a spreading foam ring).
/// </summary>
public sealed class WaterSurfaceFx : Component
{
	public float Height { get; set; }
	public Vector3 Center { get; set; }
	public List<Vector3> Stilts { get; set; } = new();

	const int CrestCount = 160;
	const float Area = 2200f;
	const int GlintCount = 70;
	readonly List<(GameObject Go, Vector3 Pos, float Phase)> glints = new();
	readonly List<(GameObject Go, Vector3 Pos, float Phase, float Length)> crests = new();
	readonly List<(GameObject Go, float Phase)> foams = new();

	static GameObject Blob( GameObject parent, string name, Color tint, bool sphere = false )
	{
		var go = new GameObject( parent, true, name );
		go.NetworkMode = NetworkMode.Never;
		var r = go.AddComponent<ModelRenderer>();
		r.Model = sphere ? Shapes.Sphere : Shapes.Box;
		r.Tint = tint;
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
		return go;
	}

	protected override void OnStart()
	{
		for ( var i = 0; i < 0; i++ )
		{
			var pos = Center + new Vector3( Game.Random.Float( -Area, Area ), Game.Random.Float( -Area, Area ), 0f );
			var tint = Color.Lerp( new Color( 0.2f, 0.55f, 0.65f ), new Color( 0.95f, 0.99f, 1f ), Game.Random.Float() );
			crests.Add( (Blob( GameObject, "Swell", tint ), pos, Game.Random.Float( 0f, 6.28f ), Game.Random.Float( 160f, 520f )) );
		}
		for ( var i = 0; i < GlintCount; i++ )
		{
			var pos = Center + new Vector3( Game.Random.Float( -1400f, 1400f ), Game.Random.Float( -1400f, 1400f ), 0f );
			glints.Add( (Blob( GameObject, "Glint", new Color( 1f, 0.98f, 0.9f ) * 2.5f, true ), pos, Game.Random.Float( 0f, 6.28f )) );
		}
		foreach ( var stilt in Stilts )
			foams.Add( (Blob( GameObject, "Stilt Foam", new Color( 0.9f, 0.97f, 1f ), true ), Game.Random.Float( 0f, 6.28f )) );
	}

	protected override void OnUpdate()
	{
		var t = Time.Now;
		var drift = new Vector3( 38f, 14f, 0f ) * Time.Delta;
		for ( var i = 0; i < crests.Count; i++ )
		{
			var (go, pos, phase, length) = crests[i];
			pos += drift;
			// Wrap round the area so the sea never runs out of swell.
			if ( pos.x > Center.x + Area ) pos.x -= Area * 2f;
			if ( pos.y > Center.y + Area ) pos.y -= Area * 2f;
			crests[i] = (go, pos, phase, length);
			// Each crest swells up out of the water, rides, then sinks back.
			var life = 0.5f + 0.5f * MathF.Sin( t * 0.55f + phase );
			go.WorldPosition = pos.WithZ( Height + 0.5f + life * 2.5f );
			go.WorldRotation = Rotation.FromYaw( 20f );
			go.WorldScale = Shapes.BoxScale( new Vector3( 26f + 26f * life, length * (0.5f + 0.5f * life), 2f + 5f * life ) );
		}
		// Sun glints: tiny bright sparks that flash on and off across the water.
		for ( var i = 0; i < glints.Count; i++ )
		{
			var (go, pos, phase) = glints[i];
			var flash = MathF.Max( 0f, MathF.Sin( t * 2.3f + phase * 3f ) );
			flash = flash * flash * flash * flash;
			go.WorldPosition = pos.WithZ( Height + 2f );
			go.WorldScale = Vector3.One * Shapes.SphereScale( 0.3f + 3.2f * flash );
		}
		for ( var i = 0; i < foams.Count && i < Stilts.Count; i++ )
		{
			var (go, phase) = foams[i];
			var s = 14f + 4f * MathF.Sin( t * 1.6f + phase );
			go.WorldPosition = Stilts[i].WithZ( Height + 1.2f );
			go.WorldScale = new Vector3( s, s, 0.6f ) * Shapes.SphereScale( 1f );
		}
	}

	/// <summary> The splash: something hit the water here (speed sets how big). </summary>
	public static void Splash( Vector3 at, float speed )
	{
		var power = MathX.Clamp( speed / 900f, 0.4f, 1.6f );
		var root = new GameObject( true, "Splash" );
		root.NetworkMode = NetworkMode.Never;
		root.WorldPosition = at;
		root.AddComponent<SelfDestruct>().Lifetime = 2.6f;
		var splash = root.AddComponent<SplashFx>();
		splash.Power = power;
	}
}

/// <summary> One splash: droplets thrown up in a crown, a tall column of spray, and a foam ring spreading out and fading. </summary>
public sealed class SplashFx : Component
{
	public float Power { get; set; } = 1f;
	readonly List<(GameObject Go, Vector3 Vel, float Size)> drops = new();
	GameObject ring, ring2, column;
	TimeSince age;

	protected override void OnStart()
	{
		age = 0f;
		var white = new Color( 0.92f, 0.98f, 1f );
		var count = (int)(36 * Power);
		for ( var i = 0; i < count; i++ )
		{
			var a = Game.Random.Float( 0f, MathF.PI * 2f );
			var outward = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f );
			// Two families: a crown thrown out low, and a jet thrown high near the middle.
			var high = i % 3 == 0;
			var vel = outward * Game.Random.Float( high ? 40f : 160f, high ? 120f : 340f ) * Power + Vector3.Up * Game.Random.Float( high ? 520f : 220f, high ? 820f : 420f ) * Power;
			var go = new GameObject( GameObject, true, "Drop" );
			go.NetworkMode = NetworkMode.Never;
			go.WorldPosition = WorldPosition + outward * Game.Random.Float( 4f, 22f );
			var r = go.AddComponent<ModelRenderer>();
			r.Model = Shapes.Sphere;
			r.Tint = Color.Lerp( white, new Color( 0.6f, 0.88f, 0.95f ), Game.Random.Float() );
			r.RenderType = ModelRenderer.ShadowRenderType.Off;
			drops.Add( (go, vel, Game.Random.Float( 2.5f, 6f ) * Power) );
		}
		ring = Make( "Foam Ring", white );
		ring2 = Make( "Foam Ring Outer", new Color( 0.75f, 0.92f, 0.98f ) );
		column = Make( "Spray Column", white );
	}

	GameObject Make( string name, Color tint )
	{
		var go = new GameObject( GameObject, true, name );
		go.NetworkMode = NetworkMode.Never;
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Shapes.Sphere;
		r.Tint = tint;
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
		return go;
	}

	protected override void OnUpdate()
	{
		var dt = Time.Delta;
		var t = (float)age;
		for ( var i = 0; i < drops.Count; i++ )
		{
			var (go, vel, size) = drops[i];
			if ( !go.IsValid() ) continue;
			vel += Vector3.Down * 900f * dt;
			vel *= MathF.Exp( -0.6f * dt );
			go.WorldPosition += vel * dt;
			drops[i] = (go, vel, size);
			// Shrink away as they fall back in.
			var fade = MathX.Clamp( 1f - t / 1.6f, 0f, 1f );
			var below = go.WorldPosition.z < WorldPosition.z;
			go.WorldScale = Vector3.One * Shapes.SphereScale( below ? 0f : size * fade );
		}

		// Foam rings racing out and thinning.
		var spread = 1f - MathF.Exp( -2.2f * t );
		var fadeRing = MathX.Clamp( 1f - t / 2.4f, 0f, 1f );
		ring.WorldPosition = WorldPosition + Vector3.Up * 1.5f;
		ring.WorldScale = new Vector3( 30f + 160f * spread * Power, 30f + 160f * spread * Power, 1.2f * fadeRing ) * Shapes.SphereScale( 1f );
		ring2.WorldPosition = WorldPosition + Vector3.Up * 1f;
		var spread2 = 1f - MathF.Exp( -1.2f * t );
		ring2.WorldScale = new Vector3( 50f + 300f * spread2 * Power, 50f + 300f * spread2 * Power, 0.8f * fadeRing ) * Shapes.SphereScale( 1f );

		// The spray column shoots up and collapses.
		var rise = MathF.Sin( MathX.Clamp( t / 0.9f, 0f, 1f ) * MathF.PI );
		column.WorldPosition = WorldPosition + Vector3.Up * (60f * rise * Power);
		column.WorldScale = new Vector3( 16f * Power * (1f - 0.4f * rise), 16f * Power * (1f - 0.4f * rise), 70f * rise * Power + 0.01f ) * Shapes.SphereScale( 1f );
	}
}
