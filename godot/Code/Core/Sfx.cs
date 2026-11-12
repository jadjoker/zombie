using Godot;

/// <summary>
/// Sound hooks. The s&box sounds were Facepunch cloud assets (not ours), so every place that played one calls
/// <see cref="Play"/> with a name instead; drop a file named after it into res://assets/sounds (.ogg, .wav or .mp3)
/// and it plays. Missing files are silent. Names: see assets/sounds/README.md.
/// </summary>
public static class Sfx
{
	static readonly Dictionary<string, AudioStream> streams = new();

	/// <summary> How often each name was asked for this run, and whether it had a file (for tests). </summary>
	public static readonly Dictionary<string, (int Count, bool Found)> Requested = new();

	static AudioStream Track( string name )
	{
		var s = Find( name );
		Requested[name] = (Requested.GetValueOrDefault( name ).Count + 1, s is not null);
		return s;
	}

	static AudioStream Find( string name )
	{
		if ( streams.TryGetValue( name, out var s ) ) return s;
		foreach ( var ext in new[] { "ogg", "wav", "mp3" } )
		{
			var path = $"res://assets/sounds/{name}.{ext}";
			if ( ResourceLoader.Exists( path ) ) { s = GD.Load<AudioStream>( path ); break; }
		}
		streams[name] = s;
		return s;
	}

	static readonly Dictionary<string, int> variants = new();

	/// <summary>
	/// One of several recordings of the same sound: name_0, name_1, ... picked at random (footstep_dock_3). The plain
	/// name is used when there are no numbered ones, so a sound can start as one file and grow variations later.
	/// </summary>
	public static string Pick( string name )
	{
		if ( !variants.TryGetValue( name, out var count ) )
		{
			count = 0;
			while ( Find( $"{name}_{count}" ) is not null ) count++;
			variants[name] = count;
		}
		return count == 0 ? name : $"{name}_{Rng.Int( 0, count - 1 )}";
	}

	/// <summary> A sound at a point in the world (volume 0-1, pitch 1 = normal). </summary>
	public static void Play( string name, Vector3 at, float volume = 1f, float pitch = 1f )
	{
		var stream = Track( name );
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( stream is null || root is null ) return;
		var player = new AudioStreamPlayer3D { Stream = stream, VolumeDb = Mathf.LinearToDb( MathF.Max( volume, 0.0001f ) ), PitchScale = pitch, UnitSize = 4f };
		root.AddChild( player );
		player.GlobalPosition = at;
		player.Finished += player.QueueFree;
		player.Play();
	}

	/// <summary> A sound for the local player only (UI, own gun, wave banners). </summary>
	public static void Play2D( string name, float volume = 1f, float pitch = 1f )
	{
		var stream = Track( name );
		var root = (Engine.GetMainLoop() as SceneTree)?.CurrentScene;
		if ( stream is null || root is null ) return;
		var player = new AudioStreamPlayer { Stream = stream, VolumeDb = Mathf.LinearToDb( MathF.Max( volume, 0.0001f ) ), PitchScale = pitch };
		root.AddChild( player );
		player.Finished += player.QueueFree;
		player.Play();
	}
}
