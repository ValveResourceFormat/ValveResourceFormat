using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers
{
    class BinaryKeyValues1 : IViewer
    {
        public static bool IsAccepted(uint magic)
        {
            return magic == BinaryKV1.MAGIC;
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream input)
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

            var text = reader.ReadToEnd();

            var control = CodeTextBox.Create(text);
            var tab = new TabPage();
            tab.Controls.Add(control);

            return tab;
        }
    }
}
