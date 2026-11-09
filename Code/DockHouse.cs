/// <summary>
/// The dock: the safe, dry start of the water level. A stilted dock house sits on the surface of the sea with a long
/// pier running out from it; at the end of the pier is a diving board. Walk out, press Use and you dive: a hop off the
/// board, then a torpedo straight down through the water to the seabed, where the waves begin.
///
/// Everything is built from code when the scene starts (one component in the scene, nothing else to set up).
/// While you're on the dock the waves wait, and the water feel (floaty swimming, muffled sound) is off above the surface.
/// </summary>
public sealed class DockHouse : Component
{
	/// <summary> Height of the sea's surface. The deck sits just above it. </summary>
	[Property] public float SurfaceHeight { get; set; } = 32000f;
	/// <summary> Where the pier meets the dock house (the house is behind this, the pier runs out along +X). </summary>
	[Property] public Vector3 Origin { get; set; } = new( -150f, -350f, 0f );
	[Property] public float PierLength { get; set; } = 460f;
	[Property] public float DiveSpeed { get; set; } = 1500f;
	/// <summary> Holding Duck dives faster, holding Jump slows it. </summary>
	[Property] public float DiveBoostSpeed { get; set; } = 2600f;
	[Property] public float DiveBrakeSpeed { get; set; } = 650f;
	/// <summary> Sideways steering speed during the dive. </summary>
	[Property] public float DiveSteerSpeed { get; set; } = 750f;
	/// <summary> Bloobers waiting in the water on the way down, to be crushed. </summary>
	[Property] public int DiveBloobers { get; set; } = 60;

	/// <summary> Bloobers burst on the way down this dive (the HUD counts them). </summary>
	public static int DiveCrushed { get; private set; }

	/// <summary> The sea surface height while a dock is in the level (null with no dock): the water feel only applies below it. </summary>
	public static float? SurfaceZ { get; private set; }
	/// <summary> A prompt for the HUD ("Press E to dive"), or null. </summary>
	public static string Prompt { get; private set; }
	/// <summary> True while the dive is happening (the HUD shows speed lines). </summary>
	public static bool Diving { get; private set; }
	/// <summary> The dive's current velocity (zero when not diving). </summary>
	public static Vector3 DiveVelocity { get; private set; }
	/// <summary> How far down the dive is, 0 at the surface to 1 at the seabed. </summary>
	public static float DiveProgress { get; private set; }
	/// <summary> Depth below the surface in metres. </summary>
	public static float DiveDepth { get; private set; }
	/// <summary> Seconds of clear water after going in before the Bloobers show up. </summary>
	[Property] public float BlooberDelay { get; set; } = 3.5f;

	float Deck => SurfaceHeight + 18f;
	Vector3 DivePoint => new( Origin.x + PierLength - 20f, Origin.y, Deck );
	Vector3 StartPoint => new( Origin.x - 140f, Origin.y, Deck + 4f );

	bool placed;
	bool dived;
	GameObject root;

	protected override void OnStart()
	{
		SurfaceZ = SurfaceHeight;
		Build();
		// The waves wait until someone dives.
		if ( !IsProxy && Director is { } d ) d.SpawningPaused = true;
	}

	protected override void OnDestroy()
	{
		SurfaceZ = null;
		Prompt = null;
		Diving = false;
	}

	WaveDirector Director => Scene.GetAllComponents<WaveDirector>().FirstOrDefault();
	PlayerSurvivor LocalPlayer => Scene.GetAllComponents<PlayerSurvivor>().FirstOrDefault( p => !p.IsProxy );

	// ---- building it ---------------------------------------------------------------------------------------------

	static readonly Color Wood = new( 0.55f, 0.37f, 0.21f );
	static readonly Color WoodDark = new( 0.36f, 0.23f, 0.13f );
	static readonly Color WoodPale = new( 0.68f, 0.5f, 0.32f );
	static readonly Color Roof = new( 0.62f, 0.2f, 0.16f );
	static readonly Color Rope = new( 0.82f, 0.72f, 0.5f );

	GameObject Box( string name, Vector3 center, Vector3 size, Color color, bool solid = false, Rotation? rotation = null )
	{
		var go = new GameObject( root, true, name );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = center;
		go.WorldRotation = rotation ?? Rotation.Identity;
		var visual = new GameObject( go, true, "Visual" );
		visual.LocalScale = Shapes.BoxScale( size );
		var r = visual.AddComponent<ModelRenderer>();
		r.Model = Shapes.Box;
		r.Tint = color;
		if ( solid )
		{
			var c = go.AddComponent<BoxCollider>();
			c.Scale = size;
		}
		return go;
	}

	void Build()
	{
		root = new GameObject( GameObject, true, "Dock" );
		var stilts = new List<Vector3>();
		root.NetworkMode = NetworkMode.Never;
		var o = Origin;
		var deck = Deck;

		// The sea's surface: a wide sheet of water, seen from above on the dock and from below on the seabed.
		var sea = Box( "Sea Surface", new Vector3( o.x, o.y, SurfaceHeight ), new Vector3( 9000f, 9000f, 2f ), new Color( 0.03f, 0.17f, 0.25f ) );
		// No shadow: a sheet this size would black out the whole seabed below.
		var seaMat = Material.Load( "materials/fx/fx_water.vmat" );
		foreach ( var r in sea.GetComponentsInChildren<ModelRenderer>() ) r.Enabled = false;
		var grid = new GameObject( root, true, "Sea Waves" );
		grid.NetworkMode = NetworkMode.Never;
		grid.WorldPosition = new Vector3( o.x, o.y, SurfaceHeight );
		var seaR = grid.AddComponent<ModelRenderer>();
		seaR.Model = WaterWaves.BuildGrid( seaMat, 9000f, 150 );
		seaR.RenderType = ModelRenderer.ShadowRenderType.Off;
		grid.AddComponent<WaterWaves>().Surface = SurfaceHeight;
		for ( var i = 0; i < 9; i++ )
		{
			var barrel = new GameObject( root, true, "Floating Barrel" );
			barrel.NetworkMode = NetworkMode.Never;
			barrel.WorldPosition = new Vector3( o.x + 120f + i * 70f, o.y + (i % 2 == 0 ? -1f : 1f) * (170f + i * 25f), SurfaceHeight + 20f );
			var bv = new GameObject( barrel, true, "Visual" );
			bv.LocalScale = Shapes.BoxScale( new Vector3( 26f, 26f, 34f ) );
			var br = bv.AddComponent<ModelRenderer>();
			br.Model = Shapes.Box;
			br.Tint = i % 3 == 0 ? Roof : WoodPale;
			barrel.AddComponent<BoxCollider>().Scale = new Vector3( 26f, 26f, 34f );
			barrel.AddComponent<Rigidbody>();
			barrel.AddComponent<WaveFloater>().Surface = SurfaceHeight;
		}

		// Pier: planks with gaps, a walkable slab under them, stilts down into the water, low rails.
		Box( "Pier Floor", new Vector3( o.x + PierLength * 0.5f, o.y, deck - 3f ), new Vector3( PierLength, 120f, 4f ), WoodDark, solid: true );
		for ( var x = 6f; x < PierLength; x += 16f )
		{
			var shade = ((int)(x / 16f)) % 3 == 0 ? WoodPale : Wood;
			Box( "Plank", new Vector3( o.x + x, o.y, deck ), new Vector3( 13f, 122f + (x % 7f), 3f ), shade );
		}
		for ( var x = 20f; x <= PierLength; x += 90f )
		{
			foreach ( var side in new[] { -1f, 1f } )
			{
				Box( "Stilt", new Vector3( o.x + x, o.y + side * 58f, deck - 70f ), new Vector3( 9f, 9f, 150f ), WoodDark ); stilts.Add( new Vector3( o.x + x, o.y + side * 58f, 0f ) );
				if ( x < PierLength - 60f )
					Box( "Rail Post", new Vector3( o.x + x, o.y + side * 58f, deck + 18f ), new Vector3( 5f, 5f, 34f ), Wood );
			}
		}
		foreach ( var side in new[] { -1f, 1f } )
			Box( "Rail", new Vector3( o.x + (PierLength - 60f) * 0.5f + 10f, o.y + side * 58f, deck + 34f ), new Vector3( PierLength - 60f, 4f, 4f ), WoodPale, solid: true );

		// The diving board at the end, a rope coil and a lamp.
		Box( "Diving Board", new Vector3( o.x + PierLength + 22f, o.y, deck + 4f ), new Vector3( 70f, 34f, 3f ), new Color( 0.92f, 0.9f, 0.84f ), solid: true, rotation: Rotation.FromPitch( -4f ) );
		Box( "Board Stand", new Vector3( o.x + PierLength - 4f, o.y, deck + 1f ), new Vector3( 14f, 30f, 6f ), Roof );
		Box( "Rope Coil", new Vector3( o.x + PierLength - 40f, o.y + 40f, deck + 4f ), new Vector3( 18f, 18f, 5f ), Rope );
		var lampPost = Box( "Lamp Post", new Vector3( o.x + PierLength - 30f, o.y - 50f, deck + 40f ), new Vector3( 5f, 5f, 80f ), WoodDark );
		Box( "Lamp", new Vector3( o.x + PierLength - 30f, o.y - 50f, deck + 82f ), new Vector3( 10f, 10f, 10f ), new Color( 1f, 0.85f, 0.5f ) * 2f );
		var light = new GameObject( root, true, "Lamp Light" );
		light.WorldPosition = new Vector3( o.x + PierLength - 30f, o.y - 50f, deck + 80f );
		var pl = light.AddComponent<PointLight>();
		pl.LightColor = new Color( 1f, 0.75f, 0.45f ) * 2.5f;
		pl.Radius = 500f;

		// Dock house platform behind the pier, on its own stilts.
		var hx = o.x - 150f;
		Box( "House Deck", new Vector3( hx, o.y, deck - 3f ), new Vector3( 300f, 280f, 6f ), WoodDark, solid: true );
		for ( var x = -140f; x <= 140f; x += 70f )
			foreach ( var y in new[] { -130f, 130f } )
				{ Box( "Stilt", new Vector3( hx + x, o.y + y, deck - 70f ), new Vector3( 11f, 11f, 150f ), WoodDark ); stilts.Add( new Vector3( hx + x, o.y + y, 0f ) ); }

		// The house: three walls and a front with a doorway facing the pier, a pitched roof, a window.
		var wallH = 110f;
		var hz = deck + wallH * 0.5f;
		Box( "Back Wall", new Vector3( hx - 110f, o.y, hz ), new Vector3( 8f, 200f, wallH ), Wood, solid: true );
		Box( "Side Wall", new Vector3( hx, o.y - 100f, hz ), new Vector3( 220f, 8f, wallH ), Wood, solid: true );
		Box( "Side Wall", new Vector3( hx, o.y + 100f, hz ), new Vector3( 220f, 8f, wallH ), Wood, solid: true );
		Box( "Front Wall", new Vector3( hx + 110f, o.y - 65f, hz ), new Vector3( 8f, 70f, wallH ), Wood, solid: true );
		Box( "Front Wall", new Vector3( hx + 110f, o.y + 65f, hz ), new Vector3( 8f, 70f, wallH ), Wood, solid: true );
		Box( "Lintel", new Vector3( hx + 110f, o.y, deck + wallH - 12f ), new Vector3( 8f, 60f, 24f ), Wood, solid: true );
		Box( "Window", new Vector3( hx, o.y - 104f, hz + 10f ), new Vector3( 50f, 2f, 30f ), new Color( 0.5f, 0.75f, 0.9f ) );
		foreach ( var side in new[] { -1f, 1f } )
			Box( "Roof", new Vector3( hx, o.y + side * 58f, deck + wallH + 26f ), new Vector3( 250f, 132f, 7f ), Roof, rotation: Rotation.FromRoll( -side * 24f ) );
		// Swell and foam on the water.
		var fx = root.AddComponent<WaterSurfaceFx>();
		fx.Height = SurfaceHeight;
		fx.Center = new Vector3( o.x, o.y, SurfaceHeight );
		fx.Stilts = stilts;

		// Inside: a table, a crate and a warm light.
		Box( "Table", new Vector3( hx - 60f, o.y + 50f, deck + 24f ), new Vector3( 50f, 34f, 4f ), WoodPale, solid: true );
		Box( "Crate", new Vector3( hx - 70f, o.y - 60f, deck + 16f ), new Vector3( 30f, 30f, 30f ), WoodDark, solid: true );
		var inside = new GameObject( root, true, "House Light" );
		inside.WorldPosition = new Vector3( hx - 20f, o.y, deck + 90f );
		var il = inside.AddComponent<PointLight>();
		il.LightColor = new Color( 1f, 0.75f, 0.5f ) * 0.8f;
		il.Radius = 200f;
	}

	// ---- the player on the dock, and the dive -------------------------------------------------------------------

	enum Phase { None, Hop, Torpedo, Land }
	Phase phase;
	float phaseTime;
	Vector3 hopFrom;
	float diveSpeed;
	bool splashed;
	float pose;
	float tumbleFor;
	Vector3 tumbleSpin;
	Rotation tumble = Rotation.Identity;
	Vector3 landDrift;
	Vector3 landEnd;
	/// <summary> Extra camera rotation while tumbling (CameraJuice applies it). </summary>
	public static Rotation CameraTumble { get; private set; } = Rotation.Identity;
	/// <summary> Bloobers knocked aside this dive. </summary>
	public static int DiveKnocked { get; private set; }
	bool bloobersSpawned;
	Vector3 diveVelocity;
	float landPitch = 70f;
	TimeSince timeSinceBubble;

	protected override void OnUpdate()
	{
		if ( hitStopUntil > 0 && RealTime.Now >= hitStopUntil ) { Scene.TimeScale = 1f; hitStopUntil = 0; }
		var player = LocalPlayer;
		if ( player is null ) return;
		var controller = player.GetComponent<PlayerController>();
		if ( !controller.IsValid() ) return;

		// Start everyone on the dock (once).
		if ( !placed && !dived )
		{
			placed = true;
			player.WorldPosition = StartPoint;
			controller.EyeAngles = new Angles( 0f, 0f, 0f );
			if ( controller.Body.IsValid() ) controller.Body.Velocity = Vector3.Zero;
		}

		if ( phase != Phase.None ) { UpdateDive( player, controller ); return; }

		// No button: jump (or walk) off the dock and the moment you go under, you're diving.
		Prompt = null;
		if ( !dived && player.IsAlive && player.WorldPosition.z < SurfaceHeight - 6f ) StartDive( player, controller );
	}

	/// <summary> Testing aid: dive from wherever the player is (scripts can't press keys). </summary>
	public void TestDive()
	{
		var player = LocalPlayer;
		if ( player is null ) return;
		// Just past the diving board, in the air: it falls in by itself.
		player.WorldPosition = DivePoint + Vector3.Forward * 90f + Vector3.Up * 40f;
		var controller = player.GetComponent<PlayerController>();
		if ( controller.Body.IsValid() ) controller.Body.Velocity = Vector3.Forward * 150f;
	}

	void StartDive( PlayerSurvivor player, PlayerController controller )
	{
		// Straight from however you went in: same speed, same look; it just takes over and pulls you down.
		var velocity = controller.Body.IsValid() ? controller.Body.Velocity : Vector3.Down * 300f;
		phase = Phase.Torpedo;
		phaseTime = 0f;
		splashed = false;
		Diving = true;
		Prompt = null;
		diveSpeed = MathF.Max( 300f, -velocity.z );
		diveVelocity = velocity.WithZ( -diveSpeed );
		DiveCrushed = 0;
		WaterSurfaceFx.Splash( player.WorldPosition.WithZ( SurfaceHeight ), velocity.Length );
		controller.UseInputControls = false;
		controller.UseLookControls = true;
		if ( controller.Body.IsValid() ) { controller.Body.Velocity = Vector3.Zero; controller.Body.Gravity = false; }
		player.GetComponent<Jetpack>()?.BeginDiveRam();
		BeginDiveFx( player );
	}

	void UpdateDive( PlayerSurvivor player, PlayerController controller )
	{
		var dt = Time.Delta;
		phaseTime += dt;
		var body = controller.Body;
		if ( body.IsValid() ) body.Velocity = Vector3.Zero;
		var rig = player.GetComponent<PlayerRig>();

		switch ( phase )
		{
			case Phase.Hop:
			{
				// A spring off the board: out and up, tipping head-first toward the water.
				var t = MathX.Clamp( phaseTime / 0.55f, 0f, 1f );
				var arc = new Vector3( 70f * t, 0f, 46f * MathF.Sin( t * MathF.PI ) - 30f * t * t );
				player.WorldPosition = hopFrom + arc;
				var pitch = MathX.Lerp( 0f, 88f, t * t );
				controller.EyeAngles = new Angles( pitch, controller.EyeAngles.yaw, 0f );
				if ( rig.IsValid() ) { rig.SurgeOn = t > 0.4f; rig.SurgeDir = Vector3.Down; rig.JetOn = t > 0.4f; }
				if ( t >= 1f )
				{
					phase = Phase.Torpedo;
					phaseTime = 0f;
					diveSpeed = 500f;
					diveVelocity = Vector3.Down * 500f;
					DiveCrushed = 0;
					// Free to look around now; steering is WASD, Duck to dive harder, Jump to brake.
					controller.UseLookControls = true;
					player.GetComponent<Jetpack>()?.BeginDiveRam();
					BeginDiveFx( player );
				}
				break;
			}

			case Phase.Torpedo:
			{
				// A falling body: weight pulls you down, water drag grows with the square of speed, and your pose sets
				// the drag. Tuck (Duck) to cut through, spread out (Jump) to catch the water; tilting turns fall into glide.
				var tumbling = tumbleFor > 0f;
				var wantPose = tumbling ? -0.4f : Input.Down( "Duck" ) ? 1f : Input.Down( "Jump" ) ? -1f : 0f;
				pose = MathX.Approach( pose, wantPose, 2.5f * dt );
				var terminal = pose >= 0f ? MathX.Lerp( DiveSpeed, DiveBoostSpeed, pose ) : MathX.Lerp( DiveSpeed, DiveBrakeSpeed, -pose );
				const float weight = 900f;
				var drag = weight / (terminal * terminal);
				var speedNow = diveVelocity.Length;
				var accel = Vector3.Down * weight - diveVelocity * (speedNow * drag);
				if ( !tumbling )
				{
					var yawRot = Rotation.FromYaw( controller.EyeAngles.yaw );
					var steer = yawRot.Forward * Input.AnalogMove.x + yawRot.Left * Input.AnalogMove.y;
					accel += steer * (speedNow * 0.28f + 120f);
				}
				diveVelocity += accel * dt;
				diveSpeed = diveVelocity.Length;
				var pos = player.WorldPosition + diveVelocity * dt;

				// Tumbling: spin that dies away, then the body rights itself.
				if ( tumbling )
				{
					tumbleFor -= dt;
					tumbleSpin *= MathF.Exp( -1.4f * dt );
					tumble = Rotation.From( tumbleSpin.x * dt, tumbleSpin.y * dt, tumbleSpin.z * dt ) * tumble;
				}
				else tumble = Rotation.Slerp( tumble, Rotation.Identity, 1f - MathF.Exp( -5f * dt ) );
				CameraTumble = tumble;
				if ( rig.IsValid() ) { rig.SurgeOn = true; rig.JetOn = !tumbling; rig.SurgeDir = tumble * diveVelocity.Normal; }

				// Rocks and walls in the way: bounce off and tumble.
				if ( !tumbling && speedNow > 300f )
				{
					var hit = Scene.Trace.Sphere( 16f, player.WorldPosition + Vector3.Up * 35f, pos + Vector3.Up * 35f )
						.WithoutTags( "player", "trigger", "creature", "gib", "gore", "viewmodel" ).IgnoreGameObjectHierarchy( player.GameObject ).Run();
					if ( hit.Hit && !hit.StartedSolid && hit.Normal.z < 0.6f )
					{
						var n = hit.Normal;
						diveVelocity = Vector3.Reflect( diveVelocity, n ) * 0.4f + n * 200f;
						pos = player.WorldPosition + n * 4f;
						Tumble( player, 1.1f, 1f );
						Jetpack.SlamBubbles( hit.HitPosition, n, 0.6f );
					}
				}

				DiveVelocity = diveVelocity;
				DiveDepth = MathF.Max( 0f, SurfaceHeight - pos.z ) / 39.37f;
				DiveProgress = MathX.Clamp( (SurfaceHeight - pos.z) / MathF.Max( 1f, SurfaceHeight ), 0f, 1f );
				if ( !bloobersSpawned && phaseTime > BlooberDelay )
				{
					bloobersSpawned = true;
					SpawnDiveBloobers( pos );
				}

				var jet = player.GetComponent<Jetpack>();
				var glanced = false;
				var crushed = jet is null ? null : jet.DiveRam( diveVelocity, out glanced );
				if ( crushed.IsValid() && crushed is Bloober )
				{
					DiveCrushed++;
				}

				if ( !splashed && pos.z < SurfaceHeight )
				{
					// Through the surface: a big burst of bubbles and a jolt.
					splashed = true;
					for ( var i = 0; i < 40; i++ )
						WaterBubble.Create( new Vector3( pos.x, pos.y, SurfaceHeight - 20f ) + Game.Random.VectorInSphere( 60f ), Game.Random.Float( 1f, 3f ), i * 0.004f );
					player.GetComponent<CameraJuice>()?.Shake( 0.5f );
					player.GetComponent<CameraJuice>()?.KickFov( 18f );
				}
				if ( splashed && timeSinceBubble > 0.02f )
				{
					timeSinceBubble = 0f;
					for ( var k = 0; k < 3; k++ )
						WaterBubble.Create( pos + Vector3.Up * Game.Random.Float( 40f, 120f ) + Game.Random.VectorInSphere( 18f ), Game.Random.Float( 0.6f, 2f ), 0f, Game.Random.Float( 0.6f, 1.2f ) );
				}

				// Pull out of it just above the seabed.
				// Hitting the seabed: full speed, straight in.
				var ground = Scene.Trace.Ray( player.WorldPosition + Vector3.Up * 10f, pos + Vector3.Down * 20f ).WithoutTags( "player", "trigger", "creature", "gib" ).IgnoreGameObjectHierarchy( player.GameObject ).Run();
				if ( ground.Hit )
				{
					pos = ground.HitPosition + Vector3.Up * 2f;
					player.WorldPosition = pos;
						Impact( player, pos, diveVelocity.Length );
						player.GetComponent<DiveRush>()?.Destroy();
						DiveVelocity = Vector3.Zero;
					landPitch = MathF.Max( controller.EyeAngles.pitch, 40f );
					phase = Phase.Land;
					phaseTime = 0f;
					hopFrom = pos;
					// The body keeps going: bounced up out of the crater, cartwheeling, landing a little way off.
					var flat = diveVelocity.WithZ( 0 );
					landDrift = (flat.Length > 50f ? flat.Normal : Rotation.FromYaw( controller.EyeAngles.yaw ).Forward) * MathX.Clamp( 140f + flat.Length * 0.15f, 140f, 320f );
					var endTrace = Scene.Trace.Ray( pos + landDrift + Vector3.Up * 300f, pos + landDrift + Vector3.Down * 600f ).WithoutTags( "player", "trigger", "creature", "gib" ).IgnoreGameObjectHierarchy( player.GameObject ).Run();
					landEnd = endTrace.Hit ? endTrace.HitPosition + Vector3.Up * 2f : pos + landDrift;
					tumbleSpin = new Vector3( Game.Random.Float( 500f, 800f ) * (Game.Random.Float() < 0.5f ? -1f : 1f ), 0f, Game.Random.Float( -300f, 300f ) );
					break;
				}
				player.WorldPosition = pos;
				break;
			}

			case Phase.Land:
			{
				// Ragdolled by the impact: thrown up and out of the crater, cartwheeling, a skid on landing, a moment
				// sprawled, then back up looking out over the seabed.
				const float fly = 0.85f;
				if ( phaseTime < fly )
				{
					var k = phaseTime / fly;
					var arc = MathF.Sin( k * MathF.PI ) * 150f;
					player.WorldPosition = Vector3.Lerp( hopFrom, landEnd, k ) + Vector3.Up * arc;
					var spinLeft = 1f - k;
					tumble = Rotation.From( tumbleSpin.x * dt * spinLeft, tumbleSpin.y * dt * spinLeft, tumbleSpin.z * dt * spinLeft ) * tumble;
					if ( rig.IsValid() ) { rig.SurgeOn = true; rig.JetOn = false; rig.SurgeDir = tumble * Vector3.Down; }
				}
				else
				{
					var k = MathX.Clamp( (phaseTime - fly) / 0.9f, 0f, 1f );
					var ease = 1f - (1f - k) * (1f - k);
					player.WorldPosition = landEnd;
					tumble = Rotation.Slerp( tumble, Rotation.Identity, 1f - MathF.Exp( -6f * dt ) );
					controller.EyeAngles = new Angles( MathX.Lerp( landPitch, 5f, ease ), controller.EyeAngles.yaw, 0f );
					if ( rig.IsValid() ) { rig.SurgeOn = k < 0.3f; rig.JetOn = false; rig.SurgeDir = tumble * Vector3.Down; }
					if ( phaseTime - fly < dt * 1.5f )
					{
						// Hitting the ground again: a smaller puff and a thump.
						player.GetComponent<CameraJuice>()?.Shake( 0.45f );
						for ( var i = 0; i < 20; i++ )
							WaterBubble.Create( landEnd + Game.Random.VectorInSphere( 30f ).WithZ( 0 ) + Vector3.Up * 5f, Game.Random.Float( 0.8f, 2f ), 0f, 1.2f );
					}
					if ( k >= 1f ) FinishDive( player, controller );
				}
				CameraTumble = tumble;
				break;
			}
		}
	}

	/// <summary>
	/// The landing: a crater punched into the seabed, rocks and sand flung everywhere (real physics, they bounce and
	/// settle), a shockwave through anything close, and a huge jolt.
	/// </summary>
	void Impact( PlayerSurvivor player, Vector3 at, float speed )
	{
		var power = MathX.Clamp( speed / DiveBoostSpeed, 0.4f, 1f );
		var radius = 170f + 100f * power;

		// The crater: a real bowl and rim that hugs the seabed, with a hot core that cools.
		DiveCrater.Build( Scene, at, power );

		// Debris: a fountain of rock and sand. Each piece flies its own arc (water drag, slow sink), lands, tumbles, settles.
		var chunks = (int)(70 + 40 * power);
		for ( var i = 0; i < chunks; i++ )
		{
			var a = Game.Random.Float( 0f, MathF.PI * 2f );
			var outward = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f );
			var go = new GameObject( true, "Crater Debris" );
			go.NetworkMode = NetworkMode.Never;
			go.Tags.Add( "gib" );
			go.WorldPosition = at + outward * Game.Random.Float( 5f, radius * 0.5f ) + Vector3.Up * Game.Random.Float( 6f, 30f );
			go.WorldRotation = Rotation.Random;
			var big = i % 6 == 0;
			var size = (big ? Game.Random.Float( 14f, 28f ) : Game.Random.Float( 4f, 12f )) * (0.7f + 0.5f * power);
			go.WorldScale = Shapes.BoxScale( new Vector3( size, size * Game.Random.Float( 0.6f, 1.3f ), size * Game.Random.Float( 0.5f, 1f ) ) );
			var mr = go.AddComponent<ModelRenderer>();
			mr.Model = big ? DiveCrater.Boulder() : Shapes.Box;
			if ( big ) go.WorldScale = Vector3.One * (size / 110f);
			mr.Tint = big ? Color.Lerp( new Color( 0.9f, 0.85f, 0.75f ), new Color( 0.55f, 0.5f, 0.45f ), Game.Random.Float() ) : Color.Lerp( new Color( 0.62f, 0.55f, 0.43f ), new Color( 0.32f, 0.29f, 0.25f ), Game.Random.Float() );
			var chunk = go.AddComponent<DebrisChunk>();
			chunk.Velocity = outward * Game.Random.Float( 150f, 650f ) * power + Vector3.Up * Game.Random.Float( 900f, 1900f ) * (big ? 0.7f : 1f) * power;
			chunk.Spin = Vector3.Random * Game.Random.Float( 5f, 16f );
			chunk.Size = size;
			chunk.Trail = big;
			go.AddComponent<SelfDestruct>().Lifetime = Game.Random.Float( 9f, 14f );
		}

		// The blast cloud: a ring of sand racing out, a column of sand boiling up, and slow drifting dust.
		var cloud = new GameObject( true, "Crater Cloud" );
		cloud.NetworkMode = NetworkMode.Never;
		cloud.WorldPosition = at;
		cloud.AddComponent<SelfDestruct>().Lifetime = 7f;
		var cf = cloud.AddComponent<SandCloud>();
		cf.Radius = radius;
		cf.Power = power;

		// A wall of sand and bubbles.
		for ( var i = 0; i < 60; i++ )
		{
			var a = Game.Random.Float( 0f, MathF.PI * 2f );
			var p = at + new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f ) * Game.Random.Float( 0f, radius * 1.2f ) + Vector3.Up * Game.Random.Float( 0f, 60f );
			WaterBubble.Create( p, Game.Random.Float( 1f, 3.2f ), Game.Random.Float( 0f, 0.15f ), Game.Random.Float( 1f, 2.2f ) );
		}

		// The shockwave: everything near the landing is hit hard, the nearest burst.
		var survivor = player;
		foreach ( var part in Scene.GetAllComponents<CreaturePart>().Where( p => p.IsValid() && p.Creature.IsValid() && p.GameObject.IsValid() ).ToList() )
		{
			if ( !part.IsValid() || !part.GameObject.IsValid() || part.Creature is SeaSpider { IsAlly: true } ) continue;
			var d = part.GameObject.WorldPosition.Distance( at );
			if ( d > radius * 2.2f ) continue;
			var falloff = 1f - d / (radius * 2.2f);
			part.ReceiveShot( 160f * falloff * power, part.GameObject, (part.GameObject.WorldPosition - at).Normal, part.GameObject.WorldPosition, at, survivor, false );
		}
		foreach ( var zombie in Scene.GetAllComponents<Zombie>().Where( z => !z.IsDead ).ToList() )
		{
			var d = zombie.WorldPosition.Distance( at );
			if ( d > radius * 2.2f ) continue;
			zombie.Components.Get<Health>()?.OnDamage( new DamageInfo { Damage = 200f * (1f - d / (radius * 2.2f)) * power, Attacker = player.GameObject, Position = zombie.WorldPosition, Origin = at } );
		}

		player.GetComponent<CameraJuice>()?.Shake( 1f );
		player.GetComponent<CameraJuice>()?.KickFov( -22f );
		if ( !Networking.IsActive ) { Scene.TimeScale = 0.1f; hitStopUntil = RealTime.Now + 0.14; }
	}

	double hitStopUntil;

	/// <summary> A column of Bloobers hanging in the water below the dive, spread out so you have to steer for them. </summary>
	void SpawnDiveBloobers( Vector3 from )
	{
		var director = Director;
		if ( director is null || IsProxy ) return;
		var bottom = 250f;
		var top = MathF.Min( SurfaceHeight - 400f, from.z - 1500f );
		for ( var i = 0; i < DiveBloobers; i++ )
		{
			var t = (i + 0.5f) / DiveBloobers;
			var height = MathX.Lerp( top, bottom, t );
			var ring = Game.Random.Float( 0f, MathF.PI * 2f );
			var radius = Game.Random.Float( 30f, 260f );
			var at = new Vector3( from.x + MathF.Cos( ring ) * radius, from.y + MathF.Sin( ring ) * radius, height );
			director.SpawnAt( WaveDirector.EnemyKind.Bloober, at, from );
		}
	}

	/// <summary> Knocked into a tumble: no control for a moment, spinning, the camera going with it. </summary>
	void Tumble( PlayerSurvivor player, float seconds, float strength )
	{
		tumbleFor = seconds;
		tumbleSpin = Vector3.Random * 900f * strength;
		player.GetComponent<CameraJuice>()?.Shake( 0.5f * strength );
	}

	void BeginDiveFx( PlayerSurvivor player )
	{
		bloobersSpawned = false;
		DiveKnocked = 0;
		pose = 0f;
		tumbleFor = 0f;
		tumble = Rotation.Identity;
		if ( !player.GetComponent<DiveHands>().IsValid() ) player.AddComponent<DiveHands>();
		if ( !player.GetComponent<DiveRush>().IsValid() ) player.AddComponent<DiveRush>();
	}

	static void EndDiveFx( PlayerSurvivor player )
	{
		player.GetComponent<DiveHands>()?.Destroy();
		player.GetComponent<DiveRush>()?.Destroy();
		DiveVelocity = Vector3.Zero;
	}

	void FinishDive( PlayerSurvivor player, PlayerController controller )
	{
		EndDiveFx( player );
		CameraTumble = Rotation.Identity;
		tumble = Rotation.Identity;
		phase = Phase.None;
		dived = true;
		Diving = false;
		controller.UseInputControls = true;
		controller.UseLookControls = true;
		if ( controller.Body.IsValid() ) controller.Body.Gravity = true;
		var rig = player.GetComponent<PlayerRig>();
		if ( rig.IsValid() ) { rig.SurgeOn = false; rig.JetOn = false; }
		for ( var i = 0; i < 24; i++ )
			WaterBubble.Create( player.WorldPosition + Vector3.Up * 6f + Game.Random.VectorInSphere( 40f ).WithZ( 0 ), Game.Random.Float( 0.8f, 2.2f ), i * 0.01f );
		player.GetComponent<CameraJuice>()?.Shake( 0.35f );
		// The waves start.
		if ( Director is { } d ) d.SpawningPaused = false;
	}
}

/// <summary> One piece of crater debris: flies its own arc through the water, lands on the seabed, tumbles and settles. </summary>
public sealed class DebrisChunk : Component
{
	public Vector3 Velocity { get; set; }
	public Vector3 Spin { get; set; }
	public float Size { get; set; } = 8f;
	public bool Trail { get; set; }
	bool landed;
	TimeSince lastBubble;

	protected override void OnUpdate()
	{
		if ( landed ) return;
		var dt = Time.Delta;
		// Heavy but in water: gravity is weak and drag is real, so pieces hang in the air a moment before sinking.
		Velocity += Vector3.Down * 520f * dt;
		Velocity *= MathF.Exp( -0.7f * dt );
		var from = WorldPosition;
		var to = from + Velocity * dt;
		if ( Velocity.z < 0f )
		{
			var tr = Scene.Trace.Ray( from, to + Vector3.Down * Size * 0.4f ).WithoutTags( "player", "trigger", "creature", "gib" ).Run();
			if ( tr.Hit )
			{
				// Bounce once, small, then lie down.
				if ( Velocity.Length > 160f && Size > 7f )
				{
					WorldPosition = tr.HitPosition + Vector3.Up * Size * 0.4f;
					Velocity = Velocity.WithZ( -Velocity.z * 0.3f ) * 0.45f;
					Spin *= 0.5f;
					return;
				}
				WorldPosition = tr.HitPosition + Vector3.Up * Size * 0.3f;
				landed = true;
				return;
			}
		}
		WorldPosition = to;
		WorldRotation = Rotation.From( Spin.x * dt * 57f, Spin.y * dt * 57f, Spin.z * dt * 57f ) * WorldRotation;
		if ( Trail && lastBubble > 0.06f )
		{
			lastBubble = 0f;
			WaterBubble.Create( WorldPosition, Game.Random.Float( 1f, 2.4f ), 0f, 1.2f );
		}
	}
}

/// <summary> The sand thrown up by the landing: an expanding ground ring, a rising boil, and a slow haze that settles. </summary>
public sealed class SandCloud : Component
{
	public float Radius { get; set; } = 150f;
	public float Power { get; set; } = 1f;
	readonly List<(GameObject Go, Vector3 Vel, float Size, float Life)> puffs = new();
	GameObject ring;
	TimeSince age;

	protected override void OnStart()
	{
		age = 0f;
		var sand = new Color( 0.62f, 0.54f, 0.4f );
		var count = (int)(300 * Power) + 80;
		for ( var i = 0; i < count; i++ )
		{
			var a = Game.Random.Float( 0f, MathF.PI * 2f );
			var outward = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f );
			var rising = i % 2 == 0;
			var go = new GameObject( GameObject, true, "Puff" );
			go.NetworkMode = NetworkMode.Never;
			go.LocalPosition = outward * Game.Random.Float( 0f, Radius * (rising ? 0.5f : 1f) ) + Vector3.Up * Game.Random.Float( 4f, 30f );
			var r = go.AddComponent<ModelRenderer>();
			r.Model = Shapes.Sphere;
			r.Tint = Color.Lerp( sand, new Color( 0.42f, 0.36f, 0.28f ), Game.Random.Float() );
			r.RenderType = ModelRenderer.ShadowRenderType.Off;
			var vel = rising
				? outward * Game.Random.Float( 20f, 140f ) + Vector3.Up * Game.Random.Float( 180f, 520f ) * Power
				: outward * Game.Random.Float( 260f, 640f ) * Power + Vector3.Up * Game.Random.Float( 20f, 90f );
			puffs.Add( (go, vel, Game.Random.Float( 4f, 12f ) * (0.7f + 0.5f * Power), Game.Random.Float( 1.4f, 3.2f )) );
		}
		ring = new GameObject( GameObject, true, "Shock Ring" );
		ring.NetworkMode = NetworkMode.Never;
		var rr = ring.AddComponent<ModelRenderer>();
		rr.Model = Shapes.Sphere;
		rr.Tint = new Color( 0.8f, 0.72f, 0.55f );
		rr.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	protected override void OnUpdate()
	{
		var dt = Time.Delta;
		var t = (float)age;
		for ( var i = 0; i < puffs.Count; i++ )
		{
			var (go, vel, size, life) = puffs[i];
			if ( !go.IsValid() ) continue;
			vel *= MathF.Exp( -1.6f * dt );
			vel += Vector3.Up * 22f * dt;
			go.WorldPosition += vel * dt;
			puffs[i] = (go, vel, size, life);
			// Swell as they thin out, then fade to nothing.
			var k = MathX.Clamp( t / life, 0f, 1f );
			var grow = 1f + k * 0.5f;
			var fade = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
			go.WorldScale = Vector3.One * Shapes.SphereScale( size * grow * MathX.Clamp( fade, 0f, 1f ) );
		}
		if ( ring.IsValid() )
		{
			var spread = 1f - MathF.Exp( -4.5f * t );
			var d = Radius * 0.6f + Radius * 5.5f * spread * Power;
			var fade = MathX.Clamp( 1f - t / 0.9f, 0f, 1f );
			ring.WorldPosition = WorldPosition + Vector3.Up * 6f;
			ring.WorldScale = new Vector3( d, d, 14f * fade + 0.01f ) * Shapes.SphereScale( 1f );
		}
	}
}

/// <summary> The sea's swell: one function the water shader and the game share, plus the grid mesh the shader bends. </summary>
public sealed class WaterWaves : Component
{
	public float Surface { get; set; }

	/// <summary> Wave height above the resting surface at a world position (matches SwellHeight in fx_water.shader). </summary>
	public static float Height( float x, float y, float t )
	{
		var h = MathF.Sin( (x * 0.80f + y * 0.60f) * 0.0060f + t * 0.55f ) * 1.00f
			+ MathF.Sin( (x * -0.50f + y * 0.87f) * 0.0110f + t * 0.80f ) * 0.65f
			+ MathF.Sin( (x * 0.30f + y * -0.95f) * 0.0240f + t * 1.20f ) * 0.38f;
		return h * 11f;
	}

	public static Model BuildGrid( Material material, float size, int cells )
	{
		var verts = new List<Vertex>();
		var indices = new List<int>();
		for ( var j = 0; j <= cells; j++ )
			for ( var i = 0; i <= cells; i++ )
			{
				var p = new Vector3( (i / (float)cells - 0.5f) * size, (j / (float)cells - 0.5f) * size, 0f );
				verts.Add( new Vertex( p, Vector3.Up, Vector3.Forward, new Vector4( i / (float)cells, j / (float)cells, 0, 0 ) ) );
			}
		for ( var j = 0; j < cells; j++ )
			for ( var i = 0; i < cells; i++ )
			{
				var a = j * (cells + 1) + i;
				var b = a + cells + 1;
				indices.AddRange( new[] { a, a + 1, b, b, a + 1, b + 1 } );
			}
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( verts.Count, verts );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.Bounds = new BBox( new Vector3( -size * 0.5f, -size * 0.5f, -60f ), new Vector3( size * 0.5f, size * 0.5f, 60f ) );
		return Model.Builder.AddMesh( mesh ).Create();
	}

	protected override void OnUpdate()
	{
		var r = GetComponent<ModelRenderer>();
		if ( r.IsValid() ) r.Attributes.Set( "WaveTime", Time.Now );
	}
}

/// <summary> Buoyancy: pushes a body up when it is under the moving wave surface, and drags it with the swell. </summary>
public sealed class WaveFloater : Component
{
	public float Surface { get; set; }

	protected override void OnFixedUpdate()
	{
		var rb = GetComponent<Rigidbody>();
		if ( !rb.IsValid() ) return;
		var p = WorldPosition;
		var wave = Surface + WaterWaves.Height( p.x, p.y, Time.Now );
		var depth = wave - p.z;
		if ( depth > -10f )
		{
			var push = MathX.Clamp( depth, -10f, 40f );
			rb.Velocity += Vector3.Up * (push * 26f + 980f * (depth > 0 ? 0.9f : 0f)) * Time.Delta;
			rb.Velocity *= MathF.Exp( -1.2f * Time.Delta );
			rb.AngularVelocity *= MathF.Exp( -1.5f * Time.Delta );
			// Slowly carried along by the swell.
			rb.Velocity += new Vector3( 0.8f, 0.6f, 0f ) * 20f * Time.Delta;
		}
	}
}
