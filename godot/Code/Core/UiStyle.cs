using Godot;

/// <summary>
/// The look of every screen ("Robot Visor"): you read the HUD off the inside of the robot's faceplate. Cyan system
/// readouts with a slight red/blue colour split, faint see-through panels marked by corner brackets, slanted tick
/// bars, and a barely-there scanline (VisorOverlay). One place for the fonts, the palette, the panels and the drawing
/// helpers, so the HUD, menus and world labels match.
/// Fonts (SIL Open Font License, in assets/fonts): Chakra Petch for HUD numbers, labels and menus (the project's
/// default font), Share Tech Mono for small readouts, Lilita One for the chunky world labels.
/// </summary>
public static class UiStyle
{
	public static readonly Color Ink = new( "020c14" );          // outlines, shadows
	public static readonly Color Panel = new( 0.01f, 0.06f, 0.09f, 0.42f );
	public static readonly Color PanelDeep = new( 0.01f, 0.05f, 0.08f, 0.9f );
	public static readonly Color Teal = new( "7ff6ff" );         // the visor's glow: brackets, labels, ticks, keys
	public static readonly Color TealSoft = new( "a6f8ff" );
	public static readonly Color Readout = new( "8fdcea" );
	/// <summary> The colour split on visor numbers: a red copy left, a blue copy right. </summary>
	public static readonly Color SplitRed = new( 1f, 0.23f, 0.42f, 0.5f );
	public static readonly Color SplitBlue = new( 0.18f, 0.55f, 1f, 0.5f );
	public static readonly Color Gold = new( "ffe46b" );         // numbers that matter: wave, points, damage
	public static readonly Color Red = new( "ff3b3b" );
	public static readonly Color RedTop = new( "ff9a8a" );
	public static readonly Color Pink = new( "d04fd8" );
	public static readonly Color PinkTop = new( "ffb3f2" );
	public static readonly Color Blue = new( "38c0ff" );
	public static readonly Color BlueTop = new( "a8e6ff" );
	public static readonly Color Amber = new( "ffb020" );
	public static readonly Color Lime = new( "c8ff7a" );

	static Font display, label, labelBold, mono, labelWide, monoWide, number;
	static Font Load( string file ) => GD.Load<Font>( $"res://assets/fonts/{file}" ) ?? ThemeDB.FallbackFont;
	/// <summary> Chunky cartoon letters for signs in the world (station names, the teleporter). </summary>
	public static Font Display => display ??= Load( "LilitaOne-Regular.ttf" );
	/// <summary> HUD numbers and banners: the visor's bold tech face. </summary>
	public static Font Number => number ??= Load( "ChakraPetch-Bold.ttf" );
	/// <summary> Labels and menus (the project default). </summary>
	public static Font Label => label ??= Load( "ChakraPetch-SemiBold.ttf" );
	public static Font LabelBold => labelBold ??= Load( "ChakraPetch-Bold.ttf" );
	/// <summary> Small spaced-out caps captions: HULL, MAG, TARGET. </summary>
	public static Font Caption => labelWide ??= new FontVariation { BaseFont = LabelBold, SpacingGlyph = 3 };
	/// <summary> Small readouts: reserve ammo, costs, timers. </summary>
	public static Font Mono => mono ??= Load( "ShareTechMono-Regular.ttf" );
	public static Font MonoWide => monoWide ??= new FontVariation { BaseFont = Mono, SpacingGlyph = 1 };

	// ---- Labels ----

	/// <summary> A big outlined cartoon number or banner (Lilita One, dark outline, a dropped shadow). </summary>
	public static Label Big( Control parent, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Center )
	{
		var l = new Label { HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
		StyleBig( l, size, colour );
		parent?.AddChild( l );
		return l;
	}

	public static void StyleBig( Label l, int size, Color colour )
	{
		l.AddThemeFontOverride( "font", Number );
		l.AddThemeFontSizeOverride( "font_size", size );
		l.AddThemeColorOverride( "font_color", colour );
		l.AddThemeColorOverride( "font_outline_color", Ink with { A = 0.7f } );
		l.AddThemeConstantOverride( "outline_size", Math.Max( 3, size / 12 ) );
		// The colour split: a red copy just left of the text.
		l.AddThemeColorOverride( "font_shadow_color", SplitRed );
		l.AddThemeConstantOverride( "shadow_offset_x", -Math.Max( 1, size / 22 ) );
		l.AddThemeConstantOverride( "shadow_offset_y", 0 );
		l.AddThemeConstantOverride( "shadow_outline_size", 0 );
	}

	/// <summary> A small glowing spaced caps caption (HULL, MAG, KILLS). </summary>
	public static Label Tag( Control parent, int size, Color? colour = null, HorizontalAlignment align = HorizontalAlignment.Center )
	{
		var l = new Label { HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore, Uppercase = true };
		l.AddThemeFontOverride( "font", Caption );
		l.AddThemeFontSizeOverride( "font_size", size );
		l.AddThemeColorOverride( "font_color", colour ?? TealSoft );
		l.AddThemeColorOverride( "font_outline_color", Ink with { A = 0.85f } );
		l.AddThemeConstantOverride( "outline_size", Math.Max( 4, size / 4 ) );
		parent?.AddChild( l );
		return l;
	}

	/// <summary> Running text on the HUD (Chakra Petch with a soft outline). </summary>
	public static Label Text( Control parent, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Center, bool mono = false )
	{
		var l = new Label { HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
		l.AddThemeFontOverride( "font", mono ? MonoWide : Label );
		l.AddThemeFontSizeOverride( "font_size", size );
		l.AddThemeColorOverride( "font_color", colour );
		l.AddThemeColorOverride( "font_outline_color", Ink with { A = 0.8f } );
		l.AddThemeConstantOverride( "outline_size", Math.Max( 4, size / 6 ) );
		parent?.AddChild( l );
		return l;
	}

	// ---- Panels ----

	/// <summary> The frame: a faint see-through panel with a hairline cyan edge and square corners (brackets go on top). </summary>
	public static StyleBoxFlat Frame( float scale = 1f, Color? fill = null, int margin = 14 )
	{
		var r = Math.Max( 1, (int)(2 * scale) );
		return new StyleBoxFlat
		{
			BgColor = fill ?? Panel,
			BorderColor = Teal with { A = 0.18f }, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
			CornerRadiusTopLeft = r, CornerRadiusBottomRight = r, CornerRadiusTopRight = r, CornerRadiusBottomLeft = r,
			ContentMarginLeft = margin * scale, ContentMarginRight = margin * scale, ContentMarginTop = margin * 0.6f * scale, ContentMarginBottom = margin * 0.6f * scale,
			AntiAliasing = true,
		};
	}

	/// <summary> A thin cyan line just inside a frame (kept for older callers; panels use brackets now). </summary>
	public static StyleBoxFlat FrameLine( float scale = 1f, Color? colour = null )
	{
		var w = Math.Max( 1, (int)MathF.Round( scale ) );
		return new StyleBoxFlat
		{
			DrawCenter = false, BorderColor = (colour ?? Teal) with { A = 0.35f },
			BorderWidthLeft = w, BorderWidthTop = w, BorderWidthRight = w, BorderWidthBottom = w,
			AntiAliasing = true,
		};
	}

	/// <summary> A frame drawn in a _Draw: the faint panel, then cyan corner brackets. </summary>
	public static void DrawFrame( CanvasItem c, Rect2 rect, float alpha = 1f, Color? line = null, float scale = 1f )
	{
		var frame = Frame( scale );
		frame.BgColor = frame.BgColor with { A = frame.BgColor.A * alpha };
		frame.BorderColor = frame.BorderColor with { A = frame.BorderColor.A * alpha };
		c.DrawStyleBox( frame, rect );
		DrawBrackets( c, rect, (line ?? Teal) with { A = alpha }, 12f * scale );
	}

	/// <summary> Four corner brackets round a rect: the visor marking out a readout. </summary>
	public static void DrawBrackets( CanvasItem c, Rect2 rect, Color? colour = null, float length = 12f, float width = 2f )
	{
		var col = colour ?? Teal;
		var ink = Ink with { A = 0.45f * col.A };
		var p = rect.Position;
		var e = rect.End;
		var corners = new[] { (p, 1f, 1f), (new Vector2( e.X, p.Y ), -1f, 1f), (new Vector2( p.X, e.Y ), 1f, -1f), (e, -1f, -1f) };
		foreach ( var pass in new[] { (ink, width + 2f), (col, width) } )
			foreach ( var (at, sx, sy) in corners )
			{
				c.DrawLine( at, at + new Vector2( length * sx, 0f ), pass.Item1, pass.Item2 );
				c.DrawLine( at, at + new Vector2( 0f, length * sy ), pass.Item1, pass.Item2 );
			}
	}

	/// <summary> A keycap chip for prompts: the key dark on a small cyan block. </summary>
	public static StyleBoxFlat KeyCap( float scale = 1f ) => new()
	{
		BgColor = Teal,
		CornerRadiusTopLeft = (int)(3 * scale), CornerRadiusTopRight = (int)(3 * scale), CornerRadiusBottomLeft = (int)(3 * scale), CornerRadiusBottomRight = (int)(3 * scale),
		ContentMarginLeft = 9 * scale, ContentMarginRight = 9 * scale, ContentMarginTop = 1 * scale, ContentMarginBottom = 1 * scale,
		ShadowColor = Teal with { A = 0.35f }, ShadowSize = (int)(4 * scale), AntiAliasing = true,
	};

	// ---- Menus ----

	static Theme menu;
	/// <summary>
	/// The menus' theme (pause, dev menu): suit-frame buttons that light teal on hover and focus, pill sliders with a
	/// teal grabber, teal toggles. Set on a menu's root; its own overrides still win.
	/// </summary>
	public static Theme Menu => menu ??= BuildMenu();

	static StyleBoxFlat Button( Color fill, Color border, int bottom = 3 )
	{
		var b = Frame( 0.7f, fill, 14 );
		b.BorderColor = border;
		b.BorderWidthLeft = bottom == 3 ? 1 : 2;
		return b;
	}

	static Theme BuildMenu()
	{
		var t = new Theme { DefaultFont = Label };
		t.SetStylebox( "normal", "Button", Button( new Color( 0.02f, 0.1f, 0.14f, 0.85f ), Teal with { A = 0.25f } ) );
		t.SetStylebox( "hover", "Button", Button( new Color( 0.05f, 0.24f, 0.3f, 0.95f ), Teal, 2 ) );
		t.SetStylebox( "pressed", "Button", Button( new Color( 0.08f, 0.34f, 0.4f, 1f ), Teal, 2 ) );
		t.SetStylebox( "hover_pressed", "Button", Button( new Color( 0.08f, 0.34f, 0.4f, 1f ), Teal, 2 ) );
		t.SetStylebox( "focus", "Button", new StyleBoxFlat { DrawCenter = false, BorderColor = Gold, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1, CornerRadiusTopLeft = 2, CornerRadiusBottomRight = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, ExpandMarginLeft = 3, ExpandMarginRight = 3, ExpandMarginTop = 3, ExpandMarginBottom = 3 } );
		t.SetStylebox( "disabled", "Button", Button( PanelDeep with { A = 0.5f }, Teal with { A = 0.1f } ) );
		t.SetColor( "font_color", "Button", Colors.White );
		t.SetColor( "font_hover_color", "Button", Teal );
		t.SetColor( "font_focus_color", "Button", Colors.White );
		t.SetColor( "font_pressed_color", "Button", Gold );
		t.SetColor( "font_hover_pressed_color", "Button", Gold );
		t.SetColor( "font_outline_color", "Button", Ink );
		t.SetConstant( "outline_size", "Button", 4 );
		t.SetFont( "font", "Button", LabelBold );
		t.SetColor( "font_color", "Label", Colors.White );
		// Sliders: a dark pill track, a teal fill, a round teal grabber with an ink edge.
		var track = new StyleBoxFlat { BgColor = new Color( 0.5f, 0.96f, 1f, 0.1f ), ContentMarginTop = 3, ContentMarginBottom = 3 };
		var fill = (StyleBoxFlat)track.Duplicate();
		fill.BgColor = Teal;
		t.SetStylebox( "slider", "HSlider", track );
		t.SetStylebox( "grabber_area", "HSlider", fill );
		t.SetStylebox( "grabber_area_highlight", "HSlider", fill );
		t.SetIcon( "grabber", "HSlider", Knob( Teal ) );
		t.SetIcon( "grabber_highlight", "HSlider", Knob( Gold ) );
		var line = new StyleBoxLine { Color = Teal with { A = 0.35f }, Thickness = 2 };
		t.SetStylebox( "separator", "HSeparator", line );
		return t;
	}

	/// <summary> A round slider knob: the colour with an ink edge. </summary>
	static Texture2D Knob( Color colour )
	{
		const int size = 22;
		var image = Image.CreateEmpty( size, size, false, Image.Format.Rgba8 );
		var c = (size - 1) * 0.5f;
		for ( var y = 0; y < size; y++ )
			for ( var x = 0; x < size; x++ )
			{
				var d = new Vector2( x - c, y - c ).Length();
				var a = MathX.Clamp( c + 0.5f - d, 0f, 1f );
				var edge = d > c - 3f;
				image.SetPixel( x, y, (edge ? Ink : colour) with { A = a } );
			}
		return ImageTexture.CreateFromImage( image );
	}

	// ---- Bars ----

	/// <summary>
	/// A bar of slanted glowing ticks filled to `fraction` (the last one part full), faint empty ones after it, and an
	/// optional pale `lag` behind the fill (health that was just lost). `top` lights a thin edge along each tick.
	/// </summary>
	public static void DrawPills( CanvasItem c, Rect2 rect, int segments, float fraction, Color fill, Color top, float alpha = 1f, float lag = -1f, float gap = 4f )
	{
		segments = Math.Max( 1, segments );
		var w = (rect.Size.X - gap * (segments - 1)) / segments;
		var h = rect.Size.Y;
		var skew = MathF.Min( h * 0.35f, w * 0.4f );
		for ( var i = 0; i < segments; i++ )
		{
			var x = rect.Position.X + i * (w + gap);
			var y = rect.Position.Y;
			var full = MathX.Clamp( fraction * segments - i, 0f, 1f );
			var lagged = lag >= 0f ? MathX.Clamp( lag * segments - i, 0f, 1f ) : 0f;
			Tick( c, x, y, w, h, skew, new Color( 0.5f, 0.96f, 1f, 0.1f * alpha ) );
			if ( lagged > full ) Tick( c, x, y, w * lagged, h, skew, new Color( 1f, 1f, 1f, 0.5f * alpha ) );
			if ( full <= 0f ) continue;
			var fw = MathF.Max( skew * 1.5f, w * full );
			Tick( c, x - 1.5f, y - 1.5f, fw + 3f, h + 3f, skew, fill with { A = 0.22f * alpha } );   // glow
			Tick( c, x, y, fw, h, skew, fill with { A = alpha } );
			Tick( c, x, y, fw, MathF.Max( 1f, h * 0.22f ), skew * 0.22f, top with { A = 0.8f * alpha } );
		}
	}

	static readonly Vector2[] tick = new Vector2[4];
	static void Tick( CanvasItem c, float x, float y, float w, float h, float skew, Color colour )
	{
		tick[0] = new Vector2( x + skew, y );
		tick[1] = new Vector2( x + w, y );
		tick[2] = new Vector2( x + w - skew, y + h );
		tick[3] = new Vector2( x, y + h );
		c.DrawColoredPolygon( tick, colour );
	}

	// ---- Text in a _Draw ----

	/// <summary> A big visor number or banner in a _Draw: a thin dark edge, the red/blue colour split, then the text. </summary>
	public static void DrawBig( CanvasItem c, Vector2 at, string text, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f, float alpha = 1f )
	{
		var f = Number;
		var split = new Vector2( Math.Max( 1, size / 22 ) + VisorOverlay.Glitch * 5f, 0f );
		c.DrawStringOutline( f, at, text, align, width, size, Math.Max( 3, size / 12 ), Ink with { A = 0.7f * alpha } );
		c.DrawString( f, at - split, text, align, width, size, SplitRed with { A = SplitRed.A * alpha } );
		c.DrawString( f, at + split, text, align, width, size, SplitBlue with { A = SplitBlue.A * alpha } );
		c.DrawString( f, at, text, align, width, size, colour with { A = colour.A * alpha } );
	}

	/// <summary> A small caption or readout in a _Draw, with a soft ink outline. </summary>
	public static void DrawTag( CanvasItem c, Vector2 at, string text, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f, bool mono = false, bool caps = true )
	{
		var f = mono ? MonoWide : Caption;
		if ( caps ) text = text.ToUpperInvariant();
		c.DrawStringOutline( f, at, text, align, width, size, Math.Max( 4, size / 4 ), Ink with { A = 0.85f * colour.A } );
		c.DrawString( f, at, text, align, width, size, colour );
	}

	/// <summary> Running text in a _Draw (Chakra Petch, soft outline). </summary>
	public static void DrawText( CanvasItem c, Vector2 at, string text, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f )
	{
		c.DrawStringOutline( Label, at, text, align, width, size, Math.Max( 4, size / 5 ), Ink with { A = 0.8f * colour.A } );
		c.DrawString( Label, at, text, align, width, size, colour );
	}

	/// <summary> Width of a string in the display font. </summary>
	public static float BigWidth( string text, int size ) => Number.GetStringSize( text, HorizontalAlignment.Left, -1f, size ).X;
	public static float TagWidth( string text, int size, bool mono = false ) => (mono ? MonoWide : Caption).GetStringSize( text.ToUpperInvariant(), HorizontalAlignment.Left, -1f, size ).X;
}
