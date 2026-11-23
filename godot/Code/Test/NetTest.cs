using Godot;

/// <summary>
/// --nettest: online co-op between two copies of the game on one machine. Run one with `--host` and one with
/// `--join 127.0.0.1 --class vacbot`. Stage 1: each sees the other's robot (the right one), the joiner's walk shows
/// on the host, and the joiner's Bubble Cannon tap fires on the host's copy of its robot too.
/// </summary>
public partial class AutoTest
{
	async Task NetTest()
	{
		DockHouse.Current?.SkipDive();
		await WaitForNav();
		var host = Net.IsHost;
		GD.Print( $"[autotest] net: {(host ? "hosting" : "joining")}" );
		PlayerController other = null;
		for ( var t = 0f; t < 40f && other is null; t += 0.25f )
		{
			await Wait( 0.25f );
			foreach ( var id in Net.Peers ) other = Net.RemoteOf( id ) ?? other;
		}
		Check( other is not null, "the other player's robot never showed up" );
		if ( other is null ) { Finish(); return; }
		GD.Print( $"[autotest] net: the other robot is {other.Robot.Name} ({other.Name})" );

		if ( host )
		{
			Check( other.Robot == RobotClass.VacBot, $"the joiner picked Vac-Bot but shows as {other.Robot.Name}" );
			await Wait( 1f );   // its first state has placed it
			var start = other.GlobalPosition;
			var firedBefore = BubbleShot.Fired;
			var moved = 0f;
			for ( var t = 0f; t < 20f && (moved < 3f || BubbleShot.Fired == firedBefore); t += 0.25f )
			{
				await Wait( 0.25f );
				moved = MathF.Max( moved, other.GlobalPosition.DistanceTo( start ) );
			}
			GD.Print( $"[autotest] net: the joiner's robot moved {moved:0.0} m here; bubbles it blew here: {BubbleShot.Fired - firedBefore}" );
			Check( moved >= 3f, "the joiner walked but its robot didn't move on the host" );
			Check( BubbleShot.Fired > firedBefore, "the joiner's Bubble Cannon tap didn't fire on the host" );
			await Wait( 8f );   // the joiner finishes its own checks first
		}
		else
		{
			Check( other.Robot == RobotClass.RamBot, $"the host is Ram-Bot but shows as {other.Robot.Name}" );
			await Wait( 1f );
			var (spot, _) = OpenLane( 8f );
			player.GlobalPosition = spot + Vector3.Up * 0.1f;
			player.Velocity = Vector3.Zero;
			await Wait( 1.5f );
			var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
			inventory.Select( inventory.Weapons.First( w => w.Bubbles ), force: true );
			await Wait( 1f );
			var mine = BubbleShot.Fired;
			Input.ActionPress( "attack1" );
			await Wait( 0.06f );
			Input.ActionRelease( "attack1" );
			await Wait( 1f );
			Check( BubbleShot.Fired > mine, "the tap didn't blow a bubble here" );
			await Wait( 4f );
		}
		Finish();
	}

	void Finish()
	{
		GD.Print( failures == 0 ? "[autotest] net: PASS" : $"[autotest] net: {failures} failed" );
		GD.Print( "[autotest] done" );
		Net.Instance?.Leave();
		GetTree().Quit();
	}
}
