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
	/// <summary> The other players' names (what they typed on the robot picker). </summary>
	readonly Dictionary<long, string> names = new();
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
		ProcessPriority = -400;   // puppets are placed before the creatures run
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
		RpcId( 1, MethodName.Hello, RobotClass.Current.Id, GameSettings.PlayerNameOrDefault );
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
		var who = PlayerName( id );
		peers.Remove( id );
		RemoveRemote( id );
		names.Remove( id );
		SetStatus( $"{who} left ({peers.Count + 1} playing)" );
	}

	public static string PlayerName( long id )
	{
		if ( id == Me || (!Online && id == 0) ) return GameSettings.PlayerNameOrDefault;
		if ( Instance is not null && Instance.names.TryGetValue( id, out var n ) && !string.IsNullOrWhiteSpace( n ) ) return n;
		return id == 1 ? "Host" : $"Player {id % 1000}";
	}

	/// <summary> This joiner's round trip to the host, in milliseconds (0 on the host). </summary>
	public static int MyPing => Instance?.Multiplayer.MultiplayerPeer is ENetMultiplayerPeer p && IsClient && Connected && p.GetPeer( 1 ) is { } host
		? (int)host.GetStatistic( ENetPacketPeer.PeerStatistic.RoundTripTime ) : 0;

	// ---- Who is playing ----

	/// <summary> A client arrived (on the host): tell it who is here, and tell everyone else about it. </summary>
	[Rpc( MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable )]
	void Hello( string robot, string name )
	{
		if ( !IsHost ) return;
		var id = Multiplayer.GetRemoteSenderId();
		foreach ( var (other, otherRobot) in peers ) RpcId( id, MethodName.Joined, other, otherRobot, names.GetValueOrDefault( other, "" ) );
		RpcId( id, MethodName.Joined, 1L, RobotClass.Current.Id, GameSettings.PlayerNameOrDefault );
		Rpc( MethodName.Joined, id, robot, name );
		names[id] = name;
		AddPeer( id, robot );
		// Everything already swimming about, and where the run is.
		foreach ( var c in Creature.Everyone ) if ( c.NetId > 0 && !c.IsDead ) SendCreatureTo( id, c );
		NetWorld.SendWaveTo( id );
	}

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable )]
	void Joined( long id, string robot, string name )
	{
		if ( id == Me ) return;
		names[id] = name;
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
		NetCreatures.Reset();
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

	// ---- The host's creatures (NetCreatures) ----

	float snapshotTimer;

	public override void _Process( double delta )
	{
		if ( !Connected ) return;
		var dt = (float)delta;
		if ( IsClient ) { NetCreatures.StepPuppets( dt ); return; }
		snapshotTimer -= dt;
		if ( snapshotTimer > 0f || peers.Count == 0 ) return;
		snapshotTimer = 1f / NetCreatures.SnapshotRate;
		var snap = NetCreatures.Snapshot();
		if ( snap.Length > 0 ) Rpc( MethodName.CreatureSnapshot, snap );
		NetWorld.HostTick();
	}

	/// <summary> Deferred from NetCreatures.Born: the spawner has set its look by now. </summary>
	void AnnounceCreature( Creature c )
	{
		if ( !Connected || !IsInstanceValid( c ) || c.NetId == 0 || c.IsDead ) return;
		var (kind, look) = NetCreatures.Describe( c );
		Rpc( MethodName.SpawnCreature, c.NetId, kind, c.GlobalPosition, c.GlobalBasis.Orthonormalized().GetRotationQuaternion(), c.Health?.MaxHealth ?? 100f, c.Health?.Current ?? 100f, look );
	}

	void SendCreatureTo( long peer, Creature c )
	{
		var (kind, look) = NetCreatures.Describe( c );
		RpcId( peer, MethodName.SpawnCreature, c.NetId, kind, c.GlobalPosition, c.GlobalBasis.Orthonormalized().GetRotationQuaternion(), c.Health?.MaxHealth ?? 100f, c.Health?.Current ?? 100f, look );
	}

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 4 )]
	void SpawnCreature( int id, string kind, Vector3 position, Quaternion rotation, float maxHealth, float health, float[] look )
		=> NetCreatures.Build( id, kind, position, rotation, maxHealth, health, look );

	public void SendCreatureGone( int id ) { if ( Connected ) Rpc( MethodName.CreatureGone, id ); }

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 4 )]
	void CreatureGone( int id )
	{
		if ( NetCreatures.Find( id ) is { } c && !c.IsDead ) c.QueueFree();
	}

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 5 )]
	void CreatureSnapshot( float[] data ) => NetCreatures.ApplySnapshot( data );

	public void SendShot( int id, int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, long shooter, bool scoreHit )
	{
		if ( Connected ) Rpc( MethodName.Shot, id, index, damage, direction, position, origin, shooter, scoreHit );
	}

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 4 )]
	void Shot( int id, int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, long shooter, bool scoreHit )
		=> NetCreatures.ApplyShot( id, index, damage, direction, position, origin, shooter, scoreHit );

	public void SendHealthHit( int id, float damage, Vector3 position, Vector3 origin, long attacker )
	{
		if ( Connected ) Rpc( MethodName.HealthHit, id, damage, position, origin, attacker );
	}

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 4 )]
	void HealthHit( int id, float damage, Vector3 position, Vector3 origin, long attacker )
		=> NetCreatures.ApplyHealthHit( id, damage, position, origin, attacker );

	// ---- The run (NetWorld) ----

	/// <summary> The wave state to everyone (peer 0), or to one joiner. </summary>
	public void SendWave( float[] state, long peer )
	{
		if ( !Connected ) return;
		if ( peer == 0 ) Rpc( MethodName.Wave, state ); else RpcId( peer, MethodName.Wave, state );
	}

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 6 )]
	void Wave( float[] state ) => NetWorld.ApplyWave( state );

	public void SendPoints( long peer, int amount ) => RpcId( peer, MethodName.Points, amount );

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3 )]
	void Points( int amount ) => PlayerSurvivor.Local?.AddPoints( amount );

	public void SendToken( long peer, int count ) => RpcId( peer, MethodName.Token, count );

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3 )]
	void Token( int count )
	{
		if ( PlayerSkills.Local is not { } skills || skills.Earned >= Skills.MaxTokens ) return;
		skills.Award( count );
		GameHud.Announce( $"+1 skill token  ({Controls.Label( "pause" )})", UiStyle.Gold );
	}

	public void SendRespawn( long peer, Vector3 position, bool fullReset ) => RpcId( peer, MethodName.RespawnHere, position, fullReset );

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3 )]
	void RespawnHere( Vector3 position, bool fullReset ) => PlayerSurvivor.Local?.Respawn( position, fullReset );

	/// <summary> The host restarts the run: everyone builds the level afresh. </summary>
	public void SendReload() { if ( Connected && IsHost ) Rpc( MethodName.ReloadLevel ); }

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3 )]
	void ReloadLevel()
	{
		GameSettings.Save();
		GetTree().ReloadCurrentScene();
	}

	public void SendTeleporter( Vector3 at, long peer = 0 )
	{
		if ( !Connected || !IsHost ) return;
		if ( peer == 0 ) Rpc( MethodName.TeleporterHere, at ); else RpcId( peer, MethodName.TeleporterHere, at );
	}

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3 )]
	void TeleporterHere( Vector3 at )
	{
		if ( Teleporter.Current is null && GetTree().CurrentScene is Node3D level ) Teleporter.Spawn( level, at, exact: true );
	}

	/// <summary> A joiner stepped into the teleporter: the host takes everyone through. </summary>
	public void RequestTeleport() { if ( Connected && IsClient ) RpcId( 1, MethodName.TeleportRequest ); }

	[Rpc( MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3 )]
	void TeleportRequest()
	{
		if ( IsHost && Teleporter.Current is { } pad && local is not null ) pad.Take( local );
	}

	public void SendTeleportAll() { if ( Connected && IsHost ) Rpc( MethodName.TeleportAll ); }

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3 )]
	void TeleportAll()
	{
		if ( local is null || !IsInstanceValid( local ) ) return;
		if ( Teleporter.Current is { } pad && IsInstanceValid( pad ) ) pad.Take( local, fromHost: true );
		else if ( GetTree().CurrentScene is Node3D level )
		{
			Areas.Next( level, local );
		}
	}

	// ---- Bubbles (the Bubble Cannon's traps on the host's creatures) ----

	public void SendBubble( int id, bool mega, long owner, float damage ) { if ( Connected ) Rpc( MethodName.Bubble, id, mega, owner, damage ); }

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 4 )]
	void Bubble( int id, bool mega, long owner, float damage )
	{
		if ( NetCreatures.Find( id ) is not { IsDead: false } c || BubbleTrap.Of( c ) is not null ) return;
		NetCreatures.Applying = true;
		try { BubbleTrap.Create( c, NetCreatures.PlayerOf( owner ), damage, mega ); }
		finally { NetCreatures.Applying = false; }
	}

	public void SendBubbleInflate( int id, int times ) { if ( Connected ) Rpc( MethodName.BubbleInflate, id, times ); }

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 4 )]
	void BubbleInflate( int id, int times )
	{
		if ( NetCreatures.Find( id ) is not { } c || BubbleTrap.Of( c ) is not { } trap ) return;
		NetCreatures.Applying = true;
		try { trap.Inflate( times ); }
		finally { NetCreatures.Applying = false; }
	}

	public void SendBubblePop( int id ) { if ( Connected ) Rpc( MethodName.BubblePop, id ); }

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 4 )]
	void BubblePop( int id )
	{
		if ( NetCreatures.Find( id ) is { } c ) BubbleTrap.PopNet( c );
	}

	// ---- Shoves and grabs on another player's robot (NetPlayer.HostSide) ----

	public void SendImpulse( long peer, Vector3 impulse ) { if ( Connected ) RpcId( peer, MethodName.Impulse, impulse ); }

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3 )]
	void Impulse( Vector3 impulse )
	{
		if ( local is null || !IsInstanceValid( local ) || local.Scripted ) return;
		if ( impulse.Y > 0.5f ) local.PreventGrounding( 0.15f );   // knocked up off the ground, not pinned to it
		local.Velocity += impulse;
	}

	public void SendHeld( long peer, Vector3 at, bool held ) { if ( Connected ) RpcId( peer, MethodName.HeldAt, at, held ); }

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 7 )]
	void HeldAt( Vector3 at, bool held )
	{
		if ( local is not null && IsInstanceValid( local ) ) local.GetNodeOrNull<NetPlayer>( "NetPlayer" )?.Held( at, held );
	}

	// ---- A player's skills and upgrades (NetPlayer) ----

	public void SendLoadout( int[] ranks, int damageLevel ) => Rpc( MethodName.Loadout, ranks, damageLevel );

	[Rpc( MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 2 )]
	void Loadout( int[] ranks, int damageLevel ) => RemoteOf( Multiplayer.GetRemoteSenderId() )?.GetNodeOrNull<NetPlayer>( "NetPlayer" )?.ApplyLoadout( ranks, damageLevel );

	// ---- Davey's locker (everyone goes) ----

	public void SendLocker( bool enter )
	{
		if ( !Connected || !IsHost ) return;
		var slot = 1;
		foreach ( var id in peers.Keys.OrderBy( x => x ) ) RpcId( id, MethodName.LockerGo, enter, slot++ );
	}

	[Rpc( MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3 )]
	void LockerGo( bool enter, int slot ) => NetWorld.Locker( enter, slot );

	// ---- Pings (TeamHud) ----

	public void SendPing( Vector3 at, int creature ) { if ( Connected ) Rpc( MethodName.PingAt, at, creature ); }

	[Rpc( MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3 )]
	void PingAt( Vector3 at, int creature ) => Pings.Show( Multiplayer.GetRemoteSenderId(), at, creature );

	// ---- A joiner's own hits (NetCreatures: the shooter's machine decides what it hit) ----

	public void SendShotRequest( int id, int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, bool scoreHit )
	{
		if ( Connected && IsClient ) RpcId( 1, MethodName.ShotRequest, id, index, damage, direction, position, origin, scoreHit );
	}

	[Rpc( MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 4 )]
	void ShotRequest( int id, int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, bool scoreHit )
	{
		if ( IsHost ) NetCreatures.ApplyShotRequest( Multiplayer.GetRemoteSenderId(), id, index, damage, direction, position, origin, scoreHit );
	}

	public void SendHealthRequest( int id, float damage, Vector3 position, Vector3 origin )
	{
		if ( Connected && IsClient ) RpcId( 1, MethodName.HealthRequest, id, damage, position, origin );
	}

	[Rpc( MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 4 )]
	void HealthRequest( int id, float damage, Vector3 position, Vector3 origin )
	{
		if ( IsHost ) NetCreatures.ApplyHealthRequest( Multiplayer.GetRemoteSenderId(), id, damage, position, origin );
	}

	// ---- Black holes (opened by their owner's own shot) ----

	public void SendBlackHole( Vector3 at, float damage, float radius, float size, Color colour, int rank )
	{
		if ( Connected ) Rpc( MethodName.BlackHoleAt, at, damage, radius, size, colour, rank );
	}

	[Rpc( MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 4 )]
	void BlackHoleAt( Vector3 at, float damage, float radius, float size, Color colour, int rank )
	{
		var owner = RemoteOf( Multiplayer.GetRemoteSenderId() );
		var vac = owner?.GetChildren().OfType<PlayerWeapon>().FirstOrDefault( w => w.Vacuum );
		if ( GetTree().CurrentScene is Node3D scene ) BlackHole.Open( scene, at, damage, radius, size, colour, owner, vac, rank );
	}

	// ---- Emotes ----

	public void SendEmote( int emote ) { if ( Connected ) Rpc( MethodName.EmoteNow, emote ); }

	[Rpc( MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 2 )]
	void EmoteNow( int emote ) => RemoteOf( Multiplayer.GetRemoteSenderId() )?.PlayEmote( (Emote)emote );
}
