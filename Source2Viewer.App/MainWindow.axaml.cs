using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Materials;

namespace Source2Viewer.App;

#pragma warning disable CA2007 // Avalonia UI event handlers intentionally resume on the UI thread.

public partial class MainWindow : Window
{
    private readonly List<PackageEntryItem> allEntries = [];
    private static readonly string LastPathFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Source2Viewer",
        "last-path.txt");

    private readonly ObservableCollection<PackageEntryItem> visibleEntries = [];
    private Package? currentPackage;
    private string? currentPath;
    private string currentFolderPrefix = string.Empty;
    private bool ignoreMapCameraChange;
    private bool ignoreAnimationChange;
    private bool ignoreAnimationFrameChange;
    private bool ignoreMeshGroupChange;
    private bool ignoreMaterialGroupChange;
    private bool ignoreRenderModeChange;
    private bool ignoreLodChange;
    private bool ignoreHitboxSetChange;
    private bool ignoreLayerChange;
    private bool ignorePhysicsGroupChange;
    private bool ignoreEntrySelectionChange;
    private bool ignoreFolderSelection;
    private readonly List<string> navigationHistory = [];
    private int navigationHistoryIndex = -1;
    private int selectionVersion;
    private string? pendingStartupEntryPath;
    private string viewportStatus = "Native OpenGL renderer viewport";
    private WindowState previousWindowState = WindowState.Normal;
    private Process? soundProcess;
    private string? soundTempPath;
    private string? clipboardScreenshotPath;

    public MainWindow() : this(Program.StartupArgs)
    {
    }

    public MainWindow(string[] args)
    {
        InitializeComponent();

        EntryList.ItemsSource = visibleEntries;
        SearchModeComboBox.ItemsSource = new[] { "File contains", "Full path", "Exact file", "Regex" };
        SearchModeComboBox.SelectedIndex = 0;
        EntryFilterComboBox.ItemsSource = new[] { "All", "Compiled", "Renderable", "Images", "Audio" };
        EntryFilterComboBox.SelectedIndex = 0;
        SortModeComboBox.ItemsSource = new[] { "Path", "Name", "Type", "Size" };
        SortModeComboBox.SelectedIndex = 0;
        RenderModeComboBox.ItemsSource = GetRenderModeNames(RenderModes.Items.Select(static mode => mode.Name));
        RenderModeComboBox.SelectedIndex = 0;

        OpenPackageButton.Click += OnOpenPackage;
        ReopenLastButton.IsEnabled = GetLastPath() != null;
        ReopenLastButton.Click += async (_, _) => await ReopenLastPathAsync();
        BackButton.Click += (_, _) => NavigateHistory(-1);
        ForwardButton.Click += (_, _) => NavigateHistory(1);
        ClearButton.Click += (_, _) => ClearPackage();
        ExportSelectedButton.Click += OnExportSelected;
        ExportVisibleButton.Click += OnExportVisible;
        ExportVisibleDecompiledButton.Click += OnExportVisibleDecompiled;
        PreviewDecompiledButton.Click += OnPreviewDecompiled;
        ExportDecompiledButton.Click += OnExportDecompiled;
        PlaySoundButton.Click += OnPlaySound;
        StopSoundButton.Click += (_, _) => StopSound();
        SaveScreenshotButton.Click += OnSaveScreenshot;
        FindMenuItem.Click += (_, _) => FocusSearch();
        AboutMenuItem.Click += (_, _) => ShowAbout();
        CopyEntryPathMenuItem.Click += OnCopyEntryPath;
        ExportSelectedMenuItem.Click += OnExportSelected;
        PreviewDecompiledMenuItem.Click += OnPreviewDecompiled;
        ExportDecompiledMenuItem.Click += OnExportDecompiled;
        ResetCameraButton.Click += (_, _) => RenderControl.ResetCamera();
        WireframeCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetWireframe(WireframeCheckBox.IsChecked == true);
        GridCheckBox.IsChecked = true;
        GridCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetGridVisible(GridCheckBox.IsChecked == true);
        SkyboxCheckBox.IsChecked = true;
        SkyboxCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetSkyboxVisible(SkyboxCheckBox.IsChecked == true);
        FogCheckBox.IsChecked = true;
        FogCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetFogVisible(FogCheckBox.IsChecked == true);
        ColorCorrectionCheckBox.IsChecked = true;
        ColorCorrectionCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetColorCorrectionEnabled(ColorCorrectionCheckBox.IsChecked == true);
        OcclusionCullingCheckBox.IsChecked = true;
        OcclusionCullingCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetOcclusionCullingEnabled(OcclusionCullingCheckBox.IsChecked == true);
        GpuCullingCheckBox.IsChecked = true;
        GpuCullingCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetGpuCullingEnabled(GpuCullingCheckBox.IsChecked == true);
        DepthPrepassCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetDepthPrepassEnabled(DepthPrepassCheckBox.IsChecked == true);
        ExperimentalLightsCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetExperimentalLightsEnabled(ExperimentalLightsCheckBox.IsChecked == true);
        ToolMaterialsCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetToolMaterialsVisible(ToolMaterialsCheckBox.IsChecked == true);
        OccludedBoundsCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetOccludedBoundsVisible(OccludedBoundsCheckBox.IsChecked == true);
        StaticOctreeCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetStaticOctreeVisible(StaticOctreeCheckBox.IsChecked == true);
        DynamicOctreeCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetDynamicOctreeVisible(DynamicOctreeCheckBox.IsChecked == true);
        RenderControl.MapCamerasChanged += OnMapCamerasChanged;
        RenderControl.AnimationsChanged += OnAnimationsChanged;
        RenderControl.MeshGroupsChanged += OnMeshGroupsChanged;
        RenderControl.MaterialGroupsChanged += OnMaterialGroupsChanged;
        RenderControl.LodsChanged += OnLodsChanged;
        RenderControl.HitboxSetsChanged += OnHitboxSetsChanged;
        RenderControl.SkeletonChanged += OnSkeletonChanged;
        RenderControl.WorldLayersChanged += OnWorldLayersChanged;
        RenderControl.PhysicsGroupsChanged += OnPhysicsGroupsChanged;
        RenderControl.EntitiesChanged += OnEntitiesChanged;
        RenderControl.RenderModesChanged += OnRenderModesChanged;
        RenderControl.ModelStatsChanged += OnModelStatsChanged;
        RenderControl.ViewportErrorChanged += OnViewportErrorChanged;
        RenderControl.ViewportStatusChanged += OnViewportStatusChanged;
        RenderControl.ScreenshotSaved += OnScreenshotSaved;
        RenderModeComboBox.SelectionChanged += (_, _) =>
        {
            if (!ignoreRenderModeChange && RenderModeComboBox.SelectedItem is string mode)
            {
                RenderControl.SetRenderMode(mode);
            }
        };
        MapCameraComboBox.SelectionChanged += (_, _) =>
        {
            if (!ignoreMapCameraChange && MapCameraComboBox.SelectedIndex >= 0)
            {
                RenderControl.SetMapCamera(MapCameraComboBox.SelectedIndex);
            }
        };
        AnimationComboBox.SelectionChanged += (_, _) =>
        {
            if (!ignoreAnimationChange)
            {
                RenderControl.SetAnimation(AnimationComboBox.SelectedIndex <= 0 ? null : AnimationComboBox.SelectedItem as string);
            }
        };
        AnimationAutoplayCheckBox.IsChecked = true;
        AnimationAutoplayCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetAnimationPaused(AnimationAutoplayCheckBox.IsChecked != true);
        RootMotionCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetRootMotionEnabled(RootMotionCheckBox.IsChecked == true);
        AnimationFrameSlider.PropertyChanged += (_, e) =>
        {
            if (!ignoreAnimationFrameChange && e.Property == Slider.ValueProperty)
            {
                RenderControl.SetAnimationFrame((float)AnimationFrameSlider.Value);
            }
        };
        AnimationSpeedSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                RenderControl.SetAnimationSpeed((float)AnimationSpeedSlider.Value);
            }
        };
        MeshGroupListBox.SelectionChanged += (_, _) =>
        {
            if (!ignoreMeshGroupChange)
            {
                RenderControl.SetMeshGroups(MeshGroupListBox.SelectedItems?.Cast<string>().ToArray() ?? []);
            }
        };
        MaterialGroupComboBox.SelectionChanged += (_, _) =>
        {
            if (!ignoreMaterialGroupChange && MaterialGroupComboBox.SelectedItem is string group)
            {
                RenderControl.SetMaterialGroup(group);
            }
        };
        LodComboBox.SelectionChanged += (_, _) =>
        {
            if (!ignoreLodChange)
            {
                RenderControl.SetLod(LodComboBox.SelectedIndex - 1);
            }
        };
        HitboxSetComboBox.SelectionChanged += (_, _) =>
        {
            if (!ignoreHitboxSetChange)
            {
                RenderControl.SetHitboxSet(HitboxSetComboBox.SelectedIndex <= 0 ? null : HitboxSetComboBox.SelectedItem as string);
            }
        };
        SkeletonCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetSkeletonVisible(SkeletonCheckBox.IsChecked == true);
        ModelStatsCheckBox.IsCheckedChanged += (_, _) => RenderControl.SetModelStatsVisible(ModelStatsCheckBox.IsChecked == true);
        WorldLayerListBox.SelectionChanged += (_, _) =>
        {
            if (!ignoreLayerChange)
            {
                RenderControl.SetWorldLayers(WorldLayerListBox.SelectedItems?.Cast<string>().ToArray() ?? []);
            }
        };
        PhysicsGroupListBox.SelectionChanged += (_, _) =>
        {
            if (!ignorePhysicsGroupChange)
            {
                RenderControl.SetPhysicsGroups(PhysicsGroupListBox.SelectedItems?.Cast<string>().ToArray() ?? []);
            }
        };
        SearchModeComboBox.SelectionChanged += (_, _) => ApplyFilter();
        EntryFilterComboBox.SelectionChanged += (_, _) => ApplyFilter();
        SortModeComboBox.SelectionChanged += (_, _) => ApplyFilter();
        SearchTextBox.TextChanged += (_, _) => ApplyFilter();
        SearchTextBox.KeyDown += OnSearchKeyDown;
        FolderTree.SelectionChanged += OnFolderSelected;
        EntryList.SelectionChanged += OnEntrySelected;
        EntryList.DoubleTapped += (_, _) => _ = LoadSelectedEntryAsync();
        EntityListBox.SelectionChanged += (_, _) => ShowSelectedEntityDetails();
        EntityListBox.DoubleTapped += (_, _) => RenderControl.FocusEntity(EntityListBox.SelectedIndex);
        EntityListBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                RenderControl.FocusEntity(EntityListBox.SelectedIndex);
                e.Handled = true;
            }
        };
        ViewportInputLayer.PointerPressed += OnViewportPointerPressed;
        ViewportInputLayer.PointerReleased += OnViewportPointerReleased;
        ViewportInputLayer.PointerMoved += OnViewportPointerMoved;
        ViewportInputLayer.PointerWheelChanged += OnViewportPointerWheelChanged;
        ViewportInputLayer.KeyDown += OnViewportKeyDown;
        ViewportInputLayer.KeyUp += OnViewportKeyUp;
        KeyDown += OnWindowKeyDown;
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);

        if (args.Length > 0)
        {
            pendingStartupEntryPath = args.Length > 1 ? args[1] : null;
            _ = LoadPathAsync(args[0]);
        }
        else
        {
            PackagePathText.Text = "No package loaded.";
            DetailsText.Text = "Open a VPK to browse entries, preview, export, or render Source 2 resources.";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        StopSound();
        RenderControl.ClearResource();
        SetPreviewImage(null);
        currentPackage?.Dispose();
        base.OnClosed(e);
    }

    private async void OnOpenPackage(object? sender, RoutedEventArgs e)
        => await OpenPackageAsync();

    private void FocusSearch()
    {
        MainTabs.SelectedItem = ExplorerTab;
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void ShowAbout()
    {
        MainTabs.SelectedItem = ExplorerTab;
        DetailsText.Text = "Source 2 Viewer - Linux Native" + Environment.NewLine
            + "Native Avalonia/OpenGL port in progress." + Environment.NewLine
            + "Shortcuts: Ctrl+O open, Ctrl+F find, F focus viewport, F11 fullscreen.";
    }

    private async Task OpenPackageAsync()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open VPK or Source 2 resource",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Valve packages and resources")
                {
                    Patterns = ["*.vpk", "*_c"],
                },
                FilePickerFileTypes.All,
            ],
        });

        var path = files.Count == 0 ? null : files[0].TryGetLocalPath();
        if (path != null)
        {
            await LoadPathAsync(path);
        }
    }

    private async Task LoadPathAsync(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".vpk", StringComparison.OrdinalIgnoreCase))
        {
            await LoadPackageAsync(path);
            SaveLastPath(path);
            return;
        }

        ClearPackage();
        currentPath = path;
        SaveLastPath(path);
        PackagePathText.Text = path;
        StatusText.Text = "Opened loose file.";
        var (details, previewImage) = await Task.Run(() => (DescribeLooseFile(path), TryExtractLooseFilePreviewImageBytes(path)));
        DetailsText.Text = details;
        SetPreviewImage(CreatePreviewBitmap(previewImage));

        if (path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase)
            && IsRenderableCompiledType(Path.GetExtension(path).TrimStart('.')))
        {
            RenderControl.LoadLooseFile(path);
            SetViewportTabHeader(path);
            MainTabs.SelectedItem = ViewportTab;
        }
        else
        {
            RenderControl.ClearResource();
        }
    }

    private async Task ReopenLastPathAsync()
    {
        var path = GetLastPath();
        if (path == null)
        {
            ReopenLastButton.IsEnabled = false;
            StatusText.Text = "No last path saved.";
            return;
        }

        await LoadPathAsync(path);
    }

    private static string? GetLastPath()
    {
        if (!File.Exists(LastPathFile))
        {
            return null;
        }

        var path = File.ReadAllText(LastPathFile).Trim();
        return File.Exists(path) ? path : null;
    }

    private void SaveLastPath(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LastPathFile)!);
        File.WriteAllText(LastPathFile, path);
        ReopenLastButton.IsEnabled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = GetDroppedPath(e) == null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = GetDroppedPath(e);
        if (path != null)
        {
            await LoadPathAsync(path);
        }

        e.Handled = true;
    }

    private static string? GetDroppedPath(DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
        return file?.TryGetLocalPath();
    }

    private async Task LoadPackageAsync(string path)
    {
        StatusText.Text = "Loading package...";
        DetailsText.Text = string.Empty;
        selectionVersion++;
        EntryList.SelectedItem = null;

        var package = await Task.Run(() =>
        {
            var loadedPackage = new Package();
            loadedPackage.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
            loadedPackage.Read(path);
            return loadedPackage;
        });

        currentPackage?.Dispose();
        currentPackage = package;
        currentPath = path;
        currentFolderPrefix = string.Empty;
        navigationHistory.Clear();
        navigationHistoryIndex = -1;

        allEntries.Clear();

        if (package.Entries != null)
        {
            allEntries.AddRange(package.Entries.Values
                .SelectMany(static entries => entries)
                .Select(static entry => new PackageEntryItem(entry))
                .OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase));
        }

        PackagePathText.Text = path;
        BuildFolderTree();
        SearchTextBox.Text = string.Empty;
        SearchModeComboBox.SelectedIndex = 0;
        EntryFilterComboBox.SelectedIndex = 0;
        SortModeComboBox.SelectedIndex = 0;
        ApplyFilter();
        StatusText.Text = $"Loaded {allEntries.Count:N0} package entries.";
        DetailsText.Text = "Select an entry to inspect it.";
        SetPreviewImage(null);

        if (!string.IsNullOrWhiteSpace(pendingStartupEntryPath))
        {
            var selectedEntryPath = pendingStartupEntryPath;
            pendingStartupEntryPath = null;

            ignoreEntrySelectionChange = true;
            if (SelectPackageEntry(selectedEntryPath))
            {
                ignoreEntrySelectionChange = false;
                await LoadSelectedEntryAsync();
                StatusText.Text = $"Loaded entry {selectedEntryPath}.";
                return;
            }
            ignoreEntrySelectionChange = false;

            StatusText.Text = $"Loaded {allEntries.Count:N0} package entries. Entry not found: {selectedEntryPath}";
        }

        SelectDefaultMapEntry();
    }

    private void ClearPackage()
    {
        selectionVersion++;
        StopSound();
        RenderControl.ClearResource();
        SetPreviewImage(null);
        currentPackage?.Dispose();
        currentPackage = null;
        currentPath = null;
        currentFolderPrefix = string.Empty;
        navigationHistory.Clear();
        navigationHistoryIndex = -1;
        UpdateNavigationButtons();
        allEntries.Clear();
        visibleEntries.Clear();
        FolderTree.Items.Clear();
        EntryList.SelectedItem = null;
        PackagePathText.Text = "No package loaded.";
        StatusText.Text = "Open a .vpk to browse package entries.";
        DetailsText.Text = string.Empty;
        SetViewportTabHeader(null);
        SetSoundButtonsVisible(false);
    }

    private void SetViewportTabHeader(string? path)
        => ViewportTab.Header = string.IsNullOrWhiteSpace(path)
            ? "Viewport"
            : Path.GetFileName(path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));

    private void ApplyFilter()
    {
        var filter = SearchTextBox.Text;
        visibleEntries.Clear();

        var entries = allEntries.AsEnumerable();

        if (!string.IsNullOrEmpty(currentFolderPrefix))
        {
            var folderPrefix = currentFolderPrefix + SteamDatabase.ValvePak.Package.DirectorySeparatorChar;
            entries = entries.Where(entry => entry.Path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase));
        }

        entries = (EntryFilterComboBox.SelectedItem as string) switch
        {
            "Compiled" => entries.Where(static entry => entry.Entry.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase)),
            "Renderable" => entries.Where(static entry => IsRenderableCompiledType(entry.Entry.TypeName)),
            "Images" => entries.Where(static entry => IsSupportedImageFileName(entry.Path)),
            "Audio" => entries.Where(static entry => IsAudioFileName(entry.Path) || entry.Entry.TypeName.Equals("vsnd_c", StringComparison.OrdinalIgnoreCase)),
            _ => entries,
        };

        if (!string.IsNullOrWhiteSpace(filter))
        {
            entries = ApplySearchMode(entries, filter);
        }

        entries = ApplySortMode(entries);

        foreach (var entry in entries)
        {
            visibleEntries.Add(entry);
        }

        if (currentPackage != null)
        {
            StatusText.Text = $"Showing {visibleEntries.Count:N0} of {allEntries.Count:N0} package entries.";
        }
    }

    private IEnumerable<PackageEntryItem> ApplySortMode(IEnumerable<PackageEntryItem> entries)
        => (SortModeComboBox.SelectedItem as string) switch
        {
            "Name" => entries.OrderBy(static entry => entry.Entry.GetFileName(), StringComparer.OrdinalIgnoreCase),
            "Type" => entries.OrderBy(static entry => entry.Entry.TypeName, StringComparer.OrdinalIgnoreCase).ThenBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase),
            "Size" => entries.OrderByDescending(static entry => entry.Entry.TotalLength).ThenBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase),
            _ => entries.OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase),
        };

    private IEnumerable<PackageEntryItem> ApplySearchMode(IEnumerable<PackageEntryItem> entries, string filter)
    {
        var normalized = filter.Replace('\\', SteamDatabase.ValvePak.Package.DirectorySeparatorChar);
        return (SearchModeComboBox.SelectedItem as string) switch
        {
            "Full path" => entries.Where(entry => entry.Path.Contains(normalized, StringComparison.OrdinalIgnoreCase)),
            "Exact file" => entries.Where(entry => entry.Entry.GetFileName().Equals(filter, StringComparison.OrdinalIgnoreCase)),
            "Regex" => ApplyRegexSearch(entries, filter),
            _ when normalized.Contains(SteamDatabase.ValvePak.Package.DirectorySeparatorChar, StringComparison.Ordinal)
                => entries.Where(entry => entry.Path.Contains(normalized, StringComparison.OrdinalIgnoreCase)),
            _ => entries.Where(entry => entry.Entry.GetFileName().Contains(filter, StringComparison.OrdinalIgnoreCase)),
        };
    }

    private static IEnumerable<PackageEntryItem> ApplyRegexSearch(IEnumerable<PackageEntryItem> entries, string filter)
    {
        Regex regex;
        try
        {
            regex = new Regex(filter, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            return [];
        }

        return entries.Where(entry => regex.IsMatch(entry.Entry.GetFileName()));
    }

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || visibleEntries.Count == 0)
        {
            return;
        }

        var filter = SearchTextBox.Text?.Trim();
        var entry = string.IsNullOrEmpty(filter)
            ? visibleEntries[0]
            : visibleEntries.FirstOrDefault(entry => entry.Path.Equals(filter.Replace('\\', SteamDatabase.ValvePak.Package.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                ?? visibleEntries[0];

        EntryList.SelectedItem = entry;
        EntryList.ScrollIntoView(entry);
        await LoadSelectedEntryAsync();
        e.Handled = true;
    }

    private void SelectDefaultMapEntry()
    {
        var entry = allEntries.FirstOrDefault(static entry => entry.Path.EndsWith(".vmap_c", StringComparison.OrdinalIgnoreCase))
            ?? allEntries.FirstOrDefault(static entry => entry.Path.EndsWith("/world.vwrld_c", StringComparison.OrdinalIgnoreCase))
            ?? allEntries.FirstOrDefault(static entry => entry.Path.EndsWith("\\world.vwrld_c", StringComparison.OrdinalIgnoreCase));

        if (entry == null)
        {
            return;
        }

        EntryList.SelectedItem = entry;
        StatusText.Text = $"Loaded map entry {entry.Path}.";
    }

    private bool SelectPackageEntry(string path)
    {
        var normalizedPath = path
            .Trim()
            .TrimStart('/', '\\')
            .Replace('\\', SteamDatabase.ValvePak.Package.DirectorySeparatorChar);

        var entry = allEntries.FirstOrDefault(entry => entry.Path.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
        entry ??= currentPackage?.FindEntry(normalizedPath) is { } packageEntry
            ? new PackageEntryItem(packageEntry)
            : null;

        if (entry == null)
        {
            return false;
        }

        EntryList.SelectedItem = entry;
        EntryList.ScrollIntoView(entry);
        return true;
    }

    private static bool IsRenderableCompiledType(string typeName)
        => typeName.Equals("vmap_c", StringComparison.OrdinalIgnoreCase)
        || typeName.Equals("vwrld_c", StringComparison.OrdinalIgnoreCase)
        || typeName.Equals("vwnod_c", StringComparison.OrdinalIgnoreCase)
        || typeName.Equals("vmdl_c", StringComparison.OrdinalIgnoreCase)
        || typeName.Equals("vmesh_c", StringComparison.OrdinalIgnoreCase)
        || typeName.Equals("vmat_c", StringComparison.OrdinalIgnoreCase)
        || typeName.Equals("vphys_c", StringComparison.OrdinalIgnoreCase)
        || typeName.Equals("vpcf_c", StringComparison.OrdinalIgnoreCase)
        || typeName.Equals("vsmart_c", StringComparison.OrdinalIgnoreCase)
        || typeName.Equals("vnmclip_c", StringComparison.OrdinalIgnoreCase)
        || typeName.Equals("vnmskel_c", StringComparison.OrdinalIgnoreCase)
        || typeName.Equals("vvis_c", StringComparison.OrdinalIgnoreCase);

    private void BuildFolderTree()
    {
        FolderTree.Items.Clear();

        var root = new TreeViewItem
        {
            Header = $"root ({allEntries.Count:N0})",
            Tag = string.Empty,
            IsExpanded = true,
        };

        FolderTree.Items.Add(root);

        var folders = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase)
        {
            [""] = root,
        };

        foreach (var entry in allEntries)
        {
            var parts = entry.Path.Split(SteamDatabase.ValvePak.Package.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var prefix = string.Empty;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                var parentPrefix = prefix;
                prefix = prefix.Length == 0
                    ? parts[i]
                    : string.Concat(prefix, SteamDatabase.ValvePak.Package.DirectorySeparatorChar, parts[i]);

                if (folders.ContainsKey(prefix))
                {
                    continue;
                }

                var item = new TreeViewItem
                {
                    Header = parts[i],
                    Tag = prefix,
                };

                folders[parentPrefix].Items.Add(item);
                folders[prefix] = item;
            }
        }

        SetCurrentFolder(string.Empty, record: true, updateSelection: true);
    }

    private void OnFolderSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (ignoreFolderSelection || FolderTree.SelectedItem is not TreeViewItem { Tag: string prefix })
        {
            return;
        }

        SetCurrentFolder(prefix, record: true, updateSelection: false);
    }

    private void SetCurrentFolder(string prefix, bool record, bool updateSelection)
    {
        currentFolderPrefix = prefix;

        if (updateSelection && FindFolderItem(prefix) is { } item)
        {
            ignoreFolderSelection = true;
            item.IsSelected = true;
            item.BringIntoView();
            ignoreFolderSelection = false;
        }

        if (record)
        {
            RecordNavigation(prefix);
        }

        ApplyFilter();
        UpdateNavigationButtons();
    }

    private void RecordNavigation(string prefix)
    {
        if (navigationHistoryIndex >= 0 && navigationHistory[navigationHistoryIndex] == prefix)
        {
            return;
        }

        if (navigationHistoryIndex < navigationHistory.Count - 1)
        {
            navigationHistory.RemoveRange(navigationHistoryIndex + 1, navigationHistory.Count - navigationHistoryIndex - 1);
        }

        navigationHistory.Add(prefix);
        navigationHistoryIndex = navigationHistory.Count - 1;
    }

    private void NavigateHistory(int delta)
    {
        var index = navigationHistoryIndex + delta;
        if (index < 0 || index >= navigationHistory.Count)
        {
            return;
        }

        navigationHistoryIndex = index;
        SetCurrentFolder(navigationHistory[index], record: false, updateSelection: true);
    }

    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = navigationHistoryIndex > 0;
        ForwardButton.IsEnabled = navigationHistoryIndex >= 0 && navigationHistoryIndex < navigationHistory.Count - 1;
    }

    private TreeViewItem? FindFolderItem(string prefix)
    {
        foreach (var item in FolderTree.Items.OfType<TreeViewItem>())
        {
            if (FindFolderItem(item, prefix) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static TreeViewItem? FindFolderItem(TreeViewItem item, string prefix)
    {
        if (item.Tag is string itemPrefix && itemPrefix == prefix)
        {
            return item;
        }

        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            if (FindFolderItem(child, prefix) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private void OnMapCamerasChanged(IReadOnlyList<string> cameras)
    {
        ignoreMapCameraChange = true;
        MapCameraComboBox.ItemsSource = cameras;
        MapCameraComboBox.SelectedIndex = -1;
        MapCameraComboBox.IsVisible = cameras.Count > 0;
        ignoreMapCameraChange = false;
    }

    private void OnAnimationsChanged(IReadOnlyList<string> animations)
    {
        ignoreAnimationChange = true;
        ignoreAnimationFrameChange = true;
        AnimationComboBox.ItemsSource = animations.Count == 0 ? [] : animations.Prepend("None").ToArray();
        AnimationComboBox.SelectedIndex = animations.Count > 0 ? 0 : -1;
        AnimationFrameSlider.Value = 0;
        RootMotionCheckBox.IsChecked = false;
        AnimationLabel.IsVisible = animations.Count > 0;
        AnimationComboBox.IsVisible = animations.Count > 0;
        AnimationAutoplayCheckBox.IsVisible = animations.Count > 0;
        RootMotionCheckBox.IsVisible = animations.Count > 0;
        AnimationFrameLabel.IsVisible = animations.Count > 0;
        AnimationFrameSlider.IsVisible = animations.Count > 0;
        AnimationSpeedLabel.IsVisible = animations.Count > 0;
        AnimationSpeedSlider.IsVisible = animations.Count > 0;
        AnimationSection.IsVisible = animations.Count > 0;
        ignoreAnimationFrameChange = false;
        ignoreAnimationChange = false;
    }

    private void OnMeshGroupsChanged(IReadOnlyList<string> groups, IReadOnlySet<string> activeGroups)
    {
        ignoreMeshGroupChange = true;
        MeshGroupListBox.ItemsSource = groups;
        MeshGroupListBox.SelectedItems?.Clear();

        foreach (var group in groups.Where(activeGroups.Contains))
        {
            MeshGroupListBox.SelectedItems?.Add(group);
        }

        MeshGroupLabel.IsVisible = groups.Count > 1;
        MeshGroupListBox.IsVisible = groups.Count > 1;
        ModelStatsCheckBox.IsVisible = groups.Count > 0;
        UpdateModelSectionVisibility();
        ignoreMeshGroupChange = false;
    }

    private void OnMaterialGroupsChanged(IReadOnlyList<string> groups, string? activeGroup)
    {
        ignoreMaterialGroupChange = true;
        MaterialGroupComboBox.ItemsSource = groups;
        MaterialGroupComboBox.SelectedItem = activeGroup;
        MaterialGroupLabel.IsVisible = groups.Count > 1;
        MaterialGroupComboBox.IsVisible = groups.Count > 1;
        UpdateModelSectionVisibility();
        ignoreMaterialGroupChange = false;
    }

    private void OnLodsChanged(IReadOnlyList<string> lods)
    {
        ignoreLodChange = true;
        LodComboBox.ItemsSource = lods;
        LodComboBox.SelectedIndex = lods.Count > 0 ? 0 : -1;
        LodLabel.IsVisible = lods.Count > 0;
        LodComboBox.IsVisible = lods.Count > 0;
        UpdateModelSectionVisibility();
        ignoreLodChange = false;
    }

    private void OnHitboxSetsChanged(IReadOnlyList<string> hitboxSets)
    {
        ignoreHitboxSetChange = true;
        HitboxSetComboBox.ItemsSource = hitboxSets.Count == 0 ? [] : hitboxSets.Prepend("None").ToArray();
        HitboxSetComboBox.SelectedIndex = hitboxSets.Count == 0 ? -1 : 0;
        HitboxSetLabel.IsVisible = hitboxSets.Count > 0;
        HitboxSetComboBox.IsVisible = hitboxSets.Count > 0;
        UpdateModelSectionVisibility();
        ignoreHitboxSetChange = false;
    }

    private void OnSkeletonChanged(bool hasSkeleton)
    {
        SkeletonCheckBox.IsChecked = false;
        SkeletonCheckBox.IsVisible = hasSkeleton;
        UpdateModelSectionVisibility();
    }

    private void UpdateModelSectionVisibility()
        => ModelSection.IsVisible = MeshGroupListBox.IsVisible
            || MaterialGroupComboBox.IsVisible
            || LodComboBox.IsVisible
            || HitboxSetComboBox.IsVisible
            || SkeletonCheckBox.IsVisible
            || ModelStatsCheckBox.IsVisible;

    private void UpdateWorldSectionVisibility()
        => WorldSection.IsVisible = WorldLayerListBox.IsVisible
            || PhysicsGroupListBox.IsVisible
            || EntityListBox.IsVisible;

    private void OnModelStatsChanged(string? stats)
    {
        ModelStatsText.Text = stats ?? string.Empty;
        ModelStatsText.IsVisible = !string.IsNullOrEmpty(stats);
    }

    private void OnWorldLayersChanged(IReadOnlyList<string> layers, IReadOnlySet<string> enabledLayers)
    {
        ignoreLayerChange = true;
        WorldLayerListBox.ItemsSource = layers;
        WorldLayerListBox.SelectedItems?.Clear();

        foreach (var layer in layers.Where(enabledLayers.Contains))
        {
            WorldLayerListBox.SelectedItems?.Add(layer);
        }

        WorldLayerLabel.IsVisible = layers.Count > 0;
        WorldLayerListBox.IsVisible = layers.Count > 0;
        UpdateWorldSectionVisibility();
        ignoreLayerChange = false;
    }

    private void OnPhysicsGroupsChanged(IReadOnlyList<string> groups, IReadOnlySet<string> enabledGroups)
    {
        ignorePhysicsGroupChange = true;
        PhysicsGroupListBox.ItemsSource = groups;
        PhysicsGroupListBox.SelectedItems?.Clear();

        foreach (var group in groups.Where(enabledGroups.Contains))
        {
            PhysicsGroupListBox.SelectedItems?.Add(group);
        }

        PhysicsGroupLabel.IsVisible = groups.Count > 0;
        PhysicsGroupListBox.IsVisible = groups.Count > 0;
        UpdateWorldSectionVisibility();
        ignorePhysicsGroupChange = false;
    }

    private void OnEntitiesChanged(IReadOnlyList<string> entities)
    {
        EntityListBox.ItemsSource = entities;
        EntityLabel.IsVisible = entities.Count > 0;
        EntityListBox.IsVisible = entities.Count > 0;
        UpdateWorldSectionVisibility();
    }

    private void ShowSelectedEntityDetails()
    {
        var details = RenderControl.GetEntityDetails(EntityListBox.SelectedIndex);
        if (details != null)
        {
            DetailsText.Text = details;
        }
    }

    private void OnRenderModesChanged(IReadOnlyList<string> modes)
    {
        ignoreRenderModeChange = true;
        var current = RenderModeComboBox.SelectedItem as string ?? "Default";
        var names = GetRenderModeNames(modes);
        RenderModeComboBox.ItemsSource = names;
        RenderModeComboBox.SelectedItem = names.Contains(current, StringComparer.Ordinal) ? current : "Default";
        ignoreRenderModeChange = false;
    }

    private static string[] GetRenderModeNames(IEnumerable<string> supportedModes)
    {
        var supported = supportedModes.ToHashSet(StringComparer.Ordinal);
        return RenderModes.Items
            .Where(mode => !mode.IsHeader && (mode.Name == "Default" || supported.Contains(mode.Name)))
            .Select(static mode => mode.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private void OnViewportErrorChanged(string? message)
    {
        ViewportMessageText.Text = message == null
            ? viewportStatus
            : $"Viewport error: {message}";

        if (message != null)
        {
            StatusText.Text = $"Viewport error: {message}";
        }
    }

    private void OnViewportStatusChanged(string message)
    {
        viewportStatus = $"Native OpenGL renderer viewport - {message}";
        ViewportMessageText.Text = viewportStatus;
    }

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewportInputLayer.Focus();
        var point = e.GetCurrentPoint(ViewportInputLayer);
        RenderControl.BeginPointerInput(point.Position, IsLeftPressed(point), IsRightPressed(point), IsMiddlePressed(point));
        e.Pointer.Capture(ViewportInputLayer);
        e.Handled = true;
    }

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var point = e.GetCurrentPoint(ViewportInputLayer);
        RenderControl.EndPointerInput(point.Properties.IsLeftButtonPressed, point.Properties.IsRightButtonPressed, point.Properties.IsMiddleButtonPressed);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        RenderControl.MovePointerInput(e.GetPosition(ViewportInputLayer));
        e.Handled = true;
    }

    private void OnViewportPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        RenderControl.WheelInput((float)e.Delta.Y);
        e.Handled = true;
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (await HandleAppShortcutAsync(e))
        {
            e.Handled = true;
        }
    }

    private async void OnViewportKeyDown(object? sender, KeyEventArgs e)
    {
        if (await HandleAppShortcutAsync(e))
        {
            e.Handled = true;
            return;
        }

        RenderControl.KeyInput(e.Key, pressed: true);
        e.Handled = true;
    }

    private void OnViewportKeyUp(object? sender, KeyEventArgs e)
    {
        RenderControl.KeyInput(e.Key, pressed: false);
        e.Handled = true;
    }

    private async Task<bool> HandleAppShortcutAsync(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (e.Key == Key.Left)
            {
                NavigateHistory(-1);
                return true;
            }

            if (e.Key == Key.Right)
            {
                NavigateHistory(1);
                return true;
            }
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.C && e.Source is not TextBox)
            {
                if (e.Source == ViewportInputLayer || e.Source == RenderControl)
                {
                    await CopyViewportScreenshotAsync();
                }
                else
                {
                    await CopySelectedEntryPathAsync();
                }

                return true;
            }

            if (e.Key == Key.O)
            {
                await OpenPackageAsync();
                return true;
            }

            if (e.Key == Key.F)
            {
                FocusSearch();
                return true;
            }
        }

        return HandleWindowShortcut(e.Key);
    }

    private async Task CopyViewportScreenshotAsync()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel?.Clipboard == null)
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), $"vrf-screenshot-{Guid.NewGuid():N}.png");
        clipboardScreenshotPath = path;
        RenderControl.SaveScreenshot(path);
        StatusText.Text = "Copying viewport screenshot...";
        await Task.CompletedTask;
    }

    private async void OnScreenshotSaved(string path)
    {
        if (!string.Equals(path, clipboardScreenshotPath, StringComparison.Ordinal))
        {
            return;
        }

        clipboardScreenshotPath = null;
        try
        {
            var topLevel = GetTopLevel(this);
            if (topLevel?.Clipboard == null)
            {
                return;
            }

            using var bitmap = new Bitmap(path);
            await topLevel.Clipboard.SetBitmapAsync(bitmap);
            StatusText.Text = "Copied viewport screenshot.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Failed to copy screenshot: {exception.Message}";
        }
        finally
        {
            File.Delete(path);
        }
    }

    private async Task CopySelectedEntryPathAsync()
    {
        if (EntryList.SelectedItem is not PackageEntryItem item)
        {
            return;
        }

        var topLevel = GetTopLevel(this);
        if (topLevel?.Clipboard == null)
        {
            return;
        }

        await topLevel.Clipboard.SetTextAsync(item.Path);
        StatusText.Text = $"Copied {item.Path}.";
    }

    private bool HandleWindowShortcut(Key key)
    {
        if (key == Key.F11)
        {
            if (WindowState == WindowState.FullScreen)
            {
                WindowState = previousWindowState;
            }
            else
            {
                previousWindowState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
                WindowState = WindowState.FullScreen;
            }

            ViewportInputLayer.Focus();
            return true;
        }

        if (key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            WindowState = previousWindowState;
            ViewportInputLayer.Focus();
            return true;
        }

        return false;
    }

    private static bool IsLeftPressed(PointerPoint point)
        => point.Properties.IsLeftButtonPressed || point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed;

    private static bool IsRightPressed(PointerPoint point)
        => point.Properties.IsRightButtonPressed || point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed;

    private static bool IsMiddlePressed(PointerPoint point)
        => point.Properties.IsMiddleButtonPressed || point.Properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed;

    private async void OnEntrySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (ignoreEntrySelectionChange)
        {
            return;
        }

        await LoadSelectedEntryAsync();
    }

    private async Task LoadSelectedEntryAsync()
    {
        if (EntryList.SelectedItem is not PackageEntryItem item || currentPackage == null)
        {
            return;
        }

        StopSound();
        SetSoundButtonsVisible(item.Entry.TypeName.Equals("vsnd_c", StringComparison.OrdinalIgnoreCase));

        var package = currentPackage;
        var packagePath = currentPath;
        var version = ++selectionVersion;
        DetailsText.Text = "Reading entry...";
        SetPreviewImage(null);

        var canRender = packagePath != null
            && item.Entry.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase)
            && IsRenderableCompiledType(item.Entry.TypeName);
        if (canRender)
        {
            RenderControl.LoadPackageEntry(package, packagePath!, item.Entry);
            SetViewportTabHeader(item.Path);
            MainTabs.SelectedItem = ViewportTab;
        }

        var (details, previewImage) = await Task.Run(() =>
        {
            var details = DescribePackageEntry(package, item.Entry);
            var previewImage = TryExtractPackagePreviewImageBytes(package, packagePath, item.Entry);

            if (!canRender && item.Entry.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                details += TryDescribeDecompiledPreview(package, packagePath, item.Entry);
            }

            return (details, previewImage);
        });

        if (version != selectionVersion || currentPackage != package || EntryList.SelectedItem is not PackageEntryItem selectedItem || selectedItem != item)
        {
            return;
        }

        DetailsText.Text = details;
        SetPreviewImage(CreatePreviewBitmap(previewImage));

        if (packagePath != null && item.Entry.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            if (!canRender)
            {
                StatusText.Text = $"No 3D viewport for {item.Entry.TypeName}; use preview/decompile/export.";
            }
        }
    }

    private async void OnSaveScreenshot(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        var target = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save viewport screenshot",
            SuggestedFileName = "source2-viewer.png",
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });

        var path = target?.TryGetLocalPath();
        if (path == null)
        {
            return;
        }

        RenderControl.SaveScreenshot(path);
        StatusText.Text = $"Saving screenshot to {path}.";
    }

    private async void OnPlaySound(object? sender, RoutedEventArgs e)
    {
        if (currentPackage == null || EntryList.SelectedItem is not PackageEntryItem item)
        {
            return;
        }

        try
        {
            StopSound();
            var (path, type) = await Task.Run(() => WriteSelectedSoundTempFile(currentPackage, item.Entry));
            soundTempPath = path;
            soundProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "ffplay",
                ArgumentList = { "-nodisp", "-autoexit", "-loglevel", "error", path },
                UseShellExecute = false,
            });
            StatusText.Text = $"Playing {type} sound.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Failed to play sound: {exception.Message}";
        }
    }

    private void StopSound()
    {
        if (soundProcess is { HasExited: false })
        {
            soundProcess.Kill(entireProcessTree: true);
        }

        soundProcess?.Dispose();
        soundProcess = null;

        if (soundTempPath != null)
        {
            File.Delete(soundTempPath);
            soundTempPath = null;
        }
    }

    private void SetSoundButtonsVisible(bool visible)
    {
        PlaySoundButton.IsVisible = visible;
        StopSoundButton.IsVisible = visible;
    }

    private static (string Path, string Type) WriteSelectedSoundTempFile(Package package, PackageEntry entry)
    {
        using var resource = new Resource { FileName = entry.GetFullPath() };
        using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
        resource.Read(stream, verifyFileSize: false);

        if (resource.DataBlock is not ValveResourceFormat.ResourceTypes.Sound sound || sound.StreamingDataSize == 0)
        {
            throw new InvalidOperationException("Selected resource has no playable sound data.");
        }

        var extension = sound.SoundType switch
        {
            ValveResourceFormat.ResourceTypes.Sound.AudioFileType.WAV => ".wav",
            ValveResourceFormat.ResourceTypes.Sound.AudioFileType.MP3 => ".mp3",
            ValveResourceFormat.ResourceTypes.Sound.AudioFileType.AAC => ".aac",
            _ => ".bin",
        };
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vrf-sound-{Guid.NewGuid():N}{extension}");
        using var soundStream = sound.GetSoundStream();
        using var output = File.Create(path);
        soundStream.CopyTo(output);
        return (path, sound.SoundType.ToString());
    }

    private async void OnExportSelected(object? sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not PackageEntryItem item || currentPackage == null)
        {
            StatusText.Text = "Select a package entry to export.";
            return;
        }

        var topLevel = GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        var suggestedName = item.Path.Split(SteamDatabase.ValvePak.Package.DirectorySeparatorChar).LastOrDefault();
        if (string.IsNullOrWhiteSpace(suggestedName))
        {
            suggestedName = "export.bin";
        }

        var target = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export selected package entry",
            SuggestedFileName = suggestedName,
            ShowOverwritePrompt = true,
        });

        if (target == null)
        {
            return;
        }

        await using var output = await target.OpenWriteAsync();
        using var input = GameFileLoader.GetPackageEntryStream(currentPackage, item.Entry);
        await input.CopyToAsync(output);
        StatusText.Text = $"Exported {item.Path}.";
    }

    private async void OnCopyEntryPath(object? sender, RoutedEventArgs e)
        => await CopySelectedEntryPathAsync();

    private async void OnExportVisible(object? sender, RoutedEventArgs e)
        => await ExportVisibleAsync(decompile: false);

    private async void OnExportVisibleDecompiled(object? sender, RoutedEventArgs e)
        => await ExportVisibleAsync(decompile: true);

    private async Task ExportVisibleAsync(bool decompile)
    {
        if (currentPackage == null || visibleEntries.Count == 0)
        {
            StatusText.Text = "No package entries are shown to export.";
            return;
        }

        var topLevel = GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose export folder",
            AllowMultiple = false,
        });

        var root = folders.Count == 0 ? null : folders[0].TryGetLocalPath();
        if (root == null)
        {
            return;
        }

        var package = currentPackage;
        var packagePath = currentPath;
        var entries = visibleEntries.ToArray();
        StatusText.Text = $"Exporting {entries.Length:N0} entries...";

        try
        {
            var exported = await Task.Run(() => ExportVisibleEntries(root, package, packagePath, entries, decompile));

            StatusText.Text = $"Exported {exported:N0} entries.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Export failed: {exception.Message}";
        }
    }

    private async void OnPreviewDecompiled(object? sender, RoutedEventArgs e)
    {
        try
        {
            DetailsText.Text = "Decompiling selected resource...";
            SetPreviewImage(null);
            var (details, previewImage) = await Task.Run(() =>
            {
                using var loaded = LoadSelectedResource();
                using var content = FileExtract.Extract(loaded.Resource, loaded.FileLoader);
                return (DescribeContentFile(content), TryGetPreviewImageBytes(content));
            });
            DetailsText.Text = details;
            SetPreviewImage(CreatePreviewBitmap(previewImage));
        }
        catch (Exception exception)
        {
            DetailsText.Text = $"Failed to decompile: {exception.Message}";
        }
    }

    private async void OnExportDecompiled(object? sender, RoutedEventArgs e)
    {
        ContentFile? content = null;
        try
        {
            using var loaded = LoadSelectedResource();
            content = await Task.Run(() => FileExtract.Extract(loaded.Resource, loaded.FileLoader));
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Failed to decompile: {exception.Message}";
            return;
        }

        using (content)
        {
            if (content.Data == null)
            {
                StatusText.Text = "Decompiled resource has no primary file to export.";
                return;
            }

            var topLevel = GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            var suggestedName = Path.GetFileName(content.FileName);
            if (string.IsNullOrWhiteSpace(suggestedName))
            {
                suggestedName = "decompiled.dat";
            }

            var target = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export decompiled resource",
                SuggestedFileName = suggestedName,
                ShowOverwritePrompt = true,
            });

            if (target == null)
            {
                return;
            }

            await using var output = await target.OpenWriteAsync();
            await output.WriteAsync(content.Data);
            StatusText.Text = $"Exported decompiled {suggestedName}.";
        }
    }

    private static string DescribeLooseFile(string path)
    {
        if (!File.Exists(path))
        {
            return $"File not found: {path}";
        }

        if (!path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return $"Path: {path}{Environment.NewLine}Size: {new FileInfo(path).Length:N0} bytes";
        }

        using var resource = new Resource();
        resource.Read(path);
        return DescribeResource(path, resource);
    }

    private static string TryDescribeRawTextPreview(Package package, PackageEntry entry)
    {
        const int MaxRawPreviewBytes = 256 * 1024;
        if (entry.TotalLength <= 0 || entry.TotalLength > MaxRawPreviewBytes)
        {
            return string.Empty;
        }

        try
        {
            using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var data = memory.ToArray();
            if (!LooksText(data))
            {
                return string.Empty;
            }

            var text = Encoding.UTF8.GetString(data);
            return string.Concat(Environment.NewLine, "Raw text preview:", Environment.NewLine, text);
        }
        catch (Exception exception)
        {
            return string.Concat(Environment.NewLine, "Raw text preview failed: ", exception.Message, Environment.NewLine);
        }
    }

    private static string TryDescribeDecompiledPreview(Package package, string? packagePath, PackageEntry entry)
    {
        try
        {
            using var fileLoader = new GameFileLoader(package, packagePath);
            using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
            using var resource = new Resource { FileName = entry.GetFullPath() };
            resource.Read(stream, verifyFileSize: false);
            using var content = FileExtract.Extract(resource, fileLoader);

            if (content.Data == null || !LooksText(content.Data))
            {
                return string.Empty;
            }

            return string.Concat(Environment.NewLine, "Decompiled preview:", Environment.NewLine, DescribeContentFile(content));
        }
        catch (Exception exception)
        {
            return string.Concat(Environment.NewLine, "Decompiled preview failed: ", exception.Message, Environment.NewLine);
        }
    }

    private static string DescribePackageEntry(Package package, PackageEntry entry)
    {
        var output = new StringBuilder();
        output.AppendLine(entry.GetFullPath());
        output.AppendLine($"Type: {entry.TypeName}");
        output.AppendLine($"Size: {entry.TotalLength:N0} bytes");

        if (!entry.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            output.Append(TryDescribeRawTextPreview(package, entry));
            return output.ToString();
        }

        try
        {
            using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
            using var resource = new Resource { FileName = entry.GetFullPath() };
            resource.Read(stream, verifyFileSize: false);

            output.AppendLine();
            output.Append(DescribeResource(entry.GetFullPath(), resource));
        }
        catch (Exception exception)
        {
            output.AppendLine();
            output.AppendLine($"Failed to parse resource: {exception.Message}");
        }

        return output.ToString();
    }

    private static string DescribeResource(string path, Resource resource)
    {
        var output = new StringBuilder();
        output.AppendLine($"Path: {path}");
        output.AppendLine($"Resource type: {resource.ResourceType}");
        output.AppendLine($"Version: {resource.Version}");
        output.AppendLine($"Blocks: {string.Join(", ", resource.Blocks.Select(static block => block.Type))}");

        if (resource.ExternalReferences?.ResourceRefInfoList.Count > 0)
        {
            output.AppendLine();
            output.AppendLine("External references:");

            foreach (var reference in resource.ExternalReferences.ResourceRefInfoList.Take(25))
            {
                output.AppendLine($"- {reference.Name}");
            }

            var remaining = resource.ExternalReferences.ResourceRefInfoList.Count - 25;
            if (remaining > 0)
            {
                output.AppendLine($"... {remaining:N0} more");
            }
        }

        return output.ToString();
    }

#pragma warning disable CA2000 // LoadedResource owns and disposes these objects.
    private LoadedResource LoadSelectedResource()
    {
        if (currentPackage != null && EntryList.SelectedItem is PackageEntryItem item)
        {
            if (!item.Entry.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Selected package entry is not a compiled resource.");
            }

            var fileLoader = new GameFileLoader(currentPackage, currentPath);
            var resource = new Resource { FileName = item.Path };
            using var stream = GameFileLoader.GetPackageEntryStream(currentPackage, item.Entry);
            resource.Read(stream, verifyFileSize: false);
            return new LoadedResource(resource, fileLoader);
        }

        if (currentPackage == null && currentPath != null && currentPath.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var fileLoader = new GameFileLoader(null, currentPath);
            var resource = new Resource();
            resource.Read(currentPath);
            return new LoadedResource(resource, fileLoader);
        }

        throw new InvalidOperationException("Select a compiled resource first.");
    }
#pragma warning restore CA2000

    private static byte[]? TryExtractLooseFilePreviewImageBytes(string path)
    {
        try
        {
            if (IsSupportedImageFileName(path))
            {
                return File.ReadAllBytes(path);
            }

            if (!path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using var fileLoader = new GameFileLoader(null, path);
            using var resource = new Resource();
            resource.Read(path);
            using var content = FileExtract.Extract(resource, fileLoader);
            return TryGetPreviewImageBytes(content);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[]? TryExtractPackagePreviewImageBytes(Package package, string? packagePath, PackageEntry entry)
    {
        try
        {
            if (IsSupportedImageFileName(entry.GetFullPath()))
            {
                using var rawStream = GameFileLoader.GetPackageEntryStream(package, entry);
                using var rawMemory = new MemoryStream();
                rawStream.CopyTo(rawMemory);
                return rawMemory.ToArray();
            }

            if (!entry.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using var fileLoader = new GameFileLoader(package, packagePath);
            using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
            using var resource = new Resource { FileName = entry.GetFullPath() };
            resource.Read(stream, verifyFileSize: false);
            using var content = FileExtract.Extract(resource, fileLoader);
            return TryGetPreviewImageBytes(content);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[]? TryGetPreviewImageBytes(ContentFile content)
    {
        if (content.Data != null
            && (IsSupportedImageFileName(content.FileName) || LooksSupportedImageBytes(content.Data)))
        {
            return content.Data;
        }

        foreach (var subFile in content.SubFiles)
        {
            if (subFile.Extract == null || !IsSupportedImageFileName(subFile.FileName))
            {
                continue;
            }

            try
            {
                var data = subFile.Extract();
                if (LooksSupportedImageBytes(data))
                {
                    return data;
                }
            }
            catch (Exception)
            {
            }
        }

        foreach (var additionalFile in content.AdditionalFiles)
        {
            var data = TryGetPreviewImageBytes(additionalFile);
            if (data != null)
            {
                return data;
            }
        }

        return null;
    }

    private static bool IsSupportedImageFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAudioFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vsnd_c", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksSupportedImageBytes(byte[] data)
    {
        if (data.Length >= 8
            && data[0] == 0x89
            && data[1] == (byte)'P'
            && data[2] == (byte)'N'
            && data[3] == (byte)'G')
        {
            return true;
        }

        if (data.Length >= 3
            && data[0] == 0xFF
            && data[1] == 0xD8
            && data[2] == 0xFF)
        {
            return true;
        }

        if (data.Length >= 6
            && data[0] == (byte)'G'
            && data[1] == (byte)'I'
            && data[2] == (byte)'F')
        {
            return true;
        }

        if (data.Length >= 2
            && data[0] == (byte)'B'
            && data[1] == (byte)'M')
        {
            return true;
        }

        return data.Length >= 12
            && data[0] == (byte)'R'
            && data[1] == (byte)'I'
            && data[2] == (byte)'F'
            && data[3] == (byte)'F'
            && data[8] == (byte)'W'
            && data[9] == (byte)'E'
            && data[10] == (byte)'B'
            && data[11] == (byte)'P';
    }

    private static int ExportVisibleEntries(string root, Package package, string? packagePath, PackageEntryItem[] entries, bool decompile)
    {
        var count = 0;
        using var fileLoader = new GameFileLoader(package, packagePath);

        foreach (var item in entries)
        {
            if (!decompile || !item.Entry.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                CopyPackageEntry(root, package, item);
                count++;
                continue;
            }

            using var input = GameFileLoader.GetPackageEntryStream(package, item.Entry);
            using var resource = new Resource { FileName = item.Path };
            resource.Read(input, verifyFileSize: false);
            using var content = FileExtract.Extract(resource, fileLoader);

            count += WriteContentFile(root, item.Path, resource, content);
        }

        return count;
    }

    private static void CopyPackageEntry(string root, Package package, PackageEntryItem item)
    {
        var outputPath = GetSafeExportPath(root, item.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var input = GameFileLoader.GetPackageEntryStream(package, item.Entry);
        using var output = File.Create(outputPath);
        input.CopyTo(output);
    }

    private static int WriteContentFile(string root, string inputPath, Resource resource, ContentFile content)
    {
        var count = 0;

        if (content.Data != null)
        {
            var outputPath = GetSafeExportPath(root, GetDecompiledPrimaryPath(inputPath, resource, content));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, content.Data);
            count++;
        }

        foreach (var additionalFile in content.AdditionalFiles)
        {
            if (additionalFile.Data != null && !string.IsNullOrWhiteSpace(additionalFile.FileName))
            {
                var outputPath = GetSafeExportPath(root, additionalFile.FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllBytes(outputPath, additionalFile.Data);
                count++;
            }

            var folder = Path.GetDirectoryName(additionalFile.FileName) ?? string.Empty;
            count += WriteSubFiles(root, folder, additionalFile);
        }

        var inputFolder = Path.GetDirectoryName(inputPath) ?? string.Empty;
        return count + WriteSubFiles(root, inputFolder, content);
    }

    private static int WriteSubFiles(string root, string folder, ContentFile content)
    {
        var count = 0;

        foreach (var subFile in content.SubFiles)
        {
            if (subFile.Extract == null)
            {
                continue;
            }

            var data = subFile.Extract();
            if (data.Length == 0)
            {
                continue;
            }

            var relativePath = string.IsNullOrEmpty(folder)
                ? subFile.FileName
                : string.Concat(folder, SteamDatabase.ValvePak.Package.DirectorySeparatorChar, subFile.FileName);
            var outputPath = GetSafeExportPath(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, data);
            count++;
        }

        return count;
    }

    private static string GetDecompiledPrimaryPath(string inputPath, Resource resource, ContentFile content)
    {
        var extension = FileExtract.GetExtension(resource);
        if (extension != null)
        {
            return Path.ChangeExtension(inputPath, extension);
        }

        return string.IsNullOrWhiteSpace(content.FileName)
            ? inputPath
            : content.FileName;
    }

    private static string GetSafeExportPath(string root, string packagePath)
    {
        var rootPath = Path.GetFullPath(root);
        var relativePath = packagePath.Replace(SteamDatabase.ValvePak.Package.DirectorySeparatorChar, Path.DirectorySeparatorChar);
        var outputPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;

        if (!outputPath.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Package entry path escapes export folder: {packagePath}");
        }

        return outputPath;
    }

    internal static void RunExportPathSelfCheck()
    {
        var root = Path.Combine(Path.GetTempPath(), "vrf-export-check");
        var valid = GetSafeExportPath(root, $"materials{SteamDatabase.ValvePak.Package.DirectorySeparatorChar}test.vtex_c");
        var expected = Path.Combine(Path.GetFullPath(root), "materials", "test.vtex_c");
        if (!string.Equals(valid, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Safe export path check failed for valid package path.");
        }

        try
        {
            GetSafeExportPath(root, $"..{SteamDatabase.ValvePak.Package.DirectorySeparatorChar}escape.txt");
            throw new InvalidOperationException("Safe export path check failed to reject traversal.");
        }
        catch (InvalidDataException)
        {
        }
    }

    private static Bitmap? CreatePreviewBitmap(byte[]? data)
    {
        if (data == null)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(data);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void SetPreviewImage(Bitmap? bitmap)
    {
        if (PreviewImage.Source is IDisposable disposable)
        {
            disposable.Dispose();
        }

        PreviewImage.Source = bitmap;
        PreviewImageBorder.IsVisible = bitmap != null;
    }

    private static string DescribeContentFile(ContentFile content)
    {
        var output = new StringBuilder();
        output.AppendLine($"Suggested file: {content.FileName}");
        output.AppendLine($"Primary data: {(content.Data == null ? "none" : $"{content.Data.Length:N0} bytes")}");
        output.AppendLine($"Additional files: {content.AdditionalFiles.Count:N0}");
        output.AppendLine($"Subfiles: {content.SubFiles.Count:N0}");

        if (content.Data == null)
        {
            return output.ToString();
        }

        output.AppendLine();
        if (!LooksText(content.Data))
        {
            output.AppendLine("Primary decompiled output is binary; use Export decompiled.");
            return output.ToString();
        }

        var text = Encoding.UTF8.GetString(content.Data);
        const int MaxPreviewChars = 60_000;
        if (text.Length > MaxPreviewChars)
        {
            text = string.Concat(text.AsSpan(0, MaxPreviewChars), Environment.NewLine, "... preview truncated ...");
        }

        output.Append(text);
        return output.ToString();
    }

    private static bool LooksText(byte[] data)
    {
        var length = Math.Min(data.Length, 4096);
        for (var i = 0; i < length; i++)
        {
            if (data[i] == 0)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class LoadedResource(Resource resource, GameFileLoader fileLoader) : IDisposable
    {
        public Resource Resource { get; } = resource;
        public GameFileLoader FileLoader { get; } = fileLoader;

        public void Dispose()
        {
            Resource.Dispose();
            FileLoader.Dispose();
        }
    }

    private sealed class PackageEntryItem(PackageEntry entry)
    {
        public PackageEntry Entry { get; } = entry;
        public string Path { get; } = entry.GetFullPath();
        public string TypeName => Entry.TypeName;
        public string SizeText => Entry.TotalLength.ToString("N0");
        public override string ToString() => $"{Path,-72} {TypeName,-12} {SizeText,10}";
    }
}
