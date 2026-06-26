#version 460

uniform float bloomScale;
uniform float g_flToneMapScalarLinear;
uniform vec2 thresholdParams;

layout(binding = 0) uniform sampler2D inputTexture;
layout(location = 0) out vec4 outputColor;

const vec3 LUMINANCE_WEIGHTS = vec3(0.3, 0.59, 0.11);

float CalculateLuminance(vec3 color) {
    return dot(color, LUMINANCE_WEIGHTS);
}

float KarisWeight(vec3 color) {
    return 1.0 / (1.0 + CalculateLuminance(color));
}

vec3 KarisAverage(vec3 a, vec3 b, vec3 c, vec3 d) {
    float wa = KarisWeight(a);
    float wb = KarisWeight(b);
    float wc = KarisWeight(c);
    float wd = KarisWeight(d);
    return (a * wa + b * wb + c * wc + d * wd) / (wa + wb + wc + wd);
}

vec3 ApplyThreshold(vec3 color) {
    float luminance = CalculateLuminance(color);
    float thresholdFactor = g_flToneMapScalarLinear * luminance * thresholdParams.y + thresholdParams.x;
    float thresholdWeight = clamp(thresholdFactor, 0.0, 1.0);
    return color * thresholdWeight;
}

vec4 DownsampleJimenez(vec2 uv) {
    vec4 center = textureLod(inputTexture, uv, 0.0);

    vec4 topLeft = textureLodOffset(inputTexture, uv, 0.0, ivec2(-1, -1));
    vec4 topRight = textureLodOffset(inputTexture, uv, 0.0, ivec2(1, -1));
    vec4 bottomLeft = textureLodOffset(inputTexture, uv, 0.0, ivec2(-1, 1));
    vec4 bottomRight = textureLodOffset(inputTexture, uv, 0.0, ivec2(1, 1));

    vec4 top = textureLodOffset(inputTexture, uv, 0.0, ivec2(0, -2));
    vec4 left = textureLodOffset(inputTexture, uv, 0.0, ivec2(-2, 0));
    vec4 right = textureLodOffset(inputTexture, uv, 0.0, ivec2(2, 0));
    vec4 bottom = textureLodOffset(inputTexture, uv, 0.0, ivec2(0, 2));

    vec4 topLeftFar = textureLodOffset(inputTexture, uv, 0.0, ivec2(-2, -2));
    vec4 topRightFar = textureLodOffset(inputTexture, uv, 0.0, ivec2(2, -2));
    vec4 bottomLeftFar = textureLodOffset(inputTexture, uv, 0.0, ivec2(-2, 2));
    vec4 bottomRightFar = textureLodOffset(inputTexture, uv, 0.0, ivec2(2, 2));

    vec3 centerBox = KarisAverage(topLeft.rgb, topRight.rgb, bottomLeft.rgb, bottomRight.rgb);
    vec3 topLeftBox = KarisAverage(topLeftFar.rgb, top.rgb, left.rgb, center.rgb);
    vec3 topRightBox = KarisAverage(topRightFar.rgb, top.rgb, right.rgb, center.rgb);
    vec3 bottomLeftBox = KarisAverage(bottomLeftFar.rgb, bottom.rgb, left.rgb, center.rgb);
    vec3 bottomRightBox = KarisAverage(bottomRightFar.rgb, bottom.rgb, right.rgb, center.rgb);

    return vec4(centerBox * 0.5 + (topLeftBox + topRightBox + bottomLeftBox + bottomRightBox) * 0.125, 1.0);
}

void main() {

    vec2 uv = gl_FragCoord.xy * 4.0 / vec2(textureSize(inputTexture, 0));

    vec4 downsampledColor = DownsampleJimenez(uv);
    vec3 thresholdedColor = ApplyThreshold(downsampledColor.rgb);
    vec3 bloomColor = thresholdedColor * bloomScale * g_flToneMapScalarLinear;

    outputColor = vec4(bloomColor, 1.0);
}
