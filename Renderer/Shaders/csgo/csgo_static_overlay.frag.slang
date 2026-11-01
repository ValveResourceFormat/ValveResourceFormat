#version 460

#include "csgo_static_overlay_features.slang"

// The blend mode 2 cuts out by the alpha
#define ALPHA_TEST_BY_BLEND_MODE (F_BLEND_MODE == 2)

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"

uniform float g_flOpacityScale = 1.0;
uniform mat4 g_mTextureColorAdjust; // evaluated on the CPU from g_fTextureColor* params

uniform float g_flAlphaTestReference = 0.5;

vec3 AdjustTextureColor(vec3 color)
{
    return (vec4(color, 1.0) * g_mTextureColorAdjust).rgb;
}

#include "../features/self_illum_ps.slang"

#if (F_LIT == 1) && (F_MATERIAL_REFERENCE == 1)
    uniform sampler2D g_tColor1; // SrgbRead(true) Sampler(UserConfig)
#else
    uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture

    #if (F_TINT_MASK == 1)
        uniform sampler2D g_tTintMask;
    #endif

    // Vertex paint replaces the tint
    vec3 ApplyTint(vec3 albedo, vec2 texCoord)
    {
        #if (F_TINT_MASK == 1) && (F_PAINT_VERTEX_COLORS == 0)
            return albedo * GetMaskedVertexTint(texture(g_tTintMask, texCoord).x);
        #else
            return albedo * vVertexColorOut.rgb;
        #endif
    }

    void InitVertexAlphaOpacity(inout MaterialProperties_t mat, float textureAlpha, vec2 texCoord)
    {
        float alpha = textureAlpha * vVertexColorOut.a;
        InitOpacity(mat, alpha, g_flOpacityScale, IsBlendModeTranslucent(F_BLEND_MODE), false, texCoord, g_flAlphaTestReference);
    }
#endif

vec3 ToBlendModeColor(vec3 color)
{
#if (F_BLEND_MODE == 3)
    return SrgbLinearToGamma(color);
#else
    return color;
#endif
}

// Not lit: the color texture and the tint
#if (F_LIT == 0)
    MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
    {
        MaterialProperties_t mat;
        InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

        vec4 color = texture(g_tColor, texCoord);
        color.rgb = ToBlendModeColor(color.rgb);

        mat.Albedo = color.rgb;

        InitVertexAlphaOpacity(mat, color.a, texCoord);

        // The texture color is adjusted before the tint
        mat.Albedo = AdjustTextureColor(mat.Albedo);
        mat.Albedo = ApplyTint(mat.Albedo, texCoord);

        FinishUnlitMaterial(mat);
        return mat;
    }

// Lit by the layer textures of the material it references
#elif (F_MATERIAL_REFERENCE == 1)
    uniform sampler2D g_tNormal1; // Sampler(UserConfig)
    uniform sampler2D g_tHeight1; // Sampler(UserConfig)

    uniform float g_fTextureNormalContrast1 = 1.0;
    uniform float g_fTextureRoughnessContrast1 = 1.0;
    uniform float g_fTextureRoughnessBrightness1 = 1.0;
    uniform bool g_bMetalness1 = true;
    uniform float g_fTintMaskContrast1 = 1.0;
    uniform float g_fTintMaskBrightness1 = 1.0;
    uniform float g_flBlendSoftness1 = 0.01;
    uniform float g_flHeightMapScale1 = 1.0;
    uniform float g_flHeightMapZeroPoint1 = 0.5;

    // The layer shows where its height rises above the translucency mask, faded by the vertex alpha
    float GetLayerBlendOpacity(float height, vec2 texCoord)
    {
        float softness = max(0.001, g_flBlendSoftness1);
        float translucency = texture(g_tSelfIllumMask, texCoord).a;

        // Unlike the game, the blend softness does not widen with the texture mip level
        float maskLevel = (0.5 - translucency * vVertexColorOut.a) * 2.0 + softness;
        float heightLevel = ((height - g_flHeightMapZeroPoint1) * g_flHeightMapScale1 - g_flHeightMapZeroPoint1) * g_flHeightMapScale1 - softness;

        float highestLevel = max(maskLevel, heightLevel);
        float maskWeight = max(maskLevel - (highestLevel - softness), 0.0) + 0.001;
        float heightWeight = max(heightLevel - (highestLevel - softness), 0.0) + 0.001;
        return heightWeight / (maskWeight + heightWeight);
    }

    MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
    {
        MaterialProperties_t mat;
        InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

        vec4 color = texture(g_tColor1, texCoord);
        vec4 normalTexture = texture(g_tNormal1, texCoord);
        vec4 heightTexture = texture(g_tHeight1, texCoord);

        color.rgb = ToBlendModeColor(color.rgb);

        mat.Albedo = color.rgb;

        #if (F_BLEND_MODE == 2)
            float alpha = color.a;
        #else
            float alpha = GetLayerBlendOpacity(heightTexture.r, texCoord);
        #endif
        InitOpacity(mat, alpha, g_flOpacityScale, IsBlendModeTranslucent(F_BLEND_MODE), false, texCoord, g_flAlphaTestReference);

        // The layer's color adjustments and model tint are not applied. Vertex paint tints where the layer's tint mask is
        #if (F_PAINT_VERTEX_COLORS == 1)
            float tintMask = ContrastBrightness(heightTexture.g, g_fTintMaskContrast1, g_fTintMaskBrightness1);
            mat.Albedo *= mix(vec3(1.0), vVertexColorOut.rgb, tintMask);
        #endif

        mat.IllumColor = SampleSelfIllumination(texCoord, mat.Albedo);

        mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
        mat.NormalMap = BlendNormalMapTowardsFlat(mat.NormalMap, g_fTextureNormalContrast1);
        mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, 1.0);
        mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

        mat.RoughnessTex = vec2(ContrastBrightness(normalTexture.b, g_fTextureRoughnessContrast1, g_fTextureRoughnessBrightness1));
        mat.Metalness = g_bMetalness1 ? heightTexture.a : 0.0;

        FinishLitMaterial(mat, texCoord, vec3(0.04));

        return mat;
    }

// Lit by its own layer textures
#else
    #include "../features/automatic_pbr_color_fitting_ps.slang"

    uniform sampler2D g_tNormal; // Sampler(UserConfig)
    uniform sampler2D g_tMetalness;
    uniform sampler2D g_tAmbientOcclusion;

    uniform float g_fTextureNormalContrast = 1.0;
    uniform float g_fTextureRoughnessContrast = 1.0;
    uniform float g_fTextureRoughnessBrightness = 1.0;
    uniform int F_AUTOMATIC_PBR_COLOR_FITTING;

    MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
    {
        MaterialProperties_t mat;
        InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

        vec4 color = texture(g_tColor, texCoord);
        vec4 normalTexture = texture(g_tNormal, texCoord);
        color.rgb = ToBlendModeColor(color.rgb);

        mat.Albedo = color.rgb;

        InitVertexAlphaOpacity(mat, color.a, texCoord);
        mat.Albedo = ApplyTint(mat.Albedo, texCoord);

        mat.IllumColor = SampleSelfIllumination(texCoord, mat.Albedo);

        // The texture color is adjusted after the tint
        mat.Albedo = AdjustTextureColor(mat.Albedo);

        mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
        mat.NormalMap.xy *= g_fTextureNormalContrast;
        mat.NormalMap = ApplyNormalMapStrength(mat.NormalMap, 1.0);
        mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

        mat.RoughnessTex = ContrastBrightness(normalTexture.bb, g_fTextureRoughnessContrast, g_fTextureRoughnessBrightness);
        mat.Metalness = texture(g_tMetalness, texCoord).g;
        mat.AmbientOcclusion = texture(g_tAmbientOcclusion, texCoord).r;

        if (F_AUTOMATIC_PBR_COLOR_FITTING == 1)
        {
            mat.Albedo = FitAlbedoToMaterialLevels(mat.Albedo, mat.Albedo, mat.Metalness, 1.0);
        }

        FinishLitMaterial(mat, texCoord, vec3(0.04));

        return mat;
    }
#endif

void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.BlendMode = F_BLEND_MODE;
    options.OccludeDirectDiffuse = false;
    options.FogMod2x = F_BLEND_MODE == 3;

#if (F_LIT == 0)
    RenderUnlitSurface(g_tColor, options);
#elif (F_MATERIAL_REFERENCE == 1)
    RenderSurface(g_tColor1, options);
#else
    RenderSurface(g_tColor, options);
#endif
}
