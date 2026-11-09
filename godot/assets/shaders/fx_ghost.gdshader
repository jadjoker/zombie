// Port of s&box fx_ghost: a dark body with a blue rim glow and slow mist drifting through it.
shader_type spatial;
#include "res://assets/shaders/fx_common.gdshaderinc"

uniform vec4 tint : source_color = vec4( 1.0 );
uniform vec3 rim_color : source_color = vec3( 0.35, 0.75, 1.0 );
uniform float rim_power = 2.2;
uniform float rim_strength = 1.6;
uniform float mist = 0.35;
varying vec3 world_pos;

void vertex() { world_pos = ( MODEL_MATRIX * vec4( VERTEX, 1.0 ) ).xyz; }

void fragment()
{
	vec3 wp = sbox_pos( world_pos );
	float rim = 1.0 - clamp( dot( NORMAL, VIEW ), 0.0, 1.0 );
	float m = fbm( wp * 0.07 + vec3( 0.0, 0.0, TIME * 0.6 ) );
	float glow = pow( rim, rim_power ) * rim_strength;
	ALBEDO = mix( tint.rgb, vec3( 0.1, 0.17, 0.3 ), 0.65 );
	EMISSION = rim_color * ( glow + m * mist * ( 0.4 + rim ) );
	ROUGHNESS = 0.6;
}
