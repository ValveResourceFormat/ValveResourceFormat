using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using GUI.Utils;
using ValveKeyValue;

namespace GUI.Controls
{
    public partial class ExplorerControl : UserControl
    {
        public ExplorerControl()
        {
            InitializeComponent();

            treeView.ImageList = MainForm.ImageList;

            var steam = Settings.GetSteamPath();

            var vpkRegex = new Regex(@"_[0-9]{3}\.vpk$");
            var kvDeserializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

            var libraryfolders = Path.Join(steam, "libraryfolders.vdf");
            using var libraryFoldersStream = File.OpenRead(libraryfolders);
            var libraryFoldersKv = kvDeserializer.Deserialize(libraryFoldersStream, KVSerializerOptions.DefaultOptions);

            var steamPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steam };

            foreach (var child in libraryFoldersKv.Children)
            {
                steamPaths.Add(Path.GetFullPath(Path.Join(child["path"].ToString(), "steamapps")));
            }

            foreach (var steamPath in steamPaths)
            {
                var manifests = Directory.GetFiles(steamPath, "appmanifest_*.acf");

                foreach (var appManifestPath in manifests)
                {
                    using var appManifestStream = File.OpenRead(appManifestPath);
                    var appManifestKv = kvDeserializer.Deserialize(appManifestStream, KVSerializerOptions.DefaultOptions);

                    var appId = appManifestKv["appid"].ToInt64(CultureInfo.InvariantCulture);
                    var appName = appManifestKv["name"].ToString();
                    var installDir = appManifestKv["installdir"].ToString();

                    var gamePath = Path.Combine(steamPath, "common", installDir);
                    var treeNodeName = $"{appName} ({appId}) - {gamePath}";
                    var treeNode = new TreeNode(treeNodeName)
                    {
                        Tag = gamePath,
                        Name = treeNodeName,
                        ImageKey = "_folder",
                        SelectedImageKey = "_folder",
                    };
                    var allFoundGamePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    var gameInfos = Directory.GetFiles(gamePath, "gameinfo.gi", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        MaxRecursionDepth = 10,
                    });

                    foreach (var file in gameInfos)
                    {
                        KVObject gameInfo;
                        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read);

                        try
                        {
                            gameInfo = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        var gameRoot = Path.GetDirectoryName(Path.GetDirectoryName(file));

                        foreach (var searchPath in (IEnumerable<KVObject>)gameInfo["FileSystem"]["SearchPaths"])
                        {
                            if (searchPath.Name != "Game")
                            {
                                continue;
                            }

                            var path = Path.Combine(gameRoot, searchPath.Value.ToString());

                            allFoundGamePaths.Add(path);
                        }
                    }

                    foreach (var path in allFoundGamePaths)
                    {
                        var vpks = Directory.GetFiles(path, "*.vpk", new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            MaxRecursionDepth = 5,
                        });

                        foreach (var vpk in vpks)
                        {
                            if (vpkRegex.IsMatch(vpk))
                            {
                                continue;
                            }

                            var icon = "vpk";

                            if (Path.GetFileName(vpk).StartsWith("shaders_", StringComparison.Ordinal))
                            {
                                icon = "vcs";
                            }
                            else if (vpk[path.Length..].StartsWith($"{Path.DirectorySeparatorChar}maps{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                            {
                                icon = "wrld";
                            }

                            var vpkName = vpk[(gamePath.Length + 1)..];
                            var toAdd = new TreeNode(vpkName)
                            {
                                Tag = vpk,
                                Name = vpkName,
                                ImageKey = icon,
                                SelectedImageKey = icon,
                            };

                            treeNode.Nodes.Add(toAdd);
                        }
                    }

                    if (treeNode.Nodes.Count > 0)
                    {
                        treeNode.Expand();
                        treeView.Nodes.Add(treeNode);
                    }
                }
            }
        }

        private void OnTreeViewNodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            var path = (string)e.Node.Tag;

            if (e.Node.ImageKey == "_folder")
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = path + Path.DirectorySeparatorChar,
                    UseShellExecute = true,
                    Verb = "open"
                });

                return;
            }

            Program.MainForm.OpenFile(path);
        }
    }
}
