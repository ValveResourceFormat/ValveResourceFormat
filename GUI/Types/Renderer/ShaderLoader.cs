using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Utils;

namespace GUI.Types.Renderer
{
    partial class ShaderLoader : IDisposable
    {
        private const string RenderModeDefinePrefix = "renderMode_";

        [GeneratedRegex(@"(?<SourceFile>[0-9]+)\((?<Line>[0-9]+)\) : error C(?<ErrorNumber>[0-9]+):")]
        private static partial Regex NvidiaGlslError();

        [GeneratedRegex(@"ERROR: (?<SourceFile>[0-9]+):(?<Line>[0-9]+):")]
        private static partial Regex AmdGlslError();

        private readonly Dictionary<ulong, Shader> CachedShaders = [];
        public int ShaderCount => CachedShaders.Count;
        private readonly Dictionary<string, HashSet<string>> ShaderDefines = [];

        private readonly static Dictionary<string, byte> EmptyArgs = new(0);

        private readonly ShaderParser Parser = new();

        public class ParsedShaderData
        {
            public HashSet<string> Defines = [];
            public HashSet<string> SrgbSamplers = [];
        }

        public Shader LoadShader(string shaderName, IReadOnlyDictionary<string, byte> arguments = null)
        {
            var shaderFileName = GetShaderFileByName(shaderName);
            arguments ??= EmptyArgs;

            if (ShaderDefines.ContainsKey(shaderName))
            {
                var shaderCacheHash = CalculateShaderCacheHash(shaderName, arguments);

                if (CachedShaders.TryGetValue(shaderCacheHash, out var cachedShader))
                {
                    return cachedShader;
                }
            }

            var shaderProgram = -1;

            try
            {
                var parsedData = new ParsedShaderData();

                // Vertex shader
                var vertexName = $"{shaderFileName}.vert";
                var vertexShader = GL.CreateShader(ShaderType.VertexShader);
                LoadShader(vertexShader, vertexName, shaderName, arguments, ref parsedData);

                // Fragment shader
                var fragmentName = $"{shaderFileName}.frag";
                var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
                LoadShader(fragmentShader, fragmentName, shaderName, arguments, ref parsedData);

                var renderModes = parsedData.Defines
                    .Where(k => k.StartsWith(RenderModeDefinePrefix, StringComparison.Ordinal))
                    .Select(k => k[RenderModeDefinePrefix.Length..])
                    .ToHashSet();

                shaderProgram = GL.CreateProgram();


#if DEBUG
                GL.ObjectLabel(ObjectLabelIdentifier.Program, shaderProgram, shaderFileName.Length, shaderFileName);
                GL.ObjectLabel(ObjectLabelIdentifier.Shader, vertexShader, vertexName.Length, vertexName);
                GL.ObjectLabel(ObjectLabelIdentifier.Shader, fragmentShader, fragmentName.Length, fragmentName);
#endif


                var shader = new Shader
                {
                    Name = shaderName,
                    Parameters = arguments,
                    Program = shaderProgram,
                    RenderModes = renderModes,
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

                MaterialLoader.ApplyMaterialDefaults(shader.Default);

                ShaderDefines[shaderName] = parsedData.Defines;
                var newShaderCacheHash = CalculateShaderCacheHash(shaderName, arguments);
                CachedShaders[newShaderCacheHash] = shader;

                var argsDescription = GetArgumentDescription(shaderName, arguments);
                Log.Info(nameof(ShaderLoader), $"Shader '{shaderName}' as '{shaderFileName}' ({argsDescription}) {newShaderCacheHash} compiled and linked succesfully");
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

        // Map Valve's shader names to shader files VRF has
        private static string GetShaderFileByName(string shaderName)
        {
            if (shaderName.Contains("black_unlit", StringComparison.InvariantCulture))
            {
                return "vr_black_unlit";
            }

            switch (shaderName)
            {
                case "vrf.default":
                    return "default";
                case "vrf.grid":
                    return "grid";
                case "vrf.picking":
                    return "picking";
                case "vrf.particle.sprite":
                    return "particle_sprite";
                case "vrf.particle.trail":
                    return "particle_trail";
                case "vrf.morph_composite":
                    return "morph_composite";
                case "sky.vfx":
                    return "sky";
                case "tools_sprite.vfx":
                    return "sprite";
                case "global_lit_simple.vfx":
                    return "global_lit_simple";
                case "water_dota.vfx":
                    return "water";
                case "csgo_water_fancy.vfx":
                    return "water_csgo";
                case "hero.vfx":
                case "hero_underlords.vfx":
                    return "dota_hero";
                case "multiblend.vfx":
                    return "multiblend";
                case "csgo_effects.vfx":
                    return "csgo_effects";
                case "csgo_environment.vfx":
                case "csgo_environment_blend.vfx":
                    return "csgo_environment";
                default:
                    return "complex";
            }
        }

        public void ClearCache()
        {
            /* Do not destroy all shaders for now because not all scene nodes reload their own shaders yet
            foreach (var shader in CachedShaders.Values)
            {
                GL.DeleteProgram(shader.Program);
            }
            */

            ShaderDefines.Clear();
            CachedShaders.Clear();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearCache();
#if DEBUG
                ShaderWatcher.Dispose();
#endif
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private IEnumerable<KeyValuePair<string, byte>> SortAndFilterArguments(string shaderName, IReadOnlyDictionary<string, byte> arguments)
        {
            return arguments
                .Where(p => ShaderDefines[shaderName].Contains(p.Key))
                .OrderBy(p => p.Key);
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

#if DEBUG // Reload shaders at runtime
        public FileSystemWatcher ShaderWatcher { get; } = new()
        {
            Path = ShaderParser.GetShaderDiskPath(string.Empty),
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
        };

        public ShaderLoader()
        {
            ShaderWatcher.Filters.Add("*.glsl");
            ShaderWatcher.Filters.Add("*.vert");
            ShaderWatcher.Filters.Add("*.frag");
        }

        public static void ValidateShaders()
        {
            using var loader = new ShaderLoader();
            var folder = ShaderParser.GetShaderDiskPath(string.Empty);

            var shaders = Directory.GetFiles(folder, "*.frag");

            using var control = new OpenTK.GLControl(OpenTK.Graphics.GraphicsMode.Default, 4, 6, OpenTK.Graphics.GraphicsContextFlags.Default);
            control.MakeCurrent();

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
