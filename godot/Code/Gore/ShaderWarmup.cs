using Godot;

/// <summary>
/// The first time an effect is drawn the GPU builds its pipelines, a stall of a tenth of a second or more (the
/// "stutter when things load"): gore, gibs and blasts each brought their own the first time they appeared in a fight.
/// At the start of a level (on the dock, behind the robot picker) this shows tiny copies of them just in front of the
/// camera for a few frames, then clears them away, so those stalls happen there instead.
/// </summary>
public partial class ShaderWarmup : Node
{
	int frames;
	readonly List<Node> made = new();
	PlayerController player;

	public static void Run( PlayerController player )
	{
		if ( AutoTest.Requested && !OS.GetCmdlineUserArgs().Contains( "--warmup" ) ) return;   // tests measure cold
		player.GetTree().CurrentScene.AddChild( new ShaderWarmup { Name = "Shader Warmup", player = player } );
	}

	public override void _Process( double delta )
	{
		frames++;
		if ( frames == 2 ) Show();
		if ( frames < 6 ) return;
		foreach ( var n in made ) if ( IsInstanceValid( n ) ) n.QueueFree();
		QueueFree();
	}

	void Show()
	{
		if ( player?.Camera is not { } camera ) { QueueFree(); return; }
		var at = camera.GlobalPosition - camera.GlobalBasis.Z * 1.6f;
		var scene = GetTree().CurrentScene;
		var before = new HashSet<Node>( scene.GetChildren() );
		var gore = GoreSystem.Get( this );
		var gibsBefore = new HashSet<Gib>( gore.Gibs );
		// The gib looks (shell, jelly, stone, mosaic), a blast, the blue blast, hit sparks and a muzzle flash.
		foreach ( var recipe in new[] { GibRecipes.Crab( new Color( 0.6f, 0.3f, 0.2f ), new Color( 0.9f, 0.8f, 0.6f ), new Color( 0.4f, 1f, 0.3f ) ),
			GibRecipes.Jelly( new Color( 0.5f, 0.3f, 0.9f ), new Color( 0.9f, 0.9f, 1f ), new Color( 0.4f, 1f, 0.3f ), false ),
			GibRecipes.RockyShark( new Color( 0.35f, 1f, 0.28f ) ) } )
			GibKit.Burst( recipe, at, Vector3.Up, 0.05f, 3, 0.01f );
		AlienBlast.Spawn( scene, at, 0.05f );
		BlueBlast.Spawn( scene, at, 0.1f );
		Shockwave.Spawn( scene, at, 0.1f, new Color( 0.3f, 0.6f, 1f ) );
		Fx.ImpactFlesh( at, -camera.GlobalBasis.Z, new Color( 0.4f, 1f, 0.3f ) );
		Fx.ImpactWorld( at, Vector3.Up );
		Fx.MuzzleFlash( at, -camera.GlobalBasis.Z, 0.05f );
		// Goo: a few tiny drops and a splat (they land as specks too small to see).
		gore.GooBurst( at, Vector3.Down, new Color( 0.4f, 1f, 0.3f ), 2, 20f, 0.05f );
		gore.GooSpray( at, Vector3.Down, 2, new Color( 0.4f, 1f, 0.3f ), 20f, 0.05f, 0.06f );
		// The GPU particle systems (sand and goo puffs, bubbles): their first use built them, over a tenth of a second.
		GpuFx.SandPuff( at, Vector3.Up * 0.1f, new Color( 0.4f, 1f, 0.3f ), 0.5f, 0.2f );
		GpuFx.Bubble( at, 0.2f, 0f, 0.2f );
		WaterBubble.Create( at, 0.3f, 0f, 0.2f );
		// Only the one-off effects go: the shared pools they made (sand puffs, bubbles, goo drops) are kept for the game.
		foreach ( var n in scene.GetChildren() ) if ( !before.Contains( n ) && n is AlienBlast or Shockwave or BlueBlast ) made.Add( n );
		foreach ( var g in gore.Gibs ) if ( !gibsBefore.Contains( g ) ) made.Add( g );
	}
}
