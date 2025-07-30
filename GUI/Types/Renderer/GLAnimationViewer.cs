using System.IO;
using System.Linq;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Renderer
{
    class GLAnimationViewer : GLModelViewer
    {
        public AnimationClip viewmodelClip { get; init; }

        private SkeletonSceneNode secondarySkeleton;
        private AnimationController secondaryAnimationController;

        public ModelSceneNode? Arms { get; set; }
        public ModelSceneNode? ArmsAg1 { get; set; }
        public ModelSceneNode? Player { get; set; }
        public ModelSceneNode? Weapon { get; set; }

        public GLAnimationViewer(VrfGuiContext guiContext, Resource resource) : base(guiContext)
        {
            if (resource.ResourceType is ResourceType.NmSkeleton)
            {
                //SkeletonData = ((BinaryKV3)resource.DataBlock!).Data;
            }
            else if (resource.DataBlock is AnimationClip animationClip)
            {
                viewmodelClip = animationClip;
            }
            else
            {
                throw new InvalidDataException($"Unsupported resource type for Animation Viewer: {resource.ResourceType}");
            }
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            var skeletonResource = GuiContext.LoadFileCompiled(viewmodelClip.SkeletonName);
            var skeletonData = ((BinaryKV3)skeletonResource.DataBlock!).Data;

            var viewmodelSkeleton = Skeleton.FromSkeletonData(skeletonData);
            animationController = new AnimationController(viewmodelSkeleton, []);

            skeletonSceneNode = new SkeletonSceneNode(Scene, animationController, viewmodelSkeleton)
            {
                Enabled = true,
            };

            Scene.Add(skeletonSceneNode, true);
            skeletonSceneNode.Update(new(0f, this)); // update bbox for viewer

            SetAnimationControllerUpdateHandler();

            AddAnimationControls();
            animationPlayPause.Enabled = true;

            var viewmodelAnim = new Animation(viewmodelClip);
            animationController.SetAnimation(viewmodelAnim);

            /*
            var arms = (Model)GuiContext.LoadFileCompiled("characters/models/shared/arms/glove_fingerless/glove_fingerless_ag2.vmdl").DataBlock!;
            Arms = new ModelSceneNode(Scene, arms) { Name = Path.GetFileNameWithoutExtension(arms.Resource!.FileName) };
            Arms.SetActiveMeshGroups(["first_or_third_person_@1_#&firstperson_default"]);
            Scene.Add(Arms, true);

            var armsAg1 = (Model)GuiContext.LoadFileCompiled("characters/models/shared/arms/glove_fingerless/v_glove_fingerless.vmdl").DataBlock!;
            ArmsAg1 = new ModelSceneNode(Scene, armsAg1) { Name = Path.GetFileNameWithoutExtension(armsAg1.Resource!.FileName) };
            Scene.Add(ArmsAg1, true);
            */

            var player = (Model)GuiContext.LoadFileCompiled("phase2/characters/models/ctm_sas/ctm_sas_ag2.vmdl").DataBlock!;
            Player = new ModelSceneNode(Scene, player) { Name = Path.GetFileNameWithoutExtension(player.Resource!.FileName) };
            Player.SetActiveMeshGroups(["first_or_third_person_@2_#&firstperson_default"]);
            Scene.Add(Player, true);

            Player.RemapAnimationSkeletonBones(viewmodelSkeleton);
            Player.SetAnimation(viewmodelAnim);

            // ak47                   ak47.nmskel
            //   -1, "weapon",           -1, "weapon",
            //    0, "weapon_offset"      0, "weapon_offset",
            //    1, "bolt",              1, "bolt",
            //    1, "clip",              1, "clip",
            //    1, "cliprelease",       1, "cliprelease",
            //    1, "trigger",           1, "econ",
            //    1, "ag1_hand_r",        1, "muzzle",
            //                            1, "trigger",

            // viewmodel.nmskel lists ak47.nmskel as secondary skeleton, attached to "wpn" bone

            var ak47Clip = viewmodelClip.SecondaryAnimations[0];
            var ak47Skeleton = Skeleton.FromSkeletonData(((BinaryKV3)GuiContext.LoadFileCompiled(ak47Clip.SkeletonName).DataBlock!).Data);

            secondaryAnimationController = new AnimationController(ak47Skeleton, []);

            secondarySkeleton = new SkeletonSceneNode(Scene, secondaryAnimationController, ak47Skeleton)
            {
                Enabled = true,
            };
            Scene.Add(secondarySkeleton, true);

            var weapon = (Model)GuiContext.LoadFileCompiled("phase2/weapons/models/ak47/weapon_rif_ak47_ag2.vmdl").DataBlock!;
            Weapon = new ModelSceneNode(Scene, weapon) { Name = Path.GetFileNameWithoutExtension(weapon.Resource!.FileName) };
            Scene.Add(Weapon, true);

            Weapon.RemapAnimationSkeletonBones(ak47Skeleton);

            var ak47Anim = new Animation(ak47Clip);
            Weapon.SetAnimation(ak47Anim);
            secondaryAnimationController.SetAnimation(ak47Anim);

            var wpnBoneIndex = Array.FindIndex(animationController.FrameCache.Skeleton.Bones, b => b.Name == "wpn");
            Weapon.AnimationController.rootBoneTransformProvider = () =>
            {
                if (wpnBoneIndex < 0 || animationController.FrameCache.InterpolatedFrame.Bones.Length <= wpnBoneIndex)
                {
                    return Matrix4x4.Identity;
                }

                var wpnBone = animationController.FrameCache.InterpolatedFrame.Bones[wpnBoneIndex];
                return Matrix4x4.CreateScale(wpnBone.Scale)
                    * Matrix4x4.CreateFromQuaternion(wpnBone.Angle)
                    * Matrix4x4.CreateTranslation(wpnBone.Position);
            };
        }

        protected override void OnPaint(object sender, RenderEventArgs e)
        {
            animationController.Update(e.FrameTime);
            secondaryAnimationController.Update(e.FrameTime);
            base.OnPaint(sender, e);
        }
    }
}
