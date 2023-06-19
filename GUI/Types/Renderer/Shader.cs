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

        // One caviat for these functions: they MUST be used when this shader is the active program.
        public bool SetUniform1(string name, float value)
        {
            var uniformLocation = GetUniformLocation(name);

            if (uniformLocation > -1)
            {
                GL.Uniform1(uniformLocation, value);
            }

            return uniformLocation > -1;
        }
        public bool SetUniform1(string name, bool value)
        {
            var uniformLocation = GetUniformLocation(name);

            if (uniformLocation > -1)
            {
                GL.Uniform1(uniformLocation, value ? 1 : 0);
            }

            return uniformLocation > -1;
        }
        public bool SetUniform1(string name, int value)
        {
            var uniformLocation = GetUniformLocation(name);

            if (uniformLocation > -1)
            {
                GL.Uniform1(uniformLocation, value);
            }

            return uniformLocation > -1;
        }
        public bool SetUniform1(string name, uint value)
        {
            var uniformLocation = GetUniformLocation(name);

            if (uniformLocation > -1)
            {
                GL.Uniform1(uniformLocation, value);
            }

            return uniformLocation > -1;
        }
        public bool SetUniform2(string name, Vector2 value)
        {
            var uniformLocation = GetUniformLocation(name);

            if (uniformLocation > -1)
            {
                GL.Uniform2(uniformLocation, value.ToOpenTK());
            }

            return uniformLocation > -1;
        }
        public bool SetUniform3(string name, Vector3 value)
        {
            var uniformLocation = GetUniformLocation(name);

            if (uniformLocation > -1)
            {
                GL.Uniform3(uniformLocation, value.ToOpenTK());
            }

            return uniformLocation > -1;
        }
        public bool SetUniform4(string name, Vector4 value)
        {
            var uniformLocation = GetUniformLocation(name);

            if (uniformLocation > -1)
            {
                GL.Uniform4(uniformLocation, value.ToOpenTK());
            }

            return uniformLocation > -1;
        }
        public bool SetUniform4Array(string name, int count, float[] value)
        {
            var uniformLocation = GetUniformLocation(name);

            if (uniformLocation > -1)
            {
                GL.Uniform4(uniformLocation, count, value);
            }

            return uniformLocation > -1;
        }

        public bool SetUniform4x4(string name, Matrix4x4 value, bool transpose = false)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                var matrix = value.ToOpenTK();

                GL.UniformMatrix4(uniformLocation, transpose, ref matrix);
            }

            return uniformLocation > -1;
        }

        public bool SetTexture(int slot, string name, int texture, TextureTarget target = TextureTarget.Texture2D)
        {
            var uniformLocation = GetUniformLocation(name);

            if (uniformLocation > -1)
            {
                //Console.WriteLine($"Uniform Location {name}, slot {slot} (ID {texture}): {uniformLocation}");
                GL.ActiveTexture(TextureUnit.Texture0 + slot);
                GL.BindTexture(target, texture);
                GL.Uniform1(uniformLocation, slot);
            }

            return uniformLocation > -1;
        }

        private bool hasBeenValidated { get; set; }
        public void Validate()
        {
            if (!hasBeenValidated)
            {
                GL.ValidateProgram(Program);
                GL.GetProgram(Program, GetProgramParameterName.ValidateStatus, out var validateStatus);

                if (validateStatus != 1)
                {
                    GL.GetProgramInfoLog(Program, out var programLog);
                    throw new InvalidProgramException($"Error validating shader \"{Name}\": {programLog}");
                }
                hasBeenValidated = true;
            }
        }
    }
}
