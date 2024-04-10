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

        targetPosition = ((viewDir * wallEyeInfluence) * distance(targetPosition, resultMatrix[3].xyz)) + resultMatrix[3].xyz;
        viewDir = normalize(targetPosition - resultMatrix[3].xyz);
    }

    const float radians40 = radians(40.0);
    const float sin40 = sin(radians40);
    const float cos40 = cos(radians40);
    const float oneMinusCos40 = 1.0 - cos40;

    vec3 testVector = normalize(fwdVecTransformed.xyz + (upVecTransformed.xyz * 0.5));
    if (acos(dot(viewDir, testVector)) > radians40)
    {
        vec3 _17148 = normalize(cross(testVector, viewDir));
        float _25223 = _17148.x;
        float _6863 = _17148.y;
        float _13016 = _17148.z;
        float _12706 = _25223 * _6863;
        float _14954 = _13016 * _25223;
        float _12707 = _6863 * _13016;

        // todo: extract to helper method
        mat3 rotation = mat3(
            vec3(fma(_25223, _25223, fma(-_25223, _25223, 1.0) * cos40), fma(_12706, oneMinusCos40, _13016 * (-sin40)), fma(_14954, oneMinusCos40, _6863 * sin40)),
            vec3(fma(_12706, oneMinusCos40, _13016 * sin40), fma(_6863, _6863, fma(-_6863, _6863, 1.0) * cos40), fma(_12707, oneMinusCos40, _25223 * (-sin40))),
            vec3(fma(_14954, oneMinusCos40, _6863 * (-sin40)), fma(_12707, oneMinusCos40, _25223 * sin40), fma(_13016, _13016, fma(-_13016, _13016, 1.0) * cos40))
        );

        targetPosition = ((testVector * rotation) * distance(targetPosition, resultMatrix[3].xyz)) + resultMatrix[3].xyz;
        viewDir = normalize(targetPosition - resultMatrix[3].xyz);
    }

    vec3 testVector2 = normalize(fwdVecTransformed.xyz + (upVecTransformed.xyz * (-0.5)));
    if (acos(dot(viewDir, testVector2)) > radians40)
    {
        vec3 _17148 = normalize(cross(testVector2, viewDir));
        float _25223 = _17148.x;
        float _6863 = _17148.y;
        float _13016 = _17148.z;
        float _12706 = _25223 * _6863;
        float _14954 = _13016 * _25223;
        float _12707 = _6863 * _13016;

        // todo: extract to helper method
        mat3 rotation = mat3(
            vec3(fma(_25223, _25223, fma(-_25223, _25223, 1.0) * cos40), fma(_12706, oneMinusCos40, _13016 * (-sin40)), fma(_14954, oneMinusCos40, _6863 * sin40)),
            vec3(fma(_12706, oneMinusCos40, _13016 * sin40), fma(_6863, _6863, fma(-_6863, _6863, 1.0) * cos40), fma(_12707, oneMinusCos40, _25223 * (-sin40))),
            vec3(fma(_14954, oneMinusCos40, _6863 * (-sin40)), fma(_12707, oneMinusCos40, _25223 * sin40), fma(_13016, _13016, fma(-_13016, _13016, 1.0) * cos40))
        );

        vec3 targetPosition2 = ((testVector2 * rotation) * distance(targetPosition, resultMatrix[3].xyz)) + resultMatrix[3].xyz;
        viewDir = normalize(targetPosition2 - resultMatrix[3].xyz);
    }

    vec3 outVector1 = cross(resultMatrix[2].xyz, viewDir);
    vec3 outVector2 = cross(viewDir, outVector1);
}

// Fragment
{
    const float lodBias = -0.5;
    const float flEyeMask = texture(g_tEyeMask1, _5761.xy, lodBias).r

    SPIRV_CROSS_BRANCH
    if (flEyeMask > 0.0)
    {
        vec3 _8784 = normalize(_3986.xyz);
        vec3 _13745 = vec3(_3986.w, _3987.w, _3988.w);
        vec3 _7850 = normalize(_7635 - _4459._m0);
        vec3 _8097 = _4459._m0 - _13745;
        float _11278 = dot(_8097, _7850);
        float _15588 = fma(_11278, _11278, -fma(-_3694._m16, _3694._m16, dot(_8097, _8097)));
        float _24028 = (_15588 > 0.0) ? ((-_11278) - sqrt(_15588)) : 0.0;

        SPIRV_CROSS_BRANCH
        if (_24028 > 0.0)
        {
            vec3 _14552 = normalize((_4459._m0 + (_7850 * _24028)) - _13745);
            vec2 _20286 = vec2(dot(_14552, normalize(_3987.xyz)), dot(_14552, normalize(-_3988.xyz)));
            float _20490 = length(_20286);
            vec2 _18020 = mix(_20286 * (2.0 - _3694._m17), _20286, vec2(_20490));
            vec4 vEyeTexel = texture(sampler2D(_5859, _3819), (_18020 * 0.5) + vec2(0.5), lodBias);
            vec3 vEyeAlbedo = vEyeTexel.rgb;
            float _12442 = cos(_3694._m19);
            vec3 vPupilLerp = vec3(vEyeTexel.a);

            const vec3 tan30deg = vec3(tan(radians(30.0)));

            vec3 _16529 = mix(vEyeAlbedo, ((vEyeAlbedo * _12442) + (cross(tan30deg, vEyeAlbedo) * sin(_3694._m19))) + ((tan30deg * dot(tan30deg, vEyeAlbedo)) * (1.0 - _12442)), vPupilLerp);
            float _18919 = saturate(dot(_8784, _14552));

            vec3 vEyeColor = (mix(_16529, mix(GetLuma(_16529).xxx, _16529, vec3(_3694._m20)), vPupilLerp) * smoothstep(_3694._m18, _3694._m18 + 0.03, _20490)) * _18919;

            if (abs(dFdx(_18020.x)) > 0.15)
            {
                vEyeColor = mix(vEyeColor, (vEyeColor * 0.8) + smoothstep(0.1, 0.08, length(_18020 - vec2(_3694._m18, -_3694._m18))).xxx, saturate((distance(_4459._m0, _7635) - 100.0) * 0.05).xxx);
            }

            float flEyeLerp = flEyeMask * smoothstep(0.1, 0.3, _18919);

            mat.Albedo =        mix(mat.Albedo, vEyeColor, vec3(flEyeLerp));
            mat.RoughnessTex =  mix(mat.RoughnessTex, vec2(0.1), vec2(flEyeLerp));
            mat.NormalMap =     mix(mat.NormalMap, normalize(mix(_14552, normalize(_14552 - (_8784 * 0.5)), vPupilLerp)), vec3(pow(flEyeLerp, 0.5)));
            mat.NormalMap =     normalize(mat.NormalMap);
        }
    }
}
