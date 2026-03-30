using System.IO;
using System.Text;
using ValveKeyValue;
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
        kv.Add("m_sourceFileName", dmxFile);
        kv.Add("m_rootBoneName", "");
        kv.Add("m_flGlobalScale", 1.0f);
        kv.Add("m_bIsAttachableProp", kvSkeleton.GetProperty<bool>("m_bIsPropSkeleton"));
        kv.Add("m_secondarySkeletons", kvSkeleton.GetChild("m_secondarySkeletons"));
        var numLowLODBones = kvSkeleton.GetInt32Property("m_numBonesToSampleAtLowLOD");
        var boneIDs = kvSkeleton.GetArray<string>("m_boneIDs")![numLowLODBones..];
        var highLODBones = KVObject.Array();
        foreach (var boneID in boneIDs)
        {
            highLODBones.Add(boneID);
        }
        kv.Add("m_highLODBones", highLODBones);
        // Mask definitions seem to be 1:1 to the source.
        kv.Add("m_boneMaskSetDefinitions", kvSkeleton.GetChild("m_maskDefinitions"));

        var contentFile = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(new KV3File(kv).ToString())
        };
        contentFile.AddSubFile(Path.GetFileName(dmxFile) ?? "skeleton.dmx", () =>
        {
            // Empty animation data
            var anim = new Animation(new AnimationClip() { Resource = null! });
            return ModelExtract.ToDmxAnim(skel, [], anim);
        });
        return contentFile;
    }
}
