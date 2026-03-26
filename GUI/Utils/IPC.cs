using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace GUI.Utils;

internal class IPC
{
    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public nint dwData;
        public int cbData;
        public nint lpData;
    }

    private const string IpcWindowTitle = "Source2Viewer_IPC";
    private static readonly nint HWND_MESSAGE = -3;

    public static unsafe bool TryForwardToExistingInstance(string[] args)
    {
        var hwnd = PInvoke.FindWindowEx(
            new HWND(HWND_MESSAGE),
            HWND.Null,
            lpszClass: null,
            IpcWindowTitle);

        if (hwnd.IsNull)
        {
            return false;
        }

        // cwd may be different in the other process than the current one, convert to canonical
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("vpk:", StringComparison.InvariantCulture))
            {
                continue;
            }

            if (File.Exists(args[i]))
            {
                args[i] = Path.GetFullPath(args[i]);
            }
        }

        // windows does not allow you to get your window in focus if you were not given permission by a foreground window
        uint processId;
        if (PInvoke.GetWindowThreadProcessId(hwnd, &processId) == 0)
        {
            return false;
        }

        PInvoke.AllowSetForegroundWindow(processId);

        var message = string.Join('\0', args);
        var messageBytes = Encoding.UTF8.GetBytes(message);

        fixed (byte* ptr = messageBytes)
        {
            var cds = new COPYDATASTRUCT
            {
                dwData = 0,
                cbData = messageBytes.Length,
                lpData = (IntPtr)ptr,
            };
            var result = PInvoke.SendMessageTimeout(
                hwnd,
                PInvoke.WM_COPYDATA,
                default,
                (LPARAM)(IntPtr)(&cds),
                SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG,
                1000,
                out _);

            if (result == 0)
            {
                return false;
            }
        }

        return true;
    }

    public sealed class IpcWindow : NativeWindow
    {
        public IpcWindow()
        {
            CreateHandle(new CreateParams
            {
                Caption = IpcWindowTitle,
                Parent = HWND_MESSAGE
            });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == PInvoke.WM_COPYDATA)
            {
                var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(m.LParam);
                var message = Marshal.PtrToStringUTF8(cds.lpData, cds.cbData);
                var args = message.Split('\0', StringSplitOptions.RemoveEmptyEntries);

                if (args.Length > 0)
                {
                    Program.MainForm.BeginInvoke(() =>
                    {
                        Program.MainForm.OpenCommandLineArgFiles(args);

                        if (Program.MainForm.WindowState == FormWindowState.Minimized)
                        {
                            Program.MainForm.WindowState = FormWindowState.Normal;
                        }

                        Program.MainForm.Activate();
                    });
                }

                m.Result = 1;
                return;
            }

            base.WndProc(ref m);
        }
    }
}
