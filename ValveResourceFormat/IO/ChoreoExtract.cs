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
        this.vcdlistResource = vcdlistResource;
        choreoDataList = (ChoreoSceneFileData)vcdlistResource.DataBlock;
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
