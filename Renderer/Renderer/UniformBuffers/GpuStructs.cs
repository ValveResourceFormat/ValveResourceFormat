using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Buffers;

[StructLayout(LayoutKind.Sequential)]
public struct MeshletCullInfo
{
    public Meshlet.MeshletBounds Bounds { get; init; }
    public Meshlet.MeshletCone Cone { get; init; }
    public uint ParentDrawBoundsIndex;
}

[StructLayout(LayoutKind.Sequential)]
public struct DrawBounds
{
    public Vector3 Min;
    public uint _Padding1;
    public Vector3 Max;
    public uint _Padding2;
};

[StructLayout(LayoutKind.Sequential)]
public struct OccludedBoundDebug
{
    public Vector3 Min;
    public float _Padding1;
    public Vector3 Max;
    public float _Padding2;
};

[StructLayout(LayoutKind.Sequential)]
public struct ObjectDataStandard
{
    public uint TintAlpha;
    public uint TransformIndex;
    public uint VisibleLPV;
    public uint Identification;
    public SceneEnvMap.EnvMapVisibility128 EnvMapVisibility;
};


[StructLayout(LayoutKind.Sequential)]
public readonly struct DrawElementsIndirectCommand
{
    public readonly uint Count { get; init; }
    public readonly uint InstanceCount { get; init; }
    public readonly uint FirstIndex { get; init; }
    public readonly int BaseVertex { get; init; }
    public readonly uint BaseInstance { get; init; }
};
