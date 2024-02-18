#version 460

#include "common/utils.glsl"
#include "common/ViewConstants.glsl"

#define renderMode_Color 0
#define renderMode_SpriteEffects 0
#define renderMode_Tint 0

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec2 vTexCoordOut;
centroid in vec4 vColorOut;

layout (location = 0) out vec4 outputColor;

#include "common/translucent.glsl"

#define F_TINT_MASK 0
#define F_DEPTH_FEATHER 0

uniform sampler2D g_tColor; // SrgbRead(true)
uniform sampler2D g_tMask1;
uniform sampler2D g_tMask2;
uniform sampler2D g_tMask3;

#if F_TINT_MASK == 1
    uniform sampler2D g_tTintMask;
#endif

#if F_DEPTH_FEATHER == 1
    uniform sampler2D g_tSceneDepth;

    vec3 GetWorldPositionFromDepth(ivec2 vScreenPosition, vec3 vCameraRay)
    {
        float flSceneDepth = texelFetch(g_tSceneDepth, vScreenPosition, 0).x;
        float flSceneDepthNormalized = RemapValClamped(flSceneDepth, g_flViewportMinZ, g_flViewportMaxZ, 0.0, 1.0);

        float invProjTerm = fma(flSceneDepthNormalized, g_vInvProjRow3.z, g_vInvProjRow3.w);

        float flPerspectiveCorrection = dot(g_vCameraDirWs, vCameraRay);

        return (g_vCameraPositionWs.xyz + (vCameraRay * (1.0 / (invProjTerm * flPerspectiveCorrection))));
    }

    uniform float g_flFeatherDistance;
    uniform float g_flFeatherFalloff = 1.0;
#endif

uniform vec2 g_vTexCoordScrollSpeed;
uniform vec2 g_vMask1PanSpeed;
uniform vec2 g_vMask2PanSpeed;
uniform vec2 g_vMask3PanSpeed;
uniform vec2 g_vMask1Scale = vec2(1.0, 1.0);
uniform vec2 g_vMask2Scale = vec2(1.0, 1.0);
uniform vec2 g_vMask3Scale = vec2(1.0, 1.0);

uniform float g_flColorBoost = 1.0;
uniform float g_flFadeDistance = 1.0;
uniform float g_flFadeFalloff = 1.0;
uniform float g_flFadeMax = 1.0;
uniform float g_flFadeMin;
uniform float g_flFresnelExponent = 0.001;
uniform float g_flFresnelFalloff = 1.0;
uniform float g_flFresnelMax = 1.0;
uniform float g_flFresnelMin;
uniform float g_flOpacityScale = 1.0;

#include "common/features.glsl"
#include "common/fog.glsl"

void main()
{
    vec4 color = texture(g_tColor, vTexCoordOut);
    float mask1 = texture(g_tMask1, vTexCoordOut * g_vMask1Scale.xy + (g_vMask1PanSpeed.xy * g_flTime)).x;
    float mask2 = texture(g_tMask2, vTexCoordOut * g_vMask2Scale.xy + (g_vMask2PanSpeed.xy * g_flTime)).x;
    float mask3 = texture(g_tMask3, vTexCoordOut * g_vMask3Scale.xy + (g_vMask3PanSpeed.xy * g_flTime)).x;

    float tintFactor = 1.0;

    #if (F_TINT_MASK == 1)
        tintFactor = texture(g_tTintMask, vTexCoordOut).x;
    #endif

    float opacity = color.a * vColorOut.a * g_flOpacityScale * mask1 * mask2 * mask3 ;

    vec3 vCameraRay = vFragPosition - g_vCameraPositionWs;
    vec3 vGeometricNormal = vNormalOut;

    if (F_RENDER_BACKFACES == 1 && F_DONT_FLIP_BACKFACE_NORMALS == 0)
    {
        vGeometricNormal *= gl_FrontFacing ? 1.0 : -1.0;
    }

    float feather = 1.0;

    #if (F_DEPTH_FEATHER == 1)
        ivec2 vScreenPosition = ivec2(gl_FragCoord.xy);
        vec3 vScenePosition = GetWorldPositionFromDepth(vScreenPosition, vCameraRay);

        float flFragmentToSceneDistance = distance(vFragPosition, vScenePosition);

        feather = saturate(flFragmentToSceneDistance / g_flFeatherDistance);
        feather = pow(feather, g_flFeatherFalloff);
    #endif

    float fresnel = saturate(dot(-normalize(vCameraRay), normalize(vGeometricNormal).xyz));
    fresnel = pow(fresnel, g_flFresnelExponent) * g_flFresnelFalloff;
    fresnel = saturate(fresnel);
    fresnel = mix(g_flFresnelMin, g_flFresnelMax, fresnel);

    float fade = saturate(length(vCameraRay / vec3(g_flFadeDistance)));
    fade = mix(g_flFadeMin, g_flFadeMax, fade);
    fade = pow(fade, g_flFadeFalloff);

    opacity *= fresnel;
    opacity *= feather;
    opacity *= fade;

    outputColor = vec4(
        mix(color.rgb, color.rgb * vColorOut.rgb, tintFactor) * g_flColorBoost,
        opacity
    );

    ApplyFog(outputColor.rgb, vFragPosition);

    outputColor = WeightColorTranslucency(outputColor);

    if (g_iRenderMode == renderMode_Color)
    {
        outputColor = color * mask1;
    }
    else if (g_iRenderMode == renderMode_Tint)
    {
        outputColor = vColorOut;
    }
    else if (g_iRenderMode == renderMode_SpriteEffects)
    {
        outputColor = vec4(mask1, mask2, mask3, 1);
    }
}
