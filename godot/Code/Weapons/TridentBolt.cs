using Godot;

/// <summary>
/// A trident in flight from the trident shooter: it leaves the muzzle for the point under the crosshair, flies fast
/// with a slight drop, turning slowly about its shaft, streaming tiny bubbles under water. It hits the first thing in
/// its way (traced each frame along its path, so it never skips through): an enemy takes the damage and the trident
/// goes on through, then heat seeks the next closest enemies (`SeekNext` of them), going through each, and sticks in
/// the last (a charged one flies faster and is magical); a loose piece is struck and pinned; the world gets an impact and the trident stays stuck there.
/// Stuck tridents shrink away after a few seconds.
/// </summary>
public partial class TridentBolt : Node3D
{
	public const float Speed = 65f;           // m/s (40 at first: it felt slow to land)
	public const float Drop = 1.5f;           // m/s² downward (the water carries it, but not all the way)
	public const float MaxFlight = 2.5f;      // seconds before an unspent trident sinks away
	const float StuckFor = 4f;                // seconds a trident stays where it hit
	const float SinkIn = 0.2f;                // how far the points go in (metres)
	const float TipReach = 0.58f;             // the middle point's tip, ahead of the trident's centre (TridentModel)

	float damage;
	PlayerController shooter;
	PlayerWeapon weapon;
	Vector3 velocity;
	Vector3 cameraStart;
	bool checkedStart;
	bool stuck;
	float roll;
	TimeSince age;
	TimeSince sinceStuck;
	Vector3 stuckScale = Vector3.One;
	Godot.Collections.Array<Rid> exclude;
	/// <summary> A charged shot: faster and magical. </summary>
	bool charged;
	public const float ChargedSpeed = 1.8f;   // times Speed
	/// <summary> After the first enemy it hits, a trident seeks this many more (the next closest each time) and goes through them. </summary>
	public const int SeekNext = 2;
	/// <summary> Seeking after the first hit flies at this share of Speed (user's choice: 45% of Speed, so the chain can be seen). </summary>
	public const float SeekSpeed = 0.45f;
	/// <summary> Metres from the last hit to the next enemy it will go for (user's choice: 20, so it starts weak), plus 1 per Trident Reach. </summary>
	public const float SeekRange = 20f;
	public const float SeekTurn = 40f;        // radians a second it can turn while seeking (at full speed a slow turn swung wide of the next enemy)
	int hits;
	readonly HashSet<Node> struck = new();
	Creature seeking;
	/// <summary> Tests: enemies the last trident hit (the first plus the ones it sought). </summary>
	public static int LastHits;

	/// <summary> Fires one from `muzzle` toward `target`. `cameraStart` is where the shot was aimed from (checked first, so a gun poked into a wall still hits the wall). </summary>
	public static TridentBolt Launch( Node parent, Vector3 muzzle, Vector3 target, Vector3 aim, Vector3 cameraStart, float damage, PlayerController shooter, PlayerWeapon weapon, bool charged = false )
	{
		var direction = (target - muzzle).SafeNormal();
		// A target nearer than the muzzle (something right in your face) would send it backward: fly along the aim.
		if ( direction == Vector3.Zero || direction.Dot( aim ) < 0.2f ) direction = aim;
		var bolt = new TridentBolt
		{
			Name = "Trident",
			damage = damage,
			shooter = shooter,
			weapon = weapon,
			velocity = direction * Speed * (charged ? ChargedSpeed : 1f),
			charged = charged,
			cameraStart = cameraStart,
			exclude = new Godot.Collections.Array<Rid> { shooter.GetRid() },
		};
		parent.AddChild( bolt );
		bolt.GlobalPosition = muzzle;
		bolt.Face();
		TridentModel.BuildTrident( bolt );
		bolt.age = 0f;
		LastHits = 0;
		if ( charged ) Enchant( bolt );
		return bolt;
	}

	/// <summary> Points the trident (its -Z) along its flight, turned about the shaft by the roll. </summary>
	void Face()
	{
		var forward = velocity.SafeNormal();
		if ( forward == Vector3.Zero ) return;
		GlobalBasis = Shapes.LookAt( forward ) * new Basis( Vector3.Back, roll );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( stuck )
		{
			if ( sinceStuck > StuckFor )
			{
				var t = MathX.Clamp( (sinceStuck - StuckFor) / 0.5f, 0f, 1f );
				Scale = stuckScale * MathX.Lerp( 1f, 0.01f, t );
				if ( t >= 1f ) QueueFree();
			}
			return;
		}

		// First frame: anything between where the shot was aimed from and the muzzle (a wall the barrel pokes into).
		if ( !checkedStart )
		{
			checkedStart = true;
			var first = Trace.Ray( this, cameraStart, GlobalPosition, Layers.World | Layers.Creature | Layers.Gib, exclude );
			if ( first.Hit ) { Strike( first ); return; }
		}

		var from = GlobalPosition;
		if ( seeking is not null && (!IsInstanceValid( seeking ) || seeking.IsDead) ) seeking = NextTarget( from );
		if ( seeking is not null )
		{
			// Heat seeking: swing hard onto the next enemy, no drop.
			var want = (AimAt( seeking, from ) - from).SafeNormal();
			var heading = velocity.SafeNormal();
			var angle = heading.AngleTo( want );
			heading = heading.Slerp( want, MathF.Min( 1f, SeekTurn * dt / MathF.Max( angle, 0.001f ) ) ).Normalized();
			velocity = heading * velocity.Length();
		}
		else velocity += Vector3.Down * Drop * dt;
		var to = from + velocity * dt;
		// While seeking it only hits enemies: gibs from the last kill, kelp and hills used to stop the chain short.
		var tr = Trace.Ray( this, from, to, seeking is not null ? Layers.Creature : Layers.World | Layers.Creature | Layers.Gib, exclude );
		if ( tr.Hit ) { Strike( tr ); return; }
		GlobalPosition = to;
		roll += 5f * dt;
		Face();
		if ( UnderWater( to ) ) { Bubbles( from, to ); if ( charged ) Bubbles( from, to ); }
		if ( charged ) Sparkles( GetTree(), from, to );
		if ( age > MaxFlight ) QueueFree();
	}

	static StandardMaterial3D[] sparkleMaterials;

	/// <summary> A charged trident is magical: a violet and sea-green aura round its head, and a light that travels with it. </summary>
	public static void Enchant( Node3D on )
	{
		var aura = new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.7f, 0.4f, 1f, 0.35f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = new Color( 0.75f, 0.45f, 1f ), EmissionEnergyMultiplier = 3f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		aura.SetMeta( "no_caustics", true );
		var glow = new MeshInstance3D
		{
			Name = "Magic Aura", Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 8 }, MaterialOverride = aura,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Scale = new Vector3( 0.32f, 0.32f, 1.6f ), Position = new Vector3( 0f, 0f, -0.35f ),
		};
		on.AddChild( glow );
		on.AddChild( new OmniLight3D { LightColor = new Color( 0.6f, 0.45f, 1f ), LightEnergy = 2.5f, OmniRange = 3.5f, ShadowEnabled = false } );
	}

	/// <summary> Glittering specks in violet, sea green and gold left along a charged trident's path, twinkling out. </summary>
	public static void Sparkles( SceneTree tree, Vector3 from, Vector3 to )
	{
		sparkleMaterials ??= new[] { new Color( 0.75f, 0.45f, 1f ), new Color( 0.4f, 1f, 0.75f ), new Color( 1f, 0.85f, 0.35f ) }
			.Select( c => new StandardMaterial3D { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 4f } ).ToArray();
		var count = Math.Clamp( (int)(from.DistanceTo( to ) / 0.25f), 1, 8 );
		var scene = tree.CurrentScene;
		for ( var i = 0; i < count; i++ )
		{
			var speck = new MeshInstance3D
			{
				Mesh = new BoxMesh { Size = Vector3.One * Rng.Float( 0.03f, 0.06f ) }, MaterialOverride = sparkleMaterials[Rng.Int( 0, sparkleMaterials.Length - 1 )],
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			scene.AddChild( speck );
			speck.GlobalPosition = from.Lerp( to, Rng.Float() ) + Rng.InSphere( 0.12f );
			speck.RotationDegrees = new Vector3( 45f, Rng.Float( 0f, 90f ), 45f );
			var tween = speck.CreateTween();
			tween.TweenProperty( speck, "scale", Vector3.One * 0.01f, Rng.Float( 0.3f, 0.6f ) ).SetEase( Tween.EaseType.In );
			tween.Parallel().TweenProperty( speck, "global_position", speck.GlobalPosition + Rng.InSphere( 0.25f ), 0.5f );
			tween.TweenCallback( Callable.From( speck.QueueFree ) );
		}
	}

	public static bool UnderWater( Vector3 at ) => WaterFeel.IsOn() && (Sea.SurfaceY is not float top || at.Y < top);

	/// <summary>
	/// A thin trail of tiny bubbles along this frame's path, rising and fading quickly. Some of them pop when their
	/// time is up: a little ring of specks, and now and then a faint pop.
	/// </summary>
	public static void Bubbles( Vector3 from, Vector3 to )
	{
		var length = from.DistanceTo( to );
		var count = Math.Clamp( (int)(length / 0.14f), 1, 10 );
		var tree = Engine.GetMainLoop() as SceneTree;
		for ( var i = 0; i < count; i++ )
		{
			var at = from.Lerp( to, (i + Rng.Float()) / count ) + Rng.InSphere( 0.03f );
			var life = Rng.Float( 0.45f, 1f );
			WaterBubble.Create( at, Rng.Float( 0.12f, 0.3f ), 0f, life );
			if ( tree is null || Rng.Float() > 0.3f ) continue;
			var popAt = at + Vector3.Up * life * 0.35f;   // about where it has risen to by then
			tree.CreateTimer( life * 0.95f, false ).Timeout += () => Pop( popAt );
		}
	}

	/// <summary> A bubble popping: a few specks flung out in a ring, gone in a blink. </summary>
	public static void Pop( Vector3 at )
	{
		for ( var k = 0; k < 4; k++ )
		{
			var a = k * MathF.PI * 0.5f + Rng.Float( 0f, 1f );
			WaterBubble.Create( at + new Vector3( MathF.Cos( a ), Rng.Float( -0.3f, 0.6f ), MathF.Sin( a ) ) * 0.05f, Rng.Float( 0.05f, 0.1f ), 0f, Rng.Float( 0.12f, 0.22f ) );
		}
		if ( Rng.Float() < 0.25f ) Sfx.Play( "bubble_pop", at, 0.22f, Rng.Float( 0.85f, 1.4f ) );
	}

	void Strike( TraceResult tr )
	{
		var direction = velocity.SafeNormal();
		var shootable = Shootables.Find( tr.Node );
		// A glow bulb pops and the trident flies on through it.
		if ( shootable is GlowBulb bulb )
		{
			if ( IsInstanceValid( shooter ) ) bulb.ReceiveShot( damage, tr.Node, direction, tr.HitPosition, cameraStart, shooter, true );
			if ( tr.Node is CollisionObject3D through ) exclude.Add( through.GetRid() );
			GlobalPosition = tr.HitPosition + direction * 0.05f;
			return;
		}
		if ( shootable is not null && shootable.ShotOwner is not null )
		{
			var wasAlive = !shootable.IsDead;
			var owner = shootable.ShotOwner;
			Fx.ImpactFlesh( tr.HitPosition, -direction, new Color( 0.9f, 0.3f, 0.2f ) );
			// The steel going in, then the flesh: heard at the hit and (quieter) by the shooter wherever it lands.
			Sfx.Play( "trident_pierce", tr.HitPosition, 1f, Rng.Float( 0.95f, 1.1f ) );
			Sfx.Play( "trident_flesh", tr.HitPosition, 0.9f, Rng.Float( 0.9f, 1.1f ) );
			Sfx.Play2D( "trident_pierce", 0.3f, Rng.Float( 0.95f, 1.1f ) );
			if ( IsInstanceValid( shooter ) ) shootable.ReceiveShot( damage, tr.Node, direction, tr.HitPosition, cameraStart, shooter, true );
			if ( wasAlive && IsInstanceValid( weapon ) )
			{
				weapon.MarkHit();
				if ( shootable.IsDead ) weapon.MarkKill();
			}
			hits++;
			LastHits = hits;
			struck.Add( owner );
			if ( seeking == owner ) seeking = null;
			var next = hits <= SeekNext + (PlayerPerks.Of( shooter )?.ExtraPierce ?? 0) ? NextTarget( tr.HitPosition ) : null;
			if ( next is not null )
			{
				// Through it and on to the next closest enemy: none of this creature's parts stop it again; a burst of bubbles out the far side.
				seeking = next;
				age = 0f;
				// It comes out of the enemy already turned onto the next one, at the plain speed so it can follow it.
				velocity = (AimAt( next, tr.HitPosition ) - tr.HitPosition).SafeNormal() * Speed * SeekSpeed;
				foreach ( var part in CreaturePart.All )
					if ( part.ShotOwner == owner && IsInstanceValid( part ) ) exclude.Add( part.GetRid() );
				if ( tr.Node is CollisionObject3D body ) exclude.Add( body.GetRid() );
				GlobalPosition = tr.HitPosition + direction * 0.05f;
				AlienBlast.Spawn( GetTree().CurrentScene, tr.HitPosition, 0.45f );   // a little magic flash as it goes through
				if ( UnderWater( tr.HitPosition ) ) Bubbles( tr.HitPosition, tr.HitPosition + direction * 0.8f );
				return;
			}
		}
		else if ( tr.Node is Gib gib )
		{
			gib.TakeHit( tr.HitPosition, direction, damage, 260f );
		}
		else if ( tr.Node is SeaMineShell ) SeaMine.Tink( tr.HitPosition, tr.Normal );
		else
		{
			Fx.ImpactWorld( tr.HitPosition, tr.Normal );
			Sfx.Play( "hit_world", tr.HitPosition, 0.8f, 0.8f );
		}
		StickAt( tr, direction );
	}

	/// <summary> The closest live enemy to `from` within SeekRange (plus Trident Reach) that this trident hasn't hit yet. </summary>
	Creature NextTarget( Vector3 from )
	{
		Creature best = null;
		var bestDistance = SeekRange + (PlayerPerks.Of( shooter )?.ExtraSeekRange ?? 0f);
		foreach ( var c in Creature.Everyone )
		{
			if ( !IsInstanceValid( c ) || !c.IsEnemy || c.IsDead || struck.Contains( c ) ) continue;
			var d = from.DistanceTo( c.GlobalPosition );
			if ( d < bestDistance ) { bestDistance = d; best = c; }
		}
		return best;
	}

	/// <summary> Where to fly at an enemy: the middle of its hit shapes (the parts' colliders), else a little above its origin. </summary>
	static Vector3 AimAt( Creature c, Vector3 from )
	{
		var sum = Vector3.Zero;
		var count = 0;
		foreach ( var part in CreaturePart.All )
		{
			if ( part.ShotOwner != c || !IsInstanceValid( part ) || part.Collider is null ) continue;
			sum += part.Collider.GlobalPosition;
			count++;
		}
		return count > 0 ? sum / count : c.GlobalPosition + Vector3.Up * 0.5f;
	}

	/// <summary> Stays where it hit, points sunk in, carried along by what it hit if that moves (a creature, a piece). </summary>
	void StickAt( TraceResult tr, Vector3 direction )
	{
		stuck = true;
		sinceStuck = 0f;
		// The points a little way into what it hit.
		GlobalPosition = tr.HitPosition - direction * (TipReach - SinkIn);
		Face();
		if ( tr.Node is { } hit && IsInstanceValid( hit ) && hit.IsInsideTree() && (Shootables.Find( hit ) is not null || hit is Gib) )
			CallDeferred( MethodName.Pin, hit );
	}

	void Pin( Node3D to )
	{
		if ( !IsInstanceValid( to ) || !to.IsInsideTree() || !IsInsideTree() ) return;
		Reparent( to, true );
		stuckScale = Scale;   // keeping its size under a scaled part leaves a local scale: shrink from that
	}
}
