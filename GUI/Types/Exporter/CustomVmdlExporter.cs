using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using VmdlExtractor;

namespace GUI.Types.Exporter
{
    /// <summary>
    /// Drives the custom VMDL extractor (<see cref="VmdlExtractor.CustomModelExporter"/>) from the
    /// package viewer's context menu, as an alternative to the built-in "Decompile &amp;&amp; export".
    /// </summary>
    static class CustomVmdlExporter
    {
        public static async Task ExtractSelection(object sender)
        {
            if (sender is not ToolStripMenuItem { Owner: ContextMenuStrip { SourceControl: var owner } })
            {
                throw new InvalidDataException("Invalid context menu structure");
            }

            VrfGuiContext? context;
            var paths = new List<string>();
            IBetterBaseItem? singleSelectedNode = null;

            if (owner is BetterTreeView tree)
            {
                context = tree.VrfGuiContext;

                if (tree.SelectedNode is IBetterBaseItem selectedNode)
                {
                    singleSelectedNode = selectedNode;
                    CollectVmdlPaths(selectedNode, paths);
                }
            }
            else if (owner is BetterListView listView)
            {
                context = listView.VrfGuiContext;

                var selectedItems = listView.GetSelectedVirtualItems();

                if (selectedItems.Count == 1 && selectedItems[0] is IBetterBaseItem onlyNode)
                {
                    singleSelectedNode = onlyNode;
                }

                foreach (var item in selectedItems)
                {
                    if (item is IBetterBaseItem selectedNode)
                    {
                        CollectVmdlPaths(selectedNode, paths);
                    }
                }
            }
            else
            {
                throw new InvalidDataException("Unknown state");
            }

            if (context == null)
            {
                return;
            }

            if (context.CurrentPackage == null)
            {
                Log.Error(nameof(CustomVmdlExporter), "CurrentPackage is null, cannot extract files");
                return;
            }

            // Same VPK the file was browsed from, opened fresh below so this always resolves
            // dependencies (including materials already shipped in the package) exactly like
            // running VmdlExtractor.exe -i <this vpk> -f <path> does from the command line,
            // rather than through the viewer's own shared, cached file loader.
            // Package.FileName is an internal base name used to resolve multi-part archives
            // (e.g. "pak01"), not a path on disk - VrfGuiContext.FileName is the actual path
            // it was opened from (e.g. "...\pak01_dir.vpk").
            var vpkPath = context.FileName;

            if (paths.Count == 0)
            {
                await AppMessageDialogs.ShowMessageAsync(
                    "The selection does not contain any .vmdl_c model files.",
                    "Nothing to export",
                    MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            using var optionsForm = new CustomVmdlExtractOptionsForm();

            if (await optionsForm.ShowDialogAsync().ConfigureAwait(true) != DialogResult.OK)
            {
                return;
            }

            string outputPath;
            bool flattenOutput;
            string? outputBasename = null;

            // A single selected file (not a folder) gets a normal "save file" prompt, same as the
            // built-in "Decompile && export" does for one file. The chosen folder is used exactly
            // as-is (no recreating the model's "models/items/..." path inside it) and the top-level
            // .vmdl is renamed to whatever filename was picked, so e.g. saving an arcana model as
            // "razor.vmdl" drops it in as the hero's base model. Only that one file is renamed - its
            // mesh/anim/physics dependencies keep their own names, which is what the renamed .vmdl
            // itself still references internally. Everything else (multiple files, or a folder to
            // recurse into) asks for a destination folder and preserves each file's relative path
            // under it instead, so a batch export doesn't collide everything into one folder.
            if (singleSelectedNode is { IsFolder: false } && paths.Count == 1)
            {
                var suggestedName = Path.GetFileNameWithoutExtension(paths[0]);

                var pickedFileName = AppFileDialogs.SaveFile(
                    "Choose where to save the file", suggestedName, "vmdl", "vmdl file|*.vmdl");

                if (pickedFileName == null)
                {
                    return;
                }

                outputPath = Path.GetDirectoryName(pickedFileName) is { Length: > 0 } directory
                    ? directory
                    : ".";
                flattenOutput = true;
                outputBasename = Path.GetFileNameWithoutExtension(pickedFileName);
            }
            else
            {
                var pickedFolder = AppFileDialogs.PickFolder(
                    "Choose which folder to export the custom-decompiled models to",
                    AppFileDialogs.RememberIn.SaveDirectory);

                if (pickedFolder == null)
                {
                    return;
                }

                outputPath = pickedFolder;
                flattenOutput = false;
            }

            var options = new CustomModelExportOptions
            {
                Verbose = optionsForm.Verbose,
                AutoBuild = optionsForm.AutoBuild,
                ResourceCompilerPath = optionsForm.AutoBuild ? optionsForm.ResourceCompilerPath : null,
                GameDir = optionsForm.AutoBuild ? optionsForm.GameDir : null,
                DonorVpkPath = vpkPath,
                FlattenOutput = flattenOutput,
                OutputBasename = outputBasename,
            };

            RunInDialog(vpkPath, paths, outputPath, options, out var workCompletion);
            await workCompletion.ConfigureAwait(true);
        }

        private static void CollectVmdlPaths(IBetterBaseItem root, List<string> results)
        {
            if (root.IsFolder)
            {
                if (root.PkgNode != null)
                {
                    CollectVmdlPaths(root.PkgNode, results);
                }
            }
            else if (root.PackageEntry is { TypeName: "vmdl_c" } entry)
            {
                results.Add(entry.GetFullPath());
            }
        }

        private static void CollectVmdlPaths(VirtualPackageNode root, List<string> results)
        {
            foreach (var node in root.Folders.Values)
            {
                CollectVmdlPaths(node, results);
            }

            foreach (var file in root.Files)
            {
                if (file.TypeName == "vmdl_c")
                {
                    results.Add(file.GetFullPath());
                }
            }
        }

        private static void RunInDialog(
            string vpkPath, List<string> paths, string outputPath, CustomModelExportOptions options,
            out Task workCompletion)
        {
            using var dialog = new CustomVmdlExtractProgressForm { StayOpenOnCompletion = true };

            dialog.AppendLine($"Exporting {paths.Count} model{(paths.Count == 1 ? "" : "s")} with the custom VMDL extractor to \"{outputPath}\"...");
            dialog.AppendLine($"Mode: {(options.FlattenOutput ? "single-file (flat)" : "folder (preserves relative paths)")}, rename to: {options.OutputBasename ?? "(keep original name)"}");

            dialog.OnProcess = cancellationToken =>
            {
                Extract(vpkPath, paths, outputPath, options, dialog, cancellationToken);
                return Task.CompletedTask;
            };

            dialog.ShowDialog();

            workCompletion = dialog.WorkCompletion;
        }

        private static void Extract(
            string vpkPath, List<string> paths, string outputPath,
            CustomModelExportOptions options, CustomVmdlExtractProgressForm progress, CancellationToken cancellationToken)
        {
            Log.Info(nameof(CustomVmdlExporter), $"Custom VMDL export started to \"{outputPath}\"");

            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdOutWriter = new LogTextWriter(isError: false, progress);
            using var stdErrWriter = new LogTextWriter(isError: true, progress);
            Console.SetOut(stdOutWriter);
            Console.SetError(stdErrWriter);

            // Open the source VPK fresh, exactly like the standalone CLI's VPK mode does, instead
            // of reusing the viewer's own shared/cached file loader. That loader's cache and search
            // path state is meant for the 3D viewer and glTF exporter, and can cause dependencies
            // that are actually present in the VPK (most commonly materials) to resolve as missing.
            using var package = new Package();
            package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
            package.Read(vpkPath);

            using var fileLoader = new GameFileLoader(package, package.FileName);

            var initialTargets = new List<(string RelativePath, Resource Resource)>(paths.Count);

            try
            {
                var processed = 0;

                foreach (var path in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    progress.AppendLine($"[{++processed}/{paths.Count}] {path}");

                    var entry = package.FindEntry(path);

                    if (entry == null)
                    {
                        Log.Error(nameof(CustomVmdlExporter), $"{path}: entry not found in \"{vpkPath}\"");
                        continue;
                    }

                    package.ReadEntry(entry, out byte[] data);

                    // Resource.Read documents that the stream must stay open for the resource's
                    // lifetime since some blocks (e.g. VBIB vertex/index buffers) are read lazily
                    // on first access. resource.Dispose() closes it for us via its BinaryReader.
                    var resource = new Resource { FileName = path };
                    var stream = new MemoryStream(data);
                    resource.Read(stream);

                    initialTargets.Add((path, resource));
                }

                var result = CustomModelExporter.ExtractModels(initialTargets, outputPath, fileLoader, options);

                Log.Info(nameof(CustomVmdlExporter), $"Custom VMDL export finished: {result.Succeeded} succeeded, {result.Failures.Count} failed");

                foreach (var failure in result.Failures)
                {
                    progress.AppendLine($"FAILED {failure.File}: {failure.Error}");
                    Log.Error(nameof(CustomVmdlExporter), $"{failure.File}: {failure.Error}");
                }

                if (result.AutoBuildAttempted)
                {
                    if (result.AutoBuildFailed)
                    {
                        Log.Error(nameof(CustomVmdlExporter), $"Auto-build failed: {result.AutoBuildError}");
                    }
                    else
                    {
                        Log.Info(nameof(CustomVmdlExporter), "Auto-build completed");
                    }
                }

                var completedText = $"Export completed in {GenericProgressForm.FormatTime(progress.Elapsed)}: {result.Succeeded} succeeded";

                if (result.Failures.Count > 0)
                {
                    completedText += $", {result.Failures.Count} file{(result.Failures.Count == 1 ? "" : "s")} failed";
                }

                progress.AppendLine(completedText);
            }
            finally
            {
                foreach (var (_, resource) in initialTargets)
                {
                    resource?.Dispose();
                }

                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        private sealed class LogTextWriter(bool isError, CustomVmdlExtractProgressForm progress) : TextWriter
        {
            public override Encoding Encoding => Encoding.UTF8;

            public override void WriteLine(string? value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return;
                }

                progress.AppendLine(value);

                if (isError)
                {
                    Log.Error(nameof(VmdlExtractor), value);
                }
                else
                {
                    Log.Info(nameof(VmdlExtractor), value);
                }
            }
        }
    }
}
