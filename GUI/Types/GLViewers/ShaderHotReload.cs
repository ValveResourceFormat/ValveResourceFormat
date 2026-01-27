#if DEBUG
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GUI.Utils;
using SlangCompiler;
using ValveResourceFormat.Renderer;
using static SlangCompiler.SlangBindings;

namespace GUI.Types.GLViewers;

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

    private readonly GLBaseControl ViewerControl;
    private readonly ShaderLoader ShaderLoader;

    public event EventHandler<string?>? ShadersReloaded;

    public ShaderHotReload(GLBaseControl viewerControl, ShaderLoader shaderLoader)
    {
        ViewerControl = viewerControl;
        ShaderLoader = shaderLoader;

        ShaderWatcher.Filters.Add("*.slang");
    }

    public void SetSynchronizingObject(ISynchronizeInvoke synchronizingObject)
    {
        Debug.Assert(ShaderWatcher is not null);

        ShaderWatcher.SynchronizingObject = synchronizingObject;

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
        }
    }

    public void ReloadShaders(string? name = null)
    {

        ShaderLoader.slangSession.release();

        SessionDesc slangSessionDesc = new SessionDesc();
        slangSessionDesc.allowGLSLSyntax = true;

        TargetDesc targetDesc = new TargetDesc();

        targetDesc.format = SlangCompileTarget.SLANG_GLSL;
        targetDesc.profile = ShaderLoader.globalSlangSession.findProfile("glsl_460");
        unsafe
        {
            slangSessionDesc.targets = &targetDesc;
        }
        slangSessionDesc.targetCount = 1;
        slangSessionDesc.defaultMatrixLayoutMode = SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR;


        //slangSessionDesc.targets = Marshal.AllocHGlobal(Marshal.SizeOf<TargetDesc>());
        ShaderLoader.globalSlangSession.createSession(slangSessionDesc, out ShaderLoader.slangSession);

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
