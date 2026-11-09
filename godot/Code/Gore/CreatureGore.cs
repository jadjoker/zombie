using Godot;

/// <summary>
/// Port of the s&box CreatureGore helpers: bullet wounds on a creature part (a ragged dark hole with goo welling
/// up and a little spray), flesh knocked loose, stumps where parts come off (with a pulsing jet then a drip),
/// point-blank overkills, bleeding trails, and the jiggle and twitch that make the pieces feel soft.
/// (s&box also dented the mesh itself; that comes with the gore detail pass.)
/// Sizes are in inches; positions and directions are Godot world space.
/// </summary>
public static class CreatureGore
{
	public const int MaxMaskParts = 31;
	public const int MaxWoundsPerPart = 6;
	/// <summary> Kills from closer than this (inches) are overkills. </summary>
	public const float OverkillRange = 220f;

	public static void AddWound( Node3D part, Vector3 hitPosition, Vector3 direction, Color skin, Color goo, float size )
	{
		if ( part is null || !GodotObject.IsInstanceValid( part ) ) return;
		direction = direction.SafeNormal();
		var normal = (hitPosition - part.GlobalPosition).SafeNormal();
		if ( normal == Vector3.Zero ) normal = -direction;
		size *= 0.5f;
		ImpactSpray( hitPosition, normal, direction, goo, size );

		var wounds = part.GetChildren().OfType<Node3D>().Where( c => c.Name.ToString().StartsWith( "Wound" ) ).ToList();
		var near = wounds.FirstOrDefault( w => w.GlobalPosition.DistanceTo( hitPosition ) < size * 1.1f * U.In );
		if ( near is not null )
		{
			if ( near.Scale.X < 1.8f ) near.Scale *= 1.15f;
			KnockChunks( near.GlobalPosition, normal, direction, skin, goo, size, 1 );
			return;
		}
		KnockChunks( hitPosition, normal, direction, skin, goo, size, Rng.Int( 1, 3 ) );
		if ( wounds.Count >= MaxWoundsPerPart ) wounds[0].QueueFree();

		var wound = new Node3D { Name = "Wound" };
		part.AddChild( wound );
		wound.GlobalTransform = new Transform3D( Shapes.LookAt( -normal ), hitPosition - normal * size * 0.2f * U.In );
		// Undo the part's scale so the wound has its own size.
		var partScale = part.GlobalBasis.Scale.X;
		wound.Scale = Vector3.One / MathF.Max( partScale, 0.001f ) * (part.GlobalBasis.Scale.X > 0f ? 1f : 1f);
		var oval = 1f + (1f - MathF.Abs( direction.Dot( normal ) )) * 0.6f;
		var flesh = goo.Lerp( new Color( 0.06f, 0.01f, 0.02f ), 0.85f );
		var lumps = Rng.Int( 2, 4 );
		for ( var i = 0; i < lumps; i++ )
		{
			var rad = Rng.Float( 0.3f, 0.5f ) * size;
			Shapes.Part( wound, "Gore", new Vector3( 0.04f * size, Rng.Float( -0.3f, 0.3f ) * oval * size, Rng.Float( -0.3f, 0.3f ) * size ),
				Shapes.Ellipsoid( 0.07f * size, rad * oval, rad * Rng.Float( 0.7f, 1f ) ), i == 0 ? new Color( 0.02f, 0.01f, 0.01f ) : flesh, shadow: false );
		}
		Shapes.Part( wound, "Gore", new Vector3( 0.02f * size, Rng.Float( -0.2f, 0.2f ) * size, -0.2f * size ), Shapes.Ellipsoid( 0.08f * size, 0.22f * size, 0.3f * size ), goo.Lerp( Colors.Black, 0.35f ), shadow: false );
		wound.AddChild( new GooOoze { Color = goo, Lifetime = Rng.Float( 1.5f, 3f ), Interval = 0.3f } );
	}

	public static void KnockChunks( Vector3 at, Vector3 normal, Vector3 direction, Color skin, Color goo, float size, int count )
	{
		var gore = GoreSystem.Get();
		var reflect = direction - 2f * direction.Dot( normal ) * normal;
		for ( var i = 0; i < count; i++ )
		{
			var dir = (normal + reflect * 0.4f + Rng.InSphere( 1f ) * 0.5f).SafeNormal();
			gore.ThrowBlob( at + normal * size * 0.5f * U.In, dir, skin, goo, Rng.Float( 0.35f, 0.6f ) * size );
		}
	}

	public static void ImpactSpray( Vector3 at, Vector3 normal, Vector3 direction, Color goo, float size )
	{
		var reflect = direction - 2f * direction.Dot( normal ) * normal;
		var back = (normal * 0.8f + reflect * 0.5f).SafeNormal();
		var drops = Rng.Int( 5, 9 );
		for ( var i = 0; i < drops; i++ )
		{
			var dir = (back + Rng.InSphere( 1f ) * 0.45f).SafeNormal();
			GooDroplet.Spawn( at + normal * 1.5f * U.In, dir * Rng.Float( 110f, 280f ) * U.In, goo, Rng.Float( 0.3f, 0.65f ) );
		}
		for ( var i = 0; i < 3; i++ )
			GooDroplet.Spawn( at + direction * size * 2f * U.In, (direction + Rng.InSphere( 1f ) * 0.25f).SafeNormal() * Rng.Float( 250f, 420f ) * U.In, goo, Rng.Float( 0.35f, 0.7f ) );
	}

	/// <summary> A torn-off joint: a goo-filled stump left on the part it came off, jetting then dripping. </summary>
	public static Node3D AddStump( Node3D attachTo, Vector3 worldPosition, Vector3 outward, float radius, Color skin, Color goo, float dripSeconds = 6f )
	{
		if ( attachTo is null || !GodotObject.IsInstanceValid( attachTo ) ) return null;
		var stump = new Node3D { Name = "Stump" };
		attachTo.AddChild( stump );
		stump.GlobalTransform = new Transform3D( Shapes.LookAt( outward == Vector3.Zero ? Vector3.Up : outward ), worldPosition );
		stump.Scale = Vector3.One / MathF.Max( attachTo.GlobalBasis.Scale.X, 0.001f );
		var s = radius;
		Shapes.Part( stump, "Rim", Vector3.Zero, Shapes.Ellipsoid( 0.45f * s, 1.05f * s, 1.05f * s ), skin.Lerp( Colors.Black, 0.35f ) );
		Shapes.Part( stump, "Goo", new Vector3( 0.2f * s, 0, 0 ), Shapes.Ellipsoid( 0.35f * s, 0.85f * s, 0.85f * s ), goo.Lerp( Colors.White, 0.22f ) );
		Shapes.Part( stump, "Hole", new Vector3( 0.32f * s, 0, 0 ), Shapes.Ellipsoid( 0.2f * s, 0.45f * s, 0.45f * s ), new Color( 0.12f, 0.02f, 0.04f ) );
		Shapes.Part( stump, "Bead", new Vector3( 0.35f * s, 0.2f * s, -0.3f * s ), Shapes.Ellipsoid( 0.3f * s, 0.3f * s, 0.45f * s ), goo.Lerp( Colors.White, 0.1f ) );
		if ( dripSeconds > 0f )
		{
			stump.AddChild( new GooJet { Color = goo, Lifetime = MathX.Clamp( dripSeconds * 0.45f, 1f, 2.8f ), Strength = MathX.Clamp( radius / 5f, 0.5f, 1.6f ) * (dripSeconds < 3f ? 0.55f : 1f) } );
			stump.AddChild( new GooOoze { Color = goo, Lifetime = dripSeconds, Interval = 0.14f } );
		}
		return stump;
	}

	/// <summary> A point-blank kill: a much bigger blast of goo and flesh, and a jolt for anyone close. </summary>
	public static void Overkill( Vector3 at, Vector3 direction, Color skin, Color goo, float scale = 1f )
	{
		var gore = GoreSystem.Get();
		direction = direction == Vector3.Zero ? Vector3.Up : direction.Normalized();
		for ( var i = 0; i < 3; i++ )
			gore.GooBurst( at, (direction + Rng.InSphere( 1f ) * 0.7f).SafeNormal(), goo, 12, 420f, 1.4f * scale );
		gore.GooSpray( at, direction, 14, goo, 520f, 0.9f, 2.4f, sag: 0.15f );
		gore.GooSpray( at, Vector3.Down, 4, goo, 200f, 1.2f, 2.4f );
		var blobs = Rng.Int( 3, 5 );
		for ( var i = 0; i < blobs; i++ )
			gore.ThrowBlob( at, (direction + Rng.InSphere( 1f ) * 0.9f + Vector3.Up * 0.4f).SafeNormal(), skin, goo, Rng.Float( 2.5f, 4.5f ) * scale );
		foreach ( var player in PlayerSurvivor.All )
		{
			var distance = (player.Controller.GlobalPosition + Vector3.Up * 60f * U.In).DistanceTo( at ) / U.In;
			if ( distance > 350f ) continue;
			player.Juice?.Shake( 0.25f );
			if ( distance < 260f ) player.Afflictions?.Splattered( goo, distance < 150f ? 7 : 4 );
		}
	}

	/// <summary> A badly hurt creature leaks goo as it goes, faster the closer it is to death. </summary>
	public static void BleedTrail( Vector3 at, Color goo, float healthFraction, ref TimeSince timer )
	{
		if ( healthFraction > 0.6f || healthFraction <= 0f ) return;
		var interval = MathX.Lerp( 0.1f, 0.5f, healthFraction / 0.6f );
		if ( timer < interval ) return;
		timer = 0f;
		var drop = at + Rng.InSphere( 6f * U.In ).Flat();
		GoreSystem.Get().GooDrip( drop, goo );
		if ( Rng.Float() < 0.5f ) GooDroplet.Spawn( drop, Vector3.Down * 30f * U.In, goo, Rng.Float( 0.7f, 1.3f ) );
	}

	/// <summary> A severed piece keeps spasming for a while: little hops and flips that die down. </summary>
	public static void Twitch( Node3D piece, float seconds = 3f, float strength = 1f )
	{
		var body = piece as RigidBody3D ?? piece?.GetParent() as RigidBody3D;
		if ( body is null ) return;
		var twitch = body.GetNodeOrNull<Twitch>( "Twitch" ) ?? new Twitch { Name = "Twitch" };
		if ( twitch.GetParent() is null ) body.AddChild( twitch );
		twitch.Seconds = seconds;
		twitch.Strength = strength;
	}

	/// <summary> A quick squash-and-wobble on a part that just got hit. </summary>
	public static void Jiggle( Node3D part, float strength = 1f )
	{
		if ( part is null || !GodotObject.IsInstanceValid( part ) ) return;
		// Physics bodies can't take a squashed scale (Jolt), so jiggle their meshes instead.
		if ( part is CollisionObject3D )
		{
			foreach ( var child in part.GetChildren().OfType<Node3D>() )
				if ( child is not CollisionShape3D and not CollisionObject3D ) Jiggle( child, strength );
			return;
		}
		var jiggle = part.GetNodeOrNull<Jiggle>( "Jiggle" );
		if ( jiggle is null ) { jiggle = new Jiggle { Name = "Jiggle" }; part.AddChild( jiggle ); }
		jiggle.Punch( strength );
	}
}

/// <summary> Squash and wobble on a hit: the parent's scale springs around its rest scale. </summary>
public partial class Jiggle : Node
{
	float amount;
	float phase;
	Vector3 rest;
	bool hasRest;

	public void Punch( float strength )
	{
		var parent = GetParent<Node3D>();
		if ( !hasRest ) { rest = parent.Scale; hasRest = true; }
		amount = MathF.Min( 0.35f, amount + 0.18f * strength );
		phase = 0f;
	}

	public override void _Process( double delta )
	{
		if ( !hasRest || amount <= 0.001f ) return;
		var parent = GetParent<Node3D>();
		phase += (float)delta * 28f;
		amount *= MathF.Exp( -6f * (float)delta );
		var s = MathF.Sin( phase ) * amount;
		parent.Scale = rest * new Vector3( 1f + s, 1f - s, 1f + s );
		if ( amount <= 0.001f ) parent.Scale = rest;
	}
}

/// <summary> Makes a loose piece spasm: random little hops and flips, weaker and rarer as it runs down. </summary>
public partial class Twitch : Node
{
	public float Seconds = 3f;
	public float Strength = 1f;
	TimeSince age;
	TimeUntil next;

	public override void _Ready()
	{
		age = 0f;
		next = Rng.Float( 0.25f, 0.5f );
	}

	public override void _PhysicsProcess( double delta )
	{
		if ( age > Seconds ) { QueueFree(); return; }
		if ( next > 0f ) return;
		var life = 1f - age / Seconds;
		next = Rng.Float( 0.15f, 0.45f ) / MathF.Max( 0.3f, life );
		if ( GetParent() is not RigidBody3D body ) return;
		var space = body.GetWorld3D().DirectSpaceState;
		var down = Trace.Ray( body, body.GlobalPosition, body.GlobalPosition + Vector3.Down * 30f * U.In, Layers.World );
		if ( !down.Hit ) return;
		body.LinearVelocity += (Vector3.Up * Rng.Float( 60f, 140f ) + Rng.InSphere( 50f ).Flat()) * U.In * Strength * life;
		body.AngularVelocity += Rng.InSphere( 10f ) * Strength * life;
	}
}

/// <summary> Goo trickling out of a wound or stump for a while. </summary>
public partial class GooOoze : Node3D
{
	public Color Color;
	public float Lifetime = 3f;
	public float Interval = 0.2f;
	TimeSince age;
	TimeSince last;

	public override void _Process( double delta )
	{
		if ( age > Lifetime ) { QueueFree(); return; }
		if ( last < Interval ) return;
		last = 0f;
		GooDroplet.Spawn( GlobalPosition, (Vector3.Down * 40f + Rng.InSphere( 15f )) * U.In, Color, Rng.Float( 0.3f, 0.6f ) );
	}
}

/// <summary> Pulsing jets of goo out of a fresh stump, with the heartbeat. </summary>
public partial class GooJet : Node3D
{
	public Color Color;
	public float Lifetime = 2f;
	public float Strength = 1f;
	TimeSince age;
	TimeSince last;

	public override void _Process( double delta )
	{
		if ( age > Lifetime ) { QueueFree(); return; }
		var beat = MathF.Max( 0f, MathF.Sin( (float)age * MathF.Tau * 1.6f ) );
		if ( last < 0.04f || beat < 0.3f ) return;
		last = 0f;
		var forward = -GlobalBasis.Z.SafeNormal();
		var fade = 1f - age / Lifetime;
		GooDroplet.Spawn( GlobalPosition, (forward + Rng.InSphere( 0.3f )).SafeNormal() * Rng.Float( 120f, 260f ) * U.In * Strength * beat * fade, Color, Rng.Float( 0.4f, 0.8f ) * Strength );
	}
}
