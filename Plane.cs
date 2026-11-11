using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Assimp;
using Assimp.Configs;
using Assimp.Unmanaged;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using StbImageSharp;

namespace GPR202_Assessment3
{
    public class Plane : GameObject
    {
        List<VertexData> vertexdata;
        List<uint> indices;

        private Vector3 up = Vector3.UnitY;
        public Vector3 Up => up;

        private Vector3 front = -Vector3.UnitZ;
        public Vector3 Front => front;

        private Vector3 right = Vector3.UnitX;
        public Vector3 Right => right;

        private float _pitch;
        public float Pitch
        {
            get => _pitch;
            set
            {
                if (value > 89.0f)
                {
                    _pitch = 89.0f;
                }
                else if (value <= -89.0f)
                {
                    _pitch = -89.0f;
                }
                else
                {
                    _pitch = value;
                }
                UpdateVertices();
            }
        }

        private float yaw;
        public float Yaw
        {
            get => yaw;
            set
            {
                yaw = value;
                UpdateVertices();
            }
        }

        public Vector3 Direction => (position + Front);

        private int planeShaderProgram;
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

        public Plane()
        {
            ImportModelFile();
            Setup();
        }

        private bool firstMove = true;
        private Vector2 lastPos;
        private float Sensitivity = -1f;
        public float FlySpeed = 35.0f;
        public float TurnSpeed = 256.0f;
        public float LiftSpeed = 256.0f;
        public float Turn = 0f;
        public float Lift = 0f;

        public void OnFrameUpdate(MouseState mouse, int viewportW, int viewportH, double deltaT)
        {
            if (firstMove) // this bool variable is initially set to true
            {
                lastPos = new Vector2(mouse.X, mouse.Y);
                firstMove = false;
            }
            else
            {
                //Calculate the offset of the mouse position
                float deltaX = mouse.X - lastPos.X;
                float deltaY = mouse.Y - lastPos.Y;
                lastPos = new Vector2(mouse.X, mouse.Y);

                Turn = ((2.0f / viewportW) * mouse.X) - 1.0f;
                Lift = ((2.0f / viewportH) * mouse.Y) - 1.0f;
                position += new Vector3(0f, Lift * 0.1f, 0f) * LiftSpeed * (float)deltaT;

            }
        }

        public void UpdateVertices()
        {
            front.X = (float)Math.Cos(MathHelper.DegreesToRadians(Pitch)) * (float)Math.Cos(MathHelper.DegreesToRadians(Yaw));
            front.Y = (float)Math.Sin(MathHelper.DegreesToRadians(Pitch));
            front.Z = (float)Math.Cos(MathHelper.DegreesToRadians(Pitch)) * (float)Math.Sin(MathHelper.DegreesToRadians(Yaw));
            front = Vector3.Normalize(front);

            right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
            up = Vector3.Normalize(Vector3.Cross(Right, Front));
        }

        public void Draw(Matrix4 view, Matrix4 proj, Camera cam)
        {
            model = Matrix4.Identity * Matrix4.CreateScale(scale) * Matrix4.CreateRotationY(rotation.Y) * Matrix4.CreateRotationX(rotation.X) * Matrix4.CreateRotationZ(rotation.Z) * Matrix4.CreateTranslation(position);

            GL.UseProgram(planeShaderProgram);
            var loc0 = GL.GetUniformLocation(planeShaderProgram, "model");
            GL.UniformMatrix4(loc0, true, ref model);
            var loc1 = GL.GetUniformLocation(planeShaderProgram, "view");
            GL.UniformMatrix4(loc1, true, ref view);
            var loc2 = GL.GetUniformLocation(planeShaderProgram, "projection");
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
                Console.WriteLine("PLANE VERT: " + vertexShaderInfo);
            }

            int fragmentShaderObject = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShaderObject, fragShaderCode);
            GL.CompileShader(fragmentShaderObject);

            string fragmentShaderInfo = GL.GetShaderInfoLog(fragmentShaderObject);
            if (fragmentShaderInfo != string.Empty)
            {
                Console.WriteLine("PLANE FRAG: " + fragmentShaderInfo);
            }

            // create shader program
            planeShaderProgram = GL.CreateProgram();
            GL.AttachShader(planeShaderProgram, vertexShaderObject);
            GL.AttachShader(planeShaderProgram, fragmentShaderObject);
            GL.LinkProgram(planeShaderProgram);

            GL.DetachShader(planeShaderProgram, vertexShaderObject);
            GL.DetachShader(planeShaderProgram, fragmentShaderObject);
            GL.DeleteShader(vertexShaderObject);
            GL.DeleteShader(fragmentShaderObject);

            GL.UseProgram(planeShaderProgram);
        }

        private void ImportModelFile()
        {
            // Load the Assimp library from the specified path (for my mac)
            AssimpLibrary.Instance.LoadLibrary("/opt/homebrew/opt/assimp/lib/libassimp.dylib");
            
            string modelName = "plane.obj";
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
            // Load the model data into own structures
            vertexdata = new List<VertexData>();
            indices = new List<uint>();

            foreach (Mesh mesh in model.Meshes)
            {
                int vertexOffset = vertexdata.Count;

                // Load vertices
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    float u = 0f;
                    float v = 0f;

                    if (mesh.HasTextureCoords(0))
                    {
                        u = mesh.TextureCoordinateChannels[0][i].X;
                        v = mesh.TextureCoordinateChannels[0][i].Y;
                    }

                    VertexData vertex = new VertexData(
                        mesh.Vertices[i].X,
                        mesh.Vertices[i].Y,
                        mesh.Vertices[i].Z,
                        u,
                        v
                    );

                    vertexdata.Add(vertex);
                }

                // Load indices
                for (int i = 0; i < mesh.FaceCount; i++)
                {
                    Assimp.Face face = mesh.Faces[i];

                    for (int j = 0; j < face.IndexCount; j++)
                    {
                        indices.Add((uint)(face.Indices[j] + vertexOffset));
                    }
                }

                Console.WriteLine("Loaded mesh: " + mesh.Name +
                                  " | Vertices: " + mesh.VertexCount +
                                  " | Faces: " + mesh.FaceCount);
            }

            // Load Texture
            textureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureHandle);

            StbImage.stbi_set_flip_vertically_on_load(1);
            string fullpath = "Resource/Model/BodyTexture.jpeg";
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
