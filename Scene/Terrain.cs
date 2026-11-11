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
    /// The desert landscape, generated in code and drawn entirely from a height texture on the GPU.
    ///
    /// How it works:
    ///  1. DesertGenerator makes a grid of heights (dunes and an oasis basin) once, at load.
    ///  2. The heights are uploaded as a float texture (HeightMapTexture). A copy stays on the CPU for
    ///     GetHeightAt (balloon spawning now, collision and prop placement later).
    ///  3. The mesh has no vertex buffer at all. Each vertex works out its own grid position from gl_VertexID,
    ///     reads its height from the texture, and works out its normal from its neighbours' heights, all in the
    ///     vertex shader. The only buffer is the index buffer, which joins the grid into triangle strips.
    ///  4. AddCrater changes part of the height texture while the game runs, and the surface and lighting update
    ///     immediately because the vertex shader reads the new heights next frame.
    /// </summary>
    internal class Terrain
    {
        #region Constants
        // Grid points (vertices) along each side. The original used 2048 (4.2 million vertices); 1024 is a quarter of
        // that, which suits smooth dunes. Change it to compare: the world size stays the same, only the spacing changes.
        private const int GRIDSIZE = 1024;

        // Width and length of the terrain in world units (the same as the original)
        private const float WORLDSIZE = 2048f;

        // Seed for the desert generator: change it for a different (but repeatable) desert
        private const int SEED = 2026;

        // Height of the oasis water's surface, in world units (the water is drawn in Step 2)
        private const float WATERLEVEL = -12f;

        // How many world units one repeat of each sand texture covers. They are separate so the ripples (normal map)
        // can be scaled up without stretching the sand's grain (colour map). The normal map has 14 ripples per repeat,
        // so the ripples are RIPPLETILESIZE / 14 units apart.
        private const float SANDTILESIZE = 96f;
        private const float RIPPLETILESIZE = 24f;

        // Texture units
        private const int HEIGHTMAPUNIT = 0;
        private const int SANDCOLORUNIT = 1;
        private const int SANDNORMALUNIT = 2;

        // The index that tells OpenGL "end this strip and start a new one" (primitive restart).
        // The largest possible index, so it can never be a real vertex.
        private const uint RESTARTINDEX = uint.MaxValue;

        // Craters: how far out (as a multiple of the radius) the raised rim is worked out. By 2x the rim has faded
        // to almost nothing (0.01% of the depth), so there is no visible step where the changed square ends.
        private const float CRATERRIMEXTENT = 2f;
        #endregion

        #region Public Fields
        /// <summary>
        /// Grid points along each side.
        /// </summary>
        public static int GridSize => GRIDSIZE;

        /// <summary>
        /// Width and length of the terrain in world units. The terrain is centred on the origin.
        /// </summary>
        public static float Size => WORLDSIZE;

        /// <summary>
        /// World units between neighbouring grid points.
        /// </summary>
        public static float Spacing => WORLDSIZE / (GRIDSIZE - 1);

        /// <summary>
        /// Height of the oasis water's surface, in world units.
        /// </summary>
        public static float WaterLevel => WATERLEVEL;
        #endregion

        #region Private Fields
        // CPU copy of every grid point's height, row by row (index = z * GRIDSIZE + x). Shared so anything can
        // ask for the surface height, and kept in step with the GPU texture when craters change it.
        private static float[] _heights = new float[GRIDSIZE * GRIDSIZE];

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
        /// Constructor that generates the desert, uploads it, and builds the strip indices and shader.
        /// </summary>
        public Terrain()
        {
            var timer = Stopwatch.StartNew();

            // --- Heights ---
            _heights = DesertGenerator.Generate(GRIDSIZE, Spacing, SEED);
            double generateMs = timer.Elapsed.TotalMilliseconds;

            _heightMap = new HeightMapTexture(_heights, GRIDSIZE, HEIGHTMAPUNIT);

            // --- Mesh: indices only ---
            // The VAO has no attributes: the vertex shader makes each vertex from gl_VertexID and the height texture.
            // It still has to exist (core OpenGL won't draw without one), and it remembers the index buffer.
            List<uint> indices = BuildStripIndices();
            _vao = new VAO();
            _ibo = new IBO(indices); // created while the VAO is bound (the VAO constructor binds it), so the VAO remembers it
            _vao.Unbind();
            _indexCount = indices.Count;

            // --- Sand textures (tiling) ---
            _sandColor = LoadTextureOrDefault("Terrain/SandColor.png", SANDCOLORUNIT, 220, 180, 125);
            _sandNormal = LoadTextureOrDefault("Terrain/SandNormal.png", SANDNORMALUNIT, 128, 128, 255);

            // --- Shader: values that never change are uploaded once ---
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
            long indexBytes = (long)_indexCount * sizeof(uint);
            DebugText.Log($"Terrain: {GRIDSIZE}x{GRIDSIZE} grid, generated in {generateMs:0} ms, total {timer.Elapsed.TotalMilliseconds:0} ms",
                Color4.LightGreen, 8f);
            DebugText.Log($"Terrain: 0 vertex bytes, {indexBytes / (1024.0 * 1024.0):0.0} MB of indices ({_indexCount} strip indices)",
                Color4.LightGreen, 8f);
            DebugText.CheckGLError("Terrain setup");
        }
        #endregion

        #region Public API
        /// <summary>
        /// The height at a grid point, in world units (kept from the original API, now in grid coordinates).
        /// </summary>
        /// <param name="x">Grid column, 0 to GridSize - 1.</param>
        /// <param name="z">Grid row, 0 to GridSize - 1.</param>
        public static float GetHeightMapAt(int x, int z) => _heights[z * GRIDSIZE + x];

        /// <summary>
        /// The world position of a grid point on the surface (kept from the original API).
        /// </summary>
        /// <param name="x">Grid column, 0 to GridSize - 1.</param>
        /// <param name="z">Grid row, 0 to GridSize - 1.</param>
        public static Vector3 WorldPositionFromHeightMap(int x, int z)
        {
            float halfSize = WORLDSIZE * 0.5f;
            return new Vector3(x * Spacing - halfSize, GetHeightMapAt(x, z), z * Spacing - halfSize);
        }

        /// <summary>
        /// The surface height at any world position, blended between the four grid points around it.
        /// Positions outside the terrain use the nearest edge.
        /// </summary>
        /// <param name="worldX">World x.</param>
        /// <param name="worldZ">World z.</param>
        public static float GetHeightAt(float worldX, float worldZ)
        {
            // World position to grid coordinates (fractional), kept inside the grid
            float halfSize = WORLDSIZE * 0.5f;
            float gridX = Math.Clamp((worldX + halfSize) / Spacing, 0f, GRIDSIZE - 1.001f);
            float gridZ = Math.Clamp((worldZ + halfSize) / Spacing, 0f, GRIDSIZE - 1.001f);

            int x0 = (int)gridX;
            int z0 = (int)gridZ;
            float fx = gridX - x0;
            float fz = gridZ - z0;

            // Bilinear: blend along x on both rows, then between the rows
            float h00 = GetHeightMapAt(x0, z0);
            float h10 = GetHeightMapAt(x0 + 1, z0);
            float h01 = GetHeightMapAt(x0, z0 + 1);
            float h11 = GetHeightMapAt(x0 + 1, z0 + 1);
            float near = MathHelper.Lerp(h00, h10, fx);
            float far = MathHelper.Lerp(h01, h11, fx);
            return MathHelper.Lerp(near, far, fz);
        }

        /// <summary>
        /// Blasts a crater into the terrain: a bowl with a raised rim. Changes the CPU heights and uploads only the
        /// changed square to the GPU, so the surface, its normals and its lighting update next frame.
        /// </summary>
        /// <param name="centre">World position of the crater's centre (only x and z are used).</param>
        /// <param name="radius">Radius of the bowl, in world units.</param>
        /// <param name="depth">How deep the bowl's centre is, in world units.</param>
        public void AddCrater(Vector3 centre, float radius, float depth)
        {
            // The square of grid points the crater (including its rim) can reach, kept inside the grid
            float halfSize = WORLDSIZE * 0.5f;
            float reach = radius * CRATERRIMEXTENT;
            int minX = Math.Clamp((int)MathF.Floor((centre.X - reach + halfSize) / Spacing), 0, GRIDSIZE - 1);
            int maxX = Math.Clamp((int)MathF.Ceiling((centre.X + reach + halfSize) / Spacing), 0, GRIDSIZE - 1);
            int minZ = Math.Clamp((int)MathF.Floor((centre.Z - reach + halfSize) / Spacing), 0, GRIDSIZE - 1);
            int maxZ = Math.Clamp((int)MathF.Ceiling((centre.Z + reach + halfSize) / Spacing), 0, GRIDSIZE - 1);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            float[] region = new float[width * height];

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    // Distance from the centre as a fraction of the radius (1 = the bowl's edge)
                    float dx = x * Spacing - halfSize - centre.X;
                    float dz = z * Spacing - halfSize - centre.Z;
                    float distance = MathF.Sqrt(dx * dx + dz * dz) / radius;

                    int index = z * GRIDSIZE + x;
                    _heights[index] += DesertGenerator.CraterShape(distance) * depth;
                    region[(z - minZ) * width + (x - minX)] = _heights[index];
                }
            }

            _heightMap.UpdateRegion(minX, minZ, width, height, region);
        }

        /// <summary>
        /// Uploads the sun's direction and colour, and the ambient light. Call once on load.
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
        /// Draws the terrain as triangle strips, with its own shader.
        /// </summary>
        /// <param name="view">The camera's view matrix.</param>
        /// <param name="debugView">The current debug view (normals, UVs and wireframe work on the terrain).</param>
        /// <param name="useLighting">Whether to light the terrain or show the raw sand colour.</param>
        public void Render(Matrix4 view, DebugView debugView, bool useLighting)
        {
            _shader.Bind();
            _shader.SetMatrix4("view", view);
            _shader.SetInt("DebugView", (int)debugView);
            _shader.SetInt("UseLighting", useLighting ? 1 : 0);

            _heightMap.Bind();
            _sandColor.Bind();
            _sandNormal.Bind();

            // Primitive restart: whenever OpenGL meets RESTARTINDEX in the index buffer it ends the current strip and
            // starts a new one, so the whole grid (one strip per row) is a single draw call
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
        /// Builds the index buffer: one triangle strip per row of grid squares, separated by restart indices.
        ///
        /// A strip zig-zags between two rows of vertices: (x, z), (x, z + 1), (x + 1, z), (x + 1, z + 1), ...
        /// Every new index after the first two makes a triangle with the two before it, so each square of the grid
        /// costs 2 indices instead of the 6 that separate triangles need. Starting on row z (not z + 1) makes the
        /// first triangle counter-clockwise when seen from above, so the top faces are the front faces.
        /// OpenGL flips every second triangle of a strip automatically, so they all keep the same winding.
        /// </summary>
        /// <returns>(GRIDSIZE - 1) * (2 * GRIDSIZE + 1) indices.</returns>
        private static List<uint> BuildStripIndices()
        {
            var indices = new List<uint>((GRIDSIZE - 1) * (2 * GRIDSIZE + 1));

            for (int z = 0; z < GRIDSIZE - 1; z++)
            {
                for (int x = 0; x < GRIDSIZE; x++)
                {
                    // Vertex (x, z) is number z * GRIDSIZE + x, which the vertex shader turns back into x and z
                    indices.Add((uint)(z * GRIDSIZE + x));
                    indices.Add((uint)((z + 1) * GRIDSIZE + x));
                }

                indices.Add(RESTARTINDEX);
            }

            return indices;
        }

        /// <summary>
        /// Loads a tiling texture, or makes a plain one-colour texture if the file is missing, so the terrain
        /// still draws (in a flat colour) instead of black.
        /// </summary>
        private static Texture LoadTextureOrDefault(string file, int unit, byte red, byte green, byte blue)
        {
            if (File.Exists(AssetPaths.Resource(file)))
                return new Texture(file, unit);

            DebugText.Log($"'{file}' not found: using a plain colour instead", Color4.Orange, 10f);
            return new Texture(red, green, blue, unit);
        }
        #endregion
    }
}
