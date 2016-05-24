#version 330

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;

in vec2 vTexCoordOut;

out vec4 outputColor;

uniform float g_flAlphaTestReference;
uniform sampler2D g_tColor;
uniform sampler2D g_tNormal;

uniform vec3 vLightPosition;

//Main entry point
void main()
{
    //Get the ambient color from the color texture
    vec4 color = texture2D(g_tColor, vTexCoordOut);

	if(color.a <= g_flAlphaTestReference)
    {
        discard;
    }

	//Get the direction from the fragment to the light - light position == camera position for now
    vec3 lightDirection = normalize(vLightPosition - vFragPosition);

    //Get the world normal for this fragment
    vec3 worldNormal = vNormalOut;

    //Calculate half-lambert lighting
    float illumination = dot(worldNormal, lightDirection);
    illumination = illumination * 0.5 + 0.5;
    illumination = illumination * illumination;
	
	// TODO: for now, screw the actual illumination
	illumination = 1.0;

    //Simply multiply the color from the color texture with the illumination
    outputColor = vec4(illumination * color.rgb, color.a);
}
