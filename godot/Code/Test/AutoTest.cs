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

		// Bloobers: three in front, then shoot the nearest one apart.
		var range = GetTree().CurrentScene;
		inventory.Select( inventory.Weapons[0] );
		player.EyeAngles = new Angles( -4f, 180f, 0f );
		for ( var b = 0; b < 3; b++ )
			Bloober.Spawn( range, player.GlobalPosition + new Vector3( (b - 1) * 120f, 70f, 500f + b * 80f ) * U.In, 160f, Vector3.Back * -1f );
		await Wait( 0.6f );
		await Shot( "bloobers" );
		var first = Bloober.All.OrderBy( x => x.GlobalPosition.DistanceTo( player.GlobalPosition ) ).First();
		// Close look with everything frozen: is the sculpted head facing the same way as the eyes?
		DevFlags.FreezeEnemies = true;
		var faceDir = -first.GlobalBasis.Z;
		player.GlobalPosition = first.GlobalPosition + faceDir * 90f * U.In - Vector3.Up * 60f * U.In;
		player.Velocity = Vector3.Zero;
		LookAt( first.GlobalPosition );
		await Wait( 0.3f );
		LookAt( first.GlobalPosition );
		await Shot( "bloober_face" );
		player.GlobalPosition = first.GlobalPosition + first.GlobalBasis.X * 140f * U.In - Vector3.Up * 60f * U.In;
		LookAt( first.GlobalPosition + first.GlobalBasis.Z * 40f * U.In );
		await Wait( 0.3f );
		LookAt( first.GlobalPosition + first.GlobalBasis.Z * 40f * U.In );
		await Shot( "bloober_side" );
		var weapon = inventory.Current;
		// Shoot the body (segment 3 area) a few times to cut it, then the head.
		for ( var k = 0; k < 4; k++ ) { LookAt( first.GlobalPosition + first.GlobalBasis.Z * 50f * U.In ); weapon.TestFire(); await Wait( 0.13f ); }
		await Wait( 0.2f );
		await Shot( "bloober_cut" );
		for ( var k = 0; k < 10 && !first.IsDead; k++ ) { LookAt( first.GlobalPosition ); weapon.TestFire(); await Wait( 0.13f ); }
		DevFlags.FreezeEnemies = false;
		GD.Print( $"[autotest] bloober after 14 rifle hits: dead {first.IsDead}, health {first.Health?.Current:0}, bloobers alive {Bloober.All.Count( x => !x.IsDead )}, points {player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Points}" );
		await Shot( "bloober_shot" );
		await Wait( 1.2f );
		await Shot( "bloober_after" );
		await Wait( 3f );
		GD.Print( $"[autotest] after 3 s: player health {player.GetNode<PlayerSurvivor>( "PlayerSurvivor" ).Health.Current:0}" );
		await Shot( "bloober_bite" );

		// Third person view.
		player.ThirdPerson = true;
		player.EyeAngles = new Angles( 15f, 200f, 0f );
		await Wait( 0.6f );
		await Shot( "third_person" );

		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}

	void LookAt( Vector3 at )
	{
		var dir = (at - player.Camera.GlobalPosition).Normalized();
		player.EyeAngles = new Angles( MathX.Deg( -MathF.Asin( dir.Y ) ), MathX.Deg( MathF.Atan2( -dir.X, -dir.Z ) ), 0f );
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
