using Godot;

/// <summary>
/// The look of every screen ("Arcade Dive"): the robot's suit display (small glowing tech labels, framed panels,
/// segmented bars, a faint scanline) carrying chunky outlined cartoon numbers and glossy pill segments. One place for
/// the fonts, the palette, the panels and the drawing helpers, so the HUD, menus and world labels match.
/// Fonts (SIL Open Font License, in assets/fonts): Lilita One for numbers and banners, Chakra Petch for labels and
/// menus (the project's default font), Share Tech Mono for small readouts.
/// </summary>
public static class UiStyle
{
	public static readonly Color Ink = new( "06202c" );          // outlines, shadows
	public static readonly Color Panel = new( 0.03f, 0.16f, 0.21f, 0.86f );
	public static readonly Color PanelDeep = new( 0.016f, 0.086f, 0.125f, 0.92f );
	public static readonly Color Teal = new( "5fffe0" );         // the suit's glow: frames, labels, keys
	public static readonly Color TealSoft = new( "7ff5e0" );
	public static readonly Color Readout = new( "9fe9de" );
	public static readonly Color Gold = new( "ffe46b" );         // numbers that matter: wave, points, damage
	public static readonly Color Red = new( "ff3b3b" );
	public static readonly Color RedTop = new( "ff9a8a" );
	public static readonly Color Pink = new( "d04fd8" );
	public static readonly Color PinkTop = new( "ffb3f2" );
	public static readonly Color Blue = new( "38c0ff" );
	public static readonly Color BlueTop = new( "a8e6ff" );
	public static readonly Color Amber = new( "ffb020" );
	public static readonly Color Lime = new( "c8ff7a" );

	static Font display, label, labelBold, mono, labelWide, monoWide;
	static Font Load( string file ) => GD.Load<Font>( $"res://assets/fonts/{file}" ) ?? ThemeDB.FallbackFont;
	/// <summary> Chunky outlined numbers and banners. </summary>
	public static Font Display => display ??= Load( "LilitaOne-Regular.ttf" );
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
		l.AddThemeFontOverride( "font", Display );
		l.AddThemeFontSizeOverride( "font_size", size );
		l.AddThemeColorOverride( "font_color", colour );
		l.AddThemeColorOverride( "font_outline_color", Ink );
		l.AddThemeConstantOverride( "outline_size", Math.Max( 6, size / 6 ) );
		l.AddThemeColorOverride( "font_shadow_color", Ink );
		l.AddThemeConstantOverride( "shadow_offset_x", 0 );
		l.AddThemeConstantOverride( "shadow_offset_y", Math.Max( 2, size / 12 ) );
		l.AddThemeConstantOverride( "shadow_outline_size", Math.Max( 6, size / 6 ) );
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

	/// <summary> The frame: a dark see-through panel with a thick ink edge, rounded on two opposite corners, a drop shadow. </summary>
	public static StyleBoxFlat Frame( float scale = 1f, Color? fill = null, int margin = 14 )
	{
		var r = (int)(14 * scale);
		var s = (int)(4 * scale);
		return new StyleBoxFlat
		{
			BgColor = fill ?? Panel,
			BorderColor = Ink, BorderWidthLeft = (int)(3 * scale), BorderWidthTop = (int)(3 * scale), BorderWidthRight = (int)(3 * scale), BorderWidthBottom = (int)(3 * scale),
			CornerRadiusTopLeft = r, CornerRadiusBottomRight = r, CornerRadiusTopRight = s, CornerRadiusBottomLeft = s,
			ShadowColor = Ink with { A = 0.9f }, ShadowSize = 0, ShadowOffset = new Vector2( 0f, 4f * scale ),
			ContentMarginLeft = margin * scale, ContentMarginRight = margin * scale, ContentMarginTop = margin * 0.7f * scale, ContentMarginBottom = margin * 0.7f * scale,
			AntiAliasing = true,
		};
	}

	/// <summary> The teal line just inside a frame. </summary>
	public static StyleBoxFlat FrameLine( float scale = 1f, Color? colour = null )
	{
		var r = (int)(10 * scale);
		var s = (int)(2 * scale);
		var w = Math.Max( 1, (int)MathF.Round( 1.5f * scale ) );
		return new StyleBoxFlat
		{
			DrawCenter = false, BorderColor = (colour ?? Teal) with { A = 0.7f },
			BorderWidthLeft = w, BorderWidthTop = w, BorderWidthRight = w, BorderWidthBottom = w,
			CornerRadiusTopLeft = r, CornerRadiusBottomRight = r, CornerRadiusTopRight = s, CornerRadiusBottomLeft = s,
			AntiAliasing = true,
		};
	}

	/// <summary> A frame drawn in a _Draw: shadow, ink edge, fill, then the teal line inside. </summary>
	public static void DrawFrame( CanvasItem c, Rect2 rect, float alpha = 1f, Color? line = null, float scale = 1f )
	{
		var frame = Frame( scale );
		frame.BgColor = frame.BgColor with { A = frame.BgColor.A * alpha };
		frame.BorderColor = frame.BorderColor with { A = alpha };
		frame.ShadowColor = frame.ShadowColor with { A = 0.9f * alpha };
		c.DrawStyleBox( frame, rect );
		var inner = FrameLine( scale, line );
		inner.BorderColor = inner.BorderColor with { A = inner.BorderColor.A * alpha };
		c.DrawStyleBox( inner, rect.Grow( -5f * scale ) );
	}

	/// <summary> A keycap chip for prompts: the key in Lilita on a teal button with an ink edge. </summary>
	public static StyleBoxFlat KeyCap( float scale = 1f ) => new()
	{
		BgColor = Teal, BorderColor = Ink, BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 3,
		CornerRadiusTopLeft = (int)(7 * scale), CornerRadiusTopRight = (int)(7 * scale), CornerRadiusBottomLeft = (int)(7 * scale), CornerRadiusBottomRight = (int)(7 * scale),
		ContentMarginLeft = 10 * scale, ContentMarginRight = 10 * scale, ContentMarginTop = 1 * scale, ContentMarginBottom = 2 * scale,
		ShadowColor = Ink, ShadowOffset = new Vector2( 0f, 3f * scale ), AntiAliasing = true,
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
		b.BorderWidthBottom = bottom;
		b.ShadowOffset = new Vector2( 0f, 3f );
		return b;
	}

	static Theme BuildMenu()
	{
		var t = new Theme { DefaultFont = Label };
		t.SetStylebox( "normal", "Button", Button( Panel, Ink ) );
		t.SetStylebox( "hover", "Button", Button( new Color( 0.06f, 0.3f, 0.36f, 0.95f ), Teal ) );
		t.SetStylebox( "pressed", "Button", Button( new Color( 0.1f, 0.42f, 0.46f, 1f ), Teal, 2 ) );
		t.SetStylebox( "hover_pressed", "Button", Button( new Color( 0.1f, 0.42f, 0.46f, 1f ), Teal, 2 ) );
		t.SetStylebox( "focus", "Button", new StyleBoxFlat { DrawCenter = false, BorderColor = Gold, BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2, CornerRadiusTopLeft = 10, CornerRadiusBottomRight = 10, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, ExpandMarginLeft = 3, ExpandMarginRight = 3, ExpandMarginTop = 3, ExpandMarginBottom = 3 } );
		t.SetStylebox( "disabled", "Button", Button( PanelDeep with { A = 0.6f }, Ink ) );
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
		var track = new StyleBoxFlat { BgColor = new Color( 1f, 1f, 1f, 0.1f ), BorderColor = Ink, BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2, CornerRadiusTopLeft = 99, CornerRadiusTopRight = 99, CornerRadiusBottomLeft = 99, CornerRadiusBottomRight = 99, ContentMarginTop = 5, ContentMarginBottom = 5 };
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

	static readonly StyleBoxFlat pill = new() { CornerRadiusTopLeft = 99, CornerRadiusTopRight = 99, CornerRadiusBottomLeft = 99, CornerRadiusBottomRight = 99, AntiAliasing = true };

	/// <summary>
	/// A bar of glossy pill segments filled to `fraction` (the last one part full), dark empty ones after it, and an
	/// optional pale `lag` behind the fill (health that was just lost).
	/// </summary>
	public static void DrawPills( CanvasItem c, Rect2 rect, int segments, float fraction, Color fill, Color top, float alpha = 1f, float lag = -1f, float gap = 4f )
	{
		segments = Math.Max( 1, segments );
		var w = (rect.Size.X - gap * (segments - 1)) / segments;
		var h = rect.Size.Y;
		for ( var i = 0; i < segments; i++ )
		{
			var r = new Rect2( rect.Position.X + i * (w + gap), rect.Position.Y, w, h );
			var full = MathX.Clamp( fraction * segments - i, 0f, 1f );
			var lagged = lag >= 0f ? MathX.Clamp( lag * segments - i, 0f, 1f ) : 0f;
			pill.BgColor = new Color( 1f, 1f, 1f, 0.09f * alpha );
			pill.BorderColor = Ink with { A = 0.7f * alpha };
			pill.SetBorderWidthAll( 1 );
			c.DrawStyleBox( pill, r );
			pill.SetBorderWidthAll( 0 );
			if ( lagged > full )
			{
				pill.BgColor = new Color( 1f, 1f, 1f, 0.55f * alpha );
				c.DrawStyleBox( pill, new Rect2( r.Position, new Vector2( MathF.Max( h, w * lagged ), h ) ) );
			}
			if ( full <= 0f ) continue;
			var fw = MathF.Max( MathF.Min( h, w ), w * full );
			pill.BgColor = fill with { A = alpha };
			c.DrawStyleBox( pill, new Rect2( r.Position, new Vector2( fw, h ) ) );
			// The gloss: a lighter band along the top.
			pill.BgColor = top with { A = 0.85f * alpha };
			c.DrawStyleBox( pill, new Rect2( r.Position + new Vector2( h * 0.2f, h * 0.12f ), new Vector2( MathF.Max( 0f, fw - h * 0.4f ), h * 0.32f ) ) );
		}
	}

	// ---- Text in a _Draw ----

	/// <summary> A big outlined cartoon string in a _Draw (Lilita One with the ink outline and dropped shadow). </summary>
	public static void DrawBig( CanvasItem c, Vector2 at, string text, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f, float alpha = 1f )
	{
		var f = Display;
		var outline = Math.Max( 6, size / 6 );
		var drop = new Vector2( 0f, Math.Max( 2, size / 12 ) );
		c.DrawStringOutline( f, at + drop, text, align, width, size, outline, Ink with { A = alpha } );
		c.DrawString( f, at + drop, text, align, width, size, Ink with { A = alpha } );
		c.DrawStringOutline( f, at, text, align, width, size, outline, Ink with { A = alpha } );
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
	public static float BigWidth( string text, int size ) => Display.GetStringSize( text, HorizontalAlignment.Left, -1f, size ).X;
	public static float TagWidth( string text, int size, bool mono = false ) => (mono ? MonoWide : Caption).GetStringSize( text.ToUpperInvariant(), HorizontalAlignment.Left, -1f, size ).X;
}
