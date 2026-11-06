#version 460

#ifndef VRF_SOURCE1_EYES_PS_INCLUDE
#define VRF_SOURCE1_EYES_PS_INCLUDE

#include "eyes_common_ps.slang"

// Center of the eyeball in world space
flat in vec3 vEyeOrigin;
// Planar projection of the iris texture, as the dot product of these with the world position and 1
flat in vec4 vIrisProjectionU;
flat in vec4 vIrisProjectionV;

vec2 GetSource1IrisCoords(vec3 position)
{
    vec4 position4 = vec4(position, 1.0);
    return vec2(dot(vIrisProjectionU, position4), dot(vIrisProjectionV, position4));
}

#endif
