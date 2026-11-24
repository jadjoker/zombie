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
	/// <summary> The swarm's fish are drawn this much smaller than the sea's decorative schools (user's ask: tiny). </summary>
	public const float Small = 0.5f;

	public Vector3 Home;
	public float Radius = 10f, LoopSpeed = 0.14f, Phase;
	public Color Colour = new( 1f, 0.8f, 0.2f );
	public int Count = 14;
	/// <summary> What most of its fish are (`FishLooks`): plain, undead or alien. </summary>
	public FishLooks.Kind Theme;
	public int Seed => seed;
	FishLooks.Kind[] kinds;
	readonly MultiMeshInstance3D[] views = new MultiMeshInstance3D[3];
	int[] slots;

	/// <summary> Tests: fish killed, bites that landed, swarms set on a player, dead fish torn apart. </summary>
	public static int FishKilled, Bites, Aggros, FishTorn;
	/// <summary> The share of dead fish that come apart (a head or a tail torn off). </summary>
	public const float TearChance = 0.5f;
	/// <summary> With so many fish, only this share of the dead become full ragdolls; the rest come apart into gore scraps. </summary>
	public const float RagdollShare = 0.35f;
	/// <summary> Tests: dead fish that became ragdolls. </summary>
	public static int FishRagdolls;
	/// <summary> Tests: dead fish that came apart into gore scraps. </summary>
	public static int FishScrapped;
	public static readonly List<FishSwarm> All = new();

	MultiMeshInstance3D view;
	Mesh mesh;
	/// <summary> Tests: undead fish whose brains came out when they died. </summary>
	public static int BrainsPopped;
	Vector3[] offsets, positions;
	Basis[] bases;
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

	public static FishSwarm Spawn( Node parent, Vector3 home, Color colour, int count, float radius, float loopSpeed, float phase, int seed = 0, FishLooks.Kind theme = FishLooks.Kind.Plain )
	{
		var s = new FishSwarm { Name = "Fish Swarm", Home = home, Colour = colour, Count = count, Radius = radius, LoopSpeed = loopSpeed, Phase = phase, seed = seed, Theme = theme };
		return Place( s, parent, home, count * FishHealth );
	}

	/// <summary> A wave swarm (`WaveDirector`, `EnemyKind.FishSwarm`): comes in from a spawn point already hunting the nearest player, never gives up, never comes back. </summary>
	public static FishSwarm SpawnHunter( Node parent, Vector3 at, Color colour, int count, float health, FishLooks.Kind theme = FishLooks.Kind.Plain )
	{
		// The seed fits a float exactly, so joiners rebuild the same mix of fish.
		var s = new FishSwarm { Name = "Fish Swarm", Home = at, Colour = colour, Count = count, Hunter = true, seed = Rng.Int( 1, 1 << 22 ), maxHp = health, Theme = theme };
		return Place( s, parent, at, health );
	}

	/// <summary> A wave enemy: hunts from the start, no leash, no respawn. </summary>
	public bool Hunter;
	float maxHp;

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	protected override void Setup()
	{
		BeginSetup( maxHp > 0f ? maxHp : Count * FishHealth );
		health.Damaged += d => { Thin( d.Position ); Provoke( d.Attacker as PlayerSurvivor ?? (d.Attacker as PlayerController)?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) ); };
		mesh = FishSchool.Painted( Colour );
		var random = new Random( seed == 0 ? (int)(Phase * 1000f) : seed );
		offsets = new Vector3[Count]; positions = new Vector3[Count]; bases = new Basis[Count]; phases = new float[Count]; sizes = new float[Count]; alive = new bool[Count];
		// One MultiMesh per kind of fish in it; each fish has a slot in its kind's.
		kinds = FishLooks.Mix( Theme, Count, random );
		slots = new int[Count];
		var counts = new int[3];
		for ( var i = 0; i < Count; i++ ) slots[i] = counts[(int)kinds[i]]++;
		for ( var k = 0; k < 3; k++ )
		{
			if ( counts[k] == 0 ) continue;
			views[k] = new MultiMeshInstance3D
			{
				Name = $"Fish {(FishLooks.Kind)k}", TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = FishLooks.Get( (FishLooks.Kind)k, Colour ) ?? mesh, InstanceCount = counts[k] },
				CustomAabb = new Aabb( -Vector3.One * 80f, Vector3.One * 160f ),
			};
			AddChild( views[k] );
			views[k].GlobalTransform = Transform3D.Identity;
		}
		view = views[(int)Theme] ?? views.First( v => v is not null );
		for ( var i = 0; i < Count; i++ ) sizes[i] = Small * (0.8f + (float)random.NextDouble() * 0.7f);
		Pack( random );
		for ( var i = 0; i < Count; i++ )
		{
			phases[i] = (float)random.NextDouble() * MathF.Tau;
			alive[i] = true;
			var part = CreaturePart.Create( this, i, "Fish", 0.16f + 0.1f * sizes[i] );   // a little bigger than the fish, so the tiny ones can be hit
			part.TopLevel = true;
			AddChild( part );
			parts.Add( part );
		}
		loopAngle = Phase;
		centre = GlobalPosition;
		Layout( 0f );
	}

	/// <summary>
	/// Places the fish in a tight shoal (a squashed ball, user's ask) with none on top of another: each fish keeps its
	/// neighbours outside an ellipsoid the size of both fish plus room for their wag and bob. If a fish finds no room the
	/// ball grows a little.
	/// </summary>
	void Pack( Random random )
	{
		var grow = 1f;
		for ( var i = 0; i < Count; i++ )
		{
			for ( var tries = 0; ; tries++ )
			{
				if ( tries > 0 && tries % 60 == 0 ) grow *= 1.05f;
				var o = new Vector3( (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f );
				if ( o.LengthSquared() > 1f ) continue;
				var at = new Vector3( o.X * 0.5f, o.Y * 0.28f, o.Z * 0.75f ) * grow;
				var clear = true;
				for ( var j = 0; j < i && clear; j++ )
				{
					var both = sizes[i] + sizes[j];
					var d = at - offsets[j];
					var q = new Vector3( d.X / (0.04f * both + 0.05f), d.Y / (0.095f * both + 0.02f), d.Z / (0.165f * both + 0.04f) );
					clear = q.LengthSquared() >= 1f;
				}
				if ( !clear ) continue;
				offsets[i] = at;
				break;
			}
		}
	}

	/// <summary> Tests: pairs of fish in the shoal whose bodies overlap (ignoring the room Pack leaves for the wag). </summary>
	public int Crowded()
	{
		var n = 0;
		for ( var i = 0; i < Count; i++ )
			for ( var j = 0; j < i; j++ )
			{
				var both = sizes[i] + sizes[j];
				var d = offsets[i] - offsets[j];
				var q = new Vector3( d.X / (0.035f * both), d.Y / (0.07f * both), d.Z / (0.13f * both) );
				if ( q.LengthSquared() < 1f ) n++;
			}
		return n;
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
		if ( Hunter && !aggro ) Provoke( null );
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
			if ( prey is null || !IsInstanceValid( prey ) || !prey.IsAlive || (!Hunter && prey.WorldPosition.DistanceTo( Home ) > LeashRange && sinceHurt > 6f) )
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
			var mm = views[(int)kinds[i]].Multimesh;
			if ( !alive[i] ) { mm.SetInstanceTransform( slots[i], new Transform3D( Basis.FromScale( Vector3.One * 0.001f ), centre ) ); continue; }
			var bob = new Vector3( 0f, MathF.Sin( t * 1.3f + phases[i] ) * 0.015f, MathF.Sin( t * 0.7f + phases[i] * 2f ) * 0.03f );   // small: Pack leaves room for it
			var wag = new Basis( Vector3.Up, MathF.Sin( t * (aggro ? 16f : 9f) + phases[i] ) * 0.2f );
			Vector3 at;
			var fishFacing = facing;
			if ( ring )
			{
				// The shoal stays together and circles the body as one tight knot of fish.
				// The shoal's own layout bent round the body (along the circle by arc length), so the spacing holds.
				var r = 1.25f + offsets[i].X;
				var a = swirl + (offsets[i].Z + bob.Z) / r;
				var round = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
				at = prey.WorldPosition + round * r + Vector3.Up * (0.9f + offsets[i].Y + MathF.Sin( swirl * 0.7f ) * 0.25f + bob.Y);
				fishFacing = Shapes.LookAt( new Vector3( -MathF.Sin( a ), 0f, MathF.Cos( a ) ) ) * new Basis( Vector3.Up, MathF.PI );   // nose along the circle
			}
			else at = centre + facing * (offsets[i] + bob);
			var basis = fishFacing * wag * Basis.FromScale( Vector3.One * sizes[i] );
			positions[i] = at;
			bases[i] = basis;
			mm.SetInstanceTransform( slots[i], new Transform3D( basis, at ) );
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
			KillFish( best, at );
		}
	}

	static readonly Color Blood = new( 0.85f, 0.2f, 0.25f );

	/// <summary>
	/// A fish dies: its body becomes a little ragdoll (head, body and tail, each a physics piece, joined so it bends and
	/// flops), thrown away from the blow; about half come apart, the head or the tail torn off with goo spurting from
	/// both torn ends.
	/// </summary>
	void KillFish( int i, Vector3 from )
	{
		alive[i] = false;
		FishKilled++;
		var at = positions[i];
		var kind = kinds[i];
		var Blood = FishLooks.Blood( kind );
		var hide = FishLooks.Skin( kind, Colour );
		if ( i < parts.Count && IsInstanceValid( parts[i] ) ) parts[i].QueueFree();
		gore?.GooBurst( at, Vector3.Up, Blood, 5, 140f, 0.5f );
		Sfx.Play( "splat", at, 0.35f, Rng.Float( 1.4f, 1.8f ) );
		if ( gore is null ) return;
		// Most of a big shoal comes apart in gore: scraps of hide and meat (the first fish to go is always a whole body).
		if ( Rng.Float() > RagdollShare && Alive < Count - 1 )
		{
			FishScrapped++;
			var scale = sizes[i] / Small;
			GibKit.Chips( GibRecipes.Mermaid( Blood, hide ), at, ((at - from).SafeNormal() + Vector3.Up * 0.5f).SafeNormal(), (at - from).SafeNormal(), 0.06f * scale, 3, 0.6f );
			return;
		}
		FishRagdolls++;
		var basis = bases[i] == default ? Basis.Identity : bases[i];
		var size = mesh.GetAabb().Size * sizes[i];
		var length = MathF.Max( size.Z, 0.1f );
		var width = MathF.Max( size.X, 0.03f );
		var height = MathF.Max( size.Y, 0.05f );
		// The model's nose is +Z (the school turns it round to swim): pieces along that axis.
		var along = (basis * Vector3.Back).Normalized();
		var up = (basis * Vector3.Up).Normalized();
		var push = ((at - from).SafeNormal() + Vector3.Up * 0.4f + Rng.InSphere( 0.4f )).SafeNormal() * Rng.Float( 1.5f, 3f );
		var skin = new StandardMaterial3D { AlbedoColor = hide, Roughness = 0.5f, RimEnabled = true, Rim = 0.4f, RimTint = 0.5f };
		var fin = new StandardMaterial3D { AlbedoColor = kind == FishLooks.Kind.Alien ? FishLooks.Accent( Colour ) : hide.Lightened( 0.35f ), Roughness = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		var meat = new StandardMaterial3D { AlbedoColor = Blood.Darkened( 0.2f ), Roughness = 0.35f };
		var eye = new StandardMaterial3D { AlbedoColor = new Color( 0.03f, 0.03f, 0.04f ), Roughness = 0.1f };
		Node3D Piece( string name, Vector3 offset )
		{
			var n = new Node3D { Name = name };
			GetTree().CurrentScene.AddChild( n );
			n.GlobalTransform = new Transform3D( basis.Orthonormalized(), at + along * offset.Z + up * offset.Y );
			return n;
		}
		MeshInstance3D Add( Node3D n, Mesh m, Material mat, Vector3 local, Vector3 scale )
		{
			var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = local, Scale = scale };
			n.AddChild( mi );
			return mi;
		}
		var ball = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 12, Rings = 6 };
		// Head: a rounded snout with two black eyes.
		var head = Piece( "Fish Head", new Vector3( 0f, 0f, length * 0.32f ) );
		Add( head, ball, skin, Vector3.Zero, new Vector3( width, height * 0.9f, length * 0.36f ) );
		foreach ( var x in new[] { -1f, 1f } ) Add( head, ball, eye, new Vector3( x * width * 0.42f, height * 0.12f, length * 0.06f ), Vector3.One * height * 0.2f );
		// Body: the fat middle with a fin on its back.
		var body = Piece( "Fish Body", Vector3.Zero );
		Add( body, ball, skin, Vector3.Zero, new Vector3( width * 1.05f, height, length * 0.42f ) );
		Add( body, new PrismMesh { Size = Vector3.One }, fin, new Vector3( 0f, height * 0.55f, 0f ), new Vector3( 0.01f, height * 0.4f, length * 0.25f ) );
		// Tail: tapering to the fan.
		var tail = Piece( "Fish Tail", new Vector3( 0f, 0f, -length * 0.33f ) );
		Add( tail, ball, skin, Vector3.Zero, new Vector3( width * 0.6f, height * 0.6f, length * 0.32f ) );
		Add( tail, new PrismMesh { Size = Vector3.One }, fin, new Vector3( 0f, 0f, -length * 0.2f ), new Vector3( 0.01f, height * 0.9f, length * 0.22f ) ).Rotation = new Vector3( MathF.PI * 0.5f, 0f, 0f );
		var radius = MathF.Max( 0.6f, width * 0.5f / U.In );
		var gHead = Gib.Attach( head, gore, radius, 5f, 0.08f, push * 1.1f, Blood );
		// An undead fish's brain pops out of its skull and flies off on its own, spurting.
		if ( kind == FishLooks.Kind.Undead )
		{
			BrainsPopped++;
			var brainAt = head.GlobalPosition + up * height * 0.45f;
			var brain = new Node3D { Name = "Fish Brain" };
			GetTree().CurrentScene.AddChild( brain );
			brain.GlobalTransform = new Transform3D( basis.Orthonormalized(), brainAt );
			var pink = new StandardMaterial3D { AlbedoColor = FishLooks.BrainPink, Roughness = 0.2f };
			var brainMat = FishLooks.LooseBrain( width );
			Add( brain, ball, brainMat, Vector3.Zero, new Vector3( width * 1f, height * 0.42f, length * 0.16f ) );
			var gBrain = Gib.Attach( brain, gore, radius * 0.6f, 3f, 0.03f, push * 0.6f + up * Rng.Float( 2.5f, 4f ), Blood );
			gBrain.ApplyTorqueImpulse( Rng.InSphere( 0.002f ) );
			var spurt = new GooJet { Color = Blood, Lifetime = 1.2f, Strength = 0.4f };
			head.AddChild( spurt );
			spurt.GlobalPosition = brainAt;
			spurt.LookAt( brainAt + up + Vector3.Up * 0.01f, Vector3.Right );
			gore.GooBurst( brainAt, up, Blood, 6, 150f, 0.5f );
			Sfx.Play( "splat", brainAt, 0.5f, Rng.Float( 0.9f, 1.1f ) );
		}
		var gBody = Gib.Attach( body, gore, radius * 1.1f, 5f, 0.12f, push, Blood );
		var gTail = Gib.Attach( tail, gore, radius * 0.8f, 5f, 0.06f, push * 0.9f, Blood );
		// It comes apart: the head or the tail torn off, its stump and the torn end spurting.
		var tear = Rng.Float() < TearChance ? (Rng.Float() < 0.5f ? 1 : 2) : 0;
		if ( tear != 0 ) FishTorn++;
		void Join( Gib a, Gib b, Vector3 atWorld, bool torn, Vector3 outward )
		{
			if ( torn )
			{
				foreach ( var (g, dir) in new[] { (a, outward), (b, -outward) } )
				{
					var jet = new GooJet { Color = Blood, Lifetime = 1.6f, Strength = 0.5f };
					g.AddChild( jet );
					jet.GlobalPosition = atWorld;
					jet.LookAt( atWorld + dir, MathF.Abs( dir.Dot( Vector3.Up ) ) > 0.9f ? Vector3.Right : Vector3.Up );
					var cap = new MeshInstance3D { Mesh = ball, MaterialOverride = meat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
					g.AddChild( cap );
					cap.GlobalPosition = atWorld;
					cap.Scale = new Vector3( width * 0.7f, height * 0.6f, width * 0.3f );
				}
				a.ApplyCentralImpulse( outward * 0.25f );
				b.ApplyCentralImpulse( -outward * 0.25f );
				gore.GooBurst( atWorld, outward, Blood, 6, 160f, 0.5f );
				return;
			}
			var joint = new ConeTwistJoint3D();
			GetTree().CurrentScene.AddChild( joint );
			joint.GlobalTransform = new Transform3D( Shapes.LookAt( along ) * new Basis( Vector3.Up, MathF.PI * 0.5f ), atWorld );   // twist axis (X) down the fish
			joint.SetParam( ConeTwistJoint3D.Param.SwingSpan, MathX.Rad( 50f ) );
			joint.SetParam( ConeTwistJoint3D.Param.TwistSpan, MathX.Rad( 15f ) );
			joint.NodeA = a.GetPath();
			joint.NodeB = b.GetPath();
			a.TreeExiting += () => { if ( IsInstanceValid( joint ) ) joint.QueueFree(); };
			b.TreeExiting += () => { if ( IsInstanceValid( joint ) ) joint.QueueFree(); };
			a.AddCollisionExceptionWith( b );
		}
		Join( gHead, gBody, at + along * length * 0.16f, tear == 1, along );
		Join( gBody, gTail, at - along * length * 0.15f, tear == 2, -along );
		// A last flop or two as it goes.
		foreach ( var g in new[] { gHead, gBody, gTail } ) g.ApplyTorqueImpulse( Rng.InSphere( 0.004f ) );
		CreatureGore.Twitch( body, Rng.Float( 1.5f, 3f ), 0.6f );
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
		if ( !Puppet && !Hunter && GetParent() is Node parent )
		{
			var (home, colour, count, radius, speed, phase, theme) = (Home, Colour, Count, Radius, LoopSpeed, Phase, Theme);
			parent.GetTree().CreateTimer( Rng.Float( RespawnMin, RespawnMax ) ).Timeout += () =>
			{
				if ( IsInstanceValid( parent ) && parent.IsInsideTree() && (!Net.Online || Net.IsHost) ) Spawn( parent, home, colour, count, radius, speed, phase, 0, theme );
			};
		}
		QueueFree();
	}

	public override void NetWrite( List<float> s ) => s.Add( aggro ? 1f : 0f );
	public override void NetRead( ReadOnlySpan<float> s ) { if ( s.Length > 0 ) aggro = s[0] > 0.5f; }
}
