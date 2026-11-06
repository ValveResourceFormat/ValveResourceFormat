#version 460

#include "../common/utils.slang"
#include "../common/view_constants.slang"
#include "../common/fog.slang"
#include "../common/lighting_constants.slang"
#include "gamma.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;
in vec2 vLightmapUVOut;

out vec4 outputColor;

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture
uniform sampler2D g_tLightmap;
// The frames of an animated texture, one layer each
uniform sampler2DArray g_tColorFrames; // SrgbRead(true) Sampler(UserConfig)

uniform int F_ALPHA_TEST;
uniform int F_ANIMATED;
uniform int F_TRANSLUCENT;
uniform int F_ADDITIVE_BLEND;
uniform int F_WATER;

// 0 multiplies the texture by the lightmap, 1 draws the texture as it is, 2 draws the render color alone
uniform int g_nSurfaceMode;
uniform vec2 g_vTextureSize = vec2(64.0);
// The render color of the entity in linear space, and its opacity
uniform vec4 g_vRenderColor = vec4(1.0);
// The alpha test discards texels whose alpha is not above this
uniform float g_flAlphaReference = 0.0;
// How fast the texture moves along its horizontal axis, in texels per second
uniform float g_flTextureScroll = 0.0;

// The number of layers of the animated texture
uniform int g_nFrameCount = 1;

const float FramesPerSecond = 10.0;

// Water textures are looked up in units of 64 texels, whatever size they have
const vec2 WaterTextureSize = vec2(64.0);
const float WaterWarpAmplitude = 8.0;
const float WaterWarpFrequency = 0.125;

// The light of one luxel as the engine draws it: the sum curved and doubled
vec3 FetchLuxel(ivec2 luxel)
{
    vec3 light = texelFetch(g_tLightmap, luxel, 0).rgb;

    return SrgbGammaToLinear(GoldSrcLightGamma(light) * 2.0);
}

// The luxels are lit before they are filtered, as the engine filters the lightmap it builds
vec3 SampleLightmap(vec2 uv)
{
    ivec2 size = textureSize(g_tLightmap, 0);
    vec2 position = uv * vec2(size) - 0.5;
    vec2 fraction = fract(position);
    ivec2 first = ivec2(floor(position));
    ivec2 last = first + 1;
    ivec2 maxLuxel = size - 1;

    first = clamp(first, ivec2(0), maxLuxel);
    last = clamp(last, ivec2(0), maxLuxel);

    vec3 top = mix(FetchLuxel(first), FetchLuxel(ivec2(last.x, first.y)), fraction.x);
    vec3 bottom = mix(FetchLuxel(ivec2(first.x, last.y)), FetchLuxel(last), fraction.x);

    return mix(top, bottom, fraction.y);
}

void main()
{
    vec2 texelCoord = vTexCoordOut * g_vTextureSize;
    vec2 lookupSize = g_vTextureSize;

    texelCoord.x += g_flTextureScroll * g_flTime;

    if (F_WATER != 0)
    {
        // The sine is evaluated exactly instead of being quantized to 256 steps
        texelCoord += WaterWarpAmplitude * sin(texelCoord.yx * WaterWarpFrequency + g_flTime);
        lookupSize = WaterTextureSize;
    }

    vec4 baseColor;

    if (F_ANIMATED != 0)
    {
        float layer = float(int(g_flTime * FramesPerSecond) % g_nFrameCount);
        baseColor = texture(g_tColorFrames, vec3(texelCoord / lookupSize, layer));
    }
    else
    {
        baseColor = texture(g_tColor, texelCoord / lookupSize);
    }
    vec3 color = baseColor.rgb;
    float alpha = 1.0;

    if (g_nSurfaceMode == 0)
    {
        color *= SampleLightmap(vLightmapUVOut);
    }
    else if (g_nSurfaceMode == 2)
    {
        color = vec3(1.0);
    }

    color *= g_vRenderColor.rgb;

    if (F_ALPHA_TEST != 0 && baseColor.a <= g_flAlphaReference)
    {
        discard;
    }

    if (F_TRANSLUCENT != 0)
    {
        alpha *= g_vRenderColor.a * baseColor.a;
    }

    if (F_ADDITIVE_BLEND != 0)
    {
        color *= GetFogAttenuation(vFragPosition);
    }
    else
    {
        ApplyFog(color, vFragPosition);
    }

    outputColor = vec4(color, alpha);
}
