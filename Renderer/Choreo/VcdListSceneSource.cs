using System.IO;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.Choreo;

namespace ValveResourceFormat.Renderer.Choreo
{
    /// <summary>
    /// Reads the scenes of every <c>.vcdlist</c> resource of the mounted game.
    /// </summary>
    /// <param name="fileLoader">The loader of the game.</param>
    /// <param name="logger">Where lists that fail to load are reported.</param>
    public sealed class VcdListSceneSource(GameFileLoader fileLoader, ILogger logger) : IChoreoSceneSource
    {
        private const string Extension = "vcdlist" + GameFileLoader.CompiledFileSuffix;

        /// <inheritdoc/>
        public IEnumerable<ChoreoScene> EnumerateScenes()
        {
            foreach (var file in fileLoader.FindFilesWithExtension(Extension))
            {
                foreach (var scene in ReadScenes(file))
                {
                    yield return scene;
                }
            }
        }

        private ChoreoScene[] ReadScenes(string file)
        {
            try
            {
                return fileLoader.LoadFile(file)?.DataBlock is ChoreoSceneFileData list ? list.Scenes : [];
            }
            catch (Exception e) when (e is InvalidDataException or EndOfStreamException or UnexpectedMagicException)
            {
                logger.LogWarning("Skipping scene list {File}: {Message}", file, e.Message);
                return [];
            }
        }
    }
}
