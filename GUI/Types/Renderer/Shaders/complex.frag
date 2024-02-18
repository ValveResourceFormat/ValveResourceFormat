#version 460

#include "common/utils.glsl"
#include "common/features.glsl"
#include "common/ViewConstants.glsl"
#include "common/LightingConstants.glsl"
#include "complex_features.glsl"

// Render modes -- Switched on/off by code
#define renderMode_Illumination 0
#define renderMode_Diffuse 0
#define renderMode_Specular 0
#define renderMode_Cubemaps 0
#define renderMode_Irradiance 0
#define renderMode_Tint 0
#define renderMode_FoliageParams 0
#define renderMode_TerrainBlend 0
#define renderMode_LightmapShadows 0

#if defined(vr_complex_vfx) || defined(csgo_complex_vfx)
    #define complex_vfx_common
#elif defined(vr_simple_vfx) || defined(csgo_simple_vfx)
    #define simple_vfx_common
#elif defined(vr_simple_2way_blend_vfx) || defined (csgo_simple_2way_blend_vfx) || defined(steampal_2way_blend_mask_vfx)
    #define simple_blend_common
#elif defined(vr_glass_vfx) || defined(vr_glass_markable_vfx) || defined(csgo_glass_vfx)
    #define glass_vfx_common
#elif defined(csgo_lightmappedgeneric_vfx) || defined(csgo_vertexlitgeneric_vfx)
    #define csgo_generic_vfx_common
#elif defined(vr_static_overlay_vfx) || defined(csgo_static_overlay_vfx)
    #define static_overlay_vfx_common
#endif

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define D_OIT_PASS 0

// BLENDING
#define F_FULLBRIGHT 0
#define F_LIT 0
#define F_UNLIT 0
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0
#define F_BLEND_MODE 0
#define F_GLASS 0
#define F_DISABLE_TONE_MAPPING 0
#define F_MORPH_SUPPORTED 0
//#define F_WRINKLE 0
#define F_SCALE_NORMAL_MAP 0
// TEXTURING
#define F_TINT_MASK 0
#define F_NORMAL_MAP 0
#define F_FANCY_BLENDING 0
#define F_METALNESS_TEXTURE 0
#define F_AMBIENT_OCCLUSION_TEXTURE 0
#define F_SELF_ILLUM 0
#define F_ENABLE_AMBIENT_OCCLUSION 0
#define F_ENABLE_TINT_MASKS 0
#define F_DECAL_TEXTURE 0
uniform int F_DECAL_BLEND_MODE;
// SHADING
#define F_SPECULAR 0
#define F_SPECULAR_INDIRECT 0
#define F_RETRO_REFLECTIVE 0
#define F_ANISOTROPIC_GLOSS 0
#define F_SPECULAR_CUBE_MAP_ANISOTROPIC_WARP 0 // only optional in HLA
#define F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS 0
#define F_CLOTH_SHADING 0
#define F_USE_BENT_NORMALS 0
#define F_DIFFUSE_WRAP 0
//#define F_TRANSMISSIVE_BACKFACE_NDOTL 0 // todo
#define F_NO_SPECULAR_AT_FULL_ROUGHNESS 0
// SKIN
//#define F_SUBSURFACE_SCATTERING 0 // todo, same preintegrated method as vr_skin in HLA
//#define F_USE_FACE_OCCLUSION_TEXTURE 0 // todo, weird
//#define F_USE_PER_VERTEX_CURVATURE 0 // todo
//#define F_SSS_MASK 0 // todo

// vr_standard
#define F_HIGH_QUALITY_GLOSS 0
#define F_BLEND_NORMALS 0

#define F_EYEBALLS 0
//End of feature defines

in vec3 vFragPosition;
in vec3 vNormalOut;
centroid in vec3 vCentroidNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec2 vTexCoordOut;
in vec4 vVertexColorOut;

layout (location = 0) out vec4 outputColor;

uniform sampler2D g_tColor; // SrgbRead(true)
uniform sampler2D g_tNormal;
uniform sampler2D g_tTintMask;

#if defined(foliage_vfx_common)
    in vec3 vFoliageParamsOut;
#endif

#if defined(vr_complex_vfx)
    #define S_SPECULAR F_SPECULAR
#elif defined(csgo_generic_vfx_common)
    #define S_SPECULAR F_SPECULAR_INDIRECT
#elif defined(generic_vfx)
    #define S_SPECULAR 0
#else
    #define S_SPECULAR 1 // Indirect
#endif

#define TINT_NOT_APPLIED

#if (defined(csgo_generic_vfx_common) && F_LAYERS > 0)
    #define csgo_generic_blend
#endif

#if (defined(simple_blend_common) || defined(csgo_generic_blend) || defined(vr_standard_vfx_blend) || defined(environment_blend_vfx))
    #if !defined(steampal_2way_blend_mask_vfx) // blending without vertex paint
        in vec4 vColorBlendValues;
    #endif
    uniform sampler2D g_tLayer2Color; // SrgbRead(true)
    uniform sampler2D g_tLayer2NormalRoughness;
    uniform vec2 g_vTexCoordScale2 = vec2(1.0);

    #define terrain_blend_common
#endif

#if defined(vr_skin_vfx)
    uniform sampler2D g_tCombinedMasks;
    uniform vec3 g_vTransmissionColor = vec3(0.74902, 0.231373, 0.011765);
    uniform float g_flMouthInteriorBrightnessScale = 1.0;
#endif


#define _uniformMetalness (defined(simple_vfx_common) || defined(complex_vfx_common)) && (F_METALNESS_TEXTURE == 0)
#define _colorAlphaMetalness ((defined(simple_vfx_common) || defined(complex_vfx_common)) && (F_METALNESS_TEXTURE == 1) || defined(pbr_vfx))
#define _colorAlphaAO (defined(vr_simple_vfx) && (F_AMBIENT_OCCLUSION_TEXTURE == 1) && (F_METALNESS_TEXTURE == 0)) || (defined(simple_blend_common) && (F_ENABLE_AMBIENT_OCCLUSION == 1))
#define _metalnessTexture (defined(complex_vfx_common) && (F_METALNESS_TEXTURE == 1) && ((F_RETRO_REFLECTIVE == 1) || (F_ALPHA_TEST == 1) || (F_TRANSLUCENT == 1))) || defined(csgo_weapon_vfx) || defined(csgo_character_vfx) || defined(csgo_vertexlitgeneric_vfx)
#define _ambientOcclusionTexture ((defined(vr_simple_vfx) && (F_AMBIENT_OCCLUSION_TEXTURE == 1) && (F_METALNESS_TEXTURE == 1)) || defined(complex_vfx_common) || defined(csgo_foliage_vfx) || defined(csgo_weapon_vfx) || defined(csgo_character_vfx) || defined(csgo_generic_vfx_common) || defined(pbr_vfx))

#define unlit (defined(vr_unlit_vfx) || defined(unlit_vfx) || defined(csgo_unlitgeneric_vfx) || (F_FULLBRIGHT == 1) || (F_UNLIT == 1) || (defined(static_overlay_vfx_common) && F_LIT == 0)) || defined(csgo_decalmodulate_vfx)
#define alphatest (F_ALPHA_TEST == 1) || ((defined(csgo_unlitgeneric_vfx) || defined(static_overlay_vfx_common)) && (F_BLEND_MODE == 2)) || defined(csgo_decalmodulate_vfx)
#define translucent (F_TRANSLUCENT == 1) || (F_GLASS == 1) || (F_BLEND_MODE > 0 && F_BLEND_MODE != 2) || defined(glass_vfx_common) || defined(csgo_decalmodulate_vfx) || ((defined(csgo_unlitgeneric_vfx) || defined(static_overlay_vfx_common)) && (F_BLEND_MODE == 1)) // need to set this up on the cpu side
#define selfillum ((F_SELF_ILLUM == 1 && (defined(generic_vfx) || defined(complex_vfx_common) || defined(csgo_vertexlitgeneric_vfx) || defined(vr_skin_vfx))) || defined(csgo_unlitgeneric_vfx))
#define blendMod2x (F_BLEND_MODE == 3) || defined(csgo_decalmodulate_vfx)

#if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
    in vec2 vTexCoord2;
    uniform bool g_bUseSecondaryUvForAmbientOcclusion = true;
    #if F_TINT_MASK
        uniform bool g_bUseSecondaryUvForTintMask = true;
    #endif
    #if F_DETAIL_TEXTURE > 0
        uniform bool g_bUseSecondaryUvForDetailMask = true;
    #endif
    #if (selfillum)
        uniform bool g_bUseSecondaryUvForSelfIllum = false;
    #endif
#endif

#if (alphatest)
    uniform float g_flAlphaTestReference = 0.5;
#endif

#if (translucent)
    uniform float g_flOpacityScale = 1.0;
#endif

#if (D_OIT_PASS == 1)
#include "common/translucent.glsl"
#endif


#if (selfillum)
    #if !defined(vr_skin_vfx) // Shaders that pack the mask into another texture
        uniform sampler2D g_tSelfIllumMask;
    #endif
    uniform float g_flSelfIllumAlbedoFactor = 0.0;
    uniform float g_flSelfIllumBrightness = 0.0;
    uniform float g_flSelfIllumScale = 1.0;
    uniform vec2 g_vSelfIllumScrollSpeed = vec2(0.0);
    uniform vec3 g_vSelfIllumTint = vec3(1.0);

    vec3 GetStandardSelfIllumination(float flSelfIllumMask, vec3 vAlbedo)
    {
        vec3 selfIllumScale = (exp2(g_flSelfIllumBrightness) * g_flSelfIllumScale) * SrgbGammaToLinear(g_vSelfIllumTint.rgb);
        return selfIllumScale * flSelfIllumMask * mix(vec3(1.0), vAlbedo, g_flSelfIllumAlbedoFactor);
    }
#endif

#if defined(csgo_glass_vfx)
    uniform vec2 g_flTranslucencyRemap = vec2(0.0, 1.0);
#endif

#if (_uniformMetalness)
    uniform float g_flMetalness = 0.0;
#elif (_metalnessTexture)
    uniform sampler2D g_tMetalness;
#elif defined(vr_standard_vfx) && (F_METALNESS_TEXTURE == 1)
    uniform sampler2D g_tMetalnessReflectance;
#endif

#if (F_FANCY_BLENDING > 0)
    uniform sampler2D g_tBlendModulation;
    uniform float g_flBlendSoftness;
#endif


#if defined(environment_blend_vfx)
    #define g_tColor1 g_tColor
    #define g_tNormalRoughness1 g_tNormal
    uniform sampler2D g_tPacked1;

    #define g_tColor2 g_tLayer2Color
    #define g_tNormalRoughness2 g_tLayer2NormalRoughness
    uniform sampler2D g_tPacked2;
    uniform sampler2D g_tRevealMask2;
    uniform float g_flRevealSoftness2;

    // todo: layer 3
    // todo: overlay

    //uniform sampler2D g_tColorOverlay;
    //uniform sampler2D g_tNormalRoughnessOverlay;
    //uniform sampler2D g_tRevealOverlay;
#endif

#if defined(simple_blend_common)
    uniform sampler2D g_tMask;
    uniform float g_flMetalnessA = 0.0;
    uniform float g_flMetalnessB = 0.0;

    #if defined(steampal_2way_blend_mask_vfx)
        uniform float g_BlendFalloff = 0.0;
        uniform float g_BlendHeight = 0.0;
    #endif
#endif

#if defined(vr_standard_vfx)
    #if (F_HIGH_QUALITY_GLOSS == 1)
        #define ANISO_ROUGHNESS
        uniform sampler2D g_tNormal2;
        uniform sampler2D g_tGloss;
    #endif

    #if defined(vr_standard_vfx_blend)

        #if (F_SPECULAR == 1)
            // uniform sampler2D g_tColor1;
            uniform sampler2D g_tLayer1Color; // SrgbRead(true)
            #define VR_STANDARD_Color2 g_tLayer1Color
        #else
            uniform sampler2D g_tLayer1Color2; // SrgbRead(true)
            uniform sampler2D g_tLayer2Color2; // SrgbRead(true)
            #define VR_STANDARD_Color2 g_tLayer1Color2
        #endif

        uniform sampler2D g_tLayer1RevealMask;
        uniform float g_flLayer1BlendSoftness = 0.5;

        #if (F_BLEND_NORMALS == 1)
            uniform sampler2D g_tLayer1Normal;
        #endif
    #endif

#endif

#if (F_RETRO_REFLECTIVE == 1)
    uniform float g_flRetroReflectivity = 1.0;
#endif

#if (F_SCALE_NORMAL_MAP == 1)
    uniform float g_flNormalMapScaleFactor = 1.0;
#endif

#if defined(csgo_generic_vfx_common)
    uniform float g_flBumpStrength = 1.0;
#endif

#if (_ambientOcclusionTexture)
    uniform sampler2D g_tAmbientOcclusion;
#endif

#if (F_ANISOTROPIC_GLOSS == 1) // complex, csgo_character
    #define ANISO_ROUGHNESS
    uniform sampler2D g_tAnisoGloss;
#endif

#include "common/lighting_common.glsl"
#include "common/fullbright.glsl"
#include "common/texturing.glsl"
#include "common/pbr.glsl"
#include "common/fog.glsl"

#include "common/environment.glsl" // (S_SPECULAR == 1 || renderMode_Cubemaps == 1)

// Must be last
#include "common/lighting.glsl"

#include "features/csgo_character_eyes_ps.glsl"

// Get material properties
MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals);

    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    // Blending
#if defined(terrain_blend_common)
    vec2 texCoordB = texCoord * g_vTexCoordScale2.xy;

    #if defined(vr_standard_vfx_blend)
        vec4 color2 = texture(VR_STANDARD_Color2, texCoordB);
        vec4 normalTexture2 = normalTexture;
        #if (F_BLEND_NORMALS == 1)
            normalTexture2 = texture(g_tLayer1Normal, texCoordB);
        #endif
    #else
        vec4 color2 = texture(g_tLayer2Color, texCoordB);
        vec4 normalTexture2 = texture(g_tLayer2NormalRoughness, texCoordB);
    #endif

    // 0: VertexBlend 1: BlendModulateTexture,rg 2: NewLayerBlending,g 3: NewLayerBlending,a
    #if (defined(csgo_generic_vfx_common) && F_FANCY_BLENDING > 0)
        float blendFactor = vColorBlendValues.r;
        vec4 blendModTexel = texture(g_tBlendModulation, texCoordB);

        #if (F_FANCY_BLENDING == 1)
            blendFactor = ApplyBlendModulation(blendFactor, blendModTexel.g, blendModTexel.r);
        #elif (F_FANCY_BLENDING == 2)
            blendFactor = ApplyBlendModulation(blendFactor, blendModTexel.g, g_flBlendSoftness);
        #elif (F_FANCY_BLENDING == 3)
            blendFactor = ApplyBlendModulation(blendFactor, blendModTexel.a, g_flBlendSoftness);
        #endif
    #elif defined(steampal_2way_blend_mask_vfx)
        float blendFactor = texture(g_tMask, texCoordB).x;

        blendFactor = ApplyBlendModulation(blendFactor, g_BlendFalloff, g_BlendHeight);

    #elif (defined(simple_blend_common))
        float blendFactor = vColorBlendValues.r;
        vec4 blendModTexel = texture(g_tMask, texCoordB);

        #if defined(csgo_simple_2way_blend_vfx)
            float softnessPaint = vColorBlendValues.a;
        #else
            float softnessPaint = vColorBlendValues.g;
        #endif

        blendFactor = ApplyBlendModulation(blendFactor, blendModTexel.r, softnessPaint);
    #elif (defined(vr_standard_vfx_blend))
        float blendFactor = vColorBlendValues.r;
        vec4 blendModTexel = texture(g_tLayer1RevealMask, texCoordB);

        blendFactor = ApplyBlendModulation(blendFactor, blendModTexel.g, blendModTexel.r * g_flLayer1BlendSoftness);
    #elif defined(environment_blend_vfx)
        float blendFactor = vColorBlendValues.r;
        float revealMask = texture(g_tRevealMask2, texCoordB).r;
        blendFactor = ApplyBlendModulation(blendFactor, revealMask, g_flRevealSoftness2);
    #else
        float blendFactor = vColorBlendValues.r;
    #endif

    #if (defined(simple_blend_common) && F_ENABLE_TINT_MASKS == 1)
        #undef TINT_NOT_APPLIED
        vec2 tintMasks = texture(g_tTintMask, texCoord).xy;

        vec3 tintFactorA = 1.0 - tintMasks.x * (1.0 - vVertexColorOut.rgb);
        vec3 tintFactorB = 1.0 - tintMasks.y * (1.0 - vVertexColorOut.rgb);

        color.rgb *= tintFactorA;
        color2.rgb *= tintFactorB;
    #endif

    color = mix(color, color2, blendFactor);
    // It's more correct to blend normals after decoding, but it's not actually how S2 does it
    normalTexture = mix(normalTexture, normalTexture2, blendFactor);

    #if defined(environment_blend_vfx)
        vec3 packed1 = texture(g_tPacked1, texCoord).rgb;
        vec3 packed2 = texture(g_tPacked2, texCoord).rgb;
        vec3 packedBlended = mix(packed1, packed2, blendFactor);
        mat.AmbientOcclusion = packedBlended.r;
        mat.Metalness = packedBlended.g;
        mat.Height = packedBlended.b;
    #endif
#endif

    float flSelfIllumMask = 0.0;

    // Vr_skin unique stuff
    #if defined(vr_skin_vfx)
        // r=MouthMask, g=AO, b=selfillum/tint mask, a=SSS/opacity
        vec4 combinedMasks = texture(g_tCombinedMasks, texCoord);

        mat.ExtraParams.a = combinedMasks.x; // Mouth Mask
        mat.AmbientOcclusion = combinedMasks.y;

        #if (F_SELF_ILLUM == 1)
            flSelfIllumMask = combinedMasks.z;
        #elif (F_TINT_MASK == 1)
            //float flTintMask = combinedMasks.z;
        #endif

        #if (F_SSS_MASK == 1)
            mat.SSSMask = combinedMasks.a;
        #endif

        #if (translucent) || (alphatest)
            mat.Opacity = combinedMasks.a;
        #endif
    #endif

#if defined(csgo_character_vfx)
    #if (F_SUBSURFACE_SCATTERING == 1)
        //mat.SSSMask = texture(g_tSSSMask, texCoord).g;
    #endif
#endif

    mat.Albedo = color.rgb;

#if (translucent) || (alphatest)
    mat.Opacity = color.a;
#endif

#if (defined(static_overlay_vfx_common) && (F_PAINT_VERTEX_COLORS == 1))
    #undef TINT_NOT_APPLIED
    mat.Albedo *= vVertexColorOut.rgb;
    mat.Opacity *= vVertexColorOut.a;
#endif

#if (translucent)
    mat.Opacity *= g_flOpacityScale;
#endif

    // Alpha test
#if (alphatest)
    mat.Opacity = AlphaTestAntiAliasing(mat.Opacity, texCoord);

    if (mat.Opacity - 0.001 < g_flAlphaTestReference)
    {
        discard;
    }
#endif

    // Tinting
    #if defined(TINT_NOT_APPLIED)
        vec3 tintColor = vVertexColorOut.rgb;

        #if (F_TINT_MASK == 1) // complex_vfx_common, csgo_generic_vfx_common, character, weapon, etc.
            vec2 tintMaskTexcoord = texCoord;
            #if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
                tintMaskTexcoord = (g_bUseSecondaryUvForTintMask || (F_FORCE_UV2 == 1)) ? vTexCoord2 : texCoord;
            #else

            #endif
            float tintStrength = texture(g_tTintMask, tintMaskTexcoord).x;
            tintColor = 1.0 - tintStrength * (1.0 - tintColor.rgb);
        #endif

        mat.Albedo *= tintColor;
    #endif

    #if (selfillum)
        // Standard mask sampling
        #if !defined(vr_skin_vfx)
            vec2 vSelfIllumMaskCoords = texCoord;

            #if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
                vSelfIllumMaskCoords = (g_bUseSecondaryUvForSelfIllum || (F_FORCE_UV2 == 1)) ? vTexCoord2 : texCoord;
            #endif

            vSelfIllumMaskCoords += fract(g_vSelfIllumScrollSpeed.xy * g_flTime);
            flSelfIllumMask = texture(g_tSelfIllumMask, vSelfIllumMaskCoords).r;
        #endif

        mat.IllumColor = GetStandardSelfIllumination(flSelfIllumMask, mat.Albedo);
    #endif

    #if (unlit)
        return mat;
    #endif

    #if defined(vr_standard_vfx) && (F_HIGH_QUALITY_GLOSS == 1)
        normalTexture = texture(g_tNormal2, texCoord);
    #endif

    // Normals and Roughness
    #if defined(generic_vfx) || defined(crystal_vfx) || defined(vr_standard_vfx) || defined(vr_eyeball_vfx)
        mat.NormalMap = DecodeDxt5Normal(normalTexture);
    #else
        mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    #endif

#if defined(ANISO_ROUGHNESS)
    #if (F_ANISOTROPIC_GLOSS == 1)
        mat.RoughnessTex.xy = texture(g_tAnisoGloss, texCoord).rg;
    #endif
    #if defined(vr_standard_vfx) && (F_HIGH_QUALITY_GLOSS == 1)
        mat.RoughnessTex.xy = texture(g_tGloss, texCoord).ag;
    #endif
#else
    mat.RoughnessTex.xy = normalTexture.bb;
#endif


#if (F_SCALE_NORMAL_MAP == 1)
    mat.NormalMap = normalize(mix(vec3(0, 0, 1), mat.NormalMap, g_flNormalMapScaleFactor));
#elif defined(csgo_generic_vfx_common)
    mat.NormalMap = normalize(mix(vec3(0, 0, 1), mat.NormalMap, g_flBumpStrength));
#endif

    // Detail texture
#if (F_DETAIL_TEXTURE > 0)
    #if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
        vec2 detailMaskCoords = (g_bUseSecondaryUvForDetailMask || (F_FORCE_UV2 == 1)) ? vTexCoord2 : texCoord;
    #else
        vec2 detailMaskCoords = texCoord;
    #endif
    applyDetailTexture(mat.Albedo, mat.NormalMap, detailMaskCoords);
#endif

    mat.Normal = calculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);

    // Metalness
#if (_metalnessTexture)
    // a = rimmask
    vec4 metalnessTexture = texture(g_tMetalness, texCoord);

    mat.Metalness = metalnessTexture.g;

    #if (F_RETRO_REFLECTIVE == 1)
        // not exclusive to csgo_character
        mat.ExtraParams.x = metalnessTexture.r;
    #endif
    #if defined(csgo_character_vfx)
        mat.ClothMask = metalnessTexture.b * (1.0 - metalnessTexture.g);
    #elif defined(csgo_weapon_vfx)
        mat.RoughnessTex.xy = metalnessTexture.rr;
    #endif
#elif (_uniformMetalness)
    mat.Metalness = g_flMetalness;
#elif (_colorAlphaMetalness)
    mat.Metalness = color.a;
#elif defined(simple_blend_common)
    mat.Metalness = mix(g_flMetalnessA, g_flMetalnessB, blendFactor);
#elif defined(vr_standard_vfx) && (F_METALNESS_TEXTURE == 1)
    mat.Metalness = texture(g_tMetalnessReflectance, texCoord).r;
#endif

    // Ambient Occlusion
#if (_colorAlphaAO)
    mat.AmbientOcclusion = color.a;
#elif (_ambientOcclusionTexture)
    #if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
        mat.AmbientOcclusion = texture(g_tAmbientOcclusion, (g_bUseSecondaryUvForAmbientOcclusion || (F_FORCE_UV2 == 1)) ? vTexCoord2 : texCoord).r;
    #else
        mat.AmbientOcclusion = texture(g_tAmbientOcclusion, texCoord).r;
    #endif
#endif

#if defined(vr_complex_vfx) && (F_METALNESS_TEXTURE == 0) && (F_RETRO_REFLECTIVE == 1)
    mat.ExtraParams.x = g_flRetroReflectivity;
#endif
#if defined(vr_complex_vfx) && (F_CLOTH_SHADING == 1)
    mat.ClothMask = 1.0;
#endif

    AdjustRoughnessByGeometricNormal(mat);

#if defined(csgo_character_vfx)
    #if (F_EYEBALLS == 1)
        ApplyEye(eyeInterpolator, texCoord, mat);
    #endif
#endif

#if (F_USE_BENT_NORMALS == 1)
    GetBentNormal(mat, texCoord);
#else
    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;
#endif


#if (F_DECAL_TEXTURE == 1)
    mat.Albedo = ApplyDecalTexture(mat.Albedo);
#endif

    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;

    vec3 F0 = vec3(0.04);

    #if (F_CLOTH_SHADING == 1) && defined(csgo_character_vfx)
        F0 = ApplySheen(0.04, mat.Albedo, mat.ClothMask);
    #elif defined(csgo_weapon_vfx)
        F0 = vec3(0.02);
    #endif

    mat.SpecularColor = mix(F0, mat.Albedo, mat.Metalness);

    #if defined(vr_skin_vfx)
        mat.TransmissiveColor = SrgbGammaToLinear(g_vTransmissionColor.rgb) * color.a;

        float mouthOcclusion = mix(1.0, g_flMouthInteriorBrightnessScale, mat.ExtraParams.a);
        mat.TransmissiveColor *= mouthOcclusion;
        mat.AmbientOcclusion *= mouthOcclusion;
    #endif

    #if (F_GLASS == 1) || defined(vr_glass_vfx)
        vec4 glassResult = GetGlassMaterial(mat);
        mat.Albedo = glassResult.rgb;
        mat.Opacity = glassResult.a;
    #endif

    #if defined(csgo_glass_vfx)
        mat.Opacity = mix(g_flTranslucencyRemap.x, g_flTranslucencyRemap.y, mat.Opacity);
    #endif

    mat.DiffuseAO = vec3(mat.AmbientOcclusion);
    mat.SpecularAO = mat.AmbientOcclusion;

#if defined(ANISO_ROUGHNESS)
    CalculateAnisotropicTangents(mat);
#endif

    return mat;
}

// MAIN

void main()
{
    vec3 vertexNormal = SwitchCentroidNormal(vNormalOut, vCentroidNormalOut);
    vec2 texCoord = vTexCoordOut;

    // Get material
    MaterialProperties_t mat = GetMaterial(texCoord, vertexNormal);
    outputColor.a = mat.Opacity;

    LightingTerms_t lighting;

#if (unlit)
    outputColor.rgb = mat.Albedo + mat.IllumColor;
#else

    lighting = CalculateLighting(mat);

    // Combining pass

    ApplyAmbientOcclusion(lighting, mat);

    vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;
    vec3 specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect;

    #if F_NO_SPECULAR_AT_FULL_ROUGHNESS == 1
        specularLighting = (mat.Roughness == 1.0) ? vec3(0) : specularLighting;
    #endif

    #if defined(S_TRANSMISSIVE_BACKFACE_NDOTL)
        vec3 transmissiveLighting = o.TransmissiveDirect * mat.TransmissiveColor;
    #else
        const vec3 transmissiveLighting = vec3(0.0);
    #endif

    // Unique HLA Membrane blend mode: specular unaffected by opacity
    #if defined(vr_complex_vfx) && (F_TRANSLUCENT == 2)
        vec3 combinedLighting = specularLighting + (mat.DiffuseColor * diffuseLighting + transmissiveLighting + mat.IllumColor) * mat.Opacity;
        outputColor.a = 1.0;
    #else
        vec3 combinedLighting = mat.DiffuseColor * diffuseLighting + specularLighting + transmissiveLighting + mat.IllumColor;
    #endif

    outputColor.rgb = combinedLighting;
#endif

    ApplyFog(outputColor.rgb, mat.PositionWS);

#if (F_DISABLE_TONE_MAPPING == 1)
    outputColor.rgb = SrgbGammaToLinear(outputColor.rgb);
#endif

#if (blendMod2x)
    vec3 gammaOutput = SrgbLinearToGamma(outputColor.rgb);
    outputColor = vec4(mix(vec3(0.5), gammaOutput, vec3(outputColor.a)), outputColor.a);
#endif

    if (HandleMaterialRenderModes(outputColor, mat))
    {
        //
    }
    else if (HandleUVRenderModes(outputColor, mat, g_tColor, vTexCoordOut, vLightmapUVScaled))
    {
        //
    }
    else if (g_iRenderMode == renderMode_Cubemaps)
    {
        // No bumpmaps, full reflectivity
        vec3 viewmodeEnvMap = GetEnvironment(mat).rgb;
        outputColor.rgb = viewmodeEnvMap;
    }
    else if (g_iRenderMode == renderMode_Illumination)
    {
        outputColor = vec4(lighting.DiffuseDirect + lighting.SpecularDirect, 1.0);
    }
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    else if (g_iRenderMode == renderMode_LightmapShadows)
    {
        #if (S_LIGHTMAP_VERSION_MINOR >= 2)
            vec4 dlsh = texture(g_tDirectLightShadows, vLightmapUVScaled);
            outputColor = vec4(vec3(1.0 - dlsh.x) + vec3(1.0 - min3(dlsh.yzw)) * vec3(0.5, 0.5, 0), 1.0);
        #endif
    }
#endif
    else if (g_iRenderMode == renderMode_Tint)
    {
        outputColor = vec4(SrgbGammaToLinear(vVertexColorOut.rgb), vVertexColorOut.a);
    }
#if (F_GLASS == 0)
    else if (g_iRenderMode == renderMode_Irradiance)
    {
        outputColor = vec4(lighting.DiffuseIndirect, 1.0);
    }
#endif
#if defined(foliage_vfx_common)
    else if (g_iRenderMode == renderMode_FoliageParams)
    {
        outputColor.rgb = SrgbGammaToLinear(vFoliageParamsOut.rgb);
    }
#endif
#if defined(terrain_blend_common) && !defined(steampal_2way_blend_mask_vfx)
    else if (g_iRenderMode == renderMode_TerrainBlend)
    {
        outputColor.rgb = SrgbGammaToLinear(vColorBlendValues.rgb);
    }
#endif
#if !(unlit)
    else if (g_iRenderMode == renderMode_Diffuse)
    {
        outputColor.rgb = diffuseLighting * 0.5;
    }
    else if (g_iRenderMode == renderMode_Specular)
    {
        outputColor.rgb = specularLighting;
    }
#endif

#if (D_OIT_PASS == 1)
    outputColor = WeightColorTranslucency(outputColor);
#endif
}
