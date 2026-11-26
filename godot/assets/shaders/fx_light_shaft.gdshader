// A shaft of light down from the surface (the clutter pass's atmosphere): additive, bright down its middle and soft at
// its edges, fading in under the surface and out before the sea bed, soft where it meets the ground, gone when the
// camera is inside it, and slowly breathing.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec4 color : source_color = vec4( 0.55, 0.85, 1.0, 1.0 );
uniform float strength = 0.09;
uniform float seed = 0.0;
uniform sampler2D depth_tex : hint_depth_texture, filter_linear;

void fragment() {
	float core = pow( abs( dot( NORMAL, VIEW ) ), 2.0 );
	float along = UV.y;   // 0 at the top, 1 at the bottom
	float ends = smoothstep( 0.0, 0.12, along ) * ( 1.0 - smoothstep( 0.55, 1.0, along ) );
	float breathe = 0.75 + 0.25 * sin( TIME * 0.35 + seed * 6.3 );
	// Soft where it meets geometry (no hard line on the sand).
	float depth = texture( depth_tex, SCREEN_UV ).r;
#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
	depth = depth * 2.0 - 1.0;   // OpenGL's depth is -1..1 in clip space
#endif
	vec4 world = INV_PROJECTION_MATRIX * vec4( SCREEN_UV * 2.0 - 1.0, depth, 1.0 );
	float scene = -world.z / max( abs( world.w ), 1e-5 ) * sign( world.w + 1e-9 );
	float soft = clamp( ( scene - ( -VERTEX.z ) ) / 3.0, 0.0, 1.0 );
	if ( isnan( soft ) || isinf( soft ) ) soft = 1.0;
	float near = smoothstep( 3.0, 12.0, length( VERTEX ) );
	ALBEDO = color.rgb;
	ALPHA = clamp( strength * core * ends * breathe * soft * near, 0.0, 1.0 );
}
