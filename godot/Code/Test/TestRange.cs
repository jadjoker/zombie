using Godot;

/// <summary>
/// A movement test range built from code: flat ground, ramps of different steepness, stairs, ledges at mantle
/// heights, and walls to kick off. Spawns the player with the ported controller, movement and camera juice,
/// plus a small debug readout (speed in s&box units, ground mode, stamina).
/// </summary>
public partial class TestRange : Node3D
{
	PlayerController player;
	PlayerMovement movement;
	Label readout;

	public override void _Ready()
	{
		var env = new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Sky,
				Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
				AmbientLightSource = Godot.Environment.AmbientSource.Sky,
				TonemapMode = Godot.Environment.ToneMapper.Filmic,
				SsaoEnabled = true,
			},
		};
		AddChild( env );
		AddChild( new DirectionalLight3D { ShadowEnabled = true, RotationDegrees = new Vector3( -50f, -30f, 0f ) } );

		// Ground: 8000 x 8000 s&box units.
		Box( new Vector3( 0f, -10f, 0f ) * U.In, new Vector3( 8000f, 20f, 8000f ) * U.In, new Color( 0.42f, 0.45f, 0.48f ), grid: true );

		// Ramps: 15, 30 and 44 degrees (walkable), 55 degrees (too steep, slide down).
		var x = -800f;
		foreach ( var angle in new[] { 15f, 30f, 44f, 55f } )
		{
			var length = 600f;
			var ramp = Box( Vector3.Zero, new Vector3( 200f, 20f, length ) * U.In, new Color( 0.55f, 0.5f, 0.42f ) );
			ramp.RotationDegrees = new Vector3( angle, 0f, 0f );
			ramp.Position = new Vector3( x, MathF.Sin( MathX.Rad( angle ) ) * length * 0.5f - 6f, -900f ) * U.In;
			x += 300f;
		}

		// Stairs: 12 unit risers (steppable) next to 24 unit ones (not).
		for ( var i = 0; i < 8; i++ )
		{
			Box( new Vector3( 600f, 6f + i * 12f, -300f + i * 30f ) * U.In, new Vector3( 160f, 12f + i * 24f, 30f ) * U.In, new Color( 0.6f, 0.58f, 0.55f ) );
			Box( new Vector3( 800f, 12f + i * 24f, -300f + i * 40f ) * U.In, new Vector3( 160f, 24f + i * 48f, 40f ) * U.In, new Color( 0.5f, 0.48f, 0.46f ) );
		}

		// Ledges at 40, 70, 95 (max mantle) and 130 (too high).
		var z = 300f;
		foreach ( var h in new[] { 40f, 70f, 95f, 130f } )
		{
			Box( new Vector3( -500f, h * 0.5f, z ) * U.In, new Vector3( 200f, h, 120f ) * U.In, new Color( 0.35f, 0.5f, 0.62f ) );
			z += 250f;
		}

		// Two tall parallel walls for wall kicks.
		Box( new Vector3( 300f, 250f, 600f ) * U.In, new Vector3( 20f, 500f, 800f ) * U.In, new Color( 0.62f, 0.4f, 0.36f ) );
		Box( new Vector3( 560f, 250f, 600f ) * U.In, new Vector3( 20f, 500f, 800f ) * U.In, new Color( 0.62f, 0.4f, 0.36f ) );

		// A long downhill for slides.
		var hill = Box( Vector3.Zero, new Vector3( 300f, 20f, 1400f ) * U.In, new Color( 0.45f, 0.55f, 0.4f ) );
		hill.RotationDegrees = new Vector3( -12f, 0f, 0f );
		hill.Position = new Vector3( 1300f, MathF.Sin( MathX.Rad( 12f ) ) * 700f - 6f, 200f ) * U.In;

		player = new PlayerController { Name = "Player", Position = new Vector3( 0f, 10f, 0f ) * U.In };
		AddChild( player );
		movement = new PlayerMovement();
		player.AddChild( movement );
		player.AddChild( new CameraJuice() );
		player.AddChild( new WeaponInventory() );
		player.AddChild( PlayerWeapon.Rifle() );
		player.AddChild( PlayerWeapon.Shotgun() );
		player.AddChild( new PlayerMelee() );
		player.AddChild( new PlayerSurvivor() );

		// Targets to shoot: a row of dummies at 10, 20 and 40 metres, and some loose crates.
		for ( var i = 0; i < 5; i++ )
		{
			var target = new TestTarget { Position = new Vector3( (i - 2) * 120f, 0f, 1200f + i * 300f ) * U.In };
			AddChild( target );
		}
		for ( var i = 0; i < 6; i++ )
		{
			var crate = new RigidBody3D { Mass = 20f, CollisionLayer = Layers.World, CollisionMask = Layers.World | Layers.Player, Position = new Vector3( 400f + (i % 3) * 40f, 20f + (i / 3) * 40f, 1400f ) * U.In };
			crate.AddChild( new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * 36f * U.In }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color( 0.55f, 0.4f, 0.25f ) } } );
			crate.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * 36f * U.In } } );
			AddChild( crate );
		}

		var hud = new CanvasLayer();
		AddChild( hud );
		readout = new Label { Position = new Vector2( 20, 120 ) };
		readout.AddThemeFontSizeOverride( "font_size", 20 );
		readout.AddThemeColorOverride( "font_outline_color", Colors.Black );
		readout.AddThemeConstantOverride( "outline_size", 6 );
		hud.AddChild( readout );
		hud.AddChild( WeaponHud.Create( player ) );
		hud.AddChild( GameHud.Create( player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ) ) );
		// The wave director, paused until P toggles it (the s&box editor started paused too).
		AddChild( new WaveDirector { SpawningPaused = !OS.GetCmdlineUserArgs().Contains( "--waves" ) } );

		if ( AutoTest.Requested ) AddChild( new AutoTest() );
	}

	public override void _UnhandledInput( InputEvent e )
	{
		// O: toggle wave spawning. B: a Bloober 15 m ahead (dev spawn).
		if ( e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.O } && WaveDirector.Current is { } d ) d.SpawningPaused = !d.SpawningPaused;
		if ( e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.B } )
		{
			var ahead = player.EyeAngles.WithPitch( 0f ).Forward;
			Bloober.Spawn( this, player.GlobalPosition + ahead * 600f * U.In + Vector3.Up * 90f * U.In, 160f, -ahead );
		}
	}

	public override void _Process( double delta )
	{
		if ( player is null ) return;
		var speed = player.Velocity.Flat().Length() / U.In;
		var survivor = player.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
		readout.Text = $"health {survivor?.Health?.Current ?? 0:0}  points {survivor?.Points ?? 0}   speed {speed:0} u/s   {(player.IsOnGround ? "ground" : "air")}   {movement.Ground}{(player.IsDucking ? "  ducked" : "")}\n"
			+ $"stamina {movement.Stamina:0.0}/{movement.MaxStamina}   air jumps {movement.AirJumpsLeft}   {(player.ThirdPerson ? "third person" : "first person")} (C)\n"
			+ "WASD move, Shift sprint, Space jump/air jump/wall kick/mantle, Ctrl slide/duck\n"
			+ "LMB fire, RMB aim (rifle scope), R reload, 1/2 or wheel switch, Mouse5 axe, B spawns a Bloober, O toggles waves, Esc frees the mouse";
	}

	/// <summary> A solid box; position and rotation go on the returned body. </summary>
	StaticBody3D Box( Vector3 position, Vector3 size, Color color, bool grid = false )
	{
		var body = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0, Position = position };
		AddChild( body );
		body.AddChild( new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = size },
			MaterialOverride = grid ? GridMaterial( color ) : new StandardMaterial3D { AlbedoColor = color, Roughness = 0.9f },
		} );
		body.AddChild( new CollisionShape3D { Shape = new BoxShape3D { Size = size } } );
		return body;
	}

	static StandardMaterial3D GridMaterial( Color color ) => new()
	{
		AlbedoColor = color,
		Roughness = 0.95f,
		Uv1Triplanar = true,
		Uv1Scale = Vector3.One * 0.5f,
		AlbedoTexture = new NoiseTexture2D { Width = 64, Height = 64, Noise = new FastNoiseLite { Frequency = 0.2f } },
	};
}
