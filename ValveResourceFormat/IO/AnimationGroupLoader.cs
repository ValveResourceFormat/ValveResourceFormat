using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO
{
    public static class AnimationGroupLoader
    {
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
            var animArray = data.GetArray<string>("m_localHAnimArray").Where(a => !string.IsNullOrEmpty(a));

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
