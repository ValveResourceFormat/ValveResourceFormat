using System.Collections.Generic;
using System.IO;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer.Animation
{
    internal static class AnimationGroupLoader
    {
        public static List<ValveResourceFormat.ResourceTypes.Animation.Animation> LoadAnimationGroup(Resource resource, string path)
        {
            var dataBlock = resource.Blocks[BlockType.DATA];
            var data = dataBlock is NTRO ntro
                ? ntro.Output as IKeyValueCollection
                : ((BinaryKV3)dataBlock).Data;

            // Get the list of animation files
            var animArray = data.GetArray<string>("m_localHAnimArray");
            // Get the key to decode the animations
            var decodeKey = data.GetSubCollection("m_decodeKey");

            var animationList = new List<ValveResourceFormat.ResourceTypes.Animation.Animation>();

            // Load animation files
            foreach (var animationFile in animArray)
            {
                var animResource = FileExtensions.LoadFileByAnyMeansNecessary(animationFile + "_c", path, null);

                if (animResource == null)
                {
                    throw new FileNotFoundException($"Failed to load {animationFile}_c. Did you configure game paths correctly?");
                }

                // Build animation classes
                animationList.Add(new ValveResourceFormat.ResourceTypes.Animation.Animation(animResource, decodeKey));
            }

            return animationList;
        }
    }
}
