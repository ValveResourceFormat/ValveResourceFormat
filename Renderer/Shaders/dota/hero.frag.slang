#version 460

// Render modes -- Switched on/off by code
#define renderMode_FullBright 0
#define renderMode_Color 0
#define renderMode_Roughness 0
#define renderMode_Normals 0
#define renderMode_Tangents 0
#define renderMode_BumpMap 0
#define renderMode_BumpNormals 0
#define renderMode_Illumination 0
#define renderMode_Mask1 0
#define renderMode_Mask2 0
#define renderMode_Metalness 0
#define renderMode_Specular 0
#define renderMode_RimLight 0

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_MASKS_1 0
#define F_MASKS_2 0
#define F_ALPHA_TEST 0
#define F_SPECULAR_CUBE_MAP 0
#define F_MASK_CUBE_MAP_BY_METALNESS 0
#define F_MASK_CUBE_MAP_BY_SEPARATE_MASK 0
#define F_SPECULAR_WARP 0
#define F_FRESNEL_COLOR_WARP 0
#define F_DIFFUSE_WARP 0
#define F_DETAIL 0
#define F_TINT_SELF_ILUM 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec4 vTangentOut;
in vec2 vTexCoordOut;
#if F_DETAIL
in vec2 vDetailTexCoordOut;
#endif
flat in vec4 vTintColorFadeOut;

out vec4 outputColor;

uniform float g_flAlphaTestReference;
uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture
uniform sampler2D g_tNormal;
uniform sampler2D g_tMasks1;
uniform sampler2D g_tMasks2;
uniform sampler2D g_tFresnelWarp;
#if (F_DIFFUSE_WARP == 1)
uniform sampler2D g_tDiffuseWarp;
#endif
#if F_DETAIL
uniform sampler2D g_tDetail; // SrgbRead(true)
#endif
#if F_SPECULAR_WARP
uniform sampler2D g_tSpecularWarp;
#endif
#if F_SPECULAR_CUBE_MAP
uniform samplerCube g_tCubeMap; // SrgbRead(true)
#endif
#if F_MASK_CUBE_MAP_BY_SEPARATE_MASK
uniform sampler2D g_tCubeMapSeparateMask;
#endif
#if F_FRESNEL_COLOR_WARP
uniform sampler3D g_tFresnelColorWarp3D; // SrgbRead(true)
#endif

#include "../common/utils.slang"
#include "../common/view_constants.slang"

// Material properties
uniform float g_flSpecularExponent = 32.0;
uniform float g_flSpecularScale = 1.0;
uniform vec3 g_vSpecularColor = vec3(1.0); // SrgbRead(true)
uniform float g_flAmbientScale = 1.0;
uniform float g_flIlluminationScale = 1.0;
uniform float g_flWorldIlluminationScale = 1.0;
uniform vec3 g_vRimLightColor = vec3(1.0); // SrgbRead(true)
uniform float g_flRimLightScale = 1.0;
uniform vec3 g_vDetail1ColorTint = vec3(1.0); // SrgbRead(true)
uniform float g_flDetailBlendFactor = 1.0;
uniform float g_flCubeMapScalar = 1.0;
uniform bool g_bDiffuseWarpMaskSelectsWarpTopOrBottom = true;
uniform bool g_bDiffuseWarpApplyShadowsAfterWarp = false;

// Blend values that override the mask channels
uniform float g_flDetailBlendToFull;
uniform float g_flDiffuseWarpBlendToFull;
uniform float g_flEnvMapBlendToFull;
uniform float g_flMetalnessBlendToFull;
uniform float g_flReflectionsTintByBaseBlendToNone;
uniform float g_flSelfIllumBlendToFull;
uniform float g_flSpecularBlendToFull;
uniform float g_flSpecularExponentBlendToFull;

#include "../common/lighting_constants.slang"
#include "../common/lighting_common.slang"
#include "../common/shadowmapping.slang"
#include "../common/fog.slang"
#include "global_light.slang"

vec4 SampleMask1()
{
#if F_MASKS_1
    return texture(g_tMasks1, vTexCoordOut);
#else
    return vec4(0.0);
#endif
}

vec4 SampleMask2()
{
#if F_MASKS_2
    return texture(g_tMasks2, vTexCoordOut);
#else
    return vec4(0.0);
#endif
}

// Fresnel warp at the view angle: rim in R, color warp in G, specular in B
vec3 SampleFresnelWarp(vec3 worldNormal, vec3 viewDirection)
{
    return texture(g_tFresnelWarp, vec2(saturate(dot(viewDirection, worldNormal)), 0.5)).rgb;
}

// The fresnel warp color channel and mask 1 channel G blend the color toward its 3D warp lookup
vec3 ApplyFresnelColorWarp(vec3 color, vec4 mask1, vec3 fresnelWarp)
{
#if F_FRESNEL_COLOR_WARP
    vec3 warped = texture(g_tFresnelColorWarp3D, pow(color, vec3(0.4545))).rgb;

    return mix(color, warped, fresnelWarp.g * mask1.g);
#else
    return color;
#endif
}

// The sun lights half-lambert, so surfaces facing away from it keep half of its light
vec3 GetSunDiffuse(vec3 worldNormal, vec3 lightDirection, float sunShadow, vec4 mask1)
{
    float halfLambert = dot(worldNormal, lightDirection) * 0.5 + 0.5;
    float shadowBeforeWarp = g_bDiffuseWarpApplyShadowsAfterWarp ? 1.0 : sunShadow;
    float diffuseX = saturate(halfLambert * shadowBeforeWarp);

#if (F_DIFFUSE_WARP == 1)
    // Mask 1 channel G picks the row of the warp
    float warpRow = saturate(max(mask1.g, g_flDiffuseWarpBlendToFull));
    vec3 warp = texture(g_tDiffuseWarp, vec2(diffuseX, warpRow)).rgb;
    float warpWeight = g_bDiffuseWarpMaskSelectsWarpTopOrBottom ? 1.0 : warpRow;
    vec3 diffuse = mix(vec3(diffuseX), warp, warpWeight);

    if (g_bDiffuseWarpApplyShadowsAfterWarp)
    {
        diffuse *= sunShadow;
    }

    return diffuse;
#else
    return vec3(diffuseX);
#endif
}

// The light that falls on the albedo: the sun, the directional ambient and the sky ambient where the sun is shadowed
vec3 GetIllumination(vec3 worldNormal, vec3 lightDirection, float sunShadow, vec4 mask1)
{
    if (g_iRenderMode == renderMode_FullBright)
    {
        return vec3(1.0);
    }

    vec3 sun = GetSunColor() * GetSunDiffuse(worldNormal, lightDirection, sunShadow, mask1);
    vec3 directionalAmbient = CalculateDotaDirectionalAmbient(worldNormal);
    vec3 shadowFill = GetSkyAmbientLighting(worldNormal) * (1.0 - sunShadow);

    return sun + (directionalAmbient + shadowFill) * g_flAmbientScale;
}

//Self illumination - mask 1 channel A, plus the detail alpha
float GetSelfIllumination(vec4 mask1, float detailSelfIllumination)
{
    return saturate(max(mask1.a, g_flSelfIllumBlendToFull) + detailSelfIllumination);
}

// The environment cube map along the view reflection, scaled by g_flCubeMapScalar
vec3 GetCubeMapReflection(vec3 worldNormal, vec3 viewDirection)
{
#if F_SPECULAR_CUBE_MAP
    vec3 reflection = texture(g_tCubeMap, reflect(-viewDirection, worldNormal)).rgb * g_flCubeMapScalar;

#if F_MASK_CUBE_MAP_BY_SEPARATE_MASK
    reflection *= texture(g_tCubeMapSeparateMask, vTexCoordOut).g;
#endif

    return reflection;
#else
    return vec3(0.0);
#endif
}

// The sun specular lobe, with the exponent from mask 2 channel A
vec3 GetSunSpecular(vec4 mask2, vec3 worldNormal, vec3 lightDirection, vec3 viewDirection)
{
    vec3 reflected = reflect(-viewDirection, worldNormal);
    float specularAngle = saturate(dot(lightDirection, reflected));
    float exponentScale = max(mask2.a, g_flSpecularExponentBlendToFull);

#if F_SPECULAR_WARP
    // The warp replaces the Phong power: angle across, exponent mask down
    return texture(g_tSpecularWarp, vec2(specularAngle, exponentScale)).rgb;
#else
    float exponent = exponentScale * g_flSpecularExponent;
    float specular = saturate(dot(worldNormal, lightDirection)) * pow(max(0.001, specularAngle), exponent);

    return vec3(specular);
#endif
}

// baseColor is the color before detail and color warp
vec3 GetSpecularLight(vec3 baseColor, vec4 mask2, float metalness, vec3 fresnelWarp, vec3 cubeMapSpecular, vec3 worldNormal, vec3 lightDirection, vec3 viewDirection)
{
    vec3 sunSpecular = GetSunColor() * GetSunSpecular(mask2, worldNormal, lightDirection, viewDirection) * g_flSpecularScale;
    vec3 specular = sunSpecular + cubeMapSpecular;

    //Multiply by mapped specular intensity - mask 2 channel R
    specular *= max(mask2.r, g_flSpecularBlendToFull);

    //Specular tint map - mask 2 channel B, from the base color to the specular color
    float tintBlend = max(mask2.b, g_flReflectionsTintByBaseBlendToNone);
    vec3 specularColor = mix(baseColor, g_vSpecularColor, tintBlend);

    return specular * specularColor * max(fresnelWarp.b, metalness);
}

// The sun color facing up, masked by mask 2 channel G and shaped by the rim of the fresnel warp
vec3 GetRimLight(vec4 mask2, vec3 fresnelWarp, vec3 worldNormal)
{
    float rimMask = max(mask2.g, g_flEnvMapBlendToFull);

    return GetSunColor() * g_vRimLightColor * (g_flRimLightScale * rimMask * max(0.0, worldNormal.z) * fresnelWarp.r);
}

vec4 ApplyRenderMode(vec4 litColor, vec4 color, vec4 mask1, vec4 mask2, vec3 normalTs, vec3 worldNormal, vec3 illumination, float metalness, vec3 specularLight, vec3 rimLight)
{
    if (g_iRenderMode == renderMode_Color)
    {
        return vec4(SrgbLinearToGamma(color.rgb), 1.0);
    }

    if (g_iRenderMode == renderMode_Illumination)
    {
        return vec4(SrgbLinearToGamma(illumination), 1.0);
    }

    if (g_iRenderMode == renderMode_BumpMap)
    {
        return vec4(PackToColor(normalTs), 1.0);
    }

    if (g_iRenderMode == renderMode_Tangents)
    {
        return vec4(PackToColor(vTangentOut.xyz), 1.0);
    }

    if (g_iRenderMode == renderMode_Normals)
    {
        return vec4(PackToColor(vNormalOut), 1.0);
    }

    if (g_iRenderMode == renderMode_BumpNormals)
    {
        return vec4(PackToColor(worldNormal), 1.0);
    }

    if (g_iRenderMode == renderMode_Metalness)
    {
        return vec4(vec3(metalness), 1.0);
    }

    if (g_iRenderMode == renderMode_Specular)
    {
        return vec4(specularLight, 1.0);
    }

    if (g_iRenderMode == renderMode_RimLight)
    {
        return vec4(SrgbLinearToGamma(rimLight), 1.0);
    }

#if F_MASKS_1
    if (g_iRenderMode == renderMode_Mask1)
    {
        return vec4(mask1.rgb, 1.0);
    }
#endif

#if F_MASKS_2
    if (g_iRenderMode == renderMode_Mask2)
    {
        return vec4(mask2.rgb, 1.0);
    }
#endif

    return litColor;
}

void main()
{
    vec3 viewDirection = normalize(g_vCameraPositionWs - vFragPosition);

    vec4 color = texture(g_tColor, vTexCoordOut);
    ApplyAlphaTest(color.a, g_flAlphaTestReference);

    vec4 mask1 = SampleMask1();
    vec4 mask2 = SampleMask2();

    vec3 baseColor = color.rgb;
    float detailSelfIllumination = 0.0;

    // The detail texture is weighted by mask 1 channel R: added to the base color, or doubled and multiplied (F_DETAIL 3).
    // F_DETAIL 2 also adds its alpha to the self illumination.
#if F_DETAIL
    vec4 detail = texture(g_tDetail, vDetailTexCoordOut);
    vec3 detailColor = detail.rgb * g_vDetail1ColorTint;
    float detailWeight = max(mask1.r, g_flDetailBlendToFull) * g_flDetailBlendFactor;

#if F_DETAIL == 3
    color.rgb *= mix(vec3(1.0), detailColor * 2.0, detailWeight);
#elif F_DETAIL == 2
    detailSelfIllumination = detail.a * detailWeight;
    color.rgb += detailColor * detailWeight;
#else
    color.rgb += detailColor * detailWeight;
#endif
#endif

    vec3 normalTs = DecodeDxt5Normal(texture(g_tNormal, vTexCoordOut));
    vec3 worldNormal = CalculateNormalizedWorldNormal(normalTs);

    vec3 lightDirection = GetSunDirection();
    float sunShadow = CalculateSunShadowMapVisibility(vFragPosition);
    vec3 illumination = GetIllumination(worldNormal, lightDirection, sunShadow, mask1);
    vec3 fresnelWarp = SampleFresnelWarp(worldNormal, viewDirection);

    color.rgb = ApplyFresnelColorWarp(color.rgb, mask1, fresnelWarp);

    //Metalness - mask 1 channel B
    float metalness = max(mask1.b, g_flMetalnessBlendToFull);
    vec3 cubeMapReflection = GetCubeMapReflection(worldNormal, viewDirection);

#if F_MASK_CUBE_MAP_BY_METALNESS
    vec3 cubeMapSpecular = vec3(0.0);
    vec3 cubeMapMetal = cubeMapReflection;
#else
    vec3 cubeMapSpecular = cubeMapReflection;
    vec3 cubeMapMetal = vec3(0.0);
#endif

    vec3 specularLight = GetSpecularLight(baseColor, mask2, metalness, fresnelWarp, cubeMapSpecular, worldNormal, lightDirection, viewDirection);
    vec3 rimLight = GetRimLight(mask2, fresnelWarp, worldNormal);

    // Metals show only the specular, and self illuminated texels show the base color unlit
    vec3 albedo = color.rgb * vTintColorFadeOut.rgb;
    vec3 metalColor = specularLight + cubeMapMetal;
    vec3 finalColor = mix(illumination * albedo + specularLight, metalColor, metalness);
    finalColor *= g_flIlluminationScale * g_flWorldIlluminationScale;
    finalColor += rimLight;

#if F_TINT_SELF_ILUM
    vec3 selfIllumColor = albedo;
#else
    vec3 selfIllumColor = color.rgb;
#endif
    finalColor = mix(finalColor, selfIllumColor, GetSelfIllumination(mask1, detailSelfIllumination));

    vec4 litColor = vec4(finalColor, color.a * vTintColorFadeOut.a);

    outputColor = ApplyRenderMode(litColor, color, mask1, mask2, normalTs, worldNormal, illumination, metalness, specularLight, rimLight);
    ApplyFog(outputColor.rgb, vFragPosition);
}
