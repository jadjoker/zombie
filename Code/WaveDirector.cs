using Sandbox.Citizen;

public enum WaveState
{
	Intermission,
	Fighting,
	GameOver,
}

/// <summary>
/// Runs the survival loop: a short break, then a wave of zombies, each wave bigger and tougher.
/// Put one in the scene. Zombies are built in code unless a prefab is assigned.
///
/// Networking: the host runs the waves and spawns (networked) zombies. The state the HUD shows is
/// synced; banners, bonuses and respawns are sent to each player. Restarting doesn't reload the scene
/// (that would drop everyone), it clears the arena and brings every player back.
/// </summary>
public sealed class WaveDirector : Component
{
	[Property, Group( "Flow" )] public float FirstWaveDelay { get; set; } = 5f;
	[Property, Group( "Flow" )] public float IntermissionSeconds { get; set; } = 10f;
	/// <summary> Points per surviving player when a wave is cleared, times the wave number. </summary>
	[Property, Group( "Flow" )] public int WaveClearBonus { get; set; } = 50;

	[Property, Group( "Wave size" )] public int BaseZombies { get; set; } = 6;
	[Property, Group( "Wave size" )] public int ZombiesPerWave { get; set; } = 4;
	[Property, Group( "Wave size" )] public int MaxAlive { get; set; } = 24;
	[Property, Group( "Wave size" )] public float SpawnInterval { get; set; } = 1.0f;

	[Property, Group( "Zombie stats" )] public float BaseHealth { get; set; } = 70f;
	[Property, Group( "Zombie stats" )] public float HealthPerWave { get; set; } = 20f;
	[Property, Group( "Zombie stats" )] public float BaseSpeed { get; set; } = 110f;
	[Property, Group( "Zombie stats" )] public float SpeedPerWave { get; set; } = 10f;
	[Property, Group( "Zombie stats" )] public float TopSpeed { get; set; } = 280f;

	[Property, Group( "Spawning" )] public GameObject ZombiePrefab { get; set; }
	/// <summary> Mesh worn over the animated citizen. Needs the citizen skeleton. Empty = green citizen. </summary>
	[Property, Group( "Spawning" )] public Model ZombieModel { get; set; }
	[Property, Group( "Spawning" )] public float MinSpawnDistance { get; set; } = 700f;
	[Property, Group( "Spawning" )] public float MaxSpawnDistance { get; set; } = 1600f;

	/// <summary> Chance each spawn is a Bloober instead of a zombie, once Bloobers have started. </summary>
	[Property, Group( "Bloober" )] public float BlooberChance { get; set; } = 0.4f;
	[Property, Group( "Bloober" )] public int BlooberFromWave { get; set; } = 1;
	[Property, Group( "Bloober" )] public float BlooberBaseHealth { get; set; } = 160f;
	[Property, Group( "Bloober" )] public float BlooberHealthPerWave { get; set; } = 25f;
	/// <summary> How high above the spawn point it appears. </summary>
	[Property, Group( "Bloober" )] public float BlooberSpawnHeight { get; set; } = 90f;

	/// <summary> Chance each spawn is a Squilien, once they've started. </summary>
	[Property, Group( "Squilien" )] public float SquilienChance { get; set; } = 0.25f;
	[Property, Group( "Squilien" )] public int SquilienFromWave { get; set; } = 1;
	[Property, Group( "Squilien" )] public float SquilienBaseHealth { get; set; } = 130f;
	[Property, Group( "Squilien" )] public float SquilienHealthPerWave { get; set; } = 20f;
	[Property, Group( "Squilien" )] public float SquilienSpawnHeight { get; set; } = 170f;

	/// <summary> Chance each spawn is a Junk Crab (ground creature), once they've started. </summary>
	[Property, Group( "Junk Crab" )] public float CrabChance { get; set; } = 0.3f;
	[Property, Group( "Junk Crab" )] public int CrabFromWave { get; set; } = 1;
	[Property, Group( "Junk Crab" )] public float CrabBaseHealth { get; set; } = 130f;
	[Property, Group( "Junk Crab" )] public float CrabHealthPerWave { get; set; } = 15f;

	/// <summary> MC61 turns up every this many waves (0 = never). </summary>
	[Property, Group( "Boss" )] public int BossEvery { get; set; } = 5;
	[Property, Group( "Boss" )] public float BossBaseHealth { get; set; } = 2000f;
	/// <summary> Extra health each time he comes back. </summary>
	[Property, Group( "Boss" )] public float BossHealthPerVisit { get; set; } = 800f;
	[Property, Group( "Boss" )] public float BossSpawnHeight { get; set; } = 160f;

	/// <summary> Every this many waves the anchor snaps: everyone floats, and only floating creatures come (0 = never). </summary>
	[Property, Group( "Anchor" )] public int AnchorSnapEvery { get; set; } = 4;
	/// <summary> The anchor is off this wave: players swim in 3D. </summary>
	[Sync] public bool FloatRound { get; set; }

	// ---- Open world: no waves. Danger climbs as players push away from the start and uncover new ground. ----

	/// <summary> On: enemies trickle in all the time and danger (shown as the "wave" number) rises with exploration. </summary>
	[Property, Group( "Open world" )] public bool OpenWorld { get; set; }
	/// <summary> Side of one square of ground that counts as "explored" when someone steps into it. </summary>
	[Property, Group( "Open world" )] public float CellSize { get; set; } = 500f;
	/// <summary> Distance from the start that adds one danger level. </summary>
	[Property, Group( "Open world" )] public float DistancePerLevel { get; set; } = 1500f;
	/// <summary> New squares explored that add one danger level. </summary>
	[Property, Group( "Open world" )] public int CellsPerLevel { get; set; } = 5;
	/// <summary> Enemies kept around at danger 1, plus more per level (capped by MaxAlive). </summary>
	[Property, Group( "Open world" )] public int BaseAlive { get; set; } = 4;
	[Property, Group( "Open world" )] public float AlivePerLevel { get; set; } = 1.5f;
	/// <summary> Stepping into fresh ground has this chance of spooking up a few extra enemies. </summary>
	[Property, Group( "Open world" )] public float AmbushChance { get; set; } = 0.35f;
	/// <summary> Enemies left this far behind every player are quietly removed so the count stays near the action. </summary>
	[Property, Group( "Open world" )] public float DespawnDistance { get; set; } = 3200f;
	/// <summary> A fallen player comes back this long after dying, as long as a teammate is alive. </summary>
	[Property, Group( "Open world" )] public float TeammateRespawnSeconds { get; set; } = 20f;

	[Sync] public int CellsExplored { get; set; }
	[Sync] public float FarthestDistance { get; set; }

	readonly HashSet<(int, int)> visitedCells = new();
	bool originSet;
	Vector3 origin;
	int lastDanger;
#pragma warning disable SB3003 // Host-only timers.
	TimeSince deadTimer;
#pragma warning restore SB3003

	(int, int) CellOf( Vector3 p ) => ((int)MathF.Floor( p.x / CellSize ), (int)MathF.Floor( p.y / CellSize ));

	void UpdateOpenWorld()
	{
		State = WaveState.Fighting;
		FloatRound = false;
		BossWave = false;
		bossPending = false;
		toSpawn = 0;

		var players = Scene.GetAllComponents<PlayerSurvivor>().Where( p => p.IsAlive ).ToList();
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
			if ( visitedCells.Add( CellOf( p.WorldPosition ) ) && Game.Random.Float() < AmbushChance )
				ambush += Game.Random.Int( 1, 2 );
			FarthestDistance = MathF.Max( FarthestDistance, (p.WorldPosition - origin).WithZ( 0 ).Length );
		}
		CellsExplored = visitedCells.Count;

		Wave = 1 + (int)(FarthestDistance / MathF.Max( 1f, DistancePerLevel )) + (CellsExplored - 1) / Math.Max( 1, CellsPerLevel );
		if ( Wave > lastDanger )
		{
			if ( lastDanger > 0 ) WaveStartedEverywhere(); // banner and sting when the danger steps up
			lastDanger = Wave;
		}

		// Drop stragglers left far behind everyone.
		if ( players.Count > 0 )
		{
			foreach ( var go in alive.ToList() )
			{
				if ( !go.IsValid() ) continue;
				var nearest = players.Min( p => p.WorldPosition.Distance( go.WorldPosition ) );
				if ( nearest > DespawnDistance ) { alive.Remove( go ); go.Destroy(); }
			}
		}

		// Keep the count at what this much danger calls for; fresh ground can add a quick ambush on top.
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

		// Fallen players come back beside a living teammate.
		var allPlayers = Scene.GetAllComponents<PlayerSurvivor>().ToList();
		var anyDead = allPlayers.Any( p => !p.IsAlive );
		if ( !anyDead ) deadTimer = 0f;
		else if ( players.Count > 0 && deadTimer >= TeammateRespawnSeconds )
		{
			var index = 0;
			foreach ( var p in allPlayers.Where( q => !q.IsAlive ) )
				p.Respawn( players[0].WorldPosition + Vector3.Right * (60f + 40f * index++), fullReset: false );
			deadTimer = 0f;
		}
	}

	/// <summary> This wave has MC61 in it. </summary>
	[Sync] public bool BossWave { get; set; }
	bool bossPending;

	[Property, Group( "Juice" )] public SoundEvent ZombieVoice { get; set; }
	[Property, Group( "Juice" )] public SoundEvent ZombieAttackSound { get; set; }
	[Property, Group( "Juice" )] public SoundEvent ZombieDeathSound { get; set; }
	[Property, Group( "Juice" )] public GameObject ZombieDeathEffect { get; set; }
	[Property, Group( "Juice" )] public SoundEvent WaveStartSound { get; set; }
	[Property, Group( "Juice" )] public SoundEvent WaveClearedSound { get; set; }

	public TimeSince TimeSinceWaveStarted { get; private set; } = 999f;
	public TimeSince TimeSinceWaveCleared { get; private set; } = 999f;

	[Sync] public WaveState State { get; set; } = WaveState.Intermission;
	[Sync] public int Wave { get; set; }
	[Sync] public int Kills { get; set; }
	public int AliveCount => alive.Count;
	/// <summary> Updated by the host every frame, synced for everyone's HUD. </summary>
	[Sync] public int ZombiesRemaining { get; set; }
	[Sync] public float SecondsUntilNextWave { get; set; }

	// ---- Dev toggles (driven by the in-game dev menu) ------------------------

	/// <summary> Master switch: pause the director's own spawning (it still fights). </summary>
	[Property, Group( "Dev" )] public bool SpawningPaused { get; set; }
	[Property, Group( "Dev" )] public bool SpawnZombies { get; set; } = true;
	[Property, Group( "Dev" )] public bool SpawnBloobers { get; set; } = true;
	[Property, Group( "Dev" )] public bool SpawnSquiliens { get; set; } = true;
	[Property, Group( "Dev" )] public bool SpawnCrabs { get; set; } = true;
	[Property, Group( "Dev" )] public bool SpawnBossWaves { get; set; } = true;

	/// <summary> Enemy types the dev menu can spawn on demand. </summary>
	public enum EnemyKind { Zombie, Bloober, Squilien, Crab, Boss }

	/// <summary> Dev tool: spawn one enemy of a type near the players. False if it had nowhere to go. </summary>
	public bool Spawn( EnemyKind kind ) => kind switch
	{
		EnemyKind.Zombie => TrySpawnZombie(),
		EnemyKind.Bloober => TrySpawnBloober(),
		EnemyKind.Squilien => TrySpawnSquilien(),
		EnemyKind.Crab => TrySpawnCrab(),
		EnemyKind.Boss => SpawnBossWaves && TrySpawnBoss(),
		_ => false,
	};

	/// <summary> Dev tool: kill everything alive right now (their deaths play out). </summary>
	public void ClearEnemies()
	{
		foreach ( var go in alive.ToList() )
		{
			if ( !go.IsValid() ) continue;
			var health = go.GetComponent<Health>();
			if ( health.IsValid() ) health.OnDamage( new DamageInfo { Damage = 1_000_000f } );
			else go.Destroy();
		}
	}

	/// <summary> Dev tool: end the current wave now (stop spawning, kill what's left). </summary>
	public void SkipWave()
	{
		toSpawn = 0;
		bossPending = false;
		ClearEnemies();
	}

	/// <summary> Every enemy still alive this wave (zombies and creatures). </summary>
	readonly List<GameObject> alive = new();
	int toSpawn;
#pragma warning disable SB3003 // Host-only wave timers; a reset on host migration is harmless.
	TimeUntil nextWaveAt;
	TimeSince timeSinceSpawn;
#pragma warning restore SB3003

	protected override void OnStart()
	{
		nextWaveAt = FirstWaveDelay;
	}

	protected override void OnUpdate()
	{
		// Anyone can ask for a restart once everybody is dead.
		if ( State == WaveState.GameOver && Input.Pressed( "Jump" ) )
			RequestRestart();

		if ( IsProxy ) return;

		alive.RemoveAll( z => !z.IsValid() );
		ZombiesRemaining = toSpawn + alive.Count;
		SecondsUntilNextWave = MathF.Max( 0f, nextWaveAt.Relative );

		if ( State == WaveState.GameOver ) return;

		var anyPlayers = Scene.GetAllComponents<PlayerSurvivor>().Any();
		if ( anyPlayers && PlayerSurvivor.FindNearestAlive( Scene, WorldPosition ) is null )
		{
			State = WaveState.GameOver;
			return;
		}

		if ( OpenWorld )
		{
			UpdateOpenWorld();
			return;
		}

		if ( State == WaveState.Intermission )
		{
			if ( nextWaveAt ) StartWave();
			return;
		}

		if ( bossPending )
		{
			if ( !SpawnBossWaves ) bossPending = false; // dev disabled boss waves: drop the pending boss
			else if ( !SpawningPaused && TrySpawnBoss() ) bossPending = false;
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
			FloatRound = false; // anchor back on: everyone sinks to the floor
			WaveClearedEverywhere();

			// Survivors get the bonus; anyone who died comes back for the next wave.
			var index = 0;
			foreach ( var survivor in Scene.GetAllComponents<PlayerSurvivor>().ToList() )
			{
				if ( survivor.IsAlive ) survivor.RewardPoints( WaveClearBonus * Wave );
				else survivor.Respawn( RespawnPosition( Scene, index ), fullReset: false );
				index++;
			}
		}
	}

	[Rpc.Broadcast]
	void WaveStartedEverywhere()
	{
		TimeSinceWaveStarted = 0f;
		PlayToPlayer( WaveStartSound );
	}

	[Rpc.Broadcast]
	void WaveClearedEverywhere()
	{
		TimeSinceWaveCleared = 0f;
		PlayToPlayer( WaveClearedSound );
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
		WaveStartedEverywhere();
	}

	void PlayToPlayer( SoundEvent sound )
	{
		if ( sound is null ) return;
		var camera = Scene.Camera;
		Sound.Play( sound, camera.IsValid() ? camera.WorldPosition : WorldPosition );
	}

	[Rpc.Host]
	void RequestRestart() => Restart();

	/// <summary>
	/// Starts over without reloading the scene: clears zombies and gore, resets the waves,
	/// and brings every player back with fresh points and upgrades.
	/// </summary>
	public void Restart()
	{
		if ( IsProxy || State != WaveState.GameOver ) return;

		foreach ( var z in Scene.GetAllComponents<Zombie>().ToList() )
			z.GameObject.Destroy();
		foreach ( var b in Scene.GetAllComponents<Bloober>().ToList() )
			b.GameObject.Destroy();
		foreach ( var q in Scene.GetAllComponents<Squilien>().ToList() )
			q.GameObject.Destroy();
		foreach ( var m in Scene.GetAllComponents<MC61>().ToList() )
			m.GameObject.Destroy();
		foreach ( var c in Scene.GetAllComponents<JunkCrab>().ToList() )
			c.GameObject.Destroy();
		alive.Clear();

		Wave = 0;
		Kills = 0;
		originSet = false;
		visitedCells.Clear();
		CellsExplored = 0;
		FarthestDistance = 0f;
		toSpawn = 0;
		bossPending = false;
		BossWave = false;
		FloatRound = false;
		State = WaveState.Intermission;
		nextWaveAt = FirstWaveDelay;
		CleanupEverywhere();

		var index = 0;
		foreach ( var survivor in Scene.GetAllComponents<PlayerSurvivor>().ToList() )
			survivor.Respawn( RespawnPosition( Scene, index++ ), fullReset: true );
	}

	/// <summary> Where the Nth survivor comes back: the lobby's spawn centre when there is one, else the origin. </summary>
	static Vector3 RespawnPosition( Scene scene, int index )
	{
		var coop = scene.Get<CoopNetwork>();
		return coop.IsValid() ? coop.SpawnPosition( index ) : CoopNetwork.SpawnPosition( Vector3.Zero, index, 90f );
	}

	/// <summary> Gibs, meat and blood are local to each machine, so everyone clears their own. </summary>
	[Rpc.Broadcast]
	void CleanupEverywhere()
	{
		foreach ( var gib in Scene.GetAllComponents<Gib>().ToList() ) gib.GameObject.Destroy();
		foreach ( var go in Scene.GetAllObjects( true ).Where( g => g.Name == "Blood" ).ToList() ) go.Destroy();
	}

	bool TrySpawnEnemy()
	{
		// One roll shared out between the enabled creatures; whatever is left over is a zombie.
		var roll = Game.Random.Float();
		var squilien = SpawnSquiliens && Wave >= SquilienFromWave ? SquilienChance : 0f;
		var bloober = SpawnBloobers && Wave >= BlooberFromWave ? BlooberChance : 0f;
		// Zombies can't swim: an anchor-snapped round is all floaters, in the same proportions.
		if ( FloatRound )
		{
			var total = MathF.Max( 0.01f, squilien + bloober );
			if ( squilien + bloober <= 0.01f )
			{
				if ( !SpawnSquiliens && !SpawnBloobers ) return false; // nothing can spawn in a float round
				squilien = SpawnSquiliens ? 0.4f : 0f;
				bloober = SpawnBloobers ? 0.6f : 0f;
				total = squilien + bloober;
			}
			squilien /= total;
			bloober /= total;
		}
		var crab = SpawnCrabs && Wave >= CrabFromWave && !FloatRound ? CrabChance : 0f;
		if ( roll < squilien ) return TrySpawnSquilien();
		if ( roll < squilien + bloober ) return TrySpawnBloober();
		if ( roll < squilien + bloober + crab ) return TrySpawnCrab();
		return SpawnZombies && TrySpawnZombie();
	}

	bool TrySpawnCrab()
	{
		if ( !TryFindSpawnPosition( out var position ) ) return false;

		var size = Game.Random.Float( 0.9f, 1.25f );
		var go = new GameObject( true, "Junk Crab" );
		go.WorldPosition = position;
		go.WorldRotation = Rotation.FromYaw( Game.Random.Float( 0f, 360f ) );

		// Body-blocking capsule on a child, tagged so bullets skip it and hit the parts instead.
		var blocker = new GameObject( go, true, "Blocker" );
		blocker.Tags.Add( "zombieblocker" );
		var capsule = blocker.AddComponent<CapsuleCollider>();
		capsule.Start = Vector3.Up * 12f * size;
		capsule.End = Vector3.Up * 20f * size;
		capsule.Radius = 18f * size;

		var agent = go.AddComponent<NavMeshAgent>();
		agent.Height = 30f * size;
		agent.Radius = 20f * size;
		agent.Acceleration = 900f;

		go.AddComponent<Health>().ResetTo( CrabBaseHealth + CrabHealthPerWave * (Wave - 1) );
		var crab = go.AddComponent<JunkCrab>();
		crab.Hue = Game.Random.Float();
		crab.Size = size;
		crab.MoveSpeed = MathF.Min( TopSpeed, 200f + SpeedPerWave * (Wave - 1) );
		crab.Killed += c =>
		{
			alive.Remove( c.GameObject );
			Kills++;
		};
		alive.Add( go );

		if ( Networking.IsActive ) go.NetworkSpawn();
		return true;
	}

	bool TrySpawnBoss()
	{
		if ( !TryFindSpawnPosition( out var position ) ) return false;

		var visit = BossEvery > 0 ? Wave / BossEvery - 1 : 0;
		var go = new GameObject( true, "MC61" );
		go.WorldPosition = position + Vector3.Up * BossSpawnHeight;
		go.WorldRotation = Rotation.FromYaw( Game.Random.Float( 0f, 360f ) );
		go.AddComponent<Health>().ResetTo( BossBaseHealth + BossHealthPerVisit * visit );
		var boss = go.AddComponent<MC61>();
		boss.Killed += b =>
		{
			alive.Remove( b.GameObject );
			Kills++;
		};
		alive.Add( go );

		if ( Networking.IsActive ) go.NetworkSpawn();
		return true;
	}

	bool TrySpawnSquilien()
	{
		if ( !TryFindSpawnPosition( out var position ) ) return false;

		var go = new GameObject( true, "Squilien" );
		go.WorldPosition = position + Vector3.Up * SquilienSpawnHeight;
		go.WorldRotation = Rotation.FromYaw( Game.Random.Float( 0f, 360f ) );

		go.AddComponent<Health>().ResetTo( SquilienBaseHealth + SquilienHealthPerWave * (Wave - 1) );
		var squilien = go.AddComponent<Squilien>();
		squilien.Hue = Game.Random.Float();
		squilien.Killed += s =>
		{
			alive.Remove( s.GameObject );
			Kills++;
		};
		alive.Add( go );

		if ( Networking.IsActive ) go.NetworkSpawn();
		return true;
	}

	bool TrySpawnBloober()
	{
		if ( !TryFindSpawnPosition( out var position ) ) return false;

		var go = new GameObject( true, "Bloober" );
		go.WorldPosition = position + Vector3.Up * BlooberSpawnHeight;
		go.WorldRotation = Rotation.FromYaw( Game.Random.Float( 0f, 360f ) );
		go.Tags.Add( "creature" );

		go.AddComponent<Health>().ResetTo( BlooberBaseHealth + BlooberHealthPerWave * (Wave - 1) );
		var bloober = go.AddComponent<Bloober>();
		// Picked here so it goes out with the network spawn and everyone builds the same colours and shape.
		bloober.Hue = Game.Random.Float();
		bloober.Killed += b =>
		{
			alive.Remove( b.GameObject );
			Kills++;
		};
		alive.Add( go );

		if ( Networking.IsActive ) go.NetworkSpawn();
		return true;
	}

	bool TrySpawnZombie()
	{
		if ( !TryFindSpawnPosition( out var position ) ) return false;

		var zombieObject = ZombiePrefab.IsValid()
			? ZombiePrefab.Clone( position )
			: BuildZombie( position );

		zombieObject.WorldRotation = Rotation.FromYaw( Game.Random.Float( 0f, 360f ) );

		var zombie = zombieObject.GetComponent<Zombie>();
		if ( !zombie.IsValid() )
		{
			Log.Warning( "Zombie prefab has no Zombie component" );
			zombieObject.Destroy();
			return false;
		}

		zombie.MoveSpeed = MathF.Min( TopSpeed, BaseSpeed + SpeedPerWave * (Wave - 1) );

		// A prefab can bring its own sounds; fill in whatever it leaves empty.
		zombie.VoiceSound ??= ZombieVoice;
		zombie.AttackSound ??= ZombieAttackSound;
		zombie.DeathSound ??= ZombieDeathSound;
		if ( !zombie.DeathEffect.IsValid() ) zombie.DeathEffect = ZombieDeathEffect;
		zombieObject.GetComponent<Health>()?.ResetTo( BaseHealth + HealthPerWave * (Wave - 1) );

		zombie.Killed += OnZombieKilled;
		alive.Add( zombieObject );

		// Everyone needs to see it: network it once it's fully set up, owned by the host.
		if ( Networking.IsActive ) zombieObject.NetworkSpawn();
		return true;
	}

	void OnZombieKilled( Zombie zombie )
	{
		alive.Remove( zombie.GameObject );
		Kills++;
	}

	bool TryFindSpawnPosition( out Vector3 position )
	{
		var player = PlayerSurvivor.FindNearestAlive( Scene, WorldPosition );
		var center = player?.WorldPosition ?? WorldPosition;

		// Prefer hand-placed spawn points that aren't right next to a player (in the open world, only ones in a sensible band).
		var points = Scene.GetAllComponents<ZombieSpawnPoint>()
			.Where( p => p.WorldPosition.Distance( center ) >= MinSpawnDistance && (!OpenWorld || p.WorldPosition.Distance( center ) <= MaxSpawnDistance) )
			.ToList();

		if ( points.Count > 0 )
		{
			position = Game.Random.FromList( points ).WorldPosition;
			return true;
		}

		// Otherwise pick a random walkable spot in a ring around the player.
		for ( var i = 0; i < 12; i++ )
		{
			var point = Scene.NavMesh.GetRandomPoint( center, MaxSpawnDistance );
			if ( point is Vector3 p && p.Distance( center ) >= MinSpawnDistance )
			{
				position = p;
				return true;
			}
		}

		position = default;
		return false;
	}

	/// <summary>
	/// Builds a zombie in code. The citizen model underneath does the animating and ragdolling;
	/// with ZombieModel set, its body is hidden and the zombie mesh is bone-merged on top.
	/// </summary>
	GameObject BuildZombie( Vector3 position )
	{
		var go = new GameObject( true, "Zombie" );
		go.WorldPosition = position;
		go.WorldScale = Game.Random.Float( 0.94f, 1.08f );
		go.Tags.Add( "zombie" );

		var renderer = go.AddComponent<SkinnedModelRenderer>();
		renderer.Model = Model.Load( "models/citizen/citizen.vmdl" );

		if ( ZombieModel is not null && !ZombieModel.IsError )
		{
			// Choice 1 of each citizen body part is empty, which hides it.
			foreach ( var part in new[] { "Head", "Chest", "Legs", "Hands", "Feet" } )
				renderer.SetBodyGroup( part, 1 );

			var mesh = new GameObject( go, true, "Mesh" );
			var meshRenderer = mesh.AddComponent<SkinnedModelRenderer>();
			meshRenderer.Model = ZombieModel;
			meshRenderer.BoneMergeTarget = renderer;

			// Slight per-zombie skin variation so a crowd isn't copy-pasted.
			var shade = Game.Random.Float( 0.75f, 1f );
			meshRenderer.Tint = new Color( shade, shade * Game.Random.Float( 0.92f, 1.05f ), shade * 0.95f );
		}
		else
		{
			renderer.Tint = new Color( 0.45f, 0.75f, 0.35f );
		}

		// Movement/blocking capsule on its own child, tagged so bullets skip it and hit the hitboxes instead.
		var blocker = new GameObject( go, true, "Blocker" );
		blocker.Tags.Add( "zombieblocker" );
		var collider = blocker.AddComponent<CapsuleCollider>();
		collider.Start = Vector3.Up * 14f;
		collider.End = Vector3.Up * 62f;
		collider.Radius = 13f;

		// Per-bone hitboxes from the citizen skeleton, so shots know which body part they hit.
		var hitboxes = go.AddComponent<ModelHitboxes>();
		hitboxes.Renderer = renderer;
		hitboxes.Target = go;

		go.AddComponent<ZombieGore>();

		var agent = go.AddComponent<NavMeshAgent>();
		agent.Height = 70f;
		agent.Radius = 14f;
		agent.Acceleration = 600f;

		go.AddComponent<Health>();

		var anim = go.AddComponent<CitizenAnimationHelper>();
		anim.Target = renderer;

		go.AddComponent<Zombie>();
		return go;
	}
}
