using System.IO;
using System.Text;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Extracts closed captions data to text format.
    /// </summary>
    public sealed class ClosedCaptionsExtract
    {
        private readonly ClosedCaptions.ClosedCaptions closedCaptions;
        private readonly string fileName;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClosedCaptionsExtract"/> class.
        /// </summary>
        /// <param name="stream">Stream containing closed captions data.</param>
        /// <param name="fileName">Name of the source file.</param>
        public ClosedCaptionsExtract(Stream stream, string fileName)
        {
            closedCaptions = new();
            closedCaptions.Read(fileName, stream);

            this.fileName = Path.ChangeExtension(fileName, ".txt");
        }

        /// <summary>
        /// Converts closed captions to a content file.
        /// </summary>
        /// <returns>Content file containing the closed captions as text.</returns>
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
