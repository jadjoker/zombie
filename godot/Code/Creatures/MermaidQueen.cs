using Godot;

/// <summary>
/// The Mermaid Queen (user's pick): Atlantis's wave 35 boss, the siren who rules the drowned city, built on the mermaids'
/// smooth kit at about three times their size and made alien: an iridescent pearl hide with glowing green veins, a long
/// swept-back skull, four black almond eyes on green rims, a mouth of four petals that splits open on a glowing throat,
/// a crown of glowing crystal spires, tentacle hair with lit tips, a jellyfish ruff, a gold shell collar and bodice with a
/// glowing pearl for a heart, and a sceptre with an alien pearl in a coral claw. She swims round the player at a distance,
/// darting across the city in a rush of bubbles when they run, and has five attacks:
/// - Siren Song: her throat opens and she sings; every player who can see her is drawn toward her and their view turns to
///   her, and one that reaches her is bitten. Shooting her throat or her heart enough (<see cref="ChokeShare"/>) chokes the
///   song off and leaves her reeling (<see cref="Act.Choke"/>, she takes more from everything);
/// - Pearl Storm: pearls gather round her sceptre and she flings them one by one; they home in slowly and burst, and can
///   be shot down;
/// - Riptide: she lashes her tail and sends crescents of water rolling out along the ground in a fan (jump them);
/// - Royal Guard: mermen in gold helmets come out of whirlpools beside her and join the wave;
/// - Abyssal Gaze: her four eyes charge, a thin line marks where she looks, then a beam burns along the ground after the
///   player (walls stop it).
/// At half health she ascends (<see cref="Act.Ascend"/>): she screams, her crown grows and blazes, her mouth stays split
/// and her hair lights up, and everything comes faster and harder. Dead, she swells and bursts. Model units (her body
/// node is scaled by <see cref="Scale"/>), facing -Z. Looks in MermaidQueenLook.cs, motion in MermaidQueenMotion.cs,
/// online in MermaidQueenNet.cs, the pearls, waves, beam and song rings in MermaidQueenFx.cs.
/// </summary>
public partial class MermaidQueen : Creature
{
	public static readonly List<MermaidQueen> All = new();
	public override bool CanScare => false;
	public override float AreaDamageScale => 0.6f;
	public override Color Blood => GooColor;
	public override GibRecipe Gibs => GibRecipes.Mermaid( GooColor, SkinColour );
	public override bool WeakSpot( int index ) => index is 0 or 1 || (index == ThroatIndex && ThroatOpen);
	public override string Voice => "spot_mermaid";
	public override float VoicePitch => 0.6f;

	/// <summary> Metres per model unit: about four mermaids (theirs is 0.345), so near 10 m from crown to fluke. </summary>
	public new const float Scale = 1.35f;
	public float HoverHeight = 6.2f;     // her waist over the floor (the tail hangs about 6 m)
	public float KeepAway = 13f;
	public float SwimSpeed = 5.5f;
	public float DashSpeed = 22f;
	// Siren Song.
	public float SongRange = 42f;
	public float SongWindUp = 1.1f;
	public float SongTime = 5f;
	public float SongPull = 3.5f;
	public float BiteRange = 3.8f;
	public float BiteDamage = 40f;
	/// <summary> This much of her full health shot into her throat or heart during the song chokes it off. </summary>
	public float ChokeShare = 0.03f;
	public float ChokeTime = 3.2f;
	public float ChokeMultiplier = 1.5f;
	// Pearl Storm.
	public int PearlCount = 6;
	public float PearlDamage = 14f;
	public float PearlSpeed = 11f;
	public float PearlRadius = 2.6f;
	// Riptide.
	public int WaveCount = 3;
	public float WaveDamage = 22f;
	public float WaveSpeed = 13f;
	public float WaveReach = 42f;
	/// <summary> Each crescent's half width (radians) and the fan's spread between them. </summary>
	public float WaveHalfArc = 0.32f, WaveSpread = 0.62f;
	// Royal Guard.
	public int GuardCount = 2;
	public int MaxGuards = 4;
	public float GuardHealth = 1300f;
	// Abyssal Gaze.
	public float GazeCharge = 1.6f;
	public float GazeTime = 2.8f;
	public float BeamDps = 34f;
	public float BeamWidth = 1.1f;
	public float BeamTrack = 6f;
	// Hits.
	public float HeadMultiplier = 2f;
	public float HeartMultiplier = 2.5f;
	public float ThroatMultiplier = 3f;
	public float TailMultiplier = 0.6f;
	/// <summary> At this much health left she ascends. </summary>
	public float AscendAt = 0.5f;

	public const int ThroatIndex = 7;
	public static readonly string[] Attacks = { "song", "pearls", "riptide", "guard", "gaze" };

	/// <summary> Tests: she only attacks when told (Force), and what she has done. </summary>
	public bool OnlyWhenTold;
	public static int Songs, Bites, Chokes, PearlsThrown, PearlHits, PearlsShot, Riptides, WaveHits, WaveJumps, Guards, Gazes, BeamHits, Dashes, Ascents;
	public static float BeamDamage;
	/// <summary> The local player is under her song (the HUD's prompt and the pink at the edges). </summary>
	public static bool LocalCharmed => Clock.Now < charmedUntil;
	static double charmedUntil;

	static readonly Color SkinColour = new( 0.74f, 0.66f, 0.96f );
	static readonly Color GooColor = new( 0.55f, 0.35f, 1f );
	static readonly Color[] Flesh = { new( 0.74f, 0.66f, 0.96f ), new( 0.4f, 0.85f, 0.85f ), new( 0.86f, 0.4f, 0.8f ) };

	public enum Act { Arrive, Swim, Dash, Song, Bite, Pearls, Riptide, Guard, Gaze, Choke, Ascend, Dead }
	Act act = Act.Arrive;
	TimeSince sinceAct;
	public Act Doing => act;
	public bool Ascended { get; private set; }
	public float HealthFraction => health?.Fraction ?? 0f;
	/// <summary> Her throat is open (the song): shots there do triple. </summary>
	public bool ThroatOpen => act is Act.Song or Act.Ascend || (act == Act.Bite && (float)sinceAct < 0.4f);

	public string Line { get; private set; } = "";
	public TimeSince TimeSinceLine { get; private set; } = 999f;
	void Say( string line ) { Line = line; TimeSinceLine = 0f; }

	Vector3 velocity;
	float orbit, orbitSign = 1f;
	float floorY;
	TimeSince sinceFloor = 99f, sinceHit = 99f;
	TimeUntil nextAttack = 3f, nextDash = 14f;
	readonly List<string> bag = new();
	string lastAttack;
	bool struck;
	float chokeTaken;
	PlayerSurvivor target, bitten;
	Vector3 dashFrom, dashTo;
	/// <summary> How much of her is there (she grows out of a whirlpool); on the body node. </summary>
	float shown = 0.05f;
	bool burst;
	// The gaze: where it looks (on the ground), and what it has burned this beat.
	Vector3 gazeAt;
	readonly Dictionary<PlayerSurvivor, float> burned = new();
	// The pearls round her sceptre, waiting to be thrown.
	readonly List<QueenPearl> held = new();
	int pearlsLeft;
	TimeSince sinceThrow;
	// The waves she sent last (online, a joiner draws the same ones).
	int waves;
	Vector3 waveFrom, waveDir;
	int pearls;
	Vector3 pearlFrom, pearlVelocity;
	TimeSince sinceRing = 99f;

	public static MermaidQueen Spawn( Node parent, Vector3 position, float maxHealth ) =>
		Place( new MermaidQueen { Name = "Mermaid Queen" }, parent, position, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	protected override void Setup()
	{
		BeginSetup( 28000f );
		orbit = Rng.Float( 0f, MathF.Tau );
		orbitSign = Rng.Float() < 0.5f ? -1f : 1f;
		floorY = Sculpt.FloorBelow( this, GlobalPosition, 80f );
		Build();
		Begin( Act.Arrive );
		PoseidonVortex.Burst( this, GlobalPosition + Vector3.Down * HoverHeight * 0.9f, 1.6f );
		Sfx.Play2D( "boss_spawn", 0.9f, 1.15f );
		Sfx.Play( "queen_song", GlobalPosition, 1.2f, 1.2f );
	}

	protected override void Cleanup()
	{
		foreach ( var p in held ) if ( IsInstanceValid( p ) ) p.QueueFree();
		held.Clear();
		if ( beam is not null && IsInstanceValid( beam ) ) beam.QueueFree();
	}

	// ---- Thinking ----

	PlayerSurvivor Nearest => PlayerSurvivor.FindNearestAlive( GlobalPosition );
	/// <summary> The point on the floor in front of her that her song draws players to. </summary>
	Vector3 Lap
	{
		get
		{
			var p = GlobalPosition + Forward * 3.6f;
			return Sculpt.InBounds( p ) with { Y = floorY };
		}
	}
	Vector3 Forward => (-GlobalBasis.Z).Flat().SafeNormal();

	public override void _Process( double delta )
	{
		if ( health is null || body is null ) return;
		var dt = MathF.Min( (float)delta, 0.1f );
		var t = (float)Clock.Now;
		if ( act == Act.Dead ) { Animate( t, dt ); Swell( dt ); return; }
		if ( Puppet ) PuppetActs();
		else if ( !Frozen || act is Act.Arrive ) Think( dt );
		Animate( t, dt );
		Effects( dt );
	}

	void Think( float dt )
	{
		if ( sinceFloor > 0.3f ) { sinceFloor = 0f; floorY = Sculpt.FloorBelow( this, GlobalPosition, 80f ); }
		var prey = Nearest;
		var a = (float)sinceAct;
		// Half health: she ascends (once), whatever she was doing.
		if ( !Ascended && health.Fraction <= AscendAt && act is not (Act.Arrive or Act.Bite or Act.Ascend or Act.Dead) ) { Begin( Act.Ascend ); return; }
		switch ( act )
		{
			case Act.Arrive:
				shown = MathX.Lerp( 0.05f, 1f, SmoothKit.Ease( a / 1.4f ) );
				RotateY( dt * 6f * (1f - MathX.Clamp( a / 1.4f, 0f, 1f )) );
				if ( a >= 1.5f ) { shown = 1f; Begin( Act.Swim ); nextAttack = Rng.Float( 1.5f, 2.5f ); }
				break;
			case Act.Swim:
				Swim( prey, dt );
				if ( prey is null || OnlyWhenTold ) break;
				if ( nextDash <= 0f || prey.RealPosition.Flat().DistanceTo( GlobalPosition.Flat() ) > 34f ) { StartDash( prey ); break; }
				if ( nextAttack <= 0f ) Begin( Pick() );
				break;
			case Act.Dash:
				Dash( a, dt );
				break;
			case Act.Song:
				Hold( prey, dt, 2.5f );
				Song( a, dt );
				break;
			case Act.Bite:
				Bite( a, dt );
				break;
			case Act.Pearls:
				Hold( prey, dt, 3f );
				Pearls( a );
				break;
			case Act.Riptide:
				Hold( prey, dt, 4f );
				if ( !struck && a >= 0.9f ) { struck = true; SendWaves( prey ); }
				if ( a >= 1.6f ) Done();
				break;
			case Act.Guard:
				Hold( prey, dt, 2f );
				if ( !struck && a >= 1.4f ) { struck = true; CallGuard(); }
				if ( a >= 2.1f ) Done();
				break;
			case Act.Gaze:
				Hold( prey, dt, 2f );
				Gaze( prey, a, dt );
				break;
			case Act.Choke:
				Hold( null, dt, 0f );
				if ( a >= ChokeTime ) Done( 1.2f );
				break;
			case Act.Ascend:
				Hold( prey, dt, 1f );
				Ascend( a );
				break;
		}
	}

	/// <summary> The next attack: each once in a turn (a shuffled bag), never the same twice running, the guard only when there is room. </summary>
	Act Pick()
	{
		var guards = Mermaid.All.Count( m => !m.IsDead && m.Guard == this );
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
			if ( next == "guard" && guards >= MaxGuards ) continue;
			lastAttack = next;
			return ActOf( next );
		}
		return Act.Pearls;
	}

	static Act ActOf( string name ) => name switch
	{
		"song" => Act.Song,
		"pearls" => Act.Pearls,
		"riptide" => Act.Riptide,
		"guard" => Act.Guard,
		"gaze" => Act.Gaze,
		"dash" => Act.Dash,
		_ => Act.Swim,
	};

	/// <summary> Tests and the dev menu: an attack now ("song", "pearls", "riptide", "guard", "gaze", "dash" or "ascend"). </summary>
	public void Force( string what )
	{
		if ( act is Act.Dead or Act.Arrive || IsDead ) return;
		ClearHeld();
		if ( what == "dash" ) { if ( Nearest is { } p ) StartDash( p ); return; }
		if ( what == "ascend" ) { if ( !Ascended ) Begin( Act.Ascend ); return; }
		Begin( ActOf( what ) );
	}

	void Done( float extra = 0f )
	{
		Begin( Act.Swim );
		var quick = Ascended ? 0.65f : 1f;
		nextAttack = (Rng.Float( 2.2f, 3.6f ) + extra) * quick;
	}

	void Begin( Act next )
	{
		if ( act == Act.Gaze && next != Act.Gaze ) StopBeam();
		act = next;
		sinceAct = 0f;
		struck = false;
		switch ( next )
		{
			case Act.Arrive: Say( "THE MERMAID QUEEN" ); break;
			case Act.Song:
				Say( "SIREN SONG" );
				Songs++;
				chokeTaken = 0f;
				Sfx.Play( "queen_charge", GlobalPosition, 1.2f, 1.1f );
				break;
			case Act.Pearls:
				Say( "PEARL STORM" );
				pearlsLeft = PearlCount + (Ascended ? 3 : 0);
				GrowPearls( pearlsLeft );
				Sfx.Play( "queen_charge", GlobalPosition, 1f, 1.5f );
				break;
			case Act.Riptide: Say( "RIPTIDE" ); Sfx.Play( "whirlpool", GlobalPosition, 1.1f, 0.7f ); break;
			case Act.Guard: Say( "ROYAL GUARD" ); Sfx.Play( "queen_song", GlobalPosition, 1f, 1.5f ); break;
			case Act.Gaze:
				Say( "ABYSSAL GAZE" );
				Gazes++;
				burned.Clear();
				gazeAt = (Nearest?.RealPosition ?? GlobalPosition + Forward * 10f);
				Sfx.Play( "beam_charge", GlobalPosition, 1.2f, 0.7f );
				break;
			case Act.Choke:
				Say( "SHE'S CHOKING. HIT HER" );
				Chokes++;
				Sfx.Play( "queen_scream", GlobalPosition, 1.2f, 1.4f );
				break;
			case Act.Ascend:
				Say( "THE QUEEN ASCENDS" );
				Ascents++;
				Sfx.Play( "queen_scream", GlobalPosition, 1.6f, 0.9f );
				break;
			case Act.Bite: Bites++; Sfx.Play( "creature_attack", GlobalPosition, 1.2f, 0.6f ); break;
			case Act.Dash: Dashes++; Sfx.Play( "whirlpool", GlobalPosition, 0.9f, 1.4f ); break;
		}
	}

	/// <summary> Circles the nearest player at a distance, at her hover height, facing them, rising over walls and roofs. </summary>
	void Swim( PlayerSurvivor prey, float dt )
	{
		var wish = Vector3.Zero;
		if ( prey is not null )
		{
			orbit += orbitSign * dt * 0.22f;
			var spot = Sculpt.InBounds( prey.RealPosition + new Vector3( MathF.Cos( orbit ), 0f, MathF.Sin( orbit ) ) * KeepAway );
			var to = (spot - GlobalPosition).Flat();
			wish = to.Length() > 0.6f ? to.Normalized() * MathF.Min( SwimSpeed, to.Length() * 1.2f ) : Vector3.Zero;
			Face( prey.RealPosition - GlobalPosition, 2.5f, dt );
		}
		velocity = velocity.Lerp( wish, 1f - MathF.Exp( -1.6f * dt ) );
		Move( dt );
	}

	/// <summary> Holds her place (easing to a stop), turning to face the player. </summary>
	void Hold( PlayerSurvivor prey, float dt, float turn )
	{
		velocity = velocity.Lerp( Vector3.Zero, 1f - MathF.Exp( -3f * dt ) );
		Move( dt );
		if ( prey is not null && turn > 0f ) Face( prey.RealPosition - GlobalPosition, turn, dt );
	}

	/// <summary> At hover height over whatever is below (streets, roofs, the canal floors), round walls, inside the globe. </summary>
	void Move( float dt )
	{
		var next = GlobalPosition + velocity * dt;
		var ahead = velocity.Flat().SafeNormal();
		if ( ahead != Vector3.Zero )
			foreach ( var drop in new[] { 0f, 2.2f } )
			{
				var from = GlobalPosition + Vector3.Down * drop;
				var hit = Trace.Ray( this, from, from + ahead * 3f, Layers.World );
				if ( !hit.Hit ) continue;
				velocity = velocity.Slide( hit.Normal.Flat().SafeNormal() ) + Vector3.Up * 3f;
				next = GlobalPosition + velocity * dt;
				break;
			}
		var want = floorY + HoverHeight + MathF.Sin( (float)Clock.Now * 1.3f ) * 0.15f;
		next.Y = MathX.Lerp( next.Y, want, 1f - MathF.Exp( -2f * dt ) );
		next.Y = MathF.Max( next.Y, floorY + HoverHeight * 0.65f );
		GlobalPosition = Sculpt.InBounds( next );
	}

	// ---- Tide Dash ----

	void StartDash( PlayerSurvivor prey )
	{
		nextDash = Rng.Float( 13f, 19f );
		var best = GlobalPosition;
		for ( var i = 0; i < 10; i++ )
		{
			var a = Rng.Float( 0f, MathF.Tau );
			var spot = Sculpt.InBounds( prey.RealPosition + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * Rng.Float( 10f, 15f ) );
			var floor = Sculpt.FloorBelow( this, spot + Vector3.Up * 30f, 80f );
			var to = spot with { Y = floor + HoverHeight };
			// A clear line (no building in the way at her height).
			if ( Trace.Ray( this, GlobalPosition, to, Layers.World ).Hit ) continue;
			best = to;
			break;
		}
		dashFrom = GlobalPosition;
		dashTo = best;
		Begin( Act.Dash );
	}

	void Dash( float a, float dt )
	{
		var length = MathF.Max( 0.4f, dashFrom.DistanceTo( dashTo ) / DashSpeed );
		var k = SmoothKit.Ease( a / length );
		var was = GlobalPosition;
		GlobalPosition = Sculpt.InBounds( dashFrom.Lerp( dashTo, k ) + Vector3.Up * MathF.Sin( k * MathF.PI ) * 1.5f );
		WaterBubble.Trail( was, GlobalPosition );
		if ( Rng.Float() < 0.6f ) WaterBubble.Create( GlobalPosition + Rng.InSphere( 2f ), Rng.Float( 0.3f, 0.8f ), 0f, Rng.Float( 0.5f, 1f ) );
		var dir = (dashTo - dashFrom).Flat();
		if ( dir.LengthSquared() > 0.01f ) Face( dir, 6f, dt );
		velocity = (GlobalPosition - was) / MathF.Max( dt, 1e-4f );
		if ( a >= length ) { velocity *= 0.2f; Begin( Act.Swim ); nextAttack = MathF.Min( (float)nextAttack, 1.2f ); }
	}

	// ---- Siren Song ----

	/// <summary> Can she see this player (nothing solid between her head and their eyes)? </summary>
	bool Sees( PlayerSurvivor p )
	{
		var eye = p.RealPosition + Vector3.Up * 1.5f;
		var from = HeadAt;
		if ( from.DistanceTo( eye ) > SongRange ) return false;
		return !Trace.Ray( this, from, eye, Layers.World ).Hit;
	}

	void Song( float a, float dt )
	{
		if ( a < SongWindUp ) return;
		var length = SongTime + (Ascended ? 1f : 0f);
		if ( !struck ) { struck = true; Sfx.Play( "queen_song", HeadAt, 1.6f, 1f ); }
		var pull = SongPull * (Ascended ? 1.35f : 1f);
		var lap = Lap;
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c || c.Scripted || !Sees( p ) ) continue;
			var to = (lap - p.RealPosition).Flat();
			var d = to.Length();
			if ( d < BiteRange ) { bitten = p; Begin( Act.Bite ); return; }
			// Drawn in, harder the nearer they are (a pull they can fight by running, but not for long).
			c.Velocity += to / MathF.Max( d, 0.01f ) * pull * (0.6f + 0.6f * (1f - d / SongRange)) * dt * 6f;
		}
		if ( a >= SongWindUp + length ) Done();
	}

	void Bite( float a, float dt )
	{
		// A lunge down onto the player at her lap, the bite, and back up.
		var p = bitten;
		var lap = Lap;
		var dip = MathF.Sin( MathX.Clamp( a / 0.7f, 0f, 1f ) * MathF.PI );
		var want = floorY + HoverHeight - dip * (HoverHeight - 2.6f);
		GlobalPosition = GlobalPosition with { Y = MathX.Lerp( GlobalPosition.Y, want, 1f - MathF.Exp( -12f * dt ) ) };
		if ( p is not null && IsInstanceValid( p ) ) Face( p.RealPosition - GlobalPosition, 8f, dt );
		if ( !struck && a >= 0.3f )
		{
			struck = true;
			if ( p is not null && IsInstanceValid( p ) && p.IsAlive && p.Controller is { } c && (p.RealPosition - lap).Flat().Length() < BiteRange + 1.5f )
			{
				p.Health?.OnDamage( new DamageInfo { Damage = BiteDamage, Attacker = this, Position = p.RealPosition + Vector3.Up, Origin = HeadAt } );
				c.Velocity = (p.RealPosition - GlobalPosition).Flat().SafeNormal() * 11f + Vector3.Up * 5f;
				p.Juice?.Shake( 1f );
				Sfx.Play( "hit_flesh", p.RealPosition, 1.2f, 0.6f );
				gore?.GooBurst( p.RealPosition + Vector3.Up, Vector3.Up, new Color( 0.08f, 0.08f, 0.1f ), 10, 200f, 0.8f );
			}
		}
		if ( a >= 0.9f ) { bitten = null; Done( 0.5f ); }
	}

	// ---- Pearl Storm ----

	void GrowPearls( int count )
	{
		ClearHeld();
		for ( var i = 0; i < count; i++ )
		{
			var pearl = QueenPearl.Grow( this, OrbAt, i, count );
			if ( pearl is not null ) held.Add( pearl );
		}
	}

	void ClearHeld()
	{
		foreach ( var p in held ) if ( IsInstanceValid( p ) && p.Held ) p.QueueFree();
		held.Clear();
	}

	/// <summary> The pearls gather for 1.2 s, then go one every 0.22 s, each at the nearest player to it. </summary>
	void Pearls( float a )
	{
		if ( a < 1.2f || held.Count == 0 )
		{
			if ( a >= 1.2f && held.Count == 0 ) Done();
			return;
		}
		if ( sinceThrow < (Ascended ? 0.16f : 0.22f) ) return;
		sinceThrow = 0f;
		var pearl = held[0];
		held.RemoveAt( 0 );
		if ( !IsInstanceValid( pearl ) ) return;
		var prey = PlayerSurvivor.FindNearestAlive( pearl.GlobalPosition );
		var aim = (prey?.RealPosition ?? GlobalPosition + Forward * 10f) + Vector3.Up * 1.1f;
		// A spread: each leaves at its own angle off the line and curves in.
		var dir = (aim - pearl.GlobalPosition).SafeNormal();
		var side = Vector3.Up.Cross( dir ).SafeNormal();
		var spread = (pearlsLeft % 2 == 0 ? 1f : -1f) * Rng.Float( 0.25f, 0.6f );
		var velocity = (dir + side * spread + Vector3.Up * 0.25f).SafeNormal() * PearlSpeed;
		pearl.Launch( velocity, PearlDamage, PearlRadius );
		pearlsLeft--;
		PearlsThrown++;
		pearls++; pearlFrom = pearl.GlobalPosition; pearlVelocity = velocity;
		Sfx.Play( "queen_pearl", pearl.GlobalPosition, 0.9f, Rng.Float( 0.9f, 1.2f ) );
		if ( held.Count == 0 ) { Done(); }
	}

	// ---- Riptide ----

	void SendWaves( PlayerSurvivor prey )
	{
		Riptides++;
		var from = GlobalPosition with { Y = floorY };
		var toward = prey is not null ? (prey.RealPosition - from).Flat().SafeNormal() : Forward;
		if ( toward == Vector3.Zero ) toward = Forward;
		waves++; waveFrom = from; waveDir = toward;
		Waves( from, toward, WaveDamage );
		Sfx.Play( "queen_wave", from, 1.5f, 0.9f );
		foreach ( var pl in PlayerSurvivor.All ) if ( pl.Controller is { } c && c.GlobalPosition.DistanceTo( from ) < 30f ) pl.Juice?.Shake( 0.5f );
	}

	/// <summary> The fan of crescents (every copy draws the same; only the host's do harm). </summary>
	void Waves( Vector3 from, Vector3 toward, float damage )
	{
		var count = WaveCount + (Ascended ? 2 : 0);
		var baseAngle = MathF.Atan2( toward.Z, toward.X );
		for ( var i = 0; i < count; i++ )
		{
			var off = (i - (count - 1) * 0.5f) * WaveSpread;
			QueenWave.Send( GetTree().CurrentScene, from, baseAngle + off, WaveHalfArc, WaveSpeed * (Ascended ? 1.15f : 1f), WaveReach, damage, this, Math.Abs( i - (count - 1) * 0.5f ) * 0.12f );
		}
		for ( var i = 0; i < 30; i++ )
		{
			var dir = (Rng.OnSphere().Flat() + Vector3.Up * 0.4f).SafeNormal();
			GpuFx.SandPuff( from + Vector3.Up * 0.2f, dir * Rng.Float( 2f, 5f ), SeaStir.SiltColour( from ), Rng.Float( 8f, 14f ), Rng.Float( 0.8f, 1.4f ) );
		}
	}

	// ---- Royal Guard ----

	void CallGuard()
	{
		var d = WaveDirector.Current;
		var count = GuardCount + (Ascended ? 1 : 0);
		var room = MaxGuards - Mermaid.All.Count( m => !m.IsDead && m.Guard == this );
		count = Math.Min( count, room );
		var near = Nearest?.RealPosition ?? GlobalPosition + Forward * 10f;
		for ( var i = 0; i < count; i++ )
		{
			var a = MathF.Atan2( Forward.Z, Forward.X ) + (i - (count - 1) * 0.5f) * 1.1f;
			var at = Sculpt.InBounds( GlobalPosition + new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * 5f );
			var ground = Sculpt.FloorBelow( this, at + Vector3.Up * 20f, 60f );
			at = at with { Y = ground };
			PoseidonVortex.Burst( this, at, 0.7f );
			Mermaid m;
			if ( d is not null ) m = d.SpawnAt( WaveDirector.EnemyKind.Mermaid, at, near ) as Mermaid;
			else m = Mermaid.Spawn( GetTree().CurrentScene, at + Vector3.Up * 1.3f, GuardHealth );
			if ( m is null ) continue;
			m.Male = true;
			m.Archer = i == 2;
			m.Guard = this;
			Guards++;
		}
	}

	// ---- Abyssal Gaze ----

	/// <summary> The middle of her four eyes, in the world. </summary>
	public Vector3 EyesAt => head is not null ? head.GlobalTransform * new Vector3( 0f, 0.34f, -0.3f ) : GlobalPosition;
	public Vector3 HeadAt => head is not null ? head.GlobalTransform * new Vector3( 0f, 0.25f, 0f ) : GlobalPosition + Vector3.Up * 2f;
	/// <summary> Where the gaze lands now (on the ground or the first wall), and whether the beam is burning. </summary>
	public Vector3 GazeAt => gazeAt;
	public bool Burning => act == Act.Gaze && (float)sinceAct >= GazeCharge && (float)sinceAct < GazeCharge + GazeTime;

	void Gaze( PlayerSurvivor prey, float a, float dt )
	{
		var feet = prey?.RealPosition ?? gazeAt;
		if ( a < GazeCharge )
		{
			// Charging: the mark follows the player exactly.
			gazeAt = feet;
			return;
		}
		if ( !struck ) { struck = true; Sfx.Play( "beam_fire", EyesAt, 1.4f, 0.6f ); }
		// Burning: it drags along the ground after them.
		var to = feet - gazeAt;
		var step = BeamTrack * (Ascended ? 1.35f : 1f) * dt;
		gazeAt += to.Length() > step ? to.Normalized() * step : to;
		var (from, end) = BeamLine();
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
		if ( a >= GazeCharge + GazeTime ) Done();
	}

	/// <summary> The beam from her eyes toward the gaze point, stopped by the first wall or floor. </summary>
	(Vector3 From, Vector3 To) BeamLine()
	{
		var from = EyesAt;
		var dir = (gazeAt + Vector3.Up * 0.3f - from).SafeNormal();
		var far = from + dir * 70f;
		var hit = Trace.Ray( this, from, far, Layers.World );
		return (from, hit.Hit ? hit.HitPosition : far);
	}

	// ---- Ascend ----

	void Ascend( float a )
	{
		if ( a < 0.05f ) ClearHeld();
		// Rising and screaming for 2.4 s; the change comes at 1.2 s with a ring of water.
		var lift = MathF.Sin( MathX.Clamp( a / 2.4f, 0f, 1f ) * MathF.PI ) * 2.5f;
		GlobalPosition = GlobalPosition with { Y = MathX.Lerp( GlobalPosition.Y, floorY + HoverHeight + lift, 0.08f ) };
		if ( !struck && a >= 1.2f )
		{
			struck = true;
			Ascended = true;
			var at = GlobalPosition with { Y = floorY };
			QueenWave.Send( GetTree().CurrentScene, at, 0f, MathF.PI, WaveSpeed, WaveReach * 0.8f, Puppet ? 0f : WaveDamage * 0.7f, this, 0f );
			AlienBlast.Spawn( GetTree().CurrentScene, HeadAt, 1.6f );
			foreach ( var p in PlayerSurvivor.All ) if ( p.Controller is { } c && c.GlobalPosition.DistanceTo( at ) < 40f ) p.Juice?.Shake( 0.9f );
		}
		if ( a >= 2.4f ) Done( -1f );
	}

	// ---- Every copy: her song's rings, the local player's charm, the beam ----

	void Effects( float dt )
	{
		var a = (float)sinceAct;
		var singing = act == Act.Song && a >= SongWindUp;
		if ( singing && sinceRing > 0.42f )
		{
			sinceRing = 0f;
			SirenRing.Send( GetTree().CurrentScene, HeadAt + Forward * 0.6f, Forward, Ascended );
		}
		// The local player under her song: drawn to look at her, the screen edged pink (the pull itself is the host's).
		if ( singing && PlayerSurvivor.Local is { IsAlive: true } me && me.Controller is { } mc && !mc.IsRemote && Sees( me ) )
		{
			charmedUntil = Clock.Now + 0.25;
			var eye = mc.GlobalPosition + Vector3.Up * 1.6f;
			var d = HeadAt - eye;
			var yaw = MathX.Deg( MathF.Atan2( -d.X, -d.Z ) );
			var pitch = -MathX.Deg( MathF.Atan2( d.Y, d.Flat().Length() ) );
			var e = mc.EyeAngles;
			var turn = (Ascended ? 55f : 38f) * dt;
			e.Yaw += MathX.Clamp( Mathf.Wrap( yaw - e.Yaw, -180f, 180f ), -turn, turn );
			e.Pitch += MathX.Clamp( pitch - e.Pitch, -turn * 0.6f, turn * 0.6f );
			mc.EyeAngles = e;
		}
		DrawBeam( dt );
		// The held pearls ride round her sceptre.
		for ( var i = 0; i < held.Count; i++ ) if ( IsInstanceValid( held[i] ) ) held[i].Orbit( OrbAt, i, held.Count );
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
			0 => HeadMultiplier,
			1 => HeartMultiplier,
			ThroatIndex => ThroatOpen ? ThroatMultiplier : 1f,
			>= 4 and <= 6 => TailMultiplier,
			_ => 1f,
		};
		if ( act == Act.Choke ) scale *= ChokeMultiplier;
		var dealt = damage * scale;
		var wasAlive = !IsDead;
		health.OnDamage( new DamageInfo { Damage = dealt, Position = position, Origin = origin, Attacker = shooter } );
		gore?.GooBurst( position, -direction, GooColor, 2, 160f, 0.6f );
		if ( index is 1 or ThroatIndex ) heartFlash = 1f;
		// The song choked off: enough shot into her throat or heart while she sings.
		if ( !Puppet && act == Act.Song && (float)sinceAct >= SongWindUp * 0.5f && index is 1 or ThroatIndex )
		{
			chokeTaken += dealt;
			if ( chokeTaken >= ChokeShare * health.MaxHealth && !IsDead ) Begin( Act.Choke );
		}
		var killed = wasAlive && IsDead;
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, WeakSpot( index ), scoreHit );
	}

	protected override void OnDied()
	{
		StopBeam();
		ClearHeld();
		act = Act.Dead;
		sinceAct = 0f;
		RaiseKilled();
		Say( "" );
		Sfx.Play( "queen_scream", GlobalPosition, 1.8f, 0.7f );
		gore?.GooBurst( GlobalPosition, Vector3.Up, GooColor, 20, 380f, 1.6f );
		foreach ( var part in FindChildren( "*", "StaticBody3D", true, false ).OfType<CreaturePart>().ToList() ) part.QueueFree();
	}

	public const float SwellSeconds = 1.3f;

	/// <summary> Dead: she arches back, swells and shakes, spurting, then bursts. </summary>
	void Swell( float dt )
	{
		if ( burst ) return;
		var t = MathX.Clamp( (float)timeSinceDeath / SwellSeconds, 0f, 1f );
		shown = 1f + t * t * 0.3f + MathF.Sin( (float)timeSinceDeath * 40f ) * 0.03f * t;
		body.Position = Rng.InSphere( 0.15f * t );
		RotateY( dt * (0.4f + t * 2.5f) );
		if ( Rng.Float() < 0.25f + t * 0.5f )
		{
			var from = chest.GlobalPosition + Rng.InSphere( 1.2f );
			gore?.GooSpray( from, (from - chest.GlobalPosition).SafeNormal() + Vector3.Up * 0.3f, 4, Rng.Float() < 0.5f ? GooColor : Flesh[Rng.Int( 0, 2 )], 260f );
		}
		if ( t >= 1f ) Burst();
	}

	/// <summary> She bursts: crown, sceptre, head, arms, chest and the tail in sections fly apart, pearls scatter, goo everywhere. </summary>
	void Burst()
	{
		burst = true;
		var centre = chest.GlobalPosition;
		var away = lastShotDirection.Flat().SafeNormal();
		void Loose( Node3D piece, float radiusMetres, float mass, float speed )
		{
			if ( piece is null || !IsInstanceValid( piece ) ) return;
			var dir = (piece.GlobalPosition - centre).SafeNormal();
			if ( dir == Vector3.Zero ) dir = Vector3.Up;
			var v = (dir + away * 0.3f + Vector3.Up * 0.5f + Rng.InSphere( 0.35f )).SafeNormal() * speed * Rng.Float( 0.7f, 1.2f );
			var gib = Gib.Attach( piece, gore, radiusMetres / U.In, 80f, mass, v, GooColor );
			gib.AngularVelocity = Rng.InSphere( 6f );
			CreatureGore.AddStump( piece, piece.GlobalPosition, -dir, radiusMetres * 0.5f / U.In, Flesh[Rng.Int( 0, 2 )], GooColor, 8f, Gibs );
		}
		// The skinned tail gives way to plain pieces, so its sections can fly apart.
		for ( var i = tail.Count - 2; i >= 1; i -= 3 ) TailPieceFrom( i, Math.Min( 3, tail.Count - i ) );
		if ( tailMesh is not null && IsInstanceValid( tailMesh ) ) tailMesh.Visible = false;
		Loose( crown, 0.5f, 3f, 12f );
		Loose( sceptre, 0.4f, 4f, 9f );
		for ( var i = tail.Count - 2; i >= 1; i -= 3 ) Loose( tail[i], 0.4f * MathX.Lerp( 1f, 0.5f, i / (float)tail.Count ), 8f, 8f );
		Loose( head, 0.45f, 8f, 11f );
		Loose( foreR, 0.25f, 4f, 12f );
		Loose( armR, 0.3f, 5f, 11f );
		Loose( foreL, 0.25f, 4f, 12f );
		Loose( armL, 0.3f, 5f, 11f );
		Loose( chest, 0.6f, 14f, 6f );
		for ( var i = 0; i < 4; i++ ) GibKit.Burst( Gibs, centre + Rng.InSphere( 1.2f ), Rng.InSphere( 1f ) + Vector3.Up * 0.6f, 0.4f, 8 );
		for ( var i = 0; i < 5; i++ ) CreatureGore.Overkill( centre + Rng.InSphere( 1.2f ), Rng.InSphere( 1f ) + Vector3.Up * 0.4f, Flesh[i % 3], GooColor, 2.2f, Gibs );
		gore?.GooBurst( centre, Vector3.Up, GooColor, 70, 620f, 2.6f );
		gore?.GooBurst( centre, away + Vector3.Up * 0.3f, Flesh[1], 30, 480f, 2f );
		for ( var i = 0; i < 16; i++ )
		{
			var a = i / 16f * MathF.Tau;
			gore?.GooSpray( centre, new Vector3( MathF.Cos( a ), 0.35f, MathF.Sin( a ) ), 6, i % 2 == 0 ? GooColor : Flesh[i % 3], 520f, 0.8f, 2f );
		}
		// Her pearls scatter, glowing, and float up.
		for ( var i = 0; i < 12; i++ )
		{
			var pearl = new Node3D { Name = "Queen's Pearl" };
			GetTree().CurrentScene.AddChild( pearl );
			pearl.GlobalPosition = centre + Rng.InSphere( 1f );
			SmoothKit.Ball( pearl, Vector3.Zero, Vector3.One * 0.22f, PearlPaint, null, "Pearl" );
			var gib = Gib.Attach( pearl, gore, 0.12f / U.In, 20f, 0.3f, (Rng.OnSphere() + Vector3.Up * 0.6f).SafeNormal() * Rng.Float( 4f, 9f ), GooColor );
			gib.AngularVelocity = Rng.InSphere( 4f );
		}
		for ( var i = 0; i < 60; i++ ) WaterBubble.Create( centre + Rng.InSphere( 3f ), Rng.Float( 0.4f, 1.4f ), 0f, Rng.Float( 0.6f, 1.6f ) );
		AlienBlast.Spawn( GetTree().CurrentScene, centre, 2.2f );
		PoseidonVortex.Burst( this, GlobalPosition + Vector3.Down * HoverHeight * 0.9f, 1.8f );
		gore?.Squelch( centre, 1.6f, 0.7f );
		Sfx.Play( "gib", centre, 1f, 0.7f );
		Sfx.Play2D( "boss_spawn", 0.9f, 0.5f );
		foreach ( var player in PlayerSurvivor.All )
			if ( player.Controller is { } pc && pc.GlobalPosition.DistanceTo( centre ) < 45f ) player.Juice?.Shake( 1.3f );
		if ( Engine.TimeScale == 1.0 && !AutoTest.Requested )
		{
			Engine.TimeScale = 0.3;
			GetTree().CreateTimer( 0.45, true, false, true ).Timeout += () => { if ( Engine.TimeScale == 0.3 ) Engine.TimeScale = 1.0; };
		}
		if ( AutoTest.Requested ) GD.Print( "[autotest] queen burst into pieces" );
		QueueFree();
	}
}
