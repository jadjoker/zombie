using Godot;

/// <summary>
/// The Lighthouse Hermit (user's pick): the shore's wave 10 boss, a giant alien hermit crab that lives in a whole sunken
/// lighthouse, leant back over its shell like a whelk. The tower is banded red and white under barnacles and weed, cracked
/// with glowing green veins; up in its lamp room a huge alien eye is the lamp, turning to look, its beam sweeping the sea.
/// A brass foghorn hangs off the gallery. The crab is red and purple with the classic alien eyes on stalks, one claw far
/// bigger than the other, and walks sideways. Four attacks:
/// - Claw Slam: both claws up, then down into the sand; it hurts in front and sends rings of sand across the ground (jump
///   them);
/// - Burrow Ambush: he drags the lighthouse down into the sand until only the lamp room shows, ploughs after the player like
///   a fin, then bursts up under them (a glowing ring warns first) and throws them into the air;
/// - Foghorn Blast: the horn groans as it winds up (shoot it, <see cref="HornShare"/>, and it jams, leaving him stunned),
///   then blasts: thick fog rolls out over the basin (<see cref="FogBank"/>), the lamp is the only light, he hunts faster and
///   small hermit crabs scuttle in under it;
/// - Lantern Beam: he rears up out of the tower, baring his soft belly (part 1, the weak spot then), and the lamp focuses
///   into a burning beam that drags after the player and leaves a glowing trail that burns (<see cref="LampTrail"/>); rocks
///   and stations block it, and shooting the belly enough (<see cref="BellyShare"/>) puts the light out.
/// At half health he is enraged: quicker, a third sand ring, a faster beam. Dead, the tower cracks and falls in pieces and the
/// crab bursts. Metres, facing -Z, the root on the ground. Looks in LighthouseHermitLook.cs, the fog, trail and horn ring in
/// LighthouseHermitFx.cs, online in LighthouseHermitNet.cs.
/// </summary>
public partial class LighthouseHermit : Creature
{
	public static readonly List<LighthouseHermit> All = new();
	public override bool CanScare => false;
	public override float AreaDamageScale => 0.6f;
	public override Color Blood => GooColor;
	public override GibRecipe Gibs => GibRecipes.Crab( ShellRed, BellyPink, GooColor );
	public override bool WeakSpot( int index ) => index is LampIndex or EyeIndex || (index == BellyIndex && BellyOpen);
	public override string Voice => "spot_crab";
	public override float VoicePitch => 0.5f;

	public float WalkSpeed = 4.2f;
	public float KeepAway = 11f;
	// Claw Slam.
	public float SlamDamage = 30f;
	public float SlamReach = 4.5f;
	public int SlamRings = 2;
	public float RingDamage = 16f, RingSpeed = 13f, RingReach = 28f, RingGap = 0.4f;
	// Burrow Ambush.
	public float BuryDepth = 7.6f;
	public float BuriedSpeed = 7f;
	public float BuriedMax = 6f;
	public float EruptCue = 1f;
	public float EruptDamage = 34f;
	public float EruptRadius = 4.5f;
	public float EruptLaunch = 11f;
	// Foghorn Blast.
	public float HornWindUp = 2.2f;
	/// <summary> This much of his full health shot into the horn while it winds up jams it. </summary>
	public float HornShare = 0.025f;
	public float JamTime = 2.8f;
	public float JamMultiplier = 1.5f;
	public float FogRadius = 34f;
	public float FogTime = 12f;
	public int FogCrabs = 3, MaxCrabs = 5;
	public float CrabHealth = 300f;
	// Lantern Beam.
	public float BeamRear = 0.8f, BeamCharge = 1.2f, BeamTime = 3.5f;
	public float BeamDps = 28f;
	public float BeamWidth = 1f;
	public float BeamTrack = 6.5f;
	public float TrailDps = 12f;
	/// <summary> This much of his full health shot into his belly while the beam is up puts the light out. </summary>
	public float BellyShare = 0.04f;
	// Hits.
	public float LampMultiplier = 2f, EyeMultiplier = 2f, BellyMultiplier = 2.5f, TowerMultiplier = 0.35f, ClawMultiplier = 0.6f, LegMultiplier = 0.8f;
	public float EnrageAt = 0.5f;

	public const int LampIndex = 0, BellyIndex = 1, ThoraxIndex = 2, TowerIndex = 3, BigClawIndex = 4, SmallClawIndex = 5, LegIndex = 6, HornIndex = 7, EyeIndex = 8;
	public static readonly string[] Attacks = { "slam", "burrow", "horn", "beam" };

	/// <summary> Tests: he only attacks when told (Force), and what he has done. </summary>
	public bool OnlyWhenTold;
	public static int Slams, SlamHits, Burrows, Eruptions, EruptHits, Horns, Jams, Fogs, FogCrabsCalled, Beams, BeamHits, TrailHits, LightsOut, Enrages;
	public static float BeamDamage;

	static readonly Color ShellRed = new( 0.86f, 0.26f, 0.18f );
	static readonly Color BellyPink = new( 0.92f, 0.55f, 0.72f );
	static readonly Color GooColor = new( 0.4f, 1f, 0.55f );
	static readonly Color[] Flesh = { new( 0.92f, 0.55f, 0.72f ), new( 0.62f, 0.3f, 0.7f ), new( 0.86f, 0.26f, 0.18f ) };

	public enum Act { Arrive, Walk, Slam, Dig, Buried, Erupt, Horn, Jammed, Beam, Recover, Dead }
	Act act = Act.Arrive;
	TimeSince sinceAct;
	public Act Doing => act;
	public bool Enraged { get; private set; }
	public float HealthFraction => health?.Fraction ?? 0f;
	/// <summary> Reared up for the beam, his soft belly out of the tower. </summary>
	public bool BellyOpen => act == Act.Beam && (float)sinceAct > BeamRear * 0.5f && (float)sinceAct < BeamRear + BeamCharge + BeamTime + 0.3f;
	public bool Underground => act is Act.Dig or Act.Buried || (act == Act.Erupt && (float)sinceAct < EruptCue);
	public bool HornWinding => act == Act.Horn;
	public Vector3 EruptSpot => eruptAt;

	public string Line { get; private set; } = "";
	public TimeSince TimeSinceLine { get; private set; } = 999f;
	void Say( string line ) { Line = line; TimeSinceLine = 0f; }

	Vector3 velocity;
	float orbit, orbitSign = 1f;
	float ground;
	TimeSince sinceGround = 99f, sinceHit = 99f, sinceSand = 99f;
	TimeUntil nextAttack = 3f;
	readonly List<string> bag = new();
	string lastAttack;
	bool struck;
	int ringsOut;
	float hornTaken, bellyTaken;
	Vector3 eruptAt, digFrom;
	/// <summary> How far he has come up out of the sand (0 buried, 1 on it). </summary>
	float risen;
	bool burst;
	// The beam: where it lands, and what it has burned this beat.
	Vector3 beamAt;
	readonly Dictionary<PlayerSurvivor, float> burned = new();
	// Small hermit crabs he called in under the fog.
	readonly List<Creature> called = new();

	public static LighthouseHermit Spawn( Node parent, Vector3 position, float maxHealth ) =>
		Place( new LighthouseHermit { Name = "Lighthouse Hermit" }, parent, position, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	protected override void Setup()
	{
		BeginSetup( 6000f );
		orbit = Rng.Float( 0f, MathF.Tau );
		orbitSign = Rng.Float() < 0.5f ? -1f : 1f;
		ground = GroundAt( GlobalPosition );
		Build();
		Begin( Act.Arrive );
		risen = 0f;
		GlobalPosition = GlobalPosition with { Y = ground - BuryDepth };
		Sfx.Play2D( "boss_spawn", 0.9f, 0.9f );
		Sfx.Play( "foghorn_blast", GlobalPosition, 1.4f, 1f );
	}

	protected override void Cleanup()
	{
		if ( beam is not null && IsInstanceValid( beam ) ) beam.QueueFree();
		if ( sweep is not null && IsInstanceValid( sweep ) ) sweep.QueueFree();
		if ( cue is not null && IsInstanceValid( cue ) ) cue.QueueFree();
		FogBank.Clear( this );
	}

	// ---- Thinking ----

	PlayerSurvivor Nearest => PlayerSurvivor.FindNearestAlive( GlobalPosition );
	Vector3 Forward => (-GlobalBasis.Z).Flat().SafeNormal();
	Vector3 Side => GlobalBasis.X.Flat().SafeNormal();

	/// <summary> The sand under a point (the first floor below where there is none). </summary>
	float GroundAt( Vector3 p )
	{
		var g = Sculpt.GroundAt( p );
		return g != float.MinValue ? g : Sculpt.FloorBelow( this, p with { Y = p.Y + 12f }, 40f );
	}

	public override void _Process( double delta )
	{
		if ( health is null || body is null ) return;
		var dt = MathF.Min( (float)delta, 0.1f );
		var t = (float)Clock.Now;
		if ( act == Act.Dead ) { Animate( t, dt ); Topple( dt ); return; }
		if ( Puppet ) PuppetActs();
		else if ( !Frozen || act is Act.Arrive ) Think( dt );
		Animate( t, dt );
		Effects( dt );
	}

	void Think( float dt )
	{
		if ( sinceGround > 0.2f ) { sinceGround = 0f; ground = GroundAt( GlobalPosition ); }
		var prey = Nearest;
		var a = (float)sinceAct;
		if ( !Enraged && health.Fraction <= EnrageAt )
		{
			Enraged = true;
			Enrages++;
			Say( "THE HERMIT IS ENRAGED" );
			Sfx.Play( "hermit_roar", GlobalPosition, 1.6f, 0.8f );
		}
		switch ( act )
		{
			case Act.Arrive:
				// Up out of the sand where he spawned, the lamp first.
				risen = SmoothKit.Ease( a / 2.4f );
				GlobalPosition = GlobalPosition with { Y = ground - BuryDepth * (1f - risen) };
				if ( a >= 2.6f ) { risen = 1f; Begin( Act.Walk ); nextAttack = Rng.Float( 1.5f, 2.5f ); }
				break;
			case Act.Walk:
				Walk( prey, dt );
				if ( prey is null || OnlyWhenTold ) break;
				if ( nextAttack <= 0f ) Begin( Pick( prey ) );
				break;
			case Act.Slam:
				Hold( prey, dt, a < 0.8f ? 2f : 0f );
				Slam( a );
				break;
			case Act.Dig:
				Dig( a, dt );
				break;
			case Act.Buried:
				Buried( prey, a, dt );
				break;
			case Act.Erupt:
				Erupt( prey, a, dt );
				break;
			case Act.Horn:
				Hold( prey, dt, 1.5f );
				if ( a >= HornWindUp ) Blast();
				break;
			case Act.Jammed:
				Hold( null, dt, 0f );
				if ( a >= JamTime ) Done( 1f );
				break;
			case Act.Beam:
				Hold( prey, dt, a < BeamRear ? 1.5f : 0.6f );
				Beam( prey, a, dt );
				break;
			case Act.Recover:
				Hold( prey, dt, 1f );
				if ( a >= 1.1f ) Done();
				break;
		}
	}

	/// <summary> The next attack: each once in a turn (a shuffled bag), never the same twice, the horn only with no fog out, the dig only over open sand, the beam only with a clear line. </summary>
	Act Pick( PlayerSurvivor prey )
	{
		for ( var tries = 0; tries < 12; tries++ )
		{
			if ( bag.Count == 0 )
			{
				bag.AddRange( Attacks );
				for ( var i = bag.Count - 1; i > 0; i-- ) { var j = Rng.Int( 0, i ); (bag[i], bag[j]) = (bag[j], bag[i]); }
			}
			var next = bag[0];
			bag.RemoveAt( 0 );
			if ( next == lastAttack && bag.Count > 0 ) { bag.Add( next ); continue; }
			if ( next == "horn" && FogBank.Amount( this ) > 0f ) continue;
			if ( next == "burrow" && !(OpenSand( GlobalPosition ) && OpenSand( prey.RealPosition )) ) continue;
			if ( next == "beam" && !Sees( prey ) ) continue;
			lastAttack = next;
			return ActOf( next );
		}
		return Act.Slam;
	}

	static Act ActOf( string name ) => name switch
	{
		"slam" => Act.Slam,
		"burrow" => Act.Dig,
		"horn" => Act.Horn,
		"beam" => Act.Beam,
		_ => Act.Walk,
	};

	/// <summary> Tests and the dev menu: an attack now ("slam", "burrow", "horn" or "beam"). </summary>
	public void Force( string what )
	{
		if ( act is Act.Dead or Act.Arrive || IsDead || Underground ) return;
		Begin( ActOf( what ) );
	}

	void Done( float extra = 0f )
	{
		Begin( Act.Walk );
		var quick = (Enraged ? 0.7f : 1f) * (FogBank.Amount( this ) > 0.5f ? 0.65f : 1f);
		nextAttack = (Rng.Float( 2.4f, 3.8f ) + extra) * quick;
	}

	void Begin( Act next )
	{
		act = next;
		sinceAct = 0f;
		struck = false;
		ringsOut = 0;
		switch ( next )
		{
			case Act.Arrive: Say( "THE LIGHTHOUSE HERMIT" ); break;
			case Act.Slam:
				Say( "CLAW SLAM" );
				Slams++;
				Sfx.Play( "hermit_roar", GlobalPosition, 1.1f, 1.2f );
				break;
			case Act.Dig:
				Say( "BURROW AMBUSH" );
				Burrows++;
				digFrom = GlobalPosition;
				Sfx.Play( "rock_crumble", GlobalPosition, 1.3f, 0.6f );
				break;
			case Act.Erupt: Sfx.Play( "whirl_warn", eruptAt, 1.2f, 0.7f ); break;
			case Act.Horn:
				Say( "FOGHORN" );
				Horns++;
				hornTaken = 0f;
				Sfx.Play( "foghorn_wind", GlobalPosition, 1.4f, 1f );
				break;
			case Act.Jammed:
				Say( "THE HORN JAMMED. HIT HIM" );
				Jams++;
				Sfx.Play( "hermit_roar", GlobalPosition, 1.3f, 1.5f );
				break;
			case Act.Beam:
				Say( "LANTERN BEAM" );
				Beams++;
				bellyTaken = 0f;
				burned.Clear();
				beamAt = Nearest?.RealPosition ?? GlobalPosition + Forward * 10f;
				Sfx.Play( "lamp_focus", LampAt, 1.4f, 1f );
				break;
		}
	}

	/// <summary> Scuttles sideways round the nearest player, claws toward them, closer while his fog is out. </summary>
	void Walk( PlayerSurvivor prey, float dt )
	{
		var wish = Vector3.Zero;
		if ( prey is not null )
		{
			var fog = FogBank.Amount( this ) > 0.5f;
			var keep = fog ? 5f : KeepAway;
			orbit += orbitSign * dt * (fog ? 0.35f : 0.24f);
			var spot = Sculpt.InBounds( prey.RealPosition + new Vector3( MathF.Cos( orbit ), 0f, MathF.Sin( orbit ) ) * keep );
			var to = (spot - GlobalPosition).Flat();
			var speed = WalkSpeed * (Enraged ? 1.2f : 1f) * (fog ? 1.4f : 1f);
			wish = to.Length() > 0.8f ? to.Normalized() * MathF.Min( speed, to.Length() * 1.2f ) : Vector3.Zero;
			Face( prey.RealPosition - GlobalPosition, 2f, dt );
		}
		velocity = velocity.Lerp( wish, 1f - MathF.Exp( -2.2f * dt ) );
		Move( dt );
	}

	/// <summary> Holds his place (easing to a stop), turning to face the player. </summary>
	void Hold( PlayerSurvivor prey, float dt, float turn )
	{
		velocity = velocity.Lerp( Vector3.Zero, 1f - MathF.Exp( -4f * dt ) );
		Move( dt );
		if ( prey is not null && turn > 0f ) Face( prey.RealPosition - GlobalPosition, turn, dt );
	}

	/// <summary> On the sand, round rocks and stations, inside the map. </summary>
	void Move( float dt )
	{
		var next = GlobalPosition + velocity * dt;
		var ahead = velocity.Flat().SafeNormal();
		if ( ahead != Vector3.Zero )
			foreach ( var up in new[] { 0.9f, 2.4f, 4f } )
			{
				var from = GlobalPosition + Vector3.Up * up;
				var hit = Trace.Ray( this, from, from + ahead * 3.4f, Layers.World );
				if ( !hit.Hit || hit.Normal.Y > 0.7f ) continue;
				velocity = velocity.Slide( hit.Normal.Flat().SafeNormal() );
				next = GlobalPosition + velocity * dt;
				break;
			}
		next = Sculpt.InBounds( next );
		next.Y = MathX.Lerp( GlobalPosition.Y, GroundAt( next ) - BuryDepth * (1f - risen), 1f - MathF.Exp( -10f * dt ) );
		GlobalPosition = next;
	}

	/// <summary> Sea bed sand here with nothing standing on it (he can only dig in that). </summary>
	bool OpenSand( Vector3 at )
	{
		if ( !SeaTerrain.Active || (SeaTerrain.Area == SeaTerrain.Globe && Atlantis.Inside( at, 0f )) ) return false;
		var sand = Sculpt.GroundAt( at );
		if ( sand == float.MinValue ) return false;
		var top = Sculpt.FloorBelow( this, at with { Y = sand + 6f }, 7f );
		return top < sand + 0.35f;
	}

	static bool OnGround( PlayerSurvivor p )
	{
		var feet = p.RealPosition;
		var sand = Sculpt.GroundAt( feet );
		return sand != float.MinValue && feet.Y - sand < 0.8f;
	}

	/// <summary> Can his lamp see this player (nothing solid between the lamp and their chest)? </summary>
	bool Sees( PlayerSurvivor p )
	{
		if ( p is null ) return false;
		var chest = p.RealPosition + Vector3.Up * 1f;
		return !Trace.Ray( this, LampAt, chest, Layers.World ).Hit;
	}

	// ---- Claw Slam ----

	/// <summary> Where the claws come down: the sand in front of him. </summary>
	Vector3 SlamAt => (GlobalPosition + Forward * 3.4f) with { Y = GroundAt( GlobalPosition + Forward * 3.4f ) };

	void Slam( float a )
	{
		const float down = 0.9f;
		if ( !struck && a >= down )
		{
			struck = true;
			var at = SlamAt;
			foreach ( var p in PlayerSurvivor.All )
			{
				if ( !p.IsAlive || p.Controller is not { } c ) continue;
				var flat = (p.RealPosition - at).Flat();
				if ( flat.Length() > SlamReach + c.BodyRadius || p.RealPosition.Y - at.Y > 3f ) continue;
				SlamHits++;
				p.Health?.OnDamage( new DamageInfo { Damage = SlamDamage, Attacker = this, Position = p.RealPosition + Vector3.Up, Origin = at } );
				if ( !c.Scripted ) NetPlayer.Push( c, flat.SafeNormal() * 9f + Vector3.Up * 4f );
				p.Juice?.Shake( 0.9f );
			}
			SlamFx( at );
		}
		var rings = SlamRings + (Enraged ? 1 : 0);
		while ( struck && ringsOut < rings && a >= down + ringsOut * RingGap )
		{
			ShockRing.Send( GetTree().CurrentScene, SlamAt, RingSpeed, RingReach, Puppet ? 0f : RingDamage, 0f, this, sand: true );
			ringsOut++;
		}
		if ( a >= down + 1.1f ) Done();
	}

	void SlamFx( Vector3 at )
	{
		Sfx.Play( "hermit_slam", at, 1.6f, 0.9f );
		for ( var i = 0; i < 36; i++ )
		{
			var dir = (Rng.OnSphere().Flat() + Vector3.Up * 0.5f).SafeNormal();
			GpuFx.SandPuff( at + Vector3.Up * 0.2f + Rng.InSphere( 1f ).Flat(), dir * Rng.Float( 2f, 6f ), SeaStir.SiltColour( at ), Rng.Float( 10f, 18f ), Rng.Float( 0.8f, 1.5f ) );
		}
		SeaStir.Blast( at, 8f );
		foreach ( var pl in PlayerSurvivor.All ) if ( pl.Controller is { } c && c.GlobalPosition.DistanceTo( at ) < 30f ) pl.Juice?.Shake( 0.6f );
	}

	// ---- Burrow Ambush ----

	void Dig( float a, float dt )
	{
		const float time = 1.4f;
		velocity = Vector3.Zero;
		risen = 1f - SmoothKit.Ease( a / time );
		GlobalPosition = GlobalPosition with { Y = MathX.Lerp( GlobalPosition.Y, ground - BuryDepth * (1f - risen), 1f - MathF.Exp( -12f * dt ) ) };
		RotateY( MathF.Sin( a * 18f ) * dt * 0.8f );
		if ( a >= time ) { risen = 0f; Begin( Act.Buried ); }
	}

	void Buried( PlayerSurvivor prey, float a, float dt )
	{
		// Under the sand toward the player, only the lamp room showing, ploughing a furrow.
		var feet = prey?.RealPosition ?? GlobalPosition;
		var to = (feet - GlobalPosition).Flat();
		var way = to.SafeNormal();
		if ( way != Vector3.Zero ) Face( way, 4f, dt );
		velocity = way * MathF.Min( BuriedSpeed * (Enraged ? 1.15f : 1f), to.Length() * 3f );
		var next = Sculpt.InBounds( GlobalPosition + velocity * dt );
		next.Y = GroundAt( next ) - BuryDepth;
		GlobalPosition = next;
		if ( prey is null || (to.Length() < 1.4f && OnGround( prey )) || a > BuriedMax )
		{
			var spot = prey is not null && to.Length() < 3f && OpenSand( feet ) ? feet : next;
			eruptAt = spot with { Y = GroundAt( spot ) };
			Begin( Act.Erupt );
		}
	}

	void Erupt( PlayerSurvivor prey, float a, float dt )
	{
		if ( a < EruptCue )
		{
			// Still under, sliding beneath the glowing ring.
			var to = (eruptAt - GlobalPosition).Flat();
			var next = GlobalPosition + to * MathF.Min( 1f, 8f * dt );
			next.Y = GroundAt( next ) - BuryDepth;
			GlobalPosition = next;
			return;
		}
		if ( !struck )
		{
			struck = true;
			EruptHit();
			EruptFx();
		}
		risen = MathF.Min( 1f, risen + dt / 0.35f );
		GlobalPosition = GlobalPosition with { Y = GroundAt( GlobalPosition ) - BuryDepth * (1f - risen) };
		if ( prey is not null ) Face( prey.RealPosition - GlobalPosition, 2f, dt );
		if ( a >= EruptCue + 1.1f ) { risen = 1f; Begin( Act.Recover ); }
	}

	/// <summary> He bursts up: everyone over the glowing ring is thrown into the air. </summary>
	void EruptHit()
	{
		Eruptions++;
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c ) continue;
			var feet = p.RealPosition;
			var flat = (feet - eruptAt).Flat();
			if ( flat.Length() > EruptRadius + c.BodyRadius || feet.Y - eruptAt.Y > 3f || feet.Y - eruptAt.Y < -1.5f ) continue;
			EruptHits++;
			p.Health?.OnDamage( new DamageInfo { Damage = EruptDamage, Attacker = this, Position = feet + Vector3.Up, Origin = eruptAt } );
			if ( !c.Scripted ) { c.Jump( Vector3.Up * EruptLaunch ); NetPlayer.Push( c, flat.SafeNormal() * 4f ); }
			p.Juice?.Shake( 1.1f );
		}
	}

	void EruptFx()
	{
		Sfx.Play( "hermit_slam", eruptAt, 1.8f, 0.7f );
		Sfx.Play( "hermit_roar", eruptAt, 1.4f, 0.9f );
		for ( var i = 0; i < 50; i++ )
		{
			var dir = (Rng.OnSphere().Flat() * 0.6f + Vector3.Up).SafeNormal();
			GpuFx.SandPuff( eruptAt + Rng.InSphere( EruptRadius * 0.7f ).Flat(), dir * Rng.Float( 3f, 9f ), SeaStir.SiltColour( eruptAt ), Rng.Float( 10f, 20f ), Rng.Float( 0.9f, 1.8f ) );
		}
		SeaStir.Blast( eruptAt, 10f );
		foreach ( var pl in PlayerSurvivor.All ) if ( pl.Controller is { } c && c.GlobalPosition.DistanceTo( eruptAt ) < 30f ) pl.Juice?.Shake( 0.8f );
	}

	// ---- Foghorn Blast ----

	void Blast()
	{
		Fogs++;
		var at = GlobalPosition with { Y = ground };
		fogs++;
		fogAt = at;
		FogBank.Roll( this, at, FogRadius, FogTime );
		HornFx( at );
		// The blast shoves everyone near back a little.
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c || c.Scripted ) continue;
			var away = (p.RealPosition - at).Flat();
			if ( away.Length() > 16f ) continue;
			NetPlayer.Push( c, away.SafeNormal() * 5f * (1f - away.Length() / 16f) + Vector3.Up * 1.5f );
		}
		CallCrabs();
		Done( -0.8f );
	}

	void HornFx( Vector3 at )
	{
		Sfx.Play( "foghorn_blast", HornAt, 2f, 1f );
		Sfx.Play2D( "foghorn_blast", 0.35f, 1f );
		ShockRing.Send( GetTree().CurrentScene, at, 22f, FogRadius, 0f, 0f, this, sand: true );
		for ( var i = 0; i < 30; i++ ) WaterBubble.Create( HornAt + Forward * 0.6f + Rng.InSphere( 0.6f ), Rng.Float( 0.2f, 0.6f ), 0f, Rng.Float( 0.6f, 1.2f ) );
		foreach ( var pl in PlayerSurvivor.All ) if ( pl.Controller is { } c && c.GlobalPosition.DistanceTo( at ) < 45f ) pl.Juice?.Shake( 0.7f );
	}

	/// <summary> Small hermit crabs scuttle in from the edge of the fog (at most <see cref="MaxCrabs"/> of his at once). </summary>
	void CallCrabs()
	{
		called.RemoveAll( c => c is null || !IsInstanceValid( c ) || c.IsDead );
		var count = Math.Min( FogCrabs + (Enraged ? 1 : 0), MaxCrabs - called.Count );
		var d = WaveDirector.Current;
		var near = Nearest?.RealPosition ?? GlobalPosition;
		for ( var i = 0; i < count; i++ )
		{
			Vector3 at = default;
			var found = false;
			for ( var tries = 0; tries < 8 && !found; tries++ )
			{
				var angle = Rng.Float( 0f, MathF.Tau );
				at = Sculpt.InBounds( near + new Vector3( MathF.Cos( angle ), 0f, MathF.Sin( angle ) ) * Rng.Float( 12f, 18f ) );
				at = Nav.Closest( this, at );
				found = OpenSand( at );
			}
			if ( !found ) continue;
			Creature crab = d is not null ? d.SpawnAt( WaveDirector.EnemyKind.HermitCrab, at, near ) as Creature : HermitCrab.Spawn( GetTree().CurrentScene, at, CrabHealth );
			if ( crab is null ) continue;
			called.Add( crab );
			FogCrabsCalled++;
		}
	}

	// ---- Lantern Beam ----

	/// <summary> The beam is burning (after the rear and the charge). </summary>
	public bool Burning => act == Act.Beam && (float)sinceAct >= BeamRear + BeamCharge && (float)sinceAct < BeamRear + BeamCharge + BeamTime;
	public Vector3 BeamAt => beamAt;

	void Beam( PlayerSurvivor prey, float a, float dt )
	{
		var feet = prey?.RealPosition ?? beamAt;
		if ( a < BeamRear + BeamCharge )
		{
			// Rearing and focusing: the mark follows the player exactly.
			beamAt = feet;
			return;
		}
		if ( a < BeamRear + BeamCharge + BeamTime )
		{
			if ( !struck ) { struck = true; Sfx.Play( "beam_fire", LampAt, 1.4f, 0.55f ); }
			var to = feet - beamAt;
			var step = BeamTrack * (Enraged ? 1.3f : 1f) * dt;
			beamAt += to.Length() > step ? to.Normalized() * step : to;
			var (from, end) = BeamLine();
			LampTrail.Leave( this, end, Puppet ? 0f : TrailDps );
			foreach ( var p in PlayerSurvivor.All )
			{
				if ( !p.IsAlive || p.Controller is not { } c ) continue;
				var chest = p.RealPosition + Vector3.Up * 0.9f;
				var closest = Geometry3D.GetClosestPointToSegment( chest, from, end );
				if ( closest.DistanceTo( chest ) > BeamWidth + c.BodyRadius ) continue;
				burned.TryGetValue( p, out var sum );
				sum += BeamDps * dt;
				if ( sum >= 6f )
				{
					p.Health?.OnDamage( new DamageInfo { Damage = sum, Attacker = this, Position = chest, Origin = from } );
					BeamDamage += sum;
					BeamHits++;
					sum = 0f;
					p.Juice?.Shake( 0.25f );
				}
				burned[p] = sum;
			}
			return;
		}
		if ( a >= BeamRear + BeamCharge + BeamTime + 0.8f ) Done();
	}

	/// <summary> The beam from the lamp toward the mark, stopped by the first wall or the floor. </summary>
	public (Vector3 From, Vector3 To) BeamLine()
	{
		var from = LampAt;
		var dir = (beamAt + Vector3.Up * 0.3f - from).SafeNormal();
		from += dir * 1.2f;   // out through the lamp room's glass, clear of the gallery deck
		var far = from + dir * 80f;
		var hit = Trace.Ray( this, from, far, Layers.World );
		return (from, hit.Hit ? hit.HitPosition : far);
	}

	// ---- Hits and death ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( NetCreatures.Intercept( this, index, ref damage, direction, position, origin, shooter, scoreHit ) ) return;
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		var scale = index switch
		{
			LampIndex => LampMultiplier,
			EyeIndex => EyeMultiplier,
			BellyIndex => BellyOpen ? BellyMultiplier : 1f,
			TowerIndex => TowerMultiplier,
			BigClawIndex or SmallClawIndex => ClawMultiplier,
			LegIndex => LegMultiplier,
			_ => 1f,
		};
		if ( act == Act.Jammed ) scale *= JamMultiplier;
		var dealt = damage * scale;
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = dealt, Position = position, Origin = origin, Attacker = shooter } );
		if ( index == HornIndex ) Sfx.Play( "magnet_clang", position, 0.6f, Rng.Float( 0.7f, 0.9f ) );
		if ( index == LampIndex ) Sfx.Play( "glass_hit", position, 0.6f, Rng.Float( 0.8f, 1f ) );
		if ( index == TowerIndex ) { Sfx.Play( "hit_world", position, 0.6f, Rng.Float( 0.5f, 0.7f ) ); GibKit.Chips( GibRecipes.CaveRock(), position, -direction, direction, 0.5f, 1 ); }
		else gore?.GooBurst( position, -direction, GooColor, 2, 160f, 0.6f );
		if ( index is LampIndex or EyeIndex or BellyIndex ) flash = 1f;
		if ( !Puppet )
		{
			// The horn jammed: enough shot into it while it winds up.
			if ( act == Act.Horn && index == HornIndex )
			{
				hornTaken += dealt;
				if ( hornTaken >= HornShare * health.MaxHealth && !IsDead ) Begin( Act.Jammed );
			}
			// The light put out: enough shot into his belly while the beam is up.
			if ( act == Act.Beam && index == BellyIndex && BellyOpen )
			{
				bellyTaken += dealt;
				if ( bellyTaken >= BellyShare * health.MaxHealth && !IsDead ) { LightsOut++; Say( "THE LIGHT GOES OUT" ); Sfx.Play( "hermit_roar", GlobalPosition, 1.3f, 1.4f ); Begin( Act.Recover ); nextAttack = 3f; }
			}
		}
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, WeakSpot( index ), scoreHit );
	}

	protected override void OnDied()
	{
		StopBeam();
		act = Act.Dead;
		sinceAct = 0f;
		RaiseKilled();
		Say( "" );
		FogBank.Lift( this );
		Sfx.Play( "hermit_roar", GlobalPosition, 1.8f, 0.6f );
		Sfx.Play( "foghorn_blast", GlobalPosition, 1.4f, 0.7f );
		gore?.GooBurst( GlobalPosition + Vector3.Up * 2f, Vector3.Up, GooColor, 20, 380f, 1.6f );
		foreach ( var part in FindChildren( "*", "StaticBody3D", true, false ).OfType<CreaturePart>().ToList() ) part.QueueFree();
	}

	public const float ToppleSeconds = 1.4f;

	/// <summary> Dead: the lamp flickers out, the tower creaks over, the crab shakes and spurts, then it all bursts. </summary>
	void Topple( float dt )
	{
		if ( burst ) return;
		var t = MathX.Clamp( (float)timeSinceDeath / ToppleSeconds, 0f, 1f );
		dying = t;
		body.Position = Rng.InSphere( 0.12f * t );
		if ( Rng.Float() < 0.3f + t * 0.5f )
		{
			var from = thorax.GlobalPosition + Rng.InSphere( 1.2f );
			gore?.GooSpray( from, (from - thorax.GlobalPosition).SafeNormal() + Vector3.Up * 0.3f, 4, Rng.Float() < 0.5f ? GooColor : Flesh[Rng.Int( 0, 2 )], 260f );
		}
		if ( t >= 1f ) Burst();
	}

	/// <summary> The tower breaks into its sections, gallery, lamp room and cap; the claws, legs and eyes fly; goo everywhere. </summary>
	void Burst()
	{
		burst = true;
		var centre = thorax.GlobalPosition;
		var away = lastShotDirection.Flat().SafeNormal();
		Gib Loose( Node3D piece, float radiusMetres, float mass, float speed, GibKind stuff, Color goo )
		{
			if ( piece is null || !IsInstanceValid( piece ) ) return null;
			var dir = (piece.GlobalPosition - centre).SafeNormal();
			if ( dir == Vector3.Zero ) dir = Vector3.Up;
			var v = (dir + away * 0.3f + Vector3.Up * 0.4f + Rng.InSphere( 0.35f )).SafeNormal() * speed * Rng.Float( 0.7f, 1.2f );
			var gib = Gib.Attach( piece, gore, radiusMetres / U.In, 80f, mass, v, goo );
			gib.Stuff = stuff;
			gib.AngularVelocity = Rng.InSphere( 3f );
			return gib;
		}
		foreach ( var section in sections ) Loose( section, 1.5f, 60f, 4f, GibKind.Rock, new Color( 0.6f, 0.55f, 0.5f ) );
		Loose( lampRoom, 1.1f, 20f, 6f, GibKind.Glass, GooColor );
		Loose( cap, 1f, 15f, 7f, GibKind.Armour, GooColor );
		Loose( horn, 0.5f, 6f, 9f, GibKind.Armour, GooColor );
		Loose( lampEye, 0.6f, 4f, 10f, GibKind.Flesh, GooColor );
		foreach ( var claw in new[] { bigClaw, smallClaw } ) if ( Loose( claw, 0.9f, 10f, 9f, GibKind.Shell, GooColor ) is not null ) CreatureGore.AddStump( claw, claw.GlobalPosition, Vector3.Up, 0.3f / U.In, Flesh[0], GooColor, 8f, Gibs );
		foreach ( var leg in legs ) foreach ( var seg in leg.Segments ) Loose( seg, 0.3f, 3f, 8f, GibKind.Shell, GooColor );
		foreach ( var stalk in stalks ) Loose( stalk, 0.25f, 1f, 10f, GibKind.Flesh, GooColor );
		Loose( abdomen, 1f, 12f, 6f, GibKind.Flesh, GooColor );
		Loose( thorax, 1.2f, 20f, 5f, GibKind.Shell, GooColor );
		for ( var i = 0; i < 4; i++ ) GibKit.Burst( Gibs, centre + Rng.InSphere( 1.4f ), Rng.InSphere( 1f ) + Vector3.Up * 0.6f, 0.4f, 8 );
		for ( var i = 0; i < 3; i++ ) GibKit.Burst( GibRecipes.CaveRock(), centre + Vector3.Up * 4f + Rng.InSphere( 2f ), Rng.InSphere( 1f ) + Vector3.Up * 0.4f, 0.4f, 8 );
		for ( var i = 0; i < 4; i++ ) CreatureGore.Overkill( centre + Rng.InSphere( 1.2f ), Rng.InSphere( 1f ) + Vector3.Up * 0.4f, Flesh[i % 3], GooColor, 2.2f, Gibs );
		gore?.GooBurst( centre, Vector3.Up, GooColor, 60, 620f, 2.6f );
		gore?.GooBurst( centre, away + Vector3.Up * 0.3f, Flesh[0], 30, 480f, 2f );
		for ( var i = 0; i < 14; i++ )
		{
			var angle = i / 14f * MathF.Tau;
			gore?.GooSpray( centre, new Vector3( MathF.Cos( angle ), 0.35f, MathF.Sin( angle ) ), 6, i % 2 == 0 ? GooColor : Flesh[i % 3], 520f, 0.8f, 2f );
		}
		for ( var i = 0; i < 40; i++ ) GpuFx.SandPuff( GlobalPosition + Rng.InSphere( 5f ).Flat(), (Rng.OnSphere().Flat() + Vector3.Up).SafeNormal() * Rng.Float( 2f, 6f ), SeaStir.SiltColour( GlobalPosition ), Rng.Float( 12f, 20f ), Rng.Float( 1f, 2f ) );
		for ( var i = 0; i < 50; i++ ) WaterBubble.Create( centre + Rng.InSphere( 3f ), Rng.Float( 0.4f, 1.4f ), 0f, Rng.Float( 0.6f, 1.6f ) );
		AlienBlast.Spawn( GetTree().CurrentScene, centre, 2f );
		gore?.Squelch( centre, 1.6f, 0.7f );
		Sfx.Play( "gib", centre, 1f, 0.7f );
		Sfx.Play( "rock_crumble", centre + Vector3.Up * 4f, 1.6f, 0.6f );
		Sfx.Play2D( "boss_spawn", 0.9f, 0.5f );
		foreach ( var player in PlayerSurvivor.All )
			if ( player.Controller is { } pc && pc.GlobalPosition.DistanceTo( centre ) < 45f ) player.Juice?.Shake( 1.2f );
		if ( Engine.TimeScale == 1.0 && !AutoTest.Requested )
		{
			Engine.TimeScale = 0.3;
			GetTree().CreateTimer( 0.45, true, false, true ).Timeout += () => { if ( Engine.TimeScale == 0.3 ) Engine.TimeScale = 1.0; };
		}
		if ( AutoTest.Requested ) GD.Print( "[autotest] hermit burst into pieces" );
		QueueFree();
	}
}
