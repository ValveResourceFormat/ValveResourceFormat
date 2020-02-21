using System.Collections.Generic;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    public class RenderMaterial
    {
        public Material Material { get; set; }
        public Dictionary<string, int> Textures { get; } = new Dictionary<string, int>();
    }
}
