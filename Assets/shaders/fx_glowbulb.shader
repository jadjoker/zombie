HEADER
{
	Description = "Self-lit glowing bulb: pure emission in a colour set from code, so it shines in the dark";
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

	float3 g_vGlowColor < Attribute( "GlowColor" ); Default3( 0.5, 1.0, 0.9 ); >;
	float g_flGlowStrength < Attribute( "GlowStrength" ); Default( 4.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );
		float3 viewDir = normalize( g_vCameraPositionWs - m.WorldPosition );
		float facing = saturate( dot( m.Normal, viewDir ) );
		// Hot white core, coloured edge.
		float3 core = lerp( g_vGlowColor, float3( 1, 1, 1 ), pow( facing, 2.5 ) * 0.12 );
		m.Albedo = g_vGlowColor * 0.3;
		m.Emission = core * g_flGlowStrength;

		#if ( S_MODE_DEPTH )
			return 1;
		#endif

		return ShadingModelStandard::Shade( m );
	}
}
