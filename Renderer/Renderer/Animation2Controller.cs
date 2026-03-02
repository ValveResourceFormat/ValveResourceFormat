using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Manages skeletal animation playback for nmclip resources.
    /// </summary>
    public class Animation2Controller : BaseAnimationController
    {
        /// <summary>
        /// The skeleton being animated.
        /// </summary>
        public Skeleton Skeleton2 { get; }

        private AnimationController Sequence { get; }
        private readonly int[] nmSkelToModelSkeleton;
        private readonly Dictionary<string, string?> boneRemapDebugNames = [];

        /// <param name="skeleton">The renderable model skeleton.</param>
        public Animation2Controller(Skeleton skeleton) : base(skeleton)
        {

            {
                // The animated skeleton bones have to be matched to renderable skeleton bones
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
    }
}
