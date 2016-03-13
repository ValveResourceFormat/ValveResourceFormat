using System;
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

            string vertexShaderSource = @"
#version 330
 
in vec3 vPosition;
in vec3 vNormal;
in vec2 vTexCoord;
in vec4 vTangent;
in ivec4 vBlendIndices;
in vec4 vBlendWeight;

out vec3 vNormalOut;
out vec2 vTexCoordOut;

uniform mat4 projection;
uniform mat4 modelview;

void main()
{
    gl_Position = projection * modelview * vec4(vPosition, 1.0);
    vTexCoordOut = vTexCoord;
}
";

            GL.ShaderSource(vertexShader, vertexShaderSource);
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

            string fragmentShaderSource = @"
#version 330
 
in vec2 vTexCoordOut;
in float alphaReference;
out vec4 outputColor;
 
uniform sampler2D currentTexture;

void main()
{
    outputColor = texture(currentTexture, vTexCoordOut);
    if(outputColor.a <= alphaReference) discard;
}
";
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
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
