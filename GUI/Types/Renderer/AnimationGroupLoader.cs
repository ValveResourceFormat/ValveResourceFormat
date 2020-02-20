using System.Collections.Generic;
using System.IO;
using System.Linq;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    internal static class AnimationGroupLoader
    {
        public static List<Animation> LoadAnimationGroup(Resource resource, VrfGuiContext vrfGuiContext)
        {
            var dataBlock = resource.DataBlock;
            var data = dataBlock is NTRO ntro
                ? ntro.Output as IKeyValueCollection
                : ((BinaryKV3)dataBlock).Data;

            // Get the list of animation files
            var animArray = data.GetArray<string>("m_localHAnimArray").Where(a => a != null);
            // Get the key to decode the animations
            var decodeKey = data.GetSubCollection("m_decodeKey");

            var animationList = new List<Animation>();

            // Load animation files
            foreach (var animationFile in animArray)
            {
                var animResource = vrfGuiContext.LoadFileByAnyMeansNecessary(animationFile + "_c");

                if (animResource == null)
                {
                    throw new FileNotFoundException($"Failed to load {animationFile}_c. Did you configure game paths correctly?");
                }

                // Build animation classes
                animationList.AddRange(Animation.FromResource(animResource, decodeKey));
            }

            return animationList;
        }
    }
}
