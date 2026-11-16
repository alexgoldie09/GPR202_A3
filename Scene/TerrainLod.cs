using GPR202_A3.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_A3.Scene
{
    /// <summary>
    /// Level of detail for the terrain (geomipmapping, de Boer 2000; the pattern follows ogldev's Terrain7).
    /// The grid is cut into square patches. Near patches are drawn with every vertex, far ones with every 2nd,
    /// 4th, 8th... vertex, and patches outside the camera's view aren't drawn at all.
    ///
    /// Every patch uses the same index lists: they're numbered from the patch's corner, and the draw call adds
    /// the patch's first vertex (base vertex). The terrain's vertex shader already turns the vertex number back
    /// into a grid position, so it works unchanged.
    /// </summary>
    internal class TerrainLod
    {
        #region Constants
        // Grid squares along each side of a patch (a power of 2, so each level can halve it)
        private const int PATCHQUADS = 64;

        // Detail levels: 0 uses every vertex, 1 every 2nd, ... 5 every 32nd (a whole patch is then 8 triangles)
        private const int LODCOUNT = 6;

        // Distance where level 1 starts; each level after starts at double the one before (160, 320, 640, ...).
        // Must be more than a patch's width (128 units), so neighbouring patches are never more than one level apart.
        private const float LODBASEDISTANCE = 160f;

        // Edge variants per level: one for every combination of the 4 neighbours being one level coarser
        private const int VARIANTCOUNT = 16;
        private const int LEFTCOARSER = 1;
        private const int RIGHTCOARSER = 2;
        private const int BOTTOMCOARSER = 4;
        private const int TOPCOARSER = 8;

        // Each block of a level is a fan of up to 8 triangles around its centre
        private const int TRIANGLEVERTICES = 3;
        #endregion

        #region Public Fields
        /// <summary>
        /// When false, every patch is drawn at full detail and nothing is culled (F10, for measuring the difference).
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Patches in the whole terrain.
        /// </summary>
        public int PatchCount => _patchesPerSide * _patchesPerSide;

        /// <summary>
        /// Patches drawn by the last Draw call.
        /// </summary>
        public int PatchesDrawn { get; private set; }

        /// <summary>
        /// Triangles drawn by the last Draw call.
        /// </summary>
        public int TrianglesDrawn { get; private set; }

        /// <summary>
        /// Total indices in the shared index buffer (for the memory readout).
        /// </summary>
        public int IndexCount { get; }
        #endregion

        #region Private Fields
        private readonly int _gridSize;
        private readonly int _patchesPerSide;
        private readonly float _spacing;
        private readonly float _halfSize;

        // The shared index lists: where each level's variant starts, and how many indices it has
        private readonly int[,] _variantOffsets = new int[LODCOUNT, VARIANTCOUNT];
        private readonly int[,] _variantCounts = new int[LODCOUNT, VARIANTCOUNT];
        private readonly VAO _vao;
        private readonly IBO _ibo;

        // Per patch, row by row (index = patchZ * _patchesPerSide + patchX)
        private readonly Vector3[] _boundsMin;
        private readonly Vector3[] _boundsMax;
        private readonly int[] _levels;
        private readonly int[] _variants;

        // Reused every draw, so drawing allocates nothing
        private readonly int[] _drawCounts;
        private readonly IntPtr[] _drawOffsets;
        private readonly int[] _drawBaseVertices;
        private readonly Frustum _frustum = new();

        // The area drawn when Draw is asked for areaOnly (e.g. the ground under the water)
        private Vector2 _areaMin;
        private Vector2 _areaMax;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that builds the shared index lists and each patch's bounding box.
        /// </summary>
        /// <param name="heights">Every height, row by row (index = z * gridSize + x).</param>
        /// <param name="gridSize">Vertices along each side. gridSize - 1 must be a multiple of the patch size.</param>
        /// <param name="spacing">World units between vertices.</param>
        public TerrainLod(float[] heights, int gridSize, float spacing)
        {
            _gridSize = gridSize;
            _spacing = spacing;
            _halfSize = (gridSize - 1) * spacing / 2f;
            _patchesPerSide = (gridSize - 1) / PATCHQUADS;

            _boundsMin = new Vector3[PatchCount];
            _boundsMax = new Vector3[PatchCount];
            _levels = new int[PatchCount];
            _variants = new int[PatchCount];
            _drawCounts = new int[PatchCount];
            _drawOffsets = new IntPtr[PatchCount];
            _drawBaseVertices = new int[PatchCount];

            // --- Shared index lists ---
            List<uint> indices = BuildIndices();
            IndexCount = indices.Count;
            _vao = new VAO(); // binds itself, so the IBO below is stored in it
            _ibo = new IBO(indices);
            _vao.Unbind();

            // --- Bounding boxes ---
            UpdateBounds(heights, 0, 0, gridSize - 1, gridSize - 1);
        }
        #endregion

        #region Public API
        /// <summary>
        /// Recalculates the bounding boxes of the patches covering part of the grid (after a crater changes it).
        /// </summary>
        /// <param name="heights">Every height, row by row.</param>
        /// <param name="minX">First changed column.</param>
        /// <param name="minZ">First changed row.</param>
        /// <param name="maxX">Last changed column.</param>
        /// <param name="maxZ">Last changed row.</param>
        public void UpdateBounds(float[] heights, int minX, int minZ, int maxX, int maxZ)
        {
            // Patches share their edge vertices, so a change on an edge affects the patches on both sides
            int firstPatchX = Math.Max(0, (minX - 1) / PATCHQUADS);
            int lastPatchX = Math.Min(_patchesPerSide - 1, maxX / PATCHQUADS);
            int firstPatchZ = Math.Max(0, (minZ - 1) / PATCHQUADS);
            int lastPatchZ = Math.Min(_patchesPerSide - 1, maxZ / PATCHQUADS);

            for (int pz = firstPatchZ; pz <= lastPatchZ; pz++)
            {
                for (int px = firstPatchX; px <= lastPatchX; px++)
                {
                    float lowest = float.MaxValue;
                    float highest = float.MinValue;
                    for (int z = pz * PATCHQUADS; z <= (pz + 1) * PATCHQUADS; z++)
                    {
                        for (int x = px * PATCHQUADS; x <= (px + 1) * PATCHQUADS; x++)
                        {
                            float h = heights[z * _gridSize + x];
                            lowest = MathF.Min(lowest, h);
                            highest = MathF.Max(highest, h);
                        }
                    }

                    int patch = pz * _patchesPerSide + px;
                    _boundsMin[patch] = new Vector3(GridToWorld(px * PATCHQUADS), lowest, GridToWorld(pz * PATCHQUADS));
                    _boundsMax[patch] = new Vector3(GridToWorld((px + 1) * PATCHQUADS), highest, GridToWorld((pz + 1) * PATCHQUADS));
                }
            }
        }

        /// <summary>
        /// Sets the area Draw uses when asked for areaOnly (a square in world x and z).
        /// </summary>
        public void SetArea(Vector2 centre, float halfSize)
        {
            _areaMin = centre - new Vector2(halfSize);
            _areaMax = centre + new Vector2(halfSize);
        }

        /// <summary>
        /// Picks every patch's detail level from its distance to the camera, then which edge variant it needs
        /// so it lines up with coarser neighbours. Call once per frame, before any Draw.
        /// </summary>
        public void SelectLevels(Vector3 cameraPosition)
        {
            // --- Pass 1: each patch's level from its distance ---
            for (int patch = 0; patch < PatchCount; patch++)
                _levels[patch] = Enabled ? DistanceToLevel(HorizontalDistance(cameraPosition, patch)) : 0;

            // --- Pass 2: which neighbours are coarser ---
            for (int pz = 0; pz < _patchesPerSide; pz++)
            {
                for (int px = 0; px < _patchesPerSide; px++)
                {
                    int patch = pz * _patchesPerSide + px;
                    int level = _levels[patch];
                    int variant = 0;

                    if (px > 0 && _levels[patch - 1] > level) variant |= LEFTCOARSER;
                    if (px < _patchesPerSide - 1 && _levels[patch + 1] > level) variant |= RIGHTCOARSER;
                    if (pz > 0 && _levels[patch - _patchesPerSide] > level) variant |= BOTTOMCOARSER;
                    if (pz < _patchesPerSide - 1 && _levels[patch + _patchesPerSide] > level) variant |= TOPCOARSER;

                    _variants[patch] = variant;
                }
            }
        }

        /// <summary>
        /// Draws every patch the camera can see, at its level, in one draw call.
        /// The terrain shader must already be bound.
        /// </summary>
        /// <param name="viewProjection">This pass's view * projection (for culling).</param>
        /// <param name="areaOnly">true to draw only the patches over the area set by SetArea.</param>
        public void Draw(Matrix4 viewProjection, bool areaOnly)
        {
            _frustum.Update(viewProjection);

            int drawCount = 0;
            int triangles = 0;
            for (int pz = 0; pz < _patchesPerSide; pz++)
            {
                for (int px = 0; px < _patchesPerSide; px++)
                {
                    int patch = pz * _patchesPerSide + px;
                    Vector3 min = _boundsMin[patch];
                    Vector3 max = _boundsMax[patch];

                    if (areaOnly && (max.X < _areaMin.X || min.X > _areaMax.X || max.Z < _areaMin.Y || min.Z > _areaMax.Y))
                        continue;

                    // Skip patches outside the view (unless LOD is switched off, to measure without it)
                    if (Enabled && !_frustum.IntersectsBox(min, max))
                        continue;

                    int level = _levels[patch];
                    int variant = _variants[patch];
                    _drawCounts[drawCount] = _variantCounts[level, variant];
                    _drawOffsets[drawCount] = (IntPtr)(_variantOffsets[level, variant] * sizeof(uint));
                    _drawBaseVertices[drawCount] = pz * PATCHQUADS * _gridSize + px * PATCHQUADS;
                    triangles += _drawCounts[drawCount] / TRIANGLEVERTICES;
                    drawCount++;
                }
            }

            PatchesDrawn = drawCount;
            TrianglesDrawn = triangles;
            if (drawCount == 0) return;

            // Every visible patch in one call: each gets its own index range and base vertex
            _vao.Bind();
            GL.MultiDrawElementsBaseVertex(PrimitiveType.Triangles, _drawCounts, DrawElementsType.UnsignedInt,
                _drawOffsets, drawCount, _drawBaseVertices);
            _vao.Unbind();
        }

        /// <summary>
        /// Frees the index buffer.
        /// </summary>
        public void Delete()
        {
            _vao.Delete();
            _ibo.Delete();
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Builds every level's 16 edge variants into one list, recording where each starts and how long it is.
        ///
        /// A level with step s is made of blocks 2s wide. Each block is a fan of triangles around its centre,
        /// through its 4 corners and 4 edge midpoints. Where the neighbouring patch is one level coarser,
        /// its edge only has every other vertex, so blocks along that edge skip their midpoint there.
        /// That leaves one triangle instead of two on that side, and the edges line up with no cracks.
        /// </summary>
        private List<uint> BuildIndices()
        {
            var indices = new List<uint>();

            // Around the centre, counter-clockwise seen from above (from +Z towards +X): top-left corner, top
            // midpoint, top-right corner, right midpoint, and so on. Every 2nd point (odd entries) is a midpoint.
            (int X, int Z)[] ring = [(-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0)];
            var kept = new List<(int X, int Z)>(ring.Length);

            for (int level = 0; level < LODCOUNT; level++)
            {
                int step = 1 << level;
                int block = step * 2;

                for (int variant = 0; variant < VARIANTCOUNT; variant++)
                {
                    _variantOffsets[level, variant] = indices.Count;

                    for (int bz = 0; bz < PATCHQUADS; bz += block)
                    {
                        for (int bx = 0; bx < PATCHQUADS; bx += block)
                        {
                            int cx = bx + step;
                            int cz = bz + step;

                            // Which of this block's edges are on a patch edge next to a coarser neighbour
                            bool skipTop = (variant & TOPCOARSER) != 0 && bz + block == PATCHQUADS;
                            bool skipRight = (variant & RIGHTCOARSER) != 0 && bx + block == PATCHQUADS;
                            bool skipBottom = (variant & BOTTOMCOARSER) != 0 && bz == 0;
                            bool skipLeft = (variant & LEFTCOARSER) != 0 && bx == 0;

                            kept.Clear();
                            foreach (var point in ring)
                            {
                                if (point == (0, 1) && skipTop) continue;
                                if (point == (1, 0) && skipRight) continue;
                                if (point == (0, -1) && skipBottom) continue;
                                if (point == (-1, 0) && skipLeft) continue;
                                kept.Add(point);
                            }

                            // A fan: the centre with each pair of neighbouring ring points (wrapping round)
                            for (int i = 0; i < kept.Count; i++)
                            {
                                var a = kept[i];
                                var b = kept[(i + 1) % kept.Count];
                                indices.Add(PatchIndex(cx, cz));
                                indices.Add(PatchIndex(cx + a.X * step, cz + a.Z * step));
                                indices.Add(PatchIndex(cx + b.X * step, cz + b.Z * step));
                            }
                        }
                    }

                    _variantCounts[level, variant] = indices.Count - _variantOffsets[level, variant];
                }
            }

            return indices;
        }

        /// <summary>
        /// A vertex's number counted from its patch's corner. The draw adds the patch's base vertex,
        /// giving z * gridSize + x, which is what the vertex shader expects.
        /// </summary>
        private uint PatchIndex(int x, int z) => (uint)(z * _gridSize + x);

        /// <summary>
        /// The level for a distance: 0 up to LODBASEDISTANCE, then one more each time the distance doubles.
        /// </summary>
        private static int DistanceToLevel(float distance)
        {
            int level = 0;
            float limit = LODBASEDISTANCE;
            while (level < LODCOUNT - 1 && distance >= limit)
            {
                level++;
                limit *= 2f;
            }
            return level;
        }

        /// <summary>
        /// Distance from the camera to the nearest point of a patch, across the ground (height ignored).
        /// Across the ground, neighbouring patches' distances differ by at most a patch width, which keeps them
        /// within one level of each other.
        /// </summary>
        private float HorizontalDistance(Vector3 cameraPosition, int patch)
        {
            float dx = MathF.Max(0f, MathF.Max(_boundsMin[patch].X - cameraPosition.X, cameraPosition.X - _boundsMax[patch].X));
            float dz = MathF.Max(0f, MathF.Max(_boundsMin[patch].Z - cameraPosition.Z, cameraPosition.Z - _boundsMax[patch].Z));
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// A grid column or row to a world x or z.
        /// </summary>
        private float GridToWorld(int grid) => grid * _spacing - _halfSize;
        #endregion
    }
}
