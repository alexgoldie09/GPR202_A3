using OpenTK.Mathematics;

namespace GPR202_A3
{
    /// <summary>
    /// A chase camera that follows behind and above a target (the plane), looking slightly ahead of it.
    ///
    /// The camera eases towards its follow position instead of snapping to it, so turns and climbs feel smooth.
    /// It also owns the projection settings and keeps the aspect ratio in step with the window.
    /// </summary>
    internal class Camera
    {
        #region Constants
        // Where the camera sits relative to the target: behind it along its heading, and above it.
        // A distance of 11 at 20 degrees above matches the original game's camera.
        private const float FOLLOWDISTANCE = 11f;
        private const float FOLLOWANGLE = 20f; // degrees above the target
        private const float LOOKAHEAD = 4f; // how far ahead of the target to aim, in units
        private const float FOLLOWRESPONSE = 6f; // how quickly the camera catches up (higher = tighter)

        // Projection. The near plane was 0.1 in the original: with a far plane of 2000 that wastes most of the
        // depth buffer's precision on the first few units, which causes flickering (z-fighting) in the distance.
        // The camera is never closer than ~10 units to anything, so 0.5 loses nothing.
        private const float NEARPLANE = 0.5f;
        private const float FARPLANE = 2000f;
        #endregion

        #region Public Fields
        /// <summary>
        /// Vertical field of view, in degrees.
        /// </summary>
        public float FOV { get; set; } = 65f;

        /// <summary>
        /// Where the camera is in the world.
        /// </summary>
        public Vector3 Position { get; private set; }

        /// <summary>
        /// The point the camera is looking at.
        /// </summary>
        public Vector3 Target { get; private set; }
        #endregion

        #region Private Fields
        // Screen dimensions for the aspect ratio
        private float _screenWidth;
        private float _screenHeight;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that sets the screen size used for the aspect ratio.
        /// </summary>
        /// <param name="width">The window's width.</param>
        /// <param name="height">The window's height.</param>
        public Camera(float width, float height)
        {
            _screenWidth = width;
            _screenHeight = height;
        }
        #endregion

        #region Public API
        /// <summary>
        /// Eases the camera towards its place behind the target.
        /// </summary>
        /// <param name="targetPosition">The target's position.</param>
        /// <param name="targetForward">The direction the target is facing (level).</param>
        /// <param name="deltaTime">Seconds since the last update.</param>
        public void Follow(Vector3 targetPosition, Vector3 targetForward, float deltaTime)
        {
            Vector3 desiredPosition = GetFollowPosition(targetPosition, targetForward);
            Vector3 desiredTarget = targetPosition + targetForward * LOOKAHEAD;

            // Exponential smoothing: covers the same fraction of the gap per second at any frame rate
            float blend = 1f - MathF.Exp(-FOLLOWRESPONSE * deltaTime);
            Position = Vector3.Lerp(Position, desiredPosition, blend);
            Target = Vector3.Lerp(Target, desiredTarget, blend);
        }

        /// <summary>
        /// Moves the camera straight to its place behind the target, with no easing (used when a game starts).
        /// </summary>
        /// <param name="targetPosition">The target's position.</param>
        /// <param name="targetForward">The direction the target is facing (level).</param>
        public void SnapTo(Vector3 targetPosition, Vector3 targetForward)
        {
            Position = GetFollowPosition(targetPosition, targetForward);
            Target = targetPosition + targetForward * LOOKAHEAD;
        }

        /// <summary>
        /// The view matrix: from world space to the camera's view.
        /// </summary>
        public Matrix4 GetViewMatrix() => Matrix4.LookAt(Position, Target, Vector3.UnitY);

        /// <summary>
        /// The projection matrix: from the camera's view to the screen.
        /// </summary>
        public Matrix4 GetProjectionMatrix() =>
            Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(FOV),
                _screenWidth / _screenHeight,
                NEARPLANE,
                FARPLANE);

        /// <summary>
        /// Updates the aspect ratio. Call from OnResize with the window size.
        /// </summary>
        public void SetScreenSize(float width, float height)
        {
            // Ignore a zero-sized window (e.g. minimised) to avoid dividing by zero
            if (width <= 0 || height <= 0) return;

            _screenWidth = width;
            _screenHeight = height;
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// The point FOLLOWDISTANCE away from the target, behind it and FOLLOWANGLE degrees above.
        /// </summary>
        private static Vector3 GetFollowPosition(Vector3 targetPosition, Vector3 targetForward)
        {
            float angle = MathHelper.DegreesToRadians(FOLLOWANGLE);
            Vector3 behind = -targetForward * MathF.Cos(angle) * FOLLOWDISTANCE;
            Vector3 above = Vector3.UnitY * MathF.Sin(angle) * FOLLOWDISTANCE;
            return targetPosition + behind + above;
        }
        #endregion
    }
}
