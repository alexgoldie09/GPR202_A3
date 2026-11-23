using OpenTK.Mathematics;

namespace GPR202_A3.Collision
{
    /// <summary>
    /// One placed prop's collider: its model's triangles, where this copy sits, and a world box around it.
    /// The box is only the quick "is the plane close?" check; the triangles decide whether it actually hit.
    /// </summary>
    internal class PropCollider
    {
        #region Public Fields
        /// <summary>
        /// A box around this copy, in world space.
        /// </summary>
        public Bounds Bounds { get; }
        #endregion

        #region Private Fields
        // The model's triangles (shared by every copy of the model)
        private readonly CollisionMesh _mesh;

        // This copy's model matrix, and its inverse to move world points into the model's units
        private readonly Matrix4 _toWorld;
        private readonly Matrix4 _toModel;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that places a model's triangles with one copy's matrix.
        /// </summary>
        /// <param name="mesh">The model's triangles.</param>
        /// <param name="instance">This copy's model matrix (from the scatter).</param>
        public PropCollider(CollisionMesh mesh, Matrix4 instance)
        {
            _mesh = mesh;
            _toWorld = instance;
            _toModel = Matrix4.Invert(instance);
            Bounds = mesh.Bounds.Transform(instance);
        }
        #endregion

        #region Public API
        /// <summary>
        /// Moves a world point into this copy's model units (do it once per point, then test as many lines as needed).
        /// </summary>
        public Vector3 ToModel(Vector3 worldPoint) => Vector3.TransformPosition(worldPoint, _toModel);

        /// <summary>
        /// Whether a line (already in model units) hits the prop's triangles.
        /// </summary>
        /// <param name="start">Where the line starts, in model units.</param>
        /// <param name="end">Where the line ends, in model units.</param>
        /// <param name="hitPoint">Where it hit, back in world space.</param>
        /// <returns>Whether the line hits the prop.</returns>
        public bool SegmentIntersects(Vector3 start, Vector3 end, out Vector3 hitPoint)
        {
            hitPoint = Vector3.Zero;
            if (!_mesh.SegmentIntersects(start, end, out float fraction))
                return false;

            hitPoint = Vector3.TransformPosition(start + (end - start) * fraction, _toWorld);
            return true;
        }
        #endregion
    }
}
