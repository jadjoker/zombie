using Godot;

/// <summary>
/// Online co-op. One player hosts (the host runs the waves, the enemies and every hit on them), the others join by
/// address. Each machine moves its own robot and sends where it is, where it looks and its button presses; every other
/// machine shows that robot (<see cref="PlayerFactory.CreateRemote"/>) and runs its weapons from those presses, so
/// shots, bubbles and lures look the same everywhere, while only the host's copy hurts enemies.
/// Lives under the Clock autoload (/root/Clock/Net, the same path on every machine, kept across level reloads); every
/// message goes through it with the sender's peer id.
/// </summary>
public partial class Net : Node
{
	public const int DefaultPort = 24565;
	public const int MaxPlayers = 4;
	/// <summary> How often a player's own state goes out (a second). </summary>
	public const float SendRate = 30f;

	public static Net Instance { get; private set; }
	public enum NetMode { Offline, Host, Client }
	public static NetMode Mode { get; private set; }
	public static bool Online => Mode != NetMode.Offline;
	/// <summary> Online and through to the others (a joiner is Online while it is still connecting). </summary>
	public static bool Connected => Online && Instance?.Multiplayer.MultiplayerPeer is { } p && p.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;
	public static bool IsHost => Mode == NetMode.Host;
	public static bool IsClient => Mode == NetMode.Client;
	/// <summary> This machine's peer id (1 for the host, 0 offline). </summary>
	public static long Me => Instance is not null && Online ? Instance.Multiplayer.GetUniqueId() : 0;
	/// <summary> What the connection is doing, for the menus. </summary>
	public static string Status { get; private set; } = "Offline";
	public static event Action StatusChanged;

	/// <summary> The other players: their robot ids (what they last picked). </summary>
	readonly Dictionary<long, string> peers = new();
	/// <summary> Their robots in this level. </summary>
	readonly Dictionary<long, PlayerController> remotes = new();
	PlayerController local;

	public static IEnumerable<long> Peers => Instance?.peers.Keys ?? Enumerable.Empty<long>();
	public static PlayerController RemoteOf( long peer ) => Instance is not null && Instance.remotes.TryGetValue( peer, out var p ) && IsInstanceValid( p ) ? p : null;

	public override void _Ready()
	{
		Name = "Net";
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		Multiplayer.ConnectedToServer += OnConnected;
		Multiplayer.ConnectionFailed += OnConnectionFailed;
		Multiplayer.ServerDisconnected += OnServerGone;
		RobotClass.Changed += OnLocalRobotChanged;
		// Testing two copies on one machine: -- --host, or -- --join 127.0.0.1
		var args = OS.GetCmdlineUserArgs();
		if ( args.Contains( "--host" ) ) CallDeferred( MethodName.HostGame, DefaultPort );
		var j = Array.IndexOf( args, "--join" );
		if ( j >= 0 ) CallDeferred( MethodName.JoinGame, j + 1 < args.Length ? args[j + 1] : "127.0.0.1", DefaultPort );
	}

	public override void _ExitTree()
	{
		RobotClass.Changed -= OnLocalRobotChanged;
		if ( Instance == this ) Instance = null;
	}

	static void SetStatus( string text )
	{
		Status = text;
		GD.Print( $"[net] {text}" );
		StatusChanged?.Invoke();
	}

	// ---- Hosting and joining ----

	public void HostGame( int port )
	{
		Leave();
		var peer = new ENetMultiplayerPeer();
		var error = peer.CreateServer( port, MaxPlayers - 1 );
		if ( error != Error.Ok ) { SetStatus( $"Couldn't host on port {port} ({error})" ); return; }
		Multiplayer.MultiplayerPeer = peer;
		Mode = NetMode.Host;
		SetStatus( $"Hosting on port {port}. Waiting for players" );
	}

	public void JoinGame( string address, int port )
	{
		Leave();
		var peer = new ENetMultiplayerPeer();
		var error = peer.CreateClient( address.Trim(), port );
		if ( error != Error.Ok ) { SetStatus( $"Couldn't join {address} ({error})" ); return; }
		Multiplayer.MultiplayerPeer = peer;
		Mode = NetMode.Client;
		SetStatus( $"Joining {address}..." );
	}

	/// <summary> Back to playing alone: the other robots leave. </summary>
	public void Leave()
	{
		if ( Multiplayer.MultiplayerPeer is ENetMultiplayerPeer old ) old.Close();
		Multiplayer.MultiplayerPeer = null;
		foreach ( var id in remotes.Keys.ToList() ) RemoveRemote( id );
		peers.Clear();
		if ( Mode != NetMode.Offline ) SetStatus( "Offline" );
		Mode = NetMode.Offline;
	}

	void OnConnected()
	{
		SetStatus( "Connected" );
		RpcId( 1, MethodName.Hello, RobotClass.Current.Id );
	}

	void OnConnectionFailed() { SetStatus( "Couldn't connect" ); Leave(); }

	void OnServerGone() { Leave(); SetStatus( "The host left" ); }

	void OnPeerConnected( long id )
	{
		// The host says hello to everyone; a client only hears about the others through the host's roster.
		if ( IsHost ) GD.Print( $"[net] peer {id} connected" );
	}

	void OnPeerDisconnected( long id )
	{
		peers.Remove( id );
		RemoveRemote( id );
		SetStatus( $"{PlayerName( id )} left ({peers.Count + 1} playing)" );
	}

	public static string PlayerName( long id ) => id == 1 ? "Host" : $"Player {id % 1000}";

	// ---- Who is playing ----

	/// <summary> A client arrived (on the host): tell it who is here, and tell everyone else about it. </summary>
	[Rpc( MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable )]
	void Hello( string robot )
	{
		if ( !IsHost ) return;
		var id = Multiplayer.GetRemoteSenderId();
		foreach ( var (other, otherRobot) in peers ) RpcId( id, MethodName.Joined, other, otherRobot );
		RpcId( id, MethodName.Joined, 1L, RobotClass.Current.Id );
		Rpc( MethodName.Joined, id, robot );
		AddPeer( id, robot );
	}

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable )]
	void Joined( long id, string robot )
	{
		if ( id == Me ) return;
		AddPeer( id, robot );
	}

	void AddPeer( long id, string robot )
	{
		peers[id] = robot;
		SetStatus( $"{PlayerName( id )} joined ({peers.Count + 1} playing)" );
		SpawnRemote( id );
	}

	/// <summary> This machine's player is in the level (a new level, a restart): the other robots go in beside it. </summary>
	public void LocalPlayerReady( PlayerController player )
	{
		local = player;
		player.PeerId = Me;
		remotes.Clear();
		foreach ( var id in peers.Keys ) SpawnRemote( id );
	}

	void SpawnRemote( long id )
	{
		if ( local is null || !IsInstanceValid( local ) || RemoteOf( id ) is not null ) return;
		var robot = RobotClass.Find( peers.TryGetValue( id, out var r ) ? r : null );
		var p = PlayerFactory.CreateRemote( local.GetParent(), id, robot, local.GlobalPosition + Vector3.Right * 2f );
		remotes[id] = p;
	}

	void RemoveRemote( long id )
	{
		if ( remotes.TryGetValue( id, out var p ) && IsInstanceValid( p ) ) p.QueueFree();
		remotes.Remove( id );
	}

	// ---- Robots ----

	void OnLocalRobotChanged()
	{
		if ( Online ) Rpc( MethodName.RobotPicked, RobotClass.Current.Id );
	}

	[Rpc( MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable )]
	void RobotPicked( string robot )
	{
		var id = Multiplayer.GetRemoteSenderId();
		peers[id] = robot;
		if ( RemoteOf( id ) is not { } p ) return;
		var pick = RobotClass.Find( robot );
		p.SetRemoteRobot( pick );
		p.GetNodeOrNull<WeaponInventory>( "WeaponInventory" )?.Replace( pick.Kit() );
		p.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.OnRobotPicked();
		p.GetNodeOrNull<PlayerSkills>( "PlayerSkills" )?.RobotChanged();
	}

	// ---- A player's state, presses and weapon ----

	public void SendState( float[] state ) => Rpc( MethodName.State, state );

	[Rpc( MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1 )]
	void State( float[] state ) => RemoteOf( Multiplayer.GetRemoteSenderId() )?.GetNodeOrNull<NetPlayer>( "NetPlayer" )?.ApplyState( state );

	public void SendEdge( int action, bool down ) => Rpc( MethodName.Edge, action, down );

	[Rpc( MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 2 )]
	void Edge( int action, bool down )
	{
		if ( RemoteOf( Multiplayer.GetRemoteSenderId() )?.In is RemoteInput input ) input.Queue( action, down );
	}

	public void SendWeapon( int slot ) => Rpc( MethodName.WeaponSlot, slot );

	[Rpc( MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 2 )]
	void WeaponSlot( int slot )
	{
		if ( RemoteOf( Multiplayer.GetRemoteSenderId() )?.GetNodeOrNull<WeaponInventory>( "WeaponInventory" ) is not { } inv ) return;
		var weapon = inv.Weapons.FirstOrDefault( w => w.Slot == slot );
		if ( weapon is not null && inv.Current != weapon ) inv.Select( weapon, force: true );
	}

	// ---- Hits on players ----

	/// <summary> The host: an enemy hit another player's robot; that player's machine takes the hit. </summary>
	public void SendHurt( long peer, DamageInfo damage ) => RpcId( peer, MethodName.Hurt, damage.Damage, damage.Position, damage.Origin );

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3 )]
	void Hurt( float amount, Vector3 position, Vector3 origin )
	{
		if ( local is null || !IsInstanceValid( local ) ) return;
		local.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.Health?.OnDamage( new DamageInfo { Damage = amount, Position = position, Origin = origin } );
	}
}
