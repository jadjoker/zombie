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
	[Property, Group( "Bloober" )] public float BlooberBaseHealth { get; set; } = 120f;
	[Property, Group( "Bloober" )] public float BlooberHealthPerWave { get; set; } = 25f;
	/// <summary> How high above the spawn point it appears. </summary>
	[Property, Group( "Bloober" )] public float BlooberSpawnHeight { get; set; } = 90f;

	/// <summary> Chance each spawn is a Squilien, once they've started. </summary>
	[Property, Group( "Squilien" )] public float SquilienChance { get; set; } = 0.25f;
	[Property, Group( "Squilien" )] public int SquilienFromWave { get; set; } = 1;
	[Property, Group( "Squilien" )] public float SquilienBaseHealth { get; set; } = 90f;
	[Property, Group( "Squilien" )] public float SquilienHealthPerWave { get; set; } = 20f;
	[Property, Group( "Squilien" )] public float SquilienSpawnHeight { get; set; } = 170f;

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

	/// <summary> Every enemy still alive this wave (zombies and creatures). </summary>
	readonly List<GameObject> alive = new();
	int toSpawn;
	TimeUntil nextWaveAt;
	TimeSince timeSinceSpawn;

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

		if ( State == WaveState.Intermission )
		{
			if ( nextWaveAt ) StartWave();
			return;
		}

		if ( toSpawn > 0 && alive.Count < MaxAlive && timeSinceSpawn >= SpawnInterval )
		{
			if ( TrySpawnEnemy() ) toSpawn--;
			timeSinceSpawn = 0f;
		}

		if ( toSpawn <= 0 && alive.Count == 0 )
		{
			State = WaveState.Intermission;
			nextWaveAt = IntermissionSeconds;
			WaveClearedEverywhere();

			// Survivors get the bonus; anyone who died comes back for the next wave.
			var index = 0;
			foreach ( var survivor in Scene.GetAllComponents<PlayerSurvivor>().ToList() )
			{
				if ( survivor.IsAlive ) survivor.RewardPoints( WaveClearBonus * Wave );
				else survivor.Respawn( CoopNetwork.SpawnPosition( Scene, index ), fullReset: false );
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
		alive.Clear();

		Wave = 0;
		Kills = 0;
		toSpawn = 0;
		State = WaveState.Intermission;
		nextWaveAt = FirstWaveDelay;
		CleanupEverywhere();

		var index = 0;
		foreach ( var survivor in Scene.GetAllComponents<PlayerSurvivor>().ToList() )
			survivor.Respawn( CoopNetwork.SpawnPosition( Scene, index++ ), fullReset: true );
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
		// One roll shared out between the creatures; whatever is left over is a zombie.
		var roll = Game.Random.Float();
		var squilien = Wave >= SquilienFromWave ? SquilienChance : 0f;
		var bloober = Wave >= BlooberFromWave ? BlooberChance : 0f;
		if ( roll < squilien ) return TrySpawnSquilien();
		if ( roll < squilien + bloober ) return TrySpawnBloober();
		return TrySpawnZombie();
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

		// Prefer hand-placed spawn points that aren't right next to a player.
		var points = Scene.GetAllComponents<ZombieSpawnPoint>()
			.Where( p => p.WorldPosition.Distance( center ) >= MinSpawnDistance )
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
