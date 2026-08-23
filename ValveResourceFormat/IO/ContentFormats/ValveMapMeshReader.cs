using System.Linq;

namespace ValveResourceFormat.IO.ContentFormats.ValveMap;

/// <summary>One renderable vertex extracted from a Hammer-authored map mesh.</summary>
/// <param name="Position">Vertex position in mesh-local space.</param>
/// <param name="Normal">Vertex normal.</param>
/// <param name="Tangent">Vertex tangent and handedness.</param>
/// <param name="TexCoord">Primary texture coordinate.</param>
public readonly record struct ValveMapMeshVertex(Vector3 Position, Vector3 Normal, Vector4 Tangent, Vector2 TexCoord);

/// <summary>A material-homogeneous triangle range extracted from a Hammer map mesh.</summary>
/// <param name="MaterialName">Material assigned to the range.</param>
/// <param name="Vertices">Vertices used by the triangles.</param>
/// <param name="Indices">Triangle indices.</param>
public sealed record ValveMapMeshPart(string MaterialName, IReadOnlyList<ValveMapMeshVertex> Vertices, IReadOnlyList<uint> Indices);

/// <summary>A Hammer-authored mesh extracted from a source VMAP node.</summary>
/// <param name="NodeId">Source-map node id.</param>
/// <param name="Parts">Triangle ranges grouped by material.</param>
public sealed record ValveMapMesh(int NodeId, IReadOnlyList<ValveMapMeshPart> Parts);

/// <summary>Reads renderable triangle data from Hammer's editable half-edge mesh representation.</summary>
public static class ValveMapMeshReader
{
    /// <summary>Attempts to read a CMapMesh source-map node.</summary>
    /// <param name="element">The source-map node.</param>
    /// <param name="mesh">The extracted mesh when successful.</param>
    /// <returns>Whether the node contains readable mesh data.</returns>
    public static bool TryRead(Datamodel.Element element, out ValveMapMesh? mesh)
    {
        mesh = null;

        if (!element.ClassName.Equals(nameof(CMapMesh), StringComparison.Ordinal)
            || !element.TryGetValue("meshData", out var meshValue)
            || meshValue is not Datamodel.Element meshData)
        {
            return false;
        }

        var positions = GetStream<Vector3>(meshData, "vertexData", "position");
        var normals = GetStream<Vector3>(meshData, "faceVertexData", "normal");
        var tangents = GetStream<Vector4>(meshData, "faceVertexData", "tangent");
        var texCoords = GetStream<Vector2>(meshData, "faceVertexData", "texcoord");
        var materialIndices = GetStream<int>(meshData, "faceData", "materialindex");

        if (positions == null
            || !TryGetIntArray(meshData, "vertexDataIndices", out var vertexDataIndices)
            || !TryGetIntArray(meshData, "edgeVertexIndices", out var edgeVertexIndices)
            || !TryGetIntArray(meshData, "edgeNextIndices", out var edgeNextIndices)
            || !TryGetIntArray(meshData, "edgeVertexDataIndices", out var edgeVertexDataIndices)
            || !TryGetIntArray(meshData, "faceEdgeIndices", out var faceEdgeIndices)
            || !TryGetIntArray(meshData, "faceDataIndices", out var faceDataIndices))
        {
            return false;
        }

        var parts = new Dictionary<int, (List<ValveMapMeshVertex> Vertices, List<uint> Indices)>();
        for (var faceIndex = 0; faceIndex < faceEdgeIndices.Count; faceIndex++)
        {
            var materialIndex = GetMaterialIndex(faceIndex, faceDataIndices, materialIndices);
            if (!parts.TryGetValue(materialIndex, out var part))
            {
                part = ([], []);
                parts.Add(materialIndex, part);
            }

            List<ValveMapMeshVertex> faceVertices = [];
            var edgeIndex = faceEdgeIndices[faceIndex];
            for (var edgeCount = 0; edgeIndex >= 0 && edgeIndex < edgeNextIndices.Count && edgeCount <= edgeNextIndices.Count; edgeCount++)
            {
                if (!TryGetVertex(
                    edgeIndex,
                    positions,
                    normals,
                    tangents,
                    texCoords,
                    vertexDataIndices,
                    edgeVertexIndices,
                    edgeVertexDataIndices,
                    out var vertex))
                {
                    faceVertices.Clear();
                    break;
                }

                faceVertices.Add(vertex);
                edgeIndex = edgeNextIndices[edgeIndex];
                if (edgeIndex == faceEdgeIndices[faceIndex])
                {
                    break;
                }
            }

            for (var i = 2; i < faceVertices.Count; i++)
            {
                var vertexIndex = (uint)part.Vertices.Count;
                part.Vertices.Add(faceVertices[0]);
                part.Vertices.Add(faceVertices[i - 1]);
                part.Vertices.Add(faceVertices[i]);
                part.Indices.Add(vertexIndex);
                part.Indices.Add(vertexIndex + 1);
                part.Indices.Add(vertexIndex + 2);
            }
        }

        var materialNames = ReadStringArray(meshData, "materials");
        var extractedParts = parts
            .Where(static pair => pair.Value.Indices.Count > 0)
            .Select(pair => new ValveMapMeshPart(
                pair.Key >= 0 && pair.Key < materialNames.Count ? materialNames[pair.Key] : string.Empty,
                pair.Value.Vertices,
                pair.Value.Indices))
            .ToList();

        if (extractedParts.Count == 0)
        {
            return false;
        }

        mesh = new ValveMapMesh(ReadInt32(element, "nodeID"), extractedParts);
        return true;
    }

    private static bool TryGetVertex(
        int edgeIndex,
        Datamodel.Array<Vector3> positions,
        Datamodel.Array<Vector3>? normals,
        Datamodel.Array<Vector4>? tangents,
        Datamodel.Array<Vector2>? texCoords,
        Datamodel.Array<int> vertexDataIndices,
        Datamodel.Array<int> edgeVertexIndices,
        Datamodel.Array<int> edgeVertexDataIndices,
        out ValveMapMeshVertex vertex)
    {
        vertex = default;
        if (edgeIndex < 0 || edgeIndex >= edgeVertexIndices.Count || edgeIndex >= edgeVertexDataIndices.Count)
        {
            return false;
        }

        var vertexIndex = edgeVertexIndices[edgeIndex];
        if (vertexIndex < 0 || vertexIndex >= vertexDataIndices.Count)
        {
            return false;
        }

        var positionIndex = vertexDataIndices[vertexIndex];
        if (positionIndex < 0 || positionIndex >= positions.Count)
        {
            return false;
        }

        var faceVertexIndex = edgeVertexDataIndices[edgeIndex];
        vertex = new(
            positions[positionIndex],
            GetOrDefault(normals, faceVertexIndex, Vector3.UnitZ),
            GetOrDefault(tangents, faceVertexIndex, new Vector4(1f, 0f, 0f, 1f)),
            GetOrDefault(texCoords, faceVertexIndex, Vector2.Zero));
        return true;
    }

    private static int GetMaterialIndex(int faceIndex, Datamodel.Array<int> faceDataIndices, Datamodel.Array<int>? materialIndices)
    {
        if (materialIndices == null || faceIndex >= faceDataIndices.Count)
        {
            return -1;
        }

        var faceDataIndex = faceDataIndices[faceIndex];
        return faceDataIndex >= 0 && faceDataIndex < materialIndices.Count ? materialIndices[faceDataIndex] : -1;
    }

    private static T GetOrDefault<T>(Datamodel.Array<T>? values, int index, T fallback) where T : notnull
        => values != null && index >= 0 && index < values.Count ? values[index] : fallback;

    private static Datamodel.Array<T>? GetStream<T>(Datamodel.Element meshData, string containerName, string semanticName)
        where T : notnull
    {
        if (!meshData.TryGetValue(containerName, out var containerValue)
            || containerValue is not Datamodel.Element container
            || !container.TryGetValue("streams", out var streamsValue)
            || streamsValue is not Datamodel.ElementArray streams)
        {
            return null;
        }

        foreach (var streamValue in streams)
        {
            if (streamValue is not Datamodel.Element stream
                || !stream.TryGetValue("semanticName", out var semanticValue)
                || semanticValue is not string semantic
                || !semantic.Equals(semanticName, StringComparison.OrdinalIgnoreCase)
                || !stream.TryGetValue("data", out var dataValue)
                || dataValue is not Datamodel.Array<T> data)
            {
                continue;
            }

            return data;
        }

        return null;
    }

    private static bool TryGetIntArray(Datamodel.Element element, string name, out Datamodel.IntArray values)
    {
        if (element.TryGetValue(name, out var value) && value is Datamodel.IntArray array)
        {
            values = array;
            return true;
        }

        values = [];
        return false;
    }

    private static List<string> ReadStringArray(Datamodel.Element element, string name)
        => element.TryGetValue(name, out var value) && value is Datamodel.StringArray values ? values.ToList() : [];

    private static int ReadInt32(Datamodel.Element element, string name)
        => element.TryGetValue(name, out var value) && value is IConvertible convertible ? convertible.ToInt32(null) : 0;
}
