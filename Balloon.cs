using Assimp.Configs;
using Assimp;
using OpenTK.Mathematics;
using StbImageSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Assimp;
using Assimp.Configs;
using Assimp.Unmanaged;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;

namespace GPR202_Assessment3
{
    public class Balloon : GameObject
    {
        List<VertexData> vertexdata;
        List<uint> indices;

        private int balloonShaderProgram;
        string vertexShaderCode =
            @"
                #version 330 core

                layout (location  =0) in vec3 aPosition;
                layout(location = 1) in vec2 aTexCoord;

                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 projection;

                out vec2 texCoord;

                void main()
                {
                    texCoord = aTexCoord;
                    gl_Position = vec4(aPosition, 1.0f) * model * view * projection;
                    
                }";

        string fragShaderCode =
        @"
                #version 330 core

                out vec4 pixelColor;

                in vec2 texCoord;

                uniform sampler2D texture0;

                void main()
                {
                    vec4 texColor = texture(texture0, texCoord);
                    pixelColor = texColor;
                }
            ";

        public Balloon()
        {
            ImportModelFile();
            Setup();
        }

        public void Draw(Vector3 pos, Vector3 rot, float scale, Matrix4 view, Matrix4 proj)
        {
            this.position = pos;
            this.rotation = rot;
            this.scale = scale;

            model = Matrix4.Identity * Matrix4.CreateScale(scale) * Matrix4.CreateRotationX(rotation.X) * Matrix4.CreateRotationY(rotation.Y) * Matrix4.CreateTranslation(position);

            GL.UseProgram(balloonShaderProgram);
            var loc0 = GL.GetUniformLocation(balloonShaderProgram, "model");
            GL.UniformMatrix4(loc0, true, ref model);
            var loc1 = GL.GetUniformLocation(balloonShaderProgram, "view");
            GL.UniformMatrix4(loc1, true, ref view);
            var loc2 = GL.GetUniformLocation(balloonShaderProgram, "projection");
            GL.UniformMatrix4(loc2, true, ref proj);

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, textureHandle);

            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, indices.Count, DrawElementsType.UnsignedInt, 0);
        }

        private void Setup()
        {
            // OpenGL Objects
            vbo = GL.GenBuffer();
            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            List<float> vbodata = new List<float>();
            for (int i = 0; i < vertexdata.Count; i++)
            {
                vbodata.Add(vertexdata[i].x);   // x position
                vbodata.Add(vertexdata[i].y);   // y position
                vbodata.Add(vertexdata[i].z);   // z position
                vbodata.Add(vertexdata[i].u);   // u texcoord
                vbodata.Add(vertexdata[i].v);   // v texcoord
            }
            float[] _vbo = vbodata.ToArray();

            GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float) * _vbo.Length, _vbo, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            ebo = GL.GenBuffer();
            uint[] _ebo = indices.ToArray();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, _ebo.Length * sizeof(uint), _ebo, BufferUsageHint.StaticDraw);

            // Vertex and Fragment Shaders
            int vertexShaderObject = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShaderObject, vertexShaderCode);
            GL.CompileShader(vertexShaderObject);

            string vertexShaderInfo = GL.GetShaderInfoLog(vertexShaderObject);
            if (vertexShaderInfo != string.Empty)
            {
                Console.WriteLine("BALLOON VERT: " + vertexShaderInfo);
            }

            int fragmentShaderObject = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShaderObject, fragShaderCode);
            GL.CompileShader(fragmentShaderObject);

            string fragmentShaderInfo = GL.GetShaderInfoLog(fragmentShaderObject);
            if (fragmentShaderInfo != string.Empty)
            {
                Console.WriteLine("BALLOON FRAG: " + fragmentShaderInfo);
            }

            // create shader program
            balloonShaderProgram = GL.CreateProgram();
            GL.AttachShader(balloonShaderProgram, vertexShaderObject);
            GL.AttachShader(balloonShaderProgram, fragmentShaderObject);
            GL.LinkProgram(balloonShaderProgram);

            GL.DetachShader(balloonShaderProgram, vertexShaderObject);
            GL.DetachShader(balloonShaderProgram, fragmentShaderObject);
            GL.DeleteShader(vertexShaderObject);
            GL.DeleteShader(fragmentShaderObject);

            GL.UseProgram(balloonShaderProgram);
        }

        private void ImportModelFile()
        {
            string modelName = "balloon.obj";
            string fileName = "Resource/Model/" + modelName;

            //Create a new importer
            AssimpContext importer = new AssimpContext();

            //This is how we add a configuration (each config is its own class)
            NormalSmoothingAngleConfig config = new NormalSmoothingAngleConfig(66.0f);
            importer.SetConfig(config);

            //This is how we add a logging callback 
            LogStream logstream = new LogStream(delegate (String msg, String userData) {
                Console.WriteLine(msg);
            });
            logstream.Attach();

            //Import the model. All configs are set. The model
            //is imported, loaded into managed memory. Then the unmanaged memory is released, and everything is reset.
            Scene model = importer.ImportFile(fileName, PostProcessSteps.Triangulate);

            // Load the model data into own structures
            vertexdata = new List<VertexData>();
            for (int i = 0; i < model.Meshes[0].VertexCount; i++)
            {
                VertexData vertex =
                    new VertexData(model.Meshes[0].Vertices[i].X, model.Meshes[0].Vertices[i].Y, model.Meshes[0].Vertices[i].Z,
                                       model.Meshes[0].TextureCoordinateChannels[0][i].X, model.Meshes[0].TextureCoordinateChannels[0][i].Y);
                //Console.WriteLine("V " + model.Meshes[0].Vertices[i].X + ", " + model.Meshes[0].Vertices[i].Y + ", " + model.Meshes[0].Vertices[i].Z);
                vertexdata.Add(vertex);
            }

            indices = new List<uint>();
            for (int i = 0; i < model.Meshes[0].FaceCount; i++)
            {
                string debugF = "F: ";
                Assimp.Face face = model.Meshes[0].Faces[i];
                for (int j = 0; j < face.IndexCount; j++)
                {
                    debugF += (uint)face.Indices[j] + ", ";
                    indices.Add((uint)face.Indices[j]);
                }
                debugF += "\n";
                //Console.Write(debugF);
            }

            // Load Texture
            textureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureHandle);

            StbImage.stbi_set_flip_vertically_on_load(1);
            string fullpath = "Resource/Model/balloon.png";
            if (!File.Exists(fullpath))
            {
                throw new FileNotFoundException("Texture not found at '" + fullpath + "'");
            }
            using (var stream = File.OpenRead(fullpath))
            {
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
            }
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapR, (int)OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge);

            Console.WriteLine("Succesfully loaded " + modelName + " with " + model.Meshes[0].Vertices.Count + " vertices.");

            importer.Dispose();
        }
    }
}
