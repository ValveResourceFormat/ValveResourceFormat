using System;
using System.IO;
using System.Text;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation2;
public class NmClipExtract
{
    private readonly Resource resource;
    private readonly AnimationClip clip;
    private readonly IFileLoader fileLoader;
    public NmClipExtract(Resource resource, IFileLoader fileLoader)
    {
        this.resource = resource;
        clip = resource.DataBlock as AnimationClip
            ?? throw new InvalidDataException("Resource DataBlock is not an AnimationClip.");
        this.fileLoader = fileLoader;
    }
    public ContentFile ToContentFile()
    {
        var contentFile = new ContentFile();

        var kv = new KVObject(null);
        var sourceFileName = Path.ChangeExtension(resource.FileName, ".dmx");
        kv.AddProperty("m_sourceFilename", sourceFileName);
        kv.AddProperty("m_animationSkeletonName", clip.SkeletonName);
        contentFile.Data = Encoding.UTF8.GetBytes(new KV3File(kv).ToString());

        contentFile.AddSubFile(sourceFileName, () =>
        {
            using var skeletonResource = fileLoader.LoadFileCompiled(clip.SkeletonName);

            if (skeletonResource == null)
            {
                return null;
            }

            var skeleton = ModelAnimation.Skeleton.FromSkeletonData(((BinaryKV3)skeletonResource.DataBlock!).Data);

            return ModelExtract.ToDmxAnim(skeleton, [], new ModelAnimation.Animation(clip));
        });

        return contentFile;
    }
}
