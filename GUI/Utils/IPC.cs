using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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

    /// <summary>
    /// Hidden message-only window that receives command line arguments forwarded by
    /// <see cref="TryForwardToExistingInstance"/> from secondary processes.
    /// Raw Win32, no WinForms. Must be created on a thread that pumps messages;
    /// the callback is invoked on that thread while the sender is blocked waiting,
    /// so it should return quickly (e.g. only schedule work).
    /// </summary>
    public sealed unsafe class IpcWindow : IDisposable
    {
        private const string ClassName = "Source2Viewer_IPC_Window";

        // The window class holds a delegate to the static WndProc, and there is only
        // ever one IpcWindow per process, so the callback is stored statically.
        private static Action<string[]>? OnArgsReceived;

        // Rooted so the marshaled callback outlives the native window class registration
        private static readonly WNDPROC WndProcDelegate = WndProc;

        private readonly HINSTANCE hInstance;
        private HWND hwnd;
        private ushort classAtom;

        public IpcWindow(Action<string[]> onArgsReceived)
        {
            OnArgsReceived = onArgsReceived;

            // Marshal.GetHINSTANCE is not single file publish compatible
            using (var exeModule = PInvoke.GetModuleHandle((string?)null))
            {
                hInstance = (HINSTANCE)exeModule.DangerousGetHandle();
            }

            fixed (char* className = ClassName)
            {
                var wndClass = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = WndProcDelegate,
                    hInstance = hInstance,
                    lpszClassName = className,
                };

                classAtom = PInvoke.RegisterClassEx(in wndClass);
            }

            if (classAtom == 0)
            {
                throw new InvalidOperationException($"Failed to register IPC window class, error {Marshal.GetLastPInvokeError()}");
            }

            hwnd = PInvoke.CreateWindowEx(
                0,
                ClassName,
                IpcWindowTitle,
                0,
                0, 0, 0, 0,
                HWND.HWND_MESSAGE,
                null,
                null,
                null
            );

            if (hwnd.IsNull)
            {
                throw new InvalidOperationException($"Failed to create IPC window, error {Marshal.GetLastPInvokeError()}");
            }
        }

        private static LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
        {
            if (msg == PInvoke.WM_COPYDATA)
            {
                // An exception escaping a native callback would crash the process
                try
                {
                    var cds = *(Windows.Win32.System.DataExchange.COPYDATASTRUCT*)(nint)lParam.Value;
                    var message = Marshal.PtrToStringUTF8((IntPtr)cds.lpData, (int)cds.cbData);
                    var args = message.Split('\0', StringSplitOptions.RemoveEmptyEntries);

                    if (args.Length > 0)
                    {
                        OnArgsReceived?.Invoke(args);
                    }
                }
                catch (Exception e)
                {
                    Log.Error(nameof(IpcWindow), $"Failed to handle forwarded arguments: {e}");
                }

                return (LRESULT)1;
            }

            return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (!hwnd.IsNull)
            {
                PInvoke.DestroyWindow(hwnd);
                hwnd = HWND.Null;
            }

            if (classAtom != 0)
            {
                fixed (char* className = ClassName)
                {
                    PInvoke.UnregisterClass(className, hInstance);
                }

                classAtom = 0;
            }

            OnArgsReceived = null;
        }
    }
}
