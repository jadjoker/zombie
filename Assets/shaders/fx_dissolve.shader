HEADER
{
	Description = "Burn-away dissolve with a glowing edge (loops)";
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

	float3 g_vEdgeColor < UiType( Color ); Default3( 1.0, 0.45, 0.08 ); UiGroup( "Dissolve,10/10" ); >;
	float g_flEdgeWidth < Default( 0.07 ); Range( 0.01, 0.3 ); UiGroup( "Dissolve,10/20" ); >;
	float g_flCycle < Default( 0.22 ); Range( 0.02, 2.0 ); UiGroup( "Dissolve,10/30" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );
		float3 wp = m.WorldPosition;
		float3 viewDir = normalize( g_vCameraPositionWs - wp );
		float rim = 1.0 - saturate( dot( m.Normal, viewDir ) );
		float t = g_flTime;

		float n = saturate( ( FBm( wp * 0.045 ) - 0.28 ) * 1.9 );   // stretch the noise so it burns away gradually
		float th = frac( t * g_flCycle ) * 1.2 - 0.1;
		float d = n - th;
		clip( d );
		float edge = 1.0 - smoothstep( 0.0, g_flEdgeWidth, d );
		m.Emission += g_vEdgeColor * edge * 4.0;
		m.Albedo = lerp( m.Albedo, float3( 0.02, 0.01, 0.01 ), edge );

		#if ( S_MODE_DEPTH )
			return 1;
		#endif

		return ShadingModelStandard::Shade( m );
	}
}
