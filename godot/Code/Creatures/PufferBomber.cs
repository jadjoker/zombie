using Godot;

/// <summary>
/// The puffer bomber (user's pick): a spiky alien pufferfish that drifts at the player and blows up. Close enough
/// (<see cref="ArmRange"/>) it lights its fuse: for <see cref="FuseTime"/> it swells up, its spines stand on end, it
/// flashes red faster and faster with a ticking that climbs, and a red ring on the sand shows how far the burst
/// reaches, while it creeps on at a crawl (back off and you clear it). Then it bursts: a blast, and a ring of spines
/// that flies out level all round (and a few slanted up and down). The blast and the spines hurt other enemies too, so
/// shooting it early, when it is near them, pops it where it is and turns it on them. However it dies, it bursts.
/// Weak spots: the eyes. Metres, facing -Z.
/// </summary>
public partial class PufferBomber : Creature
{
	public static readonly List<PufferBomber> All = new();

	public float Speed = 2.3f;
	/// <summary> From its middle to a player's chest: this close and it lights its fuse. </summary>
	public float ArmRange = 2.8f;
	public const float FuseTime = 1.6f;
	/// <summary> The share of its speed it creeps on at while the fuse burns (a walk backwards clears it). </summary>
	public const float FuseCreep = 0.35f;
	/// <summary> How big it swells by the end of the fuse. </summary>
	public const float Swell = 2.1f;
	/// <summary> The body's radius at rest (m). </summary>
	public const float Radius = 0.3f;
	public const int RingSpines = 24, SlantSpines = 12;
	public const float SpineSpeed = 15f, SpineRange = 6f;
	public const float SpineDamage = 8f, SpineCreatureDamage = 40f;
	public const float BlastRadius = 2.4f, BlastDamage = 22f, BlastCreatureDamage = 70f;
	/// <summary> The red ring on the sand while the fuse burns: as far as its spines fly. </summary>
	public const float WarnRadius = SpineRange;
	public const float EyeMultiplier = 2f;
	/// <summary> How high over the player's feet it swims (their chest). </summary>
	const float ChestHeight = 1.1f;

	/// <summary> Tests: fuses lit, bursts, spines that hit a player or an enemy, enemies the blast hurt. </summary>
	public static int FusesLit, Bursts, SpineHitsPlayer, SpineHitsCreature, BlastCreatureHits;

	enum Act { Swim, Fuse }
	Act act = Act.Swim;
	TimeSince sinceAct = 99f, sinceTick = 99f, sinceHit = 99f, sinceFloor = 99f;
	Vector3 velocity;
	float phase, rayFloor = float.MinValue;
	bool burst;
	PlayerController lastShooter;

	public static PufferBomber Spawn( Node parent, Vector3 position, float maxHealth ) =>
		Place( new PufferBomber { Name = "Puffer Bomber" }, parent, position, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	public override bool WeakSpot( int index ) => index is 1 or 2;

	public bool Fusing => act == Act.Fuse;
	/// <summary> How far the fuse has burnt, 0 to 1 (0 when it isn't lit). </summary>
	public float Fuse => act == Act.Fuse ? MathX.Clamp( (float)sinceAct / FuseTime, 0f, 1f ) : 0f;
	/// <summary> Its size now against its size at rest. </summary>
	public float SwellNow => swell;
	public Vector3 Centre => body is not null && IsInstanceValid( body ) ? body.GlobalPosition : GlobalPosition;

	protected override void Setup()
	{
		BeginSetup( 70f );
		phase = Rng.Float( 0f, MathF.Tau );
		Build();
		Pose( 0f );
	}

	public override void _Process( double delta )
	{
		if ( health is null || body is null ) return;
		var dt = MathF.Min( (float)delta, 0.1f );
		if ( !IsDead && !Frozen ) Think( dt );
		if ( !IsDead ) Pose( dt );
	}

	// ---- Brain ----

	void Think( float dt )
	{
		var prey = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var wish = Vector3.Zero;
		if ( prey is not null )
		{
			var chest = prey.WorldPosition + Vector3.Up * ChestHeight;
			var to = chest - GlobalPosition;
			var dist = to.Length();
			var dir = to.SafeNormal();
			switch ( act )
			{
				case Act.Swim:
					wish = dir * Speed;
					if ( dist < ArmRange && !Trace.Ray( this, GlobalPosition, chest, Layers.World ).Hit ) Begin( Act.Fuse );
					break;
				case Act.Fuse:
					wish = dist > 0.9f ? dir * Speed * FuseCreep : Vector3.Zero;
					if ( sinceAct >= FuseTime ) { Detonate(); return; }
					break;
			}
			Face( to, act == Act.Fuse ? 5f : 2.5f, dt );
		}
		else if ( act == Act.Fuse && sinceAct >= FuseTime ) { Detonate(); return; }
		Move( wish, dt );
	}

	/// <summary> Across and up toward where it wants to be, off the walls, the floor and the other puffers. </summary>
	void Move( Vector3 wish, float dt )
	{
		velocity = velocity.Lerp( wish, 1f - MathF.Exp( -2.2f * dt ) );
		var p = GlobalPosition;
		foreach ( var other in All )
		{
			if ( other == this || other.IsDead ) continue;
			var away = p - other.GlobalPosition;
			var d = away.Length();
			var room = Radius * (swell + other.swell) + 0.35f;
			if ( d < room && d > 0.01f ) velocity += away / d * (room - d) * 4f * dt;
		}
		var size = Radius * swell;
		if ( velocity.LengthSquared() > 0.01f )
		{
			var hit = Trace.Sphere( this, size * 0.9f, p, p + velocity.SafeNormal() * (size + 0.5f), Layers.World );
			if ( hit.Hit ) velocity = velocity.Slide( hit.Normal );
		}
		var next = p + velocity * dt;
		// The bob of a fat fish swimming, and never into the floor.
		next.Y += MathF.Sin( (float)Clock.Now * 2.1f + phase ) * 0.12f * dt;
		if ( sinceFloor > 0.15f ) { sinceFloor = 0f; rayFloor = Sculpt.FloorBelow( this, next + Vector3.Up * 0.3f, 12f ); }
		var ground = Sculpt.GroundAt( next );
		var floor = MathF.Max( ground == float.MinValue ? rayFloor : ground, rayFloor );
		var low = floor + size + 0.3f;
		if ( next.Y < low ) { next.Y = MathX.Lerp( next.Y, low, 1f - MathF.Exp( -8f * dt ) ); velocity.Y = MathF.Max( velocity.Y, 0f ); }
		GlobalPosition = Sculpt.InBounds( next );
	}

	void Begin( Act next )
	{
		act = next;
		sinceAct = 0f;
		if ( next == Act.Fuse )
		{
			FusesLit++;
			sinceTick = 99f;
			Sfx.Play( "cannon_fuse", GlobalPosition, 0.55f, 1.5f );
			Sfx.Play( "bubble_charge", GlobalPosition, 0.6f, 1.3f );
		}
	}

	/// <summary> The fuse ran out: it bursts (by dying, so a joiner's copy bursts with it). </summary>
	void Detonate()
	{
		if ( IsDead ) return;
		lastShooter = null;   // nobody set it off
		health.Kill();
	}

	// ---- Hits ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( NetCreatures.Intercept( this, index, ref damage, direction, position, origin, shooter, scoreHit ) ) return;
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		flinch = 1f;
		var weak = index is 1 or 2;
		var dealt = damage * (weak ? EyeMultiplier : 1f);
		var wasAlive = !IsDead;
		if ( !Lethal( dealt ) ) gore?.GooBurst( position, -direction, GooColour, 4, 180f, 0.5f );
		if ( shooter?.Controller is { } c ) lastShooter = c;
		health.OnDamage( new DamageInfo { Damage = dealt, Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, weak, scoreHit );
	}

	protected override void OnDied()
	{
		RaiseKilled();
		Burst();
		QueueFree();
	}

	/// <summary>
	/// The burst: a blast, its gore, and the spines flying out. Only the host's (or an offline) copy hurts anyone; a
	/// joiner's copy throws harmless spines that look the same.
	/// </summary>
	void Burst()
	{
		if ( burst ) return;
		burst = true;
		Bursts++;
		var scene = GetTree().CurrentScene;
		var at = Centre;
		var size = Radius * swell;
		var hurts = !Puppet;
		AlienBlast.Spawn( scene, at, 0.9f + 0.25f * swell );
		Sfx.Play( "puffer_burst", at, 1f, Rng.Float( 0.92f, 1.06f ) );
		var dir = lastShotDirection == Vector3.Zero ? Vector3.Up : lastShotDirection.Normalized();
		gore?.GooBurst( at, Vector3.Up, GooColour, 14, 300f, 1.1f );
		gore?.GooSpray( at, dir, 4, GooColour, 220f, 0.6f, 1.2f );
		GibKit.Burst( Gibs, at, dir, 0.1f * swell, 7 );
		if ( ring is not null ) ring.Visible = false;

		// The ring of spines, level all round, and a few slanted up and down.
		var turn = Rng.Float( 0f, MathF.Tau );
		for ( var i = 0; i < RingSpines + SlantSpines; i++ )
		{
			Vector3 way;
			if ( i < RingSpines )
			{
				var a = turn + i * MathF.Tau / RingSpines + Rng.Float( -0.04f, 0.04f );
				way = new Vector3( MathF.Cos( a ), Rng.Float( -0.06f, 0.06f ), MathF.Sin( a ) );
			}
			else
			{
				var k = i - RingSpines;
				var a = turn + (k + 0.5f) * MathF.Tau / (SlantSpines / 2f);
				var up = k % 2 == 0 ? 0.6f : -0.5f;
				way = new Vector3( MathF.Cos( a ), up, MathF.Sin( a ) );
			}
			way = way.Normalized();
			PufferSpine.Fire( scene, at + way * size * 0.85f, way * SpineSpeed, hurts ? SpineDamage : 0f, hurts ? SpineCreatureDamage : 0f, lastShooter, this );
		}

		if ( !hurts ) return;
		// The blast itself: players close by (less at the edge), and every enemy near it (other puffers go up too).
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive ) continue;
			var body = p.RealPosition + Vector3.Up * 1f;
			var d = body.DistanceTo( at );
			if ( d > BlastRadius ) continue;
			p.Health?.OnDamage( new DamageInfo { Damage = BlastDamage * (1f - 0.5f * d / BlastRadius), Attacker = this, Position = at, Origin = at } );
			if ( p.Controller is { } c && !c.Scripted ) c.Velocity += ((c.GlobalPosition - at).Flat().SafeNormal() + Vector3.Up * 0.4f) * 5f;
			p.Juice?.Shake( 0.6f );
		}
		BlastCreatureHits += PlayerPerks.HurtCreatures( at, BlastRadius, BlastCreatureDamage, lastShooter );
	}

	// ---- Online ----

	public override void NetWrite( List<float> s ) { s.Add( (int)act ); s.Add( (float)sinceAct ); }

	public override void NetRead( ReadOnlySpan<float> s )
	{
		if ( s.Length < 2 ) return;
		var next = (Act)(int)s[0];
		var changed = next != act;
		if ( changed ) Begin( next );
		NetTimer( ref sinceAct, s[1], changed );
	}

	// ---- Tests ----

	/// <summary> Tests: light the fuse now. </summary>
	public void ForceFuse() { if ( act != Act.Fuse ) Begin( Act.Fuse ); }
}

/// <summary>
/// One of the puffer bomber's spines, flung out when it bursts: it flies straight (sinking a little), hurts the first
/// player or enemy it reaches, and sticks in whatever else it hits for a moment. Damage 0: a joiner's harmless copy.
/// </summary>
public partial class PufferSpine : Node3D
{
	Vector3 velocity;
	float damage, creatureDamage, travelled;
	PlayerController shooter;
	Node from;
	bool stuck;
	TimeSince sinceStuck;
	static readonly CylinderMesh Mesh = new() { TopRadius = 0f, BottomRadius = 0.022f, Height = 0.3f, RadialSegments = 5, Rings = 1 };
	static StandardMaterial3D paint;

	public static PufferSpine Fire( Node parent, Vector3 at, Vector3 velocity, float damage, float creatureDamage, PlayerController shooter, Node from )
	{
		paint ??= Sculpt.Paint( new Color( 0.95f, 0.88f, 0.66f ), 0.45f );
		var s = new PufferSpine { Name = "Puffer Spine", velocity = velocity, damage = damage, creatureDamage = creatureDamage, shooter = shooter, from = from };
		s.AddChild( new MeshInstance3D { Mesh = Mesh, MaterialOverride = paint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
		parent.AddChild( s );
		s.GlobalPosition = at;
		s.Aim();
		return s;
	}

	void Aim()
	{
		if ( velocity.LengthSquared() > 1e-4f ) GlobalBasis = new Basis( new Quaternion( Vector3.Up, velocity.Normalized() ) );
	}

	public override void _Process( double delta )
	{
		var dt = MathF.Min( (float)delta, 0.05f );
		if ( stuck )
		{
			if ( sinceStuck > 2.5f ) QueueFree();
			return;
		}
		var start = GlobalPosition;
		velocity += Vector3.Down * 1.5f * dt;
		var end = start + velocity * dt;
		// A player in the way.
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive ) continue;
			var feet = player.RealPosition;
			var pair = Geometry3D.GetClosestPointsBetweenSegments( start, end, feet + Vector3.Up * 0.2f, feet + Vector3.Up * 1.7f );
			var a = pair[0];
			if ( a.DistanceTo( pair[1] ) > 0.38f ) continue;
			if ( damage > 0f )
			{
				PufferBomber.SpineHitsPlayer++;
				player.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = IsInstanceValid( from ) ? from as Node3D : this, Position = a, Origin = start } );
			}
			Sfx.Play( "hit_flesh", a, 0.5f, 1.4f );
			QueueFree();
			return;
		}
		// An enemy (or anything else that can be shot) in the way.
		var hit = Trace.Ray( this, start, end, Layers.Creature | Layers.World );
		if ( hit.Hit )
		{
			var shootable = Shootables.Find( hit.Node );
			if ( shootable?.ShotOwner is Creature c && c != from )
			{
				if ( !c.IsDead && creatureDamage > 0f && c.IsEnemy )
				{
					PufferBomber.SpineHitsCreature++;
					PlayerPerks.Hurt( c, creatureDamage, hit.HitPosition, start, shooter );
				}
				Fx.ImpactFlesh( hit.HitPosition, -velocity.SafeNormal(), new Color( 0.9f, 0.3f, 0.2f ) );
				QueueFree();
				return;
			}
			if ( shootable is not null && shootable.ShotOwner is not Creature && creatureDamage > 0f && !shootable.IsDead )
			{
				shootable.ReceiveShot( creatureDamage, hit.Node as Node3D, velocity.SafeNormal(), hit.HitPosition, start, shooter, false );
				QueueFree();
				return;
			}
			if ( shootable is null )
			{
				// Into the world: it sticks there a moment.
				stuck = true;
				sinceStuck = 0f;
				GlobalPosition = hit.HitPosition - velocity.SafeNormal() * 0.1f;
				return;
			}
		}
		travelled += start.DistanceTo( end );
		GlobalPosition = end;
		Aim();
		if ( travelled > PufferBomber.SpineRange ) QueueFree();
	}
}
