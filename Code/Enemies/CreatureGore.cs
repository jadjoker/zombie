/// <summary>
/// Gore that sticks to the creatures themselves (they're built from blob shapes, so decals don't work
/// on them): bullet wounds that sit exactly on the curved skin where the shot landed, and goo-filled
/// stumps that ooze and drip where a part was torn off. Everything here is local (cosmetic) and runs on
/// every machine from the same hit and cut broadcasts.
/// </summary>
public static class CreatureGore
{
	/// <summary> Most wounds one part keeps; older ones close up as new ones open. </summary>
	const int MaxWoundsPerPart = 7;

	/// <summary>
	/// How many parts a creature's synced gone-mask can track: one bit each, in an int. The creatures
	/// keep their part counts under this, so the mask never overflows.
	/// </summary>
	public const int MaxMaskParts = 31;

	/// <summary>
	/// Where a shot along this line actually meets the skin of a part: the nearest hit on any of its
	/// blob shapes (ellipsoids), with the surface normal there. Null if it misses them all.
	/// </summary>
	public static (Vector3 Position, Vector3 Normal, GameObject Shape)? SkinHit( GameObject part, Vector3 origin, Vector3 direction )
	{
		if ( !part.IsValid() ) return null;

		// Exact: trace the part's real render mesh, so the wound sits right where the bullet met the skin.
		var exact = part.Scene.Trace.Ray( origin, origin + direction.Normal * 160f )
			.UseRenderMeshes( true ).UsePhysicsWorld( false )
			.WithoutTags( "gore", "viewmodel", "player" )
			.Run();
		if ( exact.Hit && exact.GameObject.IsValid() && BelongsTo( exact.GameObject, part ) )
			return (exact.HitPosition, exact.Normal, exact.GameObject);

		// Fallback: the ellipsoid that fits each blob shape.
		(Vector3, Vector3, GameObject)? best = null;
		var bestDistance = float.MaxValue;

		foreach ( var renderer in part.Components.GetAll<ModelRenderer>( FindMode.EnabledInSelfAndDescendants ) )
		{
			if ( renderer.Model is null ) continue;
			var go = renderer.GameObject;
			if ( go.Name is "Gore" ) continue; // wounds and stumps aren't skin
			// Don't reach into child parts (an arm's shapes belong to the arm, not the body).
			if ( go != part && go.Components.Get<CreaturePart>( FindMode.InAncestors ) is { } owner && owner.GameObject != part ) continue;

			// Treat the shape as the ellipsoid that fits its bounds (exact for the blob spheres,
			// close enough for sculpted parts), and work in its own space where that's a unit sphere.
			var bounds = renderer.Model.Bounds;
			var center = bounds.Center;
			var extent = bounds.Size * 0.5f * (renderer.Model == Shapes.Sphere ? 1f : 0.92f);
			if ( extent.x < 0.01f || extent.y < 0.01f || extent.z < 0.01f ) continue;
			var tx = go.WorldTransform;
			var lo = tx.PointToLocal( origin ) - center;
			var ld = tx.PointToLocal( origin + direction ) - center - lo;
			var o = new Vector3( lo.x / extent.x, lo.y / extent.y, lo.z / extent.z );
			var d = new Vector3( ld.x / extent.x, ld.y / extent.y, ld.z / extent.z );
			const float radius = 1f;
			var a = Vector3.Dot( d, d );
			if ( a < 1e-6f ) continue;
			var b = 2f * Vector3.Dot( o, d );
			var c = Vector3.Dot( o, o ) - radius * radius;
			var disc = b * b - 4f * a * c;
			if ( disc < 0f ) continue;
			var t = (-b - MathF.Sqrt( disc )) / (2f * a);
			if ( t < 0f ) t = (-b + MathF.Sqrt( disc )) / (2f * a);
			if ( t < 0f ) continue;

			var unit = o + d * t;
			var local = new Vector3( unit.x * extent.x, unit.y * extent.y, unit.z * extent.z );
			var world = tx.PointToWorld( local + center );
			var distance = world.DistanceSquared( origin );
			if ( distance >= bestDistance ) continue;

			// Normal of the ellipsoid, pushed back out through its scale into world space.
			var scale = go.WorldScale;
			var n = new Vector3( unit.x / extent.x, unit.y / extent.y, unit.z / extent.z );
			var normal = (go.WorldRotation * new Vector3( n.x / MathF.Max( scale.x, 1e-4f ), n.y / MathF.Max( scale.y, 1e-4f ), n.z / MathF.Max( scale.z, 1e-4f ) )).Normal;
			bestDistance = distance;
			best = (world, normal, go);
		}
		return best;
	}

	/// <summary> True if `go` is one of `part`'s own shapes (not a child part's, and not gore). </summary>
	static bool BelongsTo( GameObject go, GameObject part )
	{
		if ( go.Name is "Gore" ) return false;
		if ( go != part && !go.IsDescendant( part ) ) return false;
		return go == part || go.Components.Get<CreaturePart>( FindMode.InAncestors ) is not { } owner || owner.GameObject == part;
	}

	/// <summary>
	/// A bullet wound on a part's skin: a dark, wet crater with bright goo in the middle, a little
	/// bigger each time the same spot is hit again. Sticks to the part as it moves.
	/// </summary>
	public static void AddWound( GameObject part, Vector3 hitPosition, Vector3 direction, Color skin, Color goo, float size )
	{
		if ( !part.IsValid() ) return;
		direction = direction.Normal;
		var origin = hitPosition - direction * 60f;
		if ( SkinHit( part, origin, direction ) is not { } hit ) return;

		// A bullet hole is small: about half the old crater.
		size *= 0.5f;
		ImpactSpray( part.Scene, hit.Position, hit.Normal, direction, goo, size );

		// Hitting right next to an open wound widens it instead of adding another.
		var wounds = part.Children.Where( c => c.Name == "Wound" ).ToList();
		var near = wounds.FirstOrDefault( w => w.WorldPosition.Distance( hit.Position ) < size * 1.1f );
		if ( near is not null )
		{
			if ( near.LocalScale.x < 1.8f ) near.LocalScale *= 1.15f;
			return;
		}

		if ( wounds.Count >= MaxWoundsPerPart ) wounds[0].Destroy();

		var wound = new GameObject( part, true, "Wound" );
		wound.Tags.Add( "gore" );
		wound.WorldPosition = hit.Position;
		wound.WorldRotation = Rotation.LookAt( hit.Normal ) * Rotation.FromRoll( Game.Random.Float( 0f, 360f ) );
		var partScale = part.WorldScale.x;
		var s = size / MathF.Max( partScale, 0.01f );

		// Flat and tight: a thin bruised rim, a dark punched hole, a wet bead of goo welling up in it.
		// Slightly oval when the shot came in at an angle, like a real entry wound.
		var oval = 1f + (1f - MathF.Abs( Vector3.Dot( direction, hit.Normal ) )) * 0.6f;
		Blob( wound, new Vector3( -0.05f * s, 0, 0 ), new Vector3( 0.05f, 1f * oval, 0.9f ) * s, Color.Lerp( skin, Color.Black, 0.45f ) );
		Blob( wound, new Vector3( -0.03f * s, 0, 0 ), new Vector3( 0.07f, 0.6f * oval, 0.55f ) * s, Color.Lerp( goo, Color.Black, 0.75f ) );
		Blob( wound, new Vector3( -0.01f * s, 0.08f * s, -0.1f * s ), new Vector3( 0.09f, 0.32f, 0.4f ) * s, Color.Lerp( goo, Color.Black, 0.2f ) );

		// A thin trickle running out of it.
		var drip = wound.AddComponent<GooOoze>();
		drip.Color = goo;
		drip.Lifetime = Game.Random.Float( 1.5f, 3f );
		drip.Interval = 0.3f;
	}

	/// <summary>
	/// The splash of a bullet going in: a fine fan of goo kicked back off the skin toward the shooter,
	/// plus a few drops blown out along the bullet's path.
	/// </summary>
	public static void ImpactSpray( Scene scene, Vector3 at, Vector3 normal, Vector3 direction, Color goo, float size )
	{
		var reflect = direction - 2f * Vector3.Dot( direction, normal ) * normal;
		var back = (normal * 0.8f + reflect * 0.5f).Normal;
		var drops = Game.Random.Int( 5, 9 );
		for ( var i = 0; i < drops; i++ )
		{
			var dir = (back + Vector3.Random * 0.45f).Normal;
			GooDroplet.Spawn( scene, at + normal * 1.5f, dir * Game.Random.Float( 110f, 280f ), goo, Game.Random.Float( 0.3f, 0.65f ) );
		}
		// Through-and-through: a little goes on out with the bullet.
		for ( var i = 0; i < 3; i++ )
			GooDroplet.Spawn( scene, at + direction * size * 2f, (direction + Vector3.Random * 0.25f).Normal * Game.Random.Float( 250f, 420f ), goo, Game.Random.Float( 0.35f, 0.7f ) );
	}

	/// <summary>
	/// A torn-off joint: a goo-filled stump left on the part it came off, oozing and dripping for a while.
	/// </summary>
	public static GameObject AddStump( GameObject attachTo, Vector3 worldPosition, Vector3 outward, float radius, Color skin, Color goo, float dripSeconds = 6f )
	{
		if ( !attachTo.IsValid() ) return null;
		var stump = new GameObject( attachTo, true, "Stump" );
		stump.WorldPosition = worldPosition;
		stump.WorldRotation = Rotation.LookAt( outward.IsNearlyZero() ? Vector3.Up : outward.Normal );
		var s = radius / MathF.Max( attachTo.WorldScale.x, 0.01f );

		// Ragged rim of skin, then the raw goo inside, then a hanging blob.
		Blob( stump, Vector3.Zero, new Vector3( 0.45f, 1.05f, 1.05f ) * s, Color.Lerp( skin, Color.Black, 0.35f ) );
		Blob( stump, new Vector3( 0.2f * s, 0, 0 ), new Vector3( 0.35f, 0.85f, 0.85f ) * s, Color.Lerp( goo, Color.White, 0.22f ) );
		Blob( stump, new Vector3( 0.32f * s, 0, 0 ), new Vector3( 0.2f, 0.45f, 0.45f ) * s, new Color( 0.12f, 0.02f, 0.04f ) );
		Blob( stump, new Vector3( 0.35f * s, 0.2f * s, -0.3f * s ), new Vector3( 0.3f, 0.3f, 0.45f ) * s, Color.Lerp( goo, Color.White, 0.1f ) );

		if ( dripSeconds > 0f )
		{
			// Arterial spray first: pulsing jets with the heartbeat, weaker on the piece that came off.
			var jet = stump.AddComponent<GooJet>();
			jet.Color = goo;
			jet.Lifetime = MathX.Clamp( dripSeconds * 0.45f, 1f, 2.8f );
			jet.Strength = MathX.Clamp( radius / 5f, 0.5f, 1.6f ) * (dripSeconds < 3f ? 0.55f : 1f);

			// Then it just runs and drips.
			var drip = stump.AddComponent<GooOoze>();
			drip.Color = goo;
			drip.Lifetime = dripSeconds;
			drip.Interval = 0.14f;
		}
		return stump;
	}

	/// <summary> Kills from closer than this (shotgun to the face) are overkills. </summary>
	public const float OverkillRange = 220f;

	/// <summary>
	/// A point-blank kill: the creature blows apart much harder. A big blast of goo, extra chunks of
	/// flesh, a splatter thrown across whatever's behind it, and a jolt for anyone standing close.
	/// </summary>
	public static void Overkill( Scene scene, Vector3 at, Vector3 direction, Color skin, Color goo, float scale = 1f )
	{
		var gore = GoreSystem.Get( scene );
		if ( gore is null ) return;
		direction = direction.IsNearlyZero() ? Vector3.Up : direction.Normal;

		for ( var i = 0; i < 3; i++ )
			gore.GooBurst( at, (direction + Vector3.Random * 0.7f).Normal, goo, 12, 420f, 1.4f * scale );
		// Flat and far, so it paints the wall behind as well as the floor.
		gore.GooSpray( at, direction, 14, goo, 520f, 0.9f, 2.4f, sag: 0.15f );
		gore.GooSpray( at, Vector3.Down, 4, goo, 200f, 1.2f, 2.4f );
		var blobs = Game.Random.Int( 3, 5 );
		for ( var i = 0; i < blobs; i++ )
			gore.ThrowBlob( at, (direction + Vector3.Random * 0.9f + Vector3.Up * 0.4f).Normal, skin, goo, Game.Random.Float( 2.5f, 4.5f ) * scale );
		gore.Squelch( at, 1.4f, 0.7f );

		// Feel it if you're close; right up close it gets on the camera.
		foreach ( var player in scene.GetAllComponents<PlayerSurvivor>() )
		{
			if ( player.IsProxy ) continue;
			var distance = (player.WorldPosition + Vector3.Up * 60f).Distance( at );
			if ( distance > 350f ) continue;
			player.GetComponent<CameraJuice>()?.Shake( 0.25f );
			if ( distance < 260f )
				player.GetComponent<PlayerAfflictions>()?.Splattered( goo, distance < 150f ? 7 : 4 );
		}
	}

	/// <summary>
	/// A badly hurt creature leaks: goo drips under it as it goes, faster the closer it is to death.
	/// Call every frame with its health fraction and a timer it keeps.
	/// </summary>
	public static void BleedTrail( Scene scene, Vector3 at, Color goo, float healthFraction, ref TimeSince timer )
	{
		if ( healthFraction > 0.6f || healthFraction <= 0f ) return;
		var interval = MathX.Lerp( 0.1f, 0.5f, healthFraction / 0.6f );
		if ( timer < interval ) return;
		timer = 0f;
		var gore = GoreSystem.Get( scene );
		if ( gore is null ) return;
		var drop = at + Vector3.Random.WithZ( 0 ) * 6f;
		gore.GooDrip( drop, goo );
		if ( Game.Random.Float() < 0.5f )
			GooDroplet.Spawn( scene, drop, Vector3.Down * 30f, goo, Game.Random.Float( 0.7f, 1.3f ) );
	}

	/// <summary> A severed piece keeps spasming for a while: little hops and flips that die down. </summary>
	public static void Twitch( GameObject piece, float seconds = 3f, float strength = 1f )
	{
		if ( !piece.IsValid() ) return;
		var twitch = piece.Components.Get<Twitch>() ?? piece.AddComponent<Twitch>();
		twitch.Seconds = seconds;
		twitch.Strength = strength;
	}

	/// <summary> A quick squash-and-wobble on a part that just got hit. Blobby things should jiggle. </summary>
	public static void Jiggle( GameObject part, float strength = 1f )
	{
		if ( !part.IsValid() ) return;
		var jiggle = part.Components.Get<Jiggle>() ?? part.AddComponent<Jiggle>();
		jiggle.Punch( strength );
	}

	static void Blob( GameObject parent, Vector3 at, Vector3 radii, Color color )
	{
		var go = new GameObject( parent, true, "Gore" );
		go.Tags.Add( "gore" );
		go.LocalPosition = at;
		go.LocalScale = radii * Shapes.SphereScale( 1f );
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Shapes.Sphere;
		r.Tint = color;
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
	}
}

/// <summary>
/// Makes a loose creature piece spasm: random little hops, flips and twists, weaker and rarer as it
/// runs down. Only while it's resting on something, so it doesn't fly about in mid-air.
/// </summary>
public sealed class Twitch : Component
{
	public float Seconds { get; set; } = 3f;
	public float Strength { get; set; } = 1f;

	TimeSince age;
	TimeUntil next;

	protected override void OnStart()
	{
		age = 0f;
		next = Game.Random.Float( 0.25f, 0.5f );
	}

	protected override void OnFixedUpdate()
	{
		if ( age > Seconds ) { Destroy(); return; }
		if ( !next ) return;

		var life = 1f - age / Seconds;
		next = Game.Random.Float( 0.15f, 0.45f ) / MathF.Max( 0.3f, life );

		var body = Components.Get<Rigidbody>();
		if ( !body.IsValid() || body.Velocity.Length > 120f ) return; // still flying

		var kick = Strength * life;
		body.Velocity += (Vector3.Up * Game.Random.Float( 90f, 190f ) + Vector3.Random.WithZ( 0 ) * 60f) * kick;
		body.AngularVelocity += Vector3.Random * 14f * kick;
	}
}

/// <summary>
/// Wobbles a part's scale after a hit, settling back to its rest size. Re-hitting it punches again.
/// </summary>
public sealed class Jiggle : Component
{
	Vector3 rest;
	bool hasRest;
	float amount;
	TimeSince since;

	public void Punch( float strength )
	{
		if ( !hasRest ) { rest = LocalScale; hasRest = true; }
		amount = MathF.Min( 0.3f, amount * 0.5f + 0.16f * strength );
		since = 0f;
	}

	protected override void OnUpdate()
	{
		if ( !hasRest ) return;
		var t = since.Relative;
		var decay = MathF.Exp( -t * 9f );
		if ( decay < 0.02f )
		{
			LocalScale = rest;
			amount = 0f;
			return;
		}
		// Squash on one axis, bulge on the others, ringing down.
		var wave = MathF.Sin( t * 38f ) * amount * decay;
		LocalScale = rest * new Vector3( 1f - wave, 1f + wave * 0.6f, 1f + wave * 0.6f );
	}
}

/// <summary>
/// Goo leaking from a wound or stump: drops fall (splatting where they land) and slow to a stop over
/// its lifetime. A fresh stump spurts with the heartbeat for the first second or two.
/// </summary>
public sealed class GooOoze : Component
{
	public Color Color { get; set; }
	public float Lifetime { get; set; } = 3f;
	public float Interval { get; set; } = 0.3f;
	public bool Spurt { get; set; }

	TimeSince age;
	TimeSince lastDrop;

	protected override void OnStart()
	{
		age = 0f;
		lastDrop = 0f;
	}

	protected override void OnUpdate()
	{
		if ( age > Lifetime ) { Destroy(); return; }

		// Drips come slower as it runs dry.
		var t = age / MathF.Max( 0.1f, Lifetime );
		if ( lastDrop < Interval * (1f + t * 3f) ) return;
		lastDrop = 0f;

		var at = WorldPosition;
		var outward = WorldRotation.Forward;
		if ( Spurt && age < 1.6f )
		{
			// Pulsing spurts out of a fresh stump.
			var beat = MathF.Sin( age * 9f ) > 0.3f;
			if ( beat )
			{
				for ( var i = 0; i < 2; i++ )
					GooDroplet.Spawn( Scene, at, (outward + Vector3.Random * 0.35f).Normal * Game.Random.Float( 90f, 200f ), Color, Game.Random.Float( 0.8f, 1.6f ) );
			}
			return;
		}
		GooDroplet.Spawn( Scene, at + Vector3.Random * 1.5f, Vector3.Down * 20f + Vector3.Random * 10f, Color, Game.Random.Float( 0.6f, 1.2f ) );
	}
}

/// <summary>
/// Arterial spray from a fresh stump: a tight stream of goo that surges with each heartbeat, arcing out
/// and pattering down, weakening as it bleeds out. Follows the stump as the creature moves.
/// </summary>
public sealed class GooJet : Component
{
	public Color Color { get; set; }
	public float Lifetime { get; set; } = 2.4f;
	public float Strength { get; set; } = 1f;

	TimeSince age;
	float owed;

	protected override void OnStart() => age = 0f;

	protected override void OnUpdate()
	{
		if ( age > Lifetime ) { Destroy(); return; }
		var life = 1f - age / Lifetime;

		// Heartbeat: a hard surge, then a thin weak stream until the next one.
		var phase = (age * 2.2f) % 1f;
		var surge = phase < 0.25f ? MathF.Sin( phase / 0.25f * MathF.PI ) : 0f;
		var flow = 0.25f + surge * 0.75f;

		owed += 260f * flow * Strength * (0.3f + 0.7f * life) * Time.Delta;
		var outward = WorldRotation.Forward;
		var speed = (160f + 300f * surge) * (0.45f + 0.55f * life) * MathF.Sqrt( Strength );
		while ( owed >= 1f )
		{
			owed -= 1f;
			var dir = (outward + Vector3.Random * (0.035f + 0.06f * (1f - surge))).Normal;
			GooDroplet.Spawn( Scene, WorldPosition + dir * Game.Random.Float( 0f, 2f ), dir * speed * Game.Random.Float( 0.85f, 1.1f ), Color,
				Game.Random.Float( 0.45f, 0.95f ) * MathF.Sqrt( Strength ), 1.8f );
		}
	}
}
