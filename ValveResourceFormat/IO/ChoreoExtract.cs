using System.IO;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts choreography scene data from VCD list resources.
/// </summary>
public class ChoreoExtract
{
    private readonly Resource vcdlistResource;
    private readonly ChoreoSceneFileData choreoDataList;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChoreoExtract"/> class.
    /// </summary>
    /// <param name="vcdlistResource">Resource containing choreography scene data.</param>
    public ChoreoExtract(Resource vcdlistResource)
    {
        var dataBlock = (ChoreoSceneFileData?)vcdlistResource.DataBlock;

        ArgumentNullException.ThrowIfNull(dataBlock);

        this.vcdlistResource = vcdlistResource;
        choreoDataList = dataBlock;
    }

    /// <summary>
    /// Converts choreography data to a content file with individual scene files.
    /// </summary>
    /// <returns>Content file containing the choreography scenes.</returns>
    public ContentFile ToContentFile()
    {
        using var indentedTextWriter = new IndentedTextWriter();
        choreoDataList.WriteText(indentedTextWriter);

        var vcdlist = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(indentedTextWriter.ToString()),
            FileName = Path.GetFileName(vcdlistResource.FileName) ?? "scene.vcdlist",
        };

        foreach (var scene in choreoDataList.Scenes)
        {
            var kv = new KV3File(scene.ToKeyValues());

            vcdlist.AddSubFile(
                Path.GetFileName(scene.Name) ?? "choreo_scene.vcd",
                () => Encoding.UTF8.GetBytes(kv.ToString())
            );
        }

        return vcdlist;
    }
}
