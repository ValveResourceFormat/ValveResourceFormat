#version 460

// An unlit, fogged CRT screen with distortion, channel jitter, scanlines, vignette, film grain and a reflection of the room.
//
// The entry file of a game defines the combos it ships, then these hooks:
//   MONITOR_MODEL_UP_TANGENT: the glass is bent around the up axis of the model, not the tangent of the mesh
//   MONITOR_GLASS_HIGHLIGHT: the glass reflects the environment map of the object and the highlight of the dynamic
//     lights, rather than blending the environment maps
// The combos F_TEXTURE_ANIMATION, F_ADDITIVE_BLEND_OVER_ALPHA and F_VOLUME_METER_ANIMATION are read when defined.

#define LIGHTMAP_VR_SHADING 1

#include "../common/surface_base_ps.slang"

#if (MONITOR_GLASS_HIGHLIGHT)
    #include "../common/surface_material.slang"
#endif

uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture
uniform sampler2D g_tScanlineDistortion;
uniform sampler2D g_tScanlines;
uniform sampler2D g_tVignette;
uniform sampler2D g_tFilmGrain;
uniform sampler2D g_tContrastControl;
uniform sampler2D g_tHorizontalJitter;
uniform sampler2D g_tVSyncControl;
uniform sampler2D g_tSpecularDistortion;
uniform sampler2D g_tSpecularDirt;

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)
uniform float g_flTintScale = 1.0;
uniform vec2 g_vTexCoordScale = vec2(1.0);
uniform float g_flTexCoordOffsetU = 0.0;
uniform float g_flTexCoordOffsetV = 0.0;
uniform vec2 g_vScanlineTextureOffset = vec2(0.0);
uniform float g_flFilmGrainStrength = 0.5;
uniform float g_flVSYNCJumpScale = 0.25;
uniform float g_flRedShift = 1.0;
uniform float g_flGreenShift = 0.92;
uniform float g_flBlueShift = 1.23;
uniform float g_flJitterSpeed = 1.7;
uniform float g_flReflectionStrength = 10.0;

#if (MONITOR_GLASS_HIGHLIGHT)
    uniform float g_fSpecularRoughness = 0.35;
#else
    uniform float g_fScreenReflectionRoughness = 0.2;
#endif

#if (F_TEXTURE_ANIMATION == 1)
    uniform vec2 g_vAnimationGrid = vec2(1.0);
    uniform int g_nNumAnimationCells = 1;
    uniform float g_flAnimationTimePerFrame = 0.1;
    uniform float g_flAnimationTimeOffset = 0.0;
    uniform float g_flAnimationFrame = 0.0;
#endif

#if (F_ADDITIVE_BLEND_OVER_ALPHA == 1)
    uniform float g_flAlphaOutputMaximum = 1.0;
    uniform float g_flAlphaOutputMinimum = 0.0;
#endif

// The wipe is not driven here, the material value is its resting position
#if (F_VOLUME_METER_ANIMATION == 1)
    uniform sampler2D g_tColor2; // SrgbRead(true)
    uniform float g_flVerticalWipe = 0.5;
#endif

#if (F_TEXTURE_ANIMATION == 1)
// The picture is an atlas of frames that are not scaled or offset
vec2 GetAnimatedPictureUv(vec2 uv)
{
    uint frame = uint(g_flAnimationFrame);
    uint cells = uint(g_nNumAnimationCells);
    uint cell = frame;

    if (F_TEXTURE_ANIMATION_MODE == 0)
    {
        cell = uint((g_flTime + g_flAnimationTimeOffset) / g_flAnimationTimePerFrame) % cells;
    }
    else if (F_TEXTURE_ANIMATION_MODE == 1)
    {
        cell = uint(Random2D(vec2(float(frame))) * float(cells));
    }

    uint columns = uint(g_vAnimationGrid.x);
    vec2 cellOffset = vec2(float(cell % columns), float(cell / columns));

    return (uv + cellOffset) / g_vAnimationGrid;
}
#endif

#if (S_MODE_DEPTH == 0)
// The normal of the screen glass, bent by the distortion map around a tangent frame
vec3 GetScreenNormal()
{
    vec3 distortion = texture(g_tSpecularDistortion, vTexCoordOut).xyz * 2.0 - 1.0;

    #if (MONITOR_MODEL_UP_TANGENT)
        vec3 tangent = vTangentOut.xyz;
        vec3 bitangent = cross(vNormalOut, tangent) * (vTangentOut.w > 0.0 ? 1.0 : -1.0);

        return normalize(tangent * distortion.x + bitangent * -distortion.y + vNormalOut * distortion.z);
    #else
        vec3 geometricNormal = normalize(vNormalOut);
        vec3 tangent = normalize(vTangentOut.xyz);
        vec3 bitangent = normalize(cross(geometricNormal, vTangentOut.xyz)) * vTangentOut.w;

        return normalize(tangent * distortion.x + bitangent * -distortion.y + geometricNormal * distortion.z);
    #endif
}

#if (MONITOR_GLASS_HIGHLIGHT)
// The environment map picked for the object, at a fixed blurred mip and without parallax correction
vec3 GetRoomReflection(vec3 normal, vec3 viewDir)
{
    if (!ObjectHasEnvMap())
    {
        return vec3(0.0);
    }

    vec3 reflectionDir = reflect(viewDir, normal);
    EnvMapData envData = GetObjectEnvMap();

    return textureLod(g_tEnvironmentMap, vec4(reflectionDir, envData.ArrayIndex), 2.0).rgb * envData.Color;
}

// The GGX highlight of the dynamic lights on the glass, with the roughness raised where the normal changes quickly
vec3 GetGlassHighlight(vec3 normal)
{
    float normalChange = max(dot(dFdx(normal), dFdx(normal)), dot(dFdy(normal), dFdy(normal)));
    float roughness = max(g_fSpecularRoughness, pow(saturate(normalChange), 0.333));

    MaterialProperties_t mat;
    InitProperties(mat, vNormalOut, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));
    mat.Normal = normal;
    mat.Roughness = vec2(roughness);
    mat.IsometricRoughness = roughness;
    ResolveAmbientNormal(mat, vTexCoordOut);
    ResolveDiffuseSpecular(mat, vec3(0.0));
    FinishMaterial(mat);

    LightingTerms_t lighting = InitLighting();
    CalculateDynamicLights(lighting, mat);

    return lighting.SpecularDirect;
}
#else
// What the glass shows of the room: the environment maps
vec3 GetRoomReflection(vec3 normal, vec3 viewDir)
{
    float roughness = g_fScreenReflectionRoughness;
    float dominantFactor = GetDominantReflectionFactor(roughness * roughness);
    vec3 reflectionDir = normalize(mix(normal, reflect(viewDir, normal), dominantFactor));

    float lod = GetEnvMapLOD(sqrt(roughness));

    return BlendEnvMaps(reflectionDir, normal, roughness, lod);
}
#endif
#endif

void main()
{
#if (S_MODE_DEPTH == 2)
    outputColor = vec4(0.0, 0.0, 0.0, 1.0);
    CountQuadOverdraw();
#elif (S_MODE_DEPTH == 0)
    vec2 distortion = (texture(g_tScanlineDistortion, vTexCoordOut).xy * 2.0 - 1.0) * 0.4;
    vec2 distortedUv = vTexCoordOut + distortion;

    #if (F_TEXTURE_ANIMATION == 1)
        vec2 pictureUv = GetAnimatedPictureUv(distortedUv);
    #else
        vec2 pictureUv = distortedUv * g_vTexCoordScale + vec2(g_flTexCoordOffsetU, g_flTexCoordOffsetV);
    #endif

    float vsyncJump = (texture(g_tVSyncControl, vec2(g_flTime * 0.25 + distortedUv.y * 0.02, 0.0)).y - 0.5) * g_flVSYNCJumpScale;
    float vsyncScale = 1.0 + vsyncJump;

    // Each channel is read at its own offset, which jitters along a row of the jitter texture
    float jitterTime = g_flTime * g_flJitterSpeed + distortedUv.y * 2.0;
    float jitterRed = texture(g_tHorizontalJitter, vec2(jitterTime, 0.125)).y - 1.0;
    float jitterGreen = texture(g_tHorizontalJitter, vec2(jitterTime, 0.375)).y;
    float jitterBlue = texture(g_tHorizontalJitter, vec2(jitterTime, 0.625)).y + 1.0;
    vec3 channelShift = vec3(g_flRedShift, g_flGreenShift, g_flBlueShift) * 0.01;

    float offsetRed = (jitterRed * channelShift.x + vsyncJump) * vsyncScale;
    float offsetGreen = (jitterGreen * channelShift.y + vsyncJump) * vsyncScale;
    float offsetBlue = (jitterBlue * channelShift.z + vsyncJump) * vsyncScale;

    vec2 uvRed = pictureUv + vec2(offsetRed);
    vec2 uvGreen = pictureUv + vec2(offsetGreen);
    vec2 uvBlue = pictureUv + vec2(offsetBlue);

    vec3 color;
    color.r = texture(g_tColor, uvRed).r;
    color.g = texture(g_tColor, uvGreen).g;
    color.b = texture(g_tColor, uvBlue).b;

    #if (F_VOLUME_METER_ANIMATION == 1)
        // The second picture shows left of the wipe edge
        float wipeEdge = g_flVerticalWipe * g_vTexCoordScale.x + g_flTexCoordOffsetU;

        if (wipeEdge > pictureUv.x)
        {
            color.r = texture(g_tColor2, uvRed).r;
            color.g = texture(g_tColor2, uvGreen).g;
            color.b = texture(g_tColor2, uvBlue).b;
        }
    #endif

    color *= texture(g_tScanlines, vTexCoordOut + g_vScanlineTextureOffset + distortion).y;
    color *= g_vColorTint * exp2(g_flTintScale);

    float vignette = texture(g_tVignette, vTexCoordOut).y;

    // The vignette fades the opacity instead of the color in the additive mode
    #if (F_ADDITIVE_BLEND_OVER_ALPHA != 1)
        color *= vignette;
    #endif

    vec2 grainUv = distortedUv + vec2(g_flTime * 19.2, g_flTime * 7.1);
    float grain = texture(g_tFilmGrain, grainUv).y;
    float contrast = texture(g_tContrastControl, vec2(g_flTime, 0.0)).y;
    color *= grain * g_flFilmGrainStrength * contrast + 0.5;

    vec3 normal = GetScreenNormal();
    vec3 viewDir = normalize(vFragPosition - g_vCameraPositionWs);

    float dirt = texture(g_tSpecularDirt, vTexCoordOut).y;
    float fresnel = pow(1.0 - saturate(dot(normal, -viewDir)), 5.0) + 0.5;
    float reflectionScale = dirt * g_flReflectionStrength * fresnel;

    color += reflectionScale * GetRoomReflection(normal, viewDir);

    #if (F_ADDITIVE_BLEND_OVER_ALPHA == 1)
        float transmittance = 1.0 - GetLuma(color * (g_flAlphaOutputMaximum - g_flAlphaOutputMinimum) + g_flAlphaOutputMinimum);
        outputColor = vec4(color * vignette, transmittance * vignette);
    #else
        outputColor = vec4(color, 1.0);
    #endif

    #if (MONITOR_GLASS_HIGHLIGHT)
        outputColor.rgb += reflectionScale * GetGlassHighlight(normal);
    #endif

    ApplyFog(outputColor.rgb, vFragPosition);
#endif
}
