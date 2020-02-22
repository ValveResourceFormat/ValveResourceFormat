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
        public static IEnumerable<Animation> LoadAnimationGroup(Resource resource, VrfGuiContext vrfGuiContext)
        {
            var data = GetData(resource);

            // Get the list of animation files
            var animArray = data.GetArray<string>("m_localHAnimArray").Where(a => a != null);
            // Get the key to decode the animations
            var decodeKey = data.GetSubCollection("m_decodeKey");

            var animationList = new List<Animation>();

            // Load animation files
            foreach (var animationFile in animArray)
            {
                animationList.AddRange(LoadAnimationFile(animationFile, decodeKey, vrfGuiContext));
            }

            return animationList;
        }

        public static IEnumerable<Animation> TryLoadSingleAnimationFileFromGroup(Resource resource, string animationName, VrfGuiContext vrfGuiContext)
        {
            var data = GetData(resource);

            // Get the list of animation files
            var animArray = data.GetArray<string>("m_localHAnimArray").Where(a => a != null);
            // Get the key to decode the animations
            var decodeKey = data.GetSubCollection("m_decodeKey");

            var animation = animArray.FirstOrDefault(a => a != null && a.EndsWith($"{animationName}.vanim"));

            if (animation != default)
            {
                return LoadAnimationFile(animation, decodeKey, vrfGuiContext);
            }
            else
            {
                return null;
            }
        }

        private static IKeyValueCollection GetData(Resource resource)
            => resource.DataBlock is NTRO ntro
                ? ntro.Output as IKeyValueCollection
                : ((BinaryKV3)resource.DataBlock).Data;

        private static IEnumerable<Animation> LoadAnimationFile(string animationFile, IKeyValueCollection decodeKey, VrfGuiContext vrfGuiContext)
        {
            var animResource = vrfGuiContext.LoadFileByAnyMeansNecessary(animationFile + "_c");

            if (animResource == null)
            {
                throw new FileNotFoundException($"Failed to load {animationFile}_c. Did you configure game paths correctly?");
            }

            // Build animation classes
            return Animation.FromResource(animResource, decodeKey);
        }
    }
}
