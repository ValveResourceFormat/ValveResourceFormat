using System;
using System.Numerics;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using GUI.Utils;

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

        public void SetUniform1(string name, float value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.Uniform1(uniformLocation, value);
            }
        }

        public void SetUniform1(string name, int value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.Uniform1(uniformLocation, value);
            }
        }

        public void SetUniform1(string name, uint value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.Uniform1(uniformLocation, value);
            }
        }

        public void SetUniform2(string name, Vector2 value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.Uniform2(uniformLocation, value.ToOpenTK());
            }
        }

        public void SetUniform3(string name, Vector3 value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.Uniform3(uniformLocation, value.ToOpenTK());
            }
        }

        public void SetUniform4(string name, Vector4 value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.Uniform4(uniformLocation, value.ToOpenTK());
            }
        }

        public void SetUniform4Array(string name, int count, float[] value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.Uniform4(uniformLocation, count, value);
            }
        }

        public void SetUniform4x4(string name, Matrix4x4 value, bool transpose = false)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                var matrix = value.ToOpenTK();
                GL.UniformMatrix4(uniformLocation, transpose, ref matrix);
            }
        }

        public bool SetTexture(int slot, string name, int texture, TextureTarget target = TextureTarget.Texture2D)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation < 0)
            {
                return false;
            }

            GL.ActiveTexture(TextureUnit.Texture0 + slot);
            GL.BindTexture(target, texture);
            GL.Uniform1(uniformLocation, slot);

            return true;
        }
    }
}
