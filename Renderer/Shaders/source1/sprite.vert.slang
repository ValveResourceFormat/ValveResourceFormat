#version 460

#include "sprite_features.slang"
#include "common_vs.slang"
#include "../common/sprite_billboard.slang"

// Corner of the sprite along its right and up axes, and the orientation: 0 turns around the up axis to face the
// view direction, 1 does so toward the world origin, 2 is parallel to the view plane, 3 stays as it is, 4 is parallel
// to the view plane and rolled, 5 faces the camera. 6 is a point of a beam: x is the offset across the beam, which turns
// around its axis to face the camera, and y is how far along the beam the point is. 7 is the halo at the start of a beam,
// parallel to the view plane and shown to a viewer in front of the beam.
in vec3 vTEXCOORD1;

// Roll in radians (the width of the beam for orientations 6 and 7), how much of the distance to the camera the size
// grows by (zero for a fixed size in the world), the fade: 1 fades with the distance like a glow, 2 fades with the
// distances of vTEXCOORD4, and the frames per second of the animation of a texture sheet
in vec4 vTEXCOORD2;

// Distances where a light glow starts to show, is at full strength, and is gone, and the radius of what has to be in
// view for a glow to show, which is zero for a sprite that does not check. For orientations 6 and 7 the first three
// are the vector from the start of the beam to its end.
in vec4 vTEXCOORD4;

const float GlowFullDistance = 1200.0;
const float MinUprightDot = 0.999848;

// A beam starts to fade this many widths from the line it lies on
const float BeamNearLineFactor = 4.0;

out vec3 vFragPosition;
out vec2 vTexCoordOut;
out vec4 vVertexColorOut;
flat out vec4 vGlowProxyOut;

float GetFade(float mode, float cameraDistance)
{
    if (mode == 1.0)
    {
        return saturate((GlowFullDistance * GlowFullDistance) / (cameraDistance * cameraDistance));
    }

    if (mode == 2.0)
    {
        float minDistance = vTEXCOORD4.x;
        float maxDistance = vTEXCOORD4.y;
        float outerDistance = vTEXCOORD4.z;

        if (outerDistance > maxDistance && cameraDistance > maxDistance)
        {
            return RemapValClamped(cameraDistance, maxDistance, outerDistance, 1.0, 0.0);
        }

        return maxDistance > minDistance ? RemapValClamped(cameraDistance, minDistance, maxDistance, 0.0, 1.0) : 1.0;
    }

    return 1.0;
}

// The cell of the sheet the animation is on. The last frame is only shown at the instant the animation wraps, so the
// animation loops over the frames before it.
vec2 GetSheetCell()
{
    float framerate = vTEXCOORD2.w;
    float maxFrame = float(g_nNumAnimationCells) - 1.0;

    if (framerate <= 0.0 || maxFrame <= 1.0)
    {
        return vec2(0.0);
    }

    uint frame = uint(mod(g_flTime * framerate, maxFrame));
    uint columns = uint(g_vAnimationGrid.x);

    return vec2(float(frame % columns), float(frame / columns));
}

void main()
{
    vec3 center = vec4(vPOSITION, 1.0) * CalculateSkinTransform();
    vec3 toCenter = center - g_vCameraPositionWs;
    float cameraDistance = length(toCenter);

    vec3 viewForward = g_vCameraDirWs;
    vec3 viewUp = g_vCameraUpDirWs;
    vec3 viewRight = cross(viewForward, viewUp);

    int orientation = int(vTEXCOORD1.z + 0.5);
    vec3 right = viewRight;
    vec3 up = viewUp;
    bool isVisible = true;
    float beamFade = 1.0;
    float beamSize = 1.0;

    if (orientation == 0)
    {
        isVisible = abs(viewForward.z) <= MinUprightDot;
        up = vec3(0.0, 0.0, 1.0);
        right = GetUprightBillboardRight(viewForward);
    }
    else if (orientation == 1)
    {
        vec3 toOrigin = normalize(-center);
        isVisible = abs(toOrigin.z) <= MinUprightDot;
        up = vec3(0.0, 0.0, 1.0);
        right = GetUprightBillboardRight(toOrigin);
    }
    else if (orientation == 3)
    {
        right = vec3(0.0);
        up = vec3(0.0);
    }
    else if (orientation == 4)
    {
        float sinRoll = sin(vTEXCOORD2.x);
        float cosRoll = cos(vTEXCOORD2.x);
        right = viewRight * cosRoll + viewUp * sinRoll;
        up = viewRight * -sinRoll + viewUp * cosRoll;
    }
    else if (orientation == 5)
    {
        vec3 forward = toCenter / cameraDistance;
        right = normalize(cross(forward, vec3(0.0, 0.0, 1.0)));
        up = cross(right, forward);
    }
    else if (orientation == 6)
    {
        vec3 beamDelta = vTEXCOORD4.xyz;
        vec3 beamStart = center - (vTEXCOORD1.y * beamDelta);
        float beamWidth = vTEXCOORD2.x;
        float lineDistance = length(cross(normalize(beamDelta), g_vCameraPositionWs - beamStart));

        // The beam fades out as the camera gets near the line it lies on
        beamFade = RemapValClamped(lineDistance, beamWidth * BeamNearLineFactor, beamWidth, 1.0, 0.0);

        vec3 side = cross(beamDelta, toCenter);
        right = side / max(length(side), 0.0001);
        up = vec3(0.0);
    }
    else if (orientation == 7)
    {
        vec3 beamAxis = normalize(vTEXCOORD4.xyz);
        float beamWidth = vTEXCOORD2.x;
        float lineDistance = length(cross(beamAxis, g_vCameraPositionWs - center));

        // The halo only shows to a viewer in front of the beam, and grows as the camera gets near its line
        float facing = saturate(2.0 * dot(beamAxis, normalize(g_vCameraPositionWs - center)));
        beamFade = facing * facing;
        beamSize = RemapValClamped(lineDistance, beamWidth * BeamNearLineFactor, beamWidth * 0.5, 1.0, 2.0);
    }

    float sizeScale = vTEXCOORD2.y > 0.0 ? vTEXCOORD2.y * cameraDistance : 1.0;
    sizeScale *= beamSize;
    vec3 position = center + (sizeScale * ((vTEXCOORD1.x * right) + (vTEXCOORD1.y * up)));

    vFragPosition = position;
    gl_Position = isVisible ? g_matWorldToProjection * vec4(position, 1.0) : vec4(2.0, 2.0, 2.0, 1.0);

    float fade = GetFade(vTEXCOORD2.z, cameraDistance);

    vec2 texCoord = GetSource1BaseTexCoord(vTEXCOORD);

    // A texture the material animates is stepped through by GetSource1BaseTexCoord
    if (F_TEXTURE_ANIMATION == 0)
    {
        texCoord = (texCoord + GetSheetCell()) / g_vAnimationGrid;
    }

    vTexCoordOut = texCoord;
    vVertexColorOut = vec4(vCOLOR.rgb * fade * beamFade, vCOLOR.a);
    vGlowProxyOut = vec4(center, vTEXCOORD4.w);
}
