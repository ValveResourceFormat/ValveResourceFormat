using System.Collections.Generic;

namespace GUI.Types.Renderer
{
    internal class Shader
    {
        public int Program { get; set; }
        public Dictionary<string, int> Uniforms { get; } = new Dictionary<string, int>();

        public int GetUniformLocation(string name)
        {
            int value;

            if (Uniforms.TryGetValue(name, out value))
            {
                return value;
            }

            return -1;
        }
    }
}
