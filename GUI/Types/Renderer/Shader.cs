using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class Shader
    {
        public string Name { get; set; }
        public int Program { get; set; }
        public IReadOnlyDictionary<string, byte> Parameters { get; init; }
        public List<string> RenderModes { get; init; }

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
