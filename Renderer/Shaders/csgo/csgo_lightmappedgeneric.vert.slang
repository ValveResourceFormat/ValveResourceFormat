#version 460

#include "csgo_lightmappedgeneric_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"

#if (F_LAYERS > 0)
    in vec4 vTEXCOORD4;
    out vec4 vColorBlendValues;
#endif

void main()
{
    WriteSurfaceVertex(true);

#if (F_LAYERS > 0)
    vColorBlendValues = vTEXCOORD4;
#endif
}
