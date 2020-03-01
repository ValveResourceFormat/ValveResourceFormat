using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using static GUI.Controls.GLViewerControl;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with world controls (render mode, camera selection).
    /// </summary>
    internal class GLWorldViewer : GLSceneViewer
    {
        private readonly World world;
        private readonly WorldNode worldNode;
        private CheckedListBox worldLayersComboBox;

        public GLWorldViewer(VrfGuiContext guiContext, World world)
            : base(guiContext)
        {
            this.world = world;
        }

        public GLWorldViewer(VrfGuiContext guiContext, WorldNode worldNode)
            : base(guiContext)
        {
            this.worldNode = worldNode;
        }

        protected override void InitializeControl()
        {
            AddRenderModeSelectionControl();

            worldLayersComboBox = ViewerControl.AddMultiSelection("World Layers", (worldLayers) =>
            {
                Scene.SetEnabledLayers(new HashSet<string>(worldLayers));
            });
        }

        protected override void LoadScene()
        {
            if (world != null)
            {
                var loader = new WorldLoader(GuiContext, world);
                var result = loader.Load(Scene);

                var worldLayers = Scene.AllNodes
                    .Select(r => r.LayerName)
                    .Distinct();
                SetAvailableLayers(worldLayers);

                if (worldLayers.Any())
                {
                    // TODO: Since the layers are combined, has to be first in each world node?
                    worldLayersComboBox.SetItemCheckState(0, CheckState.Checked);

                    foreach (var worldNode in result.DefaultEnabledLayers)
                    {
                        worldLayersComboBox.SetItemCheckState(worldLayersComboBox.FindStringExact(worldNode), CheckState.Checked);
                    }
                }

                if (result.DefaultWorldCamera.LengthSquared() != 0)
                {
                    ViewerControl.Camera.SetLocation(result.DefaultWorldCamera);
                    ViewerControl.Camera.LookAt(Vector3.Zero);
                }
            }

            if (worldNode != null)
            {
                var loader = new WorldNodeLoader(GuiContext, worldNode);
                loader.Load(Scene);

                var worldLayers = Scene.AllNodes
                    .Select(r => r.LayerName)
                    .Distinct();
                SetAvailableLayers(worldLayers);
            }

            ShowBaseGrid = false;
        }

        private void SetAvailableLayers(IEnumerable<string> worldLayers)
        {
            worldLayersComboBox.Items.Clear();
            if (worldLayers.Any())
            {
                worldLayersComboBox.Enabled = true;
                worldLayersComboBox.Items.AddRange(worldLayers.ToArray());
            }
            else
            {
                worldLayersComboBox.Enabled = false;
            }
        }
    }
}
