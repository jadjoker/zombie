/// <summary>
/// First person during the dive: both robot arms thrust out ahead of you, each holding a spinning saw blade.
/// Replaces the gun view model while the dive lasts.
/// </summary>
public sealed class DiveHands : Component, PlayerController.IEvents
{
	readonly List<GameObject> blades = new();
	GameObject root;
	float spin;
	Vector2 sway;

	static readonly Color Metal = new( 0.42f, 0.45f, 0.5f );
	static readonly Color Dark = new( 0.16f, 0.17f, 0.2f );
	static readonly Color Accent = new( 0.95f, 0.55f, 0.12f );
	static readonly Color Steel = new( 0.82f, 0.85f, 0.9f );

	GameObject Part( GameObject parent, string name, Vector3 at, Vector3 size, Color c, bool sphere = false, Rotation? rot = null )
	{
		var go = new GameObject( parent, true, name );
		go.NetworkMode = NetworkMode.Never;
		go.LocalPosition = at;
		go.LocalRotation = rot ?? Rotation.Identity;
		go.LocalScale = sphere ? size * Shapes.SphereScale( 1f ) : Shapes.BoxScale( size );
		var r = go.AddComponent<ModelRenderer>();
		r.Model = sphere ? Shapes.Sphere : Shapes.Box;
		r.Tint = c;
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
		return go;
	}

	protected override void OnStart()
	{
		if ( IsProxy ) return;
		root = new GameObject( true, "Dive Hands" );
		root.NetworkMode = NetworkMode.Never;
		foreach ( var side in new[] { -1f, 1f } )
		{
			// Camera space: x forward, y left, z up. Arms run from below the shoulders out to the hands.
			var shoulder = new Vector3( 2f, side * 19f, -22f );
			var hand = new Vector3( 36f, side * 25f, -18f );
			var arm = new GameObject( root, true, "Arm" );
			arm.NetworkMode = NetworkMode.Never;
			var dir = (hand - shoulder).Normal;
			var len = (hand - shoulder).Length;
			var armRot = Rotation.LookAt( dir, Vector3.Up );
			Part( arm, "Forearm", shoulder + dir * len * 0.5f, new Vector3( len, 5f, 4.6f ), Metal, rot: armRot );
			Part( arm, "Plate", shoulder + dir * len * 0.45f + Vector3.Up * 3.2f, new Vector3( len * 0.6f, 3.5f, 1.2f ), Dark, rot: armRot );
			Part( arm, "Stripe", shoulder + dir * len * 0.62f + Vector3.Up * 4.1f, new Vector3( len * 0.2f, 3.7f, 0.5f ), Accent, rot: armRot );
			Part( arm, "Wrist", hand - dir * 2f, new Vector3( 5f, 5f, 5f ), Dark, sphere: true );
			Part( arm, "Hand", hand + dir * 3f, new Vector3( 7f, 7.5f, 5f ), Metal, rot: armRot );
			Part( arm, "Mount", hand + dir * 6.5f, new Vector3( 4f, 2.5f, 3f ), Dark, rot: armRot );

			// The saw: a disc in the plane of the arm (like a circular saw), teeth round the rim, spun about the hub.
			var blade = new GameObject( arm, true, "Blade" );
			blade.NetworkMode = NetworkMode.Never;
			blade.LocalPosition = hand + dir * 11f + Vector3.Up * 1f;
			blade.LocalRotation = armRot * Rotation.FromYaw( side * 40f );
			var spinner = new GameObject( blade, true, "Spinner" );
			spinner.NetworkMode = NetworkMode.Never;
			Part( spinner, "Disc", Vector3.Zero, new Vector3( 11f, 0.5f, 11f ), Steel, sphere: true );
			Part( spinner, "Ring", Vector3.Zero, new Vector3( 7.5f, 0.7f, 7.5f ), new Color( 0.6f, 0.63f, 0.68f ), sphere: true );
			Part( spinner, "Hub", Vector3.Zero, new Vector3( 3.2f, 1.6f, 3.2f ), Accent, sphere: true );
			for ( var i = 0; i < 16; i++ )
			{
				var a = i * MathF.PI * 2f / 16f;
				var p = new Vector3( MathF.Cos( a ), 0f, MathF.Sin( a ) ) * 5.6f;
				Part( spinner, "Tooth", p, new Vector3( 1.8f, 0.5f, 1.3f ), Steel, rot: Rotation.FromPitch( -a.RadianToDegree() + 30f ) );
			}
			for ( var i = 0; i < 3; i++ )
			{
				var a = i * MathF.PI * 2f / 3f;
				Part( spinner, "Slot", new Vector3( MathF.Cos( a ), -0.2f, MathF.Sin( a ) ) * 3.6f, new Vector3( 2.6f, 0.8f, 0.9f ), Dark, rot: Rotation.FromPitch( -a.RadianToDegree() ) );
			}
			blades.Add( spinner );
		}
	}

	void PlayerController.IEvents.PostCameraSetup( CameraComponent cam )
	{
		if ( IsProxy || !root.IsValid() ) return;
		var controller = GetComponent<PlayerController>();
		var show = controller.IsValid() && !controller.ThirdPerson;
		root.Enabled = show;
		if ( !show ) return;

		// Sway against steering, shudder with speed.
		var speed = DockHouse.DiveVelocity.Length;
		var input = Input.AnalogMove;
		sway = Vector2.Lerp( sway, new Vector2( input.y, input.x ), 1f - MathF.Exp( -6f * Time.Delta ) );
		var shudder = MathX.Clamp( (speed - 800f) / 1800f, 0f, 1f );
		var jitter = new Vector3( 0f, MathF.Sin( Time.Now * 61f ) * 0.35f, MathF.Sin( Time.Now * 47f ) * 0.35f ) * shudder;
		var push = Vector3.Backward * shudder * 3f;   // the water shoves the arms back as you speed up

		root.WorldPosition = cam.WorldPosition + cam.WorldRotation * (jitter + push + new Vector3( 0f, -sway.x * 2.5f, sway.y * 1.5f ));
		root.WorldRotation = cam.WorldRotation * Rotation.From( sway.y * 4f, 0f, -sway.x * 8f );

		spin += Time.Delta * (900f + speed * 0.9f);
		foreach ( var b in blades )
			if ( b.IsValid() ) b.LocalRotation = Rotation.FromPitch( spin );
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		// The gun stays away while the saws are out.
		foreach ( var w in GetComponents<PlayerWeapon>( true ) )
			if ( w.ViewModel.IsValid() && w.ViewModel.GameObject.Enabled ) w.ViewModel.GameObject.Enabled = false;
	}

	protected override void OnDestroy()
	{
		root?.Destroy();
		foreach ( var w in GetComponents<PlayerWeapon>( true ) )
			if ( w.Enabled && w.ViewModel.IsValid() ) w.ViewModel.GameObject.Enabled = true;
	}
}

/// <summary>
/// The rush of falling: specks of silt hanging still in the water, which the dive tears past as streaks
/// stretched along the fall, plus a wider view and a steady shudder at speed.
/// </summary>
public sealed class DiveRush : Component
{
	const int Count = 90;
	const float Radius = 520f;
	readonly List<(GameObject Go, Vector3 Pos)> specks = new();

	protected override void OnStart()
	{
		var cam = Scene.Camera;
		var at = cam.IsValid() ? cam.WorldPosition : WorldPosition;
		for ( var i = 0; i < Count; i++ )
		{
			var go = new GameObject( true, "Silt" );
			go.NetworkMode = NetworkMode.Never;
			var r = go.AddComponent<ModelRenderer>();
			r.Model = Shapes.Box;
			r.Tint = Color.Lerp( new Color( 0.7f, 0.92f, 1f ), new Color( 0.45f, 0.75f, 0.85f ), Game.Random.Float() );
			r.RenderType = ModelRenderer.ShadowRenderType.Off;
			specks.Add( (go, Place( at, Vector3.Down, Game.Random.Float( -Radius, Radius ) )) );
		}
	}

	static Vector3 Place( Vector3 cam, Vector3 dir, float along )
	{
		var side = Game.Random.VectorInCircle( Radius );
		// Keep a clear hole straight ahead so streaks fly past, not into, the face.
		if ( side.Length < 160f ) side = side.Normal * Game.Random.Float( 160f, Radius );
		var basis = Rotation.LookAt( dir );
		return cam + dir * along + basis.Left * side.x + basis.Up * side.y;
	}

	protected override void OnUpdate()
	{
		var cam = Scene.Camera;
		if ( !cam.IsValid() ) return;
		var vel = DockHouse.DiveVelocity;
		var speed = vel.Length;
		var dir = speed > 1f ? vel / speed : Vector3.Down;
		var stretch = 6f + speed * 0.07f;
		var look = Rotation.LookAt( dir );
		for ( var i = 0; i < specks.Count; i++ )
		{
			var (go, pos) = specks[i];
			if ( !go.IsValid() ) continue;
			var rel = pos - cam.WorldPosition;
			var ahead = Vector3.Dot( rel, dir );
			// Behind, or drifted out of the tube: put it back far ahead.
			if ( ahead < -Radius * 0.4f || (rel - dir * ahead).Length > Radius * 1.2f )
			{
				pos = Place( cam.WorldPosition, dir, Game.Random.Float( Radius * 0.6f, Radius * 1.4f ) );
				specks[i] = (go, pos);
			}
			go.WorldPosition = pos;
			go.WorldRotation = look;
			go.WorldScale = Shapes.BoxScale( new Vector3( stretch, 0.6f, 0.6f ) );
		}

		var juice = GetComponent<CameraJuice>();
		var fast = MathX.Clamp( (speed - 600f) / 2000f, 0f, 1f );
		juice?.KickFov( 4f + 16f * fast );
		if ( fast > 0.05f ) juice?.Shake( 0.6f * fast * Time.Delta );
	}

	protected override void OnDestroy()
	{
		foreach ( var (go, _) in specks ) go?.Destroy();
	}
}
