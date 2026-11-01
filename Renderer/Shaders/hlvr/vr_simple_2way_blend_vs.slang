#version 460

// Vertex shader body of the surfaces that blend two layers by painted vertex values. The entry file includes its
// game's features file first.

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"

#if (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vCOLOR;
#endif

in vec4 vTEXCOORD4;
in vec4 vTEXCOORD5;
out vec4 vColorBlendValues;

void main()
{
    WriteSurfaceVertex(true);

#if (F_PAINT_VERTEX_COLORS == 1)
    vVertexColorOut *= SrgbGammaToLinear(vCOLOR);
#endif

    // The blend softness is painted into its own stream
    vColorBlendValues = vTEXCOORD4;
    vColorBlendValues.y = max(0.5 * vTEXCOORD5.x, 0.1);
}
