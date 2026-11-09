using Godot;

/// <summary>
/// Port of the s&box Squilien: a floating jellyfish-squid. A squashed dome bell with glowing spots, big slanted alien
/// eyes that blink a little out of step, a frill of curved spines and a glowing crown; tentacles hang underneath as a
/// soft verlet chain that lags, whips and flares on every swim stroke. It keeps its distance, circling you in pulses,
/// and lobs ink balls (two in a row ink your view). Tentacle segments can be shot off; the bell is the body.
/// Numbers are the s&box ones (inches).
/// </summary>
public partial class Squilien : Node3D, ICreature
{
	public float BellRadius = 22f;
	public int Tentacles = 6;
	public int TentacleSegments = 4;
	public float SegmentLength = 10f;
	public float TentacleRadius = 3.4f;
	public float PreferredDistance = 420f;
	public float HoverHeight = 150f;
	public float PulseInterval = 1.1f;
	public float PulseThrust = 240f;
	public float Drag = 1.4f;
	public float OrbitSpeed = 14f;
	public float MinAltitude = 60f;
	public float ThrowCooldown = 2.4f;
	public float ThrowRange = 1200f;
	public float InkSpeed = 750f;
	public float InkGravity = 300f;
	public float InkDamage = 8f;
	public float TentacleHealth = 12f;
	public float BellMultiplier = 1.1f;
	public float TentacleMultiplier = 0.12f;
	public float ChipChance = 0.35f;

	public float Hue = -1f;
	public int PulseCount;
	public static readonly List<Squilien> All = new();
	public bool IsDead => health?.IsDead ?? false;
	public Health Health => health;
	public Color BodyColor { get; private set; }
	public Color SpotColor { get; private set; }
	public Color GooColor { get; private set; }
	public Color InkColor { get; private set; }
	public event Action<Squilien> Killed;

	sealed class Piece
	{
		public int Index;
		public CreaturePart Go;
		public Node3D Skin;
		public MeshInstance3D Renderer;
		public float Radius;
		public float Health;
		public float Hurt;
		public bool Gone;
		public Vector3 Position;   // metres
		public Vector3 Velocity;   // s&box units/s
		public TimeSince Flash = 99f;
	}

	sealed class Tentacle
	{
		public float Angle;
		public float Phase;
		public readonly List<Piece> Pieces = new();
	}

	sealed class Eye
	{
		public Node3D Go, Glint;
		public Vector3 Scale, GlintScale;
		public float Closed;
		public float Start = -1f;
		public float Length = 0.16f;
	}

	Piece bell;
	readonly List<Eye> eyes = new();
	float nextBlink;
	bool doubleBlinkPending;
	readonly List<Tentacle> tentacles = new();
	readonly List<Piece> all = new();
	Health health;
	GoreSystem gore;
	Vector3 velocity;   // s&box units/s
	TimeUntil nextPulse;
	TimeUntil nextThrow;
	float orbitSign = 1f;
	int seenPulses;
	float squeeze;
	bool diedLocally;
	TimeSince timeSinceDeath;
	Vector3 lastShotDirection = Vector3.Forward;
	TimeSince bleedTimer;
	bool lastShotPointBlank;

	public static Squilien Spawn( Node parent, Vector3 position, float maxHealth )
	{
		var s = new Squilien { Name = "Squilien" };
		parent.AddChild( s );
		s.GlobalPosition = position;
		var h = new Health();
		h.ResetTo( maxHealth );
		s.AddChild( h );
		return s;
	}

	public override void _Ready()
	{
		All.Add( this );
		CallDeferred( MethodName.Setup );
	}

	public override void _ExitTree()
	{
		All.Remove( this );
		foreach ( var piece in all ) if ( !piece.Gone && IsInstanceValid( piece.Go ) ) piece.Go.QueueFree();
	}

	void Setup()
	{
		health = GetNodeOrNull<Health>( "Health" );
		if ( health is null ) { health = new Health(); health.ResetTo( 130f ); AddChild( health ); }
		gore = GoreSystem.Get();
		if ( Hue < 0f ) Hue = Rng.Float();
		var hue = Hue;
		BodyColor = Shapes.Hsv( hue, 0.85f, 0.38f );
		SpotColor = Shapes.Glow( hue + 0.5f, 2.2f );
		GooColor = Shapes.Hsv( hue + 0.35f, 0.95f, 1f );
		InkColor = Shapes.Hsv( 0.76f, 0.75f, 0.28f );
		nextPulse = Rng.Float( 0.1f, PulseInterval );
		nextThrow = Rng.Float( 1f, ThrowCooldown + 1f );
		orbitSign = Rng.Float() < 0.5f ? -1f : 1f;
		seenPulses = PulseCount;
		health.Died += OnDied;
		Build( hue );
	}

	static readonly Dictionary<string, PackedScene> models = new();

	static Node3D Model( string name )
	{
		if ( !models.TryGetValue( name, out var scene ) ) models[name] = scene = GD.Load<PackedScene>( $"res://assets/models/creatures/squilien/{name}.fbx" );
		return scene?.Instantiate<Node3D>();
	}

	void Build( float hue )
	{
		var random = new Random( (int)(hue * 100000f) + 7 );
		float Range( float a, float b ) => a + (float)random.NextDouble() * (b - a);

		bell = NewPart( "Squilien Bell", BellRadius, 0 );
		bell.Position = GlobalPosition;
		all.Add( bell );
		var bellGo = bell.Go;

		var spots = random.Next( 5, 8 );
		for ( var s = 0; s < spots; s++ )
		{
			var yaw = Range( 0f, 360f );
			var up = Range( 0.25f, 0.9f );
			var dirS = new Vector3( MathF.Cos( MathX.Rad( yaw ) ) * MathF.Sqrt( 1f - up * up ), MathF.Sin( MathX.Rad( yaw ) ) * MathF.Sqrt( 1f - up * up ), up ).Normalized();
			if ( dirS.X > 0.55f && dirS.Z < 0.5f ) continue;
			AddDot( bellGo, dirS, BellRadius, BellRadius * Range( 0.1f, 0.18f ), SpotColor );
		}

		foreach ( var side in new[] { -1f, 1f } )
		{
			var dirS = new Vector3( 0.82f, side * 0.52f, 0.14f ).Normalized();
			var eye = new Node3D { Name = "Eye" };
			bellGo.AddChild( eye );
			eye.Transform = new Transform3D( Shapes.LookAt( U.Sbox( dirS ) ), U.Sbox( dirS * BellRadius * 0.96f ) );
			var model = Model( side > 0f ? "eye_l" : "eye_r" );
			if ( model is not null )
			{
				model.Basis = new Basis( Vector3.Up, MathF.PI * 0.5f ) * Basis.FromScale( Vector3.One * 2.54f * BellRadius / 32f );
				eye.AddChild( model );
				foreach ( var mesh in model.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
					mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color( 0.02f, 0.015f, 0.03f ), Roughness = 0.1f, Metallic = 0.3f };
			}
			var glint = AddDot( bellGo, (dirS + new Vector3( 0.02f, -side * 0.08f, 0.14f )).Normalized(), BellRadius * 1.04f, BellRadius * 0.05f, Shapes.Glow( Hue + 0.5f, 3f ), glow: true );
			eyes.Add( new Eye { Go = eye, Glint = glint, Scale = eye.Scale, GlintScale = glint.Scale } );
			nextBlink = (float)Clock.Now + Range( 1.5f, 4f );
		}

		var frill = Shapes.Glow( hue + 0.5f, 1.5f );
		for ( var k = 0; k < 12; k++ )
		{
			var a = k * MathF.Tau / 12f;
			var outwardS = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f );
			var rim = outwardS * BellRadius * 0.86f + new Vector3( 0, 0, -BellRadius * 0.3f );
			Shapes.AddSpindle( bellGo, "Frill", rim, U.Sbox( (new Vector3( 0, 0, -1 ) + outwardS * 0.55f).Normalized() ).Normalized(), U.Sbox( outwardS ).Normalized(), BellRadius * 0.07f, BellRadius * 0.5f, 0f, 0.3f, frill );
		}
		Shapes.Part( bellGo, "Crown", new Vector3( -BellRadius * 0.15f, 0, BellRadius * 0.62f ), Shapes.Ellipsoid( 0.45f * BellRadius, 0.45f * BellRadius, 0.18f * BellRadius ), GooColor.Lerp( Colors.White, 0.35f ), glow: true );

		var count = Math.Clamp( Tentacles, 1, 7 );
		var segments = Math.Clamp( TentacleSegments, 1, 4 );
		for ( var t = 0; t < count; t++ )
		{
			var tentacle = new Tentacle { Angle = t * 360f / count + Range( -12f, 12f ), Phase = Range( 0f, 6.28f ) };
			var anchor = Anchor( tentacle );
			for ( var s = 0; s < segments; s++ )
			{
				var index = 1 + t * segments + s;
				var radius = TentacleRadius * MathX.Lerp( 1f, 0.55f, s / (float)Math.Max( 1, segments - 1 ) );
				var piece = NewPart( $"Squilien Tentacle {t}.{s}", radius, index );
				piece.Health = TentacleHealth;
				piece.Position = anchor + Vector3.Down * SegmentLength * (s + 1) * U.In;
				piece.Go.GlobalPosition = piece.Position;
				tentacle.Pieces.Add( piece );
				all.Add( piece );
			}
			tentacles.Add( tentacle );
		}
	}

	Piece NewPart( string name, float radius, int index )
	{
		var go = CreaturePart.Create( this, index, name, (index == 0 ? radius * 1.1f : radius * 1.3f) * U.In );
		GetTree().CurrentScene.AddChild( go );
		go.GlobalPosition = GlobalPosition;
		var skin = new Node3D { Name = "Skin" };
		go.AddChild( skin );
		var model = Model( index == 0 ? "bell" : "tentacle" );
		MeshInstance3D renderer;
		if ( model is not null )
		{
			model.Scale = Vector3.One / 0.64f;
			skin.AddChild( model );
			renderer = model.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>().First();
			renderer.MaterialOverride = Shapes.Material( BodyColor, "jelly" );
		}
		else renderer = Shapes.Part( skin, "Sphere", Vector3.Zero, Vector3.One, BodyColor, skin: "jelly" );
		return new Piece { Index = index, Go = go, Skin = skin, Renderer = renderer, Radius = radius };
	}

	MeshInstance3D AddDot( Node3D parent, Vector3 dirS, float distance, float size, Color color, bool glow = true )
	{
		var dot = Shapes.Part( parent, "Spot", new Vector3( dirS.X, dirS.Y, dirS.Z * 0.75f ) * distance * 0.97f, Vector3.One, color, glow: glow );
		dot.Basis = Shapes.LookAt( U.Sbox( dirS ) ) * Basis.FromScale( new Vector3( size, size, size * 0.25f ) * 2f * U.In );
		return dot;
	}

	Vector3 Anchor( Tentacle tentacle )
	{
		var local = new Angles( 0f, tentacle.Angle, 0f ).Forward * BellRadius * 0.55f * U.In + Vector3.Down * BellRadius * 0.4f * U.In;
		return GlobalPosition + GlobalBasis.Orthonormalized() * local;
	}

	// ---- Every frame ----

	void UpdateBlink()
	{
		if ( eyes.Count == 0 ) return;
		var now = (float)Clock.Now;
		if ( now >= nextBlink )
		{
			var odd = Rng.Float();
			var slow = odd < 0.2f ? Rng.Float() * 0.25f + 0.2f : 0f;
			var lag = odd is >= 0.2f and < 0.5f ? Rng.Float() * 0.25f + 0.1f : 0f;
			var lagged = Rng.Int( 0, eyes.Count - 1 );
			for ( var i = 0; i < eyes.Count; i++ )
			{
				eyes[i].Start = now + (i == lagged ? lag : 0f);
				eyes[i].Length = 0.16f + slow;
			}
			if ( !doubleBlinkPending && Rng.Float() < 0.2f ) { doubleBlinkPending = true; nextBlink = now + 0.35f + Rng.Float() * 0.2f; }
			else { doubleBlinkPending = false; nextBlink = now + (Rng.Float() < 0.15f ? 5f + Rng.Float() * 3f : 1.5f + Rng.Float() * 3.5f); }
		}
		foreach ( var e in eyes )
		{
			if ( !IsInstanceValid( e.Go ) ) continue;
			var k = e.Start < 0f ? 0f : (now - e.Start) / e.Length;
			e.Closed = k is < 0f or > 1f ? 0f : MathF.Sin( k * MathF.PI );
			e.Go.Scale = new Vector3( e.Scale.X, e.Scale.Y * MathX.Lerp( 1f, 0.06f, e.Closed ), e.Scale.Z );
			if ( IsInstanceValid( e.Glint ) ) e.Glint.Scale = e.GlintScale * MathF.Max( 0.001f, 1f - e.Closed );
		}
	}

	public override void _Process( double delta )
	{
		if ( health is null || bell is null ) return;
		var dt = (float)delta;
		if ( IsDead )
		{
			if ( timeSinceDeath > 1f ) QueueFree();
			return;
		}
		if ( !DevFlags.FreezeEnemies ) Think( dt );
		if ( diedLocally ) return;
		UpdateBlink();
		if ( !bell.Gone ) CreatureGore.BleedTrail( bell.Position + Vector3.Down * BellRadius * 0.4f * U.In, GooColor, health.Fraction, ref bleedTimer );
		if ( PulseCount != seenPulses ) { seenPulses = PulseCount; squeeze = 1f; }
		squeeze = MathX.Approach( squeeze, 0f, dt * 2.2f );
		PoseBody( dt );
	}

	void PoseBody( float frame )
	{
		var dt = MathF.Max( frame, 0.0001f );
		var s = squeeze * squeeze;
		bell.Position = GlobalPosition;
		bell.Go.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized(), GlobalPosition );
		var r = BellRadius * (1f - 0.12f * bell.Hurt);
		bell.Skin.Scale = Shapes.Ellipsoid( (1f - 0.2f * s) * r, (1f - 0.2f * s) * r, (0.75f + 0.15f * s) * r );
		Tint( bell );

		var time = (float)Clock.Now;
		var frameDt = MathF.Min( dt, 1f / 20f );
		const int substeps = 3;
		var h = frameDt / substeps;
		var bellRot = GlobalBasis.Orthonormalized();
		foreach ( var tentacle in tentacles )
		{
			var outward = (bellRot * new Angles( 0f, tentacle.Angle, 0f ).Forward).Flat().SafeNormal();
			var tangent = Vector3.Up.Cross( outward ).SafeNormal();
			for ( var step = 0; step < substeps; step++ )
			{
				var parent = Anchor( tentacle ) / U.In;   // work in inches like s&box
				for ( var i = 0; i < tentacle.Pieces.Count; i++ )
				{
					var piece = tentacle.Pieces[i];
					if ( piece.Gone ) break;
					var along = i / (float)Math.Max( 1, tentacle.Pieces.Count - 1 );
					var pos = piece.Position / U.In;
					var force = Vector3.Down * 140f;
					force += (parent + Vector3.Down * SegmentLength - pos) * MathX.Lerp( 70f, 25f, along );
					var wave = MathF.Sin( time * 2.4f - i * 1.1f + tentacle.Phase );
					force += tangent * wave * (60f + 110f * along);
					force += outward * MathF.Cos( time * 1.7f - i * 0.9f + tentacle.Phase * 1.3f ) * (30f + 40f * along);
					force += (outward * 500f + Vector3.Up * 380f) * squeeze * (0.4f + along);
					var vel = piece.Velocity + force * h;
					vel *= MathF.Exp( -2.2f * h );
					var next = pos + vel * h;
					var offset = next - parent;
					if ( offset.LengthSquared() < 0.01f ) offset = Vector3.Down;
					next = parent + offset.Normalized() * SegmentLength;
					piece.Velocity = (next - pos) / h;
					piece.Position = next * U.In;
					parent = next;
				}
			}
			var previous = Anchor( tentacle );
			foreach ( var piece in tentacle.Pieces )
			{
				if ( piece.Gone ) break;
				var position = piece.Position;
				piece.Go.GlobalTransform = new Transform3D( Shapes.LookAt( position - previous ), (previous + position) * 0.5f );
				var pr = piece.Radius * (1f - 0.25f * piece.Hurt);
				piece.Skin.Scale = Shapes.Ellipsoid( SegmentLength * 0.62f, pr, pr );
				Tint( piece );
				previous = position;
			}
		}
	}

	void Tint( Piece piece )
	{
		var baseColor = BodyColor.Lerp( BodyColor * 0.45f, piece.Hurt ) with { A = 1f };
		var flash = MathX.Clamp( 1f - piece.Flash / 0.1f, 0f, 1f );
		Shapes.Tint( piece.Renderer, baseColor.Lerp( Colors.White * 3f, flash ) );
	}

	// ---- Brain ----

	int TentaclesLeft => tentacles.Count( t => !t.Pieces[0].Gone );

	void Think( float dt )
	{
		var target = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var strength = MathX.Lerp( 0.45f, 1f, TentaclesLeft / (float)Math.Max( 1, tentacles.Count ) );
		if ( nextPulse <= 0f )
		{
			nextPulse = PulseInterval * Rng.Float( 0.85f, 1.15f );
			PulseCount++;
			var goal = GlobalPosition + Vector3.Up * 30f * U.In;
			if ( target is not null )
			{
				var flat = (GlobalPosition - target.WorldPosition).Flat();
				if ( flat.IsNearlyZero() ) flat = Vector3.Forward;
				var around = flat.Normalized().Rotated( Vector3.Up, MathX.Rad( orbitSign * OrbitSpeed * PulseInterval * 2f ) );
				goal = target.WorldPosition + (around * PreferredDistance + Vector3.Up * HoverHeight) * U.In;
			}
			velocity += (goal - GlobalPosition).SafeNormal() * PulseThrust * strength;
		}
		velocity *= MathF.Exp( -Drag * dt );
		velocity += Vector3.Down * 12f * dt;
		Move( dt );
		if ( target is not null )
		{
			var face = (target.WorldPosition - GlobalPosition).Flat();
			var tilt = MathX.Clamp( velocity.Length() / 300f, 0f, 1f ) * 15f;
			if ( !face.IsNearlyZero() )
			{
				var wanted = (Shapes.LookAt( face ) * new Angles( tilt, 0f, 0f ).ToBasis()).GetRotationQuaternion();
				GlobalBasis = new Basis( GlobalBasis.Orthonormalized().GetRotationQuaternion().SafeSlerp( wanted, MathX.Clamp( dt * 3f, 0f, 1f ) ) );
			}
			if ( nextThrow <= 0f ) TryThrow( target );
		}
	}

	void Move( float dt )
	{
		var position = GlobalPosition;
		var stepV = velocity * U.In * dt;
		if ( !stepV.IsNearlyZero() )
		{
			var tr = Trace.Sphere( this, BellRadius * 0.8f * U.In, position, position + stepV + stepV.Normalized() * 20f * U.In, Layers.World );
			if ( tr.Hit && !tr.StartedSolid )
			{
				velocity -= tr.Normal * velocity.Dot( tr.Normal ) * 1.6f;
				stepV = velocity * U.In * dt;
			}
		}
		var ground = Trace.Ray( this, position, position + Vector3.Down * 500f * U.In, Layers.World );
		if ( ground.Hit && (position.Y - ground.HitPosition.Y) / U.In < MinAltitude )
			velocity = velocity.WithY( MathF.Max( velocity.Y, 80f ) );
		foreach ( var other in All )
		{
			if ( other == this || other.IsDead ) continue;
			var away = (position - other.GlobalPosition) / U.In;
			var distance = away.Length();
			if ( distance < 90f && distance > 0.01f ) velocity += away / distance * (90f - distance) * 4f * dt;
		}
		GlobalPosition = position + stepV;
	}

	void TryThrow( PlayerSurvivor target )
	{
		var basis = GlobalBasis.Orthonormalized();
		var origin = GlobalPosition + (Vector3.Down * BellRadius * 0.3f + (-basis.Z) * BellRadius * 0.8f) * U.In;
		var aim = target.WorldPosition + Vector3.Up * 45f * U.In;
		var distance = (aim - origin).Length() / U.In;
		if ( distance > ThrowRange ) { nextThrow = 0.5f; return; }
		var sight = Trace.Ray( this, origin, aim, Layers.World );
		if ( sight.Hit ) { nextThrow = 0.4f; return; }
		var flight = distance / InkSpeed;
		var lead = target.Controller.Velocity.Flat() / U.In * flight * 0.6f;
		var launch = ((aim - origin) / U.In + lead).SafeNormal() * InkSpeed + Vector3.Up * 0.5f * InkGravity * flight;
		nextThrow = ThrowCooldown * Rng.Float( 0.8f, 1.2f );
		PulseCount++;
		InkBall.Spawn( origin, launch, InkGravity, InkColor, InkDamage, this );
		gore.GooBurst( origin, launch.SafeNormal(), InkColor, 4, 120f, 0.8f );
	}

	// ---- Getting shot ----

	public void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead ) return;
		var piece = all.FirstOrDefault( p => p.Index == index );
		if ( piece is null || piece.Gone ) piece = bell;
		lastShotDirection = direction;
		lastShotPointBlank = (position - origin).Length() < CreatureGore.OverkillRange * U.In;
		var isBell = piece == bell;
		var cut = false;
		float hurt;
		if ( isBell ) hurt = 1f - MathX.Clamp( (health.Current - damage * BellMultiplier) / MathF.Max( 1f, health.MaxHealth ), 0f, 1f );
		else
		{
			piece.Health -= damage;
			cut = piece.Health <= 0f;
			hurt = 1f - MathX.Clamp( piece.Health / MathF.Max( 1f, TentacleHealth ), 0f, 1f );
		}
		if ( cut ) CutLocal( piece.Index, direction, quiet: false );
		else Hit( piece, position, direction, Rng.Float() < ChipChance, hurt );
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * (isBell ? BellMultiplier : TentacleMultiplier), Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, isBell, scoreHit );
	}

	IEnumerable<(Tentacle Tentacle, int From)> Below( int index )
	{
		foreach ( var tentacle in tentacles )
		{
			var at = tentacle.Pieces.FindIndex( p => p.Index == index );
			if ( at >= 0 ) yield return (tentacle, at);
		}
	}

	void Hit( Piece piece, Vector3 position, Vector3 direction, bool chip, float hurt )
	{
		piece.Flash = 0f;
		piece.Hurt = MathF.Max( piece.Hurt, hurt );
		if ( !piece.Gone )
		{
			CreatureGore.AddWound( piece.Go, position, direction, BodyColor, GooColor, piece == bell ? BellRadius * 0.3f : piece.Radius * 1.1f );
			CreatureGore.Jiggle( piece.Skin, piece == bell ? 1.2f : 0.8f );
		}
		gore.GooBurst( position, -direction, GooColor, 6, 220f, 0.8f );
		gore.GooSpray( position, direction, 2, GooColor, 180f, 0.4f, 0.9f );
		if ( chip && !piece.Gone ) gore.ThrowBlob( position, -direction, BodyColor, GooColor, MathF.Max( 2.5f, piece.Radius * (piece == bell ? 0.25f : 0.8f) ) );
	}

	void CutLocal( int index, Vector3 direction, bool quiet )
	{
		foreach ( var (tentacle, from) in Below( index ).ToList() )
		{
			if ( !quiet && !tentacle.Pieces[from].Gone )
			{
				var cut = tentacle.Pieces[from];
				var above = from > 0 ? tentacle.Pieces[from - 1] : null;
				var joint = above is not null ? above.Position : Anchor( tentacle );
				var outward = (cut.Position - joint).SafeNormal();
				if ( above is not null && !above.Gone ) CreatureGore.AddStump( above.Go, joint, outward, above.Radius * 1.2f, BodyColor, GooColor, 4f );
				else if ( above is null && !bell.Gone ) CreatureGore.AddStump( bell.Go, joint, outward, TentacleRadius * 1.3f, BodyColor, GooColor, 4f );
			}
			for ( var i = from; i < tentacle.Pieces.Count; i++ )
			{
				var piece = tentacle.Pieces[i];
				if ( piece.Gone ) continue;
				piece.Gone = true;
				if ( quiet ) { piece.Go.QueueFree(); continue; }
				if ( i == from ) Burst( piece, direction, 8, 0 );
				else Detach( piece, direction * Rng.Float( 30f, 90f ) );
			}
		}
	}

	void Burst( Piece piece, Vector3 direction, int drops, int blobs )
	{
		gore.GooBurst( piece.Position, direction, GooColor, drops, 280f, 1.1f );
		gore.GooSpray( piece.Position, direction, 3, GooColor, 220f, 0.5f, 1.2f );
		for ( var b = 0; b < blobs; b++ ) gore.ThrowBlob( piece.Position, direction, BodyColor, GooColor, piece.Radius * Rng.Float( 0.3f, 0.45f ) );
		piece.Go.QueueFree();
	}

	void Detach( Piece piece, Vector3 push )
	{
		Shapes.Tint( piece.Renderer, BodyColor.Lerp( BodyColor * 0.45f, piece.Hurt ) with { A = 1f } );
		var holder = new Node3D { Name = "Squilien Piece" };
		GetTree().CurrentScene.AddChild( holder );
		holder.GlobalTransform = piece.Go.GlobalTransform;
		foreach ( var c in piece.Go.GetChildren().OfType<Node3D>().ToList() ) if ( c is not CollisionShape3D ) c.Reparent( holder, true );
		piece.Go.QueueFree();
		Gib.Attach( holder, gore, MathF.Max( 2f, piece.Radius ), 12f, 1.5f, (piece.Velocity * 0.4f + push) * U.In, GooColor );
		CreatureGore.Twitch( holder, Rng.Float( 2f, 4f ), 0.7f );
	}

	// ---- Death ----

	void OnDied()
	{
		timeSinceDeath = 0f;
		Killed?.Invoke( this );
		DieLocal( lastShotDirection, lastShotPointBlank );
	}

	void DieLocal( Vector3 direction, bool overkill )
	{
		if ( diedLocally ) return;
		diedLocally = true;
		timeSinceDeath = 0f;
		if ( overkill ) CreatureGore.Overkill( bell.Position, direction, BodyColor, GooColor, 1.2f );
		foreach ( var piece in all )
		{
			if ( piece.Gone ) continue;
			piece.Gone = true;
			if ( overkill && piece != bell && Rng.Float() < 0.5f ) { Burst( piece, direction, 4, 0 ); continue; }
			if ( piece == bell )
			{
				gore.GooBurst( piece.Position, direction, InkColor, 14, 260f, 1.6f );
				gore.GooSpray( piece.Position, Vector3.Down, 5, InkColor, 260f, 0.8f, 1.6f );
				Burst( piece, direction, 18, 3 );
			}
			else Detach( piece, (direction + Rng.OnSphere()).SafeNormal() * Rng.Float( 40f, 140f ) );
		}
	}
}

/// <summary> Port of s&box InkBall: a wobbling ball of ink lobbed in an arc; splats on what it hits and inks the player. </summary>
public partial class InkBall : MeshInstance3D
{
	Vector3 velocity;   // s&box units/s
	float gravity;
	Color color;
	float damage;
	Node attacker;
	TimeSince age;
	TimeSince lastDrip;
	const float Radius = 6f;

	public static void Spawn( Vector3 position, Vector3 velocity, float gravity, Color color, float damage, Node attacker )
	{
		Sfx.Play( "ink_throw", position, 0.7f, Rng.Float( 0.9f, 1.1f ) );
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		var ball = new InkBall { Mesh = Shapes.Sphere, MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.2f }, velocity = velocity, gravity = gravity, color = color, damage = damage, attacker = attacker };
		root?.AddChild( ball );
		ball.GlobalPosition = position;
		ball.Scale = Vector3.One * Radius * 2f * U.In;
		ball.age = 0f;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( DevFlags.FreezeEnemies ) { age = age - dt; return; }
		if ( age > 5f ) { QueueFree(); return; }
		velocity += Vector3.Down * gravity * dt;
		var from = GlobalPosition;
		var to = from + velocity * U.In * dt;
		var tr = Trace.Sphere( this, Radius * U.In, from, to, global::Layers.World | global::Layers.Player );
		if ( tr.Hit ) { Splash( tr ); return; }
		GlobalPosition = to;
		var wobble = 1f + 0.15f * MathF.Sin( age * 30f );
		Scale = new Vector3( 2f - wobble, 1f, wobble ) * Radius * 2f * U.In;
		if ( lastDrip > 0.06f )
		{
			lastDrip = 0f;
			GooDroplet.Spawn( from, (velocity * 0.1f + Rng.InSphere( 30f )) * U.In, color, Rng.Float( 1f, 2f ) );
		}
	}

	void Splash( TraceResult tr )
	{
		var gore = GoreSystem.Get();
		var player = tr.Node as PlayerController;
		gore.GooBurst( tr.HitPosition, tr.Normal + velocity.SafeNormal() * -0.3f, color, 10, 200f, 1.2f );
		if ( player is not null )
		{
			var survivor = player.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
			if ( survivor is { IsAlive: true } )
			{
				survivor.Health.OnDamage( new DamageInfo { Damage = damage, Attacker = attacker, Position = tr.HitPosition, Origin = tr.HitPosition - velocity.SafeNormal() * 50f * U.In } );
				survivor.Afflictions?.Inked();
			}
		}
		else
		{
			gore.GooSplat( tr.HitPosition, tr.Normal, Rng.Float( 1.2f, 1.8f ), color );
			gore.GooSpray( tr.HitPosition, -tr.Normal, 2, color, 120f, 0.4f, 0.8f );
		}
		QueueFree();
	}
}
