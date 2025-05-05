using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GUI.Controls;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

#nullable disable

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

        private readonly static Dictionary<string, byte> EmptyArgs = [];

        private readonly ShaderParser Parser = new();

        private readonly VrfGuiContext VrfGuiContext;

#if DEBUG
        public ShaderHotReload ShaderHotReload { get; private set; }
#endif

        public class ParsedShaderData
        {
            public HashSet<string> Defines = [];
            public HashSet<string> RenderModes = [];
            public HashSet<string> SrgbSamplers = [];
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

        public Shader LoadShader(string shaderName, IReadOnlyDictionary<string, byte> arguments = null)
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

            var shader = CompileAndLinkShader(shaderName, arguments);
            var newShaderCacheHash = CalculateShaderCacheHash(shaderName, arguments);
            CachedShaders[newShaderCacheHash] = shader;
            return shader;
        }

        private Shader CompileAndLinkShader(string shaderName, IReadOnlyDictionary<string, byte> arguments)
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

                var shader = new Shader
                {
#if DEBUG
                    FileName = shaderFileName,
#endif

                    Name = shaderName,
                    Parameters = arguments,
                    Program = shaderProgram,
                    RenderModes = parsedData.RenderModes,
                    SrgbSamplers = parsedData.SrgbSamplers
                };

                GL.AttachShader(shader.Program, vertexShader);
                GL.AttachShader(shader.Program, fragmentShader);

                GL.LinkProgram(shader.Program);
                GL.GetProgram(shader.Program, GetProgramParameterName.LinkStatus, out var linkStatus);

                GL.DetachShader(shader.Program, vertexShader);
                GL.DeleteShader(vertexShader);

                GL.DetachShader(shader.Program, fragmentShader);
                GL.DeleteShader(fragmentShader);

                if (linkStatus != 1)
                {
                    GL.GetProgramInfoLog(shader.Program, out var log);
                    ThrowShaderError(log, shaderFileName, shaderName, "Failed to link shader");
                }

                VrfGuiContext.MaterialLoader.SetDefaultMaterialParameters(shader.Default);
                shader.StoreAttributeLocations();

                ShaderDefines[shaderName] = parsedData.Defines;

                var argsDescription = GetArgumentDescription(shaderName, arguments);
                Log.Info(nameof(ShaderLoader), $"Shader '{shaderName}' as '{shaderFileName}' ({argsDescription}) compiled and linked succesfully");

                return shader;
            }
            catch (InvalidProgramException)
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
                ThrowShaderError(log, shaderFile, originalShaderName, "Failed to set up shader");
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

            throw new InvalidProgramException($"{errorType} {shaderFile} (original={originalShaderName}):\n{info}");
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
            hash.Append(Encoding.ASCII.GetBytes(shaderName));

            var argsOrdered = SortAndFilterArguments(shaderName, arguments);

            foreach (var (key, value) in argsOrdered)
            {
                hash.Append(NewLineArray);
                hash.Append(Encoding.ASCII.GetBytes(key));
                hash.Append(NewLineArray);
                hash.Append(Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture)));
            }

            return hash.GetCurrentHashAsUInt64();
        }

#if DEBUG
        private void OnHotReload(object sender, string name)
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

        public void ReloadAllShaders(string name = null)
        {
            foreach (var shader in CachedShaders.Values)
            {
                if (name != null && shader.FileName != name)
                {
                    continue;
                }

                var newShader = CompileAndLinkShader(shader.Name, shader.Parameters);

                GL.DeleteProgram(shader.Program);

                shader.Program = newShader.Program;
                shader.RenderModes.Clear();
                shader.RenderModes.UnionWith(newShader.RenderModes);
                shader.ClearUniformsCache();
            }
        }

        public static void ValidateShaders()
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

                loader.LoadShader(shaderFileName);
            }

            var parsedShaderData = new ParsedShaderData();

            var includes = Directory.GetFiles(folder, "*.glsl");

            foreach (var include in includes)
            {
                var shaderFileName = Path.GetFileName(include);
                var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
                loader.LoadShader(fragmentShader, shaderFileName, shaderFileName, EmptyArgs, ref parsedShaderData);
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

            System.Windows.Forms.MessageBox.Show("Shaders validated", "Shaders validated");
            Environment.Exit(0);
        }
#endif
    }
}
