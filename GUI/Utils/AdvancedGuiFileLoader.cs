//#define DEBUG_FILE_LOAD

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.IO.MemoryMappedFiles;
using System.Linq;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;

namespace GUI.Utils
{
    class AdvancedGuiFileLoader : GameFileLoader, IFileLoader, IDisposable
    {
        private static readonly Dictionary<string, Package> CachedPackages = new();
        private readonly Dictionary<string, Resource> CachedResources = new();
        private readonly Dictionary<string, ShaderCollection> CachedShaders = new();
        private readonly object shaderCacheLock = new();
        private readonly VrfGuiContext GuiContext;
        protected override List<string> SettingsGameSearchPaths { get; } = Settings.Config.GameSearchPaths;

        private bool GamePackagesScanned;
        private bool ShaderPackagesScanned;
        private bool ProvidedGameInfosScanned;

        public AdvancedGuiFileLoader(VrfGuiContext guiContext) : base(guiContext.CurrentPackage, guiContext.FileName)
        {
            GuiContext = guiContext;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearCache();

                lock (shaderCacheLock)
                {
                    foreach (var shader in CachedShaders.Values)
                    {
                        shader.Dispose();
                    }
                }

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

        public new (string PathOnDisk, VrfGuiContext Context, Package Package, PackageEntry PackageEntry) FindFile(string file, bool logNotFound = true)
        {
            var entry = GuiContext.CurrentPackage?.FindEntry(file);

            if (entry != null)
            {
#if DEBUG_FILE_LOAD
                Log.Debug(nameof(AdvancedGuiFileLoader), $"Loaded \"{file}\" from current vpk");
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
                    Log.Info(nameof(AdvancedGuiFileLoader), $"Preloading vpk \"{searchPath}\"");

                    package = new Package();
                    package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
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

            if (GuiContext.CurrentPackage != null && GuiContext.CurrentPackage.Entries.TryGetValue("vpk", out var vpkEntries))
            {
                foreach (var searchPath in vpkEntries)
                {
                    if (!CachedPackages.TryGetValue(searchPath.GetFileName(), out var package))
                    {
                        Log.Info(nameof(AdvancedGuiFileLoader), $"Preloading vpk \"{searchPath.GetFullPath()}\" from parent vpk");

                        var stream = GetPackageEntryStream(GuiContext.CurrentPackage, searchPath);
                        package = new Package();
                        package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
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
                    Log.Debug(nameof(AdvancedGuiFileLoader), $"Loaded \"{file}\" from preloaded vpk \"{package.FileName}\"");
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
                Log.Error(nameof(AdvancedGuiFileLoader), $"Failed to load \"{file}\". Did you configure VPK paths in settings correctly?");
            }

            if (string.IsNullOrEmpty(file) || file == "_c")
            {
                Log.Debug(nameof(AdvancedGuiFileLoader), $"Empty string passed to file loader here: {Environment.StackTrace}");

#if DEBUG_FILE_LOAD
                System.Diagnostics.Debugger.Break();
#endif
            }

            return (null, null, null, null);
        }

        private ShaderCollection LoadShaderFromDisk(string shaderName)
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

            for (var platformType = (VcsPlatformType)0; platformType < VcsPlatformType.Undetermined && selectedPlatformType == VcsPlatformType.Undetermined; platformType++)
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
                Log.Error(nameof(AdvancedGuiFileLoader), $"Failed to find shader \"{shaderName}\".");

                return collection;
            }

            for (var programType = VcsProgramType.VertexShader; programType < VcsProgramType.Undetermined; programType++)
            {
                TryLoadShader(programType, selectedPlatformType, selectedModelType);
            }

            return collection;
        }

        public new ShaderCollection LoadShader(string shaderName)
        {
            lock (shaderCacheLock)
            {
                if (CachedShaders.TryGetValue(shaderName, out var shader))
                {
                    return shader;
                }

                shader = LoadShaderFromDisk(shaderName);
                CachedShaders.Add(shaderName, shader);
                return shader;
            }
        }

        public new Resource LoadFile(string file)
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

        private void FindAndLoadShaderPackages()
        {
            foreach (var folder in CurrentGameSearchPaths)
            {
                for (var platformType = (VcsPlatformType)0; platformType < VcsPlatformType.Undetermined; platformType++)
                {
                    var shaderName = $"shaders_{platformType.ToString().ToLowerInvariant()}_dir.vpk";
                    var vpk = Path.Combine(folder, shaderName);

                    if (File.Exists(vpk))
                    {
                        Log.Info(nameof(AdvancedGuiFileLoader), $"Preloading vpk \"{vpk}\"");

                        var package = new Package();
                        package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                        package.Read(vpk);
                        CurrentGamePackages.Add(package);

                        break; // One for each folder
                    }
                }
            }
        }
    }
}
