using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Exporter
{
    /// <summary>
    /// Decompiles materials so they can be dropped into an addon and recompiled as is. Unlike the built-in
    /// "Decompile &amp;&amp; export", every texture the .vmat references is written next to it and referenced by its
    /// addon relative path instead of the game path it was compiled from, and cubemaps are written as a single
    /// cross image instead of one image per face.
    /// </summary>
    static class CustomVmatExporter
    {
        public const string MaterialTypeName = "vmat_c";

        // Names the built-in export gives each cubemap face image
        private static readonly string[] CubemapFaceSuffixes = ["rt", "lf", "bk", "ft", "up", "dn"];
        private static readonly string[] CubemapFaceExtensions = [".png", ".exr"];

        public static async Task ExtractSelection(object sender)
        {
            if (sender is not ToolStripMenuItem { Owner: ContextMenuStrip { SourceControl: var owner } })
            {
                throw new InvalidDataException("Invalid context menu structure");
            }

            var context = owner switch
            {
                BetterTreeView tree => tree.VrfGuiContext,
                BetterListView listView => listView.VrfGuiContext,
                _ => throw new InvalidDataException("Unknown state"),
            };

            if (context == null)
            {
                return;
            }

            if (context.CurrentPackage == null)
            {
                Log.Error(nameof(CustomVmatExporter), "CurrentPackage is null, cannot extract files");
                return;
            }

            var selectedItems = ContextMenuSelection.GetSelectedItems(owner);
            var entries = ContextMenuSelection.CollectFiles(selectedItems, MaterialTypeName);

            if (entries.Count == 0)
            {
                await AppMessageDialogs.ShowMessageAsync(
                    "The selection does not contain any .vmat_c material files.",
                    "Nothing to export",
                    MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            List<(PackageEntry Entry, string OutputPath)> targets;
            string outputFolder;

            // One file is saved exactly where the user picks, with its textures in the same folder. A batch keeps
            // each material's path from the package under the picked folder so materials do not collide.
            if (selectedItems is [{ IsFolder: false }] && entries.Count == 1)
            {
                var pickedFileName = AppFileDialogs.SaveFile(
                    "Choose where to save the material",
                    Path.GetFileNameWithoutExtension(entries[0].GetFileName()),
                    "vmat",
                    "vmat file|*.vmat");

                if (pickedFileName == null)
                {
                    return;
                }

                outputFolder = Path.GetDirectoryName(pickedFileName) ?? string.Empty;
                targets = [(entries[0], pickedFileName)];
            }
            else
            {
                var pickedFolder = AppFileDialogs.PickFolder(
                    "Choose which folder to export the materials to",
                    AppFileDialogs.RememberIn.SaveDirectory);

                if (pickedFolder == null)
                {
                    return;
                }

                outputFolder = pickedFolder;
                targets = [.. entries.Select(entry => (entry, Path.Combine(
                    pickedFolder,
                    Path.ChangeExtension(entry.GetFullPath(), "vmat").Replace('/', Path.DirectorySeparatorChar))))];
            }

            var contentRoot = FindContentRoot(outputFolder) ?? AppFileDialogs.PickFolder(
                "The export folder is not inside an addon content folder (content/<game>_addons/<addon>). " +
                "Choose the folder that texture paths in the .vmat should be relative to");

            if (contentRoot == null)
            {
                return;
            }

            if (GetContentRelativePath(contentRoot, outputFolder) == null)
            {
                await AppMessageDialogs.ShowMessageAsync(
                    $"The export folder \"{outputFolder}\" is not inside \"{contentRoot}\", so the material could not reference its textures from there.",
                    "Invalid content folder",
                    MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            RunInDialog(context, targets, contentRoot, out var workCompletion);
            await workCompletion.ConfigureAwait(true);
        }

        /// <summary>
        /// Finds the folder that asset paths are relative to for anything saved in <paramref name="directory"/>:
        /// the addon folder in <c>content/&lt;game&gt;_addons/&lt;addon&gt;</c>, or the mod folder in <c>content/&lt;mod&gt;</c>.
        /// </summary>
        internal static string? FindContentRoot(string directory)
        {
            var start = new DirectoryInfo(directory);

            for (var folder = start; folder != null; folder = folder.Parent)
            {
                if (folder.Parent?.Name.EndsWith("_addons", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return folder.FullName;
                }
            }

            for (var folder = start; folder != null; folder = folder.Parent)
            {
                if (folder.Parent?.Name.Equals("content", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return folder.FullName;
                }
            }

            return null;
        }

        /// <summary>
        /// Path of <paramref name="path"/> relative to <paramref name="contentRoot"/> with forward slashes, empty when they
        /// are the same folder, or null when it lies outside of it.
        /// </summary>
        internal static string? GetContentRelativePath(string contentRoot, string path)
        {
            var relative = Path.GetRelativePath(contentRoot, path);

            if (relative == ".")
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return null;
            }

            return relative.Replace(Path.DirectorySeparatorChar, '/');
        }

        private static void RunInDialog(VrfGuiContext context, List<(PackageEntry Entry, string OutputPath)> targets, string contentRoot, out Task workCompletion)
        {
            using var dialog = new CustomVmdlExtractProgressForm
            {
                StayOpenOnCompletion = true,
                Text = "Exporting materials...",
            };

            dialog.AppendLine($"Exporting {targets.Count} material{(targets.Count == 1 ? "" : "s")}, texture paths relative to \"{contentRoot}\"");

            dialog.OnProcess = cancellationToken =>
            {
                Export(context, targets, contentRoot, dialog, cancellationToken);
                return Task.CompletedTask;
            };

            dialog.ShowDialog();

            workCompletion = dialog.WorkCompletion;
        }

        private static void Export(VrfGuiContext context, List<(PackageEntry Entry, string OutputPath)> targets, string contentRoot,
            CustomVmdlExtractProgressForm progress, CancellationToken cancellationToken)
        {
            var package = context.CurrentPackage ?? throw new InvalidOperationException("CurrentPackage is null");

            Log.Info(nameof(CustomVmatExporter), $"Custom VMAT export started, content root \"{contentRoot}\"");

            // Materials exported together often share textures, only write those once
            var writtenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var processed = 0;
            var failed = 0;

            foreach (var (entry, outputPath) in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var materialPath = entry.GetFullPath();
                progress.AppendLine($"[{++processed}/{targets.Count}] {materialPath} -> {outputPath}");

                try
                {
                    ExportMaterial(package, entry, outputPath, contentRoot, context, progress, writtenFiles, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    failed++;
                    progress.AppendLine($"  FAILED: {e.Message}");
                    Log.Error(nameof(CustomVmatExporter), $"Failed to export '{materialPath}': {e}");
                }
            }

            var completedText = $"Export completed in {GenericProgressForm.FormatTime(progress.Elapsed)}: {targets.Count - failed} succeeded, {writtenFiles.Count} textures written";

            if (failed > 0)
            {
                completedText += $", {failed} material{(failed == 1 ? "" : "s")} failed";
            }

            Log.Info(nameof(CustomVmatExporter), completedText);
            progress.AppendLine(completedText);
        }

        private static void ExportMaterial(Package package, PackageEntry entry, string vmatPath, string contentRoot, IFileLoader fileLoader,
            IProgress<string> progress, HashSet<string> writtenFiles, CancellationToken cancellationToken)
        {
            using var resource = new Resource
            {
                FileName = entry.GetFullPath(),
            };
            resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

            ExportMaterial(resource, vmatPath, contentRoot, fileLoader, progress, writtenFiles, cancellationToken);
        }

        /// <summary>
        /// Writes <paramref name="resource"/> to <paramref name="vmatPath"/> with its textures next to it, referenced
        /// relative to <paramref name="contentRoot"/>.
        /// </summary>
        /// <param name="writtenFiles">Files already written in this export, which are not written again.</param>
        internal static void ExportMaterial(Resource resource, string vmatPath, string contentRoot, IFileLoader fileLoader,
            IProgress<string> progress, HashSet<string> writtenFiles, CancellationToken cancellationToken)
        {
            if (resource.DataBlock is not Material material)
            {
                throw new InvalidDataException("File is not a material");
            }

            var outputFolder = Path.GetDirectoryName(Path.GetFullPath(vmatPath)) ?? throw new InvalidOperationException($"\"{vmatPath}\" has no folder");
            var addonFolder = GetContentRelativePath(contentRoot, outputFolder) ?? throw new InvalidOperationException($"\"{outputFolder}\" is not inside \"{contentRoot}\"");

            Directory.CreateDirectory(outputFolder);

            var extract = new MaterialExtract(resource, fileLoader);

            // Texture paths as the decompiled .vmat names them, pointing into the game, to where they were written
            var addonPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            var textures = new Dictionary<string, Resource?>(StringComparer.OrdinalIgnoreCase);

            string ToAddonPath(string fileName) => addonFolder.Length == 0 ? fileName : $"{addonFolder}/{fileName}";

            void WriteOnce(string fileName, Func<byte[]> encode)
            {
                var filePath = Path.Combine(outputFolder, fileName);

                if (!writtenFiles.Add(filePath))
                {
                    return;
                }

                var data = encode();

                if (data.Length == 0)
                {
                    throw new InvalidDataException($"Failed to encode \"{fileName}\"");
                }

                File.WriteAllBytes(filePath, data);
            }

            try
            {
                foreach (var (textureType, texturePath) in material.TextureParams)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!textures.TryGetValue(texturePath, out var textureResource))
                    {
                        textureResource = fileLoader.LoadFileCompiled(texturePath);
                        textures.Add(texturePath, textureResource);
                    }

                    if (textureResource?.DataBlock is not Texture texture)
                    {
                        progress.Report($"  ! {textureType}: \"{texturePath}\" was not found, it keeps pointing at the game");
                        continue;
                    }

                    // Same unpacking the .vmat is written with; default textures are left out and keep their materials/default path
                    var maps = extract.GetTextureUnpackInfos(textureType, texturePath, texture, omitDefaults: true, omitUniforms: true).ToList();

                    if (maps.Count == 0)
                    {
                        continue;
                    }

                    if ((texture.Flags & VTexFlags.CUBE_TEXTURE) != 0)
                    {
                        if (!CubemapCrossLayout.CanCreate(texture))
                        {
                            progress.Report($"  ! {textureType}: \"{texturePath}\" is a cubemap array, which cannot be written as a single image");
                            continue;
                        }

                        var cubemapFileName = Path.ChangeExtension(Path.GetFileName(maps[0].FileName), ".png");

                        WriteOnce(cubemapFileName, () =>
                        {
                            using var cross = CubemapCrossLayout.Create(texture);
                            return TextureExtract.ToPngImage(cross);
                        });

                        foreach (var map in maps)
                        {
                            addonPaths[map.FileName] = ToAddonPath(cubemapFileName);
                            progress.Report($"  {map.TextureType} -> {ToAddonPath(cubemapFileName)} (cubemap cross)");
                        }

                        RemoveLeftoverCubemapFaces(outputFolder, texturePath, progress);
                        continue;
                    }

                    if (texture.Depth > 1)
                    {
                        progress.Report($"  ! {textureType}: \"{texturePath}\" is a texture array, which cannot be written as a single image");
                        continue;
                    }

                    if (texture.IsHighDynamicRange)
                    {
                        // Channels are not unpacked from HDR textures, the whole image is the first map
                        var hdrFileName = Path.GetFileName(maps[0].FileName);

                        WriteOnce(hdrFileName, () =>
                        {
                            using var bitmap = texture.GenerateBitmap();
                            return TextureExtract.ToExrImage(bitmap);
                        });

                        foreach (var map in maps)
                        {
                            addonPaths[map.FileName] = ToAddonPath(hdrFileName);
                            progress.Report($"  {map.TextureType} -> {ToAddonPath(hdrFileName)}");
                        }

                        continue;
                    }

                    using var content = new TextureExtract(textureResource).ToMaterialMaps(maps);

                    foreach (var subFile in content.SubFiles)
                    {
                        if (subFile.Extract != null)
                        {
                            WriteOnce(subFile.FileName, subFile.Extract);
                        }
                    }

                    foreach (var map in maps)
                    {
                        var fileName = Path.GetFileName(map.FileName);
                        addonPaths[map.FileName] = ToAddonPath(fileName);
                        progress.Report($"  {map.TextureType} -> {ToAddonPath(fileName)}");
                    }
                }
            }
            finally
            {
                foreach (var texture in textures.Values)
                {
                    texture?.Dispose();
                }
            }

            var vmat = extract.ToValveMaterial();

            foreach (var (gamePath, addonPath) in addonPaths)
            {
                vmat = vmat.Replace($"\"{gamePath}\"", $"\"{addonPath}\"", StringComparison.Ordinal);
            }

            File.WriteAllText(vmatPath, vmat);
        }

        /// <summary>
        /// Deletes the per face images a built-in export of this cubemap left in the folder, now replaced by the cross.
        /// </summary>
        private static void RemoveLeftoverCubemapFaces(string outputFolder, string texturePath, IProgress<string> progress)
        {
            var baseName = Path.GetFileNameWithoutExtension(texturePath);

            foreach (var suffix in CubemapFaceSuffixes)
            {
                foreach (var extension in CubemapFaceExtensions)
                {
                    var facePath = Path.Combine(outputFolder, $"{baseName}_{suffix}{extension}");

                    if (File.Exists(facePath))
                    {
                        File.Delete(facePath);
                        progress.Report($"  - removed leftover face {Path.GetFileName(facePath)}");
                    }
                }
            }
        }
    }
}
