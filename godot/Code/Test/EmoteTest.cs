using Godot;

/// <summary> --emotes (shots need a window): each emote plays on the robot, seen from the front camera, with pictures. </summary>
public partial class AutoTest
{
	async Task EmoteTest()
	{
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		var (spot, yaw) = OpenLane( 8f );
		player.GlobalPosition = spot + Vector3.Up * 0.1f;
		player.EyeAngles = new Angles( 0f, yaw, 0f );
		await Wait( 1f );
		foreach ( var (id, name, seconds) in Emotes.All )
		{
			player.PlayEmote( id );
			await Wait( 0.2f );
			Check( player.Emote == id && player.SeenFromOutside, $"{name} didn't start" );
			for ( var i = 0; i < 3; i++ )
			{
				await Wait( seconds / 4f );
				if ( shots is not null ) await Shot( $"emote_{id.ToString().ToLowerInvariant()}_{i}" );
			}
			await Wait( seconds / 4f + 0.3f );
			Check( player.Emote == Emote.None, $"{name} never ended" );
		}
		// Moving ends one early.
		player.PlayEmote( Emote.Griddy );
		await Wait( 0.3f );
		Input.ActionPress( "forward" );
		await Wait( 0.2f );
		Input.ActionRelease( "forward" );
		Check( player.Emote == Emote.None, "walking didn't end the emote" );
		GD.Print( "[autotest] emotes played" );
		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
