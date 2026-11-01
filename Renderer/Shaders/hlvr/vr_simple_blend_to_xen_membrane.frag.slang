#version 460

#include "vr_simple_blend_to_triplanar_features.slang"
#include "vr_simple_blend_to_triplanar.slang"

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    return GetBlendToTriplanarMaterial(texCoord, vertexNormals, true);
}
