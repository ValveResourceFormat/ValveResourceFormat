using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers
{
    class BinaryKeyValues1(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private string? text;

        public static bool IsAccepted(uint magic)
        {
            return magic == BinaryKV1.MAGIC;
        }

        public async Task LoadAsync(Stream input)
        {
            Stream stream;
            KVObject kv;

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
                kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Binary).Deserialize(stream);
            }
            finally
            {
                stream.Close();
            }

            using var ms = new MemoryStream();
            using var reader = new StreamReader(ms);

            KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(ms, kv);

            ms.Seek(0, SeekOrigin.Begin);

            var text = await reader.ReadToEndAsync().ConfigureAwait(false);
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
