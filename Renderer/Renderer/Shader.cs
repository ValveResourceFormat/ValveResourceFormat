using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ThirdParty;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// OpenGL shader program with uniform management and material defaults.
    /// </summary>
    public class Shader
    {
        public string Name { get; }
        public uint NameHash { get; }
        public int Program { get; set; }

        public bool IsLoaded { get; private set; }
        public bool IsValid { get; private set; }

        public required int[] ShaderObjects { get; init; }
        public required IReadOnlyDictionary<string, byte> Parameters { get; init; }
        public required HashSet<string> RenderModes { get; init; }
        public required HashSet<string> UniformNames { get; init; }
        public required HashSet<string> SrgbUniforms { get; init; }
        public HashSet<string> ReservedTexuresUsed { get; } = [];

        private readonly Dictionary<string, (ActiveUniformType Type, int Location, bool SrgbRead)> Uniforms = [];
        public RenderMaterial Default { get; init; }
        protected MaterialLoader MaterialLoader { get; init; }

        public Dictionary<string, int> Attributes { get; } = [];

#if DEBUG
        public required string FileName { get; init; }
#endif

        public Shader(string name, RendererContext rendererContext)
        {
            Name = name;
            NameHash = MurmurHash2.Hash(Name, StringToken.MURMUR2SEED);
            Default = new RenderMaterial(this);
            MaterialLoader = rendererContext.MaterialLoader;
        }

        public bool EnsureLoaded()
        {
            if (!IsLoaded)
            {
                IsLoaded = true;

                GL.GetProgram(Program, GetProgramParameterName.LinkStatus, out var linkStatus);
                IsValid = linkStatus == 1;

                foreach (var obj in ShaderObjects)
                {
                    GL.DetachShader(Program, obj);
                    GL.DeleteShader(obj);
                }

                if (IsValid)
                {
                    StoreAttributeLocations();
                    StoreUniformLocations();
                }
            }

            return IsValid;
        }

        private void StoreUniformLocations()
        {
            var vec4Val = new float[4];

            // Stores uniform types and locations
            var uniforms = GetAllUniformNames();

            // Stores uniform values
            foreach (var uniform in uniforms)
            {
                var name = uniform.Name;
                var type = uniform.Type;
                var index = uniform.Index;
                var size = uniform.Size;

                if (!name.StartsWith("g_", StringComparison.Ordinal) && !name.StartsWith("F_", StringComparison.Ordinal))
                {
                    continue;
                }

                if (size != 1) // arrays
                {
                    continue;
                }

                var isTexture = type is >= ActiveUniformType.Sampler2D and <= ActiveUniformType.SamplerCube;
                var isVector = type is >= ActiveUniformType.FloatVec2 and <= ActiveUniformType.IntVec4;
                var isScalar = type == ActiveUniformType.Float;
                var isBoolean = type == ActiveUniformType.Bool;
                var isInteger = type is ActiveUniformType.Int or ActiveUniformType.UnsignedInt;

                if (isTexture && !Default.Textures.ContainsKey(name))
                {
                    var isReserved = false;

                    foreach (var reserved in MaterialLoader.ReservedTextures)
                    {
                        if (name.Contains(reserved, StringComparison.OrdinalIgnoreCase))
                        {
                            isReserved = true;
                            break;
                        }
                    }

                    if (isReserved)
                    {
                        ReservedTexuresUsed.Add(name);
                        continue;
                    }

                    Default.Textures[name] = name switch
                    {
                        _ when name.Contains("color", StringComparison.OrdinalIgnoreCase) => MaterialLoader.GetErrorTexture(),
                        _ when name.Contains("normal", StringComparison.OrdinalIgnoreCase) => MaterialLoader.GetDefaultNormal(),
                        _ when name.Contains("mask", StringComparison.OrdinalIgnoreCase) => MaterialLoader.GetDefaultMask(),
                        _ => MaterialLoader.GetErrorTexture(),
                    };
                }
                else if (isVector && !Default.Material.VectorParams.ContainsKey(name))
                {
                    vec4Val[0] = vec4Val[1] = vec4Val[2] = vec4Val[3] = 0f;
                    GL.GetUniform(Program, GetUniformLocation(name), vec4Val);
                    Default.Material.VectorParams[name] = new Vector4(vec4Val[0], vec4Val[1], vec4Val[2], vec4Val[3]);
                }
                else if (isScalar && !Default.Material.FloatParams.ContainsKey(name))
                {
                    GL.GetUniform(Program, GetUniformLocation(name), out float flVal);
                    Default.Material.FloatParams[name] = flVal;
                }
                else if ((isBoolean || isInteger) && !Default.Material.IntParams.ContainsKey(name))
                {
                    GL.GetUniform(Program, GetUniformLocation(name), out int intVal);
                    Default.Material.IntParams[name] = intVal;
                }
            }
        }

        public void Use()
        {
            EnsureLoaded();
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
            Uniforms.Clear();

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
                    Uniforms[uniformName] = new(uniformType, uniformLocation, SrgbUniforms.Contains(uniformName));
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

            Uniforms[name] = (0, location, SrgbUniforms.Contains(name));

            return location;
        }

        public int GetUniformBlockIndex(string name)
        {
            if (Uniforms.TryGetValue(name, out var locationType))
            {
                return locationType.Location;
            }

            var location = GL.GetUniformBlockIndex(Program, name);

            Uniforms[name] = (ActiveUniformType.FloatVec4, location, false);

            return location;
        }

        public int GetRegisterSize(string name)
        {
            if (Uniforms.TryGetValue(name, out var uniform))
            {
                return uniform.Type switch
                {
                    ActiveUniformType.FloatVec2 or ActiveUniformType.IntVec2 or ActiveUniformType.UnsignedIntVec2 or ActiveUniformType.BoolVec2 => 2,
                    ActiveUniformType.FloatVec3 or ActiveUniformType.IntVec3 or ActiveUniformType.UnsignedIntVec3 or ActiveUniformType.BoolVec3 => 3,
                    ActiveUniformType.FloatVec4 or ActiveUniformType.IntVec4 or ActiveUniformType.UnsignedIntVec4 or ActiveUniformType.BoolVec4 => 4,
                    _ => 1,
                };
            }

            return 4;
        }

        public bool IsBooleanParameter(string paramName)
        {
            return Uniforms.TryGetValue(paramName, out var uniform) && uniform.Type == ActiveUniformType.Bool;
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
                if (uniform.SrgbRead)
                {
                    value = new Vector4(ColorSpace.SrgbGammaToLinear(value.AsVector3()), value.W);
                }

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

        public void SetUniform3x4(string name, Matrix4x4 value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                var matrix = value.To3x4();
                GL.ProgramUniformMatrix3x4(Program, uniformLocation, false, ref matrix);
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

        public bool SetTexture(int slot, string name, RenderTexture? texture)
        {
            if (texture == null)
            {
                return false;
            }

            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation < 0)
            {
                return false;
            }

            SetTexture(slot, uniformLocation, texture);
            return true;
        }

        public void SetTexture(int slot, int uniformLocation, RenderTexture? texture)
        {
            if (texture == null)
            {
                return;
            }
            GL.BindTextureUnit(slot, texture.Handle);
            GL.ProgramUniform1(Program, uniformLocation, slot);
        }

#if DEBUG
        public void ReplaceWith(Shader shader)
        {
            GL.DeleteProgram(Program);

            IsLoaded = false;
            Program = shader.Program;

            System.Diagnostics.Debug.Assert(shader.ShaderObjects.Length == ShaderObjects.Length);

            for (var i = 0; i < shader.ShaderObjects.Length; i++)
            {
                ShaderObjects[i] = shader.ShaderObjects[i];
            }

            RenderModes.Clear();
            RenderModes.UnionWith(shader.RenderModes);

            Uniforms.Clear();
            Attributes.Clear();
        }
#endif
    }
}
