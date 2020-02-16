using System.Collections.Generic;

namespace GUI.Types.Renderer
{
    internal interface IAnimationRenderer
    {
        IEnumerable<string> GetSupportedAnimationNames();

        void SetAnimation(string animationName);
    }
}
