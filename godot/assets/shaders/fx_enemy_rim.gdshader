// Enemy readability (the clutter pass), drawn over every enemy mesh as a material overlay:
// - in plain sight, a soft warm rim round its silhouette, so it stands off the scenery;
// - behind something (a plant, a rock), a faint warm silhouette through it, so it is never lost.
shader_type spatial;
render_mode unshaded, blend_add, depth_test_disabled, depth_draw_never, cull_back, shadows_disabled, fog_disabled;

uniform vec4 rim_color : source_color = vec4( 1.0, 0.82, 0.55, 1.0 );
uniform float rim_power = 3.0;
uniform float rim_near = 0.18;   // up close it barely shows (the enemy is plain to see)
uniform float rim_far = 0.45;    // far off, where it is easy to lose one, it shows most
uniform float fill_far = 0.04;   // a touch of warm light over the whole body far off, so dark enemies lift off dark rock
uniform vec4 xray_color : source_color = vec4( 1.0, 0.5, 0.22, 1.0 );
uniform float xray_strength = 0.22;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

void fragment() {
	float scene = texture( depth_tex, SCREEN_UV ).r;
	float frag = FRAGCOORD.z;
#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
	bool hidden = frag > scene + 0.00002;   // depth grows away from the camera
#else
	bool hidden = frag < scene - 0.000002;  // reversed depth: nearer is larger
#endif
	float fres = pow( 1.0 - clamp( dot( NORMAL, VIEW ), 0.0, 1.0 ), rim_power );
	float far = smoothstep( 5.0, 22.0, length( VERTEX ) );
	if ( hidden ) {
		ALBEDO = xray_color.rgb;
		ALPHA = xray_strength * ( 0.25 + 0.75 * fres );
	} else {
		ALBEDO = rim_color.rgb;
		ALPHA = fill_far * far + mix( rim_near, rim_far, far ) * fres;
	}
}
