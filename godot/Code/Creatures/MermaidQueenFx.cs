using Godot;

/// <summary> The Mermaid Queen's gaze beam: a thin aiming line while her eyes charge, then a burning beam to where she looks. </summary>
public partial class MermaidQueen
{
	Node3D beam;
	MeshInstance3D beamCore, beamGlow, aimLine;
	ShaderMaterial beamPaint;
	OmniLight3D beamLight;

	static readonly Shader BeamShader = new()
	{
		Code = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;
uniform vec3 colour : source_color = vec3(0.35, 1.0, 0.45);
uniform vec3 edge : source_color = vec3(0.7, 0.35, 1.0);
uniform float power = 1.0;
void fragment() {
	// UV.y along the beam, UV.x round it: bright bands rushing out from her eyes, violet at the rim.
	float bands = 0.6 + 0.4 * sin(UV.y * 90.0 - TIME * 40.0);
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.0);
	ALBEDO = mix(colour * bands, edge, rim * 0.6) * power;
}",
	};

	void EnsureBeam()
	{
		if ( beam is not null ) return;
		beam = new Node3D { Name = "Queen Gaze", TopLevel = true };
		AddChild( beam );
		beamPaint = new ShaderMaterial { Shader = BeamShader };
		beamPaint.SetMeta( "no_caustics", true );
		var mesh = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 14, Rings = 1, CapTop = false, CapBottom = false };
		beamCore = new MeshInstance3D { Name = "Beam Core", Mesh = mesh, MaterialOverride = beamPaint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		beamGlow = new MeshInstance3D { Name = "Beam Glow", Mesh = mesh, MaterialOverride = beamPaint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		var lineMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color( 0.45f, 1f, 0.5f, 0.55f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		lineMaterial.SetMeta( "no_caustics", true );
		aimLine = new MeshInstance3D { Name = "Gaze Line", Mesh = mesh, MaterialOverride = lineMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		beam.AddChild( beamCore );
		beam.AddChild( beamGlow );
		beam.AddChild( aimLine );
		beamLight = new OmniLight3D { LightColor = new Color( 0.45f, 1f, 0.5f ), LightEnergy = 0f, OmniRange = 7f, ShadowEnabled = false };
		beam.AddChild( beamLight );
		beam.Visible = false;
	}

	static void Span( MeshInstance3D m, Vector3 from, Vector3 to, float radius )
	{
		var axis = to - from;
		var length = MathF.Max( 0.01f, axis.Length() );
		var turn = new Basis( new Quaternion( Vector3.Up, axis / length ) );
		m.GlobalTransform = new Transform3D( turn * Basis.FromScale( new Vector3( radius * 2f, length, radius * 2f ) ), (from + to) * 0.5f );
	}

	void DrawBeam( float dt )
	{
		if ( act != Act.Gaze ) { StopBeam(); return; }
		EnsureBeam();
		beam.Visible = true;
		var a = (float)sinceAct;
		var (from, end) = BeamLine();
		var burning = Burning;
		aimLine.Visible = !burning;
		beamCore.Visible = beamGlow.Visible = burning;
		if ( !burning )
		{
			// The aiming line flickers, thin, from her eyes to the mark.
			var flicker = 0.02f + 0.015f * MathF.Sin( (float)Clock.Now * 50f ) + 0.03f * SmoothKit.Ease( a / GazeCharge );
			Span( aimLine, from, end, flicker );
			beamLight.LightEnergy = 0.6f * SmoothKit.Ease( a / GazeCharge );
			beamLight.GlobalPosition = end + Vector3.Up * 0.3f;
			return;
		}
		var pulse = 1f + MathF.Sin( (float)Clock.Now * 35f ) * 0.12f;
		Span( beamCore, from, end, 0.16f * pulse * (Ascended ? 1.25f : 1f) );
		Span( beamGlow, from, end, 0.5f * pulse * (Ascended ? 1.25f : 1f) );
		beamPaint.SetShaderParameter( "power", 0.9f );
		beamLight.LightEnergy = 4f * pulse;
		beamLight.GlobalPosition = end + (from - end).SafeNormal() * 0.6f;
		// Where it burns: sparks of sand and bubbles boiling up.
		if ( Rng.Float() < 0.7f ) GpuFx.SandPuff( end + Vector3.Up * 0.1f, (Rng.OnSphere().Flat() + Vector3.Up).SafeNormal() * Rng.Float( 1.5f, 4f ), new Color( 0.5f, 1f, 0.55f, 0.8f ), Rng.Float( 4f, 8f ), Rng.Float( 0.4f, 0.8f ) );
		if ( Rng.Float() < 0.5f ) WaterBubble.Create( end + Rng.InSphere( 0.5f ), Rng.Float( 0.2f, 0.5f ), 0f, Rng.Float( 0.5f, 1f ) );
		if ( Rng.Float() < 0.12f ) ScorchMark.Leave( GetTree().CurrentScene, end );
	}

	void StopBeam()
	{
		if ( beam is not null && IsInstanceValid( beam ) ) beam.Visible = false;
	}
}

/// <summary> A glowing burn the gaze beam leaves on the ground, fading over a few seconds. </summary>
public partial class ScorchMark : MeshInstance3D
{
	static StandardMaterial3D paint;
	static readonly QuadMesh Quad = new() { Size = Vector2.One, Orientation = PlaneMesh.OrientationEnum.Y };
	float age;
	const float Life = 4f;
	StandardMaterial3D mine;
	public static int Live;

	public static void Leave( Node parent, Vector3 at )
	{
		if ( parent is null || Live > 40 ) return;
		paint ??= new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color( 0.4f, 1f, 0.45f, 0.7f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, AlbedoTexture = GradientDisc() };
		var m = new ScorchMark { Name = "Scorch", Mesh = Quad, CastShadow = ShadowCastingSetting.Off };
		m.mine = (StandardMaterial3D)paint.Duplicate();
		m.mine.SetMeta( "no_caustics", true );
		m.MaterialOverride = m.mine;
		parent.AddChild( m );
		var ground = Sculpt.GroundAt( at );
		m.GlobalPosition = at with { Y = (ground != float.MinValue && MathF.Abs( ground - at.Y ) < 1.5f ? ground : at.Y) + 0.05f };
		m.Rotation = new Vector3( 0f, Rng.Float( 0f, MathF.Tau ), 0f );
		m.Scale = Vector3.One * Rng.Float( 1.2f, 2f );
		Live++;
	}

	static GradientTexture2D GradientDisc()
	{
		var g = new Gradient();
		g.SetColor( 0, Colors.White );
		g.SetColor( 1, new Color( 1f, 1f, 1f, 0f ) );
		return new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2( 0.5f, 0.5f ), FillTo = new Vector2( 1f, 0.5f ), Width = 64, Height = 64 };
	}

	public override void _ExitTree() => Live--;

	public override void _Process( double delta )
	{
		age += (float)delta;
		mine.AlbedoColor = new Color( 0.4f, 1f, 0.45f, 0.7f * MathX.Clamp( 1f - age / Life, 0f, 1f ) );
		if ( age >= Life ) QueueFree();
	}
}

/// <summary>
/// One of the Mermaid Queen's pearls: it grows on the orbit round her sceptre, then is flung, homes in slowly on the
/// nearest player and bursts on them or on the world (an alien blast that hurts players in its radius). A shot bursts it
/// harmlessly, held or flying. A joiner's copies do no harm.
/// </summary>
public partial class QueenPearl : Node3D
{
	public static readonly List<QueenPearl> All = new();
	MermaidQueen owner;
	bool held = true;
	Vector3 velocity;
	float damage, radius;
	float age;
	float speed;
	OmniLight3D light;
	StaticBody3D shootable;
	public bool Held => held;

	public static QueenPearl Grow( MermaidQueen owner, Vector3 at, int index, int count )
	{
		var scene = owner.GetTree()?.CurrentScene;
		if ( scene is null ) return null;
		var p = new QueenPearl { Name = "Queen Pearl", owner = owner };
		scene.AddChild( p );
		p.GlobalPosition = at;
		p.Scale = Vector3.One * 0.05f;
		return p;
	}

	public override void _Ready()
	{
		All.Add( this );
		SmoothKit.Ball( this, Vector3.Zero, Vector3.One * 0.5f, MermaidQueen.PearlPaint, null, "Pearl" );
		var halo = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color( 0.7f, 0.5f, 1f, 0.25f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add };
		halo.SetMeta( "no_caustics", true );
		SmoothKit.Ball( this, Vector3.Zero, Vector3.One * 0.85f, halo, null, "Halo" );
		light = new OmniLight3D { LightColor = new Color( 0.75f, 0.55f, 1f ), LightEnergy = 1.2f, OmniRange = 3.5f, ShadowEnabled = false };
		AddChild( light );
		shootable = new PearlBody { Name = "Pearl Body", Pearl = this, CollisionLayer = Layers.Creature, CollisionMask = 0 };
		shootable.AddChild( new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.45f } } );
		AddChild( shootable );
	}

	public override void _ExitTree() => All.Remove( this );

	/// <summary> Round her sceptre's orb while held, growing in. </summary>
	public void Orbit( Vector3 centre, int index, int count )
	{
		if ( !held ) return;
		age += (float)GetProcessDeltaTime();
		var a = (float)Clock.Now * 2.4f + index / (float)Math.Max( 1, count ) * MathF.Tau;
		GlobalPosition = centre + new Vector3( MathF.Cos( a ) * 1.1f, MathF.Sin( a * 1.3f ) * 0.3f, MathF.Sin( a ) * 1.1f );
		Scale = Vector3.One * MathX.Clamp( age / 0.6f, 0.05f, 1f );
	}

	public void Launch( Vector3 v, float dmg, float burstRadius )
	{
		held = false;
		velocity = v;
		speed = v.Length();
		damage = dmg;
		radius = burstRadius;
		age = 0f;
		Scale = Vector3.One;
	}

	public override void _Process( double delta )
	{
		if ( held ) { if ( !IsInstanceValid( owner ) ) QueueFree(); return; }
		if ( DevFlags.FreezeEnemies ) return;
		var dt = MathF.Min( (float)delta, 0.1f );
		age += dt;
		var from = GlobalPosition;
		// Homing, gently: it turns toward the nearest player's chest at up to 1.4 rad a second.
		if ( PlayerSurvivor.FindNearestAlive( from ) is { } prey && age > 0.25f )
		{
			var want = (prey.RealPosition + Vector3.Up * 1f - from).SafeNormal();
			var now = velocity.SafeNormal();
			var angle = now.AngleTo( want );
			if ( angle > 1e-3f )
			{
				var turn = MathF.Min( 1f, 1.4f * dt / angle );
				velocity = now.Slerp( want, turn ) * speed;
			}
		}
		var to = from + velocity * dt;
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive ) continue;
			var chest = player.RealPosition + Vector3.Up * 1f;
			if ( Geometry3D.GetClosestPointToSegment( chest, from, to ).DistanceTo( chest ) < 0.9f ) { GlobalPosition = to; Pop( true ); return; }
		}
		var hit = Trace.Ray( this, from, to, Layers.World );
		if ( hit.Hit ) { GlobalPosition = hit.HitPosition; Pop( true ); return; }
		GlobalPosition = to;
		Rotation += new Vector3( 2f, 3f, 0f ) * dt;
		light.LightEnergy = 1.2f + MathF.Sin( age * 20f ) * 0.4f;
		if ( TridentBolt.UnderWater( to ) && Rng.Float() < 0.5f ) TridentBolt.Bubbles( from, to );
		if ( age > 6f ) Pop( true );
	}

	/// <summary> It bursts: a blast, and (the host's, flying) it hurts players in its radius. </summary>
	public void Pop( bool harm )
	{
		if ( !IsInstanceValid( this ) || IsQueuedForDeletion() ) return;
		var at = GlobalPosition;
		var scene = GetTree()?.CurrentScene;
		if ( scene is not null ) AlienBlast.Spawn( scene, at, held ? 0.4f : 0.75f );
		Sfx.Play( "glass_break", at, 0.8f, Rng.Float( 1.4f, 1.7f ) );
		if ( harm && !held && damage > 0f )
			foreach ( var p in PlayerSurvivor.All )
			{
				if ( !p.IsAlive || p.Controller is not { } c ) continue;
				var chest = p.RealPosition + Vector3.Up * 0.9f;
				if ( chest.DistanceTo( at ) > radius + c.BodyRadius ) continue;
				MermaidQueen.PearlHits++;
				p.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = IsInstanceValid( owner ) ? owner : null, Position = chest, Origin = at } );
				c.Velocity += (chest - at).SafeNormal() * 5f;
			}
		QueueFree();
	}

	/// <summary> Shot: it bursts where it is, harming no one. </summary>
	public void Shot( Node attacker )
	{
		if ( IsQueuedForDeletion() ) return;
		MermaidQueen.PearlsShot++;
		(attacker as PlayerController)?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.AddPoints( 25 );
		held = true;   // a shot one never hurts
		Pop( false );
	}
}

/// <summary> A pearl's shootable body (on the creature layer, so shots stop on it). </summary>
public partial class PearlBody : StaticBody3D, IShootable
{
	public QueenPearl Pearl;
	public Node ShotOwner => this;
	public bool IsDead => Pearl is null || !IsInstanceValid( Pearl ) || Pearl.IsQueuedForDeletion();
	public void ReceiveShot( float damage, Node3D hitPart, Vector3 direction, Vector3 position, Vector3 origin, Node attacker, bool scoreHit )
	{
		if ( IsDead ) return;
		Pearl.Shot( attacker );
	}
}

/// <summary>
/// One of the Mermaid Queen's riptide crescents: an arc of curling water, about waist high, rolling out along the ground
/// from where her tail struck. A player on the ground as it passes is hurt and thrown back; a jump clears it. A full
/// circle (her ascension) is a ring. On a joiner's copy it does no damage.
/// </summary>
public partial class QueenWave : Node3D
{
	public static readonly List<QueenWave> All = new();
	public const float Height = 1.1f;
	/// <summary> How tall it is drawn (a little over the height that counts, so a jump that clears it looks like it). </summary>
	const float Drawn = 1.25f;
	const int Segments = 28;
	Vector3 centre;
	float angle, halfArc, radius = 1.5f, speed, reach, damage, delay, age;
	Node3D owner;
	readonly HashSet<PlayerSurvivor> hit = new();
	ImmediateMesh mesh;
	ShaderMaterial paint;

	public float Radius => radius;

	static readonly Shader WaveShader = new()
	{
		Code = @"shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
uniform float fade = 1.0;
void fragment() {
	// UV.x along the crescent, UV.y up the wall (1 at the crest). Deep teal water, lighter up the face, a foaming crest.
	float n = sin(UV.x * 90.0 + TIME * 6.0) * 0.5 + sin(UV.x * 37.0 - TIME * 4.0) * 0.5;
	float crest = smoothstep(0.86 + n * 0.05, 0.99, UV.y);
	float ends = smoothstep(0.0, 0.12, UV.x) * smoothstep(1.0, 0.88, UV.x);
	vec3 water = mix(vec3(0.08, 0.42, 0.55), vec3(0.4, 0.95, 1.0), UV.y);
	float streak = 0.5 + 0.5 * sin(UV.x * 160.0 + UV.y * 8.0 - TIME * 3.0);
	vec3 c = mix(water * (0.85 + streak * 0.25), vec3(0.95, 1.0, 1.0), crest);
	ALBEDO = c;
	EMISSION = c * 0.55 + vec3(0.7, 0.45, 1.0) * crest * 0.4;
	ALPHA = clamp((0.35 + UV.y * 0.35 + crest * 0.35) * ends * fade, 0.0, 0.9);
}",
	};

	public static QueenWave Send( Node parent, Vector3 centre, float angle, float halfArc, float speed, float reach, float damage, Node3D owner, float delay )
	{
		if ( parent is null ) return null;
		var w = new QueenWave { Name = "Queen Riptide", centre = centre, angle = angle, halfArc = halfArc, speed = speed, reach = reach, damage = damage, owner = owner, delay = delay };
		parent.AddChild( w );
		return w;
	}

	public override void _Ready()
	{
		All.Add( this );
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		mesh = new ImmediateMesh();
		paint = new ShaderMaterial { Shader = WaveShader };
		paint.SetMeta( "no_caustics", true );
		AddChild( new MeshInstance3D { Name = "Water", Mesh = mesh, MaterialOverride = paint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
	}

	public override void _ExitTree() => All.Remove( this );

	float Ground( Vector3 p )
	{
		var g = Sculpt.GroundAt( p );
		return g != float.MinValue ? g : centre.Y;
	}

	public override void _Process( double delta )
	{
		var dt = MathF.Min( (float)delta, 0.1f );
		age += dt;
		if ( age < delay ) return;
		var was = radius;
		radius += speed * dt;
		if ( radius > reach ) { QueueFree(); return; }
		paint.SetShaderParameter( "fade", MathX.Clamp( (reach - radius) / (reach * 0.25f), 0f, 1f ) * MathX.Clamp( (radius - 1.5f) / 2f, 0.3f, 1f ) );
		Build();
		if ( Rng.Float() < 0.5f )
		{
			var a = angle + Rng.Float( -halfArc, halfArc );
			var p = centre + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * radius;
			WaterBubble.Create( p with { Y = Ground( p ) + Rng.Float( 0.2f, Height ) }, Rng.Float( 0.2f, 0.5f ), 0f, Rng.Float( 0.4f, 0.8f ) );
		}
		if ( damage <= 0f ) return;
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || hit.Contains( p ) || p.Controller is not { } c ) continue;
			var feet = p.RealPosition;
			var off = (feet - centre).Flat();
			var d = off.Length();
			if ( d < was - c.BodyRadius || d > radius + c.BodyRadius ) continue;
			if ( halfArc < MathF.PI )
			{
				var diff = Mathf.Wrap( MathF.Atan2( off.Z, off.X ) - angle, -MathF.PI, MathF.PI );
				if ( MathF.Abs( diff ) > halfArc + c.BodyRadius / MathF.Max( d, 0.5f ) ) continue;
			}
			var ground = MathF.Max( Ground( feet ), Sculpt.FloorBelow( c, feet + Vector3.Up * 0.3f, 2f ) );
			var up = feet.Y - ground;
			if ( up > Height - 0.05f ) { hit.Add( p ); MermaidQueen.WaveJumps++; continue; }
			if ( up < -0.6f ) continue;
			hit.Add( p );
			MermaidQueen.WaveHits++;
			p.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = IsInstanceValid( owner ) ? owner : null, Position = feet + Vector3.Up * 0.5f, Origin = centre } );
			if ( !c.Scripted ) c.Velocity += off.SafeNormal() * 9f + Vector3.Up * 4f;
			p.Juice?.Shake( 0.5f );
		}
	}

	/// <summary>
	/// The crescent as a rolling body of water along its arc: a long back slope rising to the crest and a steep front face
	/// curling over it, lower toward its ends. UV.y runs 0 at the ground to 1 at the crest on both faces.
	/// </summary>
	void Build()
	{
		mesh.ClearSurfaces();
		mesh.SurfaceBegin( Mesh.PrimitiveType.Triangles );
		var rows = new Vector3[Segments + 1, 4];
		var us = new float[Segments + 1];
		for ( var i = 0; i <= Segments; i++ )
		{
			var u = i / (float)Segments;
			var a = angle - halfArc + u * halfArc * 2f;
			var dir = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			var shape = halfArc >= MathF.PI ? 1f : MathF.Sin( u * MathF.PI ) * 0.65f + 0.35f;
			var h = Drawn * shape * (0.9f + 0.1f * MathF.Sin( a * 9f + radius * 2f ));
			Vector3 At( float r, float up ) { var p = centre + dir * MathF.Max( 0f, r ); return p with { Y = Ground( p ) + up }; }
			rows[i, 0] = At( radius - 2.2f * shape, 0.02f );      // the back slope's foot
			rows[i, 1] = At( radius - 0.2f, h );                   // the crest
			rows[i, 2] = At( radius + 0.35f * shape, h * 0.8f );   // the lip, curling over
			rows[i, 3] = At( radius + 0.1f, 0.02f );               // the front face's foot
			us[i] = u;
		}
		void Quad( Vector3 a, Vector3 b, Vector3 c, Vector3 d, float ua, float ub, float va, float vb )
		{
			mesh.SurfaceSetUV( new Vector2( ua, va ) ); mesh.SurfaceAddVertex( a );
			mesh.SurfaceSetUV( new Vector2( ua, vb ) ); mesh.SurfaceAddVertex( b );
			mesh.SurfaceSetUV( new Vector2( ub, va ) ); mesh.SurfaceAddVertex( c );
			mesh.SurfaceSetUV( new Vector2( ua, vb ) ); mesh.SurfaceAddVertex( b );
			mesh.SurfaceSetUV( new Vector2( ub, vb ) ); mesh.SurfaceAddVertex( d );
			mesh.SurfaceSetUV( new Vector2( ub, va ) ); mesh.SurfaceAddVertex( c );
		}
		for ( var i = 1; i <= Segments; i++ )
		{
			float u0 = us[i - 1], u1 = us[i];
			Quad( rows[i - 1, 0], rows[i - 1, 1], rows[i, 0], rows[i, 1], u0, u1, 0f, 1f );     // back slope
			Quad( rows[i - 1, 1], rows[i - 1, 2], rows[i, 1], rows[i, 2], u0, u1, 1f, 0.9f );   // the lip
			Quad( rows[i - 1, 3], rows[i - 1, 2], rows[i, 3], rows[i, 2], u0, u1, 0f, 0.9f );   // front face
		}
		mesh.SurfaceEnd();
	}
}

/// <summary> One ring of the siren song: a pink and violet ring of sound rushing out from her throat, growing and fading. </summary>
public partial class SirenRing : Node3D
{
	float age;
	MeshInstance3D ring;
	StandardMaterial3D paint;
	Color colour;

	public static void Send( Node parent, Vector3 at, Vector3 dir, bool strong )
	{
		if ( parent is null ) return;
		var w = new SirenRing { Name = "Siren Ring", colour = Rng.Float() < 0.5f ? new Color( 1f, 0.45f, 0.85f ) : new Color( 0.65f, 0.45f, 1f ) };
		if ( strong ) w.colour = w.colour.Lerp( new Color( 0.4f, 1f, 0.6f ), 0.35f );
		parent.AddChild( w );
		var flat = dir.Flat().SafeNormal();
		if ( flat == Vector3.Zero ) flat = Vector3.Forward;
		w.GlobalTransform = new Transform3D( Shapes.LookAt( flat ) * new Basis( Vector3.Right, MathF.PI / 2f ), at );
	}

	public override void _Ready()
	{
		paint = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = colour with { A = 0.5f }, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		paint.SetMeta( "no_caustics", true );
		ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.88f, OuterRadius = 1f, Rings = 48, RingSegments = 6 }, MaterialOverride = paint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( ring );
	}

	public override void _Process( double delta )
	{
		age += (float)delta;
		var k = age / 1.6f;
		if ( k >= 1f ) { QueueFree(); return; }
		ring.Scale = Vector3.One * (0.6f + k * 9f);
		ring.Position = Vector3.Up * k * 22f;   // the ring's own up is along the song
		ring.Rotation = new Vector3( 0f, age * 2f, 0f );
		paint.AlbedoColor = colour with { A = 0.55f * (1f - k) * MathX.Clamp( k * 6f, 0f, 1f ) };
	}
}
