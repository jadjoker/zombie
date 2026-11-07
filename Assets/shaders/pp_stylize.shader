HEADER
{
	Description = "Global stylize post-process: ink outlines, toon bands, painterly, retro dither, storybook";
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

		return float4( lerp( src, saturate( c ), g_flStrength ), 1 );
	}
}
