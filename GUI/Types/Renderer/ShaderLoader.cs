#define DEBUG_SHADERS
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;

namespace GUI.Types.Renderer
{
    internal class ShaderLoader
    {
        private const string ShaderDirectory = "GUI.Types.Renderer.Shaders.";

#if !DEBUG_SHADERS || !DEBUG
        private static readonly Dictionary<int, Shader> CachedShaders = new Dictionary<int, Shader>();
#endif

        //Map shader names to shader files
        public static string GetShaderFileByName(string shaderName)
        {
            switch (shaderName)
            {
                case "vr_standard.vfx":
                    return "vr_standard";
                case "vr_unlit.vfx":
                    return "vr_unlit";
                case "water_dota.vfx":
                    return "water";
                case "hero.vfx":
                    return "dota_hero";
                case "multiblend.vfx":
                    return "multiblend";
                default:
                    //Console.WriteLine($"Unknown shader {shaderName}, defaulting to simple.");
                    //Shader names that are supposed to use this:
                    //vr_simple.vfx
                    return "simple";
            }
        }

        public static Shader LoadShader(string shaderName, ArgumentDependencies modelArguments)
        {
            var shaderFileName = GetShaderFileByName(shaderName);
            var shaderCacheHash = (shaderFileName + modelArguments).GetHashCode(); // shader collision roulette

            Shader shader;

#if !DEBUG_SHADERS || !DEBUG
            if (CachedShaders.TryGetValue(shaderCacheHash, out shader))
            {
                return shader;
            }
#endif

            /* Vertex shader */
            var vertexShader = GL.CreateShader(ShaderType.VertexShader);

            var assembly = Assembly.GetExecutingAssembly();

#if DEBUG_SHADERS && DEBUG
            using (var stream = File.Open($"../../../{ShaderDirectory.Replace('.', '/')}{shaderFileName}.vert", FileMode.Open)) //<-- reloading at runtime
#else
            using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{shaderFileName}.vert"))
#endif
            using (var reader = new StreamReader(stream))
            {
                var shaderSource = reader.ReadToEnd();
                GL.ShaderSource(vertexShader, PreprocessVertexShader(shaderSource, modelArguments));
            }

            GL.CompileShader(vertexShader);

            int shaderStatus;
            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out shaderStatus);

            if (shaderStatus != 1)
            {
                string vsInfo;
                GL.GetShaderInfoLog(vertexShader, out vsInfo);

                throw new Exception($"Error setting up Vertex Shader \"{shaderName}\": {vsInfo}");
            }

            /* Fragment shader */
            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);

#if DEBUG_SHADERS && DEBUG
            using (var stream = File.Open($"../../../{ShaderDirectory.Replace('.', '/')}{shaderFileName}.frag", FileMode.Open)) //<-- reloading at runtime
#else
            using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{shaderFileName}.frag"))
#endif
            using (var reader = new StreamReader(stream))
            {
                GL.ShaderSource(fragmentShader, reader.ReadToEnd());
            }

            GL.CompileShader(fragmentShader);

            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out shaderStatus);

            if (shaderStatus != 1)
            {
                string fsInfo;
                GL.GetShaderInfoLog(fragmentShader, out fsInfo);

                throw new Exception($"Error setting up Fragment Shader \"{shaderName}\": {fsInfo}");
            }

            shader = new Shader();
            shader.Program = GL.CreateProgram();
            GL.AttachShader(shader.Program, vertexShader);
            GL.AttachShader(shader.Program, fragmentShader);

            GL.LinkProgram(shader.Program);

            var programInfoLog = GL.GetProgramInfoLog(shader.Program);
            Console.Write(programInfoLog);

            GL.ValidateProgram(shader.Program);

            int linkStatus;
            GL.GetProgram(shader.Program, GetProgramParameterName.LinkStatus, out linkStatus);

            if (linkStatus != 1)
            {
                string linkInfo;
                GL.GetProgramInfoLog(shader.Program, out linkInfo);
                throw new Exception("Error linking shaders: " + linkInfo);
            }

            GL.DetachShader(shader.Program, vertexShader);
            GL.DeleteShader(vertexShader);

            GL.DetachShader(shader.Program, fragmentShader);
            GL.DeleteShader(fragmentShader);

#if !DEBUG_SHADERS || !DEBUG
            CachedShaders[shaderCacheHash] = shader;

            Console.WriteLine("Shader #{0} ({1}) compiled and linked succesfully", CachedShaders.Count, shaderName);
#endif

            return shader;
        }

        //Preprocess a vertex shader's source to include the #version plus #defines for parameters
        private static string PreprocessVertexShader(string source, ArgumentDependencies arguments)
        {
            //Update parameter defines
            var paramSource = UpdateDefines(source, arguments);

            //Inject code into shader based on #includes
            var includedSource = ResolveIncludes(paramSource);

            return includedSource;
        }

        //Update default defines with possible overrides from the model
        private static string UpdateDefines(string source, ArgumentDependencies arguments)
        {
            //Add all parameters to a dictionary
            var argumentDict = new Dictionary<string, uint>();
            foreach (var argument in arguments.List)
            {
                argumentDict.Add(argument.ParameterName, argument.Fingerprint);
            }

            //Find all #define param_(paramName) (paramValue) using regex
            var defines = Regex.Matches(source, @"#define param_(\S*?) (\S*?)\s*?\n");
            foreach (Match define in defines)
            {
                //Check if this parameter is in the arguments
                if (argumentDict.ContainsKey(define.Groups[1].Value))
                {
                    //Overwrite default value
                    var index = define.Groups[2].Index;
                    var length = define.Groups[2].Length;
                    source = source.Remove(index, Math.Min(length, source.Length - index)).Insert(index, argumentDict[define.Groups[1].Value].ToString());
                }
            }

            return source;
        }

        //Remove any #includes from the shader and replace with the included code
        private static string ResolveIncludes(string source)
        {
            var assembly = Assembly.GetExecutingAssembly();

            var includes = Regex.Matches(source, @"#include ""(\S*?)""\s*?\n");

            foreach (Match define in includes)
            {
                //Read included code
#if DEBUG_SHADERS  && DEBUG
                using (var stream = File.Open($"../../../{ShaderDirectory.Replace('.', '/')}{define.Groups[1].Value}", FileMode.Open)) //<-- reloading at runtime
#else
                using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{define.Groups[1].Value}"))
#endif
                using (var reader = new StreamReader(stream))
                {
                    var includedCode = reader.ReadToEnd();

                    //Recursively resolve includes in the included code. (Watch out for cyclic dependencies!)
                    includedCode = ResolveIncludes(includedCode);

                    //Replace the include with the code
                    var index = define.Index;
                    var length = define.Length;
                    source = source.Remove(index, Math.Min(length, source.Length - index)).Insert(index, includedCode);
                }
            }

            return source;
        }
    }
}
