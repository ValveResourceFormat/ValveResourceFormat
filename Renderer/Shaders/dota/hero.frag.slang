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
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec4 vTangentOut;
in vec2 vTexCoordOut;
flat in vec4 vTintColorFadeOut;

out vec4 outputColor;

uniform float g_flAlphaTestReference;
uniform sampler2D g_tColor; // RepresentativeTexture
uniform sampler2D g_tNormal;
uniform sampler2D g_tMasks1;
uniform sampler2D g_tMasks2;

#include "../common/utils.slang"
#include "../common/view_constants.slang"

// Material properties
uniform float g_flSpecularExponent = 32.0;
uniform float g_flSpecularScale = 1.0;
uniform vec3 g_vSpecularColor = vec3(1.0); // SrgbRead(true)
uniform float g_flAmbientScale = 1.0;

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

// The sun lights half-lambert, so surfaces facing away from it keep half of its light
vec3 GetIllumination(vec3 worldNormal, vec3 lightDirection)
{
    if (g_iRenderMode == renderMode_FullBright)
    {
        return vec3(1.0);
    }

    float halfLambert = dot(worldNormal, lightDirection) * 0.5 + 0.5;
    float shadow = CalculateSunShadowMapVisibility(vFragPosition);

    vec3 sun = GetSunColor() * (halfLambert * shadow);
    vec3 ambient = CalculateDotaDirectionalAmbient(worldNormal) * g_flAmbientScale;

    return sun + ambient;
}

//Self illumination - mask 1 channel A
float GetSelfIllumination(vec4 mask1)
{
#if F_MASKS_1
    return saturate(mask1.a);
#else
    return 0.0;
#endif
}

//Get metalness for future use - mask 1 channel B
float GetMetalness(vec4 mask1)
{
#if F_MASKS_1
    return mask1.b;
#else
    return 0.0;
#endif
}

vec3 GetSpecularLight(vec4 color, vec4 mask2, vec3 worldNormal, vec3 lightDirection, vec3 viewDirection)
{
#if F_MASKS_2 && !F_SPECULAR_CUBE_MAP
    //Phong specular of the sun, with the exponent from mask 2 channel A
    vec3 reflected = reflect(-viewDirection, worldNormal);
    float specularAngle = max(0.001, saturate(dot(lightDirection, reflected)));
    float specular = saturate(dot(worldNormal, lightDirection)) * pow(specularAngle, mask2.a * g_flSpecularExponent);

    //Multiply by mapped specular intensity - mask 2 channel R
    specular *= g_flSpecularScale * mask2.r;

    //Specular tint map - mask 2 channel B, from the base color to the specular color
    vec3 specularColor = mix(color.rgb, g_vSpecularColor, mask2.b);

    // TODO: the game also scales this by the blue channel of g_tFresnelWarp at the view angle
    return GetSunColor() * specularColor * specular;
#else
    return vec3(0.0);
#endif
}

// TODO: the game's rim is a global light color times g_vRimLightColor and g_flRimLightScale, masked by mask 2
// channel G, facing up (max(0, normal.z)) and scaled by the red channel of g_tFresnelWarp at the view angle.
float GetRimLight(vec4 mask2, vec3 worldNormal, vec3 viewDirection)
{
    float rimLight = 1.0 - abs(dot(worldNormal, viewDirection));
    rimLight = pow2(rimLight);

#if F_MASKS_2
    //Multiply the rim light by the rim light intensity - mask 2 channel G
    rimLight = rimLight * mask2.g;
#endif

    return rimLight;
}

vec4 ApplyRenderMode(vec4 litColor, vec4 color, vec4 mask1, vec4 mask2, vec3 normalTs, vec3 worldNormal, vec3 illumination, float metalness, vec3 specularLight, float rimLight)
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
        return vec4(color.rgb * rimLight, 1.0);
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

    vec3 normalTs = DecodeDxt5Normal(texture(g_tNormal, vTexCoordOut));
    vec3 worldNormal = CalculateNormalizedWorldNormal(normalTs);

    vec3 lightDirection = GetSunDirection();
    vec3 illumination = GetIllumination(worldNormal, lightDirection);

    float metalness = GetMetalness(mask1);
    vec3 specularLight = GetSpecularLight(color, mask2, worldNormal, lightDirection, viewDirection);
    float rimLight = GetRimLight(mask2, worldNormal, viewDirection);

    // Metals reflect the sun only, and self illuminated texels show the tinted base color unlit
    vec3 albedo = color.rgb * vTintColorFadeOut.rgb;
    vec3 finalColor = illumination * albedo + specularLight;
    finalColor = mix(finalColor, specularLight, metalness);
    finalColor += color.rgb * rimLight;
    finalColor = mix(finalColor, albedo, GetSelfIllumination(mask1));

    vec4 litColor = vec4(finalColor, color.a * vTintColorFadeOut.a);

    outputColor = ApplyRenderMode(litColor, color, mask1, mask2, normalTs, worldNormal, illumination, metalness, specularLight, rimLight);
    ApplyFog(outputColor.rgb, vFragPosition);
}
