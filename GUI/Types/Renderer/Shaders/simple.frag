#version 460

// Includes
#include "common/utils.glsl"
#include "common/rendermodes.glsl"

// Render modes -- Switched on/off by code
#define renderMode_Diffuse 0
#define renderMode_Specular 0
#define renderMode_PBR 0
#define renderMode_Cubemaps 0
#define renderMode_Irradiance 0
#define renderMode_VertexColor 0
#define renderMode_Terrain_Blend 0
#define renderMode_ExtraParams 0

#define D_BAKED_LIGHTING_FROM_LIGHTMAP 0
#define LightmapGameVersionNumber 0
#define D_BAKED_LIGHTING_FROM_VERTEX_STREAM 0
#define D_BAKED_LIGHTING_FROM_LIGHTPROBE 0

#if defined(vr_simple_2way_blend) || defined (csgo_simple_2way_blend)
    #define simple_2way_blend
#elif defined(vr_simple) || defined(csgo_simple)
    #define simple
#elif defined(vr_complex) || defined(csgo_complex)
    #define complex
#elif defined(vr_glass) || defined(csgo_glass)
    #define glass
#elif defined(vr_static_overlay) || defined(csgo_static_overlay)
    #define static_overlay
#endif

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_FULLBRIGHT 0
#define F_LIT 0
#define F_UNLIT 0
#define F_PAINT_VERTEX_COLORS 0
#define F_ADDITIVE_BLEND 0
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0
#define F_GLASS 0
#define F_DISABLE_TONE_MAPPING 0
#define F_RENDER_BACKFACES 0
#define F_MORPH_SUPPORTED 0
#define F_WRINKLE 0
#define F_DONT_FLIP_BACKFACE_NORMALS 0
#define F_SCALE_NORMAL_MAP 0
// TEXTURING
#define F_LAYERS 0
#define F_TINT_MASK 0
#define F_FANCY_BLENDING 0
#define F_METALNESS_TEXTURE 0
#define F_AMBIENT_OCCLUSION_TEXTURE 0
#define F_FANCY_BLENDING 0
#define F_DETAIL_TEXTURE 0
#define F_SELF_ILLUM 0
#define F_SECONDARY_UV 0
#define F_ENABLE_AMBIENT_OCCLUSION 0 // simple_2way_blend
#define F_ENABLE_TINT_MASKS 0 // simple_2way_blend
// SHADING
#define F_SPECULAR 0
#define F_SPECULAR_INDIRECT 0
#define F_RETRO_REFLECTIVE 0
#define F_ANISOTROPIC_GLOSS 0 // todo shading
#define F_CLOTH_SHADING 0
#define F_USE_BENT_NORMALS 0 // todo
#define F_DIFFUSE_WRAP 0 // todo
#define F_DIFFSUE_WRAP 0 // typo version that existed for part of HLA's development
#define F_SUBSURFACE_SCATTERING 0 // todo, same preintegrated method as vr_skin in HLA
#define F_TRANSMISSIVE_BACKFACE_NDOTL 0 // todo
#define F_NO_SPECULAR_AT_FULL_ROUGHNESS 0
// SKIN
#define F_USE_FACE_OCCLUSION_TEXTURE 0 // todo
#define F_USE_PER_VERTEX_CURVATURE 0 // todo
#define F_SSS_MASK 0 // todo

#define HemiOctIsoRoughness_RG_B 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
centroid in vec3 vCentroidNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec2 vTexCoordOut;
in vec4 vVertexColorOut;

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    in vec3 vLightmapUVScaled;
    uniform sampler2DArray g_tIrradiance;
    uniform sampler2DArray g_tDirectionalIrradiance;
    #if (LightmapGameVersionNumber == 1)
        uniform sampler2DArray g_tDirectLightIndices;
        uniform sampler2DArray g_tDirectLightStrengths;
    #elif (LightmapGameVersionNumber == 2)
        uniform sampler2DArray g_tDirectLightShadows;
    #endif
#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    in vec4 vPerVertexLightingOut;
#else
    uniform sampler2D g_tLPV_Irradiance;
    #if (LightmapGameVersionNumber == 1)
        uniform sampler2D g_tLPV_Indices;
        uniform sampler2D g_tLPV_Scalars;
    #elif (LightmapGameVersionNumber == 2)
        uniform sampler2D g_tLPV_Shadows;
    #endif
#endif
#if F_SECONDARY_UV == 1
    in vec2 vTexCoord2;
    uniform bool g_bUseSecondaryUvForAmbientOcclusion = true;
    #if F_TINT_MASK
        uniform bool g_bUseSecondaryUvForTintMask = true;
    #endif
    #if F_DETAIL_TEXTURE > 0
        uniform bool g_bUseSecondaryUvForDetailMask = true;
    #endif
#endif

#if (LightmapGameVersionNumber == 0)
    #define S_SPECULAR 0 // No cubemaps unless viewing map
#elif defined(csgo_lightmappedgeneric) || defined(csgo_vertexlitgeneric)
    #define S_SPECULAR F_SPECULAR_INDIRECT
#elif defined(vr_complex)
    #define S_SPECULAR F_SPECULAR
#elif defined(generic)
    #define S_SPECULAR 0
#else
    #define S_SPECULAR 1 // Indirect
#endif


#if (defined(simple_2way_blend) || F_LAYERS > 0)
    in vec4 vColorBlendValues;
    uniform sampler2D g_tLayer2Color;
    uniform sampler2D g_tLayer2NormalRoughness;
#endif

#if F_SELF_ILLUM == 1
    uniform sampler2D g_tSelfIllumMask;
    uniform float g_flSelfIllumBrightness;
    uniform vec4 g_vSelfIllumTint = vec4(1.0);
    uniform float g_flSelfIllumScale = 1.0;
#endif

out vec4 outputColor;

uniform sampler2D g_tColor;
uniform sampler2D g_tNormal;
uniform sampler2D g_tTintMask;

#include "common/lighting.glsl"
uniform vec3 vEyePosition;

uniform float g_flAlphaTestReference = 0.5;
uniform float g_flOpacityScale = 1.0;

uniform float g_flAmbientOcclusionDirectDiffuse = 1.0;
uniform float g_flAmbientOcclusionDirectSpecular = 1.0;

// glass specific params
#if (F_GLASS == 1) || defined(glass)
uniform bool g_bFresnel = true;
uniform float g_flEdgeColorFalloff = 3.0;
uniform float g_flEdgeColorMaxOpacity = 0.5;
uniform float g_flEdgeColorThickness = 0.1;
uniform vec4 g_vEdgeColor;
uniform float g_flRefractScale = 0.1;
#endif

#define hasUniformMetalness (defined(simple) || defined(complex)) && (F_METALNESS_TEXTURE == 0)
#define hasColorAlphaMetalness (defined(simple) || defined(complex)) && (F_METALNESS_TEXTURE == 1)
#define hasMetalnessTexture (defined(complex) && (F_METALNESS_TEXTURE == 1) && ((F_RETRO_REFLECTIVE == 1) || (F_ALPHA_TEST == 1) || (F_TRANSLUCENT == 1)))
#define hasAnisoGloss (defined(complex) && (F_ANISOTROPIC_GLOSS == 1))
#define unlit (defined(csgo_unlitgeneric) || (F_FULLBRIGHT == 1) || (F_UNLIT == 1) || (defined(static_overlay) && F_LIT == 0))

#if hasUniformMetalness
    uniform float g_flMetalness = 0.0;
#elif hasMetalnessTexture && !hasColorAlphaMetalness
    uniform sampler2D g_tMetalness;
#endif

#if (F_FANCY_BLENDING > 0)
    uniform sampler2D g_tBlendModulation;
    uniform float g_flBlendSoftness;
#endif

#if (F_RETRO_REFLECTIVE == 1)
    uniform float g_flRetroReflectivity = 1.0;
#endif

#if (F_SCALE_NORMAL_MAP == 1)
    uniform float g_flNormalMapScaleFactor = 1.0;
#endif
uniform float g_flBumpStrength = 1.0;

#if defined(simple_2way_blend)
    uniform sampler2D g_tMask;
    uniform float g_flMetalnessA = 0.0;
    uniform float g_flMetalnessB = 0.0;
#endif

#if defined(csgo_character) || defined(csgo_weapon)
    uniform sampler2D g_tMetalness;
    uniform sampler2D g_tAmbientOcclusion;
#endif

#if defined(csgo_foliage) || (defined(vr_simple) && (F_AMBIENT_OCCLUSION_TEXTURE == 1) && (F_METALNESS_TEXTURE == 1)) || defined(complex)
    uniform sampler2D g_tAmbientOcclusion;
#endif

#if hasAnisoGloss
    uniform sampler2D g_tAnisoGloss;
#endif


const vec3 gamma = vec3(2.2);
const vec3 invGamma = vec3(1.0 / gamma);

#include "common/texturing.glsl"

#include "common/pbr.glsl"

#if (S_SPECULAR == 1 || renderMode_Cubemaps == 1)
#include "common/environment.glsl"
#endif

void main()
{
    // Get vs inputs
    vec2 texCoord = vTexCoordOut;
    vec3 vertexNormal = SwitchCentroidNormal(vNormalOut, vCentroidNormalOut);

    // Get the view direction vector for this fragment
    vec3 V = normalize(vEyePosition - vFragPosition);

    #if F_RENDER_BACKFACES == 1 && (F_DONT_FLIP_BACKFACE_NORMALS == 0)
        vertexNormal = faceforward(vertexNormal, V, vertexNormal);
    #endif

    // Get material properties
    vec4 color = texture(g_tColor, texCoord);
    vec4 normalTexture = texture(g_tNormal, texCoord);

    vec3 normal = unpackHemiOctNormal(normalTexture);
    float roughness = normalTexture.b;

#if (F_LAYERS > 0) || defined(simple_2way_blend)
    vec4 color2 = texture(g_tLayer2Color, texCoord);
    vec4 normalTexture2 = texture(g_tLayer2NormalRoughness, texCoord);

    vec3 normal2 = unpackHemiOctNormal(normalTexture2);
    float roughness2 = normalTexture2.b;

    float blendFactor = vColorBlendValues.r;

    // 0: VertexBlend 1: BlendModulateTexture,rg 2: NewLayerBlending,g 3: NewLayerBlending,a
    #if (F_FANCY_BLENDING > 0)
        vec4 blendModTexel = texture(g_tBlendModulation, texCoord);

        #if (F_FANCY_BLENDING == 1)
            blendFactor = applyBlendModulation(blendFactor, blendModTexel.g, blendModTexel.r);
        #elif (F_FANCY_BLENDING == 2)
            blendFactor = applyBlendModulation(blendFactor, blendModTexel.g, g_flBlendSoftness);
        #elif (F_FANCY_BLENDING == 3)
            blendFactor = applyBlendModulation(blendFactor, blendModTexel.a, g_flBlendSoftness);
        #endif
    #endif

    #if (defined(simple_2way_blend))
        vec4 blendModTexel = texture(g_tMask, texCoord);

        float softnessPaint = vColorBlendValues.g;

        blendFactor = applyBlendModulation(blendFactor, blendModTexel.r, softnessPaint);
    #endif

    #if (F_ENABLE_TINT_MASKS == 1)
        vec2 tintMasks = texture(g_tTintMask, texCoord).xy;

        vec3 tintFactorA = 1.0 - tintMasks.x * (1.0 - vVertexColorOut.rgb);
        vec3 tintFactorB = 1.0 - tintMasks.y * (1.0 - vVertexColorOut.rgb);

        color.rgb *= tintFactorA;
        color2.rgb *= tintFactorB;
    #endif


    // It's more correct to blend normals after hemioct unpacking (like we are doing), but it's not actually how S2 does it
    color = mix(color, color2, blendFactor);
    normal = normalize(mix(normal, normal2, blendFactor));
    roughness = mix(roughness, roughness2, blendFactor);
#endif

#if (F_ALPHA_TEST == 1) || (defined(static_overlay) && F_BLEND_MODE == 2)
    color.a = AlphaTestAntiAliasing(color.a, texCoord);

    if (color.a - 0.001 < g_flAlphaTestReference)   discard;
#endif

#if (F_DETAIL_TEXTURE > 0)
    #if F_SECONDARY_UV == 1
        applyDetailTexture(color.rgb, normal, g_bUseSecondaryUvForDetailMask ? vTexCoord2 : texCoord);
    #else
        applyDetailTexture(color.rgb, normal, texCoord);
    #endif
#endif

#if (F_SCALE_NORMAL_MAP == 1)
    normal = mix(vec3(0, 0, 1), normal, g_flNormalMapScaleFactor);
#else
    normal = mix(vec3(0, 0, 1), normal, g_flBumpStrength);
#endif

#if F_TINT_MASK == 1
    #if F_SECONDARY_UV == 1
        vec2 tintMaskTexcoord = g_bUseSecondaryUvForTintMask ? vTexCoord2 : texCoord;
    #else
        vec2 tintMaskTexcoord = texCoord;
    #endif
    float tintStrength = texture(g_tTintMask, tintMaskTexcoord).x;
    vec3 tintFactor = 1.0 - tintStrength * (1.0 - vVertexColorOut.rgb);
#elif F_ENABLE_TINT_MASKS == 1
    vec3 tintFactor = vec3(1.0); // Skip this as we already did tint mul
#else
    vec3 tintFactor = vVertexColorOut.rgb;
#endif

    vec3 albedo = pow(color.rgb, gamma) * tintFactor;
    float opacity = color.a;
#if defined(static_overlay) && F_PAINT_VERTEX_COLORS == 1
    opacity *= vVertexColorOut.a;
#endif
#if F_TRANSLUCENT == 1
    opacity *= g_flOpacityScale;
#endif
    float metalness = 0.0;
    float occlusion = 1.0;
    vec4 extraParams = vec4(0); // r = retro reflectivity, g = sss mask, b = cloth, a = ???

    vec3 irradiance = vec3(0.3);
    LightingTerms lighting = Init();


    // Define PBR parameters
#if defined(csgo_character)
    extraParams.xz = texture(g_tMetalness, texCoord).rb;
    metalness = texture(g_tMetalness, texCoord).g;
    // a = rimmask
    occlusion = texture(g_tAmbientOcclusion, texCoord).r;
#elif defined(csgo_weapon)
    roughness = texture(g_tMetalness, texCoord).r;
    metalness = texture(g_tMetalness, texCoord).g;
    occlusion = texture(g_tAmbientOcclusion, texCoord).r;
#elif defined(csgo_foliage)
    occlusion = texture(g_tAmbientOcclusion, texCoord).r;
#elif (hasUniformMetalness)
    metalness = g_flMetalness;
#elif (hasColorAlphaMetalness)
    metalness = color.a;
#elif (hasMetalnessTexture)
    metalness = texture(g_tMetalness, texCoord).g;
    #if (F_RETRO_REFLECTIVE == 1)
        extraParams.x = texture(g_tMetalness, texCoord).r;
    #endif
#elif (defined(simple_2way_blend))
    metalness = mix(g_flMetalnessA, g_flMetalnessB, blendFactor);
    #if F_ENABLE_AMBIENT_OCCLUSION == 1
        occlusion = color.a;
    #endif
#endif

#if defined(vr_complex) && (F_METALNESS_TEXTURE == 0) && (F_RETRO_REFLECTIVE == 1)
    extraParams.x = g_flRetroReflectivity;
#endif
#if defined(vr_complex) && (F_CLOTH_SHADING == 1)
    extraParams.z = 1.0;
#endif

#if hasAnisoGloss
    vec2 anisoGloss = texture(g_tAnisoGloss, texCoord).rg;
    // convert to iso roughness. temp solution
    roughness = (anisoGloss.r + anisoGloss.g) / 2.0;
#endif

#if defined(vr_simple) && (F_AMBIENT_OCCLUSION_TEXTURE == 1)
    #if (F_METALNESS_TEXTURE == 0)
        occlusion = color.a;
    #else
        occlusion = texture(g_tAmbientOcclusion, texCoord).r;
    #endif
#endif

#if defined(complex)
    #if (F_SECONDARY_UV == 1)
        occlusion = texture(g_tAmbientOcclusion, g_bUseSecondaryUvForAmbientOcclusion ? vTexCoord2 : texCoord).r;
    #else
        occlusion = texture(g_tAmbientOcclusion, texCoord).r;
    #endif
#endif

    roughness = AdjustRoughnessByGeometricNormal(roughness, vertexNormal);

    roughness = clamp(roughness, 0.005, 1.0); // <- inaccurate?

    // Get the world normal for this fragment
    vec3 N = calculateWorldNormal(normal, vertexNormal, vTangentOut, vBitangentOut);


#if unlit == 1
    outputColor = vec4(albedo, color.a);
#else
    #if (F_GLASS == 1) || defined(glass)
        float viewDotNormalInv = saturate(1.0 - (dot(V, N) - g_flEdgeColorThickness));
        float fresnel = saturate(pow(viewDotNormalInv, g_flEdgeColorFalloff)) * g_flEdgeColorMaxOpacity;
        vec4 fresnelColor = vec4(g_vEdgeColor.xyz, g_bFresnel ? fresnel : 0.0);

        vec4 glassResult = mix(vec4(albedo, opacity), fresnelColor, g_flOpacityScale);
        albedo = glassResult.rgb;
        opacity = glassResult.a;
    #endif

    outputColor = vec4(albedo, opacity);


    vec3 L = normalize(-getSunDir());
    vec3 H = normalize(V + L);

    vec3 F0 = vec3(0.04);

    vec3 diffuseColor = albedo - albedo * metalness;
	vec3 specularColor = mix(F0, albedo, metalness);


    // Direct Lighting
    float visibility = 1.0;

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    #if (LightmapGameVersionNumber == 1)
        vec4 vLightStrengths = texture(g_tDirectLightStrengths, vLightmapUVScaled);
        vec4 strengthSquared = vLightStrengths * vLightStrengths;
        vec4 vLightIndices = texture(g_tDirectLightIndices, vLightmapUVScaled) * 255;
        // TODO: figure this out, it's barely working
        float index = 0.0;
        if (vLightIndices.r == index) visibility = strengthSquared.r;
        else if (vLightIndices.g == index) visibility = strengthSquared.g;
        else if (vLightIndices.b == index) visibility = strengthSquared.b;
        else if (vLightIndices.a == index) visibility = strengthSquared.a;
        else visibility = 0.0;

    #elif (LightmapGameVersionNumber == 2)
        visibility = 1 - texture(g_tDirectLightShadows, vLightmapUVScaled).r;
    #endif
#endif

    if (visibility > 0.0)
    {
        vec3 specularLight = specularLighting(L, V, N, F0, specularColor, roughness, extraParams);
        float diffuseLight = diffuseLobe(max(dot(N, L), 0.0), roughness);

       lighting.SpecularDirect += specularLight * visibility * getSunColor();
       lighting.DiffuseDirect += diffuseLight * visibility * getSunColor();
    }


    // Indirect Lighting
    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1) && (LightmapGameVersionNumber > 0)
        irradiance = texture(g_tIrradiance, vLightmapUVScaled).rgb;
        vec4 vAHDData = texture(g_tDirectionalIrradiance, vLightmapUVScaled);

        irradiance = ComputeLightmapShading(irradiance, vAHDData, normal);

        occlusion = min(occlusion, vAHDData.a);
    #elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
        irradiance = vPerVertexLightingOut.rgb;
    #endif


    lighting.DiffuseIndirect = irradiance;

    // Environment Map
    #if (S_SPECULAR == 1)
        vec3 specular = GetEnvironment(N, V, roughness, specularColor, irradiance, extraParams);
        lighting.SpecularIndirect += specular * occlusion;
    #endif

    lighting.DiffuseDirect *= mix(1.0, occlusion, g_flAmbientOcclusionDirectDiffuse);
    lighting.SpecularDirect *= mix(1.0, occlusion, g_flAmbientOcclusionDirectSpecular);

    vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect * occlusion;
    vec3 specularLighting = lighting.SpecularDirect + lighting.SpecularIndirect * occlusion;

    #if F_NO_SPECULAR_AT_FULL_ROUGHNESS == 1
        specularLighting = (roughness == 1.0) ? vec3(0) : specularLighting;
    #endif

    vec3 combinedLighting = diffuseColor * diffuseLighting + specularLighting;

    outputColor.rgb = combinedLighting;

    #if F_SELF_ILLUM == 1
        vec3 SelfIllumScaled = pow2(g_flSelfIllumBrightness) * g_flSelfIllumScale * SRGBtoLinear(g_vSelfIllumTint.rgb);
        outputColor.rgb *= max(vec3(1.0), texture(g_tSelfIllumMask, texCoord).r * SelfIllumScaled);
    #endif

#endif
#if F_DISABLE_TONE_MAPPING == 0
    outputColor.rgb = pow(outputColor.rgb, vec3(invGamma));
    //outputColor.rgb = SRGBtoLinear(outputColor.rgb);
#endif

    // Rendermodes

#if renderMode_FullBright == 1
    vec3 fullbrightLighting = CalculateFullbrightLighting(albedo, N, V);
    outputColor = vec4(pow(fullbrightLighting, vec3(invGamma)), opacity);
#endif

#if renderMode_Color == 1
    outputColor = vec4(color.rgb, 1.0);
#endif

#if renderMode_BumpMap == 1
    outputColor = vec4(PackToColor(normal), 1.0);
#endif

#if renderMode_Tangents == 1
    outputColor = vec4(PackToColor(vTangentOut), 1.0);
#endif

#if renderMode_Normals == 1
    outputColor = vec4(PackToColor(vertexNormal), 1.0);
#endif

#if renderMode_BumpNormals == 1
    outputColor = vec4(PackToColor(N), 1.0);
#endif

#if (renderMode_Diffuse == 1) && (unlit != 1)
    outputColor.rgb = pow(diffuseLighting * 0.5, vec3(invGamma));
#endif

#if (renderMode_Specular == 1) && (unlit != 1)
    outputColor.rgb = pow(specularLighting, vec3(invGamma));
#endif

#if renderMode_PBR == 1
    outputColor = vec4(occlusion, roughness, metalness, 1.0);
#endif

#if (renderMode_Cubemaps == 1)
    // No bumpmaps, full reflectivity
    vec3 EnvMap = GetEnvironment(vertexNormal, V, 0.0, vec3(1.0), vec3(0.0), vec4(0)).rgb;
    outputColor.rgb = pow(EnvMap, vec3(invGamma));
#endif

#if renderMode_Illumination == 1
    outputColor = vec4(pow(0.5 * lighting.DiffuseDirect + lighting.SpecularDirect, vec3(invGamma)), 1.0);
#endif

#if renderMode_Irradiance == 1 && (F_GLASS == 0)
    outputColor = vec4(pow(irradiance, vec3(invGamma)), 1.0);
#endif

#if renderMode_VertexColor == 1
    outputColor = vVertexColorOut;
#endif

#if renderMode_Terrain_Blend == 1 && (F_LAYERS > 0 || defined(simple_2way_blend))
    outputColor.rgb = vColorBlendValues.rgb;
#endif

#if renderMode_ExtraParams == 1
    outputColor.rgb = extraParams.rgb;
#endif
}
