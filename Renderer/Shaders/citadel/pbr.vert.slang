#version 460

#include "../common/utils.slang"
#include "../common/shared_combos.slang"
#include "pbr_features.slang"

#include "../common/instancing.slang"
#include "../common/animation.slang"

in vec3 vPOSITION;
in vec2 vTEXCOORD;
#include "../common/compression.slang"

#if (F_SECONDARY_UV == 1)
    in vec2 vTEXCOORD1;
#endif

#if (F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vCOLOR;
#endif

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec4 vTangentOut;
centroid out vec3 vCentroidNormalOut;
out vec2 vTexCoordOut;
#if (F_SECONDARY_UV == 1)
    out vec2 vTexCoord2;
#endif
centroid out vec4 vVertexColorOut;

#include "../common/view_constants.slang"
#include "../common/lighting_constants.slang"
#include "../common/baked_lighting_vs.slang"
#include "../common/texcoord_scale_by_model.slang"

#if (F_JITTER_VERTICES > 0)
    uniform sampler2D g_tJitterMask;

    uniform vec3 g_vJitterFrequenciesA1 = vec3(0.02, 0.04, 0.08);
    uniform vec3 g_vJitterAmplitudesXA1 = vec3(4.0, 0.0, 0.0);
    uniform vec3 g_vJitterAmplitudesYA1 = vec3(0.0, 4.0, 0.0);
    uniform vec3 g_vJitterAmplitudesZA1 = vec3(0.0, 0.0, 4.0);
    uniform float g_flJitterSpeedA1 = 10.0;

    uniform vec3 g_vJitterFrequenciesB1 = vec3(0.05, 0.09, 0.17);
    uniform vec3 g_vJitterAmplitudesXB1 = vec3(0.0);
    uniform vec3 g_vJitterAmplitudesYB1 = vec3(0.0);
    uniform vec3 g_vJitterAmplitudesZB1 = vec3(0.0);
    uniform float g_flJitterSpeedB1 = 7.0;

    uniform float g_flJitterQuantizeTime1 = 0.0;
    uniform float g_flJitterQuantizeDisplacement1 = 0.0;
    uniform vec3 g_vJitterExpression = vec3(0.0);

    #if (F_SECONDARY_UV == 1)
        uniform bool g_bUseSecondaryUvForJitterMask = false;
    #endif

    // A smoothed triangle wave per axis, centred on zero. Driving the phase from the vertex
    // position keeps neighbours in step, so the mesh sways as a sheet rather than shattering.
    // A negative frequency runs its axis backwards; the magnitude still sets the wavelength.
    vec3 GetJitterWave(vec3 position, vec3 frequencies, float speed, float time)
    {
        vec3 phase = fract(position * abs(frequencies)) + sign(frequencies) * (time * speed);
        vec3 triangleWave = abs(fract(phase) * 2.0 - 1.0);

        return triangleWave * triangleWave * (3.0 - 2.0 * triangleWave) - 0.5;
    }

    // Two waves at different frequencies, each axis mixing all three of a wave's components,
    // so a curtain can be given a slow swing and a faster ripple at once.
    vec3 GetJitterDisplacement(vec3 noisePosition, vec2 maskTexCoord)
    {
        float time = GetQuantizedTime(g_flJitterQuantizeTime1);

        vec3 waveA = GetJitterWave(noisePosition, g_vJitterFrequenciesA1, g_flJitterSpeedA1, time);
        vec3 waveB = GetJitterWave(noisePosition, g_vJitterFrequenciesB1, g_flJitterSpeedB1, time);

        vec3 displacement = vec3(dot(g_vJitterAmplitudesXA1, waveA), dot(g_vJitterAmplitudesYA1, waveA), dot(g_vJitterAmplitudesZA1, waveA))
            + vec3(dot(g_vJitterAmplitudesXB1, waveB), dot(g_vJitterAmplitudesYB1, waveB), dot(g_vJitterAmplitudesZB1, waveB))
            + g_vJitterExpression;

        if (g_flJitterQuantizeDisplacement1 != 0.0)
        {
            displacement = round(displacement / g_flJitterQuantizeDisplacement1) * g_flJitterQuantizeDisplacement1;
        }

        return displacement * textureLod(g_tJitterMask, maskTexCoord, 0.0).x;
    }
#endif

void main()
{
    ObjectData_t object = GetObjectData();

    mat3x4 skinTransform = CalculateSkinTransform();
    vec3 fragPosition = vec4(vPOSITION, 1.0) * skinTransform;

    #if (F_JITTER_VERTICES > 0)
        vec2 jitterTexCoord = vTEXCOORD.xy;
        #if (F_SECONDARY_UV == 1)
            if (g_bUseSecondaryUvForJitterMask)
            {
                jitterTexCoord = vTEXCOORD1.xy;
            }
        #endif

        #if (F_JITTER_VERTICES == 2)
            // Object space: the noise travels with the prop, so the displacement has to be
            // rotated into world space rather than added to it.
            fragPosition += vec4(GetJitterDisplacement(vPOSITION, jitterTexCoord), 0.0) * skinTransform;
        #else
            fragPosition += GetJitterDisplacement(fragPosition, jitterTexCoord);
        #endif
    #endif

    gl_Position = g_matWorldToProjection * vec4(fragPosition, 1.0);
    vFragPosition = fragPosition;

    // Normals are deliberately not recomputed against the displacement.

    ComputeWorldTangentFrame(skinTransform, vNormalOut, vTangentOut);
    vCentroidNormalOut = vNormalOut;

    WriteBakedLighting();

    vVertexColorOut.rgb = SrgbGammaToLinear(object.vTint.rgb);
    vVertexColorOut.a = object.vTint.a;

    #if (F_VERTEX_COLOR == 1) || (F_PAINT_VERTEX_COLORS == 1)
        vVertexColorOut *= vec4(SrgbGammaToLinear(vCOLOR.rgb), vCOLOR.a);
    #endif

    #if (F_SECONDARY_UV == 1)
        vec4 texCoords = vec4(vTEXCOORD.xy, vTEXCOORD1.xy);
    #else
        vec4 texCoords = vTEXCOORD.xyxy;
    #endif

    texCoords = ApplyTexCoordScaleByModel(texCoords, CalculateObjectToWorldMatrix());
    vTexCoordOut = texCoords.xy;

    #if (F_SECONDARY_UV == 1)
        vTexCoord2 = texCoords.zw;
    #endif
}
