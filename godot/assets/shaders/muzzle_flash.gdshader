// Muzzle flash pieces, drawn on plain quads and added on top of the scene.
// kind 0: the star burst at the muzzle, always facing the camera: a white-hot core, ragged orange spikes.
// kind 1: a flame tongue (UV y runs from the muzzle, 0, to the tip, 1): hot at the base, licking out and thinning.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform int kind = 0;
uniform float seed = 0.0;
uniform float fade = 1.0;           // 1 on the first frame, down to 0
uniform vec3 hot : source_color = vec3(1.0, 0.95, 0.8);
uniform vec3 warm : source_color = vec3(1.0, 0.55, 0.15);
uniform float energy = 3.0;

float hash(float n) { return fract(sin(n) * 43758.5453); }

float noise1(float x) {
	float i = floor(x);
	float f = fract(x);
	return mix(hash(i + seed * 17.0), hash(i + 1.0 + seed * 17.0), f * f * (3.0 - 2.0 * f));
}

void vertex() {
	if (kind == 0) {
		// Billboard, keeping the node's scale.
		float s = length(MODEL_MATRIX[0].xyz);
		MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * s, INV_VIEW_MATRIX[1] * s, INV_VIEW_MATRIX[2] * s, MODEL_MATRIX[3]);
	}
}

void fragment() {
	vec2 uv = UV;
	vec3 col;
	float a;
	if (kind == 0) {
		vec2 c = (uv - 0.5) * 2.0;
		float r = length(c);
		float ang = atan(c.y, c.x) + seed * 6.28;
		// Five to seven ragged spikes of different lengths.
		float spikes = pow(abs(cos(ang * 3.0)), 6.0) * (0.55 + 0.45 * noise1(ang * 1.9 + 3.0));
		spikes = max(spikes, pow(abs(cos(ang * 2.0 + 1.3)), 10.0) * 0.8);
		float reach = 0.42 + 0.58 * spikes;
		float body = 1.0 - smoothstep(reach * 0.35, reach, r);
		float core = exp(-r * r * 14.0);
		a = body * 0.7 + core;
		col = mix(warm, hot, clamp(core * 1.5 + body * 0.25, 0.0, 1.0));
	} else {
		float y = uv.y;
		// Width along the tongue: swells just past the muzzle, tapers to a point, with a flicker of noise.
		float w = sin(3.14159 * pow(y, 0.55)) * (1.0 - y * 0.6);
		w *= 0.75 + 0.5 * noise1(y * 6.0 + seed * 9.0);
		float d = abs(uv.x - 0.5) * 2.0 / max(w, 0.001);
		float shape = (1.0 - smoothstep(0.25, 1.0, d)) * (1.0 - smoothstep(0.6, 1.0, y));
		float heat = (1.0 - y) * (1.0 - d * 0.6);
		a = shape;
		col = mix(warm, hot, clamp(heat * 1.4, 0.0, 1.0));
	}
	ALBEDO = col * a * energy * fade;
}
