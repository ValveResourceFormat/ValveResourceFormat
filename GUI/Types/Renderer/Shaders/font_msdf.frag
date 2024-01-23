#version 460

in vec2 vTexCoord;
in vec4 vFragColor;

out vec4 color;

uniform sampler2D msdf;
uniform float g_fRange; // distance field's pixel range

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

float screenPxRange() {
    vec2 screenTexSize = vec2(1.0) / fwidth(vTexCoord);
    return max(0.5 * dot(vec2(g_fRange), screenTexSize), 1.0);
}

void main() {
    vec3 tex = texture(msdf, vTexCoord).rgb;
    float sd = median(tex.r, tex.g, tex.b);
    float screenPxDistance = screenPxRange() * (sd - 0.5);
    float opacity = clamp(screenPxDistance + 0.5, 0.0, 1.0);

    color = vFragColor;
    color.a = opacity - (1.0 - vFragColor.a);
}
