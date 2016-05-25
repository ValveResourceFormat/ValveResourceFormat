#version 330

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;

in vec4 vWeightsOut;

in vec2 vTexCoordOut;

out vec4 outputColor;

uniform float g_flAlphaTestReference;
uniform sampler2D g_tColor0;
uniform sampler2D g_tColor1;
uniform sampler2D g_tColor2;
uniform sampler2D g_tColor3;
uniform sampler2D g_tNormal;

uniform vec3 vLightPosition;

//Main entry point
void main()
{
    //Get the ambient color from the color texture
    vec4 color0 = texture2D(g_tColor0, vTexCoordOut);
    vec4 color1 = texture2D(g_tColor1, vTexCoordOut);
    vec4 color2 = texture2D(g_tColor2, vTexCoordOut);
    vec4 color3 = texture2D(g_tColor3, vTexCoordOut);

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
    vec4 colorWeights = normalize(vWeightsOut);
    //outputColor = colorWeights;    //Debug, show weights
    outputColor = colorWeights.x * color0 + colorWeights.y * color1 + colorWeights.z * color2 + colorWeights.w * color3;
}
