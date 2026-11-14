using Godot;

/// <summary>
/// A trident in flight from the trident shooter: it leaves the muzzle for the point under the crosshair, flies fast
/// with a slight drop, turning slowly about its shaft, streaming tiny bubbles under water. It hits the first thing in
/// its way (traced each frame along its path, so it never skips through): an enemy takes the damage and the trident
/// sticks in it; a loose piece is struck and pinned; the world gets an impact and the trident stays stuck there.
/// Stuck tridents shrink away after a few seconds.
/// </summary>
public partial class TridentBolt : Node3D
{
	public const float Speed = 40f;           // m/s
	public const float Drop = 1.5f;           // m/s² downward (the water carries it, but not all the way)
	public const float MaxFlight = 2.5f;      // seconds before an unspent trident sinks away
	const float StuckFor = 4f;                // seconds a trident stays where it hit
	const float SinkIn = 0.2f;                // how far the points go in (metres)
	const float TipReach = 0.58f;             // the middle point's tip, ahead of the trident's centre (TridentModel)

	float damage;
	PlayerController shooter;
	PlayerWeapon weapon;
	Vector3 velocity;
	Vector3 cameraStart;
	bool checkedStart;
	bool stuck;
	float roll;
	TimeSince age;
	TimeSince sinceStuck;
	Vector3 stuckScale = Vector3.One;
	Godot.Collections.Array<Rid> exclude;

	/// <summary> Fires one from `muzzle` toward `target`. `cameraStart` is where the shot was aimed from (checked first, so a gun poked into a wall still hits the wall). </summary>
	public static TridentBolt Launch( Node parent, Vector3 muzzle, Vector3 target, Vector3 aim, Vector3 cameraStart, float damage, PlayerController shooter, PlayerWeapon weapon )
	{
		var direction = (target - muzzle).SafeNormal();
		// A target nearer than the muzzle (something right in your face) would send it backward: fly along the aim.
		if ( direction == Vector3.Zero || direction.Dot( aim ) < 0.2f ) direction = aim;
		var bolt = new TridentBolt
		{
			Name = "Trident",
			damage = damage,
			shooter = shooter,
			weapon = weapon,
			velocity = direction * Speed,
			cameraStart = cameraStart,
			exclude = new Godot.Collections.Array<Rid> { shooter.GetRid() },
		};
		parent.AddChild( bolt );
		bolt.GlobalPosition = muzzle;
		bolt.Face();
		TridentModel.BuildTrident( bolt );
		bolt.age = 0f;
		return bolt;
	}

	/// <summary> Points the trident (its -Z) along its flight, turned about the shaft by the roll. </summary>
	void Face()
	{
		var forward = velocity.SafeNormal();
		if ( forward == Vector3.Zero ) return;
		GlobalBasis = Shapes.LookAt( forward ) * new Basis( Vector3.Back, roll );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( stuck )
		{
			if ( sinceStuck > StuckFor )
			{
				var t = MathX.Clamp( (sinceStuck - StuckFor) / 0.5f, 0f, 1f );
				Scale = stuckScale * MathX.Lerp( 1f, 0.01f, t );
				if ( t >= 1f ) QueueFree();
			}
			return;
		}

		// First frame: anything between where the shot was aimed from and the muzzle (a wall the barrel pokes into).
		if ( !checkedStart )
		{
			checkedStart = true;
			var first = Trace.Ray( this, cameraStart, GlobalPosition, Layers.World | Layers.Creature | Layers.Gib, exclude );
			if ( first.Hit ) { Strike( first ); return; }
		}

		var from = GlobalPosition;
		velocity += Vector3.Down * Drop * dt;
		var to = from + velocity * dt;
		var tr = Trace.Ray( this, from, to, Layers.World | Layers.Creature | Layers.Gib, exclude );
		if ( tr.Hit ) { Strike( tr ); return; }
		GlobalPosition = to;
		roll += 5f * dt;
		Face();
		if ( UnderWater( to ) ) Bubbles( from, to );
		if ( age > MaxFlight ) QueueFree();
	}

	public static bool UnderWater( Vector3 at ) => WaterFeel.IsOn() && (Sea.SurfaceY is not float top || at.Y < top);

	/// <summary> A thin stream of tiny bubbles along this frame's path, rising and fading quickly. </summary>
	public static void Bubbles( Vector3 from, Vector3 to )
	{
		var length = from.DistanceTo( to );
		var count = Math.Clamp( (int)(length / 0.18f), 1, 8 );
		for ( var i = 0; i < count; i++ )
		{
			var at = from.Lerp( to, (i + Rng.Float()) / count ) + Rng.InSphere( 0.03f );
			WaterBubble.Create( at, Rng.Float( 0.18f, 0.4f ), 0f, Rng.Float( 0.5f, 1.1f ) );
		}
	}

	void Strike( TraceResult tr )
	{
		var direction = velocity.SafeNormal();
		var shootable = Shootables.Find( tr.Node );
		if ( shootable is not null && shootable.ShotOwner is not null )
		{
			var wasAlive = !shootable.IsDead;
			Fx.ImpactFlesh( tr.HitPosition, -direction, new Color( 0.9f, 0.3f, 0.2f ) );
			Sfx.Play( "hit_flesh", tr.HitPosition, 0.9f, 0.85f );
			if ( IsInstanceValid( shooter ) ) shootable.ReceiveShot( damage, tr.Node, direction, tr.HitPosition, cameraStart, shooter, true );
			if ( wasAlive && IsInstanceValid( weapon ) ) weapon.MarkHit();
		}
		else if ( tr.Node is Gib gib )
		{
			gib.TakeHit( tr.HitPosition, direction, damage, 260f );
		}
		else
		{
			Fx.ImpactWorld( tr.HitPosition, tr.Normal );
			Sfx.Play( "hit_world", tr.HitPosition, 0.8f, 0.8f );
		}
		StickAt( tr, direction );
	}

	/// <summary> Stays where it hit, points sunk in, carried along by what it hit if that moves (a creature, a piece). </summary>
	void StickAt( TraceResult tr, Vector3 direction )
	{
		stuck = true;
		sinceStuck = 0f;
		// The points a little way into what it hit.
		GlobalPosition = tr.HitPosition - direction * (TipReach - SinkIn);
		Face();
		if ( tr.Node is { } hit && IsInstanceValid( hit ) && hit.IsInsideTree() && (Shootables.Find( hit ) is not null || hit is Gib) )
			CallDeferred( MethodName.Pin, hit );
	}

	void Pin( Node3D to )
	{
		if ( !IsInstanceValid( to ) || !to.IsInsideTree() || !IsInsideTree() ) return;
		Reparent( to, true );
		stuckScale = Scale;   // keeping its size under a scaled part leaves a local scale: shrink from that
	}
}
