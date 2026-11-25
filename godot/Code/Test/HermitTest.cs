using Godot;

/// <summary>
/// The hermit crab squatter (--hermit): every one of the seven shells builds, cut into pieces; shots at the shell take
/// little off the crab and knock pieces off a few at a time near where they land, shots through a hole reach the crab,
/// and when the shell's health is gone what is left falls apart and the naked crab panics, then comes back faster and
/// soft. With --shots (a window): each shell close up, a shell part crumbled, the shell falling apart.
/// </summary>
public partial class AutoTest
{
	async Task HermitTest()
	{
		DockHouse.Current?.SkipDive();
		await Wait( 0.5f );
		var d = WaveDirector.Current;
		d.SpawningPaused = true;
		AllyCommander.KeepAlly = false;
		foreach ( var ally in SeaSpider.All.Where( s => !s.IsEnemy ).ToList() ) ally.QueueFree();
		var survivor = player.GetNode<PlayerSurvivor>( "PlayerSurvivor" );
		survivor.Health.Invulnerable = true;
		await WaitForNav();
		var (open, yaw) = OpenLane( 14f );
		player.GlobalPosition = open + Vector3.Up * 0.1f;
		player.Velocity = Vector3.Zero;
		player.EyeAngles = new Angles( 0f, yaw, 0f );
		var ahead = new Angles( 0f, yaw, 0f ).Forward;
		var side = ahead.Cross( Vector3.Up ).Normalized();
		Check( WaveDirector.Spawners.ContainsKey( WaveDirector.EnemyKind.HermitCrab ), "the wave director cannot spawn hermit crabs" );
		Check( d.HermitShare > 0f, "no crabs come as hermit crabs" );

		// Each shell, one crab each, frozen in a row.
		DevFlags.FreezeEnemies = true;
		var crabs = new List<HermitCrab>();
		for ( var k = 0; k < HermitCrab.ShellCount; k++ )
		{
			var at = Nav.Closest( player, open + ahead * 5f + side * (k - (HermitCrab.ShellCount - 1) * 0.5f) * 2.8f );
			var c = HermitCrab.Spawn( GetTree().CurrentScene, at, 200f );
			c.Shell = (HermitCrab.ShellKind)k;
			c.Hue = k / (float)HermitCrab.ShellCount;
			c.GlobalBasis = Shapes.LookAt( -ahead );
			crabs.Add( c );
		}
		await Wait( 0.5f );
		var counts = crabs.Select( c => $"{c.Shell} {c.PieceCount}" );
		GD.Print( $"[autotest] hermit: shells {string.Join( ", ", counts )}" );
		Check( crabs.All( c => c.ShellOn && c.PieceCount >= 12 ), "a hermit crab's shell did not build in pieces" );
		Check( crabs.Select( c => c.Shell ).Distinct().Count() == HermitCrab.ShellCount, "not every shell was built" );

		if ( shots is not null )
		{
			HideHud();
			Transform3D? view = null;
			player.CameraOverride = _ => view;
			player.ThirdPerson = true;
			async Task From( string name, Node3D target, Vector3 offset )
			{
				var centre = target.GlobalPosition + Vector3.Up * 0.45f;
				var eye = centre + target.GlobalBasis * offset;
				view = new Transform3D( Shapes.LookAt( centre - eye ), eye );
				await Wait( 0.15f );
				await Shot( name );
			}
			foreach ( var c in crabs ) await From( $"hermit_{c.Shell.ToString().ToLowerInvariant()}", c, new Vector3( 1.6f, 1.2f, 1.2f ) );
			foreach ( var c in crabs.Take( 2 ) ) await From( $"hermit_{c.Shell.ToString().ToLowerInvariant()}_front", c, new Vector3( 0.8f, 0.7f, -1.9f ) );
			var eye = open + Vector3.Up * 3.2f - ahead * 2.5f;
			view = new Transform3D( Shapes.LookAt( open + ahead * 5f - eye ), eye );
			await Wait( 0.15f );
			await Shot( "hermit_row" );
		}

		// Shots at the shell: a few pieces at a time come off near where it lands, the crab barely hurt.
		var crab = crabs[0];
		var startHealth = crab.Health.Current;
		var off0 = HermitCrab.PiecesOff;
		var pieces = crab.PieceCount;
		var steps = new List<int>();
		var shell = CreaturePart.All.FirstOrDefault( p => p.Creature == crab && p.Index == 1 );
		Check( shell is not null, "the hermit crab's shell can't be shot" );
		var aim = crab.GlobalPosition + Vector3.Up * 0.6f - ahead * 0.25f;
		for ( var i = 0; i < 6 && crab.ShellOn; i++ )
		{
			var shooter = aim - ahead * 3f;
			var hitAt = crab.PieceNear( aim ) ?? aim;
			crab.ReceiveShot( 1, 30f, ahead, hitAt, shooter, survivor, true );
			steps.Add( pieces - crab.PiecesLeft );
			await Wait( 0.05f );
		}
		var shellLost = startHealth - crab.Health.Current;
		GD.Print( $"[autotest] hermit: pieces off after each of 6 shots: {string.Join( ", ", steps )} of {pieces}; crab health lost {shellLost:0.0} of {6 * 30f}" );
		Check( steps.Count > 0 && steps[^1] > 0, "shooting the shell knocked no pieces off" );
		Check( steps.Count > 0 && steps[^1] < pieces, "the whole shell fell off at once" );
		Check( steps.Zip( steps.Skip( 1 ) ).All( p => p.Second >= p.First ), "pieces came back on" );
		Check( shellLost <= 6 * 30f * HermitCrab.ShellPass + 0.5f, $"shots on the shell took {shellLost:0} off the crab" );
		Check( HermitCrab.PiecesOff > off0, "no shell pieces were thrown loose" );

		if ( shots is not null )
		{
			var eye = crab.GlobalPosition + Vector3.Up * 0.9f + crab.GlobalBasis * new Vector3( 0.6f, 0f, 1.6f );
			player.CameraOverride = _ => new Transform3D( Shapes.LookAt( crab.GlobalPosition + Vector3.Up * 0.4f - eye ), eye );
			await Wait( 0.6f );
			await Shot( "hermit_crumbled" );
		}

		// A hole: a shot where pieces came off goes through to the crab.
		var holes = HermitCrab.ThroughHoles;
		var hpBefore = crab.Health.Current;
		crab.ReceiveShot( 1, 20f, ahead, aim, aim - ahead * 3f, survivor, true );
		var through = HermitCrab.ThroughHoles > holes;
		GD.Print( $"[autotest] hermit: a shot at the hole went through {through}, took {hpBefore - crab.Health.Current:0.0}" );
		Check( through && hpBefore - crab.Health.Current > 20f * HermitCrab.ShellPass + 1f, "a shot through a hole in the shell did not reach the crab" );

		// The shell's health runs out: the rest falls apart and the crab panics, then comes back faster and soft.
		var other = crabs[6];
		var speed = other.MoveSpeed;
		var offs = HermitCrab.ShellsOff;
		DevFlags.FreezeEnemies = false;
		other.TestCrumble( 0.5f, other.GlobalPosition + Vector3.Up * 0.6f );
		var half = other.PiecesLeft;
		other.TestCrumble( 0f, other.GlobalPosition + Vector3.Up * 0.6f );
		await Wait( 0.1f );
		GD.Print( $"[autotest] hermit: {other.Shell} at half health has {half} of {other.PieceCount} pieces; at none, shell on {other.ShellOn}, panicking {other.Panicking}, speed {speed:0} to {other.MoveSpeed:0}" );
		Check( half > 0 && half < other.PieceCount, "a half broken shell is not half there" );
		Check( !other.ShellOn && HermitCrab.ShellsOff > offs && other.PiecesLeft == 0, "the shell did not fall apart when its health ran out" );
		Check( other.Panicking && other.MoveSpeed > speed * 1.5f, "the naked hermit crab did not panic and speed up" );
		var fleeFrom = other.GlobalPosition.DistanceTo( player.GlobalPosition );
		await Wait( 1f );
		var fleeTo = other.GlobalPosition.DistanceTo( player.GlobalPosition );
		GD.Print( $"[autotest] hermit: panicking it went from {fleeFrom:0.0} to {fleeTo:0.0} m from the player" );
		Check( fleeTo > fleeFrom + 0.4f, "the panicking crab did not run away" );
		DevFlags.FreezeEnemies = true;
		if ( shots is not null )
		{
			var eye = other.GlobalPosition + Vector3.Up * 0.9f + ahead * -1.6f + side * 0.5f;
			player.CameraOverride = _ => new Transform3D( Shapes.LookAt( other.GlobalPosition + Vector3.Up * 0.3f - eye ), eye );
			await Wait( 0.5f );
			await Shot( "hermit_naked" );
			player.CameraOverride = null;
			player.ThirdPerson = false;
			ShowHud();
		}
		// Naked, a body shot hurts more than one on a crab with its shell.
		var soft = other.Health.Current;
		other.ReceiveShot( 0, 10f, ahead, other.GlobalPosition + Vector3.Up * 0.35f, open, survivor, true );
		var naked = soft - other.Health.Current;
		GD.Print( $"[autotest] hermit: a 10 body shot on the naked crab took {naked:0.0}" );
		Check( naked > 10.5f, "the naked hermit crab is not soft" );

		DevFlags.FreezeEnemies = false;
		foreach ( var c in crabs ) if ( IsInstanceValid( c ) && !c.IsDead ) c.Health.Kill();
		for ( var t = 0f; t < 4f && JunkCrab.All.Count > 0; t += 0.1f ) await Wait( 0.1f );
		foreach ( var c in JunkCrab.All ) GD.Print( $"[autotest] hermit: left over {c.Name} dead {c.IsDead}" );
		Check( JunkCrab.All.Count == 0, $"{JunkCrab.All.Count} hermit crabs left" );

		GD.Print( "[autotest] done" );
		GetTree().Quit();
	}
}
