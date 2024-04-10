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

    vec4 eyeViewDirOut =  vec4(viewDir,                             resultMatrix[3].x);
    vec4 eyeRightDirOut = vec4(cross(resultMatrix[2].xyz, viewDir), resultMatrix[3].y);
    vec4 eyeUpDirOut =    vec4(cross(viewDir, eyeRightDirOut.xyz),  resultMatrix[3].z);
}

// Fragment
{
    const float lodBias = -0.5;
    const float flEyeMask = texture(g_tEyeMask1, vTexCoordOut.xy, lodBias).r

    if (flEyeMask > 0.0)
    {
        vec3 vEyeBallViewDir = normalize(eyeViewDirOut.xyz);
        vec3 vEyeBallPosition = vec3(eyeViewDirOut.w, eyeRightDirOut.w, eyeUpDirOut.w);
        vec3 vEyeBallViewSpace = g_vCameraPositionWs - vEyeBallPosition;

        vec3 viewDirInv = -mat.ViewDir;
        float _11278 = dot(vEyeBallViewSpace, viewDirInv);
        float _15588 = fma(_11278, _11278, -fma(-g_flEyeBallRadius1, g_flEyeBallRadius1, dot(vEyeBallViewSpace, vEyeBallViewSpace)));
        float flWithinEyeBallSphere = (_15588 > 0.0) ? ((-_11278) - sqrt(_15588)) : 0.0;

        if (flWithinEyeBallSphere > 0.0)
        {
            const vec3 rightDir = normalize(eyeRightDirOut.xyz);
            const vec3 upDir = normalize(-eyeUpDirOut.xyz);

            vec3 _14552 = normalize((g_vCameraPositionWs + (viewDirInv * flWithinEyeBallSphere)) - vEyeBallPosition);
            vec2 _20286 = vec2(dot(_14552, rightDir), dot(_14552, upDir));

            float _20490 = length(_20286);
            vec2 _18020 = mix(_20286 * (2.0 - g_flEyeIrisSize1), _20286, vec2(_20490));
            vec4 vEyeTexel = texture(g_tEyeAlbedo1, (_18020 * 0.5) + vec2(0.5), lodBias);

            vec3 vIrisMask = vec3(vEyeTexel.a);

            vec3 vEyeColor = vEyeTexel.rgb;

            // Iris hue shift
            const float cosHue = cos(g_flEyeHueShift1);
            const float sinHue = sin(g_flEyeHueShift1);
            const vec3 tan30deg = vec3(tan(radians(30.0)));

            vec3 vHueShifted = ((vEyeColor * cosHue) + (cross(tan30deg, vEyeColor) * sinHue)) + ((tan30deg * dot(tan30deg, vEyeColor)) * (1.0 - cosHue));
            vEyeColor = mix(vEyeColor, vHueShifted, vIrisMask);

            // Iris saturation
            vec3 vSaturated = mix(GetLuma(vEyeColor).xxx, vEyeColor, vec3(g_flEyeSaturation1));
            vEyeColor = mix(vEyeColor, vSaturated, vIrisMask);

            vEyeColor *= smoothstep(g_flEyePupilSize1, g_flEyePupilSize1 + 0.03, _20490);

            float _18919 = saturate(dot(vEyeBallViewDir, _14552));
            vEyeColor *= _18919;

            if (abs(dFdx(_18020.x)) > 0.15)
            {
                vEyeColor = mix(
                    vEyeColor,
                    (vEyeColor * 0.8) + smoothstep(0.1, 0.08, length(_18020 - vec2(g_flEyePupilSize1, -g_flEyePupilSize1))).xxx,
                    saturate((distance(g_vCameraPositionWs, mat.PositionWs) - 100.0) * 0.05).xxx
                );
            }

            float flEyeLerp = flEyeMask * smoothstep(0.1, 0.3, _18919);

            mat.Albedo =        mix(mat.Albedo, vEyeColor, vec3(flEyeLerp));
            mat.RoughnessTex =  mix(mat.RoughnessTex, vec2(0.1), vec2(flEyeLerp));
            mat.NormalMap =     mix(mat.NormalMap, normalize(mix(_14552, normalize(_14552 - (vEyeBallViewDir * 0.5)), vIrisMask)), vec3(pow(flEyeLerp, 0.5)));
            mat.NormalMap =     normalize(mat.NormalMap);
        }
    }
}
