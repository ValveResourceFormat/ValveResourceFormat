#version 330

in vec3 vFragPosition;

in vec2 vTexCoordOut;

out vec4 outputColor;

uniform float g_flAlphaTestReference;
uniform sampler2D g_tColor2;
uniform sampler2D g_tTintMask;

uniform vec3 vLightPosition;

uniform vec4 m_vTintColorSceneObject;
uniform vec3 m_vTintColorDrawCall;

uniform vec4 g_vTexCoordOffset;
uniform vec4 g_vTexCoordScale;

//Main entry point
void main()
{
    //Get the ambient color from the color texture
    vec4 color = texture2D(g_tColor2, vTexCoordOut * g_vTexCoordScale.xy + g_vTexCoordOffset.xy) * vec4(m_vTintColorDrawCall.xyz, 1);

	if(color.a <= g_flAlphaTestReference)
    {
        discard;
    }

    //Calculate tint color
    float tintStrength = texture2D(g_tTintMask, vTexCoordOut * g_vTexCoordScale.xy + g_vTexCoordScale.xy).y;
    vec3 tintColor = m_vTintColorSceneObject.xyz * m_vTintColorDrawCall;
    vec3 tintFactor = tintStrength * tintColor + (1 - tintStrength) * vec3(1);

    //Simply multiply the color from the color texture with the illumination
    //outputColor = vec4(color.rgb * tintFactor, color.a);
    vec2 tc = vTexCoordOut * g_vTexCoordScale.xy + g_vTexCoordOffset.xy;
    outputColor = vec4( texture2D(g_tColor2, vTexCoordOut * g_vTexCoordScale.xy + g_vTexCoordOffset.xy).xyz, 1);
}
