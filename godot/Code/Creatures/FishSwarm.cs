using Godot;

/// <summary>
/// A school of small fish as one enemy (user's ask): the whole swarm is one creature with one health bar, and as it
/// takes damage more and more of its fish die (the share alive follows its health; the one hit goes first, then the
/// nearest), each dead fish floating off as a little body with a puff of goo. Left alone it circles its home on a lazy
/// loop like the old decorative schools; a player close by (or a hit) sets it on them: it streams over and wraps round
/// them, nipping (`BiteDamage`, less as it thins out), until they get away or it is wiped out. A dead swarm comes back
/// after a while (`RespawnAfter`). Online the host runs them (joiners get puppets), as every creature.
/// </summary>
public partial class FishSwarm : Creature
{
	public const float FishHealth = 9f, AggroRange = 12f, LeashRange = 34f, Speed = 5.5f, BiteRange = 1.7f, BiteEvery = 0.55f, BiteDamage = 3f;
	public const float RespawnMin = 50f, RespawnMax = 90f;

	public Vector3 Home;
	public float Radius = 10f, LoopSpeed = 0.14f, Phase;
	public Color Colour = new( 1f, 0.8f, 0.2f );
	public int Count = 14;

	/// <summary> Tests: fish killed, bites that landed, swarms set on a player. </summary>
	public static int FishKilled, Bites, Aggros;
	public static readonly List<FishSwarm> All = new();

	MultiMeshInstance3D view;
	Mesh mesh;
	Vector3[] offsets, positions;
	float[] phases, sizes;
	bool[] alive;
	readonly List<CreaturePart> parts = new();
	bool aggro;
	PlayerSurvivor prey;
	TimeSince sinceBite = 9f, sinceHurt = 99f;
	float loopAngle, swirl;
	Vector3 centre;
	int seed;

	public bool Aggro => aggro;
	public int Alive { get { var n = 0; if ( alive is not null ) foreach ( var a in alive ) if ( a ) n++; return n; } }
	public Vector3 Centre => centre;

	public override GibRecipe Gibs => GibRecipes.Mermaid( new Color( 0.85f, 0.2f, 0.25f ), Colour );
	public override bool WeakSpot( int index ) => false;

	public static FishSwarm Spawn( Node parent, Vector3 home, Color colour, int count, float radius, float loopSpeed, float phase, int seed = 0 )
	{
		var s = new FishSwarm { Name = "Fish Swarm", Home = home, Colour = colour, Count = count, Radius = radius, LoopSpeed = loopSpeed, Phase = phase, seed = seed };
		return Place( s, parent, home, count * FishHealth );
	}

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	protected override void Setup()
	{
		BeginSetup( Count * FishHealth );
		health.Damaged += d => { Thin( d.Position ); Provoke( d.Attacker as PlayerSurvivor ?? (d.Attacker as PlayerController)?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) ); };
		mesh = FishSchool.Painted( Colour );
		var random = new Random( seed == 0 ? (int)(Phase * 1000f) : seed );
		offsets = new Vector3[Count]; positions = new Vector3[Count]; phases = new float[Count]; sizes = new float[Count]; alive = new bool[Count];
		view = new MultiMeshInstance3D
		{
			Name = "Fish", TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = Count },
			CustomAabb = new Aabb( -Vector3.One * 80f, Vector3.One * 160f ),
		};
		AddChild( view );
		view.GlobalTransform = Transform3D.Identity;
		for ( var i = 0; i < Count; i++ )
		{
			offsets[i] = new Vector3( (float)random.NextDouble() * 2.4f - 1.2f, (float)random.NextDouble() * 1f - 0.5f, (float)random.NextDouble() * 3.2f - 1.6f );
			phases[i] = (float)random.NextDouble() * MathF.Tau;
			sizes[i] = 0.8f + (float)random.NextDouble() * 0.7f;
			alive[i] = true;
			var part = CreaturePart.Create( this, i, "Fish", 0.22f * sizes[i] );
			part.TopLevel = true;
			AddChild( part );
			parts.Add( part );
		}
		loopAngle = Phase;
		centre = GlobalPosition;
		Layout( 0f );
	}

	public override void _Process( double delta )
	{
		if ( health is null || view is null ) return;
		var dt = MathF.Min( (float)delta, 0.1f );
		// Online, a joiner's own copy from the level's dressing goes: the host's swarm comes over as a puppet.
		if ( Net.Online && !Net.IsHost && !Puppet ) { QueueFree(); return; }
		if ( Puppet ) centre = GlobalPosition;
		else if ( !IsDead && !Frozen ) Think( dt );
		Layout( dt );
	}

	void Think( float dt )
	{
		if ( !aggro )
		{
			// The lazy loop round home; a player close by sets it on them.
			loopAngle += LoopSpeed * dt;
			centre = Home + new Vector3( MathF.Cos( loopAngle ) * Radius, MathF.Sin( loopAngle * 2f ) * 0.7f, MathF.Sin( loopAngle ) * Radius * 0.7f );
			var near = PlayerSurvivor.FindNearestAlive( centre );
			if ( near is not null && near.WorldPosition.DistanceTo( centre ) < AggroRange ) Provoke( near );
		}
		else
		{
			if ( prey is null || !IsInstanceValid( prey ) || !prey.IsAlive || (prey.WorldPosition.DistanceTo( Home ) > LeashRange && sinceHurt > 6f) )
			{
				// Lost them: back to the loop (from where it is, easing home).
				aggro = false;
				prey = null;
				loopAngle = MathF.Atan2( (centre - Home).Z / 0.7f, (centre - Home).X );
			}
			else
			{
				var chest = prey.WorldPosition + Vector3.Up * 1.1f;
				var to = (prey.WorldPosition + Vector3.Up * 0.8f) - centre;
				var d = to.Length();
				centre += to.SafeNormal() * MathF.Min( d, Speed * dt );
				if ( d < BiteRange && sinceBite > BiteEvery )
				{
					sinceBite = 0f;
					var share = 0.4f + 0.6f * Alive / (float)Count;
					prey.Health?.OnDamage( new DamageInfo { Damage = BiteDamage * share, Attacker = this, Position = chest, Origin = centre } );
					Bites++;
					Sfx.Play( "crab_snap", chest, 0.35f, Rng.Float( 1.6f, 2f ) );
				}
			}
		}
		GlobalPosition = centre;
	}

	void Provoke( PlayerSurvivor who )
	{
		sinceHurt = 0f;
		if ( Puppet || IsDead ) return;
		who ??= PlayerSurvivor.FindNearestAlive( centre );
		if ( who is null ) return;
		if ( !aggro ) Aggros++;
		aggro = true;
		prey = who;
	}

	/// <summary> Each fish's place: in formation on the loop, swirling round the player when on them. </summary>
	void Layout( float dt )
	{
		var t = (float)Clock.Now;
		swirl += dt * (aggro ? 3.2f : 0f);
		var heading = aggro && prey is not null && IsInstanceValid( prey ) ? (prey.WorldPosition + Vector3.Up * 1.1f - centre).SafeNormal() : new Vector3( -MathF.Sin( loopAngle ), 0f, MathF.Cos( loopAngle ) * 0.7f ).SafeNormal();
		if ( heading == Vector3.Zero ) heading = Vector3.Forward;
		var facing = Shapes.LookAt( heading ) * new Basis( Vector3.Up, MathF.PI );
		// On a player they circle the body at arm's length, below the eyes (they used to swarm the camera and hide it).
		var ring = aggro && prey is not null && IsInstanceValid( prey ) && (prey.WorldPosition + Vector3.Up * 0.8f).DistanceTo( centre ) < 3f;
		for ( var i = 0; i < Count; i++ )
		{
			if ( !alive[i] ) { view.Multimesh.SetInstanceTransform( i, new Transform3D( Basis.FromScale( Vector3.One * 0.001f ), centre ) ); continue; }
			var bob = new Vector3( 0f, MathF.Sin( t * 1.3f + phases[i] ) * 0.12f, MathF.Sin( t * 0.7f + phases[i] * 2f ) * 0.25f );
			var wag = new Basis( Vector3.Up, MathF.Sin( t * (aggro ? 16f : 9f) + phases[i] ) * 0.25f );
			Vector3 at;
			var fishFacing = facing;
			if ( ring )
			{
				var a = swirl * (0.8f + 0.4f * (phases[i] / MathF.Tau)) + i * MathF.Tau / Count;
				var r = 1.25f + 0.35f * MathF.Sin( phases[i] * 3f ) + 0.12f * MathF.Sin( t * 3f + phases[i] );
				var round = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
				at = prey.WorldPosition + round * r + Vector3.Up * (0.45f + 0.7f * (offsets[i].Y + 0.5f) + bob.Y);
				fishFacing = Shapes.LookAt( new Vector3( -MathF.Sin( a ), 0f, MathF.Cos( a ) ) ) * new Basis( Vector3.Up, MathF.PI );   // nose along the circle
			}
			else at = centre + facing * (offsets[i] + bob);
			var basis = fishFacing * wag * Basis.FromScale( Vector3.One * sizes[i] );
			positions[i] = at;
			view.Multimesh.SetInstanceTransform( i, new Transform3D( basis, at ) );
			if ( i < parts.Count && IsInstanceValid( parts[i] ) ) parts[i].GlobalPosition = at;
		}
	}

	/// <summary> As its health falls, fish die: the share alive follows it (the one nearest the blow first). </summary>
	void Thin( Vector3 at )
	{
		if ( alive is null ) return;
		var want = IsDead ? 0 : (int)MathF.Ceiling( Count * MathX.Clamp( health.Current / MathF.Max( 1f, health.MaxHealth ), 0f, 1f ) );
		while ( Alive > want )
		{
			var best = -1;
			var bestD = float.MaxValue;
			for ( var i = 0; i < Count; i++ )
			{
				if ( !alive[i] ) continue;
				var d = positions[i].DistanceSquaredTo( at );
				if ( d < bestD ) { bestD = d; best = i; }
			}
			if ( best < 0 ) break;
			KillFish( best );
		}
	}

	void KillFish( int i )
	{
		alive[i] = false;
		FishKilled++;
		var at = positions[i];
		if ( i < parts.Count && IsInstanceValid( parts[i] ) ) parts[i].QueueFree();
		gore?.GooBurst( at, Vector3.Up, new Color( 0.85f, 0.2f, 0.25f ), 4, 120f, 0.5f );
		// The body floats off belly up.
		var body = new MeshInstance3D { Name = "Dead Fish", Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		GetTree().CurrentScene.AddChild( body );
		body.GlobalTransform = new Transform3D( new Basis( Vector3.Forward, MathF.PI ) * Basis.FromScale( Vector3.One * sizes[i] ), at );
		Gib.Attach( body, gore, 3f, 5f, 0.2f, Rng.InSphere( 1.5f ) + Vector3.Up, new Color( 0.85f, 0.2f, 0.25f ) );
		if ( Rng.Float() < 0.5f ) GibKit.Chips( Gibs, at, Vector3.Up, Rng.OnSphere(), 0.05f, 1 );
		Sfx.Play( "splat", at, 0.35f, Rng.Float( 1.4f, 1.8f ) );
	}

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( NetCreatures.Intercept( this, index, damage, direction, position, origin, shooter, scoreHit ) ) return;
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage, Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, false, scoreHit );
	}

	protected override void OnDied()
	{
		RaiseKilled();
		Thin( centre );
		gore?.GooBurst( centre, Vector3.Up, new Color( 0.85f, 0.2f, 0.25f ), 10, 200f, 0.8f );
		// Back later, at home (the host's copy brings a new puppet to the joiners).
		if ( !Puppet && GetParent() is Node parent )
		{
			var (home, colour, count, radius, speed, phase) = (Home, Colour, Count, Radius, LoopSpeed, Phase);
			parent.GetTree().CreateTimer( Rng.Float( RespawnMin, RespawnMax ) ).Timeout += () =>
			{
				if ( IsInstanceValid( parent ) && parent.IsInsideTree() && (!Net.Online || Net.IsHost) ) Spawn( parent, home, colour, count, radius, speed, phase );
			};
		}
		QueueFree();
	}

	public override void NetWrite( List<float> s ) => s.Add( aggro ? 1f : 0f );
	public override void NetRead( ReadOnlySpan<float> s ) { if ( s.Length > 0 ) aggro = s[0] > 0.5f; }
}
