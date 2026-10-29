#version 460

#include "crystal_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)

uniform float g_flBumpStrength = 1.0;

#if (F_TINT_MASK == 1)
    uniform sampler2D g_tTintMask;
#else
    // Multiplied once more over the tint the vertex shader passes on
    uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)
#endif

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    mat.Albedo = color.rgb;

    InitOpacity(mat, color.a, 1.0, false, false, texCoord, g_flAlphaTestReference);

#if (F_TINT_MASK == 1)
    mat.Albedo *= GetMaskedVertexTint(texture(g_tTintMask, texCoord).y);
#else
    mat.Albedo *= vVertexColorOut.rgb;
    mat.Albedo *= g_vColorTint;
#endif

#if (F_NORMAL_MAP == 1)
    mat.NormalMap = ScaleNormalMapSlope(DecodeDxt5Normal(normalTexture), g_flBumpStrength);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
#else
    mat.Normal = mat.GeometricNormal;
#endif

    mat.RoughnessTex = normalTexture.bb;

    FinishLitMaterial(mat, texCoord, vec3(0.04));

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    RenderSurface(g_tColor, options);
}
