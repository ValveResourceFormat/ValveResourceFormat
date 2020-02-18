#version 330

// Render modes -- Switched on/off by code
#define param_renderMode_Color 0
#define param_renderMode_Illumination 0

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;

in vec2 vTexCoordOut;

out vec4 outputColor;

uniform float g_flAlphaTestReference;
uniform sampler2D g_tColor;
uniform sampler2D g_tNormal;
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
    vec4 color = texture2D(g_tColor, vTexCoordOut * g_vTexCoordScale.xy + g_vTexCoordOffset.xy) * vec4(m_vTintColorDrawCall.xyz, 1);

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
    illumination = min(illumination + 0.3, 1.0);

    //Calculate tint color
    float tintStrength = texture2D(g_tTintMask, vTexCoordOut * g_vTexCoordScale.xy + g_vTexCoordScale.xy).y;
    vec3 tintColor = m_vTintColorSceneObject.xyz * m_vTintColorDrawCall;
    vec3 tintFactor = tintStrength * tintColor + (1 - tintStrength) * vec3(1);

    //Simply multiply the color from the color texture with the illumination
    outputColor = vec4(illumination * color.rgb * tintFactor, color.a);

    // Different render mode definitions
#if param_renderMode_Color == 1
	outputColor = vec4(color.rgb, 1.0);
#endif

#if param_renderMode_Illumination == 1
	outputColor = vec4(illumination, 0.0, 0.0, 1.0);
#endif
}
