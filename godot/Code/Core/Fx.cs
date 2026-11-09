using Godot;

/// <summary>
/// Short-lived effects. The s&box game used Facepunch's stock particle prefabs (muzzle flash, impacts), which can't
/// come along; these are simple stand-ins until the particle pass.
/// </summary>
public static class Fx
{
	static Node Root => (Engine.GetMainLoop() as SceneTree)?.CurrentScene;

	/// <summary> Frees a node after a while. </summary>
	public static void Expire( Node node, float seconds )
	{
		var timer = node.GetTree().CreateTimer( seconds );
		timer.Timeout += () => { if ( GodotObject.IsInstanceValid( node ) ) node.QueueFree(); };
	}

	/// <summary> A muzzle flash: a bright burst and a light, riding along with the muzzle if given a parent. </summary>
	public static void MuzzleFlash( Transform3D at, float scale, Node3D parent = null )
	{
		var root = new Node3D();
		if ( parent is not null ) parent.AddChild( root ); else Root?.AddChild( root );
		root.GlobalTransform = at;
		var flash = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 2.5f * U.In * scale * 3f, Height = 5f * U.In * scale * 3f },
			MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color( 1f, 0.8f, 0.45f ), Transparency = BaseMaterial3D.TransparencyEnum.Alpha },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Scale = new Vector3( 1f, 1f, 2.2f ),
		};
		root.AddChild( flash );
		root.AddChild( new OmniLight3D { LightColor = new Color( 1f, 0.75f, 0.4f ), LightEnergy = 3f, OmniRange = 200f * U.In } );
		Expire( root, 0.05f );
	}

	/// <summary> A bullet hitting the world: a quick spark and a small dark mark that fades. </summary>
	public static void ImpactWorld( Vector3 at, Vector3 normal )
	{
		var root = Root;
		if ( root is null ) return;
		var mark = new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 1.2f * U.In, BottomRadius = 1.2f * U.In, Height = 0.05f * U.In },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color( 0.05f, 0.05f, 0.05f ), Roughness = 1f },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		root.AddChild( mark );
		var up = normal.SafeNormal() == Vector3.Zero ? Vector3.Up : normal.Normalized();
		var side = MathF.Abs( up.Dot( Vector3.Up ) ) > 0.99f ? Vector3.Right : Vector3.Up.Cross( up ).Normalized();
		mark.GlobalTransform = new Transform3D( new Basis( side, up, side.Cross( up ) ), at + up * 0.02f * U.In );
		Expire( mark, 8f );
		Spark( at, up, new Color( 1f, 0.85f, 0.5f ) );
	}

	/// <summary> A hit on something alive: a splash of its colour. </summary>
	public static void ImpactFlesh( Vector3 at, Vector3 facing, Color color ) => Spark( at, facing, color, 4f );

	static void Spark( Vector3 at, Vector3 direction, Color color, float size = 2.5f )
	{
		var root = Root;
		if ( root is null ) return;
		var spark = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = size * U.In, Height = size * 2f * U.In },
			MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = color },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		root.AddChild( spark );
		spark.GlobalPosition = at + direction * size * U.In;
		var tween = spark.CreateTween();
		tween.TweenProperty( spark, "scale", Vector3.One * 0.1f, 0.12f );
		tween.TweenCallback( Callable.From( spark.QueueFree ) );
	}
}
