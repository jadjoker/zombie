using Godot;

/// <summary>
/// The skill trees' numbers (what one point of each does) and the effects that are their own things: burning and
/// bleeding, the Full Choke slug, the Phase Shift decoy, the toxic trail, the Kraken's Blessing whirlpool. The Black
/// Hole is its own class below. The hooks live where each weapon does its work (PlayerWeapon, Jetpack, GoreVac,
/// BubbleCannon, PlayerMelee).
/// </summary>
public static class SkillFx
{
	// Ram-Bot
	public const float DeepMagPer = 0.15f, HeadhunterPer = 0.12f, OverclockRate = 0.5f;
	public static int OverclockShots( int n ) => 2 + 4 * n;
	public const float PointBlankPer = 0.10f, PointBlankRange = 5f, ShellShufflePer = 0.10f, KickbackPer = 0.4f;
	public const float BurnPer = 6f, BurnTime = 3f, LimbPer = 0.15f;
	public const int ChokeEvery = 4;
	public static float ChokeScale( int n ) => 4f + 2f * n;
	public const float PlatingPer = 20f, RamDamagePer = 0.15f, RechargePer = 0.12f, AxePer = 0.2f, AxeHealPer = 2f;
	public static float UnstoppableRefund( int n ) => 10f + 10f * n;
	// Trident-Bot
	public const float QuickDrawPer = 0.08f, RiptidePer = 0.12f, TidalPer = 30f, TidalRadius = 3f;
	public static float ForkShare( int n ) => 0.25f + 0.25f * n;
	public const float ForkDegrees = 7f;
	public const float LongSpearPer = 0.1f, BleedPer = 4f, BleedTime = 4f, BashPer = 12f, BashReach = 2.6f, BashCooldown = 1.2f;
	public const float RipostePer = 0.3f, MirrorPer = 0.12f;
	public static float SpartanDps( int n ) => 20f * n;
	public const float SeaLegsPer = 0.3f, LungePer = 0.12f, PlecoPer = 0.2f;
	public static float UndertowReach( int n ) => 1.5f + 0.4f * n;
	public static float WhirlTime( int n ) => 1f + n;
	public const float WhirlDps = 15f, WhirlRadius = 4f;
	// Vac-Bot
	public const int BellyPer = 20;
	public const float SuctionPer = 0.12f, GourmetPer = 0.15f, ShredderPer = 0.03f, BoomsPer = 0.12f;
	public const float LastingPer = 1f, LiftPer = 0.15f, ChainPer = 0.15f;
	public static float FizzDamage( int n ) => 15f + 5f * n;
	public static float PrisonScale( int n ) => n <= 0 ? 1f : 1.05f + 0.15f * n;
	public const float JetsPer = 0.1f, AfterburnerPer = 0.15f, SlipPer = 0.1f, SlipTime = 2f;
	public static float ToxicDps( int n ) => 6f * n;
	public static float PhaseTime( int n ) => 0.2f + 0.2f * n;
	public static float DecoyTime( int n ) => 1f + n;

	/// <summary> Tests: how often each effect went off. </summary>
	public static int Burns, Bleeds, Slugs, SlugHits, Decoys, Whirlpools, Reflects, Bashes, Fizzes, Overclocked, Forks, TidalBursts;

	/// <summary> The creature a part belongs to, if it is a living enemy. </summary>
	public static Creature EnemyOf( Node hit ) => (Shootables.Find( hit )?.ShotOwner as Creature) is { IsEnemy: true, IsDead: false } c ? c : null;

	/// <summary> The nearest live part of a creature to a point (for effects that land on its body). </summary>
	public static Vector3 NearestPart( Creature c, Vector3 at )
	{
		var best = c.GlobalPosition + Vector3.Up * 0.4f;
		var bestD = float.MaxValue;
		foreach ( var p in CreaturePart.All )
		{
			if ( p.Creature != c || !GodotObject.IsInstanceValid( p ) ) continue;
			var d = p.GlobalPosition.DistanceSquaredTo( at );
			if ( d < bestD ) { bestD = d; best = p.GlobalPosition; }
		}
		return best;
	}

	/// <summary> Shoves an enemy (lighter ones further). Bosses don't move. </summary>
	public static void Shove( Creature c, Vector3 delta )
	{
		if ( c is null || !c.Pullable || c.IsDead ) return;
		c.Pull( delta * MathX.Clamp( 200f / MathF.Max( 60f, c.Weight ), 0.3f, 1.2f ) );
	}

	/// <summary>
	/// Full Choke: a slug down the barrel that goes through every enemy on its line (up to eight) until the world
	/// stops it, with a gold streak to show where it went.
	/// </summary>
	public static void Slug( PlayerController shooter, Vector3 start, Vector3 direction, Vector3 trailFrom, float damage, float range )
	{
		Slugs++;
		var exclude = new Godot.Collections.Array<Rid> { shooter.GetRid() };
		var end = start + direction * range;
		var from = start;
		for ( var i = 0; i < 8; i++ )
		{
			var tr = Trace.Ray( shooter, from, end, Layers.World | Layers.Creature, exclude );
			if ( !tr.Hit ) break;
			var shootable = Shootables.Find( tr.Node );
			if ( shootable?.ShotOwner is Creature c && !shootable.IsDead )
			{
				SlugHits++;
				shootable.ReceiveShot( damage, tr.Node, direction, tr.HitPosition, start, shooter, true );
				Fx.ImpactFlesh( tr.HitPosition, -direction, new Color( 1f, 0.8f, 0.3f ) );
				foreach ( var p in CreaturePart.All ) if ( p.Creature == c && GodotObject.IsInstanceValid( p ) ) exclude.Add( p.GetRid() );
				from = tr.HitPosition;
				continue;
			}
			if ( shootable is not null ) { shootable.ReceiveShot( damage, tr.Node, direction, tr.HitPosition, start, shooter, true ); }
			end = tr.HitPosition;
			Fx.ImpactWorld( tr.HitPosition, tr.Normal );
			break;
		}
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( root is null ) return;
		var streak = new Node3D { Name = "Slug Streak" };
		root.AddChild( streak );
		var glow = Sculpt.Glow( new Color( 1f, 0.78f, 0.3f ), 5f );
		glow.SetMeta( "no_caustics", true );
		Sculpt.Limb( streak, trailFrom, end, 0.025f, 0.012f, glow, "Streak" );
		Fx.Expire( streak, 0.12f );
		if ( WaterFeel.IsOn() ) WaterBubble.Trail( trailFrom, end );
		Sfx.Play2D( "harpoon", 0.5f, 1.5f, Sfx.GunBus );
	}

	/// <summary> A blink's toxic goo left behind (Toxic Trail): spots that hurt enemies standing in them. </summary>
	public static void DropToxic( PlayerController owner, Vector3 at, float dps, Color colour )
	{
		ToxicTrail.Get( owner )?.Add( at, dps );
		GpuFx.SandPuff( at + Rng.InSphere( 0.2f ), Vector3.Up * 0.4f + Rng.InSphere( 0.4f ), colour with { A = 0.8f }, Rng.Float( 6f, 12f ), 1.2f );
	}

	/// <summary> A bright bolt from one point to another (Mirror Shield, Fizz). </summary>
	public static void Zap( Vector3 from, Vector3 to )
	{
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( root is null ) return;
		LightningArc.Spawn( root, from, to );
		LightningArc.Spark( root, to );
	}

	/// <summary> The flat whirling disc look (the whirlpool, the black hole's accretion disc): spiral arms, hot inside. </summary>
	public static ShaderMaterial Disc( Color hot, Color mid, Color edge, float inner )
	{
		var m = new ShaderMaterial { Shader = discShader ??= new Shader { Code = DiscCode } };
		m.SetShaderParameter( "hot", hot );
		m.SetShaderParameter( "mid", mid );
		m.SetShaderParameter( "edge", edge );
		m.SetShaderParameter( "inner", inner );
		m.SetMeta( "no_caustics", true );
		return m;
	}
	static Shader discShader;
	const string DiscCode = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
uniform vec3 hot : source_color = vec3(1.0, 0.9, 0.6);
uniform vec3 mid : source_color = vec3(1.0, 0.4, 0.2);
uniform vec3 edge : source_color = vec3(0.5, 0.2, 1.0);
uniform float inner = 0.16;
uniform float spin = 0.0;
uniform float fade = 1.0;
uniform float arms = 3.0;
void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);
	float a = atan(p.y, p.x);
	float lr = log(max(r, 0.01));
	float band = smoothstep(inner, inner + 0.07, r) * (1.0 - smoothstep(0.72, 1.0, r));
	float big = 0.5 + 0.5 * sin(a * arms - lr * 7.0 + spin);
	float fine = 0.65 + 0.35 * sin(a * (arms * 4.0 - 1.0) - lr * 19.0 + spin * 1.6);
	float t = clamp((r - inner) / (1.0 - inner), 0.0, 1.0);
	vec3 c = mix(hot, mid, smoothstep(0.0, 0.3, t));
	c = mix(c, edge, smoothstep(0.3, 1.0, t));
	float hotRim = exp(-pow((r - inner - 0.04) / 0.035, 2.0)) * 1.5;
	float glow = band * (0.25 + 0.75 * big) * fine * (1.25 - t) + hotRim * band;
	ALBEDO = c * glow * 2.0 * fade;
	ALPHA = clamp(glow * fade, 0.0, 1.0);
}";

	/// <summary> A glowing rim (fresnel) for spheres: the black hole's horizon, the decoy's hologram. </summary>
	public static ShaderMaterial Rim( Color colour, float power, float scan = 0f )
	{
		var m = new ShaderMaterial { Shader = rimShader ??= new Shader { Code = RimCode } };
		m.SetShaderParameter( "rim", colour );
		m.SetShaderParameter( "power", power );
		m.SetShaderParameter( "scan", scan );
		m.SetMeta( "no_caustics", true );
		return m;
	}
	static Shader rimShader;
	const string RimCode = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never, shadows_disabled, fog_disabled;
uniform vec3 rim : source_color = vec3(0.6, 0.3, 1.0);
uniform float power = 3.0;
uniform float fade = 1.0;
uniform float scan = 0.0;
varying vec3 world;
void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
	float f = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), power);
	float lines = scan > 0.0 ? 0.55 + 0.45 * step(0.5, fract(world.y * 14.0 - TIME * 2.0)) : 1.0;
	float body = scan > 0.0 ? 0.25 : 0.0;
	float v = (f + body) * lines * fade;
	ALBEDO = rim * v * 2.5;
	ALPHA = clamp(v, 0.0, 1.0);
}";
}

/// <summary>
/// Burning (Dragon's Breath) or bleeding (Barbed Tip): damage a second for a few seconds, ticking twice a second. A
/// new dose tops the time back up and keeps the stronger rate. Flames or drips show on the creature.
/// </summary>
public partial class Affliction : Node
{
	public enum Kind { Burn, Bleed }
	Kind kind;
	float dps, left, tick;
	Creature victim;
	PlayerController shooter;
	TimeSince sinceFx;

	public static void Apply( Creature victim, Kind kind, float dps, float seconds, PlayerController shooter )
	{
		if ( victim is null || victim.IsDead || dps <= 0f ) return;
		var name = kind == Kind.Burn ? "Burning" : "Bleeding";
		if ( victim.GetNodeOrNull<Affliction>( name ) is { } on )
		{
			on.left = MathF.Max( on.left, seconds );
			on.dps = MathF.Max( on.dps, dps );
			return;
		}
		if ( kind == Kind.Burn ) SkillFx.Burns++; else SkillFx.Bleeds++;
		victim.AddChild( new Affliction { Name = name, kind = kind, dps = dps, left = seconds, tick = 0.5f, victim = victim, shooter = shooter } );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( victim is null || !IsInstanceValid( victim ) || victim.IsDead ) { QueueFree(); return; }
		left -= dt;
		tick -= dt;
		var at = SkillFx.NearestPart( victim, victim.GlobalPosition + Vector3.Up );
		if ( tick <= 0f )
		{
			tick += 0.5f;
			PlayerPerks.Hurt( victim, dps * 0.5f, at, at, IsInstanceValid( shooter ) ? shooter : null );
		}
		if ( sinceFx > 0.06f )
		{
			sinceFx = 0f;
			if ( kind == Kind.Burn )
				GpuFx.SandPuff( at + Rng.InSphere( 0.25f ), Vector3.Up * Rng.Float( 1f, 2f ) + Rng.InSphere( 0.3f ), Rng.Float() < 0.5f ? new Color( 1f, 0.55f, 0.1f ) : new Color( 1f, 0.85f, 0.3f ), Rng.Float( 5f, 10f ), 0.45f );
			else
				GooDroplet.Spawn( at + Rng.InSphere( 0.15f ), Vector3.Down * 0.5f + Rng.InSphere( 0.6f ), new Color( 0.75f, 0.06f, 0.08f ), Rng.Float( 0.8f, 1.5f ) );
		}
		if ( left <= 0f ) QueueFree();
	}
}

/// <summary> Toxic Trail: the spots a blink left, and the enemies standing in them hurt four times a second. </summary>
public partial class ToxicTrail : Node
{
	const float Life = 4f, Reach = 1.3f;
	readonly List<(Vector3 At, float Dps, double Until)> spots = new();
	PlayerController owner;
	float tick;
	public static int Hits;

	public static ToxicTrail Get( PlayerController owner )
	{
		if ( owner is null ) return null;
		if ( owner.GetTree().CurrentScene.GetNodeOrNull<ToxicTrail>( "ToxicTrail" ) is { } t ) return t;
		var trail = new ToxicTrail { Name = "ToxicTrail", owner = owner };
		owner.GetTree().CurrentScene.AddChild( trail );
		return trail;
	}

	public void Add( Vector3 at, float dps ) => spots.Add( (at, dps, Clock.Now + Life) );

	public override void _Process( double delta )
	{
		spots.RemoveAll( s => s.Until < Clock.Now );
		if ( spots.Count == 0 ) return;
		tick -= (float)delta;
		if ( tick > 0f ) return;
		tick = 0.25f;
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var best = 0f;
			foreach ( var s in spots ) if ( s.At.DistanceTo( c.GlobalPosition ) < Reach + 0.4f && s.Dps > best ) best = s.Dps;
			if ( best <= 0f ) continue;
			Hits++;
			PlayerPerks.Hurt( c, best * 0.25f, c.GlobalPosition, c.GlobalPosition, IsInstanceValid( owner ) ? owner : null );
		}
	}
}

/// <summary>
/// Phase Shift's decoy: a flickering violet hologram of the robot left where the blink started. While it stands,
/// enemies go for it (`PlayerSurvivor.WorldPosition` is here), and their hits on it don't reach the player.
/// </summary>
public partial class Decoy : Node3D
{
	float life, age;
	readonly List<ShaderMaterial> materials = new();

	public static Decoy Spawn( PlayerController owner, Vector3 at, float seconds )
	{
		SkillFx.Decoys++;
		var d = new Decoy { Name = "Decoy", life = seconds };
		owner.GetTree().CurrentScene.AddChild( d );
		d.GlobalPosition = at;
		d.GlobalBasis = new Basis( Vector3.Up, owner.EyeAngles.Yaw * MathF.PI / 180f + MathF.PI );
		d.Build();
		Sfx.Play( "ghost_appear", at, 0.6f, 1.3f );
		return d;
	}

	void Build()
	{
		var colour = new Color( 0.62f, 0.35f, 1f );
		ShaderMaterial M() { var m = SkillFx.Rim( colour, 2f, 1f ); materials.Add( m ); return m; }
		Sculpt.Limb( this, new Vector3( 0f, 0.5f, 0f ), new Vector3( 0f, 1.25f, 0f ), 0.26f, 0.3f, M(), "Body" );
		Sculpt.Blob( this, new Vector3( 0f, 1.6f, 0f ), Vector3.One * 0.36f, M(), null, "Head" );
		Sculpt.Limb( this, new Vector3( -0.34f, 1.25f, 0f ), new Vector3( -0.42f, 0.75f, -0.1f ), 0.08f, 0.07f, M(), "Arm" );
		Sculpt.Limb( this, new Vector3( 0.34f, 1.25f, 0f ), new Vector3( 0.42f, 0.75f, -0.1f ), 0.08f, 0.07f, M(), "Arm" );
		Sculpt.Limb( this, new Vector3( -0.13f, 0.5f, 0f ), new Vector3( -0.15f, 0.02f, 0f ), 0.09f, 0.08f, M(), "Leg" );
		Sculpt.Limb( this, new Vector3( 0.13f, 0.5f, 0f ), new Vector3( 0.15f, 0.02f, 0f ), 0.09f, 0.08f, M(), "Leg" );
		AddChild( new OmniLight3D { LightColor = colour, LightEnergy = 1.2f, OmniRange = 3f, ShadowEnabled = false, Position = Vector3.Up * 1f } );
	}

	public bool Gone => age >= life;

	/// <summary> An enemy struck it: a flicker and a spark. </summary>
	public void Struck()
	{
		age = MathF.Min( life, age + 0.1f );
		LightningArc.Spark( GetTree().CurrentScene, GlobalPosition + Vector3.Up * Rng.Float( 0.5f, 1.5f ) );
	}

	public override void _Process( double delta )
	{
		age += (float)delta;
		var fade = MathX.Clamp( MathF.Min( age / 0.15f, (life - age) / 0.3f ), 0f, 1f ) * (Rng.Float() < 0.06f ? 0.4f : 1f);
		foreach ( var m in materials ) m.SetShaderParameter( "fade", fade );
		Position += Vector3.Up * MathF.Sin( age * 3f ) * 0.002f;
		if ( age >= life ) QueueFree();
	}
}

/// <summary>
/// Kraken's Blessing: a whirlpool left where a surge ends. Enemies inside are swept round and held toward the middle,
/// taking `SkillFx.WhirlDps`. A spinning disc on the water and bubbles spiralling in.
/// </summary>
public partial class SurgeWhirlpool : Node3D
{
	float life, age, tick;
	PlayerController owner;
	MeshInstance3D disc;
	ShaderMaterial material;
	TimeSince sinceBubble;

	public static void Spawn( PlayerController owner, Vector3 at, float seconds )
	{
		SkillFx.Whirlpools++;
		var w = new SurgeWhirlpool { Name = "Surge Whirlpool", life = seconds, owner = owner };
		owner.GetTree().CurrentScene.AddChild( w );
		var floor = Trace.Ray( owner, at + Vector3.Up * 0.5f, at + Vector3.Down * 6f, Layers.World, new Godot.Collections.Array<Rid> { owner.GetRid() } );
		w.GlobalPosition = (floor.Hit ? floor.HitPosition : at) + Vector3.Up * 0.15f;
		w.Build();
		Sfx.Play( "whirlpool", w.GlobalPosition, 0.7f, 1.2f );
	}

	void Build()
	{
		material = SkillFx.Disc( new Color( 0.8f, 1f, 1f ), new Color( 0.2f, 0.8f, 0.85f ), new Color( 0.45f, 0.25f, 0.9f ), 0.06f );
		material.SetShaderParameter( "arms", 4f );
		disc = new MeshInstance3D { Mesh = new QuadMesh { Size = Vector2.One * SkillFx.WhirlRadius * 2f }, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( disc );
		disc.RotationDegrees = new Vector3( -90f, 0f, 0f );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		age += dt;
		var fade = MathX.Clamp( MathF.Min( age / 0.25f, (life - age) / 0.4f ), 0f, 1f );
		material.SetShaderParameter( "fade", fade * 0.8f );
		material.SetShaderParameter( "spin", age * 9f );
		disc.Scale = Vector3.One * (0.6f + 0.4f * fade);
		var centre = GlobalPosition;
		tick -= dt;
		var hurt = tick <= 0f;
		if ( hurt ) tick = 0.25f;
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var to = (centre - c.GlobalPosition).Flat();
			var d = to.Length();
			if ( d > SkillFx.WhirlRadius ) continue;
			var tangent = new Vector3( -to.Z, 0f, to.X ).SafeNormal();
			if ( c.Pullable ) c.Pull( (to.SafeNormal() * MathF.Min( d, 3f * dt ) + tangent * 2.2f * dt) * fade );
			if ( hurt ) PlayerPerks.Hurt( c, SkillFx.WhirlDps * 0.25f, c.GlobalPosition, centre, IsInstanceValid( owner ) ? owner : null );
		}
		if ( sinceBubble > 0.03f )
		{
			sinceBubble = 0f;
			var a = Rng.Float( 0f, MathF.Tau );
			WaterBubble.Create( centre + new Vector3( MathF.Cos( a ), Rng.Float( 0f, 0.6f ), MathF.Sin( a ) ) * Rng.Float( 0.5f, SkillFx.WhirlRadius ), Rng.Float( 0.4f, 1.2f ), 0f, Rng.Float( 0.4f, 0.9f ) );
		}
		if ( age >= life ) QueueFree();
	}
}

/// <summary>
/// The Black Hole (Vac-Bot's Gore Vac capstone): the huge shot doesn't just burst. It tears open a black hole where it
/// lands: a pitch black core with a violet horizon, a whirling accretion disc, light bent round it (the view behind
/// swirls into it), the lights round about going dim. For its life it drags every enemy in reach (`Reach`) round and
/// in to one spot, crushes what is in the middle, swallows loose gibs and puddles, and sucks silt in from all round.
/// Then it collapses, and the huge shot's burst goes off where everything was pulled to, `DamageScale` harder, with
/// a shock ring and a flash.
/// </summary>
public partial class BlackHole : Node3D
{
	public static float Life( int n ) => 1.4f + 0.5f * n;
	public static float Reach( int n ) => 7f + 1.5f * n;
	public static float DamageScale( int n ) => 1.25f + 0.25f * n;
	const float Form = 0.35f, Collapse = 0.3f, CoreSize = 0.42f;
	/// <summary> Tests. </summary>
	public static int Opened, Detonated, Swallowed;
	public static float LastPulledIn;
	public static Vector3 LastAt;
	/// <summary> The last one opened (tests time their pictures by its age). </summary>
	public static BlackHole Current { get; private set; }
	public float Age => age;

	float damage, radius, size, age, life, reach, crushTick;
	int rank;
	Color colour;
	PlayerController shooter;
	PlayerWeapon weapon;
	MeshInstance3D core, horizon, disc, tilted, lens;
	ShaderMaterial discM, tiltedM, horizonM, lensM;
	OmniLight3D glow, dark;
	TimeSince sinceSilt;
	bool detonated;

	public static void Open( Node parent, Vector3 at, float damage, float radius, float size, Color colour, PlayerController shooter, PlayerWeapon weapon, int rank )
	{
		Opened++;
		var hole = new BlackHole { Name = "Black Hole", damage = damage, radius = radius, size = size, colour = colour, shooter = shooter, weapon = weapon, rank = rank };
		hole.life = Life( rank );
		hole.reach = Reach( rank );
		parent.AddChild( hole );
		// A little off the floor, so what it drags in gathers in the water, not in the sand.
		var exclude = shooter is not null && IsInstanceValid( shooter ) ? new Godot.Collections.Array<Rid> { shooter.GetRid() } : null;
		var floor = Trace.Ray( hole, at + Vector3.Up * 0.2f, at + Vector3.Down * 3f, Layers.World, exclude );
		hole.GlobalPosition = floor.Hit && at.Y - floor.HitPosition.Y < 1.2f ? floor.HitPosition + Vector3.Up * 1.2f : at;
		LastAt = hole.GlobalPosition;
		Current = hole;
		hole.Build();
	}

	void Build()
	{
		var black = new StandardMaterial3D { AlbedoColor = Colors.Black, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DisableFog = true };
		black.SetMeta( "no_caustics", true );
		core = new MeshInstance3D { Name = "Core", Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 32, Rings = 16 }, MaterialOverride = black, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( core );
		horizonM = SkillFx.Rim( new Color( 0.7f, 0.35f, 1f ), 2.2f );
		horizon = new MeshInstance3D { Name = "Horizon", Mesh = core.Mesh, MaterialOverride = horizonM, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( horizon );
		// The accretion disc, tipped toward the view a little, and a fainter one at a cross angle (the bent light
		// from the disc's far side, as in pictures of real black holes).
		discM = SkillFx.Disc( new Color( 1f, 0.95f, 0.8f ), new Color( 1f, 0.5f, 0.2f ), new Color( 0.55f, 0.2f, 1f ), 0.2f );
		disc = new MeshInstance3D { Name = "Disc", Mesh = new QuadMesh { Size = Vector2.One }, MaterialOverride = discM, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( disc );
		tiltedM = SkillFx.Disc( new Color( 1f, 0.9f, 0.9f ), new Color( 0.9f, 0.35f, 0.7f ), new Color( 0.3f, 0.25f, 1f ), 0.3f );
		tilted = new MeshInstance3D { Name = "Halo", Mesh = disc.Mesh, MaterialOverride = tiltedM, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( tilted );
		lensM = new ShaderMaterial { Shader = lensShader ??= new Shader { Code = LensCode }, RenderPriority = -20 };
		// The bent light is drawn first (it copies the screen behind it); the disc and the horizon glow go over it.
		discM.RenderPriority = 10;
		tiltedM.RenderPriority = 9;
		horizonM.RenderPriority = 11;
		lensM.SetMeta( "no_caustics", true );
		lens = new MeshInstance3D { Name = "Lens", Mesh = new QuadMesh { Size = Vector2.One }, MaterialOverride = lensM, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( lens );
		glow = new OmniLight3D { LightColor = new Color( 0.65f, 0.35f, 1f ), LightEnergy = 0f, OmniRange = 6f, ShadowEnabled = false };
		AddChild( glow );
		// The light round about drains into it.
		dark = new OmniLight3D { LightColor = Colors.White, LightNegative = true, LightEnergy = 0f, OmniRange = reach * 0.9f, ShadowEnabled = false };
		AddChild( dark );
		Sfx.Play( "kraken_suck", GlobalPosition, 1f, 0.55f );
		Sfx.Play( "whirlpool", GlobalPosition, 0.9f, 0.6f );
		Sfx.Play( "beam_charge", GlobalPosition, 0.6f, 0.5f );
		shooter?.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.5f );
		Controls.Rumble( 0.6f );
		Visual( 0f, 0f );
	}

	/// <summary> How big the hole is now: grows in with a little overshoot, holds with a throb, then caves in. </summary>
	float Grow( out float collapse )
	{
		collapse = MathX.Clamp( (age - (life - Collapse)) / Collapse, 0f, 1f );
		var f = MathX.Clamp( age / Form, 0f, 1f );
		var form = f < 1f ? 1f - MathF.Pow( 1f - f, 3f ) + MathF.Sin( f * MathF.PI ) * 0.25f : 1f;
		var throb = 1f + 0.05f * MathF.Sin( age * 17f );
		return form * throb * (1f - collapse * collapse * 0.9f);
	}

	void Visual( float g, float collapse )
	{
		var r = CoreSize * g;
		core.Scale = Vector3.One * MathF.Max( 0.001f, r );
		horizon.Scale = Vector3.One * MathF.Max( 0.001f, r * 1.12f );
		horizonM.SetShaderParameter( "fade", MathX.Clamp( g, 0f, 1f ) * (1f + collapse * 2f) );
		// The disc spins up and is pulled in as it collapses.
		var spin = age * (7f + 30f * collapse * collapse);
		var discSize = r * MathX.Lerp( 9f, 3f, collapse );
		discM.SetShaderParameter( "spin", spin );
		discM.SetShaderParameter( "fade", MathX.Clamp( g * 1.2f, 0f, 1.4f ) );
		tiltedM.SetShaderParameter( "spin", -spin * 0.8f );
		tiltedM.SetShaderParameter( "fade", MathX.Clamp( g, 0f, 1f ) * 0.55f );
		// Tipped so the view sees it as an ellipse; it wobbles slowly like a top.
		var cam = GetViewport()?.GetCamera3D();
		var toCam = cam is not null ? (cam.GlobalPosition - GlobalPosition).SafeNormal() : Vector3.Back;
		var flatToCam = toCam.Flat().SafeNormal();
		if ( flatToCam == Vector3.Zero ) flatToCam = Vector3.Back;
		var up = (Vector3.Up + flatToCam * 0.35f + new Vector3( MathF.Sin( age * 1.3f ), 0f, MathF.Cos( age * 1.1f ) ) * 0.08f).SafeNormal();
		disc.GlobalBasis = Shapes.LookAt( up ) * Basis.FromScale( new Vector3( discSize, discSize, 1f ) );
		var side = flatToCam.Cross( Vector3.Up ).SafeNormal();
		var haloUp = (flatToCam + Vector3.Up * 0.15f + side * 0.1f).SafeNormal();
		tilted.GlobalBasis = Shapes.LookAt( haloUp ) * Basis.FromScale( new Vector3( discSize * 0.62f, discSize * 0.62f, 1f ) );
		// The bent light: a camera-facing quad that swirls what is behind it into the hole.
		var lensSize = r * MathX.Lerp( 9f, 5f, collapse );
		lens.Scale = Vector3.One * MathF.Max( 0.001f, lensSize );
		lensM.SetShaderParameter( "strength", MathX.Clamp( g, 0f, 1.2f ) * (1f + collapse * 1.5f) );
		lensM.SetShaderParameter( "core", r / MathF.Max( 0.0005f, lensSize * 0.5f ) );
		lensM.SetShaderParameter( "twist", age * 2f );
		glow.LightEnergy = 2.5f * g * (1f + collapse * 2f);
		dark.LightEnergy = 3f * MathX.Clamp( g, 0f, 1f );
	}

	public override void _Process( double delta )
	{
		using var perf = Perf.Time( "BlackHole" );
		var dt = (float)delta;
		age += dt;
		if ( age >= life ) { Detonate(); return; }
		var g = Grow( out var collapse );
		Visual( g, collapse );
		var centre = GlobalPosition;
		var who = shooter is not null && IsInstanceValid( shooter ) ? shooter : null;
		var pull = MathX.Clamp( age / Form, 0f, 1f ) * (1f + collapse * 1.5f);

		// Enemies: swept round and in, faster the nearer they are. The heavy ones (bosses) can't be moved, only crushed.
		crushTick -= dt;
		var crush = crushTick <= 0f;
		if ( crush ) crushTick = 0.25f;
		foreach ( var c in Creature.Everyone.ToList() )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead ) continue;
			var body = c.GlobalPosition + Vector3.Up * 0.4f;
			var to = centre - body;
			var d = to.Length();
			if ( d > reach ) continue;
			var near = 1f - d / reach;
			if ( c.Pullable )
			{
				var dir = to / MathF.Max( d, 0.01f );
				var tangent = dir.Cross( Vector3.Up ).SafeNormal();
				var weight = MathX.Clamp( 260f / MathF.Max( 60f, c.Weight ), 0.5f, 1.5f );
				var speed = MathX.Lerp( 3.5f, 12f, near ) * pull * weight;
				var step = dir * MathF.Min( MathF.Max( 0f, d - 0.35f ), speed * dt ) + tangent * speed * 0.45f * dt * MathX.Clamp( d / 2f, 0f, 1f );
				c.Pull( step );
			}
			if ( crush && d < reach * 0.35f ) PlayerPerks.Hurt( c, damage * 0.035f, centre, centre, who );
		}

		// Gibs and goo: pulled in and swallowed when they reach the core.
		var gore = GoreSystem.Get( this );
		if ( gore is not null )
			foreach ( var gib in gore.Gibs.ToList() )
			{
				if ( !IsInstanceValid( gib ) || gib.IsFading ) continue;
				var to = centre - gib.GlobalPosition;
				var d = to.Length();
				if ( d > reach * 1.2f ) continue;
				if ( d < CoreSize * 1.3f )
				{
					// Swallowed gore goes into the Gore Vac's tank, as if the vac had sucked it up itself.
					Swallowed++;
					if ( weapon is not null && IsInstanceValid( weapon ) ) weapon.FeedFromHole( gib );
					gib.QueueFree();
					continue;
				}
				gib.LinearVelocity = gib.LinearVelocity.Lerp( to / d * MathX.Lerp( 3f, 14f, 1f - d / (reach * 1.2f) ) + to.Cross( Vector3.Up ).SafeNormal() * 2f, MathF.Min( 1f, 6f * dt ) );
			}

		// Silt and specks streaming in from all round, spiralling.
		if ( sinceSilt > 0.016f )
		{
			sinceSilt = 0f;
			for ( var i = 0; i < 4; i++ )
			{
				var dir = Rng.OnSphere();
				dir.Y *= 0.45f;
				var from = centre + dir.SafeNormal() * Rng.Float( reach * 0.35f, reach * 0.9f );
				var inward = (centre - from).SafeNormal();
				var swirl = inward.Cross( Vector3.Up ).SafeNormal();
				var c = Rng.Int( 0, 3 ) switch { 0 => new Color( 0.7f, 0.4f, 1f ), 1 => new Color( 1f, 0.7f, 0.3f ), 2 => colour, _ => new Color( 0.8f, 0.9f, 1f ) };
				GpuFx.SandPuff( from, inward * Rng.Float( 6f, 11f ) + swirl * Rng.Float( 2f, 5f ), c with { A = 0.85f }, Rng.Float( 4f, 9f ), 0.6f );
			}
		}

		// The player feels the tug too (never hurt by it).
		if ( who is not null )
		{
			var to = centre - who.GlobalPosition;
			var d = to.Length();
			if ( d < reach && d > 1.5f ) who.Velocity += to / d * 3f * (1f - d / reach) * dt;
			if ( d < reach * 2f ) who.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.02f + 0.05f * collapse );
		}
	}

	void Detonate()
	{
		if ( detonated ) return;
		detonated = true;
		Detonated++;
		var at = GlobalPosition;
		// How tight it pulled them: the mean distance of the enemies it had in reach.
		var near = Creature.Everyone.Where( c => IsInstanceValid( c ) && c.IsEnemy && !c.IsDead && c.GlobalPosition.DistanceTo( at ) < reach ).ToList();
		LastPulledIn = near.Count > 0 ? near.Average( c => (c.GlobalPosition + Vector3.Up * 0.4f).DistanceTo( at ) ) : -1f;
		var parent = GetParent();
		GoreBall.BurstAt( parent, at, Vector3.Up, damage * DamageScale( rank ), radius * 1.4f, size, colour, shooter, weapon, GoreBall.Kind.Big );
		AlienBlast.Spawn( parent, at, 2.6f );
		Shockwave.Spawn( parent, at, reach * 0.9f, new Color( 0.65f, 0.35f, 1f ) );
		Sfx.Play( "kraken_burst", at, 1f, 1.1f );
		Sfx.Play( "missile_boom", at, 1f, 0.6f );
		if ( shooter is not null && IsInstanceValid( shooter ) && shooter.GlobalPosition.DistanceTo( at ) < reach * 3f ) shooter.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 1f );
		Controls.Rumble( 1f );
		QueueFree();
	}

	static Shader lensShader;
	const string LensCode = @"
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float strength = 0.0;
uniform float core = 0.1;
uniform float twist = 0.0;
varying vec2 centre_ss;
varying float radius_ss;
void vertex() {
	float s = length(MODEL_MATRIX[0].xyz);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * s, INV_VIEW_MATRIX[1] * s, INV_VIEW_MATRIX[2] * s, MODEL_MATRIX[3]);
	vec4 c = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
	vec4 e = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(0.5, 0.0, 0.0, 1.0);
	centre_ss = c.xy / c.w * 0.5 + 0.5;
	radius_ss = max(abs(e.x / e.w * 0.5 + 0.5 - centre_ss.x), 0.0001);
}
void fragment() {
	vec2 aspect = vec2(1.0, VIEWPORT_SIZE.y / VIEWPORT_SIZE.x);
	vec2 d = (SCREEN_UV - centre_ss) * aspect;
	float r = length(d) / radius_ss;
	if (r > 1.0) discard;
	float f = 1.0 - r;
	float ang = strength * (3.2 * f * f + 0.3 * sin(twist + r * 9.0) * f);
	float cs = cos(ang), sn = sin(ang);
	vec2 rd = vec2(cs * d.x - sn * d.y, sn * d.x + cs * d.y);
	rd *= 1.0 + strength * 0.5 * f / max(r, 0.18);
	vec2 uv = centre_ss + rd / aspect;
	vec3 col = textureLod(screen_tex, uv, 0.0).rgb;
	float ring = exp(-pow((r - core * 1.45) / 0.035, 2.0)) * strength;
	col += vec3(0.85, 0.65, 1.0) * ring * 1.4;
	col *= mix(1.0, 0.55, f * strength * 0.6);
	col = mix(col, vec3(0.0), smoothstep(core * 1.1, core * 0.95, r));
	ALBEDO = col;
	ALPHA = smoothstep(1.0, 0.75, r);
}";
}

/// <summary> A ring of light racing out and fading (the black hole going off). </summary>
public partial class Shockwave : Node3D
{
	float radius, age;
	MeshInstance3D ring;
	StandardMaterial3D m;
	const float Life = 0.45f;

	public static void Spawn( Node parent, Vector3 at, float radius, Color colour )
	{
		var s = new Shockwave { Name = "Shockwave", radius = radius };
		parent.AddChild( s );
		s.GlobalPosition = at;
		s.m = new StandardMaterial3D { AlbedoColor = colour, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = colour, EmissionEnergyMultiplier = 5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, DisableFog = true };
		s.m.SetMeta( "no_caustics", true );
		s.ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.92f, OuterRadius = 1f, Rings = 48, RingSegments = 6 }, MaterialOverride = s.m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		s.AddChild( s.ring );
		var flash = new OmniLight3D { LightColor = colour.Lightened( 0.4f ), LightEnergy = 5f, OmniRange = radius * 1.5f, ShadowEnabled = false };
		s.AddChild( flash );
		var tween = s.CreateTween();
		tween.TweenProperty( flash, "light_energy", 0f, Life );
	}

	public override void _Process( double delta )
	{
		age += (float)delta;
		var t = MathX.Clamp( age / Life, 0f, 1f );
		var e = 1f - (1f - t) * (1f - t);
		ring.Scale = new Vector3( radius * e, 1.5f, radius * e );
		m.AlbedoColor = m.AlbedoColor with { A = 1f - t };
		if ( t >= 1f ) QueueFree();
	}
}
