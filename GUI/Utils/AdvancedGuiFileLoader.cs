//#define DEBUG_FILE_LOAD

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;

namespace GUI.Utils
{
    public class AdvancedGuiFileLoader : IFileLoader, IDisposable
    {
        private static readonly Dictionary<string, Package> CachedPackages = new();
        private readonly HashSet<string> CurrentGameSearchPaths = new();
        private readonly List<Package> CurrentGamePackages = new();
        private readonly Dictionary<string, Resource> CachedResources = new();
        private readonly VrfGuiContext GuiContext;
        private readonly string[] modIdentifiers = new[] { "gameinfo.gi", "addoninfo.txt", ".addon" };
        private bool GamePackagesScanned;
        private bool ShaderPackagesScanned;
        private bool ProvidedGameInfosScanned;

        public AdvancedGuiFileLoader(VrfGuiContext guiContext)
        {
            GuiContext = guiContext;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearCache();

                foreach (var package in CachedPackages.Values)
                {
                    package.Dispose();
                }

                CachedPackages.Clear();

                foreach (var package in CurrentGamePackages)
                {
                    package.Dispose();
                }

                CurrentGamePackages.Clear();
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public void ClearCache()
        {
            foreach (var resource in CachedResources.Values)
            {
                resource.Dispose();
            }

            CachedResources.Clear();
        }

        public (VrfGuiContext Context, PackageEntry PackageEntry) FindFileWithContext(string file)
        {
            var foundFile = FindFile(file);

            if (foundFile.PackageEntry == null && foundFile.PathOnDisk == null)
            {
                return (null, null);
            }

            VrfGuiContext newContext = null;

            if (foundFile.PathOnDisk != null)
            {
                newContext = new VrfGuiContext(foundFile.PathOnDisk, null);
            }

            if (foundFile.Package != null)
            {
                var parentContext = foundFile.Context?.ParentGuiContext;
                parentContext ??= new VrfGuiContext(foundFile.Package.FileName, null)
                {
                    CurrentPackage = foundFile.Package
                };

                newContext = new VrfGuiContext(foundFile.PackageEntry.GetFullPath(), parentContext);
            }

            return (newContext, foundFile.PackageEntry);
        }

        public (string PathOnDisk, VrfGuiContext Context, Package Package, PackageEntry PackageEntry) FindFile(string file, bool logNotFound = true)
        {
            var entry = GuiContext.CurrentPackage?.FindEntry(file);

            if (entry != null)
            {
#if DEBUG_FILE_LOAD
                Console.WriteLine($"Loaded \"{file}\" from current vpk");
#endif

                return (null, GuiContext, GuiContext.CurrentPackage, entry);
            }

            if (GuiContext.ParentGuiContext != null)
            {
                return GuiContext.ParentGuiContext.FileLoader.FindFile(file);
            }

            if (!GamePackagesScanned)
            {
                GamePackagesScanned = true;
                FindAndLoadSearchPaths();
            }

            var paths = Settings.Config.GameSearchPaths.ToList();
            var packages = CurrentGamePackages.ToList();

            foreach (var searchPath in paths.Where(searchPath => searchPath.EndsWith(".vpk", StringComparison.InvariantCulture)).ToList())
            {
                paths.Remove(searchPath);

                if (!CachedPackages.TryGetValue(searchPath, out var package))
                {
                    Console.WriteLine($"Preloading vpk \"{searchPath}\"");

                    package = new Package();
                    package.Read(searchPath);
                    CachedPackages[searchPath] = package;
                }

                packages.Add(package);
            }

            foreach (var searchPath in paths.Where(searchPath => searchPath.EndsWith("gameinfo.gi", StringComparison.InvariantCulture)).ToList())
            {
                paths.Remove(searchPath);

                if (!ProvidedGameInfosScanned)
                {
                    FindAndLoadSearchPaths(searchPath);
                }
            }

            ProvidedGameInfosScanned = true;

            if (GuiContext.CurrentPackage != null && GuiContext.CurrentPackage.Entries.ContainsKey("vpk"))
            {
                foreach (var searchPath in GuiContext.CurrentPackage.Entries["vpk"])
                {
                    if (!CachedPackages.TryGetValue(searchPath.GetFileName(), out var package))
                    {
                        Console.WriteLine($"Preloading vpk \"{searchPath.GetFullPath()}\" from parent vpk");

                        var stream = GetPackageEntryStream(GuiContext.CurrentPackage, searchPath);
                        package = new Package();
                        package.SetFileName(searchPath.GetFileName());
                        package.Read(stream);
                        CachedPackages[searchPath.GetFileName()] = package;
                    }

                    packages.Add(package);
                }
            }

            foreach (var package in packages)
            {
                entry = package?.FindEntry(file);

                if (entry != null)
                {
#if DEBUG_FILE_LOAD
                    Console.WriteLine($"Loaded \"{file}\" from preloaded vpk \"{package.FileName}\"");
#endif

                    return (null, null, package, entry);
                }
            }

            var path = FindResourcePath(paths.Concat(CurrentGameSearchPaths).ToList(), file, GuiContext.FileName);

            if (path != null)
            {
                return (path, null, null, null);
            }

            if (logNotFound)
            {
                Console.Error.WriteLine($"Failed to load \"{file}\". Did you configure VPK paths in settings correctly?");
            }

            if (string.IsNullOrEmpty(file) || file == "_c")
            {
                Console.Error.WriteLine($"Empty string passed to file loader here: {Environment.StackTrace}");

#if DEBUG_FILE_LOAD
                System.Diagnostics.Debugger.Break();
#endif
            }

            return (null, null, null, null);
        }

        public ShaderCollection LoadShader(string shaderName)
        {
            if (GuiContext.ParentGuiContext != null)
            {
                return GuiContext.ParentGuiContext.FileLoader.LoadShader(shaderName);
            }

            if (!GamePackagesScanned)
            {
                GamePackagesScanned = true;
                FindAndLoadSearchPaths();
            }

            if (!ShaderPackagesScanned)
            {
                ShaderPackagesScanned = true;
                FindAndLoadShaderPackages();
            }

            var collection = new ShaderCollection();

            bool TryLoadShader(VcsProgramType programType, VcsPlatformType platformType, VcsShaderModelType modelType)
            {
                var shaderFile = new ShaderFile();
                var path = Path.Join("shaders", "vfx", ShaderUtilHelpers.ComputeVCSFileName(shaderName, programType, platformType, modelType));
                var foundFile = FindFile(path, logNotFound: false);

                if (foundFile.PathOnDisk != null)
                {
                    var stream = File.OpenRead(foundFile.PathOnDisk);
                    shaderFile.Read(path, stream);
                }
                else if (foundFile.PackageEntry != null)
                {
                    var stream = GetPackageEntryStream(foundFile.Package, foundFile.PackageEntry);
                    shaderFile.Read(path, stream);
                }

                if (shaderFile.VcsPlatformType == platformType)
                {
                    collection.Add(shaderFile);
                    return true;
                }

                return false;
            }

            var selectedPlatformType = VcsPlatformType.Undetermined;
            var selectedModelType = VcsShaderModelType.Undetermined;

            for (var platformType = VcsPlatformType.PC; platformType < VcsPlatformType.Undetermined && selectedPlatformType == VcsPlatformType.Undetermined; platformType++)
            {
                for (var modelType = VcsShaderModelType._60; modelType > VcsShaderModelType._20; modelType--)
                {
                    if (TryLoadShader(VcsProgramType.Features, platformType, modelType))
                    {
                        selectedPlatformType = platformType;
                        selectedModelType = modelType;
                        break;
                    }
                }
            }

            if (selectedPlatformType == VcsPlatformType.Undetermined)
            {
                Console.Error.WriteLine($"Failed to find shader \"{shaderName}\".");

                return collection;
            }

            for (var programType = VcsProgramType.VertexShader; programType < VcsProgramType.Undetermined; programType++)
            {
                TryLoadShader(programType, selectedPlatformType, selectedModelType);
            }

            return collection;
        }

        public Resource LoadFile(string file)
        {
            // TODO: Might conflict where same file name is available in different paths
            if (CachedResources.TryGetValue(file, out var resource) && resource.Reader != null)
            {
                return resource;
            }

            resource = new Resource
            {
                FileName = file,
            };

            var foundFile = FindFile(file);

            if (foundFile.PathOnDisk != null)
            {
                resource.Read(foundFile.PathOnDisk);
                CachedResources[file] = resource;

                return resource;
            }
            else if (foundFile.PackageEntry != null)
            {
                var stream = GetPackageEntryStream(foundFile.Package, foundFile.PackageEntry);
                resource.Read(stream);
                CachedResources[file] = resource;

                return resource;
            }

            return null;
        }

        public void AddPackageToSearch(Package package)
        {
            CurrentGamePackages.Add(package);
        }

        private static void HandleGameInfo(List<string> folders, string gameRoot, string gameinfoPath)
        {
            KVObject gameInfo;
            using (var stream = new FileStream(gameinfoPath, FileMode.Open, FileAccess.Read))
            {
                try
                {
                    gameInfo = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e);
                    return;
                }
            }

            Console.WriteLine($"Found \"{gameInfo["game"]}\" from \"{gameinfoPath}\"");

            foreach (var searchPath in (IEnumerable<KVObject>)gameInfo["FileSystem"]["SearchPaths"])
            {
                if (searchPath.Name != "Game")
                {
                    continue;
                }

                folders.Add(Path.Combine(gameRoot, searchPath.Value.ToString()));
            }
        }

        private void FindAndLoadSearchPaths(string modIdentifierPath = null)
        {
            modIdentifierPath ??= GetModIdentifierFile();

            if (modIdentifierPath == null)
            {
                return;
            }

            var folders = new List<string>();
            var rootFolder = Path.GetDirectoryName(modIdentifierPath);
            var assumedGameRoot = Path.GetDirectoryName(rootFolder);

            if (Path.GetFileName(modIdentifierPath) == "gameinfo.gi")
            {
                HandleGameInfo(folders, assumedGameRoot, modIdentifierPath);
            }
            else
            {
                var addonsSuffix = "_addons";
                if (assumedGameRoot.EndsWith(addonsSuffix, StringComparison.InvariantCultureIgnoreCase))
                {
                    var mainGameDir = assumedGameRoot[..^addonsSuffix.Length];
                    if (Directory.Exists(mainGameDir))
                    {
                        folders.Add(mainGameDir);
                    }
                }

                folders.Add(rootFolder);
            }

            foreach (var folder in folders)
            {
                // Scan for vpks in folder, same logic as in source engine
                for (var i = 1; i < 99; i++)
                {
                    var vpk = Path.Combine(folder, $"pak{i:D2}_dir.vpk");

                    if (!File.Exists(vpk))
                    {
                        break;
                    }

                    if (GuiContext.FileName == vpk)
                    {
#if DEBUG_FILE_LOAD
                        Console.WriteLine($"VPK \"{vpk}\" is the same we just opened, skipping");
#endif
                        continue;
                    }

                    if (Settings.Config.GameSearchPaths.Contains(vpk))
                    {
#if DEBUG_FILE_LOAD
                        Console.WriteLine($"VPK \"{vpk}\" is already user-defined, skipping");
#endif
                        continue;
                    }

                    Console.WriteLine($"Preloading vpk \"{vpk}\"");

                    var package = new Package();
                    package.Read(vpk);
                    CurrentGamePackages.Add(package);
                }

                if (!Settings.Config.GameSearchPaths.Contains(folder) && CurrentGameSearchPaths.Add(folder))
                {
                    Console.WriteLine($"Added folder \"{folder}\" to game search paths");
                }
            }
        }

        private string GetModIdentifierFile()
        {
            var directory = GuiContext.FileName;
            var i = 10;

            while (i-- > 0)
            {
                directory = Path.GetDirectoryName(directory);

#if DEBUG_FILE_LOAD
                Console.WriteLine($"Scanning \"{directory}\"");
#endif

                if (directory == null || Path.GetFileName(directory) == "steamapps")
                {
                    return null;
                }

                foreach (var modIdentifier in modIdentifiers)
                {
                    var path = Path.Combine(directory, modIdentifier);
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }

            return null;
        }

        private void FindAndLoadShaderPackages()
        {
            foreach (var folder in CurrentGameSearchPaths)
            {
                for (var platformType = VcsPlatformType.PC; platformType < VcsPlatformType.Undetermined; platformType++)
                {
                    var shaderName = $"shaders_{platformType.ToString().ToLowerInvariant()}_dir.vpk";
                    var vpk = Path.Combine(folder, shaderName);

                    if (File.Exists(vpk))
                    {
                        Console.WriteLine($"Preloading vpk \"{vpk}\"");

                        var package = new Package();
                        package.Read(vpk);
                        CurrentGamePackages.Add(package);

                        break; // One for each folder
                    }
                }
            }
        }

        private static string FindResourcePath(IList<string> paths, string file, string currentFullPath = null)
        {
            if (currentFullPath != null)
            {
                paths = paths.OrderByDescending(x => currentFullPath.StartsWith(x, StringComparison.Ordinal)).ToList();
            }

            foreach (var searchPath in paths)
            {
                var path = Path.Combine(searchPath, file);
                path = Path.GetFullPath(path);

                if (File.Exists(path))
                {
#if DEBUG_FILE_LOAD
                    Console.WriteLine($"Loaded \"{file}\" from disk: \"{path}\"");
#endif

                    return path;
                }
            }

            return null;
        }

        public static Stream GetPackageEntryStream(Package package, PackageEntry entry)
        {
            // Files in a vpk that isn't split
            if (!package.IsDirVPK || entry.ArchiveIndex == 32767 || entry.SmallData.Length > 0)
            {
                package.ReadEntry(entry, out var output, false);
                return new MemoryStream(output);
            }

            var path = $"{package.FileName}_{entry.ArchiveIndex:D3}.vpk";
            var stream = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            return stream.CreateViewStream(entry.Offset, entry.Length, MemoryMappedFileAccess.Read);
        }
    }
}
