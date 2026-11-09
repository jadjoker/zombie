// Port of s&box fx_hologram: scan lines scrolling up, a flicker, a bright sweep, and (for MC61's spawn-in) only
// what's below reveal_z (s&box units) is drawn, with a hot line at the edge.
shader_type spatial;
render_mode cull_disabled, unshaded;
#include "res://assets/shaders/fx_common.gdshaderinc"

uniform vec3 holo_color : source_color = vec3( 0.1, 0.9, 1.0 );
uniform float line_density = 0.5;
uniform float scroll_speed = 0.9;
uniform float reveal_z = 1000000.0;
varying vec3 world_pos;

void vertex() { world_pos = ( MODEL_MATRIX * vec4( VERTEX, 1.0 ) ).xyz; }

void fragment()
{
	vec3 wp = sbox_pos( world_pos );
	float rim = 1.0 - clamp( dot( NORMAL, VIEW ), 0.0, 1.0 );
	if ( reveal_z - wp.z < 0.0 ) discard;
	float reveal_edge = clamp( 1.0 - ( reveal_z - wp.z ) / 10.0, 0.0, 1.0 );
	float lines = fract( wp.z * line_density - TIME * scroll_speed );
	if ( lines < 0.28 ) discard;
	float flicker = 0.85 + 0.15 * sin( TIME * 43.0 ) * sin( TIME * 7.0 );
	float sweep = pow( clamp( 1.0 - abs( fract( wp.z * 0.012 - TIME * 0.35 ) - 0.5 ) * 8.0, 0.0, 1.0 ), 2.0 );
	ALBEDO = holo_color * 0.15 + holo_color * ( 0.35 + pow( rim, 1.6 ) * 2.4 + sweep * 2.0 ) * flicker + vec3( 0.6, 1.0, 1.0 ) * reveal_edge * 3.0;
}
