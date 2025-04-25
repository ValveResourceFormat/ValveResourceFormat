using System.IO;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

public class ChoreoExtract
{
    private readonly Resource vcdlistResource;
    private readonly ChoreoSceneFileData choreoDataList;
    public ChoreoExtract(Resource vcdlistResource)
    {
        var dataBlock = (ChoreoSceneFileData?)vcdlistResource.DataBlock;

        ArgumentNullException.ThrowIfNull(dataBlock);

        this.vcdlistResource = vcdlistResource;
        choreoDataList = dataBlock;
    }

    public ContentFile ToContentFile()
    {
        using var indentedTextWriter = new IndentedTextWriter();
        choreoDataList.WriteText(indentedTextWriter);

        var vcdlist = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(indentedTextWriter.ToString()),
            FileName = Path.GetFileName(vcdlistResource.FileName),
        };

        foreach (var scene in choreoDataList.Scenes)
        {
            var kv = new KV3File(scene.ToKeyValues());

            vcdlist.AddSubFile(
                scene.Name,
                () => Encoding.UTF8.GetBytes(kv.ToString())
            );
        }

        return vcdlist;
    }
}
