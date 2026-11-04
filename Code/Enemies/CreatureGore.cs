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
	/// Where a shot along this line actually meets the skin of a part: the nearest hit on any of its
	/// blob shapes (ellipsoids), with the surface normal there. Null if it misses them all.
	/// </summary>
	public static (Vector3 Position, Vector3 Normal, GameObject Shape)? SkinHit( GameObject part, Vector3 origin, Vector3 direction )
	{
		if ( !part.IsValid() ) return null;
		var radius = Shapes.Sphere.Bounds.Size.x * 0.5f;
		var center = Shapes.Sphere.Bounds.Center;
		(Vector3, Vector3, GameObject)? best = null;
		var bestDistance = float.MaxValue;

		foreach ( var renderer in part.Components.GetAll<ModelRenderer>( FindMode.EnabledInSelfAndDescendants ) )
		{
			if ( renderer.Model != Shapes.Sphere ) continue;
			var go = renderer.GameObject;
			if ( go.Name is "Gore" ) continue; // wounds and stumps aren't skin
			// Don't reach into child parts (an arm's shapes belong to the arm, not the body).
			if ( go != part && go.Components.Get<CreaturePart>( FindMode.InAncestors ) is { } owner && owner.GameObject != part ) continue;

			// Into the shape's own space, where it's a plain sphere.
			var tx = go.WorldTransform;
			var o = tx.PointToLocal( origin ) - center;
			var d = tx.PointToLocal( origin + direction ) - center - o;
			var a = Vector3.Dot( d, d );
			if ( a < 1e-6f ) continue;
			var b = 2f * Vector3.Dot( o, d );
			var c = Vector3.Dot( o, o ) - radius * radius;
			var disc = b * b - 4f * a * c;
			if ( disc < 0f ) continue;
			var t = (-b - MathF.Sqrt( disc )) / (2f * a);
			if ( t < 0f ) t = (-b + MathF.Sqrt( disc )) / (2f * a);
			if ( t < 0f ) continue;

			var local = o + d * t;
			var world = tx.PointToWorld( local + center );
			var distance = world.DistanceSquared( origin );
			if ( distance >= bestDistance ) continue;

			// The normal of a squashed sphere: push the local normal back out through the scale.
			var scale = go.WorldScale;
			var n = local.Normal;
			var normal = (go.WorldRotation * new Vector3( n.x / MathF.Max( scale.x, 1e-4f ), n.y / MathF.Max( scale.y, 1e-4f ), n.z / MathF.Max( scale.z, 1e-4f ) )).Normal;
			bestDistance = distance;
			best = (world, normal, go);
		}
		return best;
	}

	/// <summary>
	/// A bullet wound on a part's skin: a dark, wet crater with bright goo in the middle, a little
	/// bigger each time the same spot is hit again. Sticks to the part as it moves.
	/// </summary>
	public static void AddWound( GameObject part, Vector3 hitPosition, Vector3 direction, Color skin, Color goo, float size )
	{
		if ( !part.IsValid() ) return;
		var origin = hitPosition - direction.Normal * 60f;
		if ( SkinHit( part, origin, direction.Normal ) is not { } hit ) return;

		// Hitting right next to an open wound widens it instead of adding another.
		var wounds = part.Children.Where( c => c.Name == "Wound" ).ToList();
		var near = wounds.FirstOrDefault( w => w.WorldPosition.Distance( hit.Position ) < size * 1.2f );
		if ( near is not null )
		{
			if ( near.LocalScale.x < 1.6f ) near.LocalScale *= 1.12f;
			return;
		}

		if ( wounds.Count >= MaxWoundsPerPart ) wounds[0].Destroy();

		var wound = new GameObject( part, true, "Wound" );
		wound.WorldPosition = hit.Position;
		wound.WorldRotation = Rotation.LookAt( hit.Normal ) * Rotation.FromRoll( Game.Random.Float( 0f, 360f ) );
		var partScale = part.WorldScale.x;
		var s = size / MathF.Max( partScale, 0.01f );

		// Bruised ring flush with the skin, a dark hole inside it, wet goo in the bottom of the hole.
		// Kept nearly flat so it reads as a hole, not a blister.
		Blob( wound, new Vector3( -0.1f * s, 0, 0 ), new Vector3( 0.14f, 1f, 0.85f ) * s, Color.Lerp( skin, new Color( 0.25f, 0f, 0.05f ), 0.55f ) );
		Blob( wound, new Vector3( -0.07f * s, 0, 0 ), new Vector3( 0.12f, 0.7f, 0.6f ) * s, new Color( 0.08f, 0.01f, 0.03f ) );
		Blob( wound, new Vector3( -0.05f * s, 0.05f * s, -0.08f * s ), new Vector3( 0.11f, 0.42f, 0.36f ) * s, Color.Lerp( goo, Color.White, 0.15f ) );

		// A short ooze running down from it.
		var drip = wound.AddComponent<GooOoze>();
		drip.Color = goo;
		drip.Lifetime = Game.Random.Float( 1.5f, 3f );
		drip.Interval = 0.35f;
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
			var drip = stump.AddComponent<GooOoze>();
			drip.Color = goo;
			drip.Lifetime = dripSeconds;
			drip.Interval = 0.12f;
			drip.Spurt = true;
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

		// Feel it if you're close.
		foreach ( var player in scene.GetAllComponents<PlayerSurvivor>() )
		{
			if ( player.IsProxy || player.WorldPosition.Distance( at ) > 350f ) continue;
			player.GetComponent<CameraJuice>()?.Shake( 0.25f );
		}
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
		go.LocalPosition = at;
		go.LocalScale = radii * Shapes.SphereScale( 1f );
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Shapes.Sphere;
		r.Tint = color;
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
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
