using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.Source1.Material;
using ValveResourceFormat.Source1.Model;
using ValveResourceFormat.Source1.Physics;
using S2Model = ValveResourceFormat.ResourceTypes.Model;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Converts the collision the compiler stored for each model of a map to Source 2 physics: the world's to the world
/// physics of the map, and each brush model's to a physics file its model references.
/// </summary>
internal static class Source1WorldPhysicsConverter
{
    private const int ContentsSolid = 0x1;
    private const int ContentsPlayerClip = 0x10000;
    private const int ContentsMonsterClip = 0x20000;

    // A displacement whose minimum tesselation has the top bit set stores flags in the rest of it
    private const uint DisplacementFlagsMagic = 0x80000000;
    private const int DisplacementNoPhysicsCollision = 0x2;
    private const int DisplacementNoHullCollision = 0x4;

    private const int ModelHeaderSize = 16;

    /// <summary>Adds the physics of every model of the map that has collision.</summary>
    /// <param name="map">The converted map, whose brush models are already added.</param>
    /// <param name="bsp">The map.</param>
    /// <param name="getMaterialInfo">Reads a material, for the surface property of displacements.</param>
    /// <param name="logger">Receives a warning for the collision that is left out.</param>
    public static void Convert(Source1ConvertedMap map, BspFile bsp, Func<string, Source1MaterialInfo?> getMaterialInfo, ILogger logger)
    {
        var lump = bsp.GetLumpData(LumpType.PhysCollide).ToArray();
        var offset = 0;

        while (offset + ModelHeaderSize <= lump.Length)
        {
            var header = lump.AsSpan(offset);
            var modelIndex = BinaryPrimitives.ReadInt32LittleEndian(header);
            var solidsLength = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            var keyValuesLength = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
            var solidCount = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
            offset += ModelHeaderSize;

            if (modelIndex < 0)
            {
                break;
            }

            var solidsOffset = offset;
            offset += solidsLength + keyValuesLength;

            PhyFile collision;

            try
            {
                collision = new PhyFile(lump, solidsOffset, solidCount, solidsLength, keyValuesLength);
            }
            catch (InvalidDataException exception)
            {
                logger.LogWarning("Collision of model {Model} of the map is left out: {Message}", modelIndex, exception.Message);
                continue;
            }

            if (collision.MoppSolidCount > 0)
            {
                logger.LogWarning("Model {Model} of the map has {Count} MOPP collision solids, which are left out", modelIndex, collision.MoppSolidCount);
            }

            var sections = ParseKeyValues(collision.KeyValuesText);
            var builder = new PhysicsAggregateBuilder();
            AddSolids(builder, collision, sections);

            // Without virtual terrain the displacements were compiled into the solids of the world
            if (modelIndex == 0 && sections.Exists(static section => section.Name == "virtualterrain"))
            {
                AddDisplacements(builder, bsp, getMaterialInfo);
            }

            if (builder.IsEmpty)
            {
                continue;
            }

            if (modelIndex == 0)
            {
                var physicsPath = $"{map.MapPath}/world_physics.vphys";
                Source1WorldConverter.AddFile(map, physicsPath, builder.BuildResource());
                Source1WorldConverter.AddFile(map, $"{map.MapPath}/world_physics.vrman", Source1WorldConverter.CreateWorldPhysicsManifest(physicsPath));
                continue;
            }

            AddBrushModelPhysics(map, modelIndex, builder);
        }
    }

    private static void AddBrushModelPhysics(Source1ConvertedMap map, int modelIndex, PhysicsAggregateBuilder builder)
    {
        var modelPath = map.GetBrushModelPath(modelIndex);

        if (!map.Files.TryGetValue(modelPath + GameFileLoader.CompiledFileSuffix, out var modelResource)
            || modelResource.DataBlock is not S2Model model)
        {
            return;
        }

        var physicsPath = Path.ChangeExtension(modelPath, ".vphys");
        Source1WorldConverter.AddFile(map, physicsPath, builder.BuildResource());
        model.Data["m_refPhysicsData"].Add(S2Model.CreateResourceReference(physicsPath));
    }

    internal sealed record Section(string Name, Dictionary<string, string> Values);

    private static void AddSolids(PhysicsAggregateBuilder builder, PhyFile collision, List<Section> sections)
    {
        var materialNames = GetMaterialNames(sections);
        var solidSections = GetSolidSections(sections);

        foreach (var solid in collision.Solids)
        {
            solidSections.TryGetValue(solid.Index, out var section);
            AddSolid(builder, solid, section, materialNames);
        }
    }

    /// <summary>Gets the names of the surface properties a collision lump or PHY file indexes its convexes by.</summary>
    internal static Dictionary<int, string> GetMaterialNames(List<Section> sections)
    {
        var materialNames = new Dictionary<int, string>();

        foreach (var section in sections)
        {
            if (section.Name != "materialtable")
            {
                continue;
            }

            foreach (var (name, index) in section.Values)
            {
                materialNames[int.Parse(index, CultureInfo.InvariantCulture)] = name;
            }
        }

        return materialNames;
    }

    /// <summary>Gets the key values of each solid, by solid index.</summary>
    internal static Dictionary<int, Section> GetSolidSections(List<Section> sections)
    {
        var solidSections = new Dictionary<int, Section>();

        foreach (var section in sections)
        {
            if (section.Name != "materialtable" && section.Values.TryGetValue("index", out var solidIndex))
            {
                solidSections[int.Parse(solidIndex, CultureInfo.InvariantCulture)] = section;
            }
        }

        return solidSections;
    }

    /// <summary>Adds the convexes of a solid, as hulls and as one mesh for the flat convexes that make a triangle mesh.</summary>
    /// <param name="builder">The physics to add to.</param>
    /// <param name="solid">The solid.</param>
    /// <param name="section">The key values of the solid, or <see langword="null"/> when it has none.</param>
    /// <param name="materialNames">The surface properties of the convexes, see <see cref="GetMaterialNames"/>.</param>
    internal static void AddSolid(PhysicsAggregateBuilder builder, PhyFile.Solid solid, Section? section, Dictionary<int, string> materialNames)
    {
        var collisionAttributes = GetCollisionAttributes(builder, section);
        var solidSurface = section?.Values.GetValueOrDefault("surfaceprop") ?? "default";
        var meshVertices = new List<Vector3>();
        var meshIndices = new List<int>();
        var meshSurfaces = new List<int>();

        for (var i = 0; i < solid.Convexes.Count; i++)
        {
            var surface = materialNames.GetValueOrDefault(solid.ConvexMaterials[i], solidSurface);
            var surfaceProperty = builder.GetSurfaceProperty(surface);
            var points = solid.Convexes[i];
            var triangles = solid.ConvexTriangles[i];

            // Triangle meshes, such as displacements, are compiled into one flat convex per triangle, which
            // stores the back of the triangle first and then its front
            if (points.Length == 3)
            {
                meshIndices.Add(meshVertices.Count + triangles[3]);
                meshIndices.Add(meshVertices.Count + triangles[4]);
                meshIndices.Add(meshVertices.Count + triangles[5]);
                meshVertices.AddRange(points);
                meshSurfaces.Add(surfaceProperty);
                continue;
            }

            builder.AddHull(points, triangles, collisionAttributes, surfaceProperty);
        }

        builder.AddMesh(meshVertices, meshIndices, meshSurfaces, collisionAttributes);
    }

    // Clip brushes are compiled into solids of their own, which block players or NPCs only; water into fluids
    private static int GetCollisionAttributes(PhysicsAggregateBuilder builder, Section? section)
    {
        if (section?.Name == "fluid")
        {
            return builder.GetCollisionAttributes("conditionallysolid", ["water"]);
        }

        var contents = ContentsSolid;

        if (section != null && section.Values.TryGetValue("contents", out var contentsValue))
        {
            contents = int.Parse(contentsValue, CultureInfo.InvariantCulture);
        }

        if ((contents & ContentsSolid) != 0)
        {
            return builder.GetCollisionAttributes("default", []);
        }

        var layers = new List<string>();

        if ((contents & ContentsMonsterClip) != 0)
        {
            layers.Add("npcclip");
        }

        if ((contents & ContentsPlayerClip) != 0)
        {
            layers.Add("playerclip");
        }

        if (layers.Count == 0)
        {
            return builder.GetCollisionAttributes("default", []);
        }

        return builder.GetCollisionAttributes("conditionallysolid", layers);
    }

    private static void AddDisplacements(PhysicsAggregateBuilder builder, BspFile bsp, Func<string, Source1MaterialInfo?> getMaterialInfo)
    {
        var infos = bsp.DisplacementInfos;
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        var surfaces = new List<int>();

        for (var index = 0; index < infos.Length; index++)
        {
            var flags = (uint)infos[index].MinTess;
            var hasFlags = (flags & DisplacementFlagsMagic) != 0;
            var noPhysics = hasFlags && (flags & DisplacementNoPhysicsCollision) != 0;
            var noHull = hasFlags && (flags & DisplacementNoHullCollision) != 0;

            // The physics stop both props and movement, so only a displacement that collides with neither is left out
            if (noPhysics && noHull)
            {
                continue;
            }

            var face = bsp.Faces[infos[index].MapFace];
            var textureData = bsp.TextureData[bsp.TextureInfos[face.TexInfo].TexData];
            var material = bsp.GetTextureName(textureData.NameStringTableId);
            var surfaceProperty = builder.GetSurfaceProperty(getMaterialInfo(material)?.SurfaceProperty ?? "default");

            var displacement = bsp.DisplacementManager[index];
            var size = displacement.Size;
            var first = vertices.Count;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    vertices.Add(displacement.GetPosition(x, y));
                }
            }

            for (var y = 0; y < size - 1; y++)
            {
                for (var x = 0; x < size - 1; x++)
                {
                    var i00 = first + x + (y * size);
                    var i10 = i00 + 1;
                    var i01 = i00 + size;
                    var i11 = i01 + 1;

                    if (((x + y) & 1) == 0)
                    {
                        indices.AddRange([i00, i10, i11, i00, i11, i01]);
                    }
                    else
                    {
                        indices.AddRange([i00, i10, i01, i10, i11, i01]);
                    }

                    surfaces.Add(surfaceProperty);
                    surfaces.Add(surfaceProperty);
                }
            }
        }

        builder.AddMesh(vertices, indices, surfaces, builder.GetCollisionAttributes("default", []));
    }

    // The key values are a list of named blocks of quoted key and value pairs, such as solid { "index" "0" }
    internal static List<Section> ParseKeyValues(string text)
    {
        var sections = new List<Section>();
        var tokens = Tokenize(text);
        var position = 0;

        while (position + 1 < tokens.Count)
        {
            var name = tokens[position];
            position += 2;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            while (position + 1 < tokens.Count && tokens[position] != "}")
            {
                values[tokens[position]] = tokens[position + 1];
                position += 2;
            }

            position++;
            sections.Add(new Section(name, values));
        }

        return sections;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var position = 0;

        while (position < text.Length)
        {
            var character = text[position];

            if (char.IsWhiteSpace(character) || character == '\0')
            {
                position++;
                continue;
            }

            if (character is '{' or '}')
            {
                tokens.Add(character.ToString());
                position++;
                continue;
            }

            if (character == '"')
            {
                var end = text.IndexOf('"', position + 1);

                if (end < 0)
                {
                    end = text.Length;
                }

                tokens.Add(text[(position + 1)..end]);
                position = end + 1;
                continue;
            }

            var wordEnd = position;

            while (wordEnd < text.Length && !char.IsWhiteSpace(text[wordEnd]) && text[wordEnd] is not '{' and not '}' and not '"')
            {
                wordEnd++;
            }

            tokens.Add(text[position..wordEnd]);
            position = wordEnd;
        }

        return tokens;
    }
}
