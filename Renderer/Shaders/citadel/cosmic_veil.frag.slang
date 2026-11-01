#version 460

#include "cosmic_veil_features.slang"

#include "../common/utils.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;
centroid in vec4 vVertexColorOut;
out vec4 outputColor;

#include "../common/view_constants.slang"
#include "../common/fog.slang"

uniform sampler2D g_tBlueNoise; // reserved
uniform sampler2D g_tSceneColor; // reserved

uniform sampler2D g_tMasks; // r=refract, g=hue shift
uniform sampler2D g_tWarpMask; // r=warp, g=reveal

uniform bool g_bDoubleSidedVeil = false;

// A front face, or either face of a double sided veil, shows the scene gray with the veiled amounts.
// A back face shows it in color with its hue shifted.
uniform vec2 g_vHueShiftAmounts = vec2(-0.1, 0.1);
uniform vec2 g_vRefractBlurAmounts = vec2(0.005);
uniform vec2 g_vRefractBrightnessAmounts = vec2(1.0, 0.2);
uniform vec2 g_vRefractNoiseAmounts = vec2(0.5);
uniform vec2 g_vRefractBlurAmountsVeiled = vec2(0.005);
uniform vec2 g_vRefractBrightnessAmountsVeiled = vec2(1.0, 0.2);
uniform vec2 g_vRefractNoiseAmountsVeiled = vec2(0.5);

uniform float g_flRevealMax = 1.0;
uniform vec2 g_vWarpMaskBrightnessAmounts = vec2(1.0);

uniform float g_flSmokeRateVariance = -0.8;
uniform float g_flSmokeSpeed = 1.0;
uniform float g_flSmokeStepRotation = 0.01;
uniform float g_flSmokeStepSize = 0.005;
uniform vec2 g_vSmokeSpatialFrequencies = vec2(12.0, 17.0);

const int SMOKE_STEPS = 25;

// Unit disc offsets of the blur taps
const vec2 BLUR_OFFSETS[8] = vec2[](
    vec2(-0.0876, 0.9703),
    vec2(0.4802, 0.5651),
    vec2(0.1851, 0.1580),
    vec2(-0.2616, -0.0617),
    vec2(-0.5477, -0.6603),
    vec2(-0.5325, 0.0711),
    vec2(-0.0751, -0.8954),
    vec2(0.6384, -0.4054));

// A parabolic approximation of sin(), for an angle in radians
float ApproximateSin(float angle)
{
    float wrapped = angle - floor(angle / TAU) * TAU;
    float quarterTurns = wrapped * (2.0 / PI);

    float rising = saturate(1.0 - pow2(quarterTurns - 1.0));
    float falling = saturate(1.0 - pow2(quarterTurns - 3.0));
    return rising - falling;
}

float ApproximateCos(float angle)
{
    return ApproximateSin(angle + PI * 0.5);
}

// Drifts the coordinates through a swirling field, by as much as the warp mask says
vec2 WarpSmokeCoords(vec2 texCoord, float warpAmount)
{
    float halfVariance = 0.5 * g_flSmokeRateVariance;
    float rateY = (0.5 + halfVariance) * g_flSmokeSpeed;
    float rateX = (0.5 - halfVariance) * g_flSmokeSpeed;

    float stepSin = ApproximateSin(g_flSmokeStepRotation);
    float stepCos = ApproximateCos(g_flSmokeStepRotation);

    vec2 position = texCoord * 2.0 - 1.0;

    for (int i = 0; i < SMOKE_STEPS; i++)
    {
        vec2 drift = vec2(
            ApproximateSin(g_vSmokeSpatialFrequencies.y * position.y + g_flTime * rateY),
            ApproximateCos(g_vSmokeSpatialFrequencies.x * position.x + g_flTime * rateX));

        vec2 stepped = position + drift * g_flSmokeStepSize;
        position = vec2(
            stepCos * stepped.x - stepSin * stepped.y,
            stepSin * stepped.x + stepCos * stepped.y);
    }

    return mix(texCoord, position * 0.5 + 0.5, warpAmount);
}

vec3 FetchScene(vec2 screenUv)
{
    ivec2 size = textureSize(g_tSceneColor, 0);
    ivec2 texel = min(ivec2(saturate(screenUv) * vec2(size)), size - 1);

    return texelFetch(g_tSceneColor, texel, 0).rgb;
}

// The game skips the taps that land outside of the veil, by the front depth of the veils
vec3 BlurScene(vec2 center, float radius)
{
    vec3 sum = FetchScene(center);

    for (int i = 0; i < BLUR_OFFSETS.length(); i++)
    {
        sum += FetchScene(center + BLUR_OFFSETS[i] * radius);
    }

    return sum / float(BLUR_OFFSETS.length() + 1);
}

vec3 RgbToHsv(vec3 color)
{
    float value = Max3(color);
    float range = value - Min3(color);

    if (range == 0.0)
    {
        return vec3(0.0, 0.0, value);
    }

    vec3 distances = (vec3(value) - color) / range;
    float hue;

    if (color.r >= value)
    {
        hue = distances.b - distances.g;
    }
    else if (color.g >= value)
    {
        hue = distances.r - distances.b + 2.0;
    }
    else
    {
        hue = distances.g - distances.r + 4.0;
    }

    return vec3(fract(hue / 6.0), range / value, value);
}

vec3 HsvToRgb(vec3 hsv)
{
    float scaledHue = hsv.x * 6.0;
    float sector = floor(scaledHue);
    float fraction = scaledHue - sector;

    float value = hsv.z;
    float p = value * (1.0 - hsv.y);
    float q = value * (1.0 - hsv.y * fraction);
    float t = value * (1.0 - hsv.y * (1.0 - fraction));

    // The shifted hue is not wrapped, a sector outside of 0 to 4 is the last one
    if (sector == 0.0)
    {
        return vec3(value, t, p);
    }
    if (sector == 1.0)
    {
        return vec3(q, value, p);
    }
    if (sector == 2.0)
    {
        return vec3(p, value, t);
    }
    if (sector == 3.0)
    {
        return vec3(p, q, value);
    }
    if (sector == 4.0)
    {
        return vec3(t, p, value);
    }

    return vec3(value, p, q);
}

vec3 ShiftHue(vec3 color, float shift)
{
    float peak = Max3(color);
    if (peak <= 0.0)
    {
        return color;
    }

    vec3 hsv = RgbToHsv(color / peak);
    hsv.x += shift;
    return HsvToRgb(hsv) * peak;
}

void main()
{
    vec2 texCoord = saturate(vTexCoordOut);

    vec2 warpMask = texture(g_tWarpMask, texCoord).xy;
    if (vVertexColorOut.a * g_flRevealMax < warpMask.y)
    {
        discard;
    }

    vec2 maskCoord = texCoord;
    if (warpMask.x > 0.0)
    {
        maskCoord = saturate(WarpSmokeCoords(texCoord, warpMask.x));
    }

    vec2 masks = texture(g_tMasks, maskCoord).xy;

    vec2 screenUv = gl_FragCoord.xy / vec2(textureSize(g_tSceneColor, 0));

    bool veiled = gl_FrontFacing || g_bDoubleSidedVeil;

    vec2 noiseAmounts = veiled ? g_vRefractNoiseAmountsVeiled : g_vRefractNoiseAmounts;
    vec2 blurAmounts = veiled ? g_vRefractBlurAmountsVeiled : g_vRefractBlurAmounts;
    vec2 brightnessAmounts = veiled ? g_vRefractBrightnessAmountsVeiled : g_vRefractBrightnessAmounts;

    float noiseAmount = mix(noiseAmounts.x, noiseAmounts.y, masks.x);
    float blurRadius = mix(blurAmounts.x, blurAmounts.y, masks.x);

    vec2 noise = texelFetch(g_tBlueNoise, ivec2(mod(gl_FragCoord.xy, textureSize(g_tBlueNoise, 0))), 0).xy * 2.0 - 1.0;
    vec2 refractedUv = screenUv + noise * blurRadius * noiseAmount;

    vec3 color = BlurScene(refractedUv, blurRadius);

    if (veiled)
    {
        color = vec3(GetLuma(color));
    }
    else
    {
        float hueShift = mix(g_vHueShiftAmounts.x, g_vHueShiftAmounts.y, masks.y);
        color = mix(color, ShiftHue(color, hueShift), mix(0.7, 1.0, masks.x));
    }

    float brightness = mix(brightnessAmounts.x, brightnessAmounts.y, masks.x);
    brightness *= mix(g_vWarpMaskBrightnessAmounts.x, g_vWarpMaskBrightnessAmounts.y, warpMask.x);
    color *= brightness;

    ApplyFog(color, vFragPosition);

    outputColor = vec4(color, 1.0);
}
