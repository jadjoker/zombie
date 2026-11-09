using Godot;

/// <summary>
/// Port of the s&box GoreSystem (the creature goo half): goo droplets, splats on the world that go on wet and dry
/// darker, flung blobs of flesh, and a budget on loose pieces. The s&box splats used Facepunch decal assets; here a
/// soft round decal stands in. Sounds are hooks (the s&box splat sound was a cloud asset).
/// </summary>
public partial class GoreSystem : Node
{
	public float GibLifetime = 20f;
	public int MaxGibs = 120;
	public float FadeTime = 1.2f;
	public float DecalLifetime = 40f;
	public int MaxDecals = 200;

	static GoreSystem instance;
	readonly List<Gib> gibs = new();
	readonly Queue<Node3D> decals = new();
	static Texture2D splatTexture;

	/// <summary> The gore system for the running scene, made on first use. </summary>
	public static GoreSystem Get( Node context = null )
	{
		if ( instance is not null && IsInstanceValid( instance ) ) return instance;
		var tree = Engine.GetMainLoop() as SceneTree;
		instance = new GoreSystem { Name = "GoreSystem" };
		tree?.CurrentScene?.AddChild( instance );
		return instance;
	}

	Node Root => GetTree().CurrentScene;

	public void Register( Gib gib )
	{
		gibs.RemoveAll( g => !IsInstanceValid( g ) );
		gibs.Add( gib );
		var excess = gibs.Count( g => !g.IsFading ) - MaxGibs;
		foreach ( var old in gibs.Where( g => !g.IsFading ).Take( Math.Max( 0, excess ) ).ToList() ) old.BeginFade();
	}

	public void Squelch( Vector3 position, float loudness = 1f, float pitch = 1f ) { }

	// ---- Goo ----

	public void GooSplat( Vector3 position, Vector3 normal, float scale, Color color )
	{
		splatTexture ??= MakeSplatTexture();
		var size = scale * 24f * U.In;
		var decal = new Decal
		{
			TextureAlbedo = splatTexture,
			Size = new Vector3( size, 10f * U.In, size ),
			Modulate = color.Lerp( Colors.White, 0.25f ),
			UpperFade = 0.3f,
			LowerFade = 0.3f,
			CullMask = Layers.World,
		};
		Root.AddChild( decal );
		var up = normal.SafeNormal() == Vector3.Zero ? Vector3.Up : normal.Normalized();
		var side = MathF.Abs( up.Dot( Vector3.Up ) ) > 0.99f ? Vector3.Right : Vector3.Up.Cross( up ).Normalized();
		var basis = new Basis( side, up, side.Cross( up ) ).Rotated( up, Rng.Float( 0f, MathF.Tau ) );
		decal.GlobalTransform = new Transform3D( basis, position + up * 1f * U.In );
		// Goes on bright and wet, then dries darker.
		var tween = decal.CreateTween();
		tween.TweenProperty( decal, "modulate", color.Lerp( Colors.Black, 0.35f ), 6f );
		Fx.Expire( decal, DecalLifetime );
		decals.Enqueue( decal );
		while ( decals.Count > MaxDecals )
		{
			var old = decals.Dequeue();
			if ( IsInstanceValid( old ) ) old.QueueFree();
		}
	}

	public void GooSpray( Vector3 origin, Vector3 direction, int drops, Color color, float reach = 180f, float sizeMin = 0.5f, float sizeMax = 1.3f, float sag = 1f )
	{
		for ( var i = 0; i < drops; i++ )
		{
			var dir = (direction.SafeNormal() + Rng.InSphere( 1f ) * 0.7f + Vector3.Down * Rng.Float( 0.2f, 0.9f ) * sag).SafeNormal();
			if ( TraceWorld( origin, origin + dir * reach * U.In ) is { } hit )
				GooSplat( hit.Position, hit.Normal, Rng.Float( sizeMin, sizeMax ), color );
		}
	}

	public void GooDrip( Vector3 position, Color color )
	{
		if ( TraceWorld( position, position + Vector3.Down * 200f * U.In ) is { } hit )
			GooSplat( hit.Position, hit.Normal, Rng.Float( 0.25f, 0.55f ), color );
	}

	/// <summary> A burst of goo droplets (speed in s&box units/s). </summary>
	public void GooBurst( Vector3 position, Vector3 direction, Color color, int count = 10, float speed = 260f, float size = 1f )
	{
		for ( var i = 0; i < count; i++ )
		{
			var velocity = (direction.SafeNormal() * 0.8f + Rng.InSphere( 1f )).SafeNormal() * speed * Rng.Float( 0.4f, 1.2f ) + Vector3.Up * Rng.Float( 30f, 140f );
			GooDroplet.Spawn( position, velocity * U.In, color, size * Rng.Float( 0.6f, 1.4f ) );
		}
	}

	/// <summary> A blob of flesh flung loose (radius in inches). </summary>
	public Gib ThrowBlob( Vector3 origin, Vector3 direction, Color color, Color goo, float radius )
	{
		var velocity = (direction.SafeNormal() + Rng.InSphere( 1f ) * 0.7f).SafeNormal() * Rng.Float( 100f, 220f ) + Vector3.Up * Rng.Float( 70f, 170f );
		var piece = new Node3D { Name = "Blob" };
		Root.AddChild( piece );
		piece.GlobalPosition = origin;
		Shapes.Part( piece, "Skin", Vector3.Zero, Shapes.Ellipsoid( radius * Rng.Float( 0.8f, 1.3f ), radius * Rng.Float( 0.7f, 1.1f ), radius * Rng.Float( 0.6f, 1f ) ), color );
		return Gib.Attach( piece, this, radius, 12f, 1.5f, velocity * U.In, goo );
	}

	public (Vector3 Position, Vector3 Normal)? TraceWorld( Vector3 from, Vector3 to )
	{
		var tr = Trace.Ray( (Node3D)GetTree().CurrentScene, from, to, Layers.World );
		if ( !tr.Hit ) return null;
		return (tr.HitPosition, tr.Normal);
	}

	static Texture2D MakeSplatTexture()
	{
		// A ragged round splat: a soft disc with a few lobes, white so the decal's colour tints it.
		const int size = 64;
		var image = Image.CreateEmpty( size, size, false, Image.Format.Rgba8 );
		var lobes = new List<(Vector2 C, float R)> { (new Vector2( 32, 32 ), 18f) };
		var random = new Random( 7 );
		for ( var i = 0; i < 7; i++ )
		{
			var a = i / 7f * MathF.Tau + (float)random.NextDouble();
			var d = 12f + (float)random.NextDouble() * 10f;
			lobes.Add( (new Vector2( 32 + MathF.Cos( a ) * d, 32 + MathF.Sin( a ) * d ), 4f + (float)random.NextDouble() * 6f) );
		}
		for ( var y = 0; y < size; y++ )
			for ( var x = 0; x < size; x++ )
			{
				var a = 0f;
				foreach ( var (c, r) in lobes ) a = MathF.Max( a, MathX.Clamp( (r - new Vector2( x, y ).DistanceTo( c )) / 2f, 0f, 1f ) );
				image.SetPixel( x, y, new Color( 1, 1, 1, a * 0.95f ) );
			}
		return ImageTexture.CreateFromImage( image );
	}
}
