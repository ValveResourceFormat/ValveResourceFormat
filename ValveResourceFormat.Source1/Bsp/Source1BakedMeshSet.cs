using System.Globalization;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Source1.Model;
using S2Model = ValveResourceFormat.ResourceTypes.Model;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Collects the quads of small pieces of a map, such as sprites or ropes, into one model per material and map area,
/// split by the 3D sky box area and the rest of the map.
/// </summary>
/// <typeparam name="TVertex">The vertex struct, whose fields are listed by the <see cref="VertexField"/> values.</typeparam>
internal sealed class Source1BakedMeshSet<TVertex>(string namePrefix, VertexField[] fields)
    where TVertex : unmanaged
{
    // The side of the square map areas whose pieces share a model, so that a model can be culled
    private const float ChunkSize = 2048f;

    private readonly record struct ChunkKey(bool InSky, int X, int Y, string MaterialPath);

    /// <summary>The quads of one material in one map area.</summary>
    internal sealed class Chunk
    {
        private readonly List<TVertex> vertices = [];
        private readonly List<int> indices = [];
        private Vector3 min = new(float.MaxValue);
        private Vector3 max = new(float.MinValue);

        public TVertex[] Vertices => [.. vertices];

        public int[] Indices => [.. indices];

        public Vector3 Min => min;

        public Vector3 Max => max;

        /// <summary>Adds a vertex and returns its index.</summary>
        /// <param name="vertex">The vertex.</param>
        /// <param name="position">Where the vertex draws, or the point it moves away from.</param>
        /// <param name="reach">How far from <paramref name="position"/> the vertex can draw.</param>
        public int AddVertex(TVertex vertex, Vector3 position, float reach)
        {
            var extent = new Vector3(reach);
            min = Vector3.Min(min, position - extent);
            max = Vector3.Max(max, position + extent);
            vertices.Add(vertex);

            return vertices.Count - 1;
        }

        /// <summary>Adds the two triangles of a quad from the indices of its four corners in order.</summary>
        public void AddQuad(int first, int second, int third, int fourth)
        {
            indices.Add(first);
            indices.Add(second);
            indices.Add(third);
            indices.Add(first);
            indices.Add(third);
            indices.Add(fourth);
        }
    }

    private readonly Dictionary<ChunkKey, Chunk> chunks = [];

    /// <summary>Gets the chunk for pieces of a material placed at a point, creating it on first use.</summary>
    public Chunk Get(bool inSky, Vector3 origin, string materialPath)
    {
        var x = (int)MathF.Floor(origin.X / ChunkSize);
        var y = (int)MathF.Floor(origin.Y / ChunkSize);
        var key = new ChunkKey(inSky, x, y, materialPath);

        if (!chunks.TryGetValue(key, out var chunk))
        {
            chunk = new Chunk();
            chunks.Add(key, chunk);
        }

        return chunk;
    }

    /// <summary>Adds the model of every chunk to the converted map.</summary>
    /// <param name="map">The converted map.</param>
    /// <param name="worldParts">Gets the paths of the models of the map.</param>
    /// <param name="skyParts">Gets the paths of the models in the 3D sky box area.</param>
    public void AddModels(Source1ConvertedMap map, List<string> worldParts, List<string> skyParts)
    {
        var count = 0;

        foreach (var (key, chunk) in chunks)
        {
            var path = GetPath(map, key, count++);

            Source1WorldConverter.AddFile(map, path, CreateModel(path, key.MaterialPath, chunk));

            if (key.InSky)
            {
                skyParts.Add(path);
            }
            else
            {
                worldParts.Add(path);
            }
        }
    }

    private string GetPath(Source1ConvertedMap map, ChunkKey key, int count)
    {
        var area = key.InSky ? "sky" : "world";

        return string.Create(CultureInfo.InvariantCulture, $"{map.MapPath}/models/{namePrefix}_{area}_{key.X}_{key.Y}_{count}.vmdl");
    }

    /// <summary>Adds the model of a chunk that is not placed by its map area, such as the model of an entity.</summary>
    /// <param name="map">The converted map.</param>
    /// <param name="path">The path of the model.</param>
    /// <param name="materialPath">The path of the material of the chunk.</param>
    /// <param name="chunk">The chunk, see <see cref="Get"/>.</param>
    public void AddModel(Source1ConvertedMap map, string path, string materialPath, Chunk chunk)
        => Source1WorldConverter.AddFile(map, path, CreateModel(path, materialPath, chunk));

    private Resource CreateModel(string path, string materialPath, Chunk chunk)
    {
        var vertices = chunk.Vertices;
        var indices = chunk.Indices;
        var drawCall = new MeshDrawCall(materialPath, 0, vertices.Length, 0, indices.Length);
        var buffers = Source1VertexBuffers.Create(vertices, fields, indices);
        var mesh = new ModelMeshSource(namePrefix, buffers, [drawCall], chunk.Min, chunk.Max) { MeshGroupMask = 1UL };
        var materialGroups = new List<ModelMaterialGroup> { new(Source1ModelConverter.GetSkinName(0), [materialPath]) };

        return S2Model.Create(path, [mesh], [], materialGroups, [namePrefix], 1UL);
    }
}
