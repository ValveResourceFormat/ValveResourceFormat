#version 460

#include "unlit_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture

uniform float g_flOpacityScale = 1.0;

uniform float g_flAlphaTestReference = 0.5;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord);

    mat.Albedo = color.rgb;

    float alpha = color.a * vVertexColorOut.a;
    InitOpacity(mat, alpha, g_flOpacityScale, F_TRANSLUCENT == 1, false, texCoord, g_flAlphaTestReference);

    mat.Albedo *= vVertexColorOut.rgb;

    FinishUnlitMaterial(mat);
    return mat;
}

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.OccludeDirectDiffuse = false;
    RenderUnlitSurface(g_tColor, options);
}
