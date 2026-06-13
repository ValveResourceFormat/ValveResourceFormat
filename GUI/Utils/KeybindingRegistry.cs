using System.Windows.Forms;
using GUI.Types.Exporter;
using GUI.Types.GLViewers;

namespace GUI.Utils
{
    /// <summary>
    /// Represents a single keybinding with its key combination and description.
    /// </summary>
    public readonly record struct KeybindingInfo(string KeyCombination, string Description);

    /// <summary>
    /// Enum representing different viewer types in the application.
    /// </summary>
    public enum ViewerType
    {
        Default,
        TextureViewer,
        ModelViewer,
        WorldViewer,
        ParticleViewer,
        MaterialViewer,
        AudioPlayer,
        //PackageViewer,
    }

    /// <summary>
    /// Registry that maps viewer types to their relevant keybindings.
    /// </summary>
    public static class KeybindingRegistry
    {
        private static readonly Dictionary<ViewerType, List<KeybindingInfo>> Registry = new()
        {
            [ViewerType.TextureViewer] =
            [
                new("←→↑↓", "Pan"),
                new("Scroll", "Zoom"),
                new("Ctrl+Scroll", "Pan H"),
                new("Shift+Scroll", "Pan V"),
                new("Ctrl+0", "Reset zoom"),
                new("Ctrl+±", "Zoom"),
                new("Ctrl+C", "Copy"),
                new("Ctrl+S", "Save"),
                new("F11", "Fullscreen"),
            ],

            [ViewerType.ModelViewer] =
            [
                //new("WASD", "Move"),
                new("Q/Z", "Up/Down"),
                //new("Mouse", "Rotate"),
                new("Alt", "Orbit"),
                new("Shift", "Pan"),
                new("Ctrl", "Adjust sun"),
                //new("Click", "Select"),
                //new("Esc", "Deselect"),
                new("F11", "Fullscreen"),
                new("Ctrl+C", "Screenshot"),
            ],

            [ViewerType.WorldViewer] =
            [
                new("X", "Walk"),
                //new("WASD", "Move"),
                new("Q/Z", "Up/Down"),
                new("Alt", "Orbit"),
                new("Shift", "Pan"),
                new("Click", "Pick"),
                //new("Ctrl+Click", "Multi-select"),
                //new("Esc", "Deselect"),
                //new("Del", "Hide"),
                new("F11", "Fullscreen"),
                new("Ctrl+C", "Screenshot"),
            ],

            [ViewerType.ParticleViewer] =
            [
                new("WASD", "Move"),
                new("Q/Z", "Up/Down"),
                //new("Mouse", "Rotate"),
                new("Alt", "Orbit"),
                new("Shift", "Pan"),
                new("F11", "Fullscreen"),
                new("Ctrl+C", "Screenshot"),
            ],

            [ViewerType.MaterialViewer] =
            [
                new("Mouse", "Orbit"),
                new("Scroll", "Zoom"),
                new("Ctrl+Drag", "Adjust sun"),
                new("F11", "Fullscreen"),
                new("Ctrl+C", "Screenshot"),
            ],

            [ViewerType.AudioPlayer] =
            [
                new("Space", "Play/Pause"),
                new("←→", "Seek ±5s"),
                new("↑↓", "Volume"),
                new("Home/End", "Jump"),
                new("L", "Loop"),
            ],

            [ViewerType.Default] =
            [
                new("Ctrl+F", "Find"),
                new("Ctrl+O", "Open"),
                new("Ctrl+W", "Close tab"),
                new("Ctrl+Q", "Close all tabs"),
                new("Ctrl+E", "Close tabs right"),
                //new("Ctrl+R", "Reload"),
                //new("F5", "Reload"),
            ],
        };

        /// <summary>
        /// Determines the viewer type from a tab page by examining its contents.
        /// </summary>
        /// <param name="tab">The tab page to analyze</param>
        /// <returns>The viewer type of the tab</returns>
        public static ViewerType GetViewerTypeFromTab(TabPage? tab)
        {
            if (tab?.Tag is not ExportData exportData)
            {
                return ViewerType.Default;
            }

            // Determine viewer type from DisposableContents
            var contents = exportData.DisposableContents;

            // Check if Resource viewer with GL component
            if (contents is Types.Viewers.Resource resourceViewer)
            {
                var glViewer = resourceViewer.GLViewer;
                if (glViewer != null)
                {
                    return glViewer switch
                    {
                        GLTextureViewer => ViewerType.TextureViewer,
                        GLMaterialViewer => ViewerType.MaterialViewer,
                        GLWorldViewer => ViewerType.WorldViewer,
                        GLParticleViewer => ViewerType.ParticleViewer,
                        GLSceneViewer => ViewerType.ModelViewer,
                        _ => ViewerType.Default,
                    };
                }

                return ViewerType.Default;
            }

            // Check other viewer types
            return contents switch
            {
                Types.Viewers.Audio => ViewerType.AudioPlayer,
                //PackageViewer => ViewerType.PackageViewer,
                _ => ViewerType.Default
            };
        }

        /// <summary>
        /// Gets the list of keybindings for a specific viewer type.
        /// </summary>
        /// <param name="viewerType">The viewer type</param>
        /// <returns>List of keybindings, or empty list if none defined</returns>
        public static List<KeybindingInfo> GetKeybindingsForViewer(ViewerType viewerType)
        {
            return Registry.TryGetValue(viewerType, out var bindings) ? bindings : [];
        }
    }
}
