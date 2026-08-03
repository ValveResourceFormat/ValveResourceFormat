using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace Tests.Renderer
{
    public class AnimationControllerTest
    {
        private static string FilePath(string name)
            => Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", name);

        [Test]
        public void PlayerOwnsPauseStateWhenTheClipFinishes()
        {
            using var resource = new Resource();
            resource.Read(FilePath("box_creature_ik_model.vmdl_c"));
            var model = (Model)resource.DataBlock!;

            var controller = new AnimationController(model.Skeleton, model.FlexControllers)
            {
                Looping = false,
            };

            var walk = model.GetEmbeddedAnimations().First(a => a.Name == "box_creature_leggy_walk");
            controller.SetAnimation(walk);

            // The clip runs 25 frames at 30fps, so a full second overshoots its end and pauses it.
            Assert.That(controller.Update(1f), Is.True);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(controller.IsPaused, Is.True, "finishing the clip pauses the player on the same tick");
                Assert.That(controller.ActiveClipFinished, Is.True);
                Assert.That(controller.Frame, Is.EqualTo(walk.FrameCount - 1));
            }

            Assert.That(controller.Update(0f), Is.False, "a paused controller reports no pose change");
        }

        [Test]
        public void SwitchingPlayersCarriesPauseAndRemapsTheExternalPose()
        {
            using var modelResource = new Resource();
            modelResource.Read(FilePath("box_creature_ik_model.vmdl_c"));
            var model = (Model)modelResource.DataBlock!;

            using var skeletonResource = new Resource();
            skeletonResource.Read(FilePath("ak47.vnmskel_c"));
            var externalSkeleton = Skeleton.FromSkeletonData(((BinaryKV3)skeletonResource.DataBlock!).Data);

            using var clipResource = new Resource();
            clipResource.Read(FilePath("idle_ak.vnmclip_c"));
            var clip = FindClipForSkeleton((AnimationClip)clipResource.DataBlock!, "ak47.vnmskel");

            var controller = new AnimationController(model.Skeleton, model.FlexControllers);
            controller.RegisterExternalSkeleton(clip.SkeletonName, externalSkeleton);

            var idle = model.GetEmbeddedAnimations().First(a => a.Name == "box_creature_leggy_idle");
            controller.SetAnimation(idle);
            controller.Update(0.1f);

            Assert.That(controller.CurrentPlayer, Is.Null, "a model animation plays on the model player");

            controller.IsPaused = true;
            controller.SetAnimation(new ClipAnimation(clip));

            var external = controller.CurrentPlayer;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(external, Is.Not.Null, "an NM clip plays on the player owning its skeleton");
                Assert.That(controller.IsPaused, Is.True, "pause carries onto the player taking over");
                Assert.That(controller.ActiveAnimation, Is.InstanceOf<ClipAnimation>());
            }

            controller.Update(0f);

            var remap = controller.ExternalSkeletons[clip.SkeletonName].RemapTable;
            var mapped = 0;

            for (var i = 0; i < model.Skeleton.Bones.Length; i++)
            {
                if (remap[i] == -1)
                {
                    continue;
                }

                mapped++;
                Assert.That(controller.Pose[i], Is.EqualTo(external!.Pose[remap[i]]), $"bone {model.Skeleton.Bones[i].Name}");
            }

            if (mapped == 0)
            {
                // No bone name is shared between the two skeletons, so every bone falls back to bind pose.
                Assert.That(controller.Pose, Is.EqualTo(controller.BindPose));
            }

            controller.SetAnimation(idle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(controller.CurrentPlayer, Is.Null, "switching back returns to the model player");
                Assert.That(controller.IsPaused, Is.True, "pause carries back off the external player");
                Assert.That(controller.ActiveAnimation!.Name, Is.EqualTo("box_creature_leggy_idle"));
                Assert.That(external!.Clips, Is.Empty, "the outgoing player's mixer is cleared");
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
