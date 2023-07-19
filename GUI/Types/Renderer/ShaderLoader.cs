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

        [GeneratedRegex(@"(?<SourceFile>[0-9]+)\((?<Line>[0-9]+)\) : error C(?<ErrorNumber>[0-9]+):")]
        private static partial Regex NvidiaGlslError();

        [GeneratedRegex(@"ERROR: (?<SourceFile>[0-9]+):(?<Line>[0-9]+):")]
        private static partial Regex AmdGlslError();

        private readonly Dictionary<uint, Shader> CachedShaders = new();
        public int ShaderCount => CachedShaders.Count;
        private readonly Dictionary<string, HashSet<string>> ShaderDefines = new();

        private static IReadOnlyDictionary<string, byte> EmptyArgs { get; } = new Dictionary<string, byte>(0);

        private int sourceFileNumber;
        private List<string> sourceFiles = new();

#if DEBUG
        private List<List<string>> sourceFileLines = new();
#endif

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

            int shaderProgram = -1;

            try
            {
                var defines = new HashSet<string>();

                // Vertex shader
                var vertexName = $"{shaderFileName}.vert";
                var vertexShader = GL.CreateShader(ShaderType.VertexShader);
                LoadShader(vertexShader, vertexName, shaderName, arguments, defines);

                // Fragment shader
                var fragmentName = $"{shaderFileName}.frag";
                var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
                LoadShader(fragmentShader, fragmentName, shaderName, arguments, defines);

                var renderModes = defines
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

                ShaderDefines[shaderName] = defines;
                var newShaderCacheHash = CalculateShaderCacheHash(shaderName, arguments);

                CachedShaders[newShaderCacheHash] = shader;

                Console.WriteLine($"Shader {newShaderCacheHash} ('{shaderName}' as '{shaderFileName}') ({string.Join(", ", arguments.Keys)}) compiled and linked succesfully");
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
                sourceFileNumber = 0;
                sourceFiles.Clear();

#if DEBUG
                sourceFileLines.Clear();
#endif
            }
        }

        private void LoadShader(int shader, string shaderFile, string originalShaderName, IReadOnlyDictionary<string, byte> arguments, HashSet<string> defines)
        {
            var isFirstLine = true;
            var builder = new StringBuilder();

            void AppendLineNumber(int a, int b)
            {
                builder.Append("#line ");
                builder.Append(a.ToString(CultureInfo.InvariantCulture));
                builder.Append(' ');
                builder.Append(b.ToString(CultureInfo.InvariantCulture));
                builder.Append('\n');
            }

            void LoadShaderString(string shaderFileToLoad)
            {
                using var stream = GetShaderStream(shaderFileToLoad);
                using var reader = new StreamReader(stream);
                string line;
                var lineNum = 1;
                var currentSourceFileNumber = sourceFileNumber++;
                sourceFiles.Add(shaderFileToLoad);

#if DEBUG
                var currentSourceLines = new List<string>();
                sourceFileLines.Add(currentSourceLines);
#endif

                while ((line = reader.ReadLine()) != null)
                {
                    lineNum++;

#if DEBUG
                    currentSourceLines.Add(line);
#endif

                    // TODO: Support leading whitespace?
                    if (line.Length > 7 && line[0] == '#')
                    {
                        // Includes
                        var match = RegexInclude().Match(line);

                        if (match.Success)
                        {
                            // Recursively append included shaders
                            // TODO: Add #line?

                            var includeName = match.Groups["IncludeName"].Value;

                            AppendLineNumber(1, sourceFileNumber);
                            LoadShaderString(includeName);
                            AppendLineNumber(lineNum, currentSourceFileNumber);

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

                    if (line.Contains("#endif", StringComparison.Ordinal))
                    {
                        // Fix an issue where #include is inside of an #if, which messes up line numbers
                        AppendLineNumber(lineNum, currentSourceFileNumber);
                    }

                    // Append original shader name as a define
                    if (isFirstLine)
                    {
                        isFirstLine = false;
                        builder.Append("#define ");
                        builder.Append(Path.GetFileNameWithoutExtension(originalShaderName));
                        builder.Append(" 1 // :VrfPreprocessed\n");
                        AppendLineNumber(lineNum, currentSourceFileNumber);
                    }
                }
            }

            LoadShaderString(shaderFile);

            var preprocessedShaderSource = builder.ToString();

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

                info += $"\nError in {sourceFiles[errorSourceFile]} on line {errorLine}";

#if DEBUG
                info += $":\n{sourceFileLines[errorSourceFile][errorLine - 1]}\n";
#endif
            }

            throw new InvalidProgramException($"{errorType} {shaderFile} (original={originalShaderName}):\n{info}");
        }

        private static Stream GetShaderStream(string name)
        {
#if DEBUG
            return File.Open(GetShaderDiskPath(name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
        public FileSystemWatcher ShaderWatcher { get; } = new()
        {
            Path = GetShaderDiskPath(""),
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
