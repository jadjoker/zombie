using Godot;

/// <summary>
/// The dock radio's music (issue #42). Plays the songs in assets/music in a shuffled order (no song twice running,
/// every song once before any repeats), starts when the dock is built and carries on after the dive, through
/// restarts and level changes, and while the game is paused. It has its own volume (GameSettings.MusicVolume, the
/// pause menu's Music volume slider) on the "Music" bus. Up on the dock it is quieter the further you are from the
/// radio; after the dive it is steady. Autoloaded through <see cref="Clock"/>.
/// </summary>
public partial class Radio : Node
{
	public static Radio Instance { get; private set; }
	/// <summary> The song playing now, as it is shown on screen. </summary>
	public static string NowPlaying { get; private set; }
	/// <summary> Seconds since the song last changed (the HUD shows the title for the first few). </summary>
	public static TimeSince SinceChange = 999f;
	/// <summary> Where the radio is on the dock (set by the dock), for the volume by distance. </summary>
	public static Vector3? Spot;
	/// <summary> How many songs have started this run. </summary>
	public static int Changes { get; private set; }
	/// <summary> How loud it is right now on top of the music volume (1 steady, lower away from the radio on the dock). </summary>
	public static float Gain => Instance?.gain ?? 1f;

	readonly List<(string Title, AudioStream Stream)> tracks = new();
	readonly List<int> bag = new();
	int last = -1;
	AudioStreamPlayer player;
	float gain = 1f;

	public bool Playing => player is { Playing: true };
	public int TrackCount => tracks.Count;
	public IEnumerable<string> Titles => tracks.Select( t => t.Title );

	/// <summary> The bus all music plays on. Made on first use. </summary>
	public static string MusicBus
	{
		get
		{
			if ( AudioServer.GetBusIndex( "Music" ) < 0 )
			{
				AudioServer.AddBus();
				var bus = AudioServer.BusCount - 1;
				AudioServer.SetBusName( bus, "Music" );
				AudioServer.SetBusSend( bus, "Master" );
			}
			return "Music";
		}
	}

	public override void _Ready()
	{
		Name = "Radio";
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;   // it plays on while the game is paused
		LoadTracks();
		player = new AudioStreamPlayer { Bus = MusicBus };
		AddChild( player );
		player.Finished += Next;
		GameSettings.Apply();
	}

	public override void _ExitTree() { if ( Instance == this ) Instance = null; }

	void LoadTracks()
	{
		var seen = new HashSet<string>();
		foreach ( var file in DirAccess.GetFilesAt( "res://assets/music" ) )
		{
			// An exported game lists "song.mp3.import": the song is the name without it.
			var name = file.EndsWith( ".import" ) ? file[..^7] : file;
			if ( !(name.EndsWith( ".mp3" ) || name.EndsWith( ".ogg" ) || name.EndsWith( ".wav" )) || !seen.Add( name ) ) continue;
			if ( GD.Load<AudioStream>( $"res://assets/music/{name}" ) is not { } stream ) continue;
			var words = System.IO.Path.GetFileNameWithoutExtension( name ).Replace( '_', ' ' ).Split( ' ', StringSplitOptions.RemoveEmptyEntries );
			tracks.Add( (string.Join( ' ', words.Select( ( w, i ) => i > 0 && w is "the" or "on" or "of" or "a" ? w : char.ToUpperInvariant( w[0] ) + w[1..] ) ), stream) );
		}
		tracks.Sort( ( a, b ) => string.CompareOrdinal( a.Title, b.Title ) );
	}

	/// <summary> Starts the radio if it is not already playing (the dock calls this every time it is built). </summary>
	public void Start()
	{
		if ( tracks.Count == 0 || Playing ) return;
		Next();
	}

	/// <summary> The next song: a shuffled bag, so every song plays once before any repeats and none twice running. </summary>
	public void Next()
	{
		if ( tracks.Count == 0 ) return;
		if ( bag.Count == 0 )
		{
			for ( var i = 0; i < tracks.Count; i++ ) bag.Add( i );
			for ( var i = bag.Count - 1; i > 0; i-- ) { var j = Rng.Int( 0, i ); (bag[i], bag[j]) = (bag[j], bag[i]); }
			// A new bag must not start with the song that just finished.
			if ( bag.Count > 1 && bag[^1] == last ) (bag[0], bag[^1]) = (bag[^1], bag[0]);
		}
		last = bag[^1];
		bag.RemoveAt( bag.Count - 1 );
		var (title, stream) = tracks[last];
		player.Stream = stream;
		player.Play();
		NowPlaying = title;
		SinceChange = 0f;
		Changes++;
	}

	public override void _Process( double delta )
	{
		// Up on the dock it fades with distance from the radio; after the dive it is steady.
		var target = 1f;
		if ( DockHouse.WeaponsHeld && Spot is { } spot && PlayerSurvivor.All.FirstOrDefault() is { } p )
			target = MathX.Lerp( 0.3f, 1f, 1f - MathX.Clamp( p.WorldPosition.DistanceTo( spot ) / 16f, 0f, 1f ) );
		gain = MathX.Approach( gain, target, (float)delta * 1.5f );
		player.VolumeDb = Mathf.LinearToDb( MathF.Max( gain, 0.0001f ) );
	}
}
