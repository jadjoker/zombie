HEADER
{
	Description = "Open sea: moving wave normals, sky reflection, sun glitter, crest foam; from below a bright window and a dark mirror";
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

	float g_flWaveTime < Attribute( "WaveTime" ); Default( 0.0 ); >;

	// The swell the game also samples on the CPU (WaterWaves.Height), so floating things ride the same waves.
	float SwellHeight( float2 p, float t )
	{
		float h = 0.0;
		h += sin( dot( p, float2( 0.80, 0.60 ) ) * 0.0060 + t * 0.55 ) * 1.00;
		h += sin( dot( p, float2( -0.50, 0.87 ) ) * 0.0110 + t * 0.80 ) * 0.65;
		h += sin( dot( p, float2( 0.30, -0.95 ) ) * 0.0240 + t * 1.20 ) * 0.38;
		return h * 11.0;
	}
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
		o.vPositionWs.z += SwellHeight( o.vPositionWs.xy, g_flWaveTime );
		o.vPositionPs.xyzw = Position3WsToPs( o.vPositionWs.xyz );
		return FinalizeVertex( o );
	}
}

PS
{
	StaticCombo( S_MODE_DEPTH, 0..1, Sys( ALL ) );

	#include "common/utils/Material.CommonInputs.hlsl"
	#include "common/pixel.hlsl"

	RenderState( CullMode, NONE );

	float Hash21( float2 p )
	{
		p = frac( p * float2( 123.34, 456.21 ) );
		p += dot( p, p + 45.32 );
		return frac( p.x * p.y );
	}

	float VNoise2( float2 p )
	{
		float2 i = floor( p );
		float2 f = frac( p );
		f = f * f * ( 3.0 - 2.0 * f );
		return lerp( lerp( Hash21( i ), Hash21( i + float2( 1, 0 ) ), f.x ),
					 lerp( Hash21( i + float2( 0, 1 ) ), Hash21( i + float2( 1, 1 ) ), f.x ), f.y );
	}

	// The height of the sea: long rolling swell, mid chop and fine ripple, all drifting.
	float Height( float2 p, float t )
	{
		float h = 0.0;
		h += sin( dot( p, float2( 0.80, 0.60 ) ) * 0.0060 + t * 0.55 ) * 1.00;
		h += sin( dot( p, float2( -0.50, 0.87 ) ) * 0.0110 + t * 0.80 ) * 0.65;
		h += sin( dot( p, float2( 0.30, -0.95 ) ) * 0.0240 + t * 1.20 ) * 0.38;
		h += ( VNoise2( p * 0.028 + float2( t * 0.30, -t * 0.22 ) ) - 0.5 ) * 0.9;
		h += ( VNoise2( p * 0.085 + float2( -t * 0.55, t * 0.40 ) ) - 0.5 ) * 0.42;
		h += ( VNoise2( p * 0.21 + float2( t * 0.9, t * 0.7 ) ) - 0.5 ) * 0.16;
		return h;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );
		float3 wp = m.WorldPosition;
		float t = g_flWaveTime;
		float3 toCam = g_vCameraPositionWs - wp;
		float dist = length( toCam );
		float3 view = toCam / max( dist, 0.001 );
		bool above = view.z > 0.0;

		// Wave normal from the height field, flattened with distance so the horizon doesn't shimmer to noise.
		float e = 5.0;
		float h0 = Height( wp.xy, t );
		float dx = Height( wp.xy + float2( e, 0 ), t ) - h0;
		float dy = Height( wp.xy + float2( 0, e ), t ) - h0;
		float3 n = normalize( float3( -dx * 0.55, -dy * 0.55, e * 0.35 ) );
		n = normalize( lerp( n, float3( 0, 0, 1 ), saturate( dist / 9000.0 ) ) );

		float3 sunDir = normalize( float3( 0.45, 0.25, 0.85 ) );
		float3 sunCol = float3( 1.0, 0.93, 0.78 );
		float3 col;

		if ( above )
		{
			float ndv = saturate( dot( n, view ) );
			float fres = 0.03 + 0.97 * pow( 1.0 - ndv, 5.0 );

			// What's under the surface: deep teal, lighter and greener in thin crests.
			float3 deep = float3( 0.012, 0.10, 0.17 );
			float3 shallow = float3( 0.05, 0.38, 0.42 );
			float crest = saturate( h0 * 0.35 + 0.45 );
			float3 body = lerp( deep, shallow, crest * 0.7 + ( 1.0 - ndv ) * 0.15 );

			// What's reflected: horizon haze up to a deeper sky.
			float3 refl = reflect( -view, n );
			float3 sky = lerp( float3( 0.72, 0.86, 0.95 ), float3( 0.20, 0.45, 0.82 ), saturate( refl.z * 1.4 ) );
			sky += sunCol * pow( saturate( dot( refl, sunDir ) ), 24.0 ) * 0.35;

			col = lerp( body, sky, fres );

			// Sun glitter: hard little highlights on the tilted facets.
			float spec = pow( saturate( dot( refl, sunDir ) ), 380.0 ) * 14.0;
			float spec2 = pow( saturate( dot( refl, sunDir ) ), 60.0 ) * 0.6;
			col += sunCol * ( spec + spec2 );

			// Foam on the tallest crests, broken up by noise.
			float foamNoise = VNoise2( wp.xy * 0.12 + t * 0.3 ) * 0.6 + VNoise2( wp.xy * 0.35 - t * 0.4 ) * 0.4;
			float foam = smoothstep( 1.25, 1.9, h0 + foamNoise * 0.9 - 0.2 );
			col = lerp( col, float3( 0.92, 0.97, 1.0 ), foam * 0.85 );

			// Distance haze into the horizon colour.
			col = lerp( col, float3( 0.62, 0.78, 0.88 ), saturate( ( dist - 3500.0 ) / 14000.0 ) * 0.7 );
		}
		else
		{
			// From below: a bright circular window straight up, and a dark mirror of the deep outside it.
			float cosi = saturate( dot( -n, view * -1.0 ) );
			cosi = saturate( dot( float3( -n.xy, -n.z ), -view ) );
			float window = smoothstep( 0.60, 0.72, cosi );
			float shimmer = Height( wp.xy * 1.6, t * 1.5 ) * 0.5 + 0.5;
			float3 skyThrough = lerp( float3( 0.18, 0.62, 0.78 ), float3( 0.55, 0.92, 1.0 ), saturate( shimmer * 0.9 ) );
			float sunGlow = pow( saturate( dot( refract( -view, -n, 0.75 ), sunDir ) ), 18.0 );
			skyThrough += sunCol * sunGlow * 1.6;
			float3 mirror = float3( 0.008, 0.055, 0.10 ) + float3( 0.0, 0.05, 0.05 ) * shimmer;
			col = lerp( mirror, skyThrough, window );
			// Bright rim where the window meets the mirror.
			col += float3( 0.25, 0.6, 0.7 ) * ( 1.0 - abs( cosi - 0.66 ) * 14.0 ) * step( 0.0, 1.0 - abs( cosi - 0.66 ) * 14.0 ) * 0.5;
		}

		m.Albedo = float3( 0, 0, 0 );
		m.Emission = col;
		m.Roughness = 0.1;

		#if ( S_MODE_DEPTH )
			return 1;
		#endif

		return ShadingModelStandard::Shade( m );
	}
}
