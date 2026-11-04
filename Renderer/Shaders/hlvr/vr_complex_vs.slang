#version 460

// Vertex shader body of vr_complex. The entry file includes its game's features file first.

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"
#include "../features/detail_texture_vs.slang"

#if (F_FOLIAGE_ANIMATION > 0)
    #include "../features/foliage_vr_vs.slang"

    out vec4 vFoliageParamsOut;
#elif (F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vCOLOR;
#endif

void main()
{
    WriteSurfaceVertex(false);

#if (F_FOLIAGE_ANIMATION == 4)
    vFoliageParamsOut = vec4(0.0, 0.0, 0.0, 1.0);
#elif (F_FOLIAGE_ANIMATION > 0)
    // Interpolate out for debug visualization
    vFoliageParamsOut = vec4(vTEXCOORD3.xyz, 1.0);
#endif

#if (F_FOLIAGE_ANIMATION > 0) && (F_FOLIAGE_ANIMATION != 2)
    vFragPosition = AnimateVrFoliage();
    vCentroidNormalOut = vNormalOut;
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);
#endif

#if (F_FOLIAGE_ANIMATION == 0) && ((F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1))
    vVertexColorOut *= SrgbGammaToLinear(vCOLOR);
#endif

    WriteDetailTexCoords();
}
