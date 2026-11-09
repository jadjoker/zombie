/// <summary>
/// A propeller jetpack for moving through water (no model yet, just the behaviour).
///
/// Hold Jump in the air to thrust upward. Thrust draws on a power cell; the motor can't run flat out forever, so
/// when the cell hits empty the propeller stalls (it starts to cavitate and chew bubbles instead of pushing) and
/// has to spin down and refill before it will fire again. The cell tops up while the motor idles, a little faster on
/// the ground where the water can wash over the housing.
///
/// Surge (mouse 4): hold to wind the propellers up (you hang in the water, curled, bubbles swirling), release to dump
/// whatever power is left into one hard burst along your view (or straight up if Jump is held). The fuller the cell
/// and the longer the wind-up, the faster and longer the burst. Anything you plough into takes a hit and is knocked
/// aside. It leaves the motor stalled until it recovers.
/// Local player only; sits next to the PlayerController.
/// </summary>
public sealed class Jetpack : Component
{
	[Property, Group( "Power" )] public float MaxPower { get; set; } = 100f;
	/// <summary> Power burned per second while thrusting. About 2.8 seconds of continuous thrust. </summary>
	[Property, Group( "Power" )] public float ThrustDrain { get; set; } = 36f;
	[Property, Group( "Power" )] public float Recharge { get; set; } = 22f;
	/// <summary> Pause after the last thrust before the cell starts topping up. </summary>
	[Property, Group( "Power" )] public float RechargeDelay { get; set; } = 0.5f;
	/// <summary> Ground recharge is this much faster than recharge in open water. </summary>
	[Property, Group( "Power" )] public float GroundRechargeBoost { get; set; } = 1.7f;
	/// <summary> After a stall the motor won't fire until the cell is back to this fraction. </summary>
	[Property, Group( "Power" )] public float RestartAt { get; set; } = 0.35f;

	[Property, Group( "Dock" )] public float DockRange { get; set; } = 200f;
	/// <summary> How far above an ally spider you can hover and still be topped up. </summary>
	[Property, Group( "Dock" )] public float DockHeight { get; set; } = 300f;
	[Property, Group( "Dock" )] public float DockRechargeBoost { get; set; } = 4.5f;

	[Property, Group( "Thrust" )] public float ThrustAccel { get; set; } = 900f;
	[Property, Group( "Thrust" )] public float MaxClimb { get; set; } = 230f;
	/// <summary> No thrust for this long after leaving the ground, so a normal jump still reads as a jump. </summary>
	[Property, Group( "Thrust" )] public float ThrustDelay { get; set; } = 0.16f;

	[Property, Group( "Surge" )] public float SurgeMinPower { get; set; } = 25f;
	[Property, Group( "Surge" )] public float SurgeSpeedMin { get; set; } = 650f;
	[Property, Group( "Surge" )] public float SurgeSpeedMax { get; set; } = 1550f;
	[Property, Group( "Surge" )] public float SurgeTimeMin { get; set; } = 0.22f;
	[Property, Group( "Surge" )] public float SurgeTimeMax { get; set; } = 0.75f;
	/// <summary> Seconds of holding Surge to wind up fully. A tap still launches, just weaker. </summary>
	[Property, Group( "Surge" )] public float ChargeTime { get; set; } = 0.8f;
	/// <summary> How long a full wind-up can be held before it launches by itself. </summary>
	[Property, Group( "Surge" )] public float ChargeHoldMax { get; set; } = 0.6f;
	[Property, Group( "Surge" )] public float RamDamage { get; set; } = 110f;
	[Property, Group( "Surge" )] public float RamRadius { get; set; } = 34f;

	/// <summary> The ally spider currently topping the cell up, if any. </summary>
	public SeaSpider DockedTo { get; private set; }

	public float Power { get; private set; }
	/// <summary> Stalled: out of power and waiting to restart. </summary>
	public bool Stalled { get; private set; }
	public bool Thrusting { get; private set; }
	public bool Surging => surgeLeft > 0f;
	/// <summary> Winding up a surge (Surge held). </summary>
	public bool Charging { get; private set; }
	/// <summary> How wound up the surge is, 0 to 1. </summary>
	public float Charge { get; private set; }
	/// <summary> 0 to 1 over the burst: 1 as it fires, 0 when it's done (for the speed lines). </summary>
	public float SurgeLeftFraction => surgeTotal > 0f ? MathX.Clamp( surgeLeft / surgeTotal, 0f, 1f ) : 0f;
	public float SurgeStrength { get; private set; }
	public Vector3 SurgeDirection => surgeDirection;
	/// <summary> Time since the last burst ended (the coast out of it shouldn't be clipped by the run speed cap). </summary>
	public TimeSince TimeSinceSurgeEnded { get; private set; } = 999f;
	public float PowerFraction => MaxPower > 0f ? Power / MaxPower : 0f;
	/// <summary> Set when a move was refused (stalled or too little power), so the HUD can flash. </summary>
	public TimeSince TimeSinceRefused { get; private set; } = 999f;

	/// <summary> Testing aids (scripts can't fake input): hold thrust, or press Surge once. </summary>
	public bool TestHold { get; set; }
	public void TestSurge( float holdSeconds = 0.5f ) { testSurge = true; testHold = holdSeconds; }
	bool testSurge;
	float testHold;
	TimeSince timeSinceChargeStart;
	TimeSince timeAtFullCharge;
	readonly HashSet<object> rammed = new();

	PlayerController Controller => GetComponent<PlayerController>();
	TimeSince timeSinceThrust = 999f;
	TimeSince timeSinceBubble;
	TimeSince timeSinceDockStrand;
	TimeSince timeSinceKick = 999f;
	float surgeLeft;
	float surgeTotal;
	float surgeSpeed;
	Vector3 surgeDirection;

	protected override void OnStart()
	{
		Power = MaxPower;
	}

	bool CanAct => !IsProxy && Controller.IsValid() && Controller.Body.IsValid()
		&& (GetComponent<PlayerSurvivor>()?.IsAlive ?? true) && !DevFlags.Noclip && !DockHouse.Diving;

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		if ( hitStopUntil > 0 && RealTime.Now >= hitStopUntil ) { Scene.TimeScale = 1f; hitStopUntil = 0; }
		if ( !CanAct ) { Thrusting = false; Charging = false; Charge = 0f; return; }
		var controller = Controller;
		var movement = GetComponent<PlayerMovement>();

		// Thrust: hold Jump in the air once the jump itself is underway.
		var airborne = !controller.IsOnGround && controller.TimeSinceGrounded > ThrustDelay;
		var wantThrust = (Input.Down( "Jump" ) || TestHold) && airborne && !(movement?.IsFloating ?? false) && !(movement?.IsMantling ?? false);
		Thrusting = wantThrust && !Stalled && Power > 0f && !Surging && !Charging;
		if ( wantThrust && Stalled && Input.Pressed( "Jump" ) ) TimeSinceRefused = 0f;

		UpdateSurgeInput();

		// Power: burns while thrusting, tops up while idle, stalls at empty and restarts once it has recovered.
		if ( Thrusting )
		{
			timeSinceThrust = 0f;
			Power = MathF.Max( 0f, Power - ThrustDrain * Time.Delta );
			if ( Power <= 0f ) { Stalled = true; TimeSinceRefused = 0f; }
		}
		else if ( timeSinceThrust > (DockedTo.IsValid() ? 0.1f : RechargeDelay) && !Surging && !Charging )
		{
			var rate = Recharge * (controller.IsOnGround ? GroundRechargeBoost : 1f) * (DockedTo.IsValid() ? DockRechargeBoost : 1f);
			Power = MathF.Min( MaxPower, Power + rate * Time.Delta );
			if ( Stalled && Power >= MaxPower * RestartAt ) Stalled = false;
		}

		UpdateDock();

		// Wake: a stream off the back while the propeller works (stalled and trying it chews bubbles instead).
		var rig = GetComponent<PlayerRig>();
		var jets = Thrusting || Surging || Charging;
		if ( rig.IsValid() && !DockHouse.Diving )
		{
			if ( rig.JetOn != jets ) rig.JetOn = jets;
			var charge = Charging ? Charge : 0f;
			if ( MathF.Abs( rig.SurgeCharge - charge ) > 0.02f || (charge == 0f && rig.SurgeCharge != 0f) ) rig.SurgeCharge = charge;
			if ( rig.SurgeOn != Surging ) rig.SurgeOn = Surging;
			if ( Surging && rig.SurgeDir != surgeDirection ) rig.SurgeDir = surgeDirection;
		}
		SurgeEffects();
		var stalling = wantThrust && Stalled;
		if ( (stalling || ((Thrusting || Surging) && !rig.IsValid())) && timeSinceBubble > (Surging ? 0.02f : 0.045f) )
		{
			timeSinceBubble = 0f;
			var yaw = Rotation.FromYaw( controller.EyeAngles.yaw );
			var back = WorldPosition + yaw * new Vector3( -14f, 0f, 36f ) + Game.Random.VectorInSphere( 4f );
			WaterBubble.Create( back, Game.Random.Float( 0.6f, stalling ? 2.2f : 1.5f ), 0f, Game.Random.Float( 0.8f, 1.6f ) );
		}
	}

	/// <summary>
	/// Near an ally spider the water around it carries a charge: it reaches up a glowing strand and tops the cell up
	/// fast, including from a stall, so staying close to it is how you keep flying.
	/// </summary>
	void UpdateDock()
	{
		SeaSpider best = null;
		var bestDistance = float.MaxValue;
		foreach ( var spider in Scene.GetAllComponents<SeaSpider>() )
		{
			if ( !spider.IsAlly || spider.IsDead ) continue;
			var d = spider.WorldPosition - WorldPosition;
			if ( d.WithZ( 0 ).Length > DockRange || d.z > 80f || d.z < -DockHeight ) continue;
			var length = d.Length;
			if ( length < bestDistance ) { best = spider; bestDistance = length; }
		}
		DockedTo = best;

		// Launch kick: drop onto the spider (falling, just above it) and it flicks you back up, free,
		// and jump-starts a stalled motor. Lets it catch you when the cell gives out over it.
		if ( best.IsValid() && timeSinceKick > 2.5f )
		{
			var body = Controller.Body;
			var offset = WorldPosition - best.WorldPosition;
			var height = offset.z;
			if ( offset.WithZ( 0 ).Length < 110f && height > 15f && height < 150f && body.Velocity.z < -60f && !Controller.IsOnGround )
			{
				timeSinceKick = 0f;
				body.Velocity = body.Velocity.WithZ( 0 ) * 0.6f + Vector3.Up * 520f;
				Controller.PreventGrounding( 0.2f );
				if ( Stalled ) { Stalled = false; Power = MathF.Max( Power, MaxPower * RestartAt ); }
				SpiderStrand.Create( best.LureTip, WorldPosition + Vector3.Up * 20f, new Color( 0.35f, 1f, 0.9f ), 0.35f, 1.4f );
				GetComponent<CameraJuice>()?.Shake( 0.15f );
				GetComponent<CameraJuice>()?.KickFov( 6f );
				for ( var i = 0; i < 8; i++ )
					WaterBubble.Create( WorldPosition + Game.Random.VectorInSphere( 12f ), Game.Random.Float( 0.8f, 2f ), i * 0.015f );
			}
		}

		if ( best.IsValid() && Power < MaxPower - 0.5f && timeSinceDockStrand > 0.45f )
		{
			timeSinceDockStrand = 0f;
			SpiderStrand.Create( best.LureTip, WorldPosition + Vector3.Up * 40f, new Color( 0.35f, 1f, 0.9f ), 0.4f, 0.7f );
		}
	}

	/// <summary> Press to start winding up, release to go. A full wind-up held too long goes by itself. </summary>
	void UpdateSurgeInput()
	{
		var pressed = Input.Pressed( "Surge" ) || testSurge;
		var released = Input.Released( "Surge" ) || (testSurge == false && testHold > 0f && timeSinceChargeStart > testHold);
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
		Charge = MathF.Min( 1f, Charge + Time.Delta / MathF.Max( 0.05f, ChargeTime ) );
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

		var controller = Controller;
		// A full cell and a full wind-up is the big one; a tap with a full cell is still a decent dash.
		var strength = PowerFraction * (0.45f + 0.55f * windUp);
		SurgeStrength = strength;
		rammed.Clear();
		var eyes = controller.EyeAngles.ToRotation();
		// Where you're looking. Hold Jump to go up instead, steering a little with the movement keys.
		var direction = eyes.Forward;
		if ( Input.Down( "Jump" ) || TestHold )
		{
			var steer = (eyes.Forward.WithZ( 0 ).Normal * Input.AnalogMove.x + eyes.Left.WithZ( 0 ).Normal * Input.AnalogMove.y) * 0.35f;
			direction = (Vector3.Up + steer).Normal;
		}
		// A burst along the ground gets a little lift so it can leave it.
		if ( controller.IsOnGround && direction.z < 0.15f ) direction = (direction.WithZ( 0 ).Normal + Vector3.Up * 0.18f).Normal;

		surgeDirection = direction;
		surgeSpeed = MathX.Lerp( SurgeSpeedMin, SurgeSpeedMax, strength );
		surgeTotal = surgeLeft = MathX.Lerp( SurgeTimeMin, SurgeTimeMax, strength );
		Power = 0f;
		Stalled = true;
		timeSinceThrust = 0f;
		controller.PreventGrounding( 0.12f );

		GetComponent<CameraJuice>()?.Shake( 0.25f + 0.4f * strength );
		GetComponent<CameraJuice>()?.KickFov( 14f + 22f * strength );
		// A shock ring of bubbles where it launched from.
		var ring = Rotation.LookAt( direction );
		for ( var i = 0; i < 18; i++ )
		{
			var a = i * MathF.PI * 2f / 18f;
			var p = WorldPosition + Vector3.Up * 34f + ring * new Vector3( 0f, MathF.Cos( a ), MathF.Sin( a ) ) * 16f;
			WaterBubble.Create( p, Game.Random.Float( 0.9f, 2.4f ), i * 0.006f );
		}
	}

	TimeSince timeSinceSurgeFx;

	/// <summary> Bubbles: swirling round you as it winds up, and a thick wake behind the burst. </summary>
	void SurgeEffects()
	{
		if ( timeSinceSurgeFx < 0.02f ) return;
		timeSinceSurgeFx = 0f;
		var center = WorldPosition + Vector3.Up * 36f;
		if ( Charging )
		{
			// Two arms of a spiral, tightening and speeding up with the wind-up.
			var spin = Time.Now * (6f + 10f * Charge);
			for ( var k = 0; k < 2; k++ )
			{
				var a = spin + k * MathF.PI;
				var r = MathX.Lerp( 34f, 18f, Charge );
				var p = center + new Vector3( MathF.Cos( a ) * r, MathF.Sin( a ) * r, Game.Random.Float( -30f, 20f ) );
				WaterBubble.Create( p, Game.Random.Float( 0.4f, 0.9f + Charge ), 0f, Game.Random.Float( 0.5f, 0.9f ) );
			}
		}
		else if ( Surging )
		{
			var back = -surgeDirection;
			for ( var k = 0; k < 3; k++ )
				WaterBubble.Create( center + back * Game.Random.Float( 10f, 40f ) + Game.Random.VectorInSphere( 12f ), Game.Random.Float( 0.8f, 2.2f + SurgeStrength ), 0f, Game.Random.Float( 0.8f, 1.6f ) );
		}
	}

	// ---- The slam: hitting something at surge speed ------------------------------------------------------------

	[Property, Group( "Slam" )] public float SlamRadius { get; set; } = 150f;
	/// <summary> Damage to everything caught in the shockwave (full at the middle, nothing at the edge). </summary>
	[Property, Group( "Slam" )] public float SlamDamage { get; set; } = 90f;
	/// <summary> Below this speed a collision is just a bump. </summary>
	[Property, Group( "Slam" )] public float SlamMinSpeed { get; set; } = 550f;

	/// <summary> Time since the last slam (the HUD flashes, the camera punches). </summary>
	public TimeSince TimeSinceSlam { get; private set; } = 999f;
	public float LastSlamStrength { get; private set; }
	double hitStopUntil;

	static IEnumerable<Component> Creatures( Scene scene )
	{
		foreach ( var c in scene.GetAllComponents<JunkCrab>() ) if ( !c.IsDead ) yield return c;
		foreach ( var c in scene.GetAllComponents<Bloober>() ) if ( !c.IsDead ) yield return c;
		foreach ( var c in scene.GetAllComponents<Squilien>() ) if ( !c.IsDead ) yield return c;
		foreach ( var c in scene.GetAllComponents<MC61>() ) if ( !c.IsDead ) yield return c;
		foreach ( var c in scene.GetAllComponents<SeaSpider>() ) if ( !c.IsDead && !c.IsAlly ) yield return c;
	}

	/// <summary> The dive down from the dock starts: a fresh list of things already burst through, full strength. </summary>
	public void BeginDiveRam()
	{
		rammed.Clear();
		SurgeStrength = 1f;
	}

	/// <summary> The dive ploughing through enemies (walls are the dive's own business): returns what it burst, or null. </summary>
	public Component DiveRam( Vector3 velocity ) => DiveRam( velocity, out _ );

	/// <summary> The dive ploughing through enemies: everything it hits bursts (glanced is always false now). </summary>
	public Component DiveRam( Vector3 velocity, out bool glanced )
	{
		glanced = false;
		if ( velocity.Length < 1f ) return null;
		if ( !FindImpactBody( WorldPosition, velocity * Time.Delta + velocity.Normal * RamRadius, out var hitAt, out var enemy ) || !enemy.IsValid() ) return null;
		surgeDirection = velocity.Normal;

		Slam( hitAt, enemy, MathF.Max( velocity.Length, SurgeSpeedMax ) );
		return enemy;
	}

	/// <summary> Looks ahead along the burst: the first enemy or solid thing it's about to plough into, if any. </summary>
	/// <summary> Sweeps the whole body (a low probe and a high one), so small crabs on the seabed are hit as well as things at head height. </summary>
	bool FindImpactBody( Vector3 feet, Vector3 travel, out Vector3 point, out Component enemy )
	{
		var low = FindImpact( feet + Vector3.Up * 18f, feet + Vector3.Up * 18f + travel, out var lowPoint, out var lowEnemy );
		var high = FindImpact( feet + Vector3.Up * 50f, feet + Vector3.Up * 50f + travel, out var highPoint, out var highEnemy );
		// Prefer an enemy over a wall, then whichever is nearer.
		if ( low && high )
		{
			var useLow = (lowEnemy.IsValid() && !highEnemy.IsValid()) || (lowEnemy.IsValid() == highEnemy.IsValid() && lowPoint.Distance( feet ) <= highPoint.Distance( feet ));
			point = useLow ? lowPoint : highPoint;
			enemy = useLow ? lowEnemy : highEnemy;
			return true;
		}
		point = low ? lowPoint : highPoint;
		enemy = low ? lowEnemy : highEnemy;
		return low || high;
	}

	bool FindImpact( Vector3 from, Vector3 to, out Vector3 point, out Component enemy )
	{
		point = default;
		enemy = null;
		var best = float.MaxValue;

		// Creatures float about as triggers; zombies are found by their hitboxes.
		foreach ( var tr in Scene.Trace.Sphere( RamRadius, from, to ).HitTriggersOnly().WithTag( "creature" ).RunAll() )
		{
			var part = tr.GameObject?.Components.Get<CreaturePart>( FindMode.EverythingInSelfAndAncestors );
			if ( part?.Creature is null || tr.Distance >= best || rammed.Contains( part.Creature ) ) continue;
			if ( part.Creature is SeaSpider { IsAlly: true } ) continue;
			best = tr.Distance; point = tr.HitPosition; enemy = part.Creature;
		}
		foreach ( var tr in Scene.Trace.Sphere( RamRadius, from, to ).UseHitboxes().WithoutTags( "player", "viewmodel", "gib", "gore" ).RunAll() )
		{
			var zombie = tr.GameObject?.Components.Get<Zombie>( FindMode.EverythingInSelfAndAncestors );
			if ( zombie is null || zombie.IsDead || tr.Distance >= best || rammed.Contains( zombie ) ) continue;
			best = tr.Distance; point = tr.HitPosition; enemy = zombie;
		}
		// The world: a smaller probe so skimming past a rock doesn't count.
		var wall = Scene.Trace.Sphere( 12f, from, to ).WithoutTags( "player", "viewmodel", "gib", "gore", "creature", "trigger" ).IgnoreGameObjectHierarchy( GameObject ).Run();
		// Only a surface it's heading into, not one it's skimming along.
		if ( wall.Hit && !wall.StartedSolid && wall.Distance < best && Vector3.Dot( wall.Normal, (to - from).Normal ) < -0.45f && wall.GameObject?.Components.Get<Zombie>( FindMode.EverythingInSelfAndAncestors ) is null )
		{
			best = wall.Distance; point = wall.HitPosition; enemy = null;
		}
		return best < float.MaxValue;
	}

	/// <summary>
	/// Slamming into something at speed: whatever it hit directly bursts apart, everything round the impact takes a
	/// shockwave, and you bounce off it. Hitting a wall or the seabed at speed makes the same shockwave.
	/// </summary>
	void Slam( Vector3 at, Component direct, float speed )
	{
		var survivor = GetComponent<PlayerSurvivor>();
		var power = MathX.Clamp( (speed - SlamMinSpeed) / (SurgeSpeedMax - SlamMinSpeed), 0f, 1f );
		var strength = 0.5f + 0.5f * MathF.Max( power, SurgeStrength );
		LastSlamStrength = strength;
		TimeSinceSlam = 0f;
		var origin = at - surgeDirection * 20f;      // point blank, so kills burst (overkill)

		// The one it hit: every piece of it takes the full blow (the boss only gets a heavy hit).
		if ( direct.IsValid() )
		{
			var full = RamDamage * (0.8f + 0.8f * strength);
			if ( direct is Zombie zombie )
				zombie.Components.Get<Health>()?.OnDamage( new DamageInfo { Damage = full * 2f, Attacker = GameObject, Position = at, Origin = origin } );
			else
			{
				var parts = Scene.GetAllComponents<CreaturePart>().Where( p => p.Creature == direct && p.GameObject.IsValid() ).ToList();
				if ( direct is MC61 && parts.Count > 0 ) parts = new() { parts.OrderBy( p => p.GameObject.WorldPosition.Distance( at ) ).First() };
				// (Each hit can kill it and tear the rest apart, so check every piece is still there before hitting it.)
				var first = true;
				foreach ( var part in parts )
				{
					if ( !part.IsValid() || !part.GameObject.IsValid() || !direct.IsValid() ) continue;
					part.ReceiveShot( direct is MC61 ? full : full * 1.5f, part.GameObject, surgeDirection, part.GameObject.WorldPosition, origin, survivor, first );
					first = false;
				}
			}
		}

		// Shockwave: everything nearby, falling off with distance, flung outward.
		foreach ( var creature in Creatures( Scene ).ToList() )
		{
			if ( creature == direct || !creature.IsValid() ) continue;
			var d = creature.WorldPosition.Distance( at );
			if ( d > SlamRadius ) continue;
			var falloff = 1f - d / SlamRadius;
			var outward = (creature.WorldPosition - at).Normal;
			var part = Scene.GetAllComponents<CreaturePart>().Where( p => p.IsValid() && p.Creature == creature && p.GameObject.IsValid() ).OrderBy( p => p.GameObject.WorldPosition.Distance( at ) ).FirstOrDefault();
			if ( part.IsValid() ) part.ReceiveShot( SlamDamage * strength * falloff, part.GameObject, outward, part.GameObject.WorldPosition, at, survivor, false );
		}
		foreach ( var zombie in Scene.GetAllComponents<Zombie>().Where( z => !z.IsDead && z != direct ).ToList() )
		{
			var d = zombie.WorldPosition.Distance( at );
			if ( d > SlamRadius ) continue;
			zombie.Components.Get<Health>()?.OnDamage( new DamageInfo { Damage = SlamDamage * strength * (1f - d / SlamRadius), Attacker = GameObject, Position = zombie.WorldPosition, Origin = at } );
		}

		if ( GetComponent<PlayerRig>() is { } rig ) rig.PlaySlam( at, surgeDirection, strength );
		else SlamBubbles( at, surgeDirection, strength );

		var body = Controller.Body;
		if ( direct.IsValid() )
		{
			// Bust straight through it: it bursts around you and the burst carries on, a little slower,
			// so you can plough through a whole line of them.
			rammed.Add( direct );
			surgeSpeed *= 0.88f;
			if ( !Surging ) body.Velocity *= 0.88f;
			GetComponent<CameraJuice>()?.Shake( 0.3f + 0.3f * strength );
			GetComponent<CameraJuice>()?.KickFov( 6f + 6f * strength );
			// A short hit-stop, so each one you go through lands (solo only; it would freeze everyone online).
			if ( !Networking.IsActive ) { Scene.TimeScale = 0.12f; hitStopUntil = RealTime.Now + 0.035 + 0.03 * strength; }
			return;
		}

		// A wall or the seabed: bounce off it, back and up, most of the speed gone, the burst over.
		surgeLeft = 0f;
		TimeSinceSurgeEnded = 0f;
		body.Velocity = -surgeDirection * (180f + 120f * strength) + Vector3.Up * (200f + 120f * strength);
		Controller.PreventGrounding( 0.15f );
		GetComponent<CameraJuice>()?.Shake( 0.45f + 0.4f * strength );
		GetComponent<CameraJuice>()?.KickFov( -10f - 8f * strength );
		if ( !Networking.IsActive ) { Scene.TimeScale = 0.08f; hitStopUntil = RealTime.Now + 0.06 + 0.05 * strength; }
	}

	/// <summary> The burst of bubbles and the shock ring (PlayerRig.PlaySlam runs this on every machine). </summary>
	public static void SlamBubbles( Vector3 at, Vector3 direction, float strength )
	{
		var count = (int)(24 + 26 * strength);
		for ( var i = 0; i < count; i++ )
			WaterBubble.Create( at + Game.Random.VectorInSphere( 26f * strength ), Game.Random.Float( 1f, 3f ), Game.Random.Float( 0f, 0.05f ), Game.Random.Float( 0.8f, 1.8f ) );
		// A flat ring blasting outward across the impact.
		var ring = Rotation.LookAt( direction );
		for ( var i = 0; i < 28; i++ )
		{
			var a = i * MathF.PI * 2f / 28f;
			var outward = ring * new Vector3( 0f, MathF.Cos( a ), MathF.Sin( a ) );
			for ( var k = 1; k <= 3; k++ )
				WaterBubble.Create( at + outward * (18f + k * 26f * strength), Game.Random.Float( 0.6f, 1.6f ), k * 0.025f, Game.Random.Float( 0.5f, 1.1f ) );
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( !CanAct ) return;
		var controller = Controller;
		var body = controller.Body;

		if ( Charging && !controller.IsOnGround )
		{
			// Winding up in open water: it hangs there, the propellers holding it against its own weight.
			body.Velocity = body.Velocity * MathF.Exp( -5f * Time.Delta );
			return;
		}

		if ( Surging )
		{
			// Full speed at the start, easing off; the water takes the rest.
			var t = 1f - surgeLeft / MathF.Max( 0.01f, surgeTotal );
			var speed = surgeSpeed * (1f - 0.3f * t * t * t);   // holds its speed, then gives a little at the end
			if ( FindImpactBody( WorldPosition, surgeDirection * (speed * Time.Delta + RamRadius), out var hitAt, out var enemy ) )
			{
				if ( speed >= SlamMinSpeed ) { Slam( hitAt, enemy, speed ); if ( !enemy.IsValid() || !Surging ) return; }
				else
				{ surgeLeft = 0f; return; }   // too slow to slam: just stop against it
			}
			body.Velocity = surgeDirection * speed;
			controller.PreventGrounding( 0.05f );
			surgeLeft -= Time.Delta;
			if ( surgeLeft <= 0f ) TimeSinceSurgeEnded = 0f;
			if ( surgeLeft <= 0f ) body.Velocity = surgeDirection * MathF.Min( surgeSpeed * 0.6f, 850f );   // a long coast out of it
			return;
		}

		// Still flying fast just after the burst: it can slam into things on the coast too.
		if ( TimeSinceSurgeEnded < 0.4f && body.Velocity.Length >= SlamMinSpeed && TimeSinceSlam > 0.4f )
		{
			var v = body.Velocity;
			if ( FindImpactBody( WorldPosition, v * Time.Delta + v.Normal * RamRadius, out var hitAt, out var enemy ) && enemy.IsValid() )
			{
				surgeDirection = v.Normal;
				Slam( hitAt, enemy, v.Length );
				return;
			}
		}

		if ( Thrusting )
		{
			var velocity = body.Velocity;
			// Pushes harder when you're sinking, so it catches a fall quickly.
			var accel = ThrustAccel + (velocity.z < 0f ? 700f : 0f);
			velocity.z = MathF.Min( velocity.z + accel * Time.Delta, MaxClimb );
			body.Velocity = velocity;
		}
	}
}
