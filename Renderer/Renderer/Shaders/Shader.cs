using Microsoft.Extensions.Logging;
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

        /// <summary>Gets the <see cref="MurmurHash2"/> hash of <see cref="Name"/>.</summary>
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

        /// <summary>Gets the set of sampler uniform names that use material address modes via <c>// Sampler(UserConfig)</c>.</summary>
        public required HashSet<string> SamplerUserConfigUniforms { get; init; }

        private GlobalsLayout globalsLayout = GlobalsLayout.Empty;

        /// <summary>
        /// Gets the packed layout of this shader's loose global uniforms. Derived from the source rather than
        /// from the linked program, so it is known before the shader links, and is shared by every variant
        /// compiled from the same source. A <see cref="RenderMaterial"/> only has to refill its constant
        /// buffer when this changes, which outside of a shader reload it never does.
        /// </summary>
        public GlobalsLayout GlobalsLayout
        {
            get => globalsLayout;
            internal set
            {
                globalsLayout = value;

                Default.FloatParams.Clear();
                Default.IntParams.Clear();
                Default.VectorParams.Clear();
                Default.Matrices.Clear();

                foreach (var (name, defaultValue) in value.FloatDefaults)
                {
                    Default.FloatParams[name] = defaultValue;
                }

                foreach (var (name, defaultValue) in value.IntDefaults)
                {
                    Default.IntParams[name] = defaultValue;
                }

                foreach (var (name, defaultValue) in value.VectorDefaults)
                {
                    Default.VectorParams[name] = defaultValue;
                }

                foreach (var (name, defaultValue) in value.MatrixDefaults)
                {
                    Default.Matrices[name] = defaultValue;
                }
            }
        }

        /// <summary>
        /// Gets the set of reserved texture uniform names that this shader samples. Seeded from the parsed source,
        /// so it is available before the program is linked, and trimmed by <see cref="EnsureLoaded"/> down to the
        /// ones the linker kept for this variant's combos.
        /// </summary>
        public HashSet<string> ReservedTexturesUsed { get; init; } = [];

        /// <summary>
        /// Gets a value indicating whether the program samples <c>g_tSceneColor</c>. Declaring it is enough until
        /// the program is linked, after which it means the linker kept it.
        /// </summary>
        public bool ReadsSceneColor => ReservedTexturesUsed.Contains("g_tSceneColor");

        private readonly Dictionary<string, (ActiveUniformType Type, int Location, bool SrgbRead)> Uniforms = [];


        /// <summary>Gets the default <see cref="RenderMaterial"/> whose values serve as fallbacks when a material omits a uniform.</summary>
        public RenderMaterial Default { get; init; }

        /// <summary>Gets the <see cref="MaterialLoader"/> used to resolve fallback textures.</summary>
        internal MaterialLoader MaterialLoader { get; init; }

        /// <summary>Gets the logger for messages about this shader.</summary>
        internal ILogger Logger { get; init; }

        /// <summary>Gets the renderer context this shader was loaded for.</summary>
        internal RendererContext RendererContext { get; }

        /// <summary>Gets a value indicating whether material data (textures and params) should be skipped during rendering.</summary>
        public bool IgnoreMaterialData { get; }

        /// <summary>Replacement shader that reads the material's color texture.</summary>
        public bool IsDepthOnlyAlphaTest => Name == "depth_only" && Parameters.GetValueOrDefault("F_ALPHA_TEST") == 1;

        private readonly ShaderLoader shaderLoader;
        private Dictionary<(string Combo, byte Value), Shader>? variants;

        /// <summary>
        /// Gets this shader with one combo set differently. Cached, so a pass can pick a variant per draw
        /// instead of loading every combination up front. Chain the calls to move on more than one combo.
        /// </summary>
        public Shader WithCombo(string combo, byte value)
        {
            if (Parameters.GetValueOrDefault(combo) == value)
            {
                return this;
            }

            variants ??= [];

            if (!variants.TryGetValue((combo, value), out var variant))
            {
                var combos = new Dictionary<string, byte>(Parameters, StringComparer.Ordinal)
                {
                    [combo] = value,
                };

                variant = shaderLoader.LoadShader(Name, combos);
                variants.Add((combo, value), variant);
            }

            return variant;
        }

        /// <summary>Sets a uniform on this shader and on every variant taken from it, which are separate
        /// programs and so hold their own copy.</summary>
        public void SetUniform1AllVariants(string name, uint value)
        {
            SetUniform1(name, value);

            if (variants == null)
            {
                return;
            }

            foreach (var variant in variants.Values)
            {
                variant.SetUniform1(name, value);
            }
        }

        /// <summary>Gets the locations this program reads, as a mask. Checked in <see cref="VertexArray"/>.</summary>
        public int RequiredAttributes { get; private set; }

        /// <summary>Gets those locations declared as an integer type, which need an integer format.</summary>
        public int IntegerAttributes { get; private set; }

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
            RendererContext = rendererContext;
            Default = new RenderMaterial(this);
            MaterialLoader = rendererContext.MaterialLoader;
            Logger = rendererContext.Logger;
            shaderLoader = rendererContext.ShaderLoader;

            IgnoreMaterialData = Name is "picking"
                                      or "outline"
                                      or "depth_only"
                                      or "quad_overdraw";
        }

        /// <summary>Ensures the shader program has been linked and its uniforms have been cached.</summary>
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
                    StoreUniformLocations();
                    StoreRequiredAttributes();

#if DEBUG
                    VerifyGlobalsLayout();
#endif
                }
            }

            return IsValid;
        }

        /// <summary>
        /// Caches the attribute locations the linked program reads, and verifies each landed where its
        /// <see cref="VertexSlot"/> puts it. Attributes the linker dropped are not active.
        /// </summary>
        private void StoreRequiredAttributes()
        {
            GL.GetProgram(Program, GetProgramParameterName.ActiveAttributes, out var attributeCount);

            RequiredAttributes = 0;
            IntegerAttributes = 0;

            for (var i = 0; i < attributeCount; i++)
            {
                GL.GetActiveAttrib(Program, i, 64, out _, out var elements, out var type, out var name);

                var location = GL.GetAttribLocation(Program, name);

                if (location < 0)
                {
                    continue; // A gl_ builtin
                }

                // A declaration ShaderParser did not stamp is placed by the driver, where no VAO expects it.
                // A custom attribute has no canonical location, its slot comes from the declaring set.
                if (VertexAttributeLocations.Get(name) is var canonical && canonical != -1 && canonical != location)
                {
                    ReportBadAttribute($"Shader '{Name}' has attribute '{name}' at location {location}, but {nameof(VertexSlot)} puts it at {canonical}. Its declaration was not stamped, check that it reads 'in <type> {name};'.");
                }

                // These span a location per element or column, silently taking the slots declared after them
                else if (elements > 1 || IsMatrix(type))
                {
                    ReportBadAttribute($"Shader '{Name}' declares attribute '{name}' as {type}[{elements}], which spans several locations. Vertex attributes have to fit one {nameof(VertexSlot)}.");
                }

                RequiredAttributes |= 1 << location;

                if (IsInteger(type))
                {
                    IntegerAttributes |= 1 << location;
                }
            }
        }

        /// <summary>
        /// A shader whose attributes the renderer cannot place is an authoring fault, so development builds
        /// stop on it. A release build only logs, because a mounted shader must not take the viewer down
        /// from inside a draw call.
        /// </summary>
        private void ReportBadAttribute(string message)
        {
            Logger.LogError("{Message}", message);

#if DEBUG
            throw new ShaderLoader.ShaderCompilerException(message);
#endif
        }

        /// <summary>Names the attributes this program declares at the given locations.</summary>
        public string DescribeAttributes(int locationMask)
        {
            GL.GetProgram(Program, GetProgramParameterName.ActiveAttributes, out var attributeCount);

            var names = new List<string>();

            for (var i = 0; i < attributeCount; i++)
            {
                GL.GetActiveAttrib(Program, i, 64, out _, out _, out _, out var name);

                var location = GL.GetAttribLocation(Program, name);

                if (location >= 0 && (locationMask & (1 << location)) != 0)
                {
                    names.Add(name);
                }
            }

            return string.Join(", ", names);
        }

        private static bool IsInteger(ActiveAttribType type) => type
            is ActiveAttribType.Int or ActiveAttribType.IntVec2 or ActiveAttribType.IntVec3 or ActiveAttribType.IntVec4
            or ActiveAttribType.UnsignedInt or ActiveAttribType.UnsignedIntVec2 or ActiveAttribType.UnsignedIntVec3 or ActiveAttribType.UnsignedIntVec4;

        private static bool IsMatrix(ActiveAttribType type) => type
            is ActiveAttribType.FloatMat2 or ActiveAttribType.FloatMat3 or ActiveAttribType.FloatMat4
            or ActiveAttribType.FloatMat2x3 or ActiveAttribType.FloatMat2x4
            or ActiveAttribType.FloatMat3x2 or ActiveAttribType.FloatMat3x4
            or ActiveAttribType.FloatMat4x2 or ActiveAttribType.FloatMat4x3;

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

                // Samplers are not reflected for: a packed one takes its fallback from the layout, and a loose
                // one is set by whichever renderer draws with it.
                var isVector = type is >= ActiveUniformType.FloatVec2 and <= ActiveUniformType.IntVec4;
                var isScalar = type == ActiveUniformType.Float;
                var isBoolean = type == ActiveUniformType.Bool;
                var isInteger = type is ActiveUniformType.Int or ActiveUniformType.UnsignedInt;
                var isMatrix = type is ActiveUniformType.FloatMat4;

                if (isVector && !Default.VectorParams.ContainsKey(name))
                {
                    floatVal.Clear();
                    fixed (float* ptr = floatVal)
                    {
                        GL.GetUniform(Program, GetUniformLocation(name), ptr);
                    }
                    Default.VectorParams[name] = new Vector4(floatVal[0], floatVal[1], floatVal[2], floatVal[3]);
                }
                else if (isScalar && !Default.FloatParams.ContainsKey(name))
                {
                    GL.GetUniform(Program, GetUniformLocation(name), out float flVal);
                    Default.FloatParams[name] = flVal;
                }
                else if ((isBoolean || isInteger) && !Default.IntParams.ContainsKey(name))
                {
                    GL.GetUniform(Program, GetUniformLocation(name), out int intVal);
                    Default.IntParams[name] = intVal;
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

            // Seeded from the source, where a sampler behind a combo the linker dropped still looks used.
            // These live in the SceneTextures block, so they have an index rather than a location.
            ReservedTexturesUsed.RemoveWhere(reserved => !IsActiveUniform(reserved));
        }

        /// <summary>Returns whether the linked program kept a uniform, including the members of a block.</summary>
        private bool IsActiveUniform(string name)
        {
            var indices = new int[1];
            GL.GetUniformIndices(Program, 1, [name], indices);

            return indices[0] != -1;
        }

        /// <summary>Returns the texture a sampler falls back to when the material bound supplies none.</summary>
        /// <param name="name">The sampler uniform name, which hints at what a 2D stand-in should look like.</param>
        /// <param name="kind">The sampler type the texture has to match.</param>
        internal RenderTexture GetDefaultTexture(string name, SamplerKind kind)
        {
            // Only a 2D sampler can read these stand-ins; the rest need one of their own target.
            if (kind != SamplerKind.Texture2D)
            {
                return MaterialLoader.GetNullTexture(kind);
            }

            return name switch
            {
                _ when name.Contains("normal", StringComparison.OrdinalIgnoreCase) => MaterialLoader.GetDefaultNormal(),
                _ when name.Contains("mask", StringComparison.OrdinalIgnoreCase) => MaterialLoader.GetDefaultMask(),
                _ => MaterialLoader.GetErrorTexture(),
            };
        }

        /// <summary>
        /// Installs this shader program as part of the current rendering state, along with the constant buffer
        /// holding its own global uniforms. A <see cref="RenderMaterial"/> rendered afterwards replaces that
        /// buffer with its own; shaders drawn without a material keep it, and <see cref="SetUniform(string, float)"/>
        /// writes into it.
        /// </summary>
        public void Use()
        {
            EnsureLoaded();
            GL.UseProgram(Program);

            RendererContext.SceneTextures.BindBufferBase();

            Default.BindGlobals(this);
        }

        /// <summary>Sets a packed global uniform in this shader's own constant buffer.</summary>
        /// <remarks>
        /// For shaders drawn without a material. When a material is bound its own constant buffer is what the
        /// draw reads, so set the value through <see cref="RenderMaterial.SetUniform(string, float)"/> instead.
        /// </remarks>
        public void SetUniform(string name, float value) => Default.SetUniform(name, value);

        /// <inheritdoc cref="SetUniform(string, float)"/>
        public void SetUniform(string name, int value) => Default.SetUniform(name, value);

        /// <inheritdoc cref="SetUniform(string, float)"/>
        public void SetUniform(string name, uint value) => Default.SetUniform(name, (long)value);

        /// <inheritdoc cref="SetUniform(string, float)"/>
        public void SetUniform(string name, bool value) => Default.SetUniform(name, value ? 1L : 0L);

        /// <inheritdoc cref="SetUniform(string, float)"/>
        public void SetUniform(string name, Vector2 value) => Default.SetUniform(name, new Vector4(value, 0f, 0f));

        /// <inheritdoc cref="SetUniform(string, float)"/>
        public void SetUniform(string name, Vector3 value) => Default.SetUniform(name, new Vector4(value, 0f));

        /// <inheritdoc cref="SetUniform(string, float)"/>
        public void SetUniform(string name, Vector4 value) => Default.SetUniform(name, value);

        /// <inheritdoc cref="SetUniform(string, float)"/>
        public void SetUniform(string name, Matrix4x4 value) => Default.SetUniform(name, value);

        /// <summary>Enumerates all active non-block uniforms in the program (uniforms belonging to uniform blocks are skipped), populating the internal uniform location cache.</summary>
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

#if DEBUG
        /// <summary>
        /// Checks the offsets <see cref="GlobalsLayout"/> computed against the ones the driver laid the
        /// block out at. They are both std140 so they have to agree, but getting this wrong would corrupt every
        /// material silently (debug builds only).
        /// </summary>
        private void VerifyGlobalsLayout()
        {
            if (GlobalsLayout.Size == 0)
            {
                return;
            }

            var names = new string[GlobalsLayout.Members.Count];
            var expected = new int[names.Length];
            var i = 0;

            foreach (var (name, constant) in GlobalsLayout.Members)
            {
                names[i] = name;
                expected[i] = constant.Offset;
                i++;
            }

            var indices = new int[names.Length];
            GL.GetUniformIndices(Program, names.Length, names, indices);

            var offsets = new int[names.Length];
            GL.GetActiveUniforms(Program, names.Length, indices, ActiveUniformParameter.UniformOffset, offsets);

            for (i = 0; i < names.Length; i++)
            {
                // The linker drops members nothing reads, and reports them as an invalid index.
                if (indices[i] == -1)
                {
                    continue;
                }

                System.Diagnostics.Debug.Assert(offsets[i] == expected[i],
                    $"'{names[i]}' is at offset {offsets[i]} in '{Name}', but the layout put it at {expected[i]}.");
            }
        }

#endif

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

            System.Diagnostics.Debug.Assert(location > -1 || !GlobalsLayout.Members.ContainsKey(name),
                $"'{name}' is packed into {GlobalsLayout.BlockName}, write it with the unnumbered SetUniform overload.");

            Uniforms[name] = (0, location, SrgbUniforms.Contains(name));

            return location;
        }

        /// <summary>Returns the number of scalar components in the named uniform (1 for scalars, 2-4 for vectors).</summary>
        /// <param name="name">The uniform variable name.</param>
        /// <returns>The component count, defaulting to 4 if the uniform is not found in the cache.</returns>
        public int GetRegisterSize(string name)
        {
            if (GlobalsLayout.Members.TryGetValue(name, out var constant))
            {
                return Math.Min(constant.ComponentCount, 4);
            }

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
            if (GlobalsLayout.Members.TryGetValue(paramName, out var constant))
            {
                return constant.Type == GlobalsType.Bool;
            }

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


        /// <summary>
        /// Gets this shader built for what a mesh supplies. Only a pass that replaces material shaders needs
        /// it, since a material shader already carries the combo of the mesh it was loaded for.
        /// </summary>
        public Shader WithSkinning(MeshSkinning skinning) => WithCombo("D_SKINNING", (byte)skinning);

        /// <summary>Sets the <c>uAnimationData</c> uniform used by skinned mesh shaders.</summary>
        /// <param name="animated">Whether skeletal animation is active.</param>
        /// <param name="boneOffset">Offset into the bone transform buffer.</param>
        /// <param name="boneCount">Number of bones influencing this draw call.</param>
        public void SetBoneAnimationData(bool animated, int boneOffset = 0, int boneCount = 0)
        {
            var uniformLocation = GetUniformLocation("uAnimationData");
            if (uniformLocation > -1)
            {
                GL.ProgramUniform3((uint)Program, uniformLocation, animated ? 1u : 0u, (uint)boneOffset, (uint)boneCount);
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

        /// <summary>Sets a 3×4 matrix uniform (converted from a <see cref="Matrix4x4"/> by transposing and dropping the last (M14/M24/M34/M44) column).</summary>
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

        /// <summary>Sets the named sampler uniform to a texture's bindless handle, returning <see langword="false"/> if the texture or uniform is absent.</summary>
        /// <param name="name">The sampler uniform name.</param>
        /// <param name="texture">The texture to sample.</param>
        /// <param name="sampler">Sampler object supplying the filtering and wrapping, or zero to use the texture's own.</param>
        /// <returns><see langword="true"/> if the uniform was set; otherwise <see langword="false"/>.</returns>
        public bool SetTexture(string name, RenderTexture? texture, int sampler = 0)
        {
            if (texture == null)
            {
                return false;
            }

            // A sampler lives in one of three places, decided by who owns the texture: this shader's own
            // constant buffer, the renderer's shared one, or a loose uniform for the per-draw ones.
            if (Default.SetTexture(name, texture, sampler))
            {
                return true;
            }

            if (RendererContext.SceneTextures.SetTexture(name, texture, sampler))
            {
                return true;
            }

            var uniformLocation = GetUniformLocation(name);
            if (uniformLocation < 0)
            {
                return false;
            }

            SetTexture(uniformLocation, texture, sampler);
            return true;
        }

        /// <summary>Sets the sampler uniform at the given location to a texture's bindless handle.</summary>
        /// <param name="uniformLocation">The pre-resolved sampler uniform location.</param>
        /// <param name="texture">The texture to sample.</param>
        /// <param name="sampler">Sampler object supplying the filtering and wrapping, or zero to use the texture's own.</param>
        public void SetTexture(int uniformLocation, RenderTexture? texture, int sampler = 0)
        {
            if (texture == null)
            {
                return;
            }

            GL.Arb.ProgramUniformHandle(Program, uniformLocation, texture.GetHandle(sampler));
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

            GlobalsLayout = shader.GlobalsLayout;

            ReservedTexturesUsed.Clear();
            ReservedTexturesUsed.UnionWith(shader.ReservedTexturesUsed);

            Uniforms.Clear();

        }
#endif
    }
}
