#version 460

// in regards to float weight:
// change the weight to be wider (+1 ... +5)
// or change the weight to be slimmer (-1 ... -5)
// if you pass weight as varying, then the bottom calculation can reduced to
// (0.5 - weight)
#define WEIGHT 1.0
#define OUTLINE_OFFSET 0.7
#define OUTLINE_COLOR vec3(0.0)
#define BIAS 0.5

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
    float distance = median(tex.r, tex.g, tex.b);
    float range = screenPxRange();

    // offset the SDF edge to make the font appear wider or slimmer
    float weight = (0.5 + (WEIGHT * -0.1));
    float characterDistance = range * (distance - weight);
    float outlineDistance = range * (distance - (weight - OUTLINE_OFFSET));

    // calculate how much color is required
    float characterColorAmount = clamp(characterDistance + 0.5, 0.0, 1.0);
    float outlineColorAmount = clamp(outlineDistance + 0.5, 0.0, 1.0);

    // mix between font color and outline color
    color.rgb = mix(OUTLINE_COLOR, vFragColor.rgb, characterColorAmount);

    // opacity is a max, so unreasonable outlines don't destroy the rendering
    color.a = pow(smoothstep(0.025, WEIGHT - OUTLINE_OFFSET, distance), BIAS);

    if (color.a < 0.01)
    {
        discard;
    }
}
