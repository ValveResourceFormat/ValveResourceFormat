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

    vec4 centerGroup = (topLeft + topRight + bottomLeft + bottomRight) * 0.25;

    vec4 topCross = (topLeftFar + top + center + topRightFar) * 0.25;
    vec4 rightCross = (topRightFar + right + center + bottomRightFar) * 0.25;
    vec4 bottomCross = (bottomLeftFar + bottom + center + bottomRightFar) * 0.25;
    vec4 leftCross = (topLeftFar + left + center + bottomLeftFar) * 0.25;

    return centerGroup * 0.5 + (topCross + rightCross + bottomCross + leftCross) * 0.125;
}

void main() {

    vec2 uv = gl_FragCoord.xy * 4.0 / vec2(textureSize(inputTexture, 0));

    vec4 downsampledColor = DownsampleJimenez(uv);
    vec3 thresholdedColor = ApplyThreshold(downsampledColor.rgb);
    vec3 bloomColor = thresholdedColor * bloomScale * g_flToneMapScalarLinear;

    outputColor = vec4(bloomColor, 1.0);
}
