#version 460

#include "vr_xen_foliage_features.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)

#include "../common/surface_base_vs.slang"
#include "../common/surface_model_tint_vs.slang"
#include "../features/detail_texture_vs.slang"

// The foliage params are in the vertex color, red masks the motion along the normal
in vec4 vCOLOR;
out vec4 vFoliageParamsOut;

#if (F_BLEND == 1)
    // The second layer is blended in by vertex paint
    in vec4 vTEXCOORD4;
    in vec4 vTEXCOORD5;
    out vec4 vColorBlendValues;
#endif

#if (F_XEN_GLOW == 1)
    in vec3 vPropWorldOrigin;
    out vec3 vXenGlowCenterOut;
#endif

uniform float g_flXenMovementScale = 1.0;
uniform vec3 g_vXenWorldSpaceMotionScale = vec3(1.0);

#if (F_XEN_ANIMATION_TEXTURE == 1)
    uniform sampler2D g_tAnimationTexture;
    uniform float g_flAnimationAmplitude = 1.0;
    uniform float g_flAnimationRate = 1.0;
    uniform float g_flAnimationSpatialVariance = 1.0;
    uniform bool g_bAlignAnimationToTextureV = false;
    uniform bool g_bReverseTextureVAnimationDirection = false;

    // How far the texture moved the vertex, relative to its amplitude
    out float vXenAnimationDisplacementOut;

    // The motion that the flashlight adds is not simulated
    float xenTextureAnimationWave(vec3 pos)
    {
        float spatialPhase = dot(pos, vec3(0.31, 0.23, 0.13));
        float phase = sin(g_flAnimationSpatialVariance * spatialPhase);

        if (g_bAlignAnimationToTextureV)
        {
            // The wave travels along the texture's V axis
            vec2 texCoord = vTEXCOORD.xy * g_vTexCoordScale + g_vTexCoordOffset;
            float direction = g_bReverseTextureVAnimationDirection ? -1.0 : 1.0;
            phase = g_flAnimationSpatialVariance * (texCoord.y * direction + spatialPhase * 0.01);
        }

        vec2 waveCoords = vec2(g_flAnimationRate * (phase + g_flTime), 0.5);
        return g_flAnimationAmplitude * textureLod(g_tAnimationTexture, waveCoords, 0.0).r;
    }
#endif

// The interaction volume that excites nearby plants is not simulated, only their idle motion
vec3 XenAnimation(vec3 pos, vec3 normal, float motionMask)
{
#if (F_XEN_ANIMATION_TEXTURE == 1)
    float wave = xenTextureAnimationWave(pos);
#else
    float slowWave = 0.75 + 0.25 * sin(dot(pos, vec3(0.31, 0.23, 0.13)) + g_flTime);
    float fastWave = 0.00625 * sin(dot(pos, vec3(0.51, 0.93, 0.73)) + g_flTime * 16.0);
    float wave = slowWave + fastWave;
#endif

    float amplitude = wave * min(motionMask, 1.0) * 4.0;

    vec3 motionDir = normal * g_vXenWorldSpaceMotionScale;
    vec3 offset = motionDir * amplitude * g_flXenMovementScale;

#if (F_XEN_ANIMATION_TEXTURE == 1)
    float relativeDisplacement = 0.0;
    if (g_flAnimationAmplitude != 0.0)
    {
        relativeDisplacement = length(offset) / g_flAnimationAmplitude;
    }

    vXenAnimationDisplacementOut = saturate(1.5 * (relativeDisplacement - 0.2));
#endif

    return pos + offset;
}

void main()
{
    WriteSurfaceVertex(true);

    // Interpolated out for the foliage params view
    vFoliageParamsOut = vCOLOR;

    vFragPosition = XenAnimation(vFragPosition, vNormalOut, vCOLOR.x);
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

#if (F_XEN_GLOW == 1)
    vXenGlowCenterOut = vec4(vPropWorldOrigin, 1.0) * CalculateObjectToWorldMatrix();
#endif

#if (F_BLEND == 1)
    vColorBlendValues = vTEXCOORD4;
    vColorBlendValues.y = max(0.5 * vTEXCOORD5.x, 0.1);
#endif

    WriteDetailTexCoords();
}
