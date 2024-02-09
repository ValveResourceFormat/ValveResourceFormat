#version 460
//? #include "features.glsl"
//? #include "utils.glsl"
//? #include "LightingConstants.glsl"
//? #include "lighting_common.glsl"
//? #include "texturing.glsl"
//? #include "pbr.glsl"

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
    in vec3 vPerVertexLightingOut;
#elif (D_BAKED_LIGHTING_FROM_LIGHTPROBE == 1)
    uniform sampler2D g_tLPV_Irradiance;
    #if (LightmapGameVersionNumber == 1)
        uniform sampler2D g_tLPV_Indices;
        uniform sampler2D g_tLPV_Scalars;
    #elif (LightmapGameVersionNumber == 2)
        uniform sampler2D g_tLPV_Shadows;
    #endif
#endif

vec3 getSunDir()
{
    return -normalize(mat3(vLightPosition) * vec3(-1, 0, 0));
}

vec3 getSunColor()
{
    return SrgbGammaToLinear(vLightColor.rgb) * vLightColor.a;
}


// This should contain our direct lighting loop
void CalculateDirectLighting(inout LightingTerms_t lighting, inout MaterialProperties_t mat)
{
    vec3 lightVector = normalize(-getSunDir());

    // Lighting
    float visibility = 1.0;

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    #if (LightmapGameVersionNumber == 1)
        vec4 vLightStrengths = texture(g_tDirectLightStrengths, vLightmapUVScaled);
        vec4 strengthSquared = pow2(vLightStrengths);
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
        CalculateShading(lighting, lightVector, visibility * getSunColor(), mat);
    }
}


#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)

#define UseLightmapDirectionality 1

// fixed values until we can reset state in material system
uniform float g_flDirectionalLightmapStrength = 1.0;
uniform float g_flDirectionalLightmapMinZ = 0.05;
const vec4 g_vLightmapParams = vec4(0.0); // ???? directional non-intensity?? it's set to 0.0 in all places ive looked

const float colorSpaceMul = 254 / 255;

// I don't actually understand much of this, but it's Valve's code.
vec3 ComputeLightmapShading(vec3 irradianceColor, vec4 irradianceDirection, vec3 normalMap)
{

#if UseLightmapDirectionality == 1
    vec3 vTangentSpaceLightVector;

    vTangentSpaceLightVector.xy = UnpackFromColor(irradianceDirection.xy);

    float sinTheta = dot(vTangentSpaceLightVector.xy, vTangentSpaceLightVector.xy);

#if LightmapGameVersionNumber == 1
    // Error in HLA code, fixed in DeskJob
    float cosTheta = 1.0 - sqrt(sinTheta);
#else
    vTangentSpaceLightVector *= (colorSpaceMul / max(colorSpaceMul, length(vTangentSpaceLightVector.xy)));

    float cosTheta = sqrt(1.0 - sinTheta);
#endif
    vTangentSpaceLightVector.z = cosTheta;

    float flDirectionality = mix(irradianceDirection.z, 1.0, g_flDirectionalLightmapStrength);
    vec3 vNonDirectionalLightmap = irradianceColor * saturate(flDirectionality + g_vLightmapParams.x);

    float NoL = ClampToPositive(dot(vTangentSpaceLightVector, normalMap));

    float LightmapZ = max(vTangentSpaceLightVector.z, g_flDirectionalLightmapMinZ);

    irradianceColor = (NoL * (irradianceColor - vNonDirectionalLightmap) / LightmapZ) + vNonDirectionalLightmap;
#endif

    return irradianceColor;
}

#endif


void CalculateIndirectLighting(inout LightingTerms_t lighting, inout MaterialProperties_t mat)
{
    lighting.DiffuseIndirect = vec3(0.3);

    // Indirect Lighting
#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    vec3 irradiance = texture(g_tIrradiance, vLightmapUVScaled).rgb;
    vec4 vAHDData = texture(g_tDirectionalIrradiance, vLightmapUVScaled);

    lighting.DiffuseIndirect = ComputeLightmapShading(irradiance, vAHDData, mat.NormalMap);

    lighting.SpecularOcclusion = vAHDData.a;

#elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
    lighting.DiffuseIndirect = vPerVertexLightingOut.rgb;
#elif (D_BAKED_LIGHTING_FROM_LIGHTPROBE == 1)
    // todo: probe lighting
#else
    #define NoBakeLighting 1
#endif

    // Environment Maps
#if defined(S_SPECULAR) && (S_SPECULAR == 1)
    vec3 ambientDiffuse;
    float normalizationTerm = GetEnvMapNormalization(GetIsoRoughness(mat.Roughness), mat.AmbientNormal, lighting.DiffuseIndirect);

    lighting.SpecularIndirect = GetEnvironment(mat) * normalizationTerm;
#endif
}


uniform float g_flAmbientOcclusionDirectDiffuse = 1.0;
uniform float g_flAmbientOcclusionDirectSpecular = 1.0;

// AO Proxies would be merged here
void ApplyAmbientOcclusion(inout LightingTerms_t o, MaterialProperties_t mat)
{
#if defined(DIFFUSE_AO_COLOR_BLEED)
    SetDiffuseColorBleed(mat);
#endif

    // In non-lightmap shaders, SpecularAO always does a min(1.0, specularAO) in the same place where lightmap
    // shaders does min(bakedAO, specularAO). That means that bakedAO exists and is a constant 1.0 in those shaders!
    mat.SpecularAO = min(o.SpecularOcclusion, mat.SpecularAO);

    vec3 DirectAODiffuse = mix(vec3(1.0), mat.DiffuseAO, g_flAmbientOcclusionDirectDiffuse);
    float DirectAOSpecular = mix(1.0, mat.SpecularAO, g_flAmbientOcclusionDirectSpecular);

    o.DiffuseDirect *= DirectAODiffuse;
    o.DiffuseIndirect *= mat.DiffuseAO;
    o.SpecularDirect *= DirectAOSpecular;
    o.SpecularIndirect *= mat.SpecularAO;
}
