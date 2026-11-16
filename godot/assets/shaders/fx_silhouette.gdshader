// A far-off shape in the murk (the leviathans past the rim): a flat, dark, see-through silhouette in the water's
// colour, outside the fog so it reads at any distance without ever looking near.
shader_type spatial;
render_mode unshaded, fog_disabled, cull_back, shadows_disabled, depth_draw_opaque;

uniform vec4 tint : source_color = vec4( 0.02, 0.09, 0.13, 1.0 );
uniform float opacity = 0.4;

void fragment() {
	float edge = 1.0 - abs( dot( NORMAL, VIEW ) );
	ALBEDO = tint.rgb * ( 1.0 + edge * 0.6 );
	ALPHA = opacity * ( 0.7 + 0.3 * ( 1.0 - edge ) );
}
