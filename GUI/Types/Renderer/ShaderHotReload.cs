#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;

namespace GUI.Types.Renderer;

internal class ShaderHotReload : IDisposable
{
    private FileSystemWatcher? ShaderWatcher = new()
    {
        Path = ShaderParser.GetShaderDiskPath(string.Empty),
        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        IncludeSubdirectories = true,
        EnableRaisingEvents = true,
    };

    private readonly TaskDialogPage errorReloadingPage = new()
    {
        SizeToContent = true,
        AllowCancel = true,
        Buttons = { TaskDialogButton.OK },
        Icon = TaskDialogIcon.Error,
    };

    private static readonly TimeSpan changeCoolDown = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan reloadCoolDown = TimeSpan.FromSeconds(0.5); // There is a change that happens right after reload

    private readonly SemaphoreSlim reloadSemaphore = new(1, 1);
    private DateTime lastChanged;
    private DateTime lastReload;

    public GLViewerControl? ViewerControl { get; private set; }
    public event EventHandler<string?>? ReloadShader;

    public ShaderHotReload()
    {
        ShaderWatcher.Filters.Add("*.slang");

        ShaderWatcher.Changed += Hotload;
        ShaderWatcher.Created += Hotload;
        ShaderWatcher.Renamed += Hotload;
    }

    public void Dispose()
    {
        if (ShaderWatcher != null)
        {
            ShaderWatcher.Changed -= Hotload;
            ShaderWatcher.Created -= Hotload;
            ShaderWatcher.Renamed -= Hotload;
            ShaderWatcher.Dispose();
            ShaderWatcher = null;

            reloadSemaphore.Dispose();
            ViewerControl = null;
        }
    }

    public void SetControl(GLViewerControl glControl)
    {
        if (ShaderWatcher == null)
        {
            return;
        }

        ShaderWatcher.SynchronizingObject = glControl.GLControl;
        ViewerControl = glControl;
    }

    private void Hotload(object sender, FileSystemEventArgs e)
    {
        if (ViewerControl?.GLControl?.Parent == null)
        {
            Dispose();
            return;
        }

        if (!ViewerControl.GLControl.Parent.Visible)
        {
            return;
        }

        if (e.FullPath.EndsWith(".TMP", StringComparison.Ordinal))
        {
            return; // Visual Studio writes to temporary file
        }

        Log.Debug(nameof(ShaderHotReload), $"{e.ChangeType} {e.FullPath}");

        var now = DateTime.Now;
        var timeSinceLastChange = now - lastChanged;
        var timeSinceLastReload = now - lastReload;

        if (reloadSemaphore.CurrentCount == 0
            || timeSinceLastReload < reloadCoolDown
            || timeSinceLastChange < changeCoolDown)
        {
            return;
        }

        lastChanged = now;

        if (!reloadSemaphore.Wait(0))
        {
            return;
        }

        var reloadStopwatch = Stopwatch.StartNew();

        if (errorReloadingPage.BoundDialog != null)
        {
            errorReloadingPage.Caption = "Reloading shaders…";
        }

        string? error = null;
        var title = Program.MainForm.Text;
        Program.MainForm.Text = "Source 2 Viewer - Reloading shaders…";

        try
        {
            ReloadShader?.Invoke(sender, e.Name);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Error(nameof(ShaderHotReload), error.ToString());
        }
        finally
        {
            lastReload = DateTime.Now;
            reloadSemaphore.Release();
            reloadStopwatch.Stop();
            Log.Debug(nameof(ShaderHotReload), $"Shader reload time: {reloadStopwatch.Elapsed}");
            Program.MainForm.Text = title;
        }

        if (error != null)
        {
            errorReloadingPage.Caption = "Failed to reload shaders";
            errorReloadingPage.Text = error;

            if (errorReloadingPage.BoundDialog == null)
            {
                TaskDialog.ShowDialog(Program.MainForm, errorReloadingPage);
            }
        }
        else
        {
            errorReloadingPage.BoundDialog?.Close();
        }

        ViewerControl.GLControl.Invalidate();
    }
}
#endif
