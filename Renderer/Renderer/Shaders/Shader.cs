using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ThirdParty;

namespace ValveResourceFormat.Renderer.Shaders
{
    /// <summary>
    /// OpenGL shader program with uniform management and material defaults.
    /// </summary>
    public class Shader
    {
        /// <summary>Gets the shader name (typically a Source 2 <c>.vfx</c> shader name).</summary>
        public string Name { get; }

        /// <summary>Gets the MurmurHash2 hash of <see cref="Name"/>.</summary>
        public uint NameHash { get; }

        /// <summary>Gets or sets the OpenGL program object handle.</summary>
        public int Program { get; set; }

        /// <summary>Gets a value indicating whether the shader link status has been checked.</summary>
        public bool IsLoaded { get; private set; }

        /// <summary>Gets a value indicating whether the shader linked successfully and is ready for use.</summary>
        public bool IsValid { get; private set; }

        /// <summary>Gets the compiled OpenGL shader stage object handles.</summary>
        public required int[] ShaderObjects { get; init; }

        /// <summary>Gets the static combo parameter values used to compile this shader variant.</summary>
        public required IReadOnlyDictionary<string, byte> Parameters { get; init; }

        /// <summary>Gets the set of render mode names supported by this shader.</summary>
        public required HashSet<string> RenderModes { get; init; }

        /// <summary>Gets the set of uniform names declared in the shader source.</summary>
        public required HashSet<string> UniformNames { get; init; }

        /// <summary>Gets the set of uniform names that require sRGB-to-linear conversion when setting material values.</summary>
        public required HashSet<string> SrgbUniforms { get; init; }

        /// <summary>Gets the set of reserved texture uniform names that are actively used by this shader.</summary>
        public HashSet<string> ReservedTexturesUsed { get; } = [];

        private readonly Dictionary<string, (ActiveUniformType Type, int Location, bool SrgbRead)> Uniforms = [];

        /// <summary>Gets the default <see cref="RenderMaterial"/> whose values serve as fallbacks when a material omits a uniform.</summary>
        public RenderMaterial Default { get; init; }

        /// <summary>Gets the <see cref="MaterialLoader"/> used to resolve fallback textures.</summary>
        protected MaterialLoader MaterialLoader { get; init; }

        /// <summary>Gets a mapping from vertex attribute names to their OpenGL attribute locations.</summary>
        public Dictionary<string, int> Attributes { get; } = [];

        /// <summary>Gets a value indicating whether material data (textures and params) should be skipped during rendering.</summary>
        public bool IgnoreMaterialData { get; }


#if DEBUG
        /// <summary>Gets the shader file name on disk (debug builds only).</summary>
        public required string FileName { get; init; }
#endif

        /// <summary>Initializes a new instance of the <see cref="Shader"/> class.</summary>
        /// <param name="name">The shader name, typically a Source 2 <c>.vfx</c> shader name.</param>
        /// <param name="rendererContext">The renderer context used to access the material loader.</param>
        public Shader(string name, RendererContext rendererContext)
        {
            Name = name;
            NameHash = MurmurHash2.Hash(Name, StringToken.MURMUR2SEED);
            Default = new RenderMaterial(this);
            MaterialLoader = rendererContext.MaterialLoader;

            IgnoreMaterialData = Name is "vrf.picking"
                                      or "vrf.outline"
                                      or "vrf.depth_only";
        }

        /// <summary>Ensures the shader program has been linked and its uniforms and attributes have been cached.</summary>
        /// <returns><see langword="true"/> if the shader linked successfully; otherwise <see langword="false"/>.</returns>
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

        private unsafe void StoreUniformLocations()
        {
            Span<float> floatVal = stackalloc float[16];

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
                var isMatrix = type is ActiveUniformType.FloatMat4;

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
                        ReservedTexturesUsed.Add(name);
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
                    floatVal.Clear();
                    fixed (float* ptr = floatVal)
                    {
                        GL.GetUniform(Program, GetUniformLocation(name), ptr);
                    }
                    Default.Material.VectorParams[name] = new Vector4(floatVal[0], floatVal[1], floatVal[2], floatVal[3]);
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
                else if (isMatrix && !Default.Matrices.ContainsKey(name))
                {
                    floatVal.Clear();
                    fixed (float* ptr = floatVal)
                    {
                        GL.GetUniform(Program, GetUniformLocation(name), ptr);
                    }
                    Default.Matrices[name] = new Matrix4x4(
                        floatVal[0], floatVal[4], floatVal[8], floatVal[12],
                        floatVal[1], floatVal[5], floatVal[9], floatVal[13],
                        floatVal[2], floatVal[6], floatVal[10], floatVal[14],
                        floatVal[3], floatVal[7], floatVal[11], floatVal[15]
                    );
                }
            }
        }

        /// <summary>Installs this shader program as part of the current rendering state.</summary>
        public void Use()
        {
            EnsureLoaded();
            GL.UseProgram(Program);
        }

        /// <summary>Enumerates all active uniforms in the program, populating the internal uniform location cache.</summary>
        /// <returns>A sequence of tuples with each uniform's name, index, type, and array size.</returns>
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

        /// <summary>Queries and caches the OpenGL locations of all active vertex attributes.</summary>
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

        /// <summary>Returns the OpenGL location of the named uniform, querying the driver and caching the result on first access.</summary>
        /// <param name="name">The uniform variable name.</param>
        /// <returns>The uniform location, or -1 if the uniform does not exist in the program.</returns>
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

        /// <summary>Returns the OpenGL index of the named uniform block, querying the driver and caching the result on first access.</summary>
        /// <param name="name">The uniform block name.</param>
        /// <returns>The uniform block index, or -1 if the block does not exist.</returns>
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

        /// <summary>Returns the number of scalar components in the named uniform (1 for scalars, 2–4 for vectors).</summary>
        /// <param name="name">The uniform variable name.</param>
        /// <returns>The component count, defaulting to 4 if the uniform is not found in the cache.</returns>
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

        /// <summary>Returns a value indicating whether the named uniform has a boolean type.</summary>
        /// <param name="paramName">The uniform variable name.</param>
        public bool IsBooleanParameter(string paramName)
        {
            return Uniforms.TryGetValue(paramName, out var uniform) && uniform.Type == ActiveUniformType.Bool;
        }

        /// <summary>Sets a scalar float uniform on this program.</summary>
        public void SetUniform1(string name, float value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform1(Program, uniformLocation, value);
            }
        }

        /// <summary>Sets a scalar integer uniform on this program.</summary>
        public void SetUniform1(string name, int value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform1(Program, uniformLocation, value);
            }
        }

        /// <summary>Sets a scalar boolean uniform on this program, encoded as 1u or 0u.</summary>
        public void SetUniform1(string name, bool value) => SetUniform1(name, value ? 1u : 0u);

        /// <summary>Sets a scalar unsigned integer uniform on this program.</summary>
        public void SetUniform1(string name, uint value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform1((uint)Program, uniformLocation, value);
            }
        }

        /// <summary>Sets a two-component float vector uniform on this program.</summary>
        public void SetUniform2(string name, Vector2 value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform2(Program, uniformLocation, value.X, value.Y);
            }
        }

        /// <summary>Sets a three-component float vector uniform on this program.</summary>
        public void SetUniform3(string name, Vector3 value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform3(Program, uniformLocation, value.X, value.Y, value.Z);
            }
        }

        /// <summary>Sets a four-component float vector uniform on this program.</summary>
        public void SetUniform4(string name, Vector4 value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform4(Program, uniformLocation, value.X, value.Y, value.Z, value.W);
            }
        }

        /// <summary>Sets a vector material uniform, applying sRGB-to-linear conversion if needed and adapting to the actual uniform size.</summary>
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

        /// <summary>Sets the <c>uAnimationData</c> uniform used by skinned mesh shaders.</summary>
        /// <param name="animated">Whether skeletal animation is active.</param>
        /// <param name="boneOffset">Offset into the bone transform buffer.</param>
        /// <param name="boneCount">Number of bones influencing this draw call.</param>
        /// <param name="weightCount">Number of bone weights per vertex.</param>
        public void SetBoneAnimationData(bool animated, int boneOffset = 0, int boneCount = 0, int weightCount = 0)
        {
            var uniformLocation = GetUniformLocation("uAnimationData");
            if (uniformLocation > -1)
            {
                GL.ProgramUniform4((uint)Program, uniformLocation, animated ? 1u : 0u, (uint)boneOffset, (uint)boneCount, (uint)weightCount);
            }
        }

        /// <summary>Sets an array of four-component float vector uniforms on this program.</summary>
        /// <param name="name">The uniform array name.</param>
        /// <param name="count">Number of vec4 elements to upload.</param>
        /// <param name="value">Flat array of float values (count * 4 elements).</param>
        public void SetUniform4Array(string name, int count, float[] value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniform4(Program, uniformLocation, count, value);
            }
        }

        /// <summary>Sets an array of 4×3 column-major matrix uniforms on this program.</summary>
        /// <param name="name">The uniform array name.</param>
        /// <param name="count">Number of matrices to upload.</param>
        /// <param name="value">Flat array of float values (count * 12 elements).</param>
        public void SetUniformMatrix4x3Array(string name, int count, float[] value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                GL.ProgramUniformMatrix4x3(Program, uniformLocation, count, false, value);
            }
        }

        /// <summary>Sets a 3×4 matrix uniform (converted from a <see cref="Matrix4x4"/> by dropping the last column).</summary>
        public void SetUniform3x4(string name, Matrix4x4 value)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                var matrix = value.To3x4();
                GL.ProgramUniformMatrix3x4(Program, uniformLocation, false, ref matrix);
            }
        }

        /// <summary>Sets a 4×4 matrix uniform on this program.</summary>
        /// <param name="name">The uniform variable name.</param>
        /// <param name="value">The matrix value.</param>
        /// <param name="transpose">Whether to transpose the matrix before uploading.</param>
        public void SetUniform4x4(string name, Matrix4x4 value, bool transpose = false)
        {
            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation > -1)
            {
                var matrix = value.ToOpenTK();
                GL.ProgramUniformMatrix4(Program, uniformLocation, transpose, ref matrix);
            }
        }

        /// <summary>Binds a texture to the given texture unit and sets the named sampler uniform, returning <see langword="false"/> if the texture or uniform is absent.</summary>
        /// <param name="slot">The texture unit index.</param>
        /// <param name="name">The sampler uniform name.</param>
        /// <param name="texture">The texture to bind.</param>
        /// <returns><see langword="true"/> if the texture was successfully bound; otherwise <see langword="false"/>.</returns>
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

        /// <summary>Binds a texture to the given texture unit and sets the sampler uniform by location.</summary>
        /// <param name="slot">The texture unit index.</param>
        /// <param name="uniformLocation">The pre-resolved sampler uniform location.</param>
        /// <param name="texture">The texture to bind.</param>
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
        /// <summary>Hot-reloads this shader by swapping it with a freshly compiled replacement (debug builds only).</summary>
        /// <param name="shader">The newly compiled shader to replace this instance with.</param>
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
