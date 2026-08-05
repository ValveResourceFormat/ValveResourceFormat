#if DEBUG
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.Renderer.Shaders;

namespace GUI.Types.GLViewers;

internal class ShaderHotReload : IDisposable
{
    // The built-in shader folder, plus every directory mounted through ShaderRegistry
    private List<FileSystemWatcher>? ShaderWatchers = CreateWatchers();

    private static List<FileSystemWatcher> CreateWatchers()
    {
        var watchers = new List<FileSystemWatcher>(1 + ShaderRegistry.Directories.Length);
        var paths = new List<string>(watchers.Capacity);
        paths.AddRange(ShaderRegistry.Directories);

        // Only present when this assembly was built from the shader source files, rather than using the embedded copies
        if (ShaderParser.ShaderSourceDirectory != null)
        {
            paths.Add(ShaderParser.ShaderSourceDirectory);
        }

        foreach (var path in paths)
        {
            watchers.Add(new FileSystemWatcher
            {
                Path = path,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                Filters = { "*.slang" },
            });
        }

        return watchers;
    }

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

    private readonly GLBaseControl ViewerControl;
    private readonly ShaderLoader ShaderLoader;

    public event EventHandler<string?>? ShadersReloaded;

    public ShaderHotReload(GLBaseControl viewerControl, ShaderLoader shaderLoader)
    {
        ViewerControl = viewerControl;
        ShaderLoader = shaderLoader;
    }

    public void SetSynchronizingObject(ISynchronizeInvoke synchronizingObject)
    {
        Debug.Assert(ShaderWatchers is not null);

        foreach (var watcher in ShaderWatchers)
        {
            watcher.SynchronizingObject = synchronizingObject;

            watcher.Changed += Hotload;
            watcher.Created += Hotload;
            watcher.Renamed += Hotload;
        }
    }

    public void Dispose()
    {
        if (ShaderWatchers != null)
        {
            foreach (var watcher in ShaderWatchers)
            {
                watcher.Changed -= Hotload;
                watcher.Created -= Hotload;
                watcher.Renamed -= Hotload;
                watcher.Dispose();
            }

            ShaderWatchers = null;

            reloadSemaphore.Dispose();
        }
    }

    public void ReloadShaders(string? name = null)
    {
        using var lockedGl = ViewerControl.MakeCurrent();
        ShaderLoader.ReloadAllShaders(name);
        ShadersReloaded?.Invoke(this, name);
        ViewerControl.GLControl?.Invalidate();
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
        Application.DoEvents(); // Force the updated text to show up

        try
        {
            ReloadShaders(e.Name);
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
    }
}
#endif
