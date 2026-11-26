using Godot;

/// <summary> The Abyss's hip lantern on the third person robot: a small brass lantern hanging off the right hip. </summary>
public partial class PlayerRig
{
	readonly List<Node> lanternAttachments = new();
	Node3D lanternGear;
	public Node3D LanternGlass { get; private set; }

	/// <summary> Hangs the lantern on (true) or takes it off; false while the skeleton isn't built yet. </summary>
	public bool SetLantern( bool on )
	{
		if ( !on )
		{
			foreach ( var a in lanternAttachments ) if ( IsInstanceValid( a ) ) a.QueueFree();
			lanternAttachments.Clear();
			lanternGear = null;
			LanternGlass = null;
			return true;
		}
		if ( lanternGear is not null && IsInstanceValid( lanternGear ) ) return true;
		if ( skeleton is null ) return false;
		lanternGear = OnBone( "pelvis", new Vector3( 1.5f, -9.5f, -1.5f ), lanternAttachments );
		if ( lanternGear is null ) return false;
		var brass = GunKit.Paint( new Color( 0.78f, 0.56f, 0.22f ), 0.35f, 0.85f );
		var dark = GunKit.Paint( new Color( 0.16f, 0.12f, 0.08f ), 0.6f, 0.4f );
		var glass = new StandardMaterial3D { AlbedoColor = new Color( 1f, 0.85f, 0.55f ), EmissionEnabled = true, Emission = new Color( 1f, 0.72f, 0.35f ), EmissionEnergyMultiplier = 4f, Roughness = 0.2f };
		var swing = new Node3D { Name = "Lantern" };
		lanternGear.AddChild( swing );
		GunKit.Rod( swing, Vector3.Zero, new Vector3( 0f, 0f, -1.5f ), 0.25f, dark );                  // the hook
		GunKit.Rod( swing, new Vector3( 0f, 0f, -1.4f ), new Vector3( 0f, 0f, -2.4f ), 1.9f, brass );   // the top cap
		GunKit.Rod( swing, new Vector3( 0f, 0f, -2.3f ), new Vector3( 0f, 0f, -1.2f ), 0.9f, brass );   // the chimney
		LanternGlass = GunKit.Ball( swing, new Vector3( 0f, 0f, -4.6f ), 1.85f, glass );
		for ( var i = 0; i < 4; i++ )
		{
			var a = i * MathF.PI * 0.5f + MathF.PI * 0.25f;
			var o = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f ) * 1.75f;
			GunKit.Rod( swing, o + new Vector3( 0f, 0f, -2.3f ), o + new Vector3( 0f, 0f, -6.9f ), 0.22f, brass );
		}
		GunKit.Rod( swing, new Vector3( 0f, 0f, -6.8f ), new Vector3( 0f, 0f, -7.6f ), 2f, brass );     // the base
		foreach ( var m in swing.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() ) m.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		return true;
	}
}
