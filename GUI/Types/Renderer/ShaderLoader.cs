using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using GUI.Controls;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    partial class ShaderLoader : IDisposable
    {
        [GeneratedRegex(@"(?<SourceFile>[0-9]+)\((?<Line>[0-9]+)\) : error C(?<ErrorNumber>[0-9]+):")]
        private static partial Regex NvidiaGlslError();

        [GeneratedRegex(@"ERROR: (?<SourceFile>[0-9]+):(?<Line>[0-9]+):")]
        private static partial Regex AmdGlslError();

        private readonly Dictionary<ulong, Shader> CachedShaders = [];
        public int ShaderCount => CachedShaders.Count;
        private readonly Dictionary<string, HashSet<string>> ShaderDefines = [];

        private static readonly Dictionary<string, byte> EmptyArgs = [];

        private readonly ShaderParser Parser = new();

        private readonly VrfGuiContext VrfGuiContext;

#if DEBUG
        public ShaderHotReload? ShaderHotReload { get; private set; }
        public HashSet<string> LastShaderVariantNames { get; private set; } = [];
#endif

        public class ParsedShaderData
        {
            public HashSet<string> Defines = [];
            public HashSet<string> RenderModes = [];
            public HashSet<string> SrgbSamplers = [];

#if DEBUG
            public HashSet<string> ShaderVariants = [];
#endif
        }

        public ShaderLoader(VrfGuiContext guiContext)
        {
            VrfGuiContext = guiContext;
        }

#if DEBUG
        public void EnableHotReload(OpenTK.GLControl glControl)
        {
            ShaderHotReload = new(glControl);
            ShaderHotReload.ReloadShader += OnHotReload;
        }
#endif

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

        private Shader CompileAndLinkShader(string shaderName, IReadOnlyDictionary<string, byte> arguments, bool blocking = true)
        {
            var shaderProgram = -1;

            try
            {
                var parsedData = new ParsedShaderData();
                var shaderFileName = GetShaderFileByName(shaderName);

                // Vertex shader
                var vertexName = $"{shaderFileName}.vert";
                var vertexShader = GL.CreateShader(ShaderType.VertexShader);
                LoadShader(vertexShader, vertexName, shaderName, arguments, ref parsedData);
                Parser.Reset();

                // Fragment shader
                var fragmentName = $"{shaderFileName}.frag";
                var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
                LoadShader(fragmentShader, fragmentName, shaderName, arguments, ref parsedData);

                shaderProgram = GL.CreateProgram();

#if DEBUG
                GL.ObjectLabel(ObjectLabelIdentifier.Program, shaderProgram, shaderFileName.Length, shaderFileName);
                GL.ObjectLabel(ObjectLabelIdentifier.Shader, vertexShader, vertexName.Length, vertexName);
                GL.ObjectLabel(ObjectLabelIdentifier.Shader, fragmentShader, fragmentName.Length, fragmentName);
#endif

                var shader = new Shader(VrfGuiContext)
                {
#if DEBUG
                    FileName = shaderFileName,
#endif

                    Name = shaderName,
                    Parameters = arguments,
                    Program = shaderProgram,
                    ShaderObjects = [vertexShader, fragmentShader],
                    RenderModes = parsedData.RenderModes,
                    SrgbSamplers = parsedData.SrgbSamplers
                };

                GL.AttachShader(shader.Program, vertexShader);
                GL.AttachShader(shader.Program, fragmentShader);

                GL.LinkProgram(shader.Program);

                // Not getting link status straight away allows the driver to perform parallelized shader compilation
                // TODO: Ideally we want this to work for initial load too.
                if (blocking)
                {
                    if (!shader.EnsureLoaded())
                    {
                        GL.GetProgramInfoLog(shader.Program, out var log);
                        ThrowShaderError(log, $"{shaderFileName} ({string.Join(", ", arguments.Keys)})", shaderName, "Failed to link shader");
                    }
                }

                ShaderDefines[shaderName] = parsedData.Defines;

#if DEBUG
                LastShaderVariantNames = parsedData.ShaderVariants;
#endif

                var argsDescription = GetArgumentDescription(shaderName, arguments);
                Log.Info(nameof(ShaderLoader), $"Shader '{shaderName}' as '{shaderFileName}' ({argsDescription}) compiled and linked succesfully");

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
                Parser.Reset();
            }
        }

        private void LoadShader(int shader, string shaderFile, string originalShaderName, IReadOnlyDictionary<string, byte> arguments, ref ParsedShaderData parsedData)
        {
            var preprocessedShaderSource = Parser.PreprocessShader(shaderFile, originalShaderName, arguments, parsedData);

            GL.ShaderSource(shader, preprocessedShaderSource);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out var shaderStatus);

            if (shaderStatus != 1)
            {
                GL.GetShaderInfoLog(shader, out var log);
                ThrowShaderError(log, $"{shaderFile} ({string.Join(", ", arguments.Keys)})", originalShaderName, "Failed to set up shader");
            }
        }

        private void ThrowShaderError(string info, string shaderFile, string originalShaderName, string errorType)
        {
            // Attempt to parse error message to get the line number so we can print the actual line
            var errorMatch = NvidiaGlslError().Match(info);

            if (!errorMatch.Success)
            {
                errorMatch = AmdGlslError().Match(info);
            }

            if (errorMatch.Success)
            {
                var errorSourceFile = int.Parse(errorMatch.Groups["SourceFile"].Value, CultureInfo.InvariantCulture);
                var errorLine = int.Parse(errorMatch.Groups["Line"].Value, CultureInfo.InvariantCulture);

                info += $"\nError in {Parser.SourceFiles[errorSourceFile]} on line {errorLine}";

#if DEBUG
                if (errorLine > 0 && errorLine < Parser.SourceFileLines[errorSourceFile].Count)
                {
                    info += $":\n{Parser.SourceFileLines[errorSourceFile][errorLine - 1]}\n";
                }
#endif
            }

            throw new ShaderCompilerException($"{errorType} {shaderFile} (original={originalShaderName}):\n\n{info}");
        }

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
#if DEBUG
            if (ShaderHotReload != null)
            {
                ShaderHotReload.ReloadShader -= OnHotReload;
                ShaderHotReload.Dispose();
                ShaderHotReload = null;
            }
#endif

            ShaderDefines.Clear();
            CachedShaders.Clear();
            GC.SuppressFinalize(this);
        }

        private IEnumerable<KeyValuePair<string, byte>> SortAndFilterArguments(string shaderName, IReadOnlyDictionary<string, byte> arguments)
        {
            var defines = ShaderDefines[shaderName];

            return arguments
                .Where(p => defines.Contains(p.Key))
                .Where(static p => p.Value != 0) // Shader defines should already default to zero
                .OrderBy(static p => p.Key);
        }

        private string GetArgumentDescription(string shaderName, IReadOnlyDictionary<string, byte> arguments)
        {
            var sb = new StringBuilder();
            var first = true;

            foreach (var param in SortAndFilterArguments(shaderName, arguments))
            {
                if (!first)
                {
                    sb.Append(", ");
                }

                first = false;

                sb.Append(param.Key);

                if (param.Value != 1)
                {
                    sb.Append('=');
                    sb.Append(param.Value);
                }
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
        private void OnHotReload(object? sender, string? name)
        {
            var ext = Path.GetExtension(name);

            if (ext is ".frag" or ".vert")
            {
                // If frag or vert file changed, then we can only reload this shader
                name = Path.GetFileNameWithoutExtension(name);
            }
            else
            {
                // Otherwise reload all shaders (common, etc)
                name = null;
            }

            ReloadAllShaders(name);
        }

        public void ReloadAllShaders(string? name = null)
        {
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

        public static void ValidateShaders()
        {
            using var progressDialog = new Forms.GenericProgressForm
            {
                Text = "Compiling shaders…"
            };
            progressDialog.OnProcess += (_, __) =>
            {
                ValidateShadersCore(progressDialog);
            };
            progressDialog.ShowDialog();
        }

        private static void ValidateShadersCore(Forms.GenericProgressForm progressDialog)
        {
            using var context = new VrfGuiContext(null, null);
            using var loader = new ShaderLoader(context);
            var folder = ShaderParser.GetShaderDiskPath(string.Empty);

            var shaders = Directory.GetFiles(folder, "*.frag");

            using var control = new OpenTK.GLControl(OpenTK.Graphics.GraphicsMode.Default, GLViewerControl.OpenGlVersionMajor, GLViewerControl.OpenGlVersionMinor, OpenTK.Graphics.GraphicsContextFlags.Default);
            control.MakeCurrent();

            GLViewerControl.CheckOpenGL();

            foreach (var shader in shaders)
            {
                var shaderFileName = Path.GetFileNameWithoutExtension(shader);
                var vrfFileName = string.Concat(VrfInternalShaderPrefix, shaderFileName);

                progressDialog.SetProgress($"Compiling {vrfFileName}");

                if (shaderFileName == "texture_decode")
                {
                    loader.LoadShader(vrfFileName, new Dictionary<string, byte>
                    {
                        ["S_TYPE_TEXTURE2D"] = 1,
                    });
                    loader.Parser.Reset();
                    continue;
                }

                loader.LoadShader(vrfFileName);

                // Test all defines one by one
                var defines = loader.ShaderDefines[vrfFileName];
                foreach (var define in defines)
                {
                    progressDialog.SetProgress($"Compiling {vrfFileName} with {define}");

                    loader.Parser.Reset();
                    loader.LoadShader(vrfFileName, new Dictionary<string, byte>
                    {
                        [define] = 1,
                    });
                }

                var variants = loader.LastShaderVariantNames;

                // Test all define(xxx_vfx) names
                foreach (var name in variants)
                {
                    var vfxName = string.Concat(name, ".vfx");
                    progressDialog.SetProgress($"Compiling {vfxName}");

                    loader.Parser.Reset();
                    loader.LoadShader(vfxName);

                    // Test all defines one by one in combination with the shader variant name
                    defines = loader.ShaderDefines[vfxName];
                    foreach (var define in defines)
                    {
                        progressDialog.SetProgress($"Compiling {vfxName} with {define}");

                        loader.Parser.Reset();
                        loader.LoadShader(vfxName, new Dictionary<string, byte>
                        {
                            [define] = 1,
                        });
                    }
                }

                loader.Parser.Reset();
            }

            var parsedShaderData = new ParsedShaderData();

            var includes = Directory.GetFiles(folder, "*.glsl");

            foreach (var include in includes)
            {
                var shaderFileName = Path.GetFileName(include);
                var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
                loader.LoadShader(fragmentShader, shaderFileName, shaderFileName, EmptyArgs, ref parsedShaderData);
                loader.Parser.Reset();
                GL.DeleteShader(fragmentShader);
            }

            /*
            includes = Directory.GetFiles(Path.Join(folder, "common"), "*.glsl");

            foreach (var include in includes)
            {
                var shaderFileName = $"common/{Path.GetFileName(include)}";
                var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
                loader.LoadShader(fragmentShader, shaderFileName, shaderFileName, EmptyArgs, ref parsedShaderData);
                GL.DeleteShader(fragmentShader);
            }
            */

            progressDialog.SetProgress("Shaders validated");
        }
#endif

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
