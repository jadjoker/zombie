using Godot;

public enum WaveState { Intermission, Fighting, GameOver }

/// <summary>
/// Port of the s&box WaveDirector: the survival loop. A short break, then a wave of enemies, each wave bigger and
/// tougher; boss waves every few; "anchor snapped" float rounds; an open-world mode where danger climbs with time or
/// exploration; dev tools (spawn a kind, jump waves, clear). Enemy kinds are spawned through <see cref="Spawners"/>,
/// filled in as each creature is ported (a kind with no spawner just doesn't come yet). Numbers are the s&box ones.
/// (No navmesh yet: spawn spots are found by ground traces in a ring around the player.)
/// </summary>
public partial class WaveDirector : Node3D
{
	public float FirstWaveDelay = 5f;
	public float IntermissionSeconds = 10f;
	public int WaveClearBonus = 50;
	public int BaseZombies = 6;
	public int ZombiesPerWave = 4;
	public int MaxAlive = 24;
	public float SpawnInterval = 1.0f;
	public float MinSpawnDistance = 700f;
	public float MaxSpawnDistance = 1600f;

	public float BlooberChance = 0.4f;
	public int BlooberFromWave = 1;
	public float BlooberBaseHealth = 160f;
	public float BlooberHealthPerWave = 25f;
	public float BlooberSpawnHeight = 90f;

	public float SquilienChance = 0.25f;
	public int SquilienFromWave = 1;
	public float SquilienBaseHealth = 130f;
	public float SquilienHealthPerWave = 20f;
	public float SquilienSpawnHeight = 170f;

	public float CrabChance = 0.3f;
	public int CrabFromWave = 1;
	public float CrabBaseHealth = 130f;
	public float CrabHealthPerWave = 15f;

	public float SpiderChance = 0.2f;
	public int SpiderFromWave = 2;
	public float SpiderBaseHealth = 110f;
	public float SpiderHealthPerWave = 12f;

	public int BossEvery = 5;
	public float BossBaseHealth = 2000f;
	public float BossHealthPerVisit = 800f;
	public float BossSpawnHeight = 160f;

	public int AnchorSnapEvery = 4;
	public bool FloatRound;

	// Open world
	public bool OpenWorld;
	public enum DangerSource { Time, Exploration }
	public DangerSource DangerBy = DangerSource.Time;
	public float SecondsPerLevel = 90f;
	public float SurvivalSeconds;
	public float CellSize = 500f;
	public float DistancePerLevel = 1500f;
	public int CellsPerLevel = 5;
	public int BaseAlive = 4;
	public float AlivePerLevel = 1.5f;
	public float AmbushChance = 0.35f;
	public float DespawnDistance = 3200f;
	public float TeammateRespawnSeconds = 20f;
	public int CellsExplored;
	public float FarthestDistance;

	public bool BossWave;
	public WaveState State = WaveState.Intermission;
	public int Wave;
	public int Kills;
	public int ZombiesRemaining;
	public float SecondsUntilNextWave;
	public TimeSince TimeSinceWaveStarted { get; private set; } = 999f;
	public TimeSince TimeSinceWaveCleared { get; private set; } = 999f;
	public int AliveCount => alive.Count;

	// Dev toggles
	public bool SpawningPaused;
	public bool SpawnZombies;
	public bool SpawnBloobers = true;
	public bool SpawnSquiliens = true;
	public bool SpawnCrabs = true;
	public bool SpawnSpiders;
	public bool SpawnBossWaves = true;

	public enum EnemyKind { Zombie, Bloober, Squilien, Crab, Boss, Spider }

	/// <summary> How each kind is made, given a spot on the ground, its health and a facing. Null: not ported yet. </summary>
	public static readonly Dictionary<EnemyKind, Func<WaveDirector, Vector3, float, Node3D>> Spawners = new()
	{
		[EnemyKind.Bloober] = ( d, at, health ) =>
		{
			var b = Bloober.Spawn( d.GetTree().CurrentScene, at + Vector3.Up * d.BlooberSpawnHeight * U.In, health );
			b.Hue = Rng.Float();
			b.RotationDegrees = new Vector3( 0f, Rng.Float( 0f, 360f ), 0f );
			b.Killed += x => { d.alive.Remove( x ); d.Kills++; };
			return b;
		},
		[EnemyKind.Squilien] = ( d, at, health ) =>
		{
			var q = Squilien.Spawn( d.GetTree().CurrentScene, at + Vector3.Up * d.SquilienSpawnHeight * U.In, health );
			q.Hue = Rng.Float();
			q.RotationDegrees = new Vector3( 0f, Rng.Float( 0f, 360f ), 0f );
			q.Killed += x => { d.alive.Remove( x ); d.Kills++; };
			return q;
		},
		[EnemyKind.Crab] = ( d, at, health ) =>
		{
			var c = JunkCrab.Spawn( d.GetTree().CurrentScene, at, health );
			c.Hue = Rng.Float();
			c.Size = Rng.Float( 0.9f, 1.15f );
			c.RotationDegrees = new Vector3( 0f, Rng.Float( 0f, 360f ), 0f );
			c.Killed += x => { d.alive.Remove( x ); d.Kills++; };
			return c;
		},
		[EnemyKind.Boss] = ( d, at, health ) =>
		{
			var m = MC61.Spawn( d.GetTree().CurrentScene, at + Vector3.Up * d.BossSpawnHeight * U.In, health );
			m.RotationDegrees = new Vector3( 0f, Rng.Float( 0f, 360f ), 0f );
			m.Killed += x => { d.alive.Remove( x ); d.Kills++; };
			return m;
		},
		[EnemyKind.Spider] = ( d, at, health ) =>
		{
			var s = SeaSpider.Spawn( d.GetTree().CurrentScene, at, health );
			s.RotationDegrees = new Vector3( 0f, Rng.Float( 0f, 360f ), 0f );
			s.Size = Rng.Float( 0.9f, 1.1f );
			s.Killed += x => { d.alive.Remove( x ); d.Kills++; };
			return s;
		},
	};

	public static WaveDirector Current { get; private set; }

	readonly List<Node3D> alive = new();
	readonly HashSet<(int, int)> visitedCells = new();
	int toSpawn;
	bool bossPending;
	TimeUntil nextWaveAt;
	TimeSince timeSinceSpawn;
	TimeSince deadTimer;
	Vector3? forcedSpawn;
	bool originSet;
	Vector3 origin;
	int lastDanger;

	public int NextBossWave => BossEvery > 0 ? (Wave / BossEvery + 1) * BossEvery : 0;

	public float DangerProgress => DangerBy == DangerSource.Time
		? (SurvivalSeconds % MathF.Max( 1f, SecondsPerLevel )) / MathF.Max( 1f, SecondsPerLevel )
		: MathX.Clamp( ((FarthestDistance % MathF.Max( 1f, DistancePerLevel )) / MathF.Max( 1f, DistancePerLevel ) + ((CellsExplored - 1) % Math.Max( 1, CellsPerLevel )) / (float)Math.Max( 1, CellsPerLevel )) * 0.5f, 0f, 1f );

	public override void _Ready()
	{
		Name = "WaveDirector";
		Current = this;
		nextWaveAt = FirstWaveDelay;
	}

	public override void _ExitTree()
	{
		if ( Current == this ) Current = null;
	}

	public override void _Process( double delta )
	{
		if ( State == WaveState.GameOver && Input.IsActionJustPressed( "jump" ) ) Restart();

		alive.RemoveAll( n => !IsInstanceValid( n ) || (n is ICreature c && c.IsDead) );
		ZombiesRemaining = toSpawn + alive.Count;
		SecondsUntilNextWave = MathF.Max( 0f, nextWaveAt );
		if ( State == WaveState.GameOver ) return;

		if ( PlayerSurvivor.All.Count > 0 && PlayerSurvivor.FindNearestAlive( GlobalPosition ) is null )
		{
			State = WaveState.GameOver;
			return;
		}

		if ( OpenWorld ) { UpdateOpenWorld( (float)delta ); return; }

		if ( State == WaveState.Intermission )
		{
			if ( nextWaveAt <= 0f ) StartWave();
			return;
		}

		if ( bossPending )
		{
			if ( !SpawnBossWaves ) bossPending = false;
			else if ( !SpawningPaused && Spawn( EnemyKind.Boss ) ) bossPending = false;
		}

		if ( toSpawn > 0 && alive.Count < MaxAlive && timeSinceSpawn >= SpawnInterval )
		{
			if ( !SpawningPaused && TrySpawnEnemy() ) toSpawn--;
			timeSinceSpawn = 0f;
		}

		if ( toSpawn <= 0 && !bossPending && alive.Count == 0 )
		{
			State = WaveState.Intermission;
			nextWaveAt = IntermissionSeconds;
			FloatRound = false;
			TimeSinceWaveCleared = 0f;
			Sfx.Play2D( "wave_cleared" );
			var index = 0;
			foreach ( var survivor in PlayerSurvivor.All.ToList() )
			{
				if ( survivor.IsAlive ) survivor.RewardPoints( WaveClearBonus * Wave );
				else survivor.Respawn( RespawnPosition( index ), fullReset: false );
				index++;
			}
		}
	}

	(int, int) CellOf( Vector3 p ) => ((int)MathF.Floor( p.X / (CellSize * U.In) ), (int)MathF.Floor( p.Z / (CellSize * U.In) ));

	void UpdateOpenWorld( float dt )
	{
		State = WaveState.Fighting;
		FloatRound = false;
		BossWave = false;
		bossPending = false;
		toSpawn = 0;
		var players = PlayerSurvivor.All.Where( p => p.IsAlive ).ToList();
		if ( !originSet && players.Count > 0 )
		{
			originSet = true;
			origin = players[0].WorldPosition;
			visitedCells.Clear();
			visitedCells.Add( CellOf( origin ) );
			lastDanger = 0;
			Wave = 1;
			timeSinceSpawn = SpawnInterval;
		}
		if ( !originSet ) return;

		var ambush = 0;
		foreach ( var p in players )
		{
			if ( visitedCells.Add( CellOf( p.WorldPosition ) ) && Rng.Float() < AmbushChance ) ambush += Rng.Int( 1, 2 );
			FarthestDistance = MathF.Max( FarthestDistance, (p.WorldPosition - origin).Flat().Length() / U.In );
		}
		CellsExplored = visitedCells.Count;
		SurvivalSeconds += dt;
		Wave = DangerBy == DangerSource.Time
			? 1 + (int)(SurvivalSeconds / MathF.Max( 1f, SecondsPerLevel ))
			: 1 + (int)(FarthestDistance / MathF.Max( 1f, DistancePerLevel )) + (CellsExplored - 1) / Math.Max( 1, CellsPerLevel );
		if ( Wave > lastDanger )
		{
			if ( lastDanger > 0 ) TimeSinceWaveStarted = 0f;
			lastDanger = Wave;
		}

		if ( players.Count > 0 )
		{
			foreach ( var node in alive.ToList() )
			{
				if ( !IsInstanceValid( node ) ) continue;
				var nearest = players.Min( p => p.WorldPosition.DistanceTo( node.GlobalPosition ) ) / U.In;
				if ( nearest > DespawnDistance ) { alive.Remove( node ); node.QueueFree(); }
			}
		}

		var target = Math.Min( MaxAlive, BaseAlive + (int)(AlivePerLevel * (Wave - 1)) );
		if ( !SpawningPaused )
		{
			for ( var i = 0; i < ambush && alive.Count < MaxAlive; i++ ) TrySpawnEnemy();
			if ( alive.Count < target && timeSinceSpawn >= SpawnInterval )
			{
				TrySpawnEnemy();
				timeSinceSpawn = 0f;
			}
		}

		var allPlayers = PlayerSurvivor.All.ToList();
		if ( !allPlayers.Any( p => !p.IsAlive ) ) deadTimer = 0f;
		else if ( players.Count > 0 && deadTimer >= TeammateRespawnSeconds )
		{
			var index = 0;
			foreach ( var p in allPlayers.Where( q => !q.IsAlive ) )
				p.Respawn( players[0].WorldPosition + Vector3.Right * (60f + 40f * index++) * U.In, fullReset: false );
			deadTimer = 0f;
		}
	}

	void StartWave()
	{
		Wave++;
		toSpawn = BaseZombies + ZombiesPerWave * (Wave - 1);
		BossWave = BossEvery > 0 && Wave % BossEvery == 0;
		FloatRound = AnchorSnapEvery > 0 && Wave % AnchorSnapEvery == 0;
		bossPending = BossWave;
		State = WaveState.Fighting;
		timeSinceSpawn = SpawnInterval;
		TimeSinceWaveStarted = 0f;
		Sfx.Play2D( "wave_start" );
	}

	// ---- Dev tools ----

	public bool Spawn( EnemyKind kind )
	{
		if ( !Spawners.TryGetValue( kind, out var make ) || make is null ) return false;
		if ( !TryFindSpawnPosition( out var position ) ) return false;
		var health = kind switch
		{
			EnemyKind.Bloober => BlooberBaseHealth + BlooberHealthPerWave * (Wave - 1),
			EnemyKind.Squilien => SquilienBaseHealth + SquilienHealthPerWave * (Wave - 1),
			EnemyKind.Crab => CrabBaseHealth + CrabHealthPerWave * (Wave - 1),
			EnemyKind.Spider => SpiderBaseHealth + SpiderHealthPerWave * (Wave - 1),
			EnemyKind.Boss => BossBaseHealth + BossHealthPerVisit * Math.Max( 0, Wave / Math.Max( 1, BossEvery ) - 1 ),
			_ => 100f,
		};
		var node = make( this, position, health );
		if ( node is null ) return false;
		alive.Add( node );
		return true;
	}

	public Node3D SpawnAt( EnemyKind kind, Vector3 position, Vector3 faceToward )
	{
		forcedSpawn = position;
		var count = alive.Count;
		try { if ( !Spawn( kind ) ) return null; }
		finally { forcedSpawn = null; }
		var node = alive.Count > count ? alive[^1] : null;
		var look = (faceToward - position).Flat();
		if ( node is not null && look != Vector3.Zero ) node.GlobalBasis = Shapes.LookAt( look );
		return node;
	}

	public void ClearEnemies()
	{
		foreach ( var node in alive.ToList() )
		{
			if ( !IsInstanceValid( node ) ) continue;
			var health = node.GetNodeOrNull<Health>( "Health" );
			if ( health is not null ) health.OnDamage( new DamageInfo { Damage = 1_000_000f } );
			else node.QueueFree();
		}
	}

	public void SkipWave()
	{
		toSpawn = 0;
		bossPending = false;
		ClearEnemies();
	}

	public void JumpToWave( int wave )
	{
		if ( OpenWorld ) return;
		SkipWave();
		Wave = Math.Max( 0, wave - 1 );
		State = WaveState.Intermission;
		nextWaveAt = 0.1f;
	}

	public void Restart()
	{
		if ( State != WaveState.GameOver ) return;
		foreach ( var node in alive.ToList() ) if ( IsInstanceValid( node ) ) node.QueueFree();
		foreach ( var b in Bloober.All.ToList() ) b.QueueFree();
		foreach ( var q in Squilien.All.ToList() ) q.QueueFree();
		foreach ( var c in JunkCrab.All.ToList() ) c.QueueFree();
		foreach ( var m in MC61.All.ToList() ) m.QueueFree();
		alive.Clear();
		Wave = 0;
		Kills = 0;
		originSet = false;
		visitedCells.Clear();
		CellsExplored = 0;
		FarthestDistance = 0f;
		SurvivalSeconds = 0f;
		toSpawn = 0;
		bossPending = false;
		BossWave = false;
		FloatRound = false;
		State = WaveState.Intermission;
		nextWaveAt = FirstWaveDelay;
		foreach ( var gib in GetTree().CurrentScene.FindChildren( "*", "RigidBody3D", true, false ).OfType<Gib>().ToList() ) gib.QueueFree();
		var index = 0;
		foreach ( var survivor in PlayerSurvivor.All.ToList() ) survivor.Respawn( RespawnPosition( index++ ), fullReset: true );
	}

	/// <summary> Where the Nth survivor comes back: around the director's spot, 90 units apart. </summary>
	Vector3 RespawnPosition( int index ) => GlobalPosition + new Vector3( (index % 4) * 90f, 0f, (index / 4) * 90f ) * U.In;

	bool TrySpawnEnemy()
	{
		var roll = Rng.Float();
		var has = ( EnemyKind k ) => Spawners.TryGetValue( k, out var f ) && f is not null;
		var squilien = SpawnSquiliens && has( EnemyKind.Squilien ) && Wave >= SquilienFromWave ? SquilienChance : 0f;
		var bloober = SpawnBloobers && has( EnemyKind.Bloober ) && Wave >= BlooberFromWave ? BlooberChance : 0f;
		if ( FloatRound )
		{
			var total = MathF.Max( 0.01f, squilien + bloober );
			if ( squilien + bloober <= 0.01f ) return false;
			squilien /= total;
			bloober /= total;
		}
		var crab = SpawnCrabs && has( EnemyKind.Crab ) && Wave >= CrabFromWave && !FloatRound ? CrabChance : 0f;
		var spider = SpawnSpiders && has( EnemyKind.Spider ) && Wave >= SpiderFromWave && !FloatRound ? SpiderChance : 0f;
		if ( !SpawnZombies || !has( EnemyKind.Zombie ) )
		{
			if ( squilien + bloober + crab + spider <= 0.01f )
			{
				squilien = SpawnSquiliens && has( EnemyKind.Squilien ) ? 1f : 0f;
				bloober = SpawnBloobers && has( EnemyKind.Bloober ) ? 1f : 0f;
				crab = SpawnCrabs && has( EnemyKind.Crab ) && !FloatRound ? 1f : 0f;
				spider = SpawnSpiders && has( EnemyKind.Spider ) && !FloatRound ? 1f : 0f;
			}
			var sum = squilien + bloober + crab + spider;
			if ( sum <= 0.01f ) return false;
			squilien /= sum; bloober /= sum; crab /= sum; spider /= sum;
		}
		if ( roll < squilien ) return Spawn( EnemyKind.Squilien );
		if ( roll < squilien + bloober ) return Spawn( EnemyKind.Bloober );
		if ( roll < squilien + bloober + crab ) return Spawn( EnemyKind.Crab );
		if ( roll < squilien + bloober + crab + spider ) return Spawn( EnemyKind.Spider );
		return SpawnZombies && Spawn( EnemyKind.Zombie );
	}

	bool TryFindSpawnPosition( out Vector3 position )
	{
		if ( forcedSpawn is { } forced ) { position = forced; return true; }
		var player = PlayerSurvivor.FindNearestAlive( GlobalPosition );
		var center = player?.WorldPosition ?? GlobalPosition;

		// Hand-placed spawn points first (nodes in the "spawn_point" group), away from the player.
		var points = GetTree().GetNodesInGroup( "spawn_point" ).OfType<Node3D>()
			.Where( p => p.GlobalPosition.DistanceTo( center ) / U.In >= MinSpawnDistance && (!OpenWorld || p.GlobalPosition.DistanceTo( center ) / U.In <= MaxSpawnDistance) )
			.ToList();
		if ( points.Count > 0 ) { position = points[Rng.Int( 0, points.Count - 1 )].GlobalPosition; return true; }

		// Otherwise a spot on the ground in a ring around the player.
		for ( var i = 0; i < 12; i++ )
		{
			var angle = Rng.Float( 0f, MathF.Tau );
			var distance = Rng.Float( MinSpawnDistance, MaxSpawnDistance ) * U.In;
			var at = center + new Vector3( MathF.Cos( angle ), 0f, MathF.Sin( angle ) ) * distance;
			var down = Trace.Ray( this, at + Vector3.Up * 600f * U.In, at + Vector3.Down * 1200f * U.In, Layers.World );
			if ( down.Hit && down.Normal.Y > 0.6f ) { position = down.HitPosition; return true; }
		}
		position = default;
		return false;
	}
}
