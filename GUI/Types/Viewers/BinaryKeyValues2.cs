using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers
{
    class BinaryKeyValues2(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        public const int MAGIC = 757932348; // "<!--"

        private string? text;
        private List<VmapSmartPropRow>? smartPropRows;

        public static bool IsAccepted(uint magic, string fileName)
        {
            return magic == MAGIC && (fileName.EndsWith(".dmx", StringComparison.OrdinalIgnoreCase) ||
                                      fileName.EndsWith(".vmap", StringComparison.OrdinalIgnoreCase));
        }

        public async Task LoadAsync(Stream? input)
        {
            Stream stream;
            Datamodel.Datamodel dm;

            if (input != null)
            {
                stream = input;
            }
            else
            {
                stream = File.OpenRead(vrfGuiContext.FileName!);
            }

            try
            {
                dm = Datamodel.Datamodel.Load(stream, Datamodel.Codecs.DeferredMode.Disabled);
            }
            finally
            {
                stream.Close();
            }

            using var ms = new MemoryStream();
            using var reader = new StreamReader(ms);

            dm.Save(ms, "keyvalues2", 4);

            ms.Seek(0, SeekOrigin.Begin);

            text = await reader.ReadToEndAsync().ConfigureAwait(false);

            if (vrfGuiContext.FileName?.EndsWith(".vmap", StringComparison.OrdinalIgnoreCase) == true && dm.Root is Datamodel.Element root)
            {
                smartPropRows = [];
                foreach (var evaluation in SmartPropMapEvaluator.EvaluateAll(root, LoadSmartProp))
                {
                    smartPropRows.Add(new VmapSmartPropRow(
                        evaluation.Parameters.SmartPropFilename,
                        evaluation.Parameters.Values.Count,
                        evaluation.Result.Models.Count));
                }
            }
        }

        public ViewerContent GetContent()
        {
            Debug.Assert(text is not null);

            ViewerContent content = smartPropRows is { Count: > 0 }
                ? new ViewerContent.Tabs([
                    new ViewerTab("Evaluated Smart Props", new ViewerContent.Grid(smartPropRows), Select: true),
                    new ViewerTab("DataModel", new ViewerContent.Text(text)),
                ])
                : new ViewerContent.Text(text);

            text = null;
            smartPropRows = null;

            return content;
        }

        private KVObject? LoadSmartProp(string filename)
        {
            var directory = Path.GetDirectoryName(vrfGuiContext.FileName);
            while (!string.IsNullOrEmpty(directory))
            {
                var path = Path.Combine(directory, filename.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                {
                    return KVDocumentExtensions.ParseKV3(path).Root;
                }

                directory = Path.GetDirectoryName(directory);
            }

            return null;
        }

        public void Dispose()
        {
            //
        }

        private sealed record VmapSmartPropRow(string SmartProp, int UserParameters, int EvaluatedModels);
    }
}
