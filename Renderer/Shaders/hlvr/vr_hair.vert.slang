#version 460

#include "vr_hair_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"

void main()
{
    WriteSurfaceVertex(false);
}
