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

out vec4 outputColor;

#define F_TINT_MASK 0

uniform sampler2D g_tColor; // SrgbRead(true)
uniform sampler2D g_tMask1;
uniform sampler2D g_tMask2;
uniform sampler2D g_tMask3;
#if F_TINT_MASK == 1
    uniform sampler2D g_tTintMask;
#endif

uniform vec4 g_vTexCoordScrollSpeed;
uniform vec4 g_vMask1PanSpeed;
uniform vec4 g_vMask2PanSpeed;
uniform vec4 g_vMask3PanSpeed;
uniform vec4 g_vMask1Scale;
uniform vec4 g_vMask2Scale;
uniform vec4 g_vMask3Scale;

uniform float g_flColorBoost = 1.0;
uniform float g_flFadeDistance = 1.0;
uniform float g_flFadeFalloff = 1.0;
uniform float g_flFadeMax = 1.0;
uniform float g_flFadeMin;
uniform float g_flFeatherDistance;
uniform float g_flFeatherFalloff = 1.0;
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

    float fresnel = saturate(dot(-normalize(vCameraRay), normalize(vGeometricNormal).xyz));
    fresnel = pow(fresnel, g_flFresnelExponent) * g_flFresnelFalloff;
    fresnel = saturate(fresnel);
    fresnel = mix(g_flFresnelMin, g_flFresnelMax, fresnel);

    float fade = saturate(length(vCameraRay / vec3(g_flFadeDistance)));
    fade = mix(g_flFadeMin, g_flFadeMax, fade);
    fade = pow(fade, g_flFadeFalloff);

    opacity *= fresnel;
    opacity *= fade;

    outputColor = vec4(
        mix(color.rgb, color.rgb * vColorOut.rgb, tintFactor) * g_flColorBoost,
        opacity
    );

    ApplyFog(outputColor.rgb, vFragPosition);

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
