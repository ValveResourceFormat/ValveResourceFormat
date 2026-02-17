#version 460

#define GameVfx_crystal 0
#define GameVfx_csgo_character 0
#define GameVfx_csgo_complex 0
#define GameVfx_csgo_decalmodulate 0
#define GameVfx_csgo_foliage 0
#define GameVfx_csgo_glass 0
#define GameVfx_csgo_lightmappedgeneric 0
#define GameVfx_csgo_simple 0
#define GameVfx_csgo_simple_2way_blend 0
#define GameVfx_csgo_static_overlay 0
#define GameVfx_csgo_unlitgeneric 0
#define GameVfx_csgo_vertexlitgeneric 0
#define GameVfx_csgo_weapon 0
#define GameVfx_environment_blend 0
#define GameVfx_generic 0
#define GameVfx_pbr 0
#define GameVfx_steampal_2way_blend_mask 0
#define GameVfx_unlit 0
#define GameVfx_vr_complex 0
#define GameVfx_vr_energy_field 0
#define GameVfx_vr_eyeball 0
#define GameVfx_vr_glass 0
#define GameVfx_vr_glass_markable 0
#define GameVfx_vr_simple 0
#define GameVfx_vr_simple_2way_blend 0
#define GameVfx_vr_skin 0
#define GameVfx_vr_standard 0
#define GameVfx_vr_static_overlay 0
#define GameVfx_vr_unlit 0
#define GameVfx_vr_xen_foliage 0

#include "common/utils.slang"
#include "common/features.slang"
#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"
#include "complex_features.slang"

#if (GameVfx_vr_complex != 0) || (GameVfx_csgo_complex != 0)
    #define complex_vfx_common
#elif (GameVfx_vr_simple != 0) || (GameVfx_csgo_simple != 0)
    #define simple_vfx_common
#elif (GameVfx_vr_simple_2way_blend != 0) || (GameVfx_csgo_simple_2way_blend != 0) || (GameVfx_steampal_2way_blend_mask != 0)
    #define simple_blend_common
#elif (GameVfx_vr_glass != 0) || (GameVfx_vr_glass_markable != 0) || (GameVfx_csgo_glass != 0)
    #define glass_vfx_common
#elif (GameVfx_csgo_lightmappedgeneric != 0) || (GameVfx_csgo_vertexlitgeneric != 0)
    #define csgo_generic_vfx_common
#elif (GameVfx_vr_static_overlay != 0) || (GameVfx_csgo_static_overlay != 0)
    #define static_overlay_vfx_common
#endif

//Parameter defines - These are default values and can be overwritten based on material/model parameters
// BLENDING
#define F_FULLBRIGHT 0
#define F_LIT 0
#define F_UNLIT 0
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0
#define F_BLEND_MODE 0
#define F_GLASS 0
uniform int F_DISABLE_TONE_MAPPING;
#define F_SCALE_NORMAL_MAP 0
// TEXTURING
#define F_TINT_MASK 0
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
#define F_ANISOTROPIC_GLOSS 0
#define F_SPECULAR_CUBE_MAP_ANISOTROPIC_WARP 0 // only optional in HLA
#define F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS 0
#define F_USE_BENT_NORMALS 0
#define F_DIFFUSE_WRAP 0
#define F_TRANSMISSIVE_BACKFACE_NDOTL 0
uniform int F_NO_SPECULAR_AT_FULL_ROUGHNESS;
// SKIN
//#define F_SUBSURFACE_SCATTERING 0 // todo, same preintegrated method as vr_skin in HLA
//#define F_USE_FACE_OCCLUSION_TEXTURE 0 // todo, weird
//#define F_USE_PER_VERTEX_CURVATURE 0 // todo
//#define F_SSS_MASK 0 // todo

// vr_standard
#define F_HIGH_QUALITY_GLOSS 0
#define F_BLEND_NORMALS 0

//End of feature defines

in vec3 vFragPosition;
in vec3 vNormalOut;
centroid in vec3 vCentroidNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec2 vTexCoordOut;
centroid in vec4 vVertexColorOut;

out vec4 outputColor;

uniform sampler2D g_tBlueNoise;
uniform sampler2D g_tColor; // SrgbRead(true)
uniform sampler2D g_tNormal;
uniform sampler2D g_tTintMask;

#if defined(foliage_vfx_common)
    in vec4 vFoliageParamsOut;

    uniform float g_flVertexAmbientOcclusionPower = 1.0;
    uniform float g_flVertexAmbientOcclusionAmount = 0.0;
#endif

#if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1)
    uniform int F_DISABLE_TRANSMISSIVE_SHADOWS = 0;
    uniform int F_USE_ALBEDO_FOR_TRANSMISSIVE = 0;
    uniform sampler2D g_tTransmissiveColor; // SrgbRead(true)
#endif

#if (GameVfx_vr_complex != 0)
    #define IBL F_SPECULAR
#elif defined(csgo_generic_vfx_common)
    #define IBL F_SPECULAR_INDIRECT
#elif (GameVfx_generic != 0)
    #define IBL 0
#else
    #define IBL 1 // Indirect
#endif

#define TINT_NOT_APPLIED

#if (defined(csgo_generic_vfx_common) && F_LAYERS > 0)
    #define csgo_generic_blend
#endif

#if (defined(simple_blend_common) || defined(csgo_generic_blend) || defined(vr_standard_vfx_blend) || (GameVfx_environment_blend != 0))
    #if (GameVfx_steampal_2way_blend_mask == 0) // blending without vertex paint
        in vec4 vColorBlendValues;
    #endif
    uniform sampler2D g_tLayer2Color; // SrgbRead(true)
    uniform sampler2D g_tLayer2NormalRoughness;
    uniform vec2 g_vTexCoordScale2 = vec2(1.0);

    #define terrain_blend_common
#endif

#if (GameVfx_vr_skin != 0)
    uniform sampler2D g_tCombinedMasks;
    uniform vec3 g_vTransmissionColor = vec3(0.74902, 0.231373, 0.011765); // SrgbRead(true)
    uniform float g_flMouthInteriorBrightnessScale = 1.0;
#endif


#define _uniformMetalness (defined(simple_vfx_common) || defined(complex_vfx_common)) && (F_METALNESS_TEXTURE == 0)
#define _colorAlphaMetalness ((defined(simple_vfx_common) || defined(complex_vfx_common)) && (F_METALNESS_TEXTURE == 1) || (GameVfx_pbr != 0))
#define _colorAlphaAO ((GameVfx_vr_simple != 0) && (F_AMBIENT_OCCLUSION_TEXTURE == 1) && (F_METALNESS_TEXTURE == 0)) || (defined(simple_blend_common) && (F_ENABLE_AMBIENT_OCCLUSION == 1))
#define _metalnessTexture (defined(complex_vfx_common) && (F_METALNESS_TEXTURE == 1) && ((F_ALPHA_TEST == 1) || (F_TRANSLUCENT == 1))) || (GameVfx_csgo_weapon != 0) || (GameVfx_csgo_character != 0) || (GameVfx_csgo_vertexlitgeneric != 0)
#define _ambientOcclusionTexture (((GameVfx_vr_simple != 0) && (F_AMBIENT_OCCLUSION_TEXTURE == 1) && (F_METALNESS_TEXTURE == 1)) || defined(complex_vfx_common) || (GameVfx_csgo_foliage != 0) || (GameVfx_csgo_weapon != 0) || (GameVfx_csgo_character != 0) || defined(csgo_generic_vfx_common) || (GameVfx_pbr != 0))

#define unlit ((GameVfx_vr_unlit != 0) || (GameVfx_unlit != 0) || (GameVfx_csgo_unlitgeneric != 0) || (F_FULLBRIGHT == 1) || (F_UNLIT == 1) || (defined(static_overlay_vfx_common) && F_LIT == 0)) || (GameVfx_csgo_decalmodulate != 0)
#define alphatest (F_ALPHA_TEST == 1) || (((GameVfx_csgo_unlitgeneric != 0) || defined(static_overlay_vfx_common)) && (F_BLEND_MODE == 2)) || (GameVfx_csgo_decalmodulate != 0)
#define translucent (F_TRANSLUCENT == 1) || (F_GLASS == 1) || (F_BLEND_MODE > 0 && F_BLEND_MODE != 2) || defined(glass_vfx_common) || (GameVfx_csgo_decalmodulate != 0) || (((GameVfx_csgo_unlitgeneric != 0) || defined(static_overlay_vfx_common)) && (F_BLEND_MODE == 1)) // need to set this up on the cpu side
#define selfillum ((F_SELF_ILLUM == 1 && ((GameVfx_generic != 0) || defined(complex_vfx_common) || (GameVfx_csgo_vertexlitgeneric != 0) || (GameVfx_vr_skin != 0))) || (GameVfx_csgo_unlitgeneric != 0))
#define blendMod2x (F_BLEND_MODE == 3) || (GameVfx_csgo_decalmodulate != 0)
#define blendModThenAdd (F_BLEND_MODE == 6)

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

#if (translucent)
    uniform float g_flOpacityScale = 1.0;
#endif

#if (selfillum)
    #if (GameVfx_vr_skin == 0) // Shaders that pack the mask into another texture
        uniform sampler2D g_tSelfIllumMask;
    #endif
    uniform float g_flSelfIllumAlbedoFactor = 0.0;
    uniform float g_flSelfIllumBrightness = 0.0;
    uniform float g_flSelfIllumScale = 1.0;
    uniform vec2 g_vSelfIllumScrollSpeed = vec2(0.0);
    uniform vec3 g_vSelfIllumTint = vec3(1.0); // SrgbRead(true)

    vec3 GetStandardSelfIllumination(float flSelfIllumMask, vec3 vAlbedo)
    {
        vec3 selfIllumScale = (exp2(g_flSelfIllumBrightness) * g_flSelfIllumScale) * g_vSelfIllumTint.rgb;
        return selfIllumScale * flSelfIllumMask * mix(vec3(1.0), vAlbedo, g_flSelfIllumAlbedoFactor);
    }
#endif

#if (GameVfx_csgo_glass != 0)
    uniform vec2 g_flTranslucencyRemap = vec2(0.0, 1.0);
#endif

#if (_uniformMetalness)
    uniform float g_flMetalness = 0.0;
#elif (_metalnessTexture)
    uniform sampler2D g_tMetalness;
#elif (GameVfx_vr_standard != 0) && (F_METALNESS_TEXTURE == 1)
    uniform sampler2D g_tMetalnessReflectance;
#endif

#if (F_FANCY_BLENDING > 0)
    uniform sampler2D g_tBlendModulation;
    uniform float g_flBlendSoftness;
#endif


#if (GameVfx_environment_blend != 0)
    #define g_tColor1 g_tColor
    #define g_tNormalRoughness1 g_tNormal
    uniform sampler2D g_tPacked1;
    uniform float g_fLayerSelfIllumScale1;
    uniform vec3 g_vLayerSelfIllumTint1 = vec3(1.0);

    #define g_tColor2 g_tLayer2Color
    #define g_tNormalRoughness2 g_tLayer2NormalRoughness
    uniform sampler2D g_tPacked2;
    uniform sampler2D g_tRevealMask2;
    uniform float g_flRevealSoftness2;
    uniform float g_fLayerSelfIllumScale2;
    uniform vec3 g_vLayerSelfIllumTint2 = vec3(1.0);

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

    #if (GameVfx_steampal_2way_blend_mask != 0)
        uniform float g_BlendFalloff = 0.0;
        uniform float g_BlendHeight = 0.0;
    #endif
#endif

#if (GameVfx_vr_standard != 0)
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

#if (GameVfx_vr_complex != 0) && (F_METALNESS_TEXTURE == 0)
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

#include "common/fullbright.slang"
#include "common/texturing.slang"
#include "common/fog.slang"

// Must be last
#include "common/lighting.slang"

#include "features/csgo_character_eyes_ps.slang"

// Get material properties
MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut, vBitangentOut);

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
    #elif (GameVfx_steampal_2way_blend_mask != 0)
        float blendFactor = texture(g_tMask, texCoordB).x;

        blendFactor = ApplyBlendModulation(blendFactor, g_BlendFalloff, g_BlendHeight);

    #elif (defined(simple_blend_common))
        float blendFactor = vColorBlendValues.r;
        vec4 blendModTexel = texture(g_tMask, texCoordB);

        #if (GameVfx_csgo_simple_2way_blend != 0)
            float softnessPaint = vColorBlendValues.a;
        #else
            float softnessPaint = vColorBlendValues.g;
        #endif

        blendFactor = ApplyBlendModulation(blendFactor, blendModTexel.r, softnessPaint);
    #elif (defined(vr_standard_vfx_blend))
        float blendFactor = vColorBlendValues.r;
        vec4 blendModTexel = texture(g_tLayer1RevealMask, texCoordB);

        blendFactor = ApplyBlendModulation(blendFactor, blendModTexel.g, blendModTexel.r * g_flLayer1BlendSoftness);
    #elif (GameVfx_environment_blend != 0)
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

    #if (GameVfx_environment_blend != 0)
        vec2 packed1 = texture(g_tPacked1, texCoord).rg;
        vec2 packed2 = texture(g_tPacked2, texCoordB).rg;
        vec3 envBlendIllumColor = mix(
            color.rgb * (packed1.r * g_fLayerSelfIllumScale1) * g_vLayerSelfIllumTint1,
            color2.rgb * (packed2.r * g_fLayerSelfIllumScale2) * g_vLayerSelfIllumTint2,
            blendFactor
        );
    #endif

    color = mix(color, color2, blendFactor);
    // It's more correct to blend normals after decoding, but it's not actually how S2 does it
    normalTexture = mix(normalTexture, normalTexture2, blendFactor);

    #if (GameVfx_environment_blend != 0)
        vec2 packedBlended = mix(packed1, packed2, blendFactor);
        mat.Metalness = packedBlended.g;
    #endif
#endif

    float flSelfIllumMask = 0.0;

    // Vr_skin unique stuff
    #if (GameVfx_vr_skin != 0)
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

#if (GameVfx_csgo_character != 0)
    #if (F_SUBSURFACE_SCATTERING == 1)
        //mat.SSSMask = texture(g_tSSSMask, texCoord).g;
    #endif
#endif

    mat.Albedo = color.rgb;

#if (translucent) || (alphatest)
    mat.Opacity = color.a;
#else
    // D_OPAQUE_FADE
    if (vVertexColorOut.a <= (254.0/255.0))
    {
        vec4 blueNoiseZeroToOne = texelFetch(g_tBlueNoise, ivec2(mod(gl_FragCoord.xy, textureSize(g_tBlueNoise, 0))), 0);
        if (vVertexColorOut.a <= blueNoiseZeroToOne.x)
        {
            discard;
        }
    }
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
        #if (GameVfx_vr_skin == 0)
            vec2 vSelfIllumMaskCoords = texCoord;

            #if (F_SECONDARY_UV == 1) || (F_FORCE_UV2 == 1)
                vSelfIllumMaskCoords = (g_bUseSecondaryUvForSelfIllum || (F_FORCE_UV2 == 1)) ? vTexCoord2 : texCoord;
            #endif

            vSelfIllumMaskCoords += fract(g_vSelfIllumScrollSpeed.xy * g_flTime);
            flSelfIllumMask = texture(g_tSelfIllumMask, vSelfIllumMaskCoords).r;
        #endif

        mat.IllumColor = GetStandardSelfIllumination(flSelfIllumMask, mat.Albedo);
    #elif (GameVfx_environment_blend != 0)
        mat.IllumColor = envBlendIllumColor;
    #endif

    #if (unlit)
        return mat;
    #endif

    #if (GameVfx_vr_standard != 0) && (F_HIGH_QUALITY_GLOSS == 1)
        normalTexture = texture(g_tNormal2, texCoord);
    #endif

    // Normals and Roughness
    #if (GameVfx_generic != 0) || (GameVfx_crystal != 0) || (GameVfx_vr_standard != 0) || (GameVfx_vr_eyeball != 0)
        mat.NormalMap = DecodeDxt5Normal(normalTexture);
    #else
        mat.NormalMap = DecodeHemiOctahedronNormal(normalTexture.rg);
    #endif

#if defined(ANISO_ROUGHNESS)
    #if (F_ANISOTROPIC_GLOSS == 1)
        mat.RoughnessTex.xy = texture(g_tAnisoGloss, texCoord).rg;
    #endif
    #if (GameVfx_vr_standard != 0) && (F_HIGH_QUALITY_GLOSS == 1)
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

    if (F_RETRO_REFLECTIVE == 1)
        mat.ExtraParams.x = metalnessTexture.r;

    #if (GameVfx_csgo_character != 0)
        mat.ClothMask = metalnessTexture.b * (1.0 - metalnessTexture.g);
    #elif (GameVfx_csgo_weapon != 0)
        mat.RoughnessTex.xy = metalnessTexture.rr;
    #endif
#elif (_uniformMetalness)
    mat.Metalness = g_flMetalness;
#elif (_colorAlphaMetalness)
    mat.Metalness = color.a;
#elif defined(simple_blend_common)
    mat.Metalness = mix(g_flMetalnessA, g_flMetalnessB, blendFactor);
#elif (GameVfx_vr_standard != 0) && (F_METALNESS_TEXTURE == 1)
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

    #if defined(foliage_vfx_common)
        vec4 vFoliageParams = clamp(vFoliageParamsOut, vec4(0.001), vec4(1.0));
        mat.AmbientOcclusion *= mix(1.0, pow(vFoliageParams.w, g_flVertexAmbientOcclusionPower), g_flVertexAmbientOcclusionAmount);
    #endif

    #if (F_TRANSMISSIVE_BACKFACE_NDOTL == 1)
        vec3 vTransmissiveColor = texture(g_tTransmissiveColor, texCoord).rgb;
        mat.TransmissiveColor = F_USE_ALBEDO_FOR_TRANSMISSIVE == 1 ? mat.Albedo : vTransmissiveColor;
    #endif

#if (GameVfx_vr_complex != 0) && (F_METALNESS_TEXTURE == 0)
    mat.ExtraParams.x = F_RETRO_REFLECTIVE == 1 ? g_flRetroReflectivity : 0.0;
#endif

#if (GameVfx_vr_complex != 0)
    mat.ClothMask = F_CLOTH_SHADING == 1 ? 1.0 : 0.0;
#endif

    AdjustRoughnessByGeometricNormal(mat);

#if (GameVfx_csgo_character != 0)
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

    #if (GameVfx_vr_energy_field != 0)
        mat.IllumColor = vec3(0.1, 0.3, 0.8);
        mat.Opacity = 0.2;
    #endif


#if (F_DECAL_TEXTURE == 1)
    mat.Albedo = ApplyDecalTexture(mat.Albedo);
#endif

    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;

    vec3 F0 = vec3(0.04);

    #if (GameVfx_csgo_weapon != 0)
        F0 = vec3(0.02);
    #elif (GameVfx_csgo_character != 0)
        if (F_CLOTH_SHADING == 1)
        {
            F0 = ApplySheen(0.04, mat.Albedo, mat.ClothMask);
        }
    #endif

    mat.SpecularColor = mix(F0, mat.Albedo, mat.Metalness);

    #if (GameVfx_vr_skin != 0)
        mat.TransmissiveColor = g_vTransmissionColor.rgb * color.a;

        float mouthOcclusion = mix(1.0, g_flMouthInteriorBrightnessScale, mat.ExtraParams.a);
        mat.TransmissiveColor *= mouthOcclusion;
        mat.AmbientOcclusion *= mouthOcclusion;
    #endif

    #if (F_GLASS == 1) || (GameVfx_vr_glass != 0)
        vec4 glassResult = GetGlassMaterial(mat);
        mat.Albedo = glassResult.rgb;
        mat.Opacity = glassResult.a;
    #endif

    #if (GameVfx_csgo_glass != 0)
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

    LightingTerms_t lighting = InitLighting();

#if (unlit)
    lighting.DiffuseDirect = mat.Albedo + mat.IllumColor;
    outputColor.rgb = lighting.DiffuseDirect;
#else

    lighting = CalculateLighting(mat);
    ApplyAmbientOcclusion(lighting, mat);

    vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;
    vec3 specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect;

    if (F_NO_SPECULAR_AT_FULL_ROUGHNESS == 1)
    {
        specularLighting = (mat.Roughness == vec2(1.0)) ? vec3(0) : specularLighting;
    }

    // Unique HLA Membrane blend mode: specular unaffected by opacity
    #if (GameVfx_vr_complex != 0) && (F_TRANSLUCENT == 2)
        vec3 combinedLighting = specularLighting + (mat.DiffuseColor * diffuseLighting + lighting.TransmissiveDirect + mat.IllumColor) * mat.Opacity;
        outputColor.a = 1.0;
    #else
        vec3 combinedLighting = mat.DiffuseColor * diffuseLighting + specularLighting + lighting.TransmissiveDirect + mat.IllumColor;
    #endif

    outputColor.rgb = combinedLighting;
#endif

    ApplyFog(outputColor.rgb, mat.PositionWS);

    if (F_DISABLE_TONE_MAPPING == 1)
    {
        outputColor.rgb = SrgbGammaToLinear(outputColor.rgb);
    }

#if (blendMod2x || blendModThenAdd)
    vec3 gammaOutput = SrgbLinearToGamma(outputColor.rgb);
    #if (blendMod2x)
        outputColor = vec4(mix(vec3(0.5), gammaOutput, vec3(outputColor.a)), outputColor.a);
    #elif (blendModThenAdd)
        outputColor = vec4(mix(vec3(0), outputColor.rgb, vec3(0.5)), outputColor.a);
    #endif
#endif

    if (HandleMaterialRenderModes(outputColor, mat)
    || HandleLightingRenderModes(outputColor, mat, lighting)
    || HandleUVRenderModes(outputColor, mat, g_tColor, vTexCoordOut.xy))
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
        outputColor = vec4(vVertexColorOut.rgb, vVertexColorOut.a);
    }
#if defined(foliage_vfx_common)
    else if (g_iRenderMode == renderMode_FoliageParams)
    {
        outputColor.rgb = vFoliageParamsOut.rgb;
    }
#endif
#if defined(terrain_blend_common) && (GameVfx_steampal_2way_blend_mask == 0)
    else if (g_iRenderMode == renderMode_TerrainBlend)
    {
        outputColor.rgb = vColorBlendValues.rgb;
    }
#endif
}
