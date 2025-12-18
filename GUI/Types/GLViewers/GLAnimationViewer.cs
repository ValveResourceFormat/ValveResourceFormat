using System.Diagnostics;
using System.IO;
using System.Linq;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.GLViewers
{
    class GLAnimationViewer : GLModelViewer
    {
        public KVObject SkeletonData { get; set; }
        public AnimationClip? clip { get; init; }

        public GLAnimationViewer(VrfGuiContext guiContext, Resource resource) : base(guiContext)
        {
            if (resource.ResourceType is ResourceType.NmSkeleton)
            {
                SkeletonData = ((BinaryKV3)resource.DataBlock!).Data;
            }
            else if (resource.DataBlock is AnimationClip animationClip)
            {
                clip = animationClip;

                var skeletonResource = guiContext.LoadFileCompiled(animationClip.SkeletonName);
                Debug.Assert(skeletonResource != null);
                SkeletonData = ((BinaryKV3)skeletonResource.DataBlock!).Data;
            }
            else
            {
                throw new InvalidDataException($"Unsupported resource type for Animation Viewer: {resource.ResourceType}");
            }
        }

        private void LoadSkeleton(bool firstTime)
        {
            var skeleton = Skeleton.FromSkeletonData(SkeletonData);
            animationController = new AnimationController(skeleton, []);

            if (!firstTime && skeletonSceneNode != null)
            {
                skeletonSceneNode.Enabled = false; // scene.Remove?
            }

            skeletonSceneNode = new SkeletonSceneNode(Scene, animationController, skeleton)
            {
                Enabled = true,
            };

            Scene.Add(skeletonSceneNode, true);
            skeletonSceneNode.Update(new Scene.UpdateContext
            {
                TextRenderer = TextRenderer,
                Timestep = 0f,
                View = this,
            }); // update bbox for viewer
        }

        private void LoadClipScene(AnimationClip clipToLoad, bool firstTime)
        {
            var skeletonResource = GuiContext.LoadFileCompiled(clipToLoad.SkeletonName);
            Debug.Assert(skeletonResource != null);
            SkeletonData = ((BinaryKV3)skeletonResource.DataBlock!).Data;
            LoadSkeleton(firstTime);
            animationController.SetAnimation(new Animation(clipToLoad));
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            if (clip == null)
            {
                LoadSkeleton(true);
            }
            else
            {
                LoadClipScene(clip, firstTime: true);
            }
        }

        protected override void AddUiControls()
        {
            if (clip != null)
            {
                AddAnimationControls();

                void BindAnimationUi()
                {
                    // Register update handler
                    SetAnimationControllerUpdateHandler();

                    // Set trackbar length to the animation length
                    animationController.SetAnimation(animationController.ActiveAnimation);
                }

                if (animationPlayPause != null)
                {
                    animationPlayPause.Enabled = true;
                }

                if (clip.SecondaryAnimations.Length > 0)
                {
                    animationComboBox = UiControl.AddSelection("Secondary", (_, index) =>
                    {
                        var newClip = index == 0
                            ? clip
                            : clip.SecondaryAnimations[index - 1];

                        {
                            using var lockedGl = MakeCurrent();
                            LoadClipScene(newClip, firstTime: false);
                        }
                        BindAnimationUi();

                        if (animationPlayPause != null)
                        {
                            animationPlayPause.Enabled = true;
                        }
                    });

                    var defaultSkeleton = Path.GetFileNameWithoutExtension(clip.SkeletonName);
                    var secondarySkeletonIdentifiers = clip.SecondaryAnimations.Select(x => Path.GetFileNameWithoutExtension(x.SkeletonName)).ToArray();
                    animationComboBox.Items.AddRange([defaultSkeleton, .. secondarySkeletonIdentifiers]);
                    animationComboBox.SelectedIndex = 0;
                }

                BindAnimationUi();
            }
        }

        protected override void OnPaint(RenderEventArgs e)
        {
            animationController.Update(e.FrameTime);
            base.OnPaint(e);
        }
    }
}
