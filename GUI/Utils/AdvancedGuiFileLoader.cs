using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;

namespace GUI.Utils
{
    class AdvancedGuiFileLoader : GameFileLoader
    {
        private readonly ConcurrentDictionary<string, Resource> CachedResources = [];
        private readonly VrfGuiContext GuiContext;

        public AdvancedGuiFileLoader(VrfGuiContext guiContext) : base(null, guiContext.FileName)
        {
            GuiContext = guiContext;

            if (guiContext.ParentGuiContext != null)
            {
                return;
            }

            var paths = Settings.Config.GameSearchPaths.ToList();

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
                Log.Error(nameof(AdvancedGuiFileLoader), $"Failed to add search path: {e}");
                return;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearCache();

                base.Dispose(disposing);
            }
        }

        public void ClearCache()
        {
            foreach (var resource in CachedResources.Values)
            {
                resource.Dispose();
            }

            CachedResources.Clear();
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
                var parentContext = new VrfGuiContext(foundFile.Package.FileName, foundFile.Context ?? GuiContext)
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

            if (pathOnDisk != null || packageEntry != null || GuiContext.ParentGuiContext == null)
            {
                return (pathOnDisk, GuiContext, package, packageEntry);
            }

            return GuiContext.ParentGuiContext.FileLoader.FindFileWithContextRecursive(file);
        }

        // Override to add support for parent file loaders
        public override (string? PathOnDisk, Package? Package, PackageEntry? PackageEntry) FindFile(string file, bool logNotFound = true)
        {
            var parent = GuiContext.ParentGuiContext;
            var shouldLogNotFound = logNotFound && parent == null;

            var entry = base.FindFile(file, shouldLogNotFound);

            if (parent == null || entry.PathOnDisk != null || entry.PackageEntry != null)
            {
                return entry;
            }

            return parent.FileLoader.FindFile(file, logNotFound);
        }

        // Override to add support for parent file loaders
        protected override ShaderCollection LoadShaderFromDisk(string shaderName)
        {
            if (GuiContext.ParentGuiContext != null)
            {
                return GuiContext.ParentGuiContext.FileLoader.LoadShaderFromDisk(shaderName);
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
