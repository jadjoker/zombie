// Camera-facing bubble: clear in the middle, a bright rim and a small highlight (like the s&box ghost-shaded bubbles).
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

void vertex()
{
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4( INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3] );
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4( vec4( length( MODEL_MATRIX[0].xyz ), 0.0, 0.0, 0.0 ), vec4( 0.0, length( MODEL_MATRIX[1].xyz ), 0.0, 0.0 ), vec4( 0.0, 0.0, length( MODEL_MATRIX[2].xyz ), 0.0 ), vec4( 0.0, 0.0, 0.0, 1.0 ) );
}

uniform sampler2D sprite : source_color, filter_linear_mipmap;
uniform bool use_sprite = false;

void fragment()
{
	if ( use_sprite )
	{
		// Rendered in Blender (Tools/blender/sprites.py): a clear bubble with a bright rim and a window highlight.
		vec4 t = texture( sprite, UV );
		ALBEDO = t.rgb;
		ALPHA = t.a;
	}
	else
	{
		vec2 c = UV * 2.0 - 1.0;
		float r = length( c );
		if ( r > 1.0 ) discard;
		float rim = smoothstep( 0.55, 0.95, r ) * ( 1.0 - smoothstep( 0.95, 1.0, r ) );
		float spot = 1.0 - smoothstep( 0.0, 0.22, length( c - vec2( -0.35, 0.38 ) ) );
		ALBEDO = mix( vec3( 0.55, 0.85, 1.0 ), vec3( 1.0 ), spot );
		ALPHA = clamp( 0.08 + rim * 0.75 + spot * 0.9, 0.0, 1.0 );
	}
}
