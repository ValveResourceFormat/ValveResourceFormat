#version 460

#include "csgo_simple_2way_blend_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"

#if (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vCOLOR;
#endif

in vec4 vTEXCOORD4;
out vec4 vColorBlendValues;

void main()
{
    WriteSurfaceVertex(true);

#if (F_PAINT_VERTEX_COLORS == 1)
    vVertexColorOut *= GetUndecodedVertexPaint(vCOLOR);
#endif

    vColorBlendValues = vTEXCOORD4;
}
