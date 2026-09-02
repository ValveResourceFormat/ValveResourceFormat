using System.Diagnostics;
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
        Debug.Assert(dmxFile != null);
        kv.Add("m_sourceFilename", FindAuthoredSourceFile() ?? dmxFile.Replace('\\', '/'));

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
    /// Recovers the mesh the skeleton was authored from. The compiled skeleton does not store it, but
    /// the compiler records it as an input dependency beside the skeleton document itself.
    /// </summary>
    private string? FindAuthoredSourceFile()
    {
        if (resource.EditInfo == null)
        {
            return null;
        }

        string? sourceFileName = null;

        foreach (var dependency in resource.EditInfo.InputDependencies)
        {
            var file = dependency.ContentRelativeFilename.Replace('\\', '/');

            if (file.EndsWith(".vnmskel", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (sourceFileName != null)
            {
                return null;
            }

            sourceFileName = file;
        }

        return sourceFileName;
    }
}
