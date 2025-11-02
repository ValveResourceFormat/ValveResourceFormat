#version 460

#include "common/utils.glsl"
#include "common/features.glsl"
#include "csgo_environment_features.glsl"

#define S_SPECULAR 1 // Indirect

in vec3 vFragPosition;

centroid in vec3 vCentroidNormalOut;
in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec4 vTexCoord;
in vec4 vTexCoord2;
in vec4 vTintColor_ModelAmount;
centroid in vec4 vVertexColor_Alpha;
in vec4 vBlendColorTint;


out vec4 outputColor;

// Material 1
uniform sampler2D g_tColor1; // SrgbRead(true)
uniform sampler2D g_tHeight1;
uniform sampler2D g_tNormal1;

#if (F_DETAIL_NORMAL == 1)
    in vec2 vDetailTexCoords;
    uniform sampler2D g_tNormalDetail1;
#endif

uniform bool g_bMetalness1;
uniform bool g_bModelTint1 = true;
uniform int g_nColorCorrectionMode1 = 0;
uniform float g_fTextureColorBrightness1 = 1.0;
uniform float g_fTextureColorContrast1 = 1.0;
uniform float g_fTextureColorSaturation1 = 1.0;
uniform vec3 g_vTextureColorTint1 = vec3(1.0);
uniform float g_fTextureNormalContrast1 = 1.0;
uniform float g_fTextureRoughnessBrightness1 = 1.0;
uniform float g_fTextureRoughnessContrast1 = 1.0;
uniform float g_fTintMaskBrightness1 = 1.0;
uniform float g_fTintMaskContrast1 = 1.0;
uniform int g_nVertexColorMode1 = 0; // 0=Masked by tint, 1=Unmasked, 2=Disabled
uniform vec3 g_vAmbientOcclusionLevels1 = vec3(0, 0.5, 1);

uniform float g_flHeightMapScale1 = 1.0;
uniform float g_flHeightMapZeroPoint1 = 0.5;

// Material 2
#if defined(csgo_environment_blend_vfx)
    in vec4 vColorBlendValues;

    uniform sampler2D g_tColor2; // SrgbRead(true)
    uniform sampler2D g_tHeight2;
    uniform sampler2D g_tNormal2;

    #if (F_DETAIL_NORMAL == 1)
        uniform sampler2D g_tNormalDetail2;
    #endif

    #if (F_SHARED_COLOR_OVERLAY == 1)
        uniform sampler2D g_tSharedColorOverlay; // SrgbRead(true)

        uniform int g_nColorOverlayMode;                    // "0=Both Layers,1=Layer 1,2=Layer 2"
        uniform int g_nColorOverlayTintMask;                // "0=Mask Both Layers,1=Mask Layer 1,2=Mask Layer 2,3==Unmasked"
        uniform float g_flOverlayDarknessContrast = 1.0;    // 0.2 .. 2.0
        uniform float g_flOverlayBrightnessContrast = 1.0;  // 0.2 .. 2.0
    #endif

    #if (F_BLEND_EFFECTS > 0)
        // ...
    #endif

    uniform bool g_bMetalness2;
    uniform bool g_bModelTint2 = true;
    uniform int g_nColorCorrectionMode2 = 0;
    uniform float g_fTextureColorBrightness2 = 1.0;
    uniform float g_fTextureColorContrast2 = 1.0;
    uniform float g_fTextureColorSaturation2 = 1.0;
    uniform vec3 g_vTextureColorTint2 = vec3(1.0);
    uniform float g_fTextureNormalContrast2 = 1.0;
    uniform float g_fTextureRoughnessBrightness2 = 1.0;
    uniform float g_fTextureRoughnessContrast2 = 1.0;
    uniform float g_fTintMaskBrightness2 = 1.0;
    uniform float g_fTintMaskContrast2 = 1.0;
    uniform int g_nVertexColorMode2 = 0;
    uniform vec3 g_vAmbientOcclusionLevels2 = vec3(0, 0.5, 1);

    uniform float g_flHeightMapScale2 = 1.0;
    uniform float g_flHeightMapZeroPoint2 = 0.5;

    #if (F_ENABLE_LAYER_3 == 1)
        in vec4 vTexCoord3;

        uniform sampler2D g_tColor3; // SrgbRead(true)
        uniform sampler2D g_tHeight3;
        uniform sampler2D g_tNormal3;

        uniform bool g_bMetalness3;
        uniform bool g_bModelTint3 = true;
        uniform int g_nColorCorrectionMode3 = 0;
        uniform float g_fTextureColorBrightness3 = 1.0;
        uniform float g_fTextureColorContrast3 = 1.0;
        uniform float g_fTextureColorSaturation3 = 1.0;
        uniform vec3 g_vTextureColorTint3 = vec3(1.0);
        uniform float g_fTextureNormalContrast3 = 1.0;
        uniform float g_fTextureRoughnessBrightness3 = 1.0;
        uniform float g_fTextureRoughnessContrast3 = 1.0;
        uniform float g_fTintMaskBrightness3 = 1.0;
        uniform float g_fTintMaskContrast3 = 1.0;
        uniform int g_nVertexColorMode3 = 0;
        uniform vec3 g_vAmbientOcclusionLevels3 = vec3(0, 0.5, 1);

        uniform float g_flHeightMapScale3 = 1.0;
        uniform float g_flHeightMapZeroPoint3 = 0.5;
    #endif

    uniform bool g_bUseNewBlending = false;

    vec2 GetBlendWeightsNew(vec2 heightTex, vec2 heightScale, vec2 heightZero, vec4 vColorBlendValues)
    {
        float blendFactor = vColorBlendValues.x;
        float blendSoftness = vColorBlendValues.w;

        // Scale calculation preserving direction
        float h1Scale = abs(heightScale.x + (blendSoftness * sign(heightScale.x)));
        float h2Scale = abs(heightScale.y + (blendSoftness * sign(heightScale.y)));

        // Height calculations
        float height1 = heightTex.x * h1Scale;
        float h22 = heightTex.y * abs(heightScale.y - blendSoftness);

        // Blend points with preserved scale direction
        float blend1 = (-heightZero.x * h1Scale - ((1.0 - heightZero.y) * h2Scale)) - blendSoftness;
        float blend2 = (1.0 - heightZero.x) * h1Scale - ((-heightZero.y) * h2Scale);

        float h2x = h22 + mix(blend1, blend2, blendFactor);
        vec2 weights = vec2(height1, h2x) - vec2(max(height1, h2x) - blendSoftness);
        return weights;
    }

    vec2 GetBlendWeights(vec2 heightTex, vec2 heightScale, vec2 heightZero, vec4 vColorBlendValues)
    {
        if (g_bUseNewBlending)
        {
            return GetBlendWeightsNew(heightTex, heightScale, heightZero, vColorBlendValues);
        }

        float blendFactor = vColorBlendValues.x;
        float blendSoftness = vColorBlendValues.w;

        // Weight calculations
        float h1 = heightScale.x + blendSoftness;
        float height1 = heightTex.x * h1;

        float h2 = heightScale.y + blendSoftness;
        float h22 = heightTex.y * (heightScale.y - blendSoftness);

        float blend1 = (-heightZero.x * h1 - ((1.0 - heightZero.y) * h2)) - blendSoftness;
        float blend2 = (1.0 - heightZero.x) * h1 - ((-heightZero.y) * h2);

        float h2x = h22 + mix(blend1, blend2, blendFactor);

        vec2 weights = vec2(height1, h2x) - vec2(max(height1, h2x) - blendSoftness);

        // Clamp negative values
        weights = max(weights, vec2(0.0));

        // Bias towards material 1
        weights.x += 0.001;

        // Normalize
        weights = weights / vec2(weights.x + weights.y);

        return weights;
    }
#endif

#if (F_ALPHA_TEST == 1)
    uniform float g_flAlphaTestReference = 0.5;
#endif

uniform float g_flModelTintAmount = 1.0;

#include "common/ViewConstants.glsl"
#include "common/LightingConstants.glsl"

#include "common/fullbright.glsl"
#include "common/texturing.glsl"

#include "common/fog.glsl"

// Must be last
#include "common/lighting.glsl"

vec3 AdjustBrightnessContrastSaturation(vec3 color, float brightness, float contrast, float saturation)
{
    // Brightness
    color = color * RemapVal(brightness, -8.0, 1.0, 0.0, 1.0);

    // Contrast
    // color = (color - 0.5) * contrast + 0.5;

    // Saturation
    color = mix(GetLuma(color).xxx, color, saturation);

    return color;
}

// Get material properties (possibly from two layers)
MaterialProperties_t GetMaterial(vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals);

    // vTexCoord = Layer1 (xy) shared color overlay (zw)
    // vTexCoord2 = Layer2 (xy) UV2 (zw)
    // vDetailTexCoords = Detail (normal) UVs

    vec4 color = texture(g_tColor1, vTexCoord.xy);
    vec4 height = texture(g_tHeight1, vTexCoord.xy);
    vec4 normal = texture(g_tNormal1, vTexCoord.xy);

    vec3 aoLevels = g_vAmbientOcclusionLevels1.xyz;

    float tintMask1 = saturate(((height.g - 0.5) * g_fTintMaskContrast1 + 0.5) * g_fTintMaskBrightness1);
    height.a = g_bMetalness1 ? height.a : 0.0;
    normal.rg = (normal.rg - 0.5) * g_fTextureNormalContrast1 + 0.5;
    normal.b = saturate(((normal.b - 0.5) * g_fTextureRoughnessContrast1 + 0.5) * g_fTextureRoughnessBrightness1);

    vec3 overlayFactor = vec3(1.0);

    #if (F_SHARED_COLOR_OVERLAY == 1)
        vec3 overlay = texture(g_tSharedColorOverlay, vTexCoord.zw).rgb * 2.0 - 1.0;
        vec3 _15235 = (((vec3(1.0) - pow(vec3(1.0) - max(vec3(0.0), overlay), vec3(g_flOverlayBrightnessContrast))) * g_flOverlayBrightnessContrast)
            + ((pow(vec3(1.0) + min(vec3(0.0), overlay), vec3(g_flOverlayDarknessContrast)) - vec3(1.0)) * g_flOverlayDarknessContrast)) + vec3(1.0);
        overlayFactor = max(vec3(0.0), _15235);
    #endif

    vec3 tintColorNorm = normalize(max(vTintColor_ModelAmount.xyz, vec3(0.001)));
    float tintColorNormLuma = GetLuma(tintColorNorm);

    vec3 adjust1 = AdjustBrightnessContrastSaturation(color.rgb, g_fTextureColorBrightness1, g_fTextureColorContrast1, g_fTextureColorSaturation1);
    vec3 color1MaybeAdjusted = mix(color.rgb, adjust1, bvec3(g_nColorCorrectionMode1 == 1));

    vec3 tintAdjusted = mix(color1MaybeAdjusted, adjust1, vec3(tintMask1)).xyz ;
    float tintAdjustedLuma = GetLuma(tintAdjusted);

    float tintColorAdjustedLumaRatio = tintAdjustedLuma / tintColorNormLuma;
    float tintAdjustedLuma3x = 3.0 * tintAdjustedLuma;

    float tintColorHighestPoint = max(vTintColor_ModelAmount.x, max(vTintColor_ModelAmount.y, vTintColor_ModelAmount.z));
    float highPointXLuma = tintAdjustedLuma3x * tintColorHighestPoint;
    vec3 tintResult = saturate(mix(adjust1, tintColorNorm * min(tintColorAdjustedLumaRatio, highPointXLuma), vec3((vTintColor_ModelAmount.w * tintMask1) * float(g_bModelTint1))));

    vec3 tintFactor1 = mix(vec3(1.0), (g_vTextureColorTint1.rgb), tintMask1);
    color.rgb = tintResult * tintFactor1;

    #if (F_SHARED_COLOR_OVERLAY == 1)
        // 0=Both, 1=Layer 1
        const bool g_bColorOverlayLayer1 = g_nColorOverlayMode == 0 || g_nColorOverlayMode == 1;
        const bool g_bColorOverlayMaskLayer1 = g_nColorOverlayTintMask == 0 || g_nColorOverlayTintMask == 1;
        color.rgb *= mix(vec3(1.0), overlayFactor, vec3((g_bColorOverlayMaskLayer1 ? tintMask1 : 1.0) * float(g_bColorOverlayLayer1)));
    #endif

    #if defined(csgo_environment_blend_vfx)
        float flVertexColorMask = float(g_nVertexColorMode1 != 2) * mix(height.g, 1.0, g_nVertexColorMode1 == 0);
        color.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, flVertexColorMask);
    #else
        // note: g_nVertexColorMode ignored in non-blend
        color.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, height.g);
    #endif

    // Blending
#if defined(csgo_environment_blend_vfx)
    vec4 color2 = texture(g_tColor2, vTexCoord2.xy);
    vec4 height2 = texture(g_tHeight2, vTexCoord2.xy);
    vec4 normal2 = texture(g_tNormal2, vTexCoord2.xy);
    #if (F_DETAIL_NORMAL == 1)
        vec2 detailNormal2 = texture(g_tNormalDetail2, vDetailTexCoords).rg;
    #endif

    float tintMask2 = saturate(((height2.g - 0.5) * g_fTintMaskContrast2 + 0.5) * g_fTintMaskBrightness2);
    height2.a = g_bMetalness2 ? height2.a : 0.0;
    normal2.rg = (normal2.rg - 0.5) * g_fTextureNormalContrast2 + 0.5;
    normal2.b = saturate(((normal2.b - 0.5) * g_fTextureRoughnessContrast2 + 0.5) * g_fTextureRoughnessBrightness2);

    vec3 adjust2 = AdjustBrightnessContrastSaturation(color2.rgb, g_fTextureColorBrightness2, g_fTextureColorContrast2, g_fTextureColorSaturation2);
    vec3 color2MaybeAdjusted = mix(color2.rgb, adjust2, bvec3(g_nColorCorrectionMode2 == 1));

    vec3 tintAdjusted2 = mix(color2MaybeAdjusted, adjust2, vec3(tintMask2)).xyz;
    float tintAdjusted2Luma = GetLuma(tintAdjusted2);

    float tintColorAdjusted2LumaRatio = tintAdjusted2Luma / tintColorNormLuma;
    float tintAdjusted2Luma3x = 3.0 * tintAdjusted2Luma;

    vec3 tintResult2 = saturate(mix(adjust2, tintColorNorm * min(tintColorAdjusted2LumaRatio, tintAdjusted2Luma3x * tintColorHighestPoint), vec3((vTintColor_ModelAmount.w * tintMask2) * float(g_bModelTint2))));

    vec3 tintFactor2 = mix(vec3(1.0), (g_vTextureColorTint2.rgb), tintMask2);
    color2.rgb = tintResult2 * tintFactor2;

    #if (F_SHARED_COLOR_OVERLAY == 1)
        // 0=Both, 2=Layer 2
        const bool g_bColorOverlayLayer2 = g_nColorOverlayMode == 0 || g_nColorOverlayMode == 2;
        const bool g_bColorOverlayMaskLayer2 = g_nColorOverlayTintMask == 0 || g_nColorOverlayTintMask == 2;
        color2.rgb *= mix(vec3(1.0), overlayFactor, vec3((g_bColorOverlayMaskLayer2 ? tintMask2 : 1.0) * float(g_bColorOverlayLayer2)));
    #endif

    float flVertexColorMask2 = float(g_nVertexColorMode2 != 2) * mix(height2.g, 1.0, g_nVertexColorMode2 == 0);
    color2.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, flVertexColorMask2);

    height.r -= g_flHeightMapZeroPoint1;
    height2.r -= g_flHeightMapZeroPoint2;

    vec2 weights = GetBlendWeights(
        vec2(height.r, height2.r),
        vec2(g_flHeightMapScale1, g_flHeightMapScale2),
        vec2(g_flHeightMapZeroPoint1, g_flHeightMapZeroPoint2),
        vColorBlendValues
    );

    color = color * weights.x + color2 * weights.y;
    height = height * weights.x + height2 * weights.y;
    normal = normal * weights.x + normal2 * weights.y;
    aoLevels = aoLevels * weights.x + g_vAmbientOcclusionLevels2.xyz * weights.y;

    #if (F_ENABLE_LAYER_3 == 1)
        vec4 color3 = texture(g_tColor3, vTexCoord3.xy);
        vec4 height3 = texture(g_tHeight3, vTexCoord3.xy);
        vec4 normal3 = texture(g_tNormal3, vTexCoord3.xy);

        float tintMask3 = saturate(((height3.g - 0.5) * g_fTintMaskContrast3 + 0.5) * g_fTintMaskBrightness3);
        height3.a = g_bMetalness3 ? height3.a : 0.0;
        normal3.rg = (normal3.rg - 0.5) * g_fTextureNormalContrast3 + 0.5;
        normal3.b = saturate(((normal3.b - 0.5) * g_fTextureRoughnessContrast3 + 0.5) * g_fTextureRoughnessBrightness3);

        vec3 adjust3 = AdjustBrightnessContrastSaturation(color3.rgb, g_fTextureColorBrightness3, g_fTextureColorContrast3, g_fTextureColorSaturation3);
        vec3 color3MaybeAdjusted = mix(color3.rgb, adjust3, bvec3(g_nColorCorrectionMode3 == 1));

        vec3 tintAdjusted3 = mix(color3MaybeAdjusted, adjust3, vec3(tintMask3)).xyz;
        float tintAdjusted3Luma = GetLuma(tintAdjusted3);

        float tintColorAdjusted3LumaRatio = tintAdjusted3Luma / tintColorNormLuma;
        float tintAdjusted3Luma3x = 3.0 * tintAdjusted3Luma;

        vec3 tintResult3 = saturate(mix(adjust3, tintColorNorm * min(tintColorAdjusted3LumaRatio, tintAdjusted3Luma3x * tintColorHighestPoint), vec3((vTintColor_ModelAmount.w * tintMask3) * float(g_bModelTint3))));

        vec3 tintFactor3 = mix(vec3(1.0), (g_vTextureColorTint3.rgb), tintMask3);
        color3.rgb = tintResult3 * tintFactor3;

        #if (F_SHARED_COLOR_OVERLAY == 1)
            // 0=Both, 3=Layer 3
            const bool g_bColorOverlayLayer3 = g_nColorOverlayMode == 0 || g_nColorOverlayMode == 3;
            const bool g_bColorOverlayMaskLayer3 = g_nColorOverlayTintMask == 0 || g_nColorOverlayTintMask == 3;
            color3.rgb *= mix(vec3(1.0), overlayFactor, vec3((g_bColorOverlayMaskLayer3 ? tintMask3 : 1.0) * float(g_bColorOverlayLayer3)));
        #endif

        float flVertexColorMask3 = float(g_nVertexColorMode3 != 2) * mix(height3.g, 1.0, g_nVertexColorMode3 == 0);
        color3.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, flVertexColorMask3);

        height3.r -= g_flHeightMapZeroPoint3;

        vec2 weights3 = GetBlendWeights(
            vec2(max(height.r, height2.r), height3.r),
            vec2(max(g_flHeightMapScale1, g_flHeightMapScale2), g_flHeightMapScale3),
            vec2(max(g_flHeightMapZeroPoint1, g_flHeightMapZeroPoint2), g_flHeightMapZeroPoint3),
            vec4(vColorBlendValues.y, vColorBlendValues.w, 0.0, 0.0)
        );

        color = color * (1.0 - weights3.y) + color3 * weights3.y;
        height = height * (1.0 - weights3.y) + height3 * weights3.y;
        normal = normal * (1.0 - weights3.y) + normal3 * weights3.y;
        aoLevels = aoLevels * (1.0 - weights3.y) + g_vAmbientOcclusionLevels3.xyz * weights3.y;
    #endif
#endif

    mat.Albedo = color.rgb;
    mat.AmbientOcclusion = color.a;

    // Alpha test
    #if (F_ALPHA_TEST == 1)
        mat.AmbientOcclusion = height.b;
        mat.Opacity = AlphaTestAntiAliasing(color.a, vTexCoord.xy);

        if (mat.Opacity - 0.001 < g_flAlphaTestReference)   discard;
    #endif

    mat.AmbientOcclusion = mix(aoLevels.x, aoLevels.z, pow(max(mat.AmbientOcclusion, 0.0), max(aoLevels.y, 0.001)));

    // Normals and Roughness
    mat.NormalMap = DecodeHemiOctahedronNormal(normal.rg);
    mat.RoughnessTex = normal.bb;

    // Detail texture
    #if (F_DETAIL_NORMAL == 1)
        // this is wrong. todo
        vec2 detailNormal = texture(g_tNormalDetail1, vDetailTexCoords).rg;
        mat.NormalMap.xy = (mat.NormalMap.xy + detailNormal) * vec2(0.5);
    #endif

    mat.Normal = calculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    mat.Height = height.r;
    mat.Metalness = height.a;

    AdjustRoughnessByGeometricNormal(mat);

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;

    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;

    const vec3 F0 = vec3(0.04);
	mat.SpecularColor = mix(F0, mat.Albedo, mat.Metalness);

    mat.DiffuseAO = vec3(mat.AmbientOcclusion);
    mat.SpecularAO = mat.AmbientOcclusion;

    return mat;
}

// MAIN

void main()
{
    vec3 vertexNormal = SwitchCentroidNormal(vNormalOut, vCentroidNormalOut);

    // Get material
    MaterialProperties_t mat = GetMaterial(vertexNormal);

    LightingTerms_t lighting = CalculateLighting(mat);

    // Combining pass
    ApplyAmbientOcclusion(lighting, mat);

    vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;
    vec3 specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect;

    vec3 combinedLighting = mat.DiffuseColor * diffuseLighting + specularLighting;

    ApplyFog(combinedLighting, mat.PositionWS);

    outputColor = vec4(combinedLighting, mat.Opacity);

    if (HandleMaterialRenderModes(outputColor, mat)
    || HandleLightingRenderModes(outputColor, mat, lighting)
    || HandleUVRenderModes(outputColor, mat, g_tColor1, vTexCoord.xy))
    {
        //
    }
    else if (g_iRenderMode == renderMode_Cubemaps)
    {
        // No bumpmaps, full reflectivity
        vec3 viewmodeEnvMap = GetEnvironment(mat).rgb;
        outputColor.rgb = viewmodeEnvMap;
    }
    else if (g_iRenderMode == renderMode_Tint)
    {
        outputColor = vec4(SrgbGammaToLinear(vTintColor_ModelAmount.rgb), vVertexColor_Alpha.a);
    }
    else if (g_iRenderMode == renderMode_VertexColor)
    {
        outputColor.rgb = SrgbGammaToLinear(vVertexColor_Alpha.rgb);
    }
#if defined(csgo_environment_blend_vfx)
    else if (g_iRenderMode == renderMode_TerrainBlend)
    {
        outputColor.rgb = SrgbGammaToLinear(vColorBlendValues.rgb);
    }
#endif
}
