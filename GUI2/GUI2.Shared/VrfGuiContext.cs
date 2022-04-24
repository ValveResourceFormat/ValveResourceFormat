using GUI2.Viewers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace GUI2
{
    class VrfGuiContext
    {
        public VrfGuiContext(StorageFile file)
        {
            File = file;
        }

        public Type XamlPage { get; private set; }

        public StorageFile File { get; private set; }

        public byte[] FileBytes { get; private set; }

        public bool ContainsNull { get; private set; }

        internal async Task<VrfGuiContext> Process()
        {
            using var fs = await File.OpenStreamForReadAsync().ConfigureAwait(false);
            var magicData = new byte[6];
            await fs.ReadAsync(magicData.AsMemory(0, 6)).ConfigureAwait(false);

            uint magic = BitConverter.ToUInt32(magicData, 0);
            ushort magicResourceVersion = BitConverter.ToUInt16(magicData, 4);

            // TODO: ND
            fs.Seek(0, SeekOrigin.Begin);
            using var ms = new MemoryStream();
            await fs.CopyToAsync(ms).ConfigureAwait(false);
            FileBytes = ms.ToArray();

            if (Package.IsAccepted(magic, magicResourceVersion))
            {
                XamlPage = typeof(Package);
            }
            else
            {
                XamlPage = typeof(ByteViewer);
                ContainsNull = FileBytes.Contains<byte>(0x00);
            }
            return this;
        }
    }
}
