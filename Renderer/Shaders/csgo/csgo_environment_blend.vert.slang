#version 460

#include "../common/utils.slang"
#include "../common/instancing.slang"
#include "../common/animation.slang"
#include "../common/shared_combos.slang"
#include "csgo_environment_blend_features.slang"
#include "../common/surface_height_layers_vs.slang"

out vec4 vTexCoord2;

// Material 2
uniform int g_nUVSet2 = 1;
uniform float g_flTexCoordRotation2 = 0.0;
uniform vec2 g_vTexCoordCenter2 = vec2(0.5);
uniform vec2 g_vTexCoordOffset2 = vec2(0.0);
uniform vec2 g_vTexCoordScale2 = vec2(1.0);

#if (F_DETAIL_NORMAL == 1)
    uniform int g_nDetailUVSet2 = -1;
    uniform float g_flDetailTexCoordRotation2 = 0.0;
    uniform vec2 g_vDetailTexCoordCenter2 = vec2(0.5);
    uniform vec2 g_vDetailTexCoordOffset2 = vec2(0.0);
    uniform vec2 g_vDetailTexCoordScale2 = vec2(1.0);
#endif

#if (F_ENABLE_LAYER_3 == 1)
    uniform int g_nUVSet3 = 1;
    uniform float g_flTexCoordRotation3 = 0.0;
    uniform vec2 g_vTexCoordCenter3 = vec2(0.5);
    uniform vec2 g_vTexCoordOffset3 = vec2(0.0);
    uniform vec2 g_vTexCoordScale3 = vec2(1.0);

    #if (F_DETAIL_NORMAL == 1)
        uniform int g_nDetailUVSet3 = -1;
        uniform float g_flDetailTexCoordRotation3 = 0.0;
        uniform vec2 g_vDetailTexCoordCenter3 = vec2(0.5);
        uniform vec2 g_vDetailTexCoordOffset3 = vec2(0.0);
        uniform vec2 g_vDetailTexCoordScale3 = vec2(1.0);
    #endif

    out vec4 vTexCoord3;
#endif

#if (F_SHARED_COLOR_OVERLAY == 1)
    uniform int g_nColorOverlayUVSet = 2;
    uniform float g_flOverlayTexCoordRotation = 0.0;
    uniform vec2 g_vOverlayTexCoordCenter = vec2(0.5);
    uniform vec2 g_vOverlayTexCoordOffset = vec2(0.0);
    uniform vec2 g_vOverlayTexCoordScale = vec2(1.0);

    out vec2 vOverlayTexCoord;
#endif

void main()
{
    vec4 baseUvs = WriteHeightLayerVertex();

    vTexCoord2.xy = GetLayerTexCoord(g_nUVSet2, baseUvs, g_flTexCoordRotation2, g_vTexCoordScale2, g_vTexCoordOffset2, g_vTexCoordCenter2);
    vTexCoord2.zw = baseUvs.zw;

    #if (F_ENABLE_LAYER_3 == 1)
        vTexCoord3.xy = GetLayerTexCoord(g_nUVSet3, baseUvs, g_flTexCoordRotation3, g_vTexCoordScale3, g_vTexCoordOffset3, g_vTexCoordCenter3);
        vTexCoord3.zw = baseUvs.xy;
    #endif

    #if (F_SHARED_COLOR_OVERLAY == 1)
        if (g_nColorOverlayUVSet > 0)
        {
            vOverlayTexCoord = GetLayerTexCoord(g_nColorOverlayUVSet, baseUvs, g_flOverlayTexCoordRotation, g_vOverlayTexCoordScale, g_vOverlayTexCoordOffset, g_vOverlayTexCoordCenter);
        }
    #endif

    #if (F_DETAIL_NORMAL == 1)
        vTexCoord2.zw = GetDetailTexCoord(g_nDetailUVSet2, g_nUVSet2, baseUvs, g_flDetailTexCoordRotation2, g_vDetailTexCoordScale2, g_vDetailTexCoordOffset2, g_vDetailTexCoordCenter2);

        #if (F_ENABLE_LAYER_3 == 1)
            vTexCoord3.zw = GetDetailTexCoord(g_nDetailUVSet3, g_nUVSet3, baseUvs, g_flDetailTexCoordRotation3, g_vDetailTexCoordScale3, g_vDetailTexCoordOffset3, g_vDetailTexCoordCenter3);
        #endif
    #endif
}
