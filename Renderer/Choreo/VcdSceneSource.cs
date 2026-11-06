using System.IO;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.ResourceTypes.Choreo.Parser;

namespace ValveResourceFormat.Renderer.Choreo
{
    /// <summary>
    /// Reads every compiled <c>.vcd</c> scene of the mounted game. Scenes are named as in scene lists,
    /// by their path below the <c>scenes</c> folder.
    /// </summary>
    /// <param name="fileLoader">The loader of the game.</param>
    /// <param name="logger">Where scenes that fail to load are reported.</param>
    public sealed class VcdSceneSource(GameFileLoader fileLoader, ILogger logger) : IChoreoSceneSource
    {
        private const string Extension = "vcd" + GameFileLoader.CompiledFileSuffix;
        private const string ScenesFolder = "scenes/";

        /// <inheritdoc/>
        public IEnumerable<ChoreoScene> EnumerateScenes()
        {
            foreach (var file in fileLoader.FindFilesWithExtension(Extension))
            {
                var scene = ReadScene(file);

                if (scene != null)
                {
                    yield return scene;
                }
            }
        }

        private ChoreoScene? ReadScene(string file)
        {
            try
            {
                if (fileLoader.LoadFile(file)?.DataBlock is not BinaryKV3 data)
                {
                    return null;
                }

                var scene = Vcd3Parser.Parse(data.Data.Root);
                scene.Name = GetSceneName(file);
                return scene;
            }
            catch (Exception e) when (e is InvalidDataException or EndOfStreamException or UnexpectedMagicException)
            {
                logger.LogWarning("Skipping scene {File}: {Message}", file, e.Message);
                return null;
            }
        }

        private static string GetSceneName(string file)
        {
            var name = file[..^GameFileLoader.CompiledFileSuffix.Length];
            return name.StartsWith(ScenesFolder, StringComparison.OrdinalIgnoreCase) ? name[ScenesFolder.Length..] : name;
        }
    }
}
