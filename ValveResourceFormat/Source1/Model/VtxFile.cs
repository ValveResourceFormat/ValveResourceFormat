using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Model;

internal sealed partial class VtxFile
{
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
            throw new InvalidDataException($"Unsupported VTX version {Version}.");
        }

        reader.ReadInt32(); // vertCacheSize
        reader.ReadUInt16(); // maxBonesPerStrip
        reader.ReadUInt16(); // maxBonesPerTri
        reader.ReadInt32(); // maxBonesPerVert

        Checksum = reader.ReadInt32();
        NumLods = Math.Min(reader.ReadInt32(), vvd.NumLods);
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
        var stripGroupSize = Marshal.SizeOf<StripGroupHeader>();

        Meshes = new MeshData[numBodyParts][][][];

        for (var partIndex = 0; partIndex < numBodyParts; partIndex++)
        {
            var partStart = bodyPartOffset + ((long)partIndex * bodyPartSize);
            var modelHeaders = LumpReader<ModelHeader>.ReadLumpAt(stream, partStart + bodyPartHeaders[partIndex].ModelOffset, bodyPartHeaders[partIndex].NumModels);
            var modelsStart = partStart + bodyPartHeaders[partIndex].ModelOffset;

            Meshes[partIndex] = new MeshData[modelHeaders.Length][][];

            for (var modelIndex = 0; modelIndex < modelHeaders.Length; modelIndex++)
            {
                var modelStart = modelsStart + ((long)modelIndex * modelSize);
                var lodsStart = modelStart + modelHeaders[modelIndex].LodOffset;
                var lodHeaders = LumpReader<ModelLodHeader>.ReadLumpAt(stream, lodsStart, modelHeaders[modelIndex].NumLods);
                var mdlMeshes = mdl.Meshes[partIndex][modelIndex];

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
                        var vertexCount = meshIndex < mdlMeshes.Length ? mdlMeshes[meshIndex].VertexData._numLodVertices[lodIndex] : 0;
                        var indices = new List<int>();

                        var groupsStart = meshStart + meshHeaders[meshIndex].StripGroupHeaderOffset;
                        var groups = LumpReader<StripGroupHeader>.ReadLumpAt(stream, groupsStart, meshHeaders[meshIndex].NumStripGroups);

                        var groupVertexIds = new int[groups.Length][];

                        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                        {
                            groupVertexIds[groupIndex] = ReadStripGroup(stream, groupsStart + ((long)groupIndex * stripGroupSize), groups[groupIndex], indices);
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

    private static int[] ReadStripGroup(Stream stream, long start, StripGroupHeader group, List<int> output)
    {
        var vertices = LumpReader<OptimizedVertex>.ReadLumpAt(stream, start + group.VertOffset, group.NumVerts);
        var vertexIds = new int[vertices.Length];

        for (var i = 0; i < vertexIds.Length; i++)
        {
            vertexIds[i] = vertices[i].OrigMeshVertId;
        }

        var indices = LumpReader<ushort>.ReadLumpAt(stream, start + group.IndexOffset, group.NumIndices);
        var stripsStart = start + group.StripOffset;
        var strips = LumpReader<StripHeader>.ReadLumpAt(stream, stripsStart, group.NumStrips);

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
