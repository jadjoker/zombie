using Godot;

/// <summary>
/// The hermit crab squatter (user's pick): a junk crab living in a piece of rubbish it found on the sea bed, one of
/// eight shells (<see cref="ShellKind"/>). The shell takes most of what hits it and crumbles as it is shot: each hit
/// knocks off the pieces nearest where it landed, and shots go through the holes it leaves. When the shell's health
/// runs out what is left of it falls apart, and the naked crab (a soft pink tail, the weak spot now) panics: it runs
/// for a moment, then comes back faster and snapping quicker. Crab health; the shell has <see cref="ShellShare"/> of it.
/// </summary>
public partial class HermitCrab : JunkCrab
{
	/// <summary> The shell's health against the crab's. </summary>
	public const float ShellShare = 1.1f;
	/// <summary> The share of a hit on the shell that gets through to the crab. </summary>
	public const float ShellPass = 0.15f;
	/// <summary> A shot this far (m, at size 1) from any piece still on goes through a hole to the crab. </summary>
	public const float HoleReach = 0.11f;
	/// <summary> Naked: this much faster, snapping this much quicker, and running away this long first. </summary>
	public const float NakedSpeed = 1.75f, NakedCooldown = 0.7f, PanicTime = 1.4f;
	/// <summary> The share of the pieces knocked off by the time the shell's health is gone (the rest falls apart then). </summary>
	const float CrumbleShare = 0.8f;

	/// <summary> Tests: pieces knocked off, shells that fell apart, shots that went through a hole. </summary>
	public static int PiecesOff, ShellsOff, ThroughHoles;

	public ShellKind Shell;
	public bool ShellOn => Has( Barrel );
	public int PiecesLeft => frags.Count( f => !f.Gone );
	public int PieceCount => frags.Count;
	public float ShellFraction => parts?[Barrel] is { } p && !p.Gone ? MathX.Clamp( p.Health / MathF.Max( 1f, p.MaxHealth ), 0f, 1f ) : 0f;

	public static new HermitCrab Spawn( Node parent, Vector3 position, float maxHealth )
	{
		var c = Place( new HermitCrab { Name = "Hermit Crab" }, parent, position, maxHealth );
		c.Shell = (ShellKind)Rng.Int( 0, ShellCount - 1 );
		return c;
	}

	protected override bool Exposed => !Has( Barrel );

	protected override void BuildArmour( Part body, float hue )
	{
		// The soft pink tail curled up under the shell (hidden until the shell is gone).
		var pink = new Color( 1f, 0.62f, 0.66f );
		Sphere( body.Go, body, new Vector3( -11f, 0f, 3f ), new Vector3( 7.5f, 7f, 6.5f ), pink );
		Sphere( body.Go, body, new Vector3( -16f, 2f, 6f ), new Vector3( 4.5f, 4.5f, 4f ), pink );
		Sphere( body.Go, body, new Vector3( -17f, 4f, 9.5f ), new Vector3( 2.6f, 2.6f, 2.4f ), pink.Lerp( Colors.White, 0.2f ) );
		var maxHealth = Health?.MaxHealth ?? 90f;
		var shell = AddPart( Barrel, Body, body.Go, "Shell", new Vector3( -7f, 0f, 5f ), Basis.Identity, MathF.Max( 60f, maxHealth * ShellShare ) );
		BuildShell( shell );
	}

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( index != Barrel || !Has( Barrel ) ) { base.ReceiveShot( index, damage, direction, position, origin, shooter, scoreHit ); return; }
		// A hole where the pieces have already come off: through to the crab.
		if ( NearestPiece( position, out var gap ) is null || gap > HoleReach * Size )
		{
			ThroughHoles++;
			base.ReceiveShot( Body, damage, direction, position, origin, shooter, scoreHit );
			return;
		}
		if ( NetCreatures.Intercept( this, index, damage, direction, position, origin, shooter, scoreHit ) ) return;
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		var part = parts[Barrel];
		part.Health -= damage;
		part.Flash = 0f;
		CreatureGore.Jiggle( part.Go, 0.35f );
		Crumble( position, direction );
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = damage * ShellPass, Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( !killed && part.Health <= 0f ) ShellFallsApart( direction );
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, false, scoreHit );
	}

	/// <summary> A hit on the shell: chips and a crack where it landed, and the pieces nearest it knocked off as its health falls. </summary>
	void Crumble( Vector3 at, Vector3 direction )
	{
		Sfx.Play( "shell_crack", at, 0.8f, Rng.Float( 0.9f, 1.15f ) * Look.Pitch );
		Fx.ImpactWorld( at, -direction );
		var part = parts[Barrel];
		var lost = 1f - MathX.Clamp( part.Health / MathF.Max( 1f, part.MaxHealth ), 0f, 1f );
		var want = (int)MathF.Floor( lost * CrumbleShare * frags.Count );
		var off = frags.Count( f => f.Gone );
		while ( off < want && NearestPiece( at, out _ ) is { } f )
		{
			KnockOff( f, (f.Node.GlobalPosition - ShellCentre).SafeNormal() * 1.6f + direction * 1.4f + Vector3.Up * 1.2f );
			off++;
		}
	}

	/// <summary> The shell's health is gone: what is left of it falls apart, and the crab panics. </summary>
	void ShellFallsApart( Vector3 direction )
	{
		ShellsOff++;
		var centre = ShellCentre;
		foreach ( var f in frags.Where( f => !f.Gone ).ToList() )
			KnockOff( f, (f.Node.GlobalPosition - centre).SafeNormal() * Rng.Float( 2f, 3.5f ) + direction * 1.2f + Vector3.Up * Rng.Float( 1.5f, 2.5f ) );
		Sfx.Play( "shell_crack", centre, 1f, 0.75f * Look.Pitch );
		Sfx.Play( Look.Breaks, centre, 0.8f, Rng.Float( 0.9f, 1.05f ) );
		var part = parts[Barrel];
		MarkGone( Barrel );
		GoneMask |= SubtreeMask( Barrel );
		if ( IsInstanceValid( part.Go ) ) part.Go.QueueFree();
		// Naked: it panics, then comes back faster.
		MoveSpeed *= NakedSpeed;
		AttackCooldown *= NakedCooldown;
		PanicUntil = Clock.Now + PanicTime;
		Sfx.Play( "creature_attack", GlobalPosition, 0.7f, 1.9f );
	}

	/// <summary> One piece comes off as a loose physics piece of its own stuff (porcelain and metal sink, a croissant floats). </summary>
	void KnockOff( Frag f, Vector3 velocity )
	{
		if ( f.Gone || !IsInstanceValid( f.Node ) ) return;
		f.Gone = true;
		PiecesOff++;
		var part = parts[Barrel];
		// Out of the part's tint list (it is not the crab's to flash any more).
		for ( var i = part.Renderers.Count - 1; i >= 0; i-- )
			if ( f.Meshes.Contains( part.Renderers[i].Renderer ) )
			{
				part.Renderers.RemoveAt( i );
				if ( i < part.Applied.Count ) part.Applied.RemoveAt( i );
			}
		foreach ( var m in f.Meshes ) if ( IsInstanceValid( m ) ) Shapes.Tint( m, Colors.White );
		var gib = Gib.Attach( f.Node, gore, 3f, 12f, 0.6f, velocity * Size, Look.Goo, addCollider: false );
		if ( f.Meshes.FirstOrDefault( m => IsInstanceValid( m ) && m.Mesh is not null ) is { } shape )
		{
			var cs = new CollisionShape3D { Shape = shape.Mesh.CreateConvexShape( true, true ) };
			gib.AddChild( cs );
			cs.GlobalTransform = shape.GlobalTransform;   // its (even) scale too: the piece is drawn scaled
		}
		else gib.AddChild( new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.05f } } );
		gib.Stuff = Look.Stuff;
		gib.AngularVelocity = Rng.InSphere( 9f );
	}

	Frag NearestPiece( Vector3 at, out float distance )
	{
		Frag best = null;
		distance = float.MaxValue;
		foreach ( var f in frags )
		{
			if ( f.Gone || !IsInstanceValid( f.Node ) ) continue;
			var d = (f.Node.GlobalTransform * f.Middle).DistanceTo( at ) - f.Reach * Size * ShellScale;
			if ( d < distance ) { distance = d; best = f; }
		}
		return best;
	}

	Vector3 ShellCentre => shellRoot is not null && IsInstanceValid( shellRoot ) ? shellRoot.GlobalTransform * shellMiddle : GlobalPosition + Vector3.Up * 0.5f;

	// ---- Tests ----

	/// <summary> Tests: takes the shell to this share of its health, crumbling as if shot there. </summary>
	public void TestCrumble( float share, Vector3 at )
	{
		var part = parts[Barrel];
		if ( part is null || part.Gone ) return;
		part.Health = part.MaxHealth * share;
		Crumble( at, (ShellCentre - at).SafeNormal() );
		if ( part.Health <= 0f ) ShellFallsApart( Vector3.Forward );
	}

	public bool Panicking => Clock.Now < PanicUntil;

	/// <summary> Tests: the middle of the piece still on that is nearest a point. </summary>
	public Vector3? PieceNear( Vector3 at ) => NearestPiece( at, out _ ) is { } f ? f.Node.GlobalTransform * f.Middle : null;
}
