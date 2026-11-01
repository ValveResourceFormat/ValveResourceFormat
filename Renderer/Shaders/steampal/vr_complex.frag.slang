#version 460

#include "vr_complex_features.slang"
#include "../hlvr/vr_complex.slang"

uniform float g_flBumpStrength = 1.0;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    return GetVrComplexMaterial(texCoord, vertexNormals, g_flBumpStrength);
}
