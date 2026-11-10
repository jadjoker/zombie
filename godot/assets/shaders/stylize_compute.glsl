#[compute]
#version 450
// Port of s&box pp_stylize.shader as a compositor compute pass (runs after the transparent pass, so water, bubbles
// and puffs are in it). Distances in s&box units (inches) like the original.

layout( local_size_x = 8, local_size_y = 8, local_size_z = 1 ) in;

layout( rgba16f, set = 0, binding = 0 ) uniform restrict writeonly image2D color_out;
layout( set = 0, binding = 1 ) uniform sampler2D color_in;
layout( set = 0, binding = 2 ) uniform sampler2D depth_tex;

layout( push_constant, std430 ) uniform Params
{
	vec2 size;
	float style;
	float strength;
	float outline;
	float outline_width;
	float bands;
	float pixel_size;
	vec3 ink;
	float vignette;
	float z_near;
	float z_far;
	float tonemap;
	float pad;
} p;

vec3 aces( vec3 x )
{
	x = max( x, vec3( 0.0 ) );
	return clamp( ( x * ( 2.51 * x + 0.03 ) ) / ( x * ( 2.43 * x + 0.59 ) + 0.14 ), 0.0, 1.0 );
}

vec3 fetch( vec2 uv )
{
	vec3 c = textureLod( color_in, clamp( uv, vec2( 0.0 ), vec2( 1.0 ) ), 0.0 ).rgb;
	return p.tonemap > 0.5 ? aces( c ) : c;
}
float luma( vec3 c ) { return dot( c, vec3( 0.299, 0.587, 0.114 ) ); }

float lin_depth( vec2 uv )
{
	float d = textureLod( depth_tex, clamp( uv, vec2( 0.0 ), vec2( 1.0 ) ), 0.0 ).r;   // reverse z: 1 near, 0 far
	float z = p.z_near * p.z_far / ( p.z_near + d * ( p.z_far - p.z_near ) );
	return z / 0.0254;
}

float lap_at( vec2 uv, vec2 t )
{
	float w = p.outline_width;
	float d = lin_depth( uv );
	return abs( lin_depth( uv + vec2( -w, 0.0 ) * t ) + lin_depth( uv + vec2( w, 0.0 ) * t ) + lin_depth( uv + vec2( 0.0, -w ) * t ) + lin_depth( uv + vec2( 0.0, w ) * t ) - 4.0 * d ) / max( d, 1.0 );
}

float depth_edge( vec2 uv, vec2 t )
{
	return smoothstep( 0.015, 0.05, lap_at( uv, t ) ) * ( 1.0 - smoothstep( 1500.0, 6000.0, lin_depth( uv ) ) );
}

float edge( vec2 uv, vec2 t )
{
	float w = p.outline_width;
	float depth_e = smoothstep( 0.015, 0.05, lap_at( uv, t ) );
	vec3 c = fetch( uv );
	float col_e = length( fetch( uv + vec2( w * t.x, 0.0 ) ) - c ) + length( fetch( uv + vec2( 0.0, w * t.y ) ) - c );
	col_e = smoothstep( 0.35, 0.8, col_e );
	float fade = 1.0 - smoothstep( 1500.0, 6000.0, lin_depth( uv ) );
	return clamp( max( depth_e, col_e * 0.5 ) * fade, 0.0, 1.0 );
}

vec3 toon( vec3 c )
{
	float l = luma( c );
	float b = max( p.bands, 2.0 );
	float f = sqrt( clamp( l, 0.0, 1.0 ) ) * b;
	float q = ( floor( f ) + smoothstep( 0.4, 0.6, fract( f ) ) ) / b;
	q = max( q, 0.35 / b );
	q = q * q;
	vec3 o = c / max( l, 1e-4 ) * q;
	return mix( vec3( luma( o ) ), o, 1.2 );
}

vec3 kuwahara( vec2 uv, vec2 t )
{
	vec3 best = vec3( 0.0 );
	float bv = 1e9;
	for ( int k = 0; k < 4; k++ )
	{
		vec2 dir = vec2( ( k & 1 ) != 0 ? 1.0 : -1.0, ( k & 2 ) != 0 ? 1.0 : -1.0 );
		vec3 s = vec3( 0.0 ); vec3 s2 = vec3( 0.0 );
		for ( int y = 0; y <= 4; y++ )
		for ( int x = 0; x <= 4; x++ )
		{
			vec3 c = fetch( uv + vec2( float( x ), float( y ) ) * dir * t * 1.5 );
			s += c; s2 += c * c;
		}
		vec3 mean = s / 25.0;
		vec3 v = s2 / 25.0 - mean * mean;
		float var = v.r + v.g + v.b;
		if ( var < bv ) { bv = var; best = mean; }
	}
	return best;
}

float bayer4( vec2 px )
{
	ivec2 q = ivec2( px ) & ivec2( 3 );
	float m[16] = float[]( 0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0 );
	return m[q.y * 4 + q.x] / 16.0 - 0.5;
}

float hash( vec2 v ) { return fract( sin( dot( v, vec2( 12.9898, 78.233 ) ) ) * 43758.5453 ); }

float dots( vec2 px, float angle, float amount, float scale )
{
	float s = sin( angle ); float co = cos( angle );
	vec2 q = vec2( co * px.x - s * px.y, s * px.x + co * px.y ) / max( scale, 1.0 );
	vec2 cell = fract( q ) - 0.5;
	float r = sqrt( clamp( amount, 0.0, 1.0 ) ) * 0.72;
	return 1.0 - smoothstep( r - 0.1, r + 0.1, length( cell ) );
}

float line_at( float coord, float spacing )
{
	float v = abs( fract( coord / max( spacing, 1.0 ) ) - 0.5 ) * 2.0;
	return smoothstep( 0.7, 0.86, v );
}

void main()
{
	ivec2 gid = ivec2( gl_GlobalInvocationID.xy );
	if ( gid.x >= int( p.size.x ) || gid.y >= int( p.size.y ) ) return;
	vec2 px = vec2( gid ) + 0.5;
	vec2 t = 1.0 / p.size;
	vec2 uv = px * t;
	int style = int( p.style + 0.5 );
	vec3 ink = p.ink;
	float outline = p.outline;

	vec3 src = fetch( uv );
	float vr = length( ( uv - 0.5 ) * vec2( 1.0, 0.84 ) ) * 1.6;
	src *= 1.0 - p.vignette * smoothstep( 0.15, 1.17, vr );
	vec3 c = src;

	if ( style == 1 ) c = mix( c, ink, edge( uv, t ) * outline );
	else if ( style == 2 ) { c = toon( c ); c = mix( c, ink, depth_edge( uv, t ) * outline ); }
	else if ( style == 3 ) { c = kuwahara( uv, t ); c = mix( c, c * 0.35, edge( uv, t ) * outline * 0.6 ); }
	else if ( style == 4 )
	{
		vec2 cell = vec2( max( p.pixel_size, 1.0 ) );
		vec2 spx = ( floor( px / cell ) + 0.5 ) * cell;
		vec3 q = fetch( spx * t );
		q = floor( q * 10.0 + bayer4( spx / cell ) * 0.6 + 0.5 ) / 10.0;
		c = clamp( q, 0.0, 1.0 );
	}
	else if ( style == 5 )
	{
		c = mix( vec3( luma( c ) ), c, 0.8 );
		c = c * vec3( 1.12, 1.08, 0.98 ) + vec3( 0.05, 0.045, 0.02 );
		c = mix( c, smoothstep( vec3( 0.0 ), vec3( 1.0 ), c ), 0.5 );
		c += ( hash( floor( px / 3.0 ) ) + hash( floor( px / 9.0 ) + 7.0 ) ) * 0.015 - 0.015;
		c = mix( c, ink * 1.5, edge( uv, t ) * outline * 0.6 );
	}
	else if ( style == 6 )
	{
		vec3 base = toon( c );
		float shade = sqrt( clamp( luma( base ), 0.0, 1.0 ) );
		float d = dots( px, 0.7854, 1.0 - shade, p.pixel_size * 2.0 ) * smoothstep( 0.7, 0.3, shade );
		c = mix( base, base * 0.4, d );
		c = mix( c, ink, edge( uv, t ) * outline );
	}
	else if ( style == 7 )
	{
		vec3 lifted = c * 1.35;
		float l = sqrt( clamp( luma( lifted ), 0.0, 1.0 ) );
		float tone = smoothstep( 0.24, 0.32, l ) * 0.5 + smoothstep( 0.5, 0.58, l ) * 0.5;
		vec3 o = mix( lifted * vec3( 0.78, 0.84, 1.15 ) * 0.9, lifted * vec3( 1.12, 1.05, 0.94 ), tone );
		o = mix( vec3( luma( o ) ), o, 1.25 );
		float k = max( depth_edge( uv, t ), edge( uv, t ) * 0.6 ) * outline;
		c = mix( o, o * 0.2 + ink * 0.5, k );
	}
	else if ( style == 8 )
	{
		float l = clamp( sqrt( clamp( luma( c ), 0.0, 1.0 ) ) * 1.5, 0.0, 1.0 );
		float spacing = max( p.pixel_size * 2.4, 4.0 );
		float a = line_at( px.x + px.y, spacing ) * step( l, 0.7 );
		float b = line_at( px.x - px.y, spacing ) * step( l, 0.45 );
		float h = line_at( px.y, spacing * 0.9 ) * step( l, 0.25 );
		float dark = clamp( max( a, max( b, h ) ), 0.0, 1.0 );
		vec3 paper = vec3( 0.97, 0.95, 0.88 ) + ( hash( floor( px / 2.0 ) ) - 0.5 ) * 0.04;
		vec3 pencil = vec3( 0.14, 0.14, 0.18 );
		vec3 o = mix( paper, pencil, dark * 0.85 );
		o = mix( o, o * ( src * 1.8 + 0.3 ), 0.5 );
		c = mix( o, pencil, edge( uv, t ) * outline * 0.9 );
	}
	else if ( style == 9 )
	{
		vec3 k = kuwahara( uv, t );
		vec3 wash = mix( vec3( luma( k ) ), k, 1.25 ) * 1.2 + 0.03;
		vec3 o = mix( wash, wash * 0.55, edge( uv, t ) * 0.8 * outline );
		float blotch = hash( floor( px / 6.0 ) + 3.0 ) * 0.5 + hash( floor( px / 17.0 ) + 9.0 ) * 0.5;
		o = mix( o, vec3( 0.98, 0.96, 0.9 ), 0.03 + blotch * 0.1 );
		o += ( hash( floor( px / 2.0 ) ) - 0.5 ) * 0.03;
		c = o;
	}
	else if ( style == 10 )
	{
		float l = clamp( ( sqrt( clamp( luma( c ), 0.0, 1.0 ) ) - 0.12 ) * 1.5, 0.0, 1.0 );
		vec3 shifted = fetch( uv + vec2( 2.5, 1.5 ) * t );
		float a1 = dots( px, 0.26, clamp( 1.0 - l, 0.0, 1.0 ) * 0.95, p.pixel_size * 1.8 );
		float warm = clamp( shifted.r - shifted.g * 0.8, 0.0, 1.0 ) * 1.6 + clamp( 1.0 - l, 0.0, 1.0 ) * 0.35;
		float a2 = dots( px + vec2( 2.5, 1.5 ), 1.3, clamp( warm, 0.0, 1.0 ), p.pixel_size * 1.8 );
		c = vec3( 0.97, 0.94, 0.86 ) * mix( vec3( 1.0 ), vec3( 0.02, 0.45, 0.6 ), a1 ) * mix( vec3( 1.0 ), vec3( 0.96, 0.27, 0.46 ), a2 * 0.85 );
		c = mix( c, c * 0.45, depth_edge( uv, t ) * outline * 0.7 );
		c += ( hash( floor( px ) ) - 0.5 ) * 0.025;
	}
	else if ( style == 11 )
	{
		float levels = max( p.bands, 2.0 ) + 1.0;
		// s&box runs this after TAA, which smooths texture and shadow grain first. Here a small cross blur and
		// slightly soft band edges stand in for it, so grain near a band edge does not speckle and crawl.
		vec3 soft = ( c * 4.0 + fetch( uv + vec2( t.x, 0.0 ) ) + fetch( uv - vec2( t.x, 0.0 ) ) + fetch( uv + vec2( 0.0, t.y ) ) + fetch( uv - vec2( 0.0, t.y ) ) ) / 8.0;
		vec3 g = pow( clamp( soft, 0.0, 1.0 ), vec3( 0.5 ) ) * levels;
		g = ( floor( g ) + smoothstep( vec3( 0.42 ), vec3( 0.58 ), fract( g ) ) ) / levels;
		vec3 o = g * g;
		o = mix( vec3( luma( o ) ), o, 1.25 );
		c = mix( o, ink, depth_edge( uv, t ) * outline );
	}

	else if ( style == 12 )
	{
		// Clean: the image as rendered (still HDR here; Godot tonemaps after), with thin soft ink lines on depth
		// edges that fade with distance and keep a little of the colour underneath.
		float e = depth_edge( uv, t ) * outline * 0.65;
		c = mix( src, src * 0.3 + ink * 0.4, e );
		vec3 result_clean = mix( src, c, p.strength );
		imageStore( color_out, gid, vec4( result_clean, 1.0 ) );
		return;
	}

	vec3 result = style == 0 ? src : mix( src, clamp( c, 0.0, 1.0 ), p.strength );
	imageStore( color_out, gid, vec4( result, 1.0 ) );
}
