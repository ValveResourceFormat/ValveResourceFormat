using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents Panorama dynamic images resource.
    /// </summary>
    public class PanoramaDynamicImages : Panorama
    {
        // TODO: This might need to live in `Panorama`
        /// <inheritdoc/>
        /// <remarks>
        /// Lists all dynamic images with their dimensions and metadata.
        /// </remarks>
        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine($"CRC: {CRC32:X8}");
            writer.WriteLine();
            writer.WriteLine($"Images({Images.Count}):");

            foreach (var image in Images)
            {
                writer.WriteLine($" - {image.Name} [{image.Width}x{image.Height} - {image.CRC32:X8}]");
            }

            writer.WriteLine();
            writer.WriteLine($"Content ({Data.Length} bytes):");
            writer.WriteLine(Encoding.UTF8.GetString(Data));
        }
    }
}
