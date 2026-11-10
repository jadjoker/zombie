using Godot;
using System.Linq;

/// <summary>
/// Underwater caustics: the rippling light the surface throws on the sea floor. Every lit material in the level gets
/// an extra additive pass (assets/shaders/caustics_pass.gdshader) that draws the pattern in world space on upward
/// faces, fading with distance.
/// </summary>
public static class Caustics
{
	static ShaderMaterial pass;

	public static void AddTo( Node level )
	{
		pass ??= new ShaderMaterial { Shader = GD.Load<Shader>( "res://assets/shaders/caustics_pass.gdshader" ) };
		pass.SetShaderParameter( "surface_y", Sea.SurfaceY ?? 100000f );
		var done = new System.Collections.Generic.HashSet<Material>();
		foreach ( var mesh in level.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
		{
			if ( mesh.Mesh is null ) continue;
			if ( mesh.MaterialOverride is BaseMaterial3D over ) Add( over, done );
			for ( var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++ )
				if ( (mesh.GetSurfaceOverrideMaterial( i ) ?? mesh.Mesh.SurfaceGetMaterial( i )) is BaseMaterial3D m ) Add( m, done );
		}
	}

	static void Add( BaseMaterial3D material, System.Collections.Generic.HashSet<Material> done )
	{
		// Opaque, lit surfaces only (not glass, glows or effects), each shared material once.
		if ( !done.Add( material ) || material.Transparency != BaseMaterial3D.TransparencyEnum.Disabled || material.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded ) return;
		if ( material.NextPass is null ) material.NextPass = pass;
	}
}
