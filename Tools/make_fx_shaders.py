# Generates the test FX shaders (Assets/shaders/fx_*.shader) and their materials (Assets/materials/fx/*.vmat).
import os
ROOT = r"C:/Users/smitt/Documents/zombie_survival/Assets"
os.makedirs(ROOT + "/shaders", exist_ok=True)
os.makedirs(ROOT + "/materials/fx", exist_ok=True)

HEAD = '''HEADER
{
	Description = "%(desc)s";
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

%(params)s
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );
		float3 wp = m.WorldPosition;
		float3 viewDir = normalize( g_vCameraPositionWs - wp );
		float rim = 1.0 - saturate( dot( m.Normal, viewDir ) );
		float t = g_flTime;

%(body)s
		#if ( S_MODE_DEPTH )
			return 1;
		#endif

		return ShadingModelStandard::Shade( m );
	}
}
'''

SHADERS = {}

SHADERS["fx_ghost"] = dict(desc="Ghostly rim glow with drifting mist", params='''	float3 g_vRimColor < UiType( Color ); Default3( 0.35, 0.75, 1.0 ); UiGroup( "Ghost,10/10" ); >;
	float g_flRimPower < Default( 2.2 ); Range( 0.5, 8.0 ); UiGroup( "Ghost,10/20" ); >;
	float g_flRimStrength < Default( 1.6 ); Range( 0.0, 5.0 ); UiGroup( "Ghost,10/30" ); >;
	float g_flMist < Default( 0.35 ); Range( 0.0, 1.0 ); UiGroup( "Ghost,10/40" ); >;
''', body='''		float mist = FBm( wp * 0.07 + float3( 0, 0, t * 0.6 ) );
		float glow = pow( rim, g_flRimPower ) * g_flRimStrength;
		m.Emission += g_vRimColor * ( glow + mist * g_flMist * ( 0.4 + rim ) );
		m.Albedo = lerp( m.Albedo, float3( 0.1, 0.17, 0.3 ), 0.65 );   // dark body so the rim glow reads
''')

SHADERS["fx_dissolve"] = dict(desc="Burn-away dissolve with a glowing edge (loops)", params='''	float3 g_vEdgeColor < UiType( Color ); Default3( 1.0, 0.45, 0.08 ); UiGroup( "Dissolve,10/10" ); >;
	float g_flEdgeWidth < Default( 0.07 ); Range( 0.01, 0.3 ); UiGroup( "Dissolve,10/20" ); >;
	float g_flCycle < Default( 0.22 ); Range( 0.02, 2.0 ); UiGroup( "Dissolve,10/30" ); >;
''', body='''		float n = saturate( ( FBm( wp * 0.045 ) - 0.28 ) * 1.9 );   // stretch the noise so it burns away gradually
		float th = frac( t * g_flCycle ) * 1.2 - 0.1;
		float d = n - th;
		clip( d );
		float edge = 1.0 - smoothstep( 0.0, g_flEdgeWidth, d );
		m.Emission += g_vEdgeColor * edge * 4.0;
		m.Albedo = lerp( m.Albedo, float3( 0.02, 0.01, 0.01 ), edge );
''')

SHADERS["fx_hologram"] = dict(desc="Scanline hologram", params='''	float3 g_vHoloColor < UiType( Color ); Default3( 0.1, 0.9, 1.0 ); UiGroup( "Hologram,10/10" ); >;
	float g_flLineDensity < Default( 0.5 ); Range( 0.05, 3.0 ); UiGroup( "Hologram,10/20" ); >;
	float g_flScrollSpeed < Default( 0.9 ); Range( 0.0, 4.0 ); UiGroup( "Hologram,10/30" ); >;
	float g_flRevealZ < Attribute( "RevealZ" ); Default( 1000000.0 ); >;   // world height below which he has scanned in
''', body='''		clip( g_flRevealZ - wp.z );
		float revealEdge = saturate( 1.0 - ( g_flRevealZ - wp.z ) / 10.0 );
		float lines = frac( wp.z * g_flLineDensity - t * g_flScrollSpeed );
		clip( lines - 0.28 );
		float flicker = 0.85 + 0.15 * sin( t * 43.0 ) * sin( t * 7.0 );
		float sweep = pow( saturate( 1.0 - abs( frac( wp.z * 0.012 - t * 0.35 ) - 0.5 ) * 8.0 ), 2.0 );
		m.Albedo = g_vHoloColor * 0.15;
		m.Emission = g_vHoloColor * ( 0.35 + pow( rim, 1.6 ) * 2.4 + sweep * 2.0 ) * flicker + float3( 0.6, 1.0, 1.0 ) * revealEdge * 3.0;
''')

SHADERS["fx_iridescent"] = dict(desc="Shifting rainbow sheen, like oil on water", params='''	float g_flSheen < Default( 1.0 ); Range( 0.0, 3.0 ); UiGroup( "Iridescent,10/10" ); >;
	float g_flHueSpeed < Default( 0.15 ); Range( 0.0, 2.0 ); UiGroup( "Iridescent,10/20" ); >;
''', body='''		float swirl = FBm( wp * 0.05 + float3( t * 0.1, 0, 0 ) );
		float hue = rim * 1.1 + swirl * 0.7 + t * g_flHueSpeed;
		float3 rainbow = Hsv( hue, 0.85, 1.0 );
		m.Metalness = 0.7;
		m.Roughness = 0.2;
		m.Emission += rainbow * g_flSheen * ( 0.25 + rim * 1.4 );
		m.Albedo = lerp( m.Albedo, rainbow, 0.35 );
''')

for name, d in SHADERS.items():
    open(f"{ROOT}/shaders/{name}.shader", "w", newline="\n").write(HEAD % d)
    extra = '\t"g_flRimStrength"\t\t"3.500000"\n\t"g_flRimPower"\t\t"1.600000"\n\t"g_flMist"\t\t"0.700000"\n' if name == "fx_ghost" else ""
    vmat = '''"Layer0"
{
	"shader"		"shaders/%s.shader"
	"g_flModelTintAmount"		"1.000000"
	"g_vColorTint"		"[1.000000 1.000000 1.000000 1.000000]"
	"TextureColor"		"materials/creature/smooth_color.png"
	"TextureNormal"		"materials/creature/smooth_normal.png"
	"TextureRoughness"		"materials/creature/smooth_rough.png"
	"g_flMetalness"		"0.000000"
	"g_bFogEnabled"		"1"
%s}
''' % (name, extra)
    open(f"{ROOT}/materials/fx/{name}.vmat", "w", newline="\n").write(vmat)
print("wrote", list(SHADERS))
