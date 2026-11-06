using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GUI.Forms;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter
{
    /// <summary>
    /// Saves a Source 1 or GoldSrc file as it is, together with every file of its game that converting it opens.
    /// </summary>
    static class GameFilesExport
    {
        /// <summary>
        /// Asks for a folder and exports the file and its game files to it, behind a modal progress dialog.
        /// </summary>
        /// <param name="fileName">The path of the file inside of its package, or its name when it is on disk.</param>
        /// <param name="stream">The stream of the file, which the caller disposes.</param>
        /// <param name="vrfGuiContext">The context of the package the file is in, or of the file itself when it is on disk.</param>
        public static async Task ExportWithDependencies(string fileName, Stream stream, VrfGuiContext vrfGuiContext)
        {
            var folder = AppFileDialogs.PickFolder("Choose where to export the file and its game files", AppFileDialogs.RememberIn.SaveDirectory);

            if (folder == null)
            {
                return;
            }

            using var dialog = new GenericProgressForm { StayOpenOnCompletion = true, WaitForWorkToStop = true };
            dialog.SetProgress($"Exporting {fileName}");

            dialog.OnProcess = async cancellationToken =>
            {
                var count = await Task.Run(() => Export(fileName, stream, vrfGuiContext, folder, dialog, cancellationToken), cancellationToken).ConfigureAwait(false);

                var completedText = $"Exported {count} files in {GenericProgressForm.FormatTime(dialog.Elapsed)}";
                Log.Info(nameof(GameFilesExport), completedText);
                dialog.SetProgress(completedText);
            };

            await dialog.ShowDialogAsync().ConfigureAwait(true);

            await dialog.WorkCompletion.ConfigureAwait(true);
        }

        /// <summary>
        /// Converts the file, loads the resources it refers to, and copies each file of the game that this opens.
        /// </summary>
        /// <returns>The number of files copied.</returns>
        internal static int Export(string fileName, Stream stream, VrfGuiContext parentContext, string outputDirectory, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            var copier = new GameFileCopier(outputDirectory);

            progress?.Report("Converting the file");

            using var converted = ConvertedExport.TryConvert(fileName, stream, parentContext, copier)
                ?? throw new InvalidOperationException($"\"{fileName}\" is not a Source 1 or GoldSrc file.");

            LoadReferences(converted, progress, cancellationToken);
            copier.Finish();

            return copier.Count;
        }

        // Loading a resource makes the converters open the files of the game that it is built from. The entities of a map
        // also need what the viewer loads for them besides their references: the models that stand in for them and the sounds they play.
        private static void LoadReferences(ConvertedExport converted, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var classNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<Resource>();
            var toolVisuals = new HammerEntityVisuals(converted.Context, VrfGuiContext.Logger);
            var sounds = new GameSoundFiles(converted.Context, VrfGuiContext.Logger);
            pending.Enqueue(converted.Resource);

            void Load(string name)
            {
                if (!loaded.Add(name))
                {
                    return;
                }

                progress?.Report(name);

                var dependency = converted.Context.LoadFileCompiled(name);

                if (dependency != null)
                {
                    pending.Enqueue(dependency);
                }
            }

            while (pending.TryDequeue(out var resource))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (resource.ExternalReferences is { } externalReferences)
                {
                    foreach (var reference in externalReferences.ResourceRefInfoList)
                    {
                        Load(reference.Name);
                    }
                }

                if (resource.DataBlock is EntityLump entityLump)
                {
                    foreach (var entity in entityLump.GetEntities())
                    {
                        if (entity.GetStringProperty("model") is { Length: > 0 } modelName)
                        {
                            Load(modelName);
                        }

                        if (entity.GetStringProperty("classname") is { } className && classNames.Add(className))
                        {
                            foreach (var standIn in toolVisuals.GetResourcesToPreload(className))
                            {
                                Load(standIn);
                            }
                        }

                        sounds.AddEntity(entity);
                    }
                }
                else if (resource.DataBlock is Model model)
                {
                    sounds.AddModel(model);
                }
            }
        }
    }
}
