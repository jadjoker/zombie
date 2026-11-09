using Sandbox.Citizen;

/// <summary>
/// The custom third-person body. A rigged cartoon survivor (models/characters/survivor) is posed every frame from the
/// built-in citizen skeleton, which keeps running unseen as the animation driver (walking, strafing, jumping, crouching,
/// hold poses, aim, left-hand IK all come from it). Each of our bones is turned to match the citizen's, and the arms and
/// legs are solved with two-bone IK so hands still meet the gun and feet stay planted, whatever our proportions are.
/// Sits on the player next to PlayerAppearance.
/// </summary>
public sealed class PlayerRig : Component
{
	[Property] public string ModelPath { get; set; } = "models/characters/survivor/survivor.vmdl";
	/// <summary> Our hips and shoulders sit a little higher than the citizen's, so everything is lifted by this. </summary>
	[Property] public float Lift { get; set; } = 1.7f;
	[Property] public float FootLift { get; set; } = 1.8f;

	PlayerController Controller => GetComponent<PlayerController>();
	PlayerAppearance Appearance => GetComponent<PlayerAppearance>();
	PlayerSurvivor Survivor => GetComponent<PlayerSurvivor>();

	SkinnedModelRenderer driver;
	SkinnedModelRenderer body;
	Material invisible;
	bool ready;

	// ---- Slide: the whole body lays back with one leg out in front and the other tucked, hands still on the gun ----
	/// <summary> How far the body leans back while sliding, degrees. </summary>
	[Property] public float SlideLean { get; set; } = 48f;
	/// <summary> Where the hips sit while sliding (forward, up) relative to the player's feet. </summary>
	[Property] public Vector2 SlideHips { get; set; } = new( -9f, 17f );
	float slideBlend;
	float slideHold;
	float swimBlend;
	float swimPhase;

	// ---- Jet feet: while the jetpack fires, the boots fold into little propellers that spin and churn bubbles ----
	/// <summary> Set by the owner's Jetpack while it thrusts or surges; synced so everyone sees the propellers. </summary>
	[Sync] public bool JetOn { get; set; }
	/// <summary> Surge wind-up (0 to 1) and the burst itself, from the owner's Jetpack. </summary>
	[Sync] public float SurgeCharge { get; set; }
	[Sync] public bool SurgeOn { get; set; }
	[Sync] public Vector3 SurgeDir { get; set; } = Vector3.Forward;
	float chargeBlend;
	float surgeBlend;
	float jetBlend;
	float propSpin;
	TimeSince timeSincePropBubble;
	readonly Dictionary<string, GameObject> props = new();
	readonly Dictionary<string, GameObject> propSpinners = new();
	float swimPitch, swimRoll;
	Vector3 swimMove, swimAccel, lastSwimLocal;
	Rotation slideYaw = Rotation.Identity;
	string appliedSkin;

	readonly Dictionary<string, Vector3> restPos = new();
	readonly Dictionary<string, Rotation> restRot = new();
	readonly Dictionary<string, BoneCollection.Bone> bones = new();

	// The citizen's bones have X along the bone and Z to the character's left; this turns that into our X forward, Y left, Z up.
	static readonly Rotation CitizenToFrame = Rotation.LookAt( Vector3.Up, Vector3.Left ).Inverse;

	protected override void OnStart()
	{
		invisible = Material.Load( "materials/fx/invisible.vmat" );
	}

	bool Setup()
	{
		driver = Controller?.Renderer;
		if ( !driver.IsValid() ) return false;

		if ( !body.IsValid() )
		{
			var model = Model.Load( ModelPath );
			if ( model is null || model.IsError ) return false;
			var go = new GameObject( driver.GameObject, true, "Survivor Body" );
			go.NetworkMode = NetworkMode.Never;
			go.LocalPosition = Vector3.Zero;
			go.LocalRotation = Rotation.Identity;
			body = go.AddComponent<SkinnedModelRenderer>();
			body.Model = model;
		}

		restPos.Clear();
		restRot.Clear();
		bones.Clear();
		var xf = body.WorldTransform;
		foreach ( var bone in body.Model.Bones.AllBones )
		{
			var b = bone;
			if ( !body.TryGetBoneTransformAnimation( b, out var tx ) ) return false;
			bones[b.Name] = b;
			restPos[b.Name] = xf.PointToLocal( tx.Position );
			restRot[b.Name] = xf.Rotation.Inverse * tx.Rotation;
		}
		return bones.Count > 10;
	}

	/// <summary> Keep the citizen (and its clothes) animating but not drawn. Our own model is what's seen. </summary>
	void HideDriver()
	{
		if ( invisible is null || invisible.IsError ) return;
		foreach ( var renderer in driver.GameObject.GetComponentsInChildren<ModelRenderer>( true ) )
		{
			if ( renderer == body || !renderer.IsValid() ) continue;
			if ( renderer.GameObject.Name.StartsWith( "World " ) ) continue;      // the guns in hand are still shown
			if ( renderer.GameObject.Parent == body?.GameObject ) continue;
			if ( worldAxe.IsValid() && renderer.GameObject.Parent == worldAxe ) continue;   // the axe in its hands
			if ( renderer.GameObject.Name == "Prop Piece" ) continue;                         // the foot propellers
			if ( renderer.MaterialOverride != invisible ) renderer.MaterialOverride = invisible;
		}
	}

	protected override void OnUpdate()
	{
		if ( !driver.IsValid() ) return;
		var moving = GetComponent<PlayerMovement>();
		// Hold the slide pose a moment after the slide flickers off (it can end and restart within a few frames), and
		// ease in quickly but out slowly so standing up never snaps.
		if ( moving?.IsSliding ?? false ) slideHold = 0.18f;
		slideHold = MathF.Max( 0f, slideHold - Time.Delta );
		var want = slideHold > 0f && (Survivor?.IsAlive ?? true) ? 1f : 0f;
		slideBlend = MathX.Approach( slideBlend, want, Time.Delta * (want > slideBlend ? 8f : 3.2f) );
		// Swimming (airborne in the water feel): pitched forward around the hips, legs fluttering.
		var water = GetComponent<WaterFeel>();
		swimBlend = MathX.Approach( swimBlend, slideBlend > 0.001f ? 0f : (water?.Swimming ?? 0f), Time.Delta * 4f );
		if ( swimBlend > 0.001f )
		{
			// The body leans with the way it's moving, relative to where it faces: into a forward swim, back when
			// backing off, banked into a strafe, upright when rising and a touch nose-down when sinking. It eases
			// toward that (and overshoots a little on hard changes), and drifts and breathes when it's idle.
			var dt = Time.Delta;
			var velocity = Controller.IsValid() ? Controller.Velocity : Vector3.Zero;
			var swimYaw = Rotation.FromYaw( driver.WorldRotation.Yaw() );
			var local = swimYaw.Inverse * velocity;
			var forward = MathX.Clamp( local.x / 260f, -1f, 1f );
			var sideways = MathX.Clamp( local.y / 260f, -1f, 1f );
			var rising = MathX.Clamp( velocity.z / 250f, -1f, 1f );
			swimMove = new Vector3( forward, sideways, rising );

			var accel = (local - lastSwimLocal) / MathF.Max( dt, 0.001f );
			lastSwimLocal = local;
			swimAccel = Vector3.Lerp( swimAccel, accel, 1f - MathF.Exp( -6f * dt ) );

			var speed = new Vector2( local.x, local.y ).Length;
			swimPhase += dt * (3.2f + speed / 70f);
			var idle = 1f - MathX.Clamp( speed / 120f, 0f, 1f );
			var drift = Time.Now;

			var pitch = (forward > 0f ? 44f * forward : 20f * forward)    // forward: dive in; backward: lean back
				- 16f * rising                                              // rising stands it up, sinking tips it forward
				+ MathX.Clamp( swimAccel.x * 0.02f, -10f, 10f )             // a lurch when it speeds up or brakes
				+ (8f + 3f * MathF.Sin( drift * 1.3f )) * idle;            // idle: a slight hunch that breathes
			var roll = -26f * sideways + MathX.Clamp( -swimAccel.y * 0.015f, -8f, 8f ) + 2.5f * MathF.Sin( drift * 0.9f ) * idle;
			swimPitch = MathX.Lerp( swimPitch, pitch, 1f - MathF.Exp( -5f * dt ) );
			swimRoll = MathX.Lerp( swimRoll, roll, 1f - MathF.Exp( -5f * dt ) );

			var tilt = swimYaw * new Angles( swimPitch * swimBlend, 0f, swimRoll * swimBlend ).ToRotation();
			var bob = Vector3.Up * MathF.Sin( drift * 1.6f ) * 1.8f * idle * swimBlend;
			driver.WorldRotation = tilt;
			driver.WorldPosition = GameObject.WorldPosition + Vector3.Up * 34.8f + bob - tilt * new Vector3( 0f, 0f, 34.8f );
		}
		// Surge: curls up as it winds up, then streamlines head-first along the burst.
		chargeBlend = MathX.Lerp( chargeBlend, SurgeCharge, 1f - MathF.Exp( -12f * Time.Delta ) );
		surgeBlend = MathX.Approach( surgeBlend, SurgeOn ? 1f : 0f, Time.Delta * (SurgeOn ? 10f : 3f) );
		if ( (chargeBlend > 0.01f || surgeBlend > 0.001f) && slideBlend <= 0.001f )
		{
			var rot = driver.WorldRotation;
			var yawRot = Rotation.FromYaw( rot.Yaw() );
			if ( chargeBlend > 0.01f )
				rot = Rotation.Slerp( rot, yawRot * Rotation.FromPitch( 28f ), chargeBlend );   // hunched over, gathering
			if ( surgeBlend > 0.001f )
			{
				var dir = SurgeDir.IsNearlyZero() ? yawRot.Forward : SurgeDir.Normal;
				// Head toward the burst, belly toward the ground (or forward when it's going straight up).
				var belly = (Vector3.Down - dir * Vector3.Dot( Vector3.Down, dir ));
				if ( belly.Length < 0.2f ) belly = yawRot.Forward;
				var superman = Rotation.LookAt( belly.Normal, dir );
				rot = Rotation.Slerp( rot, superman, surgeBlend * surgeBlend * (3f - 2f * surgeBlend) );
			}
			driver.WorldRotation = rot;
			driver.WorldPosition = GameObject.WorldPosition + Vector3.Up * 34.8f - rot * new Vector3( 0f, 0f, 34.8f );
		}
		if ( slideBlend <= 0.001f ) return;

		// Tilt the (unseen) citizen, and our body with it: lowered and leant back around the hips, hands and gun along for the ride.
		var yaw = driver.WorldRotation.Yaw();
		slideYaw = Rotation.FromYaw( yaw );
		var ease = slideBlend * slideBlend * (3f - 2f * slideBlend);
		var rotation = slideYaw * Rotation.FromPitch( -SlideLean * ease );
		var feet = GameObject.WorldPosition;
		var pelvisRest = feet + Vector3.Up * 34.8f;
		var pelvisSlide = feet + slideYaw * new Vector3( SlideHips.x, 0f, SlideHips.y );
		var pelvis = Vector3.Lerp( pelvisRest, pelvisSlide, ease );
		driver.WorldRotation = rotation;
		driver.WorldPosition = pelvis - rotation * new Vector3( 0f, 0f, 34.8f );
	}

	// ---- Melee: the robot swings a fire axe in both hands, matching the first-person swing ----

	/// <summary> Length of one swing (matches PlayerMelee's cooldown). </summary>
	const float SwingTime = 0.55f;
	TimeSince timeSinceSwing = 999f;
	bool swingLeft;
	GameObject worldAxe;

	/// <summary> A surge slam's bubble blast, seen by everyone. </summary>
	[Rpc.Broadcast( NetFlags.Unreliable )]
	public void PlaySlam( Vector3 at, Vector3 direction, float strength ) => Jetpack.SlamBubbles( at, direction, strength );

	public bool IsSwinging => timeSinceSwing < SwingTime;
	/// <summary> The axe is in its hands (the gun is put away), the middle of the swing once the hands have reached it. </summary>
	public bool AxeOut => timeSinceSwing > 0.035f && timeSinceSwing < SwingTime - 0.065f;

	/// <summary> Plays the axe swing on this player's body on every machine. </summary>
	[Rpc.Broadcast( NetFlags.Unreliable )]
	public void PlaySwing( bool left )
	{
		timeSinceSwing = 0f;
		swingLeft = left;
	}

	/// <summary> A key pose of the axe, in the body's own space: where the grip is, the way the haft points, the way the edge faces. </summary>
	readonly record struct AxeKey( float Time, Vector3 Grip, Vector3 Haft, Vector3 Edge, float Twist );

	// Right-handed swing (the left one is its mirror). The blade lands at 0.24 s, like the first-person swing.
	static readonly AxeKey[] SwingKeys =
	{
		new( 0.00f, new Vector3( 15f, -11f, 44f ), new Vector3( 0.35f, 0.1f, 0.93f ), new Vector3( 1f, 0f, 0f ), 0f ),
		new( 0.07f, new Vector3( 12f, -12f, 50f ), new Vector3( 0.2f, 0.1f, 0.97f ), new Vector3( 1f, 0.2f, 0f ), 8f ),
		new( 0.15f, new Vector3( 3f, -11f, 64f ), new Vector3( -0.55f, -0.15f, 0.82f ), new Vector3( 0.5f, 0.3f, 0.8f ), 26f ),
		new( 0.24f, new Vector3( 22f, -2f, 47f ), new Vector3( 0.9f, 0.25f, -0.1f ), new Vector3( 0f, 0.45f, -0.9f ), -12f ),
		new( 0.31f, new Vector3( 17f, 9f, 34f ), new Vector3( 0.5f, 0.6f, -0.6f ), new Vector3( 0f, 0.7f, -0.7f ), -28f ),
		new( 0.55f, new Vector3( 15f, -11f, 44f ), new Vector3( 0.35f, 0.1f, 0.93f ), new Vector3( 1f, 0f, 0f ), 0f ),
	};

	static Rotation AxeRotation( Vector3 haft, Vector3 edge )
	{
		var x = haft.Normal;
		var e = (edge - x * Vector3.Dot( edge, x )).Normal;
		return Rotation.LookAt( x, -e );      // haft along X, the edge toward -Z
	}

	static Vector3 CatmullRom( Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t )
	{
		var t2 = t * t;
		var t3 = t2 * t;
		return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
	}

	static float CatmullRom( float p0, float p1, float p2, float p3, float t )
	{
		var t2 = t * t;
		var t3 = t2 * t;
		return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
	}

	/// <summary>
	/// Where the axe is right now in the swing (body space), how far the torso twists, and how much of the swing
	/// is showing (0 to 1: it eases in from the gun pose at the start and back out at the end).
	/// The keys are joined by a smooth curve, so the axe never stops dead at a key.
	/// </summary>
	(Vector3 Grip, Rotation Rot, float Twist, float Weight) SwingPose()
	{
		var t = MathX.Clamp( timeSinceSwing, 0f, SwingTime );
		var n = SwingKeys.Length;
		var i = 0;
		while ( i < n - 2 && t > SwingKeys[i + 1].Time ) i++;
		var k0 = SwingKeys[Math.Max( i - 1, 0 )];
		var k1 = SwingKeys[i];
		var k2 = SwingKeys[i + 1];
		var k3 = SwingKeys[Math.Min( i + 2, n - 1 )];
		var u = k2.Time > k1.Time ? MathX.Clamp( (t - k1.Time) / (k2.Time - k1.Time), 0f, 1f ) : 1f;

		var side = swingLeft ? -1f : 1f;
		Vector3 M( Vector3 v ) => new( v.x, v.y * side, v.z );
		var grip = CatmullRom( M( k0.Grip ), M( k1.Grip ), M( k2.Grip ), M( k3.Grip ), u );
		var haft = CatmullRom( M( k0.Haft ), M( k1.Haft ), M( k2.Haft ), M( k3.Haft ), u );
		var edge = CatmullRom( M( k0.Edge ), M( k1.Edge ), M( k2.Edge ), M( k3.Edge ), u );
		var twist = CatmullRom( k0.Twist, k1.Twist, k2.Twist, k3.Twist, u ) * side;

		// Ease in over the first 0.08 s and out over the last 0.14 s.
		float Smooth( float x ) { x = MathX.Clamp( x, 0f, 1f ); return x * x * (3f - 2f * x); }
		var weight = MathF.Min( Smooth( t / 0.08f ), Smooth( (SwingTime - t) / 0.14f ) );
		return (grip, AxeRotation( haft, edge ), twist, weight);
	}

	/// <summary> A fire axe from boxes for the body to hold (origin at the grip, haft along +X, edge toward -Z). </summary>
	GameObject BuildWorldAxe()
	{
		var root = new GameObject( body.GameObject, true, "World Axe" );
		root.NetworkMode = NetworkMode.Never;
		void Part( Vector3 at, Vector3 size, Color color )
		{
			var go = new GameObject( root, true, "Axe Part" );
			go.LocalPosition = at;
			go.LocalScale = Shapes.BoxScale( size );
			var r = go.AddComponent<ModelRenderer>();
			r.Model = Shapes.Box;
			r.Tint = color;
		}
		Part( new Vector3( 12f, 0, 0 ), new Vector3( 28f, 1.9f, 1.9f ), new Color( 0.55f, 0.34f, 0.18f ) );   // haft
		Part( new Vector3( 2.5f, 0, 0 ), new Vector3( 6f, 2.3f, 2.3f ), new Color( 0.27f, 0.3f, 0.18f ) );    // grip wrap
		Part( new Vector3( 26f, 0, 0 ), new Vector3( 4.4f, 2.6f, 4.2f ), new Color( 0.82f, 0.12f, 0.1f ) );   // head block
		Part( new Vector3( 26.4f, 0, -4.8f ), new Vector3( 5.6f, 1.4f, 5.6f ), new Color( 0.82f, 0.12f, 0.1f ) );   // blade
		Part( new Vector3( 26.6f, 0, -7.6f ), new Vector3( 6.6f, 0.7f, 1.4f ), new Color( 0.85f, 0.87f, 0.9f ) );   // edge
		Part( new Vector3( 26f, 0, 3.8f ), new Vector3( 2.8f, 1.6f, 4f ), new Color( 0.3f, 0.32f, 0.35f ) );   // spike
		return root;
	}

	protected override void OnPreRender()
	{
		jetBlend = MathX.Approach( jetBlend, JetOn && (Survivor?.IsAlive ?? true) ? 1f : 0f, Time.Delta * (JetOn ? 6f : 3f) );
		propSpin = (propSpin + Time.Delta * 1800f * jetBlend * (1f + 1.5f * SurgeCharge + (SurgeOn ? 1f : 0f))) % 360f;
		if ( !ready )
		{
			ready = Setup();
			if ( !ready ) return;
		}
		if ( !driver.IsValid() || !body.IsValid() ) { ready = false; return; }

		HideDriver();
		var alive = Survivor?.IsAlive ?? true;
		var show = (Appearance?.ShowsBody ?? false) && alive;
		if ( body.Enabled != show )
		{
			body.Enabled = show;
			appliedSkin = null;           // turning the renderer off and on again drops its material overrides
		}
		if ( !show )
		{
			if ( worldAxe.IsValid() ) worldAxe.Enabled = false;
			foreach ( var p in props.Values ) if ( p.IsValid() ) p.Enabled = false;
			return;
		}

		// Overrides are lost whenever the renderer's scene object is rebuilt, so look for them rather than trust a flag.
		if ( (appliedSkin != CharacterSkins.Current || !body.Materials.HasOverride( 0 )) && CharacterSkins.Apply( body, CharacterSkins.Current ) )
			appliedSkin = CharacterSkins.Current;

		Pose();
	}

	Transform frame;

	Vector3 Cit( string name )
	{
		driver.TryGetBoneTransform( name, out var tx );
		return frame.PointToLocal( tx.Position );
	}

	Rotation CitRot( string name )
	{
		driver.TryGetBoneTransform( name, out var tx );
		return (frame.Rotation.Inverse * tx.Rotation) * CitizenToFrame;
	}

	Vector3 RestDir( string from, string to ) => (restPos[to] - restPos[from]).Normal;

	void Put( string name, Vector3 position, Rotation delta )
	{
		if ( !bones.TryGetValue( name, out var bone ) ) return;
		// SetBoneTransform takes the model's own space, which is our character space.
		body.SetBoneTransform( bone, new Transform( position, delta * restRot[name] ) );
	}

	/// <summary> Where a two-bone chain bends: the elbow or knee, on the side the pole points to. </summary>
	static Vector3 Bend( Vector3 root, Vector3 target, float upper, float lower, Vector3 pole, out Vector3 reached )
	{
		var d = target - root;
		var dist = MathX.Clamp( d.Length, MathF.Abs( upper - lower ) + 0.05f, upper + lower - 0.05f );
		var dir = d.Length > 0.001f ? d.Normal : Vector3.Down;
		reached = root + dir * dist;
		var along = (dist * dist + upper * upper - lower * lower) / (2f * dist);
		var h = MathF.Sqrt( MathF.Max( upper * upper - along * along, 0f ) );
		var p = pole - dir * Vector3.Dot( pole, dir );
		p = p.Length > 0.001f ? p.Normal : Vector3.Backward;
		return root + dir * along + p * h;
	}

	void Pose()
	{
		frame = driver.WorldTransform;
		var up = Vector3.Up * Lift;

		// Hips and spine, turned like the citizen's.
		var pelvisPos = Cit( "pelvis" ) + up;
		var pelvisRot = CitRot( "pelvis" );
		Put( "pelvis", pelvisPos, pelvisRot );

		var spinePos = pelvisPos + pelvisRot * (restPos["spine"] - restPos["pelvis"]);
		var spineRot = CitRot( "spine_2" );
		var swinging = IsSwinging;
		(Vector3 Grip, Rotation Rot, float Twist, float Weight) axePose = swinging ? SwingPose() : default;
		if ( swinging ) spineRot = Rotation.FromYaw( axePose.Twist * axePose.Weight ) * spineRot;
		Put( "spine", spinePos, spineRot );

		// The axe in its hands while swinging (in place of the gun).
		if ( swinging )
		{
			if ( !worldAxe.IsValid() ) worldAxe = BuildWorldAxe();
			worldAxe.Enabled = AxeOut;   // swapped in once the hands have come to it
			worldAxe.WorldPosition = frame.PointToWorld( axePose.Grip );
			worldAxe.WorldRotation = frame.Rotation * axePose.Rot;
		}
		else if ( worldAxe.IsValid() && worldAxe.Enabled ) worldAxe.Enabled = false;

		var headPos = spinePos + spineRot * (restPos["head"] - restPos["spine"]);
		Put( "head", headPos, CitRot( "head" ) );

		foreach ( var s in new[] { "L", "R" } )
		{
			// Arm: shoulder follows the spine; the wrist goes where the citizen's hand is (so it meets the gun).
			var shoulder = spinePos + spineRot * (restPos[$"upper_{s}"] - restPos["spine"]);
			var upperLen = (restPos[$"lower_{s}"] - restPos[$"upper_{s}"]).Length;
			var lowerLen = (restPos[$"hand_{s}"] - restPos[$"lower_{s}"]).Length;
			var wristTarget = Cit( $"hand_{s}" ) + up;
			var armPole = Cit( $"arm_lower_{s}" ) + up - shoulder;
			if ( swinging )
			{
				// Right hand at the end of the haft, left a hand's width up it; elbows hang down and out.
				var along = s == "R" ? 1.5f : 8f;
				var onAxe = axePose.Grip + axePose.Rot * new Vector3( along, 0f, 2.2f );
				var w = axePose.Weight;
				wristTarget = Vector3.Lerp( wristTarget, onAxe, w );
				armPole = Vector3.Lerp( armPole.Normal, new Vector3( -0.2f, s == "L" ? 1f : -1f, -0.9f ).Normal, w );
			}
			var elbow = Bend( shoulder, wristTarget, upperLen, lowerLen, armPole, out var wrist );
			var upperRot = Rotation.FromToRotation( RestDir( $"upper_{s}", $"lower_{s}" ), (elbow - shoulder).Normal );
			var lowerRot = Rotation.FromToRotation( RestDir( $"lower_{s}", $"hand_{s}" ), (wrist - elbow).Normal );
			Put( $"upper_{s}", shoulder, upperRot );
			Put( $"lower_{s}", elbow, lowerRot );
			Put( $"hand_{s}", wrist, lowerRot );

			// Leg: hip follows the pelvis; the ankle goes where the citizen's ankle is (so feet stay planted).
			var hip = pelvisPos + pelvisRot * (restPos[$"thigh_{s}"] - restPos["pelvis"]);
			var thighLen = (restPos[$"shin_{s}"] - restPos[$"thigh_{s}"]).Length;
			var shinLen = (restPos[$"foot_{s}"] - restPos[$"shin_{s}"]).Length;
			var ankleTarget = Cit( $"ankle_{s}" ) + Vector3.Up * FootLift;
			var kneePole = Cit( $"leg_lower_{s}" ) - hip + Vector3.Forward * 20f;
			var footLift = 0f;
			if ( slideBlend > 0.001f )
			{
				// Slide: right leg stretched out in front, heel skimming the ground; left leg folded under.
				var ease = slideBlend * slideBlend * (3f - 2f * slideBlend);
				var lead = s == "R";
				var spot = lead ? new Vector3( 34f, -5.5f, 6.5f ) : new Vector3( -2f, 7.5f, 5.2f );
				var slideTarget = frame.PointToLocal( GameObject.WorldPosition + slideYaw * spot );
				ankleTarget = Vector3.Lerp( ankleTarget, slideTarget, ease );
				var slidePole = lead ? frame.Rotation.Inverse * (slideYaw * new Vector3( 0.4f, 0f, 1f )) : frame.Rotation.Inverse * (slideYaw * new Vector3( 0.6f, 0f, 1f ));
				kneePole = Vector3.Lerp( kneePole, slidePole * 30f, ease );
				footLift = lead ? 38f * ease : 0f;
			}
			if ( swimBlend > 0.001f )
			{
				// Flutter kick: the feet take turns, harder the faster it swims. Swimming forward they trail behind.
				// Backing off it treads water instead: the feet scissor forward and back under the hips, knees soft.
				// A strafe swings them the other way.
				// Legs hang long and loose from the hips (the citizen tucks its knees up in the air, which reads as sitting).
				var hanging = hip + Vector3.Down * (thighLen + shinLen) * 0.9f + Vector3.Left * (s == "L" ? 1.5f : -1.5f);
				ankleTarget = Vector3.Lerp( ankleTarget, hanging, swimBlend );
				kneePole = Vector3.Lerp( kneePole, Vector3.Forward * 30f, swimBlend );
				var kick = MathF.Sin( swimPhase + (s == "L" ? 0f : MathF.PI) );
				var effort = 0.5f + 0.5f * MathX.Clamp( new Vector2( swimMove.x, swimMove.y ).Length + MathF.Abs( swimMove.z ), 0f, 1f );
				var back = MathF.Max( 0f, -swimMove.x );
				var trail = Vector3.Backward * (3f + kick * 2f) * (1f - back) + Vector3.Forward * back * (1.5f + kick * 5f);
				var side = Vector3.Left * (-swimMove.y * 7f) + Vector3.Left * (s == "L" ? 1.5f : -1.5f) * kick * MathF.Abs( swimMove.y );
				ankleTarget += (Vector3.Up * (kick * 7f * effort * (1f - 0.7f * back) + 2.5f * back) + trail + side) * swimBlend;
			}
			if ( chargeBlend > 0.01f )
			{
				// Winding up: knees pulled up to the chest, coiled.
				var tuck = hip + Vector3.Down * 13f + Vector3.Forward * 11f + Vector3.Left * (s == "L" ? 2f : -2f);
				ankleTarget = Vector3.Lerp( ankleTarget, tuck, chargeBlend );
				kneePole = Vector3.Lerp( kneePole, Vector3.Forward * 30f + Vector3.Up * 20f, chargeBlend );
			}
			if ( jetBlend > 0.001f && chargeBlend < 0.5f )
			{
				// Jets on: legs straight down and close together, a slight tremble from the motors.
				var straight = hip + Vector3.Down * (thighLen + shinLen) * 0.97f + Vector3.Left * (s == "L" ? -1.2f : 1.2f);
				straight += Vector3.Forward * MathF.Sin( Time.Now * 40f + (s == "L" ? 0f : 1.7f) ) * 0.4f;
				ankleTarget = Vector3.Lerp( ankleTarget, straight, jetBlend );
				kneePole = Vector3.Lerp( kneePole, Vector3.Forward * 30f, jetBlend );
			}
			var knee = Bend( hip, ankleTarget, thighLen, shinLen, kneePole, out var ankle );
			var thighRot = Rotation.FromToRotation( RestDir( $"thigh_{s}", $"shin_{s}" ), (knee - hip).Normal );
			var shinRot = Rotation.FromToRotation( RestDir( $"shin_{s}", $"foot_{s}" ), (ankle - knee).Normal );
			Put( $"thigh_{s}", hip, thighRot );
			Put( $"shin_{s}", knee, shinRot );

			var toes = Cit( $"ball_{s}" ) - Cit( $"ankle_{s}" );
			var forward = toes.WithZ( toes.z * 0.6f ).Normal;
			var footRot = Rotation.LookAt( forward, Vector3.Up );
			if ( slideBlend > 0.001f )
			{
				// toes up for the leading heel, flat for the folded foot; in the (tilted) character space
				var slideForward = frame.Rotation.Inverse * (slideYaw * (Rotation.FromPitch( -footLift ) * Vector3.Forward));
				footRot = Rotation.Slerp( footRot, Rotation.LookAt( slideForward, frame.Rotation.Inverse * Vector3.Up ), slideBlend );
			}
			if ( jetBlend > 0.001f )
			{
				// Toes turn straight down and the boot folds away as the propeller unfolds.
				var down = Rotation.LookAt( Vector3.Down + Vector3.Forward * 0.05f, Vector3.Forward );
				footRot = Rotation.Slerp( footRot, down, jetBlend );
			}
			var bootScale = 1f - 0.65f * jetBlend;
			if ( bones.TryGetValue( $"foot_{s}", out var footBone ) )
				body.SetBoneTransform( footBone, new Transform( ankle, footRot * restRot[$"foot_{s}"], bootScale ) );
			PlaceProp( s, ankle + footRot * Vector3.Forward * 4f * bootScale, footRot );
		}
	}

	/// <summary> One foot's propeller: a hub and three blades on a spinner, facing down out of the sole. </summary>
	GameObject BuildProp( string side )
	{
		var root = new GameObject( body.GameObject, true, $"Prop {side}" );
		root.NetworkMode = NetworkMode.Never;
		var spinner = new GameObject( root, true, "Spinner" );
		void Piece( GameObject parent, Vector3 at, Vector3 size, Color color, bool round = false, float yaw = 0f )
		{
			var go = new GameObject( parent, true, "Prop Piece" );
			go.LocalPosition = at;
			go.LocalRotation = Rotation.FromYaw( yaw ) * Rotation.FromRoll( 18f );   // pitched blades
			go.LocalScale = round ? Vector3.One * Shapes.SphereScale( size.x ) : Shapes.BoxScale( size );
			var r = go.AddComponent<ModelRenderer>();
			r.Model = round ? Shapes.Sphere : Shapes.Box;
			r.Tint = color;
			r.RenderType = ModelRenderer.ShadowRenderType.Off;
		}
		var metal = new Color( 0.85f, 0.87f, 0.9f );
		var accent = new Color( 0.95f, 0.52f, 0.14f );
		Piece( root, Vector3.Zero, new Vector3( 3f, 0, 0 ), accent, true );        // hub cap
		for ( var i = 0; i < 3; i++ )
		{
			var yaw = i * 120f;
			var blade = Rotation.FromYaw( yaw ) * new Vector3( 5.2f, 0f, 0f );
			Piece( spinner, blade, new Vector3( 8.5f, 3f, 0.5f ), metal, false, yaw );
		}
		propSpinners[side] = spinner;
		return root;
	}

	void PlaceProp( string side, Vector3 tipLocal, Rotation footRot )
	{
		if ( jetBlend <= 0.01f )
		{
			if ( props.TryGetValue( side, out var hidden ) && hidden.IsValid() && hidden.Enabled ) hidden.Enabled = false;
			return;
		}
		if ( !props.TryGetValue( side, out var prop ) || !prop.IsValid() ) props[side] = prop = BuildProp( side );
		prop.Enabled = true;

		// Pops out with a little overshoot, spinning up as it opens; the disc faces along the foot (straight down).
		var open = jetBlend + 0.3f * MathF.Sin( jetBlend * MathF.PI );
		prop.WorldPosition = frame.PointToWorld( tipLocal );
		prop.WorldRotation = frame.Rotation * footRot * Rotation.FromPitch( 90f );
		prop.WorldScale = Vector3.One * open;
		if ( propSpinners.TryGetValue( side, out var spinner ) && spinner.IsValid() )
			spinner.LocalRotation = Rotation.FromYaw( propSpin * (side == "L" ? 1f : -1f) );

		// Churned water: a steady stream of small bubbles off each propeller.
		if ( jetBlend > 0.5f && timeSincePropBubble > 0.025f )
		{
			if ( side == "R" ) timeSincePropBubble = 0f;
			WaterBubble.Create( prop.WorldPosition + Vector3.Down * 3f + Game.Random.VectorInSphere( 2.5f ), Game.Random.Float( 0.35f, 0.9f ), 0f, Game.Random.Float( 0.6f, 1.2f ) );
		}
	}
}
