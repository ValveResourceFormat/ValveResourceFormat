using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ThirdParty;

namespace GUI.Types.Renderer
{
    public class ShaderLoader : IDisposable
    {
        private const string ShaderDirectory = "GUI.Types.Renderer.Shaders.";
        private const int ShaderSeed = 0x13141516;
        private static readonly Regex RegexInclude = new(@"^#include ""(?<IncludeName>[^""]+)""\r?$", RegexOptions.Multiline);
        private static readonly Regex RegexDefine = new(@"^#define (?<ParamName>\S+) (?<DefaultValue>\S+)", RegexOptions.Multiline);

        private readonly Dictionary<uint, Shader> CachedShaders = new();
        private readonly Dictionary<string, List<string>> ShaderDefines = new();

        private static IReadOnlyDictionary<string, byte> EmptyArgs { get; } = new Dictionary<string, byte>(0);

        public Shader LoadShader(string shaderName, IReadOnlyDictionary<string, byte> arguments = null)
        {
            var shaderFileName = GetShaderFileByName(shaderName);
            arguments ??= EmptyArgs;

            if (ShaderDefines.ContainsKey(shaderFileName))
            {
                var shaderCacheHash = CalculateShaderCacheHash(shaderFileName, arguments);

                if (CachedShaders.TryGetValue(shaderCacheHash, out var cachedShader))
                {
                    return cachedShader;
                }
            }

            var defines = new List<string>();

            /* Vertex shader */
            var vertexShader = GL.CreateShader(ShaderType.VertexShader);

            var assembly = Assembly.GetExecutingAssembly();

#if DEBUG
            using (var stream = File.Open(GetShaderDiskPath($"{shaderFileName}.vert"), FileMode.Open))
#else
            using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{shaderFileName}.vert"))
#endif
            using (var reader = new StreamReader(stream))
            {
                var shaderSource = reader.ReadToEnd();
                var preprocessedShaderSource = PreprocessShader(shaderSource, arguments);
                GL.ShaderSource(vertexShader, preprocessedShaderSource);

                // Find defines supported from source
                defines.AddRange(FindDefines(preprocessedShaderSource));
            }

            GL.CompileShader(vertexShader);

            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out var shaderStatus);

            if (shaderStatus != 1)
            {
                GL.GetShaderInfoLog(vertexShader, out var vsInfo);

                throw new InvalidProgramException($"Error setting up Vertex Shader {shaderFileName} ({shaderName}):\n{vsInfo}");
            }

            /* Fragment shader */
            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);

#if DEBUG
            using (var stream = File.Open(GetShaderDiskPath($"{shaderFileName}.frag"), FileMode.Open))
#else
            using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{shaderFileName}.frag"))
#endif
            using (var reader = new StreamReader(stream))
            {
                var shaderSource = reader.ReadToEnd();
                var preprocessedShaderSource = PreprocessShader(shaderSource, arguments);
                GL.ShaderSource(fragmentShader, preprocessedShaderSource);

                // Find render modes supported from source, take union to avoid duplicates
                defines = defines.Union(FindDefines(preprocessedShaderSource)).ToList();
            }

            GL.CompileShader(fragmentShader);

            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out shaderStatus);

            if (shaderStatus != 1)
            {
                GL.GetShaderInfoLog(fragmentShader, out var fsInfo);

                throw new InvalidProgramException($"Error setting up Fragment Shader {shaderFileName} ({shaderName}):\n{fsInfo}");
            }

            const string renderMode = "renderMode_";
            var renderModes = defines
                .Where(k => k.StartsWith(renderMode, StringComparison.InvariantCulture))
                .Select(k => k[renderMode.Length..])
                .ToList();

            var shader = new Shader
            {
                Name = shaderName,
                Parameters = arguments,
                Program = GL.CreateProgram(),
                RenderModes = renderModes,
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
                GL.GetProgramInfoLog(shader.Program, out var programLog);
                throw new InvalidProgramException($"Error linking shader \"{shaderName}\": {programLog}");
            }

            ShaderDefines[shaderFileName] = defines;
            var newShaderCacheHash = CalculateShaderCacheHash(shaderFileName, arguments);

            CachedShaders[newShaderCacheHash] = shader;

            Console.WriteLine($"Shader {newShaderCacheHash} ('{shaderName}' as '{shaderFileName}') ({string.Join(", ", arguments.Keys)}) compiled and linked succesfully");
            return shader;
        }

        private static string PreprocessShader(string source, IReadOnlyDictionary<string, byte> arguments)
        {
            //Inject code into shader based on #includes
            var withCollapsedIncludes = ResolveIncludes(source);

            //Update parameter defines
            var withReplacedDefines = UpdateDefines(withCollapsedIncludes, arguments);
            return withReplacedDefines;
        }

        //Update default defines with possible overrides from the model
        private static string UpdateDefines(string source, IReadOnlyDictionary<string, byte> arguments)
        {
            //Find all #define param_(paramName) (paramValue) using regex
            var defines = RegexDefine.Matches(source);

            foreach (var define in defines.Cast<Match>())
            {
                //Check if this parameter is in the arguments
                if (!arguments.TryGetValue(define.Groups["ParamName"].Value, out var value))
                {
                    continue;
                }

                //Overwrite default value
                var defaultValue = define.Groups["DefaultValue"];
                var index = defaultValue.Index;
                var length = defaultValue.Length;
                var newValue = value.ToString(CultureInfo.InvariantCulture);

                source = source.Remove(index, Math.Min(length, source.Length - index)).Insert(index, newValue);
            }

            return source;
        }

        //Remove any #includes from the shader and replace with the included code
        private static string ResolveIncludes(string source)
        {
            var assembly = Assembly.GetExecutingAssembly();

            var includes = RegexInclude.Matches(source);

            foreach (var define in includes.Cast<Match>())
            {
#if DEBUG
                using var stream = File.Open(GetShaderDiskPath(define.Groups["IncludeName"].Value), FileMode.Open);
#else
                var includeResource = define.Groups["IncludeName"].Value.Replace('/', '.');
                using var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{includeResource}");
#endif
                using var reader = new StreamReader(stream);
                var includedCode = reader.ReadToEnd();

                //Recursively resolve includes in the included code. (Watch out for cyclic dependencies!)
                includedCode = ResolveIncludes(includedCode);

                //Replace the include with the code
                source = source.Replace(define.Value, includedCode, StringComparison.InvariantCulture);
            }

            return source;
        }

        private static List<string> FindDefines(string source)
        {
            var defines = RegexDefine.Matches(source);
            return defines.Select(match => match.Groups["ParamName"].Value).ToList();
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
                case "vrf.error":
                    return "error";
                case "vrf.grid":
                    return "debug_grid";
                case "vrf.picking":
                    return "picking";
                case "vrf.particle.sprite":
                    return "particle_sprite";
                case "vrf.particle.trail":
                    return "particle_trail";
                case "sky.vfx":
                    return "sky";
                case "tools_sprite.vfx":
                    return "sprite";
                case "vr_unlit.vfx":
                    return "vr_unlit";
                case "vr_black_unlit.vfx":
                    return "vr_black_unlit";
                case "water_dota.vfx":
                    return "water";
                case "hero.vfx":
                case "hero_underlords.vfx":
                    return "dota_hero";
                case "multiblend.vfx":
                    return "multiblend";
                case "csgo_effects.vfx":
                    return "csgo_effects";
                default:
                    return "simple";
            }
        }

        public void ClearCache()
        {
            ShaderDefines.Clear();
            CachedShaders.Clear();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearCache();

                foreach (var shader in CachedShaders.Values)
                {
                    GL.DeleteProgram(shader.Program);
                }
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private uint CalculateShaderCacheHash(string shaderFileName, IReadOnlyDictionary<string, byte> arguments)
        {
            var shaderCacheHashString = new StringBuilder();
            shaderCacheHashString.AppendLine(shaderFileName);

            var parameters = ShaderDefines[shaderFileName].Intersect(arguments.Keys);

            foreach (var key in parameters)
            {
                shaderCacheHashString.AppendLine(key);
                shaderCacheHashString.AppendLine(arguments[key].ToString(CultureInfo.InvariantCulture));
            }

            return MurmurHash2.Hash(shaderCacheHashString.ToString(), ShaderSeed);
        }

#if DEBUG
        // Reload shaders at runtime
        private static string GetShaderDiskPath(string name)
        {
            var guiFolderRoot = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName);

            while (Path.GetFileName(guiFolderRoot) != "GUI")
            {
                guiFolderRoot = Path.GetDirectoryName(guiFolderRoot);
            }

            return Path.Combine(Path.GetDirectoryName(guiFolderRoot), ShaderDirectory.Replace('.', '/'), name);
        }
#endif
    }
}
