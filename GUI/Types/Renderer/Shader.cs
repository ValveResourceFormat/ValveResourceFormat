using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;

namespace GUI.Types.Renderer
{
    internal class Shader
    {
        public string Name { get; set; }
        public ArgumentDependencies Parameters { get; set; }
        public int Program { get; set; }
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
