#version 460

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_ALPHA_TEST 0
//End of parameter defines

in vec3 vFragPosition;

in vec2 vTexCoordOut;
in vec4 vTintColorFadeOut;

out vec4 outputColor;

uniform float g_flAlphaTestReference;
uniform sampler2D g_tColor;
uniform sampler2D g_tTintMask;


//Main entry point
void main()
{
    //Get the ambient color from the color texture
    vec4 color = texture(g_tColor, vTexCoordOut);

#if F_ALPHA_TEST == 1
    if (color.a < g_flAlphaTestReference)
    {
       discard;
    }
#endif

    //Calculate tint color
    float tintStrength = texture(g_tTintMask, vTexCoordOut).y;
    vec3 tintFactor = mix(vec3(1.0), vTintColorFadeOut.rgb, tintStrength);

    //Simply multiply the color from the color texture with the illumination
    //outputColor = vec4(color.rgb * tintFactor, color.a * vTintColorFadeOut.a);
    outputColor = vec4(color.rgb, 1.0);
}
