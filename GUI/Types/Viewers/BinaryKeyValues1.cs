using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers
{
    class BinaryKeyValues1(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private string? text;
        private IReadOnlyList<KvSourceSpan>? sourceMap;

        public static bool IsAccepted(uint magic)
        {
            return magic == BinaryKV1.MAGIC;
        }

        public async Task LoadAsync(Stream? input)
        {
            Stream stream;
            KVDocument kv;

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

            (text, sourceMap) = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).SerializeWithSourceMap(kv);
        }

        public ViewerContent GetContent()
        {
            Debug.Assert(text is not null);
            Debug.Assert(sourceMap is not null);

            var content = new ViewerContent.Text(text, SourceMap: sourceMap);

            text = null;
            sourceMap = null;

            return content;
        }

        public void Dispose()
        {
            //
        }
    }
}
