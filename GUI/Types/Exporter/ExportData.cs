using System.Threading.Tasks;
using GUI.Utils;
using ValvePak;

namespace GUI.Types.Exporter
{
    class ExportData
    {
        private readonly TaskCompletionSource<Exception?> loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PackageEntry? PackageEntry { get; set; }
        public required VrfGuiContext VrfGuiContext { get; set; }
        public IDisposable? DisposableContents { get; set; }

        /// <summary>Completes once the tab showing this has finished loading, with the exception that stopped it or null.</summary>
        public Task<Exception?> Loaded => loaded.Task;

        public void SetLoaded(Exception? error) => loaded.TrySetResult(error);
    }
}
