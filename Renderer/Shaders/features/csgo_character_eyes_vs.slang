#version 460

#ifndef VRF_CSGO_CHARACTER_EYES_VS_INCLUDE
#define VRF_CSGO_CHARACTER_EYES_VS_INCLUDE

#include "../common/animation.slang"
#include "csgo_character_eyes_common.slang"

#if (F_EYEBALLS == 1)
// Left eye
uniform vec3 g_vEyeLBindFwd;
uniform vec3 g_vEyeLBindUp;
uniform vec3 g_vEyeLBindPos;
uniform int g_nEyeLBindIdx;
uniform float g_flEyeBallWalleyeL1;

// Right eye
uniform vec3 g_vEyeRBindFwd;
uniform vec3 g_vEyeRBindUp;
uniform vec3 g_vEyeRBindPos;
uniform int g_nEyeRBindIdx;
uniform float g_flEyeBallWalleyeR1;

struct EyeData
{
    vec3 Forward;
    vec3 Up;
    vec3 Position;
    int BoneIndex;
};

// View target
uniform vec3 g_vEyeTargetBindPos;
uniform int g_nEyeTargetBindIdx;

out EyePixelInput eyeInterpolator;

mat3 RotationMatrix(vec3 axis, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    float oc = 1.0 - c;

    return mat3(oc * axis.x * axis.x + c,           oc * axis.x * axis.y - axis.z * s,  oc * axis.z * axis.x + axis.y * s,
                oc * axis.x * axis.y + axis.z * s,  oc * axis.y * axis.y + c,           oc * axis.y * axis.z - axis.x * s,
                oc * axis.z * axis.x - axis.y * s,  oc * axis.y * axis.z + axis.x * s,  oc * axis.z * axis.z + c);
}

// Turns the view direction back to the edge of a 40 degree cone around coneAxis when it leaves it.
void ConstrainEyeDirection(vec3 coneAxis, vec3 eyePosition, inout vec3 targetPosition, inout vec3 viewDir)
{
    const float maxAngle = radians(40.0);

    if (acos(dot(viewDir, coneAxis)) > maxAngle)
    {
        vec3 axis = normalize(cross(coneAxis, viewDir));
        mat3 rotation = RotationMatrix(axis, maxAngle);

        targetPosition = ((coneAxis * rotation) * distance(targetPosition, eyePosition)) + eyePosition;
        viewDir = normalize(targetPosition - eyePosition);
    }
}

EyePixelInput GetCharacterEyeInterpolator(vec3 vPositionOs)
{
    EyeData leftEyeOs;
    leftEyeOs.Forward = g_vEyeLBindFwd;
    leftEyeOs.Up = g_vEyeLBindUp;
    leftEyeOs.Position = g_vEyeLBindPos;
    leftEyeOs.BoneIndex = g_nEyeLBindIdx;

    EyeData rightEyeOs;
    rightEyeOs.Forward = g_vEyeRBindFwd;
    rightEyeOs.Up = g_vEyeRBindUp;
    rightEyeOs.Position = g_vEyeRBindPos;
    rightEyeOs.BoneIndex = g_nEyeRBindIdx;

    bool bIsRightEye = distance(vPositionOs, rightEyeOs.Position) < distance(vPositionOs, leftEyeOs.Position);

    EyeData currentEye = bIsRightEye ? rightEyeOs : leftEyeOs;
    float flExotropia = bIsRightEye ? (g_flEyeBallWalleyeR1 * -1.0) : g_flEyeBallWalleyeL1;

    uint objectBoneOffset = GetObjectBoneOffset();
    mat3x4 eyeAnimationMatrix = GetMatrix(currentEye.BoneIndex, objectBoneOffset);

    currentEye.Forward =    vec4(currentEye.Forward, 0.0) * eyeAnimationMatrix;
    currentEye.Up =         vec4(currentEye.Up, 0.0) * eyeAnimationMatrix;
    currentEye.Position =   vec4(currentEye.Position, 1.0) * eyeAnimationMatrix;

    vec3 targetPosition = vec4(g_vEyeTargetBindPos, 1.0) * GetMatrix(g_nEyeTargetBindIdx, objectBoneOffset);
    vec3 viewDir = normalize(targetPosition - currentEye.Position);

    if (flExotropia != 0.0)
    {
        flExotropia = radians(flExotropia);

        mat3 wallEyeInfluence = RotationMatrix(currentEye.Up, flExotropia);
        targetPosition = ((viewDir * wallEyeInfluence) * distance(targetPosition, currentEye.Position)) + currentEye.Position;
        viewDir = normalize(targetPosition - currentEye.Position);
    }

    vec3 upperConeAxis = normalize(currentEye.Forward + (currentEye.Up * 0.5));
    ConstrainEyeDirection(upperConeAxis, currentEye.Position, targetPosition, viewDir);

    vec3 lowerConeAxis = normalize(currentEye.Forward + (currentEye.Up * (-0.5)));
    ConstrainEyeDirection(lowerConeAxis, currentEye.Position, targetPosition, viewDir);

    EyePixelInput o;
    o.vEyeViewDir_PosX =  vec4(viewDir,                                 currentEye.Position.x);
    o.vEyeRightDir_PosY = vec4(cross(currentEye.Up, viewDir),           currentEye.Position.y);
    o.vEyeUpDir_PosZ =    vec4(cross(viewDir, o.vEyeRightDir_PosY.xyz), currentEye.Position.z);
    return o;
}
#endif // (F_EYEBALLS == 1)

#endif
