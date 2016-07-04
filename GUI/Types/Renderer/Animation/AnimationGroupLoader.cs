using System;
using System.Collections.Generic;
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
            for (var i = 0; i < animArray.Count; i++)
            {
                // Load animation files
                var refAnim = ((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)animArray[i]).Value;
                var animResource = FileExtensions.LoadFileByAnyMeansNecessary(refAnim.Name + "_c", path, null);
#if DEBUG
                Console.WriteLine("Animation found: " + refAnim.Name);
#endif

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
