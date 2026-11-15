shader_type sky;
// Stylized gradient sky with drifting fbm clouds, sun glow and stars (for night maps).
uniform vec3 top_color : source_color = vec3(0.2, 0.3, 0.6);
uniform vec3 horizon_color : source_color = vec3(0.9, 0.6, 0.4);
uniform vec3 ground_color : source_color = vec3(0.2, 0.15, 0.15);
uniform vec3 cloud_color : source_color = vec3(1.0);
uniform vec3 sun_tint : source_color = vec3(1.0, 0.8, 0.6);
uniform float cloud_coverage = 0.45;
uniform float cloud_speed = 0.012;
uniform float stars = 0.0;
uniform float sun_size = 1.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p) {
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) { v += a * noise(p); p = p * 2.03 + vec2(1.7, 9.2); a *= 0.5; }
    return v;
}

void sky() {
    vec3 d = EYEDIR;
    float h = d.y;
    vec3 col = h > 0.0 ? mix(horizon_color, top_color, pow(clamp(h, 0.0, 1.0), 0.55))
                       : mix(horizon_color, ground_color, clamp(-h * 5.0, 0.0, 1.0));
    float sunAmt = 0.0;
    if (LIGHT0_ENABLED) {
        float sd = max(dot(d, LIGHT0_DIRECTION), 0.0);
        sunAmt = smoothstep(1.0 - 0.0006 * sun_size, 1.0 - 0.0003 * sun_size, sd);
        col += sun_tint * (sunAmt * 6.0 + pow(sd, 80.0) * 0.8 + pow(sd, 6.0) * 0.18);
    }
    if (h > 0.0) {
        vec2 uv = d.xz / (h + 0.18) * 1.1;
        float t = TIME * cloud_speed;
        float n = fbm(uv + vec2(t, t * 0.35));
        float c = smoothstep(1.0 - cloud_coverage, 1.0 - cloud_coverage + 0.35, n) * smoothstep(0.0, 0.2, h);
        float shade = fbm(uv * 1.7 + vec2(t * 1.3, 0.0));
        vec3 cc = cloud_color * mix(0.65, 1.15, shade);
        if (LIGHT0_ENABLED) cc += sun_tint * pow(max(dot(d, LIGHT0_DIRECTION), 0.0), 10.0) * 0.6;
        col = mix(col, cc, c * 0.9);
        if (stars > 0.0 && !AT_CUBEMAP_PASS) {
            vec2 sp = d.xz / (h + 0.05) * 140.0;
            float st = step(0.9965, hash(floor(sp)));
            float tw = 0.6 + 0.4 * sin(TIME * 3.0 + hash(floor(sp) + 3.1) * 40.0);
            col += vec3(st * tw * stars * (1.0 - c) * smoothstep(0.05, 0.35, h));
        }
    }
    COLOR = col;
}
