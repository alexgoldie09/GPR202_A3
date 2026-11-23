using OpenTK.Mathematics;

namespace GPR202_A3.Collision
{
    /// <summary>
    /// An axis-aligned bounding box (AABB): the smallest box, lined up with the world axes, that fits around something.
    /// Ported from CustomBounds in my ShootEmTanks3D collision engine, with the slab test from its SegmentIntersectsAABB.
    /// Here the boxes are the quick checks in front of the real triangles.
    /// </summary>
    internal readonly struct Bounds
    {
        #region Constants
        // Below this, a line counts as parallel to a pair of faces (so it can't cross them)
        private const float PARALLELEPSILON = 1e-6f;
        #endregion

        #region Public Fields
        /// <summary>
        /// The corner with the smallest x, y and z.
        /// </summary>
        public Vector3 Min { get; }

        /// <summary>
        /// The corner with the largest x, y and z.
        /// </summary>
        public Vector3 Max { get; }

        /// <summary>
        /// The middle of the box.
        /// </summary>
        public Vector3 Centre => (Min + Max) / 2f;

        /// <summary>
        /// The box's width, height and depth.
        /// </summary>
        public Vector3 Size => Max - Min;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that takes the two opposite corners.
        /// </summary>
        public Bounds(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }
        #endregion

        #region Public API
        /// <summary>
        /// Moves, rotates and scales the box, then fits a new axis-aligned box around its eight corners.
        /// A rotated box gets a bit bigger, but that's fine for this game.
        /// </summary>
        public Bounds Transform(Matrix4 matrix)
        {
            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);

            for (int corner = 0; corner < 8; corner++)
            {
                // Each bit of the corner number picks min or max on one axis
                Vector3 local = new(
                    (corner & 1) == 0 ? Min.X : Max.X,
                    (corner & 2) == 0 ? Min.Y : Max.Y,
                    (corner & 4) == 0 ? Min.Z : Max.Z);

                Vector3 world = Vector3.TransformPosition(local, matrix);
                min = Vector3.ComponentMin(min, world);
                max = Vector3.ComponentMax(max, world);
            }

            return new Bounds(min, max);
        }

        /// <summary>
        /// Whether a point is inside the box (or on its surface).
        /// </summary>
        public bool Contains(Vector3 point) =>
            point.X >= Min.X && point.X <= Max.X &&
            point.Y >= Min.Y && point.Y <= Max.Y &&
            point.Z >= Min.Z && point.Z <= Max.Z;

        /// <summary>
        /// Whether two boxes overlap (they have to overlap on all three axes).
        /// </summary>
        public bool Intersects(Bounds other) =>
            Min.X <= other.Max.X && Max.X >= other.Min.X &&
            Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
            Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

        /// <summary>
        /// The point on or in the box closest to a position (the position clamped to the box).
        /// </summary>
        public Vector3 ClosestPoint(Vector3 point) => Vector3.Clamp(point, Min, Max);

        /// <summary>
        /// Whether a sphere touches the box: the closest point on the box is within the radius.
        /// </summary>
        public bool IntersectsSphere(Vector3 centre, float radius) =>
            (ClosestPoint(centre) - centre).LengthSquared <= radius * radius;

        /// <summary>
        /// Whether the line from start to end goes through the box (the slab method, from SegmentIntersectsAABB).
        /// Each axis gives the part of the line between that axis' two faces; if those parts all overlap,
        /// the line is inside the box for that stretch.
        /// </summary>
        public bool SegmentIntersects(Vector3 start, Vector3 end)
        {
            Vector3 direction = end - start;

            // How far along the line it enters and leaves the box (0 = start, 1 = end)
            float enter = 0f;
            float exit = 1f;

            for (int axis = 0; axis < 3; axis++)
            {
                float origin = start[axis];
                float step = direction[axis];
                float min = Min[axis];
                float max = Max[axis];

                // Parallel to these two faces: it's either between them the whole way, or never
                if (MathF.Abs(step) < PARALLELEPSILON)
                {
                    if (origin < min || origin > max)
                        return false;
                    continue;
                }

                // Where the line crosses each face, nearest first
                float near = (min - origin) / step;
                float far = (max - origin) / step;
                if (near > far)
                    (near, far) = (far, near);

                enter = MathF.Max(enter, near);
                exit = MathF.Min(exit, far);
                if (enter > exit)
                    return false;
            }

            return true;
        }
        #endregion
    }
}
