using Godot;

/// <summary>
/// Poseidon, the wave 30 boss (playtest): a giant merman of the alien sea, after the classic painting of him. Bare
/// muscled torso, white hair and beard, a long scaled mermaid's tail, black armour and crown set with purple, green
/// and yellow jewels, and a trident like the player's (only far bigger). He floats, swimming with his tail, circling the
/// player at a distance; every so often he sinks into a whirlpool and comes up out of another one close to the player
/// (never outside the map or the globe). His attacks:
/// - throws tridents that stream the same bubbles as the player's trident shooter;
/// - throws a whirlpool along the floor toward the player: too close and it drags them in and carries them to him,
///   and he stabs them with his trident;
/// - summons seahorses (they join the wave).
/// Lots of health. The head is the weak spot. Built in code (Sculpt), metres, facing -Z; drawn at <see cref="Size"/>.
/// </summary>
public partial class Poseidon : Creature
{
	public static readonly List<Poseidon> All = new();

	public float Size = 1.5f;              // times the built model (about 5.5 m tall, so over 8 m)
	public float HoverHeight = 4.5f;       // his waist over the floor, in metres at Size
	public float KeepAway = 15f;           // circles the player at about this distance
	public float SwimSpeed = 5f;
	public float TridentDamage = 18f;
	public float TridentSpeed = 24f;
	public float StabDamage = 30f;
	public int MaxSeahorses = 6;
	public float SeahorseHealth = 30f;
	public float HeadMultiplier = 2f;
	public float TailMultiplier = 0.6f;
	/// <summary> Tests: he only attacks or vanishes when told (ForceAttack). </summary>
	public bool OnlyWhenTold;
	/// <summary> How many times his whirlpool has brought someone to him and he stabbed them. </summary>
	public int Stabs { get; private set; }

	static readonly Color Skin = new( 0.86f, 0.68f, 0.5f );
	static readonly Color Hair = new( 0.94f, 0.94f, 0.92f );
	static readonly Color Scales = new( 0.1f, 0.56f, 0.5f );
	static readonly Color Armour = new( 0.04f, 0.04f, 0.05f );
	static readonly Color JewelPurple = new( 0.7f, 0.22f, 1f );
	static readonly Color JewelGreen = new( 0.35f, 1f, 0.3f );
	static readonly Color JewelYellow = new( 1f, 0.82f, 0.15f );
	static readonly Color GooColor = new( 0.3f, 0.95f, 0.85f );

	enum Mode { Swim, WindUp, Summon, Vanish, Appear, Stab, Dead }

	Node3D body, chest, head, armR, foreR, armL, foreL, hand, heldTrident;
	readonly List<Node3D> tail = new();
	StandardMaterial3D skin, scales;
	Mode mode = Mode.Swim;
	TimeSince inMode;
	TimeUntil nextAttack = 5f;
	TimeUntil nextVanish = 14f;
	Action afterWindUp;
	Vector3 velocity;
	float orbit;
	float orbitSign = 1f;
	TimeSince sinceHit = 99f;
	TimeSince sinceFloor = 99f;
	float floorY;
	Vector3 appearAt;
	/// <summary> How much of him is there (shrinks into a whirlpool and grows out of one). On the body node: facing
	/// resets the creature node's own scale. </summary>
	float shown = 1f;
	PlayerSurvivor grabbed;
	Vector3 grabFrom;
	bool stabbed;

	public static Poseidon Spawn( Node parent, Vector3 position, float maxHealth ) =>
		Place( new Poseidon { Name = "Poseidon" }, parent, position, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	public float HealthFraction => health?.Fraction ?? 0f;

	protected override void Setup()
	{
		BeginSetup( 18000f );
		orbit = Rng.Float( 0f, MathF.Tau );
		orbitSign = Rng.Float() < 0.5f ? -1f : 1f;
		floorY = Sculpt.FloorBelow( this, GlobalPosition, 80f );
		Build();
		// He arrives out of a whirlpool.
		shown = 0.05f;
		appearAt = GlobalPosition;
		SetMode( Mode.Appear );
		Sfx.Play2D( "boss_spawn", 0.9f );
	}

	protected override void Cleanup() => Release();

	// ---- The body ----

	void Build()
	{
		skin = Sculpt.Paint( Skin, 0.55f );
		scales = Sculpt.Paint( Scales, 0.35f, 0.2f );
		var hair = Sculpt.Paint( Hair, 0.7f );
		var armour = Sculpt.Paint( Armour, 0.25f, 0.6f );
		var purple = Sculpt.Glow( JewelPurple, 2.4f );
		var green = Sculpt.Glow( JewelGreen, 2.4f );
		var yellow = Sculpt.Glow( JewelYellow, 2.2f );
		var fins = Sculpt.Membrane( new Color( 0.2f, 0.75f, 0.65f ), 0.55f );
		var eyes = Sculpt.Glow( new Color( 0.5f, 1f, 0.9f ), 3f );
		body = new Node3D { Name = "Body" };
		AddChild( body );

		// The tail: a chain of joints from the hips down, each a scaled blob with a darker band, ending in a wide fluke.
		Node3D parent = body;
		var at = new Vector3( 0f, -0.15f, 0f );
		for ( var i = 0; i < 9; i++ )
		{
			var joint = Sculpt.Joint( parent, at, $"Tail {i}" );
			var r = MathX.Lerp( 0.44f, 0.12f, i / 8f );
			Sculpt.Blob( joint, new Vector3( 0f, -0.2f, 0f ), new Vector3( r * 2f, 0.5f, r * 1.8f ), scales );
			Sculpt.Blob( joint, new Vector3( 0f, -0.05f, 0f ), new Vector3( r * 2.05f, 0.08f, r * 1.85f ), armour );
			tail.Add( joint );
			parent = joint;
			at = new Vector3( 0f, -0.36f, 0f );
		}
		foreach ( var s in new[] { -1f, 1f } )   // the fluke
			Sculpt.Block( parent, new Vector3( s * 0.32f, -0.42f, 0.08f ), new Vector3( 0.7f, 0.03f, 0.45f ), fins, new Basis( Vector3.Back, s * 0.5f ) * new Basis( Vector3.Right, 0.35f ) );
		foreach ( var s in new[] { -1f, 1f } )   // hip fins
			Sculpt.Block( tail[1], new Vector3( s * 0.45f, -0.15f, 0.05f ), new Vector3( 0.55f, 0.03f, 0.3f ), fins, new Basis( Vector3.Back, s * 0.7f ) );

		// Belt: black with a big yellow jewel and purple and green ones round it.
		Sculpt.Blob( body, new Vector3( 0f, 0f, 0f ), new Vector3( 1.05f, 0.32f, 0.82f ), armour );
		Sculpt.Blob( body, new Vector3( 0f, 0.0f, -0.42f ), new Vector3( 0.26f, 0.3f, 0.12f ), yellow );
		foreach ( var s in new[] { -1f, 1f } )
		{
			Sculpt.Blob( body, new Vector3( s * 0.32f, 0.02f, -0.34f ), Vector3.One * 0.12f, purple );
			Sculpt.Blob( body, new Vector3( s * 0.5f, 0.02f, -0.12f ), Vector3.One * 0.1f, green );
		}

		// The torso: belly and a broad chest.
		chest = Sculpt.Joint( body, new Vector3( 0f, 0.2f, 0f ), "Chest" );
		Sculpt.Blob( chest, new Vector3( 0f, 0.2f, 0f ), new Vector3( 0.85f, 0.75f, 0.6f ), skin );
		Sculpt.Blob( chest, new Vector3( 0f, 0.75f, 0f ), new Vector3( 1.3f, 0.85f, 0.75f ), skin );
		foreach ( var s in new[] { -1f, 1f } )
			Sculpt.Blob( chest, new Vector3( s * 0.25f, 0.82f, -0.28f ), new Vector3( 0.48f, 0.38f, 0.25f ), skin );   // pecs

		// Shoulder armour: black spiked pauldrons with jewels.
		foreach ( var s in new[] { -1f, 1f } )
		{
			var p = new Vector3( s * 0.72f, 1.05f, 0f );
			Sculpt.Blob( chest, p, new Vector3( 0.62f, 0.45f, 0.62f ), armour );
			Sculpt.Blob( chest, p + new Vector3( s * 0.12f, 0.08f, -0.26f ), Vector3.One * 0.13f, s < 0 ? green : purple );
			for ( var k = 0; k < 3; k++ )
				Sculpt.Cone( chest, p + new Vector3( s * 0.18f, 0.12f, (k - 1) * 0.16f ), p + new Vector3( s * 0.55f, 0.55f, (k - 1) * 0.22f ), 0.07f, armour );
		}

		// Head: white hair and beard, glowing eyes, the black crown with its jewels and spikes.
		head = Sculpt.Joint( chest, new Vector3( 0f, 1.32f, -0.02f ), "Head" );
		Sculpt.Blob( head, new Vector3( 0f, 0.22f, 0f ), new Vector3( 0.5f, 0.58f, 0.52f ), skin );
		Sculpt.Blob( head, new Vector3( 0f, 0.3f, 0.12f ), new Vector3( 0.6f, 0.66f, 0.5f ), hair );                              // hair
		Sculpt.Cone( head, new Vector3( 0f, 0.1f, -0.16f ), new Vector3( 0f, -0.62f, -0.22f ), 0.28f, hair, 10 );                 // beard
		Sculpt.Blob( head, new Vector3( 0f, 0.16f, -0.24f ), new Vector3( 0.32f, 0.1f, 0.12f ), hair );                           // moustache
		foreach ( var s in new[] { -1f, 1f } )
		{
			Sculpt.Blob( head, new Vector3( s * 0.11f, 0.3f, -0.23f ), new Vector3( 0.09f, 0.05f, 0.04f ), eyes );
			Sculpt.Block( head, new Vector3( s * 0.11f, 0.37f, -0.25f ), new Vector3( 0.14f, 0.035f, 0.04f ), hair, new Basis( Vector3.Back, -s * 0.35f ) );   // brows
		}
		Sculpt.Limb( head, new Vector3( 0f, 0.44f, 0f ), new Vector3( 0f, 0.62f, 0f ), 0.3f, 0.32f, armour );                    // crown band
		Sculpt.Blob( head, new Vector3( 0f, 0.53f, -0.3f ), new Vector3( 0.15f, 0.18f, 0.08f ), yellow );
		for ( var k = 0; k < 7; k++ )
		{
			var a = MathX.Rad( -90f + (k - 3) * 30f );
			var foot = new Vector3( MathF.Cos( a ) * 0.3f, 0.6f, MathF.Sin( a ) * 0.3f );
			var tall = k == 3 ? 0.55f : 0.32f + (k % 2) * 0.08f;
			Sculpt.Cone( head, foot, foot + new Vector3( MathF.Cos( a ) * 0.08f, tall, MathF.Sin( a ) * 0.08f ), 0.07f, armour );
			if ( k != 3 ) Sculpt.Blob( head, foot + new Vector3( MathF.Cos( a ) * 0.02f, -0.06f, MathF.Sin( a ) * 0.02f ), Vector3.One * 0.07f, k % 3 == 0 ? green : k % 3 == 1 ? purple : yellow );
		}

		// Arms: shoulder and elbow joints to pose, black bracers with jewels, the trident in the right hand.
		(Node3D Arm, Node3D Fore, Node3D Hand) Arm( float s, Material bracerJewel )
		{
			var arm = Sculpt.Joint( chest, new Vector3( s * 0.78f, 1.0f, 0f ), s > 0 ? "Right Arm" : "Left Arm" );
			Sculpt.Limb( arm, Vector3.Zero, new Vector3( 0f, -0.68f, 0f ), 0.2f, 0.17f, skin );
			Sculpt.Blob( arm, new Vector3( 0f, -0.25f, 0f ), new Vector3( 0.38f, 0.42f, 0.36f ), skin );   // biceps
			var fore = Sculpt.Joint( arm, new Vector3( 0f, -0.68f, 0f ), "Elbow" );
			Sculpt.Limb( fore, Vector3.Zero, new Vector3( 0f, -0.62f, 0f ), 0.16f, 0.13f, skin );
			Sculpt.Limb( fore, new Vector3( 0f, -0.22f, 0f ), new Vector3( 0f, -0.55f, 0f ), 0.18f, 0.16f, armour );
			Sculpt.Blob( fore, new Vector3( 0f, -0.38f, -0.16f ), Vector3.One * 0.1f, bracerJewel );
			var h = Sculpt.Joint( fore, new Vector3( 0f, -0.72f, 0f ), "Hand" );
			Sculpt.Blob( h, Vector3.Zero, new Vector3( 0.24f, 0.26f, 0.22f ), skin );
			return (arm, fore, h);
		}
		(armR, foreR, hand) = Arm( 1f, green );
		(armL, foreL, _) = Arm( -1f, purple );
		heldTrident = new Node3D { Name = "Trident" };
		hand.AddChild( heldTrident );
		// Its points out along the arm (the hand's -Y; the trident is built along -Z), so up when the arm is raised.
		heldTrident.Transform = new Transform3D( new Basis( Vector3.Right, -MathF.PI * 0.5f ).Scaled( Vector3.One * 3.2f ), Vector3.Zero );
		TridentModel.BuildTrident( heldTrident );

		// What can be shot: the head (index 0), chest, belly, and the top of the tail.
		void Hit( Node3D on, int index, Vector3 at, float radius )
		{
			var part = CreaturePart.Create( this, index, $"Poseidon {index}", radius );
			on.AddChild( part );
			part.Position = at;
		}
		Hit( head, 0, new Vector3( 0f, 0.25f, 0f ), 0.36f );
		Hit( chest, 1, new Vector3( 0f, 0.75f, 0f ), 0.68f );
		Hit( chest, 2, new Vector3( 0f, 0.15f, 0f ), 0.5f );
		for ( var i = 0; i < 4; i++ ) Hit( tail[i], 3 + i, new Vector3( 0f, -0.2f, 0f ), MathX.Lerp( 0.42f, 0.3f, i / 3f ) );
	}

	// ---- Thinking ----

	void SetMode( Mode m )
	{
		mode = m;
		inMode = 0f;
	}

	PlayerSurvivor Target => PlayerSurvivor.FindNearestAlive( GlobalPosition );

	public override void _Process( double delta )
	{
		if ( health is null || body is null ) return;
		var dt = (float)delta;
		var t = (float)Clock.Now;
		Animate( t, dt );
		if ( mode == Mode.Dead ) { Sink( dt ); return; }
		if ( DevFlags.FreezeEnemies && mode is not Mode.Appear and not Mode.Vanish ) return;
		if ( sinceFloor > 0.3f ) { sinceFloor = 0f; floorY = Sculpt.FloorBelow( this, GlobalPosition, 80f ); }

		var prey = Target;
		switch ( mode )
		{
			case Mode.Swim:
				Swim( prey, dt );
				if ( prey is null ) break;
				if ( OnlyWhenTold ) break;
				if ( nextVanish <= 0f || prey.WorldPosition.DistanceTo( GlobalPosition ) > 40f ) { Vanish(); break; }
				if ( nextAttack <= 0f ) ChooseAttack();
				break;
			case Mode.WindUp:
			case Mode.Summon:
				Hold( prey, dt );
				if ( inMode >= (mode == Mode.Summon ? 1.1f : 0.65f) ) { afterWindUp?.Invoke(); afterWindUp = null; SetMode( Mode.Swim ); }
				break;
			case Mode.Vanish:
				shown = MathX.Lerp( 1f, 0.05f, MathX.Clamp( (float)inMode / 0.8f, 0f, 1f ) );
				RotateY( dt * 9f );
				if ( inMode >= 0.85f )
				{
					GlobalPosition = appearAt;
					SetMode( Mode.Appear );
					PoseidonVortex.Burst( this, appearAt + Vector3.Down * HoverHeight * 0.8f );
				}
				break;
			case Mode.Appear:
				shown = MathX.Lerp( 0.05f, 1f, MathX.Clamp( (float)inMode / 0.8f, 0f, 1f ) );
				RotateY( dt * 9f * (1f - MathX.Clamp( (float)inMode / 0.8f, 0f, 1f )) );
				if ( inMode >= 0.85f ) { shown = 1f; SetMode( Mode.Swim ); nextAttack = Rng.Float( 1.5f, 3f ); }
				break;
			case Mode.Stab:
				Stab( dt );
				break;
		}
	}

	/// <summary> Tests and the dev menu: "trident", "whirlpool", "seahorses" or "vanish", now. </summary>
	public void ForceAttack( string what )
	{
		if ( mode is Mode.Dead or Mode.Stab ) return;
		SetMode( Mode.Swim );
		switch ( what )
		{
			case "trident": WindUp( Mode.WindUp, ThrowTrident ); break;
			case "whirlpool": WindUp( Mode.Summon, ThrowWhirlpool ); break;
			case "seahorses": WindUp( Mode.Summon, SummonSeahorses ); break;
			case "vanish": Vanish(); break;
		}
	}

	void ChooseAttack()
	{
		var roll = Rng.Float();
		var horses = Seahorse.All.Count( s => !s.IsDead );
		if ( roll < 0.18f && horses < MaxSeahorses ) { WindUp( Mode.Summon, SummonSeahorses ); return; }
		if ( roll < 0.42f ) { WindUp( Mode.Summon, ThrowWhirlpool ); return; }
		WindUp( Mode.WindUp, ThrowTrident );
	}

	void WindUp( Mode m, Action then )
	{
		afterWindUp = then;
		SetMode( m );
		nextAttack = Rng.Float( 3.5f, 6f ) * MathX.Lerp( 0.7f, 1f, HealthFraction );   // quicker as he weakens
	}

	/// <summary> Circles the player at a distance, at his hover height over the floor, facing them. </summary>
	void Swim( PlayerSurvivor prey, float dt )
	{
		var wish = Vector3.Zero;
		if ( prey is not null )
		{
			orbit += orbitSign * dt * 0.25f;
			var spot = prey.WorldPosition + new Vector3( MathF.Cos( orbit ), 0f, MathF.Sin( orbit ) ) * KeepAway;
			spot = Sculpt.InBounds( spot );
			spot.Y = floorY + HoverHeight;
			var to = spot - GlobalPosition;
			wish = to.Length() > 0.5f ? to.Normalized() * MathF.Min( SwimSpeed, to.Length() * 1.5f ) : Vector3.Zero;
		}
		velocity = velocity.Lerp( wish, 1f - MathF.Exp( -1.5f * dt ) );
		var next = GlobalPosition + velocity * dt;
		next.Y = MathF.Max( next.Y, floorY + HoverHeight * 0.6f );
		GlobalPosition = Sculpt.InBounds( next );
		if ( prey is not null ) Face( prey.WorldPosition - GlobalPosition, 2.5f, dt );
	}

	void Hold( PlayerSurvivor prey, float dt )
	{
		velocity = velocity.Lerp( Vector3.Zero, 1f - MathF.Exp( -3f * dt ) );
		GlobalPosition += velocity * dt;
		if ( prey is not null ) Face( prey.WorldPosition - GlobalPosition, 4f, dt );
	}

	/// <summary> Sinks into a whirlpool here and comes up out of another close to the player, inside the map. </summary>
	void Vanish()
	{
		var prey = Target;
		if ( prey is null ) return;
		nextVanish = Rng.Float( 12f, 18f );
		var around = prey.WorldPosition;
		var best = GlobalPosition;
		for ( var i = 0; i < 8; i++ )
		{
			var a = Rng.Float( 0f, MathF.Tau );
			var spot = Sculpt.InBounds( around + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * Rng.Float( 9f, 16f ) );
			var ground = Nav.Closest( this, spot );
			if ( ground == Vector3.Zero || ground.Flat().DistanceTo( spot.Flat() ) > 3f ) continue;
			best = Sculpt.InBounds( ground ) with { Y = ground.Y + HoverHeight };
			break;
		}
		appearAt = best;
		SetMode( Mode.Vanish );
		PoseidonVortex.Burst( this, GlobalPosition + Vector3.Down * HoverHeight * 0.8f );
		Sfx.Play( "whirlpool", GlobalPosition, 1f, 0.8f );
	}

	// ---- Attacks ----

	Vector3 HandAt => hand.GlobalPosition;

	void ThrowTrident()
	{
		var prey = Target;
		if ( prey is null ) return;
		var from = HandAt + Vector3.Up * 1.2f;
		var aim = prey.WorldPosition + Vector3.Up * 1.1f;
		// Leads the player a little.
		var flight = from.DistanceTo( aim ) / TridentSpeed;
		aim += prey.Controller.Velocity * flight * 0.6f;
		PoseidonTrident.Throw( this, from, (aim - from).SafeNormal() * TridentSpeed, TridentDamage );
		heldTrident.Visible = false;
		GetTree().CreateTimer( 1.0 ).Timeout += () => { if ( IsInstanceValid( heldTrident ) ) heldTrident.Visible = true; };
		Sfx.Play( "harpoon", from, 1f, 0.7f );
	}

	void ThrowWhirlpool()
	{
		var prey = Target;
		if ( prey is null ) return;
		var start = GlobalPosition with { Y = Sculpt.FloorBelow( this, GlobalPosition, 80f ) };
		var toward = (prey.WorldPosition - start).Flat().SafeNormal();
		if ( toward == Vector3.Zero ) toward = -GlobalBasis.Z.Flat().SafeNormal();
		PoseidonVortex.Throw( this, start + toward * 3f, toward );
		Sfx.Play( "whirlpool", GlobalPosition, 0.9f, 1.1f );
	}

	void SummonSeahorses()
	{
		var director = WaveDirector.Current;
		var count = Math.Min( Rng.Int( 3, 4 ), MaxSeahorses - Seahorse.All.Count( s => !s.IsDead ) );
		for ( var i = 0; i < count; i++ )
		{
			var a = i / (float)count * MathF.Tau + Rng.Float( -0.3f, 0.3f );
			var at = Sculpt.InBounds( GlobalPosition + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * 3.5f );
			at.Y = Sculpt.FloorBelow( this, at, 80f ) + 1.6f;
			PoseidonVortex.Burst( this, at + Vector3.Down * 1.2f, 0.4f );
			var horse = Seahorse.Spawn( GetTree().CurrentScene, at, SeahorseHealth );
			if ( director is not null ) director.Adopt( horse );
		}
	}

	/// <summary> A whirlpool caught the player: it brings them to him and he stabs them. </summary>
	public bool Grab( PlayerSurvivor player )
	{
		if ( mode is Mode.Dead or Mode.Vanish or Mode.Appear or Mode.Stab || player?.Controller is null || !player.IsAlive ) return false;
		grabbed = player;
		grabFrom = player.Controller.GlobalPosition;
		stabbed = false;
		player.Controller.Scripted = true;
		player.Controller.Velocity = Vector3.Zero;
		SetMode( Mode.Stab );
		return true;
	}

	void Stab( float dt )
	{
		var c = grabbed?.Controller;
		if ( c is null || !IsInstanceValid( c ) || !grabbed.IsAlive ) { Release(); SetMode( Mode.Swim ); return; }
		// Carried up to just in front of him (his height), then the thrust.
		var front = GlobalPosition + -GlobalBasis.Z.Flat().SafeNormal() * 2.6f * Size / 1.5f + Vector3.Down * 1.2f;
		front = front with { Y = MathF.Max( front.Y, Sculpt.FloorBelow( this, front, 80f ) + 0.1f ) };
		var carry = MathX.Clamp( (float)inMode / 1.0f, 0f, 1f );
		var ease = carry * carry * (3f - 2f * carry);
		c.GlobalPosition = grabFrom.Lerp( front, ease ) + Vector3.Up * MathF.Sin( carry * MathF.PI ) * 1.5f;
		c.Velocity = Vector3.Zero;
		c.ResetPhysicsInterpolation();
		Face( c.GlobalPosition - GlobalPosition, 6f, dt );
		if ( !stabbed && inMode >= 1.25f )
		{
			stabbed = true;
			Stabs++;
			grabbed.Health?.OnDamage( new DamageInfo { Damage = StabDamage, Attacker = this, Position = c.GlobalPosition + Vector3.Up * 1.1f, Origin = HandAt } );
			Sfx.Play( "hit_flesh", c.GlobalPosition, 1f, 0.7f );
			c.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 1f );
			var push = (c.GlobalPosition - GlobalPosition).Flat().SafeNormal();
			Release();
			c.Velocity = push * 9f + Vector3.Up * 3f;
			SetMode( Mode.Swim );
			nextAttack = Rng.Float( 2.5f, 4f );
		}
	}

	void Release()
	{
		if ( grabbed?.Controller is { } c && IsInstanceValid( c ) ) c.Scripted = false;
		grabbed = null;
	}

	// ---- Looks ----

	void Animate( float t, float dt )
	{
		body.Scale = Vector3.One * Size * MathF.Max( 0.02f, shown );
		var flash = MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f );
		skin.AlbedoColor = Skin.Lerp( Colors.White * 2.2f, flash ) with { A = 1f };
		scales.AlbedoColor = Scales.Lerp( Colors.White * 2.2f, flash ) with { A = 1f };
		if ( mode == Mode.Dead ) return;
		// Swimming: the tail sweeps in a wave down its length, the body bobbing and rocking with it.
		var speed = mode == Mode.Swim ? 1f + velocity.Length() * 0.15f : 0.7f;
		for ( var i = 0; i < tail.Count; i++ )
			tail[i].Rotation = new Vector3( MathF.Sin( t * 2.2f * speed - i * 0.55f ) * (0.12f + i * 0.025f) + 0.06f, 0f, MathF.Sin( t * 1.1f - i * 0.4f ) * 0.04f );
		body.Position = new Vector3( 0f, MathF.Sin( t * 1.4f ) * 0.12f, 0f );
		body.Rotation = new Vector3( MathF.Sin( t * 2.2f * speed + 1.2f ) * 0.05f, 0f, MathF.Sin( t * 0.9f ) * 0.04f );

		// Arms: the trident held up at the ready; drawn back to throw; both arms raised to call up the sea; thrust to stab.
		var raiseR = new Vector3( -0.4f, 0f, 2.6f );     // right arm up and out, trident aloft
		var raiseL = new Vector3( 0.2f, 0f, -0.5f );     // left fist out
		var foreR0 = new Vector3( 0.5f, 0f, 0f );
		var foreL0 = new Vector3( 0.9f, 0f, 0f );     // elbows bend forward (+X turns the forearm toward -Z)
		var u = MathX.Clamp( (float)inMode / 0.6f, 0f, 1f );
		switch ( mode )
		{
			case Mode.WindUp:
				raiseR = raiseR.Lerp( new Vector3( 0.6f, 0f, 2.9f ), u );   // back
				foreR0 = foreR0.Lerp( new Vector3( 1.1f, 0f, 0f ), u );
				break;
			case Mode.Summon:
				raiseR = raiseR.Lerp( new Vector3( 0f, 0f, 2.9f ), u );
				raiseL = raiseL.Lerp( new Vector3( 0f, 0f, -2.9f ), u );
				foreL0 = foreL0.Lerp( Vector3.Zero, u );
				break;
			case Mode.Stab:
				var thrust = MathX.Clamp( ((float)inMode - 1.0f) / 0.25f, 0f, 1f );
				raiseR = raiseR.Lerp( new Vector3( 1.45f, 0f, 0.15f ), thrust );   // straight out in front (+X about the shoulder swings the arm to -Z)
				foreR0 = foreR0.Lerp( Vector3.Zero, thrust );
				break;
		}
		var k = 1f - MathF.Exp( -10f * dt );
		armR.Rotation = armR.Rotation.Lerp( raiseR + new Vector3( MathF.Sin( t * 1.3f ) * 0.05f, 0f, 0f ), k );
		foreR.Rotation = foreR.Rotation.Lerp( foreR0, k );
		armL.Rotation = armL.Rotation.Lerp( raiseL + new Vector3( MathF.Sin( t * 1.1f + 1f ) * 0.08f, 0f, 0f ), k );
		foreL.Rotation = foreL.Rotation.Lerp( foreL0, k );
		head.Rotation = new Vector3( MathF.Sin( t * 0.7f ) * 0.05f, MathF.Sin( t * 0.5f ) * 0.08f, 0f );
	}

	// ---- Hits and death ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		var multiplier = index == 0 ? HeadMultiplier : index >= 3 ? TailMultiplier : 1f;
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * multiplier, Position = position, Origin = origin, Attacker = shooter } );
		gore?.GooBurst( position, -direction, GooColor, 2, 160f, 0.6f );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, index == 0, scoreHit );
	}

	protected override void OnDied()
	{
		Release();
		SetMode( Mode.Dead );
		RaiseKilled();
		Sfx.Play2D( "boss_spawn", 0.7f, 0.6f );
		for ( var i = 0; i < 8; i++ )
			gore?.ThrowBlob( GlobalPosition + Rng.InSphere( 1.5f ), lastShotDirection + Vector3.Up, i % 3 == 0 ? Scales : Skin, GooColor, 10f );
		gore?.GooBurst( GlobalPosition, Vector3.Up, GooColor, 30, 420f, 2f );
		PoseidonVortex.Burst( this, GlobalPosition + Vector3.Down * HoverHeight * 0.8f, 1.6f );
		foreach ( var part in FindChildren( "*", "StaticBody3D", true, false ).OfType<CreaturePart>().ToList() ) part.QueueFree();
	}

	/// <summary> Dead: he slumps and sinks into a last whirlpool, shrinking away. </summary>
	void Sink( float dt )
	{
		var t = MathX.Clamp( (float)timeSinceDeath / 3f, 0f, 1f );
		RotateY( dt * (1f + t * 6f) );
		GlobalPosition += Vector3.Down * dt * 1.2f;
		shown = MathX.Lerp( 1f, 0.05f, t * t );
		if ( t >= 1f ) QueueFree();
	}
}

/// <summary>
/// A trident thrown by Poseidon: flies at the player with a slight drop, turning about its shaft, streaming the same
/// bubbles as the player's trident shooter. Hurts a player it passes close to; sticks in the world and shrinks away.
/// </summary>
public partial class PoseidonTrident : Node3D
{
	Vector3 velocity;
	float damage;
	float roll;
	bool stuck;
	TimeSince age;
	TimeSince sinceStuck;
	Node3D thrower;

	public static void Throw( Node3D thrower, Vector3 from, Vector3 velocity, float damage )
	{
		var t = new PoseidonTrident { Name = "Poseidon Trident", velocity = velocity, damage = damage, thrower = thrower };
		thrower.GetTree().CurrentScene.AddChild( t );
		t.GlobalPosition = from;
		var look = new Node3D { Name = "Model" };
		t.AddChild( look );
		look.Scale = Vector3.One * 1.8f;
		TridentModel.BuildTrident( look );
		t.Face();
		t.age = 0f;
	}

	void Face()
	{
		var f = velocity.SafeNormal();
		if ( f != Vector3.Zero ) GlobalBasis = Shapes.LookAt( f ) * new Basis( Vector3.Back, roll );
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		if ( stuck )
		{
			if ( sinceStuck > 2.5f ) { Scale = Vector3.One * MathX.Clamp( 1f - ((float)sinceStuck - 2.5f) * 2f, 0.01f, 1f ); if ( sinceStuck > 3f ) QueueFree(); }
			return;
		}
		if ( DevFlags.FreezeEnemies ) return;
		var from = GlobalPosition;
		velocity += Vector3.Down * 1f * dt;
		var to = from + velocity * dt;
		// A player in the way: the trident is long (1.7 m), so anywhere near the line counts.
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive ) continue;
			var chest = player.WorldPosition + Vector3.Up * 1f;
			var closest = Geometry3D.GetClosestPointToSegment( chest, from, to );
			if ( closest.DistanceTo( chest ) > 0.8f ) continue;
			player.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = IsInstanceValid( thrower ) ? thrower : this, Position = closest, Origin = from } );
			player.Controller.Velocity += velocity.SafeNormal() * 4f;
			Sfx.Play( "hit_flesh", closest, 0.9f, 0.8f );
			QueueFree();
			return;
		}
		var hit = Trace.Ray( this, from, to, Layers.World );
		if ( hit.Hit )
		{
			stuck = true;
			sinceStuck = 0f;
			GlobalPosition = hit.HitPosition - velocity.SafeNormal() * 0.5f;
			Fx.ImpactWorld( hit.HitPosition, hit.Normal );
			return;
		}
		GlobalPosition = to;
		roll += 5f * dt;
		Face();
		if ( TridentBolt.UnderWater( to ) ) TridentBolt.Bubbles( from, to );
		if ( age > 4f ) QueueFree();
	}
}

/// <summary>
/// Poseidon's whirlpool: a spinning funnel of water rings and bubbles. Thrown, it travels along the floor toward the
/// player for a few seconds, dragging them in when they are close; one that reaches them carries them to Poseidon for
/// his stab. A burst is the short one he vanishes and appears through (and calls seahorses out of).
/// </summary>
public partial class PoseidonVortex : Node3D
{
	public const float PullRadius = 6f;
	public const float CatchRadius = 1.8f;
	public const float Speed = 5.5f;
	public const float Life = 7f;

	Poseidon owner;
	Vector3 heading;
	bool travelling;
	float size = 1f;
	float life;
	TimeSince age;
	readonly List<Node3D> rings = new();
	static StandardMaterial3D ringMaterial, coreMaterial;

	public static void Throw( Poseidon owner, Vector3 at, Vector3 heading ) => Make( owner, at, heading, true, 1f, Life );

	/// <summary> A short burst at a spot (Poseidon vanishing or appearing, a seahorse arriving). </summary>
	public static void Burst( Node3D source, Vector3 at, float size = 1f ) => Make( source as Poseidon, at, Vector3.Zero, false, size, 1.6f );

	static void Make( Poseidon owner, Vector3 at, Vector3 heading, bool travelling, float size, float life )
	{
		var scene = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( scene is null ) return;
		var v = new PoseidonVortex { Name = "Poseidon Whirlpool", owner = owner, heading = heading, travelling = travelling, size = size, life = life };
		scene.AddChild( v );
		v.GlobalPosition = at;
		v.age = 0f;
	}

	public override void _Ready()
	{
		ringMaterial ??= new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.45f, 0.95f, 1f, 0.45f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		coreMaterial ??= new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.55f, 0.25f, 1f, 0.35f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		// Rings widening upward round a purple funnel, each turning at its own speed.
		for ( var i = 0; i < 5; i++ )
		{
			var ring = new Node3D { Name = $"Ring {i}" };
			AddChild( ring );
			var r = 0.6f + i * 0.45f;
			ring.Position = Vector3.Up * (0.3f + i * 0.55f);
			ring.AddChild( new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = r - 0.08f, OuterRadius = r, Rings = 24, RingSegments = 6 }, MaterialOverride = ringMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Rotation = new Vector3( 0.15f, 0f, 0f ) } );
			rings.Add( ring );
		}
		AddChild( new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 2.4f, BottomRadius = 0.3f, Height = 2.8f, RadialSegments = 16, Rings = 1, CapTop = false, CapBottom = false }, MaterialOverride = coreMaterial, Position = Vector3.Up * 1.4f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off } );
		Scale = Vector3.One * 0.05f;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		var t = (float)age;
		// Grows in, holds, and dies away.
		var grow = MathX.Clamp( t / 0.3f, 0f, 1f ) * MathX.Clamp( (life - t) / 0.4f, 0f, 1f );
		Scale = Vector3.One * MathF.Max( 0.02f, size * grow );
		for ( var i = 0; i < rings.Count; i++ ) rings[i].RotateY( dt * (6f + i * 2f) * (i % 2 == 0 ? 1f : -1f) );
		if ( Rng.Float() < 0.6f )
		{
			var a = Rng.Float( 0f, MathF.Tau );
			var h = Rng.Float( 0f, 2.6f ) * size;
			WaterBubble.Create( GlobalPosition + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * (0.4f + h * 0.7f) + Vector3.Up * h, Rng.Float( 0.3f, 0.8f ), 0f, Rng.Float( 0.5f, 1f ) );
		}
		if ( t >= life ) { QueueFree(); return; }
		if ( !travelling || DevFlags.FreezeEnemies ) return;

		// Along the floor toward where it was thrown, kept inside the map.
		var next = GlobalPosition + heading * Speed * dt;
		next.Y = Sculpt.FloorBelow( this, next + Vector3.Up * 2f, 10f );
		GlobalPosition = Sculpt.InBounds( next );
		foreach ( var player in PlayerSurvivor.All )
		{
			if ( !player.IsAlive || player.Controller is not { } c || c.Scripted ) continue;
			var offset = (GlobalPosition - c.GlobalPosition).Flat();
			var d = offset.Length();
			if ( d > PullRadius ) continue;
			// Dragged in, harder the closer they are; caught at the middle.
			c.Velocity += offset.SafeNormal() * (1f - d / PullRadius) * 22f * dt;
			if ( d < CatchRadius && owner is not null && IsInstanceValid( owner ) && owner.Grab( player ) )
			{
				Sfx.Play2D( "whirlpool", 1f );
				c.GetNodeOrNull<CameraJuice>( "CameraJuice" )?.Shake( 0.6f );
				life = MathF.Min( life, t + 0.5f );
				travelling = false;
				return;
			}
		}
	}
}
