#version 330

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;

in vec4 vBlendWeights;
in vec4 vWeightsOut1;
in vec4 vWeightsOut2;

in vec2 vTexCoordOut;

out vec4 outputColor;

uniform float g_flAlphaTestReference;
uniform sampler2D g_tColor0;
uniform sampler2D g_tColor1;
uniform sampler2D g_tColor2;
uniform sampler2D g_tColor3;
uniform sampler2D g_tNormal;
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

uniform vec3 vLightPosition;

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
    float maskValue = texture2D(g_tTintMasks, getTexCoord(coordScale, coordRotation))[id];
    return tint1 * (maskValue) + tint2 * (1-maskValue);
}

//Main entry point
void main()
{
    //Get the ambient color from the color texture
    vec4 color0 = texture2D(g_tColor0, getTexCoord(g_flTexCoordScale0, g_flTexCoordRotate0));
    vec4 color1 = texture2D(g_tColor1, getTexCoord(g_flTexCoordScale1, g_flTexCoordRotate1));
    vec4 color2 = texture2D(g_tColor2, getTexCoord(g_flTexCoordScale2, g_flTexCoordRotate2));
    vec4 color3 = texture2D(g_tColor3, getTexCoord(g_flTexCoordScale3, g_flTexCoordRotate3));

    //Don't need lighting yet
    //Get the direction from the fragment to the light - light position == camera position for now
    /*vec3 lightDirection = normalize(vLightPosition - vFragPosition);

    //Get the world normal for this fragment
    vec3 worldNormal = vNormalOut;

    //Calculate half-lambert lighting
    float illumination = dot(worldNormal, lightDirection);
    illumination = illumination * 0.5 + 0.5;
    illumination = illumination * illumination;
    
    // TODO: for now, screw the actual illumination
    illumination = 1.0;*/

    //Simple blending
    //Calculate each of the 4 colours to blend
    vec4 b2 = vec4(max(0, 1 - vBlendWeights.x - vBlendWeights.y - vBlendWeights.z), vBlendWeights.x, max(0, vBlendWeights.y - vBlendWeights.x), max(0, vBlendWeights.z - vBlendWeights.w - vBlendWeights.y));
    b2 = b2/(b2.x + b2.y + b2.z + b2.w);
    vec4 c0 = b2.x * color0 * interpolateTint(0, g_vColorTint0, g_vColorTintB0, g_flTexCoordScale0, g_flTexCoordRotate0);
    vec4 c1 = b2.y * color1 * interpolateTint(1, g_vColorTint1, g_vColorTintB1, g_flTexCoordScale1, g_flTexCoordRotate1);
    vec4 c2 = b2.z * color2 * interpolateTint(2, g_vColorTint2, g_vColorTintB2, g_flTexCoordScale2, g_flTexCoordRotate2);
    vec4 c3 = b2.w * color3 * interpolateTint(3, g_vColorTint3, g_vColorTintB3, g_flTexCoordScale3, g_flTexCoordRotate3);
    
    //Add up the result
    vec4 finalColor = c0 + c1 + c2 + c3;
    //Apply ambient occlusion
    finalColor *= vec4(vWeightsOut2.xyz, 1);

    outputColor = finalColor;
}
