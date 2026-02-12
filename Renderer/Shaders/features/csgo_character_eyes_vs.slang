#version 460

import common.animation;
import features.csgo_character_eyes_common;

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

mat3 rotationMatrix(vec3 axis, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    float oc = 1.0 - c;

    return mat3(oc * axis.x * axis.x + c,           oc * axis.x * axis.y - axis.z * s,  oc * axis.z * axis.x + axis.y * s,
                oc * axis.x * axis.y + axis.z * s,  oc * axis.y * axis.y + c,           oc * axis.y * axis.z - axis.x * s,
                oc * axis.z * axis.x - axis.y * s,  oc * axis.y * axis.z + axis.x * s,  oc * axis.z * axis.z + c);
}

public EyePixelInput GetCharacterEyeInterpolator(vec3 vPositionOs)
{
    EyeData leftEyeOs;
    leftEyeOs.Forward = g_vEyeLBindFwd.xyz;
    leftEyeOs.Up = g_vEyeLBindUp.xyz;
    leftEyeOs.Position = g_vEyeLBindPos.xyz;
    leftEyeOs.BoneIndex = g_nEyeLBindIdx;

    EyeData rightEyeOs;
    rightEyeOs.Forward = g_vEyeRBindFwd.xyz;
    rightEyeOs.Up = g_vEyeRBindUp.xyz;
    rightEyeOs.Position = g_vEyeRBindPos.xyz;
    rightEyeOs.BoneIndex = g_nEyeRBindIdx;

    bool bIsRightEye = distance(vPositionOs, rightEyeOs.Position) < distance(vPositionOs, leftEyeOs.Position);

    EyeData currentEye = bIsRightEye ? rightEyeOs : leftEyeOs;
    float flExotropia = bIsRightEye ? (g_flEyeBallWalleyeR1 * -1.0) : g_flEyeBallWalleyeL1;

    mat4 eyeAnimationMatrix = getMatrix(uint(currentEye.BoneIndex));
    mat3 eyeRotationMatrix = mat3(eyeAnimationMatrix);

    currentEye.Forward =    eyeRotationMatrix * currentEye.Forward;
    currentEye.Up =         eyeRotationMatrix * currentEye.Up;
    currentEye.Position =   (eyeAnimationMatrix * vec4(currentEye.Position, 1.0)).xyz;

    vec3 targetPosition = (getMatrix(uint(g_nEyeTargetBindIdx)) * vec4(g_vEyeTargetBindPos.xyz, 1.0)).xyz;
    vec3 viewDir = normalize(targetPosition - currentEye.Position);

    if (flExotropia != 0.0)
    {
        flExotropia = radians(flExotropia);

        mat3 wallEyeInfluence = rotationMatrix(currentEye.Up, flExotropia);
        targetPosition = ((viewDir * wallEyeInfluence) * distance(targetPosition, currentEye.Position)) + currentEye.Position;
        viewDir = normalize(targetPosition - currentEye.Position);
    }

    const float radians40 = radians(40.0);

    vec3 testVector = normalize(currentEye.Forward + (currentEye.Up * 0.5));
    if (acos(dot(viewDir, testVector)) > radians40)
    {
        vec3 axis = normalize(cross(testVector, viewDir));
        mat3 rotation = rotationMatrix(axis, radians40);

        targetPosition = ((testVector * rotation) * distance(targetPosition, currentEye.Position)) + currentEye.Position;
        viewDir = normalize(targetPosition - currentEye.Position);
    }

    vec3 testVector2 = normalize(currentEye.Forward + (currentEye.Up * (-0.5)));
    if (acos(dot(viewDir, testVector2)) > radians40)
    {
        vec3 axis = normalize(cross(testVector2, viewDir));
        mat3 rotation = rotationMatrix(axis, radians40);

        vec3 targetPosition2 = ((testVector2 * rotation) * distance(targetPosition, currentEye.Position)) + currentEye.Position;
        viewDir = normalize(targetPosition2 - currentEye.Position);
    }

    EyePixelInput o;
    o.vEyeViewDir_PosX =  vec4(viewDir,                                 currentEye.Position.x);
    o.vEyeRightDir_PosY = vec4(cross(currentEye.Up, viewDir),           currentEye.Position.y);
    o.vEyeUpDir_PosZ =    vec4(cross(viewDir, o.vEyeRightDir_PosY.xyz), currentEye.Position.z);
    return o;
}
