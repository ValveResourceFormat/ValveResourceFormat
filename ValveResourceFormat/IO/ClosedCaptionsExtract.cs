using System.IO;
using System.Text;

namespace ValveResourceFormat.IO
{
    public sealed class ClosedCaptionsExtract
    {
        private readonly ClosedCaptions.ClosedCaptions closedCaptions;
        private readonly string fileName;
        public ClosedCaptionsExtract(Stream stream, string fileName)
        {
            closedCaptions = new();
            closedCaptions.Read(fileName, stream);

            this.fileName = Path.ChangeExtension(fileName, ".txt");
        }

        public ContentFile ToContentFile()
        {
            return new ContentFile
            {
                Data = Encoding.UTF8.GetBytes(closedCaptions.ToString()),
                FileName = Path.GetFileName(fileName),
            };
        }
    }
}
