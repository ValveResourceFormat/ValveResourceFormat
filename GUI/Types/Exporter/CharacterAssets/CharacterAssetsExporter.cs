using System.Diagnostics;
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
using VmdlExtractor;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// Exports a Dota 2 hero wearing a chosen set of cosmetic items, as read from the package's items_game.txt, into a
    /// folder laid out like the package. Models and materials go through the custom exporters so they recompile as is,
    /// everything else through the built-in decompiler.
    /// </summary>
    static class CharacterAssetsExporter
    {
        // Suffixes the compiler appends to an image's name when compiling it to a texture
        private static readonly string[] ImageSuffixes = ["_png", "_jpg", "_jpeg", "_webp", "_tga", "_psd"];

        public static bool CanExport(Control? owner)
            => GetContext(owner)?.CurrentPackage is { } package && ItemsGameCatalog.IsAvailable(package);

        public static async Task ExportFromContextMenu(object sender)
        {
            if (sender is not ToolStripMenuItem { Owner: ContextMenuStrip { SourceControl: var owner } })
            {
                throw new InvalidDataException("Invalid context menu structure");
            }

            var context = GetContext(owner);

            if (context?.CurrentPackage == null)
            {
                return;
            }

            // Package.FileName is the base name used to find the archive parts, the path it was opened from is the context's
            var vpkPath = context.FileName;

            if (!File.Exists(vpkPath))
            {
                await AppMessageDialogs.ShowMessageAsync(
                    "Character assets can only be exported from a package opened from disk.",
                    "Cannot export character assets",
                    MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            // Opened fresh so reading it on worker threads does not race the package viewer
            using var package = new Package();
            package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
            package.Read(vpkPath);

            var catalog = LoadCatalog(package, out var loadCompletion);
            await loadCompletion.ConfigureAwait(true);

            if (catalog == null)
            {
                return;
            }

            if (catalog.Heroes.Count == 0)
            {
                await AppMessageDialogs.ShowMessageAsync(
                    "No heroes were found in the package's hero scripts.",
                    "Cannot export character assets",
                    MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            CharacterLoadout loadout;
            CharacterExportOptions options;

            using (var form = new CharacterSelectForm(catalog, context))
            {
                if (await form.ShowDialogAsync().ConfigureAwait(true) != DialogResult.OK || form.SelectedHero == null)
                {
                    return;
                }

                loadout = form.CreateLoadout();
                options = form.Options;
            }

            var outputRoot = AppFileDialogs.PickFolder(
                "Choose the folder to export the character to, files are laid out like the package (e.g. content/dota_addons/<addon>)",
                AppFileDialogs.RememberIn.SaveDirectory);

            if (outputRoot == null)
            {
                return;
            }

            RunInDialog(vpkPath, package, loadout, options, outputRoot, out var workCompletion);
            await workCompletion.ConfigureAwait(true);
        }

        private static VrfGuiContext? GetContext(Control? owner) => owner switch
        {
            BetterTreeView tree => tree.VrfGuiContext,
            BetterListView listView => listView.VrfGuiContext,
            _ => null,
        };

        /// <summary>
        /// Reads the catalog behind a progress dialog. Returns null when cancelled or failed, which the dialog reports.
        /// </summary>
        private static ItemsGameCatalog? LoadCatalog(Package package, out Task workCompletion)
        {
            ItemsGameCatalog? catalog = null;

            using var dialog = new GenericProgressForm { Text = "Reading items_game.txt..." };

            dialog.OnProcess = cancellationToken =>
            {
                catalog = ItemsGameCatalog.Load(package, dialog, cancellationToken);
                return Task.CompletedTask;
            };

            dialog.ShowDialog();

            workCompletion = dialog.WorkCompletion;

            // The dialog closes once the work is done, or right away when cancelled, before the work gets to set this
            return catalog;
        }

        private static void RunInDialog(string vpkPath, Package package, CharacterLoadout loadout, CharacterExportOptions options,
            string outputRoot, out Task workCompletion)
        {
            var hero = loadout.Hero;
            var items = loadout.Items;

            using var dialog = new CustomVmdlExtractProgressForm
            {
                StayOpenOnCompletion = true,
                Text = $"Exporting {hero.DisplayName}...",
            };

            dialog.AppendLine($"Exporting {hero.DisplayName} with {items.Count} item{(items.Count == 1 ? "" : "s")} to \"{outputRoot}\"");

            foreach (var item in items)
            {
                dialog.AppendLine($"  [{item.Item.Slot}] {item.Item.Name}{(item.StyleName != null ? $" ({item.StyleName})" : string.Empty)}");
            }

            dialog.OnProcess = cancellationToken =>
            {
                Export(vpkPath, package, loadout, options, outputRoot, dialog, cancellationToken);
                return Task.CompletedTask;
            };

            dialog.ShowDialog();

            workCompletion = dialog.WorkCompletion;
        }

        /// <summary>
        /// Collects everything the hero and items need and writes it under <paramref name="outputRoot"/>.
        /// </summary>
        internal static void Export(string vpkPath, Package package, CharacterLoadout loadout, CharacterExportOptions options,
            string outputRoot, IProgress<string> progress, CancellationToken cancellationToken)
        {
            var startTimestamp = Stopwatch.GetTimestamp();

            Log.Info(nameof(CharacterAssetsExporter), $"Character export of {loadout.Hero.Name} started to \"{outputRoot}\"");

            // The model extractor reports through the console
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdOutWriter = new CustomVmdlExporter.LogTextWriter(isError: false, progress);
            using var stdErrWriter = new CustomVmdlExporter.LogTextWriter(isError: true, progress);
            Console.SetOut(stdOutWriter);
            Console.SetError(stdErrWriter);

            try
            {
                using var fileLoader = new GameFileLoader(package, vpkPath);

                progress.Report("Collecting dependencies...");

                var plan = new CharacterDependencyCollector(package, fileLoader, progress).Collect(loadout, options, cancellationToken);

                progress.Report($"Found {plan.Models.Count} models, {plan.Materials.Count} materials, {plan.Resources.Count} other resources and {plan.RawFiles.Count} other files");

                foreach (var missing in plan.Missing)
                {
                    progress.Report($"  ! not found: {missing}");
                }

                var writtenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var failed = 0;

                failed += ExportModels(plan.Models, vpkPath, outputRoot, fileLoader, progress, cancellationToken);
                failed += ExportMaterials(plan.Materials, outputRoot, fileLoader, progress, writtenFiles, cancellationToken);
                failed += ExportResources(plan.Resources, outputRoot, fileLoader, progress, writtenFiles, cancellationToken);
                failed += ExportRawFiles(plan.RawFiles, outputRoot, fileLoader, progress, writtenFiles, cancellationToken);
                failed += ApplyReplacements(plan, outputRoot, fileLoader, progress);

                var completedText = $"Export completed in {GenericProgressForm.FormatTime(Stopwatch.GetElapsedTime(startTimestamp))}";

                if (failed > 0)
                {
                    completedText += $", {failed} file{(failed == 1 ? "" : "s")} failed";
                }

                if (plan.Missing.Count > 0)
                {
                    completedText += $", {plan.Missing.Count} reference{(plan.Missing.Count == 1 ? " was" : "s were")} not found";
                }

                Log.Info(nameof(CharacterAssetsExporter), completedText);
                progress.Report(completedText);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        private static int ExportModels(List<string> models, string vpkPath, string outputRoot, GameFileLoader fileLoader,
            IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (models.Count == 0)
            {
                return 0;
            }

            progress.Report($"Exporting {models.Count} models with the custom VMDL extractor...");

            var targets = new List<(string RelativePath, Resource Resource)>(models.Count);
            var failed = 0;

            try
            {
                foreach (var model in models)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var resource = fileLoader.LoadFile(model);

                    if (resource == null)
                    {
                        failed++;
                        progress.Report($"  FAILED {model}: could not be read");
                        continue;
                    }

                    targets.Add((model, resource));
                }

                var result = CustomModelExporter.ExtractModels(targets, outputRoot, fileLoader, new CustomModelExportOptions
                {
                    DonorVpkPath = vpkPath,
                });

                foreach (var failure in result.Failures)
                {
                    progress.Report($"  FAILED {failure.File}: {failure.Error}");
                    Log.Error(nameof(CharacterAssetsExporter), $"{failure.File}: {failure.Error}");
                }

                progress.Report($"  {result.Succeeded} models exported");

                return failed + result.Failures.Count;
            }
            finally
            {
                foreach (var (_, resource) in targets)
                {
                    resource.Dispose();
                }
            }
        }

        /// <summary>
        /// Writes the exported sources of the equipped models and particles over the default ones they stand in for,
        /// see <see cref="CharacterExportOptions.ReplaceDefaults"/>.
        /// </summary>
        private static int ApplyReplacements(CharacterExportPlan plan, string outputRoot, IFileLoader fileLoader, IProgress<string> progress)
        {
            if (plan.ModelReplacements.Count == 0 && plan.ParticleReplacements.Count == 0 && plan.SkippedSharedParticles.Count == 0 && plan.UnplacedModels.Count == 0)
            {
                return 0;
            }

            progress.Report("Replacing the hero's default assets...");

            var failed = 0;

            foreach (var replacement in plan.ModelReplacements)
            {
                try
                {
                    var vmdl = File.ReadAllText(GetExportedPath(outputRoot, replacement.Source, "vmdl"));
                    var details = new List<string>();

                    if (replacement.Skin != 0)
                    {
                        try
                        {
                            vmdl = ModelDocEditor.MakeMaterialGroupDefault(vmdl, replacement.Skin);
                            details.Add($"skin {replacement.Skin} as default");
                        }
                        catch (Exception e)
                        {
                            progress.Report($"  ! {replacement.Target}: skin {replacement.Skin} was not made the default: {e.Message}");
                        }
                    }

                    if (replacement.Particles.Count > 0)
                    {
                        try
                        {
                            var particles = replacement.Particles.Select(particle => ModelDocEditor.ResolveParticle(fileLoader, particle)).ToList();
                            vmdl = ModelDocEditor.AddParticles(vmdl, particles);

                            foreach (var particle in particles)
                            {
                                var attachment = particle.AttachmentPoint.Length > 0 ? particle.AttachmentPoint : "origin";
                                details.Add($"creates {particle.Name} on {attachment}");
                            }
                        }
                        catch (Exception e)
                        {
                            progress.Report($"  ! {replacement.Target}: particles were not added: {e.Message}");
                        }
                    }

                    var targetPath = GetOutputPath(outputRoot, Path.ChangeExtension(replacement.Target, "vmdl"));
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.WriteAllText(targetPath, vmdl);

                    progress.Report($"  {replacement.Target} <- {replacement.Source}{(details.Count > 0 ? $" ({string.Join(", ", details)})" : string.Empty)}");
                }
                catch (Exception e)
                {
                    failed++;
                    progress.Report($"  FAILED {replacement.Target} <- {replacement.Source}: {e.Message}");
                    Log.Error(nameof(CharacterAssetsExporter), $"Failed to replace '{replacement.Target}': {e}");
                }
            }

            foreach (var replacement in plan.ParticleReplacements)
            {
                try
                {
                    var targetPath = GetOutputPath(outputRoot, replacement.Target);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Copy(GetExportedPath(outputRoot, replacement.Source, "vpcf"), targetPath, overwrite: true);

                    progress.Report($"  {replacement.Target} <- {replacement.Source}");
                }
                catch (Exception e)
                {
                    failed++;
                    progress.Report($"  FAILED {replacement.Target} <- {replacement.Source}: {e.Message}");
                    Log.Error(nameof(CharacterAssetsExporter), $"Failed to replace '{replacement.Target}': {e}");
                }
            }

            foreach (var model in plan.UnplacedModels)
            {
                progress.Report($"  - {model} was exported, but its slot has no default model to write it over");
            }

            foreach (var skipped in plan.SkippedSharedParticles)
            {
                progress.Report($"  - left {skipped.Target} alone, every hero uses it (it would become {skipped.Source})");
            }

            return failed;
        }

        /// <summary>
        /// Where an asset exported earlier in this export was written, failing when it was not.
        /// </summary>
        private static string GetExportedPath(string outputRoot, string sourcePath, string extension)
        {
            var path = GetOutputPath(outputRoot, Path.ChangeExtension(sourcePath, extension));

            return File.Exists(path) ? path : throw new FileNotFoundException($"\"{Path.ChangeExtension(sourcePath, extension)}\" was not exported");
        }

        private static int ExportMaterials(List<string> materials, string outputRoot, GameFileLoader fileLoader,
            IProgress<string> progress, HashSet<string> writtenFiles, CancellationToken cancellationToken)
        {
            if (materials.Count == 0)
            {
                return 0;
            }

            progress.Report($"Exporting {materials.Count} materials with the custom VMAT exporter...");

            var failed = 0;

            foreach (var material in materials)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var resource = fileLoader.LoadFile(material) ?? throw new FileNotFoundException("Could not be read");

                    var vmatPath = GetOutputPath(outputRoot, Path.ChangeExtension(StripCompiledSuffix(material), "vmat"));
                    progress.Report(material);

                    // The export folder is laid out like the package, so texture paths are relative to its root
                    CustomVmatExporter.ExportMaterial(resource, vmatPath, outputRoot, fileLoader, progress, writtenFiles, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    failed++;
                    progress.Report($"  FAILED {material}: {e.Message}");
                    Log.Error(nameof(CharacterAssetsExporter), $"Failed to export '{material}': {e}");
                }
            }

            return failed;
        }

        private static int ExportResources(List<string> resources, string outputRoot, GameFileLoader fileLoader,
            IProgress<string> progress, HashSet<string> writtenFiles, CancellationToken cancellationToken)
        {
            if (resources.Count == 0)
            {
                return 0;
            }

            progress.Report($"Decompiling {resources.Count} particles, sounds, textures and other resources...");

            var failed = 0;

            foreach (var path in resources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var resource = fileLoader.LoadFile(path) ?? throw new FileNotFoundException("Could not be read");

                    ExportResource(resource, StripCompiledSuffix(path), outputRoot, fileLoader, progress, writtenFiles);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    failed++;
                    progress.Report($"  FAILED {path}: {e.Message}");
                    Log.Error(nameof(CharacterAssetsExporter), $"Failed to export '{path}': {e}");
                }
            }

            return failed;
        }

        private static void ExportResource(Resource resource, string sourcePath, string outputRoot, IFileLoader fileLoader,
            IProgress<string> progress, HashSet<string> writtenFiles)
        {
            // Panorama compiles images straight from the image file, so they are written back as the image they were
            // compiled from, without a vtex
            if (resource.DataBlock is Texture texture
                && (texture.IsRawAnyImage || IsPanoramaImage(sourcePath, texture))
                && GetImageData(resource, texture) is { Length: > 0 } imageData)
            {
                WriteFile(outputRoot, GetImagePath(sourcePath, texture), imageData, progress, writtenFiles);
                return;
            }

            using var contentFile = FileExtract.Extract(resource, fileLoader);

            var mainPath = Path.ChangeExtension(sourcePath, FileExtract.GetExtension(resource));

            if (contentFile.Data != null)
            {
                WriteFile(outputRoot, mainPath, contentFile.Data, progress, writtenFiles);
            }

            WriteSubFiles(outputRoot, GetFolder(sourcePath), contentFile, progress, writtenFiles);

            foreach (var additionalFile in contentFile.AdditionalFiles)
            {
                var additionalPath = additionalFile.FileName.Replace('\\', '/');

                if (additionalFile.Data != null)
                {
                    WriteFile(outputRoot, additionalPath, additionalFile.Data, progress, writtenFiles);
                }

                WriteSubFiles(outputRoot, GetFolder(additionalPath), additionalFile, progress, writtenFiles);
            }
        }

        private static void WriteSubFiles(string outputRoot, string folder, ContentFile contentFile, IProgress<string> progress, HashSet<string> writtenFiles)
        {
            foreach (var subFile in contentFile.SubFiles)
            {
                var subFilePath = folder.Length == 0 ? subFile.FileName : $"{folder}/{subFile.FileName}";

                if (subFile.Extract == null || writtenFiles.Contains(GetOutputPath(outputRoot, subFilePath)))
                {
                    continue;
                }

                var data = subFile.Extract();

                if (data.Length > 0)
                {
                    WriteFile(outputRoot, subFilePath, data, progress, writtenFiles);
                }
            }
        }

        private static int ExportRawFiles(List<string> files, string outputRoot, GameFileLoader fileLoader,
            IProgress<string> progress, HashSet<string> writtenFiles, CancellationToken cancellationToken)
        {
            var failed = 0;

            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var stream = fileLoader.GetFileStream(path) ?? throw new FileNotFoundException("Could not be read");
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);

                    WriteFile(outputRoot, path, buffer.ToArray(), progress, writtenFiles);
                }
                catch (Exception e)
                {
                    failed++;
                    progress.Report($"  FAILED {path}: {e.Message}");
                    Log.Error(nameof(CharacterAssetsExporter), $"Failed to export '{path}': {e}");
                }
            }

            return failed;
        }

        private static void WriteFile(string outputRoot, string relativePath, byte[] data, IProgress<string> progress, HashSet<string> writtenFiles)
        {
            var outputPath = GetOutputPath(outputRoot, relativePath);

            if (!writtenFiles.Add(outputPath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, data);
            progress.Report($"+ {relativePath}");
        }

        /// <summary>
        /// Where a package relative path is written, refusing paths that would land outside of the export folder.
        /// </summary>
        private static string GetOutputPath(string outputRoot, string relativePath)
        {
            var outputPath = Path.GetFullPath(Path.Combine(outputRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (CustomVmatExporter.GetContentRelativePath(outputRoot, outputPath) == null)
            {
                throw new InvalidDataException($"\"{relativePath}\" points outside of the export folder");
            }

            return outputPath;
        }

        private static bool IsPanoramaImage(string sourcePath, Texture texture)
            => sourcePath.StartsWith("panorama/images/", StringComparison.OrdinalIgnoreCase)
                && (texture.Flags & VTexFlags.CUBE_TEXTURE) == 0
                && texture.Depth <= 1;

        private static byte[]? GetImageData(Resource resource, Texture texture)
        {
            if (texture.IsRawAnyImage)
            {
                // The image the texture was compiled from, stored as is
                using var contentFile = new TextureExtract(resource).ToContentFile();
                return contentFile.Data;
            }

            using var bitmap = texture.GenerateBitmap();
            return TextureExtract.ToPngImage(bitmap);
        }

        private static string GetImagePath(string sourcePath, Texture texture)
        {
            var extension = texture.IsRawJpeg ? ".jpg" : texture.IsRawWebp ? ".webp" : ".png";
            var name = Path.GetFileNameWithoutExtension(sourcePath);

            foreach (var suffix in ImageSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^suffix.Length];
                    break;
                }
            }

            var folder = GetFolder(sourcePath);

            return folder.Length == 0 ? name + extension : $"{folder}/{name}{extension}";
        }

        private static string GetFolder(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;

        private static string StripCompiledSuffix(string path)
            => path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase)
                ? path[..^GameFileLoader.CompiledFileSuffix.Length]
                : path;
    }
}
