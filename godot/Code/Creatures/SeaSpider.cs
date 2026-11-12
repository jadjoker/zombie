using System.Linq;
using Godot;

/// <summary>
/// Port of the s&box SeaSpider: a tall pale six-legged stalker with one big eye and an angler-fish lure. Legs walk by
/// IK on real foot placement (tripod gait, weight shift, toes digging in), the eye glances, blinks and narrows when it
/// attacks, the lure is a springy verlet chain with a glowing bulb. Enemy: dashes in bursts and stabs with the front
/// legs; legs and eye come off (thigh 22, shin 16, eye 35 health). Ally: follows its owner on a loose leash, fights
/// what you point it at (Z), holds a spot, climbs walls toward you when you swim above, spits harpoon strands at
/// things above it, charges your jetpack and flicks you back up. Numbers are the s&box ones (inches).
/// </summary>
public partial class SeaSpider : PartCreature
{
	public float MoveSpeed = 230f;
	public float AttackRange = 95f;
	public float StabDamage = 12f;
	public float AttackCooldown = 0.9f;
	public float ThighHealth = 22f;
	public float ShinHealth = 16f;
	public float EyeHealth = 35f;
	public float EyeMultiplier = 1.6f;
	public float LimbMultiplier = 0.12f;
	public float HarpoonRange = 650f;
	public float HarpoonCooldown = 1.4f;
	public float HarpoonDamage = 0.85f;

	public float Hue = -1f;
	public float Size = 1f;
	public Vector3 NetVelocity;   // m/s
	public bool Stabbing;

	public const int ModeFollow = 0, ModeHold = 1, ModeAttack = 2;
	public bool IsAlly;
	public PlayerController OwnerPlayer;
	public int AllyMode;
	public Vector3 HoldPoint;
	public Node3D AttackTarget;

	public static readonly List<SeaSpider> All = new();
	public Vector3 LureTip => lureBulb is not null && IsInstanceValid( lureBulb ) ? lureBulb.GlobalPosition : GlobalPosition + Vector3.Up * 60f * Size * U.In;

	const int Body = 0, EyePart = 1, FirstThigh = 2, LegCount = 6, FirstShin = FirstThigh + LegCount;
	const float BodyHeight = 43f;
	const float ThighLength = 22f, ShinLength = 58f;
	static readonly float[] LegYaws = { 48f, 92f, 132f, -48f, -92f, -132f };

	public SeaSpider() => parts = new Part[FirstShin + LegCount];
	NavAgent agent;
	Color shellColor, darkColor, gooColor, glowColor;
	TimeSince timeSinceAttack = 999f;
	TimeSince timeSinceCharging = 999f;
	TimeSince timeSinceRepath = 999f;
	TimeSince timeSinceStab = 999f;
	Vector3 wanderGoal;
	TimeSince bleedTimer;
	float gait;

	const float EyeRadius = 8.5f;
	const float LidRadius = EyeRadius * 1.07f;
	Node3D eyeBall, eyeGaze, eyePupil, upperLid, lowerLid;
	Quaternion gazeRot = Quaternion.Identity;
	Vector3 wander = Vector3.Forward;
	float lidUpper = 0.15f, lidLower = 0.05f, lidSlant, pupilSize = 1f;
	TimeUntil nextBlink = 2f;
	TimeUntil nextGlance = 1f;
	float blink = 1f;

	float S => Size * U.In;   // s&box units at this spider's size, in metres

	public static SeaSpider Spawn( Node parent, Vector3 position, float maxHealth, bool ally = false, PlayerController owner = null )
	{
		var spider = new SeaSpider { Name = ally ? "Ally Spider" : "Sea Spider", IsAlly = ally, OwnerPlayer = owner };
		if ( ally ) { spider.Hue = 0.5f; spider.Size = 1.15f; spider.MoveSpeed = 175f; }
		return Place( spider, parent, position, maxHealth );
	}

	public static SeaSpider SpawnAlly( PlayerController owner )
	{
		var right = owner.EyeAngles.WithPitch( 0f ).Right;
		var at = Nav.Closest( owner, owner.GlobalPosition + right * 110f * U.In );
		var spider = Spawn( owner.GetTree().CurrentScene, at, 100000f, ally: true, owner: owner );
		spider.GlobalBasis = new Angles( 0f, owner.EyeAngles.Yaw, 0f ).ToBasis();
		return spider;
	}

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }
	protected override string Kind => "Spider";
	protected override uint PartLayer => IsAlly ? 0u : Layers.Creature;
	public override bool IsEnemy => !IsAlly;
	protected override Color GibGoo => gooColor;

	protected override void Setup()
	{
		BeginSetup( 110f );
		agent = new NavAgent( this ) { Acceleration = (IsAlly ? 900f : 600f) * U.In };
		if ( Hue < 0f ) Hue = Rng.Float();
		var hue = Hue;
		shellColor = Shapes.Hsv( MathX.LerpUnclamped( 0.92f, 1.04f, hue ) % 1f, 0.17f, 0.95f );
		darkColor = shellColor.Lerp( new Color( 0.25f, 0.08f, 0.22f ), 0.45f );
		gooColor = Shapes.Hsv( 0.88f + hue * 0.06f, 0.95f, 1f );
		glowColor = Shapes.Hsv( 0.08f, 0.85f, 1f );
		if ( IsAlly )
		{
			glowColor = Shapes.Hsv( 0.47f, 0.85f, 1f );
			shellColor = Shapes.Hsv( 0.47f, 0.42f, 0.7f );   // pale sea-glass teal (near white blew out under the tonemapper)
			darkColor = shellColor.Lerp( new Color( 0.05f, 0.35f, 0.4f ), 0.6f );
			gooColor = Shapes.Hsv( 0.5f, 0.8f, 1f );
		}
		// Stand on the ground (spawns can come in from a point in the air).
		var down = Trace.Ray( this, GlobalPosition + Vector3.Up * 40f * U.In, GlobalPosition + Vector3.Down * 4000f * U.In, Layers.World );
		if ( down.Hit ) GlobalPosition = down.HitPosition;
		agent?.Snap();
		Build();
		// Built after this frame's update: pose it now, or its parts draw at full mesh size for a frame.
		_Process( 0.0 );
	}

	// ---- Building ----

	void Build()
	{
		rig = new Node3D { Name = "Spider Rig" };
		GetTree().CurrentScene.AddChild( rig );
		rig.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized().Scaled( Vector3.One * Size ), GlobalPosition );

		var body = AddPart( Body, -1, rig, "Body", new Vector3( 0, 0, BodyHeight ), Basis.Identity, 0f );
		Sculpt( body, "body", shellColor );
		Trigger( body, new Vector3( 0, 0, -1f ), new Vector3( 0, 0, 1f ), 16f );

		var eye = AddPart( EyePart, Body, body.Go, "Eye", new Vector3( 12f, 0, 4.5f ), Basis.Identity, EyeHealth );
		BuildEye( eye );
		Trigger( eye, new Vector3( 1f, 0, 0 ), new Vector3( 2f, 0, 0 ), EyeRadius + 0.5f );

		for ( var i = 0; i < LegCount; i++ )
		{
			var outwardS = new Vector3( MathF.Cos( MathX.Rad( LegYaws[i] ) ), MathF.Sin( MathX.Rad( LegYaws[i] ) ), 0f );   // s&box local
			var hip = outwardS * 12f + new Vector3( 0, 0, 2f );
			var outward = U.Sbox( outwardS ).Normalized();
			var thighDir = (outward * 0.62f + Vector3.Up * 0.78f).Normalized();
			var shinDir = (outward * 0.42f + Vector3.Down * 0.9f).Normalized();
			var thighRot = Shapes.LookAt( thighDir );
			var shinRot = Shapes.LookAt( shinDir );
			var thigh = AddPart( FirstThigh + i, Body, body.Go, $"Thigh {i}", hip, thighRot, ThighHealth );
			Sculpt( thigh, "thigh", shellColor );
			Trigger( thigh, Vector3.Zero, new Vector3( ThighLength, 0, 0 ), 4f );
			var shin = AddPart( FirstShin + i, FirstThigh + i, thigh.Go, $"Shin {i}", new Vector3( ThighLength, 0, 0 ), thighRot.Inverse() * shinRot, ShinHealth );
			Sculpt( shin, "shin", darkColor );
			Trigger( shin, Vector3.Zero, new Vector3( ShinLength, 0, 0 ), 3.4f );
		}
		BuildLure();
	}

	static readonly Dictionary<string, PackedScene> sculpts = new();

	void Sculpt( Part part, string model, Color color )
	{
		if ( !sculpts.TryGetValue( model, out var scene ) )
			sculpts[model] = scene = GD.Load<PackedScene>( $"res://assets/models/creatures/spider/{model}.fbx" );
		var node = scene.Instantiate<Node3D>();
		// The spider sculpts import with their length along +X; turn them so it runs along the part's forward (-Z).
		node.Basis = new Basis( Vector3.Up, MathF.PI * 0.5f ) * Basis.FromScale( Vector3.One * 2.54f );
		part.Go.AddChild( node );
		foreach ( var mesh in node.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			mesh.MaterialOverride = Shapes.Material( color, "chitin" );   // a textured shell like the crab (it was smooth plastic)
			part.Renderers.Add( (mesh, color) );
		}
	}

	MeshInstance3D Sphere( Node3D parent, Part part, Vector3 at, Vector3 radii, Color color, bool skin )
	{
		var mesh = Shapes.Part( parent, "Shape", at, Shapes.Ellipsoid( radii.X, radii.Y, radii.Z ), color, skin: skin ? "smooth" : null );
		part.Renderers.Add( (mesh, color) );
		return mesh;
	}

	// ---- The lure ----

	const int LureNodes = 8;
	const float LureSegment = 7.2f;
	readonly Vector3[] lureP = new Vector3[LureNodes];
	readonly Vector3[] lureOld = new Vector3[LureNodes];
	readonly Vector3[] lureRest = new Vector3[LureNodes];
	readonly MeshInstance3D[] lureSeg = new MeshInstance3D[LureNodes - 1];
	readonly MeshInstance3D[] lureJoint = new MeshInstance3D[LureNodes - 1];
	MeshInstance3D lureBulb;
	OmniLight3D lureLight;
	StandardMaterial3D lureBulbMaterial;
	bool lureReady;
	Vector3 lureKick, lureAnchor;

	void BuildLure()
	{
		var stalk = shellColor.Lerp( darkColor, 0.45f );
		var stalkMaterial = Shapes.Material( stalk, "smooth" );
		for ( var i = 0; i < LureNodes - 1; i++ )
		{
			lureSeg[i] = new MeshInstance3D { Name = "Lure Segment", Mesh = Shapes.Spindle( 0.78f, 0f ), MaterialOverride = stalkMaterial, TopLevel = true };
			rig.AddChild( lureSeg[i] );
			lureJoint[i] = new MeshInstance3D { Name = "Lure Joint", Mesh = Shapes.Sphere, MaterialOverride = stalkMaterial, TopLevel = true };
			rig.AddChild( lureJoint[i] );
		}
		lureBulbMaterial = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 3f };
		lureBulb = new MeshInstance3D { Name = "Lure Bulb", Mesh = Shapes.Sphere, MaterialOverride = lureBulbMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true };
		rig.AddChild( lureBulb );
		lureLight = new OmniLight3D { LightColor = glowColor, LightEnergy = 2.5f, OmniRange = 380f * U.In, OmniAttenuation = 1.1f, ShadowEnabled = false };
		lureBulb.AddChild( lureLight );
		if ( IsAlly ) { lureLight.OmniRange = 220f * U.In; lureLight.LightColor = glowColor.Lerp( Colors.White, 0.35f ); }
		// Top-level pieces start at the spider (not the map origin) until the first lure update.
		foreach ( var m in lureSeg.Concat( lureJoint ).Append( lureBulb ) )
			if ( m is not null ) m.GlobalTransform = new Transform3D( Basis.FromScale( Vector3.One * 0.001f ), rig.GlobalPosition );
	}

	void ComputeLureRest()
	{
		var body = parts[Body].Go;
		var local = new Vector3( 3f, 0f, 14.5f );
		var basis = body.GlobalBasis.Orthonormalized();
		for ( var i = 0; i < LureNodes; i++ )
		{
			if ( i > 0 )
			{
				var t = (i - 0.5f) / (LureNodes - 1);
				var theta = MathX.Rad( 6f + 76f * t );
				local += new Vector3( MathF.Sin( theta ), 0f, MathF.Cos( theta ) ) * LureSegment;
			}
			lureRest[i] = body.GlobalPosition + basis * (U.Sbox( local ) * Size);
		}
	}

	void UpdateLure( float frameDt )
	{
		if ( lureBulb is null || !IsInstanceValid( lureBulb ) || !Has( Body ) ) return;
		var dt = MathF.Min( frameDt, 1f / 30f );
		if ( dt <= 0f ) return;
		ComputeLureRest();
		if ( !lureReady || (lureP[1] - lureRest[1]).Length() > 220f * S )
		{
			for ( var i = 0; i < LureNodes; i++ ) { lureP[i] = lureRest[i]; lureOld[i] = lureRest[i]; }
			lureReady = true;
			lureAnchor = lureRest[0];
		}
		var body = parts[Body].Go;
		var basis = body.GlobalBasis.Orthonormalized();
		var segment = LureSegment * S;
		var anchorMove = lureRest[0] - lureAnchor;
		lureAnchor = lureRest[0];
		if ( anchorMove.Length() > 60f * U.In ) anchorMove = Vector3.Zero;
		lureP[0] = lureRest[0];
		lureOld[0] = lureRest[0];
		var now = (float)Clock.Now;
		for ( var i = 1; i < LureNodes; i++ )
		{
			var along = i / (float)(LureNodes - 1);
			var velocity = (lureP[i] - lureOld[i]) * 0.94f - anchorMove * (0.16f * along);
			lureOld[i] = lureP[i];
			var wobble = basis * U.Sbox( 0f, MathF.Sin( now * 2.3f + i * 0.7f ), MathF.Sin( now * 1.7f + i * 1.1f ) * 0.6f ) * (0.9f * i * Size);
			var target = lureRest[i] + wobble;
			var stiffness = 4.2f * (1f - 0.5f * along);
			var pull = (target - lureP[i]) * (1f - MathF.Exp( -stiffness * dt ));
			lureP[i] += velocity + pull + Vector3.Down * 50f * U.In * dt * dt + lureKick * dt * along;
		}
		lureKick = Vector3.Zero;
		for ( var iteration = 0; iteration < 5; iteration++ )
		{
			for ( var i = 1; i < LureNodes; i++ )
			{
				var d = lureP[i] - lureP[i - 1];
				var length = d.Length();
				if ( length < 1e-6f ) continue;
				var correction = d * ((length - segment) / length);
				if ( i - 1 == 0 ) lureP[i] -= correction;
				else { lureP[i] -= correction * 0.5f; lureP[i - 1] += correction * 0.5f; }
			}
		}
		for ( var i = 0; i < LureNodes - 1; i++ )
		{
			var a = lureP[i];
			var d = lureP[i + 1] - a;
			var length = d.Length();
			if ( length < 1e-5f ) continue;
			var radius = MathX.Lerp( 1.7f, 0.8f, i / (float)(LureNodes - 2) ) * S;
			lureSeg[i].GlobalTransform = new Transform3D( Shapes.LookAt( d ) * Basis.FromScale( new Vector3( radius, radius, length ) ), a );
			lureJoint[i].GlobalTransform = new Transform3D( Basis.FromScale( Vector3.One * radius * 1.05f * 2f ), a );
		}
		var tip = lureP[LureNodes - 1];
		var glow = 1f + 0.22f * MathF.Sin( now * 2.6f ) + 0.08f * MathF.Sin( now * 9.1f );
		if ( Stabbing || timeSinceStab < 0.5f ) glow *= 1.7f;
		if ( IsAlly && timeSinceCharging < 0.3f ) glow *= 2f;
		lureBulb.GlobalTransform = new Transform3D( Basis.FromScale( Vector3.One * 4.4f * 2f * S ), tip );
		lureBulbMaterial.EmissionEnergyMultiplier = 1.7f * glow * 1.6f;
		// The ally's lure is a soft companion glow: at full strength it flooded small rooms (the dock hut) green.
		lureLight.LightEnergy = (IsAlly ? 0.8f : 2.6f) * glow;
	}

	// ---- The eye ----

	void BuildEye( Part part )
	{
		eyeBall = new Node3D { Name = "Eyeball" };
		part.Go.AddChild( eyeBall );
		Sphere( eyeBall, part, Vector3.Zero, new Vector3( EyeRadius, EyeRadius, EyeRadius ), new Color( 0.98f, 0.96f, 0.84f ), skin: false );
		eyeGaze = new Node3D { Name = "Gaze" };
		eyeBall.AddChild( eyeGaze );
		eyePupil = new Node3D { Name = "Pupil" };
		eyeGaze.AddChild( eyePupil );
		var k = EyeRadius / 3.4f;
		Sphere( eyePupil, part, new Vector3( EyeRadius * 0.9f, 0, 0 ), new Vector3( 0.42f, 1.55f, 1.75f ) * k, new Color( 0.02f, 0.02f, 0.03f ), skin: false );
		Sphere( eyePupil, part, new Vector3( EyeRadius * 0.98f, 1.0f, 1.4f ), new Vector3( 0.2f, 0.42f, 0.42f ) * k, Colors.White, skin: false );
		Sphere( eyePupil, part, new Vector3( EyeRadius * 0.98f, -0.9f, -1.2f ), new Vector3( 0.15f, 0.2f, 0.2f ) * k, new Color( 0.85f, 0.88f, 0.95f ), skin: false );
		upperLid = Lid( part, "Upper Lid", shellColor );
		lowerLid = Lid( part, "Lower Lid", darkColor );
	}

	static readonly SphereMesh dome = new() { Radius = 1f, Height = 1f, IsHemisphere = true, RadialSegments = 28, Rings = 14 };

	Node3D Lid( Part part, string name, Color color )
	{
		var go = new Node3D { Name = name, Scale = Vector3.One * LidRadius * U.In };
		eyeBall.AddChild( go );
		var mesh = new MeshInstance3D { Mesh = dome, MaterialOverride = Shapes.Material( color, "smooth" ) };
		go.AddChild( mesh );
		part.Renderers.Add( (mesh, color) );
		return go;
	}

	void UpdateEye( float dt )
	{
		if ( !Has( EyePart ) || eyeBall is null || !IsInstanceValid( eyeBall ) ) return;
		if ( nextBlink <= 0f )
		{
			blink = 0f;
			nextBlink = Rng.Float() < 0.2f ? 0.25f : Rng.Float( 2.5f, 6f );
		}
		blink = MathF.Min( 1f, blink + dt / 0.18f );
		var closing = 1f - MathF.Abs( blink * 2f - 1f );
		var target = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var angry = Stabbing || timeSinceStab < 0.6f;
		var shocked = parts.Any( p => p is not null && !p.Gone && p.Flash < 0.5f );
		if ( nextGlance <= 0f )
		{
			nextGlance = Rng.Float( 0.6f, 1.8f );
			wander = U.Sbox( 1f, Rng.Float( -0.6f, 0.6f ), Rng.Float( -0.3f, 0.4f ) ).Normalized();
		}
		var look = wander;
		if ( target is not null && target.WorldPosition.DistanceTo( GlobalPosition ) < 1600f * U.In )
			look = eyeBall.GlobalBasis.Orthonormalized().Inverse() * ((target.WorldPosition + Vector3.Up * 60f * U.In) - eyeBall.GlobalPosition).SafeNormal();
		var limit = MathF.Cos( MathX.Rad( 48f ) );
		if ( -look.Z < limit )
		{
			var sideways = look with { Z = 0f };
			sideways = sideways.IsNearlyZero() ? Vector3.Up : sideways.Normalized();
			look = (Vector3.Forward * limit + sideways * MathF.Sqrt( 1f - limit * limit )).Normalized();
		}
		gazeRot = gazeRot.SafeSlerp( Shapes.LookAt( look ).Orthonormalized().GetRotationQuaternion(), MathX.Clamp( dt * 14f, 0f, 1f ) );
		eyeGaze.Quaternion = gazeRot;
		var now = (float)Clock.Now;
		var upper = shocked ? 0.02f : angry ? 0.4f : 0.15f + 0.04f * MathF.Sin( now * 0.7f );
		var lower = shocked ? 0f : angry ? 0.18f : 0.05f;
		var slant = angry ? 24f : shocked ? -6f : 4f;
		var pupil = shocked ? 0.6f : angry ? 0.8f : 1f;
		var k = MathX.Clamp( dt * 12f, 0f, 1f );
		lidUpper = MathX.Lerp( lidUpper, upper, k );
		lidLower = MathX.Lerp( lidLower, lower, k );
		lidSlant = MathX.Lerp( lidSlant, slant, k );
		pupilSize = MathX.Lerp( pupilSize, pupil, k );
		var up = MathX.Lerp( lidUpper, 0.56f, closing );
		var down = MathX.Lerp( lidLower, 0.5f, closing );
		var lidScale = Basis.FromScale( Vector3.One * LidRadius * U.In );
		upperLid.Basis = new Angles( 0f, 0f, lidSlant ).ToBasis() * new Angles( -LidEdge( up ), 0f, 0f ).ToBasis() * lidScale;
		lowerLid.Basis = new Angles( LidEdge( down ), 0f, 0f ).ToBasis() * new Angles( 0f, 0f, 180f ).ToBasis() * lidScale;
		eyePupil.Scale = new Vector3( pupilSize, pupilSize, 1f );
	}

	// ---- Every frame ----

	float LegsLost => Enumerable.Range( 0, LegCount ).Sum( i => !Has( FirstThigh + i ) ? 1f : !Has( FirstShin + i ) ? 0.5f : 0f );
	bool Crawling => LegsLost >= 3.5f;
	bool Blind => !Has( EyePart );

	public override void _Process( double delta )
	{
		if ( health is null || rig is null ) return;
		var dt = (float)delta;
		if ( IsDead )
		{
			if ( timeSinceDeath > 1f ) QueueFree();
			return;
		}
		if ( !DevFlags.FreezeEnemies ) Think( dt );
		if ( diedLocally || !IsInstanceValid( rig ) ) return;
		rig.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized().Scaled( Vector3.One * Size ), GlobalPosition );
		if ( Has( Body ) ) CreatureGore.BleedTrail( parts[Body].Go.GlobalPosition, gooColor, health.Fraction, ref bleedTimer );
		Animate( dt );
		Tint( dt );
	}

	sealed class Foot
	{
		public Vector3 Pos, From, To;
		public float T = -1f;
		public float Duration = 0.3f;
		public float Lift = 16f;
		public float Plant;
		public TimeUntil NextProbe = 1f;
	}

	readonly Foot[] feet = new Foot[LegCount];
	float bodyZ = float.NaN;
	Vector2 tilt;
	public int StepCount { get; private set; }
	Vector3 supportShift, hitKick, lastVelocity;
	float hitFlinch, lookYaw, pitchLean, lungeAmount, moveAmount;
	TimeUntil burstUntil;
	bool dashing;
	const float RestReach = 52f;

	Basis RigRot => rig.GlobalBasis.Orthonormalized();
	Vector3 SurfUp => rig is not null && IsInstanceValid( rig ) ? RigRot.Y : Vector3.Up;

	float GroundY( Vector3 at )
	{
		var tr = Trace.Ray( this, at + Vector3.Up * 90f * U.In, at + Vector3.Down * 160f * U.In, Layers.World );
		return tr.Hit ? tr.HitPosition.Y : GlobalPosition.Y;
	}

	Vector3 Flat( Vector3 v ) { var up = SurfUp; return v - up * v.Dot( up ); }

	Vector3 Grounded( Vector3 at )
	{
		var up = SurfUp;
		if ( up.Y > 0.98f ) return at.WithY( GroundY( at ) );
		var tr = Trace.Ray( this, at + up * 90f * U.In, at - up * 160f * U.In, Layers.World );
		return tr.Hit ? tr.HitPosition : at - up * (at - rig.GlobalPosition).Dot( up );
	}

	Vector3 Outward( int i ) => new Angles( 0f, LegYaws[i], 0f ).Forward;

	Vector3 DesiredFoot( int i, Vector3 velocity, float leadSeconds = 0.1f )
	{
		var reach = (Crawling ? RestReach * 0.7f : RestReach) * S;
		var lead = (Flat( velocity ) * leadSeconds).ClampLength( 95f * S );
		return Grounded( rig.GlobalPosition + RigRot * (Outward( i ) * reach) + lead );
	}

	void Animate( float dt )
	{
		var velocity = Flat( NetVelocity );
		var speed = velocity.Length() / U.In;
		gait += dt * MathX.Clamp( speed / 60f, 0f, 5f );
		var moving = MathX.Clamp( speed / 60f, 0f, 1f );
		AnimateLegs( velocity, moving, dt );
		Beacon();
		UpdateEye( dt );
		UpdateLure( dt );
	}

	TimeSince timeSinceBeacon;

	void Beacon()
	{
		if ( !IsAlly || timeSinceBeacon < 1.6f || OwnerPlayer is null ) return;
		var above = (OwnerPlayer.GlobalPosition.Y - GlobalPosition.Y) / U.In;
		if ( above < 260f ) return;
		timeSinceBeacon = 0f;
		var tip = LureTip;
		SpiderStrand.Create( tip, tip + Vector3.Up * MathF.Min( above, 600f ) * U.In, glowColor, 0.9f, 0.45f );
	}

	void AnimateLegs( Vector3 velocity, float moving, float dt )
	{
		var stab = Stabbing || timeSinceStab < 0.3f;
		var rot = RigRot;
		var forward = -rot.Z;
		for ( var i = 0; i < LegCount; i++ )
		{
			if ( feet[i] is not null ) continue;
			feet[i] = new Foot { Pos = DesiredFoot( i, Vector3.Zero ) };
			feet[i].NextProbe = Rng.Float( 1f, 3f );
		}

		moveAmount = moving;
		var sink = Crawling ? 20f : LegsLost * 3f;
		var grounded = 0;
		float avg = 0f, front = 0f, back = 0f, left = 0f, right = 0f;
		var support = Vector2.Zero;
		var supportCount = 0;
		for ( var i = 0; i < LegCount; i++ )
		{
			if ( !Has( FirstThigh + i ) ) continue;
			var z = (feet[i].Pos - rig.GlobalPosition).Dot( SurfUp ) / U.In;
			grounded++;
			avg += z;
			if ( i % 3 == 0 ) front += z;
			else if ( i % 3 == 2 ) back += z;
			if ( i < 3 ) left += z; else right += z;
			if ( feet[i].T < 0f && !(stab && (i == 0 || i == 3)) )
			{
				var lf = rot.Inverse() * (feet[i].Pos - rig.GlobalPosition) / U.In;
				support += new Vector2( -lf.Z, -lf.X );   // s&box local x (forward), y (left)
				supportCount++;
			}
		}
		var targetZ = grounded > 0 ? avg / grounded : 0f;
		var supportTarget = (supportCount > 0 ? support / supportCount : Vector2.Zero) * 0.4f;
		supportTarget = supportTarget.LimitLength( 7f * Size );
		supportShift = supportShift.Lerp( new Vector3( supportTarget.X, supportTarget.Y, 0f ), MathX.Clamp( dt * 10f, 0f, 1f ) );

		var accel = (velocity - lastVelocity) / MathF.Max( dt, 0.001f );
		lastVelocity = velocity;
		var accelForward = (rot.Inverse() * accel).Dot( Vector3.Forward ) / U.In;
		var pitchTarget = moving * 5f + MathX.Clamp( accelForward * 0.01f, -9f, 9f );

		var player = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var yawTarget = 0f;
		if ( player is not null && !Blind )
		{
			var lt = rot.Inverse() * (player.WorldPosition - rig.GlobalPosition);
			yawTarget = MathX.Clamp( MathF.Atan2( -lt.X, -lt.Z ) * 57.3f, -32f, 32f ) * (1f - moving * 0.7f);
		}
		lookYaw = MathX.Lerp( lookYaw, yawTarget, MathX.Clamp( dt * 5f, 0f, 1f ) );

		var now = (float)Clock.Now;
		float rear = 0f, lungeTarget = 0f;
		if ( stab )
		{
			var down = MathF.Sin( now * 18f ) > 0f;
			rear = down ? 4f : -10f;
			lungeTarget = down ? 6f : -1f;
		}
		pitchLean = MathX.Lerp( pitchLean, pitchTarget + rear, MathX.Clamp( dt * 12f, 0f, 1f ) );
		lungeAmount = MathX.Lerp( lungeAmount, lungeTarget, MathX.Clamp( dt * 14f, 0f, 1f ) );
		var breathe = MathF.Sin( now * 1.6f ) * 0.9f * (1f - moving);
		var crouch = moving * 4f + (stab ? -4f : 0f);
		var wantedBodyZ = targetZ + (BodyHeight - sink - crouch) * Size + MathF.Sin( gait * 2f ) * 1.2f * moving + breathe - hitFlinch * 4f * Size;
		bodyZ = float.IsNaN( bodyZ ) ? wantedBodyZ : MathX.Lerp( bodyZ, wantedBodyZ, MathX.Clamp( dt * 9f, 0f, 1f ) );
		var span = MathF.Max( 1, grounded / 3 );
		var pitch = MathX.Deg( MathF.Atan2( back - front, 100f * Size * span ) );
		var roll = MathX.Deg( MathF.Atan2( left - right, 90f * Size * span ) );
		tilt = tilt.Lerp( new Vector2( MathX.Clamp( pitch, -16f, 16f ), MathX.Clamp( roll, -16f, 16f ) ), MathX.Clamp( dt * 6f, 0f, 1f ) );
		hitKick = hitKick.Lerp( Vector3.Zero, MathX.Clamp( dt * 7f, 0f, 1f ) );
		hitFlinch = MathX.Lerp( hitFlinch, 0f, MathX.Clamp( dt * 6f, 0f, 1f ) );

		var body = parts[Body].Go;
		var offset = rot * U.Sbox( supportShift + new Vector3( lungeAmount, 0f, 0f ) ) * Size + hitKick;
		body.GlobalPosition = rig.GlobalPosition + SurfUp * bodyZ * U.In + offset;
		body.Basis = new Angles( tilt.X + pitchLean + (Crawling ? 8f : 0f), lookYaw, -tilt.Y ).ToBasis();

		// Which feet want to move, most stretched first. Opposite tripods never step together, at most three at once.
		var stepping = feet.Count( f => f.T >= 0f );
		var order = Enumerable.Range( 0, LegCount )
			.Where( i => Has( FirstThigh + i ) && feet[i].T < 0f )
			.OrderByDescending( i => Flat( feet[i].Pos - DesiredFoot( i, velocity ) ).Length() )
			.ToList();
		var speedIn = velocity.Length() / U.In;
		foreach ( var i in order )
		{
			if ( stab && (i == 0 || i == 3) ) continue;
			var foot = feet[i];
			var desired = DesiredFoot( i, velocity );
			var stretch = Flat( foot.Pos - desired ).Length() / U.In;
			var threshold = MathX.Lerp( 12f, 20f, moving ) * Size * (1f + MathX.Clamp( speedIn / 350f, 0f, 1f ) * 1.8f);
			var probing = moving < 0.2f && foot.NextProbe <= 0f;
			if ( stretch < threshold && !probing ) continue;
			var hipDistance = foot.Pos.DistanceTo( parts[FirstThigh + i].Go.GlobalPosition ) / U.In;
			var urgent = hipDistance > (ThighLength + ShinLength) * 0.985f * Size;
			if ( urgent && stepping >= 4 ) continue;
			if ( !urgent )
			{
				if ( stepping >= 3 ) break;
				var group = i % 2;
				var blocked = false;
				for ( var k = 0; k < LegCount; k++ )
					if ( feet[k].T >= 0f && k % 2 != group ) blocked = true;
				if ( blocked ) continue;
			}
			foot.From = foot.Pos;
			foot.Duration = MathX.Lerp( 0.4f, 0.25f, MathX.Clamp( speedIn / 350f, 0f, 1f ) ) * Rng.Float( 0.9f, 1.15f );
			StepCount++;
			foot.To = probing && stretch < threshold ? Grounded( desired + Flat( Rng.OnSphere() ) * 14f * S ) : DesiredFoot( i, velocity, foot.Duration + 0.12f );
			var mid = (foot.From + foot.To) * 0.5f;
			var rise = SurfUp.Y > 0.98f ? MathF.Max( 0f, MathF.Max( foot.To.Y, GroundY( mid ) ) - MathF.Min( foot.From.Y, foot.To.Y ) ) / U.In : 0f;
			foot.Lift = (13f + Rng.Float( 0f, 9f ) + rise + moving * 9f) * Size;
			foot.T = 0f;
			foot.NextProbe = Rng.Float( 3f, 7f );
			stepping++;
		}

		for ( var i = 0; i < LegCount; i++ )
		{
			var thigh = parts[FirstThigh + i];
			var shin = parts[FirstShin + i];
			if ( thigh is null || thigh.Gone ) continue;
			var foot = feet[i];
			Vector3 footPos;
			if ( stab && (i == 0 || i == 3) )
			{
				var side = rot.X * (i == 0 ? -1f : 1f) * 16f * S;
				var down = MathF.Sin( now * 18f + i ) > 0f;
				footPos = rig.GlobalPosition + forward * 66f * S + side + SurfUp * (down ? 6f : 58f) * S;
				foot.Pos = footPos;
				foot.T = -1f;
			}
			else if ( foot.T >= 0f )
			{
				foot.T += dt / foot.Duration;
				var t = MathX.Clamp( foot.T, 0f, 1f );
				var along = t * t * (3f - 2f * t);
				footPos = foot.From.Lerp( foot.To, along );
				footPos += SurfUp * foot.Lift * U.In * MathF.Sin( MathF.PI * MathF.Pow( t, 0.65f ) );
				if ( foot.T >= 1f )
				{
					foot.Pos = foot.To;
					foot.T = -1f;
					foot.Plant = 1f;
					footPos = foot.Pos;
				}
			}
			else footPos = foot.Pos;
			foot.Plant = MathF.Max( 0f, foot.Plant - dt * 7f );
			footPos -= SurfUp * foot.Plant * 2.5f * S;
			SolveLeg( i, thigh, shin, footPos );
		}
	}

	void SolveLeg( int i, Part thigh, Part shin, Vector3 foot )
	{
		var hip = thigh.Go.GlobalPosition;
		var a = ThighLength * S;
		var b = ShinLength * S;
		var toFoot = foot - hip;
		var distance = MathX.Clamp( toFoot.Length(), MathF.Abs( a - b ) + 0.5f * U.In, a + b - 0.5f * U.In );
		var direction = toFoot.IsNearlyZero() ? Vector3.Down : toFoot.Normalized();
		foot = hip + direction * distance;
		var outward = RigRot * Outward( i );
		var pole = outward * (0.5f + 0.5f * moveAmount) + SurfUp;
		pole -= direction * pole.Dot( direction );
		pole = pole.IsNearlyZero() ? SurfUp : pole.Normalized();
		var along = (distance * distance + a * a - b * b) / (2f * distance);
		var height = MathF.Sqrt( MathF.Max( 0f, a * a - along * along ) );
		var knee = hip + direction * along + pole * height;
		var scale = Basis.FromScale( Vector3.One * Size );
		thigh.Go.GlobalBasis = Shapes.LookAt( knee - hip, pole ) * scale;
		if ( shin is not null && !shin.Gone )
			shin.Go.GlobalBasis = Shapes.LookAt( foot - knee, pole ) * scale;
	}

	void Tint( float dt )
	{
		if ( IsAlly )
			foreach ( var player in PlayerSurvivor.All )
				if ( player.Controller.GetNodeOrNull<Jetpack>( "Jetpack" ) is { } jet && jet.DockedTo == this && jet.Power < jet.MaxPower - 0.5f ) timeSinceCharging = 0f;
		TintParts( dt );
	}

	// ---- Enemy brain ----

	void Think( float dt )
	{
		if ( IsAlly ) { ThinkAlly( dt ); agent.Update( dt ); return; }
		var target = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var speedScale = (1f - 0.1f * LegsLost) * (Crawling ? 0.4f : 1f) * (Blind ? 0.65f : 1f);
		if ( target is not null && !Crawling && (target.WorldPosition - GlobalPosition).Flat().Length() > 260f * S ) speedScale *= BurstScale();
		else dashing = false;
		agent.MaxSpeed = MoveSpeed * MathF.Max( 0.15f, speedScale ) * S;
		Stabbing = false;
		if ( target is not null )
		{
			var toTarget = (target.WorldPosition - GlobalPosition).Flat();
			var distance = toTarget.Length();
			if ( distance <= AttackRange * S && !Blind )
			{
				agent.Stop();
				Face( toTarget, 10f, dt );
				Stabbing = true;
				if ( timeSinceAttack >= AttackCooldown ) { timeSinceAttack = 0f; timeSinceStab = 0f; Stab( target ); }
			}
			else if ( timeSinceRepath > 0.3f )
			{
				timeSinceRepath = 0f;
				if ( Blind )
				{
					if ( wanderGoal == Vector3.Zero || GlobalPosition.DistanceTo( wanderGoal ) < 60f * U.In || Rng.Float() < 0.15f )
						wanderGoal = target.WorldPosition + Rng.OnSphere().Flat().SafeNormal() * 350f * U.In;
					agent.MoveTo( wanderGoal );
					if ( distance <= AttackRange * S && timeSinceAttack > AttackCooldown * 2f ) { timeSinceAttack = 0f; timeSinceStab = 0f; Stab( target ); }
				}
				else agent.MoveTo( target.WorldPosition );
			}
			var velocity = agent.Velocity.Flat();
			if ( !Blind ) Face( toTarget, 8f, dt );
			else if ( velocity.Length() > 10f * U.In ) Face( velocity, 6f, dt );
		}
		else agent.Stop();
		agent.Update( dt );
		NetVelocity = agent.Velocity;
	}

	// ---- Ally brain ----

	Node3D currentEnemy;
	bool catchingUp;
	TimeUntil nextScan;
	TimeUntil nextMove;
	TimeSince timeSinceHarpoon = 999f;
	TimeUntil nextHarpoonScan;

	public void Command( int mode, Vector3 point, Node3D target )
	{
		AllyMode = mode;
		HoldPoint = point;
		AttackTarget = target;
		currentEnemy = null;
		nextScan = 0f;
	}

	static bool EnemyDead( Node3D c ) => c is null || !IsInstanceValid( c ) || c switch
	{
		SeaSpider s => s.IsDead || s.IsAlly,
		ICreature x => x.IsDead,
		_ => true,
	};

	static IEnumerable<Node3D> Enemies() => CreatureRegistry.Alive();

	Node3D FindEnemy( Vector3 around, float range, Vector3? alsoNear = null, float nearRange = 0f, bool flat = true )
	{
		Node3D best = null;
		var bestDistance = range * U.In;
		foreach ( var c in Enemies() )
		{
			if ( EnemyDead( c ) ) continue;
			var d = flat ? (c.GlobalPosition - around).Flat().Length() : (c.GlobalPosition - around).Length();
			if ( d >= bestDistance ) continue;
			if ( alsoNear is { } n && (c.GlobalPosition - n).Flat().Length() > nearRange * U.In ) continue;
			best = c;
			bestDistance = d;
		}
		return best;
	}

	void Harpoon( Node3D enemy, PlayerSurvivor owner )
	{
		if ( timeSinceHarpoon < HarpoonCooldown || enemy is null ) return;
		var from = LureTip;
		var to = enemy.GlobalPosition + Vector3.Up * 20f * U.In;
		if ( (to - from).Length() > HarpoonRange * U.In ) return;
		var tr = Trace.Ray( this, from, to, Layers.World );
		if ( tr.Hit && tr.Distance < (to - from).Length() - 40f * U.In ) return;
		Sfx.Play( Sfx.Pick( "harpoon" ), from, 0.7f, Rng.Float( 0.92f, 1.08f ) );
		timeSinceHarpoon = 0f;
		SpiderStrand.Create( from, to, glowColor );
		Strike( enemy, owner, HarpoonDamage );
	}

	enum ClimbPhase { None, Approach, Scale, Perch, Descend }
	ClimbPhase climbPhase;
	Vector3 climbWall, climbNormal = Vector3.Back;
	TimeSince climbTimer;
	TimeUntil nextClimbCheck;
	Vector3 climbVelocity, climbGround;
	const float ClimbSpeed = 120f;
	public bool IsClimbing => climbPhase != ClimbPhase.None;

	bool FindWall( Vector3 ownerPos, out Vector3 point, out Vector3 normal )
	{
		point = default;
		normal = Vector3.Back;
		var toOwner = (ownerPos - GlobalPosition).Flat();
		toOwner = toOwner.IsNearlyZero() ? Vector3.Forward : toOwner.Normalized();
		var best = float.MaxValue;
		var origin = GlobalPosition + Vector3.Up * 45f * U.In;
		for ( var i = 0; i < 16; i++ )
		{
			var d = new Angles( 0f, i * 22.5f, 0f ).Forward;
			var tr = Trace.Ray( this, origin, origin + d * 380f * U.In, Layers.World );
			if ( !tr.Hit || MathF.Abs( tr.Normal.Y ) > 0.5f ) continue;
			var above = Trace.Ray( this, tr.HitPosition + tr.Normal * 12f * U.In + Vector3.Up * 120f * U.In, tr.HitPosition - tr.Normal * 20f * U.In + Vector3.Up * 120f * U.In, Layers.World );
			if ( !above.Hit ) continue;
			var score = tr.Distance / (380f * U.In) - 1.5f * d.Dot( toOwner );
			if ( score >= best ) continue;
			best = score;
			point = tr.HitPosition;
			normal = tr.Normal;
		}
		return best < float.MaxValue;
	}

	void EndClimb()
	{
		climbPhase = ClimbPhase.None;
		nextClimbCheck = 1.5f;
		agent.Enabled = true;
		for ( var i = 0; i < LegCount; i++ ) feet[i] = null;
		bodyZ = float.NaN;
	}

	bool ClimbThink( PlayerSurvivor owner, Vector3 ownerPos, float dt )
	{
		climbVelocity = Vector3.Zero;
		var above = (ownerPos.Y - GlobalPosition.Y) / U.In;
		var flatDistance = (ownerPos - GlobalPosition).Flat().Length() / U.In;
		switch ( climbPhase )
		{
			case ClimbPhase.None:
			{
				if ( nextClimbCheck > 0f ) return false;
				nextClimbCheck = 0.4f;
				if ( above < 110f || flatDistance > 700f || AllyMode == ModeHold || currentEnemy is not null ) return false;
				if ( !FindWall( ownerPos, out climbWall, out climbNormal ) ) return false;
				climbPhase = ClimbPhase.Approach;
				climbTimer = 0f;
				return true;
			}
			case ClimbPhase.Approach:
			{
				var stand = (climbWall + climbNormal * 55f * U.In).WithY( GlobalPosition.Y );
				var distance = (stand - GlobalPosition).Flat().Length() / U.In;
				if ( above < 60f || climbTimer > 6f ) { EndClimb(); return false; }
				if ( distance < 70f )
				{
					climbGround = GlobalPosition;
					agent.Stop();
					agent.Enabled = false;
					for ( var i = 0; i < LegCount; i++ ) feet[i] = null;
					climbPhase = ClimbPhase.Scale;
					climbTimer = 0f;
					return true;
				}
				if ( nextMove <= 0f ) { nextMove = 0.25f; agent.MoveTo( stand ); }
				var v = agent.Velocity.Flat();
				Face( v.Length() > 10f * U.In ? v : -climbNormal, 8f, dt );
				agent.MaxSpeed = MoveSpeed * 1.15f * S;
				climbVelocity = agent.Velocity;
				return true;
			}
			default:
			{
				var n = climbNormal;
				var upDir = Vector3.Up - n * Vector3.Up.Dot( n );
				upDir = upDir.IsNearlyZero() ? Vector3.Up : upDir.Normalized();
				var direction = climbPhase == ClimbPhase.Scale ? 1f : climbPhase == ClimbPhase.Descend ? -1f : 0f;
				if ( climbPhase == ClimbPhase.Scale && (ownerPos.Y - GlobalPosition.Y) / U.In < 50f ) climbPhase = ClimbPhase.Perch;
				if ( climbPhase == ClimbPhase.Perch && (ownerPos.Y - GlobalPosition.Y) / U.In > 120f && climbTimer > 0.5f ) { climbPhase = ClimbPhase.Scale; climbTimer = 0f; }
				if ( climbPhase != ClimbPhase.Descend && (ownerPos.Y < GlobalPosition.Y - 70f * U.In || flatDistance > 900f || owner is null) ) climbPhase = ClimbPhase.Descend;
				var rate = (climbPhase == ClimbPhase.Descend ? 150f : ClimbSpeed) * U.In;
				var next = GlobalPosition + upDir * direction * rate * dt;
				var probe = upDir * 45f * U.In;
				var tr = Trace.Ray( this, next + probe + n * 90f * U.In, next + probe - n * 220f * U.In, Layers.World );
				if ( !tr.Hit || tr.StartedSolid || MathF.Abs( tr.Normal.Y ) > 0.6f )
				{
					if ( climbPhase == ClimbPhase.Scale ) { climbPhase = ClimbPhase.Perch; climbTimer = 0f; }
					else if ( climbPhase == ClimbPhase.Descend )
					{
						GlobalPosition = climbGround;
						GlobalBasis = new Angles( 0f, YawOf( GlobalBasis ), 0f ).ToBasis();
						EndClimb();
						agent.SetAgentPosition( climbGround );
						return false;
					}
				}
				else
				{
					climbNormal = climbNormal.Lerp( tr.Normal, MathX.Clamp( dt * 6f, 0f, 1f ) ).Normalized();
					GlobalPosition = next.Lerp( tr.HitPosition - probe + climbNormal * 1.5f * U.In, MathX.Clamp( dt * 9f, 0f, 1f ) );
					climbVelocity = upDir * direction * rate;
				}
				if ( climbPhase == ClimbPhase.Descend )
				{
					var floor = Trace.Ray( this, GlobalPosition + Vector3.Up * 10f * U.In, GlobalPosition + Vector3.Down * 28f * U.In, Layers.World );
					if ( floor.Hit && floor.Normal.Y > 0.7f )
					{
						GlobalPosition = floor.HitPosition;
						GlobalBasis = new Angles( 0f, YawOf( GlobalBasis ), 0f ).ToBasis();
						EndClimb();
						agent.SetAgentPosition( GlobalPosition );
						return false;
					}
				}
				// Head up the wall, feet into it.
				var wanted = Basis.LookingAt( upDir, climbNormal ).GetRotationQuaternion();
				GlobalBasis = new Basis( GlobalBasis.Orthonormalized().GetRotationQuaternion().SafeSlerp( wanted, MathX.Clamp( dt * 6f, 0f, 1f ) ) );
				if ( climbPhase == ClimbPhase.Perch && nextHarpoonScan <= 0f )
				{
					nextHarpoonScan = 0.4f;
					if ( FindEnemy( GlobalPosition, HarpoonRange, flat: false ) is { } target ) Harpoon( target, owner );
				}
				return true;
			}
		}
	}

	static float YawOf( Basis b )
	{
		var f = -b.Z;
		return MathX.Deg( MathF.Atan2( -f.X, -f.Z ) );
	}

	void ThinkAlly( float dt )
	{
		Stabbing = false;
		var owner = OwnerPlayer?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
		if ( owner is null || !owner.IsAlive ) owner = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var ownerPos = owner?.WorldPosition ?? GlobalPosition;
		var velocity = agent.Velocity.Flat();

		if ( ClimbThink( owner, ownerPos, dt ) ) { NetVelocity = climbVelocity; return; }

		if ( AllyMode == ModeAttack )
		{
			currentEnemy = AttackTarget;
			if ( EnemyDead( currentEnemy ) ) { AllyMode = ModeFollow; currentEnemy = null; }
		}
		if ( AllyMode != ModeAttack && nextScan <= 0f )
		{
			nextScan = 0.25f;
			currentEnemy = AllyMode == ModeHold ? FindEnemy( HoldPoint, 380f ) : FindEnemy( GlobalPosition, 460f, ownerPos, 800f );
		}
		if ( currentEnemy is not null && EnemyDead( currentEnemy ) ) currentEnemy = null;

		if ( currentEnemy is not null && (currentEnemy.GlobalPosition.Y - GlobalPosition.Y) / U.In > 70f )
			Harpoon( currentEnemy, owner );
		else if ( currentEnemy is null && nextHarpoonScan <= 0f )
		{
			nextHarpoonScan = 0.4f;
			if ( FindEnemy( GlobalPosition + Vector3.Up * 200f * U.In, HarpoonRange, flat: false ) is { } high && (high.GlobalPosition.Y - GlobalPosition.Y) / U.In > 70f )
				Harpoon( high, owner );
		}

		var speedScale = 1f;
		if ( currentEnemy is not null )
		{
			var toEnemy = (currentEnemy.GlobalPosition - GlobalPosition).Flat();
			if ( toEnemy.Length() <= AttackRange * S * 0.9f )
			{
				agent.Stop();
				Face( toEnemy, 10f, dt );
				Stabbing = true;
				if ( timeSinceAttack >= AttackCooldown ) { timeSinceAttack = 0f; timeSinceStab = 0f; Strike( currentEnemy, owner ); }
			}
			else
			{
				speedScale = 1.1f;
				if ( nextMove <= 0f ) { nextMove = 0.25f; agent.MoveTo( currentEnemy.GlobalPosition ); }
				if ( velocity.Length() > 10f * U.In ) Face( velocity, 8f, dt ); else Face( toEnemy, 8f, dt );
			}
		}
		else
		{
			Vector3 goal;
			float stopRange;
			if ( AllyMode == ModeHold ) { goal = HoldPoint; stopRange = 60f; }
			else
			{
				var fromOwner = (GlobalPosition - ownerPos).Flat();
				var ownerDistance = fromOwner.Length() / U.In;
				if ( ownerDistance > 380f ) catchingUp = true;
				else if ( ownerDistance < 260f ) catchingUp = false;
				var side = fromOwner.IsNearlyZero() ? Vector3.Right : fromOwner.Normalized();
				goal = ownerPos + side * 210f * U.In;
				stopRange = catchingUp ? 40f : 100000f;
			}
			var distance = (goal - GlobalPosition).Flat().Length() / U.In;
			if ( AllyMode != ModeHold && distance > 1100f ) agent.SetAgentPosition( ownerPos + Rng.OnSphere().Flat().SafeNormal() * 150f * U.In );
			if ( distance > stopRange )
			{
				speedScale = distance > 450f ? 1.15f : 1f;
				if ( nextMove <= 0f ) { nextMove = 0.25f; agent.MoveTo( goal ); }
				if ( velocity.Length() > 10f * U.In ) Face( velocity, 7f, dt );
			}
			else
			{
				agent.Stop();
				var look = AllyMode == ModeHold ? (HoldPoint - ownerPos) : (ownerPos - GlobalPosition);
				Face( look.Flat(), 4f, dt );
			}
		}
		agent.MaxSpeed = MoveSpeed * speedScale * S;
		NetVelocity = agent.Velocity;
	}

	void Strike( Node3D enemy, PlayerSurvivor owner, float scale = 1f )
	{
		var damage = StabDamage * 2.5f * scale;
		var direction = (enemy.GlobalPosition - GlobalPosition).Flat().SafeNormal();
		var hitParts = CreaturePart.All.Where( p => IsInstanceValid( p ) && p.Creature == enemy ).ToList();
		if ( hitParts.Count == 0 ) return;
		var part = hitParts[Rng.Int( 0, hitParts.Count - 1 )];
		part.ReceiveShot( damage, part, direction, part.GlobalPosition, GlobalPosition + Vector3.Up * 40f * U.In, owner?.Controller, true );
	}

	float BurstScale()
	{
		if ( burstUntil <= 0f )
		{
			dashing = !dashing;
			burstUntil = dashing ? Rng.Float( 0.7f, 1.2f ) : Rng.Float( 0.35f, 0.7f );
		}
		return dashing ? 1.25f : 0.3f;
	}

	void Stab( PlayerSurvivor target )
	{
		Sfx.Play( "creature_attack", GlobalPosition, 0.8f, Rng.Float( 0.8f, 0.95f ) );
		var frontLegs = (Has( FirstThigh ) ? 1 : 0) + (Has( FirstThigh + 3 ) ? 1 : 0);
		var damage = StabDamage * (frontLegs switch { 2 => 1f, 1 => 0.7f, _ => 0.4f });
		target.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = this, Position = target.WorldPosition + Vector3.Up * 20f * U.In, Origin = GlobalPosition } );
	}

	// ---- Getting shot ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead || IsAlly ) return;
		if ( index < 0 || index >= parts.Length || !Has( index ) ) index = Body;
		NoteShot( direction, position, origin );
		var part = parts[index];
		var crit = index == EyePart;
		var multiplier = index switch { Body => 1f, EyePart => EyeMultiplier, _ => LimbMultiplier };
		var cut = false;
		if ( index != Body )
		{
			part.Health -= damage;
			cut = part.Health <= 0f;
		}
		var hurt = index == Body
			? 1f - MathX.Clamp( (health.Current - damage * multiplier) / MathF.Max( 1f, health.MaxHealth ), 0f, 1f )
			: 1f - MathX.Clamp( part.Health / MathF.Max( 1f, part.MaxHealth ), 0f, 1f );
		if ( cut )
		{
			GoneMask |= SubtreeMask( index );
			CutLocal( index, direction, quiet: false );
		}
		else Hit( index, position, direction, Rng.Float() < 0.3f, hurt );
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * multiplier, Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( !killed ) agent.SetAgentPosition( GlobalPosition + direction.Flat().SafeNormal() * 6f * U.In );
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, crit, scoreHit );
	}

	void Hit( int index, Vector3 position, Vector3 direction, bool chip, float hurt )
	{
		var part = parts[index];
		if ( part is null ) return;
		part.Flash = 0f;
		part.Hurt = MathF.Max( part.Hurt, hurt * 0.8f );
		hitKick = (hitKick + direction.Flat().SafeNormal() * 5f * U.In).ClampLength( 10f * U.In );
		hitFlinch = 1f;
		lureKick += direction.SafeNormal() * 900f * U.In;
		gore.GooBurst( position, -direction, gooColor, 6, 220f, 0.8f );
		gore.GooSpray( position, direction, 2, gooColor, 180f, 0.4f, 0.9f );
		if ( !part.Gone )
		{
			CreatureGore.AddWound( part.Go, position, direction, shellColor, gooColor, (index == Body ? 3.5f : 2f) * Size );
			CreatureGore.Jiggle( part.Go, index == Body ? 0.7f : 1f );
		}
		if ( chip && index == Body ) gore.ThrowBlob( position, -direction, shellColor, gooColor, 3.5f * Size );
	}

	void CutLocal( int index, Vector3 direction, bool quiet )
	{
		var part = parts[index];
		if ( part is null || part.Gone ) return;
		MarkGone( index );
		if ( quiet ) { part.Go.QueueFree(); return; }
		var at = part.Go.GlobalPosition;
		if ( index != EyePart )
		{
			var parent = part.Parent >= 0 ? parts[part.Parent] : null;
			var outward = parent is not null ? (at - parent.Go.GlobalPosition).SafeNormal() : Vector3.Up;
			var radius = (index < FirstShin ? 3.2f : 2.4f) * Size;
			if ( parent is not null && !parent.Gone ) CreatureGore.AddStump( parent.Go, at, outward, radius, shellColor, gooColor, 4f );
			CreatureGore.AddStump( part.Go, at, -outward, radius, shellColor, gooColor, 1.5f );
		}
		if ( index == EyePart )
		{
			gore.GooBurst( at, direction, Colors.White, 10, 220f, 0.9f );
			gore.GooBurst( at, direction, gooColor, 8, 240f, 1f );
			part.Go.QueueFree();
			return;
		}
		gore.GooBurst( at, direction, gooColor, 10, 260f, 1f );
		gore.GooSpray( at, direction, 3, gooColor, 200f, 0.5f, 1.1f );
		var piece = Detach( part, direction * Rng.Float( 80f, 160f ) + Vector3.Up * 120f );
		CreatureGore.Twitch( piece, Rng.Float( 2.5f, 4.5f ), 0.8f );
	}

	Node3D Detach( Part part, Vector3 push, float spin = 12f ) => Detach( part, push, spin, 4f, 15f, 2f, squareUp: false );

	// ---- Death ----

	protected override void OnDied()
	{
		timeSinceDeath = 0f;
		agent.Stop();
		RaiseKilled();
		DieLocal( lastShotDirection, quiet: false, lastShotPointBlank );
	}

	void DieLocal( Vector3 direction, bool quiet, bool overkill = false )
	{
		if ( diedLocally ) return;
		diedLocally = true;
		timeSinceDeath = 0f;
		agent.Enabled = false;
		if ( rig is null || !IsInstanceValid( rig ) ) return;
		if ( quiet ) { rig.QueueFree(); return; }
		var at = parts[Body].Go.GlobalPosition;
		if ( overkill ) CreatureGore.Overkill( at, direction, shellColor, gooColor, Size );
		for ( var k = 0; k < 2; k++ ) gore.GooBurst( at, direction + Vector3.Up, gooColor, 12, 320f, 1.3f );
		gore.GooSpray( at, direction, 6, gooColor, 240f, 0.7f, 1.5f );
		for ( var b = 0; b < 2; b++ ) gore.ThrowBlob( at, direction, shellColor, gooColor, 4.5f * Size );
		if ( Has( EyePart ) )
		{
			MarkGone( EyePart );
			gore.GooBurst( parts[EyePart].Go.GlobalPosition, direction, Colors.White, 8, 200f, 0.8f );
			parts[EyePart].Go.QueueFree();
		}
		for ( var i = 0; i < LegCount; i++ )
		{
			var thigh = parts[FirstThigh + i];
			if ( thigh is not null && !thigh.Gone )
			{
				MarkGone( FirstThigh + i );
				Detach( thigh, (direction + Rng.OnSphere() + Vector3.Up).SafeNormal() * Rng.Float( 120f, 260f ), spin: 20f );
			}
			else if ( parts[FirstShin + i] is { Gone: false } shin )
			{
				MarkGone( FirstShin + i );
				Detach( shin, (direction + Rng.OnSphere() + Vector3.Up).SafeNormal() * Rng.Float( 120f, 260f ), spin: 20f );
			}
		}
		if ( Has( Body ) )
		{
			MarkGone( Body );
			if ( overkill ) parts[Body].Go.QueueFree();
			else Detach( parts[Body], direction * 60f );
		}
		rig.QueueFree();
	}
}

/// <summary> Port of s&box SpiderStrand: a glowing strand that shoots out, holds, then thins away. </summary>
public partial class SpiderStrand : MeshInstance3D
{
	Vector3 from, to;
	float life = 0.5f;
	float radius = 1.1f;
	TimeSince born;
	MeshInstance3D tipBall;

	public static SpiderStrand Create( Vector3 from, Vector3 to, Color color, float life = 0.5f, float radius = 1.1f )
	{
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		var material = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		var strand = new SpiderStrand { from = from, to = to, life = life, radius = radius, Mesh = Shapes.Spindle( 0.78f, 0f ), MaterialOverride = material, CastShadow = ShadowCastingSetting.Off };
		root?.AddChild( strand );
		strand.tipBall = new MeshInstance3D { Mesh = Shapes.Sphere, MaterialOverride = material, CastShadow = ShadowCastingSetting.Off, TopLevel = true };
		strand.AddChild( strand.tipBall );
		strand.born = 0f;
		strand.Visible = false;
		return strand;
	}

	public override void _Process( double delta )
	{
		var t = born / life;
		if ( t >= 1f ) { QueueFree(); return; }
		var reach = MathX.Clamp( t / 0.2f, 0f, 1f );
		var end = from.Lerp( to, 1f - (1f - reach) * (1f - reach) );
		var thin = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
		var d = end - from;
		var length = d.Length();
		if ( length < 1e-4f ) return;
		Visible = true;
		GlobalTransform = new Transform3D( Shapes.LookAt( d ) * Basis.FromScale( new Vector3( radius * thin * U.In, radius * thin * U.In, length ) ), from );
		tipBall.GlobalTransform = new Transform3D( Basis.FromScale( Vector3.One * radius * 2.8f * thin * 2f * U.In ), end );
	}
}

/// <summary>
/// Port of s&box AllyCommander: keeps an ally spider at your side, and Z commands it: an enemy under the crosshair to
/// attack it, the spider itself to call it back, the ground to go there and guard it.
/// </summary>
public partial class AllyCommander : Node
{
	public static bool KeepAlly = true;
	SeaSpider ally;
	TimeUntil nextCheck;
	PlayerController controller;

	public override void _Ready()
	{
		Name = "AllyCommander";
		controller = GetParent<PlayerController>();
	}

	public override void _Process( double delta )
	{
		var survivor = controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
		if ( survivor is null || !survivor.IsAlive ) return;
		if ( nextCheck <= 0f && KeepAlly && Nav.Region is not null )
		{
			nextCheck = 2f;
			ally = SeaSpider.All.FirstOrDefault( s => s.IsAlly && s.OwnerPlayer == controller && !s.IsDead );
			if ( ally is null ) ally = SeaSpider.SpawnAlly( controller );
		}
		if ( ally is not null && IsInstanceValid( ally ) && Input.IsActionJustPressed( "command" ) ) Issue();
	}

	void Issue()
	{
		var camera = controller.Camera;
		var start = camera.GlobalPosition;
		var forward = -camera.GlobalBasis.Z;
		var end = start + forward * 4000f * U.In;
		var exclude = new Godot.Collections.Array<Rid> { controller.GetRid() };
		var tr = Trace.Sphere( controller, 18f * U.In, start, end, Layers.World | Layers.Creature, exclude );
		if ( tr.Hit && Shootables.Find( tr.Node ) is CreaturePart part && part.Creature is not SeaSpider { IsAlly: true } && !part.IsDead )
		{
			ally.Command( SeaSpider.ModeAttack, part.Creature.GlobalPosition, part.Creature );
			Marker( part.Creature.GlobalPosition, new Color( 1f, 0.25f, 0.2f ), 22f );
			return;
		}
		var toAlly = ally.GlobalPosition + Vector3.Up * 40f * U.In - start;
		if ( toAlly.Length() < 3000f * U.In && forward.Cross( toAlly ).Length() < 60f * U.In && forward.Dot( toAlly ) > 0f )
		{
			ally.Command( SeaSpider.ModeFollow, Vector3.Zero, null );
			Marker( ally.GlobalPosition, new Color( 0.3f, 1f, 0.5f ), 20f );
			return;
		}
		var ground = Trace.Ray( controller, start, end, Layers.World, exclude );
		if ( ground.Hit )
		{
			ally.Command( SeaSpider.ModeHold, ground.HitPosition, null );
			Marker( ground.HitPosition, new Color( 0.25f, 0.9f, 1f ), 18f );
			return;
		}
		ally.Command( SeaSpider.ModeFollow, Vector3.Zero, null );
		Marker( ally.GlobalPosition, new Color( 0.3f, 1f, 0.5f ), 20f );
	}

	void Marker( Vector3 position, Color color, float radius )
	{
		var marker = new MeshInstance3D
		{
			Mesh = Shapes.Sphere,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 2f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		GetTree().CurrentScene.AddChild( marker );
		marker.GlobalPosition = position + Vector3.Up * 2f * U.In;
		marker.Scale = new Vector3( radius, radius * 0.12f, radius ) * 2f * U.In;
		Fx.Expire( marker, 1.4f );
	}
}
