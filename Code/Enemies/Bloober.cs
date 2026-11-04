/// <summary>
/// Bloober: a snake made of spotted blobs that floats and slithers through the air. Its spots sit flat
/// most of the time and puff out into spikes like a pufferfish (taking less damage while puffed).
/// It darts in to bite, then peels away; a few bites in a row poison you with venom.
///
/// Chunking: every body segment has its own health. Shoot one off and it bursts, and the tail behind it
/// drops as loose pieces you can keep shooting, while the rest of the snake keeps coming, shorter.
/// The head is the weak spot: its health is the whole creature's.
///
/// Networking: the host flies the head (the root object, which is networked); every machine builds the
/// body locally and drags it along behind the head, so only the head needs syncing. Cuts are decided on
/// the host and broadcast; a synced mask lets late joiners catch up.
/// </summary>
public sealed class Bloober : Component, ICreature
{
	[Property, Group( "Body" )] public int SegmentCount { get; set; } = 9;
	[Property, Group( "Body" )] public float HeadRadius { get; set; } = 13f;
	[Property, Group( "Body" )] public float TailRadius { get; set; } = 5f;
	/// <summary> Segments are this much longer than they are wide. </summary>
	[Property, Group( "Body" )] public float Stretch { get; set; } = 1.25f;
	/// <summary> Gap between segment centres, as a fraction of their two radii. Lower overlaps more. </summary>
	[Property, Group( "Body" )] public float Spacing { get; set; } = 0.72f;

	[Property, Group( "Movement" )] public float MoveSpeed { get; set; } = 170f;
	[Property, Group( "Movement" )] public float TurnRate { get; set; } = 2.6f;
	/// <summary> How far the slither swings the head side to side, in degrees. </summary>
	[Property, Group( "Movement" )] public float WiggleDegrees { get; set; } = 40f;
	[Property, Group( "Movement" )] public float WiggleRate { get; set; } = 2.4f;
	[Property, Group( "Movement" )] public float MinAltitude { get; set; } = 28f;
	/// <summary> Where on the player it aims, above their feet. </summary>
	[Property, Group( "Movement" )] public float AimHeight { get; set; } = 50f;

	[Property, Group( "Bite" )] public float BiteRange { get; set; } = 95f;
	[Property, Group( "Bite" )] public float BiteReach { get; set; } = 40f;
	[Property, Group( "Bite" )] public float BiteDamage { get; set; } = 10f;
	[Property, Group( "Bite" )] public float BiteCooldown { get; set; } = 1.4f;
	[Property, Group( "Bite" )] public float LungeSpeed { get; set; } = 560f;
	[Property, Group( "Bite" )] public float LungeTime { get; set; } = 0.3f;
	[Property, Group( "Bite" )] public float RetreatTime { get; set; } = 0.75f;

	[Property, Group( "Damage" )] public float SegmentHealth { get; set; } = 40f;
	/// <summary> Head hits hurt the whole creature this much. </summary>
	[Property, Group( "Damage" )] public float HeadMultiplier { get; set; } = 2.2f;
	/// <summary> Body hits hurt the whole creature this much (they mostly chew through segments). </summary>
	[Property, Group( "Damage" )] public float BodyMultiplier { get; set; } = 0.5f;
	/// <summary> Chance a hit knocks a little blob of flesh loose. </summary>
	[Property, Group( "Damage" )] public float ChipChance { get; set; } = 0.4f;

	[Property, Group( "Puff" )] public Vector2 PuffEvery { get; set; } = new( 7f, 12f );
	[Property, Group( "Puff" )] public float PuffDuration { get; set; } = 1.8f;
	[Property, Group( "Puff" )] public float PuffSize { get; set; } = 1.3f;
	/// <summary> Damage taken while puffed up, as a fraction. </summary>
	[Property, Group( "Puff" )] public float PuffDamageScale { get; set; } = 0.5f;
	/// <summary> Chance a hit makes it puff up in defence. </summary>
	[Property, Group( "Puff" )] public float PuffWhenHurtChance { get; set; } = 0.2f;

	/// <summary> Colour seed, picked by the host before the creature is networked. </summary>
	[Sync] public float Hue { get; set; } = -1f;
	[Sync] public bool Puffed { get; set; }
	[Sync] public bool Biting { get; set; }
	/// <summary> Which segments are gone (bit per segment), for anyone who missed the cuts. </summary>
	[Sync] public int GoneMask { get; set; }

	public bool IsDead => health?.IsDead ?? false;
	public Color BodyColor { get; private set; }
	public Color SpotColor { get; private set; }
	public Color GooColor { get; private set; }

	/// <summary> Fired on the host when it dies. The wave director listens to this. </summary>
	public event System.Action<Bloober> Killed;

	sealed class Spot
	{
		public GameObject Go;
		public Vector3 Dir;
		public float Phase;
	}

	sealed class Segment
	{
		public int Index;
		public GameObject Go;
		public GameObject Skin;
		public ModelRenderer Renderer;
		public SphereCollider Collider;
		public float Radius;
		public float Health;
		public float Hurt;
		public bool Gone;
		public Vector3 Position;
		public Vector3 Velocity;
		public TimeSince Flash = 99f;
		public float ColliderPuff = -1f;
		public readonly List<Spot> Spots = new();
		public GameObject Mouth;
	}

	enum Mode { Chase, Lunge, Retreat }

	readonly List<Segment> segments = new();
	Health health;
	GoreSystem gore;
	Mode mode;
	TimeSince modeTime;
	TimeSince timeSinceBite = 999f;
	TimeSince timeSincePuff = 999f;
	TimeUntil nextPuff;
	Vector3 heading;
	Vector3 lungeDirection;
	bool bitThisLunge;
	float puff;
	float mouthOpen;
	float wigglePhase;
	bool diedLocally;
	TimeSince timeSinceDeath;
	Vector3 lastShotDirection = Vector3.Forward;
	bool lastShotPointBlank;

	// ---- Setup ------------------------------------------------------------

	protected override void OnStart()
	{
		health = GetComponent<Health>();
		gore = GoreSystem.Get( Scene );

		if ( !IsProxy && Hue < 0f ) Hue = Game.Random.Float();
		var hue = Hue < 0f ? 0.85f : Hue;
		BodyColor = Shapes.Hsv( hue, 0.72f, 1f );
		SpotColor = Shapes.Hsv( hue + 0.5f, 0.85f, 0.35f );
		GooColor = Shapes.Hsv( hue + 0.18f, 0.95f, 1f );

		heading = WorldRotation.Forward;
		wigglePhase = hue * 20f;
		nextPuff = Game.Random.Float( PuffEvery.x, PuffEvery.y );

		if ( health.IsValid() && !IsProxy ) health.Died += OnDied;

		BuildBody( hue );
	}

	void BuildBody( float hue )
	{
		// Seeded from the colour, so every machine builds the same lumpy shape.
		var random = new System.Random( (int)(hue * 100000f) );
		float Range( float a, float b ) => a + (float)random.NextDouble() * (b - a);

		var count = Math.Clamp( SegmentCount, 2, 30 );
		var position = WorldPosition;
		var back = -WorldRotation.Forward;

		for ( var i = 0; i < count; i++ )
		{
			var t = i / (float)(count - 1);
			var radius = i == 0 ? HeadRadius : MathX.Lerp( HeadRadius * 0.95f, TailRadius, MathF.Pow( t, 0.8f ) ) * Range( 0.85f, 1.15f );

			if ( i > 0 ) position += back * (segments[i - 1].Radius + radius) * Spacing;

			var go = new GameObject( true, i == 0 ? "Bloober Head" : $"Bloober Segment {i}" );
			go.NetworkMode = NetworkMode.Never;
			go.Tags.Add( "creature" );
			go.WorldPosition = position;
			go.WorldRotation = WorldRotation;

			var skin = new GameObject( go, true, "Skin" );
			var renderer = skin.AddComponent<ModelRenderer>();
			renderer.Model = Shapes.Sphere;
			renderer.Tint = BodyColor;

			// Bullets find it through a trigger, so players and physics never snag on the snake.
			var collider = go.AddComponent<SphereCollider>();
			collider.IsTrigger = true;
			collider.Radius = radius * 1.1f;

			var part = go.AddComponent<CreaturePart>();
			part.Creature = this;
			part.Index = i;

			var segment = new Segment
			{
				Index = i,
				Go = go,
				Skin = skin,
				Renderer = renderer,
				Collider = collider,
				Radius = radius,
				Health = SegmentHealth,
				Position = position,
			};

			// Dark spots scattered over the skin (kept off the head's face).
			var spots = i == 0 ? 3 : random.Next( 3, 6 );
			for ( var s = 0; s < spots; s++ )
			{
				Vector3 dir;
				do
				{
					dir = new Vector3( Range( -1f, 1f ), Range( -1f, 1f ), Range( -1f, 1f ) );
				} while ( dir.LengthSquared < 0.05f || (i == 0 && dir.Normal.x > 0.2f) );

				var spot = new GameObject( go, true, "Spot" );
				var spotRenderer = spot.AddComponent<ModelRenderer>();
				spotRenderer.Model = Shapes.Sphere;
				spotRenderer.Tint = SpotColor;
				segment.Spots.Add( new Spot { Go = spot, Dir = dir.Normal, Phase = Range( 0f, 6.28f ) } );
			}

			if ( i == 0 ) BuildFace( segment );
			segments.Add( segment );
		}
	}

	/// <summary> Big cartoon eyes and a dark mouth that gapes when it bites. </summary>
	void BuildFace( Segment head )
	{
		var r = head.Radius;
		foreach ( var side in new[] { -1f, 1f } )
		{
			var eye = new GameObject( head.Go, true, "Eye" );
			eye.LocalPosition = new Vector3( r * 0.75f * Stretch, side * r * 0.45f, r * 0.5f );
			eye.LocalScale = Shapes.SphereScale( r * 0.32f );
			var white = eye.AddComponent<ModelRenderer>();
			white.Model = Shapes.Sphere;
			white.Tint = Color.White;

			var pupil = new GameObject( head.Go, true, "Pupil" );
			pupil.LocalPosition = eye.LocalPosition + new Vector3( r * 0.22f, side * r * 0.04f, 0f );
			pupil.LocalScale = Shapes.SphereScale( r * 0.15f );
			var black = pupil.AddComponent<ModelRenderer>();
			black.Model = Shapes.Sphere;
			black.Tint = Color.Black;
		}

		var mouth = new GameObject( head.Go, true, "Mouth" );
		var mouthRenderer = mouth.AddComponent<ModelRenderer>();
		mouthRenderer.Model = Shapes.Sphere;
		mouthRenderer.Tint = new Color( 0.12f, 0.02f, 0.06f );
		head.Mouth = mouth;
	}

	protected override void OnDestroy()
	{
		// Body pieces aren't children of the head, so clean up any still attached.
		foreach ( var segment in segments )
		{
			if ( segment.Go.IsValid() && !segment.Gone ) segment.Go.Destroy();
		}
	}

	// ---- Every frame --------------------------------------------------------

	protected override void OnUpdate()
	{
		if ( IsProxy )
		{
			CatchUp();
		}
		else if ( IsDead )
		{
			// Give the death broadcast a moment to land before the head goes away.
			if ( timeSinceDeath > 1f ) GameObject.Destroy();
			return;
		}
		else
		{
			Think();
		}

		if ( diedLocally ) return;

		var targetPuff = Puffed ? 1f : 0f;
		puff = MathX.Approach( puff, targetPuff, Time.Delta * (Puffed ? 6f : 2.5f) );
		mouthOpen = MathX.Approach( mouthOpen, Biting ? 1f : 0f, Time.Delta * (Biting ? 14f : 5f) );

		UpdateBody();
	}

	/// <summary> Drags each segment along behind the one in front, then poses skin, spots and face. </summary>
	void UpdateBody()
	{
		var dt = MathF.Max( Time.Delta, 0.0001f );
		var puffScale = MathX.Lerp( 1f, PuffSize, Ease( puff ) );

		for ( var i = 0; i < segments.Count; i++ )
		{
			var segment = segments[i];
			if ( segment.Gone ) break;

			Vector3 position;
			Rotation rotation;
			if ( i == 0 )
			{
				position = WorldPosition;
				rotation = WorldRotation;
			}
			else
			{
				var previous = segments[i - 1];
				var offset = segment.Position - previous.Position;
				if ( offset.LengthSquared < 0.01f ) offset = -previous.Go.WorldRotation.Forward;
				var gap = (previous.Radius + segment.Radius) * Spacing * puffScale;
				position = previous.Position + offset.Normal * gap;
				rotation = Rotation.LookAt( previous.Position - position, Vector3.Up );
			}

			segment.Velocity = (position - segment.Position) / dt;
			segment.Position = position;
			segment.Go.WorldPosition = position;
			segment.Go.WorldRotation = rotation;

			PoseSegment( segment, puffScale );
		}
	}

	void PoseSegment( Segment segment, float puffScale )
	{
		var r = segment.Radius * puffScale * (1f - 0.2f * segment.Hurt);
		segment.Skin.LocalScale = new Vector3( Stretch, 1f, 1f ) * Shapes.SphereScale( r );

		// Hit flash, and a darker, sicker colour the more hurt it is.
		var baseColor = Color.Lerp( BodyColor, BodyColor * 0.45f, segment.Hurt );
		var flash = MathX.Clamp( 1f - segment.Flash / 0.1f, 0f, 1f );
		segment.Renderer.Tint = Color.Lerp( baseColor.WithAlpha( 1f ), Color.White * 3f, flash );

		// Spots: flat and breathing in and out; puffed, they push out into spikes.
		var p = Ease( puff );
		var time = Time.Now;
		foreach ( var spot in segment.Spots )
		{
			if ( !spot.Go.IsValid() ) continue;
			var breathe = 0.85f + 0.15f * MathF.Sin( time * 3f + spot.Phase );
			var surface = new Vector3( spot.Dir.x * Stretch, spot.Dir.y, spot.Dir.z ) * r;
			spot.Go.LocalPosition = surface * MathX.Lerp( 0.97f, 1.12f, p );
			spot.Go.LocalRotation = Rotation.LookAt( spot.Dir );
			var thickness = MathX.Lerp( 0.1f, 0.85f, p ) * segment.Radius;
			var width = MathX.Lerp( 0.32f * breathe, 0.13f, p ) * segment.Radius;
			spot.Go.LocalScale = new Vector3( thickness, width, width ) * Shapes.SphereScale( 1f );
		}

		if ( segment.Mouth.IsValid() )
		{
			var open = Ease( mouthOpen );
			segment.Mouth.LocalPosition = new Vector3( r * Stretch * 0.92f, 0f, -r * 0.1f );
			segment.Mouth.LocalScale = new Vector3( 0.25f, 0.62f, MathX.Lerp( 0.12f, 0.6f, open ) ) * Shapes.SphereScale( r );
		}

		// Keep the bullet trigger roughly the skin's size without rebuilding it every frame.
		if ( MathF.Abs( puffScale - segment.ColliderPuff ) > 0.05f && segment.Collider.IsValid() )
		{
			segment.ColliderPuff = puffScale;
			segment.Collider.Radius = segment.Radius * puffScale * 1.1f;
		}
	}

	static float Ease( float t ) => t * t * (3f - 2f * t);

	// ---- Host brain -----------------------------------------------------------

	void Think()
	{
		var dt = Time.Delta;

		if ( nextPuff && !Puffed ) StartPuff();
		if ( Puffed && timeSincePuff > PuffDuration ) Puffed = false;

		var target = PlayerSurvivor.FindNearestAlive( Scene, WorldPosition );
		var aim = target is null ? WorldPosition + heading * 200f : target.WorldPosition + Vector3.Up * AimHeight;
		var toAim = aim - WorldPosition;
		// Shorter snakes (tail shot off) get a bit quicker and angrier.
		var lost = segments.Count( s => s.Gone );
		var speed = MoveSpeed * (1f + 0.05f * lost) * (Puffed ? 0.7f : 1f);

		switch ( mode )
		{
			case Mode.Chase:
			{
				var desired = toAim.Normal;
				// The slither: swing the heading side to side, with a lazy bob up and down.
				var wiggle = MathF.Sin( Time.Now * WiggleRate + wigglePhase );
				var bob = MathF.Sin( Time.Now * WiggleRate * 0.5f + wigglePhase * 1.7f );
				desired = Rotation.FromAxis( Vector3.Up, wiggle * WiggleDegrees ) * desired;
				desired = (desired + Vector3.Up * bob * 0.25f).Normal;

				heading = Vector3.Lerp( heading, desired, MathX.Clamp( TurnRate * dt, 0f, 1f ) ).Normal;
				Move( heading * speed, dt );

				if ( target is not null && toAim.Length < BiteRange && timeSinceBite > BiteCooldown )
				{
					mode = Mode.Lunge;
					modeTime = 0f;
					lungeDirection = toAim.Normal;
					bitThisLunge = false;
					Biting = true;
				}
				break;
			}

			case Mode.Lunge:
			{
				heading = lungeDirection;
				Move( lungeDirection * LungeSpeed, dt );

				if ( !bitThisLunge && target is not null && (aim - WorldPosition).Length < BiteReach + HeadRadius )
				{
					bitThisLunge = true;
					Bite( target );
				}

				if ( modeTime > LungeTime )
				{
					// Peel off to one side and come round again.
					mode = Mode.Retreat;
					modeTime = 0f;
					Biting = false;
					timeSinceBite = 0f;
					var side = Game.Random.Float() < 0.5f ? -1f : 1f;
					heading = (Rotation.FromAxis( Vector3.Up, side * Game.Random.Float( 100f, 150f ) ) * lungeDirection.WithZ( 0 ).Normal + Vector3.Up * 0.35f).Normal;
				}
				break;
			}

			case Mode.Retreat:
			{
				Move( heading * speed * 1.15f, dt );
				if ( modeTime > RetreatTime )
				{
					mode = Mode.Chase;
					modeTime = 0f;
				}
				break;
			}
		}

		if ( !heading.IsNearlyZero() )
			WorldRotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( heading, Vector3.Up ), MathX.Clamp( dt * 10f, 0f, 1f ) );
	}

	/// <summary> Flies the head, steering round walls and staying off the floor and away from its friends. </summary>
	void Move( Vector3 velocity, float dt )
	{
		var position = WorldPosition;
		var direction = velocity.Normal;

		// Wall ahead: slide along it and lift.
		var look = Scene.Trace.Sphere( HeadRadius * 0.8f, position, position + direction * 70f )
			.WithoutTags( "zombie", "zombieblocker", "gib", "player", "viewmodel", "creature" )
			.IgnoreDynamic()
			.Run();
		if ( look.Hit )
		{
			var slide = (direction - look.Normal * Vector3.Dot( direction, look.Normal ) + look.Normal * 0.6f + Vector3.Up * 0.4f).Normal;
			heading = Vector3.Lerp( heading, slide, MathX.Clamp( dt * 8f, 0f, 1f ) ).Normal;
			velocity = slide * velocity.Length;
		}

		// Keep some air underneath.
		var ground = Scene.Trace.Ray( position, position + Vector3.Down * 400f )
			.WithoutTags( "zombie", "zombieblocker", "gib", "player", "viewmodel", "creature" )
			.IgnoreDynamic()
			.Run();
		if ( ground.Hit && position.z - ground.HitPosition.z < MinAltitude + HeadRadius )
			velocity = velocity.WithZ( MathF.Max( velocity.z, 120f ) );

		// Don't stack up inside other Bloobers.
		foreach ( var other in Scene.GetAllComponents<Bloober>() )
		{
			if ( other == this || other.IsDead ) continue;
			var away = position - other.WorldPosition;
			var distance = away.Length;
			if ( distance < 50f && distance > 0.01f )
				velocity += away / distance * (50f - distance) * 6f;
		}

		WorldPosition = position + velocity * dt;
	}

	void Bite( PlayerSurvivor target )
	{
		var hitPosition = target.WorldPosition + Vector3.Up * AimHeight;
		target.Health?.OnDamage( new DamageInfo
		{
			Damage = BiteDamage,
			Attacker = GameObject,
			Position = hitPosition,
			Origin = WorldPosition,
		} );
		target.GetComponent<PlayerAfflictions>()?.Bitten();
	}

	void StartPuff()
	{
		Puffed = true;
		timeSincePuff = 0f;
		nextPuff = PuffDuration + Game.Random.Float( PuffEvery.x, PuffEvery.y );
	}

	// ---- Getting shot -----------------------------------------------------------

	/// <summary> A bullet hit one of the segments on the shooter's machine. Runs on the host. </summary>
	public void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsProxy ) ShotOnHost( index, damage, direction, position, origin, shooter?.GameObject, scoreHit );
		else ApplyShot( index, damage, direction, position, origin, shooter, scoreHit );
	}

	[Rpc.Owner]
	void ShotOnHost( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, GameObject shooter, bool scoreHit )
	{
		ApplyShot( index, damage, direction, position, origin, shooter?.GetComponent<PlayerSurvivor>(), scoreHit );
	}

	void ApplyShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead || segments.Count == 0 ) return;

		// Hit something that's already gone (lag): count it on the nearest segment still attached.
		if ( index < 0 || index >= segments.Count || segments[index].Gone )
		{
			index = segments.Where( s => !s.Gone )
				.OrderBy( s => s.Position.DistanceSquared( position ) )
				.First().Index;
		}

		lastShotDirection = direction;
		lastShotPointBlank = (position - origin).Length < CreatureGore.OverkillRange;
		var segment = segments[index];
		var amount = damage * (Puffed ? PuffDamageScale : 1f);
		var headshot = index == 0;

		var cut = false;
		var hurt = 0f;
		if ( headshot )
		{
			hurt = 1f - MathX.Clamp( (health.Current - amount * HeadMultiplier) / MathF.Max( 1f, health.MaxHealth ), 0f, 1f );
		}
		else
		{
			segment.Health -= amount;
			cut = segment.Health <= 0f;
			hurt = 1f - MathX.Clamp( segment.Health / MathF.Max( 1f, SegmentHealth ), 0f, 1f );
		}

		if ( cut )
		{
			GoneMask |= TailMask( index );
			CutEverywhere( index, position, direction );
		}
		else
		{
			var chip = Game.Random.Float() < (headshot ? ChipChance * 0.6f : ChipChance);
			HitEverywhere( index, position, direction, chip, hurt );
		}

		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo
		{
			Damage = amount * (headshot ? HeadMultiplier : BodyMultiplier),
			Position = position,
			Origin = origin,
			Attacker = shooter?.GameObject,
		} );
		var killed = wasAlive && IsDead;

		if ( !killed && !Puffed && timeSincePuff > 4f && Game.Random.Float() < PuffWhenHurtChance )
			StartPuff();

		if ( wasAlive && shooter.IsValid() )
			shooter.RewardHit( killed, headshot, scoreHit );
	}

	int TailMask( int from )
	{
		var mask = 0;
		for ( var i = from; i < segments.Count && i < 31; i++ ) mask |= 1 << i;
		return mask;
	}

	/// <summary> Goo, a flash, and sometimes a chip of flesh, on every machine. </summary>
	[Rpc.Broadcast( NetFlags.Unreliable )]
	void HitEverywhere( int index, Vector3 position, Vector3 direction, bool chip, float hurt )
	{
		if ( index < 0 || index >= segments.Count ) return;
		var segment = segments[index];
		segment.Flash = 0f;
		segment.Hurt = MathF.Max( segment.Hurt, hurt );
		if ( !segment.Gone )
		{
			CreatureGore.AddWound( segment.Go, position, direction, BodyColor, GooColor, segment.Radius * 0.45f );
			CreatureGore.Jiggle( segment.Go );
		}

		gore?.GooBurst( position, -direction, GooColor, 6, 220f, 0.8f );
		gore?.GooSpray( position, direction, 2, GooColor, 180f, 0.4f, 0.9f );
		if ( chip && !segment.Gone )
			gore?.ThrowBlob( position, -direction + direction * 0.3f, BodyColor, GooColor, segment.Radius * 0.35f );
	}

	[Rpc.Broadcast]
	void CutEverywhere( int index, Vector3 position, Vector3 direction ) => CutLocal( index, direction, quiet: false );

	/// <summary>
	/// The segment at index bursts; everything behind it drops off as loose pieces.
	/// Quiet (catching up on a cut we missed) skips the effects and just removes them.
	/// </summary>
	void CutLocal( int index, Vector3 direction, bool quiet )
	{
		if ( index <= 0 || index >= segments.Count ) return;

		// A raw, oozing end on the snake where it was cut, and a matching cap on the tail piece.
		if ( !quiet && !segments[index].Gone )
		{
			var front = segments[index - 1];
			var cut = segments[index];
			var outward = (cut.Position - front.Position).Normal;
			if ( !front.Gone ) CreatureGore.AddStump( front.Go, front.Position + outward * front.Radius * 0.85f, outward, front.Radius * 0.8f, BodyColor, GooColor );
			if ( index + 1 < segments.Count && !segments[index + 1].Gone )
			{
				var tail = segments[index + 1];
				var back = (cut.Position - tail.Position).Normal;
				CreatureGore.AddStump( tail.Go, tail.Position + back * tail.Radius * 0.85f, back, tail.Radius * 0.8f, BodyColor, GooColor, 2.5f );
			}
		}

		for ( var i = index; i < segments.Count; i++ )
		{
			var segment = segments[i];
			if ( segment.Gone ) continue;
			segment.Gone = true;

			if ( quiet ) { segment.Go.Destroy(); continue; }

			if ( i == index ) Burst( segment, direction, 14, 2 );
			else Detach( segment, direction * Game.Random.Float( 60f, 160f ) );
		}
	}

	/// <summary> Pops a segment in a shower of goo and a couple of flesh blobs. </summary>
	void Burst( Segment segment, Vector3 direction, int drops, int blobs )
	{
		var position = segment.Position;
		gore?.GooBurst( position, direction, GooColor, drops, 320f, 1.3f );
		gore?.GooSpray( position, direction, 4, GooColor, 220f, 0.6f, 1.4f );
		for ( var b = 0; b < blobs; b++ )
			gore?.ThrowBlob( position, direction, BodyColor, GooColor, segment.Radius * Game.Random.Float( 0.35f, 0.5f ) );
		segment.Go.Destroy();
	}

	/// <summary> A segment comes loose and falls as a physics piece, spots and all. </summary>
	void Detach( Segment segment, Vector3 push )
	{
		var go = segment.Go;
		go.Tags.Remove( "creature" );
		go.Components.Get<CreaturePart>()?.Destroy();
		segment.Collider?.Destroy();
		segment.Renderer.Tint = Color.Lerp( BodyColor, BodyColor * 0.45f, segment.Hurt ).WithAlpha( 1f );

		var velocity = segment.Velocity * 0.4f + push + Vector3.Up * Game.Random.Float( 40f, 120f );
		Gib.Attach( go, gore, segment.Radius, 25f, 4f, velocity, GooColor );
	}

	/// <summary> On proxies: apply any cuts or a death we never got the message for. </summary>
	void CatchUp()
	{
		if ( IsDead && !diedLocally ) { DieLocal( Vector3.Forward, quiet: true ); return; }

		for ( var i = 1; i < segments.Count && i < 31; i++ )
		{
			if ( (GoneMask & (1 << i)) != 0 && !segments[i].Gone )
			{
				CutLocal( i, Vector3.Forward, quiet: true );
				break;
			}
		}
	}

	// ---- Death ----------------------------------------------------------------

	void OnDied()
	{
		timeSinceDeath = 0f;
		Killed?.Invoke( this );
		DieEverywhere( lastShotDirection, lastShotPointBlank );
	}

	[Rpc.Broadcast]
	void DieEverywhere( Vector3 direction, bool overkill ) => DieLocal( direction, quiet: false, overkill );

	/// <summary> The head pops; the body tumbles out of the air in pieces, some bursting as they go. </summary>
	void DieLocal( Vector3 direction, bool quiet, bool overkill = false )
	{
		if ( diedLocally ) return;
		diedLocally = true;
		timeSinceDeath = 0f;
		var overkillAt = segments[0].Position;

		if ( overkill && !quiet ) CreatureGore.Overkill( Scene, overkillAt, direction, BodyColor, GooColor );

		foreach ( var segment in segments )
		{
			if ( segment.Gone ) continue;
			segment.Gone = true;

			if ( quiet ) { segment.Go.Destroy(); continue; }

			if ( segment.Index == 0 ) Burst( segment, direction, 22, 3 );
			// An overkill bursts most of the body; a normal kill drops it in pieces.
			else if ( Game.Random.Float() < (overkill ? 0.7f : 0.25f) ) Burst( segment, direction, 8, 1 );
			else Detach( segment, (direction + Vector3.Random * 0.8f).Normal * Game.Random.Float( 60f, 180f ) );
		}
	}
}
