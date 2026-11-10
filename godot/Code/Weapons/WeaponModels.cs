using Godot;

/// <summary>
/// Loads the Blender-made first-person models (gun, arms and hands in one rig) the s&box game used, straight from
/// their FBX files: the base model plus one FBX per animation (name@anim.fbx), merged into one AnimationPlayer the
/// way the s&box .vmdl listed them. Also places the model the way s&box saw it.
/// </summary>
public static class WeaponModels
{
	/// <summary> Animations per model, with whether they loop (from the .vmdl files). </summary>
	static readonly Dictionary<string, (string Name, bool Loop)[]> animations = new()
	{
		["rifle"] = new[] { ("idle", true), ("fire", false), ("draw", false), ("reload", false), ("sprint", true) },
		["shotgun"] = new[] { ("idle", true), ("fire", false), ("draw", false), ("sprint", true), ("reload_start", false), ("reload_shell", false), ("reload_hold", true), ("reload_end", false) },
		["axe"] = new[] { ("swing_r", false), ("swing_l", false) },
	};

	/// <summary> Muzzle attachments from the .vmdl: bone and offset in the bone's space (inches). </summary>
	static readonly Dictionary<string, (string Bone, Vector3 Offset)> muzzles = new()
	{
		["rifle"] = ("gun", new Vector3( 0f, 36.2f, 3f )),
		["shotgun"] = ("gun", new Vector3( 0f, 40f, 5.2f )),
	};

	static readonly Dictionary<string, AnimationLibrary> libraries = new();

	/// <summary>
	/// A new instance of a weapon model under a holder node. The holder's space is s&box model space: the FBX was
	/// exported at 0.01 per inch with s&box forward, so it's scaled to metres and turned to face Godot's -Z.
	/// </summary>
	public static (Node3D Root, AnimationPlayer Player, Skeleton3D Skeleton) Create( string name )
	{
		var scene = GD.Load<PackedScene>( $"res://assets/models/weapons/{name}/{name}.fbx" );
		var model = scene.Instantiate<Node3D>();
		model.Name = "Model";
		model.Scale = Vector3.One * 2.54f;                 // 0.01 m per exported inch -> 0.0254 m per inch
		model.RotationDegrees = new Vector3( 0f, 180f, 0f ); // the FBX comes in with s&box forward on +X after the rig turn; face Godot -Z

		var holder = new Node3D { Name = name };
		holder.AddChild( model );

		var skeleton = model.FindChildren( "*", "Skeleton3D", true, false ).OfType<Skeleton3D>().FirstOrDefault();
		var player = new AnimationPlayer { Name = "AnimationPlayer" };
		model.AddChild( player );
		player.RootNode = player.GetPathTo( model );
		player.AddAnimationLibrary( "", Library( name ) );

		foreach ( var mesh in model.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
			mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

		if ( skeleton is not null && muzzles.TryGetValue( name, out var muzzle ) )
		{
			var attach = new BoneAttachment3D { Name = "MuzzleBone", BoneName = muzzle.Bone };
			skeleton.AddChild( attach );
			attach.AddChild( new Node3D { Name = "Muzzle", Position = muzzle.Offset * 0.01f } );
		}
		return (holder, player, skeleton);
	}

	public static Node3D Muzzle( Node3D root ) => root.FindChild( "Muzzle", true, false ) as Node3D;

	static AnimationLibrary Library( string name )
	{
		if ( libraries.TryGetValue( name, out var cached ) ) return cached;
		var library = new AnimationLibrary();
		foreach ( var (anim, loop) in animations.GetValueOrDefault( name, Array.Empty<(string, bool)>() ) )
		{
			var path = $"res://assets/models/weapons/{name}/{name}@{anim}.fbx";
			if ( !ResourceLoader.Exists( path ) ) continue;
			var source = GD.Load<PackedScene>( path ).Instantiate();
			var sourcePlayer = source.FindChildren( "*", "AnimationPlayer", true, false ).OfType<AnimationPlayer>().FirstOrDefault();
			var list = sourcePlayer?.GetAnimationList();
			if ( list is { Length: > 0 } )
			{
				var clip = (Animation)sourcePlayer.GetAnimation( list[0] ).Duplicate();
				clip.LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
				library.AddAnimation( anim, clip );
			}
			source.Free();
		}
		libraries[name] = library;
		return library;
	}
}
