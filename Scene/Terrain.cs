using GPR202_A3.Debugging;
using GPR202_A3.Graphics;
using GPR202_A3.Utilities;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;

namespace GPR202_A3.Scene
{
    /// <summary>
    /// The landscape: a grid of vertices raised by a height map image, textured with one diffuse map stretched across it.
    ///
    /// STEP 0 NOTE: this is the original generation code moved onto the project's VAO/VBO/IBO, ShaderProgram and
    /// Texture classes, with the height-reading bug fixed. The slow CPU smoothing is kept as it was on purpose,
    /// so Step 1 (moving the height map to the GPU) can be measured against it.
    /// </summary>
    internal class Terrain
    {
        #region Constants
        // Shader locations of the vertex attributes
        private const int POSITIONLOCATION = 0;
        private const int UVLOCATION = 1;

        // Each height map value (0..255) is multiplied by this to get a height in world units
        private const float HEIGHTSCALE = 4f;

        // How many pixels either side of each vertex are averaged to smooth the terrain (expensive)
        private const int SMOOTHRADIUS = 4;
        #endregion

        #region Public Fields
        /// <summary>
        /// Number of vertices along X (one per height map pixel).
        /// </summary>
        public static int Width { get; } = 2048;

        /// <summary>
        /// Number of vertices along Z (one per height map pixel).
        /// </summary>
        public static int Length { get; } = 2048;

        /// <summary>
        /// How far the whole terrain is moved down, in world units.
        /// </summary>
        public static float OffsetY { get; } = -600f;
        #endregion

        #region Private Fields
        // The raw height of every height map pixel, in world units (shared so balloons can be placed on the surface)
        private static float[,] _heightmap = new float[0, 0];

        // GPU resources this class owns, deleted in Delete()
        private readonly ShaderProgram _shader;
        private readonly Texture _diffuse;
        private readonly VAO _vao;
        private readonly VBO _positionVbo;
        private readonly VBO _uvVbo;
        private readonly IBO _ibo;
        private readonly int _indexCount;

        // The model matrix never changes, so it is uploaded once
        private readonly Matrix4 _model = Matrix4.CreateTranslation(0f, OffsetY, 0f);
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that reads the height map, builds the terrain mesh, and loads its texture and shader.
        /// </summary>
        public Terrain()
        {
            _heightmap = new float[Width, Length];
            ReadHeightMap("Terrain/heightmap.png");

            // --- Mesh ---
            // Capacity is set up front so the lists don't keep reallocating as 4 million vertices are added
            int vertexCount = Width * Length;
            var positions = new List<Vector3>(vertexCount);
            var uvs = new List<Vector2>(vertexCount);
            var indices = new List<uint>((Width - 1) * (Length - 1) * 6);
            BuildVertices(positions, uvs);
            BuildIndices(indices);

            _vao = new VAO();
            _positionVbo = new VBO(positions);
            _vao.LinkToVao(POSITIONLOCATION, 3, _positionVbo);
            _uvVbo = new VBO(uvs);
            _vao.LinkToVao(UVLOCATION, 2, _uvVbo);

            // Create the IBO while the VAO is bound so the VAO remembers it
            _vao.Bind();
            _ibo = new IBO(indices);
            _vao.Unbind();
            _indexCount = indices.Count;

            // --- Texture and shader ---
            _diffuse = new Texture("Terrain/diffuse.png", 0);

            _shader = new ShaderProgram("Terrain.vert", "Terrain.frag");
            _shader.Bind();
            _shader.SetInt("DiffuseMap", 0);
            _shader.SetMatrix4("model", _model);

            DebugText.Log($"Terrain: {vertexCount} vertices, {_indexCount / 3} triangles", Color4.LightGreen, 6f);
            DebugText.CheckGLError("Terrain setup");
        }
        #endregion

        #region Public API
        /// <summary>
        /// The raw height map value at a pixel, in world units (before OffsetY is applied).
        /// </summary>
        /// <param name="x">Pixel column, 0 to Width - 1.</param>
        /// <param name="z">Pixel row, 0 to Length - 1.</param>
        public static float GetHeightMapAt(int x, int z) => _heightmap[x, z];

        /// <summary>
        /// The world position of the terrain surface above a height map pixel.
        /// Only valid once the terrain has been created.
        /// </summary>
        /// <param name="x">Pixel column, 0 to Width - 1.</param>
        /// <param name="z">Pixel row, 0 to Length - 1.</param>
        public static Vector3 WorldPositionFromHeightMap(int x, int z) =>
            new(x - Width / 2, OffsetY + GetHeightMapAt(x, z), z - Length / 2);

        /// <summary>
        /// Uploads the projection matrix. Call when the window is resized.
        /// </summary>
        public void SetProjection(Matrix4 projection)
        {
            _shader.Bind();
            _shader.SetMatrix4("projection", projection);
        }

        /// <summary>
        /// Draws the terrain with its own shader.
        /// </summary>
        /// <param name="view">The camera's view matrix.</param>
        /// <param name="debugView">The current debug view (wireframe and UVs work on the terrain too).</param>
        public void Render(Matrix4 view, DebugView debugView)
        {
            _shader.Bind();
            _shader.SetMatrix4("view", view);
            _shader.SetInt("DebugView", (int)debugView);
            _diffuse.Bind();

            _vao.Bind();
            GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
            _vao.Unbind();
        }

        /// <summary>
        /// Frees the terrain's GPU resources.
        /// </summary>
        public void Delete()
        {
            _shader.Delete();
            _diffuse.Delete();
            _vao.Delete();
            _positionVbo.Delete();
            _uvVbo.Delete();
            _ibo.Delete();
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Reads the height map image into _heightmap, in world units.
        /// </summary>
        /// <param name="relativePath">The height map, relative to the Resource folder.</param>
        private static void ReadHeightMap(string relativePath)
        {
            string path = AssetPaths.Resource(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Height map not found at '{path}'");

            // The flip setting is global in StbImageSharp, so it is set here every time rather than relying
            // on whichever class loaded an image last (the original only worked because Plane happened to set it).
            StbImage.stbi_set_flip_vertically_on_load(1);

            using var stream = File.OpenRead(path);
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            for (int x = 0; x < Width; x++)
            {
                for (int z = 0; z < Length; z++)
                {
                    // Four bytes per pixel: red, green, blue, alpha.
                    // The original added 0, 1 and 2 to the red value instead of reading green and blue at those offsets.
                    int i = (x + Width * z) * 4;
                    float r = image.Data[i];
                    float g = image.Data[i + 1];
                    float b = image.Data[i + 2];
                    _heightmap[x, z] = (r + g + b) / 3f * HEIGHTSCALE;
                }
            }
        }

        /// <summary>
        /// Creates one vertex per height map pixel, centred on the origin, smoothing each height with its neighbours.
        /// </summary>
        private static void BuildVertices(List<Vector3> positions, List<Vector2> uvs)
        {
            // The order (x outer, z inner) means vertex (x, z) is at index x * Length + z
            for (int x = 0; x < Width; x++)
            {
                for (int z = 0; z < Length; z++)
                {
                    float h = _heightmap[x, z];

                    // Smooth with the neighbours (expensive: 64 reads per vertex). Unchanged from the original
                    // until Step 1, including its known issues: the window runs -4..+3 so it is off-centre,
                    // the edges aren't smoothed, and the result isn't stored back for GetHeightMapAt.
                    if (x > SMOOTHRADIUS && x < Width - 1 - SMOOTHRADIUS && z > SMOOTHRADIUS && z < Length - 1 - SMOOTHRADIUS)
                    {
                        float sum = 0f;
                        int count = 0;
                        for (int i = -SMOOTHRADIUS; i < SMOOTHRADIUS; i++)
                        {
                            for (int j = -SMOOTHRADIUS; j < SMOOTHRADIUS; j++)
                            {
                                sum += _heightmap[x + i, z + j];
                                count++;
                            }
                        }
                        h = sum / count;
                    }

                    positions.Add(new Vector3(x - Width / 2f, h, z - Length / 2f));

                    // Stretch the diffuse map across the whole terrain once
                    uvs.Add(new Vector2((float)x / Width, (float)z / Length));
                }
            }
        }

        /// <summary>
        /// Creates two triangles for each square of the grid, wound counter-clockwise when seen from above.
        /// </summary>
        private static void BuildIndices(List<uint> indices)
        {
            for (uint x = 0; x < Width - 1; x++)
            {
                for (uint z = 0; z < Length - 1; z++)
                {
                    uint corner = x * (uint)Length + z; // vertex (x, z)
                    uint nextX = corner + (uint)Length; // vertex (x + 1, z)

                    indices.Add(corner);
                    indices.Add(corner + 1);
                    indices.Add(nextX + 1);

                    indices.Add(nextX + 1);
                    indices.Add(nextX);
                    indices.Add(corner);
                }
            }
        }
        #endregion
    }
}
