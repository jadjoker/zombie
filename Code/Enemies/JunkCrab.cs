/// <summary>
/// Junk Crab: a sea-floor crab hyper-evolved on alien waste, wearing a dumped toxic barrel as its shell.
/// It scuttles sideways across the floor at you and snaps with its claws.
///
/// Chunking: the barrel is armour on its back; shoot it off and the soft body underneath takes extra
/// damage. Every leg can be shot off (it slows, and drags itself once a side is gone); each claw comes
/// off at the shoulder or the pincer (no claws left = a feeble bite); the eye stalks are crit spots,
/// and with both popped it's blind and stumbles about. The body is lethal.
///
/// Networking: the host walks it on the navmesh (the networked root); every machine builds and animates
/// the rig from the synced velocity and state. Cuts are broadcast; a mask lets late joiners catch up.
/// </summary>
public sealed class JunkCrab : Component, ICreature
{
	[Property, Group( "Movement" )] public float MoveSpeed { get; set; } = 210f;
	[Property, Group( "Attack" )] public float AttackRange { get; set; } = 70f;
	[Property, Group( "Attack" )] public float ClawDamage { get; set; } = 14f;
	[Property, Group( "Attack" )] public float AttackCooldown { get; set; } = 1.1f;

	[Property, Group( "Damage" )] public float BarrelHealth { get; set; } = 80f;
	[Property, Group( "Damage" )] public float LimbHealth { get; set; } = 20f;
	[Property, Group( "Damage" )] public float EyeHealth { get; set; } = 20f;
	/// <summary> Body damage once the barrel's gone. </summary>
	[Property, Group( "Damage" )] public float SoftMultiplier { get; set; } = 1.6f;
	[Property, Group( "Damage" )] public float EyeMultiplier { get; set; } = 0.6f;
	/// <summary> How much a limb hit hurts the whole crab. Low, so you can strip it before it dies. </summary>
	[Property, Group( "Damage" )] public float LimbMultiplier { get; set; } = 0.15f;

	/// <summary> Wear a dumped waste barrel as armour on its back. Off for now. </summary>
	[Property, Group( "Body" )] public bool WearsBarrel { get; set; } = false;

	[Sync] public float Hue { get; set; } = -1f;
	[Sync] public float Size { get; set; } = 1f;
	[Sync] public Vector3 NetVelocity { get; set; }
	[Sync] public bool Snapping { get; set; }
	[Sync] public int GoneMask { get; set; }

	public bool IsDead => health?.IsDead ?? false;
	public event System.Action<JunkCrab> Killed;

	const int Body = 0, Barrel = 1, EyeL = 2, EyeR = 3, ArmL = 4, PincerL = 5, ArmR = 6, PincerR = 7, FirstLeg = 8, LegCount = 6;

	sealed class Part
	{
		public int Index;
		public int Parent = -1;
		public GameObject Go;
		public float Health;
		public float MaxHealth;
		public bool Gone;
		public float Hurt;
		public TimeSince Flash = 99f;
		public readonly List<(ModelRenderer Renderer, Color Tint)> Renderers = new();
		public Vector3 LastPosition;
		public Vector3 Velocity;
		public Rotation Rest = Rotation.Identity;
	}

	readonly Part[] parts = new Part[FirstLeg + LegCount];
	GameObject rig;
	GameObject jawL, jawR;
	Health health;
	GoreSystem gore;
	NavMeshAgent agent;

	Color shellColor, bellyColor, gooColor;
	float sideSign = 1f;
	TimeSince timeSinceAttack = 999f;
	TimeSince timeSinceRepath = 999f;
	TimeSince timeSinceSnap = 999f;
	Vector3 wanderGoal;
	bool diedLocally;
	TimeSince timeSinceDeath;
	Vector3 lastShotDirection = Vector3.Forward;
	TimeSince bleedTimer;
	bool lastShotPointBlank;
	float gait;

	// ---- Setup ------------------------------------------------------------

	protected override void OnStart()
	{
		health = GetComponent<Health>();
		gore = GoreSystem.Get( Scene );
		agent = GetComponent<NavMeshAgent>();

		if ( !IsProxy && Hue < 0f ) Hue = Game.Random.Float();
		var hue = Hue < 0f ? 0.05f : Hue;
		// Crab colours: reds, oranges and hot pinks, with a toxic barrel on top.
		shellColor = Shapes.Hsv( MathX.Lerp( -0.08f, 0.1f, hue ), 0.85f, 1f );
		bellyColor = Shapes.Hsv( MathX.Lerp( -0.05f, 0.12f, hue ), 0.35f, 1f );
		gooColor = Shapes.Hsv( 0.3f + hue * 0.1f, 1f, 1f );
		sideSign = hue < 0.5f ? 1f : -1f;

		if ( agent.IsValid() )
		{
			agent.UpdateRotation = false;
			if ( IsProxy ) agent.Enabled = false;
		}
		if ( health.IsValid() && !IsProxy ) health.Died += OnDied;
		Build( hue );
	}

	void Build( float hue )
	{
		rig = new GameObject( true, "Crab Rig" );
		rig.NetworkMode = NetworkMode.Never;
		rig.WorldTransform = WorldTransform;
		rig.WorldScale = Size;

		var body = AddPart( Body, -1, rig, "Body", new Vector3( 0, 0, 14 ), Angles.Zero, 0f );
		Sphere( body, Vector3.Zero, new Vector3( 15f, 19f, 8.5f ), shellColor );
		Sphere( body, new Vector3( 0, 0, -3.5f ), new Vector3( 13.5f, 17f, 5f ), bellyColor );
		Sphere( body, new Vector3( 13.5f, 0, -1f ), new Vector3( 2.5f, 6f, 1.6f ), new Color( 0.15f, 0.02f, 0.05f ) ); // mouth
		Sphere( body, new Vector3( 14.2f, 0, -1.4f ), new Vector3( 1.2f, 3.2f, 0.7f ), new Color( 1f, 0.45f, 0.55f ) ); // tongue

		// Shell detail: a darker crown, a ridged rim, and knobbly bumps.
		var crown = Color.Lerp( shellColor, Color.Black, 0.22f );
		var knob = Color.Lerp( shellColor, Color.White, 0.25f );
		Sphere( body, new Vector3( -1f, 0, 3.2f ), new Vector3( 11f, 14.5f, 5.6f ), crown );
		Sphere( body, new Vector3( 0, 0, -0.5f ), new Vector3( 15.6f, 19.6f, 3.2f ), Color.Lerp( shellColor, Color.Black, 0.1f ) );
		var bumps = new System.Random( (int)(hue * 9973f) );
		for ( var k = 0; k < 7; k++ )
		{
			var a = (float)bumps.NextDouble() * MathF.PI * 2f;
			var d = 3f + (float)bumps.NextDouble() * 7f;
			var at = new Vector3( MathF.Cos( a ) * d * 0.8f - 1f, MathF.Sin( a ) * d, 9.2f - d * 0.3f );
			var r = 1.3f + (float)bumps.NextDouble() * 1.4f;
			Sphere( body, at, new Vector3( r, r, r * 0.7f ), knob );
		}
		// Cheek spots by the eyes.
		Sphere( body, new Vector3( 12f, 9f, 1.5f ), new Vector3( 1.2f, 2.4f, 1.6f ), Color.Lerp( shellColor, Color.White, 0.45f ) );
		Sphere( body, new Vector3( 12f, -9f, 1.5f ), new Vector3( 1.2f, 2.4f, 1.6f ), Color.Lerp( shellColor, Color.White, 0.45f ) );

		// Flat like the body, so it doesn't swallow shots aimed at things on top of it.
		Trigger( body, new Vector3( -7, -8, -1 ), new Vector3( -7, 8, -1 ), 8f );
		Trigger( body, new Vector3( 5, -8, -1 ), new Vector3( 5, 8, -1 ), 8f );

		if ( WearsBarrel ) BuildBarrel( body, hue );
		BuildLimbs( body );
	}

	/// <summary> The barrel: a dumped drum of alien waste, glowing band and all. </summary>
	void BuildBarrel( Part body, float hue )
	{
		var barrel = AddPart( Barrel, Body, body.Go, "Barrel", new Vector3( -2f, 0, 7f ), new Angles( 0, 0, 90 ), BarrelHealth );
		var drum = Shapes.Hsv( 0.48f + hue * 0.1f, 0.6f, 0.75f );
		Box( barrel, new Vector3( 0, 0, 0 ), new Vector3( 18f, 15f, 26f ), drum );
		Box( barrel, new Vector3( 0, 0, 0 ), new Vector3( 18.6f, 15.6f, 4f ), new Color( 0.4f, 1f, 0.2f ) * 1.6f );
		Box( barrel, new Vector3( 0, 0, 12.5f ), new Vector3( 17f, 14f, 1.5f ), new Color( 0.75f, 0.75f, 0.8f ) );
		Box( barrel, new Vector3( 0, 0, -12.5f ), new Vector3( 17f, 14f, 1.5f ), new Color( 0.75f, 0.75f, 0.8f ) );
		Trigger( barrel, new Vector3( 0, 0, -12 ), new Vector3( 0, 0, 12 ), 11f );
	}

	void BuildLimbs( Part body )
	{
		// Eyes on stalks.
		foreach ( var (index, side) in new[] { (EyeL, 1f), (EyeR, -1f) } )
		{
			var eye = AddPart( index, Body, body.Go, side > 0 ? "Eye L" : "Eye R", new Vector3( 11f, side * 5f, 5f ), new Angles( -10, 0, -side * 12 ), EyeHealth );
			Sphere( eye, new Vector3( 0, 0, 5f ), new Vector3( 1.3f, 1.3f, 5.5f ), bellyColor );
			Sphere( eye, new Vector3( 0, 0, 11f ), new Vector3( 3.4f, 3.4f, 3.4f ), Color.White );
			Sphere( eye, new Vector3( 2.6f, 0, 11.5f ), new Vector3( 1.2f, 1.8f, 1.8f ), Color.Black );
			Sphere( eye, new Vector3( 3.3f, 0.6f, 12.3f ), new Vector3( 0.4f, 0.55f, 0.55f ), Color.White ); // glint
			Sphere( eye, new Vector3( 0.4f, 0, 13.2f ), new Vector3( 3.5f, 3.5f, 1.2f ), Color.Lerp( shellColor, Color.Black, 0.2f ) ); // lid
			Trigger( eye, new Vector3( 0, 0, 3 ), new Vector3( 0, 0, 12 ), 4f );
		}

		// Claws: an arm, then the pincer with a moving jaw.
		foreach ( var (arm, pincer, side) in new[] { (ArmL, PincerL, 1f), (ArmR, PincerR, -1f) } )
		{
			var a = AddPart( arm, Body, body.Go, side > 0 ? "Arm L" : "Arm R", new Vector3( 10f, side * 13f, 0f ), new Angles( 0, side * 35f, 0 ), LimbHealth );
			Sphere( a, new Vector3( 7f, 0, 0 ), new Vector3( 8f, 3.2f, 3.2f ), shellColor );
			Trigger( a, new Vector3( 1, 0, 0 ), new Vector3( 13, 0, 0 ), 4.5f );

			var p = AddPart( pincer, arm, a.Go, side > 0 ? "Pincer L" : "Pincer R", new Vector3( 14f, 0, 0 ), new Angles( 0, -side * 40f, 0 ), LimbHealth );
			Sphere( p, new Vector3( 5f, 0, 0 ), new Vector3( 7f, 4.5f, 5.5f ), shellColor );
			Sphere( p, new Vector3( 13f, 0, -2f ), new Vector3( 6f, 2.2f, 1.8f ), shellColor );
			Sphere( p, new Vector3( 5f, 0, 1.5f ), new Vector3( 5f, 3.6f, 3.2f ), Color.Lerp( shellColor, Color.White, 0.3f ) ); // lighter palm
			Sphere( p, new Vector3( 14.5f, 0, -0.6f ), new Vector3( 3.5f, 1.4f, 0.8f ), Color.Lerp( shellColor, Color.Black, 0.35f ) ); // dark tip
			var jaw = new GameObject( p.Go, true, "Jaw" );
			jaw.LocalPosition = new Vector3( 9f, 0, 2f );
			Sphere( jaw, p, new Vector3( 5f, 0, 0.8f ), new Vector3( 6f, 2f, 1.6f ), shellColor );
			if ( side > 0 ) jawL = jaw; else jawR = jaw;
			Trigger( p, new Vector3( 1, 0, 0 ), new Vector3( 15, 0, 0 ), 6f );
		}

		// Three spindly legs a side.
		for ( var i = 0; i < LegCount; i++ )
		{
			var side = i < 3 ? 1f : -1f;
			var row = i % 3;
			var leg = AddPart( FirstLeg + i, Body, body.Go, $"Leg {i}", new Vector3( 6f - row * 7f, side * 15f, -1f ), new Angles( 0, side * (75f + row * 22f), 0 ), LimbHealth );
			Sphere( leg, new Vector3( 7f, 0, 3f ), new Vector3( 8f, 2.2f, 2.2f ), shellColor );
			Sphere( leg, new Vector3( 17f, 0, -5f ), new Vector3( 2f, 2f, 9f ), shellColor );
			Sphere( leg, new Vector3( 15f, 0, 3f ), new Vector3( 2.6f, 2.6f, 2.6f ), Color.Lerp( shellColor, Color.White, 0.2f ) ); // knee
			Sphere( leg, new Vector3( 17f, 0, -12f ), new Vector3( 1.4f, 1.4f, 3.2f ), Color.Lerp( shellColor, Color.Black, 0.4f ) ); // dark tip
			Trigger( leg, new Vector3( 1, 0, 2 ), new Vector3( 17, 0, -10 ), 4f );
		}

		foreach ( var part in parts )
			if ( part is not null ) part.Rest = part.Go.LocalRotation;
	}

	Part AddPart( int index, int parent, GameObject parentGo, string name, Vector3 position, Angles angles, float hp )
	{
		var go = new GameObject( parentGo, true, $"Crab {name}" );
		go.LocalPosition = position;
		go.LocalRotation = angles.ToRotation();
		go.Tags.Add( "creature" );
		var part = new Part { Index = index, Parent = parent, Go = go, Health = hp, MaxHealth = hp };
		parts[index] = part;
		var shootable = go.AddComponent<CreaturePart>();
		shootable.Creature = this;
		shootable.Index = index;
		return part;
	}

	void Sphere( Part part, Vector3 at, Vector3 radii, Color color ) => Sphere( part.Go, part, at, radii, color );

	void Sphere( GameObject parent, Part part, Vector3 at, Vector3 radii, Color color )
	{
		var go = new GameObject( parent, true, "Shape" );
		go.LocalPosition = at;
		go.LocalScale = radii * Shapes.SphereScale( 1f );
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Shapes.Sphere;
		r.Tint = color;
		part.Renderers.Add( (r, color) );
	}

	void Box( Part part, Vector3 at, Vector3 size, Color color )
	{
		var go = new GameObject( part.Go, true, "Shape" );
		var scale = Shapes.BoxScale( size );
		go.LocalPosition = at - Shapes.Box.Bounds.Center * scale;
		go.LocalScale = scale;
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Shapes.Box;
		r.Tint = color;
		part.Renderers.Add( (r, color) );
	}

	void Trigger( Part part, Vector3 from, Vector3 to, float radius )
	{
		var c = part.Go.AddComponent<CapsuleCollider>();
		c.IsTrigger = true;
		c.Start = from;
		c.End = to;
		c.Radius = radius;
	}

	protected override void OnDestroy()
	{
		if ( rig.IsValid() ) rig.Destroy();
	}

	// ---- Every frame --------------------------------------------------------

	bool Has( int index ) => parts[index] is { Gone: false };
	int LegsLeft( float side ) => Enumerable.Range( 0, LegCount ).Count( i => (i < 3 ? 1f : -1f) == side && Has( FirstLeg + i ) );
	int LegsLost => LegCount - LegsLeft( 1f ) - LegsLeft( -1f );
	bool Dragging => LegsLeft( 1f ) <= 1 || LegsLeft( -1f ) <= 1;
	bool Blind => !Has( EyeL ) && !Has( EyeR );
	int ClawsLeft => (Has( PincerL ) ? 1 : 0) + (Has( PincerR ) ? 1 : 0);

	protected override void OnUpdate()
	{
		if ( IsProxy ) CatchUp();
		else if ( IsDead )
		{
			if ( timeSinceDeath > 1f ) GameObject.Destroy();
			return;
		}
		else Think();

		if ( diedLocally || !rig.IsValid() ) return;
		rig.WorldPosition = WorldPosition;
		rig.WorldRotation = WorldRotation;
		if ( Has( Body ) && health.IsValid() )
			CreatureGore.BleedTrail( Scene, parts[Body].Go.WorldPosition, gooColor, health.Fraction, ref bleedTimer );
		Animate();
		Tint();
	}

	void Animate()
	{
		var velocity = NetVelocity.WithZ( 0 );
		var speed = velocity.Length;
		gait += Time.Delta * MathX.Clamp( speed / 18f, 0f, 14f );
		var moving = MathX.Clamp( speed / 60f, 0f, 1f );

		// Body: bobs as it scuttles; dragging, it lists toward the missing side.
		var body = parts[Body].Go;
		var list = 0f;
		if ( Dragging ) list = (LegsLeft( 1f ) < LegsLeft( -1f ) ? 1f : -1f) * 14f;
		body.LocalPosition = new Vector3( 0, 0, 14f + MathF.Abs( MathF.Sin( gait ) ) * 2f * moving - (Dragging ? 5f : 0f) );
		body.LocalRotation = new Angles( MathF.Sin( gait * 0.5f ) * 3f * moving, 0, list + MathF.Sin( gait ) * 3f * moving ).ToRotation();

		// Legs step in alternating sets.
		for ( var i = 0; i < LegCount; i++ )
		{
			var part = parts[FirstLeg + i];
			if ( part is null || part.Gone ) continue;
			var phase = gait + (i % 2 == 0 ? 0f : MathF.PI) + i * 0.4f;
			var swing = MathF.Sin( phase ) * 22f * moving;
			var lift = MathF.Max( 0f, MathF.Cos( phase ) ) * 18f * moving;
			part.Go.LocalRotation = part.Rest * new Angles( -lift, swing, 0 ).ToRotation();
		}

		// Claws raise and snap.
		var snap = Snapping || timeSinceSnap < 0.25f;
		foreach ( var (arm, jaw, side) in new[] { (ArmL, jawL, 1f), (ArmR, jawR, -1f) } )
		{
			if ( Has( arm ) )
			{
				var raise = snap ? -35f : -8f + MathF.Sin( gait * 0.5f + side ) * 6f;
				parts[arm].Go.LocalRotation = parts[arm].Rest * new Angles( raise, 0, 0 ).ToRotation();
			}
			if ( jaw.IsValid() )
			{
				var open = snap ? (MathF.Sin( Time.Now * 40f ) > 0 ? 35f : 0f) : 12f + MathF.Sin( Time.Now * 3f + side ) * 8f;
				jaw.LocalRotation = Rotation.FromPitch( -open );
			}
		}

		// Eye stalks wobble.
		foreach ( var (eye, side) in new[] { (EyeL, 1f), (EyeR, -1f) } )
			if ( Has( eye ) ) parts[eye].Go.LocalRotation = parts[eye].Rest * new Angles( MathF.Sin( Time.Now * 2.3f + side ) * 8f, 0, MathF.Sin( Time.Now * 1.7f + side * 2f ) * 10f ).ToRotation();
	}

	void Tint()
	{
		foreach ( var part in parts )
		{
			if ( part is null || part.Gone ) continue;
			var flash = MathX.Clamp( 1f - part.Flash / 0.1f, 0f, 1f );
			foreach ( var (renderer, tint) in part.Renderers )
			{
				if ( !renderer.IsValid() ) continue;
				var color = Color.Lerp( tint, tint * 0.5f, part.Hurt ).WithAlpha( 1f );
				renderer.Tint = Color.Lerp( color, Color.White * 3f, flash );
			}
			var position = part.Go.WorldPosition;
			part.Velocity = (position - part.LastPosition) / MathF.Max( Time.Delta, 0.0001f );
			part.LastPosition = position;
		}
	}

	// ---- Host brain -----------------------------------------------------------

	void Think()
	{
		var target = PlayerSurvivor.FindNearestAlive( Scene, WorldPosition );
		var speedScale = (1f - 0.11f * LegsLost) * (Dragging ? 0.6f : 1f) * (Blind ? 0.7f : 1f);
		if ( agent.IsValid() ) agent.MaxSpeed = MoveSpeed * MathF.Max( 0.2f, speedScale ) * Size;

		Snapping = false;
		if ( target is not null )
		{
			var toTarget = (target.WorldPosition - WorldPosition).WithZ( 0 );
			var distance = toTarget.Length;

			if ( distance <= AttackRange * Size && !Blind )
			{
				agent?.Stop();
				Face( toTarget, 10f );
				Snapping = true;
				if ( timeSinceAttack >= AttackCooldown )
				{
					timeSinceAttack = 0f;
					SnapEverywhere();
					Pinch( target );
				}
			}
			else if ( timeSinceRepath > 0.3f )
			{
				timeSinceRepath = 0f;
				if ( Blind )
				{
					// Can't see: stumbles about roughly where it last heard you.
					if ( wanderGoal.IsNearlyZero() || WorldPosition.Distance( wanderGoal ) < 60f || Game.Random.Float() < 0.15f )
						wanderGoal = target.WorldPosition + Vector3.Random.WithZ( 0 ) * 350f;
					agent?.MoveTo( wanderGoal );
					if ( distance <= AttackRange * Size && timeSinceAttack > AttackCooldown * 2f ) { timeSinceAttack = 0f; SnapEverywhere(); Pinch( target ); }
				}
				else agent?.MoveTo( target.WorldPosition );
			}

			// Crabs go sideways: face across the way it's moving, and turn to face you up close.
			var velocity = agent.IsValid() ? agent.Velocity.WithZ( 0 ) : Vector3.Zero;
			if ( distance < 160f * Size && !Blind ) Face( toTarget, 6f );
			else if ( velocity.Length > 10f ) Face( Rotation.FromYaw( 90f * sideSign ) * velocity, 6f );
		}
		else agent?.Stop();

		NetVelocity = agent.IsValid() ? agent.Velocity : Vector3.Zero;
	}

	void Face( Vector3 direction, float rate )
	{
		if ( direction.IsNearlyZero() ) return;
		var wanted = Rotation.LookAt( direction.WithZ( 0 ).Normal, Vector3.Up );
		WorldRotation = Rotation.Slerp( WorldRotation, wanted, MathX.Clamp( Time.Delta * rate, 0f, 1f ) );
	}

	void Pinch( PlayerSurvivor target )
	{
		var damage = ClawDamage * (ClawsLeft switch { 2 => 1f, 1 => 0.7f, _ => 0.4f });
		target.Health?.OnDamage( new DamageInfo
		{
			Damage = damage,
			Attacker = GameObject,
			Position = target.WorldPosition + Vector3.Up * 20f,
			Origin = WorldPosition,
		} );
	}

	[Rpc.Broadcast( NetFlags.Unreliable )]
	void SnapEverywhere() => timeSinceSnap = 0f;

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
		if ( index < 0 || index >= parts.Length || !Has( index ) ) index = Body;
		lastShotDirection = direction;
		lastShotPointBlank = (position - origin).Length < CreatureGore.OverkillRange;
		var part = parts[index];
		var crit = index is EyeL or EyeR;

		var multiplier = index switch
		{
			Body => WearsBarrel && !Has( Barrel ) ? SoftMultiplier : 1f,
			Barrel => 0.25f,
			EyeL or EyeR => EyeMultiplier,
			_ => LimbMultiplier,
		};

		var cut = false;
		if ( index != Body )
		{
			part.Health -= damage;
			cut = part.Health <= 0f;
		}
		var hurt = index == Body
			? 1f - MathX.Clamp( (health.Current - damage * multiplier) / MathF.Max( 1f, health.MaxHealth ), 0f, 1f )
			: 1f - MathX.Clamp( part.Health / MathF.Max( 1f, part.MaxHealth ), 0f, 1f );

		if ( cut )
		{
			GoneMask |= SubtreeMask( index );
			CutEverywhere( index, position, direction );
		}
		else HitEverywhere( index, position, direction, Game.Random.Float() < 0.3f, hurt );

		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * multiplier, Position = position, Origin = origin, Attacker = shooter?.GameObject } );
		var killed = wasAlive && IsDead;

		// A hit staggers it a little (more without its barrel).
		if ( !killed && agent.IsValid() )
		{
			var push = direction.WithZ( 0 ).Normal * (Has( Barrel ) ? 3f : 7f);
			agent.SetAgentPosition( agent.AgentPosition + push );
		}

		if ( wasAlive && shooter.IsValid() )
			shooter.RewardHit( killed, crit, scoreHit );
	}

	int SubtreeMask( int index )
	{
		var mask = 1 << index;
		for ( var i = 0; i < parts.Length; i++ )
		{
			if ( i == index || parts[i] is null ) continue;
			for ( var p = parts[i].Parent; p >= 0; p = parts[p].Parent )
				if ( p == index ) { mask |= 1 << i; break; }
		}
		return mask;
	}

	[Rpc.Broadcast( NetFlags.Unreliable )]
	void HitEverywhere( int index, Vector3 position, Vector3 direction, bool chip, float hurt )
	{
		var part = index >= 0 && index < parts.Length ? parts[index] : null;
		if ( part is null ) return;
		part.Flash = 0f;
		part.Hurt = MathF.Max( part.Hurt, hurt * 0.8f );

		if ( index == Barrel )
		{
			CreatureGore.Jiggle( part.Go, 0.5f );
			// Metal: sparks of glowing waste rather than goo.
			gore?.GooBurst( position, -direction, new Color( 0.5f, 1f, 0.2f ) * 2f, 5, 260f, 0.6f );
			return;
		}
		gore?.GooBurst( position, -direction, gooColor, 6, 220f, 0.8f );
		gore?.GooSpray( position, direction, 2, gooColor, 180f, 0.4f, 0.9f );
		if ( !part.Gone )
		{
			CreatureGore.AddWound( part.Go, position, direction, shellColor, gooColor, (index == Body ? 3f : 2f) * Size );
			CreatureGore.Jiggle( part.Go, index == Body ? 0.7f : 1f );
		}
		if ( chip && index == Body ) gore?.ThrowBlob( position, -direction, shellColor, gooColor, 3f * Size );
	}

	[Rpc.Broadcast]
	void CutEverywhere( int index, Vector3 position, Vector3 direction ) => CutLocal( index, direction, quiet: false );

	void CutLocal( int index, Vector3 direction, bool quiet )
	{
		var part = parts[index];
		if ( part is null || part.Gone ) return;
		MarkGone( index );
		if ( index == PincerL ) jawL = null;
		if ( index == PincerR ) jawR = null;
		if ( index == ArmL ) jawL = null;
		if ( index == ArmR ) jawR = null;

		if ( quiet ) { part.Go.Destroy(); return; }

		var at = part.Go.WorldPosition;
		if ( index != Barrel )
		{
			// Raw stump on the crab where it came off, and a cap on the piece that flew.
			var parent = part.Parent >= 0 ? parts[part.Parent] : null;
			var outward = parent is not null ? (at - parent.Go.WorldPosition).Normal : Vector3.Up;
			var radius = (index is EyeL or EyeR ? 1.8f : index is ArmL or ArmR or PincerL or PincerR ? 4f : 2.8f) * Size;
			if ( parent is not null && !parent.Gone ) CreatureGore.AddStump( parent.Go, at, outward, radius, shellColor, gooColor, 4f );
			if ( index is not EyeL and not EyeR ) CreatureGore.AddStump( part.Go, at, -outward, radius, shellColor, gooColor, 1.5f );
		}
		if ( index == Barrel )
		{
			// Pops off its back and clangs away, leaking glowing waste.
			gore?.GooBurst( at, Vector3.Up, new Color( 0.5f, 1f, 0.2f ) * 2f, 14, 300f, 1.2f );
			gore?.GooSpray( at, Vector3.Down, 4, new Color( 0.45f, 1f, 0.15f ), 200f, 0.6f, 1.3f );
			Detach( part, direction * 120f + Vector3.Up * 260f, spin: 18f );
			return;
		}

		if ( index is EyeL or EyeR )
		{
			gore?.GooBurst( at, direction, Color.White, 8, 200f, 0.8f );
			part.Go.Destroy();
			return;
		}

		gore?.GooBurst( at, direction, gooColor, 10, 260f, 1f );
		gore?.GooSpray( at, direction, 3, gooColor, 200f, 0.5f, 1.1f );
		Detach( part, direction * Game.Random.Float( 80f, 160f ) + Vector3.Up * 120f );
		CreatureGore.Twitch( part.Go, Game.Random.Float( 2.5f, 4.5f ), 0.8f );
	}

	void MarkGone( int index )
	{
		var mask = SubtreeMask( index );
		for ( var i = 0; i < parts.Length; i++ )
			if ( (mask & (1 << i)) != 0 && parts[i] is not null ) parts[i].Gone = true;
	}

	void Detach( Part part, Vector3 push, float spin = 12f )
	{
		var go = part.Go;
		go.SetParent( null, true );
		foreach ( var p in go.Components.GetAll<CreaturePart>( FindMode.EverythingInSelfAndDescendants ).ToList() ) p.Destroy();
		// Keep the capsule hit shapes, made solid, so a leg tumbles and lies like a leg (not a ball).
		var solids = 0;
		foreach ( var c in go.Components.GetAll<Collider>( FindMode.EverythingInSelfAndDescendants ).ToList() )
		{
			if ( c is CapsuleCollider capsule ) { capsule.IsTrigger = false; solids++; }
			else c.Destroy();
		}
		foreach ( var o in go.GetAllObjects( true ) ) o.Tags.Remove( "creature" );

		var bounds = go.GetBounds();
		var radius = MathX.Clamp( bounds.Size.Length * 0.25f, 2.5f, 20f );
		Gib.Attach( go, gore, radius, 15f, 2f, part.Velocity * 0.4f + push, gooColor, addCollider: solids == 0 );
		var rb = go.Components.Get<Rigidbody>();
		if ( rb.IsValid() ) rb.AngularVelocity = Vector3.Random * spin;
	}

	void CatchUp()
	{
		if ( IsDead && !diedLocally ) { DieLocal( Vector3.Forward, quiet: true ); return; }
		for ( var i = 1; i < parts.Length; i++ )
		{
			if ( (GoneMask & (1 << i)) != 0 && Has( i ) )
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
		agent?.Stop();
		Killed?.Invoke( this );
		DieEverywhere( lastShotDirection, lastShotPointBlank );
	}

	[Rpc.Broadcast]
	void DieEverywhere( Vector3 direction, bool overkill ) => DieLocal( direction, quiet: false, overkill );

	/// <summary> The shell cracks open in a burst of goo; barrel, claws and legs scatter. </summary>
	void DieLocal( Vector3 direction, bool quiet, bool overkill = false )
	{
		if ( diedLocally ) return;
		diedLocally = true;
		timeSinceDeath = 0f;
		var overkillAt = parts[Body].Go.WorldPosition;
		if ( agent.IsValid() ) agent.Enabled = false;
		foreach ( var c in GetComponentsInChildren<Collider>( true, true ) ) c.Enabled = false;
		if ( !rig.IsValid() ) return;
		if ( quiet ) { rig.Destroy(); return; }

		var at = parts[Body].Go.WorldPosition;
		if ( overkill ) CreatureGore.Overkill( Scene, overkillAt, direction, shellColor, gooColor, Size );
		for ( var k = 0; k < 2; k++ ) gore?.GooBurst( at, direction + Vector3.Up, gooColor, 12, 320f, 1.3f );
		gore?.GooSpray( at, direction, 6, gooColor, 240f, 0.7f, 1.5f );
		for ( var b = 0; b < 2; b++ ) gore?.ThrowBlob( at, direction, shellColor, gooColor, 4f * Size );

		foreach ( var index in new[] { Barrel, ArmL, ArmR, EyeL, EyeR }.Concat( Enumerable.Range( FirstLeg, LegCount ) ) )
		{
			if ( !Has( index ) ) continue;
			var part = parts[index];
			MarkGone( index );
			Detach( part, (direction + Vector3.Random + Vector3.Up).Normal * Game.Random.Float( 120f, 260f ), spin: 20f );
		}

		// The body itself: a cracked shell left on the floor (or, point-blank, nothing left at all).
		if ( Has( Body ) )
		{
			MarkGone( Body );
			if ( overkill ) parts[Body].Go.Destroy();
			else Detach( parts[Body], direction * 60f );
		}
		rig.Destroy();
	}
}
