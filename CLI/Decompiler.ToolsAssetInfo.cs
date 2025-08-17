using System.Globalization;
using System.IO;
using System.Text;
using ValveResourceFormat.ToolsAssetInfo;

namespace CLI;

public partial class Decompiler
{
    public void ParseToolsAssetInfo(string path, Stream stream)
    {
        var assetsInfo = new ToolsAssetInfo();

        try
        {
            assetsInfo.Read(stream);

            if (CollectStats)
            {
                var id = $"ToolsAssetInfo version {assetsInfo.Version}";

                AddStat(id, id, path);

                return;
            }

            string output;

            if (ToolsAssetInfoShort)
            {
                var str = new StringBuilder();

                foreach (var file in assetsInfo.Files)
                {
                    str.AppendLine(file.Key);
                }

                output = str.ToString();
            }
            else
            {
                output = StringifyToolsAssetInfo(assetsInfo);
            }

            if (OutputFile != null)
            {
                path = Path.ChangeExtension(path, "txt");
                path = GetOutputPath(path);

                DumpFile(path, Encoding.UTF8.GetBytes(output));
            }
            else
            {
                Console.WriteLine(output);
            }
        }
        catch (Exception e)
        {
            LogException(e, path);
        }
    }

    private static string StringifyToolsAssetInfo(ToolsAssetInfo assetsInfo)
    {
        var str = new StringBuilder();

        foreach (var file in assetsInfo.Files)
        {
            str.AppendLine(file.Key);

            foreach (var dep in file.Value.SearchPathsGameRoot)
            {
                str.AppendLine(CultureInfo.InvariantCulture, $"- GAME: {dep.Filename}");
            }

            foreach (var dep in file.Value.SearchPathsContentRoot)
            {
                str.AppendLine(CultureInfo.InvariantCulture, $"- CONTENT: {dep.Filename}");
            }

            foreach (var dep in file.Value.InputDependencies)
            {
                str.AppendLine(CultureInfo.InvariantCulture, $"- INPUT: {dep.Filename} (crc {dep.FileCRC})");
            }

            foreach (var dep in file.Value.AdditionalInputDependencies)
            {
                str.AppendLine(CultureInfo.InvariantCulture, $"- INPUT: {dep.Filename} (crc {dep.FileCRC})");
            }

            foreach (var dep in file.Value.ExternalReferences)
            {
                str.AppendLine(CultureInfo.InvariantCulture, $"- REF: {dep}");
            }

            foreach (var dep in file.Value.ChildResources)
            {
                str.AppendLine(CultureInfo.InvariantCulture, $"- CHILD: {dep}");
            }

            foreach (var dep in file.Value.AdditionalRelatedFiles)
            {
                str.AppendLine(CultureInfo.InvariantCulture, $"- RELATED: {dep}");
            }

            foreach (var dep in file.Value.WeakReferences)
            {
                str.AppendLine(CultureInfo.InvariantCulture, $"- WEAK REF: {dep}");
            }

            foreach (var dep in file.Value.SpecialDependencies)
            {
                str.AppendLine(CultureInfo.InvariantCulture, $"- SPECIAL DEPENDENCY: {dep.String} \"{dep.CompilerIdentifier}\"");
            }

            foreach (var dep in file.Value.SubassetDefinitions)
            {
                str.AppendLine(CultureInfo.InvariantCulture, $"- SUBASSET DEF: {dep.Key}");

                foreach (var val in dep.Value)
                {
                    str.AppendLine(CultureInfo.InvariantCulture, $" - {val}");
                }
            }

            foreach (var dep in file.Value.SubassetReferences)
            {
                str.AppendLine(CultureInfo.InvariantCulture, $"- SUBASSET REF: {dep.Key}");

                foreach (var val in dep.Value)
                {
                    str.AppendLine(CultureInfo.InvariantCulture, $" - {val.Key} {val.Value}");
                }
            }

            str.AppendLine();
        }

        return str.ToString();
    }
}
