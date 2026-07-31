using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers
{
    class BinaryKeyValues3(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
    {
        private string? text;
        private IReadOnlyList<KvSourceSpan>? sourceMap;

        public static bool IsAccepted(uint magic) => BinaryKV3.IsBinaryKV3(magic);

        public async Task LoadAsync(Stream? stream)
        {
            var kv3 = new BinaryKV3(ValveResourceFormat.BlockType.Undefined) { Resource = null! };
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

            (text, sourceMap) = KVSerializer.Create(KVSerializationFormat.KeyValues3Text).SerializeWithSourceMap(kv3.Data);
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
