using Godot;

/// <summary>
/// --moveposes: the third person poses for the robots' mouse 4 moves. Coral-Bot's burrow sinks the body into the
/// sand and the eruption throws the arms up; Hook-Bot's grapple reaches along the line; Magnet-Bot's pulse flings the
/// arms wide; Plunger-Bot's sling flies head first and lands in a flat belly flop; Pinball-Bot's roll tucks into a low
/// ball that turns as it goes. With --shots (a window) a side view of each.
/// </summary>
public partial class AutoTest
{
	PlayerRig Rig => player.GetChildren().OfType<PlayerRig>().First();

	/// <summary> A side view of the robot (shots only), the HUD hidden. </summary>
	async Task SideShot( string name )
	{
		if ( shots is null ) return;
		var hidden = GetTree().Root.FindChildren( "*", "", true, false ).OfType<CanvasLayer>().Where( c => c.Visible ).ToList();
		foreach ( var cl in hidden ) cl.Visible = false;
		var body = player.GlobalPosition + Vector3.Up * 0.7f;
		var face = new Angles( 0f, player.EyeAngles.Yaw, 0f ).Forward;
		var from = body + face.Cross( Vector3.Up ) * 3.2f + face * 1.2f + Vector3.Up * 0.6f;
		var at = new Transform3D( Basis.LookingAt( (body - from).Normalized(), Vector3.Up ), from );
		player.CameraOverride = _ => at;
		await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
		await ToSignal( GetTree(), SceneTree.SignalName.ProcessFrame );
		await Shot( name );
		player.CameraOverride = null;
		foreach ( var cl in hidden ) cl.Visible = true;
	}

	async Task<Arena> PoseArena( RobotClass robot )
	{
		await BotSetup( robot );
		var (spot, ahead, side) = BotArena();
		var a = new Arena { Spot = spot, Ahead = ahead, Side = side };
		player.ThirdPerson = true;
		await Face( a, a.Spot + a.Ahead * 10f + Vector3.Up * 1.5f );
		await Wait( 0.4f );
		return a;
	}

	float Above( string bone ) => Rig.BoneAt( bone ).Y - player.GlobalPosition.Y;

	async Task MovePosesTest()
	{
		var jet = player.GetNode<Jetpack>( "Jetpack" );
		// A window renders slowly here: slow time down for the pictures so a short move's beat lands in a frame.
		if ( shots is not null ) Engine.TimeScale = 0.25;

		// ---- Coral-Bot: the burrow and the eruption ----
		var a = await PoseArena( RobotClass.CoralBot );
		var standing = Above( "head" );
		jet.TestBurrow();
		await Wait( 0.7f );
		var sunk = Above( "head" );
		var hidden = Rig.GunPutAway;
		await SideShot( "pose_burrow" );
		jet.TestBurrow();
		await Wait( 0.12f );
		var handsUp = MathF.Min( Above( "hand_L" ), Above( "hand_R" ) ) - Above( "head" );
		await SideShot( "pose_erupt" );
		GD.Print( $"[autotest] moveposes: head {standing:0.00} m standing, {sunk:0.00} m burrowed; hands {handsUp:0.00} m over the head erupting" );
		Check( standing > 1.2f && sunk < 0.6f && hidden, "the burrow did not sink the robot into the sand" );
		Check( handsUp > 0.1f, "the eruption did not throw the arms up" );
		await Wait( 1f );

		// ---- Hook-Bot: the grapple reaches along the line ----
		a = await PoseArena( RobotClass.HookBot );
		var mark = Crab( a, a.Spot + a.Ahead * 14f, 2000f );
		await Wait( 0.8f );
		await Face( a, mark.GlobalPosition + Vector3.Up * 0.4f );
		jet.TestGrapple();
		await Wait( 0.1f );
		var reach = (Rig.BoneAt( "hand_L" ) - Rig.BoneAt( "upper_L" )).Normalized().Dot( jet.SurgeDirection.Normalized() );
		var zipping = jet.Zipping;
		await SideShot( "pose_grapple" );
		GD.Print( $"[autotest] moveposes: grapple reach {reach:0.00} (zipping {zipping})" );
		Check( !zipping || reach > 0.6f, "the grapple's free hand does not reach along the line" );
		Clear( a );
		await Wait( 1f );

		// ---- Magnet-Bot: the pulse flings the arms wide ----
		a = await PoseArena( RobotClass.MagnetBot );
		jet.TestPulse();
		await Wait( 0.1f );
		var spread = Rig.BoneAt( "hand_L" ).DistanceTo( Rig.BoneAt( "hand_R" ) );
		await SideShot( "pose_pulse" );
		GD.Print( $"[autotest] moveposes: pulse hands {spread:0.00} m apart" );
		Check( spread > 1f, "the pulse did not fling the arms wide" );
		await Wait( 1f );

		// ---- Plunger-Bot: superman on the bungee, then the belly flop ----
		a = await PoseArena( RobotClass.PlungerBot );
		var flops = PlungerFx.Flops;
		jet.TestSling();
		await Wait( 0.3f );
		var along = Rig.BodyUp.Dot( player.Velocity.Normalized() );
		await SideShot( "pose_sling" );
		for ( var i = 0; i < 100 && PlungerFx.Flops == flops; i++ ) await Wait( 0.02f );
		await Wait( 0.12f );
		var flat = MathF.Abs( Rig.BodyUp.Y );
		await SideShot( "pose_flop" );
		GD.Print( $"[autotest] moveposes: sling head along the flight {along:0.00}; flop body up {flat:0.00}" );
		Check( along > 0.6f, "the sling does not fly head first" );
		Check( PlungerFx.Flops > flops && flat < 0.45f, "the sling did not land in a belly flop" );
		await Wait( 1f );

		// ---- Pinball-Bot: a low ball that turns as it rolls ----
		a = await PoseArena( RobotClass.PinballBot );
		Stand( a );
		jet.TestRoll();
		var ups = new List<Vector3>();
		var lowest = 9f;
		for ( var i = 0; i < 16 && jet.Rolling; i++ )
		{
			player.GlobalPosition = Nav.Closest( player, a.Spot + a.Ahead * (0.25f * i) ) + Vector3.Up * 0.1f;
			await Wait( 0.05f );
			if ( i >= 6 ) { ups.Add( Rig.BodyUp ); lowest = MathF.Min( lowest, Above( "head" ) ); }
			if ( i == 10 ) await SideShot( "pose_roll" );
		}
		var turned = ups.Count > 1 ? ups.Min( u => u.Dot( ups[0] ) ) : 1f;
		var away = Rig.GunPutAway;
		if ( jet.Rolling ) jet.TestRoll();
		await Wait( 0.6f );
		var back = Above( "head" );
		GD.Print( $"[autotest] moveposes: roll head as low as {lowest:0.00} m, turned to {turned:0.00}; {back:0.00} m after" );
		Check( lowest < 1.1f && away, "the roll did not tuck the robot into a low ball" );
		Check( turned < 0.5f, "the ball did not turn as it rolled" );
		Check( back > 1.2f, "the robot did not stand back up after the roll" );
		player.ThirdPerson = false;
		Engine.TimeScale = 1;
		Done();
	}
}
