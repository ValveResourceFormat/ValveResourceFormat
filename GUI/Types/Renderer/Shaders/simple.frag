#version 330

// Render modes -- Switched on/off by code
#define param_renderMode_FullBright 0
#define param_renderMode_Color 0
#define param_renderMode_Normals 0
#define param_renderMode_Tangents 0
#define param_renderMode_BumpMap 0
#define param_renderMode_BumpNormals 0
#define param_renderMode_Illumination 0

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define param_F_FULLBRIGHT 0
#define param_F_TINT_MASK 0
#define param_F_ALPHA_TEST 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;

in vec2 vTexCoordOut;

out vec4 outputColor;

uniform float g_flAlphaTestReference;
uniform sampler2D g_tColor;
uniform sampler2D g_tTintMask;

uniform vec3 vLightPosition;

uniform vec4 m_vTintColorSceneObject;
uniform vec3 m_vTintColorDrawCall;

uniform vec4 g_vTexCoordOffset;
uniform vec4 g_vTexCoordScale;
uniform vec4 g_vColorTint;

//Main entry point
void main()
{
    //Get the ambient color from the color texture
    vec4 color = texture2D(g_tColor, vTexCoordOut * g_vTexCoordScale.xy + g_vTexCoordOffset.xy) * vec4(m_vTintColorDrawCall.xyz, 1);

#if param_F_ALPHA_TEST == 1
	if (color.a < g_flAlphaTestReference)
    {
       discard;
    }
#endif

	//Get the direction from the fragment to the light - light position == camera position for now
    vec3 lightDirection = normalize(vLightPosition - vFragPosition);

    //Get the world normal for this fragment
    vec3 worldNormal = vNormalOut;

#if param_renderMode_FullBright == 1 || param_F_FULLBRIGHT == 1
    float illumination = 1.0;
#else
    //Calculate half-lambert lighting
    float illumination = max(0.0, dot(worldNormal, lightDirection));
    //illumination = illumination * 0.5 + 0.5;
    //illumination = illumination * illumination;
    //illumination = min(illumination + 0.3, 1.0);
    illumination = illumination * 0.7 + 0.3;
#endif

    //Calculate tint color
    vec3 tintColor = m_vTintColorSceneObject.xyz * m_vTintColorDrawCall;

#if param_F_TINT_MASK == 1
    float tintStrength = texture2D(g_tTintMask, vTexCoordOut * g_vTexCoordScale.xy + g_vTexCoordScale.xy).y;
    vec3 tintFactor = tintStrength * tintColor + (1 - tintStrength) * vec3(1);
#else
    vec3 tintFactor = tintColor;
#endif

    //Simply multiply the color from the color texture with the illumination
    outputColor = vec4(illumination * color.rgb * g_vColorTint.xyz * tintFactor, color.a);

    // Different render mode definitions
#if param_renderMode_Color == 1
	outputColor = vec4(color.rgb, 1.0);
#endif

#if param_renderMode_Normals
	outputColor = vec4(vNormalOut * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if param_renderMode_BumpNormals
	outputColor = vec4(worldNormal * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if param_renderMode_Illumination == 1
	outputColor = vec4(illumination, illumination, illumination, 1.0);
#endif
}
