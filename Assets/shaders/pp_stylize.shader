HEADER
{
	Description = "Global stylize post-process: ink outlines, toon, painterly, retro, storybook, comic, anime, sketch, watercolor, riso, flat";
	DevShader = true;
}

MODES
{
	Default();
	Forward();
}

FEATURES
{
}

COMMON
{
	#include "postprocess/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
	float2 uv : TEXCOORD0;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif

	#if ( ( PROGRAM == VFX_PROGRAM_PS ) )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;
		o.vPositionPs = float4( i.vPositionOs.xy, 0.0f, 1.0f );
		o.uv = i.vTexCoord;
		return o;
	}
}

PS
{
	#include "postprocess/common.hlsl"
	#include "common/classes/Depth.hlsl"

	Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;

	int g_nStyle < Attribute( "Style" ); Default( 0 ); >;
	float g_flStrength < Attribute( "Strength" ); Default( 1.0 ); >;
	float g_flOutline < Attribute( "Outline" ); Default( 1.0 ); >;
	float g_flOutlineWidth < Attribute( "OutlineWidth" ); Default( 1.0 ); >;
	float3 g_vInk < Attribute( "InkColor" ); Default3( 0.05, 0.05, 0.08 ); >;
	float g_flBands < Attribute( "Bands" ); Default( 4.0 ); >;
	float g_flPixel < Attribute( "PixelSize" ); Default( 3.0 ); >;

	float3 Fetch( float2 uv ) { return g_tColorBuffer.SampleLevel( g_sTrilinearClamp, uv, 0 ).rgb; }
	float Luma( float3 c ) { return dot( c, float3( 0.299, 0.587, 0.114 ) ); }

	float LinDepth( float2 px )
	{
		px = clamp( px, 0, g_vRenderTargetSize - 1 );
		return Depth::GetLinear( px );
	}

	float DepthEdge( float2 px )
	{
		float w = g_flOutlineWidth;
		float d = LinDepth( px );
		float lap = abs( LinDepth( px + float2( -w, 0 ) ) + LinDepth( px + float2( w, 0 ) ) + LinDepth( px + float2( 0, -w ) ) + LinDepth( px + float2( 0, w ) ) - 4.0 * d ) / max( d, 1.0 );
		return smoothstep( 0.015, 0.05, lap ) * ( 1.0 - smoothstep( 1500.0, 6000.0, d ) );
	}

	// 0..1 edge from depth discontinuities (relative, so far objects still outline) and colour steps.
	float Edge( float2 px, float2 uv )
	{
		float w = g_flOutlineWidth;
		float d = LinDepth( px );
		float dl = LinDepth( px + float2( -w, 0 ) );
		float dr = LinDepth( px + float2( w, 0 ) );
		float du = LinDepth( px + float2( 0, -w ) );
		float dd = LinDepth( px + float2( 0, w ) );
		// Second derivative ignores smooth slopes (floors) and catches silhouettes.
		float lap = abs( dl + dr + du + dd - 4.0 * d ) / max( d, 1.0 );
		float depthEdge = smoothstep( 0.015, 0.05, lap );

		float2 t = w / g_vRenderTargetSize;
		float3 c = Fetch( uv );
		float colEdge = length( Fetch( uv + float2( t.x, 0 ) ) - c ) + length( Fetch( uv + float2( 0, t.y ) ) - c );
		colEdge = smoothstep( 0.35, 0.8, colEdge );

		// Fade lines with distance so the horizon doesn't turn to scribble.
		float fade = 1.0 - smoothstep( 1500.0, 6000.0, d );
		return saturate( max( depthEdge, colEdge * 0.5 ) * fade );
	}

	float3 Toon( float3 c )
	{
		float l = Luma( c );
		float bands = max( g_flBands, 2.0 );
		float pl = sqrt( saturate( l ) );
		float f = pl * bands;
		float step0 = floor( f );
		// Narrow soft transition between bands instead of hard aliasing.
		float q = ( step0 + smoothstep( 0.4, 0.6, frac( f ) ) ) / bands;
		q = max( q, 0.35 / bands );
		q = q * q;
		float3 hue = c / max( l, 1e-4 );
		float3 o = hue * q;
		// Push saturation a little, toon art is punchy.
		return lerp( Luma( o ).xxx, o, 1.2 );
	}

	// 4-sector Kuwahara: flattens detail into painted strokes while keeping edges.
	float3 Kuwahara( float2 uv )
	{
		const int R = 4;
		float2 t = 1.0 / g_vRenderTargetSize;
		float3 mean[4];
		float var[4];
		[unroll] for ( int k = 0; k < 4; k++ )
		{
			float2 dir = float2( ( k & 1 ) ? 1 : -1, ( k & 2 ) ? 1 : -1 );
			float3 s = 0; float3 s2 = 0;
			[unroll] for ( int y = 0; y <= R; y++ )
			[unroll] for ( int x = 0; x <= R; x++ )
			{
				float3 c = Fetch( uv + float2( x, y ) * dir * t * 1.5 );
				s += c; s2 += c * c;
			}
			float n = ( R + 1 ) * ( R + 1 );
			mean[k] = s / n;
			float3 v = s2 / n - mean[k] * mean[k];
			var[k] = v.r + v.g + v.b;
		}
		float3 best = mean[0]; float bv = var[0];
		[unroll] for ( int j = 1; j < 4; j++ ) { if ( var[j] < bv ) { bv = var[j]; best = mean[j]; } }
		return best;
	}

	float Bayer4( float2 px )
	{
		int2 p = int2( px ) & 3;
		const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
		return m[p.y * 4 + p.x] / 16.0 - 0.5;
	}

	float Hash( float2 p ) { return frac( sin( dot( p, float2( 12.9898, 78.233 ) ) ) * 43758.5453 ); }

	// A round halftone dot for this pixel: 1 inside the dot. `amount` (0..1) sets how big the dots are.
	float Dots( float2 px, float angle, float amount, float scale )
	{
		float s = sin( angle ); float co = cos( angle );
		float2 p = float2( co * px.x - s * px.y, s * px.x + co * px.y ) / max( scale, 1.0 );
		float2 cell = frac( p ) - 0.5;
		float r = sqrt( saturate( amount ) ) * 0.72;
		return 1.0 - smoothstep( r - 0.1, r + 0.1, length( cell ) );
	}

	// A thin repeating line along `coord`.
	float Line( float coord, float spacing )
	{
		float v = abs( frac( coord / max( spacing, 1.0 ) ) - 0.5 ) * 2.0;
		return smoothstep( 0.7, 0.86, v );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 px = i.vPositionSs.xy;
		float2 uv = CalculateViewportUv( px );
		float3 src = Fetch( uv );
		float3 c = src;

		if ( g_nStyle == 1 ) // Ink outlines
		{
			c = lerp( c, g_vInk, Edge( px, uv ) * g_flOutline );
		}
		else if ( g_nStyle == 2 ) // Toon / cel
		{
			c = Toon( c );
			c = lerp( c, g_vInk, DepthEdge( px ) * g_flOutline );
		}
		else if ( g_nStyle == 3 ) // Painterly
		{
			c = Kuwahara( uv );
			c = lerp( c, c * 0.35, Edge( px, uv ) * g_flOutline * 0.6 );
		}
		else if ( g_nStyle == 4 ) // Retro: chunky pixels, ordered dither, reduced palette
		{
			float2 cell = max( g_flPixel, 1.0 );
			float2 spx = ( floor( px / cell ) + 0.5 ) * cell;
			float3 p = Fetch( CalculateViewportUv( spx ) );
			float levels = 10.0;
			p = floor( p * levels + Bayer4( spx / cell ) * 0.6 + 0.5 ) / levels;
			c = saturate( p );
		}
		else if ( g_nStyle == 5 ) // Storybook: soft warm grade, paper grain, light ink
		{
			float l = Luma( c );
			c = lerp( l.xxx, c, 0.8 );
			c = c * float3( 1.12, 1.08, 0.98 ) + float3( 0.05, 0.045, 0.02 );
			c = lerp( c, smoothstep( 0.0, 1.0, c ), 0.5 );
			float paper = ( Hash( floor( px / 3.0 ) ) + Hash( floor( px / 9.0 ) + 7.0 ) ) * 0.015 - 0.015;
			c += paper;
			c = lerp( c, g_vInk * 1.5, Edge( px, uv ) * g_flOutline * 0.6 );
		}

		else if ( g_nStyle == 6 ) // Comic: flat bands, halftone dots in the shadows, bold ink
		{
			float3 base = Toon( c );
			float shade = sqrt( saturate( Luma( base ) ) );
			float dots = Dots( px, 0.7854, 1.0 - shade, g_flPixel * 2.0 ) * smoothstep( 0.7, 0.3, shade );
			c = lerp( base, base * 0.4, dots );
			c = lerp( c, g_vInk, Edge( px, uv ) * g_flOutline );
		}
		else if ( g_nStyle == 7 ) // Anime: three soft tones, cool shadows and warm lights, tinted outlines
		{
			float3 lifted = c * 1.35;
			float l = sqrt( saturate( Luma( lifted ) ) );
			float tone = smoothstep( 0.24, 0.32, l ) * 0.5 + smoothstep( 0.5, 0.58, l ) * 0.5;
			float3 shadow = lifted * float3( 0.78, 0.84, 1.15 ) * 0.9;
			float3 light = lifted * float3( 1.12, 1.05, 0.94 );
			float3 o = lerp( shadow, light, tone );
			o = lerp( Luma( o ).xxx, o, 1.25 );
			float ink = max( DepthEdge( px ), Edge( px, uv ) * 0.6 ) * g_flOutline;
			c = lerp( o, o * 0.2 + g_vInk * 0.5, ink );
		}
		else if ( g_nStyle == 8 ) // Sketch: pencil crosshatch on paper
		{
			float l = saturate( sqrt( saturate( Luma( c ) ) ) * 1.5 );
			float spacing = max( g_flPixel * 2.4, 4.0 );
			float a = Line( px.x + px.y, spacing ) * step( l, 0.7 );
			float b = Line( px.x - px.y, spacing ) * step( l, 0.45 );
			float h = Line( px.y, spacing * 0.9 ) * step( l, 0.25 );
			float dark = saturate( max( a, max( b, h ) ) );
			float3 paper = float3( 0.97, 0.95, 0.88 ) + ( Hash( floor( px / 2.0 ) ) - 0.5 ) * 0.04;
			float3 pencil = float3( 0.14, 0.14, 0.18 );
			float3 o = lerp( paper, pencil, dark * 0.85 );
			o = lerp( o, o * ( src * 1.8 + 0.3 ), 0.5 );   // a wash of the real colours
			c = lerp( o, pencil, Edge( px, uv ) * g_flOutline * 0.9 );
		}
		else if ( g_nStyle == 9 ) // Watercolor: bleeding washes, pooled edges, paper grain
		{
			float3 k = Kuwahara( uv );
			float3 wash = lerp( Luma( k ).xxx, k, 1.25 ) * 1.2 + 0.03;
			float edge = Edge( px, uv );
			float3 o = lerp( wash, wash * 0.55, edge * 0.8 * g_flOutline );
			float blotch = Hash( floor( px / 6.0 ) + 3.0 ) * 0.5 + Hash( floor( px / 17.0 ) + 9.0 ) * 0.5;
			o = lerp( o, float3( 0.98, 0.96, 0.9 ), 0.03 + blotch * 0.1 );
			o += ( Hash( floor( px / 2.0 ) ) - 0.5 ) * 0.03;
			c = o;
		}
		else if ( g_nStyle == 10 ) // Riso: two inks printed in dots on paper, slightly out of register
		{
			float l = saturate( ( sqrt( saturate( Luma( c ) ) ) - 0.12 ) * 1.5 );
			float3 shifted = Fetch( uv + float2( 2.5, 1.5 ) / g_vRenderTargetSize );
			float a1 = Dots( px, 0.26, saturate( 1.0 - l ) * 0.95, g_flPixel * 1.8 );
			float warm = saturate( shifted.r - shifted.g * 0.8 ) * 1.6 + saturate( 1.0 - l ) * 0.35;
			float a2 = Dots( px + float2( 2.5, 1.5 ), 1.3, saturate( warm ), g_flPixel * 1.8 );
			float3 paper = float3( 0.97, 0.94, 0.86 );
			float3 ink1 = float3( 0.02, 0.45, 0.6 );
			float3 ink2 = float3( 0.96, 0.27, 0.46 );
			c = paper * lerp( float3( 1, 1, 1 ), ink1, a1 ) * lerp( float3( 1, 1, 1 ), ink2, a2 * 0.85 );
			c = lerp( c, c * 0.45, DepthEdge( px ) * g_flOutline * 0.7 );
			c += ( Hash( floor( px ) ) - 0.5 ) * 0.025;
		}
		else if ( g_nStyle == 11 ) // Flat: posterised colour like a vector illustration, clean ink lines
		{
			float levels = max( g_flBands, 2.0 ) + 1.0;
			float3 g = pow( saturate( c ), 0.5 );
			g = floor( g * levels + 0.5 ) / levels;
			float3 o = g * g;
			o = lerp( Luma( o ).xxx, o, 1.25 );
			c = lerp( o, g_vInk, DepthEdge( px ) * g_flOutline );
		}

		return float4( lerp( src, saturate( c ), g_flStrength ), 1 );
	}
}
