using Godot;

/// <summary>
/// The look of every screen, drawn in the current HudTheme (Arcade Dive by default; the dev menu's Look tab switches
/// it, see HudTheme). One place for the fonts, the palette, the panels and the drawing helpers, so the HUD, menus
/// and world labels match. HUD pieces build their labels from here and rebuild when `Changed` fires.
/// Fonts (SIL Open Font License, in assets/fonts): Lilita One for chunky numbers and banners, Chakra Petch for labels
/// and menus (the project's default font), Share Tech Mono for small readouts.
/// </summary>
public static class UiStyle
{
	static HudTheme theme = HudTheme.Find( GameSettings.HudTheme );
	/// <summary> The look in use. Setting it fires `Changed` so the HUD rebuilds. </summary>
	public static HudTheme Theme
	{
		get => theme;
		set
		{
			value ??= HudTheme.ArcadeDive;
			if ( value == theme ) return;
			theme = value;
			GameSettings.HudTheme = value.Name;
			menu = null;
			Changed?.Invoke();
		}
	}
	public static event Action Changed;

	public static Color Ink => theme.Ink;
	public static Color Panel => theme.Panel;
	public static Color PanelDeep => theme.PanelDeep;
	public static Color Teal => theme.Accent;
	public static Color TealSoft => theme.AccentSoft;
	public static Color Readout => theme.Readout;
	public static Color Gold => theme.Gold;
	public static Color Red => theme.Red;
	public static Color RedTop => theme.RedTop;
	public static readonly Color Pink = new( "d04fd8" );
	public static readonly Color PinkTop = new( "ffb3f2" );
	public static readonly Color Blue = new( "38c0ff" );
	public static readonly Color BlueTop = new( "a8e6ff" );
	public static readonly Color Amber = new( "ffb020" );
	public static readonly Color Lime = new( "c8ff7a" );
	/// <summary> The visor's colour split: a red copy left, a blue copy right. </summary>
	public static readonly Color SplitRed = new( 1f, 0.23f, 0.42f, 0.5f );
	public static readonly Color SplitBlue = new( 0.18f, 0.55f, 1f, 0.5f );

	/// <summary> A HUD size (px at 1080p) in the current theme's scale. </summary>
	public static int S( int size ) => Math.Max( 1, (int)MathF.Round( size * theme.Scale ) );
	public static float S( float size ) => size * theme.Scale;

	static Font display, label, labelBold, mono, labelWide, monoWide;
	static Font Load( string file ) => GD.Load<Font>( $"res://assets/fonts/{file}" ) ?? ThemeDB.FallbackFont;
	/// <summary> Chunky cartoon letters (Lilita One): signs in the world, and the HUD numbers in most themes. </summary>
	public static Font Display => display ??= Load( "LilitaOne-Regular.ttf" );
	/// <summary> Labels and menus (the project default). </summary>
	public static Font Label => label ??= Load( "ChakraPetch-SemiBold.ttf" );
	public static Font LabelBold => labelBold ??= Load( "ChakraPetch-Bold.ttf" );
	/// <summary> Small spaced-out caps captions: HULL, MAG, TARGET. </summary>
	public static Font Caption => labelWide ??= new FontVariation { BaseFont = LabelBold, SpacingGlyph = 3 };
	/// <summary> Small readouts: reserve ammo, costs, timers. </summary>
	public static Font Mono => mono ??= Load( "ShareTechMono-Regular.ttf" );
	public static Font MonoWide => monoWide ??= new FontVariation { BaseFont = Mono, SpacingGlyph = 1 };
	/// <summary> HUD numbers and banners in the current theme. </summary>
	public static Font Number => theme.NumberFont switch { HudTheme.FontRole.Bold => LabelBold, HudTheme.FontRole.Mono => Mono, _ => Display };

	// ---- Labels ----

	/// <summary> A big HUD number or banner in the theme's style. </summary>
	public static Label Big( Control parent, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Center )
	{
		var l = new Label { HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
		StyleBig( l, size, colour );
		parent?.AddChild( l );
		return l;
	}

	public static void StyleBig( Label l, int size, Color colour, bool onPanel = false )
	{
		l.AddThemeFontOverride( "font", Number );
		l.AddThemeFontSizeOverride( "font_size", size );
		l.AddThemeColorOverride( "font_color", colour );
		var look = onPanel && theme.LightPanel && theme.Big != HudTheme.BigLook.Comic ? HudTheme.BigLook.Flat : theme.Big;
		var (outline, outlineSize, shadow, shadowOffset, shadowOutline) = look switch
		{
			HudTheme.BigLook.Split => (Ink with { A = 0.7f }, Math.Max( 3, size / 12 ), SplitRed, new Vector2I( -Math.Max( 1, size / 22 ), 0 ), 0),
			HudTheme.BigLook.Glow => (theme.Accent with { A = 0.28f }, Math.Max( 6, size / 4 ), theme.Accent with { A = 0.3f }, Vector2I.Zero, Math.Max( 10, size / 2 )),
			HudTheme.BigLook.Flat => (Colors.Transparent, 0, (onPanel ? Ink : Ink) with { A = onPanel ? 0.12f : 0.6f }, new Vector2I( 0, Math.Max( 1, size / 24 ) ), 0),
			HudTheme.BigLook.Comic => (Ink, Math.Max( 6, size / 5 ), Ink, new Vector2I( Math.Max( 2, size / 14 ), Math.Max( 2, size / 14 ) ), Math.Max( 6, size / 5 )),
			_ => (Ink, Math.Max( 6, size / 6 ), Ink, new Vector2I( 0, Math.Max( 2, size / 12 ) ), Math.Max( 6, size / 6 )),
		};
		l.AddThemeColorOverride( "font_outline_color", outline );
		l.AddThemeConstantOverride( "outline_size", outlineSize );
		l.AddThemeColorOverride( "font_shadow_color", shadow );
		l.AddThemeConstantOverride( "shadow_offset_x", shadowOffset.X );
		l.AddThemeConstantOverride( "shadow_offset_y", shadowOffset.Y );
		l.AddThemeConstantOverride( "shadow_outline_size", shadowOutline );
	}

	/// <summary> A small glowing spaced caps caption (HULL, MAG, KILLS). </summary>
	public static Label Tag( Control parent, int size, Color? colour = null, HorizontalAlignment align = HorizontalAlignment.Center, bool onPanel = false )
	{
		var l = new Label { HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore, Uppercase = true };
		l.AddThemeFontOverride( "font", Caption );
		l.AddThemeFontSizeOverride( "font_size", size );
		l.AddThemeColorOverride( "font_color", colour ?? TealSoft );
		l.AddThemeColorOverride( "font_outline_color", Ink with { A = 0.85f } );
		l.AddThemeConstantOverride( "outline_size", onPanel && theme.LightPanel ? 0 : Math.Max( 4, size / 4 ) );
		parent?.AddChild( l );
		return l;
	}

	/// <summary> Running text on the HUD (Chakra Petch with a soft outline). </summary>
	public static Label Text( Control parent, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Center, bool mono = false, bool onPanel = false )
	{
		var l = new Label { HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
		l.AddThemeFontOverride( "font", mono ? MonoWide : Label );
		l.AddThemeFontSizeOverride( "font_size", size );
		l.AddThemeColorOverride( "font_color", colour );
		l.AddThemeColorOverride( "font_outline_color", Ink with { A = 0.8f } );
		l.AddThemeConstantOverride( "outline_size", onPanel && theme.LightPanel ? 0 : Math.Max( 4, size / 6 ) );
		parent?.AddChild( l );
		return l;
	}

	/// <summary> A sub-line under a banner: mono readout in some themes, running text in others. </summary>
	public static Label Sub( Control parent, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Center )
		=> theme.MonoSub ? Text( parent, size, TealSoft, align, true ) : Text( parent, size, colour, align );

	// ---- Panels ----

	/// <summary> The theme's panel as a stylebox (for PanelContainers); `DrawFrameExtras` adds the details on top. </summary>
	public static StyleBoxFlat Frame( float scale = 1f, Color? fill = null, int margin = 14 )
	{
		int I( float v ) => Math.Max( 0, (int)MathF.Round( v * scale ) );
		var b = new StyleBoxFlat
		{
			BgColor = fill ?? Panel, AntiAliasing = true,
			ContentMarginLeft = margin * scale, ContentMarginRight = margin * scale, ContentMarginTop = margin * 0.7f * scale, ContentMarginBottom = margin * 0.7f * scale,
		};
		void Border( Color c, int w ) { b.BorderColor = c; b.SetBorderWidthAll( w ); }
		void Corners( int r ) => b.SetCornerRadiusAll( r );
		switch ( theme.Frame )
		{
			case HudTheme.FrameLook.Brackets:
				Border( Teal with { A = 0.18f }, 1 ); Corners( Math.Max( 1, I( 2 ) ) );
				b.ContentMarginTop = b.ContentMarginBottom = margin * 0.6f * scale;
				break;
			case HudTheme.FrameLook.Brass:
				Border( new Color( "c4913a" ), Math.Max( 2, I( 4 ) ) ); Corners( I( 12 ) );
				b.ShadowColor = Ink with { A = 0.9f }; b.ShadowSize = 1; b.ShadowOffset = new Vector2( 0f, 3f * scale );
				break;
			case HudTheme.FrameLook.Glass:
				Border( new Color( 0.9f, 1f, 1f, 0.6f ), Math.Max( 1, I( 2 ) ) ); Corners( I( 22 ) );
				break;
			case HudTheme.FrameLook.Comic:
				Border( Ink, Math.Max( 2, I( 4 ) ) ); Corners( 0 );
				b.ShadowColor = Ink; b.ShadowSize = 1; b.ShadowOffset = new Vector2( 7f, 7f ) * scale;
				break;
			case HudTheme.FrameLook.Sticker:
				Border( Colors.White, Math.Max( 2, I( 4 ) ) ); Corners( I( 12 ) );
				b.ShadowColor = new Color( 0f, 0f, 0f, 0.45f ); b.ShadowSize = I( 6 ); b.ShadowOffset = new Vector2( 2f, 4f ) * scale;
				break;
			case HudTheme.FrameLook.Sonar:
				Border( Teal with { A = 0.6f }, Math.Max( 1, I( 1.5f ) ) ); Corners( 0 );
				break;
			case HudTheme.FrameLook.None:
				b.DrawCenter = false; Border( Colors.Transparent, 0 );
				break;
			case HudTheme.FrameLook.Lcd:
				Border( new Color( "1e2327" ), Math.Max( 3, I( 7 ) ) ); Corners( I( 14 ) );
				b.ShadowColor = new Color( 0f, 0f, 0f, 0.5f ); b.ShadowSize = I( 6 ); b.ShadowOffset = new Vector2( 0f, 4f * scale );
				break;
			case HudTheme.FrameLook.Halftone:
				Border( Teal, Math.Max( 2, I( 2.5f ) ) ); Corners( I( 10 ) );
				b.ShadowColor = new Color( 0f, 0f, 0f, 0.55f ); b.ShadowSize = 1; b.ShadowOffset = new Vector2( 6f, 6f ) * scale;
				break;
			default:   // Suit (Arcade Dive): a thick ink edge, rounded on two opposite corners, a drop shadow.
				Border( Ink, I( 3 ) );
				b.CornerRadiusTopLeft = b.CornerRadiusBottomRight = I( 14 );
				b.CornerRadiusTopRight = b.CornerRadiusBottomLeft = I( 4 );
				b.ShadowColor = Ink with { A = 0.9f }; b.ShadowSize = 0; b.ShadowOffset = new Vector2( 0f, 4f * scale );
				break;
		}
		return b;
	}

	/// <summary> The suit frame's line just inside a frame (Arcade Dive). </summary>
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

	/// <summary> A frame drawn in a _Draw: the panel, then the theme's details on top. `line` tints the accent. </summary>
	public static void DrawFrame( CanvasItem c, Rect2 rect, float alpha = 1f, Color? line = null, float scale = 1f )
	{
		var frame = Frame( scale );
		frame.BgColor = frame.BgColor with { A = frame.BgColor.A * alpha };
		frame.BorderColor = frame.BorderColor with { A = frame.BorderColor.A * alpha };
		frame.ShadowColor = frame.ShadowColor with { A = frame.ShadowColor.A * alpha };
		if ( line is { } tint && theme.Frame is HudTheme.FrameLook.Halftone or HudTheme.FrameLook.Sonar ) frame.BorderColor = tint with { A = alpha };
		c.DrawStyleBox( frame, rect );
		DrawFrameExtras( c, rect, alpha, line, scale );
	}

	/// <summary> The theme's details over a panel: the inner line, corner brackets, rivets, tape, halftone dots, gloss. </summary>
	public static void DrawFrameExtras( CanvasItem c, Rect2 rect, float alpha = 1f, Color? line = null, float scale = 1f )
	{
		switch ( theme.Frame )
		{
			case HudTheme.FrameLook.Suit:
				var inner = FrameLine( scale, line );
				inner.BorderColor = inner.BorderColor with { A = inner.BorderColor.A * alpha };
				c.DrawStyleBox( inner, rect.Grow( -5f * scale ) );
				break;
			case HudTheme.FrameLook.Brackets:
				DrawBrackets( c, rect, (line ?? Teal) with { A = alpha }, 12f * scale );
				break;
			case HudTheme.FrameLook.Brass:
				// Rivets along the top and bottom edges, and a pale line inside.
				var rivet = new Color( "fff1c4" ) with { A = alpha };
				var n = Math.Max( 2, (int)(rect.Size.X / (36f * scale)) );
				for ( var i = 0; i < n; i++ )
				{
					var x = rect.Position.X + 10f * scale + i * (rect.Size.X - 20f * scale) / (n - 1);
					foreach ( var y in new[] { rect.Position.Y + 5f * scale, rect.End.Y - 5f * scale } )
					{
						c.DrawCircle( new Vector2( x, y ), 2.6f * scale, new Color( "5a3a0c" ) with { A = alpha } );
						c.DrawCircle( new Vector2( x, y ), 1.6f * scale, rivet );
					}
				}
				if ( line is { } warn ) c.DrawRect( rect.Grow( -9f * scale ), warn with { A = 0.6f * alpha }, false, 1.5f * scale );
				break;
			case HudTheme.FrameLook.Glass:
				// A gloss streak along the top.
				var gloss = new StyleBoxFlat { BgColor = new Color( 1f, 1f, 1f, 0.16f * alpha ), AntiAliasing = true };
				gloss.SetCornerRadiusAll( 99 );
				c.DrawStyleBox( gloss, new Rect2( rect.Position + new Vector2( 14f, 5f ) * scale, new Vector2( MathF.Max( 0f, rect.Size.X * 0.55f ), 5f * scale ) ) );
				if ( line is { } edge ) c.DrawRect( rect.Grow( -4f * scale ), edge with { A = 0.5f * alpha }, false, 1.5f * scale );
				break;
			case HudTheme.FrameLook.Comic:
			case HudTheme.FrameLook.Halftone:
				// Halftone dots in the bottom right corner.
				var dot = theme.Frame == HudTheme.FrameLook.Comic ? new Color( 0f, 0f, 0f, 0.16f * alpha ) : Teal with { A = 0.13f * alpha };
				var step = 7f * scale;
				for ( var y = rect.Position.Y + step; y < rect.End.Y - 3f; y += step )
					for ( var x = rect.Position.X + step; x < rect.End.X - 3f; x += step )
					{
						var k = ((x - rect.Position.X) / rect.Size.X + (y - rect.Position.Y) / rect.Size.Y) * 0.5f;
						if ( k > 0.35f ) c.DrawCircle( new Vector2( x, y ), 1.6f * scale * k, dot );
					}
				if ( line is { } hot && theme.Frame == HudTheme.FrameLook.Comic ) c.DrawRect( rect.Grow( -5f * scale ), hot with { A = alpha }, false, 3f * scale );
				break;
			case HudTheme.FrameLook.Sticker:
				// A strip of tape over the top edge, and the corner peeling up.
				var tape = new Vector2( rect.Position.X + rect.Size.X * 0.5f, rect.Position.Y );
				var tw = 56f * scale;
				var th = 16f * scale;
				var t = new[] { new Vector2( -tw * 0.5f, -th * 0.6f ), new Vector2( tw * 0.5f, -th * 0.8f ), new Vector2( tw * 0.5f, th * 0.2f ), new Vector2( -tw * 0.5f, th * 0.4f ) };
				for ( var i = 0; i < 4; i++ ) t[i] += tape;
				c.DrawColoredPolygon( t, new Color( 0.93f, 0.89f, 0.75f, 0.8f * alpha ) );
				var e = rect.End;
				var p = 22f * scale;
				c.DrawColoredPolygon( new[] { e - new Vector2( p, 0f ), e - new Vector2( 0f, p ), e - new Vector2( p * 0.15f, p * 0.15f ) }, new Color( "e7e2d6" ) with { A = alpha } );
				if ( line is { } bad ) c.DrawRect( rect.Grow( -6f * scale ), bad with { A = 0.9f * alpha }, false, 2f * scale );
				break;
			case HudTheme.FrameLook.Lcd:
				// The casing's two amber buttons on the right.
				var amber = new Color( "ffb02e" ) with { A = alpha };
				c.DrawRect( new Rect2( rect.End.X, rect.Position.Y + rect.Size.Y * 0.2f, 6f * scale, 16f * scale ), amber );
				c.DrawRect( new Rect2( rect.End.X, rect.Position.Y + rect.Size.Y * 0.55f, 6f * scale, 16f * scale ), amber );
				if ( line is { } alarm ) c.DrawRect( rect.Grow( -9f * scale ), alarm with { A = 0.7f * alpha }, false, 2f * scale );
				break;
		}
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

	/// <summary> A keycap chip for prompts: the key on a block in the theme's accent. </summary>
	public static StyleBoxFlat KeyCap( float scale = 1f )
	{
		var b = new StyleBoxFlat
		{
			BgColor = theme.LightPanel ? Ink : Teal, AntiAliasing = true,
			ContentMarginLeft = 10 * scale, ContentMarginRight = 10 * scale, ContentMarginTop = 1 * scale, ContentMarginBottom = 2 * scale,
		};
		b.SetCornerRadiusAll( (int)((theme.Frame == HudTheme.FrameLook.Brackets ? 3 : 7) * scale) );
		if ( theme.Frame == HudTheme.FrameLook.Brackets ) { b.ShadowColor = Teal with { A = 0.35f }; b.ShadowSize = (int)(4 * scale); }
		else
		{
			b.BorderColor = Ink; b.BorderWidthLeft = b.BorderWidthTop = b.BorderWidthRight = 2; b.BorderWidthBottom = 3;
			b.ShadowColor = Ink; b.ShadowOffset = new Vector2( 0f, 3f * scale );
		}
		return b;
	}

	/// <summary> The colour of the key on a KeyCap. </summary>
	public static Color KeyText => theme.LightPanel ? theme.Panel : Ink;

	// ---- Menus ----

	static Theme menu;
	/// <summary>
	/// The menus' theme (pause, dev menu): panel buttons that light up in the accent on hover and focus, pill sliders,
	/// and a round grabber. Set on a menu's root; its own overrides still win. Rebuilt when the theme changes.
	/// </summary>
	public static Theme Menu => menu ??= BuildMenu();

	static StyleBoxFlat Button( Color fill, Color border, bool hover )
	{
		var b = Frame( 0.7f, fill, 14 );
		b.BorderColor = border;
		if ( theme.Frame == HudTheme.FrameLook.Brackets ) b.BorderWidthLeft = hover ? 2 : 1;
		else b.BorderWidthBottom = hover ? b.BorderWidthBottom : b.BorderWidthBottom + 1;
		return b;
	}

	static Theme BuildMenu()
	{
		var deep = PanelDeep;
		var light = theme.LightPanel;
		var normal = light ? PanelDeep : deep with { A = 0.88f };
		var hover = light ? Teal : deep.Lerp( Teal, 0.22f ) with { A = 0.95f };
		var press = light ? Gold : deep.Lerp( Teal, 0.34f ) with { A = 1f };
		var edge = theme.Frame switch { HudTheme.FrameLook.Suit => Ink, HudTheme.FrameLook.Brackets => Teal with { A = 0.25f }, _ => Frame().BorderColor };
		var t = new Theme { DefaultFont = Label };
		t.SetStylebox( "normal", "Button", Button( normal, edge, false ) );
		t.SetStylebox( "hover", "Button", Button( hover, light ? Ink : Teal, true ) );
		t.SetStylebox( "pressed", "Button", Button( press, light ? Ink : Teal, true ) );
		t.SetStylebox( "hover_pressed", "Button", Button( press, light ? Ink : Teal, true ) );
		var focus = new StyleBoxFlat { DrawCenter = false, BorderColor = Gold, ExpandMarginLeft = 3, ExpandMarginRight = 3, ExpandMarginTop = 3, ExpandMarginBottom = 3 };
		focus.SetBorderWidthAll( 2 );
		focus.SetCornerRadiusAll( 6 );
		t.SetStylebox( "focus", "Button", focus );
		t.SetStylebox( "disabled", "Button", Button( normal with { A = 0.5f }, edge with { A = 0.3f }, false ) );
		t.SetColor( "font_color", "Button", Colors.White );
		t.SetColor( "font_hover_color", "Button", light ? Ink : Teal );
		t.SetColor( "font_focus_color", "Button", Colors.White );
		t.SetColor( "font_pressed_color", "Button", light ? Ink : Gold );
		t.SetColor( "font_hover_pressed_color", "Button", light ? Ink : Gold );
		t.SetColor( "font_outline_color", "Button", Ink );
		t.SetConstant( "outline_size", "Button", 4 );
		t.SetFont( "font", "Button", LabelBold );
		t.SetColor( "font_color", "Label", Colors.White );
		// Sliders: a dark pill track, an accent fill, a round grabber with an ink edge.
		var track = new StyleBoxFlat { BgColor = new Color( 1f, 1f, 1f, 0.1f ), BorderColor = Ink, ContentMarginTop = 5, ContentMarginBottom = 5 };
		track.SetBorderWidthAll( 2 );
		track.SetCornerRadiusAll( 99 );
		var fill = (StyleBoxFlat)track.Duplicate();
		fill.BgColor = Teal;
		t.SetStylebox( "slider", "HSlider", track );
		t.SetStylebox( "grabber_area", "HSlider", fill );
		t.SetStylebox( "grabber_area_highlight", "HSlider", fill );
		t.SetIcon( "grabber", "HSlider", Knob( Teal ) );
		t.SetIcon( "grabber_highlight", "HSlider", Knob( Gold ) );
		t.SetStylebox( "separator", "HSeparator", new StyleBoxLine { Color = Teal with { A = 0.35f }, Thickness = 2 } );
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
	/// A bar in the theme's style (pills, ticks, bubbles, inked blocks, LCD segments, glowing orbs or one liquid tube)
	/// filled to `fraction`, empty slots after it, and an optional pale `lag` behind the fill (health just lost).
	/// </summary>
	public static void DrawPills( CanvasItem c, Rect2 rect, int segments, float fraction, Color fill, Color top, float alpha = 1f, float lag = -1f, float gap = 4f )
	{
		segments = Math.Max( 1, segments );
		var look = theme.Bars;
		if ( segments == 1 && look is HudTheme.BarLook.Bubbles or HudTheme.BarLook.Orbs ) look = HudTheme.BarLook.Tube;
		if ( segments == 1 && look == HudTheme.BarLook.Ticks ) gap = 0f;
		switch ( look )
		{
			case HudTheme.BarLook.Tube: DrawTube( c, rect, fraction, fill, top, alpha, lag ); return;
			case HudTheme.BarLook.Bubbles: DrawBubbles( c, rect, segments, fraction, fill, top, alpha, lag ); return;
			case HudTheme.BarLook.Orbs: DrawOrbs( c, rect, segments, fraction, fill, top, alpha ); return;
		}
		var w = (rect.Size.X - gap * (segments - 1)) / segments;
		var h = rect.Size.Y;
		var skew = look == HudTheme.BarLook.Blocks ? MathF.Min( h * 0.3f, w * 0.3f ) : MathF.Min( h * 0.35f, w * 0.4f );
		for ( var i = 0; i < segments; i++ )
		{
			var x = rect.Position.X + i * (w + gap);
			var y = rect.Position.Y;
			var r = new Rect2( x, y, w, h );
			var full = MathX.Clamp( fraction * segments - i, 0f, 1f );
			var lagged = lag >= 0f ? MathX.Clamp( lag * segments - i, 0f, 1f ) : 0f;
			switch ( look )
			{
				case HudTheme.BarLook.Ticks:
					Tick( c, x, y, w, h, skew, new Color( 0.5f, 0.96f, 1f, 0.1f * alpha ) );
					if ( lagged > full ) Tick( c, x, y, w * lagged, h, skew, new Color( 1f, 1f, 1f, 0.5f * alpha ) );
					if ( full <= 0f ) continue;
					var fw = MathF.Max( skew * 1.5f, w * full );
					Tick( c, x - 1.5f, y - 1.5f, fw + 3f, h + 3f, skew, fill with { A = 0.22f * alpha } );   // glow
					Tick( c, x, y, fw, h, skew, fill with { A = alpha } );
					Tick( c, x, y, fw, MathF.Max( 1f, h * 0.22f ), skew * 0.22f, top with { A = 0.8f * alpha } );
					break;
				case HudTheme.BarLook.Blocks:
					// Inked comic blocks: a thick black edge, hatched when empty.
					Tick( c, x - 2f, y - 2f, w + 4f, h + 4f, skew, Ink with { A = alpha } );
					Tick( c, x, y, w, h, skew, new Color( "fff6e0" ) with { A = alpha } );
					if ( full <= 0f && lagged <= 0f )
						for ( var hx = x + 2f; hx < x + w; hx += 4f ) c.DrawLine( new Vector2( hx, y + h ), new Vector2( MathF.Min( x + w, hx + h * 0.6f ), y ), Ink with { A = 0.5f * alpha }, 1f );
					if ( lagged > full ) Tick( c, x, y, w * lagged, h, skew, new Color( 1f, 0.75f, 0.75f, alpha ) );
					if ( full > 0f ) Tick( c, x, y, MathF.Max( skew * 1.5f, w * full ), h, skew, fill with { A = alpha } );
					break;
				case HudTheme.BarLook.Segments:
					// Flat LCD segments: dim when empty.
					c.DrawRect( r, fill with { A = 0.14f * alpha } );
					if ( lagged > full ) c.DrawRect( new Rect2( r.Position, new Vector2( w * lagged, h ) ), fill with { A = 0.4f * alpha } );
					if ( full > 0f ) c.DrawRect( new Rect2( r.Position, new Vector2( w * full, h ) ), fill with { A = alpha } );
					break;
				default:   // Pills (Arcade Dive): glossy pill segments with an ink edge.
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
					var pw = MathF.Max( MathF.Min( h, w ), w * full );
					pill.BgColor = fill with { A = alpha };
					c.DrawStyleBox( pill, new Rect2( r.Position, new Vector2( pw, h ) ) );
					// The gloss: a lighter band along the top.
					pill.BgColor = top with { A = 0.85f * alpha };
					c.DrawStyleBox( pill, new Rect2( r.Position + new Vector2( h * 0.2f, h * 0.12f ), new Vector2( MathF.Max( 0f, pw - h * 0.4f ), h * 0.32f ) ) );
					break;
			}
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

	/// <summary> One glass tube with liquid in it (a pipe in brass, a sticker strip, a fish tank). </summary>
	static void DrawTube( CanvasItem c, Rect2 rect, float fraction, Color fill, Color top, float alpha, float lag )
	{
		var h = rect.Size.Y;
		var brass = theme.Frame == HudTheme.FrameLook.Brass;
		pill.BgColor = (brass ? new Color( "2a0f1e" ) : new Color( 0.05f, 0.12f, 0.2f, 0.55f )) with { A = (brass ? 0.95f : 0.55f) * alpha };
		pill.BorderColor = (brass ? new Color( "c4913a" ) : new Color( 0.9f, 1f, 1f, 0.75f )) with { A = alpha };
		pill.SetBorderWidthAll( Math.Max( 1, (int)(h * 0.16f) ) );
		c.DrawStyleBox( pill, rect.Grow( MathF.Max( 1f, h * 0.16f ) ) );
		pill.SetBorderWidthAll( 0 );
		if ( lag > fraction )
		{
			pill.BgColor = new Color( 1f, 1f, 1f, 0.45f * alpha );
			c.DrawStyleBox( pill, new Rect2( rect.Position, new Vector2( MathF.Max( h, rect.Size.X * lag ), h ) ) );
		}
		if ( fraction <= 0f ) return;
		var w = MathF.Max( h, rect.Size.X * fraction );
		pill.BgColor = fill with { A = alpha };
		c.DrawStyleBox( pill, new Rect2( rect.Position, new Vector2( w, h ) ) );
		pill.BgColor = top with { A = 0.6f * alpha };
		c.DrawStyleBox( pill, new Rect2( rect.Position + new Vector2( h * 0.3f, h * 0.14f ), new Vector2( MathF.Max( 0f, w - h * 0.6f ), h * 0.26f ) ) );
		// A few fizz bubbles rising in the liquid.
		var now = (float)Clock.Now;
		for ( var i = 0; i < 4; i++ )
		{
			var bx = rect.Position.X + w * (0.12f + i * 0.23f);
			var rise = (now * 0.7f + i * 0.31f) % 1f;
			c.DrawCircle( new Vector2( bx, rect.End.Y - rise * h ), MathF.Max( 1f, h * 0.1f ), new Color( 1f, 1f, 1f, 0.55f * (1f - rise) * alpha ) );
		}
	}

	/// <summary> A row of bubbles: full ones tinted and glossy, empty ones clear rings. </summary>
	static void DrawBubbles( CanvasItem c, Rect2 rect, int segments, float fraction, Color fill, Color top, float alpha, float lag )
	{
		var step = rect.Size.X / segments;
		var r = MathF.Min( step * 0.45f, rect.Size.Y * 0.5f + 2f );
		for ( var i = 0; i < segments; i++ )
		{
			var at = new Vector2( rect.Position.X + step * (i + 0.5f), rect.Position.Y + rect.Size.Y * 0.5f );
			var full = MathX.Clamp( fraction * segments - i, 0f, 1f );
			var lagged = lag >= 0f ? MathX.Clamp( lag * segments - i, 0f, 1f ) : 0f;
			if ( lagged > full )
			{
				// Just lost: the bubble swells and fades as it pops.
				var popR = r * (1f + (1f - lagged) * 0.8f);
				c.DrawArc( at, popR, 0f, MathF.Tau, 20, new Color( 1f, 0.85f, 0.85f, lagged * alpha ), 1.5f, true );
			}
			c.DrawArc( at, r, 0f, MathF.Tau, 20, new Color( 0.9f, 1f, 1f, 0.55f * alpha ), 1.5f, true );
			if ( full > 0f )
			{
				c.DrawCircle( at, r * MathF.Sqrt( full ), fill with { A = alpha } );
				c.DrawCircle( at - new Vector2( r * 0.3f, r * 0.32f ), r * 0.24f * MathF.Sqrt( full ), top with { A = 0.9f * alpha } );
			}
			else c.DrawCircle( at - new Vector2( r * 0.3f, r * 0.32f ), r * 0.18f, new Color( 1f, 1f, 1f, 0.5f * alpha ) );
		}
	}

	/// <summary> A row of soft glowing orbs (Deep Glow): lit ones pulse, spent ones are dark. </summary>
	static void DrawOrbs( CanvasItem c, Rect2 rect, int segments, float fraction, Color fill, Color top, float alpha )
	{
		var step = rect.Size.X / segments;
		var r = MathF.Min( step * 0.32f, rect.Size.Y * 0.5f + 1f );
		var now = (float)Clock.Now;
		for ( var i = 0; i < segments; i++ )
		{
			var at = new Vector2( rect.Position.X + step * (i + 0.5f), rect.Position.Y + rect.Size.Y * 0.5f + MathF.Sin( i * 0.7f + now * 2f ) * rect.Size.Y * 0.2f );
			var full = MathX.Clamp( fraction * segments - i, 0f, 1f );
			if ( full <= 0f ) { c.DrawCircle( at, r * 0.8f, new Color( "20333d" ) with { A = 0.8f * alpha } ); continue; }
			var pulse = 1f + 0.15f * MathF.Sin( now * 5f + i * 0.5f );
			Glow( c, at, r * pulse, fill, alpha * full );
			c.DrawCircle( at, r * 0.45f * pulse, top with { A = alpha * full } );
		}
	}

	/// <summary> A soft glowing dot: a few fading rings round a solid middle. </summary>
	public static void Glow( CanvasItem c, Vector2 at, float r, Color colour, float alpha = 1f )
	{
		for ( var k = 4; k >= 1; k-- ) c.DrawCircle( at, r * (1f + k * 0.45f), colour with { A = 0.08f * alpha } );
		c.DrawCircle( at, r, colour with { A = alpha } );
	}

	// ---- Text in a _Draw ----

	/// <summary> A big number or banner in a _Draw, in the theme's style (`onPanel`: on a pale panel it stays flat). </summary>
	public static void DrawBig( CanvasItem c, Vector2 at, string text, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f, float alpha = 1f, bool onPanel = false )
	{
		var f = Number;
		var look = onPanel && theme.LightPanel && theme.Big != HudTheme.BigLook.Comic ? HudTheme.BigLook.Flat : theme.Big;
		var main = colour with { A = colour.A * alpha };
		switch ( look )
		{
			case HudTheme.BigLook.Split:
				var split = new Vector2( Math.Max( 1, size / 22 ) + VisorOverlay.Glitch * 5f, 0f );
				c.DrawStringOutline( f, at, text, align, width, size, Math.Max( 3, size / 12 ), Ink with { A = 0.7f * alpha } );
				c.DrawString( f, at - split, text, align, width, size, SplitRed with { A = SplitRed.A * alpha } );
				c.DrawString( f, at + split, text, align, width, size, SplitBlue with { A = SplitBlue.A * alpha } );
				c.DrawString( f, at, text, align, width, size, main );
				break;
			case HudTheme.BigLook.Glow:
				var halo = colour.Lerp( theme.Accent, 0.6f );
				c.DrawStringOutline( f, at, text, align, width, size, Math.Max( 10, size / 2 ), halo with { A = 0.1f * alpha } );
				c.DrawStringOutline( f, at, text, align, width, size, Math.Max( 5, size / 5 ), halo with { A = 0.22f * alpha } );
				c.DrawString( f, at, text, align, width, size, main );
				break;
			case HudTheme.BigLook.Flat:
				c.DrawString( f, at + new Vector2( 0f, Math.Max( 1, size / 24 ) ), text, align, width, size, Ink with { A = (onPanel ? 0.12f : 0.6f) * alpha } );
				c.DrawString( f, at, text, align, width, size, main );
				break;
			case HudTheme.BigLook.Comic:
				var o = Math.Max( 6, size / 5 );
				var drop = new Vector2( Math.Max( 2, size / 14 ), Math.Max( 2, size / 14 ) );
				c.DrawStringOutline( f, at + drop, text, align, width, size, o, Ink with { A = alpha } );
				c.DrawStringOutline( f, at, text, align, width, size, o, Ink with { A = alpha } );
				c.DrawString( f, at, text, align, width, size, main );
				break;
			default:   // Cartoon (Arcade Dive): ink outline and a dropped shadow.
				var outline = Math.Max( 6, size / 6 );
				var down = new Vector2( 0f, Math.Max( 2, size / 12 ) );
				c.DrawStringOutline( f, at + down, text, align, width, size, outline, Ink with { A = alpha } );
				c.DrawString( f, at + down, text, align, width, size, Ink with { A = alpha } );
				c.DrawStringOutline( f, at, text, align, width, size, outline, Ink with { A = alpha } );
				c.DrawString( f, at, text, align, width, size, main );
				break;
		}
	}

	/// <summary> A small caption or readout in a _Draw, with a soft ink outline (none on a pale panel). </summary>
	public static void DrawTag( CanvasItem c, Vector2 at, string text, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f, bool mono = false, bool caps = true, bool onPanel = false )
	{
		var f = mono ? MonoWide : Caption;
		if ( caps ) text = text.ToUpperInvariant();
		if ( !(onPanel && theme.LightPanel) ) c.DrawStringOutline( f, at, text, align, width, size, Math.Max( 4, size / 4 ), Ink with { A = 0.85f * colour.A } );
		c.DrawString( f, at, text, align, width, size, colour );
	}

	/// <summary> Running text in a _Draw (Chakra Petch, soft outline). </summary>
	public static void DrawText( CanvasItem c, Vector2 at, string text, int size, Color colour, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f )
	{
		c.DrawStringOutline( Label, at, text, align, width, size, Math.Max( 4, size / 5 ), Ink with { A = 0.8f * colour.A } );
		c.DrawString( Label, at, text, align, width, size, colour );
	}

	/// <summary> Width of a string in the HUD number font. </summary>
	public static float BigWidth( string text, int size ) => Number.GetStringSize( text, HorizontalAlignment.Left, -1f, size ).X;
	public static float TagWidth( string text, int size, bool mono = false ) => (mono ? MonoWide : Caption).GetStringSize( text.ToUpperInvariant(), HorizontalAlignment.Left, -1f, size ).X;
}
