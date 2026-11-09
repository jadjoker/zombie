using Godot;

/// <summary> A creature that takes bullets per body part (s&box ICreature). </summary>
public interface ICreature
{
	bool IsDead { get; }
	void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit );
}

/// <summary>
/// One shootable piece of a creature (s&box CreaturePart): a body on the creature layer, so bullets and melee find it
/// while the player walks through (the player only collides with the world).
/// </summary>
public partial class CreaturePart : StaticBody3D, IShootable
{
	public Node3D Creature;
	public int Index;
	public CollisionShape3D Collider;
	ICreature Owner => Creature as ICreature;

	/// <summary> Every creature part in the scene. </summary>
	public static readonly List<CreaturePart> All = new();
	public override void _EnterTree() => All.Add( this );
	public override void _ExitTree() => All.Remove( this );

	public Node ShotOwner => Creature;
	public bool IsDead => Creature is null || !IsInstanceValid( Creature ) || (Owner?.IsDead ?? true);

	public static CreaturePart Create( Node3D creature, int index, string name, float radius )
	{
		var part = new CreaturePart { Name = name, Creature = creature, Index = index, CollisionLayer = Layers.Creature, CollisionMask = 0 };
		part.Collider = new CollisionShape3D { Shape = new SphereShape3D { Radius = radius } };
		part.AddChild( part.Collider );
		return part;
	}

	public void ReceiveShot( float damage, Node3D hitPart, Vector3 direction, Vector3 position, Vector3 origin, Node attacker, bool scoreHit )
	{
		if ( Creature is null || !IsInstanceValid( Creature ) ) return;
		var shooter = (attacker as PlayerController)?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) ?? attacker as PlayerSurvivor;
		Owner?.ReceiveShot( Index, damage, direction, position, origin, shooter, scoreHit );
	}
}

/// <summary>
/// Port of the s&box Bloober: a snake made of spotted blobs that floats and slithers through the air. Its spots sit
/// flat most of the time and puff out into spikes like a pufferfish (taking less damage while puffed). It darts in to
/// bite, then peels away; a few bites in a row poison you with venom.
///
/// Chunking: every body segment has its own health. Shoot one off and it bursts, and the tail behind it drops as
/// loose pieces you can keep shooting, while the rest of the snake keeps coming, shorter. The head is the weak spot:
/// its health is the whole creature's. Sizes and speeds are the s&box ones in inches.
/// </summary>
public partial class Bloober : Node3D, ICreature
{
	public int SegmentCount = 9;
	public float HeadRadius = 13f;
	public float TailRadius = 5f;
	public float Stretch = 1.25f;
	public float Spacing = 0.72f;

	public float MoveSpeed = 170f;
	public float TurnRate = 2.6f;
	public float WiggleDegrees = 40f;
	public float WiggleRate = 2.4f;
	public float MinAltitude = 28f;
	public float AimHeight = 50f;

	public float BiteRange = 95f;
	public float BiteReach = 40f;
	public float BiteDamage = 10f;
	public float BiteCooldown = 1.4f;
	public float LungeSpeed = 560f;
	public float LungeTime = 0.3f;
	public float RetreatTime = 0.75f;

	public float SegmentHealth = 28f;
	public float HeadMultiplier = 2.2f;
	public float BodyMultiplier = 0.25f;
	public float ChipChance = 0.4f;

	public Vector2 PuffEvery = new( 7f, 12f );
	public float PuffDuration = 1.8f;
	public float PuffSize = 1.3f;
	public float PuffDamageScale = 0.5f;
	public float PuffWhenHurtChance = 0.2f;

	public float Hue = -1f;
	public bool Puffed;
	public bool Biting;
	public int GoneMask;

	public bool IsDead => health?.IsDead ?? false;
	public Color BodyColor { get; private set; }
	public Color SpotColor { get; private set; }
	public Color GooColor { get; private set; }

	public event Action<Bloober> Killed;

	/// <summary> All live Bloobers (s&box Scene.GetAllComponents). </summary>
	public static readonly List<Bloober> All = new();

	/// <summary> Turns the sculpted FBX so its face points along the body (checked against the eyes). </summary>
	public static Basis ModelFix = Basis.Identity;

	sealed class Spot
	{
		public MeshInstance3D Mesh;
		public Vector3 Dir;      // s&box local direction
		public float Phase;
	}

	sealed class Segment
	{
		public int Index;
		public CreaturePart Go;
		public Node3D Skin;
		public MeshInstance3D Renderer;
		public float Radius;
		public float Health;
		public float Hurt;
		public bool Gone;
		public Vector3 Position;
		public Vector3 Velocity;
		public TimeSince Flash = 99f;
		public float ColliderPuff = -1f;
		public Color Shade;
		public readonly List<Spot> Spots = new();
		public MeshInstance3D Mouth;
	}

	enum Mode { Chase, Lunge, Retreat }

	readonly List<Segment> segments = new();
	Health health;
	GoreSystem gore;
	Mode mode;
	TimeSince modeTime;
	TimeSince timeSinceBite = 999f;
	TimeSince timeSincePuff = 999f;
	TimeUntil nextPuff;
	Vector3 heading;
	Vector3 lungeDirection;
	bool bitThisLunge;
	float puff;
	float mouthOpen;
	float wigglePhase;
	bool diedLocally;
	TimeSince timeSinceDeath;
	Vector3 lastShotDirection = Vector3.Forward;
	TimeSince bleedTimer;
	bool lastShotPointBlank;

	public Health Health => health;

	/// <summary> Spawns a Bloober with this much health, facing a direction. </summary>
	public static Bloober Spawn( Node parent, Vector3 position, float maxHealth, Vector3? facing = null )
	{
		var b = new Bloober { Name = "Bloober" };
		parent.AddChild( b );
		b.GlobalPosition = position;
		if ( facing is { } f && f.Flat() != Vector3.Zero ) b.GlobalBasis = Shapes.LookAt( f.Flat() );
		var health = new Health();
		health.ResetTo( maxHealth );
		b.AddChild( health );
		return b;
	}

	public override void _Ready()
	{
		All.Add( this );
		CallDeferred( MethodName.Setup );
	}

	void Setup()
	{
		health = GetNodeOrNull<Health>( "Health" );
		if ( health is null ) { health = new Health(); health.ResetTo( 160f ); AddChild( health ); }
		gore = GoreSystem.Get();
		if ( Hue < 0f ) Hue = Rng.Float();
		var hue = Hue;
		BodyColor = Shapes.Hsv( hue, 0.85f, 0.36f );
		SpotColor = Shapes.Glow( hue + 0.45f, 2.2f );
		GooColor = Shapes.Hsv( hue + 0.18f, 0.95f, 1f );

		heading = -GlobalBasis.Z;
		wigglePhase = hue * 20f;
		nextPuff = Rng.Float( PuffEvery.X, PuffEvery.Y );
		health.Died += OnDied;
		BuildBody( hue );
	}

	public override void _ExitTree()
	{
		All.Remove( this );
		foreach ( var segment in segments )
			if ( !segment.Gone && IsInstanceValid( segment.Go ) ) segment.Go.QueueFree();
	}

	static readonly Dictionary<string, PackedScene> models = new();

	static Node3D LoadModel( string name )
	{
		if ( !models.TryGetValue( name, out var scene ) )
			models[name] = scene = ResourceLoader.Exists( $"res://assets/models/creatures/bloober/{name}.fbx" ) ? GD.Load<PackedScene>( $"res://assets/models/creatures/bloober/{name}.fbx" ) : null;
		return scene?.Instantiate<Node3D>();
	}

	void BuildBody( float hue )
	{
		var random = new Random( (int)(hue * 100000f) );
		float Range( float a, float b ) => a + (float)random.NextDouble() * (b - a);

		var count = Math.Clamp( SegmentCount, 2, 30 );
		var position = GlobalPosition;
		var back = GlobalBasis.Z;   // s&box -Forward
		var root = GetTree().CurrentScene;

		for ( var i = 0; i < count; i++ )
		{
			var t = i / (float)(count - 1);
			var radius = i == 0 ? HeadRadius : MathX.LerpUnclamped( HeadRadius * 0.95f, TailRadius, MathF.Pow( t, 0.8f ) ) * Range( 0.85f, 1.15f );
			if ( i > 0 ) position += back * (segments[i - 1].Radius + radius) * Spacing * U.In;

			var go = CreaturePart.Create( this, i, i == 0 ? "Bloober Head" : $"Bloober Segment {i}", radius * 1.1f * U.In );
			root.AddChild( go );
			go.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized(), position );

			// The sculpted skin, sized like the unit sphere it replaces.
			var skin = new Node3D { Name = "Skin" };
			go.AddChild( skin );
			var shade = Shapes.Hsv( hue + t * 0.07f, 0.85f - t * 0.1f, 0.36f - t * 0.06f );
			MeshInstance3D renderer;
			var model = LoadModel( i == 0 ? "head" : $"segment{i % 2}" );
			if ( model is not null )
			{
				model.Basis = ModelFix * Basis.FromScale( Vector3.One * (1f / 0.64f) );
				skin.AddChild( model );
				renderer = model.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>().First();
				renderer.MaterialOverride = Shapes.Material( shade, "eelskin" );
			}
			else
			{
				renderer = Shapes.Part( skin, "Sphere", Vector3.Zero, Vector3.One, shade, skin: "eelskin" );
			}

			var segment = new Segment { Index = i, Go = go, Skin = skin, Renderer = renderer, Radius = radius, Health = SegmentHealth, Position = position, Shade = shade };

			var spots = i == 0 ? 3 : random.Next( 3, 6 );
			for ( var s = 0; s < spots; s++ )
			{
				Vector3 dir;
				do dir = new Vector3( Range( -1f, 1f ), Range( -1f, 1f ), Range( -1f, 1f ) );
				while ( dir.LengthSquared() < 0.05f || (i == 0 && dir.Normalized().X > 0.2f) );
				var spot = Shapes.Part( go, "Spot", Vector3.Zero, Vector3.One, SpotColor, glow: true, shadow: false );
				segment.Spots.Add( new Spot { Mesh = spot, Dir = dir.Normalized(), Phase = Range( 0f, 6.28f ) } );
			}

			if ( i == 0 ) BuildFace( segment );
			if ( i == count - 1 ) BuildTail( segment );
			segments.Add( segment );
		}
	}

	void BuildTail( Segment tail )
	{
		var r = tail.Radius;
		foreach ( var side in new[] { -1f, 1f } )
		{
			var fin = Shapes.Part( tail.Go, "Fin", new Vector3( -r * 1.5f, side * r * 0.5f, r * 0.2f ), Vector3.One, SpotColor.Lerp( BodyColor, 0.4f ) );
			fin.Basis = new Angles( -10f, side * 30f, 0f ).ToBasis() * Basis.FromScale( Shapes.Ellipsoid( 1.3f * r, 0.35f * r, 0.7f * r ) );
		}
	}

	void BuildFace( Segment head )
	{
		var r = head.Radius;
		foreach ( var side in new[] { -1f, 1f } )
		{
			var eyeAt = new Vector3( r * 0.75f * Stretch, side * r * 0.45f, r * 0.5f );
			Shapes.Part( head.Go, "Eye", eyeAt, Shapes.Ellipsoid( r * 0.22f ), new Color( 0.04f, 0.02f, 0.03f ) );
			var k = r * 0.14f;
			Shapes.Part( head.Go, "Pupil", eyeAt + new Vector3( r * 0.17f, side * r * 0.02f, 0f ), Shapes.Ellipsoid( 0.3f * k, 1f * k, 0.75f * k ), SpotColor, glow: true );
			var k2 = r * 0.13f;
			Shapes.Part( head.Go, "Slit", eyeAt + new Vector3( r * 0.21f, side * r * 0.02f, 0f ), Shapes.Ellipsoid( 0.2f * k2, 0.18f * k2, 0.85f * k2 ), Colors.Black );
		}
		head.Mouth = Shapes.Part( head.Go, "Mouth", Vector3.Zero, Vector3.One, new Color( 0.05f, 0f, 0.015f ) );

		var tooth = new Color( 0.92f, 0.88f, 0.76f );
		for ( var i = 0; i < 6; i++ )
		{
			var y = (i - 2.5f) / 2.5f * r * 0.42f;
			Fang( head, new Vector3( r * Stretch * 0.86f, y, -r * 0.02f ), new Angles( 0, 0, 0 ), r * (i is 0 or 5 ? 0.3f : 0.2f), tooth );
		}
		for ( var i = 0; i < 4; i++ )
		{
			var y = (i - 1.5f) / 1.5f * r * 0.32f;
			Fang( head, new Vector3( r * Stretch * 0.82f, y, -r * 0.3f ), new Angles( 180, 0, 0 ), r * 0.17f, tooth );
		}
	}

	void Fang( Segment head, Vector3 at, Angles angles, float length, Color color )
	{
		var dir = angles.ToBasis() * Vector3.Down;
		Shapes.AddSpindle( head.Go, "Fang", at, dir, Vector3.Back, length * 0.24f, length * 1.25f, 0f, 0.3f, color );
	}

	// ---- Every frame ----

	public override void _Process( double delta )
	{
		if ( health is null ) return;
		if ( IsDead )
		{
			if ( timeSinceDeath > 1f ) QueueFree();
			return;
		}
		if ( !DevFlags.FreezeEnemies ) Think( (float)delta );
		if ( diedLocally ) return;

		var leaking = segments.Where( s => !s.Gone ).ToList();
		if ( leaking.Count > 0 ) CreatureGore.BleedTrail( leaking[Rng.Int( 0, leaking.Count - 1 )].Position, GooColor, health.Fraction, ref bleedTimer );

		puff = MathX.Approach( puff, Puffed ? 1f : 0f, (float)delta * (Puffed ? 6f : 2.5f) );
		mouthOpen = MathX.Approach( mouthOpen, Biting ? 1f : 0f, (float)delta * (Biting ? 14f : 5f) );
		UpdateBody( (float)delta );
	}

	void UpdateBody( float dt )
	{
		dt = MathF.Max( dt, 0.0001f );
		var puffScale = MathX.Lerp( 1f, PuffSize, Ease( puff ) );
		for ( var i = 0; i < segments.Count; i++ )
		{
			var segment = segments[i];
			if ( segment.Gone ) break;
			Vector3 position;
			Basis rotation;
			if ( i == 0 )
			{
				position = GlobalPosition;
				rotation = GlobalBasis.Orthonormalized();
			}
			else
			{
				var previous = segments[i - 1];
				var offset = segment.Position - previous.Position;
				if ( offset.LengthSquared() < 1e-8f ) offset = previous.Go.GlobalBasis.Z;
				var gap = (previous.Radius + segment.Radius) * Spacing * puffScale * U.In;
				position = previous.Position + offset.Normalized() * gap;
				rotation = Shapes.LookAt( previous.Position - position );
			}
			segment.Velocity = (position - segment.Position) / dt;
			segment.Position = position;
			segment.Go.GlobalTransform = new Transform3D( rotation, position );
			PoseSegment( segment, puffScale );
		}
	}

	void PoseSegment( Segment segment, float puffScale )
	{
		var r = segment.Radius * puffScale * (1f - 0.2f * segment.Hurt);
		segment.Skin.Scale = Shapes.Ellipsoid( Stretch * r, r, r );

		var baseColor = segment.Shade.Lerp( segment.Shade * 0.45f, segment.Hurt );
		var flash = MathX.Clamp( 1f - segment.Flash / 0.1f, 0f, 1f );
		Shapes.Tint( segment.Renderer, (baseColor with { A = 1f }).Lerp( Colors.White * 3f, flash ) );

		var p = Ease( puff );
		var time = (float)Clock.Now;
		foreach ( var spot in segment.Spots )
		{
			if ( !IsInstanceValid( spot.Mesh ) ) continue;
			var breathe = 0.85f + 0.15f * MathF.Sin( time * 3f + spot.Phase );
			var surface = new Vector3( spot.Dir.X * Stretch, spot.Dir.Y, spot.Dir.Z ) * r * MathX.Lerp( 0.97f, 1.12f, p );
			var thickness = MathX.Lerp( 0.1f, 0.85f, p ) * segment.Radius;
			var width = MathX.Lerp( 0.32f * breathe, 0.13f, p ) * segment.Radius;
			spot.Mesh.Transform = new Transform3D( Shapes.LookAt( U.Sbox( spot.Dir ) ) * Basis.FromScale( new Vector3( width, width, thickness ) * 2f * U.In ), U.Sbox( surface ) );
		}

		if ( segment.Mouth is not null )
		{
			var open = Ease( mouthOpen );
			segment.Mouth.Position = U.Sbox( r * Stretch * 0.86f, 0f, -r * 0.2f );
			segment.Mouth.Scale = Shapes.Ellipsoid( 0.2f * r, 0.5f * r, MathX.Lerp( 0.07f, 0.45f, open ) * r );
		}

		if ( MathF.Abs( puffScale - segment.ColliderPuff ) > 0.05f && segment.Go.Collider?.Shape is SphereShape3D sphere )
		{
			segment.ColliderPuff = puffScale;
			sphere.Radius = segment.Radius * puffScale * 1.1f * U.In;
		}
	}

	static float Ease( float t ) => t * t * (3f - 2f * t);

	// ---- Brain ----

	void Think( float dt )
	{
		if ( nextPuff <= 0f && !Puffed ) StartPuff();
		if ( Puffed && timeSincePuff > PuffDuration ) Puffed = false;

		var target = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var aim = target is null ? GlobalPosition + heading * 200f * U.In : target.WorldPosition + Vector3.Up * AimHeight * U.In;
		var toAim = aim - GlobalPosition;
		var lost = segments.Count( s => s.Gone );
		var speed = MoveSpeed * (1f + 0.05f * lost) * (Puffed ? 0.7f : 1f) * U.In;
		var now = (float)Clock.Now;

		switch ( mode )
		{
			case Mode.Chase:
			{
				var desired = toAim.SafeNormal();
				var wiggle = MathF.Sin( now * WiggleRate + wigglePhase );
				var bob = MathF.Sin( now * WiggleRate * 0.5f + wigglePhase * 1.7f );
				desired = desired.Rotated( Vector3.Up, MathX.Rad( wiggle * WiggleDegrees ) );
				desired = (desired + Vector3.Up * bob * 0.25f).SafeNormal();
				heading = heading.Lerp( desired, MathX.Clamp( TurnRate * dt, 0f, 1f ) ).SafeNormal();
				Move( heading * speed, dt );
				if ( target is not null && toAim.Length() < BiteRange * U.In && timeSinceBite > BiteCooldown )
				{
					mode = Mode.Lunge;
					modeTime = 0f;
					lungeDirection = toAim.SafeNormal();
					bitThisLunge = false;
					Biting = true;
				}
				break;
			}
			case Mode.Lunge:
			{
				heading = lungeDirection;
				Move( lungeDirection * LungeSpeed * U.In, dt );
				if ( !bitThisLunge && target is not null && (aim - GlobalPosition).Length() < (BiteReach + HeadRadius) * U.In )
				{
					bitThisLunge = true;
					Bite( target );
				}
				if ( modeTime > LungeTime )
				{
					mode = Mode.Retreat;
					modeTime = 0f;
					Biting = false;
					timeSinceBite = 0f;
					var side = Rng.Float() < 0.5f ? -1f : 1f;
					heading = (lungeDirection.Flat().SafeNormal().Rotated( Vector3.Up, MathX.Rad( side * Rng.Float( 100f, 150f ) ) ) + Vector3.Up * 0.35f).SafeNormal();
				}
				break;
			}
			case Mode.Retreat:
			{
				Move( heading * speed * 1.15f, dt );
				if ( modeTime > RetreatTime ) { mode = Mode.Chase; modeTime = 0f; }
				break;
			}
		}

		if ( heading != Vector3.Zero )
		{
			var from = GlobalBasis.Orthonormalized().GetRotationQuaternion();
			var to = Shapes.LookAt( heading ).GetRotationQuaternion();
			GlobalBasis = new Basis( from.Slerp( to, MathX.Clamp( dt * 10f, 0f, 1f ) ) );
		}
	}

	void Move( Vector3 velocity, float dt )
	{
		var position = GlobalPosition;
		var direction = velocity.SafeNormal();

		var look = Trace.Sphere( this, HeadRadius * 0.8f * U.In, position, position + direction * 70f * U.In, Layers.World );
		if ( look.Hit && !look.StartedSolid )
		{
			var slide = (direction - look.Normal * direction.Dot( look.Normal ) + look.Normal * 0.6f + Vector3.Up * 0.4f).SafeNormal();
			heading = heading.Lerp( slide, MathX.Clamp( dt * 8f, 0f, 1f ) ).SafeNormal();
			velocity = slide * velocity.Length();
		}

		var ground = Trace.Ray( this, position, position + Vector3.Down * 400f * U.In, Layers.World );
		if ( ground.Hit && position.Y - ground.HitPosition.Y < (MinAltitude + HeadRadius) * U.In )
			velocity = velocity.WithY( MathF.Max( velocity.Y, 120f * U.In ) );

		foreach ( var other in All )
		{
			if ( other == this || other.IsDead ) continue;
			var away = position - other.GlobalPosition;
			var distance = away.Length() / U.In;
			if ( distance < 50f && distance > 0.01f )
				velocity += away.Normalized() * (50f - distance) * 6f * U.In;
		}
		GlobalPosition = position + velocity * dt;
	}

	void Bite( PlayerSurvivor target )
	{
		target.Health?.OnDamage( new DamageInfo { Damage = BiteDamage, Attacker = this, Position = target.WorldPosition + Vector3.Up * AimHeight * U.In, Origin = GlobalPosition } );
		target.Afflictions?.Bitten();
	}

	void StartPuff()
	{
		Puffed = true;
		timeSincePuff = 0f;
		nextPuff = PuffDuration + Rng.Float( PuffEvery.X, PuffEvery.Y );
	}

	// ---- Getting shot ----

	public void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead || segments.Count == 0 ) return;
		if ( index < 0 || index >= segments.Count || segments[index].Gone )
			index = segments.Where( s => !s.Gone ).OrderBy( s => s.Position.DistanceSquaredTo( position ) ).First().Index;

		lastShotDirection = direction;
		lastShotPointBlank = (position - origin).Length() < CreatureGore.OverkillRange * U.In;
		var segment = segments[index];
		var amount = damage * (Puffed ? PuffDamageScale : 1f);
		var headshot = index == 0;

		var cut = false;
		float hurt;
		if ( headshot ) hurt = 1f - MathX.Clamp( (health.Current - amount * HeadMultiplier) / MathF.Max( 1f, health.MaxHealth ), 0f, 1f );
		else
		{
			segment.Health -= amount;
			cut = segment.Health <= 0f;
			hurt = 1f - MathX.Clamp( segment.Health / MathF.Max( 1f, SegmentHealth ), 0f, 1f );
		}

		if ( cut )
		{
			GoneMask |= TailMask( index );
			CutLocal( index, direction, quiet: false );
		}
		else
		{
			var chip = Rng.Float() < (headshot ? ChipChance * 0.6f : ChipChance);
			Hit( index, position, direction, chip, hurt );
		}

		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = amount * (headshot ? HeadMultiplier : BodyMultiplier), Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( !killed && !Puffed && timeSincePuff > 4f && Rng.Float() < PuffWhenHurtChance ) StartPuff();
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, headshot, scoreHit );
	}

	int TailMask( int from )
	{
		var mask = 0;
		for ( var i = from; i < segments.Count && i < CreatureGore.MaxMaskParts; i++ ) mask |= 1 << i;
		return mask;
	}

	void Hit( int index, Vector3 position, Vector3 direction, bool chip, float hurt )
	{
		var segment = segments[index];
		segment.Flash = 0f;
		segment.Hurt = MathF.Max( segment.Hurt, hurt );
		if ( !segment.Gone )
		{
			CreatureGore.AddWound( segment.Go, position, direction, BodyColor, GooColor, segment.Radius * 0.45f );
			CreatureGore.Jiggle( segment.Skin );
		}
		gore.GooBurst( position, -direction, GooColor, 6, 220f, 0.8f );
		gore.GooSpray( position, direction, 2, GooColor, 180f, 0.4f, 0.9f );
		if ( chip && !segment.Gone ) gore.ThrowBlob( position, -direction + direction * 0.3f, BodyColor, GooColor, segment.Radius * 0.35f );
	}

	void CutLocal( int index, Vector3 direction, bool quiet )
	{
		if ( index <= 0 || index >= segments.Count ) return;
		if ( !quiet && !segments[index].Gone )
		{
			var front = segments[index - 1];
			var cut = segments[index];
			var outward = (cut.Position - front.Position).SafeNormal();
			if ( !front.Gone ) CreatureGore.AddStump( front.Go, front.Position + outward * front.Radius * 0.85f * U.In, outward, front.Radius * 0.8f, BodyColor, GooColor );
			if ( index + 1 < segments.Count && !segments[index + 1].Gone )
			{
				var tail = segments[index + 1];
				var back = (cut.Position - tail.Position).SafeNormal();
				CreatureGore.AddStump( tail.Go, tail.Position + back * tail.Radius * 0.85f * U.In, back, tail.Radius * 0.8f, BodyColor, GooColor, 2.5f );
			}
		}
		for ( var i = index; i < segments.Count; i++ )
		{
			var segment = segments[i];
			if ( segment.Gone ) continue;
			segment.Gone = true;
			if ( quiet ) { segment.Go.QueueFree(); continue; }
			if ( i == index ) Burst( segment, direction, 14, 2 );
			else Detach( segment, direction * Rng.Float( 60f, 160f ) );
		}
	}

	void Burst( Segment segment, Vector3 direction, int drops, int blobs )
	{
		var position = segment.Position;
		gore.GooBurst( position, direction, GooColor, drops, 320f, 1.3f );
		gore.GooSpray( position, direction, 4, GooColor, 220f, 0.6f, 1.4f );
		for ( var b = 0; b < blobs; b++ ) gore.ThrowBlob( position, direction, BodyColor, GooColor, segment.Radius * Rng.Float( 0.35f, 0.5f ) );
		segment.Go.QueueFree();
	}

	/// <summary> A segment comes loose and falls as a physics piece, spots and all (push in s&box units/s). </summary>
	void Detach( Segment segment, Vector3 push )
	{
		var part = segment.Go;
		Shapes.Tint( segment.Renderer, BodyColor.Lerp( BodyColor * 0.45f, segment.Hurt ) with { A = 1f } );
		// Move the visible pieces onto a plain node and let the shootable part go.
		var piece = new Node3D { Name = "Piece" };
		GetTree().CurrentScene.AddChild( piece );
		piece.GlobalTransform = part.GlobalTransform;
		foreach ( var child in part.GetChildren().OfType<Node3D>().ToList() )
			if ( child is not CollisionShape3D ) child.Reparent( piece, true );
		part.QueueFree();
		var velocity = segment.Velocity * 0.4f + (push + Vector3.Up * Rng.Float( 40f, 120f )) * U.In;
		Gib.Attach( piece, gore, segment.Radius, 25f, 4f, velocity, GooColor );
		CreatureGore.Twitch( piece, Rng.Float( 3f, 5f ), 1.1f );
	}

	// ---- Death ----

	void OnDied()
	{
		timeSinceDeath = 0f;
		Killed?.Invoke( this );
		DieLocal( lastShotDirection, quiet: false, lastShotPointBlank );
	}

	void DieLocal( Vector3 direction, bool quiet, bool overkill = false )
	{
		if ( diedLocally ) return;
		diedLocally = true;
		timeSinceDeath = 0f;
		if ( overkill && !quiet ) CreatureGore.Overkill( segments[0].Position, direction, BodyColor, GooColor );
		foreach ( var segment in segments )
		{
			if ( segment.Gone ) continue;
			segment.Gone = true;
			if ( quiet ) { segment.Go.QueueFree(); continue; }
			if ( segment.Index == 0 ) Burst( segment, direction, 22, 3 );
			else if ( Rng.Float() < (overkill ? 0.7f : 0.25f) ) Burst( segment, direction, 8, 1 );
			else Detach( segment, (direction + Rng.InSphere( 0.8f )).SafeNormal() * Rng.Float( 60f, 180f ) );
		}
	}
}
