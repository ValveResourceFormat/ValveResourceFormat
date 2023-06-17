using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using GUI.Utils;
using ValveKeyValue;

namespace GUI.Controls
{
    public partial class ExplorerControl : UserControl
    {
        private List<(TreeNode ParentNode, TreeNode[] Children)> TreeData = new();

        public ExplorerControl()
        {
            InitializeComponent();

            try
            {
                treeView.BeginUpdate();
                treeView.ImageList = MainForm.ImageList;
                Scan();
            }
            finally
            {
                treeView.EndUpdate();
            }
        }

        private void Scan()
        {
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
                    var allFoundGamePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (!Directory.Exists(gamePath))
                    {
                        continue;
                    }

                    var gameInfos = Directory.GetFiles(gamePath, "gameinfo.gi", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        MaxRecursionDepth = 10,
                    });

                    foreach (var file in gameInfos)
                    {
                        KVObject gameInfo;

                        try
                        {
                            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read);
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

                            if (Directory.Exists(path))
                            {
                                allFoundGamePaths.Add(path);
                            }
                        }
                    }

                    var foundFiles = new List<TreeNode>();

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

                            var vpkName = vpk[(gamePath.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
                            var toAdd = new TreeNode(vpkName)
                            {
                                Tag = vpk,
                                Name = vpkName,
                                ImageKey = icon,
                                SelectedImageKey = icon,
                            };

                            foundFiles.Add(toAdd);
                        }
                    }

                    if (foundFiles.Count > 0)
                    {
                        foundFiles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                        var foundFilesArray = foundFiles.ToArray();

                        var treeNodeName = $"{appName} ({appId}) - {gamePath.Replace(Path.DirectorySeparatorChar, '/')}";
                        var treeNode = new TreeNode(treeNodeName)
                        {
                            Tag = gamePath,
                            Name = treeNodeName,
                            ImageKey = "_folder",
                            SelectedImageKey = "_folder",
                        };
                        treeNode.Nodes.AddRange(foundFilesArray);
                        treeNode.Expand();
                        treeView.Nodes.Add(treeNode);

                        TreeData.Add((treeNode, foundFilesArray));
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

        private void OnFilterTextBoxTextChanged(object sender, EventArgs e)
        {
            treeView.BeginUpdate();
            treeView.Nodes.Clear();

            var foundNodes = new List<TreeNode>(TreeData.Count);

            foreach (var node in TreeData)
            {
                node.ParentNode.Nodes.Clear();

                var foundChildren = Array.FindAll(node.Children, (child) =>
                {
                    return child.Name.Contains(filterTextBox.Text, StringComparison.OrdinalIgnoreCase);
                });

                if (foundChildren.Any())
                {
                    node.ParentNode.Nodes.AddRange(foundChildren);
                    foundNodes.Add(node.ParentNode);
                }
            }

            treeView.Nodes.AddRange(foundNodes.ToArray());
            treeView.EndUpdate();
        }
    }
}
