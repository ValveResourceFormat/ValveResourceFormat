using System.Threading.Tasks;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;

namespace GUI.Types.Exporter
{
    class ExportData
    {
        private readonly TaskCompletionSource<Exception?> loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PackageEntry? PackageEntry { get; set; }
        public required VrfGuiContext VrfGuiContext { get; set; }

        /// <summary>
        /// Gets whether files are loaded past the context's resource cache. The cache of a viewer tab's context holds
        /// the resources its renderer uses, which have dropped the mesh data they uploaded and must not be disposed by
        /// an extract.
        /// </summary>
        public bool BypassResourceCache { get; init; }

        /// <summary>Gets the loader the files are extracted with.</summary>
        public IFileLoader FileLoader => BypassResourceCache ? VrfGuiContext.FileLoaderNoCache : VrfGuiContext;
        public IDisposable? DisposableContents { get; set; }

        /// <summary>Completes once the tab showing this has finished loading, with the exception that stopped it or null.</summary>
        public Task<Exception?> Loaded => loaded.Task;

        public void SetLoaded(Exception? error) => loaded.TrySetResult(error);
    }
}
