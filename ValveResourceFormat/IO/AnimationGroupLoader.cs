using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.IO
{
    public static class AnimationGroupLoader
    {
        public static IEnumerable<Animation> LoadAnimationGroup(Resource resource, IFileLoader fileLoader, Skeleton skeleton, FlexController[] flexControllers)
        {
            var data = resource.DataBlock.AsKeyValueCollection();

            // Get the key to decode the animations
            var decodeKey = data.GetSubCollection("m_decodeKey");

            var animationList = new List<Animation>();

            if (resource.ContainsBlockType(BlockType.ANIM))
            {
                var animBlock = (KeyValuesOrNTRO)resource.GetBlockByType(BlockType.ANIM);
                animationList.AddRange(Animation.FromData(animBlock.Data, decodeKey, skeleton, flexControllers));
                return animationList;
            }

            // Get the list of animation files
            var animArray = data.GetArray<string>("m_localHAnimArray").Where(a => !string.IsNullOrEmpty(a));

            // Load animation files
            foreach (var animationFile in animArray)
            {
                var animResource = fileLoader.LoadFile(animationFile + "_c");

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
