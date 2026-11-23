using Godot;

/// <summary>
/// Third person, while health comes back: a handful of tiny bristlenose plecos swim in and latch onto the robot,
/// sucking at its plates (mouths pulsing, tails beating), and little pink hearts pop up off them and float away. They
/// let go and dart off once health is full or the robot is hit. Only drawn in third person.
/// </summary>
public partial class HealPlecos : Node3D
{
	sealed class Fish
	{
		public Node3D Node, Tail, Mouth;
		public float Height, Angle, Reach, Phase, Grow;
		public bool Leaving;
		public Vector3 DriftOff;
	}

	/// <summary> Where on the robot they latch: height above the feet (m) and how far out the body is there. </summary>
	static readonly (float Height, float Reach)[] Spots = { (1.32f, 0.24f), (1.18f, 0.25f), (1.02f, 0.22f), (0.86f, 0.19f), (0.62f, 0.12f), (0.42f, 0.11f), (1.42f, 0.2f) };
	public const int Count = 7;
	/// <summary> Drawn this much bigger than life (a 9 cm fish on a robot), so they read from the third person camera. </summary>
	public const float Size = 1.7f;

	PlayerController controller;
	PlayerSurvivor survivor;
	readonly List<Fish> fish = new();
	TimeSince sinceHeart;
	static ImageTexture heartTexture;
	static StandardMaterial3D heartMaterial;

	/// <summary> For tests: how many are on the robot now, and hearts popped so far. </summary>
	public int Attached => fish.Count( f => !f.Leaving && f.Grow > 0.5f );
	public static int Hearts;

	public override void _Ready()
	{
		Name = "HealPlecos";
		controller = GetParent<PlayerController>();
		TopLevel = true;
	}

	public override void _Process( double delta )
	{
		var dt = (float)delta;
		survivor ??= controller.GetNodeOrNull<PlayerSurvivor>( "PlayerSurvivor" );
		var show = controller.SeenFromOutside && survivor is { Healing: true } && !DevFlags.FreeCam;
		GlobalPosition = controller.GlobalPosition;
		GlobalBasis = new Angles( 0f, controller.EyeAngles.Yaw, 0f ).ToBasis();

		// Come on as a group, a few at a time; let go all at once.
		if ( show && fish.Count( f => !f.Leaving ) < Count && (fish.Count == 0 || fish[^1].Grow > 0.25f) ) Latch( fish.Count( f => !f.Leaving ) );
		if ( !show ) foreach ( var f in fish ) if ( !f.Leaving ) { f.Leaving = true; f.DriftOff = Rng.InSphere( 1f ) + Vector3.Up * 0.6f; }

		var t = (float)Clock.Now;
		for ( var i = fish.Count - 1; i >= 0; i-- )
		{
			var f = fish[i];
			if ( !IsInstanceValid( f.Node ) ) { fish.RemoveAt( i ); continue; }
			if ( f.Leaving )
			{
				f.Grow -= dt * 2.5f;
				f.Node.Position += f.DriftOff * dt * 1.5f;
				if ( f.Grow <= 0f ) { f.Node.QueueFree(); fish.RemoveAt( i ); continue; }
			}
			else
			{
				f.Grow = MathF.Min( 1f, f.Grow + dt * 3f );
				// Sitting on the plate, nudging round a little as it feeds.
				var a = f.Angle + MathF.Sin( t * 0.7f + f.Phase ) * 0.06f;
				var outward = new Vector3( MathF.Sin( a ), 0f, MathF.Cos( a ) );
				var swimIn = (1f - f.Grow) * 0.35f;
				f.Node.Position = outward * (f.Reach + 0.012f + swimIn) + Vector3.Up * (f.Height + MathF.Sin( t * 1.3f + f.Phase ) * 0.008f);
				// Belly on the robot: its up points out, its head up the body, tilted a little either way.
				var up = outward;
				var forward = (Vector3.Up + new Vector3( MathF.Cos( a ), 0f, -MathF.Sin( a ) ) * MathF.Sin( f.Phase ) * 0.5f).Normalized();
				var right = forward.Cross( up ).Normalized();
				f.Node.Basis = new Basis( right, up, -forward );
			}
			f.Node.Scale = Vector3.One * MathF.Max( 0.01f, f.Grow ) * Size;
			f.Tail.Rotation = new Vector3( 0f, MathF.Sin( t * 13f + f.Phase * 3f ) * 0.45f, 0f );
			var suck = 1f + 0.25f * MathF.Max( 0f, MathF.Sin( t * 6f + f.Phase ) );
			f.Mouth.Scale = new Vector3( suck, 1f, suck );
		}

		// Little hearts popping up off the feeding fish.
		if ( show && Attached > 0 && sinceHeart > 0.22f )
		{
			sinceHeart = 0f;
			var on = fish.Where( f => !f.Leaving && f.Grow > 0.5f ).ElementAt( Rng.Int( 0, Attached - 1 ) );
			PopHeart( on.Node.GlobalPosition + on.Node.GlobalBasis.Y * 0.05f );
		}
	}

	void Latch( int index )
	{
		var spot = Spots[index % Spots.Length];
		var f = new Fish
		{
			Height = spot.Height + Rng.Float( -0.04f, 0.04f ),
			Reach = spot.Reach,
			Angle = index * 2.4f + Rng.Float( -0.4f, 0.4f ),
			Phase = Rng.Float( 0f, MathF.Tau ),
		};
		(f.Node, f.Tail, f.Mouth) = BuildPleco( this );
		f.Node.Scale = Vector3.One * 0.01f;
		fish.Add( f );
	}

	/// <summary>
	/// A bristlenose pleco about 9 cm long (its own space: +Y its back, -Z its head): a flat dark olive body dotted
	/// pale, a broad flat head with the soft bristles on its snout, the round sucker mouth underneath, a tall spiny
	/// dorsal fin, wide pectoral fins, and a tail fin that beats.
	/// </summary>
	static (Node3D Root, Node3D Tail, Node3D Mouth) BuildPleco( Node3D parent )
	{
		var root = new Node3D { Name = "Pleco" };
		parent.AddChild( root );
		var skin = Sculpt.Paint( new Color( 0.3f, 0.27f, 0.16f ), 0.7f );
		var belly = Sculpt.Paint( new Color( 0.33f, 0.3f, 0.2f ), 0.7f );
		var spot = Sculpt.Paint( new Color( 0.98f, 0.9f, 0.6f ), 0.6f );
		var fin = Sculpt.Paint( new Color( 0.16f, 0.15f, 0.1f ), 0.6f );
		var eye = Sculpt.Paint( new Color( 0.02f, 0.02f, 0.02f ), 0.15f );
		Sculpt.Blob( root, new Vector3( 0f, 0f, 0f ), new Vector3( 0.034f, 0.018f, 0.07f ), skin, name: "Body" );
		Sculpt.Blob( root, new Vector3( 0f, 0.002f, -0.028f ), new Vector3( 0.042f, 0.016f, 0.036f ), skin, name: "Head" );
		Sculpt.Blob( root, new Vector3( 0f, -0.006f, -0.005f ), new Vector3( 0.03f, 0.008f, 0.06f ), belly, name: "Belly" );
		for ( var k = 0; k < 10; k++ )
		{
			var x = (k % 3 - 1) * 0.011f;
			var z = -0.03f + k * 0.0065f;
			Sculpt.Blob( root, new Vector3( x, 0.009f, z ), Vector3.One * 0.006f, spot, name: "Spot" );
		}
		foreach ( var s in new[] { -1f, 1f } )
		{
			Sculpt.Blob( root, new Vector3( s * 0.013f, 0.008f, -0.036f ), Vector3.One * 0.006f, eye, name: "Eye" );
			// The bristles on the snout, the fish's namesake.
			for ( var k = 0; k < 3; k++ )
			{
				var foot = new Vector3( s * (0.006f + k * 0.006f), 0.006f, -0.044f + k * 0.004f );
				Sculpt.Cone( root, foot, foot + new Vector3( s * 0.004f, 0.006f, -0.003f ), 0.0015f, skin, 4, "Bristle" );
			}
			// Pectoral fins: wide, with a spine at the front.
			Sculpt.Block( root, new Vector3( s * 0.024f, -0.004f, -0.016f ), new Vector3( 0.026f, 0.002f, 0.02f ), fin, new Basis( Vector3.Up, -s * 0.5f ), "Fin" );
		}
		// The tall dorsal fin.
		Sculpt.Block( root, new Vector3( 0f, 0.02f, 0.0f ), new Vector3( 0.002f, 0.022f, 0.03f ), fin, new Basis( Vector3.Right, 0.35f ), "Dorsal" );
		var mouth = Sculpt.Joint( root, new Vector3( 0f, -0.009f, -0.034f ), "Mouth" );
		Sculpt.Blob( mouth, Vector3.Zero, new Vector3( 0.018f, 0.004f, 0.016f ), Sculpt.Paint( new Color( 0.45f, 0.35f, 0.3f ), 0.5f ), name: "Sucker" );
		var tail = Sculpt.Joint( root, new Vector3( 0f, 0f, 0.034f ), "Tail" );
		Sculpt.Block( tail, new Vector3( 0f, 0f, 0.014f ), new Vector3( 0.002f, 0.024f, 0.028f ), fin, name: "Caudal" );
		foreach ( var mesh in root.FindChildren( "*", "MeshInstance3D", true, false ).OfType<MeshInstance3D>() )
			mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		return (root, tail, mouth);
	}

	/// <summary> A small pink heart that pops up, swells, floats off and fades. </summary>
	void PopHeart( Vector3 at )
	{
		heartTexture ??= MakeHeart();
		heartMaterial ??= new StandardMaterial3D
		{
			AlbedoTexture = heartTexture, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, VertexColorUseAsAlbedo = true,
		};
		var heart = new MeshInstance3D { Name = "Heart", Mesh = new QuadMesh { Size = Vector2.One * 0.09f }, MaterialOverride = (Material)heartMaterial.Duplicate(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		GetTree().CurrentScene.AddChild( heart );
		heart.GlobalPosition = at;
		heart.Scale = Vector3.One * 0.2f;
		Hearts++;
		var material = (StandardMaterial3D)heart.MaterialOverride;
		var tween = heart.CreateTween();
		tween.TweenProperty( heart, "scale", Vector3.One * Rng.Float( 0.9f, 1.3f ), 0.15f ).SetTrans( Tween.TransitionType.Back ).SetEase( Tween.EaseType.Out );
		tween.Parallel().TweenProperty( heart, "global_position", at + Vector3.Up * Rng.Float( 0.3f, 0.45f ) + Rng.InSphere( 0.12f ), 0.9f ).SetEase( Tween.EaseType.Out );
		tween.Parallel().TweenProperty( material, "albedo_color", new Color( 1f, 1f, 1f, 0f ), 0.9f ).SetDelay( 0.35f );
		tween.TweenCallback( Callable.From( heart.QueueFree ) );
	}

	/// <summary> A heart drawn into a small texture: pink, a lighter shine, soft edged. </summary>
	static ImageTexture MakeHeart()
	{
		const int n = 48;
		var image = Image.CreateEmpty( n, n, false, Image.Format.Rgba8 );
		for ( var py = 0; py < n; py++ )
			for ( var px = 0; px < n; px++ )
			{
				var x = (px + 0.5f) / n * 2.6f - 1.3f;
				var y = 1.25f - (py + 0.5f) / n * 2.6f;
				var f = MathF.Pow( x * x + y * y - 1f, 3f ) - x * x * y * y * y;
				if ( f > 0f ) { image.SetPixel( px, py, new Color( 0f, 0f, 0f, 0f ) ); continue; }
				var edge = MathX.Clamp( -f * 6f, 0f, 1f );
				var shine = MathX.Clamp( 1f - ((x + 0.45f) * (x + 0.45f) + (y - 0.45f) * (y - 0.45f)) * 5f, 0f, 1f );
				var c = new Color( 1f, 0.32f, 0.55f ).Lerp( new Color( 1f, 0.8f, 0.88f ), shine * 0.7f );
				image.SetPixel( px, py, c with { A = edge } );
			}
		return ImageTexture.CreateFromImage( image );
	}
}
