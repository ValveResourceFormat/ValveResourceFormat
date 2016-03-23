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
        public static int ShaderProgram;

        //REDI parameters that should be passed to the shader and their default value
        private static Dictionary<string, object> shaderParameters = new Dictionary<string, object>()
        {
            {"fulltangent", 1 }
        };

        public static void LoadShaders(ArgumentDependencies modelArguments)
        {
            if (ShaderProgram != 0)
            {
                //return;
            }

            /* Vertex shader */
            var vertexShader = GL.CreateShader(ShaderType.VertexShader);

            var assembly = Assembly.GetExecutingAssembly();

            using (var stream = assembly.GetManifestResourceStream("GUI.Types.Renderer.Shaders.vertex.vert"))
            using (var reader = new StreamReader(stream))
            {
                var shaderSource = reader.ReadToEnd();
                GL.ShaderSource(vertexShader, PreprocessVertexShader(shaderSource,modelArguments));
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

            using (var stream = assembly.GetManifestResourceStream("GUI.Types.Renderer.Shaders.fragment.frag"))
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

            ShaderProgram = GL.CreateProgram();
            GL.AttachShader(ShaderProgram, vertexShader);
            GL.AttachShader(ShaderProgram, fragmentShader);

            GL.LinkProgram(ShaderProgram);

            var programInfoLog = GL.GetProgramInfoLog(ShaderProgram);
            Console.Write(programInfoLog);

            int linkStatus;
            GL.GetProgram(ShaderProgram, GetProgramParameterName.LinkStatus, out linkStatus);

            if (linkStatus != 1)
            {
                string linkInfo;
                GL.GetProgramInfoLog(ShaderProgram, out linkInfo);
                throw new Exception("Error linking shaders: " + linkInfo);
            }
            Console.WriteLine("Shaders linked succesfully.");

            GL.DetachShader(ShaderProgram, vertexShader);
            GL.DeleteShader(vertexShader);

            GL.DetachShader(ShaderProgram, fragmentShader);
            GL.DeleteShader(fragmentShader);
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
