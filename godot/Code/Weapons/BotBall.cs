using Godot;

/// <summary>
/// A ball that bounces about the world under its own simple physics (Magnet-Bot's scrap balls, Pinball-Bot's steel
/// balls): a sphere trace each step, a bounce off what it meets (`Restitution` off the world, `BumperGain` off an
/// enemy), rolling along the floor once its bounce dies down, and an end after `MaxBounces` bounces or `Life` seconds.
/// An enemy it meets is handed to `HitEnemy` (at most once every `HitGap` seconds each).
/// </summary>
public abstract partial class BotBall : Node3D
{
	protected PlayerController owner;
	protected PlayerWeapon weapon;
	public Vector3 Velocity;
	public float Radius = 0.2f, Gravity = 5f, Drag = 0.15f, Restitution = 0.6f, BumperGain = 1f, Life = 5f, HitGap = 0.3f;
	public int MaxBounces = 3;
	protected int bounces, enemyHits;
	protected float age;
	protected Node3D look;
	/// <summary> A beat of stillness after a bumper hit (the ball sits in the bumper a moment before it is kicked out). </summary>
	protected float holdFor;
	/// <summary> Held somewhere by its owner (a raised flipper): no physics of its own. </summary>
	public bool Cradled;
	bool ended;
	readonly Dictionary<Creature, double> lastHit = new();
	Godot.Collections.Array<Rid> exclude;

	public static readonly List<BotBall> All = new();

	/// <summary> A ball's blow lands like a shot on the part it hit (so weak spots and armour count as for any gun). </summary>
	protected void Strike( Creature c, Node3D part, Vector3 at, float damage )
	{
		var shootable = Shootables.Find( part );
		if ( shootable is not null && !shootable.IsDead ) shootable.ReceiveShot( damage, part, Velocity.SafeNormal(), at, owner.GlobalPosition, owner, true );
		else SkillFx.HitPart( c, damage, at - Velocity.SafeNormal() * 0.4f, owner );
		Fx.ImpactFlesh( at, -Velocity.SafeNormal(), new Color( 0.9f, 0.3f, 0.2f ) );
	}
	public PlayerController Owner => owner;
	public bool Ended => ended;
	public int EnemyHits => enemyHits;

	protected void Launch( PlayerWeapon weapon, PlayerController owner, Vector3 from, Vector3 velocity )
	{
		this.weapon = weapon;
		this.owner = owner;
		Velocity = velocity;
		owner.GetTree().CurrentScene.AddChild( this );
		GlobalPosition = from;
		exclude = new Godot.Collections.Array<Rid> { owner.GetRid() };
		look = new Node3D { Name = "Look" };
		AddChild( look );
		Build();
		All.Add( this );
	}

	public override void _ExitTree() => All.Remove( this );

	protected abstract void Build();
	/// <summary> It met an enemy at this speed; returns false to fly on through it instead of bouncing. </summary>
	protected abstract bool HitEnemy( Creature c, Node3D part, Vector3 at, float speed );
	protected virtual void HitWorld( Vector3 at, Vector3 normal, float speed ) { }
	protected virtual void Step( float dt ) { }
	protected virtual void Finish() { }
	/// <summary> The velocity it leaves an enemy with (a plain bounce, `BumperGain` faster, unless a ball says otherwise). </summary>
	protected virtual Vector3 BounceOffEnemy( Creature c, Vector3 normal, Vector3 at, Vector3 velocity ) => velocity.Bounce( normal ) * BumperGain;
	/// <summary> Whether this ball hit that enemy within the last `HitGap` seconds. </summary>
	protected bool RecentlyHit( Creature c ) => lastHit.TryGetValue( c, out var at ) && Clock.Now - at <= HitGap;

	public override void _PhysicsProcess( double delta )
	{
		if ( ended ) return;
		var dt = (float)delta;
		age += dt;
		if ( !IsInstanceValid( owner ) || age > Life ) { End(); return; }
		if ( Cradled ) { Step( dt ); return; }
		if ( holdFor > 0f ) { holdFor -= dt; Step( dt ); return; }
		Velocity += Vector3.Down * Gravity * dt;
		Velocity *= MathF.Exp( -Drag * dt );
		Step( dt );
		if ( ended ) return;
		var move = Velocity * dt;
		for ( var i = 0; i < 3 && move.LengthSquared() > 1e-8f; i++ )
		{
			var from = GlobalPosition;
			var tr = Trace.Sphere( owner, Radius, from, from + move, Layers.World | Layers.Creature, exclude );
			if ( !tr.Hit ) { GlobalPosition = from + move; break; }
			if ( tr.StartedSolid ) { GlobalPosition = from + Vector3.Up * 0.05f; break; }
			var centre = from + move * tr.Fraction;
			var normal = tr.Normal.LengthSquared() > 0.5f ? tr.Normal : (centre - tr.HitPosition).SafeNormal();
			GlobalPosition = centre + normal * 0.01f;
			var speed = Velocity.Length();
			var remaining = move * (1f - tr.Fraction);
			if ( SkillFx.EnemyOf( tr.Node ) is { } c )
			{
				var now = Clock.Now;
				var fresh = !lastHit.TryGetValue( c, out var at ) || now - at > HitGap;
				if ( fresh )
				{
					lastHit[c] = now;
					enemyHits++;
					var bounce = HitEnemy( c, tr.Node, tr.HitPosition, speed );
					if ( ended ) return;
					if ( !bounce ) { foreach ( var p in CreaturePart.All ) if ( p.Creature == c && IsInstanceValid( p ) ) exclude.Add( p.GetRid() ); move = remaining; continue; }
					Velocity = BounceOffEnemy( c, normal, tr.HitPosition, Velocity );
					if ( holdFor > 0f ) break;   // it sits in the bumper a beat, then goes
					move = Velocity.SafeNormal() * remaining.Length();
					continue;
				}
				Velocity = Velocity.Bounce( normal ) * BumperGain;
				move = remaining.Bounce( normal );
				continue;
			}
			var shootable = Shootables.Find( tr.Node );
			if ( shootable is GlowBulb bulb ) bulb.ReceiveShot( 50f, tr.Node, Velocity.SafeNormal(), tr.HitPosition, owner.GlobalPosition, owner, true );
			var into = -Velocity.Dot( normal );
			if ( normal.Y > 0.6f && into < 2.2f )
			{
				// Too soft to bounce: it rolls along the floor (losing a little to friction).
				Velocity -= normal * Velocity.Dot( normal );
				Velocity *= MathF.Exp( -0.8f * dt );
				move = remaining - normal * remaining.Dot( normal );
				continue;
			}
			HitWorld( tr.HitPosition, normal, speed );
			if ( ended ) return;
			Velocity = Velocity.Bounce( normal ) * Restitution;
			move = remaining.Bounce( normal ) * Restitution;
			if ( ++bounces > MaxBounces ) { End(); return; }
		}
		// Spin with the travel, so it reads as rolling.
		var flat = Velocity with { Y = 0f };
		if ( flat.LengthSquared() > 0.01f )
		{
			var axis = Vector3.Up.Cross( flat ).Normalized();
			look.GlobalBasis = (new Basis( axis, flat.Length() * dt / Radius ) * look.GlobalBasis).Orthonormalized();
		}
	}

	public void End()
	{
		if ( ended ) return;
		ended = true;
		Finish();
		QueueFree();
	}
}
