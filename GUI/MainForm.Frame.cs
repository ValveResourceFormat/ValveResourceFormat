using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GUI.Controls;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

// !!!!! BEWARE !!!!!
// This file contains some *pristine* bullshit, it handles all the windows API messages to extend
// the client area into the title bar, which allows us to have a custom title bar.

// Seriously, Here be dragons! We spent around 2 weeks on this and I still don't understand why half of this
// stuff works so edit with caution.

namespace GUI;

partial class MainForm
{
    // Custom system menu item IDs (must be multiples of 0x10 due to WM_SYSCOMMAND masking)
    private const int SC_WEBSITE = 0x1000;
    private const int SC_SETTINGS = 0x1010;
    private const int SC_ABOUT = 0x1020;

    public bool IsWindowMaximised()
    {
        WINDOWPLACEMENT placement = default;
        PInvoke.GetWindowPlacement((HWND)Handle, ref placement);

        return placement.showCmd == SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED;
    }

    public static Point LParamToPoint(IntPtr ptr)
    {
        return new Point(IntPtr.Size == 8 ? unchecked((int)ptr.ToInt64()) : ptr.ToInt32());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == PInvoke.WM_NCCALCSIZE && (int)m.WParam == 1)
        {
            var padding = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXPADDEDBORDER, (uint)DeviceDpi);
            var frameX = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXFRAME, (uint)DeviceDpi);
            var frameY = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CYFRAME, (uint)DeviceDpi);

            var nccsp = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(m.LParam);
            nccsp.rgrc._0.bottom -= frameY;
            nccsp.rgrc._0.right -= frameX;
            nccsp.rgrc._0.left += frameX;

            if (IsWindowMaximised())
            {
                nccsp.rgrc._0.bottom -= padding;
                nccsp.rgrc._0.right -= padding;
                nccsp.rgrc._0.left += padding;
                nccsp.rgrc._0.top += padding;
            }

            Marshal.StructureToPtr(nccsp, m.LParam, false);

            m.Result = IntPtr.Zero;
            return;
        }
        else if (m.Msg == PInvoke.WM_NCHITTEST)
        {
            var dwmHandled = PInvoke.DwmDefWindowProc((HWND)m.HWnd, (uint)m.Msg, new WPARAM((nuint)m.WParam), new LPARAM(m.LParam), out var result);

            if (dwmHandled == 1)
            {
                m.Result = result;
                return;
            }

            var rawPoint = LParamToPoint(m.LParam);

            // Convert to client coordinates
            var point = PointToClient(rawPoint);
            var controlsBoxPanelPoint = controlsBoxPanel.PointToClient(rawPoint);

            // Updating here instead of in the ControlsBoxPanel class is better because we can tell when we are outside
            // of the panel here, and correctly set NONE.
            controlsBoxPanel.CheckControlBoxHoverState(controlsBoxPanelPoint);

            // Only run top scaling logic when not fullscreened so the window can be dragged even if the cursor is at the very top of the screen.
            if (!IsWindowMaximised())
            {
                var padding = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXPADDEDBORDER, (uint)DeviceDpi);
                var frameX = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXFRAME, (uint)DeviceDpi);

                if (point.Y - padding <= menuStrip.Top)
                {
                    controlsBoxPanel.CurrentHoveredButton = ControlsBoxPanel.CustomTitleBarHoveredButton.None;

                    if (point.X <= frameX)
                    {
                        m.Result = new IntPtr(PInvoke.HTTOPLEFT);
                        return;
                    }
                    else if (point.X >= ClientSize.Width - frameX)
                    {
                        m.Result = new IntPtr(PInvoke.HTTOPRIGHT);
                        return;
                    }

                    // Regular top edge
                    m.Result = new IntPtr(PInvoke.HTTOP);
                    return;
                }

                if (point.Y < menuStrip.Height)
                {
                    var edgeSize = frameX + padding;

                    if (point.X <= edgeSize)
                    {
                        m.Result = new IntPtr(PInvoke.HTLEFT);
                        return;
                    }
                    else if (point.X >= ClientSize.Width - edgeSize)
                    {
                        m.Result = new IntPtr(PInvoke.HTRIGHT);
                        return;
                    }
                }
            }

            if (point.Y < menuStrip.Height)
            {
                if (controlsBoxPanel.CurrentHoveredButton != ControlsBoxPanel.CustomTitleBarHoveredButton.None)
                {
                    switch (controlsBoxPanel.CurrentHoveredButton)
                    {
                        case ControlsBoxPanel.CustomTitleBarHoveredButton.Maximize:
                            m.Result = new IntPtr(PInvoke.HTMAXBUTTON);
                            return;
                        case ControlsBoxPanel.CustomTitleBarHoveredButton.Minimize:
                            m.Result = new IntPtr(PInvoke.HTMINBUTTON);
                            return;
                        case ControlsBoxPanel.CustomTitleBarHoveredButton.Close:
                            m.Result = new IntPtr(PInvoke.HTCLOSE);
                            return;
                    }
                }

                m.Result = new IntPtr(PInvoke.HTCAPTION);
                return;
            }
        }
        else if (m.Msg == PInvoke.WM_NCLBUTTONDOWN)
        {
            if ((uint)m.WParam is PInvoke.HTCLOSE or PInvoke.HTMINBUTTON or PInvoke.HTMAXBUTTON)
            {
                m.Result = 0;
                return;
            }
        }
        else if (m.Msg == PInvoke.WM_NCLBUTTONUP)
        {
            if (m.WParam == PInvoke.HTCLOSE)
            {
                PInvoke.PostMessage((HWND)Handle, PInvoke.WM_CLOSE, 0, 0);
                m.Result = 0;
                return;
            }
            else if (m.WParam == PInvoke.HTMINBUTTON)
            {
                PInvoke.ShowWindow((HWND)Handle, SHOW_WINDOW_CMD.SW_MINIMIZE);
                m.Result = 0;
                return;
            }
            else if (m.WParam == PInvoke.HTMAXBUTTON)
            {
                var mode = IsWindowMaximised() ? SHOW_WINDOW_CMD.SW_NORMAL : SHOW_WINDOW_CMD.SW_MAXIMIZE;
                PInvoke.ShowWindow((HWND)Handle, mode);
                m.Result = 0;
                return;
            }
        }
        else if (m.Msg == PInvoke.WM_NCRBUTTONUP)
        {
            if (m.WParam == PInvoke.HTCAPTION)
            {
                var point = PointToScreen(PointToClient(LParamToPoint(m.LParam)));
                OpenSystemMenu(point);
                return;
            }
        }
        else if (m.Msg == PInvoke.WM_SIZE)
        {
            // Needed to make sure hover state is updated correctly when the window is maximised.
            // TODO: controlsBoxPanel IS null at start
            if (controlsBoxPanel != null)
            {
                var controlsBoxPanelPoint = controlsBoxPanel.PointToClient(LParamToPoint(m.LParam));
                controlsBoxPanel.CheckControlBoxHoverState(controlsBoxPanelPoint);
                controlsBoxPanel.Invalidate();
            }
        }
        else if (m.Msg == PInvoke.WM_SYSCOMMAND)
        {
            var command = (int)m.WParam & 0xFFF0;

            if (command == SC_WEBSITE)
            {
                using var aboutForm = new Forms.AboutForm();
                aboutForm.OnWebsiteClick(this, EventArgs.Empty);
                m.Result = 0;
                return;
            }
            else if (command == SC_SETTINGS)
            {
                OnSettingsItemClick(this, EventArgs.Empty);
                m.Result = 0;
                return;
            }
            else if (command == SC_ABOUT)
            {
                OnAboutItemClick(this, EventArgs.Empty);
                m.Result = 0;
                return;
            }
        }

        base.WndProc(ref m);
    }

    private void OnMainLogoClick(object sender, EventArgs e)
    {
        var control = (Control)sender;
        var point = PointToScreen(new Point(control.Left, control.Top + control.Height));
        OpenSystemMenu(point);
    }

    private unsafe void OpenSystemMenu(Point point)
    {
        var hwnd = (HWND)Handle;
        var menu = PInvoke.GetSystemMenu(hwnd, false);

        var cmd = PInvoke.TrackPopupMenu(menu, TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD, point.X, point.Y, 0, hwnd);

        if (cmd != 0)
        {
            PInvoke.SendMessage(hwnd, PInvoke.WM_SYSCOMMAND, (WPARAM)(uint)cmd.Value, 0);
        }
    }

    private unsafe void InitializeSystemMenu()
    {
        var hwnd = (HWND)Handle;
        var menu = PInvoke.GetSystemMenu(hwnd, false);

        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);

        fixed (char* p = "Website")
        {
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, SC_WEBSITE, p);
        }

        fixed (char* p = "Settings")
        {
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, SC_SETTINGS, p);
        }

        fixed (char* p = "About")
        {
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, SC_ABOUT, p);
        }
    }
}
