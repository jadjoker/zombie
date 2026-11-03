/// <summary>
/// A loose piece of zombie. Either a set of body segments drawn from the zombie's own mesh
/// (every other bone shrunk away at the cut), or a plain meat/organ model.
/// Shooting a multi-segment gib splits it at the segment you hit; a single segment gets pulverised.
/// </summary>
public sealed class Gib : Component
{
	public bool IsFading { get; private set; }
	public IReadOnlySet<Segment> Members => members;
	public bool IsProp => map is null;

	GoreSystem gore;
	BodySegments map;
	Color tint;
	Transform[] poseLocal;
	Dictionary<Segment, (Vector3 From, Vector3 To)> capsLocal;
	HashSet<Segment> members = new();

	SkinnedModelRenderer renderer;
	Rigidbody body;
	float health;
	float lifetime;
	TimeSince age;
	TimeSince fadeStarted;
	Vector3 scaleBeforeFade;
	TimeUntil? pendingSplit;
	TimeSince lastDrip;
	float lastSpeed;
	int splats;

	// ---- Creation ---------------------------------------------------------

	/// <summary> Spawns a gib made of the given segments, posed from world-space data captured off a body. </summary>
	public static Gib CreateSkinned( Scene scene, GoreSystem gore, BodySegments map, Color tint,
		Transform[] meshWorld, Dictionary<Segment, (Vector3 From, Vector3 To)> capsWorld,
		HashSet<Segment> members, Vector3 velocity, Vector3 angularVelocity, bool allowMidAirSplit )
	{
		var root = map.RootOf( members );
		var go = new GameObject( true, $"Gib ({string.Join( "+", members.OrderBy( s => s ) )})" );
		go.Tags.Add( "gib" );
		go.WorldPosition = capsWorld[root].From;
		go.WorldRotation = Rotation.Identity;

		var gib = go.AddComponent<Gib>();
		gib.gore = gore;
		gib.map = map;
		gib.tint = tint;
		gib.members = members;
		gib.poseLocal = meshWorld.Select( t => go.WorldTransform.ToLocal( t ) ).ToArray();
		gib.capsLocal = capsWorld.ToDictionary( kv => kv.Key, kv => (go.WorldTransform.PointToLocal( kv.Value.From ), go.WorldTransform.PointToLocal( kv.Value.To )) );

		gib.body = go.AddComponent<Rigidbody>();
		gib.Rebuild();
		gib.body.Velocity = velocity;
		gib.body.AngularVelocity = angularVelocity;

		if ( allowMidAirSplit && members.Count > 1 && Game.Random.Float() < (gore?.MidAirSplitChance ?? 0f) )
			gib.pendingSplit = Game.Random.Float( 0.08f, 0.3f );

		gib.Begin();
		return gib;
	}

	/// <summary> Spawns a loose organ or meat chunk. Its model is centred on the given point. </summary>
	public static Gib CreateProp( Scene scene, GoreSystem gore, Model model, Vector3 position, Rotation rotation, Vector3 velocity )
	{
		var go = new GameObject( true, $"Meat ({model.ResourceName})" );
		go.Tags.Add( "gib" );
		go.WorldPosition = position;
		go.WorldRotation = rotation;

		// These models are authored where they'd sit on a body, so pull them back to the origin.
		var mesh = new GameObject( go, true, "Mesh" );
		mesh.LocalPosition = -model.Bounds.Center;
		mesh.AddComponent<ModelRenderer>().Model = model;
		mesh.AddComponent<ModelCollider>().Model = model;

		var gib = go.AddComponent<Gib>();
		gib.gore = gore;
		gib.health = 20f;
		gib.body = go.AddComponent<Rigidbody>();
		gib.body.MassOverride = 3f;
		gib.body.Velocity = velocity;
		gib.body.AngularVelocity = Vector3.Random * 15f;
		gib.Begin();
		return gib;
	}

	void Begin()
	{
		age = 0f;
		lifetime = (gore?.GibLifetime ?? 20f) + Game.Random.Float( 0f, 4f );
		lastDrip = 0f;
		gore?.Register( this );
	}

	/// <summary> Re-poses the mesh and colliders for the current set of segments. </summary>
	void Rebuild()
	{
		if ( renderer is null )
		{
			renderer = AddComponent<SkinnedModelRenderer>();
			renderer.Model = map.MeshModel;
			renderer.Tint = tint;
			renderer.UseAnimGraph = false;
		}

		var root = map.RootOf( members );
		var model = renderer.SceneModel;
		if ( model is not null )
		{
			model.ClearBoneOverrides();
			for ( var i = 0; i < poseLocal.Length; i++ )
			{
				var segment = map.MeshBoneSegment[i];
				var pose = members.Contains( segment )
					? poseLocal[i]
					: new Transform( CollapsePoint( segment, root ), poseLocal[i].Rotation, 0.0005f );
				model.SetBoneOverride( i, in pose );
			}
		}

		// One capsule per segment, so the piece tumbles with its real shape.
		foreach ( var old in GetComponents<CapsuleCollider>().ToList() ) old.Destroy();
		var mass = 0f;
		health = 0f;
		foreach ( var s in members )
		{
			var def = map.Defs[s];
			var (from, to) = capsLocal[s];
			var capsule = AddComponent<CapsuleCollider>();
			capsule.Start = from;
			capsule.End = to;
			capsule.Radius = def.Radius;
			mass += map.IsTorso( s ) ? 14f : s == Segment.Head ? 5f : 3.5f;
			health += def.GibHealth;
		}
		body.MassOverride = mass;

		AddWoundDecals( root );
	}

	/// <summary>
	/// Stamps a raw, bloody wound onto every cut face: where this piece was torn from its parent,
	/// and where any of its own branches were torn off. The decals ride along with the gib.
	/// </summary>
	void AddWoundDecals( Segment root )
	{
		foreach ( var old in GameObject.Children.Where( c => c.Name == "Wound" ).ToList() ) old.Destroy();
		if ( gore is null || gore.WoundDecals.Count == 0 ) return;

		var cuts = new List<(Vector3 Joint, Vector3 Inward)>();

		// Where this piece came off: look down its own root segment.
		if ( map.Defs[root].Parent is not null )
		{
			var (from, to) = capsLocal[root];
			cuts.Add( (from, (to - from).Normal) );
		}

		// Where branches came off: look back from the missing child into this piece.
		foreach ( var s in members )
		{
			foreach ( var child in map.Children( s ) )
			{
				if ( members.Contains( child ) ) continue;
				var (from, to) = capsLocal[child];
				cuts.Add( (from, -(to - from).Normal) );
			}
		}

		foreach ( var (joint, inward) in cuts )
		{
			if ( inward.IsNearlyZero() ) continue;
			var go = new GameObject( GameObject, true, "Wound" );
			go.LocalPosition = joint - inward * 4f;
			go.LocalRotation = Rotation.LookAt( inward ) * Rotation.FromRoll( Game.Random.Float( 0f, 360f ) );
			var decal = go.AddComponent<Decal>();
			decal.Decals = new() { Game.Random.FromList( gore.WoundDecals ) };
			decal.Size = new Vector2( 1f, 1f );
			decal.Scale = Game.Random.Float( 0.5f, 0.8f );
			decal.Depth = 10f;
		}
	}

	/// <summary>
	/// Where a bone outside this gib gets shrunk to: the joint where its branch meets the gib,
	/// or this gib's own root joint if it sits above it. Shrinking at the joint keeps the cut neat.
	/// </summary>
	Vector3 CollapsePoint( Segment segment, Segment root )
	{
		var current = segment;
		while ( true )
		{
			var parent = map.Defs[current].Parent;
			if ( parent is null ) return capsLocal[root].From;
			if ( members.Contains( parent.Value ) ) return capsLocal[current].From;
			current = parent.Value;
		}
	}

	// ---- Damage -----------------------------------------------------------

	public void TakeHit( Vector3 position, Vector3 direction, float damage, float impulse )
	{
		if ( IsFading ) return;

		var physics = body?.PhysicsBody;
		physics?.ApplyImpulseAt( position, direction * impulse * physics.Mass );

		gore?.Burst( position, -direction, 0.5f );
		gore?.Spray( position, direction, 1, 120f, 0.3f, 0.7f );

		health -= damage;
		if ( health > 0f ) return;

		if ( IsProp || members.Count <= 1 ) Pulverize( position, direction );
		else Split( NearestSegment( position ), direction );
	}

	Segment NearestSegment( Vector3 worldPosition )
	{
		var local = WorldTransform.PointToLocal( worldPosition );
		return members.OrderBy( s =>
		{
			var (a, b) = capsLocal[s];
			var ab = b - a;
			var t = ab.LengthSquared > 0.001f ? MathX.Clamp( Vector3.Dot( local - a, ab ) / ab.LengthSquared, 0f, 1f ) : 0f;
			return (a + ab * t).DistanceSquared( local );
		} ).First();
	}

	/// <summary> Breaks this gib in two at a segment: that segment and everything below it flies off. </summary>
	public void Split( Segment at, Vector3 direction )
	{
		if ( members.Count <= 1 ) { Pulverize( WorldPosition, direction ); return; }

		var root = map.RootOf( members );
		if ( at == root || !members.Contains( at ) )
		{
			// Hit the root: snap off one of its branches instead.
			var branches = map.Children( root ).Where( members.Contains ).ToList();
			if ( branches.Count == 0 ) { Pulverize( WorldPosition, direction ); return; }
			at = Game.Random.FromList( branches );
		}

		var piece = map.Subtree( at, members );
		var meshWorld = poseLocal.Select( t => WorldTransform.ToWorld( t ) ).ToArray();
		var capsWorld = capsLocal.ToDictionary( kv => kv.Key, kv => (WorldTransform.PointToWorld( kv.Value.From ), WorldTransform.PointToWorld( kv.Value.To )) );
		var joint = capsWorld[at].Item1;

		var push = (direction.Normal + Vector3.Random * 0.6f).Normal * Game.Random.Float( 90f, 200f );
		var spin = Vector3.Random * Game.Random.Float( 6f, 14f );
		var velocity = body.Velocity;

		CreateSkinned( Scene, gore, map, tint, meshWorld, capsWorld, piece, velocity + push, spin, allowMidAirSplit: false );

		members.ExceptWith( piece );
		Rebuild();
		body.Velocity = velocity - push * 0.3f;

		gore?.Burst( joint, direction, 0.8f );
		gore?.Spray( joint, direction, 3, 160f );
		if ( map.IsTorso( at ) || map.IsTorso( root ) ) gore?.ThrowMeat( joint, direction, 1, organs: true );
	}

	/// <summary> Blows the piece apart into a spray of blood (and sometimes a scrap of meat). </summary>
	public void Pulverize( Vector3 position, Vector3 direction )
	{
		gore?.Burst( position, direction, 1.2f );
		gore?.Spray( position, direction, 5, 200f, 0.5f, 1.4f );

		var torso = !IsProp && members.Any( map.IsTorso );
		if ( !IsProp && Game.Random.Float() < (torso ? 0.9f : 0.4f) )
			gore?.ThrowMeat( position, direction, torso ? 2 : 1, organs: torso );

		GameObject.Destroy();
	}

	// ---- Life -------------------------------------------------------------

	public void BeginFade()
	{
		if ( IsFading ) return;
		IsFading = true;
		fadeStarted = 0f;
		scaleBeforeFade = WorldScale;
	}

	protected override void OnUpdate()
	{
		if ( pendingSplit is { } split && split )
		{
			pendingSplit = null;
			var candidates = members.Where( s => s != map.RootOf( members ) ).ToList();
			if ( candidates.Count > 0 ) Split( Game.Random.FromList( candidates ), body.Velocity.Normal );
		}

		if ( !IsFading && age > lifetime ) BeginFade();

		if ( IsFading )
		{
			var t = fadeStarted / (gore?.FadeTime ?? 1f);
			if ( t >= 1f ) { GameObject.Destroy(); return; }
			WorldScale = scaleBeforeFade * MathX.Lerp( 1f, 0.01f, t * t );
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( body is null || gore is null || IsFading ) return;

		var speed = body.Velocity.Length;

		// Blood trail while it's flying.
		if ( age < 2f && speed > 200f && lastDrip > 0.11f )
		{
			lastDrip = 0f;
			gore.Drip( WorldPosition );
		}

		// A sudden stop means it hit something: splat.
		if ( splats < 3 && lastSpeed > 220f && speed < lastSpeed * 0.55f )
		{
			splats++;
			if ( gore.TraceWorld( WorldPosition + Vector3.Up * 10f, WorldPosition + Vector3.Down * 60f ) is { } hit )
				gore.Splat( hit.Position, hit.Normal, Game.Random.Float( 0.7f, 1.4f ) );
		}

		lastSpeed = speed;
	}
}
