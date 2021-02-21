#version 330

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define param_F_ALPHA_TEST 0
#define param_HemiOctIsoRoughness_RG_B 0
#define param_LegacySource1InvertNormals 0
//End of parameter defines

// Render modes -- Switched on/off by code
#define param_renderMode_FullBright 0
#define param_renderMode_Color 0
#define param_renderMode_Normals 0
#define param_renderMode_Tangents 0
#define param_renderMode_BumpMap 0
#define param_renderMode_BumpNormals 0
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

uniform vec3 vLightPosition;
uniform vec4 g_vColorTint;

vec3 oct_to_float32x3(vec2 e)
{
    vec3 v = vec3(e.xy, 1.0 - abs(e.x) - abs(e.y));
    return normalize(v);
}

//Calculate the normal of this fragment in world space
vec3 calculateWorldNormal()
{
    //Get the noral from the texture map -- Normal map seems broken
    vec4 bumpNormal = texture2D(g_tNormal, vTexCoordOut);

    //Reconstruct the tangent vector from the map
#if param_HemiOctIsoRoughness_RG_B == 1
    vec2 temp = vec2(bumpNormal.x + bumpNormal.y -1.003922, bumpNormal.x - bumpNormal.y);
    vec3 tangentNormal = oct_to_float32x3(temp);
#else
    //vec2 temp = vec2(bumpNormal.w, bumpNormal.y) * 2 - 1;
    //vec3 tangentNormal = vec3(temp, sqrt(1 - temp.x * temp.x - temp.y * temp.y));
    vec2 temp = vec2(bumpNormal.w + bumpNormal.y -1.003922, bumpNormal.w - bumpNormal.y);
    vec3 tangentNormal = oct_to_float32x3(temp);
#endif
#if param_LegacySource1InvertNormals == 1
	tangentNormal.y *= -1.0;
#endif

    vec3 normal = vNormalOut;
    vec3 tangent = vTangentOut.xyz;
    vec3 bitangent = vBitangentOut;

    //Make the tangent space matrix
    mat3 tangentSpace = mat3(tangent, bitangent, normal);

    //Calculate the tangent normal in world space and return it
    return normalize(tangentSpace * tangentNormal);
}

//Main entry point
void main()
{
    //Get the direction from the fragment to the light - light position == camera position for now
    vec3 lightDirection = normalize(vLightPosition - vFragPosition);

    //Get the ambient color from the color texture
    vec4 color = texture2D(g_tColor, vTexCoordOut);

#if param_F_ALPHA_TEST == 1
	if (color.a < g_flAlphaTestReference)
    {
       discard;
    }
#endif

    //Get the world normal for this fragment
    vec3 worldNormal = calculateWorldNormal();

#if param_renderMode_FullBright == 1
    float illumination = 1.0;
#else
    //Calculate lambert lighting
    float illumination = max(0.0, dot(worldNormal, lightDirection));
    illumination = illumination * 0.7 + 0.3;//add ambient
#endif

    //Simply multiply the color from the color texture with the illumination
    outputColor = vec4(illumination * color.rgb * g_vColorTint.rgb, color.a);

#if param_renderMode_Color == 1
	outputColor = vec4(color.rgb, 1.0);
#endif

#if param_renderMode_BumpMap == 1
	outputColor = texture2D(g_tNormal, vTexCoordOut);
#endif

#if param_renderMode_Tangents == 1
	outputColor = vec4(vTangentOut.xyz * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if param_renderMode_Normals == 1
	outputColor = vec4(vNormalOut * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if param_renderMode_BumpNormals == 1
	outputColor = vec4(worldNormal * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if param_renderMode_Illumination == 1
	outputColor = vec4(illumination, illumination, illumination, 1.0);
#endif
}
