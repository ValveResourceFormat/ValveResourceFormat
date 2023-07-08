#version 330

// Render modes -- Switched on/off by code
#include "common/utils.glsl"
#include "common/rendermodes.glsl"
#define renderMode_Terrain_Blend 0
#define renderMode_Ambient_Occlusion 0

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_TINT_MASK 0
#define F_NORMAL_MAP 0
#define F_TWO_LAYER_BLEND 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;

in vec4 vBlendWeights;
in vec4 vWeightsOut1;
in vec4 vWeightsOut2;

in vec2 vTexCoordOut;

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

uniform float g_flTexCoordScale0;
uniform float g_flTexCoordScale1;
uniform float g_flTexCoordScale2;
uniform float g_flTexCoordScale3;

uniform vec4 g_vColorTint0;
uniform vec4 g_vColorTintB0;
uniform vec4 g_vColorTint1;
uniform vec4 g_vColorTintB1;
uniform vec4 g_vColorTint2;
uniform vec4 g_vColorTintB2;
uniform vec4 g_vColorTint3;
uniform vec4 g_vColorTintB3;

uniform float g_flTexCoordRotate0;
uniform float g_flTexCoordRotate1;
uniform float g_flTexCoordRotate2;
uniform float g_flTexCoordRotate3;

#include "common/lighting.glsl"
uniform vec3 vEyePosition;

vec2 getTexCoord(float scale, float rotation) {
    //Transform degrees to radians
    float r = rotation * 3.141593/180.0;

    //Scale texture
    vec2 coord = vTexCoordOut/scale;

    //Rotate vector
    return vec2(cos(r) * coord.x - sin(r) * coord.y, sin(r) * coord.x + cos(r) * coord.y);
}

//Interpolate between two tint colors based on the tint mask and coordinate scale.
vec4 interpolateTint(int id, vec4 tint1, vec4 tint2, float coordScale, float coordRotation)
{
    float maskValue = texture(g_tTintMasks, getTexCoord(coordScale, coordRotation))[id];
    return tint1 * (maskValue) + tint2 * (1-maskValue);
}

//Main entry point
void main()
{
    //Calculate coordinates
    vec2 coord0 = getTexCoord(g_flTexCoordScale0, g_flTexCoordRotate0);
    vec2 coord1 = getTexCoord(g_flTexCoordScale1, g_flTexCoordRotate1);
#if F_TWO_LAYER_BLEND == 0
    vec2 coord2 = getTexCoord(g_flTexCoordScale2, g_flTexCoordRotate2);
    vec2 coord3 = getTexCoord(g_flTexCoordScale3, g_flTexCoordRotate3);
#endif

    //Get the ambient color from the color texture
    vec4 color0 = texture(g_tColor0, coord0);
    vec4 color1 = texture(g_tColor1, coord1);
#if F_TWO_LAYER_BLEND == 0
    vec4 color2 = texture(g_tColor2, coord2);
    vec4 color3 = texture(g_tColor3, coord3);
#endif

    //Get normal
    vec4 normal0 = texture(g_tNormal0, coord0);
    vec4 normal1 = texture(g_tNormal1, coord1);
#if F_TWO_LAYER_BLEND == 0
    vec4 normal2 = texture(g_tNormal2, coord2);
    vec4 normal3 = texture(g_tNormal3, coord3);
#endif

    //Get specular
    vec4 specular0 = texture(g_tSpecular0, coord0);
    vec4 specular1 = texture(g_tSpecular1, coord1);
#if F_TWO_LAYER_BLEND == 0
    vec4 specular2 = texture(g_tSpecular2, coord2);
    vec4 specular3 = texture(g_tSpecular3, coord3);
#endif

    //calculate blend
    vec4 blend = vec4(max(0, 1 - vBlendWeights.x - vBlendWeights.y - vBlendWeights.z), vBlendWeights.x, max(0, vBlendWeights.y - vBlendWeights.x), max(0, vBlendWeights.z - vBlendWeights.w - vBlendWeights.y));
    blend = blend/(blend.x + blend.y + blend.z + blend.w);

    //Simple blending
    //Calculate each of the 4 colours to blend
#if F_TINT_MASK
    // Include tint mask
    vec4 c0 = blend.x * color0 * interpolateTint(0, g_vColorTint0, g_vColorTintB0, g_flTexCoordScale0, g_flTexCoordRotate0);
    vec4 c1 = blend.y * color1 * interpolateTint(1, g_vColorTint1, g_vColorTintB1, g_flTexCoordScale1, g_flTexCoordRotate1);
    #if F_TWO_LAYER_BLEND == 0
        vec4 c2 = blend.z * color2 * interpolateTint(2, g_vColorTint2, g_vColorTintB2, g_flTexCoordScale2, g_flTexCoordRotate2);
        vec4 c3 = blend.w * color3 * interpolateTint(3, g_vColorTint3, g_vColorTintB3, g_flTexCoordScale3, g_flTexCoordRotate3);
    #endif
#else
    vec4 c0 = blend.x * color0;
    vec4 c1 = blend.y * color1;
    #if F_TWO_LAYER_BLEND == 0
        // TODO: this blending is probably wrong
        vec4 c2 = blend.z * color2;
        vec4 c3 = blend.w * color3;
    #endif
#endif

#if F_TWO_LAYER_BLEND == 0
    //Add up the result
    vec4 finalColor = c0 + c1 + c2 + c3;
#else
    //Add up the result
    vec4 finalColor = c0 + c1;
#endif

#if F_NORMAL_MAP
    #if F_TWO_LAYER_BLEND == 0
        //calculate blended normal
        vec4 bumpNormal = blend.x * normal0 + blend.y * normal1 + blend.z * normal2 + blend.w * normal3;
    #else
        //calculate blended normal
        vec4 bumpNormal = blend.x * normal0 + blend.y * normal1;
    #endif

    //Reconstruct the tangent vector from the map
    vec2 temp = vec2(bumpNormal.w, bumpNormal.y) * 2 - 1;
    vec3 finalBumpNormal = vec3(temp, 1 - dot(temp,temp));

    vec3 tangent = vec3(vNormalOut.z, vNormalOut.y, -vNormalOut.x);
    vec3 bitangent = cross(vNormalOut, tangent);

    //Make the tangent space matrix
    mat3 tangentSpace = mat3(tangent, bitangent, vNormalOut);

    //Calculate the tangent normal in world space and return it
    vec3 finalNormal = normalize(tangentSpace * finalBumpNormal);
#else
    vec3 finalNormal = vNormalOut.xyz;
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
    illumination = illumination * illumination;
    illumination = min(illumination + 0.3, 1.0);
#endif

#if F_TWO_LAYER_BLEND == 0
    //Calculate specular
    vec4 blendSpecular = blend.x * specular0 + blend.y * specular1 + blend.z * specular2 + blend.w * specular3;
#else
    //Calculate specular
    vec4 blendSpecular = blend.x * specular0 + blend.y * specular1;
#endif

    float specular = blendSpecular.x * pow(max(0,dot(lightDirection, finalNormal)), 6);

    //Apply ambient occlusion
    vec4 occludedColor = finalColor * vec4(vWeightsOut2.xyz, 1.0);

    outputColor = vec4(illumination * occludedColor.xyz + vec3(0.7) * specular, 1);

#if renderMode_Color == 1
    outputColor = vec4(finalColor.rgb, 1.0);
#endif

#if renderMode_Terrain_Blend == 1
    outputColor = vec4(blend.xyz, 1.0);
#endif

#if renderMode_Ambient_Occlusion == 1
    outputColor = vec4(vWeightsOut2.xyz, 1.0);
#endif

#if renderMode_Normals == 1
    outputColor = vec4(vNormalOut * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if renderMode_Tangents == 1 && F_NORMAL_MAP == 1
    outputColor = vec4(tangent * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if renderMode_BumpMap == 1 && F_NORMAL_MAP == 1
    outputColor = vec4(bumpNormal.xyz, 1.0);
#endif

#if renderMode_BumpNormals == 1
    outputColor = vec4(finalNormal * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if renderMode_Illumination == 1
    outputColor = vec4(illumination, illumination, illumination, 1.0);
#endif
}
