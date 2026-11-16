// Stained-glass mosaic, after a jewelled seahorse: rounded cells of teal, aqua and a few deep blues, each lit
// brighter toward its middle like a cut gem, set in thin raised gold-yellow leading. Cells come from the mesh's own
// positions (Voronoi in object space), so they ride with the body. Tint multiplies the cells (hurt and hit flash).
shader_type spatial;
render_mode cull_disabled;

uniform vec4 tint : source_color = vec4( 1.0 );
uniform vec3 deep : source_color = vec3( 0.06, 0.24, 0.48 );
uniform vec3 teal : source_color = vec3( 0.07, 0.55, 0.56 );
uniform vec3 aqua : source_color = vec3( 0.42, 0.9, 0.84 );
uniform vec3 line_color : source_color = vec3( 1.0, 0.8, 0.2 );
/// Size of a cell, in the mesh's own units.
uniform float cell = 0.045;
/// How wide the leading is, as a share of a cell.
uniform float line_width = 0.11;

varying vec3 local_pos;

void vertex() { local_pos = VERTEX; }

vec3 hash3( vec3 p ) {
	p = vec3( dot( p, vec3( 127.1, 311.7, 74.7 ) ), dot( p, vec3( 269.5, 183.3, 246.1 ) ), dot( p, vec3( 113.5, 271.9, 124.6 ) ) );
	return fract( sin( p ) * 43758.5453 );
}

void fragment() {
	vec3 p = local_pos / cell;
	vec3 i = floor( p );
	vec3 f = fract( p );
	float d1 = 8.0;
	float d2 = 8.0;
	vec3 id = vec3( 0.0 );
	for ( int x = -1; x <= 1; x++ )
	for ( int y = -1; y <= 1; y++ )
	for ( int z = -1; z <= 1; z++ ) {
		vec3 g = vec3( float( x ), float( y ), float( z ) );
		vec3 r = g + hash3( i + g ) * 0.75 + 0.125 - f;
		float d = dot( r, r );
		if ( d < d1 ) { d2 = d1; d1 = d; id = i + g; }
		else if ( d < d2 ) { d2 = d; }
	}
	float edge = sqrt( d2 ) - sqrt( d1 );
	vec3 h = hash3( id * 1.7 + 3.1 );
	vec3 c = mix( teal, aqua, h.x * h.x );
	c = mix( c, deep, step( 0.8, h.y ) * 0.85 );
	// A gem: brighter in its middle, darker where it meets the leading.
	c *= 0.72 + 0.5 * ( 1.0 - smoothstep( 0.0, 0.85, sqrt( d1 ) ) );
	float line = 1.0 - smoothstep( line_width * 0.55, line_width, edge );
	ALBEDO = mix( c * tint.rgb, line_color * min( tint.rgb, vec3( 1.6 ) ), line );
	METALLIC = line * 0.7;
	ROUGHNESS = mix( 0.15, 0.32, line );
	SPECULAR = 0.65;
	EMISSION = line_color * line * 0.3 + c * 0.1 + max( tint.rgb - vec3( 1.0 ), vec3( 0.0 ) ) * 0.4;
}
