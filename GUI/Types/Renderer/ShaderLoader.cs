using System;
using System.IO;
using System.Reflection;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class ShaderLoader
    {
        public static int shaderProgram;

        public static void loadShaders()
        {
            if (shaderProgram != 0) return;

            /* Vertex shader */
            var vertexShader = GL.CreateShader(ShaderType.VertexShader);

            var assembly = Assembly.GetExecutingAssembly();

            using (var stream = assembly.GetManifestResourceStream("GUI.Types.Renderer.Shaders.vertex.vert"))
            using (var reader = new StreamReader(stream))
            {
                GL.ShaderSource(vertexShader, reader.ReadToEnd());
            }

            GL.CompileShader(vertexShader);

            int vsStatus;
            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out vsStatus);

            if (vsStatus != 1)
            {
                string vsInfo;
                GL.GetShaderInfoLog(vertexShader, out vsInfo);
                throw new Exception("Error setting up Vertex Shader: " + vsInfo);
            }
            else
            {
                Console.WriteLine("Vertex shader compiled succesfully.");
            }

            /* Fragment shader */
            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);

            using (var stream = assembly.GetManifestResourceStream("GUI.Types.Renderer.Shaders.fragment.frag"))
            using (var reader = new StreamReader(stream))
            {
                GL.ShaderSource(fragmentShader, reader.ReadToEnd());
            }

            GL.CompileShader(fragmentShader);

            int fsStatus;
            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out fsStatus);

            if (fsStatus != 1)
            {
                string fsInfo;
                GL.GetShaderInfoLog(fragmentShader, out fsInfo);
                throw new Exception("Error setting up Fragment Shader: " + fsInfo);
            }
            else
            {
                Console.WriteLine("Fragment shader compiled succesfully.");
            }

            shaderProgram = GL.CreateProgram();
            GL.AttachShader(shaderProgram, vertexShader);
            GL.AttachShader(shaderProgram, fragmentShader);

            GL.LinkProgram(shaderProgram);

            string programInfoLog = GL.GetProgramInfoLog(shaderProgram);
            Console.Write(programInfoLog);

            int linkStatus;
            GL.GetProgram(shaderProgram, GetProgramParameterName.LinkStatus, out linkStatus);

            if (linkStatus != 1)
            {
                string linkInfo;
                GL.GetProgramInfoLog(shaderProgram, out linkInfo);
                throw new Exception("Error linking shaders: " + linkInfo);
            }
            else
            {
                Console.WriteLine("Shaders linked succesfully.");
            }

            GL.DetachShader(shaderProgram, vertexShader);
            GL.DeleteShader(vertexShader);

            GL.DetachShader(shaderProgram, fragmentShader);
            GL.DeleteShader(fragmentShader);
        }
    }
}
