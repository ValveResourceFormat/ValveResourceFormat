using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    public class Shader
    {
        public string Name { get; set; }
        public IDictionary<string, bool> Parameters { get; set; }
        public int Program { get; set; }
        public List<string> Defines { get; set; }
        public List<string> RenderModes { get; set; }

        private Dictionary<string, int> Uniforms { get; } = new Dictionary<string, int>();

        public int GetUniformLocation(string name)
        {
            int value;

            if (!Uniforms.TryGetValue(name, out value))
            {
                value = GL.GetUniformLocation(Program, name);

                Uniforms[name] = value;
            }

            return value;
        }
    }
}
