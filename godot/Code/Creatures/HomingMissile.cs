using Godot;

/// <summary>
/// MC-51's homing missiles (they took the place of his cards): fat cartoon rockets in the game's colours (a green nose
/// with his one red eye in the middle of its face and a mean toothy grin, a purple body with yellow bands and fins, a
/// green and yellow flame). Launched from his hand, each drifts slowly upward for 5 to 8 seconds, then tips over and
/// dives after the nearest player, turning only so fast (they can be dodged). The body is a chain of sections that
/// bends toward where it is turning, wobbling a little. They have little health: shoot one and it bursts harmlessly;
/// one that reaches a player explodes on them. Built in code (Sculpt), metres, flying along -Z.
/// </summary>
public partial class HomingMissile : Creature
{
	public static readonly List<HomingMissile> All = new();

	public float RiseSpeed = 0.975f;   // 25% slower than the first cut (1.3)
	public float DiveSpeed = 6.6f;   // 11 at first, then 25% slower (8.25), then 20% slower again
	public float TurnRate = 2.6f;          // radians per second while diving
	public float Damage = 15f;
	public float HitRadius = 1f;           // from a player's middle
	/// <summary> How long it climbs before it dives (6.7 to 10.7 seconds unless set). </summary>
	public float RiseTime = -1f;
	public const float MaxHealth = 30f;
	/// <summary> Its size against the first cut: 15% longer, and 15% times 10% wider. </summary>
	public const float Length = 1.15f, Girth = 1.15f * 1.1f;
	/// <summary> Tests: how many have hit a player, and how many were shot down. </summary>
	public static int PlayerHits, ShotDown;

	static readonly Color Nose = new( 0.3f, 0.9f, 0.3f );
	static readonly Color Body = new( 0.5f, 0.18f, 0.8f );
	static readonly Color Trim = new( 1f, 0.82f, 0.15f );
	static readonly Color Iris = new( 1f, 0.12f, 0.08f );

	readonly List<Node3D> joints = new();
	Node3D flame;
	Node3D owner;
	Vector3 velocity;
	Vector3 heading = Vector3.Up;      // where it points
	Vector3 trail = Vector3.Up;        // where it was pointing a moment ago (the bend follows the difference)
	Vector3 drift;
	TimeSince age;
	float phase;
	bool diving, done;

	public static HomingMissile Launch( Node parent, Vector3 from, Vector3 outward, Node3D owner, float riseTime = -1f )
	{
		var m = new HomingMissile { Name = "Missile", owner = owner, RiseTime = riseTime };
		m.drift = outward.Flat().SafeNormal() * Rng.Float( 1.2f, 2.4f );
		return Place( m, parent, from, MaxHealth );
	}

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	protected override void Setup()
	{
		BeginSetup( MaxHealth );
		// Climbs as high as before at 75% of the speed, so for a third longer (6.7 to 10.7 s).
		if ( RiseTime <= 0f ) RiseTime = Rng.Float( 5f, 8f ) / 0.75f;
		phase = Rng.Float( 0f, MathF.Tau );
		age = 0f;
		heading = trail = (Vector3.Up + drift * 0.3f).Normalized();
		Build();
		Sfx.Play( "missile_launch", GlobalPosition, 0.7f, Rng.Float( 0.9f, 1.15f ) );
	}

	void Build()
	{
		// Black in the face; the body in worn silver, rust and black, section by section, with rings to match.
		var nose = Sculpt.Paint( new Color( 0.035f, 0.035f, 0.04f ), 0.3f, 0.5f );
		var silver = Sculpt.Paint( new Color( 0.66f, 0.68f, 0.7f ), 0.3f, 0.85f );
		var rust = Sculpt.Paint( new Color( 0.55f, 0.24f, 0.1f ), 0.8f, 0.3f );
		var black = Sculpt.Paint( new Color( 0.06f, 0.06f, 0.07f ), 0.4f, 0.6f );
		var sections = new[] { silver, rust, black };
		var rings = new[] { rust, black, silver };
		var trim = rust;
		var white = Sculpt.Paint( new Color( 0.97f, 0.97f, 0.95f ), 0.25f );
		var dark = Sculpt.Paint( new Color( 0.05f, 0.03f, 0.08f ), 0.4f );
		var iris = Sculpt.Glow( Iris, 2.2f );
		const float r = 0.105f;   // slim: about 21 cm across

		// The nose section (the root): a fat rounded head with the face on its front.
		var head = Sculpt.Joint( this, Vector3.Zero, "Nose" );
		joints.Add( head );
		Sculpt.Blob( head, new Vector3( 0f, 0f, -0.12f ), new Vector3( r * 2.1f, r * 2.1f, 0.5f ), nose );
		Sculpt.Blob( head, new Vector3( 0f, 0f, 0.04f ), new Vector3( r * 2.2f, r * 2.2f, 0.12f ), silver );   // the collar
		// His one eye, in the middle of its face: white ball, red iris, black pupil, an angry silver brow over it.
		var ball = Sculpt.Blob( head, new Vector3( 0f, 0.02f, -0.335f ), new Vector3( 0.11f, 0.11f, 0.08f ), white );
		var ring = Sculpt.Blob( head, new Vector3( 0f, 0.02f, -0.37f ), new Vector3( 0.065f, 0.065f, 0.025f ), iris );
		var dot = Sculpt.Blob( head, new Vector3( 0f, 0.02f, -0.382f ), new Vector3( 0.03f, 0.03f, 0.012f ), dark );
		Blinker.Add( this ).Eye( ball, ring, dot );
		foreach ( var s in new[] { -1f, 1f } )
			Sculpt.Block( head, new Vector3( s * 0.03f, 0.078f, -0.335f ), new Vector3( 0.07f, 0.02f, 0.04f ), silver, new Basis( Vector3.Back, -s * 0.45f ) );
		// The grin: a wide mean smile, its corners pulled up, the gap dark red, pointed teeth clenched top and bottom.
		var gum = Sculpt.Paint( new Color( 0.35f, 0.02f, 0.03f ), 0.5f );
		static float Smile( float x ) => -0.068f + x * x / (0.065f * 0.065f) * 0.032f;
		// On the face's front: the nose blob is an ellipsoid 0.11 by 0.11 by 0.25 (radii) round z -0.12.
		static float Face( float x, float y ) => -0.12f - 0.25f * MathF.Sqrt( MathF.Max( 0.05f, 1f - (x / 0.11f) * (x / 0.11f) - (y / 0.11f) * (y / 0.11f) ) );
		const int steps = 8;
		for ( var k = 0; k < steps; k++ )
		{
			var x0 = -0.065f + 0.13f * k / steps;
			var x1 = -0.065f + 0.13f * (k + 1) / steps;
			Sculpt.Limb( head, new Vector3( x0, Smile( x0 ), Face( x0, Smile( x0 ) ) + 0.004f ), new Vector3( x1, Smile( x1 ), Face( x1, Smile( x1 ) ) + 0.004f ), 0.014f, 0.014f, gum, "Mouth" );
		}
		for ( var k = 0; k < 6; k++ )
		{
			// Upper fangs point down; the lower ones point up between them.
			var xu = -0.0525f + k * 0.021f;
			var yu = Smile( xu );
			Sculpt.Cone( head, new Vector3( xu, yu + 0.014f, Face( xu, yu ) - 0.002f ), new Vector3( xu, yu - 0.008f, Face( xu, yu ) - 0.004f ), 0.0095f, white, 6, "Tooth" );
			if ( k == 5 ) continue;
			var xl = xu + 0.0105f;
			var yl = Smile( xl );
			Sculpt.Cone( head, new Vector3( xl, yl - 0.014f, Face( xl, yl ) - 0.002f ), new Vector3( xl, yl + 0.008f, Face( xl, yl ) - 0.004f ), 0.0085f, white, 6, "Tooth" );
		}

		// Three body sections behind it, each a joint that bends, then the tail with its fins and flame.
		var parent = head;
		for ( var i = 0; i < 3; i++ )
		{
			var joint = Sculpt.Joint( parent, new Vector3( 0f, 0f, i == 0 ? 0.1f : 0.22f ), $"Section {i}" );
			joints.Add( joint );
			Sculpt.Limb( joint, new Vector3( 0f, 0f, -0.02f ), new Vector3( 0f, 0f, 0.24f ), r, r * (i == 2 ? 0.8f : 1f), sections[i] );
			Sculpt.Limb( joint, new Vector3( 0f, 0f, 0.19f ), new Vector3( 0f, 0f, 0.235f ), r * 1.1f, r * 1.1f, rings[i] );   // a ring
			parent = joint;
		}
		for ( var k = 0; k < 4; k++ )   // fins
		{
			var turn = new Basis( Vector3.Back, k * MathF.PI * 0.5f + MathF.PI * 0.25f );
			Sculpt.Block( parent, turn * new Vector3( 0f, r + 0.06f, 0.16f ), new Vector3( 0.02f, 0.17f, 0.2f ), k % 2 == 0 ? silver : rust, turn * new Basis( Vector3.Right, -0.5f ) );
		}
		Sculpt.Limb( parent, new Vector3( 0f, 0f, 0.22f ), new Vector3( 0f, 0f, 0.3f ), r * 0.7f, r * 0.9f, dark, "Nozzle" );
		flame = Sculpt.Joint( parent, new Vector3( 0f, 0f, 0.3f ), "Flame" );
		Sculpt.Cone( flame, Vector3.Zero, new Vector3( 0f, 0f, 0.5f ), r * 0.8f, Sculpt.Glow( Trim, 3f ) );
		Sculpt.Cone( flame, Vector3.Zero, new Vector3( 0f, 0f, 0.32f ), r * 0.5f, Sculpt.Glow( new Color( 0.6f, 1f, 0.5f ), 4f ) );
		flame.AddChild( new OmniLight3D { LightColor = new Color( 0.7f, 1f, 0.4f ), LightEnergy = 1.2f, OmniRange = 3f, ShadowEnabled = false } );

		// 15% bigger than the first cut and 10% thicker on top of that: the whole body scales from the nose joint.
		head.Scale = new Vector3( Girth, Girth, Length );

		var part = CreaturePart.Create( this, 0, "Missile", 0.26f * Girth );   // a little wider than it looks, so it can be shot
		AddChild( part );
		part.Position = new Vector3( 0f, 0f, 0.15f * Length );
		var tailPart = CreaturePart.Create( this, 1, "Missile Tail", 0.24f * Girth );
		AddChild( tailPart );
		tailPart.Position = new Vector3( 0f, 0f, 0.6f * Length );
	}

	public override void _Process( double delta )
	{
		if ( health is null || joints.Count == 0 || done ) return;
		var dt = (float)delta;
		var t = (float)age + phase;
		// The flame flickers; the body bends toward its turn and wobbles like a cartoon rocket.
		flame.Scale = new Vector3( 1f, 1f, 0.8f + MathF.Sin( t * 31f ) * 0.2f + MathF.Sin( t * 47f ) * 0.12f );
		trail = trail.Slerp( heading, 1f - MathF.Exp( -2.2f * dt ) ).SafeNormal();
		if ( trail == Vector3.Zero ) trail = heading;
		var local = GlobalBasis.Inverse() * trail;
		var yaw = MathX.Clamp( MathF.Atan2( -local.X, -local.Z ), -0.5f, 0.5f );
		var pitch = MathX.Clamp( MathF.Atan2( local.Y, MathF.Sqrt( local.X * local.X + local.Z * local.Z ) ), -0.5f, 0.5f );
		for ( var i = 1; i < joints.Count; i++ )
			joints[i].Rotation = new Vector3( pitch * 0.75f + MathF.Sin( t * 7f - i * 1.1f ) * 0.07f, yaw * 0.75f + MathF.Cos( t * 6f - i * 1.3f ) * 0.07f, 0f );
		if ( DevFlags.FreezeEnemies || Puppet ) return;

		var prey = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		if ( !diving )
		{
			// Climbing slowly, drifting out from him and weaving, nose up. Not through a roof or out of the sea.
			var rise = RiseSpeed;
			var roof = Trace.Ray( this, GlobalPosition, GlobalPosition + Vector3.Up * 2f, Layers.World );
			if ( roof.Hit || (Sea.SurfaceY is float top && GlobalPosition.Y > top - 2f) ) rise = 0f;
			drift *= MathF.Exp( -0.5f * dt );
			var weave = new Vector3( MathF.Sin( t * 1.7f ), 0f, MathF.Cos( t * 1.3f ) ) * 0.5f;
			velocity = velocity.Lerp( Vector3.Up * rise + drift + weave, 1f - MathF.Exp( -3f * dt ) );
			var wantUp = (Vector3.Up + (drift + weave) * 0.35f).Normalized();
			heading = heading.Slerp( wantUp, 1f - MathF.Exp( -3f * dt ) ).SafeNormal();
			if ( age > RiseTime && prey is not null ) { diving = true; Sfx.Play( "missile_launch", GlobalPosition, 0.8f, 1.5f ); }
		}
		else
		{
			// Tipped over and after them: turning only so fast, speeding up.
			if ( prey is not null )
			{
				var want = (prey.WorldPosition + Vector3.Up * 1f - GlobalPosition).SafeNormal();
				var angle = heading.AngleTo( want );
				if ( angle > 0.001f ) heading = heading.Slerp( want, MathF.Min( 1f, TurnRate * dt / angle ) ).SafeNormal();
			}
			// Slow while it is turned away from them and fast once lined up, so it comes round onto a player close by
			// instead of circling them (at full speed its turning circle is 4 m wide).
			var lined = prey is not null ? MathX.Clamp( heading.Dot( (prey.WorldPosition + Vector3.Up * 1f - GlobalPosition).SafeNormal() ), 0f, 1f ) : 1f;
			var speed = MathX.Lerp( velocity.Length(), DiveSpeed * MathX.Lerp( 0.25f, 1f, lined * lined ), 1f - MathF.Exp( -4f * dt ) );
			velocity = heading * speed;
			if ( age > RiseTime + 9f ) { Explode( false ); return; }   // lost: it gives up
		}
		if ( heading == Vector3.Zero ) heading = Vector3.Up;
		GlobalBasis = Shapes.LookAt( heading );

		var from = GlobalPosition;
		var to = from + velocity * dt;
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive ) continue;
			var middle = player.RealPosition + Vector3.Up * 1f;
			if ( Geometry3D.GetClosestPointToSegment( middle, from, to + heading * 0.4f ).DistanceTo( middle ) > HitRadius ) continue;
			GlobalPosition = to;
			Explode( false, player );
			return;
		}
		if ( diving )
		{
			var wall = Trace.Ray( this, from, to + heading * 0.4f, Layers.World );
			if ( wall.Hit ) { GlobalPosition = wall.HitPosition - heading * 0.2f; Explode( false ); return; }
		}
		GlobalPosition = Sculpt.InBounds( to );
		if ( TridentBolt.UnderWater( to ) && Rng.Float() < 0.5f )
			WaterBubble.Create( joints[^1].GlobalPosition + Rng.InSphere( 0.08f ), Rng.Float( 0.15f, 0.4f ), 0f, Rng.Float( 0.5f, 1.1f ) );
	}

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( NetCreatures.Intercept( this, index, damage, direction, position, origin, shooter, scoreHit ) ) return;   // online: the host takes the hit and sends it
		if ( IsDead || done ) return;
		NoteShot( direction, position, origin );
		velocity += direction.SafeNormal() * 1.5f;
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage, Position = position, Origin = origin, Attacker = shooter } );
		if ( wasAlive && shooter is not null ) shooter.RewardHit( IsDead, false, scoreHit );
	}

	/// <summary> Shot out of the sky: it bursts where it is, hurting no one. </summary>
	protected override void OnDied()
	{
		if ( done ) return;
		ShotDown++;
		Explode( true );
	}

	void Explode( bool shotDown, PlayerSurvivor direct = null )
	{
		if ( done ) return;
		done = true;
		var at = GlobalPosition;
		AlienBlast.Spawn( GetTree().CurrentScene, at, 1f );
		if ( !shotDown )
			foreach ( var player in PlayerSurvivor.All )
			{
				if ( !player.IsAlive ) continue;
				var hit = player == direct || (player.RealPosition + Vector3.Up * 1f).DistanceTo( at ) < 1.8f;
				if ( !hit ) continue;
				PlayerHits++;
				player.Health?.OnDamage( new DamageInfo { Damage = Damage, Attacker = owner is not null && IsInstanceValid( owner ) ? owner : this, Position = at, Origin = at } );
				if ( player.Controller is { } c ) c.Velocity += ((c.GlobalPosition - at).Flat().SafeNormal() + Vector3.Up * 0.4f) * 4f;
			}
		RaiseKilled();
		QueueFree();
	}
}

/// <summary>
/// An alien explosion in green, purple and yellow: a white-yellow flash, a green fireball swelling inside a purple
/// shell, two shock rings, a spray of glowing shards, smoke puffs that rise, a flash of light, bubbles, a boom, and a
/// shake for players near it. `size` 1 is a missile; a sea mine is bigger.
/// </summary>
public partial class AlienBlast : Node3D
{
	const float Life = 0.9f;
	float size = 1f;
	TimeSince age;
	MeshInstance3D core, ball, shell;
	StandardMaterial3D coreM, ballM, shellM;
	readonly List<(MeshInstance3D Mesh, StandardMaterial3D Material, float Speed)> rings = new();
	// The shards and smoke puffs: drawn in batches (one MultiMesh per colour, sharing that colour's material, which all
	// of them fade alike), so a blast is about a dozen draws, not thirty-odd.
	const int ShardCount = 20, SmokeCount = 7;
	readonly Vector3[] shardAt = new Vector3[ShardCount], shardVelocity = new Vector3[ShardCount];
	readonly Basis[] shardTurn = new Basis[ShardCount];
	readonly Vector3[] smokeAt = new Vector3[SmokeCount], smokeVelocity = new Vector3[SmokeCount];
	readonly MultiMesh[] shardBatches = new MultiMesh[3], smokeBatches = new MultiMesh[2];
	readonly StandardMaterial3D[] shardMaterials = new StandardMaterial3D[3], smokeMaterials = new StandardMaterial3D[2];
	OmniLight3D light;

	static readonly Color Yellow = new( 1f, 0.9f, 0.3f );
	static readonly Color Green = new( 0.35f, 1f, 0.3f );
	static readonly Color Purple = new( 0.66f, 0.22f, 1f );

	public static void Spawn( Node parent, Vector3 at, float size )
	{
		var b = new AlienBlast { Name = "Alien Blast", size = size };
		parent.AddChild( b );
		b.GlobalPosition = at;
	}

	static StandardMaterial3D Fire( Color c, float energy )
	{
		var m = new StandardMaterial3D
		{
			AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		m.SetMeta( "no_caustics", true );
		return m;
	}

	MeshInstance3D Add( Mesh mesh, StandardMaterial3D material )
	{
		var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( mi );
		return mi;
	}

	public override void _Ready()
	{
		age = 0f;
		var sphere = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 20, Rings = 10 };
		core = Add( sphere, coreM = Fire( new Color( 1f, 1f, 0.85f ), 6f ) );
		ball = Add( sphere, ballM = Fire( Green, 3.5f ) );
		shell = Add( sphere, shellM = Fire( Purple with { A = 0.55f }, 2.5f ) );
		shellM.CullMode = BaseMaterial3D.CullModeEnum.Front;   // the far side only: a shell round the fireball, not a lid over it
		foreach ( var (colour, tilt, speed) in new[] { (Green, 0.2f, 3.2f), (Purple, 1.2f, 2.5f), (Yellow, -0.7f, 3.9f) } )
		{
			var material = Fire( colour, 4f );
			var ring = Add( new TorusMesh { InnerRadius = 0.465f, OuterRadius = 0.5f, Rings = 36, RingSegments = 6 }, material );
			ring.Rotation = new Vector3( tilt, Rng.Float( 0f, 3f ), tilt * 0.5f );
			rings.Add( (ring, material, speed) );
		}
		var shardMesh = new PrismMesh { Size = new Vector3( 0.07f, 0.3f, 0.07f ) };
		var shardColours = new[] { Yellow, Green, Purple };
		for ( var c = 0; c < 3; c++ )
		{
			shardMaterials[c] = Fire( shardColours[c], 5f );
			shardBatches[c] = Batch( shardMesh, shardMaterials[c], (ShardCount - c + 2) / 3 );
		}
		for ( var i = 0; i < ShardCount; i++ )
		{
			var dir = Rng.OnSphere();
			shardTurn[i] = new Basis( new Quaternion( Vector3.Up, MathF.Abs( dir.Dot( Vector3.Down ) ) > 0.99f ? Vector3.Up : dir ) );
			shardVelocity[i] = dir * Rng.Float( 5f, 11f ) * size;
		}
		for ( var c = 0; c < 2; c++ )
		{
			smokeMaterials[c] = Fire( (c == 0 ? Purple : Green).Darkened( 0.55f ) with { A = 0.5f }, 0.6f );
			smokeBatches[c] = Batch( sphere, smokeMaterials[c], (SmokeCount - c + 1) / 2 );
		}
		for ( var i = 0; i < SmokeCount; i++ )
		{
			smokeAt[i] = Rng.InSphere( 0.3f * size );
			smokeVelocity[i] = Rng.InSphere( 1.2f ) * size + Vector3.Up * Rng.Float( 0.8f, 1.8f );
		}
		PoseBatches( 0f );
		light = new OmniLight3D { LightColor = Green, LightEnergy = 6f * size, OmniRange = 9f * size, ShadowEnabled = false };
		AddChild( light );

		Sfx.Play( "missile_boom", GlobalPosition, MathX.Clamp( 0.8f + size * 0.2f, 0f, 1.3f ), Rng.Float( 0.9f, 1.1f ) / MathF.Sqrt( size ) );
		if ( TridentBolt.UnderWater( GlobalPosition ) )
			for ( var i = 0; i < 26; i++ ) WaterBubble.Create( GlobalPosition + Rng.InSphere( 1.1f * size ), Rng.Float( 0.3f, 1.1f ), Rng.Float( 0f, 0.15f ), Rng.Float( 0.6f, 1.6f ) );
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( player.Controller is not { } c ) continue;
			var d = c.GlobalPosition.DistanceTo( GlobalPosition );
			if ( d < 18f * size ) c.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( MathX.Clamp( 1f - d / (18f * size), 0.1f, 1f ) * size );
		}
	}

	MultiMesh Batch( Mesh mesh, StandardMaterial3D material, int count )
	{
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = count, Mesh = mesh };
		var view = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( view );
		return mm;
	}

	/// <summary> Shard i goes in batch i % 3 (its colour), smoke puff i in batch i % 2, as the old one-node-each had them. </summary>
	void PoseBatches( float t )
	{
		var ease = 1f - (1f - t) * (1f - t);
		var shardScale = size * (1f - t * 0.7f);
		for ( var i = 0; i < ShardCount; i++ )
			shardBatches[i % 3].SetInstanceTransform( i / 3, new Transform3D( shardTurn[i].Scaled( Vector3.One * shardScale ), shardAt[i] ) );
		var smokeScale = size * MathX.Lerp( 0.5f, 1.8f, ease );
		for ( var i = 0; i < SmokeCount; i++ )
			smokeBatches[i % 2].SetInstanceTransform( i / 2, new Transform3D( Basis.FromScale( Vector3.One * smokeScale ), smokeAt[i] ) );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var t = MathX.Clamp( (float)age / Life, 0f, 1f );
		var ease = 1f - (1f - t) * (1f - t);
		// The flash is gone in a blink; the fireball swells and thins; the shell outruns it.
		core.Scale = Vector3.One * size * MathX.Lerp( 0.6f, 2.2f, MathF.Min( 1f, t * 5f ) );
		coreM.AlbedoColor = coreM.AlbedoColor with { A = MathX.Clamp( 1f - t * 4f, 0f, 1f ) };
		ball.Scale = Vector3.One * size * MathX.Lerp( 0.4f, 2.2f, ease );
		ballM.AlbedoColor = Green.Lerp( Yellow, MathX.Clamp( 1f - t * 2.2f, 0f, 1f ) ) with { A = MathX.Clamp( 1.2f - t * 1.4f, 0f, 1f ) };
		shell.Scale = Vector3.One * size * MathX.Lerp( 0.5f, 3f, ease );
		shellM.AlbedoColor = Purple with { A = MathX.Clamp( 1f - t * 1.2f, 0f, 1f ) * 0.5f };
		foreach ( var (mesh, material, speed) in rings )
		{
			mesh.Scale = Vector3.One * size * (0.5f + ease * speed);
			material.AlbedoColor = material.AlbedoColor with { A = MathX.Clamp( 1f - t * 1.3f, 0f, 1f ) };
		}
		for ( var i = 0; i < ShardCount; i++ )
		{
			shardAt[i] += shardVelocity[i] * dt;
			shardVelocity[i] = shardVelocity[i] * MathF.Exp( -3.5f * dt ) + Vector3.Down * 1.5f * dt;
		}
		foreach ( var material in shardMaterials ) material.AlbedoColor = material.AlbedoColor with { A = MathX.Clamp( 1f - t * 1.1f, 0f, 1f ) };
		for ( var i = 0; i < SmokeCount; i++ ) smokeAt[i] += smokeVelocity[i] * dt * (1f - t * 0.5f);
		foreach ( var material in smokeMaterials ) material.AlbedoColor = material.AlbedoColor with { A = MathX.Clamp( MathF.Min( t * 6f, 1f - t ), 0f, 1f ) * 0.5f };
		PoseBatches( t );
		light.LightEnergy = 6f * size * (1f - t) * (1f - t);
		light.LightColor = Green.Lerp( Purple, t );
		if ( t >= 1f ) QueueFree();
	}
}
