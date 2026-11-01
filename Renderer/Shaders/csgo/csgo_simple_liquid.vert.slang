#version 460

#include "csgo_simple_liquid_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"

#if (F_NO_LIQUID == 0)
    uniform vec3 g_flLiquidCenterOffset;

    #if (F_USE_TEST_VALUES == 1)
        uniform vec3 g_vTestGravityDir;
    #endif

    out vec3 vLiquidPositionOs;
    out vec3 vLiquidCenterWs;
    out vec3 vLiquidUpWs;
    out vec3 vLiquidGravityWs;
#endif

void main()
{
    WriteSurfaceVertex(true);

#if (F_NO_LIQUID == 0)
    mat3x4 skinTransform = CalculateSkinTransform();

    vLiquidPositionOs = vPOSITION - g_flLiquidCenterOffset;
    vLiquidCenterWs = vec4(g_flLiquidCenterOffset, 1.0) * skinTransform;
    vLiquidUpWs = TransformDirectionToWorld(skinTransform, vec3(0.0, 0.0, 1.0));

    // TODO: Without the test values the game decodes the gravity from the two nibbles of the instance tint alpha, and
    // shows a resting bottle without liquid. Neither gravity pointing down nor decoding a white tint reproduces that.
    vLiquidGravityWs = vec3(0.0, 0.0, -1.0);

    #if (F_USE_TEST_VALUES == 1)
        if (length(g_vTestGravityDir) > 0.0)
        {
            vLiquidGravityWs = normalize(g_vTestGravityDir);
        }
    #endif
#endif
}
