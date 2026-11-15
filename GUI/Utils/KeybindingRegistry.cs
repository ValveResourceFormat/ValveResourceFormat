using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Exporter;
using GUI.Types.GLViewers;
using GUI.Types.PackageViewer;

namespace GUI.Utils
{
    public readonly record struct KeybindingInfo(string KeyCombination, string Description);

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
        PackageEditor,
    }

    public static class KeybindingRegistry
    {
        private static List<KeybindingInfo> CreateAppKeybindings()
        {
            return
            [
                new("Ctrl+F", Locale.Keys_Find),
                new("Ctrl+O", Locale.Keys_Open),
                new("Ctrl+W", Locale.Keys_CloseTab),
                new("Ctrl+Q", Locale.Keys_CloseAllTabs),
                new("Ctrl+E", Locale.Keys_CloseTabsRight),
                //new("Ctrl+R", "Reload"),
                //new("F5", "Reload"),
            ];
        }

        private static List<KeybindingInfo> CreateKeybindings(ViewerType viewerType) => viewerType switch
        {
            ViewerType.TextureViewer =>
            [
                new("←→↑↓", Locale.Keys_Pan),
                new("Scroll", Locale.Keys_Zoom),
                new("Ctrl+Scroll", Locale.Keys_PanHorizontal),
                new("Shift+Scroll", Locale.Keys_PanVertical),
                new("Ctrl+0", Locale.Keys_ResetZoom),
                new("Ctrl+±", Locale.Keys_Zoom),
                new("Ctrl+C", Locale.Keys_Copy),
                new("Ctrl+S", Locale.Keys_Save),
                new("F11", Locale.Keys_Fullscreen),
            ],

            ViewerType.ModelViewer =>
            [
                //new("WASD", "Move"),
                new("Z", Locale.Keys_Mouselook),
                //new("Mouse", "Rotate"),
                new("Alt", Locale.Keys_Orbit),
                new("Shift", Locale.Keys_Pan),
                new("Ctrl", Locale.Keys_AdjustSun),
                //new("Click", "Select"),
                //new("Esc", "Deselect"),
                new("F11", Locale.Keys_Fullscreen),
                new("Ctrl+C", Locale.Keys_Screenshot),
            ],

            ViewerType.WorldViewer =>
            [
                new("Q/Z", Locale.Keys_UpDown),
                new("X", Locale.Keys_Walk),
                //new("WASD", "Move"),
                new("Alt", Locale.Keys_Orbit),
                new("Shift", Locale.Keys_Pan),
                new("Click", Locale.Keys_Select),
                new("Dbl-click", Locale.Keys_Details),
                new("Ctrl+Dbl-click", Locale.Keys_Open),
                //new("Ctrl+Click", "Multi-select"),
                //new("Esc", "Deselect"),
                //new("Del", "Hide"),
                new("F11", Locale.Keys_Fullscreen),
                new("Ctrl+C", Locale.Keys_Screenshot),
            ],

            ViewerType.ParticleViewer =>
            [
                new("WASD", Locale.Keys_Move),
                new("Q/Z", Locale.Keys_UpDown),
                //new("Mouse", "Rotate"),
                new("Alt", Locale.Keys_Orbit),
                new("Shift", Locale.Keys_Pan),
                new("F11", Locale.Keys_Fullscreen),
                new("Ctrl+C", Locale.Keys_Screenshot),
            ],

            ViewerType.MaterialViewer =>
            [
                new("Drag", Locale.Keys_Orbit),
                new("Right drag", Locale.Keys_Pan),
                new("Scroll", Locale.Keys_Zoom),
                new("Ctrl+Drag", Locale.Keys_AdjustSun),
                new("F11", Locale.Keys_Fullscreen),
                new("Ctrl+C", Locale.Keys_Screenshot),
            ],

            ViewerType.GraphViewer =>
            [
                new("Drag", Locale.Keys_Pan),
                new("Scroll", Locale.Keys_Zoom),
                new("Shift+Click", Locale.Keys_MultiSelect),
                new("Ctrl+Drag", Locale.Keys_MoveConnected),
                new("Right click", Locale.Keys_Menu),
                new("Ctrl+0", Locale.Keys_Fit),
                new("Ctrl+C", Locale.Keys_Copy),
                new("Ctrl+S", Locale.Keys_Save),
                new("F11", Locale.Keys_Fullscreen),
            ],

            ViewerType.AudioPlayer =>
            [
                new("Space", Locale.Keys_PlayPause),
                new("←→", Locale.Keys_Seek),
                new("↑↓", Locale.Keys_Volume),
                new("Home/End", Locale.Keys_Jump),
                new("L", Locale.Keys_Loop),
            ],

            ViewerType.PackageEditor =>
            [
                new("Ctrl+S", Locale.Keys_Save),
                .. CreateAppKeybindings(),
            ],

            _ => CreateAppKeybindings(),
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
                return tab != null && PackageViewer.FromTab(tab) is { IsEditingPackage: true } ? ViewerType.PackageEditor : ViewerType.Default;
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
        public static List<KeybindingInfo> GetKeybindingsForViewer(ViewerType viewerType) => CreateKeybindings(viewerType);
    }
}
