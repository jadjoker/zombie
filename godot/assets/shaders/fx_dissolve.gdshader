// Port of s&box fx_dissolve: the surface burns away in noisy holes with glowing edges, over and over.
shader_type spatial;
render_mode cull_disabled;
#include "res://assets/shaders/fx_common.gdshaderinc"

uniform vec4 tint : source_color = vec4( 1.0 );
uniform vec3 edge_color : source_color = vec3( 1.0, 0.45, 0.08 );
uniform float edge_width = 0.07;
uniform float cycle = 0.22;
varying vec3 world_pos;

void vertex() { world_pos = ( MODEL_MATRIX * vec4( VERTEX, 1.0 ) ).xyz; }

void fragment()
{
	vec3 wp = sbox_pos( world_pos );
	float n = clamp( ( fbm( wp * 0.045 ) - 0.28 ) * 1.9, 0.0, 1.0 );
	float th = fract( TIME * cycle ) * 1.2 - 0.1;
	float d = n - th;
	if ( d < 0.0 ) discard;
	float e = 1.0 - smoothstep( 0.0, edge_width, d );
	ALBEDO = mix( tint.rgb, vec3( 0.02, 0.01, 0.01 ), e );
	EMISSION = edge_color * e * 4.0;
}
