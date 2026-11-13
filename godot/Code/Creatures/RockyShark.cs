using Godot;

/// <summary>
/// Rocky Shark (issue #29): a stone-plated sea serpent that swims like the Bloober serpent but with a much livelier
/// tail. Its green radiation holes and eyes are the weak points; the rock plates turn most shots. A bite can give
/// the player radiation sickness (<see cref="PlayerAfflictions.Irradiate"/>). Shooting a body section cuts the
/// shark short and drops the tail behind it, as the Bloober loses its last segments.
///
/// Built from the Blender parts in res://assets/models/creatures/rockyshark (Tools/blender/rocky_shark.py):
/// head with a hinged Jaw, two Eye meshes and Hole meshes; two body sections; a tail with its caudal fin; and a
/// pair of pectoral fins. The meshes are painted by name: Hole and Eye are emissive, the rest take the rock tint.
/// Sizes and speeds are in s&box inches, like the rest of the ported creatures.
/// </summary>
public partial class RockyShark : PartCreature
{
	// ---- Tuning (s&box inches, speeds in inches per second) ----
	public float MoveSpeed = 200f;
	public float TurnRate = 2.2f;
	public float WiggleDegrees = 24f;
	public float WiggleRate = 2.8f;
	public float MinAltitude = 30f;
	public float AimHeight = 40f;
	public float Size = 1f;
	public float Hue = -1f;

	public float BiteRange = 110f;
	public float BiteDamage = 15f;
	public float BiteCooldown = 1.6f;
	public float BiteRadiationChance = 0.6f;
	public float BiteRadiationTime = 8f;

	public float HeadRadius = 14f;
	public float SegmentHealth = 45f;
	public float EyeHealth = 25f;
	public float ArmorMultiplier = 0.55f;
	public float EyeMultiplier = 2.2f;
	public float HoleMultiplier = 2.6f;
	public float HoleRadius = 5f;
	public int SegmentCount = 4;

	/// <summary> Every Rocky Shark in the scene, so the director can clear them and they can avoid each other. </summary>
	public static readonly List<RockyShark> All = new();

	// ---- Part indices: the head holds the creature's health; the eyes and sections can be shot off ----
	const int Head = 0, EyeL = 1, EyeR = 2, FirstSegment = 3;
	int TailIndex => FirstSegment + SegmentCount;

	/// <summary> How many times it has bitten (for tests). </summary>
	public int Bites { get; private set; }
	/// <summary> Body sections shot off so far. </summary>
	public int LostSegments
	{
		get
		{
			if ( parts is null ) return 0;
			var lost = 0;
			for ( var i = 0; i < SegmentCount; i++ )
				if ( parts[FirstSegment + i] is { Gone: true } ) lost++;
			return lost;
		}
	}
	/// <summary> Both eyes shot out: it is slower and bites at shorter range. </summary>
	public bool Blind => parts is not null && !Has( EyeL ) && !Has( EyeR );
	/// <summary> Where a bite lands, in world space (the mouth under the snout). </summary>
	public Vector3 MouthWorld
	{
		get
		{
			if ( parts is null || parts[Head] is null || !IsInstanceValid( parts[Head].Go ) ) return GlobalPosition;
			var head = parts[Head].Go;
			return head.GlobalPosition + (head.GlobalBasis.Z * -1.45f + head.GlobalBasis.Y * -0.20f) * HeadRadius * U.In * Size;
		}
	}
	/// <summary> One live radiation hole and the part it sits on (for tests). </summary>
	public (int Segment, Vector3 Position)? FirstHole
	{
		get
		{
			foreach ( var hole in holes )
				if ( IsInstanceValid( hole.Node ) && Has( hole.Segment ) ) return (hole.Segment, hole.Node.GlobalPosition);
			return null;
		}
	}
	/// <summary> How many glowing radiation holes the model has. </summary>
	public int HoleCount => holes.Count;
	/// <summary> A body section's part index (for tests). </summary>
	public int SectionIndex( int index ) => FirstSegment + index;
	/// <summary> Is this body section still attached (for tests). </summary>
	public bool SectionAlive( int index ) => parts is not null && index >= 0 && index < SegmentCount && parts[FirstSegment + index] is { Gone: false };
	/// <summary> Where a body section sits, in world space (for tests). </summary>
	public Vector3 SectionPosition( int index ) => parts is not null && index >= 0 && index < SegmentCount ? parts[FirstSegment + index].Go.GlobalPosition : GlobalPosition;

	sealed class Hole
	{
		public MeshInstance3D Node;
		public int Segment;
		public OmniLight3D Light;
	}

	readonly List<Hole> holes = new();
	readonly List<(Node3D Node, float Side)> fins = new();
	MeshInstance3D eyeMeshL, eyeMeshR;
	Node3D jaw;
	Color rockColor = new( 0.32f, 0.31f, 0.30f );
	Color finColor = new( 0.20f, 0.48f, 0.30f );
	Color gooColor = new( 0.35f, 1f, 0.28f );
	Vector3 heading;
	Vector3 lastForward;
	TimeSince sinceBite = 999f;
	TimeSince sinceOoze = 999f;
	TimeSince bleedTimer;
	float bank;
	float jawOpen;

	/// <summary> Spawns a Rocky Shark with this much health, facing a direction. </summary>
	public static RockyShark Spawn( Node parent, Vector3 position, float maxHealth, Vector3? facing = null )
	{
		var shark = new RockyShark { Name = "Rocky Shark" };
		Place( shark, parent, position, maxHealth );
		if ( facing is { } f && f.Flat() != Vector3.Zero ) shark.GlobalBasis = Shapes.LookAt( f.Flat() );
		return shark;
	}

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }
	protected override string Kind => "Shark";
	protected override Color GibGoo => gooColor;

	protected override void Setup()
	{
		BeginSetup( 240f );
		if ( Hue < 0f ) Hue = Rng.Float();
		var hue = Hue;
		// Wet basalt: a little warm from the sand, with a paler belly and green-black fins.
		rockColor = Shapes.Hsv( 0.07f + hue * 0.05f, 0.18f, 0.55f );
		finColor = Shapes.Hsv( 0.33f + hue * 0.04f, 0.55f, 0.42f );
		gooColor = Shapes.Hsv( 0.30f + hue * 0.05f, 1f, 1f );
		heading = -GlobalBasis.Z;
		lastForward = heading;
		Build();
		PoseChain( 0.0001f );
		if ( rig is not null ) rig.Visible = true;
	}

	void Build()
	{
		SegmentCount = Math.Max( 1, SegmentCount );
		parts = new Part[FirstSegment + SegmentCount + 1];   // head, two eyes, the sections, and the tail
		rig = new Node3D { Name = "Rocky Shark Rig" };
		GetTree().CurrentScene.AddChild( rig );
		rig.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized().Scaled( Vector3.One * Size ), GlobalPosition );

		// Head: the whole creature's health. A long, low wedge with a hinged jaw and the glowing eyes.
		var head = AddPart( Head, -1, rig, "Head", Vector3.Zero, Basis.Identity, 0f );
		Sculpt( head, "head", HeadRadius );
		Trigger( head, new Vector3( -0.60f * HeadRadius, 0, 0 ), new Vector3( 2.15f * HeadRadius, 0, 0 ), 0.72f * HeadRadius );

		// Eyes: small, precise parts on the head. Their own health, and the best damage in the game.
		foreach ( var (index, side, name) in new[] { (EyeL, 1f, "Eye L"), (EyeR, -1f, "Eye R") } )
		{
			var eye = AddPart( index, Head, head.Go, name, new Vector3( 0.92f * HeadRadius, side * 0.46f * HeadRadius, 0.22f * HeadRadius ), Basis.Identity, EyeHealth );
			Trigger( eye, Vector3.Zero, new Vector3( 0.14f * HeadRadius, 0, 0 ), 0.17f * HeadRadius );
		}

		// Body: a chain of armoured sections. Each hangs off the one in front, overlapping so the body reads as one.
		var parent = head;
		for ( var i = 0; i < SegmentCount; i++ )
		{
			var radius = SegmentRadius( i );
			var length = SegmentLength( i );
			var frontLength = i == 0 ? HeadLength : SegmentLength( i - 1 );
			var gap = (frontLength * 0.5f + length * 0.5f) * 0.64f;
			var segment = AddPart( FirstSegment + i, parent.Index, parent.Go, $"Section {i + 1}", new Vector3( -gap, 0, 0 ), Basis.Identity, SegmentHealth );
			Sculpt( segment, i % 2 == 0 ? "segment0" : "segment1", radius );
			Trigger( segment, new Vector3( -1.35f * radius, 0, 0 ), new Vector3( 1.35f * radius, 0, 0 ), radius * 0.70f );
			parent = segment;
		}

		var tail = AddPart( TailIndex, parent.Index, parent.Go, "Tail", new Vector3( -(SegmentLength( SegmentCount - 1 ) * 0.5f + TailLength * 0.5f) * 0.70f, 0, 0 ), Basis.Identity, SegmentHealth );
		Sculpt( tail, "tail", TailRadius );
		Trigger( tail, new Vector3( -1.35f * TailRadius, 0, 0 ), new Vector3( 1.05f * TailRadius, 0, 0 ), TailRadius * 0.62f );

		BuildFins();

		foreach ( var part in parts )
			if ( part is not null ) part.Rest = part.Go.Basis;
	}

	// ---- Building ----

	static readonly Dictionary<string, PackedScene> models = new();

	static Node3D Model( string name )
	{
		if ( !models.TryGetValue( name, out var scene ) )
		{
			var path = $"res://assets/models/creatures/rockyshark/{name}.glb";
			models[name] = scene = ResourceLoader.Exists( path ) ? GD.Load<PackedScene>( path ) : null;
		}
		return scene?.Instantiate<Node3D>();
	}

	/// <summary> Puts a Blender part under a part node and paints its meshes by name. Holes and eyes glow. </summary>
	void Sculpt( Part part, string model, float radius )
	{
		var node = Model( model );
		if ( node is null )
		{
			BuildFallback( part, model, radius );
			return;
		}
		part.Go.AddChild( node );
		node.Scale = Vector3.One * (radius * 2f * U.In);
		foreach ( var mesh in node.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			var name = mesh.Name.ToString();
			if ( name.StartsWith( "Hole" ) )
			{
				mesh.MaterialOverride = Shapes.Material( gooColor, glow: true );
				mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
				var hole = new Hole { Node = mesh, Segment = part.Index };
				holes.Add( hole );
				if ( holes.Count <= 2 )
				{
					// Two holes carry the light; every hole carries the glow and the drips.
					hole.Light = new OmniLight3D { LightColor = gooColor, LightEnergy = 1.1f, OmniRange = 2.2f, ShadowEnabled = false };
					mesh.AddChild( hole.Light );
				}
			}
			else if ( name is "EyeL" or "EyeR" )
			{
				mesh.MaterialOverride = Shapes.Material( gooColor.Lerp( Colors.White, 0.35f ), glow: true );
				mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
				if ( name == "EyeL" ) eyeMeshL = mesh; else eyeMeshR = mesh;
			}
			else
			{
				var tint = name.StartsWith( "Teeth" ) ? new Color( 0.92f, 0.88f, 0.76f )
					: name == "Mouth" ? new Color( 0.10f, 0.01f, 0.02f )
					: name.StartsWith( "Fin" ) ? finColor
					: name == "Jaw" ? rockColor.Lightened( 0.08f )
					: name == "Head" ? rockColor.Lightened( 0.06f )
					: rockColor;
				mesh.MaterialOverride = Shapes.Material( tint );
				part.Renderers.Add( (mesh, tint) );
				if ( name == "Jaw" ) jaw = mesh;
			}
		}
	}

	/// <summary> A plain shape when the Blender parts are missing (a fresh clone before Godot imports them). </summary>
	void BuildFallback( Part part, string model, float radius )
	{
		if ( model is "fin_l" or "fin_r" ) return;
		var root = new Node3D { Name = model + " Fallback" };
		part.Go.AddChild( root );
		var tint = model == "head" ? rockColor.Lightened( 0.06f ) : rockColor;
		var mesh = Shapes.Part( root, "Body", Vector3.Zero, Shapes.Ellipsoid( 1.5f * radius, 0.78f * radius, 0.72f * radius ), tint );
		part.Renderers.Add( (mesh, tint) );
	}

	void BuildFins()
	{
		if ( parts[FirstSegment] is not { } segment ) return;
		var radius = SegmentRadius( 0 );
		foreach ( var (model, side, name) in new[] { ("fin_l", 1f, "Left Fin"), ("fin_r", -1f, "Right Fin") } )
		{
			var fin = Model( model );
			if ( fin is null ) continue;
			fin.Name = name;
			segment.Go.AddChild( fin );
			fin.Scale = Vector3.One * (radius * 2f * U.In);
			fin.Position = U.Sbox( new Vector3( 0.30f * radius, side * 0.60f * radius, -0.05f * radius ) );
			fins.Add( (fin, side) );
		}
	}

	float HeadLength => 3.1f * HeadRadius;
	float TailRadius => 7f;
	float TailLength => 3.1f * TailRadius;
	float SegmentRadius( int index ) => MathX.Lerp( 12f, 8f, index / (float)Math.Max( 1, SegmentCount - 1 ) );
	float SegmentLength( int index ) => 3f * SegmentRadius( index );

	// ---- Brain and body ----

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( parts is null || rig is null || !IsInstanceValid( rig ) || diedLocally ) return;
		if ( !DevFlags.FreezeEnemies )
		{
			Think( dt );
			UpdateBite();
		}
		UpdateOoze();
		if ( health is not null && health.Fraction < 0.65f )
			CreatureGore.BleedTrail( parts[Head].Go.GlobalPosition, gooColor, health.Fraction, ref bleedTimer );
		PoseChain( dt );
		UpdateJaw( dt );
		TintParts( dt );
	}

	void Think( float dt )
	{
		var target = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var now = (float)Clock.Now;
		Vector3 desired;
		if ( target is null ) desired = heading;
		else
		{
			var aim = target.WorldPosition + Vector3.Up * AimHeight * U.In;
			desired = (aim - GlobalPosition).SafeNormal();
			// Blind: it can still smell the player, but the aim wanders.
			if ( Blind ) desired = (desired + Rng.InSphere( 0.45f )).SafeNormal();
		}
		var wiggle = MathF.Sin( now * WiggleRate + Hue * 20f );
		desired = desired.Rotated( Vector3.Up, MathX.Rad( wiggle * WiggleDegrees ) );
		desired = (desired + Vector3.Up * MathF.Sin( now * WiggleRate * 0.5f + Hue * 9f ) * 0.25f).SafeNormal();
		heading = heading.Lerp( desired, MathX.Clamp( TurnRate * dt, 0f, 1f ) ).SafeNormal();
		if ( heading == Vector3.Zero ) heading = -GlobalBasis.Z;

		var speed = MoveSpeed * U.In * (1f - 0.07f * LostSegments) * (Blind ? 0.75f : 1f);
		var position = GlobalPosition;
		// Slide along the sea bed and the rocks instead of pushing through them.
		var ahead = Trace.Sphere( this, HeadRadius * 0.8f * U.In, position, position + heading * 80f * U.In, Layers.World );
		if ( ahead.Hit && !ahead.StartedSolid )
		{
			heading = (heading - ahead.Normal * heading.Dot( ahead.Normal ) + ahead.Normal * 0.7f + Vector3.Up * 0.35f).SafeNormal();
			speed *= 0.7f;
		}
		var ground = Trace.Ray( this, position, position + Vector3.Down * 500f * U.In, Layers.World );
		if ( ground.Hit && position.Y - ground.HitPosition.Y < (MinAltitude + HeadRadius) * U.In )
			heading = heading.WithY( MathF.Max( heading.Y, 0.55f ) ).SafeNormal();
		foreach ( var other in All )
		{
			if ( other == this || other.IsDead ) continue;
			var away = position - other.GlobalPosition;
			var distance = away.Length() / U.In;
			if ( distance < 70f && distance > 0.01f ) heading = (heading + away.SafeNormal() * (70f - distance) * 0.02f).SafeNormal();
		}
		GlobalPosition = position + heading * speed * dt;
		var from = GlobalBasis.GetRotationQuaternion();
		var to = Shapes.LookAt( heading ).GetRotationQuaternion();
		GlobalBasis = new Basis( from.SafeSlerp( to, MathX.Clamp( dt * 5f, 0f, 1f ) ) );
	}

	/// <summary> A wave rolling down the body, bigger toward the tail, banked into turns, fins paddling. </summary>
	void PoseChain( float dt )
	{
		dt = MathF.Max( dt, 0.0001f );
		rig.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized().Scaled( Vector3.One * Size ), GlobalPosition );
		var time = (float)Clock.Now;
		var rate = WiggleRate * (1.2f + MathX.Clamp( MoveSpeed / 300f, 0f, 1.4f ) );
		var forward = -GlobalBasis.Z;
		var turn = lastForward.Cross( forward ).Dot( GlobalBasis.Y ) / dt;
		lastForward = forward;
		bank = MathX.Lerp( bank, MathX.Clamp( -turn * 0.25f, -0.55f, 0.55f ), 1f - MathF.Exp( -4f * dt ) );
		var lost = LostSegments / (float)Math.Max( 1, SegmentCount );

		for ( var i = 0; i < SegmentCount; i++ )
		{
			var part = parts[FirstSegment + i];
			if ( part is null || part.Gone ) continue;
			var t = i / (float)Math.Max( 1, SegmentCount - 1 );
			var phase = time * rate - (i + 1) * 0.85f;
			var yaw = MathF.Sin( phase ) * (4f + 12f * t * t) * MathX.Lerp( 1f, 0.6f, lost );
			var pitch = MathF.Cos( phase * 0.85f ) * (2f + 5f * t * t);
			part.Go.Basis = part.Rest * new Angles( pitch, yaw, MathX.Deg( bank ) * t ).ToBasis();
		}

		var tail = parts[TailIndex];
		if ( tail is { Gone: false } )
		{
			var phase = time * rate - (SegmentCount + 1) * 0.85f;
			var yaw = MathF.Sin( phase ) * (20f + 6f * (1f - lost));
			tail.Go.Basis = tail.Rest * new Angles( MathF.Cos( phase * 0.85f ) * 10f, yaw, MathX.Deg( bank ) * 1.2f ).ToBasis();
		}

		var head = parts[Head];
		if ( head is not null )
			head.Go.Basis = new Angles( MathF.Sin( time * rate * 0.6f ) * 1.5f, MathF.Cos( time * rate * 0.5f ) * 2.5f, 0f ).ToBasis();

		foreach ( var (fin, side) in fins )
		{
			if ( !IsInstanceValid( fin ) ) continue;
			var paddle = MathF.Sin( time * rate * 1.5f + side ) * 0.35f;
			fin.Rotation = new Vector3( 0f, side * (0.22f + 0.18f * MathF.Sin( time * rate * 1.5f + 1f )), side * paddle );
		}
	}

	void UpdateBite()
	{
		if ( sinceBite < BiteCooldown ) return;
		var mouth = MouthWorld;
		var reach = BiteRange * U.In * Size * (Blind ? 0.7f : 1f);
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive ) continue;
			var body = player.WorldPosition + Vector3.Up * 0.9f;
			if ( body.DistanceTo( mouth ) > reach ) continue;
			Bites++;
			sinceBite = 0f;
			jawOpen = 1f;
			player.Health?.OnDamage( new DamageInfo { Damage = BiteDamage, Attacker = this, Position = body, Origin = mouth } );
			if ( Rng.Float() < BiteRadiationChance ) player.Afflictions?.Irradiate( BiteRadiationTime, poison: true );
			player.Juice?.Shake( 0.45f );
			var away = (body - mouth).SafeNormal();
			if ( away == Vector3.Zero ) away = -GlobalBasis.Z;
			gore.GooBurst( mouth, away, gooColor, 10, 260f, 1.1f );
			gore.GooSpray( mouth, away, 3, gooColor, 200f, 0.5f, 1.2f );
			Sfx.Play( "creature_attack", mouth, 0.85f, Rng.Float( 0.85f, 1.05f ) );
			Sfx.Play( "crab_snap", mouth, 0.7f, 0.65f );
			if ( player.Controller is { Scripted: false } controller )
				controller.Velocity += away * 3f + Vector3.Up * 1.5f;
			return;
		}
	}

	void UpdateJaw( float dt )
	{
		if ( jaw is null || !IsInstanceValid( jaw ) ) return;
		jawOpen = MathX.Approach( jawOpen, sinceBite < 0.35f ? 1f : 0f, dt * 5f );
		jaw.Rotation = new Vector3( -MathX.Clamp( jawOpen, 0f, 1f ) * 0.8f, 0f, 0f );
	}

	/// <summary> The green ooze: droplets fall from the radiation holes, and the two lights pulse. </summary>
	void UpdateOoze()
	{
		var time = (float)Clock.Now;
		foreach ( var hole in holes )
			if ( hole.Light is not null && IsInstanceValid( hole.Light ) )
				hole.Light.LightEnergy = 0.9f + MathF.Sin( time * 3f + hole.Segment ) * 0.25f;

		if ( sinceOoze < 0.45f ) return;
		sinceOoze = 0f;
		var live = holes.Where( h => IsInstanceValid( h.Node ) && Has( h.Segment ) ).ToList();
		for ( var i = 0; i < 3 && live.Count > 0; i++ )
		{
			var hole = live[Rng.Int( 0, live.Count - 1 )];
			var velocity = Vector3.Up * Rng.Float( 0.15f, 0.5f ) + Rng.InSphere( 0.18f );
			GooDroplet.Spawn( hole.Node.GlobalPosition, velocity, gooColor, Rng.Float( 0.5f, 1.1f ) * Size, lifeScale: 1.6f );
		}
	}

	// ---- Getting shot ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead || parts is null ) return;
		if ( index < 0 || index >= parts.Length || !Has( index ) ) index = Head;
		NoteShot( direction, position, origin );
		var eye = index is EyeL or EyeR;
		var hole = !eye && NearHole( position );
		var multiplier = eye ? EyeMultiplier : hole ? HoleMultiplier : ArmorMultiplier;
		var cut = false;
		float hurt;

		if ( eye )
		{
			var part = parts[index];
			part.Health -= damage;
			cut = part.Health <= 0f;
			hurt = 1f - MathX.Clamp( part.Health / MathF.Max( 1f, part.MaxHealth ), 0f, 1f );
		}
		else if ( index >= FirstSegment )
		{
			var part = parts[index];
			part.Health -= damage * ArmorMultiplier;
			cut = part.Health <= 0f;
			hurt = 1f - MathX.Clamp( part.Health / MathF.Max( 1f, part.MaxHealth ), 0f, 1f );
		}
		else
		{
			hurt = 1f - MathX.Clamp( (health.Current - damage * multiplier) / MathF.Max( 1f, health.MaxHealth ), 0f, 1f );
		}

		if ( cut )
		{
			GoneMask |= SubtreeMask( index );
			Cut( index, direction );
		}
		else Hit( index, position, direction, Rng.Float() < 0.3f, hurt );

		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * multiplier, Attacker = shooter, Position = position, Origin = origin } );
		var killed = wasAlive && IsDead;
		if ( !killed ) GlobalPosition += direction.SafeNormal() * 4f * U.In;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, eye || hole, scoreHit );
	}

	/// <summary> Is this hit point on one of the green radiation holes. </summary>
	bool NearHole( Vector3 position )
	{
		var radius = HoleRadius * U.In * Size;
		foreach ( var hole in holes )
		{
			if ( !IsInstanceValid( hole.Node ) || !Has( hole.Segment ) ) continue;
			if ( hole.Node.GlobalPosition.DistanceTo( position ) <= radius ) return true;
		}
		return false;
	}

	void Hit( int index, Vector3 position, Vector3 direction, bool chip, float hurt )
	{
		var part = parts[index];
		if ( part is null ) return;
		part.Flash = 0f;
		part.Hurt = MathF.Max( part.Hurt, hurt * 0.8f );
		gore.GooBurst( position, -direction, gooColor, 5, 220f, 0.7f );
		if ( !part.Gone )
		{
			CreatureGore.AddWound( part.Go, position, direction, rockColor, gooColor, (index == Head ? 2.5f : 2f) * Size );
			CreatureGore.Jiggle( part.Go, index == Head ? 0.6f : 0.9f );
		}
		if ( chip && index == Head ) gore.ThrowBlob( position, -direction, rockColor, gooColor, 2f * Size );
	}

	/// <summary> An eye pops or a body section comes off, taking everything behind it. </summary>
	void Cut( int index, Vector3 direction )
	{
		var part = parts[index];
		if ( part is null || part.Gone ) return;
		MarkGone( index );

		if ( index is EyeL or EyeR )
		{
			var at = part.Go.GlobalPosition;
			var outward = (at - parts[Head].Go.GlobalPosition).SafeNormal();
			gore.GooBurst( at, direction, gooColor, 12, 250f, 0.9f );
			gore.GooBurst( at, direction, gooColor.Lerp( Colors.White, 0.4f ), 8, 200f, 0.7f );
			CreatureGore.AddStump( parts[Head].Go, at, outward, 2f, rockColor, gooColor, 4f );
			if ( index == EyeL && eyeMeshL is not null && IsInstanceValid( eyeMeshL ) ) eyeMeshL.QueueFree();
			if ( index == EyeR && eyeMeshR is not null && IsInstanceValid( eyeMeshR ) ) eyeMeshR.QueueFree();
			part.Go.QueueFree();
			return;
		}

		var radius = index == TailIndex ? TailRadius : SegmentRadius( Math.Max( 0, index - FirstSegment ) );
		var at2 = part.Go.GlobalPosition;
		var parent = part.Parent >= 0 ? parts[part.Parent] : null;
		var outward2 = parent is not null ? (at2 - parent.Go.GlobalPosition).SafeNormal() : direction.SafeNormal();
		if ( parent is not null && !parent.Gone ) CreatureGore.AddStump( parent.Go, at2, outward2, radius * 0.55f, rockColor, gooColor, 6f );
		gore.GooBurst( at2, direction, gooColor, 14, 270f, 1.1f );
		gore.GooSpray( at2, direction, 4, gooColor, 220f, 0.5f, 1.3f );
		var piece = Detach( part, direction * Rng.Float( 80f, 160f ) + Vector3.Up * 120f, 16f, radius * 0.7f, SegmentHealth, 3f );
		CreatureGore.Twitch( piece, Rng.Float( 2.5f, 4.5f ), 0.9f );
	}

	// ---- Death ----

	protected override void OnDied()
	{
		timeSinceDeath = 0f;
		RaiseKilled();
		DieLocal( lastShotDirection, lastShotPointBlank );
	}

	/// <summary> The plates split and every section bursts loose, green goo everywhere. </summary>
	void DieLocal( Vector3 direction, bool overkill )
	{
		if ( diedLocally ) return;
		diedLocally = true;
		timeSinceDeath = 0f;
		if ( rig is null || !IsInstanceValid( rig ) || parts is null || parts[Head] is null ) return;
		var at = parts[Head] is { } head ? head.Go.GlobalPosition : GlobalPosition;
		if ( overkill ) CreatureGore.Overkill( at, direction, rockColor, gooColor, Size );
		for ( var k = 0; k < 2; k++ ) gore.GooBurst( at, direction + Vector3.Up, gooColor, 12, 320f, 1.3f );
		gore.GooSpray( at, direction, 6, gooColor, 240f, 0.7f, 1.5f );

		foreach ( var index in new[] { EyeL, EyeR } )
		{
			if ( !Has( index ) ) continue;
			MarkGone( index );
			parts[index].Go.QueueFree();
			if ( index == EyeL && eyeMeshL is not null && IsInstanceValid( eyeMeshL ) ) eyeMeshL.QueueFree();
			if ( index == EyeR && eyeMeshR is not null && IsInstanceValid( eyeMeshR ) ) eyeMeshR.QueueFree();
		}
		for ( var i = SegmentCount - 1; i >= 0; i-- )
		{
			var index = FirstSegment + i;
			if ( !Has( index ) ) continue;
			var radius = SegmentRadius( i );
			MarkGone( index );
			Detach( parts[index], (direction + Rng.OnSphere() + Vector3.Up).SafeNormal() * Rng.Float( 120f, 260f ), 20f, radius * 0.7f, SegmentHealth, 3f );
		}
		if ( Has( TailIndex ) )
		{
			MarkGone( TailIndex );
			Detach( parts[TailIndex], (direction + Rng.OnSphere() + Vector3.Up).SafeNormal() * Rng.Float( 120f, 240f ), 20f, TailRadius * 0.7f, SegmentHealth, 2.5f );
		}
		if ( parts[Head] is { Gone: false } )
		{
			MarkGone( Head );
			Detach( parts[Head], direction * 60f, 10f, HeadRadius * 0.8f, 60f, 6f );
		}
		rig.QueueFree();
	}
}