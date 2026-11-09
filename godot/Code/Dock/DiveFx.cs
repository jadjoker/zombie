using Godot;

/// <summary>
/// Port of s&box DiveHands: first person during the dive, both robot arms held out low at the edges of the view, each
/// holding a spinning saw blade (faster with speed), swaying with steering and shuddering at speed. The gun hides.
/// Positions in s&box camera space (inches).
/// </summary>
public partial class DiveHands : Node, ICameraModifier
{
	readonly List<Node3D> blades = new();
	Node3D root;
	float spin;
	Vector2 sway;
	PlayerController controller;

	static readonly Color Metal = new( 0.42f, 0.45f, 0.5f );
	static readonly Color Dark = new( 0.16f, 0.17f, 0.2f );
	static readonly Color Accent = new( 0.95f, 0.55f, 0.12f );
	static readonly Color Steel = new( 0.82f, 0.85f, 0.9f );

	MeshInstance3D Part( Node3D parent, string name, Vector3 at, Vector3 size, Color c, bool sphere = false, Basis? rot = null )
	{
		var mesh = new MeshInstance3D
		{
			Name = name,
			Mesh = sphere ? Shapes.Sphere : Shapes.Box,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = c, Metallic = c == Steel ? 0.7f : 0.2f, Roughness = 0.5f },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		parent.AddChild( mesh );
		// Sizes are s&box x (forward), y (left), z (up); sphere sizes are radii.
		var godotSize = new Vector3( size.Y, size.Z, size.X ) * U.In * (sphere ? 2f : 1f);
		mesh.Transform = new Transform3D( (rot ?? Basis.Identity) * Basis.FromScale( godotSize ), U.Sbox( at ) );
		return mesh;
	}

	public override void _Ready()
	{
		Name = "DiveHands";
		controller = GetParent<PlayerController>();
		root = new Node3D { Name = "Dive Hands" };
		controller.Camera.AddChild( root );
		foreach ( var side in new[] { -1f, 1f } )
		{
			var shoulder = new Vector3( 2f, side * 19f, -22f );
			var hand = new Vector3( 36f, side * 25f, -18f );
			var arm = new Node3D { Name = "Arm" };
			root.AddChild( arm );
			var dirS = (hand - shoulder).Normalized();
			var len = (hand - shoulder).Length();
			var dir = U.Sbox( dirS ).Normalized();
			// Boxes are along local -Z (s&box forward), so turn each to the arm's direction.
			var armRot = Shapes.LookAt( dir );
			Part( arm, "Forearm", shoulder + dirS * len * 0.5f, new Vector3( len, 5f, 4.6f ), Metal, rot: armRot );
			Part( arm, "Plate", shoulder + dirS * len * 0.45f + new Vector3( 0, 0, 3.2f ), new Vector3( len * 0.6f, 3.5f, 1.2f ), Dark, rot: armRot );
			Part( arm, "Stripe", shoulder + dirS * len * 0.62f + new Vector3( 0, 0, 4.1f ), new Vector3( len * 0.2f, 3.7f, 0.5f ), Accent, rot: armRot );
			Part( arm, "Wrist", hand - dirS * 2f, new Vector3( 5f, 5f, 5f ), Dark, sphere: true );
			Part( arm, "Hand", hand + dirS * 3f, new Vector3( 7f, 7.5f, 5f ), Metal, rot: armRot );
			Part( arm, "Mount", hand + dirS * 6.5f, new Vector3( 4f, 2.5f, 3f ), Dark, rot: armRot );

			var blade = new Node3D { Name = "Blade" };
			arm.AddChild( blade );
			blade.Transform = new Transform3D( armRot * new Angles( 0f, side * 40f, 0f ).ToBasis(), U.Sbox( hand + dirS * 11f + new Vector3( 0, 0, 1f ) ) );
			var spinner = new Node3D { Name = "Spinner" };
			blade.AddChild( spinner );
			// The disc lies in the arm's plane (s&box x/z); spins about its left axis.
			Part( spinner, "Disc", Vector3.Zero, new Vector3( 11f, 0.5f, 11f ), Steel, sphere: true );
			Part( spinner, "Ring", Vector3.Zero, new Vector3( 7.5f, 0.7f, 7.5f ), new Color( 0.6f, 0.63f, 0.68f ), sphere: true );
			Part( spinner, "Hub", Vector3.Zero, new Vector3( 3.2f, 1.6f, 3.2f ), Accent, sphere: true );
			for ( var i = 0; i < 16; i++ )
			{
				var a = i * MathF.Tau / 16f;
				Part( spinner, "Tooth", new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * 5.6f, new Vector3( 1.8f, 0.5f, 1.3f ), Steel, rot: new Angles( -MathX.Deg( a ) + 30f, 0f, 0f ).ToBasis() );
			}
			for ( var i = 0; i < 3; i++ )
			{
				var a = i * MathF.Tau / 3f;
				Part( spinner, "Slot", new Vector3( MathF.Cos( a ), -0.2f, MathF.Sin( a ) ) * 3.6f, new Vector3( 2.6f, 0.8f, 0.9f ), Dark, rot: new Angles( -MathX.Deg( a ), 0f, 0f ).ToBasis() );
			}
			blades.Add( spinner );
		}
	}

	public void PostCameraSetup( Camera3D cam )
	{
		if ( root is null ) return;
		var show = !controller.ThirdPerson;
		root.Visible = show;
		if ( !show ) return;
		var dt = (float)GetProcessDeltaTime();
		var speed = DockHouse.DiveVelocity.Length();
		var input = Input.GetVector( "left", "right", "backward", "forward" );
		sway = sway.Lerp( new Vector2( -input.X, input.Y ), 1f - MathF.Exp( -6f * dt ) );
		var shudder = MathX.Clamp( (speed - 800f) / 1800f, 0f, 1f );
		var now = (float)Clock.Now;
		var jitter = new Vector3( 0f, MathF.Sin( now * 61f ) * 0.35f, MathF.Sin( now * 47f ) * 0.35f ) * shudder;
		var push = new Vector3( -3f * shudder, 0f, 0f );
		root.Position = U.Sbox( jitter + push + new Vector3( 0f, -sway.X * 2.5f, sway.Y * 1.5f ) );
		root.Basis = new Angles( sway.Y * 4f, 0f, -sway.X * 8f ).ToBasis();
		spin += dt * (900f + speed * 0.9f);
		foreach ( var b in blades ) b.Basis = new Angles( spin, 0f, 0f ).ToBasis();
	}

	public override void _Process( double delta )
	{
		foreach ( var w in controller.GetChildren().OfType<PlayerWeapon>() )
			if ( w.ViewModel is not null && w.ViewModel.Visible ) w.ViewModel.Visible = false;
	}

	public override void _ExitTree()
	{
		root?.QueueFree();
		foreach ( var w in controller.GetChildren().OfType<PlayerWeapon>() )
			if ( w.Active && w.ViewModel is not null ) w.ViewModel.Visible = true;
	}
}

/// <summary>
/// Port of s&box DiveRush: specks of silt hanging in the water that the dive tears past as streaks stretched along the
/// fall (with a clear hole straight ahead), plus a wider view and a steady shudder at speed.
/// </summary>
public partial class DiveRush : Node
{
	const int Count = 90;
	const float Radius = 520f;
	readonly List<(MeshInstance3D Mesh, Vector3 Pos)> specks = new();
	PlayerController controller;

	public override void _Ready()
	{
		Name = "DiveRush";
		controller = GetParent<PlayerController>();
		var root = GetTree().CurrentScene;
		var at = controller.Camera.GlobalPosition;
		for ( var i = 0; i < Count; i++ )
		{
			var mesh = new MeshInstance3D
			{
				Mesh = Shapes.Box,
				MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color( 0.7f, 0.92f, 1f ).Lerp( new Color( 0.45f, 0.75f, 0.85f ), Rng.Float() ), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			root.AddChild( mesh );
			specks.Add( (mesh, Place( at, Vector3.Down, Rng.Float( -Radius, Radius ) * U.In )) );
		}
	}

	static Vector3 Place( Vector3 cam, Vector3 dir, float along )
	{
		var side = new Vector2( Rng.Float( -1f, 1f ), Rng.Float( -1f, 1f ) ).LimitLength( 1f ) * Radius;
		if ( side.Length() < 160f ) side = side.Normalized() * Rng.Float( 160f, Radius );
		var basis = Shapes.LookAt( dir );
		return cam + dir * along + (basis.X * side.X + basis.Y * side.Y) * U.In;
	}

	public override void _Process( double delta )
	{
		var cam = controller.Camera;
		var vel = DockHouse.DiveVelocity;
		var speed = vel.Length();
		var dir = speed > 1f ? vel / speed : Vector3.Down;
		var stretch = 6f + speed * 0.07f;
		var look = Shapes.LookAt( dir );
		for ( var i = 0; i < specks.Count; i++ )
		{
			var (mesh, pos) = specks[i];
			var rel = pos - cam.GlobalPosition;
			var ahead = rel.Dot( dir );
			if ( ahead < -Radius * 0.4f * U.In || (rel - dir * ahead).Length() > Radius * 1.2f * U.In )
			{
				pos = Place( cam.GlobalPosition, dir, Rng.Float( Radius * 0.6f, Radius * 1.4f ) * U.In );
				specks[i] = (mesh, pos);
			}
			mesh.GlobalTransform = new Transform3D( look * Basis.FromScale( new Vector3( 0.6f, 0.6f, stretch ) * U.In ), pos );
		}
		var juice = controller.GetNodeOrNull<CameraJuice>( "CameraJuice" );
		var fast = MathX.Clamp( (speed - 600f) / 2000f, 0f, 1f );
		juice?.KickFov( 4f + 16f * fast );
		if ( fast > 0.05f ) juice?.Shake( 0.6f * fast * (float)delta );
	}

	public override void _ExitTree()
	{
		foreach ( var (mesh, _) in specks ) if ( IsInstanceValid( mesh ) ) mesh.QueueFree();
	}
}
