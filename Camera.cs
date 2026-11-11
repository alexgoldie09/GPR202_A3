using OpenTK.Mathematics;

namespace GPR202_A3
{
    /// <summary>
    /// A chase camera that eases into place behind and above the plane, looking a little ahead of it.
    /// It also holds the projection settings.
    /// </summary>
    internal class Camera
    {
        #region Constants
        // Follow position: 11 units away, 20 degrees above (the same as the original game's camera)
        private const float FOLLOWDISTANCE = 11f;
        private const float FOLLOWANGLE = 20f; // degrees above the target
        private const float LOOKAHEAD = 4f; // how far ahead of the target to aim
        private const float FOLLOWRESPONSE = 6f; // how quickly the camera catches up (higher = tighter)

        // Projection. The original near plane of 0.1 wasted depth precision and caused flickering in the distance.
        // Nothing gets closer to the camera than about 10 units, so 0.5 loses nothing.
        private const float DEFAULTFOV = 65f; // vertical field of view, degrees (the same as the original)
        private const float NEARPLANE = 0.5f;
        private const float FARPLANE = 2000f;
        #endregion

        #region Public Fields
        /// <summary>
        /// Vertical field of view, in degrees.
        /// </summary>
        public float FOV { get; set; } = DEFAULTFOV;

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

            // Close the same fraction of the gap each second, whatever the frame rate
            float blend = 1f - MathF.Exp(-FOLLOWRESPONSE * deltaTime);
            Position = Vector3.Lerp(Position, desiredPosition, blend);
            Target = Vector3.Lerp(Target, desiredTarget, blend);
        }

        /// <summary>
        /// Moves the camera straight into place, with no easing (for the start of a game).
        /// </summary>
        /// <param name="targetPosition">The target's position.</param>
        /// <param name="targetForward">The direction the target is facing (level).</param>
        public void SnapTo(Vector3 targetPosition, Vector3 targetForward)
        {
            Position = GetFollowPosition(targetPosition, targetForward);
            Target = targetPosition + targetForward * LOOKAHEAD;
        }

        /// <summary>
        /// The view matrix (world to camera).
        /// </summary>
        public Matrix4 GetViewMatrix() => Matrix4.LookAt(Position, Target, Vector3.UnitY);

        /// <summary>
        /// The projection matrix (camera to screen).
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
            // A minimised window has no size; skip it to avoid dividing by zero
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
