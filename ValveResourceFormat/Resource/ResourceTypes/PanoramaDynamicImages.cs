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
            writer.WriteLine($"Images({Names.Count}):");

            foreach (var name in Names)
            {
                var w = name.Unknown1 & 0xFFFF;
                var h = (name.Unknown1 >> 16) & 0xFFFF;

                writer.WriteLine($" - {name.Name} [{w}x{h} - {name.Unknown2:X8}]");
            }

            writer.WriteLine();
            writer.WriteLine($"Content ({Data.Length} bytes):");
            writer.WriteLine(Encoding.UTF8.GetString(Data));
        }
    }
}
