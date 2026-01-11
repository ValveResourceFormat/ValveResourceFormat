using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers
{
    class BinaryKeyValues3(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private string? text;

        public static bool IsAccepted(uint magic) => BinaryKV3.IsBinaryKV3(magic);

        public async Task LoadAsync(Stream? stream)
        {
            var kv3 = new BinaryKV3(ValveResourceFormat.BlockType.Undefined);
            Stream kv3stream;

            if (stream != null)
            {
                kv3stream = stream;
            }
            else
            {
                kv3stream = File.OpenRead(vrfGuiContext.FileName!);
            }

            using (var binaryReader = new BinaryReader(kv3stream))
            {
                kv3.Size = (uint)kv3stream.Length;
                kv3.Read(binaryReader);
            }

            kv3stream.Close();

            text = kv3.ToString();
        }

        public void Create(TabPage tab)
        {
            Debug.Assert(text is not null);

            var control = CodeTextBox.Create(text);
            tab.Controls.Add(control);

            text = null;
        }

        public void Dispose()
        {
            //
        }
    }
}
