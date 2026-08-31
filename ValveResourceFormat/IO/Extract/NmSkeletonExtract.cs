using System.Diagnostics;
using System.IO;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.IO.Smd;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts Source 2 skeletons to editable format.
/// </summary>
public class NmSkeletonExtract
{
    private readonly Resource resource;
    private readonly KVObject kvSkeleton;

    /// <summary>
    /// Initializes a new instance of the <see cref="NmSkeletonExtract"/> class.
    /// </summary>
    public NmSkeletonExtract(Resource resource)
    {
        this.resource = resource;
        var resourceData = resource.DataBlock as BinaryKV3
            ?? throw new InvalidDataException("Resource DataBlock is not a BinaryKV3 or is null.");
        kvSkeleton = resourceData.Data;
    }

    /// <summary>
    /// Converts the skeleton to a content file.
    /// </summary>
    public ContentFile ToContentFile()
    {
        var kv = KVObject.Collection();
        var skel = Skeleton.FromSkeletonData(kvSkeleton);

        var dmxFile = Path.ChangeExtension(resource.FileName, "dmx");
        Debug.Assert(dmxFile != null);
        kv.Add("m_sourceFileName", dmxFile);

        var rootBoneName = skel.Roots.Length == 1
            ? skel.Roots[0].Name
            : string.Empty;

        kv.Add("m_rootBoneName", rootBoneName);
        kv.Add("m_flGlobalScale", 1.0f);
        kv.Add("m_bIsAttachableProp", kvSkeleton.GetBooleanProperty("m_bIsPropSkeleton"));
        kv.Add("m_secondarySkeletons", kvSkeleton["m_secondarySkeletons"]);

        var numLowLODBones = kvSkeleton.GetInt32Property("m_numBonesToSampleAtLowLOD");
        var boneIDs = kvSkeleton.GetArray<string>("m_boneIDs")![numLowLODBones..];
        var highLODBones = KVObject.Array();
        foreach (var boneID in boneIDs)
        {
            highLODBones.Add(boneID);
        }
        kv.Add("m_highLODBones", highLODBones);
        // Mask definitions seem to be 1:1 to the source.
        kv.Add("m_boneMaskSetDefinitions", kvSkeleton["m_maskDefinitions"]);

        var contentFile = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(kv.ToKV3String())
        };
        contentFile.AddSubFile(Path.GetFileName(dmxFile), () =>
        {
            return ModelExtract.ToDmxSkeleton(skel, nmSkelAxisFixup: true, nmLowLodBoneCount: numLowLODBones);
        });

        return contentFile;
    }

    /// <summary>
    /// Exports the skeleton directly to Source Studio Model Data (SMD) format for Blender.
    /// </summary>
    public SmdData ToSmdData()
    {
        var skel = Skeleton.FromSkeletonData(kvSkeleton);
        var smd = new SmdData
        {
            Name = Path.GetFileNameWithoutExtension(resource.FileName) ?? "Skeleton",
            Type = SmdType.Skeleton
        };

        foreach (var bone in skel.Bones)
        {
            smd.AddBone(bone.Parent?.Name ?? string.Empty, bone.Name);
        }

        var keyframes = new System.Collections.Generic.List<SmdData.KeyFrame>();
        for (var i = 0; i < skel.Bones.Length; i++)
        {
            var bone = skel.Bones[i];
            var euler = EntityTransformHelper.ToEulerAngles(bone.Angle);
            keyframes.Add(new SmdData.KeyFrame(i, bone.Position, euler));
        }

        smd.Frames.Add(keyframes);
        return smd;
    }

    /// <summary>
    /// Exports the skeleton as an SMD ContentFile.
    /// </summary>
    public ContentFile ToSmdContentFile()
    {
        var smd = ToSmdData();
        return new ContentFile
        {
            Data = smd.ToBytes(),
            FileName = Path.ChangeExtension(resource.FileName, "smd") ?? "skeleton.smd"
        };
    }
}
