using System.IO;
using System.Text;

namespace ValveResourceFormat.IO
{
    public sealed class FlexSceneExtract
    {
        private readonly FlexSceneFile.FlexSceneFile flexSceneFile;
        public FlexSceneExtract(Stream stream)
        {
            flexSceneFile = new();
            flexSceneFile.Read(stream);
        }

        public ContentFile ToContentFile()
        {
            var fileName = Path.ChangeExtension(flexSceneFile.Name, ".txt");

            return new ContentFile
            {
                Data = Encoding.UTF8.GetBytes(flexSceneFile.ToString()),
                FileName = Path.GetFileName(fileName),
            };
        }
    }
}
