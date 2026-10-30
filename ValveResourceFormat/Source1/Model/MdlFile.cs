using System.IO;
using System.Runtime.InteropServices;
using System.Text;

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
    public string KeyValuesText { get; }
    public string[] IncludeModels { get; }

    public StudioBone[] Bones { get; }
    public string[] BoneNames { get; }
    public string[] BoneSurfaceProperties { get; }

    public StudioAttachment[] Attachments { get; }
    public string[] AttachmentNames { get; }

    public string[] MaterialNames { get; }
    public string[] CdMaterials { get; }
    public short[][] SkinFamilies { get; }

    public StudioBodyPart[] BodyParts { get; }
    public string[] BodyPartNames { get; }
    public StudioModel[][] Models { get; }
    public StudioMesh[][][] Meshes { get; }

    public HitboxSet[] HitboxSets { get; }

    internal readonly record struct HitboxSet(string Name, Hitbox[] Hitboxes);
    internal readonly record struct Hitbox(string Name, int Bone, int Group, Vector3 Min, Vector3 Max);

    public MdlFile(byte[] data)
    {
        Hdr = LumpReader<Header>.ReadLumpAt(data, 0, 1)[0];

        if (Hdr.Id != 0x54534449)
        {
            throw new InvalidDataException("Not a MDL file.");
        }

        if (Hdr.Version < MinVersion || Hdr.Version > MaxVersion)
        {
            throw new InvalidDataException($"Unsupported MDL version {Hdr.Version}.");
        }

        var nameEnd = Array.IndexOf(Hdr._name, (byte)0);
        Name = Encoding.UTF8.GetString(Hdr._name, 0, nameEnd < 0 ? Hdr._name.Length : nameEnd);
        IncludeModels = ReadIncludeModels(data);
        SurfaceProperty = ReadOptionalString(data, Hdr.SurfacePropOffset);
        KeyValuesText = ReadKeyValuesText(data);

        MaterialNames = ReadMaterialNames(data);
        CdMaterials = ReadCdMaterials(data);
        SkinFamilies = ReadSkinFamilies(data);

        BodyParts = new StudioBodyPart[Hdr.NumBodyParts];
        BodyPartNames = new string[Hdr.NumBodyParts];
        Models = new StudioModel[Hdr.NumBodyParts][];
        Meshes = new StudioMesh[Hdr.NumBodyParts][][];
        MeshFlexes = new MeshFlex[Hdr.NumBodyParts][][][];
        Eyeballs = new StudioEyeball[Hdr.NumBodyParts][][];
        ReadBodyParts(data);
        ReadFlexData(data);
        ReadMaxEyeDeflection(data);

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

        Attachments = LumpReader<StudioAttachment>.ReadLumpAt(data, Hdr.LocalAttachmentIndex, Hdr.NumLocalAttachments);
        AttachmentNames = new string[Attachments.Length];

        var attachmentSize = Marshal.SizeOf<StudioAttachment>();

        for (var i = 0; i < Attachments.Length; i++)
        {
            var start = Hdr.LocalAttachmentIndex + ((long)i * attachmentSize);
            AttachmentNames[i] = ReadCString(data, start + Attachments[i].NameIndex);
        }

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

    private string ReadKeyValuesText(byte[] data)
    {
        // TODO: parse into a KeyValues tree once a shared parser exists
        if (Hdr.KeyValueSize <= 0)
        {
            return string.Empty;
        }

        var bytes = LumpReader<byte>.ReadLumpAt(data, Hdr.KeyValueOffset, Hdr.KeyValueSize);
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
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
            var flexes = new MeshFlex[models.Length][][];
            var eyeballs = new StudioEyeball[models.Length][];

            for (var modelIndex = 0; modelIndex < models.Length; modelIndex++)
            {
                var modelStart = modelsStart + ((long)modelIndex * modelSize);
                eyeballs[modelIndex] = ReadEyeballs(data, modelStart, models[modelIndex]);
                var meshesStart = modelStart + models[modelIndex].MeshIndex;
                meshes[modelIndex] = LumpReader<StudioMesh>.ReadLumpAt(data, meshesStart, models[modelIndex].NumMeshes);
                flexes[modelIndex] = new MeshFlex[meshes[modelIndex].Length][];

                for (var meshIndex = 0; meshIndex < meshes[modelIndex].Length; meshIndex++)
                {
                    var meshStart = meshesStart + ((long)meshIndex * meshSize);
                    flexes[modelIndex][meshIndex] = ReadMeshFlexes(data, meshStart, meshes[modelIndex][meshIndex]);
                }
            }

            Models[partIndex] = models;
            Meshes[partIndex] = meshes;
            MeshFlexes[partIndex] = flexes;
            Eyeballs[partIndex] = eyeballs;
        }
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
