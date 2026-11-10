// Scope glass: a dark, deep lens that mirrors the scene sharply at glancing angles, with the green-to-purple sheen
// of an anti-glare coating that shifts as the gun moves, and a faint glow deep inside the tube.
shader_type spatial;
render_mode specular_schlick_ggx;

uniform vec3 tint : source_color = vec3(0.02, 0.05, 0.08);
uniform float coating = 0.55;

vec3 film(float t) {
	// Thin-film colours: green, through teal and blue, to violet.
	return 0.5 + 0.5 * cos(6.28318 * (t + vec3(0.0, 0.33, 0.67)));
}

void fragment() {
	float ndv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float fres = pow(1.0 - ndv, 3.0);
	ALBEDO = tint;
	METALLIC = 0.0;
	ROUGHNESS = 0.03;
	SPECULAR = 0.9;
	CLEARCOAT = 1.0;
	CLEARCOAT_ROUGHNESS = 0.02;
	// The coating sheen: strongest off-axis, colour sliding with the angle.
	vec3 sheen = film(ndv * 0.8 + 0.15) * vec3(0.55, 0.9, 0.7);
	// Light caught deep inside: a soft glow when looking straight down the tube.
	vec3 depth = vec3(0.05, 0.12, 0.16) * pow(ndv, 4.0);
	EMISSION = sheen * coating * (0.15 + 0.85 * fres) * 0.35 + depth;
	RIM = 0.4;
	RIM_TINT = 0.2;
}
