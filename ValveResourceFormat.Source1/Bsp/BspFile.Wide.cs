using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Bsp;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct LeafAmbientIndexWide
{
    public readonly uint AmbientSampleCount;
    public readonly uint FirstAmbientSample;
}

// Maps with 32-bit indices keep the layout of the other lumps but store most indices and counts as 32-bit values
internal sealed partial class BspFile
{
    internal const int WideLumpVersion = 1;
    internal const int WideFaceLumpVersion = 2;
    internal const int WideLeafLumpVersion = 2;

    private static BspFace[] ConvertFaces(ReadOnlySpan<byte> span)
    {
        var wideFaces = AsItems<BspFaceWide>(span);
        var result = new BspFace[wideFaces.Length];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new BspFace(wideFaces[i]);
        }

        return result;
    }

    private BspBrushSide[] ReadBrushSides()
    {
        if (!IsWide)
        {
            return ReadLump<BspBrushSide>(LumpType.BrushSides);
        }

        var wideSides = ReadLumpItems<BspBrushSideWide>(LumpType.BrushSides, WideLumpVersion);
        var result = new BspBrushSide[wideSides.Length];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new BspBrushSide(wideSides[i]);
        }

        return result;
    }

    private BspEdge[] ReadEdges()
    {
        if (IsWide)
        {
            return ReadLump<BspEdge>(LumpType.Edges, WideLumpVersion);
        }

        var narrowEdges = ReadLumpItems<BspEdge16>(LumpType.Edges);
        var result = new BspEdge[narrowEdges.Length];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new BspEdge(narrowEdges[i].A, narrowEdges[i].B);
        }

        return result;
    }

    private int[] ReadIndices(LumpType type)
    {
        if (IsWide)
        {
            return ReadLump<int>(type, WideLumpVersion);
        }

        var narrowIndices = ReadLumpItems<ushort>(type);
        var result = new int[narrowIndices.Length];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = narrowIndices[i];
        }

        return result;
    }

    private BspPrimitive[] ReadPrimitives()
    {
        if (IsWide)
        {
            return ReadWidePrimitives();
        }

        return ReadNarrowPrimitives();
    }

    private BspPrimitive[] ReadWidePrimitives()
    {
        var widePrimitives = ReadLumpItems<BspPrimitive32>(LumpType.Primitives, WideLumpVersion);
        var result = new BspPrimitive[widePrimitives.Length];

        for (var i = 0; i < result.Length; i++)
        {
            var primitive = widePrimitives[i];
            result[i] = new BspPrimitive((PrimitiveType)primitive.Type, primitive.FirstIndex, primitive.IndexCount, primitive.FirstVert, primitive.VertCount);
        }

        return result;
    }

    private BspPrimitive[] ReadNarrowPrimitives()
    {
        var narrowPrimitives = ReadLumpItems<BspPrimitive16>(LumpType.Primitives);
        var result = new BspPrimitive[narrowPrimitives.Length];

        for (var i = 0; i < result.Length; i++)
        {
            var primitive = narrowPrimitives[i];
            result[i] = new BspPrimitive(primitive.Type, primitive.FirstIndex, primitive.IndexCount, primitive.FirstVert, primitive.VertCount);
        }

        return result;
    }

    private BspLeaf[] ReadLeafs()
    {
        if (IsWide)
        {
            return BspLeaf.ReadWide(GetVersionedLumpData(LumpType.Leafs, WideLeafLumpVersion).Span);
        }

        var hasAmbientCube = GetLumpInfo(LumpType.Leafs).Version == 0 && !HasLegacyLumps;
        return BspLeaf.Read(hasAmbientCube, GetVersionedLumpData(LumpType.Leafs, 0, 1).Span);
    }

    private BspDispInfo[] ReadWideDisplacementInfos()
    {
        var infos = ReadLumpItems<BspDispInfoWide>(LumpType.DispInfo, WideLumpVersion);
        var result = new BspDispInfo[infos.Length];

        for (var i = 0; i < infos.Length; i++)
        {
            result[i] = new BspDispInfo(infos[i]);
        }

        return result;
    }

    private LeafAmbientIndex[] ReadLeafAmbientIndexLump(LumpType type)
    {
        if (!IsWide)
        {
            return ReadLump<LeafAmbientIndex>(type);
        }

        var wideIndices = ReadLumpItems<LeafAmbientIndexWide>(type, WideLumpVersion);
        var result = new LeafAmbientIndex[wideIndices.Length];

        for (var i = 0; i < result.Length; i++)
        {
            var index = wideIndices[i];
            result[i] = new LeafAmbientIndex(BspWide.ToUInt16((int)index.AmbientSampleCount), BspWide.ToUInt16((int)index.FirstAmbientSample));
        }

        return result;
    }
}
