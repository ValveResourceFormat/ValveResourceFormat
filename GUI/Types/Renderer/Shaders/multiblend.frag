#version 460

// Render modes -- Switched on/off by code
#include "common/utils.glsl"
#include "common/rendermodes.glsl"
#define renderMode_Terrain_Blend 0
#define renderMode_VertexColor 0

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_TINT_MASK 0
#define F_NORMAL_MAP 0
#define F_TWO_LAYER_BLEND 0
#define F_WORLDSPACE_UVS 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;

in vec4 vBlendWeights;
in vec4 vBlendAlphas;
in vec4 vVertexColor;

in vec2 vTexCoordOut;
in vec2 vTexCoord1Out;
#if (F_TWO_LAYER_BLEND == 0)
in vec2 vTexCoord2Out;
in vec2 vTexCoord3Out;
#endif

out vec4 outputColor;

uniform sampler2D g_tColor0;
uniform sampler2D g_tColor1;
uniform sampler2D g_tColor2;
uniform sampler2D g_tColor3;

uniform sampler2D g_tNormal0;
uniform sampler2D g_tNormal1;
uniform sampler2D g_tNormal2;
uniform sampler2D g_tNormal3;

uniform sampler2D g_tSpecular0;
uniform sampler2D g_tSpecular1;
uniform sampler2D g_tSpecular2;
uniform sampler2D g_tSpecular3;

uniform sampler2D g_tTintMasks;

uniform vec4 g_vGlobalTint = vec4(1.0);
uniform vec4 g_vColorTint0;
uniform vec4 g_vColorTint1;
uniform vec4 g_vColorTint2;
uniform vec4 g_vColorTint3;
uniform vec4 g_vColorTintB0;
uniform vec4 g_vColorTintB1;
uniform vec4 g_vColorTintB2;
uniform vec4 g_vColorTintB3;

uniform vec3 vEyePosition;
uniform float g_flBumpStrength = 1.0;

//Interpolate between two tint colors based on the tint mask.
vec3 interpolateTint(int id, vec3 tint1, vec3 tint2, vec2 coords)
{
    float maskValue = texture(g_tTintMasks, coords)[id];
    return mix(tint1, tint2, maskValue);
}

// from texturing.glsl
float applyBlendModulation(float blendFactor, float blendMask, float blendSoftness)
{
    float minb = max(0.0, blendMask - blendSoftness);
    float maxb = min(1.0, blendMask + blendSoftness);

    return smoothstep(minb, maxb, blendFactor);
}



#if (F_NORMAL_MAP == 1)

vec3 DecodeNormal(vec4 bumpNormal)
{
    vec2 temp = vec2(bumpNormal.w, bumpNormal.y) * 2.0 - 1.0;
    temp.y = -temp.y;
    return vec3(temp, sqrt(saturate(1 - dot(temp,temp))));
}

//Calculate the normal of this fragment in world space
vec3 calculateWorldNormal(vec3 normalMap, vec3 normal, vec3 tangent, vec3 bitangent)
{
    //vec3 tangent = vec3(vNormalOut.z, vNormalOut.y, -vNormalOut.x);
    //vec3 bitangent = cross(vNormalOut, tangent);
    //Make the tangent space matrix
    mat3 tangentSpace = mat3(tangent, bitangent, normal);

    //Calculate the tangent normal in world space and return it
    return normalize(tangentSpace * normalMap);
}
#endif

//Main entry point
void main()
{
    //Get the base color from the color texture
    vec4 color0 = texture(g_tColor0, vTexCoordOut);
    vec4 color1 = texture(g_tColor1, vTexCoord1Out);
#if F_TWO_LAYER_BLEND == 0
    vec4 color2 = texture(g_tColor2, vTexCoord2Out);
    vec4 color3 = texture(g_tColor3, vTexCoord3Out);
#endif


    // Get blend weights
    float blendWeight1 = applyBlendModulation(vBlendWeights.x, color1.w, vBlendAlphas.x);

    float invBlendWeight1 = 1.0 - blendWeight1;

#if F_TWO_LAYER_BLEND == 0
    float blendWeight2 = applyBlendModulation(vBlendWeights.y, color2.w, vBlendAlphas.y);
    blendWeight2 = clamp(blendWeight2, 0.0, invBlendWeight1);

    float invBlendWeight2 = invBlendWeight1 - blendWeight2;

    float blendWeight3 = applyBlendModulation(vBlendWeights.z, color3.w, vBlendAlphas.z);
    blendWeight3 = clamp(blendWeight3, 0.0, invBlendWeight2);

    // remaining weight
    float blendWeight0 = invBlendWeight2 - blendWeight3;
#else
    float blendWeight0 = invBlendWeight1;
#endif

    //Get specular
    vec4 specular0 = texture(g_tSpecular0, vTexCoordOut);
    vec4 specular1 = texture(g_tSpecular1, vTexCoord1Out);
#if F_TWO_LAYER_BLEND == 0
    vec4 specular2 = texture(g_tSpecular2, vTexCoord2Out);
    vec4 specular3 = texture(g_tSpecular3, vTexCoord3Out);
#endif


    //Simple blending
    //Calculate each of the 4 colours to blend
#if F_TINT_MASK
    // Include tint mask
    vec3 tint0 = interpolateTint(0, SrgbGammaToLinear(g_vColorTint0.rgb), SrgbGammaToLinear(g_vColorTintB0.rgb), vTexCoordOut);
    vec3 tint1 = interpolateTint(1, SrgbGammaToLinear(g_vColorTint1.rgb), SrgbGammaToLinear(g_vColorTintB1.rgb), vTexCoord1Out);
    #if F_TWO_LAYER_BLEND == 0
        vec3 tint2 = interpolateTint(2, SrgbGammaToLinear(g_vColorTint2.rgb), SrgbGammaToLinear(g_vColorTintB2.rgb), vTexCoord2Out);
        vec3 tint3 = interpolateTint(3, SrgbGammaToLinear(g_vColorTint3.rgb), SrgbGammaToLinear(g_vColorTintB3.rgb), vTexCoord3Out);
    #endif
#else
    vec3 tint0 = SrgbGammaToLinear(g_vColorTint0.rgb);
    vec3 tint1 = SrgbGammaToLinear(g_vColorTint1.rgb);
    vec3 tint2 = SrgbGammaToLinear(g_vColorTint2.rgb);
    vec3 tint3 = SrgbGammaToLinear(g_vColorTint3.rgb);
#endif
    vec3 c0 = blendWeight0 * color0.rgb * tint0;
    vec3 c1 = blendWeight1 * color1.rgb * tint1;
    #if F_TWO_LAYER_BLEND == 0
        vec3 c2 = blendWeight2 * color2.rgb * tint2;
        vec3 c3 = blendWeight3 * color3.rgb * tint3;
    #endif

#if F_TWO_LAYER_BLEND == 0
    //Add up the result
    vec3 finalColor = c0 + c1 + c2 + c3;
#else
    //Add up the result
    vec3 finalColor = c0 + c1;
#endif

    finalColor *= vVertexColor.rgb * SrgbGammaToLinear(g_vGlobalTint.rgb);

#if (F_NORMAL_MAP == 1)
    //Get normal
    // horribly, they actually correct normal orientations to match with texcoord rotation. We're not doing that.
    vec4 normal0 = texture(g_tNormal0, vTexCoordOut);
    vec4 normal1 = texture(g_tNormal1, vTexCoord1Out);

    #if F_TWO_LAYER_BLEND == 0
        vec4 normal2 = texture(g_tNormal2, vTexCoord2Out);
        vec4 normal3 = texture(g_tNormal3, vTexCoord3Out);

        //calculate blended normal
        vec4 bumpNormal = blendWeight0 * normal0 + blendWeight1 * normal1 + blendWeight2 * normal2 + blendWeight3 * normal3;
    #else
        //calculate blended normal
        vec4 bumpNormal = blendWeight0 * normal0 + blendWeight1 * normal1;
    #endif

    //Reconstruct the tangent vector from the map
    vec3 finalBumpNormal = DecodeNormal(bumpNormal);

    finalBumpNormal.xy *= g_flBumpStrength;

    vec3 normal = normalize(vNormalOut.xyz);
    vec3 tangent = normalize(vTangentOut.xyz);
    vec3 bitangent = normalize(vBitangentOut.xyz);

    vec3 finalNormal = calculateWorldNormal(finalBumpNormal, normal, tangent, bitangent);
#else
    vec3 finalNormal = normalize(vNormalOut.xyz);
#endif

    //Don't need lighting yet
    //Get the direction from the fragment to the light - light position == camera position for now
    vec3 lightDirection = normalize(vEyePosition - vFragPosition);

#if renderMode_FullBright == 1
    float illumination = 1.0;
#else
    //Calculate half-lambert lighting
    float illumination = dot(finalNormal, lightDirection);
    illumination = illumination * 0.5 + 0.5;
    illumination = pow2(illumination);
    illumination = min(illumination + 0.3, 1.0);
#endif

#if F_TWO_LAYER_BLEND == 0
    //Calculate specular
    vec4 blendSpecular = blendWeight0 * specular0 + blendWeight1 * specular1 + blendWeight2 * specular2 + blendWeight3 * specular3;
#else
    //Calculate specular
    vec4 blendSpecular = blendWeight0 * specular0 + blendWeight1 * specular1;
#endif

    float NoL = ClampToPositive(dot(lightDirection, finalNormal));
    float specular = blendSpecular.x * pow(NoL, 6.0);

    outputColor = vec4(illumination * finalColor.rgb + vec3(0.7) * specular, vVertexColor.a);

#if renderMode_Color == 1
    outputColor = vec4(finalColor, 1.0);
#endif

#if renderMode_Terrain_Blend == 1
    outputColor = vec4(vBlendWeights.xyz, 1.0);
#endif

#if renderMode_VertexColor == 1
    outputColor = vec4(vVertexColor.rgb, 1.0);
#endif

#if renderMode_Normals == 1
    outputColor = vec4(PackToColor(vNormalOut), 1.0);
#endif

#if renderMode_Tangents == 1 && F_NORMAL_MAP == 1
    outputColor = vec4(PackToColor(tangent), 1.0);
#endif

#if renderMode_BumpMap == 1 && F_NORMAL_MAP == 1
    outputColor = vec4(bumpNormal.xyz, 1.0);
#endif

#if renderMode_BumpNormals == 1
    outputColor = vec4(PackToColor(finalNormal), 1.0);
#endif

#if renderMode_Illumination == 1
    outputColor = vec4(vec3(illumination), 1.0);
#endif
}
