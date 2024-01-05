using System;
using System.Collections.Generic;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class Shader
    {
        public string Name { get; set; }
        public int Program { get; set; }
        public IReadOnlyDictionary<string, byte> Parameters { get; init; }
        public HashSet<string> RenderModes { get; init; }
        public HashSet<string> SrgbSamplers { get; init; }

        private Dictionary<string, int> Uniforms { get; } = [];
        public RenderMaterial Default;

        public Shader()
        {
            Name = "unnamed";
            Default = new RenderMaterial(this);
        }

        public int NameHash => Name.GetHashCode(StringComparison.OrdinalIgnoreCase);

        public IEnumerable<(string Name, int Index, ActiveUniformType Type, int Size)> GetAllUniformNames()
        {
            GL.GetProgram(Program, GetProgramParameterName.ActiveUniforms, out var count);
            for (var i = 0; i < count; i++)
            {
                var uniformName = GL.GetActiveUniform(Program, i, out var size, out var uniformType);

                yield return (uniformName, i, uniformType, size);
            }
        }

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

        public int GetUniformBlockIndex(string name)
        {
            if (Uniforms.TryGetValue(name, out var value))
            {
                return value;
            }

            value = GL.GetUniformBlockIndex(Program, name);

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

        public void SetUniformMatrix4x3Array(string name, int count, float[] value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.UniformMatrix4x3(uniformLocation, count, false, value);
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

        public bool SetTexture(int slot, string name, RenderTexture texture)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation < 0)
            {
                return false;
            }

            SetTexture(slot, uniformLocation, texture);
            return true;
        }

        public static void SetTexture(int slot, int uniformLocation, RenderTexture texture)
        {
            GL.ActiveTexture(TextureUnit.Texture0 + slot);
            texture.Bind();
            GL.Uniform1(uniformLocation, slot);
        }
    }
}
