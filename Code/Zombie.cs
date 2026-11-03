using Sandbox.Citizen;

/// <summary>
/// Chases the nearest living player over the navmesh and hits them when close.
/// </summary>
public sealed class Zombie : Component
{
	[Property] public float MoveSpeed { get; set; } = 120f;
	[Property] public float AttackRange { get; set; } = 55f;
	[Property] public float AttackDamage { get; set; } = 15f;
	[Property] public float AttackCooldown { get; set; } = 1.0f;
	[Property] public float CorpseLifetime { get; set; } = 10f;

	[Property, Group( "Juice" )] public SoundEvent VoiceSound { get; set; }
	[Property, Group( "Juice" )] public SoundEvent AttackSound { get; set; }
	[Property, Group( "Juice" )] public SoundEvent DeathSound { get; set; }
	[Property, Group( "Juice" )] public GameObject DeathEffect { get; set; }
	[Property, Group( "Juice" )] public Vector2 GroanInterval { get; set; } = new( 4f, 10f );

	[Property, Group( "Crawler" )] public float CrawlSpeedScale { get; set; } = 0.4f;
	[Property, Group( "Crawler" )] public float LimpSpeedScale { get; set; } = 0.7f;
	/// <summary> How far forward a crawler tips, in degrees. 90 is flat on its face. </summary>
	[Property, Group( "Crawler" )] public float CrawlPitch { get; set; } = 80f;
	[Property, Group( "Crawler" )] public float CrawlAttackRange { get; set; } = 70f;

	public bool IsDead => Health?.IsDead ?? false;

	/// <summary> Lost a leg: drags itself along on its belly until it's killed. </summary>
	public bool IsCrawler { get; private set; }
	float speedScale = 1f;

	public void BecomeCrawler()
	{
		if ( IsCrawler || IsDead ) return;
		IsCrawler = true;
		speedScale = MathF.Min( speedScale, CrawlSpeedScale );
		ApplySpeed();
	}

	/// <summary> Lost a foot: still walking, just slower. </summary>
	public void Limp()
	{
		if ( IsDead ) return;
		speedScale = MathF.Min( speedScale, LimpSpeedScale );
		ApplySpeed();
	}

	void ApplySpeed()
	{
		if ( Agent.IsValid() ) Agent.MaxSpeed = MoveSpeed * speedScale;
	}

	/// <summary> Fired once when this zombie dies. The wave director listens to this. </summary>
	public event System.Action<Zombie> Killed;

	NavMeshAgent Agent;
	Health Health;
	CitizenAnimationHelper Anim;
	SkinnedModelRenderer Renderer;
	(SkinnedModelRenderer Renderer, Color Tint)[] visible = Array.Empty<(SkinnedModelRenderer, Color)>();
	TimeSince timeSinceAttack = 999f;
	TimeSince timeSinceRepath = 999f;
	TimeSince timeSinceDeath;
	TimeSince timeSinceHurt = 999f;
	TimeUntil nextGroan;
	float voicePitch;

	protected override void OnStart()
	{
		Agent = GetComponent<NavMeshAgent>();
		Health = GetComponent<Health>();
		Anim = GetComponent<CitizenAnimationHelper>();
		Renderer = GetComponent<SkinnedModelRenderer>();
		// The root renderer may be a hidden animation driver with a mesh on a child, so flash them all.
		visible = GetComponentsInChildren<SkinnedModelRenderer>( true, true )
			.Select( r => (r, r.Tint) )
			.ToArray();

		// Each zombie gets its own voice so a crowd doesn't sound like one sample on repeat.
		voicePitch = Game.Random.Float( 0.75f, 1.1f );
		nextGroan = Game.Random.Float( 0.2f, 2f );

		if ( Agent.IsValid() )
		{
			Agent.MaxSpeed = MoveSpeed * speedScale;
			Agent.UpdateRotation = false;
		}

		if ( Health.IsValid() )
		{
			Health.Damaged += OnHurt;
			Health.Died += OnDied;
		}
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		if ( IsDead )
		{
			if ( timeSinceDeath > CorpseLifetime )
				GameObject.Destroy();
			return;
		}

		UpdateHurtFlash();

		if ( nextGroan )
		{
			nextGroan = Game.Random.Float( GroanInterval.x, GroanInterval.y );
			Voice( 0.6f );
		}

		var target = PlayerSurvivor.FindNearestAlive( Scene, WorldPosition );
		var velocity = Agent.IsValid() ? Agent.Velocity : Vector3.Zero;

		if ( target is null )
		{
			Agent?.Stop();
		}
		else
		{
			var toTarget = (target.WorldPosition - WorldPosition).WithZ( 0 );
			var range = IsCrawler ? CrawlAttackRange : AttackRange;

			if ( toTarget.Length <= range )
			{
				Agent?.Stop();
				Face( toTarget );

				if ( timeSinceAttack >= AttackCooldown )
				{
					timeSinceAttack = 0f;
					Attack( target );
				}
			}
			else if ( timeSinceRepath > 0.25f )
			{
				timeSinceRepath = 0f;
				Agent?.MoveTo( target.WorldPosition );
			}

			if ( velocity.WithZ( 0 ).Length > 5f )
				Face( velocity.WithZ( 0 ) );
		}

		// Crawlers keep their prone tilt even while standing still (Face slerps them down when they first fall).
		if ( IsCrawler ) Face( WorldRotation.Forward.WithZ( 0 ) );

		if ( Anim.IsValid() )
		{
			// The citizen has no crawl animation; its swim stroke on a body tipped flat reads as dragging itself along.
			Anim.IsSwimming = IsCrawler;
			Anim.WithVelocity( velocity );
			Anim.WithWishVelocity( Agent.IsValid() ? Agent.WishVelocity : Vector3.Zero );
			Anim.IsGrounded = true;
		}
	}

	void Face( Vector3 direction )
	{
		if ( direction.IsNearlyZero() ) return;
		var wanted = Rotation.LookAt( direction.Normal, Vector3.Up );
		if ( IsCrawler ) wanted *= Rotation.FromPitch( CrawlPitch );
		// Crawlers fall over and turn a bit more slowly.
		WorldRotation = Rotation.Slerp( WorldRotation, wanted, Time.Delta * (IsCrawler ? 5f : 8f) );
	}

	void Attack( PlayerSurvivor target )
	{
		var hitPos = target.WorldPosition + Vector3.Up * 40f;
		var damage = new DamageInfo
		{
			Damage = AttackDamage,
			Attacker = GameObject,
			Position = hitPos,
			Origin = WorldPosition,
		};

		Voice( 1f );
		if ( AttackSound is not null ) Sound.Play( AttackSound, hitPos );
		target.Health?.OnDamage( damage );
	}

	/// <summary>
	/// Entry point for bullets. Works on living zombies and corpses: scales damage by hit zone,
	/// applies it, then lets the gore system cut limbs and push the ragdoll.
	/// Returns true if this shot killed the zombie.
	/// </summary>
	public bool TakeBullet( DamageInfo damage, string boneName, Vector3 direction )
	{
		var gore = GetComponent<ZombieGore>();
		var segment = gore?.SegmentForBone( boneName ) ?? Segment.Chest;
		lastHitSegment = segment;

		// The multiplier is how much a hit hurts the zombie overall (limb shots hurt less, so you can
		// strip limbs before it drops). The gore system works from the raw bullet, so limbs still come off.
		var raw = damage.Damage;
		damage.Damage *= gore?.DamageMultiplier( segment ) ?? 1f;

		var wasAlive = !IsDead;
		if ( wasAlive ) Health?.OnDamage( damage );
		var killed = wasAlive && IsDead;

		gore?.AddDamage( segment, raw, damage.Position, direction, killed );
		return killed;
	}

	public Segment LastHitSegment => lastHitSegment;
	Segment lastHitSegment = Segment.Chest;

	void OnHurt( DamageInfo damage )
	{
		timeSinceHurt = 0f;

		// Stagger: knock the zombie back along the shot and slow it down for a moment.
		// Headshots snap it back harder; leg shots nearly stop it.
		// (CitizenAnimationHelper.ProceduralHitReaction needs a hitbox hit, which plain traces don't give.)
		if ( Agent.IsValid() )
		{
			var push = (damage.Position - damage.Origin).WithZ( 0 ).Normal;
			var isHead = lastHitSegment == Segment.Head;
			var isLeg = lastHitSegment is Segment.ThighL or Segment.ThighR or Segment.ShinL or Segment.ShinR or Segment.FootL or Segment.FootR;
			Agent.SetAgentPosition( Agent.AgentPosition + push * (isHead ? 14f : 6f) );
			Agent.Velocity *= isLeg ? 0.05f : 0.3f;
		}
	}

	/// <summary> Flashes white for a moment after being hit. </summary>
	void UpdateHurtFlash()
	{
		var flash = MathX.Clamp( 1f - timeSinceHurt / 0.12f, 0f, 1f );
		foreach ( var (renderer, tint) in visible )
		{
			if ( renderer.IsValid() )
				renderer.Tint = Color.Lerp( tint, Color.White * 3f, flash );
		}
	}

	void Voice( float volume )
	{
		if ( VoiceSound is null ) return;
		var handle = Sound.Play( VoiceSound, WorldPosition + Vector3.Up * 60f );
		if ( !handle.IsValid() ) return;
		handle.Pitch = voicePitch * Game.Random.Float( 0.95f, 1.05f );
		handle.Volume = volume;
	}

	void OnDied()
	{
		timeSinceDeath = 0f;
		foreach ( var (renderer, tint) in visible )
		{
			if ( renderer.IsValid() ) renderer.Tint = tint;
		}

		if ( Agent.IsValid() )
		{
			Agent.Stop();
			Agent.Enabled = false;
		}

		var chest = WorldPosition + Vector3.Up * 45f;
		if ( DeathSound is not null ) Sound.Play( DeathSound, chest );
		if ( DeathEffect.IsValid() )
		{
			var fx = DeathEffect.Clone( chest, Rotation.LookAt( Vector3.Up ) );
			fx.AddComponent<SelfDestruct>().Lifetime = 3f;
		}

		// Stop blocking players (the capsule lives on a child), then flop over as a ragdoll.
		foreach ( var collider in GetComponentsInChildren<Collider>( true, true ) )
			collider.Enabled = false;

		if ( Renderer.IsValid() )
		{
			if ( Anim.IsValid() ) Anim.Enabled = false;
			var ragdoll = AddComponent<ModelPhysics>();
			ragdoll.Renderer = Renderer;
			ragdoll.Model = Renderer.Model;
		}

		Killed?.Invoke( this );
	}
}
