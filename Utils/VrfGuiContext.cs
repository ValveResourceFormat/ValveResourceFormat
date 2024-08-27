using System.Diagnostics;
using System.Threading.Tasks;
using GUI.Types.Renderer;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.ToolsAssetInfo;
using ValveResourceFormat.Utils;

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

        private int Children;
        private bool WantsToBeDisposed;

#if DEBUG
        private int TotalChildren;
        private static int LastContextId;
        private readonly int ContextId = ++LastContextId;
#endif

        public VrfGuiContext()
        {
            MaterialLoader = new MaterialLoader(this);
            ShaderLoader = new ShaderLoader(this);
        }

        public VrfGuiContext(string fileName, VrfGuiContext parentGuiContext) : this()
        {
#if DEBUG
            Log.Debug(nameof(VrfGuiContext), $"#{ContextId} created");
#endif

            FileName = fileName;
            ParentGuiContext = parentGuiContext;
            FileLoader = new AdvancedGuiFileLoader(this);
            MeshBufferCache = new GPUMeshBufferCache();

            if (ParentGuiContext != null)
            {
                ParentGuiContext.AddChildren();
                Task.Run(FillSurfacePropertyHashes);
            }
        }

        public void AddChildren()
        {
            Children++;

#if DEBUG
            TotalChildren++;
#endif
        }

        public void RemoveChildren()
        {
            if (--Children == 0 && WantsToBeDisposed)
            {
                Dispose();
            }
        }

        private void FillSurfacePropertyHashes()
        {
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
        public Resource LoadFileCompiled(string file) => FileLoader.LoadFileCompiled(file);

        public void ClearCache()
        {
            FileLoader.ClearCache();
            //ShaderLoader.ClearCache();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            if (Children != 0)
            {
#if DEBUG
                Log.Debug(nameof(VrfGuiContext), $"#{ContextId} Dispose ignored (children: {Children}, has parent: {ParentGuiContext != null})");
#endif

                WantsToBeDisposed = true;
                return;
            }

#if DEBUG
            Log.Debug(nameof(VrfGuiContext), $"#{ContextId} Dispose (total children: {TotalChildren}, has parent: {ParentGuiContext != null}, prev: {WantsToBeDisposed})");
#endif

            if (ParentGuiContext != null)
            {
                ParentGuiContext.RemoveChildren();
                ParentGuiContext = null;
            }

            if (FileLoader != null)
            {
                if (FileLoader.CurrentPackage != null)
                {
                    FileLoader.CurrentPackage.Dispose();
                    FileLoader.CurrentPackage = null;
                }

                FileLoader.Dispose();
                FileLoader = null;
            }

            if (ShaderLoader != null)
            {
                ShaderLoader.Dispose();
                ShaderLoader = null;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
