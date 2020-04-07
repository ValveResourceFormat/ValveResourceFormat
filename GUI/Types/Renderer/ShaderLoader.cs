//#define DEBUG_SHADERS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ThirdParty;

namespace GUI.Types.Renderer
{
    public class ShaderLoader
    {
        private const string ShaderDirectory = "GUI.Types.Renderer.Shaders.";
        private const int ShaderSeed = 0x13141516;

#if !DEBUG_SHADERS || !DEBUG
        private readonly Dictionary<uint, Shader> CachedShaders = new Dictionary<uint, Shader>();
        private readonly Dictionary<string, List<string>> ShaderDefines = new Dictionary<string, List<string>>();
#endif

        public Shader LoadShader(string shaderName, IDictionary<string, bool> arguments)
        {
            var shaderFileName = GetShaderFileByName(shaderName);

#if !DEBUG_SHADERS || !DEBUG
            if (ShaderDefines.ContainsKey(shaderFileName))
            {
                var shaderCacheHash = CalculateShaderCacheHash(shaderFileName, arguments);

                if (CachedShaders.TryGetValue(shaderCacheHash, out var cachedShader))
                {
                    return cachedShader;
                }
            }
#endif

            var defines = new List<string>();

            /* Vertex shader */
            var vertexShader = GL.CreateShader(ShaderType.VertexShader);

            var assembly = Assembly.GetExecutingAssembly();

#if DEBUG_SHADERS && DEBUG
            using (var stream = File.Open(GetShaderDiskPath($"{shaderFileName}.vert"), FileMode.Open))
#else
            using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{shaderFileName}.vert"))
#endif
            using (var reader = new StreamReader(stream))
            {
                var shaderSource = reader.ReadToEnd();
                GL.ShaderSource(vertexShader, PreprocessVertexShader(shaderSource, arguments));

                // Find defines supported from source
                defines.AddRange(FindDefines(shaderSource));
            }

            GL.CompileShader(vertexShader);

            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out var shaderStatus);

            if (shaderStatus != 1)
            {
                GL.GetShaderInfoLog(vertexShader, out var vsInfo);

                throw new Exception($"Error setting up Vertex Shader \"{shaderName}\": {vsInfo}");
            }

            /* Fragment shader */
            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);

#if DEBUG_SHADERS && DEBUG
            using (var stream = File.Open(GetShaderDiskPath($"{shaderFileName}.frag"), FileMode.Open))
#else
            using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{shaderFileName}.frag"))
#endif
            using (var reader = new StreamReader(stream))
            {
                var shaderSource = reader.ReadToEnd();
                GL.ShaderSource(fragmentShader, UpdateDefines(shaderSource, arguments));

                // Find render modes supported from source, take union to avoid duplicates
                defines = defines.Union(FindDefines(shaderSource)).ToList();
            }

            GL.CompileShader(fragmentShader);

            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out shaderStatus);

            if (shaderStatus != 1)
            {
                GL.GetShaderInfoLog(fragmentShader, out var fsInfo);

                throw new Exception($"Error setting up Fragment Shader \"{shaderName}\": {fsInfo}");
            }

            const string renderMode = "renderMode_";
            var renderModes = defines
                .Where(k => k.StartsWith(renderMode))
                .Select(k => k.Substring(renderMode.Length))
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
            GL.ValidateProgram(shader.Program);

            GL.GetProgram(shader.Program, GetProgramParameterName.LinkStatus, out var linkStatus);

            if (linkStatus != 1)
            {
                GL.GetProgramInfoLog(shader.Program, out var linkInfo);
                throw new Exception($"Error linking shaders: {linkInfo} (link status = {linkStatus}");
            }

            GL.DetachShader(shader.Program, vertexShader);
            GL.DeleteShader(vertexShader);

            GL.DetachShader(shader.Program, fragmentShader);
            GL.DeleteShader(fragmentShader);

#if !DEBUG_SHADERS || !DEBUG
            ShaderDefines[shaderFileName] = defines;
            var newShaderCacheHash = CalculateShaderCacheHash(shaderFileName, arguments);

            CachedShaders[newShaderCacheHash] = shader;

            Console.WriteLine($"Shader {newShaderCacheHash} ({shaderName}) ({string.Join(", ", arguments.Keys)}) compiled and linked succesfully");
#endif

            return shader;
        }

        public Shader LoadPlaneShader(string shaderName, IDictionary<string, bool> arguments)
        {
            var shaderFileName = GetShaderFileByName(shaderName);

            /* Vertex shader */
            var vertexShader = GL.CreateShader(ShaderType.VertexShader);

            var assembly = Assembly.GetExecutingAssembly();

#if DEBUG_SHADERS && DEBUG
            using (var stream = File.Open(GetShaderDiskPath($"plane.vert"), FileMode.Open))
#else
            using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}plane.vert"))
#endif
            using (var reader = new StreamReader(stream))
            {
                GL.ShaderSource(vertexShader, reader.ReadToEnd());
            }

            GL.CompileShader(vertexShader);

            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out var shaderStatus);

            if (shaderStatus != 1)
            {
                GL.GetShaderInfoLog(vertexShader, out var vsInfo);

                throw new Exception($"Error setting up Vertex Shader \"{shaderName}\": {vsInfo}");
            }

            /* Fragment shader */
            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);

#if DEBUG_SHADERS && DEBUG
            using (var stream = File.Open(GetShaderDiskPath($"{shaderFileName}.frag"), FileMode.Open))
#else
            using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{shaderFileName}.frag"))
#endif
            using (var reader = new StreamReader(stream))
            {
                var shaderSource = reader.ReadToEnd();
                GL.ShaderSource(fragmentShader, UpdateDefines(shaderSource, arguments));
            }

            GL.CompileShader(fragmentShader);

            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out shaderStatus);

            if (shaderStatus != 1)
            {
                GL.GetShaderInfoLog(fragmentShader, out var fsInfo);

                throw new Exception($"Error setting up Fragment Shader \"{shaderName}\": {fsInfo}");
            }

            var shader = new Shader
            {
                Name = shaderName,
                Program = GL.CreateProgram(),
            };
            GL.AttachShader(shader.Program, vertexShader);
            GL.AttachShader(shader.Program, fragmentShader);

            GL.LinkProgram(shader.Program);
            GL.ValidateProgram(shader.Program);

            GL.GetProgram(shader.Program, GetProgramParameterName.LinkStatus, out var linkStatus);

            if (linkStatus != 1)
            {
                GL.GetProgramInfoLog(shader.Program, out var linkInfo);
                throw new Exception($"Error linking shaders: {linkInfo} (link status = {linkStatus}");
            }

            GL.DetachShader(shader.Program, vertexShader);
            GL.DeleteShader(vertexShader);

            GL.DetachShader(shader.Program, fragmentShader);
            GL.DeleteShader(fragmentShader);

            return shader;
        }

        //Preprocess a vertex shader's source to include the #version plus #defines for parameters
        private static string PreprocessVertexShader(string source, IDictionary<string, bool> arguments)
        {
            //Update parameter defines
            var paramSource = UpdateDefines(source, arguments);

            //Inject code into shader based on #includes
            var includedSource = ResolveIncludes(paramSource);

            return includedSource;
        }

        //Update default defines with possible overrides from the model
        private static string UpdateDefines(string source, IDictionary<string, bool> arguments)
        {
            //Find all #define param_(paramName) (paramValue) using regex
            var defines = Regex.Matches(source, @"#define param_(\S*?) (\S*?)\s*?\n");
            foreach (Match define in defines)
            {
                //Check if this parameter is in the arguments
                if (arguments.TryGetValue(define.Groups[1].Value, out var value))
                {
                    //Overwrite default value
                    var index = define.Groups[2].Index;
                    var length = define.Groups[2].Length;
                    source = source.Remove(index, Math.Min(length, source.Length - index)).Insert(index, value ? "1" : "0");
                }
            }

            return source;
        }

        //Remove any #includes from the shader and replace with the included code
        private static string ResolveIncludes(string source)
        {
            var assembly = Assembly.GetExecutingAssembly();

            var includes = Regex.Matches(source, @"#include ""([^""]*?)"";?\s*\n");

            foreach (Match define in includes)
            {
                //Read included code
#if DEBUG_SHADERS  && DEBUG
                using (var stream = File.Open(GetShaderDiskPath(define.Groups[1].Value), FileMode.Open))
#else
                using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{define.Groups[1].Value}"))
#endif
                using (var reader = new StreamReader(stream))
                {
                    var includedCode = reader.ReadToEnd();

                    //Recursively resolve includes in the included code. (Watch out for cyclic dependencies!)
                    includedCode = ResolveIncludes(includedCode);
                    if (!includedCode.EndsWith("\n"))
                    {
                        includedCode += "\n";
                    }

                    //Replace the include with the code
                    source = source.Replace(define.Value, includedCode);
                }
            }

            return source;
        }

        private static List<string> FindDefines(string source)
        {
            var defines = Regex.Matches(source, @"#define param_(\S+)");
            return defines.Cast<Match>().Select(_ => _.Groups[1].Value).ToList();
        }

        // Map shader names to shader files
        private static string GetShaderFileByName(string shaderName)
        {
            switch (shaderName)
            {
                case "vrf.error":
                    return "error";
                case "vrf.grid":
                    return "debug_grid";
                case "vrf.particle.sprite":
                    return "particle_sprite";
                case "vrf.particle.trail":
                    return "particle_trail";
                case "vr_unlit.vfx":
                case "vr_black_unlit.vfx":
                    return "vr_unlit";
                case "water_dota.vfx":
                    return "water";
                case "hero.vfx":
                case "hero_underlords.vfx":
                    return "dota_hero";
                case "multiblend.vfx":
                    return "multiblend";
                default:
                    if (shaderName.StartsWith("vr_"))
                    {
                        return "vr_standard";
                    }

                    //Console.WriteLine($"Unknown shader {shaderName}, defaulting to simple.");
                    //Shader names that are supposed to use this:
                    //vr_simple.vfx
                    return "simple";
            }
        }

#if !DEBUG_SHADERS || !DEBUG
        private uint CalculateShaderCacheHash(string shaderFileName, IDictionary<string, bool> arguments)
        {
            var shaderCacheHashString = new StringBuilder();
            shaderCacheHashString.AppendLine(shaderFileName);

            var parameters = ShaderDefines[shaderFileName].Intersect(arguments.Keys);

            foreach (var key in parameters)
            {
                shaderCacheHashString.AppendLine(key);
                shaderCacheHashString.AppendLine(arguments[key] ? "t" : "f");
            }

            var test = shaderCacheHashString.ToString();

            return MurmurHash2.Hash(shaderCacheHashString.ToString(), ShaderSeed);
        }
#endif

#if DEBUG_SHADERS && DEBUG
        // Reload shaders at runtime
        private static string GetShaderDiskPath(string name)
        {
            return Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName), "../../../", ShaderDirectory.Replace('.', '/'), name);
        }
#endif
    }
}
