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
    internal class AnimationGroupLoader
    {
        private readonly NTRO data;
        public List<Animation> AnimationList { get; private set; }

        public AnimationGroupLoader(Resource resource, string filename, Skeleton skeleton)
        {
            data = (NTRO)resource.Blocks[BlockType.DATA];
            LoadAnimationGroup(filename, skeleton);
        }

        private void LoadAnimationGroup(string path, Skeleton skeleton)
        {
            // Get the list of animation files
            var animArray = (NTROArray)data.Output["m_localHAnimArray"];
            // Get the key to decode the animations
            var decodeKey = ((NTROValue<NTROStruct>)data.Output["m_decodeKey"]).Value;

            AnimationList = new List<Animation>();

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
                AnimationList.Add(new Animation(animResource, decodeKey, skeleton));
            }
        }

        private void HandleDecodeKey(NTROStruct decodeKey)
        {
            // TODO?
        }
    }
}
