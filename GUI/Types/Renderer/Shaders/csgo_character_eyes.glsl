#version 460
//? #include "common/animation.glsl"

// Left eye
uniform vec3 g_vEyeLBindFwd
uniform vec3 g_vEyeLBindUp
uniform vec3 g_vEyeLBindPos
uniform uint g_nEyeLBindIdx
uniform float g_flEyeBallWalleyeL1;

// Right eye
uniform vec3 g_vEyeRBindFwd
uniform vec3 g_vEyeRBindUp
uniform vec3 g_vEyeRBindPos
uniform uint g_nEyeRBindIdx
uniform float g_flEyeBallWalleyeR1;

// View target
uniform vec3 g_vEyeTargetBindPos;
uniform uint g_nEyeTargetBindIdx;

struct EyeInput
{
    vec3 Forward;
    vec3 Up;
    vec3 Position;
    uint BoneIndex;
};

{
    EyeInput leftEyeOs;
    leftEyeOs.Forward = g_vEyeLBindFwd;
    leftEyeOs.Up = g_vEyeLBindUp;
    leftEyeOs.Position = g_vEyeLBindPos;
    leftEyeOs.BoneIndex = g_nEyeLBindIdx;

    EyeInput rightEyeOs;
    rightEyeOs.Forward = g_vEyeRBindFwd;
    rightEyeOs.Up = g_vEyeRBindUp;
    rightEyeOs.Position = g_vEyeRBindPos;
    rightEyeOs.BoneIndex = g_nEyeRBindIdx;

    bool bIsRightEye = distance(vPositionOs, rightEyeOs.Position) < distance(vPositionOs, leftEyeOs.Position);

    EyeInput currentEye = bIsRightEye ? rightEyeOs : leftEyeOs;
    float flExotropia = bIsRightEye ? (g_flEyeBallWalleyeR1 * -1.0) : g_flEyeBallWalleyeL1;

    mat4 eyeAnimationMatrix = getMatrix(currentEye.BoneIndex);
    mat3 eyeRotationMatrix = mat3(eyeAnimationMatrix);

    mat4 resultMatrix = mat4(1.0);
    resultMatrix[0] = vec4(currentEye.Forward * eyeRotationMatrix, 0.0);
    resultMatrix[2] = vec4(currentEye.Up * eyeRotationMatrix, 0.0);
    resultMatrix[3] = vec4(currentEye.Position, 1.0) * eyeAnimationMatrix;

    // todo: animatedEye .Position .Rotation .Transform

    vec3 targetPosition = vec4(g_vEyeTargetBindPos, 1.0) * getMatrix(g_nEyeTargetBindIdx);
    vec3 viewDir = normalize(targetPosition - resultMatrix[3].xyz);

    if (flExotropia != 0.0)
    {
        flExotropia = radians(flExotropia);
        float sinRot = sin(flExotropia);
        float cosRot = cos(flExotropia);

        float _15300 = resultMatrix[2].x * resultMatrix[2].y;
        float _12989 = 1.0 - cosRot;
        float _10616 = resultMatrix[2].z * sinRot;
        float _14840 = resultMatrix[2].z * resultMatrix[2].x;
        float _16712 = resultMatrix[2].y * sinRot;
        float _12705 = resultMatrix[2].y * resultMatrix[2].z;
        float _16077 = resultMatrix[2].x * sinRot;

        mat3 wallEyeInfluence = mat3(
            vec3(fma(resultMatrix[2].x, resultMatrix[2].x, fma(-resultMatrix[2].x, resultMatrix[2].x, 1.0) * cosRot), fma(_15300, _12989, -_10616), fma(_14840, _12989, _16712)),
            vec3(fma(_15300, _12989, _10616), fma(resultMatrix[2].y, resultMatrix[2].y, fma(-resultMatrix[2].y, resultMatrix[2].y, 1.0) * cosRot), fma(_12705, _12989, -_16077)),
            vec3(fma(_14840, _12989, -_16712), fma(_12705, _12989, _16077), fma(resultMatrix[2].z, resultMatrix[2].z, fma(-resultMatrix[2].z, resultMatrix[2].z, 1.0) * cosRot))
        );

        vec3 targetPosition = ((viewDir * wallEyeInfluence) * distance(targetPosition, resultMatrix[3].xyz)) + resultMatrix[3].xyz;
        viewDir = normalize(targetPosition - resultMatrix[3].xyz);
    }
}
