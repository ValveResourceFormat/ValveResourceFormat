#version 460

// Render modes -- Switched on/off by code
#define renderMode_FullBright 0
#define renderMode_Color 0
#define renderMode_Roughness 0
#define renderMode_Normals 0
#define renderMode_Tangents 0
#define renderMode_BumpMap 0
#define renderMode_BumpNormals 0
#define renderMode_Illumination 0
#define renderMode_Mask1 0
#define renderMode_Mask2 0
#define renderMode_Metalness 0
#define renderMode_Specular 0
#define renderMode_RimLight 0

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_MASKS_1 0
#define F_MASKS_2 0
#define F_ALPHA_TEST 0
#define F_SPECULAR_CUBE_MAP 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec2 vTexCoordOut;
flat in vec4 vTintColorFadeOut;

out vec4 outputColor;

uniform float g_flAlphaTestReference;
uniform sampler2D g_tColor;
uniform sampler2D g_tNormal;
uniform sampler2D g_tMasks1;
uniform sampler2D g_tMasks2;
//uniform sampler2D g_tDiffuseWarp;

#include "common/utils.glsl"
#include "common/ViewConstants.glsl"
#include "common/instancing.glsl"

// Material properties
uniform float g_flSpecularExponent = 100.0;

//Calculate the normal of this fragment in world space
vec3 calculateWorldNormal(vec3 vNormalTs)
{
    vec3 normal = normalize(vNormalOut);
    vec3 tangent = normalize(vTangentOut.xyz);
    vec3 bitangent = normalize(vBitangentOut);

    //Make the tangent space matrix
    mat3 tangentSpace = mat3(tangent, bitangent, normal);

    //Calculate the tangent normal in world space and return it
    return normalize(tangentSpace * vNormalTs);
}

//Main entry point
void main()
{
    //Get the direction from the fragment to the light - light position == camera position for now
    vec3 lightDirection = normalize(g_vCameraPositionWs - vFragPosition);

    //Get the view direction
    vec3 viewDirection = normalize(g_vCameraPositionWs - vFragPosition);

    //Read textures
    vec4 color = texture(g_tColor, vTexCoordOut);

#if F_ALPHA_TEST == 1
    if (color.a < g_flAlphaTestReference)
    {
       discard;
    }
#endif

#if F_MASKS_1
    vec4 mask1 = texture(g_tMasks1, vTexCoordOut);
#endif

#if F_MASKS_2
    vec4 mask2 = texture(g_tMasks2, vTexCoordOut);
#endif

    //Get the world normal for this fragment
    vec3 vNormalTs = DecodeDxt5Normal(texture(g_tNormal, vTexCoordOut));
    vec3 worldNormal = calculateWorldNormal(vNormalTs);

    //Get shadow and light color
    //vec3 shadowColor = texture(g_tDiffuseWarp, vec2(0, mask1.g)).rgb;

    //Calculate half-lambert lighting
    float illumination = dot(worldNormal, lightDirection);
    illumination = illumination * 0.5 + 0.5;
    illumination = pow2(illumination);

    #if F_MASKS_1
        //Self illumination - mask 1 channel A
        illumination = illumination + mask1.a;
    #endif

    if (g_iRenderMode == renderMode_FullBright)
    {
        illumination = 1.0;
    }

    //Calculate ambient color
    vec3 ambientColor = illumination * color.rgb;

#if F_MASKS_1
    //Get metalness for future use - mask 1 channel B
    float metalness = mask1.b;
#else
    float metalness = 0.0;
#endif

    //Calculate Blinn specular based on reflected light
    vec3 halfDir = normalize(lightDirection + viewDirection);
    float specularAngle = max(dot(halfDir, worldNormal), 0.0);

#if F_MASKS_2 && !F_SPECULAR_CUBE_MAP
    //Calculate final specular based on specular exponent - mask 2 channel A
    float specular = pow(specularAngle, mask2.a * g_flSpecularExponent);
    //Multiply by mapped specular intensity - mask 2 channel R
    specular = illumination * specular * mask2.r;

    //Calculate specular light color based on the specular tint map - mask 2 channel B
    vec3 specularColor = mix(vec3(1.0), color.rgb, mask2.b);
#else
    vec3 specularColor = vec3(0);
    float specular = 0.0;
#endif

    //Calculate rim light
    float rimLight = 1.0 - abs(dot(worldNormal, viewDirection));
    rimLight = pow2(rimLight);

#if F_MASKS_2
    //Multiply the rim light by the rim light intensity - mask 2 channel G
    rimLight = rimLight * mask2.g;
#endif

    //Final color
    vec3 finalColor = ambientColor * mix(1.0, 0.5, metalness) + specularColor * specular + color.rgb * rimLight;

    //Simply multiply the color from the color texture with the illumination
    outputColor = vec4(finalColor, color.a);
    outputColor *= vTintColorFadeOut;

    // == End of shader

    // Different render mode definitions
    if (g_iRenderMode == renderMode_Color)
    {
        outputColor = vec4(color.rgb, 1.0);
    }
    else if (g_iRenderMode == renderMode_Illumination)
    {
        outputColor = vec4(vec3(illumination), 1.0);
    }
    else if (g_iRenderMode == renderMode_BumpMap)
    {
        outputColor = vec4(SrgbGammaToLinear(PackToColor(vNormalTs)), 1.0);
    }
    else if (g_iRenderMode == renderMode_Tangents)
    {
        outputColor = vec4(SrgbGammaToLinear(PackToColor(vTangentOut.xyz)), 1.0);
    }
    else if (g_iRenderMode == renderMode_Normals)
    {
        outputColor = vec4(SrgbGammaToLinear(PackToColor(vNormalOut)), 1.0);
    }
    else if (g_iRenderMode == renderMode_BumpNormals)
    {
        outputColor = vec4(SrgbGammaToLinear(PackToColor(worldNormal)), 1.0);
    }
    else if (g_iRenderMode == renderMode_Metalness)
    {
        outputColor = vec4(SrgbGammaToLinear(vec3(metalness)), 1.0);
    }
    else if (g_iRenderMode == renderMode_Specular)
    {
        outputColor = vec4(specularColor * specular, 1.0);
    }
    else if (g_iRenderMode == renderMode_RimLight)
    {
        outputColor = vec4(color.rgb * rimLight, 1.0);
    }
#if F_MASKS_1
    else if (g_iRenderMode == renderMode_Mask1)
    {
        outputColor = vec4(SrgbGammaToLinear(mask1.rgb), 1.0);
    }
#endif
#if F_MASKS_2
    else if (g_iRenderMode == renderMode_Mask2)
    {
        outputColor = vec4(SrgbGammaToLinear(mask2.rgb), 1.0);
    }
#endif
}
