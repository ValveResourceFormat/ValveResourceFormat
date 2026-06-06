using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.DemoPlayback;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers
{
    class Demo(VrfGuiContext vrfGuiContext) : IViewer
    {
        private GLDemoViewer? glViewer;
        private CsDemoPlayback? playback;
        private RendererContext? rendererContext;

        public static bool IsAccepted(ReadOnlySpan<byte> magicData, string fileName)
        {
            return CsDemoFormat.IsAccepted(magicData, fileName);
        }

        public async Task LoadAsync(Stream? stream)
        {
            if (stream != null || !File.Exists(vrfGuiContext.FileName))
            {
                throw new InvalidDataException("CS2 demo playback currently requires a local .dem file.");
            }

            try
            {
                playback = await CsDemoPlayback.LoadAsync(vrfGuiContext.FileName).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(playback.Summary.MapName))
                {
                    throw new InvalidDataException("Unable to determine CS2 map name from demo header.");
                }

                var (mapPath, mapResource) = LoadMapResource(vrfGuiContext, playback.Summary.MapName);

                if (mapResource == null)
                {
                    throw new FileNotFoundException(
                        $"CS2 map '{mapPath}' was not found. Add CS2 gameinfo.gi or pak01_dir.vpk in Settings, then reopen the demo.",
                        mapPath);
                }

                var worldResource = vrfGuiContext.LoadFileCompiled(WorldLoader.GetWorldNameFromMap(mapPath));

                if (worldResource?.DataBlock is not World worldData)
                {
                    throw new FileNotFoundException(
                        $"CS2 world for '{mapPath}' was not found. Add CS2 gameinfo.gi or pak01_dir.vpk in Settings, then reopen the demo.",
                        mapPath);
                }

                var radar = RadarOverview.TryLoad(vrfGuiContext, playback.Summary.MapName);

                rendererContext = vrfGuiContext.CreateRendererContext();
                glViewer = new GLDemoViewer(vrfGuiContext, rendererContext, playback, worldData, radar, mapResource.ExternalReferences as ResourceExtRefList);
                glViewer.InitializeLoad();
            }
            catch
            {
                playback?.Dispose();
                playback = null;
                rendererContext?.Dispose();
                rendererContext = null;
                throw;
            }
        }

        private static string TrimTrailingDigits(string value)
        {
            var end = value.Length;

            while (end > 0 && char.IsDigit(value[end - 1]))
            {
                end--;
            }

            return value[..end];
        }

        private static (string MapPath, ValveResourceFormat.Resource? Resource) LoadMapResource(VrfGuiContext vrfGuiContext, string mapName)
        {
            var mapPath = $"maps/{mapName}.vmap";
            var mapResource = LoadMapResourceCandidate(vrfGuiContext, mapName, mapPath);

            if (mapResource != null)
            {
                return (mapPath, mapResource);
            }

            var fallbackMapName = TrimTrailingDigits(mapName);

            if (fallbackMapName.Length == mapName.Length)
            {
                return (mapPath, null);
            }

            var fallbackMapPath = $"maps/{fallbackMapName}.vmap";

            return (fallbackMapPath, LoadMapResourceCandidate(vrfGuiContext, fallbackMapName, fallbackMapPath));
        }

        private static ValveResourceFormat.Resource? LoadMapResourceCandidate(VrfGuiContext vrfGuiContext, string mapName, string mapPath)
        {
            var mapResource = vrfGuiContext.LoadFileCompiled(mapPath);

            if (mapResource != null)
            {
                return mapResource;
            }

            TryAddMapPackage(vrfGuiContext, mapName);

            return vrfGuiContext.LoadFileCompiled(mapPath);
        }

        private static void TryAddMapPackage(VrfGuiContext vrfGuiContext, string mapName)
        {
            foreach (var searchPath in Settings.Config.GameSearchPaths)
            {
                var root = GetCsgoRoot(searchPath);

                if (root == null)
                {
                    continue;
                }

                var mapPackage = Path.Combine(root, "maps", $"{mapName}.vpk");

                if (File.Exists(mapPackage))
                {
                    vrfGuiContext.AddPackageToSearch(mapPackage);
                    return;
                }
            }
        }

        private static string? GetCsgoRoot(string searchPath)
        {
            if (File.Exists(searchPath))
            {
                var fileName = Path.GetFileName(searchPath);

                if (fileName.Equals("gameinfo.gi", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetDirectoryName(searchPath);
                }
            }

            if (Directory.Exists(searchPath))
            {
                if (Directory.Exists(Path.Combine(searchPath, "maps")))
                {
                    return searchPath;
                }

                var csgoRoot = Path.Combine(searchPath, "game", "csgo");

                if (Directory.Exists(Path.Combine(csgoRoot, "maps")))
                {
                    return csgoRoot;
                }
            }

            return null;
        }

        public void Create(TabPage containerTabPage)
        {
            Debug.Assert(glViewer != null);

            containerTabPage.Controls.Add(glViewer.InitializeUiControls());
            glViewer.InitializeRenderLoop(renderImmediately: true);
        }

        public bool TryExecuteCommand(IReadOnlyList<string> args)
            => glViewer?.TryExecuteCommand(args) == true;

        public void Dispose()
        {
            var hadGlViewer = glViewer != null;
            glViewer?.Dispose();
            glViewer = null;

            if (!hadGlViewer)
            {
                rendererContext?.Dispose();
            }

            rendererContext = null;

            playback?.Dispose();
            playback = null;
        }
    }
}
