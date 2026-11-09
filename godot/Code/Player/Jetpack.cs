using Godot;

/// <summary>
/// Port of the s&box Jetpack: a propeller pack for moving through water.
///
/// Hold Jump in the air to thrust upward. Thrust draws on a power cell; when it hits empty the propeller stalls (it
/// chews bubbles instead of pushing) and has to refill before it fires again. The cell tops up while idle, faster on
/// the ground, and much faster docked under an ally sea spider, which also flicks you back up if you drop onto it.
///
/// Surge (mouse 4): hold to wind up (you hang in the water, bubbles swirling), release to dump the cell into one hard
/// burst along your view (or up if Jump is held). Bursting into enemies slams them (the one you hit bursts and you
/// carry on through it; everything near takes a shockwave); hitting a wall bounces you off. Numbers are the s&box ones.
/// Child of the PlayerController.
/// </summary>
public partial class Jetpack : Node
{
	public float MaxPower = 100f;
	public float ThrustDrain = 36f;
	public float Recharge = 22f;
	public float RechargeDelay = 0.5f;
	public float GroundRechargeBoost = 1.7f;
	public float RestartAt = 0.35f;
	public float DockRange = 200f;
	public float DockHeight = 300f;
	public float DockRechargeBoost = 4.5f;
	public float ThrustAccel = 900f;
	public float MaxClimb = 230f;
	public float ThrustDelay = 0.16f;
	public float SurgeMinPower = 25f;
	public float SurgeSpeedMin = 650f;
	public float SurgeSpeedMax = 1550f;
	public float SurgeTimeMin = 0.22f;
	public float SurgeTimeMax = 0.75f;
	public float ChargeTime = 0.8f;
	public float ChargeHoldMax = 0.6f;
	public float RamDamage = 110f;
	public float RamRadius = 34f;
	public float SlamRadius = 150f;
	public float SlamDamage = 90f;
	public float SlamMinSpeed = 550f;

	public SeaSpider DockedTo { get; private set; }
	public float Power { get; private set; }
	public bool Stalled { get; private set; }
	public bool Thrusting { get; private set; }
	public bool Surging => surgeLeft > 0f;
	public bool Charging { get; private set; }
	public float Charge { get; private set; }
	public float SurgeLeftFraction => surgeTotal > 0f ? MathX.Clamp( surgeLeft / surgeTotal, 0f, 1f ) : 0f;
	public float SurgeStrength { get; private set; }
	public Vector3 SurgeDirection => surgeDirection;
	public TimeSince TimeSinceSurgeEnded { get; private set; } = 999f;
	public float PowerFraction => MaxPower > 0f ? Power / MaxPower : 0f;
	public TimeSince TimeSinceRefused { get; private set; } = 999f;
	public TimeSince TimeSinceSlam { get; private set; } = 999f;
	public float LastSlamStrength { get; private set; }

	/// <summary> Testing aids: hold thrust, or press Surge once. </summary>
	public bool TestHold;
	public void TestSurge( float holdSeconds = 0.5f ) { testSurge = true; testHold = holdSeconds; }
	bool testSurge;
	float testHold;

	/// <summary> Set by the dock dive: the pack is off while it runs. </summary>
	public static bool Diving;

	PlayerController controller;
	PlayerMovement movement;
	CameraJuice Juice => controller.GetNodeOrNull<CameraJuice>( "CameraJuice" );
	TimeSince timeSinceChargeStart;
	TimeSince timeAtFullCharge;
	TimeSince timeSinceThrust = 999f;
	TimeSince timeSinceBubble;
	TimeSince timeSinceDockStrand;
	TimeSince timeSinceKick = 999f;
	TimeSince timeSinceSurgeFx;
	readonly HashSet<Node> rammed = new();
	float surgeLeft;
	float surgeTotal;
	float surgeSpeed;
	Vector3 surgeDirection;
	double hitStopUntil;

	Vector3 Feet => controller.GlobalPosition;

	public override void _Ready()
	{
		Name = "Jetpack";
		controller = GetParent<PlayerController>();
		movement = controller.GetNodeOrNull<PlayerMovement>( "PlayerMovement" );
		if ( movement is not null ) movement.SpeedCapLifted = () => Surging || TimeSinceSurgeEnded < 1.2f;
		Power = MaxPower;
		ProcessPhysicsPriority = -5;   // after the movement kit, before the controller moves
	}

	bool CanAct => (controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.IsAlive ?? true) && !DevFlags.Noclip && !Diving;

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( hitStopUntil > 0 && Clock.Real >= hitStopUntil ) { Engine.TimeScale = 1f; hitStopUntil = 0; }
		if ( !CanAct ) { Thrusting = false; Charging = false; Charge = 0f; return; }

		var airborne = !controller.IsOnGround && controller.TimeSinceGrounded > ThrustDelay;
		var wantThrust = (Input.IsActionPressed( "jump" ) || TestHold) && airborne && !(movement?.IsMantling ?? false);
		Thrusting = wantThrust && !Stalled && Power > 0f && !Surging && !Charging;
		if ( wantThrust && Stalled && Input.IsActionJustPressed( "jump" ) ) TimeSinceRefused = 0f;
		UpdateSurgeInput( dt );

		if ( Thrusting )
		{
			timeSinceThrust = 0f;
			Power = MathF.Max( 0f, Power - ThrustDrain * dt );
			if ( Power <= 0f ) { Stalled = true; TimeSinceRefused = 0f; }
		}
		else if ( timeSinceThrust > (DockedTo is not null ? 0.1f : RechargeDelay) && !Surging && !Charging )
		{
			var rate = Recharge * (controller.IsOnGround ? GroundRechargeBoost : 1f) * (DockedTo is not null ? DockRechargeBoost : 1f);
			Power = MathF.Min( MaxPower, Power + rate * dt );
			if ( Stalled && Power >= MaxPower * RestartAt ) Stalled = false;
		}

		UpdateDock();
		SurgeEffects();

		var stalling = wantThrust && Stalled;
		if ( (stalling || Thrusting || Surging) && timeSinceBubble > (Surging ? 0.02f : 0.045f) )
		{
			timeSinceBubble = 0f;
			var yaw = new Angles( 0f, controller.EyeAngles.Yaw, 0f ).ToBasis();
			var back = Feet + yaw * U.Sbox( -14f, 0f, 36f ) + Rng.InSphere( 4f * U.In );
			WaterBubble.Create( back, Rng.Float( 0.6f, stalling ? 2.2f : 1.5f ), 0f, Rng.Float( 0.8f, 1.6f ) );
		}
	}

	void UpdateDock()
	{
		SeaSpider best = null;
		var bestDistance = float.MaxValue;
		foreach ( var spider in SeaSpider.All )
		{
			if ( !spider.IsAlly || spider.IsDead ) continue;
			var d = (spider.GlobalPosition - Feet) / U.In;
			if ( d.Flat().Length() > DockRange || d.Y > 80f || d.Y < -DockHeight ) continue;
			var length = d.Length();
			if ( length < bestDistance ) { best = spider; bestDistance = length; }
		}
		DockedTo = best;

		if ( best is not null && timeSinceKick > 2.5f )
		{
			var offset = (Feet - best.GlobalPosition) / U.In;
			if ( offset.Flat().Length() < 110f && offset.Y > 15f && offset.Y < 150f && controller.Velocity.Y < -60f * U.In && !controller.IsOnGround )
			{
				timeSinceKick = 0f;
				controller.Velocity = controller.Velocity.Flat() * 0.6f + Vector3.Up * 520f * U.In;
				controller.PreventGrounding( 0.2f );
				if ( Stalled ) { Stalled = false; Power = MathF.Max( Power, MaxPower * RestartAt ); }
				SpiderStrand.Create( best.LureTip, Feet + Vector3.Up * 20f * U.In, new Color( 0.35f, 1f, 0.9f ), 0.35f, 1.4f );
				Juice?.Shake( 0.15f );
				Juice?.KickFov( 6f );
				for ( var i = 0; i < 8; i++ )
					WaterBubble.Create( Feet + Rng.InSphere( 12f * U.In ), Rng.Float( 0.8f, 2f ), i * 0.015f );
			}
		}
		if ( best is not null && Power < MaxPower - 0.5f && timeSinceDockStrand > 0.45f )
		{
			timeSinceDockStrand = 0f;
			SpiderStrand.Create( best.LureTip, Feet + Vector3.Up * 40f * U.In, new Color( 0.35f, 1f, 0.9f ), 0.4f, 0.7f );
		}
	}

	void UpdateSurgeInput( float dt )
	{
		var pressed = Input.IsActionJustPressed( "surge" ) || testSurge;
		var released = Input.IsActionJustReleased( "surge" ) || (!testSurge && testHold > 0f && timeSinceChargeStart > testHold);
		testSurge = false;
		if ( !Charging && pressed && !Surging )
		{
			if ( Stalled || Power < SurgeMinPower ) { TimeSinceRefused = 0f; testHold = 0f; return; }
			Charging = true;
			Charge = 0f;
			timeSinceChargeStart = 0f;
			return;
		}
		if ( !Charging ) return;
		var wasFull = Charge >= 1f;
		Charge = MathF.Min( 1f, Charge + dt / MathF.Max( 0.05f, ChargeTime ) );
		if ( Charge >= 1f && !wasFull ) timeAtFullCharge = 0f;
		var tooLong = Charge >= 1f && timeAtFullCharge > ChargeHoldMax;
		if ( released || tooLong )
		{
			Charging = false;
			testHold = 0f;
			TrySurge( Charge );
		}
	}

	void TrySurge( float windUp )
	{
		if ( Surging ) return;
		if ( Stalled || Power < SurgeMinPower ) { TimeSinceRefused = 0f; return; }
		var strength = PowerFraction * (0.45f + 0.55f * windUp);
		SurgeStrength = strength;
		rammed.Clear();
		var eyes = controller.EyeAngles;
		var direction = eyes.Forward;
		if ( Input.IsActionPressed( "jump" ) || TestHold )
		{
			var input = Input.GetVector( "left", "right", "backward", "forward" );
			var steer = (eyes.Forward.Flat().SafeNormal() * input.Y + eyes.Right.Flat().SafeNormal() * input.X) * 0.35f;
			direction = (Vector3.Up + steer).SafeNormal();
		}
		if ( controller.IsOnGround && direction.Y < 0.15f ) direction = (direction.Flat().SafeNormal() + Vector3.Up * 0.18f).SafeNormal();
		surgeDirection = direction;
		surgeSpeed = MathX.Lerp( SurgeSpeedMin, SurgeSpeedMax, strength );
		surgeTotal = surgeLeft = MathX.Lerp( SurgeTimeMin, SurgeTimeMax, strength );
		Power = 0f;
		Stalled = true;
		timeSinceThrust = 0f;
		controller.PreventGrounding( 0.12f );
		Juice?.Shake( 0.25f + 0.4f * strength );
		Juice?.KickFov( 14f + 22f * strength );
		var ring = Shapes.LookAt( direction );
		for ( var i = 0; i < 18; i++ )
		{
			var a = i * MathF.PI * 2f / 18f;
			var p = Feet + Vector3.Up * 34f * U.In + ring * new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f ) * 16f * U.In;
			WaterBubble.Create( p, Rng.Float( 0.9f, 2.4f ), i * 0.006f );
		}
	}

	void SurgeEffects()
	{
		if ( timeSinceSurgeFx < 0.02f ) return;
		timeSinceSurgeFx = 0f;
		var center = Feet + Vector3.Up * 36f * U.In;
		if ( Charging )
		{
			var spin = (float)Clock.Now * (6f + 10f * Charge);
			for ( var k = 0; k < 2; k++ )
			{
				var a = spin + k * MathF.PI;
				var r = MathX.Lerp( 34f, 18f, Charge ) * U.In;
				var p = center + new Vector3( MathF.Cos( a ) * r, Rng.Float( -30f, 20f ) * U.In, MathF.Sin( a ) * r );
				WaterBubble.Create( p, Rng.Float( 0.4f, 0.9f + Charge ), 0f, Rng.Float( 0.5f, 0.9f ) );
			}
		}
		else if ( Surging )
		{
			for ( var k = 0; k < 3; k++ )
				WaterBubble.Create( center - surgeDirection * Rng.Float( 10f, 40f ) * U.In + Rng.InSphere( 12f * U.In ), Rng.Float( 0.8f, 2.2f + SurgeStrength ), 0f, Rng.Float( 0.8f, 1.6f ) );
		}
	}

	// ---- Ramming ----

	/// <summary> The dock dive starts: nothing rammed yet, full strength. </summary>
	public void BeginDiveRam()
	{
		rammed.Clear();
		SurgeStrength = 1f;
	}

	/// <summary> The dive ploughing through enemies: everything it hits bursts. Velocity in m/s. </summary>
	public Node3D DiveRam( Vector3 velocity )
	{
		if ( velocity.Length() < 0.01f ) return null;
		if ( !FindImpactBody( Feet, velocity * (float)GetPhysicsProcessDeltaTime() + velocity.Normalized() * RamRadius * U.In, out var hitAt, out var enemy ) || enemy is null ) return null;
		surgeDirection = velocity.Normalized();
		Slam( hitAt, enemy, MathF.Max( velocity.Length() / U.In, SurgeSpeedMax ) );
		return enemy;
	}

	bool FindImpactBody( Vector3 feet, Vector3 travel, out Vector3 point, out Node3D enemy )
	{
		var low = FindImpact( feet + Vector3.Up * 18f * U.In, feet + Vector3.Up * 18f * U.In + travel, out var lowPoint, out var lowEnemy );
		var high = FindImpact( feet + Vector3.Up * 50f * U.In, feet + Vector3.Up * 50f * U.In + travel, out var highPoint, out var highEnemy );
		if ( low && high )
		{
			var useLow = (lowEnemy is not null && highEnemy is null) || ((lowEnemy is null) == (highEnemy is null) && lowPoint.DistanceTo( feet ) <= highPoint.DistanceTo( feet ));
			point = useLow ? lowPoint : highPoint;
			enemy = useLow ? lowEnemy : highEnemy;
			return true;
		}
		point = low ? lowPoint : highPoint;
		enemy = low ? lowEnemy : highEnemy;
		return low || high;
	}

	bool FindImpact( Vector3 from, Vector3 to, out Vector3 point, out Node3D enemy )
	{
		point = default;
		enemy = null;
		var best = float.MaxValue;
		// Creatures: sweep against their parts, skipping ones already rammed (and allies).
		var exclude = new Godot.Collections.Array<Rid> { controller.GetRid() };
		foreach ( var part in CreaturePart.All )
			if ( IsInstanceValid( part ) && (rammed.Contains( part.Creature ) || part.Creature is SeaSpider { IsAlly: true }) ) exclude.Add( part.GetRid() );
		var tr = Trace.Sphere( controller, RamRadius * U.In, from, to, Layers.Creature, exclude );
		if ( tr.Hit && Shootables.Find( tr.Node ) is CreaturePart part2 && !part2.IsDead )
		{
			best = tr.Distance; point = tr.HitPosition; enemy = part2.Creature;
		}
		// The world: a smaller probe so skimming past a rock doesn't count, and only a surface we're heading into.
		var wall = Trace.Sphere( controller, 12f * U.In, from, to, Layers.World, new Godot.Collections.Array<Rid> { controller.GetRid() } );
		if ( wall.Hit && !wall.StartedSolid && wall.Distance < best && wall.Normal.Dot( (to - from).SafeNormal() ) < -0.45f )
		{
			best = wall.Distance; point = wall.HitPosition; enemy = null;
		}
		return best < float.MaxValue;
	}

	/// <summary> Hitting something at surge speed (speed in s&box units/s). </summary>
	void Slam( Vector3 at, Node3D direct, float speed )
	{
		var survivor = controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
		var power = MathX.Clamp( (speed - SlamMinSpeed) / (SurgeSpeedMax - SlamMinSpeed), 0f, 1f );
		var strength = 0.5f + 0.5f * MathF.Max( power, SurgeStrength );
		LastSlamStrength = strength;
		TimeSinceSlam = 0f;
		var origin = at - surgeDirection * 20f * U.In;

		if ( direct is not null )
		{
			var full = RamDamage * (0.8f + 0.8f * strength);
			var parts = CreaturePart.All.Where( p => IsInstanceValid( p ) && p.Creature == direct ).ToList();
			if ( direct is MC61Marker && parts.Count > 0 ) parts = new() { parts.OrderBy( p => p.GlobalPosition.DistanceTo( at ) ).First() };
			var first = true;
			foreach ( var part in parts )
			{
				if ( !IsInstanceValid( part ) || !IsInstanceValid( direct ) ) continue;
				part.ReceiveShot( direct is MC61Marker ? full : full * 1.5f, part, surgeDirection, part.GlobalPosition, origin, controller, first );
				first = false;
			}
		}

		foreach ( var creature in CreatureRegistry.Alive().ToList() )
		{
			if ( creature == direct || !IsInstanceValid( creature ) ) continue;
			var d = creature.GlobalPosition.DistanceTo( at ) / U.In;
			if ( d > SlamRadius ) continue;
			var falloff = 1f - d / SlamRadius;
			var outward = (creature.GlobalPosition - at).SafeNormal();
			var part = CreaturePart.All.Where( p => IsInstanceValid( p ) && p.Creature == creature ).OrderBy( p => p.GlobalPosition.DistanceTo( at ) ).FirstOrDefault();
			part?.ReceiveShot( SlamDamage * strength * falloff, part, outward, part.GlobalPosition, at, controller, false );
		}

		SlamBubbles( at, surgeDirection, strength );

		if ( direct is not null )
		{
			// Bust straight through it, a little slower.
			rammed.Add( direct );
			surgeSpeed *= 0.88f;
			if ( !Surging ) controller.Velocity *= 0.88f;
			Juice?.Shake( 0.3f + 0.3f * strength );
			Juice?.KickFov( 6f + 6f * strength );
			Engine.TimeScale = 0.12f;
			hitStopUntil = Clock.Real + 0.035 + 0.03 * strength;
			return;
		}

		// A wall or the seabed: bounce off, back and up, the burst over.
		surgeLeft = 0f;
		TimeSinceSurgeEnded = 0f;
		controller.Velocity = (-surgeDirection * (180f + 120f * strength) + Vector3.Up * (200f + 120f * strength)) * U.In;
		controller.PreventGrounding( 0.15f );
		Juice?.Shake( 0.45f + 0.4f * strength );
		Juice?.KickFov( -10f - 8f * strength );
		Engine.TimeScale = 0.08f;
		hitStopUntil = Clock.Real + 0.06 + 0.05 * strength;
	}

	public static void SlamBubbles( Vector3 at, Vector3 direction, float strength )
	{
		var count = (int)(24 + 26 * strength);
		for ( var i = 0; i < count; i++ )
			WaterBubble.Create( at + Rng.InSphere( 26f * strength * U.In ), Rng.Float( 1f, 3f ), Rng.Float( 0f, 0.05f ), Rng.Float( 0.8f, 1.8f ) );
		var ring = Shapes.LookAt( direction == Vector3.Zero ? Vector3.Forward : direction );
		for ( var i = 0; i < 28; i++ )
		{
			var a = i * MathF.PI * 2f / 28f;
			var outward = ring * new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f );
			for ( var k = 1; k <= 3; k++ )
				WaterBubble.Create( at + outward * (18f + k * 26f * strength) * U.In, Rng.Float( 0.6f, 1.6f ), k * 0.025f, Rng.Float( 0.5f, 1.1f ) );
		}
	}

	public override void _PhysicsProcess( double delta )
	{
		if ( !CanAct ) return;
		var dt = (float)delta;
		if ( Charging && !controller.IsOnGround )
		{
			controller.Velocity *= MathF.Exp( -5f * dt );
			controller.GravityScale = 0f;
			return;
		}
		if ( Surging )
		{
			var t = 1f - surgeLeft / MathF.Max( 0.01f, surgeTotal );
			var speed = surgeSpeed * (1f - 0.3f * t * t * t);
			if ( FindImpactBody( Feet, surgeDirection * (speed * dt + RamRadius) * U.In, out var hitAt, out var enemy ) )
			{
				if ( speed >= SlamMinSpeed ) { Slam( hitAt, enemy, speed ); if ( enemy is null || !Surging ) return; }
				else { surgeLeft = 0f; return; }
			}
			controller.Velocity = surgeDirection * speed * U.In;
			controller.GravityScale = 0f;
			controller.PreventGrounding( 0.05f );
			surgeLeft -= dt;
			if ( surgeLeft <= 0f )
			{
				TimeSinceSurgeEnded = 0f;
				controller.Velocity = surgeDirection * MathF.Min( surgeSpeed * 0.6f, 850f ) * U.In;
			}
			return;
		}
		if ( TimeSinceSurgeEnded < 0.4f && controller.Velocity.Length() / U.In >= SlamMinSpeed && TimeSinceSlam > 0.4f )
		{
			var v = controller.Velocity;
			if ( FindImpactBody( Feet, v * dt + v.Normalized() * RamRadius * U.In, out var hitAt, out var enemy ) && enemy is not null )
			{
				surgeDirection = v.Normalized();
				Slam( hitAt, enemy, v.Length() / U.In );
				return;
			}
		}
		if ( Thrusting )
		{
			var v = controller.Velocity;
			var accel = ThrustAccel + (v.Y < 0f ? 700f : 0f);
			v.Y = MathF.Min( v.Y + accel * U.In * dt, MaxClimb * U.In );
			controller.Velocity = v;
		}
	}
}

/// <summary> Marks the boss (MC61 only takes the slam on the part it hit). Implemented by MC61 when it's ported. </summary>
public interface MC61Marker { }

/// <summary> Every living enemy creature, for shockwaves and slams. Each creature kind adds its list here. </summary>
public static class CreatureRegistry
{
	public static IEnumerable<Node3D> Alive()
	{
		foreach ( var b in Bloober.All ) if ( !b.IsDead ) yield return b;
		foreach ( var s in SeaSpider.All ) if ( !s.IsDead && !s.IsAlly ) yield return s;
		foreach ( var q in Squilien.All ) if ( !q.IsDead ) yield return q;
		foreach ( var c in JunkCrab.All ) if ( !c.IsDead ) yield return c;
	}
}
