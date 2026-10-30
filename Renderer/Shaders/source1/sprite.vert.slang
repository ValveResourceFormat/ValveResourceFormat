#version 460

#include "sprite_features.slang"
#include "common_vs.slang"

// Corner of the sprite along its right and up axes, and the orientation: 0 turns around the up axis to face the
// view direction, 1 does so toward the world origin, 2 is parallel to the view plane, 3 stays as it is, 4 is parallel
// to the view plane and rolled, 5 faces the camera
in vec3 vSpriteBillboard;

// Roll in radians, how much of the distance to the camera the size grows by (zero for a fixed size in the world),
// and the fade: 1 fades with the distance like a glow, 2 fades with the distances of vSpriteFade
in vec3 vSpriteParams;

// Distances where a light glow starts to show, is at full strength, and is gone, and the radius of what has to be in
// view for a glow to show, which is zero for a sprite that does not check
in vec4 vSpriteFade;

const float GlowFullDistance = 1200.0;
const float MinUprightDot = 0.999848;

out vec3 vFragPosition;
out vec2 vTexCoordOut;
out vec4 vVertexColorOut;
flat out vec4 vGlowProxyOut;

float RemapClamped(float value, float from, float to, float resultFrom, float resultTo)
{
    float fraction = saturate((value - from) / (to - from));
    return mix(resultFrom, resultTo, fraction);
}

float GetFade(float mode, float cameraDistance)
{
    if (mode == 1.0)
    {
        return saturate((GlowFullDistance * GlowFullDistance) / (cameraDistance * cameraDistance));
    }

    if (mode == 2.0)
    {
        float minDistance = vSpriteFade.x;
        float maxDistance = vSpriteFade.y;
        float outerDistance = vSpriteFade.z;

        if (outerDistance > maxDistance && cameraDistance > maxDistance)
        {
            return RemapClamped(cameraDistance, maxDistance, outerDistance, 1.0, 0.0);
        }

        return maxDistance > minDistance ? RemapClamped(cameraDistance, minDistance, maxDistance, 0.0, 1.0) : 1.0;
    }

    return 1.0;
}

void main()
{
    vec3 center = vec4(vPOSITION, 1.0) * CalculateSkinTransform();
    vec3 toCenter = center - g_vCameraPositionWs;
    float cameraDistance = length(toCenter);

    vec3 viewForward = g_vCameraDirWs;
    vec3 viewUp = g_vCameraUpDirWs;
    vec3 viewRight = cross(viewForward, viewUp);

    int orientation = int(vSpriteBillboard.z + 0.5);
    vec3 right = viewRight;
    vec3 up = viewUp;
    bool isVisible = true;

    if (orientation == 0)
    {
        isVisible = abs(viewForward.z) <= MinUprightDot;
        up = vec3(0.0, 0.0, 1.0);
        right = normalize(vec3(viewForward.y, -viewForward.x, 0.0));
    }
    else if (orientation == 1)
    {
        vec3 toOrigin = normalize(-center);
        isVisible = abs(toOrigin.z) <= MinUprightDot;
        up = vec3(0.0, 0.0, 1.0);
        right = normalize(vec3(toOrigin.y, -toOrigin.x, 0.0));
    }
    else if (orientation == 3)
    {
        right = vec3(0.0);
        up = vec3(0.0);
    }
    else if (orientation == 4)
    {
        float sinRoll = sin(vSpriteParams.x);
        float cosRoll = cos(vSpriteParams.x);
        right = viewRight * cosRoll + viewUp * sinRoll;
        up = viewRight * -sinRoll + viewUp * cosRoll;
    }
    else if (orientation == 5)
    {
        vec3 forward = toCenter / cameraDistance;
        right = normalize(cross(forward, vec3(0.0, 0.0, 1.0)));
        up = cross(right, forward);
    }

    float sizeScale = vSpriteParams.y > 0.0 ? vSpriteParams.y * cameraDistance : 1.0;
    vec3 position = center + (sizeScale * ((vSpriteBillboard.x * right) + (vSpriteBillboard.y * up)));

    vFragPosition = position;
    gl_Position = isVisible ? g_matWorldToProjection * vec4(position, 1.0) : vec4(2.0, 2.0, 2.0, 1.0);

    float fade = GetFade(vSpriteParams.z, cameraDistance);

    vTexCoordOut = GetSource1BaseTexCoord(vTEXCOORD);
    vVertexColorOut = vec4(vCOLOR.rgb * fade, vCOLOR.a);
    vGlowProxyOut = vec4(center, vSpriteFade.w);}
