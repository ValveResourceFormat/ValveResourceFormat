using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.FlexSceneFile;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Choreo;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes.Choreo;

namespace GUI.Types.GLViewers
{
    partial class GLModelViewer
    {
        private const string SceneSoundChannelPrefix = "choreo_scene_";
        private const string PhonemesFile = "expressions/heroes/phonemes.vfe";
        private const string SoundEventsExtension = "vsndevts" + GameFileLoader.CompiledFileSuffix;

        private List<ChoreoSceneActor> modelScenes = [];
        private readonly HashSet<string> sceneSoundChannels = [];
        private (string? SceneName, string? ActorName) startScene;
        private bool unlistedSoundEventsLoaded;
        private ChoreoScenePlayer? scenePlayer;
        private ComboBox? sceneComboBox;
        private CheckBox? scenePlayCheckBox;
        private GLViewerSliderControl? sceneTimeTrackBar;

        private void AddSceneControls(ModelSceneNode sceneNode)
        {
            Debug.Assert(UiControl != null);

            IChoreoSceneSource[] sources = [new VcdSceneSource(GuiContext, VrfGuiContext.Logger), new VcdListSceneSource(GuiContext, VrfGuiContext.Logger)];
            modelScenes = ChoreoActorMatcher.FindActors(sources, sceneNode, GuiContext.FileName);

            if (modelScenes.Count == 0)
            {
                return;
            }

            using var _ = UiControl.BeginGroup("Scene");

            sceneComboBox = UiControl.AddSelection("Scene", (_, index) => SelectScene(sceneNode, index - 1));
            sceneComboBox.Items.Add("None");

            foreach (var (scene, actor, _) in modelScenes)
            {
                sceneComboBox.Items.Add(string.Create(CultureInfo.InvariantCulture, $"{scene.Name} [{actor.Name}] ({scene.Duration / 1000f:0.0}s)"));
            }

            scenePlayCheckBox = UiControl.AddCheckBox("Play Scene", true, isChecked =>
            {
                if (scenePlayer == null)
                {
                    return;
                }

                if (isChecked && scenePlayer.Time >= scenePlayer.Duration)
                {
                    using var lockedGl = MakeCurrent();
                    scenePlayer.Seek(0f);
                }

                scenePlayer.IsPlaying = isChecked;
            });

            sceneTimeTrackBar = UiControl.AddTrackBar("Scene Time", value =>
            {
                if (scenePlayer == null)
                {
                    return;
                }

                using var lockedGl = MakeCurrent();
                StopSceneSounds();
                scenePlayer.IsPlaying = false;
                scenePlayer.Seek(value * scenePlayer.Duration);
                scenePlayCheckBox.Checked = false;
            });

            // Not finding the scene leaves the first item, which is none
            var (startSceneName, startActorName) = startScene;
            sceneComboBox.SelectedIndex = modelScenes.FindIndex(entry => entry.Scene.Name == startSceneName && entry.Actor.Name == startActorName) + 1;
        }

        /// <summary>Sets the scene to play once the model has loaded, found by its name and the name of its actor.</summary>
        public void SetStartScene(string? sceneName, string actorName)
        {
            startScene = (sceneName, actorName);
        }

        private void SelectScene(ModelSceneNode sceneNode, int index)
        {
            using var lockedGl = MakeCurrent();

            StopSceneSounds();
            scenePlayer?.Stop();
            scenePlayer = null;

            if (index < 0)
            {
                return;
            }

            var (scene, actor, _) = modelScenes[index];
            scenePlayer = new ChoreoScenePlayer(scene, actor, sceneNode, LoadPhonemes());
            scenePlayer.Spoke += PlaySceneSound;
            scenePlayer.SpeechCut += StopSceneSound;
            scenePlayer.IsPlaying = scenePlayCheckBox?.Checked ?? true;
            scenePlayer.Seek(0f);

            // Lower levels of detail can lack the morphs, which would leave the face still
            if (scenePlayer.HasFlexEvents && lodComboBox?.SelectedIndex == 0)
            {
                lodComboBox.SelectedIndex = 1;
            }
        }

        private static string GetSceneSoundChannel(ChoreoEvent speakEvent)
            => string.Create(CultureInfo.InvariantCulture, $"{SceneSoundChannelPrefix}{speakEvent.Id}");

        private void PlaySceneSound(ChoreoEvent speakEvent)
        {
            if (soundPlayer == null)
            {
                return;
            }

            var channel = GetSceneSoundChannel(speakEvent);
            sceneSoundChannels.Add(channel);

            if (soundPlayer.Bank.GetSoundEvent(speakEvent.Param1) == null)
            {
                LoadUnlistedSoundEvents();
            }

            var soundFile = soundPlayer.Play(speakEvent.Param1, channel: channel)?.PlayingSoundFile;

            if (soundFile != null && GuiContext.LoadFileCompiled(soundFile)?.DataBlock is ValveResourceFormat.ResourceTypes.Sound { Sentence: { } sentence } sound)
            {
                scenePlayer?.AddSpeech(speakEvent, sentence, sound.Duration);
            }
        }

        // Games load the voice lines of a character with it, so the manifest of sound events does not list them
        private void LoadUnlistedSoundEvents()
        {
            if (unlistedSoundEventsLoaded)
            {
                return;
            }

            unlistedSoundEventsLoaded = true;

            foreach (var file in GuiContext.FindFilesWithExtension(SoundEventsExtension))
            {
                soundPlayer?.LoadSoundEventsFile(file[..^GameFileLoader.CompiledFileSuffix.Length]);
            }
        }

        private FlexSceneFile? LoadPhonemes()
        {
            using var stream = GuiContext.GetFileStream(PhonemesFile);

            if (stream == null)
            {
                return null;
            }

            var phonemes = new FlexSceneFile();
            phonemes.Read(stream);
            return phonemes;
        }

        private void StopSceneSound(ChoreoEvent speakEvent)
        {
            soundPlayer?.StopChannel(GetSceneSoundChannel(speakEvent));
        }

        private void StopSceneSounds()
        {
            foreach (var channel in sceneSoundChannels)
            {
                soundPlayer?.StopChannel(channel);
            }

            sceneSoundChannels.Clear();
        }

        protected override void OnSimulate(float timestep)
        {
            if (scenePlayer is not { IsPlaying: true } || Paused)
            {
                return;
            }

            scenePlayer.Update(timestep);

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
