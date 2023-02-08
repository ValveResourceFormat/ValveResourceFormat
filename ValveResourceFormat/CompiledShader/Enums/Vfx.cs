using System.Collections.Generic;

namespace ValveResourceFormat.CompiledShader;

public static class Vfx
{
    public static readonly Dictionary<int, string> Types = new()
    {
        {0, ""},
        {1, "float"},
        {2, "float2"},
        {3, "float3"},
        {4, "float4"},
        {5, "uint"},
        {6, "int2"},
        {7, "int3"},
        {8, "int4"},
        {9, "bool"},
        {14, "tex"},
        {15, "volumetex?"},
        {16, "cube"},
        {21, "buffer"},
        {23, "tex[]"},
        {32, "RWTexture2D<float4>"},
        {34, "RWTexture3D<float4>"},
        {40, "set?"},
    };
}
