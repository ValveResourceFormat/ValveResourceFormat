#version 460

#ifndef VRF_CSGO_CHARACTER_EYES_PS_INCLUDE
#define VRF_CSGO_CHARACTER_EYES_PS_INCLUDE

#include "csgo_character_eyes_common.slang"

#if (GameVfx_csgo_character != 0) && (F_EYEBALLS == 1) && (D_ANIMATED == 1)
uniform sampler2D g_tEyeAlbedo1; // SrgbRead(true)
uniform sampler2D g_tEyeMask1;

uniform float g_flEyeBallRadius1;
uniform float g_flEyeHueShift1;
uniform float g_flEyeIrisSize1 = 1.0;
uniform float g_flEyePupilSize1;
uniform float g_flEyeSaturation1 = 1.0;

in EyePixelInput eyeInterpolator;

void ApplyEye(EyePixelInput i, vec2 texCoord, inout MaterialProperties_t mat)
{
    const float lodBias = -0.5;
    const float flEyeMask = texture(g_tEyeMask1, vTexCoordOut.xy, lodBias).r;

    if (flEyeMask > 0.0)
    {
        vec3 vEyeBallViewDir = normalize(i.vEyeViewDir_PosX.xyz);
        vec3 vEyeBallPosition = vec3(i.vEyeViewDir_PosX.w, i.vEyeRightDir_PosY.w, i.vEyeUpDir_PosZ.w);
        vec3 vEyeBallViewSpace = g_vCameraPositionWs - vEyeBallPosition;

        vec3 viewDirInv = -mat.ViewDir;
        float _11278 = dot(vEyeBallViewSpace, viewDirInv);
        float _15588 = fma(_11278, _11278, -fma(-g_flEyeBallRadius1, g_flEyeBallRadius1, dot(vEyeBallViewSpace, vEyeBallViewSpace)));
        float flWithinEyeBallSphere = (_15588 > 0.0) ? ((-_11278) - sqrt(_15588)) : 0.0;

        //mat.Albedo = _11278.xxx;

        if (flWithinEyeBallSphere > 0.0)
        {
            const vec3 rightDir = normalize(i.vEyeRightDir_PosY.xyz);
            const vec3 upDir = normalize(-i.vEyeUpDir_PosZ.xyz);

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
                    saturate((distance(g_vCameraPositionWs, mat.PositionWS) - 100.0) * 0.05).xxx
                );
            }

            float flEyeLerp = flEyeMask * smoothstep(0.1, 0.3, _18919);

            mat.Albedo =     mix(mat.Albedo, vEyeColor, vec3(flEyeLerp));
            mat.Roughness =  mix(mat.Roughness, vec2(0.1), vec2(flEyeLerp));
            mat.Normal =     mix(mat.Normal, normalize(mix(_14552, normalize(_14552 - (vEyeBallViewDir * 0.5)), vIrisMask)), vec3(pow(flEyeLerp, 0.5)));
            mat.Normal =     normalize(mat.Normal);
        }
    }
}
#endif // (GameVfx_csgo_character != 0) && (F_EYEBALLS == 1)

#endif
