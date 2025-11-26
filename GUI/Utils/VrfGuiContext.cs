using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GUI.Types.GLViewers;
using GUI.Types.Renderer;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using ValveResourceFormat.ToolsAssetInfo;

namespace GUI.Utils
{
    class VrfGuiContext : GameFileLoader, IDisposable
    {
        public string FileName { get; }

        public new Package? CurrentPackage
        {
            get
            {
                // TODO: Due to slight mess with current/parent contexts, return parent package if available to keep old behaviour
                return base.CurrentPackage ?? ParentGuiContext?.CurrentPackage;
            }
            set
            {
                base.CurrentPackage = value;
            }
        }

        public MaterialLoader MaterialLoader { get; }
        public ShaderLoader ShaderLoader { get; }
        public GPUMeshBufferCache MeshBufferCache { get; }
        public VrfGuiContext? ParentGuiContext { get; }
        public ToolsAssetInfo? ToolsAssetInfo { get; set; }

        // This is a hack to set camera and properties when clicking a mesh from a model or map
        public Action<GLViewerControl>? GLPostLoadAction { get; set; }

        private int Children;
        private bool WantsToBeDisposed;
        private readonly ConcurrentDictionary<string, Resource> CachedResources = [];

#if DEBUG
        private int TotalChildren;
        private static int LastContextId;
        private readonly int ContextId = ++LastContextId;
#endif

        public VrfGuiContext(string fileName, VrfGuiContext? parentGuiContext) : base(null, fileName)
        {
#if DEBUG
            Log.Debug(nameof(VrfGuiContext), $"#{ContextId} created");
#endif

            MaterialLoader = new MaterialLoader(this);
            ShaderLoader = new ShaderLoader(this);
            MeshBufferCache = new GPUMeshBufferCache();

            FileName = fileName;
            ParentGuiContext = parentGuiContext;

            ParentGuiContext?.AddChildren();

            if (ParentGuiContext != null)
            {
                return;
            }

            var paths = Settings.Config.GameSearchPaths.ToList(); // Make a copy

            try
            {
                // Find any gameinfo files specified by the user in settings
                foreach (var searchPath in paths.Where(searchPath => searchPath.EndsWith("gameinfo.gi", StringComparison.InvariantCulture)).ToList())
                {
                    paths.Remove(searchPath);

                    FindAndLoadSearchPaths(searchPath);
                }

                // Find any .vpk files specified by the user
                foreach (var searchPath in paths.Where(searchPath => searchPath.EndsWith(".vpk", StringComparison.InvariantCulture)).ToList())
                {
                    paths.Remove(searchPath);

                    AddPackageToSearch(searchPath);
                }

                // Add remaining paths specified by the user
                foreach (var searchPath in paths)
                {
                    AddDiskPathToSearch(searchPath);
                }
            }
            catch (Exception e)
            {
                Log.Error(nameof(VrfGuiContext), $"Failed to add search path: {e}");
                return;
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

        public void ClearCache()
        {
            foreach (var resource in CachedResources.Values)
            {
                resource.Dispose();
            }

            CachedResources.Clear();

            //ShaderLoader.ClearCache();
        }

        protected override void Dispose(bool disposing)
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
            ParentGuiContext?.RemoveChildren();

            if (base.CurrentPackage != null)
            {
                base.CurrentPackage.Dispose();
                base.CurrentPackage = null;
            }

            ShaderLoader?.Dispose();

            ClearCache();

            base.Dispose(disposing);
        }

        public (VrfGuiContext? Context, PackageEntry? PackageEntry) FindFileWithContext(string file)
        {
            var foundFile = Path.IsPathRooted(file) switch
            {
                true => File.Exists(file) ? (PathOnDisk: file, null, null, null) : default,
                false => FindFileWithContextRecursive(file)
            };

            if (foundFile.PackageEntry == null && foundFile.PathOnDisk == null)
            {
                return (null, null);
            }

            VrfGuiContext? newContext = null;

            if (foundFile.PathOnDisk != null)
            {
                newContext = new VrfGuiContext(foundFile.PathOnDisk, null);
            }

            if (foundFile.Package != null)
            {
                Debug.Assert(foundFile.Package.FileName is not null);

                var parentContext = new VrfGuiContext(foundFile.Package.FileName, foundFile.Context ?? this)
                {
                    CurrentPackage = foundFile.Package
                };

                newContext = new VrfGuiContext(foundFile.PackageEntry!.GetFullPath(), parentContext);
            }

            return (newContext, foundFile.PackageEntry);
        }

        // Same as FindFile, but also returns the context and package that the file was found in
        private (string? PathOnDisk, VrfGuiContext? Context, Package? Package, PackageEntry? PackageEntry) FindFileWithContextRecursive(string file)
        {
            var (pathOnDisk, package, packageEntry) = base.FindFile(file);

            if (pathOnDisk != null || packageEntry != null || ParentGuiContext == null)
            {
                return (pathOnDisk, this, package, packageEntry);
            }

            return ParentGuiContext.FindFileWithContextRecursive(file);
        }

        // Override to add support for parent file loaders
        public override (string? PathOnDisk, Package? Package, PackageEntry? PackageEntry) FindFile(string file, bool logNotFound = true)
        {
            var parent = ParentGuiContext;
            var shouldLogNotFound = logNotFound && parent == null;

            var entry = base.FindFile(file, shouldLogNotFound);

            if (parent == null || entry.PathOnDisk != null || entry.PackageEntry != null)
            {
                return entry;
            }

            return parent.FindFile(file, logNotFound);
        }

        // Override to add support for parent file loaders
        protected override ShaderCollection LoadShaderFromDisk(string shaderName)
        {
            if (ParentGuiContext != null)
            {
                return ParentGuiContext.LoadShaderFromDisk(shaderName);
            }

            return base.LoadShaderFromDisk(shaderName);
        }

        // Override to add support for caching resources
        public override Resource? LoadFile(string file)
        {
            // TODO: Might conflict where same file name is available in different paths
            if (CachedResources.TryGetValue(file, out var resource) && resource.Reader != null)
            {
                return resource;
            }

            resource = base.LoadFile(file);

            if (resource != null)
            {
                CachedResources[file] = resource;
            }

            return resource;
        }

        public override Resource? LoadFileCompiled(string file) => LoadFile(string.Concat(file, CompiledFileSuffix));
    }
}
