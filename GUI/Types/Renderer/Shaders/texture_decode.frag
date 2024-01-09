#version 460

#define TYPE_TEXTURE2D 0
#define TYPE_TEXTURE2DARRAY 1

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

#define HemiOctIsoRoughness_RG_B 0
#define YCoCg_Conversion 0

#if HemiOctIsoRoughness_RG_B == 1
    vec3 PackToColor( vec3 vValue )
    {
        return ( ( vValue.xyz * 0.5 ) + 0.5 );
    }

    vec3 oct_to_float32x3(vec2 e)
    {
        vec3 v = vec3(e.xy, 1.0 - abs(e.x) - abs(e.y));
        return normalize(v);
    }

    // Unpack HemiOct normal map
    vec3 DecodeNormal(vec4 bumpNormal)
    {
        //Reconstruct the tangent vector from the map
        vec2 temp = vec2(bumpNormal.x + bumpNormal.y - 1.003922, bumpNormal.x - bumpNormal.y);
        vec3 tangentNormal = oct_to_float32x3(temp);

        return tangentNormal;
    }
#endif

#if YCoCg_Conversion == 1
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
#endif

uniform bool g_bTextureViewer = false;
uniform vec2 g_vViewportSize;
uniform float g_fZoomScale = 1.0;

vec2 AdjustTextureViewerUvs(vec2 vTexCoord)
{
    vTexCoord.xy = vTexCoord.xy * (g_vViewportSize.xy / g_vInputTextureSize.xy);
    vTexCoord.xy /= g_fZoomScale;

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

    if (g_bTextureViewer)
    {
        vScreenCoords.y = 1.0 - vScreenCoords.y;

        vBackgroundColor = CheckerboardPattern(vScreenCoords);
        vTexCoord.xy = AdjustTextureViewerUvs(vScreenCoords);
    }

    vec4 vColor = textureLod(g_tInputTexture, vTexCoord, float(g_nSelectedMip) / g_vInputTextureSize.w);

    #if HemiOctIsoRoughness_RG_B == 1
        float flRoughness = vColor.b;
        vColor.rgb = PackToColor(oct_to_float32x3(vec2(vColor.x + vColor.y - 1.003922, vColor.x - vColor.y)));
        vColor.a = flRoughness;
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

        if (vTexCoord.x <= 1.0 && vTexCoord.y <= 1.0)
            flBackgroundMix -= vColorOutput.a;

        vColorOutput.rgb = mix(vColorOutput.rgb, vBackgroundColor, flBackgroundMix);
    }
}
