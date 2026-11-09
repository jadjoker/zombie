// Port of the s&box fx_water shader: the open sea. Moving swell (shared with the CPU so floaters ride it), wave
// normals from a height field, sky reflection with fresnel, sun glitter, crest foam and distance haze; from below a
// bright circular window overhead and a dark mirror outside it. Maths kept in s&box space (inches, xy across, z up).
shader_type spatial;
render_mode unshaded, cull_disabled;

uniform float wave_time = 0.0;
const float IN = 0.0254;

vec2 sb2(vec3 w) { return vec2(-w.z, -w.x) / IN; }
vec3 sb3(vec3 v) { return vec3(-v.z, -v.x, v.y) / IN; }

float swell(vec2 p, float t) {
	float h = 0.0;
	h += sin(dot(p, vec2(0.80, 0.60)) * 0.0060 + t * 0.55) * 1.00;
	h += sin(dot(p, vec2(-0.50, 0.87)) * 0.0110 + t * 0.80) * 0.65;
	h += sin(dot(p, vec2(0.30, -0.95)) * 0.0240 + t * 1.20) * 0.38;
	return h * 11.0;
}

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float vnoise2(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash21(i), hash21(i + vec2(1, 0)), f.x), mix(hash21(i + vec2(0, 1)), hash21(i + vec2(1, 1)), f.x), f.y);
}

float height(vec2 p, float t) {
	float h = 0.0;
	h += sin(dot(p, vec2(0.80, 0.60)) * 0.0060 + t * 0.55) * 1.00;
	h += sin(dot(p, vec2(-0.50, 0.87)) * 0.0110 + t * 0.80) * 0.65;
	h += sin(dot(p, vec2(0.30, -0.95)) * 0.0240 + t * 1.20) * 0.38;
	h += (vnoise2(p * 0.028 + vec2(t * 0.30, -t * 0.22)) - 0.5) * 0.9;
	h += (vnoise2(p * 0.085 + vec2(-t * 0.55, t * 0.40)) - 0.5) * 0.42;
	h += (vnoise2(p * 0.21 + vec2(t * 0.9, t * 0.7)) - 0.5) * 0.16;
	return h;
}

varying vec3 world_pos;

void vertex() {
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX.y += swell(sb2(w), wave_time) * IN;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 p = sb2(world_pos);
	float t = wave_time;
	vec3 to_cam = sb3(CAMERA_POSITION_WORLD - world_pos);
	float dist = length(to_cam);
	vec3 view = to_cam / max(dist, 0.001);
	bool above = view.z > 0.0;

	float e = 5.0;
	float h0 = height(p, t);
	float dx = height(p + vec2(e, 0.0), t) - h0;
	float dy = height(p + vec2(0.0, e), t) - h0;
	vec3 n = normalize(vec3(-dx * 0.55, -dy * 0.55, e * 0.35));
	n = normalize(mix(n, vec3(0, 0, 1), clamp(dist / 9000.0, 0.0, 1.0)));

	vec3 sun_dir = normalize(vec3(0.45, 0.25, 0.85));
	vec3 sun_col = vec3(1.0, 0.93, 0.78);
	vec3 col;
	if (above) {
		float ndv = clamp(dot(n, view), 0.0, 1.0);
		float fres = 0.03 + 0.97 * pow(1.0 - ndv, 5.0);
		vec3 deep = vec3(0.012, 0.10, 0.17);
		vec3 shallow = vec3(0.05, 0.38, 0.42);
		float crest = clamp(h0 * 0.35 + 0.45, 0.0, 1.0);
		vec3 body = mix(deep, shallow, crest * 0.7 + (1.0 - ndv) * 0.15);
		vec3 refl = reflect(-view, n);
		vec3 sky = mix(vec3(0.72, 0.86, 0.95), vec3(0.20, 0.45, 0.82), clamp(refl.z * 1.4, 0.0, 1.0));
		sky += sun_col * pow(clamp(dot(refl, sun_dir), 0.0, 1.0), 24.0) * 0.35;
		col = mix(body, sky, fres);
		float spec = pow(clamp(dot(refl, sun_dir), 0.0, 1.0), 380.0) * 14.0;
		float spec2 = pow(clamp(dot(refl, sun_dir), 0.0, 1.0), 60.0) * 0.6;
		col += sun_col * (spec + spec2);
		float foam_noise = vnoise2(p * 0.12 + t * 0.3) * 0.6 + vnoise2(p * 0.35 - t * 0.4) * 0.4;
		float foam = smoothstep(1.25, 1.9, h0 + foam_noise * 0.9 - 0.2);
		col = mix(col, vec3(0.92, 0.97, 1.0), foam * 0.85);
		col = mix(col, vec3(0.62, 0.78, 0.88), clamp((dist - 3500.0) / 14000.0, 0.0, 1.0) * 0.7);
	} else {
		float cosi = clamp(dot(vec3(-n.xy, -n.z), -view), 0.0, 1.0);
		float window = smoothstep(0.60, 0.72, cosi);
		float shimmer = height(p * 1.6, t * 1.5) * 0.5 + 0.5;
		vec3 sky_through = mix(vec3(0.18, 0.62, 0.78), vec3(0.55, 0.92, 1.0), clamp(shimmer * 0.9, 0.0, 1.0));
		float sun_glow = pow(clamp(dot(refract(-view, -n, 0.75), sun_dir), 0.0, 1.0), 18.0);
		sky_through += sun_col * sun_glow * 1.6;
		vec3 mirror = vec3(0.008, 0.055, 0.10) + vec3(0.0, 0.05, 0.05) * shimmer;
		col = mix(mirror, sky_through, window);
		float rim = 1.0 - abs(cosi - 0.66) * 14.0;
		col += vec3(0.25, 0.6, 0.7) * max(rim, 0.0) * 0.5;
	}
	ALBEDO = col;
}
