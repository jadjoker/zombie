/// <summary>
/// MC61: a magical floating cyclops in a tux and top hat. The boss.
///
/// Attacks:
///  - Eye beam: shouts "I SEEE YOUUU", charges his eye (a thin aiming line shows where), then fires a
///    heavy beam that sweeps slowly after you. Keep moving.
///  - "Pick any card": snaps the briefcase open and spins, flinging rings of cards in every direction.
///    Each card carries a curse: burn, poison, slow or ink.
///  - The worm dance: sometimes he just drops to the floor and does the worm (it reminds him of
///    cheering crowds as a kid). He takes extra damage while he dances.
///
/// Chunking: the hat can be shot off; arms and legs come off at the shoulder/hip or elbow/knee; the
/// briefcase can be destroyed (or dropped with the arm holding it) which ends his card trick; the eye
/// is a critical spot and can be popped, which ends the beam. The head is lethal. Without eye or case
/// he just comes at you.
///
/// Networking: the host runs him (the networked root); the rig is built and animated on every machine
/// from synced state. Speech, cards and cuts are broadcast; a mask lets late joiners catch up.
/// </summary>
public sealed class MC61 : Component, ICreature
{
	[Property, Group( "Body" )] public float Size { get; set; } = 1.35f;

	[Property, Group( "Movement" )] public float PreferredDistance { get; set; } = 600f;
	[Property, Group( "Movement" )] public float HoverHeight { get; set; } = 110f;
	[Property, Group( "Movement" )] public float MoveSpeed { get; set; } = 230f;

	[Property, Group( "Beam" )] public float ChargeTime { get; set; } = 1.6f;
	[Property, Group( "Beam" )] public float BeamTime { get; set; } = 2.4f;
	[Property, Group( "Beam" )] public float BeamDamagePerSecond { get; set; } = 45f;
	[Property, Group( "Beam" )] public float BeamWidth { get; set; } = 16f;
	/// <summary> How fast the beam swings after you, in degrees per second. Slower = easier to dodge. </summary>
	[Property, Group( "Beam" )] public float BeamTrackSpeed { get; set; } = 40f;
	[Property, Group( "Beam" )] public float BeamRange { get; set; } = 3000f;

	[Property, Group( "Cards" )] public int CardsPerRing { get; set; } = 22;
	[Property, Group( "Cards" )] public int CardRings { get; set; } = 2;
	[Property, Group( "Cards" )] public float CardSpeed { get; set; } = 520f;
	[Property, Group( "Cards" )] public float CardDamage { get; set; } = 6f;

	[Property, Group( "Attacks" )] public Vector2 AttackGap { get; set; } = new( 2f, 3.5f );
	[Property, Group( "Attacks" )] public float DanceChance { get; set; } = 0.15f;
	[Property, Group( "Attacks" )] public float DanceTime { get; set; } = 4.5f;
	[Property, Group( "Attacks" )] public float DanceDamageScale { get; set; } = 1.6f;
	/// <summary> Once he's lost his eye and his case, he rams you for this much. </summary>
	[Property, Group( "Attacks" )] public float RamDamage { get; set; } = 25f;

	[Property, Group( "Damage" )] public float EyeMultiplier { get; set; } = 2.5f;
	[Property, Group( "Damage" )] public float TorsoMultiplier { get; set; } = 0.6f;
	[Property, Group( "Damage" )] public float LimbMultiplier { get; set; } = 0.35f;
	[Property, Group( "Damage" )] public float LimbHealth { get; set; } = 160f;
	[Property, Group( "Damage" )] public float HatHealth { get; set; } = 40f;
	[Property, Group( "Damage" )] public float CaseHealth { get; set; } = 150f;
	[Property, Group( "Damage" )] public float EyeHealth { get; set; } = 350f;

	public enum Mode { Float, Charge, Beam, Cards, Dance }

	[Sync] public Mode State { get; set; }
	[Sync] public Vector3 BeamEnd { get; set; }
	[Sync] public int GoneMask { get; set; }

	public bool IsDead => health?.IsDead ?? false;
	public float HealthFraction => health?.Fraction ?? 0f;
	/// <summary> What he last shouted, for the HUD. </summary>
	public string Line { get; private set; } = "";
	public TimeSince TimeSinceLine { get; private set; } = 999f;

	public event System.Action<MC61> Killed;

	// Part indices (also the bits in GoneMask).
	const int Torso = 0, Head = 1, Eye = 2, Hat = 3, ArmL = 4, ForearmL = 5, ArmR = 6, ForearmR = 7, Case = 8, LegL = 9, ShinL = 10, LegR = 11, ShinR = 12;

	static readonly Color Tux = new( 0.06f, 0.06f, 0.09f );
	static readonly Color Skin = new( 0.78f, 0.62f, 1f );
	static readonly Color Glove = new( 0.97f, 0.97f, 1f );
	static readonly Color Iris = new( 1f, 0.15f, 0.75f );
	static readonly Color BeamColor = new( 1f, 0.25f, 0.85f );

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
	}

	readonly Part[] parts = new Part[13];
	GameObject rig;
	GameObject beam, beamCore, aimLine, lid;
	Health health;
	GoreSystem gore;

	// Host brain
	TimeSince stateTime;
	TimeUntil nextAttack;
	TimeSince beamTick;
	TimeSince ramTime = 999f;
	Vector3 velocity;
	Vector3 beamDirection;
	int cardRingsThrown;
	float orbitSign = 1f;

	// Everyone
	TimeSince localStateTime;
	Mode seenState;
	TimeSince scorchTime;
	bool diedLocally;
	TimeSince timeSinceDeath;
	Vector3 lastShotDirection = Vector3.Forward;

	// ---- Setup ------------------------------------------------------------

	protected override void OnStart()
	{
		health = GetComponent<Health>();
		gore = GoreSystem.Get( Scene );
		nextAttack = 2.5f;
		orbitSign = Game.Random.Float() < 0.5f ? -1f : 1f;
		seenState = State;

		if ( health.IsValid() && !IsProxy ) health.Died += OnDied;
		Build();
	}

	void Build()
	{
		rig = new GameObject( true, "MC61 Rig" );
		rig.NetworkMode = NetworkMode.Never;
		rig.WorldTransform = WorldTransform;
		rig.WorldScale = Size;

		// Torso: black tux with a white shirt front and a red bow tie.
		var torso = AddPart( Torso, -1, rig, "Torso", Vector3.Zero, Angles.Zero, LimbHealth );
		Sphere( torso, new Vector3( 0, 0, 0 ), new Vector3( 12f, 16f, 21f ), Tux );
		Sphere( torso, new Vector3( 8.5f, 0, 5f ), new Vector3( 4f, 7f, 13f ), Glove );
		Sphere( torso, new Vector3( 12f, -3f, 17f ), new Vector3( 2f, 3.2f, 2.4f ), new Color( 0.9f, 0.05f, 0.1f ) );
		Sphere( torso, new Vector3( 12f, 3f, 17f ), new Vector3( 2f, 3.2f, 2.4f ), new Color( 0.9f, 0.05f, 0.1f ) );
		Sphere( torso, new Vector3( 12.5f, 0, 17f ), new Vector3( 1.6f, 1.6f, 1.6f ), new Color( 0.6f, 0.02f, 0.05f ) );
		Trigger( torso, new Vector3( 0, 0, -14 ), new Vector3( 0, 0, 14 ), 14f );

		// Big round head, one huge eye, top hat.
		var head = AddPart( Head, Torso, torso.Go, "Head", new Vector3( 0, 0, 20 ), Angles.Zero, 0f );
		Sphere( head, new Vector3( 0, 0, 18 ), new Vector3( 20f, 20f, 20f ), Skin );
		Trigger( head, new Vector3( 0, 0, 9 ), new Vector3( 0, 0, 27 ), 18f );

		var eye = AddPart( Eye, Head, head.Go, "Eye", new Vector3( 15.5f, 0, 21f ), Angles.Zero, EyeHealth );
		Sphere( eye, Vector3.Zero, new Vector3( 5f, 9.5f, 9.5f ), Glove );
		Sphere( eye, new Vector3( 3.6f, 0, 0 ), new Vector3( 2f, 5.5f, 5.5f ), Iris );
		Sphere( eye, new Vector3( 5f, 0, 0 ), new Vector3( 1.2f, 2.8f, 2.8f ), Color.Black );
		Sphere( eye, new Vector3( 5.4f, 1.6f, 2.2f ), new Vector3( 0.6f, 1f, 1f ), Color.White );
		Trigger( eye, new Vector3( 0, 0, -1 ), new Vector3( 0, 0, 1 ), 9f );

		var hat = AddPart( Hat, Head, head.Go, "Hat", new Vector3( -1, 0, 35f ), new Angles( 0, 0, 9f ), HatHealth );
		Box( hat, new Vector3( 0, 0, 1 ), new Vector3( 34f, 34f, 2f ), Tux );
		Box( hat, new Vector3( 0, 0, 4 ), new Vector3( 22.5f, 22.5f, 5f ), new Color( 0.85f, 0.05f, 0.15f ) );
		Box( hat, new Vector3( 0, 0, 14 ), new Vector3( 22f, 22f, 24f ), Tux );
		Trigger( hat, new Vector3( 0, 0, 2 ), new Vector3( 0, 0, 24 ), 13f );

		// Arms: black sleeves, white gloves. The right hand carries the briefcase.
		foreach ( var (upper, lower, side) in new[] { (ArmL, ForearmL, 1f), (ArmR, ForearmR, -1f) } )
		{
			var arm = AddPart( upper, Torso, torso.Go, side > 0 ? "Arm L" : "Arm R", new Vector3( 0, side * 17f, 12f ), new Angles( 0, 0, -side * 12f ), LimbHealth );
			Sphere( arm, new Vector3( 0, 0, -10 ), new Vector3( 5.5f, 5.5f, 12f ), Tux );
			Trigger( arm, Vector3.Zero, new Vector3( 0, 0, -20 ), 6.5f );

			var fore = AddPart( lower, upper, arm.Go, side > 0 ? "Forearm L" : "Forearm R", new Vector3( 0, 0, -20 ), Angles.Zero, LimbHealth );
			Sphere( fore, new Vector3( 0, 0, -9 ), new Vector3( 5f, 5f, 10.5f ), Tux );
			Sphere( fore, new Vector3( 0, 0, -18 ), new Vector3( 5.4f, 5.4f, 2.5f ), Glove );
			Sphere( fore, new Vector3( 0, 0, -23 ), new Vector3( 5.2f, 5.2f, 5.6f ), Glove );
			Trigger( fore, Vector3.Zero, new Vector3( 0, 0, -26 ), 6f );
		}

		var briefcase = AddPart( Case, ForearmR, parts[ForearmR].Go, "Briefcase", new Vector3( 0, 0, -27f ), Angles.Zero, CaseHealth );
		Box( briefcase, new Vector3( 0, 0, -1.5f ), new Vector3( 4f, 7f, 2.5f ), Tux ); // handle
		Box( briefcase, new Vector3( 0, 0, -11f ), new Vector3( 7f, 24f, 15f ), new Color( 0.32f, 0.16f, 0.07f ) );
		Box( briefcase, new Vector3( 0, 0, -4.2f ), new Vector3( 7.4f, 24.4f, 1.4f ), new Color( 0.85f, 0.7f, 0.2f ) );
		lid = new GameObject( briefcase.Go, true, "Lid" );
		lid.LocalPosition = new Vector3( 3.6f, 0, -4f );
		Box( briefcase, new Vector3( 0, 0, -11f ), new Vector3( 1f, 23f, 14f ), new Color( 0.32f, 0.16f, 0.07f ), lid );
		Trigger( briefcase, new Vector3( 0, 0, -4 ), new Vector3( 0, 0, -18 ), 9f );

		// Legs: white trousers, black shoes, dangling as he floats.
		foreach ( var (upper, lower, side) in new[] { (LegL, ShinL, 1f), (LegR, ShinR, -1f) } )
		{
			var leg = AddPart( upper, Torso, torso.Go, side > 0 ? "Leg L" : "Leg R", new Vector3( 0, side * 7f, -18f ), Angles.Zero, LimbHealth );
			Sphere( leg, new Vector3( 0, 0, -11 ), new Vector3( 6.5f, 6.5f, 13f ), Glove );
			Trigger( leg, Vector3.Zero, new Vector3( 0, 0, -22 ), 7f );

			var shin = AddPart( lower, upper, leg.Go, side > 0 ? "Shin L" : "Shin R", new Vector3( 0, 0, -22 ), Angles.Zero, LimbHealth );
			Sphere( shin, new Vector3( 0, 0, -10 ), new Vector3( 5.8f, 5.8f, 12f ), Glove );
			Sphere( shin, new Vector3( 3.5f, 0, -21 ), new Vector3( 8f, 5f, 3.5f ), Tux );
			Trigger( shin, Vector3.Zero, new Vector3( 0, 0, -23 ), 6.5f );
		}

		// The beam, its glowing core, and the thin aiming line shown while charging.
		beam = BeamPiece( "Beam", BeamColor * 2.5f );
		beamCore = BeamPiece( "Beam Core", Color.White * 3f );
		aimLine = BeamPiece( "Aim Line", new Color( 1f, 0.1f, 0.3f ) * 2f );
	}

	Part AddPart( int index, int parent, GameObject parentGo, string name, Vector3 position, Angles angles, float hp )
	{
		var go = new GameObject( parentGo, true, $"MC61 {name}" );
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
		part?.Renderers.Add( (r, color) );
	}

	void Box( Part part, Vector3 at, Vector3 size, Color color, GameObject parent = null )
	{
		var go = new GameObject( parent ?? part.Go, true, "Shape" );
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

	GameObject BeamPiece( string name, Color color )
	{
		var go = new GameObject( rig, true, name );
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Shapes.Sphere;
		r.Tint = color;
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
		go.Enabled = false;
		return go;
	}

	protected override void OnDestroy()
	{
		if ( rig.IsValid() ) rig.Destroy();
	}

	// ---- Every frame --------------------------------------------------------

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

		if ( State != seenState )
		{
			seenState = State;
			localStateTime = 0f;
		}

		rig.WorldPosition = WorldPosition;
		rig.WorldRotation = WorldRotation;
		Animate();
		DrawBeam();
		Tint();
	}

	bool Has( int index ) => parts[index] is { Gone: false };

	/// <summary> Poses the rig: idle float, beam stance, card spin, or the worm. </summary>
	void Animate()
	{
		var t = Time.Now;
		var st = localStateTime.Relative;
		var torso = parts[Torso].Go;

		// Defaults: idle float with lazy swinging limbs.
		var torsoAngles = new Angles( MathF.Sin( t * 1.3f ) * 3f, 0, MathF.Sin( t * 0.9f ) * 4f );
		var headAngles = new Angles( 0, MathF.Sin( t * 0.7f ) * 8f, 0 );
		var armL = new Angles( MathF.Sin( t * 1.6f ) * 10f, 0, -12f );
		var armR = new Angles( MathF.Sin( t * 1.6f + 1.5f ) * 10f, 0, 12f );
		var foreL = new Angles( -15f, 0, 0 );
		var foreR = new Angles( -15f, 0, 0 );
		var legL = new Angles( MathF.Sin( t * 1.2f ) * 12f, 0, 0 );
		var legR = new Angles( MathF.Sin( t * 1.2f + 2f ) * 12f, 0, 0 );
		var shinL = new Angles( 15f + MathF.Sin( t * 1.2f ) * 8f, 0, 0 );
		var shinR = new Angles( 15f + MathF.Sin( t * 1.2f + 2f ) * 8f, 0, 0 );
		var bob = MathF.Sin( t * 1.8f ) * 3f;
		var lidOpen = 0f;
		var spin = 0f;

		switch ( State )
		{
			case Mode.Charge:
				// Leans back, head tipping down to glare.
				torsoAngles = new Angles( -8f - st * 3f, 0, 0 );
				headAngles = new Angles( 10f, 0, 0 );
				armL = new Angles( -30f, 0, -40f );
				armR = new Angles( -30f, 0, 40f );
				bob = MathF.Sin( t * 30f ) * 0.6f * st;
				break;

			case Mode.Beam:
				torsoAngles = new Angles( 6f, 0, 0 );
				headAngles = Angles.Zero;
				armL = new Angles( -10f, 0, -55f );
				armR = new Angles( -10f, 0, 55f );
				bob = MathF.Sin( t * 45f ) * 0.8f;
				break;

			case Mode.Cards:
				// Case arm up, lid open, and a full spin while the cards fly.
				armR = new Angles( -150f, 0, 25f );
				foreR = new Angles( 0, 0, 0 );
				lidOpen = MathX.Clamp( st / 0.5f, 0f, 1f );
				spin = MathX.Clamp( (st - 0.6f) / 1.1f, 0f, 1f ) * 360f;
				break;

			case Mode.Dance:
			{
				// The worm: flat on his belly, a wave rolling from feet to head.
				var w = t * 7f;
				torsoAngles = new Angles( 90f + MathF.Sin( w ) * 14f, 0, 0 );
				headAngles = new Angles( -70f + MathF.Sin( w - 1.2f ) * 20f, 0, 0 );
				armL = new Angles( -10f, 0, -60f + MathF.Sin( w ) * 10f );
				armR = new Angles( -10f, 0, 60f - MathF.Sin( w ) * 10f );
				legL = legR = new Angles( MathF.Sin( w + 1.4f ) * 30f, 0, 0 );
				shinL = shinR = new Angles( 20f + MathF.Sin( w + 2.4f ) * 40f, 0, 0 );
				bob = MathF.Max( 0f, MathF.Sin( w ) ) * 8f;
				break;
			}
		}

		torso.LocalPosition = new Vector3( 0, 0, bob );
		torso.LocalRotation = Rotation.FromYaw( spin ) * torsoAngles.ToRotation();
		Pose( Head, headAngles );
		Pose( ArmL, armL );
		Pose( ArmR, armR );
		Pose( ForearmL, foreL );
		Pose( ForearmR, foreR );
		Pose( LegL, legL );
		Pose( LegR, legR );
		Pose( ShinL, shinL );
		Pose( ShinR, shinR );
		if ( lid.IsValid() ) lid.LocalRotation = Rotation.FromPitch( -lidOpen * 110f );

		// Eye: swells and glows while charging and firing.
		if ( Has( Eye ) )
		{
			var glow = State == Mode.Charge ? 0.5f + 0.5f * MathF.Sin( t * 20f ) * st : State == Mode.Beam ? 1f : 0f;
			parts[Eye].Go.LocalScale = 1f + glow * 0.25f;
		}
	}

	void Pose( int index, Angles angles )
	{
		var part = parts[index];
		if ( part is null || part.Gone ) return;
		part.Go.LocalRotation = Rotation.Slerp( part.Go.LocalRotation, angles.ToRotation(), MathX.Clamp( Time.Delta * 12f, 0f, 1f ) );
	}

	void Tint()
	{
		var glowing = State is Mode.Charge or Mode.Beam;
		foreach ( var part in parts )
		{
			if ( part is null || part.Gone ) continue;
			var flash = MathX.Clamp( 1f - part.Flash / 0.1f, 0f, 1f );
			foreach ( var (renderer, tint) in part.Renderers )
			{
				if ( !renderer.IsValid() ) continue;
				var color = Color.Lerp( tint, tint * 0.5f, part.Hurt ).WithAlpha( 1f );
				if ( part.Index == Eye && glowing && tint == Iris ) color = Iris * (2f + MathF.Sin( Time.Now * 25f ));
				renderer.Tint = Color.Lerp( color, Color.White * 3f, flash );
			}

			// Track how fast each piece moves, so it flies off naturally when it comes loose.
			var position = part.Go.WorldPosition;
			part.Velocity = (position - part.LastPosition) / MathF.Max( Time.Delta, 0.0001f );
			part.LastPosition = position;
		}
	}

	Vector3 EyePosition => Has( Eye ) ? parts[Eye].Go.WorldPosition : WorldPosition + Vector3.Up * 40f;

	void DrawBeam()
	{
		var firing = State == Mode.Beam && Has( Eye );
		var aiming = State == Mode.Charge && Has( Eye );
		beam.Enabled = firing;
		beamCore.Enabled = firing;
		aimLine.Enabled = aiming;
		if ( !firing && !aiming ) return;

		var from = EyePosition;
		var to = BeamEnd;
		var flicker = 1f + 0.12f * MathF.Sin( Time.Now * 60f );
		if ( firing )
		{
			Stretch( beam, from, to, BeamWidth * flicker );
			Stretch( beamCore, from, to, BeamWidth * 0.4f );

			// Scorch and sparks where it lands.
			if ( scorchTime > 0.07f && gore is not null )
			{
				scorchTime = 0f;
				var dir = (to - from).Normal;
				if ( gore.TraceWorld( to - dir * 20f, to + dir * 40f ) is { } hit )
					gore.GooSplat( hit.Position, hit.Normal, Game.Random.Float( 0.5f, 0.9f ), new Color( 0.08f, 0.02f, 0.06f ) );
				gore.GooBurst( to - dir * 6f, -dir, BeamColor * 2f, 2, 260f, 0.6f );
			}
		}
		else
		{
			Stretch( aimLine, from, to, 1.2f + localStateTime.Relative * 1.2f );
		}
	}

	static void Stretch( GameObject go, Vector3 from, Vector3 to, float width )
	{
		var length = MathF.Max( 1f, from.Distance( to ) );
		go.WorldPosition = (from + to) * 0.5f;
		go.WorldRotation = Rotation.LookAt( to - from );
		go.WorldScale = new Vector3( length * 0.5f, width * 0.5f, width * 0.5f ) * Shapes.SphereScale( 1f );
	}

	// ---- Host brain -----------------------------------------------------------

	void SetState( Mode mode )
	{
		State = mode;
		stateTime = 0f;
	}

	void Think()
	{
		var dt = Time.Delta;
		var target = PlayerSurvivor.FindNearestAlive( Scene, WorldPosition );
		var hasEye = Has( Eye );
		var hasCase = Has( Case );

		switch ( State )
		{
			case Mode.Float:
			{
				Hover( target, dt, hasEye || hasCase ? PreferredDistance : 60f );

				// No tricks left: ram whoever is closest.
				if ( !hasEye && !hasCase && target is not null && ramTime > 1f && (target.WorldPosition + Vector3.Up * 40f).Distance( WorldPosition ) < 90f * Size )
				{
					ramTime = 0f;
					Hurt( target, RamDamage );
				}

				if ( target is not null && nextAttack ) ChooseAttack( target, hasEye, hasCase );
				break;
			}

			case Mode.Charge:
				velocity *= MathF.Exp( -6f * dt );
				Move( dt );
				AimBeam( target, dt, BeamTrackSpeed * 0.5f );
				if ( stateTime > ChargeTime ) { SetState( Mode.Beam ); beamTick = 0f; }
				break;

			case Mode.Beam:
				velocity *= MathF.Exp( -6f * dt );
				Move( dt );
				AimBeam( target, dt, BeamTrackSpeed );
				if ( beamTick >= 0.1f )
				{
					beamTick = 0f;
					BeamDamage( BeamDamagePerSecond * 0.1f );
				}
				if ( stateTime > BeamTime || !hasEye ) EndAttack();
				break;

			case Mode.Cards:
				velocity *= MathF.Exp( -6f * dt );
				Move( dt );
				// Rings fly out during the spin.
				var due = (int)MathF.Floor( (stateTime - 0.6f) / 0.35f ) + 1;
				while ( hasCase && cardRingsThrown < Math.Min( due, CardRings ) )
				{
					var origin = parts[Case].Go.WorldPosition;
					// Tilt the ring down (or up) so it sweeps through the target's body height, the
					// second ring a little lower than the first so jumping over one doesn't dodge both.
					var slope = 0f;
					if ( target is not null )
					{
						var toTarget = target.WorldPosition + Vector3.Up * (50f - cardRingsThrown * 25f) - origin;
						slope = toTarget.z / MathF.Max( 100f, toTarget.WithZ( 0 ).Length );
					}
					CardsEverywhere( origin, Game.Random.Int( 0, 1000000 ), cardRingsThrown * 360f / CardsPerRing * 0.5f, slope );
					cardRingsThrown++;
				}
				if ( stateTime > 1.9f || !hasCase ) EndAttack();
				break;

			case Mode.Dance:
			{
				// Down to the floor, wiggle, back up.
				var floor = Ground( WorldPosition ) + 18f * Size;
				var goal = WorldPosition.WithZ( floor );
				WorldPosition = Vector3.Lerp( WorldPosition, goal, MathX.Clamp( dt * 4f, 0f, 1f ) );
				velocity = Vector3.Zero;
				if ( stateTime > DanceTime ) EndAttack();
				break;
			}
		}

		// Always face his target (during the beam, the beam's direction).
		var face = State == Mode.Beam ? beamDirection.WithZ( 0 ) : target is not null ? (target.WorldPosition - WorldPosition).WithZ( 0 ) : Vector3.Zero;
		if ( State != Mode.Dance && !face.IsNearlyZero() )
			WorldRotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( face, Vector3.Up ), MathX.Clamp( dt * 4f, 0f, 1f ) );
	}

	void ChooseAttack( PlayerSurvivor target, bool hasEye, bool hasCase )
	{
		var options = new List<Mode>();
		if ( hasEye && CanSee( target ) ) options.Add( Mode.Beam );
		if ( hasCase ) options.Add( Mode.Cards );

		if ( Game.Random.Float() < DanceChance || (options.Count == 0 && Game.Random.Float() < 0.3f) )
		{
			Say( "*breaks out the worm*" );
			SetState( Mode.Dance );
			return;
		}
		if ( options.Count == 0 ) { nextAttack = 1f; return; }

		var pick = Game.Random.FromList( options );
		if ( pick == Mode.Beam )
		{
			Say( "I SEEE YOUUU" );
			// Start a little off to the side, so the beam sweeps in rather than snapping onto you.
			var toTarget = (target.WorldPosition + Vector3.Up * 40f - EyePosition).Normal;
			beamDirection = (Rotation.FromYaw( Game.Random.Float( -25f, 25f ) ) * toTarget).Normal;
			UpdateBeamEnd();
			SetState( Mode.Charge );
		}
		else
		{
			Say( "Pick any card!" );
			cardRingsThrown = 0;
			SetState( Mode.Cards );
		}
	}

	void EndAttack()
	{
		SetState( Mode.Float );
		nextAttack = Game.Random.Float( AttackGap.x, AttackGap.y );
	}

	bool CanSee( PlayerSurvivor target )
	{
		var tr = Scene.Trace.Ray( EyePosition, target.WorldPosition + Vector3.Up * 50f )
			.WithoutTags( "zombie", "zombieblocker", "gib", "player", "viewmodel", "creature" )
			.IgnoreDynamic()
			.Run();
		return !tr.Hit;
	}

	void AimBeam( PlayerSurvivor target, float dt, float degreesPerSecond )
	{
		if ( target is not null )
		{
			var wanted = (target.WorldPosition + Vector3.Up * 40f - EyePosition).Normal;
			var angle = MathF.Acos( MathX.Clamp( Vector3.Dot( beamDirection, wanted ), -1f, 1f ) ).RadianToDegree();
			if ( angle > 0.01f )
			{
				var step = MathF.Min( 1f, degreesPerSecond * dt / angle );
				beamDirection = Vector3.Slerp( beamDirection, wanted, step ).Normal;
			}
		}
		UpdateBeamEnd();
	}

	void UpdateBeamEnd()
	{
		var from = EyePosition;
		var tr = Scene.Trace.Ray( from, from + beamDirection * BeamRange )
			.WithoutTags( "zombie", "zombieblocker", "gib", "player", "viewmodel", "creature" )
			.IgnoreDynamic()
			.Run();
		BeamEnd = tr.Hit ? tr.HitPosition : tr.EndPosition;
	}

	/// <summary> Anyone the beam passes through (head to toe) gets burned. </summary>
	void BeamDamage( float amount )
	{
		var from = EyePosition;
		var to = BeamEnd;
		foreach ( var player in Scene.GetAllComponents<PlayerSurvivor>() )
		{
			if ( !player.IsAlive ) continue;
			var feet = player.WorldPosition;
			var closest = float.MaxValue;
			for ( var h = 10f; h <= 70f; h += 15f )
				closest = MathF.Min( closest, DistanceToSegment( feet + Vector3.Up * h, from, to ) );
			if ( closest < BeamWidth + 14f ) Hurt( player, amount );
		}
	}

	static float DistanceToSegment( Vector3 point, Vector3 a, Vector3 b )
	{
		var ab = b - a;
		var t = ab.LengthSquared > 0.001f ? MathX.Clamp( Vector3.Dot( point - a, ab ) / ab.LengthSquared, 0f, 1f ) : 0f;
		return point.Distance( a + ab * t );
	}

	void Hurt( PlayerSurvivor player, float amount )
	{
		player.Health?.OnDamage( new DamageInfo
		{
			Damage = amount,
			Attacker = GameObject,
			Position = player.WorldPosition + Vector3.Up * 50f,
			Origin = WorldPosition,
		} );
	}

	void Hover( PlayerSurvivor target, float dt, float distance )
	{
		var goal = WorldPosition;
		if ( target is not null )
		{
			var flat = (WorldPosition - target.WorldPosition).WithZ( 0 );
			if ( flat.IsNearlyZero() ) flat = Vector3.Forward;
			var around = Rotation.FromYaw( orbitSign * 12f * dt * 10f ) * flat.Normal;
			goal = target.WorldPosition + around * distance + Vector3.Up * HoverHeight;
		}
		var wanted = (goal - WorldPosition).ClampLength( MoveSpeed );
		velocity = Vector3.Lerp( velocity, wanted, MathX.Clamp( dt * 2f, 0f, 1f ) );
		Move( dt );
	}

	void Move( float dt )
	{
		var position = WorldPosition;
		var step = velocity * dt;
		if ( !step.IsNearlyZero() )
		{
			var tr = Scene.Trace.Sphere( 30f * Size, position, position + step + step.Normal * 20f )
				.WithoutTags( "zombie", "zombieblocker", "gib", "player", "viewmodel", "creature" )
				.IgnoreDynamic()
				.Run();
			if ( tr.Hit )
			{
				velocity -= tr.Normal * Vector3.Dot( velocity, tr.Normal ) * 1.2f;
				step = velocity * dt;
			}
		}
		var next = position + step;
		var floor = Ground( next ) + 70f * Size;
		if ( next.z < floor ) next = next.WithZ( MathX.Lerp( next.z, floor, MathX.Clamp( dt * 5f, 0f, 1f ) ) );
		WorldPosition = next;
	}

	float Ground( Vector3 at )
	{
		var tr = Scene.Trace.Ray( at + Vector3.Up * 10f, at + Vector3.Down * 1000f )
			.WithoutTags( "zombie", "zombieblocker", "gib", "player", "viewmodel", "creature" )
			.IgnoreDynamic()
			.Run();
		return tr.Hit ? tr.HitPosition.z : at.z - 150f;
	}

	// ---- Speech and cards (everyone) ------------------------------------------

	[Rpc.Broadcast]
	void Say( string line )
	{
		Line = line;
		TimeSinceLine = 0f;
	}

	/// <summary> A ring of cards from the briefcase. Each machine flies its own copies; the host's do the harm. </summary>
	[Rpc.Broadcast]
	void CardsEverywhere( Vector3 origin, int seed, float angleOffset, float slope )
	{
		var random = new System.Random( seed );
		var count = Math.Max( 1, CardsPerRing );
		for ( var i = 0; i < count; i++ )
		{
			var yaw = angleOffset + i * 360f / count;
			var dir = (Rotation.FromYaw( yaw ).Forward + Vector3.Up * (slope + (float)random.NextDouble() * 0.06f - 0.03f)).Normal;
			var kind = (CardKind)random.Next( 0, 4 );
			Card.Spawn( Scene, origin + dir * 12f, dir * CardSpeed, kind, authority: !IsProxy, CardDamage, GameObject );
		}
	}

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
		if ( index < 0 || index >= parts.Length || !Has( index ) ) index = Torso;
		lastShotDirection = direction;

		if ( State == Mode.Dance ) damage *= DanceDamageScale;
		var part = parts[index];
		var crit = index is Head or Eye;

		var multiplier = index switch
		{
			Head => 1f,
			Eye => EyeMultiplier,
			Torso => TorsoMultiplier,
			Hat => 0.2f,
			_ => LimbMultiplier,
		};

		var cut = false;
		if ( part.MaxHealth > 0f && index != Torso )
		{
			part.Health -= damage;
			cut = part.Health <= 0f;
		}
		var hurt = index is Head or Torso
			? 1f - MathX.Clamp( health.Current / MathF.Max( 1f, health.MaxHealth ), 0f, 1f )
			: 1f - MathX.Clamp( part.Health / MathF.Max( 1f, part.MaxHealth ), 0f, 1f );

		if ( cut )
		{
			GoneMask |= SubtreeMask( index );
			CutEverywhere( index, position, direction );
		}
		else
		{
			HitEverywhere( index, position, direction, Game.Random.Float() < 0.35f, hurt );
		}

		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * multiplier, Position = position, Origin = origin, Attacker = shooter?.GameObject } );
		var killed = wasAlive && IsDead;

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
			{
				if ( p == index ) { mask |= 1 << i; break; }
			}
		}
		return mask;
	}

	/// <summary> Rainbow goo: he's magical. </summary>
	Color Goo => Shapes.Hsv( Game.Random.Float(), 0.85f, 1f );

	[Rpc.Broadcast( NetFlags.Unreliable )]
	void HitEverywhere( int index, Vector3 position, Vector3 direction, bool chip, float hurt )
	{
		var part = index >= 0 && index < parts.Length ? parts[index] : null;
		if ( part is null ) return;
		part.Flash = 0f;
		part.Hurt = MathF.Max( part.Hurt, hurt * 0.8f );

		var goo = Goo;
		if ( !part.Gone && part.Renderers.Count > 0 )
		{
			CreatureGore.AddWound( part.Go, position, direction, part.Renderers[0].Tint, goo, index is Torso or Head ? 4f : 2.5f );
			if ( index != Eye ) CreatureGore.Jiggle( part.Go, index is Head ? 0.8f : 0.6f );
		}
		gore?.GooBurst( position, -direction, goo, 7, 240f, 1f );
		gore?.GooSpray( position, direction, 2, goo, 200f, 0.5f, 1f );
		if ( chip && index is Torso or Head )
			gore?.ThrowBlob( position, -direction, index == Head ? Skin : Tux, goo, 3.5f * Size );
	}

	[Rpc.Broadcast]
	void CutEverywhere( int index, Vector3 position, Vector3 direction ) => CutLocal( index, direction, quiet: false );

	/// <summary> A piece comes off with everything hanging from it (an arm takes the briefcase with it). </summary>
	void CutLocal( int index, Vector3 direction, bool quiet )
	{
		var part = parts[index];
		if ( part is null || part.Gone ) return;

		var mask = SubtreeMask( index );
		for ( var i = 0; i < parts.Length; i++ )
			if ( (mask & (1 << i)) != 0 && parts[i] is not null ) parts[i].Gone = true;

		if ( quiet ) { part.Go.Destroy(); return; }

		var at = part.Go.WorldPosition;
		AddStumps( part, index == Eye ? 0.5f : 1f );
		for ( var k = 0; k < 3; k++ ) gore?.GooBurst( at, direction, Goo, 6, 300f, 1.2f );
		gore?.GooSpray( at, direction, 4, Goo, 240f, 0.6f, 1.3f );

		switch ( index )
		{
			case Eye:
				// Popped: a burst of white and pink, and an empty socket.
				gore?.GooBurst( at, direction, Glove, 12, 260f, 1.2f );
				gore?.GooBurst( at, direction, Iris, 8, 260f, 1f );
				part.Go.Destroy();
				Say( "MY EYEEE" );
				return;

			case Case:
				// Bursts open: a harmless confetti of cards.
				CardConfetti( at );
				Detach( part, direction * 120f + Vector3.Up * 150f );
				if ( !IsProxy ) Say( "My cards!" );
				return;

			case Hat:
				Detach( part, direction * 80f + Vector3.Up * 260f, spin: 25f );
				return;
		}

		// Losing the arm with the case also loses the case trick.
		if ( (mask & (1 << Case)) != 0 ) CardConfetti( at );
		Detach( part, direction * Game.Random.Float( 80f, 180f ) + Vector3.Up * 80f );
	}

	/// <summary> Raw stumps on both sides of a cut: on what he keeps, and on the piece that flies off. </summary>
	void AddStumps( Part part, float scale )
	{
		if ( part.Index == Hat || part.Index == Case ) return;
		var parent = part.Parent >= 0 ? parts[part.Parent] : null;
		var joint = part.Go.WorldPosition;
		var outward = parent is not null ? (joint - parent.Go.WorldPosition).Normal : Vector3.Up;
		var radius = (part.Index is ArmL or ArmR or ForearmL or ForearmR ? 5.5f : part.Index is LegL or LegR or ShinL or ShinR ? 6.5f : 7f) * Size * scale;
		var skin = part.Renderers.Count > 0 ? part.Renderers[0].Tint : Tux;
		if ( parent is not null && !parent.Gone ) CreatureGore.AddStump( parent.Go, joint, outward, radius, skin, Goo );
		if ( part.Index != Eye ) CreatureGore.AddStump( part.Go, joint, -outward, radius, skin, Goo, 2f );
	}

	void CardConfetti( Vector3 at )
	{
		for ( var i = 0; i < 14; i++ )
		{
			var dir = (Vector3.Random + Vector3.Up * 0.8f).Normal;
			Card.Spawn( Scene, at, dir * Game.Random.Float( 150f, 350f ), (CardKind)Game.Random.Int( 0, 3 ), authority: false, 0f, GameObject, confetti: true );
		}
	}

	/// <summary> Lets a piece (and anything hanging off it) go as one loose physics chunk. </summary>
	void Detach( Part part, Vector3 push, float spin = 10f )
	{
		var go = part.Go;
		go.SetParent( null, true );
		foreach ( var p in go.Components.GetAll<CreaturePart>( FindMode.EverythingInSelfAndDescendants ).ToList() ) p.Destroy();
		foreach ( var c in go.Components.GetAll<Collider>( FindMode.EverythingInSelfAndDescendants ).ToList() ) c.Destroy();
		foreach ( var o in go.GetAllObjects( true ) ) o.Tags.Remove( "creature" );

		var bounds = go.GetBounds();
		var radius = MathX.Clamp( bounds.Size.Length * 0.25f, 4f, 30f );
		var gib = Gib.Attach( go, gore, radius, 40f, 6f, part.Velocity * 0.4f + push, Goo );
		var body = go.Components.Get<Rigidbody>();
		if ( body.IsValid() ) body.AngularVelocity = Vector3.Random * spin;
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
		Killed?.Invoke( this );
		DieEverywhere( lastShotDirection );
	}

	[Rpc.Broadcast]
	void DieEverywhere( Vector3 direction ) => DieLocal( direction, quiet: false );

	/// <summary> The head bursts in rainbow goo, cards everywhere, and the rest of him falls apart. </summary>
	void DieLocal( Vector3 direction, bool quiet )
	{
		if ( diedLocally ) return;
		diedLocally = true;
		timeSinceDeath = 0f;
		if ( !rig.IsValid() ) return;

		if ( quiet ) { rig.Destroy(); return; }

		beam?.Destroy();
		beamCore?.Destroy();
		aimLine?.Destroy();

		var headAt = parts[Head].Go.WorldPosition + Vector3.Up * 18f * Size;
		for ( var k = 0; k < 6; k++ ) gore?.GooBurst( headAt, direction + Vector3.Up, Goo, 10, 380f, 1.6f );
		gore?.GooSpray( headAt, Vector3.Down, 8, Goo, 300f, 0.8f, 1.8f );
		for ( var b = 0; b < 4; b++ ) gore?.ThrowBlob( headAt, direction, Skin, Goo, 5f * Size );
		CardConfetti( headAt );

		// Hat and limbs fly off; the head is gone; the torso drops.
		foreach ( var index in new[] { Hat, ArmL, ArmR, LegL, LegR } )
		{
			if ( !Has( index ) ) continue;
			var part = parts[index];
			MarkGone( index );
			Detach( part, (direction + Vector3.Random + Vector3.Up).Normal * Game.Random.Float( 150f, 300f ), spin: 20f );
		}

		if ( Has( Head ) ) { MarkGone( Head ); parts[Head].Go.Destroy(); }
		if ( Has( Torso ) ) { MarkGone( Torso ); Detach( parts[Torso], direction * 100f ); }
		rig.Destroy();
	}

	void MarkGone( int index )
	{
		var mask = SubtreeMask( index );
		for ( var i = 0; i < parts.Length; i++ )
			if ( (mask & (1 << i)) != 0 && parts[i] is not null ) parts[i].Gone = true;
	}
}
