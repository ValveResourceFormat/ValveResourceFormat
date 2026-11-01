#version 460

#include "vr_complex_features.slang"
#include "vr_complex.slang"

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    return GetVrComplexMaterial(texCoord, vertexNormals, 1.0);
}
