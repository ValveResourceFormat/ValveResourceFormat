using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;

namespace GUI.Types.Exporter
{
    public class ExportData
    {
        public PackageEntry PackageEntry { get; set; }
        public VrfGuiContext VrfGuiContext { get; set; }
    }
}
