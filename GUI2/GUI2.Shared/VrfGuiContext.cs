using GUI2.Viewers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using ValvePackage = SteamDatabase.ValvePak.Package;

namespace GUI2
{
    class VrfGuiContext
    {
        public VrfGuiContext()
        {
        }

        public VrfGuiContext(string filename, byte[] data, ValvePackage package)
        {
            FileName = filename;
            FileBytes = data;
            Package = package;
        }

        public Type XamlPage { get; private set; }

        public string FileName { get; private set; }

        public byte[] FileBytes { get; private set; }

        public bool ContainsNull { get; private set; }

        public ValvePackage Package { get; private set; }

        internal async Task<VrfGuiContext> ProcessStorageFile(StorageFile file)
        {
            using var fs = await file.OpenStreamForReadAsync().ConfigureAwait(false);
            FileName = file.Path;
            using var ms = new MemoryStream();
            await fs.CopyToAsync(ms).ConfigureAwait(false);
            FileBytes = ms.ToArray();
            return await Process().ConfigureAwait(false);
        }

        internal Task<VrfGuiContext> Process()
        {
            var magicData = FileBytes[0..6];

            uint magic = BitConverter.ToUInt32(magicData, 0);
            ushort magicResourceVersion = BitConverter.ToUInt16(magicData, 4);

            if (Viewers.Package.IsAccepted(magic, magicResourceVersion))
            {
                XamlPage = typeof(Package);
            }
            else if (Resource.IsAccepted(magic, magicResourceVersion))
            {
                XamlPage = typeof(Resource);
            }
            else
            {
                XamlPage = typeof(ByteViewer);
                ContainsNull = FileBytes.Contains<byte>(0x00);
            }
            return Task.FromResult(this);
        }
    }
}
