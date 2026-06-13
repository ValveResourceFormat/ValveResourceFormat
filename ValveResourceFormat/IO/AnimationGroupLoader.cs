using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Loads animation data from animation group resources.
    /// </summary>
    public static class AnimationGroupLoader
    {
        /// <summary>
        /// Loads all animations from an animation group resource.
        /// </summary>
        /// <param name="resource">The animation group resource to load from.</param>
        /// <param name="fileLoader">File loader for loading external animation files.</param>
        /// <param name="skeleton">The skeleton to apply animations to.</param>
        /// <param name="flexControllers">Flex controllers for facial animations.</param>
        /// <returns>Collection of loaded animations.</returns>
        public static IEnumerable<Animation> LoadAnimationGroup(Resource resource, IFileLoader fileLoader, Skeleton skeleton, FlexController[] flexControllers)
        {
            var dataBlock = resource.DataBlock;

            if (dataBlock == null)
            {
                return [];
            }

            var data = dataBlock.AsKeyValueCollection();

            // Get the key to decode the animations
            var decodeKey = data.GetSubCollection("m_decodeKey");

            var animationList = new List<Animation>();
            var animBlock = (KeyValuesOrNTRO?)resource.GetBlockByType(BlockType.ANIM);

            if (animBlock != null)
            {
                animationList.AddRange(Animation.FromData(animBlock.Data, decodeKey, skeleton, flexControllers));
                return animationList;
            }

            // Get the list of animation files
            var animArray = data.GetArray<string>("m_localHAnimArray")!.Where(a => !string.IsNullOrEmpty(a));

            // Load animation files
            foreach (var animationFile in animArray)
            {
                var animResource = fileLoader.LoadFileCompiled(animationFile);

                if (animResource != null)
                {
                    // Build animation classes
                    animationList.AddRange(Animation.FromResource(animResource, decodeKey, skeleton, flexControllers));
                }
            }

            return animationList;
        }
    }
}
