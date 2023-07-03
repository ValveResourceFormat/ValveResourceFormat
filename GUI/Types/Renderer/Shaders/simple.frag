#version 400

// Includes
#include "common/utils.glsl"
#include "common/rendermodes.glsl"
#include "common/texturing.glsl"

// Render modes -- Switched on/off by code
#define renderMode_PBR 0
#define renderMode_Cubemaps 0
#define renderMode_Irradiance 0
#define renderMode_VertexColor 0
#define renderMode_Terrain_Blend 0

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
#endif

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_FULLBRIGHT 0
#define F_UNLIT 0
#define F_TINT_MASK 0
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0
#define F_GLASS 0
#define F_LAYERS 0
#define F_FANCY_BLENDING 0
#define F_SPECULAR 0
#define F_SPECULAR_INDIRECT 0
#define F_METALNESS_TEXTURE 0
#define F_RETRO_REFLECTIVE 0
#define F_AMBIENT_OCCLUSION_TEXTURE 0
#define F_ANISOTROPIC_GLOSS 0
#define HemiOctIsoRoughness_RG_B 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
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

#if (LightmapGameVersionNumber == 0)
    #define S_SPECULAR 0 // No cubemaps unless viewing map
#elif defined(csgo_lightmappedgeneric) || defined(csgo_vertexlitgeneric)
    #define S_SPECULAR F_SPECULAR_INDIRECT
#elif defined(complex)
    #define S_SPECULAR F_SPECULAR
#elif defined(generic)
    #define S_SPECULAR 0
#else
    #define S_SPECULAR 1 // Indirect
#endif

#if (S_SPECULAR == 1 || renderMode_Cubemaps == 1)
#include "common/environment.glsl"
#endif

#if (defined(simple_2way_blend) || F_LAYERS > 0)
    in vec4 vColorBlendValues;
    uniform sampler2D g_tLayer2Color;
    uniform sampler2D g_tLayer2NormalRoughness;
#endif

out vec4 outputColor;

uniform sampler2D g_tColor;
uniform sampler2D g_tNormal;
uniform sampler2D g_tTintMask;

#include "common/lighting.glsl"
uniform vec3 vEyePosition;

uniform float g_flAlphaTestReference = 0.5;

// glass specific params
#if (F_GLASS == 1) || defined(glass)
uniform bool g_bFresnel = true;
uniform float g_flEdgeColorFalloff = 3.0;
uniform float g_flEdgeColorMaxOpacity = 0.5;
uniform float g_flEdgeColorThickness = 0.1;
uniform vec4 g_vEdgeColor;
uniform float g_flRefractScale = 0.1;
uniform float g_flOpacityScale = 1.0;
#endif

#define hasUniformMetalness (defined(simple) || defined(complex)) && (F_METALNESS_TEXTURE == 0)
#define hasColorAlphaMetalness (defined(simple) || defined(complex)) && (F_METALNESS_TEXTURE == 1)
#define hasMetalnessTexture defined(complex) && (F_METALNESS_TEXTURE == 1) && ((F_RETRO_REFLECTIVE == 1) || (F_ALPHA_TEST == 1) || (F_TRANSLUCENT == 1))
#define hasAnisoGloss defined(complex) && (F_ANISOTROPIC_GLOSS == 1)

#if hasUniformMetalness
    uniform float g_flMetalness = 0.0;
#endif

#if hasMetalnessTexture
    uniform sampler2D g_tMetalness;
#endif

#if (F_FANCY_BLENDING > 0)
    uniform sampler2D g_tBlendModulation;
    uniform float g_flBlendSoftness;
#endif

#if defined(simple_2way_blend)
    uniform sampler2D g_tMask;
    uniform float g_flMetalnessA;
    uniform float g_flMetalnessB;
#endif

#if defined(csgo_character) || defined(csgo_weapon)
    uniform sampler2D g_tMetalness;
    uniform sampler2D g_tAmbientOcclusion;
#endif

#if defined(csgo_foliage) || (defined(vr_simple) && F_AMBIENT_OCCLUSION_TEXTURE == 1 && F_METALNESS_TEXTURE == 1) || defined(vr_complex) // csgo_complex too?
    uniform sampler2D g_tAmbientOcclusion;
#endif

#if hasAnisoGloss
    uniform sampler2D g_tAnisoGloss;
#endif

vec3 oct_to_float32x3(vec2 e)
{
    vec3 v = vec3(e.xy, 1.0 - abs(e.x) - abs(e.y));
    return normalize(v);
}

//Calculate the normal of this fragment in world space
vec3 calculateWorldNormal(vec4 bumpNormal)
{
    //Reconstruct the tangent vector from the map
#if HemiOctIsoRoughness_RG_B == 1
    vec2 temp = vec2(bumpNormal.x + bumpNormal.y -1.003922, bumpNormal.x - bumpNormal.y);
    vec3 tangentNormal = oct_to_float32x3(temp);
#else
    //vec2 temp = vec2(bumpNormal.w, bumpNormal.y) * 2 - 1;
    //vec3 tangentNormal = vec3(temp, sqrt(1 - temp.x * temp.x - temp.y * temp.y));
    vec2 temp = vec2(bumpNormal.w + bumpNormal.y -1.003922, bumpNormal.w - bumpNormal.y);
    vec3 tangentNormal = oct_to_float32x3(temp);
#endif

    tangentNormal.y *= -1.0;

    vec3 normal = vNormalOut;
    vec3 tangent = vTangentOut.xyz;
    vec3 bitangent = vBitangentOut;

    //Make the tangent space matrix
    mat3 tangentSpace = mat3(tangent, bitangent, normal);

    //Calculate the tangent normal in world space and return it
    return normalize(tangentSpace * tangentNormal);
}

#include "common/pbr.glsl"

void main()
{
    vec2 texCoord = vTexCoordOut;

    vec4 color = texture(g_tColor, texCoord);
    vec4 normal = texture(g_tNormal, texCoord);

#if (F_LAYERS > 0) || defined(simple_2way_blend)
    vec4 color2 = texture(g_tLayer2Color, texCoord);
    vec4 normal2 = texture(g_tLayer2NormalRoughness, texCoord);
    float blendFactor = vColorBlendValues.r;

    // 0: VertexBlend 1: BlendModulateTexture,rg 2: NewLayerBlending,g 3: NewLayerBlending,a
    #if (F_FANCY_BLENDING > 0)
        vec4 blendModTexel = texture(g_tBlendModulation, texCoord);

        #if (F_FANCY_BLENDING == 1 || F_FANCY_BLENDING == 2)
            float blendModFactor = blendModTexel.g;
        #else
            float blendModFactor = blendModTexel.a;
        #endif

        #if (F_FANCY_BLENDING == 1)
            float minb = max(0, blendModFactor - blendModTexel.r);
            float maxb = min(1, blendModFactor + blendModTexel.r);
        #elif (F_FANCY_BLENDING == 2 || F_FANCY_BLENDING == 3)
            float minb = max(0, blendModFactor - g_flBlendSoftness);
            float maxb = min(1, blendModFactor + g_flBlendSoftness);
        #endif

        blendFactor = smoothstep(minb, maxb, blendFactor);
    #endif

    #if (defined(simple_2way_blend))
        vec4 blendModTexel = texture(g_tMask, texCoord);
        blendFactor *= blendModTexel.r;
    #endif

    color = mix(color, color2, blendFactor);
    normal = mix(normal, normal2, blendFactor);
#endif

#if F_ALPHA_TEST == 1
    if (color.a < g_flAlphaTestReference)
    {
       discard;
    }
#endif

#if F_TINT_MASK == 1
    float tintStrength = texture(g_tTintMask, texCoord).x;
    vec3 tintFactor = tintStrength * vVertexColorOut.rgb + (1 - tintStrength);
#else
    vec3 tintFactor = vVertexColorOut.rgb;
#endif

    vec3 gamma = vec3(2.2);
    vec3 invGamma = vec3(1.0 / gamma);

    vec3 albedo = pow(color.rgb, gamma) * tintFactor;
    float opacity = color.a * vVertexColorOut.a;
    float metalness = 0.0;
    float roughness = normal.b;
    float occlusion = 1.0;

    vec3 irradiance = vec3(0.3);
    vec3 Lo = vec3(0.0);


    // Define PBR parameters
#if defined(csgo_character)
    metalness = texture(g_tMetalness, texCoord).g;
    // b = cloth, a = rimmask
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
#elif (defined(simple_2way_blend))
    metalness = mix(g_flMetalnessA, g_flMetalnessB, blendFactor);
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

#if defined(vr_complex)
    occlusion = texture(g_tAmbientOcclusion, texCoord).r;
#endif

    roughness = AdjustRoughnessByGeometricNormal(roughness, vNormalOut);

    roughness = clamp(roughness, 0.005, 1.0); // <- inaccurate?

    // Get the world normal for this fragment
    vec3 N = calculateWorldNormal(normal);

    // Get the view direction vector for this fragment
    vec3 V = normalize(vEyePosition - vFragPosition);

#if defined(csgo_unlitgeneric) || (F_FULLBRIGHT == 1) || (F_UNLIT == 1)
    outputColor = vec4(albedo, color.a);
#else
    #if (F_GLASS == 1) || defined(glass)
        float viewDotNormalInv = clamp(1.0 - (dot(V, N) - g_flEdgeColorThickness), 0.0, 1.0);
        float fresnel = clamp(pow(viewDotNormalInv, g_flEdgeColorFalloff), 0.0, 1.0) * g_flEdgeColorMaxOpacity * (g_bFresnel ? 1.0 : 0.0);
        vec4 fresnelColor = vec4(g_vEdgeColor.xyz, fresnel);

        vec4 glassResult = mix(vec4(albedo, opacity), fresnelColor, g_flOpacityScale);
        albedo = glassResult.rgb;
        opacity = glassResult.a;
    #endif

    outputColor = vec4(albedo, opacity);


    vec3 L = normalize(-getSunDir());
    vec3 H = normalize(V + L);

    vec3 F0 = vec3(0.04); 
	F0 = mix(F0, albedo, metalness);

    vec3 diffuseColor = albedo * (1.0 - metalness);
    vec3 specularColor = F0;

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
        Lo += specularContribution(L, V, N, F0, albedo, metalness, roughness) * visibility;
        Lo += diffuseLobe(max(dot(N, L), 0.0) * getSunColor()) * visibility;
    }

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1) && (LightmapGameVersionNumber > 0)
        irradiance = texture(g_tIrradiance, vLightmapUVScaled).rgb;
        vec4 vAHDData = texture(g_tDirectionalIrradiance, vLightmapUVScaled);
        const float DirectionalLightmapMinZ = 0.05;
        irradiance *= mix(1.0, vAHDData.z, DirectionalLightmapMinZ);
        occlusion *= vAHDData.w;
    #elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
        irradiance = vPerVertexLightingOut.rgb;
    #endif

    Lo *= occlusion;

    outputColor.rgb *= (irradiance);
    outputColor.rgb += Lo;

    // Environment Map
    #if (S_SPECULAR == 1)
        vec3 specular = GetEnvironment(N, V, roughness, F0, irradiance);
        outputColor.rgb += specular * occlusion;
    #endif


    outputColor.rgb = pow(outputColor.rgb, vec3(invGamma));
    //outputColor.rgb = SRGBtoLinear(outputColor.rgb);
#endif

#if renderMode_FullBright == 1
    vec3 illumination = vec3(max(0.0, dot(V, N)));
    illumination = illumination * 0.7 + 0.3;
    outputColor = vec4(illumination * pow(albedo, invGamma), opacity);
#endif

#if renderMode_Color == 1
    outputColor = vec4(color.rgb, 1.0);
#endif

#if renderMode_BumpMap == 1
    outputColor = normal;
#endif

#if renderMode_Tangents == 1
    outputColor = vec4(PackToColor(vTangentOut.xyz), 1.0);
#endif

#if renderMode_Normals == 1
    outputColor = vec4(PackToColor(vNormalOut), 1.0);
#endif

#if renderMode_BumpNormals == 1
    outputColor = vec4(N * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if renderMode_PBR == 1
    outputColor = vec4(occlusion, roughness, metalness, 1.0);
#endif

#if (renderMode_Cubemaps == 1)
    // No bumpmaps, full reflectivity
    float lod = 0.0;
    vec3 EnvMap = GetEnvironment(vNormalOut, V, roughness, vec3(1.0), vec3(0.0)).rgb;
    outputColor.rgb = pow(EnvMap, vec3(invGamma));
#endif

#if renderMode_Illumination == 1
    outputColor = vec4(pow(Lo, vec3(invGamma)), 1.0);
#endif

#if renderMode_Irradiance == 1 && F_GLASS == 0
    outputColor = vec4(pow(irradiance, vec3(invGamma)), 1.0);
#endif

#if renderMode_VertexColor == 1
    outputColor = vVertexColorOut;
#endif

#if renderMode_Terrain_Blend == 1 && (F_LAYERS > 0 || defined(simple_2way_blend))
    outputColor.rgb = vColorBlendValues.rgb;
#endif
}
