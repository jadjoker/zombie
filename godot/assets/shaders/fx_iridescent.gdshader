// Port of s&box fx_iridescent: an oily rainbow sheen that swirls and shifts with the view.
shader_type spatial;
#include "res://assets/shaders/fx_common.gdshaderinc"

uniform vec4 tint : source_color = vec4( 1.0 );
uniform float sheen = 1.0;
uniform float hue_speed = 0.15;
varying vec3 world_pos;

void vertex() { world_pos = ( MODEL_MATRIX * vec4( VERTEX, 1.0 ) ).xyz; }

void fragment()
{
	vec3 wp = sbox_pos( world_pos );
	float rim = 1.0 - clamp( dot( NORMAL, VIEW ), 0.0, 1.0 );
	float swirl = fbm( wp * 0.05 + vec3( TIME * 0.1, 0.0, 0.0 ) );
	vec3 rainbow = hsv( rim * 1.1 + swirl * 0.7 + TIME * hue_speed, 0.85, 1.0 );
	METALLIC = 0.7;
	ROUGHNESS = 0.2;
	ALBEDO = mix( tint.rgb, rainbow, 0.35 );
	EMISSION = rainbow * sheen * ( 0.25 + rim * 1.4 );
}
