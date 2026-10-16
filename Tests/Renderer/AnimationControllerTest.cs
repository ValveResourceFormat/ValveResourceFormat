using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace Tests.Renderer
{
    public class AnimationControllerTest
    {

        [Test]
        public async Task PlayerOwnsPauseStateWhenTheClipFinishes()
        {
            using var resource = new Resource();
            resource.Read(TestFixtures.Path("box_creature_ik_model.vmdl_c"));
            var model = (Model)resource.DataBlock!;

            var controller = new AnimationController(model.Skeleton, model.FlexControllers)
            {
                Looping = false,
            };

            var walk = model.GetEmbeddedAnimations().First(a => a.Name == "box_creature_leggy_walk");
            controller.SetAnimation(walk);

            // The clip runs 25 frames at 30fps, so a full second overshoots its end and pauses it.
            await Assert.That(controller.Update(1f)).IsTrue();

            using (Assert.Multiple())
            {
                await Assert.That(controller.IsPaused).IsTrue().Because("finishing the clip pauses the player on the same tick");
                await Assert.That(controller.ActiveClipFinished).IsTrue();
                await Assert.That(controller.Frame).IsEqualTo(walk.FrameCount - 1);
            }

            await Assert.That(controller.Update(0f)).IsFalse().Because("a paused controller reports no pose change");
        }

        [Test]
        public async Task SwitchingPlayersCarriesPauseAndRemapsTheExternalPose()
        {
            using var modelResource = new Resource();
            modelResource.Read(TestFixtures.Path("box_creature_ik_model.vmdl_c"));
            var model = (Model)modelResource.DataBlock!;

            using var skeletonResource = new Resource();
            skeletonResource.Read(TestFixtures.Path("ak47.vnmskel_c"));
            var externalSkeleton = Skeleton.FromSkeletonData(((BinaryKV3)skeletonResource.DataBlock!).Data);

            using var clipResource = new Resource();
            clipResource.Read(TestFixtures.Path("idle_ak.vnmclip_c"));
            var clip = FindClipForSkeleton((AnimationClip)clipResource.DataBlock!, "ak47.vnmskel");

            var controller = new AnimationController(model.Skeleton, model.FlexControllers);
            controller.RegisterExternalSkeleton(clip.SkeletonName, externalSkeleton);

            var idle = model.GetEmbeddedAnimations().First(a => a.Name == "box_creature_leggy_idle");
            controller.SetAnimation(idle);
            controller.Update(0.1f);

            await Assert.That(controller.CurrentPlayer).IsNull().Because("a model animation plays on the model player");

            controller.IsPaused = true;
            controller.SetAnimation(new ClipAnimation(clip));

            var external = controller.CurrentPlayer;

            using (Assert.Multiple())
            {
                await Assert.That(external).IsNotNull().Because("an NM clip plays on the player owning its skeleton");
                await Assert.That(controller.IsPaused).IsTrue().Because("pause carries onto the player taking over");
                await Assert.That(controller.ActiveAnimation).IsAssignableTo<ClipAnimation>();
            }

            controller.Update(0f);

            var externalSkeletonBones = controller.ExternalSkeletons[clip.SkeletonName].Skeleton;
            var mapped = 0;

            for (var i = 0; i < model.Skeleton.Bones.Length; i++)
            {
                if (externalSkeletonBones[model.Skeleton.Bones[i].Name] is not { } sourceBone)
                {
                    continue;
                }

                mapped++;
                await Assert.That(controller.Pose[i]).IsEqualTo(external!.Pose[sourceBone.Index]).Because($"bone {model.Skeleton.Bones[i].Name}");
            }

            if (mapped == 0)
            {
                // No bone name is shared between the two skeletons, so every bone falls back to bind pose.
                await Assert.That(controller.Pose).IsEquivalentTo(controller.BindPose, CollectionOrdering.Matching);
            }

            controller.SetAnimation(idle);

            using (Assert.Multiple())
            {
                await Assert.That(controller.CurrentPlayer).IsNull().Because("switching back returns to the model player");
                await Assert.That(controller.IsPaused).IsTrue().Because("pause carries back off the external player");
                await Assert.That(controller.ActiveAnimation!.Name).IsEqualTo("box_creature_leggy_idle");
                await Assert.That(external!.Clips).IsEmpty().Because("the outgoing player's mixer is cleared");
            }
        }

        private static AnimationClip FindClipForSkeleton(AnimationClip clip, string skeletonNameSuffix)
        {
            if (clip.SkeletonName.EndsWith(skeletonNameSuffix, StringComparison.Ordinal))
            {
                return clip;
            }

            return clip.SecondaryAnimations.First(c => c.SkeletonName.EndsWith(skeletonNameSuffix, StringComparison.Ordinal));
        }
    }
}
