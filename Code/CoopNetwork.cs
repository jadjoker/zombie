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
		var player = PlayerTemplate.Clone( SpawnPosition( Scene, index ) );
		player.Name = owner is null ? "Player" : $"Player - {owner.DisplayName}";
		player.Enabled = true;

		if ( owner is not null )
		{
			player.NetworkMode = NetworkMode.Object;
			player.NetworkSpawn( owner );
		}
	}

	/// <summary> Where the Nth player (re)spawns: the first in the middle, the rest in a ring around it. </summary>
	public static Vector3 SpawnPosition( Scene scene, int index )
	{
		var centre = Vector3.Zero;
		if ( index <= 0 ) return centre;
		var angle = index * 90f;
		return centre + Rotation.FromYaw( angle ).Forward * 90f;
	}
}
