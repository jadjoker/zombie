HEADER
{
	Description = "World-space triplanar material for UV-less nature props: base texture tinted by the renderer, two-tone colour variation, and an untinted top layer (moss, grass) on upward faces";
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

	SamplerState g_sTriSampler < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	// Base layer
	CreateInputTexture2D( BaseColor, Srgb, 8, "", "_color", "Base,10/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( BaseNormal, Linear, 8, "", "_normal", "Base,10/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( BaseRough, Linear, 8, "", "_rough", "Base,10/30", Default( 0.8 ) );
	Texture2D g_tBaseColor < Channel( RGB, Box( BaseColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tBaseNormal < Channel( RGB, Box( BaseNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tBaseRough < Channel( R, Box( BaseRough ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	float g_flBaseScale < Default( 64.0 ); Range( 1.0, 2048.0 ); UiGroup( "Base,10/40" ); >;
	float g_flNormalStrength < Default( 1.0 ); Range( 0.0, 3.0 ); UiGroup( "Base,10/50" ); >;

	// Top layer (moss / grass), not tinted
	CreateInputTexture2D( TopColor, Srgb, 8, "", "_color", "Top,20/10", Default3( 0.3, 0.45, 0.15 ) );
	CreateInputTexture2D( TopNormal, Linear, 8, "", "_normal", "Top,20/20", Default3( 0.5, 0.5, 1.0 ) );
	Texture2D g_tTopColor < Channel( RGB, Box( TopColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tTopNormal < Channel( RGB, Box( TopNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	float g_flTopScale < Default( 96.0 ); Range( 1.0, 2048.0 ); UiGroup( "Top,20/30" ); >;
	float g_flTopAmount < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Top,20/40" ); >;
	float g_flTopSharpness < Default( 6.0 ); Range( 1.0, 30.0 ); UiGroup( "Top,20/50" ); >;
	float g_flTopRoughness < Default( 0.9 ); Range( 0.0, 1.0 ); UiGroup( "Top,20/60" ); >;

	// Two-tone colour variation across the surface, breaks up flat tints
	float3 g_vVariationColor < UiType( Color ); Default3( 0.8, 0.75, 0.3 ); UiGroup( "Variation,30/10" ); >;
	float g_flVariationAmount < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Variation,30/20" ); >;
	float g_flVariationScale < Default( 300.0 ); Range( 1.0, 4096.0 ); UiGroup( "Variation,30/30" ); >;
	float g_flVariationContrast < Default( 3.0 ); Range( 0.5, 12.0 ); UiGroup( "Variation,30/40" ); >;

	// Darken downward-facing surfaces (fake occlusion for canopies and bushes)
	float g_flBottomDarken < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Variation,30/50" ); >;

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

	float3 Weights( float3 n )
	{
		float3 w = pow( abs( n ), 4.0 );
		return w / ( w.x + w.y + w.z );
	}

	float3 TriColor( Texture2D t, float3 p, float3 w, float s )
	{
		return t.Sample( g_sTriSampler, p.yz / s ).rgb * w.x
			 + t.Sample( g_sTriSampler, p.xz / s ).rgb * w.y
			 + t.Sample( g_sTriSampler, p.xy / s ).rgb * w.z;
	}

	// Whiteout-blended triplanar normal, returned in world space.
	float3 TriNormal( Texture2D t, float3 p, float3 n, float3 w, float s, float strength )
	{
		float3 tx = t.Sample( g_sTriSampler, p.yz / s ).rgb * 2.0 - 1.0;
		float3 ty = t.Sample( g_sTriSampler, p.xz / s ).rgb * 2.0 - 1.0;
		float3 tz = t.Sample( g_sTriSampler, p.xy / s ).rgb * 2.0 - 1.0;
		tx.xy *= strength; ty.xy *= strength; tz.xy *= strength;
		tx = float3( tx.xy + n.yz, abs( tx.z ) * n.x );
		ty = float3( ty.xy + n.xz, abs( ty.z ) * n.y );
		tz = float3( tz.xy + n.xy, abs( tz.z ) * n.z );
		return normalize( tx.zxy * w.x + ty.xzy * w.y + tz.xyz * w.z );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );
		float3 wp = m.WorldPosition;
		float3 n = normalize( i.vNormalWs );
		float3 w = Weights( n );

		// m.Albedo holds the renderer tint (TextureColor is plain white).
		float3 tint = m.Albedo;
		float3 baseCol = TriColor( g_tBaseColor, wp, w, g_flBaseScale ) * tint;
		float v = saturate( ( FBm( wp / g_flVariationScale ) - 0.5 ) * g_flVariationContrast + 0.5 );
		baseCol = lerp( baseCol, g_vVariationColor * dot( baseCol, float3( 0.3, 0.59, 0.11 ) ) * 2.2, v * g_flVariationAmount );
		float3 baseN = TriNormal( g_tBaseNormal, wp, n, w, g_flBaseScale, g_flNormalStrength );
		float rough = dot( float3( g_tBaseRough.Sample( g_sTriSampler, wp.yz / g_flBaseScale ).r,
								   g_tBaseRough.Sample( g_sTriSampler, wp.xz / g_flBaseScale ).r,
								   g_tBaseRough.Sample( g_sTriSampler, wp.xy / g_flBaseScale ).r ), w );

		// Top layer on upward faces, with a noisy edge.
		float edge = FBm( wp / 40.0 ) - 0.5;
		float top = saturate( ( n.z - ( 1.0 - g_flTopAmount * 1.4 ) + edge * 0.5 ) * g_flTopSharpness ) * step( 0.001, g_flTopAmount );
		float3 topCol = TriColor( g_tTopColor, wp, w, g_flTopScale );
		float3 topN = TriNormal( g_tTopNormal, wp, n, w, g_flTopScale, 1.0 );

		m.Albedo = lerp( baseCol, topCol, top );
		m.Normal = normalize( lerp( baseN, topN, top ) );
		m.Roughness = lerp( rough, g_flTopRoughness, top );
		m.Metalness = 0.0;

		// Fake occlusion: undersides (canopy bellies, bush bottoms) get darker.
		float low = saturate( -n.z * 1.2 + 0.25 );
		m.Albedo *= 1.0 - g_flBottomDarken * low * 0.6;
		m.AmbientOcclusion *= 1.0 - g_flBottomDarken * low * 0.5;

		#if ( S_MODE_DEPTH )
			return 1;
		#endif

		return ShadingModelStandard::Shade( m );
	}
}
