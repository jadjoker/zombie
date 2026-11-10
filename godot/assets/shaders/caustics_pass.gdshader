// Underwater caustics as an extra pass on the sea floor and rocks: the rippling light the surface throws down,
// added on top of the lit material in world space (so it tiles across everything), on upward faces, fading
// out with distance. Pattern after Dave Hoskins' tileable water caustic.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back, shadows_disabled, fog_disabled;

uniform float tile = 7.0;          // metres per repeat
uniform float speed = 0.45;
uniform float strength = 0.42;
uniform vec3 tint : source_color = vec3( 0.6, 0.95, 1.0 );
uniform float fade_start = 18.0;
uniform float fade_end = 55.0;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex()
{
	world_pos = ( MODEL_MATRIX * vec4( VERTEX, 1.0 ) ).xyz;
	world_normal = normalize( ( MODEL_MATRIX * vec4( NORMAL, 0.0 ) ).xyz );
}

float caustic( vec2 uv, float time )
{
	vec2 p = mod( uv * 6.28318, 6.28318 ) - 250.0;
	vec2 i = p;
	float c = 1.0;
	float inten = 0.005;
	for ( int n = 0; n < 5; n++ )
	{
		float t = time * ( 1.0 - ( 3.5 / float( n + 1 ) ) );
		i = p + vec2( cos( t - i.x ) + sin( t + i.y ), sin( t - i.y ) + cos( t + i.x ) );
		c += 1.0 / length( vec2( p.x / ( sin( i.x + t ) / inten ), p.y / ( cos( i.y + t ) / inten ) ) );
	}
	c /= 5.0;
	c = 1.17 - pow( c, 1.4 );
	return clamp( pow( abs( c ), 8.0 ), 0.0, 1.0 );
}

void fragment()
{
	float t = TIME * speed + 23.0;
	vec2 uv = world_pos.xz / tile;
	float c = caustic( uv, t );
	// A second slightly slower layer at another scale breaks up the repetition.
	c = c * 0.75 + caustic( uv * 0.63 + 0.37, t * 0.8 ) * 0.4;
	float up = smoothstep( 0.15, 0.85, world_normal.y );
	float dist = length( world_pos - CAMERA_POSITION_WORLD );
	float fade = 1.0 - smoothstep( fade_start, fade_end, dist );
	ALBEDO = tint * c * strength * up * fade;
}
