using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SteamDatabase.ValvePak;
using ValvePackage = SteamDatabase.ValvePak.Package;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Reflection;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GUI2.Viewers
{
    public class PackageNode
    {
        public PackageEntry File { get; set; }
        private string extension;
        public string Extension
        {
            get { return extension; }
            set
            {
                extension = "ms-appx:///Assets/AssetTypes/" + value + ".png";
            }
        }
        public bool IsFile => File != null;

        public string DisplayName { get; set; }

        public List<PackageNode> Children { get; init; } = new List<PackageNode>();
    }
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class Package : Page
    {
        private Dictionary<string, PackageNode> folderNodes = new();

        public Dictionary<string, string> ExtensionIconList { get; private set; }

        public static bool IsAccepted(uint magic, ushort magicResourceVersion)
        {
            return magic == ValvePackage.MAGIC;
        }

        public Package()
        {
            this.InitializeComponent();
        }


        public void GenerateIconList(IEnumerable<string> extensions)
        {
            ExtensionIconList = new Dictionary<string, string>();

            foreach (var originalExtension in extensions)
            {
                var extension = originalExtension;

                if (extension.EndsWith("_c", StringComparison.Ordinal))
                {
                    extension = extension[0..^2];
                }

                const string prefix = "GUI2.Assets.AssetTypes.";
                var extensionFiles = Assembly.GetEntryAssembly()
                    .GetManifestResourceNames()
                    .Where(file => file.StartsWith(prefix))
                    .Select(file => file.Split(".")[3])
                    .ToList();
                if (!extensionFiles.Contains(extension))
                {
                    if (extension.Length > 0 && extension[0] == 'v')
                    {
                        extension = extension[1..];

                        if (!extensionFiles.Contains(extension))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                ExtensionIconList.Add(originalExtension, extension);
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is VrfGuiContext ctx)
            {
                var package = new ValvePackage();
                package.SetFileName(ctx.File.Name);
                package.Read(new MemoryStream(ctx.FileBytes));

                var rootNode = new PackageNode() { Extension = "vpk", DisplayName = "root" };

                tree.ItemsSource = new List<PackageNode>() { rootNode };

                GenerateIconList(package.Entries.Keys);


                foreach (var filetype in package.Entries)
                {
                    foreach (var file in filetype.Value)
                    {
                        AddFileNode(file, rootNode);
                    }
                }
            }
            else
            {
                throw new NotSupportedException("ByteViewer only supports navigation with Context");
            }
            base.OnNavigatedTo(e);
        }

        private void AddFileNode(PackageEntry file, PackageNode root)
        {
            var folderNode = root;
            if (!string.IsNullOrWhiteSpace(file.DirectoryName))
            {
                var folderName = file.DirectoryName;
                do
                {
                    if (!folderNodes.TryGetValue(folderName, out folderNode))
                    {
                        var i = folderName.LastIndexOf(ValvePackage.DirectorySeparatorChar);
                        if (i == -1)
                        {
                            // We have hit rock bottom, and need to create the entire path
                            folderNode = root;
                            folderName = "";
                        }
                        else
                        {
                            folderName = folderName[..i];
                        }
                    }
                    if (folderName != file.DirectoryName && folderNode != null)
                    {
                        var offset = string.IsNullOrEmpty(folderName) ? 0 : folderName.Length + 1;
                        var directoryPath = file.DirectoryName.Substring(offset).Split(ValvePackage.DirectorySeparatorChar);
                        foreach (var folder in directoryPath)
                        {
                            var node = new PackageNode() { Extension = "_folder", DisplayName = folder };
                            folderNode.Children.Add(node);
                            // on the root case, don't bother
                            if (folderName.Length > 0)
                            {
                                folderName = string.Join(ValvePackage.DirectorySeparatorChar, folderName, folder);
                            }
                            else
                            {
                                folderName = folder;
                            }
                            folderNodes[folderName] = node;
                            folderNode = node;
                        }
                    }

                }
                while (folderNode == null);
            }
            if (!ExtensionIconList.TryGetValue(file.TypeName, out var ext))
            {
                ext = "_default";
            }
            folderNode.Children.Add(new PackageNode { File = file, Extension = ext, DisplayName = file.GetFileName() });
        }
    }
}
