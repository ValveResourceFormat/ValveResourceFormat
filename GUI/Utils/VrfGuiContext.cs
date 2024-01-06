using System;
using System.Diagnostics;
using GUI.Types.Renderer;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ToolsAssetInfo;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;
using System.Threading.Tasks;

namespace GUI.Utils
{
    class VrfGuiContext : IDisposable
    {
        public string FileName { get; }

        public Package CurrentPackage
        {
            get
            {
                // TODO: Due to slight mess with current/parent contexts, return parent package if available to keep old behaviour
                return FileLoader.CurrentPackage ?? ParentGuiContext?.CurrentPackage;
            }
            set
            {
                FileLoader.CurrentPackage = value;
            }
        }

        public MaterialLoader MaterialLoader { get; }

        public ShaderLoader ShaderLoader { get; private set; }
        public GPUMeshBufferCache MeshBufferCache { get; }
        public AdvancedGuiFileLoader FileLoader { get; private set; }
        public VrfGuiContext ParentGuiContext { get; private set; }
        public ToolsAssetInfo ToolsAssetInfo { get; set; }
        public int DefaultFrameBuffer { get; set; }

        public VrfGuiContext(string fileName, VrfGuiContext parentGuiContext)
        {
            FileName = fileName;
            ParentGuiContext = parentGuiContext;
            MaterialLoader = new MaterialLoader(this);
            ShaderLoader = new ShaderLoader();
            FileLoader = new AdvancedGuiFileLoader(this);
            MeshBufferCache = new GPUMeshBufferCache();

            Task.Run(FillSurfacePropertyHashes);
        }

        public void FillSurfacePropertyHashes()
        {
            if (ParentGuiContext != null)
            {
                return;
            }

            using var vsurf = FileLoader.LoadFile("surfaceproperties/surfaceproperties.vsurf_c");
            if (vsurf is not null && vsurf.DataBlock is BinaryKV3 kv3)
            {
                var surfacePropertiesList = kv3.Data.GetArray("SurfacePropertiesList");
                foreach (var surface in surfacePropertiesList)
                {
                    var name = surface.GetProperty<string>("surfacePropertyName");
                    var hash = StringToken.Get(name.ToLowerInvariant());
                    Debug.Assert(hash == surface.GetUnsignedIntegerProperty("m_nameHash"));
                }
            }
        }

        public Resource LoadFile(string file) => FileLoader.LoadFile(file);

        private const string CompiledFileSuffix = "_c";
        public Resource LoadFileCompiled(string file) => FileLoader.LoadFile(string.Concat(file, CompiledFileSuffix));

        public void ClearCache()
        {
            FileLoader.ClearCache();
            //ShaderLoader.ClearCache();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ParentGuiContext = null;

                if (CurrentPackage != null)
                {
                    CurrentPackage.Dispose();
                    CurrentPackage = null;
                }

                FileLoader.Dispose();
                FileLoader = null;

                ShaderLoader.Dispose();
                ShaderLoader = null;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
