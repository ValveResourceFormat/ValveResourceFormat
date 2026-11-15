using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using GUI.Utils;
using Windows.Win32;
using Windows.Win32.System.LibraryLoader;

namespace GUI
{
    static class Program
    {
        public static Assembly Assembly { get; private set; } = typeof(Program).Assembly;
#nullable disable
        public static MainForm MainForm { get; private set; }
        public static string ProductVersion { get; private set; }
        public static string DisplayVersion { get; private set; }
        /// <summary>The numeric part of <see cref="ProductVersion"/>, without the git commit.</summary>
        public static Version Version { get; private set; }
#nullable enable

        /// <summary>Whether this build was produced by the CI for a tagged stable release.</summary>
        // Not const, so the compiler does not flag code as unreachable in release builds
        public static bool IsReleaseBuild =>
#if CI_RELEASE_BUILD // For CI builds, it is set in Directory.Build.props
            true;
#else
            false;
#endif

        /// <summary>The update channel that produced this build.</summary>
        public static Settings.UpdateChannel BuildChannel => IsReleaseBuild ? Settings.UpdateChannel.Stable : Settings.UpdateChannel.Dev;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        internal static void Main(string[] args)
        {
            // Stop system DLLs (e.g. dinput8.dll requested by GLFW) from being hijacked by a copy next to the exe.
            PInvoke.SetDefaultDllDirectories(LOAD_LIBRARY_FLAGS.LOAD_LIBRARY_SEARCH_SYSTEM32);

            if (args.Length == 3 && args[0] == UpdateInstaller.ElevatedInstallArgument)
            {
                Environment.ExitCode = UpdateInstaller.InstallElevated(args[1], args[2]);
                return;
            }

            LoadVersion();

            if (args.Length > 0 && args[0] == AppInstaller.InstallArgument)
            {
                try
                {
                    AppInstaller.Install();
                }
                catch (Exception e)
                {
                    Environment.ExitCode = e.HResult;
                }

                return;
            }

            if (args.Length > 0 && args[0] == AppInstaller.UninstallArgument)
            {
                Environment.ExitCode = Uninstall();
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += UnhandledException;
            Application.ThreadException += ThreadException;

#if DEBUG
            // Touching Trace.Listeners reroutes Debug.Assert through the listeners,
            // which prevents the default behavior of Environment.FailFast when no debugger is attached
#pragma warning disable RS0030 // Listeners are the only hook for assert failures
            Trace.Listeners.Clear();
            Trace.Listeners.Add(new AssertTraceListener());
#pragma warning restore RS0030
#endif

            // Set invariant culture so we have consistent localization (e.g. dots do not get encoded as commas)
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            Application.EnableVisualStyles();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            Automation.Automation.TakeSwitch(ref args);

            if (args.Length > 0 && !Automation.Automation.IsEnabled && Ipc.TryForwardToExistingInstance(args))
            {
                return;
            }

            // The explorer and tab paths need the installed games, find them while the window is being created
            _ = InstalledSteamGames.FindAsync();

            ExecutableSwap.CleanupLeftovers();
            AppInstaller.RefreshUninstallEntry();

            MainForm = new MainForm(args);

            Application.Run(MainForm);
        }

        /// <summary>
        /// Closes this instance and starts the executable.
        /// </summary>
        public static void Restart(string exePath)
        {
            // Closing the main window saves the settings, so the new instance starts from the final state
            MainForm.Close();

            if (!MainForm.IsDisposed)
            {
                return; // Closing was cancelled
            }

            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        }

        private static void LoadVersion()
        {
            Assembly = Assembly.GetExecutingAssembly();

            // from winforms dep: Application.ProductVersion
            // Custom attribute
            Assembly? entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly is not null)
            {
                var attrs = entryAssembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
                if (attrs is not null && attrs.Length > 0)
                {
                    ProductVersion = ((AssemblyInformationalVersionAttribute)attrs[0]).InformationalVersion;
                }
            }

            if (ProductVersion is null || ProductVersion.Length == 0)
            {
                throw new InvalidDataException("Failed to find version number");
            }

            var versionPlus = ProductVersion.IndexOf('+', StringComparison.Ordinal);
            var numericVersion = versionPlus < 0 ? ProductVersion : ProductVersion[..versionPlus];

            Version = new Version(numericVersion);
            DisplayVersion = FormatDisplayVersion(ProductVersion);
        }

        private static int Uninstall()
        {
            try
            {
                if (AppInstaller.Uninstall() == AppInstaller.UninstallResult.Done)
                {
                    return 0;
                }

                ShowUninstallError("Close all Source 2 Viewer windows and try again.");
            }
            catch (Exception e)
            {
                ShowUninstallError(e.Message);
            }

            return 1;
        }

        private static void ShowUninstallError(string message)
        {
            AppMessageDialogs.ShowMessageAsync(message, "Uninstall Source 2 Viewer", MessageIcon.Error).GetAwaiter().GetResult();
        }

        private static string FormatDisplayVersion(string version)
        {
            var versionPlus = version.IndexOf('+', StringComparison.Ordinal);

            if (versionPlus < 0)
            {
                return version;
            }

            var commit = version.AsSpan(versionPlus + 1);

            if (commit.Length > 9)
            {
                commit = commit[..9];
            }

            return string.Concat(version.AsSpan(0, versionPlus), " ", commit);
        }

        private static void ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ErrorReport.ShowError(e.Exception);
        }

        private static void UnhandledException(object sender, UnhandledExceptionEventArgs ex)
        {
            ErrorReport.ShowError((Exception)ex.ExceptionObject);
        }
    }
}
