using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    class BinaryKeyValues2(VrfGuiContext vrfGuiContext) : IViewer
    {
        public const int MAGIC = 757932348; // "<!--"

        private string? text;

        public static bool IsAccepted(uint magic, string fileName)
        {
            return magic == MAGIC && (fileName.EndsWith(".dmx", StringComparison.OrdinalIgnoreCase) ||
                                      fileName.EndsWith(".vmap", StringComparison.OrdinalIgnoreCase));
        }

        public async Task LoadAsync(Stream input)
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

            var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        public TabPage Create()
        {
            Debug.Assert(text is not null);

            var control = CodeTextBox.Create(text);
            var tab = new ThemedTabPage();
            tab.Controls.Add(control);

            text = null;

            return tab;
        }
    }
}
