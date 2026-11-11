using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;

namespace GPR202_Assessment3
{
    public class Skybox
    {
        private float[] skyboxVertices = {
	        // positions          
	        -1.0f,  1.0f, -1.0f,
            -1.0f, -1.0f, -1.0f,
             1.0f, -1.0f, -1.0f,
             1.0f, -1.0f, -1.0f,
             1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f, -1.0f,

            -1.0f, -1.0f,  1.0f,
            -1.0f, -1.0f, -1.0f,
            -1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f,  1.0f,
            -1.0f, -1.0f,  1.0f,

             1.0f, -1.0f, -1.0f,
             1.0f, -1.0f,  1.0f,
             1.0f,  1.0f,  1.0f,
             1.0f,  1.0f,  1.0f,
             1.0f,  1.0f, -1.0f,
             1.0f, -1.0f, -1.0f,

            -1.0f, -1.0f,  1.0f,
            -1.0f,  1.0f,  1.0f,
             1.0f,  1.0f,  1.0f,
             1.0f,  1.0f,  1.0f,
             1.0f, -1.0f,  1.0f,
            -1.0f, -1.0f,  1.0f,

            -1.0f,  1.0f, -1.0f,
             1.0f,  1.0f, -1.0f,
             1.0f,  1.0f,  1.0f,
             1.0f,  1.0f,  1.0f,
            -1.0f,  1.0f,  1.0f,
            -1.0f,  1.0f, -1.0f,

            -1.0f, -1.0f, -1.0f,
            -1.0f, -1.0f,  1.0f,
             1.0f, -1.0f, -1.0f,
             1.0f, -1.0f, -1.0f,
            -1.0f, -1.0f,  1.0f,
             1.0f, -1.0f,  1.0f
        };
        private int vbo, vao;
        private int cubemapID;
        private string[] facesCubeMap =
        {
            "Skybox/right.jpg",
            "Skybox/left.jpg",
            "Skybox/top.jpg",
            "Skybox/bottom.jpg",
            "Skybox/front.jpg",
            "Skybox/back.jpg"
        };

        public int skyboxShaderProgram;
        string skyboxVertShader =
            @"
                #version 330 core

                layout (location=0) in vec3 aPosition;

                uniform mat4 view;
                uniform mat4 projection;

                out vec3 TexCoords;

                void main()
                {
                    TexCoords = aPosition;
                    vec4 pos = vec4(aPosition, 0) * view * projection;
                    gl_Position = pos.xyww;
                }
            ";

        string skyboxFragShader =
            @"
                #version 330 core

                out vec4 pixelColor;

                in vec3 TexCoords;

                uniform samplerCube skybox;

                void main()
                {
                    pixelColor = texture(skybox, TexCoords);
                }
            ";

        public Skybox()
        {
            LoadCubemap();
            Setup();
        }

        public void Draw(Matrix4 view, Matrix4 proj)
        {
            GL.UseProgram(skyboxShaderProgram);
            var loc1 = GL.GetUniformLocation(skyboxShaderProgram, "view");
            GL.UniformMatrix4(loc1, true, ref view);
            var loc2 = GL.GetUniformLocation(skyboxShaderProgram, "projection");
            GL.UniformMatrix4(loc2, true, ref proj);

            GL.BindVertexArray(vao);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.TextureCubeMap, cubemapID);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
        }

        private int LoadCubemap()
        {
            cubemapID = GL.GenTexture();
            GL.BindTexture(TextureTarget.TextureCubeMap, cubemapID);

            StbImage.stbi_set_flip_vertically_on_load(0);
            for (int i = 0; i < facesCubeMap.Length; i++)
            {
                string fullpath = "Resource/" + facesCubeMap[i];
                if (!File.Exists(fullpath))
                {
                    throw new FileNotFoundException("Skybox Texture not found at '" + fullpath + "'");
                }
                using (var stream = File.OpenRead(fullpath))
                {
                    ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                    GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i, 0, PixelInternalFormat.Rgba, image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
                }
            }

            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

            return cubemapID;
        }

        private void Setup()
        {
            vbo = GL.GenBuffer();
            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float) * skyboxVertices.Length, skyboxVertices, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

            // Setup our Skybox Shaders
            int vertexShaderObject = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShaderObject, skyboxVertShader);
            GL.CompileShader(vertexShaderObject);

            string vertexShaderInfo = GL.GetShaderInfoLog(vertexShaderObject);
            if (vertexShaderInfo != string.Empty)
            {
                Console.WriteLine("SKYBOX VERT: " + vertexShaderInfo);
            }

            // allocate fragment shader
            int fragmentShaderObject = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShaderObject, skyboxFragShader);
            GL.CompileShader(fragmentShaderObject);

            string fragmentShaderInfo = GL.GetShaderInfoLog(fragmentShaderObject);
            if (fragmentShaderInfo != string.Empty)
            {
                Console.WriteLine("SKYBOX FRAG: " + fragmentShaderInfo);
            }

            // create shader program
            skyboxShaderProgram = GL.CreateProgram();
            GL.AttachShader(skyboxShaderProgram, vertexShaderObject);
            GL.AttachShader(skyboxShaderProgram, fragmentShaderObject);
            GL.LinkProgram(skyboxShaderProgram);

            GL.DetachShader(skyboxShaderProgram, vertexShaderObject);
            GL.DetachShader(skyboxShaderProgram, fragmentShaderObject);
            GL.DeleteShader(vertexShaderObject);
            GL.DeleteShader(fragmentShaderObject);

            GL.UseProgram(skyboxShaderProgram);
            GL.Uniform1(GL.GetUniformLocation(skyboxShaderProgram, "skybox"), 0);

        }
    }
}
