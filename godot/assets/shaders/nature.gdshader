// Port of s&box triplanar_nature.shader (materials/nature/*.vmat): world-space triplanar textures, a moss/grass top
// layer on upward faces, two-tone colour variation, darker undersides and a sunlit lift on top. Maths in s&box
// space (inches, z up) so the material scales carry over. The per-object tint is an instance uniform.
shader_type spatial;
#include "res://assets/shaders/fx_common.gdshaderinc"

instance uniform vec3 tint : source_color = vec3( 1.0 );
uniform sampler2D base_color : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D base_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D base_rough : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float base_scale = 64.0;
uniform float normal_strength = 1.0;
uniform sampler2D top_color : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D top_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float top_scale = 96.0;
uniform float top_amount = 0.0;
uniform float top_sharpness = 6.0;
uniform float top_roughness = 0.9;
uniform vec3 variation_color : source_color = vec3( 0.8, 0.75, 0.3 );
uniform float variation_amount = 0.0;
uniform float variation_scale = 300.0;
uniform float variation_contrast = 3.0;
uniform float bottom_darken = 0.0;
uniform vec3 top_light_color : source_color = vec3( 1.0, 1.0, 0.8 );
uniform float top_light = 0.0;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex()
{
	world_pos = ( MODEL_MATRIX * vec4( VERTEX, 1.0 ) ).xyz;
	world_normal = normalize( ( MODEL_MATRIX * vec4( NORMAL, 0.0 ) ).xyz );
}

vec3 weights( vec3 n )
{
	vec3 w = pow( abs( n ), vec3( 4.0 ) );
	return w / ( w.x + w.y + w.z );
}

vec3 tri_color( sampler2D t, vec3 p, vec3 w, float s )
{
	return texture( t, p.yz / s ).rgb * w.x + texture( t, p.xz / s ).rgb * w.y + texture( t, p.xy / s ).rgb * w.z;
}

// Whiteout-blended triplanar normal (s&box space).
vec3 tri_normal( sampler2D t, vec3 p, vec3 n, vec3 w, float s, float strength )
{
	vec3 tx = texture( t, p.yz / s ).rgb * 2.0 - 1.0;
	vec3 ty = texture( t, p.xz / s ).rgb * 2.0 - 1.0;
	vec3 tz = texture( t, p.xy / s ).rgb * 2.0 - 1.0;
	tx.xy *= strength; ty.xy *= strength; tz.xy *= strength;
	tx = vec3( tx.xy + n.yz, abs( tx.z ) * n.x );
	ty = vec3( ty.xy + n.xz, abs( ty.z ) * n.y );
	tz = vec3( tz.xy + n.xy, abs( tz.z ) * n.z );
	return normalize( tx.zxy * w.x + ty.xzy * w.y + tz.xyz * w.z );
}

void fragment()
{
	vec3 wp = sbox_pos( world_pos );
	vec3 n = normalize( vec3( -world_normal.z, -world_normal.x, world_normal.y ) );
	vec3 w = weights( n );

	vec3 base_col = tri_color( base_color, wp, w, base_scale ) * tint;
	float v = clamp( ( fbm( wp / variation_scale ) - 0.5 ) * variation_contrast + 0.5, 0.0, 1.0 );
	base_col = mix( base_col, variation_color * dot( base_col, vec3( 0.3, 0.59, 0.11 ) ) * 2.2, v * variation_amount );
	vec3 base_n = tri_normal( base_normal, wp, n, w, base_scale, normal_strength );
	float rough = dot( vec3( texture( base_rough, wp.yz / base_scale ).r, texture( base_rough, wp.xz / base_scale ).r, texture( base_rough, wp.xy / base_scale ).r ), w );

	float edge = fbm( wp / 40.0 ) - 0.5;
	float top = clamp( ( n.z - ( 1.0 - top_amount * 1.4 ) + edge * 0.15 ) * top_sharpness, 0.0, 1.0 ) * step( 0.001, top_amount );
	vec3 top_col = tri_color( top_color, wp, w, top_scale );
	vec3 top_n = tri_normal( top_normal, wp, n, w, top_scale, 1.0 );

	vec3 albedo = mix( base_col, top_col, top );
	vec3 ns = normalize( mix( base_n, top_n, top ) );
	float low = clamp( -n.z * 1.2 + 0.25, 0.0, 1.0 );
	albedo *= 1.0 - bottom_darken * low * 0.6;
	float up = clamp( n.z, 0.0, 1.0 );
	albedo = mix( albedo, albedo * top_light_color * 1.6, up * up * top_light * ( 1.0 - top ) );

	ALBEDO = albedo;
	ROUGHNESS = mix( rough, top_roughness, top );
	METALLIC = 0.0;
	AO = 1.0 - bottom_darken * low * 0.5;
	// s&box space normal back to Godot world, then to view space.
	vec3 nw = vec3( -ns.y, ns.z, -ns.x );
	NORMAL = normalize( ( VIEW_MATRIX * vec4( nw, 0.0 ) ).xyz );
}
