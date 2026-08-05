#version 460

#include "common/utils.slang"
#include "common/features.slang"
#include "pbr_features.slang"

#define IBL 1

// The shared alpha test helper reads g_flAlphaTestReference; pbr calls that param
// g_flAlphaTestReference1, so point the helper's uniform at the material's name.
#define g_flAlphaTestReference g_flAlphaTestReference1

in vec3 vFragPosition;

centroid in vec3 vCentroidNormalOut;
in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec4 vTexCoord; // UV1 in xy, UV2 in zw
centroid in vec4 vVertexColorOut;

out vec4 outputColor;

uniform sampler2D g_tColor; // SrgbRead(true)
uniform sampler2D g_tNormalRoughness;

#if (F_USE_NPR_LIGHTING == 1)
    // NPR materials bind no g_tTintMask; they pack the tint mask and the rim light mask into
    // one two channel texture instead. Only .x is used here, .y being the rim mask that the
    // NPR lighting would consume.
    uniform sampler2D g_tTintMaskRimLightMask;
#else
    uniform sampler2D g_tTintMask;
#endif
#if (F_UNLIT == 0)
    uniform sampler2D g_tAmbientOcclusion;
    uniform bool g_bUseSecondaryUvForAmbientOcclusion = false;
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

    #if (F_UNLIT == 0)
        uniform vec2 g_vAmbientOcclusionTexcoordScale1 = vec2(1.0);
        uniform float g_flAmbientOcclusionTexcoordRotation1 = 0.0;
        uniform vec2 g_vAmbientOcclusionTexcoordOffset1 = vec2(0.0);
    #endif

    #if (F_SELF_ILLUM == 1)
        uniform vec2 g_vSelfIllumTexcoordScale1 = vec2(1.0);
        uniform float g_flSelfIllumTexcoordRotation1 = 0.0;
        uniform vec2 g_vSelfIllumTexcoordOffset1 = vec2(0.0);
    #endif
#endif

#if (METALNESS_TEXTURE == 1)
    // The colour alpha is spoken for, so metalness gets its own texture.
    uniform sampler2D g_tMetalness;
#endif

#if (FADED_OPACITY == 1)
    uniform float g_flAlphaAnglePower1 = 0.0;
    uniform vec2 g_flAlphaAngleScaleBias1 = vec2(0.0);

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

    uniform vec3 g_vSelfIllumTint1 = vec3(1.0); // SrgbRead(true)
    uniform float g_flSelfIllumScale1 = 0.0;
    uniform float g_flSelfIllumAlbedoFactor1 = 1.0;
    uniform vec3 g_vSelfIllumFresnelMaskTint1 = vec3(1.0); // SrgbRead(true)
    uniform float g_flSelfIllumFresnelMaskExponent = 5.0;
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

#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"

#include "common/fullbright.slang"
#include "common/texturing.slang"
#include "common/fog.slang"
#include "common/citadel.slang"

// Must be last
#include "common/lighting.slang"

// Scroll snaps to a step of the quantize interval, so animated textures can be made to
// tick rather than slide. A zero interval means no stepping.
float GetScrollTime(float quantize)
{
    return (quantize != 0.0) ? (round(g_flTime / quantize) * quantize) : g_flTime;
}

vec2 ScrollTexCoord(vec2 texCoord, vec2 speed, float quantize)
{
    return texCoord + fract(speed * GetScrollTime(quantize));
}

vec2 SelectTexCoord(bool useSecondary)
{
    return useSecondary ? vTexCoord.zw : vTexCoord.xy;
}

vec2 GetAlbedoTexCoord()
{
    vec2 texCoord = ScrollTexCoord(vTexCoord.xy, g_vAlbedoScrollSpeed1, g_flAlbedoScrollQuantize1);

    #if (F_ENABLE_TEXTURE_TRANSFORMS == 1)
        texCoord = RotateVector2D(texCoord, g_flAlbedoTexcoordRotation1, g_vAlbedoTexcoordScale1, g_vAlbedoTexcoordOffset1);
    #endif

    return texCoord;
}

MaterialProperties_t GetMaterial(vec3 vertexNormal)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormal, vFragPosition, vTangentOut, vBitangentOut);

    vec2 albedoTexCoord = GetAlbedoTexCoord();
    vec2 normalTexCoord = ScrollTexCoord(vTexCoord.xy, g_vNormalAndRoughnessScrollSpeed1, g_flNormalAndRoughnessScrollQuantize1);

    #if (F_ENABLE_TEXTURE_TRANSFORMS == 1)
        normalTexCoord = RotateVector2D(normalTexCoord, g_flNormalTexcoordRotation1, g_vNormalTexcoordScale1, g_vNormalTexcoordOffset1);
    #endif

    vec4 color = texture(g_tColor, albedoTexCoord);
    vec4 normalRoughness = texture(g_tNormalRoughness, normalTexCoord);
    #if (F_USE_NPR_LIGHTING == 1)
        float tintMask = texture(g_tTintMaskRimLightMask, albedoTexCoord).x;
    #else
        float tintMask = texture(g_tTintMask, albedoTexCoord).x;
    #endif

    float vertexColorAmount = (g_bMaskVertexColorTint1 ? tintMask : 1.0) * g_fVertexColorStrength1;

    // The vertex colour goes in either side of the colour correction, which is the
    // difference between tinting the texture and tinting the corrected result.
    vec3 albedo = color.rgb;

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

    vec3 illumTint = vec3(1.0);
    float illumMask = 1.0;
    float illumScale = 0.0;

    #if (F_SELF_ILLUM == 1)
        vec2 selfIllumTexCoord = ScrollTexCoord(SelectTexCoord(g_bUseSecondaryUvForSelfIllum1), g_vSelfIllumScrollSpeed1, g_flSelfIllumScrollQuantize1);
        #if (F_ENABLE_TEXTURE_TRANSFORMS == 1)
            selfIllumTexCoord = RotateVector2D(selfIllumTexCoord, g_flSelfIllumTexcoordRotation1, g_vSelfIllumTexcoordScale1, g_vSelfIllumTexcoordOffset1);
        #endif

        illumMask = texture(g_tSelfIllumMask, selfIllumTexCoord).x;
        illumScale = g_flSelfIllumScale1;
        illumTint = mix(g_vSelfIllumTint1, g_vSelfIllumTint1 * albedo, g_flSelfIllumAlbedoFactor1);
    #endif

    #if (F_DETAIL == 1)
    {
        vec2 detailBase = SelectTexCoord(g_bUseSecondaryUvForDetail1);
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
    }
    #endif

    vec3 normalTs = DecodeHemiOctahedronNormal(normalRoughness.rg);
    mat.NormalMap = normalTs;
    mat.Normal = calculateWorldNormal(normalTs, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
    mat.RoughnessTex = normalRoughness.bb;

    #if (F_SELF_ILLUM == 1)
        // Grazing angles fall back to the untinted mask, so an emissive surface reads as lit
        // where it faces away from the camera.
        vec3 fresnelMask = mix(g_vSelfIllumFresnelMaskTint1, vec3(1.0),
            vec3(pow(max(0.0, dot(mat.ViewDir, mat.Normal)), g_flSelfIllumFresnelMaskExponent)));

        illumTint *= fresnelMask;
        albedo *= fresnelMask;
    #endif

    // Self illumination replaces the albedo rather than adding to it. The deferred pass
    // re-derives the emissive from the same mask, which is what the second line does.
    float illumAmount = illumMask * saturate(illumScale);
    mat.Albedo = mix(albedo, illumTint, illumAmount);
    mat.IllumColor = mat.Albedo * (illumMask * illumScale);

    #if (METALNESS_TEXTURE == 1)
        mat.Metalness = texture(g_tMetalness, albedoTexCoord).x;
    #elif (F_UNLIT == 0)
        mat.Metalness = color.a;
    #endif

    #if (F_UNLIT == 0)
        vec2 occlusionTexCoord = SelectTexCoord(g_bUseSecondaryUvForAmbientOcclusion);
        #if (F_ENABLE_TEXTURE_TRANSFORMS == 1)
            occlusionTexCoord = RotateVector2D(occlusionTexCoord, g_flAmbientOcclusionTexcoordRotation1, g_vAmbientOcclusionTexcoordScale1, g_vAmbientOcclusionTexcoordOffset1);
        #endif

        mat.AmbientOcclusion = texture(g_tAmbientOcclusion, occlusionTexCoord).x;
    #endif

    #if (FADED_OPACITY == 1)
        #if (F_ADVANCED_TRANSLUCENCY == 1)
            vec2 translucencyTexCoord = ScrollTexCoord(SelectTexCoord(g_bUseSecondaryUvForTranslucency), g_vTranslucentScrollSpeed0_1, g_flTranslucentScrollQuantize0_1);
            vec2 altTranslucencyTexCoord = ScrollTexCoord(SelectTexCoord(g_bUseSecondaryUvForAltTranslucency), g_vTranslucentScrollSpeed1_1, g_flTranslucentScrollQuantize1_1);

            float translucency = texture(g_tColor, translucencyTexCoord).a;
            float altTranslucency = texture(g_tAltTranslucency, altTranslucencyTexCoord).x;

            float alpha = (g_nTranslucencyBlendMode1 == 0)
                ? (translucency * altTranslucency)
                : ((g_nTranslucencyBlendMode1 == 1) ? saturate(translucency + altTranslucency) : saturate(translucency - altTranslucency));
        #else
            float alpha = color.a;
        #endif

        // Fades the surface by how squarely it faces the camera, for volumetric-looking shells.
        float angleFade = pow(
            max(0.001, g_flAlphaAngleScaleBias1.x * abs(dot(g_vCameraDirWs.xyz, mat.Normal)) + g_flAlphaAngleScaleBias1.y),
            abs(g_flAlphaAnglePower1));

        mat.Opacity = vVertexColorOut.a * alpha * angleFade;

        #if (F_TRANSLUCENT == 1)
            mat.Opacity *= g_flOpacityScale1;
        #else
            mat.Opacity *= mix(1.0, g_flAlphaBoostStrength, saturate(length((mat.PositionWS - g_vCameraPositionWs) / g_flAlphaBoostDistance)));
        #endif
    #elif (F_ALPHA_TEST == 1)
        mat.Opacity = color.a;
    #endif

    #if (F_ALPHA_TEST == 1)
        mat.Opacity = AlphaTestAntiAliasing(mat.Opacity, albedoTexCoord);
    #endif

    AdjustRoughnessByGeometricNormal(mat);

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;

    #if (F_SHEEN == 1)
        // Sheen carries its colour and its own roughness, tinted through the same mask and
        // vertex colour path as the albedo.
        vec4 sheen = texture(g_tSheen, albedoTexCoord);

        vec3 sheenColor = mix(sheen.rgb, (vec4(sheen.rgb, 1.0) * g_mSheenTextureColorTint).rgb, g_bSheenMaskColorTint1 ? tintMask : 1.0);
        sheenColor = mix(sheenColor, sheenColor * vVertexColorOut.rgb,
            (g_bSheenMaskVertexColorTint1 ? tintMask : 1.0) * g_fSheenVertexColorStrength1);

        mat.SheenColor = sheenColor;
        mat.SheenRoughness = max(sheen.a, 0.05);
    #endif

    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;
    mat.SpecularColor = GetSpecularColorCitadel(mat.Albedo, mat.Metalness);

    #if (F_GLASS == 1)
        // Glass keeps its specular but trades diffuse for transmission.
        mat.DiffuseColor *= 1.0 - texture(g_tGlass, albedoTexCoord).x;
    #endif

    mat.DiffuseAO = vec3(mat.AmbientOcclusion);
    mat.SpecularAO = mat.AmbientOcclusion;

    return mat;
}

void main()
{
    vec3 vertexNormal = SwitchCentroidNormal(vNormalOut, vCentroidNormalOut);

    MaterialProperties_t mat = GetMaterial(vertexNormal);
    outputColor.a = mat.Opacity;

    LightingTerms_t lighting = InitLighting();

    #if (F_UNLIT == 1)
        lighting.DiffuseDirect = mat.Albedo * g_flUnlitAdditiveScale1;
        outputColor.rgb = lighting.DiffuseDirect;
    #else
        lighting = CalculateLighting(mat);
        ApplyAmbientOcclusion(lighting, mat);

        vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;
        vec3 specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect;

        outputColor.rgb = mat.DiffuseColor * diffuseLighting + specularLighting + mat.IllumColor;

        #if (F_GLASS == 1)
            // Beer-Lambert through the albedo, thickening as the surface turns away, over
            // whatever the scene already drew behind it. Valve offsets and blurs the lookup
            // with its cloak machinery; this samples straight through.
            float glassMask = texture(g_tGlass, GetAlbedoTexCoord()).x;

            if (glassMask > 0.0)
            {
                float NoV = max(0.01, dot(mat.ViewDir, mat.Normal));
                vec3 transmission = min(vec3(1.0), pow(max(vec3(0.01), mat.Albedo), vec3(1.0 / NoV)));
                vec3 behind = textureLod(g_tSceneColor, gl_FragCoord.xy / vec2(textureSize(g_tSceneColor, 0)), 0.0).rgb;

                outputColor.rgb += behind * transmission * (glassMask * (1.0 - mat.Metalness) * (1.0 - GetLuma(mat.SpecularColor)));
            }
        #endif
    #endif

    ApplyFog(outputColor.rgb, mat.PositionWS);

    #if (F_ADDITIVE_BLEND == 1)
        outputColor.rgb *= outputColor.a;
    #endif

    if (HandleMaterialRenderModes(outputColor, mat)
    || HandleLightingRenderModes(outputColor, mat, lighting)
    || HandleUVRenderModes(outputColor, mat, g_tColor, vTexCoord.xy))
    {
        //
    }
    else if (g_iRenderMode == renderMode_Cubemaps)
    {
        // No bumpmaps, full reflectivity
        vec3 viewmodeEnvMap = GetEnvironment(mat).rgb;
        outputColor.rgb = SrgbLinearToGamma(viewmodeEnvMap);
    }
    else if (g_iRenderMode == renderMode_Tint)
    {
        outputColor = vVertexColorOut;
    }
    else if (g_iRenderMode == renderMode_VertexColor)
    {
        outputColor.rgb = vVertexColorOut.rgb;
    }
}
