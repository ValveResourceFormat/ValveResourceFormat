//#define DEBUG_FILE_LOAD

using System;
using System.Collections.Generic;
using System.IO;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;

namespace GUI.Utils
{
    class AdvancedGuiFileLoader : GameFileLoader, IFileLoader
    {
        private readonly Dictionary<string, Resource> CachedResources = new();
        private readonly Dictionary<string, ShaderCollection> CachedShaders = new();
        private readonly object shaderCacheLock = new();
        private readonly VrfGuiContext GuiContext;
        protected override List<string> SettingsGameSearchPaths { get; } = Settings.Config.GameSearchPaths;

        private bool GamePackagesScanned;
        private bool ShaderPackagesScanned;

        public AdvancedGuiFileLoader(VrfGuiContext guiContext) : base(guiContext.CurrentPackage, guiContext.FileName)
        {
            GuiContext = guiContext;
        }

        protected override void Dispose(bool disposing)
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

                base.Dispose(disposing);
            }
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

            (string PathOnDisk, Package Package, PackageEntry PackageEntry) = base.FindFile(file, logNotFound);
            return (PathOnDisk, null, Package, PackageEntry);
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

        public ShaderCollection LoadShader(string shaderName)
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

        public override Resource LoadFile(string file)
        {
            // TODO: Might conflict where same file name is available in different paths
            if (CachedResources.TryGetValue(file, out var resource) && resource.Reader != null)
            {
                return resource;
            }

            resource = base.LoadFile(file);
            if (resource != null)
            {
                CachedResources[file] = resource;
            }
            return resource;
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
