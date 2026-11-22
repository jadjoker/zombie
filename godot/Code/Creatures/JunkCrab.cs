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
	/// <summary> Claws reach this high above the crab's feet (inches): standing over it is safe (playtest 11/11). </summary>
	public float ReachHeight = 24f;
	public float ClawDamage = 10f;
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
	/// <summary> The third eye: on its own stalk between the two, set back, so the three make a triangle. </summary>
	const int EyeM = FirstLeg + LegCount;
	static bool IsEye( int index ) => index is EyeL or EyeR or EyeM;
	public override bool WeakSpot( int index ) => IsEye( index );

	public JunkCrab() => parts = new Part[EyeM + 1];
	Node3D jawL, jawR;
	NavAgent agent;

	Color shellColor, bellyColor, clawColor, gooColor;
	static readonly float[] CrabHues = { 0.0f, 0.05f, 0.93f, 0.78f, 0.58f };
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
		/// <summary> The antenna's elbow, and its springy bend (pitch, yaw in degrees) and how fast that is moving. </summary>
		public Node3D Elbow;
		public Vector2 Bend, BendSpeed;
	}

	const float EyeRadius = 3.4f;
	const float LidRadius = EyeRadius * 1.16f;   // room for the big almond eye under the lids
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
	public override GibRecipe Gibs => GibRecipes.Crab( shellColor, bellyColor, gooColor );

	protected override void Setup()
	{
		BeginSetup( 90f );
		agent = new NavAgent( this ) { Acceleration = 600f * U.In };
		if ( Hue < 0f ) Hue = Rng.Float();
		var hue = Hue;
		// Bright reef colours (it was a near black brown and read as a dark smudge): coral red, tangerine, hot pink,
		// violet or electric blue, with a pale belly and claws a shade lighter than the shell.
		var shellHue = CrabHues[(int)(hue * CrabHues.Length) % CrabHues.Length] + (hue * CrabHues.Length % 1f - 0.5f) * 0.03f;
		shellColor = Shapes.Hsv( shellHue, 0.78f, 0.86f );
		bellyColor = Shapes.Hsv( shellHue + 0.06f, 0.32f, 0.97f );
		clawColor = Shapes.Hsv( shellHue + 0.03f, 0.68f, 0.97f );
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
			var elbow = BuildAntenna( eye, side );
			BuildEye( eye, side, elbow, ElbowToTip( side ) );
			Trigger( eye, new Vector3( 0, 0, 2 ), new Vector3( 1, side * 2.5f, 12 ), 3f );
			Trigger( eye, new Vector3( 1, side * 2.5f, 12 ), new Vector3( 12f, side * 7.5f, 15f ), 4.5f );
		}
		{
			// The middle eye: behind the pair, on the centre line, its stalk standing straight up.
			var eye = AddPart( EyeM, Body, body.Go, "Eye M", new Vector3( 4f, 0f, 7f ), new Angles( -4, 0, 0 ).ToBasis(), EyeHealth );
			var elbow = BuildAntenna( eye, 0f );
			BuildEye( eye, 0f, elbow, ElbowToTip( 0f ) );
			Trigger( eye, new Vector3( 0, 0, 2 ), new Vector3( 1, 0, 12 ), 3f );
			Trigger( eye, new Vector3( 1, 0, 12 ), new Vector3( 7f, 0, 17f ), 4.5f );
		}

		foreach ( var (arm, pincer, side) in new[] { (ArmL, PincerL, 1f), (ArmR, PincerR, -1f) } )
		{
			var a = AddPart( arm, Body, body.Go, side > 0 ? "Arm L" : "Arm R", new Vector3( 10f, side * 13f, 0f ), new Angles( 0, side * 35f, 0 ).ToBasis(), LimbHealth );
			Sculpt( a, "arm", shellColor );
			Trigger( a, new Vector3( 1, 0, 0 ), new Vector3( 13, 0, 0 ), 4.5f );

			var p = AddPart( pincer, arm, a.Go, side > 0 ? "Pincer L" : "Pincer R", new Vector3( 14f, 0, 0 ), new Angles( 0, -side * 40f, 0 ).ToBasis(), LimbHealth );
			Sculpt( p, "pincer", clawColor );
			var jaw = new Node3D { Name = "Jaw" };
			p.Go.AddChild( jaw );
			jaw.Position = U.Sbox( 9.5f, 0, 1.6f );
			Sculpt( p, "jaw", clawColor.Lerp( Colors.Black, 0.12f ), jaw );
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
			mesh.MaterialOverride = Shapes.Sheen( Shapes.Material( color, "chitin" ) );
			part.Renderers.Add( (mesh, color) );
		}
	}

	/// <summary> The alien green of the eye bulbs and their lids, and the big dark almond eye. </summary>
	static readonly Color AlienSkin = new( 0.36f, 0.78f, 0.62f ), AlienEye = new( 0.01f, 0.01f, 0.015f ), EyeRim = new( 0.3f, 1f, 0.35f );

	/// <summary> Where the eye sits from the antenna's elbow (s&box inches): forward, out and a little up. </summary>
	static Vector3 ElbowToTip( float side ) => side == 0f ? new( 6f, 0f, 5f ) : new( 11f, side * 5f, 3f );   // the middle eye leans forward less

	/// <summary>
	/// An ant's elbowed antenna on the eye part: a stiff shaft up from the shell, then a beaded whip bent forward from
	/// the elbow (its own joint, which springs and jiggles). Returns the elbow; the eye goes on its tip.
	/// </summary>
	Node3D BuildAntenna( Part part, float side )
	{
		var stalk = shellColor.Lerp( Colors.Black, 0.2f );
		var mat = Shapes.Sheen( Shapes.Material( stalk, "chitin" ) );
		var top = U.Sbox( 1f, side * 2.5f, 12f );
		part.Renderers.Add( (global::Sculpt.Limb( part.Go, Vector3.Zero, top, 0.95f * U.In, 0.6f * U.In, mat, "Shaft" ), stalk) );
		part.Renderers.Add( (Shapes.Part( part.Go, "Socket", Vector3.Zero, Shapes.Ellipsoid( 1.8f ), stalk, skin: "chitin" ), stalk) );
		var elbow = new Node3D { Name = "Elbow" };
		part.Go.AddChild( elbow );
		elbow.Position = top;
		part.Renderers.Add( (Shapes.Part( elbow, "Knuckle", Vector3.Zero, Shapes.Ellipsoid( 0.95f ), stalk, skin: "chitin" ), stalk) );
		// The whip: beads getting smaller toward the eye.
		var tip = ElbowToTip( side );
		var previous = Vector3.Zero;
		const int beads = 5;
		for ( var i = 1; i <= beads; i++ )
		{
			var t = i / (float)(beads + 1);
			var at = tip * t + new Vector3( 0f, 0f, MathF.Sin( t * MathF.PI ) * 1.2f );
			part.Renderers.Add( (global::Sculpt.Limb( elbow, U.Sbox( previous ), U.Sbox( at ), 0.55f * U.In, 0.45f * U.In, mat, "Whip" ), stalk) );
			part.Renderers.Add( (Shapes.Part( elbow, "Bead", at, Shapes.Ellipsoid( 0.7f - t * 0.2f ), stalk, skin: "chitin" ), stalk) );
			previous = at;
		}
		part.Renderers.Add( (global::Sculpt.Limb( elbow, U.Sbox( previous ), U.Sbox( tip ), 0.45f * U.In, 0.4f * U.In, mat, "Whip" ), stalk) );
		return elbow;
	}

	/// <summary>
	/// An eye on the tip of an antenna, made like the Kraken's: a teal egg of a bulb with a tall glossy black almond
	/// slanted out at the top on a glowing green rim, and teal lids that blink over it. The almond turns to follow the player.
	/// </summary>
	void BuildEye( Part part, float side, Node3D holder, Vector3 at )
	{
		var ball = new Node3D { Name = "Eyeball" };
		holder.AddChild( ball );
		ball.Position = U.Sbox( at );
		ball.Scale = new Vector3( 1f, 1.18f, 1f );   // an egg, like an alien's head
		Sphere( ball, part, Vector3.Zero, new Vector3( EyeRadius, EyeRadius, EyeRadius ), AlienSkin );

		var gaze = new Node3D { Name = "Gaze" };
		ball.AddChild( gaze );
		var pupil = new Node3D { Name = "Pupil" };
		gaze.AddChild( pupil );
		// The Kraken's eye, to the crab's scale: a tall glossy black almond slanted 38 degrees out at the top, on a thin
		// glowing green rim, set into a bulb of the Kraken's teal skin.
		var almond = new Node3D { Name = "Almond", Basis = new Basis( Vector3.Back, side * MathX.Rad( 38f ) ) };
		pupil.AddChild( almond );
		var rim = Shapes.Part( almond, "Rim", new Vector3( EyeRadius * 0.7f, 0, 0 ), Shapes.Ellipsoid( EyeRadius * 0.4f, EyeRadius * 0.42f, EyeRadius * 0.69f ), EyeRim, glow: true, shadow: false );
		part.Renderers.Add( (rim, EyeRim) );
		var black = Shapes.Part( almond, "Eye", new Vector3( EyeRadius * 0.72f, 0, 0 ), Shapes.Ellipsoid( EyeRadius * 0.4f, EyeRadius * 0.35f, EyeRadius * 0.62f ), AlienEye, shadow: false );
		if ( black.MaterialOverride is StandardMaterial3D gloss )
		{
			gloss.Roughness = 0.08f;
			gloss.ClearcoatEnabled = true;
			gloss.Clearcoat = 1f;
			gloss.ClearcoatRoughness = 0.05f;
		}
		part.Renderers.Add( (black, AlienEye) );

		var upper = Lid( ball, part, "Upper Lid", AlienSkin );
		var lower = Lid( ball, part, "Lower Lid", AlienSkin );
		eyes.Add( new CrabEye { Index = part.Index, Side = side, Ball = ball, Gaze = gaze, Pupil = pupil, UpperLid = upper, LowerLid = lower, Elbow = holder } );
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
	bool Blind => !Has( EyeL ) && !Has( EyeR ) && !Has( EyeM );
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
		if ( !Frozen ) Think( dt );
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

		WaveAntennas( dt, now, moving );
		UpdateEyes( dt );
	}

	/// <summary>
	/// The antennas wave slowly, left and right, up and down, each out of step; the whip past the elbow lags on a soft
	/// spring, so it jiggles as the crab scuttles and overshoots when the shaft turns.
	/// </summary>
	void WaveAntennas( float dt, float now, float moving )
	{
		foreach ( var e in eyes )
		{
			if ( !Has( e.Index ) || !IsInstanceValid( e.Elbow ) ) continue;
			var go = parts[e.Index].Go;
			var phase = e.Side * 1.9f + Hue * 6f;
			var pitch = MathF.Sin( now * 0.8f + phase ) * 16f + MathF.Sin( now * 1.9f + phase * 2f ) * 4f;
			var yaw = (MathF.Sin( now * 0.55f + phase * 1.3f ) * 24f + MathF.Sin( now * 1.3f + phase ) * 5f) * (e.Side == 0f ? 0.3f : 1f);   // the middle one stays between the pair
			go.Basis = parts[e.Index].Rest * new Angles( pitch, yaw, 0f ).ToBasis();
			// The whip's spring: it chases a bend that leads the shaft's own wave, and each step of the crab kicks it.
			var target = new Vector2( MathF.Sin( now * 0.8f + phase - 0.9f ) * 10f, MathF.Sin( now * 0.55f + phase * 1.3f - 0.9f ) * 12f );
			target += new Vector2( MathF.Sin( gait * 2f ) * 9f, MathF.Cos( gait ) * 5f ) * moving;
			var steps = Math.Max( 1, (int)MathF.Ceiling( dt / 0.008f ) );
			var h = dt / steps;
			for ( var i = 0; i < steps; i++ )
			{
				e.BendSpeed += ((target - e.Bend) * 90f - e.BendSpeed * 4.5f) * h;
				e.Bend += e.BendSpeed * h;
			}
			e.Bend = new Vector2( MathX.Clamp( e.Bend.X, -35f, 35f ), MathX.Clamp( e.Bend.Y, -35f, 35f ) );
			e.Elbow.Basis = new Angles( e.Bend.X, e.Bend.Y, 0f ).ToBasis();
		}
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
			var rise = (target.WorldPosition.Y - GlobalPosition.Y) / U.In;   // how far above its feet the player's feet are
			if ( distance <= AttackRange * Size && rise <= ReachHeight * Size && !Blind )
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
		target.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = this, Position = target.WorldPosition + Vector3.Up * 6f * U.In, Origin = GlobalPosition } );
	}

	// ---- Getting shot ----

	/// <summary> Dragged along the floor (its nav agent carries it, so the agent is moved). </summary>
	public override void Pull( Vector3 delta )
	{
		if ( Bubbled ) GlobalPosition += delta;   // a bubbled crab is carried freely (the Gore Vac can reel the bubble in)
		else agent?.SetAgentPosition( GlobalPosition + delta.Flat() );
	}

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead ) return;
		if ( index < 0 || index >= parts.Length || !Has( index ) ) index = Body;
		NoteShot( direction, position, origin );
		var part = parts[index];
		var crit = IsEye( index );
		var multiplier = index switch
		{
			Body => WearsBarrel && !Has( Barrel ) ? SoftMultiplier : 1f,
			Barrel => 0.25f,
			EyeL or EyeR or EyeM => EyeMultiplier,
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
			if ( Bubbled ) GlobalPosition += push;   // floating in a bubble: not back onto the navmesh
			else agent.SetAgentPosition( GlobalPosition + push );
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
		if ( chip && index == Body ) GibKit.Chips( Gibs, position, -direction, direction, 0.13f * Size, 2 );
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
			var radius = (IsEye( index ) ? 1.8f : index is ArmL or ArmR or PincerL or PincerR ? 4f : 2.8f) * Size;
			if ( parent is not null && !parent.Gone ) CreatureGore.AddStump( parent.Go, at, outward, radius, shellColor, gooColor, 4f, Gibs );
			if ( !IsEye( index ) ) CreatureGore.AddStump( part.Go, at, -outward, radius, shellColor, gooColor, 1.5f, Gibs );
		}
		if ( index == Barrel )
		{
			gore.GooBurst( at, Vector3.Up, new Color( 0.5f, 1f, 0.2f ) * 2f, 14, 300f, 1.2f );
			gore.GooSpray( at, Vector3.Down, 4, new Color( 0.45f, 1f, 0.15f ), 200f, 0.6f, 1.3f );
			Detach( part, direction * 120f + Vector3.Up * 260f, spin: 18f );
			return;
		}
		if ( IsEye( index ) )
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
		if ( overkill ) CreatureGore.Overkill( at, direction, shellColor, gooColor, Size, Gibs );
		for ( var k = 0; k < 2; k++ ) gore.GooBurst( at, direction + Vector3.Up, gooColor, 12, 320f, 1.3f );
		gore.GooSpray( at, direction, 6, gooColor, 240f, 0.7f, 1.5f );
		GibKit.Burst( Gibs, at, direction, 0.17f * Size, 6 );

		foreach ( var index in new[] { Barrel, ArmL, ArmR, EyeL, EyeR, EyeM }.Concat( Enumerable.Range( FirstLeg, LegCount ) ) )
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
