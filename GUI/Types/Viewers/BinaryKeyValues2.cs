using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using Datamodel;

namespace GUI.Types.Viewers
{
    class BinaryKeyValues2 : IViewer
    {
        public const int MAGIC = 757932348; // "<!--"

        public static bool IsAccepted(uint magic, string fileName)
        {
            return magic == MAGIC && (fileName.EndsWith(".dmx", StringComparison.OrdinalIgnoreCase) ||
                                      fileName.EndsWith(".vmap", StringComparison.OrdinalIgnoreCase));
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream input)
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

            var text = reader.ReadToEnd();

            var control = new CodeTextBox(text);
            var tab = new TabPage();
            tab.Controls.Add(control);

            return tab;
        }
    }
}
