#version 460

#include "vr_simple_features.slang"
#include "../hlvr/vr_simple.slang"

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    return GetVrSimpleMaterial(texCoord, vertexNormals, 1.0);
}
