using System;
using System.Collections.Generic;
using System.IO;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.NTROSerialization;

namespace GUI.Types.Renderer
{
    class AnimationGroupLoader
    {
        private NTRO data;
        public List<ResourceExtRefList.ResourceReferenceInfo> animationList;

        public AnimationGroupLoader(Resource resource, string filename)
        {
            data = (NTRO) resource.Blocks[BlockType.DATA];
            loadAnimationGroup();
        }

        private void loadAnimationGroup()
        {
            var animArray = (NTROArray)data.Output["m_localHAnimArray"];

            animationList = new List<ResourceExtRefList.ResourceReferenceInfo>();
            for (int i = 0; i < animArray.Count; i++)
            {
                animationList.Add(((NTROValue<ResourceExtRefList.ResourceReferenceInfo>)animArray[i]).Value);
            }

            foreach(var animation in animationList)
            {
                var animClean = Path.GetFileNameWithoutExtension(animation.Name);
                Console.WriteLine("Animation found: " + animClean);
            }
        }
    }
}
