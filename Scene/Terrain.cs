using System.Diagnostics;
using GPR202_A3.Debugging;
using GPR202_A3.Generation;
using GPR202_A3.Graphics;
using GPR202_A3.Utilities;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_A3.Scene
{
    /// <summary>
    /// The desert landscape. The heights are generated once at load and stored in a texture; the vertex shader
    /// builds each vertex from that texture, so the terrain has no vertex buffer. A CPU copy of the heights is
    /// kept for GetHeightAt, and craters change both copies while the game runs.
    /// </summary>
    internal class Terrain
    {
        #region Constants
        // Vertices along each side (the original used 2048). The world size stays the same, only the spacing changes.
        private const int GRIDSIZE = 1024;

        // Width and length of the terrain, in world units (the same as the original)
        private const float WORLDSIZE = 2048f;

        // Seed for the desert generator (change it for a different desert)
        private const int SEED = 2026;

        // Height of the oasis water's surface
        private const float WATERLEVEL = -12f;

        // World units covered by one repeat of each sand texture.
        // Separate sizes let the ripples be scaled without stretching the sand grain.
        private const float SANDTILESIZE = 96f;
        private const float RIPPLETILESIZE = 24f;

        // Texture units
        private const int HEIGHTMAPUNIT = 0;
        private const int SANDCOLORUNIT = 1;
        private const int SANDNORMALUNIT = 2;

        // Colours used if a sand texture is missing: plain sand, and a flat normal (pointing straight out)
        private static readonly (byte R, byte G, byte B) FallbackSandColor = (220, 180, 125);
        private static readonly (byte R, byte G, byte B) FlatNormalColor = (128, 128, 255);

        // Index that ends one triangle strip and starts the next (primitive restart).
        // The largest possible index, so it is never a real vertex.
        private const uint RESTARTINDEX = uint.MaxValue;

        // How far out a crater changes the terrain, as a multiple of its radius.
        // The rim has faded to almost nothing by then, so there is no step at the edge of the changed area.
        private const float CRATERREACH = 2f;

        // Keeps GetHeightAt's lookups inside the grid (x0 + 1 must still be a real grid point)
        private const float GRIDEDGEMARGIN = 0.001f;

        // For the memory readout
        private const double BYTESPERMB = 1024.0 * 1024.0;
        #endregion

        #region Public Fields
        /// <summary>
        /// Vertices along each side.
        /// </summary>
        public static int GridSize => GRIDSIZE;

        /// <summary>
        /// Width and length of the terrain in world units. The terrain is centred on the origin.
        /// </summary>
        public static float Size => WORLDSIZE;

        /// <summary>
        /// World units between neighbouring vertices.
        /// </summary>
        public static float Spacing => WORLDSIZE / (GRIDSIZE - 1);

        /// <summary>
        /// Height of the oasis water's surface.
        /// </summary>
        public static float WaterLevel => WATERLEVEL;
        #endregion

        #region Private Fields
        // CPU copy of every height, row by row (index = z * GRIDSIZE + x)
        private static float[] _heights = new float[GRIDSIZE * GRIDSIZE];

        // Half the world size: the grid runs from -HalfSize to +HalfSize
        private static float HalfSize => WORLDSIZE / 2f;

        // GPU resources this class owns, deleted in Delete()
        private readonly ShaderProgram _shader;
        private readonly HeightMapTexture _heightMap;
        private readonly Texture _sandColor;
        private readonly Texture _sandNormal;
        private readonly VAO _vao;
        private readonly IBO _ibo;
        private readonly int _indexCount;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that generates the desert, uploads it, and sets up the indices, textures and shader.
        /// </summary>
        public Terrain()
        {
            var timer = Stopwatch.StartNew();

            // --- Heights ---
            _heights = DesertGenerator.Generate(GRIDSIZE, Spacing, SEED);
            double generateMs = timer.Elapsed.TotalMilliseconds;
            _heightMap = new HeightMapTexture(_heights, GRIDSIZE, HEIGHTMAPUNIT);

            // --- Indices only ---
            // The VAO has no vertex attributes, but core OpenGL still needs one bound to draw, and it remembers the IBO
            List<uint> indices = BuildStripIndices();
            _vao = new VAO(); // binds itself, so the IBO below is stored in it
            _ibo = new IBO(indices);
            _vao.Unbind();
            _indexCount = indices.Count;

            // --- Sand textures ---
            _sandColor = LoadTextureOrDefault("Terrain/SandColor.png", SANDCOLORUNIT, FallbackSandColor);
            _sandNormal = LoadTextureOrDefault("Terrain/SandNormal.png", SANDNORMALUNIT, FlatNormalColor);

            // --- Shader (values that never change are uploaded once) ---
            _shader = new ShaderProgram("Terrain.vert", "Terrain.frag");
            _shader.Bind();
            _shader.SetInt("HeightMap", HEIGHTMAPUNIT);
            _shader.SetInt("SandColorMap", SANDCOLORUNIT);
            _shader.SetInt("SandNormalMap", SANDNORMALUNIT);
            _shader.SetInt("GridSize", GRIDSIZE);
            _shader.SetFloat("GridSpacing", Spacing);
            _shader.SetFloat("SandTileSize", SANDTILESIZE);
            _shader.SetFloat("RippleTileSize", RIPPLETILESIZE);
            _shader.SetFloat("WaterLevel", WATERLEVEL);

            timer.Stop();
            double indexMB = (long)_indexCount * sizeof(uint) / BYTESPERMB;
            DebugText.Log($"Terrain: {GRIDSIZE}x{GRIDSIZE} grid, generated in {generateMs:0} ms, total {timer.Elapsed.TotalMilliseconds:0} ms",
                Color4.LightGreen, DebugText.INFODURATION);
            DebugText.Log($"Terrain: 0 vertex bytes, {indexMB:0.0} MB of indices ({_indexCount} strip indices)",
                Color4.LightGreen, DebugText.INFODURATION);
            DebugText.CheckGLError("Terrain setup");
        }
        #endregion

        #region Public API
        /// <summary>
        /// The height at a grid point (kept from the original code).
        /// </summary>
        /// <param name="x">Grid column, 0 to GridSize - 1.</param>
        /// <param name="z">Grid row, 0 to GridSize - 1.</param>
        public static float GetHeightMapAt(int x, int z) => _heights[z * GRIDSIZE + x];

        /// <summary>
        /// The world position of a grid point on the surface (kept from the original code).
        /// </summary>
        /// <param name="x">Grid column, 0 to GridSize - 1.</param>
        /// <param name="z">Grid row, 0 to GridSize - 1.</param>
        public static Vector3 WorldPositionFromHeightMap(int x, int z) =>
            new(x * Spacing - HalfSize, GetHeightMapAt(x, z), z * Spacing - HalfSize);

        /// <summary>
        /// The surface height at any world position, blended between the four grid points around it.
        /// Positions outside the terrain use the nearest edge.
        /// </summary>
        public static float GetHeightAt(float worldX, float worldZ)
        {
            // World position to grid position, kept inside the grid
            float gridX = Math.Clamp((worldX + HalfSize) / Spacing, 0f, GRIDSIZE - 1 - GRIDEDGEMARGIN);
            float gridZ = Math.Clamp((worldZ + HalfSize) / Spacing, 0f, GRIDSIZE - 1 - GRIDEDGEMARGIN);

            int x0 = (int)gridX;
            int z0 = (int)gridZ;
            float fx = gridX - x0;
            float fz = gridZ - z0;

            // Bilinear: blend along x on both rows, then between the rows
            float near = MathHelper.Lerp(GetHeightMapAt(x0, z0), GetHeightMapAt(x0 + 1, z0), fx);
            float far = MathHelper.Lerp(GetHeightMapAt(x0, z0 + 1), GetHeightMapAt(x0 + 1, z0 + 1), fx);
            return MathHelper.Lerp(near, far, fz);
        }

        /// <summary>
        /// Adds a crater to the terrain. Only the changed area is uploaded to the GPU,
        /// and the shape and lighting update next frame.
        /// </summary>
        /// <param name="centre">The crater's centre (only x and z are used).</param>
        /// <param name="radius">Radius to the rim, in world units.</param>
        /// <param name="depth">Depth at the centre, in world units.</param>
        public void AddCrater(Vector3 centre, float radius, float depth)
        {
            // The square of grid points the crater reaches, kept inside the grid
            float reach = radius * CRATERREACH;
            int minX = WorldToGrid(centre.X - reach, MathF.Floor);
            int maxX = WorldToGrid(centre.X + reach, MathF.Ceiling);
            int minZ = WorldToGrid(centre.Z - reach, MathF.Floor);
            int maxZ = WorldToGrid(centre.Z + reach, MathF.Ceiling);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            float[] region = new float[width * height];

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    // Distance from the centre as a fraction of the radius (1 = the rim)
                    float dx = x * Spacing - HalfSize - centre.X;
                    float dz = z * Spacing - HalfSize - centre.Z;
                    float distance = MathF.Sqrt(dx * dx + dz * dz) / radius;

                    int index = z * GRIDSIZE + x;
                    _heights[index] += DesertGenerator.CraterShape(distance) * depth;
                    region[(z - minZ) * width + (x - minX)] = _heights[index];
                }
            }

            _heightMap.UpdateRegion(minX, minZ, width, height, region);
        }

        /// <summary>
        /// Uploads the sunlight settings. Call once on load.
        /// </summary>
        public void SetLighting(Vector3 lightDirection, Vector3 lightColor, float ambientStrength)
        {
            _shader.Bind();
            _shader.SetVector3("lightDir", lightDirection);
            _shader.SetVector3("lightColor", lightColor);
            _shader.SetFloat("ambientStrength", ambientStrength);
        }

        /// <summary>
        /// Uploads the projection matrix. Call when the window is resized.
        /// </summary>
        public void SetProjection(Matrix4 projection)
        {
            _shader.Bind();
            _shader.SetMatrix4("projection", projection);
        }

        /// <summary>
        /// Draws the terrain.
        /// </summary>
        /// <param name="view">The camera's view matrix.</param>
        /// <param name="debugView">The current debug view.</param>
        /// <param name="useLighting">Whether to light the terrain.</param>
        public void Render(Matrix4 view, DebugView debugView, bool useLighting)
        {
            _shader.Bind();
            _shader.SetMatrix4("view", view);
            _shader.SetInt("DebugView", (int)debugView);
            _shader.SetInt("UseLighting", useLighting ? 1 : 0);

            _heightMap.Bind();
            _sandColor.Bind();
            _sandNormal.Bind();

            // The restart index ends each row's strip, so the whole grid is one draw call
            GL.Enable(EnableCap.PrimitiveRestart);
            GL.PrimitiveRestartIndex(RESTARTINDEX);

            _vao.Bind();
            GL.DrawElements(PrimitiveType.TriangleStrip, _indexCount, DrawElementsType.UnsignedInt, 0);
            _vao.Unbind();

            GL.Disable(EnableCap.PrimitiveRestart);
        }

        /// <summary>
        /// Frees the terrain's GPU resources.
        /// </summary>
        public void Delete()
        {
            _shader.Delete();
            _heightMap.Delete();
            _sandColor.Delete();
            _sandNormal.Delete();
            _vao.Delete();
            _ibo.Delete();
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Builds one triangle strip per row of the grid, separated by restart indices.
        /// A strip zig-zags between two rows: (x, z), (x, z + 1), (x + 1, z), ... so each grid square costs
        /// 2 indices instead of 6. Starting on row z makes the triangles face up (counter-clockwise from above).
        /// </summary>
        private static List<uint> BuildStripIndices()
        {
            // Each row: 2 indices per column, plus 1 restart index
            var indices = new List<uint>((GRIDSIZE - 1) * (2 * GRIDSIZE + 1));

            for (int z = 0; z < GRIDSIZE - 1; z++)
            {
                for (int x = 0; x < GRIDSIZE; x++)
                {
                    // Vertex (x, z) is number z * GRIDSIZE + x; the vertex shader turns it back into x and z
                    indices.Add((uint)(z * GRIDSIZE + x));
                    indices.Add((uint)((z + 1) * GRIDSIZE + x));
                }

                indices.Add(RESTARTINDEX);
            }

            return indices;
        }

        /// <summary>
        /// Converts a world x or z to a grid index, rounded with the given function and kept inside the grid.
        /// </summary>
        private static int WorldToGrid(float world, Func<float, float> round) =>
            Math.Clamp((int)round((world + HalfSize) / Spacing), 0, GRIDSIZE - 1);

        /// <summary>
        /// Loads a texture, or makes a plain one-colour texture if the file is missing.
        /// </summary>
        private static Texture LoadTextureOrDefault(string file, int unit, (byte R, byte G, byte B) fallback)
        {
            if (File.Exists(AssetPaths.Resource(file)))
                return new Texture(file, unit);

            DebugText.Log($"'{file}' not found: using a plain colour instead", Color4.Orange, DebugText.WARNINGDURATION);
            return new Texture(fallback.R, fallback.G, fallback.B, unit);
        }
        #endregion
    }
}
