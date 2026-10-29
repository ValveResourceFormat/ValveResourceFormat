#version 460

#include "csgo_simple_2way_blend_features.slang"

#include "../common/surface_blend_2way.slang"

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    return GetTwoWayBlendMaterial(texCoord, vertexNormals, 1.0);
}

in vec4 vColorBlendValues;

float GetBlendFactor(Layer2_t layer2)
{
    float blendMask = texture(g_tMask, layer2.TexCoord).r;
    float softnessPaint = max(vColorBlendValues.a, 0.1);
    return ApplyBlendModulation(vColorBlendValues.r, blendMask, softnessPaint);
}
