using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Model;

internal sealed partial class VtxFile
{
    /// <summary>Size of the topology index count and offset that end the strip groups and strips of some files.</summary>
    private const int TopologyFieldsSize = 2 * sizeof(int);

    public int Version { get; }
    public int Checksum { get; }
    public int NumLods { get; }
    public float[] LodSwitchPoints { get; }

    /// <summary>Indexed by body part, model, LOD, mesh.</summary>
    public MeshData[][][][] Meshes { get; }

    public VtxFile(Stream stream, MdlFile mdl, VvdFile vvd)
    {
        stream.Position = 0;

        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        Version = reader.ReadInt32();

        if (Version != 7)
        {
            throw new UnexpectedMagicException("Unsupported VTX version, supported version is 7", Version, nameof(Version));
        }

        reader.ReadInt32(); // vertCacheSize
        reader.ReadUInt16(); // maxBonesPerStrip
        reader.ReadUInt16(); // maxBonesPerTri
        reader.ReadInt32(); // maxBonesPerVert

        Checksum = reader.ReadInt32();
        NumLods = Math.Min(reader.ReadInt32(), vvd.NumLods);

        if (NumLods < 1)
        {
            throw new InvalidDataException("Invalid VTX LOD count.");
        }

        reader.ReadInt32(); // materialReplacementListOffset

        var numBodyParts = reader.ReadInt32();
        var bodyPartOffset = reader.ReadInt32();

        if (numBodyParts != mdl.BodyParts.Length)
        {
            throw new InvalidDataException("MDL and VTX body part counts do not match.");
        }

        LodSwitchPoints = new float[NumLods];

        var bodyPartHeaders = LumpReader<BodyPartHeader>.ReadLumpAt(stream, bodyPartOffset, numBodyParts);
        var bodyPartSize = Marshal.SizeOf<BodyPartHeader>();
        var modelSize = Marshal.SizeOf<ModelHeader>();
        var lodSize = Marshal.SizeOf<ModelLodHeader>();
        var meshSize = Marshal.SizeOf<MeshHeader>();
        var topologySize = DetectTopologySize(stream, bodyPartOffset, bodyPartHeaders);
        var stripGroupSize = Marshal.SizeOf<StripGroupHeader>() + topologySize;
        var stripSize = Marshal.SizeOf<StripHeader>() + topologySize;

        Meshes = new MeshData[numBodyParts][][][];

        for (var partIndex = 0; partIndex < numBodyParts; partIndex++)
        {
            var partStart = bodyPartOffset + ((long)partIndex * bodyPartSize);
            var modelHeaders = LumpReader<ModelHeader>.ReadLumpAt(stream, partStart + bodyPartHeaders[partIndex].ModelOffset, bodyPartHeaders[partIndex].NumModels);
            var modelsStart = partStart + bodyPartHeaders[partIndex].ModelOffset;

            if (modelHeaders.Length != mdl.Models[partIndex].Length)
            {
                throw new InvalidDataException("MDL and VTX model counts do not match.");
            }

            Meshes[partIndex] = new MeshData[modelHeaders.Length][][];

            for (var modelIndex = 0; modelIndex < modelHeaders.Length; modelIndex++)
            {
                var modelStart = modelsStart + ((long)modelIndex * modelSize);
                var lodsStart = modelStart + modelHeaders[modelIndex].LodOffset;
                var lodHeaders = LumpReader<ModelLodHeader>.ReadLumpAt(stream, lodsStart, modelHeaders[modelIndex].NumLods);
                var mdlMeshes = mdl.Meshes[partIndex][modelIndex];

                if (lodHeaders.Length < NumLods)
                {
                    throw new InvalidDataException("VTX model has fewer LODs than the file.");
                }

                Meshes[partIndex][modelIndex] = new MeshData[NumLods][];

                for (var lodIndex = 0; lodIndex < NumLods; lodIndex++)
                {
                    var lodHeader = lodHeaders[lodIndex];
                    var lodStart = lodsStart + ((long)lodIndex * lodSize);

                    if (mdlMeshes.Length > 0)
                    {
                        LodSwitchPoints[lodIndex] = lodHeader.SwitchPoint;
                    }

                    var meshHeaders = LumpReader<MeshHeader>.ReadLumpAt(stream, lodStart + lodHeader.MeshOffset, lodHeader.NumMeshes);
                    var meshesStart = lodStart + lodHeader.MeshOffset;
                    var lodMeshes = new MeshData[meshHeaders.Length];

                    for (var meshIndex = 0; meshIndex < meshHeaders.Length; meshIndex++)
                    {
                        var meshStart = meshesStart + ((long)meshIndex * meshSize);
                        var vertexCount = 0;

                        if (meshIndex < mdlMeshes.Length)
                        {
                            vertexCount = mdlMeshes[meshIndex].VertexData._numLodVertices[lodIndex];

                            if (vertexCount == 0)
                            {
                                vertexCount = mdlMeshes[meshIndex].NumVertices;
                            }
                        }

                        var indices = new List<int>();

                        var groupsStart = meshStart + meshHeaders[meshIndex].StripGroupHeaderOffset;
                        var groups = ReadStrided<StripGroupHeader>(stream, groupsStart, meshHeaders[meshIndex].NumStripGroups, stripGroupSize);

                        var groupVertexIds = new int[groups.Length][];

                        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                        {
                            groupVertexIds[groupIndex] = ReadStripGroup(stream, groupsStart + ((long)groupIndex * stripGroupSize), groups[groupIndex], stripSize, indices);
                        }

                        lodMeshes[meshIndex] = new MeshData
                        {
                            VertexCount = vertexCount,
                            Indices = indices.ToArray(),
                            StripGroupVertexIds = groupVertexIds,
                        };
                    }

                    Meshes[partIndex][modelIndex][lodIndex] = lodMeshes;
                }
            }
        }
    }

    /// <summary>
    /// Finds whether the strip groups and strips of the file end with topology fields. The strip group headers
    /// are laid out one after another, followed by the data of the first group, so the distance from the
    /// first header to the nearest data offset, divided by the number of headers, is the size of a header.
    /// </summary>
    private static int DetectTopologySize(Stream stream, long bodyPartOffset, BodyPartHeader[] bodyPartHeaders)
    {
        var bodyPartSize = Marshal.SizeOf<BodyPartHeader>();
        var modelSize = Marshal.SizeOf<ModelHeader>();
        var lodSize = Marshal.SizeOf<ModelLodHeader>();
        var meshSize = Marshal.SizeOf<MeshHeader>();
        var firstGroupStart = long.MaxValue;
        long groupCount = 0;
        var meshGroups = new List<(long Start, int Count)>();

        for (var partIndex = 0; partIndex < bodyPartHeaders.Length; partIndex++)
        {
            var partStart = bodyPartOffset + ((long)partIndex * bodyPartSize);
            var modelsStart = partStart + bodyPartHeaders[partIndex].ModelOffset;
            var modelHeaders = LumpReader<ModelHeader>.ReadLumpAt(stream, modelsStart, bodyPartHeaders[partIndex].NumModels);

            for (var modelIndex = 0; modelIndex < modelHeaders.Length; modelIndex++)
            {
                var modelStart = modelsStart + ((long)modelIndex * modelSize);
                var lodsStart = modelStart + modelHeaders[modelIndex].LodOffset;
                var lodHeaders = LumpReader<ModelLodHeader>.ReadLumpAt(stream, lodsStart, modelHeaders[modelIndex].NumLods);

                for (var lodIndex = 0; lodIndex < lodHeaders.Length; lodIndex++)
                {
                    var lodStart = lodsStart + ((long)lodIndex * lodSize);
                    var meshesStart = lodStart + lodHeaders[lodIndex].MeshOffset;
                    var meshHeaders = LumpReader<MeshHeader>.ReadLumpAt(stream, meshesStart, lodHeaders[lodIndex].NumMeshes);

                    for (var meshIndex = 0; meshIndex < meshHeaders.Length; meshIndex++)
                    {
                        if (meshHeaders[meshIndex].NumStripGroups <= 0)
                        {
                            continue;
                        }

                        var meshStart = meshesStart + ((long)meshIndex * meshSize);
                        var groupsStart = meshStart + meshHeaders[meshIndex].StripGroupHeaderOffset;

                        firstGroupStart = Math.Min(firstGroupStart, groupsStart);
                        groupCount += meshHeaders[meshIndex].NumStripGroups;
                        meshGroups.Add((groupsStart, meshHeaders[meshIndex].NumStripGroups));
                    }
                }
            }
        }

        if (groupCount == 0)
        {
            return 0;
        }

        var stripGroupSize = Marshal.SizeOf<StripGroupHeader>();

        foreach (var topologySize in (ReadOnlySpan<int>)[0, TopologyFieldsSize])
        {
            var headerSize = stripGroupSize + topologySize;
            var dataStart = FindLowestGroupData(stream, meshGroups, headerSize);

            if (dataStart - firstGroupStart == groupCount * headerSize)
            {
                return topologySize;
            }
        }

        throw new InvalidDataException("Cannot determine the VTX strip group layout.");
    }

    private static long FindLowestGroupData(Stream stream, List<(long Start, int Count)> meshGroups, int headerSize)
    {
        var stripGroupSize = Marshal.SizeOf<StripGroupHeader>();
        var lowest = long.MaxValue;

        foreach (var (start, count) in meshGroups)
        {
            for (var i = 0; i < count; i++)
            {
                var groupStart = start + ((long)i * headerSize);

                if (groupStart + stripGroupSize > stream.Length)
                {
                    return long.MaxValue;
                }

                var group = LumpReader<StripGroupHeader>.ReadLumpAt(stream, groupStart, 1)[0];

                if (group.NumStrips > 0)
                {
                    lowest = Math.Min(lowest, groupStart + group.StripOffset);
                }

                if (group.NumVerts > 0)
                {
                    lowest = Math.Min(lowest, groupStart + group.VertOffset);
                }

                if (group.NumIndices > 0)
                {
                    lowest = Math.Min(lowest, groupStart + group.IndexOffset);
                }
            }
        }

        return lowest;
    }

    /// <summary>Reads headers that are followed by padding, such as the topology fields of newer files.</summary>
    private static T[] ReadStrided<T>(Stream stream, long start, int count, int stride) where T : struct
    {
        if (count < 0 || count > stream.Length)
        {
            throw new InvalidDataException($"Invalid {typeof(T).Name} count {count}.");
        }

        var items = new T[count];

        for (var i = 0; i < items.Length; i++)
        {
            items[i] = LumpReader<T>.ReadLumpAt(stream, start + ((long)i * stride), 1)[0];
        }

        return items;
    }

    private static int[] ReadStripGroup(Stream stream, long start, StripGroupHeader group, int stripSize, List<int> output)
    {
        var vertices = LumpReader<OptimizedVertex>.ReadLumpAt(stream, start + group.VertOffset, group.NumVerts);
        var vertexIds = new int[vertices.Length];

        for (var i = 0; i < vertexIds.Length; i++)
        {
            vertexIds[i] = vertices[i].OrigMeshVertId;
        }

        var indices = LumpReader<ushort>.ReadLumpAt(stream, start + group.IndexOffset, group.NumIndices);

        foreach (var index in indices)
        {
            if (index >= vertices.Length)
            {
                throw new InvalidDataException("VTX strip group index is outside its vertices.");
            }
        }

        var stripsStart = start + group.StripOffset;
        var strips = ReadStrided<StripHeader>(stream, stripsStart, group.NumStrips, stripSize);

        foreach (var strip in strips)
        {
            if (strip.IndexOffset < 0 || strip.NumIndices < 0 || strip.IndexOffset > indices.Length - strip.NumIndices)
            {
                throw new InvalidDataException("VTX strip range is outside its strip group.");
            }

            if ((strip.Flags & StripHeaderFlags.IsTriStrip) != 0)
            {
                for (var i = 2; i < strip.NumIndices; i++)
                {
                    var a = indices[strip.IndexOffset + i - 2];
                    var b = indices[strip.IndexOffset + i - 1];
                    var c = indices[strip.IndexOffset + i];

                    if (a == b || b == c || a == c)
                    {
                        continue;
                    }

                    output.Add(vertices[(i & 1) == 0 ? a : b].OrigMeshVertId);
                    output.Add(vertices[(i & 1) == 0 ? b : a].OrigMeshVertId);
                    output.Add(vertices[c].OrigMeshVertId);
                }
            }
            else
            {
                for (var i = 0; i < strip.NumIndices; i++)
                {
                    output.Add(vertices[indices[strip.IndexOffset + i]].OrigMeshVertId);
                }
            }
        }

        return vertexIds;
    }
}
