#version 330

// Render modes -- Switched on/off by code
#include "common/rendermodes.glsl"
#define renderMode_VertexColor 0

//Parameter defines - These are default values and can be overwritten based on material/model parameters
#define F_FULLBRIGHT 0
#define F_SOLID_COLOR 0
#define F_NORMAL_MAP 0
#define F_SPECULAR 0
#define F_MODULATE_SPECULAR_BY_ALPHA 0 
#define F_TINT_MASK 0
#define F_TINT_MASK2 0 // what does this do?
#define F_PAINT_VERTEX_COLORS 0
#define F_ALPHA_TEST 0
#define F_TRANSLUCENT 0
#define F_ALLOW_LIGHTING_ON_TRANSLUCENT 0
#define F_SCROLL_UV 0

#define HemiOctIsoRoughness_RG_B 0
//End of parameter defines

in vec3 vFragPosition;

in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec2 vTexCoordOut;
#if (F_PAINT_VERTEX_COLORS == 1)
    in vec4 vVertexColorOut;
#endif

out vec4 outputColor;

#if (F_SOLID_COLOR == 0)
    uniform sampler2D g_tColor;
    #if (F_TINT_MASK == 1)
        uniform sampler2D g_tTintMask;
    #endif
    #if (F_SPECULAR == 1)
        uniform sampler2D g_tSpecular; // Reflectance, SelfIllum, Bloom
        uniform vec4 g_vColorTint2 = vec4(1.0);
        uniform float g_flSpecularIntensity;
    #endif
    #if (F_SCROLL_UV == 1 || F_SCROLL_UV == 2)
        uniform sampler2D g_tScrollSpeed;
    #endif
#endif

#if (F_NORMAL_MAP == 1)
    uniform sampler2D g_tNormal;
    uniform float g_flBumpStrength = 1.0;
#endif

#if (F_SCROLL_UV == 1 || F_SCROLL_UV == 2)
    uniform float g_flScrollUvSpeed;
    uniform vec4 g_vScrollUvDirection;
#endif

uniform vec3 vEyePosition;
uniform float g_flTime;

uniform vec4 m_vTintColorSceneObject;
uniform vec3 m_vTintColorDrawCall;

uniform vec4 g_vColorTint;
uniform float g_flOpacityScale;

uniform float g_flAlphaTestReference = 0.5;

vec3 oct_to_float32x3(vec2 e)
{
    vec3 v = vec3(e.xy, 1.0 - abs(e.x) - abs(e.y));
    return normalize(v);
}

//Calculate the normal of this fragment in world space
vec3 calculateWorldNormal(vec4 bumpNormal)
{
    //Reconstruct the tangent vector from the map
#if HemiOctIsoRoughness_RG_B == 1
    vec2 temp = vec2(bumpNormal.x + bumpNormal.y -1.003922, bumpNormal.x - bumpNormal.y);
    vec3 tangentNormal = oct_to_float32x3(temp);
#else
    //vec2 temp = vec2(bumpNormal.w, bumpNormal.y) * 2 - 1;
    //vec3 tangentNormal = vec3(temp, sqrt(1 - temp.x * temp.x - temp.y * temp.y));
    vec2 temp = vec2(bumpNormal.w + bumpNormal.y -1.003922, bumpNormal.w - bumpNormal.y);
    vec3 tangentNormal = oct_to_float32x3(temp);
#endif

    tangentNormal.y *= -1.0;

    vec3 normal = vNormalOut;
    vec3 tangent = vTangentOut.xyz;
    vec3 bitangent = vBitangentOut;

    //Make the tangent space matrix
    mat3 tangentSpace = mat3(tangent, bitangent, normal);

    //Calculate the tangent normal in world space and return it
    return normalize(tangentSpace * tangentNormal);
}

void main()
{
    vec2 coordsColor = vTexCoordOut;
    vec2 coordsAll = vTexCoordOut;

    #if (F_SCROLL_UV == 1 || F_SCROLL_UV == 2)
        vec2 scrollTexel = texture(g_tScrollSpeed, coordsAll).ga;
        coordsColor = coordsColor + g_vScrollUvDirection.xy * g_flTime * g_flScrollUvSpeed * scrollTexel.x;
        #if (F_SCROLL_UV == 2)
            coordsAll = coordsColor;
        #endif
    #endif

    #if (F_SOLID_COLOR == 0)
        vec4 color = texture(g_tColor, coordsColor);
        #if (F_TRANSLUCENT == 1 && (F_SCROLL_UV == 1 || F_SCROLL_UV == 2))
            color.a = scrollTexel.y;
        #endif
    #else
        vec4 color = vec4(g_vColorTint.xyz, 1.0);
    #endif

    #if (F_NORMAL_MAP == 1)
        vec4 normal = texture(g_tNormal, coordsAll);
        vec3 worldNormal = calculateWorldNormal(normal);
    #else
        vec3 worldNormal = vNormalOut;
    #endif

#if F_ALPHA_TEST == 1
    if (color.a < g_flAlphaTestReference)
    {
       discard;
    }
#endif

    //Get the direction from the fragment to the light - light position == camera position for now
    vec3 lightDirection = normalize(vEyePosition - vFragPosition);
    vec3 viewDirection = normalize(vEyePosition - vFragPosition);

#if renderMode_FullBright == 1 || F_FULLBRIGHT == 1 || (F_TRANSLUCENT == 1 && F_ALLOW_LIGHTING_ON_TRANSLUCENT == 0)
    float illumination = 1.0;
#else
    //Calculate lambert lighting
    float illumination = max(0.0, dot(worldNormal, lightDirection));
    illumination = illumination * 0.7 + 0.4; //add ambient
#endif

    //Calculate tint color
    vec3 tintColor = m_vTintColorSceneObject.xyz * m_vTintColorDrawCall;

#if (F_TINT_MASK == 1 || F_TINT_MASK == 2)
    float tintStrength = texture(g_tTintMask, coordsAll).y;
    vec3 tintFactor = tintStrength * tintColor + (1 - tintStrength) * vec3(1);
#else
    vec3 tintFactor = tintColor;
#endif

    //Simply multiply the color from the color texture with the illumination
    outputColor = vec4(illumination * color.rgb * g_vColorTint.xyz * tintFactor, color.a);

    #if (F_PAINT_VERTEX_COLORS == 1)
        outputColor.rgb *= vVertexColorOut.rgb;
    #endif

    #if (F_SPECULAR == 1)
        vec3 specularTexel = texture(g_tSpecular, coordsAll).xyz;
        float specular = pow(max(0,dot(lightDirection, worldNormal)), 6);
        #if (F_MODULATE_SPECULAR_BY_ALPHA == 1)
            specular *= m_vTintColorSceneObject.w;
        #endif
        outputColor.rgb *=  vec3(1.0) + specularTexel.x * specular * g_flSpecularIntensity * g_vColorTint2.xyz;
        outputColor.rgb += specularTexel.y;
    #endif

#if renderMode_Color == 1
    outputColor = vec4(color.rgb, 1.0);
#endif

#if renderMode_BumpMap == 1 && F_NORMAL_MAP == 1
    outputColor = normal;
#endif

#if renderMode_Tangents == 1
    outputColor = vec4(vTangentOut.xyz * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if renderMode_Normals == 1
    outputColor = vec4(vNormalOut * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if renderMode_BumpNormals == 1
    outputColor = vec4(worldNormal * vec3(0.5) + vec3(0.5), 1.0);
#endif

#if renderMode_Illumination == 1
    outputColor = vec4(illumination, illumination, illumination, 1.0);
#endif

#if renderMode_VertexColor == 1 && F_PAINT_VERTEX_COLORS == 1
    outputColor = vVertexColorOut;
#endif
}
