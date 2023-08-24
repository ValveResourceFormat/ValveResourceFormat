using System.Globalization;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    public class PanoramaDynamicImages : Panorama
    {
        // TODO: This might need to live in `Panorama`
        public override string ToString()
        {
            var sb = new StringBuilder(Data.Length);

            sb.AppendLine(CultureInfo.InvariantCulture, $"CRC: {CRC32:X8}");
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Images({Names.Count}):");

            foreach (var name in Names)
            {
                var w = name.Unknown1 & 0xFFFF;
                var h = (name.Unknown1 >> 16) & 0xFFFF;

                sb.AppendLine(CultureInfo.InvariantCulture, $" - {name.Name} [{w}x{h} - {name.Unknown2:X8}]");
            }

            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Content ({Data.Length} bytes):");
            sb.AppendLine(Encoding.UTF8.GetString(Data));

            return sb.ToString();
        }
    }
}
