using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ValveKeyValue;

namespace ValveResourceFormat.Source1.Model;

internal sealed partial class MdlFile
{
    public const int MinVersion = 44;
    public const int MaxVersion = 49;

    public Header Hdr { get; }
    public int Version => Hdr.Version;
    public int Checksum => Hdr.Checksum;
    public int Flags => Hdr.Flags;

    public string Name { get; }
    public string SurfaceProperty { get; }
    public KVObject? KeyValues { get; }
    public string[] IncludeModels { get; }

    public StudioBone[] Bones { get; }
    public string[] BoneNames { get; }
    public StudioBoneController[] BoneControllers { get; }
    public string[] BoneSurfaceProperties { get; }

    /// <summary>The one based index of the attachment the illumination position is relative to, or 0 for the model.</summary>
    public int IllumPositionAttachmentIndex { get; }

    public StudioAttachment[] Attachments { get; }
    public string[] AttachmentNames { get; }

    public string[] MaterialNames { get; }
    public string[] CdMaterials { get; }
    public short[][] SkinFamilies { get; }

    public StudioBodyPart[] BodyParts { get; }
    public string[] BodyPartNames { get; }
    public StudioModel[][] Models { get; }
    public StudioMesh[][][] Meshes { get; }
    public Source1Flex[][][][] MeshFlexes { get; }
    public Source1Eyeball[][][] Eyeballs { get; }
    public Source1Mouth? Mouth { get; }

    public string[] FlexDescNames { get; }
    public Source1FlexController[] FlexControllers { get; }
    public Source1FlexRule[] FlexRules { get; }
    public Source1BoneFlexDriver[] BoneFlexDrivers { get; }
    public List<Source1Eyelid> Eyelids { get; } = [];

    public IkChain[] IkChains { get; }

    public HitboxSet[] HitboxSets { get; }

    // Size of a vertex animation, followed by a wrinkle delta in a flex of the wrinkle type
    private const int VertexAnimSize = 16;
    private const int VertexAnimWrinkleSize = 18;
    private const byte VertexAnimTypeWrinkle = 1;

    // Scale of the fixed point wrinkle deltas when the header does not set one
    private const float DefaultVertexAnimFixedPointScale = 1f / 4096f;

    /// <summary>A two-bone chain that can be solved by inverse kinematics. The bones are -1 for a chain that does not have three links.</summary>
    internal readonly record struct IkChain(string Name, int Hip, int Knee, int Foot);

    internal readonly record struct HitboxSet(string Name, Hitbox[] Hitboxes);
    internal readonly record struct Hitbox(string Name, int Bone, int Group, Vector3 Min, Vector3 Max);

    public MdlFile(byte[] data)
    {
        Hdr = LumpReader<Header>.ReadLumpAt(data, 0, 1)[0];

        if (Hdr.Id != (int)Source1Magic.Model)
        {
            throw new UnexpectedMagicException("Not a MDL file", Hdr.Id, nameof(Hdr.Id));
        }

        if (Hdr.Version < MinVersion || Hdr.Version > MaxVersion)
        {
            throw new UnexpectedMagicException($"Unsupported MDL version, supported versions are {MinVersion} to {MaxVersion}", Hdr.Version, nameof(Hdr.Version));
        }

        var nameEnd = Array.IndexOf(Hdr._name, (byte)0);
        Name = Encoding.UTF8.GetString(Hdr._name, 0, nameEnd < 0 ? Hdr._name.Length : nameEnd);
        IncludeModels = ReadIncludeModels(data);
        SurfaceProperty = ReadOptionalString(data, Hdr.SurfacePropOffset);
        KeyValues = ReadKeyValues(data);

        MaterialNames = ReadMaterialNames(data);
        CdMaterials = ReadCdMaterials(data);
        SkinFamilies = ReadSkinFamilies(data);

        BodyParts = new StudioBodyPart[Hdr.NumBodyParts];
        BodyPartNames = new string[Hdr.NumBodyParts];
        Models = new StudioModel[Hdr.NumBodyParts][];
        Meshes = new StudioMesh[Hdr.NumBodyParts][][];
        MeshFlexes = new Source1Flex[Hdr.NumBodyParts][][][];
        Eyeballs = new Source1Eyeball[Hdr.NumBodyParts][][];
        ReadBodyParts(data);
        Mouth = ReadMouth(data);
        IllumPositionAttachmentIndex = ReadIllumPositionAttachmentIndex(data);

        FlexDescNames = ReadFlexDescNames(data);
        FlexControllers = ReadFlexControllers(data);
        FlexRules = ReadFlexRules(data);
        BoneFlexDrivers = ReadBoneFlexDrivers(data);

        Bones = LumpReader<StudioBone>.ReadLumpAt(data, Hdr.BoneIndex, Hdr.NumBones);
        BoneNames = new string[Bones.Length];
        BoneSurfaceProperties = new string[Bones.Length];

        var boneSize = Marshal.SizeOf<StudioBone>();

        for (var i = 0; i < Bones.Length; i++)
        {
            var start = Hdr.BoneIndex + ((long)i * boneSize);
            BoneNames[i] = ReadCString(data, start + Bones[i].NameIndex);

            var surface = ReadOptionalString(data, Bones[i].SurfacePropIndex == 0 ? 0 : start + Bones[i].SurfacePropIndex);
            BoneSurfaceProperties[i] = string.IsNullOrWhiteSpace(surface) ? SurfaceProperty : surface;
        }

        BoneControllers = LumpReader<StudioBoneController>.ReadLumpAt(data, Hdr.BoneControllerIndex, Hdr.NumBoneControllers);

        Attachments = LumpReader<StudioAttachment>.ReadLumpAt(data, Hdr.LocalAttachmentIndex, Hdr.NumLocalAttachments);
        AttachmentNames = new string[Attachments.Length];

        var attachmentSize = Marshal.SizeOf<StudioAttachment>();

        for (var i = 0; i < Attachments.Length; i++)
        {
            var start = Hdr.LocalAttachmentIndex + ((long)i * attachmentSize);
            AttachmentNames[i] = ReadCString(data, start + Attachments[i].NameIndex);
        }

        IkChains = ReadIkChains(data);
        HitboxSets = ReadHitboxSets(data);
    }

    public static string ReadCString(byte[] data, long offset)
    {
        if (offset < 0 || offset >= data.Length)
        {
            throw new InvalidDataException($"MDL string offset {offset} is out of range.");
        }

        var span = data.AsSpan((int)offset);
        var end = span.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? span : span[..end]);
    }

    private static string ReadOptionalString(byte[] data, long offset)
    {
        return offset == 0 ? string.Empty : ReadCString(data, offset);
    }

    private KVObject? ReadKeyValues(byte[] data)
    {
        if (Hdr.KeyValueSize <= 0)
        {
            return null;
        }

        var bytes = LumpReader<byte>.ReadLumpAt(data, Hdr.KeyValueOffset, Hdr.KeyValueSize);
        var end = Array.IndexOf(bytes, (byte)0);
        using var stream = new MemoryStream(bytes, 0, end < 0 ? bytes.Length : end);

        try
        {
            return KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
        }
        catch (KeyValueException)
        {
            return null;
        }
    }

    private string[] ReadIncludeModels(byte[] data)
    {
        var groups = LumpReader<ModelGroup>.ReadLumpAt(data, Hdr.IncludeModelOffset, Hdr.NumIncludeModels);
        var groupSize = Marshal.SizeOf<ModelGroup>();
        var names = new string[groups.Length];

        for (var i = 0; i < groups.Length; i++)
        {
            var start = Hdr.IncludeModelOffset + ((long)i * groupSize);
            names[i] = ReadCString(data, start + groups[i].NameIndex);
        }

        return names;
    }

    private string[] ReadMaterialNames(byte[] data)
    {
        var textures = LumpReader<StudioTexture>.ReadLumpAt(data, Hdr.TextureIndex, Hdr.NumTextures);
        var textureSize = Marshal.SizeOf<StudioTexture>();
        var names = new string[textures.Length];

        for (var i = 0; i < textures.Length; i++)
        {
            var start = Hdr.TextureIndex + ((long)i * textureSize);
            names[i] = ReadCString(data, start + textures[i].NameIndex).Replace('\\', '/');
        }

        return names;
    }

    private string[] ReadCdMaterials(byte[] data)
    {
        var offsets = LumpReader<int>.ReadLumpAt(data, Hdr.CdTextureIndex, Hdr.NumCdTextures);
        var paths = new string[offsets.Length];

        for (var i = 0; i < offsets.Length; i++)
        {
            paths[i] = ReadCString(data, offsets[i]).Replace('\\', '/');
        }

        return paths;
    }

    private short[][] ReadSkinFamilies(byte[] data)
    {
        var numRef = Hdr.NumSkinRef;
        var numFamilies = Hdr.NumSkinFamilies;

        if (numRef < 0 || numFamilies < 0 || (long)numRef * numFamilies > int.MaxValue)
        {
            throw new InvalidDataException("Invalid MDL skin table counts.");
        }

        var flat = LumpReader<short>.ReadLumpAt(data, Hdr.SkinIndex, numRef * numFamilies);
        var families = new short[numFamilies][];

        for (var family = 0; family < numFamilies; family++)
        {
            families[family] = flat.AsSpan(family * numRef, numRef).ToArray();
        }

        return families;
    }

    private void ReadBodyParts(byte[] data)
    {
        var parts = LumpReader<StudioBodyPart>.ReadLumpAt(data, Hdr.BodyPartIndex, Hdr.NumBodyParts);
        var partSize = Marshal.SizeOf<StudioBodyPart>();
        var modelSize = Marshal.SizeOf<StudioModel>();
        var meshSize = Marshal.SizeOf<StudioMesh>();

        for (var partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            var part = parts[partIndex];
            var partStart = Hdr.BodyPartIndex + ((long)partIndex * partSize);

            BodyParts[partIndex] = part;
            BodyPartNames[partIndex] = ReadCString(data, partStart + part.NameIndex);

            var modelsStart = partStart + part.ModelIndex;
            var models = LumpReader<StudioModel>.ReadLumpAt(data, modelsStart, part.NumModels);
            var meshes = new StudioMesh[models.Length][];
            var meshFlexes = new Source1Flex[models.Length][][];
            Eyeballs[partIndex] = new Source1Eyeball[models.Length][];

            for (var modelIndex = 0; modelIndex < models.Length; modelIndex++)
            {
                var modelStart = modelsStart + ((long)modelIndex * modelSize);
                var meshesStart = modelStart + models[modelIndex].MeshIndex;
                var modelMeshes = LumpReader<StudioMesh>.ReadLumpAt(data, meshesStart, models[modelIndex].NumMeshes);

                meshes[modelIndex] = modelMeshes;
                meshFlexes[modelIndex] = new Source1Flex[modelMeshes.Length][];

                var eyeballsStart = modelStart + models[modelIndex].EyeBallIndex;
                var eyeballs = LumpReader<StudioEyeball>.ReadLumpAt(data, eyeballsStart, models[modelIndex].NumEyeBalls);

                ReadEyelids(eyeballs);
                Eyeballs[partIndex][modelIndex] = ConvertEyeballs(eyeballs);

                for (var meshIndex = 0; meshIndex < modelMeshes.Length; meshIndex++)
                {
                    var meshStart = meshesStart + ((long)meshIndex * meshSize);
                    meshFlexes[modelIndex][meshIndex] = ReadMeshFlexes(data, meshStart, modelMeshes[meshIndex]);
                }
            }

            Models[partIndex] = models;
            Meshes[partIndex] = meshes;
            MeshFlexes[partIndex] = meshFlexes;
        }
    }

    private static Source1Eyeball[] ConvertEyeballs(StudioEyeball[] eyeballs)
    {
        var result = new Source1Eyeball[eyeballs.Length];

        for (var i = 0; i < result.Length; i++)
        {
            var eyeball = eyeballs[i];
            result[i] = new Source1Eyeball(eyeball.Bone, eyeball.Origin, eyeball.Up, eyeball.Forward, eyeball.ZOffset, eyeball.IrisScale);
        }

        return result;
    }

    // The second header holds the attachment index after the two fields of the source bone transforms
    private int ReadIllumPositionAttachmentIndex(byte[] data)
    {
        const int fieldOffset = 8;

        if (Hdr.StudioHdr2Offset <= 0 || (long)Hdr.StudioHdr2Offset + fieldOffset + sizeof(int) > data.Length)
        {
            return 0;
        }

        return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(Hdr.StudioHdr2Offset + fieldOffset));
    }

    // The second header ends the fields before it with the count and index of the bone flex drivers
    private Source1BoneFlexDriver[] ReadBoneFlexDrivers(byte[] data)
    {
        const int fieldOffset = 24;
        const int firstVersionWithDrivers = 49;

        if (Hdr.Version < firstVersionWithDrivers || Hdr.StudioHdr2Offset <= 0 || (long)Hdr.StudioHdr2Offset + fieldOffset + (2 * sizeof(int)) > data.Length)
        {
            return [];
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(Hdr.StudioHdr2Offset + fieldOffset));
        var index = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(Hdr.StudioHdr2Offset + fieldOffset + sizeof(int)));

        if (count <= 0 || index <= 0)
        {
            return [];
        }

        var driversStart = (long)Hdr.StudioHdr2Offset + index;
        var drivers = LumpReader<StudioBoneFlexDriver>.ReadLumpAt(data, driversStart, count);
        var driverSize = Marshal.SizeOf<StudioBoneFlexDriver>();
        var result = new Source1BoneFlexDriver[drivers.Length];

        for (var i = 0; i < drivers.Length; i++)
        {
            var driver = drivers[i];
            var controlsStart = driversStart + ((long)i * driverSize) + driver.ControlIndex;
            var controls = LumpReader<StudioBoneFlexDriverControl>.ReadLumpAt(data, controlsStart, driver.NumControls);
            var converted = new Source1BoneFlexControl[controls.Length];

            for (var j = 0; j < controls.Length; j++)
            {
                var control = controls[j];
                converted[j] = new Source1BoneFlexControl(control.Component, control.FlexController, control.Min, control.Max);
            }

            result[i] = new Source1BoneFlexDriver(driver.Bone, converted);
        }

        return result;
    }

    // The first mouth lights the teeth
    private Source1Mouth? ReadMouth(byte[] data)
    {
        if (Hdr.NumMouths <= 0)
        {
            return null;
        }

        var mouth = LumpReader<StudioMouth>.ReadLumpAt(data, Hdr.MouthOffset, 1)[0];

        return new Source1Mouth(mouth.Bone, mouth.Forward, mouth.FlexDesc);
    }

    // Every sub-model has its own copy of the eyeballs, so a lid is kept once
    private void ReadEyelids(StudioEyeball[] eyeballs)
    {
        foreach (var eyeball in eyeballs)
        {
            if (eyeball.NonFacs)
            {
                continue;
            }

            AddEyelid(eyeball.UpperLidFlexDesc, eyeball.Radius, eyeball.UpperFlexDescs, eyeball.UpperTargets);
            AddEyelid(eyeball.LowerLidFlexDesc, eyeball.Radius, eyeball.LowerFlexDescs, eyeball.LowerTargets);
        }
    }

    private void AddEyelid(int flexDesc, float radius, LidFlexDescs poses, Vector3 heights)
    {
        if (flexDesc < 0 || Eyelids.Exists(eyelid => eyelid.FlexDesc == flexDesc))
        {
            return;
        }

        Eyelids.Add(new Source1Eyelid(flexDesc, radius, poses.Raiser, poses.Neutral, poses.Lowerer, heights));
    }

    private Source1Flex[] ReadMeshFlexes(byte[] data, long meshStart, StudioMesh mesh)
    {
        var flexesStart = meshStart + mesh.FlexIndex;
        var flexes = LumpReader<StudioFlex>.ReadLumpAt(data, flexesStart, mesh.NumFlexes);
        var flexSize = Marshal.SizeOf<StudioFlex>();
        var result = new Source1Flex[flexes.Length];

        var fixedPointScale = (Hdr.Flags & (int)StudioFlags.VertAnimFixedPointScale) != 0
            ? Hdr.VertAnimFixedPointScale
            : DefaultVertexAnimFixedPointScale;

        for (var i = 0; i < flexes.Length; i++)
        {
            var flex = flexes[i];
            var isWrinkle = flex.VertexAnimType == VertexAnimTypeWrinkle;
            var animSize = isWrinkle ? VertexAnimWrinkleSize : VertexAnimSize;
            var animsStart = flexesStart + ((long)i * flexSize) + flex.VertexIndex;
            var anims = LumpReader<byte>.ReadLumpAt(data, animsStart, flex.NumVertices * animSize);
            var vertices = new Source1FlexVertex[flex.NumVertices];

            for (var j = 0; j < vertices.Length; j++)
            {
                var anim = anims.AsSpan(j * animSize, animSize);
                var wrinkle = 0f;

                if (isWrinkle)
                {
                    wrinkle = BinaryPrimitives.ReadInt16LittleEndian(anim[16..]) * fixedPointScale;
                }

                vertices[j] = new Source1FlexVertex(
                    BinaryPrimitives.ReadUInt16LittleEndian(anim),
                    anim[2] / 255f,
                    anim[3] / 255f,
                    ReadHalfVector(anim[4..]),
                    ReadHalfVector(anim[10..]),
                    wrinkle);
            }

            result[i] = new Source1Flex
            {
                FlexDesc = flex.FlexDesc,

                // Flex descriptor 0 cannot be the right side of a stereo flex, so 0 means there is none
                FlexPair = flex.FlexPair == 0 ? -1 : flex.FlexPair,
                Targets = flex.Targets,
                Vertices = vertices,
            };
        }

        return result;
    }

    private static Vector3 ReadHalfVector(ReadOnlySpan<byte> data)
    {
        return new Vector3(
            (float)BinaryPrimitives.ReadHalfLittleEndian(data),
            (float)BinaryPrimitives.ReadHalfLittleEndian(data[2..]),
            (float)BinaryPrimitives.ReadHalfLittleEndian(data[4..]));
    }

    private string[] ReadFlexDescNames(byte[] data)
    {
        var offsets = LumpReader<int>.ReadLumpAt(data, Hdr.FlexDescOffset, Hdr.NumFlexDesc);
        var names = new string[offsets.Length];

        for (var i = 0; i < offsets.Length; i++)
        {
            var start = Hdr.FlexDescOffset + ((long)i * sizeof(int));
            names[i] = ReadCString(data, start + offsets[i]);
        }

        return names;
    }

    private Source1FlexController[] ReadFlexControllers(byte[] data)
    {
        var controllers = LumpReader<StudioFlexController>.ReadLumpAt(data, Hdr.FlexControllerOffset, Hdr.NumFlexControllers);
        var controllerSize = Marshal.SizeOf<StudioFlexController>();
        var result = new Source1FlexController[controllers.Length];

        for (var i = 0; i < controllers.Length; i++)
        {
            var controller = controllers[i];
            var start = Hdr.FlexControllerOffset + ((long)i * controllerSize);
            var type = ReadCString(data, start + controller.TypeIndex);
            var name = ReadCString(data, start + controller.NameIndex);

            result[i] = new Source1FlexController(name, type, controller.Min, controller.Max);
        }

        return result;
    }

    private Source1FlexRule[] ReadFlexRules(byte[] data)
    {
        var rules = LumpReader<StudioFlexRule>.ReadLumpAt(data, Hdr.FlexRuleOffset, Hdr.NumFlexRules);
        var ruleSize = Marshal.SizeOf<StudioFlexRule>();
        var result = new Source1FlexRule[rules.Length];

        for (var i = 0; i < rules.Length; i++)
        {
            var rule = rules[i];
            var start = Hdr.FlexRuleOffset + ((long)i * ruleSize);
            var ops = LumpReader<StudioFlexOp>.ReadLumpAt(data, start + rule.OpIndex, rule.NumOps);

            result[i] = new Source1FlexRule(rule.Flex, Array.ConvertAll(ops, op => new Source1FlexOp(op.Op, op.Data)));
        }

        return result;
    }

    private IkChain[] ReadIkChains(byte[] data)
    {
        var headers = LumpReader<StudioIkChain>.ReadLumpAt(data, Hdr.IkChainOffset, Hdr.NumIkChains);
        var headerSize = Marshal.SizeOf<StudioIkChain>();
        var chains = new IkChain[headers.Length];

        for (var i = 0; i < headers.Length; i++)
        {
            var start = Hdr.IkChainOffset + ((long)i * headerSize);
            var name = ReadCString(data, start + headers[i].NameIndex);

            if (headers[i].NumLinks != 3)
            {
                chains[i] = new IkChain(name, -1, -1, -1);
                continue;
            }

            var links = LumpReader<StudioIkLink>.ReadLumpAt(data, start + headers[i].LinkIndex, 3);
            chains[i] = new IkChain(name, links[0].Bone, links[1].Bone, links[2].Bone);
        }

        return chains;
    }

    private HitboxSet[] ReadHitboxSets(byte[] data)
    {
        var headers = LumpReader<HitboxSetHeader>.ReadLumpAt(data, Hdr.HitBoxSetIndex, Hdr.NumHitBoxSets);
        var headerSize = Marshal.SizeOf<HitboxSetHeader>();
        var hitboxSize = Marshal.SizeOf<HitboxHeader>();
        var sets = new HitboxSet[headers.Length];

        for (var setIndex = 0; setIndex < headers.Length; setIndex++)
        {
            var header = headers[setIndex];
            var start = Hdr.HitBoxSetIndex + ((long)setIndex * headerSize);
            var name = ReadOptionalString(data, header.NameIndex == 0 ? 0 : start + header.NameIndex);
            var hitboxesStart = start + header.HitboxIndex;
            var raw = LumpReader<HitboxHeader>.ReadLumpAt(data, hitboxesStart, header.NumHitboxes);
            var hitboxes = new Hitbox[raw.Length];

            for (var i = 0; i < raw.Length; i++)
            {
                var hitboxName = string.Empty;

                if (Version != 44 && raw[i].NameIndex != 0)
                {
                    hitboxName = ReadCString(data, hitboxesStart + ((long)i * hitboxSize) + raw[i].NameIndex);
                }

                hitboxes[i] = new Hitbox(hitboxName, raw[i].Bone, raw[i].Group, raw[i].Min, raw[i].Max);
            }

            sets[setIndex] = new HitboxSet(name, hitboxes);
        }

        return sets;
    }
}
