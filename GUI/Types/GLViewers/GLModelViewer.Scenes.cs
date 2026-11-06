using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Choreo;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Source1.Choreo;

namespace GUI.Types.GLViewers
{
    partial class GLModelViewer
    {
        private readonly List<(SceneImage.Entry Entry, ChoreoScene Scene, ChoreoActor Actor)> modelScenes = [];
        private ChoreoScenePlayer? scenePlayer;
        private CheckBox? scenePlayCheckBox;
        private GLViewerSliderControl? sceneTimeTrackBar;

        private void AddSceneControls(ModelSceneNode sceneNode)
        {
            Debug.Assert(UiControl != null);

            if (GuiContext.ResourceProvider is not Source1ResourceProvider provider || provider.GetSceneImage() is not { } image)
            {
                return;
            }

            var modelName = GuiContext.FileName;
            foreach (var entry in image.Entries)
            {
                ChoreoScene scene;
                try
                {
                    scene = image.ReadScene(entry);
                }
                catch (Exception e) when (e is InvalidDataException or EndOfStreamException or UnexpectedMagicException)
                {
                    VrfGuiContext.Logger.LogWarning("Skipping scene {Checksum}: {Message}", entry.Checksum.ToString("x8", CultureInfo.InvariantCulture), e.Message);
                    continue;
                }

                var actor = Array.Find(scene.Actors, actor => ChoreoScenePlayer.IsActorOfModel(actor, modelName));
                if (actor != null)
                {
                    modelScenes.Add((entry, scene, actor));
                }
            }

            if (modelScenes.Count == 0)
            {
                return;
            }

            using var _ = UiControl.BeginGroup("Scene");

            var sceneComboBox = UiControl.AddSelection("Scene", (_, index) => SelectScene(sceneNode, index - 1));
            sceneComboBox.Items.Add("None");

            foreach (var (entry, scene, _) in modelScenes)
            {
                var sound = entry.Sounds.Length > 0 ? entry.Sounds[0] : entry.Checksum.ToString("x8", CultureInfo.InvariantCulture);
                sceneComboBox.Items.Add(string.Create(CultureInfo.InvariantCulture, $"{sound} ({entry.Duration:0.0}s)"));
            }

            sceneComboBox.SelectedIndex = 0;

            scenePlayCheckBox = UiControl.AddCheckBox("Play Scene", true, isChecked =>
            {
                if (scenePlayer != null)
                {
                    scenePlayer.IsPlaying = isChecked;
                }
            });

            sceneTimeTrackBar = UiControl.AddTrackBar("Scene Time", value =>
            {
                if (scenePlayer == null)
                {
                    return;
                }

                using var lockedGl = MakeCurrent();
                scenePlayer.IsPlaying = false;
                scenePlayer.Seek(value * scenePlayer.Duration);
                scenePlayCheckBox.Checked = false;
            });
        }

        private void SelectScene(ModelSceneNode sceneNode, int index)
        {
            using var lockedGl = MakeCurrent();

            scenePlayer?.Stop();
            scenePlayer = null;

            if (index < 0)
            {
                return;
            }

            var (_, scene, actor) = modelScenes[index];
            scenePlayer = new ChoreoScenePlayer(scene, actor, sceneNode);
            scenePlayer.Spoke += choreoEvent => VrfGuiContext.Logger.LogInformation("Scene speaks {Sound}", choreoEvent.Param1);
            scenePlayer.IsPlaying = scenePlayCheckBox?.Checked ?? true;
            scenePlayer.Seek(0f);
        }

        private void UpdateScene(float frameTime)
        {
            if (scenePlayer is not { IsPlaying: true } || Paused)
            {
                return;
            }

            scenePlayer.Update(frameTime);

            if (sceneTimeTrackBar != null && scenePlayer.Duration > 0f)
            {
                sceneTimeTrackBar.Slider.Value = scenePlayer.Time / scenePlayer.Duration;
            }

            if (!scenePlayer.IsPlaying && scenePlayCheckBox != null)
            {
                scenePlayCheckBox.Checked = false;
            }
        }
    }
}
