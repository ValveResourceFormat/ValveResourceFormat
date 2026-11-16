#version 460

#include "crystal_features.slang"

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec4 vTangentOut;
in vec2 vTexCoordOut;
flat in vec4 vTintColorFadeOut;

out vec4 outputColor;

uniform sampler2D g_tColor; // SrgbRead(true) RepresentativeTexture

#if (F_TINT_MASK == 1)
    uniform sampler2D g_tTintMask;
#endif

#if (F_SPECULAR_CUBE_MAP == 1)
    uniform samplerCube g_tCubeMapExterior; // SrgbRead(true)
    #if (F_SEPARATE_INTERIOR_CUBE_MAP == 1)
        uniform samplerCube g_tCubeMapInterior; // SrgbRead(true)
    #endif
#endif

#include "../common/utils.slang"
#include "../common/view_constants.slang"

#include "../common/lighting_constants.slang"
#include "../common/lighting_common.slang"
#include "../common/shadowmapping.slang"
#include "../common/fog.slang"
#include "global_light.slang"

uniform vec3 g_vColorTint = vec3(1.0); // SrgbRead(true)
#if (F_TINT_MASK == 1)
    uniform vec3 g_vColorTint2 = vec3(1.0); // SrgbRead(true)
#endif

uniform float g_flOpacityScale = 1.0;
uniform float g_flSpecularIntensity = 1.0;
uniform float g_flFresnelReflectance = 0.0;
uniform float g_flFresnelExponent = 5.0;
uniform float g_flCubeMapScalarExterior = 1.0;
uniform float g_flCubeMapScalarInterior = 1.0;

vec3 GetAlbedo(vec4 color, vec2 texCoord)
{
    vec3 tint = g_vColorTint * vTintColorFadeOut.rgb;

#if (F_TINT_MASK == 1)
    float tintMask = texture(g_tTintMask, texCoord).y;
    tint = mix(g_vColorTint2, tint, tintMask);
#endif

    return color.rgb * g_vColorTint * tint;
}

// The specular light and the cube map reflection, scaled by the fresnel term
vec3 GetReflection(vec3 normal, vec3 toCamera)
{
    vec3 reflectionDir = reflect(toCamera, normal);

    float highlight = CalculateDotaSpecular(reflectionDir) * g_flSpecularIntensity;

#if (F_SPECULAR_CUBE_MAP == 0)
    highlight *= vTintColorFadeOut.a;
#endif

    vec3 specularColor = g_vSpecularAlbedoColor.rgb + g_vSpecularLitColor.rgb;
    vec3 reflection = specularColor * highlight;

#if (F_SPECULAR_CUBE_MAP == 1)
    #if (D_CULL_FRONT == 1 && F_SEPARATE_INTERIOR_CUBE_MAP == 1)
        reflection += texture(g_tCubeMapInterior, -reflectionDir).rgb * g_flCubeMapScalarInterior;
    #else
        reflection += texture(g_tCubeMapExterior, -reflectionDir).rgb * g_flCubeMapScalarExterior;
    #endif
#endif

    float facing = saturate(dot(toCamera, normal));
    float fresnel = g_flFresnelReflectance + (1.0 - g_flFresnelReflectance) * pow(abs(1.0 - facing), g_flFresnelExponent);

    return reflection * fresnel;
}

void main()
{
    vec4 color = texture(g_tColor, vTexCoordOut);

    // The interior pass shows the back faces, lit as if they faced the viewer
#if (D_CULL_FRONT == 1)
    vec3 normal = -vNormalOut;
    float alpha = 1.0;
#else
    vec3 normal = vNormalOut;
    float alpha = color.a * g_flOpacityScale;
#endif

    float sunShadow;
    vec3 illumination = CalculateDotaGlobalLight(normal, vFragPosition, sunShadow);

    vec3 toCamera = normalize(g_vCameraPositionWs - vFragPosition);
    vec3 litColor = GetAlbedo(color, vTexCoordOut) * illumination + GetReflection(normal, toCamera);

    outputColor = vec4(litColor, alpha);
    ApplyFog(outputColor.rgb, vFragPosition);
}
