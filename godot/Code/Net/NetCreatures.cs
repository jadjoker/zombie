using Godot;

/// <summary>
/// Enemies online (<see cref="Net"/>). The host runs every creature: its brain, its moves and every hit on it. Each
/// creature gets a number there; joiners build a copy of it (a puppet: its brain off, <see cref="Creature.Puppet"/>)
/// from a spawn message, place it from the host's snapshots, and replay the host's hits on it, so limbs come off and it
/// dies and bursts the same way. A joiner's own shots never hurt a puppet directly: the hit that counts is the one its
/// robot lands on the host (where its presses drive the same gun).
/// </summary>
public static class NetCreatures
{
	static int nextId = 1;
	static readonly Dictionary<int, Creature> byId = new();
	/// <summary> A joiner replaying the host's hit: the puppet takes it. </summary>
	public static bool Applying;
	/// <summary> Host: the creature whose ReceiveShot was just sent (its Health hit inside it isn't sent again). </summary>
	static Creature inShot;
	static ulong inShotFrame;

	public const float SnapshotRate = 15f;
	/// <summary> Tests: hits the host sent that another player's robot landed. </summary>
	public static int RemoteShots;
	const int SnapshotStride = 9;   // id, x, y, z, qx, qy, qz, qw, health

	public static Creature Find( int id ) => byId.TryGetValue( id, out var c ) && GodotObject.IsInstanceValid( c ) ? c : null;

	/// <summary> A level reload: the numbers start again (the creatures are gone). </summary>
	public static void Reset()
	{
		byId.Clear();
		nextId = 1;
	}

	// ---- Born and gone ----

	/// <summary> Creature._Ready. The host numbers it and (once its spawner has set its look) tells the joiners. </summary>
	public static void Born( Creature c )
	{
		if ( !Net.IsHost ) return;
		c.NetId = nextId++;
		byId[c.NetId] = c;
		Net.Instance?.CallDeferred( Net.MethodName.AnnounceCreature, c );
	}

	/// <summary> Creature._ExitTree. </summary>
	public static void Gone( Creature c )
	{
		if ( c.NetId == 0 ) return;
		byId.Remove( c.NetId );
		if ( Net.IsHost && !c.IsDead ) Net.Instance?.SendCreatureGone( c.NetId );   // despawned, not killed
	}

	/// <summary> What a joiner needs to build a copy: kind, place, facing, health, and its look. </summary>
	public static (string Kind, float[] Look) Describe( Creature c ) => c switch
	{
		JunkCrab x => ("JunkCrab", new[] { x.Hue, x.Size }),
		Bloober x => ("Bloober", new[] { x.Hue, 0f }),
		Squilien x => ("Squilien", new[] { x.Hue, 0f }),
		RockyShark x => ("RockyShark", new[] { x.Hue, x.Size }),
		SeaSpider x => ("SeaSpider", new[] { x.Hue, x.Size, x.IsAlly ? 1f : 0f, x.OwnerPlayer?.NetPeer ?? 0 }),
		Mermaid x => ("Mermaid", new[] { x.Male ? 1f : 0f, x.Archer ? 1f : 0f, x.HairColour }),
		Seahorse => ("Seahorse", Array.Empty<float>()),
		Angler => ("Angler", Array.Empty<float>()),
		MC61 => ("MC61", Array.Empty<float>()),
		Kraken x => ("Kraken", new[] { x.HelmetHealth }),
		Poseidon => ("Poseidon", Array.Empty<float>()),
		DaveyJones => ("DaveyJones", Array.Empty<float>()),
		HomingMissile => ("HomingMissile", Array.Empty<float>()),
		_ => (c.GetType().Name, Array.Empty<float>()),
	};

	/// <summary> A joiner builds the host's creature as a puppet. </summary>
	public static void Build( int id, string kind, Vector3 position, Quaternion rotation, float maxHealth, float health, float[] look )
	{
		if ( Find( id ) is not null || (Engine.GetMainLoop() as SceneTree)?.CurrentScene is not Node3D scene ) return;
		float L( int i, float fallback = 0f ) => look is not null && i < look.Length ? look[i] : fallback;
		Creature c = kind switch
		{
			"JunkCrab" => Set( JunkCrab.Spawn( scene, position, maxHealth ), x => { x.Hue = L( 0, -1f ); x.Size = L( 1, 1f ); } ),
			"Bloober" => Set( Bloober.Spawn( scene, position, maxHealth ), x => x.Hue = L( 0, -1f ) ),
			"Squilien" => Set( Squilien.Spawn( scene, position, maxHealth ), x => x.Hue = L( 0, -1f ) ),
			"RockyShark" => Set( RockyShark.Spawn( scene, position, maxHealth ), x => { x.Hue = L( 0, -1f ); x.Size = L( 1, 1f ); } ),
			"SeaSpider" => Set( SeaSpider.Spawn( scene, position, maxHealth, L( 2 ) > 0.5f, PlayerOf( (long)L( 3 ) ) ), x => { x.Hue = L( 0, -1f ); x.Size = L( 1, 1f ); } ),
			"Mermaid" => Set( Mermaid.Spawn( scene, position, maxHealth, L( 0 ) > 0.5f, L( 1 ) > 0.5f ), x => x.HairColour = (int)L( 2 ) ),
			"Seahorse" => Seahorse.Spawn( scene, position, maxHealth ),
			"Angler" => Angler.Spawn( scene, position, maxHealth ),
			"MC61" => MC61.Spawn( scene, position, maxHealth ),
			"Kraken" => Set( Kraken.Spawn( scene, position, maxHealth ), x => x.HelmetHealth = L( 0, 1200f ) ),
			"Poseidon" => Poseidon.Spawn( scene, position, maxHealth ),
			"DaveyJones" => DaveyJones.Spawn( scene, position, maxHealth ),
			"HomingMissile" => HomingMissile.Launch( scene, position, Vector3.Forward, null ),
			_ => null,
		};
		if ( c is null ) { GD.PushWarning( $"[net] can't build a {kind} here yet" ); return; }
		c.NetId = id;
		c.Puppet = true;
		c.GlobalBasis = new Basis( rotation );
		c.SetPuppetTarget( position, rotation );
		byId[id] = c;
		if ( health < maxHealth ) c.CallDeferred( Creature.MethodName.SetNetHealth, health );
	}

	static T Set<T>( T c, Action<T> look ) where T : Creature { look( c ); return c; }

	/// <summary> The robot of a peer on this machine (the local one, or a remote one). </summary>
	public static PlayerController PlayerOf( long peer )
	{
		if ( peer == 0 ) return null;
		if ( peer == Net.Me ) return PlayerSurvivor.Local?.Controller;
		return Net.RemoteOf( peer );
	}

	// ---- Hits ----

	/// <summary>
	/// The first line of every creature's ReceiveShot. Host: tells the joiners about the hit, then takes it. Joiner:
	/// a puppet only takes hits the host sent (true means: skip it).
	/// </summary>
	public static bool Intercept( Creature c, int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, PlayerSurvivor shooter, bool scoreHit )
	{
		if ( !Net.Online ) return false;
		if ( Net.IsHost )
		{
			if ( c.NetId == 0 ) return false;
			inShot = c;
			inShotFrame = Engine.GetProcessFrames();
			var peer = shooter?.Controller?.NetPeer ?? 0;
			if ( peer > 1 ) RemoteShots++;
			Net.Instance?.SendShot( c.NetId, index, damage, direction, position, origin, peer, scoreHit );
			return false;
		}
		return c.Puppet && !Applying;
	}

	/// <summary>
	/// Health.OnDamage on a creature (blasts, shocks, bleeding: anything that skips ReceiveShot). Host: sends it unless
	/// it is the inside of a shot already sent. Joiner: a puppet only takes what the host sent (true: skip it).
	/// </summary>
	public static bool InterceptHealth( Creature c, DamageInfo damage )
	{
		if ( !Net.Online ) return false;
		if ( Net.IsHost )
		{
			if ( c.NetId == 0 ) return false;
			if ( inShot == c && inShotFrame == Engine.GetProcessFrames() ) { inShot = null; return false; }
			var peer = damage.Attacker is PlayerSurvivor s ? s.Controller?.NetPeer ?? 0 : damage.Attacker is PlayerController pc ? pc.NetPeer : 0;
			Net.Instance?.SendHealthHit( c.NetId, damage.Damage, damage.Position, damage.Origin, peer );
			return false;
		}
		return c.Puppet && !Applying;
	}

	/// <summary> A joiner replays the host's shot on its puppet. </summary>
	public static void ApplyShot( int id, int index, float damage, Vector3 direction, Vector3 position, Vector3 origin, long shooterPeer, bool scoreHit )
	{
		if ( Find( id ) is not { IsDead: false } c ) return;
		var shooter = PlayerOf( shooterPeer )?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
		Applying = true;
		try { c.ReceiveShot( index, damage, direction, position, origin, shooter, scoreHit ); }
		finally { Applying = false; }
	}

	public static void ApplyHealthHit( int id, float damage, Vector3 position, Vector3 origin, long attackerPeer )
	{
		if ( Find( id ) is not { IsDead: false } c || c.Health is not { } h ) return;
		Applying = true;
		try { h.OnDamage( new DamageInfo { Damage = damage, Position = position, Origin = origin, Attacker = PlayerOf( attackerPeer )?.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" ) } ); }
		finally { Applying = false; }
	}

	// ---- Snapshots ----

	/// <summary> Host: where every numbered creature is, its facing and health. </summary>
	public static float[] Snapshot()
	{
		var list = new List<float>( byId.Count * SnapshotStride );
		foreach ( var (id, c) in byId )
		{
			if ( !GodotObject.IsInstanceValid( c ) || !c.IsInsideTree() ) continue;
			var p = c.GlobalPosition;
			var q = c.GlobalBasis.Orthonormalized().GetRotationQuaternion();
			list.Add( id ); list.Add( p.X ); list.Add( p.Y ); list.Add( p.Z );
			list.Add( q.X ); list.Add( q.Y ); list.Add( q.Z ); list.Add( q.W );
			list.Add( c.Health is { } h ? (h.IsDead ? 0f : MathF.Max( 0.01f, h.Current )) : -1f );
		}
		return list.ToArray();
	}

	/// <summary> A joiner: the host's snapshot sets each puppet's goal (and health, and death). </summary>
	public static void ApplySnapshot( float[] data )
	{
		if ( data is null ) return;
		for ( var i = 0; i + SnapshotStride <= data.Length; i += SnapshotStride )
		{
			if ( Find( (int)data[i] ) is not { } c || !c.Puppet ) continue;
			c.SetPuppetTarget( new Vector3( data[i + 1], data[i + 2], data[i + 3] ), new Quaternion( data[i + 4], data[i + 5], data[i + 6], data[i + 7] ).Normalized() );
			c.SetNetHealth( data[i + 8] );
		}
	}

	/// <summary> A joiner, every frame before the creatures run: eases each puppet toward the host's spot. </summary>
	public static void StepPuppets( float dt )
	{
		foreach ( var c in byId.Values )
			if ( GodotObject.IsInstanceValid( c ) && c.Puppet && !c.IsDead ) c.StepPuppet( dt );
	}
}
