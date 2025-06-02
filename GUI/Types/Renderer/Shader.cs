using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class Shader
    {
        public string Name { get; init; }
        public int Program { get; set; }
        public required IReadOnlyDictionary<string, byte> Parameters { get; init; }
        public required HashSet<string> RenderModes { get; init; }
        public required HashSet<string> SrgbSamplers { get; init; }

        private readonly Dictionary<string, (ActiveUniformType Type, int Location)> Uniforms = [];
        public RenderMaterial Default;

        public readonly Dictionary<string, int> Attributes = [];

#if DEBUG
        public required string FileName { get; init; }
#endif

        public Shader()
        {
            Name = "unnamed";
            Default = new RenderMaterial(this);
        }

        public int NameHash => Name.GetHashCode(StringComparison.OrdinalIgnoreCase);

        public void Use()
        {
            GL.UseProgram(Program);
        }

        public IEnumerable<(string Name, int Index, ActiveUniformType Type, int Size)> GetAllUniformNames()
        {
            var uniformBlockMemberIndices = new List<int>();

            GL.GetProgram(Program, GetProgramParameterName.ActiveUniformBlocks, out var uniformBlockCount);

            for (var i = 0; i < uniformBlockCount; i++)
            {
                GL.GetActiveUniformBlock(Program, i, ActiveUniformBlockParameter.UniformBlockActiveUniforms, out var activeUniformsCount);

                var uniformIndices = new int[activeUniformsCount];
                GL.GetActiveUniformBlock(Program, i, ActiveUniformBlockParameter.UniformBlockActiveUniformIndices, uniformIndices);
                uniformBlockMemberIndices.AddRange(uniformIndices);
            }

            GL.GetProgram(Program, GetProgramParameterName.ActiveUniforms, out var count);

            Uniforms.EnsureCapacity(count - uniformBlockMemberIndices.Count);

            for (var i = 0; i < count; i++)
            {
                if (uniformBlockMemberIndices.Contains(i))
                {
                    continue;
                }

                var uniformName = GL.GetActiveUniform(Program, i, out var size, out var uniformType);
                var uniformLocation = GL.GetUniformLocation(Program, uniformName);

                if (uniformLocation > -1)
                {
                    Uniforms[uniformName] = (uniformType, uniformLocation);
                }

                yield return (uniformName, i, uniformType, size);
            }
        }

        public void StoreAttributeLocations()
        {
            GL.GetProgram(Program, GetProgramParameterName.ActiveAttributes, out var attributeCount);

            Attributes.EnsureCapacity(attributeCount);

            for (var i = 0; i < attributeCount; i++)
            {
                GL.GetActiveAttrib(Program, i, 64, out var length, out var size, out var type, out var name);
                var attribLocation = GL.GetAttribLocation(Program, name);
                Attributes[name] = attribLocation;
            }
        }

        public int GetUniformLocation(string name)
        {
            if (Uniforms.TryGetValue(name, out var locationType))
            {
                return locationType.Location;
            }

            var location = GL.GetUniformLocation(Program, name);

            Uniforms[name] = (ActiveUniformType.FloatVec4, location);

            return location;
        }

        public int GetUniformBlockIndex(string name)
        {
            if (Uniforms.TryGetValue(name, out var locationType))
            {
                return locationType.Location;
            }

            var location = GL.GetUniformBlockIndex(Program, name);

            Uniforms[name] = (ActiveUniformType.FloatVec4, location);

            return location;
        }

        public void SetUniform1(string name, float value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform1(Program, uniformLocation, value);
            }
        }

        public void SetUniform1(string name, int value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform1(Program, uniformLocation, value);
            }
        }

        public void SetUniform1(string name, bool value) => SetUniform1(name, value ? 1u : 0u);

        public void SetUniform1(string name, uint value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform1((uint)Program, uniformLocation, value);
            }
        }

        public void SetUniform2(string name, Vector2 value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform2(Program, uniformLocation, value.X, value.Y);
            }
        }

        public void SetUniform3(string name, Vector3 value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform3(Program, uniformLocation, value.X, value.Y, value.Z);
            }
        }

        public void SetUniform4(string name, Vector4 value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform4(Program, uniformLocation, value.X, value.Y, value.Z, value.W);
            }
        }

        public void SetMaterialVector4Uniform(string name, Vector4 value)
        {
            if (Uniforms.TryGetValue(name, out var uniform) && uniform.Location > -1)
            {
                if (uniform.Type == ActiveUniformType.FloatVec3)
                {
                    GL.ProgramUniform3(Program, uniform.Location, value.X, value.Y, value.Z);
                }
                else if (uniform.Type is ActiveUniformType.FloatVec2)
                {
                    GL.ProgramUniform2(Program, uniform.Location, value.X, value.Y);
                }
                else if (uniform.Type == ActiveUniformType.FloatVec4)
                {
                    GL.ProgramUniform4(Program, uniform.Location, value.X, value.Y, value.Z, value.W);
                }
                else if (uniform.Type is ActiveUniformType.IntVec4 or ActiveUniformType.UnsignedIntVec4 or ActiveUniformType.BoolVec4)
                {
                    GL.ProgramUniform4(Program, uniform.Location, (uint)value.X, (uint)value.Y, (uint)value.Z, (uint)value.W);
                }
            }
        }

        public void SetBoneAnimationData(bool animated, int boneOffset = 0, int boneCount = 0, int weightCount = 0)
        {
            var uniformLocation = GetUniformLocation("uAnimationData");
            if (uniformLocation > -1)
            {
                GL.ProgramUniform4((uint)Program, uniformLocation, animated ? 1u : 0u, (uint)boneOffset, (uint)boneCount, (uint)weightCount);
            }
        }

        public void SetUniform4Array(string name, int count, float[] value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform4(Program, uniformLocation, count, value);
            }
        }

        public void SetUniformMatrix4x3Array(string name, int count, float[] value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniformMatrix4x3(Program, uniformLocation, count, false, value);
            }
        }

        public void SetUniform4x4(string name, Matrix4x4 value, bool transpose = false)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                var matrix = value.ToOpenTK();
                GL.ProgramUniformMatrix4(Program, uniformLocation, transpose, ref matrix);
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

        public void SetTexture(int slot, int uniformLocation, RenderTexture texture)
        {
            GL.BindTextureUnit(slot, texture.Handle);
            GL.ProgramUniform1(Program, uniformLocation, slot);
        }

#if DEBUG
        public void ClearUniformsCache() => Uniforms.Clear();
#endif
    }
}
