using Godot;

/// <summary>
/// The cleaner wrasse (user's design): a small striped fish that heals the other enemies. It picks a patient (the big and
/// the hurt first, one wrasse to a patient, never a boss), hides behind it from the nearest player and links to it with
/// a green beam: the patient heals <see cref="HealShare"/> of its full health a second and wears a
/// <see cref="CleanerWard"/> shield that fills in <see cref="CleanerWard.FillTime"/>, growing with the wave. Aimed at for
/// <see cref="AimReact"/> (or shot) it darts aside, drops the link, waits a moment and comes back; it can only do that
/// every <see cref="DartEvery"/>, so a steady aim gets it. It never attacks. Metres, facing -Z.
/// </summary>
public partial class CleanerWrasse : Creature
{
	public override bool CanScare => false;
	public static readonly List<CleanerWrasse> All = new();

	/// <summary> What the patient heals a second, of its full health (user's pick: 7%). </summary>
	public const float HealShare = 0.07f;
	/// <summary> How far the link reaches (m): past this it has to swim back. </summary>
	public const float LinkRange = 8f;
	/// <summary> Nose to tail (m): about knee height on a robot. </summary>
	public const float Length = 0.7f;
	public float Speed = 4f;
	public const float DartSpeed = 9f, DartTime = 0.5f, WaryTime = 1.5f, DartEvery = 3.5f;
	/// <summary> How long a player's aim must stay on it before it bolts (a quick flick shot still lands). </summary>
	public const float AimReact = 0.5f;
	public const float HeadMultiplier = 1.5f;
	/// <summary> The shield it gives its patients (set from the wave by the spawner). </summary>
	public float ShieldMax = 100f;

	/// <summary> Tests: links made, darts. </summary>
	public static int Links, Darts;

	enum Act { Seek, Clean, Dart, Wary }
	Act act = Act.Seek;
	Creature patient;
	Vector3 velocity, dartTo;
	float aimed, phase, rayFloor = float.MinValue;
	TimeSince sinceAct = 99f, sinceDart = 99f, sincePick = 99f, sinceFloor = 99f, sinceHit = 99f;
	PlayerController aimer;

	public static CleanerWrasse Spawn( Node parent, Vector3 position, float maxHealth ) =>
		Place( new CleanerWrasse { Name = "Cleaner Wrasse" }, parent, position, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	public override bool WeakSpot( int index ) => index == 0;

	public Creature Patient => patient is not null && IsInstanceValid( patient ) && !patient.IsDead ? patient : null;
	/// <summary> Healing now: the beam is on. </summary>
	public bool Linked => act == Act.Clean && Patient is not null;
	public bool Darting => act is Act.Dart or Act.Wary;
	public Vector3 Centre => GlobalPosition;

	/// <summary> Whom a wrasse will clean: any enemy but bosses, other wrasses, missiles, fish swarms and the scenery. </summary>
	public static bool Treats( Creature c ) =>
		c is not null && IsInstanceValid( c ) && !c.IsDead && c.IsEnemy && c.Health is not null
		&& c is not (CleanerWrasse or HomingMissile or FishSwarm or MimicOctopus or GardenEel)
		&& c is not (MC61 or Kraken or Poseidon or DaveyJones or KingRaymond);

	protected override void Setup()
	{
		BeginSetup( 80f );
		phase = Rng.Float( 0f, MathF.Tau );
		Build();
		Pose( 0f );
	}

	public override void _Process( double delta )
	{
		if ( health is null || look is null ) return;
		// Long frames are split up (up to half a second), so its healing and darting keep real time at a low frame rate.
		var total = MathF.Min( (float)delta, 0.5f );
		var steps = Math.Max( 1, (int)MathF.Ceiling( total / 0.1f ) );
		var dt = total / steps;
		for ( var i = 0; i < steps && !IsDead && !Frozen; i++ ) Think( dt );
		if ( !IsDead ) { Pose( dt ); DrawBeam( dt ); }
	}

	// ---- Brain ----

	void Think( float dt )
	{
		WatchAim( dt );
		if ( Patient is null || !Treats( patient ) ) patient = null;
		if ( act is Act.Seek or Act.Clean && sincePick > 1.5f ) { sincePick = 0f; Pick(); }
		var target = GlobalPosition;
		var speed = Speed;
		switch ( act )
		{
			case Act.Seek:
				if ( Patient is { } p )
				{
					target = CleanSpot( p );
					if ( GlobalPosition.DistanceTo( CleanerWard.CentreOf( p ) ) < LinkRange * 0.75f ) Begin( Act.Clean );
				}
				else target = Loiter();
				break;
			case Act.Clean:
				if ( Patient is not { } q || GlobalPosition.DistanceTo( CleanerWard.CentreOf( q ) ) > LinkRange ) { Begin( Act.Seek ); break; }
				target = CleanSpot( q );
				Treat( q, dt );
				break;
			case Act.Dart:
				target = dartTo;
				speed = DartSpeed;
				if ( sinceAct > DartTime ) Begin( Act.Wary );
				break;
			case Act.Wary:
				speed = Speed * 0.3f;
				if ( sinceAct > WaryTime ) Begin( Act.Seek );
				break;
		}
		if ( act is Act.Seek or Act.Clean && aimed > AimReact && sinceDart > DartEvery ) StartDart();
		Move( target, speed, dt );
		var face = act == Act.Clean && Patient is { } f ? CleanerWard.CentreOf( f ) - GlobalPosition : velocity;
		Face( face, act == Act.Dart ? 9f : 4f, dt );
	}

	/// <summary> Picks a patient: big and hurt ones first, near ones over far, and never one another wrasse has. </summary>
	void Pick()
	{
		Creature best = null;
		var bestScore = 0f;
		foreach ( var c in Creature.Everyone )
		{
			if ( !Treats( c ) ) continue;
			var taken = false;
			foreach ( var w in All ) if ( w != this && !w.IsDead && w.patient == c ) { taken = true; break; }
			if ( taken ) continue;
			var d = c.GlobalPosition.DistanceTo( GlobalPosition );
			if ( d > 40f ) continue;
			var hurt = 1f - c.Health.Fraction;
			var score = c.Health.MaxHealth * (1f + 3f * hurt) / (1f + d * 0.15f);
			if ( c == patient ) score *= 1.5f;   // keeps at it
			if ( score > bestScore ) { bestScore = score; best = c; }
		}
		if ( best != patient && act == Act.Clean ) Begin( Act.Seek );
		patient = best;
	}

	/// <summary> Behind the patient, from the nearest player: it hides behind the big ones. </summary>
	Vector3 CleanSpot( Creature p )
	{
		var centre = CleanerWard.CentreOf( p );
		var radius = p.Ward is { } w && IsInstanceValid( w ) ? w.Radius : 0.8f;
		var player = PlayerSurvivor.FindNearestAlive( centre );
		var away = player is null ? p.GlobalBasis.Z : (centre - player.WorldPosition).Flat().SafeNormal();
		if ( away == Vector3.Zero ) away = Vector3.Back;
		var side = away.Cross( Vector3.Up ).Normalized();
		var t = (float)Clock.Now + phase;
		return centre + away * (radius + 1.1f) + side * MathF.Sin( t * 0.7f ) * 0.6f + Vector3.Up * (0.25f + MathF.Sin( t * 1.1f ) * 0.2f);
	}

	/// <summary> Nobody to clean: it hangs about 6 to 9 m off the nearest player, weaving. </summary>
	Vector3 Loiter()
	{
		var player = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		if ( player is null ) return GlobalPosition;
		var t = (float)Clock.Now * 0.35f + phase;
		var round = new Vector3( MathF.Cos( t ), 0f, MathF.Sin( t ) );
		return player.WorldPosition + round * (7.5f + MathF.Sin( t * 2.3f ) * 1.5f) + Vector3.Up * 1.6f;
	}

	void Treat( Creature p, float dt )
	{
		p.Health.Heal( p.Health.MaxHealth * HealShare * dt );
		CleanerWard.For( p ).Feed( ShieldMax, dt );
	}

	/// <summary> How long a player's aim has been on it (falls off twice as fast). </summary>
	void WatchAim( float dt )
	{
		var on = false;
		var me = Centre;
		foreach ( var s in PlayerSurvivor.All )
		{
			if ( !s.IsAlive || s.Controller is not { } c ) continue;
			var eye = c.EyeTransform;
			var forward = -eye.Basis.Z;
			var to = me - eye.Origin;
			var along = to.Dot( forward );
			if ( along < 0.5f || along > 45f ) continue;
			if ( (to - forward * along).Length() > 0.45f + along * 0.025f ) continue;
			on = true;
			aimer = c;
			break;
		}
		aimed = on ? aimed + dt : MathF.Max( 0f, aimed - dt * 2f );
	}

	/// <summary> Bolts sideways off the aim (or the shot), staying in reach of its patient. </summary>
	void StartDart()
	{
		var from = aimer is not null && IsInstanceValid( aimer ) ? aimer.EyeTransform.Origin : PlayerSurvivor.FindNearestAlive( GlobalPosition )?.WorldPosition ?? GlobalPosition + Vector3.Forward;
		var off = (GlobalPosition - from).SafeNormal();
		var side = off.Cross( Vector3.Up ).SafeNormal();
		if ( side == Vector3.Zero ) side = Vector3.Right;
		if ( Rng.Float() < 0.5f ) side = -side;
		var to = GlobalPosition + side * 3.2f + off.Flat().SafeNormal() * 1f + Vector3.Up * Rng.Float( -0.3f, 0.6f );
		if ( Patient is { } p )
		{
			var centre = CleanerWard.CentreOf( p );
			if ( to.DistanceTo( centre ) > LinkRange - 1f ) to = centre + (to - centre).Normalized() * (LinkRange - 1f);
		}
		dartTo = to;
		aimed = 0f;
		sinceDart = 0f;
		Darts++;
		Begin( Act.Dart );
		Sfx.Play( "bubble_blow", GlobalPosition, 0.5f, Rng.Float( 1.5f, 1.8f ) );
	}

	void Begin( Act next )
	{
		if ( next == Act.Clean && act != Act.Clean )
		{
			Links++;
			Sfx.Play( "heal", GlobalPosition, 0.35f, 1.4f );
		}
		act = next;
		sinceAct = 0f;
	}

	/// <summary> Toward a point, off the walls, the floor, its patient's shield and the other wrasses. </summary>
	void Move( Vector3 target, float speed, float dt )
	{
		var p = GlobalPosition;
		var to = target - p;
		var wish = to.Length() > 0.1f ? to.SafeNormal() * MathF.Min( speed, to.Length() * 2.5f ) : Vector3.Zero;
		if ( act == Act.Wary ) wish *= 0.3f;
		velocity = velocity.Lerp( wish, 1f - MathF.Exp( -(act == Act.Dart ? 7f : 3f) * dt ) );
		foreach ( var other in All )
		{
			if ( other == this || other.IsDead ) continue;
			var away = p - other.GlobalPosition;
			var d = away.Length();
			if ( d < 0.9f && d > 0.01f ) velocity += away / d * (0.9f - d) * 5f * dt;
		}
		if ( Patient is { } pat && pat.Ward is { } w && IsInstanceValid( w ) )
		{
			var away = p - w.Centre;
			var d = away.Length();
			var room = w.Radius + 0.35f;
			if ( d < room && d > 0.01f ) velocity += away / d * (room - d) * 8f * dt;
		}
		if ( velocity.LengthSquared() > 0.01f )
		{
			var hit = Trace.Sphere( this, 0.14f, p, p + velocity.SafeNormal() * 0.6f, Layers.World );
			if ( hit.Hit ) velocity = velocity.Slide( hit.Normal );
		}
		var next = p + velocity * dt;
		if ( sinceFloor > 0.15f ) { sinceFloor = 0f; rayFloor = Sculpt.FloorBelow( this, next + Vector3.Up * 0.3f, 12f ); }
		var ground = Sculpt.GroundAt( next );
		var floor = MathF.Max( ground == float.MinValue ? rayFloor : ground, rayFloor );
		var low = floor + 0.35f;
		if ( next.Y < low ) { next.Y = MathX.Lerp( next.Y, low, 1f - MathF.Exp( -8f * dt ) ); velocity.Y = MathF.Max( velocity.Y, 0f ); }
		GlobalPosition = Sculpt.InBounds( next );
	}

	// ---- Hits ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( NetCreatures.Intercept( this, index, ref damage, direction, position, origin, shooter, scoreHit ) ) return;
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		flinch = 1f;
		var weak = index == 0;
		var dealt = damage * (weak ? HeadMultiplier : 1f);
		var wasAlive = !IsDead;
		if ( !Lethal( dealt ) ) gore?.GooBurst( position, -direction, GooColour, 3, 160f, 0.4f );
		health.OnDamage( new DamageInfo { Damage = dealt, Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, weak, scoreHit );
		// Shot and still alive: it bolts (if it can yet).
		if ( !IsDead && !Puppet && act is Act.Seek or Act.Clean && sinceDart > DartEvery ) { aimer = shooter?.Controller; StartDart(); }
	}

	protected override void OnDied()
	{
		RaiseKilled();
		var at = Centre;
		var dir = lastShotDirection == Vector3.Zero ? Vector3.Up : lastShotDirection.Normalized();
		gore?.GooBurst( at, Vector3.Up, GooColour, 10, 240f, 0.8f );
		gore?.GooSpray( at, dir, 3, GooColour, 200f, 0.5f, 1f );
		GibKit.Burst( Gibs, at, dir, 0.06f, 6 );
		Sfx.Play( "bubble_pop", at, 0.8f, 0.8f );
		QueueFree();
	}

	// ---- Online ----

	public override void NetWrite( List<float> s ) { s.Add( (int)act ); s.Add( (float)sinceAct ); s.Add( Patient?.NetId ?? 0 ); }

	public override void NetRead( ReadOnlySpan<float> s )
	{
		if ( s.Length < 3 ) return;
		var next = (Act)(int)s[0];
		var changed = next != act;
		if ( changed ) Begin( next );
		NetTimer( ref sinceAct, s[1], changed );
		patient = (int)s[2] > 0 ? NetCreatures.Find( (int)s[2] ) : null;
	}

	// ---- Tests ----

	/// <summary> Tests: the aim has been on it this long. </summary>
	public float AimedFor => aimed;
}
