using Godot;

/// <summary>
/// Port of the s&box Junk Crab: a sea-floor crab hyper-evolved on alien waste. It scuttles across the floor at you
/// and snaps with its claws. Every leg can be shot off (it slows, and drags itself once a side is gone); each claw
/// comes off at the shoulder or the pincer; the eye stalks are crit spots, and with both popped it is blind and
/// stumbles about. The body is lethal. Optional waste barrel armour (off, as in s&box). Numbers in inches.
/// </summary>
public partial class JunkCrab : PartCreature
{
	public float MoveSpeed = 210f;
	public float AttackRange = 70f;
	public float ClawDamage = 14f;
	public float AttackCooldown = 1.1f;
	public float BarrelHealth = 80f;
	public float LimbHealth = 20f;
	public float EyeHealth = 20f;
	public float SoftMultiplier = 1.6f;
	public float EyeMultiplier = 0.6f;
	public float LimbMultiplier = 0.15f;
	public bool WearsBarrel = false;

	public float Hue = -1f;
	public float Size = 1f;
	public bool Snapping;

	public static readonly List<JunkCrab> All = new();

	const int Body = 0, Barrel = 1, EyeL = 2, EyeR = 3, ArmL = 4, PincerL = 5, ArmR = 6, PincerR = 7, FirstLeg = 8, LegCount = 6;

	public JunkCrab() => parts = new Part[FirstLeg + LegCount];
	Node3D jawL, jawR;
	NavAgent agent;

	Color shellColor, bellyColor, gooColor;
	TimeSince timeSinceAttack = 999f;
	TimeSince timeSinceRepath = 999f;
	TimeSince timeSinceSnap = 999f;
	Vector3 wanderGoal;
	TimeSince bleedTimer;
	float gait;

	sealed class CrabEye
	{
		public int Index;
		public float Side;
		public Node3D Ball, Gaze, UpperLid, LowerLid, Pupil;
		public Quaternion GazeRot = Quaternion.Identity;
		public Vector3 Wander = Vector3.Forward;   // Godot, eyeball local
		public float Upper = 0.2f, Lower = 0.08f, Slant, PupilSize = 1f;
	}

	const float EyeRadius = 3.4f;
	const float LidRadius = EyeRadius * 1.07f;
	readonly List<CrabEye> eyes = new();
	TimeUntil nextBlink = 2f;
	TimeUntil nextGlance = 1f;
	float blink = 1f;

	static readonly SphereMesh Dome = new() { Radius = 1f, Height = 1f, IsHemisphere = true, RadialSegments = 28, Rings = 14 };

	public static JunkCrab Spawn( Node parent, Vector3 position, float maxHealth )
		=> Place( new JunkCrab { Name = "Junk Crab" }, parent, position, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }
	protected override string Kind => "Crab";
	protected override Color GibGoo => gooColor;

	protected override void Setup()
	{
		BeginSetup( 90f );
		agent = new NavAgent( this ) { Acceleration = 600f * U.In };
		if ( Hue < 0f ) Hue = Rng.Float();
		var hue = Hue;
		shellColor = Shapes.Hsv( MathX.LerpUnclamped( -0.08f, 0.1f, hue ), 0.9f, 0.42f );
		bellyColor = Shapes.Hsv( MathX.LerpUnclamped( -0.05f, 0.12f, hue ), 0.45f, 0.6f );
		gooColor = Shapes.Hsv( 0.3f + hue * 0.1f, 1f, 1f );
		var down = Trace.Ray( this, GlobalPosition + Vector3.Up * 40f * U.In, GlobalPosition + Vector3.Down * 4000f * U.In, Layers.World );
		if ( down.Hit ) GlobalPosition = down.HitPosition;
		agent?.Snap();
		Build( hue );
		// Built after this frame's update, so hide it until the first pose (the claws sat out of place for a frame).
		if ( rig is not null ) rig.Visible = false;
	}

	// ---- Building ----

	void Build( float hue )
	{
		rig = new Node3D { Name = "Crab Rig" };
		GetTree().CurrentScene.AddChild( rig );
		rig.GlobalTransform = new Transform3D( GlobalBasis.Orthonormalized().Scaled( Vector3.One * Size ), GlobalPosition );

		var body = AddPart( Body, -1, rig, "Body", new Vector3( 0, 0, 14 ), Basis.Identity, 0f );
		Sculpt( body, "body", shellColor );
		Sphere( body.Go, body, new Vector3( 16.6f, 0, -1.8f ), new Vector3( 1.4f, 4.6f, 1.3f ), new Color( 0.12f, 0.02f, 0.04f ) );   // mouth
		foreach ( var side in new[] { -1f, 1f } )
		{
			var mr = Shapes.AddSpindle( body.Go, "Shape", new Vector3( 16.2f, side * 3f, -2.6f ), new Angles( 0f, side * 25f, 0f ).Forward, U.Sbox( 0, -side, 0 ).Normalized(), 1f, 6.5f, 0f, 0.45f, shellColor.Lerp( Colors.Black, 0.5f ) );
			body.Renderers.Add( (mr, shellColor.Lerp( Colors.Black, 0.5f )) );
		}
		Trigger( body, new Vector3( -7, -8, -1 ), new Vector3( -7, 8, -1 ), 8f );
		Trigger( body, new Vector3( 5, -8, -1 ), new Vector3( 5, 8, -1 ), 8f );

		if ( WearsBarrel ) BuildBarrel( body, hue );
		BuildLimbs( body );
	}

	void BuildBarrel( Part body, float hue )
	{
		var barrel = AddPart( Barrel, Body, body.Go, "Barrel", new Vector3( -2f, 0, 7f ), new Angles( 0, 0, 90 ).ToBasis(), BarrelHealth );
		var drum = Shapes.Hsv( 0.48f + hue * 0.1f, 0.6f, 0.75f );
		Box( barrel, Vector3.Zero, new Vector3( 18f, 15f, 26f ), drum );
		Box( barrel, Vector3.Zero, new Vector3( 18.6f, 15.6f, 4f ), new Color( 0.4f, 1f, 0.2f ) * 1.6f );
		Box( barrel, new Vector3( 0, 0, 12.5f ), new Vector3( 17f, 14f, 1.5f ), new Color( 0.75f, 0.75f, 0.8f ) );
		Box( barrel, new Vector3( 0, 0, -12.5f ), new Vector3( 17f, 14f, 1.5f ), new Color( 0.75f, 0.75f, 0.8f ) );
		Trigger( barrel, new Vector3( 0, 0, -12 ), new Vector3( 0, 0, 12 ), 11f );
	}

	void BuildLimbs( Part body )
	{
		foreach ( var (index, side) in new[] { (EyeL, 1f), (EyeR, -1f) } )
		{
			var eye = AddPart( index, Body, body.Go, side > 0 ? "Eye L" : "Eye R", new Vector3( 11f, side * 5f, 5f ), new Angles( -10, 0, -side * 12 ).ToBasis(), EyeHealth );
			Sculpt( eye, "eyestalk", bellyColor );
			BuildEye( eye, side );
			Trigger( eye, new Vector3( 0, 0, 3 ), new Vector3( 0, 0, 12 ), 4f );
		}

		foreach ( var (arm, pincer, side) in new[] { (ArmL, PincerL, 1f), (ArmR, PincerR, -1f) } )
		{
			var a = AddPart( arm, Body, body.Go, side > 0 ? "Arm L" : "Arm R", new Vector3( 10f, side * 13f, 0f ), new Angles( 0, side * 35f, 0 ).ToBasis(), LimbHealth );
			Sculpt( a, "arm", shellColor );
			Trigger( a, new Vector3( 1, 0, 0 ), new Vector3( 13, 0, 0 ), 4.5f );

			var p = AddPart( pincer, arm, a.Go, side > 0 ? "Pincer L" : "Pincer R", new Vector3( 14f, 0, 0 ), new Angles( 0, -side * 40f, 0 ).ToBasis(), LimbHealth );
			Sculpt( p, "pincer", shellColor );
			var jaw = new Node3D { Name = "Jaw" };
			p.Go.AddChild( jaw );
			jaw.Position = U.Sbox( 9.5f, 0, 1.6f );
			Sculpt( p, "jaw", shellColor.Lerp( Colors.Black, 0.15f ), jaw );
			if ( side > 0 ) jawL = jaw; else jawR = jaw;
			Trigger( p, new Vector3( 1, 0, 0 ), new Vector3( 15, 0, 0 ), 6f );
		}

		for ( var i = 0; i < LegCount; i++ )
		{
			var side = i < 3 ? 1f : -1f;
			var row = i % 3;
			var leg = AddPart( FirstLeg + i, Body, body.Go, $"Leg {i}", new Vector3( 6f - row * 7f, side * 15f, -1f ), new Angles( 0, side * (75f + row * 22f), 0 ).ToBasis(), LimbHealth );
			Sculpt( leg, "leg", shellColor );
			Trigger( leg, new Vector3( 1, 0, 2 ), new Vector3( 17, 0, -10 ), 4f );
		}

		foreach ( var part in parts )
			if ( part is not null ) part.Rest = part.Go.Basis;
	}

	static readonly Dictionary<string, PackedScene> sculpts = new();

	void Sculpt( Part part, string model, Color color, Node3D parent = null )
	{
		if ( !sculpts.TryGetValue( model, out var scene ) )
			sculpts[model] = scene = GD.Load<PackedScene>( $"res://assets/models/creatures/crab/{model}.fbx" );
		var node = scene.Instantiate<Node3D>();
		// Like the spider sculpts: length imports along +X, turn it to run along the part's forward (-Z).
		node.Basis = new Basis( Vector3.Up, MathF.PI * 0.5f ) * Basis.FromScale( Vector3.One * 2.54f );
		(parent ?? part.Go).AddChild( node );
		foreach ( var mesh in node.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			mesh.MaterialOverride = Shapes.Material( color, "chitin" );
			part.Renderers.Add( (mesh, color) );
		}
	}

	void BuildEye( Part part, float side )
	{
		var ball = new Node3D { Name = "Eyeball" };
		part.Go.AddChild( ball );
		ball.Position = U.Sbox( 0.6f, 0, 11.5f );
		Sphere( ball, part, Vector3.Zero, new Vector3( EyeRadius, EyeRadius, EyeRadius ), new Color( 0.97f, 0.96f, 0.93f ) );

		var gaze = new Node3D { Name = "Gaze" };
		ball.AddChild( gaze );
		var pupil = new Node3D { Name = "Pupil" };
		gaze.AddChild( pupil );
		Sphere( pupil, part, new Vector3( EyeRadius * 0.9f, 0, 0 ), new Vector3( 0.42f, 1.55f, 1.75f ), new Color( 0.02f, 0.02f, 0.03f ) );
		Sphere( pupil, part, new Vector3( EyeRadius * 0.98f, 0.5f * side, 0.65f ), new Vector3( 0.2f, 0.42f, 0.42f ), Colors.White );
		Sphere( pupil, part, new Vector3( EyeRadius * 0.98f, -0.45f * side, -0.6f ), new Vector3( 0.15f, 0.2f, 0.2f ), new Color( 0.85f, 0.88f, 0.95f ) );

		var upper = Lid( ball, part, "Upper Lid", shellColor );
		var lower = Lid( ball, part, "Lower Lid", bellyColor );
		eyes.Add( new CrabEye { Index = part.Index, Side = side, Ball = ball, Gaze = gaze, Pupil = pupil, UpperLid = upper, LowerLid = lower } );
	}

	/// <summary> An eyelid: a half dome a touch bigger than the eyeball (dome over s&box +Z, Godot +Y). </summary>
	Node3D Lid( Node3D ball, Part part, string name, Color color )
	{
		var go = new Node3D { Name = name };
		ball.AddChild( go );
		var mesh = new MeshInstance3D { Name = "Shape", Mesh = Dome, MaterialOverride = Shapes.Material( color, "chitin" ) };
		go.AddChild( mesh );
		mesh.Scale = Vector3.One * LidRadius * U.In;
		part.Renderers.Add( (mesh, color) );
		return go;
	}

	void UpdateEyes( float dt )
	{
		if ( nextBlink <= 0f )
		{
			blink = 0f;
			nextBlink = Rng.Float() < 0.2f ? 0.25f : Rng.Float( 2f, 5f );
		}
		blink = MathF.Min( 1f, blink + dt / 0.16f );
		var closing = 1f - MathF.Abs( blink * 2f - 1f );

		var target = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var angry = Snapping || timeSinceSnap < 0.6f;
		var shocked = parts.Any( p => p is not null && !p.Gone && p.Flash < 0.5f );
		if ( nextGlance <= 0f )
		{
			nextGlance = Rng.Float( 0.6f, 1.8f );
			foreach ( var e in eyes ) e.Wander = U.Sbox( 1f, Rng.Float( -0.6f, 0.6f ), Rng.Float( -0.3f, 0.4f ) ).Normalized();
		}

		var now = (float)Clock.Now;
		foreach ( var e in eyes )
		{
			if ( !Has( e.Index ) || !IsInstanceValid( e.Ball ) ) continue;
			var look = e.Wander;
			if ( target is not null && target.WorldPosition.DistanceTo( GlobalPosition ) < 1400f * U.In )
				look = (e.Ball.GlobalBasis.Orthonormalized().Inverse() * ((target.WorldPosition + Vector3.Up * 60f * U.In) - e.Ball.GlobalPosition)).SafeNormal();
			var limit = MathF.Cos( MathX.Rad( 42f ) );
			if ( -look.Z < limit )
			{
				var sideways = look with { Z = 0f };
				sideways = sideways.IsNearlyZero() ? Vector3.Up : sideways.Normalized();
				look = (Vector3.Forward * limit + sideways * MathF.Sqrt( 1f - limit * limit )).Normalized();
			}
			e.GazeRot = e.GazeRot.SafeSlerp( Shapes.LookAt( look ).GetRotationQuaternion(), MathX.Clamp( dt * 14f, 0f, 1f ) );
			e.Gaze.Basis = new Basis( e.GazeRot );

			var upper = shocked ? 0.02f : angry ? 0.42f : 0.2f + 0.04f * MathF.Sin( now * 0.7f + e.Side );
			var lower = shocked ? 0f : angry ? 0.2f : 0.08f;
			var slant = angry ? 28f : shocked ? -8f : 6f;
			var pupilSize = shocked ? 0.6f : angry ? 0.85f : 1f;
			var k = MathX.Clamp( dt * 12f, 0f, 1f );
			e.Upper = MathX.Lerp( e.Upper, upper, k );
			e.Lower = MathX.Lerp( e.Lower, lower, k );
			e.Slant = MathX.Lerp( e.Slant, slant, k );
			e.PupilSize = MathX.Lerp( e.PupilSize, pupilSize, k );

			var up = MathX.Lerp( e.Upper, 0.56f, closing );
			var down = MathX.Lerp( e.Lower, 0.5f, closing );
			e.UpperLid.Basis = new Basis( Vector3.Forward, MathX.Rad( e.Side * e.Slant ) ) * new Angles( -LidEdge( up ), 0f, 0f ).ToBasis();
			e.LowerLid.Basis = new Angles( LidEdge( down ), 0f, 0f ).ToBasis() * new Angles( 0f, 0f, 180f ).ToBasis();
			e.Pupil.Scale = new Vector3( e.PupilSize, e.PupilSize, 1f );
		}
	}

	void Sphere( Node3D parent, Part part, Vector3 at, Vector3 radii, Color color )
	{
		var skin = color == shellColor || color == bellyColor ? "chitin" : null;
		var mesh = Shapes.Part( parent, "Shape", at, Shapes.Ellipsoid( radii.X, radii.Y, radii.Z ), color, skin: skin );
		part.Renderers.Add( (mesh, color) );
	}

	void Box( Part part, Vector3 at, Vector3 size, Color color )
	{
		var mesh = Shapes.Part( part.Go, "Shape", at, Shapes.BoxSize( size.X, size.Y, size.Z ), color, box: true );
		part.Renderers.Add( (mesh, color) );
	}

	// ---- Every frame ----

	int LegsLeft( float side ) => Enumerable.Range( 0, LegCount ).Count( i => (i < 3 ? 1f : -1f) == side && Has( FirstLeg + i ) );
	int LegsLost => LegCount - LegsLeft( 1f ) - LegsLeft( -1f );
	bool Dragging => LegsLeft( 1f ) <= 1 || LegsLeft( -1f ) <= 1;
	bool Blind => !Has( EyeL ) && !Has( EyeR );
	int ClawsLeft => (Has( PincerL ) ? 1 : 0) + (Has( PincerR ) ? 1 : 0);

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
		TintParts( dt );
		rig.Visible = true;
	}

	void Animate( float dt )
	{
		var velocity = agent.Velocity.Flat() / U.In;
		var speed = velocity.Length();
		gait += dt * MathX.Clamp( speed / 18f, 0f, 14f );
		var moving = MathX.Clamp( speed / 60f, 0f, 1f );
		var now = (float)Clock.Now;

		var body = parts[Body].Go;
		var list = 0f;
		if ( Dragging ) list = (LegsLeft( 1f ) < LegsLeft( -1f ) ? 1f : -1f) * 14f;
		body.Position = U.Sbox( 0, 0, 14f + MathF.Abs( MathF.Sin( gait ) ) * 2f * moving - (Dragging ? 5f : 0f) );
		body.Basis = new Angles( MathF.Sin( gait * 0.5f ) * 3f * moving, 0, list + MathF.Sin( gait ) * 3f * moving ).ToBasis();

		for ( var i = 0; i < LegCount; i++ )
		{
			var part = parts[FirstLeg + i];
			if ( part is null || part.Gone ) continue;
			var phase = gait + (i % 2 == 0 ? 0f : MathF.PI) + i * 0.4f;
			var swing = MathF.Sin( phase ) * 22f * moving;
			var lift = MathF.Max( 0f, MathF.Cos( phase ) ) * 18f * moving;
			part.Go.Basis = part.Rest * new Angles( -lift, swing, 0 ).ToBasis();
		}

		var snap = Snapping || timeSinceSnap < 0.25f;
		foreach ( var (arm, jaw, side) in new[] { (ArmL, jawL, 1f), (ArmR, jawR, -1f) } )
		{
			if ( Has( arm ) )
			{
				var raise = snap ? -35f : -8f + MathF.Sin( gait * 0.5f + side ) * 6f;
				parts[arm].Go.Basis = parts[arm].Rest * new Angles( raise, 0, 0 ).ToBasis();
			}
			if ( jaw is not null && IsInstanceValid( jaw ) )
			{
				var open = snap ? (MathF.Sin( now * 40f ) > 0 ? 35f : 0f) : 12f + MathF.Sin( now * 3f + side ) * 8f;
				jaw.Basis = new Angles( -open, 0f, 0f ).ToBasis();
			}
		}

		foreach ( var (eye, side) in new[] { (EyeL, 1f), (EyeR, -1f) } )
			if ( Has( eye ) ) parts[eye].Go.Basis = parts[eye].Rest * new Angles( MathF.Sin( now * 2.3f + side ) * 8f, 0, MathF.Sin( now * 1.7f + side * 2f ) * 10f ).ToBasis();
		UpdateEyes( dt );
	}

	// ---- Brain ----

	void Think( float dt )
	{
		var target = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var speedScale = (1f - 0.11f * LegsLost) * (Dragging ? 0.6f : 1f) * (Blind ? 0.7f : 1f);
		agent.MaxSpeed = MoveSpeed * MathF.Max( 0.2f, speedScale ) * Size * U.In;

		Snapping = false;
		if ( target is not null )
		{
			var toTarget = (target.WorldPosition - GlobalPosition).Flat();
			var distance = toTarget.Length() / U.In;
			if ( distance <= AttackRange * Size && !Blind )
			{
				agent.Stop();
				Face( toTarget, 10f, dt );
				Snapping = true;
				if ( timeSinceAttack >= AttackCooldown )
				{
					timeSinceAttack = 0f;
					timeSinceSnap = 0f;
					Pinch( target );
				}
			}
			else if ( timeSinceRepath > 0.3f )
			{
				timeSinceRepath = 0f;
				if ( Blind )
				{
					if ( wanderGoal == Vector3.Zero || GlobalPosition.DistanceTo( wanderGoal ) < 60f * U.In || Rng.Float() < 0.15f )
						wanderGoal = target.WorldPosition + Rng.OnSphere().Flat() * 350f * U.In;
					agent.MoveTo( wanderGoal );
					if ( distance <= AttackRange * Size && timeSinceAttack > AttackCooldown * 2f ) { timeSinceAttack = 0f; timeSinceSnap = 0f; Pinch( target ); }
				}
				else agent.MoveTo( target.WorldPosition );
			}
			var velocity = agent.Velocity.Flat();
			if ( !Blind ) Face( toTarget, 8f, dt );
			else if ( velocity.Length() > 10f * U.In ) Face( velocity, 6f, dt );
		}
		else agent.Stop();
		agent.Update( dt );
	}

	void Pinch( PlayerSurvivor target )
	{
		Sfx.Play( "crab_snap", GlobalPosition, 0.9f, Rng.Float( 0.9f, 1.1f ) );
		var damage = ClawDamage * (ClawsLeft switch { 2 => 1f, 1 => 0.7f, _ => 0.4f });
		target.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = this, Position = target.WorldPosition + Vector3.Up * 20f * U.In, Origin = GlobalPosition } );
	}

	// ---- Getting shot ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead ) return;
		if ( index < 0 || index >= parts.Length || !Has( index ) ) index = Body;
		NoteShot( direction, position, origin );
		var part = parts[index];
		var crit = index is EyeL or EyeR;
		var multiplier = index switch
		{
			Body => WearsBarrel && !Has( Barrel ) ? SoftMultiplier : 1f,
			Barrel => 0.25f,
			EyeL or EyeR => EyeMultiplier,
			_ => LimbMultiplier,
		};

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
			CutLocal( index, direction );
		}
		else Hit( index, position, direction, Rng.Float() < 0.3f, hurt );

		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * multiplier, Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( !killed )
		{
			var push = direction.Flat().SafeNormal() * (Has( Barrel ) ? 3f : 7f) * U.In;
			agent.SetAgentPosition( GlobalPosition + push );
		}
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, crit, scoreHit );
	}

	void Hit( int index, Vector3 position, Vector3 direction, bool chip, float hurt )
	{
		var part = parts[index];
		if ( part is null ) return;
		part.Flash = 0f;
		part.Hurt = MathF.Max( part.Hurt, hurt * 0.8f );
		if ( index == Barrel )
		{
			CreatureGore.Jiggle( part.Go, 0.5f );
			gore.GooBurst( position, -direction, new Color( 0.5f, 1f, 0.2f ) * 2f, 5, 260f, 0.6f );
			return;
		}
		gore.GooBurst( position, -direction, gooColor, 6, 220f, 0.8f );
		gore.GooSpray( position, direction, 2, gooColor, 180f, 0.4f, 0.9f );
		if ( !part.Gone )
		{
			CreatureGore.AddWound( part.Go, position, direction, shellColor, gooColor, (index == Body ? 3f : 2f) * Size );
			CreatureGore.Jiggle( part.Go, index == Body ? 0.7f : 1f );
		}
		if ( chip && index == Body ) gore.ThrowBlob( position, -direction, shellColor, gooColor, 3f * Size );
	}

	void CutLocal( int index, Vector3 direction )
	{
		var part = parts[index];
		if ( part is null || part.Gone ) return;
		MarkGone( index );
		if ( index is PincerL or ArmL ) jawL = null;
		if ( index is PincerR or ArmR ) jawR = null;

		var at = part.Go.GlobalPosition;
		if ( index != Barrel )
		{
			var parent = part.Parent >= 0 ? parts[part.Parent] : null;
			var outward = parent is not null ? (at - parent.Go.GlobalPosition).SafeNormal() : Vector3.Up;
			var radius = (index is EyeL or EyeR ? 1.8f : index is ArmL or ArmR or PincerL or PincerR ? 4f : 2.8f) * Size;
			if ( parent is not null && !parent.Gone ) CreatureGore.AddStump( parent.Go, at, outward, radius, shellColor, gooColor, 4f );
			if ( index is not EyeL and not EyeR ) CreatureGore.AddStump( part.Go, at, -outward, radius, shellColor, gooColor, 1.5f );
		}
		if ( index == Barrel )
		{
			gore.GooBurst( at, Vector3.Up, new Color( 0.5f, 1f, 0.2f ) * 2f, 14, 300f, 1.2f );
			gore.GooSpray( at, Vector3.Down, 4, new Color( 0.45f, 1f, 0.15f ), 200f, 0.6f, 1.3f );
			Detach( part, direction * 120f + Vector3.Up * 260f, spin: 18f );
			return;
		}
		if ( index is EyeL or EyeR )
		{
			gore.GooBurst( at, direction, Colors.White, 8, 200f, 0.8f );
			part.Go.QueueFree();
			return;
		}
		gore.GooBurst( at, direction, gooColor, 10, 260f, 1f );
		gore.GooSpray( at, direction, 3, gooColor, 200f, 0.5f, 1.1f );
		var piece = Detach( part, direction * Rng.Float( 80f, 160f ) + Vector3.Up * 120f );
		CreatureGore.Twitch( piece, Rng.Float( 2.5f, 4.5f ), 0.8f );
	}

	Node3D Detach( Part part, Vector3 push, float spin = 12f ) => Detach( part, push, spin, 4f, 15f, 2f );

	// ---- Death ----

	protected override void OnDied()
	{
		timeSinceDeath = 0f;
		agent.Stop();
		RaiseKilled();
		DieLocal( lastShotDirection, lastShotPointBlank );
	}

	/// <summary> The shell cracks open in a burst of goo; barrel, claws and legs scatter. </summary>
	void DieLocal( Vector3 direction, bool overkill )
	{
		if ( diedLocally ) return;
		diedLocally = true;
		timeSinceDeath = 0f;
		agent.Enabled = false;
		if ( rig is null || !IsInstanceValid( rig ) ) return;
		var at = parts[Body].Go.GlobalPosition;
		if ( overkill ) CreatureGore.Overkill( at, direction, shellColor, gooColor, Size );
		for ( var k = 0; k < 2; k++ ) gore.GooBurst( at, direction + Vector3.Up, gooColor, 12, 320f, 1.3f );
		gore.GooSpray( at, direction, 6, gooColor, 240f, 0.7f, 1.5f );
		for ( var b = 0; b < 2; b++ ) gore.ThrowBlob( at, direction, shellColor, gooColor, 4f * Size );

		foreach ( var index in new[] { Barrel, ArmL, ArmR, EyeL, EyeR }.Concat( Enumerable.Range( FirstLeg, LegCount ) ) )
		{
			if ( !Has( index ) ) continue;
			var part = parts[index];
			MarkGone( index );
			Detach( part, (direction + Rng.OnSphere() + Vector3.Up).SafeNormal() * Rng.Float( 120f, 260f ), spin: 20f );
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
