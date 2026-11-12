using Godot;

/// <summary>
/// Port of the s&box DockHouse: the dock, the safe dry start of the water level. A stilted dock house sits on the
/// surface of the sea (far above the seabed) with a long pier and a diving board. Jump or walk off and the moment you
/// go under you're diving: a long physics fall (weight, drag that grows with speed, tuck to cut through, spread out to
/// brake, tilt to glide), steering past a column of Bloobers to crush, tumbling off rocks, then a huge landing that
/// punches a crater into the seabed and throws you cartwheeling out of it. Then the waves start.
/// Positions are the s&box ones in inches (x forward, y left, z up), converted with U.Sbox.
/// </summary>
public partial class DockHouse : Node3D
{
	public float SurfaceHeight = 32000f;
	public Vector3 Origin = new( -150f, -350f, 0f );
	public float PierLength = 460f;
	public float DiveSpeed = 1500f;
	public float DiveBoostSpeed = 2600f;
	public float DiveBrakeSpeed = 650f;
	public float DiveSteerSpeed = 750f;
	public int DiveBloobers = 60;
	/// <summary> The old free-fall dive (off for now): the diving board takes you straight to the sea floor instead. </summary>
	public bool DiveSequence = false;
	/// <summary> How close (s&box units) you must be to the end of the diving board to use it. </summary>
	public float BoardReach = 110f;
	public float BlooberDelay = 3.5f;

	public static int DiveCrushed { get; private set; }
	public static int DiveKnocked { get; private set; }
	public static bool Diving { get; private set; }
	/// <summary> The dive's velocity in s&box units/s (zero when not diving). </summary>
	public static Vector3 DiveVelocity { get; private set; }
	public static float DiveProgress { get; private set; }
	public static float DiveDepth { get; private set; }
	public static Basis CameraTumble { get; private set; } = Basis.Identity;
	public static DockHouse Current { get; private set; }
	/// <summary> No shooting on the dock or during the dive: guns come out once you've landed on the seabed. </summary>
	public static bool WeaponsHeld => Current is { } d && !d.dived;
	/// <summary> Waves wait while the player is still up on the dock. </summary>
	public static bool HoldWaves => Current is { } d && !d.dived;

	Vector3 BoardEnd => new( Origin.X + PierLength + 40f, Origin.Y, Deck + 6f );

	/// <summary> True when the player stands at the diving board (shows the E prompt). </summary>
	public bool BoardInReach( PlayerController controller ) =>
		!DiveSequence && !dived && entering is null && controller.GlobalPosition.DistanceTo( U.Sbox( BoardEnd ) ) < BoardReach * U.In;

	Tween entering;

	/// <summary> Off the board and down to the sea floor: fade out, land on the seabed below the board, fade in. </summary>
	public void EnterSea()
	{
		var player = LocalPlayer;
		if ( player is null || dived || entering is not null ) return;
		var controller = player.Controller;
		var fade = new ColorRect { Color = new Color( 0.02f, 0.08f, 0.16f, 0f ), MouseFilter = Control.MouseFilterEnum.Ignore };
		fade.SetAnchorsPreset( Control.LayoutPreset.FullRect );
		var layer = new CanvasLayer { Layer = 50, Name = "Sea Fade" };
		AddChild( layer );
		layer.AddChild( fade );
		Sfx.Play2D( "splash", 0.9f );
		controller.UseInputControls = false;
		entering = CreateTween();
		entering.TweenProperty( fade, "color:a", 1f, 0.35f );
		entering.TweenCallback( Callable.From( () =>
		{
			// Straight down from the end of the board to the sea floor (the nearest walkable spot there).
			var above = U.Sbox( BoardEnd ) + Vector3.Down * 2f;
			// (From just under the board: the seabed is far below, an old arena floor further still.)
			var floorHit = Trace.Ray( controller, above + Vector3.Down * 3f, above + Vector3.Down * 5000f, Layers.World, new Godot.Collections.Array<Rid> { controller.GetRid() } );
			var floor = floorHit.Hit ? floorHit.HitPosition : U.Sbox( BoardEnd.X, BoardEnd.Y, 0f );
			var land = Nav.Closest( controller, floor );
			if ( land == Vector3.Zero ) land = floor;
			controller.GlobalPosition = land + Vector3.Up * 4f * U.In;
			controller.Velocity = Vector3.Zero;
			dived = true;
			Diving = false;
			Jetpack.Diving = false;
			for ( var i = 0; i < 30; i++ )
				WaterBubble.Create( controller.GlobalPosition + Rng.InSphere( 50f * U.In ) + Vector3.Up * 40f * U.In, Rng.Float( 0.8f, 2.2f ), i * 0.01f );
			controller.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.25f );
		} ) );
		entering.TweenInterval( 0.15f );
		entering.TweenProperty( fade, "color:a", 0f, 0.6f );
		entering.TweenCallback( Callable.From( () =>
		{
			controller.UseInputControls = true;
			layer.QueueFree();
			entering = null;
		} ) );
	}

	float Deck => SurfaceHeight + 18f;
	Vector3 DivePoint => new( Origin.X + PierLength - 20f, Origin.Y, Deck );
	Vector3 StartPoint => new( Origin.X - 140f, Origin.Y, Deck + 4f );

	bool placed;
	bool dived;
	Node3D root;

	static readonly Color Wood = new( 0.55f, 0.37f, 0.21f );
	static readonly Color WoodDark = new( 0.36f, 0.23f, 0.13f );
	static readonly Color WoodPale = new( 0.68f, 0.5f, 0.32f );
	static readonly Color Roof = new( 0.62f, 0.2f, 0.16f );
	static readonly Color Rope = new( 0.82f, 0.72f, 0.5f );

	public override void _Ready()
	{
		Name = "DockHouse";
		Current = this;
		Sea.SurfaceY = SurfaceHeight * U.In;
		Build();
	}

	public override void _ExitTree()
	{
		if ( env is not null ) { env.AmbientLightColor = seaAmbient; env.AmbientLightEnergy = seaAmbientEnergy; }
		if ( hitStopUntil > 0 ) Engine.TimeScale = 1f;
		Sea.SurfaceY = null;
		Diving = false;
		Jetpack.Diving = false;
		if ( Current == this ) Current = null;
	}

	// ---- Building it ----

	Node3D Box( string name, Vector3 center, Vector3 size, Color color, bool solid = false, Angles? rotation = null )
	{
		var go = new Node3D { Name = name };
		root.AddChild( go );
		go.Transform = new Transform3D( (rotation ?? default).ToBasis(), U.Sbox( center ) );
		var godotSize = new Vector3( size.Y, size.Z, size.X ) * U.In;
		var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = godotSize }, MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f } };
		go.AddChild( mesh );
		boxMeshes.Add( mesh );
		if ( solid )
		{
			var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0 };
			go.AddChild( body );
			body.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = godotSize } } );
		}
		return go;
	}

	readonly List<MeshInstance3D> boxMeshes = new();

	/// <summary> A Blender prop (Tools/blender/dock_props.py) under a parent, or null if the model is missing. </summary>
	static Node3D Prop( Node3D parent, string file, Vector3 localPosition )
	{
		var path = $"res://assets/models/dock/{file}.glb";
		if ( !ResourceLoader.Exists( path ) ) return null;
		var prop = GD.Load<PackedScene>( path ).Instantiate<Node3D>();
		parent.AddChild( prop );
		prop.Position = localPosition;
		return prop;
	}

	void Build()
	{
		root = new Node3D { Name = "Dock" };
		AddChild( root );
		var stilts = new List<Vector3>();
		var o = Origin;
		var deck = Deck;

		// The sea's surface: a dense grid the water shader rolls into a swell.
		var waves = WaterWaves.Create( 9000f, 150 );
		waves.Surface = SurfaceHeight * U.In;
		root.AddChild( waves );
		waves.GlobalPosition = U.Sbox( o.X, o.Y, SurfaceHeight );
		// The level's murk is underwater fog; above the surface it glowed green in the air and filled the hut.
		// A big negative-density volume clears it from the surface up.
		var air = new FogVolume
		{
			Name = "Clear Air", Shape = RenderingServer.FogVolumeShape.Box, Size = new Vector3( 9000f, 3000f, 9000f ) * U.In,
			Material = new FogMaterial { Density = -1f, Albedo = Colors.White, Emission = Colors.Black, EdgeFade = 0.05f },
		};
		root.AddChild( air );
		air.GlobalPosition = U.Sbox( o.X, o.Y, SurfaceHeight + 1500f + 10f );

		for ( var i = 0; i < 9; i++ )
		{
			var barrel = new RigidBody3D { Name = "Floating Barrel", Mass = 20f, CollisionLayer = Layers.World, CollisionMask = Layers.World | Layers.Player };
			root.AddChild( barrel );
			barrel.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.On;
			barrel.GlobalPosition = U.Sbox( o.X + 120f + i * 70f, o.Y + (i % 2 == 0 ? -1f : 1f) * (170f + i * 25f), SurfaceHeight + 20f );
			barrel.ResetPhysicsInterpolation();
			var size = new Vector3( 26f, 34f, 26f ) * U.In;
			if ( Prop( barrel, "barrel", Vector3.Zero ) is { } model )
			{
				model.Rotation = new Vector3( 0f, Rng.Float( 0f, MathF.Tau ), 0f );
				barrel.AddChild( new CollisionShape3D { Shape = new CylinderShape3D { Radius = size.X * 0.5f, Height = size.Y } } );
			}
			else
			{
				barrel.AddChild( new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = i % 3 == 0 ? Roof : WoodPale } } );
				barrel.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = size } } );
			}
			barrel.AddChild( new WaveFloater { Surface = SurfaceHeight * U.In } );
		}

		Box( "Pier Floor", new Vector3( o.X + PierLength * 0.5f, o.Y, deck - 3f ), new Vector3( PierLength, 120f, 4f ), WoodDark, solid: true );
		for ( var x = 6f; x < PierLength; x += 16f )
		{
			var shade = ((int)(x / 16f)) % 3 == 0 ? WoodPale : Wood;
			Box( "Plank", new Vector3( o.X + x, o.Y, deck ), new Vector3( 13f, 122f + (x % 7f), 3f ), shade );
		}
		for ( var x = 20f; x <= PierLength; x += 90f )
		{
			foreach ( var side in new[] { -1f, 1f } )
			{
				Box( "Stilt", new Vector3( o.X + x, o.Y + side * 58f, deck - 70f ), new Vector3( 9f, 9f, 150f ), WoodDark );
				stilts.Add( U.Sbox( o.X + x, o.Y + side * 58f, 0f ) );
				if ( x < PierLength - 60f ) Box( "Rail Post", new Vector3( o.X + x, o.Y + side * 58f, deck + 18f ), new Vector3( 5f, 5f, 34f ), Wood );
			}
		}
		foreach ( var side in new[] { -1f, 1f } )
			Box( "Rail", new Vector3( o.X + (PierLength - 60f) * 0.5f + 10f, o.Y + side * 58f, deck + 34f ), new Vector3( PierLength - 60f, 4f, 4f ), WoodPale, solid: true );

		Box( "Diving Board", new Vector3( o.X + PierLength + 22f, o.Y, deck + 4f ), new Vector3( 70f, 34f, 3f ), new Color( 0.92f, 0.9f, 0.84f ), solid: true, rotation: new Angles( -4f, 0f, 0f ) );
		Box( "Board Stand", new Vector3( o.X + PierLength - 4f, o.Y, deck + 1f ), new Vector3( 14f, 30f, 6f ), Roof );
		if ( Prop( root, "rope_coil", U.Sbox( o.X + PierLength - 40f, o.Y + 40f, deck + 1.5f ) ) is null )
			Box( "Rope Coil", new Vector3( o.X + PierLength - 40f, o.Y + 40f, deck + 4f ), new Vector3( 18f, 18f, 5f ), Rope );
		Box( "Lamp Post", new Vector3( o.X + PierLength - 30f, o.Y - 50f, deck + 40f ), new Vector3( 5f, 5f, 80f ), WoodDark );
		if ( Prop( root, "lamp", U.Sbox( o.X + PierLength - 30f, o.Y - 50f, deck + 79f ) ) is { } lantern )
		{
			// Its glass glows.
			var lit = new StandardMaterial3D { AlbedoColor = new Color( 1f, 0.85f, 0.5f ), EmissionEnabled = true, Emission = new Color( 1f, 0.85f, 0.5f ), EmissionEnergyMultiplier = 2f };
			foreach ( var mesh in lantern.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
				for ( var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++ )
					if ( mesh.Mesh.SurfaceGetMaterial( i )?.ResourceName == "LampGlow" ) mesh.SetSurfaceOverrideMaterial( i, lit );
		}
		else
		{
			var lamp = Box( "Lamp", new Vector3( o.X + PierLength - 30f, o.Y - 50f, deck + 82f ), new Vector3( 10f, 10f, 10f ), new Color( 1f, 0.85f, 0.5f ) );
			if ( lamp.GetChild( 0 ) is MeshInstance3D lm && lm.MaterialOverride is StandardMaterial3D lmat ) { lmat.EmissionEnabled = true; lmat.Emission = new Color( 1f, 0.85f, 0.5f ); lmat.EmissionEnergyMultiplier = 2f; }
		}
		var light = new OmniLight3D { LightColor = new Color( 1f, 0.75f, 0.45f ), LightEnergy = 2.5f, OmniRange = 500f * U.In };
		root.AddChild( light );
		light.GlobalPosition = U.Sbox( o.X + PierLength - 30f, o.Y - 50f, deck + 80f );

		var hx = o.X - 150f;
		Box( "House Deck", new Vector3( hx, o.Y, deck - 3f ), new Vector3( 300f, 280f, 6f ), WoodDark, solid: true );
		for ( var x = -140f; x <= 140f; x += 70f )
			foreach ( var y in new[] { -130f, 130f } )
			{
				Box( "Stilt", new Vector3( hx + x, o.Y + y, deck - 70f ), new Vector3( 11f, 11f, 150f ), WoodDark );
				stilts.Add( U.Sbox( hx + x, o.Y + y, 0f ) );
			}
		var wallH = 110f;
		var hz = deck + wallH * 0.5f;
		Box( "Back Wall", new Vector3( hx - 110f, o.Y, hz ), new Vector3( 8f, 200f, wallH ), Wood, solid: true );
		Box( "Side Wall", new Vector3( hx, o.Y - 100f, hz ), new Vector3( 220f, 8f, wallH ), Wood, solid: true );
		Box( "Side Wall", new Vector3( hx, o.Y + 100f, hz ), new Vector3( 220f, 8f, wallH ), Wood, solid: true );
		Box( "Front Wall", new Vector3( hx + 110f, o.Y - 65f, hz ), new Vector3( 8f, 70f, wallH ), Wood, solid: true );
		Box( "Front Wall", new Vector3( hx + 110f, o.Y + 65f, hz ), new Vector3( 8f, 70f, wallH ), Wood, solid: true );
		Box( "Lintel", new Vector3( hx + 110f, o.Y, deck + wallH - 12f ), new Vector3( 8f, 60f, 24f ), Wood, solid: true );
		Box( "Window", new Vector3( hx, o.Y - 104f, hz + 10f ), new Vector3( 50f, 2f, 30f ), new Color( 0.5f, 0.75f, 0.9f ) );
		foreach ( var side in new[] { -1f, 1f } )
			Box( "Roof", new Vector3( hx, o.Y + side * 58f, deck + wallH + 26f ), new Vector3( 250f, 132f, 7f ), Roof, rotation: new Angles( 0f, 0f, -side * 24f ) );

		var fx = new WaterSurfaceFx { Name = "Water Surface Fx", Height = SurfaceHeight * U.In, Center = U.Sbox( o.X, o.Y, SurfaceHeight ), Stilts = stilts };
		root.AddChild( fx );

		Box( "Table", new Vector3( hx - 60f, o.Y + 50f, deck + 24f ), new Vector3( 50f, 34f, 4f ), WoodPale, solid: true );
		Box( "Crate", new Vector3( hx - 70f, o.Y - 60f, deck + 16f ), new Vector3( 30f, 30f, 30f ), WoodDark, solid: true );
		BuildRadio( new Vector3( hx - 60f, o.Y + 50f, deck + 26f ) );
		// The modelled pier and house (Tools/blender/dock_house.py, same layout): the boxes stay as invisible colliders.
		if ( Prop( root, "dock_house", U.Sbox( o.X, o.Y, deck ) ) is { } house )
		{
			foreach ( var mesh in boxMeshes ) mesh.Visible = false;
			foreach ( var mesh in house.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
				mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
		}

		var inside = new OmniLight3D { LightColor = new Color( 1f, 0.72f, 0.45f ), LightEnergy = 1.1f, OmniRange = 220f * U.In, ShadowEnabled = true, ShadowBlur = 1.5f };
		root.AddChild( inside );
		inside.GlobalPosition = U.Sbox( hx - 20f, o.Y, deck + 90f );
		hutMin = U.Sbox( hx - 110f, o.Y - 100f, deck ) ;
		hutMax = U.Sbox( hx + 110f, o.Y + 100f, deck + wallH );
	}

	// ---- The player on the dock, and the dive ----

	enum Phase { None, Torpedo, Land }
	Phase phase;
	float phaseTime;
	Vector3 hopFrom;
	bool splashed;
	float pose;
	float tumbleFor;
	Vector3 tumbleSpin;
	Basis tumble = Basis.Identity;
	Vector3 landEnd;
	bool bloobersSpawned;
	Vector3 diveVelocity;   // s&box units/s, Godot axes
	float landPitch = 70f;
	TimeSince timeSinceBubble;
	double hitStopUntil;

	PlayerSurvivor LocalPlayer => PlayerSurvivor.All.FirstOrDefault();

	Vector3 hutMin, hutMax;
	Godot.Environment env;
	Color seaAmbient;
	float seaAmbientEnergy, inHut, inAir;

	/// <summary>
	/// The level's ambient is tuned for underwater (a strong teal fill). Above the surface it gives way to a dim cool
	/// moonlight fill, and inside the hut to a low warm one, so the lamp does the lighting. Eases between them.
	/// </summary>
	void UpdateAmbient( float dt )
	{
		if ( env is null )
		{
			env = GetTree().CurrentScene?.FindChildren( "*", "WorldEnvironment", true, false ).OfType<WorldEnvironment>().FirstOrDefault()?.Environment
				?? GetViewport().World3D?.Environment;
			if ( env is null ) return;
			seaAmbient = env.AmbientLightColor;
			seaAmbientEnergy = env.AmbientLightEnergy;
			dt = 10f;   // first frame: snap straight to where the camera is
		}
		var cam = GetViewport().GetCamera3D();
		if ( cam is null ) return;
		var p = cam.GlobalPosition;
		var lo = hutMin.Min( hutMax );
		var hi = hutMin.Max( hutMax );
		var hut = p.X > lo.X && p.X < hi.X && p.Z > lo.Z && p.Z < hi.Z && p.Y > lo.Y && p.Y < hi.Y ? 1f : 0f;
		var air = p.Y > SurfaceHeight * U.In ? 1f : 0f;
		var k = 1f - MathF.Exp( -4f * dt );
		inHut = MathX.Lerp( inHut, hut, k );
		inAir = MathX.Lerp( inAir, air, k );
		var night = new Color( 0.09f, 0.12f, 0.17f );
		var warm = new Color( 0.2f, 0.15f, 0.11f );
		env.AmbientLightColor = seaAmbient.Lerp( night.Lerp( warm, inHut ), inAir );
		env.AmbientLightEnergy = MathX.Lerp( seaAmbientEnergy, MathX.Lerp( 1.6f, 1.0f, inHut ), inAir );
	}

	public override void _Process( double delta )
	{
		if ( hitStopUntil > 0 && Clock.Real >= hitStopUntil ) { Engine.TimeScale = 1f; hitStopUntil = 0; }
		UpdateAmbient( (float)delta );
		var player = LocalPlayer;
		if ( player is null ) return;
		var controller = player.Controller;

		if ( !placed && !dived )
		{
			placed = true;
			controller.GlobalPosition = U.Sbox( StartPoint );
			controller.EyeAngles = new Angles( 0f, 0f, 0f );
			controller.Velocity = Vector3.Zero;
		}

		if ( phase == Phase.None ) DockAmbience( controller, (float)delta );
		if ( phase != Phase.None ) { UpdateDive( player, controller, (float)delta ); return; }
		if ( !dived && player.IsAlive && controller.GlobalPosition.Y < (SurfaceHeight - 6f) * U.In )
		{
			if ( DiveSequence ) StartDive( player, controller );
			else EnterSea();
		}
	}

	/// <summary> Testing aid: dive from just past the board. </summary>
	/// <summary> Testing aid: count the dive as done, so going below the surface doesn't start one. </summary>
	public void SkipDive() => dived = true;

	/// <summary>
	/// The old-timey radio on the hut's table (issue #42). It starts the music, which then carries on through the dive
	/// and the rest of the game (Radio). Its dial glows warm while it plays.
	/// </summary>
	void BuildRadio( Vector3 tableTop )
	{
		if ( Prop( root, "radio", U.Sbox( tableTop ) ) is { } radio )
		{
			radio.Name = "Radio Prop";
			radio.RotationDegrees = new Vector3( 0f, 90f, 0f );   // the front toward the middle of the hut
			radio.Scale = Vector3.One * 1.3f;
			var glow = new StandardMaterial3D { AlbedoColor = new Color( 1f, 0.72f, 0.28f ), EmissionEnabled = true, Emission = new Color( 1f, 0.72f, 0.28f ), EmissionEnergyMultiplier = 2.2f };
			foreach ( var mesh in radio.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
				for ( var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++ )
					if ( mesh.Mesh.SurfaceGetMaterial( i )?.ResourceName == "RadioGlow" ) mesh.SetSurfaceOverrideMaterial( i, glow );
			var light = new OmniLight3D { LightColor = new Color( 1f, 0.78f, 0.4f ), LightEnergy = 0.25f, OmniRange = 1.6f, ShadowEnabled = false, Position = new Vector3( 0f, 0.1f, -0.12f ) };
			radio.AddChild( light );
			Radio.Spot = radio.GlobalPosition;
		}
		// Under a scripted test it stays silent unless the test starts it.
		if ( !AutoTest.Requested ) Radio.Instance?.Start();
	}

	TimeUntil nextLap = 1.5f, nextGull = 12f, secondGull = 99999f;

	/// <summary>
	/// The sounds of the dock before the dive (playtest 11/11): waves lapping against the pier every few seconds
	/// from all round, and a seagull once or twice a minute, sometimes calling twice.
	/// </summary>
	void DockAmbience( PlayerController controller, float dt )
	{
		if ( dived || controller is null ) return;
		var at = controller.GlobalPosition;
		if ( nextLap <= 0f )
		{
			nextLap = Rng.Float( 2.2f, 4.8f );
			var angle = Rng.Float( 0f, MathF.Tau );
			var spot = at + new Vector3( MathF.Cos( angle ), 0f, MathF.Sin( angle ) ) * Rng.Float( 5f, 16f );
			Sfx.Play( Sfx.Pick( "dock_wave" ), new Vector3( spot.X, SurfaceHeight * U.In, spot.Z ), Rng.Float( 0.45f, 0.8f ), Rng.Float( 0.9f, 1.1f ) );
		}
		if ( nextGull <= 0f )
		{
			nextGull = Rng.Float( 28f, 48f );
			Gull( at );
			if ( Rng.Float() < 0.45f ) secondGull = Rng.Float( 1.6f, 3.2f );
		}
		if ( secondGull < 90000f )
		{
			secondGull -= dt;
			if ( secondGull <= 0f ) { secondGull = 99999f; Gull( at ); }
		}
	}

	void Gull( Vector3 near )
	{
		var angle = Rng.Float( 0f, MathF.Tau );
		var spot = near + new Vector3( MathF.Cos( angle ) * Rng.Float( 12f, 30f ), Rng.Float( 6f, 14f ), MathF.Sin( angle ) * Rng.Float( 12f, 30f ) );
		Sfx.Play( Sfx.Pick( "gull" ), spot, Rng.Float( 0.6f, 0.9f ), Rng.Float( 0.9f, 1.12f ) );
	}

	public void TestDive()
	{
		var player = LocalPlayer;
		if ( player is null ) return;
		player.Controller.GlobalPosition = U.Sbox( DivePoint + new Vector3( 90f, 0f, 40f ) );
		player.Controller.Velocity = U.Sbox( 150f, 0f, 0f );
	}

	void StartDive( PlayerSurvivor player, PlayerController controller )
	{
		var velocity = controller.Velocity / U.In;
		phase = Phase.Torpedo;
		Sfx.Play2D( "dive_whoosh", 0.8f );
		phaseTime = 0f;
		splashed = false;
		Diving = true;
		Jetpack.Diving = true;
		var speed = MathF.Max( 300f, -velocity.Y );
		diveVelocity = velocity.WithY( -speed );
		DiveCrushed = 0;
		WaterSurfaceFx.Splash( controller.GlobalPosition.WithY( SurfaceHeight * U.In ), velocity.Length() );
		controller.UseInputControls = false;
		controller.UseLookControls = true;
		controller.Scripted = true;
		controller.Velocity = Vector3.Zero;
		controller.GetNodeOrNull<Jetpack>( "Jetpack" )?.BeginDiveRam();
		BeginDiveFx( controller );
		var start = controller.GlobalPosition;   // already past the pier, just under the surface
		var floorHit = Trace.Ray( controller, start, start + Vector3.Down * 5000f, Layers.World, new Godot.Collections.Array<Rid> { controller.GetRid() } );
		seabedY = floorHit.Hit ? floorHit.HitPosition.Y : 0f;
		offEdge = false;
	}

	void UpdateDive( PlayerSurvivor player, PlayerController controller, float dt )
	{
		phaseTime += dt;
		controller.Velocity = Vector3.Zero;
		var juice = controller.GetNodeOrNull<CameraJuice>( "CameraJuice" );
		switch ( phase )
		{
			case Phase.Torpedo:
			{
				var tumbling = tumbleFor > 0f;
				var wantPose = tumbling ? -0.4f : Input.IsActionPressed( "duck" ) ? 1f : Input.IsActionPressed( "jump" ) ? -1f : 0f;
				pose = MathX.Approach( pose, wantPose, 2.5f * dt );
				var terminal = pose >= 0f ? MathX.Lerp( DiveSpeed, DiveBoostSpeed, pose ) : MathX.Lerp( DiveSpeed, DiveBrakeSpeed, -pose );
				const float weight = 900f;
				var drag = weight / (terminal * terminal);
				var speedNow = diveVelocity.Length();
				var accel = Vector3.Down * weight - diveVelocity * (speedNow * drag);
				if ( !tumbling )
				{
					var yaw = new Angles( 0f, controller.EyeAngles.Yaw, 0f );
					var input = Input.GetVector( "left", "right", "backward", "forward" );
					var steer = yaw.Forward * input.Y + yaw.Right * input.X;
					accel += steer * (speedNow * 0.28f + 120f);
				}
				diveVelocity += accel * dt;
				var pos = controller.GlobalPosition + diveVelocity * U.In * dt;

				if ( tumbling )
				{
					tumbleFor -= dt;
					tumbleSpin *= MathF.Exp( -1.4f * dt );
					tumble = new Angles( tumbleSpin.X * dt, tumbleSpin.Y * dt, tumbleSpin.Z * dt ).ToBasis() * tumble;
				}
				else tumble = new Basis( tumble.Orthonormalized().GetRotationQuaternion().SafeSlerp( Quaternion.Identity, 1f - MathF.Exp( -5f * dt ) ) );
				CameraTumble = tumble;

				if ( !tumbling && speedNow > 300f )
				{
					var hit = Trace.Sphere( controller, 16f * U.In, controller.GlobalPosition + Vector3.Up * 35f * U.In, pos + Vector3.Up * 35f * U.In, Layers.World, new Godot.Collections.Array<Rid> { controller.GetRid() } );
					if ( hit.Hit && !hit.StartedSolid && hit.Normal.Y < 0.6f )
					{
						var n = hit.Normal;
						diveVelocity = (diveVelocity - 2f * diveVelocity.Dot( n ) * n) * 0.4f + n * 200f;
						pos = controller.GlobalPosition + n * 4f * U.In;
						Tumble( controller, 1.1f, 1f );
						Jetpack.SlamBubbles( hit.HitPosition, n, 0.6f );
					}
				}

				DiveVelocity = diveVelocity;
				DiveDepth = MathF.Max( 0f, SurfaceHeight * U.In - pos.Y );
				DiveProgress = MathX.Clamp( (SurfaceHeight * U.In - pos.Y) / MathF.Max( 0.01f, SurfaceHeight * U.In ), 0f, 1f );
				if ( !bloobersSpawned && phaseTime > BlooberDelay )
				{
					bloobersSpawned = true;
					SpawnDiveBloobers( pos );
				}

				var crushed = controller.GetNodeOrNull<Jetpack>( "Jetpack" )?.DiveRam( diveVelocity * U.In );
				if ( crushed is Bloober ) DiveCrushed++;

				if ( !splashed && pos.Y < SurfaceHeight * U.In )
				{
					splashed = true;
					for ( var i = 0; i < 40; i++ )
						WaterBubble.Create( pos.WithY( (SurfaceHeight - 20f) * U.In ) + Rng.InSphere( 60f * U.In ), Rng.Float( 1f, 3f ), i * 0.004f );
					juice?.Shake( 0.5f );
					juice?.KickFov( 18f );
				}
				if ( splashed && timeSinceBubble > 0.02f )
				{
					timeSinceBubble = 0f;
					for ( var k = 0; k < 3; k++ )
						WaterBubble.Create( pos + Vector3.Up * Rng.Float( 40f, 120f ) * U.In + Rng.InSphere( 18f * U.In ), Rng.Float( 0.6f, 2f ), 0f, Rng.Float( 0.6f, 1.2f ) );
				}

				// Hitting the seabed: full speed, straight in.
				var ground = Trace.Ray( controller, controller.GlobalPosition + Vector3.Up * 10f * U.In, pos + Vector3.Down * 20f * U.In, Layers.World, new Godot.Collections.Array<Rid> { controller.GetRid() } );
				// Playtest 1 (fell forever): the seabed only covers the arena, so steering past its edge fell into the
				// void. Past the edge the water pushes you back in; and if you still drop below the seabed, you're put
				// down on the nearest floor.
				if ( !ground.Hit && splashed )
				{
					if ( timeSinceEdgeCheck > 0.1f )
					{
						timeSinceEdgeCheck = 0f;
						var below = Trace.Ray( controller, pos, pos.WithY( seabedY - 20f ), Layers.World, new Godot.Collections.Array<Rid> { controller.GetRid() } );
						offEdge = !below.Hit;
					}
					if ( offEdge )
					{
						var home = (U.Sbox( DivePoint ) - pos).Flat();
						var inward = home.SafeNormal();
						var outward = -diveVelocity.Flat().Dot( inward );
						if ( outward > 0f ) diveVelocity += inward * outward;               // stop drifting further out
						diveVelocity += inward * 900f * dt;                                // and drift back toward the arena
					}
					if ( pos.Y < seabedY - 3f )
					{
						var floor = Nav.Closest( controller, pos.WithY( seabedY ) );
						if ( floor == Vector3.Zero ) floor = pos.WithY( seabedY );
						ground = new TraceResult { Hit = true, HitPosition = floor, Normal = Vector3.Up };
						GD.Print( $"Dive: caught a fall past the seabed at {pos}, landing at {floor}" );
					}
				}
				if ( ground.Hit )
				{
					pos = ground.HitPosition + Vector3.Up * 2f * U.In;
					controller.GlobalPosition = pos;
					Impact( player, pos, diveVelocity.Length() );
					controller.GetNodeOrNull<DiveRush>( "DiveRush" )?.QueueFree();
					DiveVelocity = Vector3.Zero;
					landPitch = MathF.Max( controller.EyeAngles.Pitch, 40f );
					phase = Phase.Land;
					phaseTime = 0f;
					hopFrom = pos;
					var flat = diveVelocity.Flat();
					var drift = (flat.Length() > 50f ? flat.Normalized() : new Angles( 0f, controller.EyeAngles.Yaw, 0f ).Forward) * MathX.Clamp( 140f + flat.Length() * 0.15f, 140f, 320f ) * U.In;
					var endTrace = Trace.Ray( controller, pos + drift + Vector3.Up * 300f * U.In, pos + drift + Vector3.Down * 600f * U.In, Layers.World );
					landEnd = endTrace.Hit ? endTrace.HitPosition + Vector3.Up * 2f * U.In : pos + drift;
					tumbleSpin = new Vector3( Rng.Float( 500f, 800f ) * (Rng.Float() < 0.5f ? -1f : 1f), 0f, Rng.Float( -300f, 300f ) );
					break;
				}
				controller.GlobalPosition = pos;
				break;
			}

			case Phase.Land:
			{
				const float fly = 0.85f;
				if ( phaseTime < fly )
				{
					var k = phaseTime / fly;
					var arc = MathF.Sin( k * MathF.PI ) * 150f * U.In;
					controller.GlobalPosition = hopFrom.Lerp( landEnd, k ) + Vector3.Up * arc;
					var spinLeft = 1f - k;
					tumble = new Angles( tumbleSpin.X * dt * spinLeft, tumbleSpin.Y * dt * spinLeft, tumbleSpin.Z * dt * spinLeft ).ToBasis() * tumble;
				}
				else
				{
					var k = MathX.Clamp( (phaseTime - fly) / 0.9f, 0f, 1f );
					var ease = 1f - (1f - k) * (1f - k);
					controller.GlobalPosition = landEnd;
					tumble = new Basis( tumble.Orthonormalized().GetRotationQuaternion().SafeSlerp( Quaternion.Identity, 1f - MathF.Exp( -6f * dt ) ) );
					controller.EyeAngles = new Angles( MathX.Lerp( landPitch, 5f, ease ), controller.EyeAngles.Yaw, 0f );
					if ( phaseTime - fly < dt * 1.5f )
					{
						juice?.Shake( 0.45f );
						for ( var i = 0; i < 20; i++ )
							WaterBubble.Create( landEnd + Rng.InSphere( 30f * U.In ).Flat() + Vector3.Up * 5f * U.In, Rng.Float( 0.8f, 2f ), 0f, 1.2f );
					}
					if ( k >= 1f ) FinishDive( controller );
				}
				CameraTumble = tumble;
				break;
			}
		}
	}

	void Tumble( PlayerController controller, float seconds, float strength )
	{
		tumbleFor = seconds;
		tumbleSpin = Rng.OnSphere() * 900f * strength;
		controller.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.5f * strength );
	}

	void BeginDiveFx( PlayerController controller )
	{
		bloobersSpawned = false;
		DiveKnocked = 0;
		pose = 0f;
		tumbleFor = 0f;
		tumble = Basis.Identity;
		if ( controller.GetNodeOrNull<DiveHands>( "DiveHands" ) is null ) controller.AddChild( new DiveHands() );
		if ( controller.GetNodeOrNull<DiveRush>( "DiveRush" ) is null ) controller.AddChild( new DiveRush() );
		if ( markers is null || !IsInstanceValid( markers ) ) { markers = new DiveMarkers(); controller.AddChild( markers ); }
	}

	DiveMarkers markers;
	float seabedY;
	bool offEdge;
	TimeSince timeSinceEdgeCheck;

	void FinishDive( PlayerController controller )
	{
		controller.GetNodeOrNull<DiveHands>( "DiveHands" )?.QueueFree();
		controller.GetNodeOrNull<DiveRush>( "DiveRush" )?.QueueFree();
		if ( markers is not null && IsInstanceValid( markers ) ) markers.QueueFree();
		markers = null;
		DiveVelocity = Vector3.Zero;
		CameraTumble = Basis.Identity;
		tumble = Basis.Identity;
		phase = Phase.None;
		dived = true;
		Diving = false;
		Jetpack.Diving = false;
		controller.Scripted = false;
		controller.UseInputControls = true;
		controller.UseLookControls = true;
		for ( var i = 0; i < 24; i++ )
			WaterBubble.Create( controller.GlobalPosition + Vector3.Up * 6f * U.In + Rng.InSphere( 40f * U.In ).Flat(), Rng.Float( 0.8f, 2.2f ), i * 0.01f );
		controller.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.35f );
	}

	/// <summary> The landing: a crater, a fountain of debris, a sand cloud, bubbles, and a shockwave. Speed in s&box units/s. </summary>
	void Impact( PlayerSurvivor player, Vector3 at, float speed )
	{
		var power = MathX.Clamp( speed / DiveBoostSpeed, 0.4f, 1f );
		Sfx.Play( "dive_impact", at, 0.7f + 0.3f * power );
		var radius = 170f + 100f * power;
		DiveCrater.Build( this, at, power );

		var sceneRoot = GetTree().CurrentScene;
		var chunks = (int)(70 + 40 * power);
		for ( var i = 0; i < chunks; i++ )
		{
			var a = Rng.Float( 0f, MathF.Tau );
			var outward = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			var big = i % 6 == 0;
			var size = (big ? Rng.Float( 14f, 28f ) : Rng.Float( 4f, 12f )) * (0.7f + 0.5f * power);
			var chunk = new DebrisChunk { Name = "Crater Debris", Size = size, Trail = big };
			sceneRoot.AddChild( chunk );
			chunk.GlobalPosition = at + (outward * Rng.Float( 5f, radius * 0.5f ) + Vector3.Up * Rng.Float( 6f, 30f )) * U.In;
			chunk.Rotation = Rng.InSphere( 3f );
			var tint = big ? new Color( 0.9f, 0.85f, 0.75f ).Lerp( new Color( 0.55f, 0.5f, 0.45f ), Rng.Float() ) : new Color( 0.62f, 0.55f, 0.43f ).Lerp( new Color( 0.32f, 0.29f, 0.25f ), Rng.Float() );
			if ( big ) chunk.AddChild( DiveCrater.Boulder( size / 110f, tint ) );
			else chunk.AddChild( new MeshInstance3D { Mesh = Shapes.Box, Scale = new Vector3( size * Rng.Float( 0.6f, 1.3f ), size * Rng.Float( 0.5f, 1f ), size ) * U.In, MaterialOverride = new StandardMaterial3D { AlbedoColor = tint } } );
			chunk.Velocity = outward * Rng.Float( 150f, 650f ) * power + Vector3.Up * Rng.Float( 900f, 1900f ) * (big ? 0.7f : 1f) * power;
			chunk.Spin = Rng.InSphere( 1f ) * Rng.Float( 5f, 16f );
			Fx.Expire( chunk, Rng.Float( 9f, 14f ) );
		}

		var cloud = new SandCloud { Name = "Crater Cloud", Radius = radius, Power = power };
		sceneRoot.AddChild( cloud );
		cloud.GlobalPosition = at;
		Fx.Expire( cloud, 7f );

		for ( var i = 0; i < 60; i++ )
		{
			var a = Rng.Float( 0f, MathF.Tau );
			var p = at + (new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * Rng.Float( 0f, radius * 1.2f ) + Vector3.Up * Rng.Float( 0f, 60f )) * U.In;
			WaterBubble.Create( p, Rng.Float( 1f, 3.2f ), Rng.Float( 0f, 0.15f ), Rng.Float( 1f, 2.2f ) );
		}

		foreach ( var part in CreaturePart.All.ToList() )
		{
			if ( !IsInstanceValid( part ) || part.Creature is SeaSpider { IsAlly: true } || part.IsDead ) continue;
			var d = part.GlobalPosition.DistanceTo( at ) / U.In;
			if ( d > radius * 2.2f ) continue;
			var falloff = 1f - d / (radius * 2.2f);
			part.ReceiveShot( 160f * falloff * power, part, (part.GlobalPosition - at).SafeNormal(), part.GlobalPosition, at, player.Controller, false );
		}

		var juice = player.Juice;
		juice?.Shake( 1f );
		juice?.KickFov( -22f );
		Engine.TimeScale = 0.1f;
		hitStopUntil = Clock.Real + 0.14;
	}

	void SpawnDiveBloobers( Vector3 from )
	{
		var director = WaveDirector.Current;
		if ( director is null ) return;
		var bottom = 250f * U.In;
		var top = MathF.Min( (SurfaceHeight - 400f) * U.In, from.Y - 1500f * U.In );
		for ( var i = 0; i < DiveBloobers; i++ )
		{
			var t = (i + 0.5f) / DiveBloobers;
			var height = MathX.Lerp( top, bottom, t );
			var ring = Rng.Float( 0f, MathF.Tau );
			var radius = Rng.Float( 30f, 260f ) * U.In;
			var at = new Vector3( from.X + MathF.Cos( ring ) * radius, height, from.Z + MathF.Sin( ring ) * radius );
			// Spawned at a point in the water (not on the ground), so it's placed directly.
			var b = Bloober.Spawn( GetTree().CurrentScene, at, director.BlooberBaseHealth, from - at );
			b.Hue = Rng.Float();
			b.Killed += _ => WaveDirector.Current?.CreditKill();
			markers?.Targets.Add( b );
		}
	}
}
