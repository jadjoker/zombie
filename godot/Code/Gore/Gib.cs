using Godot;

/// <summary>
/// Port of the s&box Gib for creature pieces: a loose physics piece (a severed segment, a flung blob) that drips
/// goo while it flies, splats and wobbles when it lands, leaves a puddle when it comes to rest, can be shot until it
/// bursts, and fades away after a while (or sooner when the gore budget is over).
/// (The skinned zombie gibs come with the zombie port.)
/// </summary>
public partial class Gib : RigidBody3D
{
	public bool IsFading { get; private set; }
	/// <summary> The colour of its goo (null: a dry piece, like stone or armour). </summary>
	public Color? Goo => goo;
	/// <summary> Spat out by the Gore Vac: sucked up again it is worth only a little (no endless ammo loop). </summary>
	public bool Recycled;
	/// <summary>
	/// Caught by the Gore Vac: it stops bumping into things (so it can't snag on the floor or the player) and shrinks
	/// toward `scale` of its size as it nears the nozzle. `Release` puts it back if the trigger is let go.
	/// </summary>
	public void Suck( float scale )
	{
		if ( piece is null || !IsInstanceValid( piece ) ) return;
		if ( !sucked ) { sucked = true; suckRest = piece.Scale; CollisionMask = 0; }
		piece.Scale = suckRest * MathX.Clamp( scale, 0.05f, 1f );
	}

	public void Release()
	{
		if ( !sucked ) return;
		sucked = false;
		CollisionMask = Layers.World | Layers.Gib;
		if ( piece is not null && IsInstanceValid( piece ) ) piece.Scale = suckRest;
	}

	bool sucked;
	Vector3 suckRest = Vector3.One;

	/// <summary> No goo in it (a shell or a stone torn off the floor): it drips and splats nothing. </summary>
	public void MakeDry() => goo = null;
	/// <summary> Roughly how big it is (the radius of its collider, metres). </summary>
	public float Radius => GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.Shape is SphereShape3D s ? s.Radius : 0.1f;

	GoreSystem gore;
	Color? goo;
	float health;
	float lifetime;
	TimeSince age;
	TimeSince fadeStarted;
	TimeSince lastDrip;
	Vector3 scaleBeforeFade;
	float lastSpeed;
	int splats;
	bool pooled;
	Node3D piece;

	/// <summary>
	/// Under water a piece floats up toward the surface (playtest): it rises at about this many m/s once the water's
	/// drag has caught it, tumbling slowly, and rides at the surface if it gets there. Out of the water it falls.
	/// </summary>
	public const float RiseSpeed = 1.6f;
	const float WaterDrag = 1.1f;
	/// <summary>
	/// What it is made of (the chunking pass): stone, armour and glass sink, shell drifts down, jelly and flesh float up;
	/// each lands with its own sound, and only the soft ones splat goo.
	/// </summary>
	public GibKind Stuff = GibKind.Flesh;
	/// <summary> Metres a second it drifts up through the water (negative: it sinks). </summary>
	float Rise => Stuff switch { GibKind.Rock or GibKind.Armour => -2.4f, GibKind.Glass => -1.6f, GibKind.Shell => -0.9f, GibKind.Jelly => 1.1f, _ => RiseSpeed };
	bool Soft => Stuff is GibKind.Flesh or GibKind.Jelly;
	/// <summary> Is it under water right now (set each physics tick). </summary>
	public bool Floating { get; private set; }

	/// <summary>
	/// Makes an existing piece loose: puts it in a physics body where it is, with a sphere collider (radius in inches),
	/// health, mass and a starting velocity (m/s). Returns the gib.
	/// </summary>
	static double lastTearSound = -1, lastClack = -1;

	public static Gib Attach( Node3D piece, GoreSystem gore, float radius, float health, float mass, Vector3 velocity, Color gooColor, bool addCollider = true )
	{
		var root = (Engine.GetMainLoop() as SceneTree).CurrentScene;
		var gib = new Gib
		{
			Name = "Gib",
			gore = gore,
			health = health,
			goo = gooColor,
			Mass = mass,
			CollisionLayer = Layers.Gib,
			CollisionMask = Layers.World | Layers.Gib,
			ContinuousCd = true,
		};
		root.AddChild( gib );
		gib.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.On;
		gib.GlobalTransform = new Transform3D( Basis.Identity, piece.GlobalPosition );
		piece.Reparent( gib, true );
		gib.piece = piece;
		if ( addCollider ) gib.AddChild( new CollisionShape3D { Shape = new SphereShape3D { Radius = MathF.Max( 0.5f, radius ) * U.In } } );
		// Start drawn where it is (the piece and its wounds too), not sliding in from an old spot.
		gib.ResetPhysicsInterpolation();

		if ( Clock.Now - lastTearSound > 0.08 && radius > 2f )
		{
			lastTearSound = Clock.Now;
			Sfx.Play( "gib", piece.GlobalPosition, MathX.Clamp( radius / 8f, 0.4f, 1f ), Rng.Float( 0.9f, 1.15f ) );
		}
		gib.LinearVelocity = velocity;
		gib.AngularVelocity = Rng.InSphere( 12f );
		gib.Buoyancy();
		gib.Begin();
		return gib;
	}

	void Begin()
	{
		age = 0f;
		lastDrip = 0f;
		lifetime = (gore?.GibLifetime ?? 20f) + Rng.Float( 0f, 4f );
		gore?.Register( this );
	}

	public void BeginFade()
	{
		if ( IsFading ) return;
		IsFading = true;
		fadeStarted = 0f;
		scaleBeforeFade = piece?.Scale ?? Vector3.One;
	}

	/// <summary> Shot or struck: shoved, splashes goo, and bursts when its health runs out. </summary>
	public void TakeHit( Vector3 position, Vector3 direction, float damage, float impulse )
	{
		if ( IsFading ) return;
		ApplyImpulse( direction * impulse * U.In * Mass, position - GlobalPosition );
		if ( piece is not null ) CreatureGore.Jiggle( piece, 1.2f );
		if ( goo is Color c )
		{
			gore?.GooBurst( position, -direction, c, 5, 180f, 0.7f );
			gore?.GooSpray( position, direction, 1, c, 120f, 0.3f, 0.7f );
		}
		health -= damage;
		if ( health <= 0f ) Pulverize( position, direction );
	}

	public void Pulverize( Vector3 position, Vector3 direction )
	{
		if ( goo is Color c )
		{
			gore?.GooBurst( position, direction, c, 14, 300f, 1.2f );
			gore?.GooSpray( position, direction, 5, c, 200f, 0.5f, 1.4f );
		}
		QueueFree();
	}

	public override void _Process( double delta )
	{
		if ( !IsFading && age > lifetime ) BeginFade();
		if ( IsFading && piece is not null )
		{
			var t = fadeStarted / (gore?.FadeTime ?? 1f);
			if ( t >= 1f ) { QueueFree(); return; }
			piece.Scale = scaleBeforeFade * MathX.Lerp( 1f, 0.01f, t * t );
		}
	}

	/// <summary>
	/// Sets how the water carries it: under water, gravity turned around and drag (so it drifts up at RiseSpeed); at
	/// the surface, held there bobbing; above it (or on a dry level), ordinary falling.
	/// </summary>
	void Buoyancy()
	{
		var water = WaterFeel.IsOn();
		var surface = Sea.SurfaceY;
		Floating = water && (surface is not float top || GlobalPosition.Y < top);
		if ( !Floating )
		{
			// The body's defaults, as before buoyancy existed.
			GravityScale = 1f;
			LinearDamp = 0f;
			AngularDamp = 0f;
			return;
		}
		var gravity = (float)ProjectSettings.GetSetting( "physics/3d/default_gravity", 9.8f );
		LinearDamp = WaterDrag;
		AngularDamp = 1.4f;
		// Terminal speed under a constant push with linear drag is push / drag.
		GravityScale = -Rise * WaterDrag / MathF.Max( 0.1f, gravity );
		if ( surface is float y && GlobalPosition.Y > y - 0.3f )
		{
			// At the surface: no more lift, and the climb dies away, so it rides there.
			GravityScale = 0f;
			if ( LinearVelocity.Y > 0f ) LinearVelocity = LinearVelocity with { Y = LinearVelocity.Y * 0.8f };
		}
	}

	public override void _PhysicsProcess( double delta )
	{
		if ( !IsFading ) Buoyancy();
		if ( gore is null || IsFading ) return;
		var speed = LinearVelocity.Length() / U.In;

		// Goo trail while it's flying.
		if ( Stuff != GibKind.Rock && age < 2f && speed > 200f && lastDrip > 0.11f )
		{
			lastDrip = 0f;
			if ( goo is Color c ) gore.GooDrip( GlobalPosition, c );
		}

		// A sudden stop means it hit something: splat and wobble.
		if ( splats < 3 && lastSpeed > 220f && speed < lastSpeed * 0.55f )
		{
			splats++;
			var loud = MathX.Clamp( lastSpeed / 600f, 0.2f, 1f ) * MathX.Clamp( Mass / 6f, 0.4f, 1f );
			if ( Soft )
			{
				if ( gore.TraceWorld( GlobalPosition + Vector3.Up * 10f * U.In, GlobalPosition + Vector3.Down * 60f * U.In ) is { } hit && goo is Color c )
					gore.GooSplat( hit.Position, hit.Normal, Rng.Float( 0.7f, 1.4f ), c );
				gore.Squelch( GlobalPosition, loud, 1.1f );
				if ( piece is not null ) CreatureGore.Jiggle( piece, MathX.Clamp( lastSpeed / 400f, 0.4f, 1.6f ) * (Stuff == GibKind.Jelly ? 2f : 1f) );
			}
			else
			{
				// Hard pieces clack, click or chime instead of squelching.
				var (sound, pitch) = Stuff switch
				{
					GibKind.Rock => ("hit_world", Rng.Float( 0.6f, 0.8f )),
					GibKind.Armour => ("hit_world", Rng.Float( 1.3f, 1.6f )),
					GibKind.Glass => ("glass_hit", Rng.Float( 1.2f, 1.6f )),
					_ => ("crab_snap", Rng.Float( 1.2f, 1.5f )),
				};
				if ( Clock.Now - lastClack > 0.05 ) { lastClack = Clock.Now; Sfx.Play( sound, GlobalPosition, 0.25f + loud * 0.5f, pitch ); }
			}
		}

		// Come to rest: a puddle of its goo underneath (not when it floats: it isn't resting on anything).
		if ( Soft && goo is Color pool && !pooled && !Floating && age > 0.5f && speed < 15f )
		{
			pooled = true;
			if ( gore.TraceWorld( GlobalPosition + Vector3.Up * 20f * U.In, GlobalPosition + Vector3.Down * 120f * U.In ) is { } hit )
				gore.GooSplat( hit.Position, hit.Normal, Rng.Float( 1.4f, 2.4f ), pool );
		}
		lastSpeed = speed;
	}
}
