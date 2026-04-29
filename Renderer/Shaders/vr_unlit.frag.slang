#version 460

#include "common/utils.slang"
#include "common/features.slang"
#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"

#define F_UNLIT 0
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0

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


#if (translucent)
    uniform float g_flOpacityScale = 1.0;
#endif


uniform sampler2D g_tNormal;
uniform sampler2D g_tTintMask;


#if (translucent || alphatest)
    uniform sampler2D g_tColor; // SrgbRead(true)
#else
    uniform sampler2D g_tColor2; // SrgbRead(true)
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
    #else
        vec4 color = texture(g_tColor2, texCoord);
    #endif

    vec4 normalTexture = texture(g_tNormal, texCoord);

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

    return mat;
}

void main()
{
    vec3 vertexNormal = vNormalOut;
    vec2 texCoord = vTexCoordOut;

    MaterialProperties_t mat = GetMaterial(texCoord, vertexNormal);
    outputColor.a = mat.Opacity;

    LightingTerms_t lighting = InitLighting();

    lighting.DiffuseDirect = mat.Albedo;
    outputColor.rgb = lighting.DiffuseDirect;

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
}
