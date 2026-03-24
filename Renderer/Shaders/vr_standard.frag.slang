#version 460

#include "common/utils.slang"
#include "common/features.slang"
#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"

#define F_UNLIT 0
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0

#define F_SPECULAR 0
#define F_SPECULAR_CUBE_MAP 0
#define F_HIGH_QUALITY_GLOSS 0
#define F_METALNESS_TEXTURE 0
#define F_BLEND 0
#define F_BLEND_NORMALS 0

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec2 vTexCoordOut;
centroid in vec4 vVertexColorOut;

out vec4 outputColor;

#define unlit (F_UNLIT == 1)
#define alphatest (F_ALPHA_TEST == 1)
#define translucent (F_TRANSLUCENT == 1) || (F_GLASS == 1) || (F_BLEND_MODE > 0 && F_BLEND_MODE != 2)
#define selfillum (F_SELF_ILLUM == 1)

#if (translucent)
    uniform float g_flOpacityScale = 1.0;
#endif


uniform sampler2D g_tNormal;
uniform sampler2D g_tTintMask;

#if (F_HIGH_QUALITY_GLOSS == 1)
    #define ANISO_ROUGHNESS
    uniform sampler2D g_tNormal2;
    uniform sampler2D g_tGloss;
#endif

#if (translucent || alphatest)
    uniform sampler2D g_tColor; // SrgbRead(true)
#elif (F_SPECULAR == 1 && F_METALNESS_TEXTURE == 0)
    uniform sampler2D g_tColor1; // SrgbRead(true)
#else
    uniform sampler2D g_tColor2; // SrgbRead(true)
#endif

#if (F_BLEND > 0)
    in vec4 vColorBlendValues;

    #if (F_SPECULAR == 1)
        uniform sampler2D g_tLayer1Color; // SrgbRead(true)
    #else
        uniform sampler2D g_tLayer1Color2; // SrgbRead(true)
    #endif

    uniform sampler2D g_tLayer1NormalRoughness;
    uniform vec2 g_vTexCoordScale2 = vec2(1.0);

    uniform sampler2D g_tLayer1RevealMask;
    uniform float g_flLayer1BlendSoftness = 0.5;

    #if (F_BLEND_NORMALS == 1)
        uniform sampler2D g_tLayer1Normal;
    #endif

    #if (F_BLEND == 2)
        // g_tLayer2RevealMask
        // g_tLayer2Color
        // g_tLayer2Normal
    #endif
#endif

#if (F_METALNESS_TEXTURE == 1)
    uniform sampler2D g_tMetalnessReflectance;
#else
    uniform float g_flMetalness = 0.0;
#endif

#if (F_SCALE_NORMAL_MAP == 1)
    uniform float g_flNormalMapScaleFactor = 1.0;
#endif

#if (_ambientOcclusionTexture)
    uniform sampler2D g_tAmbientOcclusion;
#endif

#if (selfillum)
    uniform sampler2D g_tSelfIllumMask;
    uniform float g_flSelfIllumAlbedoFactor = 0.0;
    uniform float g_flSelfIllumBrightness = 0.0;
    uniform float g_flSelfIllumScale = 1.0;
    uniform vec2 g_vSelfIllumScrollSpeed = vec2(0.0);
    uniform vec3 g_vSelfIllumTint = vec3(1.0);

    vec3 GetStandardSelfIllumination(float flSelfIllumMask, vec3 vAlbedo)
    {
        vec3 selfIllumScale = (exp2(g_flSelfIllumBrightness) * g_flSelfIllumScale) * g_vSelfIllumTint.rgb;
        return selfIllumScale * flSelfIllumMask * mix(vec3(1.0), vAlbedo, g_flSelfIllumAlbedoFactor);
    }
#endif

#if (F_SPECULAR_CUBE_MAP == 1)
    #define IBL 1
#endif

#include "common/fullbright.slang"
#include "common/texturing.slang"
#include "common/fog.slang"
#include "common/lighting.slang"

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut, vBitangentOut);

    #if (translucent || alphatest)
        vec4 color = texture(g_tColor, texCoord);
    #elif (F_SPECULAR == 1 && F_METALNESS_TEXTURE == 0)
        vec4 color = texture(g_tColor1, texCoord);
    #else
        vec4 color = texture(g_tColor2, texCoord);
    #endif

    vec4 normalTexture = texture(g_tNormal, texCoord);

    #if (F_BLEND > 0)
        vec2 texCoordB = texCoord * g_vTexCoordScale2.xy;

        #if (F_SPECULAR == 1)
            vec4 color2 = texture(g_tLayer1Color, texCoordB);
        #else
            vec4 color2 = texture(g_tLayer1Color2, texCoordB);
        #endif

        vec4 normalTexture2 = normalTexture;

        #if (F_BLEND_NORMALS == 1)
            normalTexture2 = texture(g_tLayer1Normal, texCoordB);
        #endif

        float blendFactor = vColorBlendValues.r;
        vec4 blendModTexel = texture(g_tLayer1RevealMask, texCoordB);

        blendFactor = ApplyBlendModulation(blendFactor, blendModTexel.g, blendModTexel.r * g_flLayer1BlendSoftness);

        color = mix(color, color2, blendFactor);
        normalTexture = mix(normalTexture, normalTexture2, blendFactor);
    #endif

    float flSelfIllumMask = 0.0;

    mat.Albedo = color.rgb;

    #if (translucent) || (alphatest)
        mat.Opacity = color.a;
    #endif

    #if (translucent)
        mat.Opacity *= g_flOpacityScale;
    #endif

    #if (alphatest)
        mat.Opacity = AlphaTestAntiAliasing(mat.Opacity, texCoord);
    #endif

    vec3 tintColor = vVertexColorOut.rgb;

    #if (F_TINT_MASK == 1)
        float tintStrength = texture(g_tTintMask, texCoord).x;
        tintColor = 1.0 - tintStrength * (1.0 - tintColor.rgb);
    #endif

    mat.Albedo *= tintColor;

    #if (selfillum)
        vec2 vSelfIllumMaskCoords = texCoord;
        vSelfIllumMaskCoords += fract(g_vSelfIllumScrollSpeed.xy * g_flTime);
        flSelfIllumMask = texture(g_tSelfIllumMask, vSelfIllumMaskCoords).r;

        mat.IllumColor = GetStandardSelfIllumination(flSelfIllumMask, mat.Albedo);
    #endif

    #if (unlit)
        return mat;
    #endif

    #if (F_HIGH_QUALITY_GLOSS == 1)
        normalTexture = texture(g_tNormal2, texCoord);
    #endif

    mat.NormalMap = DecodeDxt5Normal(normalTexture);

    mat.RoughnessTex.xy = normalTexture.bb;

    vec3 F0 = vec3(0.04);
    // When alphatest/translucent, color.a is opacity (from g_tColor), not reflectance.
    // Reflectance is in g_tColor1.a which is only available in the non-alphatest path.
    #if (F_SPECULAR == 1 && F_METALNESS_TEXTURE == 0 && !(translucent || alphatest))
        F0 = color.aaa;
    #endif

    #if (F_METALNESS_TEXTURE == 1)
        vec3 metalnessReflectance = texture(g_tMetalnessReflectance, texCoord).rgb;
        mat.Metalness = metalnessReflectance.r;
        F0 = metalnessReflectance.ggg;
    #endif

    #if (F_HIGH_QUALITY_GLOSS == 1)
        mat.RoughnessTex.xy = texture(g_tGloss, texCoord).ag;
    #endif

    mat.Normal = calculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    AdjustRoughnessByGeometricNormal(mat);

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;

    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;

    mat.SpecularColor = mix(F0, mat.Albedo, mat.Metalness);

    mat.DiffuseAO = vec3(mat.AmbientOcclusion);
    mat.SpecularAO = mat.AmbientOcclusion;

    #if defined(ANISO_ROUGHNESS)
        CalculateAnisotropicTangents(mat);
    #endif

    return mat;
}

void main()
{
    vec3 vertexNormal = vNormalOut;
    vec2 texCoord = vTexCoordOut;

    MaterialProperties_t mat = GetMaterial(texCoord, vertexNormal);
    outputColor.a = mat.Opacity;

    LightingTerms_t lighting = InitLighting();

#if (unlit)
    lighting.DiffuseDirect = mat.Albedo + mat.IllumColor;
    outputColor.rgb = lighting.DiffuseDirect;
#else

    lighting = CalculateLighting(mat);
    ApplyAmbientOcclusion(lighting, mat);

    vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;
    vec3 specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect;

    vec3 combinedLighting = mat.DiffuseColor * diffuseLighting + specularLighting + lighting.TransmissiveDirect + mat.IllumColor;

    outputColor.rgb = combinedLighting;
#endif

    ApplyFog(outputColor.rgb, mat.PositionWS);

    if (HandleMaterialRenderModes(outputColor, mat)
    || HandleLightingRenderModes(outputColor, mat, lighting)
    || HandleUVRenderModes(outputColor, mat, g_tNormal, vTexCoordOut.xy))
    {
        //
    }
    else if (g_iRenderMode == renderMode_Cubemaps)
    {
        vec3 viewmodeEnvMap = GetEnvironment(mat).rgb;
        outputColor.rgb = SrgbLinearToGamma(viewmodeEnvMap);
    }
    else if (g_iRenderMode == renderMode_Tint)
    {
        outputColor = vec4(vVertexColorOut.rgb, vVertexColorOut.a);
    }
#if (F_BLEND > 0)
    else if (g_iRenderMode == renderMode_TerrainBlend)
    {
        outputColor.rgb = vColorBlendValues.rgb;
    }
#endif
}
