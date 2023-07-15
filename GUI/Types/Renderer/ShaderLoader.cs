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
    partial class ShaderLoader : IDisposable
    {
        private const string ShaderDirectory = "GUI.Types.Renderer.Shaders.";
        private const int ShaderSeed = 0x13141516;
        private const string RenderModeDefinePrefix = "renderMode_";

        [GeneratedRegex("^#include \"(?<IncludeName>[^\"]+)\"")]
        private static partial Regex RegexInclude();
        [GeneratedRegex("^#define (?<ParamName>\\S+) (?<DefaultValue>\\S+)")]
        private static partial Regex RegexDefine();

        [GeneratedRegex("[0-9]+\\((?<Line>\\d+)\\) : error C(?<ErrorNumber>\\d+):")]
        private static partial Regex NvidiaGlslError();

        [GeneratedRegex("ERROR: [0-9]+:(?<Line>\\d+):")]
        private static partial Regex AmdGlslError();

        private readonly Dictionary<uint, Shader> CachedShaders = new();
        private readonly Dictionary<string, HashSet<string>> ShaderDefines = new();

        private static IReadOnlyDictionary<string, byte> EmptyArgs { get; } = new Dictionary<string, byte>(0);

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

            var defines = new HashSet<string>();

            // Vertex shader
            var vertexShader = GL.CreateShader(ShaderType.VertexShader);
            LoadShader(vertexShader, $"{shaderFileName}.vert", shaderName, arguments, defines);

            // Fragment shader
            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            LoadShader(fragmentShader, $"{shaderFileName}.frag", shaderName, arguments, defines);

            var renderModes = defines
                .Where(k => k.StartsWith(RenderModeDefinePrefix, StringComparison.Ordinal))
                .Select(k => k[RenderModeDefinePrefix.Length..])
                .ToHashSet();

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

            ShaderDefines[shaderName] = defines;
            var newShaderCacheHash = CalculateShaderCacheHash(shaderName, arguments);

            CachedShaders[newShaderCacheHash] = shader;

            Console.WriteLine($"Shader {newShaderCacheHash} ('{shaderName}' as '{shaderFileName}') ({string.Join(", ", arguments.Keys)}) compiled and linked succesfully");
            return shader;
        }

        private static void LoadShader(int shader, string shaderFile, string originalShaderName, IReadOnlyDictionary<string, byte> arguments, HashSet<string> defines)
        {
            var isFirstLine = true;
            var builder = new StringBuilder();

            void LoadShaderString(string shaderFileToLoad)
            {
                using var stream = GetShaderStream(shaderFileToLoad);
                using var reader = new StreamReader(stream);
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    // TODO: Support leading whitespace?
                    if (line.Length > 7 && line[0] == '#')
                    {
                        // Includes
                        var match = RegexInclude().Match(line);

                        if (match.Success)
                        {
                            // Recursively append included shaders
                            // TODO: Add #line?
                            LoadShaderString(match.Groups["IncludeName"].Value);
                            continue;
                        }

                        // Defines
                        match = RegexDefine().Match(line);

                        if (match.Success)
                        {
                            var defineName = match.Groups["ParamName"].Value;

                            defines.Add(defineName);

                            // Check if this parameter is in the arguments
                            if (!arguments.TryGetValue(defineName, out var value))
                            {
                                builder.Append(line);
                                builder.Append('\n');
                                continue;
                            }

                            // Overwrite default value
                            var newValue = value.ToString(CultureInfo.InvariantCulture);

                            builder.Append("#define ");
                            builder.Append(defineName);
                            builder.Append(' ');
                            builder.Append(newValue);
                            builder.Append(" // :VrfPreprocessed\n");

                            continue;
                        }
                    }

                    builder.Append(line);
                    builder.Append('\n');

                    // Append original shader name as a define
                    if (isFirstLine)
                    {
                        isFirstLine = false;
                        builder.Append("#define ");
                        builder.Append(Path.GetFileNameWithoutExtension(originalShaderName));
                        builder.Append(" 1 // :VrfPreprocessed\n");
                    }
                }
            }

            LoadShaderString(shaderFile);

            var preprocessedShaderSource = builder.ToString();

            GL.ShaderSource(shader, preprocessedShaderSource);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out var shaderStatus);

            if (shaderStatus == 1)
            {
                return;
            }

            GL.GetShaderInfoLog(shader, out var info);

            // Attempt to parse error message to get the line number so we can print the actual line
            var errorLine = 0;
            var nvidiaErr = NvidiaGlslError().Match(info);
            if (nvidiaErr.Success)
            {
                errorLine = int.Parse(nvidiaErr.Groups["Line"].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                var amdErr = AmdGlslError().Match(info);
                if (amdErr.Success)
                {
                    errorLine = int.Parse(amdErr.Groups["Line"].Value, CultureInfo.InvariantCulture);
                }
            }

            if (errorLine > 0)
            {
                var lines = preprocessedShaderSource.Split('\n');
                if (errorLine <= lines.Length)
                {
                    var error = lines[errorLine - 1];
                    info += $"\n{error}";
                }
            }

            throw new InvalidProgramException($"Error setting up shader {shaderFile} (original={originalShaderName}):\n{info}");
        }

        private static Stream GetShaderStream(string name)
        {
#if DEBUG
            return File.OpenRead(GetShaderDiskPath(name));
#else
            var assembly = Assembly.GetExecutingAssembly();
            return assembly.GetManifestResourceStream($"{ShaderDirectory}{name.Replace('/', '.')}");
#endif
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
                case "global_lit_simple.vfx":
                    return "global_lit_simple";
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

        private uint CalculateShaderCacheHash(string shaderName, IReadOnlyDictionary<string, byte> arguments)
        {
            var shaderCacheHashString = new StringBuilder();
            shaderCacheHashString.AppendLine(shaderName);

            var parameters = ShaderDefines[shaderName].Intersect(arguments.Keys);

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
