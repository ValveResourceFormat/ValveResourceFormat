using System.IO;
using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

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

    /// <summary>Rule that computes the model-space transform of the bone, null for a bone placed by the animated pose.</summary>
    public Source1ProceduralBone? Procedural { get; init; }

    /// <summary>Spring simulation of the bone, null for a bone that is not a jiggle bone.</summary>
    public FeModel.FeJiggleBone? Jiggle { get; init; }

    /// <summary>Studio bone flags.</summary>
    public required int Flags { get; init; }

    /// <summary>Index of the physics bone, or -1.</summary>
    public required int PhysicsBone { get; init; }

    /// <summary>Surface property name of this bone.</summary>
    public required string SurfaceProperty { get; init; }

    /// <summary>Index of the bone controller that moves each channel of the bone (X, Y, Z, then the rotation about X, Y and Z), or -1.</summary>
    public required int[] Controllers { get; init; }
}

/// <summary>A value set from outside the animation that moves a channel of a bone.</summary>
/// <param name="Type">Studio flags of the channel the controller moves.</param>
/// <param name="Start">Value of the controller at a setting of zero, in degrees for a rotation.</param>
/// <param name="End">Value of the controller at a setting of one.</param>
/// <param name="InputField">Which input drives the controller. Controllers of the same input share a setting.</param>
internal readonly record struct Source1BoneController(int Type, float Start, float End, int InputField);

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

/// <summary>A mesh of a Source 1 sub-model at one level of detail, with one material.</summary>
internal sealed class Source1Mesh
{
    /// <summary>The level of detail the mesh belongs to, 0 being the most detailed.</summary>
    public required int Lod { get; init; }

    /// <summary>Deduplicated vertices.</summary>
    public required Source1Vertex[] Vertices { get; init; }

    /// <summary>Triangle list indices into <see cref="Vertices"/> with the winding already swapped to counter-clockwise.</summary>
    public required ushort[] Indices { get; init; }

    /// <summary>Index into the skin family rows, see <see cref="Source1Model.SkinFamilies"/>.</summary>
    public required int SkinRefIndex { get; init; }

    /// <summary>Index into <see cref="Source1Model.MaterialNames"/> for the default skin family.</summary>
    public required int MaterialIndex { get; init; }

    /// <summary>For each mesh vertex id, the index in <see cref="Vertices"/> of the vertex read from it, or -1 when no triangle uses the id.</summary>
    public required int[] OutputByOriginalVertexId { get; init; }

    /// <summary>For each strip group of the mesh, the mesh vertex id of each of its vertices.</summary>
    public required int[][] StripGroupVertexIds { get; init; }

    /// <summary>Position of the first strip group of the mesh among the strip groups of the whole model at the same level of detail.</summary>
    public required int FirstStripGroup { get; init; }

    /// <summary>The flexes that move vertices of the mesh.</summary>
    public Source1Flex[] Flexes { get; init; } = [];

    /// <summary>The eyeball the mesh draws, or <see langword="null"/> when it is not an eyeball.</summary>
    public Source1Eyeball? Eyeball { get; init; }
}

/// <summary>A flex controller, an input of the flex rules.</summary>
internal sealed record Source1FlexController(string Name, string Type, float Min, float Max);

/// <summary>A flex controller that a component of a bone's local position drives, from <paramref name="Min"/> to <paramref name="Max"/> mapped to 0 to 1.</summary>
/// <param name="Component">The component of the position, 0 to 2 for X to Z.</param>
/// <param name="FlexController">Index of the driven flex controller.</param>
/// <param name="Min">The value of the component that gives 0.</param>
/// <param name="Max">The value of the component that gives 1.</param>
internal readonly record struct Source1BoneFlexControl(int Component, int FlexController, float Min, float Max);

/// <summary>A bone that drives flex controllers from its local position.</summary>
internal sealed record Source1BoneFlexDriver(int Bone, Source1BoneFlexControl[] Controls);

/// <summary>An operation of a flex rule. <see cref="Data"/> is an index or the bits of a float, depending on the operation.</summary>
internal readonly record struct Source1FlexOp(int Code, int Data);

/// <summary>A rule that computes the weight of a flex descriptor from the flex controllers, as a stack program.</summary>
internal sealed record Source1FlexRule(int FlexDesc, Source1FlexOp[] Ops);

/// <summary>
/// An eyelid of an eyeball. Its flex descriptor has no rule: it takes the height of the lid on the eyeball, which the
/// weights of the raiser, neutral and lowerer descriptors blend between the heights of those poses.
/// </summary>
/// <param name="FlexDesc">The flex descriptor that the flexes of the lid follow.</param>
/// <param name="EyeballRadius">Radius of the eyeball.</param>
/// <param name="RaiserFlexDesc">The flex descriptor of the raised lid.</param>
/// <param name="NeutralFlexDesc">The flex descriptor of the neutral lid.</param>
/// <param name="LowererFlexDesc">The flex descriptor of the lowered lid.</param>
/// <param name="Heights">Height of the lid on the eyeball when raised (X), neutral (Y) and lowered (Z).</param>
internal sealed record Source1Eyelid(int FlexDesc, float EyeballRadius, int RaiserFlexDesc, int NeutralFlexDesc, int LowererFlexDesc, Vector3 Heights);

/// <summary>An eyeball of a sub-model. The vectors are in the space of the bone.</summary>
/// <param name="Bone">The bone the eyeball is attached to.</param>
/// <param name="Origin">The center of the eyeball.</param>
/// <param name="Up">The up direction of the eyeball.</param>
/// <param name="Forward">The direction the eyeball looks in at rest.</param>
/// <param name="ZOffset">How far the eyeball looks to the side of its target.</param>
/// <param name="IrisScale">The size of the iris texture relative to the eyeball.</param>
internal readonly record struct Source1Eyeball(int Bone, Vector3 Origin, Vector3 Up, Vector3 Forward, float ZOffset, float IrisScale);

/// <summary>The point the eyes of a model look at, carried by a bone the converter adds to the skeleton.</summary>
/// <param name="Index">The index of the target bone, which follows the bones of the model.</param>
/// <param name="Parent">The bone the target moves with.</param>
/// <param name="Position">The position of the target in the space of the converted vertices.</param>
/// <param name="EyeToBind">The frame of the eyes in the space of the converted vertices: forward on X, left on Y, up on Z, at the eye center. The bone that carries it follows the target bone.</param>
internal readonly record struct Source1EyeTarget(int Index, int Parent, Vector3 Position, Matrix4x4 EyeToBind);

/// <summary>The mouth that lights the teeth. The forward vector is in the space of the bone.</summary>
/// <param name="Bone">The bone the mouth is attached to.</param>
/// <param name="Forward">The direction the mouth faces.</param>
/// <param name="FlexDesc">The flex descriptor whose weight is how much the mouth is open.</param>
internal readonly record struct Source1Mouth(int Bone, Vector3 Forward, int FlexDesc);

/// <summary>How a flex moves one vertex of a mesh.</summary>
/// <param name="Index">Mesh vertex id, see <see cref="Source1Mesh.OutputByOriginalVertexId"/>.</param>
/// <param name="Speed">How much the vertex follows the current weight rather than the delayed one, from 0 to 1.</param>
/// <param name="Side">How much the vertex follows the weight of <see cref="Source1Flex.FlexPair"/> rather than of <see cref="Source1Flex.FlexDesc"/>, from 0 to 1.</param>
/// <param name="PositionDelta">Position offset at full weight.</param>
/// <param name="NormalDelta">Normal offset at full weight.</param>
/// <param name="WrinkleDelta">Wrinkle map blend at full weight, negative to compress and positive to stretch.</param>
internal readonly record struct Source1FlexVertex(int Index, float Speed, float Side, Vector3 PositionDelta, Vector3 NormalDelta, float WrinkleDelta);

/// <summary>The vertex offsets that a flex descriptor applies to one mesh.</summary>
internal sealed class Source1Flex
{
    /// <summary>The flex descriptor whose weight drives the flex.</summary>
    public required int FlexDesc { get; init; }

    /// <summary>The flex descriptor that drives the right side of a stereo flex, or -1 when the flex is not split.</summary>
    public required int FlexPair { get; init; }

    /// <summary>
    /// The descriptor weights at which the flex starts (X), reaches full strength (Y), starts to fade (Z) and is gone (W).
    /// </summary>
    public required Vector4 Targets { get; init; }

    /// <summary>The vertices the flex moves.</summary>
    public required Source1FlexVertex[] Vertices { get; init; }
}

/// <summary>A sub-model (one choice of a body group).</summary>
internal sealed class Source1SubModel
{
    /// <summary>Name of the sub-model.</summary>
    public required string Name { get; init; }

    /// <summary>For each level of detail, the meshes with geometry.</summary>
    public required Source1Mesh[][] Lods { get; init; }
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

/// <summary>A Source 1 model read from MDL, VVD, VTX and PHY files.</summary>
internal sealed partial class Source1Model
{
    // Size of a vertex in the VVD file, which the vertex index of an MDL model is a byte offset in
    private const int StudioVertexSize = 48;

    // The material type of a mesh that draws an eyeball
    private const int EyeballMaterialType = 1;

    /// <summary>MDL version.</summary>
    public int Version { get; private set; }

    /// <summary>MDL checksum.</summary>
    public int Checksum { get; private set; }

    /// <summary>Studio header flags.</summary>
    public int Flags { get; private set; }

    /// <summary>Eye position.</summary>
    public Vector3 EyePosition { get; private set; }

    /// <summary>Illumination position.</summary>
    public Vector3 IlluminationPosition { get; private set; }

    /// <summary>One based index of the attachment the illumination position is relative to, or 0 for the model.</summary>
    public int IlluminationAttachmentIndex { get; private set; }

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

    /// <summary>The $keyvalues block of the MDL, or <see langword="null"/> when it has none or cannot be parsed.</summary>
    public KVObject? KeyValues { get; private set; }

    /// <summary>Skeleton bones.</summary>
    public Source1Bone[] Bones { get; private set; } = [];

    /// <summary>Bone controllers.</summary>
    public Source1BoneController[] BoneControllers { get; private set; } = [];

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

    /// <summary>Body parts with the geometry of every level of detail.</summary>
    public Source1BodyPart[] BodyParts { get; private set; } = [];

    /// <summary>Names of the flex descriptors, which <see cref="FlexRules"/> give weights to.</summary>
    public string[] FlexDescNames { get; private set; } = [];

    /// <summary>Flex controllers.</summary>
    public Source1FlexController[] FlexControllers { get; private set; } = [];

    /// <summary>Flex rules, in the order they run.</summary>
    public Source1FlexRule[] FlexRules { get; private set; } = [];

    /// <summary>Bones whose local position drives flex controllers.</summary>
    public Source1BoneFlexDriver[] BoneFlexDrivers { get; private set; } = [];

    /// <summary>Eyelids whose flex descriptors follow the eyeballs, set after the flex rules run.</summary>
    public Source1Eyelid[] Eyelids { get; private set; } = [];

    /// <summary>The mouth that lights the teeth, or <see langword="null"/> when the model has none.</summary>
    public Source1Mouth? Mouth { get; private set; }

    /// <summary>Number of LODs that are drawn on screen, 0 when there is no geometry.</summary>
    public int NumLods { get; private set; }

    /// <summary>The value of the screen size metric (100 divided by the on-screen size of a unit sphere) from which each LOD is used.</summary>
    public float[] LodSwitchPoints { get; private set; } = [];

    /// <summary>The collision of the model, or <see langword="null"/> when there is no PHY file or it does not load.</summary>
    public PhyFile? Phy { get; private set; }

    /// <summary>Non-fatal problems found while loading, such as checksum mismatches.</summary>
    public List<string> Warnings { get; } = [];

    private static string StripModelExtension(string path)
        => path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;

    /// <summary>
    /// Loads a model.
    /// </summary>
    /// <param name="mdlPath">Path of the .mdl file as understood by <paramref name="openFile"/>.</param>
    /// <param name="openFile">Opens a file by path, returning null when it does not exist.</param>
    /// <remarks>A model without its VVD and VTX files, or whose VTX has an unsupported version, loads without geometry.</remarks>
    public static Source1Model Load(string mdlPath, Func<string, Stream?> openFile)
    {
        ArgumentNullException.ThrowIfNull(mdlPath);
        ArgumentNullException.ThrowIfNull(openFile);

        var basePath = StripModelExtension(mdlPath);

        var mdlData = ReadFile(openFile, basePath + ".mdl")
            ?? throw new FileNotFoundException("MDL file not found.", mdlPath);

        var mdl = new MdlFile(mdlData);
        var model = new Source1Model();
        model.ReadHeaderData(mdl, mdlData);
        model.SetAnimationSource(mdl, mdlData, basePath, openFile);

        var hasBodyParts = mdl.BodyParts.Length > 0;

        // A model that ships without its vertex files, such as one that only holds animations, has no geometry
        var vvdStream = OpenBuffered(openFile, basePath + ".vvd");
        var vtxStream = OpenFirst(openFile, basePath, ".dx90.vtx", ".vtx", ".dx80.vtx", ".sw.vtx");

        if (vvdStream != null && vtxStream != null && hasBodyParts)
        {
            var vvd = new VvdFile(vvdStream);
            VtxFile? vtx = null;

            try
            {
                vtx = new VtxFile(vtxStream, mdl, vvd);
            }
            catch (UnexpectedMagicException e)
            {
                model.Warnings.Add($"VTX failed to load, the model has no geometry: {e.Message}");
            }

            if (vtx != null)
            {
                if (vvd.Checksum != mdl.Checksum || vtx.Checksum != mdl.Checksum)
                {
                    model.Warnings.Add("MDL, VVD and VTX checksums do not match.");
                }

                model.BuildGeometry(mdl, vvd, vtx);
            }
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

                model.Phy = phy;
            }
            catch (Exception e) when (e is InvalidDataException or UnexpectedMagicException)
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
        if ((uint)materialIndex >= (uint)MaterialNames.Length)
        {
            throw new InvalidDataException($"Material index {materialIndex} is out of range.");
        }

        var name = MaterialNames[materialIndex];

        foreach (var cdMaterial in CdMaterials)
        {
            var directory = cdMaterial.Trim('/');
            var path = directory.Length == 0 ? name : $"{directory}/{name}";
            yield return $"materials/{path}.vmt";
        }
    }

    private static byte[]? ReadFile(Func<string, Stream?> openFile, string path)
    {
        using var stream = openFile(path);

        if (stream == null)
        {
            return null;
        }

        return Source1Streams.ReadAll(stream);
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

    private void ReadHeaderData(MdlFile mdl, byte[] mdlData)
    {
        var header = mdl.Hdr;

        Version = header.Version;
        Checksum = header.Checksum;
        Flags = header.Flags;
        EyePosition = header.EyePosition;
        IlluminationPosition = header.IllumPosition;
        IlluminationAttachmentIndex = mdl.IllumPositionAttachmentIndex;
        HullMin = header.HullMin;
        HullMax = header.HullMax;
        ViewMin = header.ViewBbMin;
        ViewMax = header.ViewBbMax;
        Mass = header.Mass;
        SurfaceProperty = mdl.SurfaceProperty;
        KeyValues = mdl.KeyValues;
        MaterialNames = mdl.MaterialNames;
        CdMaterials = mdl.CdMaterials;

        SkinFamilies = new int[mdl.SkinFamilies.Length][];

        for (var family = 0; family < SkinFamilies.Length; family++)
        {
            SkinFamilies[family] = Array.ConvertAll(mdl.SkinFamilies[family], m => (int)m);
        }

        FlexDescNames = mdl.FlexDescNames;
        FlexControllers = mdl.FlexControllers;
        FlexRules = mdl.FlexRules;
        BoneFlexDrivers = mdl.BoneFlexDrivers;
        Eyelids = [.. mdl.Eyelids];
        Mouth = mdl.Mouth;

        Bones = new Source1Bone[mdl.Bones.Length];

        var boneSize = Marshal.SizeOf<MdlFile.StudioBone>();

        for (var i = 0; i < Bones.Length; i++)
        {
            var bone = mdl.Bones[i];
            var boneOffset = mdl.Hdr.BoneIndex + ((long)i * boneSize);

            Bones[i] = new Source1Bone
            {
                Name = mdl.BoneNames[i],
                ParentIndex = bone.Parent,
                Position = bone.Position,
                Rotation = bone.Quaternion,
                PoseToBone = bone.PoseToBone.ToMatrix4x4(),
                Procedural = Source1ProceduralBone.Read(mdlData, boneOffset, bone),
                Jiggle = Source1ProceduralBone.ReadJiggle(mdlData, boneOffset, bone),
                Flags = bone.Flags,
                PhysicsBone = bone.PhysicsBone,
                SurfaceProperty = mdl.BoneSurfaceProperties[i],
                Controllers = [.. bone.BoneController],
            };
        }

        BoneControllers = new Source1BoneController[mdl.BoneControllers.Length];

        for (var i = 0; i < BoneControllers.Length; i++)
        {
            var controller = mdl.BoneControllers[i];
            BoneControllers[i] = new Source1BoneController(controller.Type, controller.Start, controller.End, controller.InputField);
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
        // A last LOD with a negative switch point is only drawn into shadows
        var drawnLods = vtx.NumLods;

        if (drawnLods > 1 && vtx.LodSwitchPoints[drawnLods - 1] < 0f)
        {
            drawnLods--;
        }

        NumLods = drawnLods;
        LodSwitchPoints = vtx.LodSwitchPoints[..drawnLods];

        // Every level of detail reads the vertices of the most detailed one, which come first in each mesh
        var vertexIndices = vvd.GetVertexIndices(0);
        BodyParts = new Source1BodyPart[mdl.BodyParts.Length];

        var stripGroupCountByLod = new int[NumLods];

        for (var partIndex = 0; partIndex < BodyParts.Length; partIndex++)
        {
            var mdlModels = mdl.Models[partIndex];
            var models = new Source1SubModel[mdlModels.Length];

            for (var modelIndex = 0; modelIndex < models.Length; modelIndex++)
            {
                var mdlMeshes = mdl.Meshes[partIndex][modelIndex];
                var modelVertexBase = mdlModels[modelIndex].VertexIndex / StudioVertexSize;
                var lods = new Source1Mesh[NumLods][];

                for (var lod = 0; lod < NumLods; lod++)
                {
                    var vtxMeshes = vtx.Meshes[partIndex][modelIndex][lod];
                    var meshes = new List<Source1Mesh>();

                    for (var meshIndex = 0; meshIndex < mdlMeshes.Length && meshIndex < vtxMeshes.Length; meshIndex++)
                    {
                        var meshVertexBase = modelVertexBase + mdlMeshes[meshIndex].VertexOffset;
                        var flexes = mdl.MeshFlexes[partIndex][modelIndex][meshIndex];
                        var eyeball = GetEyeball(mdlMeshes[meshIndex], mdl.Eyeballs[partIndex][modelIndex]);
                        var mesh = BuildMesh(mdl, vvd, vertexIndices, mdlMeshes[meshIndex], vtxMeshes[meshIndex], lod, meshVertexBase, stripGroupCountByLod[lod], flexes, eyeball);

                        if (mesh != null)
                        {
                            meshes.Add(mesh);
                        }

                        stripGroupCountByLod[lod] += vtxMeshes[meshIndex].StripGroupVertexIds.Length;
                    }

                    lods[lod] = meshes.ToArray();
                }

                models[modelIndex] = new Source1SubModel { Name = mdlModels[modelIndex].Name, Lods = lods };
            }

            BodyParts[partIndex] = new Source1BodyPart
            {
                Name = mdl.BodyPartNames[partIndex],
                Base = mdl.BodyParts[partIndex].Base,
                Models = models,
            };
        }
    }

    // A mesh of the eyeball material type draws the eyeball its material parameter names
    private static Source1Eyeball? GetEyeball(MdlFile.StudioMesh mdlMesh, Source1Eyeball[] eyeballs)
    {
        if (mdlMesh.MaterialType != EyeballMaterialType || (uint)mdlMesh.MaterialParam >= (uint)eyeballs.Length)
        {
            return null;
        }

        return eyeballs[mdlMesh.MaterialParam];
    }

    private static Source1Mesh? BuildMesh(MdlFile mdl, VvdFile vvd, int[]? vertexIndices, MdlFile.StudioMesh mdlMesh, VtxFile.MeshData data, int lod, int meshVertexBase, int firstStripGroup, Source1Flex[] flexes, Source1Eyeball? eyeball)
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
        var vertexIdLimit = vertexIndices?.Length ?? vvd.Vertices.Length;

        for (var i = 0; i < vertices.Length; i++)
        {
            var vertexId = meshVertexBase + sourceIds[i];

            if ((uint)vertexId >= (uint)vertexIdLimit)
            {
                throw new InvalidDataException("MDL mesh vertex is outside the VVD vertices.");
            }

            var source = vertexIndices != null ? vertexIndices[vertexId] : vertexId;

            if ((uint)source >= (uint)vvd.Vertices.Length || (vvd.Tangents != null && (uint)source >= (uint)vvd.Tangents.Length))
            {
                throw new InvalidDataException("VVD fixup vertex is outside the VVD vertices.");
            }

            vertices[i] = ConvertVertex(vvd.Vertices[source], vvd.Tangents is null ? default : vvd.Tangents[source], mdl.Bones.Length);
        }

        var defaultSkin = mdl.SkinFamilies.Length > 0 ? mdl.SkinFamilies[0] : null;

        if (defaultSkin != null && (uint)mdlMesh.Material >= (uint)defaultSkin.Length)
        {
            throw new InvalidDataException($"MDL mesh skin reference {mdlMesh.Material} is out of range.");
        }

        var materialIndex = defaultSkin != null ? defaultSkin[mdlMesh.Material] : mdlMesh.Material;

        if ((uint)materialIndex >= (uint)mdl.MaterialNames.Length)
        {
            throw new InvalidDataException($"MDL material index {materialIndex} is out of range.");
        }

        return new Source1Mesh
        {
            Lod = lod,
            Vertices = vertices,
            Indices = indices,
            SkinRefIndex = mdlMesh.Material,
            MaterialIndex = materialIndex,
            OutputByOriginalVertexId = originalToOutput,
            StripGroupVertexIds = data.StripGroupVertexIds,
            FirstStripGroup = firstStripGroup,
            Flexes = flexes,
            Eyeball = eyeball,
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
