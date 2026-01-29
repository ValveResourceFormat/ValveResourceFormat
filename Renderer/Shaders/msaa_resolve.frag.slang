#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"

#define D_DOF 0

uniform int g_nNumSamplesMSAA = 1;
uniform bool g_bFlipY = false;

layout(binding = 0) uniform sampler2DMS g_tSourceMsaa;
layout (location = 0) out vec4 outputColor;

float InvNumSamples = 1.0 / float(g_nNumSamplesMSAA);

#if D_DOF == 1
uniform mat4 g_invViewProjMatrix;
uniform vec4 g_vLensPlane;
uniform vec4 g_vNearFarScaleBias;

layout(binding = 1) uniform sampler2DMS g_tSceneDepth;
#endif

vec4 SampleColorBuffer(vec2 coords)
{
    vec4 vColorMSAA = vec4(0.0);

#if D_DOF == 1
    float lowestDepth = 1.0;
#endif

    ivec2 pixelCoords = ivec2(coords.xy);
    pixelCoords.y = g_bFlipY ? textureSize(g_tSourceMsaa).y - pixelCoords.y - 1 : pixelCoords.y;

    for (int i = 0; i < g_nNumSamplesMSAA; i++)
    {
        vec4 sampleColor = texelFetch(g_tSourceMsaa, pixelCoords, i);
        sampleColor = clamp(sampleColor, vec4(0), vec4(65504));

#if D_DOF == 1
        lowestDepth = min(texelFetch(g_tSceneDepth, ivec2(pixelCoords), i).x, lowestDepth);
#endif
        vColorMSAA += sampleColor * InvNumSamples;
    }

    float outputAlpha = 1.0;

#if D_DOF == 1
    vec2 sampleCoord = gl_FragCoord.xy * g_vInvViewportSize.xy;

    // convert from screen coords to clipspace (normalised device coordinates)
    vec4 screenToClip = vec4(

        sampleCoord.x * 2.0 - 1.0,                                    // [0, 1] -> [-1, 1]
        sampleCoord.y * 2.0 - 1.0,                                    // [0, 1] -> [-1, 1]
        LinearRamp(g_flViewportMinZ, g_flViewportMaxZ, lowestDepth),  // normalise depth 
        1.0

    );

    // undo view projection to get back 3d world coordinates
    vec4 worldPos =  g_invViewProjMatrix * screenToClip;
    vec4 worldPos3D = vec4(worldPos.xyz / worldPos.w, 1.0);

    // projecting worldpos onto the lens plane, gives us how far away the point is from the plane
    // g_vLensPlane.w is the plane offset, using dot between 4d vectors for this is clever
    float distanceFromLensPlane = dot(g_vLensPlane, worldPos3D);

    float nearScale = g_vNearFarScaleBias.x;
    float nearBias = g_vNearFarScaleBias.y;
    float farScale = g_vNearFarScaleBias.z;
    float farBias = g_vNearFarScaleBias.w;

    float closeCoC = clamp((distanceFromLensPlane * nearScale) + -nearBias, 0.0, 1.0);
    float farCoC = clamp((distanceFromLensPlane * farScale) + farBias, 0.0, 1.0);

    outputAlpha = max(closeCoC, farCoC);
#endif

    return vec4(vColorMSAA.rgb, outputAlpha);
}

void main()
{
    outputColor = SampleColorBuffer(gl_FragCoord.xy);
}
