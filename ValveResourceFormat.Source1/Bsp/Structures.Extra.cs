using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>A water volume of a leaf (LeafWaterData lump).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspLeafWaterData
{
    public readonly float SurfaceZ;
    public readonly float MinZ;
    public readonly short SurfaceTexInfo;
    private readonly short padding;
}

/// <summary>An occluder of the Occlusion lump (lump version 2).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspOccluder
{
    public readonly int Flags;
    public readonly int FirstPoly;
    public readonly int PolyCount;
    public readonly Vector3 Min;
    public readonly Vector3 Max;
    public readonly int Area;
}

/// <summary>A polygon of an occluder. Its corners are <see cref="BspOcclusion.VertexIndices"/> entries that index <see cref="BspFile.Vertices"/>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspOccluderPoly
{
    public readonly int FirstVertexIndex;
    public readonly int VertexCount;
    public readonly int PlaneNum;
}

/// <summary>The contents of the Occlusion lump.</summary>
internal sealed class BspOcclusion(BspOccluder[] occluders, BspOccluderPoly[] polys, int[] vertexIndices)
{
    public BspOccluder[] Occluders { get; } = occluders;
    public BspOccluderPoly[] Polys { get; } = polys;
    public int[] VertexIndices { get; } = vertexIndices;
}

/// <summary>An area of the map and the range of its portals in the AreaPortals lump.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspArea
{
    public readonly int PortalCount;
    public readonly int FirstPortal;
}

/// <summary>A portal between two areas. Its polygon is a range of the ClipPortalVerts lump.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspAreaPortal
{
    public readonly ushort PortalKey;
    public readonly ushort OtherArea;
    public readonly ushort FirstClipVertex;
    public readonly ushort ClipVertexCount;
    public readonly int PlaneNum;
}

/// <summary>A light of the WorldLights lumps, in the layout of lump version 0 (88 bytes).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspWorldLightV0
{
    public readonly Vector3 Origin;
    public readonly Vector3 Intensity;
    public readonly Vector3 Normal;
    public readonly int Cluster;
    public readonly int EmitType;
    public readonly int Style;
    public readonly float StopDot;
    public readonly float StopDot2;
    public readonly float Exponent;
    public readonly float Radius;
    public readonly float ConstantAttenuation;
    public readonly float LinearAttenuation;
    public readonly float QuadraticAttenuation;
    public readonly int Flags;
    public readonly int TexInfo;
    public readonly int Owner;
}

/// <summary>A light of the WorldLights lumps, in the layout of lump version 1 (100 bytes).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspWorldLight
{
    public readonly Vector3 Origin;
    public readonly Vector3 Intensity;
    public readonly Vector3 Normal;
    public readonly Vector3 ShadowCastOffset;
    public readonly int Cluster;
    public readonly int EmitType;
    public readonly int Style;
    public readonly float StopDot;
    public readonly float StopDot2;
    public readonly float Exponent;
    public readonly float Radius;
    public readonly float ConstantAttenuation;
    public readonly float LinearAttenuation;
    public readonly float QuadraticAttenuation;
    public readonly int Flags;
    public readonly int TexInfo;
    public readonly int Owner;

    public BspWorldLight(in BspWorldLightV0 light)
    {
        Origin = light.Origin;
        Intensity = light.Intensity;
        Normal = light.Normal;
        ShadowCastOffset = Vector3.Zero;
        Cluster = light.Cluster;
        EmitType = light.EmitType;
        Style = light.Style;
        StopDot = light.StopDot;
        StopDot2 = light.StopDot2;
        Exponent = light.Exponent;
        Radius = light.Radius;
        ConstantAttenuation = light.ConstantAttenuation;
        LinearAttenuation = light.LinearAttenuation;
        QuadraticAttenuation = light.QuadraticAttenuation;
        Flags = light.Flags;
        TexInfo = light.TexInfo;
        Owner = light.Owner;
    }
}

/// <summary>A displacement vertex record of the DispMultiblend lump.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct BspDispMultiblend
{
    public readonly Vector4 Multiblend;
    public readonly Vector4 AlphaClone;
    public readonly Vector3 Color0;
    public readonly Vector3 Color1;
    public readonly Vector3 Color2;
    public readonly Vector3 Color3;
}
