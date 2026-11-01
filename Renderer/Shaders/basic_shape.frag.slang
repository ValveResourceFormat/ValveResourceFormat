#version 460

#include "common/utils.slang"
#include "common/fullbright.slang"
#include "common/view_constants.slang"
#include "common/fog.slang"

#include "common/texturing.slang"
#include "common/lighting_particle.slang"

#define renderMode_Color 0
#define renderMode_Normals 0
#define renderMode_VertexColor 0

// Order independent transparency: writes the fragment into the layers of the nearest two instead of its color.
// With D_LINES, the lines read the layers instead, to fade behind the fills in front of them.
#define D_OIT 0

// Lines along the shape's edges, in its unlit color
#define D_LINES 0

#if (D_OIT == 1)
    #include "common/order_independent.slang"

    // The layers only take fragments in front of the scene
    layout(early_fragment_tests) in;

    layout(binding = 0, r32ui) uniform coherent uimage2DArray g_tOrderIndependentLayers;
#endif

// Lines fade out between these distances, where they would crowd together into noise
const float lineFadeStart = 1024.0;
const float lineFadeEnd = 4096.0;

// Fills within this fraction of a line's distance are the faces the line lies on, which do not cover it
const float lineOwnFaceTolerance = 0.02;

in vec4 vtxColor;
// One normal per triangle, so the tool texture of a smooth shape keeps a fixed plane across each triangle
flat in vec3 vtxNormal;
in vec3 vtxPos;
in vec3 vtxPosWs;
in vec3 vtxNormalWs;
in vec3 camPos;

out vec4 outputColor;

const float shadingStrength = 0.8;

// 64 pixel tool textures at Hammer's default scale of 0.25 repeat every 16 units
const float toolTextureScale = 0.0625;

uniform bool g_bNormalShaded;
uniform bool g_bHasToolTexture;
uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture

#if (D_LINES == 1)
    // The average linear color of g_tColor, which lines take in place of its pattern
    uniform vec3 g_vToolTextureAverage;
#endif

// Projects the tool texture onto the plane of the face, upright on walls and with its right towards the right of
// someone looking at the face from outside, so it reads the same way on every side of the shape. A slanted face
// shows the texture once, unstretched.
vec4 SampleToolTexture(vec3 position, vec3 normal)
{
    vec3 n = normalize(normal);
    vec3 up = abs(n.z) >= max(abs(n.x), abs(n.y)) ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, 1.0);
    vec3 u = normalize(cross(up, n));
    vec3 v = cross(u, n);

    return texture(g_tColor, vec2(dot(position, u), dot(position, v)) * toolTextureScale);
}

// Keeps the edge of label text about a pixel wide where the texture is magnified
float SharpenToolTextureOpacity(float alpha, float fillAlpha)
{
    if (alpha <= fillAlpha)
    {
        return alpha;
    }

    float coverage = (alpha - fillAlpha) / (1.0 - fillAlpha);
    float sharpness = max(1.0, 0.5 / max(fwidth(coverage), 0.0001));
    coverage = clamp((coverage - 0.5) * sharpness + 0.5, 0.0, 1.0);

    return mix(fillAlpha, 1.0, coverage);
}

#if (D_LINES == 1 && D_OIT == 1)
// What the fills in front of a line let through. Past the nearest two only their count is known.
float TransmittanceOfFillsInFront(float distance)
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    uint lineNearness = PackOrderIndependentNearness(distance * (1.0 - lineOwnFaceTolerance));

    uint nearest = imageLoad(g_tOrderIndependentLayers, ivec3(pixel, 0)).r;

    if ((nearest >> 16) <= lineNearness)
    {
        return 1.0;
    }

    float nearestTransmittance = 1.0 - UnpackOrderIndependentAlpha(nearest);
    uint second = imageLoad(g_tOrderIndependentLayers, ivec3(pixel, 1)).r;

    if ((second >> 16) <= lineNearness)
    {
        return nearestTransmittance;
    }

    uint count = imageLoad(g_tOrderIndependentLayers, ivec3(pixel, 2)).r;

    return nearestTransmittance * OrderIndependentBehindNearest(second, count);
}
#endif

#if (D_LINES == 1)
vec4 ShadeLine()
{
    float distance = length(camPos - vtxPosWs);
    float alpha = 1.0 - smoothstep(lineFadeStart, lineFadeEnd, distance);

    if (alpha <= 0.0)
    {
        discard;
    }

    vec3 color = vtxColor.rgb;

    if (g_bHasToolTexture)
    {
        color = g_vToolTextureAverage;
    }

    ApplyFog(color, vtxPosWs);

#if (D_OIT == 1)
    alpha *= TransmittanceOfFillsInFront(distance);
#endif

    return vec4(color, alpha);
}
#endif

void main(void)
{
#if (D_LINES == 1)
    outputColor = ShadeLine();
#else
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

        if (g_bHasToolTexture)
        {
            outputColor = SampleToolTexture(vtxPos, vtxNormal);
            outputColor.a = SharpenToolTextureOpacity(outputColor.a, vtxColor.a);
            toolTexture = outputColor.rgb;
        }

        vec3 lighting = CalculateFullbrightLighting(outputColor.rgb, normalWs, viewDir);

        #if (D_BAKED_LIGHTING_FROM_PROBE == 1)
            if (g_iRenderMode != renderMode_FullBright)
            {
                ParticleLight_t light = CalculateParticleLighting(vtxPosWs, normalWs, 1.0, true);

                float axisLight = mix(1.0, GetFullbrightAxisLight(normalWs), 0.5);

                lighting = outputColor.rgb * ((light.Ambient * 0.5) + light.Direct) * axisLight;
            }
        #endif

        outputColor.rgb = lighting;
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
    uint fragment = PackOrderIndependentFragment(outputColor.rgb, outputColor.a, length(camPos - vtxPosWs));

    // The first layer keeps the nearer of it and this fragment, and passes the farther on to the second
    uint previous = imageAtomicMax(g_tOrderIndependentLayers, ivec3(pixel, 0), fragment);

    if (previous != 0u)
    {
        imageAtomicMax(g_tOrderIndependentLayers, ivec3(pixel, 1), min(previous, fragment));
    }

    imageAtomicAdd(g_tOrderIndependentLayers, ivec3(pixel, 2), 1u);
#endif
#endif
}
