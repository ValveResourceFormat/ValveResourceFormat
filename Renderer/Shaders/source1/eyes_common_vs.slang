#version 460

#ifndef VRF_SOURCE1_EYES_COMMON_VS_INCLUDE
#define VRF_SOURCE1_EYES_COMMON_VS_INCLUDE

#include "common_vs.slang"
#include "../features/eye_view_cone.slang"

// The eyeball in the space of the vertices, which its bone carries into the world
uniform int g_nEyeBoneIndex;
uniform vec3 g_vEyeBindOrigin;
uniform vec3 g_vEyeBindUp = vec3(0.0, 0.0, 1.0);
uniform vec3 g_vEyeBindForward = vec3(1.0, 0.0, 0.0);
// How far the eye looks to the side of its target, and the size of the iris relative to the eyeball
uniform float g_flEyeZOffset;
uniform float g_flEyeIrisScale = 1.0;

flat out vec3 vEyeOrigin;
flat out vec4 vIrisProjectionU;
flat out vec4 vIrisProjectionV;

mat3x4 GetEyeToWorldMatrix()
{
#if (D_SKINNING == 0)
    return CalculateObjectToWorldMatrix();
#else
    return ConcatTransforms(GetMatrix(uint(g_nEyeBoneIndex), GetObjectBoneOffset()), CalculateObjectToWorldMatrix());
#endif
}

// The view target, a point the eyes look at that is carried by a bone
uniform vec3 g_vEyeTargetBindPos;
uniform int g_nEyeTargetBindIdx = -1;

// Turns the eye to the view target within the cones the eye can look in
vec3 AimEyeAtTarget(vec3 origin, vec3 up, vec3 forward)
{
#if (D_SKINNING != 0)
    if (g_nEyeTargetBindIdx < 0)
    {
        return forward;
    }

    mat3x4 targetToWorld = ConcatTransforms(GetMatrix(uint(g_nEyeTargetBindIdx), GetObjectBoneOffset()), CalculateObjectToWorldMatrix());
    vec3 targetPosition = vec4(g_vEyeTargetBindPos, 1.0) * targetToWorld;
    vec3 viewDir = normalize(targetPosition - origin);

    vec3 upperConeAxis = normalize(forward + (up * 0.5));
    ConstrainEyeDirection(upperConeAxis, origin, targetPosition, viewDir);

    vec3 lowerConeAxis = normalize(forward + (up * (-0.5)));
    ConstrainEyeDirection(lowerConeAxis, origin, targetPosition, viewDir);

    return viewDir;
#else
    return forward;
#endif
}

// Places the eye in the world and aims it at the view target. The iris texture is projected on the plane the eye faces,
// as the dot product of the projection vectors with the world position and 1.
void CalculateSource1EyeProjection()
{
    mat3x4 eyeToWorld = GetEyeToWorldMatrix();

    vec3 origin = vec4(g_vEyeBindOrigin, 1.0) * eyeToWorld;
    vec3 up = normalize(TransformDirectionToWorld(eyeToWorld, g_vEyeBindUp));
    vec3 forward = normalize(TransformDirectionToWorld(eyeToWorld, g_vEyeBindForward));
    forward = AimEyeAtTarget(origin, up, forward);

    vec3 right = normalize(cross(forward, up));
    forward = normalize(forward + right * (g_flEyeZOffset * 2.0));
    right = normalize(cross(forward, up));
    up = normalize(cross(right, forward));

    vec3 projectionU = right * -g_flEyeIrisScale;
    vec3 projectionV = up * -g_flEyeIrisScale;

    vEyeOrigin = origin;
    vIrisProjectionU = vec4(projectionU, 0.5 - dot(origin, projectionU));
    vIrisProjectionV = vec4(projectionV, 0.5 - dot(origin, projectionV));
}

#endif
