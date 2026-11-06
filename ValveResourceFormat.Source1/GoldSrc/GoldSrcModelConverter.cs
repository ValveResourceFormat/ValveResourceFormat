using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Source1.Model;
using S2Material = ValveResourceFormat.ResourceTypes.Material;
using S2Model = ValveResourceFormat.ResourceTypes.Model;

namespace ValveResourceFormat.GoldSrc;

/// <summary>A GoldSrc model converted to a Source 2 model.</summary>
/// <param name="Model">The model.</param>
/// <param name="Files">The materials and textures the model draws with, by their compiled path.</param>
/// <param name="SequenceNames">The name of the baked sequence of each sequence of the source model, or <see langword="null"/> for one that was left out.</param>
internal sealed record GoldSrcConvertedModel(S2Model Model, IReadOnlyDictionary<string, Resource> Files, IReadOnlyList<string?> SequenceNames);

/// <summary>How an entity blends a model with the frame buffer, which picks the material group it draws with.</summary>
internal enum GoldSrcStudioBlend
{
    /// <summary>The model draws as its textures say.</summary>
    Opaque,

    /// <summary>The model fades with the opacity of the entity.</summary>
    Translucent,

    /// <summary>The model adds to the frame buffer, scaled by the opacity of the entity.</summary>
    Additive,
}

/// <summary>Converts a GoldSrc model to an in-memory Source 2 model with one embedded mesh per studio sub-model.</summary>
/// <remarks>
/// Studio vertices are in the space of their bone, so they are baked to model space at the rest pose
/// and skinned with the inverse of the rest transform of their bone.
/// A vertex has a bone for its position and another for its normal, kept as the first two bone indices.
/// </remarks>
internal static class GoldSrcModelConverter
{
    private const int MaxMeshGroups = 64;
    private const int BlendCount = 3;
    private const string ShaderName = "goldsrc_studio.vfx";

    [StructLayout(LayoutKind.Sequential)]
    private struct ConvertedVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;

        // The rest position of the bone of the normal, which chrome surfaces are reflected from
        public Vector3 ChromeOrigin;
        public uint BoneIndices;
    }

    private static readonly VertexField[] VertexFields =
    [
        new(nameof(ConvertedVertex.Position), "POSITION", 0, DXGI_FORMAT.R32G32B32_FLOAT),
        new(nameof(ConvertedVertex.Normal), "NORMAL", 0, DXGI_FORMAT.R32G32B32_FLOAT),
        new(nameof(ConvertedVertex.TexCoord), "TEXCOORD", 0, DXGI_FORMAT.R32G32_FLOAT),
        new(nameof(ConvertedVertex.ChromeOrigin), "TEXCOORD", 1, DXGI_FORMAT.R32G32B32_FLOAT),
        new(nameof(ConvertedVertex.BoneIndices), "BLENDINDICES", 0, DXGI_FORMAT.R8G8B8A8_UINT),
    ];

    /// <summary>Converts a model and the materials and textures it draws with.</summary>
    /// <param name="source">The model to convert, with the files it keeps its textures and sequences in loaded.</param>
    /// <param name="path">The path of the model in the game, such as <c>models/scientist.mdl</c>, which names its files.</param>
    /// <param name="logger">Where body part models that cannot be drawn are reported.</param>
    public static GoldSrcConvertedModel Convert(GoldSrcMdlFile source, string path, ILogger logger)
    {
        var modelName = Source1Paths.ToModelPath(path);
        var textureSource = GetTextureSource(source);
        var boneToModel = BuildRestTransforms(source);
        var materialPathsByBlend = new string[BlendCount][];

        for (var blend = 0; blend < BlendCount; blend++)
        {
            materialPathsByBlend[blend] = GetMaterialPaths(path, textureSource, (GoldSrcStudioBlend)blend);
        }

        var materialPaths = materialPathsByBlend[(int)GoldSrcStudioBlend.Opaque];
        var meshGroupNames = new List<string>();
        var meshNames = new ModelNameSet();
        var meshes = new List<ModelMeshSource>();
        var sourceMeshes = new List<GoldSrcMdlMesh>();
        var defaultMask = 0UL;

        for (var partIndex = 0; partIndex < source.BodyParts.Length; partIndex++)
        {
            var part = source.BodyParts[partIndex];

            for (var subIndex = 0; subIndex < part.Models.Length; subIndex++)
            {
                var subModel = part.Models[subIndex];
                var groupIndex = meshGroupNames.Count;
                var groupName = string.Create(CultureInfo.InvariantCulture, $"{part.Name}_@{subIndex}_#&{subModel.Name}");
                meshGroupNames.Add(groupName);

                var inGroups = groupIndex < MaxMeshGroups;
                var groupBit = inGroups ? 1UL << groupIndex : 0UL;

                if (groupIndex == MaxMeshGroups)
                {
                    logger.LogWarning("Model {Model} has more than {Count} body part models, so the ones past them are never drawn", path, MaxMeshGroups);
                }

                if (subIndex == 0)
                {
                    defaultMask |= groupBit;
                }

                var meshName = meshNames.Create(part.Name, partIndex, string.Create(CultureInfo.InvariantCulture, $"_{subIndex}"));
                var mesh = CreateMesh(source, textureSource, meshName, subModel, groupBit, boneToModel, materialPaths, sourceMeshes);

                if (mesh != null)
                {
                    meshes.Add(mesh);
                }
            }
        }

        var modelData = KVObject.Collection();
        modelData.Add("m_attachments", CreateAttachments(source));
        modelData.Add("m_hitboxsets", CreateHitboxSets(source));

        var materialGroups = CreateMaterialGroups(textureSource, sourceMeshes, materialPathsByBlend);
        var animations = GoldSrcAnimationBaker.Bake(source, out var sequenceNames);
#pragma warning disable CA2000 // The model keeps its resource, which holds no unmanaged state
        var model = (S2Model)S2Model.Create(modelName, meshes, CreateBones(source), materialGroups, meshGroupNames, defaultMask, modelData, animations).DataBlock!;
#pragma warning restore CA2000
        model.Resource.FileName = modelName;
        Source1ResourceReferences.Update(model.Resource);

        return new GoldSrcConvertedModel(model, CreateFiles(path, textureSource), sequenceNames);
    }

    // The model that holds the textures of a model
    private static GoldSrcMdlFile GetTextureSource(GoldSrcMdlFile source)
        => source.HasTextures ? source : source.TextureFile ?? source;

    private static Matrix4x4[] BuildRestTransforms(GoldSrcMdlFile source)
    {
        var bones = source.Bones;
        var boneToModel = new Matrix4x4[bones.Length];

        for (var i = 0; i < bones.Length; i++)
        {
            var value = bones[i].Value;
            var local = Matrix4x4.CreateFromQuaternion(GetRestRotation(bones[i]));
            local.Translation = new Vector3(value[0], value[1], value[2]);

            var parent = bones[i].Parent;
            boneToModel[i] = (uint)parent < (uint)i ? local * boneToModel[parent] : local;
        }

        return boneToModel;
    }

    private static Quaternion GetRestRotation(GoldSrcBone bone)
        => GoldSrcMdlFile.EulerToQuaternion(bone.Value[3], bone.Value[4], bone.Value[5]);

    private static List<ModelBone> CreateBones(GoldSrcMdlFile source)
    {
        var bones = new List<ModelBone>(source.Bones.Length);

        foreach (var bone in source.Bones)
        {
            var value = bone.Value;
            var position = new Vector3(value[0], value[1], value[2]);

            bones.Add(new ModelBone(bone.ExportName, bone.Parent, position, GetRestRotation(bone), 1f));
        }

        return bones;
    }

    private static ModelMeshSource? CreateMesh(
        GoldSrcMdlFile source,
        GoldSrcMdlFile textureSource,
        string name,
        GoldSrcMdlSubmodel subModel,
        ulong groupMask,
        Matrix4x4[] boneToModel,
        string[] materialPaths,
        List<GoldSrcMdlMesh> drawnMeshes)
    {
        var vertices = new List<ConvertedVertex>();
        var indices = new List<uint>();
        var drawCalls = new List<MeshDrawCall>();
        var boneCount = 0;

        // The meshes of a sub-model share one vertex buffer and one index buffer, each drawn from its own range
        foreach (var sourceMesh in subModel.Meshes)
        {
            if (sourceMesh.Indices.Length == 0)
            {
                continue;
            }

            var textureIndex = GetTextureIndex(textureSource, sourceMesh, 0);
            var materialPath = GetMaterialPath(materialPaths, textureIndex);

            drawCalls.Add(new MeshDrawCall(materialPath, vertices.Count, sourceMesh.Vertices.Length, indices.Count, sourceMesh.Indices.Length));
            var texelScale = GetTexelScale(textureSource, textureIndex);
            vertices.AddRange(CreateVertices(subModel, sourceMesh, boneToModel, texelScale));
            drawnMeshes.Add(sourceMesh);

            foreach (var index in sourceMesh.Indices)
            {
                indices.Add((uint)index);
            }

            boneCount = Math.Max(boneCount, GetBoneCount(subModel, sourceMesh));
        }

        if (drawCalls.Count == 0)
        {
            return null;
        }

        var vertexArray = vertices.ToArray();
        var (min, max) = Source1VertexBuffers.GetBounds(vertexArray, static vertex => vertex.Position);
        var buffers = Source1VertexBuffers.Create(vertexArray, VertexFields, indices.ToArray());

        var meshBones = new List<MeshBone>(boneCount);
        var boneRemap = new int[boneCount];

        for (var i = 0; i < boneCount; i++)
        {
            var bone = source.Bones[i];
            var parentName = (uint)bone.Parent < (uint)source.Bones.Length ? source.Bones[bone.Parent].ExportName : string.Empty;
            var inverse = Matrix4x4.Invert(boneToModel[i], out var invertedRest) ? invertedRest : Matrix4x4.Identity;

            meshBones.Add(new MeshBone(bone.ExportName, parentName, inverse));
            boneRemap[i] = i;
        }

        return new ModelMeshSource(name, buffers, drawCalls, min, max)
        {
            MeshGroupMask = groupMask,
            LodMask = 1,
            Bones = meshBones,
            BoneRemap = boneRemap,
            BoneWeightCount = 1,
        };
    }

    private static Vector2 GetTexelScale(GoldSrcMdlFile textureSource, int textureIndex)
    {
        if ((uint)textureIndex >= (uint)textureSource.Textures.Length)
        {
            return Vector2.One;
        }

        var texture = textureSource.Textures[textureIndex];

        return new Vector2(1f / texture.Width, 1f / texture.Height);
    }

    // The coordinates of a vertex are in texels in the file. They are stored normalized by the size of the texture of
    // the default skin, so depth passes that sample the texture with them find the texels of the surface.
    private static ConvertedVertex[] CreateVertices(GoldSrcMdlSubmodel subModel, GoldSrcMdlMesh mesh, Matrix4x4[] boneToModel, Vector2 texelScale)
    {
        var vertices = new ConvertedVertex[mesh.Vertices.Length];

        for (var i = 0; i < vertices.Length; i++)
        {
            var source = mesh.Vertices[i];
            var positionBone = subModel.PositionBones[source.Position];
            var normalBone = subModel.NormalBones[source.Normal];

            var position = Vector3.Transform(subModel.Positions[source.Position], boneToModel[positionBone]);
            var normal = Vector3.Normalize(Vector3.TransformNormal(subModel.Normals[source.Normal], boneToModel[normalBone]));

            vertices[i] = new ConvertedVertex
            {
                Position = position,
                Normal = normal,
                TexCoord = new Vector2(source.S, source.T) * texelScale,
                ChromeOrigin = boneToModel[normalBone].Translation,
                BoneIndices = positionBone | ((uint)normalBone << 8),
            };
        }

        return vertices;
    }

    // Both the bone of the position and the bone of the normal of a vertex have to be skinned
    private static int GetBoneCount(GoldSrcMdlSubmodel subModel, GoldSrcMdlMesh mesh)
    {
        var highest = 0;

        foreach (var vertex in mesh.Vertices)
        {
            highest = Math.Max(highest, subModel.PositionBones[vertex.Position]);
            highest = Math.Max(highest, subModel.NormalBones[vertex.Normal]);
        }

        return highest + 1;
    }

    private static int GetTextureIndex(GoldSrcMdlFile textureSource, GoldSrcMdlMesh mesh, int skin)
    {
        var families = textureSource.SkinFamilies;

        if (skin < families.Length && mesh.SkinRef < families[skin].Length)
        {
            return families[skin][mesh.SkinRef];
        }

        return mesh.SkinRef;
    }

    // A group for every skin family and blend. The materials of a group are listed in the order of the default
    // materials, which are the ones the meshes draw with.
    private static List<ModelMaterialGroup> CreateMaterialGroups(GoldSrcMdlFile textureSource, List<GoldSrcMdlMesh> meshes, string[][] materialPathsByBlend)
    {
        var defaultMaterials = new List<string>();
        var firstMeshOfMaterial = new Dictionary<string, GoldSrcMdlMesh>(StringComparer.OrdinalIgnoreCase);
        var defaultPaths = materialPathsByBlend[(int)GoldSrcStudioBlend.Opaque];

        foreach (var mesh in meshes)
        {
            var path = GetMaterialPath(defaultPaths, GetTextureIndex(textureSource, mesh, 0));

            if (firstMeshOfMaterial.TryAdd(path, mesh))
            {
                defaultMaterials.Add(path);
            }
        }

        var skinCount = Math.Max(1, textureSource.SkinFamilies.Length);
        var groups = new List<ModelMaterialGroup>(skinCount * BlendCount);

        for (var blend = 0; blend < BlendCount; blend++)
        {
            var materialPaths = materialPathsByBlend[blend];

            for (var skin = 0; skin < skinCount; skin++)
            {
                var materials = new List<string>(defaultMaterials.Count);

                foreach (var defaultMaterial in defaultMaterials)
                {
                    var mesh = firstMeshOfMaterial[defaultMaterial];
                    materials.Add(GetMaterialPath(materialPaths, GetTextureIndex(textureSource, mesh, skin)));
                }

                groups.Add(new ModelMaterialGroup(GetSkinName(skin, (GoldSrcStudioBlend)blend), materials));
            }
        }

        return groups;
    }

    /// <summary>Gets the name of the material group that a converted model has for a skin family drawn with a blend.</summary>
    internal static string GetSkinName(int skin, GoldSrcStudioBlend blend)
    {
        var name = Source1ModelConverter.GetSkinName(skin);

        return blend switch
        {
            GoldSrcStudioBlend.Translucent => name + "_translucent",
            GoldSrcStudioBlend.Additive => name + "_additive",
            _ => name,
        };
    }

    private static string GetMaterialPath(string[] materialPaths, int textureIndex)
        => (uint)textureIndex < (uint)materialPaths.Length ? materialPaths[textureIndex] : string.Empty;

    // The model path keeps textures of the same name in different models apart
    private static string[] GetMaterialPaths(string path, GoldSrcMdlFile textureSource, GoldSrcStudioBlend blend)
    {
        var paths = new string[textureSource.Textures.Length];

        for (var i = 0; i < paths.Length; i++)
        {
            paths[i] = GetMaterialFilePath(path, i, textureSource.Textures[i], blend);
        }

        return paths;
    }

    private static string GetMaterialFilePath(string path, int textureIndex, GoldSrcMdlTexture texture, GoldSrcStudioBlend blend)
    {
        var extension = blend switch
        {
            GoldSrcStudioBlend.Translucent => "_translucent.vmat",
            GoldSrcStudioBlend.Additive => "_additive.vmat",
            _ => ".vmat",
        };

        return GetFilePath(path, textureIndex, texture, extension);
    }

    private static string GetFilePath(string path, int textureIndex, GoldSrcMdlTexture texture, string extension)
        => string.Create(CultureInfo.InvariantCulture, $"materials/{Path.ChangeExtension(path, null)}/{textureIndex}_{ModelNames.Sanitize(texture.Name)}{extension}");

    private static Dictionary<string, Resource> CreateFiles(string path, GoldSrcMdlFile textureSource)
    {
        var files = new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < textureSource.Textures.Length; i++)
        {
            var texture = textureSource.Textures[i];
            var texturePath = GetFilePath(path, i, texture, ".vtex");
            var textureResource = ConvertTexture(textureSource, texture, texturePath);

            var hasTexture = textureResource != null;

            if (textureResource != null)
            {
                files[texturePath + GameFileLoader.CompiledFileSuffix] = textureResource;
            }

            for (var blend = 0; blend < BlendCount; blend++)
            {
                var materialPath = GetMaterialFilePath(path, i, texture, (GoldSrcStudioBlend)blend);
                files[materialPath + GameFileLoader.CompiledFileSuffix] = CreateMaterial(materialPath, texturePath, texture.Flags, (GoldSrcStudioBlend)blend, hasTexture);
            }
        }

        return files;
    }

    private static Resource? ConvertTexture(GoldSrcMdlFile textureSource, GoldSrcMdlTexture texture, string texturePath)
    {
        PalettedImage image;

        try
        {
            image = textureSource.ReadTextureImage(texture);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        var isMasked = texture.Flags.HasFlag(GoldSrcTextureFlags.Masked);
        var generateMips = !texture.Flags.HasFlag(GoldSrcTextureFlags.NoMips);
        var resource = GoldSrcTextureConverter.Convert(image, isMasked, generateMips);
        resource.FileName = texturePath;

        return resource;
    }

    private static Resource CreateMaterial(string materialPath, string texturePath, GoldSrcTextureFlags flags, GoldSrcStudioBlend blend, bool hasTexture)
    {
        var isAdditive = blend == GoldSrcStudioBlend.Additive || flags.HasFlag(GoldSrcTextureFlags.Additive);
        var isBlended = blend != GoldSrcStudioBlend.Opaque;

        var material = new S2Material
        {
            Resource = new Resource(ResourceType.Material),
            Name = materialPath,
            ShaderName = ShaderName,
        };

        material.IntParams["F_ALPHA_TEST"] = flags.HasFlag(GoldSrcTextureFlags.Masked) ? 1 : 0;
        material.IntParams["F_TRANSLUCENT"] = blend == GoldSrcStudioBlend.Translucent ? 1 : 0;
        material.IntParams["F_ADDITIVE_BLEND"] = isAdditive ? 1 : 0;
        material.IntParams["F_UNLIT"] = blend == GoldSrcStudioBlend.Additive ? 1 : 0;
        material.IntParams["F_NO_Z_PREPASS"] = isBlended ? 1 : 0;
        material.IntParams["F_CHROME"] = flags.HasFlag(GoldSrcTextureFlags.Chrome) ? 1 : 0;
        material.IntParams["F_FLAT_SHADE"] = flags.HasFlag(GoldSrcTextureFlags.FlatShade) ? 1 : 0;

        if (hasTexture)
        {
            material.TextureParams["g_tColor"] = texturePath;
        }

        var resource = material.Resource;
        resource.FileName = materialPath;
        material.BuildData();
        resource.AddBlock(material);
        Source1ResourceReferences.Update(resource);

        return resource;
    }

    // Attachments are given in the space of their bone, which the animated pose of a bone carries into model space
    private static KVObject CreateAttachments(GoldSrcMdlFile source)
    {
        var attachments = KVObject.Array();
        var names = new ModelNameSet();

        for (var i = 0; i < source.Attachments.Length; i++)
        {
            var sourceAttachment = source.Attachments[i];

            if ((uint)sourceAttachment.Bone >= (uint)source.Bones.Length)
            {
                continue;
            }

            // The file does not name its attachments, the game addresses them by index
            var sourceName = sourceAttachment.Name.Length > 0 ? sourceAttachment.Name : string.Create(CultureInfo.InvariantCulture, $"attachment{i}");

            var boneName = KVHelpers.MakeArray(source.Bones[sourceAttachment.Bone].ExportName);
            var rotation = KVHelpers.MakeArray(KVHelpers.ToKVArray(Quaternion.Identity));
            var offset = KVHelpers.MakeArray(KVHelpers.ToKVArray(sourceAttachment.Origin));
            var weight = KVHelpers.MakeArray(1f);
            var rootTransform = KVHelpers.MakeArray(false);

            var attachment = KVObject.Collection();
            attachment.Add("m_name", names.Create(sourceName));
            attachment.Add("m_bIgnoreRotation", false);
            attachment.Add("m_nInfluences", 1);
            attachment.Add("m_influenceNames", boneName);
            attachment.Add("m_vInfluenceRotations", rotation);
            attachment.Add("m_vInfluenceOffsets", offset);
            attachment.Add("m_influenceWeights", weight);
            attachment.Add("m_bInfluenceRootTransform", rootTransform);

            attachments.Add(attachment);
        }

        return attachments;
    }

    private static KVObject CreateHitboxSets(GoldSrcMdlFile source)
    {
        var sets = KVObject.Array();

        if (source.Hitboxes.Length == 0)
        {
            return sets;
        }

        var hitboxes = KVObject.Array();

        for (var i = 0; i < source.Hitboxes.Length; i++)
        {
            var sourceHitbox = source.Hitboxes[i];

            if ((uint)sourceHitbox.Bone >= (uint)source.Bones.Length)
            {
                continue;
            }

            var hitbox = KVObject.Collection();
            hitbox.Add("m_name", string.Create(CultureInfo.InvariantCulture, $"hitbox{i}"));
            hitbox.Add("m_sSurfaceProperty", string.Empty);
            hitbox.Add("m_sBoneName", source.Bones[sourceHitbox.Bone].ExportName);
            hitbox.Add("m_vMinBounds", KVHelpers.ToKVArray(sourceHitbox.Min));
            hitbox.Add("m_vMaxBounds", KVHelpers.ToKVArray(sourceHitbox.Max));
            hitbox.Add("m_flShapeRadius", 0f);
            hitbox.Add("m_nGroupId", sourceHitbox.Group);
            hitbox.Add("m_nShapeType", (int)Hitbox.HitboxShape.Box);
            hitbox.Add("m_bTranslationOnly", false);

            hitboxes.Add(hitbox);
        }

        var set = KVObject.Collection();
        set.Add("m_name", "default");
        set.Add("m_hitboxes", hitboxes);
        sets.Add(set);

        return sets;
    }
}
