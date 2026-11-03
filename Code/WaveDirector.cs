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

	[Property, Group( "Juice" )] public SoundEvent ZombieVoice { get; set; }
	[Property, Group( "Juice" )] public SoundEvent ZombieAttackSound { get; set; }
	[Property, Group( "Juice" )] public SoundEvent ZombieDeathSound { get; set; }
	[Property, Group( "Juice" )] public GameObject ZombieDeathEffect { get; set; }
	[Property, Group( "Juice" )] public SoundEvent WaveStartSound { get; set; }
	[Property, Group( "Juice" )] public SoundEvent WaveClearedSound { get; set; }

	public TimeSince TimeSinceWaveStarted { get; private set; } = 999f;
	public TimeSince TimeSinceWaveCleared { get; private set; } = 999f;

	public WaveState State { get; private set; } = WaveState.Intermission;
	public int Wave { get; private set; }
	public int Kills { get; private set; }
	public int AliveCount => alive.Count;
	public int ZombiesRemaining => toSpawn + alive.Count;
	public float SecondsUntilNextWave => MathF.Max( 0f, nextWaveAt.Relative );

	readonly List<Zombie> alive = new();
	int toSpawn;
	TimeUntil nextWaveAt;
	TimeSince timeSinceSpawn;

	protected override void OnStart()
	{
		nextWaveAt = FirstWaveDelay;
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		if ( State == WaveState.GameOver )
		{
			if ( Input.Pressed( "Jump" ) ) Restart();
			return;
		}

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
			if ( TrySpawnZombie() ) toSpawn--;
			timeSinceSpawn = 0f;
		}

		if ( toSpawn <= 0 && alive.Count == 0 )
		{
			State = WaveState.Intermission;
			nextWaveAt = IntermissionSeconds;
			TimeSinceWaveCleared = 0f;
			PlayToPlayer( WaveClearedSound );

			foreach ( var survivor in Scene.GetAllComponents<PlayerSurvivor>().Where( p => p.IsAlive ) )
				survivor.AddPoints( WaveClearBonus * Wave );
		}
	}

	void StartWave()
	{
		Wave++;
		toSpawn = BaseZombies + ZombiesPerWave * (Wave - 1);
		State = WaveState.Fighting;
		timeSinceSpawn = SpawnInterval;
		TimeSinceWaveStarted = 0f;
		PlayToPlayer( WaveStartSound );
	}

	void PlayToPlayer( SoundEvent sound )
	{
		if ( sound is null ) return;
		var camera = Scene.Camera;
		Sound.Play( sound, camera.IsValid() ? camera.WorldPosition : WorldPosition );
	}

	/// <summary> Reloads the current scene from scratch. </summary>
	public void Restart()
	{
		if ( Scene.Source is not null )
			Scene.Load( Scene.Source );
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
		alive.Add( zombie );
		return true;
	}

	void OnZombieKilled( Zombie zombie )
	{
		alive.Remove( zombie );
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
