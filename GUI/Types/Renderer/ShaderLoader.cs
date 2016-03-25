using System;
using System.IO;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using System.Collections.Generic;

namespace GUI.Types.Renderer
{
    internal class ShaderLoader
    {
        private const string ShaderDirectory = "GUI.Types.Renderer.Shaders.";

        //REDI parameters that should be passed to the shader and their default value
        private static Dictionary<string, object> shaderParameters = new Dictionary<string, object>()
        {
            {"fulltangent", 1 }
        };

        //Map shader names to shader files
        public static string GetShaderFileByName(string shaderName)
        {
            switch (shaderName)
            {
                case "vr_standard.vfx":
                    return "vr_standard";
                default:
                    //Shader names that are supposed to use this:
                    //vr_simple.vfx
                    return "vr_standard"; //Default vr_standard for now because that works, replace with more basic one later
            }
        }

        public static int LoadShaders(string shaderName, ArgumentDependencies modelArguments)
        {
            /* Vertex shader */
            var vertexShader = GL.CreateShader(ShaderType.VertexShader);

            var assembly = Assembly.GetExecutingAssembly();

            //using (var stream = File.Open($"../../../{ShaderDirectory.Replace('.', '/')}{GetShaderFileByName(shaderName)}.vert", FileMode.Open)) //<-- reloading at runtime
            using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{GetShaderFileByName(shaderName)}.vert"))
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

            //using (var stream = File.Open($"../../../{ShaderDirectory.Replace('.','/')}{GetShaderFileByName(shaderName)}.frag", FileMode.Open)) //<-- reloading at runtime
            using (var stream = assembly.GetManifestResourceStream($"{ShaderDirectory}{GetShaderFileByName(shaderName)}.frag"))
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
            //Add all parameters to a dictionary
            var argumentDict = new Dictionary<string, double>();
            foreach (var argument in arguments.List)
            {
                argumentDict.Add(argument.ParameterName, argument.Fingerprint);
            }

            //Start the new header to be appended with the version
            var dependencyHeader = "#version 330\n";

            //For each parameter in shaderParameters add the default value, or an overridden value from the REDI
            foreach (var argument in shaderParameters)
            {
                if (argumentDict.ContainsKey(argument.Key))
                {
                    dependencyHeader += $"#define param_{argument.Key} {argumentDict[argument.Key]}\n";
                }
                else
                {
                    dependencyHeader += $"#define param_{argument.Key} {argument.Value}\n";
                }
            }

            //Return header + shader
            return dependencyHeader + source;
        }
    }
}
