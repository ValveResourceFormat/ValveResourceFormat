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

        protected override void LoadScene()
        {
            base.LoadScene();

            void LoadSkeleton(bool firstTime)
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
                }); // update bbox for viewer
            }

            void LoadClip(AnimationClip clip, string skeletonName, bool firstTime = true)
            {
                var skeletonResource = GuiContext.LoadFileCompiled(clip.SkeletonName);
                Debug.Assert(skeletonResource != null);
                SkeletonData = ((BinaryKV3)skeletonResource.DataBlock!).Data;
                LoadSkeleton(firstTime);
                SetAnimationControllerUpdateHandler();

                if (firstTime)
                {
                    AddAnimationControls();
                }

                animationPlayPause.Enabled = true;
                animationController.SetAnimation(new Animation(clip));
            }

            if (clip == null)
            {
                LoadSkeleton(true);
            }
            else
            {
                LoadClip(clip, clip.SkeletonName);

                if (clip.SecondaryAnimations.Length > 0)
                {
                    animationComboBox = AddSelection("Secondary", (_, index) =>
                    {
                        var newClip = index == 0
                            ? clip
                            : clip.SecondaryAnimations[index - 1];

                        LoadClip(newClip, newClip.SkeletonName, firstTime: false);
                    });

                    var defaultSkeleton = Path.GetFileNameWithoutExtension(clip.SkeletonName);
                    var secondarySkeletonIdentifiers = clip.SecondaryAnimations.Select(x => Path.GetFileNameWithoutExtension(x.SkeletonName)).ToArray();
                    animationComboBox.Items.AddRange([defaultSkeleton, .. secondarySkeletonIdentifiers]);
                    animationComboBox.SelectedIndex = 0;
                }
            }
        }

        protected override void OnPaint(object sender, RenderEventArgs e)
        {
            animationController.Update(e.FrameTime);
            base.OnPaint(sender, e);
        }
    }
}
