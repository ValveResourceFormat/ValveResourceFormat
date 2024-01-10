#version 460

#define TYPE_TEXTURE2D 0
#define TYPE_TEXTURE2DARRAY 0

#if TYPE_TEXTURE2D == 1
    #define TEXTURE_TYPE sampler2D
#elif TYPE_TEXTURE2DARRAY == 1
    #define TEXTURE_TYPE sampler2DArray
#endif

uniform TEXTURE_TYPE g_tInputTexture;
uniform vec4 g_vInputTextureSize;

uniform int g_nSelectedMip;
uniform int g_nSelectedDepth;
uniform uint g_nSelectedChannels = 0x03020100; // RGBA

uint GetColorIndex(uint nChannelMapping, uint nChannel)
{
    return (nChannelMapping >> (nChannel * 8)) & 0xff;
}

#define YCoCg_Conversion 0
#define HemiOctIsoRoughness_RG_B 0
#define NormalizeNormals 0
#define DXT5nm 0

vec3 PackToColor( vec3 vValue )
{
    return ( ( vValue.xyz * 0.5 ) + 0.5 );
}

vec3 oct_to_float32x3(vec2 e)
{
    vec3 v = vec3(e.xy, 1.0 - abs(e.x) - abs(e.y));
    return normalize(v);
}

vec3 DecodeYCoCg(vec4 YCoCg)
{
    float scale = (YCoCg.z * (255.0 / 8.0)) + 1.0;
    float Co = (YCoCg.x + (-128.0 / 255.0)) / scale;
    float Cg = (YCoCg.y + (-128.0 / 255.0)) / scale;
    float Y = YCoCg.w;

    float R = Y + Co - Cg;
    float G = Y + Cg;
    float B = Y - Co - Cg;

    vec3 color = vec3(R, G, B);
    return color;
}


uniform bool g_bTextureViewer = false;
uniform vec2 g_vViewportSize;
uniform vec2 g_vViewportPosition;
uniform float g_flScale = 1.0;
uniform bool g_bWantsSeparateAlpha = false;

vec2 AdjustTextureViewerUvs(vec2 vTexCoord)
{
    vTexCoord += g_vViewportPosition / g_vViewportSize;

    vTexCoord.xy *= g_vViewportSize / g_vInputTextureSize.xy;
    vTexCoord.xy /= g_flScale;

    return vTexCoord;
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

    #if TYPE_TEXTURE2D == 1
        vec2 vTexCoord = vScreenCoords;
    #elif TYPE_TEXTURE2DARRAY == 1
        vec3 vTexCoord = vec3(vScreenCoords, g_nSelectedDepth);
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

        bWithinAlphaBounds = vAlphaRegionTexCoord.x > 1.0 && vAlphaRegionTexCoord.x <= 2.0 && vAlphaRegionTexCoord.y <= 1.0;

        if (g_bWantsSeparateAlpha && bWithinAlphaBounds)
        {
            vAlphaRegionTexCoord.x -= 1.0;
            vTexCoord.xy = bIsWideImage ? vAlphaRegionTexCoord.yx : vAlphaRegionTexCoord.xy;
        }
    }

    vec4 vColor = textureLod(g_tInputTexture, vTexCoord, float(g_nSelectedMip) / g_vInputTextureSize.w);

    #if DXT5nm == 1
        float flRed = vColor.r;
        vColor.r = vColor.a;
        vColor.a = flRed;
    #endif

    #if HemiOctIsoRoughness_RG_B == 1
        float flRoughness = vColor.b;
        vColor.rgb = PackToColor(oct_to_float32x3(vec2(vColor.x + vColor.y - 1.003922, vColor.x - vColor.y)));
        vColor.a = flRoughness;
    #endif

    #if NormalizeNormals == 1
        vec2 normalXy = (vColor.rg) * 2.0 - 1.0;
        float derivedZ = sqrt(1.0 - (normalXy.x * normalXy.x) - (normalXy.y * normalXy.y));
        vColor.rgb = vec3(normalXy.xy / 2 + 0.5, derivedZ);
        vColor.a = 1.0;
    #endif

    #if YCoCg_Conversion == 1
        vColor.rgb = DecodeYCoCg(vColor);
        vColor.a = 1.0;
    #endif

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
        bool bWithinImageBounds = vTexCoord.x <= 1.0 && vTexCoord.y <= 1.0 && vTexCoord.x >= 0.0 && vTexCoord.y >= 0.0;

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
