using Godot;

/// <summary>
/// The Trident-Bot's melee kit in first person: the trident as a spear in the hands of the Blaster's rig (its gun
/// taken off, as for the trident shooter), and an energy shield on the left that comes up with right click. The
/// thrust and the shield are posed in code (Pose), on top of the rig's own idle and draw animations.
/// Gun space is the Blender rifle's: inches, +X forward, +Z up, the grip at the origin.
/// </summary>
public static class SpearModel
{
	static readonly Basis GunToBone = new( new Vector3( 0f, 1f, 0f ), new Vector3( -1f, 0f, 0f ), new Vector3( 0f, 0f, 1f ) );

	/// <summary> Puts the spear on the rig's gun bone. </summary>
	public static void Attach( Skeleton3D skeleton )
	{
		if ( skeleton.FindBone( "gun" ) < 0 ) return;
		var onGun = new BoneAttachment3D { Name = "Spear Bone", BoneName = "gun" };
		skeleton.AddChild( onGun );
		var body = new Node3D { Name = "Spear", Transform = new Transform3D( GunToBone.Scaled( Vector3.One * 0.01f ), Vector3.Zero ) };
		onGun.AddChild( body );
		BuildSpear( body );
	}

	/// <summary> A long black shaft with yellow bands and a purple grip, a gold crossbar and three barbed prongs with glowing tips. </summary>
	public static void BuildSpear( Node3D spear )
	{
		var black = TridentModel.BlackPaint;
		var yellow = TridentModel.YellowPaint;
		var purple = TridentModel.PurplePaint;
		var glow = TridentModel.GreenGlow;
		Sculpt.Limb( spear, new Vector3( -22f, 0f, 0.6f ), new Vector3( 42f, 0f, 0.6f ), 0.62f, 0.55f, black, "Shaft" );
		Sculpt.Limb( spear, new Vector3( -3f, 0f, 0.6f ), new Vector3( 4f, 0f, 0.6f ), 0.8f, 0.8f, purple, "Grip" );
		foreach ( var x in new[] { -18f, -10f, 12f, 24f, 36f } )
			Sculpt.Limb( spear, new Vector3( x - 0.4f, 0f, 0.6f ), new Vector3( x + 0.4f, 0f, 0.6f ), 0.72f, 0.72f, yellow, "Band" );
		Sculpt.Blob( spear, new Vector3( -22.5f, 0f, 0.6f ), Vector3.One * 1.8f, yellow, name: "Butt" );
		// The head: a crossbar, then the middle prong and two curving side prongs, each barbed and glowing at the tip.
		Sculpt.Limb( spear, new Vector3( 42f, -4.6f, 0.6f ), new Vector3( 42f, 4.6f, 0.6f ), 0.75f, 0.75f, yellow, "Crossbar" );
		Sculpt.Limb( spear, new Vector3( 41f, 0f, 0.6f ), new Vector3( 56f, 0f, 0.6f ), 0.6f, 0.3f, yellow, "Prong" );
		Sculpt.Cone( spear, new Vector3( 56f, 0f, 0.6f ), new Vector3( 59.5f, 0f, 0.6f ), 0.75f, glow, 6, "Tip" );
		foreach ( var side in new[] { -1f, 1f } )
		{
			var root = new Vector3( 42f, side * 4.4f, 0.6f );
			var bend = new Vector3( 47f, side * 4.9f, 0.6f );
			var end = new Vector3( 52f, side * 4f, 0.6f );
			Sculpt.Limb( spear, root, bend, 0.55f, 0.45f, yellow, "Prong" );
			Sculpt.Limb( spear, bend, end, 0.45f, 0.3f, yellow, "Prong" );
			Sculpt.Cone( spear, end, end + new Vector3( 3f, -side * 0.6f, 0f ), 0.6f, glow, 6, "Tip" );
			Sculpt.Cone( spear, bend, bend + new Vector3( -1.6f, side * 1.4f, 0f ), 0.4f, yellow, 5, "Barb" );
		}
	}

	/// <summary>
	/// Each frame: the thrust drives the spear forward and a little across (alternate sides), and the shield rises
	/// from low on the left to cover the middle of the view. thrust and raise go 0 to 1.
	/// </summary>
	public static void Pose( ViewModel vm, float thrust, bool left, float raise, float dt )
	{
		if ( vm.GetChildCount() == 0 ) return;
		if ( vm.GetChild( 0 ) is Node3D model )
		{
			var across = left ? -1f : 1f;
			model.Position = new Vector3( -0.06f * thrust * across, 0.03f * thrust - 0.05f * raise, -0.42f * thrust + 0.04f * raise );
			model.Rotation = new Vector3( -0.08f * thrust, 0.12f * thrust * across, 0f );
		}
		var shield = vm.GetNodeOrNull<Node3D>( "Energy Shield" ) ?? BuildShield( vm );
		shield.Visible = true;
		var ease = raise * raise * (3f - 2f * raise);
		shield.Position = new Vector3( -0.34f, -0.42f, -0.32f ).Lerp( new Vector3( -0.07f, -0.06f, -0.4f ), ease );
		shield.Rotation = new Vector3( MathX.Lerp( -0.7f, 0f, ease ), MathX.Lerp( 0.6f, 0.08f, ease ), MathX.Lerp( 0.4f, 0f, ease ) );
		var t = (float)Clock.Now;
		if ( shield.GetNodeOrNull<MeshInstance3D>( "Glass" ) is { MaterialOverride: StandardMaterial3D glass } )
			glass.AlbedoColor = glass.AlbedoColor with { A = 0.16f + 0.2f * ease + 0.04f * MathF.Sin( t * 7f ) };
	}

	/// <summary> A round energy shield: a sea-green glass disc ringed in gold, a honeycomb of glowing cells over it. </summary>
	static Node3D BuildShield( ViewModel vm )
	{
		var shield = new Node3D { Name = "Energy Shield" };
		vm.AddChild( shield );
		var glass = new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.35f, 1f, 0.8f, 0.2f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			EmissionEnabled = true, Emission = new Color( 0.3f, 1f, 0.75f ), EmissionEnergyMultiplier = 0.8f, Roughness = 0.1f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		var gold = Sculpt.Paint( new Color( 1f, 0.78f, 0.25f ), 0.25f, 0.85f );
		var cell = new StandardMaterial3D
		{
			AlbedoColor = new Color( 0.5f, 1f, 0.85f ), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			EmissionEnabled = true, Emission = new Color( 0.45f, 1f, 0.8f ), EmissionEnergyMultiplier = 1.6f,
		};
		const float r = 0.16f;
		shield.AddChild( new MeshInstance3D { Name = "Glass", Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.006f, RadialSegments = 32 }, MaterialOverride = glass, RotationDegrees = new Vector3( 90f, 0f, 0f ) } );
		shield.AddChild( new MeshInstance3D { Name = "Rim", Mesh = new TorusMesh { InnerRadius = r - 0.008f, OuterRadius = r + 0.012f, Rings = 32, RingSegments = 8 }, MaterialOverride = gold, RotationDegrees = new Vector3( 90f, 0f, 0f ) } );
		// Honeycomb: thin glowing hexagon outlines in rings round the middle.
		const float hex = 0.034f;
		for ( var q = -4; q <= 4; q++ )
			for ( var s = -4; s <= 4; s++ )
			{
				var centre = new Vector2( hex * 1.5f * q, hex * MathF.Sqrt( 3f ) * (s + q * 0.5f) );
				if ( centre.Length() > r - hex * 0.9f ) continue;
				for ( var k = 0; k < 6; k++ )
				{
					var a0 = MathF.Tau * k / 6f;
					var a1 = MathF.Tau * (k + 1) / 6f;
					var p0 = centre + new Vector2( MathF.Cos( a0 ), MathF.Sin( a0 ) ) * hex * 0.95f;
					var p1 = centre + new Vector2( MathF.Cos( a1 ), MathF.Sin( a1 ) ) * hex * 0.95f;
					Sculpt.Limb( shield, new Vector3( p0.X, p0.Y, -0.004f ), new Vector3( p1.X, p1.Y, -0.004f ), 0.0018f, 0.0018f, cell, "Cell" );
				}
			}
		Sculpt.Blob( shield, new Vector3( 0f, 0f, 0.012f ), new Vector3( 0.05f, 0.05f, 0.02f ), gold, name: "Boss" );
		foreach ( var mesh in shield.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			mesh.Layers = ViewModel.ViewLayer;
			mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		}
		return shield;
	}
}
