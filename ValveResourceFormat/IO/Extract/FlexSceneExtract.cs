using System.IO;
using System.Text;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Extracts flex scene data to text format.
    /// </summary>
    public sealed class FlexSceneExtract
    {
        private readonly FlexSceneFile.FlexSceneFile flexSceneFile;

        /// <summary>
        /// Initializes a new instance of the <see cref="FlexSceneExtract"/> class.
        /// </summary>
        /// <param name="stream">Stream containing flex scene data.</param>
        public FlexSceneExtract(Stream stream)
        {
            flexSceneFile = new();
            flexSceneFile.Read(stream);
        }

        /// <summary>
        /// Converts flex scene data to a content file.
        /// </summary>
        /// <returns>Content file containing the flex scene as text.</returns>
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
