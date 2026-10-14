using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using SlangShaderSharp;
using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.Utils;
using Vortice.Vulkan;

namespace ValveResourceFormat.Renderer.RHI.ShaderCompile
{
    public class SlangShaderCompiler
    {
        IGlobalSession slangGlobalSession;
        ISession slangSession;
        public SlangShaderCompiler()
        {
            Slang.CreateGlobalSession(Slang.ApiVersion, out slangGlobalSession);

            var sessionDesc = new SessionDesc
            {
                Targets = [new TargetDesc 
                {   Format = SlangCompileTarget.Spirv,
                    Profile = slangGlobalSession.FindProfile("spirv_1_6"),

                }],
                DefaultMatrixLayoutMode = SlangMatrixLayoutMode.ColumnMajor,
                CompilerOptionEntries = [
                    new CompilerOptionEntry { Name = CompilerOptionName.BindlessSpaceIndex, Value = CompilerOptionValue.FromInt(1)},
                    //H7per: could change this to 3 later
                    new CompilerOptionEntry { Name = CompilerOptionName.Optimization, Value = CompilerOptionValue.FromInt(2) },
                    new CompilerOptionEntry { Name = CompilerOptionName.DebugInformation, Value = CompilerOptionValue.FromInt(3) }
                    ]
            };

            slangGlobalSession.CreateSession(sessionDesc, out slangSession);
        }

        public void Reset()
        {
            var sessionDesc = new SessionDesc
            {
                Targets = [new TargetDesc
                {   Format = SlangCompileTarget.Spirv,
                    Profile = slangGlobalSession.FindProfile("spirv_1_6"),

                }],
                DefaultMatrixLayoutMode = SlangMatrixLayoutMode.ColumnMajor,
                CompilerOptionEntries = [
                    new CompilerOptionEntry { Name = CompilerOptionName.BindlessSpaceIndex, Value = CompilerOptionValue.FromInt(1)},
                    //H7per: could change this to 3 later
                    new CompilerOptionEntry { Name = CompilerOptionName.Optimization, Value = CompilerOptionValue.FromInt(2) },
                    new CompilerOptionEntry { Name = CompilerOptionName.DebugInformation, Value = CompilerOptionValue.FromInt(3) }
                    ]
            };

            slangGlobalSession.CreateSession(sessionDesc, out slangSession);
        }

        //FOR TESTING
        public unsafe byte[] Compile(string shader)
        {
            var module = slangSession.LoadModuleFromSourceString("test", "../../../Shaders/testShaderDescriptorHandle.slang", shader, out var diagnostics);

            //var error = diagnostics.AsString;

            var layout = module.GetLayout(0, out var diagnosticsLayout);

            var fieldCount = layout.ParameterCount;

            for (uint i = 0; i < fieldCount; i++)
            {
                var field = layout.GetParameterByIndex(i);

                var name = field.Name;
                var typeName = field.Type.Name;

                var typeLayoutName = field.TypeLayout.Name;

                var pushConstantElement = field.TypeLayout.ElementTypeLayout.GetFieldByIndex(0);

                var subName = pushConstantElement.TypeLayout.ContainerVarLayout.Name;

                var descriptorHandleGenericContainer = pushConstantElement.Type.GenericContainer;

                var descriptorHandleType = descriptorHandleGenericContainer.GetConcreteType(descriptorHandleGenericContainer.GetTypeParameter(0));


                Console.WriteLine("hello");
            }

            module.GetTargetCode(0, out var code, out var diagnostics2);

            var span = new Span<byte>(code.GetBufferPointer(), (int)code.GetBufferSize());
            return span.ToArray();
        }

        public (ulong DependencyHash, SlangShaderModule Module) LoadShaderModule(string name)
        {
            ulong hash = 0;

            var module = slangSession.LoadModule(name, out var loadDiagnostics);

            if (module == null)
            {
                if (loadDiagnostics != null)
                    throw new ShaderLoader.ShaderCompilerException(loadDiagnostics.AsString);
                else
                    throw new ShaderLoader.ShaderCompilerException("Unspecified error during shader compilation!");
            }

            List<CompileTimeConstant> compileTimeConstants = new();

            var fileCount = module.GetDependencyFileCount();

            for (int i = 0; i < fileCount; i++)
            {
                var depModuleReflection = slangSession.LoadModule(module.GetDependencyFilePath(i), out var diagDependency)!.GetModuleReflection();

                hash = ShaderHasher.HashFile(module.GetDependencyFilePath(i), hash);

                for (uint child = 0; child < depModuleReflection.ChildrenCount; child++)
                {
                    var param = depModuleReflection.GetChild(child);

                    var asVar = param.AsVariable();

                    if (param.Kind == DeclReflectionKind.Variable && asVar.FindModifier(ModifierID.Extern)! != 0)
                    {
                        int min = -1, max = -1;

                        asVar.GetDefaultValueBlob(out var defBlob);

                        int def = BitConverter.ToInt32(defBlob.Buffer);


                        for (var attribIndex = 0; attribIndex < asVar.AttributeCount; attribIndex++)
                        {
                            var attributeRefl = asVar.GetAttribute((uint)attribIndex);
                            if (attributeRefl.Name == "ConstantRange")
                            {
                                min = attributeRefl.GetArgumentValueInt(0);
                                max = attributeRefl.GetArgumentValueInt(1);
                            }
                        }

                        compileTimeConstants.Add(new() { Name = param.Name, Min = min, Max = max, Default = def });
                    }
                }

            }

            compileTimeConstants.Sort((x, y) => String.Compare(x.Name, y.Name));

            return (hash, new SlangShaderModule(module, compileTimeConstants));
        }

        public SpecialisedShader SpecialiseAndCompile(SlangShaderModule shaderModule, IReadOnlyDictionary<string, int>? arguments = null)
        {
            //guh, thats wild. Need sorting so we can traverse the arguments more easily.

            var safeArguments = arguments ?? new Dictionary<string, int>();

            var constantValues = safeArguments
                .OrderBy(entry => entry.Key)
                .ToList();

            var defaultCompileTimeConstantValues = shaderModule.CompileTimeConstants.Select(a => new CompileTimeConstantValue
            {
                // map shared properties
                Name = a.Name,
                Value = a.Default,

            }).ToArray();

            int defaultsIndex = 0;
            int passedConstantsIndex = 0;


            //totally not C and two plusses in a trench coat
            while (defaultsIndex < defaultCompileTimeConstantValues.Count() &&
                   passedConstantsIndex < constantValues.Count())
            {
                if (constantValues.ElementAt(passedConstantsIndex).Key == defaultCompileTimeConstantValues[defaultsIndex].Name)
                {
                    var val = constantValues.ElementAt(passedConstantsIndex).Value;
                    var min = shaderModule.CompileTimeConstants[defaultsIndex].Min;
                    var max = shaderModule.CompileTimeConstants[defaultsIndex].Max;
                    int clamped = Math.Clamp(val, min, max);

                    if (val != clamped)
                    {
                        Console.WriteLine("Shader Specialisation: passed constant Value not in range!"); 
                        // we would throw a warning here;
                    }

                    defaultCompileTimeConstantValues[defaultsIndex].Value = clamped;

                    passedConstantsIndex++;
                }
                defaultsIndex++;


                if (defaultsIndex + 1 == defaultCompileTimeConstantValues.Length &&
                    passedConstantsIndex < constantValues.Count())
                {
                    //throw std::logic_error("Passed constant(s) have no equivalent in the shader!");
                }
            }



            string specialisationModuleString = "";

            if (arguments != null)
                foreach (var argument in defaultCompileTimeConstantValues)
                {
                    specialisationModuleString += $"export static const int {argument.Name} = {argument.Value}; \n";
                }

            shaderModule.Module.Link(out var linkedComponent, out var diagLink);

            var specialisationModule = 
                slangSession.LoadModuleFromSourceString(
                Regex.Replace(specialisationModuleString, @"[^A-Za-z0-9_\-]+", "_"),
                Regex.Replace(specialisationModuleString, @"[^A-Za-z0-9_\-]+", "_"), 
                specialisationModuleString, 
                out var specialisationModulediagnostics);

            var entryPointCount = shaderModule.Module.GetDefinedEntryPointCount();

            Span<IComponentType> components = new IComponentType[2 + entryPointCount];

            components[0] = linkedComponent;
            components[1] = specialisationModule;


            for (var i = 0; i < entryPointCount; i++)
            {
                shaderModule.Module.GetDefinedEntryPoint(i, out var entry);
                components[2 + i] = entry;
            }


            slangSession.CreateCompositeComponentType(components, out var composedProgram, out var specialisationDiagnostics);

            var composedLayout = composedProgram.GetLayout(0, out var diagCompLayout);
            var moduleLayout = shaderModule.Module.GetLayout(0, out var diagModLayout);

            composedProgram.GetTargetCode(0, out var code, out var diagCode);

           

            //is allocating new bad here?
            Memory<byte> spirv = code.Buffer.ToArray();
            Dictionary<VkShaderStageFlags, string> stages = new();
            List<VertexInput> vertexInputs = new();
            List<StructMember> pushConstants = new();
            ParameterLayout? parameterLayout = null;

            List<StructMember> compileTimeConstants = new();

            var entryPointVertex = composedLayout.GetEntryPointByIndex(0);


            VkShaderStageFlags GetVkStageFlag(SlangStage stage)
            {
                switch (stage) {
                    case SlangStage.Vertex:
                        return VkShaderStageFlags.Vertex;
                    case SlangStage.Fragment:
                        return VkShaderStageFlags.Fragment;
                    case SlangStage.Compute:
                        return VkShaderStageFlags.Compute;
                    case SlangStage.Mesh:
                        return VkShaderStageFlags.MeshEXT;
                    default:
                        return 0;
                }
            }

            for (uint i = 0; i < composedLayout.EntryPointCount; i++)
            {
                var entryReflection = composedLayout.GetEntryPointByIndex(i);

                stages.Add(GetVkStageFlag(entryReflection.Stage), entryReflection.Name);

                if (entryReflection.Stage == SlangStage.Vertex)
                {
                    vertexInputs = ReflectVertexInputs(entryReflection);
                }
            }

            bool pushConstantBlockFound = false;
            bool parametersFound = false;

            for (uint i = 0; i < composedLayout.ParameterCount; i++)
            {
                var param = composedLayout.GetParameterByIndex(i);

                if (param.Category == SlangParameterCategory.PushConstantBuffer)
                {
                    if (pushConstantBlockFound == true)
                    {
                        throw new Exception("More than one push constant block found!");
                    }
                    pushConstantBlockFound = true;


                    var pushConstantFieldCount = param.TypeLayout.ElementTypeLayout.FieldCount;

                    for (uint pushElement = 0; pushElement < pushConstantFieldCount; pushElement++)
                    {



                        var element = param.TypeLayout.ElementTypeLayout.GetFieldByIndex(pushElement);

                        string name = element.Name;
                        string type = GetTypeString(element.Type);

                        //hardcoded, it makes life easier
                        if (type == "DescriptorHandle<ConstantBuffer<Parameters>>")
                        {
                            if (parametersFound == true)
                            {
                                throw new Exception("More than one parameter block found!");
                            }

                            var outerGenericContainer = element.Type.GenericContainer;

                            var innerGenericContainer = outerGenericContainer.GetConcreteType(outerGenericContainer.GetTypeParameter(0)).GenericContainer;

                            var parameterStruct = innerGenericContainer.GetConcreteType(innerGenericContainer.GetTypeParameter(0));

                            parameterLayout = ReflectParameterLayout(parameterStruct);

                            parametersFound = true;
                        }

                        uint offset = (uint)element.GetOffset();

                        var elementType = element.Type;

                        var genericContainer = elementType.GenericContainer;

                        pushConstants.Add(new() { Name = name, Type = type, Offset = offset });

                    }
                }

            }



            return new SpecialisedShader(
    Spirv: spirv,
    Stages: stages,
    VertexInputs: vertexInputs,
    PushConstants: pushConstants,
    MaterialParameters: parameterLayout,
    CompileTimeConstantValues: defaultCompileTimeConstantValues);
        }


        private ParameterLayout ReflectParameterLayout(TypeReflection parameterStruct)
        {
            var typeLayout = slangSession.GetTypeLayout(parameterStruct, 0, LayoutRules.DefaultConstantBuffer, out _);
            var size = (uint)typeLayout.GetSize(SlangParameterCategory.Uniform);

            var members = new List<ParameterMember>((int)typeLayout.FieldCount);
            var defaultBytes = new byte[size];

            for (uint i = 0; i < typeLayout.FieldCount; i++)
            {
                var field = typeLayout.GetFieldByIndex(i);
                var member = ReflectParameterMember(field);

                members.Add(member);
                WriteParameterDefault(field.Variable, member, defaultBytes);
            }

            return new ParameterLayout(members, size, defaultBytes);
        }

        private static ParameterMember ReflectParameterMember(VariableLayoutReflection field)
        {
            var name = field.Name;
            var offset = (uint)field.GetOffset();
            var size = (uint)field.TypeLayout.GetSize(SlangParameterCategory.Uniform);
            var flags = HasAttribute(field.Variable, "SrgbRead") ? ParameterFlags.SrgbRead : ParameterFlags.None;

            // The type layout of a handle is its lowered uint2, only the declared type knows what it points at
            if (field.Type.Kind == SlangTypeKind.Struct && field.Type.Name == "DescriptorHandle")
            {
                var handleGeneric = field.Type.GenericContainer;
                var resourceType = handleGeneric.GetConcreteType(handleGeneric.GetTypeParameter(0));

                var dimension = resourceType.Kind == SlangTypeKind.Resource
                    ? GetTextureDimension(resourceType.ResourceShape)
                    : TextureDimension.None;

                if (dimension == TextureDimension.None)
                {
                    throw new NotSupportedException($"Material parameter '{name}' is a handle to '{resourceType.Name}' ({resourceType.ResourceShape}), only non-multisampled textures are supported");
                }

                return new ParameterMember(name, ElementType.None, 1, 1, offset, size, dimension, flags);
            }

            var typeLayout = field.TypeLayout;

            if (typeLayout.Kind is not (SlangTypeKind.Scalar or SlangTypeKind.Vector or SlangTypeKind.Matrix))
            {
                throw new NotSupportedException($"Material parameter '{name}' is a {typeLayout.Kind}, only scalars, vectors, matrices and texture handles are supported");
            }

            var elementType = typeLayout.ScalarType switch
            {
                SlangScalarType.Float32 => ElementType.Float,
                SlangScalarType.Int32 => ElementType.Int,
                SlangScalarType.UInt32 => ElementType.Uint,
                SlangScalarType.Bool => ElementType.Bool,
                _ => throw new NotSupportedException($"Material parameter '{name}' has {typeLayout.ScalarType} components, only 32 bit float, int, uint and bool are supported"),
            };

            var rows = typeLayout.Kind == SlangTypeKind.Matrix ? typeLayout.RowCount : 1;
            var columns = typeLayout.Kind == SlangTypeKind.Scalar ? 1 : typeLayout.ColumnCount;

            return new ParameterMember(name, elementType, (byte)rows, (byte)columns, offset, size, TextureDimension.None, flags);
        }

        private static void WriteParameterDefault(VariableReflection variable, in ParameterMember member, byte[] defaultBytes)
        {
            // A null blob means there is no initializer, a failure means there is one reflection cannot evaluate
            var result = variable.GetDefaultValueBlob(out var blob);

            if (result.Failed)
            {
                throw new NotSupportedException($"The default value of material parameter '{member.Name}' cannot be reflected ({result.GetSymbolicName()}), remove the initializer or simplify it to a constant");
            }

            if (blob == null)
            {
                return;
            }

            var value = blob.Buffer;

            if ((uint)value.Length != member.Size)
            {
                throw new NotSupportedException($"The default value of material parameter '{member.Name}' is {value.Length} bytes, expected {member.Size}");
            }

            var target = defaultBytes.AsSpan((int)member.Offset, (int)member.Size);
            value.CopyTo(target);

            if (member.IsSrgbRead && member.ElementType == ElementType.Float)
            {
                var color = MemoryMarshal.Cast<byte, float>(target);
                var linear = ColorSpace.SrgbGammaToLinear(new Vector3(color[0], color[1], color[2]));

                color[0] = linear.X;
                color[1] = linear.Y;
                color[2] = linear.Z;
            }
        }

        private static bool HasAttribute(VariableReflection variable, string name)
        {
            for (uint i = 0; i < variable.AttributeCount; i++)
            {
                if (variable.GetAttribute(i).Name == name)
                {
                    return true;
                }
            }

            return false;
        }

        private static TextureDimension GetTextureDimension(SlangResourceShape shape)
        {
            // Combined sampling and shadow comparison do not change the shape, multisampling does
            const SlangResourceShape ShapeMask = SlangResourceShape.BaseShapeMask | SlangResourceShape.TextureArrayFlag | SlangResourceShape.TextureMultisampleFlag;

            return (shape & ShapeMask) switch
            {
                SlangResourceShape.Texture1D => TextureDimension.Texture1D,
                SlangResourceShape.Texture2D => TextureDimension.Texture2D,
                SlangResourceShape.Texture3D => TextureDimension.Texture3D,
                SlangResourceShape.TextureCube => TextureDimension.TextureCube,
                SlangResourceShape.Texture1DArray => TextureDimension.Texture1DArray,
                SlangResourceShape.Texture2DArray => TextureDimension.Texture2DArray,
                SlangResourceShape.TextureCubeArray => TextureDimension.TextureCubeArray,
                _ => TextureDimension.None,
            };
        }

        private List<VertexInput> ReflectVertexInputs(EntryPointReflection vertexStageReflection)
        {
            var vertexInputs = new List<VertexInput>();

            for (uint i = 0; i < vertexStageReflection.ParameterCount; i++)
            {
                var param = vertexStageReflection.GetParameterByIndex(i);

                if (param.Category == SlangParameterCategory.VaryingInput)
                    ReflectVertexParameter(param, 0, ref vertexInputs);
            }

            return vertexInputs;
        }

        private void ReflectVertexParameter(VariableLayoutReflection vertexInputReflection, uint locationOffset, ref List<VertexInput> vertexInputs)
        {
            if (vertexInputReflection.TypeLayout.Kind == SlangTypeKind.Struct)
            {
                for (uint iElement = 0; iElement < vertexInputReflection.TypeLayout.FieldCount; iElement++)
                {
                    var element = vertexInputReflection.TypeLayout.GetFieldByIndex(iElement);

                    //This also catches conditionals that are disabled, conveniently.
                    if (element.Category == SlangParameterCategory.VaryingInput)
                    {
                        ReflectVertexParameter(element, vertexInputReflection.BindingIndex + locationOffset, ref vertexInputs);
                    }
                }
            }
            else
            {

                VkFormat vkFormat;
                uint elementSize = 0;
                switch (vertexInputReflection.TypeLayout.ScalarType)
                {
                    case SlangScalarType.Float32:
                        vkFormat = VkFormat.R32Sfloat;
                        elementSize = 4;
                        break;
                    case SlangScalarType.Int32:
                        vkFormat = VkFormat.R32Sint;
                        elementSize = 4;
                        break;
                    case SlangScalarType.UInt32:
                        vkFormat = VkFormat.R32Uint;
                        elementSize = 4;
                        break;
                    default:
                        vkFormat = 0;
                        break;
                }

                var elementCount = Math.Max((uint)vertexInputReflection.TypeLayout.ColumnCount, 1);
                //a bit evil but this enum is static, so this trick won't just break with a new Vulkan version or anything.
                vkFormat = (VkFormat)((uint)vkFormat + (elementCount - 1) * 3);

                vertexInputs.Add(new VertexInput()
                {
                    SemanticName = vertexInputReflection.SemanticName,
                    SemanticIndex = (uint)vertexInputReflection.SemanticIndex,
                    Format = vkFormat,
                    Location = vertexInputReflection.BindingIndex + locationOffset,
                    Size = elementCount * elementSize
                });
            }
        }

        private void ReflectNormalData(VariableLayoutReflection normalInputReflection, ref List<VertexInput> vertexInputs)
        {

            Debug.Assert(normalInputReflection.TypeLayout.FieldCount == 2);
            Debug.Assert(normalInputReflection.TypeLayout.Name == "NormalData");

            var isCompressed = normalInputReflection.TypeLayout.GetFieldByIndex(1).TypeLayout.ColumnCount == 1;

            vertexInputs.Add(new VertexInput()
            {
                SemanticName = normalInputReflection.SemanticName,
                SemanticIndex = (uint)normalInputReflection.SemanticIndex,
                Format = isCompressed ? VkFormat.R32Uint : VkFormat.R16G16B16Sfloat,
                Location = normalInputReflection.BindingIndex,
                Size = (uint)(isCompressed ? 4 : 2 * 3)
            });
        }
        private string GetTypeString(TypeReflection typeReflection)
        {
            var genericContainer = typeReflection.GenericContainer;

            if (genericContainer.TypeParameterCount != 0 && (typeReflection.Kind == SlangTypeKind.Struct || typeReflection.Kind == SlangTypeKind.ConstantBuffer))
            {
                return typeReflection.Name + $"<{GetTypeString(genericContainer.GetConcreteType(genericContainer.GetTypeParameter(0)))}>";
            }
            else
            {
                if (typeReflection.Kind == SlangTypeKind.Vector)
                {
                    return typeReflection.ElementType.Name + $"{typeReflection.ColumnCount}";
                }
                if (typeReflection.Kind == SlangTypeKind.Array)
                {
                    return GetTypeString(typeReflection.ElementType) + $"[{typeReflection.ElementCount}]";
                }
                if (typeReflection.Kind == SlangTypeKind.Matrix)
                {
                    return GetTypeString(typeReflection.ElementType) + $"{typeReflection.RowCount}x" + $"{typeReflection.ColumnCount}";
                }
                return typeReflection.Name;
            }
        }
    }

    public static class ShaderHasher
    {
        // FNV-1a 64-bit constants
        private const ulong FnvOffsetBasis64 = 14695981039346656037UL;
        private const ulong FnvPrime64 = 1099511628211UL;

        public static ulong HashFile(string path, ulong prevHash = 0)
        {
            string normalized = Normalize(File.ReadAllText(path));
            return prevHash ^ Fnv1a64(normalized);
        }

        public static (ulong moduleHash, Dictionary<string, ulong> fileHashes) HashModuleDetailed(IReadOnlyList<string> filePaths)
        {
            var fileHashes = new Dictionary<string, ulong>();
            ulong moduleHash = 0;
            foreach (var path in filePaths)
            {
                ulong h = HashFile(path);
                fileHashes[path] = h;
                moduleHash ^= h;
            }
            return (moduleHash, fileHashes);
        }

        private static ulong Fnv1a64(string s)
        {
            ulong hash = FnvOffsetBasis64;
            foreach (byte b in Encoding.UTF8.GetBytes(s))
            {
                hash ^= b;
                hash *= FnvPrime64;
            }
            return hash;
        }

        private static string Normalize(string src)
        {
            var sb = new StringBuilder(src.Length);
            int i = 0, n = src.Length;

            bool pendingWs = false;
            bool pendingWsHasNewline = false;

            void FlushPendingWs(char nextChar)
            {
                if (!pendingWs) return;

                char prevChar = sb.Length > 0 ? sb[sb.Length - 1] : '\0';

                if (pendingWsHasNewline)
                {
                    // Only keep a space if dropping the newline would merge two words together.
                    if (IsWordChar(prevChar) && IsWordChar(nextChar))
                        sb.Append(' ');
                    // otherwise: newline is ignored entirely, no space emitted
                }
                else
                {
                    sb.Append(' '); // plain space/tab runs always collapse to one space
                }

                pendingWs = false;
                pendingWsHasNewline = false;
            }

            while (i < n)
            {
                char c = src[i];

                if (c == '"' || c == '\'')
                {
                    FlushPendingWs(c);
                    char quote = c;
                    sb.Append(c);
                    i++;
                    while (i < n)
                    {
                        sb.Append(src[i]);
                        if (src[i] == '\\' && i + 1 < n) { sb.Append(src[i + 1]); i += 2; continue; }
                        if (src[i] == quote) { i++; break; }
                        i++;
                    }
                    continue;
                }

                if (c == '/' && i + 1 < n && src[i + 1] == '/')
                {
                    i += 2;
                    while (i < n && src[i] != '\n') i++;
                    continue; // the trailing '\n' (if any) is handled next iteration as normal whitespace
                }

                if (c == '/' && i + 1 < n && src[i + 1] == '*')
                {
                    i += 2;
                    while (i < n && !(src[i] == '*' && i + 1 < n && src[i + 1] == '/')) i++;
                    i += 2;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    pendingWs = true;
                    if (c == '\n') pendingWsHasNewline = true;
                    i++;
                    continue;
                }

                FlushPendingWs(c);
                sb.Append(c);
                i++;
            }

            return sb.ToString(); // trailing pending whitespace is simply dropped
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
