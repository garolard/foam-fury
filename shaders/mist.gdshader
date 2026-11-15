shader_type fog;
// Low rolling ground mist for FogVolumes (requires volumetric fog in the Environment).
uniform float density = 0.6;
uniform vec3 albedo : source_color = vec3(0.7, 0.8, 0.75);
uniform vec3 emission : source_color = vec3(0.0);
uniform float height = 2.5;
uniform float speed = 0.06;

float hash3(vec3 p) { return fract(sin(dot(p, vec3(17.1, 113.3, 71.7))) * 43758.5453); }
float noise3(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash3(i), hash3(i + vec3(1,0,0)), f.x), mix(hash3(i + vec3(0,1,0)), hash3(i + vec3(1,1,0)), f.x), f.y),
               mix(mix(hash3(i + vec3(0,0,1)), hash3(i + vec3(1,0,1)), f.x), mix(hash3(i + vec3(0,1,1)), hash3(i + vec3(1,1,1)), f.x), f.y), f.z);
}

void fog() {
    vec3 p = WORLD_POSITION * 0.18 + vec3(TIME * speed, 0.0, TIME * speed * 0.6);
    float n = noise3(p) * 0.65 + noise3(p * 2.4 + 7.0) * 0.35;
    float h = clamp(1.0 - WORLD_POSITION.y / height, 0.0, 1.0);
    DENSITY = density * smoothstep(0.3, 0.8, n) * h * h;
    ALBEDO = albedo;
    EMISSION = emission;
}
