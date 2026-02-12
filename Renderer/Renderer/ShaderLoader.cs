using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using SlangShaderSharp;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Shader stage types in the rendering pipeline.
    /// </summary>
    public enum ShaderProgramType
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        Vertex = 0,
        Fragment = 1,
        Compute = 2,
        Max = 3,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    }

    /// <summary>
    /// Compiles and caches OpenGL shader programs from source files.
    /// </summary>
    public partial class ShaderLoader : IDisposable
    {
        [GeneratedRegex(@"^(?<SourceFile>[0-9]+)\((?<Line>[0-9]+)\) ?: error")]
        private static partial Regex NvidiaGlslError();

        [GeneratedRegex(@"ERROR: (?<SourceFile>[0-9]+):(?<Line>[0-9]+):")]
        private static partial Regex AmdGlslError();

        [GeneratedRegex(@"^(?<SourceFile>[0-9]+):(?<Line>[0-9]+)\((?<Column>[0-9]+)\):")]
        private static partial Regex Mesa3dGlslError();

        private readonly Dictionary<ulong, Shader> CachedShaders = [];
        public int ShaderCount => CachedShaders.Count;
        private readonly Dictionary<string, Dictionary<string, byte>> ShaderDefines = [];

        private static readonly Dictionary<string, byte> EmptyArgs = [];
        private static readonly Lock ParserLock = new();
        private static readonly Dictionary<string, ParsedShaderData> ParsedCache = [];

        private static readonly ShaderParser Parser = new();

        private readonly RendererContext RendererContext;

#if DEBUG
        private HashSet<string> LastShaderVariantNames = [];

        // TODO: This probably should be ParsedShaderData so we can access it for non-blocking linking
        private List<List<string>> LastShaderSourceLines = [];
#endif

        /// <summary>
        /// Preprocessed shader source with defines, uniforms, and compiled stage code.
        /// </summary>
        public class ParsedShaderData
        {
            public Dictionary<string, byte> Defines { get; } = [];
            public HashSet<string> RenderModes { get; } = [];
            public HashSet<string> Uniforms { get; } = [];
            public HashSet<string> SrgbUniforms { get; } = [];
            public Dictionary<ShaderProgramType, string> Sources { get; } = [];

            //SLANG. This is necessary for specialisation. We aren't parsing a GLSL source anymore.
            public IComponentType? SlangComponentType { get; set; }
            //SLANG
            public List<IEntryPoint> EntryPoints { get; } = [];
            //SLANG
            public bool IsSlang { get; set; }

#if DEBUG
            public HashSet<string> ShaderVariants { get; } = [];
#endif
        }

        static ShaderLoader()
        {
            if (slangSession == null)
            {
                setupSlangCompiler();
            }
        }

        public ShaderLoader(RendererContext rendererContext)
        {
            RendererContext = rendererContext;
        }

        static void setupSlangCompiler()
        {
            var desc = new SlangGlobalSessionDesc { EnableGLSL = true };
            Slang.CreateGlobalSession2(desc, out globalSlangSession);
            RecreateSlangSession();
        }

        static public void RecreateSlangSession()
        {
            (slangSession as IDisposable)?.Dispose();

            var targetDesc = new TargetDesc
            {
                Format = SlangCompileTarget.Spirv,
                Profile = globalSlangSession!.FindProfile("spirv_1_0"),
            };

            var sessionDesc = new SessionDesc
            {
                Targets = [targetDesc],
                AllowGLSLSyntax = true,
                DefaultMatrixLayoutMode = SlangMatrixLayoutMode.RowMajor,
                SearchPaths = [ShaderParser.GetShaderDiskPath(string.Empty)],
                CompilerOptionEntries =
                [
                    new CompilerOptionEntry(CompilerOptionName.DebugInformation, CompilerOptionValue.FromInt(0)),
                ],
            };

            globalSlangSession.CreateSession(sessionDesc, out slangSession);
        }

        static IGlobalSession? globalSlangSession;
        static ISession? slangSession;



        public Shader LoadShader(string shaderName, params (string ComboName, byte ComboValue)[] combos)
        {
            var args = combos.ToDictionary(c => c.ComboName, c => c.ComboValue);
            return LoadShader(shaderName, args);
        }

        public Shader LoadShader(string shaderName, IReadOnlyDictionary<string, byte>? arguments = null, bool blocking = true)
        {
            arguments ??= EmptyArgs;

            if (ShaderDefines.ContainsKey(shaderName))
            {
                var shaderCacheHash = CalculateShaderCacheHash(shaderName, arguments);

                if (CachedShaders.TryGetValue(shaderCacheHash, out var cachedShader))
                {
                    return cachedShader;
                }
            }

            var shader = CompileAndLinkShader(shaderName, arguments, blocking: blocking);
            var newShaderCacheHash = CalculateShaderCacheHash(shaderName, arguments);
            CachedShaders[newShaderCacheHash] = shader;
            return shader;
        }

        private ParsedShaderData GetOrParseShader(string shaderFileName)
        {
            if (File.Exists(ShaderParser.GetShaderDiskPath($"{shaderFileName}.slang")))
            {
                return GetOrParseSlangShader(shaderFileName);
            }
            else
            {
                return GetOrParseGlslShader(shaderFileName);
            }
        }

        private static ParsedShaderData GetOrParseGlslShader(string shaderFileName)
        {
            using var _ = ParserLock.EnterScope();
            if (ParsedCache.TryGetValue(shaderFileName, out var cached))
            {
                return cached;
            }

            var parsedData = new ParsedShaderData();

            var availableStages = Parser.AvailableShaders.GetValueOrDefault(shaderFileName)
                ?? throw new FileNotFoundException($"Shader '{shaderFileName}' does not exist.");

            if (availableStages.Length == 0
            || availableStages[(int)ShaderProgramType.Vertex] == false && availableStages[(int)ShaderProgramType.Compute] == false)
            {
                throw new InvalidDataException($"Shader '{shaderFileName}' does not have a vertex or compute stage.");
            }

            foreach (var (@type, extension) in ShaderParser.ProgramTypeToExtension)
            {
                if (!availableStages[(int)@type])
                {
                    continue;
                }

                var nameWithExtension = $"{shaderFileName}.{extension}.glSlang";

                var shaderSource = Parser.PreprocessShader(nameWithExtension, parsedData);
                parsedData.Sources[@type] = shaderSource;
                Parser.ClearBuilder();
            }

            ParsedCache[shaderFileName] = parsedData;
            return parsedData;
        }

        private ParsedShaderData GetOrParseSlangShader(string shaderFileName)
        {
            using var _ = ParserLock.EnterScope();
            if (ParsedCache.TryGetValue(shaderFileName, out var cached))
            {
                return cached;
            }

            var parsedData = new ParsedShaderData();

            parsedData.IsSlang = true;

            var availableStages = Parser.AvailableShaders.GetValueOrDefault(shaderFileName)
                ?? throw new FileNotFoundException($"Shader '{shaderFileName}' does not exist.");

            if (availableStages.Length == 0
            || availableStages[(int)ShaderProgramType.Vertex] == false && availableStages[(int)ShaderProgramType.Compute] == false)
            {
                throw new InvalidDataException($"Shader '{shaderFileName}' does not have a vertex or compute stage.");
            }

            //only loading one file. Shaders with split vert and frag shaders must import the vertex shader.
            var nameWithExtension = $"{shaderFileName}.slang";

            var module = slangSession!.LoadModule(ShaderParser.GetShaderDiskPath($"{shaderFileName}.slang"), out var diagnostics);

            if (module != null)
            {
                for (var i = 0; i < module.GetDefinedEntryPointCount(); i++)
                {
                    module.GetDefinedEntryPoint(i, out var entryPoint);
                    parsedData.EntryPoints.Add(entryPoint);
                }
                module.Link(out var linkedModule, out var linkDiag);
                parsedData.SlangComponentType = linkedModule;

                if (diagnostics != null)
                {
                    RendererContext.Logger.LogInformation("Shader '{ShaderFileName}': {Log}'", shaderFileName, diagnostics.AsString);
                }
            }
            else
            {
                throw new ShaderCompilerException($"Failed to load shader: {shaderFileName}:\n{(diagnostics?.AsString ?? "Diagnostics unavailable")}");
            }

            ParsedCache[shaderFileName] = parsedData;
            return parsedData;
        }

        //SLANG: false = not a bound resource. This happens when it is a disabled conditional.
        static bool ReflectResource(in VariableLayoutReflection variable, out bool IsTexture, out int BindingIndex)
        {
            IsTexture = false;
            BindingIndex = (int)variable.BindingIndex;
            var varType = variable.TypeLayout;

            //var conditionalArray = varType.getFieldByIndex(0).TypeLayout.getElementTypeLayout();

            var shape = variable.TypeLayout.Type.ResourceShape;

            if (Convert.ToBoolean(shape & SlangResourceShape.Texture2D) || Convert.ToBoolean(shape & SlangResourceShape.Texture2DArray))
            {
                IsTexture = true;
            }


            return true;
        }


        private Shader CompileAndLinkGlslShader(string shaderName, ParsedShaderData parsedData, IReadOnlyDictionary<string, byte> arguments, bool blocking = true)
        {
            var shaderProgram = -1;
            //SLANG: this is a test to check if this is as easy as it feels
            var configModuleSource = "";
            foreach (var argument in arguments)
            {
                configModuleSource += "export static const int " + argument.Key + "=" + argument.Value + ";\n";
            }


            try
            {
                var shaderFileName = GetShaderFileByName(shaderName);
                var sources = parsedData.Sources;

                static ShaderType ToShaderType(ShaderProgramType type) => type switch
                {
                    ShaderProgramType.Vertex => ShaderType.VertexShader,
                    ShaderProgramType.Fragment => ShaderType.FragmentShader,
                    ShaderProgramType.Compute => ShaderType.ComputeShader,
                    _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
                };

                var shaderObjects = new int[sources.Count];
                var shaderSources = new string[sources.Count];
                var s = 0;
                foreach (var (stage, source) in sources)
                {
                    shaderObjects[s] = GL.CreateShader(ToShaderType(stage));
                    shaderSources[s] = source!;
                    s++;
                }

                CompileShaderObjects(shaderObjects, shaderSources, shaderFileName, shaderName, arguments, parsedData);

                shaderProgram = GL.CreateProgram();

#if DEBUG
                GL.ObjectLabel(ObjectLabelIdentifier.Program, shaderProgram, shaderFileName.Length, shaderFileName);
                for (var i = 0; i < shaderObjects.Length; i++)
                {
                    GL.ObjectLabel(ObjectLabelIdentifier.Shader, shaderObjects[i], shaderFileName.Length, shaderFileName);
                }
#endif

                var shader = new Shader(shaderName, RendererContext)
                {
#if DEBUG
                    FileName = shaderFileName,
#endif

                    Parameters = arguments,
                    Program = shaderProgram,
                    ShaderObjects = shaderObjects,
                    RenderModes = parsedData.RenderModes,
                    UniformNames = parsedData.Uniforms,
                    SrgbUniforms = parsedData.SrgbUniforms,
                    IsSlang = parsedData.IsSlang
                };

                foreach (var shaderObj in shaderObjects)
                {
                    GL.AttachShader(shader.Program, shaderObj);
                }

                GL.LinkProgram(shader.Program);

                // Not getting link status straight away allows the driver to perform parallelized shader compilation
                // TODO: Ideally we want this to work for initial load too.
                if (blocking)
                {
                    if (!shader.EnsureLoaded())
                    {
                        GL.GetProgramInfoLog(shader.Program, out var log);
                        ThrowShaderError(log, string.Concat(shaderFileName, GetArgumentDescription(arguments)), shaderName, "Failed to link shader");
                    }
                }

                ShaderDefines[shaderName] = parsedData.Defines;

#if DEBUG
                LastShaderVariantNames = parsedData.ShaderVariants;
                LastShaderSourceLines = [.. Parser.SourceFileLines];
#endif

                var argsDescription = GetArgumentDescription(SortAndFilterArguments(shaderName, arguments));
                RendererContext.Logger.LogInformation("Shader '{ShaderName}' as '{ShaderFileName}'{ArgsDescription} compiled{CompiledStatus} successfully (program={Program})", shaderName, shaderFileName, argsDescription, blocking ? " and linked" : string.Empty, shader.Program);

                return shader;
            }
            catch (ShaderCompilerException)
            {
                if (shaderProgram > -1)
                {
                    GL.DeleteProgram(shaderProgram);
                }

                throw;
            }
            finally
            {
                Parser.ClearBuilder();
            }
        }

        private Shader CompileAndLinkSlangShader(string shaderName, ParsedShaderData parsedData, IReadOnlyDictionary<string, byte> arguments, bool blocking = true)
        {
            var shaderProgram = -1;
            try
            {
                var shaderFileName = GetShaderFileByName(shaderName);
                var sources = new Dictionary<SlangStage, ISlangBlob>();

                //SLANG: we must build an argument config!
                var configModuleSource = "";
                foreach (var argument in arguments)
                {
                    configModuleSource += "export static const int " + argument.Key + "=" + argument.Value + ";\n";
                }

                // Inject shader variant name (same as GLSL path does with #define)
                configModuleSource += "export static const int " + Path.GetFileNameWithoutExtension(shaderName).Replace('.', '_') + "_vfx=1;\n";

                var confName = (new Guid(MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(configModuleSource)))).ToString();
                var configMod = slangSession.LoadModuleFromSourceString(confName, null, configModuleSource, out var diagnosticBlob);

                var diagnosticString = "";

                if (diagnosticBlob != null)
                {
                    diagnosticString = diagnosticBlob!.AsString;
                }

                //apply the config right away!
                slangSession.CreateCompositeComponentType([configMod, parsedData.SlangComponentType], out var specialisedProgram, out diagnosticBlob);

                //specialisedProgram1.link(out IComponentType specialisedProgram, out diagnosticBlob);

                //time to reflect on things!
                var ReflectedUniformBufferBinding = -1;
                var ReflectedUniformBufferSize = 0;
                var ReflectedUniformOffsets = new Dictionary<string, int>();

                var ReflectedIntParams = new Dictionary<string, (ActiveUniformType Type, int Location, int DefaultValue, bool SrgbRead)>();
                var ReflectedFloatParams = new Dictionary<string, (ActiveUniformType Type, int Location, float DefaultValue, bool SrgbRead)>();
                var ReflectedVectorParams = new Dictionary<string, (ActiveUniformType Type, int Location, int size, Vector4 DefaultValue, bool SrgbRead)>();


                var ReflectedResourceBindings = new Dictionary<string, (int Binding, bool isTexture, bool SrgbRead)>();
                var ReflectedAttributes = new Dictionary<string, int>();

                HashSet<string> ReflectedReservedTextures = [];

                var programLayout = specialisedProgram.GetLayout(0, out _);
                var globalVarLayout = programLayout.GetGlobalParamsVarLayout();


                //Reflect parameters
                if (globalVarLayout is { } gvl && gvl.TypeLayout.ParameterCategory == SlangParameterCategory.None)
                {
                    //SLANG: Wonderful! That means we don't have a uniform buffer because we have no lose uniforms!
                    var globalTypeLayout = gvl.TypeLayout;

                    //SLANG: Any field in here is therefore a bound resource!
                    for (uint i = 0; i < globalTypeLayout.FieldCount; i++)
                    {
                        var field = globalTypeLayout.GetFieldByIndex(i);

                        var fieldType = field.TypeLayout;
                        var fieldVar = field.Variable;
                        var kind = fieldType.Kind;
                        var name = field.Name;

                        var SrgbRead = false;

                        for (uint attributeIndex = 0; attributeIndex < fieldVar.AttributeCount; attributeIndex++)
                        {
                            var userAttribute = fieldVar.GetAttribute(attributeIndex);
                            if (userAttribute.Name == "SrgbRead")
                            {
                                SrgbRead = true;
                            }
                        }

                        var shape = fieldType.Type.ResourceShape;
                        if (Convert.ToBoolean(shape & SlangResourceShape.Texture2D))
                        {
                            ReflectedResourceBindings.Add(field.Name, ((int)field.BindingIndex, true, SrgbRead));
                            foreach (var resTex in MaterialLoader.ReservedTextures)
                                if (name.Contains(resTex))
                                {
                                    ReflectedReservedTextures.Add(name);
                                }
                        }
                        else
                            ReflectedResourceBindings.Add(field.Name, ((int)field.BindingIndex, false, false));
                    }
                }
                else if (globalVarLayout is { } gvl2 && gvl2.TypeLayout.ParameterCategory == SlangParameterCategory.DescriptorTableSlot)
                {
                    ReflectedUniformBufferBinding = (int)gvl2.BindingIndex;
                    //SLANG: globallayout(a variable)->buffer->containing a variable -> of type struct (or so we hope)
                    var bufferType = gvl2.TypeLayout.ElementVarLayout.TypeLayout;
                    ReflectedUniformBufferSize = (int)bufferType.GetSize(SlangParameterCategory.Uniform);

                    for (uint i = 0; i < bufferType.FieldCount; i++)
                    {
                        var field = bufferType.GetFieldByIndex(i);

                        if (field.TypeLayout.ParameterCategory == SlangParameterCategory.Uniform)
                        {
                            var uniformName = field.Name;

                            ReflectedUniformOffsets.Add(uniformName, (int)field.GetOffset());
                            var fieldType = field.TypeLayout;
                            var fieldVar = field.Variable;
                            var kind = fieldType.Kind;



                            switch (kind)
                            {
                                case SlangTypeKind.Scalar:
                                    {
                                        var scalarType = fieldType.Type.ScalarType;
                                        if (scalarType == SlangScalarType.Int32)
                                        {
                                            var SrgbRead = false;
                                            float defValue = 0;
                                            for (uint attributeIndex = 0; attributeIndex < fieldVar.AttributeCount; attributeIndex++)
                                            {
                                                var userAttribute = fieldVar.GetAttribute(attributeIndex);
                                                if (userAttribute.Name == "SrgbRead")
                                                {
                                                    SrgbRead = true;
                                                }
                                                if (userAttribute.Name == "Default")
                                                {
                                                    defValue = userAttribute.GetArgumentValueFloat(0)!.Value;
                                                }
                                            }

                                            ReflectedIntParams.Add(uniformName, (ActiveUniformType.Int, (int)field.GetOffset(), (int)defValue, SrgbRead));
                                        }
                                        if (scalarType == SlangScalarType.Float32)
                                        {
                                            var SrgbRead = false;
                                            float defValue = 0;
                                            for (uint attributeIndex = 0; attributeIndex < fieldVar.AttributeCount; attributeIndex++)
                                            {
                                                var userAttribute = fieldVar.GetAttribute(attributeIndex);
                                                if (userAttribute.Name == "SrgbRead")
                                                {
                                                    SrgbRead = true;
                                                }
                                                if (userAttribute.Name == "Default")
                                                {
                                                    defValue = userAttribute.GetArgumentValueFloat(0)!.Value;
                                                }
                                            }

                                            ReflectedFloatParams.Add(uniformName, (ActiveUniformType.Int, (int)field.GetOffset(), defValue, SrgbRead));
                                        }
                                        break;
                                    }
                                //Assumption: All vectors are float vectors. If we also want to support int vectors here, we need to handle it differently.
                                case SlangTypeKind.Vector:
                                    {
                                        var SrgbRead = false;
                                        var defValue = new Vector4(0);
                                        for (uint attributeIndex = 0; attributeIndex < fieldVar.AttributeCount; attributeIndex++)
                                        {
                                            var userAttribute = fieldVar.GetAttribute(attributeIndex);
                                            if (userAttribute.Name == "SrgbRead")
                                            {
                                                SrgbRead = true;
                                            }
                                            if (userAttribute.Name.StartsWith("Default"))
                                            {
                                                for (uint argIndex = 0; argIndex < userAttribute.ArgumentCount; argIndex++)
                                                {
                                                    var defSlotValue = userAttribute.GetArgumentValueFloat(argIndex)!.Value;

                                                    defValue[(int)argIndex] = defSlotValue;
                                                }
                                            }
                                        }
                                        //SLANG: HACK: Getting the size from argument count is flawed, as we might have a mismatch between the default size and the vector type.
                                        ReflectedVectorParams.Add(uniformName, (ActiveUniformType.FloatVec4, (int)field.GetOffset(), (int)field.TypeLayout.Type.ElementCount, defValue, SrgbRead));


                                        break;
                                    }
                            }
                        }
                        else
                        {
                            var fieldType = field.TypeLayout;
                            var fieldVar = field.Variable;
                            var kind = fieldType.Kind;
                            var name = field.Name;

                            var SrgbRead = false;

                            for (uint attributeIndex = 0; attributeIndex < fieldVar.AttributeCount; attributeIndex++)
                            {
                                var userAttribute = fieldVar.GetAttribute(attributeIndex);
                                if (userAttribute.Name == "SrgbRead")
                                {
                                    SrgbRead = true;
                                }
                            }

                            if (ReflectResource(field, out var isTexture, out var BindingIndex))
                            {
                                ReflectedResourceBindings.Add(field.Name, (BindingIndex, isTexture, isTexture && SrgbRead));
                            }


                            var shape = fieldType.Type.ResourceShape;
                            if (Convert.ToBoolean(shape & SlangResourceShape.Texture2D))
                            {
                                foreach (var resTex in MaterialLoader.ReservedTextures)
                                {
                                    if (name.Contains(resTex))
                                    {
                                        ReflectedReservedTextures.Add(name);
                                    }
                                }
                            }
                        }
                    }
                }

                static ShaderType ToShaderType(SlangStage type) => type switch
                {
                    SlangStage.Vertex => ShaderType.VertexShader,
                    SlangStage.Fragment => ShaderType.FragmentShader,
                    SlangStage.Compute => ShaderType.ComputeShader,
                    _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
                };

                //time to get the stage output code and also reflect input, if we have a vertex stage!
                foreach (var entryPoint in parsedData.EntryPoints)
                {
                    slangSession.CreateCompositeComponentType(new IComponentType[] { specialisedProgram, entryPoint }, out var shaderStageProgram, out diagnosticBlob);

                    if (shaderStageProgram == null)
                    {
                        throw new ShaderCompilerException($"Failed to create composite for shader '{shaderName}':\n{diagnosticBlob?.AsString}");
                    }

                    shaderStageProgram.Link(out var linkedShaderStageProgram, out diagnosticBlob);

                    if (linkedShaderStageProgram == null)
                    {
                        throw new ShaderCompilerException($"Failed to link shader stage for '{shaderName}':\n{diagnosticBlob?.AsString}");
                    }

                    linkedShaderStageProgram.GetTargetCode(0, out var stageCode, out var codeGenDiag);
                    var testSize = (int)stageCode.GetBufferSize();
                    var entryPointReflection = linkedShaderStageProgram.GetLayout(0, out _).GetEntryPointByIndex(0);
                    var stage = entryPointReflection.Stage;
                    if (stage == SlangStage.Vertex)
                    {
                        var inputLayout = entryPointReflection.VarLayout;
                        var inputCount = inputLayout.TypeLayout.FieldCount;
                        for (uint i = 0; i < inputCount; i++)
                        {
                            var inputElementLayout = inputLayout.TypeLayout.GetFieldByIndex(i);

                            ReflectedAttributes.Add(inputElementLayout.Name, (int)inputElementLayout.BindingIndex);
                        }

                    }
                    sources.Add(stage, stageCode);
                }
                var shaderObjects = new int[sources.Count];
                var shaderSources = new ISlangBlob[sources.Count];

                var s = 0;
                foreach (var (stage, source) in sources)
                {
                    shaderObjects[s] = GL.CreateShader(ToShaderType(stage));
                    shaderSources[s] = source!;
                    s++;
                }
                unsafe void DumpToFile(IntPtr ptr, int sizeInBytes, string filename)
                {
                    var bytes = new byte[sizeInBytes];
                    Marshal.Copy(ptr, bytes, 0, sizeInBytes);
                    File.WriteAllBytes(filename, bytes);
                }

                //DumpToFile(shaderSources[0].GetBufferPointer(), (int)shaderSources[0].GetBufferSize(), shaderName + ".spv");

                for (var i = 0; i < shaderObjects.Length; i++)
                {
                    //SLANG: I forgot why I made a copy of it. too lazy to investigate rn
                    //CompileSlangShaderObject(shaderObjects[i], shaderName, shaderName, arguments, "", shaderSources[i]);

                    var soCalledNotSpirv = shaderSources[i].AsString;
                    unsafe
                    {
                        GL.ShaderBinary(1, ref shaderObjects[i], ShaderBinaryFormat.ShaderBinaryFormatSpirV, (IntPtr)shaderSources[i].GetBufferPointer()
                        , (int)shaderSources[i].GetBufferSize());
                    }

                    GL.SpecializeShader(shaderObjects[i], "main", 0, (int[])null, (int[])null);

                }

                shaderProgram = GL.CreateProgram();

#if DEBUG
                GL.ObjectLabel(ObjectLabelIdentifier.Program, shaderProgram, shaderFileName.Length, shaderFileName);
                for (var i = 0; i < shaderObjects.Length; i++)
                {
                    GL.ObjectLabel(ObjectLabelIdentifier.Shader, shaderObjects[i], shaderFileName.Length, shaderFileName);
                }
#endif


                var shader = new Shader(shaderName, RendererContext)
                {
#if DEBUG
                    FileName = shaderFileName,
#endif

                    Parameters = arguments,
                    Program = shaderProgram,
                    ShaderObjects = shaderObjects,
                    UniformBufferBinding = ReflectedUniformBufferBinding,
                    CpuUniformBuffer = new byte[ReflectedUniformBufferSize],
                    IntParams = ReflectedIntParams,
                    FloatParams = ReflectedFloatParams,
                    VectorParams = ReflectedVectorParams,
                    UniformBufferSize = ReflectedUniformBufferSize,
                    UniformOffsets = ReflectedUniformOffsets,
                    ResourceBindings = ReflectedResourceBindings,
                    ReservedTexuresUsed = ReflectedReservedTextures,
                    Attributes = ReflectedAttributes,
                    RenderModes = parsedData.RenderModes,
                    UniformNames = parsedData.Uniforms,
                    SrgbUniforms = parsedData.SrgbUniforms,
                    IsSlang = parsedData.IsSlang
                };

                foreach (var shaderObj in shaderObjects)
                {
                    GL.AttachShader(shader.Program, shaderObj);
                }

                GL.LinkProgram(shader.Program);

                //SLANG: setting this to true causes openGL errors? possible threading fuckery?
                if (blocking)
                {
                    if (!shader.EnsureLoaded())
                    {
                        GL.GetProgramInfoLog(shader.Program, out var log);
                        ThrowShaderError(log, string.Concat(shaderFileName, GetArgumentDescription(arguments)), shaderName, "Failed to link shader");
                    }
                }

                ShaderDefines[shaderName] = parsedData.Defines;

#if DEBUG
                LastShaderVariantNames = parsedData.ShaderVariants;
                LastShaderSourceLines = [.. Parser.SourceFileLines];
#endif

                var argsDescription = GetArgumentDescription(SortAndFilterArguments(shaderName, arguments));
                RendererContext.Logger.LogInformation("Shader '{ShaderName}' as '{ShaderFileName}'{ArgsDescription} compiled {CompiledStatus} successfully", shaderName, shaderFileName, argsDescription, blocking ? "and linked" : string.Empty);

                return shader;
            }
            catch (ShaderCompilerException)
            {
                if (shaderProgram > -1)
                {
                    GL.DeleteProgram(shaderProgram);
                }

                throw;
            }
            finally
            {
                Parser.ClearBuilder();
            }
        }

        private Shader CompileAndLinkShader(string shaderName, IReadOnlyDictionary<string, byte> arguments, bool blocking = true)
        {
            var shaderFileName = GetShaderFileByName(shaderName);
            var parsedData = GetOrParseShader(shaderFileName);
            if (parsedData.IsSlang)
            {
                return CompileAndLinkSlangShader(shaderName, parsedData, arguments, blocking);
            }
            else
            {
                return CompileAndLinkGlslShader(shaderName, parsedData, arguments, blocking);
            }
        }

        private static void CompileShaderObjects(int[] shaderObjects, string[] shaderSources, string shaderFile, ReadOnlySpan<char> originalShaderName, IReadOnlyDictionary<string, byte> arguments, ParsedShaderData parsedData)
        {
            var header = new StringBuilder();

            if (!(shaderSources[0].Split("\n")[0] == "#version 460"))
            {
                header.Append(ShaderParser.ExpectedShaderVersion);
                header.Append('\n');
                // Append original shader name as a define
                header.Append("#define ");
                header.Append(Path.GetFileNameWithoutExtension(originalShaderName));
                header.Append("_vfx 1\n");

                // Add all defines (with argument overrides or defaults)
                foreach (var (defineName, defaultValue) in parsedData.Defines)
                {
                    var value = arguments.TryGetValue(defineName, out var argValue) ? argValue : defaultValue;
                    header.Append("#define ");
                    header.Append(defineName);
                    header.Append(' ');
                    header.Append(value.ToString(CultureInfo.InvariantCulture));
                    header.Append('\n');
                }
            }

            var headerText = header.ToString();

            for (var i = 0; i < shaderObjects.Length; i++)
            {
                CompileShaderObject(shaderObjects[i], shaderFile, originalShaderName, arguments, headerText, shaderSources[i]);
            }
        }

        private static void CompileShaderObject(int shader, string shaderFile, ReadOnlySpan<char> originalShaderName, IReadOnlyDictionary<string, byte> arguments, string headerText, string shaderText)
        {
            string[] sources = [headerText, shaderText];
            int[] lengths = [sources[0].Length, sources[1].Length];

            GL.ShaderSource(shader, sources.Length, sources, lengths);

            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out var shaderStatus);

            if (shaderStatus != 1)
            {
                GL.GetShaderInfoLog(shader, out var log);

                ThrowShaderError(log, string.Concat(shaderFile, GetArgumentDescription(arguments)), originalShaderName, "Failed to set up shader");
            }
        }

        private static void CompileSlangShaderObject(int shader, string shaderFile, ReadOnlySpan<char> originalShaderName, IReadOnlyDictionary<string, byte> arguments, string headerText, string shaderText)
        {
            string[] sources = [headerText, shaderText];
            int[] lengths = [sources[0].Length, sources[1].Length];

            GL.ShaderSource(shader, sources.Length, sources, lengths);

            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out var shaderStatus);

            if (shaderStatus != 1)
            {
                GL.GetShaderInfoLog(shader, out var log);

                ThrowShaderError(log, string.Concat(shaderFile, GetArgumentDescription(arguments)), originalShaderName, "Failed to set up shader");
            }
        }

        private static void ThrowShaderError(string info, string shaderFile, ReadOnlySpan<char> originalShaderName, string errorType)
        {
            // Attempt to parse error message to get the line number so we can print the actual line
            var errorMatch = NvidiaGlslError().Match(info);

            if (!errorMatch.Success)
            {
                errorMatch = AmdGlslError().Match(info);
            }

            if (!errorMatch.Success)
            {
                errorMatch = Mesa3dGlslError().Match(info);
            }

            string? sourceFile = null;
            var errorLine = -1;
            var errorSourceFile = -1;

            if (errorMatch.Success)
            {
                errorSourceFile = int.Parse(errorMatch.Groups["SourceFile"].Value, CultureInfo.InvariantCulture);
                errorLine = int.Parse(errorMatch.Groups["Line"].Value, CultureInfo.InvariantCulture);
                sourceFile = Parser.SourceFiles[errorSourceFile];
            }

#if DEBUG
            // Output GitHub Actions annotation https://docs.github.com/en/actions/reference/workflow-commands-for-github-actions
            if (IsCI)
            {
                var annotation = "::error ";

                if (sourceFile != null)
                {
                    annotation += $"file={ShaderParser.ShaderDirectory.Replace('.', '/')}{sourceFile},";
                    if (errorLine > 0)
                    {
                        annotation += $"line={errorLine},";
                    }
                }

                var errorMessage = $"{info}\n({shaderFile}, original={originalShaderName})";
                annotation += $"title={nameof(ShaderCompilerException)}::{errorMessage.Replace("\n", "%0A", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal)}";

                Console.WriteLine(annotation);
            }
#endif

            if (sourceFile != null)
            {
                info += $"\nError in {sourceFile} on line {errorLine}";

#if DEBUG
                if (errorLine > 0 && errorLine <= Parser.SourceFileLines[errorSourceFile].Count)
                {
                    info += $":\n{Parser.SourceFileLines[errorSourceFile][errorLine - 1]}\n";
                }
#endif
            }

            throw new ShaderCompilerException($"{errorType} {shaderFile} (original={originalShaderName}):\n\n{info}");
        }

        public static string ShaderNameFromPath(string shaderFilePath)
        {
            //return Path.GetFileName(shaderFilePath[..^ShaderFileExtension.Length]);
            return Path.GetFileName(shaderFilePath.Split(".")[0]);
        }

        public const string SlangExtension = ".glSlang";
        public const string ShaderFileExtension = ".vert.glSlang";
        const string VrfInternalShaderPrefix = "vrf.";

        // Map Valve's shader names to shader files VRF has
        private static string GetShaderFileByName(string shaderName) => shaderName switch
        {
            "sky.vfx" => "sky",
            "tools_sprite.vfx" => "sprite",
            "global_lit_simple.vfx" => "global_lit_simple",
            "vr_black_unlit.vfx" or "csgo_black_unlit.vfx" => "vr_black_unlit",
            "water_dota.vfx" => "water",
            "csgo_water_fancy.vfx" => "water_csgo",
            "hero.vfx" or "hero_underlords.vfx" => "dota_hero",
            "multiblend.vfx" => "multiblend",
            "csgo_effects.vfx" => "csgo_effects",
            "csgo_environment.vfx" or "csgo_environment_blend.vfx" => "csgo_environment",

            _ when shaderName.StartsWith(VrfInternalShaderPrefix, StringComparison.Ordinal) => shaderName[VrfInternalShaderPrefix.Length..],
            _ => "complex",
        };

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            ShaderDefines.Clear();
            CachedShaders.Clear();
        }

        private IEnumerable<KeyValuePair<string, byte>> SortAndFilterArguments(string shaderName, IReadOnlyDictionary<string, byte> arguments)
        {
            var defines = ShaderDefines[shaderName];

            return arguments
                //SLANG: This doesn't work when we can't reflect on externs and why would the define have to be known? .Where(p => defines.ContainsKey(p.Key))
                .Where(static p => p.Value != 0) // Shader defines should already default to zero
                .OrderBy(static p => p.Key);
        }

        private static string GetArgumentDescription(IEnumerable<KeyValuePair<string, byte>> arguments)
        {
            var sb = new StringBuilder();
            var first = true;

            foreach (var param in arguments)
            {
                if (first)
                {
                    first = false;
                    sb.Append(" (");
                }
                else
                {
                    sb.Append(", ");
                }

                sb.Append(param.Key);

                if (param.Value != 1)
                {
                    sb.Append('=');
                    sb.Append(param.Value);
                }
            }

            if (!first)
            {
                sb.Append(')');
            }

            return sb.ToString();
        }

        private static readonly byte[] NewLineArray = "\n"u8.ToArray();

        private ulong CalculateShaderCacheHash(string shaderName, IReadOnlyDictionary<string, byte> arguments)
        {
            var hash = new XxHash3(StringToken.MURMUR2SEED);
            hash.Append(MemoryMarshal.AsBytes(shaderName.AsSpan()));

            var argsOrdered = SortAndFilterArguments(shaderName, arguments);
            Span<byte> valueSpan = stackalloc byte[1];

            foreach (var (key, value) in argsOrdered)
            {
                hash.Append(NewLineArray);
                hash.Append(MemoryMarshal.AsBytes(key.AsSpan()));
                hash.Append(NewLineArray);

                valueSpan[0] = value;
                hash.Append(valueSpan);
            }

            return hash.GetCurrentHashAsUInt64();
        }

#if DEBUG
        public void ReloadAllShaders(string? name = null)
        {
            Parser.Reset();

            if (name != null && (ShaderParser.ExtensionToProgramType.Keys.Any(ext => name.EndsWith($".{ext}.glSlang", StringComparison.Ordinal)) || ShaderParser.ExtensionToProgramType.Keys.Any(ext => name.EndsWith($".slang", StringComparison.Ordinal))))
            {
                // If a named shader changed (not an include), then we can only reload this shader
                name = ShaderNameFromPath(name!);
                ParsedCache.Remove(name!);
            }
            else
            {
                // Otherwise reload all shaders (common, etc)
                ParsedCache.Clear();
                name = null;
            }

            foreach (var shader in CachedShaders.Values)
            {
                if (name != null && shader.FileName != name)
                {
                    continue;
                }

                var newShader = CompileAndLinkShader(shader.Name, shader.Parameters, blocking: false);
                shader.ReplaceWith(newShader);
            }
        }

        public static void ValidateShaders(IProgress<string> progressReporter, ILogger logger, string? filter = null)
        {
            using var renderContext = new RendererContext(new ValveResourceFormat.IO.GameFileLoader(null, null), logger);
            var loader = renderContext.ShaderLoader;
            var folder = ShaderParser.GetShaderDiskPath(string.Empty);

            var vertShaders = Directory.GetFiles(folder, "*.vert.slang");
            var compShaders = Directory.GetFiles(folder, "*.comp.slang");
            var allShaders = vertShaders.Concat(compShaders).ToArray();

            // Apply filter if specified
            if (filter != null)
            {
                allShaders = [.. allShaders.Where(s => Path.GetFileName(s).Contains(filter, StringComparison.OrdinalIgnoreCase))];
            }

            var shaders = allShaders;

            GLEnvironment.Initialize(renderContext.Logger);

            foreach (var shader in shaders)
            {
                var shaderName = ShaderNameFromPath(shader);
                var vrfFileName = string.Concat(VrfInternalShaderPrefix, shaderName);

                if (IsCI)
                {
                    Console.WriteLine($"::group::Shader {shaderName}");
                }

                progressReporter.Report($"Compiling {vrfFileName}");

                if (shaderName == "texture_decode")
                {
                    loader.LoadShader(vrfFileName, new Dictionary<string, byte>
                    {
                        ["S_TYPE_TEXTURE2D"] = 1,
                    });
                    continue;
                }

                loader.LoadShader(vrfFileName);

                // Test all defines one by one
                var defines = loader.ShaderDefines[vrfFileName];
                var variants = loader.LastShaderVariantNames;
                var sourceLines = loader.LastShaderSourceLines;
                var maxValues = ExtractMaxDefineValues(defines, sourceLines);

                foreach (var define in defines.Keys)
                {
                    var maxValue = maxValues.GetValueOrDefault(define, 1);

                    for (var value = 1; value <= maxValue; value++)
                    {
                        progressReporter.Report($"Compiling {vrfFileName} with {define}={value}");

                        loader.LoadShader(vrfFileName, new Dictionary<string, byte>
                        {
                            [define] = (byte)value,
                        });
                    }
                }

                // Test all define(xxx_vfx) names
                foreach (var name in variants)
                {
                    var vfxName = string.Concat(name, ".vfx");
                    progressReporter.Report($"Compiling variant {vfxName}");

                    loader.LoadShader(vfxName);

                    // Test all defines one by one in combination with the shader variant name
                    defines = loader.ShaderDefines[vfxName];
                    foreach (var define in defines.Keys)
                    {
                        var maxValue = maxValues.GetValueOrDefault(define, 1);

                        // Test all values from 1 to maxValue
                        for (var value = 1; value <= maxValue; value++)
                        {
                            progressReporter.Report($"Compiling variant {vfxName} with {define}={value}");

                            loader.LoadShader(vfxName, new Dictionary<string, byte>
                            {
                                [define] = (byte)value,
                            });
                        }
                    }

                    // Test all defines at once with their maximum values
                    progressReporter.Report($"Compiling variant {vfxName} with all defines");

                    loader.LoadShader(vfxName, defines.Keys.ToDictionary(static d => d, d => (byte)maxValues.GetValueOrDefault(d, 1)));
                }

                if (IsCI)
                {
                    Console.WriteLine("::endgroup::");
                }
            }

            progressReporter.Report($"Validated {loader.CachedShaders.Count} shader variants");
        }

        private static bool? _isCI;
        private static bool IsCI => _isCI ??= Environment.GetEnvironmentVariable("CI") != null;

        [GeneratedRegex(@"(?<DefineName>(?:F|S|D)_\S+)\s*(?<Operator>>=|<=|>|<|==|!=)\s*(?<Value>\d+)")]
        private static partial Regex ShaderDefineConditions();

        private static Dictionary<string, int> ExtractMaxDefineValues(Dictionary<string, byte> defines, List<List<string>> allSourceLines)
        {
            var maxValues = new Dictionary<string, int>();

            foreach (var sourceLines in allSourceLines)
            {
                foreach (var line in sourceLines)
                {
                    var matches = ShaderDefineConditions().Matches(line);
                    foreach (Match match in matches)
                    {
                        var defineName = match.Groups["DefineName"].Value;
                        var operator_ = match.Groups["Operator"].Value;
                        var value = int.Parse(match.Groups["Value"].Value, CultureInfo.InvariantCulture);

                        if (defines.ContainsKey(defineName))
                        {
                            var testValue = operator_ switch
                            {
                                ">" => value + 1,
                                "<" => Math.Max(1, value - 1),
                                ">=" => value,
                                "<=" => value,
                                "==" => value,
                                "!=" => Math.Max(value + 1, 2),
                                _ => value
                            };

                            if (testValue > maxValues.GetValueOrDefault(defineName, 1))
                            {
                                maxValues[defineName] = testValue;
                            }
                        }
                    }
                }
            }

            return maxValues;
        }
#endif

        /// <summary>
        /// Exception thrown when shader compilation or linking fails.
        /// </summary>
        public class ShaderCompilerException : Exception
        {
            public ShaderCompilerException()
            {
            }

            public ShaderCompilerException(string message) : base(message)
            {
            }

            public ShaderCompilerException(string message, Exception innerException) : base(message, innerException)
            {
            }
        }
    }
}
