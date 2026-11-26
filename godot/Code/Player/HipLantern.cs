using Godot;

/// <summary>
/// In the Abyss every robot carries a lantern at its right hip (user's design): a warm light that shows the floor and
/// walls round it (shadowed for this machine's own robot), and the lantern itself on the third person robot.
/// Kept by <see cref="AbyssMood"/>; taken off when the light comes back.
/// </summary>
public partial class HipLantern : Node3D
{
	public const string NodeName = "Hip Lantern";
	public const float Range = 13f, Energy = 1.6f;
	public static readonly Color Warm = new( 1f, 0.78f, 0.48f );
	static readonly List<HipLantern> all = new();
	public static IReadOnlyList<HipLantern> All => all;

	PlayerController controller;
	public OmniLight3D Light { get; private set; }
	bool hung;
	float phase;

	public static void Ensure( PlayerController c )
	{
		if ( c.GetNodeOrNull<HipLantern>( NodeName ) is not null ) return;
		c.AddChild( new HipLantern { Name = NodeName, controller = c } );
	}

	public static void RemoveAll()
	{
		foreach ( var l in all.ToList() ) if ( IsInstanceValid( l ) ) { l.GetParent()?.RemoveChild( l ); l.Free(); }
		all.Clear();
	}

	public override void _Ready()
	{
		TopLevel = true;
		all.Add( this );
		phase = Rng.Float( 0f, 10f );
		Light = new OmniLight3D
		{
			Name = "Lantern Light",
			LightColor = Warm,
			LightEnergy = Energy,
			OmniRange = Range,
			OmniAttenuation = 0.9f,
			ShadowEnabled = !controller.IsRemote,
			LightVolumetricFogEnergy = 0.35f,
			LightSpecular = 0.3f,
		};
		AddChild( Light );
		Place();
	}

	public override void _ExitTree()
	{
		all.Remove( this );
		if ( IsInstanceValid( controller ) ) controller.GetNodeOrNull<PlayerRig>( "PlayerRig" )?.SetLantern( false );
	}

	void Place()
	{
		var yaw = MathX.Rad( controller.EyeAngles.Yaw );
		var forward = new Vector3( -MathF.Sin( yaw ), 0f, -MathF.Cos( yaw ) );
		var right = new Vector3( MathF.Cos( yaw ), 0f, -MathF.Sin( yaw ) );
		GlobalPosition = controller.GlobalPosition + right * 0.32f + forward * 0.12f + Vector3.Up * 0.95f;
	}

	public override void _Process( double delta )
	{
		if ( !IsInstanceValid( controller ) ) { QueueFree(); return; }
		if ( !hung ) hung = controller.GetNodeOrNull<PlayerRig>( "PlayerRig" )?.SetLantern( true ) ?? true;
		// Where the lantern is: on the robot's hip when it's drawn, else where the hip would be.
		var rig = controller.GetNodeOrNull<PlayerRig>( "PlayerRig" );
		if ( rig?.LanternGlass is { } glass && IsInstanceValid( glass ) && glass.IsVisibleInTree() ) GlobalPosition = glass.GlobalPosition + Vector3.Up * 0.05f;
		else Place();
		// A flame's flicker, small.
		var t = (float)Clock.Now + phase;
		var alive = controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" )?.IsAlive ?? true;
		Light.Visible = alive;
		Light.LightEnergy = Energy * (1f + 0.05f * MathF.Sin( t * 7.3f ) + 0.03f * MathF.Sin( t * 17.1f ));
	}
}
