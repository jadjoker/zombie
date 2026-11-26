using Godot;

/// <summary> The areas in order (the shore, the wreck site, the Abyss, Atlantis): going through to the next one. </summary>
public static class Areas
{
	/// <summary> Builds the next area in place and puts the player on its arrival pad. </summary>
	public static void Next( Node3D level, PlayerController controller )
	{
		switch ( SeaTerrain.Area )
		{
			case SeaTerrain.Shore: AreaTwo.Go( level, controller ); break;
			case SeaTerrain.Wreck: AbyssArea.Go( level, controller ); break;
			case SeaTerrain.Caves: AtlantisArea.Go( level, controller ); break;
		}
		BringAllies();
	}

	/// <summary> Ally spiders come through with their owners (left behind they stood where the old area's middle was: in the Abyss, inside the statue). </summary>
	public static void BringAllies()
	{
		foreach ( var s in SeaSpider.All.Where( s => s.IsAlly && !s.IsDead && s.OwnerPlayer is not null ).ToList() )
		{
			var owner = s.OwnerPlayer;
			var right = owner.EyeAngles.WithPitch( 0f ).Right;
			s.GlobalPosition = Nav.Closest( owner, owner.GlobalPosition + right * 2.5f );
		}
	}

	/// <summary> Where the next teleporter goes, for the loading screen. </summary>
	public static string NextName => SeaTerrain.Area switch
	{
		SeaTerrain.Shore => "To the wreck site",
		SeaTerrain.Wreck => "Into the Abyss",
		_ => "To Atlantis",
	};

	/// <summary> The teleporter's prompt. </summary>
	public static string NextPrompt( int nextWave ) => SeaTerrain.Area switch
	{
		SeaTerrain.Shore => "To the wreck site, the second stage. Wave 11 starts there",
		SeaTerrain.Wreck => $"Into the Abyss, the third stage. Wave {nextWave} starts there",
		_ => $"To Atlantis, the fourth stage. Wave {nextWave} starts there",
	};
}
