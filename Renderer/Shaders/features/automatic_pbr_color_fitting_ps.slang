#version 460

#ifndef VRF_AUTOMATIC_PBR_COLOR_FITTING_INCLUDE
#define VRF_AUTOMATIC_PBR_COLOR_FITTING_INCLUDE

// Albedo levels as (min, curve exponent, max)
const vec3 PbrAlbedoLevels = vec3(0.045, 0.737, 0.85);
const vec3 PbrMetallicAlbedoLevels = vec3(0.45, 0.737, 0.92);
const vec3 PbrDarkMetallicAlbedoLevels = vec3(0.1, 0.737, 0.92);
const float PbrColorBoostRange = 63.0;

// Fits a color into the albedo levels of a physically plausible material.
// The hue comes from hueSource, the brightness from color. Above a boost of 1 the color is
// lifted towards the maximum level by its luma times boostedLumaScale.
vec3 FitAlbedoToLevels(vec3 color, vec3 hueSource, vec3 levels, float minLevel, float boost, float boostedLumaScale)
{
    vec3 hue = normalize(max(vec3(0.0003), hueSource)) * 1.06;
    float luma = GetLuma(saturate(color * boost));

    vec3 lumaPreserved = ((hue * minLevel) * 1.732 / length(hue)) / GetLuma(hue);
    float brightness = saturate(pow(Max3(color) * boost, levels.y));
    vec3 fitted = max(lumaPreserved, hue * mix(levels.x, levels.z, brightness));

    float boostAmount = max(0.0, boost - 1.0) / PbrColorBoostRange;
    vec3 boosted = mix(fitted, min(levels.zzz, fitted + vec3(luma * boostedLumaScale)), boostAmount);

    float darkFade = pow(smoothstep(0.0003, minLevel, luma), 0.5);
    return mix(vec3(minLevel), boosted, darkFade);
}

// Fits a color into the levels of a dielectric, metallic or dark metallic material by its metalness
vec3 FitAlbedoToMaterialLevels(vec3 color, vec3 hueSource, float metalness, float boost)
{
    float luma = GetLuma(saturate(color * boost));

    vec3 metallicLevels = mix(PbrMetallicAlbedoLevels, PbrDarkMetallicAlbedoLevels, smoothstep(0.55, 0.0156, luma));
    vec3 levels = mix(PbrAlbedoLevels, metallicLevels, metalness);
    float minLevel = mix(PbrAlbedoLevels.x, PbrDarkMetallicAlbedoLevels.x, metalness);

    return FitAlbedoToLevels(color, hueSource, levels, minLevel, boost, 1.0);
}

#endif
