using Godot;

/// <summary>
/// Port of s&box WaterWaves: the sea's surface as a dense grid that the water shader bends into a rolling swell,
/// and the same swell on the CPU so floating things ride it.
/// </summary>
public partial class WaterWaves : MeshInstance3D
{
	public float Surface;   // metres
	ShaderMaterial material;

	/// <summary> Wave height above the resting surface (metres) at a world position (matches swell() in water.gdshader). </summary>
	public static float Height( Vector3 world, float t )
	{
		var x = -world.Z / U.In;
		var y = -world.X / U.In;
		var h = MathF.Sin( (x * 0.80f + y * 0.60f) * 0.0060f + t * 0.55f ) * 1.00f
			+ MathF.Sin( (x * -0.50f + y * 0.87f) * 0.0110f + t * 0.80f ) * 0.65f
			+ MathF.Sin( (x * 0.30f + y * -0.95f) * 0.0240f + t * 1.20f ) * 0.38f;
		return h * 7f * U.In;   // crests stay under the pier's deck (18 inches up)
	}

	public static WaterWaves Create( float sizeInches, int cells )
	{
		var waves = new WaterWaves { Name = "Sea Waves", CastShadow = ShadowCastingSetting.Off };
		waves.Mesh = new PlaneMesh { Size = Vector2.One * sizeInches * U.In, SubdivideWidth = cells, SubdivideDepth = cells };
		waves.material = new ShaderMaterial { Shader = GD.Load<Shader>( "res://assets/shaders/water.gdshader" ) };
		waves.MaterialOverride = waves.material;
		waves.ExtraCullMargin = 60f * U.In;
		return waves;
	}

	public override void _Process( double delta ) => material?.SetShaderParameter( "wave_time", (float)Clock.Now );
}

/// <summary> Port of s&box WaveFloater: buoyancy against the moving wave surface, and a drift with the swell. </summary>
public partial class WaveFloater : Node
{
	public float Surface;

	public override void _PhysicsProcess( double delta )
	{
		if ( GetParent() is not RigidBody3D body ) return;
		var dt = (float)delta;
		var p = body.GlobalPosition;
		var wave = Surface + WaterWaves.Height( p, (float)Clock.Now );
		var depth = (wave - p.Y) / U.In;
		if ( depth > -10f )
		{
			var push = MathX.Clamp( depth, -10f, 40f );
			body.LinearVelocity += Vector3.Up * (push * 26f + 980f * (depth > 0 ? 0.9f : 0f)) * U.In * dt;
			body.LinearVelocity *= MathF.Exp( -1.2f * dt );
			body.AngularVelocity *= MathF.Exp( -1.5f * dt );
			body.LinearVelocity += U.Sbox( 0.8f, 0.6f, 0f ) * 20f * dt;
		}
	}
}

/// <summary>
/// Port of s&box WaterSurfaceFx: sun glints flashing across the water near the dock and foam rings breathing round
/// the stilts. Splash() makes the splash when something goes in.
/// </summary>
public partial class WaterSurfaceFx : Node3D
{
	public float Height;
	public Vector3 Center;
	public List<Vector3> Stilts = new();

	// The lit water catches the real sun now, so only a few sparkles are added on top.
	const int GlintCount = 20;
	readonly List<(MeshInstance3D Mesh, Vector3 Pos, float Phase)> glints = new();
	readonly List<(MeshInstance3D Mesh, float Phase)> foams = new();

	static MeshInstance3D Blob( Node parent, Color tint, bool glow = false )
	{
		var m = new MeshInstance3D
		{
			Mesh = Shapes.Sphere,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = tint, ShadingMode = glow ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			TopLevel = true,
		};
		parent.AddChild( m );
		return m;
	}

	public override void _Ready()
	{
		for ( var i = 0; i < GlintCount; i++ )
		{
			var pos = Center + new Vector3( Rng.Float( -1400f, 1400f ), 0f, Rng.Float( -1400f, 1400f ) ) * U.In;
			glints.Add( (Blob( this, new Color( 1f, 0.98f, 0.9f ) * 1.4f, glow: true ), pos, Rng.Float( 0f, 6.28f )) );
		}
		// Foam round the stilts is drawn by the water shader now (contact froth), so no flat white discs here.
	}

	public override void _Process( double delta )
	{
		var t = (float)Clock.Now;
		foreach ( var (mesh, pos, phase) in glints )
		{
			var flash = MathF.Max( 0f, MathF.Sin( t * 2.3f + phase * 3f ) );
			flash = flash * flash * flash * flash;
			var y = Height + WaterWaves.Height( pos, t ) + 2f * U.In;
			mesh.GlobalTransform = new Transform3D( Basis.FromScale( Vector3.One * (0.3f + 3.2f * flash) * 2f * U.In ), pos.WithY( y ) );
		}
		for ( var i = 0; i < foams.Count && i < Stilts.Count; i++ )
		{
			var (mesh, phase) = foams[i];
			var s = 14f + 4f * MathF.Sin( t * 1.6f + phase );
			var at = Stilts[i].WithY( Height + WaterWaves.Height( Stilts[i], t ) + 1.2f * U.In );
			mesh.GlobalTransform = new Transform3D( Basis.FromScale( new Vector3( s, 0.6f, s ) * 2f * U.In ), at );
		}
	}

	/// <summary> The splash: something hit the water here (speed in s&box units/s sets how big). </summary>
	public static void Splash( Vector3 at, float speed )
	{
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( root is null ) return;
		Sfx.Play( "splash", at, MathX.Clamp( speed / 900f, 0.35f, 1f ), Rng.Float( 0.9f, 1.1f ) );
		var splash = new SplashFx { Power = MathX.Clamp( speed / 900f, 0.4f, 1.6f ), Position = at };   // placed before _Ready, which builds around it
		root.AddChild( splash );
		splash.GlobalPosition = at;
		Fx.Expire( splash, 2.6f );
	}
}

/// <summary> One splash: droplets thrown up in a crown, a tall column of spray, and foam rings spreading out. </summary>
public partial class SplashFx : Node3D
{
	public float Power = 1f;
	readonly List<(MeshInstance3D Mesh, Vector3 Vel, float Size)> drops = new();
	MeshInstance3D ring, ring2, column;
	TimeSince age;
	float baseY;

	MeshInstance3D Make( Color tint )
	{
		var m = new MeshInstance3D { Mesh = Shapes.Sphere, MaterialOverride = new StandardMaterial3D { AlbedoColor = tint }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true };
		AddChild( m );
		m.GlobalTransform = new Transform3D( Basis.FromScale( Vector3.One * 0.001f ), GlobalPosition );   // hidden at the splash until the first update
		return m;
	}

	public override void _Ready()
	{
		age = 0f;
		baseY = GlobalPosition.Y;
		var white = new Color( 0.92f, 0.98f, 1f );
		var count = (int)(36 * Power);
		for ( var i = 0; i < count; i++ )
		{
			var a = Rng.Float( 0f, MathF.Tau );
			var outward = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			var high = i % 3 == 0;
			var vel = (outward * Rng.Float( high ? 40f : 160f, high ? 120f : 340f ) * Power + Vector3.Up * Rng.Float( high ? 520f : 220f, high ? 820f : 420f ) * Power) * U.In;
			var drop = Make( white.Lerp( new Color( 0.6f, 0.88f, 0.95f ), Rng.Float() ) );
			drop.GlobalPosition = GlobalPosition + outward * Rng.Float( 4f, 22f ) * U.In;
			drops.Add( (drop, vel, Rng.Float( 2.5f, 6f ) * Power) );
		}
		ring = Make( white );
		ring2 = Make( new Color( 0.75f, 0.92f, 0.98f ) );
		column = Make( white );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var t = (float)age;
		for ( var i = 0; i < drops.Count; i++ )
		{
			var (mesh, vel, size) = drops[i];
			vel += Vector3.Down * 900f * U.In * dt;
			vel *= MathF.Exp( -0.6f * dt );
			mesh.GlobalPosition += vel * dt;
			drops[i] = (mesh, vel, size);
			var fade = MathX.Clamp( 1f - t / 1.6f, 0f, 1f );
			var below = mesh.GlobalPosition.Y < baseY;
			mesh.Scale = Vector3.One * (below ? 0.0001f : size * fade * 2f * U.In);
		}
		var spread = 1f - MathF.Exp( -2.2f * t );
		var fadeRing = MathX.Clamp( 1f - t / 2.4f, 0f, 1f );
		ring.GlobalTransform = new Transform3D( Basis.FromScale( new Vector3( 30f + 160f * spread * Power, 1.2f * fadeRing + 0.01f, 30f + 160f * spread * Power ) * 2f * U.In ), GlobalPosition + Vector3.Up * 1.5f * U.In );
		var spread2 = 1f - MathF.Exp( -1.2f * t );
		ring2.GlobalTransform = new Transform3D( Basis.FromScale( new Vector3( 50f + 300f * spread2 * Power, 0.8f * fadeRing + 0.01f, 50f + 300f * spread2 * Power ) * 2f * U.In ), GlobalPosition + Vector3.Up * U.In );
		var rise = MathF.Sin( MathX.Clamp( t / 0.9f, 0f, 1f ) * MathF.PI );
		var w = 16f * Power * (1f - 0.4f * rise);
		column.GlobalTransform = new Transform3D( Basis.FromScale( new Vector3( w, 70f * rise * Power + 0.01f, w ) * 2f * U.In ), GlobalPosition + Vector3.Up * 60f * rise * Power * U.In );
	}
}
