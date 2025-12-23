using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat.IO;

#if DEBUG
using System.Reflection;
#endif

#nullable disable

namespace GUI.Controls
{
    partial class ExplorerControl : UserControl
    {
        private class TreeDataNode
        {
            public required TreeNode ParentNode { get; init; }
            public required int AppID { get; init; }
            public required TreeNode[] Children { get; set; }
            public bool ExpandOnFirstSearch { get; set; } = true;
        }

        private const int APPID_RECENT_FILES = -1000;
        private const int APPID_BOOKMARKS = -1001;
        private readonly List<TreeDataNode> TreeData = [];
        private static readonly Dictionary<string, string> WorkshopAddons = [];
        public static readonly List<GameFolderLocator.SteamLibraryGameInfo> SteamGames = [];

        public ExplorerControl()
        {
            InitializeComponent();

            foreach (Control control in Controls)
            {
                Themer.ThemeControl(control);
            }

            filterTextBox.BackColor = Themer.CurrentThemeColors.AppMiddle;

            treeView.ImageList = MainForm.ImageList;

            Scan();
        }

        private void Scan()
        {
            var recentImage = MainForm.Icons["History"];

            // Bookmarks
            {
                var bookmarkImage = MainForm.Icons["Bookmarks"];
                var bookmarkedFilesTreeNode = new TreeNode("Bookmarks")
                {
                    ImageIndex = bookmarkImage,
                    SelectedImageIndex = bookmarkImage,
                };
                bookmarkedFilesTreeNode.Expand();

                var bookmarkedFileNodes = GetBookmarkedFileNodes();
                bookmarkedFilesTreeNode.Nodes.AddRange(bookmarkedFileNodes);

                TreeData.Add(new TreeDataNode
                {
                    ParentNode = bookmarkedFilesTreeNode,
                    AppID = APPID_BOOKMARKS,
                    Children = bookmarkedFileNodes,
                });
                treeView.Nodes.Add(bookmarkedFilesTreeNode);
            }

            // Recent files
            {
                var recentFilesTreeNode = new TreeNode("Recent files")
                {
                    ImageIndex = recentImage,
                    SelectedImageIndex = recentImage,
                    ContextMenuStrip = recentFilesContextMenuStrip,
                };
                recentFilesTreeNode.Expand();

                var recentFileNodes = GetRecentFileNodes();
                recentFilesTreeNode.Nodes.AddRange(recentFileNodes);

                TreeData.Add(new TreeDataNode
                {
                    ParentNode = recentFilesTreeNode,
                    AppID = APPID_RECENT_FILES,
                    Children = recentFileNodes,
                });
                treeView.Nodes.Add(recentFilesTreeNode);
            }

#if DEBUG
            DebugAddEmbeddedResourcesToTree();
#endif

            var scanningTreeNode = new TreeNode("Scanning game folders…")
            {
                ImageIndex = recentImage,
                SelectedImageIndex = recentImage,
            };
            treeView.Nodes.Add(scanningTreeNode);

            // Scan for vpks
            Task.Factory.StartNew(ScanForSteamGames).ContinueWith(t =>
            {
                InvokeWorkaround(() =>
                {
                    if (t.Exception != null)
                    {
                        scanningTreeNode.Text = t.Exception.Message;
                        Log.Error(nameof(ExplorerControl), t.Exception.ToString());
                    }
                    else
                    {
                        scanningTreeNode.Remove();
                    }
                });
            });
        }

        private void ScanForSteamGames()
        {
            if (GameFolderLocator.SteamPath == null)
            {
                return;
            }

            var vpkImage = MainForm.ExtensionIcons["vpk"];
            var vcsImage = MainForm.Icons["FolderShaders"];
            var mapImage = MainForm.Icons["FolderMap"];
            var pluginImage = MainForm.Icons["FolderPlugin"];
            var folderImage = MainForm.Icons["Folder"];

            int GetSortPriorityForImage(int image)
            {
                if (image == vpkImage)
                {
                    return 10;
                }
                else if (image == vcsImage)
                {
                    return 9;
                }
                else if (image == mapImage)
                {
                    return 8;
                }

                return 0;
            }

            int SortFileNodes(TreeNode a, TreeNode b)
            {
                var image = GetSortPriorityForImage(b.ImageIndex).CompareTo(GetSortPriorityForImage(a.ImageIndex));

                if (image != 0)
                {
                    return image;
                }

                return string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
            }

            var libraryCachePath = Path.Join(GameFolderLocator.SteamPath, "appcache", "librarycache");
            var kvDeserializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
            KVDocument libraryAssetsKv = null;

            try
            {
                using var stream = File.OpenRead(Path.Join(libraryCachePath, "assetcache.vdf"));
                libraryAssetsKv = KVSerializer.Create(KVSerializationFormat.KeyValues1Binary).Deserialize(stream);
            }
            catch (FileNotFoundException)
            {
                //
            }

            if (SteamGames.Count == 0)
            {
                var steamGames = GameFolderLocator.FindAllSteamGames()
                    // Ignore Apex Legends, Titanfall, Titanfall 2 because Respawn has customized VPK format and VRF can't open it
                    .Where(static gameInfo => gameInfo.AppID is not (1237970 or 1454890 or 1172470))
                    .OrderBy(static gameInfo => gameInfo.AppID)
                    .ToList();

                SteamGames.AddRange(steamGames);
            }

            // Find all games to be displayed in the recent and bookmarked files
            // to instantly load their icons before rendering the list
            {
                foreach (var path in Settings.Config.RecentFiles.Concat(Settings.Config.BookmarkedFiles))
                {
                    foreach (var game in SteamGames)
                    {
                        if (path.StartsWith(game.GamePath, StringComparison.OrdinalIgnoreCase))
                        {
                            GetOrLoadAppImage(game.AppID, libraryAssetsKv, libraryCachePath);
                        }
                    }
                }

                InvokeWorkaround(() =>
                {
                    RedrawList(APPID_BOOKMARKS, GetBookmarkedFileNodes());
                    RedrawList(APPID_RECENT_FILES, GetRecentFileNodes());
                });
            }

            if (SteamGames.Count == 0)
            {
                return;
            }

            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 5,
                BufferSize = 65536,
            };

            var checkedDirVpks = new Dictionary<string, bool>();
            var scannedGamePaths = new HashSet<string>(SteamGames.Count, StringComparer.OrdinalIgnoreCase);

            bool VpkPredicate(ref FileSystemEntry entry)
            {
                if (entry.IsDirectory)
                {
                    return false;
                }

                if (!entry.FileName.EndsWith(".vpk", StringComparison.Ordinal))
                {
                    return false;
                }

                if (!Regexes.VpkNumberArchive().IsMatch(entry.FileName))
                {
                    return true;
                }

                // If we matched dota_683.vpk, make sure dota_dir.vpk exists before excluding it from results
                var fixedPackage = $"{entry.ToFullPath()[..^8]}_dir.vpk";

                if (!checkedDirVpks.TryGetValue(fixedPackage, out var ret))
                {
                    ret = !File.Exists(fixedPackage);
                    checkedDirVpks.Add(fixedPackage, ret);
                }

                return ret;
            }

            foreach (var (appID, appName, steamPath, gamePath) in SteamGames)
            {
                // Skip if this game path was already scanned from another appID
                if (!scannedGamePaths.Add(gamePath))
                {
                    continue;
                }

                var foundFiles = new List<TreeNode>();

                // Find all the vpks in game folder
                var vpks = new FileSystemEnumerable<string>(
                    gamePath,
                    (ref entry) => entry.ToSpecifiedFullPath(),
                    enumerationOptions)
                {
                    ShouldIncludePredicate = VpkPredicate
                };

                foreach (var vpk in vpks)
                {
                    var image = vpkImage;
                    var vpkName = vpk[gamePath.Length..].Replace(Path.DirectorySeparatorChar, '/');
                    var fileName = Path.GetFileName(vpkName);

                    if (fileName.EndsWith("_bakeresourcecache.vpk", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (fileName.StartsWith("shaders_", StringComparison.Ordinal))
                    {
                        image = vcsImage;
                    }
                    else if (vpkName.Contains("/maps/", StringComparison.Ordinal))
                    {
                        image = mapImage;
                    }

                    foundFiles.Add(new TreeNode(vpkName)
                    {
                        Tag = vpk,
                        ImageIndex = image,
                        SelectedImageIndex = image,
                    });
                }

                if (foundFiles.Count == 0)
                {
                    continue;
                }

                // Find workshop content
                try
                {
                    KVObject workshopInfo;
                    var workshopManifest = Path.Join(steamPath, "workshop", $"appworkshop_{appID}.acf");

                    if (File.Exists(workshopManifest))
                    {
                        using (var stream = File.OpenRead(workshopManifest))
                        {
                            workshopInfo = kvDeserializer.Deserialize(stream);
                        }

                        foreach (var item in (IEnumerable<KVObject>)workshopInfo["WorkshopItemsInstalled"])
                        {
                            var addonPath = Path.Join(steamPath, "workshop", "content", appID.ToString(CultureInfo.InvariantCulture), item.Name);
                            var publishDataPath = Path.Join(addonPath, "publish_data.txt");
                            var vpk = Path.Join(addonPath, $"{item.Name}.vpk");

                            if (!File.Exists(vpk))
                            {
                                vpk = Path.Join(addonPath, $"{item.Name}_dir.vpk");

                                if (!File.Exists(vpk))
                                {
                                    continue;
                                }
                            }

                            using var stream = File.OpenRead(publishDataPath);
                            var publishData = kvDeserializer.Deserialize(stream);
                            var addonTitle = publishData["title"];
                            var displayTitle = $"[Workshop {item.Name}] {addonTitle}";

                            foundFiles.Add(new TreeNode(displayTitle)
                            {
                                Tag = vpk,
                                ImageIndex = pluginImage,
                                SelectedImageIndex = pluginImage,
                            });

                            WorkshopAddons[vpk] = displayTitle;
                        }
                    }
                }
                catch (Exception)
                {
                    //
                }

                // Sort the files and create the nodes
                foundFiles.Sort(SortFileNodes);
                var foundFilesArray = foundFiles.ToArray();

                var treeNodeImage = GetOrLoadAppImage(appID, libraryAssetsKv, libraryCachePath);

                if (treeNodeImage < 0)
                {
                    treeNodeImage = folderImage;
                }

                var treeNodeName = $"[{appID}] {appName} - {gamePath.Replace(Path.DirectorySeparatorChar, '/')}";
                var treeNode = new TreeNode(treeNodeName)
                {
                    Tag = gamePath,
                    ImageIndex = treeNodeImage,
                    SelectedImageIndex = treeNodeImage,
                };
                treeNode.Nodes.AddRange(foundFilesArray);
                TreeData.Add(new TreeDataNode
                {
                    ParentNode = treeNode,
                    AppID = appID,
                    Children = foundFilesArray,
                });

                InvokeWorkaround(() =>
                {
                    treeView.BeginUpdate();
                    treeView.Nodes.Insert(treeView.Nodes.Count - 1, treeNode);
                    treeView.EndUpdate();

                    if (filterTextBox.Text.Length > 0)
                    {
                        OnFilterTextBoxTextChanged(null, null); // Hack: re-filter
                    }
                });
            }

            // Update bookmarks and recent files with workshop titles and app logos
            InvokeWorkaround(() =>
            {
                RedrawList(APPID_BOOKMARKS, GetBookmarkedFileNodes());
                RedrawList(APPID_RECENT_FILES, GetRecentFileNodes());
            });
        }

        private void InvokeWorkaround(Action action)
        {
            if (treeView.InvokeRequired && !treeView.IsDisposed)
            {
                treeView.Invoke(action);
            }
            else
            {
                action();
            }
        }

        private void OnTreeViewNodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            var node = e.Node;

            // If selected node has children, that means we likely expanded it, and that can cause the event node to be wrong for some reason
            // https://stackoverflow.com/q/473113
            if (treeView.SelectedNode?.Nodes.Count > 0)
            {
                node = treeView.SelectedNode;
            }

            if (node.Tag is not string path)
            {
                return;
            }

#if DEBUG
            if (DebugOpenEmbeddedResource(path))
            {
                return;
            }
#endif

            if (File.Exists(path))
            {
                Program.MainForm.OpenFile(path);
            }
        }

        private void OnTreeViewNodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node.Tag is string path && e.Button == MouseButtons.Right)
            {
                e.Node.TreeView.SelectedNode = e.Node;

                var isBookmarked = Settings.Config.BookmarkedFiles.Contains(path);
                var isRecent = Settings.Config.RecentFiles.Contains(path);

                addToFavoritesToolStripMenuItem.Visible = !isBookmarked;
                removeFromFavoritesToolStripMenuItem.Visible = isBookmarked;
                removeFromRecentToolStripMenuItem.Visible = isRecent;

                fileContextMenuStrip.Show(e.Node.TreeView, e.Location);
            }
        }

        private void OnFilterTextBoxTextChanged(object sender, EventArgs e)
        {
            treeView.BeginUpdate();
            treeView.Nodes.Clear();

            var text = filterTextBox.Text.Replace(Path.DirectorySeparatorChar, '/');
            var showAll = text.Length == 0;

            var foundNodes = new List<TreeNode>(TreeData.Count);

            foreach (var node in TreeData)
            {
                node.ParentNode.Nodes.Clear();

                if (showAll)
                {
                    node.ParentNode.Nodes.AddRange(node.Children);
                    foundNodes.Add(node.ParentNode);

                    continue;
                }

                var first = true;

                foreach (var child in node.Children)
                {
                    if (!child.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
                    {
                        if (child.Tag is not string path || !path.Contains(text, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    node.ParentNode.Nodes.Add(child);

                    if (first)
                    {
                        first = false;
                        foundNodes.Add(node.ParentNode);

                        if (node.ExpandOnFirstSearch)
                        {
                            node.ExpandOnFirstSearch = false;
                            node.ParentNode.Expand();
                        }
                    }
                }
            }

            treeView.Nodes.AddRange([.. foundNodes]);
            treeView.EndUpdate();
        }

        private void OnVisibleChanged(object sender, EventArgs e)
        {
            // Refresh recent files list whenever explorer becomes visible
            if (!Visible)
            {
                return;
            }

            RedrawList(APPID_RECENT_FILES, GetRecentFileNodes());
        }

        private void RedrawList(int appid, TreeNode[] list)
        {
            treeView.BeginUpdate();
            var node = TreeData.Find(node => node.AppID == appid);
            node.ParentNode.Nodes.Clear();
            node.ParentNode.Nodes.AddRange(list);
            node.ParentNode.Expand();
            node.Children = list;
            treeView.EndUpdate();

            if (filterTextBox.Text.Length > 0)
            {
                OnFilterTextBoxTextChanged(null, null); // Hack: re-filter files
            }
        }

        private static TreeNode[] GetRecentFileNodes() => GetFileNodes(Settings.Config.RecentFiles);
        private static TreeNode[] GetBookmarkedFileNodes() => GetFileNodes(Settings.Config.BookmarkedFiles);

        private static TreeNode[] GetFileNodes(List<string> paths)
        {
            var treeNodes = new TreeNode[paths.Count];
            var treeNodeIndex = 0;

            for (var i = paths.Count - 1; i >= 0; i--)
            {
                var path = paths[i];
                var pathDisplay = path.Replace(Path.DirectorySeparatorChar, '/');
                var isVpk = false;
                var imageIndexFile = -1;
                var imageIndexGame = -1;

                if (WorkshopAddons.TryGetValue(path, out var displayTitle))
                {
                    imageIndexFile = MainForm.Icons["FolderPlugin"];
                    pathDisplay = $"{pathDisplay} {displayTitle}";
                }
                else
                {
                    var extension = Path.GetExtension(path).ToLowerInvariant().AsSpan();
                    isVpk = MemoryExtensions.Equals(extension, ".vpk", StringComparison.Ordinal);

                    if (isVpk && pathDisplay.Contains("/maps/", StringComparison.Ordinal))
                    {
                        extension = ".map";
                    }

                    if (extension.Length > 0)
                    {
                        extension = extension[1..];
                    }

                    imageIndexFile = MainForm.GetImageIndexForExtension(extension);
                }

                foreach (var game in SteamGames)
                {
                    if (path.StartsWith(game.GamePath, StringComparison.OrdinalIgnoreCase))
                    {
                        pathDisplay = $"[{game.AppName}] {pathDisplay.AsSpan(game.GamePath.Length)}";

                        if (isVpk)
                        {
                            imageIndexGame = MainForm.ImageList.Images.IndexOfKey($"@app{game.AppID}");
                        }

                        break;
                    }
                }

                var image = imageIndexGame > -1 ? imageIndexGame : imageIndexFile;
                treeNodes[treeNodeIndex++] = new TreeNode(pathDisplay)
                {
                    Tag = path,
                    ImageIndex = image,
                    SelectedImageIndex = image,
                };
            }

            return treeNodes;
        }

        private void OnClearRecentFilesClick(object sender, EventArgs e)
        {
            Settings.ClearRecentFiles();

            var recentFilesNode = TreeData.Find(node => node.AppID == APPID_RECENT_FILES);
            recentFilesNode.ParentNode.Nodes.Clear();
            recentFilesNode.Children = [];
        }

        private void OnRevealInFileExplorerClick(object sender, EventArgs e)
        {
            var control = (TreeView)((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl;

            if (control.SelectedNode.Tag is not string path)
            {
                return;
            }

            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = "explorer.exe",
                    Arguments = @$"/select, ""{path}"""
                });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = path + Path.DirectorySeparatorChar,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
        }

        private void OnAddToBookmarksClick(object sender, EventArgs e)
        {
            var control = (TreeView)((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl;

            if (control.SelectedNode.Tag is not string path)
            {
                return;
            }

            if (Settings.Config.BookmarkedFiles.Contains(path))
            {
                return;
            }

            Settings.Config.BookmarkedFiles.Add(path);

            RedrawList(APPID_BOOKMARKS, GetBookmarkedFileNodes());
        }

        private void OnRemoveFromBookmarksClick(object sender, EventArgs e)
        {
            var control = (TreeView)((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl;

            if (control.SelectedNode.Tag is not string path)
            {
                return;
            }

            Settings.Config.BookmarkedFiles.Remove(path);

            RedrawList(APPID_BOOKMARKS, GetBookmarkedFileNodes());
        }

        private void OnRemoveFromRecentClick(object sender, EventArgs e)
        {
            var control = (TreeView)((ContextMenuStrip)((ToolStripMenuItem)sender).Owner).SourceControl;

            if (control.SelectedNode.Tag is not string path)
            {
                return;
            }

            Settings.Config.RecentFiles.Remove(path);

            RedrawList(APPID_RECENT_FILES, GetRecentFileNodes());
        }

        private void OnExplorerLoad(object sender, EventArgs e)
        {
            filterTextBox.Focus();
        }

        public void FocusFilter()
        {
            filterTextBox.Focus();
        }

        private int GetOrLoadAppImage(int appID, KVObject libraryAssetsKv, string libraryCachePath)
        {
            var imageKey = $"@app{appID}";
            var treeNodeImage = MainForm.ImageList.Images.IndexOfKey(imageKey);

            if (treeNodeImage >= 0)
            {
                return treeNodeImage;
            }

            try
            {
                string appIconPath = null;

                if (libraryAssetsKv != null)
                {
                    // Find the actual icon filename in the assets cache, there are keys like "0f" "1f" etc, which appears to be an enum.
                    var appIDStr = appID.ToString(CultureInfo.InvariantCulture);
                    var filename = libraryAssetsKv["0"]?[appIDStr]?["4f"];

                    if (filename != null)
                    {
                        appIconPath = Path.Join(libraryCachePath, appIDStr, filename.ToString());

                        if (!File.Exists(appIconPath))
                        {
                            appIconPath = null;
                        }
                    }
                }

                if (appIconPath != null)
                {
                    using var appIcon = GetAppResizedImage(appIconPath);

                    InvokeWorkaround(() =>
                    {
                        MainForm.ImageList.Images.Add(imageKey, appIcon);
                    });

                    treeNodeImage = MainForm.ImageList.Images.IndexOfKey(imageKey);
                }
            }
            catch (Exception)
            {
                //
            }

            return treeNodeImage;
        }

        private static Bitmap GetAppResizedImage(string path)
        {
            var originalImage = Image.FromFile(path);

            var destRect = new Rectangle(0, 0, MainForm.ImageList.ImageSize.Width, MainForm.ImageList.ImageSize.Height);
            var destImage = new Bitmap(MainForm.ImageList.ImageSize.Width, MainForm.ImageList.ImageSize.Height);

            destImage.SetResolution(originalImage.HorizontalResolution, originalImage.VerticalResolution);

            using var graphics = Graphics.FromImage(destImage);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImage(originalImage, destRect, 0, 0, originalImage.Width, originalImage.Height, GraphicsUnit.Pixel);

            return destImage;
        }

#if DEBUG
        private void DebugAddEmbeddedResourcesToTree()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var embeddedResources = assembly.GetManifestResourceNames().Where(n => n.StartsWith("GUI.Utils.", StringComparison.Ordinal) && n.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal));

            var imageIndex = MainForm.Icons["Folder"];
            var embeddedFilesTreeNode = new TreeNode("Embedded Resources")
            {
                ImageIndex = imageIndex,
                SelectedImageIndex = imageIndex,
            };

            foreach (var embeddedResource in embeddedResources)
            {
                var extension = Path.GetExtension(embeddedResource.AsSpan());
                imageIndex = MainForm.GetImageIndexForExtension(extension[1..]);

                var debugTreeNode = new TreeNode(embeddedResource)
                {
                    Tag = $"vrf_embedded:{embeddedResource}",
                    ImageIndex = imageIndex,
                    SelectedImageIndex = imageIndex,
                };
                embeddedFilesTreeNode.Nodes.Add(debugTreeNode);
            }

            // Icons
            var iconsImageIndex = MainForm.Icons["Folder"];
            var iconsTreeNode = new TreeNode("Icons")
            {
                ImageIndex = iconsImageIndex,
                SelectedImageIndex = iconsImageIndex,
            };

            foreach (var iconEntry in MainForm.Icons)
            {
                var iconNode = new TreeNode(iconEntry.Key)
                {
                    ImageIndex = iconEntry.Value,
                    SelectedImageIndex = iconEntry.Value,
                };
                iconsTreeNode.Nodes.Add(iconNode);
            }

            embeddedFilesTreeNode.Nodes.Add(iconsTreeNode);

            // Extensions
            var extensionsImageIndex = MainForm.Icons["Folder"];
            var extensionsTreeNode = new TreeNode("Extensions")
            {
                ImageIndex = extensionsImageIndex,
                SelectedImageIndex = extensionsImageIndex,
            };

            foreach (var extEntry in MainForm.ExtensionIcons)
            {
                var extNode = new TreeNode(extEntry.Key)
                {
                    ImageIndex = extEntry.Value,
                    SelectedImageIndex = extEntry.Value,
                };
                extensionsTreeNode.Nodes.Add(extNode);
            }

            embeddedFilesTreeNode.Nodes.Add(extensionsTreeNode);

            treeView.Nodes.Add(embeddedFilesTreeNode);
        }

        private static bool DebugOpenEmbeddedResource(string path)
        {
            if (!path.StartsWith("vrf_embedded:", StringComparison.Ordinal))
            {
                return false;
            }

            var name = path["vrf_embedded:".Length..];

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(name);
            using var ms = new MemoryStream((int)stream.Length);

            using var package = new SteamDatabase.ValvePak.Package();
            stream.CopyTo(ms);
            var file = package.AddFile(name, ms.ToArray());

            using var contextPackage = new VrfGuiContext("vrf_embedded.vpk", null)
            {
                CurrentPackage = package
            };
            var contextFile = new VrfGuiContext(name, contextPackage);

            try
            {
                Program.MainForm.OpenFile(contextFile, file);
                contextFile = null;
            }
            finally
            {
                contextFile?.Dispose();
            }

            return true;
        }
#endif
    }
}
