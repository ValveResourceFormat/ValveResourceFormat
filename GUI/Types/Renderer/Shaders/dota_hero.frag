#version 330

// Render modes -- Switched on/off by code
#define param_renderMode_FullBright 0
#define param_renderMode_Color 0
#define param_renderMode_Normals 0
#define param_renderMode_Tangents 0
#define param_renderMode_BumpMap 0
#define param_renderMode_BumpNormals 0
#define param_renderMode_Illumination 0
#define param_renderMode_Mask1 0
#define param_renderMode_Mask2 0
#define param_renderMode_Metalness 0
#define param_renderMode_Specular 0
#define param_renderMode_RimLight 0

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;

in vec2 vTexCoordOut;

out vec4 outputColor;

uniform float g_flAlphaTestReference;
uniform sampler2D g_tColor;
uniform sampler2D g_tNormal;
uniform sampler2D g_tMasks1;
uniform sampler2D g_tMasks2;
uniform sampler2D g_tDiffuseWarp;

uniform vec3 vLightPosition;
uniform vec3 vEyePosition;

//Calculate the normal of this fragment in world space
vec3 calculateWorldNormal()
{
    //Get the noral from the texture map -- Normal map seems broken
    vec4 bumpNormal = texture2D(g_tNormal, vTexCoordOut);

    //Reconstruct the tangent vector from the map
    vec2 temp = vec2(bumpNormal.w, bumpNormal.y) * 2 - 1;
    vec3 tangentNormal = vec3(temp, sqrt(1 - temp.x * temp.x - temp.y * temp.y));

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

    //Get the view direction
    vec3 viewDirection = normalize(vEyePosition - vFragPosition);

    //Read textures
    vec4 color = texture2D(g_tColor, vTexCoordOut);
    vec4 mask1 = texture2D(g_tMasks1, vTexCoordOut);
    vec4 mask2 = texture2D(g_tMasks2, vTexCoordOut);

	//Get the world normal for this fragment
    vec3 worldNormal = calculateWorldNormal();

	//Get shadow and light color
    vec3 shadowColor = texture2D(g_tDiffuseWarp, vec2(0, mask1.g)).rgb;

#if param_renderMode_FullBright == 1
    float illumination = 1.0;
#else
    //Calculate half-lambert lighting
    float illumination = dot(worldNormal, lightDirection);
    illumination = illumination * 0.5 + 0.5;
    illumination = illumination * illumination;

    //Self illumination - mask 1 channel A
    illumination = illumination + mask1.a;
#endif

    //Calculate ambient color
    vec3 ambientColor = illumination * color.rgb;

    //Get metalness for future use - mask 1 channel B
    float metalness = mask1.b;

	//Calculate Blinn specular based on reflected light
    vec3 halfDir = normalize(lightDirection + viewDirection);
    float specularAngle = max(dot(halfDir, worldNormal), 0.0);

    //Calculate final specular based on specular exponent - mask 2 channel A
    float specular = pow(specularAngle, mask2.a * 100);
    //Multiply by mapped specular intensity - mask 2 channel R
    specular = illumination * specular * mask2.r;

    //Calculate specular light color based on the specular tint map - mask 2 channel B
    vec3 specularColor = mix(vec3(1.0, 1.0, 1.0), color.rgb, mask2.b);

	//Calculate rim light
    float rimLight = 1.0 - abs(dot(worldNormal, viewDirection));
    rimLight = pow(rimLight, 2);

    //Multiply the rim light by the rim light intensity - mask 2 channel G
    rimLight = rimLight * mask2.g;

	//Final color
    vec3 finalColor = ambientColor * mix(1.0, 0.5, metalness) + specularColor * specular + color.rgb * rimLight;

    //Simply multiply the color from the color texture with the illumination
    outputColor = vec4(finalColor, color.a);

	// == End of shader

	// Different render mode definitions
#if param_renderMode_Color == 1
	outputColor = vec4(color.rgb, 1.0);
#endif

#if param_renderMode_Illumination == 1
	outputColor = vec4(illumination, 0.0, 0.0, 1.0);
#endif

#if param_renderMode_Mask1 == 1
	outputColor = vec4(mask1.rgb, 1.0);
#endif

#if param_renderMode_Mask2 == 1
	outputColor = vec4(mask2.rgb, 1.0);
#endif

#if param_renderMode_BumpMap == 1
	outputColor = texture2D(g_tNormal, vTexCoordOut);
#endif

#if param_renderMode_Tangents == 1
	outputColor = outputColor = vec4(vTangentOut.xyz, 1.0);
#endif

#if param_renderMode_Normals == 1
	outputColor = vec4(vNormalOut, 1.0);
#endif

#if param_renderMode_BumpNormals == 1
	outputColor = vec4(worldNormal, 1.0);
#endif

#if param_renderMode_Metalness == 1
	outputColor = vec4(metalness, metalness, metalness, 1.0);
#endif

#if param_renderMode_Specular == 1
	outputColor = vec4(specularColor * specular, 1.0);
#endif

#if param_renderMode_RimLight == 1
	outputColor = vec4(color.rgb * rimLight, 1.0);
#endif
}


