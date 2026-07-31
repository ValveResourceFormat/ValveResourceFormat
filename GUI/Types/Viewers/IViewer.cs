using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;

namespace GUI.Types.Viewers
{
    interface IViewer : IDisposable
    {
        public Task LoadAsync(Stream? stream);

        /// <summary>
        /// UI agnostic description of the loaded content. Viewers that implement this instead
        /// of overriding <see cref="Create"/> must not reference WinForms at all, which makes
        /// them trivially portable to a different UI framework.
        /// </summary>
        public ViewerContent? GetContent() => null;

        public void Create(TabPage containerTabPage)
        {
            var content = GetContent()
                ?? throw new NotImplementedException($"{GetType().Name} must implement either GetContent or Create");

            ViewerContentPresenter.Present(containerTabPage, content);
        }

        /// <summary>
        /// Called after the viewer has been made visible (the loading panel was removed). Viewers that render
        /// lazily (e.g. GL viewers) use this to force their first draw. No-op by default.
        /// </summary>
        public void NotifyVisible() { }
    }
}
