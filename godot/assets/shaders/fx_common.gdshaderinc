// Shared bits of the s&box fx shaders: tiny procedural noise (no textures), HSV, and world positions in s&box
// units (inches, x forward, y left, z up) so the original frequencies carry over.

float hash31( vec3 p )
{
	p = fract( p * 0.1031 );
	p += dot( p, p.zyx + 31.32 );
	return fract( ( p.x + p.y ) * p.z );
}

float vnoise( vec3 p )
{
	vec3 i = floor( p );
	vec3 f = fract( p );
	f = f * f * ( 3.0 - 2.0 * f );
	float n000 = hash31( i );
	float n100 = hash31( i + vec3( 1, 0, 0 ) );
	float n010 = hash31( i + vec3( 0, 1, 0 ) );
	float n110 = hash31( i + vec3( 1, 1, 0 ) );
	float n001 = hash31( i + vec3( 0, 0, 1 ) );
	float n101 = hash31( i + vec3( 1, 0, 1 ) );
	float n011 = hash31( i + vec3( 0, 1, 1 ) );
	float n111 = hash31( i + vec3( 1, 1, 1 ) );
	return mix( mix( mix( n000, n100, f.x ), mix( n010, n110, f.x ), f.y ),
				mix( mix( n001, n101, f.x ), mix( n011, n111, f.x ), f.y ), f.z );
}

float fbm( vec3 p ) { return vnoise( p ) * 0.5 + vnoise( p * 2.07 ) * 0.3 + vnoise( p * 4.13 ) * 0.2; }

vec3 hsv( float h, float s, float v )
{
	vec3 k = fract( h + vec3( 0.0, 2.0 / 3.0, 1.0 / 3.0 ) );
	vec3 p = abs( k * 6.0 - 3.0 );
	return v * mix( vec3( 1.0 ), clamp( p - 1.0, 0.0, 1.0 ), s );
}

vec3 sbox_pos( vec3 world ) { return vec3( -world.z, -world.x, world.y ) / 0.0254; }
