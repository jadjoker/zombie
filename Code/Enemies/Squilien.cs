/// <summary>
/// Squilien: a floating alien jellyfish. No gravity: it hangs in the air and swims in pulses, keeping its
/// distance and circling, and lobs balls of ink. A couple of ink hits close together smear ink across
/// your screen for a few seconds.
///
/// Chunking: every tentacle is a chain of pieces. Shoot a piece and the tentacle snaps there, the rest of
/// it dropping as loose bits; lose enough tentacles and it swims weakly. The bell is the weak spot: its
/// health is the whole creature's. On death the bell bursts in ink and goo and the tentacles rain down.
///
/// Networking: like Bloober, the host flies the bell (the networked root) and every machine builds and
/// animates the tentacles locally. Pulses are counted in a synced number, throws and cuts are broadcast,
/// and a synced mask lets late joiners catch up.
/// </summary>
public sealed class Squilien : Component, ICreature
{
	[Property, Group( "Body" )] public float BellRadius { get; set; } = 22f;
	[Property, Group( "Body" )] public int Tentacles { get; set; } = 6;
	[Property, Group( "Body" )] public int TentacleSegments { get; set; } = 4;
	[Property, Group( "Body" )] public float SegmentLength { get; set; } = 10f;
	[Property, Group( "Body" )] public float TentacleRadius { get; set; } = 3.4f;

	[Property, Group( "Movement" )] public float PreferredDistance { get; set; } = 420f;
	/// <summary> Height it likes to hang at, above the player's feet. </summary>
	[Property, Group( "Movement" )] public float HoverHeight { get; set; } = 150f;
	[Property, Group( "Movement" )] public float PulseInterval { get; set; } = 1.1f;
	[Property, Group( "Movement" )] public float PulseThrust { get; set; } = 240f;
	[Property, Group( "Movement" )] public float Drag { get; set; } = 1.4f;
	/// <summary> Degrees per second it circles round its target. </summary>
	[Property, Group( "Movement" )] public float OrbitSpeed { get; set; } = 14f;
	[Property, Group( "Movement" )] public float MinAltitude { get; set; } = 60f;

	[Property, Group( "Ink" )] public float ThrowCooldown { get; set; } = 2.4f;
	[Property, Group( "Ink" )] public float ThrowRange { get; set; } = 1200f;
	[Property, Group( "Ink" )] public float InkSpeed { get; set; } = 750f;
	[Property, Group( "Ink" )] public float InkGravity { get; set; } = 300f;
	[Property, Group( "Ink" )] public float InkDamage { get; set; } = 8f;

	[Property, Group( "Damage" )] public float TentacleHealth { get; set; } = 12f;
	/// <summary> Bell hits hurt the whole creature this much. </summary>
	[Property, Group( "Damage" )] public float BellMultiplier { get; set; } = 1.1f;
	/// <summary> Tentacle hits hurt the whole creature this much (they mostly snap tentacles). </summary>
	[Property, Group( "Damage" )] public float TentacleMultiplier { get; set; } = 0.12f;
	[Property, Group( "Damage" )] public float ChipChance { get; set; } = 0.35f;

	[Sync] public float Hue { get; set; } = -1f;
	/// <summary> Bumped by the host on every swim stroke, so everyone plays the bell squeeze. </summary>
	[Sync] public int PulseCount { get; set; }
	[Sync] public int GoneMask { get; set; }

	public bool IsDead => health?.IsDead ?? false;
	public Color BodyColor { get; private set; }
	public Color SpotColor { get; private set; }
	public Color GooColor { get; private set; }
	public Color InkColor { get; private set; }

	public event System.Action<Squilien> Killed;

	sealed class Piece
	{
		public int Index;
		public GameObject Go;
		public ModelRenderer Renderer;
		public float Radius;
		public float Health;
		public float Hurt;
		public bool Gone;
		public Vector3 Position;
		public Vector3 Velocity;
		public TimeSince Flash = 99f;
	}

	sealed class Tentacle
	{
		public float Angle;
		public float Phase;
		public readonly List<Piece> Pieces = new();
	}

	Piece bell;
	GameObject bellSkin;
	readonly List<Tentacle> tentacles = new();
	readonly List<Piece> all = new();
	Health health;
	GoreSystem gore;

	Vector3 velocity;
	TimeUntil nextPulse;
	TimeUntil nextThrow;
	float orbitSign = 1f;
	int seenPulses;
	float squeeze;
	bool diedLocally;
	TimeSince timeSinceDeath;
	Vector3 lastShotDirection = Vector3.Forward;
	TimeSince bleedTimer;
	bool lastShotPointBlank;

	// ---- Setup ------------------------------------------------------------

	protected override void OnStart()
	{
		health = GetComponent<Health>();
		gore = GoreSystem.Get( Scene );

		if ( !IsProxy && Hue < 0f ) Hue = Game.Random.Float();
		var hue = Hue < 0f ? 0.5f : Hue;
		BodyColor = Shapes.Hsv( hue, 0.65f, 1f );
		SpotColor = Shapes.Hsv( hue + 0.08f, 0.7f, 0.55f );
		GooColor = Shapes.Hsv( hue + 0.35f, 0.95f, 1f );
		InkColor = Shapes.Hsv( 0.76f, 0.75f, 0.28f );

		nextPulse = Game.Random.Float( 0.1f, PulseInterval );
		nextThrow = Game.Random.Float( 1f, ThrowCooldown + 1f );
		orbitSign = Game.Random.Float() < 0.5f ? -1f : 1f;
		seenPulses = PulseCount;

		if ( health.IsValid() && !IsProxy ) health.Died += OnDied;
		Build( hue );
	}

	void Build( float hue )
	{
		var random = new System.Random( (int)(hue * 100000f) + 7 );
		float Range( float a, float b ) => a + (float)random.NextDouble() * (b - a);

		// The bell: a squashed dome with spots on top and two big dark eyes on the front.
		var bellGo = NewPart( "Squilien Bell", BellRadius, 0 );
		bell = new Piece { Index = 0, Go = bellGo, Radius = BellRadius, Position = WorldPosition };
		bellSkin = bellGo.Children.First( c => c.Name == "Skin" );
		bell.Renderer = bellSkin.Components.Get<ModelRenderer>();
		all.Add( bell );

		var spots = random.Next( 5, 8 );
		for ( var s = 0; s < spots; s++ )
		{
			var yaw = Range( 0f, 360f );
			var up = Range( 0.25f, 0.9f );
			var dir = (Rotation.FromYaw( yaw ).Forward * MathF.Sqrt( 1f - up * up ) + Vector3.Up * up).Normal;
			if ( dir.x > 0.55f && dir.z < 0.5f ) continue; // keep the face clear
			AddDot( bellGo, dir, BellRadius, BellRadius * Range( 0.1f, 0.18f ), SpotColor );
		}

		foreach ( var side in new[] { -1f, 1f } )
		{
			var dir = new Vector3( 0.82f, side * 0.38f, -0.05f ).Normal;
			var eye = AddDot( bellGo, dir, BellRadius, BellRadius * 0.3f, new Color( 0.04f, 0.02f, 0.06f ) );
			eye.LocalScale = new Vector3( 0.12f, 0.24f, 0.36f ) * Shapes.SphereScale( BellRadius );
			var glint = AddDot( bellGo, (dir + new Vector3( 0.05f, 0f, 0.12f )).Normal, BellRadius * 1.04f, BellRadius * 0.06f, Color.White );
			glint.Name = "Glint";
		}

		// A frilly skirt round the rim of the bell, and a soft glow-spot crown on top.
		var frill = Color.Lerp( BodyColor, Color.White, 0.3f );
		for ( var k = 0; k < 12; k++ )
		{
			var a = k * MathF.PI * 2f / 12f;
			var bump = new GameObject( bellGo, true, "Frill" );
			bump.LocalPosition = new Vector3( MathF.Cos( a ) * BellRadius * 0.86f, MathF.Sin( a ) * BellRadius * 0.86f, -BellRadius * 0.3f );
			bump.LocalScale = new Vector3( 0.32f, 0.32f, 0.2f ) * Shapes.SphereScale( BellRadius );
			var fr = bump.AddComponent<ModelRenderer>();
			fr.Model = Shapes.Sphere;
			fr.Tint = frill;
		}
		var crownGlow = new GameObject( bellGo, true, "Crown" );
		crownGlow.LocalPosition = new Vector3( -BellRadius * 0.15f, 0, BellRadius * 0.62f );
		crownGlow.LocalScale = new Vector3( 0.45f, 0.45f, 0.18f ) * Shapes.SphereScale( BellRadius );
		var cg = crownGlow.AddComponent<ModelRenderer>();
		cg.Model = Shapes.Sphere;
		cg.Tint = Color.Lerp( GooColor, Color.White, 0.35f ) * 1.4f;

		// Tentacles hang in a ring under the bell.
		var count = Math.Clamp( Tentacles, 1, 7 );
		var segments = Math.Clamp( TentacleSegments, 1, 4 );
		for ( var t = 0; t < count; t++ )
		{
			var tentacle = new Tentacle { Angle = t * 360f / count + Range( -12f, 12f ), Phase = Range( 0f, 6.28f ) };
			var anchor = Anchor( tentacle );
			for ( var s = 0; s < segments; s++ )
			{
				var index = 1 + t * segments + s;
				var radius = TentacleRadius * MathX.Lerp( 1f, 0.55f, s / (float)Math.Max( 1, segments - 1 ) );
				var go = NewPart( $"Squilien Tentacle {t}.{s}", radius, index );
				var piece = new Piece
				{
					Index = index,
					Go = go,
					Renderer = go.Children.First( c => c.Name == "Skin" ).Components.Get<ModelRenderer>(),
					Radius = radius,
					Health = TentacleHealth,
					Position = anchor + Vector3.Down * SegmentLength * (s + 1),
				};
				go.WorldPosition = piece.Position;
				tentacle.Pieces.Add( piece );
				all.Add( piece );
			}
			tentacles.Add( tentacle );
		}
	}

	GameObject NewPart( string name, float radius, int index )
	{
		var go = new GameObject( true, name );
		go.NetworkMode = NetworkMode.Never;
		go.Tags.Add( "creature" );
		go.WorldPosition = WorldPosition;

		var skin = new GameObject( go, true, "Skin" );
		var renderer = skin.AddComponent<ModelRenderer>();
		renderer.Model = Shapes.Sphere;
		renderer.Tint = BodyColor;

		var collider = go.AddComponent<SphereCollider>();
		collider.IsTrigger = true;
		collider.Radius = index == 0 ? radius * 1.1f : radius * 1.3f; // thin tentacles get a slightly fatter hit area

		var part = go.AddComponent<CreaturePart>();
		part.Creature = this;
		part.Index = index;
		return go;
	}

	GameObject AddDot( GameObject parent, Vector3 dir, float distance, float size, Color color )
	{
		var dot = new GameObject( parent, true, "Spot" );
		dot.LocalPosition = new Vector3( dir.x, dir.y, dir.z * 0.75f ) * distance * 0.97f;
		dot.LocalRotation = Rotation.LookAt( dir );
		dot.LocalScale = new Vector3( 0.25f, 1f, 1f ) * Shapes.SphereScale( size );
		var r = dot.AddComponent<ModelRenderer>();
		r.Model = Shapes.Sphere;
		r.Tint = color;
		return dot;
	}

	/// <summary> Where a tentacle hangs from, on the underside of the bell. </summary>
	Vector3 Anchor( Tentacle tentacle )
	{
		var local = Rotation.FromYaw( tentacle.Angle ).Forward * BellRadius * 0.55f + Vector3.Down * BellRadius * 0.4f;
		return WorldTransform.PointToWorld( local );
	}

	protected override void OnDestroy()
	{
		foreach ( var piece in all )
		{
			if ( piece.Go.IsValid() && !piece.Gone ) piece.Go.Destroy();
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
			if ( timeSinceDeath > 1f ) GameObject.Destroy();
			return;
		}
		else
		{
			Think();
		}

		if ( diedLocally ) return;

		if ( health.IsValid() && !bell.Gone )
			CreatureGore.BleedTrail( Scene, bell.Position + Vector3.Down * BellRadius * 0.4f, GooColor, health.Fraction, ref bleedTimer );

		if ( PulseCount != seenPulses )
		{
			seenPulses = PulseCount;
			squeeze = 1f;
		}
		squeeze = MathX.Approach( squeeze, 0f, Time.Delta * 2.2f );

		PoseBody();
	}

	void PoseBody()
	{
		var dt = MathF.Max( Time.Delta, 0.0001f );

		// Bell: squeezes in and stretches up on each stroke, then relaxes.
		var s = squeeze * squeeze;
		bell.Position = WorldPosition;
		bell.Go.WorldPosition = WorldPosition;
		bell.Go.WorldRotation = WorldRotation;
		bellSkin.LocalScale = new Vector3( 1f - 0.2f * s, 1f - 0.2f * s, 0.75f + 0.15f * s ) * Shapes.SphereScale( BellRadius * (1f - 0.12f * bell.Hurt) );
		Tint( bell );

		// Tentacles trail behind as the bell moves, droop under a little gravity and sway.
		var time = Time.Now;
		foreach ( var tentacle in tentacles )
		{
			var previous = Anchor( tentacle );
			for ( var i = 0; i < tentacle.Pieces.Count; i++ )
			{
				var piece = tentacle.Pieces[i];
				if ( piece.Gone ) break;

				var sway = new Vector3(
					MathF.Sin( time * 1.7f + tentacle.Phase + i * 0.8f ),
					MathF.Cos( time * 1.3f + tentacle.Phase * 1.3f + i * 0.7f ), 0f ) * (6f + i * 4f);
				var wanted = piece.Position + (Vector3.Down * 60f + sway) * dt + Vector3.Up * squeeze * 30f * dt;
				var offset = wanted - previous;
				if ( offset.LengthSquared < 0.01f ) offset = Vector3.Down;
				var position = previous + offset.Normal * SegmentLength;

				piece.Velocity = (position - piece.Position) / dt;
				piece.Position = position;
				piece.Go.WorldPosition = (previous + position) * 0.5f;
				piece.Go.WorldRotation = Rotation.LookAt( position - previous, Vector3.Up );
				var skin = piece.Renderer.GameObject;
				skin.LocalScale = new Vector3( SegmentLength * 0.62f / piece.Radius, 1f, 1f ) * Shapes.SphereScale( piece.Radius * (1f - 0.25f * piece.Hurt) );
				Tint( piece );

				previous = position;
			}
		}
	}

	void Tint( Piece piece )
	{
		var baseColor = Color.Lerp( BodyColor, BodyColor * 0.45f, piece.Hurt ).WithAlpha( 1f );
		var flash = MathX.Clamp( 1f - piece.Flash / 0.1f, 0f, 1f );
		piece.Renderer.Tint = Color.Lerp( baseColor, Color.White * 3f, flash );
	}

	// ---- Host brain -----------------------------------------------------------

	int TentaclesLeft => tentacles.Count( t => !t.Pieces[0].Gone );

	void Think()
	{
		var dt = Time.Delta;
		var target = PlayerSurvivor.FindNearestAlive( Scene, WorldPosition );
		// Fewer tentacles, weaker strokes.
		var strength = MathX.Lerp( 0.45f, 1f, TentaclesLeft / (float)Math.Max( 1, tentacles.Count ) );

		if ( nextPulse )
		{
			nextPulse = PulseInterval * Game.Random.Float( 0.85f, 1.15f );
			PulseCount++;

			var goal = WorldPosition + Vector3.Up * 30f;
			if ( target is not null )
			{
				// A spot on a circle round the target, at its preferred range and height.
				var flat = (WorldPosition - target.WorldPosition).WithZ( 0 );
				if ( flat.IsNearlyZero() ) flat = Vector3.Forward;
				var around = Rotation.FromYaw( orbitSign * OrbitSpeed * PulseInterval * 2f ) * flat.Normal;
				goal = target.WorldPosition + around * PreferredDistance + Vector3.Up * HoverHeight;
			}
			var direction = (goal - WorldPosition).Normal;
			velocity += direction * PulseThrust * strength;
		}

		// Between strokes it coasts to a stop and very slowly sinks.
		velocity *= MathF.Exp( -Drag * dt );
		velocity += Vector3.Down * 12f * dt;
		Move( dt );

		if ( target is not null )
		{
			var face = (target.WorldPosition - WorldPosition).WithZ( 0 );
			var tilt = MathX.Clamp( velocity.Length / 300f, 0f, 1f ) * 15f;
			if ( !face.IsNearlyZero() )
				WorldRotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( face, Vector3.Up ) * Rotation.FromPitch( tilt ), MathX.Clamp( dt * 3f, 0f, 1f ) );

			if ( nextThrow ) TryThrow( target );
		}
	}

	void Move( float dt )
	{
		var position = WorldPosition;
		var step = velocity * dt;

		if ( !step.IsNearlyZero() )
		{
			var tr = Scene.Trace.Sphere( BellRadius * 0.8f, position, position + step + step.Normal * 20f )
				.WithoutTags( "zombie", "zombieblocker", "gib", "player", "viewmodel", "creature" )
				.IgnoreDynamic()
				.Run();
			if ( tr.Hit )
			{
				// Bounce softly off walls.
				velocity = velocity - tr.Normal * Vector3.Dot( velocity, tr.Normal ) * 1.6f;
				step = velocity * dt;
			}
		}

		var ground = Scene.Trace.Ray( position, position + Vector3.Down * 500f )
			.WithoutTags( "zombie", "zombieblocker", "gib", "player", "viewmodel", "creature" )
			.IgnoreDynamic()
			.Run();
		if ( ground.Hit && position.z - ground.HitPosition.z < MinAltitude )
			velocity = velocity.WithZ( MathF.Max( velocity.z, 80f ) );

		foreach ( var other in Scene.GetAllComponents<Squilien>() )
		{
			if ( other == this || other.IsDead ) continue;
			var away = position - other.WorldPosition;
			var distance = away.Length;
			if ( distance < 90f && distance > 0.01f )
				velocity += away / distance * (90f - distance) * 4f * dt;
		}

		WorldPosition = position + step;
	}

	void TryThrow( PlayerSurvivor target )
	{
		var origin = WorldPosition + Vector3.Down * BellRadius * 0.3f + WorldRotation.Forward * BellRadius * 0.8f;
		var aim = target.WorldPosition + Vector3.Up * 45f;
		var distance = (aim - origin).Length;
		if ( distance > ThrowRange ) { nextThrow = 0.5f; return; }

		// Needs a clear line to the player.
		var sight = Scene.Trace.Ray( origin, aim )
			.WithoutTags( "zombie", "zombieblocker", "gib", "player", "viewmodel", "creature" )
			.IgnoreDynamic()
			.Run();
		if ( sight.Hit ) { nextThrow = 0.4f; return; }

		// Lead the target a bit, and lob it up to make up for the drop.
		var flight = distance / InkSpeed;
		var lead = target.Controller.IsValid() ? target.Controller.Velocity.WithZ( 0 ) * flight * 0.6f : Vector3.Zero;
		var launch = (aim + lead - origin).Normal * InkSpeed + Vector3.Up * 0.5f * InkGravity * flight;

		nextThrow = ThrowCooldown * Game.Random.Float( 0.8f, 1.2f );
		PulseCount++;
		ThrowEverywhere( origin, launch );
	}

	/// <summary> Everyone sees the ball fly; only the host's copy can hurt anyone. </summary>
	[Rpc.Broadcast]
	void ThrowEverywhere( Vector3 origin, Vector3 launch )
	{
		InkBall.Spawn( Scene, origin, launch, InkGravity, InkColor, authority: !IsProxy, InkDamage, GameObject );
		gore?.GooBurst( origin, launch, InkColor, 4, 120f, 0.8f );
	}

	// ---- Getting shot -----------------------------------------------------------

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
		if ( IsDead ) return;

		var piece = Find( index );
		if ( piece is null || piece.Gone ) piece = bell;
		lastShotDirection = direction;
		lastShotPointBlank = (position - origin).Length < CreatureGore.OverkillRange;

		var isBell = piece == bell;
		var cut = false;
		float hurt;
		if ( isBell )
		{
			hurt = 1f - MathX.Clamp( (health.Current - damage * BellMultiplier) / MathF.Max( 1f, health.MaxHealth ), 0f, 1f );
		}
		else
		{
			piece.Health -= damage;
			cut = piece.Health <= 0f;
			hurt = 1f - MathX.Clamp( piece.Health / MathF.Max( 1f, TentacleHealth ), 0f, 1f );
		}

		if ( cut )
		{
			GoneMask |= CutMask( piece.Index );
			CutEverywhere( piece.Index, position, direction );
		}
		else
		{
			HitEverywhere( piece.Index, position, direction, Game.Random.Float() < ChipChance, hurt );
		}

		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo
		{
			Damage = damage * (isBell ? BellMultiplier : TentacleMultiplier),
			Position = position,
			Origin = origin,
			Attacker = shooter?.GameObject,
		} );
		var killed = wasAlive && IsDead;

		if ( wasAlive && shooter.IsValid() )
			shooter.RewardHit( killed, isBell, scoreHit );
	}

	Piece Find( int index ) => index >= 0 && index < all.Count ? all.FirstOrDefault( p => p.Index == index ) : null;

	/// <summary> This tentacle piece and every piece below it on the same tentacle. </summary>
	int CutMask( int index )
	{
		var mask = 0;
		foreach ( var (tentacle, from) in Below( index ) )
		{
			for ( var i = from; i < tentacle.Pieces.Count; i++ )
				mask |= 1 << tentacle.Pieces[i].Index;
		}
		return mask;
	}

	IEnumerable<(Tentacle Tentacle, int From)> Below( int index )
	{
		foreach ( var tentacle in tentacles )
		{
			var at = tentacle.Pieces.FindIndex( p => p.Index == index );
			if ( at >= 0 ) yield return (tentacle, at);
		}
	}

	[Rpc.Broadcast( NetFlags.Unreliable )]
	void HitEverywhere( int index, Vector3 position, Vector3 direction, bool chip, float hurt )
	{
		var piece = Find( index );
		if ( piece is null ) return;
		piece.Flash = 0f;
		piece.Hurt = MathF.Max( piece.Hurt, hurt );
		if ( !piece.Gone )
		{
			CreatureGore.AddWound( piece.Go, position, direction, BodyColor, GooColor, piece == bell ? BellRadius * 0.3f : piece.Radius * 1.1f );
			CreatureGore.Jiggle( piece.Go, piece == bell ? 1.2f : 0.8f );
		}

		gore?.GooBurst( position, -direction, GooColor, 6, 220f, 0.8f );
		gore?.GooSpray( position, direction, 2, GooColor, 180f, 0.4f, 0.9f );
		if ( chip && !piece.Gone )
			gore?.ThrowBlob( position, -direction, BodyColor, GooColor, MathF.Max( 2.5f, piece.Radius * (piece == bell ? 0.25f : 0.8f) ) );
	}

	[Rpc.Broadcast]
	void CutEverywhere( int index, Vector3 position, Vector3 direction ) => CutLocal( index, direction, quiet: false );

	/// <summary> The hit piece pops; the rest of that tentacle falls away. </summary>
	void CutLocal( int index, Vector3 direction, bool quiet )
	{
		foreach ( var (tentacle, from) in Below( index ).ToList() )
		{
			// An oozing stub where the tentacle snapped: on the piece above, or the bell itself.
			if ( !quiet && !tentacle.Pieces[from].Gone )
			{
				var cut = tentacle.Pieces[from];
				var above = from > 0 ? tentacle.Pieces[from - 1] : null;
				var joint = above is not null ? above.Position : Anchor( tentacle );
				var outward = (cut.Position - joint).Normal;
				if ( above is not null && !above.Gone ) CreatureGore.AddStump( above.Go, joint, outward, above.Radius * 1.2f, BodyColor, GooColor, 4f );
				else if ( above is null && !bell.Gone ) CreatureGore.AddStump( bell.Go, joint, outward, TentacleRadius * 1.3f, BodyColor, GooColor, 4f );
			}

			for ( var i = from; i < tentacle.Pieces.Count; i++ )
			{
				var piece = tentacle.Pieces[i];
				if ( piece.Gone ) continue;
				piece.Gone = true;

				if ( quiet ) { piece.Go.Destroy(); continue; }
				if ( i == from ) Burst( piece, direction, 8, 0 );
				else Detach( piece, direction * Game.Random.Float( 30f, 90f ) );
			}
		}
	}

	void Burst( Piece piece, Vector3 direction, int drops, int blobs )
	{
		gore?.GooBurst( piece.Position, direction, GooColor, drops, 280f, 1.1f );
		gore?.GooSpray( piece.Position, direction, 3, GooColor, 220f, 0.5f, 1.2f );
		for ( var b = 0; b < blobs; b++ )
			gore?.ThrowBlob( piece.Position, direction, BodyColor, GooColor, piece.Radius * Game.Random.Float( 0.3f, 0.45f ) );
		piece.Go.Destroy();
	}

	void Detach( Piece piece, Vector3 push )
	{
		var go = piece.Go;
		go.Tags.Remove( "creature" );
		go.Components.Get<CreaturePart>()?.Destroy();
		go.Components.Get<SphereCollider>()?.Destroy();
		piece.Renderer.Tint = Color.Lerp( BodyColor, BodyColor * 0.45f, piece.Hurt ).WithAlpha( 1f );
		Gib.Attach( go, gore, MathF.Max( 2f, piece.Radius ), 12f, 1.5f, piece.Velocity * 0.4f + push, GooColor );
		CreatureGore.Twitch( go, Game.Random.Float( 2f, 4f ), 0.7f );
	}

	void CatchUp()
	{
		if ( IsDead && !diedLocally ) { DieLocal( Vector3.Forward, quiet: true ); return; }

		foreach ( var piece in all )
		{
			if ( piece == bell || piece.Gone || piece.Index >= 31 ) continue;
			if ( (GoneMask & (1 << piece.Index)) != 0 )
			{
				CutLocal( piece.Index, Vector3.Forward, quiet: true );
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

	/// <summary> The bell bursts in a cloud of ink and goo; the tentacles fall. </summary>
	void DieLocal( Vector3 direction, bool quiet, bool overkill = false )
	{
		if ( diedLocally ) return;
		diedLocally = true;
		timeSinceDeath = 0f;
		var overkillAt = bell.Position;

		if ( overkill && !quiet ) CreatureGore.Overkill( Scene, overkillAt, direction, BodyColor, GooColor, 1.2f );

		foreach ( var piece in all )
		{
			if ( piece.Gone ) continue;
			piece.Gone = true;
			if ( quiet ) { piece.Go.Destroy(); continue; }
			if ( overkill && piece != bell && Game.Random.Float() < 0.5f ) { Burst( piece, direction, 4, 0 ); continue; }

			if ( piece == bell )
			{
				gore?.GooBurst( piece.Position, direction, InkColor, 14, 260f, 1.6f );
				gore?.GooSpray( piece.Position, Vector3.Down, 5, InkColor, 260f, 0.8f, 1.6f );
				Burst( piece, direction, 18, 3 );
			}
			else
			{
				Detach( piece, (direction + Vector3.Random).Normal * Game.Random.Float( 40f, 140f ) );
			}
		}
	}
}
