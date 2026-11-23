using GPR202_A3.Graphics;
using OpenTK.Mathematics;

namespace GPR202_A3.Collision
{
    /// <summary>
    /// A model's triangles, kept on the CPU for collision ("complex as simple" in Unreal: the visible mesh is the collider).
    /// Stored once per model in its own units; each placed copy moves the line being tested into these units instead.
    /// </summary>
    internal class CollisionMesh
    {
        #region Constants
        // Below this, a line counts as parallel to a triangle (so it can't cross it)
        private const float PARALLELEPSILON = 1e-7f;
        #endregion

        #region Public Fields
        /// <summary>
        /// The box around the triangles, in the model's own units.
        /// </summary>
        public Bounds Bounds { get; }

        /// <summary>
        /// How many triangles there are.
        /// </summary>
        public int TriangleCount => _triangles.Count;
        #endregion

        #region Private Fields
        // Each triangle as one corner and the two edges from it (worked out once, since the test uses edges)
        private readonly List<(Vector3 Corner, Vector3 EdgeOne, Vector3 EdgeTwo)> _triangles = new();
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that copies the triangles out of some meshes.
        /// </summary>
        /// <param name="meshes">The meshes to collide with (e.g. the palm's trunk and crown, but not its leaves).</param>
        public CollisionMesh(IEnumerable<MeshData> meshes)
        {
            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);

            foreach (MeshData mesh in meshes)
            {
                for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
                {
                    Vector3 a = mesh.Positions[(int)mesh.Indices[i]];
                    Vector3 b = mesh.Positions[(int)mesh.Indices[i + 1]];
                    Vector3 c = mesh.Positions[(int)mesh.Indices[i + 2]];
                    _triangles.Add((a, b - a, c - a));

                    min = Vector3.ComponentMin(min, Vector3.ComponentMin(a, Vector3.ComponentMin(b, c)));
                    max = Vector3.ComponentMax(max, Vector3.ComponentMax(a, Vector3.ComponentMax(b, c)));
                }
            }

            Bounds = new Bounds(min, max);
        }
        #endregion

        #region Public API
        /// <summary>
        /// Whether the line from start to end goes through any triangle (either side).
        /// </summary>
        /// <param name="start">Where the line starts, in the model's units.</param>
        /// <param name="end">Where the line ends, in the model's units.</param>
        /// <param name="hitFraction">How far along the line the first hit is (0 = start, 1 = end).</param>
        /// <returns>Whether the line hits a triangle.</returns>
        public bool SegmentIntersects(Vector3 start, Vector3 end, out float hitFraction)
        {
            hitFraction = float.MaxValue;

            // Most lines don't come near the mesh, so the box rules them out before any triangles
            if (!Bounds.SegmentIntersects(start, end))
                return false;

            Vector3 direction = end - start;

            foreach (var (corner, edgeOne, edgeTwo) in _triangles)
            {
                if (SegmentHitsTriangle(start, direction, corner, edgeOne, edgeTwo, out float fraction) && fraction < hitFraction)
                    hitFraction = fraction;
            }

            return hitFraction <= 1f;
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// The Möller–Trumbore test: solves for where the line meets the triangle's plane, as a fraction along the line
        /// and as two barycentric coordinates (u, v). It's a hit if the point is on the line and inside the triangle.
        /// </summary>
        private static bool SegmentHitsTriangle(Vector3 start, Vector3 direction, Vector3 corner, Vector3 edgeOne,
            Vector3 edgeTwo, out float fraction)
        {
            fraction = 0f;

            Vector3 p = Vector3.Cross(direction, edgeTwo);
            float determinant = Vector3.Dot(edgeOne, p);

            // The line runs along the triangle's plane
            if (MathF.Abs(determinant) < PARALLELEPSILON)
                return false;

            float inverse = 1f / determinant;
            Vector3 fromA = start - corner;

            float u = Vector3.Dot(fromA, p) * inverse;
            if (u < 0f || u > 1f)
                return false;

            Vector3 q = Vector3.Cross(fromA, edgeOne);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f)
                return false;

            fraction = Vector3.Dot(edgeTwo, q) * inverse;
            return fraction >= 0f && fraction <= 1f;
        }
        #endregion
    }
}
