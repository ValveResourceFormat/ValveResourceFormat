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

uniform uint g_nChannelMapping = 0x03020100; // RGBA

uint GetColorIndex(uint nChannelMapping, uint nChannel)
{
    return (nChannelMapping >> (nChannel * 8)) & 0xff;
}

#define HemiOctIsoRoughness_RG_B 0

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

layout(location = 0) out vec4 vColorOutput;

void main()
{
    vec2 vTexCoord2D = gl_FragCoord.xy / g_vInputTextureSize.xy;

    #if TYPE_TEXTURE2D == 1
        vec2 vTexCoord = vTexCoord2D;
    #elif TYPE_TEXTURE2DARRAY == 1
        vec3 vTexCoord = vec3(vTexCoord2D, g_nSelectedDepth);
    #endif

    vec4 vColor = textureLod(g_tInputTexture, vTexCoord, float(g_nSelectedMip) / g_vInputTextureSize.w);

    #if HemiOctIsoRoughness_RG_B == 1
        float flRoughness = vColor.b;
        vColor.rgb = PackToColor(oct_to_float32x3(vec2(vColor.x + vColor.y - 1.003922, vColor.x - vColor.y)));
        vColor.a = flRoughness;
    #endif

    uvec4 vRemapIndices = uvec4(
        GetColorIndex(g_nChannelMapping, 0),
        GetColorIndex(g_nChannelMapping, 1),
        GetColorIndex(g_nChannelMapping, 2),
        GetColorIndex(g_nChannelMapping, 3)
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
}
