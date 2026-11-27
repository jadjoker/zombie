using Godot;

/// <summary>
/// The Lighthouse Hermit's lights and cues, drawn on every copy: the lamp's sweeping beam (a cone of light turning round
/// like a real lighthouse's, with a spotlight on the sand), the Lantern Beam (the cone narrows onto the mark, then a burning
/// core), the glowing ring where he will burst up, the horn's bubbles and steam, the sand he ploughs while buried, and the
/// shader values (flash, the cracks and veins charging, the lamp focusing and dying).
/// </summary>
public partial class LighthouseHermit
{
	Node3D beam, sweep;
	MeshInstance3D sweepCone, beamCore, beamGlow, cue;
	ShaderMaterial conePaint, corePaint;
	SpotLight3D sweepLight;
	OmniLight3D beamLight;
	ImmediateMesh cueMesh;
	float sweepYaw, focus;
	TimeSince sincePuff = 99f;

	static readonly Shader ConeShader = new()
	{
		Code = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
uniform vec3 colour : source_color = vec3(1.0, 0.86, 0.45);
uniform float power = 1.0;
varying float along;
void vertex() { along = VERTEX.y + 0.5; }
void fragment() {
	// Bright at the lamp, fading out along the beam and at its edges (soft sides).
	float edge = pow(clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0), 1.5);
	float fade = (1.0 - along) * (1.0 - along);
	float motes = 0.85 + 0.15 * sin(along * 60.0 - TIME * 3.0);
	ALBEDO = colour * edge * fade * motes * power * 0.55;
}",
	};

	static readonly Shader CoreShader = new()
	{
		Code = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
uniform vec3 colour : source_color = vec3(1.0, 0.9, 0.5);
uniform vec3 edge : source_color = vec3(0.35, 1.0, 0.45);
uniform float power = 1.0;
void fragment() {
	float bands = 0.6 + 0.4 * sin(UV.y * 80.0 - TIME * 36.0);
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.0);
	ALBEDO = mix(colour * bands, edge, rim * 0.7) * power;
}",
	};

	static readonly Shader CueShader = new()
	{
		Code = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
void fragment() { ALBEDO = COLOR.rgb * COLOR.a; }",
	};

	void EnsureBeams()
	{
		if ( sweep is not null ) return;
		var scene = GetTree().CurrentScene;
		sweep = new Node3D { Name = "Lamp Sweep", TopLevel = true };
		AddChild( sweep );
		conePaint = new ShaderMaterial { Shader = ConeShader };
		conePaint.SetMeta( "no_caustics", true );
		var cone = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.08f, Height = 1f, RadialSegments = 20, Rings = 4, CapTop = false, CapBottom = false };
		sweepCone = new MeshInstance3D { Name = "Lamp Cone", Mesh = cone, MaterialOverride = conePaint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		sweep.AddChild( sweepCone );
		sweepLight = new SpotLight3D { Name = "Lamp Spot", LightColor = LampGold, LightEnergy = 5f, SpotRange = 42f, SpotAngle = 11f, SpotAttenuation = 0.6f, ShadowEnabled = false };
		sweep.AddChild( sweepLight );

		beam = new Node3D { Name = "Lantern Beam", TopLevel = true };
		AddChild( beam );
		corePaint = new ShaderMaterial { Shader = CoreShader };
		corePaint.SetMeta( "no_caustics", true );
		var tube = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 14, Rings = 1, CapTop = false, CapBottom = false };
		beamCore = new MeshInstance3D { Name = "Beam Core", Mesh = tube, MaterialOverride = corePaint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		beamGlow = new MeshInstance3D { Name = "Beam Glow", Mesh = tube, MaterialOverride = corePaint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		beam.AddChild( beamCore );
		beam.AddChild( beamGlow );
		beamLight = new OmniLight3D { LightColor = LampGold, LightEnergy = 0f, OmniRange = 8f, ShadowEnabled = false };
		beam.AddChild( beamLight );
		beam.Visible = false;

		cueMesh = new ImmediateMesh();
		var cuePaint = new ShaderMaterial { Shader = CueShader };
		cuePaint.SetMeta( "no_caustics", true );
		cue = new MeshInstance3D { Name = "Erupt Cue", Mesh = cueMesh, MaterialOverride = cuePaint, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( cue );
		cue.GlobalTransform = Transform3D.Identity;
		cue.Visible = false;
	}

	static void Span( MeshInstance3D m, Vector3 from, Vector3 to, float radius ) => Span( m, from, to, radius, radius );

	/// <summary> A unit cylinder (Y from -0.5 to 0.5) from one point to another; `far` is the radius scale at `to`. </summary>
	static void Span( MeshInstance3D m, Vector3 from, Vector3 to, float near, float far )
	{
		var axis = to - from;
		var length = MathF.Max( 0.01f, axis.Length() );
		var turn = new Basis( new Quaternion( Vector3.Up, axis / length ) );
		var r = MathF.Max( near, far ) * 2f;
		m.GlobalTransform = new Transform3D( turn * Basis.FromScale( new Vector3( r, length, r ) ), (from + to) * 0.5f );
	}

	void StopBeam()
	{
		if ( beam is not null && IsInstanceValid( beam ) ) beam.Visible = false;
	}

	void Effects( float dt )
	{
		var t = (float)Clock.Now;
		var a = (float)sinceAct;
		flash = MathF.Max( 0f, flash - dt * 6f );
		// Charging: the cracks and veins glow up before each attack.
		var charge = act switch
		{
			Act.Slam => a < 0.9f ? a / 0.9f : 0f,
			Act.Horn => a / HornWindUp,
			Act.Beam => a < BeamRear + BeamCharge ? a / (BeamRear + BeamCharge) : 0.5f,
			Act.Dig or Act.Buried => 0.6f,
			_ => 0f,
		};
		foreach ( var p in towerPaints ) { p.SetShaderParameter( "charge", charge * (Enraged ? 1.3f : 1f) ); p.SetShaderParameter( "flash", flash * 0.4f ); }
		shell.SetShaderParameter( "flash", flash );
		softSkin.SetShaderParameter( "flash", flash );
		softSkin.SetShaderParameter( "glow", BellyOpen ? 2f + MathF.Sin( t * 8f ) * 0.5f : 0.5f + charge );
		hornPaint.SetShaderParameter( "heat", act == Act.Horn ? SmoothKit.Ease( a / HornWindUp ) : 0f );
		// The lamp focuses for the beam and goes out as he dies.
		var focusTo = act == Act.Beam && a >= BeamRear ? 1f : 0f;
		focus = MathX.Lerp( focus, focusTo, 1f - MathF.Exp( -5f * dt ) );
		var power = act == Act.Dead ? MathF.Max( 0f, 1f - dying * 1.1f ) * (Rng.Float() < 0.3f ? 0.3f : 1f) : 1f;
		lampPaint.SetShaderParameter( "focus", focus );
		lampPaint.SetShaderParameter( "power", power );
		lampPaint.SetShaderParameter( "flash", flash );
		lampLight.LightEnergy = 2.5f * power * (1f + focus);
		if ( bellyPart is not null ) bellyPart.Collider.Disabled = !BellyOpen;

		DrawSweep( dt, power );
		DrawBeam( power );
		DrawCue( a );
		// Ploughing through the sand while buried: puffs round the lamp room and a furrow behind.
		if ( act is Act.Dig or Act.Buried && sincePuff > 0.06f )
		{
			sincePuff = 0f;
			var at = GlobalPosition with { Y = Sculpt.GroundAt( GlobalPosition ) is var g && g != float.MinValue ? g : ground };
			var top = tower.GlobalTransform * new Vector3( 0f, TowerTop, 0f );
			at = top with { Y = at.Y };
			for ( var i = 0; i < 3; i++ )
				GpuFx.SandPuff( at + Rng.OnSphere().Flat().SafeNormal() * Rng.Float( 0.8f, 2.4f ) + Vector3.Up * 0.05f, (Vector3.Up * 0.8f + Rng.InSphere( 0.5f ).Flat()) * 1.2f, SeaStir.SiltColour( at ), Rng.Float( 8f, 14f ), Rng.Float( 0.7f, 1.2f ) );
		}
		// The horn: bubbles shaking out of it as it winds up, steam when it jams.
		if ( act == Act.Horn && Rng.Float() < 0.4f + a / HornWindUp * 0.5f ) WaterBubble.Create( HornAt + Rng.InSphere( 0.3f ), Rng.Float( 0.1f, 0.3f ), 0f, Rng.Float( 0.4f, 0.8f ) );
		if ( act == Act.Jammed && Rng.Float() < 0.5f ) GpuFx.SandPuff( HornAt, Vector3.Up * 2f + Rng.InSphere( 0.6f ), new Color( 0.85f, 0.88f, 0.9f, 0.6f ), Rng.Float( 10f, 18f ), Rng.Float( 0.6f, 1f ) );
	}

	/// <summary> The lighthouse sweep: a cone of light turning round, dipped to light the sand; it swings onto the mark as the beam focuses. </summary>
	void DrawSweep( float dt, float power )
	{
		if ( sweep is null ) return;
		var from = LampAt;
		sweepYaw += dt * (Enraged ? 1.1f : 0.75f);
		var dir = new Vector3( MathF.Sin( sweepYaw ), -0.32f, MathF.Cos( sweepYaw ) ).Normalized();
		var a = (float)sinceAct;
		var width = 3.4f;
		if ( act == Act.Beam && a >= BeamRear * 0.5f )
		{
			// Swinging onto the mark and narrowing (it stays as a soft halo round the burning core).
			var onto = (beamAt + Vector3.Up * 0.3f - from).SafeNormal();
			var k = SmoothKit.Ease( (a - BeamRear * 0.5f) / (BeamRear * 0.5f + BeamCharge * 0.6f) );
			dir = dir.Lerp( onto, k ).SafeNormal();
			width = MathX.Lerp( 3.4f, 0.7f, k );
			sweepYaw = MathF.Atan2( dir.X, dir.Z );
		}
		from += dir * 1.2f;   // out through the glass, clear of the gallery deck
		var far = from + dir * 34f;
		var hit = Trace.Ray( this, from, far, Layers.World );
		var end = hit.Hit ? hit.HitPosition : far;
		sweepCone.Visible = power > 0.02f;
		Span( sweepCone, from, end, 0.08f, width );
		conePaint.SetShaderParameter( "power", power * (1f + focus * 0.6f) );
		sweepLight.GlobalTransform = new Transform3D( Basis.LookingAt( dir, MathF.Abs( dir.Y ) > 0.95f ? Vector3.Forward : Vector3.Up ), from );
		sweepLight.LightEnergy = 5f * power * (1f + focus);
		sweepLight.SpotAngle = MathX.Lerp( 11f, 4f, focus );
		// The lamp's eye looks along its beam.
		if ( lampEye is not null && act != Act.Dead ) lampEye.GlobalBasis = Basis.LookingAt( dir, MathF.Abs( dir.Y ) > 0.95f ? Vector3.Forward : Vector3.Up ) * Basis.FromScale( Vector3.One );
	}

	void DrawBeam( float power )
	{
		if ( beam is null ) return;
		if ( !Burning ) { beam.Visible = false; return; }
		beam.Visible = true;
		var (from, end) = BeamLine();
		var pulse = 1f + MathF.Sin( (float)Clock.Now * 35f ) * 0.12f;
		Span( beamCore, from, end, 0.18f * pulse * (Enraged ? 1.2f : 1f) );
		Span( beamGlow, from, end, 0.55f * pulse * (Enraged ? 1.2f : 1f) );
		corePaint.SetShaderParameter( "power", 0.9f * power );
		beamLight.LightEnergy = 4f * pulse;
		beamLight.GlobalPosition = end + (from - end).SafeNormal() * 0.6f;
		if ( Rng.Float() < 0.7f ) GpuFx.SandPuff( end + Vector3.Up * 0.1f, (Rng.OnSphere().Flat() + Vector3.Up).SafeNormal() * Rng.Float( 1.5f, 4f ), new Color( 1f, 0.85f, 0.45f, 0.8f ), Rng.Float( 4f, 8f ), Rng.Float( 0.4f, 0.8f ) );
		if ( Rng.Float() < 0.5f ) WaterBubble.Create( end + Rng.InSphere( 0.5f ), Rng.Float( 0.2f, 0.5f ), 0f, Rng.Float( 0.5f, 1f ) );
	}

	/// <summary> Where he will burst up: a ring on the sand, pulsing faster and redder as it comes. </summary>
	void DrawCue( float a )
	{
		if ( cue is null ) return;
		if ( act != Act.Erupt || a >= EruptCue ) { cue.Visible = false; return; }
		cue.Visible = true;
		cueMesh.ClearSurfaces();
		cueMesh.SurfaceBegin( Mesh.PrimitiveType.Triangles );
		var k = MathX.Clamp( a / EruptCue, 0f, 1f );
		var pulse = 0.6f + 0.4f * MathF.Sin( a * (10f + k * 25f) );
		var colour = new Color( 1f, MathX.Lerp( 0.85f, 0.25f, k ), 0.3f, pulse );
		GroundRing.Flat( cueMesh, this, eruptAt, EruptRadius - 0.35f, EruptRadius, colour, 48 );
		GroundRing.Flat( cueMesh, this, eruptAt, 0f, EruptRadius * MathX.Lerp( 0.15f, 1f, k ), colour with { A = colour.A * 0.3f }, 36 );
		cueMesh.SurfaceEnd();
	}
}

/// <summary>
/// The Foghorn Blast's fog: a bank of thick fog that rolls out from where the horn blew, holds, then thins. A player inside
/// it (on their own machine) sees only a few metres (the level's depth fog pulled in and greyed), so only the lamp, which
/// ignores fog, shows. Puffs billow along its edge so it reads from outside too. Every copy runs its own from the host's
/// state; <see cref="Amount"/> is how thick it is (the hermit hunts harder while it is up).
/// </summary>
public partial class FogBank : Node3D
{
	public static readonly List<FogBank> All = new();
	LighthouseHermit owner;
	Vector3 centre;
	float radius, life, age, lifted = -1f;
	float local;
	TimeSince sincePuff = 99f;
	/// <summary> Tests: how thick the local player's fog got. </summary>
	public static float LocalPeak;

	public float Strength
	{
		get
		{
			var k = SmoothKit.Ease( age / 2f ) * MathX.Clamp( (life - age) / 2.5f, 0f, 1f );
			if ( lifted >= 0f ) k *= MathX.Clamp( 1f - lifted / 1.5f, 0f, 1f );
			return k;
		}
	}
	public float Radius => radius * SmoothKit.Ease( age / 2.2f );
	public Vector3 Centre => centre;

	static FogBank Of( LighthouseHermit h ) => All.FirstOrDefault( f => f.owner == h );
	public static float Amount( LighthouseHermit h ) => Of( h )?.Strength ?? 0f;

	public static FogBank Roll( LighthouseHermit h, Vector3 at, float radius, float time )
	{
		var bank = Of( h );
		if ( bank is null )
		{
			bank = new FogBank { Name = "Fog Bank", owner = h };
			h.GetTree().CurrentScene.AddChild( bank );
		}
		else bank.age = MathF.Min( bank.age, 2f );
		bank.centre = at;
		bank.radius = radius;
		bank.life = time;
		bank.lifted = -1f;
		return bank;
	}

	/// <summary> He died: it thins out over a second and a half. </summary>
	public static void Lift( LighthouseHermit h ) { if ( Of( h ) is { } b && b.lifted < 0f ) b.lifted = 0f; }
	public static void Clear( LighthouseHermit h ) { if ( Of( h ) is { } b ) b.QueueFree(); }

	public override void _Ready() { All.Add( this ); TopLevel = true; }
	public override void _ExitTree()
	{
		All.Remove( this );
		if ( All.Count == 0 ) Restore();
	}

	public override void _Process( double delta )
	{
		// Real game time (not capped per frame), so a slow frame never makes the fog outstay its time.
		var dt = (float)delta;
		age += dt;
		if ( lifted >= 0f ) lifted += dt;
		if ( age >= life || lifted >= 1.5f ) { QueueFree(); return; }
		var k = Strength;
		var r = Radius;
		// Billows along the edge, and slow wisps drifting inside near the local player.
		if ( k > 0.15f && sincePuff > 0.035f )
		{
			sincePuff = 0f;
			var angle = Rng.Float( 0f, MathF.Tau );
			var at = centre + new Vector3( MathF.Cos( angle ), 0f, MathF.Sin( angle ) ) * r * Rng.Float( 0.85f, 1.05f );
			var sand = Sculpt.GroundAt( at );
			at.Y = (sand != float.MinValue ? sand : centre.Y) + Rng.Float( 0.4f, 5f );
			GpuFx.SandPuff( at, new Vector3( MathF.Cos( angle ), 0.1f, MathF.Sin( angle ) ) * Rng.Float( 0.3f, 0.9f ), new Color( 0.66f, 0.72f, 0.74f, 0.5f * k ), Rng.Float( 120f, 200f ), Rng.Float( 2.5f, 3.8f ) );
			if ( PlayerSurvivor.Local?.Controller is { } me && local > 0.3f )
			{
				var near = me.GlobalPosition + Rng.InSphere( 9f ).Flat() + Vector3.Up * Rng.Float( 0.5f, 3f );
				GpuFx.SandPuff( near, Rng.InSphere( 0.3f ), new Color( 0.7f, 0.75f, 0.77f, 0.25f * k ), Rng.Float( 80f, 130f ), Rng.Float( 2.5f, 3.5f ) );
			}
		}
		// How deep in it the local player is.
		local = 0f;
		if ( PlayerSurvivor.Local?.Controller is { } c )
		{
			var d = (c.GlobalPosition - centre).Flat().Length();
			local = k * (1f - MathX.Clamp( (d - (r - 4f)) / 7f, 0f, 1f ));
		}
		LocalPeak = MathF.Max( LocalPeak, local );
		Apply();
	}

	// ---- The local player's view: the level's depth fog pulled in while they are in a bank ----

	sealed class Saved { public Color Colour, VolAlbedo; public float Begin, End, VolDensity; }
	static Saved saved;
	static Godot.Environment env;
	static readonly Color FogGrey = new( 0.36f, 0.43f, 0.45f );

	static Godot.Environment Env()
	{
		if ( env is not null && GodotObject.IsInstanceValid( env ) ) return env;
		var scene = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		env = scene?.FindChildren( "*", "WorldEnvironment", true, false ).OfType<WorldEnvironment>().FirstOrDefault()?.Environment;
		return env;
	}

	static void Apply()
	{
		var k = 0f;
		foreach ( var b in All ) k = MathF.Max( k, b.local );
		var e = Env();
		if ( e is null ) return;
		if ( k <= 0.001f ) { Restore(); return; }
		saved ??= new Saved { Colour = e.FogLightColor, Begin = e.FogDepthBegin, End = e.FogDepthEnd, VolDensity = e.VolumetricFogDensity, VolAlbedo = e.VolumetricFogAlbedo };
		e.FogLightColor = saved.Colour.Lerp( FogGrey, k );
		e.FogDepthBegin = Mathf.Lerp( saved.Begin, 0.5f, k );
		e.FogDepthEnd = Mathf.Lerp( saved.End, 8f, k );
		e.VolumetricFogDensity = Mathf.Lerp( saved.VolDensity, 0.09f, k );
		e.VolumetricFogAlbedo = saved.VolAlbedo.Lerp( new Color( 0.75f, 0.8f, 0.82f ), k );
	}

	static void Restore()
	{
		if ( saved is null ) return;
		var e = Env();
		if ( e is not null )
		{
			e.FogLightColor = saved.Colour;
			e.FogDepthBegin = saved.Begin;
			e.FogDepthEnd = saved.End;
			e.VolumetricFogDensity = saved.VolDensity;
			e.VolumetricFogAlbedo = saved.VolAlbedo;
		}
		saved = null;
	}

	/// <summary> The local player's fog right now (0 to 1). </summary>
	public static float Local => All.Count == 0 ? 0f : All.Max( b => b.local );
}

/// <summary>
/// The glowing trail the Lantern Beam burns into the sand: marks that fade over a few seconds and burn a player standing
/// on one (on the host; a joiner's copy only draws them).
/// </summary>
public partial class LampTrail : Node3D
{
	public static readonly List<LampTrail> All = new();
	const float Life = 3.5f, Radius = 0.9f, Spacing = 0.55f;
	LighthouseHermit owner;
	readonly List<(Vector3 At, float Born)> marks = new();
	readonly Dictionary<PlayerSurvivor, float> burned = new();
	ImmediateMesh mesh;
	float dps, clock;

	public int Marks => marks.Count;

	public static void Leave( LighthouseHermit h, Vector3 at, float dps )
	{
		var trail = All.FirstOrDefault( t => t.owner == h );
		if ( trail is null )
		{
			trail = new LampTrail { Name = "Lamp Trail", owner = h };
			h.GetTree().CurrentScene.AddChild( trail );
		}
		trail.dps = dps;
		if ( trail.marks.Count > 0 && trail.marks[^1].At.DistanceTo( at ) < Spacing ) return;
		var sand = Sculpt.GroundAt( at );
		if ( sand != float.MinValue && MathF.Abs( sand - at.Y ) < 1.5f ) at.Y = sand;
		trail.marks.Add( (at, trail.clock) );
	}

	public override void _Ready()
	{
		All.Add( this );
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		mesh = new ImmediateMesh();
		var paint = new ShaderMaterial { Shader = new Shader { Code = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
void fragment() { ALBEDO = COLOR.rgb * COLOR.a; }" } };
		paint.SetMeta( "no_caustics", true );
		AddChild( new MeshInstance3D { Name = "Trail", Mesh = mesh, MaterialOverride = paint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
	}

	public override void _ExitTree() => All.Remove( this );

	public override void _Process( double delta )
	{
		var dt = MathF.Min( (float)delta, 0.1f );
		clock += dt;
		marks.RemoveAll( m => clock - m.Born > Life );
		if ( marks.Count == 0 && (owner is null || !IsInstanceValid( owner ) || !owner.Burning) ) { QueueFree(); return; }
		mesh.ClearSurfaces();
		if ( marks.Count > 0 )
		{
			mesh.SurfaceBegin( Mesh.PrimitiveType.Triangles );
			foreach ( var (at, born) in marks )
			{
				var k = 1f - (clock - born) / Life;
				var flicker = 0.8f + 0.2f * MathF.Sin( clock * 20f + at.X * 3f );
				GroundRing.Flat( mesh, this, at, 0f, Radius * (0.6f + 0.4f * k), new Color( 1f, MathX.Lerp( 0.45f, 0.85f, k ), 0.3f, 0.55f * k * flicker ), 14 );
			}
			mesh.SurfaceEnd();
		}
		if ( dps <= 0f ) return;
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c ) continue;
			var feet = p.RealPosition;
			var on = false;
			foreach ( var (at, born) in marks )
				if ( (feet - at).Flat().Length() < Radius * 0.9f + c.BodyRadius * 0.5f && feet.Y - at.Y < 0.8f && feet.Y - at.Y > -0.8f ) { on = true; break; }
			if ( !on ) continue;
			burned.TryGetValue( p, out var sum );
			sum += dps * dt;
			if ( sum >= 4f )
			{
				p.Health?.OnDamage( new DamageInfo { Damage = sum, Attacker = IsInstanceValid( owner ) ? owner : null, Position = feet + Vector3.Up * 0.2f, Origin = feet } );
				LighthouseHermit.TrailHits++;
				sum = 0f;
			}
			burned[p] = sum;
		}
	}
}
