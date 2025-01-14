using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.IO;

public class AnimationGraphExtract
{
    private readonly BinaryKV3 graph;
    private readonly string outputFileName;

    public AnimationGraphExtract(Resource resource)
    {
        graph = (BinaryKV3)resource.DataBlock;

        if (resource.FileName != null)
        {
            outputFileName = resource.FileName;
            outputFileName = outputFileName.EndsWith("_c", StringComparison.Ordinal)
                ? outputFileName[..^2]
                : outputFileName;
        }
    }

    public ContentFile ToContentFile()
    {
        var contentFile = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToEditableAnimGraphVersion19()),
            FileName = outputFileName,
        };

        return contentFile;
    }

    public string ToEditableAnimGraphVersion19()
    {
        var kv = ModelExtract.MakeNode(
            "CAnimationGraph"
        );

        return new KV3File(kv, format: "animgraph19:version{0adb35b7-2585-4302-8d05-e2825b4518ac}").ToString();
    }
}
