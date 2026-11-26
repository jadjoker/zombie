using System;
using Godot;

/// <summary>
/// Each area changing as a run goes on (user's ask: wave 25 shouldn't look like wave 21). <see cref="Tension"/> runs
/// 0 to 1 over an area's ten waves. In the Abyss the moss lamps flicker more and now and then go out, a big blast
/// shakes rubble off the roof, and on boss waves (and sometimes late on) a leviathan swims over the skylight. In
/// Atlantis the crystal heart beats faster and harder (<see cref="AtlantisHeart"/>). On the shore and the wreck site
/// the leviathans circle closer and lower (<see cref="Atmosphere"/>), and the wreck's hull groans more often, shaking
/// silt off. Only for show: every machine runs its own from the wave it is told.
/// </summary>
public static class AreaLife
{
	/// <summary> How far through its area's waves the run is: 0 on the first, 1 on the last. </summary>
	public static float Tension { get; private set; }
	/// <summary> Tests: force a tension (null follows the wave). </summary>
	public static float? ForceTension;
	public static int Flickers, Outages, Rubble, Passes, Groans;

	static int lastWave = -1, lastArea = -1;
	static double nextGroan, nextPass = double.MaxValue;
	static bool subscribed;

	public static int FirstWave( int area ) => area switch { SeaTerrain.Wreck => 11, SeaTerrain.Caves => 21, SeaTerrain.Globe => 31, _ => 1 };

	public static void Step( WaveDirector d, float dt )
	{
		if ( !subscribed ) { subscribed = true; SeaStir.Blasted += OnBlast; }
		var area = SeaTerrain.Active ? SeaTerrain.Area : SeaTerrain.Shore;
		var target = ForceTension ?? MathX.Clamp( (d.Wave - FirstWave( area )) / 9f, 0f, 1f );
		Tension = ForceTension.HasValue ? target : MathX.Lerp( Tension, target, MathX.Clamp( dt * 0.5f, 0f, 1f ) );
		if ( MathF.Abs( Tension - target ) < 0.002f ) Tension = target;

		// A new wave in the caves: a boss wave always brings a leviathan over the skylight, a late one sometimes.
		if ( d.Wave != lastWave || area != lastArea )
		{
			lastWave = d.Wave;
			lastArea = area;
			nextPass = double.MaxValue;
			if ( area == SeaTerrain.Caves && d.Wave >= 21 && (d.BossWave || Rng.Float() < Tension * 0.5f) && d.BossKind != WaveDirector.EnemyKind.Leviathan ) nextPass = Clock.Now + Rng.Float( 6f, 14f );
		}
		if ( area == SeaTerrain.Caves )
		{
			Flicker( dt );
			if ( Clock.Now > nextPass ) { nextPass = double.MaxValue; PassSkylight(); }
		}
		if ( area == SeaTerrain.Wreck && ShipWreck.Placed && Clock.Now > nextGroan )
		{
			nextGroan = Clock.Now + MathX.Lerp( 28f, 9f, Tension ) * Rng.Float( 0.7f, 1.3f );
			Groan();
		}
	}

	// ---- The Abyss ----

	static readonly float[] dropout = new float[256];

	/// <summary> The moss lamps waver, more as the waves climb, and late on one now and then goes out a moment. </summary>
	static void Flicker( float dt )
	{
		var lamps = AbyssProps.MossLamps;
		var now = (float)Clock.Now;
		var t = Tension;
		for ( var i = 0; i < lamps.Count; i++ )
		{
			var (light, energy) = lamps[i];
			if ( !GodotObject.IsInstanceValid( light ) ) continue;
			var k = i % dropout.Length;
			if ( dropout[k] > 0f ) dropout[k] -= dt;
			else if ( Rng.Float() < t * t * 0.05f * dt ) { dropout[k] = Rng.Float( 0.15f, 0.9f ); Outages++; }
			var waver = MathF.Sin( now * (2f + 9f * t) + i * 1.7f ) * MathF.Sin( now * (3.1f + 13f * t) + i * 0.6f );
			var level = 1f - (0.06f + 0.4f * t) * MathF.Max( 0f, waver );
			if ( dropout[k] > 0f ) level *= 0.08f;
			light.LightEnergy = energy * level;
		}
		if ( t > 0.05f ) Flickers++;
	}

	/// <summary> A big blast in the caves shakes rubble and dust off the roof above it. </summary>
	static void OnBlast( Vector3 at, float size )
	{
		if ( SeaTerrain.Area != SeaTerrain.Caves || Abyss.Root is null || size < 0.8f ) return;
		var roof = Abyss.RoofAt( at.X, at.Z );
		if ( roof - at.Y > 32f ) return;
		Rubble++;
		var recipe = GibRecipes.CaveRock();
		var count = Math.Min( 6, 2 + (int)(size * 2f) );
		for ( var i = 0; i < count; i++ )
		{
			var from = new Vector3( at.X, roof - 0.6f, at.Z ) + Rng.InSphere( 2.5f ).Flat();
			GibKit.Chips( recipe, from, Vector3.Down, Vector3.Down, Rng.Float( 0.12f, 0.3f ), 1, 0.4f );
		}
		var dust = SeaStir.SiltColour( at );
		for ( var i = 0; i < 10; i++ )
			GpuFx.SandPuff( new Vector3( at.X, roof - 0.5f, at.Z ) + Rng.InSphere( 3f ).Flat(), Vector3.Down * Rng.Float( 0.5f, 1.5f ) + Rng.InSphere( 0.4f ), dust, Rng.Float( 10f, 20f ), Rng.Float( 1.5f, 2.6f ) );
		Sfx.Play( "rock_crumble", new Vector3( at.X, roof - 1f, at.Z ), 0.8f, Rng.Float( 0.85f, 1.1f ) );
	}

	/// <summary> A leviathan swims over the skylight: a huge shadow crossing the light, a long moan. </summary>
	public static SkylightPass PassSkylight()
	{
		if ( Abyss.Root is null || !GodotObject.IsInstanceValid( Abyss.Root ) ) return null;
		Passes++;
		var pass = new SkylightPass { Name = "Skylight Pass" };
		Abyss.Root.AddChild( pass );
		return pass;
	}

	// ---- The wreck site ----

	/// <summary> The hull settles with a long metal groan, and silt sifts off it here and there. </summary>
	static void Groan()
	{
		Groans++;
		var p = PlayerSurvivor.FindNearestAlive( Vector3.Zero );
		var near = ShipWreck.World( new Vector3( Rng.Float( -ShipWreck.HalfLength, ShipWreck.HalfLength ) * 0.8f, 6f, 0f ) );
		if ( p is not null )
		{
			// The part of the hull nearest the player, so it is heard.
			var local = ShipWreck.Frame.AffineInverse() * p.RealPosition;
			near = ShipWreck.World( new Vector3( MathX.Clamp( local.X, -ShipWreck.HalfLength, ShipWreck.HalfLength ) * 0.9f, 7f, 0f ) );
		}
		Sfx.Play( "hull_groan", near, MathX.Lerp( 0.6f, 1f, Tension ), Rng.Float( 0.8f, 1.05f ) );
		var dust = SeaStir.SiltColour( near );
		for ( var i = 0; i < 14; i++ )
			GpuFx.SandPuff( near + Rng.InSphere( 8f ) + Vector3.Up * 3f, Vector3.Down * Rng.Float( 0.3f, 1f ), dust, Rng.Float( 8f, 16f ), Rng.Float( 1.6f, 2.8f ) );
		SeaStir.Push( near, 10f, 0.4f );
		if ( p is not null && p.RealPosition.DistanceTo( near ) < 25f ) p.Controller?.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.12f * (0.5f + Tension) );
	}
}

/// <summary>
/// A leviathan crossing high over the Abyss's skylight, seen as a huge shape against the light, its tail beating; the
/// shaft dims under it. Gone when it is past.
/// </summary>
public partial class SkylightPass : Node3D
{
	public const float Time = 14f;
	Node3D body, tail;
	Vector3 from, to;
	float age;
	Light3D beam;
	float beamEnergy;

	public override void _Ready()
	{
		var length = Rng.Float( 34f, 44f );
		body = Atmosphere.BuildLeviathan( length, out tail );
		AddChild( body );
		var roof = Abyss.RoofAt( 0f, 0f );
		var a = Rng.Float( 0f, MathF.Tau );
		var dir = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
		var side = new Vector3( -dir.Z, 0f, dir.X ) * Rng.Float( -3f, 3f );
		var height = roof + Rng.Float( 14f, 20f );
		from = -dir * 70f + side + Vector3.Up * height;
		to = dir * 70f + side + Vector3.Up * (height + Rng.Float( -3f, 3f ));
		// The statue's spotlight: it dims as the shape crosses the light.
		if ( AbyssStatue.Light is { } spot && IsInstanceValid( spot ) ) { beam = spot; beamEnergy = spot.LightEnergy; }
		Sfx.Play( "leviathan_moan", Vector3.Up * (roof + 4f), 1f, Rng.Float( 0.85f, 1.05f ) );
	}

	public override void _Process( double delta )
	{
		age += (float)delta;
		var k = age / Time;
		if ( k >= 1f ) { if ( beam is not null && IsInstanceValid( beam ) ) beam.LightEnergy = beamEnergy; QueueFree(); return; }
		var at = from.Lerp( to, k );
		var heading = (to - from).Normalized();
		body.GlobalTransform = new Transform3D( Shapes.LookAt( heading ) * new Basis( Vector3.Forward, MathF.Sin( age * 0.5f ) * 0.08f ), at );
		tail.Rotation = new Vector3( MathF.Sin( age * 0.9f ) * 0.4f, 0f, 0f );
		if ( beam is not null && IsInstanceValid( beam ) )
		{
			var over = at.Flat().Length();
			beam.LightEnergy = beamEnergy * (1f - 0.75f * (1f - MathX.Clamp( (over - 4f) / 18f, 0f, 1f )));
		}
	}

	/// <summary> Tests: where it is now. </summary>
	public Vector3 At => body.GlobalPosition;
}
