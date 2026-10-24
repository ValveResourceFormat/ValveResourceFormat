using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GUI.Types.GLViewers;
using Microsoft.Extensions.Logging;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ToolsAssetInfo;

namespace GUI.Utils
{
    public class VrfGuiContext : GameFileLoader
    {
        public static ILogger Logger { get; } = new GuiLoggerAdapter(nameof(RendererContext));
        private static readonly ILogger FileLoaderLogger = new GuiLoggerAdapter(nameof(GameFileLoader));

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

        public VrfGuiContext? ParentGuiContext { get; }

        /// <summary>
        /// The context at the top of the chain of parent contexts, this one when it has no parent.
        /// </summary>
        public VrfGuiContext RootContext => ParentGuiContext?.RootContext ?? this;

        public ToolsAssetInfo? ToolsAssetInfo { get; private set; }

        /// <summary>
        /// Whether this context is for a package, rather than for a file opened from one.
        /// </summary>
        public bool IsPackage => base.CurrentPackage != null;

        /// <summary>
        /// Path of the file, as a <c>vpk:</c> link when it is inside of a package.
        /// </summary>
        public string DisplayPath
        {
            get
            {
                var chain = GetPackageChain();

                return chain.Count > 1 ? VpkLink.Create(chain) : chain[0];
            }
        }

        /// <summary>
        /// File name followed by the chain of parent files it was opened from,
        /// with paths on disk made relative to their Steam app when they are inside of one.
        /// </summary>
        public string OpenedFromPath
        {
            get
            {
                var path = GetDisplayPath(FileName);

                for (var parentContext = ParentGuiContext; parentContext != null; parentContext = parentContext.ParentGuiContext)
                {
                    path = $"{path} ← {GetDisplayPath(parentContext.FileName)}";
                }

                return path;
            }
        }

        /// <summary>
        /// Same as <see cref="DisplayPath"/> for a file inside of the package of this context,
        /// or for the file of this context itself when it is not a package.
        /// </summary>
        public string GetEntryDisplayPath(string entryPath)
        {
            if (!IsPackage)
            {
                return DisplayPath;
            }

            var chain = GetPackageChain();
            chain.Add(entryPath);

            return VpkLink.Create(chain);
        }

        /// <summary>
        /// Builds the <c>vpk:</c> link to the package of this context, and the packages it is nested in.
        /// </summary>
        public string GetVpkLink() => VpkLink.Create(GetPackageChain());

        /// <summary>
        /// File names from the file on disk down to this file, where each one is inside of the package before it.
        /// Contexts above the file on disk only opened it, so they are not part of its path.
        /// The file on disk is relative to its Steam app when it is inside of one, so links work on other computers.
        /// </summary>
        private List<string> GetPackageChain()
        {
            var chain = new List<string>();

            for (var context = this; context != null; context = context.ParentGuiContext)
            {
                if (Path.IsPathRooted(context.FileName))
                {
                    chain.Add(GetDisplayPath(context.FileName));
                    break;
                }

                chain.Add(context.FileName);
            }

            chain.Reverse();

            return chain;
        }

        /// <summary>
        /// Converts a path on disk to a portable <c>steam:&lt;appid&gt;/&lt;path&gt;</c> path when it is inside of an installed Steam app.
        /// </summary>
        public static string GetDisplayPath(string path)
            => Path.IsPathRooted(path) ? GameFolderLocator.GetSteamAppPath(path, InstalledSteamGames.Games) ?? path : path;

        private bool ToolsAssetInfoLoaded;

        // This is a hack to set camera and properties when clicking a mesh from a model or map
        internal Action<GLBaseControl>? GLPostLoadAction { get; set; }

        // Loading panel listening for the phase this file is being loaded through, if one is up
        internal IProgress<string>? LoadingProgress { get; set; }

        private int Children;
        private bool WantsToBeDisposed;
        private bool Disposed;
        private bool KeepsPackageOpen;
        private readonly ConcurrentDictionary<string, Resource> CachedResources = [];
        private readonly ConcurrentQueue<RendererContext> rendererContexts = [];
        private readonly List<Task> Readers = [];
        private readonly TaskCompletionSource DisposalCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Completes once this context is disposed and has closed its package and the resources loaded through it.
        /// Disposing is deferred while contexts opened from this one are still open.
        /// </summary>
        public Task DisposalCompleted => DisposalCompletion.Task;

#if DEBUG
        private int TotalChildren;
        private static int LastContextId;
        private readonly int ContextId = ++LastContextId;
#endif

        public VrfGuiContext(string fileName, VrfGuiContext? parentGuiContext, bool loadSearchPaths = true) : base(null, fileName, FileLoaderLogger)
        {
#if DEBUG
            Log.Debug(nameof(VrfGuiContext), $"#{ContextId} created");
#endif
            FileName = fileName;
            ParentGuiContext = parentGuiContext;

            ParentGuiContext?.AddChild();

            if (ParentGuiContext != null || !loadSearchPaths)
            {
                return;
            }

            LoadSearchPaths();
        }

        /// <summary>Adds the game search paths from settings.</summary>
        public void LoadSearchPaths()
        {
            try
            {
                var paths = Settings.Config.GameSearchPaths.ToList(); // Make a copy

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

        private void AddChild()
        {
            Children++;

#if DEBUG
            TotalChildren++;
#endif
        }

        private void RemoveChild(VrfGuiContext child)
        {
            DisposeAfter(child.DisposalCompleted);

            if (--Children == 0 && WantsToBeDisposed)
            {
                Dispose();
            }
        }

        /// <summary>
        /// Keeps the package of this context open and <see cref="DisposalCompleted"/> pending until <paramref name="reader"/> completes.
        /// </summary>
        public void DisposeAfter(Task reader)
        {
            lock (Readers)
            {
                Readers.RemoveAll(static task => task.IsCompleted);
                Readers.Add(reader);
            }
        }

        /// <summary>
        /// Lazily loads the tools asset info from disk if not already loaded.
        /// </summary>
        public ToolsAssetInfo? GetOrLoadToolsAssetInfo()
        {
            if (ToolsAssetInfoLoaded)
            {
                return ToolsAssetInfo;
            }

            ToolsAssetInfoLoaded = true;

            var folder = Path.GetDirectoryName(FileName);
            if (folder == null)
            {
                return null;
            }

            var path = Path.Join(folder, "readonly_tools_asset_info.bin");

            if (!File.Exists(path))
            {
                path = Path.Join(Path.GetDirectoryName(folder), "readonly_tools_asset_info.bin");
            }

            if (File.Exists(path))
            {
                ToolsAssetInfo = new ToolsAssetInfo();
                ToolsAssetInfo.Read(path);
            }

            return ToolsAssetInfo;
        }

        [SuppressMessage("Usage", "CA2215:Dispose methods should call base class dispose", Justification = "Deferred to StopLoadingAndDispose, which waits for the loaders first")]
        protected override void Dispose(bool disposing)
        {
            // A package tab's context is disposed by both its tree view and its tab
            if (!disposing || Disposed)
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
            Disposed = true;
            ParentGuiContext?.RemoveChild(this);

            StopLoadingAndDispose(disposing);
        }

        /// <summary>
        /// Leaves the package of this context open when the context is disposed, for the caller to dispose.
        /// </summary>
        /// <returns>The package of this context, not of a parent context.</returns>
        public Package? ReleasePackage()
        {
            KeepsPackageOpen = true;

            return base.CurrentPackage;
        }

        public RendererContext CreateRendererContext()
        {
            var context = new RendererContext(this, Logger)
            {
                FieldOfView = Settings.Config.FieldOfView,
                ViewmodelFieldOfView = Settings.Config.ViewmodelFieldOfView,
                MaxTextureSize = Settings.Config.MaxTextureSize,
            };

            rendererContexts.Enqueue(context);

            return context;
        }

        // Disposal has to wait for readers itself: callers would need the order right on every path,
        // and they do not.
        private void StopLoadingAndDispose(bool disposing)
        {
            foreach (var context in rendererContexts)
            {
                context.CancelLoading();
            }

            // The wait cannot happen on the thread that closed the tab, so the teardown follows it here
            Task.Run(async () =>
            {
                try
                {
                    foreach (var context in rendererContexts)
                    {
                        context.WaitForLoadingToStop();
                    }

                    Task[] pendingReaders;

                    lock (Readers)
                    {
                        pendingReaders = [.. Readers];
                    }

                    try
                    {
                        await Task.WhenAll(pendingReaders).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Log.Error(nameof(VrfGuiContext), $"Failed while waiting for readers of \"{FileName}\": {e}");
                    }

                    if (!KeepsPackageOpen)
                    {
                        base.CurrentPackage?.Dispose();
                    }

                    base.CurrentPackage = null;

                    foreach (var resource in CachedResources.Values)
                    {
                        resource.Dispose();
                    }

                    CachedResources.Clear();

                    base.Dispose(disposing);
                }
                finally
                {
                    DisposalCompletion.TrySetResult();
                }
            });
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
                var packageContext = GetPackageContext(foundFile.Package, foundFile.Context ?? this);

                newContext = new VrfGuiContext(foundFile.PackageEntry!.GetFullPath(), packageContext);
            }

            return (newContext, foundFile.PackageEntry);
        }

        /// <summary>
        /// Returns <paramref name="context"/> if it holds <paramref name="package"/>, otherwise a new context for the package
        /// under contexts for the packages it is nested in. The new contexts leave the package open, and are disposed once
        /// the last context opened from them is disposed.
        /// </summary>
        private static VrfGuiContext GetPackageContext(Package package, VrfGuiContext context)
        {
            if (context.CurrentPackage == package)
            {
                return context;
            }

            if (package is NestedPackage nested)
            {
                return new VrfGuiContext(nested.ParentEntry.GetFullPath(), GetPackageContext(nested.Parent, context))
                {
                    CurrentPackage = package,
                    KeepsPackageOpen = true,
                    WantsToBeDisposed = true,
                };
            }

            var fileName = package.FilePath ?? package.FileName;
            Debug.Assert(fileName is not null);

            return new VrfGuiContext(fileName, context)
            {
                CurrentPackage = package,
                KeepsPackageOpen = true,
                WantsToBeDisposed = true,
            };
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

        // A file opened from another one, such as an entry of a package, finds no game of its own
        protected override GameFileLoader? GameInfoFallback => ParentGuiContext;

        // Override to add support for parent file loaders
        protected override ShaderCollection LoadShaderFromDisk(string shaderName)
        {
            if (ParentGuiContext != null)
            {
                return ParentGuiContext.LoadShaderFromDisk(shaderName);
            }

            return base.LoadShaderFromDisk(shaderName);
        }

        /// <summary>Loader that skips resource cache.</summary>
        public IFileLoader FileLoaderNoCache => fileLoaderNoCache ??= new UncachedFileLoader(this);

        private UncachedFileLoader? fileLoaderNoCache;

        private sealed class UncachedFileLoader(VrfGuiContext context) : IFileLoader
        {
            public Resource? LoadFile(string file) => context.LoadFileUncached(file);
            public Resource? LoadFileCompiled(string file) => LoadFile(string.Concat(file, CompiledFileSuffix));
            public ShaderCollection? LoadShader(string shaderName) => context.LoadShader(shaderName);
            public Stream? GetFileStream(string file) => context.GetFileStream(file);
        }

        private Resource? LoadFileUncached(string file) => base.LoadFile(file);

        // Override to add support for caching resources
        public override Resource? LoadFile(string file)
        {
            // Some files come with backward slashes which ruin our cache
            file = file.Replace('\\', '/');

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
