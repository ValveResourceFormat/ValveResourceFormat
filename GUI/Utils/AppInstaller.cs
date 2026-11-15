using System.IO;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace GUI.Utils;

/// <summary>
/// Installs the viewer for the current user: copies it to <see cref="InstallPath"/>, adds a Start menu shortcut,
/// the file associations and an uninstall entry, and removes them again.
/// </summary>
static class AppInstaller
{
    /// <summary>Starts the viewer as a silent installer.</summary>
    public const string InstallArgument = "--install";

    /// <summary>Starts the viewer as an uninstaller.</summary>
    public const string UninstallArgument = "--uninstall";

    /// <summary>The result of <see cref="Uninstall"/>.</summary>
    public enum UninstallResult
    {
        /// <summary>The viewer was uninstalled.</summary>
        Done,
        /// <summary>The installed copy cannot be removed while another viewer window is open.</summary>
        OtherInstanceRunning,
    }

    private const string Extension = ".vpk";
    private const string ProgId = $"VRF.Source2Viewer{Extension}";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Source2Viewer";
    private const string Publisher = "ValveResourceFormat Contributors";

    // The install shares the folder with the settings, so uninstalling removes both
    private static string InstallFolder => Settings.SettingsFolder;

    /// <summary>The executable that an install puts in the per-user application data folder.</summary>
    public static string InstallPath { get; } = Path.Join(InstallFolder, "Source2Viewer.exe");

    /// <summary>Whether the running executable is the installed copy.</summary>
    public static bool IsInstalledCopy => string.Equals(Environment.ProcessPath, InstallPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>The version recorded by the install, or null when there is none.</summary>
    public static string? InstalledVersion
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
            return key?.GetValue("DisplayVersion") as string;
        }
    }

    private static string Version => Program.ProductVersion.Split('+')[0];

    private static string ShortcutPath => Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Source 2 Viewer.lnk");

    /// <summary>
    /// Registers the viewer, and installs it unless it is the installed copy or a build that cannot be copied.
    /// </summary>
    /// <returns>Whether the running executable was copied to <see cref="InstallPath"/>.</returns>
    public static bool Install()
    {
        var exePath = Environment.ProcessPath!;

        if (IsInstalledCopy)
        {
            Register(exePath);
            WriteUninstallEntry();
            return false;
        }

        if (!ExecutableSwap.IsSingleFileBundle(exePath))
        {
            Register(exePath);
            return false;
        }

        Directory.CreateDirectory(InstallFolder);
        ExecutableSwap.Swap(InstallPath, exePath, null);

        CreateShortcut();
        Register(InstallPath);
        WriteUninstallEntry();

        return true;
    }

    /// <summary>
    /// Removes what <see cref="Install"/> added. The installed copy also deletes its folder, settings included.
    /// </summary>
    public static UninstallResult Uninstall()
    {
        if (!IsInstalledCopy)
        {
            Unregister();
            return UninstallResult.Done;
        }

        if (Ipc.IsInstanceRunning())
        {
            return UninstallResult.OtherInstanceRunning;
        }

        if (!Path.IsPathRooted(InstallFolder))
        {
            throw new InvalidOperationException($"Refusing to delete {InstallFolder}.");
        }

        File.Delete(ShortcutPath);
        Unregister();
        Registry.CurrentUser.DeleteSubKey(UninstallKeyPath, throwOnMissingSubKey: false);

        // The running executable cannot be deleted but can be renamed out of the folder.
        // Only System.IO runs after the rename. A single file bundle may load assemblies from its old path.
        var tempPath = Path.GetTempPath();
        Directory.SetCurrentDirectory(tempPath);
        File.Move(InstallPath, Path.Join(tempPath, Path.GetRandomFileName()));
        Directory.Delete(InstallFolder, recursive: true);

        return UninstallResult.Done;
    }

    /// <summary>
    /// Rewrites the uninstall entry from the installed copy, so it reports the version after an update.
    /// </summary>
    public static void RefreshUninstallEntry()
    {
        if (IsInstalledCopy)
        {
            WriteUninstallEntry();
        }
    }

    private static unsafe void Register(string exePath)
    {
        // The icon file stays next to the installed executable
        var vpkIconPath = Path.Join(InstallFolder, "vpk.ico");

        if (!File.Exists(vpkIconPath))
        {
            Directory.CreateDirectory(InstallFolder);

            using var iconStream = Program.Assembly.GetManifestResourceStream("GUI.Utils.vpk.ico")!;
            using var iconDiskStream = File.Create(vpkIconPath);
            iconStream.CopyTo(iconDiskStream);
        }

        // .vpk file extension
        using var reg = Registry.CurrentUser.CreateSubKey(@$"Software\Classes\{Extension}\OpenWithProgids");
        reg.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);

        using var reg2 = Registry.CurrentUser.CreateSubKey(@$"Software\Classes\{ProgId}");
        reg2.SetValue(null, "Valve Pak File");

        using var reg3 = reg2.CreateSubKey(@"shell\open\command");
        reg3.SetValue(null, $"\"{exePath}\" \"%1\"");

        using var regIco = reg2.CreateSubKey("DefaultIcon");
        regIco.SetValue(null, vpkIconPath);

        // Protocol
        using var regProtocol = Registry.CurrentUser.CreateSubKey(@"Software\Classes\vpk");
        regProtocol.SetValue(string.Empty, "URL:Valve Pak protocol");
        regProtocol.SetValue("URL Protocol", string.Empty);

        using var regProtocolOpen = regProtocol.CreateSubKey(@"shell\open\command");
        regProtocolOpen.SetValue(null, $"\"{exePath}\" \"%1\"");

        PInvoke.SHChangeNotify(SHCNE_ID.SHCNE_ASSOCCHANGED, SHCNF_FLAGS.SHCNF_FLUSH, null, null);
    }

    private static unsafe void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@$"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\vpk", throwOnMissingSubKey: false);

        using var openWithProgids = Registry.CurrentUser.OpenSubKey(@$"Software\Classes\{Extension}\OpenWithProgids", writable: true);
        openWithProgids?.DeleteValue(ProgId, throwOnMissingValue: false);

        PInvoke.SHChangeNotify(SHCNE_ID.SHCNE_ASSOCCHANGED, SHCNF_FLAGS.SHCNF_FLUSH, null, null);
    }

    private static void CreateShortcut()
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(InstallPath);
        link.SetDescription("Source 2 Viewer");

        ((IPersistFile)link).Save(ShortcutPath, true);
    }

    private static void WriteUninstallEntry()
    {
        var uninstallCommand = $"\"{InstallPath}\" {UninstallArgument}";
        var sizeInKilobytes = (int)(new FileInfo(InstallPath).Length / 1024);

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", "Source 2 Viewer");
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", Publisher);
        key.SetValue("DisplayIcon", InstallPath);
        key.SetValue("InstallLocation", InstallFolder);
        key.SetValue("UninstallString", uninstallCommand);
        key.SetValue("QuietUninstallString", uninstallCommand);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", sizeInKilobytes, RegistryValueKind.DWord);
    }
}
