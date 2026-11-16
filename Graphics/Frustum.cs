using OpenTK.Mathematics;

namespace GPR202_A3.Graphics
{
    /// <summary>
    /// The six planes around what a camera can see (left, right, bottom, top, near, far).
    /// Used to skip anything completely outside the view (frustum culling).
    /// </summary>
    internal class Frustum
    {
        #region Constants
        private const int PLANECOUNT = 6;
        #endregion

        #region Private Fields
        // Each plane is (normal, distance): a point is on the inside when dot(normal, point) + distance >= 0
        private readonly Vector4[] _planes = new Vector4[PLANECOUNT];
        #endregion

        #region Public API
        /// <summary>
        /// Works out the planes from a camera's view and projection matrices multiplied together
        /// (Gribb and Hartmann's method: each plane is a sum or difference of two columns of the matrix).
        /// </summary>
        /// <param name="viewProjection">view * projection (row-vector order, as everywhere else in the project).</param>
        public void Update(Matrix4 viewProjection)
        {
            // With the vector on the left, clip x = dot(point, column 0), and so on. A point is visible when
            // -w <= x <= w (and the same for y and z), which gives these six planes.
            Vector4 x = viewProjection.Column0;
            Vector4 y = viewProjection.Column1;
            Vector4 z = viewProjection.Column2;
            Vector4 w = viewProjection.Column3;

            _planes[0] = w + x; // left
            _planes[1] = w - x; // right
            _planes[2] = w + y; // bottom
            _planes[3] = w - y; // top
            _planes[4] = w + z; // near
            _planes[5] = w - z; // far
        }

        /// <summary>
        /// Whether any part of a box might be visible. False only when the box is completely outside one plane.
        /// </summary>
        /// <param name="min">The box's lowest corner.</param>
        /// <param name="max">The box's highest corner.</param>
        public bool IntersectsBox(Vector3 min, Vector3 max)
        {
            foreach (Vector4 plane in _planes)
            {
                // The box corner furthest along the plane's normal. If even that is outside, the whole box is.
                var furthest = new Vector3(
                    plane.X >= 0f ? max.X : min.X,
                    plane.Y >= 0f ? max.Y : min.Y,
                    plane.Z >= 0f ? max.Z : min.Z);

                if (plane.X * furthest.X + plane.Y * furthest.Y + plane.Z * furthest.Z + plane.W < 0f)
                    return false;
            }
            return true;
        }
        #endregion
    }
}
