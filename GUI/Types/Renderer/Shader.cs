using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    public class Shader
    {
        public string Name { get; set; }
        public int Program { get; set; }
#pragma warning disable CA2227 // Collection properties should be read only
        public IDictionary<string, bool> Parameters { get; set; }
        public List<string> RenderModes { get; set; }
#pragma warning restore CA2227 // Collection properties should be read only

        private Dictionary<string, int> Uniforms { get; } = new Dictionary<string, int>();

        public int GetUniformLocation(string name)
        {
            if (Uniforms.TryGetValue(name, out var value))
            {
                return value;
            }

            value = GL.GetUniformLocation(Program, name);

            Uniforms[name] = value;

            return value;
        }
    }
}
