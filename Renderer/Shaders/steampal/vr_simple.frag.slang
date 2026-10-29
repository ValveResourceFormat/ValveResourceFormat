#version 460

#include "vr_simple_features.slang"
#include "../hlvr/vr_simple.slang"

uniform float g_flBumpStrength = 1.0;

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    return GetVrSimpleMaterial(texCoord, vertexNormals, g_flBumpStrength);
}
