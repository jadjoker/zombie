/// <summary>
/// MC61: a floating ghost magician in a tux and top hat, with one big living eye and two detached white-gloved
/// hands that do the talking. The boss.
///
/// Attacks:
///  - Eye beam: shouts "I SEEE YOUUU", charges his eye (it glows red and veins creep in; a thin aiming line shows
///    where), then fires a heavy beam that sweeps slowly after you. Keep moving.
///  - "Pick any card": his hands pinch a card, then snap open and he spins, flinging rings of cards in every
///    direction. Each card carries a curse: burn, poison, slow or ink.
///  - The worm dance: sometimes he just drops to the floor and wriggles (it reminds him of cheering crowds as a
///    kid). He takes extra damage while he dances.
///
/// Chunking: the hat can be shot off; a hand can be shot off (lose both and the card trick is over); the tail
/// breaks at the point you hit and the rest drops; the eye is a critical spot and can be popped, which ends the
/// beam. The head is lethal. Without eye or hands he just comes at you.
///
/// The eye is a live 2D picture on the eyeball: the pupil follows you, he blinks, and it glows red as the beam
/// charges. Every finger is its own jointed piece, curled into poses by the code.
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
	[Property, Group( "Damage" )] public float LimbMultiplier { get; set; } = 0.15f;
	[Property, Group( "Damage" )] public float TorsoHealth { get; set; } = 110f;
	[Property, Group( "Damage" )] public float HatHealth { get; set; } = 40f;
	[Property, Group( "Damage" )] public float HandHealth { get; set; } = 130f;
	[Property, Group( "Damage" )] public float TailHealth { get; set; } = 60f;
	public enum Surface { Plain, RimGlow, Sheen }
	/// <summary> How his ghostly body (and tail) is shaded: plain, a blue rim glow with drifting mist, or a rainbow sheen. </summary>
	[Property, Group( "Body" )] public Surface Look { get; set; } = Surface.RimGlow;
	/// <summary> He scans in from the floor up as a hologram for this long when he appears (0 = off). </summary>
	[Property, Group( "Body" )] public float SpawnInTime { get; set; } = 1.8f;
	/// <summary> Hands are modelled small; this scales the whole hand and its fingers. </summary>
	[Property, Group( "Body" )] public float HandScale { get; set; } = 1.2f;
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
	const int Torso = 0, Head = 1, Eye = 2, Hat = 3, HandL = 4, HandR = 5, Tail0 = 6, TailCount = 4;
	const int PartCount = 10;

	static readonly Color Tux = new( 0.06f, 0.06f, 0.09f );
	static readonly Color GhostBody = new( 0.7f, 0.78f, 0.93f );
	static readonly Color Skin = new( 0.42f, 0.47f, 0.4f ); // sickly
	static readonly Color Glove = new( 0.97f, 0.97f, 1f );
	static readonly Color Iris = new( 1f, 0.12f, 0.08f );
	static readonly Color Sclera = new( 1f, 0.86f, 0.8f ); // bloodshot
	static readonly Color BeamColor = new( 1f, 0.22f, 0.12f );

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

	readonly Part[] parts = new Part[PartCount];
	GameObject rig;
	GameObject beam, beamCore, aimLine;
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
#pragma warning disable SB3003 // Host-only death timer; a reset on host migration is harmless.
	TimeSince timeSinceDeath;
#pragma warning restore SB3003
	Vector3 lastShotDirection = Vector3.Forward;
	TimeSince bleedTimer;
	bool lastShotPointBlank;

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

	// Layout numbers come from Tools/blender/mc61_ghost.py (Assets/models/creatures/mc61g_layout.json).
	static readonly Vector3 HeadPivot = new( 0, 0, 38f );
	static readonly Vector3 EyeFromHead = new( 20.68f, 0, 0 );
	static readonly Vector3 HatFromHead = new( -2.13f, 11.63f, 17.92f );
	static readonly Rotation HatRotation = new( -0.16307f, -0.19920f, -0.61210f, 0.74771f );
	static readonly Vector3 TailRoot = new( -16.6f, 0, -25f );
	const float TailSegment = 10f;
	static readonly float[] TailRadius = { 10.4f, 8.6f, 6.2f, 3.6f };
	static readonly float[] FingerY = { -4.05f, -1.35f, 1.35f, 4.05f };
	const float FingerZ = 9.6f;
	static readonly float[] FingerLength = { 3.7f, 3.1f, 2.7f };
	// Fingers are chains of short links, and each bend is shared across 2-3 of them, so curls are smooth arcs (16 per finger, a bend spread over 4).
	const int FingerLinks = 16, ThumbLinks = 10;
	const float FingerLinkLength = 0.594f, FingerTaper = 0.9753f, ThumbLinkLength = 0.66f, ThumbTaper = 0.9744f;
	// Which anatomical joint (0 knuckle, 1 middle, 2 tip) each link bends for, and how much of it.
	static readonly int[] FingerLinkJoint = { 0, 0, -1, -1, 1, 1, 1, 1, -1, -1, 2, 2, 2, 2, -1, -1 };
	static readonly float[] FingerLinkShare = { 0.6f, 0.4f, 0f, 0f, 0.25f, 0.25f, 0.25f, 0.25f, 0f, 0f, 0.25f, 0.25f, 0.25f, 0.25f, 0f, 0f };
	static readonly int[] ThumbLinkJoint = { 0, 0, -1, -1, 1, 1, 1, 1, -1, -1 };
	static readonly float[] ThumbLinkShare = { 0.6f, 0.4f, 0f, 0f, 0.25f, 0.25f, 0.25f, 0.25f, 0f, 0f };
	static readonly float[] FingerScale = { 0.93f, 1.0f, 0.95f, 0.8f };   // index, middle, ring, pinky: real hands have uneven fingers
	static readonly float[] ThumbLength = { 3.6f, 3.0f };

	/// <summary>
	/// A hand: ONE smooth skinned mesh (palm, fingers, thumb) with a small skeleton. The code bends the bones, so every
	/// finger curls as a continuous surface instead of stacked rigid pieces.
	/// </summary>
	sealed class Hand
	{
		public float Side;
		public GameObject Root;
		public SkinnedModelRenderer Skin;
		public readonly int[] Bone = new int[14];            // f0_0..f3_2 then th_0, th_1
		public readonly Transform[] Rest = new Transform[14]; // rest pose, in the skin's own space
		public bool Ready;
		public readonly float[] Curl = new float[14];   // 4 fingers x 3 joints, then 2 thumb joints (degrees)
		public float Splay = 38f;
	}
	readonly Hand[] hands = new Hand[2];

	// Eye picture
	const int EyeW = 128, EyeH = 96;
	Texture eyeTexture;
	Color32[] eyePixels;
	Material eyeMaterial;

	// Tail spring state per segment (pitch back, roll sideways)
	readonly float[] tailPitch = new float[TailCount], tailPitchVel = new float[TailCount], tailRoll = new float[TailCount], tailRollVel = new float[TailCount];
	Vector3 lastRigPosition;

	// Surface looks and the hologram spawn-in
	readonly List<ModelRenderer> styleRenderers = new();
	readonly List<(ModelRenderer Renderer, Material Original)> spawnSaved = new();
	Material hologramMaterial;
	TimeSince sinceSpawned = 0f;
	bool spawnDone;

	/// <summary> Puts the chosen surface shader on the ghost body and tail (null material = the normal one). </summary>
	public void ApplyLook()
	{
		var path = Look switch { Surface.RimGlow => "materials/fx/fx_ghost.vmat", Surface.Sheen => "materials/fx/fx_iridescent.vmat", _ => null };
		var material = path is null ? null : Material.Load( path );
		foreach ( var r in styleRenderers )
			if ( r.IsValid() ) r.MaterialOverride = material;
	}

	/// <summary> The scan-in: every piece wears the hologram shader while a glowing line sweeps up him, then the real materials return. </summary>
	void UpdateSpawnIn()
	{
		if ( spawnDone ) return;
		if ( SpawnInTime <= 0f ) { spawnDone = true; ApplyLook(); return; }

		if ( hologramMaterial is null )
		{
			hologramMaterial = Material.Load( "materials/fx/fx_hologram.vmat" );
			foreach ( var part in parts )
			{
				if ( part is null ) continue;
				foreach ( var (renderer, _) in part.Renderers )
					if ( renderer.IsValid() ) spawnSaved.Add( (renderer, renderer.MaterialOverride) );
			}
			foreach ( var (renderer, _) in spawnSaved ) renderer.MaterialOverride = hologramMaterial;
		}

		var progress = MathX.Clamp( sinceSpawned.Relative / SpawnInTime, 0f, 1f );
		var centre = parts[Torso].Go.WorldPosition.z;
		var revealZ = MathX.Lerp( centre - 60f * Size, centre + 95f * Size, progress );
		foreach ( var (renderer, original) in spawnSaved )
		{
			if ( !renderer.IsValid() ) continue;
			if ( progress >= 1f ) renderer.MaterialOverride = original;
			else renderer.Attributes.Set( "RevealZ", revealZ );
		}

		if ( progress >= 1f )
		{
			spawnDone = true;
			spawnSaved.Clear();
			ApplyLook();
		}
	}

	void Build()
	{
		rig = new GameObject( true, "MC61 Rig" );
		rig.NetworkMode = NetworkMode.Never;
		rig.WorldTransform = WorldTransform;
		rig.WorldScale = Size;

		// Body: the ghost sheet, the tuxedo jacket, a squared bow tie, and the shirt choker and buttons.
		var torso = AddPart( Torso, -1, rig, "Torso", Vector3.Zero, Angles.Zero, TorsoHealth );
		Sculpt( torso, "body", GhostBody );
		Sculpt( torso, "jacket", Tux );
		Sculpt( torso, "bow", Tux );
		Sculpt( torso, "front", Tux );
		Trigger( torso, new Vector3( 0, 0, -16 ), new Vector3( 0, 0, 24 ), 19f );

		// Head: a hit shape over the dome (the meshes live on the body).
		var head = AddPart( Head, Torso, torso.Go, "Head", HeadPivot, Angles.Zero, 0f );
		Trigger( head, new Vector3( 0, 0, -2 ), new Vector3( 0, 0, 5 ), 19f );

		// Eye: a dome flush on the face, wearing a live 2D picture, with a lid ring round it.
		var eye = AddPart( Eye, Head, head.Go, "Eye", EyeFromHead, Angles.Zero, EyeHealth );
		var eyeball = new GameObject( eye.Go, true, "Eyeball" );
		var eyeRenderer = eyeball.AddComponent<ModelRenderer>();
		eyeRenderer.Model = Model.Load( "models/creatures/mc61g/eyeball.vmdl" );
		eyeRenderer.Tint = Color.White;
		eye.Renderers.Add( (eyeRenderer, Color.White) );
		MakeEyePicture( eyeRenderer );
		Sculpt( eye, "lids", Color.Black );
		Trigger( eye, new Vector3( 0, -5, 0 ), new Vector3( 0, 5, 0 ), 9f );

		var hat = AddPart( Hat, Head, head.Go, "Hat", HatFromHead, Angles.Zero, HatHealth );
		hat.Go.LocalRotation = HatRotation;
		Sculpt( hat, "hat", Tux );
		Trigger( hat, new Vector3( 0, 0, 2 ), new Vector3( 0, 0, 10 ), 8.5f );

		// Hands: floating gloves, each with a palm and fully jointed fingers.
		BuildHand( HandL, 1f );
		BuildHand( HandR, -1f );

		// The tail: four pieces in a chain that sway and trail on their own.
		var parentIndex = Torso;
		var parentGo = torso.Go;
		for ( var i = 0; i < TailCount; i++ )
		{
			var tail = AddPart( Tail0 + i, parentIndex, parentGo, $"Tail {i}", i == 0 ? TailRoot : new Vector3( 0, 0, -TailSegment ), Angles.Zero, TailHealth );
			Sculpt( tail, $"tail{i}", GhostBody );
			Trigger( tail, Vector3.Zero, new Vector3( 0, 0, -TailSegment ), TailRadius[i] * 0.9f );
			parentIndex = Tail0 + i;
			parentGo = tail.Go;
		}

		// The beam, its glowing core, and the thin aiming line shown while charging.
		beam = BeamPiece( "Beam", BeamColor * 2.5f );
		beamCore = BeamPiece( "Beam Core", Color.White * 3f );
		aimLine = BeamPiece( "Aim Line", new Color( 1f, 0.1f, 0.3f ) * 2f );
	}

	void BuildHand( int index, float side )
	{
		var part = AddPart( index, Torso, parts[Torso].Go, side > 0 ? "Hand L" : "Hand R", new Vector3( 6f, side * 36f, 18f ), Angles.Zero, HandHealth );
		part.Go.LocalScale = HandScale;
		Trigger( part, new Vector3( 0, 0, 0 ), new Vector3( 0, 0, 13 ), 8f );

		var skinGo = new GameObject( part.Go, true, "Hand Skin" );
		var skin = skinGo.AddComponent<SkinnedModelRenderer>();
		skin.Model = Model.Load( side > 0 ? "models/creatures/mc61g/hand_l.vmdl" : "models/creatures/mc61g/hand_r.vmdl" );
		skin.UseAnimGraph = false;
		skin.Tint = Glove;
		part.Renderers.Add( (skin, Glove) );

		var hand = new Hand { Side = side, Root = part.Go, Skin = skin };
		hands[index == HandL ? 0 : 1] = hand;
	}

	/// <summary> Looks up the hand skeleton and remembers its rest pose, once the renderer is ready. </summary>
	static bool PrepareHand( Hand hand )
	{
		if ( hand.Ready ) return true;
		var skin = hand.Skin;
		if ( !skin.IsValid() || skin.Model is null || skin.SceneModel is null ) return false;
		for ( var i = 0; i < 14; i++ )
		{
			var name = i < 12 ? $"f{i / 3}_{i % 3}" : $"th_{i - 12}";
			var bone = skin.Model.Bones.GetBone( name );
			if ( bone is null || !skin.TryGetBoneTransform( bone, out var tx ) ) return false;
			hand.Bone[i] = bone.Index;
			hand.Rest[i] = skin.GameObject.WorldTransform.ToLocal( tx );
		}
		hand.Ready = true;
		return true;
	}

	// ---- The live eye picture --------------------------------------------------

	void MakeEyePicture( ModelRenderer renderer )
	{
		eyePixels = new Color32[EyeW * EyeH];
		eyeTexture = Texture.Create( EyeW, EyeH ).WithDynamicUsage().Finish();
		var baseMaterial = Material.Load( "materials/creature/mc61_eye.vmat" );
		eyeMaterial = baseMaterial?.CreateCopy();
		if ( eyeMaterial is null ) return;
		eyeMaterial.Set( "Color", eyeTexture );
		renderer.MaterialOverride = eyeMaterial;
		DrawEye( Vector2.Zero, 0f, 0f );
	}

	/// <summary> Paints the eye: white with veins that redden with `glow`, a pupil looking at `look`, an upper lid closing with `lid`. </summary>
	void DrawEye( Vector2 look, float lid, float glow )
	{
		if ( eyeTexture is null || eyePixels is null ) return;
		var px = look.x * 0.5f;
		var py = look.y * 0.22f;
		var lidEdge = 1f - 2.1f * lid;
		for ( var y = 0; y < EyeH; y++ )
		{
			var ny = 1f - (y + 0.5f) / EyeH * 2f;   // up is +1 (the top row of the picture)
			for ( var x = 0; x < EyeW; x++ )
			{
				var nx = (x + 0.5f) / EyeW * 2f - 1f;
				var rr = MathF.Sqrt( nx * nx + ny * ny );
				// base: white, reddening with the charge, veins creeping in from the rim
				var veins = MathX.Clamp( (rr - 0.55f) * 1.6f, 0f, 1f ) * glow * 0.7f;
				var r = 1f;
				var g = 1f - 0.9f * glow - veins * 0.5f;
				var b = 1f - 0.92f * glow - veins * 0.5f;
				var dx = (nx - px) / 0.3f;
				var dy = (ny - py) / 0.5f;
				if ( dx * dx + dy * dy < 1f ) { r = glow < 0.5f ? 0.02f : 0.25f; g = b = glow < 0.5f ? 0.02f : 0f; }
				var hx = (nx - px + 0.1f) / 0.07f;
				var hy = (ny - py - 0.2f) / 0.09f;
				if ( hx * hx + hy * hy < 1f ) { r = g = b = 1f; }
				if ( lid > 0.02f )
				{
					if ( ny > lidEdge ) { r = GhostBody.r; g = GhostBody.g; b = GhostBody.b; }
					else if ( ny > lidEdge - 0.06f ) { r = g = b = 0.02f; }
				}
				eyePixels[y * EyeW + x] = new Color32( (byte)(MathX.Clamp( r, 0f, 1f ) * 255f), (byte)(MathX.Clamp( g, 0f, 1f ) * 255f), (byte)(MathX.Clamp( b, 0f, 1f ) * 255f), 255 );
			}
		}
		eyeTexture.Update( eyePixels, 0, 0, EyeW, EyeH );
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
		// MC61 keeps smooth skin: textured, he read as a stone statue.
		part?.Renderers.Add( (r, color) );
	}

	/// <summary> A Blender-sculpted piece (models/creatures/mc61), modelled in the part's own space. </summary>
	void Sculpt( Part part, string model, Color color, GameObject parent = null )
	{
		var go = new GameObject( parent ?? part.Go, true, "Shape" );
		var r = go.AddComponent<ModelRenderer>();
		if ( model == "body" || model.StartsWith( "tail" ) ) styleRenderers.Add( r );
		r.Model = Model.Load( $"models/creatures/mc61g/{model}.vmdl" );
		r.Tint = color;
		part.Renderers.Add( (r, color) );
	}

	/// <summary> A sharp code-built tooth on a part. </summary>
	void Tooth( Part part, Vector3 at, Vector3 direction, float length, Color color )
	{
		var r = ShapeMeshes.AddSpindle( part.Go, "Shape", at, direction, Vector3.Forward, length * 0.28f, length, 0f, 0.1f, color );
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
		else if ( !DevFlags.FreezeEnemies ) Think();

		if ( diedLocally || !rig.IsValid() ) return;

		if ( State != seenState )
		{
			seenState = State;
			localStateTime = 0f;
		}

		rig.WorldPosition = WorldPosition;
		rig.WorldRotation = WorldRotation;
		if ( Has( Torso ) && health.IsValid() )
			CreatureGore.BleedTrail( Scene, parts[Torso].Go.WorldPosition, Goo, health.Fraction, ref bleedTimer );
		UpdateSpawnIn();
		Animate();
		DrawBeam();
		Tint();
	}

	bool Has( int index ) => parts[index] is { Gone: false };

	// ---- Hand poses: curl in degrees for [index, middle, ring, pinky] x [base, mid, tip], then thumb [base, tip] ----

	static readonly float[] PoseOpen = { 8, 6, 4, 6, 5, 3, 8, 6, 4, 12, 8, 5, -10, 8 };
	static readonly float[] PoseWave = { 20, 14, 10, 16, 12, 8, 22, 14, 10, 28, 16, 12, -18, 10 };
	static readonly float[] PoseFist = { 85, 95, 60, 88, 98, 62, 85, 95, 60, 80, 92, 58, 35, 40 };
	static readonly float[] PosePoint = { 4, 2, 2, 88, 98, 62, 86, 96, 60, 82, 94, 58, 30, 20 };
	static readonly float[] PosePinch = { 52, 50, 35, 30, 28, 20, 20, 24, 18, 22, 24, 18, 40, 24 };
	static readonly float[] PoseClaw = { 55, 40, 25, 58, 42, 26, 55, 40, 25, 50, 38, 22, 20, 25 };
	static readonly float[] PoseReach = { 18, 40, 26, 20, 44, 28, 18, 40, 26, 14, 36, 24, 8, 20 };
	static readonly float[] PoseRelaxed = { 18, 24, 14, 22, 28, 16, 26, 30, 18, 30, 32, 20, 10, 12 };
	static readonly float[] PoseSpread = { -6, -4, -2, -2, -2, 0, -4, -2, 0, -10, -6, -2, -25, 0 };

	/// <summary>
	/// Eases a hand's joints toward a pose, then bends the skeleton. `ripple` sends a soft wave across the fingers so they
	/// never look frozen; fingertips lag the knuckles a little; `fan` spreads the fingers; `tremble` shakes them.
	/// </summary>
	void CurlHand( Hand hand, float[] pose, float rate, float tremble, float fan, float thumbSplay, float ripple = 0f )
	{
		if ( hand is null || !hand.Root.IsValid() ) return;
		var t = Time.Now;
		for ( var i = 0; i < 14; i++ )
		{
			var finger = i / 3;
			var joint = i % 3;
			var wobble = tremble * MathF.Sin( t * 31f + i * 1.7f + hand.Side * 2f );
			var flow = i < 12 ? ripple * MathF.Sin( t * 2.4f - finger * 0.8f - joint * 0.6f + hand.Side * 1.3f ) : ripple * 0.6f * MathF.Sin( t * 1.9f + hand.Side );
			var k = MathX.Clamp( Time.Delta * rate * (1f - 0.22f * (i < 12 ? joint : 1)), 0f, 1f );
			hand.Curl[i] = MathX.Lerp( hand.Curl[i], pose[i] + wobble + flow, k );
		}
		hand.Splay = MathX.Lerp( hand.Splay, thumbSplay, MathX.Clamp( Time.Delta * rate, 0f, 1f ) );

		if ( !PrepareHand( hand ) ) return;
		var model = hand.Skin.SceneModel;

		// Fingers: bone m's pose is its rest pose carried through the rotations of every joint above it (about each joint's pivot).
		for ( var f = 0; f < 4; f++ )
		{
			var rot = new Rotation[3];
			rot[0] = Rotation.FromAxis( Vector3.Forward, hand.Side * (f - 1.5f) * fan ) * Rotation.FromAxis( Vector3.Left, hand.Curl[f * 3] );   // index sits next to the thumb on both hands
			rot[1] = Rotation.FromAxis( Vector3.Left, hand.Curl[f * 3 + 1] );
			rot[2] = Rotation.FromAxis( Vector3.Left, hand.Curl[f * 3 + 2] );
			ApplyChain( hand, model, f * 3, 3, rot );
		}

		// Thumb: rests splayed 38 degrees outward; it flexes about that tilted axis.
		var flex = Rotation.FromAxis( Vector3.Forward, -hand.Side * 38f ) * Vector3.Left;
		var thumb = new Rotation[2];
		thumb[0] = Rotation.FromAxis( Vector3.Forward, -hand.Side * (hand.Splay - 38f) ) * Rotation.FromAxis( flex, hand.Curl[12] );
		thumb[1] = Rotation.FromAxis( flex, hand.Curl[13] );
		ApplyChain( hand, model, 12, 2, thumb );
	}

	static void ApplyChain( Hand hand, SceneModel model, int first, int count, Rotation[] rot )
	{
		for ( var m = 0; m < count; m++ )
		{
			var position = hand.Rest[first + m].Position;
			var rotation = hand.Rest[first + m].Rotation;
			for ( var k = m; k >= 0; k-- )
			{
				var pivot = hand.Rest[first + k].Position;
				position = pivot + rot[k] * (position - pivot);
				rotation = rot[k] * rotation;
			}
			model.SetBoneOverride( hand.Bone[first + m], new Transform( position, rotation ) );
		}
	}

	readonly Vector3[] handVel = new Vector3[2];
	Vector3 lastHandRigPosition;

	/// <summary> Hands are springs: they overshoot a little, trail behind his movement, and settle, instead of sliding stiffly. </summary>
	void MoveHand( int index, Vector3 position, Rotation rotation, Vector3 drift )
	{
		var part = parts[index];
		if ( part is null || part.Gone || !part.Go.IsValid() ) return;
		var dt = MathF.Min( Time.Delta, 1f / 30f );
		var slot = index == HandL ? 0 : 1;
		var target = position + drift;
		handVel[slot] += (target - part.Go.LocalPosition) * 55f * dt;
		handVel[slot] *= MathF.Exp( -6.5f * dt );
		part.Go.LocalPosition += handVel[slot] * dt;
		part.Go.LocalRotation = Rotation.Slerp( part.Go.LocalRotation, rotation, MathX.Clamp( dt * 9f, 0f, 1f ) );
	}

	/// <summary> Poses the rig: idle float, charging, beam stance, card spin, or the worm. </summary>
	void Animate()
	{
		var t = Time.Now;
		var dt = MathF.Max( Time.Delta, 0.0001f );
		var st = localStateTime.Relative;
		var torso = parts[Torso].Go;

		// Defaults: idle float, hands drifting and fluttering, tail trailing.
		var torsoAngles = new Angles( MathF.Sin( t * 1.3f ) * 3f, 0, MathF.Sin( t * 0.9f ) * 4f );
		var headAngles = new Angles( 0, MathF.Sin( t * 0.7f ) * 8f, 0 );
		var bob = MathF.Sin( t * 1.8f ) * 3f;
		var spin = 0f;
		var tailWiggle = 1f;
		var eyeGlow = 0f;

		// Hands hover loosely at his sides: relaxed fingers with a soft ripple, wrists swaying, bobbing out of step.
		float[] poseL = PoseRelaxed, poseR = PoseRelaxed;
		var tremble = 0f;
		var fan = 4f;
		var ripple = 9f;
		var thumbSplay = 34f;
		var posL = new Vector3( 8f, 36f, 12f + MathF.Sin( t * 1.4f ) * 4f + MathF.Sin( t * 0.7f ) * 2f );
		var posR = new Vector3( 8f, -36f, 14f + MathF.Sin( t * 1.4f + 1.9f ) * 4f + MathF.Sin( t * 0.8f + 1f ) * 2f );
		var sway = (float f) => Rotation.FromAxis( Vector3.Left, MathF.Sin( t * 1.3f + f ) * 9f ) * Rotation.FromAxis( Vector3.Up, MathF.Cos( t * 1.1f + f * 1.4f ) * 4f );
		var rotL = Rotation.FromAxis( Vector3.Forward, -24f + MathF.Sin( t * 1.1f ) * 7f ) * sway( 0f );
		var rotR = Rotation.FromAxis( Vector3.Forward, 26f + MathF.Sin( t * 1.2f ) * 7f ) * sway( 2f );

		switch ( State )
		{
			case Mode.Charge:
			{
				// Leans back; hands draw out to either side at shoulder height, fingers fanned and slowly curling
				// inward toward the eye as the charge builds, trembling harder the closer he gets.
				torsoAngles = new Angles( -8f - st * 3f, 0, 0 );
				headAngles = new Angles( 10f, 0, 0 );
				bob = MathF.Sin( t * 30f ) * 0.6f * st;
				var charge = MathX.Clamp( st / ChargeTime, 0f, 1f );
				eyeGlow = charge * (0.85f + 0.15f * MathF.Sin( t * 20f ));
				poseL = poseR = PoseReach;
				fan = 22f - 4f * charge;
				ripple = 2f;
				tremble = 1.2f + 3.5f * charge;
				// Reaching to grab: arms out toward the player, palms toward them, fingers hooked and closing in.
				var lift = 34f + MathF.Sin( t * 22f ) * charge * 0.8f;
				var reach = 30f + 14f * charge;
				posL = new Vector3( reach, 24f - 6f * charge, lift );
				posR = new Vector3( reach, -24f + 6f * charge, lift );
				rotL = Rotation.FromAxis( Vector3.Forward, -12f ) * Rotation.FromAxis( Vector3.Left, 62f );
				rotR = Rotation.FromAxis( Vector3.Forward, 12f ) * Rotation.FromAxis( Vector3.Left, 62f );
				tailWiggle = 0.5f;
				break;
			}

			case Mode.Beam:
			{
				torsoAngles = new Angles( 6f, 0, 0 );
				headAngles = Angles.Zero;
				bob = MathF.Sin( t * 45f ) * 0.8f;
				eyeGlow = 1f;
				// Hands held wide, palms toward the eye, fingers splayed and rattling with the beam.
				poseL = poseR = PoseSpread;
				tremble = 3.5f;
				fan = 10f;
				ripple = 0f;
				posL = new Vector3( 12f, 34f, 30f );
				posR = new Vector3( 12f, -34f, 30f );
				rotL = Rotation.FromAxis( Vector3.Forward, -26f ) * Rotation.FromAxis( Vector3.Up, 0f );
				rotR = Rotation.FromAxis( Vector3.Forward, 26f ) * Rotation.FromAxis( Vector3.Up, 0f );
				tailWiggle = 0.3f;
				break;
			}

			case Mode.Cards:
			{
				// Hands come together holding a card, then fly apart in a spin while the cards fly.
				var hold = st < 0.55f;
				poseL = poseR = hold ? PosePinch : PoseSpread;
				fan = hold ? 0f : 12f;
				ripple = 0f;
				tremble = hold ? 1.5f : 0f;
				posL = hold ? new Vector3( 22f, 9f, 14f ) : new Vector3( 8f, 38f, 22f );
				posR = hold ? new Vector3( 22f, -9f, 14f ) : new Vector3( 8f, -38f, 22f );
				rotL = hold ? Rotation.FromAxis( Vector3.Up, 0f ) : Rotation.FromAxis( Vector3.Forward, -40f );
				rotR = hold ? Rotation.FromAxis( Vector3.Up, 0f ) : Rotation.FromAxis( Vector3.Forward, 40f );
				spin = MathX.Clamp( (st - 0.6f) / 1.1f, 0f, 1f ) * 360f;
				tailWiggle = 1.6f;
				break;
			}

			case Mode.Dance:
			{
				// The worm: belly-down, a wave rolling along him, hands waving and fluttering.
				var w = t * 7f;
				torsoAngles = new Angles( 90f + MathF.Sin( w ) * 14f, 0, 0 );
				headAngles = new Angles( -70f + MathF.Sin( w - 1.2f ) * 20f, 0, 0 );
				bob = MathF.Max( 0f, MathF.Sin( w ) ) * 8f;
				poseL = poseR = PoseWave;
				ripple = 16f;
				posL = new Vector3( 6f, 36f, 22f + MathF.Sin( w ) * 10f );
				posR = new Vector3( 6f, -36f, 22f + MathF.Sin( w + 2f ) * 10f );
				rotL = Rotation.FromAxis( Vector3.Forward, MathF.Sin( w ) * 30f );
				rotR = Rotation.FromAxis( Vector3.Forward, MathF.Sin( w + 2f ) * 30f );
				tailWiggle = 3f;
				break;
			}
		}

		torso.LocalPosition = new Vector3( 0, 0, bob );
		torso.LocalRotation = Rotation.FromYaw( spin ) * torsoAngles.ToRotation();
		Pose( Head, headAngles );

		// Hands drift to where the attack wants them; every finger eases into the pose.
		var rigMove = (WorldPosition - lastHandRigPosition) / dt;
		lastHandRigPosition = WorldPosition;
		var lag = -(WorldRotation.Inverse * rigMove) * 0.06f;      // hands trail behind when he moves
		lag = lag.Clamp( new Vector3( -14f ), new Vector3( 14f ) );
		MoveHand( HandL, posL, rotL, lag );
		MoveHand( HandR, posR, rotR, lag );
		CurlHand( hands[0], poseL, 10f, tremble, fan, thumbSplay, ripple );
		CurlHand( hands[1], poseR, 10f, tremble, fan, thumbSplay, ripple );

		AnimateTail( t, dt, tailWiggle );
		AnimateEye( t, dt, eyeGlow );
	}

	/// <summary> A ghostly tail: each piece is a little spring that lags behind his movement and ripples. </summary>
	void AnimateTail( float t, float dt, float wiggle )
	{
		var velocityNow = (WorldPosition - lastRigPosition) / dt;
		lastRigPosition = WorldPosition;
		var local = WorldRotation.Inverse * velocityNow;
		for ( var i = 0; i < TailCount; i++ )
		{
			var tail = parts[Tail0 + i];
			if ( tail is null || tail.Gone ) break;
			var drag = 1f + i * 0.35f;
			var targetPitch = (i == 0 ? 42f : 12f) + MathX.Clamp( local.x * 0.05f, -22f, 22f ) * drag + MathF.Sin( t * 2.2f - i * 0.9f ) * (5f + 4f * i) * wiggle;
			var targetRoll = -14f + MathX.Clamp( -local.y * 0.05f, -22f, 22f ) * drag + MathF.Cos( t * 1.7f - i * 0.8f ) * (4f + 3f * i) * wiggle;
			tailPitchVel[i] += (targetPitch - tailPitch[i]) * 38f * dt;
			tailPitchVel[i] *= MathF.Exp( -4f * dt );
			tailPitch[i] += tailPitchVel[i] * dt;
			tailRollVel[i] += (targetRoll - tailRoll[i]) * 38f * dt;
			tailRollVel[i] *= MathF.Exp( -4f * dt );
			tailRoll[i] += tailRollVel[i] * dt;
			tail.Go.LocalRotation = Rotation.FromAxis( Vector3.Forward, tailRoll[i] ) * Rotation.FromAxis( Vector3.Left, tailPitch[i] );
		}
	}

	// ---- The living eye: tracks you, blinks (sometimes off-beat), and reddens as it charges ----

	Vector2 eyeLook;
	float eyeLid;
	float eyeGlowNow;
	float nextBlinkAt = 2f;
	float blinkStart = -1f;
	float blinkLength = 0.2f;
	float nextEyePaint;

	void AnimateEye( float t, float dt, float glowTarget )
	{
		if ( eyeTexture is null || !Has( Eye ) ) return;

		// Where is he looking? At the nearest player, in the head's own frame.
		var want = Vector2.Zero;
		PlayerSurvivor target = null;
		var best = float.MaxValue;
		foreach ( var p in Scene.GetAllComponents<PlayerSurvivor>() )
		{
			if ( !p.IsAlive ) continue;
			var d = p.WorldPosition.DistanceSquared( WorldPosition );
			if ( d < best ) { best = d; target = p; }
		}
		if ( target is not null )
		{
			var dir = (target.WorldPosition + Vector3.Up * 50f - parts[Eye].Go.WorldPosition).Normal;
			var localDir = parts[Head].Go.WorldRotation.Inverse * dir;
			// By angle, not raw direction: he hovers above you, so a raw height component would pin the pupil to the bottom.
			var yaw = MathF.Atan2( localDir.y, MathF.Max( 0.05f, localDir.x ) ).RadianToDegree();
			var pitch = MathF.Atan2( localDir.z, MathF.Sqrt( localDir.x * localDir.x + localDir.y * localDir.y ) ).RadianToDegree();
			want = new Vector2( MathX.Clamp( yaw / 26f, -1f, 1f ), MathX.Clamp( pitch / 60f, -0.8f, 0.8f ) );
		}
		else want = new Vector2( MathF.Sin( t * 0.8f ), MathF.Cos( t * 0.6f ) * 0.4f );
		eyeLook = Vector2.Lerp( eyeLook, want, MathX.Clamp( dt * 14f, 0f, 1f ) );

		// Blinks: usually a quick one, sometimes slow or a double.
		if ( t >= nextBlinkAt )
		{
			blinkStart = t;
			blinkLength = Game.Random.Float() < 0.2f ? Game.Random.Float( 0.35f, 0.55f ) : 0.18f;
			nextBlinkAt = t + (Game.Random.Float() < 0.2f ? 0.4f : Game.Random.Float( 1.8f, 5f ));
		}
		var k = blinkStart < 0f ? 2f : (t - blinkStart) / blinkLength;
		var lid = k is < 0f or > 1f ? 0f : MathF.Sin( k * MathF.PI );
		if ( State is Mode.Beam ) lid = MathF.Max( lid, 0.18f ); // narrowed to a glare while firing
		eyeLid = lid;

		eyeGlowNow = MathX.Lerp( eyeGlowNow, glowTarget, MathX.Clamp( dt * 10f, 0f, 1f ) );

		// Repaint at ~40 fps.
		if ( t < nextEyePaint ) return;
		nextEyePaint = t + 0.025f;
		DrawEye( eyeLook, eyeLid, eyeGlowNow );
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
				if ( part.Index == Eye && tint == Color.White ) color = Color.White * (1f + 1.2f * eyeGlowNow);
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
		var hasCase = Has( HandL ) || Has( HandR );

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
					var origin = parts[Has( HandR ) ? HandR : HandL].Go.WorldPosition;
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
		lastShotPointBlank = (position - origin).Length < CreatureGore.OverkillRange;

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

			case Hat:
				Detach( part, direction * 80f + Vector3.Up * 260f, spin: 25f );
				return;
		}

		// A hand that comes off drops its card in a puff of confetti.
		if ( index is HandL or HandR ) CardConfetti( at );
		Detach( part, direction * Game.Random.Float( 80f, 180f ) + Vector3.Up * 80f );
		CreatureGore.Twitch( part.Go, Game.Random.Float( 2f, 3.5f ), 1.2f );
	}

	/// <summary> Raw stumps on both sides of a cut: on what he keeps, and on the piece that flies off. </summary>
	void AddStumps( Part part, float scale )
	{
		if ( part.Index == Hat ) return;
		var parent = part.Parent >= 0 ? parts[part.Parent] : null;
		var joint = part.Go.WorldPosition;
		var outward = parent is not null ? (joint - parent.Go.WorldPosition).Normal : Vector3.Up;
		var radius = (part.Index is HandL or HandR ? 5f : 7f) * Size * scale;
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
		// Keep the capsule hit shapes, made solid, so a leg tumbles and lies like a leg (not a ball).
		var solids = 0;
		foreach ( var c in go.Components.GetAll<Collider>( FindMode.EverythingInSelfAndDescendants ).ToList() )
		{
			if ( c is CapsuleCollider capsule ) { capsule.IsTrigger = false; solids++; }
			else c.Destroy();
		}
		foreach ( var o in go.GetAllObjects( true ) ) o.Tags.Remove( "creature" );

		var bounds = go.GetBounds();
		var radius = MathX.Clamp( bounds.Size.Length * 0.25f, 4f, 30f );
		var gib = Gib.Attach( go, gore, radius, 40f, 6f, part.Velocity * 0.4f + push, Goo, addCollider: solids == 0 );
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
		DieEverywhere( lastShotDirection, lastShotPointBlank );
	}

	[Rpc.Broadcast]
	void DieEverywhere( Vector3 direction, bool overkill ) => DieLocal( direction, quiet: false, overkill );

	/// <summary> The head bursts in rainbow goo, cards everywhere, and the rest of him falls apart. </summary>
	void DieLocal( Vector3 direction, bool quiet, bool overkill = false )
	{
		if ( diedLocally ) return;
		diedLocally = true;
		timeSinceDeath = 0f;
		var overkillAt = parts[Head].Go.WorldPosition;
		if ( !rig.IsValid() ) return;

		if ( quiet ) { rig.Destroy(); return; }

		beam?.Destroy();
		beamCore?.Destroy();
		aimLine?.Destroy();

		if ( overkill ) CreatureGore.Overkill( Scene, overkillAt, direction, Skin, Goo, 1.6f * Size );
		var headAt = parts[Head].Go.WorldPosition + Vector3.Up * 18f * Size;
		for ( var k = 0; k < 6; k++ ) gore?.GooBurst( headAt, direction + Vector3.Up, Goo, 10, 380f, 1.6f );
		gore?.GooSpray( headAt, Vector3.Down, 8, Goo, 300f, 0.8f, 1.8f );
		for ( var b = 0; b < 4; b++ ) gore?.ThrowBlob( headAt, direction, Skin, Goo, 5f * Size );
		CardConfetti( headAt );

		// Hat and limbs fly off; the head is gone; the torso drops.
		foreach ( var index in new[] { Hat, HandL, HandR, Tail0 } )
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
