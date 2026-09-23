using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// A model the character preview shows.
    /// </summary>
    /// <param name="Path">The model as named in scripts, e.g. "models/heroes/axe/axe.vmdl".</param>
    /// <param name="Skin">Index of the material group to show it with.</param>
    sealed record PreviewModel(string Path, int Skin);

    /// <summary>
    /// Previews a hero playing its idle while wearing a set of item models, which follow the hero's skeleton.
    /// </summary>
    class GLCharacterPreviewViewer : GLSingleNodeViewer
    {
        private readonly List<ModelSceneNode> modelNodes = [];
        private IReadOnlyList<PreviewModel> models = [];
        private int requestedVersion;

        public GLCharacterPreviewViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext)
            : base(vrfGuiContext, rendererContext)
        {
        }

        protected override void AddUiControls()
        {
            base.AddUiControls();

            // The dialog hosting the preview has its own controls
            UiControl?.HideSidebar();
        }

        protected override void LoadScene()
        {
            AddModels(models);
        }

        /// <summary>
        /// Replaces the shown models, loading them on a worker thread. The first one is the hero, which the others follow.
        /// When called again before a load finishes, only the latest set is shown.
        /// </summary>
        /// <param name="previewModels">The models to show.</param>
        /// <param name="frameCamera">Whether to move the camera to fit the new models, rather than keeping the user's view.</param>
        public void SetModels(IReadOnlyList<PreviewModel> previewModels, bool frameCamera)
        {
            var version = Interlocked.Increment(ref requestedVersion);
            models = previewModels;

            // Before the viewer is loaded, the models are picked up by LoadScene
            if (GraphicsContext == null)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                if (version != Volatile.Read(ref requestedVersion))
                {
                    return;
                }

                try
                {
                    using var lockedGl = MakeCurrent();

                    if (version != Volatile.Read(ref requestedVersion))
                    {
                        return;
                    }

                    foreach (var node in modelNodes)
                    {
                        Scene.Remove(node, dynamic: true);
                    }

                    modelNodes.Clear();

                    AddModels(previewModels);
                    Scene.Initialize();

                    if (frameCamera)
                    {
                        FrameModels();
                    }
                }
                catch (Exception e)
                {
                    Log.Error(nameof(GLCharacterPreviewViewer), $"Failed to load preview models: {e}");
                }
            });
        }

        private void AddModels(IReadOnlyList<PreviewModel> previewModels)
        {
            foreach (var previewModel in previewModels)
            {
                // Owned by the context's resource cache
                var resource = GuiContext.LoadFileCompiled(previewModel.Path);

                if (resource?.DataBlock is not Model model)
                {
                    Log.Warn(nameof(GLCharacterPreviewViewer), $"Could not load \"{previewModel.Path}\" for the preview");
                    continue;
                }

                var node = new ModelSceneNode(Scene, model);

                if (previewModel.Skin > 0 && model.GetMaterialGroups().ElementAtOrDefault(previewModel.Skin).Name is { } skin)
                {
                    node.SetMaterialGroup(skin);
                }

                if (modelNodes.Count == 0)
                {
                    PlayIdle(node);
                }
                else
                {
                    // Worn the way the game attaches items, following the hero's bones
                    node.SetBoneMergeTarget(modelNodes[0]);
                }

                Scene.Add(node, dynamic: true);
                modelNodes.Add(node);
            }
        }

        /// <summary>
        /// Plays the hero's plain idle. Weapons in particular are only in hand once animated, in the bind pose their bones
        /// sit at the hero's feet.
        /// </summary>
        private static void PlayIdle(ModelSceneNode hero)
        {
            var idle = hero.Animations.Values
                .OfType<SequenceAnimation>()
                .Where(static animation => animation.Activities.Length > 0 && animation.Activities[0].Name == "ACT_DOTA_IDLE")
                .OrderBy(static animation => animation.Activities.Length)
                .ThenByDescending(static animation => animation.Activities[0].Weight)
                .FirstOrDefault();

            if (idle != null)
            {
                hero.SetAnimation(idle);
            }
            else if (hero.Animations.TryGetValue("idle", out var namedIdle))
            {
                hero.SetAnimation(namedIdle);
            }
        }

        /// <summary>
        /// Looks at the hero from the front, the way heroes face in Source 2 (+X). Items are left out, a pet or a
        /// courier would pull the camera away from the hero.
        /// </summary>
        private void FrameModels()
        {
            if (modelNodes.Count == 0)
            {
                return;
            }

            var bounds = modelNodes[0].BoundingBox;
            var distance = Math.Clamp(bounds.Size.Length() * 0.9f, 50f, 2000f);

            Input.Camera.SetLocation(bounds.Center + new Vector3(distance, distance * 0.35f, distance * 0.3f));
            Input.Camera.LookAt(bounds.Center);
        }

        public override void PostSceneLoad()
        {
            base.PostSceneLoad();
            FrameModels();
        }
    }
}
