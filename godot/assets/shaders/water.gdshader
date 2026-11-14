// Port of the s&box fx_water shader: the open sea. Moving swell (shared with the CPU so floaters ride it) and wave
// normals from a height field. From above the surface is lit by the engine, so it reflects the level's real sky
// (day or night) with proper fresnel and catches the real sun; foam rides the crests and froths round anything poking
// through. From below: a bright circular window overhead and a dark mirror outside it. Maths in s&box space (inches).
shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

uniform float wave_time = 0.0;
// The blue hole (issue #35, after the Great Blue Hole of Belize): a circle of near-black deep blue where the diving
// board points, ringed by pale reef water, with the rest of the lagoon a lighter turquoise. s&box inches; radius 0 = off.
uniform vec2 hole_center = vec2(0.0);
uniform float hole_radius = 0.0;
// Step 8: the scene depth behind the water, for foam where it meets the stilts, the dock and anything floating.
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
const float IN = 0.0254;

vec2 sb2(vec3 w) { return vec2(-w.z, -w.x) / IN; }
vec3 sb3(vec3 v) { return vec3(-v.z, -v.x, v.y) / IN; }
// s&box direction back to a Godot world direction.
vec3 gd3(vec3 v) { return vec3(-v.y, v.z, -v.x); }

float swell(vec2 p, float t) {
	float h = 0.0;
	h += sin(dot(p, vec2(0.80, 0.60)) * 0.0060 + t * 0.55) * 1.00;
	h += sin(dot(p, vec2(-0.50, 0.87)) * 0.0110 + t * 0.80) * 0.65;
	h += sin(dot(p, vec2(0.30, -0.95)) * 0.0240 + t * 1.20) * 0.38;
	return h * 7.0;
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

// The alien sea (playtest): the water drifts between deep purple, blue and green in broad slow patches.
vec3 alien_mix(vec2 p, float t, vec3 purple, vec3 blue, vec3 green) {
	float u = vnoise2(p * 0.0016 + vec2(t * 0.020, -t * 0.015));
	float v = vnoise2(p * 0.0011 - vec2(t * 0.013, t * 0.018) + vec2(7.3, 2.1));
	vec3 col = mix(purple, blue, smoothstep(0.25, 0.75, u));
	return mix(col, green, smoothstep(0.4, 0.82, v));
}

// Thin glowing veins drifting through it (bioluminescence): bright where a noise field crosses its middle.
float veins(vec2 p, float t) {
	float a = 1.0 - abs(vnoise2(p * 0.010 + vec2(t * 0.05, -t * 0.03)) * 2.0 - 1.0);
	float b = 1.0 - abs(vnoise2(p * 0.023 - vec2(t * 0.07, t * 0.04) + vec2(3.7, 9.1)) * 2.0 - 1.0);
	return pow(a, 14.0) * 0.8 + pow(b, 18.0) * 0.5;
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

	if (above) {
		// Fine ripples on top of the swell so close water isn't glassy.
		float r0 = vnoise2(p * 0.55 + vec2(t * 1.1, t * 0.8));
		float rx = vnoise2(p * 0.55 + vec2(t * 1.1, t * 0.8) + vec2(0.08, 0.0)) - r0;
		float ry = vnoise2(p * 0.55 + vec2(t * 1.1, t * 0.8) + vec2(0.0, 0.08)) - r0;
		float near = 1.0 - clamp(dist / 2500.0, 0.0, 1.0);
		n = normalize(n + vec3(-rx, -ry, 0.0) * 1.6 * near);
		float ndv = clamp(dot(n, view), 0.0, 1.0);
		// The body: dark deep water, a touch lighter and greener where crests thin out and light passes through.
		float crest = clamp(h0 * 0.35 + 0.45, 0.0, 1.0);
		vec3 deep = alien_mix(p, t, vec3(0.035, 0.008, 0.07), vec3(0.006, 0.02, 0.075), vec3(0.004, 0.05, 0.035));
		vec3 shallow = alien_mix(p, t, vec3(0.24, 0.07, 0.4), vec3(0.05, 0.17, 0.44), vec3(0.06, 0.42, 0.22));
		vec3 glow = alien_mix(p * 1.7, t, vec3(0.62, 0.22, 1.0), vec3(0.2, 0.5, 1.0), vec3(0.3, 1.0, 0.45));
		float inside = 0.0;
		if (hole_radius > 0.0) {
			float d = length(p - hole_center) / hole_radius + (vnoise2(p * 0.004) - 0.5) * 0.12;
			inside = 1.0 - smoothstep(0.82, 1.0, d);
			float rim = smoothstep(0.8, 0.98, d) * (1.0 - smoothstep(1.02, 1.35, d));
			float lagoon = smoothstep(1.0, 1.6, d);
			// Lagoon: the same colours a little brighter over the reef. Rim: a glowing ring where the reef nearly breaks the surface.
			deep = mix(deep, deep * 1.8 + vec3(0.01), lagoon);
			shallow = mix(shallow, shallow * 1.35, lagoon);
			deep = mix(deep, glow * 0.3, rim * 0.7);
			shallow = mix(shallow, glow * 0.6, rim * 0.7);
			// The hole: a much darker version of the colours round it, still faintly glowing.
			deep = mix(deep, deep * 0.22, inside);
			shallow = mix(shallow, shallow * 0.2, inside);
		}
		vec3 body = mix(deep, shallow, crest * 0.6);
		// Crest foam: sparse streaks on the tallest waves only.
		float foam_noise = vnoise2(p * 0.12 + t * 0.3) * 0.6 + vnoise2(p * 0.35 - t * 0.4) * 0.4;
		float foam = smoothstep(1.7, 2.3, h0 + foam_noise * 0.9 - 0.2) * 0.7;
		// Contact foam: a thin frothy band where something pokes through the surface (scene depth vs the water).
		float scene_d = texture(depth_tex, SCREEN_UV).r;
		vec4 sv = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, scene_d, 1.0);
		float behind = (-sv.z / sv.w - -VERTEX.z) / IN;
		float froth = vnoise2(p * 0.18 + vec2(t * 0.5, -t * 0.35)) * 0.6 + vnoise2(p * 0.5 - t * 0.6) * 0.4;
		float contact = (1.0 - smoothstep(0.0, 5.0 + froth * 6.0, behind)) * step(0.0, behind) * smoothstep(0.35, 0.7, froth + 0.2);
		foam = max(foam, contact * 0.6);
		ALBEDO = mix(body, vec3(0.75, 0.8, 0.82), foam);
		ROUGHNESS = mix(0.04 + (1.0 - near) * 0.06, 0.8, foam);
		METALLIC = 0.0;
		SPECULAR = mix(0.5, 0.2, foam);
		// A little light scattered up through the wave faces facing away from us, a soft glow of the water's own
		// colour, and the drifting veins (dimmer down the hole).
		float lit = (1.0 - foam) * (1.0 - inside * 0.75);
		EMISSION = shallow * crest * (1.0 - ndv) * 0.35 * lit + shallow * 0.18 * lit + glow * veins(p, t) * 0.7 * lit;
		NORMAL = normalize((VIEW_MATRIX * vec4(gd3(n), 0.0)).xyz);
	} else {
		vec3 sun_dir = normalize(vec3(0.45, 0.25, 0.85));
		vec3 sun_col = vec3(1.0, 0.93, 0.78);
		float cosi = clamp(dot(vec3(-n.xy, -n.z), -view), 0.0, 1.0);
		float window = smoothstep(0.60, 0.72, cosi);
		float shimmer = height(p * 1.6, t * 1.5) * 0.5 + 0.5;
		vec3 tint = alien_mix(p, t, vec3(0.5, 0.3, 0.95), vec3(0.25, 0.55, 0.95), vec3(0.3, 0.9, 0.6));
		vec3 sky_through = mix(tint * 0.55, mix(tint, vec3(0.9), 0.35), clamp(shimmer * 0.9, 0.0, 1.0));
		float sun_glow = pow(clamp(dot(refract(-view, -n, 0.75), sun_dir), 0.0, 1.0), 18.0);
		sky_through += sun_col * sun_glow * 1.6;
		vec3 mirror = tint * 0.09 + tint * 0.06 * shimmer;
		vec3 col = mix(mirror, sky_through, window);
		float rim = 1.0 - abs(cosi - 0.66) * 14.0;
		col += tint * max(rim, 0.0) * 0.55 + tint * veins(p, t) * 0.35;
		// From below it glows on its own (unlit look): no albedo or reflections, just the emission.
		ALBEDO = vec3(0.0);
		ROUGHNESS = 1.0;
		SPECULAR = 0.0;
		EMISSION = col;
	}
	ALPHA = 1.0;
}
