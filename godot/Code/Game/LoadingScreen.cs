using Godot;

/// <summary>
/// The teleporter's loading screen: black, with a little drawn postcard of the dock (the hut, the pier, the diving
/// board, the blue hole with its reef ring, gulls and a lazy sea), where you are going, a tip, and a loading bar.
/// Drawn in code, so it needs no image files. Shown while the next area is built.
/// </summary>
public partial class LoadingScreen : Control
{
	public string Destination = "";
	/// <summary> 0 to 1: how full the bar is (eased toward this). </summary>
	public float Progress;
	float shown;
	float time;
	string tip;

	static readonly string[] Tips =
	{
		"Shoot the glowing bits. They are usually the weak points.",
		"The Kraken's dome soaks up shots. Break it, then aim for the brain.",
		"Poseidon's whirlpool carries you to him. Keep moving sideways.",
		"Cut a serpent in half and the tail end stops chasing you.",
		"Buy stations come with you through the teleporter.",
		"Health keeps healing even after the upgrade cap.",
		"Rocky Shark plates turn bullets. The green holes do not.",
		"Somewhere down there, someone is selling cheese.",
	};

	public override void _Ready()
	{
		SetAnchorsPreset( LayoutPreset.FullRect );
		MouseFilter = MouseFilterEnum.Ignore;
		tip = Tips[Rng.Int( 0, Tips.Length - 1 )];
	}

	public override void _Process( double delta )
	{
		time += (float)delta;
		// Sized to the window directly (a Control under a CanvasLayer can sit at zero size before its first layout).
		var screen = GetViewportRect().Size;
		if ( Size != screen ) { Position = Vector2.Zero; Size = screen; }
		shown = MathX.Lerp( shown, Progress, 1f - MathF.Exp( -5f * (float)delta ) );
		QueueRedraw();
	}

	public override void _Draw()
	{
		var size = Size;
		if ( size.X < 64f || size.Y < 64f ) return;   // not laid out yet (its first frame, or a headless run)
		DrawRect( new Rect2( Vector2.Zero, size ), Colors.Black );
		var font = ThemeDB.FallbackFont;
		var scale = MathF.Min( size.X / 1280f, size.Y / 720f );
		var w = 720f * scale;
		var h = 405f * scale;
		var pic = new Rect2( (size.X - w) * 0.5f, size.Y * 0.16f, w, h );

		DrawPostcard( pic, scale );
		// A white border, like a photo.
		DrawRect( pic.Grow( 8f * scale ), Colors.White, false, 6f * scale );

		var textTop = pic.End.Y + 54f * scale;
		DrawString( font, new Vector2( 0f, size.Y * 0.16f - 34f * scale ), "T E L E P O R T I N G", HorizontalAlignment.Center, size.X, Math.Max( 8, (int)(30 * scale) ), new Color( 0.75f, 1f, 0.8f ) );
		DrawString( font, new Vector2( 0f, textTop ), Destination, HorizontalAlignment.Center, size.X, Math.Max( 8, (int)(36 * scale) ), Colors.White );
		// The bar.
		var bw = w * 0.8f;
		var bar = new Rect2( (size.X - bw) * 0.5f, textTop + 26f * scale, bw, 10f * scale );
		DrawRect( bar, new Color( 1f, 1f, 1f, 0.15f ) );
		DrawRect( new Rect2( bar.Position, new Vector2( bar.Size.X * MathX.Clamp( shown, 0f, 1f ), bar.Size.Y ) ), new Color( 0.45f, 1f, 0.55f ) );
		DrawString( font, new Vector2( 0f, bar.End.Y + 44f * scale ), tip, HorizontalAlignment.Center, size.X, Math.Max( 8, (int)(20 * scale) ), new Color( 0.7f, 0.75f, 0.8f ) );
	}

	/// <summary> The dock in the afternoon: sky, sun, the sea with its blue hole and reef, the pier, the hut, gulls. </summary>
	void DrawPostcard( Rect2 r, float scale )
	{
		Vector2 P( float x, float y ) => r.Position + new Vector2( x * r.Size.X, y * r.Size.Y );
		var horizon = 0.46f;
		// Sky: bands from deep blue to a warm glow at the horizon.
		const int bands = 24;
		for ( var i = 0; i < bands; i++ )
		{
			var t = i / (float)(bands - 1);
			var c = new Color( 0.18f, 0.42f, 0.78f ).Lerp( new Color( 1f, 0.82f, 0.6f ), t * t );
			DrawRect( new Rect2( P( 0f, horizon * i / bands ), new Vector2( r.Size.X, r.Size.Y * horizon / bands + 1f ) ), c );
		}
		DrawCircle( P( 0.78f, 0.2f ), 34f * scale, new Color( 1f, 0.93f, 0.7f ) );
		DrawCircle( P( 0.78f, 0.2f ), 46f * scale, new Color( 1f, 0.9f, 0.6f, 0.25f ) );
		// Clouds.
		foreach ( var (x, y, s) in new[] { (0.15f, 0.12f, 1f), (0.42f, 0.2f, 0.7f), (0.6f, 0.08f, 0.8f) } )
		{
			var drift = (time * 0.006f) % 1f;
			for ( var k = 0; k < 4; k++ )
				DrawCircle( P( x + drift + k * 0.03f * s, y + MathF.Sin( k * 1.7f ) * 0.012f ), (16f + k % 2 * 6f) * s * scale, new Color( 1f, 1f, 1f, 0.85f ) );
		}
		// Sea: turquoise near, deeper toward the horizon.
		for ( var i = 0; i < bands; i++ )
		{
			var t = i / (float)(bands - 1);
			var c = new Color( 0.1f, 0.35f, 0.6f ).Lerp( new Color( 0.2f, 0.75f, 0.8f ), t );
			DrawRect( new Rect2( P( 0f, horizon + (1f - horizon) * i / bands ), new Vector2( r.Size.X, r.Size.Y * (1f - horizon) / bands + 1f ) ), c );
		}
		// The blue hole: a pale reef ring round a dark circle, squashed by the view.
		var hole = P( 0.66f, 0.72f );
		DrawEllipse( hole, new Vector2( 150f, 44f ) * scale, new Color( 0.35f, 0.85f, 0.85f ) );
		DrawEllipse( hole, new Vector2( 128f, 37f ) * scale, new Color( 0.88f, 0.84f, 0.7f ) );
		DrawEllipse( hole, new Vector2( 112f, 32f ) * scale, new Color( 0.25f, 0.6f, 0.8f ) );
		DrawEllipse( hole, new Vector2( 100f, 28f ) * scale, new Color( 0.03f, 0.06f, 0.25f ) );
		// Waves: thin bright lines drifting.
		for ( var row = 0; row < 7; row++ )
		{
			var y = horizon + 0.05f + row * 0.075f;
			var points = new Vector2[24];
			for ( var k = 0; k < points.Length; k++ )
			{
				var x = k / (float)(points.Length - 1);
				points[k] = P( x, y + MathF.Sin( x * 18f + time * 1.4f + row ) * 0.006f );
			}
			DrawPolyline( points, new Color( 1f, 1f, 1f, 0.18f + row * 0.03f ), 1.5f * scale );
		}
		// The pier: planks on posts running out to the diving board over the hole.
		var wood = new Color( 0.55f, 0.36f, 0.2f );
		var dark = new Color( 0.32f, 0.2f, 0.11f );
		DrawColoredPolygon( new[] { P( 0.02f, 0.8f ), P( 0.58f, 0.66f ), P( 0.58f, 0.68f ), P( 0.02f, 0.86f ) }, wood );
		for ( var k = 0; k < 9; k++ )
		{
			var x = 0.05f + k * 0.065f;
			var y = 0.83f - (x - 0.02f) / 0.56f * 0.15f;
			DrawRect( new Rect2( P( x, y ), new Vector2( 5f * scale, 26f * scale * (1f - k * 0.06f) ) ), dark );
		}
		DrawLine( P( 0.58f, 0.665f ), P( 0.66f, 0.64f ), new Color( 0.9f, 0.9f, 0.9f ), 4f * scale );   // the diving board
		// The hut on the pier: walls, a door, a window, a pitched roof.
		DrawRect( new Rect2( P( 0.08f, 0.6f ), new Vector2( 0.16f * r.Size.X, 0.2f * r.Size.Y ) ), new Color( 0.75f, 0.55f, 0.35f ) );
		DrawRect( new Rect2( P( 0.14f, 0.68f ), new Vector2( 0.035f * r.Size.X, 0.12f * r.Size.Y ) ), dark );
		DrawRect( new Rect2( P( 0.19f, 0.65f ), new Vector2( 0.03f * r.Size.X, 0.05f * r.Size.Y ) ), new Color( 1f, 0.9f, 0.55f ) );
		DrawColoredPolygon( new[] { P( 0.06f, 0.6f ), P( 0.16f, 0.5f ), P( 0.26f, 0.6f ) }, new Color( 0.7f, 0.2f, 0.18f ) );
		// Gulls.
		for ( var g = 0; g < 3; g++ )
		{
			var c = P( 0.3f + g * 0.12f + MathF.Sin( time * 0.3f + g ) * 0.02f, 0.14f + g * 0.05f + MathF.Sin( time * 2f + g * 2f ) * 0.005f );
			var flap = MathF.Sin( time * 6f + g ) * 3f * scale;
			DrawPolyline( new[] { c + new Vector2( -10f, -2f - flap ) * scale, c, c + new Vector2( 10f, -2f - flap ) * scale }, new Color( 0.2f, 0.2f, 0.25f ), 2f * scale );
		}
	}

	void DrawEllipse( Vector2 centre, Vector2 radii, Color c )
	{
		var points = new Vector2[40];
		for ( var i = 0; i < points.Length; i++ )
		{
			var a = i / (float)points.Length * MathF.Tau;
			points[i] = centre + new Vector2( MathF.Cos( a ) * radii.X, MathF.Sin( a ) * radii.Y );
		}
		DrawColoredPolygon( points, c );
	}
}
