using Godot;

/// <summary>
/// The Kraken event boss (issue #30). It burrows up out of the sea bed in a cloud of sand and stays where it came
/// up, turning to face the nearest player. Its brain sits on top of its head under a spacesuit glass dome: the dome
/// has its own health and shatters, and after that the brain is the weak point. Each tentacle can be shot off.
/// Stage 1: the body, the arrival and dying. The attacks, the whirlpool and the wave odds come in later stages.
/// Model: Tools/blender/kraken.py (assets/models/creatures/kraken, metres, mouth facing forward).
/// </summary>
public partial class Kraken : PartCreature
{
	const int Head = 0, Helmet = 1, Brain = 2, Tentacle0 = 3;
	public const int Tentacles = 8;
	// Shared with kraken.py.
	const int Segments = 8;
	const float SegLength = 0.85f, RootRadius = 0.34f, TipRadius = 0.07f;
	static readonly Vector3 Collar = new( 0f, 2.95f, 0.25f );

	public static readonly List<Kraken> All = new();

	public float HelmetHealth = 600f;
	public float TentacleHealth = 350f;
	/// <summary> Damage to the whole Kraken per point of damage on: the brain, the head, a tentacle. </summary>
	public float BrainMultiplier = 3f, HeadMultiplier = 1f, TentacleMultiplier = 0.5f;
	/// <summary> Seconds to come up out of the ground, and how deep it starts (metres). </summary>
	public float RiseTime = 3.4f, Depth = 4.6f;
	/// <summary> How fast it turns to face the player (per second). </summary>
	public float TurnRate = 0.9f;
	/// <summary> Seconds its body takes to slide back under the sand once dead. </summary>
	public float SinkTime = 2.6f;

	public enum Mode { Burrowing, Idle }
	public Mode State { get; private set; } = Mode.Burrowing;
	/// <summary> 0 to 1: how much of the dome's health is left (0 once shattered). </summary>
	public float HelmetFraction => Has( Helmet ) ? MathX.Clamp( parts[Helmet].Health / MathF.Max( 1f, parts[Helmet].MaxHealth ), 0f, 1f ) : 0f;
	public bool HelmetOn => parts is not null && Has( Helmet );
	public int TentaclesLeft => parts is null ? 0 : Enumerable.Range( Tentacle0, Tentacles ).Count( Has );

	/// <summary> Where its brain is in the world (the middle of the dome). </summary>
	public Vector3 BrainPosition => rig.GlobalTransform * (Collar + Vector3.Up * 0.3f);
	/// <summary> The base of a tentacle in the world. </summary>
	public Vector3 TentaclePosition( int k ) => segments[k, 2].GlobalPosition;
	/// <summary> Dev menu: the dome goes at once. </summary>
	public void BreakHelmet() => ShatterHelmet( -GlobalBasis.Z );

	static readonly Color Skin = new( 0.24f, 0.46f, 0.42f );
	static readonly Color Belly = new( 0.5f, 0.56f, 0.46f );
	static readonly Color Dark = new( 0.07f, 0.11f, 0.1f );
	static readonly Color Lip = new( 0.12f, 0.24f, 0.22f );
	static readonly Color Green = new( 0.3f, 1f, 0.35f );
	static readonly Color BrainPink = new( 0.95f, 0.55f, 0.62f );
	static readonly Color Fold = new( 0.7f, 0.32f, 0.42f );
	static readonly Color Sand = new( 0.76f, 0.68f, 0.5f );
	static Color Goo => Shapes.Hsv( Rng.Float( 0.27f, 0.36f ), 0.9f, 1f );

	// Each segment's bend (degrees, up is positive) lying out on the sea bed, and wrapped up round the head.
	static readonly float[] RestBend = { 30, -20, -20, -10, 0, 10, 25, 35 };
	static readonly float[] CurlBend = { 75, 8, 8, 5, 0, -10, -25, -40 };

	readonly Node3D[,] segments = new Node3D[Tentacles, Segments];
	readonly float[] phase = new float[Tentacles];
	readonly List<MeshInstance3D> glowing = new();
	Node3D mound, brainModel;
	MeshInstance3D glass;
	StandardMaterial3D glassMaterial;
	TimeSince sinceSpawned, sinceDust, sinceGlassHit = 99f;
	float time;
	bool brokeGround;

	public static Kraken Spawn( Node parent, Vector3 ground, float maxHealth )
	{
		var kraken = new Kraken { Name = "Kraken" };
		Sfx.Play( "kraken_rise", ground, 1f );
		return Place( kraken, parent, ground, maxHealth );
	}

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }
	protected override string Kind => "Kraken";
	protected override Color GibGoo => Goo;

	protected override void Setup()
	{
		BeginSetup( 3500f );
		sinceSpawned = 0f;
		sinceDust = 99f;
		Build();
		PlaceRig();
	}

	// ---- Building ----

	static readonly Dictionary<string, PackedScene> models = new();

	static Node3D Model( string name )
	{
		if ( !models.TryGetValue( name, out var scene ) )
			models[name] = scene = GD.Load<PackedScene>( $"res://assets/models/creatures/kraken/{name}.glb" );
		return scene.Instantiate<Node3D>();
	}

	/// <summary> The material for one of the model's meshes, by its name. </summary>
	Material Paint( string surface, out Color tint )
	{
		tint = surface switch
		{
			"skin" => Skin,
			"belly" => Belly,
			"tentacle" => Dark,
			"lip" => Lip,
			"mouth" => new Color( 0.03f, 0.02f, 0.04f ),
			"eye" => new Color( 0.01f, 0.01f, 0.015f ),
			"tooth" => new Color( 0.93f, 0.9f, 0.78f ),
			"collar" => new Color( 0.72f, 0.74f, 0.78f ),
			"brain" => BrainPink,
			"fold" => Fold,
			_ => Green,
		};
		switch ( surface )
		{
			case "glow" or "sucker": return Shapes.Material( tint, glow: true );
			case "eye": return new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.08f, ClearcoatEnabled = true, Clearcoat = 1f, ClearcoatRoughness = 0.05f };
			case "collar": return new StandardMaterial3D { AlbedoColor = tint, Metallic = 0.85f, Roughness = 0.35f };
			case "mouth": return new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.9f };
			default: return Shapes.Material( tint );
		}
	}

	/// <summary> Puts a model under a node and paints it. Its meshes join the part's flash and hurt tint. </summary>
	Node3D Sculpt( Part part, Node3D under, string model )
	{
		var node = Model( model );
		under.AddChild( node );
		foreach ( var mesh in node.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			var surface = mesh.Name.ToString();
			mesh.MaterialOverride = Paint( surface, out var tint );
			part.Renderers.Add( (mesh, tint) );
			if ( surface is "glow" or "sucker" ) glowing.Add( mesh );
		}
		return node;
	}

	/// <summary> A hit capsule between two points (metres, in the node's own space) that reports shots as a part. </summary>
	void HitShape( Node3D under, int index, Vector3 a, Vector3 b, float radius )
	{
		var body = under as CreaturePart;
		if ( body is null )
		{
			body = new CreaturePart { Name = $"{Kind} Hit", Creature = this, Index = index, CollisionLayer = PartLayer, CollisionMask = 0 };
			under.AddChild( body );
		}
		var axis = b - a;
		var length = axis.Length();
		var shape = new CapsuleShape3D { Radius = radius, Height = length + radius * 2f };
		var basis = length > 1e-5f ? new Basis( new Quaternion( Vector3.Up, axis / length ) ) : Basis.Identity;
		body.AddChild( new CollisionShape3D { Shape = shape, Transform = new Transform3D( basis, (a + b) * 0.5f ) } );
	}

	void Build()
	{
		parts = new Part[Tentacle0 + Tentacles];
		rig = new Node3D { Name = "Kraken Rig" };
		GetTree().CurrentScene.AddChild( rig );

		var head = AddPart( Head, -1, rig, "Head", Vector3.Zero, Basis.Identity, 0f );
		Sculpt( head, head.Go, "head" );
		HitShape( head.Go, Head, new Vector3( 0f, 0.7f, -0.1f ), new Vector3( 0f, 1.9f, 0.2f ), 0.85f );
		HitShape( head.Go, Head, new Vector3( 0f, 2.1f, 0.25f ), new Vector3( 0f, 2.15f, 0.25f ), 0.98f );

		var brain = AddPart( Brain, Head, head.Go, "Brain", Vector3.Zero, Basis.Identity, 0f );
		brain.Go.Position = Collar;
		brainModel = Sculpt( brain, brain.Go, "brain" );
		HitShape( brain.Go, Brain, new Vector3( 0f, 0.18f, 0f ), new Vector3( 0f, 0.22f, 0f ), 0.5f );

		var helmet = AddPart( Helmet, Head, head.Go, "Helmet", Vector3.Zero, Basis.Identity, HelmetHealth );
		helmet.Go.Position = Collar;
		var dome = Model( "helmet" );
		helmet.Go.AddChild( dome );
		glass = dome.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>().First();
		glassMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.75f, 0.92f, 1f, 0.2f ),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			Roughness = 0.05f,
			Metallic = 0.2f,
			RimEnabled = true,
			Rim = 1f,
			RimTint = 0.2f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			EmissionEnabled = true,
			Emission = new Color( 0.5f, 0.8f, 1f ),
			EmissionEnergyMultiplier = 0.12f,
		};
		glass.MaterialOverride = glassMaterial;
		glass.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		HitShape( helmet.Go, Helmet, new Vector3( 0f, 0.2f, 0f ), new Vector3( 0f, 0.3f, 0f ), 0.8f );

		for ( var k = 0; k < Tentacles; k++ )
		{
			// Evenly round the base, two of them either side of the mouth.
			var angle = (k + 0.5f) / Tentacles * MathF.Tau;
			var outward = new Vector3( MathF.Sin( angle ), 0f, -MathF.Cos( angle ) );
			var tentacle = AddPart( Tentacle0 + k, Head, head.Go, $"Tentacle {k}", Vector3.Zero, Shapes.LookAt( outward ), TentacleHealth );
			tentacle.Go.Position = outward * 0.8f + Vector3.Up * 0.5f;
			phase[k] = Rng.Float( 0f, MathF.Tau );
			Node3D parent = tentacle.Go;
			for ( var j = 0; j < Segments; j++ )
			{
				var segment = new Node3D { Name = $"Segment {j}", Position = j == 0 ? Vector3.Zero : new Vector3( 0f, 0f, -SegLength ) };
				parent.AddChild( segment );
				Sculpt( tentacle, segment, $"seg{j}" );
				var radius = RootRadius * MathF.Pow( TipRadius / RootRadius, (j + 0.5f) / Segments );
				var hit = new Node3D { Name = "Hit" };
				segment.AddChild( hit );
				HitShape( hit, Tentacle0 + k, new Vector3( 0f, 0f, -radius ), new Vector3( 0f, 0f, -SegLength + radius ), radius * 1.15f );
				segments[k, j] = segment;
				parent = segment;
			}
		}

		// The sand it pushed up: a ring of lumps round where it came through.
		mound = new Node3D { Name = "Kraken Mound" };
		AddChild( mound );
		for ( var i = 0; i < 14; i++ )
		{
			var a = i / 14f * MathF.Tau + Rng.Float( -0.15f, 0.15f );
			var r = Rng.Float( 1.5f, 2.3f );
			var lump = new MeshInstance3D
			{
				Mesh = Shapes.Sphere,
				MaterialOverride = new StandardMaterial3D { AlbedoColor = (Sand * Rng.Float( 0.85f, 1.05f )) with { A = 1f }, Roughness = 1f },
				Position = new Vector3( MathF.Cos( a ) * r, -0.05f, MathF.Sin( a ) * r ),
				Scale = new Vector3( Rng.Float( 0.9f, 1.5f ), Rng.Float( 0.35f, 0.6f ), Rng.Float( 0.9f, 1.5f ) ),
				RotationDegrees = new Vector3( 0f, Rng.Float( 0f, 360f ), 0f ),
			};
			mound.AddChild( lump );
		}
		mound.Scale = Vector3.One * 0.01f;
	}

	// ---- Every frame ----

	/// <summary> 0 to 1: how far out of the ground it is. </summary>
	float Risen => RiseTime <= 0f ? 1f : MathX.Clamp( (float)sinceSpawned / RiseTime, 0f, 1f );
	/// <summary> 0 to 1: how far the tentacles have unrolled (they start once it is most of the way up). </summary>
	float Unfurled => MathX.Clamp( ((float)sinceSpawned - RiseTime * 0.7f) / 1.6f, 0f, 1f );

	static float Ease( float t ) => t * t * (3f - 2f * t);

	void PlaceRig()
	{
		var up = Ease( Risen );
		// A shudder on the way up, as if forcing through.
		var shudder = State == Mode.Burrowing ? MathF.Sin( time * 31f ) * 0.03f * (1f - up) : 0f;
		rig.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized(), GlobalPosition + Vector3.Down * Depth * (1f - up) + new Vector3( shudder, 0f, -shudder ) );
	}

	public override void _Process( double delta )
	{
		if ( health is null || rig is null ) return;
		var dt = (float)delta;
		if ( IsDead )
		{
			Sink();
			return;
		}
		if ( !IsInstanceValid( rig ) ) return;
		time += dt;
		if ( State == Mode.Burrowing ) Burrow();
		else if ( !DevFlags.FreezeEnemies && PlayerSurvivor.FindNearestAlive( GlobalPosition ) is { } target )
			Face( target.WorldPosition - GlobalPosition, TurnRate, dt );
		PlaceRig();
		Animate( dt );
		TintParts( dt );
		CreatureGore.BleedTrail( parts[Head].Go.GlobalPosition + Vector3.Up * 1.4f, Goo, health.Fraction, ref bleedTimer );
	}

	TimeSince bleedTimer;

	/// <summary> Dead: what's left of it shudders and slides back down the hole, then it is cleared away. </summary>
	void Sink()
	{
		if ( !IsInstanceValid( rig ) ) { QueueFree(); return; }
		var t = MathX.Clamp( ((float)timeSinceDeath - 0.5f) / SinkTime, 0f, 1f );
		time += (float)GetProcessDeltaTime();
		var shudder = MathF.Sin( time * 27f ) * 0.04f * (1f - t);
		var lean = new Basis( Vector3.Right, MathX.Rad( -14f * Ease( MathX.Clamp( (float)timeSinceDeath / 0.6f, 0f, 1f ) ) ) );
		rig.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized() * lean, GlobalPosition + Vector3.Down * (Depth + 0.5f) * t * t + new Vector3( shudder, 0f, shudder ) );
		mound.Scale = Vector3.One * MathF.Max( 0.01f, 1f - Ease( MathX.Clamp( t * 1.4f - 0.4f, 0f, 1f ) ) );
		if ( t > 0f && sinceDust > 0.3f )
		{
			sinceDust = 0f;
			SandBurst( GlobalPosition, 1.8f, 20, 2.4f );
			gore.GooBurst( GlobalPosition + Vector3.Up * 0.3f, Vector3.Up, Goo, 4, 200f, 1f );
		}
		if ( t >= 1f ) { rig.QueueFree(); QueueFree(); }
	}

	void Burrow()
	{
		var risen = Risen;
		mound.Scale = Vector3.One * MathF.Max( 0.01f, Ease( MathX.Clamp( risen * 2.2f, 0f, 1f ) ) );
		if ( !brokeGround )
		{
			brokeGround = true;
			SandBurst( GlobalPosition, 1.4f, 60, 5f );
			ShakeNear( 0.55f );
		}
		if ( sinceDust > 0.22f )
		{
			sinceDust = 0f;
			SandBurst( GlobalPosition, 2.2f, 26, 3.2f );
			ShakeNear( 0.22f );
		}
		if ( sinceSpawned < RiseTime + 1.7f ) return;
		State = Mode.Idle;
		// The tentacles slap down on the sand.
		SandBurst( GlobalPosition, 5.5f, 70, 2.4f );
		ShakeNear( 0.45f );
		Sfx.Play( "land", GlobalPosition, 1f, 0.6f );
	}

	void Animate( float dt )
	{
		var unfurl = Ease( Unfurled );
		for ( var k = 0; k < Tentacles; k++ )
		{
			if ( !Has( Tentacle0 + k ) ) continue;
			for ( var j = 0; j < Segments; j++ )
			{
				var bend = MathX.Lerp( CurlBend[j], RestBend[j], unfurl ) + MathF.Sin( time * 0.9f + j * 0.7f + phase[k] ) * (2.5f + j * 1.1f);
				var sway = MathF.Sin( time * 0.6f + j * 0.5f + phase[k] * 1.7f ) * (2f + j) * unfurl;
				segments[k, j].Basis = new Basis( Vector3.Up, MathX.Rad( sway ) ) * new Basis( Vector3.Right, MathX.Rad( bend ) );
			}
		}
		// The brain throbs, faster once the dome is gone; the holes and suckers pulse.
		if ( Has( Brain ) )
		{
			var rate = HelmetOn ? 2.4f : 5f;
			brainModel.Scale = Vector3.One * (1f + MathF.Sin( time * rate ) * 0.035f) + Vector3.Up * MathF.Sin( time * rate + 1f ) * 0.03f;
		}
		var pulse = 1.3f + MathF.Sin( time * 1.7f ) * 0.5f;
		foreach ( var mesh in glowing )
			if ( IsInstanceValid( mesh ) && mesh.MaterialOverride is StandardMaterial3D m ) m.EmissionEnergyMultiplier = pulse;
		// The dome clouds over as it cracks, and flashes when hit.
		if ( HelmetOn )
		{
			var cracked = 1f - HelmetFraction;
			var flash = MathX.Clamp( 1f - sinceGlassHit / 0.12f, 0f, 1f );
			glassMaterial.AlbedoColor = new Color( 0.75f, 0.92f, 1f, 0.2f + cracked * 0.4f + flash * 0.4f );
			glassMaterial.EmissionEnergyMultiplier = 0.12f + flash * 1.5f;
		}
	}

	void ShakeNear( float amount )
	{
		foreach ( var player in PlayerSurvivor.All )
		{
			var distance = player.WorldPosition.DistanceTo( GlobalPosition );
			if ( distance > 40f ) continue;
			player.Controller?.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( amount * (1f - distance / 40f) );
		}
	}

	static StandardMaterial3D sandMaterial;

	/// <summary> A puff of sand thrown up from a ring on the ground (radius and speed in metres). </summary>
	void SandBurst( Vector3 at, float radius, int amount, float speed )
	{
		sandMaterial ??= new StandardMaterial3D
		{
			AlbedoColor = Sand with { A = 0.32f },
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			VertexColorUseAsAlbedo = true,
		};
		var fade = new Gradient();
		fade.SetColor( 0, Colors.White );
		fade.SetColor( 1, new Color( 1f, 1f, 1f, 0f ) );
		var grow = new Curve();
		grow.AddPoint( new Vector2( 0f, 0.35f ) );
		grow.AddPoint( new Vector2( 1f, 1f ) );
		var puff = new CpuParticles3D
		{
			Name = "Kraken Sand",
			Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 8, Material = sandMaterial },
			Amount = amount,
			Lifetime = 2.2f,
			OneShot = true,
			Explosiveness = 0.85f,
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Ring,
			EmissionRingAxis = Vector3.Up,
			EmissionRingRadius = radius,
			EmissionRingInnerRadius = radius * 0.5f,
			EmissionRingHeight = 0.2f,
			Direction = Vector3.Up,
			Spread = 55f,
			InitialVelocityMin = speed * 0.4f,
			InitialVelocityMax = speed,
			Gravity = new Vector3( 0f, -1.6f, 0f ),
			DampingMin = 1.2f,
			DampingMax = 1.2f,
			ScaleAmountMin = 0.5f,
			ScaleAmountMax = 1.5f,
			ScaleAmountCurve = grow,
			ColorRamp = fade,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		GetTree().CurrentScene.AddChild( puff );
		puff.GlobalPosition = at + Vector3.Up * 0.15f;
		puff.Emitting = true;
		Fx.Expire( puff, 3.5f );
	}

	// ---- Getting shot ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead ) return;
		if ( index < 0 || index >= parts.Length || !Has( index ) ) index = Head;
		NoteShot( direction, position, origin );
		var part = parts[index];

		if ( index == Helmet )
		{
			// The dome soaks the shot: nothing reaches the Kraken until it breaks.
			part.Health -= damage;
			sinceGlassHit = 0f;
			Fx.ImpactWorld( position, -direction );
			Sfx.Play( "glass_hit", position, 0.8f, Rng.Float( 0.9f, 1.15f ) );
			if ( part.Health <= 0f ) ShatterHelmet( direction );
			shooter?.RewardHit( false, false, scoreHit );
			return;
		}

		var multiplier = index == Brain ? BrainMultiplier : index == Head ? HeadMultiplier : TentacleMultiplier;
		var cut = false;
		if ( index >= Tentacle0 )
		{
			part.Health -= damage;
			cut = part.Health <= 0f;
		}
		var hurt = index >= Tentacle0
			? 1f - MathX.Clamp( part.Health / MathF.Max( 1f, part.MaxHealth ), 0f, 1f )
			: 1f - health.Fraction;
		if ( cut ) CutTentacle( index, direction );
		else Hit( index, position, direction, hurt );

		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * multiplier, Position = position, Origin = origin, Attacker = shooter } );
		if ( wasAlive && shooter is not null ) shooter.RewardHit( IsDead, index == Brain, scoreHit );
	}

	void Hit( int index, Vector3 position, Vector3 direction, float hurt )
	{
		var part = parts[index];
		part.Flash = 0f;
		part.Hurt = MathF.Max( part.Hurt, hurt * 0.7f );
		var goo = index == Brain ? BrainPink : Goo;
		if ( part.Renderers.Count > 0 ) CreatureGore.AddWound( part.Go, position, direction, part.Renderers[0].Tint, goo, index == Head ? 5f : 3f );
		gore.GooBurst( position, -direction, goo, index == Brain ? 10 : 7, 240f, index == Brain ? 1.3f : 1f );
		gore.GooSpray( position, direction, 2, goo, 200f, 0.5f, 1f );
		if ( index == Brain ) CreatureGore.Jiggle( part.Go, 1f );
	}

	void ShatterHelmet( Vector3 direction )
	{
		var part = parts[Helmet];
		if ( part is null || part.Gone ) return;
		GoneMask |= SubtreeMask( Helmet );
		MarkGone( Helmet );
		var at = part.Go.GlobalPosition + Vector3.Up * 0.4f;
		Sfx.Play( "glass_break", at, 1f );
		GlassShards( at, direction );
		part.Go.QueueFree();
		ShakeNear( 0.3f );
	}

	void GlassShards( Vector3 at, Vector3 direction )
	{
		var material = new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.8f, 0.95f, 1f, 0.7f ),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			Roughness = 0.05f,
			EmissionEnabled = true,
			Emission = new Color( 0.6f, 0.9f, 1f ),
			EmissionEnergyMultiplier = 0.6f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		var shards = new CpuParticles3D
		{
			Name = "Kraken Glass",
			Mesh = new PrismMesh { Size = new Vector3( 0.22f, 0.3f, 0.02f ), Material = material },
			Amount = 48,
			Lifetime = 2.4f,
			OneShot = true,
			Explosiveness = 1f,
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
			EmissionSphereRadius = 0.7f,
			Direction = (direction.SafeNormal() * 0.4f + Vector3.Up).Normalized(),
			Spread = 80f,
			InitialVelocityMin = 2.5f,
			InitialVelocityMax = 7f,
			Gravity = new Vector3( 0f, -4f, 0f ),
			AngularVelocityMin = -360f,
			AngularVelocityMax = 360f,
			ParticleFlagRotateY = true,
			ScaleAmountMin = 0.5f,
			ScaleAmountMax = 1.4f,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		GetTree().CurrentScene.AddChild( shards );
		shards.GlobalPosition = at;
		shards.Emitting = true;
		Fx.Expire( shards, 3.5f );
	}

	void CutTentacle( int index, Vector3 direction )
	{
		var part = parts[index];
		if ( part is null || part.Gone ) return;
		GoneMask |= SubtreeMask( index );
		MarkGone( index );
		var at = part.Go.GlobalPosition;
		if ( Has( Head ) ) CreatureGore.AddStump( parts[Head].Go, at, (at - parts[Head].Go.GlobalPosition).Flat().SafeNormal(), 14f, Dark, Goo );
		for ( var i = 0; i < 3; i++ ) gore.GooBurst( at, direction, Goo, 8, 300f, 1.4f );
		gore.GooSpray( at, direction, 5, Goo, 260f, 0.7f, 1.5f );
		var piece = Detach( part, direction * Rng.Float( 60f, 140f ) + Vector3.Up * 60f, 3f, 14f, 120f, 30f );
		CreatureGore.Twitch( piece, Rng.Float( 2.5f, 4f ), 0.25f );
	}

	// ---- Death ----

	protected override void OnDied()
	{
		timeSinceDeath = 0f;
		RaiseKilled();
		if ( diedLocally ) return;
		diedLocally = true;
		if ( rig is null || !IsInstanceValid( rig ) ) return;
		var direction = lastShotDirection;
		var top = parts[Head].Go.GlobalPosition + parts[Head].Go.GlobalBasis * Collar;
		Sfx.Play( "creature_death", top, 1f, 0.5f );
		if ( Has( Helmet ) ) ShatterHelmet( direction );
		// The brain bursts.
		for ( var i = 0; i < 6; i++ ) gore.GooBurst( top, direction + Vector3.Up, i % 2 == 0 ? BrainPink : Goo, 12, 400f, 1.7f );
		for ( var i = 0; i < 5; i++ ) gore.ThrowBlob( top, direction + Rng.OnSphere() * 0.6f, BrainPink, Goo, 7f );
		gore.GooSpray( top, Vector3.Down, 10, Goo, 320f, 0.9f, 2f );
		if ( Has( Brain ) ) { MarkGone( Brain ); parts[Brain].Go.QueueFree(); }
		for ( var k = 0; k < Tentacles; k++ )
		{
			if ( !Has( Tentacle0 + k ) ) continue;
			var part = parts[Tentacle0 + k];
			MarkGone( Tentacle0 + k );
			var outward = (part.Go.GlobalPosition - GlobalPosition).Flat().SafeNormal();
			var piece = Detach( part, outward * Rng.Float( 80f, 160f ) + Vector3.Up * 40f, 2f, 14f, 120f, 30f );
			CreatureGore.Twitch( piece, Rng.Float( 2f, 4f ), 0.25f );
		}
		// The head stays on the rig and sinks (Sink); nothing on it can be shot any more.
		foreach ( var body in rig.FindChildren( "*", "StaticBody3D", true, false ).OfType<StaticBody3D>() ) body.CollisionLayer = 0;
		foreach ( var mesh in glowing ) if ( IsInstanceValid( mesh ) && mesh.MaterialOverride is StandardMaterial3D m ) m.EmissionEnergyMultiplier = 0.15f;
		sinceDust = 0f;
		SandBurst( GlobalPosition, 2.5f, 50, 2.5f );
		ShakeNear( 0.5f );
	}
}
