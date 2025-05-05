#version 460

#include "common/utils.glsl"
#include "common/ViewConstants.glsl"

#define renderMode_Color 0
#define renderMode_SpriteEffects 0
#define renderMode_Tint 0

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec2 vTexCoordOut;
in vec4 vColorOut;

out vec4 outputColor;

#define F_TINT_MASK 0

uniform sampler2D g_tColor;
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
uniform float g_flFadeDistance = 5000;
uniform float g_flFadeFalloff = 1.0;
uniform float g_flFadeMax = 1.0;
uniform float g_flFadeMin;
uniform float g_flFeatherDistance = 2000;
uniform float g_flFeatherFalloff = 1.0;
uniform float g_flFresnelExponent = 1.0;
uniform float g_flFresnelFalloff = 1.0;
uniform float g_flFresnelMax = 1.0;
uniform float g_flFresnelMin;
uniform float g_flOpacityScale = 1.0;

uniform vec4 vTint;

#include "common/fog.glsl"

//Main entry point
void main()
{
    //Get the ambient color from the color texture
    vec4 color = texture(g_tColor, vTexCoordOut);
    float mask1 = texture(g_tMask1, vTexCoordOut * g_vMask1Scale.xy + (g_vMask1PanSpeed.xy * g_flTime)).x;
    float mask2 = texture(g_tMask2, vTexCoordOut * g_vMask2Scale.xy + (g_vMask2PanSpeed.xy * g_flTime)).x;
    float mask3 = texture(g_tMask3, vTexCoordOut * g_vMask3Scale.xy + (g_vMask3PanSpeed.xy * g_flTime)).x;

    //Calculate tint color
    vec3 tintColor = vTint.rgb;

    #if F_TINT_MASK == 1
        // does texcoord scale really only apply to the tint mask?
        float tintFactor = texture(g_tTintMask, vTexCoordOut * g_vTexCoordScale.xy + g_vTexCoordScale.xy).x;
        tintColor = mix(vec3(1.0), tintColor, tintFactor);
    #endif

    float opacity = color.a * mask1 * mask2 * mask3 * g_flOpacityScale;

    // Calculate fresnel
    vec3 viewDirection = normalize(g_vCameraPositionWs - vFragPosition);
    float fresnel = abs(dot(viewDirection, vNormalOut));
    fresnel = pow(fresnel, g_flFresnelExponent);
    //fresnel = fresnel * g_flFresnelFalloff + (1 - g_flFresnelFalloff);
    //fresnel = fresnel * (g_flFresnelMax - g_flFresnelMin) + g_flFresnelMin;
    fresnel = mix(1 - g_flFresnelFalloff, 1.0, fresnel);
    fresnel = mix(g_flFresnelMin, g_flFresnelMax, fresnel);

    // Calculate fade
    float distanceToCamera = length((vFragPosition - g_vCameraPositionWs) / g_flFadeDistance);
    float fade = pow(mix(g_flFadeMin, g_flFadeMax, clamp(distanceToCamera, 0.0, 1.0)), g_flFadeFalloff);
    
    opacity = opacity * fresnel * fade * vColorOut.a;

    outputColor = vec4(
        color.rgb * tintColor * g_flColorBoost * vColorOut.rgb,
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
