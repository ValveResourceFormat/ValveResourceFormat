using GUI.Controls;
using GUI.Types.Renderer;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ToolsAssetInfo;

#nullable disable

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

        public ShaderLoader ShaderLoader { get; private set; }
        public GPUMeshBufferCache MeshBufferCache { get; }
        public AdvancedGuiFileLoader FileLoader { get; private set; }
        public VrfGuiContext ParentGuiContext { get; private set; }
        public ToolsAssetInfo ToolsAssetInfo { get; set; }

        // This is a hack to set camera and properties when clicking a mesh from a model or map
        public Action<GLViewerControl> GLPostLoadAction { get; set; }

        private int Children;
        private bool WantsToBeDisposed;

#if DEBUG
        private int TotalChildren;
        private static int LastContextId;
        private readonly int ContextId = ++LastContextId;
#endif

        public VrfGuiContext()
        {
            ShaderLoader = new ShaderLoader(this);
            MeshBufferCache = new GPUMeshBufferCache();
        }

        public VrfGuiContext(string fileName, VrfGuiContext parentGuiContext) : this()
        {
#if DEBUG
            Log.Debug(nameof(VrfGuiContext), $"#{ContextId} created");
#endif

            FileName = fileName;
            ParentGuiContext = parentGuiContext;
            FileLoader = new AdvancedGuiFileLoader(this);

            ParentGuiContext?.AddChildren();
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
