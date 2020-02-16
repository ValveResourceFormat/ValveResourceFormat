using GUI.Types.Renderer;
using SteamDatabase.ValvePak;

namespace GUI.Types.ParticleRenderer
{
    public class VrfGuiContext
    {
        public string FileName { get; }

        public Package CurrentPackage { get; }

        public MaterialLoader MaterialLoader { get; }

        public VrfGuiContext(string fileName, Package package)
        {
            FileName = fileName;
            CurrentPackage = package;
            MaterialLoader = new MaterialLoader(fileName, package);
        }
    }
}
