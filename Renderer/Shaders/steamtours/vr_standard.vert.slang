#version 460

#include "vr_standard_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"

#if (F_BLEND > 0)
    in vec4 vTEXCOORD2;
    out vec4 vColorBlendValues;
#endif

void main()
{
    WriteSurfaceVertex(true, 1.0, 1.0);

#if (F_BLEND > 0)
    vColorBlendValues = vTEXCOORD2;
#endif
}
