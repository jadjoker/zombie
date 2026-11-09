/// <summary>
/// Sea Spider: a pale egg of a body on six long, high-kneed legs, with one huge eye. It stalks across the
/// sea floor and stabs at you with its front legs.
///
/// Chunking: every leg is two parts (thigh and shin) and can be shot off; it slows with each one lost and
/// drops to the floor, dragging itself, once most are gone. The big eye is the weak spot (extra damage),
/// and with it popped the spider is blind and stumbles about. The body is lethal.
///
/// Networking is the same as the Junk Crab: the host walks it on the navmesh; every machine builds and
/// animates the rig from the synced velocity and state. Cuts are broadcast; a mask lets late joiners catch up.
/// </summary>
public sealed class SeaSpider : Component, ICreature
{
	[Property, Group( "Movement" )] public float MoveSpeed { get; set; } = 230f;
	[Property, Group( "Attack" )] public float AttackRange { get; set; } = 95f;
	[Property, Group( "Attack" )] public float StabDamage { get; set; } = 12f;
	[Property, Group( "Attack" )] public float AttackCooldown { get; set; } = 0.9f;

	[Property, Group( "Damage" )] public float ThighHealth { get; set; } = 22f;
	[Property, Group( "Damage" )] public float ShinHealth { get; set; } = 16f;
	[Property, Group( "Damage" )] public float EyeHealth { get; set; } = 35f;
	/// <summary> Body damage multiplier for hits on the eye: it's the weak spot. </summary>
	[Property, Group( "Damage" )] public float EyeMultiplier { get; set; } = 1.6f;
	/// <summary> How much a leg hit hurts the whole spider. Low, so you can strip its legs before it dies. </summary>
	[Property, Group( "Damage" )] public float LimbMultiplier { get; set; } = 0.12f;

	[Sync] public float Hue { get; set; } = -1f;
	[Sync] public float Size { get; set; } = 1f;
	[Sync] public Vector3 NetVelocity { get; set; }
	[Sync] public bool Stabbing { get; set; }
	[Sync] public int GoneMask { get; set; }

	// Ally mode: a spider on your side. It follows its owner and fights what you point it at.
	public const int ModeFollow = 0, ModeHold = 1, ModeAttack = 2;
	[Sync] public bool IsAlly { get; set; }
	[Sync] public GameObject OwnerPlayer { get; set; }
	[Sync] public int AllyMode { get; set; }
	[Sync] public Vector3 HoldPoint { get; set; }
	[Sync] public GameObject AttackTarget { get; set; }

	public bool IsDead => health?.IsDead ?? false;
	/// <summary> Where the glowing bulb is: strands start here. </summary>
	public Vector3 LureTip => lureBulb.IsValid() ? lureBulb.WorldPosition : WorldPosition + Vector3.Up * 60f * Size;
	public event System.Action<SeaSpider> Killed;

	const int Body = 0, EyePart = 1, FirstThigh = 2, LegCount = 6, FirstShin = FirstThigh + LegCount;
	const float BodyHeight = 43f;
	const float ThighLength = 22f, ShinLength = 58f;
	static readonly float[] LegYaws = { 48f, 92f, 132f, -48f, -92f, -132f };

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

	readonly Part[] parts = new Part[FirstShin + LegCount];
	GameObject rig;
	Health health;
	GoreSystem gore;
	NavMeshAgent agent;

	Color shellColor, darkColor, gooColor, glowColor;
	TimeSince timeSinceAttack = 999f;
	TimeSince timeSinceCharging = 999f;
	TimeSince timeSinceRepath = 999f;
	TimeSince timeSinceStab = 999f;
	Vector3 wanderGoal;
	bool diedLocally;
#pragma warning disable SB3003 // Host-only death timer; a reset on host migration is harmless.
	TimeSince timeSinceDeath;
#pragma warning restore SB3003
	Vector3 lastShotDirection = Vector3.Forward;
	TimeSince bleedTimer;
	bool lastShotPointBlank;
	float gait;

	// The one big eye.
	const float EyeRadius = 8.5f;
	const float LidRadius = EyeRadius * 1.07f;
	GameObject eyeBall, eyeGaze, eyePupil, upperLid, lowerLid;
	Rotation gazeRot = Rotation.Identity;
	Vector3 wander = Vector3.Forward;
	float lidUpper = 0.15f, lidLower = 0.05f, lidSlant, pupilSize = 1f;
	TimeUntil nextBlink = 2f;
	TimeUntil nextGlance = 1f;
	float blink = 1f;   // 0 = mid-blink, 1 = open

	// ---- Setup ------------------------------------------------------------

	protected override void OnStart()
	{
		health = GetComponent<Health>();
		gore = GoreSystem.Get( Scene );
		agent = GetComponent<NavMeshAgent>();

		if ( !IsProxy && Hue < 0f ) Hue = Game.Random.Float();
		var hue = Hue < 0f ? 0.5f : Hue;
		// Pale pink-white like bone, with a bright magenta goo.
		shellColor = Shapes.Hsv( MathX.Lerp( 0.92f, 1.04f, hue ) % 1f, 0.17f, 0.95f );
		darkColor = Color.Lerp( shellColor, new Color( 0.25f, 0.08f, 0.22f ), 0.45f );
		gooColor = Shapes.Hsv( 0.88f + hue * 0.06f, 0.95f, 1f );
		glowColor = Shapes.Hsv( 0.08f, 0.85f, 1f );   // hostile: warm amber
		if ( IsAlly )
		{
			glowColor = Shapes.Hsv( 0.47f, 0.85f, 1f );   // friendly: cool aqua
			// Friendly colours: minty white with a teal underside.
			shellColor = Shapes.Hsv( 0.46f, 0.2f, 0.97f );
			darkColor = Color.Lerp( shellColor, new Color( 0.05f, 0.35f, 0.4f ), 0.6f );
			gooColor = Shapes.Hsv( 0.5f, 0.8f, 1f );
		}

		if ( agent.IsValid() )
		{
			agent.UpdateRotation = false;
			if ( IsProxy ) agent.Enabled = false;
		}
		if ( health.IsValid() && !IsProxy ) health.Died += OnDied;
		Build();
	}

	void Build()
	{
		rig = new GameObject( true, "Spider Rig" );
		rig.NetworkMode = NetworkMode.Never;
		rig.WorldTransform = WorldTransform;
		rig.WorldScale = Size;

		var body = AddPart( Body, -1, rig, "Body", new Vector3( 0, 0, BodyHeight ), Angles.Zero, 0f );
		// The sculpted egg (eye socket, chin, snout, hip sockets), modelled in Blender.
		Sculpt( body, "body", shellColor );
		Trigger( body, new Vector3( 0, 0, -1f ), new Vector3( 0, 0, 1f ), 16f );

		// The eye, on the front of the egg.
		var eye = AddPart( EyePart, Body, body.Go, "Eye", new Vector3( 12f, 0, 4.5f ), Angles.Zero, EyeHealth );
		BuildEye( eye );
		Trigger( eye, new Vector3( 1f, 0, 0 ), new Vector3( 2f, 0, 0 ), EyeRadius + 0.5f );

		// Six legs, three a side: thigh up and out to a high knee, shin down and out to the floor.
		var yaws = LegYaws;
		for ( var i = 0; i < LegCount; i++ )
		{
			var outward = Rotation.FromYaw( yaws[i] ).Forward;
			var hip = outward * 12f + Vector3.Up * 2f;
			var thighDir = (outward * 0.62f + Vector3.Up * 0.78f).Normal;
			var shinDir = (outward * 0.42f + Vector3.Down * 0.9f).Normal;
			var thighRot = Rotation.LookAt( thighDir, Vector3.Up );
			var shinRot = Rotation.LookAt( shinDir, Vector3.Up );

			const float thighLength = ThighLength, shinLength = ShinLength;
			var thigh = AddPart( FirstThigh + i, Body, body.Go, $"Thigh {i}", hip, thighRot.Angles(), ThighHealth );
			Sculpt( thigh, "thigh", shellColor );
			Trigger( thigh, Vector3.Zero, new Vector3( thighLength, 0, 0 ), 4f );

			var shin = AddPart( FirstShin + i, FirstThigh + i, thigh.Go, $"Shin {i}", new Vector3( thighLength, 0, 0 ), (thighRot.Inverse * shinRot).Angles(), ShinHealth );
			Sculpt( shin, "shin", darkColor );
			Trigger( shin, Vector3.Zero, new Vector3( shinLength, 0, 0 ), 3.4f );
		}

		foreach ( var part in parts )
			if ( part is not null ) part.Rest = part.Go.LocalRotation;

		BuildLure();
	}

	Part AddPart( int index, int parent, GameObject parentGo, string name, Vector3 position, Angles angles, float hp )
	{
		var go = new GameObject( parentGo, true, $"Spider {name}" );
		go.LocalPosition = position;
		go.LocalRotation = angles.ToRotation();
		if ( !IsAlly ) go.Tags.Add( "creature" );   // allies can't be shot, so bullets pass through them
		var part = new Part { Index = index, Parent = parent, Go = go, Health = hp, MaxHealth = hp };
		parts[index] = part;
		var shootable = go.AddComponent<CreaturePart>();
		shootable.Creature = this;
		shootable.Index = index;
		return part;
	}

	/// <summary> A part model sculpted in Blender (models/creatures/spider), tinted, skinned, flashing with the part. </summary>
	void Sculpt( Part part, string model, Color color )
	{
		var go = new GameObject( part.Go, true, "Shape" );
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Model.Load( $"models/creatures/spider/{model}.vmdl" );
		r.Tint = color;
		Shapes.Skin( r, "smooth" );
		part.Renderers.Add( (r, color) );
	}

	void Sphere( Part part, Vector3 at, Vector3 radii, Color color, bool skin ) => Sphere( part.Go, part, at, radii, color, skin );

	void Sphere( GameObject parent, Part part, Vector3 at, Vector3 radii, Color color, bool skin )
	{
		var go = new GameObject( parent, true, "Shape" );
		go.LocalPosition = at;
		go.LocalScale = radii * Shapes.SphereScale( 1f );
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Shapes.Sphere;
		r.Tint = color;
		if ( skin ) Shapes.Skin( r, "smooth" );
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

	// ---- The lure: an angler-fish antenna with a glowing bulb, simulated as a springy chain ----------

	const int LureNodes = 8;            // 7 segments
	const float LureSegment = 7.2f;
	readonly Vector3[] lureP = new Vector3[LureNodes];
	readonly Vector3[] lureOld = new Vector3[LureNodes];
	readonly Vector3[] lureRest = new Vector3[LureNodes];
	readonly GameObject[] lureSeg = new GameObject[LureNodes - 1];
	readonly GameObject[] lureJoint = new GameObject[LureNodes - 1];
	GameObject lureBulb;
	PointLight lureLight;
	ModelRenderer lureBulbRenderer;
	bool lureReady;
	Vector3 lureKick, lureAnchor;

	void BuildLure()
	{
		var stalk = Color.Lerp( shellColor, darkColor, 0.45f );
		for ( var i = 0; i < LureNodes - 1; i++ )
		{
			var seg = new GameObject( rig, true, "Lure Segment" );
			var r = seg.AddComponent<ModelRenderer>();
			r.Model = ShapeMeshes.Spindle( 0.78f, 0f );
			r.Tint = stalk;
			Shapes.Skin( r, "smooth" );
			lureSeg[i] = seg;

			var joint = new GameObject( rig, true, "Lure Joint" );
			var jr = joint.AddComponent<ModelRenderer>();
			jr.Model = Shapes.Sphere;
			jr.Tint = stalk;
			Shapes.Skin( jr, "smooth" );
			lureJoint[i] = joint;
		}

		lureBulb = new GameObject( rig, true, "Lure Bulb" );
		lureBulbRenderer = lureBulb.AddComponent<ModelRenderer>();
		lureBulbRenderer.Model = Shapes.Sphere;
		lureBulbRenderer.Tint = Color.White;
		var glowMaterial = Material.Load( "materials/creature/glowbulb.vmat" );
		if ( glowMaterial is not null && !glowMaterial.IsError ) lureBulbRenderer.MaterialOverride = glowMaterial;
		else lureBulbRenderer.Tint = glowColor * 4f;
		lureBulbRenderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		lureLight = lureBulb.AddComponent<PointLight>();
		lureLight.LightColor = glowColor * 2.5f;
		lureLight.Radius = 380f;
		lureLight.Attenuation = 1.1f;
		lureLight.Shadows = false;
	}

	/// <summary> Where each node of the antenna would sit if it were stiff: up from the head, arcing forward over the eye. </summary>
	void ComputeLureRest()
	{
		var body = parts[Body].Go;
		var local = new Vector3( 3f, 0f, 14.5f );
		for ( var i = 0; i < LureNodes; i++ )
		{
			if ( i > 0 )
			{
				var t = (i - 0.5f) / (LureNodes - 1);
				var theta = MathX.DegreeToRadian( 6f + 76f * t );   // from straight up toward forward
				local += new Vector3( MathF.Sin( theta ), 0f, MathF.Cos( theta ) ) * LureSegment;
			}
			lureRest[i] = body.WorldPosition + body.WorldRotation * (local * Size);
		}
	}

	void UpdateLure()
	{
		if ( lureBulb is null || !lureBulb.IsValid() || !Has( Body ) ) return;
		var dt = MathF.Min( Time.Delta, 1f / 30f );
		if ( dt <= 0f ) return;

		ComputeLureRest();
		if ( !lureReady || (lureP[1] - lureRest[1]).Length > 220f * Size )
		{
			for ( var i = 0; i < LureNodes; i++ ) { lureP[i] = lureRest[i]; lureOld[i] = lureRest[i]; }
			lureReady = true;
			lureAnchor = lureRest[0];
		}

		var body = parts[Body].Go;
		var segment = LureSegment * Size;
		// The body's own movement doesn't fully carry the antenna along: it drags behind and swings.
		var anchorMove = lureRest[0] - lureAnchor;
		lureAnchor = lureRest[0];
		if ( anchorMove.Length > 60f ) anchorMove = Vector3.Zero;   // teleport
		lureP[0] = lureRest[0];
		lureOld[0] = lureRest[0];

		// Verlet: each node keeps its momentum (so the tip swings and lags behind the body), is pulled
		// softly back toward its rest spot (softer toward the tip), and wobbles a little on its own.
		for ( var i = 1; i < LureNodes; i++ )
		{
			var along = i / (float)(LureNodes - 1);
			var velocity = (lureP[i] - lureOld[i]) * 0.94f - anchorMove * (0.16f * along);
			lureOld[i] = lureP[i];

			var wobble = body.WorldRotation * new Vector3( 0f, MathF.Sin( Time.Now * 2.3f + i * 0.7f ), MathF.Sin( Time.Now * 1.7f + i * 1.1f ) * 0.6f ) * (0.9f * i * Size);
			var target = lureRest[i] + wobble;
			var stiffness = 4.2f * (1f - 0.5f * along);
			var pull = (target - lureP[i]) * (1f - MathF.Exp( -stiffness * dt ));

			lureP[i] += velocity + pull + Vector3.Down * 50f * dt * dt + lureKick * dt * along;
		}
		lureKick = Vector3.Zero;

		// Keep every segment its proper length.
		for ( var iteration = 0; iteration < 5; iteration++ )
		{
			for ( var i = 1; i < LureNodes; i++ )
			{
				var d = lureP[i] - lureP[i - 1];
				var length = d.Length;
				if ( length < 0.0001f ) continue;
				var correction = d * ((length - segment) / length);
				if ( i - 1 == 0 ) lureP[i] -= correction;
				else { lureP[i] -= correction * 0.5f; lureP[i - 1] += correction * 0.5f; }
			}
		}

		// Draw it.
		for ( var i = 0; i < LureNodes - 1; i++ )
		{
			var a = lureP[i];
			var b = lureP[i + 1];
			var d = b - a;
			var length = d.Length;
			if ( length < 0.001f ) continue;
			var radius = MathX.Lerp( 1.7f, 0.8f, i / (float)(LureNodes - 2) ) * Size;
			lureSeg[i].WorldPosition = a;
			lureSeg[i].WorldRotation = Rotation.LookAt( d / length, Vector3.Up );
			lureSeg[i].WorldScale = new Vector3( length, radius, radius );
			lureJoint[i].WorldPosition = a;
			lureJoint[i].WorldScale = Vector3.One * Shapes.SphereScale( radius * 1.05f );
		}

		// The bulb: breathes slowly, flares when the spider fights.
		var tip = lureP[LureNodes - 1];
		var glow = 1f + 0.22f * MathF.Sin( Time.Now * 2.6f ) + 0.08f * MathF.Sin( Time.Now * 9.1f );
		if ( Stabbing || timeSinceStab < 0.5f ) glow *= 1.7f;
		if ( IsAlly && timeSinceCharging < 0.3f ) glow *= 2f;      // lit up while it's charging a diver
		lureBulb.WorldPosition = tip;
		lureBulb.WorldScale = Vector3.One * Shapes.SphereScale( 4.4f * Size );
		lureBulbRenderer.Attributes.Set( "GlowColor", new Vector3( glowColor.r, glowColor.g, glowColor.b ) );
		lureBulbRenderer.Attributes.Set( "GlowStrength", 1.7f * glow );
		lureLight.LightColor = glowColor * (2.6f * glow);
	}

	// ---- The eye ---------------------------------------------------------------

	/// <summary> A huge cartoon eye: pale ball, a big black pupil with a shine that follows you, and lids that blink and glare. </summary>
	void BuildEye( Part part )
	{
		eyeBall = new GameObject( part.Go, true, "Eyeball" );
		Sphere( eyeBall, part, Vector3.Zero, new Vector3( EyeRadius, EyeRadius, EyeRadius ), new Color( 0.98f, 0.96f, 0.84f ), skin: false );

		eyeGaze = new GameObject( eyeBall, true, "Gaze" );
		eyePupil = new GameObject( eyeGaze, true, "Pupil" );
		var k = EyeRadius / 3.4f;
		Sphere( eyePupil, part, new Vector3( EyeRadius * 0.9f, 0, 0 ), new Vector3( 0.42f, 1.55f, 1.75f ) * k, new Color( 0.02f, 0.02f, 0.03f ), skin: false );
		Sphere( eyePupil, part, new Vector3( EyeRadius * 0.98f, 1.0f, 1.4f ), new Vector3( 0.2f, 0.42f, 0.42f ) * k, Color.White, skin: false );        // shine
		Sphere( eyePupil, part, new Vector3( EyeRadius * 0.98f, -0.9f, -1.2f ), new Vector3( 0.15f, 0.2f, 0.2f ) * k, new Color( 0.85f, 0.88f, 0.95f ), skin: false );

		upperLid = Lid( part, "Upper Lid", shellColor );
		lowerLid = Lid( part, "Lower Lid", darkColor );
	}

	GameObject Lid( Part part, string name, Color color )
	{
		var go = new GameObject( eyeBall, true, name );
		go.LocalScale = LidRadius;
		var r = go.AddComponent<ModelRenderer>();
		r.Model = ShapeMeshes.Dome;
		r.Tint = color;
		Shapes.Skin( r, "smooth" );
		part.Renderers.Add( (r, color) );
		return go;
	}

	/// <summary> How far to swing a lid's front edge (degrees above the eye's middle) to cover `coverage` of it. </summary>
	static float LidEdge( float coverage ) => MathX.RadianToDegree( MathF.Asin( 1f - 2f * MathX.Clamp( coverage, 0f, 1f ) ) );

	void UpdateEye()
	{
		if ( !Has( EyePart ) || !eyeBall.IsValid() ) return;

		if ( nextBlink )
		{
			blink = 0f;
			nextBlink = Game.Random.Float() < 0.2f ? 0.25f : Game.Random.Float( 2.5f, 6f );
		}
		blink = MathF.Min( 1f, blink + Time.Delta / 0.18f );
		var closing = 1f - MathF.Abs( blink * 2f - 1f );

		var target = PlayerSurvivor.FindNearestAlive( Scene, WorldPosition );
		var angry = Stabbing || timeSinceStab < 0.6f;
		var shocked = parts.Any( p => p is not null && !p.Gone && p.Flash < 0.5f );
		if ( nextGlance )
		{
			nextGlance = Game.Random.Float( 0.6f, 1.8f );
			wander = new Vector3( 1f, Game.Random.Float( -0.6f, 0.6f ), Game.Random.Float( -0.3f, 0.4f ) ).Normal;
		}

		var look = wander;
		if ( target is not null && target.WorldPosition.Distance( WorldPosition ) < 1600f )
			look = eyeBall.WorldRotation.Inverse * ((target.WorldPosition + Vector3.Up * 60f) - eyeBall.WorldPosition).Normal;
		var limit = MathF.Cos( MathX.DegreeToRadian( 48f ) );
		if ( look.x < limit )
		{
			var sideways = look.WithX( 0 );
			sideways = sideways.IsNearlyZero() ? Vector3.Up : sideways.Normal;
			look = (Vector3.Forward * limit + sideways * MathF.Sqrt( 1f - limit * limit )).Normal;
		}
		gazeRot = Rotation.Slerp( gazeRot, Rotation.LookAt( look ), MathX.Clamp( Time.Delta * 14f, 0f, 1f ) );
		eyeGaze.LocalRotation = gazeRot;

		var upper = shocked ? 0.02f : angry ? 0.4f : 0.15f + 0.04f * MathF.Sin( Time.Now * 0.7f );
		var lower = shocked ? 0f : angry ? 0.18f : 0.05f;
		var slant = angry ? 24f : shocked ? -6f : 4f;
		var pupil = shocked ? 0.6f : angry ? 0.8f : 1f;
		var k = MathX.Clamp( Time.Delta * 12f, 0f, 1f );
		lidUpper = MathX.Lerp( lidUpper, upper, k );
		lidLower = MathX.Lerp( lidLower, lower, k );
		lidSlant = MathX.Lerp( lidSlant, slant, k );
		pupilSize = MathX.Lerp( pupilSize, pupil, k );

		var up = MathX.Lerp( lidUpper, 0.56f, closing );
		var down = MathX.Lerp( lidLower, 0.5f, closing );
		upperLid.LocalRotation = Rotation.FromAxis( Vector3.Forward, lidSlant ) * Rotation.FromPitch( -LidEdge( up ) );
		lowerLid.LocalRotation = Rotation.FromPitch( LidEdge( down ) ) * Rotation.FromRoll( 180f );
		eyePupil.LocalScale = new Vector3( 1f, pupilSize, pupilSize );
	}

	protected override void OnDestroy()
	{
		if ( rig.IsValid() ) rig.Destroy();
	}

	// ---- Every frame --------------------------------------------------------

	bool Has( int index ) => parts[index] is { Gone: false };
	int ThighsLeft( float side ) => Enumerable.Range( 0, LegCount ).Count( i => (i < 3 ? 1f : -1f) == side && Has( FirstThigh + i ) );
	/// <summary> Legs lost, counting a missing shin as half a leg. </summary>
	float LegsLost => Enumerable.Range( 0, LegCount ).Sum( i => !Has( FirstThigh + i ) ? 1f : !Has( FirstShin + i ) ? 0.5f : 0f );
	bool Crawling => LegsLost >= 3.5f;
	bool Blind => !Has( EyePart );

	protected override void OnUpdate()
	{
		if ( IsProxy ) CatchUp();
		else if ( IsDead )
		{
			if ( timeSinceDeath > 1f ) GameObject.Destroy();
			return;
		}
		else if ( !DevFlags.FreezeEnemies ) Think();

		if ( diedLocally || !rig.IsValid() ) return;
		rig.WorldPosition = WorldPosition;
		rig.WorldRotation = WorldRotation;
		if ( Has( Body ) && health.IsValid() )
			CreatureGore.BleedTrail( Scene, parts[Body].Go.WorldPosition, gooColor, health.Fraction, ref bleedTimer );
		Animate();
		Tint();
	}

	/// <summary> One foot: planted in the world until it's stretched too far, then it reaches for a new spot and grabs the ground. </summary>
	sealed class Foot
	{
		public Vector3 Pos, From, To;
		public float T = -1f;      // 0..1 while stepping, -1 when planted
		public float Duration = 0.3f;
		public float Lift = 16f;
		public float Plant;        // 1 right after landing, decays: the toes dig in
		public TimeUntil NextProbe = 1f;
	}

	readonly Foot[] feet = new Foot[LegCount];
	float bodyZ = float.NaN;
	Vector2 tilt;
	/// <summary> Steps taken so far (for checking the gait isn't frantic). </summary>
	public int StepCount { get; private set; }
	Vector3 supportShift, hitKick, lastVelocity;
	float hitFlinch, lookYaw, pitchLean, lungeAmount, moveAmount;
	TimeUntil burstUntil;
	bool dashing;
	const float RestReach = 52f;

	/// <summary> Height of the floor at a point, so feet can land on rocks and slopes. </summary>
	float GroundZ( Vector3 at )
	{
		var tr = Scene.Trace.Ray( at + Vector3.Up * 90f, at + Vector3.Down * 160f )
			.WithoutTags( "creature", "zombieblocker", "gib", "player", "viewmodel", "gore" )
			.Run();
		return tr.Hit ? tr.HitPosition.z : WorldPosition.z;
	}

	/// <summary> Up in the spider's own frame: straight up on the floor, the wall's normal while it climbs. </summary>
	Vector3 SurfUp => rig.IsValid() ? rig.WorldRotation.Up : Vector3.Up;

	/// <summary> A vector flattened onto the surface the spider is standing on. </summary>
	Vector3 Flat( Vector3 v )
	{
		var up = SurfUp;
		return v - up * Vector3.Dot( v, up );
	}

	Vector3 Grounded( Vector3 at )
	{
		var up = SurfUp;
		if ( up.z > 0.98f ) return at.WithZ( GroundZ( at ) );
		// On a wall: land on the wall, found by looking into it.
		var tr = Scene.Trace.Ray( at + up * 90f, at - up * 160f )
			.WithoutTags( "creature", "zombieblocker", "gib", "player", "viewmodel", "gore" )
			.Run();
		return tr.Hit ? tr.HitPosition : at - up * Vector3.Dot( at - rig.WorldPosition, up );
	}

	/// <summary> Where leg i would like its foot: out from the body at rest, leaning the way it's travelling. </summary>
	Vector3 DesiredFoot( int i, Vector3 velocity, float leadSeconds = 0.1f )
	{
		var outward = Rotation.FromYaw( LegYaws[i] ).Forward;
		var reach = (Crawling ? RestReach * 0.7f : RestReach) * Size;
		var lead = (Flat( velocity ) * leadSeconds).ClampLength( 95f * Size );
		return Grounded( rig.WorldPosition + rig.WorldRotation * (outward * reach) + lead );
	}

	void Animate()
	{
		var velocity = Flat( NetVelocity );
		var speed = velocity.Length;
		gait += Time.Delta * MathX.Clamp( speed / 60f, 0f, 5f );
		var moving = MathX.Clamp( speed / 60f, 0f, 1f );

		AnimateLegs( velocity, moving );
		Beacon();
		UpdateEye();
		UpdateLure();
	}

	TimeSince timeSinceBeacon;

	/// <summary> When its owner is swimming far above, the lure throws a pulse of light straight up: a "here I am" marker. </summary>
	void Beacon()
	{
		if ( !IsAlly || timeSinceBeacon < 1.6f || !OwnerPlayer.IsValid() ) return;
		var above = OwnerPlayer.WorldPosition.z - WorldPosition.z;
		if ( above < 260f ) return;
		timeSinceBeacon = 0f;
		var tip = LureTip;
		SpiderStrand.Create( tip, tip + Vector3.Up * MathF.Min( above, 600f ), glowColor, 0.9f, 0.45f );
	}

	void AnimateLegs( Vector3 velocity, float moving )
	{
		var scale = Size;
		var stab = Stabbing || timeSinceStab < 0.3f;
		var forward = rig.WorldRotation.Forward;

		// First frame: stand with every foot flat on the floor.
		for ( var i = 0; i < LegCount; i++ )
		{
			if ( feet[i] is not null ) continue;
			feet[i] = new Foot { Pos = DesiredFoot( i, Vector3.Zero ) };
			feet[i].NextProbe = Game.Random.Float( 1f, 3f );
		}

		// Body rides on the feet: it sits at a stand height above the average ground under them and tips with the slope.
		moveAmount = moving;
		var dt = Time.Delta;
		var sink = Crawling ? 20f : LegsLost * 3f;
		var grounded = 0;
		float avg = 0f, front = 0f, back = 0f, left = 0f, right = 0f;
		var support = Vector2.Zero;
		var supportCount = 0;
		for ( var i = 0; i < LegCount; i++ )
		{
			if ( !Has( FirstThigh + i ) ) continue;
			var z = Vector3.Dot( feet[i].Pos - rig.WorldPosition, SurfUp );
			grounded++;
			avg += z;
			if ( i % 3 == 0 ) front += z;
			else if ( i % 3 == 2 ) back += z;
			if ( i < 3 ) left += z; else right += z;
			if ( feet[i].T < 0f && !(stab && (i == 0 || i == 3)) )
			{
				var lf = rig.WorldRotation.Inverse * (feet[i].Pos - rig.WorldPosition);
				support += new Vector2( lf.x, lf.y );
				supportCount++;
			}
		}
		var targetZ = grounded > 0 ? avg / grounded : 0f;   // height above the surface, along its normal

		// Weight shift: the body drifts toward the middle of the feet that are on the ground, so it sways with every step.
		var supportTarget = (supportCount > 0 ? support / supportCount : Vector2.Zero) * 0.4f;
		supportTarget = supportTarget.ClampLength( 7f * scale );
		supportShift = Vector3.Lerp( supportShift, new Vector3( supportTarget.x, supportTarget.y, 0f ), MathX.Clamp( dt * 10f, 0f, 1f ) );

		// Acceleration: lean into a dash, rock back when it brakes.
		var accel = (velocity - lastVelocity) / MathF.Max( dt, 0.001f );
		lastVelocity = velocity;
		var accelLocal = rig.WorldRotation.Inverse * accel;
		var pitchTarget = moving * 5f + MathX.Clamp( accelLocal.x * 0.01f, -9f, 9f );

		// Look: the body turns toward you a little ahead of its legs.
		var player = PlayerSurvivor.FindNearestAlive( Scene, WorldPosition );
		var yawTarget = 0f;
		if ( player is not null && !Blind )
		{
			var lt = rig.WorldRotation.Inverse * (player.WorldPosition - rig.WorldPosition);
			yawTarget = MathX.Clamp( MathF.Atan2( lt.y, lt.x ) * 57.3f, -32f, 32f ) * (1f - moving * 0.7f);
		}
		lookYaw = MathX.Lerp( lookYaw, yawTarget, MathX.Clamp( dt * 5f, 0f, 1f ) );

		// Attack: rear up, then lunge down onto each stab.
		float rear = 0f, lungeTarget = 0f;
		if ( stab )
		{
			var down = MathF.Sin( Time.Now * 18f ) > 0f;
			rear = down ? 4f : -10f;
			lungeTarget = down ? 6f : -1f;
		}
		pitchLean = MathX.Lerp( pitchLean, pitchTarget + rear, MathX.Clamp( dt * 12f, 0f, 1f ) );
		lungeAmount = MathX.Lerp( lungeAmount, lungeTarget, MathX.Clamp( dt * 14f, 0f, 1f ) );

		var breathe = MathF.Sin( Time.Now * 1.6f ) * 0.9f * (1f - moving);
		var crouch = moving * 4f + (stab ? -4f : 0f);   // low and fast when running, tall when it rears up
		var wantedBodyZ = targetZ + (BodyHeight - sink - crouch) * scale + MathF.Sin( gait * 2f ) * 1.2f * moving + breathe - hitFlinch * 4f * scale;
		bodyZ = float.IsNaN( bodyZ ) ? wantedBodyZ : MathX.Lerp( bodyZ, wantedBodyZ, MathX.Clamp( dt * 9f, 0f, 1f ) );   // relative to the spider's feet plane
		var span = MathF.Max( 1, grounded / 3 );
		var pitch = MathX.RadianToDegree( MathF.Atan2( back - front, 100f * scale * span ) );
		var roll = MathX.RadianToDegree( MathF.Atan2( left - right, 90f * scale * span ) );
		tilt = Vector2.Lerp( tilt, new Vector2( MathX.Clamp( pitch, -16f, 16f ), MathX.Clamp( roll, -16f, 16f ) ), MathX.Clamp( dt * 6f, 0f, 1f ) );

		hitKick = Vector3.Lerp( hitKick, Vector3.Zero, MathX.Clamp( dt * 7f, 0f, 1f ) );
		hitFlinch = MathX.Lerp( hitFlinch, 0f, MathX.Clamp( dt * 6f, 0f, 1f ) );
		var body = parts[Body].Go;
		var offset = rig.WorldRotation * (supportShift + new Vector3( lungeAmount, 0f, 0f ) * scale) + hitKick;
		body.WorldPosition = rig.WorldPosition + SurfUp * bodyZ + offset;
		body.LocalRotation = new Angles( tilt.x + pitchLean + (Crawling ? 8f : 0f), lookYaw, -tilt.y ).ToRotation();

		// Which feet want to move, most stretched first. Opposite tripods never step together, and at most three at a time.
		var stepping = feet.Count( f => f.T >= 0f );
		var order = Enumerable.Range( 0, LegCount )
			.Where( i => Has( FirstThigh + i ) && feet[i].T < 0f )
			.OrderByDescending( i => Flat( feet[i].Pos - DesiredFoot( i, velocity ) ).Length )
			.ToList();
		foreach ( var i in order )
		{
			if ( stab && (i == 0 || i == 3) ) continue;   // the front pair are busy stabbing
			var foot = feet[i];
			var desired = DesiredFoot( i, velocity );
			var stretch = Flat( foot.Pos - desired ).Length;
			var threshold = MathX.Lerp( 12f, 20f, moving ) * scale * (1f + MathX.Clamp( velocity.Length / 350f, 0f, 1f ) * 1.8f);
			var probing = moving < 0.2f && foot.NextProbe;
			if ( stretch < threshold && !probing ) continue;

			// A leg that's about to run out of reach must go now, whatever the others are doing.
			var hipDistance = (foot.Pos - parts[FirstThigh + i].Go.WorldPosition).Length;
			var urgent = hipDistance > (ThighLength + ShinLength) * 0.985f * scale;
			if ( urgent && stepping >= 4 ) continue;
			if ( !urgent )
			{
				if ( stepping >= 3 ) break;
				var group = i % 2;
				var blocked = false;
				for ( var k = 0; k < LegCount; k++ )
					if ( feet[k].T >= 0f && k % 2 != group ) blocked = true;
				if ( blocked ) continue;
			}

			// Reach: pick the spot, how high to lift (over rocks, up slopes) and how fast.
			foot.From = foot.Pos;
			foot.Duration = MathX.Lerp( 0.4f, 0.25f, MathX.Clamp( velocity.Length / 350f, 0f, 1f ) ) * Game.Random.Float( 0.9f, 1.15f );
			StepCount++;
			// Land where the body will be by the time the foot arrives.
			foot.To = probing && stretch < threshold ? Grounded( desired + Flat( Vector3.Random ) * 14f * scale ) : DesiredFoot( i, velocity, foot.Duration + 0.12f );
			var mid = (foot.From + foot.To) * 0.5f;
			var rise = SurfUp.z > 0.98f ? MathF.Max( 0f, MathF.Max( foot.To.z, GroundZ( mid ) ) - MathF.Min( foot.From.z, foot.To.z ) ) : 0f;
			foot.Lift = (13f + Game.Random.Float( 0f, 9f ) + rise + moving * 9f) * scale;
			foot.T = 0f;
			foot.NextProbe = Game.Random.Float( 3f, 7f );
			stepping++;
		}

		for ( var i = 0; i < LegCount; i++ )
		{
			var thigh = parts[FirstThigh + i];
			var shin = parts[FirstShin + i];
			if ( thigh is null || thigh.Gone ) continue;
			var foot = feet[i];

			Vector3 footPos;
			if ( stab && (i == 0 || i == 3) )
			{
				// Front legs stab: up, then down hard at the spot in front.
				var side = rig.WorldRotation.Right * (i == 0 ? -1f : 1f) * 16f * scale;
				var down = MathF.Sin( Time.Now * 18f + i ) > 0f;
				footPos = rig.WorldPosition + forward * 66f * scale + side + SurfUp * (down ? 6f : 58f) * scale;
				foot.Pos = footPos;
				foot.T = -1f;
			}
			else if ( foot.T >= 0f )
			{
				foot.T += Time.Delta / foot.Duration;
				var t = MathX.Clamp( foot.T, 0f, 1f );
				var along = t * t * (3f - 2f * t);
				footPos = Vector3.Lerp( foot.From, foot.To, along );
				// The arc peaks early, then the foot plunges at the spot: it grabs for the ground.
				footPos += SurfUp * foot.Lift * MathF.Sin( MathF.PI * MathF.Pow( t, 0.65f ) );
				if ( foot.T >= 1f )
				{
					foot.Pos = foot.To;
					foot.T = -1f;
					foot.Plant = 1f;
					footPos = foot.Pos;
				}
			}
			else footPos = foot.Pos;

			// The toes dig in for a moment after landing.
			foot.Plant = MathF.Max( 0f, foot.Plant - Time.Delta * 7f );
			footPos -= SurfUp * foot.Plant * 2.5f * scale;

			SolveLeg( i, thigh, shin, footPos );
		}
	}

	/// <summary> Two-bone solve: aim the thigh at a high knee and the shin at the foot, so the leg reaches exactly where the foot is. </summary>
	void SolveLeg( int i, Part thigh, Part shin, Vector3 foot )
	{
		var hip = thigh.Go.WorldPosition;
		var a = ThighLength * Size;
		var b = ShinLength * Size;

		var toFoot = foot - hip;
		var distance = MathX.Clamp( toFoot.Length, MathF.Abs( a - b ) + 0.5f, a + b - 0.5f );
		var direction = toFoot.IsNearlyZero() ? Vector3.Down : toFoot.Normal;
		foot = hip + direction * distance;

		// Knee sits off the hip-to-foot line toward up and outward, like a spider's.
		var outward = rig.WorldRotation * Rotation.FromYaw( LegYaws[i] ).Forward;
		var pole = outward * (0.5f + 0.5f * moveAmount) + SurfUp;
		pole -= direction * Vector3.Dot( pole, direction );
		pole = pole.IsNearlyZero() ? SurfUp : pole.Normal;

		var along = (distance * distance + a * a - b * b) / (2f * distance);
		var height = MathF.Sqrt( MathF.Max( 0f, a * a - along * along ) );
		var knee = hip + direction * along + pole * height;

		thigh.Go.WorldRotation = Rotation.LookAt( (knee - hip).Normal, pole );
		if ( shin is not null && !shin.Gone )
			shin.Go.WorldRotation = Rotation.LookAt( (foot - knee).Normal, pole );
	}

	void Tint()
	{
		if ( IsAlly )
			foreach ( var jet in Scene.GetAllComponents<Jetpack>() )
				if ( jet.DockedTo == this && jet.Power < jet.MaxPower - 0.5f ) timeSinceCharging = 0f;
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
		if ( IsAlly ) { ThinkAlly(); return; }
		var target = PlayerSurvivor.FindNearestAlive( Scene, WorldPosition );
		var speedScale = (1f - 0.1f * LegsLost) * (Crawling ? 0.4f : 1f) * (Blind ? 0.65f : 1f);
		// Closing in from a distance it scuttles in bursts: a hard dash, a creeping pause, a dash.
		if ( target is not null && !Crawling && (target.WorldPosition - WorldPosition).WithZ( 0 ).Length > 260f * Size )
			speedScale *= BurstScale();
		else dashing = false;
		if ( agent.IsValid() ) agent.MaxSpeed = MoveSpeed * MathF.Max( 0.15f, speedScale ) * Size;

		Stabbing = false;
		if ( target is not null )
		{
			var toTarget = (target.WorldPosition - WorldPosition).WithZ( 0 );
			var distance = toTarget.Length;

			if ( distance <= AttackRange * Size && !Blind )
			{
				agent?.Stop();
				Face( toTarget, 10f );
				Stabbing = true;
				if ( timeSinceAttack >= AttackCooldown )
				{
					timeSinceAttack = 0f;
					StabEverywhere();
					Stab( target );
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
					if ( distance <= AttackRange * Size && timeSinceAttack > AttackCooldown * 2f ) { timeSinceAttack = 0f; StabEverywhere(); Stab( target ); }
				}
				else agent?.MoveTo( target.WorldPosition );
			}

			var velocity = agent.IsValid() ? agent.Velocity.WithZ( 0 ) : Vector3.Zero;
			if ( !Blind ) Face( toTarget, 8f );
			else if ( velocity.Length > 10f ) Face( velocity, 6f );
		}
		else agent?.Stop();

		NetVelocity = agent.IsValid() ? agent.Velocity : Vector3.Zero;
	}

	// ---- Ally brain -------------------------------------------------------------

	Component currentEnemy;
	bool catchingUp;
	TimeUntil nextScan;
	TimeUntil nextMove;

	/// <summary> Spawns a spider on this player's side. </summary>
	public static SeaSpider SpawnAlly( Scene scene, PlayerSurvivor owner )
	{
		var go = new GameObject( true, "Ally Spider" );
		go.WorldPosition = owner.WorldPosition + owner.WorldRotation.Right * 110f;
		go.WorldRotation = owner.WorldRotation;
		var agent = go.AddComponent<NavMeshAgent>();
		agent.Height = 60f;
		agent.Radius = 18f;
		agent.Acceleration = 900f;
		go.AddComponent<Health>().ResetTo( 100000f );
		var spider = go.AddComponent<SeaSpider>();
		spider.IsAlly = true;
		spider.OwnerPlayer = owner.GameObject;
		spider.Hue = 0.5f;
		spider.Size = 1.15f;   // a bit bigger: longer legs mean longer strides, so a calmer gait
		spider.MoveSpeed = 175f;
		if ( Networking.IsActive ) go.NetworkSpawn();
		return spider;
	}

	/// <summary> Orders from the owner: follow, hold a spot, or attack a target. Runs on the host. </summary>
	public void Command( int mode, Vector3 point, GameObject target )
	{
		if ( IsProxy ) CommandOnHost( mode, point, target );
		else ApplyCommand( mode, point, target );
	}

	[Rpc.Owner]
	void CommandOnHost( int mode, Vector3 point, GameObject target ) => ApplyCommand( mode, point, target );

	void ApplyCommand( int mode, Vector3 point, GameObject target )
	{
		AllyMode = mode;
		HoldPoint = point;
		AttackTarget = target;
		currentEnemy = null;
		nextScan = 0f;
	}

	static Component ResolveEnemy( GameObject go )
	{
		if ( !go.IsValid() ) return null;
		Component c = go.Components.Get<JunkCrab>();
		c ??= go.Components.Get<Bloober>();
		c ??= go.Components.Get<Squilien>();
		c ??= go.Components.Get<MC61>();
		c ??= go.Components.Get<Zombie>();
		c ??= go.Components.Get<SeaSpider>();
		return c;
	}

	static bool EnemyDead( Component c ) => c switch
	{
		JunkCrab x => x.IsDead,
		Bloober x => x.IsDead,
		Squilien x => x.IsDead,
		MC61 x => x.IsDead,
		Zombie x => x.IsDead,
		SeaSpider x => x.IsDead || x.IsAlly,
		_ => true,
	};

	/// <summary> The nearest living enemy to a point (optionally also near the owner). </summary>
	Component FindEnemy( Vector3 around, float range, Vector3? alsoNear = null, float nearRange = 0f, bool flat = true )
	{
		Component best = null;
		var bestDistance = range;
		void Consider( Component c )
		{
			if ( !c.IsValid() || EnemyDead( c ) ) return;
			var d = flat ? (c.WorldPosition - around).WithZ( 0 ).Length : (c.WorldPosition - around).Length;
			if ( d >= bestDistance ) return;
			if ( alsoNear is { } n && (c.WorldPosition - n).WithZ( 0 ).Length > nearRange ) return;
			best = c;
			bestDistance = d;
		}
		foreach ( var c in Scene.GetAllComponents<JunkCrab>() ) Consider( c );
		foreach ( var c in Scene.GetAllComponents<Bloober>() ) Consider( c );
		foreach ( var c in Scene.GetAllComponents<Squilien>() ) Consider( c );
		foreach ( var c in Scene.GetAllComponents<MC61>() ) Consider( c );
		foreach ( var c in Scene.GetAllComponents<Zombie>() ) Consider( c );
		foreach ( var c in Scene.GetAllComponents<SeaSpider>() ) Consider( c );
		return best;
	}

	// ---- Harpoon strands ---------------------------------------------------------

	[Property, Group( "Ally" )] public float HarpoonRange { get; set; } = 650f;
	[Property, Group( "Ally" )] public float HarpoonCooldown { get; set; } = 1.4f;
	[Property, Group( "Ally" )] public float HarpoonDamage { get; set; } = 0.85f;
	TimeSince timeSinceHarpoon = 999f;
	TimeUntil nextHarpoonScan;

	/// <summary> A sticky strand shot from the lure at something up out of reach. </summary>
	void Harpoon( Component enemy, PlayerSurvivor owner )
	{
		if ( timeSinceHarpoon < HarpoonCooldown || !enemy.IsValid() ) return;
		var from = lureBulb.IsValid() ? lureBulb.WorldPosition : WorldPosition + Vector3.Up * 60f * Size;
		var to = enemy.WorldPosition + Vector3.Up * 20f;
		if ( (to - from).Length > HarpoonRange ) return;
		var tr = Scene.Trace.Ray( from, to ).WithoutTags( "creature", "gib", "player", "viewmodel", "gore", "zombieblocker" ).Run();
		if ( tr.Hit && tr.Distance < (to - from).Length - 40f ) return;   // something solid in the way
		timeSinceHarpoon = 0f;
		StrandEverywhere( from, to );
		Strike( enemy, owner, HarpoonDamage );
	}

	[Rpc.Broadcast( NetFlags.Unreliable )]
	void StrandEverywhere( Vector3 from, Vector3 to ) => SpiderStrand.Create( from, to, glowColor );

	// ---- Climbing: up walls toward a swimming owner ------------------------------

	enum ClimbPhase { None, Approach, Scale, Perch, Descend }
	ClimbPhase climbPhase;
	Vector3 climbWall, climbNormal = Vector3.Backward;
	TimeSince climbTimer;
	TimeUntil nextClimbCheck;
	Vector3 climbVelocity, climbGround;
	const float ClimbSpeed = 120f;

	/// <summary> True while it's up on a wall (or heading for one). </summary>
	public bool IsClimbing => climbPhase != ClimbPhase.None;
	public string ClimbInfo => $"{climbPhase} n={climbNormal} agent={(agent.IsValid() ? agent.Enabled : false)} g={climbGround} rotUp={rig.IsValid() && true}";

	static readonly string[] NotWall = { "creature", "zombieblocker", "gib", "player", "viewmodel", "gore" };

	/// <summary> The best steep surface near it to climb toward the owner: one that rises at least a little above it. </summary>
	bool FindWall( Vector3 ownerPos, out Vector3 point, out Vector3 normal )
	{
		point = default;
		normal = Vector3.Backward;
		var toOwner = (ownerPos - WorldPosition).WithZ( 0 );
		toOwner = toOwner.IsNearlyZero() ? Vector3.Forward : toOwner.Normal;
		var best = float.MaxValue;
		var origin = WorldPosition + Vector3.Up * 45f;
		for ( var i = 0; i < 16; i++ )
		{
			var d = Rotation.FromYaw( i * 22.5f ).Forward;
			var tr = Scene.Trace.Ray( origin, origin + d * 380f ).WithoutTags( NotWall ).Run();
			if ( !tr.Hit || MathF.Abs( tr.Normal.z ) > 0.5f ) continue;
			// It has to keep going up for a good way.
			var above = Scene.Trace.Ray( tr.HitPosition + tr.Normal * 12f + Vector3.Up * 120f, tr.HitPosition - tr.Normal * 20f + Vector3.Up * 120f ).WithoutTags( NotWall ).Run();
			if ( !above.Hit ) continue;
			var score = tr.Distance / 380f - 1.5f * Vector3.Dot( d, toOwner );
			if ( score >= best ) continue;
			best = score;
			point = tr.HitPosition;
			normal = tr.Normal;
		}
		return best < float.MaxValue;
	}

	void EndClimb()
	{
		climbPhase = ClimbPhase.None;
		nextClimbCheck = 1.5f;
		if ( agent.IsValid() ) agent.Enabled = true;
		for ( var i = 0; i < LegCount; i++ ) feet[i] = null;      // stand the feet flat on the floor again
		bodyZ = float.NaN;
	}

	/// <summary> Runs the climb: returns true while it's in charge of moving the spider. </summary>
	bool ClimbThink( PlayerSurvivor owner, Vector3 ownerPos )
	{
		climbVelocity = Vector3.Zero;
		var dt = Time.Delta;
		var above = ownerPos.z - WorldPosition.z;
		var flatDistance = (ownerPos - WorldPosition).WithZ( 0 ).Length;

		switch ( climbPhase )
		{
			case ClimbPhase.None:
			{
				if ( !nextClimbCheck ) return false;
				nextClimbCheck = 0.4f;
				if ( above < 110f || flatDistance > 700f || AllyMode == ModeHold || currentEnemy.IsValid() ) return false;
				if ( !FindWall( ownerPos, out climbWall, out climbNormal ) ) return false;
				climbPhase = ClimbPhase.Approach;
				climbTimer = 0f;
				return true;
			}

			case ClimbPhase.Approach:
			{
				var stand = (climbWall + climbNormal * 55f).WithZ( WorldPosition.z );
				var distance = (stand - WorldPosition).WithZ( 0 ).Length;
				if ( above < 60f || climbTimer > 6f ) { EndClimb(); return false; }
				if ( distance < 70f )
				{
					// Reached the foot of it: leave the navmesh and go up.
					climbGround = WorldPosition;
					agent?.Stop();
					if ( agent.IsValid() ) agent.Enabled = false;
					for ( var i = 0; i < LegCount; i++ ) feet[i] = null;
					climbPhase = ClimbPhase.Scale;
					climbTimer = 0f;
					return true;
				}
				if ( nextMove ) { nextMove = 0.25f; agent?.MoveTo( stand ); }
				var v = agent.IsValid() ? agent.Velocity.WithZ( 0 ) : Vector3.Zero;
				Face( v.Length > 10f ? v : -climbNormal, 8f );
				agent.MaxSpeed = MoveSpeed * 1.15f * Size;
				climbVelocity = agent.IsValid() ? agent.Velocity : Vector3.Zero;
				return true;
			}

			case ClimbPhase.Scale:
			case ClimbPhase.Perch:
			case ClimbPhase.Descend:
			{
				var n = climbNormal;
				var upDir = (Vector3.Up - n * Vector3.Dot( Vector3.Up, n ));
				upDir = upDir.IsNearlyZero() ? Vector3.Up : upDir.Normal;

				var direction = 0f;
				if ( climbPhase == ClimbPhase.Scale ) direction = 1f;
				else if ( climbPhase == ClimbPhase.Descend ) direction = -1f;

				// Phase changes.
				if ( climbPhase == ClimbPhase.Scale && ownerPos.z - WorldPosition.z < 50f ) climbPhase = ClimbPhase.Perch;
				if ( climbPhase == ClimbPhase.Perch && ownerPos.z - WorldPosition.z > 120f && climbTimer > 0.5f ) { climbPhase = ClimbPhase.Scale; climbTimer = 0f; }
				if ( climbPhase != ClimbPhase.Descend && (ownerPos.z < WorldPosition.z - 70f || flatDistance > 900f || owner is null) ) climbPhase = ClimbPhase.Descend;

				var step = direction * (climbPhase == ClimbPhase.Descend ? 150f : ClimbSpeed) * dt;
				var next = WorldPosition + upDir * step;

				// Stay glued to the wall: look into it from just off it.
				var probe = upDir * 45f;      // look from above the floor, so a trace along the floor doesn't start inside it
				var tr = Scene.Trace.Ray( next + probe + n * 90f, next + probe - n * 220f ).WithoutTags( NotWall ).Run();
				if ( !tr.Hit || tr.StartedSolid || MathF.Abs( tr.Normal.z ) > 0.6f )
				{
					// Ran out of wall: at the top, stay put; at the bottom, step off.
					if ( climbPhase == ClimbPhase.Scale ) { climbPhase = ClimbPhase.Perch; climbTimer = 0f; }
					else if ( climbPhase == ClimbPhase.Descend ) { WorldPosition = climbGround; WorldRotation = Rotation.FromYaw( WorldRotation.Yaw() ); EndClimb(); agent?.SetAgentPosition( climbGround ); return false; }
				}
				else
				{
					climbNormal = Vector3.Lerp( climbNormal, tr.Normal, MathX.Clamp( dt * 6f, 0f, 1f ) ).Normal;
					WorldPosition = Vector3.Lerp( next, tr.HitPosition - probe + climbNormal * 1.5f, MathX.Clamp( dt * 9f, 0f, 1f ) );   // up the wall, easing in against it
					climbVelocity = upDir * direction * (climbPhase == ClimbPhase.Descend ? 150f : ClimbSpeed);
				}

				// Back on the floor after a descent.
				if ( climbPhase == ClimbPhase.Descend )
				{
					var floor = Scene.Trace.Ray( WorldPosition + Vector3.Up * 10f, WorldPosition + Vector3.Down * 28f ).WithoutTags( NotWall ).Run();
					if ( floor.Hit && floor.Normal.z > 0.7f )
					{
						WorldPosition = floor.HitPosition;
						WorldRotation = Rotation.FromYaw( WorldRotation.Yaw() );
						EndClimb();
						agent?.SetAgentPosition( WorldPosition );
						return false;
					}
				}

				// Turn onto the wall (head up the wall, feet into it), easing in.
				var wanted = Rotation.LookAt( upDir, climbNormal );
				WorldRotation = Rotation.Slerp( WorldRotation, wanted, MathX.Clamp( dt * 6f, 0f, 1f ) );

				// While it hangs there it still spits at whatever it can see.
				if ( climbPhase == ClimbPhase.Perch && nextHarpoonScan )
				{
					nextHarpoonScan = 0.4f;
					if ( FindEnemy( WorldPosition, HarpoonRange, flat: false ) is { } target ) Harpoon( target, owner );
				}
				return true;
			}
		}
		return false;
	}

	void ThinkAlly()
	{
		Stabbing = false;
		var owner = OwnerPlayer.IsValid() ? OwnerPlayer.Components.Get<PlayerSurvivor>() : null;
		if ( owner is null || !owner.IsAlive ) owner = PlayerSurvivor.FindNearestAlive( Scene, WorldPosition );
		var ownerPos = owner?.WorldPosition ?? WorldPosition;
		var velocity = agent.IsValid() ? agent.Velocity.WithZ( 0 ) : Vector3.Zero;

		// Following up a wall when you're swimming above it (takes over movement while it's climbing).
		if ( ClimbThink( owner, ownerPos ) )
		{
			NetVelocity = climbVelocity;
			return;
		}

		// Pick something to fight: what it was told to attack, else the nearest enemy to it (or to its post).
		if ( AllyMode == ModeAttack )
		{
			currentEnemy = ResolveEnemy( AttackTarget );
			if ( currentEnemy is null || EnemyDead( currentEnemy ) ) { AllyMode = ModeFollow; currentEnemy = null; }
		}
		if ( AllyMode != ModeAttack && nextScan )
		{
			nextScan = 0.25f;
			currentEnemy = AllyMode == ModeHold
				? FindEnemy( HoldPoint, 380f )
				: FindEnemy( WorldPosition, 460f, ownerPos, 800f );
		}
		if ( currentEnemy.IsValid() && EnemyDead( currentEnemy ) ) currentEnemy = null;

		// Something above its reach: spit a strand up at it.
		if ( currentEnemy.IsValid() && currentEnemy.WorldPosition.z - WorldPosition.z > 70f )
			Harpoon( currentEnemy, owner );
		else if ( !currentEnemy.IsValid() && nextHarpoonScan )
		{
			nextHarpoonScan = 0.4f;
			if ( FindEnemy( WorldPosition + Vector3.Up * 200f, HarpoonRange, flat: false ) is { } high && high.WorldPosition.z - WorldPosition.z > 70f )
				Harpoon( high, owner );
		}

		var speedScale = 1f;
		if ( currentEnemy.IsValid() )
		{
			var toEnemy = (currentEnemy.WorldPosition - WorldPosition).WithZ( 0 );
			if ( toEnemy.Length <= AttackRange * Size * 0.9f )
			{
				agent?.Stop();
				Face( toEnemy, 10f );
				Stabbing = true;
				if ( timeSinceAttack >= AttackCooldown )
				{
					timeSinceAttack = 0f;
					StabEverywhere();
					Strike( currentEnemy, owner );
				}
			}
			else
			{
				speedScale = 1.1f;
				if ( nextMove ) { nextMove = 0.25f; agent?.MoveTo( currentEnemy.WorldPosition ); }
				if ( velocity.Length > 10f ) Face( velocity, 8f ); else Face( toEnemy, 8f );
			}
		}
		else
		{
			Vector3 goal;
			float stopRange;
			if ( AllyMode == ModeHold )
			{
				goal = HoldPoint;
				stopRange = 60f;
			}
			else
			{
				// Loose leash: it ignores you until you're well away, then comes to a ring around you on the side it's already on.
				var fromOwner = (WorldPosition - ownerPos).WithZ( 0 );
				var ownerDistance = fromOwner.Length;
				if ( ownerDistance > 380f ) catchingUp = true;
				else if ( ownerDistance < 260f ) catchingUp = false;
				var side = fromOwner.IsNearlyZero() ? Vector3.Right : fromOwner.Normal;
				goal = ownerPos + side * 210f;
				stopRange = catchingUp ? 40f : 100000f;
			}

			var distance = (goal - WorldPosition).WithZ( 0 ).Length;
			if ( AllyMode != ModeHold && distance > 1100f && agent.IsValid() )
				agent.SetAgentPosition( ownerPos + Vector3.Random.WithZ( 0 ).Normal * 150f );   // lost: catch up
			if ( distance > stopRange )
			{
				speedScale = distance > 450f ? 1.15f : 1f;
				if ( nextMove ) { nextMove = 0.25f; agent?.MoveTo( goal ); }
				if ( velocity.Length > 10f ) Face( velocity, 7f );
			}
			else
			{
				agent?.Stop();
				var look = AllyMode == ModeHold ? (HoldPoint - ownerPos) : (ownerPos - WorldPosition);
				Face( look.WithZ( 0 ), 4f );
			}
		}

		if ( agent.IsValid() ) agent.MaxSpeed = MoveSpeed * speedScale * Size;
		NetVelocity = agent.IsValid() ? agent.Velocity : Vector3.Zero;
	}

	/// <summary> A stab on an enemy: hits one of its parts, so it can take legs and claws off. Kills score for the owner. </summary>
	void Strike( Component enemy, PlayerSurvivor owner, float scale = 1f )
	{
		var damage = StabDamage * 2.5f * scale;
		var direction = (enemy.WorldPosition - WorldPosition).WithZ( 0 ).Normal;
		if ( enemy is Zombie zombie )
		{
			zombie.Components.Get<Health>()?.OnDamage( new DamageInfo { Damage = damage, Attacker = owner?.GameObject ?? GameObject, Position = zombie.WorldPosition + Vector3.Up * 40f, Origin = WorldPosition } );
			return;
		}

		var hitParts = Scene.GetAllComponents<CreaturePart>().Where( p => p.Creature == enemy && p.GameObject.IsValid() ).ToList();
		if ( hitParts.Count == 0 ) return;
		var part = hitParts[Game.Random.Int( 0, hitParts.Count - 1 )];
		part.ReceiveShot( damage, part.GameObject, direction, part.GameObject.WorldPosition, WorldPosition + Vector3.Up * 40f, owner, true );
	}

	float BurstScale()
	{
		if ( burstUntil )
		{
			dashing = !dashing;
			burstUntil = dashing ? Game.Random.Float( 0.7f, 1.2f ) : Game.Random.Float( 0.35f, 0.7f );
		}
		return dashing ? 1.25f : 0.3f;
	}

	void Face( Vector3 direction, float rate )
	{
		if ( direction.IsNearlyZero() ) return;
		var wanted = Rotation.LookAt( direction.WithZ( 0 ).Normal, Vector3.Up );
		WorldRotation = Rotation.Slerp( WorldRotation, wanted, MathX.Clamp( Time.Delta * rate, 0f, 1f ) );
	}

	void Stab( PlayerSurvivor target )
	{
		var frontLegs = (Has( FirstThigh ) ? 1 : 0) + (Has( FirstThigh + 3 ) ? 1 : 0);
		var damage = StabDamage * (frontLegs switch { 2 => 1f, 1 => 0.7f, _ => 0.4f });
		target.Health?.OnDamage( new DamageInfo
		{
			Damage = damage,
			Attacker = GameObject,
			Position = target.WorldPosition + Vector3.Up * 20f,
			Origin = WorldPosition,
		} );
	}

	[Rpc.Broadcast( NetFlags.Unreliable )]
	void StabEverywhere() => timeSinceStab = 0f;

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
		if ( IsDead || IsAlly ) return;
		if ( index < 0 || index >= parts.Length || !Has( index ) ) index = Body;
		lastShotDirection = direction;
		lastShotPointBlank = (position - origin).Length < CreatureGore.OverkillRange;
		var part = parts[index];
		var crit = index == EyePart;

		var multiplier = index switch
		{
			Body => 1f,
			EyePart => EyeMultiplier,
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

		// A hit staggers it a little.
		if ( !killed && agent.IsValid() )
		{
			var push = direction.WithZ( 0 ).Normal * 6f;
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
		hitKick = (hitKick + direction.WithZ( 0 ).Normal * 5f).ClampLength( 10f );
		hitFlinch = 1f;
		lureKick += direction.Normal * 900f;

		gore?.GooBurst( position, -direction, gooColor, 6, 220f, 0.8f );
		gore?.GooSpray( position, direction, 2, gooColor, 180f, 0.4f, 0.9f );
		if ( !part.Gone )
		{
			CreatureGore.AddWound( part.Go, position, direction, shellColor, gooColor, (index == Body ? 3.5f : 2f) * Size );
			CreatureGore.Jiggle( part.Go, index == Body ? 0.7f : 1f );
		}
		if ( chip && index == Body ) gore?.ThrowBlob( position, -direction, shellColor, gooColor, 3.5f * Size );
	}

	[Rpc.Broadcast]
	void CutEverywhere( int index, Vector3 position, Vector3 direction ) => CutLocal( index, direction, quiet: false );

	void CutLocal( int index, Vector3 direction, bool quiet )
	{
		var part = parts[index];
		if ( part is null || part.Gone ) return;
		MarkGone( index );

		if ( quiet ) { part.Go.Destroy(); return; }

		var at = part.Go.WorldPosition;
		if ( index != EyePart )
		{
			// Raw stump where it came off, and a cap on the piece that flew.
			var parent = part.Parent >= 0 ? parts[part.Parent] : null;
			var outward = parent is not null ? (at - parent.Go.WorldPosition).Normal : Vector3.Up;
			var radius = (index < FirstShin ? 3.2f : 2.4f) * Size;
			if ( parent is not null && !parent.Gone ) CreatureGore.AddStump( parent.Go, at, outward, radius, shellColor, gooColor, 4f );
			CreatureGore.AddStump( part.Go, at, -outward, radius, shellColor, gooColor, 1.5f );
		}

		if ( index == EyePart )
		{
			// The eye pops: a splash of white and goo.
			gore?.GooBurst( at, direction, Color.White, 10, 220f, 0.9f );
			gore?.GooBurst( at, direction, gooColor, 8, 240f, 1f );
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

	/// <summary> The egg bursts in a spray of goo; the legs and eye scatter. </summary>
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
		for ( var b = 0; b < 2; b++ ) gore?.ThrowBlob( at, direction, shellColor, gooColor, 4.5f * Size );

		if ( Has( EyePart ) )
		{
			MarkGone( EyePart );
			gore?.GooBurst( parts[EyePart].Go.WorldPosition, direction, Color.White, 8, 200f, 0.8f );
			parts[EyePart].Go.Destroy();
		}

		for ( var i = 0; i < LegCount; i++ )
		{
			var thigh = parts[FirstThigh + i];
			if ( thigh is not null && !thigh.Gone )
			{
				// The shin goes with the thigh it hangs from, as one long leg.
				MarkGone( FirstThigh + i );
				Detach( thigh, (direction + Vector3.Random + Vector3.Up).Normal * Game.Random.Float( 120f, 260f ), spin: 20f );
			}
			else if ( parts[FirstShin + i] is { Gone: false } shin )
			{
				MarkGone( FirstShin + i );
				Detach( shin, (direction + Vector3.Random + Vector3.Up).Normal * Game.Random.Float( 120f, 260f ), spin: 20f );
			}
		}

		// The body itself: a burst egg left on the floor (or, point-blank, nothing left at all).
		if ( Has( Body ) )
		{
			MarkGone( Body );
			if ( overkill ) parts[Body].Go.Destroy();
			else Detach( parts[Body], direction * 60f );
		}
		rig.Destroy();
	}
}
