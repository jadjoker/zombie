using Godot;

/// <summary>
/// Seahorse (Poseidon's summons, playtest): a small alien seahorse with one big yellow eye like MC61's. Green body with
/// yellow belly rings, a purple crest and fins, a black snout and a curling black-tipped tail. Much faster than the
/// other enemies but weak: it darts in, bites, peels away and comes again. Swims upright, bobbing, the fin on its back
/// fluttering and the tail curling. Built in code (Sculpt), metres. Its head is the weak spot.
/// </summary>
public partial class Seahorse : Creature
{
	public static readonly List<Seahorse> All = new();

	public float Speed = 7.5f;           // m/s: the others swim about 3 to 5
	public float BiteDamage = 6f;
	public float BiteRange = 1.3f;
	public float BiteCooldown = 1.1f;
	public float HoverHeight = 1.2f;     // above the floor, where the player's chest is
	public float HeadMultiplier = 1.5f;

	static readonly Color Green = new( 0.24f, 0.78f, 0.3f );
	static readonly Color Purple = new( 0.5f, 0.15f, 0.78f );
	static readonly Color Yellow = new( 1f, 0.82f, 0.12f );
	static readonly Color Black = new( 0.035f, 0.035f, 0.045f );
	static readonly Color GooColor = new( 0.4f, 1f, 0.35f );

	Node3D body, tail, fin;
	StandardMaterial3D skin;
	Vector3 velocity;
	TimeSince sinceBite = 99f;
	TimeSince sinceHit = 99f;
	TimeSince sinceFloor = 99f;
	float floorY;
	float phase;
	float weave;

	public static Seahorse Spawn( Node parent, Vector3 position, float maxHealth ) =>
		Place( new Seahorse { Name = "Seahorse" }, parent, position, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	protected override void Setup()
	{
		BeginSetup( 25f );
		phase = Rng.Float( 0f, MathF.Tau );
		weave = Rng.Float() < 0.5f ? -1f : 1f;
		floorY = Sculpt.FloorBelow( this, GlobalPosition );
		Build();
	}

	void Build()
	{
		skin = Sculpt.Paint( Green );
		var belly = Sculpt.Paint( Yellow );
		var dark = Sculpt.Paint( Black, 0.35f );
		var crest = Sculpt.Paint( Purple );
		var fins = Sculpt.Membrane( Purple, 0.7f );
		body = new Node3D { Name = "Body" };
		AddChild( body );

		// The upright S of the body (facing -Z): chest, belly, then the tail curling forward underneath.
		var spine = new[] { new Vector3( 0f, 0.32f, 0.03f ), new Vector3( 0f, 0.14f, -0.02f ), new Vector3( 0f, -0.03f, 0f ), new Vector3( 0f, -0.18f, 0.05f ) };
		var radii = new[] { 0.15f, 0.16f, 0.14f, 0.11f };
		for ( var i = 0; i < spine.Length; i++ )
		{
			Sculpt.Blob( body, spine[i], new Vector3( radii[i] * 1.7f, radii[i] * 2.3f, radii[i] * 2f ), skin );
			Sculpt.Blob( body, spine[i] + new Vector3( 0f, 0f, -radii[i] * 0.8f ), new Vector3( radii[i] * 1.3f, radii[i] * 0.5f, radii[i] * 0.8f ), belly );   // belly ring
			Sculpt.Cone( body, spine[i] + new Vector3( 0f, 0f, radii[i] * 0.85f ), spine[i] + new Vector3( 0f, 0.04f, radii[i] * 0.85f + 0.09f ), 0.035f, crest );   // back spike
		}

		// The head, leaning forward, with its long black snout, purple crown spikes and one big eye.
		var head = new Vector3( 0f, 0.53f, -0.03f );
		Sculpt.Blob( body, head, new Vector3( 0.24f, 0.22f, 0.27f ), skin, name: "Head" );
		var snoutTip = head + new Vector3( 0f, -0.1f, -0.33f );
		Sculpt.Limb( body, head + new Vector3( 0f, -0.02f, -0.1f ), snoutTip, 0.06f, 0.035f, dark );
		Sculpt.Blob( body, snoutTip, new Vector3( 0.085f, 0.07f, 0.06f ), dark );
		for ( var i = 0; i < 3; i++ )
			Sculpt.Cone( body, head + new Vector3( (i - 1) * 0.05f, 0.08f, 0.04f + i * 0.02f ), head + new Vector3( (i - 1) * 0.08f, 0.24f, 0.1f + i * 0.03f ), 0.03f, crest );
		var eyeAt = head + new Vector3( 0f, 0.04f, -0.1f );
		Sculpt.Blob( body, eyeAt, new Vector3( 0.17f, 0.17f, 0.12f ), dark );                                         // socket
		Sculpt.Blob( body, eyeAt + new Vector3( 0f, 0f, -0.035f ), new Vector3( 0.14f, 0.14f, 0.1f ), Sculpt.Paint( new Color( 0.92f, 1f, 0.85f ), 0.2f ) );
		Sculpt.Blob( body, eyeAt + new Vector3( 0f, 0f, -0.075f ), new Vector3( 0.095f, 0.095f, 0.03f ), Sculpt.Glow( Yellow, 2.2f ) );
		Sculpt.Block( body, eyeAt + new Vector3( 0f, 0f, -0.09f ), new Vector3( 0.018f, 0.075f, 0.01f ), dark );     // slit pupil

		// Small side fins by the head, and the fin on its back that flutters.
		foreach ( var s in new[] { -1f, 1f } )
			Sculpt.Block( body, head + new Vector3( s * 0.12f, -0.1f, 0.06f ), new Vector3( 0.01f, 0.08f, 0.1f ), fins, new Basis( Vector3.Up, s * 0.5f ) );
		fin = Sculpt.Joint( body, new Vector3( 0f, 0.18f, 0.15f ), "Fin" );
		Sculpt.Block( fin, new Vector3( 0f, 0f, 0.07f ), new Vector3( 0.012f, 0.24f, 0.14f ), fins );

		// The tail, curling forward under the belly, its tip black.
		tail = Sculpt.Joint( body, new Vector3( 0f, -0.25f, 0.07f ), "Tail" );
		var curl = new[] { new Vector3( 0f, -0.08f, 0.04f ), new Vector3( 0f, -0.18f, 0.03f ), new Vector3( 0f, -0.25f, -0.04f ), new Vector3( 0f, -0.24f, -0.12f ), new Vector3( 0f, -0.17f, -0.15f ), new Vector3( 0f, -0.12f, -0.11f ) };
		for ( var i = 0; i < curl.Length; i++ )
		{
			var r = 0.085f - i * 0.011f;
			Sculpt.Blob( tail, curl[i], Vector3.One * r * 2f, i >= curl.Length - 2 ? dark : skin );
			if ( i % 2 == 0 ) Sculpt.Blob( tail, curl[i] + new Vector3( 0f, 0f, -r * 0.6f ), new Vector3( r * 1.4f, r * 0.5f, r ), belly );
		}

		// What can be shot: the head (index 0, the weak spot) and the body.
		var headPart = CreaturePart.Create( this, 0, "Seahorse Head", 0.17f );
		body.AddChild( headPart );
		headPart.Position = head;
		var bodyPart = CreaturePart.Create( this, 1, "Seahorse Body", 0.22f );
		body.AddChild( bodyPart );
		bodyPart.Position = new Vector3( 0f, 0.08f, 0f );
	}

	public override void _Process( double delta )
	{
		if ( health is null || body is null ) return;
		var dt = (float)delta;
		var t = (float)Clock.Now + phase;

		// Swimming: a quick bob, the fin fluttering, the tail curling and uncurling.
		body.Position = new Vector3( 0f, MathF.Sin( t * 4.2f ) * 0.05f, 0f );
		fin.Rotation = new Vector3( 0f, MathF.Sin( t * 22f ) * 0.45f, 0f );
		tail.Rotation = new Vector3( MathF.Sin( t * 3f ) * 0.3f, 0f, 0f );
		var flash = MathX.Clamp( 1f - (float)sinceHit / 0.1f, 0f, 1f );
		skin.AlbedoColor = Green.Lerp( Colors.White * 2.5f, flash ) with { A = 1f };

		if ( IsDead || DevFlags.FreezeEnemies ) return;
		if ( sinceFloor > 0.25f ) { sinceFloor = 0f; floorY = Sculpt.FloorBelow( this, GlobalPosition + Vector3.Up * 0.5f ); }

		var prey = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var wish = Vector3.Zero;
		if ( prey is not null )
		{
			var aim = prey.WorldPosition + Vector3.Up * 1.1f;
			var to = aim - GlobalPosition;
			var distance = to.Length();
			var dir = to / MathF.Max( 0.01f, distance );
			if ( sinceBite < 0.55f ) wish = -dir * Speed * 0.75f + Vector3.Up * 2f;   // peel away after a bite
			else
			{
				// Darting in on a weaving line, straight at the end.
				var side = Vector3.Up.Cross( dir ).SafeNormal() * weave * MathF.Sin( t * 2.6f ) * MathX.Clamp( distance / 6f, 0f, 1f );
				wish = (dir + side * 0.6f).SafeNormal() * Speed;
			}
			if ( distance < BiteRange && sinceBite > BiteCooldown && prey.Health is { } h )
			{
				sinceBite = 0f;
				h.OnDamage( new DamageInfo { Damage = BiteDamage, Attacker = this, Position = aim, Origin = GlobalPosition } );
				Sfx.Play( "creature_attack", GlobalPosition, 0.6f, Rng.Float( 1.4f, 1.7f ) );
			}
			Face( dir, 8f, dt );
		}
		velocity = velocity.Lerp( wish, 1f - MathF.Exp( -3.5f * dt ) );
		// Over the floor, round walls (rising over them), inside the map.
		var next = GlobalPosition + velocity * dt;
		var ahead = Trace.Ray( this, GlobalPosition, next + velocity.SafeNormal() * 0.6f, Layers.World );
		if ( ahead.Hit ) { velocity = velocity.Slide( ahead.Normal ) + Vector3.Up * 2.5f; next = GlobalPosition + velocity * dt; }
		next.Y = MathF.Max( next.Y, floorY + HoverHeight * 0.5f );
		if ( next.Y < floorY + HoverHeight ) velocity.Y += 6f * dt;
		GlobalPosition = Sculpt.InBounds( next );
	}

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		velocity += direction.SafeNormal() * 4f;   // light: a hit knocks it back
		var head = index == 0;
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * (head ? HeadMultiplier : 1f), Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, head, scoreHit );
	}

	protected override void OnDied()
	{
		RaiseKilled();
		// Pops: goo and a few loose pieces of it.
		gore?.GooBurst( GlobalPosition, lastShotDirection, GooColor, 12, 260f, 0.8f );
		for ( var i = 0; i < 3; i++ ) gore?.ThrowBlob( GlobalPosition + Rng.InSphere( 0.2f ), lastShotDirection, i == 0 ? Yellow : Green, GooColor, 3.5f );
		QueueFree();
	}
}
