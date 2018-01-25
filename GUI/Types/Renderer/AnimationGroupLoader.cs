using System;
using System.Collections.Generic;
using System.IO;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;

namespace GUI.Types.Renderer.Animation
{
    internal static class AnimationGroupLoader
    {
        public static List<ValveResourceFormat.ResourceTypes.Animation.Animation> LoadAnimationGroup(Resource resource, string path)
        {
            var data = (NTRO)resource.Blocks[BlockType.DATA];

            // Get the list of animation files
            var animArray = (NTROArray)data.Output["m_localHAnimArray"];
            // Get the key to decode the animations
            var decodeKey = ((NTROValue<NTROStruct>)data.Output["m_decodeKey"]).Value;

            var animationList = new List<ValveResourceFormat.ResourceTypes.Animation.Animation>();

            // Load animation files
            foreach (var t in animArray)
            {
                var refAnim = ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)t).Value;
                var animResource = FileExtensions.LoadFileByAnyMeansNecessary(refAnim.Name + "_c", path, null);

                if (animResource == null)
                {
                    throw new FileNotFoundException($"Failed to load {refAnim.Name}_c. Did you configure game paths correctly?");
                }

                // Build animation classes
                animationList.Add(new ValveResourceFormat.ResourceTypes.Animation.Animation(animResource, decodeKey));
            }

            return animationList;
        }
    }
}
