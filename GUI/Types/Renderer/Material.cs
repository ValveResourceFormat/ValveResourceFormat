using System.Collections.Generic;

namespace GUI.Types.Renderer
{
    public class Material
    {
        public ValveResourceFormat.ResourceTypes.Material Parameters { get; set; }
        public Dictionary<string, int> Textures { get; } = new Dictionary<string, int>();
    }
}
