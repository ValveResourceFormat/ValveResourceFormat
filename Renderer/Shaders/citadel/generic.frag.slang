#version 460

#include "generic_features.slang"

#define UNLIT (F_UNLIT == 1)

// Has no specular
#define IBL 0
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

// Unlit and translucent surfaces do not write the G-buffer flag that accepts it
#if (F_UNLIT == 0) && (F_TRANSLUCENT == 0)
    #define SCREEN_SPACE_AO 1
#endif

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)

uniform float g_flOpacityScale = 1.0;
uniform float g_flBumpStrength = 1.0;

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    mat.Albedo = color.rgb * vVertexColorOut.rgb;

    InitOpacity(mat, color.a, g_flOpacityScale, F_TRANSLUCENT == 1, false, texCoord, g_flAlphaTestReference);

#if (UNLIT)
    FinishUnlitMaterial(mat);
    return mat;
#endif

    mat.NormalMap = ScaleNormalMapSlope(DecodeDxt5Normal(normalTexture), g_flBumpStrength);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.RoughnessTex = vec2(1.0);

    FinishLitMaterial(mat, texCoord, vec3(0.04));

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.DirectSpecular = false;

#if (UNLIT)
    RenderUnlitSurface(g_tColor, options);
#else
    RenderSurface(g_tColor, options);
#endif
}
