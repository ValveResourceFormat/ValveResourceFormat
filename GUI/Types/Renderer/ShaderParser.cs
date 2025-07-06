using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GUI.Utils;
using static GUI.Types.Renderer.ShaderLoader;

namespace GUI.Types.Renderer
{
    partial class ShaderParser
    {
        private const string ShaderDirectory = "GUI.Types.Renderer.Shaders.";
        private const string ExpectedShaderVersion = "#version 460";
        private const string RenderModeDefinePrefix = "renderMode_";

        [GeneratedRegex("^#include \"(?<IncludeName>[^\"]+)\"")]
        private static partial Regex RegexInclude();
        [GeneratedRegex("^#define (?<ParamName>(?:renderMode|F|S|D)_\\S+) (?<DefaultValue>\\S+)")]
        private static partial Regex RegexDefine();

#if DEBUG
        [GeneratedRegex(@"defined\((?<Name>\w+)_vfx\)")]
        private static partial Regex RegexIsVfxDefined();
#endif

        // regex that detects "uniform samplerx sampler; // SrgbRead(true)"
        // accept whitespace in front
        [GeneratedRegex("^uniform sampler(?<SamplerType>\\S+) (?<SamplerName>\\S+);\\s*// SrgbRead\\(true\\)")]
        private static partial Regex RegexSamplerWithSrgbRead();

        private readonly StringBuilder builder = new(1024);
        private int sourceFileNumber;
        public List<string> SourceFiles { get; } = [];

#if DEBUG
        public List<List<string>> SourceFileLines { get; } = [];
#endif

        public void Reset()
        {
            builder.Clear();
            sourceFileNumber = 0;
            SourceFiles.Clear();

#if DEBUG
            SourceFileLines.Clear();
#endif
        }

        public string PreprocessShader(string shaderFile, string originalShaderName, IReadOnlyDictionary<string, byte> arguments, ParsedShaderData parsedData)
        {
            var isFirstLine = true;
            var resolvedIncludes = new HashSet<string>(4);

            void AppendLineNumber(int a, int b)
            {
                builder.Append("#line ");
                builder.Append(a.ToString(CultureInfo.InvariantCulture));
                builder.Append(' ');
                builder.Append(b.ToString(CultureInfo.InvariantCulture));
                builder.Append('\n');
            }

            void LoadShaderString(string shaderFileToLoad, string? parentFile, bool isInclude)
            {
                if (parentFile != null)
                {
                    var folder = Path.GetDirectoryName(parentFile);

                    if (!string.IsNullOrEmpty(folder))
                    {
                        shaderFileToLoad = $"{folder}/{shaderFileToLoad}";
                    }

                    if (!resolvedIncludes.Add(shaderFileToLoad))
                    {
                        //Console.WriteLine($"{shaderFileToLoad} already loaded");
                        return;
                    }
                }

                using var stream = GetShaderStream(shaderFileToLoad);
                using var reader = new StreamReader(stream);
                string? line;
                var lineNum = 1;
                var currentSourceFileNumber = sourceFileNumber++;
                SourceFiles.Add(shaderFileToLoad);

#if DEBUG
                var currentSourceLines = new List<string>();
                SourceFileLines.Add(currentSourceLines);
#endif

                builder.EnsureCapacity(builder.Length + (int)stream.Length);

                while ((line = reader.ReadLine()) != null)
                {
                    lineNum++;

                    if (lineNum == 2)
                    {
                        if (line != ExpectedShaderVersion)
                        {
                            throw new ShaderCompilerException($"First line must be '{ExpectedShaderVersion}' in '{shaderFileToLoad}'");
                        }

                        if (isInclude)
                        {
#if DEBUG
                            currentSourceLines.Add("// :VrfPreprocessed {line}");
#endif

                            // We add #version even in includes so that they can be compiled individually for better editing experience
                            continue;
                        }
                    }

#if DEBUG
                    currentSourceLines.Add(line);
#endif

                    {
                        line = line.Trim(); // we will be outputting trimmed lines to compile too

                        // Includes
                        var match = RegexInclude().Match(line);

                        if (match.Success)
                        {
                            // Recursively append included shaders

                            var includeName = match.Groups["IncludeName"].Value;

                            AppendLineNumber(1, sourceFileNumber);
                            LoadShaderString(includeName, shaderFileToLoad, isInclude: true);
                            AppendLineNumber(lineNum, currentSourceFileNumber);

                            continue;
                        }

                        // Defines
                        match = RegexDefine().Match(line);

                        if (match.Success)
                        {
                            var defineName = match.Groups["ParamName"].Value;
                            byte value = 0;

                            if (defineName.StartsWith(RenderModeDefinePrefix, StringComparison.Ordinal))
                            {
                                var renderMode = defineName[RenderModeDefinePrefix.Length..];

                                parsedData.RenderModes.Add(renderMode);

                                value = RenderModes.GetShaderId(renderMode);

                                if (value == 0)
                                {
                                    var renderModeObj = new RenderModes.RenderMode(renderMode);
                                    var index = RenderModes.Items.IndexOf(renderModeObj);

                                    if (index == -1)
                                    {
                                        Debug.Assert(false); /// Add to <see cref="RenderModes.Items"/> if this assert is hit

                                        RenderModes.Items = RenderModes.Items.Add(renderModeObj);
                                        index = RenderModes.Items.IndexOf(renderModeObj);
                                    }

                                    value = (byte)index;
                                    RenderModes.AddShaderId(renderMode, value);
                                }
                            }
                            else
                            {
                                parsedData.Defines.Add(defineName);

                                // Check if this parameter is in the arguments
                                if (!arguments.TryGetValue(defineName, out value))
                                {
                                    builder.Append(line);
                                    builder.Append('\n');
                                    continue;
                                }
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

                        // sRGB samplers
                        match = RegexSamplerWithSrgbRead().Match(line);
                        if (match.Success)
                        {
                            var samplerName = match.Groups["SamplerName"].Value;
                            var samplerType = match.Groups["SamplerType"].Value;

                            parsedData.SrgbSamplers.Add(samplerName);
                        }

#if DEBUG
                        // defined(shader_vfx)
                        match = RegexIsVfxDefined().Match(line);
                        if (match.Success)
                        {
                            var shaderName = match.Groups["Name"].Value;
                            parsedData.ShaderVariants.Add(shaderName);
                        }
#endif
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
                        builder.Append("_vfx 1 // :VrfPreprocessed\n");
                        AppendLineNumber(lineNum, currentSourceFileNumber);
                    }
                }
            }

            LoadShaderString(shaderFile, null, isInclude: false);

            return builder.ToString();
        }

#if !DEBUG
        private static Stream GetShaderStream(string name)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{name.Replace('/', '.')}");
            ArgumentNullException.ThrowIfNull(stream);
            return stream;
        }
#else
        private static readonly string ShadersFolderPathOnDisk = GetShadersFolder();

        private static FileStream GetShaderStream(string name)
        {
            var path = GetShaderDiskPath(name);

            try
            {
                return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            catch (IOException e)
            {
                Log.Error(nameof(ShaderParser), e.Message);

                // Sometimes hot reloading shaders throws "The process cannot access the file x because it is being used by another process."
                // Just sleep for a second and try again
                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(1));

                return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
        }

        public static string GetShaderDiskPath(string name)
        {
            return Path.Combine(ShadersFolderPathOnDisk, ShaderDirectory.Replace('.', '/'), name);
        }

        private static string GetShadersFolder()
        {
            var root = AppContext.BaseDirectory;
            var failsafe = 10;
            var fileName = string.Empty;

            do
            {
                root = Path.GetDirectoryName(root);
                ArgumentNullException.ThrowIfNull(root);
                fileName = Path.Join(root, "ValveResourceFormat.sln");

                if (failsafe-- == 0)
                {
                    throw new DirectoryNotFoundException("Failed to find GUI folder for the shaders, are you debugging in some unconventional setup?");
                }
            }
            while (!File.Exists(fileName));

            return root;
        }
#endif
    }
}
