#version 460

// Lines drawn as quads as wide as the line width, six vertices to a segment
#define D_THICK_LINES 0

// Pulled towards the camera, so lines and faces show on the surfaces they lie on
#define D_DEPTH_BIAS 0

in vec3 vPOSITION;
in vec3 vNORMAL;
in vec4 vCOLOR;
in vec3 vLineStart;
in vec3 vLineEnd;
in float vLineCorner;

#include "common/utils.slang"
#include "common/view_constants.slang"
#include "common/instancing.slang"

// Width of thick lines, in pixels
uniform float g_flLineThickness;

out vec4 vColorOut;
out vec3 vNormalWs;
out vec3 vPositionWs;
out vec4 vLineStartPs;
out vec4 vThisVertexPs;

const float DepthBiasAmount = 0.005;

// Reverse-Z, so the bias moves towards larger depths, and it falls off from the near plane as the
// depth the editor biases with does
vec4 ApplyDepthBias(vec4 positionPs)
{
#if D_DEPTH_BIAS == 1
    float depthFromNear = clamp(1.0 - positionPs.z / positionPs.w, 0.0, 1.0);
    positionPs.z += mix(DepthBiasAmount, 1e-6, depthFromNear) * positionPs.w;
#endif
    return positionPs;
}

void main(void)
{
    mat4 objectToWorld = UnpackMatrix4(CalculateObjectToWorldMatrix());

    vColorOut = vec4(SrgbGammaToLinear(vCOLOR.rgb), vCOLOR.a);
    vNormalWs = normalize(mat3(objectToWorld) * vNORMAL);

#if D_THICK_LINES == 1
    vec4 startPs = g_matWorldToProjection * (objectToWorld * vec4(vLineStart, 1.0));
    vec4 endPs = g_matWorldToProjection * (objectToWorld * vec4(vLineEnd, 1.0));

    // An end behind the camera would throw the width out, so the segment is cut at the near plane
    const float NearPlaneW = 0.01;
    bool startBehind = startPs.w < NearPlaneW;
    bool endBehind = endPs.w < NearPlaneW;

    if (startBehind != endBehind)
    {
        vec4 cut = mix(startPs, endPs, (NearPlaneW - startPs.w) / (endPs.w - startPs.w));

        if (startBehind)
        {
            startPs = cut;
        }
        else
        {
            endPs = cut;
        }
    }

    // The corners go start and end on one side, then start and end on the other
    int corner = int(vLineCorner);
    bool isEnd = (corner & 1) != 0;
    float side = corner < 2 ? 1.0 : -1.0;

    // One pixel across the line, in projective space
    vec2 lineDirection = ((endPs.xy / endPs.w) - (startPs.xy / startPs.w)) / (2.0 * g_vInvViewportSize);
    vec2 lineNormal = normalize(vec2(1.0, -1.0) * lineDirection.yx) * (2.0 * g_vInvViewportSize);

    vec4 positionPs = isEnd ? endPs : startPs;
    positionPs.xy += side * (0.5 * g_flLineThickness) * lineNormal * positionPs.w;

    vPositionWs = (objectToWorld * vec4(isEnd ? vLineEnd : vLineStart, 1.0)).xyz;
    vLineStartPs = startPs;
#else
    vec4 positionWs = objectToWorld * vec4(vPOSITION, 1.0);
    vec4 positionPs = g_matWorldToProjection * positionWs;

    vPositionWs = positionWs.xyz;
    vLineStartPs = g_matWorldToProjection * (objectToWorld * vec4(vLineStart, 1.0));
#endif

    vThisVertexPs = positionPs;
    gl_Position = ApplyDepthBias(positionPs);
}
