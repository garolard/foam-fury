shader_type canvas_item;
// Light screen-space grade: chromatic fringing toward the edges, vignette, film grain, color tint.
uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap;
uniform float vignette = 0.32;
uniform float aberration = 1.0;
uniform float grain = 0.03;
uniform vec3 tint : source_color = vec3(1.0);
uniform vec3 lift : source_color = vec3(0.0);

void fragment() {
    vec2 uv = SCREEN_UV;
    vec2 c = uv - 0.5;
    float d = length(c);
    vec2 off = c * aberration * 0.006 * d;
    vec3 col = vec3(texture(screen, uv + off).r, texture(screen, uv).g, texture(screen, uv - off).b);
    col *= tint;
    col += lift * (1.0 - col);
    col *= mix(1.0 - vignette, 1.0, smoothstep(0.85, 0.3, d * 1.1));
    float n = fract(sin(dot(uv * vec2(12.9898, 78.233) + fract(TIME) * 7.0, vec2(1.0, 1.7))) * 43758.5453);
    col += (n - 0.5) * grain;
    COLOR = vec4(col, 1.0);
}
