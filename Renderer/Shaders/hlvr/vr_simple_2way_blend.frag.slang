#version 460

#include "vr_simple_2way_blend_features.slang"

#define LIGHTMAP_VR_SHADING 1
#define DYNAMIC_LIGHT_ROUGHNESS_SHAPED_DIFFUSE 1

#include "../common/surface_blend_2way.slang"

// A Desk Job parameter, Half-Life: Alyx materials do not set it
uniform float g_flBumpStrength = 1.0;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    return GetTwoWayBlendMaterial(texCoord, vertexNormals, g_flBumpStrength);
}

in vec4 vColorBlendValues;

float GetBlendFactor(Layer2_t layer2)
{
    float blendMask = texture(g_tMask, layer2.TexCoord).r;
    return ApplyBlendModulation(vColorBlendValues.r, blendMask, vColorBlendValues.g);
}
