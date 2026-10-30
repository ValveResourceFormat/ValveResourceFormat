#version 460

#include "detailsprite_features.slang"
#include "common_vs.slang"

// Corner of the sprite along its right and up axes, and the orientation: 0 keeps the position as it is,
// 1 faces the camera, 2 turns around the up axis to face the camera
in vec3 vSpriteBillboard;

// Squared distances where the sprites start to fade and where they are gone
uniform float g_flDetailFadeStartSq = 640000.0;
uniform float g_flDetailFadeEndSq = 1440000.0;

out vec3 vFragPosition;
out vec2 vTexCoordOut;
out vec4 vVertexColorOut;

void main()
{
    vec3 position = vec4(vPOSITION, 1.0) * CalculateSkinTransform();
    vec3 toCamera = g_vCameraPositionWs - position;
    float distanceSq = dot(toCamera, toCamera);

    int orientation = int(vSpriteBillboard.z + 0.5);

    if (orientation != 0)
    {
        if (orientation == 2)
        {
            toCamera.z = 0.0;
        }

        // Source right is -Y when the sprite looks along +X
        vec3 forward = normalize(toCamera);
        vec3 right = normalize(cross(forward, vec3(0.0, 0.0, 1.0)));
        vec3 up = cross(right, forward);

        position += (vSpriteBillboard.x * right) + (vSpriteBillboard.y * up);
    }

    vFragPosition = position;
    gl_Position = g_matWorldToProjection * vec4(position, 1.0);

    float fade = clamp((g_flDetailFadeEndSq - distanceSq) / (g_flDetailFadeEndSq - g_flDetailFadeStartSq), 0.0, 1.0);

    vTexCoordOut = GetSource1BaseTexCoord(vTEXCOORD);
    vVertexColorOut = vec4(vCOLOR.rgb, vCOLOR.a * fade);
}
