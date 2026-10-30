using System.IO;

namespace ValveResourceFormat.Source1.Model;

/// <summary>A bone of a Source 1 model. Coordinates are Source units (Z-up inches).</summary>
internal sealed class Source1Bone
{
    /// <summary>Bone name.</summary>
    public required string Name { get; init; }

    /// <summary>Index of the parent bone, or -1 for a root bone.</summary>
    public required int ParentIndex { get; init; }

    /// <summary>Local bind position relative to the parent.</summary>
    public required Vector3 Position { get; init; }

    /// <summary>Local bind rotation relative to the parent.</summary>
    public required Quaternion Rotation { get; init; }

    /// <summary>Inverse of the bone's model-space bind transform, as a row-vector matrix (translation in M41-M43).</summary>
    public required Matrix4x4 PoseToBone { get; init; }

    /// <summary>Studio bone flags.</summary>
    public required int Flags { get; init; }

    /// <summary>Index of the physics bone, or -1.</summary>
    public required int PhysicsBone { get; init; }

    /// <summary>Surface property name of this bone.</summary>
    public required string SurfaceProperty { get; init; }
}

/// <summary>An attachment point of a Source 1 model.</summary>
internal sealed class Source1Attachment
{
    /// <summary>Attachment name.</summary>
    public required string Name { get; init; }

    /// <summary>Studio attachment flags.</summary>
    public required int Flags { get; init; }

    /// <summary>Index of the bone the attachment is relative to.</summary>
    public required int BoneIndex { get; init; }

    /// <summary>Transform relative to the bone, as a row-vector matrix (translation in M41-M43).</summary>
    public required Matrix4x4 Local { get; init; }
}

/// <summary>A hitbox of a Source 1 model.</summary>
internal sealed class Source1Hitbox
{
    /// <summary>Hitbox name (empty for MDL version 44).</summary>
    public required string Name { get; init; }

    /// <summary>Index of the bone the box is attached to.</summary>
    public required int BoneIndex { get; init; }

    /// <summary>Hitgroup number.</summary>
    public required int Group { get; init; }

    /// <summary>Minimum corner in bone space.</summary>
    public required Vector3 Min { get; init; }

    /// <summary>Maximum corner in bone space.</summary>
    public required Vector3 Max { get; init; }
}

/// <summary>A named set of hitboxes.</summary>
internal sealed class Source1HitboxSet
{
    /// <summary>Set name.</summary>
    public required string Name { get; init; }

    /// <summary>Hitboxes of the set.</summary>
    public required Source1Hitbox[] Hitboxes { get; init; }
}

/// <summary>A vertex of a Source 1 mesh. Up to three bone influences.</summary>
internal struct Source1Vertex
{
    /// <summary>Position in Source units.</summary>
    public Vector3 Position;

    /// <summary>Normal.</summary>
    public Vector3 Normal;

    /// <summary>Tangent in XYZ, bitangent sign in W.</summary>
    public Vector4 Tangent;

    /// <summary>Texture coordinates.</summary>
    public Vector2 TexCoord;

    /// <summary>Number of bone influences used (at least 1).</summary>
    public byte NumBones;

    /// <summary>First bone index.</summary>
    public byte Bone0;

    /// <summary>Second bone index.</summary>
    public byte Bone1;

    /// <summary>Third bone index.</summary>
    public byte Bone2;

    /// <summary>First bone weight.</summary>
    public float Weight0;

    /// <summary>Second bone weight.</summary>
    public float Weight1;

    /// <summary>Third bone weight.</summary>
    public float Weight2;
}

/// <summary>A mesh of a Source 1 sub-model, one material, LOD 0.</summary>
internal sealed class Source1Mesh
{
    /// <summary>Deduplicated vertices.</summary>
    public required Source1Vertex[] Vertices { get; init; }

    /// <summary>Triangle list indices into <see cref="Vertices"/> with the winding already swapped to counter-clockwise.</summary>
    public required ushort[] Indices { get; init; }

    /// <summary>Index into the skin family rows, see <see cref="Source1Model.SkinFamilies"/>.</summary>
    public required int SkinRefIndex { get; init; }

    /// <summary>Index into <see cref="Source1Model.MaterialNames"/> for the default skin family.</summary>
    public required int MaterialIndex { get; init; }

    /// <summary>Studio material type, 1 marks an eyeball mesh.</summary>
    public required int MaterialType { get; init; }

    /// <summary>For an eyeball mesh, the index of its eyeball in <see cref="Source1SubModel.Eyeballs"/>.</summary>
    public required int MaterialParam { get; init; }

    /// <summary>For each mesh vertex id, the index in <see cref="Vertices"/> of the vertex read from it, or -1 when no triangle uses the id.</summary>
    public required int[] OutputByOriginalVertexId { get; init; }

    /// <summary>For each LOD 0 strip group of the mesh, the mesh vertex id of each of its vertices.</summary>
    public required int[][] StripGroupVertexIds { get; init; }

    /// <summary>Position of the first strip group of the mesh among the LOD 0 strip groups of the whole model.</summary>
    public required int FirstStripGroup { get; init; }

    /// <summary>Morph targets that move the vertices of the mesh.</summary>
    public required Source1MeshFlex[] Flexes { get; init; }
}

/// <summary>A sub-model (one choice of a body group).</summary>
internal sealed class Source1SubModel
{
    /// <summary>Name of the sub-model.</summary>
    public required string Name { get; init; }

    /// <summary>Meshes with geometry.</summary>
    public required Source1Mesh[] Meshes { get; init; }

    /// <summary>Eyeballs of the sub-model.</summary>
    public required Source1Eyeball[] Eyeballs { get; init; }
}

/// <summary>A body part (body group) holding alternative sub-models.</summary>
internal sealed class Source1BodyPart
{
    /// <summary>Body group name.</summary>
    public required string Name { get; init; }

    /// <summary>Multiplier of this body group in the body value of an entity, which holds the sub-model of every group as a mixed radix number.</summary>
    public required int Base { get; init; }

    /// <summary>Sub-models to choose from.</summary>
    public required Source1SubModel[] Models { get; init; }
}

/// <summary>A solid of the physics model, as convex point clouds in Source units.</summary>
internal sealed class Source1PhySolid
{
    /// <summary>Solid index.</summary>
    public required int Index { get; init; }

    /// <summary>Center of mass.</summary>
    public required Vector3 MassCenter { get; init; }

    /// <summary>Convex hulls as point lists.</summary>
    public required Vector3[][] Convexes { get; init; }
}

/// <summary>A Source 1 model read from MDL, VVD, VTX and PHY files.</summary>
internal sealed partial class Source1Model
{
    // Size of a vertex in the VVD file, which the vertex index of an MDL model is a byte offset in
    private const int StudioVertexSize = 48;

    /// <summary>MDL version.</summary>
    public int Version { get; private set; }

    /// <summary>MDL checksum.</summary>
    public int Checksum { get; private set; }

    /// <summary>Studio header flags.</summary>
    public int Flags { get; private set; }

    /// <summary>Whether the model is flagged as a static prop.</summary>
    public bool IsStaticProp => (Flags & 0x10) != 0;

    /// <summary>Eye position.</summary>
    public Vector3 EyePosition { get; private set; }

    /// <summary>Illumination position.</summary>
    public Vector3 IlluminationPosition { get; private set; }

    /// <summary>Hull minimum.</summary>
    public Vector3 HullMin { get; private set; }

    /// <summary>Hull maximum.</summary>
    public Vector3 HullMax { get; private set; }

    /// <summary>View bounding box minimum.</summary>
    public Vector3 ViewMin { get; private set; }

    /// <summary>View bounding box maximum.</summary>
    public Vector3 ViewMax { get; private set; }

    /// <summary>Mass.</summary>
    public float Mass { get; private set; }

    /// <summary>Default surface property.</summary>
    public string SurfaceProperty { get; private set; } = string.Empty;

    /// <summary>Raw $keyvalues text of the MDL.</summary>
    public string KeyValuesText { get; private set; } = string.Empty;

    /// <summary>Skeleton bones.</summary>
    public Source1Bone[] Bones { get; private set; } = [];

    /// <summary>Attachments.</summary>
    public Source1Attachment[] Attachments { get; private set; } = [];

    /// <summary>Hitbox sets.</summary>
    public Source1HitboxSet[] HitboxSets { get; private set; } = [];

    /// <summary>Material search paths, relative to materials/.</summary>
    public string[] CdMaterials { get; private set; } = [];

    /// <summary>Material names without extension, relative to a search path.</summary>
    public string[] MaterialNames { get; private set; } = [];

    /// <summary>For each skin family, the material index of every skin reference.</summary>
    public int[][] SkinFamilies { get; private set; } = [];

    /// <summary>Body parts with LOD 0 geometry.</summary>
    public Source1BodyPart[] BodyParts { get; private set; } = [];

    /// <summary>Number of LODs in the VTX file, 0 when there is no geometry.</summary>
    public int NumLods { get; private set; }

    /// <summary>Switch distance of each LOD.</summary>
    public float[] LodSwitchPoints { get; private set; } = [];

    /// <summary>Physics solids, empty when there is no PHY file.</summary>
    public Source1PhySolid[] PhySolids { get; private set; } = [];

    /// <summary>Raw trailing KeyValues text of the PHY file.</summary>
    public string PhyKeyValuesText { get; private set; } = string.Empty;

    /// <summary>Non-fatal problems found while loading, such as checksum mismatches.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// Loads a model.
    /// </summary>
    /// <param name="mdlPath">Path of the .mdl file as understood by <paramref name="openFile"/>.</param>
    /// <param name="openFile">Opens a file by path, returning null when it does not exist.</param>
    /// <param name="requireGeometry">Whether a model with body parts fails to load without its VVD and VTX files. When false, it loads without geometry.</param>
    public static Source1Model Load(string mdlPath, Func<string, Stream?> openFile, bool requireGeometry = true)
    {
        ArgumentNullException.ThrowIfNull(mdlPath);
        ArgumentNullException.ThrowIfNull(openFile);

        var basePath = mdlPath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) ? mdlPath[..^4] : mdlPath;

        var mdlData = ReadFile(openFile, basePath + ".mdl")
            ?? throw new FileNotFoundException("MDL file not found.", mdlPath);

        var mdl = new MdlFile(mdlData);
        var model = new Source1Model();
        model.ReadHeaderData(mdl);
        model.ReadFlexData(mdl);
        model.SetAnimationSource(mdl, mdlData, basePath, openFile);

        var hasGeometry = mdl.BodyParts.Length > 0;

        var vvdStream = OpenBuffered(openFile, basePath + ".vvd");
        var vtxStream = OpenFirst(openFile, basePath, ".dx90.vtx", ".vtx", ".dx80.vtx", ".sw.vtx");

        if (requireGeometry && hasGeometry && (vvdStream == null || vtxStream == null))
        {
            throw new FileNotFoundException(vvdStream == null ? "VVD file not found." : "VTX file not found.", mdlPath);
        }

        if (vvdStream != null && vtxStream != null && hasGeometry)
        {
            var vvd = new VvdFile(vvdStream);
            var vtx = new VtxFile(vtxStream, mdl, vvd);

            if (vvd.Checksum != mdl.Checksum || vtx.Checksum != mdl.Checksum)
            {
                model.Warnings.Add("MDL, VVD and VTX checksums do not match.");
            }

            model.BuildGeometry(mdl, vvd, vtx);
        }

        var phyStream = OpenBuffered(openFile, basePath + ".phy");

        if (phyStream != null)
        {
            try
            {
                var phy = new PhyFile(phyStream);

                if (phy.Checksum != mdl.Checksum)
                {
                    model.Warnings.Add("MDL and PHY checksums do not match.");
                }

                model.PhySolids = new Source1PhySolid[phy.Solids.Length];

                for (var i = 0; i < model.PhySolids.Length; i++)
                {
                    var solid = phy.Solids[i];

                    model.PhySolids[i] = new Source1PhySolid
                    {
                        Index = solid.Index,
                        MassCenter = solid.MassCenter,
                        Convexes = [.. solid.Convexes],
                    };
                }

                model.PhyKeyValuesText = phy.KeyValuesText;
            }
            catch (InvalidDataException e)
            {
                model.Warnings.Add($"PHY failed to load: {e.Message}");
            }
        }

        return model;
    }

    /// <summary>
    /// Returns candidate material paths (relative to the game root) for a material index, one per search path.
    /// </summary>
    public IEnumerable<string> GetMaterialPaths(int materialIndex)
    {
        var name = MaterialNames[materialIndex];

        foreach (var cdMaterial in CdMaterials)
        {
            var directory = cdMaterial.Trim('/');
            var path = directory.Length == 0 ? name : $"{directory}/{name}";
            yield return $"materials/{path}.vmt";
        }
    }

    internal static byte[] ReadAll(Stream stream)
    {
        if (!stream.CanSeek)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        var data = new byte[stream.Length - stream.Position];
        stream.ReadExactly(data);
        return data;
    }

    private static byte[]? ReadFile(Func<string, Stream?> openFile, string path)
    {
        using var stream = openFile(path);

        if (stream == null)
        {
            return null;
        }

        return ReadAll(stream);
    }

    private static MemoryStream? OpenBuffered(Func<string, Stream?> openFile, string path)
    {
        var data = ReadFile(openFile, path);

        if (data == null)
        {
            return null;
        }

        return new MemoryStream(data);
    }

    private static MemoryStream? OpenFirst(Func<string, Stream?> openFile, string basePath, params string[] extensions)
    {
        foreach (var extension in extensions)
        {
            var stream = OpenBuffered(openFile, basePath + extension);

            if (stream != null)
            {
                return stream;
            }
        }

        return null;
    }

    private void ReadHeaderData(MdlFile mdl)
    {
        var header = mdl.Hdr;

        Version = header.Version;
        Checksum = header.Checksum;
        Flags = header.Flags;
        EyePosition = header.EyePosition;
        IlluminationPosition = header.IllumPosition;
        HullMin = header.HullMin;
        HullMax = header.HullMax;
        ViewMin = header.ViewBbMin;
        ViewMax = header.ViewBbMax;
        Mass = header.Mass;
        MaxEyeDeflection = mdl.MaxEyeDeflection;
        SurfaceProperty = mdl.SurfaceProperty;
        KeyValuesText = mdl.KeyValuesText;
        MaterialNames = mdl.MaterialNames;
        CdMaterials = mdl.CdMaterials;

        SkinFamilies = new int[mdl.SkinFamilies.Length][];

        for (var family = 0; family < SkinFamilies.Length; family++)
        {
            SkinFamilies[family] = Array.ConvertAll(mdl.SkinFamilies[family], m => (int)m);
        }

        Bones = new Source1Bone[mdl.Bones.Length];

        for (var i = 0; i < Bones.Length; i++)
        {
            var bone = mdl.Bones[i];

            Bones[i] = new Source1Bone
            {
                Name = mdl.BoneNames[i],
                ParentIndex = bone.Parent,
                Position = bone.Position,
                Rotation = bone.Quaternion,
                PoseToBone = bone.PoseToBone.ToMatrix4x4(),
                Flags = bone.Flags,
                PhysicsBone = bone.PhysicsBone,
                SurfaceProperty = mdl.BoneSurfaceProperties[i],
            };
        }

        Attachments = new Source1Attachment[mdl.Attachments.Length];

        for (var i = 0; i < Attachments.Length; i++)
        {
            var attachment = mdl.Attachments[i];

            Attachments[i] = new Source1Attachment
            {
                Name = mdl.AttachmentNames[i],
                Flags = attachment.Flags,
                BoneIndex = attachment.LocalBone,
                Local = attachment.Local.ToMatrix4x4(),
            };
        }

        HitboxSets = new Source1HitboxSet[mdl.HitboxSets.Length];

        for (var i = 0; i < HitboxSets.Length; i++)
        {
            var set = mdl.HitboxSets[i];
            var hitboxes = new Source1Hitbox[set.Hitboxes.Length];

            for (var j = 0; j < hitboxes.Length; j++)
            {
                var hitbox = set.Hitboxes[j];

                hitboxes[j] = new Source1Hitbox
                {
                    Name = hitbox.Name,
                    BoneIndex = hitbox.Bone,
                    Group = hitbox.Group,
                    Min = hitbox.Min,
                    Max = hitbox.Max,
                };
            }

            HitboxSets[i] = new Source1HitboxSet { Name = set.Name, Hitboxes = hitboxes };
        }
    }

    private void BuildGeometry(MdlFile mdl, VvdFile vvd, VtxFile vtx)
    {
        NumLods = vtx.NumLods;
        LodSwitchPoints = vtx.LodSwitchPoints;

        var vertexIndices = vvd.GetVertexIndices(0);
        var eyeballs = ConvertEyeballs(mdl);
        BodyParts = new Source1BodyPart[mdl.BodyParts.Length];

        var stripGroupCount = 0;

        for (var partIndex = 0; partIndex < BodyParts.Length; partIndex++)
        {
            var mdlModels = mdl.Models[partIndex];
            var models = new Source1SubModel[mdlModels.Length];

            for (var modelIndex = 0; modelIndex < models.Length; modelIndex++)
            {
                var mdlMeshes = mdl.Meshes[partIndex][modelIndex];
                var vtxMeshes = vtx.Meshes[partIndex][modelIndex][0];
                var modelVertexBase = mdlModels[modelIndex].VertexIndex / StudioVertexSize;
                var meshes = new List<Source1Mesh>();

                for (var meshIndex = 0; meshIndex < mdlMeshes.Length && meshIndex < vtxMeshes.Length; meshIndex++)
                {
                    var meshVertexBase = modelVertexBase + mdlMeshes[meshIndex].VertexOffset;
                    var flexes = mdl.MeshFlexes[partIndex][modelIndex][meshIndex];
                    var mesh = BuildMesh(mdl, vvd, vertexIndices, mdlMeshes[meshIndex], vtxMeshes[meshIndex], flexes, meshVertexBase, stripGroupCount);

                    if (mesh != null)
                    {
                        meshes.Add(mesh);
                    }

                    stripGroupCount += vtxMeshes[meshIndex].StripGroupVertexIds.Length;
                }

                models[modelIndex] = new Source1SubModel { Name = mdlModels[modelIndex].Name, Meshes = meshes.ToArray(), Eyeballs = eyeballs[partIndex][modelIndex] };
            }

            BodyParts[partIndex] = new Source1BodyPart
            {
                Name = mdl.BodyPartNames[partIndex],
                Base = mdl.BodyParts[partIndex].Base,
                Models = models,
            };
        }
    }

    private static Source1Mesh? BuildMesh(MdlFile mdl, VvdFile vvd, int[]? vertexIndices, MdlFile.StudioMesh mdlMesh, VtxFile.MeshData data, MdlFile.MeshFlex[] flexes, int meshVertexBase, int firstStripGroup)
    {
        if (mdlMesh.NumVertices == 0 || data.Indices.Length < 3)
        {
            return null;
        }

        var originalToOutput = new int[Math.Min(data.VertexCount, ushort.MaxValue + 1)];
        Array.Fill(originalToOutput, -1);

        var indices = new ushort[data.Indices.Length];
        var sourceIds = new List<int>();

        for (var i = 0; i < indices.Length; i++)
        {
            var original = data.Indices[i];

            if ((uint)original >= (uint)originalToOutput.Length)
            {
                throw new InvalidDataException("VTX vertex is outside its MDL mesh.");
            }

            var mapped = originalToOutput[original];

            if (mapped == -1)
            {
                mapped = sourceIds.Count;
                originalToOutput[original] = mapped;
                sourceIds.Add(original);
            }

            indices[i] = (ushort)mapped;
        }

        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        }

        var vertices = new Source1Vertex[sourceIds.Count];

        for (var i = 0; i < vertices.Length; i++)
        {
            var vertexId = meshVertexBase + sourceIds[i];
            var source = vertexIndices != null ? vertexIndices[vertexId] : vertexId;
            vertices[i] = ConvertVertex(vvd.Vertices[source], vvd.Tangents is null ? default : vvd.Tangents[source], mdl.Bones.Length);
        }

        var defaultSkin = mdl.SkinFamilies.Length > 0 ? mdl.SkinFamilies[0] : null;
        var materialIndex = defaultSkin != null ? defaultSkin[mdlMesh.Material] : mdlMesh.Material;

        return new Source1Mesh
        {
            Vertices = vertices,
            Indices = indices,
            SkinRefIndex = mdlMesh.Material,
            MaterialIndex = materialIndex,
            MaterialType = mdlMesh.MaterialType,
            MaterialParam = mdlMesh.MaterialParam,
            OutputByOriginalVertexId = originalToOutput,
            StripGroupVertexIds = data.StripGroupVertexIds,
            FirstStripGroup = firstStripGroup,
            Flexes = BuildMeshFlexes(flexes, originalToOutput),
        };
    }

    private static Source1Vertex ConvertVertex(StudioVertex source, Vector4 tangent, int boneCount)
    {
        var weights = source.BoneWeights;
        var vertex = new Source1Vertex
        {
            Position = source.Position,
            Normal = source.Normal,
            Tangent = tangent,
            TexCoord = source.TexCoord,
        };

        if (weights.NumBones <= 1)
        {
            vertex.NumBones = 1;
            vertex.Bone0 = weights.NumBones == 1 ? weights.Bone0 : (byte)0;
            vertex.Weight0 = 1f;
        }
        else
        {
            vertex.NumBones = Math.Min(weights.NumBones, (byte)3);
            vertex.Bone0 = weights.Bone0;
            vertex.Bone1 = weights.Bone1;
            vertex.Bone2 = weights.NumBones > 2 ? weights.Bone2 : (byte)0;
            vertex.Weight0 = weights.Weight0;
            vertex.Weight1 = weights.Weight1;
            vertex.Weight2 = weights.NumBones > 2 ? weights.Weight2 : 0f;
        }

        if (vertex.Bone0 >= boneCount || vertex.Bone1 >= boneCount || vertex.Bone2 >= boneCount)
        {
            throw new InvalidDataException("VVD vertex references an invalid bone.");
        }

        return vertex;
    }
}
