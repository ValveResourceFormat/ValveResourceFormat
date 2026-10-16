using System.IO;
using ValveResourceFormat.ToolsAssetInfo;

namespace CLI;

public partial class Decompiler
{
    public void ParseToolsAssetInfo(string path, Stream stream, string? originalPath)
    {
        var assetsInfo = new ToolsAssetInfo();

        try
        {
            assetsInfo.Read(stream);

            if (CollectStats)
            {
                AddNonResourceStat($"ToolsAssetInfo version {assetsInfo.Version}", path, originalPath);
                return;
            }

            Console.WriteLine(assetsInfo.ToDependencyText(ToolsAssetInfoShort));
        }
        catch (Exception e)
        {
            LogException(e, path, originalPath);
        }
    }
}
