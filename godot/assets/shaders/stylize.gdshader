// Port of s&box pp_stylize.shader: whole-screen stylized looks (GlobalStylize). Drawn on a quad that covers the
// view, reading the screen and depth. Distances are s&box units (inches) like the original.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_test_disabled, depth_draw_never, fog_disabled, shadows_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;

uniform int style = 11;
uniform float strength = 1.0;
uniform float outline = 0.9;
uniform float outline_width = 1.6;
uniform vec3 ink = vec3( 0.05, 0.05, 0.08 );
uniform float bands = 4.0;
uniform float pixel_size = 3.0;
// s&box Vignette on the main camera (applied before the look, like the s&box stack).
uniform float vignette = 0.28;

varying mat4 inv_proj;

void vertex()
{
	POSITION = vec4( VERTEX.xy * 2.0, 0.0, 1.0 );
	inv_proj = INV_PROJECTION_MATRIX;
}

vec2 rt_size( vec2 px_size ) { return 1.0 / px_size; }

vec3 fetch( vec2 uv ) { return textureLod( screen_tex, uv, 0.0 ).rgb; }
float luma( vec3 c ) { return dot( c, vec3( 0.299, 0.587, 0.114 ) ); }

float lin_depth_uv( vec2 uv )
{
	float d = textureLod( depth_tex, clamp( uv, vec2( 0.0 ), vec2( 1.0 ) ), 0.0 ).r;
	vec4 view = inv_proj * vec4( uv * 2.0 - 1.0, d, 1.0 );
	return -view.z / view.w / 0.0254;   // inches
}

float depth_edge( vec2 uv, vec2 t )
{
	float w = outline_width;
	float d = lin_depth_uv( uv );
	float lap = abs( lin_depth_uv( uv + vec2( -w, 0.0 ) * t ) + lin_depth_uv( uv + vec2( w, 0.0 ) * t ) + lin_depth_uv( uv + vec2( 0.0, -w ) * t ) + lin_depth_uv( uv + vec2( 0.0, w ) * t ) - 4.0 * d ) / max( d, 1.0 );
	return smoothstep( 0.015, 0.05, lap ) * ( 1.0 - smoothstep( 1500.0, 6000.0, d ) );
}

float edge( vec2 uv, vec2 t )
{
	float w = outline_width;
	float d = lin_depth_uv( uv );
	float lap = abs( lin_depth_uv( uv + vec2( -w, 0.0 ) * t ) + lin_depth_uv( uv + vec2( w, 0.0 ) * t ) + lin_depth_uv( uv + vec2( 0.0, -w ) * t ) + lin_depth_uv( uv + vec2( 0.0, w ) * t ) - 4.0 * d ) / max( d, 1.0 );
	float depth_e = smoothstep( 0.015, 0.05, lap );
	vec3 c = fetch( uv );
	float col_e = length( fetch( uv + vec2( w * t.x, 0.0 ) ) - c ) + length( fetch( uv + vec2( 0.0, w * t.y ) ) - c );
	col_e = smoothstep( 0.35, 0.8, col_e );
	float fade = 1.0 - smoothstep( 1500.0, 6000.0, d );
	return clamp( max( depth_e, col_e * 0.5 ) * fade, 0.0, 1.0 );
}

vec3 toon( vec3 c )
{
	float l = luma( c );
	float b = max( bands, 2.0 );
	float pl = sqrt( clamp( l, 0.0, 1.0 ) );
	float f = pl * b;
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
	ivec2 p = ivec2( px ) & ivec2( 3 );
	float m[16] = float[]( 0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0 );
	return m[p.y * 4 + p.x] / 16.0 - 0.5;
}

float hash( vec2 p ) { return fract( sin( dot( p, vec2( 12.9898, 78.233 ) ) ) * 43758.5453 ); }

float dots( vec2 px, float angle, float amount, float scale )
{
	float s = sin( angle ); float co = cos( angle );
	vec2 p = vec2( co * px.x - s * px.y, s * px.x + co * px.y ) / max( scale, 1.0 );
	vec2 cell = fract( p ) - 0.5;
	float r = sqrt( clamp( amount, 0.0, 1.0 ) ) * 0.72;
	return 1.0 - smoothstep( r - 0.1, r + 0.1, length( cell ) );
}

float line_at( float coord, float spacing )
{
	float v = abs( fract( coord / max( spacing, 1.0 ) ) - 0.5 ) * 2.0;
	return smoothstep( 0.7, 0.86, v );
}

void fragment()
{
	vec2 uv = SCREEN_UV;
	vec2 t = 1.0 / VIEWPORT_SIZE;
	vec2 px = FRAGCOORD.xy;
	vec3 src = fetch( uv );
	float vr = length( ( uv - 0.5 ) * vec2( 1.0, 0.6 + 0.4 * 0.6 ) ) * 1.6;
	src *= 1.0 - vignette * smoothstep( 0.15, 0.15 + 0.85 * 1.2, vr );
	vec3 c = src;

	if ( style == 1 ) // Ink outlines
	{
		c = mix( c, ink, edge( uv, t ) * outline );
	}
	else if ( style == 2 ) // Toon
	{
		c = toon( c );
		c = mix( c, ink, depth_edge( uv, t ) * outline );
	}
	else if ( style == 3 ) // Painterly
	{
		c = kuwahara( uv, t );
		c = mix( c, c * 0.35, edge( uv, t ) * outline * 0.6 );
	}
	else if ( style == 4 ) // Retro
	{
		vec2 cell = vec2( max( pixel_size, 1.0 ) );
		vec2 spx = ( floor( px / cell ) + 0.5 ) * cell;
		vec3 p = fetch( spx * t );
		float levels = 10.0;
		p = floor( p * levels + bayer4( spx / cell ) * 0.6 + 0.5 ) / levels;
		c = clamp( p, 0.0, 1.0 );
	}
	else if ( style == 5 ) // Storybook
	{
		float l = luma( c );
		c = mix( vec3( l ), c, 0.8 );
		c = c * vec3( 1.12, 1.08, 0.98 ) + vec3( 0.05, 0.045, 0.02 );
		c = mix( c, smoothstep( vec3( 0.0 ), vec3( 1.0 ), c ), 0.5 );
		c += ( hash( floor( px / 3.0 ) ) + hash( floor( px / 9.0 ) + 7.0 ) ) * 0.015 - 0.015;
		c = mix( c, ink * 1.5, edge( uv, t ) * outline * 0.6 );
	}
	else if ( style == 6 ) // Comic
	{
		vec3 base = toon( c );
		float shade = sqrt( clamp( luma( base ), 0.0, 1.0 ) );
		float d = dots( px, 0.7854, 1.0 - shade, pixel_size * 2.0 ) * smoothstep( 0.7, 0.3, shade );
		c = mix( base, base * 0.4, d );
		c = mix( c, ink, edge( uv, t ) * outline );
	}
	else if ( style == 7 ) // Anime
	{
		vec3 lifted = c * 1.35;
		float l = sqrt( clamp( luma( lifted ), 0.0, 1.0 ) );
		float tone = smoothstep( 0.24, 0.32, l ) * 0.5 + smoothstep( 0.5, 0.58, l ) * 0.5;
		vec3 o = mix( lifted * vec3( 0.78, 0.84, 1.15 ) * 0.9, lifted * vec3( 1.12, 1.05, 0.94 ), tone );
		o = mix( vec3( luma( o ) ), o, 1.25 );
		float k = max( depth_edge( uv, t ), edge( uv, t ) * 0.6 ) * outline;
		c = mix( o, o * 0.2 + ink * 0.5, k );
	}
	else if ( style == 8 ) // Sketch
	{
		float l = clamp( sqrt( clamp( luma( c ), 0.0, 1.0 ) ) * 1.5, 0.0, 1.0 );
		float spacing = max( pixel_size * 2.4, 4.0 );
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
	else if ( style == 9 ) // Watercolor
	{
		vec3 k = kuwahara( uv, t );
		vec3 wash = mix( vec3( luma( k ) ), k, 1.25 ) * 1.2 + 0.03;
		vec3 o = mix( wash, wash * 0.55, edge( uv, t ) * 0.8 * outline );
		float blotch = hash( floor( px / 6.0 ) + 3.0 ) * 0.5 + hash( floor( px / 17.0 ) + 9.0 ) * 0.5;
		o = mix( o, vec3( 0.98, 0.96, 0.9 ), 0.03 + blotch * 0.1 );
		o += ( hash( floor( px / 2.0 ) ) - 0.5 ) * 0.03;
		c = o;
	}
	else if ( style == 10 ) // Riso
	{
		float l = clamp( ( sqrt( clamp( luma( c ), 0.0, 1.0 ) ) - 0.12 ) * 1.5, 0.0, 1.0 );
		vec3 shifted = fetch( uv + vec2( 2.5, 1.5 ) * t );
		float a1 = dots( px, 0.26, clamp( 1.0 - l, 0.0, 1.0 ) * 0.95, pixel_size * 1.8 );
		float warm = clamp( shifted.r - shifted.g * 0.8, 0.0, 1.0 ) * 1.6 + clamp( 1.0 - l, 0.0, 1.0 ) * 0.35;
		float a2 = dots( px + vec2( 2.5, 1.5 ), 1.3, clamp( warm, 0.0, 1.0 ), pixel_size * 1.8 );
		vec3 paper = vec3( 0.97, 0.94, 0.86 );
		c = paper * mix( vec3( 1.0 ), vec3( 0.02, 0.45, 0.6 ), a1 ) * mix( vec3( 1.0 ), vec3( 0.96, 0.27, 0.46 ), a2 * 0.85 );
		c = mix( c, c * 0.45, depth_edge( uv, t ) * outline * 0.7 );
		c += ( hash( floor( px ) ) - 0.5 ) * 0.025;
	}
	else if ( style == 11 ) // Flat
	{
		float levels = max( bands, 2.0 ) + 1.0;
		vec3 g = pow( clamp( c, 0.0, 1.0 ), vec3( 0.5 ) );
		g = floor( g * levels + 0.5 ) / levels;
		vec3 o = g * g;
		o = mix( vec3( luma( o ) ), o, 1.25 );
		c = mix( o, ink, depth_edge( uv, t ) * outline );
	}

	ALBEDO = mix( src, clamp( c, 0.0, 1.0 ), strength );
}
