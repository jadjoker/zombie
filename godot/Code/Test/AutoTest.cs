using Godot;

/// <summary>
/// Scripted checks for the ported movement, run with: godot -- --autotest [--shots dir]
/// Presses the actions like a player would and prints speeds and heights in s&box units, so they can be compared
/// with the s&box tunings. Takes screenshots when asked, then quits.
/// </summary>
public partial class AutoTest : Node
{
	PlayerController player;
	PlayerMovement movement;
	string shots;

	public static bool Requested => OS.GetCmdlineUserArgs().Contains( "--autotest" );

	public override async void _Ready()
	{
		var args = OS.GetCmdlineUserArgs();
		var i = Array.IndexOf( args, "--shots" );
		if ( i >= 0 && i + 1 < args.Length ) shots = args[i + 1];

		await Wait( 0.5f );
		player = GetTree().Root.FindChild( "Player", true, false ) as PlayerController;
		movement = player.GetNode<PlayerMovement>( "PlayerMovement" );
		Input.MouseMode = Input.MouseModeEnum.Visible;

		Report( "settled" );
		await Shot( "spawn" );

		// Run (default) and sprint across the open half of the range (+Z, yaw 180).
		player.GlobalPosition = new Vector3( 0f, 0.05f, 1600f * U.In );
		player.EyeAngles = new Angles( 0f, 180f, 0f );
		await Wait( 0.3f );
		Input.ActionPress( "forward" );
		await Wait( 1.0f );
		Report( "run 1s (expect 320)" );
		Input.ActionPress( "run" );
		await Wait( 1.0f );
		Report( "sprint 1s (expect 470)" );
		Input.ActionRelease( "run" );
		Input.ActionRelease( "forward" );
		await Wait( 0.6f );
		Report( "stopped 0.6s (expect 0)" );

		// Jump height: s&box JumpSpeed 300, gravity 850 -> 52.9 units.
		var startY = player.GlobalPosition.Y;
		var peak = startY;
		Input.ActionPress( "jump" );
		await Wait( 0.05f );
		Input.ActionRelease( "jump" );
		for ( var t = 0f; t < 0.9f; t += 0.02f )
		{
			await Wait( 0.02f );
			peak = MathF.Max( peak, player.GlobalPosition.Y );
		}
		GD.Print( $"[autotest] jump height {(peak - startY) / U.In:0.0} u (expect ~52.9)" );

		// Slide: sprint, then crouch.
		Input.ActionPress( "forward" );
		Input.ActionPress( "run" );
		await Wait( 0.8f );
		Input.ActionPress( "duck" );
		await Wait( 0.08f );
		Report( "slide start (expect ~620 = 470 + 150 boost)" );
		await Shot( "slide" );
		await Wait( 0.6f );
		Report( "slide 0.6s" );
		Input.ActionRelease( "duck" );
		Input.ActionRelease( "run" );
		Input.ActionRelease( "forward" );
		await Wait( 1.0f );

		// Air jump: jump, then jump again near the top.
		Input.ActionPress( "jump" ); await Wait( 0.05f ); Input.ActionRelease( "jump" );
		await Wait( 0.3f );
		var before = movement.Stamina;
		Input.ActionPress( "jump" ); await Wait( 0.05f ); Input.ActionRelease( "jump" );
		await Wait( 0.05f );
		GD.Print( $"[autotest] air jump: vertical {player.Velocity.Y / U.In:0} u/s (expect ~340), stamina {before:0.0} -> {movement.Stamina:0.0}" );
		await Wait( 1.2f );

		// Weapons: face the targets from 6 m back.
		Input.MouseMode = Input.MouseModeEnum.Captured;
		player.ThirdPerson = false;
		player.GlobalPosition = new Vector3( 0f, 0.05f, 900f * U.In );
		player.Velocity = Vector3.Zero;
		player.EyeAngles = new Angles( 4f, 180f, 0f );
		var inventory = player.GetNode<WeaponInventory>( "WeaponInventory" );
		await Wait( 1.0f );
		await Shot( "rifle_hip" );
		var rifle = inventory.Current;
		Input.ActionPress( "attack1" );
		await Wait( 0.5f );
		Input.ActionRelease( "attack1" );
		await Shot( "rifle_after_burst" );
		GD.Print( $"[autotest] rifle burst: ammo {rifle.Ammo}/{rifle.MagazineSize} reserve {rifle.Reserve} (8/s for 0.5 s -> about 26 left)" );
		Input.ActionPress( "attack2" );
		await Wait( 0.12f );
		await Shot( "rifle_aim_mid" );
		await Wait( 0.4f );
		await Shot( "rifle_scoped" );
		GD.Print( $"[autotest] scope: aim {rifle.AimAmount:0.00} scope {rifle.ScopeAmount:0.00} fov {player.Camera.Fov:0.0} (expect 90*0.7*0.43 = 27)" );
		Input.ActionRelease( "attack2" );
		await Wait( 0.4f );
		Input.ActionPress( "reload" ); await Wait( 0.05f ); Input.ActionRelease( "reload" );
		await Wait( 0.8f );
		await Shot( "rifle_reload" );
		await Wait( 1.0f );
		GD.Print( $"[autotest] after reload: ammo {rifle.Ammo} reserve {rifle.Reserve}" );

		inventory.Select( inventory.Weapons[1] );
		await Wait( 1.0f );
		await Shot( "shotgun_hip" );
		var shotgun = inventory.Current;
		await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
		Input.ActionPress( "attack1" );
		await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
		await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
		Input.ActionRelease( "attack1" );
		GD.Print( $"[autotest] shotgun fired: ammo {shotgun.Ammo} (expect 5), active {shotgun.Active}" );
		await Wait( 0.1f );
		await Shot( "shotgun_fire" );
		await Wait( 0.6f );
		Input.ActionPress( "reload" ); await Wait( 0.05f ); Input.ActionRelease( "reload" );
		await Wait( 1.0f );
		await Shot( "shotgun_reload" );
		await Wait( 1.0f );
		GD.Print( $"[autotest] shotgun: ammo {shotgun.Ammo}/{shotgun.MagazineSize} reserve {shotgun.Reserve}" );
		Input.ActionPress( "attack2" );
		await Wait( 0.5f );
		await Shot( "shotgun_aim" );
		Input.ActionRelease( "attack2" );

		player.GetNode<PlayerMelee>( "PlayerMelee" ).TestSwing();
		await Wait( 0.2f );
		await Shot( "axe_swing" );
		await Wait( 0.8f );

		// Third person view.
		player.ThirdPerson = true;
		player.EyeAngles = new Angles( 15f, 200f, 0f );
		await Wait( 0.6f );
		await Shot( "third_person" );

		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	void Report( string label )
	{
		var v = player.Velocity;
		GD.Print( $"[autotest] {label}: horizontal {v.Flat().Length() / U.In:0} u/s, vertical {v.Y / U.In:0}, ground={player.IsOnGround}, mode={movement.Ground}, pos {player.GlobalPosition / U.In}" );
	}

	async Task Wait( float seconds )
	{
		await ToSignal( GetTree().CreateTimer( seconds, processAlways: true, processInPhysics: true ), SceneTreeTimer.SignalName.Timeout );
	}

	async Task Shot( string name )
	{
		if ( string.IsNullOrEmpty( shots ) ) return;
		await ToSignal( RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw );
		var image = GetViewport().GetTexture().GetImage();
		var path = System.IO.Path.Combine( shots, name + ".png" );
		image.SavePng( path );
		GD.Print( $"[autotest] shot {path}" );
	}
}
