#version 460

#include "common/utils.slang"

// D_EXPOSED_THRESHOLD = 1 -> exposes the scene and thresholds each of the 13 taps before averaging them,
// without Karis weighting
#define D_EXPOSED_THRESHOLD 0

uniform float g_flBloomScale;
uniform float g_flToneMapScalarLinear;
uniform vec2 g_flThresholdParams;
uniform float g_flSceneBloomWeight = 1.0;
uniform bool g_bEffectsBloom;
uniform bool g_bEffectsBloomClamped;
uniform bool g_bEffectsBloomEncoded;

#if D_EXPOSED_THRESHOLD == 1
    uniform float g_flExposureBiasScaleFactor = 1.0;
#endif

layout(binding = 0) uniform sampler2D inputTexture;

// Geometry drawn only to glow, at a lower resolution than the scene, bloomed without the threshold
layout(binding = 1) uniform sampler2D g_tEffectsBloom;
layout(location = 0) out vec4 outputColor;

const vec3 LUMINANCE_WEIGHTS = vec3(0.3, 0.59, 0.11);

float CalculateLuminance(vec3 color) {
    return dot(color, LUMINANCE_WEIGHTS);
}

vec3 ApplyThreshold(vec3 color) {
    float luminance = CalculateLuminance(color);
    float thresholdFactor = g_flToneMapScalarLinear * luminance * g_flThresholdParams.y + g_flThresholdParams.x;
    float thresholdWeight = clamp(thresholdFactor, 0.0, 1.0);
    return color * thresholdWeight;
}

#if D_EXPOSED_THRESHOLD == 1

// The scene is read as if stored exposed and Karis encoded, so every bilinear tap below averages
// its four texels in the encoded space and decodes the result. Exposure ends up in the threshold
// twice, once in the color and once in the luminance scale.
vec3 FetchExposedEncoded(ivec2 coord, float exposure)
{
    ivec2 clampedCoord = clamp(coord, ivec2(0), textureSize(inputTexture, 0) - 1);
    vec3 color = clamp(texelFetch(inputTexture, clampedCoord, 0).rgb, 0.0, 65504.0) * exposure;
    return color / (1.0 + max3(color));
}

vec3 DownsampleThresholdJimenez()
{
    float exposure = g_flToneMapScalarLinear * g_flExposureBiasScaleFactor;

    // The taps sit on texel corners, at -2 to +2 texels from the quarter resolution texel centre,
    // so they cover a 6x6 block of texels
    ivec2 baseCoord = ivec2(gl_FragCoord.xy) * 4 - 1;
    vec3 encoded[6][6];

    for (int y = 0; y < 6; y++)
    {
        for (int x = 0; x < 6; x++)
        {
            encoded[y][x] = FetchExposedEncoded(baseCoord + ivec2(x, y), exposure);
        }
    }

    vec3 taps[5][5];

    for (int y = 0; y < 5; y++)
    {
        for (int x = 0; x < 5; x++)
        {
            vec3 average = (encoded[y][x] + encoded[y][x + 1] + encoded[y + 1][x] + encoded[y + 1][x + 1]) * 0.25;
            vec3 decoded = average / (1.0 - max3(average));
            taps[y][x] = ApplyThreshold(decoded);
        }
    }

    vec3 centerBox = (taps[1][1] + taps[1][3] + taps[3][1] + taps[3][3]) * 0.25;
    vec3 topLeftBox = (taps[0][0] + taps[0][2] + taps[2][0] + taps[2][2]) * 0.25;
    vec3 topRightBox = (taps[0][2] + taps[0][4] + taps[2][2] + taps[2][4]) * 0.25;
    vec3 bottomLeftBox = (taps[2][0] + taps[2][2] + taps[4][0] + taps[4][2]) * 0.25;
    vec3 bottomRightBox = (taps[2][2] + taps[2][4] + taps[4][2] + taps[4][4]) * 0.25;

    return centerBox * 0.5 + (topLeftBox + topRightBox + bottomLeftBox + bottomRightBox) * 0.125;
}

#else

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

#endif

void main() {

    vec2 uv = gl_FragCoord.xy * 4.0 / vec2(textureSize(inputTexture, 0));

#if D_EXPOSED_THRESHOLD == 1
    vec3 bloomColor = DownsampleThresholdJimenez() * g_flSceneBloomWeight * g_flBloomScale;
#else
    vec4 downsampledColor = DownsampleJimenez(uv);
    vec3 bloomColor = ApplyThreshold(downsampledColor.rgb) * g_flSceneBloomWeight * g_flBloomScale;
#endif

    if (g_bEffectsBloom)
    {
        // A bilinear tap at the output texel centre averages the half resolution effects texels it covers.
        // Drawing the effects at quarter resolution and reading them texel for texel would rasterise thin
        // effects differently, but soft sprites come out the same.
        vec3 effectsColor = textureLod(g_tEffectsBloom, uv, 0.0).rgb;

        // The effects end up in an 8-bit target written with an sRGB encode, so they bloom clamped, and
        // gamma encoded when read back without decoding
        if (g_bEffectsBloomClamped)
        {
            effectsColor = clamp(effectsColor, 0.0, 1.0);
        }

        if (g_bEffectsBloomEncoded)
        {
            effectsColor = SrgbLinearToGamma(effectsColor);
        }

        bloomColor += effectsColor;
    }

    outputColor = vec4(bloomColor, 1.0);
}
