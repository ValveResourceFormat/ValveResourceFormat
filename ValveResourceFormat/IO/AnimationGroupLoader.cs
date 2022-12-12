using System;
using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.IO
{
    public static class AnimationGroupLoader
    {
        public static IEnumerable<Animation> LoadAnimationGroup(Resource resource, IFileLoader fileLoader)
        {
            var data = resource.DataBlock.AsKeyValueCollection();

            // Get the key to decode the animations
            var decodeKey = data.GetSubCollection("m_decodeKey");

            var animationList = new List<Animation>();

            if (resource.ContainsBlockType(BlockType.ANIM))
            {
                var animBlock = (KeyValuesOrNTRO)resource.GetBlockByType(BlockType.ANIM);
                animationList.AddRange(Animation.FromData(animBlock.Data, decodeKey));
                return animationList;
            }

            // Get the list of animation files
            var animArray = data.GetArray<string>("m_localHAnimArray").Where(a => !string.IsNullOrEmpty(a));

            // Load animation files
            foreach (var animationFile in animArray)
            {
                animationList.AddRange(LoadAnimationFile(animationFile, decodeKey, fileLoader));
            }

            return animationList;
        }

        private static IEnumerable<Animation> LoadAnimationFile(string animationFile, IKeyValueCollection decodeKey, IFileLoader fileLoader)
        {
            var animResource = fileLoader.LoadFile(animationFile + "_c");

            if (animResource == null)
            {
                return Enumerable.Empty<Animation>();
            }

            // Build animation classes
            return Animation.FromResource(animResource, decodeKey);
        }
    }
}
