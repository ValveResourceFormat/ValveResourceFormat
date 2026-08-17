#if DEBUG
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Shaders
{
    public partial class ShaderLoader
    {
        [GeneratedRegex(@"(?<DefineName>(?:F|S|D)_\S+)\s*(?<Operator>>=|<=|>|<|==|!=)\s*(?<Value>\d+)")]
        private static partial Regex ShaderDefineConditions();

        /// <summary>Compiles every known shader (and all their define combinations) to validate correctness (debug builds only).</summary>
        /// <param name="progressReporter">Receives status messages as each shader variant is compiled.</param>
        /// <param name="logger">Logger used when constructing the internal <see cref="RendererContext"/>.</param>
        /// <param name="filter">Optional substring to restrict which shader files are validated.</param>
        public static void ValidateShaders(IProgress<string> progressReporter, ILogger logger, string? filter = null)
        {
            using var renderContext = new RendererContext(new ValveResourceFormat.IO.GameFileLoader(null, null), logger);
            var loader = renderContext.ShaderLoader;
            var folder = ShaderParser.ShaderSourceDirectory
                ?? throw new DirectoryNotFoundException("Shader validation requires the shader source files, but this build only has the embedded copies.");

            var vertShaders = Directory.GetFiles(folder, "*.vert.slang");
            var compShaders = Directory.GetFiles(folder, "*.comp.slang");
            var allShaders = vertShaders.Concat(compShaders).ToArray();

            if (filter != null)
            {
                allShaders = [.. allShaders.Where(s => Path.GetFileName(s).Contains(filter, StringComparison.OrdinalIgnoreCase))];
            }

            if (allShaders.Length == 0)
            {
                throw new InvalidOperationException($"No shaders matched filter '{filter}'.");
            }

            GLEnvironment.Initialize(renderContext.Logger);
            GLEnvironment.EnableParallelShaderCompile();

            var validatedCount = 0;
            HashSet<string> reportedLinkLogs = [];

            foreach (var shader in allShaders)
            {
                CompileShaderVariants(ShaderNameFromPath(shader));
            }

            LinkAndDeleteBatch();

            progressReporter.Report($"Validated {validatedCount} shader variants");

            // Compile without waiting on link status so the driver can link the batch in parallel
            void Compile(string name, IReadOnlyDictionary<string, byte>? arguments = null)
            {
                loader.LoadShader(name, arguments, blocking: false);

                if (loader.ShaderCount >= 128)
                {
                    LinkAndDeleteBatch();
                }
            }

            // Collect the link statuses that were deferred above, failing like a blocking load would, then
            // delete the programs so they do not pile up across every shader
            void LinkAndDeleteBatch()
            {
                var batchCount = loader.ShaderCount;

                if (batchCount == 0)
                {
                    return;
                }

                progressReporter.Report($"Linking {batchCount} shader variants");

                foreach (var compiledShader in loader.CachedShaders.Values)
                {
                    if (compiledShader.IsLoaded)
                    {
                        continue;
                    }

                    var linked = compiledShader.EnsureLoaded();

                    GL.GetProgramInfoLog(compiledShader.Program, out var log);

                    if (!linked)
                    {
                        var failedParsed = GetOrParseShader(compiledShader.FileName);
                        var argsDescription = GetArgumentDescription(SortAndFilterArguments(failedParsed.Defines, compiledShader.Parameters));

                        ThrowShaderError(log, string.Concat(compiledShader.FileName, argsDescription), compiledShader.Name, "Failed to link shader", failedParsed);
                    }

                    if (!string.IsNullOrWhiteSpace(log) && reportedLinkLogs.Add(log))
                    {
                        var parsed = GetOrParseShader(compiledShader.FileName);
                        var argsDescription = GetArgumentDescription(SortAndFilterArguments(parsed.Defines, compiledShader.Parameters));

                        logger.LogWarning("Link log for {FileName}{Arguments}:\n{Log}", compiledShader.FileName, argsDescription, log.Trim());
                    }
                }

                validatedCount += batchCount;
                loader.DeleteCachedShaders();
            }

            void CompileShaderVariants(string shaderName)
            {
                progressReporter.Report($"Compiling {shaderName}");

                if (shaderName == "texture_decode")
                {
                    Compile(shaderName, new Dictionary<string, byte>
                    {
                        ["S_TYPE_TEXTURE2D"] = 1,
                    });
                    return;
                }

                Compile(shaderName);

                // Test all defines one by one
                var shaderFileName = GetShaderFileByName(shaderName);
                var parsed = GetOrParseShader(shaderFileName);
                var defines = parsed.Defines.Where(static x => !x.Key.StartsWith("GameVfx_", StringComparison.Ordinal)).ToDictionary();
                var variants = parsed.Defines.Keys.Where(static x => x.StartsWith("GameVfx_", StringComparison.Ordinal));
                var sourceLines = parsed.SourceFileLines;
                var maxValues = ExtractMaxDefineValues(defines, sourceLines);

                foreach (var define in defines.Keys)
                {
                    var maxValue = maxValues.GetValueOrDefault(define, 1);

                    for (var value = 1; value <= maxValue; value++)
                    {
                        progressReporter.Report($"Compiling {shaderName} with {define}={value}");

                        Compile(shaderName, new Dictionary<string, byte>
                        {
                            [define] = (byte)value,
                        });
                    }
                }

                // Test all variants
                foreach (var name in variants)
                {
                    var vfxName = string.Concat(name.AsSpan()["GameVfx_".Length..], ".vfx");
                    progressReporter.Report($"Compiling variant {vfxName}");

                    Compile(vfxName);

                    // Test all defines one by one in combination with the shader variant name
                    foreach (var define in defines.Keys)
                    {
                        var maxValue = maxValues.GetValueOrDefault(define, 1);

                        for (var value = 1; value <= maxValue; value++)
                        {
                            progressReporter.Report($"Compiling variant {vfxName} with {define}={value}");

                            Compile(vfxName, new Dictionary<string, byte>
                            {
                                [define] = (byte)value,
                            });
                        }
                    }

                    // Test all defines at once with their maximum values
                    progressReporter.Report($"Compiling variant {vfxName} with all defines");

                    Compile(vfxName, defines.Keys.ToDictionary(static d => d, d => (byte)maxValues.GetValueOrDefault(d, 1)));
                }
            }
        }

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
    }
}
#endif
