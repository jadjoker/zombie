using Godot;

/// <summary>
/// Step 8, a real ragdoll: when the player dies the robot body becomes physics. A copy of the survivor model in its
/// last pose gets one rigid capsule per limb (pelvis, spine, head, upper and lower arms, thighs, shins) joined by
/// cone-twist joints, and every frame the bones follow their bodies (hands ride the forearms, feet the shins).
/// </summary>
public partial class RobotRagdoll : Node3D
{
	sealed class Limb
	{
		public string Bone, Child;
		public RigidBody3D Body;
		public Transform3D BodyToBone;
		public int Index;
	}

	Skeleton3D skeleton;
	readonly List<Limb> limbs = new();
	readonly List<(int Bone, Limb Rider, Transform3D Offset)> riders = new();

	// Limb, the bone its capsule reaches to, capsule radius (inches), parent limb.
	static readonly (string Bone, string Child, float Radius, string Parent)[] Layout =
	{
		("pelvis", "spine", 7f, null),
		("spine", "head", 8f, "pelvis"),
		("head", null, 6.5f, "spine"),
		("upper_L", "lower_L", 2.6f, "spine"), ("lower_L", "hand_L", 2.4f, "upper_L"),
		("upper_R", "lower_R", 2.6f, "spine"), ("lower_R", "hand_R", 2.4f, "upper_R"),
		("thigh_L", "shin_L", 3.2f, "pelvis"), ("shin_L", "foot_L", 2.8f, "thigh_L"),
		("thigh_R", "shin_R", 3.2f, "pelvis"), ("shin_R", "foot_R", 2.8f, "thigh_R"),
	};

	/// <summary> A ragdoll of this posed model (left as it is), thrown with this velocity (m/s). </summary>
	public static RobotRagdoll Create( Node3D posedModel, Vector3 velocity, IEnumerable<MeshInstance3D> paintedFrom )
	{
		var root = posedModel.GetTree().CurrentScene;
		var source = posedModel.FindChildren( "*", "Skeleton3D", true, false ).OfType<Skeleton3D>().First();
		var rag = new RobotRagdoll { Name = "Robot Ragdoll" };
		root.AddChild( rag );
		var copy = GD.Load<PackedScene>( "res://assets/models/characters/survivor/survivor.fbx" ).Instantiate<Node3D>();
		rag.AddChild( copy );
		copy.GlobalTransform = posedModel.GlobalTransform;
		rag.skeleton = copy.FindChildren( "*", "Skeleton3D", true, false ).OfType<Skeleton3D>().First();
		for ( var b = 0; b < source.GetBoneCount(); b++ ) rag.skeleton.SetBonePose( b, source.GetBonePose( b ) );
		// Same paint job.
		var meshes = copy.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>().ToList();
		var painted = paintedFrom.ToList();
		for ( var m = 0; m < meshes.Count && m < painted.Count; m++ )
			for ( var i = 0; i < meshes[m].Mesh.GetSurfaceCount(); i++ )
				meshes[m].SetSurfaceOverrideMaterial( i, painted[m].GetSurfaceOverrideMaterial( i ) );
		rag.Build( velocity );
		return rag;
	}

	Transform3D BoneWorld( int bone ) => skeleton.GlobalTransform * skeleton.GetBoneGlobalPose( bone );

	void Build( Vector3 velocity )
	{
		var byName = new Dictionary<string, Limb>();
		foreach ( var (bone, child, radius, parent) in Layout )
		{
			var index = skeleton.FindBone( bone );
			if ( index < 0 ) continue;
			var boneWorld = BoneWorld( index );
			var from = boneWorld.Origin;
			var to = child is not null && skeleton.FindBone( child ) is var ci and >= 0 ? BoneWorld( ci ).Origin : from + Vector3.Up * radius * 1.6f * U.In;
			var axis = to - from;
			var length = MathF.Max( axis.Length(), radius * U.In );
			var dir = axis.Length() > 1e-5f ? axis / axis.Length() : Vector3.Up;
			var basis = new Basis( new Quaternion( Vector3.Up, dir ) );
			var body = new RigidBody3D
			{
				Name = $"Rag {bone}",
				CollisionLayer = Layers.Gib,
				CollisionMask = Layers.World,
				Mass = bone is "pelvis" or "spine" ? 12f : bone == "head" ? 5f : 4f,
				LinearDamp = 0.2f,
				AngularDamp = 1.2f,
				LinearVelocity = velocity,
			};
			AddChild( body );
			body.GlobalTransform = new Transform3D( basis, from + axis * 0.5f );
			var shape = bone == "head"
				? (Shape3D)new SphereShape3D { Radius = radius * U.In }
				: new CapsuleShape3D { Radius = radius * U.In, Height = MathF.Max( length, radius * 2f * U.In ) };
			body.AddChild( new CollisionShape3D { Shape = shape } );
			var limb = new Limb { Bone = bone, Child = child, Body = body, Index = index, BodyToBone = body.GlobalTransform.AffineInverse() * boneWorld };
			limbs.Add( limb );
			byName[bone] = limb;

			if ( parent is not null && byName.TryGetValue( parent, out var up ) )
			{
				// Placed first, then given its two bodies: a joint anchors where it is when the bodies are set.
				var joint = new ConeTwistJoint3D();
				AddChild( joint );
				joint.GlobalTransform = new Transform3D( basis * new Basis( Vector3.Back, MathF.PI * 0.5f ), from );   // twist axis (X) along the limb
				var knee = bone.StartsWith( "shin" ) || bone.StartsWith( "lower" );
				joint.SetParam( ConeTwistJoint3D.Param.SwingSpan, MathX.Rad( knee ? 70f : 45f ) );
				joint.SetParam( ConeTwistJoint3D.Param.TwistSpan, MathX.Rad( knee ? 10f : 30f ) );
				joint.NodeA = up.Body.GetPath();
				joint.NodeB = body.GetPath();
			}
		}
		// Limbs overlap where they meet (and the arms cross the chest holding the gun): they only hit the world.
		foreach ( var a in limbs ) foreach ( var b in limbs ) if ( a != b ) a.Body.AddCollisionExceptionWith( b.Body );
		// Hands ride the forearms, feet the shins, keeping their pose relative to them.
		foreach ( var (bone, owner) in new[] { ("hand_L", "lower_L"), ("hand_R", "lower_R"), ("foot_L", "shin_L"), ("foot_R", "shin_R") } )
		{
			var index = skeleton.FindBone( bone );
			if ( index < 0 || !byName.TryGetValue( owner, out var rider ) ) continue;
			riders.Add( (index, rider, rider.Body.GlobalTransform.AffineInverse() * BoneWorld( index )) );
		}
		// A shove so it crumples rather than standing there.
		if ( byName.TryGetValue( "spine", out var chest ) ) chest.Body.ApplyCentralImpulse( (-chest.Body.GlobalBasis.Z * 0.5f + Rng.OnSphere().Flat() * 0.6f) * chest.Body.Mass * 1.5f );
	}

	public override void _Process( double delta )
	{
		if ( skeleton is null ) return;
		var inv = skeleton.GlobalTransform.AffineInverse();
		// Parents before children (the layout is in that order), so each bone's pose lands where its body is.
		foreach ( var limb in limbs )
			skeleton.SetBoneGlobalPose( limb.Index, inv * (limb.Body.GlobalTransform * limb.BodyToBone) );
		foreach ( var (bone, rider, offset) in riders )
			skeleton.SetBoneGlobalPose( bone, inv * (rider.Body.GlobalTransform * offset) );
	}
}
