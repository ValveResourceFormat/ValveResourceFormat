#version 460

#include "../hlvr/vr_simple_2way_blend_features.slang"

#define LIGHTMAP_VR_SHADING 1

#include "../common/surface_blend_2way.slang"

uniform float g_flBumpStrength = 1.0;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    return GetTwoWayBlendMaterial(texCoord, vertexNormals, g_flBumpStrength);
}

uniform float g_BlendFalloff = 0.0;
uniform float g_BlendHeight = 0.0;

float GetBlendFactor(Layer2_t layer2)
{
    // The height is the blend amount, compared against the mask
    float blendMask = texture(g_tMask, layer2.TexCoord).x;
    return ApplyBlendModulation(g_BlendHeight, blendMask, g_BlendFalloff);
}
