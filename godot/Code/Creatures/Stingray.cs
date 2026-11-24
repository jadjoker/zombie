using Godot;

/// <summary>
/// The sting ray (user's design; the wreck site and Atlantis, from the second stage, at most <see cref="WaveDirector.MaxStingrays"/>
/// alive): an alien ray that glides low over the sea bed, sand camouflaged, sneaking up on the player (it freezes flat
/// when looked at from afar and comes round behind). Up close it rears up and zaps: a stun of <see cref="StunTime"/> as
/// the player is electrocuted. From further off it flaps up above the player, then nose dives at them spinning inside a
/// whirlpool, taking half damage while it dives. Its eyes are the weak spots.
///
/// The ground (user's ask: some mobs glitch into it): its height comes from the highest ground under its nose, wing tips,
/// tail and the spot it is heading to (rays and the worked out terrain height, whichever is higher), it tilts to the
/// slope, and it is never let below that ground; walls are felt ahead with a sphere and slid along.
/// </summary>
public partial class Stingray : Creature
{
	public static readonly List<Stingray> All = new();

	public float Speed = 3.6f;              // m/s gliding
	public float CruiseHeight = 0.34f;      // body centre over the highest ground under it
	public const float MinClear = 0.14f;    // never lower than this over the ground (centre)
	public float ZapRange = 2.6f;           // flat distance it starts a zap from
	public float ZapReach = 3.1f;           // the zap lands on a chest this close to it
	public float ZapDamage = 14f;
	public float ZapCooldown = 3.6f;
	public const float ZapWindup = 0.55f;
	public const float StunTime = 0.6f;     // user's number
	public float DiveMin = 4.5f, DiveMax = 12f;
	public float DiveHeight = 6.5f;         // how high over the player's floor it climbs
	public float DiveSpeed = 13f;
	public float DiveDamage = 26f;
	public float DiveCooldown = 8.5f;
	public float DiveHitRadius = 1.25f;
	public const float ClimbTime = 1.8f, AimTime = 0.45f;
	public const float DiveDamageScale = 0.5f;   // user's ask: half damage while nose diving
	public float EyeMultiplier = 2.2f;

	/// <summary> Tests: zaps and dives that landed, lowest clearance over the ground seen, the deepest dive. </summary>
	public static int ZapHits, DiveHits, Dives;
	public static float LowestClearance = 99f;
	public static bool TrackClearance;   // tests: measure it every frame (five rays a frame)

	enum Act { Prowl, Zap, Climb, Aim, Dive, Recover }
	Act act = Act.Prowl;
	TimeSince sinceAct = 99f, sinceZap = 99f, sinceDive = 99f, sinceHit = 99f, sinceGround = 99f, sincePuff;
	bool struck, watched;
	float phase;
	Vector3 velocity, diveDir;
	float ySpeed;

	// The ground under it: the highest of its samples, and the slope (pitch, roll) it tilts to.
	float groundMax, groundPitch, groundRoll;
	float rayGround = float.MinValue;

	public static Stingray Spawn( Node parent, Vector3 position, float maxHealth ) =>
		Place( new Stingray { Name = "Stingray" }, parent, position, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	protected override void Setup()
	{
		BeginSetup( 370f );
		GoreSetup();
		phase = Rng.Float( 0f, MathF.Tau );
		sinceZap = Rng.Float( 0f, ZapCooldown );
		sinceDive = Rng.Float( 2f, DiveCooldown );
		Build();
		lastPos = GlobalPosition;
		lastYaw = GlobalBasis.GetEuler().Y;
		SampleGround();
		// Spawned in the air or the sand: settle onto the ground at once.
		var y = MathF.Max( GlobalPosition.Y, groundMax + CruiseHeight );
		GlobalPosition = GlobalPosition with { Y = MathF.Min( y, groundMax + CruiseHeight + 1.5f ) };
	}

	/// <summary> Whether it is nose diving (half damage). </summary>
	public bool Diving => act is Act.Aim or Act.Dive;
	public override float AreaDamageScale => Diving ? DiveDamageScale : 1f;
	public bool Charging => act == Act.Zap && sinceAct < ZapWindup;
	public float Charge => charge;
	public bool Watched => watched;
	public float GroundUnder => groundMax;
	/// <summary>
	/// Tests: the lowest point of the disc (its belly or a wing tip, with the most the wings dip as they wave) over the
	/// ground under it.
	/// </summary>
	public float Clearance
	{
		get
		{
			var lowest = float.MaxValue;
			var dip = 0.11f * 1.6f + 0.16f * MathF.Abs( bankV ) + 0.34f * droop;
			foreach ( var (p, d) in new[] { (new Vector3( 0f, -0.06f, 0f ), 0f), (new Vector3( -HalfSpan, 0f, 0.12f ), dip), (new Vector3( HalfSpan, 0f, 0.12f ), dip), (new Vector3( 0f, 0f, -HalfLength ), 0f), (new Vector3( 0f, 0f, HalfLength ), 0f) } )
			{
				var w = body.GlobalTransform * p;
				lowest = MathF.Min( lowest, w.Y - d * ModelScale - Ground( w ) );
			}
			return lowest;
		}
	}

	// ---- The ground ----

	/// <summary> The ground under a point: a ray down (the wreck's decks, rocks) or the worked out terrain, whichever is higher. </summary>
	float Ground( Vector3 at, bool ray = true )
	{
		var g = Sculpt.GroundAt( at );
		if ( ray ) g = MathF.Max( g, Sculpt.FloorBelow( this, at + Vector3.Up * 0.6f, 30f ) );
		return g == float.MinValue ? at.Y - 30f : g;
	}

	/// <summary>
	/// Looks at the ground under its nose, wing tips, tail and where it is heading. Each point must clear its own ground
	/// where it really is (tilted, a wing tip dipping as it waves), so the highest need sets how low the middle may go;
	/// the slope sets its tilt. Rays every tenth of a second; the worked out terrain every frame.
	/// </summary>
	void SampleGround()
	{
		var p = GlobalPosition;
		var pose = body is not null && IsInstanceValid( body ) ? body.GlobalTransform : GlobalTransform.ScaledLocal( Vector3.One * ModelScale );
		var dip = (0.11f * 1.6f + 0.16f * MathF.Abs( bankV ) + 0.34f * droop + 0.42f * MathF.Abs( stroke )) * ModelScale;
		var rays = sinceGround > 0.1f;
		if ( rays ) sinceGround = 0f;
		var need = float.MinValue;
		float Need( Vector3 local, float extra )
		{
			var w = pose * local;
			var g = Ground( w with { Y = MathF.Max( w.Y, p.Y ) }, rays );
			// The middle has to be this high for this point to clear its ground.
			need = MathF.Max( need, g - (w.Y - pose.Origin.Y) + extra );
			return g;
		}
		var hN = Need( new Vector3( 0f, 0f, -HalfLength ), 0f );
		var hT = Need( new Vector3( 0f, 0f, HalfLength ), 0f );
		var hL = Need( new Vector3( -HalfSpan, 0f, 0.3f ), dip );
		var hR = Need( new Vector3( HalfSpan, 0f, 0.3f ), dip );
		Need( new Vector3( 0f, -0.06f, 0f ), 0f );
		need = MathF.Max( need, Ground( p + velocity.Flat() * 0.45f, rays ) );
		if ( rays ) rayGround = need;
		// Between rays: the analytic heights, never below what the last rays saw near here.
		groundMax = rays ? need : MathF.Max( need, rayGround - 0.05f );
		var s = ModelScale;
		groundPitch = MathX.Clamp( MathF.Atan2( hN - hT, 2f * HalfLength * s ), -0.45f, 0.45f );
		groundRoll = MathX.Clamp( MathF.Atan2( hR - hL, 2f * HalfSpan * s ), -0.45f, 0.45f );
	}

	// ---- Every frame ----

	public override void _Process( double delta )
	{
		if ( health is null || body is null ) return;
		var dt = MathF.Min( (float)delta, 0.1f );
		SampleGround();
		if ( !IsDead && !Frozen ) Think( dt );
		Pose( dt );
		GoreFrame( dt );
		if ( TrackClearance && !IsDead && !Puppet ) LowestClearance = MathF.Min( LowestClearance, Clearance );
	}

	void Think( float dt )
	{
		var prey = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var pos = GlobalPosition;
		var wish = Vector3.Zero;
		var wantY = groundMax + CruiseHeight;
		if ( prey is null ) { watched = false; if ( act != Act.Prowl ) End(); }
		else
		{
			var feet = prey.WorldPosition;
			var chest = feet + Vector3.Up * 1.1f;
			var flat = (feet - pos).Flat();
			var dist = flat.Length();
			var dir = flat.SafeNormal();
			var a = (float)sinceAct;
			switch ( act )
			{
				case Act.Prowl:
				{
					// Sneaking: from afar it comes round behind the player, and freezes flat while they look at it.
					watched = dist > 4f && IsWatched( prey, pos );
					var target = feet;
					if ( dist > 6f ) target = feet - ViewForward( prey ).Flat().SafeNormal() * 3f;
					var to = (target - pos).Flat();
					var way = to.SafeNormal();
					var speed = watched ? Speed * 0.3f : Speed;
					wish = dist > ZapRange * 0.75f ? way * speed : -dir * 0.6f;
					Face( dist > 3f ? way : dir, 2.4f, dt );
					var free = prey.Controller is { Scripted: false };
					if ( free && dist < ZapRange && sinceZap > ZapCooldown && MathF.Abs( chest.Y - pos.Y ) < 2.6f ) Begin( Act.Zap );
					else if ( dist > DiveMin && dist < DiveMax && sinceDive > DiveCooldown && !watched
						&& !Trace.Ray( this, pos + Vector3.Up * 0.3f, pos + Vector3.Up * (DiveHeight + 1f), Layers.World ).Hit ) Begin( Act.Climb );
					break;
				}
				case Act.Zap:
				{
					// It rises at the player's chest and rears, charging; then the zap.
					Face( dir, 5f, dt );
					wantY = MathF.Max( wantY, chest.Y - 0.45f );
					wish = dist > 1.6f ? dir * 2.4f : -dir * 0.8f;
					if ( !struck && a >= ZapWindup ) { struck = true; Zap( prey, chest ); }
					if ( a > ZapWindup + 0.6f ) Begin( Act.Recover );
					break;
				}
				case Act.Climb:
				{
					// Flapping up above the player, keeping a few metres off.
					Face( dir, 3f, dt );
					var floor = Ground( feet, false );
					wantY = MathF.Max( floor, groundMax ) + DiveHeight;
					wish = dist < 4.5f ? -dir * 2.6f : dist > 7f ? dir * 2.6f : Vector3.Zero;
					var high = pos.Y > wantY - 0.5f;
					var roof = Trace.Ray( this, pos, pos + Vector3.Up * 1.2f, Layers.World ).Hit;
					if ( (high && a > 0.7f) || a > ClimbTime || roof ) Begin( Act.Aim );
					break;
				}
				case Act.Aim:
				{
					// Hanging above, nose turning down at the player, starting to spin in its whirlpool.
					Face( dir, 4f, dt );
					wantY = pos.Y;
					diveDir = (chest - pos).SafeNormal();
					if ( a > AimTime ) Begin( Act.Dive );
					break;
				}
				case Act.Dive:
				{
					// Down along its path, steering a little after the player while they are still ahead.
					var toChest = chest - pos;
					if ( toChest.Dot( diveDir ) > 0.5f )
					{
						var want = toChest.SafeNormal();
						var angle = diveDir.AngleTo( want );
						if ( angle > 0.001f ) diveDir = diveDir.Slerp( want, MathF.Min( 1f, 1.1f * dt / angle ) ).Normalized();
					}
					velocity = diveDir * DiveSpeed;
					if ( !struck && pos.DistanceTo( chest ) < DiveHitRadius ) { struck = true; DiveHit( prey, chest ); }
					var next = pos + velocity * dt;
					var wall = Trace.Sphere( this, 0.3f, pos, next + diveDir * 0.4f, Layers.World );
					var floorNext = Ground( next, true );
					if ( next.Y < floorNext + 0.75f || wall.Hit || a > 1.8f ) { PullOut( floorNext ); break; }
					GlobalPosition = Sculpt.InBounds( next );
					return;
				}
				case Act.Recover:
				{
					// Pulled out low, skidding on and settling to the sand.
					Face( velocity.Flat().IsNearlyZero() ? dir : velocity.Flat(), 2f, dt );
					if ( a > 1f ) End();
					break;
				}
			}
		}

		Move( wish, wantY, dt );
	}

	/// <summary> Moves it: across on its velocity (walls slid along), up and down on a spring to the height it wants, never into the ground. </summary>
	void Move( Vector3 wish, float wantY, float dt )
	{
		var rate = act == Act.Recover ? 1.2f : act == Act.Climb ? 2.5f : 3f;
		var flatV = velocity.Flat().Lerp( wish.Flat(), 1f - MathF.Exp( -rate * dt ) );
		// Keep clear of other rays and of the player's body (except in its attacks).
		var p = GlobalPosition;
		foreach ( var other in All )
		{
			if ( other == this || other.IsDead ) continue;
			var away = (p - other.GlobalPosition).Flat();
			var d = away.Length();
			if ( d < 2.4f && d > 0.01f ) flatV += away / d * (2.4f - d) * 3f * dt;
		}
		// Walls ahead: feel with a small sphere at body height and slide along (the floor itself is the height's job).
		if ( flatV.LengthSquared() > 0.01f )
		{
			var from = p + Vector3.Up * 0.12f;
			var hit = Trace.Sphere( this, 0.28f, from, from + flatV.SafeNormal() * 1.1f, Layers.World );
			if ( hit.Hit && MathF.Abs( hit.Normal.Y ) < 0.6f ) flatV = flatV.Slide( hit.Normal.Flat().SafeNormal() );
		}
		velocity = new Vector3( flatV.X, 0f, flatV.Z );
		var next = p + velocity * dt;
		// Up and down: a soft spring to the height it wants.
		var k = act == Act.Climb ? 2.2f : act == Act.Zap ? 3.4f : 2.4f;
		ySpeed = MathX.Lerp( ySpeed, (wantY - p.Y) * k, 1f - MathF.Exp( -6f * dt ) );
		ySpeed = MathX.Clamp( ySpeed, -4f, act == Act.Climb ? 5.5f : 3f );
		next.Y = p.Y + ySpeed * dt;
		// Never into the ground.
		var floor = MathF.Max( groundMax, Ground( next, false ) );
		if ( next.Y < floor + MinClear ) { next.Y = floor + MinClear; ySpeed = MathF.Max( ySpeed, 0f ); }
		GlobalPosition = Sculpt.InBounds( next );
		// Gliding low and fast it stirs up a little sand.
		if ( act == Act.Prowl && !watched && sincePuff > 0.18f && velocity.Length() > 1.5f && next.Y - floor < CruiseHeight + 0.15f )
		{
			sincePuff = 0f;
			var side = Rng.Float() < 0.5f ? -1f : 1f;
			var at = body.GlobalTransform * new Vector3( side * HalfSpan * 0.8f, -0.05f, 0.25f );
			at.Y = floor + 0.05f;
			GpuFx.SandPuff( at, (Vector3.Up * 0.3f + Rng.InSphere( 0.3f ).Flat()) * 0.6f, new Color( 0.62f, 0.58f, 0.5f, 0.55f ), Rng.Float( 3f, 6f ), Rng.Float( 0.6f, 1.1f ) );
		}
	}

	/// <summary> The dive ends near the ground or at a wall: it flattens out low, skidding on (and kicking up sand). </summary>
	void PullOut( float floor )
	{
		Begin( Act.Recover );
		var p = GlobalPosition;
		p.Y = MathF.Max( MathF.Max( p.Y, floor + MinClear + 0.1f ), groundMax + MinClear );
		GlobalPosition = Sculpt.InBounds( p );
		velocity = diveDir.Flat().SafeNormal() * DiveSpeed * 0.35f;
		ySpeed = 0f;
		for ( var i = 0; i < 14; i++ )
		{
			var dir = (Rng.OnSphere().Flat() + Vector3.Up * 0.5f).SafeNormal();
			GpuFx.SandPuff( p with { Y = floor + 0.08f }, dir * Rng.Float( 0.6f, 1.8f ), new Color( 0.62f, 0.58f, 0.5f, 0.7f ), Rng.Float( 5f, 10f ), Rng.Float( 0.7f, 1.3f ) );
		}
		Sfx.Play( "land", p, 0.8f, 0.7f );
	}

	static Vector3 ViewForward( PlayerSurvivor p ) => p.Controller is { } c ? new Angles( c.EyeAngles.Pitch, c.EyeAngles.Yaw, 0f ).Forward : Vector3.Forward;

	/// <summary> Is the player looking its way (and can see it)? </summary>
	bool IsWatched( PlayerSurvivor prey, Vector3 at )
	{
		if ( prey.Controller is not { } c ) return false;
		var eye = c.GlobalPosition + Vector3.Up * 1.6f;
		var to = (at - eye).SafeNormal();
		return ViewForward( prey ).Dot( to ) > 0.82f;
	}

	/// <summary> The zap: bolts from its head to the player's chest; a hit hurts and stuns them in place. </summary>
	void Zap( PlayerSurvivor prey, Vector3 chest )
	{
		sinceZap = 0f;
		var head = body.GlobalTransform * new Vector3( 0f, 0.05f, -0.35f );
		var scene = GetTree().CurrentScene;
		var near = head.DistanceTo( chest ) < ZapReach && !Trace.Ray( this, head, chest, Layers.World ).Hit;
		Sfx.Play( "ray_zap", head, 1f, Rng.Float( 0.9f, 1.05f ) );
		// Crackles off the wing tips either way.
		foreach ( var x in new[] { -1f, 1f } )
			RayBolt.Arc( scene, body.GlobalTransform * new Vector3( x * HalfSpan * 0.9f, 0f, 0.15f ), head + Rng.InSphere( 0.3f ), 0.14f, 0.8f, light: false );
		if ( !near ) return;
		for ( var i = 0; i < 3; i++ ) RayBolt.Arc( scene, head, chest + Rng.InSphere( 0.25f ), 0.16f, 1f, light: i == 0 );
		ZapHits++;
		if ( prey.Health is { } h ) h.OnDamage( new DamageInfo { Damage = ZapDamage, Attacker = this, Position = chest, Origin = head } );
		if ( prey.Controller is { } c ) Electrocuted.Apply( c, StunTime, hold: true );
	}

	void DiveHit( PlayerSurvivor prey, Vector3 chest )
	{
		DiveHits++;
		if ( prey.Health is { } h ) h.OnDamage( new DamageInfo { Damage = DiveDamage, Attacker = this, Position = chest, Origin = GlobalPosition } );
		if ( prey.Controller is { } c )
		{
			if ( !c.Scripted ) c.Velocity += diveDir.Flat().SafeNormal() * 6f + Vector3.Up * 2f;
			c.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.8f );
		}
		Sfx.Play( "crab_snap", chest, 1f, 0.6f );
		Sfx.Play( "hit_flesh", chest, 0.9f, 0.7f );
	}

	void Begin( Act next )
	{
		act = next;
		sinceAct = 0f;
		struck = false;
		switch ( next )
		{
			case Act.Zap: sinceZap = 0f; Sfx.Play( "ray_charge", GlobalPosition, 0.9f, Rng.Float( 0.95f, 1.1f ) ); break;
			case Act.Climb: sinceDive = 0f; Sfx.Play( "swim", GlobalPosition, 0.8f, 0.7f ); break;
			case Act.Aim: Sfx.Play( "whirlpool", GlobalPosition, 0.7f, 1.4f ); break;
			case Act.Dive: Dives++; Sfx.Play( "dive_whoosh", GlobalPosition, 1f, 1.2f ); break;
		}
	}

	void End() { act = Act.Prowl; sinceAct = 0f; }

	/// <summary> Tests: start an attack now. </summary>
	public void ForceZap() { sinceZap = 99f; Begin( Act.Zap ); }
	public void ForceDive() { sinceDive = 99f; Begin( Act.Climb ); }
	/// <summary> Tests: back to gliding, its attacks just used. </summary>
	public void Calm() { End(); sinceDive = 0f; sinceZap = 0f; }
	public string ActName => act.ToString();

	// ---- Online: a joiner's copy draws the host's attacks ----

	public override void NetWrite( List<float> s )
	{
		s.Add( (int)act ); s.Add( (float)sinceAct ); s.Add( struck ? 1f : 0f );
		s.Add( diveDir.X ); s.Add( diveDir.Y ); s.Add( diveDir.Z );
	}

	public override void NetRead( ReadOnlySpan<float> s )
	{
		if ( s.Length < 6 ) return;
		var next = (Act)(int)s[0];
		var changed = next != act;
		if ( changed ) { act = next; if ( next is Act.Zap or Act.Climb or Act.Aim or Act.Dive ) Begin( next ); }
		NetTimer( ref sinceAct, s[1], changed );
		var nowStruck = s[2] > 0.5f;
		if ( act == Act.Zap && nowStruck && !struck ) PuppetZap();
		struck = nowStruck;
		diveDir = new Vector3( s[3], s[4], s[5] );
	}

	/// <summary> A joiner's copy: the host's zap went off. Bolts to whoever is close, and on this player's own robot the crackle (the host holds it). </summary>
	void PuppetZap()
	{
		var head = body.GlobalTransform * new Vector3( 0f, 0.05f, -0.35f );
		Sfx.Play( "ray_zap", head, 1f, 1f );
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c ) continue;
			var chest = c.GlobalPosition + Vector3.Up * 1.1f;
			if ( head.DistanceTo( chest ) > ZapReach + 0.5f ) continue;
			RayBolt.Arc( GetTree().CurrentScene, head, chest, 0.16f );
			if ( !c.IsRemote ) Electrocuted.Apply( c, StunTime, hold: false );
		}
	}

	// ---- Hits ----

	/// <summary> The eyes are the weak spots. </summary>
	public override bool WeakSpot( int index ) => index is 2 or 3;

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( NetCreatures.Intercept( this, index, damage, direction, position, origin, shooter, scoreHit ) ) return;
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		var weak = index is 2 or 3;
		var scale = (weak ? EyeMultiplier : 1f) * (Diving ? DiveDamageScale : 1f);
		var wasAlive = !IsDead;
		if ( !Lethal( damage * scale ) ) HitGore( index, damage * scale, position, direction );
		health.OnDamage( new DamageInfo { Damage = damage * scale, Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, weak, scoreHit );
	}

	protected override void OnDied()
	{
		RaiseKilled();
		if ( zapLight is not null ) zapLight.Visible = false;
		if ( vortex is not null ) vortex.Visible = false;
		FallApart( lastShotDirection );
		QueueFree();
	}
}
