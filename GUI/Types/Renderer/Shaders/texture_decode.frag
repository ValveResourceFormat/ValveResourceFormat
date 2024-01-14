#version 460

#define TYPE_TEXTURE2D 0
#define TYPE_TEXTURE2DARRAY 0
#define TYPE_TEXTURECUBEMAP 0
#define TYPE_TEXTURECUBEMAPARRAY 0

#if TYPE_TEXTURE2D == 1
    #define TEXTURE_TYPE sampler2D
#elif TYPE_TEXTURE2DARRAY == 1
    #define TEXTURE_TYPE sampler2DArray
#elif TYPE_TEXTURECUBEMAP == 1
    #define TEXTURE_TYPE samplerCube
#elif TYPE_TEXTURECUBEMAPARRAY == 1
    #define TEXTURE_TYPE samplerCubeArray
#else
    #error "Missing TEXTURE_TYPE"
#endif

uniform TEXTURE_TYPE g_tInputTexture;
uniform vec4 g_vInputTextureSize;

#include "common/utils.glsl"

#define YCoCg_Conversion (1 << 0)
#define RGBM (1 << 1)
#define HemiOctIsoRoughness_RG_B (1 << 2)
#define NormalizeNormals (1 << 3)
#define Dxt5nm_AlphaGreen (1 << 4)
#define ColorSpace_Linear (1 << 5)
#define ColorSpace_Srgb (1 << 6)

uniform int g_nSelectedMip;
uniform int g_nSelectedDepth;
uniform int g_nSelectedCubeFace;
uniform int g_nDecodeFlags;
uniform uint g_nSelectedChannels = 0x03020100; // RGBA

uint GetColorIndex(uint nChannelMapping, uint nChannel)
{
    return (nChannelMapping >> (nChannel * 8)) & 0xff;
}

vec3 GetCubemapFaceCoords(vec2 vTexCoord, int nFace)
{
    vec3 vFaceCoord = vec3(0.0);
    vec2 vMapCoord = 2 * vTexCoord - 1;

    if (nFace == 0) // +X
    {
        vFaceCoord = vec3(1.0, -vMapCoord.y, -vMapCoord.x);
    }
    else if (nFace == 1) // -X
    {
        vFaceCoord = vec3(-1.0, -vMapCoord.y, vMapCoord.x);
    }
    else if (nFace == 2) // +Y
    {
        vFaceCoord = vec3(vMapCoord.x, 1.0, vMapCoord.y);
    }
    else if (nFace == 3) // -Y
    {
        vFaceCoord = vec3(vMapCoord.x, -1.0, -vMapCoord.y);
    }
    else if (nFace == 4) // +Z
    {
        vFaceCoord = vec3(vMapCoord.x, -vMapCoord.y, 1.0);
    }
    else if (nFace == 5) // -Z
    {
        vFaceCoord = vec3(-vMapCoord.x, -vMapCoord.y, -1.0);
    }

    return vFaceCoord;
}

vec3 EquirectangularProjection(vec2 vTexCoord)
{
    vec2 vMapCoord = -2 * vTexCoord.xy - 1;
    vec2 thetaphi = vMapCoord * vec2(PI, PI * 0.5) + vec2(0, 0);
    vec3 rayDirection = vec3(cos(thetaphi.y) * cos(thetaphi.x), cos(thetaphi.y) * sin(thetaphi.x), -sin(thetaphi.y));

    return rayDirection;
}

uniform bool g_bTextureViewer = false;
uniform vec2 g_vViewportSize;
uniform vec2 g_vViewportPosition;
uniform float g_flScale = 1.0;
uniform bool g_bWantsSeparateAlpha = false;
uniform int g_nCubemapProjectionType;

#define g_bCubeEquiRectangularProjection (g_nCubemapProjectionType == 1)

vec2 AdjustTextureViewerUvs(vec2 vTexCoord)
{
    vTexCoord += g_vViewportPosition / g_vViewportSize;

    vTexCoord.xy *= g_vViewportSize / g_vInputTextureSize.xy;
    vTexCoord.xy /= g_flScale;

    return vTexCoord;
}

vec2 GetImageDimensionsMultiplier()
{
    //if (g_bWantsSeparateAlpha)
    //    return vec2(2.0, 1.0);

    if (g_bCubeEquiRectangularProjection)
        return vec2(4.0, 2.0);

    return vec2(1.0);
}

vec3 CheckerboardPattern(vec2 vScreenCoords)
{
    const vec3 color1 = vec3(0.9, 0.9, 0.9);
    const vec3 color2 = vec3(0.6, 0.6, 0.6);

    const vec2 vSizeInPixels = vec2(32);

    vec2 vTexCoord = vScreenCoords * g_vViewportSize / vSizeInPixels;
    vec2 vCell = floor(vTexCoord);
    vec2 vFrac = fract(vTexCoord);

    vec3 vColor = mix(color1, color2, mod(vCell.x + vCell.y, 2.0));

    return vColor;
}

layout(location = 0) out vec4 vColorOutput;

void main()
{
    vec2 vScreenCoords = gl_FragCoord.xy / g_vViewportSize.xy;

    #if TYPE_TEXTURE2D == 1 || TYPE_TEXTURECUBEMAP == 1
        vec2 vTexCoord = vScreenCoords;
    #elif TYPE_TEXTURE2DARRAY == 1 || TYPE_TEXTURECUBEMAPARRAY == 1
        vec3 vTexCoord = vec3(vScreenCoords, g_nSelectedDepth);
    #else
        #error "Missing vTexCoord for TYPE_xxxx"
    #endif

    vec3 vBackgroundColor = vec3(0.0);
    bool bWithinAlphaBounds = false;

    if (g_bTextureViewer)
    {
        vScreenCoords.y = 1.0 - vScreenCoords.y;

        vBackgroundColor = CheckerboardPattern(vScreenCoords);
        vTexCoord.xy = AdjustTextureViewerUvs(vScreenCoords);

        bool bIsWideImage = g_vInputTextureSize.x > g_vInputTextureSize.y;
        vec2 vAlphaRegionTexCoord = bIsWideImage ? vTexCoord.yx : vTexCoord.xy;

        bWithinAlphaBounds = vAlphaRegionTexCoord.x >= 1.0 && vAlphaRegionTexCoord.x < 2.0 && vAlphaRegionTexCoord.x >= 0.0 && vAlphaRegionTexCoord.y < 1.0;

        if (g_bWantsSeparateAlpha && bWithinAlphaBounds)
        {
            vAlphaRegionTexCoord.x -= 1.0;
            vTexCoord.xy = bIsWideImage ? vAlphaRegionTexCoord.yx : vAlphaRegionTexCoord.xy;
        }
    }

    // cubemaps take a direction vector as sample coords
    #if (TYPE_TEXTURECUBEMAP == 1) || (TYPE_TEXTURECUBEMAPARRAY == 1)
        #if TYPE_TEXTURECUBEMAP == 1
            vec3 vSampleCoords;
        #elif TYPE_TEXTURECUBEMAPARRAY == 1
            vec4 vSampleCoords = vec4(0, 0, 0, vTexCoord.z);
        #endif

        if (g_bCubeEquiRectangularProjection)
        {
            vTexCoord.xy /= GetImageDimensionsMultiplier();
            vSampleCoords.xyz = EquirectangularProjection(vTexCoord.xy);
        }
        else
        {
            vSampleCoords.xyz = GetCubemapFaceCoords(vTexCoord.xy, g_nSelectedCubeFace);
        }
    #else
        #define vSampleCoords vTexCoord
    #endif

    vec4 vColor = textureLod(g_tInputTexture, vSampleCoords, float(g_nSelectedMip));

    // similar to a channel mapping value of 0x00020103
    if ((g_nDecodeFlags & Dxt5nm_AlphaGreen) != 0)
    {
        vColor.rgba = vColor.agbr;
    }

    if ((g_nDecodeFlags & HemiOctIsoRoughness_RG_B) != 0)
    {
        float flRoughness = vColor.b;
        vColor.rgb = PackToColor(oct_to_float32x3(vec2(vColor.x + vColor.y - 1.003922, vColor.x - vColor.y)));
        vColor.a = flRoughness;
    }

    if ((g_nDecodeFlags & NormalizeNormals) != 0)
    {
        vec2 normalXy = (vColor.rg) * 2.0 - 1.0;
        float derivedZ = sqrt(1.0 - (normalXy.x * normalXy.x) - (normalXy.y * normalXy.y));
        derivedZ = mix(derivedZ, 1.0, isnan(derivedZ)); // todo: becomes NaN if we are out of bounds
        vColor.rgb = PackToColor(vec3(normalXy.xy, isnan(derivedZ) ? 1.0 : derivedZ));
    }

    if ((g_nDecodeFlags & YCoCg_Conversion) != 0)
    {
        vColor.rgb = DecodeYCoCg(vColor);
        vColor.a = 1.0;
    }

    if ((g_nDecodeFlags & ColorSpace_Srgb) != 0)
    {
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }

    if ((g_nDecodeFlags & ColorSpace_Linear) != 0)
    {
        vColor.rgb = SrgbLinearToGamma(vColor.rgb);
    }

    uvec4 vRemapIndices = uvec4(
        GetColorIndex(g_nSelectedChannels, 0),
        GetColorIndex(g_nSelectedChannels, 1),
        GetColorIndex(g_nSelectedChannels, 2),
        GetColorIndex(g_nSelectedChannels, 3)
    );

    // Single channel texture write to RGB
    bool bSingleChannel = (vRemapIndices.yzw == uvec3(0xFF));
    if (bSingleChannel)
    {
        vColorOutput = vec4(vec3(vColor[vRemapIndices.x]), 1.0);
    }
    else
    {
        bvec4 bWriteMask = bvec4(vRemapIndices.x != 0xFF, vRemapIndices.y != 0xFF, vRemapIndices.z != 0xFF, vRemapIndices.w != 0xFF);
        vec4 vRemappedColor = vec4(vColor[vRemapIndices.x], vColor[vRemapIndices.y], vColor[vRemapIndices.z], vColor[vRemapIndices.w]);

        vColorOutput = mix(vec4(0, 0, 0, 1), vRemappedColor, bWriteMask);
        //vColorOutput = vec4(vColorOutput.rgb, 1.0);
    }

    if (g_bTextureViewer)
    {
        float flBackgroundMix = 1.0;
        bool bWithinImageBounds = vTexCoord.x < 1.0 && vTexCoord.y < 1.0 && vTexCoord.x >= 0.0 && vTexCoord.y >= 0.0;

        if (g_bWantsSeparateAlpha && (bWithinImageBounds || bWithinAlphaBounds))
        {
            if (bWithinAlphaBounds)
            {
                vColorOutput.rgb = vColorOutput.aaa;
            }

            vColorOutput.a = 1.0;
        }

        if (bWithinImageBounds)
        {
            flBackgroundMix -= vColorOutput.a;
        }

        vColorOutput.rgb = mix(vColorOutput.rgb, vBackgroundColor, flBackgroundMix);
    }
}
