// Camera-facing soft puff tinted by the particle colour (sand and silt clouds).
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

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
		// A soft lumpy cloud rendered in Blender (grey), tinted by the particle colour.
		vec4 t = texture( sprite, UV );
		ALBEDO = COLOR.rgb * ( 0.55 + 0.6 * t.r );
		ALPHA = COLOR.a * t.a * 0.9;
	}
	else
	{
		vec2 c = UV * 2.0 - 1.0;
		float r = length( c );
		if ( r > 1.0 ) discard;
		ALBEDO = COLOR.rgb;
		ALPHA = COLOR.a * ( 1.0 - smoothstep( 0.35, 1.0, r ) ) * 0.9;
		NORMAL = normalize( vec3( c * 0.6, 1.0 ) );
	}
}
