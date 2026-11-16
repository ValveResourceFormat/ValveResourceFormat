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
#define renderMode_Tint 0

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_FULLBRIGHT 0
#define F_SOLID_COLOR 0
#define F_NORMAL_MAP 0
#define F_SPECULAR 0
#define F_MODULATE_SPECULAR_BY_ALPHA 0
#define F_TINT_MASK 0
#define F_TINT_MASK_2 0
#define F_PAINT_VERTEX_COLORS 0
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0
#define F_ADDITIVE_BLEND 0
#define F_ALPHA_ANGLE_POWER 0
#define F_MOD_2X 0
#define F_ALLOW_LIGHTING_ON_TRANSLUCENT 0
#define F_SCROLL_UV 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec4 vTangentOut;
in vec2 vTexCoordOut;
#if (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vVertexColorOut;
#endif
flat in vec4 vTintColorFadeOut;

out vec4 outputColor;

#if (F_SOLID_COLOR == 0)
    uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture
#endif

#if (F_TINT_MASK == 1)
    uniform sampler2D g_tTintMask;
#endif

#if (F_SPECULAR == 1)
    uniform sampler2D g_tSpecular; // SrgbRead(true) Reflectance, SelfIllum, Bloom
    uniform float g_flSpecularIntensity = 1.0;
#endif

#if (F_NORMAL_MAP == 1)
    uniform sampler2D g_tNormal;
    uniform float g_flBumpStrength = 1.0;
#endif

#if (F_SCROLL_UV == 1 || F_SCROLL_UV == 2)
    uniform sampler2D g_tScrollSpeed;
    uniform float g_flScrollUvSpeed;
    uniform vec2 g_vScrollUvDirection;
#endif

#include "../common/utils.slang"
#include "../common/view_constants.slang"

#include "../common/lighting_constants.slang"
#include "../common/lighting_common.slang"
#include "../common/shadowmapping.slang"
#include "../common/fog.slang"
#include "global_light.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)
uniform float g_flOpacityScale = 1.0;

#if (F_TINT_MASK == 1)
    uniform vec3 g_vColorTint2 = vec3(1.0); // SrgbRead(true)
#endif

#if (F_TINT_MASK_2 == 1)
    uniform vec3 g_vColorTint3 = vec3(1.0); // SrgbRead(true)
    uniform vec3 g_vColorTint4 = vec3(1.0); // SrgbRead(true)
#endif

#if (F_ADDITIVE_BLEND == 1)
    uniform float g_flOverbrightFactor = 1.0;
#endif

#if (F_ALPHA_ANGLE_POWER == 1)
    uniform float g_flAlphaAnglePower = 1.0;
#endif

uniform float g_flAlphaTestReference = 0.7;

struct TexCoords_t
{
    vec2 Color;
    vec2 All;
    float ScrollAlpha;
};

TexCoords_t GetTexCoords()
{
    TexCoords_t coords;
    coords.Color = vTexCoordOut;
    coords.All = vTexCoordOut;
    coords.ScrollAlpha = 1.0;

#if (F_SCROLL_UV == 1 || F_SCROLL_UV == 2)
    vec4 scrollTexel = texture(g_tScrollSpeed, coords.All);
    coords.ScrollAlpha = scrollTexel.a;
    coords.Color = coords.Color + g_vScrollUvDirection.xy * g_flTime * g_flScrollUvSpeed * scrollTexel.g;
#endif

#if (F_SCROLL_UV == 2)
    coords.All = coords.Color;
#endif

    return coords;
}

vec4 SampleColor(TexCoords_t coords)
{
#if (F_SOLID_COLOR == 0)
    vec4 color = texture(g_tColor, coords.Color);

    #if (F_TRANSLUCENT == 1 && (F_SCROLL_UV == 1 || F_SCROLL_UV == 2))
        color.a = coords.ScrollAlpha;
    #endif

    return color;
#else
    return vec4(1.0);
#endif
}

vec3 SampleNormalTs(vec2 coords)
{
#if (F_NORMAL_MAP == 1)
    vec3 normalTs = DecodeDxt5Normal(texture(g_tNormal, coords));
    normalTs.xy *= g_flBumpStrength;
    return normalize(normalTs);
#else
    return vec3(0.0, 0.0, 1.0);
#endif
}

vec3 GetGeometricNormal()
{
    vec3 normal = normalize(vNormalOut);

    if (F_RENDER_BACKFACES == 1 && !gl_FrontFacing)
    {
        normal = -normal;
    }

    return normal;
}

//Calculate the normal of this fragment in world space
vec3 CalculateWorldNormal(vec3 vNormalTs, vec3 geometricNormal)
{
#if (F_NORMAL_MAP == 1)
    vec3 tangent = vTangentOut.xyz;
    vec3 bitangent = GetBitangent(vNormalOut, vTangentOut);

    mat3 tangentSpace = mat3(tangent, bitangent, geometricNormal);

    return normalize(tangentSpace * vNormalTs);
#else
    return geometricNormal;
#endif
}

vec3 GetIllumination(vec3 worldNormal, out float sunLit)
{
    sunLit = 0.0;

    if (g_iRenderMode == renderMode_FullBright)
    {
        return vec3(1.0);
    }

#if F_FULLBRIGHT == 1 || (F_TRANSLUCENT == 1 && F_ALLOW_LIGHTING_ON_TRANSLUCENT == 0)
    return vec3(1.0);
#else
    return CalculateDotaGlobalLight(worldNormal, vFragPosition, sunLit);
#endif
}

vec3 GetTintFactor(vec2 coords)
{
    vec3 tintFactor = g_vColorTint * vTintColorFadeOut.rgb;

#if (F_TINT_MASK == 1)
    vec3 tintMask = texture(g_tTintMask, coords).xyz;
    tintFactor = mix(g_vColorTint2, tintFactor, tintMask.y);

    #if (F_TINT_MASK_2 == 1)
        tintFactor = ApplyTintMask2(tintFactor, tintMask.z, g_vColorTint3, g_vColorTint4);
    #endif
#endif

    return tintFactor;
}

float GetOutputAlpha(float colorAlpha, vec3 geometricNormal)
{
#if (F_MOD_2X == 1)
    return colorAlpha * vTintColorFadeOut.a;
#elif (F_TRANSLUCENT == 1)
    // The scrolled alpha is the alpha texel, so only the other paths apply the instance alpha twice
    #if (F_SCROLL_UV == 1 || F_SCROLL_UV == 2)
        float alpha = vTintColorFadeOut.a * colorAlpha * g_flOpacityScale;
    #else
        float instanceAlphaSquared = vTintColorFadeOut.a * vTintColorFadeOut.a;
        float alpha = instanceAlphaSquared * colorAlpha * g_flOpacityScale;
    #endif

    #if (F_ALPHA_ANGLE_POWER == 1)
        vec3 viewDir = normalize(g_vCameraPositionWs - vFragPosition);
        alpha *= pow(abs(dot(viewDir, geometricNormal)), g_flAlphaAnglePower);
    #endif

    #if (F_ADDITIVE_BLEND == 1)
        alpha *= GetFogAttenuation(vFragPosition);
    #endif

    return alpha;
#else
    return colorAlpha;
#endif
}

vec3 SampleSpecularTexel(vec2 coords)
{
#if (F_SPECULAR == 1)
    return texture(g_tSpecular, coords).xyz;
#else
    return vec3(0.0);
#endif
}

vec3 ShadeAlbedo(vec3 albedo, vec3 illumination, float sunLit, vec3 specularTexel, vec3 worldNormal)
{
#if (F_SPECULAR == 1)
    float reflectance = specularTexel.x * g_flSpecularIntensity;
    #if (F_MODULATE_SPECULAR_BY_ALPHA == 1)
        reflectance *= vTintColorFadeOut.a;
    #endif

    float specular = CalculateDotaSpecular(worldNormal, vFragPosition) * reflectance;
    vec3 specularAlbedo = albedo + specular * g_vSpecularAlbedoColor.rgb;
    vec3 litColor = illumination * specularAlbedo + specular * g_vSpecularLitColor.rgb * sunLit;

    // Self illumination shows the unlit albedo
    return mix(litColor, specularAlbedo, specularTexel.y);
#else
    return illumination * albedo;
#endif
}

#if (F_MOD_2X == 1)
// The surface blends around a gray of 0.5, with the specular fading to gray by alpha
vec4 ShadeMod2x(vec3 albedo, float alpha, vec3 illumination, vec3 worldNormal)
{
    float luminance = dot(albedo, vec3(0.2989, 0.587, 0.114));
    vec3 baseColor = mix(vec3(0.5), albedo, alpha);
    float specular = CalculateDotaSpecular(worldNormal, vFragPosition) * mix(0.5, luminance, alpha);

    vec3 litColor = illumination * (baseColor + specular * g_vSpecularAlbedoColor.rgb) + specular * g_vSpecularLitColor.rgb;
    return vec4(litColor, 1.0);
}
#endif

vec4 ApplyRenderMode(vec4 litColor, vec4 color, vec3 specularTexel, vec3 normalTs, vec3 worldNormal, vec3 illumination)
{
    if (g_iRenderMode == renderMode_Color)
    {
        return vec4(color.rgb, 1.0);
    }

#if (F_SPECULAR == 1)
    if (g_iRenderMode == renderMode_Roughness)
    {
        litColor.rgb = pow2(1 - specularTexel.xxx);
        return litColor;
    }
#endif

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

    if (g_iRenderMode == renderMode_Illumination)
    {
        return vec4(SrgbLinearToGamma(illumination), 1.0);
    }

#if F_NORMAL_MAP == 1
    if (g_iRenderMode == renderMode_BumpMap)
    {
        litColor.rgb = PackToColor(normalTs);
        return litColor;
    }
#endif

#if F_PAINT_VERTEX_COLORS == 1
    if (g_iRenderMode == renderMode_Tint)
    {
        return vVertexColorOut;
    }
#endif

    return litColor;
}

void main()
{
    TexCoords_t coords = GetTexCoords();

    vec4 color = SampleColor(coords);
    vec3 normalTs = SampleNormalTs(coords.All);
    vec3 geometricNormal = GetGeometricNormal();
    vec3 worldNormal = CalculateWorldNormal(normalTs, geometricNormal);

    ApplyAlphaTest(color.a, g_flAlphaTestReference);

    float sunLit;
    vec3 illumination = GetIllumination(worldNormal, sunLit);

    vec3 albedo = color.rgb * GetTintFactor(coords.All);
    float alpha = GetOutputAlpha(color.a, geometricNormal);

#if (F_MOD_2X == 1)
    outputColor = ShadeMod2x(albedo, alpha, illumination, worldNormal);
    ApplyFogMod2x(outputColor, vFragPosition);
#else
    vec3 specularTexel = SampleSpecularTexel(coords.All);
    vec3 litColor = ShadeAlbedo(albedo, illumination, sunLit, specularTexel, worldNormal);

    #if (F_ADDITIVE_BLEND == 1)
        litColor *= g_flOverbrightFactor;
    #endif

    outputColor = ApplyRenderMode(vec4(litColor, alpha), color, specularTexel, normalTs, worldNormal, illumination);

    #if (F_ADDITIVE_BLEND == 0)
        ApplyFog(outputColor.rgb, vFragPosition);
    #endif
#endif
}
