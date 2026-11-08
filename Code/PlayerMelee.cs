/// <summary>
/// Quick melee: press Melee (X) to swing a fire axe. It works with any gun in hand, hides the gun during
/// the swing and cleaves everything in front of you, so it's great for shearing legs and claws off.
/// Added to the local player by PlayerSurvivor; no scene setup needed.
/// </summary>
public sealed class PlayerMelee : Component
{
	[Property, Group( "Stats" )] public float Damage { get; set; } = 70f;
	[Property, Group( "Stats" )] public float Reach { get; set; } = 110f;
	[Property, Group( "Stats" )] public float Radius { get; set; } = 16f;
	/// <summary> Degrees either side of straight ahead the swing covers. </summary>
	[Property, Group( "Stats" )] public float ArcDegrees { get; set; } = 38f;
	[Property, Group( "Stats" )] public float Impulse { get; set; } = 320f;
	[Property, Group( "Stats" )] public float Cooldown { get; set; } = 0.55f;
	/// <summary> Seconds from pressing the button to the blade landing. </summary>
	[Property, Group( "Stats" )] public float WindUp { get; set; } = 0.24f;

	/// <summary> True while the axe is out (the gun is hidden and can't fire). </summary>
	public bool IsSwinging => ScrubTime >= 0f || Swing < Cooldown;

	TimeSince Swing { get; set; } = 999f;
	bool struck;
	bool leftSwing;
	GameObject axe;
	readonly HashSet<object> hitThisSwing = new();

	PlayerWeapon Weapon => GetComponent<WeaponInventory>()?.Current ?? GetComponent<PlayerWeapon>();

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		var survivor = GetComponent<PlayerSurvivor>();
		var alive = survivor.IsValid() && survivor.IsAlive;
		var blocked = !alive || DevFlags.Placing || DevFlags.FreeCam || (GetComponent<PlayerController>()?.ThirdPerson ?? false);

		if ( !blocked && Input.Pressed( "Melee" ) && Swing >= Cooldown )
		{
			Swing = 0f;
			struck = false;
			leftSwing = !leftSwing;
			hitThisSwing.Clear();
		}

		if ( !struck && Swing >= WindUp && Swing < Cooldown )
		{
			struck = true;
			Strike();
		}

		UpdateView( blocked );
	}

	/// <summary> Testing aid: when 0 or more, the axe is held at this many seconds into the swing. </summary>
	public float ScrubTime { get; set; } = -1f;

	/// <summary> Testing aid: swings as if the button was pressed (scripts can't fake input). </summary>
	public void TestSwing()
	{
		Swing = 0f;
		struck = false;
		leftSwing = !leftSwing;
		hitThisSwing.Clear();
	}

	// ---- the hit ----------------------------------------------------------------------------

	void Strike()
	{
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;
		var survivor = GetComponent<PlayerSurvivor>();
		var start = camera.WorldPosition;
		var rotation = camera.WorldRotation;
		var anyHit = false;

		// A fan of sphere sweeps across the arc, so one swing can catch several things.
		for ( var i = -2; i <= 2; i++ )
		{
			var direction = (rotation * Rotation.FromYaw( i * ArcDegrees * 0.5f )).Forward;
			var end = start + direction * Reach;

			var tr = Scene.Trace.Sphere( Radius, start, end )
				.IgnoreGameObjectHierarchy( GameObject )
				.UseHitboxes()
				.WithoutTags( "zombieblocker", "viewmodel", "gore" )
				.Run();

			// Floating creatures are triggers; take whichever is nearer.
			var creature = Scene.Trace.Sphere( Radius, start, end )
				.HitTriggersOnly()
				.WithTag( "creature" )
				.Run();
			if ( creature.Hit && (!tr.Hit || creature.Distance < tr.Distance) ) tr = creature;

			if ( tr.Hit && Hit( tr, start, direction, survivor ) ) anyHit = true;
		}

		var juice = GetComponent<CameraJuice>();
		juice?.Shake( anyHit ? 0.45f : 0.12f );
		juice?.KickFov( anyHit ? 2.5f : 0.8f );
		if ( anyHit ) Weapon?.MarkHit();
	}

	/// <summary> Applies one sweep's hit. Returns true if it landed on something new. </summary>
	bool Hit( SceneTraceResult tr, Vector3 start, Vector3 direction, PlayerSurvivor survivor )
	{
		if ( !tr.GameObject.IsValid() ) return false;
		var go = tr.GameObject;

		var zombie = go.Components.Get<Zombie>( FindMode.EverythingInSelfAndAncestors );
		if ( zombie.IsValid() )
		{
			if ( !hitThisSwing.Add( zombie ) ) return false;
			var bone = tr.Hitbox?.Bone?.Name;
			if ( string.IsNullOrEmpty( bone ) && go != zombie.GameObject ) bone = go.Name;
			if ( string.IsNullOrEmpty( bone ) )
				bone = zombie.GetComponent<SkinnedModelRenderer>()?.GetClosestBone( tr.HitPosition )?.Name;
			PlayFlesh( tr.HitPosition );
			zombie.ReceiveBullet( Damage * 2f, bone, direction, tr.HitPosition, start, survivor, scoreHit: true );
			return true;
		}

		// Creatures with chunkable parts: each part can be struck once per swing.
		var shootable = go.Components.Get<IShootable>( FindMode.EverythingInSelfAndAncestors );
		if ( shootable is not null && shootable.ShotOwner.IsValid() )
		{
			if ( shootable.IsDead || !hitThisSwing.Add( go ) ) return false;
			PlayFlesh( tr.HitPosition );
			shootable.ReceiveShot( Damage, go, direction, tr.HitPosition, start, survivor, scoreHit: hitThisSwing.Add( shootable.ShotOwner ) );
			return true;
		}

		var gib = go.Components.Get<Gib>( FindMode.EverythingInSelfAndAncestors );
		if ( gib.IsValid() )
		{
			if ( !hitThisSwing.Add( gib ) ) return false;
			PlayFlesh( tr.HitPosition );
			gib.TakeHit( tr.HitPosition, direction, Damage, Impulse * 0.5f );
			return true;
		}

		if ( tr.Body is { } body && body.MotionEnabled && hitThisSwing.Add( body ) )
			body.ApplyImpulseAt( tr.HitPosition, direction * Impulse * body.Mass * 0.3f );

		var damageables = go.Components.GetAll<Component.IDamageable>( FindMode.EverythingInSelfAndAncestors ).ToList();
		if ( damageables.Count == 0 )
		{
			if ( hitThisSwing.Add( "world" ) ) PlayAt( Weapon?.HitWorldSound, tr.HitPosition );
			return false;
		}

		var landed = false;
		foreach ( var target in damageables )
		{
			if ( !hitThisSwing.Add( target ) ) continue;
			var health = target as Health;
			var wasAlive = health is null || !health.IsDead;
			target.OnDamage( new DamageInfo { Damage = Damage, Attacker = GameObject, Weapon = GameObject, Position = tr.HitPosition, Origin = start } );
			landed = true;
			if ( !wasAlive ) continue;
			var killed = health is not null && health.IsDead;
			if ( killed ) Weapon?.MarkKill();
			survivor?.AddPoints( killed ? survivor.PointsPerKill : survivor.PointsPerHit );
		}
		if ( landed ) PlayFlesh( tr.HitPosition );
		return landed;
	}

	void PlayFlesh( Vector3 position ) => PlayAt( Weapon?.HitFleshSound, position );

	static void PlayAt( SoundEvent sound, Vector3 position )
	{
		if ( sound is not null ) Sound.Play( sound, position );
	}

	// ---- the axe in view ----------------------------------------------------------------------

	const float RaiseEnd = 0.14f;   // seconds: axe is back over the shoulder
	const float SlashEnd = 0.32f;   // seconds: blade has swept through

	/// <summary> One pose of the axe's grip: position and angles in camera space, for the right-hand swing (mirrored for the left). </summary>
	readonly record struct Pose( Vector3 Position, Angles Angles );

	static readonly Pose Ready = new( new Vector3( 24f, -16f, -12f ), new Angles( -35f, -5f, -20f ) );
	static readonly Pose Raised = new( new Vector3( 18f, -20f, -6f ), new Angles( -65f, -10f, -35f ) );
	static readonly Pose Struck = new( new Vector3( 28f, 12f, -10f ), new Angles( 10f, 35f, 40f ) );
	static readonly Pose Gone = new( new Vector3( 22f, 0f, -40f ), new Angles( 60f, 0f, 0f ) );

	static Pose Blend( Pose a, Pose b, float k, float side )
	{
		var position = Vector3.Lerp( a.Position, b.Position, k );
		var angles = new Angles( MathX.Lerp( a.Angles.pitch, b.Angles.pitch, k ), MathX.Lerp( a.Angles.yaw, b.Angles.yaw, k ), MathX.Lerp( a.Angles.roll, b.Angles.roll, k ) );
		// Left swings mirror the sideways parts of the pose.
		return new Pose( new Vector3( position.x, position.y * side, position.z ), new Angles( angles.pitch, angles.yaw * side, angles.roll * side ) );
	}

	void UpdateView( bool blocked )
	{
		var camera = Scene.Camera;
		var scrubbing = ScrubTime >= 0f;
		var show = !blocked && !DevFlags.HideWeapon && camera.IsValid() && (scrubbing || Swing < Cooldown);
		if ( !show )
		{
			if ( axe.IsValid() ) axe.Enabled = false;
			return;
		}

		if ( !axe.IsValid() ) axe = BuildAxe( camera.GameObject );
		axe.Enabled = true;
		axe.SetParent( camera.GameObject, false );

		// Four beats: lift into view, haul back, slash through, drop away.
		var time = scrubbing ? ScrubTime : (float)Swing;
		var side = leftSwing ? -1f : 1f;
		Pose pose;
		if ( time < RaiseEnd ) pose = Blend( Ready, Raised, Ease( time / RaiseEnd ), side );
		else if ( time < SlashEnd ) pose = Blend( Raised, Struck, Ease( (time - RaiseEnd) / (SlashEnd - RaiseEnd) ), side );
		else pose = Blend( Struck, Gone, Ease( (time - SlashEnd) / MathF.Max( 0.05f, Cooldown - SlashEnd ) ), side );

		axe.LocalPosition = pose.Position;
		axe.LocalRotation = pose.Angles.ToRotation();
	}

	static float Ease( float x )
	{
		x = MathX.Clamp( x, 0f, 1f );
		return x * x * (3f - 2f * x);
	}

	/// <summary> A fire axe from boxes, origin at the grip: wooden haft, red grip band, steel head with a bit and a spike. </summary>
	static GameObject BuildAxe( GameObject camera )
	{
		var root = new GameObject( camera, true, "Melee Axe" );
		root.Tags.Add( "viewmodel" );

		// The haft points along +X (away from the camera); the head sits at the far end.
		Part( root, "Haft", new Vector3( 14f, 0, 0 ), new Vector3( 30f, 1.8f, 1.8f ), new Color( 0.45f, 0.28f, 0.14f ) );
		Part( root, "Grip", new Vector3( 4f, 0, 0 ), new Vector3( 9f, 2.2f, 2.2f ), new Color( 0.7f, 0.08f, 0.08f ) );
		Part( root, "Neck", new Vector3( 28.5f, 0, 0 ), new Vector3( 4f, 2.4f, 4.5f ), new Color( 0.5f, 0.5f, 0.55f ) );
		Part( root, "Head", new Vector3( 31f, 0, 0 ), new Vector3( 6f, 1.4f, 9f ), new Color( 0.62f, 0.64f, 0.7f ) );
		Part( root, "Edge", new Vector3( 31.4f, 0, 6.2f ), new Vector3( 5f, 0.6f, 4.5f ), new Color( 0.9f, 0.92f, 0.96f ) );
		Part( root, "Spike", new Vector3( 31f, 0, -5.6f ), new Vector3( 4f, 1.1f, 4f ), new Color( 0.45f, 0.46f, 0.5f ) );
		return root;
	}

	static void Part( GameObject parent, string name, Vector3 at, Vector3 size, Color color )
	{
		var go = new GameObject( parent, true, name );
		go.LocalPosition = at;
		go.LocalScale = Shapes.BoxScale( size );
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Shapes.Box;
		r.Tint = color;
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
	}
}
