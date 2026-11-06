#version 460

#include "studio_common.slang"
#include "../common/view_constants.slang"
#include "../common/fog.slang"
#include "../common/lighting_lpv.slang"

in vec3 vFragPosition;
in vec3 vNormalOut;
in vec2 vTexCoordOut;
in vec4 vTintOut;

out vec4 outputColor;

uniform sampler2D g_tColor; // Sampler(UserConfig) RepresentativeTexture

// The map bakes the light of every facing at a point into its light probes, with the ambient light, the shade and
// the light curve already applied. A flat shaded surface does not depend on its facing, so it takes the mean of all of them.
vec3 GetProbeLight(LightProbeSample_t probe, vec3 origin)
{
    if (F_FLAT_SHADE == 0)
    {
        return max(probe.Irradiance, vec3(0.0));
    }

    vec3 sum = vec3(0.0);

    for (int axis = 0; axis < 3; axis++)
    {
        vec3 direction = vec3(0.0);
        direction[axis] = 1.0;

        sum += GetLightProbeSample(origin, direction).Irradiance;
        sum += GetLightProbeSample(origin, -direction).Irradiance;
    }

    return max(sum / 6.0, vec3(0.0));
}

// The light of the surface in the gamma space of the screen, from the probes of the map where they cover the model
vec3 GetSurfaceLight(vec3 normal)
{
    vec3 origin = GetObjectLightingOrigin();
    LightProbeSample_t probe = GetLightProbeSample(origin, normal);

    if (probe.Coverage <= 0.0)
    {
        return GetStudioLight(normal);
    }

    return SrgbLinearToGamma(GetProbeLight(probe, origin));
}

void main()
{
    vec2 texCoord = vTexCoordOut;

    if (F_CHROME != 0)
    {
        // The reflection coordinates are in texels
        texCoord /= vec2(textureSize(g_tColor, 0));
    }

    vec4 texel = texture(g_tColor, texCoord);

    if (F_ALPHA_TEST != 0 && texel.a <= 0.5)
    {
        discard;
    }

    // Blending happens in gamma space, so the light is applied there and the result converted for the linear pipeline
    // The entity tints and fades the model
    vec4 objectTint = vTintOut;
    vec3 light = vec3(1.0);

    if (F_UNLIT == 0)
    {
        light = GetSurfaceLight(normalize(vNormalOut));
    }

    vec3 shaded = texel.rgb * light * objectTint.rgb;

    vec3 color = SrgbGammaToLinear(saturate(shaded));

    if (F_ADDITIVE_BLEND != 0)
    {
        color *= GetFogAttenuation(vFragPosition);
    }
    else
    {
        ApplyFog(color, vFragPosition);
    }

    outputColor = vec4(color, objectTint.a);
}
