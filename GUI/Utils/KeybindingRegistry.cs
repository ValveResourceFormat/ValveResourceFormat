using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
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
        GraphViewer,
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
                new("Z", "Mouselook"),
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
                new("Q/Z", "Up/Down"),
                new("X", "Walk"),
                //new("WASD", "Move"),
                new("Alt", "Orbit"),
                new("Shift", "Pan"),
                new("Click", "Select"),
                new("Dbl-click", "Details"),
                new("Ctrl+Dbl-click", "Open"),
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
                new("Drag", "Orbit"),
                new("Right drag", "Pan"),
                new("Scroll", "Zoom"),
                new("Ctrl+Drag", "Adjust sun"),
                new("F11", "Fullscreen"),
                new("Ctrl+C", "Screenshot"),
            ],

            [ViewerType.GraphViewer] =
            [
                new("Drag", "Pan"),
                new("Scroll", "Zoom"),
                new("Shift+Click", "Multi-select"),
                new("Ctrl+Drag", "Move connected"),
                new("Right click", "Menu"),
                new("Ctrl+0", "Fit"),
                new("Ctrl+C", "Copy"),
                new("Ctrl+S", "Save"),
                new("F11", "Fullscreen"),
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

            var contents = exportData.DisposableContents;

            if (contents is Types.Viewers.Resource resourceViewer)
            {
                var selectedSubTab = tab.Controls.OfType<TabControl>().FirstOrDefault()?.SelectedTab;
                var glViewer = (selectedSubTab != null ? GLBaseControl.FindHostedIn(selectedSubTab) : null) ?? resourceViewer.GLViewer;

                if (glViewer != null)
                {
                    return glViewer switch
                    {
                        GLGraphViewer => ViewerType.GraphViewer,
                        GLTextureViewer => ViewerType.TextureViewer,
                        GLMaterialViewer => ViewerType.MaterialViewer,
                        GLWorldViewer => ViewerType.WorldViewer,
                        GLParticleViewer => ViewerType.ParticleViewer,
                        GLSceneViewer => ViewerType.ModelViewer,
                        _ => ViewerType.Default,
                    };
                }

                if ((selectedSubTab ?? tab).Controls.OfType<AudioPlaybackPanel>().Any())
                {
                    return ViewerType.AudioPlayer;
                }

                return ViewerType.Default;
            }

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
