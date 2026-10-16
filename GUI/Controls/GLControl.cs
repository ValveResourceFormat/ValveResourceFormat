using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenTK.Windowing.GraphicsLibraryFramework;
using NativeWindow = OpenTK.Windowing.Desktop.NativeWindow;
using ValveResourceFormat.Renderer.RHI;
using static ValveResourceFormat.Renderer.RHI.S2vDevice;

namespace GUI.Controls;

/// <summary>
/// WinForms control holding a Swapchain through which it can be drawn to via Vulkan.
/// </summary>
public class GLControl : Control
{
    public GLControl()
    {
        SetStyle(ControlStyles.Opaque, true);
        SetStyle(ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = false;
    }

    protected override void Dispose(bool disposing)
    {
        //if (disposing)
        //{
        //    DestroyNativeWindow();
        //}
        base.Dispose(disposing);
    }

    /// <summary>
    /// This event handler will be invoked by WinForms when the HWND of this
    /// control itself has been created and assigned in the Handle property.
    /// </summary>
    /// <param name="e">An EventArgs instance (ignored).</param>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
    }

    /// <summary>
    /// Gets the CreateParams instance for this <see cref="GLControl"/>.
    /// This is overridden to force correct child behavior.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            const int CS_VREDRAW = 0x1;
            const int CS_HREDRAW = 0x2;
            const int CS_OWNDC = 0x20;

            var cp = base.CreateParams;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                cp.ClassStyle |= CS_VREDRAW | CS_HREDRAW | CS_OWNDC;
            }
            return cp;
        }
    }

    /// <summary>
    /// This is triggered when the underlying Handle/HWND instance is *about to be*
    /// destroyed (this is called *before* the Handle/HWND is destroyed).  We use it
    /// to cleanly destroy the swapchain before its parent disappears.
    /// </summary>
    /// <param name="e">An EventArgs instance (ignored).</param>
    protected override void OnHandleDestroyed(EventArgs e)
    {
        base.OnHandleDestroyed(e);
    }

    /// <summary>
    /// Key for the <see cref="Load"/> handler in the Events collection, only needed
    /// when the Load event is used.
    /// </summary>
    private static readonly object EVENT_LOAD = new();

    /// <summary>
    /// An event hook, triggered when the control is created for the first time.
    /// </summary>
    [Category("Behavior")]
    [Description("Occurs when the GLControl is first created.")]
    public event EventHandler Load
    {
        add => Events.AddHandler(EVENT_LOAD, value);
        remove => Events.RemoveHandler(EVENT_LOAD, value);
    }

    /// <summary>
    /// Raises the CreateControl event.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected override void OnCreateControl()
    {
        base.OnCreateControl();

        OnLoad(EventArgs.Empty);
    }

    /// <summary>
    /// The <see cref="Load"/> event is fired before the control becomes visible for the first time.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnLoad(EventArgs e)
    {
        // There is no good way to explain this event except to say
        // that it's just another name for OnControlCreated.
        ((EventHandler?)Events[EVENT_LOAD])?.Invoke(this, e);
    }
}
