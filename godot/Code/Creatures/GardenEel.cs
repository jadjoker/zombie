using Godot;

/// <summary>
/// The garden eel (user's design, after photos of spotted garden eels): a small angry eel living in a hole in the sand,
/// part of the scenery rather than the round (it never counts toward a wave). They live in beds of 2 to 4 packed close
/// together (<see cref="EelBed"/>), 4 to 6 beds an area from the wreck site on. Hidden under the sand until a player comes
/// near, it digs out of its hole, stands up swaying with its head turned to them, and nips at anyone in reach. Shot, it
/// ducks back into its hole for a moment; killed, it bursts and its bed grows a new one after a while. Weak spot: the head.
/// Metres; the creature's node sits on the mouth of its hole, facing (-Z) whoever it watches.
/// </summary>
public partial class GardenEel : Creature
{
	public override bool CanScare => false;
	public static readonly List<GardenEel> All = new();

	public const int Joints = 8;
	public const float Step = 0.2f;                 // between joints
	public const float Below = 0.3f;                // the root's depth under the sand while it stands up
	public const float Radius = 0.055f;             // the body's half width
	public float WakeRange = 9f;                    // a player this close (flat) wakes it
	public float SleepRange = 15f;                  // nobody this close for a while: back down
	public float Reach = 1.75f;                     // flat from the hole to the player's middle
	public float BiteDamage = 4f;            // a nip: a bed of four is still worth stepping away from
	public float BiteCooldown = 1.3f;
	public const float DigTime = 0.9f, WindupTime = 0.35f, LungeTime = 0.18f, DuckTime = 1.6f;
	public const float HeadMultiplier = 2f;

	/// <summary> Tests: bites that landed, ducks, digs out. </summary>
	public static int Bites, Ducks, Digs;

	enum Act { Hidden, Dig, Up, Windup, Lunge, Duck, Sink }
	Act act = Act.Hidden;
	TimeSince sinceAct = 99f, sinceBite = 99f, sinceNear = 0f, sinceHit = 99f;
	bool struck;
	float rise, riseVel, phase, lean, leanVel, look;
	Vector3 target;

	public static GardenEel Spawn( Node parent, Vector3 hole, float maxHealth ) =>
		Place( new GardenEel { Name = "Garden Eel" }, parent, hole, maxHealth );

	protected override void Track( bool add ) { if ( add ) All.Add( this ); else All.Remove( this ); }

	public override GibRecipe Gibs => GibRecipes.GardenEel( Blood, Skins[Tint], SpotBlack );
	/// <summary> It stays in its hole: no dragging it out with the Gore Vac or a bubble. </summary>
	public override bool Pullable => false;
	public override void Pull( Vector3 delta ) { }
	public override bool WeakSpot( int index ) => index == 0;
	/// <summary> Down in the sand it is nothing to aim at (no health bar, no aim assist, no skill picks it). </summary>
	public override bool IsEnemy => act != Act.Hidden || rise > 0.05f;

	public bool Hidden => act == Act.Hidden;
	public bool IsOut => act is Act.Up or Act.Windup or Act.Lunge;
	public float Rise => rise;
	public string ActName => act.ToString();
	public Vector3 HeadPosition => head is not null ? head.GlobalPosition : GlobalPosition;

	protected override void Setup()
	{
		BeginSetup( 45f );
		phase = Rng.Float( 0f, MathF.Tau );
		sinceBite = Rng.Float( 0f, BiteCooldown );
		tint = Rng.Int( 0, Skins.Length - 1 );
		Build();
		Pose( 0f );
	}

	public override void _Process( double delta )
	{
		if ( health is null || body is null ) return;
		var dt = MathF.Min( (float)delta, 0.1f );
		if ( !IsDead && !Frozen ) Think( dt );
		Pose( dt );
	}

	void Think( float dt )
	{
		var prey = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var feet = prey?.RealPosition ?? GlobalPosition;
		var flat = (feet - GlobalPosition).Flat();
		var dist = prey is null ? 999f : flat.Length();
		var up = feet.Y - GlobalPosition.Y;
		if ( dist < SleepRange ) sinceNear = 0f;
		if ( prey is not null && dist < WakeRange ) target = feet;
		var a = (float)sinceAct;
		var inReach = prey is not null && dist < Reach + 0.25f && up > -0.6f && up < 1.4f;
		switch ( act )
		{
			case Act.Hidden:
				if ( prey is not null && dist < WakeRange && MathF.Abs( up ) < 4f ) Begin( Act.Dig );
				break;
			case Act.Dig:
				FaceTarget( dt, 3f );
				if ( a > DigTime ) Begin( Act.Up );
				break;
			case Act.Up:
				FaceTarget( dt, 4f );
				if ( inReach && sinceBite > BiteCooldown ) Begin( Act.Windup );
				else if ( sinceNear > 2.5f ) Begin( Act.Sink );
				break;
			case Act.Windup:
				FaceTarget( dt, 8f );
				if ( a > WindupTime ) Begin( Act.Lunge );
				break;
			case Act.Lunge:
				if ( !struck && a > LungeTime * 0.7f )
				{
					struck = true;
					sinceBite = Rng.Float( -0.3f, 0.3f );
					if ( inReach && prey?.Health is { } h )
					{
						Bites++;
						h.OnDamage( new DamageInfo { Damage = BiteDamage, Attacker = this, Position = feet + Vector3.Up * MathX.Clamp( up + 0.9f, 0.3f, 1.4f ), Origin = HeadPosition } );
						prey.Juice?.Shake( 0.2f );
						Sfx.Play( "crab_snap", HeadPosition, 0.6f, Rng.Float( 1.6f, 1.9f ) );
					}
				}
				if ( a > LungeTime + 0.25f ) Begin( Act.Up );
				break;
			case Act.Duck:
				if ( a > DuckTime ) Begin( prey is not null && dist < WakeRange ? Act.Dig : Act.Sink );
				break;
			case Act.Sink:
				if ( prey is not null && dist < WakeRange * 0.8f ) Begin( Act.Dig );
				else if ( rise < 0.02f ) Begin( Act.Hidden );
				break;
		}
	}

	void FaceTarget( float dt, float rate ) { if ( target != Vector3.Zero ) Face( target - GlobalPosition, rate, dt ); }

	void Begin( Act next )
	{
		act = next;
		sinceAct = 0f;
		struck = false;
		switch ( next )
		{
			case Act.Dig:
				Digs++;
				DigFx();
				break;
			case Act.Windup:
				Sfx.Play( "creature_attack", GlobalPosition + Vector3.Up, 0.35f, Rng.Float( 1.7f, 2f ) );
				break;
			case Act.Duck:
				Ducks++;
				Sfx.Play( "swim", GlobalPosition, 0.4f, 1.8f );
				break;
		}
	}

	void DigFx()
	{
		var at = GlobalPosition + Vector3.Up * 0.05f;
		for ( var i = 0; i < 6; i++ )
		{
			var dir = (Rng.OnSphere().Flat() * 0.6f + Vector3.Up).SafeNormal();
			GpuFx.SandPuff( at, dir * Rng.Float( 0.4f, 1.1f ), new Color( 0.62f, 0.58f, 0.5f, 0.7f ), Rng.Float( 2.5f, 5f ), Rng.Float( 0.5f, 0.9f ) );
		}
		Sfx.Play( "land", at, 0.25f, 1.8f );
		ShowHole();
	}

	// ---- Hits ----

	public override void ReceiveShot( int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( NetCreatures.Intercept( this, index, ref damage, direction, position, origin, shooter, scoreHit ) ) return;
		if ( IsDead ) return;
		NoteShot( direction, position, origin );
		sinceHit = 0f;
		var weak = index == 0;
		var dealt = damage * (weak ? HeadMultiplier : 1f);
		var wasAlive = !IsDead;
		if ( !Lethal( dealt ) )
		{
			gore?.GooBurst( position, -direction, Blood, 5, 200f, 0.5f );
			GibKit.Chips( Gibs, position, -direction, direction, 0.035f, 1 );
		}
		health.OnDamage( new DamageInfo { Damage = dealt, Position = position, Origin = origin, Attacker = shooter } );
		var killed = wasAlive && IsDead;
		if ( !IsDead && !Puppet && act is not (Act.Hidden or Act.Duck) ) Begin( Act.Duck );   // shot: back into its hole for a moment
		if ( wasAlive && shooter is not null ) shooter.RewardHit( killed, weak, scoreHit );
	}

	protected override void OnDied()
	{
		RaiseKilled();
		var centre = body.GlobalTransform * new Vector3( 0f, 0f, 0f );
		if ( head is not null ) centre = head.GlobalPosition;
		var dir = lastShotDirection == Vector3.Zero ? Vector3.Up : lastShotDirection.Normalized();
		gore?.GooBurst( centre, dir, Blood, 14, 260f, 0.8f );
		gore?.GooSpray( centre, dir, 3, Blood, 200f, 0.5f, 1f );
		GibKit.Burst( Gibs, centre, dir, 0.09f, 6 );
		// The head comes off and drifts; the rest is shredded into the burst.
		if ( head is not null && IsInstanceValid( head ) && rise > 0.3f )
		{
			Gib.Attach( head, gore, 3f, 20f, 0.3f, dir * 2f + Vector3.Up * 1.5f, Blood );
			CreatureGore.Twitch( head, 2f, 0.8f );
		}
		Sfx.Play( "stab_squish_2", centre, 0.7f, 1.4f );
		QueueFree();
	}

	// ---- Online ----

	public override void NetWrite( List<float> s ) { s.Add( (int)act ); s.Add( (float)sinceAct ); }

	public override void NetRead( ReadOnlySpan<float> s )
	{
		if ( s.Length < 2 ) return;
		var next = (Act)(int)s[0];
		var changed = next != act;
		if ( changed ) Begin( next );
		NetTimer( ref sinceAct, s[1], changed );
	}

	// ---- Tests ----

	/// <summary> Tests: up out of its hole now (as if a player came near). </summary>
	public void ForceUp() { if ( act is Act.Hidden or Act.Sink ) Begin( Act.Dig ); }
}

/// <summary>
/// A bed of garden eels: 2 to 4 holes packed close in the sand. It makes the eels (on the host; joiners get copies) and,
/// when one dies, grows a new one in its hole after <see cref="RegrowMin"/> to <see cref="RegrowMax"/> seconds.
/// <see cref="Lay"/> puts 4 to 6 beds over an area from the wreck site on.
/// </summary>
public partial class EelBed : Node3D
{
	public static readonly List<EelBed> All = new();
	public const int BedsMin = 4, BedsMax = 6, EelsMin = 2, EelsMax = 4;
	public const float RegrowMin = 45f, RegrowMax = 75f;
	public const float EelHealth = 45f;
	/// <summary> Tests: speeds up the regrowing. </summary>
	public static float RegrowScale = 1f;

	readonly List<Vector3> holes = new();
	readonly List<GardenEel> eels = new();
	readonly List<float> regrow = new();

	public IReadOnlyList<Vector3> Holes => holes;
	public IReadOnlyList<GardenEel> Eels => eels;

	public override void _EnterTree() => All.Add( this );
	public override void _ExitTree() => All.Remove( this );

	/// <summary> Clears the old beds and puts new ones about the area (the host and offline only; joiners get the eels). </summary>
	public static void Lay( Node3D level )
	{
		Clear();
		if ( Net.IsClient || level is null || SeaTerrain.Area < 2 || !SeaTerrain.Active ) return;
		var beds = Rng.Int( BedsMin, BedsMax );
		var made = 0;
		var placed = new List<Vector3>();
		for ( var b = 0; b < beds; b++ )
		{
			if ( FindSpot( level, placed ) is not { } centre ) continue;
			placed.Add( centre );
			if ( Make( level, centre, Rng.Int( EelsMin, EelsMax ) ) is not null ) made++;
		}
		GD.Print( $"[eels] {made} garden eel beds, {GardenEel.All.Count} eels, in area {SeaTerrain.Area}" );
	}

	public static void Clear()
	{
		foreach ( var bed in All.ToList() ) if ( IsInstanceValid( bed ) ) { bed.GetParent()?.RemoveChild( bed ); bed.QueueFree(); }
		foreach ( var eel in GardenEel.All.ToList() ) if ( IsInstanceValid( eel ) && !eel.Puppet ) { eel.GetParent()?.RemoveChild( eel ); eel.QueueFree(); }
	}

	/// <summary> A bed of `count` eels round `centre` (also the dev menu's). Null if no hole fits. </summary>
	public static EelBed Make( Node3D level, Vector3 centre, int count )
	{
		var bed = new EelBed { Name = "Garden Eels" };
		level.AddChild( bed );
		bed.GlobalPosition = centre;
		// Packed close like a real bed: each hole 0.45 to 0.75 m from one already dug, none nearer than 0.42 m.
		for ( var tries = 0; tries < 60 && bed.holes.Count < count; tries++ )
		{
			Vector3 at;
			if ( bed.holes.Count == 0 ) at = centre;
			else
			{
				var from = bed.holes[Rng.Int( 0, bed.holes.Count - 1 )];
				var angle = Rng.Float( 0f, MathF.Tau );
				at = from + new Vector3( MathF.Cos( angle ), 0f, MathF.Sin( angle ) ) * Rng.Float( 0.45f, 0.75f );
			}
			if ( bed.holes.Any( h => h.Flat().DistanceTo( at.Flat() ) < 0.42f ) ) continue;
			if ( OpenFloor( bed, at ) is not { } floor ) continue;
			bed.holes.Add( at with { Y = floor } );
		}
		return bed.Fill();
	}

	/// <summary> A bed with its holes just where given (tests: the worst layouts, a row pointing at the player). </summary>
	public static EelBed MakeAt( Node3D level, IReadOnlyList<Vector3> holes )
	{
		var bed = new EelBed { Name = "Garden Eels" };
		level.AddChild( bed );
		bed.GlobalPosition = holes[0];
		bed.holes.AddRange( holes );
		return bed.Fill();
	}

	EelBed Fill()
	{
		if ( holes.Count == 0 ) { QueueFree(); return null; }
		foreach ( var hole in holes ) { eels.Add( null ); regrow.Add( 0f ); }
		for ( var i = 0; i < holes.Count; i++ ) Grow( i );
		return this;
	}

	void Grow( int i )
	{
		var scene = GetTree()?.CurrentScene;
		if ( scene is null ) return;
		var eel = GardenEel.Spawn( scene, holes[i], EelHealth );
		eel.RotationDegrees = new Vector3( 0f, Rng.Float( 0f, 360f ), 0f );
		eels[i] = eel;
	}

	public override void _Process( double delta )
	{
		if ( Net.IsClient ) return;
		var dt = (float)delta * RegrowScale;
		for ( var i = 0; i < holes.Count; i++ )
		{
			var e = eels[i];
			if ( e is not null && IsInstanceValid( e ) && !e.IsDead ) continue;
			if ( regrow[i] <= 0f ) { regrow[i] = Rng.Float( RegrowMin, RegrowMax ); continue; }
			regrow[i] -= dt;
			if ( regrow[i] <= 0f ) { regrow[i] = 0f; Grow( i ); }
		}
	}

	/// <summary> The floor at a point if it is open sand there (no rock, plant, deck or wall on it), else null. </summary>
	static float? OpenFloor( Node3D context, Vector3 at )
	{
		float sand;
		if ( SeaTerrain.Area == SeaTerrain.Globe ) sand = Atlantis.CanalFloorY;
		else sand = SeaTerrain.HeightAt( at );
		var floor = Sculpt.FloorBelow( context, at with { Y = sand + 3f }, 6f );
		if ( MathF.Abs( floor - sand ) > 0.25f ) return null;
		// Room to stand up: nothing over the hole, and the ground nearly level.
		if ( Trace.Ray( context, at with { Y = floor + 0.2f }, at with { Y = floor + 2.2f }, Layers.World ).Hit ) return null;
		if ( SeaTerrain.Area != SeaTerrain.Globe )
			foreach ( var d in new[] { Vector3.Right, Vector3.Back, Vector3.Left, Vector3.Forward } )
				if ( MathF.Abs( SeaTerrain.HeightAt( at + d * 0.8f ) - sand ) > 0.35f ) return null;
		return floor;
	}

	/// <summary> A spot for a bed: on open sand where the fighting is, off the wreck, the spawn points, the stations, the arrival and the other beds. </summary>
	static Vector3? FindSpot( Node3D level, List<Vector3> others )
	{
		var keepOff = level.GetTree().GetNodesInGroup( "spawn_point" ).OfType<Node3D>().Select( n => (n.GlobalPosition, 8f) ).ToList();
		keepOff.AddRange( level.GetTree().GetNodesInGroup( "buy_station" ).OfType<Node3D>().Select( n => (n.GlobalPosition, 7f) ) );
		keepOff.AddRange( others.Select( o => (o, 10f) ) );
		if ( SeaTerrain.Area == 2 ) keepOff.Add( (AreaTwo.ArrivalFlat, 10f) );
		if ( SeaTerrain.Area == SeaTerrain.Globe ) keepOff.Add( (Atlantis.Arrival, 10f) );
		for ( var tries = 0; tries < 120; tries++ )
		{
			Vector3 at;
			if ( SeaTerrain.Area == SeaTerrain.Globe )
				at = Atlantis.Polar( Rng.Float() < 0.4f ? Rng.Float( 21f, 23f ) : Rng.Float( 48.5f, 51.5f ), Rng.Float( 0f, 360f ), Atlantis.CanalFloorY );
			else
			{
				var angle = Rng.Float( 0f, MathF.Tau );
				var radius = MathF.Sqrt( Rng.Float( 10f * 10f, 40f * 40f ) );
				at = new Vector3( MathF.Cos( angle ) * radius, 0f, MathF.Sin( angle ) * radius );
				if ( SeaTerrain.Area == 2 && AreaTwo.OnPlain( at, 5f ) ) continue;   // not on the wreck
				at.Y = SeaTerrain.HeightAt( at );
			}
			if ( keepOff.Any( o => o.Item1.Flat().DistanceTo( at.Flat() ) < o.Item2 ) ) continue;
			if ( OpenFloor( level, at ) is not { } floor ) continue;
			return at with { Y = floor };
		}
		return null;
	}
}
