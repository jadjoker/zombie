using Godot;

/// <summary>
/// The mimic octopus (user's pick): rare scenery, not part of the round (like the garden eels). Each sea mine or glow
/// bulb laid has a <see cref="Chance"/> (0.2%) of being one of these instead, copying it perfectly. A player who comes
/// within <see cref="RevealRange"/> (or shoots it) finds out: the disguise shrinks away, an octopus with the classic alien
/// eyes drops to the sand and unfurls its arms, and from then on it crawls after the nearest player and lashes an arm out
/// to grab them. Grabbed, the player is held and drawn in to its beak, squeezed and bitten, while the HUD says
/// "CUT THE TENTACLE OFF!": a melee strike (the axe, the spear, any melee weapon) cuts that arm off, frees them and
/// takes <see cref="CutShare"/> of its health. A teammate can shoot the arm off too. It has eight arms. Metres, facing -Z.
/// </summary>
public partial class MimicOctopus : Creature
{
	public override bool CanScare => false;
	public static readonly List<MimicOctopus> All = new();

	/// <summary> The chance a sea mine or glow bulb is a mimic instead (user's number: 0.2%). </summary>
	public static float Chance = 0.002f;
	public static bool Roll() => Rng.Float() < Chance;

	public enum Disguise { Mine, Bulb, Chest }
	public const float BaseHealth = 360f;
	public const int Arms = 8;
	public float RevealRange = 3.4f;
	public float Speed = 1.7f;
	/// <summary> It keeps about this far from its prey while it waits to grab. </summary>
	public float KeepAt = 2.3f;
	public float GrabRange = 4.6f;
	public float GrabCooldown = 2.6f;
	public const float WindupTime = 0.55f;
	public float LashSpeed = 11f;
	/// <summary> How fast a held player is drawn in (m/s), and how near its beak they end up. </summary>
	public float ReelSpeed = 0.8f, HoldAt = 1.15f;
	public float SqueezeDps = 5f, BiteDamage = 12f, BiteEvery = 1.3f;
	public const float HoldMax = 12f;
	/// <summary> The share of its health an arm cut off takes. </summary>
	public const float CutShare = 0.2f;
	/// <summary> Shooting a grabbing arm this much cuts it off (a teammate's rescue). </summary>
	public const float LashHealth = 45f;
	public const float EyeMultiplier = 2f;

	/// <summary> Tests: reveals, grabs, arms cut off. </summary>
	public static int Reveals, Grabs, Cuts, Bites;

	enum Act { Disguised, Reveal, Prowl, Windup, Lash, Hold, Recoil }
	Act act = Act.Disguised;
	TimeSince sinceAct = 99f, sinceGrab = 99f, sinceBite = 99f, sinceFloor = 99f, sinceHit = 99f;
	public Disguise Look = Disguise.Mine;
	/// <summary> How high the disguise floats over the floor. </summary>
	public float DisguiseHeight = 1.8f;
	Vector3 velocity;
	float floorY, lashHealth, phase;
	bool floorSet;
	/// <summary> The arm doing the grabbing, how far out it has reached, and where it is reaching. </summary>
	int arm = -1;
	float reach;
	Vector3 lashTarget, lashDir;
	PlayerSurvivor held;
	bool holding;
	/// <summary> Arms cut off (a bit each). </summary>
	public int GoneMask;

	public static MimicOctopus Spawn( Node parent, Vector3 floor, float maxHealth ) =>
		Place( new MimicOctopus { Name = "Mimic Octopus" }, parent, floor, maxHealth );

	/// <summary> A mimic in a disguise, its anchor on the floor at `floor`. </summary>
	public static MimicOctopus Disguised( Node3D level, Vector3 floor, Disguise look, float height )
	{
		var m = Spawn( level, floor, BaseHealth * (SeaTerrain.Area >= 3 ? 1.3f : 1f) );
		m.Look = look;
		m.DisguiseHeight = height;
		m.RotationDegrees = new Vector3( 0f, Rng.Float( 0f, 360f ), 0f );
		GD.Print( $"[mimic] a mimic octopus is hiding as a {look switch { Disguise.Mine => "sea mine", Disguise.Bulb => "glow bulb", _ => "treasure chest" }}" );
		return m;
	}

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	/// <summary> In disguise it is part of the scenery: no health bar, no aim assist. </summary>
	public override bool IsEnemy => act != Act.Disguised;
	public override bool Pullable => act != Act.Disguised && act != Act.Hold && Weight < 1000f;
	public override bool WeakSpot( int index ) => index == 1;
	public bool Hidden => act == Act.Disguised;
	public bool Holding => holding && held is not null;
	public int ArmsLeft => Arms - BitCount( GoneMask );
	public string ActName => act.ToString();
	static int BitCount( int m ) { var n = 0; for ( ; m != 0; m &= m - 1 ) n++; return n; }

	/// <summary> This machine's player is the one held (online, a joiner's copy reads it from the host). </summary>
	public bool HoldsLocal => Puppet
		? holding && PlayerSurvivor.Local is { IsAlive: true } me && me.RealPosition.DistanceTo( heldAt ) < 1.6f
		: holding && held is not null && held.Controller is { IsRemote: false };
	Vector3 heldAt;

	/// <summary> The octopus holding this machine's player, if any (the HUD's cue). </summary>
	public static MimicOctopus HoldingLocal
	{
		get
		{
			foreach ( var m in All ) if ( !m.IsDead && m.HoldsLocal ) return m;
			return null;
		}
	}

	protected override void Setup()
	{
		BeginSetup( BaseHealth );
		phase = Rng.Float( 0f, MathF.Tau );
		lashHealth = LashHealth;
		PlayerMelee.Swung += OnSwung;
		Build();
		Pose( 0f );
	}

	protected override void Cleanup()
	{
		PlayerMelee.Swung -= OnSwung;
		Release();
		if ( lashRoot is not null && IsInstanceValid( lashRoot ) ) lashRoot.QueueFree();
	}

	public override void _Process( double delta )
	{
		if ( health is null || body is null ) return;
		var dt = MathF.Min( (float)delta, 0.1f );
		if ( !IsDead && !Frozen ) Think( dt );
		if ( !IsDead ) Pose( dt );
	}

	// ---- Brain ----

	void Think( float dt )
	{
		var prey = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var a = (float)sinceAct;
		var wish = Vector3.Zero;
		switch ( act )
		{
			case Act.Disguised:
				if ( prey is not null && (prey.RealPosition + Vector3.Up * 1f).DistanceTo( DisguiseAt ) < RevealRange ) Begin( Act.Reveal );
				return;
			case Act.Reveal:
				if ( a > RevealTime ) Begin( Act.Prowl );
				return;
			case Act.Prowl:
			{
				if ( prey is null ) break;
				var to = (prey.RealPosition - GlobalPosition).Flat();
				var dist = to.Length();
				Face( to, 3f, dt );
				wish = dist > KeepAt ? to.SafeNormal() * Speed : dist < KeepAt - 0.8f ? -to.SafeNormal() * Speed * 0.5f : Vector3.Zero;
				if ( ArmsLeft > 0 && dist < GrabRange && sinceGrab > GrabCooldown && prey.Controller is { Scripted: false }
					&& !Trace.Ray( this, Centre, prey.RealPosition + Vector3.Up * 1.1f, Layers.World ).Hit )
				{
					arm = ArmToward( to );
					if ( arm >= 0 ) Begin( Act.Windup );
				}
				break;
			}
			case Act.Windup:
				if ( prey is not null ) Face( (prey.RealPosition - GlobalPosition).Flat(), 6f, dt );
				if ( a > WindupTime )
				{
					Begin( Act.Lash );
					var chest = (prey?.RealPosition ?? GlobalPosition + GlobalBasis * Vector3.Forward * 3f) + Vector3.Up * 1.1f;
					lashDir = (chest - ArmBase( arm )).SafeNormal();
					reach = 0f;
				}
				break;
			case Act.Lash:
			{
				// The arm shoots out along its line; whoever it reaches is caught.
				reach += LashSpeed * dt;
				var from = ArmBase( arm );
				var tip = from + lashDir * reach;
				lashTarget = tip;
				foreach ( var p in PlayerSurvivor.All )
				{
					if ( !p.IsAlive || p.Controller is not { } c || c.Scripted ) continue;
					var chest = p.RealPosition + Vector3.Up * 1.1f;
					if ( chest.DistanceTo( tip ) > 0.7f ) continue;
					Grab( p );
					break;
				}
				if ( !holding && (reach > GrabRange + 0.6f || Trace.Ray( this, from, tip, Layers.World ).Hit) ) { Sfx.Play( "swim", tip, 0.5f, 1.6f ); Begin( Act.Recoil ); }
				break;
			}
			case Act.Hold:
				HoldThink( dt );
				return;
			case Act.Recoil:
				if ( prey is not null ) { var away = (GlobalPosition - prey.RealPosition).Flat(); wish = away.SafeNormal() * Speed * 0.9f; }
				if ( a > 1.3f ) Begin( Act.Prowl );
				break;
		}
		Move( wish, dt );
	}

	/// <summary> A player is held: drawn in toward the beak, squeezed, bitten; let go if they die, it takes too long, or anyone frees them. </summary>
	void HoldThink( float dt )
	{
		var c = held?.Controller;
		if ( c is null || !IsInstanceValid( c ) || !held.IsAlive || sinceAct > HoldMax ) { Release(); Begin( Act.Recoil ); return; }
		var mouth = Mouth;
		var feet = held.RealPosition;
		var flat = (feet - GlobalPosition).Flat();
		var dist = flat.Length();
		if ( dist > HoldAt )
		{
			var next = feet + (-flat.SafeNormal()) * MathF.Min( ReelSpeed * dt, dist - HoldAt );
			next.Y = MathF.Max( next.Y - 1.2f * dt, Sculpt.FloorBelow( this, next + Vector3.Up * 1f, 6f ) );
			c.GlobalPosition = next;
			c.ResetPhysicsInterpolation();
		}
		c.Velocity = Vector3.Zero;
		Face( flat, 5f, dt );
		heldAt = held.RealPosition;
		lashTarget = heldAt + Vector3.Up * 1.1f;
		held.Health?.OnDamage( new DamageInfo { Damage = SqueezeDps * dt, Attacker = this, Position = lashTarget, Origin = Centre } );
		if ( dist < HoldAt + 0.25f && sinceBite > BiteEvery )
		{
			sinceBite = 0f;
			Bites++;
			held.Health?.OnDamage( new DamageInfo { Damage = BiteDamage, Attacker = this, Position = lashTarget, Origin = mouth } );
			held.Juice?.Shake( 0.5f );
			Sfx.Play( "crab_snap", mouth, 0.9f, 0.7f );
		}
	}

	void Grab( PlayerSurvivor p )
	{
		Grabs++;
		held = p;
		holding = true;
		heldAt = p.RealPosition;
		if ( p.Controller is { } c ) { c.Scripted = true; c.Velocity = Vector3.Zero; }
		p.Juice?.Shake( 0.6f );
		Sfx.Play( "octo_grab", p.RealPosition + Vector3.Up * 1.1f, 1f, Rng.Float( 0.9f, 1.05f ) );
		sinceBite = BiteEvery * 0.5f;
		lashHealth = LashHealth;
		Begin( Act.Hold );
	}

	void Release()
	{
		if ( held?.Controller is { } c && IsInstanceValid( c ) ) c.Scripted = false;
		held = null;
		holding = false;
	}

	/// <summary> Crawls over the sand: toward what it wants, along walls, kept on the floor. </summary>
	void Move( Vector3 wish, float dt )
	{
		velocity = velocity.Lerp( wish, 1f - MathF.Exp( -3f * dt ) );
		var p = GlobalPosition;
		foreach ( var other in All )
		{
			if ( other == this || other.IsDead || other.Hidden ) continue;
			var away = (p - other.GlobalPosition).Flat();
			var d = away.Length();
			if ( d < 1.6f && d > 0.01f ) velocity += away / d * (1.6f - d) * 3f * dt;
		}
		if ( velocity.LengthSquared() > 0.01f )
		{
			var from = p + Vector3.Up * 0.35f;
			var hit = Trace.Sphere( this, 0.3f, from, from + velocity.SafeNormal() * 0.8f, Layers.World );
			if ( hit.Hit && MathF.Abs( hit.Normal.Y ) < 0.6f ) velocity = velocity.Slide( hit.Normal.Flat().SafeNormal() );
		}
		var next = Sculpt.InBounds( p + velocity.Flat() * dt );
		if ( sinceFloor > 0.2f || !floorSet ) { sinceFloor = 0f; floorY = Sculpt.FloorBelow( this, next + Vector3.Up * 1.2f, 8f ); floorSet = true; }
		var ground = Sculpt.GroundAt( next );
		var floor = SeaTerrain.Area == SeaTerrain.Globe || ground == float.MinValue ? floorY : MathF.Max( ground, floorY - 0.05f );
		next.Y = MathX.Lerp( p.Y, floor, 1f - MathF.Exp( -10f * dt ) );
		GlobalPosition = next;
	}

	void Begin( Act next )
	{
		act = next;
		sinceAct = 0f;
		switch ( next )
		{
			case Act.Reveal:
				Reveals++;
				RevealFx();
				break;
			case Act.Windup:
				Sfx.Play( "creature_attack", Centre, 0.6f, 0.75f );
				break;
			case Act.Lash:
				sinceGrab = 0f;
				Sfx.Play( "dive_whoosh", Centre, 0.6f, 1.6f );
				break;
			case Act.Recoil:
				sinceGrab = 0f;
				reach = 0f;
				break;
		}
	}

	// ---- Melee: the grabbed player cuts the arm off ----

	void OnSwung( PlayerController striker )
	{
		if ( Puppet || IsDead || !holding || held?.Controller != striker ) return;
		CutArm( striker );
	}

	/// <summary> The grabbing arm is cut off: it falls away in pieces, the player is free, it is hurt and backs off. </summary>
	void CutArm( PlayerController by )
	{
		if ( arm < 0 || (GoneMask & (1 << arm)) != 0 ) return;
		Cuts++;
		var victim = held;
		GoneMask |= 1 << arm;
		CutFx( arm );
		Release();
		if ( victim?.Controller is { } c ) c.Velocity += (victim.RealPosition - GlobalPosition).Flat().SafeNormal() * 3f + Vector3.Up * 1.5f;
		Begin( Act.Recoil );
		sinceGrab = -1.5f;   // a longer wait before the next grab
		var shooter = by?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = health.MaxHealth * CutShare, Position = ArmBase( arm ), Origin = by?.GlobalPosition ?? Centre, Attacker = shooter } );
		if ( wasAlive && shooter is not null ) shooter.RewardHit( IsDead, false, true );
		arm = -1;
	}

	// ---- Hits ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( NetCreatures.Intercept( this, index, ref damage, direction, position, origin, shooter, scoreHit ) ) return;
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		// Shot in disguise: it gives itself away.
		if ( act == Act.Disguised && !Puppet ) Begin( Act.Reveal );
		// A grabbing arm shot enough comes off.
		if ( index == 2 )
		{
			gore?.GooBurst( position, -direction, Blood, 4, 160f, 0.4f );
			if ( Puppet ) return;
			lashHealth -= damage;
			if ( lashHealth <= 0f && holding ) CutArm( shooter?.Controller );
			if ( shooter is not null ) shooter.RewardHit( false, false, scoreHit );
			return;
		}
		var weak = index == 1;
		var dealt = damage * (weak ? EyeMultiplier : 1f);
		var wasAlive = !IsDead;
		if ( !Lethal( dealt ) ) { gore?.GooBurst( position, -direction, Blood, 5, 200f, 0.5f ); if ( Rng.Float() < 0.35f ) GibKit.Chips( Gibs, position, -direction, direction, 0.05f, 1 ); }
		health.OnDamage( new DamageInfo { Damage = dealt, Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, weak, scoreHit );
	}

	protected override void OnDied()
	{
		RaiseKilled();
		Release();
		var at = Centre;
		var dir = lastShotDirection == Vector3.Zero ? Vector3.Up : lastShotDirection.Normalized();
		gore?.GooBurst( at, Vector3.Up, Blood, 16, 300f, 1.1f );
		gore?.GooSpray( at, dir, 5, Blood, 220f, 0.6f, 1.3f );
		InkCloud( at, 10 );
		GibKit.Burst( Gibs, at, dir, 0.14f, 8 );
		// The arms come off and twitch.
		for ( var k = 0; k < Arms; k++ )
		{
			if ( (GoneMask & (1 << k)) != 0 || k >= arms.Count || !IsInstanceValid( arms[k].Base ) ) continue;
			var piece = arms[k].Base;
			piece.Reparent( GetTree().CurrentScene, true );
			Gib.Attach( piece, gore, 4f, 20f, 0.5f, (piece.GlobalPosition - at).SafeNormal() * Rng.Float( 1.5f, 3f ) + Vector3.Up * 1.2f, Blood );
			CreatureGore.Twitch( piece, Rng.Float( 2f, 4f ), 0.9f );
		}
		Sfx.Play( "stab_squish_2", at, 0.9f, 0.8f );
		QueueFree();
	}

	// ---- Online ----

	public override void NetWrite( List<float> s )
	{
		s.Add( (int)act ); s.Add( (float)sinceAct ); s.Add( arm ); s.Add( GoneMask ); s.Add( holding ? 1f : 0f );
		s.Add( lashTarget.X ); s.Add( lashTarget.Y ); s.Add( lashTarget.Z ); s.Add( reach );
		s.Add( heldAt.X ); s.Add( heldAt.Y ); s.Add( heldAt.Z ); s.Add( (int)Look ); s.Add( DisguiseHeight );
	}

	public override void NetRead( ReadOnlySpan<float> s )
	{
		if ( s.Length < 14 ) return;
		var next = (Act)(int)s[0];
		var changed = next != act;
		if ( changed && next == Act.Reveal ) Begin( next );
		else if ( changed ) act = next;
		NetTimer( ref sinceAct, s[1], changed );
		arm = (int)s[2];
		var mask = (int)s[3];
		for ( var k = 0; k < Arms; k++ ) if ( (mask & (1 << k)) != 0 && (GoneMask & (1 << k)) == 0 ) { GoneMask |= 1 << k; CutFx( k ); }
		holding = s[4] > 0.5f;
		lashTarget = new Vector3( s[5], s[6], s[7] );
		reach = s[8];
		heldAt = new Vector3( s[9], s[10], s[11] );
	}

	// ---- Tests ----

	/// <summary> Tests: give itself away now. </summary>
	public void ForceReveal() { if ( act == Act.Disguised ) Begin( Act.Reveal ); }
	/// <summary> Tests: a grab ready now. </summary>
	public void ReadyGrab() => sinceGrab = 99f;
}
