using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using NativeWindow = OpenTK.Windowing.Desktop.NativeWindow;

namespace GUI.Controls;

/// <summary>
/// OpenGL-capable WinForms control that is a specialized wrapper around
/// OpenTK's NativeWindow.
/// </summary>
/// <see href="https://github.com/opentk/GLControl/blob/9b3e527aac9b5f5759c5f637aa57da5d3821a32f/OpenTK.GLControl/GLControl.cs"/>
public class GLControl : Control
{
    /// <summary>
    /// The OpenGL configuration of this control.
    /// </summary>
    private readonly NativeWindowSettings _glControlSettings;

    /// <summary>
    /// The underlying native window.  This will be reparented to be a child of
    /// this control.
    /// </summary>
    private NativeWindow? _nativeWindow;

    // Indicates that OnResize was called before OnHandleCreated.
    // To avoid issues with missing OpenGL contexts, we suppress
    // the premature Resize event and raise it as soon as the handle
    // is ready.
    private bool _resizeEventSuppressed;

    /// <summary>
    /// Gets the <see cref="IGraphicsContext"/> instance that is associated with the <see cref="GLControl"/>.
    /// </summary>
    [Browsable(false)]
    public IGLFWGraphicsContext? Context => _nativeWindow?.Context;

    /// <summary>
    /// Constructs a new instance with the specified GLControlSettings.
    /// </summary>
    /// <param name="glControlSettings">The preferred configuration for the OpenGL  renderer.</param>
    public GLControl(NativeWindowSettings glControlSettings)
    {
        SetStyle(ControlStyles.Opaque, true);
        SetStyle(ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = false;

        _glControlSettings = glControlSettings;
        _glControlSettings.StartFocused = false;
        _glControlSettings.StartVisible = false;
        _glControlSettings.WindowBorder = WindowBorder.Hidden;
        _glControlSettings.WindowState = WindowState.Normal;

        CreateNativeWindow();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DestroyNativeWindow();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// This event handler will be invoked by WinForms when the HWND of this
    /// control itself has been created and assigned in the Handle property.
    /// We capture the event to construct the NativeWindow that will be responsible
    /// for all of the actual OpenGL rendering and native device input.
    /// </summary>
    /// <param name="e">An EventArgs instance (ignored).</param>
    protected override void OnHandleCreated(EventArgs e)
    {
        Debug.Assert(_nativeWindow != null);

        NonportableReparent(_nativeWindow);

        // Force the newly child-ified GLFW window to be resized to fit this control.
        ResizeNativeWindow();

        // And now show the child window, since it hasn't been made visible yet.
        _nativeWindow.IsVisible = true;

        base.OnHandleCreated(e);

        if (_resizeEventSuppressed)
        {
            OnResize(EventArgs.Empty);
            _resizeEventSuppressed = false;
        }
    }

    /// <summary>
    /// Construct the child NativeWindow that will wrap the underlying GLFW instance.
    /// </summary>
    private void CreateNativeWindow()
    {
        if (DesignMode)
        {
            return;
        }

        _nativeWindow = new NativeWindow(_glControlSettings);
    }

    /// <summary>
    /// Gets the CreateParams instance for this GLControl.
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
    /// Reparent the given NativeWindow to be a child of this GLControl.  This is a
    /// non-portable operation, as its name implies:  It works wildly differently
    /// between OSes.  The current implementation only supports Microsoft Windows.
    /// </summary>
    /// <param name="nativeWindow">The NativeWindow that must become a child of this control.</param>
    private void NonportableReparent(NativeWindow nativeWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new NotSupportedException("The current operating system is not supported by this control.");
        }

        Windows.Win32.Foundation.HWND hWnd;
        unsafe
        {
            hWnd = (Windows.Win32.Foundation.HWND)GLFW.GetWin32Window(nativeWindow.WindowPtr);
        }

        // Change the real HWND's window styles to be "WS_CHILD | WS_DISABLED" (i.e.,
        // a child of some container, with no input support), and turn off *all* the
        // other style bits (most of the rest of them could cause trouble).  In
        // particular, this turns off stuff like WS_BORDER and WS_CAPTION and WS_POPUP
        // and so on, any of which GLFW might have turned on for us.
        var style = (IntPtr)(Windows.Win32.UI.WindowsAndMessaging.WINDOW_STYLE.WS_CHILD
            | Windows.Win32.UI.WindowsAndMessaging.WINDOW_STYLE.WS_DISABLED);

        Windows.Win32.PInvoke.SetWindowLongPtr(hWnd, Windows.Win32.UI.WindowsAndMessaging.WINDOW_LONG_PTR_INDEX.GWL_STYLE, style);

        // Change the real HWND's extended window styles to be "WS_EX_NOACTIVATE", and
        // turn off *all* the other extended style bits (most of the rest of them
        // could cause trouble).  We want WS_EX_NOACTIVATE because we don't want
        // Windows mistakenly giving the GLFW window the focus as soon as it's created,
        // regardless of whether it's a hidden window.
        style = (IntPtr)Windows.Win32.UI.WindowsAndMessaging.WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
        Windows.Win32.PInvoke.SetWindowLongPtr(hWnd, Windows.Win32.UI.WindowsAndMessaging.WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, style);

        // Reparent the real HWND under this control.
        Windows.Win32.PInvoke.SetParent(hWnd, (Windows.Win32.Foundation.HWND)Handle);
    }

    /// <summary>
    /// This is triggered when the underlying Handle/HWND instance is *about to be*
    /// destroyed (this is called *before* the Handle/HWND is destroyed).  We use it
    /// to cleanly destroy the NativeWindow before its parent disappears.
    /// </summary>
    /// <param name="e">An EventArgs instance (ignored).</param>
    protected override void OnHandleDestroyed(EventArgs e)
    {
        base.OnHandleDestroyed(e);

        DestroyNativeWindow();
    }

    /// <summary>
    /// Destroy the child NativeWindow that wraps the underlying GLFW instance.
    /// </summary>
    private void DestroyNativeWindow()
    {
        if (_nativeWindow != null)
        {
            _nativeWindow.Dispose();
            _nativeWindow = null;
        }
    }

    /// <summary>
    /// This private object is used as the reference for the 'Load' handler in
    /// the Events collection, and is only needed if you use the 'Load' event.
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
    /// The Load event is fired before the control becomes visible for the first time.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnLoad(EventArgs e)
    {
        // There is no good way to explain this event except to say
        // that it's just another name for OnControlCreated.
        ((EventHandler?)Events[EVENT_LOAD])?.Invoke(this, e);
    }

    /// <summary>
    /// This is invoked when the Resize event is triggered, and is used to position
    /// the internal GLFW window accordingly.
    ///
    /// Note: This method may be called before the OpenGL context is ready or the
    /// NativeWindow even exists, so everything inside it requires safety checks.
    /// </summary>
    /// <param name="e">An EventArgs instance (ignored).</param>
    protected override void OnResize(EventArgs e)
    {
        // Do not raise OnResize event before the handle and context are created.
        if (!IsHandleCreated)
        {
            _resizeEventSuppressed = true;
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            BeginInvoke(new Action(ResizeNativeWindow)); // Need the native window to resize first otherwise our control will be in the wrong place.
        }
        else
        {
            ResizeNativeWindow();
        }

        base.OnResize(e);
    }

    /// <summary>
    /// Resize the native window to fit this control.
    /// </summary>
    private void ResizeNativeWindow()
    {
        if (DesignMode)
        {
            return;
        }

        if (_nativeWindow != null)
        {
            _nativeWindow.ClientRectangle = new Box2i(0, 0, Width, Height);
        }
    }

    /// <summary>
    /// This event is raised when this control's parent control is changed,
    /// which may result in this control becoming a different size or shape, so
    /// we capture it to ensure that the underlying GLFW window gets correctly
    /// resized and repositioned as well.
    /// </summary>
    /// <param name="e">An EventArgs instance (ignored).</param>
    protected override void OnParentChanged(EventArgs e)
    {
        ResizeNativeWindow();

        base.OnParentChanged(e);
    }

    /// <summary>
    /// Swaps the front and back buffers, presenting the rendered scene to the user.
    /// </summary>
    public void SwapBuffers()
    {
        if (DesignMode)
        {
            return;
        }

        Debug.Assert(_nativeWindow != null);

        _nativeWindow.Context.SwapBuffers();
    }

    /// <summary>
    /// Makes this control's OpenGL context current in the calling thread.
    /// All OpenGL commands issued are hereafter interpreted by this context.
    /// When using multiple GLControls, calling MakeCurrent on one control
    /// will make all other controls non-current in the calling thread.
    /// A GLControl can only be current in one thread at a time.
    /// </summary>
    public void MakeCurrent()
    {
        if (DesignMode)
        {
            return;
        }

        Debug.Assert(_nativeWindow != null);

        _nativeWindow.MakeCurrent();
    }
}
