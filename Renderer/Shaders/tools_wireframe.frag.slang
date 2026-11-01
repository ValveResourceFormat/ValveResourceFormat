#version 460

// Faces lit from the camera and two fixed directions
#define D_SHADED 0

in vec4 vColorOut;
in vec3 vNormalWs;
in vec3 vPositionWs;
in vec4 vLineStartPs;
in vec4 vThisVertexPs;

#include "common/view_constants.slang"

// 0 solid, 1 dashed, 2 dotted
uniform float g_flPatternType;

out vec4 outputColor;

// Dashes run from the start of each segment, measured on the screen
bool IsInPatternGap()
{
    float gap = 0.0;
    if (g_flPatternType == 1.0)
    {
        gap = 16.0;
    }
    else if (g_flPatternType == 2.0)
    {
        gap = 3.0;
    }

    if (gap == 0.0)
    {
        return false;
    }

    vec2 thisPosition = (vThisVertexPs.xy / vThisVertexPs.w) / g_vInvViewportSize;
    vec2 startPosition = (vLineStartPs.xy / vLineStartPs.w) / g_vInvViewportSize;

    return mod(length(thisPosition - startPosition), gap * 2.0) > gap;
}

void main(void)
{
    if (IsInPatternGap())
    {
        discard;
    }

    vec4 color = vColorOut;

#if D_SHADED == 1
    vec3 normal = normalize(vNormalWs);
    vec3 positionVs = (g_matWorldToView * vec4(vPositionWs, 1.0)).xyz;
    vec3 normalVs = (g_matWorldToView * vec4(normal, 0.0)).xyz;

    float facing = dot(normalize(-positionVs), normalVs);
    float key = max(0.0, dot(vec3(0.5819, 0.3637, 0.7274), normal));
    float fill = max(0.0, dot(vec3(-0.572, -0.6674, -0.4767), normal));

    color.rgb *= clamp((facing + key + fill) * 0.5, 0.0, 1.0);
#endif

    outputColor = color;
}
