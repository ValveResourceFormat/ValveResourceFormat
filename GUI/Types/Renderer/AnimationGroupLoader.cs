using System.Collections.Generic;
using System.IO;
using GUI.Types.ParticleRenderer;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    internal static class AnimationGroupLoader
    {
        public static List<ValveResourceFormat.ResourceTypes.ModelAnimation.Animation> LoadAnimationGroup(Resource resource, VrfGuiContext vrfGuiContext)
        {
            var dataBlock = resource.DataBlock;
            var data = dataBlock is NTRO ntro
                ? ntro.Output as IKeyValueCollection
                : ((BinaryKV3)dataBlock).Data;

            // Get the list of animation files
            var animArray = data.GetArray<string>("m_localHAnimArray");
            // Get the key to decode the animations
            var decodeKey = data.GetSubCollection("m_decodeKey");

            var animationList = new List<ValveResourceFormat.ResourceTypes.ModelAnimation.Animation>();

            // Load animation files
            foreach (var animationFile in animArray)
            {
                var animResource = vrfGuiContext.LoadFileByAnyMeansNecessary(animationFile + "_c");

                if (animResource == null)
                {
                    throw new FileNotFoundException($"Failed to load {animationFile}_c. Did you configure game paths correctly?");
                }

                // Build animation classes
                animationList.Add(new ValveResourceFormat.ResourceTypes.ModelAnimation.Animation(animResource, decodeKey));
            }

            return animationList;
        }
    }
}
