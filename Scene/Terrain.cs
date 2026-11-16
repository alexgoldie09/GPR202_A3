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
    /// It's drawn in patches with level of detail (TerrainLod), and lit per pixel from a normal map, so the
    /// lighting stays at full detail when a patch drops to a coarser level.
    /// </summary>
    internal class Terrain
    {
        #region Constants
        // Vertices along each side. 2049 = 32 patches of 64 squares (TerrainLod needs a whole number of patches).
        private const int GRIDSIZE = 2049;

        // Width and length of the terrain, in world units: twice the play area, so the desert carries on past it.
        // With 2049 vertices, that's exactly 2 units between vertices.
        private const float WORLDSIZE = 4096f;

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
        private const int TERRAINNORMALUNIT = 3;

        // Colours used if a sand texture is missing: plain sand, and a flat normal (pointing straight out)
        private static readonly (byte R, byte G, byte B) FallbackSandColor = (220, 180, 125);
        private static readonly (byte R, byte G, byte B) FlatNormalColor = (128, 128, 255);

        // Bytes per pixel in the normal map (red, green, blue, alpha), and the largest byte value
        private const int CHANNELS = 4;
        private const float MAXBYTE = 255f;

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

        /// <summary>
        /// Whether level of detail and culling are on (F10 turns them off, to measure the difference).
        /// </summary>
        public bool LodEnabled
        {
            get => _lod.Enabled;
            set => _lod.Enabled = value;
        }

        /// <summary>
        /// Patches in the whole terrain.
        /// </summary>
        public int PatchCount => _lod.PatchCount;

        /// <summary>
        /// Patches drawn by the last Render (the main pass is drawn last each frame).
        /// </summary>
        public int PatchesDrawn => _lod.PatchesDrawn;

        /// <summary>
        /// Triangles drawn by the last Render.
        /// </summary>
        public int TrianglesDrawn => _lod.TrianglesDrawn;
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
        private readonly Texture _normalMap;
        private readonly TerrainLod _lod;

        // Kept for culling (view * projection)
        private Matrix4 _projection = Matrix4.Identity;
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

            // --- Surface normals, one per vertex, for per-pixel lighting ---
            _normalMap = new Texture(BuildNormals(0, 0, GRIDSIZE - 1, GRIDSIZE - 1), GRIDSIZE, GRIDSIZE, TERRAINNORMALUNIT);

            // --- Patches and their detail levels (the shared index lists) ---
            _lod = new TerrainLod(_heights, GRIDSIZE, Spacing);

            // --- Sand textures ---
            _sandColor = LoadTextureOrDefault("Terrain/SandColor.png", SANDCOLORUNIT, FallbackSandColor);
            _sandNormal = LoadTextureOrDefault("Terrain/SandNormal.png", SANDNORMALUNIT, FlatNormalColor);

            // --- Shader (values that never change are uploaded once) ---
            _shader = new ShaderProgram("Terrain.vert", "Terrain.frag");
            _shader.Bind();
            _shader.SetInt("HeightMap", HEIGHTMAPUNIT);
            _shader.SetInt("SandColorMap", SANDCOLORUNIT);
            _shader.SetInt("SandNormalMap", SANDNORMALUNIT);
            _shader.SetInt("TerrainNormalMap", TERRAINNORMALUNIT);
            _shader.SetInt("GridSize", GRIDSIZE);
            _shader.SetFloat("GridSpacing", Spacing);
            _shader.SetFloat("SandTileSize", SANDTILESIZE);
            _shader.SetFloat("RippleTileSize", RIPPLETILESIZE);
            _shader.SetFloat("WaterLevel", WATERLEVEL);

            timer.Stop();
            double indexMB = (long)_lod.IndexCount * sizeof(uint) / BYTESPERMB;
            Log.Info($"Terrain: {GRIDSIZE}x{GRIDSIZE} grid, generated in {generateMs:0} ms, total {timer.Elapsed.TotalMilliseconds:0} ms");
            Log.Info($"Terrain: {_lod.PatchCount} patches, 0 vertex bytes, {indexMB:0.0} MB of shared indices ({_lod.IndexCount})");
            Log.CheckGLError("Terrain setup");
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

            // The patches' bounding boxes, and the normals (one further out, since a normal uses its neighbours)
            _lod.UpdateBounds(_heights, minX, minZ, maxX, maxZ);
            int normalMinX = Math.Max(0, minX - 1);
            int normalMinZ = Math.Max(0, minZ - 1);
            int normalMaxX = Math.Min(GRIDSIZE - 1, maxX + 1);
            int normalMaxZ = Math.Min(GRIDSIZE - 1, maxZ + 1);
            _normalMap.UpdateRegion(normalMinX, normalMinZ, normalMaxX - normalMinX + 1, normalMaxZ - normalMinZ + 1,
                BuildNormals(normalMinX, normalMinZ, normalMaxX, normalMaxZ));
        }

        /// <summary>
        /// Sets the area Render draws when asked for areaOnly: a square of the terrain.
        /// The water's refraction pass uses it, since it only ever sees the terrain under the water.
        /// </summary>
        /// <param name="centre">The square's centre in world x and z.</param>
        /// <param name="halfSize">Half the square's width, in world units.</param>
        public void SetArea(Vector2 centre, float halfSize) => _lod.SetArea(centre, halfSize);

        /// <summary>
        /// Picks each patch's detail level from its distance to the camera. Call once per frame, before rendering.
        /// Every pass that frame (reflection, refraction, main) uses the same levels, so they all match.
        /// </summary>
        public void SelectLevels(Vector3 cameraPosition) => _lod.SelectLevels(cameraPosition);

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
            _projection = projection;
            _shader.Bind();
            _shader.SetMatrix4("projection", projection);
        }

        /// <summary>
        /// Draws the patches the camera can see, each at its detail level.
        /// </summary>
        /// <param name="view">This pass's view matrix.</param>
        /// <param name="clipPlane">The water pass's clip plane (only used while clipping is enabled).</param>
        /// <param name="debugView">The current debug view.</param>
        /// <param name="useLighting">Whether to light the terrain.</param>
        /// <param name="areaOnly">true to draw only the area set by SetArea.</param>
        public void Render(Matrix4 view, Vector4 clipPlane, DebugView debugView, bool useLighting, bool areaOnly = false)
        {
            _shader.Bind();
            _shader.SetMatrix4("view", view);
            _shader.SetVector4("clipPlane", clipPlane);
            _shader.SetInt("DebugView", (int)debugView);
            _shader.SetInt("UseLighting", useLighting ? 1 : 0);

            _heightMap.Bind();
            _sandColor.Bind();
            _sandNormal.Bind();
            _normalMap.Bind();

            _lod.Draw(view * _projection, areaOnly);
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
            _normalMap.Delete();
            _lod.Delete();
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Works out the surface normal at every grid point in a rectangle, as normal map pixels.
        /// Each normal comes from its neighbours' heights: if the ground rises to the right, the normal leans left.
        /// </summary>
        /// <returns>The rectangle's normals as RGBA bytes, row by row (each axis -1..1 stored as 0..255).</returns>
        private static byte[] BuildNormals(int minX, int minZ, int maxX, int maxZ)
        {
            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            byte[] pixels = new byte[width * height * CHANNELS];

            Parallel.For(0, height, row =>
            {
                int z = minZ + row;
                for (int x = minX; x <= maxX; x++)
                {
                    // Neighbours past the edge use the edge
                    float left = GetHeightMapAt(Math.Max(x - 1, 0), z);
                    float right = GetHeightMapAt(Math.Min(x + 1, GRIDSIZE - 1), z);
                    float down = GetHeightMapAt(x, Math.Max(z - 1, 0));
                    float up = GetHeightMapAt(x, Math.Min(z + 1, GRIDSIZE - 1));
                    Vector3 normal = Vector3.Normalize(new Vector3(left - right, 2f * Spacing, down - up));

                    int i = (row * width + (x - minX)) * CHANNELS;
                    pixels[i] = ToByte(normal.X);
                    pixels[i + 1] = ToByte(normal.Y);
                    pixels[i + 2] = ToByte(normal.Z);
                    pixels[i + 3] = (byte)MAXBYTE;
                }
            });

            return pixels;
        }

        /// <summary>
        /// A -1..1 value stored as a 0..255 byte.
        /// </summary>
        private static byte ToByte(float value) => (byte)MathF.Round((value * 0.5f + 0.5f) * MAXBYTE);

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

            Log.Warning($"'{file}' not found: using a plain colour instead");
            return new Texture(fallback.R, fallback.G, fallback.B, unit);
        }
        #endregion
    }
}
