using System;
using System.Collections.Generic;
using System.IO;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;

namespace GUI.Types.Renderer
{
    internal class AnimationGroupLoader
    {
        private readonly NTRO data;
        public List<ResourceExtRefList.ResourceReferenceInfo> AnimationList { get; private set; }

        public AnimationGroupLoader(Resource resource, string filename)
        {
            data = (NTRO)resource.Blocks[BlockType.DATA];
            LoadAnimationGroup();
        }

        private void LoadAnimationGroup()
        {
            var animArray = (NTROArray)data.Output["m_localHAnimArray"];

            AnimationList = new List<ResourceExtRefList.ResourceReferenceInfo>();
            for (var i = 0; i < animArray.Count; i++)
            {
                AnimationList.Add(((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)animArray[i]).Value);
            }

            foreach (var animation in AnimationList)
            {
                var animClean = Path.GetFileNameWithoutExtension(animation.Name);
                Console.WriteLine("Animation found: " + animClean);
            }
        }
    }
}
