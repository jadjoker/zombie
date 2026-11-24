using Godot;

/// <summary>
/// King Raymond IV (user's design): the wave 15 boss, a sting ray <see cref="Size"/> times as big with a jewelled gold crown
/// like Poseidon's. He has the sting ray's two attacks made bigger (the zap stuns for a second, the nose dive hits harder
/// and its landing throws a shockwave) and three of his own: the carpet bomb (he climbs high, a line of blast marks on the
/// sand follows the player and locks, then he flies the line dropping bombs), the sand ambush (he sinks into the sand with
/// only his crown and eyes showing, a ripple runs at the player, the sand glows where he will burst up, then he bursts out
/// and launches them with a zap) and the shock rings (he rises charging, warning rings glow on the sand, he slams down and
/// three rings of electricity run out across the ground; jump them).
/// </summary>
public partial class KingRaymond : Stingray
{
	public static new readonly List<KingRaymond> All = new();
	public const float KingSize = 3f;

	public static new KingRaymond Spawn( Node parent, Vector3 position, float maxHealth ) =>
		Place( new KingRaymond { Name = "King Raymond IV" }, parent, position, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	protected override void Setup()
	{
		King = true;
		Size = KingSize;
		Speed = 4.4f;
		CruiseHeight = 0.34f * KingSize * 0.9f;
		ZapRange = 6f;
		ZapReach = 7.5f;
		ZapDamage = 30f;
		ZapCooldown = 4f;
		ZapBolts = 6f;
		StunSeconds = 1f;   // user's number
		DiveMin = 8f;
		DiveMax = 22f;
		DiveHeight = 12f;
		DiveSpeed = 18f;
		DiveDamage = 50f;
		DiveCooldown = 10f;
		DiveHitRadius = 3f;
		DiveShove = 10f;
		ClimbSeconds = 3f;
		EyeMultiplier = 2f;
		base.Setup();
	}

	protected override void Cleanup() => KingCleanup();
}

public partial class Stingray
{
	// ---- The king's numbers ----

	/// <summary> The least time between his big attacks (the zap can come in between). </summary>
	public float KingGap = 2.2f;
	// The carpet bomb.
	public float BombHeight = 9f;           // over the player's floor
	public int BombCount = 9;
	public float BombSpacing = 2.4f;
	public const float BombMarkTime = 2.2f, BombLockBefore = 1f;
	public float BombRunSpeed = 11f;
	public float BombDamage = 26f;
	public float BombRadius = 2.6f;
	public float BombCooldown = 13f;
	// The sand ambush.
	public const float BurrowTime = 0.9f, BuriedMax = 5.5f, EruptCue = 0.85f, EruptRise = 0.35f;
	public float BuriedSpeed = 6.5f;
	public float EruptDamage = 36f;
	public float EruptRadius = 3.2f;
	public float EruptLaunch = 10f;
	public float BurrowCooldown = 14f;
	// The shock rings.
	public const float ShockRiseTime = 1.1f, ShockLockBefore = 0.4f, ShockDrop = 0.2f, ShockSlamTime = 1.9f;
	public const int RingCount = 3;
	public const float RingGap = 0.55f;
	public float RingSpeed = 8.5f;
	public float RingReach = 18f;
	public float RingDamage = 22f;
	public float RingStun = 0.4f;
	public float ShockCooldown = 11f;
	// The dive's landing.
	public float DiveShockRadius = 5f;
	public float DiveShockDamage = 18f;

	/// <summary> Tests: how often each of the king's own attacks landed. </summary>
	public static int BombsDropped, BombHits, Eruptions, EruptHits, RingsSent, RingHits, RingJumps, DiveShockHits, DiveShocks;

	/// <summary> The boss's move name for the HUD (like Davey's). </summary>
	public string Line { get; private set; } = "";
	public TimeSince TimeSinceLine { get; private set; } = 999f;
	void Say( string line ) { Line = line; TimeSinceLine = 0f; }

	TimeSince sinceSpecial = 99f, sinceBomb = 99f, sinceBurrow = 99f, sinceShock = 99f, sinceKingFx = 99f, sinceOpenCheck = 99f;
	// The bomb line (from its first mark, along the ground) and whether it has locked.
	Vector3 lineFrom, lineDir = Vector3.Forward;
	bool lineLocked;
	// Where he bursts up, and where he last was under open sand (a spot he can come up at).
	Vector3 eruptAt, lastOpen;
	// Where the shock rings start.
	Vector3 ringCentre;
	// What this copy has done of the current act (host and joiners alike): bombs dropped, rings sent, the burst shown.
	Act kingSeen = Act.Prowl;
	int bombsOut, ringsOut;
	bool burstShown, slamShown;
	readonly Vector3[] bombAt = new Vector3[16];
	readonly bool[] bombLanded = new bool[16];
	readonly List<Node> spawned = new();
	MeshInstance3D cue;
	ImmediateMesh cueMesh;
	StandardMaterial3D cuePaint;
	Node3D crown;
	readonly List<Node3D> spiracles = new();

	public bool Underground => act is Act.Burrow or Act.Buried || (act == Act.Erupt && sinceAct < EruptCue);
	public Vector3 EruptSpot => eruptAt;
	public bool LineLocked => lineLocked;
	public int BombsOut => bombsOut;
	/// <summary> Tests: a warning is drawn on the sand; the disc (wings) is showing. </summary>
	public bool CueShowing => cue is not null && IsInstanceValid( cue ) && cue.Visible;
	public bool DiscShowing => disc is not null && disc.Visible;
	public bool HasCrown => crown is not null && IsInstanceValid( crown ) && crown.GetParent() == body;

	float BuryDepth => 0.1f * ModelScale;

	void KingSetup()
	{
		sinceSpecial = 0f;
		sinceDive = DiveCooldown * 0.6f;
		sinceBomb = BombCooldown * 0.5f;
		sinceBurrow = BurrowCooldown * 0.4f;
		sinceShock = ShockCooldown * 0.6f;
		lastOpen = GlobalPosition;
		Sfx.Play2D( "boss_spawn", 0.9f, 0.7f );
		// His greeting once the wave's banner has gone.
		GetTree().CreateTimer( 2.6 ).Timeout += () => { if ( IsInstanceValid( this ) && !IsDead && string.IsNullOrEmpty( Line ) ) Say( "ALL HAIL KING RAYMOND IV!" ); };
	}

	void KingCalm() { sinceSpecial = 0f; sinceBomb = 0f; sinceBurrow = 0f; sinceShock = 0f; }

	// ---- Choosing an attack ----

	/// <summary> Gliding about: the zap when he is close, otherwise one of his big attacks that fits, picked at random. </summary>
	void KingChoose( PlayerSurvivor prey, float dist, bool free )
	{
		var pos = GlobalPosition;
		var feet = prey.WorldPosition;
		var chest = feet + Vector3.Up * 1.1f;
		if ( free && dist < ZapRange && sinceZap > ZapCooldown && MathF.Abs( chest.Y - pos.Y ) < 2.6f + Size ) { Begin( Act.Zap ); return; }
		if ( sinceSpecial < KingGap ) return;
		var options = new List<Act>( 4 );
		if ( dist > DiveMin && dist < DiveMax && sinceDive > DiveCooldown && SkyClear( pos, DiveHeight + 1f ) ) options.Add( Act.Climb );
		if ( dist < RingReach * 0.65f && sinceShock > ShockCooldown && SkyClear( pos, 4f ) ) options.Add( Act.ShockRise );
		if ( dist > 6f && dist < 26f && sinceBomb > BombCooldown && SkyClear( pos, BombHeight + 2f ) && SkyClear( feet, BombHeight + 2f ) ) options.Add( Act.BombClimb );
		if ( dist > 7f && sinceBurrow > BurrowCooldown && OpenSand( pos ) && OpenSand( feet ) && OnGround( prey ) ) options.Add( Act.Burrow );
		if ( options.Count == 0 ) return;
		var pick = options[Rng.Int( 0, options.Count - 1 )];
		sinceSpecial = 0f;
		Begin( pick );
	}

	/// <summary> Sea bed sand here (not Atlantis's paving, not the old flat floor) with nothing standing on it and open water above. </summary>
	bool OpenSand( Vector3 at )
	{
		if ( !SeaTerrain.Active || (SeaTerrain.Area == 3 && Atlantis.Inside( at, 0f )) ) return false;
		var sand = Sculpt.GroundAt( at );
		if ( sand == float.MinValue ) return false;
		var top = Sculpt.FloorBelow( this, at with { Y = sand + 4f * Size }, 5f * Size );
		return top < sand + 0.35f;
	}

	/// <summary> The player is standing (or nearly) on the sand. </summary>
	static bool OnGround( PlayerSurvivor p )
	{
		var feet = p.RealPosition;
		var sand = Sculpt.GroundAt( feet );
		return sand != float.MinValue && feet.Y - sand < 0.8f;
	}

	/// <summary> The ground under a point for his cues and rings: the sand where there is some, else the first floor below. </summary>
	float CueGround( Vector3 at )
	{
		var g = Sculpt.GroundAt( at );
		return g != float.MinValue ? g : Sculpt.FloorBelow( this, at, 30f );
	}

	void KingBegin( Act next )
	{
		switch ( next )
		{
			case Act.BombClimb: sinceBomb = 0f; lineLocked = false; break;
			case Act.Burrow: sinceBurrow = 0f; lastOpen = GlobalPosition; break;
			case Act.ShockRise: sinceShock = 0f; ringCentre = GlobalPosition; break;
			case Act.Erupt: break;
		}
	}

	// ---- His attacks (the host) ----

	/// <summary> The king's own acts. True when it placed itself this frame (no <see cref="Move"/>). </summary>
	bool KingThink( PlayerSurvivor prey, Vector3 feet, Vector3 chest, float dist, Vector3 dir, float a, float dt, ref Vector3 wish, ref float wantY )
	{
		var pos = GlobalPosition;
		switch ( act )
		{
			case Act.BombClimb:
			{
				// Up high, keeping some way off.
				Face( dir, 2f, dt );
				var floor = MathF.Max( Ground( feet, false ), groundMax );
				wantY = floor + BombHeight;
				wish = dist < 7f ? -dir * 3f : dist > 12f ? dir * 3f : Vector3.Zero;
				var roof = Trace.Ray( this, pos, pos + Vector3.Up * 1.2f * Size, Layers.World ).Hit;
				if ( (pos.Y > wantY - 0.6f && a > 0.8f) || a > 3f || roof ) { Begin( Act.BombMark ); AimLine( feet, pos ); }
				return false;
			}
			case Act.BombMark:
			{
				// The line follows the player, then locks; he lines up behind its start.
				if ( a < BombMarkTime - BombLockBefore ) AimLine( feet, pos );
				else lineLocked = true;
				var start = lineFrom - lineDir * 4f;
				var to = (start - pos).Flat();
				wish = to.Length() > 0.5f ? to.SafeNormal() * MathF.Min( Speed * 2f, to.Length() * 2f ) : Vector3.Zero;
				Face( lineDir, 3f, dt );
				wantY = MathF.Max( pos.Y, groundMax + Clear + 2f * Size );
				if ( a > BombMarkTime ) { lineLocked = true; Begin( Act.BombRun ); }
				return false;
			}
			case Act.BombRun:
			{
				// Along the line at bombing height; the bombs drop in KingFrame as he passes each mark.
				Face( lineDir, 6f, dt );
				var step = lineDir * BombRunSpeed * dt;
				var from = pos + Vector3.Up * 0.1f * Size;
				var wall = Trace.Sphere( this, 0.3f * Size, from, from + lineDir * 1.2f * Size, Layers.World );
				var along = (pos - lineFrom).Flat().Dot( lineDir );
				if ( wall.Hit || along > (BombCount - 1) * BombSpacing + 3f || a > 6f )
				{
					DropRest();
					velocity = lineDir * BombRunSpeed * 0.5f;
					Begin( Act.Recover );
					return false;
				}
				velocity = lineDir * BombRunSpeed;
				var next = pos + step;
				next.Y = MathF.Max( pos.Y, groundMax + Clear + 2f * Size );
				GlobalPosition = Sculpt.InBounds( next );
				return true;
			}
			case Act.Burrow:
			{
				// Down into the sand where he is, wriggling.
				var sand = Sculpt.GroundAt( pos );
				var k = MathX.Clamp( a / BurrowTime, 0f, 1f );
				var y = MathX.Lerp( pos.Y, sand - BuryDepth, 1f - MathF.Exp( -8f * dt ) );
				if ( k >= 1f ) y = sand - BuryDepth;
				velocity = velocity.Lerp( Vector3.Zero, 1f - MathF.Exp( -4f * dt ) );
				GlobalPosition = Sculpt.InBounds( new Vector3( pos.X, y, pos.Z ) + velocity * dt );
				ySpeed = 0f;
				if ( k >= 1f ) Begin( Act.Buried );
				return true;
			}
			case Act.Buried:
			{
				// Under the sand towards the player, only the crown and eyes showing.
				var to = (feet - pos).Flat();
				var way = to.SafeNormal();
				Face( way.IsNearlyZero() ? dir : way, 4f, dt );
				velocity = way * MathF.Min( BuriedSpeed, to.Length() * 3f );
				var next = Sculpt.InBounds( pos + velocity * dt );
				next.Y = Sculpt.GroundAt( next ) - BuryDepth;
				GlobalPosition = next;
				if ( sinceOpenCheck > 0.2f ) { sinceOpenCheck = 0f; if ( OpenSand( next ) ) lastOpen = next; }
				if ( (to.Length() < 1.2f && OnGround( prey )) || a > BuriedMax )
				{
					// The sand glows where he will come up: under the player if he got to them, else where he is.
					var spot = to.Length() < 3f && OpenSand( feet ) ? feet : OpenSand( next ) ? next : lastOpen;
					eruptAt = spot with { Y = Sculpt.GroundAt( spot ) };
					Begin( Act.Erupt );
				}
				return true;
			}
			case Act.Erupt:
			{
				if ( a < EruptCue )
				{
					// Still under: sliding under the glowing spot.
					var to = (eruptAt - pos).Flat();
					var next = pos + to * MathF.Min( 1f, 8f * dt );
					next.Y = Sculpt.GroundAt( next ) - BuryDepth;
					GlobalPosition = next;
					velocity = Vector3.Zero;
					return true;
				}
				if ( !struck )
				{
					// Out of the sand at once (the burst of sand hides it), then up.
					struck = true;
					GlobalPosition = Sculpt.InBounds( pos with { Y = MathF.Max( pos.Y, groundMax + Clear ) } );
					pos = GlobalPosition;
					EruptHit( prey );
				}
				if ( a < EruptCue + EruptRise )
				{
					// Bursting up out of the sand.
					var top = groundMax + Clear + 2.2f * Size;
					var y = MathF.Min( top, pos.Y + 14f * dt );
					y = MathF.Max( y, groundMax + Clear );
					GlobalPosition = Sculpt.InBounds( pos with { Y = y } );
					ySpeed = 0f;
					Face( dir, 3f, dt );
					return true;
				}
				Face( dir, 2f, dt );
				wish = -dir * 1.5f;
				if ( a > EruptCue + EruptRise + 0.9f ) Begin( Act.Recover );
				return false;
			}
			case Act.ShockRise:
			{
				// Up a little, crackling; the warning rings glow on the sand round him.
				Face( dir, 3f, dt );
				wish = Vector3.Zero;
				velocity = velocity.Lerp( Vector3.Zero, 1f - MathF.Exp( -3f * dt ) );
				wantY = groundMax + CruiseHeight + 2.2f * Size * 0.5f;
				ringCentre = pos;
				if ( a > ShockRiseTime ) Begin( Act.ShockSlam );
				return false;
			}
			case Act.ShockSlam:
			{
				// Slammed flat on the sand; the rings go out (KingFrame sends them).
				velocity = Vector3.Zero;
				var low = groundMax + Clear;
				var y = a < ShockDrop ? MathF.Max( low, pos.Y - 18f * dt ) : low;
				if ( a >= ShockDrop ) y = low;
				GlobalPosition = pos with { Y = y };
				ySpeed = 0f;
				ringCentre = GlobalPosition;
				if ( a > ShockSlamTime ) Begin( Act.Recover );
				return true;
			}
		}
		return false;
	}

	/// <summary> The bomb line: through the player, along the way from him to them. </summary>
	void AimLine( Vector3 feet, Vector3 pos )
	{
		var d = (feet - pos).Flat();
		if ( d.LengthSquared() > 0.25f ) lineDir = d.Normalized();
		lineFrom = feet - lineDir * ((BombCount - 1) * BombSpacing * 0.5f);
		lineFrom.Y = feet.Y;
	}

	/// <summary> Ran into a wall mid run: the rest of the bombs fall where they were marked. </summary>
	void DropRest()
	{
		while ( bombsOut < BombCount ) DropBomb( bombsOut );
	}

	void DropBomb( int i )
	{
		if ( i >= BombCount || i >= bombAt.Length ) return;
		bombsOut = Math.Max( bombsOut, i + 1 );
		var from = body is not null ? body.GlobalTransform * new Vector3( 0f, -0.08f, 0.05f ) : GlobalPosition;
		var bomb = RayBomb.Drop( GetTree().CurrentScene, from, bombAt[i], Puppet ? 0f : BombDamage, BombRadius, this, i );
		if ( bomb is not null ) spawned.Add( bomb );
		BombsDropped++;
	}

	/// <summary> A bomb went off (the mark it fell on goes out). </summary>
	public void BombLanded( int i ) { if ( i >= 0 && i < bombLanded.Length ) bombLanded[i] = true; }

	/// <summary> He bursts up: everyone over the glowing spot is thrown up, zapped. </summary>
	void EruptHit( PlayerSurvivor prey )
	{
		Eruptions++;
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c ) continue;
			var feet = p.RealPosition;
			var flat = (feet - eruptAt).Flat();
			if ( flat.Length() > EruptRadius + c.BodyRadius || feet.Y - eruptAt.Y > 2.5f || feet.Y - eruptAt.Y < -1.5f ) continue;
			EruptHits++;
			p.Health?.OnDamage( new DamageInfo { Damage = EruptDamage, Attacker = this, Position = feet + Vector3.Up, Origin = eruptAt } );
			if ( !c.Scripted ) { c.Jump( Vector3.Up * EruptLaunch ); c.Velocity += flat.SafeNormal() * 3f; }   // Jump lifts them off the ground first
			Electrocuted.Apply( c, 0.45f, hold: false );
			p.Juice?.Shake( 1f );
		}
	}

	/// <summary> The nose dive lands: a shockwave through the sand round him. </summary>
	void DiveShock( Vector3 at )
	{
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || p.Controller is not { } c ) continue;
			var feet = p.RealPosition;
			var flat = (feet - at).Flat();
			if ( flat.Length() > DiveShockRadius || feet.Y - at.Y > 1.6f ) continue;
			DiveShockHits++;
			p.Health?.OnDamage( new DamageInfo { Damage = DiveShockDamage, Attacker = this, Position = feet + Vector3.Up, Origin = at } );
			if ( !c.Scripted ) { c.Jump( Vector3.Up * 4f ); c.Velocity += flat.SafeNormal() * 7f; }
			p.Juice?.Shake( 0.7f );
		}
	}

	// ---- Every frame, host and joiners: the cues, bombs, rings and bursts ----

	void KingFrame( float dt )
	{
		var a = (float)sinceAct;
		if ( act != kingSeen ) KingEnter( kingSeen, act );
		kingSeen = act;
		switch ( act )
		{
			case Act.BombMark or Act.BombRun:
			{
				if ( sinceKingFx > 0.1f || act == Act.BombRun )
				{
					sinceKingFx = 0f;
					for ( var i = 0; i < BombCount; i++ )
					{
						var p = lineFrom + lineDir * (i * BombSpacing);
						bombAt[i] = p with { Y = CueGround( p with { Y = p.Y + 1f } ) };
					}
				}
				if ( act == Act.BombRun )
				{
					// A bomb as he passes over each mark (a little before, so it lands on it).
					var along = (GlobalPosition - lineFrom).Flat().Dot( lineDir );
					while ( bombsOut < BombCount && along >= bombsOut * BombSpacing - 1.2f ) DropBomb( bombsOut );
				}
				DrawBombCue( a );
				break;
			}
			case Act.Burrow or Act.Buried:
			{
				if ( sinceKingFx > 0.07f )
				{
					sinceKingFx = 0f;
					// The sand heaves where he goes: puffs round him and a trail behind.
					var p = GlobalPosition;
					var sand = CueGround( p );
					for ( var i = 0; i < 3; i++ )
					{
						var at = p + Rng.OnSphere().Flat().SafeNormal() * Rng.Float( 0.4f, HalfSpan * ModelScale * 0.8f );
						GpuFx.SandPuff( at with { Y = sand + 0.05f }, (Vector3.Up * 0.6f + Rng.InSphere( 0.4f ).Flat()) * 0.8f, new Color( 0.62f, 0.58f, 0.5f, 0.7f ), Rng.Float( 4f, 8f ), Rng.Float( 0.6f, 1.1f ) );
					}
				}
				HideCue();
				break;
			}
			case Act.Erupt:
			{
				if ( a < EruptCue )
				{
					DrawEruptCue( a );
					if ( sinceKingFx > 0.06f )
					{
						sinceKingFx = 0f;
						var at = eruptAt + Rng.OnSphere().Flat().SafeNormal() * Rng.Float( 0f, EruptRadius );
						GpuFx.SandPuff( at with { Y = eruptAt.Y + 0.05f }, Vector3.Up * Rng.Float( 0.5f, 1.5f ), new Color( 0.62f, 0.58f, 0.5f, 0.75f ), Rng.Float( 4f, 9f ), Rng.Float( 0.5f, 1f ) );
					}
					break;
				}
				HideCue();
				if ( !burstShown ) { burstShown = true; BurstFx(); }
				break;
			}
			case Act.ShockRise:
				DrawShockCue( a );
				break;
			case Act.ShockSlam:
			{
				HideCue();
				if ( a >= ShockDrop && !slamShown ) { slamShown = true; SlamFx(); }
				while ( ringsOut < RingCount && a >= ShockDrop + ringsOut * RingGap )
				{
					var ring = ShockRing.Send( GetTree().CurrentScene, ringCentre, RingSpeed, RingReach, Puppet ? 0f : RingDamage, RingStun, this );
					if ( ring is not null ) spawned.Add( ring );
					ringsOut++;
					RingsSent++;
					Sfx.Play( "ray_zap", ringCentre, 1f, 0.6f + ringsOut * 0.08f );
				}
				break;
			}
			default:
				HideCue();
				break;
		}
		// While under the sand only his crown and eyes show (the wings are under it).
		var under = Underground;
		if ( disc is not null ) disc.Visible = !under || act == Act.Burrow && a < BurrowTime * 0.7f;
		if ( barb is not null && tail.Count > 0 ) tail[0].Visible = !under;
		foreach ( var s in spiracles ) s.Visible = !under;
		spawned.RemoveAll( n => !IsInstanceValid( n ) );
	}

	/// <summary> An act starts (on every copy): its sound, its name on the HUD, and what this copy has done of it reset. </summary>
	void KingEnter( Act from, Act to )
	{
		if ( from == Act.Dive && to == Act.Recover ) ShockwaveFx( GlobalPosition with { Y = CueGround( GlobalPosition ) } );
		if ( from == Act.BombRun ) DropRest();   // cut short (a wall): the rest fall on their marks
		sinceKingFx = 99f;
		switch ( to )
		{
			case Act.BombClimb:
				Say( "CARPET BOMB!" );
				Sfx.Play( "swim", GlobalPosition, 0.9f, 0.6f );
				break;
			case Act.BombMark:
				Sfx.Play( "whirl_warn", GlobalPosition, 1f, 0.8f );
				Array.Clear( bombLanded );
				bombsOut = 0;
				break;
			case Act.BombRun:
				bombsOut = 0;
				Sfx.Play( "dive_whoosh", GlobalPosition, 1f, 0.7f );
				break;
			case Act.Burrow:
				Say( "SAND AMBUSH!" );
				Sfx.Play( "kraken_rise", GlobalPosition, 0.9f, 1.4f );
				for ( var i = 0; i < 20; i++ )
				{
					var dir = (Rng.OnSphere().Flat() + Vector3.Up * 0.6f).SafeNormal();
					GpuFx.SandPuff( GlobalPosition with { Y = CueGround( GlobalPosition ) + 0.1f } + Rng.InSphere( 1.5f ).Flat(), dir * Rng.Float( 1f, 3f ), new Color( 0.62f, 0.58f, 0.5f, 0.75f ), Rng.Float( 8f, 16f ), Rng.Float( 0.8f, 1.5f ) );
				}
				break;
			case Act.Erupt:
				burstShown = false;
				Sfx.Play( "whirl_warn", eruptAt, 1f, 1.2f );
				Sfx.Play( "kraken_rise", eruptAt, 1f, 1.1f );
				break;
			case Act.ShockRise:
				Say( "SHOCK RINGS!" );
				Sfx.Play( "ray_charge", GlobalPosition, 1f, 0.6f );
				Sfx.Play( "whirl_warn", GlobalPosition, 1f, 1f );
				break;
			case Act.ShockSlam:
				ringsOut = 0;
				slamShown = false;
				break;
		}
	}

	void BurstFx()
	{
		var at = eruptAt;
		for ( var i = 0; i < 40; i++ )
		{
			var dir = (Rng.OnSphere().Flat() * 0.6f + Vector3.Up).SafeNormal();
			GpuFx.SandPuff( at + Rng.InSphere( EruptRadius * 0.6f ).Flat() + Vector3.Up * 0.1f, dir * Rng.Float( 2f, 6f ), new Color( 0.62f, 0.58f, 0.5f, 0.8f ), Rng.Float( 10f, 22f ), Rng.Float( 0.9f, 1.8f ) );
		}
		var scene = GetTree().CurrentScene;
		for ( var i = 0; i < 6; i++ )
		{
			var from = at + Vector3.Up * 0.3f + Rng.InSphere( 1f ).Flat();
			RayBolt.Arc( scene, from, from + Vector3.Up * Rng.Float( 2f, 4f ) + Rng.InSphere( 1.2f ).Flat(), 0.2f, 1.5f, light: i == 0 );
		}
		Sfx.Play( "dive_impact", at, 1f, 0.6f );
		Sfx.Play( "ray_zap", at, 1f, 0.75f );
		ShakeNear( at, 0.8f, 18f );
	}

	void SlamFx()
	{
		var at = ringCentre with { Y = CueGround( ringCentre ) };
		for ( var i = 0; i < 30; i++ )
		{
			var dir = (Rng.OnSphere().Flat() + Vector3.Up * 0.35f).SafeNormal();
			GpuFx.SandPuff( at + Rng.InSphere( 2.5f ).Flat() + Vector3.Up * 0.1f, dir * Rng.Float( 2f, 5f ), new Color( 0.62f, 0.58f, 0.5f, 0.75f ), Rng.Float( 8f, 16f ), Rng.Float( 0.8f, 1.4f ) );
		}
		Sfx.Play( "kraken_slam", at, 1f, 1.2f );
		ShakeNear( at, 0.7f, 22f );
	}

	/// <summary> The dive's landing: a ring of sand going out and a bump for anyone near. </summary>
	void ShockwaveFx( Vector3 at )
	{
		DiveShocks++;
		var ring = ShockRing.Send( GetTree().CurrentScene, at, 16f, DiveShockRadius, 0f, 0f, this, sand: true );
		if ( ring is not null ) spawned.Add( ring );
		Sfx.Play( "dive_impact", at, 1f, 0.7f );
		ShakeNear( at, 0.8f, 16f );
	}

	static void ShakeNear( Vector3 at, float amount, float reach )
	{
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( p.Controller is null ) continue;
			var d = p.RealPosition.DistanceTo( at );
			if ( d < reach ) p.Juice?.Shake( amount * (1f - d / reach) );
		}
	}

	// ---- The cues on the sand (like the Kraken's strike lines: faint while they follow, bright once locked) ----

	void CueMesh()
	{
		if ( cue is not null && IsInstanceValid( cue ) ) return;
		cueMesh = new ImmediateMesh();
		cuePaint = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			VertexColorUseAsAlbedo = true,
			NoDepthTest = false,
		};
		cuePaint.SetMeta( "no_caustics", true );
		cue = new MeshInstance3D { Name = "King Cue", Mesh = cueMesh, MaterialOverride = cuePaint, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( cue );
		cue.GlobalTransform = Transform3D.Identity;
	}

	void HideCue() { if ( cue is not null && IsInstanceValid( cue ) ) cue.Visible = false; }

	static Color Warn( bool locked, float a, float strength = 1f )
	{
		var pulse = 0.5f + 0.5f * MathF.Sin( a * 30f );
		return locked ? new Color( 1f, 0.2f, 0.12f, (0.5f + pulse * 0.3f) * strength ) : new Color( 1f, 0.6f, 0.15f, 0.3f * strength );
	}

	/// <summary> The carpet bomb's marks: a strip down the line and a ring the size of each blast where it will fall. </summary>
	void DrawBombCue( float a )
	{
		CueMesh();
		cue.Visible = true;
		cueMesh.ClearSurfaces();
		cueMesh.SurfaceBegin( Mesh.PrimitiveType.Triangles );
		var colour = Warn( lineLocked, a );
		var side = new Vector3( -lineDir.Z, 0f, lineDir.X );
		for ( var i = 0; i < BombCount; i++ )
		{
			if ( bombLanded[i] ) continue;
			GroundRing.Flat( cueMesh, this, bombAt[i], BombRadius - 0.25f, BombRadius, colour, 28 );
			GroundRing.Flat( cueMesh, this, bombAt[i], 0f, 0.45f, colour with { A = colour.A * 1.2f }, 12 );
			if ( i + 1 < BombCount && !bombLanded[i + 1] )
				GroundRing.Strip( cueMesh, this, bombAt[i], bombAt[i + 1], 0.22f, colour with { A = colour.A * 0.8f } );
		}
		cueMesh.SurfaceEnd();
	}

	/// <summary> The glowing spot where he will burst up: a ring that tightens and goes red, cracks across it. </summary>
	void DrawEruptCue( float a )
	{
		CueMesh();
		cue.Visible = true;
		cueMesh.ClearSurfaces();
		cueMesh.SurfaceBegin( Mesh.PrimitiveType.Triangles );
		var locked = a > EruptCue * 0.45f;
		var colour = Warn( locked, a, 1.2f );
		GroundRing.Flat( cueMesh, this, eruptAt, EruptRadius - 0.3f, EruptRadius, colour, 40 );
		GroundRing.Flat( cueMesh, this, eruptAt, 0f, EruptRadius * MathX.Clamp( a / EruptCue, 0.1f, 1f ), colour with { A = colour.A * 0.35f }, 32 );
		for ( var i = 0; i < 6; i++ )
		{
			var angle = i * MathF.Tau / 6f + 0.4f;
			var tip = eruptAt + new Vector3( MathF.Cos( angle ), 0f, MathF.Sin( angle ) ) * EruptRadius * 0.9f;
			GroundRing.Strip( cueMesh, this, eruptAt, tip, 0.12f, colour );
		}
		cueMesh.SurfaceEnd();
	}

	/// <summary> The shock rings' warning: rings on the sand where they will run, faint, then red just before the slam. </summary>
	void DrawShockCue( float a )
	{
		CueMesh();
		cue.Visible = true;
		cueMesh.ClearSurfaces();
		cueMesh.SurfaceBegin( Mesh.PrimitiveType.Triangles );
		var locked = a > ShockRiseTime - ShockLockBefore;
		var colour = Warn( locked, a );
		var centre = ringCentre with { Y = CueGround( ringCentre ) };
		for ( var k = 1; k <= 3; k++ )
		{
			var r = RingReach * k / 3f;
			GroundRing.Flat( cueMesh, this, centre, r - 0.2f, r, colour with { A = colour.A * (1.2f - k * 0.2f) }, 64 );
		}
		GroundRing.Flat( cueMesh, this, centre, 0f, 1.4f * Size, colour with { A = colour.A * 0.5f }, 32 );
		cueMesh.SurfaceEnd();
	}

	// ---- The crown ----

	/// <summary> A gold crown like Poseidon's on his head: a band rimmed top and bottom, seven spikes, jewels below them. </summary>
	void BuildCrown()
	{
		var gold = Sculpt.Paint( new Color( 1f, 0.76f, 0.26f ), 0.14f, 0.95f );
		gold.EmissionEnabled = true;
		gold.Emission = new Color( 1f, 0.76f, 0.26f );
		gold.EmissionEnergyMultiplier = 0.15f;
		var purple = Sculpt.Glow( new Color( 0.7f, 0.22f, 1f ), 2.4f );
		var green = Sculpt.Glow( new Color( 0.35f, 1f, 0.3f ), 2.4f );
		var yellow = Sculpt.Glow( new Color( 1f, 0.82f, 0.15f ), 2.2f );
		var red = Sculpt.Glow( new Color( 1f, 0.15f, 0.25f ), 2.4f );
		var head = DiscPoint( 0f, 0.36f, true );
		crown = Sculpt.Joint( body, head + new Vector3( 0f, -0.012f, 0f ), "Crown" );
		crown.Rotation = new Vector3( MathX.Rad( -8f ), 0f, 0f );
		const float r = 0.13f, h = 0.075f;
		var band = new MeshInstance3D { Name = "Crown Band", Mesh = new CylinderMesh { TopRadius = r * 1.08f, BottomRadius = r, Height = h, RadialSegments = 48, Rings = 1, CapTop = false, CapBottom = true }, MaterialOverride = gold };
		crown.AddChild( band );
		band.Position = new Vector3( 0f, h * 0.5f, 0f );
		foreach ( var (y, rr) in new[] { (0.004f, r), (h, r * 1.08f) } )
		{
			var rim = new MeshInstance3D { Name = "Crown Rim", Mesh = new TorusMesh { InnerRadius = rr - 0.007f, OuterRadius = rr + 0.01f, Rings = 48, RingSegments = 8 }, MaterialOverride = gold };
			crown.AddChild( rim );
			rim.Position = new Vector3( 0f, y, 0f );
		}
		for ( var k = 0; k < 7; k++ )
		{
			// The spikes go right round; the front one (towards the nose, -Z) the tallest.
			var angle = -MathF.PI * 0.5f + k * MathF.Tau / 7f;
			var outward = new Vector3( MathF.Cos( angle ), 0f, MathF.Sin( angle ) );
			var foot = outward * r * 1.06f + Vector3.Up * h;
			var tall = k == 0 ? 0.13f : 0.075f + (k % 2) * 0.02f;
			Sculpt.Cone( crown, foot, foot + outward * 0.02f + Vector3.Up * tall, 0.026f, gold, 14, "Crown Spike" );
			SmoothKit.Ball( crown, foot + outward * 0.02f + Vector3.Up * (tall + 0.008f), Vector3.One * 0.022f, k == 0 ? red : yellow, null, "Crown Pearl" );
			var jewel = k == 0 ? red : k % 3 == 0 ? yellow : k % 2 == 0 ? green : purple;
			SmoothKit.Ball( crown, outward * (r * 1.12f) + Vector3.Up * (h * 0.5f), Vector3.One * (k == 0 ? 0.045f : 0.03f), jewel, null, "Jewel" );
		}
		// A velvet cap inside the band.
		SmoothKit.Ball( crown, Vector3.Up * h * 0.7f, new Vector3( r * 1.9f, h * 1.6f, r * 1.9f ), Sculpt.Paint( new Color( 0.45f, 0.08f, 0.35f ), 0.85f ), null, "Crown Velvet" );
		foreach ( var n in body.GetChildren() ) if ( n is Node3D s && s.Name.ToString().StartsWith( "Spiracle" ) ) spiracles.Add( s );
		var part = CreaturePart.Create( this, 7, "Crown", 0.13f );
		crown.AddChild( part );
		part.Position = Vector3.Up * h;
	}

	/// <summary> On death the crown comes off in one piece and sinks. </summary>
	int CrownOff( Vector3 velocity )
	{
		if ( crown is null || !IsInstanceValid( crown ) ) return 0;
		Loose( crown, 6f * Size, 2f, velocity );
		crown = null;
		return 1;
	}

	void KingDied()
	{
		Say( "THE KING IS DEAD!" );
		KingCleanup();
	}

	protected void KingCleanup()
	{
		foreach ( var n in spawned ) if ( IsInstanceValid( n ) ) n.QueueFree();
		spawned.Clear();
		if ( cue is not null && IsInstanceValid( cue ) ) cue.QueueFree();
		cue = null;
	}

	// ---- Tests and the dev menu ----

	public static readonly string[] KingAttacks = { "Zap", "Dive", "Carpet bomb", "Sand ambush", "Shock rings" };

	/// <summary> Starts one of his five attacks now. </summary>
	public void ForceAttack( string name )
	{
		sinceSpecial = 99f;
		switch ( name )
		{
			case "Zap": ForceZap(); break;
			case "Dive": ForceDive(); break;
			case "Carpet bomb": Begin( Act.BombClimb ); break;
			case "Sand ambush": Begin( Act.Burrow ); break;
			case "Shock rings": Begin( Act.ShockRise ); break;
		}
	}

	// ---- Online ----

	void KingWrite( List<float> s )
	{
		s.Add( lineFrom.X ); s.Add( lineFrom.Y ); s.Add( lineFrom.Z );
		s.Add( lineDir.X ); s.Add( lineDir.Z ); s.Add( lineLocked ? 1f : 0f );
		s.Add( eruptAt.X ); s.Add( eruptAt.Y ); s.Add( eruptAt.Z );
		s.Add( ringCentre.X ); s.Add( ringCentre.Y ); s.Add( ringCentre.Z );
	}

	void KingRead( ReadOnlySpan<float> s )
	{
		if ( s.Length < 12 ) return;
		lineFrom = new Vector3( s[0], s[1], s[2] );
		var d = new Vector3( s[3], 0f, s[4] );
		if ( d.LengthSquared() > 0.01f ) lineDir = d.Normalized();
		lineLocked = s[5] > 0.5f;
		eruptAt = new Vector3( s[6], s[7], s[8] );
		ringCentre = new Vector3( s[9], s[10], s[11] );
	}
}

/// <summary> Ribbons on the ground for the king's cues and rings, following the sand (built into an ImmediateMesh surface). </summary>
public static class GroundRing
{
	static float At( Node3D context, Vector3 p )
	{
		var g = Sculpt.GroundAt( p );
		return g != float.MinValue ? g : Sculpt.FloorBelow( context, p with { Y = p.Y + 1f }, 30f );
	}

	static void Quad( ImmediateMesh m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col )
	{
		m.SurfaceSetColor( col ); m.SurfaceAddVertex( a );
		m.SurfaceSetColor( col ); m.SurfaceAddVertex( b );
		m.SurfaceSetColor( col ); m.SurfaceAddVertex( c );
		m.SurfaceSetColor( col ); m.SurfaceAddVertex( b );
		m.SurfaceSetColor( col ); m.SurfaceAddVertex( d );
		m.SurfaceSetColor( col ); m.SurfaceAddVertex( c );
	}

	/// <summary> A flat ring (or a disc, `inner` 0) lying on the ground round `centre`. </summary>
	public static void Flat( ImmediateMesh m, Node3D context, Vector3 centre, float inner, float outer, Color col, int segments )
	{
		const float lift = 0.07f;
		Vector3 Point( float r, int i )
		{
			var angle = i * MathF.Tau / segments;
			var p = centre + new Vector3( MathF.Cos( angle ) * r, 0f, MathF.Sin( angle ) * r );
			return p with { Y = (r < 0.01f ? centre.Y : At( context, p )) + lift };
		}
		var lastIn = Point( inner, 0 );
		var lastOut = Point( outer, 0 );
		for ( var i = 1; i <= segments; i++ )
		{
			var nIn = Point( inner, i );
			var nOut = Point( outer, i );
			Quad( m, lastIn, lastOut, nIn, nOut, col );
			lastIn = nIn;
			lastOut = nOut;
		}
	}

	/// <summary> A strip along the ground from one point to another. </summary>
	public static void Strip( ImmediateMesh m, Node3D context, Vector3 from, Vector3 to, float halfWidth, Color col )
	{
		const float lift = 0.06f;
		var d = (to - from).Flat();
		if ( d.LengthSquared() < 0.0001f ) return;
		var side = new Vector3( -d.Z, 0f, d.X ).Normalized() * halfWidth;
		var steps = Math.Max( 1, (int)(d.Length() / 0.6f) );
		Vector3 P( float t, float s )
		{
			var p = from.Lerp( to, t ) + side * s;
			return p with { Y = At( context, p ) + lift };
		}
		for ( var i = 0; i < steps; i++ )
		{
			float t0 = i / (float)steps, t1 = (i + 1f) / steps;
			Quad( m, P( t0, -1f ), P( t0, 1f ), P( t1, -1f ), P( t1, 1f ), col );
		}
	}
}

/// <summary>
/// One of King Raymond's shock rings: a wall of crackling electricity knee high that runs out across the ground from where
/// he slammed down. A player standing on the ground as it passes is hurt and stunned for a moment; one in the air (a jump)
/// is missed. On a joiner's copy it does no damage (the host lands the hits). `sand` makes the dive's harmless ring of sand.
/// </summary>
public partial class ShockRing : Node3D
{
	public static readonly List<ShockRing> All = new();
	public const float Height = 0.6f;
	const int Segments = 72;

	Vector3 centre;
	float radius, speed, reach, damage, stun;
	bool sand;
	Node3D owner;
	readonly HashSet<PlayerSurvivor> hit = new();
	ImmediateMesh mesh;
	MeshInstance3D view;
	ShaderMaterial paint;

	public float Radius => radius;

	public static ShockRing Send( Node parent, Vector3 centre, float speed, float reach, float damage, float stun, Node3D owner, bool sand = false )
	{
		if ( parent is null ) return null;
		var r = new ShockRing { Name = sand ? "Dive Shockwave" : "Shock Ring", centre = centre, speed = speed, reach = reach, damage = damage, stun = stun, owner = owner, sand = sand, radius = sand ? 0.5f : 1.2f };
		parent.AddChild( r );
		return r;
	}

	static readonly Shader RingShader = new()
	{
		Code = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;
uniform vec3 colour : source_color = vec3(0.45, 1.0, 0.9);
uniform vec3 core : source_color = vec3(0.9, 1.0, 0.95);
uniform float fade = 1.0;
uniform float sandy = 0.0;
float hash(float x) { return fract(sin(x * 127.1) * 43758.5453); }
void fragment() {
	// UV.x round the ring, UV.y up it. Crackling: bright jagged lines that flicker, brightest low down.
	float t = floor(TIME * 24.0);
	float along = UV.x * 220.0;
	float jag = mix(hash(floor(along) + t), hash(floor(along) + 1.0 + t), fract(along));
	float line = 1.0 - smoothstep(0.0, 0.09, abs(UV.y - (0.25 + jag * 0.55)));
	float base = (1.0 - UV.y) * 0.45;
	vec3 c = mix(colour * (base + line * 1.6) + core * line * 0.8, vec3(0.55, 0.5, 0.42) * (1.0 - UV.y) * 0.9, sandy);
	ALBEDO = c * fade;
	ALPHA = 1.0;
}",
	};

	public override void _Ready()
	{
		All.Add( this );
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		mesh = new ImmediateMesh();
		paint = new ShaderMaterial { Shader = RingShader };
		paint.SetShaderParameter( "sandy", sand ? 1f : 0f );
		paint.SetMeta( "no_caustics", true );
		view = new MeshInstance3D { Name = "Ring", Mesh = mesh, MaterialOverride = paint, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild( view );
	}

	public override void _ExitTree() => All.Remove( this );

	float Ground( Vector3 p )
	{
		var g = Sculpt.GroundAt( p );
		return g != float.MinValue ? g : centre.Y;
	}

	public override void _Process( double delta )
	{
		var dt = MathF.Min( (float)delta, 0.1f );
		var was = radius;
		radius += speed * dt;
		if ( radius > reach ) { QueueFree(); return; }
		var fade = MathX.Clamp( (reach - radius) / (reach * 0.25f), 0f, 1f );
		paint.SetShaderParameter( "fade", fade );
		Build( sand ? 0.4f : Height );
		if ( damage <= 0f ) return;
		foreach ( var p in PlayerSurvivor.All )
		{
			if ( !p.IsAlive || hit.Contains( p ) || p.Controller is not { } c ) continue;
			var feet = p.RealPosition;
			var d = (feet - centre).Flat().Length();
			// The ring sweeps from `was` to `radius` this frame; it hits them where it passes.
			if ( d < was - c.BodyRadius || d > radius + c.BodyRadius ) continue;
			var ground = MathF.Max( Ground( feet ), Sculpt.FloorBelow( c, feet + Vector3.Up * 0.3f, 2f ) );
			var up = feet.Y - ground;
			if ( up > Height - 0.05f ) { hit.Add( p ); Stingray.RingJumps++; continue; }   // jumped it
			if ( up < -0.6f ) continue;
			hit.Add( p );
			Stingray.RingHits++;
			p.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = IsInstanceValid( owner ) ? owner : null, Position = feet + Vector3.Up * 0.3f, Origin = centre } );
			if ( stun > 0f ) Electrocuted.Apply( c, stun, hold: true );
			p.Juice?.Shake( 0.5f );
		}
	}

	/// <summary> The ring as a band round the centre, standing on the ground at each point, its top a little ragged. </summary>
	void Build( float height )
	{
		mesh.ClearSurfaces();
		mesh.SurfaceBegin( Mesh.PrimitiveType.Triangles );
		Vector3 lastLow = default, lastHigh = default;
		var lastU = 0f;
		for ( var i = 0; i <= Segments; i++ )
		{
			var angle = i * MathF.Tau / Segments;
			var p = centre + new Vector3( MathF.Cos( angle ) * radius, 0f, MathF.Sin( angle ) * radius );
			var g = Ground( p );
			var low = p with { Y = g + 0.02f };
			var high = p with { Y = g + height * (0.85f + 0.15f * MathF.Sin( angle * 13f + radius * 3f )) };
			var u = i / (float)Segments;
			if ( i > 0 )
			{
				mesh.SurfaceSetUV( new Vector2( lastU, 1f ) ); mesh.SurfaceAddVertex( lastLow );
				mesh.SurfaceSetUV( new Vector2( lastU, 0f ) ); mesh.SurfaceAddVertex( lastHigh );
				mesh.SurfaceSetUV( new Vector2( u, 1f ) ); mesh.SurfaceAddVertex( low );
				mesh.SurfaceSetUV( new Vector2( lastU, 0f ) ); mesh.SurfaceAddVertex( lastHigh );
				mesh.SurfaceSetUV( new Vector2( u, 0f ) ); mesh.SurfaceAddVertex( high );
				mesh.SurfaceSetUV( new Vector2( u, 1f ) ); mesh.SurfaceAddVertex( low );
			}
			lastLow = low;
			lastHigh = high;
			lastU = u;
		}
		mesh.SurfaceEnd();
	}
}

/// <summary>
/// One of King Raymond's bombs: a dark alien shell banded in glowing green with a blinking light, dropped from his belly. It
/// falls (faster and faster) onto the mark it was aimed at and goes off as an alien blast, hurting players in its
/// radius. On a joiner's copy it does no damage.
/// </summary>
public partial class RayBomb : Node3D
{
	public static readonly List<RayBomb> All = new();
	Vector3 from, to;
	float damage, radius, fall, age;
	Stingray owner;
	int index;
	OmniLight3D blink;

	static StandardMaterial3D shell, band;

	public static RayBomb Drop( Node parent, Vector3 from, Vector3 to, float damage, float radius, Stingray owner, int index )
	{
		if ( parent is null ) return null;
		var height = MathF.Max( 0.5f, from.Y - to.Y );
		var b = new RayBomb { Name = "Ray Bomb", from = from, to = to, damage = damage, radius = radius, owner = owner, index = index, fall = MathX.Clamp( MathF.Sqrt( 2f * height / 16f ), 0.35f, 1.4f ) };
		parent.AddChild( b );
		b.GlobalPosition = from;
		Sfx.Play( "missile_launch", from, 0.6f, 0.6f );
		return b;
	}

	public override void _Ready()
	{
		All.Add( this );
		shell ??= Sculpt.Paint( new Color( 0.12f, 0.1f, 0.16f ), 0.35f, 0.4f );
		band ??= Sculpt.Glow( new Color( 0.35f, 1f, 0.45f ), 2.6f );
		var look = new Node3D { Name = "Look", Scale = Vector3.One * 1.6f };   // sized to the king
		AddChild( look );
		SmoothKit.Ball( look, Vector3.Zero, new Vector3( 0.5f, 0.62f, 0.5f ), shell, null, "Bomb Shell" );
		var ring = new MeshInstance3D { Name = "Bomb Band", Mesh = new TorusMesh { InnerRadius = 0.24f, OuterRadius = 0.28f, Rings = 24, RingSegments = 6 }, MaterialOverride = band };
		look.AddChild( ring );
		for ( var k = 0; k < 4; k++ )
		{
			var a = k * MathF.Tau / 4f;
			var dir = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) );
			Sculpt.Cone( look, Vector3.Up * 0.2f + dir * 0.12f, Vector3.Up * 0.42f + dir * 0.2f, 0.05f, band, 6, "Bomb Fin" );
		}
		blink = new OmniLight3D { LightColor = new Color( 0.4f, 1f, 0.5f ), LightEnergy = 1.5f, OmniRange = 3f, ShadowEnabled = false };
		AddChild( blink );
	}

	public override void _ExitTree() => All.Remove( this );

	public override void _Process( double delta )
	{
		age += (float)delta;
		var u = MathX.Clamp( age / fall, 0f, 1f );
		var p = new Vector3( MathX.Lerp( from.X, to.X, u ), MathX.Lerp( from.Y, to.Y, u * u ), MathX.Lerp( from.Z, to.Z, u ) );
		GlobalPosition = p;
		Rotation = new Vector3( 0f, age * 6f, 0.25f * MathF.Sin( age * 9f ) );
		blink.LightEnergy = MathF.Sin( age * 30f ) > 0f ? 2.5f : 0.3f;
		if ( u < 1f ) return;
		Go();
	}

	void Go()
	{
		var at = to + Vector3.Up * 0.3f;
		var scene = GetTree().CurrentScene;
		AlienBlast.Spawn( scene, at, 1.4f );
		if ( IsInstanceValid( owner ) ) owner.BombLanded( index );
		var attacker = IsInstanceValid( owner ) ? owner : null;
		if ( damage > 0f )
			foreach ( var p in PlayerSurvivor.All )
			{
				if ( !p.IsAlive || p.Controller is not { } c ) continue;
				var chest = p.RealPosition + Vector3.Up * 0.9f;
				var d = chest.DistanceTo( at );
				if ( d > radius + c.BodyRadius ) continue;
				Stingray.BombHits++;
				p.Health?.OnDamage( new DamageInfo { Damage = damage, Attacker = attacker, Position = chest, Origin = at } );
				if ( !c.Scripted ) { c.Jump( Vector3.Up * 4f ); c.Velocity += (chest - at).Flat().SafeNormal() * 6f; }
			}
		for ( var i = 0; i < 10; i++ )
		{
			var dir = (Rng.OnSphere().Flat() + Vector3.Up * 0.5f).SafeNormal();
			GpuFx.SandPuff( to + Vector3.Up * 0.1f, dir * Rng.Float( 1f, 3f ), new Color( 0.62f, 0.58f, 0.5f, 0.75f ), Rng.Float( 6f, 12f ), Rng.Float( 0.7f, 1.3f ) );
		}
		QueueFree();
	}
}
