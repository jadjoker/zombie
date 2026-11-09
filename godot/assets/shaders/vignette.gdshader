// CSS-style radial-gradient( rgba( c, 0 ) inner%, rgba( c, alpha ) 100% ) over the screen, for the s&box HUD
// overlays (hurt, venom, burn, slow). With invert on it is bright in the middle instead (the slam flash).
shader_type canvas_item;

uniform vec4 tint : source_color = vec4( 1.0, 0.0, 0.0, 0.9 );
uniform vec4 centre_tint : source_color = vec4( 1.0, 1.0, 1.0, 0.0 );
uniform float inner = 0.4;
uniform float outer = 1.0;
uniform float strength = 1.0;

void fragment()
{
	// Distance from the middle as a fraction of the half-diagonal ("farthest-corner").
	vec2 p = UV * 2.0 - 1.0;
	float r = length( p ) / sqrt( 2.0 );
	float k = smoothstep( inner, outer, r );
	vec4 c = mix( centre_tint, tint, k );
	COLOR = vec4( c.rgb, c.a * strength );
}
