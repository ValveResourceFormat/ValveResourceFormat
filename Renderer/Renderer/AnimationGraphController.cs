using System.Diagnostics;
using System.IO;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.ThirdParty;

namespace ValveResourceFormat.Renderer
{
    public class AnimationGraphController : AnimationController
    {
        public Skeleton Skeleton2 { get; }
        public Animation Animation { get; }

        AnimationController SequencePlayback { get; }

        private readonly int[] nmSkelToModelSkeleton;
        private readonly Dictionary<string, string?> boneRemapDebugNames = [];

        public AnimationGraphController(Skeleton modelSkeleton, NmGraphDefinition graphDefinition, GameFileLoader fileLoader)
            : base(modelSkeleton, [])
        {
            var graph = graphDefinition.Data;

            // Load animated skeleton
            var skeletonName = graph.GetProperty<string>("m_skeleton");
            var res = fileLoader.LoadFileCompiled(skeletonName) ?? throw new InvalidDataException($"Skeleton file '{skeletonName}' could not be found.");
            Skeleton2 = Skeleton.FromSkeletonData(((BinaryKV3)res.DataBlock!).Data);

            // Load first clip
            var resources = graph.GetArray<string>("m_resources");
            Debug.Assert(resources.Length > 0, "No resources in animation graph definition.");
            var firstClipName = resources[0];

            var clip = fileLoader.LoadFileCompiled(firstClipName) ?? throw new InvalidDataException($"Animation clip file '{firstClipName}' could not be found.");

            Animation = new Animation((AnimationClip)clip.DataBlock!);

            // test sequence playback for the ag2 anim
            SequencePlayback = new AnimationController(Skeleton2, []);
            SequencePlayback.SetAnimation(Animation);

            {
                // Build bone remap table
                var sourceBoneCount = Skeleton2.Bones.Length;
                var destinationBoneCount = Skeleton.Bones.Length;
                nmSkelToModelSkeleton = new int[destinationBoneCount];
                var nameToIndex = new Dictionary<uint, int>(sourceBoneCount);

                for (var i = 0; i < sourceBoneCount; i++)
                {
                    var name = Skeleton2.Bones[i].Name;
                    nameToIndex[StringToken.Store(name)] = i;
                }

                for (var i = 0; i < destinationBoneCount; i++)
                {
                    var name = Skeleton.Bones[i].Name;
                    var hash = StringToken.Store(name);

                    nmSkelToModelSkeleton[i] = -1;
                    boneRemapDebugNames[name] = null;

                    if (nameToIndex.TryGetValue(hash, out var idx))
                    {
                        nmSkelToModelSkeleton[i] = idx;
                        boneRemapDebugNames[name] = Skeleton2.Bones[idx].Name;
                    }
                }
            }
        }

        public override void SetAnimation(Animation? animation)
        {
            // Ignore
        }

        public override bool Update(float timeStep)
        {
            var updated = SequencePlayback.Update(timeStep);

            if (updated)
            {
                for (var i = 0; i < Pose.Length; i++)
                {
                    var srcIdx = nmSkelToModelSkeleton[i];
                    if (srcIdx >= 0 && srcIdx < SequencePlayback.Pose.Length)
                    {
                        Pose[i] = SequencePlayback.Pose[srcIdx];
                        continue;
                    }

                    // If bone not found, fallback to bind pose or leave unchanged
                    // Pose[i] = SequencePlayback.BindPose.Length > i ? SequencePlayback.BindPose[i] : Pose[i];
                }
            }

            return updated;
        }
    }
}
