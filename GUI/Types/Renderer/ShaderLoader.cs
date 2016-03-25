using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using System.Collections.Generic;

namespace GUI.Types.Renderer
{
    internal class ShaderLoader
    {
        private const string ShaderDirectory = "GUI.Types.Renderer.Shaders.";

        //Map shader names to shader files
        public static string GetShaderFileByName(string shaderName)
        {
            switch (shaderName)
            {
                case "vr_standard.vfx":
                    return "vr_standard";
                case "hero.vfx":
                    return "dota_hero";
                default:
                    Console.WriteLine($"Unknown shader {shaderName}, defaulting to simple.");
                    //Shader names that are supposed to use this:
                    //vr_simple.vfx
                    return "simple";
            }
        }

        public static int LoadShaders(string shaderName, ArgumentDependencies modelArguments)
        {
            /* Vertex shader */
            var vertexShader = GL.CreateShader(ShaderType.VertexShader);

            var assembly = Assembly.GetExecutingAssembly();

#if DEBUG
            using (var stream = File.Open($"../../../{ShaderDirectory.Replace('.', '/')}{GetShaderFileByName(shaderName)}.vert", FileMode.Open)) //<-- reloading at runtime
#else
            using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{GetShaderFileByName(shaderName)}.vert"))
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
                throw new Exception("Error setting up Vertex Shader: " + vsInfo);
            }
            Console.WriteLine("Vertex shader compiled succesfully.");

            /* Fragment shader */
            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);

#if DEBUG
            using (var stream = File.Open($"../../../{ShaderDirectory.Replace('.','/')}{GetShaderFileByName(shaderName)}.frag", FileMode.Open)) //<-- reloading at runtime
#else
            using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{GetShaderFileByName(shaderName)}.frag"))
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
                throw new Exception("Error setting up Fragment Shader: " + fsInfo);
            }
            Console.WriteLine("Fragment shader compiled succesfully.");

            int shaderProgram = GL.CreateProgram();
            GL.AttachShader(shaderProgram, vertexShader);
            GL.AttachShader(shaderProgram, fragmentShader);

            GL.LinkProgram(shaderProgram);

            var programInfoLog = GL.GetProgramInfoLog(shaderProgram);
            Console.Write(programInfoLog);

            int linkStatus;
            GL.GetProgram(shaderProgram, GetProgramParameterName.LinkStatus, out linkStatus);

            if (linkStatus != 1)
            {
                string linkInfo;
                GL.GetProgramInfoLog(shaderProgram, out linkInfo);
                throw new Exception("Error linking shaders: " + linkInfo);
            }
            Console.WriteLine("Shaders linked succesfully.");

            GL.DetachShader(shaderProgram, vertexShader);
            GL.DeleteShader(vertexShader);

            GL.DetachShader(shaderProgram, fragmentShader);
            GL.DeleteShader(fragmentShader);

            GL.ValidateProgram(shaderProgram);

            return shaderProgram;
        }

        //Preprocess a vertex shader's source to include the #version plus #defines for parameters
        private static string PreprocessVertexShader(string source, ArgumentDependencies arguments)
        {
            //Update parameter defines
            string paramSource = UpdateDefines(source, arguments);

            //Inject code into shader based on #includes
            string includedSource = ResolveIncludes(paramSource);

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
                    int index = define.Groups[2].Index;
                    int length = define.Groups[2].Length;
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
#if DEBUG
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
                    int index = define.Index;
                    int length = define.Length;
                    source = source.Remove(index, Math.Min(length, source.Length - index)).Insert(index, includedCode);
                }
            }

            return source;
        }
    }
}
