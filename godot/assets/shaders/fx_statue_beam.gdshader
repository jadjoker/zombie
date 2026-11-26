// The Abyss statue's shaft of light: see-through (mixed, not additive: additive drew solid white under the OpenGL renderer), bright down its middle and soft at its edges, fading in under the roof
// and out at the floor, gone when the camera is inside it, slowly breathing.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform vec4 color : source_color = vec4( 0.7, 0.88, 1.0, 1.0 );
uniform float strength = 0.2;

void fragment() {
	float core = pow( abs( dot( NORMAL, VIEW ) ), 2.0 );
	float along = UV.y;   // 0 at the top, 1 at the floor
	float ends = smoothstep( 0.0, 0.15, along ) * ( 1.0 - smoothstep( 0.8, 1.0, along ) * 0.6 );
	float breathe = 0.8 + 0.2 * sin( TIME * 0.35 );
	float near = smoothstep( 2.0, 9.0, length( VERTEX ) );
	ALBEDO = color.rgb;
	ALPHA = clamp( strength * core * ends * breathe * near, 0.0, 1.0 );
}
