using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

public class NmSkeletonExtract
{
    private readonly Resource resource;
    private readonly KVObject kvSkeleton;
    public NmSkeletonExtract(Resource resource)
    {
        this.resource = resource;
        var resourceData = resource.DataBlock as BinaryKV3
            ?? throw new InvalidDataException("Resource DataBlock is not a BinaryKV3 or is null.");
        kvSkeleton = resourceData.Data;
    }
    public ContentFile ToContentFile()
    {
        var kv = new KVObject(null);
        var skel = Skeleton.FromSkeletonData(kvSkeleton);
        var dmxFile = Path.ChangeExtension(resource.FileName, "dmx");
        kv.AddProperty("m_sourceFileName", dmxFile);
        kv.AddProperty("m_rootBoneName", skel.Roots.FirstOrDefault()?.Name);
        kv.AddProperty("m_flGlobalScale", 1.0f);
        kv.AddProperty("m_bIsAttachableProp", kvSkeleton.GetProperty<bool>("m_bIsPropSkeleton"));
        kv.AddProperty("m_secondarySkeletons", kvSkeleton.GetProperty<object>("m_secondarySkeletons"));
        var numLowLODBones = kvSkeleton.GetInt32Property("m_numBonesToSampleAtLowLOD");
        var boneIDs = kvSkeleton.GetArray<string>("m_boneIDs")[numLowLODBones..];
        var highLODBones = new KVObject("m_highLODBones", true, boneIDs.Length);
        foreach (var boneID in boneIDs)
        {
            highLODBones.AddItem(boneID);
        }
        kv.AddProperty("m_highLODBones", highLODBones);
        // Mask definitions seem to be 1:1 to the source.
        kv.AddProperty("m_boneMaskSetDefinitions", kvSkeleton.GetProperty<object>("m_maskDefinitions"));

        var contentFile = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(new KV3File(kv).ToString())
        };
        contentFile.AddSubFile(dmxFile, () =>
        {
            // Empty animation data
            var anim = new Animation(new AnimationClip());
            return ModelExtract.ToDmxAnim(skel, [], anim);
        });
        return contentFile;
    }
}
