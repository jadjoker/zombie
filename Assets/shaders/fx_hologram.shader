HEADER
{
	Description = "Scanline hologram";
	DevShader = false;
	DebugInfo = false;
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		return FinalizeVertex( o );
	}
}

PS
{
	StaticCombo( S_MODE_DEPTH, 0..1, Sys( ALL ) );

	#include "common/utils/Material.CommonInputs.hlsl"
	#include "common/pixel.hlsl"

	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT );

	// ---- tiny procedural noise (no textures needed) ----
	float Hash31( float3 p )
	{
		p = frac( p * 0.1031 );
		p += dot( p, p.zyx + 31.32 );
		return frac( ( p.x + p.y ) * p.z );
	}

	float VNoise( float3 p )
	{
		float3 i = floor( p );
		float3 f = frac( p );
		f = f * f * ( 3.0 - 2.0 * f );
		float n000 = Hash31( i );
		float n100 = Hash31( i + float3( 1, 0, 0 ) );
		float n010 = Hash31( i + float3( 0, 1, 0 ) );
		float n110 = Hash31( i + float3( 1, 1, 0 ) );
		float n001 = Hash31( i + float3( 0, 0, 1 ) );
		float n101 = Hash31( i + float3( 1, 0, 1 ) );
		float n011 = Hash31( i + float3( 0, 1, 1 ) );
		float n111 = Hash31( i + float3( 1, 1, 1 ) );
		return lerp( lerp( lerp( n000, n100, f.x ), lerp( n010, n110, f.x ), f.y ),
					 lerp( lerp( n001, n101, f.x ), lerp( n011, n111, f.x ), f.y ), f.z );
	}

	float FBm( float3 p )
	{
		return VNoise( p ) * 0.5 + VNoise( p * 2.07 ) * 0.3 + VNoise( p * 4.13 ) * 0.2;
	}

	float3 Hsv( float h, float s, float v )
	{
		float3 k = frac( h + float3( 0.0, 2.0 / 3.0, 1.0 / 3.0 ) );
		float3 p = abs( k * 6.0 - 3.0 );
		return v * lerp( 1.0, saturate( p - 1.0 ), s );
	}

	float3 g_vHoloColor < UiType( Color ); Default3( 0.1, 0.9, 1.0 ); UiGroup( "Hologram,10/10" ); >;
	float g_flLineDensity < Default( 0.5 ); Range( 0.05, 3.0 ); UiGroup( "Hologram,10/20" ); >;
	float g_flScrollSpeed < Default( 0.9 ); Range( 0.0, 4.0 ); UiGroup( "Hologram,10/30" ); >;
	float g_flRevealZ < Attribute( "RevealZ" ); Default( 1000000.0 ); >;   // world height below which he has scanned in

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );
		float3 wp = m.WorldPosition;
		float3 viewDir = normalize( g_vCameraPositionWs - wp );
		float rim = 1.0 - saturate( dot( m.Normal, viewDir ) );
		float t = g_flTime;

		clip( g_flRevealZ - wp.z );
		float revealEdge = saturate( 1.0 - ( g_flRevealZ - wp.z ) / 10.0 );
		float lines = frac( wp.z * g_flLineDensity - t * g_flScrollSpeed );
		clip( lines - 0.28 );
		float flicker = 0.85 + 0.15 * sin( t * 43.0 ) * sin( t * 7.0 );
		float sweep = pow( saturate( 1.0 - abs( frac( wp.z * 0.012 - t * 0.35 ) - 0.5 ) * 8.0 ), 2.0 );
		m.Albedo = g_vHoloColor * 0.15;
		m.Emission = g_vHoloColor * ( 0.35 + pow( rim, 1.6 ) * 2.4 + sweep * 2.0 ) * flicker + float3( 0.6, 1.0, 1.0 ) * revealEdge * 3.0;

		#if ( S_MODE_DEPTH )
			return 1;
		#endif

		return ShadingModelStandard::Shade( m );
	}
}
