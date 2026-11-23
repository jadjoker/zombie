using Godot;

/// <summary>
/// The run online (<see cref="Net"/>): the host's wave director runs the waves; joiners show its wave, timer, count and
/// game over (<see cref="WaveDirector.ApplyNet"/>), follow it through the teleporters to the next area, and reload
/// with it on a restart. Points, skill tokens and respawns the host's director hands out to another player's robot go
/// to that player's machine.
/// </summary>
public static class NetWorld
{
	static float waveTimer;

	/// <summary> The host, each creature snapshot: the wave state a few times a second. </summary>
	public static void HostTick()
	{
		waveTimer -= 1f / NetCreatures.SnapshotRate;
		if ( waveTimer > 0f ) return;
		waveTimer = 0.25f;
		if ( WaveDirector.Current is { } d ) Net.Instance?.SendWave( Pack( d ), 0 );
	}

	public static void SendWaveTo( long peer )
	{
		if ( WaveDirector.Current is { } d ) Net.Instance?.SendWave( Pack( d ), peer );
		if ( Teleporter.Current is { } pad && GodotObject.IsInstanceValid( pad ) ) Net.Instance?.SendTeleporter( pad.GlobalPosition, peer );
	}

	static float[] Pack( WaveDirector d ) => new float[]
	{
		d.Wave, (int)d.State, d.SecondsUntilNextWave, d.ZombiesRemaining, d.Kills,
		d.BossWave ? 1 : 0, d.FloatRound ? 1 : 0, d.WaitingForTeleport ? 1 : 0, d.KrakenWave ? 1 : 0, SeaTerrain.Area,
	};

	public static void ApplyWave( float[] s )
	{
		if ( s is null || s.Length < 10 || WaveDirector.Current is not { } d ) return;
		// Joined a run that is already further on: through to the host's area.
		if ( (int)s[9] > SeaTerrain.Area && SeaTerrain.Active && PlayerSurvivor.Local?.Controller is { } me && me.GetTree().CurrentScene is Node3D level )
		{
			if ( SeaTerrain.Area == 1 ) AreaTwo.Go( level, me );
			else AreaThree.Go( level, me );
		}
		d.ApplyNet( (int)s[0], (WaveState)(int)s[1], s[2], (int)s[3], (int)s[4], s[5] > 0.5f, s[6] > 0.5f, s[7] > 0.5f, s[8] > 0.5f );
	}

	// ---- Players the host's director looks after ----

	/// <summary> PlayerSurvivor.AddPoints on another player's robot: on the host, the points are theirs. True: sent (skip here). </summary>
	public static bool ForwardPoints( PlayerSurvivor s, int amount )
	{
		if ( !Net.Online || s.Controller is not { IsRemote: true } c ) return false;
		if ( Net.IsHost ) Net.Instance?.SendPoints( c.PeerId, amount );
		return true;
	}

	public static bool ForwardRespawn( PlayerSurvivor s, Vector3 position, bool fullReset )
	{
		if ( !Net.Online || s.Controller is not { IsRemote: true } c || NetPlayer.ApplyingState ) return false;
		if ( Net.IsHost ) Net.Instance?.SendRespawn( c.PeerId, position, fullReset );
		return true;
	}

	public static bool ForwardToken( PlayerSkills skills, PlayerController c, int count )
	{
		if ( !Net.Online || c is not { IsRemote: true } ) return false;
		if ( Net.IsHost ) Net.Instance?.SendToken( c.PeerId, count );
		return true;
	}
}
