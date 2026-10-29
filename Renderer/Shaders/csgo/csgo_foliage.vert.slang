#version 460

#include "csgo_foliage_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"

// Each include is expanded once wherever it is first named, so the wind that all three animations share comes first
#if (F_FOLIAGE_ANIMATION == 1) || (F_VERTEX_ANIMATION > 0)
    #include "../features/foliage_wind_vs.slang"
#endif

#if (F_FOLIAGE_ANIMATION == 1)
    #include "../features/foliage_deflection_vs.slang"
#elif (F_VERTEX_ANIMATION == 2)
    #include "../features/foliage_noise_vs.slang"
#elif (F_VERTEX_ANIMATION == 1)
    #include "../features/foliage_sway_vs.slang"
#endif

// The foliage params are in the vertex color
in vec4 vCOLOR;
out vec4 vFoliageParamsOut;

#if (F_FOLIAGE_ANIMATION == 0) && (F_VERTEX_ANIMATION > 0)
vec3 AnimatePoint(vec3 position, vec4 foliageParams)
{
    #if (F_VERTEX_ANIMATION == 2)
        return AnimateFoliageNoise(position, foliageParams);
    #else
        return AnimateFoliageSway(position, foliageParams);
    #endif
}
#endif

void main()
{
    WriteSurfaceVertex(true);

    // Interpolated out for the foliage params view
    vFoliageParamsOut = vCOLOR;

#if (F_FOLIAGE_ANIMATION == 1)
    vFragPosition = AnimateFoliageDeflection();
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);
    vCentroidNormalOut = vNormalOut;
#elif (F_VERTEX_ANIMATION > 0)
    // Points one unit along the tangent and bitangent are animated too, and the frame is rebuilt from them
    vec3 position = vFragPosition;
    vec3 animatedPosition = AnimatePoint(position, vCOLOR);
    vec3 bitangentDir = cross(vNormalOut, vTangentOut.xyz);
    vec3 tangent = AnimatePoint(position + vTangentOut.xyz, vCOLOR) - animatedPosition;
    vec3 bitangent = AnimatePoint(position + bitangentDir, vCOLOR) - animatedPosition;

    vNormalOut = normalize(cross(tangent, bitangent));
    vTangentOut.xyz = normalize(tangent);
    vFragPosition = animatedPosition;
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);
    vCentroidNormalOut = vNormalOut;
#endif
}
