#version 330

// Render modes -- Switched on/off by code
#include "common/rendermodes.glsl"
#define renderMode_VertexColor 0
#define renderMode_Terrain_Blend 0

#define F_VERTEX_COLOR 0

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
#if VERTEX_COLOR == 1
    in vec4 vColorOut;
#endif
#if F_LAYERS > 0
    in vec4 vColorBlendValues;
    uniform sampler2D g_tLayer2Color;
    uniform sampler2D g_tLayer2NormalRoughness;
#endif

out vec4 outputColor;

uniform sampler2D g_tColor;
uniform sampler2D g_tNormal;
uniform sampler2D g_tTintMask;

uniform vec3 vLightPosition;
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

#if F_FANCY_BLENDING == 1
    uniform sampler2D g_tBlendModulation;
    uniform float g_flBlendSoftness;
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

//Main entry point
void main()
{
    //Get the ambient color from the color texture
    vec2 texCoord = vTexCoordOut * g_vTexCoordScale.xy + g_vTexCoordOffset.xy;
    vec4 color = texture(g_tColor, texCoord);
    vec4 normal = texture(g_tNormal, texCoord);

#if (F_LAYERS > 0)
    vec4 color2 = texture(g_tLayer2Color, texCoord);
    vec4 normal2 = texture(g_tLayer2NormalRoughness, texCoord);
    float blendFactor = vColorBlendValues.r;

    // 0: VertexBlend 1: BlendModulateTexture,rg 2: NewLayerBlending,g 3: NewLayerBlending,a
    #if (F_FANCY_BLENDING == 1)
        vec4 blendModTexel = texture(g_tBlendModulation, texCoord);
        float blendModFactor = blendModTexel.g;
        #if simple_2way_blend == 1
            blendModFactor = blendModTexel.r;
        #endif

        float minb = max(0, blendModFactor - g_flBlendSoftness);
        float maxb = min(1, blendModFactor + g_flBlendSoftness);
        blendFactor = smoothstep(minb, maxb, blendFactor);
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

    //Get the direction from the fragment to the light - light position == camera position for now
    vec3 lightDirection = normalize(vLightPosition - vFragPosition);
    vec3 viewDirection = normalize(vEyePosition - vFragPosition);

    //Get the world normal for this fragment
    vec3 worldNormal = calculateWorldNormal(normal);

#if renderMode_FullBright == 1 || F_FULLBRIGHT == 1
    float illumination = 1.0;
#else
    //Calculate lambert lighting
    float illumination = max(0.0, dot(worldNormal, lightDirection));
    illumination = illumination * 0.7 + 0.3;//add ambient
#endif

    //Calculate tint color
    vec3 tintColor = m_vTintColorSceneObject.xyz * m_vTintColorDrawCall;

#if F_TINT_MASK == 1
    float tintStrength = texture(g_tTintMask, vTexCoordOut * g_vTexCoordScale.xy + g_vTexCoordScale.xy).x;
    vec3 tintFactor = tintStrength * tintColor + (1 - tintStrength) * vec3(1);
#else
    vec3 tintFactor = tintColor;
#endif

#if F_GLASS == 1
    vec4 glassColor = vec4(illumination * color.rgb * g_vColorTint.rgb, color.a);

    float viewDotNormalInv = clamp(1.0 - (dot(viewDirection, worldNormal) - g_flEdgeColorThickness), 0.0, 1.0);
    float fresnel = clamp(pow(viewDotNormalInv, g_flEdgeColorFalloff), 0.0, 1.0) * g_flEdgeColorMaxOpacity * (g_bFresnel ? 1.0 : 0.0);
    vec4 fresnelColor = vec4(g_vEdgeColor.xyz, fresnel);

    outputColor = mix(glassColor, fresnelColor, g_flOpacityScale);
#else
    //Simply multiply the color from the color texture with the illumination
    outputColor = vec4(illumination * color.rgb * g_vColorTint.xyz * tintFactor, color.a);
#endif

    // Different render mode definitions
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
    outputColor = vec4(worldNormal * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if renderMode_Illumination == 1
    outputColor = vec4(illumination, illumination, illumination, 1.0);
#endif

#if renderMode_VertexColor == 1 && F_VERTEX_COLOR == 1
    outputColor = vColorOut == vec4(vec3(0), 1) ? outputColor : vColorOut;
#endif

#if renderMode_Terrain_Blend == 1 && F_LAYERS > 0
    outputColor.rgb = vColorBlendValues.rgb;
#endif
}
