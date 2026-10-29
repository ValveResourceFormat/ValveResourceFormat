#version 460

#include "vr_eyeball_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tNormal; // Sampler(UserConfig)

#if (F_SELF_ILLUM == 1)
    #include "../features/self_illum_ps.slang"
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

    mat.Albedo *= vVertexColorOut.rgb;

#if (F_SELF_ILLUM == 1)
    mat.IllumColor = SampleSelfIllumination(texCoord, mat.Albedo);
#endif

    mat.NormalMap = ScaleNormalMapSlope(DecodeDxt5Normal(normalTexture), 1.0);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.RoughnessTex = normalTexture.bb;

    FinishLitMaterial(mat, texCoord, vec3(0.04));

    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    RenderSurface(g_tColor, options);
}
