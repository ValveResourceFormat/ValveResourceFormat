using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace GUI.Utils;

internal class Ipc
{
    private const string IpcWindowTitle = "Source2Viewer_IPC";

    public static unsafe bool TryForwardToExistingInstance(string[] args)
    {
        var hwnd = PInvoke.FindWindowEx(
            HWND.HWND_MESSAGE,
            HWND.Null,
            null,
            IpcWindowTitle
        );

        if (hwnd.IsNull)
        {
            return false;
        }

        // cwd may be different in the other process than the current one, convert to canonical
        var absoluteArgs = new string[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("vpk:", StringComparison.InvariantCulture) && File.Exists(args[i]))
            {
                absoluteArgs[i] = Path.GetFullPath(args[i]);
            }
            else
            {
                absoluteArgs[i] = args[i];
            }
        }

        // windows does not allow you to get your window in focus if you were not given permission by a foreground window
        uint processId;
        if (PInvoke.GetWindowThreadProcessId(hwnd, &processId) == 0)
        {
            return false;
        }

        PInvoke.AllowSetForegroundWindow(processId);

        var message = string.Join('\0', absoluteArgs);
        var messageBytes = Encoding.UTF8.GetBytes(message);

        fixed (byte* ptr = messageBytes)
        {
            var cds = new Windows.Win32.System.DataExchange.COPYDATASTRUCT
            {
                dwData = 0,
                cbData = (uint)messageBytes.Length,
                lpData = ptr,
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
                Parent = HWND.HWND_MESSAGE
            });
        }

        protected override unsafe void WndProc(ref Message m)
        {
            if (m.Msg == PInvoke.WM_COPYDATA)
            {
                var cds = Marshal.PtrToStructure<Windows.Win32.System.DataExchange.COPYDATASTRUCT>(m.LParam);
                var message = Marshal.PtrToStringUTF8((IntPtr)cds.lpData, (int)cds.cbData);
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
