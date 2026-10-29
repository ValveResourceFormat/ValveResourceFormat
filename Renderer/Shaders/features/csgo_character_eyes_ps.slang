#version 460

#ifndef VRF_CSGO_CHARACTER_EYES_PS_INCLUDE
#define VRF_CSGO_CHARACTER_EYES_PS_INCLUDE

#include "csgo_character_eyes_common.slang"

#if (F_EYEBALLS == 1)
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
    float flEyeMask = texture(g_tEyeMask1, vTexCoordOut.xy, lodBias).r;

    if (flEyeMask > 0.0)
    {
        vec3 vEyeBallViewDir = normalize(i.vEyeViewDir_PosX.xyz);
        vec3 vEyeBallPosition = vec3(i.vEyeViewDir_PosX.w, i.vEyeRightDir_PosY.w, i.vEyeUpDir_PosZ.w);
        vec3 vEyeBallViewSpace = g_vCameraPositionWs - vEyeBallPosition;

        vec3 viewDirInv = -mat.ViewDir;
        float flViewProjection = dot(vEyeBallViewSpace, viewDirInv);
        float flSphereDiscriminant = fma(flViewProjection, flViewProjection, -fma(-g_flEyeBallRadius1, g_flEyeBallRadius1, length_squared(vEyeBallViewSpace)));
        float flWithinEyeBallSphere = (flSphereDiscriminant > 0.0) ? ((-flViewProjection) - sqrt(flSphereDiscriminant)) : 0.0;

        if (flWithinEyeBallSphere > 0.0)
        {
            vec3 rightDir = normalize(i.vEyeRightDir_PosY.xyz);
            vec3 upDir = normalize(-i.vEyeUpDir_PosZ.xyz);

            vec3 vSurfaceDir = normalize((g_vCameraPositionWs + (viewDirInv * flWithinEyeBallSphere)) - vEyeBallPosition);
            vec2 vEyePlane = vec2(dot(vSurfaceDir, rightDir), dot(vSurfaceDir, upDir));

            float flEyeRadius = length(vEyePlane);
            vec2 vEyeUv = mix(vEyePlane * (2.0 - g_flEyeIrisSize1), vEyePlane, vec2(flEyeRadius));
            vec4 vEyeTexel = texture(g_tEyeAlbedo1, (vEyeUv * 0.5) + vec2(0.5), lodBias);

            vec3 vIrisMask = vec3(vEyeTexel.a);

            vec3 vEyeColor = vEyeTexel.rgb;

            // Iris hue shift
            vEyeColor = mix(vEyeColor, RotateHue(vEyeColor, g_flEyeHueShift1), vIrisMask);

            // Iris saturation
            vec3 vSaturated = mix(GetLuma(vEyeColor).xxx, vEyeColor, vec3(g_flEyeSaturation1));
            vEyeColor = mix(vEyeColor, vSaturated, vIrisMask);

            vEyeColor *= smoothstep(g_flEyePupilSize1, g_flEyePupilSize1 + 0.03, flEyeRadius);

            float flFacing = saturate(dot(vEyeBallViewDir, vSurfaceDir));
            vEyeColor *= flFacing;

            if (abs(dFdx(vEyeUv.x)) > 0.15)
            {
                vEyeColor = mix(
                    vEyeColor,
                    (vEyeColor * 0.8) + smoothstep(0.1, 0.08, length(vEyeUv - vec2(g_flEyePupilSize1, -g_flEyePupilSize1))).xxx,
                    saturate((distance(g_vCameraPositionWs, mat.PositionWS) - 100.0) * 0.05).xxx
                );
            }

            float flEyeLerp = flEyeMask * smoothstep(0.1, 0.3, flFacing);

            mat.Albedo =     mix(mat.Albedo, vEyeColor, vec3(flEyeLerp));
            mat.Roughness =  mix(mat.Roughness, vec2(0.1), vec2(flEyeLerp));
            mat.Normal =     mix(mat.Normal, normalize(mix(vSurfaceDir, normalize(vSurfaceDir - (vEyeBallViewDir * 0.5)), vIrisMask)), vec3(pow(flEyeLerp, 0.5)));
            mat.Normal =     normalize(mat.Normal);
        }
    }
}
#endif // (F_EYEBALLS == 1)

#endif
