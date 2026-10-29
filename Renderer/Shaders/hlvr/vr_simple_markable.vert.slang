#version 460

#include "vr_simple_markable_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"

#if (GameVfx_vr_parallax_occlusion == 1)
    #include "../features/parallax_occlusion_vs.slang"
#endif

#if (F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vCOLOR;
#endif

void main()
{
    WriteSurfaceVertex(true);

#if (GameVfx_vr_parallax_occlusion == 1)
    vParallaxOffsetOut = GetParallaxOcclusionOffset(vFragPosition, vNormalOut, vTangentOut);
#endif

#if (F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1)
    vVertexColorOut *= SrgbGammaToLinear(vCOLOR);
#endif
}
