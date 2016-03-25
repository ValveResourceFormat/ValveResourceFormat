#version 330

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;

in vec2 vTexCoordOut;

out vec4 outputColor;

uniform float alphaReference;
uniform sampler2D colorTexture;
uniform sampler2D normalTexture;

uniform vec3 vLightPosition;

//Main entry point
void main()
{
    //Get the direction from the fragment to the light - light position == camera position for now
    vec3 lightDirection = normalize(vLightPosition - vFragPosition);

    //Get the ambient color from the color texture
    vec4 color = texture2D(colorTexture, vTexCoordOut);

    //Get the world normal for this fragment
    vec3 worldNormal = vNormalOut;

    //Calculate half-lambert lighting
    float illumination = dot(worldNormal, lightDirection);
    illumination = illumination * 0.5 + 0.5;
    illumination = illumination * illumination;

    //Simply multiply the color from the color texture with the illumination
    outputColor = vec4(illumination * color.rgb, color.a);
}


