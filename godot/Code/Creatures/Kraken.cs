using Godot;

/// <summary>
/// The Kraken event boss (issue #30). It burrows up out of the sea bed in a cloud of sand and stays where it came
/// up, turning to face the nearest player. Its brain sits on top of its head under a spacesuit glass dome: the dome
/// has its own health and shatters, and after that the brain is the weak point. A tentacle can be shot through at
/// any segment: what is past the cut drops off as a limp chain of pieces, and the stump pumps goo.
/// Stage 1: the body, the arrival and dying. Stage 2: the whirlpool that keeps players close (KrakenWhirlpool.cs).
/// Stage 3: the tentacle slam (KrakenSlam.cs). The bite, the ink and the wave odds come in later stages.
/// Playtest round 3: the mouth, the dome's glass and the death are in KrakenBurst.cs.
/// Playtest round 1: drawn at twice the model's size (Size), slower, and alive (KrakenLife.cs).
/// Model: Tools/blender/kraken.py (assets/models/creatures/kraken; the mouth is underneath).
/// </summary>
public partial class Kraken : PartCreature
{
	const int Head = 0, Helmet = 1, Brain = 2, Tentacle0 = 3;
	public const int Tentacles = 8;
	// Shared with kraken.py.
	const int Segments = 8;
	const float SegLength = 0.85f, RootRadius = 0.34f, TipRadius = 0.07f;
	static readonly Vector3 Collar = new( 0f, 2.78f, 0.25f );

	public static readonly List<Kraken> All = new();

	public float HelmetHealth = 600f;
	/// <summary> Health of a tentacle's thickest segment; the tip has TipHealthScale of it. </summary>
	public float SegmentHealth = 220f, TipHealthScale = 0.3f;
	/// <summary> Damage to the whole Kraken per point of damage on: the brain, the head, a tentacle. </summary>
	public float BrainMultiplier = 3f, HeadMultiplier = 1f, TentacleMultiplier = 0.5f;
	/// <summary> Seconds to come up out of the ground, and how deep it starts (in the model's own units, before Size). </summary>
	public float RiseTime = 4.2f, Depth = 4.8f;
	/// <summary> How fast it turns to face the player (per second). </summary>
	public float TurnRate = 0.6f;
	/// <summary> Seconds its body takes to slide back under the sand once dead. </summary>
	public float SinkTime = 3.4f;

	public enum Mode { Burrowing, Idle }
	public Mode State { get; private set; } = Mode.Burrowing;
	/// <summary> 0 to 1: how much of the dome's health is left (0 once shattered). </summary>
	public float HelmetFraction => Has( Helmet ) ? MathX.Clamp( parts[Helmet].Health / MathF.Max( 1f, parts[Helmet].MaxHealth ), 0f, 1f ) : 0f;
	public bool HelmetOn => parts is not null && Has( Helmet );
	public int TentaclesLeft => length.Count( n => n > 0 );
	/// <summary> How many segments a tentacle still has (0: shot off at the root). </summary>
	public int SegmentsLeft( int k ) => length[k];
	/// <summary> The part index shots report for one segment of one tentacle. </summary>
	public static int SegIndex( int k, int j ) => SegBase + k * Segments + j;
	const int SegBase = 100;

	/// <summary> Where its brain is in the world (the middle of the dome). </summary>
	public Vector3 BrainPosition => (Has( Head ) ? parts[Head].Go.GlobalTransform : rig.GlobalTransform) * (Collar + Vector3.Up * 0.45f);
	/// <summary> The base of a tentacle in the world. </summary>
	public Vector3 TentaclePosition( int k, int j = 2 ) => segments[k, Math.Min( j, Math.Max( 0, length[k] - 1 ) )].GlobalTransform * new Vector3( 0f, 0f, -SegLength * 0.5f );
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
	// The roots sit well off the sea bed now: out, down to the sand, along it, and the tip curling up.
	static readonly float[] RestBend = { 20, -30, -25, 5, 22, 8, 12, 23 };
	static readonly float[] CurlBend = { 75, 8, 8, 5, 0, -10, -25, -40 };

	readonly Node3D[,] segments = new Node3D[Tentacles, Segments];
	readonly float[] phase = new float[Tentacles];
	readonly float[,] segHealth = new float[Tentacles, Segments];
	readonly int[] length = new int[Tentacles];
	readonly List<(Node3D Stump, TimeSince Age, float Size)> bleeders = new();
	TimeSince sinceSpurt;
	readonly List<MeshInstance3D> glowing = new();
	Node3D mound, brainModel;
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
			"flesh" => new Color( 0.72f, 0.28f, 0.4f ),
			_ => Green,
		};
		switch ( surface )
		{
			case "glow" or "sucker": return Shapes.Material( tint, glow: true );
			case "eye": return new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.08f, ClearcoatEnabled = true, Clearcoat = 1f, ClearcoatRoughness = 0.05f };
			case "collar": return new StandardMaterial3D { AlbedoColor = tint, Metallic = 0.85f, Roughness = 0.35f };
			case "mouth": return new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.9f };
			case "skin" or "belly":
				// Dry cracked mud: matte plates in the skin colour with black cracks (Tools/blender/mudcrack_texture.py).
				var mud = Shapes.Material( tint, "mudcrack" );
				mud.Uv1Scale = Vector3.One * 0.9f;
				mud.ClearcoatEnabled = false;
				mud.RimEnabled = false;
				return mud;
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
		// The model hangs off a pivot that breathes, so hits can wobble the model itself.
		headPivot = new Node3D { Name = "Head Pivot" };
		head.Go.AddChild( headPivot );
		headModel = Sculpt( head, headPivot, "head" );
		BuildMouth( head );
		// Both stop at the collar (the dome is the whole top half of the head now), so a shot up at the dome or
		// the brain from below is not caught by the head.
		HitShape( head.Go, Head, new Vector3( 0f, 1.2f, -0.1f ), new Vector3( 0f, 1.8f, 0.05f ), 0.9f );
		HitShape( head.Go, Head, new Vector3( 0f, 1.7f, 0.25f ), new Vector3( 0f, 1.76f, 0.25f ), 1f );

		var brain = AddPart( Brain, Head, head.Go, "Brain", Vector3.Zero, Basis.Identity, 0f );
		brain.Go.Position = Collar;
		brainModel = Sculpt( brain, brain.Go, "brain" );
		HitShape( brain.Go, Brain, new Vector3( 0f, 0.3f, 0f ), new Vector3( 0f, 0.36f, 0f ), 0.75f );

		var helmet = AddPart( Helmet, Head, head.Go, "Helmet", Vector3.Zero, Basis.Identity, HelmetHealth );
		helmet.Go.Position = Collar;
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
		BuildDome( helmet.Go );
		HitShape( helmet.Go, Helmet, new Vector3( 0f, 0.18f, 0f ), new Vector3( 0f, 0.3f, 0f ), 1.13f );

		for ( var k = 0; k < Tentacles; k++ )
		{
			// Evenly round the base, two of them either side of the mouth.
			var angle = (k + 0.5f) / Tentacles * MathF.Tau;
			var outward = new Vector3( MathF.Sin( angle ), 0f, -MathF.Cos( angle ) );
			var tentacle = AddPart( Tentacle0 + k, Head, head.Go, $"Tentacle {k}", Vector3.Zero, Shapes.LookAt( outward ), 0f );
			tentacle.Go.Position = outward * 0.85f + Vector3.Up * 1.05f;
			phase[k] = Rng.Float( 0f, MathF.Tau );
			length[k] = Segments;
			Node3D parent = tentacle.Go;
			for ( var j = 0; j < Segments; j++ )
			{
				var segment = new Node3D { Name = $"Segment {j}", Position = j == 0 ? Vector3.Zero : new Vector3( 0f, 0f, -SegLength ) };
				parent.AddChild( segment );
				Sculpt( tentacle, segment, $"seg{j}" );
				var radius = RootRadius * MathF.Pow( TipRadius / RootRadius, (j + 0.5f) / Segments );
				var hit = new Node3D { Name = "Hit" };
				segment.AddChild( hit );
				segHealth[k, j] = SegmentHealth * MathX.Lerp( 1f, TipHealthScale, j / (Segments - 1f) );
				HitShape( hit, SegIndex( k, j ), new Vector3( 0f, 0f, -radius ), new Vector3( 0f, 0f, -SegLength + radius ), radius * 1.15f );
				segments[k, j] = segment;
				parent = segment;
			}
		}

		// The sand it pushed up: a ring of lumps round where it came through.
		// It belongs to the sea bed, not to the Kraken: it stays put when the Kraken turns.
		mound = new Node3D { Name = "Kraken Mound" };
		GetTree().CurrentScene.AddChild( mound );
		mound.GlobalPosition = GlobalPosition;
		// An outer ring, and a taller inner one the body sits down into (playtest round 2: it looked like it floated).
		for ( var i = 0; i < 26; i++ )
		{
			var inner = i >= 14;
			var a = (inner ? (i - 14) / 12f : i / 14f) * MathF.Tau + Rng.Float( -0.15f, 0.15f );
			var r = (inner ? Rng.Float( 0.85f, 1.2f ) : Rng.Float( 1.5f, 2.3f )) * Size;
			var lump = new MeshInstance3D
			{
				Mesh = Shapes.Sphere,
				MaterialOverride = new StandardMaterial3D { AlbedoColor = (Sand * Rng.Float( 0.85f, 1.05f )) with { A = 1f }, Roughness = 1f },
				Position = new Vector3( MathF.Cos( a ) * r, -0.05f, MathF.Sin( a ) * r ),
				Scale = new Vector3( Rng.Float( 0.9f, 1.5f ), Rng.Float( 0.35f, 0.6f ) * (inner ? 1.7f : 1f), Rng.Float( 0.9f, 1.5f ) ) * 1.7f,
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
	float Unfurled => MathX.Clamp( ((float)sinceSpawned - RiseTime * 0.7f) / 2.2f, 0f, 1f );

	static float Ease( float t ) => t * t * (3f - 2f * t);

	void PlaceRig()
	{
		var up = Ease( Risen );
		// A shudder on the way up, as if forcing through.
		var shudder = State == Mode.Burrowing ? MathF.Sin( time * 31f ) * 0.03f * (1f - up) : 0f;
		rig.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized().Scaled( Vector3.One * Size ), GlobalPosition + Vector3.Down * Depth * Size * (1f - up) + new Vector3( shudder, 0f, -shudder ) );
	}

	public override void _Process( double delta )
	{
		if ( health is null || rig is null ) return;
		var dt = (float)delta;
		if ( IsDead )
		{
			Bleed();
			Dying( dt );
			return;
		}
		if ( !IsInstanceValid( rig ) ) return;
		time += dt;
		if ( State == Mode.Burrowing ) Burrow();
		else
		{
			if ( !DevFlags.FreezeEnemies ) Think( dt );
			UpdateAttack( dt );
		}
		PlaceRig();
		UpdateLife( dt );
		Animate( dt );
		TintParts( dt );
		Bleed();
		RefreshRing();
		UpdateWhirlpool( dt );
		CreatureGore.BleedTrail( parts[Head].Go.GlobalPosition + Vector3.Up * 1.4f * Size, Goo, health.Fraction, ref bleedTimer );
	}

	TimeSince bleedTimer;

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
		if ( sinceSpawned < RiseTime + 2.3f ) return;
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
			for ( var j = 0; j < length[k]; j++ )
			{
				var (bend, sway) = SegmentPose( k, j, unfurl );
				segments[k, j].Basis = new Basis( Vector3.Up, MathX.Rad( sway ) ) * new Basis( Vector3.Right, MathX.Rad( bend ) );
			}
		}
		ApplySlamPose();
		if ( State == Mode.Idle ) for ( var k = 0; k < Tentacles; k++ ) KeepAboveGround( k );
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
		radius *= Size;
		speed *= 1.4f;
		sandMaterial ??= new StandardMaterial3D
		{
			AlbedoColor = Sand with { A = 0.24f },
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
		if ( index >= SegBase ) { ShotSegment( (index - SegBase) / Segments, (index - SegBase) % Segments, damage, direction, position, origin, shooter, scoreHit ); return; }
		if ( index < 0 || index >= Tentacle0 || !Has( index ) ) index = Head;
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
			else CrackDome( position, direction );
			shooter?.RewardHit( false, false, scoreHit );
			return;
		}

		var multiplier = index == Brain ? BrainMultiplier : HeadMultiplier;
		Hit( index, position, direction, 1f - health.Fraction );
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * multiplier, Position = position, Origin = origin, Attacker = shooter } );
		if ( wasAlive && shooter is not null ) shooter.RewardHit( IsDead, index == Brain, scoreHit );
	}

	/// <summary> A shot on one segment of a tentacle: it bleeds, and when its health is gone the tentacle is cut there. </summary>
	void ShotSegment( int k, int j, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( k < 0 || k >= Tentacles || j >= length[k] ) return;
		NoteShot( direction, position, origin );
		var part = parts[Tentacle0 + k];
		part.Flash = 0f;
		KickSegment( k, j, direction, 240f );
		segHealth[k, j] -= damage;
		var goo = Goo;
		CreatureGore.AddWound( segments[k, j], position, direction, Dark, goo, 5f );
		gore.GooBurst( position, -direction, goo, 10, 300f, 2f );
		gore.GooSpray( position, direction, 3, goo, 260f, 0.9f, 1.8f );
		if ( segHealth[k, j] <= 0f ) CutAt( k, j, direction );
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * TentacleMultiplier, Position = position, Origin = origin, Attacker = shooter } );
		if ( wasAlive && shooter is not null ) shooter.RewardHit( IsDead, false, scoreHit );
	}

	void Hit( int index, Vector3 position, Vector3 direction, float hurt )
	{
		var part = parts[index];
		part.Flash = 0f;
		part.Hurt = MathF.Max( part.Hurt, hurt * 0.7f );
		if ( index == Head ) KickHead( direction, 30f );
		var goo = index == Brain ? BrainPink : Goo;
		if ( part.Renderers.Count > 0 ) CreatureGore.AddWound( part.Go, position, direction, part.Renderers[0].Tint, goo, index == Head ? 9f : 6f );
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
		var at = part.Go.GlobalPosition + Vector3.Up * 0.55f * Size;
		Sfx.Play( "glass_break", at, 1f );
		// The panes themselves fly (BlowDome); the particles are only the glitter between them.
		BlowDome( direction );
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
			Mesh = new PrismMesh { Size = new Vector3( 0.12f, 0.16f, 0.015f ), Material = material },
			Amount = 80,
			Lifetime = 2.4f,
			OneShot = true,
			Explosiveness = 1f,
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
			EmissionSphereRadius = 1.05f * Size,
			Direction = (direction.SafeNormal() * 0.4f + Vector3.Up).Normalized(),
			Spread = 80f,
			InitialVelocityMin = 4f,
			InitialVelocityMax = 11f,
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

	static float SegRadius( float j ) => RootRadius * MathF.Pow( TipRadius / RootRadius, j / Segments );

	/// <summary>
	/// Cuts a tentacle at a segment. Everything from there to the tip comes loose as a chain of physics pieces
	/// pinned end to end, so it flops; the stump left behind gushes and keeps pumping goo.
	/// </summary>
	void CutAt( int k, int j, Vector3 direction, bool heavy = true, float fling = 1f )
	{
		var count = length[k];
		if ( j < 0 || j >= count ) return;
		var part = parts[Tentacle0 + k];
		var cutAt = segments[k, j].GlobalPosition;
		var along = -segments[k, j].GlobalBasis.Z.Normalized();
		var cutRadius = SegRadius( j ) * Size / U.In;
		var push = (direction.SafeNormal() * 0.8f + along * 0.6f + Vector3.Up * 0.3f) * Rng.Float( 0.9f, 1.5f ) * fling;

		// From the tip inward, so each segment is taken before the one it hangs from.
		var gibs = new Gib[Segments];
		for ( var s = count - 1; s >= j; s-- )
		{
			var segment = segments[k, s];
			var piece = new Node3D { Name = "Kraken Piece" };
			GetTree().CurrentScene.AddChild( piece );
			piece.GlobalTransform = segment.GlobalTransform.Orthonormalized();
			(Shape3D Shape, Transform3D Global)? shape = null;
			foreach ( var child in segment.GetChildren().OfType<Node3D>().ToList() )
			{
				if ( child.Name == "Hit" )
				{
					if ( child.FindChildren( "*", "CollisionShape3D", true, false ).OfType<CollisionShape3D>().FirstOrDefault() is { } cs ) shape = (cs.Shape, cs.GlobalTransform);
					child.QueueFree();
					continue;
				}
				foreach ( var mesh in child.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
				{
					part.Renderers.RemoveAll( r => r.Renderer == mesh );
					glowing.Remove( mesh );
				}
				child.Reparent( piece, true );
			}
			segment.QueueFree();
			segments[k, s] = null;
			var radius = SegRadius( s + 0.5f ) * Size;
			var gib = Gib.Attach( piece, gore, radius / U.In, 70f, 3f + radius * 40f, part.Velocity * 0.4f + push, Goo, addCollider: false );
			if ( shape is { } found )
			{
				// The piece keeps the Kraken's size; its collider is rebuilt at that size (bodies cannot be scaled).
				var capsule = (CapsuleShape3D)found.Shape;
				var collider = new CollisionShape3D { Shape = new CapsuleShape3D { Radius = capsule.Radius * Size, Height = capsule.Height * Size } };
				gib.AddChild( collider );
				collider.GlobalTransform = found.Global.Orthonormalized();
			}
			gib.AngularVelocity = Rng.InSphere( 2f );
			CreatureGore.Twitch( gib, Rng.Float( 2.5f, 4.5f ), 0.2f );
			gibs[s] = gib;
			if ( s == j && CreatureGore.AddStump( piece, cutAt, -along, cutRadius * 0.9f, Dark, Goo, 8f ) is { } looseEnd && heavy ) bleeders.Add( (looseEnd, 0f, cutRadius / 26f * 0.6f) );
		}
		// Pin each piece to the next at the joint between them.
		for ( var s = j; s < count - 1; s++ )
		{
			var joint = new PinJoint3D { Name = "Joint" };
			gibs[s].AddChild( joint );
			joint.GlobalPosition = gibs[s + 1].GlobalPosition;
			joint.NodeA = gibs[s].GetPath();
			joint.NodeB = gibs[s + 1].GetPath();
		}
		length[k] = j;
		Thrash( k, 320f );

		// The stump: on the segment before the cut, or on the head if the whole tentacle went.
		var keep = j == 0 ? parts[Head].Go : segments[k, j - 1];
		if ( j == 0 )
		{
			GoneMask |= 1 << (Tentacle0 + k);
			MarkGone( Tentacle0 + k );
			slammers.Remove( k );
			slamBlend[k] = 0f;
			part.Go.QueueFree();
		}
		if ( CreatureGore.AddStump( keep, cutAt, along, cutRadius * 0.95f, Dark, Goo, 14f ) is { } stump && heavy ) bleeders.Add( (stump, 0f, cutRadius / 26f) );
		// A lot of goo, sized for something this big: a gush out of the cut, a pool under it, chunks.
		var big = MathX.Lerp( 3f, 7f, cutRadius / 26f );
		var bursts = heavy ? 7 : 2;
		for ( var i = 0; i < bursts; i++ ) gore.GooBurst( cutAt, along + direction.SafeNormal() * 0.4f + Rng.InSphere( 0.4f ), Goo, 12, 420f, big );
		gore.GooSpray( cutAt, along, heavy ? 10 : 3, Goo, 380f, 1.5f, 3.2f );
		gore.GooSpray( cutAt, Vector3.Down, heavy ? 6 : 2, Goo, 300f, 2f, 3.6f );
		if ( gore.TraceWorld( cutAt, cutAt + Vector3.Down * 14f ) is { } floor ) gore.GooSplat( floor.Position, floor.Normal, heavy ? 4.5f : 2.5f, Goo );
		if ( heavy ) for ( var i = 0; i < 4; i++ ) gore.ThrowBlob( cutAt, along + Rng.OnSphere() * 0.5f, Dark, Goo, MathF.Max( 3f, cutRadius * 0.45f ) );
		gore.Squelch( cutAt, 1.3f, 0.75f );
	}

	/// <summary> Fresh stumps pump goo in beats for a while. </summary>
	void Bleed()
	{
		if ( bleeders.Count == 0 || sinceSpurt < 0.1f ) return;
		sinceSpurt = 0f;
		bleeders.RemoveAll( b => b.Age > 8f || !IsInstanceValid( b.Stump ) );
		foreach ( var (stump, age, size) in bleeders )
		{
			// A heartbeat: strong squirts that weaken as it bleeds out, and a pool that keeps growing underneath.
			var beat = MathF.Max( 0f, MathF.Sin( age * 9f ) );
			var strength = (1f - age / 8f) * (0.35f + beat);
			var outward = -stump.GlobalBasis.Z.Normalized();
			var at = stump.GlobalPosition + outward * 0.05f;
			gore.GooBurst( at, outward * 2f + Vector3.Up * 0.3f, Goo, beat > 0.6f ? 7 : 3, 380f * strength, MathX.Lerp( 1.6f, 3.6f, size ) );
			if ( beat > 0.85f )
			{
				gore.GooSpray( at, outward, 2, Goo, 340f, 1.2f, 2.6f );
				if ( gore.TraceWorld( at, at + Vector3.Down * 14f ) is { } floor ) gore.GooSplat( floor.Position + Rng.InSphere( 0.5f ).Flat(), floor.Normal, Rng.Float( 1.2f, 2.4f ), Goo );
			}
		}
	}

	// ---- Death ----
	protected override void OnDied()
	{
		timeSinceDeath = 0f;
		RaiseKilled();
		ReleaseAll();
		EndAttack();
		if ( diedLocally ) return;
		diedLocally = true;
		if ( rig is null || !IsInstanceValid( rig ) ) return;
		// Nothing on it can be shot any more; it swells for a moment and then bursts (Dying, in KrakenBurst.cs).
		foreach ( var body in rig.FindChildren( "*", "StaticBody3D", true, false ).OfType<StaticBody3D>() ) body.CollisionLayer = 0;
		Sfx.Play( "creature_death", BrainPosition, 1f, 0.5f );
		ShakeNear( 0.5f );
	}
}
