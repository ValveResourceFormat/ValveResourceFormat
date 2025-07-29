using System.IO;
using System.Linq;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace GUI.Types.Renderer
{
    class GLAnimationViewer : GLModelViewer
    {
        public Skeleton Skeleton { get; set; }
        public AnimationClip? clip { get; init; }

        public GLAnimationViewer(VrfGuiContext guiContext, Resource resource) : base(guiContext)
        {
            if (resource.ResourceType is ResourceType.NmSkeleton)
            {
                Skeleton = Skeleton.FromSkeletonData(((BinaryKV3)resource.DataBlock!).Data);
            }
            else if (resource.DataBlock is AnimationClip animationClip)
            {
                clip = animationClip;

                var skeletonResource = guiContext.LoadFileCompiled(animationClip.SkeletonName);
                Skeleton = Skeleton.FromSkeletonData(((BinaryKV3)skeletonResource.DataBlock!).Data);
            }
            else
            {
                throw new InvalidDataException($"Unsupported resource type for Animation Viewer: {resource.ResourceType}");
            }
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            if (clip != null)
            {
                void LoadClip(AnimationClip clip, string skeletonName, bool firstTime = true)
                {
                    animationController = new AnimationController(Skeleton, []);

                    if (!firstTime && skeletonSceneNode != null)
                    {
                        skeletonSceneNode.Enabled = false; // scene.Remove?
                    }

                    skeletonSceneNode = new SkeletonSceneNode(Scene, animationController, Skeleton)
                    {
                        Enabled = true,
                    };

                    SetAnimationControllerUpdateHandler();

                    if (firstTime)
                    {
                        AddAnimationControls();
                        skeletonSceneNode.Update(new(0f, this)); // update bbox for viewer
                    }

                    animationPlayPause.Enabled = true;
                    animationController.SetAnimation(new Animation(clip));
                    Scene.Add(skeletonSceneNode, true);
                }

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
    }
}
