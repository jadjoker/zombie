using Sandbox.Network;

/// <summary>
/// Hosts a co-op lobby when the game starts and gives everyone who joins (the host included)
/// their own player, cloned from a disabled template in the scene. Falls back to a solo player
/// if no lobby comes up (offline, or networking unavailable).
/// Sits on the Game object.
/// </summary>
public sealed class CoopNetwork : Component, Component.INetworkListener
{
	/// <summary> A disabled player in the scene that gets copied for each person. </summary>
	[Property] public GameObject PlayerTemplate { get; set; }
	[Property] public bool HostOnStart { get; set; } = true;
	[Property] public int MaxPlayers { get; set; } = 4;
	[Property] public LobbyPrivacy Privacy { get; set; } = LobbyPrivacy.FriendsOnly;
	/// <summary> How long to wait for a lobby before starting solo. </summary>
	[Property] public float SoloFallbackSeconds { get; set; } = 2f;
	/// <summary> Where players spawn: an empty object marking the spot. Empty means the world origin. </summary>
	[Property] public GameObject SpawnCentre { get; set; }
	/// <summary> Distance between players when more than one spawns (they ring round the centre). </summary>
	[Property] public float SpawnSpacing { get; set; } = 90f;

	TimeSince timeSinceStart;
	bool spawnedSolo;

	protected override void OnStart()
	{
		if ( PlayerTemplate.IsValid() ) PlayerTemplate.Enabled = false;
		timeSinceStart = 0f;

		if ( HostOnStart && !Networking.IsActive )
		{
			Networking.CreateLobby( new LobbyConfig
			{
				Name = "Zombie Survival",
				MaxPlayers = MaxPlayers,
				Privacy = Privacy,
			} );
		}
	}

	protected override void OnUpdate()
	{
		// No lobby came up: just play on our own.
		if ( spawnedSolo || Networking.IsActive || timeSinceStart < SoloFallbackSeconds ) return;
		if ( Scene.GetAllComponents<PlayerSurvivor>().Any() ) return;

		spawnedSolo = true;
		Log.Info( "CoopNetwork: no lobby, starting solo." );
		SpawnPlayer( null );
	}

	/// <summary> On the host, once per connection that has finished loading (including our own). </summary>
	void Component.INetworkListener.OnActive( Connection connection )
	{
		Log.Info( $"CoopNetwork: {connection.DisplayName} joined, spawning their player." );
		SpawnPlayer( connection );
	}

	void SpawnPlayer( Connection owner )
	{
		if ( !PlayerTemplate.IsValid() )
		{
			Log.Warning( "CoopNetwork: no PlayerTemplate set." );
			return;
		}

		var index = Scene.GetAllComponents<PlayerSurvivor>().Count();
		var player = PlayerTemplate.Clone( SpawnPosition( index ) );
		player.Name = owner is null ? "Player" : $"Player - {owner.DisplayName}";
		player.Enabled = true;

		if ( owner is not null )
		{
			player.NetworkMode = NetworkMode.Object;
			player.NetworkSpawn( owner );
		}
	}

	/// <summary> The spawn centre (empty means the world origin). </summary>
	public Vector3 SpawnCentrePosition => SpawnCentre.IsValid() ? SpawnCentre.WorldPosition : Vector3.Zero;

	/// <summary> Where the Nth player (re)spawns, using this lobby's spawn centre and spacing. </summary>
	public Vector3 SpawnPosition( int index ) => SpawnPosition( SpawnCentrePosition, index, SpawnSpacing );

	/// <summary> Where the Nth player respawns: the first at the centre, the rest in a ring around it. </summary>
	public static Vector3 SpawnPosition( Vector3 centre, int index, float spacing )
	{
		if ( index <= 0 ) return centre;
		return centre + Rotation.FromYaw( index * 90f ).Forward * spacing;
	}
}
