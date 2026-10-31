#version 460

#include "pbr_features.slang"

// Rect lights shade as area lights, the way Deadlock does it
#define BARN_LIGHT_AREA_LIGHTS 2
#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

// Deadlock blends the light probe volumes in every lightmap version
#define ENABLE_LPV_BLENDING 1

// Unlit, glass and translucent surfaces do not write the G-buffer flag that accepts it
#if (F_UNLIT == 0) && (F_GLASS == 0) && (F_TRANSLUCENT == 0)
    #define SCREEN_SPACE_AO 1
#endif

#include "../common/surface_base_ps.slang"
#include "../common/citadel.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

#if (F_ALPHA_TEST == 1)
    uniform float g_flAlphaTestReference1 = 0.5;
#endif

uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture
uniform sampler2D g_tNormalRoughness;

#if (F_USE_NPR_LIGHTING == 1)
    // NPR materials bind no g_tTintMask and pack the tint and rim light masks into one two channel texture
    uniform sampler2D g_tTintMaskRimLightMask;
    uniform sampler2D g_tNprTransmissiveColor; // SrgbRead(true)
    uniform sampler2D g_tAmbientOcclusion;

    uniform bool g_bUseSecondaryUvForAmbientOcclusion = false;
#else
    uniform sampler2D g_tTintMask;
#endif

uniform mat4 g_mAlbedoColorCorrect = mat4(1.0);
uniform mat4 g_mTextureColorTint = mat4(1.0);

uniform bool g_bMaskColorTint1 = true;
uniform bool g_bMaskVertexColorTint1 = true;
uniform bool g_bApplyTintToVertexColors = false;
uniform float g_fVertexColorStrength1 = 1.0;

// Every sampled texture scrolls on its own clock.
uniform vec2 g_vAlbedoScrollSpeed1 = vec2(0.0);
uniform float g_flAlbedoScrollQuantize1 = 0.0;
uniform vec2 g_vNormalAndRoughnessScrollSpeed1 = vec2(0.0);
uniform float g_flNormalAndRoughnessScrollQuantize1 = 0.0;

#if (F_ENABLE_TEXTURE_TRANSFORMS == 1)
    uniform vec2 g_vAlbedoTexcoordScale1 = vec2(1.0);
    uniform float g_flAlbedoTexcoordRotation1 = 0.0;
    uniform vec2 g_vAlbedoTexcoordOffset1 = vec2(0.0);

    uniform vec2 g_vNormalTexcoordScale1 = vec2(1.0);
    uniform float g_flNormalTexcoordRotation1 = 0.0;
    uniform vec2 g_vNormalTexcoordOffset1 = vec2(0.0);

    #if (F_SELF_ILLUM == 1)
        uniform vec2 g_vSelfIllumTexcoordScale1 = vec2(1.0);
        uniform float g_flSelfIllumTexcoordRotation1 = 0.0;
        uniform vec2 g_vSelfIllumTexcoordOffset1 = vec2(0.0);
    #endif
#endif

// The colour alpha is the opacity whenever the material is cut out or see through, so metalness gets its own
// texture. Unlit shades nothing, so it carries no metalness.
#if ((F_ALPHA_TEST == 1) || (F_TRANSLUCENT == 1)) && (F_UNLIT == 0)
    uniform sampler2D g_tMetalness;
#endif

// Translucency runs the alpha through the view angle fade, and unlit alpha test tests against that faded value
// rather than the raw texture alpha.
#if (F_TRANSLUCENT == 1) || ((F_UNLIT == 1) && (F_ALPHA_TEST == 1))
    uniform float g_flAlphaAnglePower1 = 0.0;

    #if (F_TRANSLUCENT == 1)
        uniform float g_flOpacityScale1 = 1.0;
    #else
        // Unlit fades in rather than out with distance, to keep thin sprites readable far away.
        uniform float g_flAlphaBoostStrength = 3.0;
        uniform float g_flAlphaBoostDistance = 20000.0;
    #endif

    #if (F_ADVANCED_TRANSLUCENCY == 1)
        uniform sampler2D g_tAltTranslucency;

        uniform int g_nTranslucencyBlendMode1 = 0; // 0="Multiply", 1="Add", 2="Subtract"
        uniform bool g_bUseSecondaryUvForTranslucency = false;
        uniform bool g_bUseSecondaryUvForAltTranslucency = false;
        uniform vec2 g_vTranslucentScrollSpeed0_1 = vec2(0.0);
        uniform float g_flTranslucentScrollQuantize0_1 = 0.0;
        uniform vec2 g_vTranslucentScrollSpeed1_1 = vec2(0.0);
        uniform float g_flTranslucentScrollQuantize1_1 = 0.0;
    #endif
#endif

#if (F_SELF_ILLUM == 1)
    uniform sampler2D g_tSelfIllumMask;

    uniform vec3 g_vSelfIllumTint1 = vec3(1.0);
    uniform float g_flSelfIllumScale1 = 0.0;
    uniform float g_flSelfIllumAlbedoFactor1 = 1.0;
    uniform vec3 g_vSelfIllumFresnelMaskTint1 = vec3(1.0);
    uniform float g_flSelfIllumFresnelMaskExponent = 5.0;
    uniform vec2 g_vSelfIllumFresnelMaskRange1 = vec2(0.0);
    uniform bool g_bUseSecondaryUvForSelfIllum1 = false;
    uniform vec2 g_vSelfIllumScrollSpeed1 = vec2(0.0);
    uniform float g_flSelfIllumScrollQuantize1 = 0.0;
#endif

#if (F_DETAIL == 1)
    uniform sampler2D g_tDetail; // SrgbRead(true)
    uniform sampler2D g_tMasks1;

    uniform vec3 g_vDetailColorTint1 = vec3(1.0); // SrgbRead(true)
    uniform float g_flDetailBlendFactor1 = 1.0;
    uniform vec2 g_vDetailTexCoordScale1 = vec2(1.0);
    uniform vec2 g_vDetailTexCoordOffset1 = vec2(0.0);
    uniform float g_flDetailTexCoordRotation1 = 0.0;
    uniform bool g_bUseSecondaryUvForDetail1 = false;
    uniform int g_nDetailBlendMode = 0; // 0="Add to albedo", 1="Add to self illum", 2="Mod2x albedo"
#endif

#if (F_SHEEN == 1)
    uniform sampler2D g_tSheen; // SrgbRead(true)
    uniform mat4 g_mSheenTextureColorTint = mat4(1.0); // currently unset

    uniform bool g_bSheenMaskColorTint1 = true;
    uniform bool g_bSheenMaskVertexColorTint1 = true;
    uniform float g_fSheenVertexColorStrength1 = 1.0;
#endif

#if (F_GLASS == 1)
    uniform sampler2D g_tGlass; // r=glass mask
    uniform sampler2D g_tSceneColor; // reserved
#endif

#if (F_UNLIT == 1)
    uniform float g_flUnlitAdditiveScale1 = 1.0;
#endif

// How much of each fog the material takes
uniform float g_flGradientFogAmount = 1.0;
uniform float g_flCubmapFogAmount = 1.0;
uniform float g_flVolumeFogAmount = 1.0;

vec2 GetAlbedoTexCoord(vec2 texCoord)
{
    vec2 albedoTexCoord = ScrollTexCoord(texCoord, g_vAlbedoScrollSpeed1, g_flAlbedoScrollQuantize1);

#if (F_ENABLE_TEXTURE_TRANSFORMS == 1)
    albedoTexCoord = RotateVector2D(albedoTexCoord, g_flAlbedoTexcoordRotation1, g_vAlbedoTexcoordScale1, g_vAlbedoTexcoordOffset1);
#endif

    return albedoTexCoord;
}

vec2 GetNormalTexCoord(vec2 texCoord)
{
    vec2 normalTexCoord = ScrollTexCoord(texCoord, g_vNormalAndRoughnessScrollSpeed1, g_flNormalAndRoughnessScrollQuantize1);

#if (F_ENABLE_TEXTURE_TRANSFORMS == 1)
    normalTexCoord = RotateVector2D(normalTexCoord, g_flNormalTexcoordRotation1, g_vNormalTexcoordScale1, g_vNormalTexcoordOffset1);
#endif

    return normalTexCoord;
}

float GetTintMask(vec2 albedoTexCoord, out float rimLightMask)
{
    rimLightMask = 0.0;

#if (F_USE_NPR_LIGHTING == 1)
    vec2 masks = texture(g_tTintMaskRimLightMask, albedoTexCoord).xy;
    rimLightMask = masks.y;
    return masks.x;
#else
    return texture(g_tTintMask, albedoTexCoord).x;
#endif
}

// The vertex colour goes in either side of the colour correction, which is the
// difference between tinting the texture and tinting the corrected result.
vec3 GetCitadelAlbedo(vec3 textureColor, float tintMask)
{
    float vertexColorAmount = (g_bMaskVertexColorTint1 ? tintMask : 1.0) * g_fVertexColorStrength1;
    vec3 albedo = textureColor;

    if (g_bApplyTintToVertexColors)
    {
        albedo = mix(albedo, albedo * vVertexColorOut.rgb, vertexColorAmount);
    }

    albedo = (vec4(albedo, 1.0) * g_mAlbedoColorCorrect).rgb;
    albedo = mix(albedo, (vec4(albedo, 1.0) * g_mTextureColorTint).rgb, g_bMaskColorTint1 ? tintMask : 1.0);

    if (!g_bApplyTintToVertexColors)
    {
        albedo = mix(albedo, albedo * vVertexColorOut.rgb, vertexColorAmount);
    }

    return albedo;
}

void SampleSelfIllum(vec2 texCoord, vec3 albedo, out vec3 illumTint, out float illumMask, out float illumScale)
{
    illumTint = vec3(1.0);
    illumMask = 1.0;
    illumScale = 0.0;

#if (F_SELF_ILLUM == 1)
    vec2 selfIllumTexCoord = ScrollTexCoord(GetUvSet(texCoord, g_bUseSecondaryUvForSelfIllum1), g_vSelfIllumScrollSpeed1, g_flSelfIllumScrollQuantize1);
    #if (F_ENABLE_TEXTURE_TRANSFORMS == 1)
        selfIllumTexCoord = RotateVector2D(selfIllumTexCoord, g_flSelfIllumTexcoordRotation1, g_vSelfIllumTexcoordScale1, g_vSelfIllumTexcoordOffset1);
    #endif

    illumMask = texture(g_tSelfIllumMask, selfIllumTexCoord).x;
    illumScale = g_flSelfIllumScale1;
    illumTint = mix(g_vSelfIllumTint1, g_vSelfIllumTint1 * albedo, g_flSelfIllumAlbedoFactor1);
#endif
}

// Facing the camera keeps the untinted mask and grazing angles take the fresnel tint, with
// the range picking where the change happens. An empty range is a hard step at its start.
void ApplySelfIllumFresnel(MaterialProperties_t mat, float illumMask, inout vec3 albedo, inout vec3 illumTint)
{
#if (F_SELF_ILLUM == 1)
    float fresnel = pow(max(0.0, dot(mat.ViewDir, mat.Normal)), g_flSelfIllumFresnelMaskExponent);
    float fresnelRange = g_vSelfIllumFresnelMaskRange1.y - g_vSelfIllumFresnelMaskRange1.x;
    float fresnelBlend = (fresnelRange != 0.0)
        ? saturate((fresnel - g_vSelfIllumFresnelMaskRange1.x) / fresnelRange)
        : float(fresnel > g_vSelfIllumFresnelMaskRange1.x);
    vec3 fresnelMask = mix(g_vSelfIllumFresnelMaskTint1, vec3(1.0), fresnelBlend);

    illumTint *= fresnelMask;
    albedo *= mix(vec3(1.0), fresnelMask, illumMask);
#endif
}

void ApplyDetailBlend(vec2 texCoord, vec2 albedoTexCoord, inout vec3 albedo, inout vec3 illumTint)
{
#if (F_DETAIL == 1)
    vec2 detailBase = GetUvSet(texCoord, g_bUseSecondaryUvForDetail1);
    #if (F_ENABLE_TEXTURE_TRANSFORMS == 1)
        vec2 detailTexCoord = RotateVector2D(detailBase, g_flDetailTexCoordRotation1, g_vDetailTexCoordScale1, g_vDetailTexCoordOffset1);
    #else
        vec2 detailTexCoord = detailBase * g_vDetailTexCoordScale1 + g_vDetailTexCoordOffset1;
    #endif

    float detailMask = texture(g_tMasks1, albedoTexCoord).x * g_flDetailBlendFactor1;
    vec3 detail = texture(g_tDetail, detailTexCoord).rgb * g_vDetailColorTint1;

    if (g_nDetailBlendMode == 0)
    {
        albedo += detail * detailMask;
    }
    else if (g_nDetailBlendMode == 1)
    {
        illumTint += detail * detailMask;
    }
    else if (g_nDetailBlendMode == 2)
    {
        albedo *= mix(vec3(1.0), detail * 2.0, detailMask);
    }
#endif
}

float GetMetalness(vec2 albedoTexCoord, float colorAlpha)
{
#if ((F_ALPHA_TEST == 1) || (F_TRANSLUCENT == 1)) && (F_UNLIT == 0)
    return texture(g_tMetalness, albedoTexCoord).x;
#elif (F_UNLIT == 0)
    return colorAlpha;
#else
    return 0.0;
#endif
}

#if (F_TRANSLUCENT == 1) || ((F_UNLIT == 1) && (F_ALPHA_TEST == 1))
float GetTranslucencyAlpha(vec2 texCoord, float colorAlpha)
{
#if (F_ADVANCED_TRANSLUCENCY == 1)
    vec2 translucencyTexCoord = ScrollTexCoord(GetUvSet(texCoord, g_bUseSecondaryUvForTranslucency), g_vTranslucentScrollSpeed0_1, g_flTranslucentScrollQuantize0_1);
    vec2 altTranslucencyTexCoord = ScrollTexCoord(GetUvSet(texCoord, g_bUseSecondaryUvForAltTranslucency), g_vTranslucentScrollSpeed1_1, g_flTranslucentScrollQuantize1_1);

    float translucency = texture(g_tColor, translucencyTexCoord).a;
    float altTranslucency = texture(g_tAltTranslucency, altTranslucencyTexCoord).x;

    if (g_nTranslucencyBlendMode1 == 0)
    {
        return translucency * altTranslucency;
    }

    if (g_nTranslucencyBlendMode1 == 1)
    {
        return saturate(translucency + altTranslucency);
    }

    return saturate(translucency - altTranslucency);
#else
    return colorAlpha;
#endif
}

// Fades the surface by how squarely it faces the camera, for volumetric-looking shells.
// A negative power inverts the facing term, fading the silhouette instead of the middle.
float GetViewFadedOpacity(MaterialProperties_t mat, float alpha)
{
    vec2 angleScaleBias = (g_flAlphaAnglePower1 < 0.0) ? vec2(-1.0, 1.0) : vec2(1.0, 0.0);
    float angleFade = pow(
        max(0.001, angleScaleBias.x * abs(dot(g_vCameraDirWs.xyz, mat.Normal)) + angleScaleBias.y),
        abs(g_flAlphaAnglePower1));

    float opacity = vVertexColorOut.a * alpha * angleFade;

#if (F_TRANSLUCENT == 1)
    return opacity * g_flOpacityScale1;
#else
    return opacity * mix(1.0, g_flAlphaBoostStrength, saturate(length((mat.PositionWS - g_vCameraPositionWs) / g_flAlphaBoostDistance)));
#endif
}
#endif

float GetCitadelOpacity(MaterialProperties_t mat, vec2 texCoord, vec2 albedoTexCoord, vec4 color)
{
    float opacity = mat.Opacity;

#if (F_TRANSLUCENT == 1) || ((F_UNLIT == 1) && (F_ALPHA_TEST == 1))
    opacity = GetViewFadedOpacity(mat, GetTranslucencyAlpha(texCoord, color.a));
#elif (F_ALPHA_TEST == 1)
    opacity = color.a;
#endif

#if (F_ALPHA_TEST == 1)
    opacity = AlphaTestAntiAliasing(opacity, albedoTexCoord, g_flAlphaTestReference1, g_flAntiAliasedEdgeStrength);
#endif

    return opacity;
}

// Sheen carries its colour and its own roughness, tinted through the same mask and
// vertex colour path as the albedo.
void ApplySheen(inout MaterialProperties_t mat, vec2 albedoTexCoord, float tintMask)
{
#if (F_SHEEN == 1)
    vec4 sheen = texture(g_tSheen, albedoTexCoord);

    vec3 sheenColor = mix(sheen.rgb, (vec4(sheen.rgb, 1.0) * g_mSheenTextureColorTint).rgb, g_bSheenMaskColorTint1 ? tintMask : 1.0);
    sheenColor = mix(sheenColor, sheenColor * vVertexColorOut.rgb,
        (g_bSheenMaskVertexColorTint1 ? tintMask : 1.0) * g_fSheenVertexColorStrength1);

    mat.SheenColor = sheenColor;
    mat.SheenRoughness = max(sheen.a, 0.05);
#endif
}

// Glass keeps its specular but trades diffuse for transmission.
void ApplyGlassDiffuse(inout MaterialProperties_t mat, vec2 albedoTexCoord)
{
#if (F_GLASS == 1)
    mat.DiffuseColor *= 1.0 - texture(g_tGlass, albedoTexCoord).x;
#endif
}

// NPR lighting takes the occlusion from its own texture and applies it to the bounce light, the rim and the
// indirect specular, not to the direct light. The metalness only darkens the diffuse where the surface shines.
void ApplyNprMaterial(inout MaterialProperties_t mat, vec2 texCoord, vec2 albedoTexCoord, float rimLightMask)
{
#if (F_USE_NPR_LIGHTING == 1)
    mat.AmbientOcclusion = texture(g_tAmbientOcclusion, GetUvSet(texCoord, g_bUseSecondaryUvForAmbientOcclusion)).x;
    mat.RimLightMask = rimLightMask;
    mat.TransmissiveColor = texture(g_tNprTransmissiveColor, albedoTexCoord).rgb;

    float roughness = mat.Roughness.x;
    float alpha = pow2(roughness);

    mat.DiffuseAO = vec3(1.0);
    mat.SpecularAO = saturate(pow(saturate(dot(mat.Normal, mat.ViewDir)) + mat.AmbientOcclusion, alpha) - 1.0 + mat.AmbientOcclusion);

    if (F_NO_SPECULAR_AT_FULL_ROUGHNESS == 1)
    {
        float shine = saturate((0.99607843 - roughness) * 255.0 + 1.0);
        mat.DiffuseColor = mat.Albedo - mat.Albedo * (mat.Metalness * shine);
    }
#endif
}

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec2 albedoTexCoord = GetAlbedoTexCoord(texCoord);

    vec4 color = texture(g_tColor, albedoTexCoord);
    vec4 normalRoughness = texture(g_tNormalRoughness, GetNormalTexCoord(texCoord));
    float rimLightMask;
    float tintMask = GetTintMask(albedoTexCoord, rimLightMask);

    vec3 albedo = GetCitadelAlbedo(color.rgb, tintMask);

    vec3 illumTint;
    float illumMask;
    float illumScale;
    SampleSelfIllum(texCoord, albedo, illumTint, illumMask, illumScale);
    ApplyDetailBlend(texCoord, albedoTexCoord, albedo, illumTint);

    vec3 normalTs = DecodeHemiOctahedronNormal(normalRoughness.rg);
    mat.NormalMap = normalTs;
    mat.Normal = CalculateWorldNormal(normalTs, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
    mat.RoughnessTex = normalRoughness.bb;

    ApplySelfIllumFresnel(mat, illumMask, albedo, illumTint);

    // Self illumination replaces the albedo rather than adding to it. The deferred pass
    // re-derives the emissive from the same mask, which is what the second line does.
    float illumAmount = illumMask * saturate(illumScale);
    mat.Albedo = mix(albedo, illumTint, illumAmount);
    mat.IllumColor = mat.Albedo * (illumMask * illumScale);

    mat.Metalness = GetMetalness(albedoTexCoord, color.a);
    mat.Opacity = GetCitadelOpacity(mat, texCoord, albedoTexCoord, color);

    AdjustRoughnessByGeometricNormal(mat);

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;

    ApplySheen(mat, albedoTexCoord, tintMask);

    ResolveDiffuseSpecularCitadel(mat);

    ApplyGlassDiffuse(mat, albedoTexCoord);

    ApplyNprMaterial(mat, texCoord, albedoTexCoord, rimLightMask);

    return mat;
}

#if (S_MODE_DEPTH == 0)

#if (F_GLASS == 1)
// Beer-Lambert through the albedo, thickening as the surface turns away, over whatever the scene
// already drew behind it. The lookup samples straight through, without offset or blur.
void AddGlassTransmission(inout vec4 color, MaterialProperties_t mat)
{
    float glassMask = texture(g_tGlass, GetAlbedoTexCoord(vTexCoordOut)).x;

    if (glassMask <= 0.0)
    {
        return;
    }

    float NoV = max(0.01, dot(mat.ViewDir, mat.Normal));
    vec3 transmission = min(vec3(1.0), pow(max(vec3(0.01), mat.Albedo), vec3(1.0 / NoV)));
    vec3 behind = textureLod(g_tSceneColor, gl_FragCoord.xy / vec2(textureSize(g_tSceneColor, 0)), 0.0).rgb;

    color.rgb += behind * transmission * (glassMask * (1.0 - mat.Metalness) * (1.0 - GetLuma(mat.SpecularColor)));
}
#endif

vec4 ShadeCitadelSurface(inout MaterialProperties_t mat, out LightingTerms_t lighting)
{
#if (F_UNLIT == 1)
    lighting = InitLighting();
    lighting.DiffuseDirect = mat.Albedo * ((F_ADDITIVE_BLEND == 1) ? g_flUnlitAdditiveScale1 : 1.0);
    return vec4(lighting.DiffuseDirect, mat.Opacity);
#else
    SurfaceOptions_t options = DefaultSurfaceOptions();

#if (F_USE_NPR_LIGHTING == 1)
    options.DirectSpecularAO = 0.0;
#endif

    vec4 color = ShadeSurface(mat, lighting, options);
#if (F_GLASS == 1)
    AddGlassTransmission(color, mat);
#endif
    return color;
#endif
}

#endif

void main()
{
#if (S_MODE_DEPTH != 0)
    RenderDepthPass();
#else
    MaterialProperties_t mat = GetShadedMaterial();
    LightingTerms_t lighting;
    outputColor = ShadeCitadelSurface(mat, lighting);

#if (F_TRANSLUCENT == 0 && F_ADDITIVE_BLEND == 0)
    // Opaque surfaces end up fogged at full strength whatever amounts the material sets.
    // Unlit ones are fogged by the material amounts first.
    #if (F_UNLIT == 1)
        g_vMaterialFogAmounts = vec3(g_flGradientFogAmount, g_flCubmapFogAmount, g_flVolumeFogAmount);
        ApplyFog(outputColor.rgb, mat.PositionWS);
        g_vMaterialFogAmounts = vec3(1.0);
    #endif
#else
    g_vMaterialFogAmounts = vec3(g_flGradientFogAmount, g_flCubmapFogAmount, g_flVolumeFogAmount);
#endif

    ApplyFogAndToneMapping(outputColor, mat.PositionWS);

    if (F_ADDITIVE_BLEND == 1)
    {
        outputColor.rgb *= outputColor.a;
    }

    if (!HandleSurfaceRenderModes(outputColor, mat, lighting, g_tColor))
    {
        HandleVertexRenderModes(outputColor, vec3(0.0));
    }
#endif
}
