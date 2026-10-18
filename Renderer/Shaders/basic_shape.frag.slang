#version 460

#include "common/utils.slang"
#include "common/fullbright.slang"
#include "common/ViewConstants.slang"
#include "common/fog.slang"

#include "common/lighting.particle.slang"

#define renderMode_Color 0
#define renderMode_Normals 0
#define renderMode_VertexColor 0

// Order independent transparency: writes the fragment into the layers of the nearest two instead of its color
#define D_OIT 0

#if (D_OIT == 1)
    #include "common/order_independent.slang"

    // The layers only take fragments in front of the scene
    layout(early_fragment_tests) in;

    layout(binding = 0, r32ui) uniform coherent uimage2DArray g_tOrderIndependentLayers;
#endif

in vec4 vtxColor;
in vec3 vtxNormal;
in vec3 vtxPos;
in vec3 vtxPosWs;
in vec3 vtxNormalWs;
in vec3 camPos;

out vec4 outputColor;

const float shadingStrength = 0.8;

uniform bool g_bNormalShaded;
uniform bool g_bTriplanarMapping;
uniform sampler2D g_tColor; // SrgbRead(true)

// "p" point being textured
// "n" surface normal at "p"
// "k" controls the sharpness of the blending in the transition areas
// "s" texture sampler
vec4 boxmap( in sampler2D s, in vec3 p, in vec3 n, in float k )
{
    // project+fetch
    vec4 x = texture( s, p.zy );
    vec4 y = texture( s, p.xz * vec2(1,-1) );
    vec4 z = texture( s, p.yx );

    // blend weights
    vec3 w = pow( abs(n), vec3(k) );
    // blend and return
    return (x*w.x + y*w.y + z*w.z) / (w.x + w.y + w.z);
}

void main(void)
{
#if (D_OIT == 1)
    // Each sample belongs to one triangle of a surface, so only the fragment covering the first sample is kept,
    // and a pixel on an edge shared by two triangles does not take the surface twice
    if ((gl_SampleMaskIn[0] & 1) == 0)
    {
        return;
    }
#endif

    outputColor = vtxColor;
    vec3 toolTexture = vec3(1.0);

    if(g_bNormalShaded)
    {
        vec3 normalWs = normalize(vtxNormalWs);
        vec3 viewDir = normalize(camPos - vtxPosWs);

        if (g_bTriplanarMapping)
        {
            const float uvScale = 0.0625;
            outputColor = boxmap(g_tColor, vtxPos * uvScale, vtxNormal, 1.0);
            toolTexture = outputColor.rgb;
        }

        vec3 lighting = CalculateFullbrightLighting(outputColor.rgb, normalWs, viewDir);

        #if (D_BAKED_LIGHTING_FROM_PROBE == 1 && S_SCENE_PROBE_TYPE == 2)
            if (g_iRenderMode != renderMode_FullBright)
            {
                ParticleLight_t light = CalculateParticleLighting(vtxPosWs, normalWs, 1.0);

                float axisLight = mix(1.0, GetFullbrightAxisLight(normalWs), 0.5);

                lighting = outputColor.rgb * ((light.Ambient * 0.5) + light.Direct) * axisLight;
            }
        #endif

        outputColor = vec4(lighting, vtxColor.a);
    }
    else if (!gl_FrontFacing)
    {
        outputColor.rgba *= 0.75;
    }

    ApplyFog(outputColor.rgb, vtxPosWs);

    if (g_iRenderMode == renderMode_Color)
    {
        outputColor = vec4(SrgbLinearToGamma(toolTexture), 1.0);
    }
    else if (g_iRenderMode == renderMode_Normals)
    {
        outputColor = vec4(PackToColor(normalize(vtxNormalWs)), 1.0);
    }
    else if (g_iRenderMode == renderMode_VertexColor)
    {
        outputColor = vec4(vtxColor.rgb, 1.0);
    }

#if (D_OIT == 1)
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    uint fragment = PackOrderIndependentFragment(outputColor.rgb, length(camPos - vtxPosWs));

    // The first layer keeps the nearer of it and this fragment, and passes the farther on to the second
    uint previous = imageAtomicMax(g_tOrderIndependentLayers, ivec3(pixel, 0), fragment);

    if (previous != 0u)
    {
        imageAtomicMax(g_tOrderIndependentLayers, ivec3(pixel, 1), min(previous, fragment));
    }

    imageAtomicAdd(g_tOrderIndependentLayers, ivec3(pixel, 2), 1u);
#endif
}
