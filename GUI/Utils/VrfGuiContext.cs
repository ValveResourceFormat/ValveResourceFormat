using GUI.Controls;
using GUI.Types.Renderer;
using SteamDatabase.ValvePak;
using ValveResourceFormat;

namespace GUI.Utils
{
    public class VrfGuiContext
    {
        public string FileName { get; }

        public Package CurrentPackage { get; }

        public Package ParentPackage { get; }

        public MaterialLoader MaterialLoader { get; }

        public ShaderLoader ShaderLoader { get; }
        public GPUMeshBufferCache MeshBufferCache { get; }

        private readonly AdvancedGuiFileLoader FileLoader;

        public QuadIndexBuffer QuadIndices
        {
            get
            {
                if (quadIndices == null)
                {
                    quadIndices = new QuadIndexBuffer(65532);
                }

                return quadIndices;
            }
        }

        private QuadIndexBuffer quadIndices;

        public VrfGuiContext(string fileName, TreeViewWithSearchResults.TreeViewPackageTag package)
        {
            FileName = fileName;
            CurrentPackage = package?.Package;
            ParentPackage = package?.ParentPackage;
            MaterialLoader = new MaterialLoader(this);
            ShaderLoader = new ShaderLoader();
            FileLoader = new AdvancedGuiFileLoader(this);
            MeshBufferCache = new GPUMeshBufferCache();
        }

        public Resource LoadFileByAnyMeansNecessary(string file) =>
            FileLoader.LoadFile(file);

        public void ClearCache() => FileLoader.ClearCache();
    }
}
