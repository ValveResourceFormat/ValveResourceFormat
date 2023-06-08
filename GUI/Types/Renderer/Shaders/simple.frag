#version 330

// Render modes -- Switched on/off by code
#include "common/rendermodes.glsl"
#define renderMode_Irradiance 0
#define renderMode_VertexColor 0
#define renderMode_Terrain_Blend 0

#define F_VERTEX_COLOR 0

#define D_BAKED_LIGHTING_FROM_LIGHTMAP 0
#define LightmapGameVersionNumber 0
#define D_BAKED_LIGHTING_FROM_VERTEX_STREAM 0
#define D_BAKED_LIGHTING_FROM_LIGHTPROBE 0

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_FULLBRIGHT 0
#define F_TINT_MASK 0
#define F_ALPHA_TEST 0
#define F_GLASS 0
#define F_LAYERS 0
#define F_FANCY_BLENDING 0
#define simple_2way_blend 0
#define HemiOctIsoRoughness_RG_B 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec2 vTexCoordOut;
#if F_VERTEX_COLOR == 1
    in vec4 vColorOut;
#endif

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
#endif

#if (simple_2way_blend == 1 || F_LAYERS > 0)
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

uniform vec4 m_vTintColorSceneObject;
uniform vec3 m_vTintColorDrawCall;

uniform vec4 g_vTexCoordOffset;
uniform vec4 g_vTexCoordScale;
uniform vec4 g_vColorTint;

uniform float g_flAlphaTestReference = 0.5;

// glass specific params
#if F_GLASS == 1
uniform bool g_bFresnel = true;
uniform float g_flEdgeColorFalloff = 3.0;
uniform float g_flEdgeColorMaxOpacity = 0.5;
uniform float g_flEdgeColorThickness = 0.1;
uniform vec4 g_vEdgeColor;
uniform float g_flRefractScale = 0.1;
uniform float g_flOpacityScale = 1.0;
#endif

#if (F_FANCY_BLENDING > 0)
    uniform sampler2D g_tBlendModulation;
    uniform float g_flBlendSoftness;
#endif

#if (simple_2way_blend == 1)
    uniform sampler2D g_tMask;
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

vec3 getSunColor(float brightness)
{
    vec3 color = vec3(255, 222, 189);
    return vec3(
        (color.r / 255.0),
        (color.g / 255.0),
        (color.b / 255.0)
    );
}

void main()
{
    vec2 texCoord = vTexCoordOut * g_vTexCoordScale.xy + g_vTexCoordOffset.xy;
    vec4 color = texture(g_tColor, texCoord);
    vec4 normal = texture(g_tNormal, texCoord);

#if (F_LAYERS > 0)
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

    #if (simple_2way_blend == 1)
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

    // TODO: calculate tint in the vertex stage
    vec3 tintColor = m_vTintColorSceneObject.xyz * m_vTintColorDrawCall;

#if F_TINT_MASK == 1
    float tintStrength = texture(g_tTintMask, vTexCoordOut * g_vTexCoordScale.xy + g_vTexCoordScale.xy).x;
    vec3 tintFactor = tintStrength * tintColor + (1 - tintStrength) * vec3(1);
#else
    vec3 tintFactor = tintColor;
#endif

    // Get the world normal for this fragment
    vec3 N = calculateWorldNormal(normal);

    // Get the direction from the fragment to the light
    vec3 V = normalize(vEyePosition - vFragPosition);

#if F_GLASS == 1
    // TODO: make this receive lightmaps
    vec4 glassColor = vec4(color.rgb * g_vColorTint.rgb, color.a);

    float viewDotNormalInv = clamp(1.0 - (dot(V, N) - g_flEdgeColorThickness), 0.0, 1.0);
    float fresnel = clamp(pow(viewDotNormalInv, g_flEdgeColorFalloff), 0.0, 1.0) * g_flEdgeColorMaxOpacity * (g_bFresnel ? 1.0 : 0.0);
    vec4 fresnelColor = vec4(g_vEdgeColor.xyz, fresnel);

    outputColor = mix(glassColor, fresnelColor, g_flOpacityScale);
#else
    outputColor = vec4(color.rgb * g_vColorTint.rgb * tintFactor, color.a);

    vec3 L = normalize(-getSunDir());
    vec3 H = normalize(V + L);

    vec3 F0 = vec3(0.04); 
	F0 = mix(F0, color.rgb, 0.0);
    vec3 Lo = vec3(0.0);

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
        Lo += specularContribution(L, V, N, F0, outputColor.rgb, 0.0, normal.b) * visibility;
        Lo += diffuseLobe(max(dot(N, L), 0.0) * getSunColor(1.5)) * visibility;
    }

    vec3 irradiance = vec3(0.5);

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1) && (LightmapGameVersionNumber > 0)
        irradiance = texture(g_tIrradiance, vLightmapUVScaled).rgb;
        vec4 vAHDData = texture(g_tDirectionalIrradiance, vLightmapUVScaled);
        const float DirectionalLightmapMinZ = 0.05;
        irradiance *= mix(1.0, vAHDData.z, DirectionalLightmapMinZ);
    #elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
        irradiance = vPerVertexLightingOut.rgb;
    #endif

    float gamma = 2.2;
    irradiance = pow(irradiance, vec3(1.0/gamma));

    outputColor.rgb *= irradiance;
    outputColor.rgb += Lo;
#endif

#if renderMode_FullBright == 1
    vec3 illumination = vec3(max(0.0, dot(V, N)));
    illumination = illumination * 0.7 + 0.3;
    outputColor = vec4(illumination * color.rgb, color.a);
#endif

#if renderMode_Color == 1
    outputColor = vec4(color.rgb, 1.0);
#endif

#if renderMode_BumpMap == 1
    outputColor = normal;
#endif

#if renderMode_Tangents == 1
    outputColor = vec4(vTangentOut.xyz * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if renderMode_Normals == 1
    outputColor = vec4(vNormalOut * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if renderMode_BumpNormals == 1
    outputColor = vec4(N * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if renderMode_Illumination == 1
    outputColor = vec4(Lo, 1.0);
#endif

#if renderMode_Irradiance == 1 && F_GLASS == 0
    outputColor = vec4(irradiance, 1.0);
#endif

#if renderMode_VertexColor == 1 && F_VERTEX_COLOR == 1
    outputColor = vColorOut == vec4(vec3(0), 1) ? outputColor : vColorOut;
#endif

#if renderMode_Terrain_Blend == 1 && F_LAYERS > 0
    outputColor.rgb = vColorBlendValues.rgb;
#endif
}
