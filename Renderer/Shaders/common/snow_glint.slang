#version 460

#ifndef VRF_SNOW_GLINT_INCLUDE
#define VRF_SNOW_GLINT_INCLUDE

#if (SNOW_GLINT)

// Sparkling disks in a grid of one world unit cells, each with a jittered normal.

uniform float g_flSnowGlintIntensity = 1.0;
uniform float g_flSnowGlintSparkle = 1.0;
uniform float g_flSnowGlintTwinkleAmount = 0.02;
uniform float g_flSnowGlintTwinkleSpeed = 5.0;

// Maps 16 bits of the hash to 0 to 1
const float SNOW_GLINT_HASH_SCALE = 1.0 / 65535.0;

uvec3 SnowGlintHash(uvec3 v)
{
    v = v * 1664525u + 1013904223u;

    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;

    v ^= v >> 16u;

    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;

    return v;
}

// Moves the roughness and reflectance toward the disk of the cell under the pixel
void ApplySnowGlint(inout MaterialProperties_t mat, float strength, inout float reflectance)
{
    if (strength <= 0.0)
    {
        return;
    }

    float footprint = length(fwidth(mat.PositionWS));

    uvec3 hash = SnowGlintHash(uvec3(ivec3(floor(mat.PositionWS))));
    uvec3 hashNext = SnowGlintHash(hash);
    vec3 diskOffset = vec3(hashNext & 65535u) * SNOW_GLINT_HASH_SCALE;

    float radius = 0.1 + 0.15 * float(hash.y >> 16u) * SNOW_GLINT_HASH_SCALE;
    float edge = min(radius, footprint);
    float distanceToDisk = length(fract(mat.PositionWS) - (vec3(0.25) + diskOffset * 0.5));
    float cell = (1.0 - smoothstep(edge - footprint, edge + footprint, distanceToDisk)) * min(4.0, (radius * radius) / (edge * edge));

    if (cell <= 0.0)
    {
        return;
    }

    // Each disk twinkles on its own phase
    float time = g_flTime * g_flSnowGlintTwinkleSpeed * (0.75 + 0.5 * diskOffset.x);
    float phase = float(hash.z >> 16u) * SNOW_GLINT_HASH_SCALE;
    vec2 twinkle = vec2(sin(time + phase * TAU), sin(time * 1.37 + phase * 2.1 * TAU));

    vec3 jitter = vec3(hash & 65535u) * SNOW_GLINT_HASH_SCALE;
    vec3 tilt = (jitter * 2.0 - vec3(1.0)) + vec3(twinkle * g_flSnowGlintTwinkleAmount, 0.0);
    vec3 tilted = normalize(mat.Normal + tilt * 0.9);
    vec3 glintNormal = normalize(tilted + mat.Normal * max(0.0, 0.1 - dot(tilted, mat.Normal)));

    float footprintScale = 1.0 / max(footprint, 1e-6);
    float intensity = strength * g_flSnowGlintIntensity * 0.75;
    float facing = saturate(dot(reflect(-mat.ViewDir, glintNormal), mat.Normal) * 4.0);
    float amount = saturate(cell) * saturate(footprintScale) * saturate(intensity) * facing;
    float roughnessRandom = float(hashNext.x >> 16u) * SNOW_GLINT_HASH_SCALE;

    mat.RoughnessTex = mix(mat.RoughnessTex, vec2(mix(0.3, 0.5, roughnessRandom)), amount);
    reflectance = mix(reflectance, min(0.3 * (max(cell, 1.0) * intensity), 0.9), amount);

    float disk = strength * cell * saturate(footprintScale);
    mat.SnowGlint = SnowGlint_t(disk * smoothstep(1.0, 2.0, footprintScale), disk, glintNormal, amount);
}

#if (S_MODE_DEPTH == 0)

float GetSnowGlintAlignment(vec3 glintNormal, vec3 lightDirection, vec3 viewDirection, float coneCosine)
{
    float coneWidth = max(1.0 - coneCosine, 1e-6);
    return saturate((dot(glintNormal, normalize(lightDirection + viewDirection)) - coneCosine) / coneWidth);
}

// The sky glint is lit from straight above by the indirect diffuse light
vec3 GetSnowGlintLighting(MaterialProperties_t mat, vec3 indirectDiffuse)
{
    SnowGlint_t glint = mat.SnowGlint;

    if (glint.SkyMask <= 0.0)
    {
        return vec3(0.0);
    }

    float grazing = 1.0 - saturate(dot(mat.Normal, mat.ViewDir));
    float coneCosine = cos(radians(5.0 * mix(1.0, 3.0, grazing * grazing * grazing * grazing)));

    vec3 sunGlint = vec3(0.0);
    vec3 sunColor = GetSunColor();
    vec3 sunDirection = GetSunDirection();
    float sparkle = smoothstep(0.9, 1.0, GetSnowGlintAlignment(glint.Normal, sunDirection, mat.ViewDir, coneCosine)) * glint.SunMask;

    if (sparkle > 0.0 && sunColor != vec3(0.0))
    {
        float sunVisibility = CalculateSunShadowMapVisibility(mat.PositionWS);
        sunGlint = sunColor * sunColor * (sparkle * 20000.0 * g_flSnowGlintSparkle * sunVisibility * saturate(dot(sunDirection, mat.Normal)));
    }

    float skyAlignment = GetSnowGlintAlignment(glint.Normal, vec3(0.0, 0.0, 1.0), mat.ViewDir, coneCosine);
    vec3 skyGlint = indirectDiffuse * (1.5 * smoothstep(0.0, 0.3, skyAlignment) * glint.SkyMask * g_flSnowGlintIntensity);

    return sunGlint + skyGlint;
}

#endif

#endif

#endif
