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
	readonly Dictionary<string, Limb> byBone = new();
	readonly Dictionary<string, ConeTwistJoint3D> joints = new();
	readonly HashSet<int> hidden = new();
	Material paint;
	/// <summary> Tests: limbs (and heads) torn off ragdolls. </summary>
	public static int Severed;
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
		rag.paint = painted.SelectMany( m => Enumerable.Range( 0, m.Mesh?.GetSurfaceCount() ?? 0 ).Select( i => m.GetSurfaceOverrideMaterial( i ) ?? m.Mesh.SurfaceGetMaterial( i ) ) ).FirstOrDefault( m => m is not null );
		rag.Build( velocity );
		return rag;
	}

	/// <summary> Where the body is (its pelvis): what the death camera watches. </summary>
	public Vector3 Centre => limbs.Count > 0 && IsInstanceValid( limbs[0].Body ) ? limbs[0].Body.GlobalPosition : GlobalPosition;

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
			body.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.On;
			body.GlobalTransform = new Transform3D( basis, from + axis * 0.5f );
			body.ResetPhysicsInterpolation();
			var shape = bone == "head"
				? (Shape3D)new SphereShape3D { Radius = radius * U.In }
				: new CapsuleShape3D { Radius = radius * U.In, Height = MathF.Max( length, radius * 2f * U.In ) };
			body.AddChild( new CollisionShape3D { Shape = shape } );
			var limb = new Limb { Bone = bone, Child = child, Body = body, Index = index, BodyToBone = body.GlobalTransform.AffineInverse() * boneWorld };
			limbs.Add( limb );
			byName[bone] = limb;
			byBone[bone] = limb;

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
				joints[bone] = joint;
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
			skeleton.SetBoneGlobalPose( limb.Index, Shown( limb.Index, inv * (limb.Body.GlobalTransform * limb.BodyToBone) ) );
		foreach ( var (bone, rider, offset) in riders )
			skeleton.SetBoneGlobalPose( bone, Shown( bone, inv * (rider.Body.GlobalTransform * offset) ) );
	}

	/// <summary> A torn-off part's bones are shrunk away on the body (its piece flies off on its own). </summary>
	Transform3D Shown( int bone, Transform3D pose ) => hidden.Contains( bone ) ? new Transform3D( pose.Basis.Orthonormalized().Scaled( Vector3.One * 0.001f ), pose.Origin ) : pose;

	static readonly (string Root, string[] Bones)[] Chains =
	{
		("upper_L", new[] { "upper_L", "lower_L", "hand_L" }), ("upper_R", new[] { "upper_R", "lower_R", "hand_R" }),
		("thigh_L", new[] { "thigh_L", "shin_L", "foot_L" }), ("thigh_R", new[] { "thigh_R", "shin_R", "foot_R" }),
		("head", new[] { "head" }),
	};

	/// <summary>
	/// The robot comes apart: `count` of its arms, legs and head are torn off (the head among them when `head`), each
	/// flying off as a piece of robot with a chrome joint and gushing oil, its socket on the body gushing too.
	/// </summary>
	public void BreakApart( int count, bool head, Vector3 push )
	{
		var order = Chains.Where( c => c.Root != "head" ).OrderBy( _ => Rng.Float() ).Take( Math.Max( 0, count - (head ? 1 : 0) ) ).ToList();
		if ( head ) order.Add( Chains.First( c => c.Root == "head" ) );
		var chrome = new StandardMaterial3D { AlbedoColor = new Color( 0.75f, 0.77f, 0.8f ), Metallic = 1f, Roughness = 0.2f };
		var dark = new StandardMaterial3D { AlbedoColor = new Color( 0.08f, 0.08f, 0.09f ), Roughness = 0.6f };
		var visor = new StandardMaterial3D { AlbedoColor = new Color( 1f, 0.45f, 0.85f ), EmissionEnabled = true, Emission = new Color( 1f, 0.45f, 0.85f ), EmissionEnergyMultiplier = 1.5f };
		var gore = GoreSystem.Get( this );
		foreach ( var (root, bones) in order )
		{
			if ( !byBone.TryGetValue( root, out var limb ) || !IsInstanceValid( limb.Body ) ) continue;
			Severed++;
			if ( joints.TryGetValue( root, out var joint ) && IsInstanceValid( joint ) ) joint.QueueFree();
			foreach ( var b in bones ) { var i = skeleton.FindBone( b ); if ( i >= 0 ) hidden.Add( i ); }
			// The piece: a robot limb in its paint on each of its bodies (hands and feet as blocks on the ends).
			foreach ( var b in bones )
			{
				if ( !byBone.TryGetValue( b, out var part ) ) continue;
				var shape = part.Body.GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.Shape;
				var radius = shape is CapsuleShape3D cap ? cap.Radius : shape is SphereShape3D sph ? sph.Radius : 0.06f;
				var length = shape is CapsuleShape3D cap2 ? cap2.Height : radius * 2f;
				if ( b == "head" )
				{
					part.Body.AddChild( new MeshInstance3D { Mesh = new SphereMesh { Radius = radius, Height = radius * 2.1f }, MaterialOverride = paint } );
					part.Body.AddChild( new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3( radius * 1.3f, radius * 0.45f, radius * 0.3f ) }, MaterialOverride = visor, Position = new Vector3( 0f, radius * 0.1f, -radius * 0.85f ) } );
				}
				else
				{
					part.Body.AddChild( new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = radius * 0.85f, BottomRadius = radius * 0.95f, Height = MathF.Max( length - radius, radius ) }, MaterialOverride = paint } );
					part.Body.AddChild( new MeshInstance3D { Mesh = new SphereMesh { Radius = radius * 0.9f, Height = radius * 1.8f }, MaterialOverride = chrome, Position = Vector3.Up * length * 0.5f } );
				}
				if ( b is "lower_L" or "lower_R" ) part.Body.AddChild( new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * radius * 2.2f }, MaterialOverride = dark, Position = Vector3.Up * (length * 0.5f + radius) } );
				if ( b is "shin_L" or "shin_R" ) part.Body.AddChild( new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3( radius * 2.4f, radius * 1.4f, radius * 3.4f ) }, MaterialOverride = dark, Position = Vector3.Up * (length * 0.5f + radius) } );
			}
			// The torn end on the piece and the socket on the body: chrome, sparks and oil gushing.
			var torn = limb.Body;
			var tornShape = torn.GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.Shape;
			var half = tornShape is CapsuleShape3D tc ? tc.Height * 0.5f : 0.08f;
			var end = new Node3D { Name = "Torn End", Position = Vector3.Down * half };
			torn.AddChild( end );
			end.AddChild( new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f }, MaterialOverride = chrome } );
			end.AddChild( new GooJet { Color = RobotOil.Oil, Lifetime = 2.5f, Strength = 1.2f } );
			if ( joint is not null && IsInstanceValid( joint ) && GetNodeOrNull( joint.NodeA ) is RigidBody3D parentBody )
			{
				var socket = new Node3D { Name = "Socket" };
				parentBody.AddChild( socket );
				socket.GlobalPosition = joint.GlobalPosition;
				socket.AddChild( new GooJet { Color = RobotOil.Oil, Lifetime = 3.5f, Strength = 1.4f } );
				socket.AddChild( new GooOoze { Color = RobotOil.Oil, Lifetime = 8f, Interval = 0.2f } );
			}
			var at = torn.GlobalPosition;
			gore?.GooBurst( at, push.SafeNormal() + Vector3.Up, RobotOil.Oil, 14, 300f, 1.1f );
			Fx.ImpactWorld( at, Rng.OnSphere() );
			// Off it goes.
			foreach ( var b in bones )
				if ( byBone.TryGetValue( b, out var p ) ) p.Body.ApplyCentralImpulse( (push + Rng.OnSphere() * 3f + Vector3.Up * 3f) * p.Body.Mass * 0.6f );
		}
		Sfx.Play( "glass_break", GlobalPosition, 0.6f, 0.6f );
		Sfx.Play( "mine_tink", GlobalPosition, 0.8f, 0.7f );
	}
}
