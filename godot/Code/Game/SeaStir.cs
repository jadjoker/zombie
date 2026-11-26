using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// The sea reacting to a fight (user's ask: feel more alive). Blasts, gunfire, landings and the Gore Vac's pull are
/// told here as "stirs"; the fish schools (<see cref="FishScatter"/>), the swaying plants (<see cref="Sway"/>) and the
/// silt read them. Big kills leave a drifting goo haze (<see cref="GooHaze"/>). Only for show: nothing here touches
/// the game's state, so every machine draws its own.
/// </summary>
public static class SeaStir
{
	public readonly struct Stir
	{
		public readonly Vector3 At;
		public readonly float Radius, Strength;
		public readonly int Seq;
		public readonly double Time;
		public Stir( Vector3 at, float radius, float strength, int seq ) { At = at; Radius = radius; Strength = strength; Seq = seq; Time = Clock.Now; }
	}

	/// <summary> The stirs of the last second, oldest first. Readers keep the last <see cref="Stir.Seq"/> they took. </summary>
	public static readonly List<Stir> Recent = new();
	public static int Seq { get; private set; }

	/// <summary> A blast at a point (size as <see cref="AlienBlast"/>'s, 1 a missile). </summary>
	public static event Action<Vector3, float> Blasted;

	/// <summary> A sudden disturbance: fish dart away, plants are shoved over and spring back. </summary>
	public static void Push( Vector3 at, float radius, float strength )
	{
		var now = Clock.Now;
		while ( Recent.Count > 0 && now - Recent[0].Time > 1.0 ) Recent.RemoveAt( 0 );
		if ( Recent.Count > 64 ) Recent.RemoveAt( 0 );
		Recent.Add( new Stir( at, radius, strength, ++Seq ) );
	}

	/// <summary> An explosion: a big shove, silt thrown off the sea bed under it, and the area's own reaction. </summary>
	public static void Blast( Vector3 at, float size )
	{
		if ( size < 0.25f ) return;
		Push( at, 7f + 6f * size, MathF.Min( 1.6f, 0.5f + 0.6f * size ) );
		var ground = GroundBelow( at );
		if ( at.Y - ground < 2.5f + 2f * size ) SiltRing( at with { Y = ground }, 0.6f + 0.5f * size );
		Blasted?.Invoke( at, size );
	}

	/// <summary> A gunshot: the fish near the shooter startle (no shove on the plants). </summary>
	public static void Shot( Vector3 at, float loudness = 1f ) => Push( at, 9f * loudness, -0.6f * loudness );

	// The Gore Vac's pull this frame: plants in the cone lean toward the nozzle.
	public static Vector3 PullFrom, PullDir;
	public static float PullReach;
	static ulong pullFrame;
	/// <summary> Pulled this frame or the last (the Gore Vac calls it every frame it sucks). </summary>
	public static bool Pulling => pullFrame > 0 && Engine.GetProcessFrames() - pullFrame <= 1;

	public static void Pull( Vector3 nozzle, Vector3 direction, float reach )
	{
		PullFrom = nozzle;
		PullDir = direction.SafeNormal();
		PullReach = reach;
		pullFrame = Engine.GetProcessFrames();
	}

	/// <summary> The sea bed under a point (the terrain's height, or a ray down where there is no terrain). </summary>
	public static float GroundBelow( Vector3 at )
	{
		var g = Sculpt.GroundAt( at );
		return g > float.MinValue ? g : 0f;
	}

	/// <summary> The colour silt kicked up here should be: the area's sand, darker the deeper the light. </summary>
	public static Color SiltColour( Vector3 at )
	{
		var c = new Color( 0.62f, 0.58f, 0.5f, 0.7f );
		if ( SeaTerrain.Area == SeaTerrain.Caves ) c = new Color( 0.3f, 0.31f, 0.34f, 0.75f );
		var dim = 1f - 0.45f * AbyssMood.Depth;
		return new Color( c.R * dim, c.G * dim, c.B * dim, c.A );
	}

	/// <summary> A puff of silt rolling out along the sea bed (a landing, something dropped, a blast). </summary>
	public static void SiltRing( Vector3 at, float scale )
	{
		var colour = SiltColour( at );
		var count = (int)MathF.Min( 22f, 8f + 10f * scale );
		for ( var i = 0; i < count; i++ )
		{
			var dir = (Rng.OnSphere().Flat().SafeNormal() + Vector3.Up * Rng.Float( 0.05f, 0.35f )).SafeNormal();
			GpuFx.SandPuff( at + Vector3.Up * 0.1f + dir.Flat() * Rng.Float( 0f, 0.4f ) * scale, dir * Rng.Float( 1.2f, 3.2f ) * scale,
				colour, Rng.Float( 8f, 16f ) * scale, Rng.Float( 1.2f, 2.4f ) );
		}
	}

	/// <summary> A creature died: big ones leave a cloud of their goo hanging in the water. </summary>
	public static void Died( Creature creature )
	{
		if ( creature is null || !GodotObject.IsInstanceValid( creature ) || !creature.IsInsideTree() || !creature.IsEnemy ) return;
		var max = creature.Health?.MaxHealth ?? 0f;
		Push( creature.GlobalPosition, 6f, 0.6f );
		if ( max < 500f ) return;
		GooHaze.Spawn( creature.GlobalPosition + Vector3.Up * 0.6f, creature.Blood, MathX.Clamp( max / 1500f, 0.6f, 2.2f ) );
	}
}

/// <summary>
/// Each fish of a school pushed out of its place by stirs and drawn back on a spring, so a school bursts apart from a
/// blast and slowly gathers again. <see cref="Panic"/> (0 to 1) lets the school swim faster for a while.
/// </summary>
public sealed class FishScatter
{
	readonly Vector3[] offset, velocity, spread;
	int seen = SeaStir.Seq;
	public float Panic;

	public FishScatter( int count, Random random )
	{
		offset = new Vector3[count];
		velocity = new Vector3[count];
		spread = new Vector3[count];
		for ( var i = 0; i < count; i++ )
			spread[i] = new Vector3( (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f ) * 1.4f;
	}

	public Vector3 Offset( int i ) => offset[i];
	public int Count => offset.Length;
	/// <summary> Tests: the furthest any fish is from its place. </summary>
	public float Spread { get { var m = 0f; foreach ( var o in offset ) m = MathF.Max( m, o.Length() ); return m; } }
	public Vector3 Velocity( int i ) => velocity[i];
	public bool Calm { get; private set; } = true;

	/// <summary> Takes the new stirs near the school (its centre in the owner's space) and moves every fish a step. </summary>
	public void Step( Node3D owner, Vector3 centre, float dt )
	{
		var stirs = SeaStir.Recent;
		if ( stirs.Count > 0 && stirs[^1].Seq > seen )
		{
			for ( var k = 0; k < stirs.Count; k++ )
			{
				var s = stirs[k];
				if ( s.Seq <= seen ) continue;
				var local = owner.ToLocal( s.At );
				var away = centre - local;
				var d = away.Length();
				var reach = s.Radius + 3f;
				if ( d > reach ) continue;
				var power = MathF.Abs( s.Strength ) * (1f - d / reach);
				Panic = MathF.Min( 1f, Panic + power );
				var dir = d > 0.01f ? away / d : Vector3.Up;
				for ( var i = 0; i < offset.Length; i++ )
					velocity[i] += (dir + spread[i]).SafeNormal() * power * Rng.Float( 3f, 6f );
			}
			seen = stirs[^1].Seq;
			Calm = false;
		}
		Panic = MathF.Max( 0f, Panic - dt * 0.25f );
		if ( Calm ) return;
		// Back to their places: about three seconds, without overshooting much.
		const float k2 = 1.1f, c = 1.9f;
		var still = true;
		for ( var i = 0; i < offset.Length; i++ )
		{
			velocity[i] += (-offset[i] * k2 - velocity[i] * c) * dt;
			if ( velocity[i].LengthSquared() > 25f ) velocity[i] = velocity[i].Normalized() * 5f;
			offset[i] += velocity[i] * dt;
			if ( offset[i].LengthSquared() > 0.0004f || velocity[i].LengthSquared() > 0.0004f ) still = false;
		}
		if ( still ) { Calm = true; Array.Clear( offset ); Array.Clear( velocity ); }
	}

	/// <summary> The way a fish should face: its school's heading, turned toward its own dart while it flees. </summary>
	public Vector3 Heading( int i, Vector3 schoolVelocity )
	{
		var v = schoolVelocity + velocity[i];
		if ( v.LengthSquared() < 0.0001f ) v = schoolVelocity;
		// Never straight up or down (a fish dives at a slant, and a look along up has no turn).
		var flat = v.Flat();
		var h = flat.Length();
		if ( h < 0.001f ) return schoolVelocity.Flat().SafeNormal() is var f && f != Vector3.Zero ? f : Vector3.Forward;
		var y = MathX.Clamp( v.Y, -h * 1.2f, h * 1.2f );
		return new Vector3( flat.X, y, flat.Z ).Normalized();
	}
}

/// <summary> A cloud of a dead creature's goo, hanging in the water and slowly drifting up and thinning out. </summary>
public partial class GooHaze : Node3D
{
	const float Life = 8f;
	const int Max = 5;
	static readonly List<GooHaze> live = new();
	public static int Made;
	static Texture2D puff;

	float size, peak = 0.24f;
	Color colour;
	TimeSince age;
	StandardMaterial3D material;
	readonly List<(MeshInstance3D Mesh, Vector3 Drift, float Grow)> puffs = new();

	public static void Spawn( Vector3 at, Color colour, float size )
	{
		var scene = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( scene is null ) return;
		live.RemoveAll( h => !IsInstanceValid( h ) );
		if ( live.Count >= Max ) live[0].QueueFree();
		var haze = new GooHaze { Name = "Goo Haze", size = size, colour = colour };
		scene.AddChild( haze );
		haze.GlobalPosition = at;
		live.Add( haze );
		Made++;
	}

	public override void _Ready()
	{
		age = 0f;
		puff ??= ResourceLoader.Exists( "res://assets/textures/fx/puff.png" ) ? GD.Load<Texture2D>( "res://assets/textures/fx/puff.png" ) : null;
		// A little brighter in deep water, where the murk would swallow it (it is unshaded, so it shows as a faint glow).
		var dim = 1f + 0.3f * AbyssMood.Depth;
		peak = 0.24f + 0.1f * AbyssMood.Depth;
		// Thinned toward the water's own teal, so it reads as goo in the water, not a coloured wall.
		var tint = colour.Lerp( new Color( 0.1f, 0.3f, 0.35f ), 0.4f );
		material = new StandardMaterial3D
		{
			AlbedoColor = new Color( tint.R * 0.75f * dim, tint.G * 0.75f * dim, tint.B * 0.75f * dim, 0f ),
			AlbedoTexture = puff,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
			BillboardKeepScale = true,
			DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			ProximityFadeEnabled = true,
			ProximityFadeDistance = 1.2f,
			// Fades away as the camera comes close, so it never blinds a player fighting in it.
			DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha,
			DistanceFadeMinDistance = 1.5f,
			DistanceFadeMaxDistance = 5f,
		};
		material.SetMeta( "no_caustics", true );
		var quad = new QuadMesh { Size = Vector2.One };
		for ( var i = 0; i < 6; i++ )
		{
			var mesh = new MeshInstance3D { Mesh = quad, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			AddChild( mesh );
			mesh.Position = Rng.InSphere( 0.9f * size ) + Vector3.Up * 0.4f * size;
			mesh.Scale = Vector3.One * Rng.Float( 1.2f, 2.2f ) * size;
			puffs.Add( (mesh, Rng.InSphere( 0.25f ) + Vector3.Up * Rng.Float( 0.12f, 0.3f ), Rng.Float( 0.15f, 0.3f )) );
		}
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var k = age / Life;
		if ( k >= 1f ) { QueueFree(); return; }
		// Swells in fast, hangs, then thins away.
		var alpha = (k < 0.08f ? k / 0.08f : k > 0.45f ? 1f - (k - 0.45f) / 0.55f : 1f) * peak;
		material.AlbedoColor = material.AlbedoColor with { A = alpha };
		foreach ( var (mesh, drift, grow) in puffs )
		{
			mesh.Position += drift * dt;
			mesh.Scale *= 1f + grow * dt;
		}
	}

	public override void _ExitTree() => live.Remove( this );
}
