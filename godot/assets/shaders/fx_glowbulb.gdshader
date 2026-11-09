// Port of s&box fx_glowbulb: a lit bulb, a coloured glow with a hot near-white core facing you.
shader_type spatial;

uniform vec3 glow_color : source_color = vec3( 0.5, 1.0, 0.9 );
uniform float glow_strength = 4.0;

void fragment()
{
	float facing = clamp( dot( NORMAL, VIEW ), 0.0, 1.0 );
	vec3 core = mix( glow_color, vec3( 1.0 ), pow( facing, 2.5 ) * 0.12 );
	ALBEDO = glow_color * 0.3;
	EMISSION = core * glow_strength;
}
