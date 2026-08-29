using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    public class SequenceAnimationTest
    {
        // box_creature_leggy_walk travels ~47.92 source units forward (+X) over its 25-frame cycle,
        // stored in the movement array (root motion), not in the bone frames.
        private const float FullDisplacementX = 47.92f;

        [Test]
        public async Task TestEmbeddedAnimations()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "box_creature_ik_model.vmdl_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var model = (Model)resource.DataBlock!;

            var animGroupPaths = model.GetReferencedAnimationGroupNames();
            var animations = model.GetEmbeddedAnimations().ToList();

            using (Assert.Multiple())
            {
                await Assert.That(animGroupPaths.Count()).IsZero();
                await Assert.That(animations).Count().IsEqualTo(3);
                await Assert.That(animations).All(animation => animation is SequenceAnimation);

                await Assert.That(animations[0].Name).IsEqualTo("ref_pose");
                await Assert.That(animations[0].Fps).IsEqualTo(30);
                await Assert.That(animations[0].FrameCount).IsEqualTo(1);

                await Assert.That(animations[1].Name).IsEqualTo("box_creature_leggy_idle");
                await Assert.That(animations[1].Fps).IsEqualTo(30);
                await Assert.That(animations[1].FrameCount).IsEqualTo(49);

                await Assert.That(animations[2].Name).IsEqualTo("box_creature_leggy_walk");
                await Assert.That(animations[2].Fps).IsEqualTo(30);
                await Assert.That(animations[2].FrameCount).IsEqualTo(25);
            }
        }

        [Test]
        public async Task LoadsExternalAnimationGroupAndDecodesPose()
        {
            var vpkPath = Path.Combine(TestContext.TestDirectory!, "Files", "hand_l_v3_anim_group.vpk");

            using var package = new Package();
            package.Read(vpkPath);

            using var loader = new GameFileLoader(package, vpkPath);
            using var resource = loader.LoadFileCompiled("models/characters/avatars/hand_l_v3.vmdl");
            var model = (Model)resource!.DataBlock!;

            var skeleton = model.Skeleton;

            using (Assert.Multiple())
            {
                await Assert.That(skeleton.Bones).Count().IsEqualTo(20);
                await Assert.That(skeleton.Bones[0].Name).IsEqualTo("handL_root");
                await Assert.That(skeleton.Bones[3].Name).IsEqualTo("handL_midd_1");
            }

            // The group references five animation files; the package carries one of them,
            // and the missing ones are skipped rather than failing the whole group.
            var animations = model.GetAllAnimations(loader).ToList();

            using (Assert.Multiple())
            {
                await Assert.That(animations.Select(a => a.Name)).IsEquivalentTo(["idle_to_fist"]);
                await Assert.That(animations[0]).IsTypeOf<SequenceAnimation>();
                await Assert.That(animations[0].Fps).IsEqualTo(30);
                await Assert.That(animations[0].FrameCount).IsEqualTo(10);
            }

            var frameCache = new AnimationFrameCache(skeleton, []);
            var firstFrame = frameCache.GetFrame(animations[0], 0);
            var lastFrame = frameCache.GetFrame(animations[0], 9);

            using (Assert.Multiple())
            {
                // The stored position decompresses to slightly off the bind pose value of -1.6160,
                // so this pins that the compressed position was actually decoded.
                await Assert.That(firstFrame.Bones[3].Position.X).IsEqualTo(-1.6152344f).Within(0.0001f);

                // The middle finger curls in as the fist closes.
                await Assert.That(firstFrame.Bones[3].Angle.Z).IsEqualTo(0.0459205f).Within(0.0001f);
                await Assert.That(lastFrame.Bones[3].Angle.Z).IsEqualTo(0.7130554f).Within(0.0001f);
                await Assert.That(lastFrame.Bones[3].Angle.W).IsEqualTo(0.7011077f).Within(0.0001f);
                await Assert.That(lastFrame.Bones[3].Scale).IsEqualTo(1f).Within(0.0001f);
            }
        }

        [Test]
        public async Task MovementOffsetReachesFullDisplacement()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "box_creature_ik_model.vmdl_c"));
            var model = (Model)resource.DataBlock!;
            var anim = model.GetAllAnimations(new NullFileLoader()).First(a => a.Name == "box_creature_leggy_walk");

            await Assert.That(anim.HasMovementData()).IsTrue();

            var lastFrame = anim.FrameCount - 1;
            var byFrame = anim.GetMovementOffsetData(lastFrame);
            var byTime = anim.GetMovementOffsetData(lastFrame / anim.Fps);

            using (Assert.Multiple())
            {
                // Both overloads must reach the full displacement; the time-based one previously under-shot.
                await Assert.That(byFrame.Position.X).IsEqualTo(FullDisplacementX).Within(0.05f);
                await Assert.That(byTime.Position.X).IsEqualTo(FullDisplacementX).Within(0.05f);
            }
        }
    }
}
