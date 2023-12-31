#version 460

uniform sampler2D g_tInputTexture;
uniform vec4 g_vInputTextureSize

uniform int g_nSelectedMip;
uniform int g_nSelectedDepth;

uniform uint g_nChannelMapping = 0x03020100; // RGBA

uint GetColorIndex(uint nChannelMapping, uint nChannel)
{
    return (nChannelMapping >> (nChannel * 8)) & 0xff;
}

layout(location = 0) out vec4 vColorOutput;

void main()
{
    vec2 vTexCoord = gl_FragCoord.xy / g_vInputTextureSize.xy;

    vec4 vColor = texture2DLod(g_tInputTexture, vTexCoord, g_nSelectedMip);

    #if defined(HemiOctIsoRoughness_RG_B)
        float flRoughness = vColor.b;
        vColor.rgb = DecodeNormal(vColor.rgb);
        vColor.a = flRoughness;
    #endif

    uvec4 vRemapIndices = uvec4(
        GetColorIndex(g_nChannelMapping, 0),
        GetColorIndex(g_nChannelMapping, 1),
        GetColorIndex(g_nChannelMapping, 2),
        GetColorIndex(g_nChannelMapping, 3)
    );

    bvec4 bWriteMask = bvec4(vRemapIndices != uvec4(0xFF));

    vec4 vRemappedColor = vec4(
        vColor[vRemapIndices.x],
        vColor[vRemapIndices.y],
        vColor[vRemapIndices.z],
        vColor[vRemapIndices.w]
    );

    vColorOutput = mix(vec4(0, 0, 0, 1), vRemappedColor, bWriteMask);
}
